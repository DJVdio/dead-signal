using System;
using System.Numerics;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 敌营岗哨纯逻辑测试（用户口径：敌人营地也会有类似幸存者营地的岗哨）。
///
/// 核心断言：<b>站岗的人不会瞎逛</b>（这是岗哨与普通劫掠者的根本区别），
/// 听见动静会去看、看完会**回岗**，而且**不会被一声远处的枪响拽离据点**（牵引半径）。
/// </summary>
public class SentryLogicTests
{
    private static readonly SentryParams P = SentryParams.Default;
    private static readonly SentryPost Post = new(new Vector2(100, 100), FacingRadians: 0f);

    [Fact]
    public void 在岗且无事_站定不动_绝不游荡()
    {
        // 岗哨的枚举里根本没有"游荡"这个选项——这条就是它和普通劫掠者的分野。
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(100, 100), Post, investigatePoint: null, investigateElapsed: 0, P);
        Assert.Equal(SentryAction.HoldPost, a);
    }

    [Fact]
    public void 岗位带朝向_视野锥才有得绕()
    {
        // 朝向是潜入能成立的前提：哨兵的视野锥钉在一个方向上，玩家可以绕它的侧后。
        var facingEast = new SentryPost(new Vector2(0, 0), FacingRadians: 0f);
        var cone = VisionLogic.ConeFor(VisionLogic.DaylightAmbient);
        Vector2 facing = new(MathF.Cos(facingEast.FacingRadians), MathF.Sin(facingEast.FacingRadians));

        // 正东方向的人：看得见
        Assert.True(VisionLogic.CanSee(facingEast.Position, facing, new Vector2(150, 0), cone, occluded: false));
        // 正西（背后）的人：看不见 —— 绕到背后就是潜入
        Assert.False(VisionLogic.CanSee(facingEast.Position, facing, new Vector2(-150, 0), cone, occluded: false));
    }

    [Fact]
    public void 离开了岗位且无事_走回岗位()
    {
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(300, 100), Post, investigatePoint: null, investigateElapsed: 0, P);
        Assert.Equal(SentryAction.ReturnToPost, a);
    }

    [Fact]
    public void 听见附近的动静_离岗去查看()
    {
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(100, 100), Post,
            investigatePoint: new Vector2(300, 200), investigateElapsed: 0, P); // 离岗位 ~224 < 牵引 500
        Assert.Equal(SentryAction.Investigate, a);
    }

    [Fact]
    public void 动静在牵引半径之外_不离岗()
    {
        // 没有这条，一声枪响能把整个据点的哨兵全拽到地图另一头，据点就空了。
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(100, 100), Post,
            investigatePoint: new Vector2(900, 100), investigateElapsed: 0, P); // 离岗位 800 > 牵引 500
        Assert.Equal(SentryAction.HoldPost, a);
    }

    [Fact]
    public void 动静太远且自己已经离岗了_先回岗()
    {
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(400, 100), Post,
            investigatePoint: new Vector2(900, 100), investigateElapsed: 0, P);
        Assert.Equal(SentryAction.ReturnToPost, a);
    }

    [Fact]
    public void 查看超时_放弃并回岗()
    {
        // 防止被一串噪音牵着走、永远回不了岗。
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(300, 200), Post,
            investigatePoint: new Vector2(300, 200), investigateElapsed: 12.5, P); // > 12s
        Assert.Equal(SentryAction.ReturnToPost, a);
    }

    [Fact]
    public void 查看未超时_继续查看()
    {
        SentryAction a = SentryLogic.DecideIdle(
            self: new Vector2(300, 200), Post,
            investigatePoint: new Vector2(300, 200), investigateElapsed: 5.0, P);
        Assert.Equal(SentryAction.Investigate, a);
    }

    [Fact]
    public void 到岗容差_边界两侧各断一次()
    {
        Assert.True(SentryLogic.AtPost(new Vector2(100, 117), Post, P));   // 17 ≤ 18
        Assert.False(SentryLogic.AtPost(new Vector2(100, 120), Post, P));  // 20 > 18
    }

    // ── 在岗扫视：有规律地转动（用户口径：「哨兵在岗时朝向会有规律的转动」）──────────
    // ⚠️ 关键词是"有规律"，不是"随机"。随机转头的哨兵只会逼玩家读档；
    //    周期固定、可观察、可预测的扫视，才让玩家能蹲着数拍子、算准背对你的那几秒窗口再动。
    //    这是《合金装备》《盟军敢死队》的核心玩法 —— 下面每一条测试都在守它。

    private static readonly SentrySweep Sweep = SentrySweep.Alert;
    private static readonly SentryPost SweepPost = new(new Vector2(0, 0), FacingRadians: 0f);

    [Fact]
    public void 扫视朝向是时间的确定性函数_同一时刻永远同一个朝向()
    {
        // 可预测 = 可测试 = 可利用。同一个 t 连问 100 次必须给同一个答案。
        float first = SentryLogic.SweepFacing(SweepPost, timeSeconds: 3.7, Sweep);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, SentryLogic.SweepFacing(SweepPost, 3.7, Sweep), 6);
        }
    }

    [Fact]
    public void 扫视是周期性的_一个周期之后回到原样()
    {
        for (double t = 0; t < Sweep.PeriodSeconds; t += 0.37)
        {
            Assert.Equal(
                SentryLogic.SweepFacing(SweepPost, t, Sweep),
                SentryLogic.SweepFacing(SweepPost, t + Sweep.PeriodSeconds, Sweep), 4);
        }
    }

    [Fact]
    public void 扫视始终在岗位朝向的正负扫视角之内_不会转到背后去()
    {
        float half = Sweep.HalfAngleDeg * MathF.PI / 180f;
        for (double t = 0; t < Sweep.PeriodSeconds * 3; t += 0.05)
        {
            float f = SentryLogic.SweepFacing(SweepPost, t, Sweep);
            Assert.InRange(f - SweepPost.FacingRadians, -half - 1e-4f, half + 1e-4f);
        }
    }

    [Fact]
    public void 扫视会真的扫到两个端点_不是原地小幅抖动()
    {
        float half = Sweep.HalfAngleDeg * MathF.PI / 180f;
        float min = float.MaxValue, max = float.MinValue;
        for (double t = 0; t < Sweep.PeriodSeconds; t += 0.01)
        {
            float f = SentryLogic.SweepFacing(SweepPost, t, Sweep) - SweepPost.FacingRadians;
            min = MathF.Min(min, f);
            max = MathF.Max(max, f);
        }
        Assert.Equal(-half, min, 2);
        Assert.Equal(half, max, 2);
    }

    [Fact]
    public void 端点有停顿_停顿期间朝向纹丝不动()
    {
        // 停顿很重要：它给玩家一个明确的"现在动"的信号（头转到头了、定住了 → 这几秒是你的）。
        float half = Sweep.HalfAngleDeg * MathF.PI / 180f;
        double travel = (Sweep.PeriodSeconds - 2 * Sweep.PauseSeconds) / 2.0; // 单程转动时长

        // 单程转完的那一刻起，整个 PauseSeconds 窗口内都钉在 +half 上
        for (double t = travel + 0.01; t < travel + Sweep.PauseSeconds - 0.01; t += 0.02)
        {
            Assert.Equal(half, SentryLogic.SweepFacing(SweepPost, t, Sweep) - SweepPost.FacingRadians, 3);
        }
    }

    [Fact]
    public void 扫视给玩家留出了可利用的窗口_同一个位置会从可见变成不可见()
    {
        // 玩法的机器证明：站在哨兵侧翼的某个点，在扫视的一半周期里被看见、另一半里看不见。
        // ——这正是"蹲着观察他的规律、算准他背对你的那几秒再动"。
        var post = new SentryPost(new Vector2(0, 0), FacingRadians: 0f); // 中心朝东
        var cone = VisionLogic.ConeFor(VisionLogic.DaylightAmbient);     // 半角 60°
        // 侧翼一点：在东偏北 80° 方向（只有当哨兵把头转向北侧端点时才落进 60° 锥内）
        float a = 80f * MathF.PI / 180f;
        Vector2 spot = post.Position + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 150f;

        bool seenSometime = false, hiddenSometime = false;
        for (double t = 0; t < Sweep.PeriodSeconds; t += 0.05)
        {
            float f = SentryLogic.SweepFacing(post, t, Sweep);
            Vector2 facing = new(MathF.Cos(f), MathF.Sin(f));
            if (VisionLogic.CanSee(post.Position, facing, spot, cone, occluded: false))
                seenSometime = true;
            else
                hiddenSometime = true;
        }
        Assert.True(seenSometime, "扫到那一侧时应当看得见——否则这个哨兵形同虚设");
        Assert.True(hiddenSometime, "扫开之后应当看不见——这就是玩家要等的窗口");
    }

    [Fact]
    public void 每个哨兵的初始相位一次性掷定_此后不再变_两个哨兵不同步()
    {
        // 唯一用到随机的地方：生成时掷一次初始相位（走可注入 IRandomSource，可复现）。
        // 之后朝向就只是时间的函数了——**扫视本身绝不随机**。
        double phaseA = SentryLogic.RollSweepPhase(new SequenceRandomSource(0.0), Sweep);
        double phaseB = SentryLogic.RollSweepPhase(new SequenceRandomSource(0.5), Sweep);

        Assert.InRange(phaseA, 0, Sweep.PeriodSeconds);
        Assert.InRange(phaseB, 0, Sweep.PeriodSeconds);
        Assert.NotEqual(phaseA, phaseB);

        var a = new SentryPost(new Vector2(0, 0), 0f, SweepPhaseSeconds: phaseA);
        var b = new SentryPost(new Vector2(0, 0), 0f, SweepPhaseSeconds: phaseB);
        // 同一时刻，两个哨兵看的方向不一样（不会全场哨兵像仪仗队一样整齐划一转头）
        Assert.NotEqual(
            SentryLogic.SweepFacing(a, 1.0, Sweep),
            SentryLogic.SweepFacing(b, 1.0, Sweep), 3);
    }

    [Fact]
    public void 扫视角设为零_退化成钉死岗位朝向()
    {
        var still = Sweep with { HalfAngleDeg = 0f };
        for (double t = 0; t < 10; t += 0.5)
        {
            Assert.Equal(SweepPost.FacingRadians, SentryLogic.SweepFacing(SweepPost, t, still), 5);
        }
    }

    [Fact]
    public void 警觉的哨兵扫得又快又宽_懈怠的又慢又窄()
    {
        // 差异化维度（拟定待调）：同一套规则，两组数值。
        Assert.True(SentrySweep.Alert.PeriodSeconds < SentrySweep.Slack.PeriodSeconds, "警觉的周期更短=扫得更勤");
        Assert.True(SentrySweep.Alert.HalfAngleDeg > SentrySweep.Slack.HalfAngleDeg, "警觉的扫视角更大=盯得更宽");
        Assert.True(SentrySweep.Alert.PauseSeconds < SentrySweep.Slack.PauseSeconds, "懈怠的在端点发呆更久=窗口更大");
    }

    [Fact]
    public void 在岗才扫视_离岗移动时不干预朝向_由行进方向决定()
    {
        const float travelFacing = 3.0f;
        Assert.Equal(travelFacing,
            SentryLogic.FacingFor(SentryAction.Investigate, SweepPost, travelFacing, 1.0, Sweep), 5);
        Assert.Equal(travelFacing,
            SentryLogic.FacingFor(SentryAction.ReturnToPost, SweepPost, travelFacing, 1.0, Sweep), 5);

        // 在岗 → 走扫视（而不是保持当前朝向、也不是钉死中心）
        Assert.Equal(SentryLogic.SweepFacing(SweepPost, 1.0, Sweep),
            SentryLogic.FacingFor(SentryAction.HoldPost, SweepPost, travelFacing, 1.0, Sweep), 5);
    }

    [Fact]
    public void 哨兵参数可整体替换_不硬编码魔法数()
    {
        var lax = P with { LeashRadius = 2000f }; // 死忠追声派：追到天边
        Vector2 far = new(900, 100);
        Assert.Equal(SentryAction.HoldPost,
            SentryLogic.DecideIdle(new Vector2(100, 100), Post, far, 0, P));
        Assert.Equal(SentryAction.Investigate,
            SentryLogic.DecideIdle(new Vector2(100, 100), Post, far, 0, lax));
    }

    // ── 增援：绝不刷怪（口径的形式化）──────────────────────────────

    [Fact]
    public void 主动呼喊的噪音必须够响_否则喊了也白喊()
    {
        // 「呼喊」= 一个较大半径的战斗噪音（走既有 EmitNoiseAt 通道），不是刷怪。
        // 它必须比"响枪"的门槛还响，否则拿匕首的哨兵喊人反而不如开一枪 —— 那这个机制就没意义了。
        RaiderTacticsParams p = RaiderTacticsParams.Default;
        Assert.True(p.ShoutNoiseRadius >= p.LoudWeaponNoiseRadius,
            $"呼喊半径 {p.ShoutNoiseRadius} 应 ≥ 响枪门槛 {p.LoudWeaponNoiseRadius}");
    }

    [Fact]
    public void 呼喊是战斗噪音_所以不分阵营_喊人也会把丧尸招来()
    {
        // NoiseKind.Combat 不分阵营（用户拍板）：喊人的代价是丧尸也听见了。这是取舍，不是 bug。
        Assert.True(NoiseLogic.ShouldInvestigate(
            NoiseKind.Combat,
            listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: false,
            hostileToSource: false,                       // 同阵营的同伙 → 来
            distance: 100, noiseRadius: RaiderTacticsParams.Default.ShoutNoiseRadius));
        Assert.True(NoiseLogic.ShouldInvestigate(
            NoiseKind.Combat,
            listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: false,
            hostileToSource: true,                        // 敌对的丧尸 → 也来
            distance: 100, noiseRadius: RaiderTacticsParams.Default.ShoutNoiseRadius));
    }

    [Fact]
    public void 呼喊只叫得动闲着的人_已经在打架的不会被喊走()
    {
        // 增援 = 唤醒"当前无目标"的已在场敌人。已经在追人的不会被一声喊拽走。
        Assert.False(NoiseLogic.ShouldInvestigate(
            NoiseKind.Combat,
            listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: true,
            hostileToSource: false,
            distance: 100, noiseRadius: RaiderTacticsParams.Default.ShoutNoiseRadius));
    }
}
