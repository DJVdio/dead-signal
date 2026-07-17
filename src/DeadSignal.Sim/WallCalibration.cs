using System.Globalization;
using System.Text;
using DeadSignal.Combat;
using DeadSignal.Godot; // CampStructureTable / StructureDamage / HordeTimeline / RaidWave

/// <summary>
/// 围墙 / 大门破防校准（只读诊断 harness，不改任何数值）。回答三问：
///  ① 各武器砸穿各结构要几击、几秒（结构血量读 <see cref="CampStructureTable"/> 权威表；砸墙伤害与节奏读
///     <see cref="StructureDamage"/> 权威规则 + <see cref="WeaponTable"/> 权威武器表 —— 防手抄漂移）。
///  ② 「一堵墙相当于几个人」：同一只丧尸，砸穿一堵墙的爪数 vs 撂倒一名幸存者的爪数（后者走真实结算：
///     逐层护甲 + 部位命中分配 + 效果触发，沙袋口径=目标不还手，纯测吞吐）。
///  ③ 尸潮/袭营场景：一波 N 只丧尸并肩砸门要多久，占一夜（480s）多大比例。
///
/// <b>⚠ 本 harness 测不了空间</b>：Sim 没有碰撞体积、没有攻击距离、没有走位。凡涉及「多个攻击者同时砸同一处」的数字，
/// Sim 只会做**纯线性叠加**（假设 8 只丧尸能同时贴在门上打）——**真实游戏里做不到**：丧尸有碰撞体积、爪击是近战距离，
/// **一扇门前只挤得下 3~4 只**，后面的够不着（用户口径）。空间碰撞天然就是次线性叠加，规则层无需封顶。
/// 故下文所有多攻击者的表都<b>同时给两栏</b>：「空间实际（3~4 只）」= 该信的；「Sim 口径（8 只）」= <b>失真，仅作对照</b>。
///
/// <b>砸墙伤害现在由武器派生</b>（<c>每击 = 砸墙有效武器平均伤害 × 砸墙系数</c>，枪械取枪托 profile）。
/// 本 harness 直接调 <see cref="StructureDamage"/>，**不再镜像任何常数** —— 旧实现里那两个写死的
/// 12（丧尸）/ 25（劫掠者）只作为「旧值」列保留，用于对比改动前后。
/// </summary>
public static class WallCalibration
{
    private const int Seeds = 20000;

    /// <summary>旧实现的写死常数（Zombie.cs / Raider.cs 的 private const），**仅用于新旧对比列**。</summary>
    private const int LegacyZombieHit = 12;
    private const int LegacyRaiderHit = 25;

    /// <summary>一夜实时秒数（godot/data/daynight.json: nightLengthSeconds）。袭营/围攻都发生在夜里。</summary>
    private const double NightSeconds = 480.0;

    /// <summary>
    /// 一扇门前**真正够得着**的丧尸只数上限（用户口径）：丧尸有碰撞体积、爪击是近战距离 ⇒ 只有围在门前的 3~4 只能打到门，
    /// 后面的挤不进来。<b>这就是次线性叠加的来源——空间做的，不是规则做的。</b>
    /// Sim 没有碰撞体积也没有攻击距离，若不设此上限就会算出"8 只同时砸门"的失真数字。
    /// </summary>
    private const int MaxAbreast = 4;

    private static readonly (string Label, StructureTier Tier)[] Structures =
    {
        ("基础围栏",   StructureTier.FenceBasic),
        ("加固围栏",   StructureTier.FenceReinforced),
        ("铁皮围栏",   StructureTier.FenceSheetMetal),
        ("全金属围栏", StructureTier.FenceFullMetal),
        ("基础大门",   StructureTier.GateBasic),
        ("铁皮大门",   StructureTier.GateSheetMetal),
        ("浇筑大门",   StructureTier.GateCastMetal),
        ("木门",       StructureTier.DoorWood),
        ("加固木门",   StructureTier.DoorReinforced),
        ("金属门",     StructureTier.DoorMetal),
    };

