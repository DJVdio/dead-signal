namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威武器数据源。Godot 消费层、Sim 聚合模拟、Sim 对决战报全部从此读取，
/// 消除此前 CombatData / Sim.Program / Sim.DuelReport 三处各自维护武器表的数据源分裂。
///
/// 数值口径：重叠武器（匕首/手枪/爪击）统一采用 <b>Godot 侧现行值</b>；仅 Sim 侧存在的武器
/// （短剑/长剑/重剑/棍棒/尖头锤/破甲锤/土制枪/冲锋枪/步枪/狙击枪）沿用其原值。
/// 穿透/伤害类型来自设计文档第 5 节；伤害区间/攻速为原型期<b>拟定待调</b>，靠 Sim 拉表校准。
///
/// <para><b>全局慢节奏（用户拍板：多角色操控需要慢战斗）</b>：全表冷却重标定。
/// 近战锚点匕首 1.4 / 长剑 3.6，按 ×2~3.3 随沉重度插值（破甲锤外推离谱故按手感封顶 4.5）；爪击 2.3（敌方同步）。
/// <b>枪械倍率经用户回调 ×5~5.6→×3~4</b>（原步枪 4.5/狙击 7.5 两锚作废）：手枪 2.0 / 冲锋枪轮冷却 1.8 / 步枪 2.8 /
/// 狙击 5.0 / 土制枪 3.5，目标"压制多数近战但非碾压"（步枪vs长剑~70-80%）。
/// 新增民用<b>栓动猎枪</b>冷却锚 4.5（用户最初给它的）。均拟定待调。</para>
/// </summary>
public static class WeaponTable
{
    // ---- 近战锐器 ----

    /// <summary>匕首：近战锐器，穿透 9%（文档：匕首 9%）。数值采 Godot 值（4-10，此前 Sim 为 4-14）。</summary>
    public static Weapon Dagger() => new()
    {
        Name = "匕首",
        DamageMin = 4,           // 拟定待调
        DamageMax = 10,          // 拟定待调
        Penetration = 0.09,
        DamageType = DamageType.Sharp,
        CanDualWield = true,
        AttackInterval = 1.4,    // 拟定待调（全局慢节奏锚点：匕首 0.7→1.4，×2.0）
    };

    /// <summary>短剑：近战锐器，穿透 12%。</summary>
    public static Weapon Shortsword() => new()
    {
        Name = "短剑",
        DamageMin = 6,           // 拟定待调
        DamageMax = 20,          // 拟定待调
        Penetration = 0.12,
        DamageType = DamageType.Sharp,
        AttackInterval = 2.4,    // 拟定待调（近战倍率曲线 0.9→×2.64）
    };

    /// <summary>刺剑：单手近战锐器，突刺路线——伤害区间窄、穿透略高于短剑，攻速略快。可双持。</summary>
    public static Weapon Rapier() => new()
    {
        Name = "刺剑",
        DamageMin = 7,           // 拟定待调（刺击：区间窄）
        DamageMax = 18,          // 拟定待调
        Penetration = 0.15,      // 拟定待调（刺击穿透略高于短剑 12%）
        DamageType = DamageType.Sharp,
        CanDualWield = true,     // 拟定待调
        AttackInterval = 1.9,    // 拟定待调（近战倍率曲线 0.8→×2.32；略快于短剑）
    };

    /// <summary>长剑：近战锐器，穿透 18%。双手武器。</summary>
    public static Weapon Longsword() => new()
    {
        Name = "长剑",
        DamageMin = 10,          // 拟定待调
        DamageMax = 30,          // 拟定待调
        Penetration = 0.18,
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        AttackInterval = 3.6,    // 拟定待调（全局慢节奏锚点：长剑 1.1→3.6，×3.27）
    };

    /// <summary>重剑：近战锐器，穿透 24%。双手武器。</summary>
    public static Weapon Greatsword() => new()
    {
        Name = "重剑",
        DamageMin = 14,          // 拟定待调
        DamageMax = 40,          // 拟定待调
        Penetration = 0.24,
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        AttackInterval = 4.0,    // 拟定待调（重近战曲线 1.1→1.8 段插值，1.4→4.0，封顶带内）
    };

