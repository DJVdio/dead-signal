namespace DeadSignal.Combat;

/// <summary>
/// 伤害效果种类。设计文档第 5 节"伤害效果"。
/// 锐器：流血、切除；钝器：震荡、骨折（钝器效果隔着未破的甲也能生效）。
/// </summary>
public enum DamageEffectKind
{
    Bleed,
    Sever,
    Concussion,
    Fracture,
}

/// <summary>一个被触发的伤害效果实例。</summary>
public sealed class DamageEffect
{
    public DamageEffectKind Kind { get; init; }
    public string PartName { get; init; } = "";

    /// <summary>严重度为归一化值（流血＝这一次占部位血量的比例；切除为满值；震荡/骨折＝触发时的概率）。供叙事/UI 使用。</summary>
    public double Severity { get; init; }

    /// <summary>
    /// 仅 <see cref="DamageEffectKind.Bleed"/> 使用：这一次造成的**出血等级**（[T58] 小/中/大）。
    /// 其余效果为 null。⚠️ 这是**这一击**的等级，**不是**该部位合并后的当前等级 ——
    /// 合并后的现状请查 <see cref="Body.BleedSeverityOn"/>。
    /// </summary>
    public BleedModel.BleedSeverity? BleedSeverity { get; init; }

    /// <summary>
    /// 时序时长（秒）。仅 <see cref="DamageEffectKind.Concussion"/> 使用：本次震荡的硬打断时长
    /// （时长范围由 Wiki 配置提供）。其余效果为零。消费方（对决引擎/Godot 实时层）据此设打断时限与冷却清零。
    /// </summary>
    public double DurationSeconds { get; init; }
}

/// <summary>
/// 效果触发概率参数（全部由 Wiki 配置提供）。概率曲线口径（用户确认）：
/// 流血/骨折 = k × 单次伤害 / 部位MaxHP（相对部位血量占比，自适应部位大小）；
/// 震荡 = k × 初始武器roll / 部位MaxHP（头部 MaxHP 小→天然高概率、躯干大→低概率）。
/// </summary>
public sealed class EffectConfig
{
    public double BleedK { get; init; }
    public double BleedCap { get; init; }
    public double FractureK { get; init; }
    public double FractureCap { get; init; }
    public double ConcussionHeadK { get; init; }
    public double ConcussionHeadCap { get; init; }
    public double ConcussionTorsoK { get; init; }
    public double ConcussionTorsoCap { get; init; }

    /// <summary>零生命部位受实质钝伤时的 MaxHp 磨损系数（由 Wiki 配置提供）。</summary>
    public double MaxHpErosionFactor { get; init; }

    // ---- 震荡时序 + 抗性（重制口径，具体参数由 Wiki 配置提供）----

    /// <summary>震荡硬打断最短时长（秒，Wiki 配置）。</summary>
    public double ConcussionMinSeconds { get; init; }

    /// <summary>震荡硬打断最长时长（秒，Wiki 配置）。触发时在配置范围内均匀 roll。</summary>
    public double ConcussionMaxSeconds { get; init; }

    /// <summary>
    /// 未击穿护甲（被甲完全挡下=FinalDamage==0，「甲没破人被锤懵」）触发的震荡固定打断时长（秒，Wiki 配置）。
    /// 击穿/无甲命中仍走配置范围 roll。粒度取舍：只把"完全挡下"判为未击穿；减半但穿透仍算击穿走区间 roll。
    /// </summary>
    public double ConcussionBlockedSeconds { get; init; }

    /// <summary>
    /// 震荡抗性系数：吃到一次震荡后，其打断+首轮重走冷却期间，再次震荡的触发概率乘此系数，
    /// 防震荡死锁。抗性窗覆盖打断本身+首轮冷却为默认推导（待确认）。
    /// </summary>
    public double ConcussionResistFactor { get; init; }

    /// <summary>震荡打断期间移动速度系数（Godot 实时层消费；对决引擎无位移故不用）。由 Wiki 配置提供。</summary>
    public double ConcussionMoveSlowFactor { get; init; }

