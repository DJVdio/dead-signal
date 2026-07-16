using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// **皮特**的 authored 三级专属效果（<see cref="PetePerk"/>）纯逻辑单测。
/// 用户口径（主 agent 已拍板，原话不许引申）：
///   · 一级：移速 = 普通 1.15×；25% 概率一相位掉 2 饥饿值（不论几级都常驻）。
///   · 二级：移速 1.25×；操作能力 +5%。
///   · 三级：移速 1.3×；负重&lt;30kg 时受击 15% 概率判定闪避。
/// 升级条件：
///   · L1→L2：连续五天饥饿≥3（=每相位查，任一相位&lt;3 清零重记；连续 10 相位≥3 升级）。
///   · L2→L3：出发瞬间饥饿≤5 计一次，单调累计 3 次升级。
/// 锁的是规则形态与 authored 数值（用常量引用），随机走可注入 <see cref="SequenceRandomSource"/> 复现。
/// </summary>
public class PetePerkTests
{
    // ==================== 三级移速：1.15 / 1.25 / 1.3 ====================

    [Fact]
    public void 移速乘子_三级各为1点15_1点25_1点3()
    {
        Assert.Equal(1.15, PetePerk.Level1MoveSpeedMultiplier, precision: 10);
        Assert.Equal(1.25, PetePerk.Level2MoveSpeedMultiplier, precision: 10);
        Assert.Equal(1.30, PetePerk.Level3MoveSpeedMultiplier, precision: 10);

        Assert.Equal(1.15, PetePerk.MoveSpeedMultiplier(peteLevel: 1, isPete: true), precision: 10);
        Assert.Equal(1.25, PetePerk.MoveSpeedMultiplier(peteLevel: 2, isPete: true), precision: 10);
        Assert.Equal(1.30, PetePerk.MoveSpeedMultiplier(peteLevel: 3, isPete: true), precision: 10);
    }

    [Fact]
    public void 非皮特的移速乘子恒为1_零回归()
    {
        for (int lvl = 1; lvl <= 3; lvl++)
        {
            Assert.Equal(1.0, PetePerk.MoveSpeedMultiplier(peteLevel: lvl, isPete: false), precision: 10);
        }
    }

    // ==================== L2 操作能力 +5%（消费点乘算，不 clamp） ====================

    [Fact]
    public void 操作能力加成_L2起乘1点05_L1无()
    {
        Assert.Equal(0.05, PetePerk.OperationCapabilityBonus, precision: 10);

        Assert.Equal(1.0, PetePerk.OperationCapabilityMultiplier(peteLevel: 1, isPete: true), precision: 10);
        Assert.Equal(1.05, PetePerk.OperationCapabilityMultiplier(peteLevel: 2, isPete: true), precision: 10);
        Assert.Equal(1.05, PetePerk.OperationCapabilityMultiplier(peteLevel: 3, isPete: true), precision: 10); // 累进保留
        Assert.Equal(1.0, PetePerk.OperationCapabilityMultiplier(peteLevel: 3, isPete: false), precision: 10);
    }

    [Fact]
    public void 操作能力加成走乘算消费点_残缺归零者不被凭空补偿()
    {
        // [通则·乘算] 0 × 1.05 = 0：没有手的人操作能力仍是 0，加成不凭空补偿残缺。
        Assert.Equal(0.0, PetePerk.OperationCapabilityWithBonus(0.0, peteLevel: 2, isPete: true), precision: 10);
        // 满状态者 1.0 × 1.05 = 1.05（不 clamp 截回 1.0，与山姆光环消费点同法）。
        Assert.Equal(1.05, PetePerk.OperationCapabilityWithBonus(1.0, peteLevel: 2, isPete: true), precision: 10);
        Assert.Equal(0.86 * 1.05, PetePerk.OperationCapabilityWithBonus(0.86, peteLevel: 2, isPete: true), precision: 10);
        // L1 或非皮特不乘。
        Assert.Equal(1.0, PetePerk.OperationCapabilityWithBonus(1.0, peteLevel: 1, isPete: true), precision: 10);
        Assert.Equal(0.9, PetePerk.OperationCapabilityWithBonus(0.9, peteLevel: 2, isPete: false), precision: 10);
    }

    // ==================== L3 闪避：负重<30kg → 0.15，否则 0 ====================

    [Fact]
    public void 闪避概率_L3且负重小于30kg为0点15_否则0()
    {
        Assert.Equal(0.15, PetePerk.DodgeChanceValue, precision: 10);
        Assert.Equal(30.0, PetePerk.DodgeMaxCarriedKg, precision: 10);

        Assert.Equal(0.15, PetePerk.DodgeChance(peteLevel: 3, carriedKg: 0.0), precision: 10);
        Assert.Equal(0.15, PetePerk.DodgeChance(peteLevel: 3, carriedKg: 29.9), precision: 10);
        Assert.Equal(0.0, PetePerk.DodgeChance(peteLevel: 3, carriedKg: 30.0), precision: 10);  // 恰 30kg 不闪（免罚线）
        Assert.Equal(0.0, PetePerk.DodgeChance(peteLevel: 3, carriedKg: 45.0), precision: 10);
        // 未到 L3 无闪避。
        Assert.Equal(0.0, PetePerk.DodgeChance(peteLevel: 2, carriedKg: 0.0), precision: 10);
        Assert.Equal(0.0, PetePerk.DodgeChance(peteLevel: 1, carriedKg: 0.0), precision: 10);
    }

