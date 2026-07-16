using System;
using System.Collections.Generic;
using System.Reflection;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（只收已解析的 GameConfig POCO，绝不碰 IO；IO 由宿主注入的 Bootstrapper 承担）。

/// <summary>
/// 消费层数值<b>静态目录</b>：只收已解析的 <see cref="GameConfig"/> POCO，<b>绝不碰 IO</b>。
/// <para>
/// 🔴 纯库 <c>CombatCatalog</c> 在 godot 消费层的<b>平行镜像</b>。消费层子系统（如 <c>NightWatchContest</c>）
/// 的静态取用点现在的身体是 <c>=&gt; GameConfigCatalog.Section&lt;NightWatchConfig&gt;().StealthCoverWeight</c>——
/// 数值真源从 C# 常量搬到了 <c>nightwatch.json</c>。
/// </para>
/// <para>
/// <b>初始化两条路</b>：
/// <list type="number">
///   <item><see cref="Initialize"/>：宿主/测试直接注入已解析配置。</item>
///   <item><see cref="Bootstrapper"/> 懒加载：宿主在启动时（<c>[ModuleInitializer]</c>）注册一个「按需解析」委托，
///     首次段访问时才读盘。<b>只注册委托、不在注册时读盘</b> ⇒ 与启动时序无关。</item>
/// </list>
/// 三宿主各注册自己的 IO 实现：Godot=<c>GameConfigDb.Load</c>（FileAccess）；Sim/Tests=
/// <see cref="GameConfigFiles.Load"/>（System.IO）。
/// </para>
/// <para>
/// 🔴 <b>高并发扩展</b>：新子系统用泛型 <see cref="Section{T}"/> 取自己的段（如
/// <c>GameConfigCatalog.Section&lt;HungerConfig&gt;()</c>），<b>不必往本类加方法</b>——各子系统的取用糖放在
/// 自己的类里（如 <c>NightWatchContest</c> 的静态属性），本类零改动。
/// </para>
/// </summary>
public static class GameConfigCatalog
{
    private static GameConfig? _config;
    // Type → 段实例的缓存索引（一次装配、O(1) 取用，避免每次取值都反射）。
    private static Dictionary<Type, IGameConfigSection>? _sections;

    /// <summary>
    /// 宿主注册的「按需解析」委托（读盘 + <see cref="GameConfigLoader.Parse"/>）。<b>注册即轻量</b>——
    /// 只存委托、不触发 IO；真正读盘发生在首次 <see cref="Config"/> 访问。
    /// </summary>
    public static Func<GameConfig>? Bootstrapper { get; set; }

    /// <summary>是否已解析（IO 已发生）。</summary>
    public static bool IsInitialized => _config != null;

    /// <summary>直接注入已解析配置（宿主/测试用；覆盖懒加载）。</summary>
    public static void Initialize(GameConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _sections = IndexSections(config);
    }

    /// <summary>清空已解析配置（测试隔离用；下次访问重新走 <see cref="Bootstrapper"/>）。</summary>
    public static void Reset()
    {
        _config = null;
        _sections = null;
    }

    private static GameConfig Config
    {
        get
        {
            if (_config == null)
            {
                var boot = Bootstrapper ?? throw new InvalidOperationException(
                    "GameConfigCatalog 未初始化且未注册 Bootstrapper。宿主须在启动时注册"
                    + "（Godot=GameConfigDb / Sim·Tests=GameConfigFiles）。");
                Initialize(boot());
            }
            return _config!;
        }
    }

    /// <summary>
    /// 泛型取段（<b>后续子系统的统一入口</b>）：<c>Section&lt;NightWatchConfig&gt;()</c> / <c>Section&lt;HungerConfig&gt;()</c>。
    /// 缺段 fail-fast（说明 <see cref="GameConfig"/> 没挂这段属性）。
    /// </summary>
    public static T Section<T>() where T : class, IGameConfigSection
    {
        _ = Config; // 触发懒加载 + 建索引
        if (_sections!.TryGetValue(typeof(T), out var s))
        {
            return (T)s;
        }
        throw new InvalidOperationException($"GameConfig 未挂 {typeof(T).Name} 段（去 GameConfig 加一行属性）。");
    }

    private static Dictionary<Type, IGameConfigSection> IndexSections(GameConfig config)
    {
        var map = new Dictionary<Type, IGameConfigSection>();
        foreach (PropertyInfo prop in typeof(GameConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetValue(config) is IGameConfigSection s)
            {
                map[prop.PropertyType] = s;
            }
        }
        return map;
    }
}
