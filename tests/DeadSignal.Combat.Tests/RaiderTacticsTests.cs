using System.Numerics;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 劫掠者战术 AI 纯逻辑测试：包抄 / 找掩体 / 撤退 / 呼叫增援。
///
/// 核心断言不是"函数返回了某个数"，而是**战术目的达成的机器证明**：
///  - 包抄点必须让 <c>VisionLogic.CanSee(敌人视角 → 该点) == false</c>（真的绕出了视野锥）；
///  - 掩体必须是"敌人视线被墙断掉"的点（BreaksEnemySight），且探头位要能打着人；
///  - 逃跑点必须**背离**威胁（不是原地转圈）；
///  - 增援：枪声够响且刚开过火 → **不显式呼叫**（噪音系统已经替它喊了）。
/// </summary>
public class RaiderTacticsTests
{
    private static readonly RaiderTacticsParams P = RaiderTacticsParams.Default;

    private static TacticalSituation Sit(
        Vector2? self = null,
        double hp = 1.0,
        bool hasEnemy = true,
        Vector2? enemy = null,
        Vector2? enemyFacing = null,
        float coneHalf = 60f,
        float coneRange = 1200f,   // 特意放大：确保"看不见"只可能来自角度，而非超视距
        int hostiles = 1,
        int allies = 0,
        int squadIndex = 0,
        int squadSize = 1,
        float weaponRange = 240f,
        bool isRanged = true,
        bool retreatCommitted = false,
        bool flankDone = false) => new()
        {
            Self = self ?? new Vector2(0, 0),
            HealthFraction = hp,
            HasVisibleEnemy = hasEnemy,
            EnemyPos = enemy ?? new Vector2(200, 0),
            EnemyFacing = enemyFacing ?? new Vector2(-1, 0), // 敌人正朝着我（我在它锥内）
            EnemyConeHalfAngleDeg = coneHalf,
            EnemyConeRange = coneRange,
            VisibleHostiles = hostiles,
            AlliesAlive = allies,
            SquadIndex = squadIndex,
            SquadSize = squadSize,
            WeaponRange = weaponRange,
            IsRanged = isRanged,
            RetreatCommitted = retreatCommitted,
            FlankDone = flankDone,
        };

    /// <summary>敌人视角看某点：复用视野系统的同一个 CanSee（包抄有没有绕出去，由它说了算）。</summary>
    private static bool EnemyCanSee(in TacticalSituation s, Vector2 point) =>
        VisionLogic.CanSee(
            s.EnemyPos, s.EnemyFacing, point,
            new VisionLogic.VisionCone(s.EnemyConeRange, s.EnemyConeHalfAngleDeg), occluded: false);

    // ─────────────────────────── 撤退 ───────────────────────────

    [Fact]
    public void 满血且势均力敌_不撤退()
    {
        Assert.False(RaiderTactics.ShouldRetreat(Sit(hp: 1.0, hostiles: 1, allies: 1), P));
    }

    [Fact]
    public void 伤重_撤退()
    {
        // 阈值 0.35：0.34 逃、0.36 不逃（边界两侧各断一次）
        Assert.True(RaiderTactics.ShouldRetreat(Sit(hp: 0.34, hostiles: 1, allies: 2), P));
        Assert.False(RaiderTactics.ShouldRetreat(Sit(hp: 0.36, hostiles: 1, allies: 2), P));
    }

    [Fact]
    public void 伤重时哪怕没看见敌人也撤退()
    {
        Assert.True(RaiderTactics.ShouldRetreat(Sit(hp: 0.2, hasEnemy: false), P));
    }

    [Fact]
    public void 同伴死光且敌人不止一个_撤退()
    {
        Assert.True(RaiderTactics.ShouldRetreat(Sit(hp: 1.0, allies: 0, hostiles: 2), P));
        // 只剩自己但只有一个敌人 → 满血照打（一对一不算寡不敌众）
        Assert.False(RaiderTactics.ShouldRetreat(Sit(hp: 1.0, allies: 0, hostiles: 1), P));
    }

    [Fact]
    public void 寡不敌众_撤退()
    {
        // 2 人小队（自己+1 同伴）对 4 个敌人 = 2.0 倍 → 逃
        Assert.True(RaiderTactics.ShouldRetreat(Sit(hp: 1.0, allies: 1, hostiles: 4), P));
        // 2 人对 3 个 = 1.5 倍 → 还打得过
        Assert.False(RaiderTactics.ShouldRetreat(Sit(hp: 1.0, allies: 1, hostiles: 3), P));
    }

