using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 世界地图（选目的地）。
///
/// <para>
/// 🔴 [T57] 目的地表**不再硬编码在这里** —— 全部来自 <c>res://data/world_graph.json</c>（<see cref="WorldGraph"/>）：
/// 名字/坐标/行程/规模/危险度/**前置**都在那份数据里。用户想重排路线，改数据即可，不用动代码。
/// </para>
///
/// <para>
/// 调查点是**网状解锁**的（用户原话）：「需要去过前置的调查点且探索度达到门槛，才能够走到之后的调查点。
/// 开局可以选择向东或者向西北的两个简单前期探查点。」判定全部走 <see cref="WorldGraphUnlock"/> 那一个纯函数
/// —— 这里**不许**另写一份"能不能去"的 if（那正是"锁是画上去的、其实点得进去"怎么来的）。
/// </para>
///
/// <para>
/// 锁着的点**照样画在地图上**，灰着、挂把锁、并且**明说为什么锁着**——
/// 把锁着的点藏起来，玩家就不知道还有路可走。
/// </para>
/// </summary>
public sealed partial class WorldMapPanel : CanvasLayer
{
    /// <summary>地图上一个可选目的地的渲染态（图数据 + 当下锁没锁）。</summary>
    private struct Destination
    {
        public WorldNode Node;
        public Vector2 Position;
        public bool Locked;
        /// <summary>锁着的理由（给玩家的一句人话）。解锁时为空。</summary>
        public string LockReason;

        public string Name => Node.Name;
        public string Display => Node.Display;
        public int TravelTimeSeconds => Node.TravelSeconds;
    }

    /// <summary>金手指帮根据地目的地名（CampMain / 探索关按此名分流发现点，务必一致）。</summary>
    public const string GoldfingerBaseName = "金手指帮根据地";

    /// <summary>守望者森林小屋目的地名（哥顿上吊尸+日记B 所在；按此名分流发现点，务必一致）。显示名正名为「守林人小屋」。</summary>
    public const string WatchersCabinName = "守望者森林小屋";

    /// <summary>城市之巅瞭望观景台目的地名（望远镜瞭望发现点按此名分流，务必一致）。</summary>
    public const string CityRooftopLookoutName = "城市之巅瞭望观景台";

    /// <summary>广播台目的地名（「dead signal」主线中后期探索点；发出设备定点投放于此）。</summary>
    public const string BroadcastStationName = "广播台";

    /// <summary>南林村庄目的地名（道格与布鲁斯正史入队地；须与 <see cref="VillageRescue.DestinationName"/> 一致）。</summary>
    public const string SouthForestVillageName = VillageRescue.DestinationName;

    /// <summary>世界图数据文件（单一事实源；wiki 上的「调查点路线」表由它播种）。</summary>
    public const string GraphDataPath = "res://data/world_graph.json";

    private static WorldGraph? _graph;

    /// <summary>世界图（懒加载，进程内只读一次）。</summary>
    public static WorldGraph Graph => _graph ??= LoadGraph();

    private static WorldGraph LoadGraph()
    {
        using var f = FileAccess.Open(GraphDataPath, FileAccess.ModeFlags.Read);
        if (f == null)
            throw new InvalidOperationException($"读不到世界图数据 {GraphDataPath}（FileAccess 错误码 {FileAccess.GetOpenError()}）");
        return WorldGraph.FromJson(f.GetAsText());
    }

    private Destination[] _destinations = Array.Empty<Destination>();
    private readonly List<Label> _markerLabels = new();

    private Control _root = null!;
    private Control _mapContainer = null!;
    private WorldMapDraw _mapDraw = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;
    private Label _hintLabel = null!;
    private int _selectedIndex = -1;

    // —— 解锁上下文（由 CampMain 灌进来；默认空＝只有起点开着）——
    private IReadOnlyCollection<string> _visited = Array.Empty<string>();
    private StoryFlags _flags = new();
    private bool _christineLeftForRevenge;
    private bool _legacyFullUnlock;

    public event Action<string, int>? DestinationSelected;
    public event Action? Cancelled;

    /// <summary>
    /// 灌入解锁上下文（去过哪些点 / 剧情旗标 / 老档兜底），并重算全图的锁。每次打开地图前由 CampMain 调。
    /// </summary>
    public void SetUnlockContext(
        IReadOnlyCollection<string> visited,
        StoryFlags flags,
        bool christineLeftForRevenge,
        bool legacyFullUnlock)
    {
        _visited = visited ?? Array.Empty<string>();
        _flags = flags ?? new StoryFlags();
        _christineLeftForRevenge = christineLeftForRevenge;
        _legacyFullUnlock = legacyFullUnlock;
        RefreshLocks();
    }

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

        BuildDestinations();

