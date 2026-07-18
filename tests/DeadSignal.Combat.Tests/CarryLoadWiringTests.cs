using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 🔴 [T45·负重激活] 「**装备真的进了账、debuff 真的作用到了人身上**」——两条断链的护栏。
///
/// <para>═══ <b>这组测试是为一个真断链写的，别删</b> ═══
/// T45 之前，负重系统看起来是完整的：<c>Loadout</c> 有上限、有三档、有移速/攻速乘子，
/// <c>CarryCapacityTests</c> 一路全绿。**但它整个不存在**：
/// <list type="number">
/// <item><b>断链一</b>：<c>ExpeditionBag.CarriedKg</c> 只算 <c>LootItem</c>，而出门那一刻
///       <c>_bag = new ExpeditionBag(...)</c> 是**空包** ⇒ <b>玩家出门时负重恒为 0kg</b>。
///       一把 7.5kg 的满改装步枪、一身 15kg 的板甲，对负重的贡献都是**零**。</item>
/// <item><b>断链二</b>：<c>Loadout.SpeedMultiplier</c> / <c>AttackSpeedMultiplier</c> 全仓
///       **没有任何游戏代码消费**（只有 <c>CarryWeight.cs</c> 自己和单测）——移速是常数
///       <c>Pawn.MoveSpeed = 95f</c> ⇒ <b>负重 debuff 是死代码</b>。</item>
/// </list>
/// 两条都**不报错、不崩溃**，纯逻辑单测还全绿——正是本项目反复踩的
/// 「**纯逻辑绿 ≠ 功能生效**」。用户原话：「我希望重量在改装中是一个重要的因素，玩家可以把一把武器
/// 改装得很强，**但是一出门就进入负重 debuff**」——那句话在断链下**无论把改装重量写多大都不可能成立**。
/// </para>
///
/// <para>所以本组测三层，缺一不可：
/// ① <b>账</b>（装备的重量算得对不对，用真 <c>ArmorTable</c>/<c>ItemWeights</c> 数据）；
/// ② <b>分档</b>（用户要的"普通不 debuff / 满改装就 debuff"到底成不成立）；
/// ③ <b>接线</b>（消费层是不是真的读了那两个乘子）—— ③ 才是防静默失效的那一层。</para>
/// </summary>
public class CarryLoadWiringTests
{
    // ════════════════ ① 装备的账：真数据（ArmorTable / ItemWeights 是单一事实源，本文件不复制数值）════════════════

    /// <summary>护甲重量取引擎 <c>ArmorTable</c> 的 Weight（单一事实源）。</summary>
    private static double A(ArmorLayer layer) => layer.Weight;

    /// <summary>某套装备的总重（kg）——武器名走 <see cref="ItemWeights.WeaponKg"/>，护甲走 <c>ArmorLayer.Weight</c>。</summary>
    private static double Gear(string weapon, params ArmorLayer[] worn)
        => ItemWeights.WeaponKg(weapon) + worn.Sum(A);

    /// <summary>开局幸存者（长袖布衣 + 长裤 + 一双运动鞋）。</summary>
    private static ArmorLayer[] StarterClothes() => new[]
    {
        ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
        ArmorTable.Sneakers(), ArmorTable.Sneakers(), // 一只占一只脚槽 ⇒ 两只
    };

    /// <summary>中期标准：基础衣物 + 皮夹克 + 皮甲 + 军用头盔。</summary>
    private static ArmorLayer[] MidGameArmor() => StarterClothes()
        .Concat(new[] { ArmorTable.LeatherJacket(), ArmorTable.Leather(), ArmorTable.MilitaryHelmet() })
        .ToArray();

    /// <summary>重装：基础衣物 + 皮夹克 + **板甲** + **防暴头盔** + 工作手套×2。</summary>
    private static ArmorLayer[] HeavyArmor() => StarterClothes()
        .Concat(new[]
        {
            ArmorTable.LeatherJacket(), ArmorTable.Plate(), ArmorTable.RiotHelmet(),
            ArmorTable.WorkGloves(), ArmorTable.WorkGloves(),
        })
        .ToArray();

    /// <summary>
    /// 🔴 <b>出门负重 ≠ 0</b> —— 本组最要害的一条。
    /// <para>⚠️ 修复前这条**必红**：装备一克都不进账，一个刚建好的幸存者出门时负重恒为 0.0kg。</para>
    /// <para>清单读的是 <see cref="SurvivorStartingKit"/> —— <c>Pawn.Create</c> **照着同一份清单**穿
    /// （见下方 <see cref="PawnCreate_WearsTheStartingKit_TwoSourcesWelded"/>），两份事实源焊死，不是各抄一遍。</para>
    /// </summary>
    [Fact]
    public void AStartingSurvivor_LeavesCampWithRealWeightOnHim_NotZero()
    {
        double dagger = SurvivorStartingKit.GearKg(StartingWeapon.Dagger);
        double pistol = SurvivorStartingKit.GearKg(StartingWeapon.Pistol);

        Assert.True(dagger > 0, "刚建好的幸存者出门时身上必须有重量——修复前这里是 0kg");

        // 布衣 0.15 + 长裤 0.15 + 鞋 0.25×2 = 0.80kg 衣物
        Assert.Equal(0.80, SurvivorStartingKit.ApparelKg, 6);
        Assert.Equal(0.80 + 0.5, dagger, 6);  // + 匕首
        Assert.Equal(0.80 + 1.0, pistol, 6);  // + 手枪
    }

