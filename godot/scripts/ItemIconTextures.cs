using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 物品图标的 Godot 侧加载器（<see cref="ItemIcons"/> 出路径，本类把路径变成能挂到 UI 上的纹理）。
///
/// <para>
/// <b>为什么要兜底</b>：映射表（<see cref="ItemIcons"/>）是全的（103 条），但磁盘上的 PNG 是**按需生成**的
/// （<c>tools/icons/build_icons.sh</c> 一次只补它被要求补的那些）。所以"有映射、没图片"是正常状态而非 bug——
/// 这时一律回退占位图，让物品照样能在库存里列出来，而不是崩在 <c>ResourceLoader.Load</c> 上或显示一片空白。
/// </para>
///
/// <para>
/// 纹理按路径缓存：库存面板每次刷新都重建一堆行，逐行 Load 同一张 32×32 是白费的。
/// </para>
/// </summary>
public static class ItemIconTextures
{
    /// <summary>UI 里物品图标的标准显示边长（像素）。图标本体是 32×32，按整数倍显示才不糊。</summary>
    public const int DisplaySize = 32;

    private static readonly Dictionary<string, Texture2D?> _cache = new();

    /// <summary>
    /// 按一件库存物品取图标纹理。查不到映射、或 PNG 尚未生成 ⇒ 回退占位图；连占位图都没有 ⇒ 返回 <c>null</c>
    /// （调用方据此不挂图标，UI 仍正常）。
    /// </summary>
    public static Texture2D? For(Item item) => Load(ItemIcons.PathFor(item));

    /// <summary>按物品引用键取图标纹理（武器/护甲=中文名，材料/光源=英文 key，书=书 id）。</summary>
    public static Texture2D? ForRefKey(string? refKey) => Load(ItemIcons.PathFor(refKey));

    private static Texture2D? Load(string path)
    {
        if (_cache.TryGetValue(path, out Texture2D? cached))
        {
            return cached;
        }

        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex is null && path != ItemIcons.PlaceholderPath)
        {
            tex = Load(ItemIcons.PlaceholderPath);
        }

        _cache[path] = tex;
        return tex;
    }

    /// <summary>
    /// 按一件**搜刮掉落**取图标纹理。<see cref="LootItem"/> 不是 <see cref="Item"/>：它按 <see cref="LootItem.RefId"/>
    /// 指向底层数据（武器/护甲=中文名、材料=材料键、书=书 id、工具=工具键），而食物掉落的 RefId 是空串
    /// （食物按份数计，不指向任何目录条目）——所以食物要转查 <see cref="ItemIcons.FoodRefKey"/>，
    /// 否则一背包的食物会全变成占位图。
    /// </summary>
    public static Texture2D? ForLoot(LootItem loot)
        => ForRefKey(loot.Kind == LootKind.Food ? ItemIcons.FoodRefKey : loot.RefId);

    /// <summary>
    /// 造一个挂好纹理的图标控件（默认 32×32，最近邻放大不糊）。物品没有可用图标时返回一个等宽的空占位控件，
    /// 好让列表里各行的文字仍然对齐——图标缺失不该让整列文字错位。
    /// <paramref name="size"/> 可调小（角色面板的行比库存行紧凑，32px 会把行撑高）。
    /// </summary>
    public static Control MakeIcon(Item item, int size = DisplaySize) => Make(For(item), size);

    /// <summary>造一个挂好纹理的图标控件（按物品引用键：武器/护甲=中文名，材料/光源=英文 key，书=书 id）。</summary>
    public static Control MakeIconForRefKey(string? refKey, int size = DisplaySize) => Make(ForRefKey(refKey), size);

    /// <summary>造一个挂好纹理的图标控件（按搜刮掉落，见 <see cref="ForLoot"/>）。</summary>
    public static Control MakeIconForLoot(LootItem loot, int size = DisplaySize) => Make(ForLoot(loot), size);

    /// <summary>
    /// 造一个挂好纹理的图标控件（按**配方产物**，见 <see cref="ItemIcons.PathForOutput"/>——
    /// 武器/护甲的产物键是内部英文键而引用键是中文名，得两头都试）。
    /// </summary>
    public static Control MakeIconForOutput(string? outputKey, string? displayName, int size = DisplaySize)
        => Make(Load(ItemIcons.PathForOutput(outputKey, displayName)), size);

    private static Control Make(Texture2D? tex, int size)
    {
        if (tex is null)
        {
            return new Control { CustomMinimumSize = new Vector2(size, size) };
        }

        return new TextureRect
        {
            Texture = tex,
            CustomMinimumSize = new Vector2(size, size),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, // 像素图标：整数缩放 + 最近邻，禁线性插值
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
    }
}
