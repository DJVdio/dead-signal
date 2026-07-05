namespace DeadSignal.Combat;

/// <summary>
/// 可注入的随机源。战斗结算的每一次 roll 都经此接口，
/// 保证测试可用固定序列复现文档算例。
/// </summary>
public interface IRandomSource
{
    /// <summary>返回 [min, max] 连续均匀分布上的一个小数（min==max 时返回 min）。</summary>
    double Range(double min, double max);
}

/// <summary>生产用随机源，包裹 System.Random。</summary>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _rng;

    public SystemRandomSource(int? seed = null)
    {
        _rng = seed is int s ? new Random(s) : new Random();
    }

    public double Range(double min, double max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + _rng.NextDouble() * (max - min);
    }
}

/// <summary>
/// 测试用随机源：按入队顺序返回预设值，并校验落在 [min, max] 内（容差 1e-9）。
/// 越界或耗尽都抛异常，用于捕获算例写错或结算调用顺序变化。
/// </summary>
public sealed class SequenceRandomSource : IRandomSource
{
    private readonly Queue<double> _values;
    private const double Tolerance = 1e-9;

    public SequenceRandomSource(params double[] values)
    {
        _values = new Queue<double>(values);
    }

    public int Remaining => _values.Count;

    public double Range(double min, double max)
    {
        if (_values.Count == 0)
        {
            throw new InvalidOperationException(
                "SequenceRandomSource 已耗尽：结算请求的 roll 次数多于预设序列。");
        }

        double v = _values.Dequeue();
        if (v < min - Tolerance || v > max + Tolerance)
        {
            throw new InvalidOperationException(
                $"SequenceRandomSource 值 {v} 越界 [{min}, {max}]，算例与结算区间不一致。");
        }

        return v;
    }
}
