namespace DeadSignal.Combat;

/// <summary>
/// 弹药类型键。引擎只认字符串键；对应的库存物品在消费层 <c>Materials</c> 目录里（弹药＝可堆叠材料）。
/// <para>
/// <b>粒度＝用户拍板</b>（原话）：「弹药类型简单分为：短子弹，中子弹，长子弹，鹿弹。
/// 手枪、冲锋枪用短子弹；自制猎枪、步枪用中子弹；狙击枪用长子弹」。加上弓弩吃的
/// <see cref="Arrow"/>（归 <c>Archery</c>），共 5 类。
/// </para>
/// <para>
/// <b>稀缺梯度就写在制作比里</b>（用户拍板）：同一个「子弹零件」能造
/// <b>8 发短子弹 / 5 发中子弹 / 4 发鹿弹 / 2 发长子弹</b> ——
/// 越强的枪，同样一份原料能喂它的次数越少。这就是"强，但打不起"的算式。
/// </para>
/// <para>
/// <b>另一重杠杆在 <see cref="Weapon.AmmoPerAttack"/></b>：连发几发就吃几发
/// （步枪二连发＝2 发/次，冲锋枪三连发＝3 发/次）；霰弹枪 8 颗弹丸却只吃 <b>1 发</b>鹿弹
/// （弹丸在同一发壳内，不乘弹药）。
/// </para>
/// </summary>
public static class AmmoKeys
{
    /// <summary>短子弹：手枪 / 冲锋枪。1 个子弹零件造 <b>8</b> 发——最便宜的枪弹。</summary>
    public const string ShortBullet = "ammo_short";

    /// <summary>中子弹：自制猎枪 / 步枪 / 栓动猎枪。1 个子弹零件造 <b>5</b> 发。</summary>
    public const string MediumBullet = "ammo_medium";

    /// <summary>长子弹：狙击枪。1 个子弹零件只造 <b>2</b> 发——全表最贵的一发。</summary>
    public const string LongBullet = "ammo_long";

    /// <summary>鹿弹：霰弹枪（一发壳内 8 颗弹丸，只扣 1 发）。1 个子弹零件造 <b>4</b> 发。</summary>
    public const string Buckshot = "ammo_buck";

    /// <summary>
    /// 箭：弓 / 弩。**类别键**——箭是「1 类别 : N 材料」（具体箭种见 <see cref="ArrowKeys"/>），
    /// 它本身不是一种材料、也没有配方。箭**不吃子弹零件、不吃火药**且**可回收**，是弓弩的立身之本。
    /// </summary>
    public const string Arrow = "ammo_arrow";
}

/// <summary>
/// 「子弹零件」——四种子弹的<b>唯一</b>共同原料（弹壳 / 底火 / 弹头坯这些没法用土办法糊弄的精密件）。
/// 用户拍板的制作比（1 个零件 → N 发）全部挂在这里：短 8 / 中 5 / 鹿 4 / 长 2。
/// </summary>
public static class BulletParts
{
    /// <summary>材料标识键（<c>Materials</c> 目录里的一条）。</summary>
    public const string Key = "bullet_parts";

    /// <summary>1 个子弹零件能造出多少发该弹药；不是子弹类弹药（箭）返回 0。</summary>
    public static int YieldPer(string ammoKey) => ammoKey switch
    {
        AmmoKeys.ShortBullet => 8,
        AmmoKeys.MediumBullet => 5,
        AmmoKeys.Buckshot => 4,
        AmmoKeys.LongBullet => 2,
        _ => 0,
    };
}

/// <summary>
/// 一次"射击"的弹药结算方案（纯数据）。按项目架构，判定走纯函数、**实扣由调用方做**
/// （同 <c>CraftingLogic</c> 出"能不能做/扣什么产什么"、调用方去 <c>InventoryStore</c> 实扣的模式）。
/// </summary>
/// <param name="CanFire">能否开火。<c>false</c> ＝ 弹药耗尽 → 调用方应降级为枪托近战（<see cref="Weapon.MeleeProfile"/>）。</param>
/// <param name="RoundsFired">本次射击实际打出的**发**数（≤ <see cref="Weapon.BurstCount"/>，被余弹夹紧）。</param>
/// <param name="AmmoSpent">本次射击应从库存扣除的弹药数（不吃弹药的武器恒为 0）。</param>
public readonly record struct ShotPlan(bool CanFire, int RoundsFired, int AmmoSpent)
{
    /// <summary>弹药耗尽：打不响。</summary>
    public static readonly ShotPlan Dry = new(false, 0, 0);
}

/// <summary>
/// 弹药消耗与回收的纯逻辑（零依赖，随机走可注入的 <see cref="IRandomSource"/>）。
/// 空间侧（子弹飞行、地上的箭能不能捡）归 Godot 实时层，本类只出纯函数。
/// </summary>
public static class AmmoLogic
{
    // 箭矢回收率**不在这里**（原先这儿有个 ArrowRecoveryRate = 0.60，已退役）：
    // 用户拍板「箭只有 25% 的几率不被损毁；读过《弓与箭之道》则是 50%」——回收率**取决于射手读没读过书**，
    // 那是弓弩的规则，不是弹药系统的规则。故单一真源移到 <see cref="Archery.ArrowRecoveryRate"/>。
    // 本类只保留下面那个**通用掷点器**（rate 是入参，谁调谁给），子弹/鹿弹不回收、压根不碰它。

    /// <summary>
    /// 排一次射击的弹药方案（**纯判定，不扣库存**）。
    /// <list type="bullet">
    /// <item>不吃弹药的武器（全部近战 + 枪托近战 profile）：恒可开火、扣 0 —— **既有武器零回归**。</item>
    /// <item>余弹为 0：<see cref="ShotPlan.Dry"/> → 调用方降级为枪托近战。</item>
    /// <item>余弹不够一整轮连发：打出剩下的几发（步枪只剩 1 发 → 打单发而非二连发），
    ///       而**不是**"凑不齐一轮就不开火"——否则最后一发会变成永久死库存，且"最后一颗子弹"该有它的戏。</item>
    /// </list>
    /// </summary>
    /// <param name="weapon">开火武器。</param>
    /// <param name="available">该武器所需弹药类型（<see cref="Weapon.AmmoKey"/>）在库存里的余量。</param>
    public static ShotPlan PlanShot(Weapon weapon, int available)
    {
        int burst = Math.Max(1, weapon.BurstCount);

        if (!weapon.UsesAmmo)
        {
            return new ShotPlan(true, burst, 0);
        }

        if (available <= 0)
        {
            return ShotPlan.Dry;
        }

        int rounds = Math.Min(burst, available);
        return new ShotPlan(true, rounds, rounds);
    }

    /// <summary>
    /// 掷一次箭矢回收：<paramref name="arrowsFired"/> 支箭**各自独立** roll 是否可回收，返回捡回的支数。
    /// 空间层（Godot）据此在落点生成可拾取的箭；引擎不涉几何。
    /// </summary>
    public static int RollArrowRecovery(int arrowsFired, double recoveryRate, IRandomSource rng)
    {
        int recovered = 0;
        for (int i = 0; i < arrowsFired; i++)
        {
            if (rng.Range(0, 1) < recoveryRate)
            {
                recovered++;
            }
        }

        return recovered;
    }
}
