using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 尸体 = 一个可搜刮点，<b>它穿的就是它掉的</b>（<see cref="CorpseLoot"/>）。
///
/// <para><b>用户拍板：所见即所得，零掷骰</b>——「做成尸体会变成一个可搜刮点」「丧尸穿的是什么，就原原本本
/// 的写出来，可以直接扒下来」。此前那套「软质 50% / 刚性 90%」的材质分档掷骰<b>已被推翻并删除</b>。</para>
///
/// <para><b>为什么零掷骰更好</b>（这是本模块的设计核心，别做丢）：远远看见一只丧尸穿着牛仔外套 ⇒
/// <b>那就是一件牛仔外套在那儿走着</b>；那个戴防暴头盔的精英，就是<b>一顶明晃晃的防暴头盔</b>。
/// 丧尸从"障碍"变成了<b>行走的、可见的、可评估的战利品</b>——值不值得为它冒险，玩家自己算。
/// <b>掷骰会把这个决策变成赌博</b>：运气不该构成决策，可见的价值才构成决策。</para>
///
/// <para>只剩两条例外：① <b>腐皮不掉</b>（那是烂肉，不是装备）；② <b>掉的必须穿得上</b>（在穿戴目录里）。</para>
///
/// <para><b>武器同理</b>（用户拍板：「敌人掉武器的，他的武器直接落在他的可搜刮尸体里」）——持什么掉什么、必掉。
/// 天生武器（爪击/撕咬/拳脚）是那条例外在武器侧的镜像：它们不是装备，是身体。</para>
///
/// <para>⚠️ 本类挂 <see cref="ModdedWeaponRegistryCollection"/>：其中一条测试要 <c>Clear</c>/<c>Register</c>
/// 那张<b>进程级静态</b>注册表，不串行会随机弄红别的类（见该 collection 的注释）。</para>
/// </summary>
[Collection(ModdedWeaponRegistryCollection.Name)]
public class CorpseLootTests
{
    /// <summary>
    /// [T56] 只看战利品里「<b>装备</b>」那一段（武器 + 护甲），把<b>固定产出的骨头</b>滤掉。
    /// <para>
    /// 本文件通篇讲的是同一条规则——「<b>穿什么扒什么、持什么掉什么</b>，零掷骰、一件不少」。
    /// 骨头是**另一条**规则（用户拍板「尸体固定产出一个骨头」，见 <c>BoneKnifeAndHuntingBowTests</c>），
    /// 与"所见即所得"无关：它不是这具尸体身上**看得见**的东西，玩家也不会因为看见它而决定动不动手。
    /// 把它滤掉，这些断言才继续说的是它们本来要说的那件事。
    /// </para>
    /// </summary>
    private static IReadOnlyList<LootItem> Gear(
        IEnumerable<ArmorLayer> worn, IEnumerable<Weapon>? held = null)
        => CorpseLoot.Strip(worn, held).Where(l => l.Kind != LootKind.Material).ToList();

    // ---- 核心：穿什么扒什么，一件不少 ----

    /// <summary>一只穿着夹克的丧尸倒下 ⇒ 那件夹克、那件布衣、那条裤子，一件不少地躺在那儿。</summary>
    [Fact]
    public void EverythingWorn_IsStrippable_NothingLost()
    {
        IReadOnlyList<ArmorLayer> worn = ZombieOutfit.ArmorOf("穿牛仔外套的");   // 牛仔外套+长袖布衣+长裤+腐皮

        IReadOnlyList<LootItem> loot = Gear(worn);

        Assert.Equal(
            new[] { LootItem.Armor("牛仔外套"), LootItem.Armor("长袖布衣"), LootItem.Armor("长裤") },
            loot);
    }

