using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 负重上限系统（阈值与上限来自 Wiki 配置）。
/// 本文件覆盖上限算式、物品称重、远征背包硬上限、队伍运力（人+狗）。三档 debuff 曲线本身见 <c>LoadoutTests</c>；
/// [T45] 装备进账、逐人分档、消费层接线、**搜刮余量表**见 <c>CarryLoadWiringTests</c>。
/// 铁律核对：**不存在"力量"属性** —— 基准负重来自 Wiki 配置，个体差异只来自 authored 专属效果（山姆）与身体状态（残缺/饥饿）。
/// </summary>
public class CarryCapacityTests
{
    // ---------- 上限模型 ----------

    [Fact]
    public void Limit_BaseIsUniformForEveryone_80kg()
    {
        // 无残缺无饥饿无专属效果 = 全员使用同一 Wiki 配置基准（这就是"无属性系统"的形态）
        // [T45] 这本账现在**含装备**（连人带甲带枪），基准值来自 Wiki 配置——
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
        // 饥饿等级惩罚按乘算规则作用于 Wiki 配置的基础上限。
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
    public void Limit_SamLevel3_SamHimself_UsesPersonalCarryBonusOnly()
    {
        // 当前 authored 语义：山姆 L1 起只有本人负重加成；L3 不再叠加退役的全营光环。
        double mult = SamPerk.CarryCapacityMultiplier(3, isSam: true);
        double limit = CarryCapacity.For(1.0, mult);

        Assert.Equal(92.0, limit, 6); // 80 × 1.15
        Assert.Equal(80.0 * 1.15, limit, 6);
    }

    [Fact]
    public void Limit_SamLevel3_OtherSurvivors_DoNotGetRetiredCampAura()
    {
        double mult = SamPerk.CarryCapacityMultiplier(3, isSam: false);
        Assert.Equal(80.0, CarryCapacity.For(1.0, mult), 6); // 当前页面没有全营负重效果
    }

    [Fact]
    public void Limit_SamMissingTwoFingers_PerkOffsetsDisability()
    {
        // 山姆开局缺两指导致操作能力下降；二级专属效果按乘算规则抵消部分影响。
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
    public void ItemWeight_AuditedAmmoAndTea_HaveExplicitWeights()
    {
        // 弹药重量来自 Wiki 配置；这里同时校验显式登记。
        Assert.Equal(0.01, ItemWeights.MaterialKg("ammo_short"), 6);
        Assert.Equal(0.02, ItemWeights.MaterialKg("ammo_medium"), 6);
        Assert.Equal(0.05, ItemWeights.MaterialKg("ammo_buck"), 6);

        // 重头箭使用 Wiki 配置中的独立重量，不走通用兜底。
        Assert.Equal(0.05, ItemWeights.MaterialKg("ammo_arrow_heavy"), 6);
        Assert.NotEqual(ItemWeights.AmmoPerRoundKg, ItemWeights.MaterialKg("ammo_arrow_heavy"));

        // 审计确认的弹药与茶必须有显式登记；即使弹药值恰好等于旧兜底，也不能靠返回值判断登记是否存在。
        var expected = new Dictionary<string, double>
        {
            ["ammo_long"] = 0.03,
            ["ammo_arrow_stick"] = 0.03,
            ["ammo_arrow_handmade"] = 0.03,
            ["ammo_arrow_carbon"] = 0.03,
            ["dandelion_tea"] = 0.25,
            ["rosehip_tea"] = 0.25,
        };

        foreach (var (key, expectedKg) in expected)
        {
            Assert.True(ItemRegistry.Materials.TryGetValue(key, out double registeredKg),
                $"{key} 未显式登记进 ItemRegistry.Materials（会静默走默认/弹药兜底）");
            Assert.Equal(expectedKg, registeredKg, 6);
            Assert.Equal(expectedKg, ItemWeights.MaterialKg(key), 6);
        }
    }

    /// <summary>
    /// 🔴 焊死测试：<c>WeaponTable.Arsenal()</c> 里**每一把**武器都必须在 <c>ItemWeights._weaponKg</c> 显式登记重量，
    /// 禁止落 <see cref="ItemWeights.DefaultWeaponKg"/> 兜底。
    /// <para>
    /// 武器重量的**唯一真源**是 <c>_weaponKg</c>（中文名字典）——<c>Weapon</c> record 里**没有 Weight 字段**。
    /// 加/改武器漏登记就会静默使用默认重量，负重系统当场失真。
    /// 护甲侧早有对应焊死（<see cref="ItemWeight_Armor_ReadsArmorTableWeight"/> 一类），武器侧本测补齐。
    /// </para>
    /// <para>
    /// ⚠ 用**私有字典的键是否存在**判定（反射直读 <c>_weaponKg</c>），不能用重量值代替——
    /// 合法武器可能与默认值相同。天生武器（爪击/撕咬/拳脚）不入 Arsenal，不在此列。
    /// </para>
    /// </summary>
    [Fact]
    public void ItemWeight_EveryArsenalWeapon_HasExplicitWeightRegistered()
    {
        var field = typeof(ItemWeights).GetField(
            "_weaponKg",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var registered = (IDictionary<string, double>)field!.GetValue(null)!;

        var missing = WeaponTable.Arsenal()
            .Select(w => w.Name)
            .Where(name => !registered.ContainsKey(name))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"以下武器未在 ItemWeights._weaponKg 显式登记重量（会静默落默认值；数值来自 Wiki 配置）：" +
            string.Join("、", missing));
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
        Assert.Equal(0.0, ItemWeights.OfLoot(LootItem.Tool("calipers")), 6); // 工具随队携带，但自身不计重
    }

    // ---------- 远征背包：硬上限（基准值来自 Wiki 配置）----------

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
        Assert.True(bag.TryAdd(LootItem.Material("wood", 2)));  // [T68] 2 × 1kg = 2kg（木料减半）
        Assert.Equal(2.0, bag.CarriedKg, 6);
        Assert.Single(bag.Contents);
    }

    [Fact]
    public void Bag_TryAdd_HardRejectsWhatDoesNotFit()
    {
        // **硬上限**：装不下就是装不下（不是"超重减速"），这才制造取舍
        var bag = new ExpeditionBag(5.0);
        Assert.False(bag.TryAdd(LootItem.Material("wood", 6))); // [T68] 6 × 1kg = 6kg > 5kg（木料减半后要 6 根才超）
        Assert.Equal(0.0, bag.CarriedKg, 6);                     // 一点没进
        Assert.Empty(bag.Contents);
    }

    [Fact]
    public void Bag_NeverExceedsEightyKilos_ForABaselinePerson()
    {
        // 用户原话"不能超过上限"：一路搬到满，再多一根木头也进不去
        var bag = new ExpeditionBag(CarryCapacity.For(1.0)); // 80kg
        for (int i = 0; i < 100; i++)
        {
            bag.AddAsManyAsFit(LootItem.Material("wood", 5)); // [T68] 每次 5kg（木料 1kg/根）
        }

        Assert.True(bag.CarriedKg <= 80.0 + 1e-9, $"背了 {bag.CarriedKg}kg，超过 Wiki 配置的负重上限");
        Assert.True(bag.IsFull);
        Assert.False(bag.TryAdd(LootItem.Material("wood", 1)));
    }

    [Fact]
    public void Bag_TryAdd_PartialStack_TakesOnlyWhatFits()
    {
        // 成堆材料可拆：背得下几件拿几件（"这堆木头只拿得走两根"）
        var bag = new ExpeditionBag(2.0);
        int taken = bag.AddAsManyAsFit(LootItem.Material("wood", 4)); // [T68] 每根 1kg，2kg 只装得下 2 根
        Assert.Equal(2, taken);
        Assert.Equal(2.0, bag.CarriedKg, 6);
    }

    [Fact]
    public void Bag_CanFit_PredictsWithoutMutating()
    {
        var bag = new ExpeditionBag(5.0);
        Assert.True(bag.CanFit(LootItem.Material("cloth", 1)));
        Assert.False(bag.CanFit(LootItem.Material("wood", 6)));   // [T68] 6kg > 5kg（木料减半）
        Assert.Equal(0.0, bag.CarriedKg, 6); // 预判不改状态
    }

    [Fact]
    public void Bag_Drop_FreesCapacity()
    {
        // 取舍的另一半：扔掉旧的换新的
        var bag = new ExpeditionBag(3.0);
        LootItem wood = LootItem.Material("wood", 2);            // [T68] 2 × 1kg = 2kg
        Assert.True(bag.TryAdd(wood));
        Assert.False(bag.CanFit(LootItem.Material("wood", 2))); // 只剩 1kg，塞不下再来 2kg 的木头

        Assert.True(bag.Drop(wood));
        Assert.Equal(0.0, bag.CarriedKg, 6);
        Assert.True(bag.CanFit(LootItem.Material("wood", 2)));
    }

    // ---------- 背包的三档（玩家的决策依据）----------

    [Fact]
    public void Bag_ReportsTheThreeTiers_AsItFillsUp()
    {
        var bag = new ExpeditionBag(80.0); // 基准人；阈值来自 Wiki 配置

        bag.AddAsManyAsFit(LootItem.Material("wood", 20)); // [T68] 20kg（木料 1kg/根）
        Assert.Equal(LoadoutTier.Unencumbered, bag.Tier);
        Assert.Equal(1.0, bag.SpeedMultiplier, 6);
        Assert.Equal(1.0, bag.AttackSpeedMultiplier, 6);

        bag.AddAsManyAsFit(LootItem.Material("wood", 20)); // 40kg
        Assert.Equal(LoadoutTier.Encumbered, bag.Tier);
        Assert.True(bag.SpeedMultiplier < 1.0);
        // 🔴 [T45·用户新曲线] 轻度档**也罚攻速了**——旧口径「低负重挥剑没什么影响」已被用户推翻。
        Assert.True(bag.AttackSpeedMultiplier < 1.0);

        bag.AddAsManyAsFit(LootItem.Material("wood", 24)); // 64kg
        Assert.Equal(LoadoutTier.Strained, bag.Tier);
        Assert.True(bag.SpeedMultiplier < Loadout.SpeedAtStrain);
        Assert.True(bag.AttackSpeedMultiplier < Loadout.AttackSpeedAtStrain); // 重度档接着掉
    }

    [Fact]
    public void Bag_CapacityDropsMidTrip_LandsInOverloaded_ButKeepsTheGoods()
    {
        // 关内断了手：上限下降，已背物品不会凭空消失，但你几乎走不动了
        var bag = new ExpeditionBag(80.0);
        bag.AddAsManyAsFit(LootItem.Material("wood", 60)); // [T68] 60kg（木料 1kg/根）
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
        double a = CarryCapacity.For(1.0);                                                  // 基准人
        double b = CarryCapacity.For(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true)); // 山姆专属效果
        Assert.Equal(172.0, ExpeditionBag.PartyCapacity(new[] { a, b }, dogCapacityKg: 0), 6);
    }

