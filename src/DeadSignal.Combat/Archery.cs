namespace DeadSignal.Combat;

/// <summary>
/// 箭矢材料键（4 种）。**弓与弩共用弹药**（用户拍板），故这 4 个键同时供 5 把弓和 3 把弩使用。
/// <para>
/// 与 <see cref="AmmoKeys"/> 的关系：<c>AmmoKeys.Arrow</c>（"ammo_arrow"）是**类别键**——
/// <see cref="Weapon.AmmoKey"/> 填它，只表达"这把武器吃箭"；而下面 4 个是**具体材料键**，
/// 是真正躺在库存里、被制作/搜刮/扣减/回收的东西。子弹与霰弹是 1 类别 : 1 材料，只有箭是 **1 类别 : 4 材料**——
/// 因为只有箭会**反过来改写武器的属性**（见 <see cref="Archery.Combine"/>）。
/// </para>
/// </summary>
public static class ArrowKeys
{
    /// <summary>削尖的木箭：最简陋的应急货。</summary>
    public const string SharpenedStick = "ammo_arrow_stick";

    /// <summary>自制箭：标准自制箭，全表基线（修正系数全为 1）。</summary>
    public const string Handmade = "ammo_arrow_handmade";

    /// <summary>重头箭：破甲更高，但射程与攻速削弱（用户原话）。</summary>
    public const string Heavy = "ammo_arrow_heavy";

    /// <summary>碳纤维箭：全面优秀，**不可制作**（只能搜刮）——稀缺就是它的代价。</summary>
    public const string Carbon = "ammo_arrow_carbon";
}

/// <summary>
/// 一种箭的定义（不可变值对象）。**它不是一件武器，而是一组作用在弓/弩上的乘法修正系数**。
/// <para>
/// 系数一律是**乘数**，语义统一为「乘上去之后是什么样」：
/// <list type="bullet">
/// <item><see cref="DamageMult"/> / <see cref="PenetrationMult"/> / <see cref="RangeMult"/>：&gt;1 更好，&lt;1 更差。</item>
/// <item><see cref="CooldownMult"/> / <see cref="SpreadMult"/>：**&gt;1 更差**（冷却更长＝攻速更慢；散布角更大＝更不准）。</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Key">库存材料键（<see cref="ArrowKeys"/> 之一）。</param>
/// <param name="Name">中文显示名。</param>
/// <param name="Description">黑色幽默风味文案（玩家可见，不参与结算）。</param>
/// <param name="Craftable">是否可制作。<c>false</c> = 只能搜刮（碳纤维箭）。</param>
/// <param name="DamageMult">伤害**上限**乘数（下限恒为 1，见 <see cref="Archery.Combine"/>）。</param>
/// <param name="PenetrationMult">穿透乘数（结果 clamp 到 ≤ <see cref="Archery.MaxPenetration"/>）。</param>
/// <param name="RangeMult">射程乘数（同时作用于 <see cref="Weapon.MaxRange"/> 与 <see cref="Weapon.FalloffStart"/>，等比缩放整条衰减曲线）。</param>
/// <param name="CooldownMult">冷却乘数（&gt;1 = 出手更慢；重箭要多花力气搭上弦、拉满）。</param>
/// <param name="SpreadMult">散布角乘数（&gt;1 = 更不准）。</param>
public sealed record ArrowDef(
    string Key,
    string Name,
    string Description,
    bool Craftable,
    double DamageMult,
    double PenetrationMult,
    double RangeMult,
    double CooldownMult,
    double SpreadMult);

