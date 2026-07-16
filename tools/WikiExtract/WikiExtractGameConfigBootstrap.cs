using System.Runtime.CompilerServices;
using DeadSignal.Godot;

namespace DeadSignal.WikiExtract;

/// <summary>
/// WikiExtract 宿主启动接线：程序集加载即把 <see cref="GameConfigCatalog.Bootstrapper"/> 注册为
/// <see cref="GameConfigFiles.Load"/>（System.IO 定位仓库 godot/data/config）。
/// <para>
/// 与 <c>WikiExtractConfigBootstrap</c>（注册纯库 combat 配置）并列——抽取器 Link 了 <c>NightWatchContest</c>，
/// 其外置后的潜行力权重读 <see cref="GameConfigCatalog"/>，故抽取器宿主也须注册消费层 Bootstrapper，
/// 否则首个 <c>NightWatchContest</c> 静态属性访问会 fail-fast。
/// </para>
/// </summary>
internal static class WikiExtractGameConfigBootstrap
{
    [ModuleInitializer]
    internal static void Init() => GameConfigCatalog.Bootstrapper = GameConfigFiles.Load;
}
