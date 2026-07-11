using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 专属效果（诺蒂·书虫样板）+ 读书进度 + 读速合成纯逻辑单测。
/// 锁的是规则形态：书虫按累计阅读时间跨阈值升级、各级读速倍率、L3 全营加成；
/// 读书进度按 (读者,书) 累计且跨夜不清零；有效读速 = 基础 × 自身倍率 × 座位 × (1+全营加成汇总)。
/// 具体阈值/座位惩罚/每本书 ReadHours 皆为 draft，测试锁形态不锁绝对数值（用相对断言/常量引用）。
/// </summary>
public class SurvivorPerksTests
{
    // ---------- 书虫等级 / 各级读速倍率 ----------

    [Fact]
    public void Bookworm_StartsAtLevel1_WithBaseBonus()
    {
        var perk = new BookwormPerk();
        Assert.Equal(1, perk.Level);                 // 持有者天生至少 L1（书虫本就读得快）
        Assert.Equal(0.0, perk.AccumulatedReadingHours);
        Assert.Equal(0.25, perk.ReadingSpeedBonus);  // L1 自身 +25%（加法）
        Assert.Equal(0.0, perk.CampWideReadingSpeedBonus); // 未到 L3 无全营加成
    }

    [Fact]
    public void Bookworm_LevelBonuses()
    {
        Assert.Equal(0.25, BookwormPerk.BonusForLevel(1));
        Assert.Equal(0.50, BookwormPerk.BonusForLevel(2));
        Assert.Equal(0.50, BookwormPerk.BonusForLevel(3)); // L3 自身与 L2 相同（升级点在全营加成）
    }

    [Fact]
    public void AddReadingTime_BelowThreshold_NoLevelUp()
    {
        var perk = new BookwormPerk();
        bool leveled = perk.AddReadingTime(BookwormPerk.Level2ThresholdHours - 1);
        Assert.False(leveled);
        Assert.Equal(1, perk.Level);
        Assert.Equal(0.25, perk.ReadingSpeedBonus);
    }

    [Fact]
    public void AddReadingTime_CrossingLevel2Threshold_LevelsUp()
    {
        var perk = new BookwormPerk();
        bool leveled = perk.AddReadingTime(BookwormPerk.Level2ThresholdHours);
        Assert.True(leveled);
        Assert.Equal(2, perk.Level);
        Assert.Equal(0.50, perk.ReadingSpeedBonus);
        Assert.Equal(0.0, perk.CampWideReadingSpeedBonus); // L2 仍无全营加成
    }

    [Fact]
    public void AddReadingTime_CrossingLevel3Threshold_UnlocksCampWideBonus()
    {
        var perk = new BookwormPerk();
        perk.AddReadingTime(BookwormPerk.Level3ThresholdHours);
        Assert.Equal(3, perk.Level);
        Assert.Equal(0.50, perk.ReadingSpeedBonus);        // L3 自身仍 +50%（不再涨）
        Assert.Equal(0.25, perk.CampWideReadingSpeedBonus); // L3 解锁全营 +25%
    }

    [Fact]
    public void AddReadingTime_CanJumpMultipleLevelsAtOnce()
    {
        var perk = new BookwormPerk();
        bool leveled = perk.AddReadingTime(BookwormPerk.Level3ThresholdHours + 100); // 一口气跨过 L2、L3
        Assert.True(leveled);
        Assert.Equal(3, perk.Level);
    }

    [Fact]
    public void AddReadingTime_CapsAtLevel3()
    {
        var perk = new BookwormPerk();
        perk.AddReadingTime(BookwormPerk.Level3ThresholdHours);
        bool leveledAgain = perk.AddReadingTime(1000); // 已满级，再读不再升
        Assert.False(leveledAgain);
        Assert.Equal(3, perk.Level);
    }

    [Fact]
    public void AddReadingTime_AccumulatesHours()
    {
        var perk = new BookwormPerk();
        perk.AddReadingTime(10);
        perk.AddReadingTime(5);
        Assert.Equal(15.0, perk.AccumulatedReadingHours);
    }

    // ---------- 读书进度：跨 (读者,书) 累计、跨夜持久 ----------

