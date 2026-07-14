using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>改装出来的枪，玩家必须真的装得上——而且吃的是改装数值，不是原版数值。</b>
///
/// <para><b>这里守的是什么</b>：改装武器的名字（"步枪（刺刀型）"）是**运行时**注册进
/// <see cref="ModdedWeaponRegistry"/> 的，它**永远不在** <see cref="WeaponTable.Arsenal"/> 里。
/// 所以任何"拿武器名换武器"的地方，只要它自己从 <c>Arsenal()</c> 建一份私有字典去查，
/// 改装武器就会**当场解析失败**。</para>
///
/// <para><b>这不是假想的风险，是真发生过的 P0</b>：<c>Pawn.EquipWeapon</c> / <c>EquipWeaponTwoHanded</c>
/// 一度就是这么写的（一个 <c>static readonly WeaponCatalog = WeaponTable.Arsenal().ToDictionary(...)</c>），
/// 结果玩家造了改装台、花了材料和工时改出一把枪，**点「装备」却装不上**——而且报错信息还甩锅给
/// "断肢禁槽 / 持握冲突"，三个原因一个都不是真的。三种枪托型态（利爪/创伤/刺刀）因此**永远进不了战斗**。
/// 尤其致命的是**双手枪**（步枪/狙击/霰弹/冲锋枪全是双手）——它们正是最该装刺刀的那批。</para>
///
/// <para><b>为什么不直接测 <c>Pawn.EquipWeapon</c></b>：<c>Pawn.cs</c> 引了 Godot，没 Link 进测试工程。
/// 所以这里测的是它**逐层调用的那两个组件**（<see cref="ModdedWeaponRegistry.WeaponByName"/> +
/// <see cref="WeaponLoadout.EquipTwoHanded"/>，两者都已 Link），外加一条**源码级架构护栏**
/// （<see cref="Pawn源码不得再自建原厂武器字典"/>）——那条才是真正挡住"私有字典复活"的东西。</para>
/// </summary>
[Collection(ModdedWeaponRegistryCollection.Name)]
public class ModdedWeaponEquipTests
{
    /// <summary>一把**双手**枪（步枪）。双手枪是这个 bug 最致命的受害者。</summary>
    private static Weapon TwoHandedGun()
        => WeaponTable.Arsenal().First(w => w.Name == "步枪");

