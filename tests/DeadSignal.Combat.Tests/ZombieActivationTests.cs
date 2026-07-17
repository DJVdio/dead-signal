using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-T60·探索威胁模型] 探索期丧尸唤醒规则（<see cref="ZombieActivation"/>）+ 三关门→门后丧尸关联。
///
/// <para>用户口径（原话）：「普通丧尸根据视野、噪音、靠近这些唤醒;这几个房间里的丧尸作为一类特殊丧尸,
/// 有且仅有开门会唤醒,变为普通丧尸。」</para>
///
/// <para>
/// 🔴 这条把三关的门×丧尸谜题从「门宽/固定像素卡住丧尸」**重定义**为「一开门就激活原本门后休眠的丧尸」：
/// 两类丧尸——普通丧尸（视野/噪音/靠近唤醒）与门后特殊丧尸（冻结、仅其门被开才唤醒→转普通）。
/// 本文件把这条规则钉成红/绿断言（激活规则的纯逻辑 + 三关门→门后丧尸的拓扑关联）。
/// </para>
/// </summary>
public class ZombieActivationTests
{
    // ══════════════════════ 两类丧尸的唤醒规则（纯逻辑） ══════════════════════

    /// <summary>门后特殊丧尸在其门打开前**完全冻结**（普通丧尸从不冻结）。</summary>
    [Fact]
    public void OnlyDoorLockedZombies_AreFrozen_AndOnlyBeforeTheirDoorOpens()
    {
        Assert.True(ZombieActivation.IsFrozen(doorLocked: true, activated: false));   // 门后特殊·未激活 = 冻结
        Assert.False(ZombieActivation.IsFrozen(doorLocked: true, activated: true));   // 门开后 = 转普通，不再冻结
        Assert.False(ZombieActivation.IsFrozen(doorLocked: false, activated: false)); // 普通丧尸从不冻结
    }

    /// <summary>
    /// 🔴 冻结的门后特殊丧尸**对噪音/视野/靠近全免疫**（有且仅有开门唤醒）；普通丧尸/已激活/营地丧尸照常响应。
    /// </summary>
    [Theory]
    // explorationMode, doorLocked, activated → 期望（响应噪音 / 参与感知）
    [InlineData(true, true, false, false)]  // 探索·门后特殊·未激活 = 冻结 ⇒ 免疫
    [InlineData(true, true, true, true)]    // 探索·门后特殊·已激活（门开了）= 转普通 ⇒ 响应
    [InlineData(true, false, false, true)]  // 探索·普通丧尸 ⇒ 视野/噪音/靠近唤醒
    [InlineData(false, false, false, true)] // 营地丧尸（非探索）⇒ 原昼夜行为，照常响应（本轴不接管）
    public void FrozenSpecialZombies_AreImmuneToNoiseAndPerception_EveryoneElseResponds(
        bool exploration, bool doorLocked, bool activated, bool expectResponds)
    {
        Assert.Equal(expectResponds, ZombieActivation.RespondsToNoise(exploration, doorLocked, activated));
        Assert.Equal(expectResponds, ZombieActivation.RespondsToPerception(exploration, doorLocked, activated));
    }

    /// <summary>开对了门才唤醒：门后特殊丧尸只认自己那扇（组）门；开别的门不唤醒；已激活的不重复。</summary>
    [Fact]
    public void OpeningTheRightDoor_Activates_OpeningAnotherDoesNot()
    {
        var wake = new[] { "后院门", "北耳门" };
        Assert.True(ZombieActivation.DoorOpenActivates(wake, "后院门", activated: false));   // 开对门 ⇒ 唤醒
        Assert.True(ZombieActivation.DoorOpenActivates(wake, "北耳门", activated: false));   // 组内任一门都算
        Assert.False(ZombieActivation.DoorOpenActivates(wake, "正门", activated: false));    // 开无关的门 ⇒ 不唤醒
        Assert.False(ZombieActivation.DoorOpenActivates(wake, "后院门", activated: true));   // 已激活 ⇒ 不重复
    }

    // ══════════════════════ 破败教堂：墓地一群锁在后院门/北耳门后 ══════════════════════

