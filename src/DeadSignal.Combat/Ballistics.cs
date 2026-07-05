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
}
