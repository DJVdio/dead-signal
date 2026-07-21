using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 自动存档的轮转策略。
/// <para>
/// 用户拍板：<b>只有自动存档（相位切换，一天两次），读档随时</b>——玩家没有"存档"这个动作。
/// 这些测试钉的就是这条规则的两个后果：①存档只在那两顿饭落地；②必须留一串历史档
/// （玩家没有手动存档可以兜底，单槽覆盖会把他永久锁死在一个已经输了的局面里）。
/// </para>
/// </summary>
public class SaveRotationTests
{
    // ---- 一天存两次：哪两次 ----

    [Fact]
    public void 只在清晨聚餐与黄昏聚餐存档()
    {
        Assert.True(SaveRotation.ShouldAutosaveAt(DayPhase.DawnMeal));
        Assert.True(SaveRotation.ShouldAutosaveAt(DayPhase.DuskMeal));
    }

    [Fact]
    public void 其余六个相位一概不存()
    {
        // 一天只有白天/黑夜两个相位，两顿边界聚餐各存一次。
        DayPhase[] noSave =
        {
            DayPhase.DayPrep, DayPhase.DayTravel, DayPhase.DayExplore,
            DayPhase.DayReturn, DayPhase.NightPrep, DayPhase.NightAct,
        };
        foreach (DayPhase p in noSave)
        {
            Assert.False(SaveRotation.ShouldAutosaveAt(p), $"{p} 不该触发自动存档");
        }
    }

    [Fact]
    public void 走完一整天恰好存两次()
    {
        // 把一天的全部内部流程节点走一遍，数一数落地了几次，钉死一天两次。
        DayPhase[] fullDay =
        {
            DayPhase.DawnMeal, DayPhase.DayPrep, DayPhase.DayTravel, DayPhase.DayExplore,
            DayPhase.DayReturn, DayPhase.DuskMeal, DayPhase.NightPrep, DayPhase.NightAct,
        };

        int saves = fullDay.Count(SaveRotation.ShouldAutosaveAt);

        Assert.Equal(2, saves);
    }

    // ---- 槽名 ----

    [Fact]
    public void 槽名一眼能看出是哪一天的哪顿饭()
    {
        Assert.Equal("auto_d12_DuskMeal", SaveRotation.SlotNameFor(12, DayPhase.DuskMeal));
        Assert.Equal("auto_d1_DawnMeal", SaveRotation.SlotNameFor(1, DayPhase.DawnMeal));
    }

    [Fact]
    public void 天数单调递增所以槽名不会撞()
    {
        var names = new HashSet<string>();
        for (int day = 1; day <= 40; day++)
        {
            Assert.True(names.Add(SaveRotation.SlotNameFor(day, DayPhase.DawnMeal)));
            Assert.True(names.Add(SaveRotation.SlotNameFor(day, DayPhase.DuskMeal)));
        }
        Assert.Equal(80, names.Count);   // 40 天 × 2 顿饭，无一重名
    }

    // ---- 轮转：留一串历史档 ----

    [Fact]
    public void 保留最近六个更老的淘汰()
    {
        // 一天两次 ⇒ 6 个 ≈ 回退三天。
        var slots = Enumerable.Range(0, 10)
            .Select(i => ($"auto_d{10 - i}_DuskMeal", $"2026-07-{20 - i:00}T09:00:00Z"))
            .ToList();   // 已按时刻从新到旧

        IReadOnlyList<string> prune = SaveRotation.SlotsToPrune(slots);

        Assert.Equal(4, prune.Count);                    // 10 个里淘汰 4 个
        Assert.DoesNotContain(slots[0].Item1, prune);    // 最新的留着
        Assert.DoesNotContain(slots[5].Item1, prune);    // 第 6 个（边界）留着
        Assert.Contains(slots[6].Item1, prune);          // 第 7 个开始淘汰
        Assert.Contains(slots[9].Item1, prune);          // 最老的必淘汰
    }

    [Fact]
    public void 不足六个时一个都不删()
    {
        // 玩家没有手动存档可以兜底——删早了就是永久失去一条退路。
        var slots = new List<(string, string)>
        {
            ("auto_d2_DuskMeal", "2026-07-13T20:00:00Z"),
            ("auto_d2_DawnMeal", "2026-07-13T08:00:00Z"),
            ("auto_d1_DuskMeal", "2026-07-12T20:00:00Z"),
        };

        Assert.Empty(SaveRotation.SlotsToPrune(slots));
    }

    [Fact]
    public void 轮转只碰自动存档槽不碰别的文件()
    {
        // 存档目录里若躺着别的东西（玩家自己备份的、调试用的），轮转不该顺手删掉。
        var slots = new List<(string, string)>();
        for (int i = 0; i < 8; i++)
        {
            slots.Add(($"auto_d{8 - i}_DuskMeal", $"2026-07-{20 - i:00}T09:00:00Z"));
        }
        slots.Add(("my_backup", "2026-07-01T09:00:00Z"));   // 最老，但不是自动存档

        IReadOnlyList<string> prune = SaveRotation.SlotsToPrune(slots);

        Assert.DoesNotContain("my_backup", prune);
        Assert.Equal(2, prune.Count);   // 8 个自动档里只淘汰 2 个
    }

    [Fact]
    public void 认得出哪些槽是自动存档()
    {
        Assert.True(SaveRotation.IsAutosaveSlot("auto_d12_DuskMeal"));
        Assert.False(SaveRotation.IsAutosaveSlot("my_backup"));
    }
}
