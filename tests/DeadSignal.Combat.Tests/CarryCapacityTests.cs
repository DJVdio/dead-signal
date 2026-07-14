using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 负重上限系统（用户口径：&lt;30kg 无影响 / 30~50kg debuff / 50~80kg debuff 加重 / **不能超过 80kg**）。
/// 本文件覆盖上限算式、物品称重、远征背包硬上限、队伍运力（人+狗）。三档 debuff 曲线本身见 <c>LoadoutTests</c>；
/// [T45] 装备进账、逐人分档、消费层接线、**搜刮余量表**见 <c>CarryLoadWiringTests</c>。
/// 铁律核对：**不存在"力量"属性** —— 80kg 基数全员相同，个体差异只来自 authored 专属效果（山姆）与身体状态（残缺/饥饿）。
/// </summary>
public class CarryCapacityTests
{
    // ---------- 上限模型 ----------

    [Fact]
    public void Limit_BaseIsUniformForEveryone_80kg()
    {
        // 无残缺无饥饿无专属效果 = 全员同一个 80kg（这就是"无属性系统"的形态）
        // [T45] 这本账现在**含装备**（连人带甲带枪），但 80kg 这个数**用户拍板不动**——
        // 装备的代价体现在"搜刮余量被吃掉"，不是把上限调小。
        Assert.Equal(80.0, Loadout.BaseCarryLimitKg, 6);
        Assert.Equal(80.0, CarryCapacity.For(1.0), 6);
    }

    [Fact]
    public void Limit_ScalesWithCarryCapability_OneHandLost()
    {
        // 断一只手 = OperationPenalty 0.5 → 承载能力 0.5 → 上限对折（连同三档阈值一起收紧）
        double capability = HungerState.CombineCapability(disabilityPenalty: 0.5, hungerPenalty: 0.0);
        Assert.Equal(40.0, CarryCapacity.For(capability), 6);
    }

    [Fact]
    public void Limit_ScalesWithHunger()
    {
        // 饥饿(3) = 0.10 惩罚 → 80 × 0.9 = 72kg
        double capability = HungerState.CombineCapability(0.0, HungerState.PenaltyFor((int)HungerLevel.Hungry));
        Assert.Equal(72.0, CarryCapacity.For(capability), 6);
    }

    [Fact]
    public void Limit_DisabilityAndHunger_CompoundMultiplicatively()
    {
        // 断一手 + 饥饿 → (1-0.5) × (1-0.10) = 0.45（乘算通则，不是加算的 1-0.6 = 0.4）
        double capability = HungerState.CombineCapability(0.5, 0.10);
        Assert.Equal(80.0 * 0.45, CarryCapacity.For(capability), 6);
        Assert.NotEqual(80.0 * 0.40, CarryCapacity.For(capability), 6);
    }

    [Fact]
    public void Limit_BothHandsLost_CarriesNothing()
    {
        double capability = HungerState.CombineCapability(disabilityPenalty: 1.0, hungerPenalty: 0.0);
        Assert.Equal(0.0, CarryCapacity.For(capability), 6);
    }

    [Fact]
    public void Limit_ClampsOutOfRangeInputs()
    {
        Assert.Equal(80.0, CarryCapacity.For(1.5), 6);       // 能力上钳到 1
        Assert.Equal(0.0, CarryCapacity.For(-0.2), 6);       // 能力下钳到 0
        Assert.Equal(0.0, CarryCapacity.For(1.0, -1.0), 6);  // 负乘子钳到 0
    }

    // ---------- 山姆 authored 专属效果（乘算通则）----------

    [Fact]
    public void Limit_SamLevel2_Plus15Percent_92kg()
    {
        double mult = SamPerk.CarryCapacityMultiplier(2, isSam: true);
        Assert.Equal(92.0, CarryCapacity.For(1.0, mult), 6); // 80 × 1.15
    }

