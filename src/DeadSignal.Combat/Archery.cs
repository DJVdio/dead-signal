namespace DeadSignal.Combat;

/// <summary>
/// 箭矢材料键。弓与弩共用弹药；具体材料项与武器数量以 Wiki 配置表为准。
/// <para>
/// 与 <see cref="AmmoKeys"/> 的关系：<c>AmmoKeys.Arrow</c>（"ammo_arrow"）是**类别键**——
/// <see cref="Weapon.AmmoKey"/> 填它，只表达"这把武器吃箭"；而下面是**具体材料键**，
/// 是真正躺在库存里、被制作/搜刮/扣减/回收的东西。箭允许一个类别对应多个具体材料——
/// 因为只有箭会**反过来改写武器的属性**（见 <see cref="Archery.Combine"/>）。
/// </para>
/// </summary>
public static class ArrowKeys
{
    /// <summary>削尖的木箭：最简陋的应急货。</summary>
    public const string SharpenedStick = "ammo_arrow_stick";

    /// <summary>自制箭：标准自制箭，作为比较基线。</summary>
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
/// <param name="DamageMult">伤害**上限**乘数（只改上限；下限由弓/弩各自的 wiki 值决定，箭不碰——「下限恒 1」已退役，见 <see cref="Archery.Combine"/>）。</param>
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
/// 箭矢目录。可配置数值以<b>本地 Wiki 数值表</b>（<c>docs/wiki</c>）『箭矢表』为准；
/// <b>wiki 是数值唯一设计源</b>，旧的 <c>docs/weapons-calc.xlsx</c> 已删除退役，见下方
/// [DECISION] impl-archery-redo）。
/// <para>
/// 设计意图（方向保留，数值不在代码注释中维护）：
/// <list type="bullet">
/// <item><b>削尖的木箭</b>——**便宜好用的主力箭**：整体弱一档，但不以"遇甲即废"作为代价，
///       具体制作门槛与属性以 Wiki 配置为准。</item>
/// <item><b>自制箭</b>——基线，读表时以它为原点。</item>
/// <item><b>重头箭</b>——破甲↑、射程↓、攻速↓，并以伤害与散布方向表达重箭取舍。具体系数以 Wiki 配置为准，定位是**专打披甲目标**。</item>
/// <item><b>碳纤维箭</b>——高阶属性且更准。它**不该有配方**：
///       工厂停工了，用一支少一支。稀缺是它唯一的代价，也是唯一需要的代价。</item>
/// </list>
/// </para>
/// </summary>
public static class ArrowTable
{
    /// <summary>
    /// 削尖的木箭：**便宜好用的主力箭**（用户手改后的定位）。整体弱一档但不致残，
    /// 代价集中在**射程**；且不额外拖慢出手。具体属性以 Wiki 配置为准。
    /// <para>
    /// ⚠ <b>它不再是"没箭了的应急货"</b>：属性调整以 Wiki 表为准；它的制作门槛与射程取舍承担主要代价。
    /// </para>
    /// </summary>
    public static ArrowDef SharpenedStick() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.SharpenedStick);

