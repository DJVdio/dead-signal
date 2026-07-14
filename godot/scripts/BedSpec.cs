using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 玩家可建造、可自由摆放的<b>床</b>规格（批次21·impl-bedrest）。**整体照抄 <see cref="SandbagSpec"/> 的形态**——
/// 同一条链路（配方产出一件"床" → 库存「摆放」→ 左键落位建成），不发明新的建造范式。
///
/// <para>═══ <b>床凭什么能自由摆放？（同沙袋那条论证，别"统一"掉）</b> ═══
/// 用户拍板 <b>"墙不能建"</b> 是为了防 kill box：能砌实心墙就能用迷宫牵着敌人的寻路走。
/// 床和沙袋一样<b>不阻挡移动、不改变寻路</b>（<see cref="IsSolid"/> / <see cref="CarvesNavHole"/> 恒 false）——
/// 你得能<b>走到床上去躺下</b>，它要是实心的，伤员就被自己的床挡在门外了。
/// ⇒ 摆不出 kill box，故获准自由摆放。谁把它改成实心的，kill box 就回来了。</para>
///
/// <para>床<b>不是掩体</b>（区别于沙袋）：躲在床后面挡不了枪。<see cref="CoverLogic"/> 不登记它。</para>
///
/// <para>开局营地只有 2 张（camp.json 的 床#1/床#2），第三张起要照配方 <see cref="RecipeId"/> 造。
/// 稀缺是有意的：一张床只躺一个人（<see cref="BedRegistry"/>），躺着的那个不站岗不生产（<see cref="BedrestLogic"/>）
/// —— 玩家得反复决定"这张床今晚归谁"。</para>
/// </summary>
public static class BedSpec
{
    /// <summary>床配方 id（<see cref="RecipeBook"/>）。拆除返还也按这张配方的材料算（<see cref="SalvageLogic"/>）。</summary>
    public const string RecipeId = "bed";

    /// <summary>库存里那件"床"的物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "bed";

    /// <summary>家具类型名（<see cref="FurnitureBuildCost"/> 的键；场上实例名带流水号"床#3"）。</summary>
    public const string FurnitureKey = "床";

    /// <summary>库存里的物品描述（黑色幽默文风，同批次15 的物品级 flavor）。</summary>
    public const string ItemDescription =
        "一张床。在这个世界上，能躺平地睡一觉已经算是一种奢侈的医疗手段了——而且它确实管用。";

    /// <summary>一张床的占地（世界像素，拟定待调）：躺得下一个人，比沙袋长、比工作台窄。与 camp.json 里那两张开局床同尺寸。</summary>
    public const float Width = 68f;
    public const float Height = 40f;

    /// <summary>
    /// <b>恒 false。</b>不建碰撞体 ⇒ 人走得上去躺下。床要是实心的，伤员就被自己的床挡在外面了。
    /// 改成 true 之前请先回去读本类的类注：kill box 就是这么来的。
    /// </summary>
    public const bool IsSolid = false;

    /// <summary><b>恒 false。</b>不挖导航洞 ⇒ 寻路图不受影响。与 <see cref="IsSolid"/> 一起保证摆不出 kill box。</summary>
    public const bool CarvesNavHole = false;

    /// <summary>摆放时与既有实心障碍/其它床之间要留的余隙（像素）。</summary>
    public const float Clearance = 2f;

    /// <summary>以 <paramref name="center"/> 为中心的床占地矩形（复用 <see cref="SandbagSpec.Box"/>，同一套矩形代数）。</summary>
    public static SandbagSpec.Box BoxAt(Vector2 center) =>
        new(center.X - Width / 2f, center.Y - Height / 2f, Width, Height);

    /// <summary>摆放被拒的原因（供 UI 出提示；<see cref="PlacementResult.Ok"/> = 可以放）。</summary>
    public enum PlacementResult
    {
        Ok,
        /// <summary>超出营地边界。</summary>
        OutOfBounds,
        /// <summary>压在墙/建筑/围栏/工作台一类实心物上。</summary>
        BlockedBySolid,
        /// <summary>压在另一张床（或沙袋）上。</summary>
        OverlapsFurniture,
    }

    /// <summary>
    /// 能不能把一张床摆在 <paramref name="center"/>。校验的是"摆得下吗"——床**永远**不挡路（<see cref="IsSolid"/>=false），
    /// 不许压在实心物上纯粹是因为一张床不该长在墙里（视觉穿模，且躺在"墙里的床"上也走不过去）。
    /// </summary>
    /// <param name="solids">场上实心障碍（墙/建筑/围栏/大门/实心道具）。</param>
    /// <param name="occupied">已摆好的床与沙袋（都是贴地非实心物，互相不该重叠）。</param>
    public static PlacementResult CanPlace(Vector2 center, in SandbagSpec.Box bounds,
        IReadOnlyList<SandbagSpec.Box> solids, IReadOnlyList<SandbagSpec.Box> occupied)
    {
        SandbagSpec.Box box = BoxAt(center);

        if (!box.InsideOf(bounds))
        {
            return PlacementResult.OutOfBounds;
        }

        for (int i = 0; i < solids.Count; i++)
        {
            if (box.Overlaps(solids[i], Clearance))
            {
                return PlacementResult.BlockedBySolid;
            }
        }

        for (int i = 0; i < occupied.Count; i++)
        {
            if (box.Overlaps(occupied[i], Clearance))
            {
                return PlacementResult.OverlapsFurniture;
            }
        }

        return PlacementResult.Ok;
    }

    /// <summary>摆放被拒时给玩家的一行提示（黑色幽默文风，同物品描述）。</summary>
    public static string RejectionText(PlacementResult r) => r switch
    {
        PlacementResult.OutOfBounds => "那是营地外面。在围栏外头睡觉，你的伤会好得比预期快——因为你不会再有伤了。",
        PlacementResult.BlockedBySolid => "那儿已经有东西了。床摆在墙里，人躺不进去。",
        PlacementResult.OverlapsFurniture => "这儿已经占了。两张床叠在一起不会让人睡得更香。",
        _ => "",
    };
}
