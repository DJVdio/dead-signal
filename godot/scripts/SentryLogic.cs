using System;
using System.Numerics;
using DeadSignal.Combat; // IRandomSource（唯一用途：生成时掷一次初始扫视相位，此后扫视本身全程无随机）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 VisionLogic.cs / NoiseLogic.cs / RaiderTactics.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（寻路回岗、转朝向）归 Godot 实时层（Raider.cs），本文件只出**纯判定函数**。

/// <summary>
/// 哨兵没敌人时的三种行为。<b>注意这里没有"游荡"</b> —— 那正是岗哨与普通劫掠者的区别：
/// <b>站岗的人不会瞎逛</b>。
/// </summary>
public enum SentryAction
{
    /// <summary>在岗：站在岗位上<b>有规律地扫视</b>（见 <see cref="SentrySweep"/>）——玩家要读的就是这个规律。</summary>
    HoldPost,

    /// <summary>回岗：查看完了 / 被拉太远了 / 查看超时了 → 走回自己的岗位。</summary>
    ReturnToPost,

    /// <summary>离岗查看：听见动静了，过去看一眼（复用既有的「最后目击点 + CommandMoveTo」通道）。</summary>
    Investigate,
}

/// <summary>
/// 一个岗位：<b>位置 + 扫视中心朝向 + 初始相位</b>。
/// <para>
/// 朝向是关键——视野锥有朝向（<c>VisionLogic.ConeFor</c>），所以"绕过岗哨的视野锥"才成立。
/// 但哨兵<b>不是雕像</b>：在岗时它会绕着 <see cref="FacingRadians"/> <b>有规律地左右扫视</b>
/// （见 <see cref="SentrySweep"/>）。<see cref="FacingRadians"/> 是<b>扫视的中心</b>，不是唯一朝向。
/// </para>
/// </summary>
/// <param name="Position">岗位坐标。</param>
/// <param name="FacingRadians">扫视的<b>中心</b>朝向（弧度）。</param>
/// <param name="SweepPhaseSeconds">
/// 本哨兵扫视的<b>初始相位</b>（秒）。生成时用 <see cref="SentryLogic.RollSweepPhase"/> 掷<b>一次</b>、此后不再变——
/// 这是整套扫视里<b>唯一</b>用到随机的地方，为的是不让全场哨兵像仪仗队一样整齐划一地转头。
/// 掷定之后，朝向就只是**时间的确定性函数**了。
/// </param>
public readonly record struct SentryPost(
    Vector2 Position, float FacingRadians, double SweepPhaseSeconds = 0);

/// <summary>
/// <b>在岗扫视</b>的规律（用户口径：「哨兵在岗时朝向会<b>有规律的</b>转动」）。
/// <para>
/// ⚠️ <b>关键词是"有规律"，不是"随机"</b>。随机转头的哨兵只会逼玩家读档；
/// <b>周期固定、可观察、可预测</b>的扫视，才让玩家能<b>蹲着数拍子、算准他背对你的那几秒窗口再动</b>。
/// 这是《合金装备》《盟军敢死队》的核心玩法，也是这套东西存在的全部意义。
/// </para>
/// <para>
/// 波形 = <b>带端点停顿的三角波</b>，一个周期四段：
/// 左端 → 转到右端（转动）→ <b>在右端停住</b>（停顿）→ 转回左端（转动）→ <b>在左端停住</b>（停顿）。
/// <b>停顿是给玩家的信号</b>：头转到头了、定住了 ⇒ 这几秒是你的。
/// </para>
/// </summary>
public sealed record SentrySweep
{
    /// <summary>扫视半角（度）：绕中心朝向左右各扫这么多。0 = 不扫（退化成钉死朝向）。</summary>
    public float HalfAngleDeg { get; init; } = 55f;

    /// <summary>一个完整来回的周期（秒）。含两段转动 + 两次端点停顿。</summary>
    public double PeriodSeconds { get; init; } = 6.0;

