using System.Runtime.CompilerServices;
using Godot;

namespace DeadSignal.Godot;

// 注意：本文件**引 Godot 类型（FileAccess）**，故只在 godot 运行时编译，**不** Link 进 Sim/Tests
//（那两个宿主用 GameConfigFiles 的 System.IO 版）。这是 godot 侧宿主接线的唯一 Godot 依赖点。

/// <summary>
/// Godot 宿主的<b>消费层数值配置加载器</b>（仿 <c>ConfigDb</c>：那个是纯库 combat 配置的 res:// 加载器，
/// 本类是 godot 消费层配置 <see cref="GameConfig"/> 的对应物）。
/// 读 <c>res://data/config/*.json</c> → <see cref="GameConfigLoader.Parse"/> → 喂给 <see cref="GameConfigCatalog"/>。
/// <para>
/// <b>用 <c>Godot.FileAccess</c> 而非 System.IO</b>：游戏跑在 <c>res://</c> 虚拟文件系统上，导出后 json 打进 pck，
/// System.IO 读不到。Sim/Tests 才用 <see cref="GameConfigFiles"/>。
/// </para>
/// <para>
/// <b>懒加载注册</b>：<c>[ModuleInitializer]</c> 在游戏程序集加载时只<b>注册委托</b>（不读盘），
/// 首次某处访问消费层配置时才真正读 res://（那时 Godot 运行时早已就绪）。与启动时序无关，也不需要 autoload 节点。
/// </para>
/// </summary>
public static class GameConfigDb
{
    private const string Dir = "res://data/config/";

    /// <summary>游戏程序集加载即注册消费层配置的按需解析委托（轻量，不触发 IO）。</summary>
    // CA2255：ModuleInitializer 通常劝阻用于类库；此处是**游戏程序集本体**（application code，正是该属性的适用场景），
    // 且只注册委托、不做 IO，无副作用惊喜。故显式抑制。
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register() => GameConfigCatalog.Bootstrapper = Load;
#pragma warning restore CA2255

    /// <summary>读 res://data/config/ 下各消费层 json 并解析（首次 catalog 访问时被调用）。</summary>
    public static GameConfig Load() => GameConfigLoader.Parse(ReadText);

    private static string ReadText(string file)
    {
        string path = Dir + file;
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null)
        {
            throw new System.IO.FileNotFoundException($"缺消费层配置 {path}（fail-fast，不软回落）。");
        }
        return f.GetAsText();
    }
}
