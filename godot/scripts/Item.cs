using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 物品模型：营地共享库存里的一件物品的不可变值对象。五类：食物 / 书 / 护甲 / 武器 / 材料。
// 只描述"这是什么、指向哪条底层数据"，不含装备/阅读/入库/配方消耗等行为——那些归上层（搜刮/装备/阅读/配方接入）。

/// <summary>物品类别（营地共享库存里可搜刮/持有之物）。</summary>
public enum ItemCategory
{
    /// <summary>食物：以份数计（1 份 = 1 人 1 餐），处置（加 CampResources.Food）留给 W3 搜刮接入。</summary>
    Food,

    /// <summary>书：可阅读物品，指向一条 <see cref="BookData"/>（按 <see cref="Item.RefKey"/> = 书 id 关联）。</summary>
    Book,

    /// <summary>护甲：可装备物品，指向 <c>ArmorTable</c> 的某甲组/甲层名（<see cref="Item.RefKey"/> = 护甲名）。</summary>
    Armor,

    /// <summary>武器：可装备物品，指向 <c>WeaponTable</c> 的某武器名（<see cref="Item.RefKey"/> = 武器名）。</summary>
    Weapon,

    /// <summary>
    /// 材料：配方系统的消耗物（成堆持有），指向一条 <see cref="MaterialDef"/>（<see cref="Item.RefKey"/> = 材料标识名）。
    /// 堆叠数量走 <see cref="Item.MaterialQuantity"/>；配方消耗/扣减留给配方波。
    /// </summary>
    Material,
}

/// <summary>
/// 营地共享库存里的一件物品（不可变值对象，record 值语义相等）。
/// 用 <see cref="RefKey"/> 指向底层数据：武器名 / 护甲名 / 书 id / 材料标识名（食物无引用，用 <see cref="FoodQuantity"/> 表数量）。
/// 构造只走静态工厂（<see cref="Weapon"/> / <see cref="Armor"/> / <see cref="Book"/> / <see cref="Food"/> / <see cref="Material"/>），保证类别与引用一致。
/// </summary>
public sealed record Item
{
    /// <summary>物品类别。</summary>
    public ItemCategory Category { get; }

    /// <summary>显示名（UI 用）。武器/护甲取其名，书取标题，食物取给定名。</summary>
    public string DisplayName { get; }

    /// <summary>描述文本（UI 用，可空串）。</summary>
    public string Description { get; }

    /// <summary>
    /// 指向底层数据的引用键：武器类=武器名（WeaponTable.Name）、护甲类=护甲名（ArmorTable.Name）、
    /// 书类=书 id（BookData.Id）、材料类=材料标识名（<see cref="MaterialDef.Key"/>）。
    /// 食物类为 <c>null</c>（用 <see cref="FoodQuantity"/> 表数量）。
    /// </summary>
    public string? RefKey { get; }

    /// <summary>仅食物类使用：这堆食物代表多少份口粮（≥0）。其余类别恒为 0。</summary>
    public int FoodQuantity { get; }

    /// <summary>仅材料类使用：这一堆材料的数量（≥0，配方消耗据此扣减）。其余类别恒为 0。</summary>
    public int MaterialQuantity { get; }

    private Item(ItemCategory category, string displayName, string description, string? refKey, int foodQuantity, int materialQuantity)
    {
        Category = category;
        DisplayName = displayName ?? string.Empty;
        Description = description ?? string.Empty;
        RefKey = refKey;
        FoodQuantity = foodQuantity;
        MaterialQuantity = materialQuantity;
    }

    /// <summary>造一件武器物品（<paramref name="weaponName"/> 即 WeaponTable 的武器名，同时作显示名与引用键）。</summary>
    public static Item Weapon(string weaponName, string description = "")
        => new(ItemCategory.Weapon, weaponName, description, weaponName, 0, 0);

    /// <summary>造一件护甲物品（<paramref name="armorName"/> 即 ArmorTable 的护甲名，同时作显示名与引用键）。</summary>
    public static Item Armor(string armorName, string description = "")
        => new(ItemCategory.Armor, armorName, description, armorName, 0, 0);

    /// <summary>造一件书物品（引用键=书 id；显示名=书标题）。已读标记/正文在 <see cref="BookData"/> 上，本对象只做引用。</summary>
    public static Item Book(string bookId, string title, string description = "")
        => new(ItemCategory.Book, title, description, bookId, 0, 0);

    /// <summary>造一堆食物物品（<paramref name="quantity"/> 份，clamp 到 ≥0）。处置（加 CampResources.Food）留给 W3。</summary>
    public static Item Food(int quantity, string displayName = "食物", string description = "")
        => new(ItemCategory.Food, displayName, description, null, Math.Max(0, quantity), 0);

    /// <summary>
    /// 造一堆材料物品（<paramref name="materialKey"/> = 材料标识名，作引用键指向 <see cref="MaterialDef"/>；
    /// <paramref name="quantity"/> = 堆叠数，clamp 到 ≥0）。通常由 <see cref="MaterialDef.ToItem"/> 从目录构造。配方消耗留给配方波。
    /// </summary>
    public static Item Material(string materialKey, string displayName, int quantity = 1, string description = "")
        => new(ItemCategory.Material, displayName, description, materialKey, 0, Math.Max(0, quantity));

    /// <summary>是否可装备（武器或护甲）。W3 装备接入据此从库存筛可上身之物。</summary>
    public bool IsEquippable => Category is ItemCategory.Weapon or ItemCategory.Armor;
}