    /// <summary>
    /// <b>零随机</b>：同一具尸体扒一百次，结果一模一样。<see cref="CorpseLoot"/> 不接随机源、也不该接——
    /// 它连 <see cref="IRandomSource"/> 都拿不到，想赌都赌不了。
    /// </summary>
    [Fact]
    public void Stripping_IsDeterministic_NoDiceAtAll()
    {
        IReadOnlyList<ArmorLayer> worn = ZombieOutfit.ArmorOf("防暴警察丧尸");

        List<string> first = Gear(worn).Select(l => l.RefId).ToList();
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, Gear(worn).Select(l => l.RefId).ToList());
        }
    }

    /// <summary>
    /// <b>精英丧尸头上那顶盔，杀了就是你的</b>——不再有"一半概率凭空蒸发"。
    /// 那是玩家为一场硬仗换来的奖励，也是头盔的主要获取途径。
    /// </summary>
    [Fact]
    public void RiotCopZombie_AlwaysDropsItsHelmetAndPlate()
    {
        List<string> loot = Gear(ZombieOutfit.ArmorOf("防暴警察丧尸"))
            .Select(l => l.RefId).ToList();

        Assert.Contains("防暴头盔", loot);
        Assert.Contains("板甲", loot);
        Assert.Contains("粗布外套", loot);
        Assert.Contains("长袖布衣", loot);
        Assert.Contains("劳保手套", loot);
        Assert.DoesNotContain("腐皮", loot);
    }

    [Fact]
    public void SoldierZombie_AlwaysDropsItsMilitaryHelmet()
    {
        List<string> loot = Gear(ZombieOutfit.ArmorOf("军人丧尸"))
            .Select(l => l.RefId).ToList();

        Assert.Contains("军用头盔", loot);
        Assert.Contains("皮甲", loot);
    }

    /// <summary>掉落顺序 = 它身上的层序（由外到内）：先是外套，再是里面的衣服。</summary>
    [Fact]
    public void LootOrder_MirrorsTheLayerOrder_OuterFirst()
    {
        List<string> loot = Gear(ZombieOutfit.ArmorOf("穿皮夹克的"))
            .Select(l => l.RefId).ToList();

        Assert.Equal(new[] { "皮夹克", "长袖布衣", "长裤" }, loot);
    }

    // ---- 例外一：腐皮永不掉 ----

    [Fact]
    public void ZombieHide_NeverDrops_ItIsRottenMeat_NotGear()
    {
        Assert.Empty(Gear(ArmorTable.ZombieHide()));
    }

    /// <summary>不在穿戴目录里的东西（天生甲 / 未来的怪物硬壳）一律不掉。</summary>
    [Fact]
    public void UnwearableLayers_AreSkipped()
    {
        var innate = new ArmorLayer
        {
            Name = "甲壳", Slot = ArmorSlot.Skin, SharpDefense = 9, BluntDefense = 9, Weight = 0,
        };

        Assert.Empty(Gear(new[] { innate }));
    }

    /// <summary>
    /// 衣不蔽体的那 15%：一具光尸体身上<b>没有一件装备</b>。
    /// <para>
    /// ⚠️ [T56] <b>这条测试的结论变了，规则也变了</b>。它从前叫
    /// <c>NakedZombie_YieldsNothing_SoItIsNotEvenASearchablePoint</c>——「扒不出任何东西 ⇒ 不登记搜刮点」。
    /// 用户随后拍板「<b>尸体固定产出一个骨头</b>」⇒ <see cref="CorpseLoot.Strip"/> <b>永不返回空</b>，
    /// 连光尸体也有一根骨头 ⇒ 它<b>现在是</b>可搜刮点了。
    /// </para>
    /// <para>
    /// <b>「不登记搜刮点」那道闸门并没有废</b>，它的含义依然是「**没东西**的尸体不该留一个点了没反应的假点」；
    /// 只是现在没有"没东西"的尸体了。而且这不是"尸潮变成一地假点"——**85% 的丧尸尸体本来就已经是搜刮点**
    /// （9 个日常预设里只有「衣不蔽体」是空的），固定产骨把它从 85% 抬到 100%，不是从 0 抬到 100%。
    /// </para>
    /// <para>骨头本身的断言在 <c>BoneKnifeAndHuntingBowTests</c>；这里只钉「<b>装备</b>是空的」。</para>
    /// </summary>
    [Fact]
    public void NakedZombie_HasNoGear_ButStillYieldsItsBone()
    {
        Assert.Empty(Gear(ZombieOutfit.ArmorOf("衣不蔽体")));

        // 但它**不再**是一具"什么都没有"的尸体——它有一根骨头，所以它是个**真的**搜刮点。
        IReadOnlyList<LootItem> all = CorpseLoot.Strip(ZombieOutfit.ArmorOf("衣不蔽体"));
        Assert.Single(all);
        Assert.Equal("bone", all[0].RefId);
    }

    // ---- 例外二：掉的必须穿得上 ----

    [Fact]
    public void EverythingDropped_IsActuallyWearable()
    {
        foreach (string preset in new[] { "防暴警察丧尸", "军人丧尸", "寻常打扮", "夏日打扮", "套着粗布外套" })
        {
            foreach (LootItem l in Gear(ZombieOutfit.ArmorOf(preset)))
            {
                Assert.Equal(LootKind.Armor, l.Kind);
                Assert.True(ApparelCatalog.IsApparel(l.RefId), $"{preset} 掉了穿不上的「{l.RefId}」");
            }
        }
    }

    /// <summary>
    /// <b>「看货下手」的机器可证版本</b>：日常池里每一套的"可见着装"都恰好等于它的战利品——
    /// 逐件、同序、无损。玩家远远看见什么，杀完就扒到什么，中间没有任何一层随机。
    /// </summary>
    [Fact]
    public void WhatYouSee_IsExactlyWhatYouGet_ForEveryEverydayOutfit()
    {
        foreach (ZombieOutfitPreset preset in ZombieOutfit.Presets)
        {
            List<string> visible = preset.Clothes().Select(c => c.Name).ToList();
            List<string> looted = Gear(ZombieOutfit.ArmorOf(preset.Name))
                .Select(l => l.RefId).ToList();

            Assert.Equal(visible, looted);
        }
    }

    // ---- 阵营中立 ----

    [Fact]
    public void RaiderCorpse_DropsItsJacketToo()
    {
        List<string> loot = Gear(ArmorTable.SurvivorArmor()).Select(l => l.RefId).ToList();

        Assert.Equal(new[] { "皮夹克", "长袖布衣" }, loot);
    }

    /// <summary>狗装备也在册（布鲁斯倒下 → 那身手搓的狗衣扒得下来，不该跟着尸体一起蒸发）。</summary>
    [Fact]
    public void DogGear_IsSalvageableFromTheDog()
    {
        List<string> loot = Gear(new[] { ArmorTable.DogIronHelmet(), ArmorTable.DogClothVest() })
            .Select(l => l.RefId).ToList();

        Assert.Equal(new[] { "铁皮头甲", "布制狗衣" }, loot);
    }

    // ---- 掷骰真的没了（防回归）----

    /// <summary>
    /// 用户推翻的那套「软质 50% / 刚性 90%」<b>必须彻底消失</b>，不能留成"默认 1.0 的常量"苟着——
    /// 留着它就是留着一个随时会被重新拧开的旋钮。本条锁死：那两个常量与那个方法都不存在。
    /// </summary>
    [Fact]
    public void SalvageChanceKnobs_AreGone_NotJustSetToOne()
    {
        System.Type t = typeof(CorpseLoot);

        Assert.Null(t.GetField("ClothSalvageChance"));
        Assert.Null(t.GetField("RigidSalvageChance"));
        Assert.Null(t.GetMethod("SalvageChanceOf"));
    }

    // ================= 手里那把家伙 =================
    // 用户拍板：「敌人掉武器的，他的武器直接落在他的可搜刮尸体里。」
    // ⇒ 与衣服同一条口径：**持什么掉什么，必掉、零掷骰、不折损**。

    /// <summary>
    /// <b>劫掠者本来就持械，杀了他就该拿到他的家伙。</b>这是全图近战武器的主要来源——
    /// 在此之前，一个劫掠者死了只留下他的夹克，那把匕首凭空蒸发。
    /// </summary>
    [Fact]
    public void RaiderCorpse_DropsTheDaggerHeWasHolding()
    {
        IReadOnlyList<LootItem> loot = Gear(
            ArmorTable.SurvivorArmor(), new[] { WeaponTable.Dagger() });

        Assert.Equal(
            new[] { LootItem.Weapon("匕首"), LootItem.Armor("皮夹克"), LootItem.Armor("长袖布衣") },
            loot);
    }

    /// <summary>武器排在护甲之前：玩家点开尸体，第一眼看见的是那把家伙——它才是决定值不值得动手的东西。</summary>
    [Fact]
    public void WeaponsComeFirst_ThenTheClothes()
    {
        IReadOnlyList<LootItem> loot = Gear(
            ZombieOutfit.ArmorOf("穿皮夹克的"), new[] { WeaponTable.Pistol() });

        Assert.Equal(LootKind.Weapon, loot[0].Kind);
        Assert.All(loot.Skip(1), l => Assert.Equal(LootKind.Armor, l.Kind));
    }

    // ---- 天生武器永不掉（与"腐皮不掉"同一条例外）----

    /// <summary>
    /// <b>丧尸不持械</b>——它用爪牙。所以丧尸尸体里该有的是<b>衣服，不是武器</b>：
    /// 爪击是它身体的一部分，跟腐皮一样，扒不下来也穿不上。
    /// </summary>
    [Fact]
    public void ZombieCorpse_DropsItsClothes_ButNeverItsClaws()
    {
        IReadOnlyList<LootItem> loot = Gear(
            ZombieOutfit.ArmorOf("穿皮夹克的"), new[] { WeaponTable.ZombieClaw() });

        Assert.DoesNotContain(loot, l => l.Kind == LootKind.Weapon);
        Assert.Equal(new[] { "皮夹克", "长袖布衣", "长裤" }, loot.Select(l => l.RefId));
    }

    /// <summary>布鲁斯倒下 ⇒ 那身狗衣扒得下来，但它的<b>牙</b>扒不下来。</summary>
    [Fact]
    public void DogCorpse_NeverDropsItsBite()
    {
        IReadOnlyList<LootItem> loot = Gear(
            new[] { ArmorTable.DogClothVest() }, new[] { WeaponTable.DogBite() });

        Assert.Equal(new[] { LootItem.Armor("布制狗衣") }, loot);
    }

    /// <summary>空手的人死了，掉不出一双"拳脚"来。</summary>
    [Fact]
    public void BareHandedHuman_DropsNoFists()
    {
        Assert.DoesNotContain(
            Gear(ArmorTable.SurvivorArmor(), new[] { WeaponTable.Fists() }),
            l => l.Kind == LootKind.Weapon);
    }

    /// <summary>
    /// 三件天生武器（爪击/撕咬/拳脚）不进 <see cref="WeaponTable.Arsenal"/> ⇒ 按名回查恒空 ⇒ <b>结构性</b>掉不出来。
    /// 这条把"为什么不掉"钉在判据上，而不是钉在一张会腐化的黑名单上：
    /// 日后新增任何天生武器，只要它没进 Arsenal，就自动不掉。
    /// </summary>
    [Fact]
    public void InnateWeapons_AreNotSalvageable_BecauseTheyAreNotInTheArsenal()
    {
        foreach (Weapon innate in new[] { WeaponTable.ZombieClaw(), WeaponTable.DogBite(), WeaponTable.Fists() })
        {
            Assert.False(CorpseLoot.IsSalvageable(innate), $"天生武器「{innate.Name}」不该扒得下来");
            Assert.DoesNotContain(WeaponTable.Arsenal(), w => w.Name == innate.Name);
        }
    }

    // ---- 一把武器只掉一把 ----

    /// <summary>
    /// 🔴 <b>双手握一把 ≠ 两把</b>：<see cref="WeaponLoadout.EquipTwoHanded"/> 把<b>同一个</b> Weapon 实例
    /// 同时放进左右手（<c>TwoHandGrip=true</c>）。天真地"左手 + 右手"一读，一个抱着重剑倒下的人会掉出<b>两把重剑</b>——
    /// 凭空印钱。<see cref="WeaponLoadout.HeldWeapons"/> 按 <c>TwoHandGrip</c> 去重，本条钉死。
    /// </summary>
    [Fact]
    public void TwoHandedWeapon_DropsExactlyOnce_NotTwice()
    {
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipTwoHanded(WeaponTable.Greatsword()));

        Assert.Equal(new[] { "重剑" }, loadout.HeldWeapons.Select(w => w.Name));
        Assert.Equal(
            new[] { LootItem.Weapon("重剑") },
            Gear(System.Array.Empty<ArmorLayer>(), loadout.HeldWeapons));
    }

    /// <summary>双持短剑的人倒下 ⇒ 掉<b>两把</b>短剑（他手里真的有两把）。</summary>
    [Fact]
    public void DualWield_DropsBothBlades()
    {
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipToHand(WeaponTable.Shortsword(), Hand.Right));
        Assert.True(loadout.EquipToHand(WeaponTable.Shortsword(), Hand.Left));

        Assert.Equal(
            new[] { LootItem.Weapon("短剑"), LootItem.Weapon("短剑") },
            Gear(System.Array.Empty<ArmorLayer>(), loadout.HeldWeapons));
    }

    /// <summary>空着两手的人 ⇒ 一把武器都不掉（连 null 都不该混进清单）。</summary>
    [Fact]
    public void EmptyHands_YieldNoWeapons()
    {
        Assert.Empty(new WeaponLoadout().HeldWeapons);
        Assert.Empty(Gear(System.Array.Empty<ArmorLayer>(), new WeaponLoadout().HeldWeapons));
    }

    // ---- 改装武器不许在尸体上蒸发 ----

    /// <summary>
    /// 改装枪<b>不在 <see cref="WeaponTable"/> 里</b>（"步枪（刺刀型）"是运行时合成的变体）。若掉落判据只认原厂表，
    /// 队员带着一把改装步枪战死 ⇒ 那把枪连同改装材料<b>静默蒸发</b>。判据走
    /// <see cref="ModdedWeaponRegistry.WeaponByName"/>（全项目唯一的"按名回查武器"入口：先原厂表、后改装表），
    /// 变体名即库存 <c>Item.RefKey</c> ⇒ 扒下来能直接入库、能再装备。
    /// </summary>
    [Fact]
    public void ModdedWeapon_SurvivesTheCorpse_DoesNotSilentlyVanish()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            WeaponMod bayonet = WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Id == "bayonet");
            ModdedWeapon modded = WeaponMods.ApplyMods(
                WeaponTable.Arsenal().First(w => w.Name == "步枪"), new[] { bayonet });
            string variantName = ModdedWeaponRegistry.Register(modded);

            Assert.True(CorpseLoot.IsSalvageable(modded.Weapon));
            Assert.Equal(
                new[] { LootItem.Weapon(variantName) },
                Gear(System.Array.Empty<ArmorLayer>(), new[] { modded.Weapon }));

            // 扒下来那个名字必须还能回查成一把枪——否则入库即成孤儿。
            Assert.NotNull(ModdedWeaponRegistry.WeaponByName(variantName));
        }
        finally
        {
            ModdedWeaponRegistry.Clear();
        }
    }

    // ---- 枪掉下来是空的 ----

    /// <summary>
    /// 🔴 <b>枪掉下来不带子弹</b>：敌方根本<b>没有弹匣模型</b>——丧尸/劫掠者用
    /// <c>UnlimitedAmmoSource</c>（Actor.cs:704），恒可开火、不扣弹。既然"他还剩几发"这个量在引擎里不存在，
    /// 就不能凭空发明一个数出来。
    /// <para>后果正是既有设计想要的：<b>枪的代价是弹药</b>——你从劫掠者尸体上捡到一把手枪，
    /// 但子弹得自己去找。掉落清单里因此<b>不含任何 Material（子弹）条目</b>。</para>
    /// </summary>
    [Fact]
    public void LootedGun_ComesWithNoAmmo_BecauseEnemiesHaveNoMagazineModel()
    {
        IReadOnlyList<LootItem> loot = Gear(
            ArmorTable.SurvivorArmor(), new[] { WeaponTable.Pistol() });

        Assert.Contains(LootItem.Weapon("手枪"), loot);
        Assert.DoesNotContain(loot, l => l.Kind == LootKind.Material);
        Assert.All(loot, l => Assert.True(l.Kind is LootKind.Weapon or LootKind.Armor));
    }

    // ---- 必掉，不掷骰 ----

    /// <summary>
    /// 武器侧同样<b>零随机</b>：扒一百次，那把匕首每次都在。用户原话是"直接落在他的可搜刮尸体里"——
    /// 直接，不是"有几率"。
    /// </summary>
    [Fact]
    public void WeaponDrop_IsAlwaysGuaranteed_NeverARoll()
    {
        for (int i = 0; i < 100; i++)
        {
            Assert.Contains(
                LootItem.Weapon("匕首"),
                Gear(ArmorTable.SurvivorArmor(), new[] { WeaponTable.Dagger() }));
        }
    }

    /// <summary>老口径（只扒衣服）的单参调用必须<b>逐字节不变</b>——本单是加通道，不是改既有行为。</summary>
    [Fact]
    public void ArmorOnlyOverload_IsUnchanged()
    {
        Assert.Equal(
            Gear(ZombieOutfit.ArmorOf("穿牛仔外套的")),
            Gear(ZombieOutfit.ArmorOf("穿牛仔外套的"), null));
    }
}
