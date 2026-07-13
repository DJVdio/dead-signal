using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>围墙升级 / 修复</b>（<see cref="FenceUpgradeLogic"/>）—— 用户拍板那句
/// 「<b>墙不能建，只能升级开局自带的围栏</b>」的唯一落点。
///
/// <para>
/// <b>本类最重要的一组测试是"不能建墙"</b>（见 <c>不能变相建墙</c> 那几个）。理由不是洁癖，是设计底线：
/// 可自由摆墙 ⇒ 玩家能搭 <b>kill box</b>（用墙的迷宫牵着敌人寻路），会架空视野锥/噪音/包抄/掩体/岗哨整套系统。
/// 「<b>修复</b>」是唯一一条能把墙重新立起来的路，所以它必须<b>只能在原来就有墙的位置修</b> —— 这几条测试就是那把锁。
/// </para>
/// </summary>
public class FenceUpgradeTests
{
    private static List<FenceSegmentState> Run(int count, StructureTier tier, double hp = 1d)
        => Enumerable.Range(0, count).Select(_ => new FenceSegmentState(tier, hp)).ToList();

    private static Func<string, int> Rich => _ => 9999;
    private static Func<string, int> Broke => _ => 0;

    // ======================== 升级：按边下令、按格结算 ========================

    [Fact]
    public void 升级一条边_目标是最低档的下一档_且只动低于目标的那些格()
    {
        // 8 格基础 + 8 格加固的一面墙：目标 = 基础的下一档（加固），只动那 8 格基础的。
        var run = new List<FenceSegmentState>();
        run.AddRange(Run(8, StructureTier.FenceBasic));
        run.AddRange(Run(8, StructureTier.FenceReinforced));

        FenceWorkPlan plan = FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, run);

