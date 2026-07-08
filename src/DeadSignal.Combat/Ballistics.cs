namespace DeadSignal.Combat;

/// <summary>
/// 远程弹道误差角采样（纯函数）。用户口径：没有命中率——远程向一个锥形范围内随机一个方向射击，
/// 锥角越小越准。俯视角 2D 下，锥即以准星方向为中轴、半角为误差角的角度扇区。
/// 弹道飞行与碰撞判定归 Godot 实时层，本类只负责"从准星方向偏转多少度"。
/// </summary>
public static class Ballistics
{
    /// <summary>
    /// 在 [−spread, +spread] 度内均匀采样一个偏转角（相对准星方向）。
    /// spread=0 → 恒为 0（正前方，绝对精准）。spread 越大越发散。
    /// </summary>
    public static double SampleDeflectionDegrees(double spreadDegrees, IRandomSource rng)
    {
        double s = Math.Abs(spreadDegrees);
        if (s == 0)
        {
            return 0;
        }

        return rng.Range(-s, s);
    }

    /// <summary>把准星方向（度）叠加一次采样偏转，返回实际射击方向（度）。</summary>
    public static double SampleShotDirectionDegrees(double aimDegrees, double spreadDegrees, IRandomSource rng) =>
        aimDegrees + SampleDeflectionDegrees(spreadDegrees, rng);

    /// <summary>
    /// 射程内伤害衰减系数 [0,1]（纯函数，给 Godot 空间层调）。用户口径：
    /// distance ≤ <see cref="Weapon.FalloffStart"/> → 1（满伤）；
    /// (FalloffStart, MaxRange] → 从 1 线性降到 <see cref="Weapon.FalloffFloor"/>；
    /// > <see cref="Weapon.MaxRange"/> → 0（不可开火）。
    /// 无射程模型（近战/未设 MaxRange）→ 恒 1（不衰减、无射程约束）。
    /// </summary>
    public static double RangedDamageFactor(double distance, Weapon weapon)
    {
        // 无射程模型：不做任何衰减/截断（近战武器、或远程未填 MaxRange）。
        if (weapon.MaxRange is not double maxRange || maxRange <= 0)
        {
            return 1.0;
        }

        if (distance > maxRange)
        {
            return 0.0; // 超出射程：不可开火
        }

        double start = weapon.FalloffStart ?? 0;
        double floor = Math.Clamp(weapon.FalloffFloor ?? 1.0, 0, 1);

        // 满伤段，或退化配置（衰减段长度<=0）→ 恒满伤。
        if (distance <= start || maxRange <= start)
        {
            return 1.0;
        }

        double t = (distance - start) / (maxRange - start); // 0..1
        return 1.0 - t * (1.0 - floor);                     // 1 → floor 线性
    }

    /// <summary>该距离是否在武器射程内（可开火）。无射程模型的武器（近战/未设 MaxRange）恒真。</summary>
    public static bool InRange(double distance, Weapon weapon) =>
        weapon.MaxRange is not double maxRange || maxRange <= 0 || distance <= maxRange;
}
