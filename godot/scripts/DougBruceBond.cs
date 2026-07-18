namespace DeadSignal.Godot;

// 本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SurvivorPerks.cs / GuardPost.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 角色**专属效果（authored perk）** 地基：不做通用技能系统（旧 SkillSet 已连根删除，勿复活），
// 每角色一套手写羁绊。本文件＝**道格 & 布鲁斯（犬）羁绊**——全部静态纯函数 + 常量，供接线层
// （synergy-wiring：伤害/光环/生产/视野系数）消费，落地施加归运行时空间层。
//
// 升级方式（用户口径原话）：「道格和布鲁斯都存活的时间」。故等级由**共同存活天数**推进；
// 一方死亡后：等级**冻结**（由调用方停止累加共同存活天数实现，本类不持久状态）+ 依赖伙伴的技能失效
// （各系数函数吃 dougAlive/bruceAlive/bothAlive 门控，伙伴死则回落到中性值，光环「一方死亡即永失」）。
// 当前阈值、倍率与站岗效率统一以 Wiki 配置为准；本文件只保留规则形态。
//
// 所有可调阈值/系数均以 Wiki 配置为准，形态锁定。

/// <summary>
/// 3 级光环（道格与布鲁斯相依为命）生效结果：是否激活 + 生产/受伤两系数（纯值对象）。
/// 未激活时各系数为中性值；激活时操作与受伤倍率读取 Wiki 配置。
/// </summary>
public readonly struct AuraEffect
{
    /// <summary>光环是否激活（3 级 + 两者皆存活 + 互相在光环半径内）。</summary>
    public bool IsActive { get; }
    /// <summary>生产效率兼容乘子；当前 authored 规则不再给生产加成，激活时保持 1.0。</summary>
    public float ProductionMult { get; }
    /// <summary>操作能力乘子（激活 = <see cref="DougBruceBond.AuraOperationMult"/>，否则 1.0）。</summary>
    public float OperationMult { get; }
    /// <summary>受到伤害乘子（激活 = <see cref="DougBruceBond.AuraDamageTakenMult"/>，否则 1.0）。</summary>
    public float DamageTakenMult { get; }

    public AuraEffect(bool isActive, float productionMult, float operationMult, float damageTakenMult)
    {
        IsActive = isActive;
        ProductionMult = productionMult;
        OperationMult = operationMult;
        DamageTakenMult = damageTakenMult;
    }

    /// <summary>旧三参数构造兼容入口：未有独立操作轴的旧调用保持操作中性。</summary>
    public AuraEffect(bool isActive, float productionMult, float damageTakenMult)
        : this(isActive, productionMult, 1.0f, damageTakenMult)
    {
    }

    /// <summary>未激活的中性结果（各系数皆 1.0）。</summary>
    public static readonly AuraEffect Inactive = new(false, 1.0f, 1.0f, 1.0f);
}

/// <summary>
/// 道格 &amp; 布鲁斯羁绊纯逻辑（零 Godot 依赖）。全静态纯函数 + Wiki 配置入口；不持久任何状态
/// （等级由调用方喂的「共同存活天数」经 <see cref="EvaluateLevel"/> 现算，死亡冻结＝调用方停喂）。
/// </summary>
public static class DougBruceBond
{
    // ── 升级阈值（共同存活天数，当前值以 Wiki 配置为准）──────────────────────
    // 入队即 1 级（daysBothAlive=0 → L1）；跨阈值升 2/3 级。
    /// <summary>升到 2 级所需的共同存活天数（当前值以 Wiki 配置为准）。</summary>
    public const int Level2Days = 5;
    /// <summary>升到 3 级所需的共同存活天数（当前值以 Wiki 配置为准）。</summary>
    public const int Level3Days = 12;

    // ── 技能系数（当前值以 Wiki 配置为准）──────────────────────────────────────
    /// <summary>1 级：道格**自带**视野角乘子（倍率以 Wiki 配置为准；不依赖布鲁斯，道格活即在）。</summary>
    public const float DougAngleBonusMult = 1.10f;
    /// <summary>1 级：布鲁斯视野角乘子（倍率以 Wiki 配置为准；源于道格带领，道格死则失效）。</summary>
    public const float BruceAngleBonusMult = 1.10f;
    /// <summary>2 级：布鲁斯视野**距离**乘子（倍率以 Wiki 配置为准；同样依赖道格存活）。</summary>
    public const float BruceRangeBonusMult = 1.10f;
    /// <summary>2 级：解锁道格为布鲁斯制作狗装备所需的羁绊等级（用户 L2 修订，替换原缠斗伤害条款）。</summary>
    public const int DogGearUnlockLevel = 2;
    /// <summary>3 级光环：道格操作能力乘子（倍率以 Wiki 配置为准）。</summary>
    public const float AuraOperationMult = 1.25f;
    /// <summary>旧版生产光环常量兼容入口；新 authored 规则的生产轴为中性。</summary>
    public const float AuraProductionMult = 1.0f;
    /// <summary>3 级光环：受到伤害乘子（倍率以 Wiki 配置为准）。</summary>
    public const float AuraDamageTakenMult = 0.85f;
    /// <summary>2 级：布鲁斯攻击速度乘子（倍率以 Wiki 配置为准）。</summary>
    public const float BruceAttackSpeedMult = 1.12f;
    /// <summary>2 级：布鲁斯移动速度乘子（倍率以 Wiki 配置为准）。</summary>
    public const float BruceMoveSpeedMult = 1.12f;
    /// <summary>3 级光环默认半径（空间参数以 Wiki 配置为准；供调用方作 <see cref="AuraActive"/> 的 auraRadius 缺省）。</summary>
    public const float DefaultAuraRadius = 160f;

