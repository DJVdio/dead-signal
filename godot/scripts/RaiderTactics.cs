using System;
using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Combat; // IRandomSource（可注入随机；测试用 SequenceRandomSource 复现）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 VisionLogic.cs / NoiseLogic.cs / BreachLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（采样候选点、raycast 判遮挡、寻路、开火）归 Godot 实时层（Raider.cs），本文件只出**纯判定函数**。

/// <summary>
/// 劫掠者战术姿态。<b>这是人类敌人与丧尸的根本分野</b>：丧尸只会直线冲上来（有意为之，别动），
/// 人类会绕、会躲、会跑、会喊人。
/// </summary>
public enum RaiderStance
{
    /// <summary>正面交战：逼近到射程内开打（基类既有行为）。近战劫掠者恒走这条。</summary>
    Engage,

    /// <summary>包抄：绕出目标的视野锥，从侧翼/背后接近。</summary>
    Flank,

    /// <summary>找掩体：躲到能断掉目标视线的位置，开火探头、被压制缩回。</summary>
    TakeCover,

    /// <summary>撤退：真的逃（跑向背向威胁的出口），不回头。</summary>
    Retreat,
}

/// <summary>掩体两态：缩回（不可见）/ 探头（可开火，同时暴露）。</summary>
public enum CoverPhase
{
    /// <summary>缩在掩体后：断视线，不可见也打不着。</summary>
    Hunker,

    /// <summary>探头：挪到掩体边缘的射击位，能打也会被打。</summary>
    Peek,
}

/// <summary>
/// 一个候选掩体点。<see cref="BreaksEnemySight"/> 与 <see cref="Reachable"/> 由 Godot 层
/// 用 <c>VisionOcclusion.IsOccluded</c>（视野系统的遮挡权威源，墙层 0b0100）实测填入 ——
/// <b>"掩体"的定义就是"敌人到该点的视线被墙挡住"</b>，纯逻辑不自造遮挡判据。
/// </summary>
/// <param name="Position">候选点（cartesian 世界坐标）。</param>
/// <param name="BreaksEnemySight">敌人 → 该点的视线是否被墙断掉（true 才算掩体）。</param>
/// <param name="Reachable">本单位 → 该点是否走得过去（直线不撞墙的廉价近似）。</param>
public readonly record struct CoverCandidate(Vector2 Position, bool BreaksEnemySight, bool Reachable);

/// <summary>
/// 战术决策的输入快照。由 <see cref="RaiderStance"/> 决策与四组子判定共用。
/// 数值来源：<see cref="EnemyFacing"/>/<see cref="EnemyConeHalfAngleDeg"/>/<see cref="EnemyConeRange"/>
/// 即目标当前的**视野锥**（<c>VisionLogic.ConeFor</c> 的产物）—— 包抄要绕的就是它。
/// </summary>
public readonly record struct TacticalSituation
{
    /// <summary>本单位位置。</summary>
    public Vector2 Self { get; init; }
    /// <summary>本单位血量比例 0~1。</summary>
    public double HealthFraction { get; init; }
    /// <summary>当前是否看得见敌人（无敌人时战术退化为基类的追击/游荡）。</summary>
    public bool HasVisibleEnemy { get; init; }
    /// <summary>敌人位置。</summary>
    public Vector2 EnemyPos { get; init; }
    /// <summary>敌人朝向（视野锥轴向，无需归一化）。</summary>
    public Vector2 EnemyFacing { get; init; }
    /// <summary>敌人视野锥半角（度）。</summary>
    public float EnemyConeHalfAngleDeg { get; init; }
    /// <summary>敌人视距。</summary>
    public float EnemyConeRange { get; init; }
    /// <summary>本单位当前看得见的敌对者数量（寡不敌众判定）。</summary>
    public int VisibleHostiles { get; init; }
    /// <summary>还活着的同阵营同伴数（**不含自己**）。</summary>
    public int AlliesAlive { get; init; }
    /// <summary>本单位在小队里的序号；<b>0 号正面压制（吸引火力），其余人包抄</b>——这就是"别全走一条路"。</summary>
    public int SquadIndex { get; init; }
    /// <summary>小队规模（**含自己**）。单兵（1）不包抄。</summary>
    public int SquadSize { get; init; }
    /// <summary>本单位武器有效射程。</summary>
    public float WeaponRange { get; init; }
    /// <summary>是否远程武器（近战劫掠者躲墙后没意义，只能冲）。</summary>
    public bool IsRanged { get; init; }
    /// <summary>已经开始撤退（一旦开逃就逃到底，防止在生死线附近抖动）。</summary>
    public bool RetreatCommitted { get; init; }
    /// <summary>包抄已完成（到位 或 已在敌人视野锥外）→ 不再绕，转打。</summary>
    public bool FlankDone { get; init; }
}

