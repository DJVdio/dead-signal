using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 通用书籍前置链纯逻辑单测：书可声明前置书，未读完前置时读该书**不禁止**、但读速 ×0.2（拟定待调）。
// 覆盖：无前置书不受影响 / 有前置未读→×0.2 / 有前置已读→正常 / 减速只影响耗时不改读满阈值 / 首条数据《进阶木匠技术》←《木匠入门》。

public class ReadingPrerequisiteTests
{
    private static BookData Book(string id, string? prereq = null, double readHours = 12)
        => new(id: id, title: id, body: "", grantsRecipeStub: null, readHours: readHours, prerequisiteBookId: prereq);

    [Fact]
    public void NoPrerequisite_FactorIsOne()
    {
        var b = Book("plain");
        Assert.Equal(1.0, ReadingSpeed.PrerequisiteFactor(b, _ => false));
    }

    [Fact]
    public void PrerequisiteUnread_FactorIsPenalty()
    {
        var b = Book("advanced", prereq: "basics");
        double f = ReadingSpeed.PrerequisiteFactor(b, _ => false); // 没读过任何书
        Assert.Equal(ReadingSpeed.MissingPrerequisiteMultiplier, f);
        Assert.Equal(0.2, f); // draft 系数
    }

    [Fact]
    public void PrerequisiteRead_FactorIsOne()
    {
        var b = Book("advanced", prereq: "basics");
        double f = ReadingSpeed.PrerequisiteFactor(b, id => id == "basics"); // 已读前置
        Assert.Equal(1.0, f);
    }

    [Fact]
    public void Penalty_ScalesEffectiveSpeed_ByFactor()
    {
        // 同样条件下，未读前置的有效读速正好是已读的 0.2 倍（其余项相同）。
        double full = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0, prerequisiteFactor: 1.0);
        double slowed = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0, prerequisiteFactor: 0.2);
        Assert.Equal(full * 0.2, slowed, 6);
    }

    [Fact]
    public void Effective_PrerequisiteFactorDefaultsToOne_BackwardCompatible()
    {
        // 旧调用（不传前置系数）行为不变：与显式传 1.0 相同。
        double legacy = ReadingSpeed.Effective(1.0, 0.5, hasSeat: false, campWideMult: 1.25);
        double explicitOne = ReadingSpeed.Effective(1.0, 0.5, hasSeat: false, campWideMult: 1.25, prerequisiteFactor: 1.0);
        Assert.Equal(legacy, explicitOne, 6);
    }

    [Fact]
    public void SlowSpeed_DoesNotDistortCompletion_JustTakesLonger()
    {
        // 减速只是每单位时间推进得少，读满阈值仍是 ReadHours（不因慢速失真）：慢速仍能读满、但耗时明显更多。
        double readHours = 20;
        double perTickHours = 1.0;
        double factor = ReadingSpeed.MissingPrerequisiteMultiplier; // 0.2

        int fastTicks = TicksToComplete(readHours, perTickHours * 1.0);   // 常速
        int slowTicks = TicksToComplete(readHours, perTickHours * factor); // 未读前置：×0.2

        Assert.True(fastTicks > 0);
        Assert.True(slowTicks > fastTicks, "慢速应耗更多 tick 才读满");
        // 约 5 倍（0.2 的倒数），允许浮点累加的 ±1 tick 余量，不写死精确值。
        Assert.InRange(slowTicks, fastTicks * 5 - 2, fastTicks * 5 + 2);
    }

    private static int TicksToComplete(double readHours, double hoursPerTick)
    {
        var progress = new ReadingProgress();
        int ticks = 0;
        while (!progress.IsComplete("book", readHours))
        {
            progress.Advance("book", hoursPerTick);
            ticks++;
            Assert.True(ticks <= 100000, "减速只增耗时、不改可完成性，不应无限循环");
        }
        return ticks;
    }

    [Fact]
    public void FirstDataPoint_AdvancedCarpentry_RequiresCarpentryBasics()
    {
        var advanced = BookLibrary.All().First(b => b.Id == "advanced_carpentry");
        Assert.Equal("carpentry_basics", advanced.PrerequisiteBookId);

        // 没读《木匠入门》→ 读《进阶木匠技术》×0.2；读了 → 正常。
        Assert.Equal(0.2, ReadingSpeed.PrerequisiteFactor(advanced, _ => false));
        Assert.Equal(1.0, ReadingSpeed.PrerequisiteFactor(advanced, id => id == "carpentry_basics"));
    }

    [Fact]
    public void CarpentryBasics_HasNoPrerequisite_ReadsAtFullSpeed()
    {
        var basics = BookLibrary.All().First(b => b.Id == "carpentry_basics");
        Assert.Null(basics.PrerequisiteBookId);
        Assert.Equal(1.0, ReadingSpeed.PrerequisiteFactor(basics, _ => false));
    }

    [Fact]
    public void AdvancedCarpentry_UnlocksSofaRecipe()
    {
        // 沙发是进阶木匠书名下的高级座位配方；门槛本体仍由 RecipeBook.RequiredBookIds 判定。
        var advanced = BookLibrary.All().First(b => b.Id == "advanced_carpentry");
        Assert.Equal("recipe:sofa", advanced.GrantsRecipeStub);
    }
}
