using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>Wiki authored 装甲移速惩罚：目录登记与实时移动消费必须同时存在。</summary>
public sealed class ApparelMovementWiringTests
{
    [Theory]
    [InlineData("皮革胸甲", 0.99)]
    [InlineData("皮甲", 0.97)]
    [InlineData("板甲", 0.90)]
    public void 三件装甲按真实穿戴名提供移速乘子(string name, double expected)
    {
        double actual = ApparelCatalog.ApparelEffectMultiplier(
            new[] { name }, ApparelCatalog.EquipEffectKind.MovementSpeed);

        Assert.Equal(expected, actual, precision: 10);
    }

    [Fact]
    public void 多件装备效果按项目通则连乘而非相加()
    {
        double actual = ApparelCatalog.ApparelEffectMultiplier(
            new[] { "皮革胸甲", "皮甲", "板甲" },
            ApparelCatalog.EquipEffectKind.MovementSpeed);

        Assert.Equal(0.99 * 0.97 * 0.90, actual, precision: 10);
    }

    [Fact]
    public void 实时移动链从Pawn真实穿戴表取乘子()
    {
        string actor = Source("godot/scripts/Actor.cs");
        string pawn = Source("godot/scripts/Pawn.cs");

        Assert.Contains("mobility *= ApparelMoveSpeedMultiplier;", actor);
        Assert.Contains("ApparelEffectMultiplier(EquippedApparel, ApparelCatalog.EquipEffectKind.MovementSpeed)", pawn);
    }

    [Theory]
    [InlineData("皮革胸甲", "移动速度 -1%")]
    [InlineData("皮甲", "移动速度 -3%")]
    [InlineData("板甲", "移动速度 -10%")]
    public void Wiki护甲表直接展示真实生效值(string name, string expected)
    {
        using JsonDocument doc = JsonDocument.Parse(Source("docs/wiki/data/armor.json"));
        foreach (JsonElement row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("name").GetString() == name)
            {
                Assert.Contains(expected, row.GetProperty("effects").GetString());
                return;
            }
        }
        throw new Xunit.Sdk.XunitException($"护甲表找不到：{name}");
    }

    private static string Source(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relativePath)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }
}
