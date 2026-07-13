using DeadSignal.Combat; // Weapon / Faction 判定所需的引擎类型（纯 C#，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 VisionLogic.cs / BreachLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 开火噪音：谁听得见、听见了做什么。空间侧（遍历场上 Actor、下达寻路指令）归 Godot 实时层
// （Actor.EmitNoise / Actor.HearNoise），本文件只出**纯判定函数**。

/// <summary>
/// 噪音的**类别**。它决定的只有一件事：<b>听者的「与噪音源敌对」那一条查不查</b>。
/// <para>
/// 用户拍板原话：「<b>区分：战斗声不分阵营，脚步声分</b>」。
/// </para>
/// <para>
/// ⚠️ <b>这个枚举的语义轴是"分不分阵营"，不是"是不是在打架"</b>。所以除了脚步，
/// **其余一切噪音都归 <see cref="Combat"/>**（开火 / 近战挥击 / 爪击 / 撕咬 / 枪托 / 砸门破防 / 开门 / 撬锁）。
/// </para>
/// </summary>
public enum NoiseKind
{
    /// <summary>
    /// <b>移动噪音（分阵营）</b>：只惊动**与噪音源敌对**的 AI。目前只有脚步声用它。
    /// <para>
    /// <b>为什么脚步必须分阵营 —— 这是抱团震荡护栏</b>：脚步是**持续行为**，每个 Actor 每走一个步幅就发一次。
    /// 若不分阵营，每只丧尸的每一步都会把周围闲着的丧尸吸过来，它们会**互相吸引、越滚越紧，
    /// 最后聚成一坨在原地抖**。这条线必须保住。
    /// </para>
    /// </summary>
    Movement,

    /// <summary>
    /// <b>战斗噪音（不分阵营）</b>：惊动半径内**所有**闲着的 AI，**不管敌对与否**。
    /// <para>
    /// 打斗的惨叫、撞击、撕咬、枪响——**谁听见都会过来看，不管是谁在打谁**。
    /// 这正是用户要的「<b>被围就真的会滚雪球</b>」：丧尸爪你的动静，同样会把别的丧尸引过来。
    /// </para>
    /// <para>
    /// ⚠️ "不分阵营" <b>不等于</b> "连玩家的人也一起吸" —— 听者仍必须是 AI
    /// （<see cref="Actor.RespondsToNoise"/>），玩家的 Pawn/Dog 永远不响应任何噪音。
    /// </para>
    /// </summary>
    Combat,
}

/// <summary>
/// 噪音的纯判定（用户拍板：<b>丧尸 + 劫掠者都听得到</b>，全量版）。
/// <para>
/// <b>噪音源不只有开火</b>（用户拍板：「走路会有较小的噪音、开门、近战会有一定的噪音」）。当前<b>五</b>类：
/// <b>走路</b>（<see cref="WalkNoiseRadius"/>，按 <see cref="StrideDue"/> 节流）、<b>挥砍/开火</b>
/// （每把武器一个 <see cref="Weapon.NoiseRadius"/>）、<b>砸门破防</b>（<see cref="BreachNoiseRadius"/>）、
/// <b>开门</b>（<see cref="DoorNoiseRadius"/>）、<b>撬锁</b>（<see cref="LockpickNoiseRadius"/>）。
/// 五类共用同一条通道：在噪音源位置发一次声 → 半径内的听者过 <see cref="ShouldInvestigateSquared"/> → 走过去看一眼。
/// <br/>后两类由**门系统**（<see cref="DoorLogic"/>，已立项落地）驱动，构成用户要的那个取舍：
/// <b>撬锁 30（安静、慢、要铁丝） vs 砸门 180（快、把全场招来）</b>。
/// </para>
/// <para>
/// <b>机制</b>：每次攻击在攻击者位置发出一次噪音（半径 = <see cref="Weapon.NoiseRadius"/>，默认 0 = 无声）。
/// 半径内、**当前没有攻击目标**的**敌对** Actor 会走过去**侦查一次** —— 复用既有的
/// 「丢失视野 → 前往最后目击点」通道（<c>Actor.UpdatePerception</c> 的 <c>_lastSeenPos</c>），
/// <b>不新造 AI 状态机、不改寻路</b>。
/// </para>
/// <para>
/// <b>为什么只唤醒"当前无目标"的</b>：已经在追人的敌人不该被一声枪响拽走注意力（那会变成
/// "开枪把追我的丧尸引开"的滑稽解法，且会破坏既有追击/破防行为）。噪音只负责**把闲着的敌人叫过来**，
/// 这正是"招怪"该有的语义。
/// </para>
/// <para>
/// <b>噪音不吃墙遮挡</b>：声音会绕、会穿墙——与吃遮挡判定的视线（<c>VisionLogic</c>）和气味
/// （<c>Zombie.SmellRadius</c>，复用遮挡判同房间）都不同。隔着一堵墙开枪，对面照样听得见。
/// </para>
/// </summary>
public static class NoiseLogic
{
    // ---- 行为噪音（用户拍板：「走路会有较小的噪音、开门、近战会有一定的噪音」）----
    // 噪音源不只有开火。武器的噪音在 WeaponTable（每把一个值）；**行为**的噪音是全局常量，放这里。
    // 数值拟定待调。梯度锚点见 Weapon.NoiseRadius 注释（丧尸嗅觉 70 / 丧尸夜视 219）。

