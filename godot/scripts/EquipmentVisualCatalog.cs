using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 纯 C# 视觉目录：不得引用 Godot 类型，供测试 Link 编译。

public enum EquipmentVisualKind
{
    Weapon,
    Light,
    Apparel,
    DogApparel,
}

public enum PaperDollLayer
{
    None,
    Back,
    Skin,
    Pants,
    Feet,
    Outer,
    Plate,
    Hands,
    Head,
    Eyes,
    Face,
    Front,
}

/// <summary>图块落在角色身上的方式；Face 与 Head 分开，保证面具/眼镜不盖发型。</summary>
public enum EquipmentVisualAnchor
{
    Held,
    Body,
    Head,
    Face,
    Hand,
    Foot,
    DogBody,
}

/// <summary>一件装备在共享 8 方向图集中的稳定登记。</summary>
public sealed record EquipmentVisualDef(
    string ItemKey,
    EquipmentVisualKind Kind,
    string AtlasPath,
    int AtlasRow,
    float DisplayScale,
    PaperDollLayer Layer,
    EquipmentVisualAnchor Anchor,
    int CellWidth = 96,
    int CellHeight = 96);

/// <summary>
/// 装备表现的单一登记入口。运行时只读角色的真实持械/穿戴/持光状态；目录只回答画哪张图、哪一行和哪一层。
/// 60 件正式批次＝22 件剩余武器 + 33 件人类穿戴 + 5 件布鲁斯装备；上一批 7 件手持物继续共用本目录。
/// </summary>
public static class EquipmentVisualCatalog
{
    public const string HeldAtlasPath = "res://assets/world/held-equipment-directions.png";
    public const string HeldAtlasBPath = "res://assets/world/held-equipment-b.png";
    public const string HeldAtlasCPath = "res://assets/world/held-equipment-c.png";
    public const string HeldAtlasDPath = "res://assets/world/held-equipment-d.png";
    public const string HeldFireAxePath = "res://assets/world/held-fire-axe.png";
    public const string HeldRapierPath = "res://assets/world/held-rapier.png";
    public const string HeldMilitaryRiflesPath = "res://assets/world/held-military-rifles.png";
    public const string ApparelBasePath = "res://assets/world/apparel-base.png";
    public const string ApparelOuterPath = "res://assets/world/apparel-outer.png";
    public const string ApparelSpecialPath = "res://assets/world/apparel-special.png";
    public const string ApparelHeadPath = "res://assets/world/apparel-head.png";
    public const string ApparelGlovePath = "res://assets/world/apparel-glove.png";
    public const string ApparelWarMaskPath = "res://assets/world/apparel-war-mask.png";
    public const string ApparelPlatePath = "res://assets/world/apparel-plate.png";
    public const string DogApparelPath = "res://assets/world/dog-apparel.png";
    public const string DogPocketHarnessPath = "res://assets/world/dog-pocket-harness.png";

    private static EquipmentVisualDef Held(string key, string atlas, int row, float scale)
        => new(key, EquipmentVisualKind.Weapon, atlas, row, scale, PaperDollLayer.Front, EquipmentVisualAnchor.Held);

    private static EquipmentVisualDef Worn(string key, string atlas, int row, PaperDollLayer layer,
        EquipmentVisualAnchor anchor = EquipmentVisualAnchor.Body, float scale = 1f, int cellWidth = 64, int cellHeight = 96)
        => new(key, EquipmentVisualKind.Apparel, atlas, row, scale, layer, anchor, cellWidth, cellHeight);

