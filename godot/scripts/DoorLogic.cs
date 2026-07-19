using DeadSignal.Combat; // Faction / IRandomSource（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 VisionLogic.cs / BreachLogic.cs / NoiseLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（门实体碰撞 / 挡视线 / 阻断寻路 / 开关门重烘焙导航 / AI 走过去开门）归 Godot 运行时层
//（CampMain.DoorInstance + Raider.TryOpenDoor），本文件只出**纯判定函数**。

/// <summary>门的三态。</summary>
public enum DoorState
{
    /// <summary>开着：不挡人、不挡视线、不断寻路。可被关上。</summary>
    Open,

    /// <summary>关着（没锁）：挡人 + 挡视线 + 断寻路。人推一下就开，丧尸只能砸。</summary>
    Closed,

    /// <summary>锁着：和「关着」一样挡，但推不开——要么撬（安静、慢、要铁丝），要么砸（快、很响）。</summary>
    Locked,

    /// <summary>
    /// <b>闩着</b>（自家的门：一根横木从**里面**插上）。用户拍板「营地大门要能闩上」。
    /// <para>
    /// <b>闩 ≠ 锁</b>，这是它单独成一态的全部理由：
    /// <list type="bullet">
    /// <item><b>自己人</b>（<see cref="Faction.Survivor"/>）：<b>一抬就开</b>——不用铁丝、不用撬，就是<b>普通的开门那一个动作</b>
    /// （呼应用户「开门只有一种动作」。要玩家拿铁丝撬自家大门是荒谬的）。</item>
    /// <item><b>劫掠者</b>：<b>推不开，也撬不了</b>——撬锁撬的是**锁芯**，而横木在门的**内侧**，撬锁的手艺在这儿没有用武之地。
    /// <b>只剩砸这一条路。</b></item>
    /// <item><b>丧尸</b>：一如既往，只会砸。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>它堵的是一个真实存在过的洞</b>：营地大门此前是「关着 + 没锁」，而劫掠者<b>会开门</b> ⇒
    /// 「关着 + 没锁 + 够得着 → 推开」三条全中 ⇒ <b>劫掠者直接推门进营，250HP 形同虚设</b>。
    /// 「自家大门靠『关着』说话」这句话，<b>对会拧门把手的敌人根本不成立</b>。
    /// 闩上之后 250HP 重新生效，而砸门声（180，<c>NoiseKind.Combat</c> 不分阵营）会把附近的丧尸一并招来——
    /// <b>攻方自己制造的动静反噬攻方</b>。这是设计意图。
    /// </para>
    /// </summary>
    Barred,
}

/// <summary>
/// 锁的档次（数据驱动：每扇门在 <c>camp.json</c> / 关卡数据里指定）。数值皆「拟定待调」。
/// </summary>
public enum LockTier
{
    /// <summary>没装锁（绝大多数门）。此档不进撬锁流程。</summary>
    None,

    /// <summary>简单锁：一把挂锁、一个弹子锁芯。撬它基本只是花点时间。</summary>
    Simple,

    /// <summary>普通锁：正经的门锁。撬得开，但你得有耐心，还得有铁丝。</summary>
    Standard,

    /// <summary>坚固锁：保险门、双锁芯。撬它是一场豪赌——期望要断掉三根铁丝、花掉半分钟。</summary>
    Sturdy,
}

/// <summary>一次撬锁尝试的结果。</summary>
/// <param name="Success">是否撬开。</param>
/// <param name="ToolBroken">铁丝是否断在锁里（失败即断，成功不损耗）。</param>
/// <param name="Seconds">本次尝试花掉的时间（无论成败都要花）。</param>
public readonly record struct DoorPickAttempt(bool Success, bool ToolBroken, double Seconds);

