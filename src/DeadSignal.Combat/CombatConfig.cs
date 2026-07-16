namespace DeadSignal.Combat;

/// <summary>
/// 战斗数值配置<b>聚合容器</b>（init-only POCO）。<b>只做聚合引用，不塞逻辑</b>：每个子系统的段类
/// （<see cref="WeaponConfig"/> …）各占独立文件，此处每段只挂<b>一行</b>属性。
/// <para>
/// 🔴 <b>高并发扩展点</b>：后续迁移单在此**加一行**（如 <c>public ArmorConfig Armor { get; init; } = new();</c>），
/// 再建自己的 <c>ArmorConfig.cs</c> + <c>armor.json</c> 即接入——<see cref="CombatConfigLoader.Parse"/> 反射本类的
/// 所有 <see cref="IConfigSection"/> 属性自动加载，<b>加载器与宿主接线都不用动</b>。各段属性必须给默认实例
/// （<c>= new()</c>），以便加载前反射能读到其 <see cref="IConfigSection.FileName"/>。
/// </para>
/// </summary>
public sealed class CombatConfig
{
    /// <summary>武器段（weapons.json）。</summary>
    public WeaponConfig Weapons { get; init; } = new();

    /// <summary>护甲段（armor.json）。</summary>
    public ArmorConfig Armor { get; init; } = new();

    /// <summary>弓弩段（archery.json）：4 种箭 + 穿透封顶/散布下限/回收率/《弓与箭之道》三项被动。</summary>
    public ArcheryConfig Archery { get; init; } = new();

    /// <summary>弹药段（ammo.json）：四种可造子弹的「1 个子弹零件造几发」制作比（短 8/中 5/鹿 4/长 2）。</summary>
    public AmmoConfig Ammo { get; init; } = new();

    /// <summary>身体段（body.json）：部位体积权重(命中倾向)+最大 HP(切除阈值) 与残疾惩罚(单肢/每指/每趾)。</summary>
    public BodyConfig Body { get; init; } = new();

    // ── 后续 config 迁移单在此加一行（各自独立、互不撞车）──────────────────
    // …
}
