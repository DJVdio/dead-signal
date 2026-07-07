using System.Collections.Generic;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 屏幕右下角的描述性战斗日志面板（E③）。订阅 <see cref="CombatFeed.Published"/>，
/// 用 <see cref="CombatLogFormatter"/> 把每次命中翻成一行中文战报，追加到滚动列表；
/// 只保留最近 <see cref="MaxLines"/> 行，越旧越淡（顶部最暗、底部最新最亮）。
///
/// 生命周期（<see cref="CombatFeed"/> 硬约定）：本面板是场景节点，static event 会长期持有其引用，
/// 故 <see cref="_ExitTree"/> 必须退订，否则跨场景 stale ref / 泄漏。承伤方取名前 <c>IsInstanceValid</c> 守一手。
/// </summary>
public sealed partial class CombatLogPanel : Control
{
    private const int MaxLines = 8;
    private const float LineWidth = 300f;

    private VBoxContainer _lines = null!;
    private readonly Queue<Label> _queue = new();

    public override void _Ready()
    {
        // 铺满 HUD 层但不吃鼠标；真正的可视框是内部 PanelContainer，钉在右下角向左上生长。
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.BottomRight);
        panel.GrowHorizontal = GrowDirection.Begin;
        panel.GrowVertical = GrowDirection.Begin;
        panel.OffsetRight = -16;
        panel.OffsetBottom = -16;
        panel.MouseFilter = MouseFilterEnum.Ignore;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.45f),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        });
        AddChild(panel);

        _lines = new VBoxContainer();
        _lines.AddThemeConstantOverride("separation", 2);
        _lines.MouseFilter = MouseFilterEnum.Ignore;
        panel.AddChild(_lines);

        CombatFeed.Published += OnCombatEvent;
    }

    public override void _ExitTree()
    {
        // static event 硬约定：退订，避免总线抓着已销毁的面板。
        CombatFeed.Published -= OnCombatEvent;
    }

    private void OnCombatEvent(CombatFeed.Event e)
    {
        Actor target = e.Target;
        // 承伤方可能已在本帧销毁（如致死）——守一手后兜底名。
        string name = GodotObject.IsInstanceValid(target) ? ResolveName(target) : "某人";
        // 攻击方可为 null（环境伤害/无源）或本帧失效——传 null，Formatter 优雅退回承伤方视角、不凭空归属。
        string? attackerName = e.Attacker is { } atk && GodotObject.IsInstanceValid(atk) ? ResolveName(atk) : null;
        AppendLine(CombatLogFormatter.Format(attackerName, name, e.Hit));
    }

    private void AppendLine(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(LineWidth, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 3);
        _lines.AddChild(label);

        _queue.Enqueue(label);
        while (_queue.Count > MaxLines)
        {
            _queue.Dequeue().QueueFree();
        }
        RefreshFade();
    }

    /// <summary>越旧越淡：顶部（最旧）最暗，底部（最新）最亮。</summary>
    private void RefreshFade()
    {
        int n = _queue.Count;
        int i = 0;
        foreach (Label l in _queue)
        {
            float t = n <= 1 ? 1f : (float)i / (n - 1);
            l.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 1f, t));
            i++;
        }
    }

    private static string ResolveName(Actor a)
    {
        if (a is Pawn p) return p.DisplayName;
        if (a is Raider r) return r.DisplayName;
        if (a.Faction == Faction.Zombie) return "丧尸";
        return string.IsNullOrEmpty(a.Name) ? "某人" : a.Name;
    }
}
