using System.Text.Json;

namespace DeadSignal.Combat;

/// <summary>
/// 一个「配置段」＝一个子系统的数值文件（weapons.json / armor.json / ammo.json …）。
/// <para>
/// 🔴 <b>高并发扩展的关键抽象</b>：每个子系统迁移单只需<b>新建自己的段类文件</b>（如 <c>ArmorConfig.cs</c>）
/// 实现本接口 + 放一个 <c>x.json</c> + 往 <see cref="CombatConfig"/> 加<b>一行</b>属性——
/// <see cref="CombatConfigLoader.Parse"/> 是<b>反射驱动</b>的，会自动发现并加载所有段，<b>加载器主体永不改</b>
/// （不成为串行瓶颈）。各单锁自己的新文件为主，撞车窗口＝只有 <see cref="CombatConfig"/> 那一行。
/// </para>
/// <para>
/// 段类自己负责「文件名」与「怎么从 json 构造自己」（<see cref="FromJson"/>）——json 文件保持
/// <b>裸载荷</b>（如 weapons.json 顶层就是 <c>{ id: {...} }</c>，不套 wrapper 键），人可读、schema 干净。
/// </para>
/// </summary>
public interface IConfigSection
{
    /// <summary>本段对应的配置文件名（相对 config 目录，如 <c>"weapons.json"</c>）。</summary>
    string FileName { get; }

    /// <summary>从该文件内容构造本段（用统一 <paramref name="options"/>，保证与生成器同口径 ⇒ 往返精确）。缺/空 fail-fast。</summary>
    IConfigSection FromJson(string json, JsonSerializerOptions options);
}
