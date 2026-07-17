using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [A4]《进阶木匠技术》→ 持消防斧攻速 +8% —— 消费层 per-wielder **出手间隔乘子**（×1/1.08），不动零依赖引擎。
/// 范式同 <see cref="CraftWorkTime"/>/ApparelEffectMultiplier 的"消费层乘子汇总"：把"读某书→持某武器→攻速%"集中一处算。
///
/// 意图护栏（先红[编译红]→绿）：
///   ① 读过进阶木匠 + 持消防斧 ⇒ 间隔乘子 = 1/1.08（攻速 +8%）；
///   ② 没读书 / 读了别的书 / 持别的武器 ⇒ 乘子 1.0（零回归）；
///   ③ 武器名不硬编码字面量——权威取 <see cref="WeaponTable.Axe"/>.Name（config 改名不会漏）。
/// </summary>
public class MeleeBookEffectTests
{
    private static string AxeName => WeaponTable.Axe().Name;
    private const string AdvCarpentry = "advanced_carpentry"; // = RecipeBook.AdvancedCarpentryBookId

    [Fact]
    public void 读进阶木匠_持消防斧_间隔乘子1除以1点08()
    {
        double m = MeleeBookEffect.AttackIntervalMultiplier(AxeName, id => id == AdvCarpentry);
        Assert.Equal(1.0 / 1.08, m, 9);
        Assert.True(m < 1.0); // 间隔更短 = 攻速更快
    }

    [Fact]
    public void 没读书_持消防斧_间隔乘子1_零回归()
        => Assert.Equal(1.0, MeleeBookEffect.AttackIntervalMultiplier(AxeName, _ => false), 9);

    [Fact]
    public void 读别的书_持消防斧_间隔乘子1()
        => Assert.Equal(1.0, MeleeBookEffect.AttackIntervalMultiplier(AxeName, id => id == "carpentry_basics"), 9);

    [Fact]
    public void 读进阶木匠_持别的武器_间隔乘子1()
        => Assert.Equal(1.0, MeleeBookEffect.AttackIntervalMultiplier("长剑", id => id == AdvCarpentry), 9);

    // 两处事实源焊死：常量口径与 RecipeBook 书 id 一致。
    [Fact]
    public void 书id_与RecipeBook一致()
        => Assert.Equal(RecipeBook.AdvancedCarpentryBookId, AdvCarpentry);

    // 加成常量口径：+8% ⇒ 攻速乘子 1.08。
    [Fact]
    public void 攻速加成常量_是1点08()
        => Assert.Equal(1.08, MeleeBookEffect.AdvancedCarpentryAxeAttackSpeedMultiplier, 9);
}
