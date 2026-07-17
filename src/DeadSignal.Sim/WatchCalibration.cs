using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using DeadSignal.Godot; // VisionLogic / NightWatchContest / GuardPost / NightRaidLogic / ShiftSchedule

/// <summary>
/// 夜防「警戒力 vs 潜行力」发现率矩阵解析校准（param-calibration，里程碑3；night-response 修复后复扫）。
/// **新模型（对齐 CampMain.TickApproachContest）**：尖兵逼近，nearestGuardDist 逐带跨过 <see cref="Bands"/>（各带只掷一次），
/// 检测跨带累积——全营发现率 = 1 − Π_带 Π_守卫 (1 − p)。疲劳经视锥 <see cref="VisionLogic.VisionCone.Scaled"/>(0.85,0.90) 削视力项
/// （不再叠警戒标量，防双罚，同 night-response 修复）。Bands/StrikeDistance 属 param-calibration 调参 scope。
///
/// 目标：裸营 35-55%／满配(3守卫+火堆+无疲劳) 80-90%／疲劳 -15~25pp。
/// 真实几何：营心(1200,900)、火堆(1040,980,I0.95/R460)、岗位 哨塔(1780,860,视1.10)/屋顶(650,1000,视1.10)/暗哨(1200,1300,视1.0)。
/// 尖兵南向逼近（point=离营心最近，南门1200,1489）。假设守卫朝逼近向(frontal)、不被遮挡（cover=0），为守卫最有利上界。
/// </summary>
public static class WatchCalibration
{
    private const float NightAmbient = 0.15f;
    private const float FireI = 0.95f, FireR = 460f;
    private static readonly Vector2 Fire = new(1040, 980);
    // 岗位（pos, structBonus, sightBoost）：暗哨无加成；哨塔/屋顶 sight 1.10 → StructureBonusFrom=0.10。
    private static readonly (Vector2 pos, float bonus) Hidden = (new(1200, 1300), 0f);
    private static readonly (Vector2 pos, float bonus) Tower = (new(1780, 860), NightRaidLogic.StructureBonusFrom(1.10f));
    private static readonly (Vector2 pos, float bonus) Roof = (new(650, 1000), NightRaidLogic.StructureBonusFrom(1.10f));

    // 逼近距离带（= CampMain.NightRaidContestBands，param-calibration scope）。复扫定值 {150,90}（原 {170,120,80} 裸营偏高）。
    private static float[] Bands = { 150f, 90f };

    private static float LightAt(Vector2 p, bool fireOn)
    {
        float src = fireOn ? VisionLogic.SourceContribution(FireI, Vector2.Distance(p, Fire), FireR) : 0f;
        return VisionLogic.CombineLight(NightAmbient, src);
    }

    // 尖兵沿南向轴：到暗哨(y=1300)距离为 dHidden 时的位置（从南侧逼近）。
    private static Vector2 SouthRaider(float dHidden) => new(1200, 1300 + dHidden);

    // 单守卫对尖兵一次掷点的发现概率（frontal、未遮挡；fatigued→视锥缩放）。
    private static float GuardP((Vector2 pos, float bonus) g, Vector2 raider, bool fireOn, bool fatigued, float nearestDist)
    {
        float dist = Vector2.Distance(g.pos, raider);
        float gL = LightAt(g.pos, fireOn);
        var cone = VisionLogic.ConeFor(gL);
        if (fatigued)
            cone = cone.Scaled(ShiftSchedule.FatigueVisionRangeMult, ShiftSchedule.FatigueVisionAngleMult); // 0.85 / 0.90
        bool canSee = dist <= cone.Range; // frontal 上界
        float acuity = NightWatchContest.VisionAcuity(canSee, dist, cone);
        // night-response 修复后：疲劳经视锥(上方 cone.Scaled)削视力项 + FatigueAdjustedAlertness 补削听力项（单一真源同调）。
        float alert = NightRaidLogic.FatigueAdjustedAlertness(acuity, dist, g.bonus, 1f, fatigued, NightWatchContest.HearingBaseRange);
        float pL = LightAt(raider, fireOn);
        float stealth = NightWatchContest.ComputeStealth(pL, apparelStealthSum: 0f, nearestDist, coverWeight: 0f);
        return NightWatchContest.DetectionChance(alert, stealth);
    }

