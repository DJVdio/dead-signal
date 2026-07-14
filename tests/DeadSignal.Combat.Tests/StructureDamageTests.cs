using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 砸墙伤害由武器派生（<see cref="StructureDamage"/>）：每击伤害 = 砸墙有效武器平均伤害 × 该武器「砸墙系数」，
/// 节奏 = 砸墙有效武器的出手间隔。护栏住三条此前断裂的语义：
/// ① 破甲锤砸墙 ≠ 匕首砸墙（旧实现两者都是常数 25）；
/// ② 枪械砸墙 = 抡枪托（旧实现拿手枪的比拿匕首的还慢）；
/// ③ 武器表改数值必须传导到围墙（旧实现丧尸恒 12、劫掠者恒 25，与武器表完全解耦）。
/// </summary>
public class StructureDamageTests
{
    private static double PerHit(Weapon w) => StructureDamage.PerHit(w);
    private static double Dps(Weapon w) => StructureDamage.PerSecond(w);

    // ---- ① 钝器/重锤 > 锐器：破甲锤才配叫破甲锤 ----

    [Fact]
    public void 破甲锤砸墙远胜匕首_每击与每秒皆是()
    {
        Weapon hammer = WeaponTable.Warhammer();
        Weapon dagger = WeaponTable.Dagger();

        Assert.True(PerHit(hammer) > PerHit(dagger) * 5,
            $"破甲锤每击 {PerHit(hammer):F1} 应远超匕首 {PerHit(dagger):F1}（旧实现两者都是 25）");
        Assert.True(Dps(hammer) > Dps(dagger) * 5,
            $"破甲锤破墙效率 {Dps(hammer):F2}/s 应远超匕首 {Dps(dagger):F2}/s");
    }

