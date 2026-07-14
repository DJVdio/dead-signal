using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「强制双手」的**门槛效果**（T12）。既有 <see cref="TwoHandedGripTests"/> 覆盖的是持握态的**攻速系数**；
/// 本文件覆盖的是 <see cref="Weapon.TwoHanded"/> 作为**装备门槛**的强制力——
/// 双手武器不能与另一件手持物（另一把武器 / 手持光源）并存。
/// </summary>
public class TwoHandEnforcementTests
{
    private static Weapon OneHand() => new()
    {
        Name = "匕首", DamageMin = 3, DamageMax = 7, DamageType = DamageType.Sharp,
        TwoHanded = false, CanDualWield = true, AttackInterval = 0.5,
    };

    private static Weapon TwoHand() => new()
    {
        Name = "步枪", DamageMin = 20, DamageMax = 35, DamageType = DamageType.Sharp,
        TwoHanded = true, CanDualWield = false, AttackInterval = 2.8, IsRanged = true,
    };

    private static LightProfile Torch => LightSource.Find(LightSource.TorchKey)!.Value;

    // ---------------- 全表持握标注（24 把：9 近战 + 7 枪 + 8 弓弩） ----------------
    // wiki「强制双手」列直接取 Weapon.TwoHanded；本表是它的权威快照，改数据必先改这里。

    public static TheoryData<string, bool> GripSnapshot() => new()
    {
        // 近战 9
        { "匕首", false }, { "短剑", false }, { "刺剑", false },
        { "长剑", true }, { "重剑", true }, { "草叉", true },
        { "棍棒", false }, { "尖头锤", false }, { "破甲锤", true },
        // 枪 7
        { "自制猎枪", true }, { "手枪", false }, { "冲锋枪", true }, { "步枪", true },
        { "栓动猎枪", true }, { "狙击枪", true }, { "自制霰弹枪", true },
        // 弓弩 8
        { "短弓", true }, { "反曲弓", true }, { "长弓", true },
        { "竞技复合弓", true }, { "狩猎弓", true },
        { "单手轻弩", false }, { "双手重弩", true }, { "复合弩", true },
    };

    [Theory]
    [MemberData(nameof(GripSnapshot))]
    public void Arsenal_TwoHandedFlag_MatchesAuthoredSnapshot(string name, bool twoHanded)
    {
        Weapon w = WeaponTable.Arsenal().Single(x => x.Name == name);
        Assert.Equal(twoHanded, w.TwoHanded);
    }

    [Fact]
    public void Arsenal_SnapshotCoversEveryWeapon()
    {
        // 新增武器必须在上表登记持握，否则本测试红——挡住"加了武器忘标双手"。
        IEnumerable<string> snapshot = GripSnapshot().Select(row => (string)row[0]!);
        Assert.Equal(
            WeaponTable.Arsenal().Select(w => w.Name).OrderBy(n => n),
            snapshot.OrderBy(n => n));
    }

    /// <summary>
    /// 全表不变式（**用户拍板**「保双手，放弃双持」后的硬口径）：**强制双手 ⇒ 不可双持**，一把例外都没有。
    /// <para>
    /// 二者在 <see cref="WeaponLoadout.EquipToHand"/> 里物理互斥——双手武器**直接短路**走 EquipTwoHanded（占满两手），
    /// 永远进不了双持分支 ⇒ 双手武器身上的 <see cref="Weapon.CanDualWield"/> **永远读不到**，是个**骗人的死标记**。
    /// 冲锋枪曾挂着这么一行（"用户拍板：放开可双持"），从未生效，已按用户裁决删除。
    /// </para>
    /// 本护栏就是为了**把这个坑永久堵上**：将来谁再加一把"双手又可双持"的武器，这里立刻红，
    /// 而不是让那个标记继续躺在表里骗下一个人。
    /// </summary>
    [Fact]
    public void TwoHandedWeapons_AreNeverDualWieldable()
    {
        string[] contradictory = WeaponTable.Arsenal()
            .Concat(WeaponTable.ArcheryArsenal())   // 弓弩虽已含在 Arsenal 内，仍显式并入：将来任一表扩表都跑不掉
            .Where(w => w is { TwoHanded: true, CanDualWield: true })
            .Select(w => w.Name)
            .Distinct()
            .ToArray();

        Assert.Empty(contradictory);
    }

    // ---------------- 门槛：双手武器 vs 另一把武器 ----------------

    [Fact]
    public void TwoHandedWeapon_CannotCoexistWithOffhandWeapon()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(OneHand(), Hand.Left));   // 左手匕首
        Assert.True(lo.EquipToHand(TwoHand(), Hand.Right));  // 装步枪 → 强制占两手
        Assert.Equal(GripMode.TwoHanded, lo.Grip);
        Assert.Same(lo.LeftHand, lo.RightHand);              // 两手同一把
        Assert.Equal("步枪", lo.LeftHand!.Name);              // 匕首已被挤掉，不可能左匕首右步枪
    }

    [Fact]
    public void TwoHandedWeapons_CannotBeDualWielded()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(TwoHand(), Hand.Left));
        Assert.True(lo.EquipToHand(TwoHand(), Hand.Right));  // 第二把只是顶替，不形成双持
        Assert.Equal(GripMode.TwoHanded, lo.Grip);           // 永远不会是 DualWield
    }

    // ---------------- 门槛：双手武器 vs 手持光源（本次补的洞） ----------------

    [Fact]
    public void HoldingLight_BlocksTwoHandedWeapon()
    {
        var lo = new WeaponLoadout();
        var light = new HeldLightState();
        Assert.True(light.TryHold(Torch, Hand.Left, lo));    // 左手火把

        // 反向门槛：正持光 → 双手武器一律挡（两只手都挡，无处可去）。
        Assert.True(HeldLightState.BlocksWeaponEquip(light, TwoHand(), Hand.Right));
        Assert.True(HeldLightState.BlocksWeaponEquip(light, TwoHand(), Hand.Left));
    }

    [Fact]
    public void HoldingLight_BlocksOneHandWeapon_OnlyInTheLitHand()
    {
        var lo = new WeaponLoadout();
        var light = new HeldLightState();
        Assert.True(light.TryHold(Torch, Hand.Left, lo));    // 左手火把

        // 持光那只手不能再塞武器（旧洞：WeaponLoadout 看不见光源，会让一只手同时握火把和匕首）。
        Assert.True(HeldLightState.BlocksWeaponEquip(light, OneHand(), Hand.Left));
        // 另一只空手可以拿单手武器——「一手火把 + 一手单手武器」是允许的既有玩法。
        Assert.False(HeldLightState.BlocksWeaponEquip(light, OneHand(), Hand.Right));
    }

    [Fact]
    public void NoLight_BlocksNothing()
    {
        Assert.False(HeldLightState.BlocksWeaponEquip(null, TwoHand(), Hand.Right));
        Assert.False(HeldLightState.BlocksWeaponEquip(new HeldLightState(), TwoHand(), Hand.Right));
    }

    [Fact]
    public void BlocksWeaponEquip_AgreesWithBlocksTwoHandedEquip()
    {
        var lo = new WeaponLoadout();
        var light = new HeldLightState();
        light.TryHold(Torch, Hand.Right, lo);

        // 两个门槛对双手武器必须给同一个答案（后者是前者的特例，保留是为既有调用方）。
        Assert.Equal(
            HeldLightState.BlocksTwoHandedEquip(light),
            HeldLightState.BlocksWeaponEquip(light, TwoHand(), Hand.Left));
    }
}
