using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【T53】🔴 **实机口径 == Sim 口径** 的焊死护栏 —— 本单的**根因**就是这两份数字静默漂开了。
///
/// <para>
/// 出事经过：<c>DuelConfig</c>（Sim）写死 储血 70 / 每伤口 1.5，而 <b>Godot 运行时从不设置流血口径</b>
/// （全仓 grep <c>BleedRatePerWound</c>/<c>SetBloodMax</c> 在 <c>godot/scripts</c> 命中 <b>0 次</b>）
/// ⇒ 实机跑 <see cref="Body"/> 的字段默认值 储血 100 / 每伤口 <b>0.55</b>。
/// **Sim 的流血比实机热 3.9 倍**（放干一个致命伤口 46.7s vs 181.8s）。
/// 后果：实机口径下「锯齿剑刃 vs 丧尸」的流血致死占比是 <b>0.0%</b> —— 流血改装件在实机里等于没做。
/// </para>
/// <para>
/// 现在两边**都从 <see cref="BleedModel"/> 读同一个常量**，物理上不可能再分叉。
/// 本文件钉死这条：谁再把其中一份改成别的数，这里当场红。
/// </para>
/// </summary>
public class BleedConfigParityTests
{
    [Fact]
    public void Runtime_body_defaults_match_the_sim_duel_config()
    {
        var runtimeBody = HumanBody.NewBody(); // 实机走的就是这条（Godot 层不设任何流血参数）
        var simConfig = new DuelConfig();      // Sim/Duel 走的是这条

        Assert.Equal(simConfig.BloodMax, runtimeBody.BloodMax);
        Assert.Equal(simConfig.BleedRatePerWound, runtimeBody.BleedRatePerWound);
    }

    [Fact]
    public void Both_layers_read_the_single_source_of_truth()
    {
        var body = HumanBody.NewBody();
        var cfg = new DuelConfig();

        Assert.Equal(BleedModel.DefaultBloodMax, body.BloodMax);
        Assert.Equal(BleedModel.DefaultBleedRatePerWound, body.BleedRatePerWound);
        Assert.Equal(BleedModel.DefaultBloodMax, cfg.BloodMax);
        Assert.Equal(BleedModel.DefaultBleedRatePerWound, cfg.BleedRatePerWound);
    }

    [Fact]
    public void The_shipping_values_are_the_ones_the_game_actually_runs()
    {
        // 🔴 [T53 二次拍板] 用户否决了"实机对齐到 Sim"（原话「不对齐了」）——口径回退到游戏一直在跑的 100 / 0.55。
        // 起因：70/1.5 的热口径下丧尸围攻是断崖（2 只 16.6%、3 只 0.8%、4 只 0%），用户不接受"两只丧尸就是死局"。
        // 这条钉死数值本身；上面两条钉死"两边不许再漂开"的结构。
        Assert.Equal(100, BleedModel.DefaultBloodMax);
        Assert.Equal(0.55, BleedModel.DefaultBleedRatePerWound);
    }

    [Fact]
    public void Blood_regen_is_derived_from_the_pool_so_seven_day_refill_cannot_silently_drift()
    {
        // 回血速率**由储血上限推导**，不写死 —— [T53] 储血上限已经被改过三次（100→70→100），
        // 若回血写死 10/昼夜，回满就会从 7 昼夜变成 10 昼夜而没人发现。
        Assert.Equal(
            BleedModel.DefaultBloodMax / BleedModel.FullBloodRefillDays,
            BleedModel.BloodRegenPerRestDay,
            9);
        Assert.Equal(7.0, BleedModel.DefaultBloodMax / BleedModel.BloodRegenPerRestDay, 9);
    }

    [Fact]
    public void A_lethal_wound_bleeds_out_in_the_same_time_in_both_layers()
    {
        // 端到端口径：同一处躯干伤口，实机与 Sim 放干耗时必须**一模一样**。
        static double BleedOutSeconds(Body b)
        {
            b.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
            double t = 0;
            while (!b.IsDead && t < 10_000)
            {
                b.TickBleed(0.1);
                t += 0.1;
            }

            return t;
        }

        var runtime = HumanBody.NewBody();

        var sim = HumanBody.NewBody();
        var cfg = new DuelConfig();
        sim.BleedRatePerWound = cfg.BleedRatePerWound; // DuelEngine.Init 做的就是这两件事
        sim.SetBloodMax(cfg.BloodMax);

        Assert.Equal(BleedOutSeconds(sim), BleedOutSeconds(runtime), 6);
    }
}

/// <summary>【T53】两条流血改装件真的接上了引擎轴（此前它们只有非流血的那一半生效）。</summary>
public class BleedModWiringTests
{
    [Fact]
    public void Serrated_blade_cuts_penetration_multiplicatively_and_raises_bleed_by_40pct()
    {
        Weapon baseW = WeaponTable.Longsword();
        Weapon modded = WeaponMods.ApplyMods(baseW, new[] { WeaponModCatalog.SerratedBlade() });

        // 穿透 −20% 是**乘算**（用户口径：20% → 18%，不是 20% − 20% = 0%）
        Assert.Equal(baseW.Penetration * 0.80, modded.Penetration, 6);
        Assert.Equal(0.192, modded.Penetration, 6); // 长剑 24% → 19.2%

        // 流血速度 +40%
        Assert.Equal(1.40, modded.BleedRateMultiplier, 6);
    }

