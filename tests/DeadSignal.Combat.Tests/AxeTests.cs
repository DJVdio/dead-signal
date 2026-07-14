using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [批次25·T44] 消防斧 —— 用户点名新建的一把近战武器，同时是<b>「利爪型」枪械改装的近战锚点</b>
/// （改装件 <c>claw_stock</c> 的口径是「近战模式等同于 <b>80% 攻速的消防斧</b>」，见 docs/wiki/data/weapon-mods.json）。
///
/// <para>
/// <b>它的数值不是自由的</b>：利爪型 = 消防斧 × 0.8 DPS，而利爪型被两条护栏夹住
/// （必须强过原厂枪托，否则没人改；必须弱于匕首，否则改装取代真近战武器）
/// ⇒ 反推出<b>消防斧 DPS 的硬区间 [2.36, 2.94]</b>（推导见 docs/research/2026-07-14-user-mod-review.md §2）。
/// 本文件把这个区间钉成硬护栏：以后谁动消防斧的伤害/攻速，利爪型的强度就跟着动，这里当场红。
/// </para>
///
/// <para>
/// <b>砸墙那条（本单拍板，数值层）</b>：消防斧是<b>锐器</b>（劈砍就是刃；利爪型语义也要求 Sharp），
/// 但它是<b>全表唯一一把破拆工具型锐器</b> —— 劈门本来就是斧子的正经用途。
/// 故砸墙三段梯度在它这里变成四段，见 <see cref="消防斧是唯一能破拆的锐器_压过全部枪托_但仍不及最弱的钝器"/>。
/// </para>
/// </summary>
public class AxeTests
{
    private static double Dps(Weapon w) => WeaponDps.Single(w);

    // ——— 数值：利爪型反推出来的硬区间 ———

    /// <summary>利爪型必须<b>强于原厂枪托</b>（否则没人去改）⇒ 消防斧 DPS 下界 2.36。</summary>
    private const double ClawFloorAxeDps = 2.36;

    /// <summary>利爪型必须<b>弱于匕首</b>（改装不该取代一把真刀）⇒ 消防斧 DPS 上界 2.94。</summary>
    private const double ClawCeilingAxeDps = 2.94;

    /// <summary>利爪型 = 80% 攻速的消防斧（口径写在 weapon-mods.json 的「利爪型」那一行）。</summary>
    private const double ClawAttackSpeedFactor = 0.8;

    [Fact]
    public void 消防斧在武器表里_且是全表唯一的斧子()
    {
        Weapon axe = WeaponTable.Axe();
        Assert.Equal("消防斧", axe.Name);
        Assert.Single(WeaponTable.Arsenal().Where(w => w.Name == axe.Name));
    }

    [Fact]
    public void 消防斧追加在Arsenal末尾_不插队()
    {
        // CLAUDE.md 铁律：新武器一律追加末尾。Sim 按 idx 派生种子 ⇒ 插队会打乱其后所有武器的随机流，
        // 既有基线当场漂移。这条护栏比注释管用。
        //
        // ⚠️ [T56] 消防斧**不再是最后一把**——骨刀在它之后追加了。但这条护栏要钉的从来不是"消防斧在末尾"，
        // 而是"**消防斧没有插队**"：它当年是第 24 把（idx 23），今天仍必须是第 24 把。后来者只能排在它**后面**，
        // 一旦有人把新武器插到它前面，它的 idx 前移、其后所有武器的随机流被打乱 —— 这条当场红。
        IReadOnlyList<Weapon> arsenal = WeaponTable.Arsenal();
        Assert.Equal("消防斧", arsenal[23].Name);
    }

    [Fact]
    public void 消防斧的DPS落在利爪型反推出来的硬区间内()
    {
        double dps = Dps(WeaponTable.Axe());

        Assert.True(dps >= ClawFloorAxeDps,
            $"消防斧 DPS {dps:F4} 低于 {ClawFloorAxeDps} ⇒ 利爪型（×0.8 = {dps * ClawAttackSpeedFactor:F2}）会弱于原厂枪托，没人会去改装");
        Assert.True(dps <= ClawCeilingAxeDps,
            $"消防斧 DPS {dps:F4} 高于 {ClawCeilingAxeDps} ⇒ 利爪型（×0.8 = {dps * ClawAttackSpeedFactor:F2}）会超过匕首，" +
            "改装就取代了真近战武器——「我该不该带把刀」也就不成其为选择");
    }

