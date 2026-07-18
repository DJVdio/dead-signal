using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 日常外套两件新装备（布夹克 / 牛仔外套）的落地校验：Wiki 配置映射、外套层梯度、占槽登记、获取途径。
/// 🔴 [T68·用户手改] 防护与重量数值来自 Wiki 配置。
/// ⚠️ <b>钝防梯度被用户有意抹平</b>：布夹克与牛仔外套钝防持平（用户原话"有意的，就该弱"）——
/// 故本测试的梯度断言改为「锐防严格递增、钝防**非递减**」，不再要求钝防严格递增（那会把用户的值判红）。
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
        Assert.Equal(7.5, l.SharpDefense);   // [T68] Wiki 配置值
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
        Assert.Equal(4, l.BluntDefense);   // [T68] Wiki 配置值
        Assert.Equal(0.6, l.Weight);
        Assert.True(TorsoArms.SetEquals(l.CoversParts!));
        Assert.False(string.IsNullOrWhiteSpace(l.Description));
    }

    /// <summary>
    /// 外套层四件同覆盖胸腹双臂；防护与重量数值来自 Wiki 配置。
    /// 🔴 [T68] <b>锐防严格递增；钝防只保证非递减</b>——其中一格有意持平。
    /// 重量<b>不</b>随防护单调——最强外套保持轻量定位。
    /// 只有布类三件（粗布外套→布夹克→牛仔外套）重量递增。
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
            // [T68] 钝防**非递减**（≥）：Wiki 配置允许相邻外套持平 ⇒ 不要求严格递增。
            Assert.True(ladder[i].BluntDefense >= ladder[i - 1].BluntDefense, $"{ladder[i].Name} 钝防不应低于 {ladder[i - 1].Name}");
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
