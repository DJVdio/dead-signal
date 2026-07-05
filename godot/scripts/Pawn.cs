using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 幸存者：完全由玩家指令驱动（左键选中、右键移动/攻击），无自主 AI。
/// 两名幸存者一个持手枪（中距离）、一个持匕首（近战），通过工厂参数区分。
/// </summary>
public sealed partial class Pawn : Actor
{
    public string DisplayName { get; private set; } = "幸存者";

    public static Pawn Create(string name, bool usePistol, Color color)
    {
        var p = new Pawn
        {
            DisplayName = name,
            BodyColor = color,
        };
        p.Faction = Faction.Survivor;
        p.Radius = 12f;
        p.MoveSpeed = 95f;
        p.Body = CombatData.NewHumanoidBody();
        p.DefenderArmor = CombatData.SurvivorArmor();

        if (usePistol)
        {
            p.AttackWeapon = CombatData.Pistol();
            p.AttackRange = 260f;   // 中距离
            p.AttackCooldown = 1.1;
            p.IsRanged = true;      // 锥形散布弹道（误差角来自武器 BaseSpreadDegrees）
        }
        else
        {
            p.AttackWeapon = CombatData.Dagger();
            p.AttackRange = 26f;    // 近战
            p.AttackCooldown = 0.7;
        }
        return p;
    }
}
