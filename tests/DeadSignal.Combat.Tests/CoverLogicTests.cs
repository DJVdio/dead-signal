using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 半身掩体（桌子/椅子/沙袋）：远程命中判定成立后，按几率整发无效。
///
/// 用户口径（原话）：「当躲在半身掩体后，被远程攻击时，会有 25% 的无伤概率。逻辑上来说是敌人的子弹
/// 射在掩体上了，但是这样不好做，游戏的表现是这一下射中了人，但是判定 25% 几率无效。」
/// ⇒ 不做弹道碰撞，只在承伤入口掷点。方向性：掩体须落在射击者与目标之间（"子弹射在掩体上"的前提）。
/// </summary>
public class CoverLogicTests
{
    // 一张 60×40 的桌子，左上角 (100,100) → 覆盖 x∈[100,160], y∈[100,140]。
    private static HalfCover Table() => HalfCover.FromRect(100, 100, 60, 40, CoverLogic.DefaultCoverChance);

    // ── 方向性判定 ─────────────────────────────────────────────

    [Fact]
    public void 掩体在射击者与目标之间_目标贴着掩体_受保护()
    {
        // 目标贴在桌子南侧（y=155，桌子南边 y=140，距 15px），射击者在正北（桌子另一侧）。
        // 连线 (130,20) → (130,155) 纵穿桌子 ⇒ 子弹得先经过桌子。
        Assert.True(CoverLogic.Protects(Table(), shooter: new Vector2(130, 20), target: new Vector2(130, 155)));
    }

    [Fact]
    public void 射击者绕到目标背后_掩体白躲()
    {
        // 同一目标（桌子南侧 155），射击者绕到南边 (130, 300)：连线 (130,300)→(130,155) 压根不碰桌子。
        // 这正是包抄的意义——绕后就是为了绕掉掩体。
        Assert.False(CoverLogic.Protects(Table(), shooter: new Vector2(130, 300), target: new Vector2(130, 155)));
    }

    [Fact]
    public void 侧翼射击_掩体不在连线上_不受保护()
    {
        // 射击者在正东，连线 (400,155)→(130,155) 水平走在桌子南边之外（y=155 > 桌底 140），不穿桌。
        Assert.False(CoverLogic.Protects(Table(), shooter: new Vector2(400, 155), target: new Vector2(130, 155)));
    }

    [Fact]
    public void 目标离掩体太远_即使连线穿过也不受保护()
    {
        // 目标在桌南 200px 处（远超贴身半径）：子弹确实会先过桌子，但「躲在掩体后」要求人贴着它——
        // 站在旷野中间隔着一张远处的桌子不算掩体（也让"我在掩体后"的可见性提示成为可能）。
        Assert.False(CoverLogic.Protects(Table(), shooter: new Vector2(130, 20), target: new Vector2(130, 340)));
    }

    [Fact]
    public void 目标站在掩体上_不算躲在掩体后()
    {
        // 目标中心落在桌子矩形内（如坐在椅子上）：任何方向的连线都会穿过矩形 → 若不排除就是 360° 无死角掩体。
        Assert.False(CoverLogic.Protects(Table(), shooter: new Vector2(130, 20), target: new Vector2(130, 120)));
    }

    [Fact]
    public void 隔着掩体对射_双方各自贴着桌子两侧_都受保护()
    {
        Vector2 north = new(130, 85);  // 贴桌北侧
        Vector2 south = new(130, 155); // 贴桌南侧
        Assert.True(CoverLogic.Protects(Table(), shooter: north, target: south));
        Assert.True(CoverLogic.Protects(Table(), shooter: south, target: north)); // 对称：桌子同时保护两边
    }

    // ── 场上多掩体 ─────────────────────────────────────────────

    [Fact]
    public void 多掩体取最高几率_无一保护则为零()
    {
        var covers = new List<HalfCover>
        {
            HalfCover.FromRect(100, 100, 60, 40, 0.25f), // 挡在连线上
            HalfCover.FromRect(600, 600, 60, 40, 0.40f), // 远在天边，不挡
        };
        Vector2 shooter = new(130, 20), target = new(130, 155);
        Assert.Equal(0.25f, CoverLogic.CoverChanceFor(covers, shooter, target), 3);

        // 再叠一个同样挡在连线上、也贴着目标、但几率更高的沙袋 → 取最高（不叠加）。
        covers.Add(HalfCover.FromRect(115, 142, 30, 8, 0.35f)); // 贴在目标(130,155)身前 5px，压在连线 x=130 上
        Assert.Equal(0.35f, CoverLogic.CoverChanceFor(covers, shooter, target), 3);

        // 射击者绕后 → 一个都不保护。
        Assert.Equal(0f, CoverLogic.CoverChanceFor(covers, new Vector2(130, 300), target), 3);
    }

    [Fact]
    public void 空掩体场_几率为零()
    {
        Assert.Equal(0f, CoverLogic.CoverChanceFor(new List<HalfCover>(), Vector2.Zero, new Vector2(10, 10)), 3);
    }

    // ── 掷点：只对远程生效、零漂移 ──────────────────────────────