    [Fact]
    public void 一旦开逃就逃到底_哪怕形势逆转也不回头()
    {
        // 满血、我方三打一（形势大好）——但已经 committed → 仍然逃。防在生死线上抖动/原地转圈。
        Assert.True(RaiderTactics.ShouldRetreat(
            Sit(hp: 1.0, allies: 3, hostiles: 1, retreatCommitted: true), P));
    }

    [Fact]
    public void 撤退姿态压过一切其他战术()
    {
        // 重伤 + 在敌人锥内 + 有队友（本来该包抄）→ 仍然是 Retreat
        var s = Sit(hp: 0.1, squadIndex: 1, squadSize: 3, allies: 2);
        Assert.Equal(RaiderStance.Retreat, RaiderTactics.DecideStance(s, P));
    }

    [Fact]
    public void 逃跑点必须背离威胁_而不是原地转圈()
    {
        var self = new Vector2(0, 0);
        var threat = new Vector2(200, 0);           // 威胁在东
        var exits = new[]
        {
            new Vector2(400, 0),    // 东（威胁背后）—— 绝不能选
            new Vector2(-500, 0),   // 西（正后方）—— 该选这个
            new Vector2(0, 60),     // 北，但离威胁比我还近 —— 不是退路
        };

        Vector2? escape = RaiderTactics.SelectEscape(self, threat, exits, P);
        Assert.NotNull(escape);
        Assert.Equal(new Vector2(-500, 0), escape!.Value);
        // 形式化：逃跑点离威胁更远，且方向与"背离威胁"同向
        Assert.True(Vector2.Distance(escape.Value, threat) > Vector2.Distance(self, threat));
        Assert.True(Vector2.Dot(escape.Value - self, self - threat) > 0);
    }

    [Fact]
    public void 被包围时仍然逃向最远的出口_不站着等死()
    {
        var self = new Vector2(0, 0);
        var threat = new Vector2(0, 0);   // 敌人就在脸上：没有任何出口"比我更远离威胁"的常规解
        var exits = new[] { new Vector2(50, 0), new Vector2(300, 0) };

        Vector2? escape = RaiderTactics.SelectEscape(self, threat, exits, P);
        Assert.Equal(new Vector2(300, 0), escape); // 回落：挑最远的
    }

    [Fact]
    public void 没有出口可逃_返回null()
    {
        Assert.Null(RaiderTactics.SelectEscape(Vector2.Zero, new Vector2(10, 0), System.Array.Empty<Vector2>(), P));
    }

    // ─────────────────────────── 包抄 ───────────────────────────

    [Fact]
    public void 单兵不包抄_老老实实找掩体()
    {
        var s = Sit(squadSize: 1, squadIndex: 0);
        Assert.False(RaiderTactics.ShouldFlank(s, P));
        Assert.Equal(RaiderStance.TakeCover, RaiderTactics.DecideStance(s, P));
    }

    [Fact]
    public void 小队里的零号正面吸引火力_不包抄()
    {
        // 别全部走同一条路：0 号顶在正面，1/2 号才绕
        Assert.False(RaiderTactics.ShouldFlank(Sit(squadSize: 3, squadIndex: 0, allies: 2), P));
        Assert.True(RaiderTactics.ShouldFlank(Sit(squadSize: 3, squadIndex: 1, allies: 2), P));
        Assert.True(RaiderTactics.ShouldFlank(Sit(squadSize: 3, squadIndex: 2, allies: 2), P));
    }

    [Fact]
    public void 已经绕出敌人视野锥就不再绕_转打()
    {
        // 我已在敌人背后（它朝东，我在它西边看着它…… 反过来：它朝东 (1,0)，我在它西侧 → 我在锥外）
        var s = Sit(self: new Vector2(0, 0), enemy: new Vector2(200, 0),
                    enemyFacing: new Vector2(1, 0), squadSize: 2, squadIndex: 1, allies: 1);
        Assert.False(RaiderTactics.IsInEnemyCone(s));
        Assert.False(RaiderTactics.ShouldFlank(s, P));   // 已经在侧后了，直接打
        Assert.Equal(RaiderStance.TakeCover, RaiderTactics.DecideStance(s, P));
    }

