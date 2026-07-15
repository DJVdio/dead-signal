using System;
using System.Collections.Generic;
using DeadSignal.Combat;   // IRandomSource（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型
//（与 CookingLogic.cs / TrapLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（把宰杀点立到场上、开面板、下单排队、把肉与皮塞进库存）归 Godot 消费层，本文件只出**规则 + 数值**。

// ═══════════════════════════════════════════════════════════════════════════════════════
// 【T67】宰杀 —— 用户拍板的一道**新工序**，卡在"猎物"和"饭"之间。
//
// 用户原话（规格，一字不改）：
//   「老鼠和鸟不能直接入锅了，而是要先宰杀。
//     新增简易宰杀点，可以用工作台制作（木材*1）。
//     简易宰杀点一个槽位，可以放入匕首或者骨刀。
//     宰杀老鼠->老鼠肉*1+碎皮革*1 1h
//     宰杀鸟->鸟肉*1+羽毛*1 1h
//     简易宰杀点可以升级为宰杀台（木板*3+钉子*4），一个槽位，可以放入匕首或者骨刀。
//     宰杀台+50%宰杀速度，并且有20%几率获得双倍产出。
//     匕首+50%宰杀速度，骨刀+25%宰杀速度」
//
// 🔴 **它把三样东西同时接上了**：
//   ① **羽毛的唯一来源** ⇒ 三种箭（用户已把它们全改成吃羽毛）从此有料可造 —— 这是整条弓箭线的源头。
//   ② **生皮的第一条生产线** ⇒ 核实过：`rawhide` 此前**零掉落、零配方产出**，只能找商人买。碎皮革缝起来就是它。
//   ③ 🔴 **骨刀终于有了存在的理由** ⇒ 它是全表最弱的武器（DPS 1.50，打重甲 0.46），
//      此前是个"造出来也没人拿"的摆设。现在它是**宰杀工具**（+25% 速度）——
//      **骨刀的定位从"废武器"变成了"工具"**，而它的配方门槛（《野外生存指南》，开局共享库存就有）
//      正好让它成为营地第一把能上案板的刀。
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>宰杀设施的两档（用户："简易宰杀点<b>可以升级为</b>宰杀台"）。</summary>
public enum ButcherTier
{
    /// <summary>简易宰杀点：一块板子加一个钩子。基准速度，无双倍产出。</summary>
    SimplePoint,

    /// <summary>宰杀台：+50% 宰杀速度，且 20% 几率双倍产出。</summary>
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
///       宰杀速度这条设计（+50%）的代价就消失了，槽位也就不再是一个选择。</item>
/// <item><b>它制造的取舍是真的</b>：营地往往<b>只有一把匕首</b>（DPS 2.353，全表最强的近战之一）。
///       把它钉在案板上，今晚站岗的人就得换别的家伙。<b>这正是「骨刀」的位置</b>：
///       骨刀慢一档（+25%），但没人会心疼一把 DPS 1.50 的刀——**它本来就不该上战场。**</item>
/// </list></para>
/// </summary>
public enum ButcherKnife
{
    /// <summary>槽位空着。<b>没刀就宰不了</b>（<see cref="ButcheryLogic.CanButcher"/>）——徒手撕不开一只老鼠。</summary>
    None,

    /// <summary>匕首（+50% 宰杀速度）。全表最好的宰杀刀，也是全表最好的近战刀之一——这就是那个取舍。</summary>
    Dagger,

