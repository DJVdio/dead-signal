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
        /// <summary>调查点规模三级（用户拍板：小 1~2 天 / 中 3~5 天 / 大 5 天以上）。各点定级为数据、拟定待调。</summary>
        public SizeTier Tier;
        /// <summary>显示名（缺省 null＝用 Name）。用于「正名」：内部路由键/flag 仍用 Name，地图只改显示。</summary>
        public string? DisplayName;
    }

    /// <summary>金手指帮根据地目的地名（CampMain / 探索关按此名分流发现点，务必一致）。</summary>
    public const string GoldfingerBaseName = "金手指帮根据地";

    /// <summary>守望者森林小屋目的地名（哥顿上吊尸+日记B 所在，与金手指帮根据地异地；按此名分流发现点，务必一致）。</summary>
    public const string WatchersCabinName = "守望者森林小屋";

    /// <summary>
    /// 城市之巅瞭望观景台目的地名（前中期可达调查点；望远镜瞭望发现点按此名分流，务必一致）。
    /// 关内核心交互物＝望远镜占位（见 <c>TestExploration.SetupCityRooftopLookout</c>）：踏入触发→（演出/剧情由兄弟系统接）→置 HordeSighted 旗标解锁尸潮倒计时。
    /// </summary>
    public const string CityRooftopLookoutName = "城市之巅瞭望观景台";

    /// <summary>
    /// 广播台目的地名（「dead signal」主线中后期探索点；发出设备定点投放于此，按此名分流发现点，务必一致）。
    /// 关内核心交互物＝发射机占位（见 <c>TestExploration.SetupBroadcastStation</c>）：踏入取得「发出设备」→ 电台解锁"回复军方/呼叫南方"抉择（见 <see cref="RadioMainline"/>）。
    /// </summary>
    public const string BroadcastStationName = "广播台";

    /// <summary>
    /// 南林村庄目的地名（前中期探索点，道格与布鲁斯正史入队地；须与 <see cref="VillageRescue.DestinationName"/> 一致，按此名分流关卡布局）。
    /// 关内核心＝一栋**上锁的屋子**（道格布鲁斯被困其中）+ 周边**丧尸围困**；调查团靠近中距离→布鲁斯吠叫引导→
    /// 清丧尸→开锁→发现饿昏迷的道格→回营正史入队（见 <c>TestExploration.SetupSouthForestVillage</c> 与 <see cref="VillageRescue"/>）。
    /// </summary>
    public const string SouthForestVillageName = VillageRescue.DestinationName;

    // 各点规模定级（Tier）为「拟定待调」：按各关内容量+直觉给档，用户可拍板改。
    // 大＝剧情重/内容多(5天+)；中＝常规据点(3~5天)；小＝单屋/单点(1~2天)。
    private static readonly Destination[] Destinations =
    {
        new() { Name = "超市", Position = new Vector2(140, 120), TravelTimeSeconds = 300, Tier = SizeTier.Medium },
        new() { Name = "医院", Position = new Vector2(420, 80), TravelTimeSeconds = 480, Tier = SizeTier.Large },
        // 南丁格尔的小药店（[SPEC-B13]）：正名"药店→南丁格尔的小药店"（内部路由键/flag 仍用 Name="药店"，只改 DisplayName，同守林人小屋先例）；
        // 用户口径"**小**药店"→定级 Small（5 物资点，band 下限；大头药品在医院）。关内核心＝可招募护士（见 TestExploration.SetupNightingalePharmacy 与 NurseRecruit）。
        new() { Name = NurseRecruit.DestinationName, DisplayName = NurseRecruit.DisplayName, Position = new Vector2(300, 300), TravelTimeSeconds = 360, Tier = SizeTier.Small },
        // [SPEC-B13·拟设定待确认] 住宅区正名「东部新村」：内部路由键 Name="住宅区" 不动（守林人小屋先例），显示名走 DisplayName。半建成迁建安置区＝建材工具大户，中点 12 处（见 ExplorationCache/TestExploration）。
        new() { Name = ExplorationCache.EastNewVillageName, DisplayName = "东部新村", Position = new Vector2(100, 340), TravelTimeSeconds = 240, Tier = SizeTier.Medium },
        // [SPEC-B13·拟设定待确认] 加油站（无正名）＝燃油大户，中点下限 10 处（加油区/便利店/修车棚/油罐区，见 ExplorationCache/TestExploration）。
        new() { Name = ExplorationCache.GasStationName, Position = new Vector2(460, 220), TravelTimeSeconds = 420, Tier = SizeTier.Medium },
        // [SPEC-B12-补] 用户改口径「金手指帮＝中型探索点·以战斗为主」：Large→Medium（配 11 处帮派储备点，见 ExplorationCache）。
        new() { Name = GoldfingerBaseName, Position = new Vector2(210, 210), TravelTimeSeconds = 540, Tier = SizeTier.Medium },
        // 森林深处、远离城镇：坐标落在城镇方框（80,60,440,260）之外的右侧林地，行程最长（拟定待调）。
        // 守望者森林小屋＝内部路由键/flag；显示名正名为「守林人小屋」（用户最新口径），小点样板（屋中屋+后院哥顿尸+2搜刮）。
        new() { Name = WatchersCabinName, DisplayName = "守林人小屋", Position = new Vector2(545, 150), TravelTimeSeconds = 600, Tier = SizeTier.Small },
        // 两个前中期探索点（用户拍板"加两个探索点 河边小屋 联合收割机仓库"），搜刮点铺设见 TestExploration，投放见 ExplorationCache。
        // 河边小屋：城镇以南、临河（坐标落在城镇方框下缘），行程 6 分钟（前中期档，拟定待调）。单间猎人小屋＝小点。
        new() { Name = ExplorationCache.RiversideCabinName, Position = new Vector2(250, 335), TravelTimeSeconds = 360, Tier = SizeTier.Small },
        // 联合收割机仓库：城镇东侧田野（方框之外的右下林地/农地），行程 7 分钟（前中期档，拟定待调）。农机棚+阁楼＝中点。
        new() { Name = ExplorationCache.HarvesterWarehouseName, Position = new Vector2(555, 285), TravelTimeSeconds = 420, Tier = SizeTier.Medium },
        // 城市之巅瞭望观景台：城镇北缘的高层建筑（坐标落城镇方框内偏北，正北可望见尸潮），前中期偏中档，行程 8 分钟（危险度/行程拟定待调）。用户口径瞭望台＝小点。
        new() { Name = CityRooftopLookoutName, Position = new Vector2(360, 110), TravelTimeSeconds = 480, Tier = SizeTier.Small },
        // 广播台：城镇北侧山脊上的通讯发射塔（坐标落城镇方框北缘外的高地），主线中后期解锁位、路程最远，行程 11 分钟（中后期定位/危险度/行程拟定待调）。机房+值班+备件＝中点。
        new() { Name = BroadcastStationName, Position = new Vector2(500, 55), TravelTimeSeconds = 660, Tier = SizeTier.Medium },
        // 南林村庄：城镇以南林地边缘的一处小聚落（坐标落城镇方框下缘外的南侧林带，与临河的河边小屋分开），
        // 道格布鲁斯正史入队地，用户拍板＝大调查点（按大点规格铺内容，doug-village 负责），行程 7 分钟（危险度/行程拟定待调）。
        new() { Name = SouthForestVillageName, Position = new Vector2(400, 330), TravelTimeSeconds = 420, Tier = SizeTier.Large },
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
            // 显示名用 DisplayName（正名，如「守林人小屋」）回退到 Name；规模/预计天数/完成度按用户拍板不对玩家外显。
            markerLabel.Text = $"{dest.DisplayName ?? dest.Name}（{dest.TravelTimeSeconds / 60} 分钟）";
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