    // ==================== 饥饿 25% 掉 2（注入随机复现） ====================

    [Fact]
    public void 皮特饥饿相位_命中25pct则额外掉1合计掉2_未命中掉1()
    {
        Assert.Equal(0.25, PetePerk.ExtraHungerDropChance, precision: 10);

        // 未进食：普通掉 1；皮特命中 25% 再掉 1 ⇒ 合计掉 2。
        var hit = new HungerState(value: 5);
        PetePerk.ResolveHungerPhase(hit, ate: false, new SequenceRandomSource(0.1), isPete: true); // 0.1<0.25 命中
        Assert.Equal(3, hit.Value);   // 5 → 3（掉 2）

        var miss = new HungerState(value: 5);
        PetePerk.ResolveHungerPhase(miss, ate: false, new SequenceRandomSource(0.9), isPete: true); // 0.9≥0.25 未命中
        Assert.Equal(4, miss.Value);  // 5 → 4（掉 1）
    }

    [Fact]
    public void 皮特进食相位命中_仍额外掉1_大胃口()
    {
        // 进食本抵消衰减（净 0），但皮特命中 25% 仍额外掉 1（大男孩代谢快）。
        var fed = new HungerState(value: 5);
        PetePerk.ResolveHungerPhase(fed, ate: true, new SequenceRandomSource(0.1), isPete: true);
        Assert.Equal(4, fed.Value);   // 5 →（吃回 +1、衰减 -1 净 0）→ 额外 -1 → 4

        var fedMiss = new HungerState(value: 5);
        PetePerk.ResolveHungerPhase(fedMiss, ate: true, new SequenceRandomSource(0.9), isPete: true);
        Assert.Equal(5, fedMiss.Value); // 未命中：净 0 维持
    }

    [Fact]
    public void 非皮特的饥饿相位与普通ResolvePhase一致_且不消耗随机流()
    {
        // 非皮特：不掷骰（不动随机流，零回归）——传空序列源也不该抛耗尽异常。
        var s = new HungerState(value: 5);
        var emptyRng = new SequenceRandomSource(); // 空：若被 Range 调用即抛
        bool starved = PetePerk.ResolveHungerPhase(s, ate: false, emptyRng, isPete: false);
        Assert.Equal(4, s.Value);
        Assert.False(starved);
        Assert.Equal(0, emptyRng.Remaining);
    }

    [Fact]
    public void 皮特饥饿掉2不会跨0误杀_到0即饿死终态()
    {
        var s = new HungerState(value: 1);
        bool starved = PetePerk.ResolveHungerPhase(s, ate: false, new SequenceRandomSource(0.1), isPete: true);
        Assert.Equal(0, s.Value);   // 1 → 掉 2 被 clamp 到 0
        Assert.True(starved);
    }

    // ==================== L1→L2：连续 10 相位≥3，任一<3 清零 ====================

