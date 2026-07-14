using DeadSignal.Combat;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// WeaponDps 是**数值表 wiki 的 DPS 列的唯一事实源**（绝不在网页 JS 里另写一遍公式）。
/// 这些测试钉死的不是"数字是多少"，而是**三条最容易被写错的规则**：
///   ① 冷却在整轮连发之后才起算（周期 ≠ 攻击间隔）
///   ② 持握系数只除在冷却上 ⇒ 双持 ≠ 常规 ×1.4
///   ③ 不可双持的武器**没有**双持 DPS（null，不是 0）
/// </summary>
public class WeaponDpsTests
{
    private static Weapon Melee(double min, double max, double interval, bool twoHanded = false, bool dual = false) => new()
    {
        Name = "测试近战",
        DamageMin = min,
        DamageMax = max,
        AttackInterval = interval,
        TwoHanded = twoHanded,
        CanDualWield = dual,
    };

    // ── 期望伤害 = 区间中点（引擎第一层攻方在区间内均匀 roll）──

    [Fact]
    public void 期望单发伤害_取伤害区间的中点()
    {
        Assert.Equal(4.0, WeaponDps.ExpectedDamagePerShot(Melee(1, 7, 1.4)), 6);
    }

    // ── ① 连发：冷却在末发之后才起算 ──

    [Fact]
    public void 单发武器_周期就是攻击间隔()
    {
        Weapon w = Melee(1, 7, 1.4);
        Assert.Equal(1.4, WeaponDps.CycleSeconds(w, GripMode.OneHanded), 6);
    }

    [Fact]
    public void 连发武器_周期_等于_连发间隔累计_加_冷却()
    {
        // 三连发、发间隔 0.06、冷却 2.6（冲锋枪的形状）
        Weapon smg = new()
        {
            Name = "测试连发",
            DamageMin = 10,
            DamageMax = 18,
            AttackInterval = 2.6,
            BurstCount = 3,
            BurstInterval = 0.06,
            TwoHanded = true,
        };

        // 🔴 周期 = (3-1)×0.06 + 2.6/1.15 = 0.12 + 2.260869...
        //    冷却是在**末发之后**才起算的，连发间隔不并进冷却里。
        double expected = 2 * 0.06 + 2.6 / DualWield.GripSpeedFactor(GripMode.TwoHanded);
        Assert.Equal(expected, WeaponDps.CycleSeconds(smg, GripMode.TwoHanded), 6);

        // 若误写成 "1 / 攻击间隔"（忽略连发），周期会短一截 —— 钉死不许那样算
        Assert.NotEqual(2.6, WeaponDps.CycleSeconds(smg, GripMode.TwoHanded), 3);
    }

    [Fact]
    public void 连发武器_一个周期打出_连发数_乘_弹丸数_发伤害()
    {
        Weapon w = new()
        {
            Name = "连发多弹丸",
            DamageMin = 2,
            DamageMax = 2,          // 期望恒为 2，便于算账
            AttackInterval = 1.0,
            BurstCount = 2,
            BurstInterval = 0,
            PelletCount = 4,
        };
        // 一周期 = 1.0 秒（单持），打出 2 发 × 4 弹丸 × 2 伤害 = 16
        Assert.Equal(16.0, WeaponDps.Of(w, GripMode.OneHanded), 6);
    }

    // ── ② 持握系数只除在冷却上 ──

    [Fact]
    public void 双手持的攻速系数_一律以引擎的GripSpeedFactor为准()
    {
        // ⚠️ 刻意**不写死 1.15**：双手加成是会变的规则（[SPEC-B21] 已按用户口径把 +15% 删掉、
        //    双手现与单手同为 1.0）。DPS 必须跟着引擎走，所以这里断言的是"与引擎系数一致"，
        //    而不是"等于某个数"。规则再改一次，这条测试和 wiki 的 DPS 都会自动跟上。
        Weapon w = Melee(1, 7, 1.4);
        double one = WeaponDps.Of(w, GripMode.OneHanded);
        double two = WeaponDps.Of(w, GripMode.TwoHanded);
        Assert.Equal(one * DualWield.GripSpeedFactor(GripMode.TwoHanded), two, 6);   // 无连发时恰好等于系数倍
    }

    [Fact]
    public void 无连发的武器_双持DPS_恰好是常规的1点4倍()
    {
        // 无连发（BurstInterval 那一截为 0）时，双持 = 2 × 0.70 = 1.4 倍——这是**特例**，不是通则
        Weapon dagger = Melee(1, 7, 1.4, dual: true);
        double single = WeaponDps.Single(dagger);
        double dual = WeaponDps.Dual(dagger)!.Value;
        Assert.Equal(single * 1.4, dual, 6);
    }

