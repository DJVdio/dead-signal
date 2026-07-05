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
    }

    private Control _root = null!;
    private VBoxContainer _listContainer = null!;
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
        _root = new Control { Name = "ExpeditionPanel" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Pass;
        AddChild(_root);

        var overlay = new ColorRect();
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.Color = new Color(0, 0, 0, 0.6f);
        overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(overlay);

        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(600, 460);
        panel.Position = new Vector2(-300, -230);
        panel.MouseFilter = Control.MouseFilterEnum.Pass;
        _root.AddChild(panel);

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        bg.BorderColor = new Color(0.25f, 0.22f, 0.18f);
        bg.BorderWidthLeft = 2;
        bg.BorderWidthRight = 2;
        bg.BorderWidthTop = 2;
        bg.BorderWidthBottom = 2;
        bg.CornerRadiusTopLeft = 8;
        bg.CornerRadiusTopRight = 8;
        bg.CornerRadiusBottomLeft = 8;
        bg.CornerRadiusBottomRight = 8;
        panel.AddThemeStyleboxOverride("panel", bg);

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
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.Size = new Vector2(552, 240);
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        listBg.AddChild(scroll);

        _listContainer = new VBoxContainer();
        _listContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _listContainer.MouseFilter = Control.MouseFilterEnum.Pass;
        scroll.AddChild(_listContainer);

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
        StyleButton(_destBtn, new Color(0.4f, 0.5f, 0.3f));
        panel.AddChild(_destBtn);

        _confirmBtn = new Button();
        _confirmBtn.Text = "确认出发";
        _confirmBtn.Position = new Vector2(400, 400);
        _confirmBtn.Size = new Vector2(160, 38);
        _confirmBtn.Disabled = true;
        _confirmBtn.Pressed += OnConfirm;
        StyleButton(_confirmBtn, new Color(0.3f, 0.6f, 0.3f));
        panel.AddChild(_confirmBtn);

        _cancelBtn = new Button();
        _cancelBtn.Text = "取消";
        _cancelBtn.Position = new Vector2(240, 400);
        _cancelBtn.Size = new Vector2(120, 38);
        _cancelBtn.Pressed += () => Cancelled?.Invoke();
        StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);
    }

    public void SetPawns(IReadOnlyList<PawnEntry> pawns)
    {
        _pawns.Clear();
        _pawns.AddRange(pawns);
        _checkBoxes.Clear();
        ClearChildren(_listContainer);

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
        int checkedCount = 0;
        foreach (var cb in _checkBoxes)
        {
            if (cb.ButtonPressed)
                checkedCount++;
        }
        bool limitOk = checkedCount > 0 && checkedCount <= 3;
        for (int i = 0; i < _checkBoxes.Count; i++)
        {
            if (!_checkBoxes[i].ButtonPressed && checkedCount >= 3)
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

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
            node.RemoveChild(child);
    }

    private static void StyleButton(Button btn, Color accent)
    {
        btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.9f));
        var normal = new StyleBoxFlat();
        normal.BgColor = new Color(0.15f, 0.15f, 0.17f);
        normal.BorderColor = accent;
        normal.BorderWidthLeft = 1;
        normal.BorderWidthRight = 1;
        normal.BorderWidthTop = 1;
        normal.BorderWidthBottom = 1;
        normal.CornerRadiusTopLeft = 4;
        normal.CornerRadiusTopRight = 4;
        normal.CornerRadiusBottomLeft = 4;
        normal.CornerRadiusBottomRight = 4;
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = accent * new Color(1, 1, 1, 0.3f);
        hover.BorderColor = accent;
        hover.BorderWidthLeft = 1;
        hover.BorderWidthRight = 1;
        hover.BorderWidthTop = 1;
        hover.BorderWidthBottom = 1;
        hover.CornerRadiusTopLeft = 4;
        hover.CornerRadiusTopRight = 4;
        hover.CornerRadiusBottomLeft = 4;
        hover.CornerRadiusBottomRight = 4;
        btn.AddThemeStyleboxOverride("hover", hover);

        var disabled = new StyleBoxFlat();
        disabled.BgColor = new Color(0.08f, 0.08f, 0.1f);
        disabled.BorderColor = new Color(0.2f, 0.2f, 0.2f);
        disabled.BorderWidthLeft = 1;
        disabled.BorderWidthRight = 1;
        disabled.BorderWidthTop = 1;
        disabled.BorderWidthBottom = 1;
        disabled.CornerRadiusTopLeft = 4;
        disabled.CornerRadiusTopRight = 4;
        disabled.CornerRadiusBottomLeft = 4;
        disabled.CornerRadiusBottomRight = 4;
        btn.AddThemeStyleboxOverride("disabled", disabled);
    }
}
