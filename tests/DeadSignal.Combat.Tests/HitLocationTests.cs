using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class HitLocationTests
{
    [Fact]
    public void SinglePart_AlwaysReturnsIt()
    {
        var parts = new[] { new BodyPart { Name = "胸部", VolumeWeight = 40 } };
        var sel = new VolumeWeightedHitSelector(new SequenceRandomSource(/* 不消耗 */));
        Assert.Equal("胸部", sel.Select(parts).Name);
    }

    [Fact]
    public void VolumeWeighted_PicksByCumulativeWeight()
    {
        var parts = new[]
        {
            new BodyPart { Name = "头", VolumeWeight = 30 },
            new BodyPart { Name = "胸", VolumeWeight = 70 },
        };
        // 总权重 100，pick=50 落在累加区间 [30,100) → 胸
        var sel = new VolumeWeightedHitSelector(new SequenceRandomSource(50));
        Assert.Equal("胸", sel.Select(parts).Name);

        // pick=10 落在 [0,30) → 头
        var sel2 = new VolumeWeightedHitSelector(new SequenceRandomSource(10));
        Assert.Equal("头", sel2.Select(parts).Name);
    }

    [Fact]
    public void AimWeights_OverrideVolumeWeight()
    {
        var parts = new[]
        {
            new BodyPart { Name = "头", VolumeWeight = 5 },
            new BodyPart { Name = "胸", VolumeWeight = 40 },
        };
        // 瞄准头：把头权重抬到 95，总权重 95+40=135，pick=50 → 头
        var aim = new Dictionary<string, double> { ["头"] = 95 };
        var sel = new VolumeWeightedHitSelector(new SequenceRandomSource(50));
        Assert.Equal("头", sel.Select(parts, aim).Name);
    }
}
