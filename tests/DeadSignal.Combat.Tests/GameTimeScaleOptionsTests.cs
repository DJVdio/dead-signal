using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class GameTimeScaleOptionsTests
{
    [Fact]
    public void 速度档恰为三档()
    {
        Assert.Equal(3, GameTimeScaleOptions.Speeds.Length);
    }

    [Fact]
    public void 速度档为一倍二倍三倍()
    {
        Assert.Equal(1.0, GameTimeScaleOptions.Speeds[0]);
        Assert.Equal(2.0, GameTimeScaleOptions.Speeds[1]);
        Assert.Equal(3.0, GameTimeScaleOptions.Speeds[2]);
    }

    [Fact]
    public void 最大索引为2()
    {
        Assert.Equal(2, GameTimeScaleOptions.MaxIndex);
    }

    [Fact]
    public void SpeedAt_返回正确速度值()
    {
        Assert.Equal(1.0, GameTimeScaleOptions.SpeedAt(0));
        Assert.Equal(2.0, GameTimeScaleOptions.SpeedAt(1));
        Assert.Equal(3.0, GameTimeScaleOptions.SpeedAt(2));
    }

    [Fact]
    public void SpeedAt_越界索引被钳制()
    {
        Assert.Equal(1.0, GameTimeScaleOptions.SpeedAt(-1));
        Assert.Equal(3.0, GameTimeScaleOptions.SpeedAt(99));
    }

    [Fact]
    public void LabelAt_返回正确标签()
    {
        Assert.Equal("1x", GameTimeScaleOptions.LabelAt(0));
        Assert.Equal("2x", GameTimeScaleOptions.LabelAt(1));
        Assert.Equal("3x", GameTimeScaleOptions.LabelAt(2));
    }

    [Fact]
    public void PausedLabel_暂停时返回暂停()
    {
        Assert.Equal("暂停", GameTimeScaleOptions.PausedLabel(0, true));
        Assert.Equal("暂停", GameTimeScaleOptions.PausedLabel(1, true));
    }

    [Fact]
    public void PausedLabel_非暂停返回速度标签()
    {
        Assert.Equal("1x", GameTimeScaleOptions.PausedLabel(0, false));
        Assert.Equal("2x", GameTimeScaleOptions.PausedLabel(1, false));
        Assert.Equal("3x", GameTimeScaleOptions.PausedLabel(2, false));
    }

    [Fact]
    public void DayTravel固定8倍_未体现在玩家速度档中()
    {
        // DayTravel 的固定 8.0 是独立表现，不由 GameTimeScaleOptions 承载
        // 本测试确认玩家档不含 8
        for (int i = 0; i < GameTimeScaleOptions.Speeds.Length; i++)
            Assert.NotEqual(8.0, GameTimeScaleOptions.Speeds[i]);
    }
}
