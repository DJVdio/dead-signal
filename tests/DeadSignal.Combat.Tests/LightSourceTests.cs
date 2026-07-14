using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次4 光照与视野——光源侧纯逻辑单测：光源目录、按距离衰减、最强贡献查询、持光暴露代价、
/// 手持光源占手态、光源物品/火把配方落地。数值皆"拟定待调"——本测规则形态(中心满档/半径外归零/
/// 单调衰减/取最强/黑暗放大暴露/占一只手与武器互斥)，常量若调断言随常量走(引用目录值而非硬编码)。
/// </summary>
public class LightSourceTests
{
    private static LightProfile Flashlight => LightSource.Find(LightSource.FlashlightKey)!.Value;
    private static LightProfile Torch => LightSource.Find(LightSource.TorchKey)!.Value;

    // ---------------- 目录 ----------------

    [Fact]
    public void Catalog_HasFourSources_HandheldAndFixed()
    {
        Assert.True(LightSource.Has(LightSource.FlashlightKey));
        Assert.True(LightSource.Has(LightSource.TorchKey));
        Assert.True(LightSource.Has(LightSource.LampKey));
        Assert.True(LightSource.Has(LightSource.CampfireKey));
        Assert.Equal(LightKind.Handheld, Flashlight.Kind);
        Assert.Equal(LightKind.Handheld, Torch.Kind);
        Assert.Equal(LightKind.Fixed, LightSource.Find(LightSource.LampKey)!.Value.Kind);
        Assert.Equal(LightKind.Fixed, LightSource.Find(LightSource.CampfireKey)!.Value.Kind);
    }

    [Fact]
    public void Find_UnknownKey_ReturnsNull()
    {
        Assert.Null(LightSource.Find("nope"));
        Assert.Null(LightSource.Find(null!));
        Assert.False(LightSource.Has(null!));
    }

    // ---------------- 按距离衰减 ----------------

    [Fact]
    public void ContributionAt_Center_IsFullIntensity()
        => Assert.Equal(Flashlight.Intensity, LightSource.ContributionAt(Flashlight, 0f), 3);

    [Fact]
    public void ContributionAt_BeyondRadius_IsZero()
    {
        Assert.Equal(0f, LightSource.ContributionAt(Flashlight, Flashlight.Radius), 3);
        Assert.Equal(0f, LightSource.ContributionAt(Flashlight, Flashlight.Radius + 50f), 3);
    }

    [Fact]
    public void ContributionAt_MonotonicallyDecreasesWithDistance()
    {
        float near = LightSource.ContributionAt(Flashlight, Flashlight.Radius * 0.25f);
        float mid = LightSource.ContributionAt(Flashlight, Flashlight.Radius * 0.5f);
        float far = LightSource.ContributionAt(Flashlight, Flashlight.Radius * 0.75f);
        Assert.True(near > mid && mid > far && far > 0f);
    }

    [Fact]
    public void ContributionAt_HalfRadius_IsHalfIntensity_LinearFalloff()
        => Assert.Equal(Flashlight.Intensity * 0.5f, LightSource.ContributionAt(Flashlight, Flashlight.Radius * 0.5f), 3);

    [Fact]
    public void ContributionAt_PlacedLight_UsesEuclideanDistance()
    {
        // 距圆心正好半径(3-4-5 → 距离=Radius) → 归零边界。
        var light = new PlacedLight(0f, 0f, Flashlight);
        float r = Flashlight.Radius;
        Assert.Equal(0f, LightSource.ContributionAt(light, r * 0.6f, r * 0.8f), 3);
    }

    // ---------------- 最强贡献查询(HANDOFF 入口) ----------------

    [Fact]
    public void StrongestContribution_NoLights_IsZero()
        => Assert.Equal(0f, LightSource.StrongestContribution(0f, 0f, Enumerable.Empty<PlacedLight>()), 3);

    [Fact]
    public void StrongestContribution_TakesMaxAcrossLights()
    {
        // 查询点靠近火把、远离手电：即便手电更亮，近处火把贡献更大 → 取火把。
        var lights = new[]
        {
            new PlacedLight(1000f, 0f, Flashlight), // 远
            new PlacedLight(10f, 0f, Torch),        // 近
        };
        float torchNear = LightSource.ContributionAt(Torch, 10f);
        Assert.Equal(torchNear, LightSource.StrongestContribution(0f, 0f, lights), 3);
    }