    /// <summary>成对品（鞋/手套）**逐只计重**：一只 0.25kg 的运动鞋，两只就是 0.5kg（不是"一双 = 一件"）。</summary>
    [Fact]
    public void PairedApparel_WeighsPerPiece_NotPerPair()
    {
        double oneShoe = GearWeight.OfArmor(new[] { ArmorTable.Sneakers() });
        double twoShoes = GearWeight.OfArmor(new[] { ArmorTable.Sneakers(), ArmorTable.Sneakers() });

        Assert.Equal(oneShoe * 2, twoShoes, 6);
        Assert.Equal(0.5, twoShoes, 6);
    }

    /// <summary>双手握一把（重剑）只算**一次**重量——去重在 <c>WeaponLoadout.HeldWeapons</c> 收口，不是两把。</summary>
    [Fact]
    public void TwoHandedWeapon_CountsOnce_NotTwice()
    {
        var loadout = new WeaponLoadout();
        loadout.EquipToHand(WeaponTable.Greatsword(), Hand.Right); // 双手武器 ⇒ 占两手

        Assert.Equal(GripMode.TwoHanded, loadout.Grip);
        Assert.Equal(3.2, GearWeight.OfWeapons(loadout.HeldWeapons), 6); // 重剑 3.2kg（用户手改 3.0→3.2），**不是 6.4**
    }

    /// <summary>双持两把 ⇒ 两把都压秤（各自算各自的）。</summary>
    [Fact]
    public void DualWield_CountsBothWeapons()
    {
        var loadout = new WeaponLoadout();
        loadout.EquipToHand(WeaponTable.Dagger(), Hand.Right);
        loadout.EquipToHand(WeaponTable.Dagger(), Hand.Left);

        Assert.Equal(GripMode.DualWield, loadout.Grip);
        Assert.Equal(1.0, GearWeight.OfWeapons(loadout.HeldWeapons), 6); // 匕首 0.5 × 2
    }

    /// <summary>
    /// 五档真实配置的实重（**这张表就是免罚线 15kg 的标定依据**）。
    /// 数字全部由 <c>ArmorTable</c>/<c>ItemWeights</c> 复算，本文件不写死任何护甲重量。
    /// </summary>
    [Fact]
    public void TheFiveRealLoadouts_WeighWhatTheCalibrationAssumes()
    {
        Assert.Equal(1.30, Gear("匕首", StarterClothes()), 2);       // 开局
        Assert.Equal(17.30, Gear("步枪", MidGameArmor()), 2);        // 中期标准（步枪 4.0→7.5）
        Assert.Equal(29.90, Gear("狙击枪", HeavyArmor()), 2);        // 板甲重装（狙击 6.0→9.0）

        // 满改装（重量走 ItemWeights.WeaponKg；改装件增重由 impl-weaponmod 接进那个函数，见 [HANDOFF]）：
        // 步枪 7.5 → 15.1875（创伤型枪托 +50% × 加长枪管 +35%，**连乘**）
        Assert.Equal(24.9875, ModdedRifleKg + GearWeight.OfArmor(MidGameArmor()), 2);
        // 狙击 9.0 → 18.225
        Assert.Equal(39.125, ModdedSniperKg + GearWeight.OfArmor(HeavyArmor()), 2);
    }

    // ════════════════ ② 🔴 用户的设计意图：装备**吃掉你的搜刮余量** ════════════════
    //
    // ⚠️ 别搞反：负重的代价**不是"出门就减速"**。用户原话：
    //   「**不改啊，就应当是 30/50/80。带装备出门，随便搜点就超 30 了。如果全身重甲+重武器（单板甲就 15 了），
    //     那出门就差不多 30 了，能搜的空间会很小。**」
    // ⇒ 每个配置出门时**都还在免罚线下**（不罚）；装备的代价是**它把你到 30kg 那条线的余量吃掉了**。
    //   穿板甲带重枪 ⇒ 余量只剩 0.1kg（出门就差不多 30）⇒ **搜一根木料就掉档**。这比"出门即罚"好——**它把选择权留给玩家**。

    /// <summary>基准人的上限（80kg，健全饱食无加成）。</summary>
    private static double Limit => CarryCapacity.For(1.0);