    [Fact]
    public void Limit_SamLevel3_SamHimself_ChainsMultiplicatively_Not_Additively()
    {
        // 用户拍板的乘算通则：山姆自己 = 二级 ×1.15 × 三级全营 ×1.03 **连乘**，不是加算的 ×1.18
        double mult = SamPerk.CarryCapacityMultiplier(3, isSam: true);
        double limit = CarryCapacity.For(1.0, mult);

        Assert.Equal(80.0 * 1.15 * 1.03, limit, 6);
        Assert.Equal(94.76, limit, 2);
        Assert.NotEqual(80.0 * 1.18, limit, 6); // 防加算回潮（94.4 ≠ 94.76）
    }

    [Fact]
    public void Limit_SamLevel3_OtherSurvivors_OnlyGetCampAura()
    {
        double mult = SamPerk.CarryCapacityMultiplier(3, isSam: false);
        Assert.Equal(82.4, CarryCapacity.For(1.0, mult), 6); // 80 × 1.03
    }

    [Fact]
    public void Limit_SamMissingTwoFingers_PerkOffsetsDisability()
    {
        // 山姆开局缺两指 = -14% 操作能力；他的二级专属效果 ×1.15 几乎把残缺补了回来
        double capability = HungerState.CombineCapability(disabilityPenalty: 0.14, hungerPenalty: 0.0);
        double mult = SamPerk.CarryCapacityMultiplier(2, isSam: true);
        Assert.Equal(80.0 * 0.86 * 1.15, CarryCapacity.For(capability, mult), 6); // 79.12kg
    }

    // ---------- 物品重量表 ----------

    [Fact]
    public void ItemWeight_Food_PerPortion()
    {
        Assert.Equal(ItemWeights.FoodPerPortionKg * 3, ItemWeights.Of(Item.Food(3, "罐头")), 6);
    }

    [Fact]
    public void ItemWeight_Material_StacksByQuantity()
    {
        double oneWood = ItemWeights.MaterialKg("wood");
        Assert.True(oneWood > 0);
        Assert.Equal(oneWood * 4, ItemWeights.Of(Item.Material("wood", "木料", 4)), 6);
    }

    [Fact]
    public void ItemWeight_BulkyIsHeavierThanFiddly()
    {
        // 木料/燃料这类大件明显重于布/钉子这类零碎（否则"拿什么扔什么"没有张力）
        Assert.True(ItemWeights.MaterialKg("wood") > ItemWeights.MaterialKg("cloth"));
        Assert.True(ItemWeights.MaterialKg("fuel") > ItemWeights.MaterialKg("nails"));
        // 白银是货币，几乎不占负重（否则没法带钱去交易）
        Assert.True(ItemWeights.MaterialKg("silver") < 0.05);
    }

    [Fact]
    public void ItemWeight_Armor_ReadsArmorTableWeight()
    {
        // 护甲重量以引擎 ArmorTable 的 Weight 为单一事实源（不在消费层复制数值）
        ArmorLayer plate = ArmorTable.Plate();
        Assert.Equal(plate.Weight, ItemWeights.ArmorKg(plate.Name), 6);
        Assert.Equal(plate.Weight, ItemWeights.Of(Item.Armor(plate.Name)), 6);
    }

    [Fact]
    public void ItemWeight_Weapon_HeavyGunsWeighMoreThanKnives()
    {
        Assert.True(ItemWeights.WeaponKg("步枪") > ItemWeights.WeaponKg("匕首"));
        Assert.True(ItemWeights.WeaponKg("狙击枪") > ItemWeights.WeaponKg("手枪"));
        Assert.Equal(ItemWeights.WeaponKg("匕首"), ItemWeights.Of(Item.Weapon("匕首")), 6);
    }

    [Fact]
    public void ItemWeight_UnknownKeysFallBackToDefault_NeverThrows()
    {
        Assert.Equal(ItemWeights.DefaultMaterialKg, ItemWeights.MaterialKg("不存在的材料"), 6);
        Assert.Equal(ItemWeights.DefaultWeaponKg, ItemWeights.WeaponKg("不存在的武器"), 6);
        Assert.Equal(ItemWeights.DefaultArmorKg, ItemWeights.ArmorKg("不存在的护甲"), 6);
    }

