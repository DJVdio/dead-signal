using System.Numerics;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T69] 五条新改装（护手挡格 / 弓臂缠手 / 复合弓臂 / 重磅弓弦 / 弩盾）的数值 + 两条防御型否决钩子
/// （<see cref="WeaponModDefense"/>）的纯逻辑焊死。所有数值"拟定待调"，本文件钉的是**规则形态与倍率关系**。
/// </summary>
public class WeaponModDefenseTests
{
    // ════════════════════════════ 五条改装的数值 ════════════════════════════

    [Fact]
    public void 弓臂缠手_攻速加5百分比_无重量无其他()
    {
        WeaponMod m = WeaponModCatalog.LimbWrap();
        Assert.Equal(WeaponPart.LimbWrap, m.Part);
        Assert.Equal(1.0, m.WeightMultiplier);                       // 无重量改动
        Assert.Equal(0.0, m.HandGuardNegateChance);
        Assert.Equal(0.0, m.FrontalRangedNegateChance);

        Weapon bow = WeaponTable.ShortBow();
        Weapon after = WeaponMods.ApplyMods(bow, new[] { m }).Weapon;
        Assert.Equal(bow.AttackInterval * 0.95, after.AttackInterval, 6);   // 攻速 +5% ⇒ 间隔 ×0.95
        Assert.Equal(bow.DamageMax, after.DamageMax, 6);                    // 别的不动
    }

    [Fact]
    public void 弓臂缠手_与复合弓臂不互斥_可同装一把弓()
    {
        Weapon bow = WeaponTable.ShortBow();   // 两条都勾了短弓
        WeaponMod wrap = WeaponModCatalog.LimbWrap();     // part = 缠手(LimbWrap)
        WeaponMod compound = WeaponModCatalog.CompoundLimbs();   // part = 弓(Bow)
        Assert.NotEqual(wrap.Part, compound.Part);        // 两个不同部位 ⇒ 不占位冲突

        // 同装不抛（部位不冲突）；两者效果连乘
        Weapon after = WeaponMods.ApplyMods(bow, new[] { wrap, compound }).Weapon;
        Assert.Equal(bow.AttackInterval * 0.95 * 1.06, after.AttackInterval, 6);   // 缠手×0.95 · 复合×1.06
        Assert.Equal(bow.FlightSpeed * 1.12, after.FlightSpeed, 6);                // 复合弓臂的飞速 +12%
    }

    [Fact]
    public void 复合弓臂_五项含飞速12百分比_全乘算()
    {
        WeaponMod m = WeaponModCatalog.CompoundLimbs();
        Assert.Equal(WeaponPart.Bow, m.Part);
        Assert.Equal(1.15, m.WeightMultiplier);                     // 重量 +15%

        Weapon bow = WeaponTable.ShortBow();
        Weapon a = WeaponMods.ApplyMods(bow, new[] { m }).Weapon;
        Assert.Equal(bow.AttackInterval * 1.06, a.AttackInterval, 6);   // 攻速 −6% ⇒ 间隔 ×1.06
        Assert.Equal(bow.DamageMin * 1.08, a.DamageMin, 6);            // 伤害 +8%
        Assert.Equal(bow.DamageMax * 1.08, a.DamageMax, 6);
        Assert.Equal(bow.Penetration * 1.08, a.Penetration, 6);       // 穿透 +8%
        Assert.Equal(bow.FlightSpeed * 1.12, a.FlightSpeed, 6);       // 飞速 +12%
    }

    [Fact]
    public void 复合弓臂飞速_与弓与箭之道书的20百分比自动连乘()
    {
        // 飞速轴由 modweapon-axis 落地：改装 ×1.12 与书 ×1.20 走同一 FlightSpeed 字段 ⇒ 结构上必然连乘。
        Weapon bow = WeaponTable.ShortBow();
        Weapon compounded = WeaponMods.ApplyMods(bow, new[] { WeaponModCatalog.CompoundLimbs() }).Weapon;
        Assert.Equal(bow.FlightSpeed * 1.12, compounded.FlightSpeed, 6);
        // 书的 +20% 由 Archery/HasReadArcheryBook 在消费层叠加（不同轴、同字段），此处只钉改装这一乘子成立。
    }

