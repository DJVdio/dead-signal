using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 神秘商人 NPC：中立、不参战、不可指派的到访者。从地图边缘走到营地约定停留点，停留期间可被右键前往交互
/// 打开交易面板，夜幕前走出画面离开。<see cref="Faction.Neutral"/> 保证谁都不打它、它也不择敌
/// （<see cref="Factions.IsHostile"/> 对中立恒 false）；不给 target provider、不 override <see cref="Actor.Think"/>，
/// 故无攻击/追击 AI，只按外部指令 <see cref="Actor.CommandMoveTo"/> 走位。
/// </summary>
public sealed partial class Merchant : Actor
{
    /// <summary>战斗日志/检视显示名。</summary>
    public string DisplayName { get; private set; } = "神秘商人";

    /// <summary>
    /// 工厂：仿 <see cref="Raider.Create"/> 风格。中立、无武器、无择敌池 → 纯 idle NPC。
    /// 给一具 Body 只为满足 Actor 的健康/存活契约（它不会被攻击，Neutral 不被择敌）。
    /// 独特配色（青灰）以便与幸存者/丧尸/劫掠者一眼区分；蓝边描边由 outline 消费 Faction.Neutral。
    /// </summary>
    public static Merchant Create(string displayName = "神秘商人")
    {
        var m = new Merchant
        {
            DisplayName = displayName,
            BodyColor = new Color(0.35f, 0.55f, 0.68f), // 青灰：中立商人独特色
        };
        m.Faction = Faction.Neutral;
        m.Radius = 12f;
        m.MoveSpeed = 78f; // 慢悠悠的行脚商人
        m.Body = CombatData.NewHumanoidBody();
        return m;
    }
}