    [Fact]
    public void ReadingProgress_DefaultsToZero_NotComplete()
    {
        var prog = new ReadingProgress();
        Assert.Equal(0.0, prog.HoursOn("wilderness_survival_guide"));
        Assert.False(prog.IsComplete("wilderness_survival_guide", bookReadHours: 24));
    }

    [Fact]
    public void ReadingProgress_Advance_Accumulates()
    {
        var prog = new ReadingProgress();
        prog.Advance("guide", 4);
        prog.Advance("guide", 3);
        Assert.Equal(7.0, prog.HoursOn("guide"));
    }

    [Fact]
    public void ReadingProgress_PersistsAcrossNights_NotReset()
    {
        var prog = new ReadingProgress();
        prog.Advance("guide", 10);            // 第一夜读了 10h（书需 24h）
        Assert.False(prog.IsComplete("guide", 24));
        prog.Advance("guide", 14);            // 第二夜接着读，进度不清零
        Assert.True(prog.IsComplete("guide", 24));
        Assert.Equal(24.0, prog.HoursOn("guide"));
    }

    [Fact]
    public void ReadingProgress_PerBookIndependent()
    {
        var prog = new ReadingProgress();
        prog.Advance("guide", 20);
        Assert.Equal(20.0, prog.HoursOn("guide"));
        Assert.Equal(0.0, prog.HoursOn("farmer")); // 另一本互不影响
    }

    // ---------- 有效读速合成 ----------

    [Fact]
    public void EffectiveSpeed_NoPerk_Seated_IsBase()
    {
        double s = ReadingSpeed.Effective(baseSpeed: 1.0, selfBonus: 0.0, hasSeat: true, campWideBonusSum: 0.0);
        Assert.Equal(1.0, s);
    }

    [Fact]
    public void EffectiveSpeed_NoSeat_AppliesPenalty()
    {
        double s = ReadingSpeed.Effective(1.0, 0.0, hasSeat: false, campWideBonusSum: 0.0);
        Assert.Equal(ReadingSpeed.NoSeatMultiplier, s); // 无座 -10%
    }

    [Fact]
    public void EffectiveSpeed_Level2_Seated()
    {
        // L2 自身 +50%（加法）→ ×1.5
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.50, hasSeat: true, campWideBonusSum: 0.0);
        Assert.Equal(1.50, s);
    }

    [Fact]
    public void EffectiveSpeed_CampWideBonus_AppliesToReader()
    {
        // 某普通读者(自身无 perk=0)受营内某 L3 书虫的全营 +25% 加成 → ×1.25
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideBonusSum: 0.25);
        Assert.Equal(1.25, s);
    }

    [Fact]
    public void EffectiveSpeed_TinoL3_Seated_IsSeventyFivePercent()
    {
        // 诺蒂 L3 有座：基础 ×(1 + 自身 0.50 + 含自己的全营 0.25) = ×1.75（加起来对自己就是 75%）
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.50, hasSeat: true, campWideBonusSum: 0.25);
        Assert.Equal(1.75, s, precision: 10);
    }

    [Fact]
    public void EffectiveSpeed_MultipleCampHolders_BonusesSum()
    {
        // 两个 L3 书虫在营 → 全营加成汇总 0.25+0.25=0.5；普通读者 → ×1.5
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideBonusSum: 0.50);
        Assert.Equal(1.50, s);
    }

    [Fact]
    public void EffectiveSpeed_NoSeat_And_Perk_And_Camp_AllStack()
    {
        // 基础 ×(1 + 自身 0.50 + 全营 0.25) × 无座 0.9
        double s = ReadingSpeed.Effective(2.0, selfBonus: 0.50, hasSeat: false, campWideBonusSum: 0.25);
        Assert.Equal(2.0 * (1.0 + 0.50 + 0.25) * ReadingSpeed.NoSeatMultiplier, s, precision: 10);
    }

    // ---------- BookData.ReadHours（每本书读完所需游戏内小时，draft） ----------

    [Fact]
    public void BookData_HasReadHours_Positive()
    {
        Assert.True(BookLibrary.WildernessSurvivalGuide().ReadHours > 0);
    }

    [Fact]
    public void BookData_LoreShorterThanTechnical()
    {
        // 技术书长、lore 短：日记(纯 lore) 应比技术工具书读得快
        Assert.True(BookLibrary.GoldfingerDiaryA().ReadHours < BookLibrary.WildernessSurvivalGuide().ReadHours);
    }
}
