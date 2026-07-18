using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型
//（与 SandbagSpec.cs / FenceUpgradeLogic.cs / SalvageLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（建碰撞体 / 挖导航洞 / 重烘焙导航 / 画放置预览）归 Godot 消费层（CampMain），本文件只出**判定**。

/// <summary>一件可放置家具的**类型级**规格（尺寸 + 它守不守本规则）。</summary>
/// <param name="TypeName">中文类型名（"改装台" / "床"）——只用于提示文案，不做逻辑键。</param>
/// <param name="Width">占地宽（世界像素）。</param>
/// <param name="Height">占地高（世界像素）。</param>
/// <param name="IsSolid">
/// 实心（建碰撞体 + 挖导航洞 ⇒ <b>真的挡路</b>）。<b>本字段不参与放置判定</b>，只是给消费层看的施工说明；
/// 「守不守围栏缓冲带」由 <paramref name="AllowedAgainstDefenses"/> 单独拍板 —— 两件事分开，见该参数的说明。
/// </param>
/// <param name="AllowedAgainstDefenses">
/// <b>获准贴着防线放</b>（缺省 <c>false</c> = 老老实实守缓冲带）。
/// <para>
/// ⚠️ <b>缺省值是"受约束"，这是刻意的 fail-safe</b>：新家具的作者忘了填这个标记时，拿到的是<b>安全</b>的那一侧。
/// 一个漏填就把 kill box 的门开回来，代价太大——所以默认值站在保守这边，想豁免的人必须显式写出来、并说明理由。
/// </para>
/// <para>
/// 目前唯一的豁免是<b>沙袋</b>：它恒不挡路（<see cref="SandbagSpec.IsSolid"/> = false），
/// 而它的**本职就是垒在防线后面**让人蹲着挨枪 —— 把它赶离围栏 64px 等于废掉掩体这个机制，
/// 且与"防止家具阻挡寻路"这个动机毫无关系。
/// </para>
/// </param>
/// <param name="AllowedOutdoors">
/// <b>获准放在室外</b>（缺省 <c>false</c> = <b>只能放在建筑里</b>）。
/// <para>
/// 用户拍板：「<b>家具不能放到室外</b>」「<b>沙袋能放到室内和室外</b>」。
/// </para>
/// <para>
/// ⚠️ <b>缺省值是"只能室内"，与 <paramref name="AllowedAgainstDefenses"/> 同一套 fail-safe</b>：
/// 新家具的作者忘了填这个标记时，拿到的是<b>受限</b>的那一侧。忘填只会导致"这件家具只能摆屋里"（一句提示的事），
/// 而反过来设默认会让新家具<b>悄悄获得户外特权</b> —— 那正是用户要禁的东西，且没有任何测试会报红。
/// </para>
/// <para>
/// <b>这条与「桌子算掩体」是一对</b>：桌子（室内掩体，打巷战用）与沙袋（唯一能垒在门口/院子里的掩体，守门用）
/// 因此<b>不再互相替代</b> —— 否则桌子的材料成本与掩体效果会直接架空沙袋机制。
/// </para>
/// <para>
/// <b>目前拿到户外豁免的</b>：沙袋（本职就是垒在门口）、圈套陷阱（一圈铁丝套，摆屋里等于废掉它）、
/// 以及 authored 的固定作业台（工作台本身就在院子里）。<b>床与桌子没有</b>——它们是家具。
/// </para>
/// </param>
public readonly record struct PlaceableSpec(
    string TypeName,
    float Width,
    float Height,
    bool IsSolid,
    bool AllowedAgainstDefenses = false,
    bool AllowedOutdoors = false);

/// <summary>放置被拒的原因（<see cref="PlacementVerdict.Ok"/> = 可以放）。</summary>
/// <remarks>
/// <b>刻意不进 <c>DisplayNames</c></b>：本枚举永远不会被 <c>ToString()</c> 给玩家看——玩家看到的是
/// <see cref="PlacementRules.RejectionText"/> 出的整句中文（同 <see cref="SandbagSpec.PlacementResult"/> 的既有做法）。
/// </remarks>
public enum PlacementVerdict
{
    Ok,

    /// <summary>超出营地边界。</summary>
    OutOfBounds,

