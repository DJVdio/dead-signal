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
            Assert.Equal(direction, ActorFrameCatalog.RowForDirection(direction));
    }

    [Fact]
    public void EveryFrameAtlasIsAnExactTwelveByEightGrid()
    {
        string[] names = { "山姆", "诺蒂", "克莉丝汀", "耗子", "道格", "南丁格尔", "皮特", "布鲁斯" };
        foreach (string name in names)
            AssertPngGrid(ActorFrameCatalog.PathFor(name, "survivor"));
        foreach (string kind in new[] { "survivor", "raider", "zombie", "dog" })
            AssertPngGrid(ActorFrameCatalog.PathFor(null, kind));
    }

    [Fact]
    public void EveryArmedHumanHasAnExactSevenActionByEightDirectionAtlas()
    {
        string[] names = { "山姆", "诺蒂", "克莉丝汀", "耗子", "道格", "南丁格尔", "皮特" };
        foreach (string name in names)
            AssertAttackPngGrid(ActorAttackFrameCatalog.PathFor(name, "survivor"));
        foreach (string kind in new[] { "survivor", "raider" })
            AssertAttackPngGrid(ActorAttackFrameCatalog.PathFor(null, kind));

        Assert.Equal(0, ActorAttackFrameCatalog.FrameFor(0f));
        Assert.Equal(0, ActorAttackFrameCatalog.FrameFor(0.279f));
        Assert.Equal(1, ActorAttackFrameCatalog.FrameFor(0.28f));
        Assert.Equal(1, ActorAttackFrameCatalog.FrameFor(0.619f));
        Assert.Equal(2, ActorAttackFrameCatalog.FrameFor(0.62f));
        Assert.Equal(2, ActorAttackFrameCatalog.FrameFor(1f));
        foreach (WeaponAttackPose pose in Enum.GetValues<WeaponAttackPose>())
        {
            if (pose == WeaponAttackPose.None) continue;
            Assert.InRange(ActorAttackFrameCatalog.ColumnFor(pose, 0.45f), 0, ActorAttackFrameCatalog.Columns - 1);
        }
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
        Assert.DoesNotContain("SourceColumnForDirection", actor);
        Assert.Contains("SetupFormalEnvironmentArt();", level);
        Assert.Contains("new ExplorationPropSprite", level);
        Assert.Contains("_backgroundPath", panel);
        Assert.Contains("horde-escape.png", bad);
        Assert.Contains("military-escape.png", bad);
        Assert.Contains("family-win.png", win);
    }

    private static void AssertAsset(string resourcePath)
        => Assert.True(File.Exists(Path.Combine(RepoRoot(), "godot", resourcePath[6..].Replace('/', Path.DirectorySeparatorChar))), resourcePath);

    private static void AssertPngGrid(string resourcePath)
    {
        string path = Path.Combine(RepoRoot(), "godot", resourcePath[6..].Replace('/', Path.DirectorySeparatorChar));
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 16;
        int width = ReadBigEndianInt32(reader);
        int height = ReadBigEndianInt32(reader);
        Assert.Equal(0, width % ActorFrameCatalog.Columns);
        Assert.Equal(0, height % ActorFrameCatalog.Rows);
        Assert.Equal(128, width / ActorFrameCatalog.Columns);
        Assert.Equal(147, height / ActorFrameCatalog.Rows);
    }

    private static void AssertAttackPngGrid(string resourcePath)
    {
        string path = Path.Combine(RepoRoot(), "godot", resourcePath[6..].Replace('/', Path.DirectorySeparatorChar));
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 16;
        int width = ReadBigEndianInt32(reader);
        int height = ReadBigEndianInt32(reader);
        Assert.Equal(128 * ActorAttackFrameCatalog.Actions * ActorAttackFrameCatalog.FramesPerAction, width);
        Assert.Equal(147 * ActorAttackFrameCatalog.Rows, height);
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string RepoRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
