using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 武器 × 甲组失衡诊断 harness（[SPEC-B18] 护甲整表重做后，武器数值一格未动 → 拉表定位失衡）。
///
/// 两套口径互补：
/// 1. <b>单次命中统计</b>（蒙特卡洛，命中部位按体积权重随机）——期望伤害 / 无伤率 / 穿透层数 / 锐转钝降解率。
///    关键：[SPEC-B18] 覆盖收窄后头/手/脚多为裸露，全身平均会被裸命中稀释，故**同时输出「命中有甲部位」的条件统计**
///    ——那才是"打不穿"的真证据。
/// 2. <b>TTK（击杀耗时）</b>——攻方打一个**不还手的靶**（靶只穿甲、武器无害），复用 <see cref="DuelEngine"/>
///    的完整结算（含连发/失血/震荡/切除）。超时（<see cref="DuelConfig.MaxSimSeconds"/>）未击杀 = 实质打不穿。
///    TTK 把攻速与伤害合并成单一可比量纲，跨近战/远程可比。
///
/// 本 harness 只读 <see cref="WeaponTable"/>/<see cref="ArmorTable"/>，不改任何数值。
/// </summary>
public static class WeaponCalibration
{
    private const int HitSamples = 200_000;
    private const int TtkSeeds = 1_000;