    /// <summary>布鲁斯站岗效率（当前系数以 Wiki 配置为准，供 GuardPost 消费）。</summary>
    public const float BruceGuardEfficiency = 0.75f;

    /// <summary>
    /// 共同存活天数 → 羁绊等级（1/2/3）。入队即 1 级（≥1 保底）；≥<see cref="Level2Days"/> 升 2 级、
    /// ≥<see cref="Level3Days"/> 升 3 级。负数按 0 处理（仍 1 级）。等级冻结＝调用方在一方死亡后停止累加天数。
    /// </summary>
    public static int EvaluateLevel(int daysBothAlive)
    {
        if (daysBothAlive >= Level3Days) return 3;
        if (daysBothAlive >= Level2Days) return 2;
        return 1;
    }

    /// <summary>
    /// 道格视野角乘子（1 级自带）：有该羁绊（level≥1）即 <see cref="DougAngleBonusMult"/>，否则 1.0。
    /// **不依赖布鲁斯存活**（道格的视野角是他本人的天赋）。
    /// </summary>
    public static float DougAngleMult(int level)
        => level >= 1 ? DougAngleBonusMult : 1.0f;

    /// <summary>
    /// 布鲁斯视野角乘子（1 级）：level≥1 且**道格存活**时 <see cref="BruceAngleBonusMult"/>，否则 1.0
    /// （布鲁斯的机敏源于道格带领，道格死则加成失效）。
    /// </summary>
    public static float BruceAngleMult(int level, bool dougAlive)
        => level >= 1 && dougAlive ? BruceAngleBonusMult : 1.0f;

    /// <summary>
    /// 布鲁斯视野距离乘子（2 级解锁）：level≥2 且**道格存活**时 <see cref="BruceRangeBonusMult"/>，否则 1.0。
    /// </summary>
    public static float BruceRangeMult(int level, bool dougAlive)
        => level >= 2 && dougAlive ? BruceRangeBonusMult : 1.0f;

    /// <summary>布鲁斯 L2 攻击速度乘子：level≥2 且道格存活时读取配置，否则返回中性值。</summary>
    public static float BruceAttackSpeedMultiplier(int level, bool dougAlive)
        => level >= 2 && dougAlive ? BruceAttackSpeedMult : 1.0f;

    /// <summary>布鲁斯 L2 移动速度乘子：level≥2 且道格存活时读取配置，否则返回中性值。</summary>
    public static float BruceMoveSpeedMultiplier(int level, bool dougAlive)
        => level >= 2 && dougAlive ? BruceMoveSpeedMult : 1.0f;

    /// <summary>
    /// 2 级：能否让道格为布鲁斯制作狗装备（布制/皮制/口袋狗衣、铁皮/铁丝头甲）。
    /// 羁绊 level≥<see cref="DogGearUnlockLevel"/> 且**两者皆存活**时解锁——道格是制作者（死则无人可做，默认不能，待确认），
    /// 布鲁斯是受益者（死则狗装备无意义）。实际配方/材料/护甲值归 dog-gear 单，本函数只出解锁门控。
    /// 注：用户 L2 修订以本条替换了原缠斗伤害条款；原条款已退役，属于历史/非配置源记录（见 git 历史 DougDamageMult）。
    /// </summary>
    public static bool CanCraftDogGear(int level, bool bothAlive)
        => level >= DogGearUnlockLevel && bothAlive;

    /// <summary>
    /// 3 级光环（相依为命）：level≥3、**两者皆存活**（bothAlive）且互相距离 ≤ auraRadius 时激活
    /// （道格操作 ×<see cref="AuraOperationMult"/>、受伤 ×<see cref="AuraDamageTakenMult"/>）；否则 <see cref="AuraEffect.Inactive"/>。
    /// 「一方死亡即永失」＝ bothAlive 转 false 后不再激活（死亡不可逆，故永失）。距离边界含（≤）。
    /// </summary>
    public static AuraEffect AuraActive(int level, bool bothAlive, float distance, float auraRadius)
    {
        bool active = level >= 3 && bothAlive && auraRadius > 0f && distance <= auraRadius;
        return active
            ? new AuraEffect(true, 1.0f, AuraOperationMult, AuraDamageTakenMult)
            : AuraEffect.Inactive;
    }
}