    /// <summary>单处**未治疗**手部骨折对操作能力的乘算系数（含攻速，用户口径）。多处乘算叠加，数值由 Wiki 配置提供。</summary>
    public double HandFractureOperationMult { get; init; }

    /// <summary>单处**已治疗**（手术成功、愈合中）手部骨折的操作系数（减轻惩罚，用户口径）。数值由 Wiki 配置提供。</summary>
    public double HandFractureHealedOperationMult { get; init; }

    /// <summary>单处**未治疗**腿/脚骨折对移动能力的乘算系数（用户口径）。多处乘算叠加，数值由 Wiki 配置提供。</summary>
    public double LegFractureMobilityMult { get; init; }

    /// <summary>单处**已治疗**腿/脚骨折的移动系数（减轻惩罚，用户口径）。数值由 Wiki 配置提供。</summary>
    public double LegFractureHealedMobilityMult { get; init; }

    /// <summary>多处骨折乘算叠加后的能力系数下限（防止叠到过低，拟定待确认）。</summary>
    public double FractureCapabilityFloor { get; init; }

    // ---- 命中减速（通用，RimWorld stagger 式；用户口径「未破防也会短暂减速 是 所有角色都是这样的（参考rimworld）」）----

    /// <summary>
    /// 命中减速时长（秒，Wiki 配置）。**任何攻击命中任何角色、无论是否击穿护甲**都施加，只降移速、
    /// 短暂即恢复。与震荡严格区分：减速=轻量通用（无攻击打断、无冷却清零、无概率 roll——命中即触发，
    /// 重复命中刷新时长、不叠幅度）；震荡=既有重效果（<see cref="ConcussionMinSeconds"/> 一系）不变。
    /// 移动层效果——对决引擎 <see cref="DuelEngine"/> 不建模位移，故减速对对决胜率零影响，引擎只透传数值。
    /// </summary>
    public double StaggerSeconds { get; init; }

    /// <summary>命中减速期间移速系数（Godot 实时层消费，Wiki 配置）。比震荡打断轻。</summary>
    public double StaggerSpeedMult { get; init; }

    public static EffectConfig Default() => new()
    {
        BleedK = 1.0,
        BleedCap = 0.90,
        FractureK = 0.4,   // 参数由 Wiki 配置投影；代码值仅作为配置缺省。
        FractureCap = 0.60,
        ConcussionHeadK = 0.9,
        ConcussionHeadCap = 0.85,
        ConcussionTorsoK = 0.25,
        ConcussionTorsoCap = 0.40,
        MaxHpErosionFactor = 1.0,
        ConcussionMinSeconds = 2.0,
        ConcussionMaxSeconds = 5.0,
        ConcussionBlockedSeconds = 1.0,
        ConcussionResistFactor = 0.25,
        ConcussionMoveSlowFactor = 0.1,
        HandFractureOperationMult = 0.7,
        HandFractureHealedOperationMult = 0.85,
        LegFractureMobilityMult = 0.7,
        LegFractureHealedMobilityMult = 0.85,
        FractureCapabilityFloor = 0.2,
        StaggerSeconds = 1.0,     // Wiki 配置缺省
        StaggerSpeedMult = 0.6,   // Wiki 配置缺省
    };
}

/// <summary>一次命中的效果结算产物。</summary>
public sealed class EffectOutcome
{
    public HpChange Damage { get; init; }
    public IReadOnlyList<DamageEffect> Effects { get; init; } = Array.Empty<DamageEffect>();
    public bool CausedDeath { get; init; }
    public IReadOnlyList<string> SeveredParts { get; init; } = Array.Empty<string>();

    /// <summary>本次对 0HP 部位造成的 MaxHp 磨损量（0 = 未磨损）。</summary>
    public double MaxHpEroded { get; init; }

    /// <summary>被磨损的部位名（null = 未磨损）。</summary>
    public string? ErodedPart { get; init; }

    /// <summary>因 MaxHp 归 0 而永久损毁的部位（含连带后代；不掉落装备）。</summary>
    public IReadOnlyList<string> DestroyedParts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 本次命中施加的通用减速时长（秒；命中即置=<see cref="EffectConfig.StaggerSeconds"/>，含被甲完全挡下 dmg==0）。
    /// 不进 <see cref="Effects"/> 概率型效果列表（不耗 roll、不产生 Concussion）；由 Godot 实时层消费到移速。0=引擎未配置。
    /// </summary>
    public double StaggerSeconds { get; init; }
}

