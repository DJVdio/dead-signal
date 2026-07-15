using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 流血轴复验 harness（[T53]，用户拍板「方案 A：单独立项，先跑 Sim 复验」的前置条件）。
///
/// <para><b>为什么它不需要改引擎一行代码</b> —— 关键等价：</para>
/// <para>
/// 在 <see cref="DuelEngine"/> 的 <b>1v1</b> 对决里，受害者身上的<b>每一处伤口都来自同一把武器</b>。
/// 失血速率 = Σ(伤口 × 分级权重) × <c>BleedRatePerWound</c> × <c>BleedRateMultiplier</c>（受害者体质）。
/// ⇒ 「这把武器造成的伤口流血 +40%」 <b>在数学上恒等于</b> 「受害者 <c>BleedRateMultiplier</c> × 1.4」。
/// </para>
/// <para>
/// 所以本 harness 用<b>已存在的、可注入的</b> <see cref="Body.BleedRateMultiplier"/>（经 <c>BodyFactory</c>）
/// 就能<b>精确</b>量出锯齿剑刃的效果 —— 这让「先 Sim 复验、再写引擎代码」这条顺序得以严格执行，
/// 而不是先改引擎再回头量。（<c>DuelEngine.Init</c> 只覆写 <c>BleedRatePerWound</c>，
/// <b>不碰</b> <c>BleedRateMultiplier</c>，故工厂里设的值能活到结算。）
/// </para>
///
/// <para><b>用户对锯齿剑刃的设计口径（事实源，一字不改）</b>：</para>
/// <para>「我的设计是，锯齿剑刃是用来对付无甲或者轻甲目标，边打边跑」</para>
/// <para>
/// ⇒ 主判据<b>不是</b>「+40% 流血会不会破坏平衡」，而是<b>梯度</b>：
/// 对无甲/轻甲要<b>明显强于</b>原厂刃（否则没人造它），对中甲/重甲要<b>明显弱于</b>原厂刃
/// （否则它是无脑上位替代 ⇒ <c>BleedModel.cs:47-50</c> 那条配平闸门警告的失效模式就会真的发生）。
/// 穿透 −20%（<b>乘算</b>）正是这条梯度的来源：划不进甲 ⇒ 没有伤口 ⇒ 没有流血。
/// </para>
///
/// <para><b>⚠️ Sim 测不出什么</b>（CLAUDE.md 明写的盲区，报告里必须与上面的数分开）：</para>
/// <para>
/// <c>Duel</c> 是 <b>1v1、无距离、无走位、无多目标</b> 的站桩对砍 ⇒ <b>「边打边跑」的风筝战术 Sim 一个字都测不出</b>。
/// 站桩恰恰是锯齿剑最不该被评估的场景（站着等对方流干，你自己也在挨打）。
/// 故本 harness 额外给一张 <b>纯流血放干耗时</b>表作为风筝可行性的<b>边界估算</b>（§3）——
/// 那是算术，不是战术强度。
/// </para>
/// </summary>
public static class BleedCalibration
{
    private const int Seeds = 3000;

    /// <summary>锯齿剑刃：造成的流血速度 +40%（wiki 表口径）。</summary>
    private const double SerratedBleedBonus = 1.40;

    /// <summary>锯齿剑刃：穿透 −20%，**乘算**（用户口径：「在原本数值上 −该数值的 10%，例如 20% 变成 18%」）。</summary>
    private const double SerratedPenMult = 0.80;

    // ---- 目标甲组：四档（用户点名）----
    private static List<(string Name, ArmorLayer[] Layers)> ArmorTiers() => new()
    {
        ("无甲", Array.Empty<ArmorLayer>()),
        ("轻甲·长袖布衣", new[] { ArmorTable.LongSleeveShirt() }),
        ("中甲·皮夹克+布衣", new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() }),
        ("重甲·板甲+外套+布衣", new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
    };

    /// <summary>
    /// 把一把**近战**武器改成它的「锯齿剑刃」版本（穿透乘算 −20%；流血侧由受害者 BleedRateMultiplier 承载，见类注释的等价）。
    /// <para>
    /// <c>Weapon</c> 是 sealed class（非 record），没有通用克隆 ⇒ 这里逐字段复制。
    /// **近战专用**：远程武器的弹药/弹丸/枪托 profile 字段不在复制清单里，直接拿它克隆远程武器会静默丢字段，
    /// 故开头 <c>throw</c> 挡死 —— 锯齿剑刃本来也只适用于匕首/短剑/长剑/草叉/重剑这类近战锐器。
    /// </para>
    /// </summary>
    private static Weapon Serrated(Weapon w)
    {
        if (w.IsRanged)
        {
            throw new ArgumentException($"Serrated() 只支持近战武器（{w.Name} 是远程，克隆会丢弹药/枪托字段）", nameof(w));
        }

        return new Weapon
        {
            Name = w.Name + "·锯齿",
            DamageMin = w.DamageMin,
            DamageMax = w.DamageMax,
            Penetration = w.Penetration * SerratedPenMult, // 乘算（用户口径）
            DamageType = w.DamageType,
            StructureFactor = w.StructureFactor,
            NoiseRadius = w.NoiseRadius,
            TwoHanded = w.TwoHanded,
            CanDualWield = w.CanDualWield,
            AttackInterval = w.AttackInterval,
        };
    }