    /// <summary>
    /// 走路噪音半径。**必须 &lt; 70（丧尸嗅觉兜底半径）** —— 否则玩家一迈腿就把周围全招来，地图寸步难行。
    /// 走路该是「贴着地板的低语」，不是招魂铃：40 意味着只有已经快贴到你脸上的敌人才听得见脚步。
    /// </summary>
    public const double WalkNoiseRadius = 40;

    /// <summary>
    /// 走路噪音的**节流步幅**：每累计走过这么多像素才发一次声（不是每帧发）。
    /// <para>
    /// <b>为什么按「距离」而不是「时间」节流</b>：按距离 ⇒ 走得慢自然发得少、站着不动完全不发，
    /// 且**自动随移速缩放**（跑起来脚步就密）——这既是正确的物理直觉，也顺手把"潜行慢走更安静"
    /// 免费做出来了。按时间节流做不到这一点（站着不动也会滴答响）。
    /// </para>
    /// 48px ≈ 一个身位的步幅；以基础移速 90px/s 计，约每 0.53 秒一次。
    /// </summary>
    public const double StrideDistance = 48;

    /// <summary>
    /// 丧尸嗅觉兜底半径 —— <b>整张噪音表的锚点</b>：任何"想悄悄干的事"，噪音都必须压在这条线以下，
    /// 否则你还没干完，门外那群东西就已经闻着味贴到脸上了。走路（40）、撬锁（30）皆据此定档。
    /// <para>单一真源：<c>Zombie.SmellSenseRadius</c> 转发到本常量（此前两处各写一个 70，且单测里还硬编码了第三个）。</para>
    /// </summary>
    public const double ZombieSmellRadius = 70;

    /// <summary>
    /// <b>开门</b>噪音半径。比走路响得多，但远不到枪的量级 —— 一扇旧门被推开时的吱呀与碰撞，
    /// 动静约等于抡一棍子（近战量级）。
    /// <para>
    /// <b>只有这一个值，没有第二档</b>：用户拍板「玩家开门只有一种动作，<b>不分轻推和踹开</b>」。
    /// 取值口径见 <see cref="DoorLogic.NoiseOfOpening"/>（该函数刻意**不收力度参数**，从签名上挡住两档化）。
    /// </para>
    /// <para>
    /// 玩法后果：<b>开门声（100）压过丧尸嗅觉（70）</b> —— 你推开一扇门，附近闲逛的东西**听得见**。
    /// 这正是"撬锁"存在的意义：撬（30）安静得没人理你，但慢；开着的门你可以大摇大摆走过去，代价是有人会转头看你。
    /// </para>
    /// </summary>
    public const double DoorNoiseRadius = 100;

    /// <summary>
    /// <b>撬锁</b>噪音半径 —— <b>全表最轻</b>，比走路（40）还轻。
    /// <para>
    /// <b>为什么必须比丧尸嗅觉（<see cref="ZombieSmellRadius"/> = 70）低得多</b>：这是撬锁机制**存在的全部理由**。
    /// 撬锁若能招来东西，它就只是"慢速版砸门"——又慢又要工具，还照样把人引来，没有任何人会选它。
    /// 定 30 意味着<b>撬锁本身惊动不了任何东西</b>：你唯一要担心的是走到门口那一路（走路 40），以及蹲在那儿撬的
    /// 那几十秒里、别人走过来撞见你。<b>撬锁买的是"寂静"，付的是"时间"</b> —— 这就是用户要的噪音 vs 效率取舍。
    /// </para>
    /// <para>对照另一端：<b>砸门 180</b>（<see cref="BreachNoiseRadius"/>），快，但把全场都招来。</para>
    /// </summary>
    public const double LockpickNoiseRadius = 30;

