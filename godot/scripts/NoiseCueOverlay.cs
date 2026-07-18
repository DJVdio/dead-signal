using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 实时噪音可视化：在发声点画一圈短时等距半径环。
///
/// <para>环只告诉玩家“这一下大约会传多远”，不替代 <see cref="NoiseLogic"/> 的唤醒判定，
/// 也不把敌方是否实际听见泄露成信息。脚步环更短更淡，战斗/开门等环更醒目。</para>
/// </summary>
public sealed partial class NoiseCueOverlay : Node2D
{
    private const int OverlayZIndex = 4070;
    private const double CombatLifetime = 0.85;
    private const double FootstepLifetime = 0.45;
    private const int SegmentCount = 32;

    private sealed class Entry
    {
        public NoiseCue Cue;
        public double Age;
    }

    private readonly List<Entry> _entries = new();

    public override void _Ready()
    {
        ZIndex = OverlayZIndex;
        TopLevel = true;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>从任意发声者节点定位到场景的等距根，懒创建唯一 overlay 并追加一圈提示。</summary>
    public static void Emit(Node source, Vector2 cartOrigin, double radius, NoiseKind kind, RatNoiseSource ratSource)
    {
        NoiseCue cue = new(new System.Numerics.Vector2(cartOrigin.X, cartOrigin.Y), radius, kind, ratSource);
        NoiseCueFeed.Publish(cue);

        if (!GodotObject.IsInstanceValid(source) || source.GetTree() is not SceneTree tree)
        {
            return;
        }

        Node host = source;
        if (tree.GetFirstNodeInGroup("iso_layer") is Node isoLayer && isoLayer.GetParent() is Node sceneRoot)
        {
            // IsoLayer 会随营地视图开关隐藏；overlay 挂在其父层并 TopLevel，避免跟着隐藏。
            host = sceneRoot;
        }

        NoiseCueOverlay? overlay = host.GetNodeOrNull<NoiseCueOverlay>("NoiseCueOverlay");
        if (overlay is null)
        {
            overlay = new NoiseCueOverlay { Name = "NoiseCueOverlay" };
            host.AddChild(overlay);
        }
        overlay.AddCue(cue);
    }

    private void AddCue(NoiseCue cue)
    {
        if (cue.Radius <= 0.0)
        {
            return;
        }

        _entries.Add(new Entry { Cue = cue });
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            _entries[i].Age += Math.Max(0.0, delta);
            double lifetime = _entries[i].Cue.Source == RatNoiseSource.Footstep
                ? FootstepLifetime
                : CombatLifetime;
            if (_entries[i].Age >= lifetime)
            {
                _entries.RemoveAt(i);
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Entry entry in _entries)
        {
            double lifetime = entry.Cue.Source == RatNoiseSource.Footstep ? FootstepLifetime : CombatLifetime;
            float alpha = (float)Math.Clamp(1.0 - entry.Age / lifetime, 0.0, 1.0);
            Color color = entry.Cue.Kind == NoiseKind.Movement
                ? new Color(0.35f, 0.75f, 1.0f, 0.65f * alpha)
                : new Color(1.0f, 0.55f, 0.2f, 0.8f * alpha);
            DrawRing(entry.Cue, color);
        }
    }

    private static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    private void DrawRing(NoiseCue cue, Color color)
    {
        var points = new Vector2[SegmentCount + 1];
        for (int i = 0; i <= SegmentCount; i++)
        {
            double radians = Math.PI * 2.0 * i / SegmentCount;
            float x = (float)(cue.Origin.X + Math.Cos(radians) * cue.Radius);
            float y = (float)(cue.Origin.Y + Math.Sin(radians) * cue.Radius);
            points[i] = Iso.Project(x, y);
        }

        // 一条很细的暗边让环在白天/夜间遮暗下都可读。
        DrawPolyline(points, WithAlpha(Colors.Black, color.A * 0.55f), 3.0f, true);
        DrawPolyline(points, color, 1.35f, true);
    }
}
