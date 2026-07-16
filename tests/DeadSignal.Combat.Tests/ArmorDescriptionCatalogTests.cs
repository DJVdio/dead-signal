using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 防腐护栏：<see cref="ArmorTable.DescriptionOf"/> 必须能取到 <b>config 里每一件护甲</b>的风味文案。
/// <para>
/// 起因（消费层静默失效）：旧实现走手维护的 <c>_flavorByName</c> 字典，只列了一部分护甲，
/// 每加一件新护甲若忘了补字典 ⇒ 库存 UI 描述空白（尽管 armor.json 里明明有 Description）。
/// 根治后 <c>DescriptionOf</c> 直接从 <see cref="ArmorConfig"/> 按名取 Description，config 是唯一权威源。
/// </para>
/// <para>
/// 本测试<b>遍历 catalog、不硬编码名单</b> —— 这正是防"再漏一件"的护栏：
/// 任何进 armor.json 的护甲都自动被覆盖，漏描述立刻红。
/// </para>
/// </summary>
public class ArmorDescriptionCatalogTests
{
    [Fact]
    public void DescriptionOf_NonEmpty_ForEveryArmorInCatalog()
    {
        var byId = CombatCatalog.Section<ArmorConfig>().ById;
        Assert.NotEmpty(byId);

        var missing = byId.Values
            .Where(layer => string.IsNullOrWhiteSpace(ArmorTable.DescriptionOf(layer.Name)))
            .Select(layer => layer.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"以下护甲 DescriptionOf 返回空串（消费层库存描述会空白）：{string.Join("、", missing)}");
    }
}
