using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 弓弩体系校准：验证 8 把弓弩 × 4 种箭的 **32 个组合**是不是真的各有各的活，而不是一堆换皮。
/// <para>
/// 要证的三件事：
/// ① <b>组合修正真的起作用</b>——同一把弓换一种箭，胜率有可观的位移（不是 1~2 个百分点的噪声）。
/// ② <b>重头箭是破甲专精</b>——打披甲目标显著优于自制箭，打无甲丧尸则不划算（射程/攻速的代价白付了）。
/// ③ <b>8 把弓弩生态位不重叠</b>——各自在某一列上有不可替代的强项。
/// </para>
/// <para>
/// 跑法：<c>dotnet run --project src/DeadSignal.Sim archery [输出路径]</c>
/// </para>
/// <para>
/// ⚠ <b>Duel/Arena 是无空间模型</b>：不跑射程、不跑散布角、不跑箭矢消耗与回收。故长弓的"射程之王"、
/// 竞技复合弓的"精度之王"在这里**量不出来**（它们的价值在 Godot 空间层兑现）。本报告只量得到
/// **伤害 / 破甲 / 攻速**这三条轴——读表时别把"射程列缺席"当成"射程没用"。
/// </para>
/// </summary>
public static class ArcheryCalibration
{
    private const int Seeds = 3000;

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 弓弩体系校准（8 把弓弩 × 4 种箭 = 32 个组合）");
        sb.AppendLine();
        sb.AppendLine("**组合修正**：最终属性 = 弓/弩的基础属性 ⊗ 箭的乘法修正（`Archery.Combine`）。");
        sb.AppendLine("下表每一格都是**一个具体组合**跑 " + Seeds.ToString("N0") + " 场 1v1 到死的胜率。");
        sb.AppendLine();
        sb.AppendLine("> ⚠ Duel 是**无空间**模型：射程与散布角量不出来。长弓的射程、竞技复合弓的精度，");
        sb.AppendLine("> 其价值在 Godot 空间层兑现，本报告只量得到 **伤害 / 破甲 / 攻速** 三条轴。");
        sb.AppendLine();