    private static readonly IReadOnlyDictionary<string, EquipmentVisualDef> Weapons =
        new Dictionary<string, EquipmentVisualDef>(StringComparer.Ordinal)
        {
            ["匕首"] = Held("匕首", HeldAtlasPath, 0, 0.62f),
            ["长剑"] = Held("长剑", HeldAtlasPath, 1, 1.00f),
            ["手枪"] = Held("手枪", HeldAtlasPath, 2, 0.64f),
            ["步枪"] = Held("步枪", HeldMilitaryRiflesPath, 0, 1.02f),
            ["长弓"] = Held("长弓", HeldAtlasPath, 4, 1.05f),

            ["短剑"] = Held("短剑", HeldAtlasBPath, 0, 0.82f),
            ["刺剑"] = Held("刺剑", HeldRapierPath, 0, 0.96f),
            ["重剑"] = Held("重剑", HeldAtlasBPath, 2, 1.12f),
            ["草叉"] = Held("草叉", HeldAtlasBPath, 3, 1.14f),
            ["消防斧"] = Held("消防斧", HeldFireAxePath, 0, 1.08f),
            ["骨刀"] = Held("骨刀", HeldAtlasBPath, 5, 0.68f),
            ["自制手枪"] = Held("自制手枪", HeldAtlasBPath, 6, 0.68f),
            ["牙医小手枪"] = Held("牙医小手枪", HeldAtlasBPath, 7, 0.56f),

            ["棍棒"] = Held("棍棒", HeldAtlasCPath, 0, 0.82f),
            ["尖头锤"] = Held("尖头锤", HeldAtlasCPath, 1, 0.88f),
            ["破甲锤"] = Held("破甲锤", HeldAtlasCPath, 2, 0.94f),
            ["自制猎枪"] = Held("自制猎枪", HeldAtlasCPath, 3, 1.05f),
            ["冲锋枪"] = Held("冲锋枪", HeldAtlasCPath, 4, 0.84f),
            ["狙击枪"] = Held("狙击枪", HeldMilitaryRiflesPath, 1, 1.14f),
            ["自制霰弹枪"] = Held("自制霰弹枪", HeldAtlasCPath, 6, 1.02f),
            ["短弓"] = Held("短弓", HeldAtlasCPath, 7, 0.86f),

            ["反曲弓"] = Held("反曲弓", HeldAtlasDPath, 0, 0.96f),
            ["竞技复合弓"] = Held("竞技复合弓", HeldAtlasDPath, 1, 1.00f),
            ["狩猎弓"] = Held("狩猎弓", HeldAtlasDPath, 2, 1.00f),
            ["单手轻弩"] = Held("单手轻弩", HeldAtlasDPath, 3, 0.82f),
            ["双手重弩"] = Held("双手重弩", HeldAtlasDPath, 4, 1.08f),
            ["复合弩"] = Held("复合弩", HeldAtlasDPath, 5, 1.02f),
        };

    private static readonly IReadOnlyDictionary<string, EquipmentVisualDef> Lights =
        new Dictionary<string, EquipmentVisualDef>(StringComparer.Ordinal)
        {
            [LightSource.FlashlightKey] = new(LightSource.FlashlightKey, EquipmentVisualKind.Light, HeldAtlasPath, 5, 0.52f, PaperDollLayer.Front, EquipmentVisualAnchor.Held),
            [LightSource.TorchKey] = new(LightSource.TorchKey, EquipmentVisualKind.Light, HeldAtlasPath, 6, 0.78f, PaperDollLayer.Front, EquipmentVisualAnchor.Held),
        };

