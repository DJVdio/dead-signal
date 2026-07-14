using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 CraftingLogic.cs / SalvageLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 武器改装的**门槛判定与成本结算**（纯函数）。刻意与 CraftingLogic 同形：
//   CanApply —— 逐条核对 设施 / 材料 / 合成合法性，输出"能不能改 + 缺什么（逐条原因）"，供 UI 灰显。
//   Resolve  —— 能改时给出"要扣的材料 delta"，由调用方去 InventoryStore 实扣（本块不碰库存）。
// 改装**不点击即得**：走既有 CraftingJob 工时制（同拆解——拆解也是复用它的，见 SalvageLogic.JobIdFor）。
//
// 【门槛口径（本 agent 拟定，数值待调）】改装 = **改装台** + **材料** + **工时**。三条，没有第四条：
//   · 不设**书**门槛：项目通例是"不为一个新系统凭空造一本新书"（会带出新的书籍投放/掉落缺口，
//     见 Recipe.cs 里"不必新造一本枪匠书"的同款论证）。改枪的门槛已经由"先得造出改装台"承担了。
//   · 不设**工具槽**门槛：工具槽（卡尺/锯片/烧杯）是**工作台**的机制；改装台是另一座设施，
//     它自己就是那道门槛，再叠一层工具槽只是重复收费。

/// <summary>某次改装未满足门槛的原因类别。</summary>
public enum WeaponModBlockReason
{
    /// <summary>营地里还没有改装台（改装只能在改装台上做）。</summary>
    NoModBench,

    /// <summary>库存材料不足。</summary>
    InsufficientMaterial,

    /// <summary>这组改装本身非法（大类不符 / 同部位冲突 / 近战型态撞车）。</summary>
    InvalidCombination,

    /// <summary>没选任何改装。</summary>
    NothingSelected,
}

/// <summary>一条未满足门槛的明细（原因 + 人读说明 + 相关键）。</summary>
public readonly record struct WeaponModBlock(WeaponModBlockReason Reason, string Detail, string Key);

/// <summary>一次 <see cref="WeaponModLogic.CanApply"/> 的结果：能否改装 + 全部未满足门槛（供 UI 灰显/提示）。</summary>
public sealed record WeaponModAvailability(bool CanApply, IReadOnlyList<WeaponModBlock> Blocks);

/// <summary>武器改装的门槛判定与成本结算（纯函数，无状态、无副作用）。</summary>
public static class WeaponModLogic
{
    /// <summary>改装台的家具键（= camp.json prop 名 / <see cref="FurnitureBuildCost"/> 键 / 场上容器名）。</summary>
    public const string BenchFurnitureKey = "改装台";

    /// <summary>改装台的配方 id（在**工作台**上造，见 <see cref="RecipeBook"/>）。</summary>
    public const string BenchRecipeId = "mod_bench";

    /// <summary>
    /// 改装台的配方产物键。⚠️ 它**不会变成一件库存物品**——改装台是**固定位置**的实心工作案，
    /// 完工即立在车间里（见 <c>CampMain.CompleteModBenchBuild</c>），不进背包、也没有「摆放」按钮。
    /// </summary>
    public const string BenchItemKey = "mod_bench";

    /// <summary>改装台占地（像素）。比工作台（120×74）略小一圈。</summary>
    public const float BenchWidth = 104f;
    public const float BenchHeight = 68f;

    // ───────────────────────── 改装台的固定位置（用户拍板）─────────────────────────
    //
    // 【用户口径】"改装台、烹饪台**不允许跨越**，但是他们是营地内**固定位置**。改装台放在**车间**。"
    // 又：camp.json 里本来**没有车间**（只有 住宅 / 仓库 / **空牛棚**）⇒ 用户选定：**空牛棚改造成车间**。
    //
    // ⇒ 改装台**不是玩家摆的**：造好即自动落在车间（空牛棚）里的这个锚点。玩家没有"放置"这个动作，
    //   自然也没有"放置时不许贴围栏"这回事（那条规则是给**可摆放**家具的，见 PlacementRules）。
    //
    // 【为什么固定位置反而更要校验】它**实心、挖导航洞、不可跨越**，而玩家**摆不了也挪不动**——
    // 锚点若压进防线禁建带，就是一条玩家永远无法纠正的死路（守卫走不到墙根、砌墙的人站不进施工位）。
    // 故锚点仍按 PlacementRules 的口径自检（见 BenchSpec + impl-modbench 的 FixedFacilityAnchorTests）。
    //
    // 【本锚点的实测余量】空牛棚 [1480,980,420,320] 内，离棚墙 左168/上46/右148/下206 px；
    // 与「牛棚-草垛A」留 22px 间隙；**距最近围栏/大门 326px**（禁建带才 64px）⇒ 远在安全区。

    /// <summary>改装台固定锚点的左上角（cartesian 世界坐标）。在**空牛棚（车间）**内。</summary>
    public const float BenchAnchorX = 1648f;
    public const float BenchAnchorY = 1026f;

    /// <summary>
    /// 改装台的规格：**实心家具**（挖导航洞、真挡路、不可跨越）。
    /// <para>
    /// 玩家摆不了它（固定锚点），但这份规格仍有用：**设计期自检**拿它去过 <see cref="PlacementRules"/>，
    /// 断言固定锚点没有侵入防线禁建带。<c>AllowedAgainstDefenses</c> 保持缺省 <c>false</c>（**别填 true**）——
    /// 那是沙袋才有的豁免（沙袋恒不挡路）；改装台真挡路，不该豁免。
    /// </para>
    /// </summary>
    public static PlaceableSpec BenchSpec => new(BenchFurnitureKey, BenchWidth, BenchHeight, IsSolid: true);

