using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【数值外置 · weapons 范式的零漂移 A/B 焊死】config-skeleton 单。
/// <para>
/// 武器数值已从 <see cref="WeaponTable"/> 的 C# 常量搬到 <c>godot/data/config/weapons.json</c>，
/// 工厂方法（<c>WeaponTable.Dagger()</c> …）身体改成 <c>=&gt; CombatCatalog.Weapon("dagger")</c>。
/// 本文件钉死「搬家没搬错一个数」：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c> 注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：抽样武器的每一类字段（double/bool/枚举/可空/弹药键/连发/弹丸）
///     逐条断言 == 迁移前的原始常量。<b>double 用位级相等</b>（0.075 → "0.075" → 0.075 一位不差）。</item>
///   <item><b>往返保真</b>：catalog 里每把武器再序列化→反序列化，逐字段位级相等 ⇒ 加载器不丢精度（值无关，永久护栏）。</item>
///   <item><b>完整性/顺序</b>：28 把 id 全可取、Arsenal 25 把顺序不变、ArcheryArsenal 8 把、名字非空。</item>
///   <item><b>枚举出字符串</b>：DamageType 序列化为 "Sharp"/"Blunt"（非序号 0/1）。</item>
/// </list>
/// <para>
/// 🔴 <b>另有一条更强的零漂移证明在 test 之外</b>：整表 10M 次蒙特卡洛 Sim 输出，迁移前后 MD5 完全一致
/// （<c>44c28a8efe62f118c2322ad2de38f432</c>，见 config-skeleton journal）。那是"Sim 结算路径读到的武器值逐位不变"的
/// 全量证明；本文件是它的单测版补充 + 字段级锚点。
/// </para>
/// <para>
/// 📐 <b>后续 config 迁移单（armor/ammo/archery/body…）照此文件立范式</b>：①从当前工厂逐字节序列化生成 json；
/// ②工厂身体改读 catalog；③永久护栏＝字面值锚定 + 往返保真 + 完整性；④combat 单额外跑 Sim MD5 前后一致。
/// </para>
/// </summary>
public sealed class WeaponConfigMigrationTests
{
    // 迁移前 WeaponTable 里的原始常量（golden）——手抄自迁移前的工厂 literal，作为 A/B 的"旧硬编码"一侧。
    // 抽样覆盖每一类字段；改 weapons.json 里这些值会让本表变红（数值调整须是深思熟虑的、可见的）。

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        var dagger = WeaponTable.Dagger();   // 首次访问触发懒加载
        Assert.True(CombatCatalog.IsInitialized, "首次取武器后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal("匕首", dagger.Name);
    }

    [Fact]
    public void All_28_ids_resolve_with_nonempty_name()
    {
        foreach (var id in AllIds)
        {
            var w = CombatCatalog.Weapon(id);
            Assert.False(string.IsNullOrEmpty(w.Name), $"{id} 名字不应为空");
        }
        Assert.Equal(28, CombatCatalog.Weapons.Count);
    }

    [Fact]
    public void Missing_id_fails_fast()
    {
        Assert.Throws<KeyNotFoundException>(() => CombatCatalog.Weapon("no_such_weapon"));
    }

    [Fact]
    public void Arsenal_order_and_counts_unchanged()
    {
        var arsenal = WeaponTable.Arsenal();
        Assert.Equal(25, arsenal.Count);
        Assert.Equal("匕首", arsenal[0].Name);            // 历史首位
        Assert.Equal("骨刀", arsenal[^1].Name);           // 历史末位（追加不插队）
        Assert.Equal("自制霰弹枪", arsenal[15].Name);      // 第 16 位：原地替换短弓之后紧邻（随机流锚点）
        Assert.Equal(8, WeaponTable.ArcheryArsenal().Count);
    }

    // ── 字面值锚定（A/B）：抽样武器 × 每类字段，== 迁移前原始常量 ──────────────────────

    [Fact]
    public void Dagger_matches_original_literals()
    {
        var w = WeaponTable.Dagger();
        Assert.Equal("匕首", w.Name);
        Assert.Equal(1, w.DamageMin);
        Assert.Equal(7, w.DamageMax);
        ExactlyEqual(0.075, w.Penetration);          // double 位级：0.075 一位不差
        Assert.Equal(DamageType.Sharp, w.DamageType);
        ExactlyEqual(1.7, w.AttackInterval);
        Assert.True(w.CanDualWield);
        Assert.False(w.TwoHanded);
        Assert.False(w.IsRanged);
        Assert.Equal(90, w.NoiseRadius);
        Assert.Equal(0.4, w.StructureFactor);
        Assert.Null(w.MaxRange);                     // 近战：无射程模型
        Assert.Null(w.StockMeleeDamageMax);          // 近战：无枪托 profile
        Assert.Equal("", w.AmmoKey);
        ExactlyEqual(560.0, w.FlightSpeed);          // 默认飞速（零漂移锚）
        ExactlyEqual(1.0, w.BleedRateMultiplier);
        Assert.Equal(1, w.PelletCount);
        Assert.Equal(1, w.BurstCount);
    }

