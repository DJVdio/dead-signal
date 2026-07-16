using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【数值外置 · 弹药段的零漂移 A/B 焊死】config-ammo 单（照 <see cref="WeaponConfigMigrationTests"/> 立范式）。
/// <para>
/// 弹药制作比已从 <c>BulletParts.YieldPer</c> 的硬编码 <c>switch</c>（短 8/中 5/鹿 4/长 2）搬到
/// <c>godot/data/config/ammo.json</c>；<c>BulletParts.YieldPer</c> 身体改成
/// <c>=&gt; CombatCatalog.Section&lt;AmmoConfig&gt;().YieldPerBulletPart(key)</c>。本文件钉死「搬家没搬错一个数」：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：段可懒加载、4 键全取。</item>
///   <item><b>字面值锚定（A/B）</b>：四种子弹产出逐条 == 迁移前原始常量；箭/未知键仍返回 0（旧 <c>_ =&gt; 0</c> 语义）。</item>
///   <item><b>往返保真</b>：每条 AmmoDef 序列化→反序列化位级相等（值无关的永久护栏）。</item>
///   <item><b>完整性</b>：恰好 4 条、箭不在表内。</item>
/// </list>
/// <para>
/// 🔴 <b>更强的零漂移证明在 test 之外</b>：整表蒙特卡洛 Sim 输出迁移前后 MD5 完全一致
/// （见 config-ammo journal）——虽然 <c>Duel</c> 不建模弹药、结算路径根本读不到 YieldPer，这条是结构性零回归的兜底铁证。
/// </para>
/// </summary>
public sealed class AmmoConfigMigrationTests
{
    // 迁移前 BulletParts.YieldPer 里的原始常量（golden）——用户拍板制作比，A/B 的"旧硬编码"一侧。
    // 改 ammo.json 里这些值会让本表变红（数值调整须是深思熟虑的、可见的）。
    private static readonly (string Key, int Yield)[] Golden =
    {
        (AmmoKeys.ShortBullet, 8),
        (AmmoKeys.MediumBullet, 5),
        (AmmoKeys.Buckshot, 4),
        (AmmoKeys.LongBullet, 2),
    };

    [Fact]
    public void Section_is_wired_and_all_keys_resolve()
    {
        var cfg = CombatCatalog.Section<AmmoConfig>();   // 首次访问触发懒加载
        Assert.True(CombatCatalog.IsInitialized, "取弹药段后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(4, cfg.ById.Count);
        foreach (var (key, _) in Golden)
        {
            Assert.True(cfg.ById.ContainsKey(key), $"ammo.json 应含 {key}");
        }
    }

    [Fact]
    public void Yields_match_original_literals()
    {
        // A 侧：经 BulletParts.YieldPer（→catalog）取到的值 == B 侧 golden 常量。
        foreach (var (key, yield) in Golden)
        {
            Assert.Equal(yield, BulletParts.YieldPer(key));
            Assert.Equal(yield, CombatCatalog.Section<AmmoConfig>().Get(key).YieldPerBulletPart);
        }
    }

    [Fact]
    public void Arrow_and_unknown_keys_yield_zero()
    {
        // 旧 `_ => 0` 语义：箭（类别键、不吃子弹零件）与未知键 → 0，且不进 ammo.json。
        Assert.Equal(0, BulletParts.YieldPer(AmmoKeys.Arrow));
        Assert.Equal(0, BulletParts.YieldPer("no_such_ammo"));
        Assert.False(CombatCatalog.Section<AmmoConfig>().ById.ContainsKey(AmmoKeys.Arrow),
            "箭是类别键、不吃子弹零件，不该出现在 ammo.json");
    }

    [Fact]
    public void Missing_key_get_fails_fast()
    {
        Assert.Throws<KeyNotFoundException>(() => CombatCatalog.Section<AmmoConfig>().Get("no_such_ammo"));
    }

    [Fact]
    public void Exactly_four_craftable_bullets_no_more()
    {
        var cfg = CombatCatalog.Section<AmmoConfig>();
        Assert.Equal(4, cfg.ById.Count);
        // 表内每一条都是四种 golden 子弹之一（没有多余键）。
        var goldenKeys = new HashSet<string>();
        foreach (var (key, _) in Golden) goldenKeys.Add(key);
        foreach (var k in cfg.ById.Keys)
        {
            Assert.Contains(k, goldenKeys);
        }
    }

    [Fact]
    public void Every_ammo_survives_json_round_trip_bit_for_bit()
    {
        foreach (var (id, d) in CombatCatalog.Section<AmmoConfig>().ById)
        {
            string json = JsonSerializer.Serialize(d, CombatConfigLoader.Options);
            var back = JsonSerializer.Deserialize<AmmoDef>(json, CombatConfigLoader.Options)!;
            foreach (var p in typeof(AmmoDef).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.True(Equals(p.GetValue(d), p.GetValue(back)), $"{id}.{p.Name} 往返漂移");
            }
        }
    }
}
