using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 战报飘字"串+色"生成器（<see cref="CombatMoteText"/>）。配色口径（用户拍板）：
/// 伤害数字按伤害类型着色——钝器=黄字、锐器=红字；被甲挡下走中性灰"叮·挡下"。
/// 效果标志（断肢/骨折/震荡/流血）依序拼接（本单为"飘字①增强"，多效果同时显示）。
/// </summary>
public class CombatMoteTextTests
{
    // AttackOutcome ctor 顺序：damage, partName, finalType, blocked, severed, bled, concussed, fractured, died
    private static AttackOutcome Hit(
        int damage, string part, DamageType type,
        bool blocked = false, bool severed = false, bool bled = false,
        bool concussed = false, bool fractured = false, bool died = false)
        => new(damage, part, type, blocked, severed, bled, concussed, fractured, died);

    [Fact]
    public void BluntDamage_IsYellow()
    {
        var mote = CombatMoteText.Build(Hit(7, "头部", DamageType.Blunt));
        Assert.Equal(CombatMoteText.BluntColor, mote.Color);
    }

    [Fact]
    public void SharpDamage_IsRed()
    {
        var mote = CombatMoteText.Build(Hit(7, "头部", DamageType.Sharp));
        Assert.Equal(CombatMoteText.SharpColor, mote.Color);
    }

    [Fact]
    public void BluntAndSharpColors_Differ()
    {
        // 钝黄 vs 锐红：两种伤害类型配色必须可区分。
        Assert.NotEqual(CombatMoteText.BluntColor, CombatMoteText.SharpColor);
    }

    [Fact]
    public void Text_ContainsDamageNumber_PartName_And_TypeTag()
    {
        var mote = CombatMoteText.Build(Hit(9, "左小腿", DamageType.Sharp));
        Assert.Contains("-9", mote.Text);
        Assert.Contains("左小腿", mote.Text);
        Assert.Contains("锐", mote.Text);
    }

    [Fact]
    public void BluntTypeTag_IsBlunt()
    {
        var mote = CombatMoteText.Build(Hit(4, "腹部", DamageType.Blunt));
        Assert.Contains("钝", mote.Text);
        Assert.DoesNotContain("锐", mote.Text);
    }

    [Fact]
    public void Severed_ShowsBreakFlag()
    {
        var mote = CombatMoteText.Build(Hit(20, "右手", DamageType.Sharp, severed: true));
        Assert.Contains("断!", mote.Text);
    }

    [Fact]
    public void Fractured_ShowsFractureFlag()
    {
        var mote = CombatMoteText.Build(Hit(12, "左臂", DamageType.Blunt, fractured: true));
        Assert.Contains("骨折", mote.Text);
    }

    [Fact]
    public void Concussed_ShowsConcussionFlag()
    {
        var mote = CombatMoteText.Build(Hit(8, "头部", DamageType.Blunt, concussed: true));
        Assert.Contains("震荡", mote.Text);
    }

    [Fact]
    public void Bled_ShowsBleedFlag()
    {
        var mote = CombatMoteText.Build(Hit(6, "颈部", DamageType.Sharp, bled: true));
        Assert.Contains("流血", mote.Text);
    }

    [Fact]
    public void NoEffects_HasNoEffectFlags()
    {
        var mote = CombatMoteText.Build(Hit(5, "腹部", DamageType.Sharp));
        Assert.DoesNotContain("断!", mote.Text);
        Assert.DoesNotContain("骨折", mote.Text);
        Assert.DoesNotContain("震荡", mote.Text);
        Assert.DoesNotContain("流血", mote.Text);
    }

    [Fact]
    public void MultipleEffects_AllConcatenatedInOrder()
    {
        // 增强点：一次命中同时断肢+流血，两个标志都要出现，且按 断!→流血 顺序。
        var mote = CombatMoteText.Build(Hit(30, "右腿", DamageType.Sharp, severed: true, bled: true));
        Assert.Contains("断!", mote.Text);
        Assert.Contains("流血", mote.Text);
        Assert.True(mote.Text.IndexOf("断!") < mote.Text.IndexOf("流血"));
    }

    [Fact]
    public void AllEffects_OrderedSeveredFractureConcussBleed()
    {
        var mote = CombatMoteText.Build(Hit(40, "头部", DamageType.Blunt,
            severed: true, bled: true, concussed: true, fractured: true));
        int iSever = mote.Text.IndexOf("断!");
        int iFrac = mote.Text.IndexOf("骨折");
        int iConc = mote.Text.IndexOf("震荡");
        int iBled = mote.Text.IndexOf("流血");
        Assert.True(iSever < iFrac && iFrac < iConc && iConc < iBled);
    }

    [Fact]
    public void Blocked_UsesNeutralColor_AndBlockedText()
    {
        var mote = CombatMoteText.Build(Hit(0, "胸部", DamageType.Sharp, blocked: true));
        Assert.Equal(CombatMoteText.BlockedColor, mote.Color);
        Assert.Contains("挡下", mote.Text);
        Assert.Contains("胸部", mote.Text);
    }

    [Fact]
    public void Blocked_DoesNotShowNegativeDamage()
    {
        // 挡下不显示 "-0 伤害"，只给"叮·挡下"手感。
        var mote = CombatMoteText.Build(Hit(0, "胸部", DamageType.Blunt, blocked: true));
        Assert.DoesNotContain("-0", mote.Text);
        Assert.Contains("叮", mote.Text);
    }

    [Fact]
    public void Blocked_TakesPriorityOverTypeColor()
    {
        // 即便 FinalType=Blunt，被挡下时颜色也应是中性灰而非钝黄。
        var mote = CombatMoteText.Build(Hit(0, "胸部", DamageType.Blunt, blocked: true));
        Assert.NotEqual(CombatMoteText.BluntColor, mote.Color);
        Assert.Equal(CombatMoteText.BlockedColor, mote.Color);
    }
}