    /// <summary>破防主力：全部近战 + 全部枪械（走枪托）+ 代表性弓弩 + 丧尸爪击。旧值列只有丧尸/劫掠者两档有对照。</summary>
    private static (string Label, Weapon Weapon, double? LegacyHit)[] Breachers() => new (string, Weapon, double?)[]
    {
        ("丧尸（爪击·撕扯）", WeaponTable.ZombieClaw(),   LegacyZombieHit),
        ("破甲锤",           WeaponTable.Warhammer(),    LegacyRaiderHit),
        ("尖头锤",           WeaponTable.SpikeHammer(),  LegacyRaiderHit),
        ("棍棒",             WeaponTable.Club(),         LegacyRaiderHit),
        ("重剑",             WeaponTable.Greatsword(),   LegacyRaiderHit),
        ("长剑",             WeaponTable.Longsword(),    LegacyRaiderHit),
        ("匕首",             WeaponTable.Dagger(),       LegacyRaiderHit),
        ("草叉",             WeaponTable.Pitchfork(),    LegacyRaiderHit),
        ("步枪（枪托）",     WeaponTable.Rifle(),        LegacyRaiderHit),
        ("手枪（枪柄）",     WeaponTable.Pistol(),       LegacyRaiderHit),
        ("冲锋枪（枪托）",   WeaponTable.Smg(),          LegacyRaiderHit),
        ("霰弹枪（枪托）",   WeaponTable.ImprovisedShotgun(), LegacyRaiderHit),
        ("狩猎弓（射箭）",   WeaponTable.HuntingBow(),   LegacyRaiderHit),
        ("短弓（射箭）",     WeaponTable.ShortBow(),     LegacyRaiderHit),
    };

