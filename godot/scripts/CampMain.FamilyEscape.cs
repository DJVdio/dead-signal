using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 「举家南逃 WIN」好结局的**运行时接线**（消费 <see cref="FamilyEscapeWin"/> 纯逻辑内核）。
/// 触发链：南方三问满 5 分通过（<see cref="SouthTrial.IsPassed"/>）→ 回营地电台启程确认
/// （<see cref="ConfirmSouthDeparture"/> 通过分支）→ 本序列。
///
/// <para>与坏结局「南逃谢幕」（<see cref="BeginSouthEscapeEnding"/>）**共享几何、区分演出**：
/// <list type="number">
///   <item>启动 <see cref="BeginFamilyEscapeWin"/>：记录**全营名单**（持久化 → 第二幕全员延续接口）、置 WIN outcome flag、_gameOver。</item>
///   <item>全员行军段 <see cref="LoadEscapeCorridorFamily"/>：复用 <see cref="EscapeCorridor"/>（<see cref="EscapeCorridor.FamilyMode"/>=true）——
///     全员列队、玩家操控排头、跟随者自动跟、相机取景全队、**全员到齐**才触发终点（无屠营 CG-A、无半残移速惩罚）。</item>
///   <item>正面谢幕 <see cref="PlayFamilyEscapeWinFarewell"/>：峡谷前大桥落下 + 被迎接 → <see cref="EndingPanel"/> 播 CG-WIN → WIN 谢幕。</item>
/// </list>
/// 坏结局的单人序列（CampMain.SouthEscape.cs）不被本文件改动，仅复用其 <c>_escapeCorridor</c>/<c>_escapeCorridorRoot</c> 字段与
/// <c>_southEscapeActive</c> 相位停摆守卫（南逃走廊操作期间，营地聚餐/相位/陷阱副作用一律停摆，语义一致）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>
    /// 🟢 启动「举家南逃 WIN」好结局序列。<see cref="ConfirmSouthDeparture"/> 二次确认通过后调。
    /// 先确认全营仍有存活者，再由 <see cref="FamilyEscapeWin.MarkDeparted"/> 去重，记录全营名单并置 WIN outcome flag，
    /// 最后进入全员行军段。没有存活者时返回 false，不消费启程 flag、不锁终局。
    /// </summary>
    private bool BeginFamilyEscapeWin()
    {
        if (_southEscapeActive)
            return false;

        // 先确认至少一名存活者，再锁终局。否则确认面板与死亡事件同帧竞态时，会留下
        // _gameOver=true / departed=true、却没有名单也无法载入走廊的半终局。
        var family = _survivors.Where(s => s.Alive && IsInstanceValid(s)).ToList();
        if (family.Count == 0)
        {
            GD.Print("[举家南逃 WIN] 启程时已无存活者，取消终局锁定；全灭由 GameOverCondition 接管。");
            return false;
        }
        if (!FamilyEscapeWin.MarkDeparted(_storyFlags))
            return false; // 已启程过或坏结局已锁定

        _southEscapeActive = true; // 复用南逃走廊相位停摆守卫（OnGamePhaseChanged 最前）
        _gameOver = true;          // 好结局终局：停掉全灭/围攻/其它路由
        RecordPlaytestEvent(PlaytestEventKind.Ending, "举家南逃", "营地", $"好结局：全营 {family.Count} 人启程");

        // 全营存活者 = 举家南逃名单（全员延续到第二幕「峡谷营地」）。
        var roster = family
            .Select(p => new FamilyEscapeWin.Member(p.DisplayName, p.Id.ToString()))
            .ToList();
        FamilyEscapeWin.RecordFamily(_storyFlags, roster);
        GD.Print($"[举家南逃 WIN] 触发：全营 {family.Count} 人列队向南（名单已持久化，为第二幕全员延续留接口）。载入全员行军走廊。");

        LoadEscapeCorridorFamily(family);
        return true;
    }

    /// <summary>
    /// 载入 <see cref="EscapeCorridor"/> 的**全员行军版**（FamilyMode）：全员进廊道、玩家操控排头、跟随者自动跟；
    /// 复用坏结局的 <c>_escapeCorridor</c>/<c>_escapeCorridorRoot</c> 字段与 CampMain 既有选中+移动机制。
    /// 与坏结局单人版差异：全员放置、相机取景全队、**全员到齐**才触发、无半残移速惩罚（健全全营）。终点区触发 → CG-WIN。
    /// </summary>
    private void LoadEscapeCorridorFamily(List<Pawn> family)
    {
        if (_escapeCorridorRoot != null || family == null || family.Count == 0)
            return;

        var scene = GD.Load<PackedScene>("res://scenes/EscapeCorridor.tscn");
        _escapeCorridorRoot = scene.Instantiate();
        _escapeCorridor = (EscapeCorridor)_escapeCorridorRoot;
        _currentLevel = _escapeCorridor; // 令 CampMain 输入按"在关卡里"处理（营地容器点不到）

        _escapeCorridor.Clock = _clock;
        _escapeCorridor.FamilyMode = true;                 // 🟢 全员行军版分叉（放全员/跟随/取景全队/全员到齐/大桥落下美术）
        _escapeCorridor.ExpeditionTeam = new List<Pawn>(family);
        _escapeCorridor.OnReachedCanyon += PlayFamilyEscapeWinFarewell;

        _campNavRegion.Enabled = false;
        GetTree().Root.AddChild(_escapeCorridorRoot);
        SetCampVisible(false);
        _escapeCorridor.Initialize();

        // 选中排头 → 右键操控生效（跟随者由 EscapeCorridor 自动跟排头）。WIN 全营健全，无半残移速惩罚。
        SetSelection(family[0]);

        // 解冻，进入可操作行走（启程确认时标本为 0）。
        Engine.TimeScale = 1;
        GD.Print("[举家南逃 WIN] 进入全员行军段：右键操控排头向南走到峡谷前，全员到齐即谢幕。");
    }

    /// <summary>
    /// 正面谢幕（全员踏入终点区触发）：大桥落下、有人来迎——走 <see cref="EndingPanel"/> 播 CG-WIN 分段文本，播完＝**好结局 WIN**。
    /// 与坏结局 <see cref="PlaySouthEscapeFarewell"/>（大桥未落·两哨兵冷眼）对称反转。
    /// </summary>
    private void PlayFamilyEscapeWinFarewell()
    {
        Engine.TimeScale = 0;
        GD.Print("[举家南逃 WIN] 全员抵达峡谷前：大桥落下，有人来迎。播 CG-WIN 谢幕（好结局）。");
        EndingPanel.Show(_hud, FamilyEscapeWin.WinCg(), EndingCg.FamilyEscapeWinTitle,
            "res://assets/cg/family-win.png");
    }
}