    /// <summary>
    /// <b>家具只能放在建筑里</b>（用户拍板：「家具不能放到室外」）。沙袋/陷阱等有户外豁免的东西不会撞上这条。
    /// </summary>
    OutdoorsNotAllowed,

    /// <summary><b>贴着围栏/大门/门</b>——本规则的主角，见 <see cref="PlacementRules"/> 类注。</summary>
    TooCloseToDefenses,

    /// <summary>压在墙/建筑/废墟一类实心物上。</summary>
    BlockedBySolid,

    /// <summary>压在另一件已放好的家具上。</summary>
    OverlapsFurniture,
}

/// <summary>
/// <b>家具放置的通用校验</b> —— 用户原话「<b>为了防止玩家使用改装台、椅子等家具阻挡寻路，放置的时候就不允许贴着大门和围栏</b>」的唯一落点。
///
/// <para>═══ <b>这条规则为什么现在才需要</b> ═══
/// 在它之前，营地里玩家唯一放得下的东西是<b>沙袋</b>，而沙袋<b>恒不挡路</b>
/// （<see cref="SandbagSpec.IsSolid"/> / <see cref="SandbagSpec.CarvesNavHole"/> 双 false）——
/// "玩家用摆件堵死寻路"这件事在物理上根本发生不了，所以从来不需要管。
/// <b>床</b>与<b>改装台</b>是头两件玩家可放置的<b>实心</b>家具：它们进 <c>_navHoles</c>、挖导航洞、真的挡路。
/// kill box 的门从这一刻起第一次被推开一条缝，本类就是把它按回去的东西。
/// </para>
///
/// <para>═══ <b>与「墙不能建」同一个动机，别把它当平衡问题"优化"掉</b> ═══
/// 用户拍板「墙不能建、只能升级开局自带的围栏」（见 <see cref="StructureBuildCost"/> / <see cref="FenceUpgradeLogic"/> 类注），
/// 理由是<b>可自由摆墙 ⇒ 玩家能搭 kill box</b>（用障碍的迷宫牵着敌人寻路，把一场战斗变成一道几何题），
/// 会架空视野锥 / 噪音 / 包抄 / 掩体 / 岗哨一整套系统。
/// <b>实心家具是"墙"的一个后门</b>：一排改装台和一堵墙对寻路来说没有任何区别。本规则堵的正是这个后门里
/// 最危险的一段——<b>贴着防线的那一圈</b>：那里既是敌人破防涌入的地方，也是自己人砌墙/撤退要走的地方。
/// </para>
///
/// <para>═══ <b>没有格子，所以是像素缓冲带</b> ═══
/// 营地是<b>连续像素空间</b>（轴对齐 <c>Rect2</c>，iso 投影只用于显示），<b>没有网格</b> ——
/// 故"贴着"没法表达成"4 邻接 / 8 邻接"，只能是<b>一条 <see cref="KeepOut"/> 像素宽的禁建带</b>，
/// 沿围栏 / 大门 / 门的<b>四周</b>展开。宽度取值的两条硬约束见 <see cref="KeepOut"/>。
/// </para>
/// </summary>
public static class PlacementRules
{
    /// <summary>
    /// <b>禁建缓冲带宽度</b>（像素）：家具边缘与围栏/大门/门边缘之间至少要空出这么多。
    ///
    /// <para><b>这个数不是拍脑袋来的，它必须同时越过两条硬下限</b>（要改先读这两条）：</para>
    /// <list type="number">
    /// <item><b>&gt; <see cref="NavCorridorFloor"/>（32px）</b>：导航烘焙 <c>AgentRadius = 14</c>、障碍轮廓外扩 2px
    ///       （<c>CampMain.BakeNavPoly</c>）⇒ 人能挤过去的最窄走廊 = 2 × (14 + 2) = 32px。
    ///       缓冲带若窄于此，家具与围栏之间那道缝**根本走不了人** —— 那就等于玩家砌了一堵墙，本规则也就白写了。</item>
    /// <item><b>&gt; <see cref="WallBuildStandLane"/>（40px）</b>：砌墙工得站进 <c>seg.Rect.Grow(Pawn.Radius 12 + PendingArriveMargin 28)</c>
    ///       才算"到位"（<c>CampMain</c> 的围栏施工到达判定）。缓冲带必须把这条**施工站位带**整个让出来，
    ///       否则玩家会用家具把自己围栏的<b>升级/修复入口</b>堵死 —— 一个远比 kill box 更难查的坑
    ///       （墙还在，料也够，人就是走不过去，于是"这墙升不了"）。</item>
    /// </list>
    /// <para>
    /// 64 同时是围栏一格（<c>CampMain.FenceSegment</c> = 100px）的六成、一个人（半径 12）身位的两倍多——
    /// 沿整条防线让出一条真正走得开人的通道，而不是一条理论上挤得过去的缝。
    /// </para>
    /// <para>营地内场约 1756 × 1156，这条带子吃掉约 17% 的可用地面：<b>够宽到有意义，也远不至于放不下东西</b>。</para>
    /// </summary>
    public const float KeepOut = 64f;

