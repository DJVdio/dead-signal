using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// **读档面板**（模态）。与 <see cref="DiscoveryPanel"/>/<see cref="StashPanel"/> 同骨架
/// （CanvasLayer + <see cref="UiStyle.BuildModalShell"/>），视觉语言一致。
///
/// <para>
/// ⚠️ <b>这里没有"存档"按钮，是有意的</b>（用户拍板）。存档只由系统在相位切换时自动进行
/// （见 <see cref="SaveRotation.ShouldAutosaveAt"/>），玩家**没有"选择何时存档"的自由**——
/// 这从源头堵死了 S/L 大法：他没法在冲进去之前先手动存一个。
/// </para>
/// <para>
/// 但他有<b>随时离开的自由</b>：读档随时可读。打崩了想重来？只能回到上一个聚餐存档点，
/// 那半天的决策（派谁出门、搜了哪些点、开没开门、排谁的班）全部重做——
/// <b>而不是"读回到开枪前一秒重掷骰子"</b>。这就是检查点存档：<b>没有存档的自由，有离开的自由。</b>
/// </para>
/// </summary>
public sealed partial class SavePanel : CanvasLayer
{
    private Control _root = null!;
    private VBoxContainer _slotList = null!;
    private Label _statusLabel = null!;

    /// <summary>玩家要读某个槽。</summary>
    public event Action<string>? LoadRequested;

    /// <summary>面板关闭（CampMain 据此恢复时间流速）。</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        Layer = 12;
        Panel panel = UiStyle.BuildModalShell(
            this, out _root, "SavePanel",
            overlayAlpha: 0.55f,
            panelSize: new Vector2(620, 520),
            borderColor: UiStyle.Warning);

        var box = new VBoxContainer
        {
            Position = new Vector2(20, 18),
            CustomMinimumSize = new Vector2(580, 484),
        };
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var title = new Label { Text = "读取存档" };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", UiStyle.Warning);
        box.AddChild(title);

        // 把规则直接写在玩家眼前——否则他会一直在找那个不存在的"存档"按钮。
        var hint = new Label { Text = "游戏在每天的清晨聚餐与黄昏聚餐自动存档。没有手动存档。" };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.58f));
        box.AddChild(hint);

        _statusLabel = new Label { Text = "" };
        _statusLabel.AddThemeFontSizeOverride("font_size", 13);
        _statusLabel.AddThemeColorOverride("font_color", UiStyle.Danger);
        box.AddChild(_statusLabel);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(576, 366) };
        box.AddChild(scroll);

        _slotList = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        _slotList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_slotList);

        var close = new Button { Text = "关闭", CustomMinimumSize = new Vector2(100, 34) };
        UiStyle.StyleButton(close, UiStyle.Warning);
        close.Pressed += () => { HidePanel(); Closed?.Invoke(); };
        box.AddChild(close);

        _root.Visible = false;
    }

    /// <summary>打开面板并刷新槽位列表。</summary>
    public void Open()
    {
        _root.Visible = true;
        _statusLabel.Text = "";
        RefreshList();
    }

    private void HidePanel() => _root.Visible = false;

    private void RefreshList()
    {
        UiStyle.ClearChildren(_slotList);

        IReadOnlyList<SaveManager.SlotInfo> slots = SaveManager.List();
        if (slots.Count == 0)
        {
            var empty = new Label { Text = "还没有存档——熬到第一顿饭就有了。" };
            empty.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            _slotList.AddChild(empty);
            return;
        }

        foreach (SaveManager.SlotInfo slot in slots)
        {
            _slotList.AddChild(BuildRow(slot));
        }
    }

    private Control BuildRow(SaveManager.SlotInfo slot)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(556, 48) };
        row.AddThemeConstantOverride("separation", 8);

        var info = new VBoxContainer { CustomMinimumSize = new Vector2(430, 46) };

        // 主行「第 12 天 · 黄昏聚餐」——玩家靠这个认出是哪一个档。
        var head = new Label { Text = slot.Headline() };
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color",
            slot.Compatible ? new Color(0.95f, 0.95f, 0.9f) : UiStyle.Danger);
        info.AddChild(head);

        // 副行「4 人存活 · 存于 07-13 09:30」（不可读时改写为原因）。
        var sub = new Label { Text = slot.Detail() };
        sub.AddThemeFontSizeOverride("font_size", 12);
        sub.AddThemeColorOverride("font_color",
            slot.Compatible ? new Color(0.65f, 0.65f, 0.62f) : UiStyle.Danger);
        info.AddChild(sub);
        row.AddChild(info);

        var load = new Button { Text = "读取", CustomMinimumSize = new Vector2(90, 36) };
        UiStyle.StyleButton(load, slot.Compatible ? UiStyle.Success : UiStyle.Danger);
        // 版本过旧/损坏 ⇒ 读不了。置灰而不是隐藏——玩家得看见它还在，只是打不开。
        load.Disabled = !slot.Compatible;
        string captured = slot.Slot;
        load.Pressed += () => LoadRequested?.Invoke(captured);
        row.AddChild(load);

        return row;
    }

    /// <summary>读档失败时由 CampMain 回调（版本过旧 / 文件损坏）。</summary>
    public void ReportError(string? error)
    {
        _statusLabel.Text = error ?? "读取失败。";
        RefreshList();
    }
}
