using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 武器改装模型 + 合成逻辑：单改装数值变化、多部位叠加、同部位冲突拒绝、不适用武器拒绝、
/// 锐击枪托型（刺刀/利爪）与钝击型（创伤）枪托改装。数值皆 draft，测试只校验方向/形态，不锁死具体数。
/// </summary>
public sealed class WeaponModTests
{
    // ---- 大类派生 ----

    [Fact]
    public void ClassOf_DerivesFromExistingFields()
    {
        Assert.Equal(WeaponClass.Firearm, WeaponMods.ClassOf(WeaponTable.Rifle()));
        Assert.Equal(WeaponClass.Blade, WeaponMods.ClassOf(WeaponTable.Shortsword()));
        Assert.Equal(WeaponClass.Blunt, WeaponMods.ClassOf(WeaponTable.Club()));
    }

    // ---- 单改装数值变化 ----

    [Fact]
    public void SingleMod_ChangesTargetedStats_LeavesBaseUntouched()
    {
        var baseSword = WeaponTable.Shortsword();
        var result = WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.HonedEdge() });

        // 研磨：穿透 +0.05
        Assert.Equal(baseSword.Penetration + 0.05, result.Weapon.Penetration, 6);
        // 其余不变
        Assert.Equal(baseSword.DamageMax, result.Weapon.DamageMax, 6);
        // 原 base 未被改动
        Assert.Equal(0.12, baseSword.Penetration, 6);
    }

    [Fact]
    public void WeightedHandle_RaisesDamage_LowersAttackSpeed()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.WeightedHandle() });

        Assert.True(r.Weapon.DamageMin > baseSword.DamageMin);
        Assert.True(r.Weapon.DamageMax > baseSword.DamageMax);
        Assert.True(r.Weapon.AttackInterval > baseSword.AttackInterval); // 间隔↑ = 攻速↓
    }

    [Fact]
    public void LightenedHandle_RaisesAttackSpeed_LowersDamage()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.LightenedHandle() });

        Assert.True(r.Weapon.AttackInterval < baseSword.AttackInterval); // 间隔↓ = 攻速↑
        Assert.True(r.Weapon.DamageMax < baseSword.DamageMax);
    }

    [Fact]
    public void SawnOffBarrel_ShortensRange_WidensSpread()
    {
        var baseGun = WeaponTable.Rifle();
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.SawnOffBarrel() });

        Assert.True(r.Weapon.MaxRange < baseGun.MaxRange);
        Assert.True(r.Weapon.BaseSpreadDegrees > baseGun.BaseSpreadDegrees);
    }

    [Fact]
    public void ExtendedBarrel_LengthensRange_TightensSpread()
    {
        var baseGun = WeaponTable.Rifle();
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.ExtendedBarrel() });

        Assert.True(r.Weapon.MaxRange > baseGun.MaxRange);
        Assert.True(r.Weapon.BaseSpreadDegrees < baseGun.BaseSpreadDegrees);
    }

    [Fact]
    public void NailStuds_OnClub_AddsPenetration()
    {
        var baseClub = WeaponTable.Club();
        var r = WeaponMods.ApplyMods(baseClub, new[] { WeaponModCatalog.NailStuds() });

        Assert.True(r.Weapon.Penetration > baseClub.Penetration);
        Assert.True(r.Weapon.DamageMax > baseClub.DamageMax);
    }

    // ---- 多部位叠加 ----

    [Fact]
    public void MultipleMods_DifferentParts_AllApplied()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, new[]
        {
            WeaponModCatalog.SerratedBlade(),   // 刃
            WeaponModCatalog.WeightedHandle(),  // 柄
            WeaponModCatalog.GripWrapBlade(),   // 缠手
        });

        // 三部位各自的效果都体现
        Assert.True(r.Weapon.DamageMax > baseSword.DamageMax);   // 锯齿 + 加重
        Assert.True(r.Weapon.DamageMin > baseSword.DamageMin);   // 加重
        Assert.Equal(3, r.AppliedMods.Count);
        Assert.Contains("锯齿剑刃", r.Weapon.Name);
        Assert.Contains("防滑缠手", r.Weapon.Name);
    }

    [Fact]
    public void GunMultiPart_Barrel_Muzzle_Stock_Coexist()
    {
        var baseGun = WeaponTable.Rifle();
        // 加长枪管(Barrel) + 刺刀(Muzzle) + 轻质化枪托(Stock)：三个不同部位并存
        var r = WeaponMods.ApplyMods(baseGun, new[]
        {
            WeaponModCatalog.ExtendedBarrel(),
            WeaponModCatalog.Bayonet(),
            WeaponModCatalog.LightenedStock(),
        });

        Assert.Equal(3, r.AppliedMods.Count);
        Assert.True(r.Weapon.MaxRange > baseGun.MaxRange);        // 加长枪管
        Assert.NotNull(r.StockMeleeOverride);                     // 刺刀锐击枪托
        Assert.Equal(DamageType.Sharp, r.StockMeleeOverride!.DamageType);
    }

    // ---- 同部位冲突拒绝 ----

    [Fact]
    public void SamePart_Twice_Throws()
    {
        var baseSword = WeaponTable.Shortsword();
        var ex = Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseSword, new[]
            {
                WeaponModCatalog.WeightedHandle(),   // 柄
                WeaponModCatalog.LightenedHandle(),  // 柄 —— 冲突
            }));
        Assert.Contains("柄", ex.Message);
    }

    [Fact]
    public void SameBladePart_Twice_Throws()
    {
        var baseSword = WeaponTable.Shortsword();
        Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseSword, new[]
            {
                WeaponModCatalog.SerratedBlade(),  // 刃
                WeaponModCatalog.HonedEdge(),      // 刃 —— 冲突
            }));
    }

    // ---- 不适用武器拒绝 ----

    [Fact]
    public void GunMod_OnMeleeWeapon_Throws()
    {
        var baseSword = WeaponTable.Shortsword();
        var ex = Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.SawnOffBarrel() }));
        Assert.Contains("不适用", ex.Message);
    }

    [Fact]
    public void BladeMod_OnClub_Throws()
    {
        var baseClub = WeaponTable.Club();
        Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseClub, new[] { WeaponModCatalog.SerratedBlade() }));
    }

    [Fact]
    public void BluntMod_OnFirearm_Throws()
    {
        var baseGun = WeaponTable.Rifle();
        Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.WireWrap() }));
    }

    // ---- 锐击/钝击枪托型改装 ----

    [Fact]
    public void Bayonet_MakesStockMeleeSharp_HigherThanDefault()
    {
        var baseGun = WeaponTable.Rifle();
        var defaultMelee = baseGun.MeleeProfile();
        Assert.NotNull(defaultMelee);
        Assert.Equal(DamageType.Blunt, defaultMelee!.DamageType); // 默认枪托钝击

        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.Bayonet() });
        var eff = r.EffectiveMeleeProfile();

        Assert.NotNull(eff);
        Assert.Equal(DamageType.Sharp, eff!.DamageType);            // 刺刀锐击
        Assert.True(eff.Penetration > defaultMelee.Penetration);    // 穿透↑
        Assert.True(eff.DamageMax > defaultMelee.DamageMax);        // 伤↑
        Assert.False(eff.IsRanged);                                 // 近战必中
    }

    [Fact]
    public void ClawStock_MakesStockMeleeSharp()
    {
        var baseGun = WeaponTable.Rifle();
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.ClawStock() });
        var eff = r.EffectiveMeleeProfile();

        Assert.NotNull(eff);
        Assert.Equal(DamageType.Sharp, eff!.DamageType);
    }

    [Fact]
    public void TraumaStock_KeepsBluntButHitsHarderAndSlower()
    {
        var baseGun = WeaponTable.Rifle();
        var defaultMelee = baseGun.MeleeProfile()!;
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.TraumaStock() });
        var eff = r.EffectiveMeleeProfile();

        Assert.NotNull(eff);
        Assert.Equal(DamageType.Blunt, eff!.DamageType);           // 铁锤仍钝击（无锐击覆盖）
        Assert.Null(r.StockMeleeOverride);                         // 走数值增量，非覆盖
        Assert.True(eff.DamageMax > defaultMelee.DamageMax);       // 伤↑
        Assert.True(eff.AttackInterval > defaultMelee.AttackInterval); // 更慢
    }

    [Fact]
    public void TwoSharpStockMods_Conflict_Throws()
    {
        // 刺刀(Muzzle)与利爪(Stock)部位不同、可过部位校验，但两者都要改"锐击枪托" → 拒绝
        var baseGun = WeaponTable.Rifle();
        var ex = Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseGun, new[]
            {
                WeaponModCatalog.Bayonet(),
                WeaponModCatalog.ClawStock(),
            }));
        Assert.Contains("枪托近战", ex.Message);
    }

    // ---- 合成不改动 base、无改装即原样 ----

    [Fact]
    public void EmptyMods_ProducesEquivalentWeapon()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, System.Array.Empty<WeaponMod>());

        Assert.Equal(baseSword.Name, r.Weapon.Name);
        Assert.Equal(baseSword.DamageMax, r.Weapon.DamageMax, 6);
        Assert.Equal(baseSword.Penetration, r.Weapon.Penetration, 6);
    }

    [Fact]
    public void ImplicitConversion_ToWeapon_Works()
    {
        Weapon w = WeaponMods.ApplyMods(WeaponTable.Shortsword(), new[] { WeaponModCatalog.HonedEdge() });
        Assert.Contains("锋刃研磨", w.Name);
    }

    // ---- 目录 ----

    [Fact]
    public void Catalog_For_ReturnsClassAppropriateMods()
    {
        Assert.All(WeaponModCatalog.For(WeaponTable.Rifle()),
            m => Assert.Equal(WeaponClass.Firearm, m.RequiredClass));
        Assert.All(WeaponModCatalog.For(WeaponTable.Club()),
            m => Assert.Equal(WeaponClass.Blunt, m.RequiredClass));
        Assert.NotEmpty(WeaponModCatalog.All());
    }
}