    /// <summary>骨刀（+25% 宰杀速度）。慢一档，但你不会舍不得。</summary>
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

/// <summary>宰杀一次的产出（主料 + 副产物；<b>可能因宰杀台的双倍产出而翻倍</b>）。</summary>
/// <param name="MeatKey">肉的材料键（老鼠肉 / 鸟肉）。</param>
/// <param name="MeatQuantity">肉的数量。</param>
/// <param name="ByproductKey">副产物材料键（碎皮革 / 羽毛）。</param>
/// <param name="ByproductQuantity">副产物数量。</param>
/// <param name="Doubled">这一刀有没有掷中宰杀台的 20% 双倍产出。</param>
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
/// <b>用户原话拍板</b>：「<b>加算 ⇒ +100%（2 倍速，30 分钟）</b>」
/// ⇒ 宰杀台(+50%) + 匕首(+50%) = <b>+100%</b> ⇒ 速度倍率 <b>2.0</b> ⇒ 1h ÷ 2.0 = <b>30 分钟</b>。
/// <b>不是</b>乘算的 1.5 × 1.5 = 2.25（那会算出 26.7 分钟）。
///
/// <para>⚠️ <b>给下一个 agent</b>：CLAUDE.md 写着「百分比加成一律乘算，禁止加算」，你会很想"顺手修正"这里 ——
/// <b>别改。</b> 这是用户逐条问过之后<b>明确拍板的例外</b>（同类先例：耗子的搜刮速度加成）。
/// 乘算铁律真正要防的是<b>"没有手的人凭空获得 3% 操作能力"</b>那种**能力从零变正**的荒谬；
/// 而这里两项加成都是<b>设备与工具的独立贡献</b>（案子更顺手 + 刀更快），它们本来就该相加。
/// <b>有测试钉死「宰杀台 + 匕首 = 2 倍速 = 30 分钟」，改成乘算当场红。</b></para>
///
/// <para>⚠️ <b>换算方向别搞反</b>：<b>实际耗时 = 基准耗时 ÷ 速度倍率</b>（<b>取倒数，不是减法</b>）。
/// 60 分 ÷ 2.0 = 30 分。若写成"减 100% 时间"就会得到 0 分钟 —— 那正是这条恒等式要防的坑
/// （既有先例：<c>LootSession.EffectiveSecondsPerItem = baseSeconds / workEfficiency</c>，同一个模型）。</para></para>
/// </summary>
public static class ButcheryLogic
{
    /// <summary>基准宰杀工时（<b>用户给定</b>：1h = 60 游戏分钟；老鼠与鸟同价）。</summary>
    public const int BaseMinutes = 60;

    /// <summary>宰杀台的速度加成（<b>用户给定</b>：+50%）。<b>加算</b>。</summary>
    public const double TableSpeedBonus = 0.50;

    /// <summary>匕首的速度加成（<b>用户给定</b>：+50%）。<b>加算</b>。</summary>
    public const double DaggerSpeedBonus = 0.50;

    /// <summary>骨刀的速度加成（<b>用户给定</b>：+25%）。<b>加算</b>。</summary>
    public const double BoneKnifeSpeedBonus = 0.25;

    /// <summary>宰杀台的双倍产出几率（<b>用户给定</b>：20%）。简易宰杀点<b>没有</b>这一条。</summary>
    public const double TableDoubleYieldChance = 0.20;

    /// <summary>可被宰杀的猎物 → (肉, 副产物)。<b>用户给定的两条，一条不多</b>（兔子/鱼不在此列——用户没提，不引申）。</summary>
    private static readonly IReadOnlyDictionary<string, (string Meat, string Byproduct)> _table =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            // 宰杀老鼠 → 老鼠肉*1 + 碎皮革*1
            ["rat"] = (Materials.RatMeatKey, Materials.LeatherScrapKey),
            // 宰杀鸟 → 鸟肉*1 + 羽毛*1   ⚠️ 键仍是 pigeon（显示名已改「鸟」，见 Materials 的注释：改名不改键）
            ["pigeon"] = (Materials.BirdMeatKey, Materials.FeatherKey),
        };

    /// <summary>这只东西宰不宰得了（<b>只有老鼠和鸟</b>）。</summary>
    public static bool IsButcherable(string? quarryKey)
        => quarryKey is not null && _table.ContainsKey(quarryKey);

    /// <summary>全部可宰杀的猎物键（面板列表用）。</summary>
    public static IReadOnlyCollection<string> ButcherableKeys => (IReadOnlyCollection<string>)_table.Keys;

