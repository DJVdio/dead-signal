using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 Item.cs / InventoryStore.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 营地容器搜刮的两块纯逻辑：①容器掉落登记 + 一次性搜刮语义（ContainerLoot）；
// ②把一批掉落落地到共享库存/食物/书登记（LootApplication）。CampMain 只做 Godot 侧的点击命中与面板弹出。

/// <summary>掉落物的类别（对应 <see cref="ItemCategory"/>，另加食物走份数、工具走工作台安装）。</summary>
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

    /// <summary>材料：<see cref="LootItem.RefId"/> = 材料标识名（<see cref="MaterialDef.Key"/>）；<see cref="LootItem.Quantity"/> = 堆叠数。入 <see cref="InventoryStore"/> 作 <see cref="Item.Material"/>。</summary>
    Material,

    /// <summary>工具：<see cref="LootItem.RefId"/> = 工具标识名（calipers/sawblade/beaker，见 <see cref="ContainerLoot.ParseToolSlot"/>）。搜到即装进营地共享工作台对应槽，解锁该类配方。</summary>
    Tool,
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

    /// <summary>造一堆材料掉落（<paramref name="materialKey"/> = 材料标识名，<paramref name="quantity"/> = 堆叠数，clamp 到 ≥1）。</summary>
    public static LootItem Material(string materialKey, int quantity = 1) => new(LootKind.Material, Math.Max(1, quantity), materialKey);

    /// <summary>造一件工具掉落（<paramref name="toolKey"/> = calipers/sawblade/beaker）。搜到即装进工作台对应槽。</summary>
    public static LootItem Tool(string toolKey) => new(LootKind.Tool, 1, toolKey);
}

/// <summary>
/// 容器掉落登记 + 一次性搜刮：每个 loot 容器登记一份藏物清单，<see cref="Search"/> 首次返回清单并标记已搜，
/// 再搜返回空（不重复产出）。已搜标记只在本类内存（一局有效）；storage 类容器不用它（营地共享库存另存）。
/// </summary>
public sealed class ContainerLoot
{
    private readonly Dictionary<string, List<LootItem>> _tables = new();
    private readonly HashSet<string> _searched = new();
    private readonly HashSet<string> _partial = new();   // 动过但没搜完（逐件搜刮中途走人）

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

    /// <summary>
    /// 注销一个容器（清掉它的藏物清单与已搜标记）。给<b>动态容器</b>用——尸体是一次性的、会被回收
    /// （<c>CorpseYard</c> 场上上限 240 具，超限淘汰最老的），若只从场景里摘掉节点而不在这里注销，
    /// 一局打下来几百具尸体的清单会永远留在字典里。营地里那些**固定**容器（储物柜/废墟/祖母的尸体）
    /// 一局到底，不用调它。
    /// </summary>
    public void Remove(string container)
    {
        if (string.IsNullOrEmpty(container))
        {
            return;
        }
        _tables.Remove(container);
        _searched.Remove(container);
        _partial.Remove(container);
    }

    /// <summary>该容器是否已被搜过（搜过则不再产出）。</summary>
    public bool IsSearched(string container) => container != null && _searched.Contains(container);

    /// <summary>
    /// 搜一个容器：首次返回其藏物清单并标记已搜；已搜或未登记返回空列表。
    /// <para>
    /// ⚠️ 这是**一次性语义**（一把抽干）。玩家可搜刮点已改为**逐件搜刮**（见 <see cref="LootSession"/> /
    /// <see cref="TakeNext"/>），本方法只留给"不该有暴露时间"的路径——开局 storage 容器整批入库、
    /// 以及不经玩家站桩的系统性投放。**玩家点开的容器一律不要走这里**，否则"点一下全拿走"就复活了。
    /// </para>
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

    /// <summary>
    /// 还留在容器里的件（逐件搜刮的事实源）。未登记/已搜空返回空列表。
    /// <see cref="LootSession"/> 开工时拿它作起始清单，中途走开则剩下的**原样留在这里**——回来接着搜。
    /// </summary>
    public IReadOnlyList<LootItem> Remaining(string container)
        => container != null && !_searched.Contains(container) && _tables.TryGetValue(container, out List<LootItem>? loot)
            ? loot
            : Array.Empty<LootItem>();

    /// <summary>还剩几件没拿（悬停提示用："还剩 3 件"）。</summary>
    public int RemainingCount(string container) => Remaining(container).Count;

