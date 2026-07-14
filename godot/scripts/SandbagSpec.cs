using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 玩家可建造、可自由摆放的<b>沙袋</b>规格（用户拍板：「可自由建造摆放」）。
///
/// <para>═══ <b>为什么沙袋可以自由建造，而墙不行？（别"统一"掉这条规则）</b> ═══
/// 用户拍板 <b>"墙不能建"</b> 是为了<b>防 kill box</b>——玩家能砌墙就能用迷宫牵着敌人的寻路走，
/// 一举架空视野锥、噪音、包抄、掩体、岗哨整套系统。
///
/// 沙袋<b>不触发这个风险</b>，因为它<b>不阻挡移动、不改变寻路</b>（见 <see cref="IsSolid"/> /
/// <see cref="CarvesNavHole"/>，两者恒 false）：敌人照样直线冲过来，不会被牵着绕迷宫 ⇒ <b>摆不出 kill box</b>。
/// 它只给 25% 远程无伤，<b>而且敌人也能蹲在你的沙袋后面用</b>（<see cref="CoverLogic"/> 双向对称）。
/// ⇒ 玩家能经营防御位置，但摆不出必胜阵型——沙袋给的是"这个角度我更耐打"，不是"敌人只能从这里进来"。
///
/// <b>所以规则不一致是有意的</b>：能不能建，取决于它改不改寻路。谁要把沙袋改成实心的，
/// kill box 就回来了。</para>
/// </summary>
public static class SandbagSpec
{
    /// <summary>沙袋配方 id（<see cref="RecipeBook"/>）。拆除返还也按这张配方的材料算（<see cref="SalvageLogic"/>）。</summary>
    public const string RecipeId = "sandbag";

    /// <summary>库存里那件"沙袋"的物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "sandbag";

    /// <summary>
    /// 场上每垛沙袋的家具名前缀（实例名带流水号："沙袋#3"）。沙袋可重复摆放，故名字必须唯一。
    /// <para>拆除按**类型名**归一（<see cref="FurnitureBuildCost"/> 截掉 '#' 后缀）；
    /// 存读档按**实例名**对号入座（<c>CampSave.PlacedFurniture</c>）——摆了三垛就得还你三垛，位置一垛不差。</para>
    /// </summary>
    public const string FurnitureNamePrefix = "沙袋#";

    /// <summary>这个家具名是不是一垛玩家摆的沙袋（"沙袋#3" → true；"工作台" → false）。</summary>
    public static bool IsSandbagFurniture(string? furnitureName)
        => furnitureName is not null
        && furnitureName.StartsWith(FurnitureNamePrefix, System.StringComparison.Ordinal);

    /// <summary>库存里的物品描述（黑色幽默文风，同批次15 的物品级 flavor）。</summary>
    public const string ItemDescription =
        "一只麻袋，装满了土。它替你挨枪子，四分之一的时候还真挡得住——它对这份工作没有怨言，也没有别的抱负。";

    /// <summary>一垛沙袋的占地（世界像素，拟定待调）：够一个人蹲在后面，不至于大到当墙用。</summary>
    public const float Width = 60f;
    public const float Height = 24f;

    /// <summary>
    /// <b>恒 false，这是沙袋获准自由建造的全部理由。</b>不建碰撞体 ⇒ 人和丧尸都能直接走过去。
    /// 改成 true 之前请先回去读本类的类注：kill box 就是这么来的。
    /// </summary>
    public const bool IsSolid = false;

    /// <summary>
    /// <b>恒 false。</b>不挖导航洞 ⇒ 寻路图完全不受影响，敌人不会为了绕开沙袋而改道。
    /// 与 <see cref="IsSolid"/> 一起构成"摆不出 kill box"的硬保证。
    /// </summary>
    public const bool CarvesNavHole = false;

    /// <summary>躲在其后的远程无伤概率（同其它半身掩体，25%，拟定待调）。</summary>
    public const float CoverChance = CoverLogic.DefaultCoverChance;