        Assert.Equal(StructureTier.FenceReinforced, plan.TargetTier);
        Assert.Equal(8, plan.Steps.Count);                                  // 已经够高的 8 格不重复收费
        Assert.All(plan.Steps, s => Assert.InRange(s.SegmentIndex, 0, 7));  // 动的正是那 8 格基础的
        Assert.All(plan.Steps, s => Assert.Equal(StructureTier.FenceReinforced, s.TargetTier));
    }

    [Fact]
    public void 升级不许跳档_一档一档往上()
    {
        Assert.Equal(StructureTier.FenceReinforced, FenceUpgradeLogic.NextTier(StructureTier.FenceBasic));
        Assert.Equal(StructureTier.FenceSheetMetal, FenceUpgradeLogic.NextTier(StructureTier.FenceReinforced));
        Assert.Equal(StructureTier.FenceFullMetal, FenceUpgradeLogic.NextTier(StructureTier.FenceSheetMetal));
        Assert.Null(FenceUpgradeLogic.NextTier(StructureTier.FenceFullMetal));

        // 基础围栏一步登天变全金属？不存在的（逐档累加的总成本正是"墙是不可逆重投入"的重量）。
        FenceWorkPlan plan = FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, Run(4, StructureTier.FenceBasic));
        Assert.All(plan.Steps, s => Assert.NotEqual(StructureTier.FenceFullMetal, s.TargetTier));
    }

    [Fact]
    public void 满档的墙_没得升()
    {
        FenceWorkPlan plan = FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, Run(16, StructureTier.FenceFullMetal));
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void 升级要付目标档的全额料_旧料一点不退()
    {
        FenceWorkPlan plan = FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, Run(16, StructureTier.FenceBasic));

        IReadOnlyDictionary<string, int> per = StructureBuildCost.Of(StructureTier.FenceReinforced);
        foreach (KeyValuePair<string, int> kv in per)
        {
            // 一条 16 格的边 = 16 份全额料。**旧木桩不折价**（与"墙零回收"同一口径）。
            Assert.Equal(kv.Value * 16, plan.TotalCost[kv.Key]);
        }
        Assert.Equal(16 * FenceUpgradeLogic.BuildWorkSeconds(StructureTier.FenceReinforced), plan.TotalWorkSeconds);
    }

    // ======================== 收益：这两条是升级唯一的理由，做丢了整个机制就白做 ========================

    [Fact]
    public void 升级的收益一_丧尸要啃更久_每格血量都变高()
    {
        Assert.True(CampStructureTable.MaxHp(StructureTier.FenceReinforced) > CampStructureTable.MaxHp(StructureTier.FenceBasic));
        Assert.True(CampStructureTable.MaxHp(StructureTier.FenceSheetMetal) > CampStructureTable.MaxHp(StructureTier.FenceReinforced));
        Assert.True(CampStructureTable.MaxHp(StructureTier.FenceFullMetal) > CampStructureTable.MaxHp(StructureTier.FenceSheetMetal));
    }

    [Fact]
    public void 升级的收益二_劫掠者静默拆除要更久_守夜人多几次发现机会()
    {
        SilentDismantleParams p = SilentDismantleParams.Default;
        double basic = SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, p);
        double full = SilentDismantleLogic.SecondsFor(StructureTier.FenceFullMetal, p);

        Assert.True(full > basic);
        Assert.Equal(basic + 3 * p.SecondsPerTier, full);                       // 每升一档 +20 秒
        Assert.True(SilentDismantleLogic.DetectionRolls(full, p) > SilentDismantleLogic.DetectionRolls(basic, p));
    }

    [Fact]
    public void 硬顺序_先大门后围栏_大门升一档远比一面墙便宜又快()
    {
        FenceWorkPlan gate = FenceUpgradeLogic.PlanUpgrade(
            CampStructureKind.Gate, Run(1, StructureTier.GateBasic));           // 大门 = 只有一格的边
        FenceWorkPlan wall = FenceUpgradeLogic.PlanUpgrade(
            CampStructureKind.Fence, Run(16, StructureTier.FenceBasic));        // 一面 16 格的墙

        Assert.False(gate.IsEmpty);
        Assert.True(gate.TotalWorkSeconds < wall.TotalWorkSeconds);
    }

    // ======================== 修复：被砸穿的墙怎么补 ========================

    [Fact]
    public void 被砸穿的格_修复恢复到原档_绝不借修复偷偷升档()
    {
        var run = new List<FenceSegmentState>
        {
            new(StructureTier.FenceSheetMetal, 1d),   // 完好
            new(StructureTier.FenceSheetMetal, 0d),   // 被砸穿了：这里现在是个洞
        };

        FenceWorkPlan plan = FenceUpgradeLogic.PlanRepair(CampStructureKind.Fence, run);

        FenceWorkStep step = Assert.Single(plan.Steps);
        Assert.Equal(1, step.SegmentIndex);
        Assert.Equal(StructureTier.FenceSheetMetal, step.TargetTier); // **原档**，不是下一档
        // 洞 = 缺 100% 血 ⇒ 按那一档全额重立一遍。
        Assert.Equal(StructureBuildCost.Of(StructureTier.FenceSheetMetal), step.Cost);
        Assert.Equal(FenceUpgradeLogic.BuildWorkSeconds(StructureTier.FenceSheetMetal), step.WorkSeconds);
    }

    [Fact]
    public void 修复按缺失血量收费_缺一半就付一半_向上取整且每样最少一个()
    {
        IReadOnlyDictionary<string, int> full = StructureBuildCost.Of(StructureTier.FenceReinforced);
        IReadOnlyDictionary<string, int> half = FenceUpgradeLogic.RepairCost(StructureTier.FenceReinforced, 0.5);

        foreach (KeyValuePair<string, int> kv in full)
        {
            Assert.Equal(Math.Max(1, (int)Math.Ceiling(kv.Value * 0.5)), half[kv.Key]);
        }

        // 擦破点皮也得钉两根钉子：**修墙永远不是白干的**（堵死"掉一点血再修"的无聊操作）。
        IReadOnlyDictionary<string, int> scratch = FenceUpgradeLogic.RepairCost(StructureTier.FenceReinforced, 0.99);
        Assert.All(scratch, kv => Assert.True(kv.Value >= 1));
        Assert.Equal(FenceUpgradeLogic.MinWorkSeconds,
            FenceUpgradeLogic.RepairWorkSeconds(StructureTier.FenceReinforced, 0.99));
    }

    [Fact]
    public void 完好无损的墙_没得修()
    {
        Assert.True(FenceUpgradeLogic.PlanRepair(CampStructureKind.Fence, Run(16, StructureTier.FenceBasic)).IsEmpty);
        Assert.Empty(FenceUpgradeLogic.RepairCost(StructureTier.FenceBasic, 1d));
    }

    // ======================== ⚠️ 不能变相建墙（kill box 的门，从这里关死）========================

    [Fact]
    public void 不能变相建墙_没有墙的地方永远造不出墙()
    {
        // 一条"空的边"——现实里根本不存在这种东西（围栏格只在建图时从 camp.json 诞生）。
        // 就算有人硬把空表喂进来：**出来的还是空计划**。修复造不出第一格墙。
        Assert.True(FenceUpgradeLogic.PlanRepair(CampStructureKind.Fence, new List<FenceSegmentState>()).IsEmpty);
        Assert.True(FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, new List<FenceSegmentState>()).IsEmpty);
    }

    [Fact]
    public void 不能变相建墙_计划只动既有的格_永远不会多出一格()
    {
        var run = new List<FenceSegmentState>
        {
            new(StructureTier.FenceBasic, 0d),    // 洞
            new(StructureTier.FenceBasic, 0.3),   // 快没了
            new(StructureTier.FenceBasic, 1d),    // 完好
        };

        foreach (FenceWorkPlan plan in new[]
        {
            FenceUpgradeLogic.PlanRepair(CampStructureKind.Fence, run),
            FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, run),
        })
        {
            // 步骤数不可能超过这条边本来的格数；下标恒在既有格的域内；同一格不会被排两次。
            Assert.True(plan.Steps.Count <= run.Count);
            Assert.All(plan.Steps, s => Assert.InRange(s.SegmentIndex, 0, run.Count - 1));
            Assert.Equal(plan.Steps.Count, plan.Steps.Select(s => s.SegmentIndex).Distinct().Count());
        }
    }

    [Fact]
    public void 不能变相建墙_规则层根本不知道墙在哪()
    {
        // FenceSegmentState 里**没有位置**（只有档次和血量）。一个不知道坐标的规则，造不出一堵墙来。
        // 这不是"我们没实现建墙"，是**签名里就没有那个参数** —— 结构性保证，不是自觉。
        string[] props = typeof(FenceSegmentState).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Position", props);
        Assert.DoesNotContain("Rect", props);
        Assert.Equal(new[] { nameof(StructureTier), "HealthFraction", "IsHole", "Missing" }.Length, props.Length);
    }

    [Fact]
    public void 不能变相建墙_围栏格只在建图时诞生_CampMain里只有唯一一处()
    {
        // 源码守卫 —— 这是<b>唯一能钉住 Godot 消费层</b>的办法（CampMain 引 Godot 类型，进不了单测）。
        // 整个 CampMain 的**代码**（注释不算）里：
        //   · 生出围栏格的调用只有一处（BuildFences 从 camp.json 切格）
        //   · 切格函数只有"一处声明 + 一处调用"
        // 谁要是在"升级 / 修复"里偷偷再加一格墙（哪怕只是为了"体验友好"），这条测试立刻红。
        // ⚠️ 红了别改这条测试的数字——先想清楚你是不是正在把 kill box 放回来。
        string code = StripComments(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "CampMain.cs")));
        Assert.Equal(1, Occurrences(code, "fence: true"));
        Assert.Equal(2, Occurrences(code, "SplitFence("));
    }

    [Fact]
    public void 门体不走这条路_它是装上去的东西_卸得下来()
    {
        Assert.False(FenceUpgradeLogic.CanImprove(CampStructureKind.Door));
        Assert.True(FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Door, Run(1, StructureTier.DoorWood)).IsEmpty);
        Assert.True(FenceUpgradeLogic.PlanRepair(CampStructureKind.Door, Run(1, StructureTier.DoorWood, 0d)).IsEmpty);
    }

    // ======================== 料够不够 ========================

    [Fact]
    public void 料不够就开不了工_而且要说清还缺什么()
    {
        FenceWorkPlan plan = FenceUpgradeLogic.PlanUpgrade(CampStructureKind.Fence, Run(16, StructureTier.FenceBasic));

        Assert.True(FenceUpgradeLogic.CanAfford(plan.TotalCost, Rich));
        Assert.False(FenceUpgradeLogic.CanAfford(plan.TotalCost, Broke));

        IReadOnlyDictionary<string, int> lack = FenceUpgradeLogic.Missing(plan.TotalCost, Broke);
        Assert.Equal(plan.TotalCost, lack);                                     // 一点没有 ⇒ 全缺
        Assert.Empty(FenceUpgradeLogic.Missing(plan.TotalCost, Rich));
    }

    // ======================== 菜单：成本与收益，玩家按下之前就得看见 ========================

    [Fact]
    public void 菜单把成本和收益都写出来_玩家要能看出值不值()
    {
        IReadOnlyList<SiteActionOption> opts = SiteActions.ForOwnStructure(
            Faction.Survivor, isAnimal: false, CampStructureKind.Fence,
            Run(16, StructureTier.FenceBasic), Rich);

        SiteActionOption up = Assert.Single(opts.Where(o => o.Action == SiteAction.Upgrade));
        Assert.True(up.Enabled);
        Assert.Contains("150", up.Hint);                     // 血量 X → Y
        Assert.Contains("250", up.Hint);
        Assert.Contains("秒", up.Hint);                       // 要干多久
        Assert.Contains("料：", up.Hint);                     // 要多少料
        Assert.Contains("木料", up.Hint);                     // 人话，不是 "wood"
    }

    [Fact]
    public void 料不够时_加固照样列出来_只是灰掉并写明还缺什么()
    {
        IReadOnlyList<SiteActionOption> opts = SiteActions.ForOwnStructure(
            Faction.Survivor, isAnimal: false, CampStructureKind.Fence,
            Run(16, StructureTier.FenceBasic), Broke);

        SiteActionOption up = Assert.Single(opts.Where(o => o.Action == SiteAction.Upgrade));
        Assert.False(up.Enabled);                            // 按不下去
        Assert.Contains("还缺", up.DisabledReason);           // 但告诉他缺什么 —— 藏起来他就学不会
    }

    [Fact]
    public void 有洞的墙_菜单给修补_并说清楚有几个洞()
    {
        var run = new List<FenceSegmentState>
        {
            new(StructureTier.FenceBasic, 1d),
            new(StructureTier.FenceBasic, 0d),   // 洞
            new(StructureTier.FenceBasic, 0d),   // 洞
        };

        IReadOnlyList<SiteActionOption> opts = SiteActions.ForOwnStructure(
            Faction.Survivor, isAnimal: false, CampStructureKind.Fence, run, Rich);

        SiteActionOption fix = Assert.Single(opts.Where(o => o.Action == SiteAction.Repair));
        Assert.True(fix.Enabled);
        Assert.Contains("2 格是洞", fix.Hint);
        Assert.Contains("不升档", fix.Hint);
    }

    [Fact]
    public void 满档又完好的墙_两项都没得干_加固灰着写明已到顶()
    {
        IReadOnlyList<SiteActionOption> opts = SiteActions.ForOwnStructure(
            Faction.Survivor, isAnimal: false, CampStructureKind.Fence,
            Run(4, StructureTier.FenceFullMetal), Rich);

        Assert.DoesNotContain(opts, o => o.Action == SiteAction.Repair);
        SiteActionOption up = Assert.Single(opts.Where(o => o.Action == SiteAction.Upgrade));
        Assert.False(up.Enabled);
        Assert.Contains("最高档", up.DisabledReason);
    }

    [Fact]
    public void 丧尸和狗不会砌墙()
    {
        Assert.Empty(SiteActions.ForOwnStructure(
            Faction.Zombie, isAnimal: false, CampStructureKind.Fence, Run(4, StructureTier.FenceBasic), Rich));
        Assert.Empty(SiteActions.ForOwnStructure(
            Faction.Survivor, isAnimal: true, CampStructureKind.Fence, Run(4, StructureTier.FenceBasic), Rich));
    }

    // ---- 源码守卫的小工具 ----

    private static string RepoRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    /// <summary>把行注释剥掉（守卫只数**代码**里的调用；解释这条规矩的注释本身当然会提到它）。</summary>
    private static string StripComments(string src)
        => string.Join('\n', src.Split('\n').Select(line =>
        {
            string t = line.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("///", StringComparison.Ordinal))
            {
                return "";
            }
            int i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }));

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }
        return n;
    }
}
