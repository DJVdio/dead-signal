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
/// </summary>
public class CorpseLootTests
{
    // ---- 核心：穿什么扒什么，一件不少 ----

    /// <summary>一只穿着夹克的丧尸倒下 ⇒ 那件夹克、那件布衣、那条裤子，一件不少地躺在那儿。</summary>
    [Fact]
    public void EverythingWorn_IsStrippable_NothingLost()
    {
        IReadOnlyList<ArmorLayer> worn = ZombieOutfit.ArmorOf("穿牛仔外套的");   // 牛仔外套+长袖布衣+长裤+腐皮

        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(worn);

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

        List<string> first = CorpseLoot.Strip(worn).Select(l => l.RefId).ToList();
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(first, CorpseLoot.Strip(worn).Select(l => l.RefId).ToList());
        }
    }

    /// <summary>
    /// <b>精英丧尸头上那顶盔，杀了就是你的</b>——不再有"一半概率凭空蒸发"。
    /// 那是玩家为一场硬仗换来的奖励，也是头盔的主要获取途径。
    /// </summary>
    [Fact]
    public void RiotCopZombie_AlwaysDropsItsHelmetAndPlate()
    {
        List<string> loot = CorpseLoot.Strip(ZombieOutfit.ArmorOf("防暴警察丧尸"))
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
        List<string> loot = CorpseLoot.Strip(ZombieOutfit.ArmorOf("军人丧尸"))
            .Select(l => l.RefId).ToList();

        Assert.Contains("军用头盔", loot);
        Assert.Contains("皮甲", loot);
    }

    /// <summary>掉落顺序 = 它身上的层序（由外到内）：先是外套，再是里面的衣服。</summary>
    [Fact]
    public void LootOrder_MirrorsTheLayerOrder_OuterFirst()
    {
        List<string> loot = CorpseLoot.Strip(ZombieOutfit.ArmorOf("穿皮夹克的"))
            .Select(l => l.RefId).ToList();

        Assert.Equal(new[] { "皮夹克", "长袖布衣", "长裤" }, loot);
    }

    // ---- 例外一：腐皮永不掉 ----

    [Fact]
    public void ZombieHide_NeverDrops_ItIsRottenMeat_NotGear()
    {
        Assert.Empty(CorpseLoot.Strip(ArmorTable.ZombieHide()));
    }

    /// <summary>不在穿戴目录里的东西（天生甲 / 未来的怪物硬壳）一律不掉。</summary>
    [Fact]
    public void UnwearableLayers_AreSkipped()
    {
        var innate = new ArmorLayer
        {
            Name = "甲壳", Slot = ArmorSlot.Skin, SharpDefense = 9, BluntDefense = 9, Weight = 0,
        };

        Assert.Empty(CorpseLoot.Strip(new[] { innate }));
    }

    /// <summary>衣不蔽体的那 15%：一具光尸体扒不出任何东西 ⇒ 调用方据此<b>不登记搜刮点</b>。</summary>
    [Fact]
    public void NakedZombie_YieldsNothing_SoItIsNotEvenASearchablePoint()
    {
        Assert.Empty(CorpseLoot.Strip(ZombieOutfit.ArmorOf("衣不蔽体")));
    }

    // ---- 例外二：掉的必须穿得上 ----

    [Fact]
    public void EverythingDropped_IsActuallyWearable()
    {
        foreach (string preset in new[] { "防暴警察丧尸", "军人丧尸", "寻常打扮", "夏日打扮", "套着粗布外套" })
        {
            foreach (LootItem l in CorpseLoot.Strip(ZombieOutfit.ArmorOf(preset)))
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
            List<string> looted = CorpseLoot.Strip(ZombieOutfit.ArmorOf(preset.Name))
                .Select(l => l.RefId).ToList();

            Assert.Equal(visible, looted);
        }
    }

    // ---- 阵营中立 ----

    [Fact]
    public void RaiderCorpse_DropsItsJacketToo()
    {
        List<string> loot = CorpseLoot.Strip(ArmorTable.SurvivorArmor()).Select(l => l.RefId).ToList();

        Assert.Equal(new[] { "皮夹克", "长袖布衣" }, loot);
    }

    /// <summary>狗装备也在册（布鲁斯倒下 → 那身手搓的狗衣扒得下来，不该跟着尸体一起蒸发）。</summary>
    [Fact]
    public void DogGear_IsSalvageableFromTheDog()
    {
        List<string> loot = CorpseLoot
            .Strip(new[] { ArmorTable.DogIronHelmet(), ArmorTable.DogClothVest() })
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
}
