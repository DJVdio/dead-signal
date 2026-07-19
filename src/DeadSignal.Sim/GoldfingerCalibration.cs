using System.Text;
using DeadSignal.Combat;
using DeadSignal.Godot; // GoldfingerGang（编制表 Link 自 godot/scripts —— 与运行时读同一张表）

/// <summary>
/// 「金手指帮之战」校准（<c>dotnet run --project src/DeadSignal.Sim goldfinger</c>）。
///
/// <para><b>这份 harness 回答的不是"打不打得赢"，而是"赢下来要付什么代价"。</b>
/// 用户纠正过一次框架错误——「战斗难道不是成本吗？」——所以这里<b>不出"胜率 × 8 件战利品"</b>那种账：
/// 每一波都把队员<b>身上还剩什么</b>捞出来（死了几个、永久少了什么、骨折几处），<b>并且带进下一波</b>。
/// 赢了但断一只手 / 躺三个人 / 烧光医疗物资，跟毫发无伤地赢，在这个游戏里根本不是一回事。</para>
///
/// <para>🔴 <b>三个场景对应三种打法，差别是这一关的全部内容</b>：
/// <list type="bullet">
/// <item><b>惊动全据点</b>（3v8 一次性）——<b>开枪的后果</b>。枪声半径 350~700（战斗声不分阵营），
/// 一枪就是把整个据点叫醒。</item>
/// <item><b>逐波推进</b>（3v2 → 3v3 → 3v3，伤累积）——正常推进：他们分三个纵深布防（近入口 / 中段 / 深处）。</item>
/// <item><b>逐个清哨</b>（3v1 ×8，伤累积）——弓（噪音 70）/ 匕首（90）潜行，绕开哨兵扫视的端点停顿一个个摸掉。</item>
/// </list></para>
///
/// <para>⚠️ <b>Sim 测不了什么</b>：<see cref="Arena"/> 无空间——无距离、无走位、无掩体、无先手、不建模弹药
/// （敌方两把手枪在这里是<b>无限子弹</b>）。所以它给的是每种打法的<b>下界</b>；先手、掩体、逃跑都只会让真实结果更好。
/// 波间假设：<b>止血但不回血</b>（队员能简单包扎，但探索途中没有真正的治疗）——HP 缺口 / 骨折 / 残缺全部带进下一波。</para>
/// </summary>
public static class GoldfingerCalibration
{
    private const int Seeds = 2000;
    private const int TeamSize = 3; // 探索队上限

    private static IReadOnlyList<ArmorLayer> PlayerArmor() => ArmorTable.SurvivorArmor();

    /// <summary>一名守备：持械与伤情<b>全部读 authored 编制表</b>（<see cref="GoldfingerGang.Roster"/>）。</summary>
    private static DuelFighter Guard(GangGuard g, bool injured) => new()
    {
        Name = $"{g.DisplayName}·{(injured ? g.Injury.Name : "满状态")}",
        Weapons = new[] { new WeaponMount { Weapon = GoldfingerGang.WeaponFor(g.Arm) } },
        Armor = ArmorTable.SurvivorArmor(), // 与 Raider.Create 同：皮夹克 + 长袖布衣
        BodyFactory = injured
            ? () => GoldfingerGang.NewInjuredBody(g.Injury) // 与运行时 Raider.ApplyInjury 同一个函数
            : HumanBody.NewBody,
    };

    /// <summary>一个队员在整场探索中的<b>连续状态</b>（伤带进下一波）。</summary>
    private sealed class Member
    {
        public required string Name { get; init; }
        public BodySnapshot? Carry { get; set; } // null = 还没受过伤（首波满状态）
        public bool Dead { get; set; }
    }

    /// <summary>整趟探索打完之后的账。</summary>
    private readonly record struct Toll(
        bool Won, int WavesCleared, int Dead, int Severed, int Fractured, int Disabled, double Duration);