    /// <summary>
    /// <b>静默拆除</b>（围栏一格）的噪音半径 —— 比撬锁略响一点点，但<b>仍比走路（40）轻</b>。
    /// <para>
    /// 撬锁（30）是金属细碎刮擦；静默拆围栏是<b>撬开木板、压着声音把它放下</b>，动静自然大一档，
    /// 但仍然<b>远低于丧尸嗅觉（<see cref="ZombieSmellRadius"/> = 70）</b> —— 这是"静默"二字的<b>全部意义</b>：
    /// <b>它惊动不了任何东西</b>。若 ≥ 70，静默拆除就只是"又慢又招人的破坏"（180 只要 15 秒），
    /// <b>没有任何人（玩家或 AI）会选它</b>，机制当场作废。
    /// </para>
    /// <para>
    /// <b>玩家和 AI 用的是同一个数</b>（<see cref="SilentDismantleLogic"/>，对称性靠签名保证：
    /// 那些函数根本不接受"谁在拆"这个参数）。
    /// </para>
    /// </summary>
    public const double SilentDismantleNoiseRadius = 35;

    /// <summary>
    /// <b>砸门/砸围栏/砸墙（破防）</b>的噪音半径 —— 全表最响的**非枪**噪音。
    /// <para>
    /// <see cref="BreachController.TryBreach"/>：敌人到不了目标时，走到最近的围栏/大门/**门**前一下一下砸。
    /// 那是抡着家伙砸木头和铁皮的动静，理应比任何一次挥砍都大：<b>180</b> —— 压过最响的近战（破甲锤 150），
    /// 但仍远不到枪的量级（最轻的手枪 350）。
    /// </para>
    /// <para>
    /// <b>门系统落地后，这个值成了取舍的一端</b>：一扇锁着的门，撬（<see cref="LockpickNoiseRadius"/>=30，安静但慢）
    /// 还是砸（180，快但把半条街招来）？<b>丧尸没得选——它不会开门，只会砸</b>（用户拍板），所以每一次丧尸破门，
    /// 都在替你把更多东西喊过来。
    /// </para>
    /// <para>
    /// <b>玩法后果（免费得来的好东西）</b>：噪音只惊动<b>与噪音源敌对</b>的闲人 ⇒ 劫掠者砸你家大门时，
    /// 把附近闲逛的<b>丧尸</b>一并招了过来（三方敌对矩阵，见 <c>Factions</c>）。攻方自己制造的动静会反噬攻方——
    /// 你趴在屋里听着外面又是砸门声又是丧尸的动静，这一幕不需要任何额外代码。
    /// </para>
    /// </summary>
    public const double BreachNoiseRadius = 180;

    /// <summary>
    /// 狗（布鲁斯）的脚步噪音：比人轻得多。四只软肉垫、体重不到人的四分之一——
    /// 25px 意味着几乎只有踩到脸上才听得见。这让布鲁斯天然适合放出去探路。
    /// </summary>
    public const double DogWalkNoiseRadius = 25;

    /// <summary>
    /// 枪托贴脸砸人的噪音（钝器量级，≈棍棒）。
    /// <para>
    /// 上一版曾把它设成恒 0（无声）——**那是错的**：抡枪托砸人显然有动静。它是**近战**，就该按近战计。
    /// 由 <see cref="Weapon.MeleeProfile"/> 派生时写入（枪本体的大噪音**不**继承过来：砸不是打，没有枪声）。
    /// </para>
    /// 单一真源在引擎侧（<see cref="Weapon.StockMeleeNoise"/>），此处只是转发，防两处漂移。
    /// </summary>
    public const double StockMeleeNoiseRadius = Weapon.StockMeleeNoise;

    /// <summary>
    /// 走路噪音节流闸：把本帧走过的距离累加进 <paramref name="accumulated"/>；
    /// 攒够一个步幅就返回 true（并**扣掉一个步幅、保留余量**，不清零——否则跑起来会丢步）。
    /// 站着不动（<paramref name="moved"/>=0）恒返回 false。
    /// </summary>
    /// <param name="accumulated">该单位累计未发声的位移（跨帧保存，由调用方持有）。</param>
    /// <param name="moved">本帧走过的距离。</param>
    /// <param name="strideDistance">一个步幅（<see cref="StrideDistance"/>）。</param>
    public static bool StrideDue(ref double accumulated, double moved, double strideDistance)
    {
        if (strideDistance <= 0)
        {
            return false;
        }
        if (moved > 0)
        {
            accumulated += moved;
        }
        if (accumulated < strideDistance)
        {
            return false;
        }
        accumulated -= strideDistance; // 保留余量，快跑不丢步
        return true;
    }