/// <summary>
/// 箭矢目录（4 种，**拟定待调**——用户会在 <c>docs/weapons-calc.xlsx</c>『箭矢表』里改）。
/// <para>
/// 设计意图（方向自洽，数值待调）：
/// <list type="bullet">
/// <item><b>削尖的木箭</b>——全面最差，但**极便宜**（木料 1 → 4 支，无工具槽无书门槛，开局即可做）。
///       它的存在意义是"没箭了也不至于打不响"，不是"用得起"。</item>
/// <item><b>自制箭</b>——基线，系数全 1。看表时以它为原点。</item>
/// <item><b>重头箭</b>——**用户唯一明确的一条**：破甲↑、射程↓、攻速↓。本表照办（穿透 ×1.45、射程 ×0.75、冷却 ×1.25），
///       并额外给伤害 ×1.20（重箭动能更高）与散布 ×1.15（初速低、弹道更弯）。**专打披甲目标**。</item>
/// <item><b>碳纤维箭</b>——四项全优（伤害/穿透/射程/攻速）且更准。它**不该有配方**：
///       工厂停工了，用一支少一支。稀缺是它唯一的代价，也是唯一需要的代价。</item>
/// </list>
/// </para>
/// </summary>
public static class ArrowTable
{
    /// <summary>削尖的木箭：应急货。伤害/穿透大幅下降、更不准；但**不额外拖慢出手**（它轻，不难拉）。</summary>
    public static ArrowDef SharpenedStick() => new(
        ArrowKeys.SharpenedStick,
        "削尖的木箭",
        "一根削尖的木棍，勉强算箭。它会飞，也会中，就是别指望它飞直，或者中得深。",
        Craftable: true,
        DamageMult: 0.70,       // 拟定待调
        PenetrationMult: 0.55,  // 拟定待调（没有箭簇，光靠一个尖——遇甲即废）
        RangeMult: 0.85,        // 拟定待调
        CooldownMult: 1.00,     // 拟定待调（轻，不拖慢）
        SpreadMult: 1.30);      // 拟定待调（不直、无尾羽 → 最散）

    /// <summary>自制箭：标准自制箭，**全表基线**（系数全 1，读表以它为原点）。</summary>
    public static ArrowDef Handmade() => new(
        ArrowKeys.Handmade,
        "自制箭",
        "木杆、铁头、布尾羽，手工出品。称不上精良，但每一支都长得一样——对一个弓手来说，这比精良更要紧。",
        Craftable: true,
        DamageMult: 1.00,
        PenetrationMult: 1.00,
        RangeMult: 1.00,
        CooldownMult: 1.00,
        SpreadMult: 1.00);

    /// <summary>
    /// 重头箭：**破甲专精**。用户原话「破甲能力更高，但射程和攻速有所削弱」——三个方向一条不少。
    /// <para>
    /// ⚠ <b>数值经 Sim 重标定过一轮，原因值得记下来</b>：初版取 伤害 ×1.20 / 冷却 ×1.25，
    /// Sim 实测它<b>在每一列都输给自制箭</b>（打重甲 −2~−4.5pp）——即"破甲专精"专不起来，成了纯劣化。
    /// 根因是本仓库的已知结论（<c>WeaponTable.ImprovisedHuntingGun</c> 注释里也写着同一件事）：
    /// <b>穿透是低杠杆轴</b>——能不能击穿甲层主要由<b>伤害骰</b>决定，穿透系数只压低防御骰的上限。
    /// 于是「破甲 ×1.45」几乎买不到胜率，而「冷却 ×1.25」是实打实的 −20% DPS，一加一减就成了净负。
    /// </para>
    /// <para>
    /// 修法是<b>把它的收益挪到吃得上劲的轴上</b>：伤害 ×1.20 → <b>×1.35</b>（灌铅的箭头本就该砸得更狠），
    /// 冷却惩罚 ×1.25 → <b>×1.15</b>（仍然更慢，用户口径不变）。用户的三个方向（破甲↑/射程↓/攻速↓）
    /// 一个都没动，动的只是幅度——数值本就"拟定待调"。它真正的代价落在 Sim <b>量不到</b>的两处：
    /// <b>射程 −25%</b>（空间层才兑现）与<b>造价</b>（吃金属锭，比自制箭的废金属贵一档）。
    /// </para>
    /// </summary>
    public static ArrowDef Heavy() => new(
        ArrowKeys.Heavy,
        "重头箭",
        "箭头灌了铅，沉得手腕发酸。飞不远，抬手也慢，但扎上去的时候，护甲的意见就不太重要了。",
        Craftable: true,
        DamageMult: 1.35,       // 拟定待调（Sim 重标定：1.20 → 1.35，见上）
        PenetrationMult: 1.45,  // 用户口径：破甲更高
        RangeMult: 0.75,        // 用户口径：射程削弱（代价主要落在这里，Sim 量不到）
        CooldownMult: 1.15,     // 用户口径：攻速削弱（Sim 重标定：1.25 → 1.15，见上）
        SpreadMult: 1.15);      // 拟定待调（初速低、弹道更弯）