/// <summary>呼叫增援的输入快照。</summary>
public readonly record struct ReinforceSituation
{
    /// <summary>当前发现了敌人。</summary>
    public bool EnemySpotted { get; init; }
    /// <summary>看得见的敌对者数量。</summary>
    public int VisibleHostiles { get; init; }
    /// <summary>还活着的同伴数（不含自己）。</summary>
    public int AlliesAlive { get; init; }
    /// <summary>呼叫半径内、**当前没有攻击目标**的同伴数（没人可叫就别喊，省下冷却）。</summary>
    public int IdleAlliesInRange { get; init; }
    /// <summary>呼叫冷却剩余（秒）。</summary>
    public double CallCooldownRemaining { get; init; }
    /// <summary>本单位武器的噪音半径（枪 350~700 / 匕首 90~150）。</summary>
    public double WeaponNoiseRadius { get; init; }
    /// <summary>最近是否刚开过火（秒；见 <see cref="RaiderTacticsParams.NoiseCoversCallWindow"/>）。</summary>
    public double SinceLastShot { get; init; }
}

/// <summary>
/// 逃跑者身份钩子（**只出机制，不写剧情**）。逃出地图的劫掠者由 Godot 层发出此记录，
/// 剧情层（阵营/敌对矩阵 <c>Factions</c>）日后可订阅它来做"以后再来报复"。本类不含任何剧情内容。
/// </summary>
/// <param name="DisplayName">战斗日志显示名（"劫掠者"/"夜袭者"/具名者如克莉丝汀）。</param>
/// <param name="Day">逃走当天（游戏天数）。</param>
/// <param name="HealthFraction">逃走时的血量比例（伤得多重）。</param>
public readonly record struct RaiderEscape(string DisplayName, int Day, double HealthFraction);

/// <summary>
/// 战术参数（**全部"拟定待调"**，数据驱动：由 <c>godot/data/raider_tactics.json</c> 覆盖，代码只写规则）。
/// </summary>
public sealed record RaiderTacticsParams
{
    // ── 撤退 ────────────────────────────────────────────────────────
    /// <summary>血量比例低于此值 → 逃（伤重）。</summary>
    public double RetreatHealthFraction { get; init; } = 0.35;
    /// <summary>敌我比达到此倍数 → 逃（寡不敌众）。敌人数 ≥ (同伴+自己) × 本值。</summary>
    public double OutnumberedRatio { get; init; } = 2.0;
    /// <summary>同伴死光（只剩自己）时，看见几个敌人就逃。</summary>
    public int LoneSurvivorHostiles { get; init; } = 2;
    /// <summary>选逃跑出口时"绕远"的惩罚权重（越大越倾向就近的出口）。</summary>
    public float EscapeDetourWeight { get; init; } = 0.35f;

    // ── 掩体 ────────────────────────────────────────────────────────
    /// <summary>掩体搜索半径（只在身边找，不做全图搜索——这是性能红线）。</summary>
    public float CoverSearchRadius { get; init; } = 160f;
    /// <summary>每次搜索采样的候选点数（环形均布）。</summary>
    public int CoverSampleCount { get; init; } = 12;
    /// <summary>掩体离敌人不得近于此（贴脸墙角=送死）。</summary>
    public float CoverMinEnemyDistance { get; init; } = 70f;
    /// <summary>理想交战距离 = 武器射程 × 本系数（探头就能打着）。</summary>
    public float CoverIdealRangeFactor { get; init; } = 0.70f;
    /// <summary>掩体评分里"离自己远"的惩罚权重（越大越懒得跑远路）。</summary>
    public float CoverApproachWeight { get; init; } = 0.5f;
    /// <summary>掩体重搜的最小间隔（秒）——节流，防每帧全场搜索。</summary>
    public double CoverRecomputeInterval { get; init; } = 1.0;
    /// <summary>敌人挪动超过此距离 → 现掩体作废，重搜（缓存失效条件）。</summary>
    public float CoverEnemyMoveInvalidate { get; init; } = 64f;
    /// <summary>探头位移：从掩体点朝敌人方向挪出这么远开火。</summary>
    public float PeekOffset { get; init; } = 24f;
    /// <summary>攻击冷却剩余 ≤ 此值 → 提前探头（枪快好了）。</summary>
    public double PeekLeadTime { get; init; } = 0.35;
    /// <summary>挨打后缩回掩体的时长（被压制）。</summary>
    public double SuppressedHunkerTime { get; init; } = 1.2;