    /// <summary>每个端点上停顿多久（秒）。<b>停顿越久，玩家的窗口越大</b>。</summary>
    public double PauseSeconds { get; init; } = 1.0;

    /// <summary><b>警觉的哨兵</b>：扫得又快又宽，端点几乎不发呆——难绕。</summary>
    public static SentrySweep Alert { get; } = new()
    {
        HalfAngleDeg = 55f,
        PeriodSeconds = 6.0,
        PauseSeconds = 1.0,
    };

    /// <summary><b>懈怠的哨兵</b>：扫得又慢又窄，还老在端点发呆——好绕。（差异化维度，拟定待调）</summary>
    public static SentrySweep Slack { get; } = new()
    {
        HalfAngleDeg = 35f,
        PeriodSeconds = 10.0,
        PauseSeconds = 2.0,
    };

    /// <summary>默认档（= 警觉）。</summary>
    public static SentrySweep Default => Alert;
}

/// <summary>哨兵参数（拟定待调，数据驱动：随 <c>raider_tactics.json</c> 一起配）。</summary>
public sealed record SentryParams
{
    /// <summary>离岗位多近算"在岗"。</summary>
    public float PostArrivalTolerance { get; init; } = 18f;

    /// <summary>
    /// 离岗查看的<b>牵引半径</b>：声音/目击点离**岗位**超过这么远就不去了。
    /// 没有这条，一声枪响能把整个据点的哨兵全拽到地图另一头，据点就空了。
    /// </summary>
    public float LeashRadius { get; init; } = 500f;

    /// <summary>查看多久还没结果就回岗（防止被一串噪音牵着走、永远回不了岗）。</summary>
    public double InvestigateTimeout { get; init; } = 12.0;

    public static SentryParams Default { get; } = new();
}

/// <summary>
/// 敌营岗哨纯逻辑（用户口径：<b>敌人营地也会有类似幸存者营地的岗哨</b>）。
/// <para>
/// <b>最小可用版</b>：固定岗位 + <b>有规律的在岗扫视</b>（<see cref="SentrySweep"/>）、被噪音唤醒去查看、查看完<b>回岗</b>。
/// 巡逻路线 / 换班 / 警戒等级 <b>不做</b>（见回报遗留）。
/// </para>
/// <para>
/// <b>这让"潜入敌营"第一次成立</b>：哨兵的视野锥<b>按固定周期左右扫</b>，玩家可以<b>蹲着数拍子、
/// 算准他背对你的那几秒</b>再动（端点停顿就是那个"现在动"的信号）；或者用安静的手段
/// （弓 70 / 匕首 90）悄悄解决。<b>开一枪（350~700）就等于把整个据点叫醒</b> —— 而且叫醒的是
/// <b>已经在场</b>的哨兵，不是凭空刷出来的新敌人。
/// </para>
/// </summary>
public static class SentryLogic
{
    /// <summary>
    /// 哨兵在<b>没有敌人</b>时该干嘛（有敌人时交给 <see cref="RaiderTactics"/> 那套战术，与本类无关）。
    /// </summary>
    /// <param name="self">哨兵当前位置。</param>
    /// <param name="post">它的岗位。</param>
    /// <param name="investigatePoint">要去查看的点（噪音源 / 最后目击点）；null = 没什么好查的。</param>
    /// <param name="investigateElapsed">已经查看了多久（秒）。</param>
    public static SentryAction DecideIdle(
        Vector2 self, SentryPost post, Vector2? investigatePoint,
        double investigateElapsed, SentryParams p)
    {
        if (investigatePoint is { } target)
        {
            // 查看太久还没结果 → 别耗着了，回岗。
            if (investigateElapsed >= p.InvestigateTimeout)
            {
                return SentryAction.ReturnToPost;
            }
            // 动静在岗位的牵引半径之外 → **不离岗**（守着自己那块地比追声音重要）。
            if (Vector2.Distance(target, post.Position) > p.LeashRadius)
            {
                return AtPost(self, post, p) ? SentryAction.HoldPost : SentryAction.ReturnToPost;
            }
            return SentryAction.Investigate;
        }

        return AtPost(self, post, p) ? SentryAction.HoldPost : SentryAction.ReturnToPost;
    }