    [Fact]
    public void L1到L2_连续10相位饥饿不低于3才升级()
    {
        Assert.Equal(3, PetePerk.HungerThresholdForStreak);
        Assert.Equal(10, PetePerk.Level2ConsecutivePhases); // 5 天 × 2 相位/天

        var flags = new StoryFlags();
        Assert.Equal(1, PetePerk.LevelOf(flags));

        // 连续 9 个相位≥3：还不够（尚未达 10）。
        for (int i = 0; i < 9; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        Assert.Equal(9, PetePerk.HungerStreakPhases(flags));
        Assert.False(PetePerk.Level2Reached(flags));
        Assert.Equal(1, PetePerk.LevelOf(flags));

        // 第 10 个相位≥3：达标 → 永久升到 L2。
        PetePerk.RecordPhaseHunger(flags, phaseHunger: 4);
        Assert.True(PetePerk.Level2Reached(flags));
        Assert.Equal(2, PetePerk.LevelOf(flags));
    }

    [Fact]
    public void 任一相位低于3_连续计数清零重记()
    {
        var flags = new StoryFlags();
        for (int i = 0; i < 8; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        Assert.Equal(8, PetePerk.HungerStreakPhases(flags));

        // 一个 <3 的相位：清零。
        PetePerk.RecordPhaseHunger(flags, phaseHunger: 2);
        Assert.Equal(0, PetePerk.HungerStreakPhases(flags));
        Assert.False(PetePerk.Level2Reached(flags));
        Assert.Equal(1, PetePerk.LevelOf(flags));

        // 须重新连续 10 相位才升级。
        for (int i = 0; i < 10; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        Assert.Equal(2, PetePerk.LevelOf(flags));
    }

    [Fact]
    public void 达L2后再遇低饥饿相位不会跌回L1_升级已latch()
    {
        var flags = new StoryFlags();
        for (int i = 0; i < 10; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        Assert.Equal(2, PetePerk.LevelOf(flags));

        // 升到 L2 后哪怕再有 <3 相位（streak 清零），等级不倒退（与山姆的可倒退相反）。
        PetePerk.RecordPhaseHunger(flags, phaseHunger: 1);
        Assert.Equal(0, PetePerk.HungerStreakPhases(flags));
        Assert.True(PetePerk.Level2Reached(flags));
        Assert.Equal(2, PetePerk.LevelOf(flags));
    }

    // ==================== L2→L3：出发饥饿≤5 累计 3 次（单调） ====================

    [Fact]
    public void L2到L3_出发饥饿不超过5累计3次升级()
    {
        Assert.Equal(3, PetePerk.Level3DepartureCount);
        Assert.Equal(5, PetePerk.DepartureHungerCeiling);

        var flags = new StoryFlags();
        // 先升到 L2（L3 以 L2 为前提）。
        for (int i = 0; i < 10; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        Assert.Equal(2, PetePerk.LevelOf(flags));

        // 出发饥饿≤5 计一次。
        PetePerk.RecordDeparture(flags, departureHunger: 5);
        Assert.Equal(1, PetePerk.DeparturesLogged(flags));
        Assert.Equal(2, PetePerk.LevelOf(flags));

        PetePerk.RecordDeparture(flags, departureHunger: 4);
        Assert.Equal(2, PetePerk.DeparturesLogged(flags));
        Assert.Equal(2, PetePerk.LevelOf(flags));

        PetePerk.RecordDeparture(flags, departureHunger: 5);
        Assert.Equal(3, PetePerk.DeparturesLogged(flags));
        Assert.Equal(3, PetePerk.LevelOf(flags)); // 第 3 次 → L3
    }

    [Fact]
    public void 出发饥饿高于5不计数()
    {
        var flags = new StoryFlags();
        for (int i = 0; i < 10; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }

        PetePerk.RecordDeparture(flags, departureHunger: 6); // >5 不计
        Assert.Equal(0, PetePerk.DeparturesLogged(flags));
        PetePerk.RecordDeparture(flags, departureHunger: 5); // ≤5 计
        Assert.Equal(1, PetePerk.DeparturesLogged(flags));
    }

    [Fact]
    public void 出行计数单调_满3后不倒退()
    {
        var flags = new StoryFlags();
        for (int i = 0; i < 10; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        for (int i = 0; i < 5; i++)
        {
            PetePerk.RecordDeparture(flags, departureHunger: 3);
        }
        Assert.Equal(5, PetePerk.DeparturesLogged(flags));
        Assert.Equal(3, PetePerk.LevelOf(flags)); // 早已 L3，继续出行不影响
    }

    // ==================== 等级派生纯函数 ====================

    [Fact]
    public void 等级派生_未达L2恒L1_达L2看出行数()
    {
        Assert.Equal(1, PetePerk.EvaluateLevel(level2Reached: false, departures: 0));
        Assert.Equal(1, PetePerk.EvaluateLevel(level2Reached: false, departures: 99)); // 未达 L2，出行再多也 L1
        Assert.Equal(2, PetePerk.EvaluateLevel(level2Reached: true, departures: 0));
        Assert.Equal(2, PetePerk.EvaluateLevel(level2Reached: true, departures: 2));
        Assert.Equal(3, PetePerk.EvaluateLevel(level2Reached: true, departures: 3));
        Assert.Equal(3, PetePerk.EvaluateLevel(level2Reached: true, departures: 99));
    }

    // ==================== 升级计数存 StoryFlags：存档天然覆盖 ====================

    [Fact]
    public void 升级计数走StoryFlags_存档往返后等级不丢()
    {
        var flags = new StoryFlags();
        for (int i = 0; i < 10; i++)
        {
            PetePerk.RecordPhaseHunger(flags, phaseHunger: 3);
        }
        for (int i = 0; i < 3; i++)
        {
            PetePerk.RecordDeparture(flags, departureHunger: 5);
        }
        Assert.Equal(3, PetePerk.LevelOf(flags));

        var reloaded = new StoryFlags(flags.Snapshot());
        Assert.Equal(3, PetePerk.LevelOf(reloaded));
        Assert.True(PetePerk.Level2Reached(reloaded));
        Assert.Equal(3, PetePerk.DeparturesLogged(reloaded));
    }

    // ==================== SurvivorPerks 身份标记 ====================

    [Fact]
    public void 皮特身份标记_GrantPete置IsPete_其余角色false()
    {
        var perks = new SurvivorPerks();
        Assert.False(perks.IsPete);
        perks.GrantPete();
        Assert.True(perks.IsPete);
        Assert.Equal("皮特", PetePerk.PeteName);
    }
}
