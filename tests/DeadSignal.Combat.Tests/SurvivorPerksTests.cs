using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 专属效果（诺蒂·书虫样板）+ 读书进度 + 读速合成纯逻辑单测。
/// 锁的是规则形态：书虫按累计阅读时间跨阈值升级、各级读速倍率、L3 全营加成；
/// 读书进度按 (读者,书) 累计且跨夜不清零；有效读速 = 基础 × (1+自身) × 全营乘子 × 穿戴品乘子 × 座位 × 前置（§2 全乘算）。
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
        Assert.Equal(0.75, BookwormPerk.BonusForLevel(2));
        Assert.Equal(0.75, BookwormPerk.BonusForLevel(3)); // L3 自身与 L2 相同（升级点在全营加成）
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
        Assert.Equal(0.75, perk.ReadingSpeedBonus);
        Assert.Equal(0.0, perk.CampWideReadingSpeedBonus); // L2 仍无全营加成
    }

    [Fact]
    public void AddReadingTime_CrossingLevel3Threshold_UnlocksCampWideBonus()
    {
        var perk = new BookwormPerk();
        perk.AddReadingTime(BookwormPerk.Level3ThresholdHours);
        Assert.Equal(3, perk.Level);
        Assert.Equal(0.75, perk.ReadingSpeedBonus);        // L3 自身仍 +75%（不再涨）
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

    // 🔴 [加算残留整改·诺蒂读速] 读速改 §2 全乘算：campWideMult 现是**乘子**(∏(1+各L3书虫贡献))，非旧加成和。
    //    单来源不变（自身×1.75、单书虫×1.25、无座×0.9），多来源由加算→乘算：诺蒂L3 ×2.1875、双书虫 ×1.5625。

    [Fact]
    public void EffectiveSpeed_NoPerk_Seated_IsBase()
    {
        double s = ReadingSpeed.Effective(baseSpeed: 1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0);
        Assert.Equal(1.0, s);
    }

    [Fact]
    public void EffectiveSpeed_NoSeat_AppliesPenalty()
    {
        double s = ReadingSpeed.Effective(1.0, 0.0, hasSeat: false, campWideMult: 1.0);
        Assert.Equal(ReadingSpeed.NoSeatMultiplier, s); // 无座 -10%
    }

    [Fact]
    public void EffectiveSpeed_Level2_Seated()
    {
        // L2 自身 +75% → 单因子 ×1.75（无全营 = 乘子 1.0）
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.75, hasSeat: true, campWideMult: 1.0);
        Assert.Equal(1.75, s);
    }

    [Fact]
    public void EffectiveSpeed_CampWideBonus_AppliesToReader()
    {
        // 某普通读者(自身无 perk=0)受营内某 L3 书虫的全营 +25% → 乘子 1.25 → ×1.25
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.25);
        Assert.Equal(1.25, s);
    }

    [Fact]
    public void EffectiveSpeed_TinoL3_Seated_IsSeventyFivePercent()
    {
        // 诺蒂 L3 有座：基础 × 自身(1+0.75) × 全营乘子(1.25) = 1.75 × 1.25 = ×2.1875
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.75, hasSeat: true, campWideMult: 1.25);
        Assert.Equal(2.1875, s, precision: 10);
    }

    [Fact]
    public void EffectiveSpeed_MultipleCampHolders_BonusesMultiply()
    {
        // 两个 L3 书虫在营 → 全营乘子 (1+0.25)×(1+0.25)=1.5625；普通读者 → ×1.5625（§2 全乘算，替代旧加算 ×1.5）
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.25 * 1.25);
        Assert.Equal(1.5625, s, precision: 10);
    }

    [Fact]
    public void EffectiveSpeed_NoSeat_And_Perk_And_Camp_AllStack()
    {
        // 基础 × 自身(1+0.75) × 全营乘子(1.25) × 无座 0.9
        double s = ReadingSpeed.Effective(2.0, selfBonus: 0.75, hasSeat: false, campWideMult: 1.25);
        Assert.Equal(2.0 * 1.75 * 1.25 * ReadingSpeed.NoSeatMultiplier, s, precision: 10);
    }

    [Fact]
    public void EffectiveSpeed_ApparelMult_MultipliesIn()
    {
        // [装备→能力加成] 平光眼镜 ×1.05 作独立乘子并入：诺蒂 L3 + 平光眼镜 = 1.75 × 1.25 × 1.05 = ×2.296875。
        double s = ReadingSpeed.Effective(1.0, selfBonus: 0.75, hasSeat: true, campWideMult: 1.25, apparelMult: 1.05);
        Assert.Equal(2.296875, s, precision: 10);
    }

    // ---------- BookData.ReadHours（每本书读完所需游戏内小时，draft） ----------

    [Fact]
    public void BookData_HasReadHours_Positive()
    {
        Assert.True(BookLibrary.WildernessSurvivalGuide().ReadHours > 0);
    }

    /// <summary>
    /// 🔴 <b>[T59] 原来那条「日记必须比技术书读得快」的不变量已<u>作废并重写</u> —— 因为它问错了问题。</b>
    ///
    /// <para>它假设「日记是一种读得快的书」，于是去比两者的工时。<b>用户澄清后这个前提整个塌了</b>：
    /// <list type="bullet">
    /// <item><b>书</b>给<b>角色</b>读 —— 代价是**角色的时间**（整夜占座位，不能站岗、不能干活）。</item>
    /// <item><b>日记</b>给<b>玩家</b>读 —— 是**道具**，点开就看，游戏冻结着，**零角色时间**。</item>
    /// </list>
    /// ⇒ 日记<b>根本不该有"阅读工时"这个字段</b>，「日记比书读得快多少」是个**没有意义的比较**。
    /// （此前它被做成了 <c>readHours: 6</c> 的书，还会出现在夜间读书指派列表里 —— 那是个真 bug，已修。）</para>
    ///
    /// <para>故本测试改成钉住<b>新语义</b>：日记不是书 / 日记没有工时 / 日记不进"派谁去读"的清单。</para>
    /// </summary>
    [Fact]
    public void 日记不是书_它没有阅读工时_也不吃角色的时间()
    {
        foreach (BookData d in BookLibrary.Diaries())
        {
            Assert.True(d.IsDiary, $"《{d.Title}》应被归类为日记（道具），不是书");

            // 🔴 日记没有"阅读工时"——它不由角色去读，这个字段对它不适用。
            Assert.Equal(0.0, d.ReadHours, 6);

            // 日记什么也不解锁：它是叙事，不是能力（能力只由 authored 专属效果 + 读过的书承载）。
            Assert.Null(d.GrantsRecipeStub);
        }

        // 日记**不在**"真正的书"里 ⇒ 派不了人去读它（CampMain.PopulateReadingPanel 拿 Manuals 筛）。
        Assert.DoesNotContain(BookLibrary.Manuals(), b => b.IsDiary);

        // 但它仍然在库存目录里（掉落/存档/图标照旧走书那条线，只是不再是"一件可以派人干的活"）。
        Assert.Contains(BookLibrary.All(), b => b.Id == "goldfinger_diary_a");
    }

    /// <summary>
    /// 把日记摘出去之后，**真正的书**这一侧的不变量还成不成立：<b>每一本书都必须有正的阅读工时</b>
    /// —— 工时就是书的代价，一本零工时的"书"＝白送的能力。
    /// </summary>
    [Fact]
    public void 真正的书_每一本都必须有正的阅读工时()
    {
        foreach (BookData b in BookLibrary.Manuals())
        {
            Assert.True(b.ReadHours > 0, $"《{b.Title}》的阅读工时是 {b.ReadHours} —— 书的代价就是工时，不能为零");
        }
    }
}