    /// <summary>碳纤维箭：**不可制作**（无配方，只能搜刮）。四项全优 + 更准——稀缺是它唯一的代价。</summary>
    public static ArrowDef Carbon() => new(
        ArrowKeys.Carbon,
        "碳纤维箭",
        "碳纤维箭杆，笔直、轻盈、贵得离谱。工厂早就停工了，用一支少一支——所以射出去之后，你一定会回去把它捡起来。",
        Craftable: false,
        DamageMult: 1.25,       // 拟定待调
        PenetrationMult: 1.25,  // 拟定待调
        RangeMult: 1.20,        // 拟定待调
        CooldownMult: 0.90,     // 拟定待调（轻、直、好搭弦 → 出手更快）
        SpreadMult: 0.70);      // 拟定待调（工业级直度 → 最准）

    private static readonly IReadOnlyList<ArrowDef> _all = new[]
    {
        SharpenedStick(), Handmade(), Heavy(), Carbon(),
    };

    /// <summary>全部 4 种箭（按低端→高端顺序）。</summary>
    public static IReadOnlyList<ArrowDef> All => _all;

    /// <summary>可制作的箭（3 种；碳纤维箭不在其中）。</summary>
    public static IEnumerable<ArrowDef> Craftable() => _all.Where(a => a.Craftable);

    /// <summary>按材料键查一种箭；查不到返回 <c>null</c>。</summary>
    public static ArrowDef? Find(string key)
        => key is null ? null : _all.FirstOrDefault(a => a.Key == key);

    /// <summary>该材料键是不是一种箭。</summary>
    public static bool IsArrow(string key) => Find(key) is not null;
}

/// <summary>
/// **箭的类型反过来修改弓的属性**（用户拍板的核心机制）——纯函数，零依赖。
/// <para>
/// 最终属性 = 弓/弩的基础属性 ⊗ 箭的修正。<see cref="Combine"/> 给定 (弓, 箭) 造一把**有效武器**，
/// 之后的一切结算（<see cref="CombatResolver"/> / <see cref="Ballistics"/>）拿这把有效武器走**既有路径**，
/// 不需要任何新规则。空间侧（箭飞出去、落地能不能捡）归 Godot 实时层。
/// </para>
/// <para>
/// <b>为什么是乘法</b>：弓的基础量级跨度很大（短弓伤害上限 18，狩猎弓 38）。若用加法修正，
/// 「+6 破甲」在短弓上是翻倍、在狩猎弓上几乎没感觉——同一支箭在不同弓上的**语义会变**。
/// 乘法则自动按弓的量级缩放：重头箭永远是"这把弓的破甲版"，不管这把弓多强。
/// </para>
/// <para>
/// <b>怎么防止叠乘失控</b>：唯一会失控的是穿透（复合弩 68% × 重头箭 1.45 = 98.6%，等于无视一切护甲）。
/// 故穿透 clamp 到 <see cref="MaxPenetration"/>；散布 clamp 到 <see cref="MinSpreadDegrees"/>（不许出现绝对精准）。
/// 伤害/射程/冷却不设上限——它们的代价已经写在箭的其他系数里（重头箭拿伤害换射程与攻速），叠乘本身就是设计。
/// </para>
/// <para>
/// <b>零回归保证</b>：<see cref="Combine"/> 对**不吃箭的武器**（一切枪械与近战）**原样返回同一个对象引用**，
/// 一个字段都不碰。枪/近战根本不进这条路径，既有 Sim 基线不可能漂移。
/// </para>
/// </summary>
public static class Archery
{
    /// <summary>穿透 clamp 上限。挡住「复合弩 × 重头箭 = 98.6% 穿透」这种叠乘失控（那等于护甲系统对它不存在）。</summary>
    public const double MaxPenetration = 0.95;

