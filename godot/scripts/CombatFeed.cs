using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 战斗表现总线：战斗结算层（<see cref="Actor.ReceiveAttack"/>）单点<b>发布</b>每次命中结果，
/// 表现层（飘字①、受击视觉②、战斗日志③、状态图标④）各自<b>订阅</b>、互不耦合。
///
/// —— 生命周期约定（订阅方务必遵守，否则跨场景 stale ref / 内存泄漏）——
/// 本总线是 <c>static event</c>：只要还挂着，委托就长期持有订阅者引用。
///   • 凡订阅者是 Godot 场景节点（②③④ 大多如此）：必须在 <c>_ExitTree</c>（或节点销毁前）
///     执行 <c>CombatFeed.Published -= Handler</c> 退订，避免总线抓着已 QueueFree 的死节点。
///   • 无状态的纯静态订阅者（如本文件内置的飘字 presenter）不持有场景状态、每次用事件里传入的
///     live source，天然安全，可常驻不退订。
///   • 场景整体重载时可调 <see cref="Reset"/> 一次性清空全部订阅（安全阀，兜底忘记退订的情况）。
///
/// 选型说明：未用 autoload 节点（那需改 project.godot、且当前唯一落地订阅者——飘字——本就无状态），
/// static event + 内置无状态 presenter 足够，且不越过本单文件边界；②③④ 接入时按上面约定退订即可。
/// </summary>
public static class CombatFeed
{
    /// <summary>一次命中的表现载荷。用 struct 便于日后加字段而不破坏订阅者签名。</summary>
    public readonly struct Event
    {
        /// <summary>发起攻击的一方（攻击者）。可能为 <c>null</c>（环境伤害/无源）。战斗日志用它做归属。</summary>
        public readonly Actor? Attacker;
        /// <summary>被击中的一方（承伤者）。表现层由它取世界坐标/父节点/阵营。</summary>
        public readonly Actor Target;
        public readonly AttackOutcome Hit;

        public Event(Actor? attacker, Actor target, AttackOutcome hit)
        {
            Attacker = attacker;
            Target = target;
            Hit = hit;
        }
    }

    /// <summary>每次命中结算后触发。订阅方注意上文生命周期约定。</summary>
    public static event Action<Event>? Published;

    /// <summary>
    /// 半身掩体整发判无效的载荷（**没有 <see cref="AttackOutcome"/>**——这一下没有伤害发生，
    /// 不结算部位/护甲/效果，故没有可摊平的结果）。用户口径：这一下**射中了人，但判定无效**，
    /// 不是落空、也不是被甲挡下，所以走独立事件而非塞进 <see cref="Event"/>。
    /// </summary>
    public readonly struct CoverEvent
    {
        /// <summary>开枪的一方（环境伤害/无源为 null——无源时掩体门本就不生效，此处恒非 null）。</summary>
        public readonly Actor? Attacker;
        /// <summary>躲在掩体后、白挨了这一枪的一方。</summary>
        public readonly Actor Target;

        public CoverEvent(Actor? attacker, Actor target)
        {
            Attacker = attacker;
            Target = target;
        }
    }

    /// <summary>远程命中被半身掩体判无效时触发（<c>Actor.ReceiveAttack</c> 单点发布）。订阅方注意上文生命周期约定。</summary>
    public static event Action<CoverEvent>? CoverNegated;

    static CombatFeed()
    {
        // 内置飘字 presenter：无状态、常驻，把结果落成上飘战报文字。
        Published += ShowFloatingText;
        CoverNegated += ShowCoverText;
    }

    /// <summary>发布一次命中结果。<paramref name="attacker"/> 为攻击方（可 <c>null</c>），<paramref name="target"/> 为承伤方。</summary>
    public static void Publish(Actor? attacker, Actor target, AttackOutcome hit)
    {
        Published?.Invoke(new Event(attacker, target, hit));
    }

    /// <summary>远程命中被半身掩体判无效时发布（不结算伤害，只出表现）。</summary>
    public static void PublishCoverNegated(Actor? attacker, Actor target)
    {
        CoverNegated?.Invoke(new CoverEvent(attacker, target));
    }

    /// <summary>安全阀：场景整体重载时清空全部订阅，防止残留死节点引用。清空后重挂内置 presenter。</summary>
    public static void Reset()
    {
        Published = null;
        Published += ShowFloatingText;
        CoverNegated = null;
        CoverNegated += ShowCoverText;
    }

    /// <summary>
    /// 内置掩体飘字 presenter：出一句"掩体挡下"（中性青）。
    /// 表达的是<b>打中了但没伤到</b>——与被甲挡下的"叮·X挡下"（灰）、伤害的钝黄/锐红都区分开。
    /// </summary>
    private static void ShowCoverText(CoverEvent e)
    {
        Actor target = e.Target;
        if (!GodotObject.IsInstanceValid(target))
        {
            return;
        }

        MoteText mote = CombatMoteText.BuildCoverNegated();
        var color = new Color(mote.Color.R, mote.Color.G, mote.Color.B);
        FloatingText.Spawn(
            target.GetParent(),
            target.GlobalPosition + new Vector2(0, -target.Radius - 10),
            mote.Text,
            color);
        CombatVfxBurst.SpawnImpact(target.PresentationLayer,
            Iso.Project(target.GlobalPosition) + new Vector2(0f, -target.Radius * 2.2f),
            ImpactVfxKind.Wall, 0.85f);
    }

    /// <summary>内置飘字 presenter：吃 <see cref="CombatMoteText"/> 的中性串+色，落成 <see cref="FloatingText"/>。</summary>
    private static void ShowFloatingText(Event e)
    {
        Actor target = e.Target;
        // 承伤方可能已在本帧销毁（如致死）——守一手，避免访问死节点。
        if (!GodotObject.IsInstanceValid(target))
        {
            return;
        }

        MoteText mote = CombatMoteText.Build(e.Hit);
        var color = new Color(mote.Color.R, mote.Color.G, mote.Color.B);
        FloatingText.Spawn(
            target.GetParent(),
            target.GlobalPosition + new Vector2(0, -target.Radius - 10),
            mote.Text,
            color);
    }
}