    private static WeaponMod Bayonet()
        => WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Id == "bayonet");

    /// <summary>造一把「步枪（刺刀型）」并登记，返回它的变体名。</summary>
    private static string RegisterBayonetRifle()
    {
        ModdedWeaponRegistry.Clear();
        ModdedWeapon modded = WeaponMods.ApplyMods(TwoHandedGun(), new[] { Bayonet() });
        return ModdedWeaponRegistry.Register(modded);
    }

    // ———————————————————————————— 根因：变体永远不在原厂表里 ————————————————————————————

    /// <summary>
    /// **这条解释了全部**：改装变体**不在** <see cref="WeaponTable.Arsenal"/> 里。
    /// 所以任何自建 <c>Arsenal().ToDictionary()</c> 私有字典的回查点，对改装武器**必然落空**。
    /// </summary>
    [Fact]
    public void 改装变体永远不在原厂武器表里()
    {
        string variant = RegisterBayonetRifle();

        Assert.DoesNotContain(WeaponTable.Arsenal(), w => w.Name == variant);

        // 而唯一正确的入口查得到它（先原厂表、后改装表）。
        Assert.NotNull(ModdedWeaponRegistry.WeaponByName(variant));
        Assert.NotNull(ModdedWeaponRegistry.WeaponByName("步枪"));      // 原厂武器照样查得到
        Assert.Null(ModdedWeaponRegistry.WeaponByName("步枪（不存在的改装）"));
    }

    // ———————————————————————————— 装得上：走 Pawn 真实的那条组合 ————————————————————————————

    /// <summary>
    /// <b>改装后的双手枪必须装得上。</b>复现 <c>Pawn.EquipWeaponTwoHanded</c> 的真实调用链：
    /// 按名回查 → 双手持械。任一环断掉，玩家改出来的枪就是一块砖。
    /// </summary>
    [Fact]
    public void 改装后的双手枪必须装得上()
    {
        string variant = RegisterBayonetRifle();

        // ① Pawn 的第一步：按名回查（它现在走 ModdedWeaponRegistry.WeaponByName）
        Weapon? w = ModdedWeaponRegistry.WeaponByName(variant);
        Assert.NotNull(w);
        Assert.True(w!.TwoHanded, "步枪是双手武器——改装不该把它变成单手");

        // ② Pawn 的第二步：双手持械
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipTwoHanded(w), "改装后的双手枪必须能双手握住");
    }

    /// <summary>
    /// 单手枪（手枪）改装后同样装得上——两条入口都得通。
    /// <para>⚠️ [T47] 从前这里用的是**利爪型**，但用户已把手枪从三种近战型态的白名单里划掉了
    /// （手枪装刺刀/绑消防斧本来就荒诞）⇒ 改用手枪**真装得上**的改装：<b>加长枪管</b>。</para>
    /// </summary>
    [Fact]
    public void 改装后的单手枪也必须装得上()
    {
        ModdedWeaponRegistry.Clear();
        Weapon pistol = WeaponTable.Arsenal().First(x => x.Name == "手枪");
        WeaponMod barrel = WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Id == "extended_barrel");
        Assert.Contains("手枪", barrel.FitsWeapons);

        string variant = ModdedWeaponRegistry.Register(WeaponMods.ApplyMods(pistol, new[] { barrel }));

        Weapon? w = ModdedWeaponRegistry.WeaponByName(variant);
        Assert.NotNull(w);

        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipToHand(w!, Hand.Right));
    }

    // ———————————————————————————— 装上之后吃的是改装数值，不是原版 ————————————————————————————

    /// <summary>
    /// <b>比"装不上"更隐蔽的失败：装上了，但吃的是原版数值（改装白改）。</b>
    /// 刺刀型把枪托近战从**钝击**改成**锐击**，并整个换成「80% 攻速的刺剑」。
    /// 回查回来的那把枪必须带着这些——否则玩家的材料和工时喂了狗。
    /// <para>⚠️ [T47] 口径已换：不再是"在枪托数值上乘系数"，而是**覆盖成刺剑**
    /// ⇒ 单击伤害与原厂枪托**上限持平**（都是 7），赢的是**穿透与出手速度**。故断言改看这两条 + DPS。</para>
    /// </summary>
    [Fact]
    public void 装上之后吃的是改装数值而不是原版数值()
    {
        Weapon baseGun = TwoHandedGun();
        Weapon baseStock = baseGun.MeleeProfile()!;

        string variant = RegisterBayonetRifle();
        Weapon moddedStock = ModdedWeaponRegistry.WeaponByName(variant)!.MeleeProfile()!;

        // 型态：钝击 → 锐击（这是"刺刀"三个字的全部意义）
        Assert.Equal(DamageType.Blunt, baseStock.DamageType);
        Assert.Equal(DamageType.Sharp, moddedStock.DamageType);

        // 数值：穿透提高（原版枪托 0.03 → 刺剑 0.25）、出手更快、每秒伤害更高
        Assert.True(moddedStock.Penetration > baseStock.Penetration,
            $"刺刀型穿透({moddedStock.Penetration})应高于原版({baseStock.Penetration})——否则改装白改");
        Assert.True(moddedStock.AttackInterval < baseStock.AttackInterval,
            $"刺刀型出手({moddedStock.AttackInterval}s)应快于原版枪托({baseStock.AttackInterval}s)");
        Assert.True(WeaponDps.Single(moddedStock) > WeaponDps.Single(baseStock),
            "刺刀型每秒伤害应高于原厂枪托——否则没人会去改");
    }

    // ———————————————————————————— 存档：改装枪读回来还在，且数值不变 ————————————————————————————

    /// <summary>
    /// 改装枪存档只存三个字符串（变体名 / 基础武器名 / 改装名），读档按**当前规则重算**。
    /// 读回来必须还是那把枪——否则玩家存个档，改装就蒸发了。
    /// </summary>
    [Fact]
    public void 改装枪存档读回来还在且数值一致()
    {
        string variant = RegisterBayonetRifle();
        Weapon before = ModdedWeaponRegistry.WeaponByName(variant)!;
        double dmgBefore = before.MeleeProfile()!.DamageMax;
        DamageType typeBefore = before.MeleeProfile()!.DamageType;

        // 模拟存档 → 清空世界 → 读档
        var specs = ModdedWeaponRegistry.Specs.ToList();
        ModdedWeaponRegistry.Clear();
        Assert.Null(ModdedWeaponRegistry.WeaponByName(variant));   // 清空后确实没了（自证）

        ModdedWeaponRegistry.Restore(specs);

        Weapon? after = ModdedWeaponRegistry.WeaponByName(variant);
        Assert.NotNull(after);
        Assert.Equal(dmgBefore, after!.MeleeProfile()!.DamageMax);
        Assert.Equal(typeBefore, after.MeleeProfile()!.DamageType);
    }

    // ———————————————————————————— 架构护栏：别让那个私有字典复活 ————————————————————————————

    /// <summary>
    /// <b>源码级护栏</b>：<c>Pawn.cs</c> 不许再自建一份「原厂武器名 → Weapon」的私有字典。
    ///
    /// <para>为什么要用这么"笨"的办法：<c>Pawn.cs</c> 引了 Godot、没 Link 进测试工程，
    /// 所以**没有任何单测能调到 <c>Pawn.EquipWeapon</c>**。而这正是那个 P0 藏身的地方——
    /// 它当初就是靠一个 <c>static readonly WeaponCatalog = WeaponTable.Arsenal().ToDictionary(...)</c>
    /// 悄悄把改装武器全挡在门外的，而所有单测都是绿的。</para>
    ///
    /// <para>规则很简单：<b>武器名回查只准有一个入口</b>（<see cref="ModdedWeaponRegistry.WeaponByName"/>，
    /// 它内部先查原厂表、后查改装表）。谁再从 <c>Arsenal()</c> 建私有字典，这条就红。</para>
    /// </summary>
    [Fact]
    public void Pawn源码不得再自建原厂武器字典()
    {
        string source = File.ReadAllText(PawnSourcePath());

        // 只看**真代码**，跳过注释行 —— Pawn.cs 里有一句刻意留下的"墓碑注释"，
        // 记着这个字典曾经存在、为什么被删（那段文字里就带着 Arsenal().ToDictionary 这几个字）。
        // 那句注释是**资产**，不是违规：它拦住下一个想把字典加回来的人。别让它把自己的护栏搞红。
        string[] codeLines = source
            .Split('\n')
            .Where(l =>
            {
                string t = l.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("///");
            })
            .ToArray();
        string code = string.Join("\n", codeLines);

        Assert.False(
            code.Contains("Arsenal().ToDictionary"),
            "Pawn.cs 又自建了一份原厂武器字典 —— 改装武器（\"步枪（刺刀型）\"）不在 Arsenal() 里，" +
            "这么写会让玩家改出来的枪装不上（这个 P0 已经发生过一次）。" +
            "武器名回查只准走 ModdedWeaponRegistry.WeaponByName（先原厂表、后改装表）。");

        // 两条装备入口都必须走唯一入口。
        int hits = code.Split("ModdedWeaponRegistry.WeaponByName").Length - 1;
        Assert.True(hits >= 2,
            $"Pawn.cs 里 ModdedWeaponRegistry.WeaponByName 只出现 {hits} 次——" +
            "单手(EquipWeapon)与双手(EquipWeaponTwoHanded)两条入口都必须走它，漏一条改装枪就装不上。");
    }

    /// <summary>从本测试源文件往上找仓库根，定位 <c>godot/scripts/Pawn.cs</c>。</summary>
    private static string PawnSourcePath([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "godot", "scripts", "Pawn.cs")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "找不到 godot/scripts/Pawn.cs");
        return Path.Combine(dir!.FullName, "godot", "scripts", "Pawn.cs");
    }
}
