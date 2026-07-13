using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 日常外套两件新装备（布夹克 / 牛仔外套）的落地校验：表值、外套层梯度、占槽登记、获取途径。
/// 定位：让外套层形成完整梯度 粗布外套 6/3 → 布夹克 8/4 → 牛仔外套 10/5 → 皮夹克 12/6，
/// 供「灾难当天日常着装」的丧尸/幸存者使用。数值"拟定待调"，本测试锁的是表值一致 + 梯度形态。
/// </summary>
public class JacketGearTests
{
    private static readonly IReadOnlySet<string> TorsoArms = new HashSet<string>
    {
        HumanBody.Chest, HumanBody.Abdomen, HumanBody.LeftArm, HumanBody.RightArm,
    };

    [Fact]
    public void ClothJacket_MatchesTable()
    {
        ArmorLayer l = ArmorTable.ClothJacket();
        Assert.Equal("布夹克", l.Name);
        Assert.Equal(ArmorSlot.Outer, l.Slot);
        Assert.Equal(8, l.SharpDefense);
        Assert.Equal(4, l.BluntDefense);
        Assert.Equal(0.3, l.Weight);
        Assert.True(TorsoArms.SetEquals(l.CoversParts!));
        Assert.False(string.IsNullOrWhiteSpace(l.Description));
    }

    [Fact]
    public void DenimJacket_MatchesTable()
    {
        ArmorLayer l = ArmorTable.DenimJacket();
        Assert.Equal("牛仔外套", l.Name);
        Assert.Equal(ArmorSlot.Outer, l.Slot);
        Assert.Equal(10, l.SharpDefense);
        Assert.Equal(5, l.BluntDefense);
        Assert.Equal(0.6, l.Weight);
        Assert.True(TorsoArms.SetEquals(l.CoversParts!));
        Assert.False(string.IsNullOrWhiteSpace(l.Description));
    }

    /// <summary>
    /// 外套层四件（同覆盖：胸腹双臂）的防护梯度严格递增：粗布外套 6/3 → 布夹克 8/4 → 牛仔外套 10/5 → 皮夹克 12/6。
    /// 重量<b>不</b>随之单调——皮夹克 0.5kg 比牛仔外套 0.6kg 更轻（"最强且仍很轻"是它的定位），
    /// 厚牛仔布则是"便宜、笨重、够挡"的中档货。只有布类三件（粗布外套→布夹克→牛仔外套）重量递增。
    /// </summary>
    [Fact]
    public void OuterLayer_FormsMonotonicDefenseGradient()
    {
        ArmorLayer[] ladder =
        {
            ArmorTable.CoarseClothCoat(), ArmorTable.ClothJacket(),
            ArmorTable.DenimJacket(), ArmorTable.LeatherJacket(),
        };
        for (int i = 1; i < ladder.Length; i++)
        {
            Assert.True(ladder[i].SharpDefense > ladder[i - 1].SharpDefense, $"{ladder[i].Name} 锐防应高于 {ladder[i - 1].Name}");
            Assert.True(ladder[i].BluntDefense > ladder[i - 1].BluntDefense, $"{ladder[i].Name} 钝防应高于 {ladder[i - 1].Name}");
            Assert.Equal(ArmorSlot.Outer, ladder[i].Slot);
            Assert.True(TorsoArms.SetEquals(ladder[i].CoversParts!), $"{ladder[i].Name} 覆盖应为胸腹双臂");
        }

        // 布类三件的重量递增（越厚越沉）；皮夹克不在此列，见摘要。
        Assert.True(ArmorTable.ClothJacket().Weight > ArmorTable.CoarseClothCoat().Weight);
        Assert.True(ArmorTable.DenimJacket().Weight > ArmorTable.ClothJacket().Weight);
        Assert.True(ArmorTable.LeatherJacket().Weight < ArmorTable.DenimJacket().Weight);
    }

    [Fact]
    public void BothJackets_HaveFlavorText()
    {
        Assert.False(string.IsNullOrWhiteSpace(ArmorTable.DescriptionOf("布夹克")));
        Assert.False(string.IsNullOrWhiteSpace(ArmorTable.DescriptionOf("牛仔外套")));
    }

    /// <summary>两件都登记进穿戴目录，占外套层单槽（与粗布外套/皮夹克互斥），覆盖直接取自 ArmorTable（单一事实源）。</summary>
    [Theory]
    [InlineData("布夹克")]
    [InlineData("牛仔外套")]
    public void Jacket_RegisteredInApparelCatalog_OuterSlot(string name)
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get(name);
        Assert.NotNull(def);
        Assert.False(def!.Paired);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.OuterLayer }, def.Slots.ToHashSet());
        Assert.Equal(ArmorSlot.Outer, def.Layer);
        Assert.True(TorsoArms.SetEquals(def.CoversParts!));
    }

    /// <summary>外套层互斥：布夹克在身时牛仔外套被挡；开 replace 才顶替，不叠两件。</summary>
    [Fact]
    public void OuterJackets_AreMutuallyExclusive()
    {
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "布夹克"));
        Assert.Equal(EquipOutcome.BlockedSlotOccupied, ApparelCatalog.Equip(slots, "牛仔外套"));
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "牛仔外套", replace: true));
        Assert.Equal("牛仔外套", slots.ItemAt(EquipSlot.OuterLayer));
    }

    /// <summary>布夹克的获取途径：缝纫配方（读《裁缝手记》解锁，同粗布背心），产物落地为护甲件。</summary>
    [Fact]
    public void ClothJacket_IsCraftable_GatedByTailorsNotes()
    {
        RecipeData? r = RecipeBook.Find("cloth_jacket");
        Assert.NotNull(r);
        Assert.Equal("布夹克", r!.DisplayName);
        Assert.Equal(RecipeCategory.Tailoring, r.Category);
        Assert.Contains(RecipeBook.TailorsNotesBookId, r.RequiredBookIds);
        Assert.Empty(r.RequiredTools);
        Assert.True(r.WorkMinutes > 0);

        Item made = Assert.Single(CraftOutputFactory.Create("cloth_jacket", 1));
        Assert.Equal(ItemCategory.Armor, made.Category);
        Assert.Equal("布夹克", made.DisplayName);
    }
}
