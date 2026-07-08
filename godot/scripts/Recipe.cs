using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Workbench.cs / Materials.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 配方数据模型：配方 = 材料成本 + 需要工具槽 + 制作者解锁条件（读完的书）→ 产物。
// 通用技能系统已删——能力改由"每角色 authored 专属效果"与"读过的书"承载，配方门槛只看 工具/书/材料。
// 本文件只放**数据**（RecipeData 值对象 + RecipeBook 草稿表）；能不能做（CanCraft）与产出结算（Resolve）在 CraftingLogic。
// 材料/产物一律用**字符串 RefKey**（对齐 Materials 目录 / Item RefKey），不硬依赖枚举，解耦并发。

/// <summary>配方类别（供 UI 分组；与解锁工具大致对应，但工具门槛以 <see cref="RecipeData.RequiredTools"/> 为准）。</summary>
public enum RecipeCategory
{
    /// <summary>木工（锯片类，如椅子）。</summary>
    Woodwork,

    /// <summary>精工/弓弩（卡尺类，如自制弓）。</summary>
    Precision,

    /// <summary>化学（烧杯类，如火药、鞣制药水）。</summary>
    Chemistry,

    /// <summary>缝纫/纺织（如粗布背心）。</summary>
    Tailoring,

    /// <summary>杂项/工具（如骨刀）。</summary>
    Misc,
}

/// <summary>
/// 一张配方的不可变定义。产物由 <see cref="OutputKey"/>（Item RefKey）+ <see cref="OutputQuantity"/> 描述；
/// 解锁条件按**制作者**判定：需读完 <see cref="RequiredBookIds"/> 的全部书；
/// 且工作台已装 <see cref="RequiredTools"/> 的全部工具槽；且库存够付 <see cref="MaterialCosts"/>。
/// 通用技能门槛已删——配方只看 工具/书/材料 三类门槛。
/// </summary>
public sealed record RecipeData(
    string Id,
    string DisplayName,
    RecipeCategory Category,
    string OutputKey,
    int OutputQuantity,
    IReadOnlyDictionary<string, int> MaterialCosts,
    IReadOnlySet<ToolSlot> RequiredTools,
    IReadOnlyList<string> RequiredBookIds);

/// <summary>
/// 内置配方表（**拟定草稿 draft**，工具/条件/材料数值用户后续调）。覆盖用户给的 6 个例子：
/// 骨刀 / 粗布背心 / 椅子 / 火药 / 鞣制药水 / 自制弓，各按拍板的"defining 条件"填工具槽 + 解锁 + 拟定材料。
/// 材料 RefKey 对齐 <see cref="Materials"/> 目录；产物 RefKey 为拟定名（gunpowder / tanning_solution 与材料同名=化学产出）。
/// </summary>
public static class RecipeBook
{
    /// <summary>《野外生存指南》书 id（对齐 <see cref="BookLibrary.WildernessSurvivalGuide"/>）。骨刀解锁读它。</summary>
    public const string WildernessSurvivalGuideBookId = "wilderness_survival_guide";

    /// <summary>《裁缝手记》纺织书 id（对齐 <see cref="BookLibrary.TailorsNotes"/>）。粗布背心解锁读它。</summary>
    public const string TailorsNotesBookId = "tailors_notes";

    /// <summary>《土法化学笔记》化学书 id（对齐 <see cref="BookLibrary.FolkChemistryNotes"/>）。火药 / 鞣制药水解锁读它。</summary>
    public const string FolkChemistryNotesBookId = "folk_chemistry_notes";

    private static IReadOnlySet<ToolSlot> Tools(params ToolSlot[] slots) => new HashSet<ToolSlot>(slots);
    private static IReadOnlyDictionary<string, int> Cost(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (key, qty) in items)
        {
            d[key] = qty;
        }
        return d;
    }
    private static readonly IReadOnlyList<string> NoBooks = new List<string>();
    private static IReadOnlyList<string> Books(params string[] ids) => new List<string>(ids);

    // draft：以下配方的工具/条件/材料/数量/经验均为占位草稿，最终由用户调（对标 6 个例子 + 生存造物常识）。
    private static readonly IReadOnlyList<RecipeData> _all = new[]
    {
        // 骨刀：读完《野外生存指南》解锁；削骨即成，无需工具台工具。
        new RecipeData(
            Id: "bone_knife",
            DisplayName: "骨刀",
            Category: RecipeCategory.Misc,
            OutputKey: "bone_knife",
            OutputQuantity: 1,
            MaterialCosts: Cost(("bone", 2), ("scrap_cloth", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(WildernessSurvivalGuideBookId)),

        // 粗布背心：读过《裁缝手记》解锁；缝制布甲。
        new RecipeData(
            Id: "cloth_vest",
            DisplayName: "粗布背心",
            Category: RecipeCategory.Tailoring,
            OutputKey: "cloth_vest",
            OutputQuantity: 1,
            MaterialCosts: Cost(("cloth", 2), ("scrap_cloth", 2)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(TailorsNotesBookId)),

        // 椅子：锯片类木工。
        new RecipeData(
            Id: "chair",
            DisplayName: "木椅",
            Category: RecipeCategory.Woodwork,
            OutputKey: "chair",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 4), ("nails", 2)),
            RequiredTools: Tools(ToolSlot.SawBlade),
            RequiredBookIds: NoBooks),

        // 火药：烧杯类化学 + 读过《土法化学笔记》解锁。
        new RecipeData(
            Id: "gunpowder",
            DisplayName: "火药",
            Category: RecipeCategory.Chemistry,
            OutputKey: "gunpowder",
            OutputQuantity: 2,
            MaterialCosts: Cost(("stone", 1), ("fuel", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId)),

        // 鞣制药水：烧杯类化学 + 读过《土法化学笔记》解锁。
        new RecipeData(
            Id: "tanning_solution",
            DisplayName: "鞣制药水",
            Category: RecipeCategory.Chemistry,
            OutputKey: "tanning_solution",
            OutputQuantity: 2,
            MaterialCosts: Cost(("fuel", 1), ("stone", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId)),

        // 自制弓：卡尺类精工。
        new RecipeData(
            Id: "handmade_bow",
            DisplayName: "自制弓",
            Category: RecipeCategory.Precision,
            OutputKey: "handmade_bow",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 2), ("rope", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: NoBooks),
    };

    private static readonly IReadOnlyDictionary<string, RecipeData> _byId = ToMap(_all);
    private static IReadOnlyDictionary<string, RecipeData> ToMap(IReadOnlyList<RecipeData> list)
    {
        var d = new Dictionary<string, RecipeData>();
        foreach (var r in list)
        {
            d[r.Id] = r;
        }
        return d;
    }

    /// <summary>全部内置配方（按声明顺序）。</summary>
    public static IReadOnlyList<RecipeData> All => _all;

    /// <summary>按配方 id 查一张配方；查不到返回 <c>null</c>。</summary>
    public static RecipeData? Find(string id)
        => id != null && _byId.TryGetValue(id, out RecipeData? r) ? r : null;
}
