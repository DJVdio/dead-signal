using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SalvageLogic.cs / CampStructure.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 营地家具（camp.json 的 props：工作台 / 柜子 / 衣柜 / 展示柜…）的「建造成本 + 建造工时」数据表。
// 这些家具此前和围栏一样，是建图时凭空立在那儿的——没有成本，也就无从谈返还。
// 本表先服务于**拆除**（用户拍板：家具可拆，回收 50%），也是未来建造系统的地基。

/// <summary>
/// 「家具键 → 建造成本 + 建造工时」纯数据表（数值拟定待调）。键 = <c>camp.json</c> 里 prop 的 <c>name</c>。
/// <para>
/// <b>不在本表里的东西拆不动</b>：收音机 / 废墟 / 祖母的尸体这类不是"造出来"的东西——没有建造成本可依，
/// 自然也没有返还（同"没有配方的物品拆不了"，见 <see cref="SalvageLogic.RecipeFor"/>）。
/// </para>
/// <para>
/// <b>工作台 = 木料 16</b>：这正是用户举的那个例子（「建造需要 16 木材，拆除会获得 4 木材和 4 废木料」）——
/// 拆它走 <see cref="SalvageLogic.YieldOfFurniture"/>，正好掉 4 木料 + 4 废木料（+ 钉子的一半）。
/// </para>
/// 返还与工时见 <see cref="SalvageLogic"/>；材料键对齐 <see cref="Materials"/> 目录。
/// </summary>
public static class FurnitureBuildCost
{
    private static IReadOnlyDictionary<string, int> Cost(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (key, qty) in items)
        {
            d[key] = qty;
        }
        return d;
    }

    private sealed record FurnitureDef(IReadOnlyDictionary<string, int> Cost, int BuildMinutes);

    // draft：成本/工时皆占位草稿，用户后续调。
    private static readonly IReadOnlyDictionary<string, FurnitureDef> _all = new Dictionary<string, FurnitureDef>
    {
        // 工作台：一张厚重的作业台（**用户例子里那 16 木料**）。拆了它 = 拆掉自己的生产线，但那是玩家的自由。
        ["工作台"] = new(Cost(("wood", 16), ("nails", 8)), 180),

        // 储物家具：柜子 / 衣柜 / 展示柜。板材 + 钉子，木工活。
        ["住宅-柜子"] = new(Cost(("wood", 10), ("nails", 6)), 120),
        ["住宅-衣柜"] = new(Cost(("wood", 12), ("nails", 6)), 140),
        ["住宅-展示柜"] = new(Cost(("wood", 8), ("nails", 4)), 100),

        // 沙袋（玩家可自由建造摆放的半身掩体，见 SandbagSpec）：成本与工时**与配方 sandbag 保持一致**
        // （布 2 + 石料 4，30 分），拆了按通用规则还一半（布 1 + 石料 2，向下取整）。
        // 摆错了地方就拆走重摆——这正是"自由摆放"该配的退出机制。
        ["沙袋"] = new(Cost(("cloth", 2), ("stone", 4)), 30),

        // 收音机、废墟、尸体等**刻意不在表内**——它们不是造出来的，拆不动。
    };

    /// <summary>
    /// 实例名 → 类型名：<b>可重复摆放的家具</b>（沙袋）在场上的名字带流水号（"沙袋#3"），
    /// 而本表按类型索引。截掉 '#' 及其后缀即可。
    /// 场景预置的独一份家具（工作台/柜子…）名字里没有 '#'，原样返回 ⇒ <b>零回归</b>。
    /// </summary>
    private static string TypeKeyOf(string key)
    {
        int hash = key.IndexOf('#');
        return hash >= 0 ? key[..hash] : key;
    }

    /// <summary>某件家具的建造材料；不在目录里（拆不动）⇒ <c>null</c>。可传实例名（"沙袋#3"）。</summary>
    public static IReadOnlyDictionary<string, int>? Of(string furnitureKey)
        => furnitureKey is not null && _all.TryGetValue(TypeKeyOf(furnitureKey), out FurnitureDef? def) ? def.Cost : null;

    /// <summary>某件家具的建造工时（游戏分钟）；不在目录里 ⇒ <c>null</c>。可传实例名（"沙袋#3"）。</summary>
    public static int? BuildMinutes(string furnitureKey)
        => furnitureKey is not null && _all.TryGetValue(TypeKeyOf(furnitureKey), out FurnitureDef? def) ? def.BuildMinutes : null;

    /// <summary>全部可拆家具的键（供 UI / 测试遍历）。</summary>
    public static IEnumerable<string> All => _all.Keys;
}
