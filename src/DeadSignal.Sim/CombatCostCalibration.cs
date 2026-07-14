using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// <b>战斗的真实成本</b>（Sim 模式 <c>cost</c>）—— 回答"打一场硬仗值不值"，而不是"能不能打赢"。
///
/// <para><b>为什么要有这个 harness（读之前先看这段，否则你多半又会算错）</b></para>
/// <para>
/// 用户原话：「一趟白捡……什么意思，<b>战斗难道不是成本吗</b>」。这句话纠正的是一类反复出现的分析错误：
/// 拿**胜率**当**成本**的代理指标，于是得出"打赢 8 个持械劫掠者 = 白捡 8 把武器 + 8 件皮夹克"这种结论。
/// </para>
/// <para>
/// 胜率只说"你能不能站着走出这一场"，它**一个字都没说**你为此付了什么：打光的弹药、要愈合 7 昼夜的骨折
/// （占床、不能干活、不能站岗）、永久断掉的手、跟死神赛跑的感染、耗材与有失败风险的手术、以及不可复活的死人。
/// </para>
/// <para>
/// 本 harness 量的就是这些：<b>打赢之后，胜者身上还剩什么。</b>两条实测结论足以说明胜率有多误导——
/// </para>
/// <list type="bullet">
/// <item><b>胜率高 ≠ 便宜</b>：持棍棒劫掠者胜率 87.7%，但 **80.7% 的胜场留下骨折**（每处 = 卧床 7 昼夜）；
///   丧尸胜率 98.5%，惨胜只有 0.3%。按胜率排两者接近；按**成本**排，棍棒劫掠者贵得离谱。
///   （[T53] 数字随流血口径回退 100/0.55 重算——旧值 96.6%/81.4% 是在一个**游戏并不运行**的热口径下算的。）</item>
/// <item><b>连场不能用胜率相乘去想</b>：单场 68% 胜率，连打 8 个 ⇒ 就算每场之间满血复原也只有 0.68⁸ ≈ 3.6%；
///   而真实的"不治疗连打"里，能撑过第 3 个的只剩 3.5%，第 4 个 0.6%。**"打 8 个劫掠者"这个场景根本不存在。**</item>
/// </list>
///
/// <para><b>⚠️ 本 harness 仍然量不到的成本</b>（用它的数字前必须知道，否则又会低估）：弹药消耗
/// （<see cref="DuelEngine"/> 不建模弹药，枪械按无限弹算）、战后的感染竞速与手术耗材/失败率、
/// 卧床期间的劳力损失与营地空转、士气/剧情后果。<b>所以它给出的是成本的下界，不是全部。</b></para>
/// </summary>
public static class CombatCostCalibration
{
    private const int Seeds = 2000;
    private const int GauntletRounds = 1000;
    private const int GauntletLength = 8;

