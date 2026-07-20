using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【感染 + 医疗数值外置 · godot 侧配置范式的零漂移 A/B 焊死】config-health 单。
/// <para>
/// <see cref="HealthConditionSet"/> 的恶化/愈合速率、感染几率基数、免疫窗、手术点数/阈值，以及三张目录
/// （<see cref="MedicineCatalog"/>/<see cref="SurgeryCatalog"/>/<see cref="MedicalBookPoints"/>）的**逐条可调数字**
/// 已从 C# <c>const</c> 搬到 <c>godot/data/config/health.json</c>，静态取用点身体改成
/// <c>=&gt; GameConfigCatalog.Section&lt;HealthConfig&gt;().X</c>；目录的 <c>For</c> 保留 authored 结构、只把数字读 catalog。
/// </para>
/// <para>
/// ⚠️ 感染/医疗是 <c>HealthConditions</c> 纯逻辑、<b>不进 Duel/Sim 战斗结算</b>（设计文档载明）——零漂移由本文件的
/// <b>位级往返 + 字面锚定</b>证明，不跑 Sim MD5。本文件同时钉死「只搬数字、authored 结构没搬走」：
/// 目录的 Treats/Exclusive/RequiresNoSupplies 仍来自代码。
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：首次取 <see cref="HealthConditionSet"/> 静态属性触发 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：段的全部标量 + 三张目录数字逐条位级断言 == 迁移前原始常量。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="HealthConditionSet"/> 公开静态属性 == 段值；目录 <c>For</c> 出的数字 == 段值。</item>
///   <item><b>authored 结构留代码</b>：目录 Treats/Exclusive/RequiresNoSupplies 与迁移前一致（不随数字外置漂走）。</item>
///   <item><b>往返保真 + 盘上文件解析 + 反射发现 + fail-fast</b>。</item>
/// </list>
/// </summary>
public sealed class HealthConfigMigrationTests
{
    // ── 迁移前 HealthConditions.cs 里的原始常量（golden）——A/B 的「旧硬编码」一侧 ──
    private const double GBleedWorsenPerDay = 0.10;
    private const double GInfectionInitialSeverity = 0.0;
    private const double GInfectionBaseChanceLarge = 0.25;
    private const double GInfectionBaseChanceMedium = 0.15;
    private const double GInfectionBaseChanceSmall = 0.05;
    private const double GImmuneWindowDays = 1.0;
    private const double GImmuneWindowInfectionFactor = 0.05;
    private const double GInfectionWorsenPerDay = 1.0 / 6.0;
    private const double GCureProgressBaseRate = 0.67;
    private const double GFractureMalunionPerDay = 0.05;
    private const double GMinorBleedSeverityCap = 0.6;
    private const double GWoundClosedThreshold = 0.15;
    private const double GOperatedInfectionFactor = 0.5;
    private const int GInfectionWindowDays = 4;
    private const double GAbrasionSeverityThreshold = 0.2;
    private const double GBleedHealPerDay = 0.20;
    private const double GFractureHealPerDay = 0.24;
    private const int GSurgeryBasePoints = 15;
    private const int GBedBonusPoints = 10;
    private const int GSurgeryFailThreshold = 10;
    private const int GSurgeryMinPoints = 15;
    private const double GSelfSurgeryFactor = 0.60;
    private const double GImmediateHealOnSuccess = 0.05;
    private const double GBedSleepHealBonusPct = 10.0;
    private const double GRosehipTeaHealBonusPct = 9.0;   // 原 Pawn.RosehipTeaHealBonusPct const（config-cleanup 外置）
    private const int GRedoSurgeryCooldownDays = 1;