    /// <summary>
    /// 打完一整趟：按 <paramref name="waves"/> 逐波遭遇，<b>伤在波与波之间累积</b>。
    /// 死掉的队员不再参战（也不会复活到下一波）。
    /// </summary>
    private static Toll Campaign(
        Weapon kit, IReadOnlyList<IReadOnlyList<GangGuard>> waves, IRandomSource rng, bool injured)
    {
        var team = Enumerable.Range(0, TeamSize)
            .Select(i => new Member { Name = $"队员{i + 1}" })
            .ToList();

        int cleared = 0;
        double dur = 0;
        bool won = true;

        foreach (IReadOnlyList<GangGuard> wave in waves)
        {
            List<Member> alive = team.Where(m => !m.Dead).ToList();
            if (alive.Count == 0)
            {
                won = false;
                break;
            }

            // 每个还活着的队员，带着**上一波打完的身体**上场。
            var teamA = alive.Select(m => new DuelFighter
            {
                Name = m.Name,
                Weapons = new[] { new WeaponMount { Weapon = kit } },
                Armor = PlayerArmor(),
                BodyFactory = () =>
                {
                    Body b = HumanBody.NewBody();
                    if (m.Carry is { } s)
                    {
                        b.Restore(s);
                    }
                    return b;
                },
            }).ToList();

            Arena.ArenaOutcome o = Arena.RunOnce(teamA, wave.Select(g => Guard(g, injured)).ToList(), rng);
            dur += o.Duration;

            // 回写每个人的身体（TeamAEnd 与 teamA 同序）。
            for (int i = 0; i < alive.Count && i < o.TeamAEnd.Count; i++)
            {
                BodySnapshot snap = o.TeamAEnd[i];
                if (snap.IsDead || snap.BledOut)
                {
                    alive[i].Dead = true;
                }
                else
                {
                    // 波间止血：队员能给自己缠一圈绷带，但治不好骨折、更长不回断掉的手。
                    // （不止血的话所有人都会在关卡里流血死——那是在建模"没人会包扎"，不是在建模这一关。）
                    snap.Bleeding = new List<string>();
                }
                alive[i].Carry = snap;
            }

            if (o.WinnerTeam != 0)
            {
                won = false;
                break;
            }
            cleared++;
        }

        List<BodySnapshot> survivors = team
            .Where(m => !m.Dead && m.Carry is not null)
            .Select(m => m.Carry!)
            .ToList();

        return new Toll(
            Won: won,
            WavesCleared: cleared,
            Dead: team.Count(m => m.Dead),
            // 切除 + 损毁 = 永久少一块（断手 / 砸烂的腿）。这是最贵的代价：治不回来。
            Severed: survivors.Sum(b => b.Severed.Count + b.Destroyed.Count),
            Fractured: survivors.Sum(b => b.Fractured.Count),
            Disabled: survivors.Sum(b => b.Disabled.Count),
            Duration: dur);
    }

    /// <summary>
    /// 一个场景跑完 <see cref="Seeds"/> 次之后的<b>账</b>（不是只有胜率——§2 通则③「胜率不是成本」）。
    /// <para>🔴 <b>报告排版与护栏测试共用这一条码路</b>（<c>GoldfingerCalibrationDocTests</c> 拿它跟 research 文档对账）——
    /// 谁再抄一份就会像 991b777 那份 born-stale 报告一样悄悄漂掉。</para>
    /// </summary>
    public readonly record struct ScenarioStat(
        double WinRate, int WonCount, double AvgDead, double AvgSevered,
        double AvgFractured, double PyrrhicRate, double FlawlessRate, double AvgWavesCleared);

    /// <summary>
    /// 跑一个场景 <see cref="Seeds"/> 次，出账。<b>纯计算、不排版、不写文件</b> ⇒ 可被单测直接调。
    /// </summary>
    public static ScenarioStat Measure(
        Weapon kit, IReadOnlyList<IReadOnlyList<GangGuard>> waves, bool injured)
    {
        var tolls = new List<Toll>(Seeds);
        for (int seed = 0; seed < Seeds; seed++)
        {
            tolls.Add(Campaign(kit, waves, new SystemRandomSource(30260714 + seed * 131), injured));
        }

        var won = tolls.Where(t => t.Won).ToList();

        // 「惨胜」= 赢了，但有人死了 / 永久少了一块 / 骨折了。赢，不等于没付代价。
        return new ScenarioStat(
            WinRate: (double)won.Count / Seeds,
            WonCount: won.Count,
            AvgDead: won.Count == 0 ? 0 : won.Average(t => t.Dead),
            AvgSevered: won.Count == 0 ? 0 : won.Average(t => t.Severed),
            AvgFractured: won.Count == 0 ? 0 : won.Average(t => t.Fractured),
            PyrrhicRate: won.Count == 0
                ? 0
                : (double)won.Count(t => t.Dead > 0 || t.Severed > 0 || t.Fractured > 0) / won.Count,
            FlawlessRate: won.Count == 0
                ? 0
                : (double)won.Count(t => t.Dead == 0 && t.Severed == 0 && t.Fractured == 0 && t.Disabled == 0) / won.Count,
            AvgWavesCleared: tolls.Average(t => t.WavesCleared));
    }

