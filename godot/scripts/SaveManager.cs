using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 存档的**磁盘侧**：列举 / 读 / 写 / 删 `user://saves/*.json`。
/// <para>
/// 这是整个存档系统里<b>唯一</b>碰 Godot 的地方——状态树（<see cref="SaveData"/>）、编解码与版本闸门
/// （<see cref="SaveCodec"/>）、状态映射（<see cref="SaveMapper"/>）全是纯 C#，脱引擎可单测。
/// 本类只做一件事：把一串文本落到盘上，或从盘上捞回来。
/// </para>
/// <para>
/// 为什么是 <c>user://</c>：Godot 把它映射到各平台的用户数据目录（macOS 上是
/// <c>~/Library/Application Support/Godot/app_userdata/…</c>），是唯一保证可写的位置。
/// <c>res://</c> 在导出后是只读的（打进 pck 里），往那儿写存档到了玩家机器上就会失败。
/// </para>
/// </summary>
public static class SaveManager
{
    /// <summary>存档目录。</summary>
    public const string SaveDir = "user://saves";

    /// <summary>存档文件扩展名。</summary>
    private const string Ext = ".json";

    /// <summary>一个存档槽在列表里的样子。</summary>
    public readonly record struct SlotInfo(
        string Slot,
        SaveMeta? Meta,
        bool Compatible,
        bool Corrupted)
    {
        /// <summary>
        /// 列表主行：<b>"第 12 天 · 黄昏聚餐"</b>。
        /// 玩家认档靠的就是这个——"哦，那是我出门前的那个档"。
        /// </summary>
        public string Headline()
            => Corrupted || Meta is null
                ? "（存档已损坏）"
                : $"第 {Meta.Day} 天 · {DisplayNames.Of(Meta.Phase)}";

        /// <summary>列表副行：几人存活 + 存档时刻；读不了的话，这一行改说明原因。</summary>
        public string Detail()
        {
            if (Corrupted || Meta is null)
            {
                return "文件读不出来，只能删掉。";
            }
            if (!Compatible)
            {
                return "⚠️ 存档版本过旧，当前版本读不了它。";
            }
            return $"{Meta.SurvivorsAlive} 人存活 · 存于 {LocalStamp(Meta.SavedAtUtc)}";
        }

        /// <summary>存档时刻转成本地可读的"07-13 09:30"（存的是 UTC，给玩家看要转回来）。</summary>
        private static string LocalStamp(string savedAtUtc)
            => DateTime.TryParse(savedAtUtc, null,
                   System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                   out DateTime utc)
                ? utc.ToLocalTime().ToString("MM-dd HH:mm")
                : savedAtUtc;
    }

    private static string PathOf(string slot) => $"{SaveDir}/{slot}{Ext}";

    private static void EnsureDir()
    {
        if (!DirAccess.DirExistsAbsolute(SaveDir))
        {
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        }
    }

    /// <summary>
    /// 写一个存档槽。返回是否成功（失败时 <paramref name="error"/> 是给玩家看的中文）。
    /// <para>
    /// <b>先写临时文件、成功后再改名顶上</b>：直接往目标文件上写，万一写到一半进程挂了
    /// （或玩家强退），玩家就同时失去了新存档<b>和</b>旧存档——那个存档槽会变成一个写了半截的
    /// 破 JSON。改名在同一分区上是原子的，所以要么是完整的新档，要么是完好的旧档，没有中间态。
    /// </para>
    /// </summary>
    public static bool Write(string slot, SaveData data, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(slot))
        {
            error = "存档名不能为空。";
            return false;
        }

        EnsureDir();
        string json = SaveCodec.Serialize(data);
        string finalPath = PathOf(slot);
        string tmpPath = $"{finalPath}.tmp";

        using (FileAccess? f = FileAccess.Open(tmpPath, FileAccess.ModeFlags.Write))
        {
            if (f is null)
            {
                error = $"写不了存档（{FileAccess.GetOpenError()}）。";
                return false;
            }
            f.StoreString(json);
        }   // using 保证句柄关闭后才改名——否则改名可能撞上还没落盘的缓冲