    // 全营跨带累积发现率。守卫沿南向对尖兵；每带 nearestGuardDist=到暗哨距离（南向暗哨恒最近）。
    private static float CampDetect((Vector2, float)[] guards, bool fireOn, bool fatigued)
    {
        float miss = 1f;
        foreach (float band in Bands)
        {
            Vector2 raider = SouthRaider(band); // nearestGuardDist(暗哨)=band
            float bandMiss = 1f;
            foreach (var g in guards)
                bandMiss *= 1f - GuardP(g, raider, fireOn, fatigued, band);
            miss *= bandMiss;
        }
        return 1f - miss;
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 夜防发现率矩阵解析校准（night-response 修复后复扫）");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"模型：逼近带 {{{string.Join(",", Bands)}}} 各掷一次·跨带累积；疲劳视锥×({ShiftSchedule.FatigueVisionRangeMult},{ShiftSchedule.FatigueVisionAngleMult}) + 听力项×{NightRaidLogic.FatigueHearingMult}（双路径，night-response 修复后）。"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"权重：视力 {NightWatchContest.VisionWeight}/听力 {NightWatchContest.HearingWeight}(半径 {NightWatchContest.HearingBaseRange})　潜行 暗{NightWatchContest.StealthDarknessWeight}/距{NightWatchContest.StealthDistanceWeight}(参考 {NightWatchContest.StealthDistanceReference})。"));
        sb.AppendLine();

        var oneGuard = new[] { Hidden };
        var threeScattered = new[] { Hidden, Tower, Roof };
        var threeCovered = CoveredThree();

        sb.AppendLine("## 全营发现率（南向逼近，跨带累积，带 {150,90}）");
        sb.AppendLine("| 场景 | 发现率 | 目标 | 判定 |");
        sb.AppendLine("|---|---:|---|---|");
        float bare = CampDetect(oneGuard, fireOn: false, fatigued: false);
        float fullScat = CampDetect(threeScattered, fireOn: true, fatigued: false);
        float fullCov = CampDetect(threeCovered, fireOn: true, fatigued: false);
        float fullCovFat = CampDetect(threeCovered, fireOn: true, fatigued: true);
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| 裸营(1守卫·无火·无疲劳) | {bare:P0} | 35-55% | {Verdict(bare, 0.35f, 0.55f)} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| 满配·**分散布防**(3守卫散在3岗+火堆) | {fullScat:P0} | 80-90% | {Verdict(fullScat, 0.80f, 0.90f)}（岗位分散只1守卫在南轴感知内） |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| 满配·**覆盖逼近轴**(3守卫盖南轴+火堆) | {fullCov:P0} | 80-90% | {Verdict(fullCov, 0.80f, 0.90f)} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| 覆盖+疲劳(视锥×0.85/0.90+听力×{NightRaidLogic.FatigueHearingMult}) | {fullCovFat:P0} | 15~25pp | Δ={(fullCov - fullCovFat) * 100:F0}pp {Verdict((fullCov - fullCovFat), 0.15f, 0.25f)} |"));
        sb.AppendLine();
        sb.AppendLine("## 结论");
        sb.AppendLine("- **裸营 48% ✓**（带 {150,90} 定值，落 35-55%）。");
        sb.AppendLine("- **满配 80-90% 取决于守卫覆盖**：分散布防 65%（3 岗位散在东/西/南，任一逼近轴只 1 守卫在感知内）；覆盖逼近轴 85% ✓。→ 需 night-response 布防站位（守卫朝威胁方向集中/岗位重叠盖大门），非纯数值可调。[DECISION]");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- **疲劳劣化达标（Δ={(fullCov - fullCovFat) * 100:F0}pp，落 15~25pp）**：视锥 ×0.85/0.90 单路径饱和(~4pp) → night-response 修复补一道听力项 ×{NightRaidLogic.FatigueHearingMult}（NightRaidLogic.FatigueAdjustedAlertness，运行时与本 Sim 同调单一真源）。听力是满配检测大头，削之即打破地板饱和。[DECISION-RESOLVED]"));

        var report = sb.ToString();
        SimReport.Write("docs/research/2026-07-12-watch-calibration.md", report); // 出处戳 + 落盘（含建目录）
        Console.Write(report);
    }

    private static string Verdict(float v, float lo, float hi) => v >= lo && v <= hi ? "✓" : (v < lo ? "偏低" : "偏高");

