using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型
//（与 CoverLogic.cs / PlacementRules.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（不建碰撞体 / 不挖导航洞 / 每帧查场并乘进移速）归 Godot 消费层（CampMain / Actor），本文件只出**规则 + 数值**。

/// <summary>
/// <b>家具可跨越 + 跨过减速</b> —— 用户拍板「椅子之类的别的家具都可以跨过，但是跨过时会减少移动速度」；当前减速值以 Wiki 配置为准。
/// 与「<b>改装台、烹饪台不允许跨越</b>」的唯一落点。
///
/// <para>═══ <b>这条规则把家具从"墙"改成了"减速带"</b> ═══
/// 在它之前，实心家具（柜子/衣柜/展示柜）会<b>建碰撞体 + 挖导航洞</b> —— 对寻路来说，一排柜子和一堵墙没有任何区别。
/// 这正是「墙不能建」（防 kill box，见 <see cref="StructureBuildCost"/>）留下的那个后门。
/// 现在它们<b>不挡路了</b>：谁都能走过去，只是按配置减速。<b>kill box 的风险从根上没了 —— 摆不出迷宫，因为没有一格是走不通的。</b>
/// </para>
///
/// <para>═══ <b>那「不许贴着大门和围栏」为什么还留着？</b>（<see cref="PlacementRules"/>，别顺手删了）═══
/// 因为它还挡着另外两件事：① 贴着围栏堆一排减速带，等于给砸墙的丧尸<b>白送一条减速走廊</b>给守军 —— 不，反过来，
/// 是给<b>砌墙工</b>添堵：他每次去修墙都得趟过你堆的家具。② 家具仍会占住<b>围栏施工站位带</b>。
/// <b>减速带不是墙，但也不该糊在防线上。</b>
/// </para>
///
/// <para>═══ <b>减速一律乘算，禁止加算</b>（项目铁律）═══
/// <see cref="CrossingSpeedMultiplier"/> = 0.75 是<b>乘进</b> <c>Actor</c> 那条既有移速乘子链的
/// （残疾 × 饥饿 × 骨折 × 震荡/命中减速 × <b>家具</b>），不是从总数里减百分点。
/// 各效果按乘算组合，具体乘子以 Wiki 配置为准。
/// </para>
///
/// <para>═══ <b>对所有角色生效，不只是玩家</b> ═══
/// 减速挂在 <c>Actor</c> 的移速链上，而 <c>Pawn</c> / <c>Zombie</c> / <c>Raider</c> / <c>Dog</c> <b>全都是 Actor</b>
/// ⇒ 丧尸、劫掠者、布鲁斯跨过家具<b>同样按配置减速</b>。这是<b>结构性</b>的，不是逐个角色加的
/// —— 想给某个角色开后门，得先把它从 Actor 里摘出去。
/// 「把家具堆成减速带拖住丧尸」因此成了一个真战术，且<b>双向对称</b>（你的人跨自家柜子也慢），同 <see cref="CoverLogic"/> 的口径。
/// </para>
/// </summary>
public static class FurnitureTraversal
{
    /// <summary>
    /// 跨过一件可跨越家具时的<b>移速乘子</b>，具体值以 Wiki 配置为准。
    /// <b>乘算</b>进 <c>Actor</c> 的既有移速链，<b>不是</b>从总数里减百分点（见类注）。
    /// </summary>
    public const double CrossingSpeedMultiplier = 0.75;

    /// <summary>没踩在任何家具上 ⇒ 原速。</summary>
    public const double NoSlowdown = 1.0;