    private static readonly IReadOnlyDictionary<string, EquipmentVisualDef> Apparel =
        new Dictionary<string, EquipmentVisualDef>(StringComparer.Ordinal)
        {
            ["长袖布衣"] = Worn("长袖布衣", ApparelBasePath, 0, PaperDollLayer.Skin),
            ["花衬衫"] = Worn("花衬衫", ApparelBasePath, 1, PaperDollLayer.Skin),
            ["长裤"] = Worn("长裤", ApparelBasePath, 2, PaperDollLayer.Pants),
            ["短裤"] = Worn("短裤", ApparelBasePath, 3, PaperDollLayer.Pants),
            ["粗布衬衫"] = Worn("粗布衬衫", ApparelBasePath, 4, PaperDollLayer.Skin),
            ["粗布短裤"] = Worn("粗布短裤", ApparelBasePath, 5, PaperDollLayer.Pants),
            ["粗布长裤"] = Worn("粗布长裤", ApparelBasePath, 6, PaperDollLayer.Pants),
            ["防弹背心"] = Worn("防弹背心", ApparelBasePath, 7, PaperDollLayer.Skin),

            ["粗布背心"] = Worn("粗布背心", ApparelOuterPath, 0, PaperDollLayer.Outer),
            ["粗布外套"] = Worn("粗布外套", ApparelOuterPath, 1, PaperDollLayer.Outer),
            ["布夹克"] = Worn("布夹克", ApparelOuterPath, 2, PaperDollLayer.Outer),
            ["牛仔外套"] = Worn("牛仔外套", ApparelOuterPath, 3, PaperDollLayer.Outer),
            ["皮夹克"] = Worn("皮夹克", ApparelOuterPath, 4, PaperDollLayer.Outer),
            ["皮革胸甲"] = Worn("皮革胸甲", ApparelOuterPath, 5, PaperDollLayer.Plate),
            ["皮甲"] = Worn("皮甲", ApparelOuterPath, 6, PaperDollLayer.Plate),
            ["板甲"] = Worn("板甲", ApparelPlatePath, 0, PaperDollLayer.Plate),

            ["恐怖装甲"] = Worn("恐怖装甲", ApparelSpecialPath, 0, PaperDollLayer.Plate, cellWidth: 96, cellHeight: 96),
            ["厚重裤子"] = Worn("厚重裤子", ApparelSpecialPath, 1, PaperDollLayer.Pants, cellWidth: 96, cellHeight: 96),
            ["厚重披风"] = Worn("厚重披风", ApparelSpecialPath, 2, PaperDollLayer.Plate, cellWidth: 96, cellHeight: 96),
            ["简易装甲"] = Worn("简易装甲", ApparelSpecialPath, 3, PaperDollLayer.Plate, cellWidth: 96, cellHeight: 96),
            ["运动鞋"] = Worn("运动鞋", ApparelSpecialPath, 4, PaperDollLayer.Feet, EquipmentVisualAnchor.Foot, 0.78f, 96, 96),
            ["护踝鞋具"] = Worn("护踝鞋具", ApparelSpecialPath, 5, PaperDollLayer.Feet, EquipmentVisualAnchor.Foot, 0.88f, 96, 96),
            ["雪地靴"] = Worn("雪地靴", ApparelSpecialPath, 6, PaperDollLayer.Feet, EquipmentVisualAnchor.Foot, 0.94f, 96, 96),
            ["马靴"] = Worn("马靴", ApparelSpecialPath, 7, PaperDollLayer.Feet, EquipmentVisualAnchor.Foot, 1.02f, 96, 96),

            ["军用头盔"] = Worn("军用头盔", ApparelHeadPath, 0, PaperDollLayer.Head, EquipmentVisualAnchor.Head, 1.00f, 96, 96),
            ["防暴头盔"] = Worn("防暴头盔", ApparelHeadPath, 1, PaperDollLayer.Head, EquipmentVisualAnchor.Head, 1.10f, 96, 96),
            ["战争面具"] = Worn("战争面具", ApparelWarMaskPath, 0, PaperDollLayer.Face, EquipmentVisualAnchor.Face, 0.88f, 96, 96),
            ["棉帽"] = Worn("棉帽", ApparelHeadPath, 3, PaperDollLayer.Head, EquipmentVisualAnchor.Head, 0.92f, 96, 96),
            ["墨镜"] = Worn("墨镜", ApparelHeadPath, 4, PaperDollLayer.Eyes, EquipmentVisualAnchor.Face, 0.58f, 96, 96),
            ["平光眼镜"] = Worn("平光眼镜", ApparelHeadPath, 5, PaperDollLayer.Eyes, EquipmentVisualAnchor.Face, 0.55f, 96, 96),
            ["自制简易墨镜"] = Worn("自制简易墨镜", ApparelHeadPath, 6, PaperDollLayer.Eyes, EquipmentVisualAnchor.Face, 0.64f, 96, 96),
            ["牛仔帽"] = Worn("牛仔帽", ApparelHeadPath, 7, PaperDollLayer.Head, EquipmentVisualAnchor.Head, 1.12f, 96, 96),
            ["劳保手套"] = Worn("劳保手套", ApparelGlovePath, 0, PaperDollLayer.Hands, EquipmentVisualAnchor.Hand, 0.58f, 96, 96),
        };

