using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 营地共享库存（用户拍板：背包=营地共享库存，不做每人背包）。只持物品 + 增删查，纯数据地基。
// 本块不接入 CampMain：搜刮入库 / 装备 / 阅读 / 食物加进 CampResources.Food 均为 W3 接入的事。

/// <summary>
/// 营地共享库存：一个 <see cref="Item"/> 列表 + 增删按类别查询。纯 C# 可独立测试。
/// 搜到的物品进这里；W3 再据此装备（<see cref="Equippable"/>）、阅读（<see cref="Books"/>）、
/// 或把食物份数（<see cref="Foods"/> / <see cref="TotalFood"/>）加进 <c>CampResources.Food</c>。
/// </summary>
public sealed class InventoryStore
{
    private readonly List<Item> _items = new();

    /// <summary>当前全部物品（只读视图，按加入顺序）。</summary>
    public IReadOnlyList<Item> Items => _items;

    /// <summary>物品件数。</summary>
    public int Count => _items.Count;

    /// <summary>入库一件物品（追加到末尾）。</summary>
    public void Add(Item item) => _items.Add(item);

    /// <summary>批量入库（按给定顺序追加）。</summary>
    public void AddRange(IEnumerable<Item> items) => _items.AddRange(items);

    /// <summary>移除一件物品（按 <see cref="Item"/> 值相等匹配首个），成功返回 <c>true</c>。</summary>
    public bool Remove(Item item) => _items.Remove(item);

    /// <summary>按类别筛选。</summary>
    public IEnumerable<Item> ByCategory(ItemCategory category) => _items.Where(i => i.Category == category);

    /// <summary>全部武器物品。</summary>
    public IEnumerable<Item> Weapons => ByCategory(ItemCategory.Weapon);

    /// <summary>全部护甲物品。</summary>
    public IEnumerable<Item> Armors => ByCategory(ItemCategory.Armor);

    /// <summary>全部书物品（W3 阅读接入据此列可读物）。</summary>
    public IEnumerable<Item> Books => ByCategory(ItemCategory.Book);

    /// <summary>全部食物物品（W3 搜刮/进食接入据此把份数加进 CampResources.Food）。</summary>
    public IEnumerable<Item> Foods => ByCategory(ItemCategory.Food);

    /// <summary>全部可装备物品（武器 + 护甲）。W3 装备接入据此筛可上身之物。</summary>
    public IEnumerable<Item> Equippable => _items.Where(i => i.IsEquippable);

    /// <summary>库存里食物份数合计（供 W3 参考；本块不改 CampResources）。</summary>
    public int TotalFood => _items.Where(i => i.Category == ItemCategory.Food).Sum(i => i.FoodQuantity);

    /// <summary>
    /// 某材料标识键（<see cref="MaterialDef.Key"/>）在库存里的总量：所有该键材料堆的 <see cref="Item.MaterialQuantity"/> 之和。
    /// 货币（白银）持有量即 <c>MaterialCount(Materials.CurrencyKey)</c>；配方够不够付亦可用它。
    /// </summary>
    public int MaterialCount(string key)
        => key == null ? 0 : _items.Where(i => i.Category == ItemCategory.Material && i.RefKey == key).Sum(i => i.MaterialQuantity);

    /// <summary>
    /// 从库存实扣某材料键 <paramref name="amount"/> 个（跨多堆合计扣减）：总量不足则原样不动返回 <c>false</c>；
    /// 足量则按加入顺序逐堆扣（整堆扣光则移除，部分扣则替换为余量堆），返回 <c>true</c>。
    /// 交易付款（扣白银）、配方消耗材料均走此实扣路径。<paramref name="amount"/> ≤ 0 视作无操作返回 <c>true</c>。
    /// </summary>
    public bool TrySpendMaterial(string key, int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (key == null || MaterialCount(key) < amount)
        {
            return false;
        }

        int remaining = amount;
        for (int i = 0; i < _items.Count && remaining > 0; i++)
        {
            Item it = _items[i];
            if (it.Category != ItemCategory.Material || it.RefKey != key)
            {
                continue;
            }

            if (it.MaterialQuantity <= remaining)
            {
                remaining -= it.MaterialQuantity;
                _items.RemoveAt(i);
                i--; // 该槽已移除，回退以复查新占位的下一件
            }
            else
            {
                _items[i] = Item.Material(key, it.DisplayName, it.MaterialQuantity - remaining, it.Description);
                remaining = 0;
            }
        }

        return true;
    }
}