    /// <summary>自制箭：标准自制箭，作为全表比较基线。</summary>
    public static ArrowDef Handmade() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.Handmade);

    /// <summary>
    /// 重头箭：**破甲专精**。用户原话「破甲能力更高，但射程和攻速有所削弱」——三个方向一条不少。
    /// 具体系数、封顶值与制作成本以 Wiki 配置为准；Sim 结果只用于校准，不在引擎注释中保留快照。
    /// </summary>
    public static ArrowDef Heavy() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.Heavy);

    /// <summary>碳纤维箭：**不可制作**（无配方，只能搜刮）。四项全优 + 更准——稀缺是它唯一的代价。</summary>
    public static ArrowDef Carbon() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.Carbon);

    /// <summary>
    /// 全部箭（按低端→高端顺序：削尖的木箭 → 自制箭 → 重头箭 → 碳纤维箭）。
    /// <para>顺序由这里<b>显式钉死</b>（各工厂调用的排列），不依赖 archery.json 里 <c>Arrows</c> 字典的书写序——
    /// <c>PickCheapestAvailable</c> 的「优先用最差」语义靠这个顺序，不能被数据文件的排版改动破坏。</para>
    /// </summary>
    public static IReadOnlyList<ArrowDef> All => new[]
    {
        SharpenedStick(), Handmade(), Heavy(), Carbon(),
    };

    /// <summary>可制作的箭；碳纤维箭不在其中。</summary>
    public static IEnumerable<ArrowDef> Craftable() => All.Where(a => a.Craftable);

    /// <summary>按材料键查一种箭；查不到返回 <c>null</c>。</summary>
    public static ArrowDef? Find(string key)
        => key is null ? null : All.FirstOrDefault(a => a.Key == key);

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
/// <b>为什么是乘法</b>：弓的基础量级跨度很大。若用加法修正，同一支箭在不同弓上的**语义会变**。
/// 乘法则自动按弓的量级缩放：重头箭永远是"这把弓的破甲版"，不管这把弓多强。
/// </para>
/// <para>
/// <b>怎么防止叠乘失控</b>：穿透可能在叠乘后超过合理范围，等于无视护甲。
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
    /// <summary>穿透 clamp 上限，防止叠乘后超过合理范围。数值外置于 Wiki 配置。</summary>
    public static double MaxPenetration => CombatCatalog.Section<ArcheryConfig>().MaxPenetration;

    /// <summary>散布角 clamp 下限（度）。不许任何 弓×箭 组合变成"绝对精准"——弓总有手抖。<b>数值外置 archery.json</b>。</summary>
    public static double MinSpreadDegrees => CombatCatalog.Section<ArcheryConfig>().MinSpreadDegrees;

    // 弓弩伤害下限由 Wiki 配置决定。搭箭只改上限，下限原样保留 weapon.DamageMin。

    /// <summary>这把武器吃不吃箭（弓 / 弩）。判据＝弹药类别键，故新增弓弩无需改本函数。</summary>
    public static bool UsesArrows(Weapon weapon) =>
        weapon is not null && weapon.AmmoKey == AmmoKeys.Arrow;

    // ==================== 箭矢回收 ====================
    // 回收率由 Wiki 配置决定；读书状态选择对应配置项。
    // AmmoLogic.RollArrowRecovery 只负责按入参逐支掷点，不维护第二份数值。

    /// <summary>箭矢回收率·基础。数值外置于 Wiki 配置。</summary>
    public static double BaseArrowRecoveryRate => CombatCatalog.Section<ArcheryConfig>().BaseArrowRecoveryRate;

    /// <summary>箭矢回收率·读过《弓与箭之道》后。数值外置于 Wiki 配置。</summary>
    public static double SkilledArrowRecoveryRate => CombatCatalog.Section<ArcheryConfig>().SkilledArrowRecoveryRate;

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

    // ==================== 《弓与箭之道》的被动加成（用户在数值表『书籍』页写下） ====================
    //
    // 🔴 [T68] 用户把射程加成**换成了弹道速度加成**（不是加一条，是替换）。
    //    · 弹道速度是引擎新轴：`Weapon.FlightSpeed`，由本 Combine 连乘进有效武器。
    //    · 射程加成按用户意图删除（见 BookRangeMult 的配置项）——射手侧不再由本书提供射程加成。
    //    ⇒ 这本书提供弹道速度、散布、攻速和箭矢回收四项效果，具体值以 Wiki 配置为准。
    //
    // 用户原话（旧）是射程、散布、攻速三项；新口径以弹道速度替换射程，具体值以 Wiki 配置为准。
    //
    // **归属：射手，不是箭**。这三项是"人的本事"——射得远、射得准、抽箭快，与搭的是哪种箭无关。
    // 故它们不能写进 ArrowDef（那是箭的属性），而是作为 Combine 的一个**射手侧入参**参与连乘。
    // 与回收率完全同一条口径：引擎零依赖、看不见消费层的 ReadBookSet，故只吃一个 bool，
    // 由调用方（Actor.ResolveRangedShot / Projectile）从射手本人的已读书集里取。
    //
    // ⚠ **乘算，不是加算**（CLAUDE.md 铁律）：与箭的同轴系数一律**连乘**。
    //   [T68] 射程加成已中和（书不再改射程）；散布/攻速两项仍与箭的同轴系数连乘。

    /// <summary>
    /// 《弓与箭之道》·射程加成：🔴 [T68] 已中和为无效果。用户把射程改为弹道速度，具体状态以 Wiki 配置为准。
    /// <para>保留配置项是为了不动 <see cref="Combine"/> 的连乘管线，并让这条被有意中和的设计留痕。</para>
    /// </summary>
    public static double BookRangeMult => CombatCatalog.Section<ArcheryConfig>().BookRangeMult;   // [T68] 射程加成已删除，具体值外置 archery.json

    /// <summary>
    /// 《弓与箭之道》·弹道速度加成：🔴 [T68] 用户把原射程效果换成的那一条，具体值以 Wiki 配置为准。
    /// <para>作用轴＝逐武器 <see cref="Weapon.FlightSpeed"/>：读过书的射手射出的弹丸飞得更快、
    /// 出膛到命中的滞空更短。它是**射手的本事**（属于人，不属于箭），故与射程/散布/冷却三项一样作为 <see cref="Combine"/>
    /// 的射手侧入参连乘进有效武器的飞速。飞速是空间层弹道飞行属性，Sim 不建模弹道 ⇒ 本加成对 Sim 结算零影响。</para>
    /// </summary>
    public static double BookFlightSpeedMult => CombatCatalog.Section<ArcheryConfig>().BookFlightSpeedMult;

    /// <summary>《弓与箭之道》·锥形角（散布）加成：散布收窄即更准。仍受 <see cref="MinSpreadDegrees"/> 钳制。数值外置 archery.json。</summary>
    public static double BookSpreadMult => CombatCatalog.Section<ArcheryConfig>().BookSpreadMult;

    /// <summary>《弓与箭之道》·攻速加成：攻速＝每秒出手数，故它是**冷却的倒数**，见 <see cref="BookCooldownMult"/>。数值外置 archery.json。</summary>
    public static double BookAttackSpeedMult => CombatCatalog.Section<ArcheryConfig>().BookAttackSpeedMult;

    /// <summary>
    /// 攻速折算到出手间隔上的乘数＝<c>1 / BookAttackSpeedMult</c>（间隔变短）。
    /// **别把派生结果直接写死**：攻速加成与间隔减免不是同一件事，加成轴一律以"攻速"为准，间隔由它派生。
    /// <para><b>派生量，不入 archery.json</b>：由 <see cref="BookAttackSpeedMult"/>（外置）现算，避免两处真源打架。</para>
    /// </summary>
    public static double BookCooldownMult => 1.0 / BookAttackSpeedMult;

    /// <summary>
    /// 掷一次箭矢回收：<paramref name="arrowsFired"/> 支箭**各自独立** roll，返回捡回的支数。
    /// 粒度是**逐支**而非"一次射击整体判定"——射出去的是一支支箭，崩断与否本就该一支支算。
    /// （弓弩恒为单发单弹丸，故实战里每次调用 n=1；留 n 是为了让规则本身说得清楚，且便于测试。）
    /// </summary>
    public static int RollArrowRecovery(int arrowsFired, bool hasReadArcheryBook, IRandomSource rng) =>
        AmmoLogic.RollArrowRecovery(arrowsFired, ArrowRecoveryRate(hasReadArcheryBook), rng);

    /// <summary>
    /// **(弓/弩, 箭, 射手读没读过《弓与箭之道》) → 有效武器**。核心纯函数。
    /// <para>
    /// 修正的字段：伤害上限 / 穿透 / 射程（MaxRange 与 FalloffStart 等比缩放，整条衰减曲线一起挪）/
    /// 冷却 / 散布角 / [T68] 弹道速度（<see cref="Weapon.FlightSpeed"/>，仅读过《弓与箭之道》时生效，箭不参与）。
    /// **不改**的字段：伤害下限（原样保留弓/弩各自的 wiki 下限——「下限恒 1」机制已退役）、伤害类型、单双手、
    /// 末端衰减系数 <see cref="Weapon.FalloffFloor"/>（那是"箭还剩多少劲"的比例，与箭的种类无关）、
    /// 连发/弹丸数（弓弩恒 1）。
    /// </para>
    /// <para>
    /// <b>两层修正连乘</b>：箭的系数（<see cref="ArrowDef"/>）× 射手的书（<see cref="BookRangeMult"/> 等三项）。
    /// 同轴一律**乘算**（CLAUDE.md 铁律），钳制（穿透上限 / 散布下限）在**连乘之后只做一次**。
    /// 书只碰射程/散布/冷却三轴——伤害与穿透是箭头的事，读书读不出来。
    /// </para>
    /// </summary>
    /// <param name="weapon">弓 / 弩。**不吃箭的武器原样返回**（同一引用，零回归）。</param>
    /// <param name="arrow">搭上弦的那种箭；<c>null</c> 视为无箭修正（系数全 1）。</param>
    /// <param name="hasReadArcheryBook">
    /// 射手**本人**读完《弓与箭之道》没有（判据＝其 <c>ReadBookSet</c>，同 <see cref="ArrowRecoveryRate"/>）。
    /// 默认 <c>false</c> ＝ 原样：既有调用方（含 Sim 全部既有表）一个字节都不会漂。
    /// </param>
    public static Weapon Combine(Weapon weapon, ArrowDef? arrow, bool hasReadArcheryBook = false)
    {
        if (weapon is null || !UsesArrows(weapon) || (arrow is null && !hasReadArcheryBook))
        {
            return weapon!;   // 枪 / 近战 / 既没搭箭又没读书：一个字段都不碰。
        }

        // 箭的 5 个系数（没搭箭 → 全 1）。
        double damageMult = arrow?.DamageMult ?? 1.0;
        double penetrationMult = arrow?.PenetrationMult ?? 1.0;

        // 射手侧的书：只碰射程 / 散布 / 冷却三轴，没读过则全 1。**与箭的同轴系数连乘，不是加算。**
        double rangeMult = (arrow?.RangeMult ?? 1.0) * (hasReadArcheryBook ? BookRangeMult : 1.0);
        double cooldownMult = (arrow?.CooldownMult ?? 1.0) * (hasReadArcheryBook ? BookCooldownMult : 1.0);
        double spreadMult = (arrow?.SpreadMult ?? 1.0) * (hasReadArcheryBook ? BookSpreadMult : 1.0);

        // [T68] 弹道速度：飞速是**射手的本事**（属于人不属于箭），箭不参与——只由书连乘。
        double flightSpeedMult = hasReadArcheryBook ? BookFlightSpeedMult : 1.0;

        // 上限＝弓基础上限 × 箭伤系数；兜底不低于弓自己的下限（保证 Max ≥ Min，wiki 值下恒成立）。
        double maxDamage = Math.Max(weapon.DamageMin, weapon.DamageMax * damageMult);

        return new Weapon
        {
            Name = arrow is null ? weapon.Name : $"{weapon.Name}（{arrow.Name}）",
            Description = weapon.Description,

            // —— 被 箭 ⊗ 书 改写的 5 项 ——
            // 🔴 下限恒 1 机制退役：搭箭不改下限，原样保留弓/弩各自的 wiki 下限（见本类顶部 [DECISION]）。
            DamageMin = weapon.DamageMin,
            DamageMax = maxDamage,
            Penetration = Math.Clamp(weapon.Penetration * penetrationMult, 0, MaxPenetration),
            AttackInterval = weapon.AttackInterval * cooldownMult,
            BaseSpreadDegrees = Math.Max(MinSpreadDegrees, weapon.BaseSpreadDegrees * spreadMult),
            MaxRange = weapon.MaxRange * rangeMult,
            FalloffStart = weapon.FalloffStart * rangeMult,
            // [T68] 弹道速度：弓的飞速 × 书效果。搭箭不改飞速 ⇒ 换支箭不会让箭飞得更快。
            FlightSpeed = weapon.FlightSpeed * flightSpeedMult,

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
    /// 按材料键搭箭（便利重载）：查不到该键 → 当作没搭箭（书的三项加成仍照吃，它属于射手不属于箭）。
    /// </summary>
    public static Weapon Combine(Weapon weapon, string arrowKey, bool hasReadArcheryBook = false) =>
        Combine(weapon, ArrowTable.Find(arrowKey), hasReadArcheryBook);

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
