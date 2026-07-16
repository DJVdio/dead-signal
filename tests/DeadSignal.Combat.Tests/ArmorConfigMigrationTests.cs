using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【数值外置 · armor 段的零漂移 A/B 焊死】config-armor 单（照 <see cref="WeaponConfigMigrationTests"/> 立范式）。
/// <para>
/// 护甲数值已从 <see cref="ArmorTable"/> 的 C# 常量搬到 <c>godot/data/config/armor.json</c>，
/// 工厂方法（<c>ArmorTable.Plate()</c> …）身体改成 <c>=&gt; Cfg("plate")</c>（读 <see cref="ArmorConfig"/> 段）。
/// 本文件钉死「搬家没搬错一个数」：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c> 注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：抽样护甲的每一类字段（double/枚举/可空集合/集合内容）逐条断言 == 迁移前原始常量。
///     <b>double 用位级相等</b>（12.5 → "12.5" → 12.5 一位不差）；集合用 <c>SetEquals</c>。</item>
///   <item><b>往返保真</b>：catalog 里每件护甲再序列化→反序列化，逐字段位级相等（<see cref="ArmorLayer.CoversParts"/>
///     是 <c>IReadOnlySet&lt;string&gt;</c>，经 <see cref="ReadOnlyStringSetJsonConverter"/> 往返）⇒ 加载器不丢精度、不掉部位。</item>
///   <item><b>完整性/顺序</b>：33 件 id 全可取；ZombieHide 单层组表、SurvivorArmor 组合、DescriptionOf 风味表全对。</item>
///   <item><b>枚举出字符串</b>：<see cref="ArmorSlot"/> 序列化为 "Skin"/"Plate"（非序号 0/1/2）。</item>
/// </list>
/// <para>
/// 🔴 <b>另有一条更强的零漂移证明在 test 之外</b>：整表蒙特卡洛 Sim 输出迁移前后 MD5 完全一致
/// （<c>44c28a8efe62f118c2322ad2de38f432</c>，护甲进 Sim 结算——Program.cs 的具名甲组含长袖布衣/皮夹克/板甲/粗布外套）。
/// </para>
/// </summary>
public sealed class ArmorConfigMigrationTests
{
    // 迁移前 ArmorTable 里的原始常量（golden）——手抄自迁移前工厂 literal，作为 A/B 的"旧硬编码"一侧。

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        var plate = ArmorTable.Plate();   // 首次访问触发懒加载
        Assert.True(CombatCatalog.IsInitialized, "首次取护甲后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal("板甲", plate.Name);
    }

    [Fact]
    public void All_33_ids_resolve_with_nonempty_name()
    {
        var section = CombatCatalog.Section<ArmorConfig>();
        foreach (var id in AllIds)
        {
            var a = section.Get(id);
            Assert.False(string.IsNullOrEmpty(a.Name), $"{id} 名字不应为空");
        }
        Assert.Equal(33, section.ById.Count);
    }

    [Fact]
    public void Missing_id_fails_fast()
    {
        Assert.Throws<KeyNotFoundException>(() => CombatCatalog.Section<ArmorConfig>().Get("no_such_armor"));
    }

    // ── 字面值锚定（A/B）：抽样护甲 × 每类字段，== 迁移前原始常量 ──────────────────────

    [Fact]
    public void Plate_matches_original_literals()
    {
        var a = ArmorTable.Plate();
        Assert.Equal("板甲", a.Name);
        Assert.Equal("重吗？他能保护你脆弱的肉体。", a.Description);
        Assert.Equal(70, a.SharpDefense);
        Assert.Equal(35, a.BluntDefense);
        Assert.Equal(15, a.Weight);
        Assert.Equal(ArmorSlot.Plate, a.Slot);
        Assert.NotNull(a.CoversParts);
        Assert.True(a.CoversParts!.SetEquals(new[]
        {
            "胸", "腹", "左手臂", "右手臂", "左大腿", "右大腿", "左小腿", "右小腿",
        }), "板甲覆盖=躯干+双臂+双腿");
    }

    [Fact]
    public void Fractional_defenses_and_weights_round_trip_exactly()
    {
        ExactlyEqual(12.5, ArmorTable.ChestPlate().BluntDefense);   // 唯一 .5 防御
        ExactlyEqual(12.5, ArmorTable.Leather().BluntDefense);
        ExactlyEqual(0.15, ArmorTable.LongSleeveShirt().Weight);   // 0.15 位级锚
        ExactlyEqual(0.75, ArmorTable.AnkleGuard().Weight);
        ExactlyEqual(2.5, ArmorTable.MilitaryHelmet().Weight);
        ExactlyEqual(4.5, ArmorTable.RiotHelmet().Weight);
        ExactlyEqual(0.1, ArmorTable.Sunglasses().Weight);
    }

    [Fact]
    public void Slot_layers_are_preserved_across_sample()
    {
        Assert.Equal(ArmorSlot.Skin, ArmorTable.LongSleeveShirt().Slot);   // 贴身
        Assert.Equal(ArmorSlot.Outer, ArmorTable.LeatherJacket().Slot);    // 外套
        Assert.Equal(ArmorSlot.Plate, ArmorTable.ChestPlate().Slot);       // 装甲
        Assert.Equal(ArmorSlot.Skin, ArmorTable.BallisticVest().Slot);     // 🔴 防弹背心是贴身层不是装甲层
        Assert.Equal(ArmorSlot.Plate, ArmorTable.MilitaryHelmet().Slot);   // 头盔=Plate 层序（不占装甲层槽由消费层管）
    }

