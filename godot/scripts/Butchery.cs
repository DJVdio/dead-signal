using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型
//（与 CookingLogic.cs / TrapLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（把宰杀点立到场上、开面板、下单排队、把肉与皮塞进库存）归 Godot 消费层，本文件只出**规则 + 数值**。

// ═══════════════════════════════════════════════════════════════════════════════════════
// 【T67】宰杀 —— 用户拍板的一道**新工序**，卡在"猎物"和"饭"之间。
//
// 用户 authored 说明（当前配方、工时、速度、概率与产出数值以 Wiki 配置为准）：
//   老鼠和鸟不能直接入锅，必须先经过宰杀；简易宰杀点可升级为宰杀台；
//   两档设施各有一个刀槽，只允许放入匕首或骨刀；宰杀台有双倍产出机制，
//   设施与刀的速度贡献采用本文件后文说明的加算例外。
//
// 🔴 **它把三样东西同时接上了**：
//   ① **羽毛的唯一来源** ⇒ 三种箭（用户已把它们全改成吃羽毛）从此有料可造 —— 这是整条弓箭线的源头。
//   ② **生皮的第一条生产线** ⇒ 核实过：`rawhide` 此前**零掉落、零配方产出**，只能找商人买。碎皮革缝起来就是它。
//   ③ 🔴 **骨刀终于有了存在的理由** ⇒ 它是全表较弱的武器，
//      此前是个"造出来也没人拿"的摆设。现在它是**宰杀工具**——
//      **骨刀的定位从"废武器"变成了"工具"**，而它的配方门槛（《野外生存指南》，开局共享库存就有）
//      正好让它成为营地第一把能上案板的刀。
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>宰杀设施的两档（用户："简易宰杀点<b>可以升级为</b>宰杀台"）。</summary>
public enum ButcherTier
{
    /// <summary>简易宰杀点：一块板子加一个钩子。基准速度，无双倍产出。</summary>
    SimplePoint,

    /// <summary>宰杀台：速度与双倍产出效果均以 Wiki 配置为准。</summary>
    Table,
}

/// <summary>
/// 宰杀设施槽位里的那把刀（用户："<b>一个槽位</b>，可以放入<b>匕首</b>或者<b>骨刀</b>"）。
///
/// <para>═══ 🔴 <b>「放进槽位的刀还能不能拿去打架？」—— 不能，这是有意的</b> ═══
/// 装槽 = <b>把刀从库存里拿走，钉在案板上</b>；卸槽 = 还回库存（<b>与烹饪台的锅/烤架完全同一条语义</b>，
/// 见 <see cref="CookwareSlot"/> / <see cref="CookStation.ItemKeyOf"/> —— 那两件也是装槽即离库、卸槽即归库）。
/// <list type="bullet">
/// <item><b>为什么不做"共享"</b>：让同一把匕首既挂在案板上又插在腰上，等于<b>白送</b>——
///       宰杀速度这条设计的代价就消失了，槽位也就不再是一个选择。</item>
/// <item><b>它制造的取舍是真的</b>：营地往往<b>只有一把匕首</b>（全表较强的近战之一）。
///       把它钉在案板上，今晚站岗的人就得换别的家伙。<b>这正是「骨刀」的位置</b>：
///       骨刀慢一档，但没人会心疼一把较弱的刀——**它本来就不该上战场。**</item>
/// </list></para>
/// </summary>
public enum ButcherKnife
{
    /// <summary>槽位空着。<b>没刀就宰不了</b>（<see cref="ButcheryLogic.CanButcher"/>）——徒手撕不开一只老鼠。</summary>
    None,

    /// <summary>匕首（宰杀速度以 Wiki 配置为准）。全表较好的宰杀刀，也是近战取舍的一部分。</summary>
    Dagger,

    /// <summary>骨刀（宰杀速度以 Wiki 配置为准）。慢一档，但你不会舍不得。</summary>
    BoneKnife,
}

