using System;
using System.IO;
using System.Runtime.CompilerServices;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class VisualProductionAssetTests
{
    [Fact]
    public void EveryRuntimeActorKindHasAFrameAtlas()
    {
        string[] names = { "山姆", "诺蒂", "克莉丝汀", "耗子", "道格", "南丁格尔", "皮特", "布鲁斯" };
        foreach (string name in names)
            AssertAsset(ActorFrameCatalog.PathFor(name, "survivor"));
        foreach (string kind in new[] { "survivor", "raider", "zombie", "dog" })
            AssertAsset(ActorFrameCatalog.PathFor(null, kind));
    }

    [Fact]
    public void EveryAnimationStateMapsInsideTheTwelveColumnAtlas()
    {
        foreach (ActorAnimationState state in Enum.GetValues<ActorAnimationState>())
            Assert.InRange(ActorFrameCatalog.ColumnFor(state, 1.25, 0.75f), 0, ActorFrameCatalog.Columns - 1);
        for (int direction = 0; direction < 8; direction++)
            Assert.InRange(ActorFrameCatalog.RowForDirection(direction), 0, ActorFrameCatalog.Rows - 1);
    }

    [Fact]
    public void ExplorationAndAllThreeEndingBackgroundsExist()
    {
        AssertAsset("res://assets/world/exploration-props.png");
        AssertAsset("res://assets/cg/military-escape.png");
        AssertAsset("res://assets/cg/horde-escape.png");
        AssertAsset("res://assets/cg/family-win.png");
    }

    [Fact]
    public void RuntimeWiresFrameAtlasEnvironmentPropsAndEndingBackgrounds()
    {
        string actor = Read("godot/scripts/ActorSprite.cs");
        string level = Read("godot/scripts/TestExploration.cs");
        string panel = Read("godot/scripts/EndingPanel.cs");
        string bad = Read("godot/scripts/CampMain.SouthEscape.cs");
        string win = Read("godot/scripts/CampMain.FamilyEscape.cs");
        Assert.Contains("DrawAnimationFrame", actor);
        Assert.Contains("SetupFormalEnvironmentArt();", level);
        Assert.Contains("new ExplorationPropSprite", level);
        Assert.Contains("_backgroundPath", panel);
        Assert.Contains("horde-escape.png", bad);
        Assert.Contains("military-escape.png", bad);
        Assert.Contains("family-win.png", win);
    }

    private static void AssertAsset(string resourcePath)
        => Assert.True(File.Exists(Path.Combine(RepoRoot(), "godot", resourcePath[6..].Replace('/', Path.DirectorySeparatorChar))), resourcePath);

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string RepoRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
