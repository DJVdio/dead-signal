using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 端到端：直接吃 <c>godot/data/camp.json</c> 的**真实 props**，按 CampMain 同样的规则灌进 CoverField，
/// 在**营地真实坐标**上验证掩体判定。防的是"单测坐标里都对、真营地里 25% 从不触发"这种空转。
/// </summary>
public class RealCampCoverTests
{
    /// <summary>从测试程序集向上找仓库根，定位 <c>godot/data/camp.json</c>（不写死绝对路径/工作目录）。</summary>
    private static string CampJsonPath()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "godot", "data", "camp.json");
            if (File.Exists(p))
                return p;
        }
        throw new FileNotFoundException("从测试程序集向上未找到 godot/data/camp.json");
    }

    private sealed class Prop
    {
        public string? name { get; set; }
        public string? role { get; set; }
        public bool cover { get; set; }
        public double[]? rect { get; set; }
    }

    private sealed class Cfg
    {
        public Prop[]? props { get; set; }
    }

    /// <summary>照 CampMain 的口径：cover:true 的 prop 登记进掩体场（其余一律是实心全身遮挡，不进）。</summary>
    private static (CoverField field, List<string> names) LoadCamp()
    {
        Cfg cfg = JsonSerializer.Deserialize<Cfg>(File.ReadAllText(CampJsonPath()))!;
        var field = new CoverField();
        var names = new List<string>();
        foreach (Prop p in cfg.props!)
        {
            if (!p.cover || p.rect is not { Length: 4 })
                continue;
            field.Add((float)p.rect[0], (float)p.rect[1], (float)p.rect[2], (float)p.rect[3]);
            names.Add(p.name!);
        }
        return (field, names);
    }

    [Fact]
    public void 真实camp_json_读出七处半身掩体_且实心家具一处都没混进来()
    {
        (CoverField field, List<string> names) = LoadCamp();

        Assert.Equal(7, field.Covers.Count);
        Assert.Contains("北门沙袋垒A", names);
        Assert.Contains("南门沙袋垒B", names);
        Assert.Contains("住宅-座椅A", names);

        // 实心物（墙层：断视线+挡子弹+挖导航洞）绝不能混进掩体场——那 25% 会是永不触发的死代码。
        Assert.DoesNotContain("工作台", names);
        Assert.DoesNotContain("住宅-柜子", names);
        Assert.DoesNotContain("牛棚-草垛A", names);

        // 掩体几率一律是拍板的 25%。
        foreach (HalfCover c in field.Covers)
            Assert.Equal(0.25f, c.Chance, 3);
    }

    [Fact]
    public void 南门守卫躲沙袋后_挨门外来敌的枪_受掩体保护()
    {
        (CoverField field, _) = LoadCamp();

        // 南门沙袋垒A = [1090, 1394, 92, 26] → 覆盖 x∈[1090,1182], y∈[1394,1420]。南门在 y=1478，
        // 营地内部是 y 更小的一侧 ⇒ 守卫站沙袋**北**侧（营内），沙袋横在他与门之间。
        Vector2 guard = new(1136, 1370);        // 贴在沙袋北侧，距沙袋 24px
        Vector2 zombieAtGate = new(1150, 1490); // 丧尸刚破南门进来，在沙袋另一侧

        // 敌人在门外/门口 → 沙袋正好横在两者之间 ⇒ 受保护。
        Assert.Equal(0.25f, field.ChanceFor(shooter: zombieAtGate, target: guard), 3);

        // 守卫头顶该出现"掩"标记（StatusIconStrip 查的就是这个）。
        Assert.NotNull(field.AdjacentTo(guard));
    }

    [Fact]
    public void 劫掠者绕到守卫背后开枪_沙袋白躲()
    {
        (CoverField field, _) = LoadCamp();

        Vector2 guard = new(1136, 1370);        // 同一名守卫，仍贴着南门沙袋
        Vector2 flanker = new(1136, 1290);      // 劫掠者包抄到守卫**背后**（更靠营内）——沙袋这下在守卫身前，白摆了

        Assert.Equal(0f, field.ChanceFor(shooter: flanker, target: guard), 3);

        // 但"贴着掩体"这个状态本身还在（头顶仍有"掩"）——提示的是"你有掩体可用"，
        // 不承诺"这一枪一定挡得住"。绕后的代价由方向性判定兑现。
        Assert.NotNull(field.AdjacentTo(guard));
    }

    [Fact]
    public void 掩体双向对称_劫掠者躲在北门沙袋后_玩家从门外打它也吃百分之二十五()
    {
        (CoverField field, _) = LoadCamp();

        // 北门沙袋垒B = [1220, 386, 92, 26] → x∈[1220,1312], y∈[386,412]。北门在 y=300。
        Vector2 raider = new(1266, 432);   // 劫掠者已进营，贴在沙袋南侧（营内）
        Vector2 player = new(1266, 330);   // 玩家在北门口（沙袋另一侧）朝它开枪

        Assert.Equal(0.25f, field.ChanceFor(shooter: player, target: raider), 3);

        // 掷点也一样吃（同一个纯函数、同一条随机源）。
        var rng = new SequenceRandomSource(new[] { 0.10 });
        Assert.True(CoverLogic.Negates(ranged: true, field.ChanceFor(player, raider), rng));
    }

    [Fact]
    public void 站在营地空地上_四下无掩体_不消耗随机流()
    {
        (CoverField field, _) = LoadCamp();

        Vector2 sam = new(700, 900);   // 山姆开局出生点（camp.json spawns），周围没有掩体
        Vector2 attacker = new(700, 700);

        Assert.Equal(0f, field.ChanceFor(attacker, sam), 3);
        Assert.Null(field.AdjacentTo(sam));

        // 零漂移：无掩体 ⇒ 掷点函数一个随机数都不取。
        var rng = new SequenceRandomSource(new[] { 0.01, 0.02 });
        Assert.False(CoverLogic.Negates(true, field.ChanceFor(attacker, sam), rng));
        Assert.Equal(0.01, rng.Range(0.0, 1.0), 6); // 序列没被动过
    }
}