    [Fact]
    public void 利爪型只是消防斧的八折_必须弱于匕首()
    {
        // 这条是上一条的"人话版"：直接把利爪型的实际强度算出来跟匕首比，
        // 免得日后有人只改区间常数、不看它到底意味着什么。
        double claw = Dps(WeaponTable.Axe()) * ClawAttackSpeedFactor;
        double dagger = Dps(WeaponTable.Dagger());

        Assert.True(claw < dagger,
            $"利爪型 {claw:F2} 必须弱于匕首 {dagger:F2}——改装能让你「打空了还能打」，不能让你不必带近战武器");
    }

    [Fact]
    public void 消防斧是锐器_利爪型的语义要求它是刃不是锤()
    {
        // WeaponMods.StockMeleeDamageTypeOf(Claw) 已经写死返回 Sharp（"枪托绑利刃"）。
        // 消防斧若改成钝器，利爪型就会自相矛盾：一把绑着"消防斧"的枪打出的却是锐伤。
        Assert.Equal(DamageType.Sharp, WeaponTable.Axe().DamageType);
    }

    [Fact]
    public void 消防斧是双手武器_不可双持()
    {
        Weapon axe = WeaponTable.Axe();
        Assert.True(axe.TwoHanded);
        Assert.False(axe.CanDualWield);
    }

    // ——— 砸墙：本单拍板的那条 ———

    [Fact]
    public void 消防斧是唯一能破拆的锐器_压过全部枪托_但仍不及最弱的钝器()
    {
        // 【本单拍板·数值层】消防斧是锐器，但**劈门是斧子的正经用途**，它必须打破"锐器砸墙无用"这条通则。
        // 落点：三段梯度（锐器 ≤0.98 ＜ 枪托 2.08~2.84 ＜ 钝器 3.67~12.44）在它这里变成**四段**——
        //
        //     其余锐器（≤0.98） ＜ 枪托（2.08~2.84） ＜ 【消防斧】 ＜ 钝器（3.67~12.44）
        //
        // 为什么不让它进钝器档：钝器打**人**是弱的（棍棒打丧尸 47.8% 全表垫底），它们的**全部**回报就在砸墙上。
        // 消防斧打人已经有 ~2.8 DPS（长剑档），再让它砸墙也压过锤子，钝器就彻底没有存在理由了。
        double axe = StructureDamage.PerSecond(WeaponTable.Axe());

        Weapon[] guns = WeaponTable.Arsenal().Where(w => w.HasMeleeProfile).ToArray();
        Assert.NotEmpty(guns);
        double bestStock = guns.Max(StructureDamage.PerSecond);

        double weakestBlunt = WeaponTable.Arsenal()
            .Where(w => !w.IsRanged && w.DamageType == DamageType.Blunt)
            .Min(StructureDamage.PerSecond);

        Assert.True(axe > bestStock,
            $"消防斧砸墙 {axe:F2} 点/秒 必须压过最猛的枪托 {bestStock:F2} 点/秒——" +
            "拿枪托砸门都比拿斧子劈门快，那是荒唐的");
        Assert.True(axe < weakestBlunt,
            $"消防斧砸墙 {axe:F2} 点/秒 不得赶上最弱的钝器 {weakestBlunt:F2} 点/秒——" +
            "钝器打人弱是代价，破拆强是它唯一的回报，这条塌了钝器就没有存在理由了");
    }

    [Fact]
    public void 消防斧砍不动金属门_要破金属门还是得带把锤子()
    {
        // 用户口径：「消防斧砍木头很快，但砍不动金属门」。
        // 单一标量的砸墙系数模型里，"砍不动金属门"表现为**绝对耗时长到不可接受**：
        // 一分多钟贴在门上劈，门后的东西早出来了。
        double hp = CampStructureTable.MaxHp(StructureTier.DoorMetal);
        double axe = StructureDamage.SecondsToBreach(WeaponTable.Axe(), hp);
        double hammer = StructureDamage.SecondsToBreach(WeaponTable.Warhammer(), hp);

        Assert.True(axe > hammer * 3,
            $"砸穿金属门：消防斧 {axe:F0}s vs 破甲锤 {hammer:F0}s——差距不到 3 倍，" +
            "「要破金属门就得带把锤子」这条就不成立了");
        Assert.True(axe > 60,
            $"消防斧砸穿金属门只要 {axe:F0}s——太快了。斧子是劈木头的，不是开罐头的");
    }

    // ——— 消费层登记：漏一样它就是把「死武器」 ———

    [Fact]
    public void 消防斧登记了重量_否则负重表会当它是未登记武器()
    {
        // 未登记的武器名会静默落到 DefaultWeaponKg = 2.0kg 的兜底值——不报错、不崩，只是悄悄错。
        Assert.NotEqual(ItemWeights.DefaultWeaponKg, ItemWeights.WeaponKg("消防斧"));
    }

