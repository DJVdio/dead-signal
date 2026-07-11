using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 CraftingJob.cs / ContainerLoot.cs / Materials.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 开局营地废墟挖掘（批次9，参考《这是我的战争》开局挖废墟）：营地内铺数处废墟可交互点，玩家指派幸存者走过去
// 逐分钟挖掘（教交互）→ 挖满工时产出基础材料（木/碎金属/布，量级拟定待调）→ 该处永久清空、开拓可用空间。
// 本块两分工：①单个废墟点的**工时进度 + 一次性收获**载体 RubbleSite（工时进度同 CraftingJob，一次性清空同 ContainerLoot）；
// ②营地全部废墟点的注册表 + 批量推进 + 按点收获 RubbleField（同 ContainerLoot 的按名登记/查状态形态）。
// 产出复用 LootItem（→ LootApplication.Apply 落地入共享库存/食物），挖掘不发明新的落地逻辑。
// 空间执行（走过去/占用该人/清空视觉显露空地）落 CampMain Godot 运行时层，本块只出纯函数决策。

/// <summary>
/// 一处废墟点的工时进度 + 一次性收获载体（可变：进度逐分钟累积，收获后永久清空）。
/// 工时进度模型同 <see cref="CraftingJob"/>（仅"挖掘者在场"时 <see cref="Advance"/> 推进，中断不丢进度）；
/// 收获语义同 <see cref="ContainerLoot"/>（挖满后 <see cref="Harvest"/> 首次返回产出并标记 <see cref="Cleared"/>，再挖返回空）。
/// </summary>
public sealed class RubbleSite
{
    /// <summary>废墟点稳定标识（对齐 camp.json 里该废墟的 name；CampMain 据此关联视觉节点与收获落地）。</summary>
    public string Id { get; }

    /// <summary>挖净本处所需总工时（游戏分钟，= 拟定待调）。零工时视为即可收获。</summary>
    public int TotalWorkMinutes { get; }

    /// <summary>已投入工时（游戏分钟，封顶 <see cref="TotalWorkMinutes"/>）。</summary>
    public int ElapsedWorkMinutes { get; private set; }

    /// <summary>挖满后产出的掉落清单（复用 <see cref="LootItem"/>；基础材料 木/碎金属/布 等，量级拟定待调）。</summary>
    public IReadOnlyList<LootItem> Drops { get; }

    /// <summary>
    /// 是否含彩蛋位（1~2 处废墟埋彩蛋，内容 authored 待用户）。为 true 时 <see cref="EggContentId"/> 指向 authored 内容键；
    /// MVP 内容未填（EggContentId 空）→ 收获仍只出 <see cref="Drops"/> 普通材料，彩蛋落地由 CampMain 后续接 authored 内容。
    /// </summary>
    public bool HasEggSlot { get; }

    /// <summary>彩蛋 authored 内容键（待用户填；空=未填。仅 <see cref="HasEggSlot"/> 为 true 时有意义）。</summary>
    // TODO(彩蛋 authored 待用户)：填入具体彩蛋内容键后，CampMain 收获时据此播放 authored 剧情/发特殊物；当前仅数据槽占位。
    public string EggContentId { get; }

    /// <summary>已收获清空（挖满后 <see cref="Harvest"/> 过一次 → 该处永久消失、显露空地）。</summary>
    public bool Cleared { get; private set; }