    [Fact]
    public void High_penetration_doubles_round_trip_exactly()
    {
        ExactlyEqual(0.24, WeaponTable.Longsword().Penetration);
        ExactlyEqual(0.40, WeaponTable.Greatsword().Penetration);
        ExactlyEqual(0.70, WeaponTable.Rifle().Penetration);
        ExactlyEqual(0.95, WeaponTable.SniperRifle().Penetration);
        ExactlyEqual(0.03, WeaponTable.ZombieClaw().Penetration);
        Assert.Equal(15, WeaponTable.Longsword().DamageMax);
        Assert.True(WeaponTable.Longsword().TwoHanded);
        Assert.Equal(3, WeaponTable.ZombieClaw().DamageMax);
    }

    [Fact]
    public void Guns_match_ammo_burst_pellet_and_stock_fields()
    {
        var rifle = WeaponTable.Rifle();
        Assert.True(rifle.IsRanged);
        Assert.Equal(2, rifle.BurstCount);                    // 二连发
        Assert.Equal("ammo_medium", rifle.AmmoKey);
        Assert.Equal(2, rifle.AmmoPerAttack);                 // 派生：连发数
        Assert.Equal(550, rifle.MaxRange);
        Assert.Equal(0.6, rifle.FalloffFloor);
        Assert.Equal(3.5, rifle.StockMeleeDamageMin);
        ExactlyEqual(0.03, rifle.StockMeleePenetration!.Value);
        Assert.Equal(DamageType.Blunt, rifle.StockMeleeDamageType);

        var smg = WeaponTable.Smg();
        Assert.Equal(3, smg.BurstCount);                      // 三连发
        ExactlyEqual(0.06, smg.BurstInterval);
        Assert.Equal("ammo_short", smg.AmmoKey);

        var shotgun = WeaponTable.ImprovisedShotgun();
        Assert.Equal(8, shotgun.PelletCount);                 // 唯一多弹丸
        Assert.Equal("ammo_buck", shotgun.AmmoKey);
        Assert.Equal(1, shotgun.AmmoPerAttack);               // 8 弹丸只扣 1 发
        Assert.Equal(18, shotgun.BaseSpreadDegrees);
        Assert.Equal(0.2, shotgun.FalloffFloor);

        var sniper = WeaponTable.SniperRifle();
        Assert.Equal("ammo_long", sniper.AmmoKey);
        ExactlyEqual(7.5, sniper.AttackInterval);
    }

    [Fact]
    public void Innate_and_crossbow_edge_fields()
    {
        var fists = WeaponTable.Fists();
        Assert.Equal(0, fists.Penetration);                   // 全表唯一 0 穿透
        Assert.Equal(DamageType.Blunt, fists.DamageType);
        Assert.Equal("", fists.AmmoKey);
        Assert.False(fists.UsesAmmo);

        var lc = WeaponTable.LightCrossbow();
        Assert.False(lc.TwoHanded);                           // 唯一单手弓弩
        Assert.True(lc.CanDualWield);
        Assert.Equal("ammo_arrow", lc.AmmoKey);
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）──────────────────────────────

    [Fact]
    public void Every_weapon_survives_json_round_trip_bit_for_bit()
    {
        foreach (var (id, w) in CombatCatalog.Weapons)
        {
            string json = JsonSerializer.Serialize(w, CombatConfigLoader.Options);
            var back = JsonSerializer.Deserialize<Weapon>(json, CombatConfigLoader.Options)!;
            AssertWeaponBitEqual(w, back, id);
        }
    }

    [Fact]
    public void Enums_serialize_as_strings_not_ordinals()
    {
        string json = JsonSerializer.Serialize(WeaponTable.Dagger(), CombatConfigLoader.Options);
        Assert.Contains("\"DamageType\": \"Sharp\"", json);   // 出字符串，不是 0
        Assert.DoesNotContain("\"DamageType\": 0", json);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] AllIds =
    {
        "dagger", "shortsword", "rapier", "longsword", "pitchfork", "greatsword", "axe", "bone_knife",
        "club", "spike_hammer", "warhammer",
        "improvised_hunting_gun", "pistol", "smg", "rifle", "sniper_rifle", "improvised_shotgun",
        "short_bow", "recurve_bow", "longbow", "competition_compound_bow", "hunting_bow",
        "light_crossbow", "heavy_crossbow", "compound_crossbow",
        "zombie_claw", "dog_bite", "fists",
    };

    /// <summary>double 位级相等（比 == 更严：连 -0.0/NaN 都区分）——证明"往返一位不差"。</summary>
    private static void ExactlyEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    /// <summary>反射比对两把武器的全部可读属性（double 走位级、可空/枚举/字符串照常）。</summary>
    private static void AssertWeaponBitEqual(Weapon a, Weapon b, string id)
    {
        foreach (var p in typeof(Weapon).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? va = p.GetValue(a);
            object? vb = p.GetValue(b);
            if (va is double da && vb is double db)
            {
                Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                    $"{id}.{p.Name} double 漂移：{da} vs {db}");
            }
            else
            {
                Assert.True(Equals(va, vb), $"{id}.{p.Name} 不等：{va} vs {vb}");
            }
        }
    }
}