    // ── 包抄 ────────────────────────────────────────────────────────
    /// <summary>至少几人才分兵包抄（单兵不包抄，老老实实找掩体）。</summary>
    public int FlankMinSquad { get; init; } = 2;
    /// <summary>包抄目标角 = 敌人视锥半角 + 本余量（度）——绕出锥外的安全边。</summary>
    public float FlankMarginDeg { get; init; } = 25f;
    /// <summary>包抄角随机抖动上限（度，走 IRandomSource；防两人路线完全对称、可预测）。</summary>
    public float FlankJitterDeg { get; init; } = 15f;
    /// <summary>包抄环半径 = 武器射程 × 本系数（绕到侧后正好进入自己的射程）。</summary>
    public float FlankRadiusFactor { get; init; } = 0.85f;
    /// <summary>到包抄点多近算到位。</summary>
    public float FlankArrivalTolerance { get; init; } = 40f;
    /// <summary>敌人挪动超过此距离 → 包抄点作废，重算。</summary>
    public float FlankEnemyMoveInvalidate { get; init; } = 96f;

    // ── 增援 ────────────────────────────────────────────────────────
    /// <summary>呼叫半径（喊得到多远的同伴）。</summary>
    public float CallRadius { get; init; } = 520f;
    /// <summary>呼叫冷却（秒），防刷屏。</summary>
    public double CallCooldown { get; init; } = 8.0;
    /// <summary>武器噪音半径 ≥ 此值即算"够响"：<b>响的枪一开火，噪音系统就替它把人喊来了</b>，不必再显式呼叫。</summary>
    public double LoudWeaponNoiseRadius { get; init; } = 300.0;
    /// <summary>开火后多久之内算"枪声还在替我喊人"（秒）。</summary>
    public double NoiseCoversCallWindow { get; init; } = 3.0;
    /// <summary>
    /// <b>主动呼喊</b>的噪音半径。呼喊<b>就是一次噪音</b>（<see cref="NoiseKind.Combat"/>，走既有 <c>EmitNoiseAt</c> 通道）——
    /// <b>它唤醒的是已经在场、当前闲着的敌人，绝不凭空生成新敌人</b>。
    /// 必须 ≥ <see cref="LoudWeaponNoiseRadius"/>：否则拿匕首的喊一嗓子还不如开一枪，这机制就白做了。
    /// 代价：战斗噪音不分阵营 ⇒ <b>喊人也会把丧尸招来</b>。这是取舍，不是 bug。
    /// </summary>
    public double ShoutNoiseRadius { get; init; } = 500.0;

    /// <summary>默认参数（数据文件缺失/解析失败时的回落）。</summary>
    public static RaiderTacticsParams Default { get; } = new();
}

