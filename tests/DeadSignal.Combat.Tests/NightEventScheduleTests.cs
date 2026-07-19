using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class NightEventScheduleTests
{
    [Fact]
    public void Roll_HumanRaidHit_ReturnsHumanRaid()
    {
        var rng = new SequenceRandomSource(0.10, 1.5);
        NightEventSchedule s = NightEventSchedule.Roll(rng);
        Assert.Equal(NightEventKind.HumanRaid, s.EventKind);
        Assert.False(s.Fired);
        Assert.InRange(s.TriggerGameHour, 0.0, NightEventSchedule.HumanRaidWindowHours);
        Assert.Equal(1.5, s.TriggerGameHour, 9);
    }

    [Fact]
    public void Roll_HumanRaidMiss_ZombieRaidHit_ReturnsZombieRaid()
    {
        var rng = new SequenceRandomSource(0.50, 0.20, 3.0);
        NightEventSchedule s = NightEventSchedule.Roll(rng);
        Assert.Equal(NightEventKind.ZombieRaid, s.EventKind);
        Assert.False(s.Fired);
        Assert.InRange(s.TriggerGameHour, 0.0, NightEventSchedule.ZombieRaidWindowHours);
        Assert.Equal(3.0, s.TriggerGameHour, 9);
    }

    [Fact]
    public void Roll_BothMiss_ReturnsNone()
    {
        var rng = new SequenceRandomSource(0.90, 0.90);
        NightEventSchedule s = NightEventSchedule.Roll(rng);
        Assert.Equal(NightEventKind.None, s.EventKind);
        Assert.False(s.Fired);
        Assert.Equal(0.0, s.TriggerGameHour);
    }

    [Fact]
    public void ShouldTrigger_AfterTriggerGameHour_ReturnsTrue()
    {
        NightEventSchedule s = NightEventSchedule.Restore(NightEventKind.HumanRaid, 1.0, false);
        Assert.True(s.ShouldTrigger(1.0));
        Assert.True(s.ShouldTrigger(2.0));
    }

    [Fact]
    public void ShouldTrigger_BeforeTriggerGameHour_ReturnsFalse()
    {
        NightEventSchedule s = NightEventSchedule.Restore(NightEventKind.ZombieRaid, 3.0, false);
        Assert.False(s.ShouldTrigger(0.0));
        Assert.False(s.ShouldTrigger(2.999));
    }

    [Fact]
    public void ShouldTrigger_NoneKind_AlwaysFalse()
    {
        NightEventSchedule s = NightEventSchedule.Restore(NightEventKind.None, 0.0, false);
        Assert.False(s.ShouldTrigger(0.0));
        Assert.False(s.ShouldTrigger(12.0));
    }

    [Fact]
    public void ShouldTrigger_AfterFired_ReturnsFalse()
    {
        NightEventSchedule s = NightEventSchedule.Restore(NightEventKind.HumanRaid, 0.5, true);
        Assert.False(s.ShouldTrigger(0.5));
        Assert.False(s.ShouldTrigger(5.0));
    }

    [Fact]
    public void WithFired_ReturnsFiredCopy()
    {
        NightEventSchedule s = NightEventSchedule.Restore(NightEventKind.ZombieRaid, 2.0, false);
        NightEventSchedule fired = s.WithFired();
        Assert.Equal(NightEventKind.ZombieRaid, fired.EventKind);
        Assert.Equal(2.0, fired.TriggerGameHour, 9);
        Assert.True(fired.Fired);
        Assert.False(s.Fired);
    }

    [Fact]
    public void Restore_RoundTrip_PreservesAllValues()
    {
        NightEventSchedule s = NightEventSchedule.Restore(NightEventKind.HumanRaid, 1.5, true);
        Assert.Equal(NightEventKind.HumanRaid, s.EventKind);
        Assert.Equal(1.5, s.TriggerGameHour, 9);
        Assert.True(s.Fired);
    }

    [Fact]
    public void None_Static_IsNone()
    {
        NightEventSchedule s = NightEventSchedule.None;
        Assert.Equal(NightEventKind.None, s.EventKind);
        Assert.False(s.Fired);
        Assert.Equal(0.0, s.TriggerGameHour);
        Assert.False(s.ShouldTrigger(0.0));
    }

    [Fact]
    public void Roll_ConsumesExactRollCount()
    {
        var rng = new SequenceRandomSource(0.10, 2.0);
        NightEventSchedule s = NightEventSchedule.Roll(rng);
        Assert.Equal(NightEventKind.HumanRaid, s.EventKind);
        // human raid = first roll for chance, second for trigger hour; zombie never rolled
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Roll_ZombieRaid_ConsumesExactRollCount()
    {
        var rng = new SequenceRandomSource(0.50, 0.20, 4.0);
        NightEventSchedule s = NightEventSchedule.Roll(rng);
        Assert.Equal(NightEventKind.ZombieRaid, s.EventKind);
        // human miss (1) + zombie hit (1) + trigger hour (1) = 3
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Roll_None_ConsumesExactRollCount()
    {
        var rng = new SequenceRandomSource(0.90, 0.90);
        NightEventSchedule s = NightEventSchedule.Roll(rng);
        Assert.Equal(NightEventKind.None, s.EventKind);
        // human miss (1) + zombie miss (1) = 2, no trigger hour roll
        Assert.Equal(0, rng.Remaining);
    }
}