/// <summary>
/// 宰杀设施的规格（两档：<see cref="ButcherTier.SimplePoint"/> / <see cref="ButcherTier.Table"/>）。
/// <b>建造范式零发明</b>：走烹饪台那条链（配方产出一件 → 库存「摆放」→ 落位；<b>营地里一座就够</b>，
/// 由 <see cref="AbsentGate"/> 把重复建造灰掉 —— 同 <see cref="CookStation.AbsentGate"/> / <c>ModBenchAbsentGate</c> 的既有做法）。
/// </summary>
public static class ButcherStation
{
    /// <summary>简易宰杀点配方 id。</summary>
    public const string PointRecipeId = "butcher_point";

    /// <summary>宰杀台（升级）配方 id。</summary>
    public const string TableRecipeId = "butcher_table";

    /// <summary>库存里那件"简易宰杀点"的物品 key（配方产物；摆放时扣一件）。</summary>
    public const string PointItemKey = "butcher_point";

    /// <summary>库存里那件"宰杀台"的物品 key。</summary>
    public const string TableItemKey = "butcher_table";

    /// <summary>家具类型名（简易宰杀点）。</summary>
    public const string PointFurnitureKey = "简易宰杀点";

    /// <summary>家具类型名（宰杀台）。</summary>
    public const string TableFurnitureKey = "宰杀台";

    /// <summary>
    /// 制作者门槛键：<b>营地里还没有宰杀设施</b>才做得出简易宰杀点（一座就够，同 <see cref="CookStation.AbsentGate"/>）。
    /// 判定委托营地层（<c>CampMain</c> 的 gate 解析）。
    /// </summary>
    public const string AbsentGate = "butcher_absent";

    /// <summary>
    /// 制作者门槛键：<b>营地里已有简易宰杀点、且还没升级过</b>才做得出宰杀台。
    /// 「升级」在本项目里没有独立机制轴 ⇒ <b>用既有的 gate 表达</b>：宰杀台是一条**要求前置设施在场**的配方，
    /// 造出来落位时把简易宰杀点顶掉（消费层做）。<b>不为"升级"新开一条引擎轴。</b>
    /// </summary>
    public const string UpgradeGate = "butcher_point_present";

    /// <summary>简易宰杀点的物品描述。</summary>
    public const string PointItemDescription =
        "一块钉在墙上的板子，一个钩子。它谈不上讲究，但它让一只老鼠变成两样东西——肉，和一小块皮。";

    /// <summary>宰杀台的物品描述。</summary>
    public const string TableItemDescription =
        "有槽有沿的正经案子，血水顺着槽流走。手上快了，也就没那么难熬了——熟练是这世道给人的唯一一点慈悲。";

    /// <summary>占地（世界像素，拟定待调）。</summary>
    public const float Width = 56f;
    public const float Height = 40f;

    /// <summary><b>实心</b>：它是一张钉死的案子（同烹饪台，不可跨越）。</summary>
    public const bool IsSolid = true;

    /// <summary>放置规格（<b>室内</b>：案子在屋里，同烹饪台；守 64px 禁建带）。</summary>
    public static readonly PlaceableSpec PointPlaceSpec =
        new(PointFurnitureKey, Width, Height, IsSolid: IsSolid);

    /// <summary>宰杀台的放置规格（同上，只是名字不同）。</summary>
    public static readonly PlaceableSpec TablePlaceSpec =
        new(TableFurnitureKey, Width, Height, IsSolid: IsSolid);

