using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 人形头部护甲（防暴头盔 / 军用头盔）：护甲表**第一次**给人形件出头盔——在此之前
/// <c>ZombieOutfit</c> 的注释里写着「头/脚恒裸……爆头是精英的唯一软肋」，本组测试就是把那句话改写掉的门禁。
///
/// <b>为什么头盔是重物</b>：`头` 是 <see cref="BodyPartCategory.Vital"/>、MaxHp 仅 16、VolumeWeight 6
/// （全身权重和 ≈103.4 ⇒ **5.8% 的命中落在头上，且头归零直接致死**）。
/// <para>
/// 🔴 <b>[T68] 两顶盔不再"防护相同"了</b>（旧不变量作废，用户拍板：「防暴头盔应当更重防御更强，还有一些 debuff；
/// 军用头盔应当更泛用一些」）。现在：<b>军用 = 泛用款</b>（28/14、2.5kg、只护颅顶、无 debuff）；
/// <b>防暴 = 重装款</b>（35/22 防御全面更高、4.5kg、护整张脸、外加 视野−10%/听力−10% 的 debuff——那两条是引擎新轴，未落地）。
/// 玩家挑的是"**要泛用的轻盔，还是要更硬但更笨重、还削感知的重盔**"。
/// </para>
/// </summary>
public class HumanHelmetTests
{
    private static readonly IReadOnlySet<string> FaceParts = new HashSet<string>
    {
        HumanBody.Head, HumanBody.LeftEye, HumanBody.RightEye, HumanBody.Nose, HumanBody.Chin,
    };

    // ---- 表值（docs/items-data.xlsx『护甲表』逐格对齐）----

    [Fact]
    public void MilitaryHelmet_IsRigidHeadPlate_CoversHeadOnly()
    {
        ArmorLayer h = ArmorTable.MilitaryHelmet();

        Assert.Equal("军用头盔", h.Name);
        Assert.Equal(ArmorSlot.Plate, h.Slot);   // 伤害层序（≠ 装备槽——它占的是 EquipSlot.Head，见下）
        Assert.Equal(28, h.SharpDefense);
        Assert.Equal(14, h.BluntDefense);
        Assert.Equal(2.5, h.Weight);
        Assert.Equal(new HashSet<string> { HumanBody.Head }, h.CoversParts);   // 只护颅顶，脸全裸
    }

    [Fact]
    public void RiotHelmet_IsHeavyVariant_StrongerAndCoversFace()
    {
        ArmorLayer h = ArmorTable.RiotHelmet();

        Assert.Equal("防暴头盔", h.Name);
        Assert.Equal(ArmorSlot.Plate, h.Slot);
        Assert.Equal(35, h.SharpDefense);   // [T68] 用户拍板：更强
        Assert.Equal(22, h.BluntDefense);
        Assert.Equal(4.5, h.Weight);
        Assert.Equal(FaceParts, h.CoversParts);          // 头 + 双眼 + 鼻 + 下巴（面罩）
    }

    /// <summary>
    /// 🔴 [T68] 两顶盔<b>不再是"防护相同"的取舍</b>，而是**两条不同路线**：军用泛用（轻、无副作用），
    /// 防暴重装（防御全面更强 + 更重 + debuff + 护脸）。用户原话：「防暴更重防御更强，军用更泛用」。
    /// 这条测试锁住新形态：防暴在两条防御轴上都严格 ≥ 军用，且更重、更护脸。
    /// </summary>
    [Fact]
    public void RiotHelmet_IsStrictlyBetterDefense_ButHeavierThanMilitary()
    {
        ArmorLayer riot = ArmorTable.RiotHelmet();
        ArmorLayer mil = ArmorTable.MilitaryHelmet();

        // [T68] 防暴防御全面更高（不再相同）——挑盔时防护重新进入决策
        Assert.True(riot.SharpDefense > mil.SharpDefense);
        Assert.True(riot.BluntDefense > mil.BluntDefense);

        // 代价：更沉 + 护脸（军用则轻、脸敞着、但无 debuff——泛用款）
        Assert.True(riot.Weight > mil.Weight);                          // 防暴更沉（吃负重上限）
        Assert.True(riot.CoversParts!.Count > mil.CoversParts!.Count);  // 且把脸罩住了
        Assert.DoesNotContain(HumanBody.LeftEye, mil.CoversParts!);     // 军用盔：脸是敞着的
        Assert.Contains(HumanBody.LeftEye, riot.CoversParts!);
    }

    /// <summary>耳朵不在面罩下（防暴盔面罩不包耳；且耳归零无系统后果，护它没有意义）。</summary>
    [Fact]
    public void RiotHelmet_DoesNotCoverEars()
    {
        IReadOnlySet<string> covers = ArmorTable.RiotHelmet().CoversParts!;

        Assert.DoesNotContain(HumanBody.LeftEar, covers);
        Assert.DoesNotContain(HumanBody.RightEar, covers);
    }

    [Fact]
    public void BothHelmets_HaveFlavorText()
    {
        Assert.False(string.IsNullOrWhiteSpace(ArmorTable.DescriptionOf("防暴头盔")));
        Assert.False(string.IsNullOrWhiteSpace(ArmorTable.DescriptionOf("军用头盔")));
        Assert.Equal(ArmorTable.RiotHelmet().Description, ArmorTable.DescriptionOf("防暴头盔"));
        Assert.Equal(ArmorTable.MilitaryHelmet().Description, ArmorTable.DescriptionOf("军用头盔"));
    }