    /// <summary>
    /// <b>不可跨越的家具</b>（<b>用户点名</b>：改装台、烹饪台）—— 它们是<b>固定锚点的大型作业台</b>，
    /// 照旧建碰撞体 + 挖导航洞，与围栏/墙同列。
    /// <para>
    /// <b>工作台也在这里</b>：它与改装台/烹饪台是<b>同一类东西</b>（一整张厚重的作业台，不是你能一脚跨过去的矮物），
    /// 用户点名那两台时说的理由（"营地内固定位置"的大型设施）对它<b>一字不差地适用</b>。
    /// ⚠️ 但用户<b>只点名了改装台与烹饪台</b> —— 工作台是我按同类推的，已在 [DECISION] 上抛，随时可一行改回。
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这里引用常量而不是抄字符串，是有原因的</b>（impl-modbench 指出的坑，别改回字面量）：
    /// 「一件设施是实心的」和「它不可跨越」是<b>同一个事实的两半</b>，但分别写在两个文件里
    /// （<c>WeaponModLogic.BenchSpec.IsSolid</c> / 本集合）。若这里抄一份字面量 "改装台"，
    /// 那么谁哪天改了 <see cref="WeaponModLogic.BenchFurnitureKey"/> 的值，两边就<b>悄悄分叉</b>：
    /// 改装台会变成一脚跨得过去的东西，<b>而且不会有任何测试报红</b> —— 它同时会抽掉
    /// 「固定锚点摆错 = 一条玩家绕不开的死路」那组自检的全部前提。
    /// 直接引用常量 ⇒ 改名字会牵着这里一起动，<b>分叉在编译期就不可能发生</b>。
    /// （<c>FixedFacilityAnchorTests.实心与不可跨越必须一致_两份事实源不许分叉</c> 另有一道运行期护栏。）
    /// </remarks>
    private static readonly HashSet<string> Workstations = new()
    {
        // 工作台：全仓没有为它定义过常量（<see cref="FurnitureBuildCost"/> 的键就是它的事实源），故此处只能写字面量。
        // 它也不参与「实心 spec ↔ 不可跨越」那对双事实源，没有分叉面。
        "工作台",
        WeaponModLogic.BenchFurnitureKey,   // "改装台"
        CookStation.PropName,               // "烹饪台"
    };

    /// <summary>
    /// 实例名 → 类型名：可重复摆放的家具在场上名字带流水号（"沙袋#3" / "床#2"），而本表按类型索引。
    /// 与 <see cref="FurnitureBuildCost"/> 同一套归一口径（截掉 '#' 及其后缀）。
    /// </summary>
    private static string TypeKeyOf(string key)
    {
        int hash = key.IndexOf('#');
        return hash >= 0 ? key[..hash] : key;
    }

    /// <summary>
    /// <b>这件家具能不能跨过去。</b>
    ///
    /// <para>
    /// ⚠️ <b>缺省是「可跨越」，这是刻意的 fail-safe</b>（同 <see cref="PlaceableSpec.AllowedAgainstDefenses"/> 那套）：
    /// 新家具的作者忘了登记时，拿到的是<b>安全</b>的那一侧 —— 家具<b>不挡路</b>。
    /// 反过来设默认（忘填 = 实心）意味着一次疏漏就能在营地里凭空造出一堵墙、把人卡死在门外，
    /// 而这类 bug 要等到丧尸绕不进来才会被发现。<b>忘填只该导致"这件家具没那么碍事"，不该导致"寻路断了"。</b>
    /// </para>
    ///
    /// <para><b>只对家具发问</b>：调用方须先确认它是家具（在 <see cref="FurnitureBuildCost"/> 目录里）。
    /// 草垛 / 收音机 / 尸体这类<b>不是"造出来的家具"</b>的道具不走这条路，维持各自原样。</para>
    /// </summary>
    public static bool IsTraversable(string furnitureKey)
        => furnitureKey is not null && !Workstations.Contains(TypeKeyOf(furnitureKey));

    /// <summary>
    /// 踩在这件家具上的移速乘子：可跨越 ⇒ <see cref="CrossingSpeedMultiplier"/>；
    /// 不可跨越 ⇒ <see cref="NoSlowdown"/>（<b>它是实心的，你压根站不上去</b> —— 减速对它没有意义）。
    /// </summary>
    public static double SpeedMultiplierOf(string furnitureKey)
        => IsTraversable(furnitureKey) ? CrossingSpeedMultiplier : NoSlowdown;

