using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 消费层**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间问题（弹道飞行/碰撞）归 Godot 实时层；本层只负责调用引擎既有 API（Resolve → 效果结算）。

/// <summary>
/// 原型期武器/护甲/部位数据工厂 + 一次攻击的封装。
/// 数值取自设计文档第 5 节（穿透口径直接照抄，其余为原型拟定，待蒙特卡洛拉表微调）。
/// 纯数据与规则调用，不含任何 Godot 类型，方便与 Sim 层共用。
/// </summary>
public static class CombatData
{
    // ---- 武器/护甲（权威数据源为 DeadSignal.Combat.WeaponTable / ArmorTable，本层仅转发）----

    /// <summary>手枪：中距离锐器，穿透 15%（文档：手枪 15%）。远程有误差角。</summary>
    public static Weapon Pistol() => WeaponTable.Pistol();

    /// <summary>匕首：近战锐器，穿透 9%（文档：匕首 9%）。</summary>
    public static Weapon Dagger() => WeaponTable.Dagger();

    /// <summary>丧尸爪击：近战钝器，穿透 3%（文档：棍棒级 3%）。天然钝器逐层保留自身穿透。</summary>
    public static Weapon ZombieClaw() => WeaponTable.ZombieClaw();

    /// <summary>人形两层甲：皮夹克(外套) + 长袖布衣(贴身)——劫掠者生成配置/战利品。</summary>
    public static IReadOnlyList<ArmorLayer> SurvivorArmor() => ArmorTable.SurvivorArmor();

    /// <summary>丧尸：一层腐烂硬皮（对钝器略韧）。<b>生成丧尸请用 <see cref="ZombieArmor"/></b>——单靠腐皮防护恒为零。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => ArmorTable.ZombieHide();

    /// <summary>
    /// 普通丧尸的护甲：随机抽一套<b>日常着装</b>（布衣/夹克/长裤/短裤…，85% 至少还穿着一件）叠在腐皮之外。
    /// 腐皮锐防/钝防仅 3 → 挡下门槛 1.5 &lt; 任何武器的伤害下限 ⇒ 光靠腐皮<b>对全部武器 0% 阻挡</b>；
    /// 布类锐防 6 把门槛抬到 3.0，丧尸才真有防护。规则与预设表见 <see cref="ZombieOutfit"/>。
    /// </summary>
    public static IReadOnlyList<ArmorLayer> ZombieArmor(IRandomSource rng) => ZombieOutfit.RollArmor(rng);

    /// <summary>
    /// <b>点名</b>一套装束的丧尸护甲（确定性，不掷骰）——摆放 authored 的**精英丧尸**用，
    /// 如 <c>CombatData.ZombieArmorNamed("防暴警察丧尸")</c>（穿板甲）。精英预设不在随机池里，只能这样点名。
    /// 可用名字见 <see cref="ZombieOutfit.ElitePresets"/>（精英）与 <see cref="ZombieOutfit.Presets"/>（日常）。
    /// </summary>
    public static IReadOnlyList<ArmorLayer> ZombieArmorNamed(string outfitName) => ZombieOutfit.ArmorOf(outfitName);

    // ---- 部位（接入引擎细部位表：15 细部位，含 MaxHp/Region/Category/树形父子） ----

    /// <summary>新建一具满血人形躯体（幸存者与丧尸同用人体细部位表）。</summary>
    public static Body NewHumanoidBody() => HumanBody.NewBody();
}

/// <summary>弹道命中一具躯体时的处理决策。</summary>
public enum ProjectileContact
{
    /// <summary>结算这次命中（敌对，或不在架肩豁免内的友军误伤）。</summary>
    Hit,

    /// <summary>穿过继续飞（紧贴射手的同阵营队友——视作架在其肩上射击，不误伤）。</summary>
    PassThrough,
}

/// <summary>
/// 友军误伤判定（纯函数）。用户口径（拍板）：敌对阵营正常命中；**非敌对**（同阵营/未来的盟友）默认友伤，
/// 但当其与射手**紧贴**（在架肩间距阈值内）时视作架在其肩上射击、不造成友伤——弹道穿过继续飞；
/// 紧贴阈值之外的非敌对单位仍可被击中（真实向友伤）。敌我由调用方经 <see cref="Factions.IsHostile"/> 裁定后传入，
/// 本函数只做纯比较，便于单测。阈值本身（像素/肩宽量级）由空间层给出。
/// </summary>
public static class FriendlyFire
{
    /// <summary>
    /// 决定弹道命中某 Actor 时的处理。
    /// </summary>
    /// <param name="hostile">被命中者是否与射手敌对（<see cref="Factions.IsHostile"/> 的结果）。</param>
    /// <param name="distanceToShooter">被命中者与射手的间距（与阈值同单位）。</param>
    /// <param name="shoulderGraceDistance">架肩豁免间距阈值（含）；≤ 此距离的非敌对单位被穿过。</param>
    public static ProjectileContact Resolve(
        bool hostile, double distanceToShooter, double shoulderGraceDistance)
    {
        if (hostile)
        {
            return ProjectileContact.Hit; // 敌对：正常命中
        }

        // 非敌对（同阵营/盟友）：紧贴（架肩）豁免则穿过，否则友伤命中。
        return distanceToShooter <= shoulderGraceDistance
            ? ProjectileContact.PassThrough
            : ProjectileContact.Hit;
    }
}

