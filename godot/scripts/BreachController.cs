using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 袭营敌人「砸墙破防」的共用行为（Zombie/Raider 各持一个实例，在 Think 里调用 <see cref="TryBreach"/>）。
///
/// 背景：营地四面围栏围合、大门默认关闭（皆实心带 HP），门外生成的敌人**没有到营内幸存者的导航路径**。
/// 本控制器让敌人在被阻隔时走到最近的围栏/大门前、按攻击节奏砸墙；某段结构 HP 归零→导航开口→
/// 敌人下一次可达性探测即转为常规追击，穿破口涌入。
///
/// 分工：可达性判定走 <see cref="Actor.CanReach"/>（只读寻路查询）；几何/决策走纯逻辑 <see cref="BreachLogic"/>；
/// 「找最近结构 / 对结构施伤」是 <c>CampMain</c> 私有结构体系，经注入委托暴露（不外泄结构类型）。
/// 结构伤害不经 <see cref="Actor"/> 的战斗结算（结构非 Actor），故不动战斗定稿。
/// </summary>
public sealed class BreachController
{
    /// <summary>
    /// 认领一个「攻击位」：在 <paramref name="radius"/> 内挑一处**还站得下人**的未毁结构（按边缘距离就近），占位并输出其边缘点；
    /// 半径内全满则返回 false（该敌人挤不上，交回常规行为——在后面顶着，<b>不会绕路</b>）。
    /// <para>
    /// <b>这里的"还站得下人"就是本单的全部改动</b>（用户拍板「丧尸也会打围栏，不止会打门」）：此前它只是"取最近的那处"，
    /// 没有名额概念 ⇒ 一波丧尸全叠在同一扇门上砸。现在每处结构按可攻击面长度定名额（<see cref="BreachSlots.Capacity"/>），
    /// <b>挤不进门口的就去啃紧挨着门的那格围栏</b> —— 受攻击面从两扇门摊开到整条墙线。
    /// </para>
    /// </summary>
    public delegate bool FindNearestStructure(ulong attackerId, Vector2 from, float radius, out Vector2 edgePoint, out float edgeDistance);

    /// <summary>对该攻击者**当前占着的那处结构**施加 <paramref name="amount"/> 伤害，返回是否本击摧毁。</summary>
    /// <remarks>
    /// 打的是**认领的**那处，不是"脚下最近的"那处 —— 否则一只被挤到围栏边的丧尸会隔着 70px 的判定半径把伤害记到门上，
    /// 名额就白设了。伤害是<b>小数</b>（由武器派生，见 <see cref="StructureDamage"/>），全程不取整，遵精度通则。
    /// </remarks>
    public delegate bool DamageNearestStructure(ulong attackerId, Vector2 from, float radius, double amount);

    /// <summary>放开该攻击者占着的攻击位（墙破了转常规追击 / 停手 / 死亡）——位子空出来，后面挤着的那只顶上。</summary>
    public delegate void ReleaseBreachSlot(ulong attackerId);

    /// <summary>破防该不该整体停手（门刚开关过、导航尚未同步完）。见 <see cref="_suppress"/>。</summary>
    public delegate bool SuppressBreach();

    /// <summary>
    /// 「抡家伙之前，先试试有没有更文明的办法」。返回 true = 本帧已被替代方案接管（别砸了）。
    /// <para>
    /// <b>这是丧尸与劫掠者的唯一分野</b>（用户拍板）：<b>劫掠者</b>注入「去开门」——够得着的、关着没锁的门，
    /// 推开就是了；<b>丧尸</b>什么都不注入（<c>null</c>）⇒ 直接砸。同一个破防控制器，两种生物。
    /// </para>
    /// </summary>
    public delegate bool TryAlternativeToHammer();

    private const float SearchRadius = 320f;  // 门外找可砸结构的搜索半径（拟定待调）
    private const float DamageRadius = 72f;    // 砸墙时判定「贴着哪个结构」的半径（拟定待调）
    private const double ProbeInterval = 0.3;  // 可达性重算节流（避免每帧 A*；结构摧毁后最多 0.3s 转追击）

