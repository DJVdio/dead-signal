using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 新增装备数据的字段/形态校验（数值来自 Wiki 配置，这里主要锁定规则形态）：
/// 刺剑=单手锐器可双持、草叉=双手锐器、粗布外套=外套层且防护低于皮甲、劳保手套=手部轻护甲层；
/// 并验证冲锋枪已放开可双持、两把新近战入 Arsenal。
/// </summary>
public class NewEquipmentDataTests
{
    // ---- 刺剑：单手锐器，可双持 ----

    [Fact]
    public void Rapier_IsOneHandedSharp_CanDualWield()
    {
        Weapon rapier = WeaponTable.Rapier();
        Assert.Equal("刺剑", rapier.Name);
        Assert.Equal(DamageType.Sharp, rapier.DamageType);
        Assert.False(rapier.TwoHanded);
        Assert.True(rapier.CanDualWield);
        Assert.False(rapier.IsRanged);
        Assert.True(rapier.DamageMax > rapier.DamageMin);
        Assert.True(rapier.Penetration > 0);
    }

    // ---- 草叉：双手锐器 ----

    [Fact]
    public void Pitchfork_IsTwoHandedSharp()
    {
        Weapon fork = WeaponTable.Pitchfork();
        Assert.Equal("草叉", fork.Name);
        Assert.Equal(DamageType.Sharp, fork.DamageType);
        Assert.True(fork.TwoHanded);
        Assert.False(fork.IsRanged);
        Assert.True(fork.DamageMax > fork.DamageMin);
        Assert.True(fork.Penetration > 0);
    }

    // ---- 冲锋枪：双手抵肩、**不可双持**（用户拍板「保双手，放弃双持」，推翻旧的"放开可双持"口径） ----

    [Fact]
    public void Smg_IsTwoHanded_AndNotDualWieldable()
    {
        Weapon smg = WeaponTable.Smg();
        Assert.True(smg.TwoHanded);
        // 旧口径曾断言 CanDualWield=true，但双手武器在 EquipToHand 里被短路、永远进不了双持分支，
        // 那个标记从未生效 ⇒ 用户拍板删除。双持只对单手武器开放（设计文档 §5「双持限单手类」）。
        Assert.False(smg.CanDualWield);
    }

    // ---- 两把新近战入 Arsenal ----

