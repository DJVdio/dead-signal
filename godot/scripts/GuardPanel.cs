using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class GuardPanel : CanvasLayer
{
    public struct GuardPostDef
    {
        public int PostId;
        public string Name;
    }

    public struct PawnOption
    {
        public int Id;
        public string Name;
        public string EquipmentSummary;
    }

    private Control _root = null!;
    private VBoxContainer _postContainer = null!;
    private Label _emptyHint = null!;
    private Label _warningLabel = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;

    // 全空岗（无人守夜）时，首次「确认」只弹警示不放行，改成待二次确认态；再次点击才真正 emit。
    private bool _pendingEmptyConfirm;

    private readonly List<GuardPostDef> _posts = new();
    private readonly List<PawnOption> _pawns = new();
    private readonly List<OptionButton> _dropdowns = new();
    private readonly List<Label> _equipLabels = new();

    public event Action<Dictionary<int, int>>? GuardConfirmed;
    public event Action? Cancelled;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "GuardPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(520, 400),
            borderColor: new Color(0.25f, 0.22f, 0.18f));

        var title = new Label();
        title.Text = "夜晚岗哨配置";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        var listBg = new Panel();
        listBg.Position = new Vector2(24, 52);
        listBg.Size = new Vector2(472, 240);
        var listStyle = new StyleBoxFlat();
        listStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        listStyle.CornerRadiusTopLeft = 4;
        listStyle.CornerRadiusTopRight = 4;
        listStyle.CornerRadiusBottomLeft = 4;
        listStyle.CornerRadiusBottomRight = 4;
        listBg.AddThemeStyleboxOverride("panel", listStyle);
        panel.AddChild(listBg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        listBg.AddChild(scroll);

        _postContainer = new VBoxContainer();
        _postContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _postContainer.MouseFilter = Control.MouseFilterEnum.Pass;
        scroll.AddChild(_postContainer);

        _emptyHint = new Label();
        _emptyHint.Text = "没有可上岗的守卫（全员在外/伤重/已另有安排）。";
        _emptyHint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.VerticalAlignment = VerticalAlignment.Center;
        _emptyHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _emptyHint.AddThemeFontSizeOverride("font_size", 14);
        _emptyHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.55f));
        _emptyHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _emptyHint.Visible = false;
        listBg.AddChild(_emptyHint);

        _warningLabel = new Label();
        _warningLabel.Position = new Vector2(24, 300);
        _warningLabel.Size = new Vector2(472, 34);
        _warningLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _warningLabel.AddThemeFontSizeOverride("font_size", 14);
        _warningLabel.AddThemeColorOverride("font_color", UiStyle.Danger);
        _warningLabel.Visible = false;
        panel.AddChild(_warningLabel);

        _confirmBtn = new Button();
        _confirmBtn.Text = "确认";
        _confirmBtn.Position = new Vector2(340, 340);
        _confirmBtn.Size = new Vector2(140, 36);
        _confirmBtn.Pressed += OnConfirm;
        UiStyle.StyleButton(_confirmBtn, new Color(0.3f, 0.6f, 0.3f));
        panel.AddChild(_confirmBtn);

        _cancelBtn = new Button();
        _cancelBtn.Text = "取消";
        _cancelBtn.Position = new Vector2(180, 340);
        _cancelBtn.Size = new Vector2(120, 36);
        _cancelBtn.Pressed += () => Cancelled?.Invoke();
        UiStyle.StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);
    }

    public void SetupPosts(IReadOnlyList<GuardPostDef> posts)
    {
        _posts.Clear();
        _posts.AddRange(posts);
        _dropdowns.Clear();
        _equipLabels.Clear();
        UiStyle.ClearChildren(_postContainer);

        foreach (var post in _posts)
        {
            var hbox = new HBoxContainer();
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.MouseFilter = Control.MouseFilterEnum.Pass;

            var postLabel = new Label();
            postLabel.Text = post.Name;
            postLabel.CustomMinimumSize = new Vector2(100, 0);
            postLabel.AddThemeFontSizeOverride("font_size", 15);
            postLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
            hbox.AddChild(postLabel);

            var dropdown = new OptionButton();
            dropdown.CustomMinimumSize = new Vector2(180, 0);
            dropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            dropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
            dropdown.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
            dropdown.ItemSelected += _ => UpdateEquipSummary();
            hbox.AddChild(dropdown);
            _dropdowns.Add(dropdown);

            var equipLabel = new Label();
            equipLabel.Text = "";
            equipLabel.CustomMinimumSize = new Vector2(120, 0);
            equipLabel.AddThemeFontSizeOverride("font_size", 11);
            equipLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.55f));
            hbox.AddChild(equipLabel);
            _equipLabels.Add(equipLabel);

            var sep = new HSeparator();
            sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
            _postContainer.AddChild(hbox);
            _postContainer.AddChild(sep);
        }
    }

    public void SetPawns(IReadOnlyList<PawnOption> pawns)
    {
        _pawns.Clear();
        _pawns.AddRange(pawns);

        foreach (var dd in _dropdowns)
        {
            dd.Clear();
            dd.AddItem("（留空）", -1);
            foreach (var p in _pawns)
                dd.AddItem(p.Name, p.Id);
        }
        UpdateEquipSummary();
        _emptyHint.Visible = _pawns.Count == 0;
    }

    private void UpdateEquipSummary()
    {
        ResetEmptyConfirm(); // 排岗有变即撤回上一次的空岗警示
        for (int i = 0; i < _dropdowns.Count && i < _equipLabels.Count; i++)
        {
            int selectedId = _dropdowns[i].GetItemId(_dropdowns[i].Selected);
            if (selectedId < 0)
            {
                _equipLabels[i].Text = "";
                continue;
            }
            foreach (var p in _pawns)
            {
                if (p.Id == selectedId)
                {
                    _equipLabels[i].Text = p.EquipmentSummary;
                    break;
                }
            }
        }
    }

    private void OnConfirm()
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < _posts.Count && i < _dropdowns.Count; i++)
        {
            int pawnId = _dropdowns[i].GetItemId(_dropdowns[i].Selected);
            if (pawnId >= 0)
                map[_posts[i].PostId] = pawnId;
        }

        // 全空岗：首次确认只警示不放行（不硬拦，仅要求二次确认）。
        if (map.Count == 0 && !_pendingEmptyConfirm)
        {
            _pendingEmptyConfirm = true;
            _warningLabel.Text = "今夜无人守夜，袭营将无预警。再次点击「确认」放行。";
            _warningLabel.Visible = true;
            _confirmBtn.Text = "仍要确认";
            return;
        }

        GuardConfirmed?.Invoke(map);
    }

    /// <summary>清掉全空岗待二次确认态（玩家改了排岗或重开面板时调用）。</summary>
    private void ResetEmptyConfirm()
    {
        if (!_pendingEmptyConfirm)
            return;
        _pendingEmptyConfirm = false;
        _warningLabel.Visible = false;
        _confirmBtn.Text = "确认";
    }
}
