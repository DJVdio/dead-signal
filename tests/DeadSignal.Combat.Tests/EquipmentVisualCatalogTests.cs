using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class EquipmentVisualCatalogTests
{
    public static TheoryData<string> 全部正式武器 => new()
    {
        "匕首", "短剑", "刺剑", "长剑", "重剑", "草叉", "消防斧", "骨刀", "自制手枪", "牙医小手枪",
        "棍棒", "尖头锤", "破甲锤", "自制猎枪", "手枪", "冲锋枪", "步枪", "狙击枪", "自制霰弹枪",
        "短弓", "反曲弓", "长弓", "竞技复合弓", "狩猎弓", "单手轻弩", "双手重弩", "复合弩",
    };

    [Theory]
    [MemberData(nameof(全部正式武器))]
    public void 二十七件正式武器都有八方向图集(string name)
    {
        EquipmentVisualDef? visual = EquipmentVisualCatalog.ResolveWeapon(name);

        Assert.NotNull(visual);
        Assert.Equal(EquipmentVisualKind.Weapon, visual!.Kind);
        Assert.Equal(EquipmentVisualAnchor.Held, visual.Anchor);
        Assert.EndsWith(".png", visual.AtlasPath);
    }

    [Fact]
    public void 武器视觉目录恰好覆盖二十七件非天然攻击武器()
        => Assert.Equal(27, EquipmentVisualCatalog.WeaponNames.Count);

    [Theory]
    [InlineData("步枪（刺刀型）", "步枪", 0)]
    [InlineData("长剑（锋刃研磨）#12", "长剑", 1)]
    public void 改装变体沿用基础武器外形直到改装叠层阶段(string runtimeName, string baseName, int row)
    {
        EquipmentVisualDef? visual = EquipmentVisualCatalog.ResolveWeapon(runtimeName);

        Assert.NotNull(visual);
        Assert.Equal(baseName, visual!.ItemKey);
        Assert.Equal(row, visual.AtlasRow);
    }

    [Theory]
    [InlineData(LightSource.FlashlightKey, 5)]
    [InlineData(LightSource.TorchKey, 6)]
    public void 两种手持光源都有稳定图集行(string key, int row)
    {
        EquipmentVisualDef? visual = EquipmentVisualCatalog.ResolveLight(key);

        Assert.NotNull(visual);
        Assert.Equal(row, visual!.AtlasRow);
        Assert.Equal(EquipmentVisualKind.Light, visual.Kind);
    }

    public static TheoryData<string> 三十三件Wiki人类穿戴 => new()
    {
        "长袖布衣", "花衬衫", "长裤", "运动鞋", "短裤", "皮革胸甲", "粗布背心", "粗布外套",
        "布夹克", "牛仔外套", "皮夹克", "皮甲", "板甲", "军用头盔", "防暴头盔", "劳保手套",
        "战争面具", "棉帽", "粗布衬衫", "粗布短裤", "粗布长裤", "恐怖装甲", "墨镜", "平光眼镜",
        "自制简易墨镜", "护踝鞋具", "防弹背心", "厚重裤子", "厚重披风", "雪地靴", "牛仔帽",
        "马靴", "简易装甲",
    };

    [Theory]
    [MemberData(nameof(三十三件Wiki人类穿戴))]
    public void 三十三件人类穿戴都有纸娃娃定义(string name)
    {
        EquipmentVisualDef? visual = EquipmentVisualCatalog.ResolveApparel(name);

        Assert.NotNull(visual);
        Assert.Equal(EquipmentVisualKind.Apparel, visual!.Kind);
        Assert.NotEqual(PaperDollLayer.None, visual.Layer);
    }

    [Fact]
    public void 人类穿戴视觉目录严格等于Wiki三十三件()
        => Assert.Equal(33, EquipmentVisualCatalog.ApparelNames.Count);

    [Theory]
    [InlineData("战争面具")]
    [InlineData("墨镜")]
    [InlineData("平光眼镜")]
    [InlineData("自制简易墨镜")]
    public void 面具和眼镜固定走面部锚点不覆盖发型(string name)
        => Assert.Equal(EquipmentVisualAnchor.Face, EquipmentVisualCatalog.ResolveApparel(name)!.Anchor);

    [Fact]
    public void 板甲是带腿部的身体叠层而不是手脚附件()
    {
        EquipmentVisualDef visual = EquipmentVisualCatalog.ResolveApparel("板甲")!;

        Assert.Equal(PaperDollLayer.Plate, visual.Layer);
        Assert.Equal(EquipmentVisualAnchor.Body, visual.Anchor);
        Assert.Equal(EquipmentVisualCatalog.ApparelPlatePath, visual.AtlasPath);
    }

    [Fact]
    public void 刺剑消防斧和两把军用步枪使用修订后的独立图集()
    {
        Assert.Equal(EquipmentVisualCatalog.HeldRapierPath, EquipmentVisualCatalog.ResolveWeapon("刺剑")!.AtlasPath);
        Assert.Equal(EquipmentVisualCatalog.HeldFireAxePath, EquipmentVisualCatalog.ResolveWeapon("消防斧")!.AtlasPath);
        Assert.Equal(EquipmentVisualCatalog.HeldMilitaryRiflesPath, EquipmentVisualCatalog.ResolveWeapon("步枪")!.AtlasPath);
        Assert.Equal(EquipmentVisualCatalog.HeldMilitaryRiflesPath, EquipmentVisualCatalog.ResolveWeapon("狙击枪")!.AtlasPath);
    }

    [Theory]
    [InlineData(DogGearCatalog.ClothVestKey)]
    [InlineData(DogGearCatalog.LeatherVestKey)]
    [InlineData(DogGearCatalog.PocketVestKey)]
    [InlineData(DogGearCatalog.IronHelmetKey)]
    [InlineData(DogGearCatalog.WireHelmetKey)]
    public void 布鲁斯五件装备都有专属叠层(string key)
    {
        EquipmentVisualDef? visual = EquipmentVisualCatalog.ResolveDogApparel(key);

        Assert.NotNull(visual);
        Assert.Equal(EquipmentVisualKind.DogApparel, visual!.Kind);
        Assert.Equal(EquipmentVisualAnchor.DogBody, visual.Anchor);
    }

    [Fact]
    public void 口袋狗衣使用独立带袋挂具而非整块狗衣图集()
        => Assert.Equal(EquipmentVisualCatalog.DogPocketHarnessPath,
            EquipmentVisualCatalog.ResolveDogApparel(DogGearCatalog.PocketVestKey)!.AtlasPath);

    [Fact]
    public void 十一个穿戴槽全部落到明确的纸娃娃层()
    {
        foreach (EquipSlot slot in Enum.GetValues<EquipSlot>())
            Assert.NotEqual(PaperDollLayer.None, EquipmentVisualCatalog.LayerFor(slot));
    }

    [Theory]
    [InlineData(0, false)] // 南
    [InlineData(1, false)] // 西南
    [InlineData(2, false)] // 西
    [InlineData(3, true)]  // 西北
    [InlineData(4, true)]  // 北
    [InlineData(5, true)]  // 东北
    [InlineData(6, false)] // 东
    [InlineData(7, false)] // 东南
    public void 背向三方向的手持物先画在身体后面(int directionColumn, bool behind)
        => Assert.Equal(behind, EquipmentVisualCatalog.DrawHeldBehindBody(directionColumn));
}
