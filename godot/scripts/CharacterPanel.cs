using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// RimWorld 风右侧检视侧栏：点选幸存者时从屏幕右侧出现的竖向面板，不遮挡世界中心。
/// 只消费只读快照 <see cref="PawnInspection"/>，永远拿不到可变的战斗对象引用。
///
/// 骨架在 <see cref="_Ready"/> 里程序化搭一次（与本工程 Hud.cs 一致的代码驱动 UI 风格），
/// 内容在 <see cref="ShowFor"/> 里按快照清空重填。挂载/点选触发由 CampMain 侧负责，本类只保证
/// 是可被 PackedScene 加载实例化的独立场景 + 下述对外 API 可用。
///
/// 挂载约定：根 Control 用锚点贴屏幕右侧，故须挂在 CanvasLayer 或铺满视口的 Control 下，
/// 锚点才会相对屏幕解析（挂在裸 Node2D 下锚点无参照）。
/// </summary>
public sealed partial class CharacterPanel : PanelContainer
{
    // —— 状态标记配色（狠辣致残是本游戏有意为之，用醒目色，不淡化）——
    private static readonly Color ColSevered = new(0.92f, 0.27f, 0.22f);   // 切除/损毁：红
    private static readonly Color ColFractured = new(0.96f, 0.62f, 0.16f); // 骨折：橙
    private static readonly Color ColBleeding = new(0.72f, 0.06f, 0.08f);  // 出血：深红
    private static readonly Color ColDisabled = new(0.56f, 0.56f, 0.58f);  // 失能：灰
    private static readonly Color ColText = new(0.93f, 0.95f, 1f);
    private static readonly Color ColMuted = new(0.66f, 0.70f, 0.78f);

    /// <summary>关闭按钮点击时 emit；由 trigger 连到业务关闭逻辑，本面板不自行决定关闭。</summary>
    [Signal]
    public delegate void CloseRequestedEventHandler();

    /// <summary>true 时在 _Ready 用一份手工快照自测布局；发布保持默认 false。</summary>
    [Export]
    private bool _debugPreview = false;

    private Label _nameLabel = null!;
    private Label _statusLabel = null!;
    private VBoxContainer _overviewBox = null!;
    private VBoxContainer _healthBox = null!;

    /// <summary>当前面板是否显示。</summary>
    public bool IsShown => Visible;

    public override void _Ready()
    {
        BuildSkeleton();
        Visible = false;

        if (_debugPreview)
        {
            ShowFor(PawnInspection.FromBody(HumanBody.NewBody(), null, null, "预览·测试对象"));
        }
    }

    /// <summary>显示面板并用快照填充；重复调用即切换到新幸存者刷新内容。</summary>
    public void ShowFor(PawnInspection insp)
    {
        _nameLabel.Text = insp.DisplayName;
        _statusLabel.Text = HealthSummary(insp);
        _statusLabel.AddThemeColorOverride("font_color", insp.IsDead ? ColSevered : ColText);

        FillOverview(insp);
        FillHealth(insp);

        Visible = true;
    }

    /// <summary>隐藏面板。</summary>
    public void HidePanel() => Visible = false;

    // ———————————————————————————— 骨架 ————————————————————————————

    private void BuildSkeleton()
    {
        // 贴屏幕右侧、全高、约占 1/3 宽的竖向侧栏。
        AnchorLeft = 0.68f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetLeft = OffsetTop = OffsetRight = OffsetBottom = 0f;
        CustomMinimumSize = new Vector2(340, 0);
        MouseFilter = MouseFilterEnum.Stop; // 吃掉侧栏区域点击，避免穿透到世界

        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.94f),
            BorderColor = new Color(0.30f, 0.34f, 0.40f, 0.9f),
            BorderWidthLeft = 2,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        AddThemeStyleboxOverride("panel", bg);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        // —— 头部：姓名 + 关闭按钮 ——
        var header = new HBoxContainer();
        root.AddChild(header);