    [Fact]
    public void ItemWeight_TotalOfInventory_SumsEverything()
    {
        var store = new InventoryStore();
        store.Add(Item.Food(2, "罐头"));
        store.Add(Item.Material("wood", "木料", 3));
        store.Add(Item.Weapon("匕首"));

        double expected = ItemWeights.FoodPerPortionKg * 2
                          + ItemWeights.MaterialKg("wood") * 3
                          + ItemWeights.WeaponKg("匕首");
        Assert.Equal(expected, ItemWeights.TotalOf(store.Items), 6);
    }

    [Fact]
    public void LootWeight_MirrorsItemWeight()
    {
        // 搜刮前要能预判"这堆背不背得下" → LootItem 也得能称重
        Assert.Equal(ItemWeights.MaterialKg("wood") * 2, ItemWeights.OfLoot(LootItem.Material("wood", 2)), 6);
        Assert.Equal(ItemWeights.FoodPerPortionKg * 3, ItemWeights.OfLoot(LootItem.Food(3)), 6);
        Assert.Equal(ItemWeights.WeaponKg("步枪"), ItemWeights.OfLoot(LootItem.Weapon("步枪")), 6);
        Assert.Equal(0.0, ItemWeights.OfLoot(LootItem.Tool("calipers")), 6); // 工具进工作台不进背包
    }

    // ---------- 远征背包：硬上限（用户："不能超过 80kg"）----------

    [Fact]
    public void Bag_StartsEmpty_WithGivenCapacity()
    {
        var bag = new ExpeditionBag(80.0);
        Assert.Equal(80.0, bag.CapacityKg, 6);
        Assert.Equal(0.0, bag.CarriedKg, 6);
        Assert.Equal(80.0, bag.FreeKg, 6);
        Assert.False(bag.IsFull);
        Assert.Equal(LoadoutTier.Unencumbered, bag.Tier);
    }

    [Fact]
    public void Bag_TryAdd_AcceptsWhatFits()
    {
        var bag = new ExpeditionBag(10.0);
        Assert.True(bag.TryAdd(LootItem.Material("wood", 2)));  // 2 × 2kg = 4kg
        Assert.Equal(4.0, bag.CarriedKg, 6);
        Assert.Single(bag.Contents);
    }

    [Fact]
    public void Bag_TryAdd_HardRejectsWhatDoesNotFit()
    {
        // **硬上限**：装不下就是装不下（不是"超重减速"），这才制造取舍
        var bag = new ExpeditionBag(5.0);
        Assert.False(bag.TryAdd(LootItem.Material("wood", 4))); // 8kg > 5kg
        Assert.Equal(0.0, bag.CarriedKg, 6);                     // 一点没进
        Assert.Empty(bag.Contents);
    }

    [Fact]
    public void Bag_NeverExceedsEightyKilos_ForABaselinePerson()
    {
        // 用户原话"不能超过 80kg"：一路搬到满，再多一根木头也进不去
        var bag = new ExpeditionBag(CarryCapacity.For(1.0)); // 80kg
        for (int i = 0; i < 100; i++)
        {
            bag.AddAsManyAsFit(LootItem.Material("wood", 5)); // 每次 10kg
        }

        Assert.True(bag.CarriedKg <= 80.0 + 1e-9, $"背了 {bag.CarriedKg}kg，超过 80kg 硬上限");
        Assert.True(bag.IsFull);
        Assert.False(bag.TryAdd(LootItem.Material("wood", 1)));
    }

    [Fact]
    public void Bag_TryAdd_PartialStack_TakesOnlyWhatFits()
    {
        // 成堆材料可拆：背得下几件拿几件（"这堆木头只拿得走两根"）
        var bag = new ExpeditionBag(5.0);
        int taken = bag.AddAsManyAsFit(LootItem.Material("wood", 4)); // 每根 2kg，5kg 只装得下 2 根
        Assert.Equal(2, taken);
        Assert.Equal(4.0, bag.CarriedKg, 6);
    }