/// <summary>
/// 劫掠者战术纯逻辑：<b>包抄 / 找掩体 / 撤退 / 呼叫增援</b> 四种战术的判定。
/// <para>
/// <b>全部复用既有系统，不另造</b>：
/// <list type="bullet">
///   <item><b>包抄</b> = 构造一个让 <c>VisionLogic.CanSee(敌人, 敌人朝向, 该点, 敌人视锥)</c> 返回
///     <b>false</b> 的点 —— 绕出的正是视野系统里那个真实的视野锥。</item>
///   <item><b>掩体</b> = 一个让 <c>VisionOcclusion.IsOccluded(敌人, 该点)</c> 为 <b>true</b> 的点 ——
///     用的正是视野系统那条唯一权威的遮挡射线。</item>
///   <item><b>增援</b> = <b>优先由噪音系统天然实现</b>（<c>NoiseKind.Combat</c> 不分阵营 ⇒ 枪一响，半径内
///     所有闲着的 AI 都会被 <c>CommandMoveTo</c> 过来）。显式呼叫只补两个缺口：<b>武器不够响</b>（匕首）
///     与 <b>还没开枪</b>（潜行/包抄途中）。见 <see cref="ShouldCallReinforcements"/>。</item>
///   <item><b>撤退</b> 与逃跑者身份钩子 <see cref="RaiderEscape"/>（机制，不含剧情）。</item>
/// </list>
/// </para>
/// </summary>
public static class RaiderTactics
{
    private const float Epsilon = 1e-4f;
    private const float RadToDeg = 180f / MathF.PI;
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>本单位当前是否落在敌人的视野锥内（包抄的触发判据；直接复用视野系统的 CanSee）。</summary>
    public static bool IsInEnemyCone(in TacticalSituation s) =>
        VisionLogic.CanSee(
            s.EnemyPos, s.EnemyFacing, s.Self,
            new VisionLogic.VisionCone(s.EnemyConeRange, s.EnemyConeHalfAngleDeg), occluded: false);

    /// <summary>
    /// 四选一的姿态决策，优先级 <b>撤退 &gt; 包抄 &gt; 掩体 &gt; 正面</b>。
    /// 命都不要了还讲什么战术 → 撤退压过一切；能绕就先绕（绕到侧后再躲更好）；
    /// 近战/贴脸 → 只能正面（拿匕首躲墙后没意义，转身跑去躲墙是背对着人挨枪）。
    /// </summary>
    public static RaiderStance DecideStance(in TacticalSituation s, RaiderTacticsParams p)
    {
        if (ShouldRetreat(s, p))
        {
            return RaiderStance.Retreat;
        }
        if (!s.HasVisibleEnemy)
        {
            return RaiderStance.Engage; // 没敌人：交回基类的追击/游荡
        }
        if (ShouldFlank(s, p))
        {
            return RaiderStance.Flank;
        }
        if (!s.IsRanged)
        {
            return RaiderStance.Engage; // 近战：冲
        }
        if (Vector2.Distance(s.Self, s.EnemyPos) < p.CoverMinEnemyDistance)
        {
            return RaiderStance.Engage; // 已经贴脸：打，别转身
        }
        return RaiderStance.TakeCover;
    }

    /// <summary>
    /// 该不该逃。三条独立触发：<b>伤重</b>（哪怕没看见人也跑）、<b>同伴死光且还打不过</b>、<b>寡不敌众</b>。
    /// 一旦开逃 <see cref="TacticalSituation.RetreatCommitted"/> 就<b>逃到底</b>——不给"跑两步觉得又行了"的回头机会，
    /// 否则会在生死线附近来回抖动、变成原地转圈。
    /// </summary>
    public static bool ShouldRetreat(in TacticalSituation s, RaiderTacticsParams p)
    {
        if (s.RetreatCommitted)
        {
            return true;
        }
        if (s.HealthFraction <= p.RetreatHealthFraction)
        {
            return true;
        }
        if (!s.HasVisibleEnemy)
        {
            return false; // 满血又没看见人：没什么好跑的
        }
        if (s.AlliesAlive <= 0 && s.VisibleHostiles >= p.LoneSurvivorHostiles)
        {
            return true; // 只剩自己，对面还不止一个
        }
        return s.VisibleHostiles >= (s.AlliesAlive + 1) * p.OutnumberedRatio;
    }

    /// <summary>
    /// 该不该包抄。<b>分兵</b>：小队 0 号顶在正面吸引火力，1 号往后的才绕——这条就是"别全部走同一条路"。
    /// 单兵不包抄（一个人绕后路只是白白挨打）；已经绕出锥外了也不再绕（该打了）。
    /// </summary>
    public static bool ShouldFlank(in TacticalSituation s, RaiderTacticsParams p)
    {
        if (!s.HasVisibleEnemy || s.FlankDone)
        {
            return false;
        }
        if (s.SquadSize < p.FlankMinSquad || s.SquadIndex <= 0)
        {
            return false;
        }
        return IsInEnemyCone(s); // 只有还杵在人家正面时，绕才有意义
    }