    [Fact]
    public void 破甲锤是全表最强破门武器()
    {
        double hammer = Dps(WeaponTable.Warhammer());
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            if (w.Name == WeaponTable.Warhammer().Name)
            {
                continue;
            }
            Assert.True(hammer > Dps(w), $"破甲锤 {hammer:F2}/s 应强于 {w.Name} {Dps(w):F2}/s");
        }
    }

    [Fact]
    public void 钝器系数高于锐器_同等伤害下砸墙更狠()
    {
        var blunt = new Weapon { Name = "钝", DamageMin = 10, DamageMax = 10, DamageType = DamageType.Blunt, AttackInterval = 1 };
        var sharp = new Weapon { Name = "锐", DamageMin = 10, DamageMax = 10, DamageType = DamageType.Sharp, AttackInterval = 1 };

        Assert.True(PerHit(blunt) > PerHit(sharp));
    }

    // ---- ② 枪械砸墙 = 抡枪托（复用 MeleeProfile） ----

    [Fact]
    public void 枪械砸墙走枪托profile_伤害与节奏都取枪托而非子弹()
    {
        Weapon rifle = WeaponTable.Rifle();
        Weapon stock = rifle.MeleeProfile()!;

        double expectedHit = (stock.DamageMin + stock.DamageMax) / 2 * StructureDamage.FactorFor(rifle);
        Assert.Equal(expectedHit, PerHit(rifle), 6);
        Assert.Equal(stock.AttackInterval, StructureDamage.Interval(rifle), 6);

        // 子弹伤害仍显著高于枪托——若误取子弹伤害，本条会炸。
        // ⚠ T21：原断言是 < 子弹均值的 50%。用户把步枪削到 10~24（均值 17）后，枪托均值 8.5 恰好 = 17×0.5，
        // 这个启发式阈值刚好卡死在边界上 ⇒ 改为直接断言"严格低于子弹均值"（意图不变：砸墙取的是枪托不是子弹）。
        double bulletAvg = (rifle.DamageMin + rifle.DamageMax) / 2;
        Assert.True(PerHit(rifle) < bulletAvg, "砸墙不该按子弹伤害算（子弹打不穿承重墙）");
    }

    [Fact]
    public void 持枪劫掠者砸墙不再慢于持匕首者()
    {
        // 旧实现的荒谬：伤害都是常数 25，节奏却取武器（手枪 2.0s / 匕首 1.4s）⇒ 一把匕首拆铁皮大门比手枪快 43%。
        Assert.True(Dps(WeaponTable.Pistol()) > Dps(WeaponTable.Dagger()),
            $"手枪（抡枪托）{Dps(WeaponTable.Pistol()):F2}/s 应快于匕首 {Dps(WeaponTable.Dagger()):F2}/s");
    }

    // ---- ③ 弓弩：全表最徒劳 ----

    [Fact]
    public void 弓弩砸墙是全表最低()
    {
        double bestBow = double.MinValue;
        double worstOther = double.MaxValue;
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            bool isArchery = Archery.UsesArrows(w);
            if (isArchery)
            {
                bestBow = System.Math.Max(bestBow, Dps(w));
            }
            else
            {
                worstOther = System.Math.Min(worstOther, Dps(w));
            }
        }
        Assert.True(bestBow < worstOther,
            $"最能砸墙的弓弩 {bestBow:F2}/s 也应低于最不能砸墙的非弓弩 {worstOther:F2}/s（射箭砸墙最徒劳）");
    }

    // ---- ④ 丧尸：专属「撕扯」系数（不是用爪尖划墙，是整只扑上去撞、扒、咬） ----

    [Fact]
    public void 丧尸爪击有专属撕扯系数_远高于锐器兜底()
    {
        Weapon claw = WeaponTable.ZombieClaw();
        Assert.True(StructureDamage.FactorFor(claw) > StructureDamage.DefaultSharpFactor * 3,
            "丧尸砸墙不是爪尖划，是整只撞扒咬——系数必须显式高于锐器兜底");
        Assert.Equal(5.0, PerHit(claw), 3); // 爪击均值 2（T29 用户手改 1~3）× 撕扯系数 2.5（未动）
    }

    [Fact]
    public void 丧尸破基础大门时间在合理量纲内()
    {
        double dps = Dps(WeaponTable.ZombieClaw());
        double seconds = CampStructureTable.MaxHp(StructureTier.GateBasic) / dps;

        // 一夜 480s。单只丧尸砸基础大门应落在「够久到玩家来得及反应、又不至于荒谬」的窗口内。
        Assert.InRange(seconds, 40, 120);
    }

    // ---- ⑤ 传导：武器表改数值 → 围墙立刻感知（本单的根本目的） ----

    [Fact]
    public void 武器伤害改动直接传导到砸墙()
    {
        var weak = new Weapon { Name = "弱", DamageMin = 2, DamageMax = 4, DamageType = DamageType.Blunt, AttackInterval = 1, StructureFactor = 1.0 };
        var strong = new Weapon { Name = "强", DamageMin = 20, DamageMax = 40, DamageType = DamageType.Blunt, AttackInterval = 1, StructureFactor = 1.0 };

        Assert.Equal(3, PerHit(weak), 6);   // 均值 3 × 1.0
        Assert.Equal(30, PerHit(strong), 6); // 均值 30 × 1.0
    }

    [Fact]
    public void 砸墙系数是数据_未填则按锐钝兜底()
    {
        var noFactorBlunt = new Weapon { Name = "钝", DamageMin = 10, DamageMax = 10, DamageType = DamageType.Blunt, AttackInterval = 1 };
        var noFactorSharp = new Weapon { Name = "锐", DamageMin = 10, DamageMax = 10, DamageType = DamageType.Sharp, AttackInterval = 1 };

        Assert.Equal(StructureDamage.DefaultBluntFactor, StructureDamage.FactorFor(noFactorBlunt), 6);
        Assert.Equal(StructureDamage.DefaultSharpFactor, StructureDamage.FactorFor(noFactorSharp), 6);

        var custom = new Weapon { Name = "自定义", DamageMin = 10, DamageMax = 10, DamageType = DamageType.Sharp, AttackInterval = 1, StructureFactor = 3.0 };
        Assert.Equal(3.0, StructureDamage.FactorFor(custom), 6);
        Assert.Equal(30, PerHit(custom), 6);
    }

    [Fact]
    public void 全表武器砸墙伤害皆为正数_没有谁完全砸不动()
    {
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            Assert.True(PerHit(w) > 0, $"{w.Name} 砸墙伤害应 > 0");
            Assert.True(StructureDamage.Interval(w) > 0, $"{w.Name} 砸墙节奏应 > 0");
        }
    }

    // ---- ⑥ 结构承伤走小数（精度通则：伤害全程不取整） ----

    [Fact]
    public void 结构承伤不取整()
    {
        var fence = new CampStructureState(StructureTier.FenceBasic); // 150
        fence.TakeDamage(7.5);

        Assert.Equal(142.5, fence.Hp, 6);
    }

    [Fact]
    public void 丧尸砸基础围栏的爪数与小数血量一致()
    {
        var fence = new CampStructureState(StructureTier.FenceBasic); // 150
        double perClaw = PerHit(WeaponTable.ZombieClaw());            // 5.0（T29：爪击 1~3 之后每爪少了 2.5 点）
        int claws = 0;
        while (!fence.IsDestroyed)
        {
            fence.TakeDamage(perClaw);
            claws++;
        }

        Assert.Equal(30, claws); // 150 / 5.0（原 20 爪；爪变轻了，但出爪也快了近一倍 ⇒ 拆围栏的实际耗时反而更短）
    }
}