    /// <summary>
    /// 人能挤过去的最窄走廊（像素）= 2 × (导航 AgentRadius 14 + 障碍外扩 2)。
    /// <b>缓冲带的绝对下限</b>——比它还窄的"通道"不是通道，是墙。
    /// </summary>
    public const float NavCorridorFloor = 32f;

    /// <summary>
    /// 砌墙工的施工站位带（像素）= Pawn 半径 12 + 到达判定余量 28。
    /// 围栏边这么宽的一圈<b>必须是空地</b>，否则围栏升级/修复的人走不到墙根下。
    /// </summary>
    public const float WallBuildStandLane = 40f;

    /// <summary>与既有实心物/家具之间的视觉余隙（像素）——不许长在别人身体里（同 <see cref="SandbagSpec.Clearance"/>）。</summary>
    public const float Clearance = 2f;

    /// <summary>
    /// 一处轴对齐矩形（放置校验用；避开 <c>Godot.Rect2</c> 以保零依赖）。
    /// <para>
    /// 与 <see cref="SandbagSpec.Box"/> 长得一样却各自独立，是<b>有意的</b>：沙袋豁免本规则、走的是它自己那条老路
    /// （<see cref="SandbagSpec.CanPlace"/>），两者从不交互 —— 这不是重复，是两件互不相干的事恰好都需要一个矩形。
    /// </para>
    /// </summary>
    public readonly record struct Box(float X, float Y, float W, float H)
    {
        public float MaxX => X + W;
        public float MaxY => Y + H;

        /// <summary>与另一矩形是否重叠（含 <paramref name="margin"/> 外扩：传 <see cref="KeepOut"/> 即"是否进了缓冲带"）。</summary>
        public bool Overlaps(in Box o, float margin = 0f) =>
            X - margin < o.MaxX && MaxX + margin > o.X &&
            Y - margin < o.MaxY && MaxY + margin > o.Y;

        /// <summary>是否完全落在 <paramref name="bounds"/> 内。</summary>
        public bool InsideOf(in Box bounds) =>
            X >= bounds.X && Y >= bounds.Y && MaxX <= bounds.MaxX && MaxY <= bounds.MaxY;
    }

    /// <summary>以 <paramref name="center"/> 为中心、该类型家具的占地矩形。</summary>
    public static Box BoxAt(in PlaceableSpec spec, Vector2 center) =>
        new(center.X - spec.Width / 2f, center.Y - spec.Height / 2f, spec.Width, spec.Height);

