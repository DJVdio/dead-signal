using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 守卫岗位类型（D 守卫防御战）。三种岗位各有一条主机制属性，数值全部"拟定待调"占位。
///  - 哨塔 <see cref="Watchtower"/>：远程 +10% 射程/视野 + 围栏 25% 抵挡远程。实心障碍（走 AddSolid，会挖导航洞）。
///  - 屋顶平台 <see cref="RoofPlatform"/>：远程 +10% 射程/视野（无抵挡）。实心障碍。
///  - 暗哨 <see cref="HiddenPost"/>：首发（隐蔽先发/先手一击）。非碰撞驻守标记点，不挡路。
/// </summary>
public enum GuardPostKind
{
    Watchtower,
    RoofPlatform,
    HiddenPost,
}

/// <summary>
/// 单个岗位的机制属性（纯数据、无 Godot 依赖，可 Link 进单测）。
/// 由 <see cref="For"/> 按类型给出"拟定待调"占位值；接入战斗见 <c>Actor.ApplyGuardPost</c>，
/// 纯算法（有效射程/视野/抵挡掷免/首发 reach）见 <see cref="GuardPostMath"/>。
/// </summary>
public readonly struct GuardPostStats
{
    /// <summary>
    /// 远程射程倍率——作用到武器 <c>MaxRange</c>（仅远程守卫；近战无 MaxRange 开火模型，不加射程）。
    /// 哨塔/屋顶=1.10（登高远射 +10%）；暗哨=1（无）。接入见 <c>Actor.ApplyGuardPost</c>。
    /// </summary>
    public float RangeMultiplier { get; init; }

    /// <summary>
    /// 视野/侦测半径倍率——作用到守卫巡防锁敌半径 <c>GuardSightRadius</c>。
    /// 哨塔/屋顶=1.10（+10%，更早锁定来袭者）；暗哨=1（无）。
    /// </summary>
    public float SightMultiplier { get; init; }

    /// <summary>
    /// 围栏远程抵挡几率 [0,1]——命中该岗位守卫的**远程**攻击按此几率整发打在围栏上、对角色完全无效
    /// （非减伤、整发免掉；近战不抵挡）。哨塔=0.25；屋顶/暗哨=0。掷判见 <see cref="GuardPostMath.RangedBlocked"/>。
    /// </summary>
    public float BlockChance { get; init; }

    /// <summary>首发一击：驻守后对首个来袭目标立即无冷却打一击（一次性）。暗哨专属。</summary>
    public bool FirstStrike { get; init; }

    /// <summary>是否实心障碍：true=哨塔/屋顶平台（AddSolid 挖导航洞）；false=暗哨（非碰撞标记点）。</summary>
    public bool IsSolid { get; init; }

    /// <summary>岗位中文名（面板/日志显示用）。</summary>
    public string DisplayName { get; init; }

    public static GuardPostStats For(GuardPostKind kind) => kind switch
    {
        GuardPostKind.Watchtower => new GuardPostStats
        {
            RangeMultiplier = 1.10f, SightMultiplier = 1.10f, BlockChance = 0.25f,
            FirstStrike = false, IsSolid = true, DisplayName = "哨塔",
        },
        GuardPostKind.RoofPlatform => new GuardPostStats
        {
            RangeMultiplier = 1.10f, SightMultiplier = 1.10f, BlockChance = 0f,
            FirstStrike = false, IsSolid = true, DisplayName = "屋顶平台",
        },
        GuardPostKind.HiddenPost => new GuardPostStats
        {
            RangeMultiplier = 1f, SightMultiplier = 1f, BlockChance = 0f,
            FirstStrike = true, IsSolid = false, DisplayName = "暗哨",
        },
        _ => new GuardPostStats { RangeMultiplier = 1f, SightMultiplier = 1f, DisplayName = "岗位" },
    };
}

/// <summary>
/// 岗位加成的纯算法（无 Godot 依赖、可 Link 进单测）。把"百分比加成算有效射程/视野、远程抵挡掷免、
/// 暗哨首发 reach 选择"从 <c>Actor</c> 的空间层抽出，锁死"拟定待调"数值方向。
/// </summary>
public static class GuardPostMath
{
    /// <summary>
    /// 岗位射程倍率下的**等效距离**：把实际距离压回武器原生射程曲线（distance / 倍率）。
    /// 倍率&gt;1 → 有效开火射程与衰减曲线整体拉长（+10% → 距离 110 视作 100，仍满伤段/未截断）；
    /// 倍率=1（非守卫/近战）→ 原样；倍率&le;0 → 兜底原样。据此复用 <see cref="Ballistics"/> 判定，无需改引擎。
    /// </summary>
    public static double EffectiveRangeDistance(double distance, float rangeMultiplier)
        => rangeMultiplier > 0f ? distance / rangeMultiplier : distance;

    /// <summary>岗位视野倍率下的有效锁敌半径 = 基础 × 倍率。</summary>
    public static float EffectiveSight(float baseSight, float sightMultiplier)
        => baseSight * sightMultiplier;

    /// <summary>
    /// 围栏远程抵挡：<paramref name="blockChance"/>&gt;0 时掷 [0,1) 判定本发是否整发免掉（对角色完全无效）。
    /// &le;0 恒不免。随机走可注入 <see cref="IRandomSource"/>（测试用 SequenceRandomSource 复现）。
    /// </summary>
    public static bool RangedBlocked(float blockChance, IRandomSource rng)
        => blockChance > 0f && rng.Range(0.0, 1.0) < blockChance;

    /// <summary>
    /// 暗哨首发的 reach（中心距上限）：远程用 <c>MaxRange</c>×射程倍率（无 MaxRange 的罕见远程武器视作无限远）；
    /// 近战用 <paramref name="meleeRange"/>（调用方再叠双方半径，对齐近战交战边缘口径）。
    /// </summary>
    public static double FirstStrikeReach(bool isRanged, double? maxRange, float meleeRange, float rangeMultiplier)
    {
        if (isRanged)
        {
            return maxRange is double mr && mr > 0 ? mr * rangeMultiplier : double.PositiveInfinity;
        }
        return meleeRange;
    }
}
