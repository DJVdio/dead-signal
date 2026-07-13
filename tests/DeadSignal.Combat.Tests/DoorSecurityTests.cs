using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>自动闩门</b>（<see cref="DoorSecurityLogic"/>）与<b>门态徽标</b>（<see cref="DoorBadgeVisual"/>）。
///
/// <para>
/// <b>这里堵的洞</b>：门闩此前<b>纯靠玩家手动</b>（关门即闩）。玩家派三个人出去探索，营地大门就那么开着——
/// 夜里劫掠者散步进来把营地端了，而玩家在地图另一头搜刮，毫不知情。
/// <b>这不是"硬核"，是玩家无法预期的陷阱。</b>
/// </para>
///
/// <para>
/// <b>为什么规则挂在「相位」上而不是「每一帧」</b>：如果"营里有人 → 门自动闩上"是一条**持续**规则，
/// 玩家推开门的下一帧门就会自己闩回去——<b>他将永远开不了自家的门</b>。
/// 故自动闩门只在**玩家的注意力要离开营地 / 危险将至**的那几个**时刻**触发（<see cref="DoorSecurityLogic.ShouldSecureAt"/>）：
/// 出发、回营、入夜。此后玩家仍能随时手动开门，本相位内不会被强行关回去——<b>安全的默认值，玩家仍可推翻</b>。
/// </para>
/// </summary>
public class DoorSecurityTests
{
    // ---------------- 何时扫门：三个「注意力离开营地 / 危险将至」的时刻 ----------------

    [Fact]
    public void 探索队出发那一刻要闩门_这正是那个洞()
    {
        // 本仓的"出门"不是空间动作（相位推进 + 场景切换，没人真的走过大门），
        // 故这一刻是唯一能替玩家把门带上的时机。
        Assert.True(DoorSecurityLogic.ShouldSecureAt(DayPhase.DayTravel));
    }

    [Fact]
    public void 入夜要闩门_睡前谁都会检查一遍门()
    {
        Assert.True(DoorSecurityLogic.ShouldSecureAt(DayPhase.NightPrep));
    }

    [Fact]
    public void 探索队回营要闩门_把夜里可能开着的门重新闩好()
    {
        Assert.True(DoorSecurityLogic.ShouldSecureAt(DayPhase.DayReturn));
    }

    [Theory]
    [InlineData(DayPhase.DawnMeal)]
    [InlineData(DayPhase.DayPrep)]
    [InlineData(DayPhase.DayExplore)]
    [InlineData(DayPhase.DuskMeal)]
    [InlineData(DayPhase.NightAct)]
    public void 其余相位不强制闩门_玩家白天想开着门_夜里想开门迎战都是他的自由(DayPhase phase)
    {
        // 反例护栏：若这里返回 true，玩家夜里刚推开大门放人进来，下一个相位就被强行关上——
        // 自动化就从"兜底"变成了"跟玩家抢方向盘"。
        Assert.False(DoorSecurityLogic.ShouldSecureAt(phase));
    }

    // ---------------- 扫到一扇门时：闩不闩 ----------------

    [Fact]
    public void 敞着的大门要自动闩上()
    {
        Assert.True(DoorSecurityLogic.ShouldAutoBar(DoorState.Open, barrable: true, doorwayOccupied: false));
    }

    [Fact]
    public void 只是关着的大门也要自动闩上_对会拧门把手的敌人来说关着就等于没关()
    {
        // 劫掠者会开门 ⇒「关着 + 没锁 + 够得着」三条全中 ⇒ 推门直入，250HP 形同虚设。
        // 这是 impl-door 亲手堵过一次的洞，自动闩门不能把它漏回来。
        Assert.True(DoorSecurityLogic.ShouldAutoBar(DoorState.Closed, barrable: true, doorwayOccupied: false));
    }

    [Fact]
    public void 已经闩上的门不再动它_幂等()
    {
        // 每个相位都扫一遍，若已闩的门还"闩一次"，就会每天白白重烘焙导航 + 刷一条没意义的提示。
        Assert.False(DoorSecurityLogic.ShouldAutoBar(DoorState.Barred, barrable: true, doorwayOccupied: false));
    }

    [Fact]
    public void 民居的门不闩_它们没有闩_而且营内的人要自己走进屋去()
    {
        // 零回归的要害：住宅/仓库/牛棚的门默认**开着**，营内幸存者 AI 靠它寻路进屋读书睡觉干活。
        // 自动闩门若把它们一并关上，全营的人会卡死在门外。barrable=false 是这条线的唯一闸。
        Assert.False(DoorSecurityLogic.ShouldAutoBar(DoorState.Open, barrable: false, doorwayOccupied: false));
        Assert.False(DoorSecurityLogic.ShouldAutoBar(DoorState.Closed, barrable: false, doorwayOccupied: false));
    }

    [Fact]
    public void 门口站着人就不闩_否则会把他实心夹在门板里()
    {
        Assert.False(DoorSecurityLogic.ShouldAutoBar(DoorState.Open, barrable: true, doorwayOccupied: true));
    }

    [Fact]
    public void 锁着的门不碰_它已经挡着了_而且锁和闩是两种信息()
    {
        // 锁着 = 能撬（玩家的取舍工具）；闩着 = 撬不了。把 Locked 改写成 Barred 会悄悄抹掉一扇门"有锁"这件事。
        Assert.False(DoorSecurityLogic.ShouldAutoBar(DoorState.Locked, barrable: true, doorwayOccupied: false));
    }

