namespace DeadSignal.Combat;

/// <summary>
/// 部位命中分配接口。命中部位按体积权重随机分配；瞄准指令通过 aimWeights 覆盖/放大权重。
/// 现有实现为体积加权随机（<see cref="VolumeWeightedHitSelector"/>，两级采样）。
/// 细部位表<b>已填齐</b>（<see cref="HumanBody"/>：眼/耳/胸腹/大腿小腿/手指/脚趾），采样逻辑无须再改。
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

/// <summary>
/// 体积加权随机命中选择器（两级采样）。先按"大区域"体积权重选区域（区域权重 = 成员子部位有效权重之和），
/// 再在区域内按子部位体积权重选子部位。两级加权在边际分布上等价于单级扁平加权，但保留"区域→子部位"结构，
/// 供手指/耳等细部位随部位树扩展而无须改采样逻辑。
/// 仅有一个大区域时跳过第一级 roll（退化为单级），保持既有单区域用例的 roll 消耗不变。
/// </summary>
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

        // 第一级：按首次出现顺序分组到大区域（顺序稳定，供序列随机源复现）。
        var regionOrder = new List<BodyMacroRegion>();
        var byRegion = new Dictionary<BodyMacroRegion, List<BodyPart>>();
        foreach (var p in parts)
        {
            if (!byRegion.TryGetValue(p.MacroRegion, out var list))
            {
                list = new List<BodyPart>();
                byRegion[p.MacroRegion] = list;
                regionOrder.Add(p.MacroRegion);
            }

            list.Add(p);
        }

        List<BodyPart> regionParts;
        if (regionOrder.Count == 1)
        {
            // 单区域：跳过第一级 roll，退化为单级（与既有用例 roll 消耗一致）。
            regionParts = byRegion[regionOrder[0]];
        }
        else
        {
            double totalRegion = 0;
            var regionWeight = new double[regionOrder.Count];
            for (int i = 0; i < regionOrder.Count; i++)
            {
                double w = 0;
                foreach (var p in byRegion[regionOrder[i]])
                {
                    w += WeightOf(p, aimWeights);
                }

                regionWeight[i] = w;
                totalRegion += w;
            }

            if (totalRegion <= 0)
            {
                throw new ArgumentException("候选部位权重总和为 0。", nameof(parts));
            }

            regionParts = byRegion[regionOrder[PickIndex(regionWeight, totalRegion)]];
        }

        // 第二级：区域内按子部位体积权重选子部位。
        return SelectWithinRegion(regionParts, aimWeights);
    }

    /// <summary>在单个大区域内按子部位体积权重加权抽取（区域内单一子部位时不消耗 roll）。</summary>
    private BodyPart SelectWithinRegion(IReadOnlyList<BodyPart> parts, IReadOnlyDictionary<string, double>? aimWeights)
    {
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

    /// <summary>按累加权重抽取索引（含浮点边界兜底）。</summary>
    private int PickIndex(double[] weights, double total)
    {
        double pick = _rng.Range(0, total);
        double acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (pick < acc)
            {
                return i;
            }
        }

        return weights.Length - 1;
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
