using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>角色 Wiki、生成器与 CampMain 消费层接线的定向护栏。</summary>
public sealed class CharacterWiringTests
{
    private static string Source(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relativePath)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    private static JsonElement WikiRows(string file)
        => JsonDocument.Parse(Source(file)).RootElement.GetProperty("rows");

    private static JsonElement WikiRow(string file, string name)
    {
        foreach (JsonElement row in WikiRows(file).EnumerateArray())
            if (row.TryGetProperty("name", out JsonElement value) && value.GetString() == name)
                return row;
        throw new Xunit.Sdk.XunitException($"找不到角色 Wiki 行：{name}");
    }

    [Fact]
    public void WikiCharacters_KeepAuthoredLoadouts()
    {
        const string file = "docs/wiki/data/characters.json";
        Assert.Equal("开局三件套（长袖布衣 / 长裤 / 一双运动鞋）", WikiRow(file, "山姆").GetProperty("gear").GetString());
        Assert.Equal("匕首+开局三件套+皮革胸甲", WikiRow(file, "克莉丝汀").GetProperty("gear").GetString());
        Assert.Equal("刺剑+开局三件套", WikiRow(file, "耗子").GetProperty("gear").GetString());
        Assert.Equal("开局三件套", WikiRow(file, "皮特").GetProperty("gear").GetString());
    }

    [Fact]
    public void CharacterStats_KeepCurrentAuthoredValues()
    {
        JsonElement rows = WikiRows("docs/wiki/data/character-stats.json");
        AssertStat(rows, "nordi_l2_hours", 24);
        AssertStat(rows, "nordi_l3_hours", 72);
        AssertStat(rows, "doug_l2_days", 5);
        AssertStat(rows, "doug_l3_days", 12);
        AssertStat(rows, "nurse_l2_bed_heal", 20);
        AssertStat(rows, "rat_l3_darkness", 50);
        AssertStat(rows, "christine_l2_buy_discount", 6.25);
        AssertStat(rows, "sam_l3_fracture_penalty_reduction", 30);
    }

    [Fact]
    public void CampMain_WiresCharacterEffectsAndRecruitmentLoadouts()
    {
        string camp = Source("godot/scripts/CampMain.cs");
        Assert.Contains("SetConcussionChanceMultiplier", camp);
        Assert.Contains("SetLargeBleedDowngradeProvider", camp);
        Assert.Contains("SetFracturePenaltyReductionProvider", camp);
        Assert.Contains("FracturePenaltyReduction", camp);
        Assert.Contains("PersonalHealSpeedMultiplier", camp);
        Assert.Contains("NightingalePerk.BedSleepHealBonusPct", camp);
        Assert.Contains("BondOperationMultFor", camp);
        Assert.Contains("SetAuthoredAttackSpeedMult", camp);
        Assert.Contains("SetAuthoredMoveSpeedMult", camp);
        Assert.Contains("StartingWeapon.Rapier", camp);
        Assert.Contains("StartingWeapon.Dagger", camp);
        Assert.Contains("皮革胸甲", camp);
    }

    [Fact]
    public void CharacterGenerator_UsesCurrentAuthoredFields()
    {
        string generator = Source("tools/WikiExtract/Characters.cs");
        Assert.Contains("AuraOperationMult", generator);
        Assert.DoesNotContain("AuraProductionMult", generator);
        Assert.Contains("匕首+开局三件套+皮革胸甲", generator);
        Assert.Contains("刺剑+开局三件套", generator);
        Assert.Contains("受到骨折的负面影响-30%", generator);
    }

    private static void AssertStat(JsonElement rows, string id, double expected)
    {
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.GetProperty("_id").GetString() == id)
            {
                Assert.Equal(expected, row.GetProperty("value").GetDouble(), precision: 6);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException($"找不到角色数值行：{id}");
    }
}
