namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威武器数据源。Godot 消费层、Sim 聚合模拟、Sim 对决战报全部从此读取，
/// 消除此前 CombatData / Sim.Program / Sim.DuelReport 三处各自维护武器表的数据源分裂。
///
/// 数值口径：重叠武器（匕首/手枪/爪击）统一采用 <b>Godot 侧现行值</b>；仅 Sim 侧存在的武器
/// （短剑/长剑/重剑/棍棒/尖头锤/破甲锤/土制枪/冲锋枪/步枪/狙击枪）沿用其原值。
/// 穿透/伤害类型来自设计文档第 5 节；伤害区间/攻速为原型期<b>拟定待调</b>，靠 Sim 拉表校准。
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
        AttackInterval = 0.7,    // 拟定待调
    };

    /// <summary>短剑：近战锐器，穿透 12%。</summary>
    public static Weapon Shortsword() => new()
    {
        Name = "短剑",
        DamageMin = 6,           // 拟定待调
        DamageMax = 20,          // 拟定待调
        Penetration = 0.12,
        DamageType = DamageType.Sharp,
        AttackInterval = 0.9,    // 拟定待调
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
        AttackInterval = 0.8,    // 拟定待调（略快于短剑 0.9）
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
        AttackInterval = 1.1,    // 拟定待调
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
        AttackInterval = 1.4,    // 拟定待调
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
        AttackInterval = 1.2,    // 拟定待调（农具笨重，略慢于长剑 1.1）
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
        AttackInterval = 0.9,    // 拟定待调
    };

    /// <summary>尖头锤：近战钝器，穿透 5%。</summary>
    public static Weapon SpikeHammer() => new()
    {
        Name = "尖头锤",
        DamageMin = 12,          // 拟定待调
        DamageMax = 16,          // 拟定待调
        Penetration = 0.05,
        DamageType = DamageType.Blunt,
        AttackInterval = 1.2,    // 拟定待调
    };

    /// <summary>破甲锤：近战钝器，穿透 20%（破甲路线）。重型双手武器。</summary>
    public static Weapon Warhammer() => new()
    {
        Name = "破甲锤",
        DamageMin = 20,          // 拟定待调
        DamageMax = 28,          // 拟定待调
        Penetration = 0.20,
        DamageType = DamageType.Blunt,
        TwoHanded = true,        // 拟定待调
        AttackInterval = 1.8,    // 拟定待调
    };

    // ---- 远程 ----

    /// <summary>土制枪：远程锐器，穿透 10%，误差角大。单手；枪托可贴脸钝击。</summary>
    public static Weapon Zipgun() => new()
    {
        Name = "土制枪",
        DamageMin = 8,           // 拟定待调
        DamageMax = 16,          // 拟定待调
        Penetration = 0.10,
        DamageType = DamageType.Sharp,
        IsRanged = true,
        BaseSpreadDegrees = 8,   // 拟定待调
        AttackInterval = 2.5,    // 拟定待调
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
        AttackInterval = 0.5,    // 拟定待调
        MaxRange = 200,          // 拟定待调（手枪：近而陡）
        FalloffStart = 55,       // 拟定待调
        FalloffFloor = 0.5,      // 拟定待调
        StockMeleeDamageMin = 3,        // 拟定待调（手枪柄砸击）
        StockMeleeDamageMax = 6,        // 拟定待调
        StockMeleePenetration = 0.02,   // 拟定待调
        StockMeleeInterval = 1.2,       // 拟定待调
    };

    /// <summary>冲锋枪：远程锐器，穿透 18%，攻速极快。双手抵肩；枪托可贴脸钝击。</summary>
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
        AttackInterval = 0.1,    // 拟定待调
        MaxRange = 280,          // 拟定待调（冲锋枪：中距，衰减中等）
        FalloffStart = 70,       // 拟定待调
        FalloffFloor = 0.45,     // 拟定待调
        StockMeleeDamageMin = 4,        // 拟定待调（枪托钝击）
        StockMeleeDamageMax = 7,        // 拟定待调
        StockMeleePenetration = 0.02,   // 拟定待调
        StockMeleeInterval = 1.3,       // 拟定待调
    };

    /// <summary>步枪：远程锐器，穿透 21%。双手抵肩；枪托可贴脸钝击。</summary>
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
        AttackInterval = 0.8,    // 拟定待调
        MaxRange = 550,          // 拟定待调（步枪：远而缓）
        FalloffStart = 200,      // 拟定待调
        FalloffFloor = 0.6,      // 拟定待调
        StockMeleeDamageMin = 6,        // 拟定待调（枪托重砸）
        StockMeleeDamageMax = 10,       // 拟定待调
        StockMeleePenetration = 0.03,   // 拟定待调
        StockMeleeInterval = 1.5,       // 拟定待调
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
        AttackInterval = 1.5,    // 拟定待调
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
        AttackInterval = 1.3,    // 拟定待调
    };

    /// <summary>
    /// 玩家/敌方可用武器全集（14 种，不含天生的丧尸爪击），Sim 聚合模拟按此顺序遍历。
    /// 顺序与旧 Sim 行内武器表一致，便于对照基线。
    /// </summary>
    public static IReadOnlyList<Weapon> Arsenal() => new[]
    {
        Dagger(), Shortsword(), Rapier(), Longsword(), Pitchfork(), Greatsword(),
        Club(), SpikeHammer(), Warhammer(),
        Zipgun(), Pistol(), Smg(), Rifle(), SniperRifle(),
    };
}
