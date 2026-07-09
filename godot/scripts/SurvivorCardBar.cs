using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 屏幕底部幸存者卡牌栏（RimWorld/《这是我的战争》式底部头像条）：横向排列每个幸存者一张卡
/// （头像 + 姓名 + 血量/饥饿状态条），单击选中、双击聚焦。
///
/// **完全自包含**：不引用 <c>CampMain</c>，可被独立 <c>new</c> 出来 <c>AddChild</c> 到任意
/// <c>CanvasLayer</c>（或铺满视口的 Control）下——锚点才相对屏幕解析。接线（喂幸存者、连事件）
/// 由外部（CampMain）负责，本类只暴露下述对外 API。
///
/// 骨架与配色沿用本工程 <c>Hud.cs</c>/<c>CharacterPanel.cs</c> 的代码驱动 UI 风格。
/// Id→头像/占位色的稳定映射走纯逻辑 <see cref="SurvivorCardVisuals"/>（已单测）。
///
/// 布局避让：屏幕右侧约 340px 被 <c>CharacterPanel</c> 占用，本栏右边距留 356px 不与之重叠。
/// </summary>
public sealed partial class SurvivorCardBar : Control
{
    // —— 尺寸与边距（拟定，可调）——
    private const int CardWidth = 92;
    private const int CardHeight = 104;
    private const int PortraitHeight = 60;
    private const int RightReserve = 356; // 让开右侧 CharacterPanel（340）+ 余量
    private const int SideMargin = 12;
    private const int BottomMargin = 12;
    private const int CardSeparation = 6;

    // —— 配色（与 CharacterPanel 同一深色底 + 文字色）——
    private static readonly Color ColText = new(0.93f, 0.95f, 1f);
    private static readonly Color ColCardBg = new(0.08f, 0.09f, 0.11f, 0.92f);
    private static readonly Color ColCardBorder = new(0.30f, 0.34f, 0.40f, 0.9f);
    private static readonly Color ColSelectedBorder = new(0.62f, 0.82f, 1f); // 选中高亮：与 SectionTitle 同蓝
    private static readonly Color ColBarBg = new(0.16f, 0.17f, 0.2f);

    /// <summary>单击某卡牌（选中该幸存者）。</summary>
    public event Action<Pawn>? CardClicked;

    /// <summary>双击某卡牌（聚焦/居中该幸存者）。用 <c>InputEventMouseButton.DoubleClick</c> 判定。</summary>
    public event Action<Pawn>? CardDoubleClicked;

    private HBoxContainer _row = null!;
    private readonly List<CardEntry> _cards = new();
    private Pawn? _selected;

    /// <summary>一张卡牌与其绑定幸存者 + 可重着色的边框样式。</summary>
    private sealed class CardEntry
    {
        public required Pawn Pawn;
        public required PanelContainer Panel;
        public required StyleBoxFlat Style;
    }

    public override void _Ready() => BuildSkeleton();