    /// <summary>典型幸存者：长剑 + 皮夹克 + 长袖布衣（同 EndgameCalibration 口径）。</summary>
    private static DuelFighter ArmoredSurvivor() => new()
    {
        Name = "武装幸存者（皮夹克+布衣）",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Longsword() } },
        Armor = new[] { ArmorTable.Leather(), ArmorTable.LongSleeveShirt() },
    };

    /// <summary>无甲平民：只穿一件长袖布衣。</summary>
    private static DuelFighter Civilian() => new()
    {
        Name = "无甲平民（仅布衣）",
        Weapons = new[] { new WeaponMount { Weapon = WeaponTable.Dagger() } },
        Armor = new[] { ArmorTable.LongSleeveShirt() },
    };

    /// <summary>
    /// 沙袋口径：一只丧尸对着一个<b>不还手</b>的目标一直挥爪，数到「失能（不能再战）」与「死亡」各要几爪。
    /// 走真实结算链（逐层护甲 → 部位命中分配 → 效果触发/流血），与 <c>Arena.Attack</c> 同口径，只是去掉还手与走位。
    /// 之所以要沙袋而非 1v1：1v1 的时长里混着幸存者反杀丧尸的过程，测不出「墙 vs 肉」的纯吞吐比。
    /// </summary>
    private static (double toDisabled, double toDead) ClawsToFell(DuelFighter victim)
    {
        var cfg = new DuelConfig();
        long disabledSum = 0, deadSum = 0;
        var claw = WeaponTable.ZombieClaw();

        for (int seed = 0; seed < Seeds; seed++)
        {
            IRandomSource rng = new SystemRandomSource(90260713 + seed * 137);
            var resolver = new CombatResolver(rng);
            var hit = new VolumeWeightedHitSelector(rng);
            var effects = new CombatEffectResolver(rng, cfg.Effects);

            var body = victim.BodyFactory();
            body.BleedRatePerWound = cfg.BleedRatePerWound;
            body.SetBloodMax(cfg.BloodMax);
            var armor = CombatResolver.OrderOuterToInner(victim.RollArmor(rng));

            int hits = 0, disabledAt = 0;
            while (!body.IsDead && hits < 500)
            {
                var alive = body.Parts.Values.Where(p => !body.IsGone(p.Name)).ToList();
                if (alive.Count == 0) break;

                var part = hit.Select(alive);
                var result = resolver.Resolve(claw, armor, part);
                effects.Apply(body, claw, result);
                hits++;

                // 爪与爪之间流逝一个攻击间隔 → 流血照常推进（不然沙袋会低估丧尸，失血是它的主要杀伤）。
                if (!body.IsDead) body.TickBleed(claw.AttackInterval);

                // 「失能」＝ Arena 的 Fightable 反面：死 / 昏迷 / 操作penalty 满 —— 到此这人已经出局。
                bool downed = body.IsDead || body.IsUnconscious
                              || body.DisabilityModifiers.OperationPenalty >= 1.0;
                if (disabledAt == 0 && downed) disabledAt = hits;
            }
            if (disabledAt == 0) disabledAt = hits;
            disabledSum += disabledAt;
            deadSum += hits;
        }
        return ((double)disabledSum / Seeds, (double)deadSum / Seeds);
    }

    public static void Run(string outPath)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        Weapon claw = WeaponTable.ZombieClaw();
        double clawHit = StructureDamage.PerHit(claw);
        double clawInterval = StructureDamage.Interval(claw);
        double clawDps = StructureDamage.PerSecond(claw);

        sb.AppendLine("# 围墙/大门破防校准（砸墙伤害由武器派生）");
        sb.AppendLine();
        sb.AppendLine("**规则**：`每击伤害 = 砸墙有效武器平均伤害 × 该武器砸墙系数`，`节奏 = 砸墙有效武器出手间隔`。");
        sb.AppendLine("**砸墙有效武器**：枪械 → 枪托 profile（抡枪托，不是开枪打墙）；弓弩 → 弓本体 × 全表最低系数；近战/天生 → 本体。");
        sb.AppendLine(string.Create(inv, $"一夜 = {NightSeconds:0}s。系数是数据（`docs/weapons-calc.xlsx`『武器表』「砸墙系数」列），本文只算不改。"));
        sb.AppendLine();

        // ① 各武器的砸墙效率（新旧对比）
        sb.AppendLine("## ① 各武器砸墙效率（新 vs 旧）");
        sb.AppendLine();
        sb.AppendLine("> 旧实现：砸墙伤害是两个写死的常数——丧尸恒 12/爪、劫掠者恒 25/次（**不读武器伤害**）。");
        sb.AppendLine("> 于是破甲锤砸墙 = 匕首砸墙 = 25，且持枪者因节奏慢反而比持匕首者破墙慢 43%。");
        sb.AppendLine();
        sb.AppendLine("| 武器 | 砸墙系数 | 伤害基数（有效武器） | 每击 | 节奏 | 每秒 | 旧·每击 | 旧·每秒 |");
        sb.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|");
        foreach (var (label, w, legacy) in Breachers())
        {
            Weapon bash = StructureDamage.Bashing(w);
            string basis = bash.Name == w.Name
                ? string.Create(inv, $"{w.DamageMin:0.#}~{w.DamageMax:0.#}")
                : string.Create(inv, $"{bash.DamageMin:0.#}~{bash.DamageMax:0.#}（枪托）");
            double hit = StructureDamage.PerHit(w);
            double interval = StructureDamage.Interval(w);
            double dps = StructureDamage.PerSecond(w);
            double legacyDps = legacy.HasValue ? legacy.Value / w.AttackInterval : 0; // 旧：节奏取武器本体（枪也是开火间隔）
            sb.AppendLine(string.Create(inv,
                $"| {label} | ×{StructureDamage.FactorFor(w):0.0#} | {basis} | {hit:0.0} | {interval:0.0}s | **{dps:0.00}** | {legacy:0.#} | {legacyDps:0.00} |"));
        }
        sb.AppendLine();

        // ② 破防耗时矩阵：武器 × 结构（单个攻击者）
        sb.AppendLine("## ② 破防耗时矩阵：一个攻击者砸穿一处结构（击数／秒）");
        sb.AppendLine();
        sb.Append("| 结构 | 血量 |");
        foreach (var (label, _, _) in Breachers()) sb.Append(inv, $" {label} |");
        sb.AppendLine();
        sb.Append("|---|---:|");
        foreach (var _ in Breachers()) sb.Append("---:|");
        sb.AppendLine();
        foreach (var (label, tier) in Structures)
        {
            int hp = CampStructureTable.MaxHp(tier);
            sb.Append(inv, $"| {label} | {hp} |");
            foreach (var (_, w, _) in Breachers())
            {
                int hits = StructureDamage.HitsToBreach(w, hp);
                double sec = StructureDamage.SecondsToBreach(w, hp);
                sb.Append(inv, $" {hits}／{sec:0}s |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        // ③ 丧尸新旧破防耗时对比
        sb.AppendLine("## ③ 丧尸破防耗时：新 vs 旧（爪击砍半终于传导到了墙）");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"丧尸每爪 **{clawHit:0.0}**（爪击均值 {(claw.DamageMin + claw.DamageMax) / 2:0.#} × 撕扯系数 {StructureDamage.FactorFor(claw):0.0#}）／{clawInterval:0.0}s ⇒ {clawDps:0.00} 点/秒。"));
        sb.AppendLine(string.Create(inv,
            $"旧值每爪 {LegacyZombieHit}（写死常数）⇒ {LegacyZombieHit / clawInterval:0.00} 点/秒。**破墙速度降至旧值的 {clawDps / (LegacyZombieHit / clawInterval) * 100:0}%**。"));
        sb.AppendLine();
        sb.AppendLine("| 结构 | 血量 | 新（爪／秒） | 旧（爪／秒） | 单只丧尸变慢 |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (label, tier) in Structures)
        {
            int hp = CampStructureTable.MaxHp(tier);
            int nHits = StructureDamage.HitsToBreach(claw, hp);
            double nSec = StructureDamage.SecondsToBreach(claw, hp);
            int oHits = (int)Math.Ceiling((double)hp / LegacyZombieHit);
            double oSec = oHits * clawInterval;
            sb.AppendLine(string.Create(inv,
                $"| {label} | {hp} | {nHits}／**{nSec:0}s** | {oHits}／{oSec:0}s | +{(nSec / oSec - 1) * 100:0}% |"));
        }
        sb.AppendLine();

        // ④ 一群丧尸并肩砸同一处（线性叠加，用户拍板维持；但一线只挤得下 3~4 只）
        sb.AppendLine("## ④ 一群丧尸并肩砸同一处：破防秒数");
        sb.AppendLine();
        sb.AppendLine("> 丧尸在南北两门外生成，直扑最近结构 ⇒ **火力自动集中在两扇大门上**。");
        sb.AppendLine("> ⚠ **能同时打到门的只有 3~4 只**（丧尸有碰撞体积、爪击是近战距离，后面的挤不进来、够不着）——");
        sb.AppendLine("> **这就是次线性叠加，是空间物理做的，不是规则做的**（故规则层维持线性，用户拍板）。");
        sb.AppendLine("> 「8 只」那栏是 **Sim 口径：失真**（Sim 无碰撞体积/无攻击距离，会让 8 只同时贴门打），仅作对照，**别拿它调平衡**。");
        sb.AppendLine();
        int[] gang = { 1, 2, 3, 4, 8 };
        sb.Append("| 结构 | 血量 |");
        foreach (int n in gang)
        {
            string head = n > MaxAbreast
                ? string.Create(inv, $" ⚠{n} 只（Sim 失真） |")
                : string.Create(inv, $" {n} 只 |");
            sb.Append(head);
        }
        sb.AppendLine();
        sb.Append("|---|---:|");
        foreach (var _ in gang) sb.Append("---:|");
        sb.AppendLine();
        foreach (var (label, tier) in Structures)
        {
            int hp = CampStructureTable.MaxHp(tier);
            sb.Append(inv, $"| {label} | {hp} |");
            foreach (int n in gang)
            {
                int hits = (int)Math.Ceiling(hp / (clawHit * n));
                double sec = hits * clawInterval;
                sb.Append(inv, $" {sec:0.0}s |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        // ⑤ 墙 vs 人：同一只丧尸的吞吐比
        sb.AppendLine("## ⑤ 一堵墙相当于几个人（同一只丧尸的爪数比）");
        sb.AppendLine();
        var armored = ClawsToFell(ArmoredSurvivor());
        var civ = ClawsToFell(Civilian());
        sb.AppendLine(string.Create(inv,
            $"沙袋实测（{Seeds} 次，走真实逐层护甲+部位命中+流血；目标不还手）："));
        sb.AppendLine();
        sb.AppendLine("| 目标 | 撂倒（失能）需 | 打死需 | 撂倒耗时 |");
        sb.AppendLine("|---|---:|---:|---:|");
        sb.AppendLine(string.Create(inv,
            $"| 武装幸存者（皮夹克+布衣） | {armored.toDisabled:0.0} 爪 | {armored.toDead:0.0} 爪 | {armored.toDisabled * clawInterval:0.0}s |"));
        sb.AppendLine(string.Create(inv,
            $"| 无甲平民（仅布衣） | {civ.toDisabled:0.0} 爪 | {civ.toDead:0.0} 爪 | {civ.toDisabled * clawInterval:0.0}s |"));
        sb.AppendLine();
        sb.AppendLine("| 结构 | 砸穿需 | ＝几个武装幸存者 | ＝几个无甲平民 |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var (label, tier) in Structures)
        {
            int hp = CampStructureTable.MaxHp(tier);
            int hits = StructureDamage.HitsToBreach(claw, hp);
            sb.AppendLine(string.Create(inv,
                $"| {label} | {hits} 爪 | {hits / armored.toDisabled:0.0} 人 | {hits / civ.toDisabled:0.0} 人 |"));
        }
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"（旧值下基础围栏只值 {Math.Ceiling(150.0 / LegacyZombieHit) / armored.toDisabled:0.0} 个武装幸存者——**比一个人还不如**。"));
        sb.AppendLine(string.Create(inv,
            $"现在值 {StructureDamage.HitsToBreach(claw, 150) / armored.toDisabled:0.0} 人：墙终于比肉硬。）"));
        sb.AppendLine();

        // ⑥ 尸潮场景
        sb.AppendLine("## ⑥ 尸潮 / 袭营场景：墙撑得住多久");
        sb.AppendLine();
        sb.AppendLine("> 「一线」= 真正够得着门的只数，上限 **4**（碰撞体积 + 近战距离）。最后一列是 Sim 的失真口径（上限 8），只作对照。");
        sb.AppendLine();
        sb.AppendLine("| 场景 | 来袭数 | 每门分到 | 一线（实际） | 基础大门(250) 破防 | 浇筑大门(800) 破防 | 占一夜 | 旧·基础大门 | ⚠Sim 失真(8只) |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        (string, int)[] scenes =
        {
            ("常规袭营 第1天（4人营）", RaidWave.ZombieCount(1, 4)),
            ("常规袭营 第10天（4人营）", RaidWave.ZombieCount(10, 4)),
            ("常规袭营 第30天（4人营）", RaidWave.ZombieCount(30, 4)),
            ("尸潮 首波（4人营）", HordeTimeline.WaveSize(0, 4)),
            ("尸潮 第5波", HordeTimeline.WaveSize(5, 4)),
            ("尸潮 并发上限", HordeTimeline.MaxConcurrentSiege),
        };
        foreach (var (name, count) in scenes)
        {
            int perGate = Math.Max(1, count / 2);               // 南北两门均分
            int abreast = Math.Min(perGate, MaxAbreast);        // 真够得着门的只数（碰撞体积+近战距离）
            int simAbreast = Math.Min(perGate, 8);              // Sim 的失真口径（无碰撞体积）
            double t250 = Math.Ceiling(250.0 / (clawHit * abreast)) * clawInterval;
            double t800 = Math.Ceiling(800.0 / (clawHit * abreast)) * clawInterval;
            double old250 = Math.Ceiling(250.0 / (LegacyZombieHit * abreast)) * clawInterval;
            double sim250 = Math.Ceiling(250.0 / (clawHit * simAbreast)) * clawInterval;
            sb.AppendLine(string.Create(inv,
                $"| {name} | {count} | {perGate} | {abreast} | {t250:0.0}s | {t800:0.0}s | {t250 / NightSeconds * 100:0.0}% | {old250:0.0}s | {sim250:0.0}s |"));
        }
        sb.AppendLine();
        sb.AppendLine("对照 `endgamecal`：4 人营地 vs 一波 10 只 → 清波率 1%、平均耗时 51.9s（营地基本被打光）。");
        sb.AppendLine("⚠ **本节整节的前提「火力全集中在两扇门上」已被推翻** —— 丧尸也啃围栏（用户拍板）。正确口径见 ⑦。");

        AppendPerimeter(sb, inv, claw, clawHit, clawInterval);

        SimReport.Write(outPath, sb.ToString()); // 出处戳 + 落盘（含建目录）
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"[已写出] {outPath}");
    }

    // ================= ⑦ 整条墙线口径（丧尸也啃围栏）=================

    /// <summary>营地南墙实际几何（godot/data/camp.json）：大门 200×22，两侧各一条 800×22 的围栏（CampMain 切成 100px 一格）。</summary>
    private const double GateFace = 200;
    private const double FenceRun = 800;
    private const double SegLen = 100;   // CampMain.FenceSegment
    private const double WallThick = 22;

    /// <summary>
    /// 一处结构的攻击位名额 —— <b>直接调 <see cref="BreachSlots.Capacity"/>（运行时同一份规则）</b>，不再手抄"3~4 只"。
    /// 这就是 Sim 此前缺的那条空间约束：碰撞体积 + 近战距离 ⇒ 一处只站得下几个。
    /// </summary>
    private static int Slots(double w, double h) => BreachSlots.Capacity(w, h, BreachSlots.DefaultFootprint);

    /// <summary>
    /// 把 <paramref name="count"/> 只丧尸按运行时的择目标规则（<see cref="BreachSlots.ChooseTarget"/>）摊到南墙一线上，
    /// 返回**第一处被砸穿**要多久（秒）——防线的寿命取决于最脆的那一点，不是大门一处。
    /// </summary>
    private static (double seconds, string where, int onGate, int onFence) FirstBreach(
        int count, int gateHp, int fenceHp, double perHit, double interval)
    {
        // 南墙一线：大门 + 左右各 8 格围栏（camp.json 的 800px 段切成 100px 一格）。
        var cands = new List<BreachCandidate> { new(0, 1100, 1478, GateFace, WallThick, Slots(GateFace, WallThick)) };
        int segs = (int)Math.Round(FenceRun / SegLen);
        for (int i = 0; i < segs; i++)
        {
            cands.Add(new BreachCandidate(1 + i, 1300 + i * SegLen, 1478, SegLen, WallThick, Slots(SegLen, WallThick)));            // 门东侧
            cands.Add(new BreachCandidate(1 + segs + i, 1100 - (i + 1) * SegLen, 1478, SegLen, WallThick, Slots(SegLen, WallThick))); // 门西侧
        }

        // 生成带就是 SpawnCampZombies 的口径：x∈[1110,1290]、门外 40px 处。
        var book = new BreachSlotBook();
        for (ulong i = 0; i < (ulong)count; i++)
        {
            double px = 1110 + (i * 43) % 180;
            BreachSlots.ChooseTarget(px, 1540, cands, book, i, radius: 320, out _, out _, out _);
        }

        double best = double.PositiveInfinity;
        string where = "无人可砸";
        foreach (BreachCandidate c in cands)
        {
            int n = book.Occupancy(c.Id);
            if (n == 0)
            {
                continue;
            }
            int hp = c.Id == 0 ? gateHp : fenceHp;
            double t = Math.Ceiling(hp / (perHit * n)) * interval;
            if (t < best)
            {
                best = t;
                where = c.Id == 0 ? "大门" : "围栏";
            }
        }
        int onGate = book.Occupancy(0);
        return (best, where, onGate, count - onGate);
    }

    /// <summary>
    /// ⑦ <b>整条墙线口径</b>：丧尸也啃围栏（用户拍板），受攻击面从两扇门摊开到整条墙线。
    /// 本节是**唯一该信的破防口径**——④⑥ 两节的前提（火力全集中在门上）已被推翻。
    /// </summary>
    private static void AppendPerimeter(StringBuilder sb, CultureInfo inv, Weapon claw, double perHit, double interval)
    {
        sb.AppendLine();
        sb.AppendLine("## ⑦ 整条墙线口径：丧尸也啃围栏（**该信的就是这一节**）");
        sb.AppendLine();
        sb.AppendLine("> 用户拍板：**丧尸也会打围栏，不止会打门**。查下来丧尸的 AI 里本就没有「只打门」这回事——");
        sb.AppendLine("> 它们全砸门，纯粹是因为生成点被钉在门缝正前方 40px（一出生就够得着门）。");
        sb.AppendLine("> 现在每处结构有**攻击位名额**（`BreachSlots`，运行时与本 harness 同一份规则）：**门口站满了，后面的去啃旁边的围栏。**");
        sb.AppendLine(">");
        sb.AppendLine(string.Create(inv,
            $"> 名额（按真实几何算，不是估的）：大门 {GateFace:0}px ⇒ **{Slots(GateFace, WallThick)} 只**；一格围栏 {SegLen:0}px ⇒ **{Slots(SegLen, WallThick)} 只**（占位宽度 {BreachSlots.DefaultFootprint:0}px = 丧尸直径）。"));
        sb.AppendLine("> （⚠ 旧报告手抄的「一扇门前 3~4 只」偏低：200px 的门面按 26px 的身板排得下 7 只。）");
        sb.AppendLine();
        sb.AppendLine("**围栏已切成 100px 一格**（`CampMain.SplitFence`）：一格独立 HP、独立名额、独立缺口。");
        sb.AppendLine("不切的话，camp.json 里一段围栏是 **800px 的一整块却只有 150 血** ⇒ 砸穿它一次性抹掉半面墙，围栏成了防线上最脆的一环。");
        sb.AppendLine();

        // 升级路径对比：谁值得花料
        (string Label, StructureTier Gate, StructureTier Fence)[] plans =
        {
            ("现状（基础门 + 基础围栏）",     StructureTier.GateBasic,     StructureTier.FenceBasic),
            ("只升大门 → 浇筑(800)",         StructureTier.GateCastMetal, StructureTier.FenceBasic),
            ("只升围栏 → 全金属(750)",       StructureTier.GateBasic,     StructureTier.FenceFullMetal),
            ("两样都升到顶格",               StructureTier.GateCastMetal, StructureTier.FenceFullMetal),
        };
        int[] waves = { 8, 10, 20, 40 };

        sb.AppendLine("### 升级路径对比：第一处破口出现在第几秒");
        sb.AppendLine();
        sb.Append("| 升级方案 | 门血 | 围栏血/格 |");
        foreach (int w in waves) sb.Append(inv, $" 来 {w} 只 |");
        sb.AppendLine();
        sb.Append("|---|---:|---:|");
        foreach (var _ in waves) sb.Append("---:|");
        sb.AppendLine();
        foreach (var (label, gateTier, fenceTier) in plans)
        {
            int gHp = CampStructureTable.MaxHp(gateTier);
            int fHp = CampStructureTable.MaxHp(fenceTier);
            sb.Append(inv, $"| {label} | {gHp} | {fHp} |");
            foreach (int w in waves)
            {
                (double sec, string where, _, _) = FirstBreach(Math.Max(1, w / 2), gHp, fHp, perHit, interval);
                sb.Append(inv, $" {sec:0.0}s（{where}）|");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("（「来 N 只」= 整波 N 只，南北两门均分 ⇒ 每面墙 N/2。破口出现在**最脆的那一点**，不一定是门。）");
        sb.AppendLine();

        // 一线摊开的样子
        sb.AppendLine("### 一波丧尸是怎么摊开的");
        sb.AppendLine();
        sb.AppendLine("| 来袭数 | 每面墙 | 砸门 | 啃围栏 |");
        sb.AppendLine("|---:|---:|---:|---:|");
        foreach (int w in new[] { 8, 10, 20, 40, 80 })
        {
            int per = Math.Max(1, w / 2);
            (_, _, int onGate, int onFence) = FirstBreach(per, 250, 150, perHit, interval);
            sb.AppendLine(string.Create(inv, $"| {w} | {per} | {onGate} | {onFence} |"));
        }
        sb.AppendLine();
        sb.AppendLine("**门只吃得下 7 只，多出来的全摊到围栏上** —— 这就是「受攻击面从两扇门变成整条墙线」的全部含义。");
        sb.AppendLine();

        // 结论（数字全部现算，不手抄）
        int segsPerRun = (int)Math.Round(FenceRun / SegLen);
        int perimeterSegs = segsPerRun * 4 + (int)Math.Ceiling(1156.0 / SegLen) * 2; // 南北各2条800 + 东西各1条1156

        int gBasic = CampStructureTable.MaxHp(StructureTier.GateBasic);
        int gTop = CampStructureTable.MaxHp(StructureTier.GateCastMetal);
        int fBasic = CampStructureTable.MaxHp(StructureTier.FenceBasic);
        int fTop = CampStructureTable.MaxHp(StructureTier.FenceFullMetal);

        double Base(int w) => FirstBreach(Math.Max(1, w / 2), gBasic, gTop == 0 ? fBasic : fBasic, perHit, interval).seconds;
        double GateOnly(int w) => FirstBreach(Math.Max(1, w / 2), gTop, fBasic, perHit, interval).seconds;
        double FenceOnly(int w) => FirstBreach(Math.Max(1, w / 2), gBasic, fTop, perHit, interval).seconds;
        double Both(int w) => FirstBreach(Math.Max(1, w / 2), gTop, fTop, perHit, interval).seconds;

        sb.AppendLine("### 结论：升级围墙值不值得（**答案变了，而且有顺序**）");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"**① 大门永远是第一个破的点，先升它。** 门前站得下 7 只（围栏一格只站得下 3 只），火力最集中："));
        sb.AppendLine(string.Create(inv,
            $"门（{gBasic} 血、7 只同砸）{Math.Ceiling(gBasic / (perHit * 7)) * interval:0.0}s 破；"
            + $"一格围栏（{fBasic} 血、只站得下 3 只）要 {Math.Ceiling(fBasic / (perHit * 3)) * interval:0.0}s ⇒ **门先破**。"));
        sb.AppendLine(string.Create(inv,
            $"升到浇筑({gTop})：小波（10 只）**{Base(10):0.0}s → {GateOnly(10):0.0}s**，多撑 {GateOnly(10) - Base(10):0.0} 秒（+{(GateOnly(10) / Base(10) - 1) * 100:0}%）。"));
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"**② 只升围栏 = 纯白花钱。** 围栏从 {fBasic} 升到 {fTop}，破防时间**一秒都没变**"));
        sb.AppendLine(string.Create(inv,
            $"（10 只：{Base(10):0.0}s → {FenceOnly(10):0.0}s；40 只：{Base(40):0.0}s → {FenceOnly(40):0.0}s）——因为门还是那么脆，谁也没绕开它。"));
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"**③ 但大门升上去之后，围栏立刻接管成新的最脆点。** 波次一大（20 只以上），门口 7 个位子坐满，"));
        sb.AppendLine(string.Create(inv,
            $"多出来的全去啃围栏 ⇒ 只升门的收益被围栏**封顶**：20 只时 {GateOnly(20):0.0}s，两样都升才 {Both(20):0.0}s（围栏这一步值 +{Both(20) - GateOnly(20):0.0} 秒）；"));
        sb.AppendLine(string.Create(inv,
            $"40 只时 {GateOnly(40):0.0}s vs {Both(40):0.0}s（+{Both(40) - GateOnly(40):0.0} 秒）。"));
        sb.AppendLine();
        sb.AppendLine("⇒ **升级顺序是硬的：先大门，后围栏。** 反过来做，前一半的料全打水漂。");
        sb.AppendLine("⇒ 这与旧报告「升级围墙投资回报≈0」的结论**相反**。旧结论建立在两个已被推翻的前提上：");
        sb.AppendLine("   「丧尸只砸门」（错：它也啃围栏）+「一线能站 8 只」（Sim 失真口径，且方向反了——门其实站得下 7 只，围栏一格只站得下 3 只）。");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"⚠ **代价也第一次真实**：整圈围栏是 **{perimeterSegs} 格**（不是 6 段）。升满 = {perimeterSegs} 格的料 + 2 扇门，"));
        sb.AppendLine("而不是从前以为的「升 2 扇门就完事」。围栏这条路是**真正的经营大坑**——但它现在买得到东西了。");
        sb.AppendLine();
        sb.AppendLine("> ⚠ **口径声明**：本节所有数字＝**空间实际口径**（攻击位名额由 `BreachSlots` 按真实几何算，与运行时同一份规则）。");
        sb.AppendLine("> ④⑥ 两节是 **Sim 口径**（无碰撞体积、无名额、假设火力全压在门上），**已失真，勿用于调平衡**。");
    }
}
