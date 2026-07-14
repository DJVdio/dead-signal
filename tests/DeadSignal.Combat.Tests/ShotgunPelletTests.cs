using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 多弹丸（pellet）建模 + 自制霰弹枪。用户口径（原话）：
/// 「霰弹枪采用 8 颗弹丸单独计算的 10% 穿透力武器，射程较短，伤害衰减严重，锥形扩散较大」。
///
/// 「单独计算」= 每颗弹丸走**完整独立**判定链：独立选命中部位 → 独立过护甲三段判定 → 独立结算伤害。
/// 8 颗可打中不同部位、各自被挡/半伤/全伤，穿透逐颗生效。**不是**"一次伤害 ×8"。
///
/// 向后兼容硬要求：<see cref="Weapon.PelletCount"/> 默认 1，单弹丸武器的随机流消耗**位级不变**
/// （否则既有 Sim 基线漂移）。
/// </summary>
public class ShotgunPelletTests
{
    private static BodyPart Chest() => HumanBody.Parts().First(p => p.Name == HumanBody.Chest);

    /// <summary>只有布衣一层（锐防 6 / 钝防 3），穿透 15%（T29 用户手改）→ 挡下门槛 = 6×0.85/2 = 2.55。</summary>
    private static IReadOnlyList<ArmorLayer> Shirt() => new[] { ArmorTable.LongSleeveShirt() };

    // ---- 向后兼容：所有既有武器 PelletCount = 1 ----

    [Fact]
    public void 既有武器全表弹丸数为1()
    {
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            if (w.Name == "自制霰弹枪")
            {
                continue; // 本单新增的多弹丸武器
            }

            Assert.Equal(1, w.PelletCount);
        }