/// <summary>
/// 门的纯规则。用户拍板三条口径，本类逐条落地：
/// <list type="number">
/// <item><b>丧尸不会开门，只会砸</b>——门对丧尸就是一堵墙（<see cref="CanOperateDoors"/> 对 <see cref="Faction.Zombie"/> 恒 false，
/// 砸门整条链路复用既有破防系统 <see cref="BreachLogic"/>/<c>BreachController</c>，不新造 AI）。<b>劫掠者会正常开门。</b></item>
/// <item><b>门有锁，能撬 / 能砸</b>——撬锁（安静、慢、要铁丝）vs 砸开（快、很响）= 又一个<b>噪音 vs 效率</b>的取舍。</item>
/// <item><b>玩家开门只有一种动作</b>（不分"轻推"/"踹开"）——故只有 <see cref="NoiseLogic.DoorNoiseRadius"/> 一个开门噪音值，
/// 且 <see cref="NoiseOfOpening"/> <b>不接受任何"力度/模式"参数</b>：用签名把"两档"从源头挡住。</item>
/// </list>
///
/// <para>
/// <b>「阻挡」是一件事，不是三件事</b>（本系统的地基）：<see cref="Blocks"/> 一个判定同时决定「挡不挡人 / 挡不挡视线 /
/// 断不断寻路」。因为 Godot 层用**同一个墙层（0b0100）StaticBody2D + 同一条导航洞**承载这三者——
/// 碰撞天然挡人；<c>VisionOcclusion</c> 正是对墙层打 raycast，故自动挡视线；<c>BakeNavPoly</c> 把导航洞挖成障碍，
/// 故自动断寻路。开门 = 撤掉这个 body + 摘掉这条导航洞；关门 = 装回来。**没有第二套判定，也就没有第二处会漂移。**
/// </para>
///
/// <para>
/// <b>撬锁为什么挂"工具"而不是"技能"</b>：本项目的通用技能系统**已被删除**（见 <c>Recipe.cs</c>：「能力改由每角色
/// authored 专属效果与读过的书承载，配方门槛只看 工具/书/材料」）。撬锁遵循同一口径——门槛是**铁丝**
/// （<see cref="LockpickMaterialKey"/>，<c>Materials</c> 里已有的 <c>wire</c>，<b>不新造物品</b>）。
/// </para>
/// </summary>
public static class DoorLogic
{
    // ---------------- 谁能开门 ----------------

    /// <summary>
    /// 这个单位有没有"操作门"这回事。<b>丧尸恒 false</b>（用户拍板：不会开门，只会砸）；
    /// <b>动物恒 false</b>（狗没有手——布鲁斯是 <see cref="Faction.Survivor"/> 阵营，光看阵营会把它误判成能开门，
    /// 故 <paramref name="isAnimal"/> 必须是独立的一维）。
    /// 其余（幸存者 / 劫掠者 / 中立商人）都能开。
    /// </summary>
    public static bool CanOperateDoors(Faction faction, bool isAnimal)
        => faction != Faction.Zombie && !isAnimal;

    /// <summary>
    /// 能不能把这扇门推开。
    /// <list type="bullet">
    /// <item><b>关着（没锁）</b>：任何长着手的都推得开 —— <b>包括劫掠者</b>。这不是疏漏，是「关着」的定义。</item>
    /// <item><b>闩着</b>：<b>只有自己人</b>（<see cref="Faction.Survivor"/>）抬得起那根横木。劫掠者只能砸。见 <see cref="DoorState.Barred"/>。</item>
    /// <item><b>锁着</b>：谁都推不开（要撬或砸）。</item>
    /// </list>
    /// </summary>
    public static bool CanOpen(DoorState state, Faction faction, bool isAnimal)
    {
        if (!CanOperateDoors(faction, isAnimal))
        {
            return false;
        }
        return state switch
        {
            DoorState.Closed => true,                        // 没锁的门，有手就能推开（劫掠者也一样）
            DoorState.Barred => faction == Faction.Survivor, // 自家的门闩，只有自己人抬得起
            _ => false,                                      // 开着的没得开；锁着的要撬/砸
        };
    }

    /// <summary>能不能把这扇门关上：得会开门，且门正开着。</summary>
    public static bool CanClose(DoorState state, Faction faction, bool isAnimal)
        => CanOperateDoors(faction, isAnimal) && state == DoorState.Open;

    /// <summary>
    /// 这扇门「关上」之后**歇在哪个状态**。
    /// <para>
    /// <b>能闩的门（营地大门）关上的那一刻，门闩就落下</b> —— <b>不做单独的"闩门"交互</b>。
    /// 理由：用户拍板「玩家开门只有一种动作」，偏好简化；若把"关门"和"闩门"拆成两步，玩家迟早会忘了闩，
    /// 而"忘了闩"的后果（劫掠者推门直入）恰恰是这整套东西要堵的洞。<b>关门即闩门</b>，一个动作。
    /// </para>
    /// 民居的门没有闩，关上就只是关着。
    /// </summary>
    public static DoorState ClosedRestingState(bool barrable)
        => barrable ? DoorState.Barred : DoorState.Closed;

    // ---------------- 阻挡：挡人 / 挡视线 / 断寻路，同一个判定 ----------------