    [Fact]
    public void 消防斧登记了图标()
    {
        Assert.NotNull(ItemIcons.Find("消防斧"));
        Assert.Empty(ItemIcons.MissingFor(new[] { "消防斧" }));
    }

    [Fact]
    public void 消防斧有玩家可见的简介()
    {
        Assert.False(string.IsNullOrWhiteSpace(WeaponTable.Axe().Description));
        Assert.False(string.IsNullOrWhiteSpace(WeaponTable.DescriptionOf("消防斧")));
    }

    // ——— 可造 + 可捡：两条获取通道 ———

    [Fact]
    public void 消防斧有配方_且产物落地为一把真武器()
    {
        RecipeData recipe = Assert.Single(RecipeBook.All.Where(r => r.Id == "axe"));
        Assert.Equal("消防斧", recipe.DisplayName);

        Item made = Assert.Single(CraftOutputFactory.Create(recipe.OutputKey, 1));
        Assert.Equal(ItemCategory.Weapon, made.Category);
        // RefKey 必须是中文名——ItemWeights / ItemIcons / WeaponTable 三张表都以它为键。
        Assert.Equal("消防斧", made.RefKey);
        // 描述由 WeaponTable.DescriptionOf 自动填 ⇒ 造出来的消防斧在库存里不是一件没有说明的东西。
        Assert.False(string.IsNullOrWhiteSpace(made.Description));
    }

    [Fact]
    public void 消防斧能在探索关里搜到_至少两处()
    {
        var found = new List<string>();
        foreach (string id in AllCacheIds())
        {
            CacheResult? r = ExplorationCache.Resolve(id, new StoryFlags());
            if (r is { } hit && hit.Loot.Any(l => l.Kind == LootKind.Weapon && l.RefId == "消防斧"))
            {
                found.Add(id);
            }
        }

        Assert.True(found.Count >= 2,
            $"消防斧只在 {found.Count} 处能捡到——一把只能造不能捡的武器，等于把它锁死在材料链后面");
    }

    /// <summary>
    /// 🔴 <b>[T62 改名护栏] 消防斧的重量必须是 3.0kg —— 不是默认值 2.0kg。</b>
    /// <para><c>ItemWeights._weaponKg</c> 是**按中文名索引的字典**（武器重量的真源在那儿，<c>Weapon</c> 记录里根本没有重量字段）。
    /// 改名时若漏改这本字典的键，重量会**静默**落到 <c>DefaultWeaponKg = 2.0</c> —— 不报错、不崩溃，
    /// 只是消防斧比棍棒（1.5kg）沉不了多少，玩家的负重余量凭空多出 1kg。而负重系统刚刚接线（装备真的进负重账）。</para>
    /// <para>顺带钉死"名字改了、字典没改"这类回归：查的是 <c>WeaponTable.Axe().Name</c>，不是硬编码字符串。</para>
    /// </summary>
    [Fact]
    public void 消防斧的重量是3公斤_不是未登记时的默认2公斤()
    {
        string name = WeaponTable.Axe().Name;
        Assert.Equal("消防斧", name);

        double kg = ItemWeights.WeaponKg(name);
        Assert.Equal(3.0, kg, 3);
        Assert.NotEqual(ItemWeights.DefaultWeaponKg, kg);
    }

    private static IEnumerable<string> AllCacheIds()
        => new[]
        {
            ExplorationCache.RiversideCabinName, ExplorationCache.HarvesterWarehouseName,
            ExplorationCache.WatchersCabinName, ExplorationCache.CityRooftopLookoutName,
            ExplorationCache.BroadcastStationName, ExplorationCache.GoldfingerBaseName,
            ExplorationCache.EastNewVillageName, ExplorationCache.GasStationName,
            ExplorationCache.SupermarketName, ExplorationCache.HospitalName,
            // [批次25·T50·impl-firestation] 消防站——消防斧的**天然出处**（消防斧），也是玩家最早够得着的一把。
            // 同一单撤掉了南林村庄·柴垛那把（大点/中后期，冗余）⇒ 消防斧仍是**两处**可捡，本测试的 ≥2 依然成立，
            // 具体分布由 FireStationCacheTests.消防斧全图恰两处_… 钉死。
            ExplorationCache.FireStationName,
            VillageRescue.DestinationName, NurseRecruit.DestinationName,
        }.SelectMany(ExplorationCache.CacheIdsFor).Distinct();
}