/// <summary>
/// 效果触发规则。输入一次 <see cref="CombatResult"/>，对 <see cref="Body"/> 施加伤害并按概率触发效果。
///
/// 门控口径（用户终裁）：锐器被护甲降解成钝伤后【无任何状态效果】——不流血/切除/震荡/骨折，只造成伤害。
/// 效果资格看【实际到达部位的伤害类型】。
/// - 流血/切除：仅当伤害仍以锐器抵达部位（result.FinalDamageType==Sharp）。降解成钝的锐器不流血/不切除。
/// - 切除：实际造成伤害 &gt;0 且（单次伤害 ≥ 部位MaxHP，或命中已 0HP 部位）。
///   被甲完全挡下（dmg==0）不切除，部位保住。致死部位达成条件 = 直接死亡（不生成断肢）。
/// - 震荡：仅天然钝器（weapon.DamageType==Blunt）。锐器降解的钝伤不触发。
///   无论是否造成伤害（含被甲完全挡下），仅头/躯干类部位；输入用初始武器 roll（"钝器隔甲生效"）。
/// - 骨折：仅天然钝器，且造成伤害（dmg&gt;0），任意部位。锐器降解的钝伤不触发。
/// - MaxHp 磨损（【非状态效果】，不走概率门控）：对**当前 HP=0** 的部位造成**实质钝伤**
///   （FinalDamageType==Blunt，含锐转钝的降解伤害与天然钝器）且穿甲后 dmg&gt;0 时，永久降低其 MaxHp；
///   被甲完全挡下（dmg==0）不磨损。MaxHp 归 0 → 部位永久损毁（连带后代、致死部位=死亡、不掉落装备）。
///   锐器（未降解）对 0HP 部位造成实质伤害仍走切除，不磨损。
///
/// rng 调用顺序（供测试复现）：切除/磨损为确定性判定不耗 roll；随后依次 流血→震荡→骨折 各最多一次 roll。
/// </summary>
public sealed class CombatEffectResolver
{
    private readonly IRandomSource _rng;
    private readonly EffectConfig _cfg;

    public CombatEffectResolver(IRandomSource rng, EffectConfig? cfg = null)
    {
        _rng = rng;
        _cfg = cfg ?? EffectConfig.Default();
    }