    /// <summary>
    /// <b>能不能把一件 <paramref name="spec"/> 放在 <paramref name="center"/>。</b>
    /// </summary>
    /// <param name="defenses">
    /// 场上<b>围栏 / 大门 / 门</b>的占位（<c>CampStructureKind</c> 那三类，即 <c>CampMain</c> 的 <c>_structures</c>）。
    /// 它们同时也在 <paramref name="solids"/> 里（都挖了导航洞），但必须<b>单独传一份</b>——
    /// 缓冲带只沿防线展开，<b>不沿建筑墙</b>：把工作台靠在自家屋里的墙角是天经地义的事
    /// （camp.json 的 props 本来就都摆在内墙角），堵不了任何人的路。
    /// </param>
    /// <param name="solids">场上一切实心障碍（建筑墙 / 废墟 / 实心岗位 / 已放好的实心家具，即 <c>_navHoles</c>）。</param>
    /// <param name="furniture">已放好的家具占位（不许摞第二件）。</param>
    /// <param name="indoorAreas">
    /// 场上各建筑的<b>室内可用区</b>（外框<b>缩进墙厚</b>后的矩形——墙本身不是室内）。
    /// <para>
    /// ⚠️ <b>这是必填参数，刻意不给缺省</b>：给它一个"缺省 = 不校验"的空表，就等于让任何忘了传它的调用方
    /// <b>悄悄绕过</b>「家具不能放到室外」——规则还在，却没人执行，而且<b>不会有任何测试报红</b>。
    /// 这个坑本项目刚踩过一次（沙袋减速在纯逻辑里是绿的，消费层却从没登记过它）。
    /// <b>没有室内数据的调用方，本就没资格判这件事。</b>
    /// </para>
    /// </param>
    public static PlacementVerdict CanPlace(
        in PlaceableSpec spec,
        Vector2 center,
        in Box bounds,
        IReadOnlyList<Box> defenses,
        IReadOnlyList<Box> solids,
        IReadOnlyList<Box> furniture,
        IReadOnlyList<Box> indoorAreas)
    {
        Box box = BoxAt(spec, center);

        if (!box.InsideOf(bounds))
        {
            return PlacementVerdict.OutOfBounds;
        }

        // 家具只能放屋里（用户拍板）。**整个占位矩形**都得在同一间屋子里 —— 一张桌子半截探出墙外不算"在屋里"。
        // 排在防线/实心物判定之前：对一个把床往院子里放的玩家，"家具只能摆屋里"远比"离围栏远点"说明问题。
        if (!spec.AllowedOutdoors && !IsIndoors(box, indoorAreas))
        {
            return PlacementVerdict.OutdoorsNotAllowed;
        }

        // 防线缓冲带 —— 本类的主角。**先于实心物判定**：一件压在围栏身上的家具，
        // 报"太靠近防线"比报"那儿已经有东西了"更说明问题（后者会让玩家以为挪一点点就行）。
        if (!spec.AllowedAgainstDefenses)
        {
            for (int i = 0; i < defenses.Count; i++)
            {
                if (box.Overlaps(defenses[i], KeepOut))
                {
                    return PlacementVerdict.TooCloseToDefenses;
                }
            }
        }

        for (int i = 0; i < solids.Count; i++)
        {
            if (box.Overlaps(solids[i], Clearance))
            {
                return PlacementVerdict.BlockedBySolid;
            }
        }

        for (int i = 0; i < furniture.Count; i++)
        {
            if (box.Overlaps(furniture[i], Clearance))
            {
                return PlacementVerdict.OverlapsFurniture;
            }
        }

        return PlacementVerdict.Ok;
    }

    /// <summary>
    /// <b>这个占位是不是整个都在某一间屋子里。</b>
    /// <para>
    /// <b>"整个"是关键</b>：只判中心点的话，一张桌子可以半截探出墙外——那既不是室内，视觉上也穿模。
    /// 而且必须落在<b>同一间</b>屋子里（<see cref="Box.InsideOf"/> 逐间试）：横跨两栋楼的家具不存在。
    /// </para>
    /// </summary>
    public static bool IsIndoors(in Box box, IReadOnlyList<Box> indoorAreas)
    {
        for (int i = 0; i < indoorAreas.Count; i++)
        {
            if (box.InsideOf(indoorAreas[i]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>放置被拒时给玩家的一行提示（中文，黑色幽默文风，同 <see cref="SandbagSpec.RejectionText"/>）。</summary>
    public static string RejectionText(PlacementVerdict verdict) => verdict switch
    {
        PlacementVerdict.OutOfBounds =>
            "那已经是营地外面了。围栏是用来把东西挡在外头的——包括你的家具。",
        PlacementVerdict.OutdoorsNotAllowed =>
            "家具得摆在屋里。露天摆一张床，你会连人带被子一起泡在雨里——沙袋倒是不挑地方。",
        PlacementVerdict.TooCloseToDefenses =>
            "离围栏和大门远一点。墙根下那条道得留着：砌墙的人要走，逃命的人也要走。",
        PlacementVerdict.BlockedBySolid =>
            "那儿已经有东西了。",
        PlacementVerdict.OverlapsFurniture =>
            "那儿已经摆了一件了。摞两层不会让它更好用。",
        _ => "",
    };
}
