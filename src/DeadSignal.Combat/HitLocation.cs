namespace DeadSignal.Combat;

/// <summary>
/// 部位命中分配接口。命中部位按体积权重随机分配；瞄准指令通过 aimWeights 覆盖/放大权重。
/// 本期实现体积加权随机；细部位表后续填数据即可复用。
/// </summary>
public interface IHitLocationSelector
{
    /// <summary>
    /// 从候选部位中选出命中部位。
    /// </summary>
    /// <param name="parts">候选部位（体积权重 &gt; 0）。</param>
    /// <param name="aimWeights">
    /// 可选瞄准权重覆盖：键为部位名，值为该部位的替换权重。null 表示纯体积加权。
    /// </param>
    BodyPart Select(IReadOnlyList<BodyPart> parts, IReadOnlyDictionary<string, double>? aimWeights = null);
}

/// <summary>体积加权随机命中选择器。</summary>
public sealed class VolumeWeightedHitSelector : IHitLocationSelector
{
    private readonly IRandomSource _rng;

    public VolumeWeightedHitSelector(IRandomSource rng)
    {
        _rng = rng;
    }

    public BodyPart Select(IReadOnlyList<BodyPart> parts, IReadOnlyDictionary<string, double>? aimWeights = null)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("候选部位为空。", nameof(parts));
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        double total = 0;
        foreach (var p in parts)
        {
            total += WeightOf(p, aimWeights);
        }

        if (total <= 0)
        {
            throw new ArgumentException("候选部位权重总和为 0。", nameof(parts));
        }

        double pick = _rng.Range(0, total);
        double acc = 0;
        foreach (var p in parts)
        {
            acc += WeightOf(p, aimWeights);
            if (pick < acc)
            {
                return p;
            }
        }

        // 浮点累加边界兜底。
        return parts[^1];
    }

    private static double WeightOf(BodyPart p, IReadOnlyDictionary<string, double>? aimWeights)
    {
        if (aimWeights is not null && aimWeights.TryGetValue(p.Name, out double w))
        {
            return w;
        }

        return p.VolumeWeight;
    }
}