    /// <param name="concussionResistFactor">
    /// 目标当前的震荡抗性系数（默认 1.0=无抗性）。&lt;1 时震荡触发概率按此打折（防死锁，见
    /// <see cref="EffectConfig.ConcussionResistFactor"/>）。抗性窗由调用方（对决/实时层）按其时序判定后传入。
    /// 无论是否抗性，只要天然钝器命中震荡部位就照旧消耗一次震荡 roll（保持 rng 顺序稳定可复现）。
    /// </param>
    public EffectOutcome Apply(Body body, Weapon weapon, CombatResult result, double concussionResistFactor = 1.0)
    {
        string partName = result.HitPart.Name;
        BodyPart part = result.HitPart;
        double dmg = result.FinalDamage;
        double maxHp = body.MaxHpOf(partName); // 命中时的（可能已磨损的）部位上限
        // 效果资格看【实际到达部位的伤害类型】：锐器被降解成钝伤后无任何效果（不流血/切除/震荡/骨折）。
        // 流血/切除仅当伤害仍以锐器抵达；震荡/骨折仅天然钝器。
        bool sharp = result.FinalDamageType == DamageType.Sharp;
        bool bluntArriving = result.FinalDamageType == DamageType.Blunt; // 含锐转钝的降解 + 天然钝器
        bool nativeBlunt = weapon.DamageType == DamageType.Blunt;

        var effects = new List<DamageEffect>();

        double hpBefore = body.HpOf(partName);
        bool wasZeroBefore = hpBefore <= 0 && !body.IsGone(partName);

        // 1. 施加伤害
        HpChange change = body.ApplyDamage(partName, dmg);

        // 2. 切除判定（锐器抵达、造成伤害）
        bool severed = false;
        var severedParts = new List<string>();
        bool death = body.IsDead;
        if (sharp && dmg > 0 && !body.IsGone(partName)
            && (dmg >= maxHp || wasZeroBefore))
        {
            var sr = body.Sever(partName);
            severed = true;
            severedParts.AddRange(sr.RemovedParts);
            death = death || sr.CausedDeath;
            effects.Add(new DamageEffect { Kind = DamageEffectKind.Sever, PartName = partName, Severity = 1.0 });
            // 切除即重创出血。[T58] 断口一律 **大流血**：肢体被整个砍下来，血管是全断的 ——
            // 这本来就是"封顶"那一档的定义。（切除的判据本就是"单次伤害 ≥ 部位 MaxHp"，
            // 按比例算也会超过高等级阈值；唯一的例外是砍在已经归零的部位上，那更该按最狠算。）
            // 断口按**这把武器**的流血速率放血（锯齿剑砍下的断口流得更快）。
            effects.Add(new DamageEffect
            {
                Kind = DamageEffectKind.Bleed, PartName = partName, Severity = 1.0,
                BleedSeverity = BleedModel.BleedSeverity.Large,
            });
            body.RegisterBleed(partName, BleedModel.BleedSeverity.Large, weapon.BleedRateMultiplier);
        }

        // 2b. MaxHp 磨损（非状态效果）：0HP 部位受实质钝伤（降解钝伤/天然钝器）→ 永久磨损上限，归 0 则损毁。
        double erodedAmount = 0;
        string? erodedPart = null;
        var destroyedParts = new List<string>();
        if (bluntArriving && dmg > 0 && wasZeroBefore && !severed && !body.IsGone(partName))
        {
            var er = body.ErodeMaxHp(partName, dmg * _cfg.MaxHpErosionFactor);
            erodedAmount = er.MaxHpBefore - er.MaxHpAfter;
            erodedPart = partName;
            if (er.Destroyed)
            {
                destroyedParts.AddRange(er.DestroyedParts);
                death = death || er.CausedDeath;
            }
        }

        // 3. 流血（锐器抵达、造成伤害、未切除）—— [T58] **三级流血**。
        //
        // 用户规格：锐器伤害进入流血分级；分级阈值与封顶由 Wiki 配置提供。
        //
        // 🔴 **【DECISION 待用户裁决】这句话有两种自洽读法，本实现取【读法 B】** ——
        //    **读法 A（字面）**：锐器**只要进了肉就必挂**流血（去掉概率门）。
        //    **读法 B（本实现）**：**保留既有的概率门**（p = 进肉伤害 / 部位MaxHp），
        //                        这句话定义的是「**挂上流血时它是哪一级**」，等级阈值由 Wiki 配置提供。
        //
        //    **为什么取 B**：读法 A 与用户**自己的另一条硬口径直接冲突**。
        //    下表是一次性的 A/B 实验存档；具体实验输入和结果见历史研究报告，不能作为当前配置引用。
        //    读法 A 之下，爪击**每一下必挂小流血**，而用户的合并规则（两小合中、小+中合大）会让
        //    **同一部位挨满 3 下就 ratchet 到大流血** ⇒ 围攻时全身部位一路升到封顶 ⇒ **平方律必然恶化**。
        //    用户此前已**明确拒绝**过「两只丧尸就是死局」（[T53] 因此把流血口径回退过一次）。
        //    读法 B 不但不冲突，还**兑现了三级流血的设计初衷**：浅爪多半连血都挂不上、挂上也只是低等级流血，
        //    从而减轻围攻时的平方律恶化。
        //    ⇒ 若用户确认要字面的读法 A，把下面这个 `if (_rng.Range(0, 1) < p)` 拆掉即可（一行）。
        //
        // 🔴 分级看的是 **进肉伤害 ÷ 部位最大生命值** ⇒ **护甲自动防住了流血**：
        //    穿甲后的少量剐蹭 ⇒ 比例较小 ⇒ 只挂低等级流血；裸露部位的深劈 ⇒ 进入高等级流血。
        //    （[T53] 查明的根因正是"流血速率与口子多深完全无关 ⇒ 流血对护甲免疫"。本条即其修复。）
        bool bledThisHit = severed; // 切除已经登记过大流血，别再叠
        if (sharp && dmg > 0 && !severed)
        {
            double p = Clamp01Cap(_cfg.BleedK * dmg / maxHp, _cfg.BleedCap);
            if (_rng.Range(0, 1) < p)
            {
                var level = BleedModel.SeverityOf(dmg, maxHp);
                effects.Add(new DamageEffect
                {
                    Kind = DamageEffectKind.Bleed, PartName = partName,
                    Severity = maxHp > 0 ? dmg / maxHp : 1.0, // 严重度＝这一刀占部位血量的比例（叙事/UI 用）
                    BleedSeverity = level,
                });
                // 出血带上**这把武器**的流血速率，具体倍率由 Wiki 配置提供。
                // 同部位再挨一刀 ⇒ Body 内部**即时合并**（两小合中、两中合大、小+中合大、封顶大流血）。
                body.RegisterBleed(partName, level, weapon.BleedRateMultiplier);
                bledThisHit = true;
            }
        }

        // 3b. 「小流血」（[T53] 钉子强化：棍棒造成伤害时按配置概率扎出一个小口子）。
        //
        // 🔴 [T58]「小流血」**现在有了正式语义** —— 它就是 <see cref="BleedModel.BleedSeverity.Small"/> 这一级，
        //    **不再是"速率乘数减半的普通伤口"**（旧的速率常数已退役）。
        //    ⇒ 钉子强化扎出的口子会和别的小流血**按同一套规则合并**（扎两下同一处 ⇒ 中流血）。
        //
        // 🔴 **零漂移的关键**：`weapon.BleedOnHitChance > 0` 是**前置短路**条件 ——
        // 所有既有武器该值为 0 ⇒ 整个分支连 `_rng.Range` 都不会调用 ⇒ 随机流不受它影响。
        //
        // 它**独立于上面的锐器流血**：钝器（棍棒）本来一处出血都造不出来（流血资格要求锐器抵达），
        // 钉子强化正是要给钝器开一个小口子。已经流血的这一击不再叠（!bledThisHit）。
        if (weapon.BleedOnHitChance > 0 && dmg > 0 && !bledThisHit)
        {
            if (_rng.Range(0, 1) < weapon.BleedOnHitChance)
            {
                effects.Add(new DamageEffect
                {
                    Kind = DamageEffectKind.Bleed, PartName = partName,
                    Severity = weapon.BleedOnHitChance,
                    BleedSeverity = BleedModel.BleedSeverity.Small,
                });
                body.RegisterBleed(partName, BleedModel.BleedSeverity.Small, weapon.BleedRateMultiplier);
            }
        }

        // 4. 震荡（天然钝器、头/躯干类部位、无论是否造成伤害）
        if (nativeBlunt && part.ConcussionProne)
        {
            bool torso = part.Region == BodyRegion.Torso;
            double k = torso ? _cfg.ConcussionTorsoK : _cfg.ConcussionHeadK;
            double cap = torso ? _cfg.ConcussionTorsoCap : _cfg.ConcussionHeadCap;
            // 抗性期内触发概率打折（×resistFactor，防震荡死锁）；无抗性时概率不变。
            double p = Clamp01Cap(k * result.InitialAttackRoll / maxHp, cap) * concussionResistFactor;
            if (_rng.Range(0, 1) < p)
            {
                // 未击穿护甲（被甲完全挡下 dmg==0）→ 固定短打断，不消耗时长 roll；
                // 击穿/无甲（dmg>0）→ 按 Wiki 配置范围 roll，走同一 IRandomSource 保持可复现。
                double dur = dmg > 0
                    ? _rng.Range(_cfg.ConcussionMinSeconds, _cfg.ConcussionMaxSeconds)
                    : _cfg.ConcussionBlockedSeconds;
                effects.Add(new DamageEffect
                {
                    Kind = DamageEffectKind.Concussion, PartName = partName, Severity = p, DurationSeconds = dur,
                });
            }
        }

        // 5. 骨折（天然钝器、造成伤害、**仅四肢**——[SPEC-FRAC-LIMB]）
        //
        // 🔴 部位门（`part.FractureProne`）：用户拍板「软组织不会骨折，只有四肢会」。命中软组织（胸/腹/头/眼/面/耳）
        //    **直接不掷骨折 roll**（形似震荡的 `ConcussionProne` 门）；命中四肢任一部位（含手指/脚趾）才掷。
        //    命中则标记**所属肢**骨折（手指→上肢、脚趾→下肢；同一肢重复命中幂等 ⇒ 全身最多 4 处）。
        // 🔴 `nativeBlunt` ⇒ **只有天然钝器能打断骨头；锐器降解出来的钝伤不算**（同 §切除 的对称口径）。
        //    这是**已查明的设计事实，不是待修的 bug**——别"顺手"把它放宽成 FinalDamageType==Blunt。
        //    它的后果是具体且刻意的：[T57] 撤掉手枪后守备清一色利器 ⇒ 战斗代价**从骨折换成永久残缺**
        //    （骨折会占用恢复资源；切除是**永久**断手，不可逆）。
        //    换句话说"这一轮没人骨折"不代表变轻松了，只代表**账单换了一种货币**。
        //    （对照 2b：MaxHp 磨损**收**降解钝伤，用的是 FinalDamageType==Blunt —— 两处判据不同是有意的。）
        if (nativeBlunt && dmg > 0 && part.FractureLimb is Limb fractureLimb)
        {
            double p = Clamp01Cap(_cfg.FractureK * dmg / maxHp, _cfg.FractureCap);
            if (_rng.Range(0, 1) < p)
            {
                // 战报/UI 用**所属肢**显示名（"骨折:右上肢"），而非命中的细部位名——整肢裁定。
                effects.Add(new DamageEffect { Kind = DamageEffectKind.Fracture, PartName = fractureLimb.DisplayName(), Severity = p });
                body.MarkFractured(partName); // 落到 Body 持久态（内部归并到所属肢），供健康页签查询
            }
        }

        // 切除/损毁使肢体或手指消失 → 重算残疾净惩罚（操作/移动）。幂等，仅在有部位移除时触发。
        if (severed || destroyedParts.Count > 0)
        {
            body.RecalculatePenalties();
        }

        return new EffectOutcome
        {
            Damage = change,
            Effects = effects,
            CausedDeath = death,
            SeveredParts = severedParts,
            MaxHpEroded = erodedAmount,
            ErodedPart = erodedPart,
            DestroyedParts = destroyedParts,
            // 命中减速（通用）：命中即触发，无论破防与否、无概率 roll、不打断/不清冷却。仅透传时长供实时层消费移速。
            StaggerSeconds = _cfg.StaggerSeconds,
        };
    }