    /// <summary>
    /// 包抄落点：把自己放到 <b>敌人视野锥外</b> 的一个点上。
    /// 目标方位角 = 敌人朝向 ± (视锥半角 + 安全余量 + 随机抖动)，落点半径 = 自己武器射程 × 系数
    /// （绕过去正好进入自己的射程）。<b>左右交替分兵</b>（奇数号走一边、偶数号走另一边），
    /// 抖动走 <see cref="IRandomSource"/>（可复现，且两次袭击路线不会一模一样）。
    /// </summary>
    public static Vector2 FlankPoint(in TacticalSituation s, RaiderTacticsParams p, IRandomSource rng)
    {
        float facingDeg = DegOf(s.EnemyFacing);
        int side = (s.SquadIndex % 2 == 1) ? +1 : -1;
        float jitter = (float)rng.Range(0, Math.Max(0, p.FlankJitterDeg));
        // 只往锥外加码（余量与抖动同号），绝不会把人抖回正面。
        float offsetDeg = s.EnemyConeHalfAngleDeg + p.FlankMarginDeg + jitter;
        float targetDeg = facingDeg + side * offsetDeg;

        float radius = MathF.Max(1f, s.WeaponRange * p.FlankRadiusFactor);
        return s.EnemyPos + UnitFromDeg(targetDeg) * radius;
    }