/// <summary>
/// 一次攻击的规则输出，供表现层浮字/血条使用。
/// 伤害已在结算时施加到防御方 <see cref="Body"/>；本结构只是把结果摊平给表现层读。
/// </summary>
public readonly struct AttackOutcome
{
    /// <summary>实际作用到部位的**小数**伤害（0 = 被甲完全挡下；[SPEC-B14-补6 伤害不取整]）。</summary>
    public readonly double Damage;
    public readonly string PartName;
    public readonly DamageType FinalType;

    /// <summary>被护甲完全挡下（伤害 0）。</summary>
    public readonly bool Blocked;
    /// <summary>本次命中造成了切除。</summary>
    public readonly bool Severed;
    /// <summary>本次命中触发了流血伤口。</summary>
    public readonly bool Bled;
    /// <summary>本次命中触发了震荡。</summary>
    public readonly bool Concussed;
    /// <summary>本次命中触发了骨折。</summary>
    public readonly bool Fractured;
    /// <summary>本次命中后防御方死亡（含斩首/开膛/失血致死）。</summary>
    public readonly bool Died;

    /// <summary>本次震荡的硬打断时长（秒，2~5s roll，拟定待调）；未震荡为 0。实时层据此设打断计时器。</summary>
    public readonly double ConcussionSeconds;

    /// <summary>
    /// 本次命中的通用减速时长（秒，拟定待调 ~1s）；**命中即触发**（无论破防与否，含被甲完全挡下）。
    /// 实时层（<c>Actor._staggerTimer</c>）据此短暂降移速（×StaggerSpeedMult）。与震荡区分：不打断、不清冷却。
    /// </summary>
    public readonly double StaggerSeconds;

    public AttackOutcome(
        double damage, string partName, DamageType finalType,
        bool blocked, bool severed, bool bled, bool concussed, bool fractured, bool died,
        double concussionSeconds = 0, double staggerSeconds = 0)
    {
        Damage = damage;
        PartName = partName;
        FinalType = finalType;
        Blocked = blocked;
        Severed = severed;
        Bled = bled;
        Concussed = concussed;
        Fractured = fractured;
        Died = died;
        ConcussionSeconds = concussionSeconds;
        StaggerSeconds = staggerSeconds;
    }
}

/// <summary>
/// 把命中选择器 + 逐层结算 + 效果结算包成一次"攻击者打防御者躯体"的调用。
/// 消费引擎既有 API：<see cref="VolumeWeightedHitSelector"/> 选部位 → <see cref="CombatResolver"/>
/// 逐层护甲结算 → <see cref="CombatEffectResolver"/> 施加到 <see cref="Body"/>（扣 HP/流血/切除/震荡/骨折/致死）。
/// </summary>
public sealed class CombatEngine
{
    private readonly IRandomSource _rng;
    private readonly CombatResolver _resolver;
    private readonly VolumeWeightedHitSelector _hitSelector;
    private readonly CombatEffectResolver _effectResolver;
    private readonly EffectConfig _effectCfg;

    public CombatEngine(int? seed = null)
        : this(new SystemRandomSource(seed))
    {
    }

    /// <summary>测试用：注入确定性随机源。</summary>
    public CombatEngine(IRandomSource rng)
    {
        _rng = rng;
        _effectCfg = EffectConfig.Default();
        _resolver = new CombatResolver(_rng);
        _hitSelector = new VolumeWeightedHitSelector(_rng);
        _effectResolver = new CombatEffectResolver(_rng, _effectCfg);
    }

    /// <summary>本引擎生效的效果参数（只读）。供实时层读震荡抗性/移速系数/骨折能力系数，与结算同源。</summary>
    public EffectConfig Effects => _effectCfg;