    [Fact]
    public void Bag_CanFit_PredictsWithoutMutating()
    {
        var bag = new ExpeditionBag(5.0);
        Assert.True(bag.CanFit(LootItem.Material("cloth", 1)));
        Assert.False(bag.CanFit(LootItem.Material("wood", 4)));
        Assert.Equal(0.0, bag.CarriedKg, 6); // 预判不改状态
    }

    [Fact]
    public void Bag_Drop_FreesCapacity()
    {
        // 取舍的另一半：扔掉旧的换新的
        var bag = new ExpeditionBag(5.0);
        LootItem wood = LootItem.Material("wood", 2);
        Assert.True(bag.TryAdd(wood));
        Assert.False(bag.CanFit(LootItem.Material("wood", 1))); // 只剩 1kg，塞不下 2kg 的木头

        Assert.True(bag.Drop(wood));
        Assert.Equal(0.0, bag.CarriedKg, 6);
        Assert.True(bag.CanFit(LootItem.Material("wood", 1)));
    }

    // ---------- 背包的三档（玩家的决策依据）----------

    [Fact]
    public void Bag_ReportsTheThreeTiers_AsItFillsUp()
    {
        var bag = new ExpeditionBag(80.0); // 基准人：30 / 50 / 80

        bag.AddAsManyAsFit(LootItem.Material("wood", 10)); // 20kg
        Assert.Equal(LoadoutTier.Unencumbered, bag.Tier);
        Assert.Equal(1.0, bag.SpeedMultiplier, 6);
        Assert.Equal(1.0, bag.AttackSpeedMultiplier, 6);

        bag.AddAsManyAsFit(LootItem.Material("wood", 10)); // 40kg
        Assert.Equal(LoadoutTier.Encumbered, bag.Tier);
        Assert.True(bag.SpeedMultiplier < 1.0);
        // 🔴 [T45·用户新曲线] 轻度档**也罚攻速了**——旧口径「背 30kg 挥剑没什么影响」已被用户推翻
        // （「50kg 减少 20% 移动速度**和攻击速度**」）。此处原本断言 == 1.0。
        Assert.True(bag.AttackSpeedMultiplier < 1.0);

        bag.AddAsManyAsFit(LootItem.Material("wood", 12)); // 64kg
        Assert.Equal(LoadoutTier.Strained, bag.Tier);
        Assert.True(bag.SpeedMultiplier < Loadout.SpeedAtStrain);
        Assert.True(bag.AttackSpeedMultiplier < Loadout.AttackSpeedAtStrain); // 重度档接着掉
    }

    [Fact]
    public void Bag_CapacityDropsMidTrip_LandsInOverloaded_ButKeepsTheGoods()
    {
        // 关内断了手：上限从 80 掉到 40，已背的 60kg 不会凭空消失，但你几乎走不动了
        var bag = new ExpeditionBag(80.0);
        bag.AddAsManyAsFit(LootItem.Material("wood", 30)); // 60kg
        Assert.Equal(LoadoutTier.Strained, bag.Tier);

        bag.SetCapacity(CarryCapacity.For(HungerState.CombineCapability(0.5, 0.0))); // 40kg
        Assert.Equal(LoadoutTier.Overloaded, bag.Tier);
        Assert.Equal(60.0, bag.CarriedKg, 6);                          // 东西还在
        Assert.True(bag.SpeedMultiplier < Loadout.SpeedAtLimit);       // 但爬着走
    }

    // ---------- 队伍运力：人 + 狗 ----------

