namespace DeadSignal.Godot;

/// <summary>
/// 守卫岗位类型（D 守卫防御战）。三种岗位各有一条主机制属性，数值全部"拟定待调"占位。
///  - 哨塔 <see cref="Watchtower"/>：+射程（登高远射）。实心障碍（走 AddSolid，会挖导航洞）。
///  - 屋顶平台 <see cref="RoofPlatform"/>：+视野/侦测半径（更早锁定来袭者）。实心障碍。
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
/// 由 <see cref="For"/> 按类型给出"拟定待调"占位值；接入战斗见 <c>Actor.ApplyGuardPost</c>。
/// </summary>
public readonly struct GuardPostStats
{
    /// <summary>射程加成（cartesian 像素）——叠加到守卫 <c>AttackRange</c>。哨塔专属。</summary>
    public float RangeBonus { get; init; }

    /// <summary>侦测半径加成（cartesian 像素）——叠加到守卫巡防锁敌半径。屋顶平台专属。</summary>
    public float SightBonus { get; init; }

    /// <summary>首发一击：驻守后对首个来袭目标立即无冷却打一击（一次性）。暗哨专属。</summary>
    public bool FirstStrike { get; init; }

    /// <summary>是否实心障碍：true=哨塔/屋顶平台（AddSolid 挖导航洞）；false=暗哨（非碰撞标记点）。</summary>
    public bool IsSolid { get; init; }

    /// <summary>
    /// 对守卫"有效交战距离"的加成 = 射程加成 + 视野加成。屋顶平台的视野加成据此真正延长
    /// 守卫的开火/交战距离（登高远射有用），而非仅提前锁定。接入见 <c>Actor.ApplyGuardPost</c>。
    /// </summary>
    public float EngageRangeBonus => RangeBonus + SightBonus;

    /// <summary>岗位中文名（面板/日志显示用）。</summary>
    public string DisplayName { get; init; }

    public static GuardPostStats For(GuardPostKind kind) => kind switch
    {
        GuardPostKind.Watchtower => new GuardPostStats
        {
            RangeBonus = 140f, SightBonus = 0f, FirstStrike = false, IsSolid = true, DisplayName = "哨塔",
        },
        GuardPostKind.RoofPlatform => new GuardPostStats
        {
            RangeBonus = 0f, SightBonus = 180f, FirstStrike = false, IsSolid = true, DisplayName = "屋顶平台",
        },
        GuardPostKind.HiddenPost => new GuardPostStats
        {
            RangeBonus = 0f, SightBonus = 0f, FirstStrike = true, IsSolid = false, DisplayName = "暗哨",
        },
        _ => new GuardPostStats { DisplayName = "岗位" },
    };
}
