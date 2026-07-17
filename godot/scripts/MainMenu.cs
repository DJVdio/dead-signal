using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// **最简主菜单**（游戏入口场景）。它是冷启动读档的 <b>producer</b>：在切到 <see cref="CampMain"/> 之前
/// 决定「新开局」还是「读某个存档槽」，后者靠给 <c>CampMain.PendingColdLoadSlot</c> 赋值来传参
/// （见 <see cref="CampMain.Save"/> 里 <c>PendingColdLoadSlot</c>/<c>TakeColdLoadRequest</c> 的注释）。
///
/// <para>
/// <b>两个入口，刻意只做这两项</b>（用户拍板：最简 producer，不做设置/退出）：
/// </para>
/// <list type="bullet">
///   <item><b>新开局</b>——<b>不</b>设 <c>PendingColdLoadSlot</c>，直接切 <c>CampMain.tscn</c>。
///     静态槽默认 <c>null</c> ⇒ <c>_Ready</c> 的冷启动分支不入 ⇒ 走与旧的 CampMain 直启**逐字节等价**的新开局。</item>
///   <item><b>读档</b>——列出轮转自动存档槽（<see cref="SaveManager.List"/>，已按时刻新→旧、上限 6），
///     每槽两行文案（"第 12 天 · 黄昏聚餐" / "4 人存活 · 存于 …"），损坏/版本过旧的置灰不可点
///     （<see cref="MainMenuSlots.IsSelectable"/>）。点某槽 ⇒ 设 <c>PendingColdLoadSlot</c> 再切场景 ⇒ 走冷启动读档。</item>
/// </list>
///
/// <para>
/// UI 与游戏内的 <see cref="SavePanel"/> 同视觉语言（<see cref="UiStyle"/> 的语义色 + <c>StyleButton</c>），
/// 但这里是**全屏场景**（不是模态 CanvasLayer），因为它就是 <c>main_scene</c>。
/// </para>
/// </summary>
public sealed partial class MainMenu : Control
{
    private const string CampScenePath = "res://scenes/CampMain.tscn";

    private VBoxContainer _homeView = null!;   // 新开局 / 读档 两个主入口
    private Control _loadView = null!;         // 读档：槽列表
    private VBoxContainer _slotList = null!;
    private Label _loadStatus = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.Color = new Color(0.06f, 0.07f, 0.09f);
        AddChild(bg);

