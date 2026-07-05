namespace DeadSignal.Combat;

/// <summary>
/// 伤害效果种类。设计文档第 5 节"伤害效果"。
/// 锐器：流血、切除；钝器：震荡、骨折（钝器效果隔着未破的甲也能生效）。
/// 本期仅定义枚举与触发接口，具体触发判定 TODO。
/// </summary>
public enum DamageEffectKind
{
    Bleed,
    Sever,
    Concussion,
    Fracture,
}

/// <summary>一个被触发的伤害效果实例（本期作为钩子占位）。</summary>
public sealed class DamageEffect
{
    public DamageEffectKind Kind { get; init; }
    public string PartName { get; init; } = "";
}

/// <summary>
/// 伤害效果触发规则钩子。结算引擎产出 <see cref="CombatResult"/> 后交由此接口判定效果。
/// 本期不实现具体触发逻辑。
/// </summary>
/// TODO(效果): 实现流血/切除/震荡/骨折触发判定；
/// 注意钝器的震荡/骨折需支持"甲未破也生效"。
public interface IDamageEffectRule
{
    IEnumerable<DamageEffect> Evaluate(CombatResult result);
}
