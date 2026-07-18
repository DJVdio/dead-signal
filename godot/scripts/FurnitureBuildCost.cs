using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SalvageLogic.cs / CampStructure.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 营地家具（camp.json 的 props：工作台 / 柜子 / 衣柜 / 展示柜…）的「建造成本 + 建造工时」数据表。
// 这些家具此前和围栏一样，是建图时凭空立在那儿的——没有成本，也就无从谈返还。
// 本表先服务于**拆除**（用户拍板：家具可拆，回收 50%），也是未来建造系统的地基。

/// <summary>
/// 「家具键 → 建造成本 + 建造工时 + 简介」的家具目录。键 = <c>camp.json</c> 里 prop 的 <c>name</c>。
/// <para>
/// 🔴 <b>建造成本数量 + 工时已外置 <c>godot/data/config/furniture.json</c></b>（消费层配置范式，读
/// <see cref="FurnitureConfig"/> 段）——本表只保留<b>家具键 + 简介文案（authored）+ 成本注释</b>；数字真源在 json。
/// 简介 <see cref="Description"/> 是 authored 内容、已单独接线进 wiki「简介」列，故留在代码不外置。
/// </para>
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
    private sealed record FurnitureDef(string Description);

    // 建造成本/工时的真源在 furniture.json（FurnitureConfig 段）；本表只登记「家具键 → 简介」。
    // 成本注释保留为设计意图说明（「与配方 X 一致」的不变量由 CarpentryWorkTimeTests/TrapTests 等钉死）。
    private static readonly IReadOnlyDictionary<string, FurnitureDef> _all = new Dictionary<string, FurnitureDef>
    {
        // 工作台：一张厚重的作业台（**用户例子里那 16 木料**，wood 16 + nails 8，180 分，见 furniture.json）。
        // 拆了它 = 拆掉自己的生产线，但那是玩家的自由。
        ["工作台"] = new(
            "营地里所有\"造\"出来的东西，都始于这块厚板——包括你用来骗自己还能重建的那些。"),

        // 改装台（批次21·T7）：武器改造的唯一场所。成本与工时**与配方 mod_bench 保持一致**
        // （木 8 + 废金属 4 + 机械零件 2 + 钉 6，200 分），拆了按通用规则还一半。
        // 键 = camp.json prop 名 = WeaponModLogic.BenchFurnitureKey，三处必须同名（拆除按名归一）。
        ["改装台"] = new(
            "专门用来把武器改得更趁手。在这儿，\"改进\"和\"更致命\"是同一个词。"),

        // 烹饪台（批次21·T14）：做饭的唯一场所。成本与工时**与配方 cook_station 保持一致**
        // （石 8 + 木 6 + 废金属 3 + 钉 4，180 分），拆了按通用规则还一半（木料那份再分半走废木料）。
        // 键 = camp.json prop 名 = CookStation.PropName，三处必须同名（拆除按名归一）。
        ["烹饪台"] = new(
            "有了它才谈得上做饭。一顿热的，能让人暂时忘了热汤底下垫着的是什么日子。"),

        // 储物家具：柜子 / 衣柜 / 展示柜。板材 + 钉子，木工活。
        ["住宅-柜子"] = new(
            "一个放东西的地方。清点存货的时候，你数的其实是还能撑几天。"),
        ["住宅-衣柜"] = new(
            "挂衣服的地方。那些主人不会回来取的外套，如今谁冷谁穿。"),
        ["住宅-展示柜"] = new(
            "本来是用来炫耀的。柜子还在，值得炫耀的日子没了。"),

        // 床（批次21·impl-bedrest）：养病的物质基础。成本/工时与配方 bed 一致（木料 12 + 布 4 + 钉子 6，150 分）。
        // 开局那两张（camp.json 的 床#1/床#2）也吃这张表 ⇒ **它们同样可拆**——把床拆了当木料烧，是玩家的自由，
        // 代价是从此没人能睡床（拆床会把躺在上面的人赶下来改打地铺，见 BedRegistry.RemoveBed）。
        // 可重复摆放 ⇒ 实例名带流水号（"床#3"），本表按类型索引（见 TypeKeyOf）。
        // 拆除走通用规则（SalvageLogic 50% 向下取整 ⇒ 木料 6 + 布 2 + 钉子 3）。
        ["床"] = new(
            "睡个整觉，或者把断掉的骨头养上七天。末日里，能平躺下来就是一种特权——那七天，这张床只属于躺着的那个人。"),

        // 桌子（批次21·T25）：一件**纯家具**（目前无任何玩法作用，见 TableSpec 类注）。可摆、可跨越（−25% 移速）、可拆。
        // 成本/工时与配方 table 一致（木 8 + 钉 4，120 分）——两处分叉 = 拆出来的料对不上账（护栏见 CarpentryWorkTimeTests）。
        // 可重复摆放 ⇒ 实例名带流水号（"桌子#3"），本表按类型索引（见 TypeKeyOf）。
        // 拆除走通用规则（50% 向下取整 ⇒ 木料 2 + 废木料 2 + 钉子 2）。
        [TableSpec.FurnitureKey] = new(
            "就是一张桌子。不挡丧尸，不长粮食——可围着它坐下的那一刻，像是还有人在过日子。"),

        // 沙发（升级座位）：材料/工时与配方 sofa 一致；可拆后按通用 50% 返还。
        // 场上实例名带流水号（"沙发#1"），通过 TypeKeyOf 归一到本条。
        [SofaSpec.FurnitureKey] = new(
            "木椅的升级版。坐上去读书快一点，恢复也快一点——代价是它占地方，也吃掉更多布料。"),

        // 沙袋（玩家可自由建造摆放的半身掩体，见 SandbagSpec）：成本与工时**与配方 sandbag 保持一致**
        // （布 2 + 石料 4，30 分），拆了按通用规则还一半（布 1 + 石料 2，向下取整）。
        // 摆错了地方就拆走重摆——这正是"自由摆放"该配的退出机制。
        ["沙袋"] = new(
            "堆起来能挡半个身子。剩下那半个，看运气。"),

        // 陷阱（批次21·T26；玩家可自由建造摆放的圈套，见 TrapSpec）：成本与工时**与配方 snare_trap 保持一致**
        // （木料 2 + 铁丝 2 + 绳 1，40 分）——两处不一致就等于开了个"造一个拆一个"的材料永动机
        // （TrapTests.陷阱可拆_建造成本与配方一致 钉死这一点）。拆了按通用规则还一半（向下取整 ⇒ 木料 1 + 铁丝 1 + 绳 0）。
        // 可重复摆放 ⇒ 实例名带流水号（"陷阱#3"），本表按类型索引（见 TypeKeyOf）。
        ["陷阱"] = new(
            "踩上去就走不了了。它不认丧尸还是活人，只认那一脚。"),

        // [T75] 捕鸟陷阱（玩家可造/摆/拆，见 BirdTrapSpec）：成本与工时**与配方 bird_trap 一致**（木料 2 + 绳 2，40 分）
        // ——两处分叉 = "造一个拆一个"的材料永动机。拆了按通用规则还一半（向下取整 ⇒ 木料 1 + 绳 1）。
        // 可重复摆放 ⇒ 实例名带流水号（"捕鸟陷阱#3"），本表按类型索引。键 = BirdTrapSpec.FurnitureKey。
        [BirdTrapSpec.FurnitureKey] = new(
            "支起来，等一只倒霉的鸟。这年头肯为一只麻雀守一整天的人，是真饿了。"),

        // 菜园（[T72] 玩家可造/摆/拆的持久种植区，见 CropPlotSpec/CropPlotRuntime）：成本与工时**与配方 crop_plot 一致**
        // （木料 2，60 分）——两处分叉 = "造一个拆一个"的木料永动机。拆了按通用规则还一半（木料 1）。
        // ⚠️ 拆走菜园 = 未收的作物随它一起没了（消费层 CropPlotRuntime.ClearPlot 清计时器）。
        // 可重复摆放 ⇒ 实例名带流水号（"菜园#3"），本表按类型索引（见 TypeKeyOf）。键 = CropPlotSpec.FurnitureKey。
        [CropPlotSpec.FurnitureKey] = new(
            "翻两下土，种下去，然后学着相信明天。拆了它，没熟的那茬也跟着一起没。"),

        // [T67] 简易宰杀点（玩家可造/摆/拆的宰杀设施，见 ButcherStation）：成本与工时**与配方 butcher_point 一致**
        // （木料 1，30 分）——两处分叉 = "造一个拆一个"的木料永动机。拆了按通用规则还一半（木料 0，向下取整 ⇒ 拆了啥都不剩）。
        // 键 = camp.json 无预置、由玩家摆放落位的家具名 = ButcherStation.PointFurnitureKey。
        [ButcherStation.PointFurnitureKey] = new(
            "一块木头，够把猎物开膛。讲究不来——反正它也不介意了。"),

        // [T67] 宰杀台（简易宰杀点的升级档，见 ButcherStation）：成本与工时**与配方 butcher_table 一致**
        // （木料 3 + 钉子 4，60 分）——同上，两处不一致就开了永动机。拆了按通用规则还一半（木料 1 + 钉子 2，向下取整）。
        // 键 = ButcherStation.TableFurnitureKey。
        [ButcherStation.TableFurnitureKey] = new(
            "比那块木头体面些。同样的活，只是溅得少一点、快一点。"),

        // 收音机、废墟、尸体等**刻意不在表内**——它们不是造出来的，拆不动。
    };

    /// <summary>建造成本/工时的真源段（<c>furniture.json</c>）。</summary>
    private static FurnitureConfig Config => GameConfigCatalog.Section<FurnitureConfig>();

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

    /// <summary>某件家具的建造材料；不在目录里（拆不动）⇒ <c>null</c>。可传实例名（"沙袋#3"）。数值取 <c>furniture.json</c>。</summary>
    public static IReadOnlyDictionary<string, int>? Of(string furnitureKey)
        => furnitureKey is not null && _all.ContainsKey(TypeKeyOf(furnitureKey))
            ? Config.Get(TypeKeyOf(furnitureKey)).Cost : null;

    /// <summary>某件家具的建造工时（游戏分钟）；不在目录里 ⇒ <c>null</c>。可传实例名（"沙袋#3"）。数值取 <c>furniture.json</c>。</summary>
    public static int? BuildMinutes(string furnitureKey)
        => furnitureKey is not null && _all.ContainsKey(TypeKeyOf(furnitureKey))
            ? Config.Get(TypeKeyOf(furnitureKey)).BuildMinutes : null;

    /// <summary>某件家具面向玩家的简介文案（authored）；不在目录里 ⇒ <c>null</c>。可传实例名（"沙袋#3"）。</summary>
    public static string? Description(string furnitureKey)
        => furnitureKey is not null && _all.TryGetValue(TypeKeyOf(furnitureKey), out FurnitureDef? def) ? def.Description : null;

    /// <summary>全部可拆家具的键（供 UI / 测试遍历）。</summary>
    public static IEnumerable<string> All => _all.Keys;
}
