namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 真正的画笔（DrawRect / DrawCircle / DrawArc）在 DoorStateOverlay.cs，本文件只回答「该画哪个形状、几道刻痕」。

/// <summary>
/// 门态徽标的**形状**。<b>一个门态一个形状</b>——不是四种颜色的同一个方块。
/// <para>
/// <b>为什么必须是形状</b>：闩着和关着此前在画面上长得一模一样（只有悬停提示文字区分），
/// 而"大门到底闩没闩"<b>是攸关生死的一件事</b>。只靠颜色区分对色觉障碍玩家等于没区分，
/// 何况夜间遮暗下所有颜色都会趋同。<b>形状即语义。</b>
/// </para>
/// </summary>
public enum DoorGlyph
{
    /// <summary><b>开着</b>：一个空心门框——里头空空如也，你能走过去，别的东西也能。</summary>
    OpenFrame,

    /// <summary><b>关着</b>：一个门把手（实心小圆）。<b>推一下就开</b>——包括劫掠者的手。</summary>
    Handle,

    /// <summary><b>闩着</b>：一道横闩（贯穿门板的粗横杠 + 两端卡座）。外人推不开、撬不了，<b>只能砸</b>。</summary>
    Bar,

    /// <summary><b>锁着</b>：一把挂锁（锁体 + U 形锁梁）。要撬（安静、慢、费铁丝）或砸（快、很响）。</summary>
    Padlock,
}

/// <summary>
/// 门的三态（外加"锁着"）怎么画成**一眼可辨**的徽标。
///
/// <para>
/// ⚠️ <b>项目铁律：只映射引擎里真实存在的状态</b>。本类的输入只有 <see cref="DoorState"/> 与 <see cref="LockTier"/>
/// 这两个引擎里真有的东西——<b>不发明</b>"正在被砸""快撑不住了"之类的状态。
/// </para>
/// </summary>
public static class DoorBadgeVisual
{
    /// <summary>
    /// 这个门态该画哪个形状。<b>四态四形状，是单射</b>（单测钉死）——任何把两个态映射到同一形状
    /// （指望"再用颜色区分一下"）的改动都会当场变红。
    /// </summary>
    public static DoorGlyph GlyphFor(DoorState state) => state switch
    {
        DoorState.Open => DoorGlyph.OpenFrame,
        DoorState.Closed => DoorGlyph.Handle,
        DoorState.Barred => DoorGlyph.Bar,
        DoorState.Locked => DoorGlyph.Padlock,
        _ => DoorGlyph.OpenFrame,
    };

    /// <summary>
    /// 挂锁锁体上刻几道竖痕 = 锁的档次。<b>档次也用形状编码，不用颜色</b>——
    /// 玩家据此一眼判断"这扇门值不值得撬"（坚固锁 ‖‖‖ 期望要花 32 秒、断 3 根铁丝，而门外那群东西不会等你）。
    /// </summary>
    public static int LockNotches(LockTier tier) => tier switch
    {
        LockTier.None => 0,
        LockTier.Simple => 1,
        LockTier.Standard => 2,
        LockTier.Sturdy => 3,
        _ => 0,
    };
}
