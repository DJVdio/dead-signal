using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 Item.cs / InventoryStore.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 营地容器搜刮的两块纯逻辑：①容器掉落登记 + 一次性搜刮语义（ContainerLoot）；
// ②把一批掉落落地到共享库存/食物/书登记（LootApplication）。CampMain 只做 Godot 侧的点击命中与面板弹出。

/// <summary>掉落物的四类（对应 <see cref="ItemCategory"/>，另加食物走份数）。</summary>
public enum LootKind
{
    /// <summary>食物：按份数计，搜到后加进 <c>CampResources.Food</c>（不长留库存）。</summary>
    Food,

    /// <summary>书：<see cref="LootItem.RefId"/> = 书 id（<see cref="BookData.Id"/>）。</summary>
    Book,

    /// <summary>武器：<see cref="LootItem.RefId"/> = 武器名。</summary>
    Weapon,

    /// <summary>护甲：<see cref="LootItem.RefId"/> = 护甲名。</summary>
    Armor,
}

/// <summary>
/// 容器藏物清单里的一条掉落（不可变值对象）。食物用 <see cref="Quantity"/> 记份数；
/// 武器/护甲/书用 <see cref="RefId"/> 指向底层数据（武器名 / 护甲名 / 书 id）。构造走静态工厂保证一致。
/// </summary>
public readonly record struct LootItem(LootKind Kind, int Quantity, string RefId)
{
    /// <summary>造一堆食物掉落（<paramref name="quantity"/> 份，clamp 到 ≥0）。</summary>
    public static LootItem Food(int quantity) => new(LootKind.Food, Math.Max(0, quantity), "");

    /// <summary>造一本书掉落（<paramref name="bookId"/> = 书 id）。</summary>
    public static LootItem Book(string bookId) => new(LootKind.Book, 1, bookId);

    /// <summary>造一件武器掉落（<paramref name="weaponName"/> = 武器名）。</summary>
    public static LootItem Weapon(string weaponName) => new(LootKind.Weapon, 1, weaponName);

    /// <summary>造一件护甲掉落（<paramref name="armorName"/> = 护甲名）。</summary>
    public static LootItem Armor(string armorName) => new(LootKind.Armor, 1, armorName);
}

/// <summary>
/// 容器掉落登记 + 一次性搜刮：每个 loot 容器登记一份藏物清单，<see cref="Search"/> 首次返回清单并标记已搜，
/// 再搜返回空（不重复产出）。已搜标记只在本类内存（一局有效）；storage 类容器不用它（营地共享库存另存）。
/// </summary>
public sealed class ContainerLoot
{
    private readonly Dictionary<string, List<LootItem>> _tables = new();
    private readonly HashSet<string> _searched = new();

    /// <summary>登记一个容器的藏物清单（按容器名，重复登记覆盖）。空名忽略。</summary>
    public void Register(string container, IEnumerable<LootItem> loot)
    {
        if (string.IsNullOrEmpty(container))
        {
            return;
        }
        _tables[container] = loot?.ToList() ?? new List<LootItem>();
    }

    /// <summary>该容器是否已登记藏物清单。</summary>
    public bool Has(string container) => container != null && _tables.ContainsKey(container);

    /// <summary>该容器是否已被搜过（搜过则不再产出）。</summary>
    public bool IsSearched(string container) => container != null && _searched.Contains(container);

    /// <summary>
    /// 搜一个容器：首次返回其藏物清单并标记已搜；已搜或未登记返回空列表。
    /// </summary>
    public IReadOnlyList<LootItem> Search(string container)
    {
        if (container == null || !_tables.TryGetValue(container, out List<LootItem>? loot) || _searched.Contains(container))
        {
            return Array.Empty<LootItem>();
        }
        _searched.Add(container);
        return loot;
    }
}

/// <summary>
/// 把一批掉落落地到营地：武器/护甲/书作 <see cref="Item"/> 入 <see cref="InventoryStore"/>，
/// 书另把 <see cref="BookData"/> 实例登记到 registry（供阅读面板共享已读态），食物累加份数返回（由调用方加进 CampResources.Food）。
/// 纯函数，无 Godot 依赖，可独立测试。
/// </summary>
public static class LootApplication
{
    /// <summary>
    /// 落地一批掉落。<paramref name="bookResolver"/> 按书 id 取 <see cref="BookData"/> 实例（须返回同一实例以共享已读态）；
    /// 解析不到的书 id 跳过。返回本批食物份数合计。
    /// </summary>
    public static int Apply(
        IReadOnlyList<LootItem> loot,
        InventoryStore inventory,
        IDictionary<string, BookData> bookRegistry,
        Func<string, BookData?> bookResolver)
    {
        int food = 0;
        foreach (LootItem l in loot ?? Array.Empty<LootItem>())
        {
            switch (l.Kind)
            {
                case LootKind.Food:
                    food += Math.Max(0, l.Quantity);
                    break;
                case LootKind.Weapon:
                    inventory.Add(Item.Weapon(l.RefId));
                    break;
                case LootKind.Armor:
                    inventory.Add(Item.Armor(l.RefId));
                    break;
                case LootKind.Book:
                    BookData? bd = bookResolver(l.RefId);
                    if (bd != null)
                    {
                        bookRegistry[bd.Id] = bd;
                        inventory.Add(bd.ToItem());
                    }
                    break;
            }
        }
        return food;
    }
}
