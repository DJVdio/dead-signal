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
/// <item><b>胜率高 ≠ 便宜</b>：持棍棒劫掠者胜率 78.7%，但 **74.3% 的胜场留下骨折**（每处 = 卧床 7 昼夜）；
///   丧尸胜率 100.0%，惨胜只有 0.2%。按胜率排丧尸<b>还更好打</b>；按**成本**排，棍棒劫掠者贵得离谱。</item>
/// <item><b>连场不能用胜率相乘去想</b>：单场（打匕首劫掠者）94.8% 胜率，就算每场之间满血复原，
///   连赢 8 场也只有 0.948⁸ ≈ 65.5%；而真实的"不治疗连打"里，能撑过第 3 个的只剩 18.7%，第 4 个 2.9%、
///   第 5 个 0.5%、第 6 个起<b>归零</b>。**"打 8 个劫掠者白捡 8 把武器"这个场景根本不存在。**</item>
/// </list>
///
/// <para>
/// 🔴 <b>[T63] 这些数字现在全部由 harness 插值生成，不再手写。</b>此前正文里的关键行是<b>硬编码</b>的
/// （87.7% / 80.7% / 98.5% / 单场 68% / 3.5% / 0.6%），与它<b>正上方那张自己生成的表</b>互相矛盾，
/// 而 <c>CLAUDE.md</c> 又抄了第三套（96.5% / 66% / 81.3% / 3%）。一份标着"机器生成——勿手改"的报告
/// 不能自己打自己的脸 ⇒ 关键行改成从实测统计里插值。
/// </para>
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

    /// <summary>
    /// [T63] <b>③ 每把武器 × 三种对手：胜率 <u>和</u> 代价并排。</b>
    /// <para>
    /// 存在理由：<c>weaponsweep</c> 的「表 7 实战胜率」只有胜率一列 —— 而<b>胜率一个字都没说你付了什么</b>。
    /// 拿着它排武器，就会得出「棍棒打丧尸 98.9%、随便打」这种结论，可实际上钝器的每一次胜利
    /// 都可能<b>换来一处要卧床 7 昼夜的骨折</b>。这张表把两者钉在同一行里，让「便宜」和「打得赢」再也不能混为一谈。
    /// </para>
    /// <para>三个对手与「表 7」<b>逐列对齐</b>（同样的护甲组、同样的敌方武器），所以两张表可以直接并排读。</para>
    /// </summary>
    private static void AppendWeaponCostSweep(StringBuilder sb)
    {
        sb.AppendLine("## ③ 每把武器：打得赢 vs 打得起（胜率与代价并排）");
        sb.AppendLine();
        sb.AppendLine("我方护甲＝中甲（皮夹克+长袖布衣）。每格 2,000 场。**代价列均为「胜场中的占比」**——");
        sb.AppendLine("即「赢了之后还是留下了这个伤」的概率，输掉的场次不计（死人不必谈代价）。");
        sb.AppendLine();
        sb.AppendLine("> **骨折**＝每处齐装卧床 7 昼夜（占床、不能干活、不能站岗）。**断肢/报废＝永久**。");
        sb.AppendLine("> **惨胜**＝赢了但留下骨折/断肢/重度失血昏迷之一。");
        sb.AppendLine();

        var opponents = new (string Name, Func<DuelFighter> Make)[]
        {
            ("丧尸", Zombie),
            ("长剑手·中甲", () => Fighter("长剑手", WeaponTable.Longsword(), MidKit())),
            ("长剑重装·重甲", () => Fighter("长剑重装", WeaponTable.Longsword(), HeavyKit())),
        };

        foreach (var (foeName, makeFoe) in opponents)
        {
            sb.AppendLine($"### vs {foeName}");
            sb.AppendLine();
            sb.AppendLine("| 武器 | 类型 | 胜率 | **惨胜** | 毫发无伤 | 骨折率 | 断肢/报废 |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");

            foreach (var w in WeaponTable.Arsenal())
            {
                int wins = 0, fx = 0, lost = 0, pyr = 0, clean = 0;
                for (int seed = 0; seed < Seeds; seed++)
                {
                    var me = Fighter("幸存者", w, MidKit());
                    var r = new DuelEngine(new SystemRandomSource(5550713 + seed * 131)).Run(me, makeFoe());
                    if (r.Winner != "幸存者") continue;

                    wins++;
                    var toll = TollOn(r, "幸存者");
                    if (toll.Fractures > 0) fx++;
                    if (toll.PartsLost > 0) lost++;
                    if (toll.Pyrrhic) pyr++;
                    if (toll.Unscathed) clean++;
                }

                double w2 = Math.Max(1, wins);
                string kind = (w.DamageType == DamageType.Blunt ? "钝" : "锐")
                    + (w.IsRanged ? "·远程" : "·近战");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {w.Name} | {kind} | {wins * 100.0 / Seeds:F1} % | **{pyr * 100.0 / w2:F1} %** | {clean * 100.0 / w2:F1} % | {fx * 100.0 / w2:F1} % | {lost * 100.0 / w2:F1} % |"));
            }

            sb.AppendLine();
        }

        sb.AppendLine("⚠️ **这张表测不到的**（`DuelEngine` 是 **1v1 / 无距离 / 无走位 / 无多目标**，且**不建模噪音与弹药**）：");
        sb.AppendLine("远程武器在此**没有射程与先手优势**（那属实时层）⇒ 枪弩的胜率被系统性低估；");
        sb.AppendLine("反过来，霰弹枪的短射程代价、以及「一发散射同时打中多只」的清群优势，**这里一个都测不出**。");
    }

    private static DuelFighter Fighter(string name, Weapon w, ArmorLayer[] armor) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = w } },
        Armor = armor,
        BodyFactory = HumanBody.NewBody,
    };

    private static ArmorLayer[] MidKit() => new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() };

    private static ArmorLayer[] HeavyKit() =>
        new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() };

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

        // 🔴 关键行的数字必须**从实测里长出来**，不能手写。
        // 此前 164~166 / 211~212 行是**硬编码**的散文数字（87.7% / 80.7% / 98.5% / 3.5% / 0.6%），
        // 与它们正上方那张机器生成的表**互相矛盾**（表里是 78.7% / 74.3% / 100.0%），
        // 而 CLAUDE.md 又抄了第三套（96.5% / 66% / 81.3%）。一份标着"机器生成——勿手改"的报告
        // 自己打自己的脸，还把错数字喂给项目纪律文档 ⇒ 全部改成插值。
        var stat = new Dictionary<string, (double Win, double Pyr, double Fx)>();

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
            stat[name] = (wins * 100.0 / Seeds, pyr * 100.0 / w, fx * 100.0 / w);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {wins * 100.0 / Seeds:F1} % | {deaths * 100.0 / Seeds:F1} % | **{pyr * 100.0 / w:F1} %** | {clean * 100.0 / w:F1} % | {fx * 100.0 / w:F1} % | {lost * 100.0 / w:F1} % | {dying * 100.0 / w:F1} % |"));
        }

        sb.AppendLine();
        sb.AppendLine("- **惨胜** = 赢了，但留下了长期/不可逆代价：骨折、断肢、部位报废，或是打到重度失血昏迷线。");
        sb.AppendLine("- **骨折只来自天然钝器** ⇒ 匕首/手枪劫掠者打不出骨折，棍棒/破甲锤才会。每处骨折 = 齐装卧床 **7 昼夜**（占床、不能干活、不能站岗）。");
        sb.AppendLine("- **断肢 / 报废 = 永久**（断手不能持械；两只手都没了 ⇒ 操作能力归零，这个人再也上不了战场）。");
        sb.AppendLine();
        var club = stat["持棍棒劫掠者"];
        var zed = stat["丧尸"];
        sb.AppendLine("### 🔴 这张表最该记住的一行");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"**持棍棒劫掠者胜率 {club.Win:F1}%，但 {club.Fx:F1}% 的胜场留下骨折（每处＝卧床 7 昼夜、占床、不能干活、不能站岗）；**"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"**丧尸胜率 {zed.Win:F1}%，惨胜只有 {zed.Pyr:F1}%。**"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"按**胜率**排，丧尸({zed.Win:F1}%)比棍棒劫掠者({club.Win:F1}%)还好打；按**成本**排，棍棒劫掠者贵得离谱"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"（惨胜 {club.Pyr:F1}% vs {zed.Pyr:F1}%）。**这就是为什么胜率不能当成本用。**"));
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
        double single = stat["持匕首劫掠者"].Win / 100.0;
        double naive = Math.Pow(single, GauntletLength) * 100.0;
        sb.AppendLine("**「打赢 8 个持械劫掠者一趟白捡 8 把武器」这个场景根本不存在。**");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"单场胜率 {single * 100.0:F1}%，就算每场之间**满血复原**，连赢 {GauntletLength} 场也只有 {single:F3}^{GauntletLength} ≈ **{naive:F1}%**；"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"而在不治疗的连打里，能撑过第 3 个的只剩 {alive[3] * 100.0 / GauntletRounds:F1}%、第 4 个 {alive[4] * 100.0 / GauntletRounds:F1}%。"));
        sb.AppendLine();
        sb.AppendLine("⇒ **做掉落/经济分析时，正确的框架是「这场仗要拿多少伤病和人命去换」，而不是「胜率 × 敌人数」。**");
        sb.AppendLine();

        AppendWeaponCostSweep(sb);

        SimReport.Write(outPath, sb.ToString()); // 出处戳 + 落盘（含建目录）
        Console.WriteLine($"已写出 {outPath}");
    }
}
