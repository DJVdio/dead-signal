using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 护甲消费层接线护栏：<c>ApparelCatalog</c> 已登记的护甲必须同时进入 Pawn 的
/// <c>ArmorLayerCatalog</c>，否则穿戴成功后会在 <c>BuildDefenderArmor</c> 被静默过滤，
/// 读档重建也会丢失同一层。
/// </summary>
public sealed class ArmorConsumerWiringTests
{
    [Fact]
    public void 新增人形护甲_必须进入PawnArmorLayerCatalog_否则会被装备与读档链过滤()
    {
        string pawn = StripComments(Source("godot/scripts/Pawn.cs"));
        const string method = "private static Dictionary<string, ArmorLayer> BuildArmorLayerCatalog()";
        int start = pawn.IndexOf(method, StringComparison.Ordinal);
        Assert.True(start >= 0, "找不到 Pawn.BuildArmorLayerCatalog");

        int end = pawn.IndexOf("private IReadOnlySet<string> SeveredParts()", start, StringComparison.Ordinal);
        Assert.True(end > start, "找不到 Pawn.ArmorLayerCatalog 后的边界");
        string catalog = pawn[start..end];

        string[] newlyAddedArmorFactories =
        {
            "WarMask",
            "CottonHat",
            "CoarseClothShirt",
            "CoarseShorts",
            "CoarseTrousers",
            "HorrorArmor",
            "Sunglasses",
            "PlainGlasses",
            "SelfMadeSnowGoggles",
            "AnkleGuard",
            "BallisticVest",
        };

        string[] missing = newlyAddedArmorFactories
            .Where(factory => !catalog.Contains($"ArmorTable.{factory}()", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"以下 ApparelCatalog 护甲未进入 Pawn.ArmorLayerCatalog，会被 BuildDefenderArmor 和读档重建过滤：{string.Join("、", missing)}");
    }

    [Fact]
    public void 装备与读档重建_都必须经同一ArmorLayerCatalog()
    {
        string pawn = StripComments(Source("godot/scripts/Pawn.cs"));
        string save = StripComments(Source("godot/scripts/Pawn.Save.cs"));

        Assert.Contains("ArmorLayerCatalog.TryGetValue(apparelName", pawn);
        Assert.Contains("DefenderArmor = BuildDefenderArmor();", pawn);
        Assert.Contains("SaveMapper.RestoreApparel(_apparel, s.Apparel);", save);
        Assert.Contains("RebuildApparelLayers();", save);
        Assert.Contains("ArmorLayerCatalog.TryGetValue(w.Item", save);
    }

    private static string Source(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relativePath)))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, $"找不到 {relativePath}");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    private static string StripComments(string source)
        => string.Join("\n", source.Split('\n').Where(line =>
        {
            string trimmed = line.TrimStart();
            return !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        }));
}