    /// <summary>是否已经站在岗位上。</summary>
    public static bool AtPost(Vector2 self, SentryPost post, SentryParams p)
        => Vector2.Distance(self, post.Position) <= p.PostArrivalTolerance;

    /// <summary>
    /// 哨兵此刻该朝哪儿（弧度）。<b>在岗 ⇒ 走扫视规律</b>（<see cref="SweepFacing"/>，时间的确定性函数）；
    /// 离岗移动时<b>不干预</b>（保持 <paramref name="currentFacing"/>，由移动逻辑面朝行进方向）。
    /// </summary>
    public static float FacingFor(
        SentryAction action, SentryPost post, float currentFacing, double timeSeconds, SentrySweep sweep)
        => action == SentryAction.HoldPost ? SweepFacing(post, timeSeconds, sweep) : currentFacing;

    /// <summary>
    /// <b>在岗扫视的朝向</b>：<paramref name="timeSeconds"/> 时刻该看哪儿（弧度）。
    /// <para>
    /// <b>这是一个纯函数，没有任何随机</b>——同一个 t 永远给同一个答案。可预测 ⇒ 可测试 ⇒ <b>可利用</b>：
    /// 玩家蹲在暗处数几个周期，就能算准他背对你的那几秒。
    /// </para>
    /// <para>
    /// 波形 = 带端点停顿的三角波（见 <see cref="SentrySweep"/>）。相位由 <see cref="SentryPost.SweepPhaseSeconds"/>
    /// 平移（各哨兵不同步，但每个自己是规律的）。<see cref="SentrySweep.HalfAngleDeg"/>=0 → 退化成钉死中心朝向。
    /// </para>
    /// </summary>
    public static float SweepFacing(SentryPost post, double timeSeconds, SentrySweep sweep)
    {
        float half = MathF.Max(0f, sweep.HalfAngleDeg) * DegToRad;
        if (half <= 0f)
        {
            return post.FacingRadians; // 不扫视：钉死中心朝向
        }

        double period = Math.Max(1e-3, sweep.PeriodSeconds);
        // 停顿吃不下整个周期（否则就没有转动段了）：两次停顿最多占掉周期的一半，剩下的留给转动。
        double pause = Math.Clamp(sweep.PauseSeconds, 0, period / 4.0);
        double travel = (period - 2 * pause) / 2.0; // 单程转动时长（左端→右端）

        double u = Mod(timeSeconds + post.SweepPhaseSeconds, period);

        float offset;
        if (u < travel)
        {
            offset = Lerp(-half, half, (float)(u / travel));       // 左 → 右（转动）
        }
        else if (u < travel + pause)
        {
            offset = half;                                          // 在右端**停住**（玩家的窗口）
        }
        else if (u < 2 * travel + pause)
        {
            offset = Lerp(half, -half, (float)((u - travel - pause) / travel)); // 右 → 左（转动）
        }
        else
        {
            offset = -half;                                         // 在左端**停住**
        }

        return post.FacingRadians + offset;
    }

    /// <summary>
    /// 掷一个哨兵的<b>初始扫视相位</b>（秒，落在 [0, 周期)）。<b>生成时只掷一次，此后永不再变</b>——
    /// 这是整套扫视里唯一用到随机的地方（走可注入的 <see cref="IRandomSource"/>，测试可复现），
    /// 为的是不让全场哨兵像仪仗队一样整齐划一地转头。<b>扫视本身绝不随机。</b>
    /// </summary>
    public static double RollSweepPhase(IRandomSource rng, SentrySweep sweep)
        => rng.Range(0, Math.Max(1e-3, sweep.PeriodSeconds));

    private const float DegToRad = MathF.PI / 180f;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static double Mod(double x, double m)
    {
        double r = x % m;
        return r < 0 ? r + m : r;
    }
}