    /// <summary>
    /// 把一个场景的账排成 research 文档里的<b>那一行</b>。
    /// <para>🔴 <b>排版也是单一源</b>：报告与 <c>GoldfingerCalibrationDocTests</c> 都用这个函数 ⇒
    /// 测试拿它跟文档里的行<b>逐字比对</b>，数变了或排版变了都当场红。</para>
    /// </summary>
    public static string Row(string title, int waveCount, ScenarioStat s) =>
        s.WonCount == 0
            ? $"  {title,-14} 胜率 {s.WinRate,6:P1} │ 平均推进 {s.AvgWavesCleared,4:0.0}/{waveCount} 波后团灭"
            : $"  {title,-14} 胜率 {s.WinRate,6:P1}"
              + $" │ 阵亡 {s.AvgDead,4:0.00} │ 永久残缺 {s.AvgSevered,4:0.00} │ "
              + $"骨折 {s.AvgFractured,4:0.00} │ 惨胜 {s.PyrrhicRate,5:P0} │ "
              + $"全身而退 {s.FlawlessRate,5:P0}";

    private static void Scenario(
        StringBuilder sb, string title, Weapon kit,
        IReadOnlyList<IReadOnlyList<GangGuard>> waves, bool injured)
    {
        sb.AppendLine(Row(title, waves.Count, Measure(kit, waves, injured)));
    }

    // ── 三种打法的波次分组（公开＝报告与护栏测试共用单一源，别各写各的）──────────────
    // 分组 = SpawnGoldfingerGuards 的空间布点（roster 顺序即编制：0-1 深处 / 2 中段 / 3 近入口）。
    // 玩家从南边入口进 ⇒ 先撞近入口，再中段，最后深处（军械柜/银库就在那两个人背后）。

    /// <summary>逐波推进 1→1→2：正常推进，按纵深一波波撞上去（伤在波间累积）。</summary>
    public static IReadOnlyList<IReadOnlyList<GangGuard>> PushWaves { get; } =
        new IReadOnlyList<GangGuard>[]
        {
            new[] { GoldfingerGang.Roster[3] },                    // 近入口
            new[] { GoldfingerGang.Roster[2] },                    // 中段
            new[] { GoldfingerGang.Roster[0], GoldfingerGang.Roster[1] }, // 深处
        };

    /// <summary>惊动全据点 4：一次性 3v4 ——<b>开枪的后果</b>（噪音招怪见 <see cref="GoldfingerGang.AlertedBy"/>）。</summary>
    public static IReadOnlyList<IReadOnlyList<GangGuard>> AllAtOnce { get; } =
        new IReadOnlyList<GangGuard>[] { GoldfingerGang.Roster.ToList() };

    /// <summary>逐个清哨 1×4：弓/匕首潜行，绕开哨兵扫视一个个摸掉（伤累积）。</summary>
    public static IReadOnlyList<IReadOnlyList<GangGuard>> OneByOne { get; } =
        GoldfingerGang.Roster.Select(g => (IReadOnlyList<GangGuard>)new[] { g }).ToArray();