    /// <summary>草叉：双手近战锐器，农具改造——多齿突刺，穿透中等、伤害区间较宽、攻速偏慢。照长剑数值风格。</summary>
    public static Weapon Pitchfork() => new()
    {
        Name = "草叉",
        DamageMin = 9,           // 拟定待调
        DamageMax = 26,          // 拟定待调
        Penetration = 0.16,      // 拟定待调（多齿刺击，略低于长剑 18%）
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        AttackInterval = 3.7,    // 拟定待调（重近战曲线 1.2→3.7，略慢于长剑）
    };

    // ---- 近战钝器 ----

    /// <summary>棍棒：近战钝器，穿透 3%（文档：棍棒级 3%）。</summary>
    public static Weapon Club() => new()
    {
        Name = "棍棒",
        DamageMin = 7,           // 拟定待调
        DamageMax = 9,           // 拟定待调
        Penetration = 0.03,
        DamageType = DamageType.Blunt,
        AttackInterval = 2.4,    // 拟定待调（近战倍率曲线 0.9→×2.64）
    };

    /// <summary>尖头锤：近战钝器，穿透 5%。</summary>
    public static Weapon SpikeHammer() => new()
    {
        Name = "尖头锤",
        DamageMin = 12,          // 拟定待调
        DamageMax = 16,          // 拟定待调
        Penetration = 0.05,
        DamageType = DamageType.Blunt,
        AttackInterval = 3.7,    // 拟定待调（重近战曲线 1.2→3.7）
    };

    /// <summary>破甲锤：近战钝器，穿透 35%（破甲路线，全近战最高穿透）。重型双手武器。
    /// 穿透 20%→35% 拟定待调（用户拍板"提升穿透力"）：坐实"破甲专精"身份（＞重剑 24%）。
    /// 注意：Sim 复验显示穿透对「破甲锤 vs 长剑·满甲」纯对决胜率几乎无影响（20%→70% 仅 9.9%→10.5%），
    /// 该对决瓶颈是攻速 1.8s 而非穿透——穿透买不回胜率。见回报，若要重甲对决表现需另议攻速/直击。</summary>
    public static Weapon Warhammer() => new()
    {
        Name = "破甲锤",
        DamageMin = 20,          // 拟定待调
        DamageMax = 28,          // 拟定待调
        Penetration = 0.35,      // 拟定待调（20%→35%，破甲专精，全近战最高穿透）
        DamageType = DamageType.Blunt,
        TwoHanded = true,        // 拟定待调
        // 拟定待调（全局慢节奏）：沿轻武器倍率外推 1.8×5.5≈9.9s 太离谱，按手感封顶到 4.5s
        // ——长剑 3.6 量级偏上、全近战最慢，与步枪(栓动猎枪)4.5 同档。
        AttackInterval = 4.5,
    };

    // ---- 远程 ----

    /// <summary>土制枪：远程锐器，穿透 10%，误差角大。单手；枪托可贴脸钝击。</summary>
    public static Weapon Zipgun() => new()
    {
        Name = "土制枪",
        DamageMin = 11,          // 拟定待调（8→11，小幅提伤配合缩间隔，脱离全场垫底）
        DamageMax = 22,          // 拟定待调（16→22）
        Penetration = 0.10,
        DamageType = DamageType.Sharp,
        IsRanged = true,
        BaseSpreadDegrees = 8,   // 拟定待调
        // 拟定待调（枪械倍率回调 ×3~4）：取 3.5s，保持它为最弱但可用的枪（DPS≈4.9，略低于手枪，仍是枪械垫底）。
        AttackInterval = 3.5,
        MaxRange = 130,          // 拟定待调（土制枪：近而陡，出满伤段掉得快）
        FalloffStart = 25,       // 拟定待调
        FalloffFloor = 0.35,     // 拟定待调
        StockMeleeDamageMin = 3,        // 拟定待调（枪托钝击）
        StockMeleeDamageMax = 5,        // 拟定待调
        StockMeleePenetration = 0.02,   // 拟定待调
        StockMeleeInterval = 1.4,       // 拟定待调
    };

