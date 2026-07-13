using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>玩家侧右键菜单：对着一扇门/一段围栏，你能干什么</b>（用户拍板原话：
/// 「玩家也可以控制角色，右键点击敌营门时选择<b>撬锁/破坏</b>，点击围栏时<b>静默拆除/破坏</b>」）。
///
/// <para>
/// <b>这让"潜入敌营"闭环了</b>：摸到金手指帮据点 → <b>撬锁</b>（安静、慢，得算准哨兵背对的窗口）
/// / <b>静默拆围栏</b>（侧面开洞，绕开正门）/ <b>破坏</b>（快，但整个据点瞬间警觉，然后他们包抄你）。
/// 每一条都是同一个取舍的另一张面孔：<b>安静但慢 vs 快但很响</b>。
/// </para>
///
/// <para>
/// <b>⚠️ 本类的核心判断：菜单只在"真有得选"时才存在。</b>
/// 一扇没锁的门只有"推开"这一个动作（用户拍板「开门只有一种动作，不分轻推和踹开」）——
/// 给它弹一个只有一项的菜单，是在拿仪式感惩罚玩家。<see cref="SiteActions.NeedsMenu"/> 把这条钉死：
/// <b>≥2 个可选动作才弹菜单，否则右键 = 直接干那一件事</b>（保持既有的一击直达手感）。
/// </para>
/// </summary>
public class SiteActionsTests
{
    private static SiteActionOption? Find(System.Collections.Generic.IReadOnlyList<SiteActionOption> opts, SiteAction a)
        => opts.FirstOrDefault(o => o.Action == a) is { Action: var x } o && x == a ? o : null;

    private static bool Has(System.Collections.Generic.IReadOnlyList<SiteActionOption> opts, SiteAction a)
        => opts.Any(o => o.Action == a);

    // ---------------- 敌营的门：撬锁 / 破坏 ----------------

    [Fact]
    public void 锁着的门_右键给出撬锁和破坏两个选项()
    {
        // 用户原话：「右键点击敌营门时选择**撬锁/破坏**」。这是本单最核心的一条。
        var opts = SiteActions.ForDoor(
            DoorState.Locked, LockTier.Standard, Faction.Survivor, isAnimal: false, lockpickCount: 3);

        Assert.True(Has(opts, SiteAction.PickLock), "锁着的门必须能撬");
        Assert.True(Has(opts, SiteAction.Bash), "锁着的门必须能砸");
        Assert.True(SiteActions.NeedsMenu(opts), "两个动作 ⇒ 真有得选 ⇒ 该弹菜单");
    }

    [Fact]
    public void 撬锁是安静的_破坏是很响的_取舍要写在选项上()
    {
        // 菜单必须**把代价写在脸上**——玩家在按下之前就该知道自己在选什么。
        var opts = SiteActions.ForDoor(
            DoorState.Locked, LockTier.Sturdy, Faction.Survivor, isAnimal: false, lockpickCount: 3);

        SiteActionOption pick = opts.Single(o => o.Action == SiteAction.PickLock);
        SiteActionOption bash = opts.Single(o => o.Action == SiteAction.Bash);

        // 噪音是规则，不是文案：撬锁必须比丧尸嗅觉安静，破坏必须远超它。
        Assert.True(SiteActions.NoiseOf(SiteAction.PickLock) < NoiseLogic.ZombieSmellRadius);
        Assert.True(SiteActions.NoiseOf(SiteAction.Bash) > NoiseLogic.ZombieSmellRadius);
        Assert.True(SiteActions.NoiseOf(SiteAction.PickLock) < SiteActions.NoiseOf(SiteAction.Bash));

        // 提示文案里得能看出"安静/响"和"慢/快"（否则玩家是在盲选）
        Assert.False(string.IsNullOrWhiteSpace(pick.Hint));
        Assert.False(string.IsNullOrWhiteSpace(bash.Hint));
    }

