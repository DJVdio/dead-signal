using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次25·T47：**穿透 ≤ 100%**（用户拍板：「穿透不能超过 100%」）+ **穿透一律乘算**（钉子强化是唯一例外）。
///
/// <para>
/// 为什么需要一组独立的护栏：改装的穿透是**乘算**的（锋刃研磨 ×1.75、加长枪管 ×1.10），
/// 而穿透在引擎里是 0~1 的比例。乘算天然会顶穿 1.0 —— 全靠
/// <c>WeaponMods.WeaponDraft.Build</c> 的 <c>Clamp(0, 1)</c> 这一处兜住。
/// 那个 Clamp 是**单点故障**：谁手一贱把它拿掉，护甲系统当场失效（穿透 &gt; 100% ⇒ 无视一切护甲）。
/// </para>
/// </summary>
public class WeaponModPenetrationCapTests
{
    /// <summary>
    /// 🔴 <b>全局上限：叠满改装也不许超过 100%。</b>
    /// 用一条**故意越界**的合成改装（穿透 ×10）去撞上限 —— 它必须被夹在 1.0，而不是变成 250%。
    /// <para>这条是**结构性**的：它证明 Clamp 存在且生效，而不是"恰好现有数值没超"。</para>
    /// </summary>
    [Fact]
    public void 穿透乘到爆表也会被夹在百分之百()
    {
        var absurd = new WeaponMod
        {
            Id = "test_absurd",
            Name = "荒谬研磨",
            FitsWeapons = new[] { "重剑" }.ToHashSet(),
            Part = WeaponPart.Blade,
            Stats = new[] { StatMod.Mul(WeaponStat.Penetration, 10.0) },   // ×10 ⇒ 重剑 0.40 → 4.0
        };

        Weapon w = WeaponMods.ApplyMods(WeaponTable.Greatsword(), new[] { absurd }).Weapon;

        Assert.Equal(1.0, w.Penetration, 9);   // 夹住了，不是 4.0
        Assert.True(w.Penetration <= 1.0);
    }

    /// <summary>枪托近战的穿透（三种近战型态走的是这条）同样有 100% 上限 —— 两个字段要各夹各的。</summary>
    [Fact]
    public void 枪托近战的穿透同样被夹在百分之百()
    {
        var absurd = new WeaponMod
        {
            Id = "test_absurd_stock",
            Name = "荒谬刺刀",
            FitsWeapons = new[] { "步枪" }.ToHashSet(),
            Part = WeaponPart.Muzzle,
            Form = MeleeForm.Bayonet,
            Stats = new[] { StatMod.Set(WeaponStat.StockMeleePenetration, 3.0) },
        };

        Weapon w = WeaponMods.ApplyMods(WeaponTable.Rifle(), new[] { absurd }).Weapon;

        Assert.Equal(1.0, w.MeleeProfile()!.Penetration, 9);
    }

    /// <summary>
    /// **目录里真实存在的每一种合法组合**，穿透都在 [0, 1] 内（含枪托）。
    /// 逐把武器 × 逐条改装扫一遍 —— 用户日后在 wiki 上把某个穿透加成调大，这里会替他兜住并报警。
    /// </summary>
    [Fact]
    public void 全表每把武器每条改装_穿透都不越界()
    {
        foreach (Weapon w in WeaponModCatalog.AllModdableWeapons())
        {
            foreach (WeaponMod mod in WeaponModCatalog.For(w))
            {
                Weapon modded = WeaponMods.ApplyMods(w, new[] { mod }).Weapon;

                Assert.InRange(modded.Penetration, 0.0, 1.0);
                if (modded.MeleeProfile() is { } stock)
                {
                    Assert.InRange(stock.Penetration, 0.0, 1.0);
                }
            }
        }
    }

    // ══════════════ 乘算 vs 加算：唯一的例外必须是钉子强化 ══════════════

    /// <summary>
    /// 🔴 <b>「穿透 −10%」= 在原本数值上乘 0.9</b>（用户原话：「例如 20% 变成 18%」），
    /// <b>不是</b>绝对减掉 10 个百分点。截短枪管（−15%）是这条口径的实例。
    /// </summary>
    [Fact]
    public void 穿透减百分之十五_是乘算不是绝对减十五个点()
    {
        Weapon rifle = WeaponTable.Rifle();
        Weapon sawn = WeaponMods.ApplyMods(rifle, new[] { WeaponModCatalog.SawnOffBarrel() }).Weapon;

        Assert.Equal(rifle.Penetration * 0.85, sawn.Penetration, 9);          // 乘算
        Assert.NotEqual(rifle.Penetration - 0.15, sawn.Penetration, 6);       // 不是加算
        Assert.True(sawn.Penetration > 0, "乘算永远不会把一个正穿透打成 0（加算会）");
    }

    /// <summary>
    /// 🔴🔴 <b>钉子强化的 +0.03 是全项目乘算铁律的【唯一例外】—— 而且它必须是例外。</b>
    ///
    /// <para>用户原话：「**钉子强化：穿透 +0.03 是因为棍棒原本是 0 穿透**」。
    /// <b>零陷阱</b>：棍棒穿透 = 0，乘算在零上永远是零（<c>0 × 1.75 = 0</c>）。
    /// 谁把它改成 <c>Mul</c>，这条改装当场变成一件废件 —— 本测试会红。</para>
    /// </summary>
    [Fact]
    public void 钉子强化必须是加算_否则在棍棒的零穿透上永远是零()
    {
        Weapon club = WeaponTable.Club();
        Assert.Equal(0.0, club.Penetration, 9);   // 前提：棍棒穿透本来就是 0（这就是零陷阱的成因）

        WeaponMod nails = WeaponModCatalog.NailStuds();
        StatMod pen = Assert.Single(nails.Stats, s => s.Stat == WeaponStat.Penetration);

        Assert.Equal(StatOp.Add, pen.Op);   // ← 必须是 Add。改成 Mul 这里立刻红
        Assert.Equal(0.03, pen.Value, 9);

        Weapon studded = WeaponMods.ApplyMods(club, new[] { nails }).Weapon;
        Assert.Equal(0.03, studded.Penetration, 9);
        Assert.True(studded.Penetration > club.Penetration, "钉尖必须真的能破一点甲，否则这条改装白装");
    }

    /// <summary>
    /// **除钉子强化外，全表所有穿透改动都必须是乘算**（CLAUDE.md 铁律）。
    /// 谁又新加一条加算的穿透，这里会红并逼他说明理由（零陷阱是唯一站得住的理由）。
    /// </summary>
    [Fact]
    public void 除钉子强化外_全表穿透改动一律乘算()
    {
        foreach (WeaponMod mod in WeaponModCatalog.All())
        {
            foreach (StatMod s in mod.Stats.Where(s => s.Stat == WeaponStat.Penetration))
            {
                if (mod.Id == "nail_studs")
                {
                    Assert.Equal(StatOp.Add, s.Op);   // 唯一例外（零陷阱，用户点名）
                    continue;
                }

                Assert.True(s.Op == StatOp.Mul,
                    $"改装「{mod.Name}」的穿透改动用了 {s.Op} —— 百分比一律乘算（CLAUDE.md 铁律）。" +
                    "唯一的例外是钉子强化（棍棒穿透为 0，乘算在零上永远是零）；" +
                    "你若确有第二个零陷阱，请在这里加白名单并写清理由。");
            }
        }
    }
}