        Error rename = DirAccess.RenameAbsolute(tmpPath, finalPath);
        if (rename != Error.Ok)
        {
            error = $"存档收尾失败（{rename}）。";
            return false;
        }
        return true;
    }

    /// <summary>读一个存档槽（过版本闸门）。槽不存在/版本不符/损坏 → <see cref="SaveLoadResult.Ok"/> 为 false，Error 是人话。</summary>
    public static SaveLoadResult Read(string slot)
    {
        string path = PathOf(slot);
        if (!FileAccess.FileExists(path))
        {
            return SaveLoadResult.Fail("找不到这个存档。");
        }

        using FileAccess? f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f is null)
        {
            return SaveLoadResult.Fail($"读不了存档（{FileAccess.GetOpenError()}）。");
        }
        return SaveCodec.Deserialize(f.GetAsText());
    }

    /// <summary>
    /// 列出全部存档槽，<b>新的在前</b>。
    /// 版本过旧/已损坏的<b>照样列出来</b>（标注不可读）——让它们从列表上凭空消失，
    /// 只会让玩家以为存档丢了。
    /// </summary>
    public static IReadOnlyList<SlotInfo> List()
    {
        EnsureDir();
        using DirAccess? dir = DirAccess.Open(SaveDir);
        if (dir is null)
        {
            return Array.Empty<SlotInfo>();
        }

        var slots = new List<SlotInfo>();
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(Ext, StringComparison.OrdinalIgnoreCase))
            {
                continue;   // .tmp 之类的中间文件不入列表
            }
            string slot = file[..^Ext.Length];
            slots.Add(Peek(slot));
        }

        // 按存档时刻倒序（字符串比较即可——ISO 8601 的字典序就是时间序，这正是选它的原因）。
        return slots
            .OrderByDescending(s => s.Meta?.SavedAtUtc ?? "")
            .ToList();
    }

    /// <summary>只读某槽的摘要（不反序列化整棵世界树）。</summary>
    public static SlotInfo Peek(string slot)
    {
        string path = PathOf(slot);
        if (!FileAccess.FileExists(path))
        {
            return new SlotInfo(slot, null, false, Corrupted: true);
        }

        using FileAccess? f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f is null)
        {
            return new SlotInfo(slot, null, false, Corrupted: true);
        }

        string json = f.GetAsText();
        SaveMeta? meta = SaveCodec.PeekMeta(json);
        return new SlotInfo(slot, meta, SaveCodec.IsCompatible(json), Corrupted: meta is null);
    }

    /// <summary>删一个存档槽。</summary>
    public static bool Delete(string slot)
    {
        string path = PathOf(slot);
        if (!FileAccess.FileExists(path))
        {
            return false;
        }
        return DirAccess.RemoveAbsolute(path) == Error.Ok;
    }

    /// <summary>这个槽存在吗（存档前问"要覆盖吗"）。</summary>
    public static bool Exists(string slot) => FileAccess.FileExists(PathOf(slot));

    /// <summary>存档时刻（ISO 8601 UTC）。字典序 = 时间序，列表排序直接拿它比。</summary>
    public static string NowUtc() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>
    /// 淘汰超出保留数的旧自动存档（写完新档后调）。
    /// <para>
    /// 轮转策略（保留几个、按什么排、淘汰哪些）是<b>纯逻辑</b>，住在 <see cref="SaveRotation"/> 里、有单测钉着。
    /// 本方法只负责按它给的名单去删文件——玩家没有手动存档可以兜底，删错一个就是永久失去一条退路。
    /// </para>
    /// </summary>
    public static void PruneAutosaves()
    {
        var ordered = List()   // 已按存档时刻从新到旧
            .Where(s => s.Meta is not null)
            .Select(s => (s.Slot, s.Meta!.SavedAtUtc));

        foreach (string stale in SaveRotation.SlotsToPrune(ordered))
        {
            Delete(stale);
        }
    }
}
