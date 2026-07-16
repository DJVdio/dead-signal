using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// Godot 宿主的<b>战斗数值配置加载器</b>（仿 <see cref="RaiderTacticsData"/> 惯例）：
/// 读 <c>res://data/config/*.json</c> → <see cref="CombatConfigLoader.Parse"/> → 喂给
/// <see cref="CombatCatalog"/>。
/// <para>
/// <b>用 <c>Godot.FileAccess</c> 而非 System.IO</b>：游戏跑在 <c>res://</c> 虚拟文件系统上，
/// 导出后 json 打进 pck，System.IO 读不到。Sim/Tests/WikiExtract 才用 <see cref="CombatConfigFiles"/>。
/// </para>
/// <para>
/// <b>懒加载注册</b>：<c>[ModuleInitializer]</c> 在游戏程序集加载时只<b>注册委托</b>（不读盘），
/// 首次某处调 <c>WeaponTable.Dagger()</c> 时才真正读 res://（那时 Godot 运行时早已就绪）。
/// 与启动时序无关，也不需要 autoload 节点。
/// </para>
/// </summary>
public static class ConfigDb
{
    private const string Dir = "res://data/config/";

    /// <summary>游戏程序集加载即注册 combat 配置的按需解析委托（轻量，不触发 IO）。</summary>
    // CA2255：ModuleInitializer 通常劝阻用于类库；此处是**游戏程序集本体**（application code，正是该属性的适用场景），
    // 且只注册委托、不做 IO，无副作用惊喜。故显式抑制。
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register() => CombatCatalog.Bootstrapper = Load;
#pragma warning restore CA2255

    /// <summary>读 res://data/config/ 下各 json 并解析（首次 catalog 访问时被调用）。</summary>
    public static CombatConfig Load() => CombatConfigLoader.Parse(ReadText);

    private static string ReadText(string file)
    {
        string path = Dir + file;
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null)
        {
            throw new System.IO.FileNotFoundException($"缺战斗配置 {path}（fail-fast，不软回落）。");
        }
        return f.GetAsText();
    }
}