    [Fact]
    public void Arsenal_ContainsNewMeleeWeapons()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.Contains("刺剑", names);
        Assert.Contains("草叉", names);
    }

    // ---- 栓动猎枪：**已被用户从数值表删除**（T29）----
    //
    // 本处原有 BoltActionHuntingRifle_InArsenal_WithBetweenStats，钉的是"这把民用猎枪存在，
    // 且数值介于步枪与自制猎枪之间"。用户在 wiki 上把整行划掉（墓碑 sync=「删除·待同步进代码」）
    // ⇒ **它编码的意图已被整体推翻**：武器都没了，谈何"数值介于两者之间"。
    // 故不是改数字，而是**改钉新意图**：这把枪不许再回到 Arsenal（防止日后有人"顺手补回来"）。

    [Fact]
    public void BoltActionHuntingRifle_已被用户删除_不得再出现在武器表里()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.DoesNotContain("栓动猎枪", names);

        // ⚠️ Arsenal 总数只是附加护栏；本测试真正意图是"栓动猎枪不许回来"。
        //    以后再加武器，改这个数字就行；但 DoesNotContain 那条一个字都不许动。
        Assert.Equal(27, WeaponTable.Arsenal().Count);
    }

    // ---- 粗布外套：外套层，防护劣于皮甲 ----

    [Fact]
    public void CoarseClothCoat_IsOuterLayer_WeakerThanLeather()
    {
        ArmorLayer coat = ArmorTable.CoarseClothCoat();
        ArmorLayer leather = ArmorTable.Leather();
        Assert.Equal("粗布外套", coat.Name);
        Assert.Equal(ArmorSlot.Outer, coat.Slot);
        Assert.True(coat.SharpDefense < leather.SharpDefense, "粗布外套锐防应劣于皮甲");
        Assert.True(coat.BluntDefense < leather.BluntDefense, "粗布外套钝防应劣于皮甲");
        Assert.True(coat.SharpDefense > 0 && coat.BluntDefense > 0);
    }

    // ---- 劳保手套：物品定义不分左右（表里只有一行），表口径覆盖双手含五指；
    //      实际一件只占一只手槽、只护那一只手，双手要两件（[SPEC-B18-补]，见 ApparelSlotsTests）----

    [Fact]
    public void WorkGloves_AreOneUnsidedDef_TableCoversBothHands()
    {
        // 表口径（这件"能"护的部位类别）；穿戴时按槽裁剪成单只手。
        ArmorLayer gloves = ArmorTable.WorkGloves();
        Assert.Equal("劳保手套", gloves.Name);
        Assert.Equal(ArmorSlot.Skin, gloves.Slot);
        Assert.NotNull(gloves.CoversParts);
        Assert.Contains(HumanBody.LeftHand, gloves.CoversParts!);
        Assert.Contains(HumanBody.RightHand, gloves.CoversParts!);
        Assert.Contains(HumanBody.LeftThumb, gloves.CoversParts!);   // 连带五指子树
        Assert.Contains(HumanBody.RightPinky, gloves.CoversParts!);
        Assert.DoesNotContain(HumanBody.Chest, gloves.CoversParts!);

        // 全身最轻的一件（覆盖面最小）。
        Assert.True(gloves.Weight < ArmorTable.CoarseClothCoat().Weight, "手套应比粗布外套轻");
        Assert.True(gloves.SharpDefense > 0 && gloves.BluntDefense > 0);
    }

    // ---- [T68] 恐怖装甲 / 平光眼镜：焊死登记与数值形态 ----
    //
    // 两件是用户 Wiki 手写、由同步单回写进代码的。本组焊死：配置映射/护甲层/
    // 覆盖部位 + **消费层 ApparelCatalog 真登记落对槽**（ItemDef/ArmorTable 写了数值 ≠ 装备目录能穿——两层都要焊）。

    [Fact]
    public void HorrorArmor_装甲层护胸腹_数值与登记焊死()
    {
        ArmorLayer a = ArmorTable.HorrorArmor();
        Assert.Equal("恐怖装甲", a.Name);
        Assert.Equal(ArmorSlot.Plate, a.Slot);
        Assert.Equal(20, a.SharpDefense);
        Assert.Equal(10, a.BluntDefense);
        Assert.Equal(3, a.Weight);
        Assert.NotNull(a.CoversParts);
        Assert.Equal(new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen }, a.CoversParts!);
        Assert.Equal("每一片防护都来自于没做够防护的人", a.Description);   // 用户原话

        Assert.True(ApparelCatalog.IsApparel("恐怖装甲"), "恐怖装甲必须在 ApparelCatalog 里，否则造得出穿不上");
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("恐怖装甲");
        Assert.NotNull(def);
        Assert.Contains(EquipSlot.PlateLayer, def!.Slots);
    }

    [Fact]
    public void PlainGlasses_眼镜槽护双眼_数值与登记焊死()
    {
        ArmorLayer g = ArmorTable.PlainGlasses();
        Assert.Equal("平光眼镜", g.Name);
        Assert.Equal(ArmorSlot.Skin, g.Slot);
        Assert.Equal(1, g.SharpDefense);
        Assert.Equal(1, g.BluntDefense);
        Assert.Equal(0.1, g.Weight);
        Assert.NotNull(g.CoversParts);
        Assert.Equal(new HashSet<string> { HumanBody.LeftEye, HumanBody.RightEye }, g.CoversParts!);
        Assert.Equal("至少看起来很像知识分子。", g.Description);   // 用户原话

        Assert.True(ApparelCatalog.IsApparel("平光眼镜"), "平光眼镜必须在 ApparelCatalog 里");
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("平光眼镜");
        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.Eyes }, def!.Slots);
    }

    // ---- [装备→能力加成] 平光眼镜阅读速度效果：登记 + 真生效两层焊死 ----
    //
    // 🔴 教训「登记效果 ≠ 生效」：ApparelDef.Effects 里挂了效果，若消费方不经 ApparelEffectMultiplier 从穿戴品取数
    //    （手写常数），效果就是摆设。故两层各焊一条，真生效那条**走 name→查表→乘子→Effective 全链，绝不手传配置值**。

    [Fact]
    public void PlainGlasses_读速效果已登记_焊死()
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("平光眼镜");
        Assert.NotNull(def);
        Assert.NotNull(def!.Effects);
        Assert.Contains(def.Effects!, e => e.Kind == ApparelCatalog.EquipEffectKind.ReadingSpeed && e.Multiplier == 1.05);
    }

    [Fact]
    public void PlainGlasses_读速效果真生效_全链焊死()
    {
        // 走真聚合函数（穿戴品名 → 查 Defs → 取 Effects → 连乘），不手写配置乘子。
        double wornMult = ApparelCatalog.ApparelEffectMultiplier(
            new[] { "平光眼镜" }, ApparelCatalog.EquipEffectKind.ReadingSpeed);
        Assert.Equal(1.05, wornMult, precision: 10);

        // 无穿戴 / 戴无读速效果的墨镜 → 中性乘子（不误伤）。
        Assert.Equal(1.0, ApparelCatalog.ApparelEffectMultiplier(
            System.Array.Empty<string>(), ApparelCatalog.EquipEffectKind.ReadingSpeed), precision: 10);
        Assert.Equal(1.0, ApparelCatalog.ApparelEffectMultiplier(
            new[] { "墨镜" }, ApparelCatalog.EquipEffectKind.ReadingSpeed), precision: 10);

        // 把真乘子喂进真读速合成：戴平光眼镜的结果体现 Wiki 配置效果（全链贯通，非摆设）。
        double worn = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0, apparelMult: wornMult);
        double bare = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0, apparelMult: 1.0);
        Assert.Equal(bare * 1.05, worn, precision: 10);
    }

    // ---- 隔离/零漂移护栏 ----
    //
    // 效果只挂消费层 ApparelDef，**没**污染零依赖战斗引擎 ArmorLayer 的战斗数值：平光眼镜的 ArmorLayer 与墨镜仍全等
    // （效果不在引擎类型上）⇒ CombatResolver/Duel 读不到读速效果 ⇒ Sim 结构性零漂移。

    [Fact]
    public void PlainGlasses_读速效果只在消费层_未污染引擎护甲数值()
    {
        ArmorLayer glasses = ArmorTable.PlainGlasses();
        ArmorLayer sunglasses = ArmorTable.Sunglasses();
        Assert.Equal(sunglasses.Slot, glasses.Slot);
        Assert.Equal(sunglasses.SharpDefense, glasses.SharpDefense);
        Assert.Equal(sunglasses.BluntDefense, glasses.BluntDefense);
        Assert.Equal(sunglasses.Weight, glasses.Weight);
        Assert.Equal(sunglasses.CoversParts, glasses.CoversParts);
    }

    [Fact]
    public void 新增两件不进任何生成套_保Sim基线零漂移()
    {
        var survivorSetNames = ArmorTable.SurvivorArmor().Select(a => a.Name).ToList();
        Assert.DoesNotContain("恐怖装甲", survivorSetNames);
        Assert.DoesNotContain("平光眼镜", survivorSetNames);
    }

    // ---- [T72] 护踝鞋具：成对·脚槽，护小腿+脚(含趾)。数值/护甲层/覆盖 + ApparelCatalog 真登记(成对、落脚槽) 三层焊死 ----
    //
    // 用户 wiki 手写(数值表『护甲表』new_armor_2)，本单回写进代码三层：ArmorTable 工厂 / ApparelSlots 成对脚槽 /
    // ItemDef 花名册(称重)。写了数值 ≠ 穿得上 ≠ 称得准——三层都要焊(同恐怖装甲/平光眼镜的教训)。

    [Fact]
    public void AnkleGuard_成对脚槽护小腿和脚_数值与覆盖焊死()
    {
        ArmorLayer a = ArmorTable.AnkleGuard();
        Assert.Equal("护踝鞋具", a.Name);
        Assert.Equal(ArmorSlot.Skin, a.Slot);
        Assert.Equal(12, a.SharpDefense);
        Assert.Equal(6, a.BluntDefense);
        Assert.Equal(0.75, a.Weight);

        // 表口径覆盖：两侧小腿+脚+十趾（小腿子树含脚，脚挂小腿下）；不碰躯干/大腿。
        Assert.NotNull(a.CoversParts);
        Assert.Contains(HumanBody.LeftCalf, a.CoversParts!);
        Assert.Contains(HumanBody.RightCalf, a.CoversParts!);
        Assert.Contains(HumanBody.LeftFoot, a.CoversParts!);
        Assert.Contains(HumanBody.RightFoot, a.CoversParts!);
        Assert.Contains(HumanBody.LeftBigToe, a.CoversParts!);   // 连带五趾子树
        Assert.Contains(HumanBody.RightToe5, a.CoversParts!);
        Assert.DoesNotContain(HumanBody.Chest, a.CoversParts!);
        Assert.DoesNotContain(HumanBody.LeftLeg, a.CoversParts!); // 大腿不护
    }

    [Fact]
    public void AnkleGuard_ApparelCatalog成对登记_一件占一只脚槽护那侧()
    {
        Assert.True(ApparelCatalog.IsApparel("护踝鞋具"), "护踝鞋具必须在 ApparelCatalog 里，否则造得出穿不上");
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("护踝鞋具");
        Assert.NotNull(def);
        Assert.True(def!.Paired, "护踝鞋具是成对品(一件占一只脚槽)");
        // 候选槽 = 左右脚；一件只占其一。
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.LeftFoot, EquipSlot.RightFoot }, def.Slots);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.LeftFoot }, def.SlotsFor(EquipSlot.LeftFoot));
        // 装左脚 → 只护左侧小腿+脚+趾，不碰右侧。
        IReadOnlySet<string>? leftCovers = def.CoversFor(EquipSlot.LeftFoot);
        Assert.NotNull(leftCovers);
        Assert.Contains(HumanBody.LeftCalf, leftCovers!);
        Assert.Contains(HumanBody.LeftFoot, leftCovers!);
        Assert.DoesNotContain(HumanBody.RightFoot, leftCovers!);
    }

    [Fact]
    public void AnkleGuard_不进任何生成套_保Sim基线零漂移()
    {
        Assert.DoesNotContain("护踝鞋具", ArmorTable.SurvivorArmor().Select(a => a.Name).ToList());
    }

    // ---- [警察局] 防弹背心：**贴身层**·护胸+腹。数值/护甲层/覆盖 + ApparelCatalog 真登记(落贴身层单槽) 焊死 ----
    //
    // 用户 authored（新探索关「警察局」掉落）。四层登记(工厂/贴身层槽/ItemDef花名册/NightWatch潜行)——写了数值 ≠ 穿得上
    // ≠ 称得准，各层都要焊(同恐怖装甲/护踝鞋具的教训)。🔴 关键焊死：它是**贴身层(Skin)不是装甲层(Plate)** ⇒
    // 占 SkinLayer 而非 PlateLayer ⇒ 能与皮甲/板甲叠穿。数值皆拟定待 Sim 校准，这里只锁"规则形态"。

    [Fact]
    public void BallisticVest_贴身层护胸腹_数值与覆盖焊死()
    {
        ArmorLayer v = ArmorTable.BallisticVest();
        Assert.Equal("防弹背心", v.Name);
        // 🔴 贴身层，不是装甲层——占贴身层槽才能与皮甲/板甲叠穿。
        Assert.Equal(ArmorSlot.Skin, v.Slot);
        Assert.Equal(24, v.SharpDefense);
        Assert.Equal(6, v.BluntDefense);
        Assert.Equal(2.5, v.Weight);
        // 覆盖：胸 + 腹（护胸腹），不碰双臂/双腿/头。
        Assert.NotNull(v.CoversParts);
        Assert.Equal(new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen }, v.CoversParts!);
        Assert.DoesNotContain(HumanBody.LeftArm, v.CoversParts!);
        // 当前 Wiki 配置是锐防高于钝防；额外备注不在统一规则层生效。
        Assert.True(v.SharpDefense > v.BluntDefense, "防弹背心应遵循 Wiki 配置的锐防/钝防关系");
    }

    [Fact]
    public void BallisticVest_ApparelCatalog登记落贴身层_非装甲层()
    {
        Assert.True(ApparelCatalog.IsApparel("防弹背心"), "防弹背心必须在 ApparelCatalog 里，否则造得出穿不上");
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("防弹背心");
        Assert.NotNull(def);
        // 🔴 占贴身层槽(SkinLayer)、不占装甲层槽(PlateLayer) ⇒ 与皮甲/板甲不互斥、可叠穿。
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.SkinLayer }, def!.Slots);
        Assert.DoesNotContain(EquipSlot.PlateLayer, def.Slots);
    }

    [Fact]
    public void BallisticVest_贴身层打底可与板甲叠穿_装甲层槽不冲突()
    {
        // 防弹背心占贴身层、板甲占装甲层(+裤装) ⇒ 两件可同时在身（真"叠穿"，不是互斥）。
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "防弹背心"));
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "板甲"));
        Assert.True(slots.IsEquipped("防弹背心"));
        Assert.True(slots.IsEquipped("板甲"));
    }

    [Fact]
    public void BallisticVest_不进任何生成套_保Sim基线零漂移()
    {
        Assert.DoesNotContain("防弹背心", ArmorTable.SurvivorArmor().Select(a => a.Name).ToList());
    }
}
