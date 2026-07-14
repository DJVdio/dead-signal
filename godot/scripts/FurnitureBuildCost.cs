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

        // 改装台（批次21·T7）：武器改造的唯一场所。成本与工时**与配方 mod_bench 保持一致**
        // （木 8 + 废金属 4 + 机械零件 2 + 钉 6，200 分），拆了按通用规则还一半。
        // 键 = camp.json prop 名 = WeaponModLogic.BenchFurnitureKey，三处必须同名（拆除按名归一）。
        ["改装台"] = new(Cost(("wood", 8), ("scrap_metal", 4), ("components", 2), ("nails", 6)), 200),

        // 烹饪台（批次21·T14）：做饭的唯一场所。成本与工时**与配方 cook_station 保持一致**
        // （石 8 + 木 6 + 废金属 3 + 钉 4，180 分），拆了按通用规则还一半（木料那份再分半走废木料）。
        // 键 = camp.json prop 名 = CookStation.PropName，三处必须同名（拆除按名归一）。
        ["烹饪台"] = new(Cost(("stone", 8), ("wood", 6), ("scrap_metal", 3), ("nails", 4)), 180),

        // 储物家具：柜子 / 衣柜 / 展示柜。板材 + 钉子，木工活。
        ["住宅-柜子"] = new(Cost(("wood", 10), ("nails", 6)), 120),
        ["住宅-衣柜"] = new(Cost(("wood", 12), ("nails", 6)), 140),
        ["住宅-展示柜"] = new(Cost(("wood", 8), ("nails", 4)), 100),

        // 床（批次21·impl-bedrest）：养病的物质基础。成本/工时与配方 bed 一致（木料 12 + 布 4 + 钉子 6，150 分）。
        // 开局那两张（camp.json 的 床#1/床#2）也吃这张表 ⇒ **它们同样可拆**——把床拆了当木料烧，是玩家的自由，
        // 代价是从此没人能睡床（拆床会把躺在上面的人赶下来改打地铺，见 BedRegistry.RemoveBed）。
        // 可重复摆放 ⇒ 实例名带流水号（"床#3"），本表按类型索引（见 TypeKeyOf）。
        // 拆除走通用规则（SalvageLogic 50% 向下取整 ⇒ 木料 6 + 布 2 + 钉子 3）。
        ["床"] = new(Cost(("wood", 12), ("cloth", 4), ("nails", 6)), 150),

        // 桌子（批次21·T25）：一件**纯家具**（目前无任何玩法作用，见 TableSpec 类注）。可摆、可跨越（−25% 移速）、可拆。
        // 成本/工时与配方 table 一致（木 8 + 钉 4，120 分）——两处分叉 = 拆出来的料对不上账（护栏见 CarpentryWorkTimeTests）。
        // 可重复摆放 ⇒ 实例名带流水号（"桌子#3"），本表按类型索引（见 TypeKeyOf）。
        // 拆除走通用规则（50% 向下取整 ⇒ 木料 2 + 废木料 2 + 钉子 2）。
        [TableSpec.FurnitureKey] = new(Cost(("wood", 8), ("nails", 4)), 120),

        // 沙袋（玩家可自由建造摆放的半身掩体，见 SandbagSpec）：成本与工时**与配方 sandbag 保持一致**
        // （布 2 + 石料 4，30 分），拆了按通用规则还一半（布 1 + 石料 2，向下取整）。
        // 摆错了地方就拆走重摆——这正是"自由摆放"该配的退出机制。
        ["沙袋"] = new(Cost(("cloth", 2), ("stone", 4)), 30),

        // 陷阱（批次21·T26；玩家可自由建造摆放的圈套，见 TrapSpec）：成本与工时**与配方 snare_trap 保持一致**
        // （木料 2 + 铁丝 2 + 绳 1，40 分）——两处不一致就等于开了个"造一个拆一个"的材料永动机
        // （TrapTests.陷阱可拆_建造成本与配方一致 钉死这一点）。拆了按通用规则还一半（向下取整 ⇒ 木料 1 + 铁丝 1 + 绳 0）。
        // 可重复摆放 ⇒ 实例名带流水号（"陷阱#3"），本表按类型索引（见 TypeKeyOf）。
        ["陷阱"] = new(Cost(("wood", 2), ("wire", 2), ("rope", 1)), 40),

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