    private static HealthConfig Section() => GameConfigCatalog.Section<HealthConfig>();

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        int basePoints = HealthConditionSet.SurgeryBasePoints; // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取手术基础点后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(GSurgeryBasePoints, basePoints);
    }

    // ── 字面值锚定（A/B）：段的全部标量 × 位级 == 迁移前原始常量 ──
    [Fact]
    public void Section_scalars_match_original_literals()
    {
        var s = Section();
        BitEqual(GBleedWorsenPerDay, s.BleedWorsenPerDay);
        BitEqual(GInfectionInitialSeverity, s.InfectionInitialSeverity);
        BitEqual(GInfectionBaseChanceLarge, s.InfectionBaseChanceLarge);
        BitEqual(GInfectionBaseChanceMedium, s.InfectionBaseChanceMedium);
        BitEqual(GInfectionBaseChanceSmall, s.InfectionBaseChanceSmall);
        BitEqual(GImmuneWindowDays, s.ImmuneWindowDays);
        BitEqual(GImmuneWindowInfectionFactor, s.ImmuneWindowInfectionFactor);
        BitEqual(GInfectionWorsenPerDay, s.InfectionWorsenPerDay);
        BitEqual(GCureProgressBaseRate, s.CureProgressBaseRate);
        BitEqual(GFractureMalunionPerDay, s.FractureMalunionPerDay);
        BitEqual(GMinorBleedSeverityCap, s.MinorBleedSeverityCap);
        BitEqual(GWoundClosedThreshold, s.WoundClosedThreshold);
        BitEqual(GOperatedInfectionFactor, s.OperatedInfectionFactor);
        Assert.Equal(GInfectionWindowDays, s.InfectionWindowDays);
        BitEqual(GAbrasionSeverityThreshold, s.AbrasionSeverityThreshold);
        BitEqual(GBleedHealPerDay, s.BleedHealPerDay);
        BitEqual(GFractureHealPerDay, s.FractureHealPerDay);
        Assert.Equal(GSurgeryBasePoints, s.SurgeryBasePoints);
        Assert.Equal(GBedBonusPoints, s.BedBonusPoints);
        Assert.Equal(GSurgeryFailThreshold, s.SurgeryFailThreshold);
        Assert.Equal(GSurgeryMinPoints, s.SurgeryMinPoints);
        BitEqual(GSelfSurgeryFactor, s.SelfSurgeryFactor);
        BitEqual(GImmediateHealOnSuccess, s.ImmediateHealOnSuccess);
        BitEqual(GBedSleepHealBonusPct, s.BedSleepHealBonusPct);
        BitEqual(GRosehipTeaHealBonusPct, s.RosehipTeaHealBonusPct);
        Assert.Equal(GRedoSurgeryCooldownDays, s.RedoSurgeryCooldownDays);
    }

    // ── 字面值锚定（A/B）：三张目录的逐条数字 == 迁移前原始常量 ──
    [Fact]
    public void Section_catalog_numbers_match_original_literals()
    {
        var s = Section();

        // 感染药三档双效（抗生素 1.00/0.50、草药膏 0.35/0.75、蒲公英茶 0.15/0.85）。
        Assert.Equal(3, s.Medicines.Count);
        AssertMedicine(s, "antibiotics", 1.00, 0.50);
        AssertMedicine(s, "herbal_salve", 0.35, 0.75);
        AssertMedicine(s, "dandelion_tea", 0.15, 0.85);

        // 手术耗材供点 + 感染乘子（草药绷带 20 + 0.75）。
        Assert.Equal(5, s.SurgerySupplies.Count);
        AssertSupply(s, "bandage", 15, 1.0);
        AssertSupply(s, "herbal_bandage", 20, 0.75);
        AssertSupply(s, "needle_thread", 15, 1.0);
        AssertSupply(s, "splint", 25, 1.0);
        AssertSupply(s, "first_aid_kit", 60, 1.0);

        // 医疗书加点（野外生存指南 +3）。
        Assert.Single(s.MedicalBooks);
        Assert.Equal(3, s.MedicalBooks["wilderness_survival_guide"]);
    }

    // ── 取用点确实读 catalog（公开静态属性 + 目录 For 出的数字都 == 段值）──
    [Fact]
    public void Take_points_read_from_catalog_section()
    {
        var s = Section();
        // HealthConditionSet 公开静态属性委托到配置（证明非残留 const）。
        Assert.Equal(s.SurgeryBasePoints, HealthConditionSet.SurgeryBasePoints);
        Assert.Equal(s.BedBonusPoints, HealthConditionSet.BedBonusPoints);
        Assert.Equal(s.SurgeryFailThreshold, HealthConditionSet.SurgeryFailThreshold);
        Assert.Equal(s.SurgeryMinPoints, HealthConditionSet.SurgeryMinPoints);
        Assert.Equal(s.InfectionWindowDays, HealthConditionSet.InfectionWindowDays);
        Assert.Equal(s.RedoSurgeryCooldownDays, HealthConditionSet.RedoSurgeryCooldownDays);
        BitEqual(s.SelfSurgeryFactor, HealthConditionSet.SelfSurgeryFactor);
        BitEqual(s.ImmediateHealOnSuccess, HealthConditionSet.ImmediateHealOnSuccess);
        BitEqual(s.BedSleepHealBonusPct, HealthConditionSet.BedSleepHealBonusPct);
        BitEqual(s.MinorBleedSeverityCap, HealthConditionSet.MinorBleedSeverityCap);
        BitEqual(s.AbrasionSeverityThreshold, HealthConditionSet.AbrasionSeverityThreshold);
        BitEqual(s.CureProgressBaseRate, HealthConditionSet.CureProgressBaseRate);

        // 目录 For 出的数字 == 段值（证明目录也吃 config，不是残留字面）。
        Medicine anti = MedicineCatalog.For("antibiotics")!.Value;
        BitEqual(s.Medicines["antibiotics"].Efficacy, anti.Efficacy);
        BitEqual(s.Medicines["antibiotics"].WorsenMultiplier, anti.WorsenMultiplier);

        SurgerySupply herb = SurgeryCatalog.For("herbal_bandage")!.Value;
        Assert.Equal(s.SurgerySupplies["herbal_bandage"].Points, herb.Points);
        BitEqual(s.SurgerySupplies["herbal_bandage"].InfectionChanceMultiplier, herb.InfectionChanceMultiplier);

        Assert.Equal(s.MedicalBooks["wilderness_survival_guide"], MedicalBookPoints.For("wilderness_survival_guide"));
    }

    // ── authored 结构留代码：目录 Treats/Exclusive/RequiresNoSupplies 与迁移前一致（不随数字外置漂走）──
    [Fact]
    public void Authored_structure_stays_in_code()
    {
        Assert.Equal(HealthConditionType.Infection, MedicineCatalog.For("antibiotics")!.Value.Treats);
        Assert.Equal(HealthConditionType.Infection, MedicineCatalog.For("herbal_salve")!.Value.Treats);
        Assert.Null(MedicineCatalog.For("medicine"));
        Assert.True(MedicineCatalog.IsMedicine("antibiotics"));
        Assert.Null(MedicineCatalog.For("not_a_drug"));

        SurgerySupply bandage = SurgeryCatalog.For("bandage")!.Value;
        Assert.False(bandage.Exclusive);
        Assert.True(bandage.CanTreat(HealthConditionType.Bleeding));
        Assert.False(bandage.CanTreat(HealthConditionType.Fracture));

        SurgerySupply kit = SurgeryCatalog.For("first_aid_kit")!.Value;
        Assert.True(kit.Exclusive);
        Assert.True(kit.CanTreat(HealthConditionType.Bleeding));
        Assert.True(kit.CanTreat(HealthConditionType.Fracture));
        Assert.True(SurgeryCatalog.For("splint")!.Value.CanTreat(HealthConditionType.Fracture));
        Assert.Null(SurgeryCatalog.For("not_a_supply"));

        Assert.True(MedicalBookPoints.RequiresNoSupplies("wilderness_survival_guide"));
        Assert.True(MedicalBookPoints.IsMedicalBook("wilderness_survival_guide"));
        Assert.Equal(0, MedicalBookPoints.For("not_a_book"));
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）──
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = new HealthConfig();
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<HealthConfig>(json, GameConfigLoader.Options)!;

        BitEqual(golden.InfectionWorsenPerDay, back.InfectionWorsenPerDay);
        BitEqual(golden.CureProgressBaseRate, back.CureProgressBaseRate);
        BitEqual(golden.BedSleepHealBonusPct, back.BedSleepHealBonusPct);
        BitEqual(golden.RosehipTeaHealBonusPct, back.RosehipTeaHealBonusPct);
        Assert.Equal(golden.SurgeryBasePoints, back.SurgeryBasePoints);
        Assert.Equal(golden.InfectionWindowDays, back.InfectionWindowDays);

        Assert.Equal(golden.Medicines.Count, back.Medicines.Count);
        foreach (KeyValuePair<string, MedicineNumbers> kv in golden.Medicines)
        {
            MedicineNumbers b = back.Medicines[kv.Key];
            BitEqual(kv.Value.Efficacy, b.Efficacy);
            BitEqual(kv.Value.WorsenMultiplier, b.WorsenMultiplier);
        }
        Assert.Equal(golden.SurgerySupplies.Count, back.SurgerySupplies.Count);
        foreach (KeyValuePair<string, SurgerySupplyNumbers> kv in golden.SurgerySupplies)
        {
            SurgerySupplyNumbers b = back.SurgerySupplies[kv.Key];
            Assert.Equal(kv.Value.Points, b.Points);
            BitEqual(kv.Value.InfectionChanceMultiplier, b.InfectionChanceMultiplier);
        }
        Assert.Equal(golden.MedicalBooks["wilderness_survival_guide"], back.MedicalBooks["wilderness_survival_guide"]);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 health.json 经 FromJson == golden ──
    [Fact]
    public void On_disk_health_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "health.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new HealthConfig();
        var loaded = (HealthConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        BitEqual(GInfectionWorsenPerDay, loaded.InfectionWorsenPerDay);
        BitEqual(GCureProgressBaseRate, loaded.CureProgressBaseRate);
        Assert.Equal(GSurgeryBasePoints, loaded.SurgeryBasePoints);
        BitEqual(GRosehipTeaHealBonusPct, loaded.RosehipTeaHealBonusPct);
        BitEqual(0.75, loaded.SurgerySupplies["herbal_bandage"].InfectionChanceMultiplier);
        Assert.Equal(20, loaded.SurgerySupplies["herbal_bandage"].Points);
        Assert.Equal(3, loaded.MedicalBooks["wilderness_survival_guide"]);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Health 段 ──
    [Fact]
    public void Loader_reflection_discovers_health_section()
    {
        var cfg = GameConfigLoader.Parse(file => File.ReadAllText(Path.Combine(GameConfigFiles.LocateConfigDir(), file)));
        Assert.Equal(GSurgeryMinPoints, cfg.Health.SurgeryMinPoints);
        BitEqual(GInfectionBaseChanceLarge, cfg.Health.InfectionBaseChanceLarge);
    }

    // ── 缺键 fail-fast：目录数字访问器遇未登记键抛（不软回落 0）──
    [Fact]
    public void Missing_catalog_key_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => Section().MedicineFor("no_such_drug"));
        Assert.Throws<InvalidOperationException>(() => Section().SurgerySupplyFor("no_such_supply"));
        Assert.Throws<InvalidOperationException>(() => Section().MedicalBookPointsFor("no_such_book"));
    }

    // ── helpers ──
    private static void AssertMedicine(HealthConfig s, string key, double efficacy, double worsen)
    {
        MedicineNumbers m = s.Medicines[key];
        BitEqual(efficacy, m.Efficacy);
        BitEqual(worsen, m.WorsenMultiplier);
    }

    private static void AssertSupply(HealthConfig s, string key, int points, double infMult)
    {
        SurgerySupplyNumbers n = s.SurgerySupplies[key];
        Assert.Equal(points, n.Points);
        BitEqual(infMult, n.InfectionChanceMultiplier);
    }

    /// <summary>double 位级相等（比 == 更严）——证明「往返一位不差」。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