    [Fact]
    public void PartyCapacity_DogPocketVest_AddsItsEightKilos()
    {
        // 布鲁斯的口袋狗衣容量来自 Wiki 配置，并统一进同一套负重账。
        var vest = new DogApparelSlots();
        vest.TryEquip(DogGearCatalog.PocketVestKey, out _);

        double human = CarryCapacity.For(1.0);
        double party = ExpeditionBag.PartyCapacity(new[] { human }, vest.TotalCarryCapacity());

        Assert.Equal(human + DogGearCatalog.PocketVestCapacity, party, 6);
        Assert.Equal(88.0, party, 6); // 与 Wiki 配置的狗衣容量一致
    }

    [Fact]
    public void PartyCapacity_NoDog_IsJustTheHumans()
    {
        Assert.Equal(80.0, ExpeditionBag.PartyCapacity(new[] { CarryCapacity.For(1.0) }, 0), 6);
    }

    // ---------- 校准：一趟能搬回多少 ----------

    /// <summary>
    /// 🔴 [T45 / carryweight2·枪械翻倍后重推] <b>「能搜的空间会很小」——用户原话，这条锁住校准结果。</b>
    /// <para>
    /// 装备进账之后，硬上限按 Wiki 配置计入装备：它不再只限制"你搜了多少"，
    /// 而是限制"<b>装备之外</b>你还能搜多少"。同一个人去最大点位（住宅区 ≈66kg 货）：
    /// </para>
    /// <list type="bullet">
    /// <item><b>轻装出门</b>（1.3kg）⇒ 硬余量 78.7kg ⇒ <b>搬得空</b>，到家 67.3kg（移速仅剩 45%）。</item>
    /// <item><b>中期装备</b>（17.3kg）⇒ 硬余量 62.7kg ⇒ <b>搬不空了</b>，当前测量结果留 3.3kg。</item>
    /// <item><b>板甲重装</b>（29.9kg）⇒ 硬余量 50.1kg ⇒ <b>搬不空，得留 15.9kg 在原地</b>。</item>
    /// </list>
    /// <b>⇒ 「要么带甲带枪，要么带货」——这正是用户要的取舍，重武器把余量吃得更狠正是本轮翻倍的意图。</b>
    /// <para>日后若调重物品重量或改上限，此测试会红，提示重新校准。</para>
    /// </summary>
    [Fact]
    public void Calibration_GearEatsYourHaul_HeavyArmourCannotClearTheBiggestSite()
    {
        // 实测 ExplorationCache（158 个搜刮点）：最大点位「住宅区」全部搜完约 66kg 货。
        const double biggestSiteKg = 66.0;
        double solo = CarryCapacity.For(1.0); // 80kg

        const double lightGear = 1.3;    // 开局：匕首 + 布衣 + 长裤 + 鞋
        const double midGear = 17.3;     // 中期：步枪7.5 + 皮甲 + 军用头盔
        const double heavyGear = 29.9;   // 重装：狙击9 + 板甲 + 防暴头盔

        // 轻装：搬得空——但若真背满 66kg 货，到家几乎走不动
        Assert.True(solo - lightGear >= biggestSiteKg, "轻装单人搬得空最大点位");
        double lightHome = lightGear + biggestSiteKg; // 67.3kg
        Assert.Equal(LoadoutTier.Strained, Loadout.TierOf(lightHome, solo));
        Assert.True(Loadout.SpeedMultiplier(lightHome, solo) < 0.5, "背满最大点位货物到家，移速掉到一半以下");

        // 🔴 中期：按当前 Wiki 配置，连原厂中期步枪都搬不空最大点位了——留 3.3kg 在原地
        Assert.True(solo - midGear < biggestSiteKg, "枪械翻倍后，中期原厂步枪单人也搬不空最大点位");
        Assert.Equal(3.3, biggestSiteKg - (solo - midGear), 6); // 留在原地的那 3.3kg

        // 🔴 板甲重装：搬不空得更多，有 15.9kg 货只能留在原地。
        Assert.True(solo - heavyGear < biggestSiteKg, "穿板甲带重枪的人搬不空最大点位——「能搜的空间会很小」");
        Assert.Equal(15.9, biggestSiteKg - (solo - heavyGear), 6); // 留在原地的那 15.9kg

        // 出门那一刻：重装仍在 Wiki 配置的免罚线下，不触发惩罚。
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(heavyGear, solo));
    }

    // ───────────────────── [R6] 物品单一登记入口（ItemRegistry）─────────────────────
    // 三张重量字典（武器/材料/护甲）已合并进一处按类别分区的 ItemRegistry；ItemWeights 的三个私有字段
    // 现在是它的薄别名。下面三条护栏钉死：①别名不发生数值漂移（同一实例）②护甲侧补齐 R4 式焊死（漏登记即红）。

    /// <summary>
    /// [R6] <c>ItemWeights</c> 的三个私有重量字段与 <see cref="ItemRegistry"/> 的分区表是**同一个字典实例**——
    /// 证明合并后没有第二份数值副本在别处偷偷分叉（两份事实源打架正是本次重构要消灭的病）。
    /// </summary>
    [Fact]
    public void ItemRegistry_ItemWeightsFields_AreTheSameInstanceAsRegistry()
    {
        object Field(string name) => typeof(ItemWeights).GetField(
            name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

        Assert.Same(ItemRegistry.Weapons, Field("_weaponKg"));
        Assert.Same(ItemRegistry.Materials, Field("_materialKg"));
        Assert.Same(ItemRegistry.Armor, Field("_armorKg"));

        // 分区规模钉桩——防止别处误插/误删一整类。
        Assert.Equal(27, ItemRegistry.Weapons.Count);
        Assert.Equal(56, ItemRegistry.Materials.Count);
        Assert.Equal(38, ItemRegistry.Armor.Count);
        Assert.Equal(121, ItemRegistry.All.Count());
    }

    /// <summary>
    /// 🔴 [R6] 护甲侧焊死测试（补齐 R4 武器侧 <see cref="ItemWeight_EveryArsenalWeapon_HasExplicitWeightRegistered"/>）：
    /// <c>ArmorTable</c> 里**每一个单层护甲方法**产出的护甲名，都必须在 <see cref="ItemRegistry.Armor"/> 登记，
    /// 禁止落 <see cref="ItemWeights.DefaultArmorKg"/> 兜底。
    /// <para>
    /// 这正是本轮修的 bug 类：护甲漏登记会静默使用默认重量。护甲重量的单一事实源是引擎 <c>ArmorLayer.Weight</c>，
    /// 登记花名册是 <see cref="ItemRegistry.ArmorRoster"/>——
    /// 加一件护甲到 ArmorTable 却忘了补进花名册，本测试当场报红。
    /// </para>
    /// <para>⚠ 只覆盖返回单个 <c>ArmorLayer</c> 的无参方法；<c>ZombieHide()/SurvivorArmor()</c> 返回列表
    /// （天生腐皮/组合层，不入称重花名册），按返回类型自动排除。</para>
    /// </summary>
    [Fact]
    public void ItemWeight_EveryArmorTableLayer_IsRegistered()
    {
        var layerMethods = typeof(ArmorTable)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(ArmorLayer) && m.GetParameters().Length == 0);

        var missing = layerMethods
            .Select(m => ((ArmorLayer)m.Invoke(null, null)!).Name)
            .Where(name => !ItemRegistry.Armor.ContainsKey(name))
            .Distinct()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"以下 ArmorTable 护甲未登记进 ItemRegistry.ArmorRoster（会静默落 {ItemWeights.DefaultArmorKg}kg 兜底）：" +
            string.Join("、", missing));
    }
}
