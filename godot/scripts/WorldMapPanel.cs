using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class WorldMapPanel : CanvasLayer
{
    private struct Destination
    {
        public string Name;
        public Vector2 Position;
        public int TravelTimeSeconds;
    }

    private static readonly Destination[] Destinations =
    {
        new() { Name = "超市", Position = new Vector2(140, 120), TravelTimeSeconds = 300 },
        new() { Name = "医院", Position = new Vector2(420, 80), TravelTimeSeconds = 480 },
        new() { Name = "药店", Position = new Vector2(300, 300), TravelTimeSeconds = 360 },
        new() { Name = "住宅区", Position = new Vector2(100, 340), TravelTimeSeconds = 240 },
        new() { Name = "加油站", Position = new Vector2(460, 220), TravelTimeSeconds = 420 },
    };

    private Control _root = null!;
    private Control _mapContainer = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;
    private int _selectedIndex = -1;

    public event Action<string, int>? DestinationSelected;
    public event Action? Cancelled;

    public override void _Ready()
    {
        _root = new Control { Name = "WorldMapPanel" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Pass;
        AddChild(_root);

        var overlay = new ColorRect();
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.Color = new Color(0, 0, 0, 0.7f);
        overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(overlay);

        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(640, 500);
        panel.Position = new Vector2(-320, -250);
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
        title.Text = "选择目的地";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        _mapContainer = new Control();
        _mapContainer.Position = new Vector2(24, 52);
        _mapContainer.Size = new Vector2(592, 340);
        _mapContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(_mapContainer);

        foreach (var dest in Destinations)
        {
            var markerLabel = new Label();
            markerLabel.Text = dest.Name;
            markerLabel.Position = dest.Position + new Vector2(18, -8);
            markerLabel.AddThemeFontSizeOverride("font_size", 12);
            markerLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
            markerLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            markerLabel.AddThemeConstantOverride("outline_size", 3);
            markerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _mapContainer.AddChild(markerLabel);
        }

        var mapDraw = new WorldMapDraw();
        mapDraw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mapDraw.MouseFilter = Control.MouseFilterEnum.Pass;
        mapDraw.Destinations = Destinations;
        mapDraw.Clicked += OnMapClick;
        _mapContainer.AddChild(mapDraw);

        _confirmBtn = new Button();
        _confirmBtn.Text = "确认前往";
        _confirmBtn.Position = new Vector2(400, 416);
        _confirmBtn.Size = new Vector2(140, 38);
        _confirmBtn.Disabled = true;
        _confirmBtn.Pressed += OnConfirm;
        StyleButton(_confirmBtn, new Color(0.3f, 0.6f, 0.3f));
        panel.AddChild(_confirmBtn);

        _cancelBtn = new Button();
        _cancelBtn.Text = "取消";
        _cancelBtn.Position = new Vector2(240, 416);
        _cancelBtn.Size = new Vector2(120, 38);
        _cancelBtn.Pressed += () =>
        {
            _selectedIndex = -1;
            _confirmBtn.Disabled = true;
            Cancelled?.Invoke();
        };
        StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);
    }

    private void OnMapClick(int index)
    {
        _selectedIndex = index;
        _confirmBtn.Disabled = false;
        if (_mapContainer.GetChild(_mapContainer.GetChildCount() - 1) is WorldMapDraw draw)
        {
            draw.SelectedIndex = index;
            draw.QueueRedraw();
        }
    }

    private void OnConfirm()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Destinations.Length)
            return;
        var d = Destinations[_selectedIndex];
        DestinationSelected?.Invoke(d.Name, d.TravelTimeSeconds);
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

    private sealed partial class WorldMapDraw : Control
    {
        public Destination[] Destinations = Array.Empty<Destination>();
        public int SelectedIndex = -1;

        public event Action<int>? Clicked;

        public override void _Draw()
        {
            var mapBg = new Rect2(0, 0, Size.X, Size.Y);
            DrawRect(mapBg, new Color(0.12f, 0.14f, 0.12f), true);
            DrawRect(mapBg, new Color(0.2f, 0.22f, 0.18f), false, 2f);

            DrawRect(new Rect2(80, 60, 440, 260), new Color(0.14f, 0.16f, 0.14f), true);
            DrawRect(new Rect2(80, 60, 440, 260), new Color(0.22f, 0.24f, 0.2f), false, 1f);

            DrawLine(new Vector2(80, 160), new Vector2(520, 160), new Color(0.2f, 0.22f, 0.18f, 0.5f), 1f);
            DrawLine(new Vector2(80, 220), new Vector2(520, 220), new Color(0.2f, 0.22f, 0.18f, 0.5f), 1f);
            DrawLine(new Vector2(200, 60), new Vector2(200, 320), new Color(0.2f, 0.22f, 0.18f, 0.5f), 1f);
            DrawLine(new Vector2(380, 60), new Vector2(380, 320), new Color(0.2f, 0.22f, 0.18f, 0.5f), 1f);

            var roadColor = new Color(0.3f, 0.26f, 0.2f);
            DrawLine(new Vector2(120, 160), new Vector2(440, 160), roadColor, 3f);
            DrawLine(new Vector2(300, 100), new Vector2(300, 280), roadColor, 3f);
            DrawLine(new Vector2(120, 120), new Vector2(120, 280), roadColor, 2f);
            DrawLine(new Vector2(440, 120), new Vector2(440, 280), roadColor, 2f);

            for (int i = 0; i < Destinations.Length; i++)
            {
                var d = Destinations[i];
                bool hovered = i == SelectedIndex;
                float r = hovered ? 10f : 7f;
                var color = hovered ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.5f, 0.45f, 0.35f);
                DrawCircle(d.Position, r + 2, new Color(0, 0, 0, 0.5f));
                DrawCircle(d.Position, r, color, true);
                DrawCircle(d.Position, r, new Color(1, 1, 1, 0.3f), false, 1.5f);
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                for (int i = 0; i < Destinations.Length; i++)
                {
                    if (Destinations[i].Position.DistanceTo(mb.Position) < 16f)
                    {
                        Clicked?.Invoke(i);
                        return;
                    }
                }
            }
            if (@event is InputEventMouseMotion mm)
            {
                int prev = SelectedIndex;
                SelectedIndex = -1;
                for (int i = 0; i < Destinations.Length; i++)
                {
                    if (Destinations[i].Position.DistanceTo(mm.Position) < 16f)
                    {
                        SelectedIndex = i;
                        break;
                    }
                }
                if (prev != SelectedIndex)
                    QueueRedraw();
            }
        }
    }
}
