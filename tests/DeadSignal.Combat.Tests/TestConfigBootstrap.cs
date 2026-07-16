using System.Runtime.CompilerServices;
using DeadSignal.Combat;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 测试宿主启动接线：程序集加载即把 <see cref="CombatCatalog.Bootstrapper"/> 注册为
/// <see cref="CombatConfigFiles.Load"/>（System.IO 定位仓库 godot/data/config）。
/// <para>
/// <c>[ModuleInitializer]</c> 在任何测试方法之前跑 ⇒ 首次 <c>WeaponTable.*()</c> / <c>CombatCatalog.Weapon()</c>
/// 访问即能懒加载。这就是 config-skeleton 派单里「测试 fixture/静态构造兜底」的落点。
/// </para>
/// </summary>
internal static class TestConfigBootstrap
{
    [ModuleInitializer]
    internal static void Init() => CombatCatalog.Bootstrapper = CombatConfigFiles.Load;
}
