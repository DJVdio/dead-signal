using System.Collections.Generic;
using DeadSignal.Combat;   // ArmorLayer（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 ApparelSlots.cs / ContainerLoot.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间侧（尸体落在哪、点它弹什么面板）在 CorpseYard / CampMain；本类只回答一个问题：
// **这具尸体身上有哪些东西能扒下来穿。**

/// <summary>
/// 尸体 = 一个可搜刮点，<b>它穿的就是它掉的</b>。
///
/// <para><b>用户拍板</b>：「做成尸体会变成一个可搜刮点」「**丧尸穿的是什么，就原原本本的写出来，可以直接扒下来**」。
/// ⇒ <b>所见即所得，零掷骰</b>。此前那套「软质 50% / 刚性 90%」的材质分档已被推翻并删除——
/// 本类<b>不接随机源</b>，连 <c>IRandomSource</c> 都拿不到。</para>
///
/// <para><b>这条规则是玩法的地基，别当成"省事"</b>：远远看见一只丧尸穿着牛仔外套 ⇒
/// <b>那就是一件牛仔外套在那儿走着</b>（而牛仔外套只能搜刮、缝不出来）；那个戴防暴头盔的精英，
/// 就是<b>一顶明晃晃的防暴头盔</b>。丧尸从"障碍"变成了<b>行走的、可见的、可评估的战利品</b>——
/// 值不值得为它冒险，玩家自己算。<b>掷骰会把这个决策变成赌博</b>：运气不该构成决策，可见的价值才构成决策。</para>
///
/// <para>只剩两条例外：
/// <list type="number">
/// <item><b>腐皮不掉</b>（以及任何未登记在穿戴目录里的天生层）——那是烂肉，不是装备。</item>
/// <item><b>掉的必须穿得上</b>（在 <see cref="ApparelCatalog"/> 或 <see cref="DogGearCatalog"/> 里）——
/// 扒下来却穿不上的东西是纯粹的垃圾，不该进背包。</item>
/// </list></para>
///
/// <para><b>阵营中立</b>：本类不问死者是谁——丧尸、劫掠者、自己人、布鲁斯身上的东西一视同仁地扒。</para>
/// </summary>
public static class CorpseLoot
{
    /// <summary>
    /// 这一件是不是<b>扒得下来、且真穿得上</b>的东西：只认已登记在穿戴目录里的（人形
    /// <see cref="ApparelCatalog"/> + 狗 <see cref="DogGearCatalog"/>）。腐皮 / 未来的怪物硬壳等天生层不在册。
    /// </summary>
    public static bool IsSalvageable(ArmorLayer layer)
        => layer is not null
           && (ApparelCatalog.IsApparel(layer.Name) || DogGearCatalog.IsDogGear(layer.Name));

    /// <summary>
    /// 把一具尸体身上的东西<b>原样</b>扒成战利品清单：按 <paramref name="worn"/> 的层序（由外到内）
    /// 逐件列出，<b>一件不少、不掷骰、不折损</b>（已包装成 <see cref="LootItem.Armor"/>，直接进
    /// <see cref="ContainerLoot"/> / <see cref="LootApplication"/> 的既有入库链路）。
    /// <para>返回空 ⇒ 这具尸体身上什么也没有（衣不蔽体）。调用方据此<b>不把它登记成可搜刮点</b>——
    /// 一具光尸体不该在地图上留一个点了没反应的可交互点；尸潮之后满地尸体时，这也顺带压掉了一批容器。</para>
    /// </summary>
    public static IReadOnlyList<LootItem> Strip(IEnumerable<ArmorLayer> worn)
    {
        var loot = new List<LootItem>();
        if (worn is null)
        {
            return loot;
        }

        foreach (ArmorLayer layer in worn)
        {
            if (IsSalvageable(layer))
            {
                loot.Add(LootItem.Armor(layer.Name));
            }
        }
        return loot;
    }
}