    /// <summary>散布角 clamp 下限（度）。不许任何 弓×箭 组合变成"绝对精准"——弓总有手抖。</summary>
    public const double MinSpreadDegrees = 0.5;

    /// <summary>
    /// 伤害**下限**恒为此值，不吃箭的修正。理由：这是全仓的**近战锐器通则**（用户口径「刀可以轻划一下」）——
    /// 箭是"飞出去的刀"，斜面掠射、擦过划开一道口子是常态，再好的箭也擦得过去，再烂的箭也能划破皮。
    /// 箭的好坏体现在**上限**（能不能扎穿）而非下限。
    /// </summary>
    public const double DamageFloor = 1.0;

    /// <summary>这把武器吃不吃箭（弓 / 弩）。判据＝弹药类别键，故新增弓弩无需改本函数。</summary>
    public static bool UsesArrows(Weapon weapon) =>
        weapon is not null && weapon.AmmoKey == AmmoKeys.Arrow;

    // ==================== 箭矢回收（用户拍板） ====================
    //
    // 用户原话：「箭只有 25% 的几率不被损毁。如果读过《弓与箭之道》，则是 50% 的几率能回收。」
    //
    // **这两个数字定义了弓弩的整个身份**。25% ＝ 射出四支只捡回一支 —— 弓弩**不是免费远程**，
    // 它只是后勤压力小于枪（枪弹要子弹零件/火药，箭只要木料和一点金属，且至少还能捡回来一些）。
    // 读书后翻倍到 50%，于是《弓与箭之道》成了弓弩流的**硬前置**：不读它，你养不起一个弓手。
    //
    // **单一真源在这里**。`AmmoLogic.RollArrowRecovery(n, rate, rng)` 只是个通用掷点器（rate 是入参），
    // 具体用什么 rate 由本类说了算 —— 因为"读没读过书"是弓弩的规则，不是弹药系统的规则。

    /// <summary>箭矢回收率·基础：**25%**（用户拍板）。射出四支，捡回一支。</summary>
    public const double BaseArrowRecoveryRate = 0.25;

    /// <summary>箭矢回收率·读过《弓与箭之道》后：**50%**（用户拍板）。正好翻倍。</summary>
    public const double SkilledArrowRecoveryRate = 0.50;

    /// <summary>
    /// 这个射手的箭矢回收率。<paramref name="hasReadArcheryBook"/> ＝ 射手**本人**读完了《弓与箭之道》没有。
    /// <para>
    /// 引擎是零依赖纯 C#，看不见消费层的 <c>ReadBookSet</c>，故这里只吃一个 <c>bool</c>，
    /// 由调用方从读者的已读书集里取——**与 <c>MedicalBookPoints</c>（读过的医疗书→手术加点）
    /// 已经确立的模式完全一致**，不新造任何架构。
    /// </para>
    /// </summary>
    public static double ArrowRecoveryRate(bool hasReadArcheryBook) =>
        hasReadArcheryBook ? SkilledArrowRecoveryRate : BaseArrowRecoveryRate;

    /// <summary>
    /// 掷一次箭矢回收：<paramref name="arrowsFired"/> 支箭**各自独立** roll，返回捡回的支数。
    /// 粒度是**逐支**而非"一次射击整体判定"——射出去的是一支支箭，崩断与否本就该一支支算。
    /// （弓弩恒为单发单弹丸，故实战里每次调用 n=1；留 n 是为了让规则本身说得清楚，且便于测试。）
    /// </summary>
    public static int RollArrowRecovery(int arrowsFired, bool hasReadArcheryBook, IRandomSource rng) =>
        AmmoLogic.RollArrowRecovery(arrowsFired, ArrowRecoveryRate(hasReadArcheryBook), rng);