    /// <summary>改装任务的 jobId 前缀（复用 <see cref="CraftingJob"/> 承载工时；同拆解的 "salvage:"）。</summary>
    public const string JobIdPrefix = "weaponmod:";

    private const char BaseModSeparator = '|';
    private const char ModSeparator = ',';

    /// <summary>合成一个改装任务的 jobId："weaponmod:步枪|刺刀型,加长枪管"。武器名/改装名皆中文，不含分隔符。</summary>
    public static string JobIdFor(string baseWeaponKey, IReadOnlyList<string> modNames)
        => JobIdPrefix + baseWeaponKey + BaseModSeparator + string.Join(ModSeparator, modNames ?? Array.Empty<string>());

    /// <summary>解一个改装任务的 jobId 回（基础武器名, 改装名列表）；不是改装任务 ⇒ null。</summary>
    public static (string BaseWeaponKey, IReadOnlyList<string> ModNames)? TargetOf(string? jobId)
    {
        if (jobId is null || !jobId.StartsWith(JobIdPrefix, StringComparison.Ordinal)) return null;

        string body = jobId[JobIdPrefix.Length..];
        int sep = body.IndexOf(BaseModSeparator);
        if (sep < 0) return null;

        string baseKey = body[..sep];
        string modsPart = body[(sep + 1)..];
        IReadOnlyList<string> mods = modsPart.Length == 0
            ? Array.Empty<string>()
            : modsPart.Split(ModSeparator).ToList();

        return baseKey.Length == 0 ? null : (baseKey, mods);
    }

    /// <summary>这组改装的材料总成本（同材料跨改装累加）。</summary>
    public static IReadOnlyDictionary<string, int> TotalCost(IReadOnlyList<WeaponMod> mods)
    {
        var total = new Dictionary<string, int>();
        foreach (WeaponMod mod in mods ?? Array.Empty<WeaponMod>())
        {
            foreach (KeyValuePair<string, int> c in mod.MaterialCosts)
            {
                total[c.Key] = total.GetValueOrDefault(c.Key) + c.Value;
            }
        }
        return total;
    }

    /// <summary>这组改装的总工时（游戏分钟，累加）。</summary>
    public static int TotalWorkMinutes(IReadOnlyList<WeaponMod> mods)
        => (mods ?? Array.Empty<WeaponMod>()).Sum(m => m.WorkMinutes);

    /// <summary>
    /// 判定这组改装当前能否施加到 <paramref name="baseWeapon"/>，并列出全部未满足门槛。
    /// 三类门槛全过 ⇒ 可改装：① 营地有改装台；② 材料够付；③ 这组改装本身合法（大类/部位/型态）。
    /// </summary>
    /// <param name="baseWeapon">要改的基础武器。</param>
    /// <param name="mods">选中的改装。</param>
    /// <param name="availableMaterial">材料 RefKey → 当前库存计数（未登记视为 0）。</param>
    /// <param name="hasModBench">营地里是否已有改装台（由营地层判定）。</param>
    public static WeaponModAvailability CanApply(
        Weapon baseWeapon,
        IReadOnlyList<WeaponMod> mods,
        Func<string, int> availableMaterial,
        bool hasModBench)
    {
        if (baseWeapon is null) throw new ArgumentNullException(nameof(baseWeapon));
        if (availableMaterial is null) throw new ArgumentNullException(nameof(availableMaterial));

        var blocks = new List<WeaponModBlock>();
        mods ??= Array.Empty<WeaponMod>();

        // 1) 设施门槛：没有改装台，一切免谈（用户拍板：武器改造只能在改装台上做）。
        if (!hasModBench)
        {
            blocks.Add(new WeaponModBlock(
                WeaponModBlockReason.NoModBench,
                $"需先在工作台造一台{BenchFurnitureKey}（改装只能在{BenchFurnitureKey}上做）",
                BenchFurnitureKey));
        }

        // 2) 空选：没勾任何改装就点改装 —— 拦下（否则会白扣一把基础武器换一把同样的枪）。
        if (mods.Count == 0)
        {
            blocks.Add(new WeaponModBlock(
                WeaponModBlockReason.NothingSelected,
                "还没勾选任何改装",
                ""));
        }

        // 3) 材料门槛：按总成本核对库存。
        foreach (KeyValuePair<string, int> cost in TotalCost(mods))
        {
            int have = availableMaterial(cost.Key);
            if (have < cost.Value)
            {
                blocks.Add(new WeaponModBlock(
                    WeaponModBlockReason.InsufficientMaterial,
                    $"材料不足：{cost.Key} 需{cost.Value}、有{have}",
                    cost.Key));
            }
        }

        // 4) 合成合法性：把真正的合成规则跑一遍（大类不符 / 同部位冲突 / 近战型态三选一撞车）。
        //    不复述规则、直接问 ApplyMods —— 判定与执行同一个真源，永不漂移。
        if (mods.Count > 0)
        {
            try
            {
                WeaponMods.ApplyMods(baseWeapon, mods);
            }
            catch (WeaponModException ex)
            {
                blocks.Add(new WeaponModBlock(WeaponModBlockReason.InvalidCombination, ex.Message, ""));
            }
        }

        return new WeaponModAvailability(blocks.Count == 0, blocks);
    }

    /// <summary>
    /// 一次改装的材料扣减 delta（消耗为负）。**不校验**——调用方应先 <see cref="CanApply"/> 通过再调。
    /// 基础武器本身的消耗**不在此**（它不是材料，由调用方从库存移除那件武器物品）。
    /// </summary>
    public static IReadOnlyDictionary<string, int> Resolve(IReadOnlyList<WeaponMod> mods)
    {
        var deltas = new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> c in TotalCost(mods))
        {
            deltas[c.Key] = -c.Value;
        }
        return deltas;
    }
}