    [Fact]
    public void 没有铁丝_撬锁项仍然列出来_但是灰的_并且说明为什么()
    {
        // **不能直接把选项藏掉**：玩家会以为"这门撬不了"，而真相是"你没带铁丝"。
        // 列出来 + 灰掉 + 写明原因 = 教会他下次带铁丝。
        var opts = SiteActions.ForDoor(
            DoorState.Locked, LockTier.Simple, Faction.Survivor, isAnimal: false, lockpickCount: 0);

        SiteActionOption pick = opts.Single(o => o.Action == SiteAction.PickLock);
        Assert.False(pick.Enabled, "没铁丝 ⇒ 撬锁不可用");
        Assert.Contains("铁丝", pick.DisabledReason);

        // 但砸门永远可用（不需要任何工具）——玩家绝不会被一扇门永久卡死。
        Assert.True(opts.Single(o => o.Action == SiteAction.Bash).Enabled);
    }

    [Fact]
    public void 有铁丝_撬锁可用()
    {
        var opts = SiteActions.ForDoor(
            DoorState.Locked, LockTier.Simple, Faction.Survivor, isAnimal: false, lockpickCount: 1);
        Assert.True(opts.Single(o => o.Action == SiteAction.PickLock).Enabled);
    }

    // ---------------- 没得选的时候，就别弹菜单 ----------------

    [Fact]
    public void 关着没锁的门_只有推开这一个动作_不弹菜单()
    {
        // 用户拍板：「**开门只有一种动作**，不分轻推和踹开」。
        // 一扇没锁的门，右键就该**直接推开**——弹一个只有一项的菜单是在拿仪式感惩罚玩家。
        var opts = SiteActions.ForDoor(
            DoorState.Closed, LockTier.None, Faction.Survivor, isAnimal: false, lockpickCount: 5);

        Assert.True(Has(opts, SiteAction.OpenDoor));
        Assert.False(Has(opts, SiteAction.PickLock), "没上锁的门没什么可撬的");
        Assert.False(SiteActions.NeedsMenu(opts), "只有一个可用动作 ⇒ 直接干，别弹菜单");
    }

    [Fact]
    public void 推得开的门_不提供破坏选项_那是被严格支配的选项()
    {
        // 【这一条是设计，不是省事】
        // 砸一扇你**本来就推得开**的门 —— 开门更快、更安静（100 < 180）、还不毁掉门 ——
        // **没有任何人会选它**。把它摆在菜单上，只会让每一次开门都变成一次无意义的二选一。
        // **破坏是"打不开时的暴力解"，不是并列项。**
        var closed = SiteActions.ForDoor(DoorState.Closed, LockTier.None, Faction.Survivor, false, 0);
        Assert.False(Has(closed, SiteAction.Bash), "推得开的门，不该提供'破坏'");

        var barred = SiteActions.ForDoor(DoorState.Barred, LockTier.None, Faction.Survivor, false, 0);
        Assert.False(Has(barred, SiteAction.Bash), "自家闩着的门，自己人抬闩就行，砸它干什么");

        // 反过来：**打不开的门，破坏必须在**（否则没铁丝的玩家会被一扇门永久卡死）。
        var locked = SiteActions.ForDoor(DoorState.Locked, LockTier.Sturdy, Faction.Survivor, false, 0);
        Assert.True(Has(locked, SiteAction.Bash), "锁着又没铁丝 ⇒ 砸是唯一出路，必须提供");
        Assert.True(locked.Single(o => o.Action == SiteAction.Bash).Enabled);
    }

    [Fact]
    public void 锁着但没带铁丝_仍然弹菜单_因为玩家得看见那条灰掉的撬锁()
    {
        // 只有 1 个**可用**动作（破坏），但仍要弹菜单 —— 因为玩家必须看见
        // 「撬锁 · 没有铁丝」那一条，否则他学不到"下次带铁丝"。
        // 这是 NeedsMenu 判据是「列出 ≥2 且 可用 ≥1」而不是「可用 ≥2」的唯一理由。
        var opts = SiteActions.ForDoor(DoorState.Locked, LockTier.Standard, Faction.Survivor, false, lockpickCount: 0);
        Assert.True(SiteActions.NeedsMenu(opts));
        Assert.False(opts.Single(o => o.Action == SiteAction.PickLock).Enabled);
        Assert.True(opts.Single(o => o.Action == SiteAction.Bash).Enabled);
    }

