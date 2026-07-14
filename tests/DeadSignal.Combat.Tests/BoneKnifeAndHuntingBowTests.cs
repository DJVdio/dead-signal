using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T56] 用户拍板的两件事：① 狩猎弓压伤害到 3~9；② 骨刀「保留双持、大幅削弱」并真正能拿起来；
/// ③ 尸体固定产出 1 个骨头（authored 剧情尸体永不产骨）。
/// <para>
/// DPS 口径（与数值表一致）：<c>单持 = (min+max)/2 / 冷却</c>；<c>双持 = 单持 × 1.4</c>
/// （匕首 2.3529 → 3.2941 即此系数）。本文件只钉**相对关系**，不钉绝对数字——
/// 绝对值是「拟定待调」，相对关系才是用户拍板的那条规则。
/// </para>
/// </summary>
public sealed class BoneKnifeAndHuntingBowTests
{
    private const double DualWieldDpsFactor = 1.4;

    private static double Dps(Weapon w) => (w.DamageMin + w.DamageMax) / 2.0 / w.AttackInterval;

    private static double DualDps(Weapon w) => Dps(w) * DualWieldDpsFactor;

    // ───────────────────────── ① 狩猎弓：压伤害到 3~9 ─────────────────────────

    /// <summary>
    /// 用户拍板：狩猎弓伤害 4~12 → <b>3~9</b>，冷却保持 1.6s（「全表最快的弓」这个人设完整保留）
    /// ⇒ DPS 5.00 → <b>3.75</b>。
    /// </summary>
    [Fact]
    public void 狩猎弓_伤害压到3到9_冷却保持1点6秒()
    {
        Weapon bow = WeaponTable.HuntingBow();

        Assert.Equal(3, bow.DamageMin);
        Assert.Equal(9, bow.DamageMax);
        Assert.Equal(1.6, bow.AttackInterval, 3);
        Assert.Equal(3.75, Dps(bow), 3);
    }

    /// <summary>
    /// 「快」是狩猎弓的<b>唯一</b>人设 ⇒ 它必须是全表出手最快的弓弩。
    /// （这条是人设护栏：谁把别的弓调得比它还快，这里当场红。）
    /// </summary>
    [Fact]
    public void 狩猎弓_是全表最快的弓弩()
    {
        Weapon bow = WeaponTable.HuntingBow();

        foreach (Weapon other in WeaponTable.ArcheryArsenal().Where(w => w.Name != bow.Name))
        {
            Assert.True(
                bow.AttackInterval < other.AttackInterval,
                $"狩猎弓（{bow.AttackInterval}s）必须比「{other.Name}」（{other.AttackInterval}s）出手快");
        }
    }

    /// <summary>
    /// 「快」要<b>付代价</b>：狩猎弓换来的是全表最差的射程/穿透/散布之一。
    /// 用户的设计意图原话——「竞技复合弓和狩猎弓是同级别武器，区别是竞技复合弓远而准，狩猎弓快」。
    /// </summary>
    [Fact]
    public void 狩猎弓_快的代价是近而糙()
    {
        Weapon hunting = WeaponTable.HuntingBow();

        // 它是「近」的那一把：射程短、衰减早。
        Assert.True(hunting.MaxRange <= 200, $"狩猎弓射程应该很短，实际 {hunting.MaxRange}");
        // 它是「糙」的那一把：散布大、穿透低。
        Assert.True(hunting.BaseSpreadDegrees >= 6, $"狩猎弓应该很糙，实际散布 {hunting.BaseSpreadDegrees}°");
        Assert.True(hunting.Penetration <= 0.35, $"狩猎弓穿透应该很低，实际 {hunting.Penetration}");
    }

    /// <summary>
    /// 🔴 <b>箭是全乘算修正</b> ⇒ 改弓的基础伤害<b>不会</b>打乱任何箭的相对关系。
    /// 这条把「箭对弓的修正是乘法」钉死：压了狩猎弓的基础伤害之后，四种箭在它身上的
    /// <b>相对排序</b>与在别的弓上完全一致。
    /// </summary>
    [Fact]
    public void 压狩猎弓伤害_不改变四种箭的相对关系()
    {
        Weapon hunting = WeaponTable.HuntingBow();
        Weapon longbow = WeaponTable.Longbow();

        List<string> RankArrowsOn(Weapon bow) => ArrowTable.All
            .Select(a => (Name: a.Name, Dmg: Archery.Combine(bow, a).DamageMax))
            .OrderByDescending(x => x.Dmg)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Name)
            .ToList();

