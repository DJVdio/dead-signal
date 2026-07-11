using System;
using System.Collections.Generic;
using System.Text;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 一名 <see cref="Actor"/> 头顶的状态图标条：把该 Actor 当前<b>引擎真实存在</b>的状态
/// （失血 / 骨折 / 切除 / 失能 / 损毁 / 昏迷 / 死亡）显示成一排彩色字符标记（原型占位，非美术）。
///
/// 与 <see cref="ActorSprite"/> 同构：本节点挂在 iso_layer（YSortEnabled）下、<b>不是</b> Actor 的子节点
/// —— Actor 本体在不可见 LogicLayer 里，直接做它的子节点会跟着不可见。故每帧把 actor 的 cartesian
/// 逻辑位置投到 iso 屏幕坐标（<c>Iso.Project</c>）后定位到其头顶（血条之上）。
///
/// 状态<b>只读</b>自 <see cref="PawnInspection"/> 快照（构造它的委托由 Actor 就地捕获自身受保护的
/// Body/武器/护甲）——永远拿不到、也改不坏可变的战斗对象。刷新时机：订阅 <see cref="CombatFeed.Published"/>
/// 在命中承伤方后即时刷新，另加低频轮询兜底（失血随时间推进、无命中也会变档）。
///
/// 生命周期（遵 <see cref="CombatFeed"/> 硬约定）：static event 长期持有订阅者，本节点在 <see cref="_ExitTree"/>
/// 退订，避免总线抓着已 QueueFree 的死节点；一切访问 actor 前先 <see cref="GodotObject.IsInstanceValid"/> 守一手。
/// </summary>
public sealed partial class StatusIconStrip : Node2D
{
    private Actor _actor = null!;
    private Func<PawnInspection> _snapshot = null!;
    private bool _bound;

    private double _pollTimer;
    private bool _dirty = true;
    private string _lastSignature = "";

    /// <summary>低频轮询间隔（秒）：失血等随时间变的状态无命中事件也能刷新。</summary>
    private const double PollInterval = 0.5;

    /// <summary>一个状态标记：显示字符 + 颜色（引擎真实状态的占位可视化）。</summary>
    private readonly struct Mark
    {
        public readonly string Glyph;
        public readonly Color Color;

        public Mark(string glyph, Color color)
        {
            Glyph = glyph;
            Color = color;
        }
    }

    /// <summary>绑定所表现的 Actor 与只读快照工厂（由 Actor 就地捕获自身 Body 构造）。</summary>
    public void Bind(Actor actor, Func<PawnInspection> snapshot)
    {
        _actor = actor;
        _snapshot = snapshot;
        _bound = true;
        ZIndex = 1; // 压在人形（ZIndex 0）之上、同 Y 时不被盖住
        SyncPosition();
        CombatFeed.Published += OnCombatEvent;
    }

    public override void _ExitTree()
    {
        // static event 硬约定：务必退订，否则总线长期抓着本（可能已销毁的）节点。
        CombatFeed.Published -= OnCombatEvent;
    }

    /// <summary>承伤方是本 Actor 时标脏——命中可能改了其状态，下帧重建标记。</summary>
    private void OnCombatEvent(CombatFeed.Event e)
    {
        if (_bound && GodotObject.IsInstanceValid(_actor) && e.Target == _actor)
        {
            _dirty = true;
        }
    }

    public override void _Process(double delta)
    {
        if (!_bound)
        {
            return;
        }
        // Actor 死亡即 QueueFree 自身、或将被回收——同步销毁本条，避免悬挂（同 ActorSprite）。
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.Alive)
        {
            QueueFree();
            return;
        }

        SyncPosition();

        _pollTimer += delta;
        if (_pollTimer >= PollInterval)
        {
            _pollTimer = 0;
            _dirty = true; // 兜底轮询：无命中事件时也重扫一次
        }

