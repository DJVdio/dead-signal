using System;
using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 RadioMainline.cs / EndingCg.cs / GameOverCondition.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「南逃谢幕」结局序列的**纯逻辑内核**（🔴 REUSABLE：军袭结局 + 将来「第 40 天无限尸潮」结局共用同一序列）。
// 序列形态（用户 authored）：全营被屠/覆灭 → 随机一名幸存者以半残状态向南逃 → 玩家操作段单线南逃到峡谷前
// → 对方大桥未落、两个哨兵冷眼看着 → 黑屏谢幕（游戏结束）。南逃者是**保留身份的桥梁角色**，过渡到第二幕
// 「峡谷营地」（很久远排期、不在本单实装）——本类只负责**持久化"是谁逃出去了"**，为第二幕留延续点。
//
// 运行时入口是 CampMain.BeginSouthEscapeEnding(逃亡者 pawn, 触发上下文)（Godot 层，见 CampMain.SouthEscape.cs）；
// 本纯类承载：触发上下文枚举、随机半残南逃者选择（纯函数·注入 IRandomSource 复现）、南逃者身份持久化、序列态 flag。

/// <summary>「南逃谢幕」序列由哪种结局触发——决定 CG-A 屠杀演出的施暴方与旁白语气（authored 待用户细化）。</summary>
public enum SouthEscapeTrigger
{
    /// <summary>军袭结局：回复军方后军人带顶级装备屠尽全营，随机一名幸存者半残南逃。</summary>
    MilitaryRaid,

    /// <summary>第 40 天尸潮结局：尸潮踏平营地，随机一名幸存者半残南逃。复用同一序列。</summary>
    HordeSiege,
}

/// <summary>
/// 「南逃谢幕」序列的纯逻辑内核（REUSABLE 入口的逻辑侧）：随机南逃者选择 + 身份持久化 + 序列态。
/// 状态一律存入 <see cref="StoryFlags"/>，随存档 <see cref="StoryFlags.Snapshot"/> 天然往返（无需另写序列化）。
/// </summary>
public static class SouthEscapeEnding
{
    // —— 持久化键（随 StoryFlags 存档天然往返；第二幕「峡谷营地」读它续接桥梁角色）——

    /// <summary>南逃者显示名（谁逃出去了）。第二幕据此续接同一角色。</summary>
    public const string EscapeeNameKey = "south_escapee_name";

    /// <summary>南逃者稳定 id（跨存档/跨幕的角色主键；无 id 时可空，仅靠名字）。</summary>
    public const string EscapeeIdKey = "south_escapee_id";

    /// <summary>触发本次南逃谢幕的结局种类（<see cref="SouthEscapeTrigger"/> 字符串承载）。</summary>
    public const string TriggerKey = "south_escape_trigger";

    /// <summary>南逃谢幕序列进行中/已发生 flag（防重入 + 存档标记本局已进入强制终局）。</summary>
    public const string SequenceActiveFlag = "south_escape_seq_active";

    // —— 随机半残南逃者选择（纯函数，注入 IRandomSource 复现）——

    /// <summary>
    /// 从存活幸存者里**随机选一名**作半残南逃者。泛型不依赖 Godot 类型（测试传 <c>string</c> 即可复现），
    /// 调用方（CampMain）传存活 Pawn 列表、拿回选中的那一个。空列表 → default；单人直接返回。
    /// index = clamp((int)<see cref="IRandomSource.Range"/>(0, count), 0, count-1)——与引擎其余随机同走可注入源。
    /// </summary>
    public static T? SelectEscapee<T>(IReadOnlyList<T> aliveSurvivors, IRandomSource rng)
    {
        if (aliveSurvivors == null || aliveSurvivors.Count == 0) return default;
        if (aliveSurvivors.Count == 1) return aliveSurvivors[0];
        int idx = (int)rng.Range(0, aliveSurvivors.Count);
        if (idx < 0) idx = 0;
        if (idx >= aliveSurvivors.Count) idx = aliveSurvivors.Count - 1;
        return aliveSurvivors[idx];
    }

    // —— 南逃者身份持久化（存档往返 → 第二幕延续点）——

    /// <summary>
    /// 记录南逃者身份 + 触发上下文，并置序列态 flag。<paramref name="id"/> 可空（仅靠名字）。
    /// <b>首次终局结果锁死</b>：本序列已经激活，或举家南逃已经启程/获胜时，后续调用一律无操作，
    /// 防止读档恢复或重复入口把逃亡者/触发源改写、以及好坏结局 flag 同时出现。flags 为空则无操作。
    /// </summary>
    public static void RecordEscapee(StoryFlags flags, string name, string? id, SouthEscapeTrigger trigger)
    {
        if (flags == null
            || IsSequenceActive(flags)
            || HasEscapee(flags)
            || FamilyEscapeWin.HasDeparted(flags)
            || FamilyEscapeWin.HasWon(flags))
        {
            return;
        }
        flags.Set(EscapeeNameKey, name);
        flags.Set(EscapeeIdKey, string.IsNullOrEmpty(id) ? null : id);
        flags.Set(TriggerKey, trigger.ToString());
        flags.Set(SequenceActiveFlag, "true");
    }

    /// <summary>南逃者显示名；未记录 → null。</summary>
    public static string? EscapeeName(StoryFlags flags) => flags?.Get(EscapeeNameKey);

    /// <summary>南逃者稳定 id；未记录/无 id → null。</summary>
    public static string? EscapeeId(StoryFlags flags) => flags?.Get(EscapeeIdKey);

    /// <summary>是否已记录南逃者（本局已进入南逃谢幕、有桥梁角色留存）。</summary>
    public static bool HasEscapee(StoryFlags flags) => flags != null && flags.Has(EscapeeNameKey);

    /// <summary>南逃谢幕序列是否已激活（防重入 + 存档识别本局强制终局态）。</summary>
    public static bool IsSequenceActive(StoryFlags flags) => flags != null && flags.Has(SequenceActiveFlag);

    /// <summary>触发本次南逃谢幕的结局种类；未记录/无法解析 → null。</summary>
    public static SouthEscapeTrigger? TriggerOf(StoryFlags flags)
    {
        string? v = flags?.Get(TriggerKey);
        return Enum.TryParse<SouthEscapeTrigger>(v, out var t) ? t : (SouthEscapeTrigger?)null;
    }
}