    [Fact]
    public void 包抄点真的绕出了敌人的视野锥()
    {
        // 这是包抄的核心证明：用视野系统自己的 CanSee 检查敌人看不看得见那个点。
        var rng = new SequenceRandomSource(0.0); // 抖动取 0（最保守：只留 FlankMargin 的安全边）
        var s = Sit(squadSize: 2, squadIndex: 1, allies: 1);

        Assert.True(RaiderTactics.IsInEnemyCone(s));   // 出发时：我在敌人正面锥内
        Vector2 fp = RaiderTactics.FlankPoint(s, P, rng);
        Assert.False(EnemyCanSee(s, fp));              // 包抄点：敌人看不见（角度绕出去了，不是靠超视距）
        Assert.True(Vector2.Distance(fp, s.EnemyPos) <= s.EnemyConeRange); // 证明"看不见"来自角度而非距离
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void 任意包抄者的落点都在敌人视野锥外(int squadIndex)
    {
        var rng = new SequenceRandomSource(0.0, 0.0, 0.0, 0.0);
        var s = Sit(squadSize: 5, squadIndex: squadIndex, allies: 4);
        Vector2 fp = RaiderTactics.FlankPoint(s, P, rng);
        Assert.False(EnemyCanSee(s, fp));
    }

    [Fact]
    public void 两个包抄者分走左右两边_不挤同一条路()
    {
        var s1 = Sit(squadSize: 3, squadIndex: 1, allies: 2);
        var s2 = Sit(squadSize: 3, squadIndex: 2, allies: 2);

        Vector2 f1 = RaiderTactics.FlankPoint(s1, P, new SequenceRandomSource(0.0));
        Vector2 f2 = RaiderTactics.FlankPoint(s2, P, new SequenceRandomSource(0.0));

        // 相对敌人朝向轴的叉积异号 = 一左一右
        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        float c1 = Cross(s1.EnemyFacing, f1 - s1.EnemyPos);
        float c2 = Cross(s2.EnemyFacing, f2 - s2.EnemyPos);
        Assert.True(c1 * c2 < 0, $"两个包抄者应分走异侧，实测 c1={c1} c2={c2}");
    }

    [Fact]
    public void 包抄环半径落在自己的武器射程内_绕过去正好能打()
    {
        var s = Sit(squadSize: 2, squadIndex: 1, allies: 1, weaponRange: 240f);
        Vector2 fp = RaiderTactics.FlankPoint(s, P, new SequenceRandomSource(0.0));
        float d = Vector2.Distance(fp, s.EnemyPos);
        Assert.True(d <= s.WeaponRange, $"包抄点离敌人 {d} 超出武器射程 {s.WeaponRange}");
        Assert.True(d > s.WeaponRange * 0.5f, "包抄点也不该贴到脸上");
    }

    [Fact]
    public void 随机抖动经IRandomSource注入_可复现()
    {
        var s = Sit(squadSize: 2, squadIndex: 1, allies: 1);
        Vector2 a = RaiderTactics.FlankPoint(s, P, new SequenceRandomSource(0.0));
        Vector2 b = RaiderTactics.FlankPoint(s, P, new SequenceRandomSource(0.0));
        Vector2 c = RaiderTactics.FlankPoint(s, P, new SequenceRandomSource(15.0)); // 抖满

        Assert.Equal(a, b);            // 同序列 → 同结果（可复现）
        Assert.NotEqual(a, c);         // 不同抖动 → 不同路线（两次袭击不完全一样）
        Assert.False(EnemyCanSee(s, c)); // 抖满也仍在锥外（抖动只向锥外加码，不会把人抖回正面）
    }

    [Fact]
    public void 包抄姿态优先于找掩体()
    {
        var s = Sit(squadSize: 3, squadIndex: 1, allies: 2);
        Assert.Equal(RaiderStance.Flank, RaiderTactics.DecideStance(s, P));
    }

    // ─────────────────────────── 掩体 ───────────────────────────

    [Fact]
    public void 只有断掉敌人视线的点才算掩体()
    {
        var self = new Vector2(0, 0);
        var enemy = new Vector2(200, 0);
        var cands = new[]
        {
            new CoverCandidate(new Vector2(40, 0), BreaksEnemySight: false, Reachable: true),   // 空地：不是掩体
            new CoverCandidate(new Vector2(-20, 40), BreaksEnemySight: true, Reachable: true),  // 墙后：是掩体
        };
        Vector2? cover = RaiderTactics.SelectCover(self, enemy, weaponRange: 240f, cands, P);
        Assert.Equal(new Vector2(-20, 40), cover);
    }

    [Fact]
    public void 走不过去的掩体不选()
    {
        var cands = new[]
        {
            new CoverCandidate(new Vector2(-20, 40), BreaksEnemySight: true, Reachable: false),
        };
        Assert.Null(RaiderTactics.SelectCover(Vector2.Zero, new Vector2(200, 0), 240f, cands, P));
    }

    [Fact]
    public void 打不着人的掩体不选_躲到射程外等于白躲()
    {
        // 掩体离敌人 400 > 武器射程 240：探头也够不着 → 不是有效掩体
        var cands = new[]
        {
            new CoverCandidate(new Vector2(-200, 0), BreaksEnemySight: true, Reachable: true),
        };
        Assert.Null(RaiderTactics.SelectCover(Vector2.Zero, new Vector2(200, 0), weaponRange: 240f, cands, P));
    }

    [Fact]
    public void 贴着敌人脸的墙角不选()
    {
        // 离敌人 50 < CoverMinEnemyDistance(70)：躲在敌人鼻子底下 = 送死
        var cands = new[]
        {
            new CoverCandidate(new Vector2(150, 0), BreaksEnemySight: true, Reachable: true),
        };
        Assert.Null(RaiderTactics.SelectCover(Vector2.Zero, new Vector2(200, 0), 240f, cands, P));
    }

    [Fact]
    public void 多个合格掩体_挑离理想交战距离最近的()
    {
        var self = new Vector2(0, 0);
        var enemy = new Vector2(200, 0);
        // 理想距离 = 240 × 0.70 = 168
        var cands = new[]
        {
            new CoverCandidate(new Vector2(30, 10), true, true),    // 离敌 ~170 → 接近理想
            new CoverCandidate(new Vector2(-30, 10), true, true),   // 离敌 ~230 → 偏远
        };
        Vector2? cover = RaiderTactics.SelectCover(self, enemy, 240f, cands, P);
        Assert.Equal(new Vector2(30, 10), cover);
    }

    [Fact]
    public void 没有任何掩体_返回null让调用方回落正面交战()
    {
        Assert.Null(RaiderTactics.SelectCover(
            Vector2.Zero, new Vector2(200, 0), 240f, System.Array.Empty<CoverCandidate>(), P));
    }

    [Fact]
    public void 探头位在掩体与敌人之间_探出去就能打()
    {
        var cover = new Vector2(0, 0);
        var enemy = new Vector2(100, 0);
        Vector2 peek = RaiderTactics.PeekPosition(cover, enemy, P);

        Assert.Equal(P.PeekOffset, Vector2.Distance(cover, peek), 3);            // 只探出 PeekOffset
        Assert.True(Vector2.Distance(peek, enemy) < Vector2.Distance(cover, enemy)); // 朝敌人方向
    }

    [Fact]
    public void 枪冷却好了就探头_没好就缩着()
    {
        Assert.Equal(CoverPhase.Peek, RaiderTactics.PhaseFor(
            attackCooldownRemaining: 0.2, suppressedRemaining: 0, P));   // ≤ PeekLeadTime(0.35)
        Assert.Equal(CoverPhase.Hunker, RaiderTactics.PhaseFor(
            attackCooldownRemaining: 1.8, suppressedRemaining: 0, P));   // 还早，缩着
    }

    [Fact]
    public void 被压制时缩回_哪怕枪已经好了()
    {
        Assert.Equal(CoverPhase.Hunker, RaiderTactics.PhaseFor(
            attackCooldownRemaining: 0.0, suppressedRemaining: 0.5, P));
    }

    [Fact]
    public void 掩体候选采样在搜索半径内_数量等于配置()
    {
        var self = new Vector2(100, 100);
        Vector2[] probes = RaiderTactics.SampleCoverProbes(self, P);

        Assert.Equal(P.CoverSampleCount, probes.Length);
        foreach (Vector2 pt in probes)
        {
            float d = Vector2.Distance(pt, self);
            Assert.True(d <= P.CoverSearchRadius + 0.01f, $"候选点 {pt} 超出搜索半径（{d}）");
            Assert.True(d > 0f, "候选点不该落在自己脚下");
        }
    }

    [Fact]
    public void 近战劫掠者不躲掩体_只会冲()
    {
        // 拿匕首躲墙后没意义（探头也够不着）→ 恒 Engage。这是远近程的分野。
        var s = Sit(isRanged: false, weaponRange: 26f, squadSize: 1);
        Assert.Equal(RaiderStance.Engage, RaiderTactics.DecideStance(s, P));
    }

    [Fact]
    public void 敌人已经贴脸_不再找掩体_直接打()
    {
        // 距离 50 < CoverMinEnemyDistance(70)：这时候转身跑去躲墙 = 背对着人挨枪
        var s = Sit(self: new Vector2(0, 0), enemy: new Vector2(50, 0), squadSize: 1);
        Assert.Equal(RaiderStance.Engage, RaiderTactics.DecideStance(s, P));
    }

    // ─────────────────────────── 增援 ───────────────────────────

    private static ReinforceSituation Call(
        bool spotted = true, int hostiles = 1, int allies = 1, int idleAllies = 1,
        double cooldown = 0, double noise = 350, double sinceShot = 99) => new()
        {
            EnemySpotted = spotted,
            VisibleHostiles = hostiles,
            AlliesAlive = allies,
            IdleAlliesInRange = idleAllies,
            CallCooldownRemaining = cooldown,
            WeaponNoiseRadius = noise,
            SinceLastShot = sinceShot,
        };

    [Fact]
    public void 没发现敌人不喊人()
    {
        Assert.False(RaiderTactics.ShouldCallReinforcements(Call(spotted: false), P));
    }

    [Fact]
    public void 发现敌人且还没开枪_显式喊人()
    {
        // 这正是噪音系统盖不住的缺口：潜行/包抄途中还没开火 → 枪声没响过，得喊。
        Assert.True(RaiderTactics.ShouldCallReinforcements(Call(sinceShot: 99), P));
    }

    [Fact]
    public void 刚开过枪且枪够响_不再显式喊人_噪音系统已经替它喊了()
    {
        // 枪声 350 ≥ LoudWeaponNoiseRadius(300) 且 1s 前刚开火 → NoiseKind.Combat 不分阵营，
        // 半径内所有闲着的同伴已被 CommandMoveTo 过来了。再喊一遍是重复机制。
        Assert.False(RaiderTactics.ShouldCallReinforcements(
            Call(noise: 350, sinceShot: 1.0), P));
    }

    [Fact]
    public void 拿匕首的劫掠者_哪怕刚砍过人也要喊_因为声音太小叫不到人()
    {
        // 匕首噪音 120 < 300：砍人的动静传不远 → 噪音系统盖不住 → 必须显式呼叫
        Assert.True(RaiderTactics.ShouldCallReinforcements(
            Call(noise: 120, sinceShot: 0.5), P));
    }

    [Fact]
    public void 枪声窗口过期后_可以再喊()
    {
        // 4s 前开的那一枪早就把附近的人叫完了（且他们可能没来）；窗口(3s)外 → 允许再喊
        Assert.True(RaiderTactics.ShouldCallReinforcements(
            Call(noise: 350, sinceShot: 4.0), P));
    }

    [Fact]
    public void 冷却中不喊_防刷屏()
    {
        Assert.False(RaiderTactics.ShouldCallReinforcements(Call(cooldown: 2.0), P));
    }

    [Fact]
    public void 半径内没人闲着_不喊_省下冷却()
    {
        Assert.False(RaiderTactics.ShouldCallReinforcements(Call(idleAllies: 0), P));
    }

    // ─────────────────────────── 参数数据驱动 ───────────────────────────

    [Fact]
    public void 战术参数可整体替换_不硬编码魔法数()
    {
        var brave = P with { RetreatHealthFraction = 0.05 }; // 死硬派：血只剩 5% 才跑
        var s = Sit(hp: 0.20, allies: 2, hostiles: 1);
        Assert.True(RaiderTactics.ShouldRetreat(s, P));       // 默认参数：0.20 ≤ 0.35 → 逃
        Assert.False(RaiderTactics.ShouldRetreat(s, brave));  // 换一套参数：同一个局面就不逃了
    }

    [Fact]
    public void 逃跑者身份钩子_带得走名字与天数_供剧情层日后订阅()
    {
        // 只做机制：记录"谁在第几天带着几成血逃走了"。剧情（报复/再遇）由别处写。
        var e = new RaiderEscape("克莉丝汀", Day: 7, HealthFraction: 0.22);
        Assert.Equal("克莉丝汀", e.DisplayName);
        Assert.Equal(7, e.Day);
        Assert.Equal(0.22, e.HealthFraction, 3);
    }
}