    /// <summary>手枪：远程锐器，穿透 15%（文档：手枪 15%）。数值采 Godot 值（8-14，此前 Sim 为 12-20）。</summary>
    public static Weapon Pistol() => new()
    {
        Name = "手枪",
        DamageMin = 8,           // 拟定待调
        DamageMax = 14,          // 拟定待调
        Penetration = 0.15,
        DamageType = DamageType.Sharp,
        CanDualWield = true,
        IsRanged = true,
        BaseSpreadDegrees = 3,   // 拟定待调
        AttackInterval = 2.0,    // 拟定待调（枪械倍率回调 ×3~4：0.5→×4.0）
        MaxRange = 200,          // 拟定待调（手枪：近而陡）
        FalloffStart = 55,       // 拟定待调
        FalloffFloor = 0.5,      // 拟定待调
        StockMeleeDamageMin = 3,        // 拟定待调（手枪柄砸击）
        StockMeleeDamageMax = 6,        // 拟定待调
        StockMeleePenetration = 0.02,   // 拟定待调
        StockMeleeInterval = 1.2,       // 拟定待调
    };

    /// <summary>冲锋枪：远程锐器，穿透 18%。双手抵肩；枪托可贴脸钝击。
    /// 用户拍板：三连发（一次射击打 3 发）。攻击模型＝冷却→三连发→冷却→三连发。
    /// AttackInterval 语义为连发之后的冷却；DPS＝3 发/循环。拟定待调。</summary>
    public static Weapon Smg() => new()
    {
        Name = "冲锋枪",
        DamageMin = 10,          // 拟定待调
        DamageMax = 18,          // 拟定待调
        Penetration = 0.18,
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        CanDualWield = true,     // 用户拍板：放开可双持
        IsRanged = true,
        BaseSpreadDegrees = 6,   // 拟定待调
        BurstCount = 3,          // 用户拍板：三连发
        BurstInterval = 0.06,    // 拟定待调（连发内每弹间隔，快速点射不随全局慢节奏放缓）
        // 冷却（连发之间）拟定待调（枪械倍率回调 ×3~4）：重拟至 1.8s——一次开火打 3 发、约每 1.9s 一轮，
        // DPS＝3×14.5 / (1.8 + 2×0.06)＝22.7，仍是全表 DPS 榜首(约 2×次名)但不碾压。
        // 三连发是它的特色（爆发点射换更长冷却）。
        AttackInterval = 1.8,
        MaxRange = 280,          // 拟定待调（冲锋枪：中距，衰减中等）
        FalloffStart = 70,       // 拟定待调
        FalloffFloor = 0.45,     // 拟定待调
        StockMeleeDamageMin = 4,        // 拟定待调（枪托钝击）
        StockMeleeDamageMax = 7,        // 拟定待调
        StockMeleePenetration = 0.02,   // 拟定待调
        StockMeleeInterval = 1.3,       // 拟定待调
    };

    /// <summary>步枪（军用定位，与民用"栓动猎枪"并存——用户拍板两把都有）：远程锐器，穿透 21%。双手抵肩；枪托可贴脸钝击。</summary>
    public static Weapon Rifle() => new()
    {
        Name = "步枪",
        DamageMin = 20,          // 拟定待调
        DamageMax = 35,          // 拟定待调
        Penetration = 0.21,
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        BaseSpreadDegrees = 2,   // 拟定待调
        AttackInterval = 2.8,    // 拟定待调（枪械倍率回调 ×3~4：0.8→×3.5；目标步枪vs长剑~70-80%，harness校准）
        MaxRange = 550,          // 拟定待调（步枪：远而缓）
        FalloffStart = 200,      // 拟定待调
        FalloffFloor = 0.6,      // 拟定待调
        StockMeleeDamageMin = 6,        // 拟定待调（枪托重砸）
        StockMeleeDamageMax = 10,       // 拟定待调
        StockMeleePenetration = 0.03,   // 拟定待调
        StockMeleeInterval = 1.5,       // 拟定待调
    };

