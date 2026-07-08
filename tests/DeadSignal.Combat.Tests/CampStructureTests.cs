using System;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 围栏/大门可破坏结构纯逻辑：等级→血量表、承伤、摧毁判定（升级机制后续，只测数据表 + 血量语义）。
/// </summary>
public sealed class CampStructureTests
{
    // ---------------- 等级 → 血量表（拟定待调，改表须同步这里） ----------------

    [Theory]
    [InlineData(StructureTier.FenceBasic, 150)]
    [InlineData(StructureTier.FenceReinforced, 250)]
    [InlineData(StructureTier.FenceSheetMetal, 400)]
    [InlineData(StructureTier.FenceFullMetal, 750)]
    [InlineData(StructureTier.GateBasic, 250)]
    [InlineData(StructureTier.GateSheetMetal, 400)]
    [InlineData(StructureTier.GateCastMetal, 800)]
    public void MaxHp_匹配拟定数值(StructureTier tier, int expected)
    {
        Assert.Equal(expected, CampStructureTable.MaxHp(tier));
    }

    [Theory]
    [InlineData(StructureTier.FenceBasic, CampStructureKind.Fence)]
    [InlineData(StructureTier.FenceFullMetal, CampStructureKind.Fence)]
    [InlineData(StructureTier.GateBasic, CampStructureKind.Gate)]
    [InlineData(StructureTier.GateCastMetal, CampStructureKind.Gate)]
    public void KindOf_区分围栏与大门档(StructureTier tier, CampStructureKind expected)
    {
        Assert.Equal(expected, CampStructureTable.KindOf(tier));
    }

    [Fact]
    public void BaseTier_取各类基础档()
    {
        Assert.Equal(StructureTier.FenceBasic, CampStructureTable.BaseTier(CampStructureKind.Fence));
        Assert.Equal(StructureTier.GateBasic, CampStructureTable.BaseTier(CampStructureKind.Gate));
    }

    // ---------------- 初始状态 ----------------

    [Fact]
    public void 新建结构_满血未摧毁()
    {
        var fence = new CampStructureState(StructureTier.FenceBasic);
        Assert.Equal(CampStructureKind.Fence, fence.Kind);
        Assert.Equal(150, fence.MaxHp);
        Assert.Equal(150, fence.Hp);
        Assert.False(fence.IsDestroyed);
        Assert.Equal(1f, fence.HealthFraction);

        var gate = new CampStructureState(StructureTier.GateBasic);
        Assert.Equal(CampStructureKind.Gate, gate.Kind);
        Assert.Equal(250, gate.Hp);
    }

    // ---------------- 承伤 / 摧毁 ----------------

    [Fact]
    public void 承伤未见底_扣血且不摧毁_返回false()
    {
        var fence = new CampStructureState(StructureTier.FenceBasic); // 150
        bool destroyed = fence.TakeDamage(60);
        Assert.False(destroyed);
        Assert.Equal(90, fence.Hp);
        Assert.False(fence.IsDestroyed);
        Assert.Equal(90f / 150f, fence.HealthFraction, 5);
    }

    [Fact]
    public void 致命一击_血量见底_返回true并摧毁()
    {
        var fence = new CampStructureState(StructureTier.FenceBasic); // 150
        fence.TakeDamage(100); // 剩 50
        bool destroyed = fence.TakeDamage(50); // 归 0
        Assert.True(destroyed);
        Assert.Equal(0, fence.Hp);
        Assert.True(fence.IsDestroyed);
        Assert.Equal(0f, fence.HealthFraction);
    }

    [Fact]
    public void 过量伤害_血量夹到0_且只在本击返回一次true()
    {
        var gate = new CampStructureState(StructureTier.GateBasic); // 250
        bool destroyed = gate.TakeDamage(9999);
        Assert.True(destroyed);
        Assert.Equal(0, gate.Hp);

        // 摧毁后再打：不再报"本击摧毁"（消费层据此不重复开缺口/重烘焙），血量保持 0。
        bool again = gate.TakeDamage(10);
        Assert.False(again);
        Assert.Equal(0, gate.Hp);
        Assert.True(gate.IsDestroyed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 非正伤害_忽略(int amount)
    {
        var fence = new CampStructureState(StructureTier.FenceBasic);
        bool destroyed = fence.TakeDamage(amount);
        Assert.False(destroyed);
        Assert.Equal(150, fence.Hp);
    }

    [Fact]
    public void MaxHp_未知等级抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CampStructureTable.MaxHp((StructureTier)999));
    }
}