        _nameLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _nameLabel.AddThemeFontSizeOverride("font_size", 24);
        _nameLabel.AddThemeColorOverride("font_color", ColText);
        _nameLabel.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_nameLabel);

        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(34, 30) };
        close.TooltipText = "关闭";
        close.Pressed += () => EmitSignal(SignalName.CloseRequested);
        header.AddChild(close);

        // —— 健康聚合态（常驻头部下方，一眼可见）——
        _statusLabel = new Label();
        _statusLabel.AddThemeFontSizeOverride("font_size", 15);
        _statusLabel.AddThemeColorOverride("font_color", ColText);
        root.AddChild(_statusLabel);

        root.AddChild(new HSeparator());

        // —— 概览 / 健康 两页签 ——
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(tabs);

        _overviewBox = MakeTabPage(tabs, "概览");
        _healthBox = MakeTabPage(tabs, "健康");
    }

    /// <summary>造一个带滚动的页签，返回其内容 VBox。</summary>
    private static VBoxContainer MakeTabPage(TabContainer tabs, string title)
    {
        var scroll = new ScrollContainer
        {
            Name = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        tabs.AddChild(scroll);

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);
        return box;
    }

    // ———————————————————————————— 概览页 ————————————————————————————

    private void FillOverview(PawnInspection insp)
    {
        ClearChildren(_overviewBox);

        _overviewBox.AddChild(SectionTitle("武器"));
        if (insp.Weapon is { } w)
        {
            _overviewBox.AddChild(Line(w.Name, ColText, 15));
            var kind = w.IsRanged ? "远程" : "近战";
            var hands = w.TwoHanded ? "双手" : "单手";
            _overviewBox.AddChild(Line($"伤害 {w.DamageMin:0.#}~{w.DamageMax:0.#}   穿透 {w.Penetration:0.#}", ColMuted, 13));
            _overviewBox.AddChild(Line($"{kind} · {hands} · 攻击间隔 {w.AttackInterval:0.##}s", ColMuted, 13));
        }
        else
        {
            _overviewBox.AddChild(Line("徒手（无武器）", ColMuted, 13));
        }

        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("护甲"));
        if (insp.Armor.Count == 0)
        {
            _overviewBox.AddChild(Line("无护甲", ColMuted, 13));
        }
        else
        {
            foreach (var a in insp.Armor)
            {
                _overviewBox.AddChild(Line($"{a.Name}  〔{SlotLabel(a.Slot)}〕", ColText, 14));
                _overviewBox.AddChild(Line($"利器防御 {a.SharpDefense:0.#}   钝器防御 {a.BluntDefense:0.#}", ColMuted, 13));
            }
        }
    }

    // ———————————————————————————— 健康页 ————————————————————————————

    private void FillHealth(PawnInspection insp)
    {
        ClearChildren(_healthBox);

        // 用 ParentName 重建父子层级：根 = ParentName 为 null 者（通常躯干）。
        var byParent = insp.Parts
            .GroupBy(p => p.ParentName)
            .ToDictionary(g => g.Key ?? "", g => g.ToList());
        var byName = insp.Parts.ToDictionary(p => p.Name, p => p);

        var roots = insp.Parts.Where(p => p.ParentName is null || !byName.ContainsKey(p.ParentName));
        foreach (var r in roots)
        {
            AddPartRow(r, 0, byParent);
        }
    }

    /// <summary>递归：一部位一行（缩进表层级）+ 其子部位。</summary>
    private void AddPartRow(PartStatus part, int depth, IReadOnlyDictionary<string, List<PartStatus>> byParent)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        if (depth > 0)
        {
            row.AddChild(new Control { CustomMinimumSize = new Vector2(depth * 14, 0) });
        }

        var name = new Label
        {
            Text = part.Name,
            CustomMinimumSize = new Vector2(72, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", part.IsDestroyed || part.IsSevered ? ColDisabled : ColText);
        row.AddChild(name);

        // HP 条 + 数值
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = part.MaxHp <= 0 ? 1 : part.MaxHp,
            Value = part.Hp,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(96, 16),
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HpColor(part) });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.16f, 0.17f, 0.2f) });
        row.AddChild(bar);

        var hp = new Label { Text = $"{part.Hp:0}/{part.MaxHp:0}", CustomMinimumSize = new Vector2(52, 0) };
        hp.AddThemeFontSizeOverride("font_size", 12);
        hp.AddThemeColorOverride("font_color", ColMuted);
        hp.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(hp);

        _healthBox.AddChild(row);

        // 状态短标（同一行下方的标记条）
        var marks = PartMarks(part);
        if (marks.Count > 0)
        {
            var markRow = new HBoxContainer();
            markRow.AddThemeConstantOverride("separation", 5);
            if (depth > 0)
            {
                markRow.AddChild(new Control { CustomMinimumSize = new Vector2(depth * 14, 0) });
            }
            foreach (var (text, col) in marks)
            {
                markRow.AddChild(MarkTag(text, col));
            }
            _healthBox.AddChild(markRow);
        }

        if (byParent.TryGetValue(part.Name, out var kids))
        {
            foreach (var k in kids)
            {
                AddPartRow(k, depth + 1, byParent);
            }
        }
    }

    private static List<(string, Color)> PartMarks(PartStatus p)
    {
        var marks = new List<(string, Color)>();
        if (p.IsSevered) marks.Add(("切除", ColSevered));
        if (p.IsDestroyed) marks.Add(("损毁", ColSevered));
        if (p.IsFractured) marks.Add(("骨折", ColFractured));
        if (p.IsBleeding) marks.Add(("出血", ColBleeding));
        if (p.IsDisabled) marks.Add(("失能", ColDisabled));
        return marks;
    }

    // ———————————————————————————— 小工具 ————————————————————————————

    private static string HealthSummary(PawnInspection insp)
    {
        if (insp.IsDead)
        {
            return "已死亡";
        }

        var parts = new List<string> { insp.IsUnconscious ? "昏迷" : "存活" };
        if (insp.IsFullyBlind)
        {
            parts.Add("全盲");
        }

        var blood = insp.BloodTier switch
        {
            BloodLossTier.Mild => "失血:轻度",
            BloodLossTier.Moderate => "失血:中度",
            BloodLossTier.Severe => "失血:重度",
            BloodLossTier.Dead => "失血致死",
            _ => null,
        };
        if (blood is not null)
        {
            parts.Add(blood);
        }

        return string.Join(" · ", parts) + $"　血量 {insp.BloodRatio * 100:0}%";
    }

    private static Color HpColor(PartStatus p)
    {
        if (p.IsDestroyed || p.IsSevered)
        {
            return ColSevered;
        }
        var ratio = p.MaxHp <= 0 ? 0 : Mathf.Clamp((float)(p.Hp / p.MaxHp), 0f, 1f);
        // 绿 → 黄 → 红
        return ratio > 0.5f
            ? new Color(Mathf.Lerp(0.85f, 0.35f, (ratio - 0.5f) * 2f), 0.78f, 0.28f)
            : new Color(0.85f, Mathf.Lerp(0.25f, 0.78f, ratio * 2f), 0.22f);
    }

    private static string SlotLabel(ArmorSlot slot) => slot switch
    {
        ArmorSlot.Plate => "装甲层",
        ArmorSlot.Outer => "外套层",
        ArmorSlot.Skin => "贴身层",
        _ => slot.ToString(),
    };

    private static Label SectionTitle(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 16);
        l.AddThemeColorOverride("font_color", new Color(0.62f, 0.82f, 1f));
        return l;
    }

    private static Label Line(string text, Color col, int size)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private static Label MarkTag(string text, Color col)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", new Color(0.98f, 0.98f, 0.98f));
        var box = new StyleBoxFlat
        {
            BgColor = col,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
            ContentMarginTop = 1,
            ContentMarginBottom = 1,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
        };
        l.AddThemeStyleboxOverride("normal", box);
        return l;
    }

    private static void ClearChildren(Node node)
    {
        foreach (var c in node.GetChildren())
        {
            c.QueueFree();
        }
    }
}
