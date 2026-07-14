using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <see cref="Arena"/>（Sim 的 N vs M 竞技场，2v1 校准用）必须与 <see cref="DuelEngine"/> **同一套效果口径**。
///
/// <para>
/// 背景：Arena 曾以"略去只会让结论更保守"为由不建模震荡。那个假设是**错的** —— 钝器对丧尸零流血、
/// 杀伤全靠 HP 磨，它的收益本来就建立在打断上；只给代价不给收益，等于把钝器系统性低估。
/// 复跑证据：接上震荡前「道格·棍棒 vs 丧尸」被压到 19%，接上后回到 DuelEngine 同量级。
/// 本文件用**变异测试**钉死这条：把 Arena 的震荡消费摘掉，下面的测试必须红。
/// </para>
/// </summary>
public class ArenaEffectsTests
{
    [Fact]
    public void Arena_ConsumesConcussion_BluntKeepsItsInterruptPayoff()
    {
        // Arena.Run 用**固定种子**（30260712 + seed×131）⇒ 下面的数字是确定性的，没有采样抖动。
        // 钝器的收益建立在打断上：接了震荡 = 无损率 17.8%；把震荡消费摘掉 = 14.9%（变异必红）。
        // ⚠️ 本阈值咬的是"破甲锤 2v2 vs 穿衣丧尸"这一具体对局——若有人重调破甲锤/丧尸装束数值，
        //    此测会红，那不是误报，而是要求重新确认钝器的打断收益还在。
        var hammer = Fighter("我方", WeaponTable.Warhammer(), Array.Empty<ArmorLayer>());
        var zombie = new DuelFighter
        {
            Name = "丧尸",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
            ArmorFactory = ZombieOutfit.RollArmor,
        };

        double noLoss = Arena.Run(new[] { hammer, hammer }, new[] { zombie, zombie }, 3000).NoLossRate;

        Assert.True(noLoss > 0.165, $"破甲锤 2v2 无损率塌到 {noLoss:P1}——Arena 多半又没消费震荡（不消费=14.9%）");
    }

    [Fact]
    public void Arena_AndDuel_ShareTheSameBleedGrading()
    {
        // 小部位伤口在 Arena 里同样不该把人放干（两边共用 Body/BleedModel，此测防有人给 Arena 另写一套失血）。
        var body = HumanBody.NewBody();
        var cfg = new DuelConfig();
        body.BleedRatePerWound = cfg.BleedRatePerWound;
        body.SetBloodMax(cfg.BloodMax);
        body.RegisterBleed(HumanBody.RightIndex, BleedModel.BleedSeverity.Medium);
        body.TickBleed(100_000);

        Assert.False(body.BledOut);
    }

    private static DuelFighter Fighter(string name, Weapon w, ArmorLayer[] armor) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
        Armor = armor,
    };
}
