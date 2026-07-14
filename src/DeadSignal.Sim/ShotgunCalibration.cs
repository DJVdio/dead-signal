using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 自制霰弹枪（多弹丸）校准。验证用户预期的**涌现效果**是否真的出现：
/// ① 布甲能挡下相当一部分弹丸、板甲几乎全挡（单颗低伤 + 穿透仅 10% → 挡下门槛 = 护甲值×0.9/2）；
/// ② 对无甲/薄衣目标（丧尸：只穿布衣、头/手/脚裸露）极强 → 它是**清丧尸**的武器；
/// ③ 对披甲的劫掠者极差 → **不是**打劫掠者的武器（有意为之，别当平衡问题修）；
/// ④ 贴脸毁灭性、拉开距离只剩零星几颗命中（锥形扩散 + 伤害衰减）。
///
/// 跑法：<c>dotnet run --project src/DeadSignal.Sim shotgun [输出路径]</c>
/// </summary>
public static class ShotgunCalibration
{
    private const int Seeds = 4000;
    private const int Samples = 40000;

    /// <summary>目标碰撞半径（对齐 Godot <c>Actor.Radius</c>=12px）：几何命中估算用的"靶宽"。</summary>
    private const double TargetRadius = 12.0;

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 自制霰弹枪校准（多弹丸 · 8 颗单独计算）");
        sb.AppendLine();
        Weapon sg = WeaponTable.ImprovisedShotgun();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"武器：单颗弹丸 {sg.DamageMin}~{sg.DamageMax} × {sg.PelletCount} 颗　穿透 {sg.Penetration:P0}　" +
            $"冷却 {sg.AttackInterval}s　射程 {sg.MaxRange}　满伤段 {sg.FalloffStart}　末端衰减 {sg.FalloffFloor:P0}　" +
            $"扩散 ±{sg.BaseSpreadDegrees}°");
        sb.AppendLine();

        PelletBlockTable(sb, sg);
        DuelTable(sb, sg);
        HordeTable(sb, sg);
        DistanceTable(sb, sg);
        Sweep(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"已写出 {outPath}");
    }

    // ---- ① 弹丸级：挡下率 / 每次齐射进肉弹丸数（贴脸满伤口径）----

    private static void PelletBlockTable(StringBuilder sb, Weapon sg)
    {
        sb.AppendLine("## ① 弹丸挡下率（每次齐射 8 颗，逐颗独立过护甲三段判定）");
        sb.AppendLine();
        sb.AppendLine("挡下门槛 = 护甲值 × (1−穿透) / 2 —— 穿透仅 10% 故门槛几乎不被削。单颗弹丸伤害低 ⇒ 甲越厚挡得越多。");
        sb.AppendLine();
        sb.AppendLine("| 目标 | 挡下门槛(锐) | 挡下率 | 平均进肉弹丸数/齐射 | 平均总伤/齐射 |");
        sb.AppendLine("|---|---|---|---|---|");

        (string name, IReadOnlyList<ArmorLayer> armor)[] targets =
        {
            ("无甲（裸）", Array.Empty<ArmorLayer>()),
            ("丧尸·腐皮（无衣）", ArmorTable.ZombieHide()),
            ("丧尸·寻常打扮（布衣+长裤+腐皮）", ZombieOutfit.ArmorOf("寻常打扮")),
            ("长袖布衣（薄衣）", new[] { ArmorTable.LongSleeveShirt() }),
            ("皮夹克+布衣（劫掠者中甲）", ArmorTable.SurvivorArmor()),
            ("板甲+粗布外套+布衣（重甲）", new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
        };

        foreach ((string name, IReadOnlyList<ArmorLayer> armor) in targets)
        {
            var rng = new SystemRandomSource(20260713);
            var resolver = new CombatResolver(rng);
            var body = HumanBody.NewBody();
            var hit = new VolumeWeightedHitSelector(rng);
            var alive = body.Parts.Values.ToList();

            long blocked = 0, landed = 0, pellets = 0;
            double damage = 0;
            for (int i = 0; i < Samples; i++)
            {
                VolleyResult v = resolver.ResolveVolley(sg, armor, () => hit.Select(alive));
                blocked += v.BlockedCount;
                landed += v.LandedCount;
                pellets += v.Pellets.Count;
                damage += v.TotalDamage;
            }

            // 门槛按最外层锐防算（部位未覆盖处=裸，实测挡下率是全身命中分布下的平均，故低于门槛的理论值）。
            double outerSharp = armor.Count > 0 ? armor[0].SharpDefense : 0;
            double gate = outerSharp * (1 - sg.Penetration) / 2;

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {gate:0.0} | {(double)blocked / pellets:P1} | " +
                $"{(double)landed / Samples:0.00} / 8 | {damage / Samples:0.0} |");
        }

        sb.AppendLine();
    }

    // ---- ② 对决胜率：清丧尸 vs 打披甲劫掠者（与既有武器横向对照）----

    private static void DuelTable(StringBuilder sb, Weapon sg)
    {
        sb.AppendLine("## ② 对决胜率（1v1 到死，贴脸口径=8 颗全部飞到身上）");
        sb.AppendLine();
        sb.AppendLine("对照组取既有武器。**关键读数：霰弹枪打丧尸名列前茅，打披甲劫掠者垫底**——这正是设计意图。");
        sb.AppendLine();
        sb.AppendLine("| 武器 | vs 丧尸（穿衣） | vs 劫掠者·中甲（皮夹克+布衣，持长剑） | vs 劫掠者·重甲（板甲，持长剑） |");
        sb.AppendLine("|---|---|---|---|");

        Weapon[] arsenal =
        {
            sg, WeaponTable.ImprovisedHuntingGun(), WeaponTable.Pistol(), WeaponTable.Rifle(),
            WeaponTable.Longsword(), WeaponTable.Warhammer(), WeaponTable.Dagger(),
        };

        foreach (Weapon w in arsenal)
        {
            double vsZombie = WinRate(w, ZombieDef(), Seeds);
            double vsMid = WinRate(w, RaiderDef("劫掠者·中甲", ArmorTable.SurvivorArmor()), Seeds);
            double vsHeavy = WinRate(w, RaiderDef("劫掠者·重甲",
                new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }), Seeds);

            string mark = w.Name == sg.Name ? "**" : "";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {mark}{w.Name}{mark} | {vsZombie:P1} | {vsMid:P1} | {vsHeavy:P1} |");
        }

        sb.AppendLine();
    }

    private static DuelFighter ZombieDef() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        BodyFactory = HumanBody.NewZombieBody, // 失血 1/3
        ArmorFactory = ZombieOutfit.RollArmor, // 每场现抽一套生前装束（含腐皮）
    };

    private static DuelFighter RaiderDef(string name, IReadOnlyList<ArmorLayer> armor) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
        Armor = armor,
        Grip = GripMode.TwoHanded,
    };

    private static double WinRate(Weapon w, DuelFighter enemy, int seeds)
    {
        int wins = 0;
        for (int s = 0; s < seeds; s++)
        {
            var rng = new SystemRandomSource(20260713 + s * 7919);
            var player = new DuelFighter
            {
                Name = "玩家",
                Weapons = new[] { new WeaponMount { Weapon = w } },
                Armor = ArmorTable.SurvivorArmor(),
                Grip = w.TwoHanded ? GripMode.TwoHanded : GripMode.OneHanded,
            };

            DuelResult r = new DuelEngine(rng).Run(player, enemy);
            if (r.Winner == "玩家")
            {
                wins++;
            }
        }

        return (double)wins / seeds;
    }

    // ---- ②b 群战：霰弹枪的真正主场（清丧尸 = 以一敌多）----

    /// <summary>
    /// 1 人 vs N 只丧尸（<see cref="Arena"/> 团队战）。1v1 对决量不出霰弹枪的价值——它的定位是<b>清群</b>：
    /// 8 颗弹丸把一次射击的期望伤害堆到 24（贴脸），足以一枪撂倒一只无甲丧尸，这在越多敌人的场合越值钱。
    /// 注意 Arena 是无空间模型（8 颗全落同一目标）；真实的"一枪扫到多只"由 Godot 侧 N 枚独立弹丸给出，
    /// 故这里的数字是霰弹枪清群能力的<b>下界</b>。
    /// </summary>
    private static void HordeTable(StringBuilder sb, Weapon sg)
    {
        sb.AppendLine("## ②b 群战：1 人 vs N 只丧尸（霰弹枪的定位主场）");
        sb.AppendLine();
        sb.AppendLine("Arena 是**无空间**模型：8 颗弹丸全落在同一只丧尸身上。真实游戏里锥形散射会让一枪同时扫到多只"
            + "（Godot 侧 8 枚独立弹丸各自碰撞）——故下表是霰弹枪清群能力的**下界**。");
        sb.AppendLine();
        sb.AppendLine("| 武器 | vs 2 只丧尸 | vs 3 只丧尸 | vs 4 只丧尸 |");
        sb.AppendLine("|---|---|---|---|");

        Weapon[] arsenal =
        {
            sg, WeaponTable.ImprovisedHuntingGun(), WeaponTable.Pistol(),
            WeaponTable.Longsword(), WeaponTable.Club(), WeaponTable.Dagger(),
        };

        foreach (Weapon w in arsenal)
        {
            string mark = w.Name == sg.Name ? "**" : "";
            string row = $"| {mark}{w.Name}{mark} |";
            foreach (int n in new[] { 2, 3, 4 })
            {
                var player = new[]
                {
                    new DuelFighter
                    {
                        Name = "玩家",
                        Weapons = new[] { new WeaponMount { Weapon = w } },
                        Armor = ArmorTable.SurvivorArmor(),
                        Grip = w.TwoHanded ? GripMode.TwoHanded : GripMode.OneHanded,
                    },
                };
                var horde = Enumerable.Range(0, n).Select(_ => ZombieDef()).ToList();
                Arena.ArenaStat stat = Arena.Run(player, horde, 1200);
                row += $" {stat.TeamAWinRate:P1} |";
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"{row}");
        }

        sb.AppendLine();
        sb.AppendLine("> ⚠ **这些数字是下界，而且低估得厉害**：Arena 把 8 颗全砸在同一只身上——一只无甲丧尸只要 ~16 伤就倒，"
            + "打进 24 伤是纯浪费（overkill）。霰弹枪真正的清群价值是**一枪扫到多只**，见下表（空间几何量化）。");
        sb.AppendLine();

        SpreadHitTable(sb, sg);
    }

    /// <summary>
    /// 一枪能扫到几只丧尸（<b>空间几何</b>量化）：N 只丧尸并排挤在射线前方，8 颗弹丸各自在 ±spread 锥内独立采样方向，
    /// 落点 = d·tan(θ)，落在谁的身宽区间里就打谁。这是 Godot 侧 8 枚独立 Projectile 各自碰撞的纯几何等价物，
    /// 也是"霰弹枪是清丧尸的武器"这句话唯一能被量化的地方（引擎 1v1 / Arena 无空间模型都测不出）。
    /// </summary>
    private static void SpreadHitTable(StringBuilder sb, Weapon sg)
    {
        sb.AppendLine("### 一枪扫到几只（锥形散射的空间几何）");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"3 只丧尸并排（间距 = 身宽 {2 * TargetRadius}px），8 颗弹丸各自在 ±{sg.BaseSpreadDegrees}° 锥内独立采样落点。");
        sb.AppendLine();
        sb.AppendLine("| 距离 | 平均命中丧尸数 | 平均命中弹丸数 | ≥2 只被打中的概率 |");
        sb.AppendLine("|---|---|---|---|");

        var rng = new SystemRandomSource(20260713);
        const int Trials = 20000;
        double bodyW = 2 * TargetRadius;

        foreach (double d in new double[] { 20, 30, 45, 60 })
        {
            double sumZombies = 0, sumPellets = 0;
            int multi = 0;

            for (int t = 0; t < Trials; t++)
            {
                var struck = new HashSet<int>();
                int landed = 0;
                for (int p = 0; p < sg.PelletCount; p++)
                {
                    double deg = Ballistics.SampleDeflectionDegrees(sg.BaseSpreadDegrees, rng);
                    double x = d * Math.Tan(deg * Math.PI / 180.0);   // 落点横向偏移
                    // 3 只并排，中心 x = −bodyW, 0, +bodyW；各占 [中心−半宽, 中心+半宽]。
                    for (int z = -1; z <= 1; z++)
                    {
                        if (Math.Abs(x - z * bodyW) <= TargetRadius)
                        {
                            struck.Add(z);
                            landed++;
                            break;
                        }
                    }
                }

                sumZombies += struck.Count;
                sumPellets += landed;
                if (struck.Count >= 2)
                {
                    multi++;
                }
            }

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {d}px | {sumZombies / Trials:0.00} / 3 | {sumPellets / Trials:0.0} / 8 | {(double)multi / Trials:P1} |");
        }

        sb.AppendLine();
        sb.AppendLine("> **散射自己长出了一条距离曲线**（不是设计出来的，是 8 颗独立采样 + 锥角的自然结果）：");
        sb.AppendLine("> - **贴脸（≤30px）**：锥还没铺开（散布宽 13~19px ＜ 身宽 24px）→ 8 颗**全砸在同一只身上**，"
            + "24 点伤害一枪撂倒——毁灭性**单体**，不是清群。");
        sb.AppendLine("> - **中距（45~60px）**：弹丸铺开到能盖住 2~3 只，77~98% 的概率一枪同时咬到两只以上"
            + "——这才是**清群**档，代价是每只只挨 2~4 颗、且吃距离衰减。");
        sb.AppendLine("> - **远距（>90px）**：射程外，开不了火。");
        sb.AppendLine(">");
        sb.AppendLine("> 玩家因此有一个真实的**站位决策**：想秒掉眼前这只就贴上去，想同时打断三只的逼近就退半步。");
        sb.AppendLine();
    }

    // ---- ④ 参数 sweep：找"清丧尸强 / 打披甲弱"这条定位真正成立的数值 ----

    /// <summary>
    /// 定位校准的张力：8 颗弹丸的**总量**会补偿挡下率——即便板甲挡下 69% 的弹丸，剩下 2.5 颗仍能凑出可观伤害，
    /// 使它对重甲的胜率反而高过长剑。要坐实"对披甲极差"，得压低单颗上限（让更多弹丸落到挡下门槛之下）
    /// 并放慢冷却（土制单管装填慢）。本 sweep 扫单颗伤害区间 × 冷却，看哪一组同时满足：
    /// vs 丧尸高（清群强） ∧ vs 重甲显著低于长剑 24.5%（打披甲差）。
    /// </summary>
    private static void Sweep(StringBuilder sb)
    {
        sb.AppendLine("## ④ 定位校准 sweep（单颗伤害 × 冷却）");
        sb.AppendLine();
        sb.AppendLine("目标：vs 丧尸尽量高（清群），vs 重甲**显著低于长剑 24.5%**（打披甲差=有意的短板）。");
        sb.AppendLine();
        sb.AppendLine("| 单颗伤害 | 冷却 | 齐射期望(无甲) | vs 丧尸 | vs 中甲 | vs 重甲 | 定位达标? |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        (double min, double max)[] ranges = { (1, 5), (1, 4), (1, 3), (1, 2.5) };
        double[] cooldowns = { 4.0, 4.5, 5.0 };

        foreach ((double min, double max) in ranges)
        {
            foreach (double cd in cooldowns)
            {
                Weapon w = Variant(min, max, cd);
                double vsZombie = WinRate(w, ZombieDef(), 1500);
                double vsMid = WinRate(w, RaiderDef("中甲", ArmorTable.SurvivorArmor()), 1500);
                double vsHeavy = WinRate(w, RaiderDef("重甲",
                    new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }), 1500);

                // 达标：清丧尸仍强（≥88%）且对重甲明显弱于长剑（<20%）。
                string ok = vsZombie >= 0.88 && vsHeavy < 0.20 ? "**✓**" : "";
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {min}~{max} | {cd}s | {(min + max) / 2 * 8:0.0} | {vsZombie:P1} | {vsMid:P1} | {vsHeavy:P1} | {ok} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("> 对照：长剑 vs 重甲 24.5%、破甲锤 38.6%、匕首 12.6%、自制猎枪 19.0%。");
        sb.AppendLine();
    }

    private static Weapon Variant(double min, double max, double cooldown)
    {
        Weapon b = WeaponTable.ImprovisedShotgun();
        return new Weapon
        {
            Name = b.Name, DamageMin = min, DamageMax = max, PelletCount = b.PelletCount,
            Penetration = b.Penetration, DamageType = b.DamageType, TwoHanded = b.TwoHanded,
            IsRanged = b.IsRanged, BaseSpreadDegrees = b.BaseSpreadDegrees, AttackInterval = cooldown,
            MaxRange = b.MaxRange, FalloffStart = b.FalloffStart, FalloffFloor = b.FalloffFloor,
        };
    }

    // ---- ③ 距离维度：锥形扩散 → 命中弹丸数；距离衰减 → 每颗伤害 ----

    private static void DistanceTable(StringBuilder sb, Weapon sg)
    {
        sb.AppendLine("## ③ 距离：贴脸毁灭性 vs 拉开只剩零星几颗");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"弹丸命中数按**几何**估算（引擎不管空间，实弹道在 Godot 层）：±{sg.BaseSpreadDegrees}° 锥在距离 d 处铺开的" +
            $"横向散布宽 ≈ 2·d·tan({sg.BaseSpreadDegrees}°)，靶宽 = 2×{TargetRadius}px（对齐 Actor.Radius）；" +
            $"命中比 = min(1, 靶宽/散布宽)。伤害衰减走 Ballistics.RangedDamageFactor（引擎纯函数）。");
        sb.AppendLine();
        sb.AppendLine("| 距离 | 散布宽(px) | 期望命中弹丸数 | 距离衰减系数 | 期望到肉总伤（无甲） |");
        sb.AppendLine("|---|---|---|---|---|");

        double[] distances = { 15, 25, 40, 60, 90, 110, 130 };
        foreach (double d in distances)
        {
            if (!Ballistics.InRange(d, sg))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {d} | — | **射程外，不可开火** | 0 | 0 |");
                continue;
            }

            double spreadWidth = 2 * d * Math.Tan(sg.BaseSpreadDegrees * Math.PI / 180.0);
            double targetWidth = 2 * TargetRadius;
            double hitRatio = Math.Min(1.0, targetWidth / spreadWidth);
            double expectedPellets = hitRatio * sg.PelletCount;
            double factor = Ballistics.RangedDamageFactor(d, sg);
            double perPellet = (sg.DamageMin + sg.DamageMax) / 2.0 * factor;

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {d} | {spreadWidth:0.0} | {expectedPellets:0.0} / 8 | {factor:P0} | {expectedPellets * perPellet:0.0} |");
        }

        sb.AppendLine();
        sb.AppendLine("> 贴脸（≤25px）8 颗全中且满伤；到 90px 只剩 1~2 颗且伤害掉到地板价；>130px 射程外。");
        sb.AppendLine();
    }
}
