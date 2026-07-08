using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 幸存者技能系统纯逻辑单测：技能项 → 等级（未掌握/初级/中级/高级）的持有态、
/// 门槛查询（HasSkill / LevelOf）、经验累积升级（含从"未掌握"习得初级）、authored 初始分布直设。
/// 阈值/等级映射皆"拟定待调"，测试锁的是规则形态与当前拟定值。
/// </summary>
public class SkillSetTests
{
    [Fact]
    public void Defaults_AllSkillsUnknown()
    {
        var s = new SkillSet();
        Assert.Equal(SkillLevel.None, s.LevelOf(SkillType.Textile));
        Assert.Equal(SkillLevel.None, s.LevelOf(SkillType.Mechanical));
        Assert.False(s.HasSkill(SkillType.Textile));            // 默认门槛=初级
        Assert.False(s.HasSkill(SkillType.Chemistry, SkillLevel.Novice));
        Assert.Equal(0, s.ExperienceToward(SkillType.Textile));
    }

    [Fact]
    public void Train_SetsAuthoredLevel_ResetsProgress()
    {
        var s = new SkillSet();
        s.Train(SkillType.Textile, SkillLevel.Novice);
        Assert.Equal(SkillLevel.Novice, s.LevelOf(SkillType.Textile));
        Assert.True(s.HasSkill(SkillType.Textile));                       // 达到初级门槛
        Assert.True(s.HasSkill(SkillType.Textile, SkillLevel.Novice));
        Assert.False(s.HasSkill(SkillType.Textile, SkillLevel.Adept));    // 未达中级
        Assert.Equal(0, s.ExperienceToward(SkillType.Textile));
    }

    [Fact]
    public void Train_ClampsToMaxLevel()
    {
        var s = new SkillSet();
        s.Train(SkillType.Mechanical, (SkillLevel)99);
        Assert.Equal(SkillSet.MaxLevel, s.LevelOf(SkillType.Mechanical)); // 封顶=高级
    }

    [Fact]
    public void GainExperience_LearnsNoviceFromUnknown_AtThreshold()
    {
        var s = new SkillSet();
        int gained = s.GainExperience(SkillType.Textile, SkillSet.ExperiencePerLevel - 1);
        Assert.Equal(0, gained);                                 // 未到阈值：仍未掌握
        Assert.Equal(SkillLevel.None, s.LevelOf(SkillType.Textile));

        gained = s.GainExperience(SkillType.Textile, 1);         // 补满一格阈值
        Assert.Equal(1, gained);                                 // 习得初级
        Assert.Equal(SkillLevel.Novice, s.LevelOf(SkillType.Textile));
        Assert.Equal(0, s.ExperienceToward(SkillType.Textile));  // 余量清零
    }

    [Fact]
    public void GainExperience_CarriesRemainder()
    {
        var s = new SkillSet();
        int gained = s.GainExperience(SkillType.Chemistry, SkillSet.ExperiencePerLevel + 30);
        Assert.Equal(1, gained);
        Assert.Equal(SkillLevel.Novice, s.LevelOf(SkillType.Chemistry));
        Assert.Equal(30, s.ExperienceToward(SkillType.Chemistry)); // 溢出余量转入下一级进度
    }

    [Fact]
    public void GainExperience_MultipleLevelsAtOnce()
    {
        var s = new SkillSet();
        int gained = s.GainExperience(SkillType.Mechanical, SkillSet.ExperiencePerLevel * 2);
        Assert.Equal(2, gained);
        Assert.Equal(SkillLevel.Adept, s.LevelOf(SkillType.Mechanical)); // 未掌握→初级→中级
    }

    [Fact]
    public void GainExperience_CapsAtExpert_DiscardsExcess()
    {
        var s = new SkillSet();
        s.Train(SkillType.Combat, SkillLevel.Expert);
        int gained = s.GainExperience(SkillType.Combat, SkillSet.ExperiencePerLevel * 5);
        Assert.Equal(0, gained);
        Assert.Equal(SkillLevel.Expert, s.LevelOf(SkillType.Combat));
        Assert.Equal(0, s.ExperienceToward(SkillType.Combat)); // 封顶不再累积经验
    }

    [Fact]
    public void GainExperience_NonPositive_NoOp()
    {
        var s = new SkillSet();
        Assert.Equal(0, s.GainExperience(SkillType.Cooking, 0));
        Assert.Equal(0, s.GainExperience(SkillType.Cooking, -50));
        Assert.Equal(SkillLevel.None, s.LevelOf(SkillType.Cooking));
    }

    [Fact]
    public void Levels_Ordered_ForThresholdComparison()
    {
        Assert.True(SkillLevel.Novice < SkillLevel.Adept);
        Assert.True(SkillLevel.Adept < SkillLevel.Expert);
        Assert.Equal(0, (int)SkillLevel.None);
    }

    [Fact]
    public void Snapshot_ExposesOnlyLearnedSkills()
    {
        var s = new SkillSet();
        s.Train(SkillType.Textile, SkillLevel.Novice);
        s.Train(SkillType.Cooking, SkillLevel.Adept);
        var snap = s.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal(SkillLevel.Novice, snap[SkillType.Textile]);
        Assert.Equal(SkillLevel.Adept, snap[SkillType.Cooking]);
        Assert.False(snap.ContainsKey(SkillType.Mechanical)); // 未掌握不入快照
    }
}
