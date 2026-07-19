using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// [批次18补] 用户方案 what-if：**锐器降下限 + 钝器提伤**。
///
/// 用户原话：「1.锐器降低下限，例如匕首伤害改为1-7　2.钝器提高基础伤害」
///
/// 核心待验证命题（来自 weaponsweep 的诊断）：单层护甲要能挡下一击，必须
/// **护甲值×(1−穿透)/2 &gt; 武器伤害下限**。具体门槛随 Wiki 配置动态计算。
/// 本 harness 实测该命题，并把"降下限"推广到全部锐器，给出多套上限方案的代价对比。
///
/// ⚠️ <b>时效</b>：以上是 [批次18补] <b>当时</b>的诊断快照，保留作立项依据，<b>不是现值</b>。
/// 具体武器值以 Wiki 配置表为准；报告正文读运行时配置，不在注释中复制。
///
/// 只读 <see cref="WeaponTable"/>/<see cref="ArmorTable"/> 构造**变体武器**，不改任何游戏数值。
/// </summary>
public static class UserPlanCalibration
{
    private const int HitSamples = 200_000;
    private const int Seeds = 1_000;

    // ---- 锐器方案：如何把一把锐器的伤害区间改写 ----
    private sealed record Plan(string Name, string Note, Func<Weapon, (double Min, double Max)> Map);

    private static List<Plan> Plans() => new()
    {
        new Plan("现值", "不动，作对照", w => (w.DamageMin, w.DamageMax)),

        // 用户原案的形态：匕首 4~10 → 1~7 = 下限 ×0.25、上限 ×0.7。等比推广到全锐器。
        new Plan("方案甲·等比缩放", "照匕首 4~10→1~7 的比例（下限 ×0.25、上限 ×0.7）推全表",
            w => (Math.Round(w.DamageMin * 0.25, 1), Math.Round(w.DamageMax * 0.7, 1))),

        // 统一把下限压到 2（布衣门槛之下），上限不动 → 期望下降、区间拉大。
        new Plan("方案乙·下限统一 2·保上限", "下限一律 2，上限不动",
            w => (2, w.DamageMax)),

        // 统一下限 2，抬上限把期望补回原值 → 期望不塌，但上限膨胀。
        new Plan("方案丙·下限统一 2·保期望", "下限一律 2，上限抬到让平均伤害不变",
            w => (2, Math.Round(2 * ((w.DamageMin + w.DamageMax) / 2) - 2, 1))),

        // 下限压到 1（比 2 更狠，腐皮门槛 1.365 只有下限 1 才够得着）。
        new Plan("方案丁·下限统一 1·保上限", "下限一律 1，上限不动",
            w => (1, w.DamageMax)),

        // 下限 1（布甲救活力度最大）+ 上限抬到保住平均伤害（PvE 锚点不塌）。
        new Plan("方案戊·下限统一 1·保平均", "下限一律 1，上限抬到让平均伤害不变",
            w => (1, Math.Round(2 * ((w.DamageMin + w.DamageMax) / 2) - 1, 1))),
    };

    private static Weapon WithRange(Weapon w, double min, double max) => new()
    {
        Name = w.Name,
        DamageMin = min,
        DamageMax = max,
        Penetration = w.Penetration,
        DamageType = w.DamageType,
        TwoHanded = w.TwoHanded,
        CanDualWield = w.CanDualWield,
        IsRanged = w.IsRanged,
        BurstCount = w.BurstCount,
        BurstInterval = w.BurstInterval,
        AttackInterval = w.AttackInterval,
    };

    /// <summary>按方案改写一把武器：只动锐器；钝器按 bluntMul 缩放伤害。</summary>
    private static Weapon Apply(Weapon w, Plan p, double bluntMul)
    {
        if (w.DamageType == DamageType.Blunt)
        {
            return WithRange(w, Math.Round(w.DamageMin * bluntMul, 1), Math.Round(w.DamageMax * bluntMul, 1));
        }

        var (min, max) = p.Map(w);
        return WithRange(w, min, max);
    }

