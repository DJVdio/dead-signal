using System.Text.Json;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 NightWatchContest.cs / HungerState.cs 一样被 DeadSignal.Combat.Tests 与 DeadSignal.Sim 以 Link 方式编入）。

/// <summary>
/// 一个「消费层配置段」＝一个消费层子系统的数值文件（nightwatch.json / hunger.json / recipes.json …）。
/// <para>
/// 🔴 <b>这是 <see cref="global::DeadSignal.Combat.IConfigSection"/> 在 godot 消费层的<b>平行镜像</b></b>——
/// 纯库 <c>src/DeadSignal.Combat</c> 里的 <c>CombatConfig</c>/<c>CombatCatalog</c> 只能装纯库 POCO；
/// 消费层数值（Recipe/Materials/FurnitureBuildCost/HungerState/NightWatchContest… 全在 <c>godot/scripts</c>）
/// 的类型纯库<b>引用不了</b>（会循环依赖）。故此处在 godot 侧另立一套<b>完全相同的反射注入范式</b>，宿主在 godot/scripts。
/// </para>
/// <para>
/// <b>高并发扩展的关键抽象</b>：每个消费层迁移单只需<b>新建自己的段类文件</b>（如 <c>HungerConfig.cs</c>）
/// 实现本接口 + 放一个 <c>x.json</c> + 往 <see cref="GameConfig"/> 加<b>一行</b>属性——
/// <see cref="GameConfigLoader.Parse"/> 是<b>反射驱动</b>的，会自动发现并加载所有段，<b>加载器主体永不改</b>。
/// 各单锁自己的新文件为主，撞车窗口＝只有 <see cref="GameConfig"/> 那一行。
/// </para>
/// <para>
/// 段类自己负责「文件名」与「怎么从 json 构造自己」（<see cref="FromJson"/>）——json 文件保持
/// <b>裸载荷</b>（顶层就是段的字段，不套 wrapper 键），人可读、schema 干净。
/// </para>
/// </summary>
public interface IGameConfigSection
{
    /// <summary>本段对应的配置文件名（相对 config 目录，如 <c>"nightwatch.json"</c>）。</summary>
    string FileName { get; }

    /// <summary>从该文件内容构造本段（用统一 <paramref name="options"/>，保证与生成器同口径 ⇒ 往返精确）。缺/空 fail-fast。</summary>
    IGameConfigSection FromJson(string json, JsonSerializerOptions options);
}