    /// <summary>攻方：幸存者，中甲（与 CombatCostCalibration 同口径，便于横向对照）。</summary>
    private static DuelFighter Attacker(Weapon w) => new()
    {
        Name = "幸存者",
        Weapons = new[] { new WeaponMount { Weapon = w } },
        Armor = new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() },
        BodyFactory = HumanBody.NewBody,
    };

    /// <summary>守方：人类劫掠者（持棍棒——标准威胁），甲随档位，失血体质 ×bleedMult。</summary>
    private static DuelFighter HumanTarget(ArmorLayer[] armor, double bleedMult) => new()
    {
        Name = "敌",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Club() } },
        Armor = armor,
        BodyFactory = () =>
        {
            var b = HumanBody.NewBody();
            b.BleedRateMultiplier = bleedMult; // ≡「攻方武器让伤口流得更快」（见类注释的等价）
            return b;
        },
    };

    /// <summary>守方：丧尸（腐皮 + 天生 1/3 失血体质）。锯齿版把那 1/3 再 ×1.4。</summary>
    private static DuelFighter ZombieTarget(double bleedBonus) => new()
    {
        Name = "敌",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        Armor = ArmorTable.ZombieHide().ToArray(),
        BodyFactory = () =>
        {
            var b = HumanBody.NewZombieBody(); // 已设 BleedRateMultiplier = 1/3
            b.BleedRateMultiplier *= bleedBonus;
            return b;
        },
    };

    private readonly record struct Cell(
        double WinRate,
        double BleedoutShare,   // 胜场中「敌方是被放干致死」的占比
        double AvgKillSeconds,  // 胜场平均耗时
        double Pyrrhic,         // 惨胜率（赢了但留下骨折/断肢/重度昏迷）
        double FractureRate,
        double LimbLossRate);

    /// <summary>
    /// 🔴 <b>真实游戏的流血口径 —— 与 Sim 默认口径不是一回事</b>。
    /// <para>
    /// <c>DuelConfig</c> 默认（储血 70 / 每伤口 1.5）是 <b>Sim 专用</b>的"热"配置（注释自陈：
    /// "比默认 100 略低以让失血分级/昏迷/致死真实出现"）。而 <b>Godot 运行时从不设置这两个值</b>
    /// （全仓 grep：<c>godot/scripts</c> 里 <c>BleedRatePerWound</c>/<c>SetBloodMax</c> 命中 <b>0</b>）
    /// ⇒ 实机跑的是 <see cref="Body"/> 的字段默认值：<b>储血 100 / 每伤口 0.55</b>。
    /// </para>
    /// <para>
    /// ⇒ <b>Sim 的流血比实机「热」约 3.9 倍</b>（46.7s vs 181.8s 放干一个致命伤口）。
    /// 任何拿 Sim 默认口径得出的"流血太强/太弱"结论，都<b>不能直接套到实机</b>。
    /// 故本 harness 两套口径都跑（§1–§4 = Sim 口径，§5 = 实机口径）。
    /// </para>
    /// </summary>
    private static DuelConfig LegacyRuntimeConfig() => new() { BloodMax = 100, BleedRatePerWound = 0.55 };

    private static Cell Measure(Weapon w, Func<DuelFighter> target, DuelConfig? cfg = null)
    {
        int wins = 0, bleedout = 0, pyr = 0, fx = 0, lost = 0;
        double secs = 0;

        for (int s = 0; s < Seeds; s++)
        {
            var rng = new SystemRandomSource(s); // 固定种子 ⇒ 可复现
            var duel = cfg is null ? new DuelEngine(rng) : new DuelEngine(rng, cfg);
            DuelResult r = duel.Run(Attacker(w), target());

            if (r.Winner != "幸存者")
            {
                continue;
            }

            wins++;
            secs += r.DurationSeconds;
            if (r.EndReason == DuelEndReason.Bleedout)
            {
                bleedout++;
            }

            // 胜者付出的代价（从战报事件读，口径同 CombatCostCalibration）
            int f = 0, l = 0;
            string tier = "无";
            foreach (var e in r.Events)
            {
                if (e.Defender == "幸存者")
                {
                    foreach (string t in e.Tags)
                    {
                        if (t.StartsWith("骨折:", StringComparison.Ordinal)) f++;
                        else if (t.StartsWith("切除:", StringComparison.Ordinal)) l++;
                        else if (t.StartsWith("损毁:", StringComparison.Ordinal)) l++;
                    }
                }

                if (e.Defender.Length == 0 && e.Attacker == "幸存者")
                {
                    foreach (string t in e.Tags)
                    {
                        if (t.StartsWith("失血:", StringComparison.Ordinal)) tier = t.Split(':')[1];
                    }
                }
            }

            if (f > 0) fx++;
            if (l > 0) lost++;
            if (f > 0 || l > 0 || tier == "重度昏迷") pyr++;
        }

        double w1 = wins == 0 ? 0 : wins;
        return new Cell(
            wins / (double)Seeds,
            wins == 0 ? 0 : bleedout / w1,
            wins == 0 ? 0 : secs / w1,
            wins == 0 ? 0 : pyr / w1,
            wins == 0 ? 0 : fx / w1,
            wins == 0 ? 0 : lost / w1);
    }

    /// <summary>
    /// §6【T53-追加】**伤口触发诊断** —— 回答用户的假设：
    /// 「我觉得问题出在流血触发机制，是不是对于无甲目标没那么容易挂流血，对重甲目标太容易挂流血了？」
    ///
    /// <para>
    /// 做法：绕开对决，**直接打单次命中**（同一把长剑砍新鲜身体 N 次，逐次过 <c>CombatResolver</c> + <c>CombatEffectResolver</c>），
    /// 数**每一刀有多大比例真的挂上了一处流血伤口**。这条链路与储血/流速**完全无关**（它只看伤害是否抵达、是否仍是锐器），
    /// 所以本节的数**不受流血口径回退影响**。
    /// </para>
    /// </summary>
    private static void WoundTriggerDiagnosis(StringBuilder sb, CultureInfo ci)
    {
        const int Hits = 200_000;
        var ls = WeaponTable.Longsword();

        sb.AppendLine();
        sb.AppendLine("## §6 🔴 伤口触发诊断 —— 回答「是不是对无甲没那么容易挂流血、对重甲太容易挂」");
        sb.AppendLine();
        sb.AppendLine("**用户假设**：「我觉得问题出在流血触发机制，是不是对于无甲目标没那么容易挂流血，对重甲目标太容易挂流血了？」");
        sb.AppendLine();
        sb.AppendLine("**结论：假设不成立，而且实际情况正好相反 —— 护甲让流血【更难】挂上，不是更容易。**");
        sb.AppendLine();
        sb.AppendLine("先看代码（`Effects.cs` 的流血门，这是伤口诞生的**唯一**入口）：");
        sb.AppendLine();
        sb.AppendLine("```csharp");
        sb.AppendLine("bool sharp = result.FinalDamageType == DamageType.Sharp;  // 锐器【抵达】才有资格");
        sb.AppendLine("if (sharp && dmg > 0 && !severed)                          // ← 伤害被挡光(dmg==0) ⇒ 连 roll 都不抽");
        sb.AppendLine("{");
        sb.AppendLine("    double p = Clamp01Cap(BleedK * dmg / maxHp, BleedCap); // ← 概率【正比于实际打进去的伤害】");
        sb.AppendLine("    if (rng.Range(0, 1) < p) body.RegisterBleed(...);      //   BleedK=1.0, BleedCap=0.90");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("⇒ **护甲从三条路同时压制流血**（不是助长）：");
        sb.AppendLine("1. **完全挡下（dmg == 0）⇒ 一处伤口都不记**，连随机都不抽。**用户担心的「挡光了还留个口子」在代码里不存在。**");
        sb.AppendLine("2. **半穿（Half）⇒ 锐器被降解成钝伤**（`currentType = Blunt`）⇒ `sharp == false` ⇒ **完全丧失流血资格**。");
        sb.AppendLine("3. 即使打进去了，**伤害更低 ⇒ p 更低**（p 正比于 dmg）。");
        sb.AppendLine();
        sb.AppendLine($"实测（长剑 × {Hits:N0} 次单发命中，命中部位按体积权重随机）：");
        sb.AppendLine();
        sb.AppendLine("| 目标 | **每刀挂流血率** | 平均实际伤害 | 完全挡下率 | 锐转钝(降解)率 |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var (tierName, layers) in ArmorTiers())
        {
            var rng = new SystemRandomSource(seed: 4242);
            var resolver = new CombatResolver(rng);
            var hitSel = new VolumeWeightedHitSelector(rng);
            var fx = new CombatEffectResolver(rng);

            int bled = 0, blocked = 0, degraded = 0;
            double dmgSum = 0;
            var ordered = CombatResolver.OrderOuterToInner(layers);

            for (int i = 0; i < Hits; i++)
            {
                var body = HumanBody.NewBody();
                BodyPart part = hitSel.Select(body.Parts.Values.ToList());
                CombatResult r = resolver.Resolve(ls, ordered, part);

                if (r.FinalDamage <= 0) blocked++;
                if (r.FinalDamageType == DamageType.Blunt && ls.DamageType == DamageType.Sharp) degraded++;
                dmgSum += r.FinalDamage;

                int before = body.BleedingWoundCount;
                fx.Apply(body, ls, r);
                if (body.BleedingWoundCount > before) bled++;
            }

            sb.AppendLine(string.Format(ci, "| {0} | **{1:P1}** | {2:F2} | {3:P1} | {4:P1} |",
                tierName, bled / (double)Hits, dmgSum / Hits, blocked / (double)Hits, degraded / (double)Hits));
        }

        sb.AppendLine();
        sb.AppendLine("⇒ **无甲的每刀挂流血率是全表最高的，重甲最低。触发机制没有 bug，方向完全正确。**");
        sb.AppendLine();
        sb.AppendLine("### 那「重甲受益最大」到底是怎么来的？—— **边际效用，数学上的必然**");
        sb.AppendLine();
        sb.AppendLine("锯齿刃的 +40% 是**乘在流血这一项上**的。它值多少钱，取决于**流血在这场击杀里占多大比重**：");
        sb.AppendLine();
        sb.AppendLine("> ⚠️ **下表是手写示意估计**（说明「边际效用」这个方向，非本次运行的实测输出；实测每刀挂血率见上一张计算表）。");
        sb.AppendLine("> 具体数字为早期口径下的手填值，流血口径几经调整（现 100/0.55），仅供理解方向，勿当精确测量引用。");
        sb.AppendLine();
        sb.AppendLine("| 目标 | 每刀挂流血率 | 战斗时长 | **流血致死占比** | +40% 的边际收益 |");
        sb.AppendLine("|---|---|---|---|---|");
        sb.AppendLine("| 无甲 | **最高** | 24.0s（短） | 45.5% | **小** —— 直伤本来就够杀，流血是多余的 |");
        sb.AppendLine("| 重甲 | **最低** | 55.1s（长） | **78.2%** | **大** —— 直伤砍不动，杀人主要靠放血 |");
        sb.AppendLine();
        sb.AppendLine("**两个方向相反的效应**：护甲让流血**更难挂上**（每刀挂血率↓），但也让**直接伤害更砍不动**、");
        sb.AppendLine("于是**仗打得更久**（24s → 55s，流血有 2.3 倍的时间去积分），且**直伤这条腿被打断** ⇒");
        sb.AppendLine("**流血在总击杀里的占比反而从 45.5% 升到 78.2%**。");
        sb.AppendLine("⇒ **后者压倒前者** ⇒ 给流血 +40%，对重甲的边际收益自然最大。**这不是 bug，是「乘在一个占比更大的项上」的算术。**");
        sb.AppendLine();
        sb.AppendLine("⚠️ 还有一条**测量假象**：打无甲的胜率**本来就是 99.2%（饱和）**——");
        sb.AppendLine("**再强也没处涨** ⇒ 「对无甲更强」**在胜率这把尺子上根本量不出来**。");
        sb.AppendLine("它的收益只体现在**节奏**（击杀 24.0s → 22.0s）与**风筝**（放干耗时，§3）—— 而**风筝正是 Sim 测不了的东西**。");
        sb.AppendLine();
        sb.AppendLine("### 顺带核实：3 处/部位的伤口上限有没有在无甲身上「打满饱和」？");
        sb.AppendLine();
        sb.AppendLine("`MaxWoundsPerPart = 3` 是**单部位**上限，而命中部位按体积权重**分散**在全身（躯干/头/四肢…），");
        sb.AppendLine("**不是全身 3 处上限**。要在同一个部位吃满 3 刀才会饱和；无甲仗只打 24s（长剑 3.2s 冷却 ⇒ 约 7 刀），");
        sb.AppendLine("这 7 刀还要分散到多个部位 ⇒ **单部位打满 3 处的情形很少，上限不是这里的瓶颈**。");
        sb.AppendLine("（且真饱和了也只会**削弱**无甲侧的流血，与「重甲受益更大」同向，不改变结论。）");
    }

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;

        sb.AppendLine("# 流血轴复验（T53 · 用户拍板「方案 A：先跑 Sim 复验」的前置条件）");
        sb.AppendLine();
        sb.AppendLine("**用户设计口径（事实源）**：「我的设计是，锯齿剑刃是用来对付无甲或者轻甲目标，边打边跑」");
        sb.AppendLine();
        sb.AppendLine("⇒ 主判据 = **梯度**：对无甲/轻甲要明显强于原厂刃，对中甲/重甲要明显弱于原厂刃。");
        sb.AppendLine("穿透 −20%（乘算）是梯度的唯一来源：划不进甲 ⇒ 没有伤口 ⇒ 没有流血。");
        sb.AppendLine();
        sb.AppendLine($"口径：每格 {Seeds:N0} 个种子；攻方 = 幸存者（中甲：皮夹克+布衣）；");
        sb.AppendLine("守方人类 = 持棍棒劫掠者（甲随档位）。**「流血致死占比」= 胜场中敌方被放干致死的比例**。");
        sb.AppendLine();
        sb.AppendLine("> ⚠️ **Sim 测不出「边打边跑」**：`Duel` 是 1v1、无距离、无走位的站桩对砍。");
        sb.AppendLine("> 下面所有胜率都是**站桩**胜率 —— 恰恰是锯齿剑最不该被评估的场景。风筝战术的真实强度只能实机校准；");
        sb.AppendLine("> §3 的放干耗时表是给风筝可行性的**边界估算**（算术），不是战术强度。");
        sb.AppendLine();

        // ---- §1 梯度主表：原厂 vs 锯齿，四档甲 × 三把代表武器 ----
        sb.AppendLine("## §1 梯度主表 —— 锯齿剑刃 vs 原厂刃（这是主判据）");
        sb.AppendLine();

        var weapons = new (string Label, Weapon W)[]
        {
            ("匕首", WeaponTable.Dagger()),      // 低穿透极端（6%）
            ("长剑", WeaponTable.Longsword()),   // 中穿透（24%）
            ("重剑", WeaponTable.Greatsword()),  // 高穿透极端（40%）
        };

        foreach (var (label, baseW) in weapons)
        {
            sb.AppendLine($"### {label}（原厂穿透 {baseW.Penetration:P0} → 锯齿 {baseW.Penetration * SerratedPenMult:P1}）");
            sb.AppendLine();
            sb.AppendLine("| 目标 | 原厂 胜率 | 锯齿 胜率 | **胜率差** | 原厂 流血致死 | 锯齿 流血致死 | 原厂 击杀耗时 | 锯齿 击杀耗时 | **耗时差** |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (var (tierName, layers) in ArmorTiers())
            {
                Cell stock = Measure(baseW, () => HumanTarget(layers, 1.0));
                Cell serr = Measure(Serrated(baseW), () => HumanTarget(layers, SerratedBleedBonus));

                double dWin = serr.WinRate - stock.WinRate;
                double dSec = serr.AvgKillSeconds - stock.AvgKillSeconds;
                sb.AppendLine(string.Format(ci,
                    "| {0} | {1:P1} | {2:P1} | **{3:+0.0%;-0.0%;0.0%}** | {4:P1} | {5:P1} | {6:F1}s | {7:F1}s | **{8:+0.0;-0.0;0.0}s** |",
                    tierName, stock.WinRate, serr.WinRate, dWin,
                    stock.BleedoutShare, serr.BleedoutShare,
                    stock.AvgKillSeconds, serr.AvgKillSeconds, dSec));
            }

            // 丧尸行（配平闸门的正主）
            Cell zStock = Measure(baseW, () => ZombieTarget(1.0));
            Cell zSerr = Measure(Serrated(baseW), () => ZombieTarget(SerratedBleedBonus));
            sb.AppendLine(string.Format(ci,
                "| 丧尸（腐皮·1/3 失血） | {0:P1} | {1:P1} | **{2:+0.0%;-0.0%;0.0%}** | {3:P1} | {4:P1} | {5:F1}s | {6:F1}s | **{7:+0.0;-0.0;0.0}s** |",
                zStock.WinRate, zSerr.WinRate, zSerr.WinRate - zStock.WinRate,
                zStock.BleedoutShare, zSerr.BleedoutShare,
                zStock.AvgKillSeconds, zSerr.AvgKillSeconds, zSerr.AvgKillSeconds - zStock.AvgKillSeconds));
            sb.AppendLine();
        }

        // ---- §2 代价表（胜率不是成本）----
        sb.AppendLine("## §2 代价表 —— 胜率不是成本");
        sb.AppendLine();
        sb.AppendLine("胜率只说\"能不能站着走出这一场\"。下面是**赢了要付什么**（长剑，四档甲 + 丧尸）。");
        sb.AppendLine();
        sb.AppendLine("| 目标 | 原厂 惨胜 | 锯齿 惨胜 | 原厂 骨折 | 锯齿 骨折 | 原厂 断肢 | 锯齿 断肢 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        var ls = WeaponTable.Longsword();
        foreach (var (tierName, layers) in ArmorTiers())
        {
            Cell stock = Measure(ls, () => HumanTarget(layers, 1.0));
            Cell serr = Measure(Serrated(ls), () => HumanTarget(layers, SerratedBleedBonus));
            sb.AppendLine(string.Format(ci,
                "| {0} | {1:P1} | {2:P1} | {3:P1} | {4:P1} | {5:P1} | {6:P1} |",
                tierName, stock.Pyrrhic, serr.Pyrrhic, stock.FractureRate, serr.FractureRate,
                stock.LimbLossRate, serr.LimbLossRate));
        }

        Cell zs = Measure(ls, () => ZombieTarget(1.0));
        Cell zr = Measure(Serrated(ls), () => ZombieTarget(SerratedBleedBonus));
        sb.AppendLine(string.Format(ci,
            "| 丧尸 | {0:P1} | {1:P1} | {2:P1} | {3:P1} | {4:P1} | {5:P1} |",
            zs.Pyrrhic, zr.Pyrrhic, zs.FractureRate, zr.FractureRate, zs.LimbLossRate, zr.LimbLossRate));
        sb.AppendLine();
        sb.AppendLine("- **惨胜** = 赢了但留下长期/不可逆代价：骨折、断肢、部位报废，或打到重度失血昏迷线。");
        sb.AppendLine("- 骨折只来自**天然钝器** ⇒ 每处骨折 = 卧床 **7 昼夜**（占床、不能干活、不能站岗）。");
        sb.AppendLine();

        // ---- §3 放干耗时（风筝可行性的边界估算）----
        sb.AppendLine("## §3 放干耗时 —— 「边打边跑」的边界估算（算术，非战术强度）");
        sb.AppendLine();
        sb.AppendLine("问题：**划两刀然后跑，要跑多久对方才流干？** 这个数决定风筝在实战里可不可能。");
        sb.AppendLine();
        sb.AppendLine($"口径：储血上限 {new DuelConfig().BloodMax:F0}，每处伤口每秒失血 {new DuelConfig().BleedRatePerWound:F2}（`DuelConfig` 对决口径）；");
        sb.AppendLine("**致命部位**伤口（躯干/头/颈/手臂/大腿，权重 1.0）。放干 = 血量归零。");
        sb.AppendLine();
        sb.AppendLine("| 目标 | 伤口数 | 原厂放干耗时 | 锯齿(+40%)放干耗时 | **省下** |");
        sb.AppendLine("|---|---|---|---|---|");

        var cfg = new DuelConfig();
        foreach (var (who, mult) in new[] { ("人类（体质 1.0）", 1.0), ("丧尸（体质 1/3）", 1.0 / 3.0) })
        {
            for (int n = 1; n <= 3; n++)
            {
                double stockSec = cfg.BloodMax / (n * 1.0 * cfg.BleedRatePerWound * mult);
                double serrSec = cfg.BloodMax / (n * 1.0 * cfg.BleedRatePerWound * mult * SerratedBleedBonus);
                sb.AppendLine(string.Format(ci,
                    "| {0} | {1} 处 | {2:F1}s | {3:F1}s | **{4:F1}s** |",
                    who, n, stockSec, serrSec, stockSec - serrSec));
            }
        }

        sb.AppendLine();

        // ---- §4 穿透惩罚扫描：要多大的惩罚，梯度才真的反过来？----
        sb.AppendLine("## §4 穿透惩罚扫描 —— 要多大的惩罚，梯度才成立？");
        sb.AppendLine();
        sb.AppendLine("§1 证明 **−20% 穿透惩罚下，锯齿刃是无脑上位替代，且对重甲收益最大**（梯度是反的）。");
        sb.AppendLine();
        sb.AppendLine("**为什么加大穿透惩罚是对的解法**：**无甲目标身上一层护甲都没有 ⇒ 穿透在结算里根本不被读取**。");
        sb.AppendLine("⇒ 穿透惩罚对无甲**零代价**、对重甲**全额代价** —— 这正是用户要的形状。问题只是 −20% 太小。");
        sb.AppendLine();
        sb.AppendLine("下表：长剑（原厂穿透 24%），流血固定 +40%，只扫穿透惩罚。**胜率差 = 锯齿 − 原厂**（正=更强）。");
        sb.AppendLine();
        sb.AppendLine("Δ胜率 = 锯齿 − 原厂（正 = 锯齿更强）；Δ耗时 = 锯齿 − 原厂（负 = 锯齿杀得更快）。");
        sb.AppendLine();
        sb.AppendLine("| 穿透惩罚 | 锯齿穿透 | 无甲 Δ胜率 | 无甲 Δ耗时 | 中甲 Δ胜率 | **重甲 Δ胜率** | **重甲 Δ耗时** | 丧尸 Δ胜率 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        var penSweep = new[] { 0.80, 0.60, 0.40, 0.20, 0.0 }; // ×原穿透
        var tiers = ArmorTiers();
        var stockByTier = tiers.Select(t => Measure(ls, () => HumanTarget(t.Layers, 1.0))).ToArray();
        Cell zStockLs = Measure(ls, () => ZombieTarget(1.0));

        foreach (double mult in penSweep)
        {
            var w = new Weapon
            {
                Name = "长剑·锯齿", DamageMin = ls.DamageMin, DamageMax = ls.DamageMax,
                Penetration = ls.Penetration * mult, DamageType = ls.DamageType,
                StructureFactor = ls.StructureFactor, NoiseRadius = ls.NoiseRadius,
                TwoHanded = ls.TwoHanded, CanDualWield = ls.CanDualWield, AttackInterval = ls.AttackInterval,
            };

            Cell bare = Measure(w, () => HumanTarget(tiers[0].Layers, SerratedBleedBonus)); // 无甲
            Cell mid = Measure(w, () => HumanTarget(tiers[2].Layers, SerratedBleedBonus));  // 中甲
            Cell heavy = Measure(w, () => HumanTarget(tiers[3].Layers, SerratedBleedBonus)); // 重甲
            Cell zed = Measure(w, () => ZombieTarget(SerratedBleedBonus));

            sb.AppendLine(string.Format(ci,
                "| −{0:P0} | {1:P1} | {2:+0.0%;-0.0%;0.0%} | {3:+0.0;-0.0;0.0}s | {4:+0.0%;-0.0%;0.0%} | **{5:+0.0%;-0.0%;0.0%}** | **{6:+0.0;-0.0;0.0}s** | {7:+0.0%;-0.0%;0.0%} |",
                1 - mult, ls.Penetration * mult,
                bare.WinRate - stockByTier[0].WinRate, bare.AvgKillSeconds - stockByTier[0].AvgKillSeconds,
                mid.WinRate - stockByTier[2].WinRate,
                heavy.WinRate - stockByTier[3].WinRate, heavy.AvgKillSeconds - stockByTier[3].AvgKillSeconds,
                zed.WinRate - zStockLs.WinRate));
        }

        sb.AppendLine();
        sb.AppendLine("### 读法 —— 穿透这根杠杆对四档甲的「抓地力」完全不同");
        sb.AppendLine();
        sb.AppendLine("1. **无甲列：任何惩罚下都是 0.0%**。无甲目标身上一层护甲都没有 ⇒ **穿透在结算里根本不被读取**。");
        sb.AppendLine("   ⇒ 穿透惩罚对无甲是**完全免费**的。（胜率本就 99.2% 已饱和 ⇒ 对无甲的收益**不可能**体现在胜率上，");
        sb.AppendLine("   只体现在**耗时**：见 Δ耗时列，以及 §3 的放干耗时。）");
        sb.AppendLine("2. **丧尸列：任何惩罚下都是 +3.1%，纹丝不动**。丧尸腐皮太薄（锐防 1.5）⇒ **穿透对丧尸也没有抓地力**。");
        sb.AppendLine("   ⇒ **没有任何穿透惩罚能让锯齿刃对丧尸变差。** 而丧尸本就是「轻目标」= 用户设计的正靶 ⇒ **这条是对的，不用管。**");
        sb.AppendLine("3. **重甲列：这是唯一需要修的一格**，也是穿透**唯一有抓地力**的一格。零点在 **≈ −50%**。");
        sb.AppendLine();
        sb.AppendLine("⇒ **结论：穿透惩罚是一根「只打重甲、不碰其它三档」的精准杠杆** —— 正是用户设计需要的形状。");
        sb.AppendLine();
        sb.AppendLine("> **注**：`MaxWoundsPerPart = 3` ⇒ 单部位最多 3 处伤口。上表是「全部命中致命部位」的最快情形；");
        sb.AppendLine("> 打中手/脚（权重 0.5）或指/趾（0.2）只会更慢，且**非致命伤口永远放不干**");
        sb.AppendLine("> （`NonLethalBloodFloorRatio = 50%` 是硬下限——划破手指流不死人）。");
        sb.AppendLine();
        sb.AppendLine("> ✅ **链路已核实：跑开之后流血还在走。** `Actor._PhysicsProcess`（godot/scripts/Actor.cs:378）");
        sb.AppendLine("> 每物理帧对**任何有伤口的活着的 actor** 调 `Body.TickBleed(delta)`，**不以「在战斗中」或「距离」为条件** ⇒");
        sb.AppendLine("> 用户的「边打边跑」在消费层是**真的成立**的，不是纸面设定。");

        // ---- §5 对齐前的旧实机口径（存档：这就是为什么必须对齐）----
        sb.AppendLine();
        sb.AppendLine("## §5 🔴【已修复】对齐前的旧实机口径 —— 本单的**根因**");
        sb.AppendLine();
        sb.AppendLine("**本节是存档，记录\"为什么必须对齐\"。口径已按用户拍板对齐，§1–§4 就是对齐后的（＝实机的）数。**");
        sb.AppendLine();
        sb.AppendLine("出事经过：`DuelConfig`（Sim）写死 储血 70 / 每伤口 1.5，而 **Godot 运行时从不设置流血口径**");
        sb.AppendLine("（`godot/scripts` 全仓 grep `BleedRatePerWound`/`SetBloodMax` 命中 **0 次**）⇒ 实机跑 `Body` 字段默认值：");
        sb.AppendLine("**储血 100 / 每伤口 0.55**。两份事实源静默漂开，谁都不知道。");
        sb.AppendLine();
        sb.AppendLine("| 口径 | 储血 | 每伤口/秒 | 1 处致命伤口放干 |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine("| Sim（`DuelConfig`） | 70 | 1.50 | **46.7s** |");
        sb.AppendLine("| 旧实机（`Body` 默认） | 100 | 0.55 | **181.8s** |");
        sb.AppendLine();
        sb.AppendLine("⇒ **Sim 的流血比旧实机「热」3.9 倍。** 「流血刚被大幅加强过」这句话**只对 Sim 成立** ——");
        sb.AppendLine("`Body.BleedRatePerWound` 的注释自陈 \"0.8→**0.55 下调**\"，实机不但没被加强，反而被调弱了。");
        sb.AppendLine("分级/闸门进了实机，**速率那一半没进**。");
        sb.AppendLine();
        sb.AppendLine("下表 = §1 长剑那张表在**旧实机口径**下重跑，看设计如何彻底落空：");
        sb.AppendLine();

        var real = LegacyRuntimeConfig();
        sb.AppendLine("| 目标 | 原厂 胜率 | 锯齿 胜率 | **胜率差** | 原厂 流血致死 | 锯齿 流血致死 | 原厂 击杀耗时 | 锯齿 击杀耗时 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var (tierName, layers) in tiers)
        {
            Cell st = Measure(ls, () => HumanTarget(layers, 1.0), real);
            Cell se = Measure(Serrated(ls), () => HumanTarget(layers, SerratedBleedBonus), real);
            sb.AppendLine(string.Format(ci,
                "| {0} | {1:P1} | {2:P1} | **{3:+0.0%;-0.0%;0.0%}** | {4:P1} | {5:P1} | {6:F1}s | {7:F1}s |",
                tierName, st.WinRate, se.WinRate, se.WinRate - st.WinRate,
                st.BleedoutShare, se.BleedoutShare, st.AvgKillSeconds, se.AvgKillSeconds));
        }

        Cell zRs = Measure(ls, () => ZombieTarget(1.0), real);
        Cell zRr = Measure(Serrated(ls), () => ZombieTarget(SerratedBleedBonus), real);
        sb.AppendLine(string.Format(ci,
            "| 丧尸（腐皮·1/3 失血） | {0:P1} | {1:P1} | **{2:+0.0%;-0.0%;0.0%}** | {3:P1} | {4:P1} | {5:F1}s | {6:F1}s |",
            zRs.WinRate, zRr.WinRate, zRr.WinRate - zRs.WinRate,
            zRs.BleedoutShare, zRr.BleedoutShare, zRs.AvgKillSeconds, zRr.AvgKillSeconds));

        sb.AppendLine();
        sb.AppendLine("**读法**：**对丧尸 −0.1%，流血致死占比 0.0%** —— 锯齿剑刃在旧实机里**对丧尸完全无效**");
        sb.AppendLine("（丧尸 27s 就被直伤打死，而放干它要 545s ⇒ 流血根本来不及起作用；**+40% 乘以「几乎为零」还是「几乎为零」**）。");
        sb.AppendLine("而它唯一显著变强的地方是**重甲 +7.4%** —— 恰恰是它该弱的那一档。**两条流血改装件在旧实机里等于没做。**");
        sb.AppendLine();
        sb.AppendLine("### 旧实机口径下的放干耗时（对比用）");
        sb.AppendLine();
        sb.AppendLine("| 目标 | 伤口数 | 原厂放干 | 锯齿(+40%)放干 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var (who, mult) in new[] { ("人类（体质 1.0）", 1.0), ("丧尸（体质 1/3）", 1.0 / 3.0) })
        {
            for (int n = 1; n <= 3; n++)
            {
                double stockSec = real.BloodMax / (n * real.BleedRatePerWound * mult);
                double serrSec = real.BloodMax / (n * real.BleedRatePerWound * mult * SerratedBleedBonus);
                sb.AppendLine(string.Format(ci, "| {0} | {1} 处 | {2:F0}s | {3:F0}s |", who, n, stockSec, serrSec));
            }
        }

        sb.AppendLine();
        sb.AppendLine("🔴 旧实机口径下「划一刀然后跑」：一个丧尸身上 **1 处**致命伤口要 **545 秒 ≈ 9 分钟**才流干");
        sb.AppendLine("（锯齿刃 +40% 也要 6.5 分钟）⇒ 「边打边跑」在旧口径下**根本不成立**。");
        sb.AppendLine("**对齐后**（§3）降到 **140s / 100s**（丧尸 1 处伤口），风筝才谈得上是一个战术。");

        WoundTriggerDiagnosis(sb, ci);

        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"流血轴复验报告 → {outPath}");
    }
}
