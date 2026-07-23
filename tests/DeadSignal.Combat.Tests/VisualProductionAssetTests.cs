using System;
using System.IO;
using System.Linq;
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

        for (int model = 0; model < EnemyVisualModels.Count; model++)
        {
            AssertAsset(ActorFrameCatalog.PathFor(null, "zombie", model));
            AssertAsset(ActorFrameCatalog.PathFor(null, "raider", model));
        }
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

        for (int model = 0; model < EnemyVisualModels.Count; model++)
        {
            AssertPngGrid(ActorFrameCatalog.PathFor(null, "zombie", model));
            AssertPngGrid(ActorFrameCatalog.PathFor(null, "raider", model));
        }
    }

    [Fact]
    public void EveryArmedHumanHasAnExactSevenActionByEightDirectionAtlas()
    {
        string[] names = { "山姆", "诺蒂", "克莉丝汀", "耗子", "道格", "南丁格尔", "皮特" };
        foreach (string name in names)
            AssertAttackPngGrid(ActorAttackFrameCatalog.PathFor(name, "survivor"));
        foreach (string kind in new[] { "survivor", "raider" })
            AssertAttackPngGrid(ActorAttackFrameCatalog.PathFor(null, kind));
        for (int model = 0; model < EnemyVisualModels.Count; model++)
            AssertAttackPngGrid(ActorAttackFrameCatalog.PathFor(null, "raider", model));

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
    public void EnemyModelsAreEightUniformlyAddressableVariants()
    {
        Assert.Equal(8, EnemyVisualModels.Count);
        Assert.Equal(0, EnemyVisualModels.Pick(new SequenceRandomSource(0)));
        Assert.Equal(0, EnemyVisualModels.Pick(new SequenceRandomSource(0.999)));
        Assert.Equal(1, EnemyVisualModels.Pick(new SequenceRandomSource(1.0)));
        Assert.Equal(7, EnemyVisualModels.Pick(new SequenceRandomSource(7.999)));
        Assert.Equal(7, EnemyVisualModels.Normalize(999));
        Assert.Equal(0, EnemyVisualModels.Normalize(-1));
        Assert.Equal(4, Enumerable.Range(0, EnemyVisualModels.Count).Count(EnemyVisualModels.IsFemale));
        Assert.Equal(4, Enumerable.Range(0, EnemyVisualModels.Count).Count(i => !EnemyVisualModels.IsFemale(i)));
    }

    [Fact]
    public void EnemyFactoriesAndRendererWireRandomModelsAndRealRaiderGear()
    {
        string zombie = Read("godot/scripts/Zombie.cs");
        string raider = Read("godot/scripts/Raider.cs");
        string sprite = Read("godot/scripts/ActorSprite.cs");
        Assert.Contains("VisualModelIndex = EnemyVisualModels.Pick", zombie);
        Assert.Contains("VisualModelIndex = EnemyVisualModels.Pick", raider);
        Assert.Contains("_actor.VisualModelIndex", sprite);
        Assert.Contains("raider.CurrentAttackWeapon", sprite);
        Assert.Contains("foreach (ArmorLayer armor in raider.WornArmor)", sprite);
    }

    [Fact]
    public void EveryPaperDollAtlasReferencedByTheFormalCatalogExists()
    {
        string[] paths = EquipmentVisualCatalog.WeaponNames
            .Select(name => EquipmentVisualCatalog.ResolveWeapon(name)!.AtlasPath)
            .Concat(EquipmentVisualCatalog.ApparelNames.Select(name => EquipmentVisualCatalog.ResolveApparel(name)!.AtlasPath))
            .Concat(EquipmentVisualCatalog.DogApparelNames.Select(name => EquipmentVisualCatalog.ResolveDogApparel(name)!.AtlasPath))
            .Concat(new[]
            {
                EquipmentVisualCatalog.ResolveLight(LightSource.FlashlightKey)!.AtlasPath,
                EquipmentVisualCatalog.ResolveLight(LightSource.TorchKey)!.AtlasPath,
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(paths);
        foreach (string path in paths)
            AssertAsset(path);
    }

    [Fact]
    public void WorldActorsUseAuthoredFixedOutfitsAndNeverDrawWornApparel()
    {
        string sprite = Read("godot/scripts/ActorSprite.cs");
        Assert.Contains("ActorFrameCatalog.PathFor", sprite);
        Assert.Contains("ActorAttackFrameCatalog.PathFor", sprite);
        Assert.Contains("DrawHeldEquipmentPass", sprite);
        Assert.DoesNotContain("DrawWornEquipment", sprite);
        Assert.DoesNotContain("DrawWornCell", sprite);
        Assert.DoesNotContain("ResolveApparel", sprite);
        Assert.DoesNotContain("PaperDollPrototype", sprite);
    }

    [Fact]
    public void WeaponActionAuditCoversEveryPoseDirectionAndKeyFrame()
    {
        string audit = Read("godot/scripts/WeaponActionAudit.cs");
        foreach (string character in new[]
                 {
                     "山姆", "诺蒂", "克莉丝汀", "耗子", "道格", "南丁格尔", "皮特",
                     "袭击者 01", "袭击者 02", "袭击者 03", "袭击者 04",
                     "袭击者 05", "袭击者 06", "袭击者 07", "袭击者 08",
                 })
            Assert.Contains($"\"{character}\"", audit);
        Assert.Contains("DEAD_SIGNAL_AUDIT_CHARACTER", audit);
        Assert.Contains("DEAD_SIGNAL_AUDIT_PHASE", audit);
        Assert.Contains("DEAD_SIGNAL_AUDIT_SCREENSHOT", audit);
        foreach (string pose in new[]
                 {
                     "OneHandSwing", "OneHandThrust", "OneHandShot",
                     "TwoHandSwing", "TwoHandThrust", "TwoHandShot", "BowShot",
                 })
            Assert.Contains($"WeaponAttackPose.{pose}", audit);
        Assert.Contains("for (int direction = 0; direction < 8; direction++)", audit);
        Assert.Contains("float[] keyFrames = { 0.12f, 0.45f, 0.82f };", audit);
        Assert.Contains("SetAuditAttackFrame", audit);
    }

    [Fact]
    public void ExplorationAndAllThreeEndingBackgroundsExist()
    {
        AssertAsset("res://assets/world/exploration-props.png");
        AssertAsset("res://assets/world/site-specific-exploration-props.png");
        AssertPngSize("res://assets/world/site-specific-exploration-props.png", 1536, 1024);
        AssertAsset("res://assets/cg/military-escape.png");
        AssertAsset("res://assets/cg/horde-escape.png");
        AssertAsset("res://assets/cg/family-win.png");
        foreach (string cinematic in new[]
                 {
                     "res://assets/world/cinematics/horde-overview.png",
                     "res://assets/world/cinematics/canyon-bridge-raised.png",
                     "res://assets/world/cinematics/canyon-bridge-lowered.png",
                 })
            AssertPngSize(cinematic, 1672, 941);
    }

    [Fact]
    public void EveryAuthoredSurvivorHasANamedCardPortrait()
    {
        foreach (string name in new[] { "山姆", "诺蒂", "克莉丝汀", "耗子", "道格", "南丁格尔", "皮特" })
        {
            string relative = SurvivorCardVisuals.PortraitFileFor(name, 999);
            string path = Path.Combine(RepoRoot(), "godot", "assets", "portraits", relative.Replace('/', Path.DirectorySeparatorChar));
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            stream.Position = 16;
            Assert.Equal(365, ReadBigEndianInt32(reader));
            Assert.Equal(564, ReadBigEndianInt32(reader));
        }
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
        Assert.Contains("SetupSiteSpecificEnvironmentArt", level);
        Assert.Contains("SiteSpecificAtlasPath", level);
        foreach (string destination in new[]
                 {
                     "GasStationName", "NurseRecruit.DestinationName", "FireStationName",
                     "BroadcastStationName", "HarvesterWarehouseName", "VillageRescue.DestinationName",
                 })
            Assert.Contains(destination, level);
        Assert.Contains("_backgroundPath", panel);
        Assert.Contains("horde-escape.png", bad);
        Assert.Contains("military-escape.png", bad);
        Assert.Contains("family-win.png", win);
    }

    [Fact]
    public void RuntimeWiresBothRisingOverviewCinematics()
    {
        string lookout = Read("godot/scripts/HordeLookoutCinematic.cs");
        string corridor = Read("godot/scripts/EscapeCorridor.cs");
        Assert.Contains("OverviewTexturePath", lookout);
        Assert.Contains("HordeRiseDurationSeconds", lookout);
        Assert.Contains("DrawOverviewBackground", lookout);
        Assert.Contains("RaisedBridgeBackdropPath", corridor);
        Assert.Contains("LoweredBridgeBackdropPath", corridor);
        Assert.Contains("BeginCanyonOverview", corridor);
        Assert.Contains("CinematicHold = true", corridor);
        Assert.Contains("CanyonTargetZoom", corridor);
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

    private static void AssertPngSize(string resourcePath, int expectedWidth, int expectedHeight)
    {
        string path = Path.Combine(RepoRoot(), "godot", resourcePath[6..].Replace('/', Path.DirectorySeparatorChar));
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 16;
        Assert.Equal(expectedWidth, ReadBigEndianInt32(reader));
        Assert.Equal(expectedHeight, ReadBigEndianInt32(reader));
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