        BaseTable(sb);
        ComboMatrix(sb, "vs 丧尸（穿衣，含腐皮）", ZombieDef());
        ComboMatrix(sb, "vs 劫掠者·中甲（皮夹克+布衣，持长剑）",
            RaiderDef("劫掠者·中甲", ArmorTable.SurvivorArmor()));
        ComboMatrix(sb, "vs 劫掠者·重甲（板甲+粗布外套+衬衣，持长剑）",
            RaiderDef("劫掠者·重甲", new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }));
        HeavyArrowVerdict(sb);
        GunBaseline(sb);
        CraftableRangedLines(sb);
        LogisticsTable(sb);
        ArcheryBookTable(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"已写出 {outPath}");
    }

    // ---- ① 基础属性表（搭自制箭＝基线时的值）----

    private static void BaseTable(StringBuilder sb)
    {
        sb.AppendLine("## ① 8 把弓弩的基础属性（＝搭「自制箭」时的值）");
        sb.AppendLine();
        sb.AppendLine("| 弓弩 | 伤害 | 穿透 | 冷却 | 散布角 | 射程 | 裸DPS | 可制作 | 生态位 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

        (string name, string craft, string niche)[] meta =
        {
            ("短弓", "✅", "入门/最便宜/可制作里出手最快"),
            ("反曲弓", "✅", "均衡基准"),
            ("长弓", "✅", "**射程之王**"),
            ("竞技复合弓", "❌搜刮", "**精度之王**"),
            ("狩猎弓", "❌搜刮", "**全弓弩出手最快**（1.6s）/伤害偏低"),
            ("单手轻弩", "✅", "**唯一单手/可双持**"),
            ("双手重弩", "✅", "**可制作里最慢**/可制作里破甲最高（全表最慢已让位复合弩 6.2s）"),
            ("复合弩", "❌搜刮", "**破甲之王**"),
        };

        foreach (Weapon w in WeaponTable.ArcheryArsenal())
        {
            (string _, string craft, string niche) = meta.First(m => m.name == w.Name);
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {w.Name} | {w.DamageMin:0}~{w.DamageMax:0} | {w.Penetration:P0} | {w.AttackInterval}s | " +
                $"{w.BaseSpreadDegrees}° | {w.MaxRange:0} | {Dps(w):0.00} | {craft} | {niche} |");
        }

        sb.AppendLine();
        sb.AppendLine("### 4 种箭的修正倍率");
        sb.AppendLine();
        sb.AppendLine("| 箭 | 伤害× | 破甲× | 射程× | 冷却×(>1更慢) | 散布×(>1更不准) | 可制作 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (ArrowDef a in ArrowTable.All)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {a.Name} | {a.DamageMult:0.00} | {a.PenetrationMult:0.00} | {a.RangeMult:0.00} | " +
                $"{a.CooldownMult:0.00} | {a.SpreadMult:0.00} | {(a.Craftable ? "✅" : "❌ 只能搜刮")} |");
        }

        sb.AppendLine();
    }

    // ---- ② 32 组合矩阵 ----

    private static void ComboMatrix(StringBuilder sb, string title, DuelFighter enemy)
    {
        sb.AppendLine($"## ② {title}");
        sb.AppendLine();
        sb.Append("| 弓弩 |");
        foreach (ArrowDef a in ArrowTable.All)
        {
            sb.Append($" {a.Name} |");
        }

        sb.AppendLine(" 木箭→碳纤维 位移 |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (Weapon bow in WeaponTable.ArcheryArsenal())
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {bow.Name} |");

            double first = 0, last = 0;
            for (int i = 0; i < ArrowTable.All.Count; i++)
            {
                double wr = WinRate(Archery.Combine(bow, ArrowTable.All[i]), enemy);
                if (i == 0) first = wr;
                if (i == ArrowTable.All.Count - 1) last = wr;
                sb.Append(CultureInfo.InvariantCulture, $" {wr:P1} |");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $" **{(last - first) * 100:+0.0;-0.0}pp** |");
        }

        sb.AppendLine();
    }

    // ---- ③ 重头箭的专精验证 ----

    private static void HeavyArrowVerdict(StringBuilder sb)
    {
        sb.AppendLine("## ③ 重头箭是不是真的「破甲专精」？");
        sb.AppendLine();
        sb.AppendLine("用户原话：「重头箭（破甲能力更高，但射程和攻速有所削弱）」。");
        sb.AppendLine();
        sb.AppendLine("**判据：收益应随甲的厚度单调上升** —— 打无甲丧尸接近持平（穿透在裸肉上是废的，还要付攻速的账），");
        sb.AppendLine("打中甲明显赚，打重甲赚最多。这才叫「破甲专精」，而不是「一支更好的箭」。");
        sb.AppendLine();
        sb.AppendLine("> 🔁 **这张表推翻过一版设计**。初版取 伤害 ×1.20 / 冷却 ×1.25，实测重头箭**在每一列都输给自制箭**");
        sb.AppendLine("> （打重甲 −2~−4.5pp）—— 破甲专精专不起来，成了纯劣化。根因是本仓库的已知结论：");
        sb.AppendLine("> **穿透是低杠杆轴**（能不能击穿甲层主要由伤害骰决定，穿透只压低防御骰上限），");
        sb.AppendLine("> 于是「破甲 ×1.45」几乎买不到胜率，而「冷却 ×1.25」是实打实的 −20% DPS。");
        sb.AppendLine("> 修法是把收益挪到吃得上劲的轴上：伤害 ×1.35、冷却惩罚收窄到 ×1.15。");
        sb.AppendLine("> **用户的三个方向（破甲↑/射程↓/攻速↓）一个没动，动的只是幅度**（数值本就「拟定待调」）。");
        sb.AppendLine("> 它真正的代价落在 Sim 量不到的两处：**射程 −25%**（空间层兑现）与**造价**（吃铁 2，自制箭只要 1）。");
        sb.AppendLine();
        sb.AppendLine("| 弓弩 | 丧尸：重头 − 自制 | 中甲：重头 − 自制 | 重甲：重头 − 自制 |");
        sb.AppendLine("|---|---|---|---|");

        DuelFighter zombie = ZombieDef();
        DuelFighter mid = RaiderDef("中甲", ArmorTable.SurvivorArmor());
        DuelFighter heavy = RaiderDef("重甲",
            new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() });

        foreach (Weapon bow in WeaponTable.ArcheryArsenal())
        {
            Weapon baseline = Archery.Combine(bow, ArrowTable.Handmade());
            Weapon heavyArrow = Archery.Combine(bow, ArrowTable.Heavy());

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {bow.Name} | {Delta(heavyArrow, baseline, zombie)} | {Delta(heavyArrow, baseline, mid)} | " +
                $"{Delta(heavyArrow, baseline, heavy)} |");
        }

        sb.AppendLine();
    }

    private static string Delta(Weapon a, Weapon b, DuelFighter enemy)
    {
        double d = (WinRate(a, enemy) - WinRate(b, enemy)) * 100;
        d += 0.0;   // 抹掉负零：.NET 会把 -0.0 印成 "-+0.0"（先补符号再套正数段格式）
        return string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0}pp", d == 0 ? 0 : d);
    }

    // ---- ④ 与枪的横向对照（弓弩不该是 DPS 武器）----

    private static void GunBaseline(StringBuilder sb)
    {
        sb.AppendLine("## ④ 与枪的横向对照");
        sb.AppendLine();
        sb.AppendLine("弓弩的价值在**潜行**（噪音 50~70 vs 枪 350~700）与**后勤**（箭不吃火药、可回收 25%，读《弓与箭之道》后 50%），");
        sb.AppendLine("**不在输出**。下表证明：连最强的弓弩组合，裸 DPS 也低于步枪。");
        sb.AppendLine();
        sb.AppendLine("| 武器（组合） | 裸DPS | 噪音半径 | vs 丧尸 | vs 中甲 | vs 重甲 |");
        sb.AppendLine("|---|---|---|---|---|---|");

        DuelFighter zombie = ZombieDef();
        DuelFighter mid = RaiderDef("中甲", ArmorTable.SurvivorArmor());
        DuelFighter heavy = RaiderDef("重甲",
            new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() });

        Weapon[] rows =
        {
            Archery.Combine(WeaponTable.HuntingBow(), ArrowTable.Carbon()),        // 最强弓组合
            Archery.Combine(WeaponTable.CompoundCrossbow(), ArrowTable.Heavy()),   // 最强破甲组合
            Archery.Combine(WeaponTable.ShortBow(), ArrowTable.SharpenedStick()),  // 最弱组合
            WeaponTable.Rifle(), WeaponTable.Pistol(), WeaponTable.ImprovisedHuntingGun(),
            WeaponTable.Longsword(),
        };

        foreach (Weapon w in rows)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {w.Name} | {Dps(w):0.00} | {w.NoiseRadius:0} | {WinRate(w, zombie):P1} | " +
                $"{WinRate(w, mid):P1} | {WinRate(w, heavy):P1} |");
        }

        sb.AppendLine();
    }


    // ---- ⑤ 三条【可制作】远程线各有生态位（弓弩 / 自制猎枪 / 自制霰弹枪）----

    private static void CraftableRangedLines(StringBuilder sb)
    {
        sb.AppendLine("## ⑤ 三条**可制作**远程线：各有各的活");
        sb.AppendLine();
        sb.AppendLine("玩家能自己造的远程武器只有三条线。它们必须各干各的——否则造哪个都一样，选择就没意义。");
        sb.AppendLine();
        sb.AppendLine("| 可制作远程 | vs 丧尸 | vs 中甲 | vs 重甲 | 噪音 | 弹药代价 |");
        sb.AppendLine("|---|---|---|---|---|---|");

        DuelFighter zombie = ZombieDef();
        DuelFighter mid = RaiderDef("中甲", ArmorTable.SurvivorArmor());
        DuelFighter heavy = RaiderDef("重甲",
            new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() });

        (Weapon w, string ammo)[] rows =
        {
            (Archery.Combine(WeaponTable.HeavyCrossbow(), ArrowTable.Heavy()), "箭：木料+铁2，**可捡回 25%**"),
            (Archery.Combine(WeaponTable.Longbow(), ArrowTable.Handmade()), "箭：木料+铁1+布，**可捡回 25%**"),
            (Archery.Combine(WeaponTable.ShortBow(), ArrowTable.SharpenedStick()), "箭：木料，**可捡回 25%**"),
            (WeaponTable.ImprovisedHuntingGun(), "子弹：子弹零件（不可捡回）"),
            (WeaponTable.ImprovisedShotgun(), "鹿弹：子弹零件（不可捡回）"),
        };

        foreach ((Weapon w, string ammo) in rows)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {w.Name} | {WinRate(w, zombie):P1} | {WinRate(w, mid):P1} | {WinRate(w, heavy):P1} | " +
                $"{w.NoiseRadius:0} | {ammo} |");
        }

        sb.AppendLine();
        sb.AppendLine("**读法（照实说，别只挑好听的）**：");
        sb.AppendLine();
        // 三个数字**实算**，不写死：武器数值一改（如近战锐器重标定、双手攻速加成退役），写死的数字立刻变成假话。
        Weapon shotgun = WeaponTable.ImprovisedShotgun();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"⚠️ **自制霰弹枪在这张表的三列里全都最高**（{WinRate(shotgun, zombie):P1} / {WinRate(shotgun, mid):P1} / " +
            $"{WinRate(shotgun, heavy):P1}），把两条弓弩线都压住了。");
        sb.AppendLine("但**这张表量不到它的两个致命短板**，别据此以为它是万能解：");
        sb.AppendLine();
        sb.AppendLine("1. **Duel 是贴脸模型**，而霰弹枪的射程最短、衰减最重、锥形扩散最大——拉开距离只剩零星几颗命中。");
        sb.AppendLine("   它在这张表上的分数拿的是**贴脸口径**（8 颗全中）；真实战斗里那是它最不想待的位置。");
        sb.AppendLine("2. **噪音 550 vs 弓弩 55~70**（十倍差）。它一响，视野外、屏幕外的丧尸全来了。这张表里它单挑赢，");
        sb.AppendLine("   但真实场景是它把自己送进了群战——而弓弩**不招人**。");
        sb.AppendLine();
        sb.AppendLine("所以三条线的真实分工是：");
        sb.AppendLine("**自制霰弹枪** ＝ 贴脸爆发，打得赢就赢在三秒内，代价是把整条街喊过来；");
        sb.AppendLine("**弓弩** ＝ 远、静、养得起（箭捡得回一部分），但**慢**——你能悄悄干掉一个，干不掉一群；");
        sb.AppendLine("**自制猎枪** ＝ 两头不靠，胜在**最省事**（不挑箭、不用管回收、贴脸还能拿枪托砸，而弓弩贴脸只能挨打）。");
        sb.AppendLine();
        sb.AppendLine("弓弩内部：**双手重弩 + 重头箭**（穿透 94%）是可制作里唯一的破甲手段；");
        sb.AppendLine("**长弓** 射得最远；**短弓 + 木箭** 是全表垫底，但它开局第一天就能做出来。");
        sb.AppendLine();
    }

    // ---- ⑥ 后勤：25% 回收率意味着什么 ----

    private static void LogisticsTable(StringBuilder sb)
    {
        sb.AppendLine("## ⑥ 后勤：25% / 50% 回收率意味着什么");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"用户拍板：**箭只有 {Archery.BaseArrowRecoveryRate:P0} 的几率不被损毁；读过《弓与箭之道》则是 " +
            $"{Archery.SkilledArrowRecoveryRate:P0}**。");
        sb.AppendLine();
        sb.AppendLine("这两个数字定义了弓弩的整个身份。下表把它换算成「打一场仗要烧掉多少支箭」：");
        sb.AppendLine();
        sb.AppendLine("| 场景 | 未读《弓与箭之道》(25%) | 读过 (50%) |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine("| 射 4 支，净损耗 | **3 支** | **2 支** |");
        sb.AppendLine("| 每支箭的**期望射击次数** | 1 / 0.75 ≈ **1.33 次** | 1 / 0.50 = **2.00 次** |");
        sb.AppendLine("| 一筒 20 支箭能射 | ≈ **27 箭** | ≈ **40 箭** |");
        sb.AppendLine();
        sb.AppendLine("> **弓弩不是免费远程**——它只是后勤压力小于枪（箭不吃子弹零件，且至少还捡得回一部分）。");
        sb.AppendLine("> 那本书把每支箭的寿命从 1.33 次拉到 2.00 次（**+50%**），于是它是弓弩流的**硬前置**：");
        sb.AppendLine("> 找不到它，你养不起一个弓手。");
        sb.AppendLine();
        sb.AppendLine("> 配套：箭的造价刻意**不低**（除应急木箭外，每支都要吃金属）。若造箭近乎白送，");
        sb.AppendLine("> 「跑回战场把箭捡回来」就不值得玩家冒一次险，回收率这条机制也就白设计了。");
        sb.AppendLine();
    }

    // ---- ⑦ 《弓与箭之道》的三项被动：Sim 只量得到其中一项 ----

    private static void ArcheryBookTable(StringBuilder sb)
    {
        sb.AppendLine("## ⑦ 《弓与箭之道》：弹道速度 +20%（挂起）/ 锥形角 −10% / 攻速 +2% / 箭矢回收 25%→50%");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"用户口径（数值表『书籍』页）：读过这本书的**射手本人**，其弓弩 **散布 ×{BookSpread:0.00}**（锥形角收窄＝更准）、" +
            $"**攻速 ×{BookSpeed:0.00}**（＝出手间隔 ×{Archery.BookCooldownMult:0.0000}）。" +
            $"[T68] 原「射程 +10%」已被用户换成「弹道速度 +20%」（引擎新轴、挂起未落地），故射程加成已中和为 ×{BookRange:0.00}（无效果）。");
        sb.AppendLine("散布/攻速两项与**箭的同轴系数连乘**（乘算不加算）；书不再改射程，故长弓 × 重头箭 × 书 的射程仍是 ×0.75（重头箭一项）。");
        sb.AppendLine();
        sb.AppendLine("> ⚠️ **这张表只量得到攻速那一项**，而且它小得几乎读不出来。Duel 是 **1v1 / 无距离 / 无走位** 的模型，");
        sb.AppendLine("> 既不跑射程也不读散布角（`BaseSpreadDegrees`）——**弹道速度 +20%（挂起）与锥形角 −10% 在这里结构性地量不出来**，");
        sb.AppendLine("> 这是模型的盲区，不是漏改。那两项的价值在 Godot 空间层兑现：弹道更快够得着、锥形采样更收拢。");
        sb.AppendLine("> 换句话说，**这本书的战力有三分之二是这张表照不到的**——别拿下表的 +0.x pp 去判断它值不值得读。");
        sb.AppendLine();
        sb.AppendLine("| 弓弩（搭自制箭） | 出手间隔 未读→读过 | vs 丧尸 | vs 中甲 | vs 重甲 |");
        sb.AppendLine("|---|---|---|---|---|");

        DuelFighter zombie = ZombieDef();
        DuelFighter mid = RaiderDef("中甲", ArmorTable.SurvivorArmor());
        DuelFighter heavy = RaiderDef("重甲",
            new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() });

        foreach (Weapon bow in WeaponTable.ArcheryArsenal())
        {
            Weapon unread = Archery.Combine(bow, ArrowTable.Handmade());
            Weapon read = Archery.Combine(bow, ArrowTable.Handmade(), hasReadArcheryBook: true);

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {bow.Name} | {unread.AttackInterval:0.000}s → {read.AttackInterval:0.000}s | " +
                $"{Delta(read, unread, zombie)} | {Delta(read, unread, mid)} | {Delta(read, unread, heavy)} |");
        }

        sb.AppendLine();
        sb.AppendLine("**读法**：胜率位移只有 +0.2~+1.7pp（攻速 +2% 本就只值这么多，且逐格还叠着蒙特卡洛噪声）。");
        sb.AppendLine("这本书**不是一本战力书**——它的分量压在 Sim 量不到的三处：**回收率 25%→50%**（每支箭的寿命 +50%，");
        sb.AppendLine("弓弩流的硬前置）、**弹道速度 +20%（挂起）**、**精度 +10%**。前者决定你养不养得起弓手，后两者决定你能不能在丧尸够到你之前把它放倒。");
        sb.AppendLine();
    }

    private const double BookRange = Archery.BookRangeMult;
    private const double BookSpread = Archery.BookSpreadMult;
    private const double BookSpeed = Archery.BookAttackSpeedMult;

    // ---- harness ----

    private static double Dps(Weapon w) =>
        (w.DamageMin + w.DamageMax) / 2.0 * Math.Max(1, w.BurstCount) * Math.Max(1, w.PelletCount) / w.AttackInterval;

    private static DuelFighter ZombieDef() => new()
    {
        Name = "丧尸",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
        BodyFactory = HumanBody.NewZombieBody, // 失血 1/3
        ArmorFactory = ZombieOutfit.RollArmor,
    };

    private static DuelFighter RaiderDef(string name, IReadOnlyList<ArmorLayer> armor) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
        Armor = armor,
        Grip = GripMode.TwoHanded,
    };

    private static double WinRate(Weapon w, DuelFighter enemy)
    {
        int wins = 0;
        for (int s = 0; s < Seeds; s++)
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

        return (double)wins / Seeds;
    }
}
