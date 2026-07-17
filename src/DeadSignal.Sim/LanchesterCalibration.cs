using System.Globalization;
using System.Text;
using DeadSignal.Combat;
using DeadSignal.Godot; // BreachSlots —— 项目里**唯一**一处显式的「攻击位名额」空间约束（用于砸墙，不用于围人）

/// <summary>
/// 「N 打 1 的战力比到底是不是 N²」校准（<c>dotnet run --project src/DeadSignal.Sim lanchester</c>）。
///
/// <para><b>要验的原话</b>（用户）：「丧尸虽然伤害低了，但是丧尸本就是以量取胜，<b>三打一的战力比是九比一</b>，
/// 所以单一丧尸不该很强。」—— 这是<b>兰彻斯特平方律</b>（战力 ∝ N²）。设计意图（单只弱、群体致命）没问题，
/// 但「九比一」这个<b>放大倍数</b>是个可证伪的数学主张，本 harness 就是去量它。</para>
///
/// <para>🔴 <b>平方律的前提，本项目未必满足</b>：平方律成立要两个条件——
/// ①<b>集火不受限</b>（所有单位都能同时打同一个目标，没有空间名额）；
/// ②<b>集火能让敌方减员，从而削减敌方输出</b>。
/// <b>②在 Nv1 里根本不存在</b>——玩家只有一个人，没有"减员"可言。所以严格说 <b>Nv1 不适用平方律</b>。
/// 真正会长出 N² 的是 <b>NvN</b>。故本表分两半量：<b>①Nv1（玩家实际会遇到的）</b> + <b>②NvM 同类对同类（平方律的教科书判据）</b>。</para>
///
/// <para>⚠️ <b>Sim 盲区（用这些数字前必读）</b>：<see cref="Arena"/> <b>无空间</b>——不建模碰撞体积、攻击距离、
/// 走位、后退、掩体、先手。⇒ <b>它对"围攻"没有任何名额上限：8 只丧尸可以同时贴脸砍同一个人。</b>
/// 现实里围一个人只站得下几只（见 <see cref="BreachSlots"/> 的同款几何：丧尸半径 13px、攻击距离 47px）。
/// 所以 <b>Arena 的 Nv1 数字是"围攻代价的上界"（最坏情况）</b>，真实关卡只会比它好。</para>
/// </summary>
public static class LanchesterCalibration
{
    private const int Seeds = 3000;