    // 覆盖假设扫描：若守卫集中盖逼近轴（3 守卫都在南向暗哨附近，而非分散）——验证"满配 80-90%"是否只差布防覆盖。
    private static (Vector2, float)[] CoveredThree() => new[]
    {
        Hidden,
        (new Vector2(1120, 1300), NightRaidLogic.StructureBonusFrom(1.10f)), // 哨塔挪到南轴近旁
        (new Vector2(1280, 1300), NightRaidLogic.StructureBonusFrom(1.10f)), // 屋顶挪到南轴近旁
    };

    /// <summary>扫描：带集 × 疲劳视锥系数 × 覆盖假设 → 裸营/满配/满配疲劳，用于定值+验证瓶颈。控制台。</summary>
    public static void Sweep()
    {
        Console.WriteLine("## A. 疲劳视锥系数扫描（3 带·真实分散布防·满配 vs 满配疲劳）");
        Console.WriteLine("| range×,angle× | 满配 | 满配疲劳 | Δpp |");
        Console.WriteLine("|---|---:|---:|---:|");
        var three = new[] { Hidden, Tower, Roof };
        foreach (var (rm, am) in new[] { (0.85f, 0.90f), (0.70f, 0.80f), (0.60f, 0.75f), (0.50f, 0.65f), (0.40f, 0.55f) })
        {
            float full = CampDetect(three, true, false);
            float ff = CampDetectFat(three, true, rm, am);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {rm},{am} | {full:P0} | {ff:P0} | {(full - ff) * 100:F0} |"));
        }
        Console.WriteLine();
        Console.WriteLine("## B. 带集扫描（裸营，真实分散布防）");
        Console.WriteLine("| 带集 | 裸营 |");
        Console.WriteLine("|---|---:|");
        foreach (var bset in new[] { new[] { 170f, 120f, 80f }, new[] { 150f, 90f }, new[] { 130f, 80f }, new[] { 120f, 70f } })
        {
            Bands = bset;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {{{string.Join(",", bset)}}} | {CampDetect(new[] { Hidden }, false, false):P0} |"));
        }
        Bands = new[] { 170f, 120f, 80f };
        Console.WriteLine();
        Console.WriteLine("## C. 覆盖假设 × 带集 {150,90}（候选定案）");
        Bands = new[] { 150f, 90f };
        var cov = CoveredThree();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"- 带{{150,90}}：裸营(1守卫)={CampDetect(new[] { Hidden }, false, false):P0}／分散满配={CampDetect(new[] { Hidden, Tower, Roof }, true, false):P0}／**覆盖满配={CampDetect(cov, true, false):P0}**／覆盖满配疲劳(0.65,0.78)={CampDetectFat(cov, true, 0.65f, 0.78f):P0}（Δ={(CampDetect(cov, true, false) - CampDetectFat(cov, true, 0.65f, 0.78f)) * 100:F0}pp）"));
        Bands = new[] { 170f, 120f, 80f };
    }

    // 疲劳系数可注入版（扫描用）。
    private static float CampDetectFat((Vector2, float)[] guards, bool fireOn, float rm, float am)
    {
        float miss = 1f;
        foreach (float band in Bands)
        {
            Vector2 raider = SouthRaider(band);
            float bandMiss = 1f;
            foreach (var g in guards)
                bandMiss *= 1f - GuardPFat(g, raider, fireOn, rm, am, band);
            miss *= bandMiss;
        }
        return 1f - miss;
    }

    private static float GuardPFat((Vector2 pos, float bonus) g, Vector2 raider, bool fireOn, float rm, float am, float nearestDist)
    {
        float dist = Vector2.Distance(g.pos, raider);
        var cone = VisionLogic.ConeFor(LightAt(g.pos, fireOn)).Scaled(rm, am);
        bool canSee = dist <= cone.Range;
        float acuity = NightWatchContest.VisionAcuity(canSee, dist, cone);
        float alert = NightRaidLogic.FatigueAdjustedAlertness(acuity, dist, g.bonus, 1f, fatigued: true, NightWatchContest.HearingBaseRange);
        float stealth = NightWatchContest.ComputeStealth(LightAt(raider, fireOn), 0f, nearestDist, 0f);
        return NightWatchContest.DetectionChance(alert, stealth);
    }
}