    /// <summary>
    /// 沙袋<b>不阻断近战</b>（区别于围栏）：它是矮物，绕过去/跨过去就能贴身砍。
    /// 「不许隔着围栏近战」只针对围栏那层网。
    /// </summary>
    public const bool BlocksMelee = false;

    /// <summary>摆放时与既有实心障碍/沙袋之间要留的余隙（像素，防止贴着墙面塞进去、视觉穿模）。</summary>
    public const float Clearance = 2f;

    /// <summary>一处轴对齐矩形（摆放校验用；避开 Godot.Rect2 以保零依赖）。</summary>
    public readonly record struct Box(float X, float Y, float W, float H)
    {
        public float MaxX => X + W;
        public float MaxY => Y + H;

        /// <summary>与另一矩形是否重叠（含 <paramref name="clearance"/> 余隙外扩）。</summary>
        public bool Overlaps(in Box o, float clearance = 0f) =>
            X - clearance < o.MaxX && MaxX + clearance > o.X &&
            Y - clearance < o.MaxY && MaxY + clearance > o.Y;

        /// <summary>是否完全落在 <paramref name="bounds"/> 内。</summary>
        public bool InsideOf(in Box bounds) =>
            X >= bounds.X && Y >= bounds.Y && MaxX <= bounds.MaxX && MaxY <= bounds.MaxY;
    }

    /// <summary>以 <paramref name="center"/> 为中心的沙袋占地矩形。</summary>
    public static Box BoxAt(Vector2 center) =>
        new(center.X - Width / 2f, center.Y - Height / 2f, Width, Height);

    /// <summary>摆放被拒的原因（供 UI 出提示；<see cref="PlacementResult.Ok"/> = 可以放）。</summary>
    public enum PlacementResult
    {
        Ok,
        /// <summary>超出营地边界。</summary>
        OutOfBounds,
        /// <summary>压在墙/建筑/围栏/工作台一类实心物上。</summary>
        BlockedBySolid,
        /// <summary>压在另一垛沙袋上。</summary>
        OverlapsSandbag,
    }

    /// <summary>
    /// 能不能把一垛沙袋摆在 <paramref name="center"/>。
    ///
    /// <para><b>注意这里校验的是"摆得下吗"，不是"会不会挡路"</b>——沙袋<b>永远</b>不挡路
    /// （<see cref="IsSolid"/>=false）。之所以不许压在实心物上，纯粹是因为一垛沙袋不该长在墙里/桌子里
    /// （视觉穿模，且躲在"墙里的沙袋"后面毫无意义）。</para>
    ///
    /// <paramref name="solids"/> = 场上实心障碍（墙/建筑/围栏/大门/实心道具，即 CampMain 的 _navHoles 那批）。
    /// <paramref name="sandbags"/> = 已摆好的沙袋。
    /// </summary>
    public static PlacementResult CanPlace(Vector2 center, in Box bounds,
        IReadOnlyList<Box> solids, IReadOnlyList<Box> sandbags)
    {
        Box box = BoxAt(center);

        if (!box.InsideOf(bounds))
            return PlacementResult.OutOfBounds;

        for (int i = 0; i < solids.Count; i++)
        {
            if (box.Overlaps(solids[i], Clearance))
                return PlacementResult.BlockedBySolid;
        }

        for (int i = 0; i < sandbags.Count; i++)
        {
            if (box.Overlaps(sandbags[i], Clearance))
                return PlacementResult.OverlapsSandbag;
        }

        return PlacementResult.Ok;
    }

    /// <summary>摆放被拒时给玩家的一行提示（黑色幽默文风，同物品描述）。</summary>
    public static string RejectionText(PlacementResult r) => r switch
    {
        PlacementResult.OutOfBounds => "那已经是营地外面了——沙袋是拿来挡枪的，不是拿来标地界的。",
        PlacementResult.BlockedBySolid => "那儿已经有东西了。沙袋摞在墙里，谁也躲不进去。",
        PlacementResult.OverlapsSandbag => "这儿已经有一垛了。堆两层不会让你多挡两成——只会让你更难趴下。",
        _ => "",
    };
}
