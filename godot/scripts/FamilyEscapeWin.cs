using System;
using System.Collections.Generic;
using System.Text;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SouthEscapeEnding.cs / SouthTrial.cs / EndingCg.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「举家南逃 WIN」好结局的**纯逻辑内核**（[SPEC-B11] 新矩阵好结局那条·用户拍板）：
//   南方三问**满 5 分通过**（<see cref="SouthTrial.IsPassed"/>）→ 回营地电台启程确认 → **举家南逃 WIN**。
//   与坏结局「南逃谢幕」（<see cref="SouthEscapeEnding"/>：屠营 + 随机一名半残者独自南逃 → 大桥未落·两哨兵冷眼）
//   **对称反转**：
//     · 坏结局＝单个半残者、屠营开场、大桥未落谢幕（dark）；
//     · 好结局＝**全营列队向南**、正面出发、**大桥落下 + 被迎接 + 胜利画面**（WIN）。
//
// 本类只承载**纯逻辑**：全营名单持久化（存档往返 → 第二幕「峡谷营地」全员延续接口）、WIN 达成/启程去重 flag、
// 与坏结局 outcome 的区分、正面 WIN CG 文本组装。运行时接线在 <see cref="CampMain"/>（CampMain.FamilyEscape.cs）。
// 空间演出（全员行军廊道/相机取景全队/大桥落下美术）落 Godot 运行时层，几何复用 <see cref="EscapeCorridor"/>（FamilyMode 分叉）。
//
// ★结局体系区分（供 outcome 判读）：
//   · 好结局 WIN ⇒ <see cref="HasWon"/>（本类 flag）；坏结局南逃谢幕 ⇒ <see cref="SouthEscapeEnding.IsSequenceActive"/>。
//     两者 flag 命名空间独立、互斥出现（同一局只走其一），故天然可区分。

/// <summary>
/// 「举家南逃 WIN」好结局的纯逻辑内核：全营名单持久化 + WIN/启程去重 flag + 与坏结局区分 + 正面 CG 文本组装。
/// 状态一律存入 <see cref="StoryFlags"/>，随存档 <see cref="StoryFlags.Snapshot"/> 天然往返（无需另写序列化）。
/// </summary>
public static class FamilyEscapeWin
{
    // —— 持久化键（随 StoryFlags 存档天然往返；第二幕「峡谷营地」读它续接全营）——

    /// <summary>全营南逃名单：显示名串（<see cref="RecordSep"/> 分隔各员）。</summary>
    public const string RosterNamesKey = "family_escape_roster_names";

    /// <summary>全营南逃名单：与 <see cref="RosterNamesKey"/> 平行的稳定 id 串（同分隔；无 id 位留空）。</summary>
    public const string RosterIdsKey = "family_escape_roster_ids";

    /// <summary>举家 WIN 已达成 flag（区分好结局与坏结局南逃谢幕 <see cref="SouthEscapeEnding.SequenceActiveFlag"/>）。</summary>
    public const string WonFlag = "family_escape_won";

    /// <summary>举家南逃已启程 flag（一次性去重，防重复触发终局序列）。</summary>
    public const string DepartedFlag = "family_escape_departed";

    /// <summary>名单串分隔符：ASCII 记录分隔符（0x1E，authored 中文显示名绝不含），随 JSON 存档天然往返。</summary>
    private const char RecordSep = '\u001e';

    /// <summary>南逃一员的身份（显示名 + 稳定 id，id 可空）。</summary>
    public readonly struct Member
    {
        public readonly string Name;
        public readonly string? Id;
        public Member(string name, string? id) { Name = name; Id = id; }
    }

    // —— 全营名单持久化（存档往返 → 第二幕全员延续接口）——

    /// <summary>
    /// 记录**全营南逃名单**并置 WIN 达成 flag（好结局 outcome）。名/ id 分两串平行存（<see cref="RecordSep"/> 分隔）。
    /// <b>首次终局结果锁死</b>：坏结局已经激活，或 WIN 已经记录时不再覆盖；
    /// flags 为空或名单为空则不置 WIN flag（无人可逃不算达成）。
    /// </summary>
    public static void RecordFamily(StoryFlags flags, IReadOnlyList<Member> roster)
    {
        if (flags == null
            || roster == null
            || roster.Count == 0
            || SouthEscapeEnding.IsSequenceActive(flags)
            || SouthEscapeEnding.HasEscapee(flags)
            || HasWon(flags))
        {
            return;
        }
        var names = new StringBuilder();
        var ids = new StringBuilder();
        for (int i = 0; i < roster.Count; i++)
        {
            if (i > 0) { names.Append(RecordSep); ids.Append(RecordSep); }
            names.Append(roster[i].Name ?? string.Empty);
            ids.Append(roster[i].Id ?? string.Empty);
        }
        flags.Set(RosterNamesKey, names.ToString());
        flags.Set(RosterIdsKey, ids.ToString());
        flags.Set(WonFlag, "true");
    }

    /// <summary>读回全营南逃名单（存档往返后仍一致）；未记录 → 空列表。</summary>
    public static IReadOnlyList<Member> Roster(StoryFlags flags)
    {
        string? namesRaw = flags?.Get(RosterNamesKey);
        if (string.IsNullOrEmpty(namesRaw)) return Array.Empty<Member>();
        string[] names = namesRaw.Split(RecordSep);
        string[] ids = (flags!.Get(RosterIdsKey) ?? string.Empty).Split(RecordSep);
        var list = new List<Member>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            string? id = i < ids.Length && ids[i].Length > 0 ? ids[i] : null;
            list.Add(new Member(names[i], id));
        }
        return list;
    }

    /// <summary>全营南逃名单人数；未记录 → 0。</summary>
    public static int RosterCount(StoryFlags flags) => Roster(flags).Count;

    // —— WIN 达成 / 与坏结局区分 ——

    /// <summary>是否已达成举家南逃 WIN（好结局 outcome）。</summary>
    public static bool HasWon(StoryFlags flags) => flags != null && flags.Has(WonFlag);

    // —— 启程去重（防重复触发终局序列）——

    /// <summary>举家南逃是否已启程（WIN 序列已触发过）。</summary>
    public static bool HasDeparted(StoryFlags flags) => flags != null && flags.Has(DepartedFlag);

    /// <summary>一次性置"已启程"：首次返回 true，其后恒 false；坏结局已激活时也返回 false（防结局串线）。</summary>
    public static bool MarkDeparted(StoryFlags flags)
    {
        if (flags == null
            || flags.Has(DepartedFlag)
            || SouthEscapeEnding.IsSequenceActive(flags)
            || SouthEscapeEnding.HasEscapee(flags))
        {
            return false;
        }
        flags.Set(DepartedFlag, "true");
        return true;
    }

    // —— 正面 WIN CG 文本组装（占位草稿·忠实 authored 节拍：举家幸存、大桥落下、被迎接、胜利画面）——
    // ⚠️ 与坏结局 <see cref="EndingCg.SouthEscape"/>「活下来的没剩几个」**完全不复用**——那是坏结局措辞。
    //   待 author 润色定稿；本处只忠实节拍占位（全营幸存向南、对方大桥落下、被迎接、胜利画面）。

    /// <summary>正面 WIN 谢幕 CG 完整播放序列（启程行军旁白 + 峡谷前被迎接的胜利段）。供 CampMain 喂 EndingPanel。</summary>
    public static IReadOnlyList<string> WinCg()
    {
        var list = new List<string>(EndingCg.FamilyDepartureNarration);
        list.AddRange(EndingCg.FamilyEscapeWin);
        return list;
    }
}
