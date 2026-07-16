using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 幸存者 perk 数值的零漂移 A/B 焊死】config-perks 单。
/// <para>
/// 诺蒂书虫 / 南丁格尔 / 山姆 / 耗子 / 皮特 五套 authored perk 的**可调数字常量**已从 C# <c>const</c> 搬到
/// <c>godot/data/config/perks.json</c>，各取用点身体改成 <c>=&gt; GameConfigCatalog.Section&lt;PerkConfig&gt;().X</c>
/// （照 <see cref="NightWatchConfig"/> 范式）。本文件钉死「搬家没搬错一个数」，并护住整套装配链。
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 TestGameConfigBootstrap 注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：每个数值逐条位级断言 == 迁移前原始常量（double 用 <see cref="BitConverter.DoubleToInt64Bits"/>，int 直等）。</item>
///   <item><b>取用点确实读 catalog</b>：各 perk 类静态属性 == PerkConfig 段值（证明委托到配置、非残留常量）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，全属性位级相等 ⇒ 加载器不丢精度。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 perks.json 经 FromJson 解析 == golden。</item>
///   <item><b>反射驱动 Parse 发现 Perks 段 + fail-fast</b>。</item>
/// </list>
/// <para>
/// ⚠️ perk 不进 Sim（Sim 工程未 Link 任何 perk 文件 ⇒ 结算路径读不到 perk 数值）⇒ 零漂移是**结构性**的，
/// 无需 Sim MD5；本文件的位级往返 + 字面锚定即零漂移铁证。
/// </para>
/// <para>
/// 📐 <b>只搬数值、保留 authored 结构</b>：本单未搬 perk 的分级逻辑 / 升级轴 / 身份名字 / 旗标 key / 噪音真值表
/// （仍在 SurvivorPerks.cs / PetePerk.cs）。既有 SurvivorPerksTests / SamPerkTests / RatPerkTests / PetePerkTests /
/// NurseRecruitTests / SeatReadingRuleTests 是 perk **行为**的护栏，本文件只护「数值真源＝perks.json」。
/// </para>
/// </summary>
public sealed class PerkConfigMigrationTests
{
    // ── 迁移前的原始常量（golden）——A/B 的"旧硬编码"一侧。改 perks.json 里这些值会让本表变红 ──
    // 诺蒂·书虫 / 读速
    private const double GoldBookwormL2Hours = 48;
    private const double GoldBookwormL3Hours = 120;
    private const double GoldBookwormSelfL1 = 0.25;
    private const double GoldBookwormSelfL2Plus = 0.50;
    private const double GoldBookwormCampWide = 0.25;
    private const double GoldNoSeat = 0.9;
    private const double GoldMissingPrereq = 0.2;
    // 南丁格尔
    private const int GoldNurseL2Surgeries = 3;
    private const int GoldNurseL3Surgeries = 8;
    private const int GoldDefaultSurgeryPoints = 15;
    private const int GoldNurseSurgeryPoints = 30;
    private const int GoldCampSurgeryBonus = 5;
    private const double GoldNurseL2Infection = 0.15;
    private const double GoldNurseL3Infection = 0.10;
    // 山姆
    private const int GoldSamL2Pop = 3;
    private const int GoldSamL3Pop = 6;
    private const double GoldSamL1Damage = 0.10;
    private const double GoldSamL2Carry = 0.15;
    private const double GoldSamAuraCarry = 0.03;
    private const double GoldSamAuraWork = 0.03;
    private const double GoldSamAuraHeal = 0.03;
    private const double GoldSamAuraInfection = 0.03;
    // 耗子
    private const int GoldRatL2Items = 75;
    private const int GoldRatL3Items = 250;
    private const double GoldRatActionNoise = 0.60;
    private const double GoldRatL1Loot = 0.50;
    private const double GoldRatL2Loot = 1.00;
    private const double GoldRatL3Stealth = 0.40;
    private const double GoldRatL3Ambush = 0.35;
    // 皮特
    private const double GoldPeteL1Move = 1.15;
    private const double GoldPeteL2Move = 1.25;
    private const double GoldPeteL3Move = 1.30;
    private const double GoldPeteOperation = 0.05;
    private const double GoldPeteDodge = 0.15;
    private const double GoldPeteDodgeKg = 30.0;
    private const double GoldPeteExtraHunger = 0.25;
    private const int GoldPeteHungerThreshold = 3;
    private const int GoldPeteConsecutivePhases = 10;
    private const int GoldPeteDepartureCeiling = 5;
    private const int GoldPeteDepartureCount = 3;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        double v = BookwormPerk.Level2ThresholdHours;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取 perk 数值后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        BitEqual(GoldBookwormL2Hours, v);
    }

    // ── 字面值锚定（A/B）：每个数值 × 位级/整数 == 迁移前原始常量 ──────────────────
    [Fact]
    public void Bookworm_and_reading_values_match_original_literals()
    {
        BitEqual(GoldBookwormL2Hours, BookwormPerk.Level2ThresholdHours);
        BitEqual(GoldBookwormL3Hours, BookwormPerk.Level3ThresholdHours);
        BitEqual(GoldBookwormSelfL1, BookwormPerk.BonusForLevel(1));
        BitEqual(GoldBookwormSelfL2Plus, BookwormPerk.BonusForLevel(2));
        BitEqual(GoldBookwormSelfL2Plus, BookwormPerk.BonusForLevel(3));
        BitEqual(GoldBookwormCampWide, BookwormPerk.CampWideBonusAtMax);
        BitEqual(GoldNoSeat, ReadingSpeed.NoSeatMultiplier);
        BitEqual(GoldMissingPrereq, ReadingSpeed.MissingPrerequisiteMultiplier);
    }

    [Fact]
    public void Nightingale_values_match_original_literals()
    {
        Assert.Equal(GoldNurseL2Surgeries, NightingalePerk.Level2ThresholdSurgeries);
        Assert.Equal(GoldNurseL3Surgeries, NightingalePerk.Level3ThresholdSurgeries);
        Assert.Equal(GoldDefaultSurgeryPoints, NightingalePerk.DefaultSurgeryBasePoints);
        Assert.Equal(GoldNurseSurgeryPoints, NightingalePerk.NightingaleSurgeryBasePoints);
        Assert.Equal(GoldCampSurgeryBonus, NightingalePerk.CampSurgeryBaseBonus);
        BitEqual(GoldNurseL2Infection, NightingalePerk.Level2InfectionReduction);
        BitEqual(GoldNurseL3Infection, NightingalePerk.Level3InfectionReduction);
    }

    [Fact]
    public void Sam_values_match_original_literals()
    {
        Assert.Equal(GoldSamL2Pop, SamPerk.Level2CampPopulation);
        Assert.Equal(GoldSamL3Pop, SamPerk.Level3CampPopulation);
        BitEqual(GoldSamL1Damage, SamPerk.Level1DamageReduction);
        BitEqual(GoldSamL2Carry, SamPerk.Level2CarryBonus);
        BitEqual(GoldSamAuraCarry, SamPerk.AuraCarryBonus);
        BitEqual(GoldSamAuraWork, SamPerk.AuraWorkSpeedBonus);
        BitEqual(GoldSamAuraHeal, SamPerk.AuraHealSpeedBonus);
        BitEqual(GoldSamAuraInfection, SamPerk.AuraInfectionWorsenReduction);
    }

    [Fact]
    public void Rat_values_match_original_literals()
    {
        Assert.Equal(GoldRatL2Items, RatPerk.Level2ThresholdItems);
        Assert.Equal(GoldRatL3Items, RatPerk.Level3ThresholdItems);
        BitEqual(GoldRatActionNoise, RatPerk.Level1ActionNoiseMultiplier);
        BitEqual(GoldRatL1Loot, RatPerk.Level1LootSpeedBonus);
        BitEqual(GoldRatL2Loot, RatPerk.Level2LootSpeedBonus);
        BitEqual(GoldRatL3Stealth, RatPerk.Level3DarknessStealthBonus);
        BitEqual(GoldRatL3Ambush, RatPerk.Level3AmbushDamageBonus);
    }

    [Fact]
    public void Pete_values_match_original_literals()
    {
        BitEqual(GoldPeteL1Move, PetePerk.Level1MoveSpeedMultiplier);
        BitEqual(GoldPeteL2Move, PetePerk.Level2MoveSpeedMultiplier);
        BitEqual(GoldPeteL3Move, PetePerk.Level3MoveSpeedMultiplier);
        BitEqual(GoldPeteOperation, PetePerk.OperationCapabilityBonus);
        BitEqual(GoldPeteDodge, PetePerk.DodgeChanceValue);
        BitEqual(GoldPeteDodgeKg, PetePerk.DodgeMaxCarriedKg);
        BitEqual(GoldPeteExtraHunger, PetePerk.ExtraHungerDropChance);
        Assert.Equal(GoldPeteHungerThreshold, PetePerk.HungerThresholdForStreak);
        Assert.Equal(GoldPeteConsecutivePhases, PetePerk.Level2ConsecutivePhases);
        Assert.Equal(GoldPeteDepartureCeiling, PetePerk.DepartureHungerCeiling);
        Assert.Equal(GoldPeteDepartureCount, PetePerk.Level3DepartureCount);
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void Perk_classes_read_from_catalog_section()
    {
        var s = GameConfigCatalog.Section<PerkConfig>();
        BitEqual(s.BookwormLevel2ThresholdHours, BookwormPerk.Level2ThresholdHours);
        BitEqual(s.BookwormLevel3ThresholdHours, BookwormPerk.Level3ThresholdHours);
        BitEqual(s.BookwormSelfBonusL1, BookwormPerk.BonusForLevel(1));
        BitEqual(s.BookwormSelfBonusL2Plus, BookwormPerk.BonusForLevel(2));
        BitEqual(s.BookwormCampWideBonusAtMax, BookwormPerk.CampWideBonusAtMax);
        BitEqual(s.ReadingNoSeatMultiplier, ReadingSpeed.NoSeatMultiplier);
        BitEqual(s.ReadingMissingPrerequisiteMultiplier, ReadingSpeed.MissingPrerequisiteMultiplier);

        Assert.Equal(s.NightingaleLevel2ThresholdSurgeries, NightingalePerk.Level2ThresholdSurgeries);
        Assert.Equal(s.NightingaleLevel3ThresholdSurgeries, NightingalePerk.Level3ThresholdSurgeries);
        Assert.Equal(s.NightingaleDefaultSurgeryBasePoints, NightingalePerk.DefaultSurgeryBasePoints);
        Assert.Equal(s.NightingaleSurgeryBasePoints, NightingalePerk.NightingaleSurgeryBasePoints);
        Assert.Equal(s.NightingaleCampSurgeryBaseBonus, NightingalePerk.CampSurgeryBaseBonus);
        BitEqual(s.NightingaleLevel2InfectionReduction, NightingalePerk.Level2InfectionReduction);
        BitEqual(s.NightingaleLevel3InfectionReduction, NightingalePerk.Level3InfectionReduction);

        Assert.Equal(s.SamLevel2CampPopulation, SamPerk.Level2CampPopulation);
        Assert.Equal(s.SamLevel3CampPopulation, SamPerk.Level3CampPopulation);
        BitEqual(s.SamLevel1DamageReduction, SamPerk.Level1DamageReduction);
        BitEqual(s.SamLevel2CarryBonus, SamPerk.Level2CarryBonus);
        BitEqual(s.SamAuraCarryBonus, SamPerk.AuraCarryBonus);
        BitEqual(s.SamAuraWorkSpeedBonus, SamPerk.AuraWorkSpeedBonus);
        BitEqual(s.SamAuraHealSpeedBonus, SamPerk.AuraHealSpeedBonus);
        BitEqual(s.SamAuraInfectionWorsenReduction, SamPerk.AuraInfectionWorsenReduction);

        Assert.Equal(s.RatLevel2ThresholdItems, RatPerk.Level2ThresholdItems);
        Assert.Equal(s.RatLevel3ThresholdItems, RatPerk.Level3ThresholdItems);
        BitEqual(s.RatLevel1ActionNoiseMultiplier, RatPerk.Level1ActionNoiseMultiplier);
        BitEqual(s.RatLevel1LootSpeedBonus, RatPerk.Level1LootSpeedBonus);
        BitEqual(s.RatLevel2LootSpeedBonus, RatPerk.Level2LootSpeedBonus);
        BitEqual(s.RatLevel3DarknessStealthBonus, RatPerk.Level3DarknessStealthBonus);
        BitEqual(s.RatLevel3AmbushDamageBonus, RatPerk.Level3AmbushDamageBonus);

        BitEqual(s.PeteLevel1MoveSpeedMultiplier, PetePerk.Level1MoveSpeedMultiplier);
        BitEqual(s.PeteLevel2MoveSpeedMultiplier, PetePerk.Level2MoveSpeedMultiplier);
        BitEqual(s.PeteLevel3MoveSpeedMultiplier, PetePerk.Level3MoveSpeedMultiplier);
        BitEqual(s.PeteOperationCapabilityBonus, PetePerk.OperationCapabilityBonus);
        BitEqual(s.PeteDodgeChanceValue, PetePerk.DodgeChanceValue);
        BitEqual(s.PeteDodgeMaxCarriedKg, PetePerk.DodgeMaxCarriedKg);
        BitEqual(s.PeteExtraHungerDropChance, PetePerk.ExtraHungerDropChance);
        Assert.Equal(s.PeteHungerThresholdForStreak, PetePerk.HungerThresholdForStreak);
        Assert.Equal(s.PeteLevel2ConsecutivePhases, PetePerk.Level2ConsecutivePhases);
        Assert.Equal(s.PeteDepartureHungerCeiling, PetePerk.DepartureHungerCeiling);
        Assert.Equal(s.PeteLevel3DepartureCount, PetePerk.Level3DepartureCount);
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = GoldenSection();
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<PerkConfig>(json, GameConfigLoader.Options)!;
        AssertSectionBitEqual(golden, back);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 perks.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_perks_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "perks.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new PerkConfig();
        var loaded = (PerkConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        AssertSectionBitEqual(GoldenSection(), loaded);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Perks 段 ─────────────────────
    [Fact]
    public void Loader_reflection_discovers_perks_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        BitEqual(GoldRatActionNoise, cfg.Perks.RatLevel1ActionNoiseMultiplier);
        Assert.Equal(GoldPeteDepartureCount, cfg.Perks.PeteLevel3DepartureCount);
    }

    // ── 坏 json fail-fast（段自解析层）─────────────────────────────────────────────
    [Fact]
    public void Section_bad_json_fails_fast()
        => Assert.Throws<JsonException>(() =>
            new PerkConfig().FromJson("{ not valid json ", GameConfigLoader.Options));

    // ── golden 段（= 迁移前全部原始常量）─────────────────────────────────────────
    private static PerkConfig GoldenSection() => new()
    {
        BookwormLevel2ThresholdHours = GoldBookwormL2Hours,
        BookwormLevel3ThresholdHours = GoldBookwormL3Hours,
        BookwormSelfBonusL1 = GoldBookwormSelfL1,
        BookwormSelfBonusL2Plus = GoldBookwormSelfL2Plus,
        BookwormCampWideBonusAtMax = GoldBookwormCampWide,
        ReadingNoSeatMultiplier = GoldNoSeat,
        ReadingMissingPrerequisiteMultiplier = GoldMissingPrereq,
        NightingaleLevel2ThresholdSurgeries = GoldNurseL2Surgeries,
        NightingaleLevel3ThresholdSurgeries = GoldNurseL3Surgeries,
        NightingaleDefaultSurgeryBasePoints = GoldDefaultSurgeryPoints,
        NightingaleSurgeryBasePoints = GoldNurseSurgeryPoints,
        NightingaleCampSurgeryBaseBonus = GoldCampSurgeryBonus,
        NightingaleLevel2InfectionReduction = GoldNurseL2Infection,
        NightingaleLevel3InfectionReduction = GoldNurseL3Infection,
        SamLevel2CampPopulation = GoldSamL2Pop,
        SamLevel3CampPopulation = GoldSamL3Pop,
        SamLevel1DamageReduction = GoldSamL1Damage,
        SamLevel2CarryBonus = GoldSamL2Carry,
        SamAuraCarryBonus = GoldSamAuraCarry,
        SamAuraWorkSpeedBonus = GoldSamAuraWork,
        SamAuraHealSpeedBonus = GoldSamAuraHeal,
        SamAuraInfectionWorsenReduction = GoldSamAuraInfection,
        RatLevel2ThresholdItems = GoldRatL2Items,
        RatLevel3ThresholdItems = GoldRatL3Items,
        RatLevel1ActionNoiseMultiplier = GoldRatActionNoise,
        RatLevel1LootSpeedBonus = GoldRatL1Loot,
        RatLevel2LootSpeedBonus = GoldRatL2Loot,
        RatLevel3DarknessStealthBonus = GoldRatL3Stealth,
        RatLevel3AmbushDamageBonus = GoldRatL3Ambush,
        PeteLevel1MoveSpeedMultiplier = GoldPeteL1Move,
        PeteLevel2MoveSpeedMultiplier = GoldPeteL2Move,
        PeteLevel3MoveSpeedMultiplier = GoldPeteL3Move,
        PeteOperationCapabilityBonus = GoldPeteOperation,
        PeteDodgeChanceValue = GoldPeteDodge,
        PeteDodgeMaxCarriedKg = GoldPeteDodgeKg,
        PeteExtraHungerDropChance = GoldPeteExtraHunger,
        PeteHungerThresholdForStreak = GoldPeteHungerThreshold,
        PeteLevel2ConsecutivePhases = GoldPeteConsecutivePhases,
        PeteDepartureHungerCeiling = GoldPeteDepartureCeiling,
        PeteLevel3DepartureCount = GoldPeteDepartureCount,
    };

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>double 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    /// <summary>反射比对两个段实例的全部 double/int 属性（double 位级、int 直等）。</summary>
    private static void AssertSectionBitEqual(PerkConfig a, PerkConfig b)
    {
        foreach (var p in typeof(PerkConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? va = p.GetValue(a);
            object? vb = p.GetValue(b);
            if (va is double da && vb is double db)
            {
                Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                    $"{p.Name} double 漂移：{da} vs {db}");
            }
            else if (va is int ia && vb is int ib)
            {
                Assert.True(ia == ib, $"{p.Name} int 漂移：{ia} vs {ib}");
            }
        }
    }

    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