    [Fact]
    public void 重磅弓弦_含飞速12百分比与末端伤害系数提高()
    {
        WeaponMod m = WeaponModCatalog.HeavyBowstring();
        Assert.Equal(WeaponPart.String, m.Part);
        Assert.Equal(1.0, m.WeightMultiplier);   // wiki 未给重量

        Weapon bow = WeaponTable.ShortBow();
        Assert.NotNull(bow.FalloffFloor);
        Weapon a = WeaponMods.ApplyMods(bow, new[] { m }).Weapon;
        Assert.Equal(bow.AttackInterval * 1.06, a.AttackInterval, 6);   // 攻速 −6%
        Assert.Equal(bow.DamageMax * 1.04, a.DamageMax, 6);            // 伤害 +4%
        Assert.Equal(bow.BaseSpreadDegrees * 0.92, a.BaseSpreadDegrees, 6);   // 散布 −8%
        Assert.Equal(bow.FlightSpeed * 1.12, a.FlightSpeed, 6);       // 飞速 +12%
        // 衰减率 −18% ≈ 末端伤害系数提高（拟定待 Sim 校准）：FalloffFloor 变大（打远了伤害掉得更少）
        Assert.True(a.FalloffFloor > bow.FalloffFloor,
            "重磅弓弦应提高末端伤害系数（衰减率 −18% 的映射）");
    }

    [Fact]
    public void 护手挡格_重量加10百分比_带50百分比武器手否决()
    {
        WeaponMod m = WeaponModCatalog.Handguard();
        Assert.Equal(WeaponPart.Grip, m.Part);
        Assert.Equal(1.10, m.WeightMultiplier);                     // 重量 +10%
        Assert.Equal(0.50, m.HandGuardNegateChance);               // 武器手受击 50% 否决
        Assert.Equal(0.0, m.FrontalRangedNegateChance);            // 不是弩盾
        Assert.Empty(m.Stats);                                      // 否决不是 StatMod

        // 合成后武器变体也暴露这个几率（供 Actor 承伤入口读）
        ModdedWeapon mw = WeaponMods.ApplyMods(WeaponTable.Shortsword(), new[] { m });
        Assert.Equal(0.50, mw.HandGuardNegateChance);
    }

    [Fact]
    public void 弩盾_重量加50百分比_带25百分比正面远程否决_120度()
    {
        WeaponMod m = WeaponModCatalog.CrossbowShield();
        Assert.Equal(WeaponPart.CrossbowBody, m.Part);
        Assert.Equal(1.50, m.WeightMultiplier);                     // 重量 +50%
        Assert.Equal(0.25, m.FrontalRangedNegateChance);           // 正面远程 25% 否决
        Assert.Equal(60.0, m.FrontalNegateHalfAngleDeg);           // 半角 60 ⇒ 全张角 120°
        Assert.Equal(0.0, m.HandGuardNegateChance);                // 不是护手挡格
        Assert.Empty(m.Stats);

        ModdedWeapon mw = WeaponMods.ApplyMods(WeaponTable.HeavyCrossbow(), new[] { m });
        Assert.Equal(0.25, mw.FrontalRangedNegateChance);
        Assert.Equal(60.0, mw.FrontalNegateHalfAngleDeg);
    }

    // ════════════════════════════ 钩子 A：护手挡格（部位条件否决） ════════════════════════════

    [Fact]
    public void 护手挡格否决_命中武器手且掷中_才否决()
    {
        var rng = new SequenceRandomSource(0.4);   // 0.4 < 0.5 ⇒ 掷中
        Assert.True(WeaponModDefense.HandGuardNegates(0.5, hitIsWeaponHand: true, rng));
        Assert.Equal(0, rng.Remaining);            // 恰好取一个数
    }