        BuildHomeView();
        BuildLoadView();
        ShowHome();
    }

    // ---- 主入口：新开局 / 读档 ----

    private void BuildHomeView()
    {
        _homeView = new VBoxContainer();
        _homeView.SetAnchorsPreset(LayoutPreset.Center);
        _homeView.CustomMinimumSize = new Vector2(320, 0);
        _homeView.Position = new Vector2(-160, -140);
        _homeView.AddThemeConstantOverride("separation", 16);
        AddChild(_homeView);

        var title = new Label { Text = "Dead Signal", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        title.AddThemeColorOverride("font_color", new Color(0.92f, 0.9f, 0.84f));
        _homeView.AddChild(title);

        var sub = new Label { Text = "死寂信号", HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeFontSizeOverride("font_size", 16);
        sub.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.52f));
        _homeView.AddChild(sub);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 24) };
        _homeView.AddChild(spacer);

        var newGame = new Button { Text = "新开局", CustomMinimumSize = new Vector2(320, 48) };
        UiStyle.StyleButton(newGame, UiStyle.Success, fontSize: 18);
        newGame.Pressed += OnNewGame;
        _homeView.AddChild(newGame);

        var load = new Button { Text = "读取存档", CustomMinimumSize = new Vector2(320, 48) };
        UiStyle.StyleButton(load, UiStyle.Warning, fontSize: 18);
        load.Pressed += ShowLoad;
        _homeView.AddChild(load);
    }

    /// <summary>新开局：不碰冷启动请求槽，切场景走正常新开局（逐字节零漂移）。</summary>
    private void OnNewGame()
    {
        CampMain.PendingColdLoadSlot = null;   // 明确清空：避免上一次读档面板残留污染新开局
        GetTree().ChangeSceneToFile(CampScenePath);
    }

    // ---- 读档：槽列表 ----

    private void BuildLoadView()
    {
        _loadView = new Control { Visible = false };
        _loadView.SetAnchorsPreset(LayoutPreset.Center);
        _loadView.CustomMinimumSize = new Vector2(620, 520);
        _loadView.Position = new Vector2(-310, -260);
        AddChild(_loadView);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(620, 520) };
        box.AddThemeConstantOverride("separation", 10);
        _loadView.AddChild(box);

        var title = new Label { Text = "读取存档" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", UiStyle.Warning);
        box.AddChild(title);

        // 与游戏内读档面板一致的一句话规则（否则玩家会找那个不存在的"存档"按钮）。
        var hint = new Label { Text = "游戏在每天的清晨聚餐与黄昏聚餐自动存档。没有手动存档。" };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.58f));
        box.AddChild(hint);

        _loadStatus = new Label { Text = "" };
        _loadStatus.AddThemeFontSizeOverride("font_size", 13);
        _loadStatus.AddThemeColorOverride("font_color", UiStyle.Danger);
        box.AddChild(_loadStatus);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(600, 400) };
        box.AddChild(scroll);

        _slotList = new VBoxContainer { CustomMinimumSize = new Vector2(584, 0) };
        _slotList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_slotList);

        var back = new Button { Text = "返回", CustomMinimumSize = new Vector2(100, 34) };
        UiStyle.StyleButton(back, UiStyle.Warning);
        back.Pressed += ShowHome;
        box.AddChild(back);
    }

    private void ShowHome()
    {
        _homeView.Visible = true;
        _loadView.Visible = false;
    }

    private void ShowLoad()
    {
        _homeView.Visible = false;
        _loadView.Visible = true;
        _loadStatus.Text = "";
        RefreshSlots();
    }

    private void RefreshSlots()
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
        bool selectable = MainMenuSlots.IsSelectable(slot.Compatible, slot.Corrupted);

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(580, 48) };
        row.AddThemeConstantOverride("separation", 8);

        var info = new VBoxContainer { CustomMinimumSize = new Vector2(450, 46) };

        // 主行「第 12 天 · 黄昏聚餐」——玩家靠这个认出是哪一个档。
        var head = new Label { Text = slot.Headline() };
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color",
            selectable ? new Color(0.95f, 0.95f, 0.9f) : UiStyle.Danger);
        info.AddChild(head);

        // 副行「4 人存活 · 存于 07-13 09:30」（不可读时改写为原因）。
        var sub = new Label { Text = slot.Detail() };
        sub.AddThemeFontSizeOverride("font_size", 12);
        sub.AddThemeColorOverride("font_color",
            selectable ? new Color(0.65f, 0.65f, 0.62f) : UiStyle.Danger);
        info.AddChild(sub);
        row.AddChild(info);

        var load = new Button { Text = "读取", CustomMinimumSize = new Vector2(90, 36) };
        UiStyle.StyleButton(load, selectable ? UiStyle.Success : UiStyle.Danger);
        // 版本过旧/损坏 ⇒ 读不了。置灰而不是隐藏——玩家得看见它还在，只是打不开。
        load.Disabled = !selectable;
        string captured = slot.Slot;
        load.Pressed += () => OnLoadSlot(captured);
        row.AddChild(load);

        return row;
    }

    /// <summary>选中某槽读档：写冷启动请求槽 → 切 CampMain（_Ready 会消费它走冷启动读档分支）。</summary>
    private void OnLoadSlot(string slot)
    {
        CampMain.PendingColdLoadSlot = slot;
        GetTree().ChangeSceneToFile(CampScenePath);
    }
}
