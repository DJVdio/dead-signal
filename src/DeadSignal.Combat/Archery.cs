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
/// 箭矢目录（4 种，**拟定待调**——用户会在 <c>docs/weapons-calc.xlsx</c>『箭矢表』里改）。
/// <para>
/// 设计意图（方向自洽，数值待调）：
/// <list type="bullet">
/// <item><b>削尖的木箭</b>——**便宜好用的主力箭**（用户手改后的定位）：样样差一档但都不致残，
///       代价集中在**射程 ×0.75**（全表最短）。它**极便宜**（木料 1 → 4 支，无工具槽无书门槛，开局即可做），
///       是新营地唯一撑得起弓手的箭。<b>它曾经是"没箭了的应急货"（破甲 ×0.55 遇甲即废），用户把它扶正了。</b></item>
/// <item><b>自制箭</b>——基线，系数全 1。看表时以它为原点。</item>
/// <item><b>重头箭</b>——**用户唯一明确的一条**：破甲↑、射程↓、攻速↓。本表照办（穿透 ×1.75、射程 ×0.75、冷却 ×1.10），
///       并给伤害 ×1.25（重箭动能更高）与散布 ×1.25（初速低、弹道更弯）。**专打披甲目标**。</item>
/// <item><b>碳纤维箭</b>——四项全优（伤害/穿透/射程/攻速）且更准。它**不该有配方**：
///       工厂停工了，用一支少一支。稀缺是它唯一的代价，也是唯一需要的代价。</item>
/// </list>
/// </para>
/// </summary>
public static class ArrowTable
{
    /// <summary>
    /// 削尖的木箭：**便宜好用的主力箭**（用户手改后的定位）。四项各差一档但都不致残，
    /// 代价集中在**射程**（×0.75，全表最短）；且**不额外拖慢出手**（它轻，不难拉）。
    /// <para>
    /// ⚠ <b>它不再是"没箭了的应急货"</b>：用户在数值表里把伤害 0.70→<b>0.75</b>、破甲 0.55→<b>0.75</b>、
    /// 散布 1.30→<b>1.10</b> 一起上调，只把射程 0.85→<b>0.75</b> 压下去。原先「破甲 ×0.55 遇甲即废」的
    /// 惩罚被拿掉了——它是全表<b>唯一无工具槽、无书门槛、开局即可做</b>的箭，用户要它真的能用，
    /// 用射程而不是"废"来定价。
    /// </para>
    /// </summary>
    public static ArrowDef SharpenedStick() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.SharpenedStick);

    /// <summary>自制箭：标准自制箭，**全表基线**（系数全 1，读表以它为原点）。</summary>
    public static ArrowDef Handmade() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.Handmade);

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
    /// 修法是<b>把它的收益挪到吃得上劲的轴上</b>：伤害 ×1.20 → ×1.35，冷却惩罚 ×1.25 → <b>×1.15</b>（仍然更慢）。
    /// </para>
    /// <para>
    /// ⚠ <b>用户随后把伤害红利收回了一部分</b>（数值表手改：伤害 ×1.35 → <b>×1.25</b>、破甲 ×1.45 → <b>×1.50</b>）——
    /// 即"别拿伤害喂它，破甲专精就该在破甲轴上兑现"。上面那条"穿透是低杠杆轴"的观察依然成立，
    /// 所以那轮改动的净效果是<b>削弱重头箭</b>（伤害 −7.4%，换来几乎买不到胜率的 +3.4% 破甲）。
    /// <b>之后用户在 wiki 弹药表上又把这条路线走到底</b>：破甲 ×1.50 → <b>×1.75</b>、冷却惩罚 ×1.15 → <b>×1.10</b>
    /// （破甲再加码、攻速再松一档）。穿透仍是低杠杆轴、且过 <see cref="MaxPenetration"/>=0.95 封顶，
    /// 所以 ×1.75 在多数弓上买到的胜率依旧有限——这是用户明知代价的取向，别当平衡问题"修"回去。
    /// 它真正的代价仍落在 Sim <b>量不到</b>的两处：<b>射程 −25%</b>（空间层才兑现）与<b>造价</b>（吃铁 2，比自制箭的铁 1 贵一档）。
    /// </para>
    /// </summary>
    public static ArrowDef Heavy() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.Heavy);

    /// <summary>碳纤维箭：**不可制作**（无配方，只能搜刮）。四项全优 + 更准——稀缺是它唯一的代价。</summary>
    public static ArrowDef Carbon() => CombatCatalog.Section<ArcheryConfig>().Arrow(ArrowKeys.Carbon);

    /// <summary>
    /// 全部 4 种箭（按低端→高端顺序：削尖的木箭 → 自制箭 → 重头箭 → 碳纤维箭）。
    /// <para>顺序由这里<b>显式钉死</b>（各工厂调用的排列），不依赖 archery.json 里 <c>Arrows</c> 字典的书写序——
    /// <c>PickCheapestAvailable</c> 的「优先用最差」语义靠这个顺序，不能被数据文件的排版改动破坏。</para>
    /// </summary>
    public static IReadOnlyList<ArrowDef> All => new[]
    {
        SharpenedStick(), Handmade(), Heavy(), Carbon(),
    };

    /// <summary>可制作的箭（3 种；碳纤维箭不在其中）。</summary>
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
/// <b>为什么是乘法</b>：弓的基础量级跨度很大（短弓伤害上限 18，狩猎弓 38）。若用加法修正，
/// 「+6 破甲」在短弓上是翻倍、在狩猎弓上几乎没感觉——同一支箭在不同弓上的**语义会变**。
/// 乘法则自动按弓的量级缩放：重头箭永远是"这把弓的破甲版"，不管这把弓多强。
/// </para>
/// <para>
/// <b>怎么防止叠乘失控</b>：唯一会失控的是穿透（复合弩 68% × 重头箭 1.75 = 119%，等于无视一切护甲）。
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
    /// <summary>穿透 clamp 上限。挡住「复合弩 0.68 × 重头箭 1.75 = 1.19 穿透」这种叠乘失控（那等于护甲系统对它不存在）。<b>数值外置 archery.json</b>。</summary>
    public static double MaxPenetration => CombatCatalog.Section<ArcheryConfig>().MaxPenetration;

    /// <summary>散布角 clamp 下限（度）。不许任何 弓×箭 组合变成"绝对精准"——弓总有手抖。<b>数值外置 archery.json</b>。</summary>
    public static double MinSpreadDegrees => CombatCatalog.Section<ArcheryConfig>().MinSpreadDegrees;

    // 🔴 ==================== 「箭下限恒 1」机制已退役（用户拍板） ====================
    //
    // [DECISION] impl-archery-redo, journal 2026-07-15：**xlsx 已退役、以 wiki 为准**，
    // 且「弓弩伤害下限恒 1 / 近战锐器通则」这条**规则形态整条退役**。
    //
    // 从前这里有个 `DamageFloor = 1.0` 常量，理由是"箭是飞出去的刀，斜面掠射擦得过去，再烂的箭也能划破皮"，
    // 于是 Combine 把任何 弓×箭 的 DamageMin 一律拍回 1。现在弓弩按各自 wiki 下限（短弓 2 … 复合弩 12）——
    // 搭箭只改**上限**（DamageMult 作用于 DamageMax），下限**原样保留 weapon.DamageMin**，不再被拍回 1。
    // 护栏见 ArcheryTests.组合修正_搭箭后保留弓弩各自下限_不再拍回1 / 弓弩_伤害下限按各自wiki值_下限恒1通则已退役。

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

    /// <summary>箭矢回收率·基础：**25%**（用户拍板）。射出四支，捡回一支。<b>数值外置 archery.json</b>。</summary>
    public static double BaseArrowRecoveryRate => CombatCatalog.Section<ArcheryConfig>().BaseArrowRecoveryRate;

    /// <summary>箭矢回收率·读过《弓与箭之道》后：**50%**（用户拍板）。正好翻倍。<b>数值外置 archery.json</b>。</summary>
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
    // 🔴 [T68] 用户把「射程 +10%」**换成了「弹道速度 +20%」**（不是加一条，是替换）。
    //    · 「弹道速度 +20%」是**引擎新轴**——[T68] 已落地：`Weapon.FlightSpeed`（逐武器飞速，默认 560＝旧全局常量），
    //      读过本书的射手其射出弹丸飞速 ×1.2（见 BookFlightSpeedMult，由本 Combine 连乘进有效武器的 FlightSpeed）。
    //    · 「射程 +10%」按用户意图**删除**（见 BookRangeMult 现为 1.0＝无效果）——射手侧的射程加成不再由本书提供。
    //    ⇒ 现在这本书一共给四项：**弹道速度 +20%、锥形角 −10%、攻速 +2%、箭矢回收 25%→50%**（原「射程 +10%」被弹道速度取代）。
    //
    // 用户原话（旧）：「弓箭射程+10%，锥形角-10%，攻速+2%」；(新)：射程+10% → 弹道速度+20%。
    //
    // **归属：射手，不是箭**。这三项是"人的本事"——射得远、射得准、抽箭快，与搭的是哪种箭无关。
    // 故它们不能写进 ArrowDef（那是箭的属性），而是作为 Combine 的一个**射手侧入参**参与连乘。
    // 与回收率完全同一条口径：引擎零依赖、看不见消费层的 ReadBookSet，故只吃一个 bool，
    // 由调用方（Actor.ResolveRangedShot / Projectile）从射手本人的已读书集里取。
    //
    // ⚠ **乘算，不是加算**（CLAUDE.md 铁律）：与箭的同轴系数一律**连乘**。
    //   [T68] 射程加成已中和为 ×1.0（书不再改射程）；散布/攻速两项仍连乘：如 长弓 × 重头箭 × 书(散布 ×0.90)。

    /// <summary>
    /// 《弓与箭之道》·射程加成：🔴 [T68] **已中和为 1.0（无效果）**。用户把「射程 +10%」换成了「弹道速度 +20%」——
    /// 后者是引擎新轴（`Projectile.Speed` 常量），未落地、已挂起。前者按用户意图删除。
    /// <para>保留常量（值 1.0）而非删掉，是为了不动 <see cref="Combine"/> 的连乘管线 + 让"这条被有意中和"在代码里留痕；
    /// 弹道速度轴立项后，若用户仍要射程加成再改回即可。</para>
    /// </summary>
    public static double BookRangeMult => CombatCatalog.Section<ArcheryConfig>().BookRangeMult;   // [T68] 1.10 → 1.0（射程加成删除，用户换成弹道速度+20%）·数值外置 archery.json

    /// <summary>
    /// 《弓与箭之道》·弹道速度加成：🔴 [T68] **+20%（×1.2）**——用户把原「射程 +10%」换成的那一条。
    /// <para>作用轴＝逐武器 <see cref="Weapon.FlightSpeed"/>（默认 560＝旧全局常量）：读过书的射手射出的弹丸飞得更快、
    /// 出膛到命中的滞空更短。它是**射手的本事**（属于人，不属于箭），故与射程/散布/冷却三项一样作为 <see cref="Combine"/>
    /// 的射手侧入参连乘进有效武器的飞速。飞速是空间层弹道飞行属性，Sim 不建模弹道 ⇒ 本加成对 Sim 结算零影响。</para>
    /// </summary>
    public static double BookFlightSpeedMult => CombatCatalog.Section<ArcheryConfig>().BookFlightSpeedMult;

    /// <summary>《弓与箭之道》·锥形角（散布）加成：**−10%**（用户口径）——散布收窄即更准。仍受 <see cref="MinSpreadDegrees"/> 钳制。<b>数值外置 archery.json</b>。</summary>
    public static double BookSpreadMult => CombatCatalog.Section<ArcheryConfig>().BookSpreadMult;

    /// <summary>《弓与箭之道》·攻速加成：**+2%**（用户口径）。攻速＝每秒出手数，故它是**冷却的倒数**，见 <see cref="BookCooldownMult"/>。<b>数值外置 archery.json</b>。</summary>
    public static double BookAttackSpeedMult => CombatCatalog.Section<ArcheryConfig>().BookAttackSpeedMult;

    /// <summary>
    /// 攻速 +2% 折算到出手间隔上的乘数 ＝ 1 / 1.02 ≈ 0.9804（间隔变短）。
    /// **别把 0.98 直接写死**：攻速 +2% 与间隔 −2% 不是同一件事（1/1.02 = 0.98039…），
    /// 加成轴一律以"攻速"为准，间隔由它派生。
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
    /// 冷却 / 散布角 / [T68] 弹道速度（<see cref="Weapon.FlightSpeed"/>，仅读过《弓与箭之道》时 ×1.2，箭不参与）。
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

        // [T68] 弹道速度：飞速是**射手的本事**（属于人不属于箭），箭不参与——只由书连乘。读过书 ×1.2，否则原样继承弓的飞速。
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
            // [T68] 弹道速度：弓的飞速 × 书(读过 ×1.2)。搭箭不改飞速 ⇒ 换支箭不会让箭飞得更快。
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