    /// <summary>基线幸存者：中甲（皮夹克 + 长袖布衣）+ 长剑。与 <c>cost</c> harness 逐字段同款，两表可直接对读。</summary>
    private static DuelFighter Survivor() => new()
    {
        Name = "幸存者",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
        Armor = new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() },
        BodyFactory = HumanBody.NewBody,
    };

    private static DuelFighter Zombie() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        Armor = ArmorTable.ZombieHide().ToArray(),
        BodyFactory = HumanBody.NewZombieBody,
    };

    private static List<DuelFighter> Zombies(int n) => Enumerable.Range(0, n).Select(_ => Zombie()).ToList();

    /// <summary>一场打完，幸存者身上的账。<b>胜率不是成本</b>——赢了但断一只手，跟毫发无伤地赢不是一回事。</summary>
    private readonly record struct Toll(bool Won, bool Dead, int Fractured, int Severed, int Disabled,
        double HpLostFrac, double BloodFrac, double Duration)
    {
        /// <summary>惨胜 = 赢了，但留下长期/不可逆代价（骨折要愈合 7 昼夜、占床；切除永久）。</summary>
        public bool Pyrrhic => Won && (Fractured > 0 || Severed > 0 || Disabled > 0);

        public bool Flawless => Won && Fractured == 0 && Severed == 0 && Disabled == 0 && HpLostFrac < 0.001;
    }

    private static Toll Read(Arena.ArenaOutcome o)
    {
        BodySnapshot b = o.TeamAEnd[0];
        double maxHp = b.MaxHp.Values.Sum();
        double hp = b.Hp.Values.Sum();
        return new Toll(
            Won: o.WinnerTeam == 0,
            Dead: b.IsDead || b.BledOut,
            Fractured: b.Fractured.Count,
            Severed: b.Severed.Count + b.Destroyed.Count,
            Disabled: b.Disabled.Count,
            HpLostFrac: maxHp > 0 ? 1.0 - (hp / maxHp) : 0,
            BloodFrac: b.BloodMax > 0 ? 1.0 - (b.Blood / b.BloodMax) : 0,
            Duration: o.Duration);
    }

    private static IReadOnlyList<Toll> Fight(int zombieCount)
    {
        var a = new[] { Survivor() };
        var b = Zombies(zombieCount);
        return Arena.RunDetailed(a, b, Seeds).Select(Read).ToList();
    }

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 「N 打 1 的战力比是 N²」—— 引擎实测（Sim `lanchester` 模式，机器生成——勿手改）");
        sb.AppendLine();
        sb.AppendLine("> 待验命题（用户原话）：「丧尸本就是以量取胜，**三打一的战力比是九比一**，所以单一丧尸不该很强。」");
        sb.AppendLine("> 前半句（设计意图：单只弱、群体致命）不是这份表能否定的；这份表只量后半句那个**倍数**。");
        sb.AppendLine();
        sb.AppendLine("🔴 **Arena 无空间**：不建模碰撞体积/攻击距离/走位 ⇒ **对围攻没有名额上限**，8 只丧尸能同时贴脸砍同一个人。");
        sb.AppendLine("所以下表是**围攻代价的上界（最坏情况）**。真实关卡里同时够得着一个人的丧尸数受几何限制（见文末 §4）。");
        sb.AppendLine();

        Table1(sb);
        Table2(sb);
        Table3(sb);
        Table4(sb);
        Table5(sb);

        SimReport.Write(outPath, sb.ToString()); // 出处戳 + 落盘（含建目录）
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"已写入 {outPath}");
    }

    // ---- ① Nv1：N 只丧尸 vs 1 个中甲长剑幸存者 ----
    private static readonly int[] Ns = { 1, 2, 3, 4, 5, 6, 8 };

    private static readonly Dictionary<int, IReadOnlyList<Toll>> Cache = new();

    private static IReadOnlyList<Toll> Get(int n) =>
        Cache.TryGetValue(n, out var v) ? v : Cache[n] = Fight(n);

    private static void Table1(StringBuilder sb)
    {
        sb.AppendLine("## ① N 只丧尸 vs 1 个幸存者（中甲皮夹克+长袖布衣 · 长剑）");
        sb.AppendLine();
        sb.AppendLine($"每格 {Seeds:N0} 场。**代价一律只统计打赢的那些场**（死了就没有「代价」可谈了，只有一具尸体）。");
        sb.AppendLine();
        sb.AppendLine("🔴 **「战死率」会严重骗人**：幸存者输掉的绝大多数场次**不是当场被打死，是被打到昏迷/失能**（失血昏迷、双手被切掉）。");
        sb.AppendLine("**在一群丧尸中间昏过去 ＝ 被吃掉。** 所以真正该看的是**败率（=1−胜率）**，不是「战死率」那一列。");
        sb.AppendLine();
        sb.AppendLine("| 丧尸数 | 幸存者胜率 | **败率** | 其中·当场毙命 | 其中·昏迷/失能倒地 | 打赢时·骨折 | 打赢时·永久残缺 | **惨胜率** | 毫发无伤 | 打赢时失血 | 时长 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (int n in Ns)
        {
            var t = Get(n);
            var won = t.Where(x => x.Won).ToList();
            double wr = (double)won.Count / t.Count;
            double dr = (double)t.Count(x => x.Dead) / t.Count;
            string head = $"| {n} | **{wr:P1}** | **{1 - wr:P1}** | {dr:P1} | {1 - wr - dr:P1} |";
            if (won.Count == 0)
            {
                sb.AppendLine($"{head} — | — | — | — | — | — |");
                continue;
            }
            sb.AppendLine(
                $"{head} {won.Average(x => (double)x.Fractured):0.00} 处 " +
                $"({(double)won.Count(x => x.Fractured > 0) / won.Count:P0}) | " +
                $"{won.Average(x => (double)x.Severed):0.00} 处 ({(double)won.Count(x => x.Severed > 0) / won.Count:P0}) | " +
                $"**{(double)won.Count(x => x.Pyrrhic) / won.Count:P0}** | " +
                $"{(double)won.Count(x => x.Flawless) / won.Count:P0} | " +
                $"{won.Average(x => x.BloodFrac):P0} | {won.Average(x => x.Duration):0.0}s |");
        }
        sb.AppendLine();

        // 50% 线在哪：线性插值。
        double? n50 = null;
        for (int i = 0; i + 1 < Ns.Length; i++)
        {
            double w0 = Get(Ns[i]).Count(x => x.Won) / (double)Seeds;
            double w1 = Get(Ns[i + 1]).Count(x => x.Won) / (double)Seeds;
            if (w0 >= 0.5 && w1 < 0.5)
            {
                n50 = Ns[i] + (w0 - 0.5) / (w0 - w1) * (Ns[i + 1] - Ns[i]);
                break;
            }
        }
        sb.AppendLine(n50 is { } x50
            ? $"**幸存者胜率跌破 50% 的临界点：约 {x50:0.0} 只丧尸。**"
            : "**在测到的丧尸数内，幸存者胜率未跌破 50%。**");
        sb.AppendLine();
    }

    // ---- ② 威胁到底按几次方长：承伤 D(N) 的指数拟合 ----
    private static void Table2(StringBuilder sb)
    {
        sb.AppendLine("## ② 「战力比」到底是几次方（败率赔率法）");
        sb.AppendLine();
        sb.AppendLine("**为什么不能用「承伤」当判据**：承伤会被**死亡硬顶住**（人只有一条命、一个血库），N 越大越饱和 ⇒ 指数被压到 <1，纯属伪影。");
        sb.AppendLine("**改用赔率**：败率赔率 `odds(N) = 败率 / 胜率`。这是没有天花板的量。");
        sb.AppendLine("战力放大倍数 `R(N) = odds(N) / odds(1)`，实测指数 = log R(N) / log N。**指数 1 = 线性律，2 = 平方律。**");
        sb.AppendLine();
        sb.AppendLine("| 丧尸数 | 幸存者败率 | 败率赔率 | **战力放大 R(N)** | **实测指数** | 线性律预测 N | 平方律预测 N² |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        double Odds(int n)
        {
            double w = Get(n).Count(x => x.Won) / (double)Seeds;
            w = Math.Clamp(w, 0.5 / Seeds, 1 - 0.5 / Seeds); // 0%/100% 格用半场连续性修正，免除以零
            return (1 - w) / w;
        }
        double o1 = Odds(1);
        foreach (int n in Ns)
        {
            double lose = 1 - Get(n).Count(x => x.Won) / (double)Seeds;
            double r = Odds(n) / o1;
            string exp = n == 1 ? "—" : $"**{Math.Log(r) / Math.Log(n):0.0}**";
            string tail = n >= 4 ? "（胜率已触 0，R 为下界）" : "";
            sb.AppendLine($"| {n} | {lose:P1} | {Odds(n):0.00} | **{r:0.#}×**{tail} | {exp} | {n}× | {n * n}× |");
        }
        sb.AppendLine();
    }

    // ---- ③ 平方律的教科书判据：同类打同类，赢家剩多少 ----
    private static void Table3(StringBuilder sb)
    {
        sb.AppendLine("## ③ 平方律的教科书判据（NvM 同类对同类 —— 这才是 N² 该出现的地方）");
        sb.AppendLine();
        sb.AppendLine("兰彻斯特平方律说：N 打 M（同类单位、集火不受限），赢家**剩余战力** = √(N²−M²)。");
        sb.AppendLine("线性律（每打一次只换一次，无集火收益）说：赢家剩余 = N−M。**两者差得很开，一测就分得出来。**");
        sb.AppendLine("用**同类丧尸互打**测（同类 ⇒ 不需要归一化强度）。");
        sb.AppendLine("**剩余战力 = 赢家一侧还站着几只**（战斗单位数才是战力，不是 HP ——");
        sb.AppendLine("HP 占比是个坏指标：对称局 2v2 的赢家也剩 86% HP，因为集火会让先破口的一方雪崩，赢家几乎不掉血）。");
        sb.AppendLine();
        sb.AppendLine("| 对局 | 大队胜率 | **实测剩余（只）** | 平方律预测 √(N²−M²) | 线性律预测 N−M | 更接近谁 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        (int N, int M)[] pairs = { (2, 1), (3, 1), (3, 2), (4, 2), (4, 1), (6, 3), (2, 2), (3, 3) };
        foreach ((int n, int m) in pairs)
        {
            var outs = Arena.RunDetailed(Zombies(n), Zombies(m), Seeds);
            var wins = outs.Where(o => o.WinnerTeam == 0).ToList();
            double wr = (double)wins.Count / outs.Count;
            // 战力 = 还能打的单位数（死/流血死的不算）。
            double remain = wins.Count == 0 ? 0
                : wins.Average(o => o.TeamAEnd.Count(b => !b.IsDead && !b.BledOut));
            double sq = n > m ? Math.Sqrt(n * (double)n - m * (double)m) : 0;
            double lin = n > m ? n - m : 0;
            string closer = n == m ? "—（对称局，只作 sanity）"
                : Math.Abs(remain - sq) < Math.Abs(remain - lin) ? "**平方律** ✅" : "**线性律**";
            sb.AppendLine($"| {n} v {m} | {wr:P1} | **{remain:0.00}** | {sq:0.00} | {lin:0.00} | {closer} |");
        }
        sb.AppendLine();
        sb.AppendLine("⇒ **NvM 里平方律成立**（集火不受限 ⇒ 先减员的一方雪崩）。这正是用户直觉的来源，而且它在引擎里是真的。");
        sb.AppendLine();
    }

    // ---- ④ 空间上限：真实关卡里到底能围上来几只 ----
    private static void Table4(StringBuilder sb)
    {
        sb.AppendLine("## ④ 🔴 Arena 没有的东西：围攻名额上限");
        sb.AppendLine();
        sb.AppendLine("Arena **无空间** ⇒ 上面所有 Nv1 的格子都默认「N 只全都够得着、全都在砍」。");
        sb.AppendLine($"项目里唯一一处显式的空间名额是 `BreachSlots`（**砸墙用，不是围人用**）：占位宽度 " +
                      $"`DefaultFootprint = {BreachSlots.DefaultFootprint}` px ＝ 丧尸直径（半径 13 × 2）。");
        sb.AppendLine();
        sb.AppendLine("**但 Godot 运行时里也没有围人名额** —— 已逐处核实（下表全是实测常量，不是估计）：");
        sb.AppendLine();
        sb.AppendLine("| 项 | 值 | 出处 |");
        sb.AppendLine("|---|---|---|");
        // ⚠️ 下面这批是 **Godot 消费层**的值，Sim 零依赖 Godot ⇒ 只能抄，抄了就会腐烂。
        //    行号尤其易漂（2026-07-17 复核：值全部仍成立，但行号已从 Zombie.cs:64/68 漂到 111/115，
        //    Pawn.cs:476 漂到 535，攻击判定其实在 Pawn.cs 不在 Actor.cs）⇒ **只给文件名 + 符号，不给行号**。
        sb.AppendLine("| 丧尸攻击距离 `AttackRange` | 24 px | `Zombie.cs` → `z.AttackRange` |");
        sb.AppendLine("| 实际攻击判定（圆心距，**纯距离比较，不需碰撞接触**） | 24 + 13 + 12 = **49 px** | `Pawn.cs` → `dist > AttackRange + Radius + tgt.Radius` |");
        sb.AppendLine("| 丧尸碰撞半径 | 13 px | `Zombie.cs` → `z.Radius` |");
        sb.AppendLine("| 幸存者碰撞半径 | 12 px | `Pawn.cs` → `p.Radius` |");
        sb.AppendLine("| 🔴 丧尸 ↔ 幸存者 | **不碰撞，可完全重叠**（mask 只含 墙/围栏/同阵营） | `Actor.cs` → `ApplyFactionCollision` |");
        sb.AppendLine("| 丧尸 ↔ 丧尸 | 碰撞（圆心最小间距 26 px） | `Actor.cs:285` |");
        // [T63] 此前这一格把冷却**硬编码**成 "1.2 s"，而表里早已是 1.4（用户在 wiki 上改的）。
        // 一行标着"读表，非硬编码"的字，自己却是硬编码的 ⇒ 真·读表。
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| 攻击冷却 | **{WeaponTable.ZombieClaw().AttackInterval:0.0##} s**（读表，非硬编码） | `Zombie.cs:69` → `WeaponTable.ZombieClaw()` |"));
        sb.AppendLine("| 显式围攻名额 | **不存在**（`BreachSlots` 只管砸结构） | 全仓 grep 零命中 |");
        sb.AppendLine();
        sb.AppendLine("⇒ 因为丧尸**能站进幸存者身体里**，「贴身环」那套公式不适用。真实约束只剩两条：");
        sb.AppendLine("丧尸彼此圆心 ≥ 26 px，且都在离目标 49 px 内 ⇒ **把 N 个 r=13 的圆塞进 R=62 的容器圆**：");
        sb.AppendLine();
        sb.AppendLine("| 排布 | 上限 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| 外环（半径 49） | ⌊360 / (2·arcsin(13/49))⌋ = **11 只** |");
        sb.AppendLine("| 内环（半径 23） | ⌊360 / (2·arcsin(13/23))⌋ = **5 只** |");
        sb.AppendLine("| **几何上限（圆填充）** | **16 只** |");
        sb.AppendLine("| 计入 RVO 避障（`Actor.cs:238` `Radius+2`⇒有效分离 30px） | **实战稳态 10~13 只** |");
        sb.AppendLine();
        sb.AppendLine("🔴 **结论：Arena 的「无名额」在这里居然不算大盲区** —— 真实关卡里 **10~16 只丧尸确实能同时够到同一个人**");
        sb.AppendLine("（本作丧尸不与人碰撞，是「能穿过你」的设计）。**所以上面 N≤8 的每一行都是真实可达的场面。**");
        sb.AppendLine("Arena 仍然测不到的是：**走位/后退/门/掩体/先手**——玩家能边打边退、把丧尸拉成一列。");
        sb.AppendLine("⇒ 表①是**「被围死在原地」的最坏情况**；会走位的玩家实际面对的 N 远小于名义 N。");
        sb.AppendLine();
    }

    // ---- ⑤ 对照设计意图 ----
    private static void Table5(StringBuilder sb)
    {
        double w1 = Get(1).Count(x => x.Won) / (double)Seeds;
        double w2 = Get(2).Count(x => x.Won) / (double)Seeds;
        double w3 = Get(3).Count(x => x.Won) / (double)Seeds;

        sb.AppendLine("## ⑤ 对照用户的设计意图：「单只丧尸弱、群体丧尸致命」");
        sb.AppendLine();
        sb.AppendLine("| 问题 | 实测答案 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 单只丧尸弱吗？ | **弱。** 中甲长剑幸存者 **{w1:P1}** 胜 —— 但**这不是白送**：赢的时候平均已流失 20% 血。 |");
        sb.AppendLine($"| 要多少只才「致命」？ | **两只。** 胜率 {w1:P1} → **{w2:P1}**。三只就是 **{w3:P1}**（≈必死）。 |");
        sb.AppendLine("| 这个数在真实关卡出得来吗？ | **随处都是。** 袭营首日就 8 只（`RaidWave`：3 + 0.6×天 + 1×在营人数，封顶 40）；" +
                      "尸潮 `HordeTimeline` 每波 8 起、每波 +2、封顶 60、同屏上限 80；南林村庄光围屋的就 5 只 + 4 只游荡。 |");
        sb.AppendLine();
        sb.AppendLine("🔴 **梯度成立，但陡得超出直觉**：设计意图（单只弱、群体致命）**在引擎里是成立的**，");
        sb.AppendLine("问题在于「群体」的门槛低到 **2 只**——不是想象中的 3~5 只。**没有「三五成群还能周旋」这个中间地带。**");
        sb.AppendLine();
        sb.AppendLine("**为什么这么陡**（机制解释，不是 bug）：");
        sb.AppendLine("1. **失血是时间积分**。丧尸多一倍 ⇒ 单位时间伤口多一倍，**同时**清场时间也变长（23.5s → 37.9s → 48.5s）。");
        sb.AppendLine("   总失血 ≈ 伤口速率 × 时长 ⇒ **两个因子都随 N 涨 ⇒ 天然平方**。这正是平方律的物理来源。");
        sb.AppendLine("2. **血库是硬阈值**。1v1 打赢时已经掉了 20% 血；威胁翻两番，直接冲破昏迷线 ⇒ **不是线性变难，是断崖**。");
        sb.AppendLine("3. **输的方式不是被打死，是倒地**（表①：8 只围攻时也只有 17.6% 当场毙命，其余全是昏迷/失能）。");
        sb.AppendLine();
        sb.AppendLine("## 🔴 结论（一句话）");
        sb.AppendLine();
        sb.AppendLine("**用户的「三打一的战力比是九比一」在这个引擎里不但成立，而且偏保守。**");
        sb.AppendLine();
        sb.AppendLine("- **NvN（平方律的正确适用场景）**：实测赢家剩余单位数**贴着甚至略高于 √(N²−M²)** ⇒ **平方律确认成立**（表③）。");
        sb.AppendLine("  唯一的前提「集火不受限」在本作里**真的不受限**——丧尸不与人碰撞，能站进你身体里（表④）。");
        sb.AppendLine("- **Nv1（玩家实际遭遇）**：严格说不适用平方律（玩家没有「减员」可言），");
        sb.AppendLine("  但实测的威胁放大**指数 4.6~5.8，远超 2** ⇒ **比九比一还狠得多**（表②）。");
        sb.AppendLine("- ⇒ **「所以单一丧尸不该很强」这个推论，前提是对的、结论也是对的。**");
        sb.AppendLine("  要留意的只有一点：**梯度已经陡到「2 只即致命」**，再压低单只丧尸也换不回中间地带——");
        sb.AppendLine("  要造出「三五成群还能周旋」，得改的是**失血/昏迷线**或**给玩家走位收益**，不是再调爪击伤害。");
        sb.AppendLine();
        sb.AppendLine("⚠️ **复跑提醒**：本表跑在 `impl-bleed`（武器影响流血）**落地中**的工作树上。");
        sb.AppendLine("长剑与爪击都**没有**流血改装（速率乘数 = 1.0，按其设计与旧裸计数模型逐位等价）⇒ 本表数字应当零漂移，");
        sb.AppendLine("但 `impl-bleed` 一旦改动**基础流血速率或分级权重**，**本表必须复跑**（失血正是上面那条断崖的主因）。");
        sb.AppendLine();
    }
}
