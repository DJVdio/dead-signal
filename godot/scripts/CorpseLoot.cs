using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // ArmorLayer / Weapon（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 ApparelSlots.cs / ContainerLoot.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间侧（尸体落在哪、点它弹什么面板）在 CorpseYard / CampMain；本类只回答一个问题：
// **这具尸体身上有哪些东西能扒下来穿。**

/// <summary>
/// 尸体 = 一个可搜刮点，<b>它穿的、它拿的，就是它掉的</b>。
///
/// <para><b>用户拍板（两条，同一口径）</b>：
/// <list type="bullet">
/// <item>衣服：「做成尸体会变成一个可搜刮点」「**丧尸穿的是什么，就原原本本的写出来，可以直接扒下来**」。</item>
/// <item>武器：「**敌人掉武器的，他的武器直接落在他的可搜刮尸体里。**」——<b>直接</b>，不是"有几率"。</item>
/// </list>
/// ⇒ <b>所见即所得，零掷骰，必掉</b>。此前那套「软质 50% / 刚性 90%」的材质分档已被推翻并删除——
/// 本类<b>不接随机源</b>，连 <c>IRandomSource</c> 都拿不到。</para>
///
/// <para><b>这条规则是玩法的地基，别当成"省事"</b>：远远看见一只丧尸穿着牛仔外套 ⇒
/// <b>那就是一件牛仔外套在那儿走着</b>（而牛仔外套只能搜刮、缝不出来）；那个戴防暴头盔的精英，
/// 就是<b>一顶明晃晃的防暴头盔</b>；那个持匕首的劫掠者，<b>就是一把匕首朝你走过来</b>。敌人从"障碍"变成了
/// <b>行走的、可见的、可评估的战利品</b>——值不值得为它冒险，玩家自己算。
/// <b>掷骰会把这个决策变成赌博</b>：运气不该构成决策，可见的价值才构成决策。</para>
///
/// <para><b>武器为什么重要</b>：劫掠者本来就持械，而全图近战武器的<b>投放几乎为零</b>（匕首/短剑/长剑/重剑/
/// 棍棒/锤子在搜刮点里一把都没有）。这条通道是玩家拿到近战武器的<b>主要来源</b>——杀了持械的人，就该拿到他的家伙。</para>
///
/// <para>例外仍然只有一条，只是它现在管两样东西：<b>天生的不算装备</b>。
/// <list type="number">
/// <item><b>腐皮不掉</b>（以及任何未登记在穿戴目录里的天生层）——那是烂肉，不是装备。</item>
/// <item><b>爪击 / 撕咬 / 拳脚不掉</b>——丧尸用爪牙、狗用牙、空手的人用拳头，那是身体的一部分，
/// 扒不下来也拿不走。⇒ <b>丧尸尸体里有的是衣服，不是武器</b>。</item>
/// <item><b>掉的必须用得上</b>：护甲在 <see cref="ApparelCatalog"/> / <see cref="DogGearCatalog"/> 里，
/// 武器在 <see cref="ModdedWeaponRegistry.WeaponByName"/> 回查得到——扒下来却装不上的东西是纯粹的垃圾，不该进背包。</item>
/// </list></para>
///
/// <para><b>枪掉下来是空的</b>：敌方<b>没有弹匣模型</b>（丧尸/劫掠者用 <c>UnlimitedAmmoSource</c>，恒可开火、不扣弹）——
/// "他还剩几发"这个量在引擎里根本不存在，不能凭空发明一个数。后果恰是既有设计想要的：<b>枪的代价是弹药</b>，
/// 捡到枪，子弹还得自己找。故本类<b>永不产出 <see cref="LootKind.Material"/>（子弹）条目</b>。</para>
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
    /// 这把武器是不是<b>拿得走、且真装得上</b>的东西。判据 = <see cref="ModdedWeaponRegistry.WeaponByName"/>
    /// 回查得到（全项目唯一的"给我一个武器名、还我一把枪"入口：<b>先原厂表、后改装表</b>）。
    ///
    /// <para>这一个判据同时解决了两件事，且都是<b>结构性</b>的、不靠维护一张会腐化的黑/白名单：</para>
    /// <list type="bullet">
    /// <item><b>天生武器自动排除</b>：爪击 / 撕咬 / 拳脚<b>不在</b> <see cref="WeaponTable.Arsenal"/> 里
    /// （它们是天生武器，玩家不可穿脱、不进库存），故按名回查恒空 ⇒ 掉不出来。日后新增任何天生武器，
    /// 只要它同样不进 Arsenal，就自动继续掉不出来——不需要有人记得回来加一行黑名单。</item>
    /// <item><b>改装武器不会蒸发</b>：「步枪（刺刀型）」是运行时合成的变体，<b>不在原厂表里</b>。若判据只认
    /// <c>WeaponTable</c>，队员带着改装枪战死 ⇒ 那把枪连同改装材料静默消失。走本入口则变体名（= 库存
    /// <c>Item.RefKey</c>）回查得到 ⇒ 扒下来能直接入库、能再装备。</item>
    /// </list>
    /// </summary>
    public static bool IsSalvageable(Weapon weapon)
        => weapon is not null && ModdedWeaponRegistry.WeaponByName(weapon.Name) is not null;

    /// <summary>
    /// 把一具尸体身上的东西<b>原样</b>扒成战利品清单：先是他<b>手里那把家伙</b>（<paramref name="held"/>，主手在前），
    /// 再是他<b>身上穿的</b>（<paramref name="worn"/>，按层序由外到内）——<b>一件不少、不掷骰、不折损</b>
    /// （已包装成 <see cref="LootItem"/>，直接进 <see cref="ContainerLoot"/> / <see cref="LootApplication"/>
    /// 的既有入库链路）。
    ///
    /// <para><b>武器排在前面</b>是有意的：玩家点开尸体第一眼看见的应该是那把家伙——它才是决定"值不值得动手"的东西。</para>
    ///
    /// <para><paramref name="held"/> 传 <c>null</c> ⇒ 只扒衣服（老口径，逐字节不变）。
    /// <b>调用方务必传 <c>WeaponLoadout.HeldWeapons</c> 而不是"左手+右手"</b>——双手握一把武器时左右手是<b>同一个</b>
    /// 实例，天真地各读一次会掉出两把重剑。</para>
    ///
    /// <para>返回空 ⇒ 这具尸体身上什么也没有（赤手空拳、衣不蔽体）。调用方据此<b>不把它登记成可搜刮点</b>——
    /// 一具光尸体不该在地图上留一个点了没反应的可交互点；尸潮之后满地尸体时，这也顺带压掉了一批容器。</para>
    /// </summary>
    public static IReadOnlyList<LootItem> Strip(
        IEnumerable<ArmorLayer> worn, IEnumerable<Weapon>? held = null)
    {
        var loot = new List<LootItem>();

        foreach (Weapon weapon in held ?? Enumerable.Empty<Weapon>())
        {
            if (IsSalvageable(weapon))
            {
                loot.Add(LootItem.Weapon(weapon.Name));
            }
        }

        foreach (ArmorLayer layer in worn ?? Enumerable.Empty<ArmorLayer>())
        {
            if (IsSalvageable(layer))
            {
                loot.Add(LootItem.Armor(layer.Name));
            }
        }

        // [T56] 🔴 用户拍板：「骨头获取途径：**尸体固定产出一个骨头**」——零掷骰、必掉、恒 1 个。
        //
        // **排在最后**：玩家点开尸体，第一眼该看见的仍是那把家伙（武器 → 衣服 → 骨头）。
        //
        // 【为什么这不会把尸潮变成一地假交互点】
        // 「Strip 返回空 ⇒ 不登记成可搜刮点」那道闸门（CorpseYard.SpawnFor）是**故意**的，为的是压掉
        // 「点了没反应」的假交互点。固定产骨之后 Strip **永不返回空** ⇒ 闸门对 actor 尸体不再触发。
        // 这**不是**倒退，因为：
        //   ① **闸门本来就很少触发**：丧尸穿的是生前的日常着装（ZombieOutfit），9 个日常预设里只有
        //      「衣不蔽体」（权重 0.15）是空的 ⇒ **85% 的丧尸尸体今天就已经是可搜刮点了**。
        //      固定产骨把它从 85% 抬到 100%（+17.6%），**不是**"从几个变成几十个"。
        //   ② **闸门的含义依然成立**：它挡的是「**没东西**的尸体」。一具带骨头的尸体，点开是**有东西**的
        //      ——它不是假点。闸门挡假点的职责没变，只是现在没有假点了。
        //
        // 【authored 剧情尸体永不产骨】结构性隔离，不靠自觉：骨头**只有这一条**进战利品的路，而本函数的产出
        // **只挂在尸体容器**上（容器名含中文 CorpseNaming.Marker「的尸体 #」，由 CorpseYard.SpawnFor /
        // CampMain.SpawnLevelCorpse 对**战死的 Actor** 生成）。斯图尔特庄园门口的吊尸、枯井底抱着婴儿的女尸
        // 是**叙事发现点**（id 一律 ascii narrative_），两个命名空间不可能相交 ⇒ 它们根本不是"可扒的尸体"，
        // 本函数碰不到它们。**从那家人身上扒骨头，观感上是不可接受的**——这条由测试钉死。
        // 材料键 "bone"（Materials.cs 登记为「骨头」）——与 Recipe.cs 的 Cost(("bone", N)) 同一个字面量口径。
        loot.Add(LootItem.Material("bone", 1));

        return loot;
    }
}