    public RubbleSite(
        string id,
        int totalWorkMinutes,
        IEnumerable<LootItem>? drops = null,
        bool hasEggSlot = false,
        string eggContentId = "")
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("废墟 id 不可空", nameof(id));
        Id = id;
        TotalWorkMinutes = totalWorkMinutes < 0 ? 0 : totalWorkMinutes;
        Drops = drops?.ToList() ?? new List<LootItem>();
        HasEggSlot = hasEggSlot;
        EggContentId = eggContentId ?? "";
        ElapsedWorkMinutes = 0;
        Cleared = false;
    }

    /// <summary>已挖满工时（累计工时 ≥ 总工时；零工时视为即满）。</summary>
    public bool IsComplete => ElapsedWorkMinutes >= TotalWorkMinutes;

    /// <summary>剩余工时（游戏分钟），下限 0。</summary>
    public int RemainingWorkMinutes => Math.Max(0, TotalWorkMinutes - ElapsedWorkMinutes);

    /// <summary>挖掘进度 [0,1]（总工时为 0 时恒 1）。</summary>
    public float Progress => TotalWorkMinutes <= 0
        ? 1f
        : Math.Clamp((float)ElapsedWorkMinutes / TotalWorkMinutes, 0f, 1f);

    /// <summary>
    /// 推进挖掘工时：仅当 <paramref name="workerPresent"/>（挖掘者在场且在挖，由调用方合成）为真、
    /// <paramref name="minutes"/>&gt;0、且尚未挖满、且未清空时才累加，封顶总工时。返回本次实际推进的分钟数
    /// （暂停/已满/已清空/无效均为 0）。同 <see cref="CraftingJob.Advance"/>：中断（挖掘者被拉走）传 false → 停推进、不丢进度。
    /// </summary>
    /// <param name="minutes">本次流逝的游戏分钟。</param>
    /// <param name="workerPresent">调用方合成：指派的挖掘者仍在该废墟点作业（被袭营/改派/相位切走 = false）。</param>
    public int Advance(int minutes, bool workerPresent)
    {
        if (!workerPresent || minutes <= 0 || IsComplete || Cleared) return 0;
        int before = ElapsedWorkMinutes;
        ElapsedWorkMinutes = Math.Min(TotalWorkMinutes, ElapsedWorkMinutes + minutes);
        return ElapsedWorkMinutes - before;
    }

    /// <summary>
    /// 收获本处废墟：仅当已挖满（<see cref="IsComplete"/>）且未清空时，标记 <see cref="Cleared"/> 并返回 <see cref="Drops"/>；
    /// 未挖满或已收获过 → 返回空（不重复产出）。收获后该处永久清空（CampMain 据此移除视觉、显露空地）。
    /// </summary>
    public IReadOnlyList<LootItem> Harvest()
    {
        if (!IsComplete || Cleared) return Array.Empty<LootItem>();
        Cleared = true;
        return Drops;
    }
}

/// <summary>
/// 营地全部废墟点的注册表 + 批量推进 + 按点收获（同 <see cref="ContainerLoot"/> 的按名登记/查状态形态）。
/// CampMain 开局按 camp.json 登记全部废墟点；每游戏分钟对"当前有挖掘者在场的那处"推进；挖满后按点收获落地清空。
/// </summary>
public sealed class RubbleField
{
    private readonly Dictionary<string, RubbleSite> _sites = new();

    /// <summary>登记一处废墟点（按 <see cref="RubbleSite.Id"/>，重复登记覆盖）。空点忽略。</summary>
    public void Register(RubbleSite site)
    {
        if (site is null || string.IsNullOrEmpty(site.Id)) return;
        _sites[site.Id] = site;
    }

    /// <summary>该 id 是否已登记为废墟点。</summary>
    public bool Has(string id) => id != null && _sites.ContainsKey(id);

    /// <summary>按 id 取废墟点；查不到返回 <c>null</c>。</summary>
    public RubbleSite? Find(string id)
        => id != null && _sites.TryGetValue(id, out RubbleSite? s) ? s : null;

    /// <summary>该处是否已挖净清空（未登记按未清空处理）。</summary>
    public bool IsCleared(string id) => Find(id)?.Cleared ?? false;

    /// <summary>尚未清空的废墟点（供 CampMain 判断是否还剩可挖点 / 是否全挖净）。</summary>
    public IEnumerable<RubbleSite> ActiveSites => _sites.Values.Where(s => !s.Cleared);

    /// <summary>全部已挖净（无登记点时按 true——没有废墟即"全清"，语义中性）。</summary>
    public bool AllCleared => _sites.Values.All(s => s.Cleared);

    /// <summary>
    /// 对某处废墟推进工时（其挖掘者在场时）。转调 <see cref="RubbleSite.Advance"/>；未登记返回 0。
    /// 单点驱动——每次只有被指派挖掘者站在的那处传 <paramref name="workerPresent"/>=true。
    /// </summary>
    public int Advance(string id, int minutes, bool workerPresent)
        => Find(id)?.Advance(minutes, workerPresent) ?? 0;

    /// <summary>
    /// 收获某处废墟（挖满且未清空才出产并清空）。转调 <see cref="RubbleSite.Harvest"/>；未登记返回空。
    /// </summary>
    public IReadOnlyList<LootItem> Harvest(string id)
        => Find(id)?.Harvest() ?? Array.Empty<LootItem>();
}