    [Fact]
    public void Eye_and_head_coverage_sets_preserved()
    {
        Assert.True(ArmorTable.Sunglasses().CoversParts!.SetEquals(new[] { "左眼", "右眼" }));
        Assert.True(ArmorTable.PlainGlasses().CoversParts!.SetEquals(new[] { "左眼", "右眼" }));
        Assert.True(ArmorTable.RiotHelmet().CoversParts!.SetEquals(new[] { "头", "左眼", "右眼", "鼻", "下巴" }));
        Assert.True(ArmorTable.BallisticVest().CoversParts!.SetEquals(new[] { "胸", "腹" }));
    }

    [Fact]
    public void Null_coverage_for_innate_zombie_hide()
    {
        var hide = ArmorTable.ZombieHide();
        Assert.Single(hide);
        var layer = hide[0];
        Assert.Equal("腐皮", layer.Name);
        Assert.Equal(3, layer.SharpDefense);
        Assert.Equal(3, layer.BluntDefense);
        Assert.Equal(0, layer.Weight);
        Assert.Null(layer.CoversParts);   // 全身覆盖=null（零漂移锚）
    }

    [Fact]
    public void Survivor_armor_composition_unchanged()
    {
        var set = ArmorTable.SurvivorArmor();
        Assert.Equal(2, set.Count);
        Assert.Equal("皮夹克", set[0].Name);      // 外套
        Assert.Equal("长袖布衣", set[1].Name);    // 贴身
    }

    [Fact]
    public void Flavor_description_table_still_resolves()
    {
        Assert.Equal("重吗？他能保护你脆弱的肉体。", ArmorTable.DescriptionOf("板甲"));
        Assert.Equal("给可爱的狗狗穿上可爱的衣服。", ArmorTable.DescriptionOf("布制狗衣"));
        Assert.Equal("", ArmorTable.DescriptionOf("不存在的护甲"));
    }

    // ── 往返保真：加载器不丢精度/不掉部位（值无关，永久护栏）──────────────────────────

    [Fact]
    public void Every_armor_survives_json_round_trip_bit_for_bit()
    {
        foreach (var (id, a) in CombatCatalog.Section<ArmorConfig>().ById)
        {
            string json = JsonSerializer.Serialize(a, CombatConfigLoader.Options);
            var back = JsonSerializer.Deserialize<ArmorLayer>(json, CombatConfigLoader.Options)!;
            AssertArmorBitEqual(a, back, id);
        }
    }

    [Fact]
    public void Slot_enum_serializes_as_string_not_ordinal()
    {
        string json = JsonSerializer.Serialize(ArmorTable.LongSleeveShirt(), CombatConfigLoader.Options);
        Assert.Contains("\"Slot\": \"Skin\"", json);   // 出字符串，不是 2
        Assert.DoesNotContain("\"Slot\": 2", json);
        string pj = JsonSerializer.Serialize(ArmorTable.Plate(), CombatConfigLoader.Options);
        Assert.Contains("\"Slot\": \"Plate\"", pj);
        Assert.DoesNotContain("\"Slot\": 0", pj);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] AllIds =
    {
        "long_sleeve_shirt", "floral_shirt", "trousers", "sneakers", "shorts", "chest_plate",
        "coarse_cloth_vest", "coarse_cloth_coat", "cloth_jacket", "denim_jacket", "leather_jacket",
        "leather", "plate", "military_helmet", "riot_helmet", "work_gloves", "war_mask", "cotton_hat",
        "coarse_cloth_shirt", "coarse_shorts", "coarse_trousers", "horror_armor", "sunglasses",
        "plain_glasses", "self_made_snow_goggles", "ankle_guard", "ballistic_vest",
        "dog_cloth_vest", "dog_leather_vest", "dog_pocket_vest", "dog_iron_helmet", "dog_wire_helmet",
        "zombie_hide",
    };

    /// <summary>double 位级相等（比 == 更严：连 -0.0/NaN 都区分）——证明"往返一位不差"。</summary>
    private static void ExactlyEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    /// <summary>反射比对两件护甲的全部可读属性（double 走位级、集合走 SetEquals、可空/枚举/字符串照常）。</summary>
    private static void AssertArmorBitEqual(ArmorLayer a, ArmorLayer b, string id)
    {
        foreach (var p in typeof(ArmorLayer).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? va = p.GetValue(a);
            object? vb = p.GetValue(b);
            if (va is double da && vb is double db)
            {
                Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                    $"{id}.{p.Name} double 漂移：{da} vs {db}");
            }
            else if (va is IReadOnlySet<string> sa && vb is IReadOnlySet<string> sb)
            {
                Assert.True(sa.SetEquals(sb), $"{id}.{p.Name} 覆盖部位集合漂移");
            }
            else
            {
                Assert.True(Equals(va, vb), $"{id}.{p.Name} 不等：{va} vs {vb}");
            }
        }
    }
}
