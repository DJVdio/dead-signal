using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>
/// <see cref="IReadOnlySet{T}"/>（string）的 JSON 读写：序列化为字符串数组，反序列化materialize为 <see cref="HashSet{T}"/>。
/// <para>
/// 🔴 <b>为什么需要它</b>：System.Text.Json <b>不能反序列化 <c>IReadOnlySet&lt;string&gt;</c></b>（接口/只读，无法实例化——
/// 直接反序列化会抛 <c>NotSupportedException</c>）。<see cref="ArmorLayer.CoversParts"/> 是该类型，护甲数值外置到
/// <c>armor.json</c> 后加载器必须能把它读回来，故在该属性上挂本转换器。序列化输出与默认集合一致（<c>["胸","腹"]</c>），
/// 往返位级保真。仅作用于挂了 <c>[JsonConverter(typeof(...))]</c> 的那个属性，不影响其它类型（如 Weapon 无集合字段）。
/// </para>
/// <para>
/// null 由 STJ 在属性层直接处理（写 <c>null</c>、读 <c>null</c> 均不进本转换器），故本类只管非 null 的集合体。
/// </para>
/// </summary>
public sealed class ReadOnlyStringSetJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override IReadOnlySet<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = JsonSerializer.Deserialize<List<string>>(ref reader, options);
        return list is null ? null : new HashSet<string>(list);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var s in value)
        {
            writer.WriteStringValue(s);
        }
        writer.WriteEndArray();
    }
}