    // ---- 甲组 ----
    private static ArmorLayer[] Shirt() => new[] { ArmorTable.LongSleeveShirt() };
    private static ArmorLayer[] Mid() => new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() };
    private static ArmorLayer[] Heavy() => new[]
    { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() };
    private static ArmorLayer[] Hide() => ArmorTable.ZombieHide().ToArray();
    private static ArmorLayer[] Starter() => new[]
    { ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(), ArmorTable.Sneakers() };

    private static List<(string Name, ArmorLayer[] Layers)> ArmorSets() => new()
    {
        ("长袖布衣", Shirt()),
        ("皮夹克+长袖布衣", Mid()),
        ("板甲+粗布外套+长袖布衣", Heavy()),
        ("丧尸腐皮", Hide()),
    };

    // ---- 命中统计（不含击杀计时，快）----
    private sealed record Hits(double Blocked, double Half, double Damage, double Dps, double Sever, double Bleed);

    private static Hits MeasureHits(Weapon w, ArmorLayer[] template, IReadOnlyList<BodyPart> parts, int salt)
    {
        var rng = new SystemRandomSource(880000 + salt);
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);
        var effects = new CombatEffectResolver(rng);
        var layers = CombatResolver.OrderOuterToInner(template);

        int blocked = 0, half = 0, armored = 0, sever = 0, bleed = 0;
        double dmgArmored = 0, dmgAll = 0;

        for (int i = 0; i < HitSamples; i++)
        {
            var part = hit.Select(parts);
            bool covered = layers.Any(l => l.Covers(part));
            var r = resolver.Resolve(w, layers, part);
            dmgAll += r.FinalDamage;

            // 效果统计：每次命中打一具满血人体（切除/流血受伤害上限影响，抬上限的副作用要看这里）
            var outcome = effects.Apply(new Body(parts), w, r);
            foreach (var e in outcome.Effects)
            {
                if (e.Kind == DamageEffectKind.Sever) { sever++; break; }
            }
            foreach (var e in outcome.Effects)
            {
                if (e.Kind == DamageEffectKind.Bleed) { bleed++; break; }
            }

            if (!covered) continue;
            armored++;
            dmgArmored += r.FinalDamage;
            if (r.Terminated) blocked++;
            else if (r.Layers.Any(l => l.Outcome == LayerOutcome.Half)) half++;
        }

        int burst = Math.Max(1, w.BurstCount);
        double cycle = w.AttackInterval + (burst - 1) * Math.Max(0, w.BurstInterval);
        double dps = cycle > 0 ? dmgAll / HitSamples * burst / cycle : 0;

        return new Hits(
            armored > 0 ? (double)blocked / armored : 0,
            armored > 0 ? (double)half / armored : 0,
            armored > 0 ? dmgArmored / armored : 0,
            dps,
            (double)sever / HitSamples,
            (double)bleed / HitSamples);
    }

    private static DuelFighter F(string name, Weapon w, ArmorLayer[] armor, Func<Body>? bodyFactory = null) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
        Armor = armor,
        // 默认人类；丧尸传 HumanBody.NewZombieBody（失血流速 1/3，用户口径）。
        BodyFactory = bodyFactory ?? HumanBody.NewBody,
    };

    private static (double WinRate, double Duration) Duel(DuelFighter a, DuelFighter b)
    {
        int wins = 0;
        double dur = 0;
        for (int s = 0; s < Seeds; s++)
        {
            var r = new DuelEngine(new SystemRandomSource(20260713 + s * 131)).Run(a, b);
            dur += r.DurationSeconds;
            if (r.Winner == a.Name) wins++;
        }

        return ((double)wins / Seeds, dur / Seeds);
    }

    /// <summary>击杀一个只穿甲、不还手的靶所需秒数（NaN = 600 秒内没打死）。</summary>
    private static double Ttk(Weapon w, ArmorLayer[] armor)
    {
        int killed = 0;
        double sum = 0;
        for (int s = 0; s < Seeds; s++)
        {
            var r = new DuelEngine(new SystemRandomSource(770100 + s * 131))
                .Run(F("攻方", w, Array.Empty<ArmorLayer>()), F("靶", Harmless, armor));
            if (r.Winner == "攻方") { killed++; sum += r.DurationSeconds; }
        }

        return killed > 0 ? sum / killed : double.NaN;
    }

    private static Weapon Harmless => new()
    { Name = "无", DamageMin = 0, DamageMax = 0, DamageType = DamageType.Blunt, AttackInterval = 1e6 };

    public static void Run(string outPath)
    {
        var parts = HumanBody.Parts();
        var plans = Plans();
        var sets = ArmorSets();
        var arsenal = WeaponTable.Arsenal().ToList();
        var sharp = arsenal.Where(w => w.DamageType == DamageType.Sharp).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("## [批次18补] 用户方案 what-if：锐器降下限 + 钝器提伤");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"日期：2026-07-13　样本：每格 {HitSamples:N0} 次命中 + {Seeds:N0} 场对决　复现：`~/.dotnet/dotnet run --project src/DeadSignal.Sim -- userplan`");
        sb.AppendLine();
        sb.AppendLine("用户方案：**① 锐器降低下限（例：匕首 4~10 → 1~7）　② 钝器提高基础伤害**。");
        sb.AppendLine();

        RenderDaggerFirst(sb, parts);
        RenderThreshold(sb);
        RenderPlanPanel(sb, sharp, plans);
        RenderPlanEffects(sb, arsenal, plans, sets, parts);
        RenderBaselines(sb, plans, parts);
        RenderBlunt(sb, plans, parts);
        RenderClaw(sb, parts);

        // 追加到既有报告（不重写原有章节）。基底是**手写散文**，只有 --- 以下这一节是机器生成，
        // 故出处戳只盖在**追加的这一节**头上（不走 SimReport.Write 给整份盖头——那会谎称手写正文也是机器跑的）。
        string existing = File.Exists(outPath) ? File.ReadAllText(outPath) : "";
        File.WriteAllText(outPath, existing + "\n---\n\n" + SimReport.Stamp() + sb);
        Console.WriteLine($"已追加到 {outPath}");
        Console.WriteLine();
        Console.Write(sb.ToString());
    }

    /// <summary>最重要的一张表：匕首 1~7 到底有没有救活布甲/腐皮。</summary>
    private static void RenderDaggerFirst(StringBuilder sb, IReadOnlyList<BodyPart> parts)
    {
        sb.AppendLine("### 1. 最要紧的一问：匕首 1~7 有没有救活布甲？");
        sb.AppendLine();

        var candidates = new (string Label, Weapon W)[]
        {
            ("现值 4~10（平均 7）", WeaponTable.Dagger()),
            ("用户原案 1~7（平均 4）", WithRange(WeaponTable.Dagger(), 1, 7)),
            ("2~10（平均 6，保上限）", WithRange(WeaponTable.Dagger(), 2, 10)),
            ("1~10（平均 5.5，保上限）", WithRange(WeaponTable.Dagger(), 1, 10)),
            ("**1~13（平均 7，保平均）**", WithRange(WeaponTable.Dagger(), 1, 13)),
        };

        var zombie = F("丧尸", WeaponTable.ZombieClaw(), ArmorTable.ZombieHide().ToArray(), HumanBody.NewZombieBody);

        sb.AppendLine("| 匕首改成 | 长袖布衣挡下率 | 丧尸腐皮挡下率 | 皮夹克组挡下率 | 重甲挡下率 | 打丧尸胜率 | 打丧尸耗时 | 切除率 |");
        sb.AppendLine("|----------|---------------:|---------------:|---------------:|-----------:|-----------:|-----------:|-------:|");
        int salt = 0;
        foreach (var (label, w) in candidates)
        {
            var shirt = MeasureHits(w, Shirt(), parts, salt++);
            var hide = MeasureHits(w, Hide(), parts, salt++);
            var mid = MeasureHits(w, Mid(), parts, salt++);
            var heavy = MeasureHits(w, Heavy(), parts, salt++);
            var bare = MeasureHits(w, Array.Empty<ArmorLayer>(), parts, salt++);
            var d = Duel(F("我方", w, Starter()), zombie);
            string flag = d.WinRate is >= 0.85 and <= 0.95 ? " ✅" : " ❌";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {label} | **{shirt.Blocked:P1}** | {hide.Blocked:P1} | {mid.Blocked:P1} | {heavy.Blocked:P1} | {d.WinRate:P1}{flag} | {d.Duration:F0} 秒 | {bare.Sever:P1} |");
        }
        sb.AppendLine();
        sb.AppendLine("> 「切除率」= 无甲命中时打断一个部位的比例（抬上限的副作用看这里）。「✅/❌」= 打丧尸是否还在 85~95% 的锚点带内。");
        sb.AppendLine();
    }

    /// <summary>各甲种的"下限门槛"——武器下限必须低于这个数，这件甲才可能挡下一击。</summary>
    private static void RenderThreshold(StringBuilder sb)
    {
        sb.AppendLine("### 2. 各件护甲的「下限门槛」——武器下限必须压到这个数以下，这件甲才开始有效");
        sb.AppendLine();
        sb.AppendLine("门槛 = 护甲防御值 ×(1−武器穿透) ÷ 2。武器伤害下限只要 ≥ 门槛，这件甲就**一次也挡不下**（数学上恒为零，不是概率低）。");
        double pen = WeaponTable.Dagger().Penetration;
        sb.AppendLine("下表按当前武器配置计算；穿透越高门槛越低。");
        sb.AppendLine();
        sb.AppendLine("| 护甲 | 对锐器防御 | 锐器下限门槛 | 对钝器防御 | 钝器下限门槛 |");
        sb.AppendLine("|------|-----------:|-------------:|-----------:|-------------:|");
        var rows = new (string Name, ArmorLayer Layer)[]
        {
            ("丧尸腐皮", ArmorTable.ZombieHide()[0]),
            ("长袖布衣", ArmorTable.LongSleeveShirt()),
            ("长裤", ArmorTable.Trousers()),
            ("粗布外套", ArmorTable.CoarseClothCoat()),
            ("粗布背心", ArmorTable.CoarseClothVest()),
            ("皮夹克", ArmorTable.LeatherJacket()),
            ("皮甲", ArmorTable.Leather()),
            ("皮革胸甲", ArmorTable.ChestPlate()),
            ("板甲", ArmorTable.Plate()),
        };
        foreach (var (n, layer) in rows)
        {
            double s = layer.SharpDefense;
            double b = layer.BluntDefense;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {n} | {s:0.#} | **{s * (1 - pen) / 2:F2}** | {b:0.#} | **{b * (1 - pen) / 2:F2}** |");
        }
        sb.AppendLine();
        var arsenal = WeaponTable.Arsenal();
        double clothThreshold = 6 * (1 - pen) / 2;
        var lowest = arsenal.MinBy(w => w.DamageMin)!;
        int below = arsenal.Count(w => w.DamageMin < clothThreshold);
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"> 现有 {arsenal.Count} 把武器里，有 **{below}** 把的伤害下限低于布衣门槛 {clothThreshold:F2}（最低是{lowest.Name} {lowest.DamageMin:0.#}）——下限低于门槛的那些，布衣才可能挡得下；下限压不下去的，布衣对它恒为 0%。"));
        sb.AppendLine();
    }

    private static void RenderPlanPanel(StringBuilder sb, List<Weapon> sharp, List<Plan> plans)
    {
        sb.AppendLine("### 3. 全锐器推荐下限：三套方案对照");
        sb.AppendLine();
        sb.Append("| 锐器 | 现值（平均） |");
        foreach (var p in plans.Skip(1)) sb.Append(CultureInfo.InvariantCulture, $" {p.Name} |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in plans.Skip(1)) sb.Append("------|");
        sb.AppendLine();
        foreach (var w in sharp)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"| {w.Name} | {w.DamageMin:0.#}~{w.DamageMax:0.#}（{(w.DamageMin + w.DamageMax) / 2:0.#}） |");
            foreach (var p in plans.Skip(1))
            {
                var (min, max) = p.Map(w);
                string flag = min >= 2.73 ? " ⚠️" : "";
                sb.Append(CultureInfo.InvariantCulture, $" {min:0.#}~{max:0.#}（{(min + max) / 2:0.#}）{flag} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("> ⚠️ = 下限仍在布衣门槛 2.7 之上，**这把武器对布衣仍然是 0% 挡下率**（方案没救到它）。");
        foreach (var p in plans.Skip(1))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"> **{p.Name}**：{p.Note}。");
        }
        sb.AppendLine();
    }

    private static void RenderPlanEffects(StringBuilder sb, List<Weapon> arsenal, List<Plan> plans,
        List<(string Name, ArmorLayer[] Layers)> sets, IReadOnlyList<BodyPart> parts)
    {
        sb.AppendLine("### 4. 各方案下，护甲的挡下率（全锐器平均）");
        sb.AppendLine();
        var sharp = arsenal.Where(w => w.DamageType == DamageType.Sharp).ToList();

        var grid = new double[plans.Count, sets.Count];
        var dmgGrid = new double[plans.Count, sets.Count];
        Parallel.For(0, plans.Count * sets.Count, k =>
        {
            int pi = k / sets.Count, si = k % sets.Count;
            double sumB = 0, sumD = 0;
            for (int i = 0; i < sharp.Count; i++)
            {
                var v = Apply(sharp[i], plans[pi], 1.0);
                var h = MeasureHits(v, sets[si].Layers, parts, k * 100 + i);
                sumB += h.Blocked;
                sumD += h.Damage;
            }
            grid[pi, si] = sumB / sharp.Count;
            dmgGrid[pi, si] = sumD / sharp.Count;
        });

        sb.Append("| 锐器方案 |");
        foreach (var s in sets) sb.Append(CultureInfo.InvariantCulture, $" {s.Name} 挡下率 |");
        sb.AppendLine();
        sb.Append("|------|");
        foreach (var _ in sets) sb.Append("------:|");
        sb.AppendLine();
        for (int pi = 0; pi < plans.Count; pi++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {plans[pi].Name} |");
            for (int si = 0; si < sets.Count; si++)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {grid[pi, si]:P1} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("同一张表，换成「一下能打进去多少伤害」（命中甲片时）：");
        sb.AppendLine();
        sb.Append("| 锐器方案 |");
        foreach (var s in sets) sb.Append(CultureInfo.InvariantCulture, $" {s.Name} |");
        sb.AppendLine();
        sb.Append("|------|");
        foreach (var _ in sets) sb.Append("------:|");
        sb.AppendLine();
        for (int pi = 0; pi < plans.Count; pi++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {plans[pi].Name} |");
            for (int si = 0; si < sets.Count; si++)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {dmgGrid[pi, si]:F2} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    private static void RenderBaselines(StringBuilder sb, List<Plan> plans, IReadOnlyList<BodyPart> parts)
    {
        sb.AppendLine("### 5. 主基线是否还在目标带");
        sb.AppendLine();
        sb.AppendLine("两个锚：**匕首打丧尸 ≈ 90%**、**步枪打长剑手 70~80%**（双方同穿皮夹克组）。");
        sb.AppendLine();

        var res = new (double Zombie, double RifleVsSword, double DaggerTtkHeavy, double SwordTtkHeavy)[plans.Count];
        Parallel.For(0, plans.Count, pi =>
        {
            var p = plans[pi];
            var dagger = Apply(WeaponTable.Dagger(), p, 1.0);
            var rifle = Apply(WeaponTable.Rifle(), p, 1.0);
            var sword = Apply(WeaponTable.Longsword(), p, 1.0);
            var zombie = F("丧尸", WeaponTable.ZombieClaw(), Hide(), HumanBody.NewZombieBody);

            double z = Duel(F("我方", dagger, Starter()), zombie).WinRate;
            double r = Duel(F("步枪手", rifle, Mid()), F("长剑手", sword, Mid())).WinRate;
            res[pi] = (z, r, Ttk(dagger, Heavy()), Ttk(sword, Heavy()));
        });

        sb.AppendLine("| 锐器方案 | 匕首打丧尸（锚 ~90%） | 步枪打长剑手（锚 70~80%） | 匕首打重甲耗时 | 长剑打重甲耗时 |");
        sb.AppendLine("|------|------:|------:|------:|------:|");
        for (int pi = 0; pi < plans.Count; pi++)
        {
            var (z, r, dt, st) = res[pi];
            string zf = z is >= 0.85 and <= 0.95 ? "✅" : "❌";
            string rf = r is >= 0.70 and <= 0.80 ? "✅" : "❌";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {plans[pi].Name} | {z:P1} {zf} | {r:P1} {rf} | {Sec(dt)} | {Sec(st)} |");
        }
        sb.AppendLine();
    }

    private static void RenderBlunt(StringBuilder sb, List<Plan> plans, IReadOnlyList<BodyPart> parts)
    {
        sb.AppendLine("### 6. 钝器该提多少（在锐器已降下限的新环境里重算）");
        sb.AppendLine();
        sb.AppendLine("钝器**吃不到「降下限救薄甲」这一手**：具体门槛随 Wiki 护甲与武器配置动态变化。");
        sb.AppendLine("所以钝器的路子只有一条：**提高基础伤害**，靠伤害量级压过护甲掷点。");
        sb.AppendLine();

        // 在推荐的「方案戊」环境下重算：锐器全部下限 1、上限抬到保平均（对手长剑 = 1~39，平均仍是 20）
        var envPlan = plans.First(p => p.Name.StartsWith("方案戊", StringComparison.Ordinal));
        var swordNew = Apply(WeaponTable.Longsword(), envPlan, 1.0);
        var zombie = F("丧尸", WeaponTable.ZombieClaw(), Hide(), HumanBody.NewZombieBody);

        var rows = new List<(string W, double Mul)>();
        foreach (double m in new[] { 1.0, 1.2, 1.3, 1.5, 1.8 }) rows.Add(("棍棒", m));
        foreach (double m in new[] { 1.0, 1.2, 1.3, 1.5 }) rows.Add(("尖头锤", m));
        foreach (double m in new[] { 1.0, 1.2 }) rows.Add(("破甲锤", m));

        var outv = new (string Label, double Zombie, double VsSword, double Ttk, double Blocked, double Dps)[rows.Count];
        Parallel.For(0, rows.Count, i =>
        {
            var (name, mul) = rows[i];
            Weapon baseW = name switch
            {
                "棍棒" => WeaponTable.Club(),
                "尖头锤" => WeaponTable.SpikeHammer(),
                _ => WeaponTable.Warhammer(),
            };
            var v = WithRange(baseW, Math.Round(baseW.DamageMin * mul, 1), Math.Round(baseW.DamageMax * mul, 1));
            double z = Duel(F("我方", v, Starter()), zombie).WinRate;
            double s = Duel(F("我方", v, Mid()), F("长剑手", swordNew, Mid())).WinRate;
            var h = MeasureHits(v, Heavy(), parts, 5000 + i);
            outv[i] = (string.Create(CultureInfo.InvariantCulture,
                $"{name} ×{mul:0.0}（{v.DamageMin:0.#}~{v.DamageMax:0.#}）"), z, s, Ttk(v, Heavy()), h.Blocked, h.Dps);
        });

        sb.AppendLine("| 钝器改成 | 打丧尸胜率 | 打长剑手胜率（长剑已按方案戊改成 1~39） | 打重甲耗时 | 重甲挡下率 |");
        sb.AppendLine("|------|------:|------:|------:|------:|");
        foreach (var (label, z, s, t, b, _) in outv)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {label} | {z:P1} | {s:P1} | {Sec(t)} | {b:P1} |");
        }
        sb.AppendLine();
    }

    private static void RenderClaw(StringBuilder sb, IReadOnlyList<BodyPart> parts)
    {
        sb.AppendLine("### 7. 副作用扫描：丧尸的爪击要不要也降下限？");
        sb.AppendLine();
        sb.AppendLine("爪击（3~9）也是锐器。如果「锐器降下限」是**全局规则**，爪击也该降 → 玩家的布衣就能挡下丧尸的爪子 → 玩家变强。");
        sb.AppendLine("如果只降玩家武器、不降爪击，就是**单方面削弱玩家**。这一格要你拍板，下面是两条路的实测。");
        sb.AppendLine();

        var dagger17 = WithRange(WeaponTable.Dagger(), 1, 7);
        var dagger113 = WithRange(WeaponTable.Dagger(), 1, 13);
        var clawNow = WeaponTable.ZombieClaw();          // 3~9（平均 6）
        var clawLow = WithRange(clawNow, 1, 9);          // 保上限 → 平均降到 5
        var clawKeep = WithRange(clawNow, 1, 11);        // 保平均 6

        sb.AppendLine("| 情形 | 玩家打丧尸胜率 | 玩家布衣挡下爪击的比例 |");
        sb.AppendLine("|------|------:|------:|");

        var cases = new (string Label, Weapon Player, Weapon Claw)[]
        {
            ("现状（匕首 4~10，爪击 3~9）", WeaponTable.Dagger(), clawNow),
            ("只降玩家武器（匕首 1~7，爪击不动）＝单方面削玩家", dagger17, clawNow),
            ("全局降下限·都不保平均（匕首 1~7，爪击 1~9）", dagger17, clawLow),
            ("**推荐：全局降下限+都保平均（匕首 1~13，爪击 1~11）**", dagger113, clawKeep),
            ("只改玩家、爪击不动，但玩家保平均（匕首 1~13，爪击 3~9）", dagger113, clawNow),
        };
        int salt = 9000;
        foreach (var (label, player, claw) in cases)
        {
            double z = Duel(F("我方", player, Starter()), F("丧尸", claw, Hide())).WinRate;
            var h = MeasureHits(claw, Shirt(), parts, salt++);
            string flag = z is >= 0.85 and <= 0.95 ? " ✅" : " ❌";
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {z:P1}{flag} | {h.Blocked:P1} |");
        }
        sb.AppendLine();
        sb.AppendLine("> 爪击若也降下限但**不保平均**（1~9，平均 6→5），丧尸变弱、玩家变强，两头都动锚点。");
        sb.AppendLine("> 爪击 **1~11（保平均 6）** 才是对称的做法：丧尸强度不变，但玩家的布衣开始能挡下爪子。");
        sb.AppendLine();
    }

    private static string Sec(double t) =>
        double.IsNaN(t) ? "打不死" : string.Create(CultureInfo.InvariantCulture, $"{t:F0} 秒");
}
