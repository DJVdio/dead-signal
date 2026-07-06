using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 描述性战斗日志生成器（<see cref="CombatLogFormatter"/>）。攻击方归属视角、纯函数。
/// 措辞口径：有攻击方写"{A} 击中 {B} 的{部位}，{砸伤/撕裂} N…"；无攻击方（环境/无源）退回
/// 承伤方视角"{B} 的{部位}被砸伤/被撕裂 N…"。被甲挡下走"挡下"专述；断肢/骨折/脑震荡/流血依序追加；
/// 致死追加专门措辞（"毙命"）。
/// </summary>
public class CombatLogFormatterTests
{
    // AttackOutcome ctor 顺序：damage, partName, finalType, blocked, severed, bled, concussed, fractured, died
    private static AttackOutcome Hit(
        int damage, string part, DamageType type,
        bool blocked = false, bool severed = false, bool bled = false,
        bool concussed = false, bool fractured = false, bool died = false)
        => new(damage, part, type, blocked, severed, bled, concussed, fractured, died);

    [Fact]
    public void Line_ContainsAttackerName_TargetName_PartName_AndDamageNumber()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(9, "左小腿", DamageType.Sharp));
        Assert.Contains("丧尸", line);
        Assert.Contains("阿强", line);
        Assert.Contains("左小腿", line);
        Assert.Contains("9", line);
    }

    [Fact]
    public void WithAttacker_UsesAttributionWording_AttackerBeforeTarget()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(9, "左小腿", DamageType.Sharp));
        Assert.Contains("击中", line);
        Assert.True(line.IndexOf("丧尸") < line.IndexOf("阿强"));
    }

    [Fact]
    public void NullAttacker_FallsBackToTargetPerspective_NoAttribution()
    {
        string line = CombatLogFormatter.Format(null, "阿强", Hit(5, "腹部", DamageType.Sharp));
        Assert.Contains("阿强", line);
        Assert.Contains("腹部", line);
        Assert.DoesNotContain("击中", line);
    }

    [Fact]
    public void EmptyAttacker_FallsBackToTargetPerspective_NoAttribution()
    {
        string line = CombatLogFormatter.Format("   ", "阿强", Hit(5, "腹部", DamageType.Sharp));
        Assert.Contains("阿强", line);
        Assert.Contains("腹部", line);
        Assert.DoesNotContain("击中", line);
    }

    [Fact]
    public void BluntDamage_UsesBluntVerb()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(7, "头部", DamageType.Blunt));
        Assert.Contains("砸伤", line);
        Assert.DoesNotContain("撕裂", line);
    }

    [Fact]
    public void SharpDamage_UsesSharpVerb()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(7, "头部", DamageType.Sharp));
        Assert.Contains("撕裂", line);
        Assert.DoesNotContain("砸伤", line);
    }

    [Fact]
    public void Blocked_UsesBlockWording_AndNoWoundVerb()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(0, "胸部", DamageType.Sharp, blocked: true));
        Assert.Contains("挡下", line);
        Assert.Contains("胸部", line);
        Assert.DoesNotContain("撕裂", line);
        Assert.DoesNotContain("砸伤", line);
    }

    [Fact]
    public void Blocked_WithAttacker_StillShowsAttribution()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(0, "胸部", DamageType.Sharp, blocked: true));
        Assert.Contains("丧尸", line);
        Assert.Contains("击中", line);
    }

    [Fact]
    public void Severed_ShowsAmputationWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(20, "右手", DamageType.Sharp, severed: true));
        Assert.Contains("断肢", line);
    }

    [Fact]
    public void Fractured_ShowsFractureWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(12, "左臂", DamageType.Blunt, fractured: true));
        Assert.Contains("骨折", line);
    }

    [Fact]
    public void Concussed_ShowsConcussionWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(8, "头部", DamageType.Blunt, concussed: true));
        Assert.Contains("震荡", line);
    }

    [Fact]
    public void Bled_ShowsBleedWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(6, "颈部", DamageType.Sharp, bled: true));
        Assert.Contains("流血", line);
    }

    [Fact]
    public void Died_ShowsDeathWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(40, "头部", DamageType.Sharp, died: true));
        Assert.Contains("毙命", line);
    }

    [Fact]
    public void NoEffects_HasNoEffectOrDeathWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(5, "腹部", DamageType.Sharp));
        Assert.DoesNotContain("断肢", line);
        Assert.DoesNotContain("骨折", line);
        Assert.DoesNotContain("震荡", line);
        Assert.DoesNotContain("流血", line);
        Assert.DoesNotContain("毙命", line);
    }

    [Fact]
    public void MultipleEffects_ConcatenatedInOrder_SeveredBeforeBled()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强",
            Hit(30, "右腿", DamageType.Sharp, severed: true, bled: true));
        Assert.Contains("断肢", line);
        Assert.Contains("流血", line);
        Assert.True(line.IndexOf("断肢") < line.IndexOf("流血"));
    }

    [Fact]
    public void AllEffects_OrderedSeveredFractureConcussBleed()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强", Hit(40, "头部", DamageType.Blunt,
            severed: true, bled: true, concussed: true, fractured: true));
        int iSever = line.IndexOf("断肢");
        int iFrac = line.IndexOf("骨折");
        int iConc = line.IndexOf("震荡");
        int iBled = line.IndexOf("流血");
        Assert.True(iSever < iFrac && iFrac < iConc && iConc < iBled);
    }

    [Fact]
    public void Death_ComesAfterEffectWording()
    {
        string line = CombatLogFormatter.Format("丧尸", "阿强",
            Hit(40, "头部", DamageType.Sharp, bled: true, died: true));
        Assert.True(line.IndexOf("流血") < line.IndexOf("毙命"));
    }

    [Fact]
    public void EmptyTargetName_FallsBackToPlaceholder_NoLeadingSpaceOnly()
    {
        string line = CombatLogFormatter.Format("丧尸", "", Hit(5, "腹部", DamageType.Blunt));
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("腹部", line);
    }
}