    /// <summary>
    /// 🔴 教堂"吓一跳"重定义：墓地那 12 只是**门后特殊丧尸**，锁在墓地边界的两扇门后
    /// （<see cref="RuinedChurch.BackyardDoor"/> / <see cref="RuinedChurch.NorthSideDoor"/>）——推开其一，整片墓地醒来涌向你。
    /// 教堂本体那 3 只是**普通丧尸**（视野/靠近唤醒，不锁门）。
    /// </summary>
    [Fact]
    public void Church_GraveyardHordeIsDoorLocked_ChurchBodyIsNormal()
    {
        // 墓地的唤醒门 = 墓地边界那两扇（且都是真门、非关不上的洞）
        Assert.Equal(
            RuinedChurch.GraveyardDoorways().Where(d => d.DoorName != null).Select(d => d.DoorName!)
                .OrderBy(x => x).ToList(),
            RuinedChurch.GraveyardWakeDoors.OrderBy(x => x).ToList());
        Assert.Contains(RuinedChurch.BackyardDoor, RuinedChurch.GraveyardWakeDoors);
        Assert.Contains(RuinedChurch.NorthSideDoor, RuinedChurch.GraveyardWakeDoors);

        // 推开后院门 ⇒ 墓地那一群（尚未激活）全部唤醒
        Assert.True(ZombieActivation.DoorOpenActivates(RuinedChurch.GraveyardWakeDoors, RuinedChurch.BackyardDoor, activated: false));
        // 「大量」仍在（门后这一群不能是三五只）
        Assert.True(RuinedChurch.GraveyardZombieSpots.Count >= 10);
        // 教堂本体稀、且是普通丧尸（不锁门）
        Assert.True(RuinedChurch.ChurchZombieSpots.Count <= 4);
    }

    // ══════════════════════ 难民营地：每只伏击丧尸锁在自己那间房的门后 ══════════════════════

    /// <summary>
    /// 🔴 难民营地重定义：**去掉"门宽 48 < 52 物理卡住丧尸"那条依赖**（正是用户不满的"门卡住丧尸"）——
    /// 改成：每只伏击丧尸是**门后特殊丧尸**，锁在自己那间房的门后，推开那扇房门才唤醒它。
    /// 一间房一只、一门唤醒一只（拓扑一一对应）；过道那 4 只是普通丧尸。
    /// </summary>
    [Fact]
    public void Refugee_EachAmbushZombieIsLockedBehindItsOwnRoomDoor()
    {
        foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
        {
            string door = RefugeeCamp.WakeDoorFor(a);
            // 该伏击丧尸的唤醒门 = 它那间房的门
            Assert.Equal(RefugeeCamp.DoorNameOf(a.RoomNumber), door);
            // 推开这扇房门 ⇒ 唤醒它
            Assert.True(ZombieActivation.DoorOpenActivates(new[] { door }, door, activated: false));
            // 推开别人的房门 ⇒ 不唤醒它（一门唤醒一只）
            int other = a.RoomNumber == 1 ? 2 : 1;
            Assert.False(ZombieActivation.DoorOpenActivates(new[] { door }, RefugeeCamp.DoorNameOf(other), activated: false));
        }
        // 每扇门至多唤醒一只（房号互不相同）
        Assert.Equal(
            RefugeeCamp.AmbushZombies.Count,
            RefugeeCamp.AmbushZombies.Select(a => RefugeeCamp.WakeDoorFor(a)).Distinct().Count());
    }

    // ══════════════════════ 警察局：拘留区那只锁在拘留区铁门后 ══════════════════════

    /// <summary>
    /// 🔴 警察局：唯一有门的房是**拘留区**（锁死的拘留区铁门）——守着两件甲的那只是门后特殊丧尸，撬开铁门才唤醒。
    /// 另 3 间是无门开放侧房，其丧尸是普通丧尸（靠近/视野唤醒）。
    /// </summary>
    [Fact]
    public void Police_HoldingCellZombieIsLockedBehindTheIronGate_OthersAreNormal()
    {
        var behindGate = ExplorationWalls.PoliceZombieSpots
            .Where(s => ExplorationWalls.PoliceSpotBehindHoldingDoor(s)).ToList();
        Assert.Single(behindGate);                       // 有且仅有拘留区那只锁在门后
        Assert.Equal((1960f, 240f), behindGate[0]);      // 守着两件甲的那只（Phase2 放大 2800×1900 后的拘留区远 NE 角）

        Assert.True(ZombieActivation.DoorOpenActivates(
            new[] { ExplorationWalls.PoliceHoldingDoorName }, ExplorationWalls.PoliceHoldingDoorName, activated: false));
    }
}
