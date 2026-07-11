using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class ExpeditionPanel : CanvasLayer
{
    public struct PawnEntry
    {
        public int Id;
        public string Name;
        public string WeaponSummary;
        public string ArmorSummary;

        /// <summary>随队伙伴（布鲁斯·狗）：不占 3 人上限、可与人类同勾。带狗须道格同队的约束由 CampMain 在确认时裁定。</summary>
        public bool IsCompanion;
    }

    private Control _root = null!;
    private VBoxContainer _listContainer = null!;
    private Label _emptyHint = null!;
    private Label _destinationLabel = null!;
    private Button _destBtn = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;

    private readonly List<PawnEntry> _pawns = new();
    private readonly List<CheckBox> _checkBoxes = new();
    private string _destination = "";
    private int _travelTime;

    public event Action? SelectDestinationRequested;
    public event Action<int[], string>? ExpeditionConfirmed;
    public event Action? Cancelled;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "ExpeditionPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(600, 460),
            borderColor: new Color(0.25f, 0.22f, 0.18f));

        var title = new Label();
        title.Text = "白天探险队配置";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        var listBg = new Panel();
        listBg.Position = new Vector2(24, 52);
        listBg.Size = new Vector2(552, 240);
        var listStyle = new StyleBoxFlat();
        listStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        listStyle.CornerRadiusTopLeft = 4;
        listStyle.CornerRadiusTopRight = 4;
        listStyle.CornerRadiusBottomLeft = 4;
        listStyle.CornerRadiusBottomRight = 4;
        listBg.AddThemeStyleboxOverride("panel", listStyle);
        panel.AddChild(listBg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        listBg.AddChild(scroll);

        _listContainer = new VBoxContainer();
        _listContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _listContainer.MouseFilter = Control.MouseFilterEnum.Pass;
        scroll.AddChild(_listContainer);

        _emptyHint = new Label();
        _emptyHint.Text = "没有可派出的队员（全员在外/伤重/已另有安排）。";
        _emptyHint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.VerticalAlignment = VerticalAlignment.Center;
        _emptyHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _emptyHint.AddThemeFontSizeOverride("font_size", 14);
        _emptyHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.55f));
        _emptyHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _emptyHint.Visible = false;
        listBg.AddChild(_emptyHint);

        _destinationLabel = new Label();
        _destinationLabel.Position = new Vector2(24, 308);
        _destinationLabel.Text = "目的地：未选择";
        _destinationLabel.AddThemeFontSizeOverride("font_size", 14);
        _destinationLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.65f));
        panel.AddChild(_destinationLabel);

        _destBtn = new Button();
        _destBtn.Text = "选择目的地";
        _destBtn.Position = new Vector2(24, 338);
        _destBtn.Size = new Vector2(160, 36);
        _destBtn.Pressed += () => SelectDestinationRequested?.Invoke();
        UiStyle.StyleButton(_destBtn, new Color(0.4f, 0.5f, 0.3f));
        panel.AddChild(_destBtn);

        _confirmBtn = new Button();
        _confirmBtn.Text = "确认出发";
        _confirmBtn.Position = new Vector2(400, 400);
        _confirmBtn.Size = new Vector2(160, 38);
        _confirmBtn.Disabled = true;
        _confirmBtn.Pressed += OnConfirm;
        UiStyle.StyleButton(_confirmBtn, new Color(0.3f, 0.6f, 0.3f));
        panel.AddChild(_confirmBtn);

        _cancelBtn = new Button();
        _cancelBtn.Text = "取消";
        _cancelBtn.Position = new Vector2(240, 400);
        _cancelBtn.Size = new Vector2(120, 38);
        _cancelBtn.Pressed += () => Cancelled?.Invoke();
        UiStyle.StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);
    }

    public void SetPawns(IReadOnlyList<PawnEntry> pawns)
    {
        _pawns.Clear();
        _pawns.AddRange(pawns);
        _checkBoxes.Clear();
        UiStyle.ClearChildren(_listContainer);

        foreach (var p in _pawns)
        {
            var hbox = new HBoxContainer();
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.MouseFilter = Control.MouseFilterEnum.Pass;

            var cb = new CheckBox();
            cb.Toggled += _ => OnCheckChanged();
            cb.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
            hbox.AddChild(cb);
            _checkBoxes.Add(cb);

            var nameLabel = new Label();
            nameLabel.Text = p.Name;
            nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            nameLabel.CustomMinimumSize = new Vector2(100, 0);
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
            hbox.AddChild(nameLabel);

            var weaponLabel = new Label();
            weaponLabel.Text = p.WeaponSummary;
            weaponLabel.CustomMinimumSize = new Vector2(140, 0);
            weaponLabel.AddThemeFontSizeOverride("font_size", 12);
            weaponLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.55f));
            hbox.AddChild(weaponLabel);

            var armorLabel = new Label();
            armorLabel.Text = p.ArmorSummary;
            armorLabel.CustomMinimumSize = new Vector2(140, 0);
            armorLabel.AddThemeFontSizeOverride("font_size", 12);
            armorLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.55f));
            hbox.AddChild(armorLabel);

            var separator = new HSeparator();
            separator.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
            _listContainer.AddChild(hbox);
            _listContainer.AddChild(separator);
        }
        _emptyHint.Visible = _pawns.Count == 0;
        OnCheckChanged();
    }

    public void SetDestination(string name, int travelTimeSeconds)
    {
        _destination = name;
        _travelTime = travelTimeSeconds;
        _destinationLabel.Text = $"目的地：{name}（{travelTimeSeconds / 60} 分钟）";
        OnCheckChanged();
    }

    private void OnCheckChanged()
    {
        // 只数人类（狗=companion 不占人头上限、不受满员禁用）。
        int humanChecked = 0;
        for (int i = 0; i < _checkBoxes.Count; i++)
        {
            if (_checkBoxes[i].ButtonPressed && i < _pawns.Count && !_pawns[i].IsCompanion)
                humanChecked++;
        }
        bool limitOk = humanChecked > 0 && humanChecked <= 3; // 至少 1 人、至多 3 人
        for (int i = 0; i < _checkBoxes.Count; i++)
        {
            bool isCompanion = i < _pawns.Count && _pawns[i].IsCompanion;
            // 人类满 3 后未勾选者禁用；狗始终可勾（不占人头）。
            if (!isCompanion && !_checkBoxes[i].ButtonPressed && humanChecked >= 3)
                _checkBoxes[i].Disabled = true;
            else
                _checkBoxes[i].Disabled = false;
        }
        _confirmBtn.Disabled = !limitOk || string.IsNullOrEmpty(_destination);
    }

    private void OnConfirm()
    {
        var ids = new List<int>();
        for (int i = 0; i < _checkBoxes.Count; i++)
        {
            if (_checkBoxes[i].ButtonPressed && i < _pawns.Count)
                ids.Add(_pawns[i].Id);
        }
        ExpeditionConfirmed?.Invoke(ids.ToArray(), _destination);
    }
}