    public static void Run()
    {
        IReadOnlyList<GangGuard> r = GoldfingerGang.Roster;

        IReadOnlyList<IReadOnlyList<GangGuard>> pushWaves = PushWaves;
        IReadOnlyList<IReadOnlyList<GangGuard>> allAtOnce = AllAtOnce;
        IReadOnlyList<IReadOnlyList<GangGuard>> oneByOne = OneByOne;

        var sb = new StringBuilder();
        sb.AppendLine("# 金手指帮之战（Arena·无空间下界）");
        sb.AppendLine();
        sb.AppendLine($"敌方 {r.Count} 名守备：{string.Join(" / ", r.GroupBy(g => g.Arm).Select(g => $"{g.Count()}×{g.Key}"))}；");
        sb.AppendLine($"伤情：{string.Join(" / ", r.GroupBy(g => g.Injury.Name).Select(g => $"{g.Count()}×{g.Key}"))}（读 GoldfingerGang.Roster，与运行时同一张表）。");
        sb.AppendLine($"玩家：{TeamSize} 人探索队，护甲＝皮夹克+长袖布衣（与守备同）。{Seeds} 次蒙特卡洛。");
        sb.AppendLine();
        sb.AppendLine("⚠️ Arena 无空间：无掩体、无先手、无逃跑，敌方手枪**无限子弹** ⇒ 每个数字都是**下界**。");
        sb.AppendLine("   波间假设：**止血但不回血**——HP 缺口 / 骨折 / 永久残缺全部带进下一波。");
        sb.AppendLine();

        (string name, Weapon w)[] kits =
        {
            ("棍棒(开局)", WeaponTable.Club()),
            ("匕首", WeaponTable.Dagger()),
            ("短剑", WeaponTable.Shortsword()),
            // 🔴 中期玩家**真实拿得到的那两把**（这一关排在中期，必须按中期的手牌算账，不能拿长剑/步枪替他答题）：
            //   · 消防斧 —— 消防站（西北起点，深度 0）和联合收割机仓库（东路，深度 1）都出，两条路都够得着。
            //   · 自制猎枪 —— 河边小屋（东起点）的枪柜；配方也造得出。它是中期唯一现实的火力。
            ("消防斧(中期)", WeaponTable.Axe()),
            ("自制猎枪(中期)", WeaponTable.ImprovisedShotgun()),
            ("长剑", WeaponTable.Longsword()),
            ("破甲锤", WeaponTable.Warhammer()),
            ("手枪", WeaponTable.Pistol()),
            ("步枪", WeaponTable.Rifle()),
        };

        foreach ((string name, Weapon w) in kits)
        {
            sb.AppendLine($"## 3 人同持「{name}」");
            Scenario(sb, "逐波推进 1→1→2", w, pushWaves, injured: true);
            Scenario(sb, "逐个清哨 1×4", w, oneByOne, injured: true);
            Scenario(sb, "惊动全据点 4", w, allAtOnce, injured: true);
            sb.AppendLine();
        }

        // ── 枪的代价：一枪叫醒几个人 ────────────────────────────────────────────
        // Arena 无空间、测不到噪音 ⇒ 光看上面的胜率表会得出"带枪最稳"的**错误结论**。
        // 把噪音接上（纯几何：GoldfingerGang.AlertedBy）才看得见真正的取舍。
        sb.AppendLine("## 🔴 枪的代价：在据点中央弄出一次动静，会叫醒几个人");
        sb.AppendLine();
        // 🔴 画布尺寸与探针位置一律读 GoldfingerGang 的单一事实源，**不写硬编码字面量**——
        //    此前这里硬写 2400×1600 / 0.55,0.40，与 ExplorationLevelSize（真源）之间零保障：
        //    画布一改，游戏里的招怪变了、这份报告还在印旧数，且不会有任何测试红。
        //    GoldfingerGang.LevelW/LevelH 由 GoldfingerGangTests 焊死在 ExplorationLevelSize 上。
        sb.AppendLine(
            $"   关卡 {GoldfingerGang.LevelW:0}×{GoldfingerGang.LevelH:0}；下表＝以**中段**"
            + $"（{GoldfingerGang.NoiseProbeX:0.00}, {GoldfingerGang.NoiseProbeY:0.00}，玩家推进必经）为噪音源，半径罩住的守备数。");
        sb.AppendLine("   ⇒ 把它和上面的胜率表**一起读**：叫醒的人越多，场景就越往「惊动全据点」那一行塌。");
        sb.AppendLine();
        (string name, double noise)[] noises =
        {
            ("弓（潜行）", 70), ("匕首", 90), ("短剑", 95), ("长剑", 120),
            ("破甲锤", 150), ("手枪", 350), ("冲锋枪", 500), ("步枪", 600), ("狙击枪", 700),
        };
        foreach ((string name, double noise) in noises)
        {
            int alerted = GoldfingerGang.AlertedBy(
                GoldfingerGang.NoiseProbeX, GoldfingerGang.NoiseProbeY,
                noise, GoldfingerGang.LevelW, GoldfingerGang.LevelH);
            string verdict = alerted <= 1 ? "只惊动交手的那个" : alerted <= 3 ? "招来一小撮" : "半个据点扑上来";
            sb.AppendLine($"  {name,-10} 噪音 {noise,4:0} px → 叫醒 {alerted}/{GoldfingerGang.Roster.Count} 人　{verdict}");
        }
        sb.AppendLine();
        sb.AppendLine("   ⚠️ 这解释了胜率表里那个陷阱：**枪在纸面上打得最狠（逐个清哨 99%），但一开枪，就没有「逐个」了**。");
        sb.AppendLine();

        sb.AppendLine("## 「他们带伤」值多少（逐波推进，同种子对照）");
        sb.AppendLine("   两行之差 = 「刚经历完异常战斗，状态都不是巅峰」这条设定在数值上的全部分量。");
        foreach ((string name, Weapon w) in kits)
        {
            sb.AppendLine($"### {name}");
            Scenario(sb, "vs 8 残兵", w, pushWaves, injured: true);
            Scenario(sb, "vs 8 满状态", w, pushWaves, injured: false);
        }

        string report = sb.ToString();
        Console.Write(report);
        SimReport.Write("docs/research/2026-07-14-goldfinger-calibration.md", report); // 出处戳 + 落盘（单一入口）
        Console.WriteLine("\n已写出 docs/research/2026-07-14-goldfinger-calibration.md");
    }
}