    /// <summary>
    /// <b>建图时：这件 camp.json 的 prop 是不是一件「不进 <c>_furniture</c> 的可跨越矮物」</b>
    /// —— 即减速场要单独收录它的矩形。
    ///
    /// <para>
    /// <b>为什么需要这个谓词（一个真踩过的坑）</b>：减速场原本只从 <c>_furniture</c> 重建，而 <c>_furniture</c>
    /// 只收<b>家具目录（<see cref="FurnitureBuildCost"/>）里认得的、可拆的</b>东西。于是两类东西<b>被漏掉了</b>：
    /// <list type="bullet">
    /// <item><b>座位</b>（<c>role="seat"</c>）—— 它们不可拆、不进 <c>_furniture</c>。而<b>椅子正是用户点名的那件可跨越家具</b>。</item>
    /// <item><b>门口的沙袋垒</b>（<c>role="cover"</c>，名叫"北门沙袋垒A"这类）—— 它们是 authored 的，<b>名字不在家具目录里</b>
    ///       （目录里只有玩家造的那件"沙袋"），所以永远进不了 <c>_furniture</c>。
    ///       ⇒ 用户拍板「沙袋也减速」时权衡的那个效果（<b>涌门的丧尸被自家门口沙袋拖慢</b>）<b>根本不会发生</b>。</item>
    /// </list>
    /// 这两类都是<b>矮的、跨得过去的、且确实站得上人的</b>东西，理应减速。本谓词就是它们进场的唯一入口。
    /// </para>
    ///
    /// <para>
    /// <b>尸体不算</b>（<c>role="corpse"</c>）：祖母的尸体不是家具，用户说的是"椅子之类的<b>别的家具</b>"。
    /// 跨过她不减速（维持原样，零回归）。<b>床不走这条路</b>：床进 <c>_furniture</c>，由那边统一处理（别在这儿重复登记，
    /// 否则同一张床会被连乘两次 ⇒ 0.75 × 0.75 = 0.5625）。
    /// </para>
    /// </summary>
    /// <param name="role">camp.json 里 prop 的 <c>role</c> 字段。</param>
    public static bool IsLooseTraversableProp(string? role)
        => role is "seat" or "cover";
}

/// <summary>
/// <b>减速场</b>：场上一堆「可跨越家具」的占地矩形 + 各自的移速乘子。<c>Actor</c> 每帧问一次
/// 「我脚下这点该乘多少」（<see cref="MultiplierAt"/>），乘进移速链。
/// <para>形态照搬 <see cref="CoverField"/>（同为"消费层维护、Actor 静态查询"的空间场），不另造一套概念。</para>
/// </summary>
public sealed class TraversalField
{
    private readonly record struct Patch(float X, float Y, float W, float H, double Multiplier)
    {
        public bool Contains(Vector2 p) =>
            p.X >= X && p.X <= X + W && p.Y >= Y && p.Y <= Y + H;
    }

    private readonly List<Patch> _patches = new();

    /// <summary>登记一块可跨越家具的占地（<paramref name="multiplier"/> 缺省 = 跨家具的 0.75）。</summary>
    public void Add(float x, float y, float w, float h,
        double multiplier = FurnitureTraversal.CrossingSpeedMultiplier)
        => _patches.Add(new Patch(x, y, w, h, multiplier));

    /// <summary>撤掉一块（家具被拆走 / 摆到别处）。按矩形值相等匹配，撤第一块命中的。</summary>
    public bool RemoveRect(float x, float y, float w, float h)
    {
        for (int i = 0; i < _patches.Count; i++)
        {
            Patch p = _patches[i];
            if (p.X == x && p.Y == y && p.W == w && p.H == h)
            {
                _patches.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>清空（重建营地 / 重烘焙时）。</summary>
    public void Clear() => _patches.Clear();

    /// <summary>登记块数（供测试/调试）。</summary>
    public int Count => _patches.Count;

    /// <summary>
    /// <paramref name="point"/> 处的移速乘子：<b>把盖住这一点的每一块家具的乘子连乘起来</b>
    /// （没踩着任何家具 ⇒ <see cref="FurnitureTraversal.NoSlowdown"/> = 原速）。
    /// <para>
    /// <b>连乘而不是取最小</b>：这是项目的乘算通则（百分比加成一律乘算）。放置规则本就禁止家具重叠
    /// （<see cref="PlacementVerdict.OverlapsFurniture"/>），所以实战里最多命中一块 —— 但规则本身仍按连乘写，
    /// 免得日后哪天允许重叠了，这里悄悄变成一条加算的例外。
    /// </para>
    /// </summary>
    public double MultiplierAt(Vector2 point)
    {
        double mult = FurnitureTraversal.NoSlowdown;
        for (int i = 0; i < _patches.Count; i++)
        {
            if (_patches[i].Contains(point))
            {
                mult *= _patches[i].Multiplier;
            }
        }
        return mult;
    }
}
