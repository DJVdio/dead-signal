using System;
using System.IO;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 营地站岗运行时接线护栏：守卫在非战斗时自主巡查、不得生产，敌袭进入战斗后才交还玩家控制。
/// 空间执行依赖 Godot，故这里钉死消费层源码接线；扫视波形本身由 <see cref="SentryLogicTests"/> 做纯逻辑测试。
/// </summary>
public class GuardDutyRuntimeTests
{
    [Fact]
    public void Pawn到岗后自动规律扫视_交战时不覆盖战斗朝向()
    {
        string pawn = Source("godot/scripts/Pawn.cs");

        Assert.Contains("BeginGuardDuty", pawn);
        Assert.Contains("EndGuardDuty", pawn);
        Assert.Contains("SentryLogic.FacingFor(SentryAction.HoldPost", pawn);
        Assert.Contains("!HasActiveTarget", pawn);
        Assert.Contains("if (!GuardCombatControlEnabled)", pawn);
    }

    [Fact]
    public void 每夜每岗只掷一次扫视相位_驻防结束清理运行时态()
    {
        string camp = Source("godot/scripts/CampMain.cs");

        Assert.Contains("post.SweepPhaseDay != _clock.Day", camp);
        Assert.Contains("SentryLogic.RollSweepPhase(_guardSweepRng", camp);
        Assert.Contains("guard.BeginGuardDuty", camp);
        Assert.Contains("EndGuardDuty", camp);
    }

    [Fact]
    public void Guard只有敌袭已进入战斗时才允许玩家操控()
    {
        string camp = Source("godot/scripts/CampMain.cs");

        Assert.Contains("pawn.Role == PawnRole.Guard", camp);
        Assert.Contains("_nightRaidActive && _nightRaidResolved", camp);
        Assert.Contains("_raidActive || _tutorialActive || _siegeActive", camp);
        Assert.Contains("guard.GuardCombatControlEnabled = GuardCombatControlAllowed(guard)", camp);
    }

    [Fact]
    public void Guard既不会进入生产候选_已开工后转岗也不会推进进度()
    {
        string camp = Source("godot/scripts/CampMain.cs");

        Assert.Contains("p.Role != PawnRole.Guard", camp);
        Assert.Contains("worker is { Role: PawnRole.Producing }", camp);
    }

    private static string Source(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到 DeadSignal 仓库根目录");
    }
}
