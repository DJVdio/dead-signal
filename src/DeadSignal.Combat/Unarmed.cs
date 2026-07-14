namespace DeadSignal.Combat;

/// <summary>
/// 空手近战规则（纯函数）。用户原话：<b>「空手和持弓近战都视作空手近战，造成钝伤」</b>。
/// <para>
/// 一句话规则：<b>近战时手里那把家伙能怎么打人，就怎么打；打不了人的，就用拳脚。</b>
/// <list type="bullet">
///   <item><b>空手</b>（无武器）→ <see cref="WeaponTable.Fists"/>：钝伤、低伤害、快冷却。</item>
///   <item><b>近战武器</b>（含天生的爪击/撕咬）→ 原样用它自己（长剑仍是锐伤，不被拉成钝伤）。</item>
///   <item><b>枪械</b> → 沿用既有的枪托近战 <see cref="Weapon.MeleeProfile"/>（<b>此次不改</b>）：枪有枪托，抡得动。</item>
///   <item><b>弓 / 弩</b> → 拳脚。弓不是钝器：没有"抡弓砸人"这种形态（真抡断的是弓），
///     手里那把射不出去的东西在近战里等于不存在，你能用的还是自己的拳头。</item>
/// </list>
/// </para>
/// <para>
/// <b>弩为何与弓同判</b>（引擎结构给出的答案，非引申）：枪托近战 profile（<c>StockMelee*</c> 一族字段）
/// 全表<b>只有 7 把枪填了</b>，8 把弓弩<b>一把都没填</b>——「有没有枪托可抡」这条既有的结构界线，
/// 恰好就把"枪"和"弓弩"分在了两边。故本规则不为弩开特例，一条
/// 「<c>MeleeProfile() ?? Fists()</c>」同时覆盖弓与弩：远程武器近战时，有枪托就抡枪托，没有就用拳头。
/// </para>
/// <para>
/// 本类只出<b>「这一下该用哪把武器结算」</b>的纯判定；真正的空间执行（贴脸距离判定、噪音广播、伤害施加）
/// 在 Godot 消费层（<c>Actor.TryAttack</c>）。
/// </para>
/// </summary>
public static class Unarmed
{
    /// <summary>
    /// 近战这一下实际用的武器：空手/弓/弩 → 拳脚；枪 → 枪托 profile；近战武器 → 它自己。
    /// </summary>
    /// <param name="held">当前持握的武器；<c>null</c> = 空手。</param>
    public static Weapon MeleeFor(Weapon? held)
    {
        if (held is null)
        {
            return WeaponTable.Fists();
        }

        if (!held.IsRanged)
        {
            return held;   // 近战武器（含爪击/撕咬）：原样打
        }

        // 远程武器近战：有枪托就抡枪托（枪），没枪托就用拳头（弓/弩）。
        return held.MeleeProfile() ?? WeaponTable.Fists();
    }

    /// <summary>
    /// 拿这把家伙打近战，是不是等于空手（＝空手、或持弓/弩）。供 UI/日志区分"抡枪托"与"上拳头"。
    /// </summary>
    public static bool IsUnarmedMelee(Weapon? held)
        => held is null || (held.IsRanged && !held.HasMeleeProfile);
}