    [Fact]
    public void 自动闩门的落点就是关门的落点_规则不散写两份()
    {
        // 自动闩门 = 替玩家做那个"关门"动作，目标态必须复用 DoorLogic.ClosedRestingState，
        // 否则日后改了"关门即闩"的口径，自动闩门会悄悄留在旧口径上。
        Assert.Equal(DoorState.Barred, DoorLogic.ClosedRestingState(barrable: true));
        Assert.Equal(DoorState.Closed, DoorLogic.ClosedRestingState(barrable: false));
    }

    // ---------------- 空营地：一律闩上（本单拍板） ----------------

    [Fact]
    public void 最后一个人出门时大门照样闩上_空营地也闩()
    {
        // 【拍板】不看营里还剩没剩人，出发那一刻一律闩上。
        // 决定性理由：**回营根本不经过大门**——探索队是场景切换回来的（UnloadExplorationLevel 把人 reparent
        // 到营地正中），不是走回来的。故"闩上了回来得自己开门"这个代价在本仓**不存在**，闩上是纯收益。
        // 判定签名里因此**没有"营里还有几个人"这一维**——一条不影响结果的输入，不该存在。
        Assert.True(DoorSecurityLogic.ShouldAutoBar(DoorState.Open, barrable: true, doorwayOccupied: false));
    }

    // ---------------- 组合护栏：闩上之后，那个洞真的堵住了 ----------------

    [Fact]
    public void 出发时自动闩门之后_劫掠者再也推不开大门_只剩砸()
    {
        DoorState gate = DoorState.Open; // 玩家开着门就去探索了

        if (DoorSecurityLogic.ShouldSecureAt(DayPhase.DayTravel)
            && DoorSecurityLogic.ShouldAutoBar(gate, barrable: true, doorwayOccupied: false))
        {
            gate = DoorLogic.ClosedRestingState(barrable: true);
        }

        Assert.Equal(DoorState.Barred, gate);
        Assert.False(DoorLogic.CanOpen(gate, Faction.Raider, isAnimal: false));  // 推不开
        Assert.False(DoorLogic.CanPick(gate, Faction.Raider, isAnimal: false, lockpickCount: 99)); // 也撬不了（横木在里侧）
        Assert.True(DoorLogic.CanBash(gate));                                     // 只剩砸——250HP 重新说话
        Assert.True(DoorLogic.CanOpen(gate, Faction.Survivor, isAnimal: false));  // 自己人回来一抬就开
    }

    // ---------------- 门态徽标：三态一眼可辨，且不靠颜色 ----------------

    [Fact]
    public void 四个门态四个形状_互不相同_不许靠颜色区分()
    {
        // ⚠️ 这条是**可访问性护栏**，不是形式测试：闩着和关着在画面上长得一样，正是玩家"以为闩上了"而全营被端的成因。
        // 一旦有人把两个态映射到同一个形状（只用颜色区分），这条会红。
        DoorGlyph[] glyphs =
        [
            DoorBadgeVisual.GlyphFor(DoorState.Open),
            DoorBadgeVisual.GlyphFor(DoorState.Closed),
            DoorBadgeVisual.GlyphFor(DoorState.Barred),
            DoorBadgeVisual.GlyphFor(DoorState.Locked),
        ];
        Assert.Equal(4, glyphs.Distinct().Count());
    }

    [Fact]
    public void 形状即语义_闩着画一道横闩_锁着画一把挂锁()
    {
        Assert.Equal(DoorGlyph.OpenFrame, DoorBadgeVisual.GlyphFor(DoorState.Open));   // 空门框：什么都没有
        Assert.Equal(DoorGlyph.Handle, DoorBadgeVisual.GlyphFor(DoorState.Closed));    // 门把手：推一下就开
        Assert.Equal(DoorGlyph.Bar, DoorBadgeVisual.GlyphFor(DoorState.Barred));       // 横闩：外人只能砸
        Assert.Equal(DoorGlyph.Padlock, DoorBadgeVisual.GlyphFor(DoorState.Locked));   // 挂锁：要撬（或砸）
    }

    [Fact]
    public void 开着的门绝不画锁_锁只属于真锁着的门()
    {
        // 铁律：视觉只映射引擎里真实存在的状态。开着的门不需要撬，画锁就是在发明状态。
        Assert.NotEqual(DoorGlyph.Padlock, DoorBadgeVisual.GlyphFor(DoorState.Open));
    }

    [Fact]
    public void 锁的档次用刻痕数表示_随档次严格递增_同样不靠颜色()
    {
        Assert.Equal(0, DoorBadgeVisual.LockNotches(LockTier.None));
        Assert.Equal(1, DoorBadgeVisual.LockNotches(LockTier.Simple));
        Assert.Equal(2, DoorBadgeVisual.LockNotches(LockTier.Standard));
        Assert.Equal(3, DoorBadgeVisual.LockNotches(LockTier.Sturdy));

        // 玩家据此一眼判断"这扇门值不值得撬"（坚固锁期望 32 秒 / 3 根铁丝）。
        Assert.True(DoorBadgeVisual.LockNotches(LockTier.Sturdy) > DoorBadgeVisual.LockNotches(LockTier.Standard));
        Assert.True(DoorBadgeVisual.LockNotches(LockTier.Standard) > DoorBadgeVisual.LockNotches(LockTier.Simple));
    }
}