    /// <summary>已经动过、但没搜完（悬停提示据此区分"没搜过"/"搜了一半"/"搜过了"）。</summary>
    public bool IsPartiallySearched(string container)
        => container != null && _partial.Contains(container) && !_searched.Contains(container);

    /// <summary>
    /// 从容器里**转出一件**（逐件搜刮的实扣入口，由 <see cref="LootSession"/> 报出件后调用）：
    /// 弹出清单头一件返回；容器**被拿空的那一刻**才标记已搜。已搜/未登记/已空返回 <c>null</c>。
    /// <para>拿到一半跑掉 ⇒ 剩下的还在清单里、容器不算已搜 —— 这正是"回头再来搜完"成立的地方。</para>
    /// </summary>
    public LootItem? TakeNext(string container)
    {
        if (container == null || _searched.Contains(container)
            || !_tables.TryGetValue(container, out List<LootItem>? loot) || loot.Count == 0)
        {
            return null;
        }

        LootItem taken = loot[0];
        loot.RemoveAt(0);
        _partial.Add(container);
        if (loot.Count == 0)
        {
            _searched.Add(container); // 拿空了才算搜过
        }
        return taken;
    }

    // ---- 存档：搜刮进度的事实源 ----

    /// <summary>
    /// 导出全部容器的<b>剩余</b>藏物（存档用）。<see cref="TakeNext"/> 是从这份清单里弹件的，
    /// 所以"这个柜子被搜了一半"这件事**天然就在这里**——不需要另外记账。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<LootItem>> SnapshotTables()
        => _tables.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<LootItem>)kv.Value.ToList());

    /// <summary>已搜空的容器（存档用）。</summary>
    public IReadOnlyCollection<string> SnapshotSearched() => _searched.ToList();

    /// <summary>动过但没搜完的容器（存档用；悬停提示"搜了一半"靠它）。</summary>
    public IReadOnlyCollection<string> SnapshotPartial() => _partial.ToList();

    /// <summary>读档：整体覆盖三份账（先清空再灌入）。</summary>
    public void Restore(
        IEnumerable<KeyValuePair<string, IReadOnlyList<LootItem>>> tables,
        IEnumerable<string> searched,
        IEnumerable<string> partial)
    {
        _tables.Clear();
        _searched.Clear();
        _partial.Clear();
        foreach (var kv in tables)
        {
            _tables[kv.Key] = kv.Value.ToList();
        }
        foreach (string s in searched)
        {
            _searched.Add(s);
        }
        foreach (string p in partial)
        {
            _partial.Add(p);
        }
    }

    /// <summary>工具标识名 → 工作台槽（calipers/sawblade/beaker，大小写不敏感）；未知返回 <c>null</c>。</summary>
    public static ToolSlot? ParseToolSlot(string? toolKey) => toolKey?.Trim().ToLowerInvariant() switch
    {
        "calipers" or "卡尺" => ToolSlot.Calipers,
        "sawblade" or "saw_blade" or "锯片" => ToolSlot.SawBlade,
        "beaker" or "烧杯" => ToolSlot.Beaker,
        _ => null,
    };
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
    /// <param name="toolsFound">
    /// 可选：工具槽收集器。<see cref="LootKind.Tool"/> 掉落解析出的 <see cref="ToolSlot"/> 加进这里，
    /// 由调用方（营地）装进共享工作台。为 <c>null</c> 时工具掉落被忽略（无工作台的语境）。
    /// </param>
    public static int Apply(
        IReadOnlyList<LootItem> loot,
        InventoryStore inventory,
        IDictionary<string, BookData> bookRegistry,
        Func<string, BookData?> bookResolver,
        ICollection<ToolSlot>? toolsFound = null)
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
                case LootKind.Material:
                    // 材料落地为库存材料堆；显示名/描述取自目录，目录外的键退化为用键本身作显示名。
                    MaterialDef? def = Materials.Find(l.RefId);
                    inventory.Add(def is { } d
                        ? d.ToItem(Math.Max(1, l.Quantity))
                        : Item.Material(l.RefId, l.RefId, Math.Max(1, l.Quantity)));
                    break;
                case LootKind.Tool:
                    if (ContainerLoot.ParseToolSlot(l.RefId) is { } slot)
                    {
                        toolsFound?.Add(slot);
                    }
                    break;
            }
        }
        return food;
    }
}
