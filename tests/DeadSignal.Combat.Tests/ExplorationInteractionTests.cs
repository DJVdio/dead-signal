using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 探索期输入/目的地威胁配置护栏。
///
/// 营地与探索关统一使用 faux-isometric 表现、逻辑仍是 cartesian 平面；输入门控不能复用
/// <c>Pawn.IsControllable</c>（远征队角色的 Role=Expedition）。这些规则先由纯逻辑钉住，
/// Godot 层只负责把鼠标世界坐标和发现点事件接到同一条路径。
/// </summary>
public sealed class ExplorationInteractionTests
{
    [Theory]
    [InlineData(PawnRole.Idle, false, true)]
    [InlineData(PawnRole.Expedition, false, false)]
    [InlineData(PawnRole.Expedition, true, true)]
    [InlineData(PawnRole.Idle, true, false)]
    [InlineData(PawnRole.Guard, true, false)]
    public void ControlGate_ExplorationUsesExpeditionRoleOnly(PawnRole role, bool inExploration, bool expected)
        => Assert.Equal(expected, ExplorationInteractionLogic.CanControl(role, inExploration));

    [Fact]
    public void NarrativeTrigger_ClickNeverFiresFromWalkingThrough()
    {
        Assert.True(ExplorationInteractionLogic.TriggersOnBodyEntered(NarrativeTrigger.Proximity));
        Assert.False(ExplorationInteractionLogic.TriggersOnBodyEntered(NarrativeTrigger.Click));
    }

    [Fact]
    public void FauxIsoMousePosition_UsesInjectedInverse_InCampAndExploration()
    {
        var screen = new System.Numerics.Vector2(31.5f, 42.25f);
        Assert.Equal(new System.Numerics.Vector2(1030.5f, 1041.25f),
            ExplorationInteractionLogic.WorldPoint(screen, p => p + new System.Numerics.Vector2(999, 999)));
        Assert.Equal(new System.Numerics.Vector2(1030.5f, 1042.25f),
            ExplorationInteractionLogic.WorldPoint(screen, p => p + new System.Numerics.Vector2(999, 1000)));
    }

    [Fact]
    public void DiscoveryTarget_IsRepeatableForLootCachesAndCorpses()
    {
        Assert.True(ExplorationInteractionLogic.IsRepeatableDiscovery("cache:riverside"));
        Assert.True(ExplorationInteractionLogic.IsRepeatableDiscovery("尸体的尸体 #1"));
        Assert.False(ExplorationInteractionLogic.IsRepeatableDiscovery("narrative:old-wall"));
    }

    [Fact]
    public void RealWorldGraph_EveryDestinationHasDangerAndEnemyCountConfig()
    {
        string path = Path.Combine(RepoRoot(), "godot", "data", "world_graph.json");
        WorldGraph graph = WorldGraph.FromJson(File.ReadAllText(path));

        Assert.NotEmpty(graph.Nodes);
        Assert.All(graph.Nodes, node =>
        {
            Assert.True(node.Danger.HasValue, $"目的地「{node.Display}」缺 DangerTier");
            Assert.True(node.EnemyCount >= 0, $"目的地「{node.Display}」缺敌数配置");
        });
    }

    [Fact]
    public void ThreatProfile_MapsDangerToStableEnemyBand()
    {
        Assert.Equal(3, ExplorationThreatProfile.EnemyCountFor(DangerTier.Low));
        Assert.Equal(6, ExplorationThreatProfile.EnemyCountFor(DangerTier.Medium));
        Assert.Equal(10, ExplorationThreatProfile.EnemyCountFor(DangerTier.High));
    }

    [Fact]
    public void RuntimeWiresClickSpotsToRightClickArrivalAndExplorationHover()
    {
        string level = File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "TestExploration.cs"));
        string camp = File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "CampMain.cs"));

        Assert.Contains("trigger: spot.Trigger", level);
        Assert.Contains("ExplorationInteractionLogic.TriggersOnBodyEntered(trigger)", level);
        Assert.Contains("DiscoveryTrigger == NarrativeTrigger.Click", camp);
        Assert.Contains("level.TriggerDiscovery(hit.Name, arriver)", camp);
        Assert.Contains("!target.Visual.Visible", level);
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "DeadSignal.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
