using System;
using System.IO;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**（BCL System.IO 不算 Godot/外部依赖），不得引入任何 Godot 类型。
// 被 DeadSignal.Sim 与 DeadSignal.Combat.Tests 以 Link 编入（它们无 Godot 运行时，走 System.IO）；
// godot 运行时也编译本文件但不用它（那边 GameConfigDb 用 res://FileAccess 抢先注册 Bootstrapper）。

/// <summary>
/// 非 Godot 宿主（<b>Sim / Tests</b>）的 <c>System.IO</c> 消费层配置加载器。
/// <para>
/// 🔴 纯库 <c>CombatConfigFiles</c> 在 godot 消费层的<b>平行镜像</b>：复用同一个 <c>godot/data/config/</c> 目录
/// （combat 与消费层的 json 同放一处），逐文件读内容喂给 IO 无关的 <see cref="GameConfigLoader.Parse"/>。
/// </para>
/// <para>
/// <b>Godot 侧不用此类</b>：它跑在 <c>res://</c> 虚拟文件系统上、必须用 <c>Godot.FileAccess</c>（见 <c>GameConfigDb</c>），
/// <c>System.IO</c> 读不到 <c>res://</c>。之所以放在 godot/scripts（可被 Sim/Tests Link、也被 godot 编译），
/// 是让两个非 Godot 宿主共用同一套 System.IO 定位逻辑去重。
/// </para>
/// </summary>
public static class GameConfigFiles
{
    /// <summary>读盘 + 解析（供 <see cref="GameConfigCatalog.Bootstrapper"/> 注册）。</summary>
    public static GameConfig Load()
    {
        string dir = LocateConfigDir();
        return GameConfigLoader.Parse(file => File.ReadAllText(Path.Combine(dir, file)));
    }

    /// <summary>定位到的 config 目录（供诊断/测试）。与纯库 <c>CombatConfigFiles</c> 同一目录、同一定位规则。</summary>
    public static string LocateConfigDir()
    {
        string? env = Environment.GetEnvironmentVariable("DEADSIGNAL_CONFIG_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }

        // 从可执行目录上溯找 godot/data/config（Sim/Tests 都在仓库内运行）。
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            string cand = Path.Combine(d.FullName, "godot", "data", "config");
            if (Directory.Exists(cand))
            {
                return cand;
            }
            d = d.Parent;
        }

        throw new DirectoryNotFoundException(
            "定位不到 godot/data/config（从 " + AppContext.BaseDirectory
            + " 上溯到根仍未找到）。可设环境变量 DEADSIGNAL_CONFIG_DIR 覆盖。");
    }
}
