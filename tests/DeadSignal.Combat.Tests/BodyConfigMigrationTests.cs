using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【数值外置 · body 范式的零漂移 A/B 焊死】config-body 单。
/// <para>
/// 身体部位的<b>可调数字</b>（体积权重 + 最大 HP）与残疾惩罚系数（单肢 −50% / 每指 −7% / 每趾 −2%）
/// 已从 <see cref="HumanBody"/>/<see cref="Body"/> 的 C# 常量搬到 <c>godot/data/config/body.json</c>；
/// <b>结构</b>（部位名 const、Region/MacroRegion/Category 分类、父子拓扑）仍在代码里、不外置。
/// 本文件钉死「搬家没搬错一个数、也没动一处结构」：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 Bootstrapper 让 catalog 懒加载成功，<see cref="HumanBody.Parts"/> 能取到数值。</item>
///   <item><b>字面值锚定（A/B）</b>：抽样部位 × 每类数字（整/小数/边界）逐条 == 迁移前原始常量，double 位级相等。</item>
///   <item><b>惩罚行为锚定</b>：切一根手指 → 操作惩罚 = 0.07（证明 FingerPenalty 真从 json 读到）。</item>
///   <item><b>往返保真</b>：整段 body.json 序列化→反序列化，逐字段位级相等（值无关，永久护栏）。</item>
///   <item><b>结构不变</b>：39 部位齐全、父子拓扑/分类逐条锚定（外置数值不得动一处结构）。</item>
/// </list>
/// <para>
/// 🔴 更强的零漂移证明在 test 之外：整表蒙特卡洛 Sim 输出迁移前后 MD5 完全一致
/// （<c>44c28a8efe62f118c2322ad2de38f432</c>，见 config-body journal）——部位 HP/命中权重/切除都进 Sim 结算路径。
/// </para>
/// </summary>
public sealed class BodyConfigMigrationTests
{
    // ── 接线 + 完整性 ──────────────────────────────────────────────────────────
    [Fact]
    public void Catalog_is_wired_and_parts_load()
    {
        var parts = HumanBody.Parts();   // 首次访问触发懒加载 + 读 body.json
        Assert.True(CombatCatalog.IsInitialized, "取部位后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(39, parts.Count);
        Assert.Equal("body.json", CombatCatalog.Section<BodyConfig>().FileName);
    }

    [Fact]
    public void Missing_part_fails_fast()
    {
        Assert.Throws<KeyNotFoundException>(() => CombatCatalog.Section<BodyConfig>().Part("没有这个部位"));
    }

    // ── 字面值锚定（A/B）：抽样部位 × 每类数字 == 迁移前原始常量 ──────────────────────
    [Fact]
    public void Sampled_part_stats_match_original_literals()
    {
        var by = HumanBody.Parts().ToDictionary(p => p.Name);

        // 整数 HP / 权重
        Assert.Equal(20, by[HumanBody.Chest].VolumeWeight);
        Assert.Equal(20, by[HumanBody.Chest].MaxHp);
        Assert.Equal(16, by[HumanBody.Abdomen].MaxHp);
        Assert.Equal(6, by[HumanBody.Head].VolumeWeight);
        Assert.Equal(16, by[HumanBody.Head].MaxHp);
        Assert.Equal(21, by[HumanBody.LeftArm].MaxHp);
        Assert.Equal(11, by[HumanBody.LeftCalf].MaxHp);   // 小腿 11>匕首上限 10（细分防秒切）

        // 小数权重：double 位级一位不差
        ExactlyEqual(0.4, by[HumanBody.LeftEye].VolumeWeight);
        Assert.Equal(6, by[HumanBody.LeftEye].MaxHp);
        ExactlyEqual(1.5, by[HumanBody.Chin].VolumeWeight);
        ExactlyEqual(0.35, by[HumanBody.LeftThumb].VolumeWeight);
        ExactlyEqual(0.3, by[HumanBody.LeftIndex].VolumeWeight);
        ExactlyEqual(0.25, by[HumanBody.LeftPinky].VolumeWeight);
        ExactlyEqual(0.2, by[HumanBody.LeftToe2].VolumeWeight);
        ExactlyEqual(0.15, by[HumanBody.LeftToe5].VolumeWeight);
        Assert.Equal(10, by[HumanBody.LeftToe5].MaxHp);
    }

    [Fact]
    public void Disability_penalties_match_original_literals()
    {
        var d = CombatCatalog.Section<BodyConfig>().Disability;
        ExactlyEqual(0.5, d.SingleLimbPenalty);
        ExactlyEqual(0.07, d.FingerPenalty);
        ExactlyEqual(0.02, d.ToePenalty);
    }

    // ── 惩罚行为锚定：证明 json 的 0.07/0.02 真被 RecalculatePenalties 读到 ──────────────
    [Fact]
    public void One_finger_severed_gives_seven_percent_operation_penalty()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftIndex);      // 切左手食指（一指）
        body.RecalculatePenalties();
        ExactlyEqual(0.07, body.DisabilityModifiers.OperationPenalty); // 1 指 × FingerPenalty
    }

    [Fact]
    public void One_toe_severed_gives_two_percent_mobility_penalty()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftToe2);       // 切左脚二趾（一趾）
        body.RecalculatePenalties();
        ExactlyEqual(0.02, body.DisabilityModifiers.MobilityPenalty);  // 1 趾 × ToePenalty
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）──────────────────────────────
    [Fact]
    public void Body_config_survives_json_round_trip_bit_for_bit()
    {
        var cfg = CombatCatalog.Section<BodyConfig>();
        string json = JsonSerializer.Serialize(cfg, CombatConfigLoader.Options);
        var back = JsonSerializer.Deserialize<BodyConfig>(json, CombatConfigLoader.Options)!;

        Assert.Equal(cfg.Parts.Count, back.Parts.Count);
        foreach (var (name, st) in cfg.Parts)
        {
            var b = back.Part(name);
            Assert.True(BitConverter.DoubleToInt64Bits(st.VolumeWeight) == BitConverter.DoubleToInt64Bits(b.VolumeWeight),
                $"{name}.VolumeWeight double 漂移：{st.VolumeWeight} vs {b.VolumeWeight}");
            Assert.True(BitConverter.DoubleToInt64Bits(st.MaxHp) == BitConverter.DoubleToInt64Bits(b.MaxHp),
                $"{name}.MaxHp double 漂移：{st.MaxHp} vs {b.MaxHp}");
        }
        ExactlyEqual(cfg.Disability.SingleLimbPenalty, back.Disability.SingleLimbPenalty);
        ExactlyEqual(cfg.Disability.FingerPenalty, back.Disability.FingerPenalty);
        ExactlyEqual(cfg.Disability.ToePenalty, back.Disability.ToePenalty);
    }

    // ── 结构不变：外置数值不得动一处结构（部位名/分类/父子拓扑） ──────────────────────
    [Fact]
    public void Topology_and_classification_unchanged()
    {
        var by = HumanBody.Parts().ToDictionary(p => p.Name);

        // 树根与主干拓扑
        Assert.Null(by[HumanBody.Chest].Parent);                         // 胸=树根
        Assert.Equal(HumanBody.Chest, by[HumanBody.Abdomen].Parent);     // 腹挂胸
        Assert.Equal(HumanBody.Chest, by[HumanBody.Head].Parent);        // 头挂胸
        Assert.Equal(HumanBody.Abdomen, by[HumanBody.LeftLeg].Parent);   // 腿挂腹
        Assert.Equal(HumanBody.LeftLeg, by[HumanBody.LeftCalf].Parent);  // 小腿挂大腿
        Assert.Equal(HumanBody.LeftCalf, by[HumanBody.LeftFoot].Parent); // 脚挂小腿
        Assert.Equal(HumanBody.LeftHand, by[HumanBody.LeftThumb].Parent);// 指挂手
        Assert.Equal(HumanBody.LeftFoot, by[HumanBody.LeftToe5].Parent); // 趾挂脚

        // 分类（切除后果由此归类）
        Assert.Equal(BodyPartCategory.Vital, by[HumanBody.Chest].Category);
        Assert.Equal(BodyPartCategory.Vital, by[HumanBody.Head].Category);
        Assert.Equal(BodyPartCategory.Eye, by[HumanBody.LeftEye].Category);
        Assert.Equal(BodyPartCategory.Minor, by[HumanBody.Nose].Category);
        Assert.Equal(BodyPartCategory.Limb, by[HumanBody.LeftThumb].Category);

        // Region / MacroRegion（命中判定两级）
        Assert.Equal(BodyRegion.Finger, by[HumanBody.LeftThumb].Region);
        Assert.Equal(BodyMacroRegion.Hand, by[HumanBody.LeftThumb].MacroRegion);
        Assert.Equal(BodyRegion.Toe, by[HumanBody.LeftToe5].Region);
        Assert.Equal(BodyMacroRegion.Foot, by[HumanBody.LeftToe5].MacroRegion);

        // 子树展开（护甲覆盖连带）仍完整：左手 = 左手 + 5 指
        Assert.Equal(6, HumanBody.SubtreeNames(HumanBody.LeftHand).Count);
    }

    /// <summary>double 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void ExactlyEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