    private static DuelFighter Survivor(Func<Body>? bodyFactory = null) => new()
    {
        Name = "幸存者",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
        Armor = new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() },
        BodyFactory = bodyFactory ?? HumanBody.NewBody,
    };

    private static DuelFighter Raider(Weapon w) => new()
    {
        Name = "敌",
        Weapons = new[] { new WeaponMount { Weapon = w } },
        Armor = new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() },
        BodyFactory = HumanBody.NewBody,
    };

    private static DuelFighter Zombie() => new()
    {
        Name = "敌",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        Armor = ArmorTable.ZombieHide().ToArray(),
        BodyFactory = HumanBody.NewZombieBody,
    };

    /// <summary>胜者在一场对决里付出的代价（从战报事件里读，不需要引擎暴露内部状态）。</summary>
    private readonly record struct Toll(int Fractures, int PartsLost, bool Bled, string BloodTier)
    {
        public bool Pyrrhic => Fractures > 0 || PartsLost > 0 || BloodTier == "重度昏迷";

        public bool Unscathed => Fractures == 0 && PartsLost == 0 && !Bled;
    }

    private static Toll TollOn(DuelResult r, string who)
    {
        int fx = 0, lost = 0;
        bool bled = false;
        string tier = "无";
        foreach (var e in r.Events)
        {
            if (e.Defender == who)
            {
                foreach (string t in e.Tags)
                {
                    if (t.StartsWith("骨折:", StringComparison.Ordinal)) fx++;
                    else if (t.StartsWith("切除:", StringComparison.Ordinal)) lost++;
                    else if (t.StartsWith("损毁:", StringComparison.Ordinal)) lost++;
                    else if (t.StartsWith("流血:", StringComparison.Ordinal)) bled = true;
                }
            }

            // 失血分级事件：Attacker = 本人、Defender 为空。
            if (e.Defender.Length == 0 && e.Attacker == who)
            {
                foreach (string t in e.Tags)
                {
                    if (t.StartsWith("失血:", StringComparison.Ordinal)) tier = t.Split(':')[1];
                }
            }
        }

        return new Toll(fx, lost, bled, tier);
    }

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 战斗的真实成本（Sim `cost` 模式，机器生成——勿手改）");
        sb.AppendLine();
        sb.AppendLine("> **胜率不是成本。** 这份表回答的是「打赢之后你还剩什么」，不是「你能不能打赢」。");
        sb.AppendLine("> 拿胜率当成本的代理指标，就会得出「打赢 8 个持械劫掠者 = 白捡 8 把武器」这种结论——");
        sb.AppendLine("> 而实测里，**你连第 3 个都打不过去**。");
        sb.AppendLine();
        sb.AppendLine("⚠️ **本表仍量不到的成本**：弹药消耗（对决引擎不建模弹药，枪械按无限弹算）、");
        sb.AppendLine("战后感染竞速、手术耗材与失败率、卧床期间的劳力损失。**所以这是成本的下界，不是全部。**");
        sb.AppendLine();

        // ---- ① 单场：按对手类型 ----
        sb.AppendLine("## ① 打赢一场，胜者身上还剩什么");
        sb.AppendLine();
        sb.AppendLine($"我方 = 中甲（皮夹克+长袖布衣）+ 长剑。每格 {Seeds:N0} 场。");
        sb.AppendLine();
        sb.AppendLine("| 对手 | 胜率 | 战死 | **惨胜** | 毫发无伤 | 骨折率 | 断肢/报废 | 打赢时濒死 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

        var foes = new (string Name, Func<DuelFighter> Make)[]
        {
            ("持匕首劫掠者", () => Raider(WeaponTable.Dagger())),
            ("持棍棒劫掠者", () => Raider(WeaponTable.Club())),
            ("持破甲锤劫掠者", () => Raider(WeaponTable.Warhammer())),
            ("持手枪劫掠者", () => Raider(WeaponTable.Pistol())),
            ("丧尸", Zombie),
        };

        foreach (var (name, make) in foes)
        {
            int wins = 0, deaths = 0, fx = 0, lost = 0, pyr = 0, clean = 0, dying = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                var r = new DuelEngine(new SystemRandomSource(4460713 + seed * 131)).Run(Survivor(), make());
                if (r.Winner != "幸存者")
                {
                    deaths++;
                    continue;
                }

                wins++;
                var toll = TollOn(r, "幸存者");
                if (toll.Fractures > 0) fx++;
                if (toll.PartsLost > 0) lost++;
                if (toll.BloodTier == "重度昏迷") dying++;
                if (toll.Pyrrhic) pyr++;
                if (toll.Unscathed) clean++;
            }

            double w = Math.Max(1, wins);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {wins * 100.0 / Seeds:F1} % | {deaths * 100.0 / Seeds:F1} % | **{pyr * 100.0 / w:F1} %** | {clean * 100.0 / w:F1} % | {fx * 100.0 / w:F1} % | {lost * 100.0 / w:F1} % | {dying * 100.0 / w:F1} % |"));
        }

        sb.AppendLine();
        sb.AppendLine("- **惨胜** = 赢了，但留下了长期/不可逆代价：骨折、断肢、部位报废，或是打到重度失血昏迷线。");
        sb.AppendLine("- **骨折只来自天然钝器** ⇒ 匕首/手枪劫掠者打不出骨折，棍棒/破甲锤才会。每处骨折 = 齐装卧床 **7 昼夜**（占床、不能干活、不能站岗）。");
        sb.AppendLine("- **断肢 / 报废 = 永久**（断手不能持械；两只手都没了 ⇒ 操作能力归零，这个人再也上不了战场）。");
        sb.AppendLine();
        sb.AppendLine("### 🔴 这张表最该记住的一行");
        sb.AppendLine();
        sb.AppendLine("**持棍棒劫掠者胜率 87.7%，但 80.7% 的胜场留下骨折（每处＝卧床 7 昼夜、占床、不能干活、不能站岗）；**");
        sb.AppendLine("**丧尸胜率 98.5%，惨胜只有 0.3%。按胜率排两者差不多；按成本排，棍棒劫掠者贵得离谱。**");
        sb.AppendLine("按**胜率**排，棍棒劫掠者好打得多；按**成本**排，它贵得多。**这就是为什么胜率不能当成本用。**");
        sb.AppendLine();

        // ---- ② 连场：不治疗打到第几个会崩 ----
        sb.AppendLine("## ② 连续打 N 个持匕首劫掠者，中途不治疗");
        sb.AppendLine();
        sb.AppendLine($"{GauntletRounds:N0} 轮。部位 HP / 骨折 / 断肢 / 未包扎的伤口**全部跨场累积**；");
        sb.AppendLine("两场之间只回血量——**这已经是偏乐观的假设**（真实游戏里没手术的失血伤口是会一路恶化到死的）。");
        sb.AppendLine();
        sb.AppendLine("| 打到第几个 | 还活着并打赢 | 累计断肢/报废 |");
        sb.AppendLine("|---:|---:|---:|");

        var alive = new int[GauntletLength + 1];
        var lostCum = new double[GauntletLength + 1];
        for (int round = 0; round < GauntletRounds; round++)
        {
            var body = HumanBody.NewBody();
            var me = Survivor(() => body); // 同一具身体贯穿全程
            int lost = 0;
            for (int k = 1; k <= GauntletLength; k++)
            {
                var r = new DuelEngine(new SystemRandomSource(7770713 + round * 977 + k * 131))
                    .Run(me, Raider(WeaponTable.Dagger()));
                lost += TollOn(r, "幸存者").PartsLost;
                if (r.Winner != "幸存者")
                {
                    break;
                }

                alive[k]++;
                lostCum[k] += lost;
            }
        }

        for (int k = 1; k <= GauntletLength; k++)
        {
            string lostStr = alive[k] > 0
                ? (lostCum[k] / alive[k]).ToString("F2", CultureInfo.InvariantCulture)
                : "—";
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| 第 {k} 个 | {alive[k] * 100.0 / GauntletRounds:F1} % | {lostStr} |"));
        }

        sb.AppendLine();
        sb.AppendLine("**「打赢 8 个持械劫掠者一趟白捡 8 把武器」这个场景根本不存在。**");
        sb.AppendLine("单场胜率 68%，就算每场之间满血复原，连赢 8 场也只有 0.68⁸ ≈ **3.6%**；");
        sb.AppendLine("而在不治疗的连打里，能撑过第 3 个的只剩 3.5%、第 4 个 0.6%。");
        sb.AppendLine();
        sb.AppendLine("⇒ **做掉落/经济分析时，正确的框架是「这场仗要拿多少伤病和人命去换」，而不是「胜率 × 敌人数」。**");

        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"已写出 {outPath}");
    }
}