    // ———— 满改装武器的实重：**权威在 `WeaponModCatalog`（impl-weaponmod 所有）**，这里是快照 ————
    // 负重的**规则**不依赖这两个数（它们只经 ItemWeights.WeaponKg 进账）；它们变了，只需改这两行 + 下面的余量表。
    // 现值：创伤型枪托 +50% × 加长枪管 +35%，**连乘**（百分比一律乘算，CLAUDE.md 铁律）。

    /// <summary>满改装步枪：7.5 × 1.50 × 1.35 = 15.1875kg（步枪基础重 4.0→7.5 翻倍后）。</summary>
    private const double ModdedRifleKg = 15.1875;

    /// <summary>满改装狙击：9.0 × 1.50 × 1.35 = 18.225kg（狙击基础重 6.0→9.0 后）。</summary>
    private const double ModdedSniperKg = 18.225;

    /// <summary>改装的重量必须是**连乘**出来的，不是加算（加算会得到 7.5 × 1.85 = 13.875kg）。</summary>
    [Fact]
    public void ModWeights_ChainMultiplicatively_NotAdditively()
    {
        Assert.Equal(7.5 * 1.50 * 1.35, ModdedRifleKg, 6);
        Assert.Equal(9.0 * 1.50 * 1.35, ModdedSniperKg, 6);
        Assert.NotEqual(7.5 * (1 + 0.50 + 0.35), ModdedRifleKg, 6); // 防加算回潮
    }

    /// <summary>基准人的免罚线（30kg）。</summary>
    private static double FreeLine => Loadout.FreeThresholdFor(Limit);

    /// <summary>
    /// 🔴🔴 <b>用户设计意图的核心表</b>：出门时**没有一个配置在罚**，但它们的<b>搜刮余量天差地别</b>。
    /// <para>⚠️ 修复前这张表的「余量」列**全是 30.0kg**：装备不进账 ⇒ 一把 7.5kg 的满改装步枪、一身 15kg 的板甲，
    /// 对搜刮余量的影响都是**零**。那正是这单要修的东西。</para>
    /// </summary>
    [Theory]
    [InlineData("开局(匕首+布衣+长裤+鞋)", 1.30, 28.7)]        // 想拿什么拿什么
    [InlineData("中期(步枪7.5+皮甲+军盔)", 17.30, 12.7)]       // 枪翻倍后余量从 16.2 缩到 12.7，但仍不罚
    [InlineData("中期+满改装步枪(15.19)", 24.9875, 5.0)]      // 改装吃掉了 7.69kg 余量 ← **改装重量的全部意义**
    [InlineData("重装(狙击9+板甲+防暴盔)", 29.90, 0.1)]       // **只剩 0.1kg——出门就差不多 30，搜一根木料就掉档**
    public void GearDoesNotSlowYouDownAtTheGate_ItEatsYourLootingRoom(
        string _, double gearKg, double expectFreeRoomKg)
    {
        // 出门：一个都不罚（用户："那出门就差不多 30 了"——差不多，但还没到）
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(gearKg, Limit));
        Assert.Equal(1.0, Loadout.SpeedMultiplier(gearKg, Limit), 9);
        Assert.Equal(1.0, Loadout.AttackSpeedMultiplier(gearKg, Limit), 9);