    /// <summary>栓动猎枪（用户拍板新增，民用猎枪定位，与军用"步枪"并存）：远程锐器，单发郑重、栓动慢冷却。
    /// 数值介于步枪(20-35/穿21%/远)与土制枪(11-22/穿10%/近)之间，民用可得性更高。冷却锚 4.5s（用户最初给它的）。
    /// 全字段拟定待调（伤害/射程/衰减为 draft，靠 Sim 拉表校准）。</summary>
    public static Weapon BoltActionHuntingRifle() => new()
    {
        Name = "栓动猎枪",
        DamageMin = 16,          // 拟定待调（介于步枪 20 与土制枪 11 之间）
        DamageMax = 28,          // 拟定待调（介于步枪 35 与土制枪 22 之间）
        Penetration = 0.16,      // 拟定待调（介于步枪 21% 与土制枪 10% 之间）
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        BaseSpreadDegrees = 3,   // 拟定待调（民用光学，介于步枪 2 与土制枪 8）
        AttackInterval = 4.5,    // 拟定待调（栓动慢冷却锚点——用户最初给它的 4.5s）
        MaxRange = 420,          // 拟定待调（介于步枪 550 与土制枪 130）
        FalloffStart = 160,      // 拟定待调
        FalloffFloor = 0.55,     // 拟定待调
        StockMeleeDamageMin = 6,        // 拟定待调（枪托重砸，同步枪）
        StockMeleeDamageMax = 10,       // 拟定待调
        StockMeleePenetration = 0.03,   // 拟定待调
        StockMeleeInterval = 1.6,       // 拟定待调
    };

    /// <summary>狙击枪：远程锐器，穿透 70%（碾压多层甲，弹药稀缺是唯一约束）。双手抵肩；枪托可贴脸钝击。</summary>
    public static Weapon SniperRifle() => new()
    {
        Name = "狙击枪",
        DamageMin = 40,          // 拟定待调
        DamageMax = 70,          // 拟定待调
        Penetration = 0.70,
        DamageType = DamageType.Sharp,
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        BaseSpreadDegrees = 0.5, // 拟定待调
        AttackInterval = 5.0,    // 拟定待调（枪械倍率回调 ×3~4：狙击 1.5→×3.33；栓动猎枪 4.5 接手全表最慢枪之一）
        MaxRange = 900,          // 拟定待调（狙击：远而缓，末端仍高伤）
        FalloffStart = 450,      // 拟定待调
        FalloffFloor = 0.8,      // 拟定待调
        StockMeleeDamageMin = 6,        // 拟定待调（长枪身重砸）
        StockMeleeDamageMax = 11,       // 拟定待调
        StockMeleePenetration = 0.03,   // 拟定待调
        StockMeleeInterval = 1.6,       // 拟定待调
    };

    // ---- 天生武器 ----

    /// <summary>丧尸爪击：近战锐器，穿透 5%（用户拍板：锐器/0.05，沿用旧 Sim 值，非 Godot 钝器值）。名沿用"爪击"。</summary>
    public static Weapon ZombieClaw() => new()
    {
        Name = "爪击",
        DamageMin = 3,           // 拟定待调
        DamageMax = 9,           // 拟定待调
        Penetration = 0.05,
        DamageType = DamageType.Sharp,
        // 拟定待调（全局慢节奏必须含敌方，否则玩家武器单方放慢→人vs丧尸 91%→57%）：
        // 爪击 1.3→2.3（×1.77），Sim 复验人vs丧尸恢复 ~90%≈旧 91%（保持平衡而非改难度；
        // ×2→2.6 会让丧尸偏易 94%）。⚠ 注意 Godot 运行时丧尸冷却硬编码在 Zombie.cs(1.3)、不读此值，见回报决策项。
        AttackInterval = 2.3,
    };

    /// <summary>
    /// 玩家/敌方可用武器全集（15 种，含新增栓动猎枪，不含天生的丧尸爪击），Sim 聚合模拟按此顺序遍历。
    /// 顺序与旧 Sim 行内武器表一致，便于对照基线。
    /// </summary>
    public static IReadOnlyList<Weapon> Arsenal() => new[]
    {
        Dagger(), Shortsword(), Rapier(), Longsword(), Pitchfork(), Greatsword(),
        Club(), SpikeHammer(), Warhammer(),
        Zipgun(), Pistol(), Smg(), Rifle(), BoltActionHuntingRifle(), SniperRifle(),
    };
}