    // ---------------- 持光暴露代价 ----------------

    [Fact]
    public void Exposure_NotHolding_IsOne()
        => Assert.Equal(1f, LightSource.ExposureDetectionMultiplier((LightProfile?)null, ambient: 0.2f), 3);

    [Fact]
    public void Exposure_HoldingInDark_GreaterThanInDaylight()
    {
        float dark = LightSource.ExposureDetectionMultiplier(Flashlight, ambient: 0f);
        float day = LightSource.ExposureDetectionMultiplier(Flashlight, ambient: 1f);
        Assert.True(dark > day);
        Assert.Equal(1f, day, 3);          // 白天满环境光 → 无额外暴露
        Assert.True(dark > 1f);            // 满黑持光 → 被发现距离放大
    }

    [Fact]
    public void Exposure_BrighterLight_MoreExposedInDark()
    {
        float flash = LightSource.ExposureDetectionMultiplier(Flashlight, ambient: 0f);
        float torch = LightSource.ExposureDetectionMultiplier(Torch, ambient: 0f);
        Assert.True(flash > torch); // 手电(0.9) 比火把(0.5，T21 用户手改自 0.7) 更亮更显眼
    }

    [Fact]
    public void Exposure_ByKey_MatchesByProfile()
        => Assert.Equal(
            LightSource.ExposureDetectionMultiplier(Flashlight, 0.3f),
            LightSource.ExposureDetectionMultiplier(LightSource.FlashlightKey, 0.3f), 3);

    // ---------------- LightField 场查询 ----------------

    [Fact]
    public void LightField_AddFixed_UnknownKey_Rejected()
    {
        var field = new LightField();
        Assert.False(field.AddFixed("nope", 0f, 0f));
        Assert.True(field.AddFixed(LightSource.CampfireKey, 100f, 100f));
        Assert.Equal(1, field.Count);
    }

    [Fact]
    public void LightField_StrongestAt_QueriesFixedLights()
    {
        var field = new LightField();
        field.AddFixed(LightSource.CampfireKey, 0f, 0f);
        var fire = LightSource.Find(LightSource.CampfireKey)!.Value;
        Assert.Equal(fire.Intensity, field.StrongestAt(0f, 0f), 3);            // 火堆中心
        Assert.Equal(0f, field.StrongestAt(fire.Radius + 100f, 0f), 3);       // 半径外
    }

    [Fact]
    public void LightField_StrongestAt_MergesHandheldExtras()
    {
        var field = new LightField();
        field.AddFixed(LightSource.LampKey, 5000f, 5000f); // 固定光源远在天边
        var handheld = new[] { new PlacedLight(0f, 0f, Torch) };
        float torchCenter = LightSource.ContributionAt(Torch, 0f);
        Assert.Equal(torchCenter, field.StrongestAt(0f, 0f, handheld), 3);     // 近处手持火把胜出
    }

    [Fact]
    public void LightField_FromFixed_BuildsAndSkipsUnknownKeys()
    {
        var field = LightField.FromFixed(new[]
        {
            (LightSource.CampfireKey, 0f, 0f),
            ("nope", 10f, 10f),          // 坏 key → 跳过
            (LightSource.LampKey, 500f, 0f),
        });
        Assert.Equal(2, field.Count);
        var fire = LightSource.Find(LightSource.CampfireKey)!.Value;
        Assert.Equal(fire.Intensity, field.StrongestAt(0f, 0f), 3);
    }

    [Fact]
    public void LightField_Clear_EmptiesField()
    {
        var field = new LightField();
        field.AddFixed(LightSource.CampfireKey, 0f, 0f);
        field.Clear();
        Assert.Equal(0, field.Count);
        Assert.Equal(0f, field.StrongestAt(0f, 0f), 3);
    }

    // ---------------- HeldLightState 占手态 ----------------

    private static Weapon OneHand() => new()
    {
        Name = "匕首", DamageMin = 3, DamageMax = 7, DamageType = DamageType.Sharp,
        TwoHanded = false, CanDualWield = true, AttackInterval = 0.5,
    };

    private static Weapon TwoHand() => new()
    {
        Name = "消防斧", DamageMin = 10, DamageMax = 20, DamageType = DamageType.Sharp,
        TwoHanded = true, CanDualWield = false, AttackInterval = 1.2,
    };