        Assert.Equal(RankArrowsOn(longbow), RankArrowsOn(hunting));
    }

    // ───────────────────────── ② 骨刀：救活它 + 大幅削弱 ─────────────────────────

    /// <summary>
    /// 🔴 <b>这条是这次改动的理由本身</b>：骨刀早有配方/图标/风味文案，但 <see cref="WeaponTable.Arsenal"/>
    /// 里<b>没有</b>它 ⇒ <c>ModdedWeaponRegistry.WeaponByName("骨刀")</c> 查不到 ⇒ <c>Pawn.EquipWeapon</c> 直接
    /// <c>return false</c> ⇒ <b>玩家花材料造出来的骨刀，永远拿不起来</b>（不报错、不崩溃，只是静默拿不动）。
    /// </summary>
    [Fact]
    public void 骨刀_必须在Arsenal里_否则造出来也拿不起来()
    {
        Assert.Contains(WeaponTable.Arsenal(), w => w.Name == "骨刀");
    }

    /// <summary>
    /// 🔴 <b>新武器一律追加末尾，不插队</b>（CLAUDE.md 铁律）：Sim 按 <c>idx</c> 派生随机种子，
    /// 插队会打乱其后所有武器的随机流 ⇒ 既有基线当场漂移。
    /// </summary>
    [Fact]
    public void 骨刀_追加在Arsenal末尾_不插队()
    {
        Assert.Equal("骨刀", WeaponTable.Arsenal()[^1].Name);
    }

    /// <summary>用户拍板：<b>保留双持</b>。</summary>
    [Fact]
    public void 骨刀_保留双持()
    {
        Assert.True(WeaponTable.BoneKnife().CanDualWield);
    }

    /// <summary>
    /// 用户拍板「<b>大幅削弱</b>」的落点：骨刀＝<b>应急武器</b>（单持 1.50 / 双持 2.10，均 −27%）。
    /// 它的材料是<b>尸体固定产骨＝近乎无限</b>、<b>第一本书就解锁</b>、<b>不需要工作台</b> ⇒ 若它能打，
    /// 「去找一把真武器」这条循环开局即被绕过。
    /// </summary>
    [Fact]
    public void 骨刀_大幅削弱到应急武器档()
    {
        Weapon bone = WeaponTable.BoneKnife();

        Assert.Equal(1, bone.DamageMin);
        Assert.Equal(5, bone.DamageMax);
        Assert.Equal(2.0, bone.AttackInterval, 3);
        Assert.Equal(1.50, Dps(bone), 3);      // 削弱前 2.0588
        Assert.Equal(2.10, DualDps(bone), 3);  // 削弱前 2.8824（＞ 长剑 2.81！）
    }

    /// <summary>
    /// 🔴 <b>双持是骨刀的特色，但不能是它的免死金牌</b>：削弱前双持 DPS <b>2.88 ＞ 长剑 2.81 ＞ 消防斧</b>
    /// ——一把无限材料的应急武器压过了全部真近战武器。
    /// <para>
    /// 护栏：<b>双持骨刀 ＜ 单持匕首</b>（拿两把骨刀，天花板也就够不着<b>一把</b>匕首）。
    /// </para>
    /// <para>
    /// ⚠ <b>为什么护栏钉在匕首而不是拳脚/棍棒</b>：匕首（1~7 / 1.7s ⇒ 2.3529）在**代码与数值表两代里
    /// 完全一致**，是眼下唯一稳定的锚点；而拳脚（冷却 1.2→1.4）与棍棒（上限 7→8）正处在用户手改的
    /// **迁移中途**（<c>impl-unarmed</c> / <c>review-user-mods</c> 在做），拿它们做护栏会在迁移窗口里假红。
    /// 骨刀 vs 拳脚 的目标关系见 <see cref="WeaponTable.BoneKnife"/> 注释。
    /// </para>
    /// </summary>
    [Fact]
    public void 骨刀_双持天花板够不着一把匕首()
    {
        double boneDual = DualDps(WeaponTable.BoneKnife());
        double dagger = Dps(WeaponTable.Dagger());

        Assert.True(
            boneDual < dagger,
            $"双持骨刀({boneDual:F4})够到了单持匕首({dagger:F4})——否则「去找一把真武器」这条循环就废了");
    }

    /// <summary>
    /// 骨刀必须是<b>全表最弱的近战武器</b>（按单持 DPS）——它是应急武器，不是真武器。
    /// 这条不写死数字，拿 <see cref="WeaponTable.Arsenal"/> 里的真近战武器<b>实时比</b>。
    /// </summary>
    [Fact]
    public void 骨刀_是全表最弱的近战武器()
    {
        double bone = Dps(WeaponTable.BoneKnife());

        IEnumerable<Weapon> realMelee = WeaponTable.Arsenal()
            .Where(w => !w.IsRanged && w.Name != "骨刀");

        foreach (Weapon w in realMelee)
        {
            Assert.True(
                bone < Dps(w),
                $"骨刀({bone:F4})不该强过真武器「{w.Name}」({Dps(w):F4})——它是应急武器");
        }
    }

    /// <summary>
    /// ⚠️ 用户<b>没填重量</b>这一格。留空会落到 <c>ItemWeights.DefaultWeaponKg = 2.0</c>
    /// ⇒ 一把骨刀重 2kg（比棍棒 1.5kg 还沉），荒谬。
    /// 🔴 重量的真源在 <c>ItemWeights._weaponKg</c>，不在 <see cref="WeaponTable"/>（<c>Weapon</c> 记录里没有重量字段）。
    /// </summary>
    [Fact]
    public void 骨刀_有登记重量_且比匕首轻()
    {
        double bone = ItemWeights.WeaponKg("骨刀");
        double dagger = ItemWeights.WeaponKg("匕首");

        Assert.Equal(0.4, bone, 3);
        Assert.True(bone < dagger, $"骨刀({bone}kg)应比匕首({dagger}kg)轻——它就是一片削出来的骨头");
        Assert.NotEqual(ItemWeights.DefaultWeaponKg, bone);
    }

    // ───────────────────────── ③ 尸体固定产出 1 个骨头 ─────────────────────────

    /// <summary>用户拍板：「骨头获取途径：<b>尸体固定产出一个骨头</b>」——零掷骰，必掉，且只掉 1 个。</summary>
    [Fact]
    public void 尸体_固定产出一个骨头()
    {
        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(ZombieOutfit.ArmorOf("寻常打扮"));

        Assert.Equal(1, loot.Count(l => l.Kind == LootKind.Material && l.RefId == "bone"));
        Assert.Equal(1, loot.Single(l => l.RefId == "bone").Quantity);
    }

    /// <summary>
    /// 🔴 连<b>衣不蔽体</b>（全表唯一一个空着装预设）的丧尸也产骨 ⇒ <see cref="CorpseLoot.Strip"/> 从此
    /// <b>永不返回空</b>。这正是「固定产出」的字面意思，也顺带让「空 Strip ⇒ 不登记可搜刮点」那道闸门
    /// 对 actor 尸体<b>不再触发</b>——但闸门本身的含义（没东西的尸体不该留一个点了没反应的假交互点）
    /// <b>依然成立</b>：一具带骨头的尸体，点开是<b>有东西</b>的，它不是假点。
    /// </summary>
    [Fact]
    public void 光尸体也产骨_Strip从此不返回空()
    {
        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(ZombieOutfit.ArmorOf("衣不蔽体"));

        Assert.Single(loot);
        Assert.Equal("bone", loot[0].RefId);
    }

    /// <summary>骨头排在<b>最后</b>：玩家点开尸体第一眼该看见的是那把家伙（武器 → 衣服 → 骨头）。</summary>
    [Fact]
    public void 骨头排在战利品最后_武器仍在最前()
    {
        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(
            ArmorTable.SurvivorArmor(), new[] { WeaponTable.Dagger() });

        Assert.Equal(LootKind.Weapon, loot[0].Kind);
        Assert.Equal("bone", loot[^1].RefId);
    }

    /// <summary>
    /// 🔴🔴 <b>authored 剧情尸体绝不产骨</b>。斯图尔特庄园有门口的<b>吊尸</b>和枯井底<b>抱着婴儿的女尸</b>——
    /// <b>从这家人身上扒骨头，观感上是不可接受的。</b>
    /// <para>
    /// <b>为什么它是结构性安全的</b>（<c>impl-level-corpse</c> 建立的隔离，本条钉死）：骨头<b>只有一条</b>
    /// 进入战利品的路——<see cref="CorpseLoot.Strip"/>。而 Strip 的产出<b>只挂在尸体容器</b>上
    /// （容器名含中文 <c>CorpseNaming.Marker</c>「的尸体 #」，由 <c>CorpseYard.SpawnFor</c> /
    /// <c>CampMain.SpawnLevelCorpse</c> 对<b>战死的 Actor</b> 生成）。authored 剧情尸体是
    /// <b>叙事发现点</b>，id 一律 ascii <c>narrative_</c> ⇒ <b>两个命名空间不可能相交</b>
    /// ⇒ 它们根本不是"可扒的尸体"，Strip 碰不到它们，骨头自然也落不到它们身上。
    /// </para>
    /// <para>谁哪天把 authored 尸体接进尸体容器（想让它"能搜"），这条当场红。</para>
    /// </summary>
    [Fact]
    public void authored剧情尸体_不在可扒尸体的命名空间里_故永不产骨()
    {
        string[] authoredCorpseSpots =
        {
            StuartManor.GateHangedSpotId,   // 门口的吊尸
            StuartManor.DryWellSpotId,      // 枯井底抱着婴儿的女尸
            StuartManor.InnerRoomSpotId,
            StuartManor.TakenInSpotId,
        };

        foreach (string id in authoredCorpseSpots)
        {
            Assert.False(
                CorpseNaming.IsCorpseContainer(id),
                $"authored 剧情尸体「{id}」落进了可扒尸体的命名空间 ⇒ 它会被 Strip 扒出骨头。不可接受。");
        }

        // 正对照：程序化尸体<b>就是</b>可扒的，骨头正该从这儿出。
        Assert.True(CorpseNaming.IsCorpseContainer(CorpseNaming.ContainerName("劫掠者", 1)));
    }
}
