namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 阵营模型 + 敌对矩阵；空间层（Actor 碰撞层/弹道）消费 IsHostile，自己不做 faction 相等的散点判断。

/// <summary>
/// 战斗阵营。<see cref="Survivor"/> 玩家营地、<see cref="Zombie"/> 丧尸、<see cref="Raider"/> 人类劫掠者第三方、
/// <see cref="Neutral"/> 中立方（如神秘商人：不参战、与任何阵营都不敌对）。
/// 敌我关系由 <see cref="Factions.IsHostile"/> 统一裁定（见其矩阵），不要在别处散写 faction 相等比较。
/// </summary>
public enum Faction
{
    Survivor,
    Zombie,
    Raider,

    /// <summary>中立方（神秘商人等）：与所有阵营都不敌对，谁都不打它、它也不打谁。</summary>
    Neutral,
}

/// <summary>
/// 敌对矩阵（用户拍板）。口径：Survivor↔Zombie、Survivor↔Raider、Zombie↔Raider **均敌对**
/// （丧尸也扑咬劫掠者）；同阵营不敌对；<see cref="Faction.Neutral"/> 与任何阵营（含彼此）**都不敌对**
/// （中立商人不参战、不被择敌）。集中在此以便日后引入结盟关系时只改一处。
/// </summary>
public static class Factions
{
    /// <summary>
    /// 两阵营是否敌对（对称；同阵营恒 false）。命中/友伤/锁敌一律走此判据，勿散写相等比较。
    /// 中立方（<see cref="Faction.Neutral"/>）对谁都不敌对——先短路，再对其余三方走"阵营不同即敌对"。
    /// </summary>
    public static bool IsHostile(Faction a, Faction b)
    {
        if (a == Faction.Neutral || b == Faction.Neutral)
        {
            return false;
        }

        return a != b;
    }
}
