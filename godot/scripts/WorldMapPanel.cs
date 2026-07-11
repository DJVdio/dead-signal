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

    /// <summary>金手指帮根据地目的地名（CampMain / 探索关按此名分流发现点，务必一致）。</summary>
    public const string GoldfingerBaseName = "金手指帮根据地";

    /// <summary>守望者森林小屋目的地名（哥顿上吊尸+日记B 所在，与金手指帮根据地异地；按此名分流发现点，务必一致）。</summary>
    public const string WatchersCabinName = "守望者森林小屋";

    private static readonly Destination[] Destinations =
    {
        new() { Name = "超市", Position = new Vector2(140, 120), TravelTimeSeconds = 300 },
        new() { Name = "医院", Position = new Vector2(420, 80), TravelTimeSeconds = 480 },
        new() { Name = "药店", Position = new Vector2(300, 300), TravelTimeSeconds = 360 },
        new() { Name = "住宅区", Position = new Vector2(100, 340), TravelTimeSeconds = 240 },
        new() { Name = "加油站", Position = new Vector2(460, 220), TravelTimeSeconds = 420 },
        new() { Name = GoldfingerBaseName, Position = new Vector2(210, 210), TravelTimeSeconds = 540 },
        // 森林深处、远离城镇：坐标落在城镇方框（80,60,440,260）之外的右侧林地，行程最长（拟定待调）。
        new() { Name = WatchersCabinName, Position = new Vector2(545, 150), TravelTimeSeconds = 600 },
    };

    private Control _root = null!;
    private Control _mapContainer = null!;
    private WorldMapDraw _mapDraw = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;
    private int _selectedIndex = -1;

    public event Action<string, int>? DestinationSelected;
    public event Action? Cancelled;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "WorldMapPanel",
            overlayAlpha: 0.7f,
            panelSize: new Vector2(640, 500),
            borderColor: new Color(0.25f, 0.22f, 0.18f));

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

        const float labelWidth = 150f;
        foreach (var dest in Destinations)
        {
            var markerLabel = new Label();
            markerLabel.Text = $"{dest.Name}（{dest.TravelTimeSeconds / 60} 分钟）";
            markerLabel.AddThemeFontSizeOverride("font_size", 12);
            markerLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
            markerLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            markerLabel.AddThemeConstantOverride("outline_size", 3);
            markerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            markerLabel.CustomMinimumSize = new Vector2(labelWidth, 0);
            // 靠右侧的点把标签放到左边并右对齐，避免文字冲出地图容器右界。
            bool placeLeft = dest.Position.X + 18 + labelWidth > _mapContainer.Size.X;
            if (placeLeft)
            {
                markerLabel.HorizontalAlignment = HorizontalAlignment.Right;
                markerLabel.Position = dest.Position + new Vector2(-18 - labelWidth, -8);
            }
            else
            {
                markerLabel.HorizontalAlignment = HorizontalAlignment.Left;
                markerLabel.Position = dest.Position + new Vector2(18, -8);
            }
            _mapContainer.AddChild(markerLabel);
        }

        _mapDraw = new WorldMapDraw();
        _mapDraw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mapDraw.MouseFilter = Control.MouseFilterEnum.Pass;
        _mapDraw.Destinations = Destinations;
        _mapDraw.Clicked += OnMapClick;
        _mapContainer.AddChild(_mapDraw);

        _confirmBtn = new Button();
        _confirmBtn.Text = "确认前往";
        _confirmBtn.Position = new Vector2(400, 416);
        _confirmBtn.Size = new Vector2(140, 38);
        _confirmBtn.Disabled = true;
        _confirmBtn.Pressed += OnConfirm;
        UiStyle.StyleButton(_confirmBtn, new Color(0.3f, 0.6f, 0.3f));
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
        UiStyle.StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);
    }

    private void OnMapClick(int index)
    {
        _selectedIndex = index;
        _confirmBtn.Disabled = false;
        _mapDraw.SelectedIndex = index;
        _mapDraw.QueueRedraw();
    }

    private void OnConfirm()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Destinations.Length)
            return;
        var d = Destinations[_selectedIndex];
        DestinationSelected?.Invoke(d.Name, d.TravelTimeSeconds);
    }

    private sealed partial class WorldMapDraw : Control
    {
        public Destination[] Destinations = Array.Empty<Destination>();
        public int SelectedIndex = -1; // 点选目标：恒亮，不被鼠标移开清除
        public int HoveredIndex = -1;  // 悬停高亮：随鼠标进出增删，与 SelectedIndex 独立

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
                bool highlighted = i == SelectedIndex || i == HoveredIndex;
                float r = highlighted ? 10f : 7f;
                var color = highlighted ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.5f, 0.45f, 0.35f);
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
                int prev = HoveredIndex;
                HoveredIndex = -1;
                for (int i = 0; i < Destinations.Length; i++)
                {
                    if (Destinations[i].Position.DistanceTo(mm.Position) < 16f)
                    {
                        HoveredIndex = i;
                        break;
                    }
                }
                if (prev != HoveredIndex)
                    QueueRedraw();
            }
        }
    }
}
