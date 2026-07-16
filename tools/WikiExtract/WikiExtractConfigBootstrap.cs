using System.Runtime.CompilerServices;
using DeadSignal.Combat;

namespace DeadSignal.WikiExtract;

/// <summary>
/// WikiExtract 宿主启动接线：程序集加载即把 <see cref="CombatCatalog.Bootstrapper"/> 注册为
/// <see cref="CombatConfigFiles.Load"/>（System.IO 定位仓库 godot/data/config）。
/// <para>
/// 抽取器反射遍历 <c>WeaponTable</c> 的工厂方法（现在读 catalog）⇒ 必须先注册 bootstrapper，
/// 否则首个 <c>WeaponTable.Dagger()</c> 反射调用会 fail-fast。外置后抽取器反射到的就是 config 值。
/// </para>
/// </summary>
internal static class WikiExtractConfigBootstrap
{
    [ModuleInitializer]
    internal static void Init() => CombatCatalog.Bootstrapper = CombatConfigFiles.Load;
}
