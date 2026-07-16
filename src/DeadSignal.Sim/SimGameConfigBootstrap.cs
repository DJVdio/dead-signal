using System.Runtime.CompilerServices;
using DeadSignal.Godot;

namespace DeadSignal.Sim;

/// <summary>
/// Sim 宿主启动接线：程序集加载即把 <see cref="GameConfigCatalog.Bootstrapper"/> 注册为
/// <see cref="GameConfigFiles.Load"/>（System.IO 定位仓库 godot/data/config）。
/// <para>
/// 与 <see cref="SimConfigBootstrap"/>（那个注册纯库 combat 配置）并列——Sim 经 <c>WatchCalibration</c> Link 了
/// <c>NightWatchContest</c>，其潜行力权重外置后读 <see cref="GameConfigCatalog"/>，故 Sim 也须注册消费层 Bootstrapper。
/// <c>[ModuleInitializer]</c> 保证在任何 <c>NightWatchContest</c> 访问之前跑，首次访问即能懒加载。
/// </para>
/// </summary>
internal static class SimGameConfigBootstrap
{
    [ModuleInitializer]
    internal static void Init() => GameConfigCatalog.Bootstrapper = GameConfigFiles.Load;
}