    /// <summary>
    /// 从候选里挑最佳掩体。<b>三道硬门槛</b>：必须真断掉敌人视线（<see cref="CoverCandidate.BreaksEnemySight"/>，
    /// 由 Godot 层的遮挡射线实测）、走得过去、且离敌人 <b>不太近也不太远</b>——
    /// 太近是躲在人家鼻子底下，太远则探头也打不着（躲了等于白躲）。
    /// 合格者按「离理想交战距离的偏差 + 自己要跑多远」打分取最优。无合格候选 → null（调用方回落正面交战）。
    /// </summary>
    public static Vector2? SelectCover(
        Vector2 self, Vector2 enemy, float weaponRange,
        IReadOnlyList<CoverCandidate> candidates, RaiderTacticsParams p)
    {
        float ideal = weaponRange * p.CoverIdealRangeFactor;
        Vector2? best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            CoverCandidate c = candidates[i];
            if (!c.BreaksEnemySight || !c.Reachable)
            {
                continue; // 不断视线的只是块空地，不是掩体
            }
            float toEnemy = Vector2.Distance(c.Position, enemy);
            if (toEnemy < p.CoverMinEnemyDistance || toEnemy > weaponRange)
            {
                continue;
            }
            float score = -MathF.Abs(toEnemy - ideal) - p.CoverApproachWeight * Vector2.Distance(c.Position, self);
            if (score > bestScore)
            {
                bestScore = score;
                best = c.Position;
            }
        }
        return best;
    }

    /// <summary>掩体点 → 探头射击位（朝敌人挪出 <see cref="RaiderTacticsParams.PeekOffset"/>）。</summary>
    public static Vector2 PeekPosition(Vector2 cover, Vector2 enemy, RaiderTacticsParams p)
    {
        Vector2 toEnemy = enemy - cover;
        float len = toEnemy.Length();
        if (len <= Epsilon)
        {
            return cover;
        }
        return cover + (toEnemy / len) * p.PeekOffset;
    }

    /// <summary>
    /// 探头还是缩回。<b>被压制（刚挨过打）恒缩回</b>——哪怕枪已经好了，这条压过开火欲望；
    /// 否则枪快冷却好了（剩余 ≤ <see cref="RaiderTacticsParams.PeekLeadTime"/>）才探头，其余时间躲着。
    /// </summary>
    public static CoverPhase PhaseFor(
        double attackCooldownRemaining, double suppressedRemaining, RaiderTacticsParams p)
    {
        if (suppressedRemaining > 0)
        {
            return CoverPhase.Hunker;
        }
        return attackCooldownRemaining <= p.PeekLeadTime ? CoverPhase.Peek : CoverPhase.Hunker;
    }

    /// <summary>
    /// 掩体搜索的候选点采样：<b>只在身边一圈</b>均布 <see cref="RaiderTacticsParams.CoverSampleCount"/> 个点
    /// （**不做全图搜索**——这是性能红线）。Godot 层再对每个点补射线，填 <see cref="CoverCandidate"/> 的两个 bool。
    /// 内外双环交替（远一点的点能绕到墙的另一侧，近一点的点跑得快）。
    /// </summary>
    public static Vector2[] SampleCoverProbes(Vector2 self, RaiderTacticsParams p)
    {
        int n = Math.Max(1, p.CoverSampleCount);
        var probes = new Vector2[n];
        float step = 360f / n;
        for (int i = 0; i < n; i++)
        {
            float r = p.CoverSearchRadius * (i % 2 == 0 ? 1.0f : 0.6f); // 外环/内环交替
            probes[i] = self + UnitFromDeg(i * step) * MathF.Max(1f, r);
        }
        return probes;
    }

    /// <summary>
    /// 逃跑落点：从出口里挑<b>背向威胁</b>的那个（必须比我现在离威胁更远——否则那不叫退路），
    /// 再按「离威胁多远 − 我要绕多远」打分。若<b>被包围</b>（没有任何出口背向威胁），
    /// 回落成"挑离威胁最远的那个"——<b>硬跑也比站着等死强</b>，绝不返回 null 让它原地转圈。
    /// 出口表为空才 null。
    /// </summary>
    public static Vector2? SelectEscape(
        Vector2 self, Vector2 threat, IReadOnlyList<Vector2> exits, RaiderTacticsParams p)
    {
        if (exits.Count == 0)
        {
            return null;
        }

        float selfToThreat = Vector2.Distance(self, threat);
        Vector2? best = null;
        float bestScore = float.NegativeInfinity;

        Vector2? fallback = null;
        float fallbackDist = float.NegativeInfinity;

        for (int i = 0; i < exits.Count; i++)
        {
            Vector2 e = exits[i];
            float exitToThreat = Vector2.Distance(e, threat);

            if (exitToThreat > fallbackDist)
            {
                fallbackDist = exitToThreat;
                fallback = e;
            }
            if (exitToThreat <= selfToThreat)
            {
                continue; // 这个出口不比我现在安全 → 不是退路
            }
            float score = exitToThreat - p.EscapeDetourWeight * Vector2.Distance(self, e);
            if (score > bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        return best ?? fallback;
    }

    /// <summary>
    /// 该不该<b>显式</b>呼叫增援。
    /// <para>
    /// ⚠️ <b>增援大部分是免费的</b>：<see cref="NoiseKind.Combat"/> 不分阵营 ⇒ 劫掠者一开枪（噪音半径 350~700），
    /// 半径内所有<b>闲着的</b> AI 就已经被噪音系统 <c>CommandMoveTo</c> 过来了。<b>枪声本身就是呼叫</b>。
    /// </para>
    /// <para>
    /// 所以显式呼叫<b>只补噪音盖不住的两个缺口</b>：
    /// <list type="number">
    ///   <item><b>武器不够响</b>：匕首噪音 90~150，砍人的动静传不远，叫不到人。</item>
    ///   <item><b>还没开枪</b>：潜行接近 / 包抄途中还一枪没放，枪声自然没响过。</item>
    /// </list>
    /// 反之——<b>刚开过响枪 ⇒ 不喊</b>（噪音已经替它喊了，再喊是重复机制）。
    /// 另加两道省事门：冷却中不喊、半径内没人闲着也不喊（喊了也没人来，白费一个冷却）。
    /// </para>
    /// </summary>
    public static bool ShouldCallReinforcements(in ReinforceSituation s, RaiderTacticsParams p)
    {
        if (!s.EnemySpotted)
        {
            return false;
        }
        if (s.CallCooldownRemaining > 0)
        {
            return false;
        }
        if (s.IdleAlliesInRange <= 0)
        {
            return false; // 喊了也没人来
        }

        bool loud = s.WeaponNoiseRadius >= p.LoudWeaponNoiseRadius;
        bool noiseAlreadyCalling = loud && s.SinceLastShot <= p.NoiseCoversCallWindow;
        return !noiseAlreadyCalling;
    }

    /// <summary>方向向量 → 角度（度）。零向量按 0° 处理。</summary>
    private static float DegOf(Vector2 v)
    {
        if (v.LengthSquared() <= Epsilon * Epsilon)
        {
            return 0f;
        }
        return MathF.Atan2(v.Y, v.X) * RadToDeg;
    }

    /// <summary>角度（度）→ 单位方向向量。</summary>
    private static Vector2 UnitFromDeg(float deg)
    {
        float rad = deg * DegToRad;
        return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
    }
}