    [Fact]
    public void 护手挡格否决_命中武器手但掷不中_不否决()
    {
        var rng = new SequenceRandomSource(0.7);   // 0.7 ≥ 0.5 ⇒ 掷不中
        Assert.False(WeaponModDefense.HandGuardNegates(0.5, hitIsWeaponHand: true, rng));
    }

    [Fact]
    public void 护手挡格否决_命中非武器手_不否决且不掷点()
    {
        var rng = new SequenceRandomSource();      // 空序列：一旦掷点即抛
        Assert.False(WeaponModDefense.HandGuardNegates(0.5, hitIsWeaponHand: false, rng));   // 短路，不抛
    }

    [Fact]
    public void 护手挡格否决_无改装几率0_恒不触发且不掷点_零漂移()
    {
        var rng = new SequenceRandomSource();      // 空序列
        Assert.False(WeaponModDefense.HandGuardNegates(0.0, hitIsWeaponHand: true, rng));    // 短路，不抛
    }

    // ════════════════════════════ 钩子 B：弩盾（方向性远程否决） ════════════════════════════

    // 防御方在原点、面朝 +X。射手在正前方（正面锥内）/正后方（锥外）。
    private static readonly Vector2 DefenderPos = Vector2.Zero;
    private static readonly Vector2 FacingRight = new(1f, 0f);

    [Fact]
    public void 弩盾否决_正面锥内的远程且掷中_否决()
    {
        var shooterFront = new Vector2(100f, 0f);   // 正前方，夹角 0° < 60°
        var rng = new SequenceRandomSource(0.1);    // 0.1 < 0.25 ⇒ 掷中
        Assert.True(WeaponModDefense.FrontalRangedNegates(
            ranged: true, 0.25, 60.0, DefenderPos, FacingRight, shooterFront, rng));
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void 弩盾否决_背后来袭_不否决且不掷点()
    {
        var shooterBehind = new Vector2(-100f, 0f);   // 正后方，夹角 180° > 60°
        var rng = new SequenceRandomSource();          // 空序列：锥外应短路、不掷点
        Assert.False(WeaponModDefense.FrontalRangedNegates(
            ranged: true, 0.25, 60.0, DefenderPos, FacingRight, shooterBehind, rng));
    }

    [Fact]
    public void 弩盾否决_正面锥内但掷不中_不否决()
    {
        var shooterFront = new Vector2(100f, 20f);   // 约 11°，仍在正面锥内
        var rng = new SequenceRandomSource(0.9);     // 0.9 ≥ 0.25 ⇒ 掷不中
        Assert.False(WeaponModDefense.FrontalRangedNegates(
            ranged: true, 0.25, 60.0, DefenderPos, FacingRight, shooterFront, rng));
    }

    [Fact]
    public void 弩盾否决_近战不吃_不掷点()
    {
        var shooterFront = new Vector2(100f, 0f);
        var rng = new SequenceRandomSource();        // 空序列
        Assert.False(WeaponModDefense.FrontalRangedNegates(
            ranged: false, 0.25, 60.0, DefenderPos, FacingRight, shooterFront, rng));   // 近战短路
    }

    [Fact]
    public void 弩盾否决_无弩盾几率0_恒不触发且不掷点_零漂移()
    {
        var shooterFront = new Vector2(100f, 0f);
        var rng = new SequenceRandomSource();        // 空序列
        Assert.False(WeaponModDefense.FrontalRangedNegates(
            ranged: true, 0.0, 60.0, DefenderPos, FacingRight, shooterFront, rng));     // chance 0 短路
    }

    [Fact]
    public void 弩盾锥边界_侧面90度在锥外_不否决()
    {
        var shooterSide = new Vector2(0f, 100f);     // 正侧方，夹角 90° > 60°
        var rng = new SequenceRandomSource();
        Assert.False(WeaponModDefense.FrontalRangedNegates(
            ranged: true, 0.25, 60.0, DefenderPos, FacingRight, shooterSide, rng));
    }
}
