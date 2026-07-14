using System.Globalization;
using System.Text;
using DeadSignal.Combat;
using DeadSignal.Godot; // HordeTimeline / RaidWave

/// <summary>
/// 尸潮终局节奏解析校准（param-calibration，里程碑4）。两部分：
///  ① 确定性波次时刻表（<see cref="HordeTimeline.WaveSize"/> 逐波规模 + 触发判据）。
///  ② 战斗吞吐推演：用 <see cref="Arena"/> 跑"典型营地战力 N 幸存者 vs 一波 M 丧尸"，看能否清波、清一波要多久、损几人，
///     据此推"必死但撑几波"是否成立（目标：非秒崩、非无感）。
/// 注意：纯战斗吞吐**不含空间要素**（围栏/大门破防漏斗、走位风筝、岗哨高地）——那些会显著延长坚守，
/// 故本推演是坚守时长的**下界**（无掩体最坏情形）。真实"几波"戏剧性含空间，归 playtest。
/// </summary>
public static class EndgameCalibration
{
    private const int Seeds = 2000;

    // 典型营地战力：4 幸存者 = 2 近战(长剑+皮甲) + 2 远程(手枪+长袖布衣)。远程假设弹道命中（同 Sim 口径）。
    private static DuelFighter Melee() => new()
    {
        Name = "近战幸存者",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
        Armor = new[] { ArmorTable.Leather(), ArmorTable.LongSleeveShirt() },
    };

    private static DuelFighter Ranged() => new()
    {
        Name = "远程幸存者",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Pistol() } },
        Armor = new[] { ArmorTable.LongSleeveShirt() },
    };

    // 丧尸穿生前的破衣服：每场逐只按 ZombieOutfit 的加权预设现抽（一波 M 只各穿各的），叠在腐皮之外。
    private static DuelFighter Zombie() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        BodyFactory = HumanBody.NewZombieBody, // 失血 1/3
        ArmorFactory = ZombieOutfit.RollArmor,
    };

    private static System.Collections.Generic.List<DuelFighter> Camp(int n)
    {
        var list = new System.Collections.Generic.List<DuelFighter>();
        for (int i = 0; i < n; i++) list.Add(i % 2 == 0 ? Melee() : Ranged());
        return list;
    }

    private static System.Collections.Generic.List<DuelFighter> Horde(int m)
    {
        var list = new System.Collections.Generic.List<DuelFighter>();
        for (int i = 0; i < m; i++) list.Add(Zombie());
        return list;
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 尸潮终局节奏解析校准");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"时限第 {HordeTimeline.DeadlineDay} 天　首波 {HordeTimeline.WaveBase}／递增 {HordeTimeline.WaveGrowth}／营地系数 {HordeTimeline.WaveCampFactor}　单波帽 {HordeTimeline.WaveCap}　并发帽 {HordeTimeline.MaxConcurrentSiege}　强制间隔 {HordeTimeline.WaveInterval}s　清阈 {HordeTimeline.WaveClearThreshold}"));
        sb.AppendLine();

        // ① 波次时刻表（campSize=4）
        int camp = 4;
        sb.AppendLine("## ① 逐波规模（营地 4 人）");
        sb.AppendLine("| 波次 | 规模 | 累计投放 |");
        sb.AppendLine("|---:|---:|---:|");
        int cumulative = 0;
        for (int w = 0; w < 10; w++)
        {
            int size = HordeTimeline.WaveSize(w, camp);
            cumulative += size;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {w} | {size} | {cumulative} |"));
        }
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 触发判据：首波立投；此后残敌≤{HordeTimeline.WaveClearThreshold} 或距上波≥{HordeTimeline.WaveInterval}s 即补下一波，且并发被 clamp 到 {HordeTimeline.MaxConcurrentSiege}−残敌。无「守住」出口，唯一终止=全灭。"));
        sb.AppendLine();

        // ② 战斗吞吐：4 人 vs 单波 M（下界，无掩体）
        sb.AppendLine("## ② 战斗吞吐：典型 4 人营地 vs 单波 M 丧尸（下界，无空间掩体）");
        sb.AppendLine("| 单波 M | 营地清波率 | 零阵亡率 | 平均耗时(s) |");
        sb.AppendLine("|---:|---:|---:|---:|");
        foreach (int m in new[] { 4, 6, 8, 10, 12, 16 })
        {
            var s = Arena.Run(Camp(4), Horde(m), Seeds);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {m} | {s.TeamAWinRate:P0} | {s.NoLossRate:P0} | {s.AvgDuration:F1} |"));
        }
        sb.AppendLine();
        sb.AppendLine("## 推演");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 首波规模 {HordeTimeline.WaveSize(0, camp)}（4 人营地）。对照上表清波率判：首波即压垮=秒崩；能清前几波但逐波损人=戏剧性坚守。"));

        var report = sb.ToString();
        System.IO.File.WriteAllText("docs/research/2026-07-12-endgame-calibration.md", report);
        Console.Write(report);
    }
}
