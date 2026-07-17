using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 「丧尸穿生前的破衣服」前后对比校准。
/// <para>
/// 背景：腐皮锐防/钝防均为 3。逐层结算 atk ∈ [伤害下限,上限]、def ∈ [0, 护甲值×(1−穿透)]，atk &lt; def/2 才算挡下
/// ⇒ <b>单层甲有可能挡下一击 ⟺ 护甲值×(1−穿透)/2 &gt; 武器伤害下限</b>。腐皮门槛只有 1.5，低于任何武器的伤害下限
/// ⇒ 光身丧尸对<b>全部武器 0% 阻挡（数学恒零）</b>。用户拒绝抬腐皮（"腐皮本来就是烂肉"），理由是<b>丧尸也会穿衣服</b>。
/// 本校准量化那句话落地后的效果：布类锐防 6 → 门槛 3.0，丧尸开始真的挡得下东西。
/// </para>
/// 五张表：① 逐武器挡下率（光身 vs 穿衣）；② 玩家打丧尸胜率/耗时（光身 vs 穿衣）；
/// ③ **精英丧尸**（authored 具名、穿护甲、不进随机池）有多硬；④ 尸潮吞吐；⑤ 日常着装分布自检。
/// </summary>
public static class ZombieClothCalibration
{
    private const int HitSamples = 200_000; // 挡下率：逐次命中采样
    private const int DuelSeeds = 4000;     // 胜率：逐场对决采样

    /// <summary>被测武器（玩家侧常用近战/远程 + 丧尸爪击自身）。</summary>
    private static (string Name, Weapon W)[] Weapons() => new[]
    {
        ("匕首", WeaponTable.Dagger()),
        ("短剑", WeaponTable.Shortsword()),
        ("长剑", WeaponTable.Longsword()),
        ("棍棒", WeaponTable.Club()),
        ("尖头锤", WeaponTable.SpikeHammer()),
        ("破甲锤", WeaponTable.Warhammer()),
        ("手枪", WeaponTable.Pistol()),
        ("步枪", WeaponTable.Rifle()),
    };

    private static DuelFighter Player(Weapon w) => new()
    {
        Name = "我方",
        Weapons = new[] { new WeaponMount { Weapon = w } },
        // 开局三件套（长袖布衣 + 长裤 + 运动鞋），与其它校准的玩家口径一致。
        Armor = new[] { ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(), ArmorTable.Sneakers() },
    };