    private static readonly IReadOnlyDictionary<string, EquipmentVisualDef> DogApparel =
        new Dictionary<string, EquipmentVisualDef>(StringComparer.Ordinal)
        {
            [DogGearCatalog.ClothVestKey] = new(DogGearCatalog.ClothVestKey, EquipmentVisualKind.DogApparel, DogApparelPath, 0, 1f, PaperDollLayer.Outer, EquipmentVisualAnchor.DogBody, 64, 96),
            [DogGearCatalog.LeatherVestKey] = new(DogGearCatalog.LeatherVestKey, EquipmentVisualKind.DogApparel, DogApparelPath, 1, 1f, PaperDollLayer.Outer, EquipmentVisualAnchor.DogBody, 64, 96),
            [DogGearCatalog.PocketVestKey] = new(DogGearCatalog.PocketVestKey, EquipmentVisualKind.DogApparel, DogPocketHarnessPath, 0, 1f, PaperDollLayer.Outer, EquipmentVisualAnchor.DogBody, 64, 96),
            [DogGearCatalog.IronHelmetKey] = new(DogGearCatalog.IronHelmetKey, EquipmentVisualKind.DogApparel, DogApparelPath, 3, 1f, PaperDollLayer.Head, EquipmentVisualAnchor.DogBody, 64, 96),
            [DogGearCatalog.WireHelmetKey] = new(DogGearCatalog.WireHelmetKey, EquipmentVisualKind.DogApparel, DogApparelPath, 4, 1f, PaperDollLayer.Head, EquipmentVisualAnchor.DogBody, 64, 96),
        };

    public static IReadOnlyCollection<string> WeaponNames => new List<string>(Weapons.Keys);
    public static IReadOnlyCollection<string> ApparelNames => new List<string>(Apparel.Keys);
    public static IReadOnlyCollection<string> DogApparelNames => new List<string>(DogApparel.Keys);

    /// <summary>改装变体沿用基础武器外形；改装件独立叠层不在本批 60 件内。</summary>
    public static EquipmentVisualDef? ResolveWeapon(string? runtimeName)
    {
        if (string.IsNullOrWhiteSpace(runtimeName))
            return null;
        if (Weapons.TryGetValue(runtimeName, out EquipmentVisualDef? exact))
            return exact;
        foreach (KeyValuePair<string, EquipmentVisualDef> entry in Weapons)
            if (runtimeName.StartsWith(entry.Key + "（", StringComparison.Ordinal))
                return entry.Value;
        return null;
    }

    public static EquipmentVisualDef? ResolveLight(string? key)
        => key is not null && Lights.TryGetValue(key, out EquipmentVisualDef? visual) ? visual : null;

    public static EquipmentVisualDef? ResolveApparel(string? key)
        => key is not null && Apparel.TryGetValue(key, out EquipmentVisualDef? visual) ? visual : null;

    public static EquipmentVisualDef? ResolveDogApparel(string? key)
        => key is not null && DogApparel.TryGetValue(key, out EquipmentVisualDef? visual) ? visual : null;

    public static PaperDollLayer LayerFor(EquipSlot slot) => slot switch
    {
        EquipSlot.SkinLayer => PaperDollLayer.Skin,
        EquipSlot.Pants => PaperDollLayer.Pants,
        EquipSlot.LeftFoot or EquipSlot.RightFoot => PaperDollLayer.Feet,
        EquipSlot.OuterLayer => PaperDollLayer.Outer,
        EquipSlot.PlateLayer => PaperDollLayer.Plate,
        EquipSlot.LeftHand or EquipSlot.RightHand => PaperDollLayer.Hands,
        EquipSlot.Head => PaperDollLayer.Head,
        EquipSlot.Eyes => PaperDollLayer.Eyes,
        EquipSlot.Face => PaperDollLayer.Face,
        _ => PaperDollLayer.None,
    };

    /// <summary>列顺序 S,SW,W,NW,N,NE,E,SE；背向三列的手持物先画，身体自然遮住握柄。</summary>
    public static bool DrawHeldBehindBody(int directionColumn)
        => directionColumn is 3 or 4 or 5;
}
