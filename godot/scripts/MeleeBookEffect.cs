using System;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ApparelSlots.cs / CraftWorkTime.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// [A4] 书 → **近战攻速**被动的**消费层乘子汇总**（纯逻辑）。范式同 <see cref="CraftWorkTime"/>「工时乘子」/
/// <c>ApparelCatalog.ApparelEffectMultiplier</c>「穿戴乘子」：把每条"读过某书 → 持某武器 → 攻速%"的加成集中在**一处**
/// 算成一个「出手间隔乘子」（&gt;1 更慢、&lt;1 更快），由消费层 <c>Actor.EffectiveAttackInterval</c> 乘上去。
///
/// <para><b>现阶段唯一来源</b>：《进阶木匠技术》→ 持消防斧 → 攻速效果见 Wiki 配置表。
/// 该书原有的其它效果（解锁配方、制作家具效果）各在各的通路，本类不覆盖它们。</para>
///
/// <para><b>为什么在消费层而不在零依赖引擎</b>：本乘子的输入是"持械者读过什么书"——Sim 的 <c>Duel</c>/<c>Arena</c>
/// 拿的是 base <c>Weapon</c>、根本没有"持械者已读书集"这个入参 ⇒ 结算路径读不到本乘子 ⇒ 既有武器×护甲基线**零漂移**。
/// 引擎的 <c>Weapon.AttackInterval</c> 一字未改，本类只在 Godot 运行时层把乘子叠上去。</para>
///
/// <para><b>乘算，禁加算</b>（项目铁律）：多条来源应在此**连乘**（当前仅一条）。武器名不硬编码字面量——
/// 权威取 <see cref="WeaponTable.Axe"/> 的 Name（config 改名不漏）。</para>
/// </summary>
public static class MeleeBookEffect
{
    /// <summary>《进阶木匠技术》持消防斧的攻速加成，具体值见 Wiki 配置表。乘算，禁加算。</summary>
    public const double AdvancedCarpentryAxeAttackSpeedMultiplier = 1.08;

    /// <summary>消防斧的**权威武器名**（不硬编码字面量，随 <see cref="WeaponTable"/>/config 走）。</summary>
    private static readonly string AxeName = WeaponTable.Axe().Name;

    /// <summary>
    /// 某持械者手持 <paramref name="weaponName"/> 时的**出手间隔乘子**（1.0=无加成 ⇒ 零回归）：
    /// 读过《进阶木匠技术》且持消防斧 ⇒ 应用 Wiki 配置的间隔乘子。其余组合使用中性值。
    /// 多条来源在此连乘（当前仅一条）。
    /// </summary>
    /// <param name="weaponName">当前生效近战武器名（消费层传 <c>Actor.CurrentAttackWeapon.Name</c>）。</param>
    /// <param name="isBookRead">该持械者是否读过某 bookId（调用方＝其 <c>Pawn.HasReadBook</c>；非 Pawn 单位传恒 false）。</param>
    public static double AttackIntervalMultiplier(string weaponName, Func<string, bool> isBookRead)
    {
        double mult = 1.0;
        if (weaponName == AxeName && isBookRead != null && isBookRead(RecipeBook.AdvancedCarpentryBookId))
            mult /= AdvancedCarpentryAxeAttackSpeedMultiplier;
        return mult;
    }
}