    // ---- 甲组（含用户点名的头甲变体）----
    private static List<(string Name, ArmorLayer[] Layers)> Combos() => new()
    {
        ("裸甲", Array.Empty<ArmorLayer>()),
        ("轻甲·长袖布衣", new[] { ArmorTable.LongSleeveShirt() }),
        ("中甲·皮夹克+长袖布衣", new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() }),
        ("重甲·板甲+粗布外套+长袖布衣", new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
        ("重甲+铁皮头甲", new[] { ArmorTable.Plate(), ArmorTable.DogIronHelmet(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
        ("重甲+铁丝头甲", new[] { ArmorTable.Plate(), ArmorTable.DogWireHelmet(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
        ("丧尸腐皮", ArmorTable.ZombieHide().ToArray()),
    };

    // ---- 旧护甲对照组（[SPEC-B18] 之前的值，工具内联；只作 A/B 对照，不改 ArmorTable）----
    // 旧值取自护甲重做前的 ArmorTable：布衣 4/2、皮甲(外套层) 12/6、板甲 34/11；且全部**全身覆盖**。
    private static ArmorLayer OldCloth() => new()
    { Name = "旧布衣", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2, Weight = 1 };

    private static ArmorLayer OldLeather() => new()
    { Name = "旧皮甲", Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 4 };

    private static ArmorLayer OldPlate() => new()
    { Name = "旧板甲", Slot = ArmorSlot.Plate, SharpDefense = 34, BluntDefense = 11, Weight = 12 };

    private static ArmorLayer OldZombieHide() => new()
    { Name = "旧腐皮", Slot = ArmorSlot.Skin, SharpDefense = 1.5, BluntDefense = 3, Weight = 0 };

    private static List<(string Name, ArmorLayer[] Old, ArmorLayer[] New)> AbPairs() => new()
    {
        ("轻甲（布类一层）", new[] { OldCloth() }, new[] { ArmorTable.LongSleeveShirt() }),
        ("中甲（外套+贴身）", new[] { OldLeather(), OldCloth() },
            new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() }),
        ("重甲（三层）", new[] { OldPlate(), OldLeather(), OldCloth() },
            new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
        ("丧尸", new[] { OldZombieHide() }, ArmorTable.ZombieHide().ToArray()),
    };

    private sealed record Cell(
        string Weapon, string Combo,
        double Dps, double DamagePerSwing,
        double BlockedAll, double BlockedArmored, double HalfArmored, double DamageArmored,
        double BareHitShare, double DegradeRate,
        double[] PenDist,          // 穿透层数分布（index=层数）
        double AvgPenLayers,
        double Ttk, double NoKillRate);

    public static void Run(string outPath)
    {
        var weapons = WeaponTable.Arsenal().ToList();
        var combos = Combos();
        var parts = HumanBody.Parts();

        var units = new List<(int Idx, Weapon W, string ComboName, ArmorLayer[] Layers)>();
        int idx = 0;
        foreach (var w in weapons)
        {
            foreach (var (cn, cl) in combos)
            {
                units.Add((idx++, w, cn, cl));
            }
        }

        var cells = new Cell[units.Count];
        Parallel.ForEach(units, u =>
        {
            cells[u.Idx] = Measure(u.W, u.ComboName, u.Layers, parts);
        });

        // ---- 胜率（三条实战对局）----
        var winRates = new (double VsZombie, double VsMidLongsword, double VsHeavyLongsword)[weapons.Count];
        Parallel.For(0, weapons.Count, i =>
        {
            var w = weapons[i];
            var zombie = Fighter("丧尸", WeaponTable.ZombieClaw(), ArmorTable.ZombieHide().ToArray());
            double a = Duel(Fighter("我方", w, StarterKit()), zombie, TtkSeeds).WinRate;
            double b = Duel(Fighter("我方", w, MidKit()),
                Fighter("长剑手", WeaponTable.Longsword(), MidKit()), TtkSeeds).WinRate;
            double c = Duel(Fighter("我方", w, MidKit()),
                Fighter("长剑重装", WeaponTable.Longsword(), HeavyKit()), TtkSeeds).WinRate;
            winRates[i] = (a, b, c);
        });

        // ---- 新旧护甲 A/B 对照（同武器，旧甲值 vs 新甲值）----
        var pairs = AbPairs();
        var ab = new (double OldTtk, double NewTtk, double OldBlocked, double NewBlocked)[weapons.Count, pairs.Count];
        Parallel.For(0, weapons.Count * pairs.Count, k =>
        {
            int wi = k / pairs.Count, pi = k % pairs.Count;
            var w = weapons[wi];
            var p = pairs[pi];
            var mOld = Measure(w, p.Name, p.Old, parts);
            var mNew = Measure(w, p.Name, p.New, parts);
            ab[wi, pi] = (mOld.Ttk, mNew.Ttk, mOld.BlockedArmored, mNew.BlockedArmored);
        });

        var sb = new StringBuilder();
        Render(sb, weapons, combos, cells.ToList(), winRates, pairs, ab);
        RenderWhatIf(sb, parts);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"已写出 {outPath}（{weapons.Count} 武器 × {combos.Count} 甲组；命中样本 {HitSamples:N0}/单元，TTK 种子 {TtkSeeds:N0}/单元）。");
        Console.WriteLine();
        Console.Write(sb.ToString());
    }

    private static Cell Measure(Weapon w, string comboName, ArmorLayer[] template, IReadOnlyList<BodyPart> parts)
    {
        var rng = new SystemRandomSource(20260713 + comboName.GetHashCode() % 1000 + w.Name.Length * 7919);
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);
        var layers = CombatResolver.OrderOuterToInner(template);

        double dmgSum = 0, dmgArmoredSum = 0;
        int blockedAll = 0, blockedArmored = 0, halfArmored = 0, armoredHits = 0, bareHits = 0, degraded = 0;
        var penDist = new long[5];
        long penSum = 0;

        for (int i = 0; i < HitSamples; i++)
        {
            var part = hit.Select(parts);
            bool covered = layers.Any(l => l.Covers(part));
            var r = resolver.Resolve(w, layers, part);

            dmgSum += r.FinalDamage;
            penSum += r.LayersPenetrated;
            penDist[Math.Min(r.LayersPenetrated, 4)]++;
            if (r.Terminated) blockedAll++;

            if (covered)
            {
                armoredHits++;
                dmgArmoredSum += r.FinalDamage;
                if (r.Terminated) blockedArmored++;
                else if (r.Layers.Any(l => l.Outcome == LayerOutcome.Half)) halfArmored++;
                if (w.DamageType == DamageType.Sharp && r.FinalDamageType == DamageType.Blunt) degraded++;
            }
            else
            {
                bareHits++;
            }
        }

        int burst = Math.Max(1, w.BurstCount);
        double perHit = dmgSum / HitSamples;
        double perSwing = perHit * burst;
        double cycle = w.AttackInterval + (burst - 1) * Math.Max(0, w.BurstInterval);
        double dps = cycle > 0 ? perSwing / cycle : 0;

        var (ttk, noKill) = Ttk(w, template);

        return new Cell(
            w.Name, comboName,
            dps, perSwing,
            (double)blockedAll / HitSamples,
            armoredHits > 0 ? (double)blockedArmored / armoredHits : 0,
            armoredHits > 0 ? (double)halfArmored / armoredHits : 0,
            armoredHits > 0 ? dmgArmoredSum / armoredHits : 0,
            (double)bareHits / HitSamples,
            armoredHits > 0 ? (double)degraded / armoredHits : 0,
            penDist.Select(c => (double)c / HitSamples).ToArray(),
            (double)penSum / HitSamples,
            ttk, noKill);
    }

    /// <summary>1v1 胜率（A 视角）。每场重建（Body 有状态）。</summary>
    private static (double WinRate, double AvgDuration) Duel(DuelFighter a, DuelFighter b, int seeds)
    {
        int wins = 0;
        double dur = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            var r = new DuelEngine(new SystemRandomSource(20260713 + seed * 131)).Run(a, b);
            dur += r.DurationSeconds;
            if (r.Winner == a.Name) wins++;
        }

        return ((double)wins / seeds, dur / seeds);
    }

    private static DuelFighter Fighter(string name, Weapon w, ArmorLayer[] armor) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
        Armor = armor,
    };

    /// <summary>开局三件套（长袖布衣+长裤+运动鞋）——PvE 基线的玩家着装。</summary>
    private static ArmorLayer[] StarterKit() => new[]
    { ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(), ArmorTable.Sneakers() };

    private static ArmorLayer[] MidKit() => new[]
    { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() };

    private static ArmorLayer[] HeavyKit() => new[]
    { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() };

    /// <summary>攻方 vs 不还手的靶（只穿甲）。返回（击杀成功场次的平均耗时秒, 超时未击杀率）。</summary>
    private static (double Ttk, double NoKillRate) Ttk(Weapon w, ArmorLayer[] armor)
    {
        int killed = 0;
        double sum = 0;
        for (int seed = 0; seed < TtkSeeds; seed++)
        {
            var attacker = new DuelFighter
            {
                Name = "攻方",
                Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
                Armor = Array.Empty<ArmorLayer>(),
            };
            var dummy = new DuelFighter
            {
                Name = "靶",
                Weapons = new[] { new WeaponMount { Weapon = Harmless, RequiresHand = false } },
                Armor = armor,
            };
            var r = new DuelEngine(new SystemRandomSource(770100 + seed * 131)).Run(attacker, dummy);
            if (r.Winner == "攻方")
            {
                killed++;
                sum += r.DurationSeconds;
            }
        }

        return (killed > 0 ? sum / killed : double.NaN, 1.0 - (double)killed / TtkSeeds);
    }

    /// <summary>靶的"武器"：零伤、冷却远超 MaxSimSeconds，保证靶从不出手（TTK 纯净）。</summary>
    private static Weapon Harmless => new()
    {
        Name = "无",
        DamageMin = 0,
        DamageMax = 0,
        Penetration = 0,
        DamageType = DamageType.Blunt,
        AttackInterval = 1e6,
    };

    private static void Render(StringBuilder sb, IReadOnlyList<Weapon> weapons,
        List<(string Name, ArmorLayer[] Layers)> combos, List<Cell> cells,
        (double VsZombie, double VsMidLongsword, double VsHeavyLongsword)[] winRates,
        List<(string Name, ArmorLayer[] Old, ArmorLayer[] New)> pairs,
        (double OldTtk, double NewTtk, double OldBlocked, double NewBlocked)[,] ab)
    {
        Cell Get(string w, string c) => cells.First(x => x.Weapon == w && x.Combo == c);

        sb.AppendLine("# 武器 × 甲组失衡诊断（护甲整表重做后·武器未动）");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"样本：每单元 {HitSamples:N0} 次命中 + {TtkSeeds:N0} 场击杀计时　武器 {weapons.Count} 把 × 甲组 {combos.Count} 组");
        sb.AppendLine();
        sb.AppendLine("> 「命中有甲部位」= 排除了头/手/脚等本次未被护甲覆盖的裸露部位后的条件统计——护甲覆盖收窄后，全身平均会被裸命中稀释，只有这一列能看出「打不穿」。");
        sb.AppendLine("> 「击杀耗时」= 打一个只穿甲、不还手的靶到死所需秒数（含攻速、连发、流血、切除）；打不死率 = 600 秒内没打死的比例。");
        sb.AppendLine("> 远程武器假设弹道命中（几何误差与射程衰减属实时层，不在此模型内）。");
        sb.AppendLine();

        sb.AppendLine("## 表 1：每秒伤害（全身平均，含攻速与连发）");
        sb.AppendLine();
        sb.Append("| 武器 | 类型 |");
        foreach (var (cn, _) in combos) sb.Append(CultureInfo.InvariantCulture, $" {cn} |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in combos) sb.Append("------:|");
        sb.AppendLine();
        foreach (var w in weapons)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {w.Name} | {TypeOf(w)} |");
            foreach (var (cn, _) in combos)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {Get(w.Name, cn).Dps:F2} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 表 2：命中有甲部位时的无伤率（护甲把这一下完全吃掉的比例）");
        sb.AppendLine();
        sb.Append("| 武器 | 类型 |");
        foreach (var (cn, _) in combos) sb.Append(CultureInfo.InvariantCulture, $" {cn} |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in combos) sb.Append("------:|");
        sb.AppendLine();
        foreach (var w in weapons)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {w.Name} | {TypeOf(w)} |");
            foreach (var (cn, _) in combos)
            {
                var c = Get(w.Name, cn);
                sb.Append(cn == "裸甲" ? " — |" : string.Create(CultureInfo.InvariantCulture, $" {c.BlockedArmored:P1} |"));
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 表 3：命中有甲部位时的期望伤害（一下能打进去多少）");
        sb.AppendLine();
        sb.Append("| 武器 | 类型 |");
        foreach (var (cn, _) in combos) sb.Append(CultureInfo.InvariantCulture, $" {cn} |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in combos) sb.Append("------:|");
        sb.AppendLine();
        foreach (var w in weapons)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {w.Name} | {TypeOf(w)} |");
            foreach (var (cn, _) in combos)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {Get(w.Name, cn).DamageArmored:F2} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 表 4：击杀耗时（秒）／打不死率");
        sb.AppendLine();
        sb.Append("| 武器 | 类型 |");
        foreach (var (cn, _) in combos) sb.Append(CultureInfo.InvariantCulture, $" {cn} |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in combos) sb.Append("------:|");
        sb.AppendLine();
        foreach (var w in weapons)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {w.Name} | {TypeOf(w)} |");
            foreach (var (cn, _) in combos)
            {
                var c = Get(w.Name, cn);
                string ttk = double.IsNaN(c.Ttk) ? "打不死" : string.Create(CultureInfo.InvariantCulture, $"{c.Ttk:F0}");
                string nk = c.NoKillRate > 0.001 ? string.Create(CultureInfo.InvariantCulture, $" ({c.NoKillRate:P0})") : "";
                sb.Append(CultureInfo.InvariantCulture, $" {ttk}{nk} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 表 5：穿透层数分布与锐器降解率（仅重甲三层组）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 类型 | 平均穿透层数 | 0层(被挡) | 1层 | 2层 | 3层+ | 锐转钝降解率 |");
        sb.AppendLine("|------|------|-------------:|----------:|----:|----:|-----:|-------------:|");
        const string heavy = "重甲·板甲+粗布外套+长袖布衣";
        foreach (var w in weapons)
        {
            var c = Get(w.Name, heavy);
            string deg = w.DamageType == DamageType.Sharp
                ? string.Create(CultureInfo.InvariantCulture, $"{c.DegradeRate:P1}")
                : "—（天然钝器）";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {w.Name} | {TypeOf(w)} | {c.AvgPenLayers:F2} | {c.PenDist[0]:P1} | {c.PenDist[1]:P1} | {c.PenDist[2]:P1} | {c.PenDist[3] + c.PenDist[4]:P1} | {deg} |");
        }
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"> 「0层」含命中裸露部位（无甲可穿=0层但满伤）。裸露命中占比约 {Get("匕首", heavy).BareHitShare:P0}（头/手/脚/眼等不在覆盖内）。");
        sb.AppendLine();

        sb.AppendLine("## 表 6：命中有甲部位时的半伤率（锐器在此转钝、伤害减半）");
        sb.AppendLine();
        sb.Append("| 武器 | 类型 |");
        foreach (var (cn, _) in combos) sb.Append(CultureInfo.InvariantCulture, $" {cn} |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in combos) sb.Append("------:|");
        sb.AppendLine();
        foreach (var w in weapons)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {w.Name} | {TypeOf(w)} |");
            foreach (var (cn, _) in combos)
            {
                var c = Get(w.Name, cn);
                sb.Append(cn == "裸甲" ? " — |" : string.Create(CultureInfo.InvariantCulture, $" {c.HalfArmored:P1} |"));
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 表 7：实战胜率（1v1，到死）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 类型 | 打丧尸（我方开局三件套） | 打长剑手·中甲（双方中甲） | 打长剑重装·重甲（我方中甲） |");
        sb.AppendLine("|------|------|------:|------:|------:|");
        for (int i = 0; i < weapons.Count; i++)
        {
            var w = weapons[i];
            var (z, m, h) = winRates[i];
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {w.Name} | {TypeOf(w)} | {z:P1} | {m:P1} | {h:P1} |");
        }
        sb.AppendLine();
        sb.AppendLine("> 远程武器在纯数值对决里没有射程/先手优势（那属实时层），故枪械胜率被系统性低估——枪的真实地位比此表更高。");
        sb.AppendLine();

        sb.AppendLine("## 表 8：改护甲前 vs 改护甲后（同一把武器，A/B 对照）");
        sb.AppendLine();
        sb.AppendLine("旧甲值：布衣 锐4/钝2、皮甲 锐12/钝6、板甲 锐34/钝11、腐皮 锐1.5/钝3，且**全部全身覆盖**。");
        sb.AppendLine("新甲值：长袖布衣 6/3、皮夹克 12/6、板甲 50/25、腐皮 3/3，且外套类**覆盖收窄到胸腹双臂**。");
        sb.AppendLine();
        sb.Append("| 武器 | 类型 |");
        foreach (var p in pairs) sb.Append(CultureInfo.InvariantCulture, $" {p.Name}·击杀耗时 旧→新 | {p.Name}·无伤率 旧→新 |");
        sb.AppendLine();
        sb.Append("|------|------|");
        foreach (var _ in pairs) sb.Append("------:|------:|");
        sb.AppendLine();
        for (int i = 0; i < weapons.Count; i++)
        {
            var w = weapons[i];
            sb.Append(CultureInfo.InvariantCulture, $"| {w.Name} | {TypeOf(w)} |");
            for (int j = 0; j < pairs.Count; j++)
            {
                var (ot, nt, ob, nb) = ab[i, j];
                string t = string.Create(CultureInfo.InvariantCulture,
                    $" {Sec(ot)}→{Sec(nt)}{Delta(ot, nt)} |");
                string b = string.Create(CultureInfo.InvariantCulture,
                    $" {ob:P0}→{nb:P0} |");
                sb.Append(t).Append(b);
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 表 9：武器面板（现值，供对照）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 类型 | 伤害区间 | 穿透 | 出手间隔(秒) |");
        sb.AppendLine("|------|------|----------|-----:|-------------:|");
        foreach (var w in weapons)
        {
            string dmg = string.Create(CultureInfo.InvariantCulture, $"{w.DamageMin:0.#}~{w.DamageMax:0.#}");
            if (w.BurstCount > 1) dmg += string.Create(CultureInfo.InvariantCulture, $" ×{w.BurstCount}发");
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {w.Name} | {TypeOf(w)} | {dmg} | {w.Penetration:P0} | {w.AttackInterval:0.#} |");
        }
        sb.AppendLine();
    }

    // ---- what-if 敏感度扫描：给"该抬多少"找幅度区间（只在工具内构造变体武器，不改 WeaponTable）----

    /// <summary>按倍率/覆盖值派生一把变体武器（伤害区间整体缩放；穿透/间隔可覆盖）。</summary>
    private static Weapon Variant(Weapon w, double dmgMul = 1.0, double? pen = null, double? interval = null) => new()
    {
        Name = w.Name,
        DamageMin = w.DamageMin * dmgMul,
        DamageMax = w.DamageMax * dmgMul,
        Penetration = pen ?? w.Penetration,
        DamageType = w.DamageType,
        TwoHanded = w.TwoHanded,
        CanDualWield = w.CanDualWield,
        IsRanged = w.IsRanged,
        BurstCount = w.BurstCount,
        BurstInterval = w.BurstInterval,
        AttackInterval = interval ?? w.AttackInterval,
    };

    /// <summary>扫描一把变体武器的四个关键读数。</summary>
    private static (double VsZombie, double VsMid, double HeavyTtk, double HeavyBlocked, double Dps) Probe(
        Weapon v, IReadOnlyList<BodyPart> parts)
    {
        var zombie = Fighter("丧尸", WeaponTable.ZombieClaw(), ArmorTable.ZombieHide().ToArray());
        double z = Duel(Fighter("我方", v, StarterKit()), zombie, TtkSeeds).WinRate;
        double m = Duel(Fighter("我方", v, MidKit()),
            Fighter("长剑手", WeaponTable.Longsword(), MidKit()), TtkSeeds).WinRate;
        var heavy = Measure(v, "重甲", HeavyKit(), parts);
        return (z, m, heavy.Ttk, heavy.BlockedArmored, heavy.Dps);
    }

    private static void RenderWhatIf(StringBuilder sb, IReadOnlyList<BodyPart> parts)
    {
        sb.AppendLine("## 表 10：what-if 敏感度扫描（找调整幅度区间）");
        sb.AppendLine();
        sb.AppendLine("每行 = 把该武器的伤害/穿透改成这个值之后重跑。「打丧尸胜率」是最硬的锚（匕首现值 90%）。");
        sb.AppendLine();

        var rows = new List<(string Group, string Label, Weapon V)>();

        // 钝器抬伤 / 抬穿透
        foreach (double mul in new[] { 1.0, 1.3, 1.5, 1.8, 2.2 })
        {
            rows.Add(("棍棒", string.Create(CultureInfo.InvariantCulture,
                $"伤害 ×{mul:0.0}（{WeaponTable.Club().DamageMin * mul:0.#}~{WeaponTable.Club().DamageMax * mul:0.#}）"),
                Variant(WeaponTable.Club(), dmgMul: mul)));
        }
        foreach (double p in new[] { 0.10, 0.15 })
        {
            rows.Add(("棍棒", string.Create(CultureInfo.InvariantCulture, $"伤害 ×1.5 + 穿透 {p:P0}"),
                Variant(WeaponTable.Club(), dmgMul: 1.5, pen: p)));
        }

        foreach (double mul in new[] { 1.0, 1.3, 1.5, 1.8 })
        {
            rows.Add(("尖头锤", string.Create(CultureInfo.InvariantCulture,
                $"伤害 ×{mul:0.0}（{WeaponTable.SpikeHammer().DamageMin * mul:0.#}~{WeaponTable.SpikeHammer().DamageMax * mul:0.#}）"),
                Variant(WeaponTable.SpikeHammer(), dmgMul: mul)));
        }
        rows.Add(("尖头锤", "伤害 ×1.5 + 穿透 12%", Variant(WeaponTable.SpikeHammer(), dmgMul: 1.5, pen: 0.12)));

        // 匕首抬穿透（伤害不动，守住打丧尸 90% 锚）
        foreach (double p in new[] { 0.09, 0.18, 0.25, 0.35 })
        {
            rows.Add(("匕首", string.Create(CultureInfo.InvariantCulture, $"穿透 {p:P0}（伤害不动）"),
                Variant(WeaponTable.Dagger(), pen: p)));
        }
        rows.Add(("匕首", "穿透 25% + 伤害 ×1.2（5~12）", Variant(WeaponTable.Dagger(), dmgMul: 1.2, pen: 0.25)));

        // 手枪抬穿透
        foreach (double p in new[] { 0.15, 0.25, 0.35 })
        {
            rows.Add(("手枪", string.Create(CultureInfo.InvariantCulture, $"穿透 {p:P0}（伤害不动）"),
                Variant(WeaponTable.Pistol(), pen: p)));
        }

        // 冲锋枪降 DPS。冷却 1.8→2.6 已于 [批次18补] 落地，故「现值」= 2.6s；
        // 1.8s 作为回溯对照保留（原始诊断锚，说明这次削弱削掉了多少）。
        rows.Add(("冲锋枪", "冷却 1.8s（改前原值·回溯对照）", Variant(WeaponTable.Smg(), interval: 1.8)));
        rows.Add(("冲锋枪", "现值（伤害 10~18 ×3发 / 冷却 2.6s）", WeaponTable.Smg()));
        rows.Add(("冲锋枪", "冷却 3.2s", Variant(WeaponTable.Smg(), interval: 3.2)));
        rows.Add(("冲锋枪", "伤害 ×0.7（7~12.6）", Variant(WeaponTable.Smg(), dmgMul: 0.7)));
        rows.Add(("冲锋枪", "伤害 ×0.7 + 冷却 3.2s", Variant(WeaponTable.Smg(), dmgMul: 0.7, interval: 3.2)));

        var results = new (double VsZombie, double VsMid, double HeavyTtk, double HeavyBlocked, double Dps)[rows.Count];
        Parallel.For(0, rows.Count, i => results[i] = Probe(rows[i].V, parts));

        sb.AppendLine("| 武器 | 改成 | 打重甲每秒伤害 | 打丧尸胜率 | 打长剑手·中甲胜率 | 重甲击杀耗时 | 重甲无伤率 |");
        sb.AppendLine("|------|------|---------------:|-----------:|------------------:|-------------:|-----------:|");
        for (int i = 0; i < rows.Count; i++)
        {
            var (g, label, _) = rows[i];
            var r = results[i];
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {g} | {label} | {r.Dps:F2} | {r.VsZombie:P1} | {r.VsMid:P1} | {Sec(r.HeavyTtk)} | {r.HeavyBlocked:P1} |");
        }
        sb.AppendLine();
        sb.AppendLine("> 参照锚（现值）：匕首 打丧尸 90.2%、长剑 92.8%、重剑 96.0%；**打重甲每秒伤害** 长剑 2.86、重剑 3.87、破甲锤 3.31。");
        sb.AppendLine();
    }

    private static string TypeOf(Weapon w) =>
        (w.DamageType == DamageType.Sharp ? "锐" : "钝") + (w.IsRanged ? "·远程" : "·近战");

    private static string Sec(double t) =>
        double.IsNaN(t) ? "打不死" : string.Create(CultureInfo.InvariantCulture, $"{t:F0}s");

    /// <summary>击杀耗时倍数（新/旧）：>1 = 护甲改动后更难杀。</summary>
    private static string Delta(double oldT, double newT)
    {
        if (double.IsNaN(oldT) || double.IsNaN(newT) || oldT <= 0) return "";
        return string.Create(CultureInfo.InvariantCulture, $"（×{newT / oldT:F2}）");
    }
}
