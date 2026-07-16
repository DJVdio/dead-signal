using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载饥饿刻度的全部规则：衰减/进食/上限 clamp/到 0 饿死/各级能力惩罚，
// 以及"饥饿惩罚 × 残疾惩罚"的合并算法。Pawn 只持有本对象并转发，战斗消费点读其惩罚。

/// <summary>
/// 单个角色的饥饿刻度状态机（见 <see cref="HungerLevel"/>，数值化 0-6）。
/// 模型：每次昼夜相位切换 <see cref="Decay"/>（-1），吃一餐 <see cref="Feed"/>（+1，clamp 到 <see cref="Cap"/>）；
/// 吃满两餐净零维持。<see cref="Value"/> 到 0 即饿死（<see cref="IsStarved"/>）。
/// 数值全部"拟定待调"，用于走通规则形态。
/// </summary>
public sealed class HungerState
{
    /// <summary>普通角色饥饿上限（正常）。</summary>
    public const int DefaultCap = (int)HungerLevel.Sated;   // 5

    /// <summary>刻度硬上限（吃撑；"大胃袋"特质预留，本轮无 buff）。</summary>
    public const int MaxCap = (int)HungerLevel.Stuffed;     // 6

    /// <summary>饿死刻度（终态）。</summary>
    public const int StarvedValue = (int)HungerLevel.Starved; // 0

    /// <summary>本角色的饥饿上限（默认 5；"大胃袋"可设 6）。餐后 clamp 到此值。</summary>
    public int Cap { get; }

    /// <summary>当前饥饿刻度序号（0=饿死…5=正常…6=吃撑）。</summary>
    public int Value { get; private set; }

    /// <summary>当前刻度对应的枚举级别（供 UI/快照/日志读取）。</summary>
    public HungerLevel Level => (HungerLevel)Value;

    /// <param name="value">初始刻度（默认正常=5），会 clamp 到 [0, <paramref name="cap"/>]。</param>
    /// <param name="cap">该角色饥饿上限（默认 5；"大胃袋"传 6），自身 clamp 到 [0, 6]。</param>
    public HungerState(int value = DefaultCap, int cap = DefaultCap)
    {
        Cap = Math.Clamp(cap, StarvedValue, MaxCap);
        Value = Math.Clamp(value, StarvedValue, Cap);
    }

    /// <summary>已饿死（刻度归 0，终态）。</summary>
    public bool IsStarved => Value <= StarvedValue;

    /// <summary>昼夜相位切换：刻度 -1（下限 0 = 饿死）。无条件衰减，与进食解耦（净零由 <see cref="Feed"/> 抵消）。</summary>
    public void Decay() => Value = Math.Max(StarvedValue, Value - 1);

    /// <summary>吃一餐：刻度 +1，clamp 到该角色上限 <see cref="Cap"/>。饿死为终态，进食不复活。</summary>
    public void Feed()
    {
        if (IsStarved)
        {
            return; // 饿死是终态，不因进食恢复
        }
        Value = Math.Min(Cap, Value + 1);
    }

    /// <summary>
    /// 把刻度直接压到指定档（**仅降不升**）：用于剧情设定态（如道格"饿昏迷"入队时压到营养不良档）。
    /// 目标 clamp 到 [0, <see cref="Cap"/>]；已比目标更低（更饿）则不动（不因设定态回填）。压到 0 即饿死（终态）。
    /// </summary>
    /// <param name="level">目标刻度（越小越饿）；高于当前刻度时不生效（本方法只饿不喂）。</param>
    public void DrainTo(int level)
        => Value = Math.Min(Value, Math.Clamp(level, StarvedValue, Cap));

    /// <summary>
    /// 读档：把刻度直接设回去（<b>可升可降</b>）。
    /// <para>
    /// ⚠️ 不能借 <see cref="DrainTo"/> 恢复——那个方法**只饿不喂**（刻意的：剧情设定态不该把人喂饱）。
    /// 拿它读档会静默丢掉"吃撑"这类高于默认值的状态：新造的人是满刻度 5，存档里的 6 灌不进去，
    /// 而且不会有任何报错。
    /// </para>
    /// </summary>
    internal void Restore(int value) => Value = Math.Clamp(value, StarvedValue, Cap);

    /// <summary>
    /// 一次昼夜相位结算（净模型，一次性施加，避免"decay 落 0 → feed 被短路"的跨 0 误杀）：
    /// 无条件 -1，吃到饭再 +1 —— 净变化 = 吃到 ? 0（维持） : -1（前进一级）。餐后 clamp 到该角色上限。
    /// **进餐前**已饿死者（终态）不复活：直接维持 0。返回结算后是否饿死（刻度归 0）。
    /// </summary>
    /// <param name="ate">本相位是否吃到一餐（供餐份数覆盖到本人）。</param>
    public bool ResolvePhase(bool ate)
    {
        if (IsStarved)
        {
            return true; // 进餐前已是终态：不复活、也不再变化
        }
        int delta = ate ? 0 : -1; // 净零维持 / 净 -1 前进一级
        Value = Math.Clamp(Value + delta, StarvedValue, Cap);
        return IsStarved;
    }

    /// <summary>
    /// 饥饿能力惩罚净值 0~1（1=完全丧失）。越饿越重，从"饥饿(3)"开始，正常/有点饿/吃撑无惩罚。
    /// 操作与移动共用同一阶梯（拟定待调）。战斗消费点把它与残疾惩罚按 <see cref="CombineCapability"/> 合并。
    /// </summary>
    public double AbilityPenalty => PenaltyFor(Value);

    /// <summary>饥饿能力惩罚阶梯的数值段（hunger.json）——三档梯度值外置到 <see cref="HungerConfig"/>。</summary>
    private static HungerConfig Cfg => GameConfigCatalog.Section<HungerConfig>();

    /// <summary>
    /// 某刻度的能力惩罚阶梯（拟定待调）。三档梯度值外置到 <c>hunger.json</c>（<see cref="HungerConfig"/>）；
    /// 饿死档 <c>1.0</c>(满值兜底)与饱档 <c>0.0</c>(无惩罚)是两端<b>语义边界</b>，保留字面不外置。
    /// </summary>
    public static double PenaltyFor(int value) => value switch
    {
        <= (int)HungerLevel.Starved => 1.0,                       // 0 饿死（已亡，取满值兜底）
        (int)HungerLevel.Malnourished => Cfg.MalnourishedPenalty, // 1 营养不良
        (int)HungerLevel.Ravenous => Cfg.RavenousPenalty,         // 2 极度饥饿
        (int)HungerLevel.Hungry => Cfg.HungryPenalty,             // 3 饥饿
        _ => 0.0,                                                 // 4 有点饿 / 5 正常 / 6 吃撑
    };

    /// <summary>
    /// 把某能力上的"残疾惩罚"与"饥饿惩罚"合并成有效能力系数（0~1）。
    /// 口径：有效能力 = (1−残疾惩罚) × (1−饥饿惩罚)——两者相互独立各自打折，不覆盖、不改残疾数学本身。
    /// 任一惩罚 ≥1（如断双手/饿死）→ 结果 0（完全丧失）。战斗消费点（攻速/移速）直接乘用。
    /// </summary>
    public static double CombineCapability(double disabilityPenalty, double hungerPenalty)
    {
        double d = 1.0 - disabilityPenalty;
        double h = 1.0 - hungerPenalty;
        double eff = d * h;
        return eff < 0 ? 0 : eff;
    }
}