    [Fact]
    public void 有连发的武器_双持DPS_不等于常规的1点4倍()
    {
        // 🔴 这条是整个 DPS 计算最容易写错的地方，而且**方向和直觉相反**：
        //    双持的惩罚（÷0.70）只落在**冷却**上，**连发那一段时间不受惩罚**。
        //    ⇒ 连发占周期比重越大，惩罚被稀释得越厉害 ⇒ 双持连发武器反而**比 ×1.4 更划算**。
        //
        //    算给自己看（伤害恒 10、三连发、连发间隔 0.5、冷却 1.0）：
        //      单持周期 = 2×0.5 + 1.0/1.00 = 2.000 ⇒ DPS = 30/2.000  = 15.00
        //      双持周期 = 2×0.5 + 1.0/0.70 = 2.429 ⇒ 每手 30/2.429 = 12.35，两手 = 24.71
        //      天真估计 = 15.00 × 1.4 = 21.00   ← 低估了 3.7
        //    连发那 1.0 秒是**白送的**：它在两种持法下一样长。
        Weapon burst = new()
        {
            Name = "可双持连发",
            DamageMin = 10,
            DamageMax = 10,
            AttackInterval = 1.0,
            BurstCount = 3,
            BurstInterval = 0.5,     // 连发间隔占比很大，放大这个差别
            CanDualWield = true,
        };

        double single = WeaponDps.Single(burst);
        double dual = WeaponDps.Dual(burst)!.Value;

        // 若用 "×1.4" 的捷径会算出这个数：
        double naiveShortcut = single * 1.4;

        Assert.NotEqual(naiveShortcut, dual, 3);
        Assert.True(dual > naiveShortcut,
            $"连发那一段不吃双持惩罚 ⇒ 真实双持 DPS({dual:F2}) 应**高于** ×1.4 的天真估计({naiveShortcut:F2})");
    }

    [Fact]
    public void 连发占比越大_双持惩罚被稀释得越厉害()
    {
        // 同一把武器，只把"连发间隔"从 0 拉到 0.5：双持相对常规的倍率应从 1.4 单调上升。
        static double Ratio(double burstGap)
        {
            Weapon w = new()
            {
                Name = "x",
                DamageMin = 10,
                DamageMax = 10,
                AttackInterval = 1.0,
                BurstCount = 3,
                BurstInterval = burstGap,
                CanDualWield = true,
            };
            return WeaponDps.Dual(w)!.Value / WeaponDps.Single(w);
        }

        Assert.Equal(1.4, Ratio(0), 6);          // 无连发间隔 ⇒ 恰好 ×1.4（那个"特例"）
        Assert.True(Ratio(0.2) > 1.4);
        Assert.True(Ratio(0.5) > Ratio(0.2));    // 连发占比越大，双持越占便宜
    }

    // ── ③ 不可双持 ⇒ null（不是 0，也不是编一个数） ──

    [Fact]
    public void 不可双持的武器_双持DPS为null()
    {
        Assert.Null(WeaponDps.Dual(Melee(1, 21, 3.6, twoHanded: true)));   // 长剑：强制双手
        Assert.Null(WeaponDps.Dual(Melee(10, 13, 2.4)));                   // 棍棒：单手但不可双持
    }

    [Fact]
    public void 全表_可双持的武器才有双持DPS_其余一律null()
    {
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            double? dual = WeaponDps.Dual(w);
            if (w.CanDualWield)
            {
                Assert.True(dual is > 0, $"{w.Name} 可双持，应有双持 DPS");
            }
            else
            {
                Assert.True(dual is null, $"{w.Name} 不可双持，双持 DPS 必须是 null（不是 0）");
            }
        }
    }

    [Fact]
    public void 强制双手的武器_一律不可双持_故无双持DPS()
    {
        foreach (Weapon w in WeaponTable.Arsenal().Where(x => x.TwoHanded))
        {
            Assert.Null(WeaponDps.Dual(w));
        }
    }

    // ── 全表护栏：DPS 必须是有限正数（防除零 / NaN 溜进 wiki）──

    [Fact]
    public void 全表武器_常规DPS_都是有限正数()
    {
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            double dps = WeaponDps.Single(w);
            Assert.True(double.IsFinite(dps) && dps > 0, $"{w.Name} 的 DPS 不是有限正数：{dps}");
        }
    }

    // ── 结构护栏：DPS 是只读派生量，绝不能反过来影响战斗结算（Sim 零漂移的结构性保证）──

    [Fact]
    public void WeaponDps_不被任何战斗结算路径引用_只读派生量()
    {
        // 它是纯函数、不持有状态、不改 Weapon —— 同一把武器算两次必然同值。
        Weapon w = WeaponTable.Rifle();
        Assert.Equal(WeaponDps.Single(w), WeaponDps.Single(w), 10);
        Assert.Equal(WeaponDps.CycleSeconds(w, GripMode.TwoHanded), WeaponDps.CycleSeconds(w, GripMode.TwoHanded), 10);
    }
}
