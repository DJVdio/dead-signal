using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class BallisticsTests
{
    [Fact]
    public void ZeroSpread_AlwaysDeadCenter_NoRoll()
    {
        var rng = new SequenceRandomSource(); // 不应消耗
        Assert.Equal(0, Ballistics.SampleDeflectionDegrees(0, rng));
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Spread_SampleWithinCone()
    {
        var rng = new SequenceRandomSource(3.0); // 落在 [-5,5]
        Assert.Equal(3.0, Ballistics.SampleDeflectionDegrees(5, rng), 9);
    }

    [Fact]
    public void ShotDirection_AimPlusDeflection()
    {
        var rng = new SequenceRandomSource(-2.0);
        Assert.Equal(88.0, Ballistics.SampleShotDirectionDegrees(90, 5, rng), 9);
    }
}

public class DualWieldTests
{
    [Fact]
    public void AttackRate_DualWieldIsSeventyPercent()
    {
        Assert.Equal(7.0, DualWield.EffectiveAttackRate(10, dualWielding: true), 9);
        Assert.Equal(10.0, DualWield.EffectiveAttackRate(10, dualWielding: false), 9);
    }

    [Fact]
    public void Interval_DualWieldLengthens()
    {
        Assert.Equal(1.0 / 0.7, DualWield.EffectiveAttackInterval(1.0, dualWielding: true), 9);
    }

    [Fact]
    public void Spread_OnlyRangedDualWieldGetsIncrease()
    {
        Assert.Equal(3.75, DualWield.EffectiveSpreadDegrees(3.0, dualWielding: true, ranged: true), 9); // ×1.25
        Assert.Equal(3.0, DualWield.EffectiveSpreadDegrees(3.0, dualWielding: true, ranged: false), 9); // 近战不变
        Assert.Equal(3.0, DualWield.EffectiveSpreadDegrees(3.0, dualWielding: false, ranged: true), 9); // 单持不变
    }
}

/// <summary>
/// 负重三档 + 硬上限（**用户拍板**）：&lt;30kg 无影响 / 30~50kg 有 debuff / 50~80kg debuff 加重 / **不能超过 80kg**。
/// 基准人（无残缺、不饿、无专属加成）的上限就是 80kg，故这里直接按公斤数断言。
/// <para>
/// 🔴 [T45] 这本账现在**含装备**（武器 + 11 槽护甲），但三条线**不动**。用户原话：
/// 「**不改啊，就应当是 30/50/80。带装备出门，随便搜点就超 30 了。如果全身重甲+重武器（单板甲就 15 了），
/// 那出门就差不多 30 了，能搜的空间会很小。**」
/// ⇒ 负重的代价**不是"出门就减速"，而是「装备把你的搜刮余量吃掉了」**。见 <c>CarryLoadWiringTests</c> 的余量表。
/// </para>
/// <para>
/// 🔴 惩罚曲线也是**用户拍板的四个数**（不是"拟定待调"）：
/// 「50kg 减少 20% 移动速度和攻击速度；80kg 减少 80% 移动速度和 50% 攻击速度；30-50，50-80 线性变化」。
/// </para>
/// </summary>
public class LoadoutTests
{
    private static readonly double Limit = Loadout.CarryLimit(); // 基准人 = 80kg

    [Fact]
    public void UserSpec_TheThreeNumbersAre_30_50_80()
    {
        Assert.Equal(80.0, Loadout.BaseCarryLimitKg, 9);
        Assert.Equal(30.0, Loadout.FreeThresholdKg, 9);
        Assert.Equal(50.0, Loadout.StrainThresholdKg, 9);
        Assert.Equal(80.0, Limit, 9);
    }

    // ———————————— 🔴 用户拍板的惩罚曲线：四个锚点 + 两段线性 ————————————

    /// <summary>
    /// 🔴 <b>用户给的四个数，一个不许动</b>：50kg → 移速/攻速各 −20%；80kg → 移速 −80%、攻速 −50%。
    /// <para>移速在满载掉到 <b>0.20</b>（只剩两成速度）是**有意的**——贪多就基本走不动、被丧尸追上。别当笔误"修"。</para>
    /// </summary>
    [Theory]
    [InlineData(30.0, 1.00, 1.00)] // 免罚线：平坦段的终点，仍不罚
    [InlineData(50.0, 0.80, 0.80)] // 加重线：移速攻速一起 −20%
    [InlineData(80.0, 0.20, 0.50)] // 硬上限：移速 −80%、攻速 −50%
    public void UserSpec_TheFourAnchorsOfThePenaltyCurve(double kg, double expectSpeed, double expectAttack)
    {
        Assert.Equal(expectSpeed, Loadout.SpeedMultiplier(kg, Limit), 9);
        Assert.Equal(expectAttack, Loadout.AttackSpeedMultiplier(kg, Limit), 9);
    }

    /// <summary>两段都是**线性插值**（用户原话：「30-50，50-80 线性变化」）——取两个中点钉死。</summary>
    [Theory]
    [InlineData(40.0, 0.90, 0.90)] // [30,50] 中点：移速攻速同为 −10%
    [InlineData(65.0, 0.50, 0.65)] // [50,80] 中点：移速 −50%、攻速 −35%（两条曲线在此分道扬镳）
    public void UserSpec_BothSegmentsInterpolateLinearly(double kg, double expectSpeed, double expectAttack)
    {
        Assert.Equal(expectSpeed, Loadout.SpeedMultiplier(kg, Limit), 9);
        Assert.Equal(expectAttack, Loadout.AttackSpeedMultiplier(kg, Limit), 9);
    }

    /// <summary>免罚线两侧卡边界：29.9kg 一分不罚，30.1kg 已经开始掉（移速与攻速**同时**开始）。</summary>
    [Fact]
    public void TheFreeLine_IsSharp_PenaltyStartsTheMomentYouCrossIt()
    {
        Assert.Equal(1.0, Loadout.SpeedMultiplier(29.9, Limit), 9);
        Assert.Equal(1.0, Loadout.AttackSpeedMultiplier(29.9, Limit), 9);
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(29.9, Limit));

        Assert.True(Loadout.SpeedMultiplier(30.1, Limit) < 1.0);
        Assert.True(Loadout.AttackSpeedMultiplier(30.1, Limit) < 1.0);
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(30.1, Limit));
    }

    /// <summary>
    /// 🔴 <b>攻速现在从 30kg 起就罚</b> —— 旧口径「轻度档不罚攻速、背 30kg 挥剑没什么影响」**已被用户推翻**。
    /// <para>⚠️ 这条替换掉了原来的 <c>Between30And50kg_LightDebuff_MoveOnly</c>（名字里的 "MoveOnly" 已经是错的）。</para>
    /// </summary>
    [Fact]
    public void Between30And50kg_NowPenalisesAttackSpeedToo_NotJustMovement()
    {
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(40, Limit));
        Assert.True(Loadout.AttackSpeedMultiplier(40, Limit) < 1.0, "轻度档也罚攻速了（用户新口径）");
        Assert.Equal(Loadout.AttackSpeedAtStrain, Loadout.AttackSpeedMultiplier(50, Limit), 9);
    }

    [Fact]
    public void Under30kg_NoPenaltyAtAll()
    {
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(29, Limit));
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(30, Limit)); // 边界含
        Assert.Equal(1.0, Loadout.SpeedMultiplier(30, Limit), 9);
        Assert.Equal(1.0, Loadout.AttackSpeedMultiplier(30, Limit), 9);
    }

    [Fact]
    public void Between50And80kg_HeavyDebuff_MovementCollapses()
    {
        Assert.Equal(LoadoutTier.Strained, Loadout.TierOf(65, Limit));
        Assert.Equal(Loadout.SpeedAtLimit, Loadout.SpeedMultiplier(80, Limit), 9);
        Assert.Equal(Loadout.AttackSpeedAtLimit, Loadout.AttackSpeedMultiplier(80, Limit), 9);
    }

    /// <summary>
    /// 「debuff 加重」必须真的更陡 —— <b>但只对移速成立，攻速两档等陡</b>（用户拍板的曲线自然结果）。
    /// <para>
    /// 移速：轻档 20%/20kg = <b>1.0%/kg</b> → 重档 60%/30kg = <b>2.0%/kg</b>（**加速恶化** ✅ 严格更陡）
    /// 攻速：轻档 20%/20kg = <b>1.0%/kg</b> → 重档 30%/30kg = <b>1.0%/kg</b>（**匀速** ⇒ 只能要求"不更缓"）
    /// </para>
    /// <b>⇒ 负重压垮的首先是你的腿，不是你的手。</b> 这是这条曲线的性格，不是 bug。
    /// **绝不许为了让本护栏"更漂亮"去动用户拍板的那四个数。**
    /// </summary>
    [Fact]
    public void HeavyTierIsSteeperThanLightTier()
    {
        double lightBandKg = Loadout.StrainThresholdKg - Loadout.FreeThresholdKg;   // 20kg
        double heavyBandKg = Loadout.BaseCarryLimitKg - Loadout.StrainThresholdKg;  // 30kg

        double lightMovePerKg = (1.0 - Loadout.SpeedAtStrain) / lightBandKg;
        double heavyMovePerKg = (Loadout.SpeedAtStrain - Loadout.SpeedAtLimit) / heavyBandKg;
        Assert.True(heavyMovePerKg > lightMovePerKg, // 移速：**严格**更陡
            $"移速重度 {heavyMovePerKg}/kg 应陡于轻度 {lightMovePerKg}/kg");

        double lightAtkPerKg = (1.0 - Loadout.AttackSpeedAtStrain) / lightBandKg;
        double heavyAtkPerKg = (Loadout.AttackSpeedAtStrain - Loadout.AttackSpeedAtLimit) / heavyBandKg;
        Assert.True(heavyAtkPerKg >= lightAtkPerKg - 1e-9, // 攻速：**非严格**（用户曲线下两档等陡）
            $"攻速重度 {heavyAtkPerKg}/kg 不该缓于轻度 {lightAtkPerKg}/kg");

        // 而且腿确实垮得比手快：满载时移速的损失 > 攻速的损失
        Assert.True(1.0 - Loadout.SpeedAtLimit > 1.0 - Loadout.AttackSpeedAtLimit,
            "负重压垮的首先是腿（−80%），不是手（−50%）");
    }

    [Fact]
    public void Over80kg_IsNotAllowed()
    {
        Assert.True(Loadout.CanCarry(80, Limit));
        Assert.False(Loadout.CanCarry(80.1, Limit)); // 用户："不能超过 80kg"
    }

    [Fact]
    public void OverloadTier_OnlyReachableWhenLimitDropsMidTrip_AndCrawls()
    {
        // 硬上限拦住拾取，所以 >100% 只在**上限中途下降**时出现（关内断手/饿掉一档）：东西不消失，但你快走不动了
        double injured = Loadout.CarryLimit(0.5); // 断一手 → 40kg
        Assert.Equal(LoadoutTier.Overloaded, Loadout.TierOf(80, injured));
        Assert.True(Loadout.SpeedMultiplier(80, injured) < Loadout.SpeedAtLimit);
        Assert.Equal(0.10, Loadout.SpeedMultiplier(1000, Limit), 9); // 下限
    }

    /// <summary>
    /// 上限＝全员统一基数 × 承载能力 × authored 专属乘子。
    /// **没有"力量/体力"属性**（旧签名 CapacityFromStrength(strength) 的 strength 是虚构属性，已退役）。
    /// </summary>
    [Fact]
    public void CarryLimit_UniformBase_ScaledByCapability()
    {
        Assert.Equal(Loadout.BaseCarryLimitKg, Loadout.CarryLimit(), 9);       // 全员同一个基数
        Assert.Equal(40.0, Loadout.CarryLimit(0.5), 9);                        // 断一手 → 背不动一半
    }

    [Fact]
    public void Thresholds_ScaleWithTheLimit_NotHardcodedKilograms()
    {
        // 残缺把上限压到 40kg → 三档整体收紧到 15 / 25 / 40（而不是还按 30/50 算）
        double injured = Loadout.CarryLimit(0.5);
        Assert.Equal(15.0, Loadout.FreeThresholdFor(injured), 9);
        Assert.Equal(25.0, Loadout.StrainThresholdFor(injured), 9);
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(20, injured)); // 健全人背 20kg 毫无感觉，断手的人已经吃力
    }
}