    /// <summary>
    /// **(弓/弩, 箭) → 有效武器**。核心纯函数。
    /// <para>
    /// 修正的 5 个字段：伤害上限 / 穿透 / 射程（MaxRange 与 FalloffStart 等比缩放，整条衰减曲线一起挪）/
    /// 冷却 / 散布角。**不改**的字段：伤害下限（恒 1，见 <see cref="DamageFloor"/>）、伤害类型、单双手、
    /// 末端衰减系数 <see cref="Weapon.FalloffFloor"/>（那是"箭还剩多少劲"的比例，与箭的种类无关）、
    /// 连发/弹丸数（弓弩恒 1）。
    /// </para>
    /// </summary>
    /// <param name="weapon">弓 / 弩。**不吃箭的武器原样返回**（同一引用，零回归）。</param>
    /// <param name="arrow">搭上弦的那种箭；<c>null</c> 视为无修正（原样返回）。</param>
    public static Weapon Combine(Weapon weapon, ArrowDef? arrow)
    {
        if (weapon is null || arrow is null || !UsesArrows(weapon))
        {
            return weapon!;   // 枪 / 近战 / 没搭箭：一个字段都不碰。
        }

        double maxDamage = Math.Max(DamageFloor, weapon.DamageMax * arrow.DamageMult);

        return new Weapon
        {
            Name = $"{weapon.Name}（{arrow.Name}）",
            Description = weapon.Description,

            // —— 被箭改写的 5 项 ——
            DamageMin = DamageFloor,
            DamageMax = maxDamage,
            Penetration = Math.Clamp(weapon.Penetration * arrow.PenetrationMult, 0, MaxPenetration),
            AttackInterval = weapon.AttackInterval * arrow.CooldownMult,
            BaseSpreadDegrees = Math.Max(MinSpreadDegrees, weapon.BaseSpreadDegrees * arrow.SpreadMult),
            MaxRange = weapon.MaxRange * arrow.RangeMult,
            FalloffStart = weapon.FalloffStart * arrow.RangeMult,

            // —— 原样继承（箭改不了的）——
            DamageType = weapon.DamageType,
            TwoHanded = weapon.TwoHanded,
            CanDualWield = weapon.CanDualWield,
            IsRanged = weapon.IsRanged,
            BurstCount = weapon.BurstCount,
            BurstInterval = weapon.BurstInterval,
            PelletCount = weapon.PelletCount,
            FalloffFloor = weapon.FalloffFloor,
            // 噪音：箭改不了弓弦的响度（换支箭不会让弓变安静），原样继承。
            // **漏抄这一条会让弓一搭上箭就变成完全无声**——潜行武器白送无敌属性。
            NoiseRadius = weapon.NoiseRadius,
            AmmoKey = weapon.AmmoKey,
            // AmmoPerAttack 不用抄：它是 BurstCount 的派生量（impl-ammo 的口径），BurstCount 已继承。
            // 枪托近战 profile：弓弩一律没有（贴脸即死），故无须继承——但仍照搬，
            // 以免将来给弩加了个"用弩身砸人"的 profile 时这里悄悄丢掉。
            StockMeleeDamageMin = weapon.StockMeleeDamageMin,
            StockMeleeDamageMax = weapon.StockMeleeDamageMax,
            StockMeleeInterval = weapon.StockMeleeInterval,
            StockMeleePenetration = weapon.StockMeleePenetration,
        };
    }

    /// <summary>
    /// 按材料键搭箭（便利重载）：查不到该键 → 当作没搭箭，原样返回。
    /// </summary>
    public static Weapon Combine(Weapon weapon, string arrowKey) =>
        Combine(weapon, ArrowTable.Find(arrowKey));

    /// <summary>
    /// 从库存余量里挑一种"当前打得出来的箭"（供 Godot 开火接线在玩家未显式选箭时兜底）。
    /// 口径：**优先用最差的**（<see cref="ArrowTable.All"/> 的声明顺序＝低端→高端）——
    /// 好箭要留着，这是每个弓手的本能，也省得玩家眼睁睁看着碳纤维箭被自动打光。
    /// 全空则返回 <c>null</c>（→ 调用方按 <see cref="AmmoLogic.PlanShot"/> 的空弹处理）。
    /// </summary>
    public static ArrowDef? PickCheapestAvailable(Func<string, int> countOf)
    {
        foreach (ArrowDef arrow in ArrowTable.All)
        {
            if (countOf(arrow.Key) > 0)
            {
                return arrow;
            }
        }

        return null;
    }
}
