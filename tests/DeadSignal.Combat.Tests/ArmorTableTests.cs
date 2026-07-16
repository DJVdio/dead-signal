using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-B18] 护甲表整表重做的落地校验：数据表『护甲表』17 件（人形 12 + 狗 5）逐件锁
/// 层(<see cref="ArmorSlot"/>) / 防护部位 / 锐防·钝防 / 重量 / 风味文案。
/// 与既有 *GearTests 的分工：那边锁"规则形态"（覆盖取舍、槽互斥），这边锁"表值本身"——
/// 表是唯一事实源，改表必改这里。数值仍属"拟定待调"，但代码必须与表逐格一致。
/// </summary>
public class ArmorTableTests
{
    private static readonly IReadOnlySet<string> Torso = new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen };
    private static readonly IReadOnlySet<string> TorsoArms = new HashSet<string>
    {
        HumanBody.Chest, HumanBody.Abdomen, HumanBody.LeftArm, HumanBody.RightArm,
    };
    private static readonly IReadOnlySet<string> Legs = new HashSet<string>
    {
        HumanBody.LeftLeg, HumanBody.RightLeg, HumanBody.LeftCalf, HumanBody.RightCalf,
    };

    private static void Check(
        ArmorLayer layer, string name, ArmorSlot slot,
        double sharp, double blunt, double weight, IReadOnlySet<string>? covers)
    {
        Assert.Equal(name, layer.Name);
        Assert.Equal(slot, layer.Slot);
        Assert.Equal(sharp, layer.SharpDefense);
        Assert.Equal(blunt, layer.BluntDefense);
        Assert.Equal(weight, layer.Weight);
        if (covers is null)
        {
            Assert.Null(layer.CoversParts);   // 全身
        }
        else
        {
            Assert.NotNull(layer.CoversParts);
            Assert.True(
                covers.SetEquals(layer.CoversParts!),
                $"{name} 防护部位不符：期望 [{string.Join('、', covers.OrderBy(s => s))}]，" +
                $"实际 [{string.Join('、', layer.CoversParts!.OrderBy(s => s))}]");
        }
        Assert.False(string.IsNullOrWhiteSpace(layer.Description), $"{name} 缺风味文案");
    }

    // ---- 人形 12 件 ----

    [Fact]
    public void LongSleeveShirt_MatchesTable()
        => Check(ArmorTable.LongSleeveShirt(), "长袖布衣", ArmorSlot.Skin, 6, 3, 0.15, TorsoArms);

    [Fact]
    public void Trousers_MatchesTable()
        => Check(ArmorTable.Trousers(), "长裤", ArmorSlot.Skin, 6, 3, 0.15, Legs);

    [Fact]
    public void Sneakers_MatchesTable()
        => Check(ArmorTable.Sneakers(), "运动鞋", ArmorSlot.Skin, 6, 3, 0.25,
            HumanBody.SubtreeNames(HumanBody.LeftFoot, HumanBody.RightFoot));

    [Fact]
    public void Shorts_MatchesTable()
        => Check(ArmorTable.Shorts(), "短裤", ArmorSlot.Skin, 6, 3, 0.1,
            new HashSet<string> { HumanBody.LeftLeg, HumanBody.RightLeg });

    [Fact]
    public void LeatherCuirass_MatchesTable()
        => Check(ArmorTable.ChestPlate(), "皮革胸甲", ArmorSlot.Plate, 25, 12.5, 4,
            new HashSet<string> { HumanBody.Chest });

    [Fact]
    public void CoarseClothVest_MatchesTable()
        => Check(ArmorTable.CoarseClothVest(), "粗布背心", ArmorSlot.Outer, 6, 3, 0.1, Torso);

    [Fact]
    public void CoarseClothCoat_MatchesTable()
        => Check(ArmorTable.CoarseClothCoat(), "粗布外套", ArmorSlot.Outer, 6, 3, 0.25, TorsoArms);

    [Fact]
    public void ClothJacket_MatchesTable()
        => Check(ArmorTable.ClothJacket(), "布夹克", ArmorSlot.Outer, 7.5, 4, 0.3, TorsoArms);

    [Fact]
    public void DenimJacket_MatchesTable()
        => Check(ArmorTable.DenimJacket(), "牛仔外套", ArmorSlot.Outer, 10, 4, 0.6, TorsoArms);

    [Fact]
    public void LeatherJacket_MatchesTable()
        => Check(ArmorTable.LeatherJacket(), "皮夹克", ArmorSlot.Outer, 18, 9, 0.5, TorsoArms);

    [Fact]
    public void Leather_MatchesTable()
        => Check(ArmorTable.Leather(), "皮甲", ArmorSlot.Plate, 25, 12.5, 6, TorsoArms);

    [Fact]
    public void Plate_MatchesTable()
    {
        var covers = new HashSet<string>(TorsoArms);
        covers.UnionWith(Legs);
        Check(ArmorTable.Plate(), "板甲", ArmorSlot.Plate, 70, 35, 15, covers);
    }

    [Fact]
    public void WorkGloves_MatchesTable_OneItemCoveringBothHands()
        => Check(ArmorTable.WorkGloves(), "劳保手套", ArmorSlot.Skin, 6, 3, 0.05,
            HumanBody.SubtreeNames(HumanBody.LeftHand, HumanBody.RightHand));

    [Fact]
    public void ZombieHide_MatchesTable_FullBody()
    {
        IReadOnlyList<ArmorLayer> hide = ArmorTable.ZombieHide();
        Assert.Single(hide);
        Check(hide[0], "腐皮", ArmorSlot.Skin, 3, 3, 0, covers: null);
    }

    // ---- 狗 5 件 ----

    [Fact]
    public void DogClothVest_MatchesTable()
        => Check(ArmorTable.DogClothVest(), "布制狗衣", ArmorSlot.Skin, 10, 5, 0.15, Torso);

    [Fact]
    public void DogLeatherVest_MatchesTable()
        => Check(ArmorTable.DogLeatherVest(), "皮制狗衣", ArmorSlot.Outer, 25, 12.5, 0.5, Torso);

    [Fact]
    public void DogIronHelmet_MatchesTable()
        => Check(ArmorTable.DogIronHelmet(), "铁皮头甲", ArmorSlot.Plate, 18, 12, 3,
            new HashSet<string> { HumanBody.Head });

    [Fact]
    public void DogWireHelmet_MatchesTable()
        => Check(ArmorTable.DogWireHelmet(), "铁丝头甲", ArmorSlot.Plate, 12, 12, 1.5,
            new HashSet<string> { HumanBody.Head });

    /// <summary>口袋狗衣从"无甲纯容器"改为"薄甲 + 容量"（表：护甲 2/1、重量 0.25、为狗提供 6kg 负重）。</summary>
    [Fact]
    public void DogPocketVest_MatchesTable_NowHasArmor()
        => Check(ArmorTable.DogPocketVest(), "口袋狗衣", ArmorSlot.Skin, 2, 1, 0.25, Torso);

    /// <summary>铁丝头甲文案 = 数据表『狗装备』说明列用户新改（两处真源须同文）。</summary>
    [Fact]
    public void DogWireHelmet_DescriptionMatchesWikiTable()
    {
        const string expected = "曾经保护你的，现在保护狗。";
        Assert.Equal(expected, ArmorTable.DogWireHelmet().Description);
        Assert.Equal(expected, DeadSignal.Godot.DogGearCatalog.Get("铁丝头甲")!.Description);
    }

    // ---- 删除件不得复活：布衣 / 贴身布衣 / 左右手套 / 胸甲 已从表中移除 ----

    [Fact]
    public void RemovedItems_AreGone_FromSurvivorArmorAndFlavor()
    {
        // 劫掠者生成配置 = 皮夹克(外套) + 长袖布衣(贴身)，不再有"贴身布衣"。
        IReadOnlyList<ArmorLayer> set = ArmorTable.SurvivorArmor();
        Assert.Equal(2, set.Count);
        Assert.Contains(set, a => a.Name == "皮夹克");
        Assert.Contains(set, a => a.Name == "长袖布衣");

        foreach (string gone in new[] { "布衣", "贴身布衣", "左手套", "右手套", "胸甲" })
        {
            Assert.Equal("", ArmorTable.DescriptionOf(gone));
        }
    }

    [Fact]
    public void DescriptionOf_CoversEveryTableItem()
    {
        foreach (string name in new[]
        {
            "长袖布衣", "长裤", "运动鞋", "短裤", "皮革胸甲", "粗布背心", "粗布外套", "布夹克",
            "牛仔外套", "皮夹克", "皮甲", "板甲", "劳保手套",
            "布制狗衣", "皮制狗衣", "铁皮头甲", "铁丝头甲", "口袋狗衣",
        })
        {
            Assert.False(string.IsNullOrWhiteSpace(ArmorTable.DescriptionOf(name)), $"{name} 无风味文案");
        }
    }
}