    [Fact]
    public void Serrated_blade_is_no_longer_a_pure_downside()
    {
        // impl-weaponmod 留的信号：流血轴落地前，锯齿剑刃只有 −20% 穿透 ⇒ **纯负收益，没人会造它**。
        // 现在它必须有一条**正收益**。
        Weapon modded = WeaponMods.ApplyMods(WeaponTable.Longsword(), new[] { WeaponModCatalog.SerratedBlade() });
        Assert.True(modded.BleedRateMultiplier > 1.0, "锯齿剑刃仍是纯负收益——流血轴没接上。");
    }

    [Fact]
    public void Nail_studs_add_penetration_additively_the_zero_trap()
    {
        Weapon baseClub = WeaponTable.Club();
        Assert.Equal(0, baseClub.Penetration); // 棍棒本来 0 穿透 —— 这就是"零陷阱"的前提

        Weapon modded = WeaponMods.ApplyMods(baseClub, new[] { WeaponModCatalog.NailStuds() });

        // 🔴 加算。若谁把它改成乘算：0 × 1.03 = 0 ⇒ 棍棒穿透静默变回 0，改装变废件。
        Assert.Equal(0.03, modded.Penetration, 6);
        Assert.True(modded.Penetration > 0, "钉子强化的穿透被乘算吃掉了——零陷阱，必须加算。");
    }

    [Fact]
    public void Nail_studs_give_the_club_its_only_source_of_bleeding()
    {
        Weapon baseClub = WeaponTable.Club();
        Assert.Equal(0, baseClub.BleedOnHitChance); // 钝器本来一处伤口都造不出来（流血资格要求锐器抵达）

        Weapon modded = WeaponMods.ApplyMods(baseClub, new[] { WeaponModCatalog.NailStuds() });
        Assert.Equal(0.25, modded.BleedOnHitChance, 6);
    }

    [Fact]
    public void Penetration_cap_still_holds_at_100pct()
    {
        // 用户拍板的全局上限。锯齿是 ×0.8（只会降），但护栏照钉。
        Weapon modded = WeaponMods.ApplyMods(WeaponTable.Club(), new[] { WeaponModCatalog.NailStuds() });
        Assert.InRange(modded.Penetration, 0, 1);
    }
}

/// <summary>
/// 【T53】休养自然回血 —— **跑通两层**的护栏（用户拍板：「补——休养自然回血」）。
///
/// <para>
/// 项目长期教训 <c>pure-logic-green-not-wired</c>：规则层绿但消费层没登记 = 静默失效。
/// 这里两层都验：① 规则层 <see cref="BloodRecovery.PerRestDay"/> 的判据；
/// ② 引擎层 <see cref="Body.RecoverBlood"/> 真的把血加回去了（<c>Pawn</c> 只是三行调用方，
/// 它是 Godot 节点无法单测，所以规则**故意**没写在它身体里）。
/// </para>
/// </summary>
public class BloodRecoveryTests
{
    [Fact]
    public void Resting_actually_puts_blood_back_into_the_body()
    {
        var body = HumanBody.NewBody();
        body.LoseBlood(40);
        double low = body.Blood;

        // Pawn.AdvanceHealthDay 里那一步的逐字复刻（伤口已缝合 ⇒ hasOpenWound = false）
        double regen = BloodRecovery.PerRestDay(restFraction: 1.0, bedFraction: 1.0, hasOpenWound: false);
        body.RecoverBlood(regen);

        Assert.True(body.Blood > low, "休养了一整昼夜，血却没回来 —— 回血链路没接上。");
        Assert.Equal(low + regen, body.Blood, 6);
    }

    [Fact]
    public void An_open_wound_blocks_recovery_you_must_get_surgery_first()
    {
        // 用户口径：「任何时候只要伤口没被手术治疗就会流血」⇒ 还在流的时候不许回血（否则规则被架空）。
        Assert.Equal(0, BloodRecovery.PerRestDay(1.0, 1.0, hasOpenWound: true));
    }

    [Fact]
    public void Working_survivors_do_not_recover_blood()
    {
        Assert.Equal(0, BloodRecovery.PerRestDay(restFraction: 0, bedFraction: 0, hasOpenWound: false));
    }

    [Fact]
    public void Bed_beats_a_floor_mat()
    {
        double bed = BloodRecovery.PerRestDay(1.0, bedFraction: 1.0, hasOpenWound: false);
        double mat = BloodRecovery.PerRestDay(1.0, bedFraction: 0.0, hasOpenWound: false);
        Assert.True(bed > mat);
    }

    [Fact]
    public void Full_refill_from_empty_takes_about_seven_days_same_order_as_a_fracture()
    {
        // 数值依据（写进 journal）：70 储血 ÷ 10 每昼夜 = 7 昼夜从零回满，与「骨折愈合 7 昼夜」同量级。
        var body = HumanBody.NewBody();
        body.LoseBlood(body.BloodMax - 1); // 几乎放干（留 1 点，别真死）

        int days = 0;
        while (body.BloodRatio < 1.0 && days < 100)
        {
            body.RecoverBlood(BloodRecovery.PerRestDay(1.0, 0.0, hasOpenWound: false)); // 地铺，不吃床加成
            days++;
        }

        Assert.InRange(days, 6, 8);
    }

    [Fact]
    public void Recovery_never_overfills_and_never_revives_the_dead()
    {
        var healthy = HumanBody.NewBody();
        Assert.Equal(0, healthy.RecoverBlood(999)); // 已满：加不进去
        Assert.Equal(healthy.BloodMax, healthy.Blood);

        var dead = HumanBody.NewBody();
        dead.LoseBlood(dead.BloodMax); // 出血致死
        Assert.True(dead.IsDead);
        Assert.Equal(0, dead.RecoverBlood(50)); // 回血不是复活术
        Assert.True(dead.IsDead);
    }
}