        Assert.Equal(1, WeaponTable.ZombieClaw().PelletCount);
        Assert.Equal(1, new Weapon().PelletCount); // 默认值=1，不填即单弹丸
    }

    /// <summary>
    /// 零漂移锚：单弹丸武器走 <see cref="CombatResolver.ResolveVolley"/> 与直接 <see cref="CombatResolver.Resolve"/>
    /// 消耗**完全相同**的随机序列（同样的 roll 数、同样的取值顺序）。这是既有 Sim 基线不漂移的根因。
    /// </summary>
    [Fact]
    public void 单弹丸走齐射路径与单发路径随机流位级一致()
    {
        Weapon dagger = WeaponTable.Dagger(); // PelletCount=1
        double[] rolls = { 5.0, 2.0 };        // 攻 roll、防 roll（布衣一层）

        var seqA = new SequenceRandomSource(rolls);
        CombatResult single = new CombatResolver(seqA).Resolve(dagger, Shirt(), Chest());

        var seqB = new SequenceRandomSource(rolls);
        VolleyResult volley = new CombatResolver(seqB).ResolveVolley(dagger, Shirt(), () => Chest());

        Assert.Equal(seqA.Remaining, seqB.Remaining); // 消耗的 roll 数一致
        Assert.Single(volley.Pellets);
        Assert.Equal(single.FinalDamage, volley.Pellets[0].FinalDamage);
        Assert.Equal(single.Outcome(), volley.Pellets[0].Outcome());
    }

    // ---- 「单独计算」：8 颗各走各的完整判定链 ----

    [Fact]
    public void 八颗弹丸各自独立选部位_可打中不同部位()
    {
        Weapon shotgun = WeaponTable.ImprovisedShotgun();
        Assert.Equal(8, shotgun.PelletCount);

        // 部位选择器由调用方注入 → 逐颗各调一次；此处依次喂 8 个不同部位。
        string[] parts =
        {
            HumanBody.Head, HumanBody.Chest, HumanBody.Abdomen, HumanBody.LeftArm,
            HumanBody.RightArm, HumanBody.LeftHand, HumanBody.LeftLeg, HumanBody.RightFoot,
        };
        var byName = HumanBody.Parts().ToDictionary(p => p.Name);
        int i = 0;

        // 无甲：每颗只消耗 1 次伤害 roll（直击）。
        var rng = new SequenceRandomSource(3, 3, 3, 3, 3, 3, 3, 3);
        VolleyResult v = new CombatResolver(rng).ResolveVolley(
            shotgun, Array.Empty<ArmorLayer>(), () => byName[parts[i++]]);

        Assert.Equal(8, v.Pellets.Count);
        Assert.Equal(parts, v.Pellets.Select(p => p.HitPart.Name).ToArray()); // 8 颗打中 8 个不同部位
        Assert.Equal(8, v.HitParts.Distinct().Count());
    }

    /// <summary>
    /// 每颗弹丸**独立**过护甲三段判定：同一次射击里可以有的被挡下、有的半伤、有的全伤。
    /// 布衣锐防 6、穿透 10% → 防 roll ∈ [0, 5.4]；攻 &lt; 防/2 挡下、防/2 ≤ 攻 &lt; 防 半伤、攻 ≥ 防 全伤。
    /// </summary>
    [Fact]
    public void 每颗弹丸独立过护甲三段判定()
    {
        Weapon shotgun = WeaponTable.ImprovisedShotgun(); // 2~6 伤害（T21 用户手改，原 1~5）
        BodyPart chest = Chest();

        // 三颗：①攻2 vs 防5.0 → 2 < 2.5 挡下；②攻3 vs 防5.0 → 2.5 ≤ 3 < 5 半伤；③攻6 vs 防2.0 → 全伤。
        // 其余 5 颗喂全伤（攻6 vs 防0）。每颗 2 次 roll（攻、防）。攻击 roll 必须落在新区间 2~6 内。
        var rng = new SequenceRandomSource(
            2, 5.0,
            3, 5.0,
            6, 2.0,
            6, 0, 6, 0, 6, 0, 6, 0, 6, 0);

        VolleyResult v = new CombatResolver(rng).ResolveVolley(shotgun, Shirt(), () => chest);

        Assert.Equal(LayerOutcome.Blocked, v.Pellets[0].Outcome());
        Assert.Equal(LayerOutcome.Half, v.Pellets[1].Outcome());
        Assert.Equal(LayerOutcome.Full, v.Pellets[2].Outcome());

        Assert.True(v.Pellets[0].Terminated);   // 被布衣挡下 → 0 伤
        Assert.Equal(0, v.Pellets[0].FinalDamage);
        Assert.Equal(1.5, v.Pellets[1].FinalDamage); // 半伤 3/2
        Assert.Equal(6, v.Pellets[2].FinalDamage);   // 全伤（T21：满掷点 5 → 6）

        Assert.Equal(1, v.BlockedCount);
        Assert.Equal(7, v.LandedCount);              // 8 颗里 7 颗进肉
        Assert.Equal(0 + 1.5 + 6 + 5 * 6, v.TotalDamage);
    }

    /// <summary>穿透 15%（T29 用户手改，原 10%）逐颗生效：每颗弹丸各自把防御上限压到 ×0.85（防 roll 上界 6×0.85=5.1）。</summary>
    [Fact]
    public void 穿透逐颗生效()
    {
        Weapon shotgun = WeaponTable.ImprovisedShotgun();
        Assert.Equal(0.15, shotgun.Penetration);
        BodyPart chest = Chest();

        // 每颗喂 防 roll = 5.1（= 6×(1−0.15) 的上界）。SequenceRandomSource 会校验越界。
        var rng = new SequenceRandomSource(
            5, 5.1, 5, 5.1, 5, 5.1, 5, 5.1, 5, 5.1, 5, 5.1, 5, 5.1, 5, 5.1);

        VolleyResult v = new CombatResolver(rng).ResolveVolley(shotgun, Shirt(), () => chest);

        Assert.All(v.Pellets, p =>
        {
            LayerResolution shirt = p.Layers[0];
            Assert.Equal(0.15, shirt.PenetrationUsed);        // 每颗都用了 15% 穿透
            Assert.Equal(6, shirt.ApplicableDefense);          // 布衣锐防
        });

        // 防上限被穿透压到 5.1：喂 5.2 必须越界抛（证明上限确实是 5.1 而非 6）。
        var over = new SequenceRandomSource(5, 5.2);
        Assert.Throws<InvalidOperationException>(
            () => new CombatResolver(over).ResolveVolley(shotgun, Shirt(), () => chest));
    }

    // ---- 自制霰弹枪的武器规格（用户口径） ----

    [Fact]
    public void 自制霰弹枪规格符合用户口径()
    {
        Weapon sg = WeaponTable.ImprovisedShotgun();

        Assert.Equal(8, sg.PelletCount);        // 8 颗弹丸
        Assert.Equal(0.15, sg.Penetration);     // T29 用户手改（0.10 → 0.15）：仍是全枪械最低穿透
        Assert.True(sg.IsRanged);

        // 射程较短：短于全表任何一把远程武器。
        var otherGuns = WeaponTable.Arsenal().Where(w => w.IsRanged && w.Name != sg.Name).ToList();
        Assert.All(otherGuns, g => Assert.True(sg.MaxRange < g.MaxRange, $"射程应短于{g.Name}"));

        // 伤害衰减严重：末端衰减系数低于全表任何一把远程武器。
        Assert.All(otherGuns, g => Assert.True(sg.FalloffFloor < g.FalloffFloor, $"衰减应重于{g.Name}"));

        // 锥形扩散较大：误差角大于全表任何一把远程武器。
        Assert.All(otherGuns, g => Assert.True(sg.BaseSpreadDegrees > g.BaseSpreadDegrees, $"扩散应大于{g.Name}"));

        // 单颗弹丸伤害必须显著低于板甲的挡下门槛（50×(1−0.15)/2 = 21.25）——这是"对板甲极差"的数学根源。
        // ⚠ T29：旧断言是「个位数」（DamageMax ≤ 9）。用户把单颗上限提到 12 ⇒ 那条字面说法不再成立，
        // 但它真正要守的东西（"一颗弹丸打不穿板甲"）依然成立且更该被直接钉住，故改钉门槛本身。
        Assert.True(sg.DamageMax < 21.25 * 0.6,
            $"单颗弹丸上限 {sg.DamageMax} 必须远低于板甲挡下门槛 21.25，否则霰弹就成了破甲武器");
        Assert.True(sg.DamageMin >= 1);
    }

    // ---- 对决引擎：一发霰弹 = 8 条战报（同一时刻），但只算「一次出手」 ----

    /// <summary>多弹丸武器在 <see cref="DuelEngine"/> 里一次出手打出 8 颗，产生 8 条同一时刻的命中事件。</summary>
    [Fact]
    public void 对决引擎里一发霰弹产生八条同刻事件且只计一次出手()
    {
        var shotgun = new Weapon
        {
            Name = "测试霰弹", DamageMin = 1, DamageMax = 5, Penetration = 0.10,
            DamageType = DamageType.Sharp, IsRanged = true, PelletCount = 8, AttackInterval = 4.0,
        };
        var stick = new Weapon { Name = "木棍", DamageMin = 1, DamageMax = 2, AttackInterval = 999 };

        var player = new DuelFighter
        {
            Name = "玩家", Weapons = new[] { new WeaponMount { Weapon = shotgun } },
        };
        var dummy = new DuelFighter
        {
            Name = "靶子", Weapons = new[] { new WeaponMount { Weapon = stick } },
        };

        DuelResult r = new DuelEngine(new SystemRandomSource(seed: 7)).Run(player, dummy);

        // 玩家的第一次出手（t=4.0，起手先满冷却）应产生 8 条弹丸事件、同一时刻。
        var firstVolley = r.Events
            .Where(e => e.Attacker == "玩家" && e.Weapon == "测试霰弹")
            .GroupBy(e => e.Time)
            .First();

        Assert.Equal(8, firstVolley.Count());
        Assert.All(firstVolley, e => Assert.Equal(firstVolley.Key, e.Time));

        // 8 颗弹丸各自选部位 → 一次齐射通常命中多个不同部位（同一发子弹不可能只打一个点）。
        Assert.True(firstVolley.Select(e => e.Part).Distinct().Count() > 1,
            "8 颗弹丸应能打中不同部位");
    }

    /// <summary>
    /// 涌现效果（用户预期的定位）：板甲挡下绝大多数、布衣几乎挡不住、丧尸腐皮更挡不住。
    /// 挡下门槛 = 护甲值×(1−穿透)/2 → 板甲 21.25 ≫ 弹丸上限 12；布衣 2.55 只夹得住最低那几档掷点。
    ///
    /// ⚠ T29：用户把单颗弹丸 2~6 → <b>2~12</b>、穿透 10% → <b>15%</b>，霰弹对甲**变强了**：
    /// 板甲挡下率从 ~86% 掉到 <b>73.6%</b>。"对板甲极差"这条定位仍然成立（三档序没变、板甲仍挡掉近四分之三），
    /// 但<b>严苛程度是用户主动放宽的</b> ⇒ 阈值由 &gt;0.80 下调到 &gt;0.70，钉的是"板甲仍挡下绝大多数"这个立意本身。
    /// </summary>
    [Fact]
    public void 涌现效果_对板甲极差对无甲极强()
    {
        Weapon sg = WeaponTable.ImprovisedShotgun();
        BodyPart chest = Chest();
        var rng = new SystemRandomSource(seed: 20260713);
        var resolver = new CombatResolver(rng);

        double BlockRate(IReadOnlyList<ArmorLayer> armor)
        {
            int blocked = 0, total = 0;
            for (int i = 0; i < 2000; i++)
            {
                VolleyResult v = resolver.ResolveVolley(sg, armor, () => chest);
                blocked += v.BlockedCount;
                total += v.Pellets.Count;
            }

            return (double)blocked / total;
        }

        double plate = BlockRate(new[] { ArmorTable.Plate(), ArmorTable.LongSleeveShirt() });
        double cloth = BlockRate(Shirt());
        double zombie = BlockRate(ArmorTable.ZombieHide());

        // [T59] 逐层口径改为「重掷+取min」后，板甲组（板甲+布衣，2 层）不再白吃"层数减伤"
        // ⇒ 挡下率从 ~71% 降到 **67.4%**。方向正确（减伤重新由防御力说了算），门槛随之下调。
        Assert.True(plate > 0.62, $"板甲应挡下绝大多数弹丸，实测 {plate:P1}");

        // ⚠ T21：旧断言是 InRange(cloth, 0.05, 0.35)「布衣挡下相当一部分」。
        // 用户把单颗弹丸下限从 1 提到 2 之后，布衣的挡下门槛（防6×(1−穿透0.1)/2 = 2.7）
        // 只夹得住掷点 2 那一档 ⇒ 布衣挡下率跌到 ~2.3%，旧区间的下界 5% 已不成立。
        // 这是提下限的直接后果，且方向明确（"薄衣挡不住霰弹"更符合直觉）⇒ 改钉新事实：
        // 布衣几乎挡不住，但仍略好于丧尸腐皮（腐皮防更低）。
        Assert.InRange(cloth, 0.005, 0.05);
        Assert.True(zombie < 0.05, $"丧尸腐皮几乎挡不住，实测 {zombie:P1}");
        Assert.True(cloth > zombie, $"布衣仍应略强于丧尸腐皮（布 {cloth:P1} vs 腐皮 {zombie:P1}）");
        Assert.True(plate > cloth * 2, "对披甲目标应显著差于对薄衣目标");
    }
}