    /// <summary>
    /// 这个听者会不会被这次噪音叫过来侦查。条件全真才动：
    /// <b>听者是 AI</b> / 活着 / 当前没有攻击目标 / <b>（仅移动噪音）与噪音源敌对</b> / 在噪音半径内（含边界）。
    /// </summary>
    /// <param name="kind">
    /// 噪音类别。决定**敌对那一条查不查**——见 <see cref="NoiseKind"/>。用户拍板：
    /// 「<b>战斗声不分阵营，脚步声分</b>」。
    /// </param>
    /// <param name="listenerRespondsToNoise">
    /// 听者是否**由噪音驱动**（即：是不是 AI）。**玩家操控的单位恒 false**——见
    /// <see cref="Actor.RespondsToNoise"/>。这一条是**硬安全阀**，不是可调平衡项，
    /// 且**跨噪音类别一律生效**（战斗噪音"不分阵营"绝不意味着玩家单位会被吸引）。
    /// </param>
    /// <param name="listenerAlive">听者是否存活（尸体不侦查）。</param>
    /// <param name="listenerHasTarget">听者当前是否已有攻击目标（有则不被拽走注意力，见类注释）。</param>
    /// <param name="hostileToSource">
    /// 听者是否与噪音源敌对。**只有 <see cref="NoiseKind.Movement"/> 会查这一条**；
    /// <see cref="NoiseKind.Combat"/> 完全无视它（打斗的动静谁听见都过来）。
    /// </param>
    /// <param name="distance">听者到噪音源的距离（世界单位，负值按 0 处理）。</param>
    /// <param name="noiseRadius">本次噪音半径（<see cref="Weapon.NoiseRadius"/>；≤0 = 无声，恒不触发）。</param>
    public static bool ShouldInvestigate(
        NoiseKind kind,
        bool listenerRespondsToNoise,
        bool listenerAlive,
        bool listenerHasTarget,
        bool hostileToSource,
        double distance,
        double noiseRadius)
    {
        double d = distance < 0 ? 0 : distance;
        return ShouldInvestigateSquared(
            kind, listenerRespondsToNoise, listenerAlive, listenerHasTarget, hostileToSource, d * d, noiseRadius);
    }

    /// <summary>
    /// <see cref="ShouldInvestigate"/> 的**平方距离**版，语义完全相同，是空间层每次广播实际调用的那个。
    /// <para>
    /// <b>为什么要有它</b>：走路噪音是**每个 Actor 持续产生**的（玩家 + 队友 + 全场敌人），广播频率远高于开火。
    /// 传平方距离让调用方用 <c>DistanceSquaredTo</c> 就够了，**整条热路径一次开方都不做**
    /// （<c>Vector2.DistanceTo</c> 每个听者一次 <c>sqrt</c>，在"每 Actor 每步 × 全场听者"的量级上不该付这个钱）。
    /// </para>
    /// 判定顺序也按**从便宜到贵**排：先几个 bool 短路，最后才比距离。
    /// </summary>
    /// <param name="distanceSquared">听者到噪音源的**距离平方**（负值按 0 处理）。</param>
    public static bool ShouldInvestigateSquared(
        NoiseKind kind,
        bool listenerRespondsToNoise,
        bool listenerAlive,
        bool listenerHasTarget,
        bool hostileToSource,
        double distanceSquared,
        double noiseRadius)
    {
        if (noiseRadius <= 0)
        {
            return false; // 无声（NoiseRadius 默认 0）：零回归，一个听者也不惊动
        }

        // ⚠️ 硬安全阀：**只有 AI 被噪音驱动**。玩家操控的单位（Pawn/Dog）绝不能被声音牵着走——
        // 噪音走的是 CommandMoveTo，那是**玩家下指令的同一条通道**，一旦让它对玩家单位生效，
        // 玩家的角色就会自己朝丧尸走过去（"角色中邪了"）。这一条必须排在最前面，
        // 且**跨噪音类别一律生效**：战斗噪音"不分阵营"不等于"连玩家的人也一起吸"。
        if (!listenerRespondsToNoise)
        {
            return false;
        }

        if (!listenerAlive || listenerHasTarget)
        {
            return false;
        }

        // ⭐ 用户拍板：「**战斗声不分阵营，脚步声分**」。
        // 只有**移动**噪音查敌对；**战斗**噪音完全无视阵营——打斗的惨叫、撞击、撕咬，
        // 谁听见都会过来看，不管是谁在打谁（丧尸爪你的动静，同样把别的丧尸引过来 ⇒ 被围就真的滚雪球）。
        if (kind == NoiseKind.Movement && !hostileToSource)
        {
            return false;
        }

        double d2 = distanceSquared < 0 ? 0 : distanceSquared;
        return d2 <= noiseRadius * noiseRadius;
    }

    /// <summary>
    /// 这把武器这次攻击发出多大噪音（开火 or 挥砍，同一个口子）。<c>null</c> 武器按无声处理。
    /// <para>
    /// 枪的 <see cref="Weapon.MeleeProfile"/>（贴脸枪托）派生时**不继承枪本体的大噪音**，改写
    /// <see cref="Weapon.StockMeleeNoise"/>（≈近战量级）——砸不是打，有动静但没枪声。
    /// </para>
    /// </summary>
    public static double NoiseOf(Weapon? weapon) =>
        weapon is null || weapon.NoiseRadius <= 0 ? 0 : weapon.NoiseRadius;
}
