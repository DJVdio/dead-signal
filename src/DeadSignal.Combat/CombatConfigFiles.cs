using System;
using System.IO;

namespace DeadSignal.Combat;

/// <summary>
/// 非 Godot 宿主（<b>Sim / Tests / WikiExtract</b>）的 <c>System.IO</c> 配置加载器。
/// <para>
/// 定位仓库的 <c>godot/data/config/</c>（从 <see cref="AppContext.BaseDirectory"/> 上溯，
/// 或环境变量 <c>DEADSIGNAL_CONFIG_DIR</c> 覆盖），逐文件读内容喂给 IO 无关的
/// <see cref="CombatConfigLoader.Parse"/>。
/// </para>
/// <para>
/// 🔴 <b>Godot 侧不用此类</b>：它跑在 <c>res://</c> 虚拟文件系统上、必须用 <c>Godot.FileAccess</c>
/// （见 <c>ConfigDb</c>），<c>System.IO</c> 读不到 <c>res://</c>。本类之所以放在纯库里（而非各宿主各写一份），
/// 是因为三个非 Godot 宿主共用同一套 System.IO 定位逻辑——放这里去重，且 <see cref="CombatConfigLoader.Parse"/>
/// 的「IO 无关」并未被破坏（Godot 走自己的 readText，根本不碰此类）。BCL 的 System.IO 不算 Godot/外部依赖，
/// 「零依赖纯库」指的是不引 Godot/NuGet。
/// </para>
/// </summary>
public static class CombatConfigFiles
{
    /// <summary>读盘 + 解析（供 <see cref="CombatCatalog.Bootstrapper"/> 注册）。</summary>
    public static CombatConfig Load()
    {
        string dir = LocateConfigDir();
        return CombatConfigLoader.Parse(file => File.ReadAllText(Path.Combine(dir, file)));
    }

    /// <summary>定位到的 config 目录（供诊断/测试）。</summary>
    public static string LocateConfigDir()
    {
        string? env = Environment.GetEnvironmentVariable("DEADSIGNAL_CONFIG_DIR");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }

        // 从可执行目录上溯找 godot/data/config（Sim/Tests/WikiExtract 都在仓库内运行）。
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