    [Fact]
    public void 开着的门_只有关上这一个动作_不弹菜单_而且砸不了()
    {
        var opts = SiteActions.ForDoor(
            DoorState.Open, LockTier.None, Faction.Survivor, isAnimal: false, lockpickCount: 5);

        Assert.True(Has(opts, SiteAction.CloseDoor));
        // 开着的门不挡路 ⇒ 没什么可砸的（DoorLogic.CanBash 恒等于 Blocks，这条铁律不能在菜单层被绕过）
        Assert.False(Has(opts, SiteAction.Bash), "敞着的门砸它干什么？");
        Assert.False(SiteActions.NeedsMenu(opts));
    }

    [Fact]
    public void 闩着的自家大门_自己人能抬闩_但撬不了_因为闩不是锁()
    {
        // 闩是**从里面插的横木**，不是锁芯——撬锁的手艺在这儿没有用武之地（DoorLogic 已钉死）。
        var opts = SiteActions.ForDoor(
            DoorState.Barred, LockTier.None, Faction.Survivor, isAnimal: false, lockpickCount: 9);

        Assert.True(Has(opts, SiteAction.OpenDoor), "自己人抬得起自家的闩");
        Assert.False(Has(opts, SiteAction.PickLock), "闩不是锁，撬不了");
    }

    // ---------------- 谁能操作 ----------------

    [Fact]
    public void 狗和丧尸_菜单是空的_它们什么都干不了()
    {
        // 狗没有手；丧尸只会砸（而砸是 AI 的破防路径，不走玩家菜单）。
        Assert.Empty(SiteActions.ForDoor(DoorState.Locked, LockTier.Simple, Faction.Survivor, isAnimal: true, 9));
        Assert.Empty(SiteActions.ForDoor(DoorState.Locked, LockTier.Simple, Faction.Zombie, isAnimal: false, 9));
        Assert.False(SiteActions.NeedsMenu(
            SiteActions.ForDoor(DoorState.Locked, LockTier.Simple, Faction.Zombie, isAnimal: false, 9)));
    }

    // ---------------- 对称性：玩家和 AI 用同一套数值（硬要求） ----------------

    [Fact]
    public void 玩家和AI用同一套噪音_菜单不许自己造一套数值()
    {
        // ⚠️ 主 agent 的硬要求：「玩家和 AI **用同一套耗时/噪音/判定**。别给玩家开后门。」
        // 故菜单层**一个数值都不许自己定**——全部转发到 NoiseLogic / DoorLogic 的单一真源。
        Assert.Equal(NoiseLogic.LockpickNoiseRadius, SiteActions.NoiseOf(SiteAction.PickLock));
        Assert.Equal(NoiseLogic.BreachNoiseRadius, SiteActions.NoiseOf(SiteAction.Bash));   // 玩家砸门 = AI 砸门，同一个 180
        Assert.Equal(NoiseLogic.DoorNoiseRadius, SiteActions.NoiseOf(SiteAction.OpenDoor)); // 玩家开门 = 劫掠者开门，同一个 100
    }

    // ---------------- 围栏：静默拆除 / 破坏 ----------------

    [Fact]
    public void 围栏_右键给出静默拆除和破坏两个选项()
    {
        // 用户原话：「点击围栏时**静默拆除/破坏**」。
        var opts = SiteActions.ForFence(Faction.Survivor, isAnimal: false, CampStructureKind.Fence, StructureTier.FenceBasic);

        Assert.True(Has(opts, SiteAction.SilentDismantle));
        Assert.True(Has(opts, SiteAction.Bash));
        Assert.True(SiteActions.NeedsMenu(opts), "两条真路子 ⇒ 该弹菜单");
    }

    [Fact]
    public void 静默拆除必须比丧尸嗅觉安静_否则它只是慢速版破坏()
    {
        // 这是"静默"二字存在的**全部理由**：若它能招来东西，那就是又慢又招人，没有任何人会选它。
        Assert.True(SiteActions.NoiseOf(SiteAction.SilentDismantle) < NoiseLogic.ZombieSmellRadius);
        Assert.True(SiteActions.NoiseOf(SiteAction.SilentDismantle) < SiteActions.NoiseOf(SiteAction.Bash));
    }

