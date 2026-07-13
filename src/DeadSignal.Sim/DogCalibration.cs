using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 布鲁斯（狗）战斗单元校准 harness（param-calibration）。
/// 目标（用户口径）：布鲁斯 1v1 丧尸"难独杀也难被速杀"——胜率约 30~45%、平均战斗时长显著长于人类武器对决（缠斗感）；
/// 配道格（低配武器）2v1 应稳赢。
///
/// 手段：复用 <see cref="DuelEngine"/>（真实血量/部位/失血/效果结算）跑聚合胜率+平均时长。
/// 布鲁斯用新增的 <see cref="DuelFighter.DodgeChance"/> 轴表达"高闪避"，DogBite 低伤区间表达"低伤"。
/// 移速（150 vs 丧尸 55）是空间层（追得上/留得住）属性，纯数值对决不建模——列入 playtest。
/// 2v1 走内置极简竞技场 <see cref="Arena"/>（同 DuelEngine 的伤害/失血原语，聚焦火力目标选择）。
/// </summary>
public static class DogCalibration
{
    private const int Seeds = 4000;

    // ---- 参战方工厂（数值全部读 WeaponTable 权威源）----

    private static DuelFighter Bruce() => new()
    {
        Name = "布鲁斯",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.DogBite(), RequiresHand = false } },
        Armor = System.Array.Empty<ArmorLayer>(),
        DodgeChance = 0.45, // = Dog.DodgeChance（校准定值）
    };

    // 丧尸穿生前的破衣服：每场按 ZombieOutfit 的加权预设现抽（80% 至少还剩一件），叠在腐皮之外。
    // 光身丧尸（旧口径）的对照见 NakedZombie()。
    private static DuelFighter Zombie() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        ArmorFactory = ZombieOutfit.RollArmor,
    };

    // 低配武器道格：长袖布衣 + 棍棒（"低配"基线，验证 2v1 稳赢）。
    private static DuelFighter DougLowKit() => new()
    {
        Name = "道格",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Club() } },
        Armor = new[] { ArmorTable.LongSleeveShirt() },
    };

    // 人类武器对决基线（时长参照物）：匕首幸存者 vs 丧尸。
    private static DuelFighter DaggerSurvivor() => new()
    {
        Name = "匕首幸存者",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Dagger() } },
        Armor = new[] { ArmorTable.LongSleeveShirt() },
    };

    /// <summary>批次1~2 战斗基线复核（里程碑7）：确认新系统未破坏旧基线。控制台输出。</summary>
    public static void Baselines()
    {
        var zombie = Zombie();
        // 对称护甲（皮夹克+长袖布衣），隔离武器相对强度——步枪的射程/先手优势在纯数值对决里不建模，故 stat-duel 会低估步枪。
        ArmorLayer[] sym() => new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() };
        DuelFighter Rifle() => new()
        {
            Name = "步枪手",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Rifle() } },
            Armor = sym(),
        };
        DuelFighter Longsworder() => new()
        {
            Name = "长剑手",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
            Armor = sym(),
        };
        Console.WriteLine("| 基线对局 | A 胜率 | 目标 | 平均时长s |");
        Console.WriteLine("|---|---:|---|---:|");
        var d1 = Duel(DaggerSurvivor(), zombie);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| 匕首幸存者 vs 丧尸 | {d1.AWinRate:P1} | ~90% | {d1.AvgDuration:F1} |"));
        var d2 = Duel(Rifle(), Longsworder());
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| 步枪手 vs 长剑手(对称甲·无射程建模) | {d2.AWinRate:P1} | 70-80%* | {d2.AvgDuration:F1} |"));
        Console.WriteLine("*70-80% 目标含射程/先手优势(实时层)，纯数值对决不建模故偏低；此列只验武器相对强度未崩。");
    }

    /// <summary>参数扫描：dodge × 咬合间隔 × 咬伤上限 → 布鲁斯 solo 胜率/时长，用于定值。控制台输出。</summary>
    public static void Sweep()
    {
        var zombie = Zombie();
        double baseline = Duel(DaggerSurvivor(), zombie).AvgDuration;
        Console.WriteLine($"匕首基线时长 {baseline:F1}s（目标：布鲁斯显著更长）");
        Console.WriteLine("| dodge | 间隔s | 咬伤 | solo胜率 | 时长s |");
        Console.WriteLine("|---:|---:|---:|---:|---:|");
        double[] dodges = { 0.30, 0.35, 0.40, 0.45, 0.50 };
        double[] intervals = { 1.6, 2.0, 2.3, 2.6 };
        (int lo, int hi)[] dmgs = { (1, 4), (2, 6) };
        foreach (var (lo, hi) in dmgs)
        foreach (double iv in intervals)
        foreach (double dodge in dodges)
        {
            var bruce = new DuelFighter
            {
                Name = "布鲁斯",
                Weapons = new[] { new WeaponMount { Weapon = new Weapon { Name = "撕咬", DamageMin = lo, DamageMax = hi, Penetration = 0.10, DamageType = DamageType.Sharp, AttackInterval = iv }, RequiresHand = false } },
                Armor = System.Array.Empty<ArmorLayer>(),
                DodgeChance = dodge,
            };
            var s = Duel(bruce, zombie);
            string flag = s.AWinRate is >= 0.30 and <= 0.45 ? " ★" : "";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {dodge:F2} | {iv} | {lo}-{hi} | {s.AWinRate:P1} | {s.AvgDuration:F1} |{flag}"));
        }
    }

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 布鲁斯（狗）战斗单元校准");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"样本：每对局 {Seeds} 个种子　DogBite={Fmt(WeaponTable.DogBite())}　闪避={Bruce().DodgeChance:P0}　爪击={Fmt(WeaponTable.ZombieClaw())}");
        sb.AppendLine();

        // 1v1 布鲁斯 vs 丧尸
        var bruceVsZombie = Duel(Bruce(), Zombie());
        // 基线：匕首幸存者 vs 丧尸（时长参照）
        var daggerVsZombie = Duel(DaggerSurvivor(), Zombie());
        // 道格（低配）solo vs 丧尸
        var dougVsZombie = Duel(DougLowKit(), Zombie());

        sb.AppendLine("## 1v1");
        sb.AppendLine("| 对局 | A 胜率 | 平局/超时率 | 平均时长(s) |");
        sb.AppendLine("|---|---:|---:|---:|");
        Emit(sb, "布鲁斯 vs 丧尸", bruceVsZombie);
        Emit(sb, "匕首幸存者 vs 丧尸（基线）", daggerVsZombie);
        Emit(sb, "道格·棍棒长袖布衣 vs 丧尸", dougVsZombie);
        sb.AppendLine();

        // 2v1 道格(低配)+布鲁斯 vs 丧尸
        var twoVsOne = Arena.Run(new[] { DougLowKit(), Bruce() }, new[] { Zombie() }, Seeds);
        sb.AppendLine("## 2v1（竞技场）");
        sb.AppendLine("| 对局 | 己方全胜率 | 己方零阵亡率 | 平均时长(s) |");
        sb.AppendLine("|---|---:|---:|---:|");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"| 道格·棍棒 + 布鲁斯 vs 丧尸 | {twoVsOne.TeamAWinRate:P1} | {twoVsOne.NoLossRate:P1} | {twoVsOne.AvgDuration:F1} |");
        sb.AppendLine();

        sb.AppendLine("## 判定");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 布鲁斯 solo 胜率 {bruceVsZombie.AWinRate:P1}（目标 30~45%）");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 布鲁斯 solo 平均时长 {bruceVsZombie.AvgDuration:F1}s vs 匕首基线 {daggerVsZombie.AvgDuration:F1}s（目标：显著更长=缠斗感）");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 2v1 己方全胜率 {twoVsOne.TeamAWinRate:P1}（目标：稳赢≈≥90%）");

        var report = sb.ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, report);
        Console.WriteLine($"已写出 {outPath}");
        Console.WriteLine();
        Console.Write(report);
    }

    private static string Fmt(Weapon w) =>
        string.Create(CultureInfo.InvariantCulture, $"{w.DamageMin}-{w.DamageMax}/穿{w.Penetration:P0}/{w.AttackInterval}s");

    private readonly record struct DuelStat(double AWinRate, double DrawRate, double AvgDuration);

    private static DuelStat Duel(DuelFighter a, DuelFighter b)
    {
        int aWins = 0, draws = 0;
        double durSum = 0;
        for (int seed = 0; seed < Seeds; seed++)
        {
            // 每场重建（Body 有状态）。
            var fa = Clone(a);
            var fb = Clone(b);
            var engine = new DuelEngine(new SystemRandomSource(20260712 + seed * 131));
            var r = engine.Run(fa, fb);
            durSum += r.DurationSeconds;
            if (r.Winner == a.Name) aWins++;
            else if (r.Winner is null) draws++;
        }
        return new DuelStat((double)aWins / Seeds, (double)draws / Seeds, durSum / Seeds);
    }

    private static void Emit(StringBuilder sb, string label, DuelStat s) =>
        sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {s.AWinRate:P1} | {s.DrawRate:P1} | {s.AvgDuration:F1} |");

    // DuelFighter 是 init-only 值配置，直接复用同实例即可（Body 由 BodyFactory 每场新建）。此处保留 hook 便于将来带状态时深拷。
    private static DuelFighter Clone(DuelFighter f) => f;
}
