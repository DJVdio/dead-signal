namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（被 DeadSignal.Combat.Tests 与 DeadSignal.Sim 以 Link 方式编入；godot 运行时直接编译）。

/// <summary>
/// 消费层数值配置<b>聚合容器</b>（init-only POCO）。<b>只做聚合引用，不塞逻辑</b>：每个消费层子系统的段类
/// （<see cref="NightWatchConfig"/> …）各占独立文件，此处每段只挂<b>一行</b>属性。
/// <para>
/// 🔴 这是纯库 <c>CombatConfig</c> 在 godot 消费层的<b>平行镜像</b>——数值类型都在 <c>godot/scripts</c>，
/// 纯库装不下（循环依赖），故另立于此，范式一字不差。
/// </para>
/// <para>
/// 🔴 <b>高并发扩展点（消费层各单唯一共享文件·一行）</b>：后续迁移单在此**加一行**（如
/// <c>public HungerConfig Hunger { get; init; } = new();</c>），再建自己的 <c>HungerConfig.cs</c> +
/// <c>hunger.json</c> 即接入——<see cref="GameConfigLoader.Parse"/> 反射本类的所有
/// <see cref="IGameConfigSection"/> 属性自动加载，<b>加载器与宿主接线都不用动</b>。各段属性必须给默认实例
/// （<c>= new()</c>），以便加载前反射能读到其 <see cref="IGameConfigSection.FileName"/>。
/// </para>
/// </summary>
public sealed class GameConfig
{
    /// <summary>夜防对抗段（nightwatch.json）——潜行力权重等数值。</summary>
    public NightWatchConfig NightWatch { get; init; } = new();

    // ── 后续消费层 config 迁移单在此加一行（各自独立、互不撞车）──────────────────
    // public HungerConfig    Hunger    { get; init; } = new();   // hunger.json
    // public RecipeConfig    Recipes   { get; init; } = new();   // recipes.json
    // public FurnitureConfig Furniture { get; init; } = new();   // furniture.json
    // …
}
