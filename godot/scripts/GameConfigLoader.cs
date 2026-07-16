using System;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（IO 由宿主注入的 readText 委托承担；godot 走 res://FileAccess，Sim/Tests 走 System.IO）。

/// <summary>
/// <b>IO 无关 + 反射驱动</b>的消费层配置解析器：宿主喂一个「文件名 → 内容字符串」委托，它反射
/// <see cref="GameConfig"/> 的每个 <see cref="IGameConfigSection"/> 段属性，逐段读盘 + 反序列化，装配出完整配置。
/// <para>
/// 🔴 纯库 <c>CombatConfigLoader</c> 在 godot 消费层的<b>平行镜像</b>，主体一字不差。
/// <b>本类主体永不随迁移单改动</b>（不成为串行瓶颈）：新增子系统只需建段类 + json + 往 <see cref="GameConfig"/>
/// 加一行，<see cref="Parse"/> 会自动发现新段。纯逻辑<b>不知道文件在哪</b>——Godot 用 <c>res://</c>（<c>FileAccess</c>，
/// 见 <c>GameConfigDb</c>）、Sim/Tests 用 <c>System.IO</c>（<see cref="GameConfigFiles"/>），各自实现 <c>readText</c>。
/// </para>
/// <para>
/// <b>缺配置 fail-fast</b>（用户拍板：不软回落、不留 C# 兜底默认值）——文件缺失/解析失败/为空一律抛。
/// </para>
/// </summary>
public static class GameConfigLoader
{
    /// <summary>
    /// 序列化 / 反序列化的<b>唯一口径</b>（与纯库 <c>CombatConfigLoader.Options</c> 同配置）。生成 <c>*.json</c>
    /// 与读取它<b>必须用同一份</b>，才能保证浮点往返精确、枚举出字符串（防序号漂移）、中文名直出（可读的数值源文件）。
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,                 // Name / name 都认（手写 json 容错）
        Converters = { new JsonStringEnumConverter() },     // 枚举出字符串
        ReadCommentHandling = JsonCommentHandling.Skip,     // 允许 json 里写 // 注释（数值表可读性）
        AllowTrailingCommas = true,
        WriteIndented = true,
        // 中文名/描述直出（不转义成 \uXXXX）——config json 是可人工/agent 编辑的数值源，须可读。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 装配全部消费层配置。<paramref name="readText"/>(文件名)＝返回该文件的完整内容。
    /// <b>反射 <see cref="GameConfig"/> 的每个段属性自动加载</b>，无需为新子系统改本方法。
    /// </summary>
    public static GameConfig Parse(Func<string, string> readText)
    {
        if (readText == null)
        {
            throw new ArgumentNullException(nameof(readText));
        }

        var config = new GameConfig();
        foreach (PropertyInfo prop in typeof(GameConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IGameConfigSection).IsAssignableFrom(prop.PropertyType))
            {
                continue;
            }

            // 默认实例（GameConfig 每段 = new()）报出自己的文件名，再由段类自解析。
            var proto = (IGameConfigSection)prop.GetValue(config)!;
            string text = readText(proto.FileName)
                ?? throw new InvalidOperationException($"消费层配置文件缺失：{proto.FileName}（fail-fast，不软回落）。");

            IGameConfigSection loaded;
            try
            {
                loaded = proto.FromJson(text, Options);
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException($"消费层配置解析失败：{proto.FileName} —— {e.Message}", e);
            }

            // init-only 属性运行时可经反射赋值（init 限制只在编译期）。
            prop.SetValue(config, loaded);
        }
        return config;
    }
}