        _mapDraw = new WorldMapDraw();
        _mapDraw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mapDraw.MouseFilter = Control.MouseFilterEnum.Pass;
        _mapDraw.Destinations = _destinations;
        _mapDraw.Camp = new Vector2(Graph.Camp.X, Graph.Camp.Y);
        _mapDraw.Clicked += OnMapClick;
        _mapContainer.AddChild(_mapDraw);

        BuildMarkerLabels();

        // 锁着的点点下去 ⇒ 这行告诉玩家**为什么**（不是干脆不响应，那样玩家只会以为地图坏了）。
        _hintLabel = new Label();
        _hintLabel.Position = new Vector2(24, 398);
        _hintLabel.Size = new Vector2(592, 20);
        _hintLabel.AddThemeFontSizeOverride("font_size", 13);
        _hintLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.6f, 0.35f));
        _hintLabel.Text = "";
        panel.AddChild(_hintLabel);

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
            _hintLabel.Text = "";
            Cancelled?.Invoke();
        };
        UiStyle.StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);

        RefreshLocks();
    }

    private void BuildDestinations()
    {
        var list = new List<Destination>();
        foreach (var n in Graph.Nodes)
        {
            list.Add(new Destination
            {
                Node = n,
                Position = new Vector2(n.X, n.Y),
                Locked = !n.IsStart, // 开局默认：只有起点开着；SetUnlockContext 会重算
                LockReason = "",
            });
        }
        _destinations = list.ToArray();
    }

    private void BuildMarkerLabels()
    {
        const float labelWidth = 150f;
        foreach (var dest in _destinations)
        {
            var markerLabel = new Label();
            markerLabel.AddThemeFontSizeOverride("font_size", 12);
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
            _markerLabels.Add(markerLabel);
        }

        // 营地锚点的标签（起点的"向东/向西北"是相对它说的）。
        var campLabel = new Label();
        campLabel.Text = Graph.Camp.Display;
        campLabel.AddThemeFontSizeOverride("font_size", 13);
        campLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.55f));
        campLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        campLabel.AddThemeConstantOverride("outline_size", 3);
        campLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        campLabel.Position = new Vector2(Graph.Camp.X + 14, Graph.Camp.Y - 8);
        _mapContainer.AddChild(campLabel);
    }

    /// <summary>按当下的解锁上下文重算每个点的锁 + 刷新标签/重绘。</summary>
    private void RefreshLocks()
    {
        if (_destinations.Length == 0)
            return;

        for (int i = 0; i < _destinations.Length; i++)
        {
            var st = WorldGraphUnlock.StateOf(
                Graph, _destinations[i].Name, _visited, _flags, _christineLeftForRevenge, _legacyFullUnlock);
            _destinations[i].Locked = !st.Unlocked;
            _destinations[i].LockReason = st.Reason;
        }

        // 选中的点若在此期间被锁上（理论上不会，兜底），撤销选择。
        if (_selectedIndex >= 0 && _selectedIndex < _destinations.Length && _destinations[_selectedIndex].Locked)
        {
            _selectedIndex = -1;
            if (_confirmBtn != null)
                _confirmBtn.Disabled = true;
        }

        for (int i = 0; i < _markerLabels.Count && i < _destinations.Length; i++)
        {
            var d = _destinations[i];
            var lbl = _markerLabels[i];
            if (d.Locked)
            {
                // 锁着的点**照样显示**（名字 + 一把锁），但不给行程/危险度 —— 那是踩过点才知道的事。
                lbl.Text = $"🔒 {d.Display}";
                lbl.AddThemeColorOverride("font_color", new Color(0.45f, 0.42f, 0.38f));
            }
            else
            {
                string danger = d.Node.Danger is { } dt ? $" · {DisplayNames.Of(dt)}" : "";
                string enemies = d.Node.EnemyCount >= 0 ? $" · 敌对 {d.Node.EnemyCount}" : "";
                string start = d.Node.IsStart && !string.IsNullOrEmpty(d.Node.Start) ? $" · 向{d.Node.Start}" : "";
                lbl.Text = $"{d.Display}（{d.TravelTimeSeconds / 60} 分钟{danger}{enemies}{start}）";
                lbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
            }
        }

        if (_mapDraw != null)
        {
            _mapDraw.Destinations = _destinations;
            _mapDraw.SelectedIndex = _selectedIndex;
            _mapDraw.QueueRedraw();
        }
    }

    private void OnMapClick(int index)
    {
        if (index < 0 || index >= _destinations.Length)
            return;

        var d = _destinations[index];

        // 🔴 闸门（点击）：锁着的点**选不中** —— 但要告诉玩家为什么，而不是默默无视。
        if (!WorldGraphUnlock.CanTravelTo(Graph, d.Name, _visited, _flags, _christineLeftForRevenge, _legacyFullUnlock))
        {
            _hintLabel.Text = $"🔒 {d.Display}：{d.LockReason}";
            _selectedIndex = -1;
            _confirmBtn.Disabled = true;
            _mapDraw.SelectedIndex = -1;
            _mapDraw.QueueRedraw();
            return;
        }

        _hintLabel.Text = string.IsNullOrEmpty(d.Node.Summary) ? "" : d.Node.Summary;
        _selectedIndex = index;
        _confirmBtn.Disabled = false;
        _mapDraw.SelectedIndex = index;
        _mapDraw.QueueRedraw();
    }

    private void OnConfirm()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _destinations.Length)
            return;
        var d = _destinations[_selectedIndex];

        // 🔴 闸门（确认）：再问一次同一个纯函数。上下文可能在选中之后变过；
        // 而且"按钮 Disabled" 从来不是安全边界——真正的边界是这一行。
        if (!WorldGraphUnlock.CanTravelTo(Graph, d.Name, _visited, _flags, _christineLeftForRevenge, _legacyFullUnlock))
        {
            _hintLabel.Text = $"🔒 {d.Display}：{d.LockReason}";
            _selectedIndex = -1;
            _confirmBtn.Disabled = true;
            _mapDraw.SelectedIndex = -1;
            _mapDraw.QueueRedraw();
            return;
        }

        DestinationSelected?.Invoke(d.Name, d.TravelTimeSeconds);
    }

    private sealed partial class WorldMapDraw : Control
    {
        public Destination[] Destinations = Array.Empty<Destination>();
        public Vector2 Camp;
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

            // —— 网状路线：前置 → 后继 的连线。**它就是那张网**，玩家一眼看得出还有哪条路没走通。——
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Destinations.Length; i++)
                index[Destinations[i].Name] = i;

            for (int i = 0; i < Destinations.Length; i++)
            {
                var d = Destinations[i];
                // 起点：从营地拉一条线出去（那两条"向东/向西北"的路）。
                if (d.Node.IsStart)
                {
                    DrawLine(Camp, d.Position, new Color(0.42f, 0.5f, 0.36f, 0.75f), 2f);
                    continue;
                }
                foreach (string p in d.Node.Prereq)
                {
                    if (!index.TryGetValue(p, out int pi))
                        continue;
                    // 已解锁的路亮、还锁着的路暗（灰）——两条路都画，玩家才知道"还有一条没走"。
                    var c = d.Locked
                        ? new Color(0.28f, 0.28f, 0.26f, 0.55f)
                        : new Color(0.45f, 0.52f, 0.38f, 0.8f);
                    DrawLine(Destinations[pi].Position, d.Position, c, d.Locked ? 1.5f : 2f);
                }
            }

            // 营地锚点（不可选；起点的"向东/向西北"是相对它说的）。
            DrawCircle(Camp, 8f, new Color(0, 0, 0, 0.5f));
            DrawCircle(Camp, 6f, new Color(0.45f, 0.75f, 0.45f), true);
            DrawCircle(Camp, 6f, new Color(1, 1, 1, 0.35f), false, 1.5f);

            for (int i = 0; i < Destinations.Length; i++)
            {
                var d = Destinations[i];
                bool highlighted = i == SelectedIndex || i == HoveredIndex;
                float r = highlighted ? 10f : 7f;

                Color color;
                if (d.Locked)
                {
                    // 🔒 锁着：暗灰、更小、有一圈虚边 —— **看得见，但一眼就知道去不了**。
                    r = highlighted ? 8f : 6f;
                    color = new Color(0.3f, 0.29f, 0.27f);
                }
                else if (d.Node.IsStart)
                {
                    color = highlighted ? new Color(0.95f, 0.75f, 0.35f) : new Color(0.6f, 0.7f, 0.45f);
                }
                else
                {
                    color = highlighted ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.5f, 0.45f, 0.35f);
                }

                DrawCircle(d.Position, r + 2, new Color(0, 0, 0, 0.5f));
                DrawCircle(d.Position, r, color, true);
                DrawCircle(d.Position, r,
                    d.Locked ? new Color(0.6f, 0.55f, 0.45f, 0.55f) : new Color(1, 1, 1, 0.3f),
                    false, 1.5f);

                if (d.Locked)
                {
                    // 锁芯：中间一个更暗的点，远看就是"闭着的"。
                    DrawCircle(d.Position, 2.5f, new Color(0.14f, 0.14f, 0.13f), true);
                }
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
                        // 锁着的点也照样把点击**转出去** —— 由 WorldMapPanel 那道闸门决定"不给去 + 说明为什么"。
                        // （这里不自己判断锁，避免出现第二份"能不能去"的规则。）
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