        // 但余量已经被装备吃掉了——**这才是代价**
        Assert.Equal(expectFreeRoomKg, FreeLine - gearKg, 1);
    }

    /// <summary>
    /// 🔴🔴 <b>用户设计意图的核心断言</b>：「全身重甲+重武器 ⇒ **能搜的空间会很小**」——
    /// 板甲重装出门就<b>贴着免罚线</b>：余量很小，随便搜一点点建材就掉进负重档。
    /// <para>⚠️ [T68] <b>本条改成"按实重推断"而非钉死数字</b>：它同时吃**武器重量**（另有 agent 在同步武器表）
    /// 与**木料重量**（本单减半 2→1）两个都在动的变量 —— 钉死"余量 3.1kg / 搜 N 根"会被任一改动打红，
    /// 且那不是这条测试要守的东西。它守的是**机制**：重甲重武器出门时余量所剩无几，加一点点货就越线。</para>
    /// </summary>
    [Fact]
    public void HeavyArmour_LeavesLittleRoom_ALittleLootTipsYouIntoEncumbered()
    {
        double heavy = Gear("狙击枪", HeavyArmor());
        double log = ItemWeights.MaterialKg("wood");

        // 出门：还没罚（贴着线，但还在线下）
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(heavy, Limit));
        Assert.Equal(1.0, Loadout.SpeedMultiplier(heavy, Limit), 9);

        // 余量很小：重甲重武器几乎把免罚线吃光（用户要的"能搜的空间会很小"）
        double room = FreeLine - heavy;
        Assert.InRange(room, 0.0, 8.0);   // 出门就贴着 30kg 免罚线（具体数随武器/材料重量浮动，机制不变）

        // 🔴 只要多搬进"刚好越过余量"的木料，就掉进负重档——移速与攻速一起掉
        int logsToCross = (int)System.Math.Floor(room / log) + 1;
        double crossed = heavy + log * logsToCross;
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(crossed, Limit));
        Assert.True(Loadout.SpeedMultiplier(crossed, Limit) < 1.0, "搜一点建材就掉进负重档——这就是穿板甲背重枪的代价");
        Assert.True(Loadout.AttackSpeedMultiplier(crossed, Limit) < 1.0);
    }

    /// <summary>
    /// 🔴 <b>满改装步枪的代价：吃掉 7.69kg 搜刮余量</b>（不是"出门就减速"——它出门仍然一分不罚）。
    /// <para>[carryweight2] 步枪基础重翻倍(4.0→7.5)后，改装增重 = 7.5×1.5×1.35 − 7.5 = 7.6875kg，
    /// 原样从搜刮余量里扣走——比原厂多背这些，就得少搬这些货回家。</para>
    /// </summary>
    [Fact]
    public void AFullyModdedRifle_CostsYouLootingRoom_NotSpeedAtTheGate()
    {
        double stock = Gear("步枪", MidGameArmor());                       // 17.30kg 原厂（步枪 7.5）
        double modded = ModdedRifleKg + GearWeight.OfArmor(MidGameArmor()); // 24.9875kg 满改装

        // 两个出门都不罚——改装**不是**"出门即 debuff"（满改装 24.99kg 仍在 30kg 免罚线下）
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(stock, Limit));
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(modded, Limit));
        Assert.Equal(1.0, Loadout.SpeedMultiplier(modded, Limit), 9);

        // 代价在余量：满改装比原厂少 7.69kg 的搜刮空间（= 那把枪多出来的重量，一克不差）
        double stockRoom = FreeLine - stock;    // 12.7kg
        double moddedRoom = FreeLine - modded;  // 5.0125kg
        Assert.Equal(12.7, stockRoom, 1);
        Assert.Equal(5.0, moddedRoom, 1);
        Assert.Equal(ModdedRifleKg - 7.5, stockRoom - moddedRoom, 6); // 改装增重**原样**变成搜刮损失，一克不差
    }

    /// <summary>
    /// 🔴 <b>枪械翻倍后：连原厂中期步枪都搬不空最大点位了</b>（住宅区 66kg，硬余量只剩 62.7kg，留 3.3kg 在原地）；
    /// 换上满改装步枪，硬余量掉到 55.0kg ⇒ <b>留 11.0kg 在原地，改装把差距又拉大了 7.69kg</b>。
    /// <para>[carryweight2] 旧口径"原厂步枪刚好搬得空"随枪重翻倍作废——重武器出门余量更小正是本轮意图。
    /// 保留的机制：改装增重原样变成额外留货（modded 留的 − stock 留的 = 那把枪多出的重量）。
    /// 「你可以把枪改装得很强，但你得接受多空手而归」——用户要的取舍，这就是它的数字形态。</para>
    /// </summary>
    [Fact]
    public void AHeavyRifle_LeavesLootBehind_AndModdingLeavesEvenMore()
    {
        const double biggestSiteKg = 66.0; // 住宅区全部搜完
        double stockRoom = Limit - Gear("步枪", MidGameArmor());                            // 62.7kg
        double moddedRoom = Limit - (ModdedRifleKg + GearWeight.OfArmor(MidGameArmor()));   // 55.0125kg

        double stockLeftBehind = biggestSiteKg - stockRoom;    // 3.3kg
        double moddedLeftBehind = biggestSiteKg - moddedRoom;  // 10.9875kg

        Assert.True(stockLeftBehind > 0, "枪械翻倍后，原厂中期步枪也搬不空最大点位了");
        Assert.True(moddedLeftBehind > stockLeftBehind, "满改装留下的货更多");
        Assert.Equal(3.3, stockLeftBehind, 1);
        // 改装增重原样变成额外留货：多留的 = 那把枪多出来的重量，一克不差
        Assert.Equal(ModdedRifleKg - 7.5, moddedLeftBehind - stockLeftBehind, 6);
    }

    /// <summary>轻装（开局）：一大车战利品也还在无罚线内——「轻装出门跑得快」这条不能被装备账毁掉。</summary>
    [Fact]
    public void TravellingLight_StillGetsAFreeHaul()
    {
        double gear = Gear("匕首", StarterClothes()); // 1.30kg

        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(gear, Limit));
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(gear + 28.0, Limit)); // 还能白拿 28kg
        Assert.Equal(1.0, Loadout.SpeedMultiplier(gear + 28.0, Limit), 9);
        Assert.Equal(28.7, FreeLine - gear, 1);
    }

    /// <summary>贪多战利品：任何配置背满都要付代价（用户："随便搜点就超 30 了"）。</summary>
    [Fact]
    public void GreedForLoot_CostsSpeed_WhateverYouWear()
    {
        double gear = Gear("匕首", StarterClothes()); // 最轻的配置也逃不掉

        Assert.Equal(LoadoutTier.Strained, Loadout.TierOf(gear + 60.0, Limit));
        Assert.True(Loadout.SpeedMultiplier(gear + 60.0, Limit) < Loadout.SpeedAtStrain);
        Assert.True(Loadout.AttackSpeedMultiplier(gear + 60.0, Limit) < Loadout.AttackSpeedAtStrain);
    }

    // ════════════════ ③ 逐人分档：你的枪，你自己背 ════════════════

    /// <summary>
    /// 🔴 <b>负重是逐人的，不是全队摊薄的</b>：两人同队各搜 10kg 货，
    /// 穿板甲的那个（29.9kg 装备）**当场掉档变慢**，轻装的队友（1.3kg）**一点不受连累**。
    /// <para>若按全队总账分档，板甲那 25kg 会被队友摊薄成"全队平均"，
    /// 「穿板甲的人自己走得慢」这条代价就凭空消失了。</para>
    /// </summary>
    [Fact]
    public void TheGuyWithThePlateArmorIsTheOneWhoWalksSlow_NotHisTeammates()
    {
        double tank = Gear("狙击枪", HeavyArmor());   // 29.90kg —— 余量只剩 0.1kg
        double scout = Gear("匕首", StarterClothes()); // 1.30kg  —— 余量 28.7kg
        double total = Limit * 2;
        const double loot = 20.0; // 全队搜了 20kg，两人按运力平摊各 10kg

        MemberLoad tankLoad = ExpeditionLoad.For(tank, loot, dogCapacityKg: 0, Limit, total);
        MemberLoad scoutLoad = ExpeditionLoad.For(scout, loot, dogCapacityKg: 0, Limit, total);

        Assert.Equal(10.0, tankLoad.LootShareKg, 6);  // 平摊：各 10kg
        Assert.Equal(10.0, scoutLoad.LootShareKg, 6);

        // 板甲那位：29.9 + 10 = 39.9kg ⇒ 越线，走得慢、打得也慢
        Assert.Equal(LoadoutTier.Encumbered, tankLoad.Tier);
        Assert.True(tankLoad.SpeedMultiplier < 1.0);
        Assert.True(tankLoad.AttackSpeedMultiplier < 1.0);

        // 轻装队友：1.3 + 10 = 11.3kg ⇒ 一点不受连累
        Assert.Equal(LoadoutTier.Unencumbered, scoutLoad.Tier);
        Assert.Equal(1.0, scoutLoad.SpeedMultiplier, 9);
        Assert.Equal(1.0, scoutLoad.AttackSpeedMultiplier, 9);
    }

    /// <summary>战利品按**运力占比**摊：背得动的人多背（山姆的 ×1.15 让他多分到货，也确实扛得住）。</summary>
    [Fact]
    public void LootIsSharedInProportionToWhoCanCarryIt()
    {
        double normal = CarryCapacity.For(1.0);                                                  // 40
        double sam = CarryCapacity.For(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true));    // 46
        double total = normal + sam;

        MemberLoad n = ExpeditionLoad.For(0, lootKg: 20, dogCapacityKg: 0, normal, total);
        MemberLoad s = ExpeditionLoad.For(0, lootKg: 20, dogCapacityKg: 0, sam, total);

        Assert.Equal(20.0, n.LootShareKg + s.LootShareKg, 6); // 一克不多、一克不少
        Assert.True(s.LootShareKg > n.LootShareKg, "扛得动的人多背");
        Assert.Equal(20.0 * (sam / total), s.LootShareKg, 6);
    }

    /// <summary>布鲁斯的口袋狗衣（8kg）**真的把 8kg 从人的肩膀上卸下来**——狗先驮满，剩下的才摊给人。</summary>
    [Fact]
    public void ThePocketVest_TakesEightKilosOffHumanShoulders()
    {
        Assert.Equal(12.0, ExpeditionLoad.LootOnHumans(lootKg: 20, dogCapacityKg: 8), 6);
        Assert.Equal(0.0, ExpeditionLoad.LootOnHumans(lootKg: 6, dogCapacityKg: 8), 6);  // 狗全驮走了
        Assert.Equal(20.0, ExpeditionLoad.LootOnHumans(lootKg: 20, dogCapacityKg: 0), 6); // 没带狗
    }

    /// <summary>全队都断手了（总运力 0）⇒ 分摊不下去，返 0 而不是除零炸掉。</summary>
    [Fact]
    public void ZeroPartyCapacity_DoesNotDivideByZero()
    {
        MemberLoad load = ExpeditionLoad.For(5, lootKg: 10, dogCapacityKg: 0, memberLimitKg: 0, totalMemberLimitKg: 0);
        Assert.Equal(0.0, load.LootShareKg, 6);
        Assert.Equal(5.0, load.CarriedKg, 6);
    }

    /// <summary>没有负重账（营地 / 不在探索队）= 全 1.0，零回归。</summary>
    [Fact]
    public void NoLoad_MeansNoPenalty()
    {
        Assert.Equal(1.0, MemberLoad.None.SpeedMultiplier, 9);
        Assert.Equal(1.0, MemberLoad.None.AttackSpeedMultiplier, 9);
        Assert.Equal(LoadoutTier.Unencumbered, MemberLoad.None.Tier);
    }

    // ════════════════ ④ 背包：装备压在同一本账上 ════════════════

    /// <summary>
    /// 🔴 装备<b>同时吃掉搬运余量</b>——穿板甲出门 = 又慢、又背不回东西。这是一本账，不是两本。
    /// <para>⚠️ 修复前这条**必红**：<c>CarriedKg</c> 只算战利品，装备再重也不占一格余量。</para>
    /// </summary>
    [Fact]
    public void GearEatsIntoTheHaul_ItIsOneAccountNotTwo()
    {
        // ⚠️ [T68] 按实重推断（武器重量另有 agent 在同步、木料重量本单减半，都在动）——
        //    这条守的是"装备与战利品同占一本账"的机制，不钉死具体 kg。
        var bag = new ExpeditionBag(80.0); // 单人硬上限
        Assert.Equal(80.0, bag.FreeKg, 6);

        double gear = Gear("狙击枪", HeavyArmor());
        bag.SetGear(gear);                          // 装备穿在身上就已经上账
        Assert.Equal(gear, bag.CarriedKg, 2);       // 一件战利品都没捡，账上已经是装备的重量
        Assert.Equal(80.0 - gear, bag.FreeKg, 2);   // 🔴 余量被装备吃掉了（「能搜的空间会很小」）
        Assert.Equal(LoadoutTier.Unencumbered, bag.Tier); // 但出门还不罚

        double log = ItemWeights.MaterialKg("wood");
        bag.AddAsManyAsFit(LootItem.Material("wood", 1000)); // 往死里塞——直到硬上限
        Assert.True(bag.CarriedKg <= 80.0 + 1e-9);           // 装备 + 战利品从不越过 80kg 硬上限
        Assert.True(bag.FreeKg < log, "背到几乎装不下下一根木料");
        Assert.False(bag.TryAdd(LootItem.Material("wood", 1)), "余量被装备吃掉了，塞不下更多");
        Assert.Equal(LoadoutTier.Strained, bag.Tier);        // 背成这样，重度档
    }

    /// <summary>不喂装备 ⇒ 与修复前**逐位一致**（<c>GearKg</c> 默认 0）。既有 <c>ExpeditionBag</c> 行为零回归。</summary>
    [Fact]
    public void WithoutGear_TheBagBehavesExactlyAsBefore()
    {
        var bag = new ExpeditionBag(80.0);
        bag.AddAsManyAsFit(LootItem.Material("wood", 10)); // [T68] 10 × 1kg = 10kg

        Assert.Equal(0.0, bag.GearKg, 9);
        Assert.Equal(10.0, bag.LootKg, 6);
        Assert.Equal(10.0, bag.CarriedKg, 6); // == LootKg，与修复前同一个数
        Assert.Equal(70.0, bag.FreeKg, 6);
    }

    /// <summary>装备重到把余量吃光（把全队塞进板甲）⇒ 一件东西都拿不走，但不会炸（FreeKg 钳到 0）。</summary>
    [Fact]
    public void OverGearedParty_CanCarryNothingHome_ButDoesNotCrash()
    {
        var bag = new ExpeditionBag(30.0);
        bag.SetGear(35.0); // 装备已超过队伍运力（穿太多了）

        Assert.Equal(0.0, bag.FreeKg, 9);
        Assert.True(bag.IsFull);
        Assert.False(bag.TryAdd(LootItem.Material("cloth", 1)));
        Assert.Equal(LoadoutTier.Overloaded, bag.Tier);
    }

    // ════════════════ ⑤ HUD：超重了、慢了多少，必须看得见 ════════════════

    [Fact]
    public void Hud_ShowsNothingWhenThereIsNothingToWarnAbout()
        => Assert.Equal("", CarryCapacity.PenaltyText(29.9, Limit)); // 板甲重装+重枪出门(29.9kg)仍不罚 ⇒ HUD 保持干净

    /// <summary>
    /// 越线后，HUD 要写清**移速和攻速各掉了多少**。
    /// <para>🔴 [用户新曲线] 攻速从 30kg 起就开始掉 ⇒ 轻度档的提示里**也有攻速**了
    /// （旧口径"轻度档只罚移速"已作废）。</para>
    /// </summary>
    [Fact]
    public void Hud_SpellsOutHowMuchSlowerYouAre()
    {
        string encumbered = CarryCapacity.PenaltyText(40.0, Limit); // 轻度档中点：移速 −10%、攻速 −10%
        Assert.Contains("移速 −10%", encumbered);
        Assert.Contains("攻速 −10%", encumbered);

        string strained = CarryCapacity.PenaltyText(65.0, Limit);   // 重度档中点：移速 −50%、攻速 −35%
        Assert.Contains("移速 −50%", strained);
        Assert.Contains("攻速 −35%", strained);

        string full = CarryCapacity.PenaltyText(80.0, Limit);       // 满载：用户拍板的两个数
        Assert.Contains("移速 −80%", full);
        Assert.Contains("攻速 −50%", full);
    }

    /// <summary>HUD 把**装备**和**战利品**分开列——那 26.9kg 是你自己穿上去的，不是捡来的（而且扔不掉）。</summary>
    [Fact]
    public void Hud_SeparatesWhatYouWoreFromWhatYouLooted()
    {
        string line = CarryCapacity.FormatBag(gearKg: 26.9, lootKg: 30.0, capacityKg: 80.0);
        Assert.Contains("装备 26.9", line);
        Assert.Contains("战利品 30.0", line);
        Assert.Contains("56.9 / 80.0 kg", line);
        Assert.Contains("重负", line);
    }

    /// <summary>负重逐人分档 ⇒ HUD 要**点名**最慢的那个人，玩家才知道该给谁减负。</summary>
    [Fact]
    public void Hud_NamesTheGuyWhoIsSlowingEveryoneDown()
    {
        // 板甲重装+重枪 + 分摊到 20kg 货 = 49.9kg ⇒ 轻度档，走得慢
        MemberLoad tank = ExpeditionLoad.For(Gear("狙击枪", HeavyArmor()), lootKg: 20, dogCapacityKg: 0, Limit, Limit);
        Assert.Contains("山姆", CarryCapacity.FormatMember("山姆", tank));
        Assert.Contains("移速 −", CarryCapacity.FormatMember("山姆", tank));

        // 没有惩罚 ⇒ 空串（HUD 整段省略，不刷屏）
        Assert.Equal("", CarryCapacity.FormatMember("诺蒂", MemberLoad.None));
    }

    // ════════════════ ⑥ 🔴 消费层接线自检：防"纯逻辑绿 ≠ 功能生效" ════════════════
    //
    // 上面每一条都是**纯逻辑**——修复前它们全部可以是绿的，而游戏里负重系统**根本不存在**。
    // 真正会静默失效的是**接线**：Actor 有没有读那两个乘子、CampMain 有没有把装备灌进账。
    // 那些代码活在 Godot 类型里（Pawn / Actor / CampMain），进不了单测 ⇒ 只能盯**源码**。
    // 这几条粗，但它们盯的正是那条唯一会无声断掉的链：**谁删了接线，这里立刻红。**

    private static string Source(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relativePath)))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, $"找不到 {relativePath} —— 消费层的事实源不该消失");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    /// <summary>
    /// 🔴 <b>移速常数 95f 必须被负重乘数消费</b>。
    /// <para>⚠️ 修复前这条**必红**：<c>Actor</c> 的移动链是 残缺×饥饿×骨折×战斗减速×家具，**没有负重项** ⇒
    /// <c>Loadout.SpeedMultiplier</c> 算出来的数字没有任何人读它。</para>
    /// </summary>
    [Fact]
    public void Actor_MovementChain_ActuallyMultipliesTheCarryLoad()
    {
        string actor = Source(Path.Combine("godot", "scripts", "Actor.cs"));
        Assert.Contains("mobility *= CarryLoadSpeedMult;", actor);
        Assert.Contains("MoveSpeed * (float)mobility", actor); // 而 mobility 正是喂给 MoveSpeed 的那个数
    }

    /// <summary>🔴 出手间隔必须除以负重攻速乘子（乘子 &lt;1 ⇒ 间隔变长 ⇒ 打得慢）。修复前无此消费点。</summary>
    [Fact]
    public void Actor_AttackInterval_ActuallyDividesByTheCarryLoad()
    {
        string actor = Source(Path.Combine("godot", "scripts", "Actor.cs"));
        Assert.Contains("interval / System.Math.Max(CarryLoadAttackSpeedMult", actor);
    }

    [Fact]
    public void Actor_AttackInterval_ActuallyConsumesAuthoredAttackSpeedProvider()
    {
        string actor = Source(Path.Combine("godot", "scripts", "Actor.cs"));
        Assert.Contains("SetAuthoredAttackSpeedMult", actor);
        Assert.Contains("_authoredAttackSpeedMult is { } authoredAttack", actor);
        Assert.Contains("interval /= System.Math.Max(authoredAttackSpeed", actor);
    }

    [Fact]
    public void Actor_ReceiveAttack_ConsumesConcussionAndLargeBleedProviders()
    {
        string actor = Source(Path.Combine("godot", "scripts", "Actor.cs"));
        Assert.Contains("SetConcussionChanceMultiplier", actor);
        Assert.Contains("_concussionChanceMultProvider", actor);
        Assert.Contains("concussionMult()", actor);
        Assert.Contains("SetLargeBleedDowngradeProvider", actor);
        Assert.Contains("_downgradeLargeBleedProvider", actor);
        Assert.Contains("Body.DowngradeLargeBleed(hit.PartName)", actor);
    }

    [Fact]
    public void Actor_FractureChains_ConsumeSamPenaltyReductionProvider()
    {
        string actor = Source(Path.Combine("godot", "scripts", "Actor.cs"));
        Assert.Contains("SetFracturePenaltyReductionProvider", actor);
        Assert.Contains("_fracturePenaltyReductionProvider", actor);
        Assert.Contains("ApplyFracturePenaltyReduction", actor);
        Assert.Contains("LowerLimbFractureMobilityFactor", actor);
        Assert.Contains("UpperLimbFractureOperationFactor", actor);
    }

    /// <summary>🔴 <c>Pawn.SetCarryLoad</c> 必须把两个乘子落到 Actor 的那两个字段上（否则灌了也白灌）。</summary>
    [Fact]
    public void Pawn_SetCarryLoad_LandsBothMultipliersOnTheActor()
    {
        string pawn = Source(Path.Combine("godot", "scripts", "Pawn.cs"));
        Assert.Contains("CarryLoadSpeedMult = load.SpeedMultiplier;", pawn);
        Assert.Contains("CarryLoadAttackSpeedMult = load.AttackSpeedMultiplier;", pawn);
    }

    /// <summary>
    /// 🔴 <b>出门那一刻必须刷负重账</b>——这是断链一的修复点。
    /// <para>⚠️ 修复前 <c>LoadExplorationLevel</c> 里只有 <c>_bag = new ExpeditionBag(...)</c>（空包），
    /// 之后**再没有任何人往里放装备**。</para>
    /// </summary>
    [Fact]
    public void CampMain_TheMomentYouWalkOut_TheGearGoesOnTheBooks()
    {
        string camp = Source(Path.Combine("godot", "scripts", "CampMain.cs"));

        // 出门：新建背包的**下一步**就是刷账
        int newBag = camp.IndexOf("_bag = new ExpeditionBag(PartyCarryLimit());", System.StringComparison.Ordinal);
        Assert.True(newBag > 0, "找不到出门新建背包那一行");
        int sync = camp.IndexOf("SyncExpeditionLoad();", newBag, System.StringComparison.Ordinal);
        Assert.True(sync > newBag && sync - newBag < 400,
            "出门新建背包之后必须紧接着 SyncExpeditionLoad()——否则背包是空的，装备一克都不进账");

        // 刷账那个方法必须同时做两件事：装备进账 + 逐人 debuff 落到 Pawn
        Assert.Contains("_bag.SetGear(ExpeditionLoad.PartyGearKg(", camp);
        Assert.Contains("m.SetCarryLoad(ExpeditionLoad.For(", camp);
    }

    /// <summary>
    /// 🔴 <b>两份事实源焊死</b>：<c>Pawn.Create</c> 必须照 <see cref="SurvivorStartingKit"/> 发衣服。
    /// 否则本文件里"开局幸存者 1.30kg"那条断言就是**自说自话**（测试算的是清单 A，游戏穿的是清单 B）。
    /// </summary>
    [Fact]
    public void PawnCreate_WearsTheStartingKit_TwoSourcesWelded()
    {
        string pawn = Source(Path.Combine("godot", "scripts", "Pawn.cs"));
        Assert.Contains("SurvivorStartingKit.Apparel", pawn);
        Assert.Contains("p.EquipApparel(item, slot: slot);", pawn);
    }

    /// <summary>
    /// 🔴 <b>负重不进 Sim 结算路径</b>（既有基线零漂移的结构性证明）：
    /// 引擎的战斗结算与仿真（<c>CombatResolver</c>/<c>Duel</c>/<c>Ballistics</c>/<c>WeaponDps</c>）
    /// **一个字都没提 <c>Loadout</c>** ⇒ 改它的三条线不可能移动任何既有 Sim 输出。
    /// </summary>
    [Theory]
    [InlineData("CombatResolver.cs")]
    [InlineData("Duel.cs")]
    [InlineData("Ballistics.cs")]
    [InlineData("WeaponDps.cs")]
    public void CarryWeight_NeverEntersTheSimSettlementPath(string engineFile)
    {
        string src = Source(Path.Combine("src", "DeadSignal.Combat", engineFile));
        Assert.DoesNotContain("Loadout.", src);
    }
}
