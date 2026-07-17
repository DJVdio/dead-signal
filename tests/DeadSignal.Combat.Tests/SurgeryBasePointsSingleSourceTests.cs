using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【常人手术基础点数·单一事实源焊死护栏】
/// <para>
/// 「常人（非南丁格尔）手术基础点数」历史上有两个落点：
/// <list type="bullet">
///   <item><b>真源</b>：<c>health.json → SurgeryBasePoints</c>（手术/医疗域·更自然的家），
///         由 <see cref="HealthConditionSet.SurgeryBasePoints"/> 读取；徒手无床时的手术底池。</item>
///   <item><b>镜像</b>：<c>perks.json → NightingaleDefaultSurgeryBasePoints</c>，
///         由 <see cref="NightingalePerk.DefaultSurgeryBasePoints"/> 读取（供 per-surgeon 结算区分
///         南丁格尔 30 / 常人 X）。SurvivorPerks.cs 注释已注明其"原 HealthConditionSet.SurgeryBasePoints"。</item>
/// </list>
/// 两处语义完全同一（都=常人手术基础点数），当前都是 15。本护栏钉死
/// <b>「两处必须始终相等」</b>——将来有人改一份忘另一份会立刻变红，防止双份漂移。
/// 参照本仓 ItemDef「护甲重量投影自引擎 ArmorTable.Weight、登记焊死反射护栏防漏登记」的思路。
/// </para>
/// <para>⚠️ 零行为变化护栏：两处值不变（15），本文件不改任何结算路径，只断言两份数据源一致。</para>
/// </summary>
public sealed class SurgeryBasePointsSingleSourceTests
{
    /// <summary>
    /// 焊死：常人手术基础点数的两个落点（health.json 真源 / perks.json 镜像）必须相等。
    /// 改一处忘另一处 ⇒ 本断言变红。
    /// </summary>
    [Fact]
    public void Default_surgery_base_points_stay_single_sourced()
    {
        Assert.Equal(
            HealthConditionSet.SurgeryBasePoints,        // 真源：health.json SurgeryBasePoints
            NightingalePerk.DefaultSurgeryBasePoints);   // 镜像：perks.json NightingaleDefaultSurgeryBasePoints
    }
}