    /// <summary>某一档设施的家具名。</summary>
    public static string FurnitureKeyOf(ButcherTier tier) => tier switch
    {
        ButcherTier.SimplePoint => PointFurnitureKey,
        ButcherTier.Table => TableFurnitureKey,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>某一档设施的库存物品 key。</summary>
    public static string ItemKeyOf(ButcherTier tier) => tier switch
    {
        ButcherTier.SimplePoint => PointItemKey,
        ButcherTier.Table => TableItemKey,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>能装进槽位的那把刀的<b>武器显示名</b>（武器表是中文名主键，见 <c>WeaponTable</c>）。</summary>
    public static string WeaponNameOf(ButcherKnife knife) => knife switch
    {
        ButcherKnife.Dagger => "匕首",
        ButcherKnife.BoneKnife => "骨刀",
        _ => string.Empty,
    };

    /// <summary>反查：这把武器能不能上案板（不是匕首也不是骨刀 ⇒ <see cref="ButcherKnife.None"/>）。</summary>
    /// <remarks>
    /// ⚠️ <b>白名单，不是"凡是刃器都行"</b>：用户点名的就这两把。别顺手把短剑/砍刀/军刺放进来——那是引申。
    /// </remarks>
    public static ButcherKnife KnifeOf(string? weaponName) => weaponName switch
    {
        "匕首" => ButcherKnife.Dagger,
        "骨刀" => ButcherKnife.BoneKnife,
        _ => ButcherKnife.None,
    };
}

/// <summary>一条宰杀配方的基础产出（主料 + 副产物；<b>可能因宰杀台的双倍产出而翻倍</b>）。</summary>
public readonly record struct ButcherRecipe(
    string Id,
    ButcherTier Tier,
    string QuarryKey,
    string QuarryName,
    string MeatKey,
    int MeatQuantity,
    string ByproductKey,
    int ByproductQuantity);

/// <summary>宰杀一次的结算产出（主料 + 副产物；<b>可能因宰杀台的双倍产出而翻倍</b>）。</summary>
/// <param name="MeatKey">肉的材料键（老鼠肉 / 兔子肉 / 鸟肉）。</param>
/// <param name="MeatQuantity">肉的数量。</param>
/// <param name="ByproductKey">副产物材料键（碎皮革 / 羽毛）。</param>
/// <param name="ByproductQuantity">副产物数量。</param>
/// <param name="Doubled">这一刀有没有掷中宰杀台的双倍产出概率（当前值以 Wiki 配置为准）。</param>
public readonly record struct ButcherYield(
    string MeatKey,
    int MeatQuantity,
    string ByproductKey,
    int ByproductQuantity,
    bool Doubled);

/// <summary>
/// <b>宰杀的判定与结算</b>（纯函数；随机走可注入的 <see cref="IRandomSource"/>，测试用 <c>SequenceRandomSource</c> 复现）。
///
/// <para>═══ 🔴🔴 <b>速度加成是【加算】—— 这是对项目"百分比一律乘算"铁律的一条【显式例外】</b> ═══
/// <b>用户原话拍板</b>：设施与刀的速度贡献采用<b>加算</b>，不是乘算；当前加成与工时以 Wiki 配置为准。
///
/// <para>⚠️ <b>给下一个 agent</b>：CLAUDE.md 写着「百分比加成一律乘算，禁止加算」，你会很想"顺手修正"这里 ——
/// <b>别改。</b> 这是用户逐条问过之后<b>明确拍板的例外</b>（同类先例：耗子的搜刮速度加成）。
/// 乘算铁律真正要防的是<b>"没有手的人凭空获得 3% 操作能力"</b>那种**能力从零变正**的荒谬；
/// 而这里两项加成都是<b>设备与工具的独立贡献</b>（案子更顺手 + 刀更快），它们本来就该相加。
/// <b>测试钉死加算规则与最终工时换算，具体期望值以 Wiki 配置为准。</b></para>
///
/// <para>⚠️ <b>换算方向别搞反</b>：<b>实际耗时 = 基准耗时 ÷ 速度倍率</b>（<b>取倒数，不是减法</b>）。
/// 工时按速度倍率取倒数换算；若写成"减去速度加成"会得到错误结果。
/// （既有先例：<c>LootSession.EffectiveSecondsPerItem = baseSeconds / workEfficiency</c>，同一个模型）。</para></para>
/// </summary>
public static class ButcheryLogic
{
    /// <summary>基准宰杀工时（老鼠与鸟同价；当前值以 Wiki 配置为准）。</summary>
    public const int BaseMinutes = 60;

    /// <summary>宰杀台的速度加成（当前值以 Wiki 配置为准）。<b>加算</b>。</summary>
    public const double TableSpeedBonus = 0.50;

    /// <summary>匕首的速度加成（当前值以 Wiki 配置为准）。<b>加算</b>。</summary>
    public const double DaggerSpeedBonus = 0.50;

    /// <summary>骨刀的速度加成（当前值以 Wiki 配置为准）。<b>加算</b>。</summary>
    public const double BoneKnifeSpeedBonus = 0.25;

    /// <summary>宰杀台的双倍产出几率（当前值以 Wiki 配置为准）。简易宰杀点<b>没有</b>这一条。</summary>
    public const double TableDoubleYieldChance = 0.20;

    /// <summary>
    /// 宰杀配方清单：简易宰杀点沿用老鼠/鸟两条旧工序；宰杀台使用 Wiki 新给的三条基础产出。
    /// 声明顺序也是 Wiki 展示顺序，新条目只追加，不改已有随机流。
    /// </summary>
    private static readonly IReadOnlyList<ButcherRecipe> _recipes = new[]
    {
        new ButcherRecipe("simple_rat", ButcherTier.SimplePoint, "rat", "老鼠", Materials.RatMeatKey, 1, Materials.LeatherScrapKey, 1),
        new ButcherRecipe("simple_pigeon", ButcherTier.SimplePoint, "pigeon", "鸟", Materials.BirdMeatKey, 1, Materials.FeatherKey, 1),
        new ButcherRecipe("table_rat", ButcherTier.Table, "rat", "老鼠", Materials.RatMeatKey, 1, Materials.LeatherScrapKey, 2),
        new ButcherRecipe("table_rabbit", ButcherTier.Table, "rabbit", "兔子", Materials.RabbitMeatKey, 1, Materials.LeatherScrapKey, 3),
        new ButcherRecipe("table_pigeon", ButcherTier.Table, "pigeon", "鸟", Materials.BirdMeatKey, 1, Materials.FeatherKey, 1),
    };

    private static readonly IReadOnlyDictionary<(ButcherTier Tier, string QuarryKey), ButcherRecipe> _recipesByKey =
        _recipes.ToDictionary(r => (r.Tier, r.QuarryKey));

    /// <summary>供 Wiki 抽取器使用的宰杀配方清单。</summary>
    public static IReadOnlyList<ButcherRecipe> Recipes => _recipes;

    /// <summary>按设施档和猎物键找基础宰杀配方。</summary>
    public static ButcherRecipe? FindRecipe(ButcherTier tier, string? quarryKey)
        => quarryKey is not null && _recipesByKey.TryGetValue((tier, quarryKey), out ButcherRecipe recipe)
            ? recipe
            : null;

    /// <summary>这只东西在简易宰杀点能不能宰（默认查询保持旧 API 语义）。</summary>
    public static bool IsButcherable(string? quarryKey)
        => IsButcherable(ButcherTier.SimplePoint, quarryKey);

    /// <summary>这只东西在指定设施档能不能宰。</summary>
    public static bool IsButcherable(ButcherTier tier, string? quarryKey)
        => FindRecipe(tier, quarryKey) is not null;

    /// <summary>简易宰杀点可处理的猎物键（旧 API；面板应使用 <see cref="ButcherableKeysFor"/>）。</summary>
    public static IReadOnlyCollection<string> ButcherableKeys
        => _recipes.Where(r => r.Tier == ButcherTier.SimplePoint).Select(r => r.QuarryKey).ToArray();

    /// <summary>指定设施档可处理的猎物键（面板列表用）。</summary>
    public static IReadOnlyCollection<string> ButcherableKeysFor(ButcherTier tier)
        => _recipes.Where(r => r.Tier == tier).Select(r => r.QuarryKey).ToArray();

    /// <summary>某把刀的速度加成（空槽 = 0）。</summary>
    public static double SpeedBonusOf(ButcherKnife knife) => knife switch
    {
        ButcherKnife.Dagger => DaggerSpeedBonus,
        ButcherKnife.BoneKnife => BoneKnifeSpeedBonus,
        _ => 0.0,
    };

    /// <summary>某一档设施的速度加成（简易宰杀点无加成；其余当前值以 Wiki 配置为准）。</summary>
    public static double SpeedBonusOf(ButcherTier tier) => tier switch
    {
        ButcherTier.Table => TableSpeedBonus,
        _ => 0.0,
    };

    /// <summary>
    /// <b>速度倍率 = 1 + 设施加成 + 刀加成</b>（🔴 <b>加算</b>，用户拍板的例外，见类注）。
    /// <para>设施与刀的贡献相加；各组合的当前倍率以 Wiki 配置为准。</para>
    /// </summary>
    public static double SpeedMultiplier(ButcherTier tier, ButcherKnife knife)
        => 1.0 + SpeedBonusOf(tier) + SpeedBonusOf(knife);

    /// <summary>
    /// <b>一刀工时按基准工时 ÷ 速度倍率</b>（<b>取倒数</b>，见类注的换算方向警告；四舍五入到整分钟）。
    /// <para>各设施与刀的组合工时以 Wiki 配置为准；空槽仍不允许开工。</para>
    /// </summary>
    public static int MinutesFor(ButcherTier tier, ButcherKnife knife)
        => (int)Math.Round(BaseMinutes / SpeedMultiplier(tier, knife), MidpointRounding.AwayFromZero);

    /// <summary>
    /// <b>能不能开工</b>：设施在场 + <b>槽里有刀</b> + 这东西宰得了。
    /// <para>🔴 <b>没刀不许宰</b>（用户："一个槽位，可以放入匕首或者骨刀" ⇒ 那把刀不是加成，是<b>开工的前提</b>）。</para>
    /// </summary>
    public static bool CanButcher(ButcherKnife knife, string? quarryKey)
        => CanButcher(ButcherTier.SimplePoint, knife, quarryKey);

    /// <summary>指定设施档能不能开工。</summary>
    public static bool CanButcher(ButcherTier tier, ButcherKnife knife, string? quarryKey)
        => knife != ButcherKnife.None && IsButcherable(tier, quarryKey);

    /// <summary>
    /// <b>结算一刀</b>：出肉 + 副产物；<b>宰杀台</b>额外掷一次 Wiki 配置的<b>双倍产出</b>点（简易宰杀点<b>不掷点</b>，
    /// 随机流干净——测试算例按此写）。
    /// <para>宰不了（不在白名单 / 没刀）⇒ 返回 null，<b>一次点都不掷</b>。</para>
    /// </summary>
    public static ButcherYield? Resolve(ButcherTier tier, ButcherKnife knife, string? quarryKey, IRandomSource rng)
    {
        ButcherRecipe? recipe = FindRecipe(tier, quarryKey);
        if (!CanButcher(tier, knife, quarryKey) || recipe is null)
        {
            return null;
        }

        bool doubled = false;
        if (tier == ButcherTier.Table && rng is not null)
        {
            doubled = rng.Range(0.0, 1.0) < TableDoubleYieldChance;
        }

        int mult = doubled ? 2 : 1;
        return new ButcherYield(
            recipe.Value.MeatKey,
            recipe.Value.MeatQuantity * mult,
            recipe.Value.ByproductKey,
            recipe.Value.ByproductQuantity * mult,
            doubled);
    }

    /// <summary>
    /// <b>一只猎物的期望羽毛/碎皮革产出</b>（给经济分析与护栏测试用）：
    /// 基础产出与双倍产出概率按 Wiki 配置代入期望值公式。
    /// </summary>
    public static double ExpectedByproductPerQuarry(ButcherTier tier)
        => tier == ButcherTier.Table ? (1 * (1 - TableDoubleYieldChance) + 2 * TableDoubleYieldChance) : 1.0;

    /// <summary>指定猎物的期望副产物数量（基础产出 × 宰杀台双倍期望）。</summary>
    public static double ExpectedByproductPerQuarry(ButcherTier tier, string quarryKey)
    {
        ButcherRecipe? recipe = FindRecipe(tier, quarryKey);
        if (recipe is null) return 0.0;
        double multiplier = tier == ButcherTier.Table
            ? 1.0 + TableDoubleYieldChance
            : 1.0;
        return recipe.Value.ByproductQuantity * multiplier;
    }
}

/// <summary>
/// 宰杀设施那<b>一个刀槽</b>的装配态（用户："一个槽位，可以放入匕首或者骨刀"）。
///
/// <para>═══ <b>与烹饪台炊具槽同一条语义</b>（见 <see cref="CookStationState"/>）═══
/// 装槽 = <b>把刀从库存里拿走、钉在案板上</b>（消费层 <c>InstallKnife</c> 从 <see cref="InventoryStore"/> 扣一把该武器）；
/// 卸槽 = 还回库存（消费层 <c>RemoveKnife</c> 加回一把）。<b>只有一个槽</b>，装第二把会顶掉前一把（返还前一把）。
/// 装了哪把刀本身要进存档（刀已离库，不存就等于读档后既不在库、也不在案板上——凭空蒸发）。</para>
/// </summary>
public sealed class ButcherStationState
{
    /// <summary>案板上当前那把刀（<see cref="ButcherKnife.None"/> = 空槽）。</summary>
    public ButcherKnife Slotted { get; private set; } = ButcherKnife.None;

    /// <summary>槽里有没有刀（<b>没刀不许宰</b>，见 <see cref="ButcheryLogic.CanButcher"/>）。</summary>
    public bool HasKnife => Slotted != ButcherKnife.None;

    /// <summary>把一把刀钉上案板（幂等语义由消费层保证：装前先把旧刀返还库存）。返回被顶下来的旧刀（空槽则 None）。</summary>
    public ButcherKnife Install(ButcherKnife knife)
    {
        ButcherKnife prev = Slotted;
        Slotted = knife;
        return prev;
    }

    /// <summary>把刀从案板上取下来（还回库存由消费层做）。返回取下的那把刀（空槽则 None）。</summary>
    public ButcherKnife Remove()
    {
        ButcherKnife prev = Slotted;
        Slotted = ButcherKnife.None;
        return prev;
    }

    /// <summary>读档：直接置回存档里那把刀（不经库存——刀在存档里就"住在"案板上）。</summary>
    public void Restore(ButcherKnife knife) => Slotted = knife;
}

/// <summary>
/// <b>宰杀的运行时编排</b>（纯函数；随机走可注入的 <see cref="IRandomSource"/>）——镜像
/// <see cref="BirdTrapRuntime.ResolveCatch"/> / <see cref="CropPlotRuntime.HarvestRipe"/>：
/// <b>消费层与单测同一段代码</b>，拿真 <see cref="InventoryStore"/> 跑通两层。
///
/// <para>🔴 <b>为什么它必须存在</b>：<see cref="ButcheryLogic.Resolve"/> 只算"出什么"，<b>不碰库存</b>。
/// 消费层若各写各的"扣一只猎物、把肉塞进库存"，就会与单测分叉（"纯逻辑绿≠功能生效"的又一入口）。
/// 本类把"<b>扣 1 只猎物 → Resolve → 产物入库</b>"焊成一个原子步骤。</para>
/// </summary>
public static class ButcheryRuntime
{
    /// <summary>
    /// <b>宰一只</b>：从 <paramref name="inventory"/> 扣 1 只 <paramref name="quarryKey"/>，按设施档 + 刀
    /// 走 <see cref="ButcheryLogic.Resolve"/> 出肉 + 副产物（<b>宰杀台按 Wiki 配置掷双倍</b>），产物<b>逐样真入库</b>。
    /// <list type="bullet">
    /// <item>没刀 / 不在白名单 / 库里没有这只猎物 ⇒ 返回 null，<b>库存零变化、一次点都不掷</b>（随机流干净）。</item>
    /// <item>宰成了 ⇒ 返回本刀产出（含是否双倍），库存已同步扣猎物、加肉与副产物。</item>
    /// </list>
    /// </summary>
    public static ButcherYield? Butcher(
        ButcherTier tier, ButcherKnife knife, string? quarryKey, InventoryStore inventory, IRandomSource rng)
    {
        if (inventory is null
            || !ButcheryLogic.CanButcher(tier, knife, quarryKey)
            || quarryKey is null
            || inventory.MaterialCount(quarryKey) <= 0)
        {
            return null;   // 开不了工：不扣猎物、不掷点、不产出
        }

        // 先扣猎物（锁定这一只），扣不动就彻底作罢（并发兜底：MaterialCount 刚放行、TrySpend 却失败）。
        if (!inventory.TrySpendMaterial(quarryKey, 1))
        {
            return null;
        }

        ButcherYield? y = ButcheryLogic.Resolve(tier, knife, quarryKey, rng);
        if (y is null)
        {
            return null;   // 理论到不了（CanButcher 已放行）；真到了也不半吞——猎物已扣，如实返回空
        }

        AddMaterial(inventory, y.Value.MeatKey, y.Value.MeatQuantity);
        AddMaterial(inventory, y.Value.ByproductKey, y.Value.ByproductQuantity);
        return y;
    }

    private static void AddMaterial(InventoryStore inventory, string key, int quantity)
    {
        if (quantity <= 0 || Materials.Find(key) is not { } def)
        {
            return;
        }
        inventory.Add(def.ToItem(quantity));
    }
}
