using System.Numerics;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class StealthAndNoiseTests
{
    [Fact]
    public void 装备潜行值转成噪音乘子_正负值均保持中性可逆方向()
    {
        Assert.Equal(1.0, StealthLogic.EquipmentNoiseMultiplier(0.0), 10);
        Assert.Equal(0.8, StealthLogic.EquipmentNoiseMultiplier(0.25), 10);
        Assert.True(StealthLogic.EquipmentNoiseMultiplier(-0.35) > 1.0);
    }

    [Fact]
    public void 负重与服饰噪音必须连乘()
    {
        Assert.Equal(1.25, StealthLogic.LoadNoiseMultiplier(0.8), 10);
        Assert.Equal(1.0, StealthLogic.ActionNoiseMultiplier(0.25, 0.8), 10);
        Assert.Equal(1.0, StealthLogic.ActionNoiseMultiplier(0.0, 1.0), 10);
    }

    [Fact]
    public void 黑暗三级耗子缩短被发现视距_白天不吃加成()
    {
        Assert.Equal(1.0, StealthLogic.DetectionRangeMultiplier(1.0f, 0.0, 0.5), 10);
        Assert.Equal(1.0 / 1.5, StealthLogic.DetectionRangeMultiplier(0.2f, 0.0, 0.5), 10);
        Assert.Equal(0.8 / 1.5, StealthLogic.DetectionRangeMultiplier(0.2f, 0.25, 0.5), 10);
    }

    [Fact]
    public void 真实穿戴品从Authored潜行表投影出两个效果()
    {
        ApparelCatalog.ApparelDef def = Assert.IsType<ApparelCatalog.ApparelDef>(ApparelCatalog.Get("板甲"));
        Assert.Equal(1.0 / 0.65, ApparelCatalog.ApparelEffectMultiplier(new[] { "板甲" }, ApparelCatalog.EquipEffectKind.ExplorationStealth), 6);
        Assert.Equal(1.0 / 0.65, ApparelCatalog.ApparelEffectMultiplier(new[] { "板甲" }, ApparelCatalog.EquipEffectKind.ActionNoise), 6);

        // 平光眼镜已有读速效果，追加潜行效果时不能覆盖旧效果。
        Assert.Equal(1.05, ApparelCatalog.ApparelEffectMultiplier(new[] { "平光眼镜" }, ApparelCatalog.EquipEffectKind.ReadingSpeed), 10);
        Assert.NotNull(def);
    }

    [Fact]
    public void 耗子三级未被发现时先手伤害乘算_一旦破隐回到中性()
    {
        Assert.Equal(1.35, RatPerk.AmbushDamageMultiplier(true, 3, true), 10);
        Assert.Equal(1.0, RatPerk.AmbushDamageMultiplier(true, 3, false), 10);
        Assert.Equal(1.0, RatPerk.AmbushDamageMultiplier(true, 2, true), 10);
        Assert.Equal(1.0, RatPerk.AmbushDamageMultiplier(false, 3, true), 10);
    }

    [Fact]
    public void 噪音事件可被缓冲并按容量淘汰旧事件()
    {
        using var buffer = new NoiseCueBuffer(2);
        NoiseCueFeed.Publish(new NoiseCue(new Vector2(1, 2), 40, NoiseKind.Movement, RatNoiseSource.Footstep));
        NoiseCueFeed.Publish(new NoiseCue(new Vector2(3, 4), 100, NoiseKind.Combat, RatNoiseSource.DoorOpen));
        NoiseCueFeed.Publish(new NoiseCue(new Vector2(5, 6), 180, NoiseKind.Combat, RatNoiseSource.Breach));

        IReadOnlyList<NoiseCue> cues = buffer.Snapshot();
        Assert.Equal(2, cues.Count);
        Assert.Equal(new Vector2(3, 4), cues[0].Origin);
        Assert.Equal(RatNoiseSource.Breach, cues[1].Source);
    }

    [Fact]
    public void 噪音文案不泄露英文枚举名()
    {
        string text = NoiseCueText.Describe(new NoiseCue(Vector2.Zero, 40, NoiseKind.Movement, RatNoiseSource.Footstep));
        Assert.Contains("脚步", text);
        Assert.DoesNotContain("Footstep", text);
        Assert.DoesNotContain("Movement", text);
    }

    // ── 综合潜行评级 StealthRating（四项消费轴） ──

    [Fact]
    public void 四项全部中性值_返回1()
    {
        Assert.Equal(1.0, StealthLogic.StealthRating(
            apparelStealthScore: 0.0,
            movementSpeedMultiplier: 1.0,
            ambientLight: 1.0f,
            darknessStealthBonus: 0.0,
            coverCoefficient: 0.0), 10);
    }

    [Fact]
    public void 仅有服饰安静时_乘算低于1()
    {
        double r = StealthLogic.StealthRating(
            apparelStealthScore: 0.25,
            movementSpeedMultiplier: 1.0,
            ambientLight: 1.0f,
            darknessStealthBonus: 0.0,
            coverCoefficient: 0.0);
        Assert.Equal(0.8, r, 10);
    }

    [Fact]
    public void 仅有负重超载时_乘算高于1()
    {
        double r = StealthLogic.StealthRating(
            apparelStealthScore: 0.0,
            movementSpeedMultiplier: 0.8,
            ambientLight: 1.0f,
            darknessStealthBonus: 0.0,
            coverCoefficient: 0.0);
        Assert.Equal(1.25, r, 10);
    }

    [Fact]
    public void 仅有黑暗加成时_乘算低于1()
    {
        double r = StealthLogic.StealthRating(
            apparelStealthScore: 0.0,
            movementSpeedMultiplier: 1.0,
            ambientLight: 0.2f,
            darknessStealthBonus: 0.5,
            coverCoefficient: 0.0);
        Assert.Equal(1.0 / 1.5, r, 10);
    }

    [Fact]
    public void 仅有掩体时_乘算低于1()
    {
        double r = StealthLogic.StealthRating(
            apparelStealthScore: 0.0,
            movementSpeedMultiplier: 1.0,
            ambientLight: 1.0f,
            darknessStealthBonus: 0.0,
            coverCoefficient: 1.0);
        Assert.Equal(0.5, r, 10);
    }

    [Fact]
    public void 四项全部生效时_等于各自乘算连乘()
    {
        double apparel = StealthLogic.EquipmentNoiseMultiplier(0.25); // 0.8
        double load = StealthLogic.LoadNoiseMultiplier(0.8);          // 1.25
        double dark = 1.0 / 1.5;                                       // 0.666…
        double cover = 0.5;                                            // 0.5
        double expected = apparel * load * dark * cover;

        double actual = StealthLogic.StealthRating(
            apparelStealthScore: 0.25,
            movementSpeedMultiplier: 0.8,
            ambientLight: 0.2f,
            darknessStealthBonus: 0.5,
            coverCoefficient: 1.0);
        Assert.Equal(expected, actual, 10);
    }

    [Fact]
    public void 黑暗因子_白昼或无加成返中性值()
    {
        Assert.Equal(1.0, StealthLogic.DarknessFactor(1.0f, 0.0), 10);
        Assert.Equal(1.0, StealthLogic.DarknessFactor(0.5f, 0.0), 10);
        Assert.Equal(1.0, StealthLogic.DarknessFactor(0.0f, -0.1), 10);
    }

    [Fact]
    public void 黑暗因子_暗处且加成生效时压低()
    {
        Assert.Equal(1.0 / 1.5, StealthLogic.DarknessFactor(0.2f, 0.5), 10);
        Assert.Equal(1.0 / 2.0, StealthLogic.DarknessFactor(0.0f, 1.0), 10);
    }

    [Fact]
    public void 掩体因子_中性值为1_满掩体为0_5()
    {
        Assert.Equal(1.0, StealthLogic.CoverFactor(0.0), 10);
        Assert.Equal(1.0 / 1.5, StealthLogic.CoverFactor(0.5), 10);
        Assert.Equal(0.5, StealthLogic.CoverFactor(1.0), 10);
    }

    [Fact]
    public void 掩体因子_系数越界自动钳位()
    {
        Assert.Equal(1.0, StealthLogic.CoverFactor(-0.1), 10);
        Assert.Equal(0.5, StealthLogic.CoverFactor(1.5), 10);
    }

    [Fact]
    public void 负重因子与噪音负重因子同源()
    {
        Assert.Equal(StealthLogic.LoadNoiseMultiplier(0.8), StealthLogic.LoadFactor(0.8), 10);
        Assert.Equal(StealthLogic.LoadNoiseMultiplier(0.5), StealthLogic.LoadFactor(0.5), 10);
        Assert.Equal(StealthLogic.LoadNoiseMultiplier(1.0), StealthLogic.LoadFactor(1.0), 10);
    }
}