    [Fact]
    public void Held_EmptyHands_CanHoldLight_OccupiesOneHand()
    {
        var loadout = new WeaponLoadout();
        var held = new HeldLightState();
        Assert.True(held.TryHold(Flashlight, Hand.Left, loadout));
        Assert.True(held.IsActive);
        Assert.Equal(Hand.Left, held.HandUsed);
        Assert.Equal(LightSource.FlashlightKey, held.Held!.Value.Key);
    }

    [Fact]
    public void Held_WithOneHandWeaponInOtherHand_CanHoldLight()
    {
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipToHand(OneHand(), Hand.Right)); // 右手单手武器
        var held = new HeldLightState();
        Assert.True(held.TryHold(Torch, Hand.Left, loadout));     // 左手持光 → 允许
    }

    [Fact]
    public void Held_HandAlreadyHoldsWeapon_Rejected()
    {
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipToHand(OneHand(), Hand.Right));
        var held = new HeldLightState();
        Assert.False(held.TryHold(Torch, Hand.Right, loadout));   // 右手已持械 → 拒绝
        Assert.False(held.IsActive);
    }

    [Fact]
    public void Held_TwoHandedWeaponEquipped_LightRejected_MutuallyExclusive()
    {
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipTwoHanded(TwoHand()));           // 双手武器占两手
        var held = new HeldLightState();
        Assert.False(held.TryHold(Flashlight, Hand.Left, loadout));
        Assert.False(held.TryHold(Flashlight, Hand.Right, loadout));
    }

    [Fact]
    public void Held_SeveredHand_Rejected()
    {
        var loadout = new WeaponLoadout(leftHandLost: true);
        var held = new HeldLightState();
        Assert.False(held.TryHold(Flashlight, Hand.Left, loadout));
    }

    [Fact]
    public void Held_Drop_ClearsState()
    {
        var loadout = new WeaponLoadout();
        var held = new HeldLightState();
        held.TryHold(Flashlight, Hand.Right, loadout);
        held.Drop();
        Assert.False(held.IsActive);
        Assert.Null(held.HandUsed);
    }

    [Fact]
    public void Held_BlocksTwoHandedEquip_WhenActive()
    {
        var held = new HeldLightState();
        Assert.False(HeldLightState.BlocksTwoHandedEquip(held));      // 未持光 → 不挡
        held.TryHold(Flashlight, Hand.Left, new WeaponLoadout());
        Assert.True(HeldLightState.BlocksTwoHandedEquip(held));       // 持光 → 挡双手武器
        Assert.False(HeldLightState.BlocksTwoHandedEquip(null));
    }

    [Fact]
    public void CanHold_PureCore()
    {
        Assert.True(HeldLightState.CanHold(handLost: false, twoHandGrip: false, handHoldsWeapon: false));
        Assert.False(HeldLightState.CanHold(handLost: true, twoHandGrip: false, handHoldsWeapon: false));
        Assert.False(HeldLightState.CanHold(handLost: false, twoHandGrip: true, handHoldsWeapon: false));
        Assert.False(HeldLightState.CanHold(handLost: false, twoHandGrip: false, handHoldsWeapon: true));
    }

    // ---------------- 光源物品 / 火把配方落地 ----------------

    [Fact]
    public void Item_Light_IsLightNotEquippable()
    {
        var flash = Item.Light(LightSource.FlashlightKey, "手电");
        Assert.Equal(ItemCategory.Light, flash.Category);
        Assert.True(flash.IsLight);
        Assert.False(flash.IsEquippable);
        Assert.Equal(LightSource.FlashlightKey, flash.RefKey);
    }

    [Fact]
    public void Recipe_Torch_Exists_NoToolNoBook_OutputIsLight()
    {
        var torch = RecipeBook.Find("torch");
        Assert.NotNull(torch);
        Assert.Empty(torch!.RequiredTools);
        Assert.Empty(torch.RequiredBookIds);
        Assert.Equal(LightSource.TorchKey, torch.OutputKey);
    }

    [Fact]
    public void CraftOutputFactory_Torch_ProducesLightItem()
    {
        var outputs = CraftOutputFactory.Create("torch", 1).ToList();
        Assert.Single(outputs);
        Assert.Equal(ItemCategory.Light, outputs[0].Category);
        Assert.Equal(LightSource.TorchKey, outputs[0].RefKey);
        Assert.Equal("火把", outputs[0].DisplayName);
    }
}