    [Fact]
    public void 静默拆除的耗时和噪音_与劫掠者用同一套_玩家没有后门()
    {
        // ⚠️ 对称性硬要求：**玩家和 AI 用同一套耗时/噪音/判定**。
        // impl-raider-ai 的 IntrusionLogic 是单一真源，菜单层**纯转发**，一个数都不自己定。
        // ⚠️ 直接对**通用机制层** SilentDismantleLogic 断言，**不绕道 IntrusionLogic**（那是劫掠者的**决策层**：
        // "我现在该撬、该拆、还是该砸" —— 那是 AI 的心思，玩家不该从它那里取数）。两边都从同一个通用机制层取，
        // 才是对称性的正确形状。
        Assert.Equal(SilentDismantleLogic.NoiseRadius, SiteActions.NoiseOf(SiteAction.SilentDismantle));
        foreach (StructureTier t in new[]
                 { StructureTier.FenceBasic, StructureTier.FenceReinforced, StructureTier.FenceSheetMetal })
        {
            Assert.Equal(SilentDismantleLogic.SecondsFor(t, SilentDismantleParams.Default),
                         SiteActions.DismantleSecondsFor(t));
        }
        // 而 AI 的决策层本身也转发同一处 ⇒ 玩家与劫掠者逐字相等（impl-raider-ai 那边也有一条对称性测试）。
        Assert.Equal(IntrusionLogic.QuietNoiseRadius, SiteActions.NoiseOf(SiteAction.SilentDismantle));
    }

    [Fact]
    public void 围栏越硬_静默拆得越久_升级围栏因此也更难被悄悄拆开()
    {
        // impl-raider-ai 的副产品：升级围栏不只更耐砸，也**更难被悄悄拆开**。玩家侧同享这条梯度。
        Assert.True(SiteActions.DismantleSecondsFor(StructureTier.FenceBasic)
                  < SiteActions.DismantleSecondsFor(StructureTier.FenceReinforced));
    }

    [Fact]
    public void 门不给静默拆除_它已经有撬和砸两条完整的路()
    {
        // impl-raider-ai 的口径（我同意）：`CanDismantle(Door, ...)` 恒 false。
        // 门的两条路（撬 30 / 砸 180）已经完整，再加一条"静默拆门"是**重复机制**——
        // 三个选项里有两个都是"安静地弄开这扇门"，玩家只会困惑该选哪个。
        var opts = SiteActions.ForFence(
            Faction.Survivor, isAnimal: false, CampStructureKind.Door, StructureTier.DoorWood);
        Assert.False(Has(opts, SiteAction.SilentDismantle), "门不该有'静默拆除'");
        Assert.True(Has(opts, SiteAction.Bash), "但砸总是可以的");
    }

    [Fact]
    public void 静默拆除不掷骰_所以菜单不显示成功率_只显示需要多少秒()
    {
        // 静默拆除**没有 IRandomSource**：花够时间就一定拆开。它的取舍是「时间 + 被撞见的风险」，**不是运气**。
        // 故文案写"需要 45 秒"而不是"70% 成功率"——后者是撒谎。
        // 这也正好与撬锁形成对照：**两条安静路子，一条赌运气（撬锁会断铁丝），一条赌时间（拆围栏必成但很慢）。**
        var fence = SiteActions.ForFence(
            Faction.Survivor, isAnimal: false, CampStructureKind.Fence, StructureTier.FenceBasic);
        SiteActionOption dis = fence.Single(o => o.Action == SiteAction.SilentDismantle);
        Assert.DoesNotContain("成功率", dis.Hint);
        Assert.DoesNotContain("%", dis.Hint);
        Assert.Contains("秒", dis.Hint);

        // 对照：撬锁**必须**显示成功率（它真的会失败）。
        var door = SiteActions.ForDoor(DoorState.Locked, LockTier.Standard, Faction.Survivor, false, 3);
        Assert.Contains("成功率", door.Single(o => o.Action == SiteAction.PickLock).Hint);
    }

    [Fact]
    public void 撬锁耗时也走单一真源_不许菜单层自己加速()
    {
        // 玩家撬锁的耗时 = DoorLogic.PickSeconds，一个字都不改（"别给玩家开后门：撬锁特别快"）。
        foreach (LockTier t in new[] { LockTier.Simple, LockTier.Standard, LockTier.Sturdy })
        {
            Assert.Equal(DoorLogic.PickSeconds(t), SiteActions.PickSecondsFor(t));
            Assert.Equal(DoorLogic.PickChance(t), SiteActions.PickChanceFor(t));
        }
    }
}
