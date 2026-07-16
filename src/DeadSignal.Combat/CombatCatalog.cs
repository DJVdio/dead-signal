using System;
using System.Collections.Generic;
using System.Reflection;

namespace DeadSignal.Combat;

/// <summary>
/// 战斗数值<b>静态目录</b>：只收已解析的 <see cref="CombatConfig"/> POCO，<b>绝不碰 IO</b>。
/// <para>
/// <see cref="WeaponTable.Dagger"/> 等工厂方法（方法名不变、~120 调用点一处不动）现在的身体是
/// <c>=&gt; CombatCatalog.Weapon("dagger")</c>——数值真源从 C# 常量搬到了 <c>weapons.json</c>。
/// </para>
/// <para>
/// <b>初始化两条路</b>：
/// <list type="number">
///   <item><see cref="Initialize"/>：宿主/测试直接注入已解析配置。</item>
///   <item><see cref="Bootstrapper"/> 懒加载：宿主在启动时（<c>[ModuleInitializer]</c>）注册一个「按需解析」委托，
///     首次段访问时才读盘。<b>只注册委托、不在注册时读盘</b> ⇒ 与启动时序无关，
///     且 IO 发生在首次用时（Godot 运行时已就绪、res:// 可读）。</item>
/// </list>
/// 四宿主各注册自己的 IO 实现：Godot=<c>ConfigDb.Load</c>（FileAccess）；Sim/Tests/WikiExtract=
/// <see cref="CombatConfigFiles.Load"/>（System.IO）。
/// </para>
/// <para>
/// 🔴 <b>高并发扩展</b>：新子系统用泛型 <see cref="Section{T}"/> 取自己的段（如
/// <c>CombatCatalog.Section&lt;ArmorConfig&gt;().Get("plate")</c>），<b>不必往本类加方法</b>——
/// 各子系统的取用糖可放在自己的 Table 文件里（如 <c>ArmorTable</c>），本类零改动。
/// <see cref="Weapon"/> 是 weapons 段的既有取用糖（~120 调用点在用），保留。
/// </para>
/// </summary>
public static class CombatCatalog
{
    // 🔴 懒加载线程安全门（双检锁）：xUnit 默认并行跑不同 test collection，多线程会同时首访本目录。
    // 若无同步，Initialize 先写 _config 后写 _sections，快路径读者会在 _config 已非空、_sections 仍 null 的
    // 半初始化窗口里读到 null → Section<T>() 的 _sections!.TryGetValue 抛 NullReferenceException。
    private static readonly object _initGate = new();
    // volatile：发布屏障。写 _config 前的 _sections 写对任何"看到非空 _config"的读者可见（release/acquire）。
    private static volatile CombatConfig? _config;
    // Type → 段实例的缓存索引（一次装配、O(1) 取用，避免每次取武器都反射）。
    private static Dictionary<Type, IConfigSection>? _sections;

    /// <summary>
    /// 宿主注册的「按需解析」委托（读盘 + <see cref="CombatConfigLoader.Parse"/>）。<b>注册即轻量</b>——
    /// 只存委托、不触发 IO；真正读盘发生在首次 <see cref="Config"/> 访问。
    /// </summary>
    public static Func<CombatConfig>? Bootstrapper { get; set; }

    /// <summary>是否已解析（IO 已发生）。</summary>
    public static bool IsInitialized => _config != null;

    /// <summary>直接注入已解析配置（宿主/测试用；覆盖懒加载）。</summary>
    public static void Initialize(CombatConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }
        lock (_initGate)
        {
            // 🔴 顺序关键：先建索引，最后再 volatile-发布 _config。读者以 _config 非空为"就绪"信号，
            // 故 _sections 必须在 _config 发布之前完成（release 屏障保证发布前的写对读者可见）。
            _sections = IndexSections(config);
            _config = config;
        }
    }

    /// <summary>清空已解析配置（测试隔离用；下次访问重新走 <see cref="Bootstrapper"/>）。</summary>
    public static void Reset()
    {
        lock (_initGate)
        {
            // 先撤"就绪"信号（_config），再清索引——与 Initialize 的发布顺序对称。
            _config = null;
            _sections = null;
        }
    }

    private static CombatConfig Config
    {
        get
        {
            // 快路径：已就绪则免锁直返（volatile 读，acquire 语义）。
            var cfg = _config;
            if (cfg != null)
            {
                return cfg;
            }
            // 慢路径：双检锁，保证并发首访只 boot() 一次、只装配一次。
            lock (_initGate)
            {
                if (_config == null)
                {
                    var boot = Bootstrapper ?? throw new InvalidOperationException(
                        "CombatCatalog 未初始化且未注册 Bootstrapper。宿主须在启动时注册"
                        + "（Godot=ConfigDb / Sim·Tests·WikiExtract=CombatConfigFiles）。");
                    Initialize(boot()); // lock 可重入（Monitor），Initialize 内再取同一 gate 无死锁
                }
                return _config!;
            }
        }
    }

    /// <summary>
    /// 泛型取段（<b>后续子系统的统一入口</b>）：<c>Section&lt;WeaponConfig&gt;()</c> / <c>Section&lt;ArmorConfig&gt;()</c>。
    /// 缺段 fail-fast（说明 <see cref="CombatConfig"/> 没挂这段属性）。
    /// </summary>
    public static T Section<T>() where T : class, IConfigSection
    {
        _ = Config; // 触发懒加载 + 建索引（此后 _config 非空 ⇒ acquire 屏障保证 _sections 也已可见）
        var sections = _sections!; // 捕获局部：Config 就绪后 _sections 必非空，避免二次读取被并发 Reset 撞空
        if (sections.TryGetValue(typeof(T), out var s))
        {
            return (T)s;
        }
        throw new InvalidOperationException($"CombatConfig 未挂 {typeof(T).Name} 段（去 CombatConfig 加一行属性）。");
    }

    /// <summary>按 id 取武器（weapons 段取用糖，~120 调用点在用）。缺失 fail-fast。</summary>
    public static Weapon Weapon(string id) => Section<WeaponConfig>().Get(id);

    /// <summary>全部武器（id → Weapon），供反射/遍历类消费（如 WikiExtract）。</summary>
    public static IReadOnlyDictionary<string, Weapon> Weapons => Section<WeaponConfig>().ById;

    private static Dictionary<Type, IConfigSection> IndexSections(CombatConfig config)
    {
        var map = new Dictionary<Type, IConfigSection>();
        foreach (PropertyInfo prop in typeof(CombatConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetValue(config) is IConfigSection s)
            {
                map[prop.PropertyType] = s;
            }
        }
        return map;
    }
}