        if (_dirty)
        {
            _dirty = false;
            Rebuild();
        }
    }

    private void SyncPosition() => Position = Iso.Project(_actor.GlobalPosition);

    /// <summary>重扫快照 → 只在标记集合变化时才重建 Label 子节点（避免每帧/每 0.5s 空转造节点）。</summary>
    private void Rebuild()
    {
        List<Mark> marks = CollectMarks(_snapshot());

        // 用签名判等：集合没变就不重建（否则轮询会无谓重造 Label）。
        var sig = new StringBuilder();
        foreach (Mark m in marks)
        {
            sig.Append(m.Glyph);
        }
        string signature = sig.ToString();
        if (signature == _lastSignature)
        {
            return;
        }
        _lastSignature = signature;

        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        // 头顶（血条之上）水平居中排一排。血条约在 y ≈ -r*3.4-8（见 ActorSprite），再抬一档。
        const float step = 15f;
        float topY = -_actor.Radius * 3.4f - 22f;
        float startX = -(marks.Count - 1) * step * 0.5f;
        for (int i = 0; i < marks.Count; i++)
        {
            AddChild(BuildChip(marks[i], new Vector2(startX + i * step, topY)));
        }
    }

    /// <summary>把只读快照翻成一排状态标记；<b>只收引擎真实存在的状态</b>，不发明任何效果。</summary>
    private static List<Mark> CollectMarks(PawnInspection insp)
    {
        var marks = new List<Mark>();

        if (insp.IsDead)
        {
            // 死亡通常一瞬即 QueueFree（Actor.Die），此处仅为健壮兜底。
            marks.Add(new Mark("亡", new Color(0.85f, 0.85f, 0.9f)));
            return marks; // 死了其余状态无意义
        }

        // 逐部位真实标记聚合（切除/损毁/骨折/失能/流血皆来自 Body 的对应判定）。
        bool severed = false, destroyed = false, fractured = false, disabled = false, bleeding = false;
        foreach (PartStatus p in insp.Parts)
        {
            severed |= p.IsSevered;
            destroyed |= p.IsDestroyed;
            fractured |= p.IsFractured;
            disabled |= p.IsDisabled;
            bleeding |= p.IsBleeding;
        }

        // 失血：整体血量分档 > None，或仍有活动出血伤口（血量尚 ≥75% 但正在流血）。按档加深颜色。
        if (insp.BloodTier != BloodLossTier.None || bleeding)
        {
            marks.Add(new Mark("血", BloodColor(insp.BloodTier)));
        }
        if (fractured)
        {
            marks.Add(new Mark("骨", new Color(0.95f, 0.75f, 0.3f)));   // 骨折
        }
        if (severed)
        {
            marks.Add(new Mark("断", new Color(0.9f, 0.25f, 0.2f)));    // 切除
        }
        if (destroyed)
        {
            marks.Add(new Mark("毁", new Color(0.6f, 0.15f, 0.15f)));   // 损毁
        }
        if (disabled)
        {
            marks.Add(new Mark("废", new Color(0.6f, 0.62f, 0.68f)));   // 失能
        }
        if (insp.HasInfection)
        {
            marks.Add(new Mark("疫", InfectionColor(insp.MaxInfectionSeverity))); // 感染（坏疽/败血症前兆）——按严重度加深
        }
        if (insp.IsUnconscious)
        {
            marks.Add(new Mark("昏", new Color(0.45f, 0.55f, 0.95f)));  // 昏迷
        }

        return marks;
    }

    /// <summary>感染标记按严重度着色：越接近封顶（坏疽/败血症）越红，早期偏黄警示。</summary>
    private static Color InfectionColor(double severity) => severity switch
    {
        >= 0.66 => new Color(0.80f, 0.15f, 0.55f), // 危重：品红（区别于失血纯红）
        >= 0.33 => new Color(0.90f, 0.45f, 0.35f), // 中度：橙红
        _ => new Color(0.85f, 0.72f, 0.30f),       // 早期：警示黄
    };

    /// <summary>失血标记按分档着色：越低越红（None 不会走到这里）。</summary>
    private static Color BloodColor(BloodLossTier tier) => tier switch
    {
        BloodLossTier.Severe => new Color(0.85f, 0.1f, 0.1f),
        BloodLossTier.Moderate => new Color(0.9f, 0.3f, 0.3f),
        _ => new Color(0.9f, 0.5f, 0.45f), // Mild / 仅有出血伤口
    };

    /// <summary>一枚字符 chip：沿用 FloatingText 的做法（Label + 深色描边），无需外部字体资源。</summary>
    private static Label BuildChip(Mark mark, Vector2 pos)
    {
        var label = new Label
        {
            Text = mark.Glyph,
            Modulate = mark.Color,
            Position = pos + new Vector2(-8f, 0f), // CustomMinimumSize 半宽回中
            CustomMinimumSize = new Vector2(16, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }
}
