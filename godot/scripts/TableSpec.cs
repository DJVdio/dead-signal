namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 玩家可建造、可自由摆放的<b>桌子</b>规格（批次21·T25）。<b>整体照抄 <see cref="BedSpec"/> 的形态</b> ——
/// 同一条链路（配方产出一件"桌子" → 库存「摆放」→ 左键落位建成 → 可拆走重摆），不发明新的建造范式。
///
/// <para>═══ <b>桌子 = 室内的半身掩体</b>（用户拍板）═══
/// 用户的掩体原话是「<b>躲在桌子/椅子/沙袋后</b>，被【远程】攻击有无伤概率」—— 桌子是他<b>点了名的掩体</b>，
/// 玩家自己造的那张也算（<see cref="CoverChance"/> 与沙袋同档，数值以 Wiki 配置为准；<b>不拦近战</b>：矮物，绕过去就能砍）。
/// <para>
/// <b>那它会不会变成沙袋的廉价替代品？——不会，用户同时拍了另一条规则：</b>
/// 「<b>家具不能放到室外</b>；<b>沙袋能放到室内和室外</b>」（缓冲带/室内外判定归 <see cref="PlacementRules"/>）。
/// ⇒ <b>桌子只摆得进屋里</b>（室内掩体，打巷战用）、<b>沙袋是唯一能垒在门口和院子里的掩体</b>（守门用）。
/// 两者<b>各有各的场，不互相替代</b>。
/// </para>
/// <para>
/// <b>桌子仍然没有"家具功能"</b>：营地里没有"桌子"这个概念（聚餐是模态相位、<c>camp.json</c> 无此 prop、
/// 本作<b>没有心情系统</b>）⇒ 不许替用户编一个"吃饭 +心情"出来。它现在的全部作用就是<b>掩体 + 减速带</b>。
/// </para>
/// <para>
/// <b>床<u>不是</u>掩体</b>（别顺手"统一"）：用户原话点名的是桌子/椅子/沙袋，<b>没有床</b>。
/// 故 <see cref="BedSpec"/> 里压根没有掩体这两个字段 —— 想给床开，得先造出字段来（改不动是刻意的）。
/// </para>
/// </para>
///
/// <para>═══ <b>桌子凭什么能自由摆放</b>（同床/沙袋那条论证，别"统一"掉）═══
/// 用户拍板 <b>"墙不能建"</b> 是为了防 kill box。桌子和床一样<b>不阻挡移动、不改变寻路</b>
/// （<see cref="IsSolid"/> / <see cref="CarvesNavHole"/> 恒 false）—— 它是<b>减速带，不是墙</b>：
/// 谁都能跨过去，只是按配置减速（<see cref="FurnitureTraversal"/>）。摆不出迷宫，因为没有一格是走不通的。
/// 但它仍<b>守禁建带</b>（<see cref="PlacementRules"/>）：墙根下那条道得留给砌墙的人和逃命的人。
/// </para>
/// </summary>
public static class TableSpec
{
    /// <summary>桌子配方 id（<see cref="RecipeBook"/>）。拆除返还也按这张配方的材料算（<see cref="SalvageLogic"/>）。</summary>
    public const string RecipeId = "table";

    /// <summary>库存里那件"桌子"的物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "table";

    /// <summary>家具类型名（<see cref="FurnitureBuildCost"/> 的键；场上实例名带流水号"桌子#1"）。</summary>
    public const string FurnitureKey = "桌子";

    /// <summary>库存里的物品描述（黑色幽默文风，同批次15 的物品级 flavor）。</summary>
    public const string ItemDescription =
        "一张桌子。四条腿，一个面。它什么也挡不住、什么也做不了——但把碗放在桌上吃，和蹲在地上啃，是两种活法。";

    /// <summary>一张桌子的占地（世界像素，拟定待调）：比木椅大、比床宽——围得下几个人吃饭的那种大小。</summary>
    public const float Width = 72f;
    public const float Height = 48f;

    /// <summary>
    /// <b>恒 false。</b>不建碰撞体 ⇒ 人跨得过去（跨过按配置减速，见 <see cref="FurnitureTraversal"/>）。
    /// 改成 true 之前请先回去读本类的类注：kill box 就是这么来的。
    /// </summary>
    public const bool IsSolid = false;

    /// <summary><b>恒 false。</b>不挖导航洞 ⇒ 寻路图不受影响。与 <see cref="IsSolid"/> 一起保证摆不出 kill box。</summary>
    public const bool CarvesNavHole = false;

    /// <summary>
    /// <b>半身掩体的无伤概率</b>（取 <see cref="CoverLogic.DefaultCoverChance"/> —— 与沙袋同档，不另立数值）。
    /// <para>
    /// <b>方向性 + 双向对称</b>照搬 <see cref="CoverLogic"/> 的既有规则：只有桌子落在射击者与目标的连线上才生效
    /// （敌人绕后就绕掉了），而且<b>敌人也能蹲在你的桌子后面</b>。桌子不新增任何例外。
    /// </para>
    /// </summary>
    public const float CoverChance = CoverLogic.DefaultCoverChance;

    /// <summary>
    /// <b>恒 false</b>：不拦近战。矮物，绕过去就能砍 —— 同沙袋/桌椅的既有口径（只有围栏 <c>BlocksMelee</c>）。
    /// </summary>
    public const bool BlocksMelee = false;

    /// <summary>放置规格（喂 <c>CampMain.CheckFurniturePlacement</c> ⇒ <see cref="PlacementRules.CanPlace"/>）。
    /// <b>不豁免防线缓冲带</b>（<c>AllowedAgainstDefenses</c> 取缺省 false）——沙袋是唯一的豁免，桌子不是掩体。</summary>
    public static PlaceableSpec PlaceSpec => new(FurnitureKey, Width, Height, IsSolid: IsSolid);

    /// <summary>
    /// 场上这件家具是不是<b>玩家摆的桌子</b>（实例名带流水号，如 "桌子#3"）。
    /// 存档只存玩家摆下的东西（<c>CampSave.PlacedFurniture</c>），故这是它的入表判据。
    /// <para>类型名 "桌子"（不带 '#'）返回 false —— 那是目录里的<b>类型</b>，不是场上的<b>实例</b>。</para>
    /// </summary>
    public static bool IsTableFurniture(string? furnitureName)
        => furnitureName is not null && furnitureName.StartsWith(FurnitureKey + "#");
}