    /// <summary>光身丧尸（旧口径：只有腐皮）。</summary>
    private static DuelFighter NakedZombie() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        BodyFactory = HumanBody.NewZombieBody, // 失血 1/3
        Armor = ArmorTable.ZombieHide(),
    };

    /// <summary>穿衣丧尸（新口径：生前的破衣服 + 腐皮，每场现抽）。</summary>
    private static DuelFighter ClothedZombie() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        BodyFactory = HumanBody.NewZombieBody, // 失血 1/3
        ArmorFactory = ZombieOutfit.RollArmor,
    };

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 丧尸穿衣（生前的破衣服）前后对比");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"挡下率样本 {HitSamples:N0} 次命中/格　胜率样本 {DuelSeeds:N0} 场/格　玩家 = 开局三件套（长袖布衣+长裤+运动鞋）");
        sb.AppendLine();
        sb.AppendLine("> **挡下** = 该次命中被护甲完全吃掉（伤害 0）。命中部位按体积加权随机——打到裸露的头/手/脚自然挡不下，");
        sb.AppendLine("> 故穿衣后的挡下率是**全身平均**，不是「打中衣服时的挡下率」。");
        sb.AppendLine();

        BlockRates(sb);
        WinRates(sb);
        Elites(sb);
        Horde(sb);
        Distribution(sb);

        var report = sb.ToString();
        SimReport.Write(outPath, report); // 出处戳 + 落盘（含建目录）
        Console.WriteLine($"已写出 {outPath}");
        Console.WriteLine();
        Console.Write(report);
    }

    /// <summary>① 逐武器挡下率：光身（腐皮）vs 穿衣。</summary>
    private static void BlockRates(StringBuilder sb)
    {
        sb.AppendLine("## ① 丧尸挡下率（全身平均）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 伤害区间 | 光身(仅腐皮) | 穿衣 | 变化 |");
        sb.AppendLine("|---|---|---:|---:|---|");

        foreach (var (name, w) in Weapons())
        {
            double naked = BlockRate(w, rng => ArmorTable.ZombieHide());
            double clothed = BlockRate(w, ZombieOutfit.RollArmor);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {w.DamageMin:0.#}~{w.DamageMax:0.#} | {naked:P1} | {clothed:P1} | {naked:P1} → **{clothed:P1}** |"));
        }

        sb.AppendLine();
        sb.AppendLine("爪击（丧尸打玩家，对照组——玩家穿开局三件套）：");
        Weapon claw = WeaponTable.ZombieClaw();
        double vsPlayer = BlockRate(claw, rng => new[]
        {
            ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(), ArmorTable.Sneakers(),
        });
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 玩家开局三件套挡下爪击（{claw.DamageMin:0.#}~{claw.DamageMax:0.#}）：**{vsPlayer:P1}**"));
        sb.AppendLine();
    }

    /// <summary>
    /// 单格挡下率：反复"选一个部位 → 逐层结算"，统计伤害为 0 的比例。
    /// 每次采样重抽护甲（穿衣丧尸即每只一套），命中部位按体积加权（复用引擎的 <see cref="VolumeWeightedHitSelector"/>）。
    /// </summary>
    private static double BlockRate(Weapon w, Func<IRandomSource, IReadOnlyList<ArmorLayer>> armorOf)
    {
        var rng = new SystemRandomSource(20260713);
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);
        var parts = HumanBody.NewBody().Parts.Values.ToList();

        int blocked = 0;
        for (int i = 0; i < HitSamples; i++)
        {
            IReadOnlyList<ArmorLayer> armor = CombatResolver.OrderOuterToInner(armorOf(rng));
            BodyPart part = hit.Select(parts);
            if (resolver.Resolve(w, armor, part).FinalDamage <= 0)
            {
                blocked++;
            }
        }

        return (double)blocked / HitSamples;
    }

    /// <summary>② 玩家打丧尸：胜率与耗时，光身 vs 穿衣。</summary>
    private static void WinRates(StringBuilder sb)
    {
        sb.AppendLine("## ② 玩家打丧尸（1v1 到死）");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 胜率(光身) | 胜率(穿衣) | 变化 | 耗时s(光身) | 耗时s(穿衣) |");
        sb.AppendLine("|---|---:|---:|---|---:|---:|");

        foreach (var (name, w) in Weapons())
        {
            var naked = Duel(Player(w), NakedZombie());
            var clothed = Duel(Player(w), ClothedZombie());
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {naked.WinRate:P1} | {clothed.WinRate:P1} | {(clothed.WinRate - naked.WinRate) * 100:+0.0;-0.0;0.0}pt | {naked.Duration:F1} | {clothed.Duration:F1} |"));
        }

        sb.AppendLine();
        sb.AppendLine("> 「变化」= 穿衣胜率 − 光身胜率，单位百分点（负数 = 穿衣后丧尸更难打，玩家胜率下降）。");
        sb.AppendLine();
    }

    private static (double WinRate, double Duration) Duel(DuelFighter a, DuelFighter b)
    {
        int aWins = 0;
        double durSum = 0;
        for (int seed = 0; seed < DuelSeeds; seed++)
        {
            var engine = new DuelEngine(new SystemRandomSource(20260713 + seed * 131));
            DuelResult r = engine.Run(a, b);
            durSum += r.DurationSeconds;
            if (r.Winner == a.Name)
            {
                aWins++;
            }
        }

        return ((double)aWins / DuelSeeds, durSum / DuelSeeds);
    }

    /// <summary>
    /// ③ 精英丧尸（authored·具名，**不在随机池里**）：点名穿护甲的高难度丧尸有多硬。
    /// 用 <see cref="ZombieOutfit.Fixed"/> 塞进 <c>DuelFighter.ArmorFactory</c>（确定性、不掷骰）。
    /// </summary>
    private static void Elites(StringBuilder sb)
    {
        sb.AppendLine("## ③ 精英丧尸（authored·具名，不进随机池）");
        sb.AppendLine();
        sb.AppendLine("样板草案两套（**draft，待用户定稿**），用的都是护甲表现有件，未新造装备：");
        foreach (ZombieOutfitPreset p in ZombieOutfit.ElitePresets)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- **{p.Name}**：{string.Join(" + ", p.Clothes().Select(c => c.Name))}（+ 腐皮）"));
        }

        sb.AppendLine();
        sb.AppendLine("| 武器 | 打日常丧尸 | 打军人丧尸 | 打防暴警察丧尸 | 防暴挡下率 |");
        sb.AppendLine("|---|---:|---:|---:|---:|");

        foreach (var (name, w) in Weapons())
        {
            double daily = Duel(Player(w), ClothedZombie()).WinRate;
            double soldier = Duel(Player(w), EliteZombie("军人丧尸")).WinRate;
            double riot = Duel(Player(w), EliteZombie("防暴警察丧尸")).WinRate;
            double riotBlock = BlockRate(w, _ => ZombieOutfit.ArmorOf("防暴警察丧尸"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {daily:P1} | {soldier:P1} | {riot:P1} | {riotBlock:P1} |"));
        }

        sb.AppendLine();
        sb.AppendLine("> 防暴警察丧尸穿**板甲**（锐防 50 → 挡下门槛 25），躯干几乎打不动；能赢是因为**头/脚裸露**");
        sb.AppendLine("> ——护甲表的人形件里没有头盔。**爆头是精英丧尸的唯一软肋**，这是有意的设计对称。");
        sb.AppendLine();
    }

    /// <summary>点名一只 authored 精英丧尸（<see cref="ZombieOutfit.Fixed"/> 忽略随机源）。</summary>
    private static DuelFighter EliteZombie(string outfitName) => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        BodyFactory = HumanBody.NewZombieBody, // 失血 1/3
        ArmorFactory = ZombieOutfit.Fixed(outfitName),
    };

    /// <summary>
    /// ④ 尸潮吞吐：典型 4 人营地 vs 单波 M 丧尸，光身 vs 穿衣。
    /// 两侧用**同一套武器数值**跑，故差值可干净地归因给"穿衣"这一项（不与武器改动混淆）。
    /// 口径同 <see cref="EndgameCalibration"/>：无空间掩体的**下界**。
    /// </summary>
    private static void Horde(StringBuilder sb)
    {
        const int seeds = 2000;
        DuelFighter Melee() => new()
        {
            Name = "近战幸存者",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
            Armor = new[] { ArmorTable.Leather(), ArmorTable.LongSleeveShirt() },
        };
        DuelFighter Ranged() => new()
        {
            Name = "远程幸存者",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Pistol() } },
            Armor = new[] { ArmorTable.LongSleeveShirt() },
        };
        List<DuelFighter> Camp() => new() { Melee(), Ranged(), Melee(), Ranged() };
        List<DuelFighter> Wave(int m, Func<DuelFighter> z)
        {
            var list = new List<DuelFighter>();
            for (int i = 0; i < m; i++) list.Add(z());
            return list;
        }

        sb.AppendLine("## ④ 尸潮吞吐：4 人营地 vs 单波 M（下界，无空间掩体）");
        sb.AppendLine();
        sb.AppendLine("| 单波 M | 清波率(光身) | 清波率(穿衣) | 零阵亡率(光身) | 零阵亡率(穿衣) |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        foreach (int m in new[] { 4, 6, 8, 10 })
        {
            Arena.ArenaStat naked = Arena.Run(Camp(), Wave(m, NakedZombie), seeds);
            Arena.ArenaStat clothed = Arena.Run(Camp(), Wave(m, ClothedZombie), seeds);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {m} | {naked.TeamAWinRate:P0} | {clothed.TeamAWinRate:P0} | {naked.NoLossRate:P0} | {clothed.NoLossRate:P0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("> 两列用**同一套武器数值**，故差值可干净归因给「穿衣」（不与 impl-weapons 的武器改动混淆）。");
        sb.AppendLine();
    }

    /// <summary>④ 装束分布自检：实抽 N 只，看是否吻合预设权重。</summary>
    private static void Distribution(StringBuilder sb)
    {
        sb.AppendLine("## ⑤ 日常着装分布自检（实抽 100,000 只）");
        sb.AppendLine();
        sb.AppendLine("| 生前装束 | 权重 | 实抽占比 | 还剩的衣物 |");
        sb.AppendLine("|---|---:|---:|---|");

        const int n = 100_000;
        var rng = new SystemRandomSource(20260713);
        var count = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            ZombieOutfitPreset p = ZombieOutfit.RollPreset(rng);
            count[p.Name] = count.GetValueOrDefault(p.Name) + 1;
        }

        foreach (ZombieOutfitPreset p in ZombieOutfit.Presets)
        {
            IReadOnlyList<ArmorLayer> clothes = p.Clothes();
            string worn = clothes.Count == 0 ? "（无）" : string.Join(" + ", clothes.Select(c => c.Name));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {p.Name} | {p.Weight:P0} | {(double)count.GetValueOrDefault(p.Name) / n:P1} | {worn} |"));
        }

        double dressed = ZombieOutfit.Presets.Where(p => p.Clothes().Count > 0).Sum(p => p.Weight);
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- **{dressed:P0} 的丧尸至少还穿着一件**；头/手/脚恒裸（与护甲表「三件外套只覆盖胸/腹/双臂」的取舍对称）。"));
        sb.AppendLine("- 布类防护值**不打折**：表值即最终值。「破损」由**部分覆盖**表达（多数丧尸只剩一两件、头手脚全裸），");
        sb.AppendLine("  而非给防御值打折——打折会把刚够着的挡下门槛（布类 3.0）又压回腐皮那种恒零状态，等于白做。");
    }
}
