using System.Runtime.CompilerServices;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 测试宿主启动接线：程序集加载即把 <see cref="GameConfigCatalog.Bootstrapper"/> 注册为
/// <see cref="GameConfigFiles.Load"/>（System.IO 定位仓库 godot/data/config）。
/// <para>
/// 与 <c>TestConfigBootstrap</c>（注册纯库 combat 配置）并列——消费层纯逻辑（<c>NightWatchContest</c> 等）
/// 以 Link 编入本测试工程，其外置后的数值读 <see cref="GameConfigCatalog"/>，故测试宿主也须注册消费层 Bootstrapper。
/// <c>[ModuleInitializer]</c> 在任何测试方法之前跑 ⇒ 首次访问即能懒加载。
/// </para>
/// </summary>
internal static class TestGameConfigBootstrap
{
    [ModuleInitializer]
    internal static void Init() => GameConfigCatalog.Bootstrapper = GameConfigFiles.Load;
}