    [Fact]
    public void 远程_掷点低于几率_整发无效()
    {
        var rng = new SequenceRandomSource(new[] { 0.10 }); // < 0.25
        Assert.True(CoverLogic.Negates(ranged: true, coverChance: 0.25f, rng));
    }

    [Fact]
    public void 远程_掷点高于几率_照常受伤()
    {
        var rng = new SequenceRandomSource(new[] { 0.30 }); // ≥ 0.25
        Assert.False(CoverLogic.Negates(ranged: true, coverChance: 0.25f, rng));
    }

    [Fact]
    public void 近战不吃掩体_贴身砍你桌子挡不住_且不掷点()
    {
        // 序列只备了一个 0.01（必中无效的低值）：若近战错误地掷了点，就会返回 true。
        var rng = new SequenceRandomSource(new[] { 0.01 });
        Assert.False(CoverLogic.Negates(ranged: false, coverChance: 0.25f, rng));
    }

    [Fact]
    public void 无掩体_不消耗随机流_零漂移()
    {
        // 关键回归护栏：coverChance=0（场上无掩体/绕后）时**一个数都不能从随机源里取**，
        // 否则既有 Sim 基线与所有确定性单测的随机流全部错位。
        var rng = new SequenceRandomSource(new[] { 0.01, 0.02 });
        Assert.False(CoverLogic.Negates(ranged: true, coverChance: 0f, rng));
        // 序列一个都没被吃掉 → 下一次取仍是 0.01。
        Assert.Equal(0.01, rng.Range(0.0, 1.0), 6);
    }

    [Fact]
    public void 掷点走可注入随机源_序列可复现()
    {
        var rng = new SequenceRandomSource(new[] { 0.24, 0.26, 0.00 });
        Assert.True(CoverLogic.Negates(true, 0.25f, rng));   // 0.24 < 0.25
        Assert.False(CoverLogic.Negates(true, 0.25f, rng));  // 0.26 ≥ 0.25
        Assert.True(CoverLogic.Negates(true, 0.25f, rng));   // 0.00 < 0.25
    }

    // ── 双向对称（敌人也受掩体保护）─────────────────────────────

    [Fact]
    public void 掩体双向对称_玩家朝躲在桌后的劫掠者开枪也吃无效()
    {
        // 纯函数不认阵营：谁贴着掩体、谁在对面开枪，只看几何。
        Vector2 raiderBehindTable = new(130, 155); // 劫掠者贴在桌南
        Vector2 playerShooting = new(130, 20);     // 玩家在桌北开枪
        Assert.True(CoverLogic.Protects(Table(), playerShooting, raiderBehindTable));

        var rng = new SequenceRandomSource(new[] { 0.10 });
        float chance = CoverLogic.CoverChanceFor(new[] { Table() }, playerShooting, raiderBehindTable);
        Assert.True(CoverLogic.Negates(ranged: true, coverChance: chance, rng)); // 敌人一样白挨一枪
    }

    // ── 可见性：玩家怎么知道自己在掩体后 ────────────────────────

    [Fact]
    public void 贴着掩体即可被高亮_不看射击方向()
    {
        var covers = new[] { Table() };
        // 贴在桌南 → 有可用掩体（表现层据此高亮桌子 + 脚下盾标）。
        Assert.NotNull(CoverLogic.AdjacentCover(covers, new Vector2(130, 155)));
        // 站远了 → 没有。
        Assert.Null(CoverLogic.AdjacentCover(covers, new Vector2(130, 340)));
        // 站在桌子上 → 不算（与 Protects 口径一致，不误导玩家）。
        Assert.Null(CoverLogic.AdjacentCover(covers, new Vector2(130, 120)));
    }

    // ── 掩体场注册表 ───────────────────────────────────────────

    [Fact]
    public void 掩体场_登记与清空()
    {
        var field = new CoverField();
        Assert.Equal(0f, field.ChanceFor(new Vector2(130, 20), new Vector2(130, 155)), 3);

        field.Add(Table());
        Assert.Equal(0.25f, field.ChanceFor(new Vector2(130, 20), new Vector2(130, 155)), 3);
        Assert.NotNull(field.AdjacentTo(new Vector2(130, 155)));

        field.Clear(); // 换关/重载：不能残留上一关的掩体
        Assert.Equal(0f, field.ChanceFor(new Vector2(130, 20), new Vector2(130, 155)), 3);
        Assert.Null(field.AdjacentTo(new Vector2(130, 155)));
    }

    // ── 表现层：打中了但没伤到 ─────────────────────────────────

    [Fact]
    public void 掩体飘字_说的是打中了但无效_不是未命中()
    {
        MoteText mote = CombatMoteText.BuildCoverNegated();
        Assert.Contains("掩体", mote.Text);
        // 与"被甲挡下"（叮·部位挡下）区分，也不是落空/Miss。
        Assert.DoesNotContain("落空", mote.Text);
        Assert.DoesNotContain("未命中", mote.Text);
        // 中性色（既非钝黄也非锐红——没有伤害发生）。
        Assert.Equal(CombatMoteText.CoverColor.R, mote.Color.R, 3);
        Assert.Equal(CombatMoteText.CoverColor.G, mote.Color.G, 3);
        Assert.Equal(CombatMoteText.CoverColor.B, mote.Color.B, 3);
    }
}