    private readonly FindNearestStructure _find;
    private readonly DamageNearestStructure _damage;
    private readonly ReleaseBreachSlot? _release;

    /// <summary>
    /// 破防整体停手闸（可空 = 从不停手，零回归）。
    /// <para>
    /// ⚠️ <b>存在理由：nav region 同步滞后</b>。门一开关，<c>CampMain</c> 会重烘焙导航，但 NavigationServer 要到
    /// 下一次同步才让新网格生效——这中间 <see cref="Actor.CanReach"/> 拿到的还是旧网格。若不管，就会出现
    /// <b>「劫掠者刚推开一扇门，同一帧却被告知还是走不通，于是转身开始砸这扇自己刚打开的门」</b>。
    /// 故 CampMain 在门开关后打一小段宽限期，期间本闸让所有破防 AI 停手（<see cref="TryBreach"/> 直接交回常规追击），
    /// 等导航同步完再判——那时门是通的，它自然就走过去了。
    /// </para>
    /// </summary>
    private readonly SuppressBreach? _suppress;

    /// <summary>被阻隔后、抡家伙之前的"文明解法"（劫掠者=开门；丧尸=null 直接砸）。见 <see cref="TryAlternativeToHammer"/>。</summary>
    private readonly TryAlternativeToHammer? _alternative;

    private readonly Vector2 _fallbackObjective; // 未侦测到幸存者时的可达性探测点（营心）

    /// <summary>
    /// 每次砸墙伤害。<b>由敌人手上的武器派生</b>（<see cref="StructureDamage.PerHit"/> = 砸墙有效武器平均伤害 × 砸墙系数），
    /// 不再是写死的常数——破甲锤砸墙就该比匕首狠，武器表改一个数字，围墙立刻感知。
    /// </summary>
    private readonly double _hitDamage;

    /// <summary>
    /// 砸墙节奏（秒/击）。同样由武器派生（<see cref="StructureDamage.Interval"/>）：<b>枪械取枪托间隔</b>，不是开火间隔——
    /// 敌人贴到墙上是抡枪托，不是对着承重墙扣扳机。
    /// </summary>
    private readonly double _hitCooldown;
    private readonly float _attackReach;         // 够得着结构的距离（≈近战边缘，不管远近程一律贴身砸）
    private readonly float _standoff;            // 贴近时停在结构外沿多远

    private double _hitTimer;
    private double _probeTimer;
    private bool _blocked;

    public BreachController(
        FindNearestStructure find,
        DamageNearestStructure damage,
        Vector2 fallbackObjective,
        double hitDamage,
        double hitCooldown,
        float attackReach,
        float standoff,
        SuppressBreach? suppress = null,
        TryAlternativeToHammer? alternative = null,
        ReleaseBreachSlot? release = null)
    {
        _find = find;
        _damage = damage;
        _release = release;
        _suppress = suppress;
        _alternative = alternative;
        _fallbackObjective = fallbackObjective;
        _hitDamage = hitDamage;
        _hitCooldown = hitCooldown;
        _attackReach = attackReach;
        _standoff = standoff;
    }