    /// <summary>
    /// 这扇门此刻挡不挡路。<b>关着和锁着都挡，只有开着不挡。</b>
    /// 见类注释：这一个 bool 同时决定碰撞 / 视线遮挡 / 导航阻断（Godot 层由同一个墙层 body + 同一条导航洞承载）。
    /// </summary>
    public static bool Blocks(DoorState state) => state != DoorState.Open;

    /// <summary>
    /// 这扇门此刻能不能被砸（＝能不能作为破防目标）。<b>恒等于 <see cref="Blocks"/></b>。
    /// <para>
    /// ⚠️ <b>这条恒等关系是护栏，不是巧合</b>：若开着的门仍算破防目标，袭营 AI 的择目标
    /// （<c>CampMain.NearestStructureByEdge</c> 按边缘距离取最近未毁结构）会挑中一扇**敞开的门**去砸——
    /// 敌人站在洞开的门口一下一下砸门框，而旁边就是能走过去的路。故 Godot 层必须让开着的门退出破防候选。
    /// </para>
    /// </summary>
    public static bool CanBash(DoorState state) => Blocks(state);

    // ---------------- 撬锁：铁丝 + 时间 + 运气 ----------------

    /// <summary>
    /// 撬锁工具 = <b>铁丝</b>（<c>Materials</c> 已有的 <c>wire</c>：「捆东西、设陷阱、修栅栏，居家末日必备」）。
    /// <b>不新造物品</b>——铁丝撬锁是文化通识，而它本来就在材料池里躺着。
    /// </summary>
    public const string LockpickMaterialKey = "wire";

    /// <summary>
    /// 能不能撬这把锁：得会开门 + 门锁着 + <b>身上有铁丝</b>。
    /// 没铁丝 = 撬不动（只剩砸这一条路——而砸很响，取舍就此成立）。
    /// </summary>
    public static bool CanPick(DoorState state, Faction faction, bool isAnimal, int lockpickCount)
        => CanOperateDoors(faction, isAnimal) && state == DoorState.Locked && lockpickCount > 0;

    /// <summary>
    /// 撬一次要花多久（秒）。具体耗时以 Wiki 配置为准，但<b>随锁的档次单调递增</b>是规则。
    /// <see cref="LockTier.None"/> = 没装锁，不进入撬锁流程，此处只钉边界。
    /// </summary>
    public static double PickSeconds(LockTier tier) => tier switch
    {
        LockTier.None     => 0,
        LockTier.Simple   => 4,
        LockTier.Standard => 6,
        LockTier.Sturdy   => 8,
        _ => 0,
    };

    /// <summary>
    /// 撬一次的成功率。具体概率以 Wiki 配置为准，但<b>随锁的档次单调递减、且恒 &gt; 0</b> 是规则——
    /// 再硬的锁也必须撬得开（成功率一旦为 0，玩家就会被一扇门永久卡死）。
    /// <para>
    /// <b>期望代价</b>由单次耗时、成功率和断丝概率共同决定；<b>砸永远更快，撬永远更静</b>——
    /// 这是用户要的那个取舍。具体比较值以 Wiki 配置和实机校准为准。
    /// </para>
    /// </summary>
    public static double PickChance(LockTier tier) => tier switch
    {
        LockTier.None     => 1.0,  // 没锁 = 必成（边界，防调用方误传 None 时死循环）
        LockTier.Simple   => 0.70,
        LockTier.Standard => 0.45,
        LockTier.Sturdy   => 0.25,
        _ => 1.0,
    };

    /// <summary>
    /// 撬一次锁。<b>无论成败都要花掉 <see cref="PickSeconds"/> 的时间</b>；
    /// <b>失败则铁丝断在锁里</b>（消耗 1 根），成功不损耗。
    /// 随机走可注入的 <see cref="IRandomSource"/>（项目铁律；测试用 <c>SequenceRandomSource</c> 复现）。
    /// </summary>
    public static DoorPickAttempt TryPick(LockTier tier, IRandomSource rng)
    {
        double roll = rng.Range(0, 1);
        bool success = roll < PickChance(tier);
        return new DoorPickAttempt(success, ToolBroken: !success, Seconds: PickSeconds(tier));
    }

    // ---------------- 噪音 ----------------

    /// <summary>
    /// 开门的噪音半径。<b>一个固定值，没有第二档</b>——用户拍板：玩家开门只有一种动作，
    /// <b>不分"轻推"和"踹开"</b>。故本函数**不接受任何力度/模式参数**：用签名把"两档"从源头挡住，
    /// 而不是靠注释提醒后人别加。
    /// </summary>
    public static double NoiseOfOpening() => NoiseLogic.DoorNoiseRadius;
}
