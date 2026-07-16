using System.Numerics;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 坐标一律用 System.Numerics.Vector2，与 CoverLogic/VisionLogic 同口径；消费方转换即可。

/// <summary>
/// [T69] 两条**防御型改装**的承伤否决判定（纯函数，零 Godot 依赖）——照 <see cref="CoverLogic.Negates"/> /
/// <c>GuardPostMath.RangedBlocked</c> 的先例：给定几率 + 几何 + 可注入 <see cref="IRandomSource"/>，出"这次是否整发无效"。
///
/// <para><b>为什么是两条独立函数、而不是一个通用否决</b>：两者的**触发几何完全不同**——
/// 护手挡格看的是"命中部位是不是持械那只手"（**选部位之后**才知道），弩盾看的是"射手在不在我正面锥内"
/// （**选部位之前**、整发否决，与半身掩体同层）。接线点也因此分属两处（见各函数注）。</para>
///
/// <para><b>零漂移铁律</b>：几率 ≤ 0（绝大多数武器不带这两条改装）时**一个随机数都不取**（短路求值），
/// 随机流与既有路径逐位一致。</para>
/// </summary>
public static class WeaponModDefense
{
    /// <summary>
    /// **护手挡格**否决：仅当本次命中**选中了持械手**（连同手指）为受击部位时，按 <paramref name="negateChance"/>
    /// 掷一次"整发无效"。非持械手命中 / 无护手挡格（chance ≤ 0）→ 不掷点、不否决。
    ///
    /// <para><b>接线点＝<c>CombatEngine.ResolveHit</c></b>：部位在那里由体积加权选出、伤害在其后才施加，
    /// 故"按命中部位否决"必须落在选部位之后、结算之前（承伤入口 <c>Actor.ReceiveAttack</c> 只知"整次攻击"、不知打哪个部位）。</para>
    /// </summary>
    /// <param name="negateChance">护手挡格否决几率（护手挡格 = 0.5；无此改装 = 0）。</param>
    /// <param name="hitIsWeaponHand">本次命中选中的部位是不是持械手（含其手指）。</param>
    public static bool HandGuardNegates(double negateChance, bool hitIsWeaponHand, IRandomSource rng)
        => hitIsWeaponHand && negateChance > 0.0 && rng.Range(0.0, 1.0) < negateChance;

    /// <summary>
    /// **弩盾**否决：仅当来袭是**远程**、且射手落在防御方**正面锥**（半角 <paramref name="coneHalfAngleDeg"/>，
    /// 弩盾 = 60° ⇒ 全张角 120°）内时，按 <paramref name="negateChance"/> 掷一次"整发无效"。
    /// 近战 / 无弩盾（chance ≤ 0）/ 射手在锥外（绕到侧后）→ 不掷点、不否决。
    ///
    /// <para><b>接线点＝<c>Actor.ReceiveAttack</c></b>：与半身掩体 <see cref="CoverLogic.Negates"/> / 哨塔围栏
    /// <c>GuardPostMath.RangedBlocked</c> 同一层——整发否决、不结算伤害。</para>
    ///
    /// <para><b>方向数学复用 <see cref="VisionLogic.CanSee"/></b>：把"射手在我正面 120° 内"表达成
    /// "以我为观察者、我的朝向为视线、射手为目标，落在半角 60° 的锥里"（视距设无限，只判角度、不判遮挡）。</para>
    /// </summary>
    /// <param name="ranged">来袭是否远程（近战恒不否决）。</param>
    /// <param name="negateChance">弩盾否决几率（弩盾 = 0.25；无此改装 = 0）。</param>
    /// <param name="coneHalfAngleDeg">正面锥半角（度；弩盾 = 60）。</param>
    /// <param name="defenderPos">防御方（举弩者）位置。</param>
    /// <param name="defenderFacing">防御方朝向单位向量（无需归一，内部处理）。</param>
    /// <param name="shooterPos">射手位置。</param>
    public static bool FrontalRangedNegates(
        bool ranged, double negateChance, double coneHalfAngleDeg,
        Vector2 defenderPos, Vector2 defenderFacing, Vector2 shooterPos, IRandomSource rng)
    {
        if (!ranged || negateChance <= 0.0)
        {
            return false;   // 零漂移短路：非远程 / 无弩盾 ⇒ 不动随机流
        }

        // 射手是否落在防御方正面锥内（视距无限，只看角度）。锥外（侧后偷袭）→ 弩盾挡不住。
        var cone = new VisionLogic.VisionCone(float.MaxValue, (float)coneHalfAngleDeg);
        if (!VisionLogic.CanSee(defenderPos, defenderFacing, shooterPos, cone, occluded: false))
        {
            return false;   // 不在正面锥 ⇒ 不掷点
        }

        return rng.Range(0.0, 1.0) < negateChance;
    }
}
