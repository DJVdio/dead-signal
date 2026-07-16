using System.Runtime.CompilerServices;
using DeadSignal.Combat;

namespace DeadSignal.Sim;

/// <summary>
/// Sim 宿主启动接线：程序集加载即把 <see cref="CombatCatalog.Bootstrapper"/> 注册为
/// <see cref="CombatConfigFiles.Load"/>（System.IO 定位仓库 godot/data/config）。
/// <para>
/// <c>[ModuleInitializer]</c> 保证在 <c>Program</c> 顶层语句（<c>WeaponTable.Arsenal()</c>）之前跑，
/// 首次武器访问即能懒加载到 catalog。<b>不用 Godot.FileAccess</b>（Sim 无 Godot 运行时）。
/// </para>
/// </summary>
internal static class SimConfigBootstrap
{
    [ModuleInitializer]
    internal static void Init() => CombatCatalog.Bootstrapper = CombatConfigFiles.Load;
}
