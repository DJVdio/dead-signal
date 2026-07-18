using System.Text.Json;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 武器重量不属于 <c>weapons.json</c> 战斗配置：唯一真源仍是
/// <c>godot/scripts/ItemDef.cs :: ItemRegistry.Weapons</c>。因此不能伪造 <c>configKey</c>，
/// 而要把 Wiki 展示表与该代码真源逐行焊死。
/// </summary>
public sealed class WikiWeaponWeightSyncTests
{
    [Fact]
    public void WikiWeapons_WeightColumn_DeclaresCodeSource_AndMatchesItemRegistryExactly()
    {
        string path = Path.Combine(RepoRoot(), "docs", "wiki", "data", "weapons.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));

        JsonElement weightColumn = doc.RootElement.GetProperty("columns")
            .EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "weight");

        Assert.False(weightColumn.TryGetProperty("configKey", out _),
            "武器重量真源不在 godot/data/config/weapons.json，不得伪造 configKey");
        Assert.Equal("godot/scripts/ItemDef.cs :: ItemRegistry.Weapons",
            weightColumn.GetProperty("codeSource").GetString());

        Dictionary<string, double> wikiWeights = doc.RootElement.GetProperty("rows")
            .EnumerateArray()
            .Where(r => r.TryGetProperty("weight", out JsonElement weight)
                        && weight.ValueKind == JsonValueKind.Number)
            .ToDictionary(
                r => r.GetProperty("name").GetString()!,
                r => r.GetProperty("weight").GetDouble());

        Assert.Equal(ItemRegistry.Weapons.Keys.OrderBy(x => x), wikiWeights.Keys.OrderBy(x => x));
        foreach ((string name, double expected) in ItemRegistry.Weapons)
        {
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(expected),
                BitConverter.DoubleToInt64Bits(wikiWeights[name]));
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到 Dead Signal 仓库根");
    }
}