    /// <summary>某把刀的速度加成（空槽 = 0）。</summary>
    public static double SpeedBonusOf(ButcherKnife knife) => knife switch
    {
        ButcherKnife.Dagger => DaggerSpeedBonus,
        ButcherKnife.BoneKnife => BoneKnifeSpeedBonus,
        _ => 0.0,
    };

    /// <summary>某一档设施的速度加成（简易宰杀点 = 0，宰杀台 = +50%）。</summary>
    public static double SpeedBonusOf(ButcherTier tier) => tier switch
    {
        ButcherTier.Table => TableSpeedBonus,
        _ => 0.0,
    };

    /// <summary>
    /// <b>速度倍率 = 1 + 设施加成 + 刀加成</b>（🔴 <b>加算</b>，用户拍板的例外，见类注）。
    /// <para>简易+骨刀 = 1.25 · 简易+匕首 = 1.50 · 宰杀台+骨刀 = 1.75 · <b>宰杀台+匕首 = 2.00</b>（用户点名的那个数）。</para>
    /// </summary>
    public static double SpeedMultiplier(ButcherTier tier, ButcherKnife knife)
        => 1.0 + SpeedBonusOf(tier) + SpeedBonusOf(knife);

    /// <summary>
    /// <b>一刀要多少游戏分钟 = 60 ÷ 速度倍率</b>（<b>取倒数</b>，见类注的换算方向警告；四舍五入到整分钟）。
    /// <para>简易+骨刀 48 分 · 简易+匕首 40 分 · 宰杀台+骨刀 34 分 · <b>宰杀台+匕首 30 分</b>（用户点名）。</para>
    /// <para>空槽（<see cref="ButcherKnife.None"/>）⇒ 倍率 1.0 ⇒ 60 分，但 <see cref="CanButcher"/> 本来就不让你开工。</para>
    /// </summary>
    public static int MinutesFor(ButcherTier tier, ButcherKnife knife)
        => (int)Math.Round(BaseMinutes / SpeedMultiplier(tier, knife), MidpointRounding.AwayFromZero);

    /// <summary>
    /// <b>能不能开工</b>：设施在场 + <b>槽里有刀</b> + 这东西宰得了。
    /// <para>🔴 <b>没刀不许宰</b>（用户："一个槽位，可以放入匕首或者骨刀" ⇒ 那把刀不是加成，是<b>开工的前提</b>）。</para>
    /// </summary>
    public static bool CanButcher(ButcherKnife knife, string? quarryKey)
        => knife != ButcherKnife.None && IsButcherable(quarryKey);

    /// <summary>
    /// <b>结算一刀</b>：出肉 + 副产物；<b>宰杀台</b>额外掷一次 20% 的<b>双倍产出</b>点（简易宰杀点<b>不掷点</b>，
    /// 随机流干净——测试算例按此写）。
    /// <para>宰不了（不在白名单 / 没刀）⇒ 返回 null，<b>一次点都不掷</b>。</para>
    /// </summary>
    public static ButcherYield? Resolve(ButcherTier tier, ButcherKnife knife, string? quarryKey, IRandomSource rng)
    {
        if (!CanButcher(knife, quarryKey) || quarryKey is null || !_table.TryGetValue(quarryKey, out var pair))
        {
            return null;
        }

        bool doubled = false;
        if (tier == ButcherTier.Table && rng is not null)
        {
            doubled = rng.Range(0.0, 1.0) < TableDoubleYieldChance;
        }

        int mult = doubled ? 2 : 1;
        return new ButcherYield(pair.Meat, 1 * mult, pair.Byproduct, 1 * mult, doubled);
    }

    /// <summary>
    /// <b>一只猎物的期望羽毛/碎皮革产出</b>（给经济分析与护栏测试用）：
    /// 简易宰杀点 = 1.0；宰杀台 = 1 × 0.8 + 2 × 0.2 = <b>1.2</b>（+20%）。
    /// </summary>
    public static double ExpectedByproductPerQuarry(ButcherTier tier)
        => tier == ButcherTier.Table ? (1 * (1 - TableDoubleYieldChance) + 2 * TableDoubleYieldChance) : 1.0;
}
