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
        // 页面值是人话 20（config/runtime 百分点 20 直接显示，不再经 `_configPercent` 放大）。
        AssertStat(rows, "nurse_l2_bed_heal", 20);
        AssertStat(rows, "rat_l3_darkness", 50);
        AssertStat(rows, "christine_l2_buy_discount", 6.25);
        AssertStat(rows, "sam_l3_fracture_penalty_reduction", 30);
    }

    [Fact]
    public void NurseL2BedHeal_PercentIsHumanReadable()
    {
        JsonElement nurse = WikiRow("docs/wiki/data/characters.json", "南丁格尔");
        string l2 = nurse.GetProperty("perkL2").GetString()!;
        Assert.DoesNotContain("2000%", l2);
        Assert.Contains("20%", l2);
    }

    [Fact]
    public void PeteWiki_Describes_L2Operation_And_L3Dodge_AtTheRightLevels()
    {
        JsonElement pete = WikiRow("docs/wiki/data/characters.json", "皮特");
        string l2 = pete.GetProperty("perkL2").GetString()!;
        string l3 = pete.GetProperty("perkL3").GetString()!;
        Assert.Contains("操作能力 *1.05", l2);
        Assert.DoesNotContain("闪避", l2);
        Assert.Contains("15% 概率闪避", l3);
    }

    [Fact]
    public void ChristineWiki_NoLongerClaimsSheHasNoPerk()
    {
        JsonElement christine = WikiRow("docs/wiki/data/characters.json", "克莉丝汀");
        string notes = christine.GetProperty("notes").GetString()!;
        Assert.DoesNotContain("没有专属效果", notes);
        Assert.Contains("合计 35%", notes);
        Assert.Contains("仍在营存活", notes);
    }

    [Fact]
    public void PeteRescuedFlag_IsWrittenOnlyAfterSuccessfulRecruitment()
    {
        string source = Source("godot/scripts/CampMain.PeteEvent.cs");
        int fightStart = source.IndexOf("private void BeginPeteRescueFight()", System.StringComparison.Ordinal);
        int targetsStart = source.IndexOf("private IEnumerable<Actor> PeteZombieTargets()", System.StringComparison.Ordinal);
        int recruitStart = source.IndexOf("private void RecruitPete()", System.StringComparison.Ordinal);
        int removeStart = source.IndexOf("private void RemovePeteBoy()", System.StringComparison.Ordinal);
        Assert.True(fightStart >= 0 && targetsStart > fightStart && recruitStart >= 0 && removeStart > recruitStart);
        Assert.DoesNotContain("_storyFlags.Set(PeteRescuedFlag", source[fightStart..targetsStart]);
        Assert.Contains("_storyFlags.Set(PeteRescuedFlag", source[recruitStart..removeStart]);
    }

    [Fact]
    public void Pete_Has_A_Dedicated_Design_Entry_Without_Invented_Backstory()
    {
        string design = Source("docs/superpowers/specs/2026-07-04-dead-signal-design.md");
        Assert.Contains("### 可招募角色：皮特（已确认事实与 authored 留白）", design);
        Assert.Contains("移动速度改为 ×1.25，操作能力 ×1.05", design);
        Assert.Contains("前史、性格、关系", design);
        Assert.Contains("全部等待用户手写", design);
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
        Assert.Contains("ChristineDaysInCamp = _christineDaysInCamp", Source("godot/scripts/CampMain.Save.cs"));
        Assert.Contains("_christineDaysInCamp = s.Bonds.ChristineDaysInCamp", Source("godot/scripts/CampMain.Save.cs"));
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
