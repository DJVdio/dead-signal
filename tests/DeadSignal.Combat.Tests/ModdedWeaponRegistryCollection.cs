using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b><see cref="DeadSignal.Godot.ModdedWeaponRegistry"/> 是进程级静态注册表，测试必须串行访问它。</b>
///
/// <para>xunit 默认**把不同测试类并行跑**（一个类＝一个 collection）。而三个类都要
/// <c>ModdedWeaponRegistry.Clear()</c> 之后再 <c>Register()</c> 自己的变体枪——并行下
/// 甲类的 <c>Clear()</c> 会插进乙类的 <c>Register()</c> 和断言之间，把乙类刚登记的枪擦掉，
/// 于是 <c>WeaponByName</c> 返回 null，乙类**随机**红一条。实测闪烁率约 1/5。</para>
///
/// <para>把它们并进同一个 collection ⇒ 三者彼此串行（与其余测试类照旧并行）。
/// <b>这不是给产品打补丁</b>：游戏运行时只有一个注册表、只有一条主线程，本来就没有这个竞争；
/// 竞争纯粹是测试并行造出来的，所以修在测试侧。</para>
///
/// <para>⚠️ 以后任何新测试只要碰 <see cref="DeadSignal.Godot.ModdedWeaponRegistry"/> 的
/// <c>Clear</c>/<c>Register</c>/<c>Restore</c>，都必须挂上 <c>[Collection(Name)]</c>，否则它会
/// 随机弄红别人。</para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class ModdedWeaponRegistryCollection
{
    public const string Name = "ModdedWeaponRegistry（进程级静态注册表，须串行）";
}