    // ---- 穿戴槽（消费层 ApparelCatalog）：玩家扒下来也能戴（护甲不分阵营）----

    [Fact]
    public void MilitaryHelmet_OccupiesHeadSlotOnly_LeavesFaceSlotsFree()
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("军用头盔");

        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.Head }, def!.Slots);
        // ⚠️ 「头盔不在装甲层」这句用户口径，落点是**装备槽**：它不占 EquipSlot.PlateLayer ⇒ 板甲照穿。
        // ArmorSlot 是伤害层序（先破哪层），与占哪个槽无关，两者别混。
        Assert.DoesNotContain(EquipSlot.PlateLayer, def.Slots);
    }

    [Fact]
    public void RiotHelmet_OccupiesHeadEyesAndFace()
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("防暴头盔");

        Assert.NotNull(def);
        Assert.Equal(
            new HashSet<EquipSlot> { EquipSlot.Head, EquipSlot.Eyes, EquipSlot.Face },
            def!.Slots);
    }

    /// <summary>防暴头盔与防毒面具互斥（同抢眼镜/面部槽）——戴着面罩没法再扣一张面具，这是槽系统自己长出来的。</summary>
    [Fact]
    public void RiotHelmet_AndGasMask_AreMutuallyExclusive()
    {
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "防暴头盔"));

        Assert.Equal(EquipOutcome.BlockedSlotOccupied, ApparelCatalog.Equip(slots, "防毒面具"));
    }

    /// <summary>军用头盔只占头槽 ⇒ 还能再扣一张防毒面具（眼/面还空着）。</summary>
    [Fact]
    public void MilitaryHelmet_LeavesRoomForGasMask()
    {
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "军用头盔"));

        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "防毒面具"));
    }

    /// <summary>两顶盔同占护甲层的头槽，互斥。</summary>
    [Fact]
    public void HelmetsAreMutuallyExclusiveWithEachOther()
    {
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "军用头盔"));

        Assert.Equal(EquipOutcome.BlockedSlotOccupied, ApparelCatalog.Equip(slots, "防暴头盔"));
    }

    /// <summary>头盔不占躯干的护甲层：戴着头盔照样能穿板甲（头槽 ≠ PlateLayer 槽）。</summary>
    [Fact]
    public void Helmet_DoesNotBlockBodyArmor()
    {
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "防暴头盔"));

        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "板甲"));
    }

    /// <summary>穿上后覆盖部位真的生效（ApparelSlots 聚合出的覆盖集含头/眼）——战斗层 DefenderArmor 据此裁剪。</summary>
    [Fact]
    public void RiotHelmet_CoverageReachesTheCombatLayer()
    {
        var slots = new ApparelSlots();
        ApparelCatalog.Equip(slots, "防暴头盔");

        IReadOnlySet<string> covered = slots.ActiveCoverage()
            .Single(c => c.Item == "防暴头盔").CoversParts;

        Assert.Contains(HumanBody.Head, covered);
        Assert.Contains(HumanBody.LeftEye, covered);
        Assert.Contains(HumanBody.Chin, covered);
    }

    // ---- 精英丧尸预设（authored·点名，权重 0）----

    [Fact]
    public void RiotCopZombie_WearsTheRiotHelmet()
    {
        IReadOnlyList<ArmorLayer> armor = ZombieOutfit.ArmorOf("防暴警察丧尸");

        Assert.Contains(armor, l => l.Name == "防暴头盔");
    }

    [Fact]
    public void SoldierZombie_WearsTheMilitaryHelmet()
    {
        IReadOnlyList<ArmorLayer> armor = ZombieOutfit.ArmorOf("军人丧尸");

        Assert.Contains(armor, l => l.Name == "军用头盔");
    }

    /// <summary>
    /// **头盔绝不进日常随机池**（结构性零漂移的保证）：日常池 9 套里一顶头盔都没有 ⇒ 街上随便一只丧尸
    /// 不会突然戴着防暴盔，既有 Sim 基线也不会因为本次新增而漂移。
    /// </summary>
    [Fact]
    public void Helmets_NeverAppearInTheEverydayPool()
    {
        List<string> everyday = ZombieOutfit.Presets
            .SelectMany(p => p.Clothes())
            .Select(l => l.Name)
            .ToList();

        Assert.DoesNotContain("防暴头盔", everyday);
        Assert.DoesNotContain("军用头盔", everyday);
        Assert.All(ZombieOutfit.ElitePresets, p => Assert.Equal(0, p.Weight));
    }

    /// <summary>精英盔是**给头的**，不是给别处的：两套精英预设加起来恰好各戴一顶，且都盖住了 `头`。</summary>
    [Fact]
    public void EliteZombies_NoLongerHaveBareHeads()
    {
        foreach (string elite in new[] { "防暴警察丧尸", "军人丧尸" })
        {
            IReadOnlyList<ArmorLayer> armor = ZombieOutfit.ArmorOf(elite);
            // 除腐皮（全身覆盖，CoversParts=null）之外，必须有一件专门盖住头的
            Assert.Contains(armor, l => l.CoversParts is not null && l.CoversParts.Contains(HumanBody.Head));
        }
    }
}