    private static double Clamp01Cap(double v, double cap) => Math.Clamp(v, 0, Math.Min(1, cap));
}

// 【已删除·2026-07-17】`public interface IEffectTickSink { void Tick(Body, double); }`
//   —— 零实现者、零消费者的**死抽象**，留着就是假事实源（会教下一个人以为"时序效果从这个接口过"）。
//   它自称"留给震荡时限衰减、骨折恢复等后续时序效果"，但引擎里**三个时序效果无一走它**：
//     · 流血失血 → `Body.TickBleed`（**它自己的注释就承认了这一点** —— 头号用例当场绕开）
//     · 震荡时限 → `Duel`/`DuelEngine` 的内部时间轴（打断+冷却清零+抗性窗）
//     · 治疗恢复 → `Body.RestRecover` + 消费层 `Pawn.AdvanceHealthDay`（按昼夜推进，不按 dt）
//   设计文档 §5 确有震荡规则（L482-483），但 §5:508 自陈「震荡打断+冷却清零+抗性…**已落地**」
//   ⇒ 不是"设计有、代码没有"的待造项，而是**已实装且走别的路** ⇒ 不适用"保留待办"的理由。
//   若将来做 §5:509 的辐射 DoT（待实现），按流血的先例走 `Body.TickXxx` 具体方法即可，不需要这层抽象。