    /// <summary>
    /// 结算一次命中：在防御方当前尚存的部位中按体积加权选一处 → 逐层护甲结算 →
    /// 走效果结算施加到 <paramref name="defenderBody"/> → 摊平结果给表现层。
    /// <paramref name="damageFactor"/> 为远程距离衰减系数（(0,1]，见 <see cref="Ballistics.RangedDamageFactor"/>）：
    /// &lt;1 时按系数缩放武器伤害区间后再结算（护甲于其后照常逐层扣减，远射更易被甲挡下）；近战/满伤传 1.0。
    /// </summary>
    /// <param name="incomingDamageReduction">
    /// 防方**护甲后**乘算减伤比例（0..1，<b>默认 0＝无减免、零回归</b>）：与 <paramref name="damageFactor"/> 分层——
    /// 后者缩放**武器伤害区间**（护甲之前，甲再吃缩小后的伤害），本参数在**护甲三段判定之后**才乘
    /// （见 <see cref="CombatResolver.Resolve"/>）。现阶段唯一来源＝山姆 1 级"比常人耐揍"−10%
    /// （<c>SamPerk.IncomingDamageReduction</c>，经 <c>Actor.SetIncomingDamageReduction</c> 注入）。
    /// </param>
    public AttackOutcome ResolveHit(
        Weapon weapon,
        IReadOnlyList<ArmorLayer> defenderArmor,
        Body defenderBody,
        double damageFactor = 1.0,
        double concussionResistFactor = 1.0,
        double incomingDamageReduction = 0.0)
    {
        // 远程距离衰减：只在系数 <1 时建缩放副本，满伤/近战路径沿用原武器、逐字节零改动（零回归）。
        Weapon effective = damageFactor < 1.0 ? ScaleWeaponDamage(weapon, damageFactor) : weapon;

        // 只在尚存（未切除/未损毁）的部位里选，避免把伤害"打"到已消失的部位上。
        var candidates = defenderBody.Parts.Values
            .Where(p => !defenderBody.IsGone(p.Name))
            .ToList();

        BodyPart part = _hitSelector.Select(candidates);
        IReadOnlyList<ArmorLayer> ordered = CombatResolver.OrderOuterToInner(defenderArmor);
        CombatResult result = _resolver.Resolve(effective, ordered, part, incomingDamageReduction);
        EffectOutcome fx = _effectResolver.Apply(defenderBody, effective, result, concussionResistFactor);

        bool bled = false, concussed = false, fractured = false;
        double concussionSeconds = 0;
        foreach (DamageEffect e in fx.Effects)
        {
            switch (e.Kind)
            {
                case DamageEffectKind.Bleed: bled = true; break;
                case DamageEffectKind.Concussion: concussed = true; concussionSeconds = e.DurationSeconds; break;
                case DamageEffectKind.Fracture: fractured = true; break;
            }
        }

        return new AttackOutcome(
            damage: result.FinalDamage,
            partName: part.Name,
            finalType: result.FinalDamageType,
            blocked: result.FinalDamage <= 0,
            severed: fx.SeveredParts.Count > 0,
            bled: bled,
            concussed: concussed,
            fractured: fractured,
            died: fx.CausedDeath || defenderBody.IsDead,
            concussionSeconds: concussionSeconds,
            // 命中减速（通用）：无论破防与否命中即透传，供 Actor._staggerTimer 消费（全角色）。
            staggerSeconds: fx.StaggerSeconds);
    }

    /// <summary>
    /// 本引擎注入的随机源（只读）。供空间层的可注入 roll 复用同一 <see cref="IRandomSource"/>——如
    /// 哨塔围栏远程抵挡掷免（<c>Actor.ReceiveAttack</c> → <c>GuardPostMath.RangedBlocked</c>），保证确定性单测可复现。
    /// </summary>
    public IRandomSource Rng => _rng;

    /// <summary>把准星方向（度）叠加一次远程误差角锥采样，返回实际射击方向（度）。近战 spread=0 即恒为准星方向。</summary>
    public double SampleShotDirectionDegrees(double aimDegrees, double spreadDegrees) =>
        Ballistics.SampleShotDirectionDegrees(aimDegrees, spreadDegrees, _rng);

    /// <summary>
    /// 派生一把伤害区间按系数缩放的武器副本（仅缩放 <see cref="Weapon.DamageMin"/>/<see cref="Weapon.DamageMax"/>，
    /// 其余字段原样保留）。远程距离衰减用。<see cref="Weapon"/> 为 sealed class 无 <c>with</c> 表达式：
    /// **若 Weapon 新增字段，须在此同步拷贝**，否则副本会静默丢字段。
    /// </summary>
    private static Weapon ScaleWeaponDamage(Weapon w, double factor) => new()
    {
        Name = w.Name,
        DamageMin = w.DamageMin * factor,
        DamageMax = w.DamageMax * factor,
        Penetration = w.Penetration,
        DamageType = w.DamageType,
        TwoHanded = w.TwoHanded,
        CanDualWield = w.CanDualWield,
        IsRanged = w.IsRanged,
        BaseSpreadDegrees = w.BaseSpreadDegrees,
        AttackInterval = w.AttackInterval,
        BurstCount = w.BurstCount,
        BurstInterval = w.BurstInterval,
        PelletCount = w.PelletCount,   // 批次18补：原先漏拷（副本会静默丢弹丸数，霰弹变单弹丸）
        AmmoKey = w.AmmoKey,           // 批次18：漏拷会让副本变成"不吃弹药"，枪凭空获得无限弹
        MaxRange = w.MaxRange,
        FalloffStart = w.FalloffStart,
        FalloffFloor = w.FalloffFloor,
        StockMeleeDamageMin = w.StockMeleeDamageMin,
        StockMeleeDamageMax = w.StockMeleeDamageMax,
        StockMeleeInterval = w.StockMeleeInterval,
        StockMeleePenetration = w.StockMeleePenetration,
    };
}