    private void BuildSkeleton()
    {
        // 贴屏幕底边的一条横向 strip：左对齐、右侧让开 CharacterPanel。
        AnchorLeft = 0f;
        AnchorTop = 1f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetLeft = SideMargin;
        OffsetTop = -(CardHeight + BottomMargin);
        OffsetRight = -RightReserve;
        OffsetBottom = -BottomMargin;
        // 根不吃点击（卡牌之间/周围的空隙放行世界点击）；由卡牌自身 Stop 捕获。
        MouseFilter = MouseFilterEnum.Ignore;

        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", CardSeparation);
        _row.AnchorLeft = 0f;
        _row.AnchorTop = 0f;
        _row.AnchorRight = 1f;
        _row.AnchorBottom = 1f;
        _row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_row);
    }

    /// <summary>
    /// 刷新卡牌列表（重建全部卡）：按传入顺序为每个幸存者建一张卡，读其当前快照填血量/饥饿。
    /// 重复调用即整体刷新（增删幸存者或更新状态都调它）。会保留仍在列表中的选中高亮。
    /// </summary>
    public void SetSurvivors(IEnumerable<Pawn> survivors)
    {
        if (_row is null)
        {
            BuildSkeleton(); // 允许在入树前先喂数据
        }

        foreach (CardEntry c in _cards)
        {
            c.Panel.QueueFree();
        }
        _cards.Clear();

        foreach (Pawn p in survivors)
        {
            _cards.Add(BuildCard(p));
        }

        // 选中项若已不在新列表则清空，否则重着色。
        if (_selected is not null && !_cards.Exists(c => c.Pawn == _selected))
        {
            _selected = null;
        }
        ApplySelectionStyles();
    }

    /// <summary>高亮当前选中卡牌（传 null 取消全部高亮）。不改变列表。</summary>
    public void SetSelected(Pawn? p)
    {
        _selected = p;
        ApplySelectionStyles();
    }

    // ———————————————————————————— 单卡构建 ————————————————————————————

    private CardEntry BuildCard(Pawn pawn)
    {
        (float r, float g, float b) = SurvivorCardVisuals.StableColorForId(pawn.Id);
        var stable = new Color(r, g, b);

        var style = new StyleBoxFlat
        {
            BgColor = ColCardBg,
            BorderColor = ColCardBorder,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
            ContentMarginTop = 5,
            ContentMarginBottom = 5,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(CardWidth, CardHeight),
            MouseFilter = MouseFilterEnum.Stop, // 捕获点击
            TooltipText = pawn.DisplayName,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var entry = new CardEntry { Pawn = pawn, Panel = panel, Style = style };
        panel.GuiInput += e => OnCardInput(e, pawn);

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 3);
        panel.AddChild(vbox);

        // —— 头像（有素材用图，缺素材用稳定色块占位）——
        vbox.AddChild(BuildPortrait(pawn, stable));

        // —— 姓名 ——
        var name = new Label
        {
            Text = pawn.DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", pawn.Alive ? ColText : new Color(0.6f, 0.6f, 0.62f));
        vbox.AddChild(name);

        // —— 状态条：血量 + 饥饿（能从快照拿就拿）——
        vbox.AddChild(BuildStatusBars(pawn));

        _row.AddChild(panel);
        return entry;
    }

    /// <summary>头像块：优先 res://assets/portraits 下按 Id 稳定映射的图；无导入则用稳定色块占位（留 TextureRect 结构）。</summary>
    private static Control BuildPortrait(Pawn pawn, Color placeholder)
    {
        string file = SurvivorCardVisuals.PortraitFileForId(pawn.Id);
        string path = $"res://assets/portraits/{file}";

        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;

        if (tex is not null)
        {
            var rect = new TextureRect
            {
                Texture = tex,
                CustomMinimumSize = new Vector2(0, PortraitHeight),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
                ClipContents = true,
            };
            return rect;
        }

        // 占位色块（素材未导入时）——同为 Control 子树，接口结构不变。
        var block = new ColorRect
        {
            Color = placeholder,
            CustomMinimumSize = new Vector2(0, PortraitHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // 占位上叠姓名首字，便于无图时也能区分。
        var initial = new Label
        {
            Text = pawn.DisplayName.Length > 0 ? pawn.DisplayName.Substring(0, 1) : "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        initial.AnchorRight = 1f;
        initial.AnchorBottom = 1f;
        initial.AddThemeFontSizeOverride("font_size", 28);
        initial.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
        block.AddChild(initial);
        return block;
    }

    /// <summary>血量条（红↔绿）+ 饥饿条（按等级配色）。从 <see cref="Pawn.Inspect"/> 快照取值，拿不到用满值占位。</summary>
    private static Control BuildStatusBars(Pawn pawn)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);

        // 快照：血量比例 0..1、饥饿等级 0..6。
        PawnInspection insp = pawn.Inspect();

        float blood = Mathf.Clamp((float)insp.BloodRatio, 0f, 1f);
        box.AddChild(MakeBar(blood, BloodColor(blood)));

        // 饥饿：5=正常满，向 0 递减为"越饿"。用等级/上限做占比，配色按等级加深。
        float hungerRatio = Mathf.Clamp(insp.HungerStage / 5f, 0f, 1f);
        box.AddChild(MakeBar(hungerRatio, HungerColor(insp.HungerStage)));

        return box;
    }

    private static ProgressBar MakeBar(float value01, Color fill)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = value01,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = ColBarBg });
        return bar;
    }

    // ———————————————————————————— 交互 ————————————————————————————

    private void OnCardInput(InputEvent e, Pawn pawn)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
        {
            if (mb.DoubleClick)
            {
                CardDoubleClicked?.Invoke(pawn);
            }
            else
            {
                CardClicked?.Invoke(pawn);
            }
            AcceptEvent();
        }
    }

    private void ApplySelectionStyles()
    {
        foreach (CardEntry c in _cards)
        {
            bool sel = c.Pawn == _selected;
            c.Style.BorderColor = sel ? ColSelectedBorder : ColCardBorder;
            int w = sel ? 2 : 1;
            c.Style.BorderWidthLeft = w;
            c.Style.BorderWidthRight = w;
            c.Style.BorderWidthTop = w;
            c.Style.BorderWidthBottom = w;
            c.Style.BgColor = sel ? new Color(0.14f, 0.17f, 0.22f, 0.95f) : ColCardBg;
        }
    }

    // ———————————————————————————— 配色 ————————————————————————————

    /// <summary>血量条配色：高绿→低红（与 CharacterPanel HP 条同梯度）。</summary>
    private static Color BloodColor(float ratio)
    {
        return ratio > 0.5f
            ? new Color(Mathf.Lerp(0.85f, 0.35f, (ratio - 0.5f) * 2f), 0.78f, 0.28f)
            : new Color(0.85f, Mathf.Lerp(0.25f, 0.78f, ratio * 2f), 0.22f);
    }

    /// <summary>饥饿配色：正常/吃撑柔绿，逐级向红加深（复刻 CharacterPanel.HungerColor 口径）。</summary>
    private static Color HungerColor(int stage) => stage switch
    {
        <= 0 => new Color(0.92f, 0.27f, 0.22f),
        1 => new Color(0.93f, 0.35f, 0.20f),
        2 => new Color(0.95f, 0.55f, 0.22f),
        3 => new Color(0.92f, 0.75f, 0.30f),
        4 => new Color(0.80f, 0.85f, 0.40f),
        _ => new Color(0.55f, 0.82f, 0.45f),
    };
}