    [Fact]
    public void PartyCapacity_SumsMembers()
    {
        double a = CarryCapacity.For(1.0);                                                  // 80
        double b = CarryCapacity.For(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true)); // 92（山姆）
        Assert.Equal(172.0, ExpeditionBag.PartyCapacity(new[] { a, b }, dogCapacityKg: 0), 6);
    }

    [Fact]
    public void PartyCapacity_DogPocketVest_AddsItsEightKilos()
    {
        // 布鲁斯的口袋狗衣（8kg）统一进同一套负重账：狗衣容量直接加到队伍背包上限
        var vest = new DogApparelSlots();
        vest.TryEquip(DogGearCatalog.PocketVestKey, out _);

        double human = CarryCapacity.For(1.0);
        double party = ExpeditionBag.PartyCapacity(new[] { human }, vest.TotalCarryCapacity());

        Assert.Equal(human + DogGearCatalog.PocketVestCapacity, party, 6);
        Assert.Equal(88.0, party, 6); // 80 + 8（T29 用户手改 6 → 8）
    }

    [Fact]
    public void PartyCapacity_NoDog_IsJustTheHumans()
    {
        Assert.Equal(80.0, ExpeditionBag.PartyCapacity(new[] { CarryCapacity.For(1.0) }, 0), 6);
    }

    // ---------- 校准：一趟能搬回多少 ----------

    /// <summary>
    /// 🔴 [T45] <b>「能搜的空间会很小」——用户原话，这条把它钉成数字。</b>
    /// <para>
    /// 装备进账之后，<b>硬上限 80kg 第一次真的咬人了</b>：它不再只限制"你搜了多少"，
    /// 而是限制"<b>装备之外</b>你还能搜多少"。同样是一个人去最大的点位（住宅区 ≈66kg 货）：
    /// </para>
    /// <list type="bullet">
    /// <item><b>轻装出门</b>（1.3kg）⇒ 硬余量 78.7kg ⇒ <b>搬得空</b>，到家 67.3kg（移速仅剩 45%）。</item>
    /// <item><b>中期装备</b>（13.8kg）⇒ 硬余量 66.2kg ⇒ <b>刚好搬得空</b>（只多 0.2kg 富余！），到家 79.8kg——<b>几乎钉在地上（移速 20%）</b>。</item>
    /// <item><b>板甲重装</b>（26.9kg）⇒ 硬余量 53.1kg ⇒ <b>搬不空，得留 12.9kg 在原地</b>。</item>
    /// </list>
    /// <b>⇒ 「要么带甲，要么带货」——这正是用户要的取舍，而且它是通过"余量"实现的，不是"出门即罚"。</b>
    /// <para>日后若调重物品重量或改上限，此测试会红，提示重新校准。</para>
    /// </summary>
    [Fact]
    public void Calibration_GearEatsYourHaul_HeavyArmourCannotClearTheBiggestSite()
    {
        // 实测 ExplorationCache（158 个搜刮点）：最大点位「住宅区」全部搜完约 66kg 货。
        const double biggestSiteKg = 66.0;
        double solo = CarryCapacity.For(1.0); // 80kg

        const double lightGear = 1.3;   // 开局：匕首 + 布衣 + 长裤 + 鞋
        const double midGear = 13.8;    // 中期：步枪 + 皮甲 + 军用头盔
        const double heavyGear = 26.9;  // 重装：狙击 + 板甲 + 防暴头盔

        // 轻装：搬得空，还富余一大截
        Assert.True(solo - lightGear >= biggestSiteKg, "轻装单人搬得空最大点位");

        // 中期：刚好搬得空（富余不到 1kg）——但到家时几乎走不动了
        Assert.True(solo - midGear >= biggestSiteKg, "中期装备单人**刚好**搬得空");
        Assert.True(solo - midGear - biggestSiteKg < 1.0, "而且只是刚好——富余不到 1kg");
        double midHome = midGear + biggestSiteKg; // 79.8kg，逼近 80kg 硬顶
        Assert.Equal(LoadoutTier.Strained, Loadout.TierOf(midHome, solo));
        Assert.True(Loadout.SpeedMultiplier(midHome, solo) < 0.25, "背成这样，移速只剩两成——被丧尸追上就完了");

        // 🔴 板甲重装：**搬不空**。装备吃掉了 26.9kg 的余量 ⇒ 有 12.9kg 货只能留在原地。
        Assert.True(solo - heavyGear < biggestSiteKg, "穿板甲的人搬不空最大点位——「能搜的空间会很小」");
        Assert.Equal(12.9, biggestSiteKg - (solo - heavyGear), 6); // 留在原地的那 12.9kg
    }
}
