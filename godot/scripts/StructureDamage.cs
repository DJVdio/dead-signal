using System;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 「砸墙伤害由武器派生」的纯逻辑（无 Godot 依赖，Link 进单测与 Sim）。
///
/// <para><b>规则一句话</b>：<c>每击伤害 = 砸墙有效武器的平均伤害 × 该武器的砸墙系数</c>，<c>节奏 = 砸墙有效武器的出手间隔</c>。</para>
///
/// <para>
/// <b>它修的是什么</b>：此前砸墙伤害是两个写死的常数（丧尸 12/爪、劫掠者 25/次），**完全不读武器表**。后果：
/// 破甲锤砸墙 = 匕首砸墙（都是 25，对不起它「铁皮罐头也照开不误」那句介绍）；持枪的劫掠者破墙**比拿匕首的慢 43%**
/// （伤害同为常数，节奏却取自武器）；武器表怎么改都传导不到围墙。现在武器表是唯一真源，改一个数字，围墙立刻感知。
/// </para>
///
/// <para>
/// <b>「砸墙有效武器」（<see cref="Bashing"/>）</b>：结构没有护甲、没有部位、不吃穿透，只有一个血条——所以砸墙从来
/// 不是"用你的杀伤方式打墙"，而是"用你手上这件东西去撞它"。
/// <list type="bullet">
/// <item><b>枪械</b> → 取 <see cref="Weapon.MeleeProfile"/>（<b>抡枪托</b>）：伤害、节奏全取枪托 profile。子弹打不穿
/// 承重墙，但一支步枪的枪托砸门是真事。这条同时修好了"匕首拆铁皮大门比手枪快"的不自洽——枪托是钝的金属块，
/// 比刀刃管用。</item>
/// <item><b>弓弩</b> → 没有枪托可抡，取弓本体（箭伤）× 全表最低系数 0.1：射箭砸墙是全游戏最徒劳的行为。</item>
/// <item><b>近战/天生武器</b> → 取本体。</item>
/// </list>
/// </para>
///
/// <para>
/// <b>系数是数据不是代码</b>：每把武器一格「砸墙系数」（<see cref="Weapon.StructureFactor"/>），用户在
/// <c>docs/weapons-calc.xlsx</c>『武器表』直接调。本类只写规则（怎么算），不写数值（算多少）。
/// </para>
/// </summary>
public static class StructureDamage
{
    /// <summary>
    /// 钝器缺省砸墙系数（未在武器表填「砸墙系数」时兜底）。钝器天生就是对付死物的：力全砸在结构上，不靠切开什么。
    /// 拟定待调。
    /// </summary>
    public const double DefaultBluntFactor = 1.2;

    /// <summary>
    /// 锐器缺省砸墙系数（未填时兜底）。拿刀砍墙是徒劳——刃口的杀伤全建立在"切开血肉"上，木头铁皮不吃这一套。
    /// 拟定待调。
    /// </summary>
    public const double DefaultSharpFactor = 0.4;

    /// <summary>
    /// 「砸墙有效武器」：枪械 → 其枪托近战 profile（抡枪托）；其余（近战/弓弩/天生武器）→ 武器本体。
    /// 砸墙的**伤害基数与节奏**都取自它。见类注。
    /// </summary>
    public static Weapon Bashing(Weapon weapon) => weapon.MeleeProfile() ?? weapon;

    /// <summary>
    /// 这把武器的砸墙系数：优先取武器表里填的 <see cref="Weapon.StructureFactor"/>；未填则按**砸墙有效武器**的
    /// 伤害类型兜底（枪未填 → 走枪托的钝器档，符合直觉）。
    /// </summary>
    public static double FactorFor(Weapon weapon)
    {
        if (weapon.StructureFactor is double f)
        {
            return Math.Max(0, f);
        }
        return Bashing(weapon).DamageType == DamageType.Blunt ? DefaultBluntFactor : DefaultSharpFactor;
    }

    /// <summary>砸墙每击伤害 = 砸墙有效武器平均伤害 × 砸墙系数（<b>不取整</b>，遵精度通则）。</summary>
    public static double PerHit(Weapon weapon)
    {
        Weapon bash = Bashing(weapon);
        double avg = (bash.DamageMin + bash.DamageMax) / 2.0;
        return Math.Max(0, avg * FactorFor(weapon));
    }

    /// <summary>砸墙节奏（秒/击）= 砸墙有效武器的出手间隔（枪 = 枪托间隔，不是开火间隔）。</summary>
    public static double Interval(Weapon weapon) => Bashing(weapon).AttackInterval;

    /// <summary>砸墙效率（点/秒）。诊断/校准用（Sim 的 <c>wallcal</c>）；运行时按 <see cref="PerHit"/> + <see cref="Interval"/> 逐击结算。</summary>
    public static double PerSecond(Weapon weapon)
    {
        double interval = Interval(weapon);
        return interval > 0 ? PerHit(weapon) / interval : 0;
    }

    /// <summary>砸穿一处 <paramref name="maxHp"/> 血的结构需要几击（向上取整；单个攻击者）。</summary>
    public static int HitsToBreach(Weapon weapon, double maxHp)
    {
        double perHit = PerHit(weapon);
        return perHit > 0 ? (int)Math.Ceiling(maxHp / perHit) : int.MaxValue;
    }

    /// <summary>砸穿一处 <paramref name="maxHp"/> 血的结构需要几秒（单个攻击者；伤害线性叠加，N 个攻击者除以 N）。</summary>
    public static double SecondsToBreach(Weapon weapon, double maxHp)
        => HitsToBreach(weapon, maxHp) * Interval(weapon);
}