    /// <summary>
    /// 尝试接管本帧：若敌人到目标（幸存者，或营心）不可达（被结构阻隔）则走到最近结构前砸墙，返回 <c>true</c>
    /// （敌人应 return、不要再自行追击/游荡）；可达则返回 <c>false</c>（交回常规追击/游荡）。
    /// </summary>
    /// <param name="self">敌人自身。</param>
    /// <param name="delta">帧时长。</param>
    /// <param name="survivorPos">已侦测到的追击目标位置；无则传 null（改用营心探可达性）。</param>
    public bool TryBreach(Actor self, double delta, Vector2? survivorPos)
    {
        ulong id = self.GetInstanceId(); // 攻击位账本按敌人实例记名（谁占着哪堵墙）

        // 「近战被围栏挡住」：够得着人却打不出去（围栏射得穿、捅不穿）⇒ 必须去啃这道栏，不能站着空挥。
        // ⚠️ 这一条**不吃 _suppress、也不吃可达性**：它是**几何**判定（谁和谁之间隔着什么），
        // 与导航网格同步与否、绕不绕得过去都无关。大门开着时导航明明是通的，可丧尸不绕路——它就贴在这道栏上。
        bool meleeStalled = self.MeleeStalledByFence;

        // 门刚开关过、导航还没同步完 → 整体停手，交回常规追击（见 _suppress）。
        // 不这么做，敌人会去砸一扇已经敞开的门。
        if (!meleeStalled && _suppress != null && _suppress())
        {
            _blocked = false;
            _hitTimer = 0;
            _release?.Invoke(id);
            return false;
        }

        Vector2 probe = survivorPos ?? _fallbackObjective;

        _probeTimer -= delta;
        if (_probeTimer <= 0)
        {
            _probeTimer = ProbeInterval;
            _blocked = !self.CanReach(probe);
        }

        if (!BreachLogic.ShouldBreach(_blocked, meleeStalled))
        {
            _hitTimer = 0;
            _release?.Invoke(id); // 墙破了/绕得过去了：把攻击位让出来，后面挤着的顶上
            return false;         // 可达且没被栏卡住：交回常规追击/游荡
        }

        // 被阻隔了。**抡家伙之前，先试试更文明的办法**——劫掠者：能开的门就推开（用户拍板「劫掠者会正常开门」）。
        // 丧尸没注入这个（_alternative == null）⇒ 直接落到下面去砸。这一句就是丧尸与劫掠者的全部分野。
        // ⚠️ 顺序不能反：若先砸，劫掠者会把每一扇关着的门都砸开，与丧尸再无区别。
        if (_alternative != null && _alternative())
        {
            _hitTimer = 0;
            _release?.Invoke(id);
            return true; // 本帧在走向门 / 推门
        }

        // 开不了（门锁着 / 那根本不是门，是围栏）：认领一处**还站得下人**的围栏/大门/门。
        // 认领不到 = 面前的结构全被挤满了 ⇒ 不接管，让它在后面顶着（**不会绕路去别处找空位**——那是包抄，丧尸不干这事）。
        if (!_find(id, self.GlobalPosition, SearchRadius, out Vector2 edge, out float edgeDist))
        {
            return false;
        }

        if (BreachLogic.Decide(edgeDist, _attackReach) == BreachAction.Hammer)
        {
            // 够得着：站定砸墙。CancelOrders 清指令+零速，敌人贴墙不动、按节奏施伤。
            self.CancelOrders();
            _hitTimer -= delta;
            if (_hitTimer <= 0)
            {
                _hitTimer = _hitCooldown;
                _damage(id, self.GlobalPosition, DamageRadius, _hitDamage);

                // 砸门噪音（用户拍板「开门…会有一定的噪音」在本仓唯一能落地的门交互）：
                // 砸木头铁皮的动静盖过任何一次挥砍。噪音只惊动**与噪音源敌对**的闲人 ⇒ 劫掠者砸你家大门时，
                // 顺手把附近闲逛的丧尸也招了过来（三方敌对矩阵）——攻方自己制造的动静会反噬攻方。
                self.EmitBreachNoise();

                // 摧毁后不必特判：下次可达性探测（≤0.3s）会转 false，敌人自然穿破口追击。
            }
        }
        else
        {
            // 够不着：寻路到结构外沿的攻击站位。
            (double ax, double ay) = BreachLogic.ApproachPoint(
                self.GlobalPosition.X, self.GlobalPosition.Y, edge.X, edge.Y, _standoff);
            self.CommandMoveTo(new Vector2((float)ax, (float)ay));
        }
        return true;
    }
}
