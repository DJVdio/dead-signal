using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // IRandomSource（纯 C# 引擎，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 VisionLogic.cs / DougBruceBond.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载批次6 夜防「警戒力 vs 潜行力」对抗规则（用户口径：站岗者用视力/听力/岗哨建筑加成得警戒力，
// 袭击者用光照/服饰/距离/视野遮蔽得潜行力，双方 roll 点对抗）。
// 视力/光照/距离/遮蔽的底层输入复用 VisionLogic（同命名空间纯逻辑）；听力为本批新增轴。
// 空间执行（守卫/袭击者位置、raycast 遮挡、拉人入战、实扣物资）归 night-response 运行时层，本类只出纯函数。

/// <summary>袭击者意图（未被发现时决定后果）。</summary>
public enum RaiderIntent
{
    /// <summary>杀戮型：未被发现 → 潜行先手一击（伤害×<see cref="NightWatchContest.PreemptiveStrikeMultiplier"/>）。</summary>
    Killer,
    /// <summary>劫掠型：未被发现 → 静默潜入偷走物资（晨间发现损失）。</summary>
    Looter,
}

/// <summary>
/// 袭击响应危险分级（站岗者发现敌人后由玩家选择）。
/// ★共用枚举单一事实源：本类（watch-contest）定义，shift-sleep 的 ShiftSchedule 引用消费
/// （其 ShiftSchedule.cs 已明确不重定义，归本文件）。成员名 Low/Medium/High 与 shift-sleep 现有用法对齐。
/// </summary>
public enum RaidTier
{
    /// <summary>低危：仅站岗守卫加入战斗。</summary>
    Low,
    /// <summary>中危：除睡觉的探险队外全部加入（守卫+夜班生产者）。</summary>
    Medium,
    /// <summary>高危：所有人投入战斗（含唤醒睡眠的探险队）。</summary>
    High,
}

/// <summary>营地成员的夜间班别（决定其在三档响应中是否参战）。</summary>
public enum WatchDuty
{
    /// <summary>站岗守卫（夜班·清醒·执勤）。</summary>
    Guard,
    /// <summary>夜班生产者（夜班·清醒·在工作台）。</summary>
    Producer,
    /// <summary>探险队（昼班·夜里强制睡眠）。</summary>
    Expedition,
}

/// <summary>一名营地成员的班别与睡眠状态（供三档响应名册裁定）。</summary>
public readonly struct WatchMember
{
    /// <summary>成员标识（Pawn Id 或狗标识，由调用方给定）。</summary>
    public string Id { get; }
    /// <summary>夜间班别。</summary>
    public WatchDuty Duty { get; }
    /// <summary>当前是否在睡眠（被拉入战斗则由 shift-sleep 施次相位疲劳 debuff）。</summary>
    public bool Asleep { get; }

    public WatchMember(string id, WatchDuty duty, bool asleep)
    {
        Id = id;
        Duty = duty;
        Asleep = asleep;
    }
}

/// <summary>一次警戒 vs 潜行对抗的结算结果。</summary>
public readonly struct ContestResult
{
    /// <summary>站岗方是否发现了袭击者。</summary>
    public bool Detected { get; }
    /// <summary>本次结算的警戒力（clamp ≥0）。</summary>
    public float Alertness { get; }
    /// <summary>本次结算的潜行力（clamp ≥0）。</summary>
    public float Stealth { get; }
    /// <summary>解析出的发现概率 = 警戒力/(警戒力+潜行力)。</summary>
    public float DetectionChance { get; }
    /// <summary>发现距离（仅 <see cref="Detected"/> 有意义；未发现为 0）。</summary>
    public float DetectionDistance { get; }

    public ContestResult(bool detected, float alertness, float stealth, float chance, float distance)
    {
        Detected = detected;
        Alertness = alertness;
        Stealth = stealth;
        DetectionChance = chance;
        DetectionDistance = distance;
    }
}

/// <summary>
/// 夜防「警戒力 vs 潜行力」对抗纯逻辑（批次6 权威源）。全静态纯函数 + draft 常量。
/// 消费 pipeline（night-response 运行时层每次袭营/每个袭击者对每个守卫）：
///   1. 守卫视锥经 VisionLogic：L=CombineLight(AmbientLight(相位,indoorsDark), 光源贡献)；cone=ConeFor(L[,R0])；
///      canSee=VisionLogic.CanSee(守卫位/朝向, 袭击者位, cone, occluded)；acuity=<see cref="VisionAcuity"/>(canSee,dist,cone)。
///   2. alertness=<see cref="ComputeAlertness"/>(acuity,dist,岗哨加成,疲劳系数,站岗效率[布鲁斯 0.75])。
///   3. 袭击者局部光照 L'（同 VisionLogic）；stealth=<see cref="ComputeStealth"/>(L',服饰潜行合计,dist,遮蔽权重)。
///   4. result=<see cref="Resolve"/>(alertness,stealth,rng,dist)。
///      · Detected → 站岗者拉响警报，玩家选 <see cref="RaidTier"/> → <see cref="RespondersFor"/> 出参战名册。
///      · Undetected → 按袭击者意图：Killer 走 <see cref="PreemptiveDamageMultiplier"/> 先手；Looter 走 <see cref="SilentTheftAmount"/> 偷窃。
/// 所有数值常量皆「拟定待调」，由 Sim/目视校准。
/// </summary>
public static class NightWatchContest
{
    // ── 警戒力权重（拟定待调）─────────────────────────────────────────────────
    /// <summary>视力项权重：满档清晰目击贡献的警戒量。</summary>
    public const float VisionWeight = 1.0f;
    /// <summary>听力项权重：满档（贴身）听觉贡献的警戒量（弱于直接目视）。</summary>
    public const float HearingWeight = 0.6f;
    /// <summary>听力基值半径（世界像素，新轴）：袭击者在此半径内可被"听见"，随距离线性衰减到 0。</summary>
    public const float HearingBaseRange = 220f;

    // ── 潜行力权重（拟定待调）─────────────────────────────────────────────────
    /// <summary>黑暗项权重：局部光照越低越隐蔽（贡献 = 权重×(1-L)）。</summary>
    public const float StealthDarknessWeight = 1.0f;
    /// <summary>服饰项权重：服饰潜行值合计的放大系数。</summary>
    public const float StealthApparelWeight = 1.0f;
    /// <summary>距离项权重：越远越难被察觉。</summary>
    public const float StealthDistanceWeight = 0.6f;
    /// <summary>距离项归一参考（世界像素）：距离≥此值时距离项达满档。</summary>
    public const float StealthDistanceReference = 300f;
    /// <summary>遮蔽项权重：视野遮蔽物权重 [0,1] 的放大系数。</summary>
    public const float StealthCoverWeight = 0.5f;

    // ── 未发现后果（拟定待调）─────────────────────────────────────────────────
    /// <summary>杀戮意图潜行先手伤害乘数（用户口径 1.5x）。</summary>
    public const float PreemptiveStrikeMultiplier = 1.5f;
    /// <summary>静默偷窃单位数下限（量级拟定）。</summary>
    public const int SilentTheftMinUnits = 1;
    /// <summary>静默偷窃单位数上限（量级拟定）。</summary>
    public const int SilentTheftMaxUnits = 4;

    private const float Epsilon = 1e-4f;

    /// <summary>
    /// 服饰潜行值目录（物品标识 → 潜行贡献，拟定待调）。未登记的服饰不贡献潜行值。
    /// 本处以本地小表承载「服饰潜行属性」，不侵入 <see cref="ApparelSlots"/>（保持其只管穿哪/防哪）；
    /// 数值日后可挪 json。ApparelCatalog 已登记的服饰名与此对齐。
    /// </summary>
    public const string DarkCloakId = "夜行斗篷";

    /// <remarks>
    /// 覆盖率是硬门禁：<c>ApparelCatalog.Defs</c> 里每一件人形穿戴品都必须在此登记（见
    /// NightWatchContestTests.ApparelStealth_CoversEveryWearableInCatalog）。<b>新增护甲/穿戴品请同步补一行</b>——
    /// 布夹克/牛仔外套/花衬衫当初就是这样漏掉的（新增了护甲，没人想起潜行表）。
    /// 核定为中性的显式登记 0f（＝已核对，区别于"忘了给"）。
    /// 直觉口径：深色/柔软/贴身/不反光 → 正；鲜艳/刚性/沉重/摩擦作响 → 负。数值皆「拟定待调」。
    /// 狗装备（布制/皮制/口袋狗衣、铁皮/铁丝头甲）不登记：夜防对抗里狗是**守方**（布鲁斯站岗），不作为袭击者，
    /// 潜行力这条轴对它无入口。
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, float> ApparelStealth = new Dictionary<string, float>
    {
        // ── 专门的潜行装（既有基准，勿漂移）──
        [DarkCloakId] = 0.30f,        // 深色斗篷：显著潜行
        ["软底鞋"] = 0.15f,           // 消音脚步

        // ── 外套层（五件互斥）：轻软 > 朴素 > 厚重挺括 > 反光皮革 ──
        ["布夹克"] = 0.06f,           // 0.3kg，轻软贴身、无硬壳、动起来不出声——外套层潜行最佳
        ["粗布外套"] = 0.05f,         // 无反光的朴素外套，微弱潜行（基准件）
        ["粗布背心"] = 0.03f,         // 朴素但无袖，遮得少于外套
        ["牛仔外套"] = 0.02f,         // 0.6kg 厚牛仔，布料挺括、走动摩擦沙沙响；胜在仍是深色布
        ["皮夹克"] = -0.02f,          // 皮革吱呀作响，表面还反光（"倍有范儿"＝锃亮）

        // ── 贴身层（互斥两件）：素色遮皮肤 vs 花色自曝 ──
        ["长袖布衣"] = 0.03f,         // 素色贴身布，遮住裸露皮肤的反光
        ["花衬衫"] = -0.08f,          // "够艳，够喜庆"——夜里就是一团彩色，风味文案本身在说它显眼

        // ── 裤/鞋/手 ──
        ["长裤"] = 0.02f,             // 遮腿、不反光，聊胜于无
        ["短裤"] = -0.02f,            // 裸腿在月光下发白，反倒扎眼
        ["运动鞋"] = 0.04f,           // 橡胶软底，脚步轻——但不及专门的软底鞋
        ["劳保手套"] = 0.00f,         // 核定中性：既不消音也不显眼

        // ── 护甲层（三件互斥）：刚性件一律为负，越重越糟 ──
        ["皮革胸甲"] = -0.05f,        // 刚性护心甲，束住上身、皮革吱呀
        ["皮甲"] = -0.08f,            // 整身鞣皮甲，更硬更沉
        ["板甲"] = -0.35f,            // 15kg 铁罐头摸黑：金属哐当、反光、动作迟缓。一件就吃掉一整件夜行斗篷还有余

        // ── 头部护甲（[SPEC-B19]，两件互斥）──
        ["军用头盔"] = -0.06f,        // 硬盔壳压着耳朵，听不清也藏不住；1.5kg 顶在头上，动作跟着变笨
        ["防暴头盔"] = -0.12f,        // 2.4kg + 一整片聚碳酸酯面罩：更沉，面罩在月光下反光，整张脸罩住＝听觉视觉全闷

        // ── 纯覆盖品 ──
        ["防毒面具"] = -0.03f,        // 滤罐呼哧作响；胜在遮脸不反光，故只是小负
    };

    /// <summary>已装服饰列表 → 潜行值合计（未登记项贡献 0）。</summary>
    public static float ApparelStealthSum(IEnumerable<string> equippedItems)
    {
        if (equippedItems is null)
            return 0f;
        float sum = 0f;
        foreach (var item in equippedItems)
        {
            if (item is not null && ApparelStealth.TryGetValue(item, out var v))
                sum += v;
        }
        return sum;
    }

    /// <summary>
    /// 视力项 [0,1]：由 VisionLogic 判定的目击转成警戒贡献。看不见（出锥/遮蔽/超视距）→ 0；
    /// 看得见时越近越清晰（1 - 距离/视距），贴脸满值。<paramref name="cone"/> 用 <see cref="VisionLogic.ConeFor(float)"/> 得出。
    /// </summary>
    public static float VisionAcuity(bool canSee, float distance, VisionLogic.VisionCone cone)
    {
        if (!canSee)
            return 0f;
        if (cone.Range <= Epsilon)
            return 1f; // 视距≈0 但判定可见（贴身重合）→ 满值
        float d = Math.Clamp(distance, 0f, cone.Range);
        return 1f - d / cone.Range;
    }

    /// <summary>听力衰减 [0,1]：距离 0 处满档，达 <paramref name="hearingRange"/> 及以外为 0，线性衰减。</summary>
    public static float HearingFalloff(float distance, float hearingRange)
    {
        if (hearingRange <= 0f)
            return 0f;
        float t = 1f - Math.Max(0f, distance) / hearingRange;
        return Math.Clamp(t, 0f, 1f);
    }

    /// <summary>
    /// 站岗方警戒力 = (视力项 + 听力项)×站岗效率 + 岗哨建筑加成，再乘疲劳系数。
    /// </summary>
    /// <param name="visionAcuity">视力项 [0,1]，见 <see cref="VisionAcuity"/>。</param>
    /// <param name="distance">袭击者到守卫的距离（世界像素，供听力衰减）。</param>
    /// <param name="structureBonus">岗哨建筑加成（加法项，≥0，由 <see cref="GuardPostStats"/> 映射，如哨塔更高更远）。</param>
    /// <param name="fatigueMultiplier">疲劳系数 (0,1]，1=无疲劳，被唤醒者次相位 &lt;1（shift-sleep 供给）。</param>
    /// <param name="watchEfficiency">站岗效率系数，人类=1，布鲁斯=0.75（consumer 传入 <c>DougBruceBond.BruceGuardEfficiency</c>，本类不硬编狗）。</param>
    /// <param name="hearingRange">听力基值半径（默认 <see cref="HearingBaseRange"/>）。</param>
    public static float ComputeAlertness(
        float visionAcuity,
        float distance,
        float structureBonus = 0f,
        float fatigueMultiplier = 1f,
        float watchEfficiency = 1f,
        float hearingRange = HearingBaseRange)
    {
        float vision = VisionWeight * Math.Clamp(visionAcuity, 0f, 1f);
        float hearing = HearingWeight * HearingFalloff(distance, hearingRange);
        float perception = (vision + hearing) * Math.Max(0f, watchEfficiency);
        float fatigue = Math.Clamp(fatigueMultiplier, 0f, 1f);
        float raw = (perception + Math.Max(0f, structureBonus)) * fatigue;
        return Math.Max(0f, raw);
    }

    /// <summary>距离项归一 [0,1]：距离/参考，clamp（<see cref="StealthDistanceReference"/> 恒正）。</summary>
    public static float DistanceFactor(float distance)
        => Math.Clamp(Math.Max(0f, distance) / StealthDistanceReference, 0f, 1f);

    /// <summary>
    /// 袭击者潜行力 = 黑暗项 + 服饰项 + 距离项 + 遮蔽项。
    /// </summary>
    /// <param name="lightLevel">袭击者所在局部光照 L∈[0,1]（VisionLogic.CombineLight 结果）；越低越隐蔽。</param>
    /// <param name="apparelStealthSum">服饰潜行值合计，见 <see cref="ApparelStealthSum"/>；<b>可为负</b>（板甲/花衬衫一类是减分项）。</param>
    /// <param name="distance">袭击者到守卫的距离（世界像素）。</param>
    /// <param name="coverWeight">视野遮蔽物权重 [0,1]（袭击者身处掩体的程度）。</param>
    public static float ComputeStealth(
        float lightLevel,
        float apparelStealthSum,
        float distance,
        float coverWeight)
    {
        float darkness = StealthDarknessWeight * (1f - Math.Clamp(lightLevel, 0f, 1f));
        // 服饰项**不得夹到 0**：旧实现写的是 Max(0f, apparelStealthSum)，等于把一切负系数原地吞掉——
        // 穿一身板甲摸黑与赤身潜行完全等价。负系数要能真正抵扣黑暗/距离/掩体带来的隐蔽，方向才成立。
        // 总潜行力仍在下面统一 clamp 到 ≥0（潜行力不为负）。
        float apparel = StealthApparelWeight * apparelStealthSum;
        float distTerm = StealthDistanceWeight * DistanceFactor(distance);
        float cover = StealthCoverWeight * Math.Clamp(coverWeight, 0f, 1f);
        return Math.Max(0f, darkness + apparel + distTerm + cover);
    }

    /// <summary>
    /// 发现概率 = 警戒力/(警戒力+潜行力)。双方皆 0 → 0.5（五五开）。这是 <see cref="Resolve"/> 掷点所对的阈值。
    /// </summary>
    public static float DetectionChance(float alertness, float stealth)
    {
        float a = Math.Max(0f, alertness);
        float s = Math.Max(0f, stealth);
        float total = a + s;
        if (total <= Epsilon)
            return 0.5f;
        return a / total;
    }

    /// <summary>
    /// 对抗结算：掷 [0,1) 一点，低于 <see cref="DetectionChance"/> → 站岗方发现袭击者，否则未发现。
    /// 随机走可注入 <see cref="IRandomSource"/>（测试用 <see cref="SequenceRandomSource"/> 复现）。
    /// </summary>
    /// <param name="encounterDistance">交战当刻袭击者距守卫距离，记入结果发现距离（未发现置 0）。</param>
    public static ContestResult Resolve(float alertness, float stealth, IRandomSource rng, float encounterDistance = 0f)
    {
        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        float a = Math.Max(0f, alertness);
        float s = Math.Max(0f, stealth);
        float chance = DetectionChance(a, s);
        double roll = rng.Range(0.0, 1.0);
        bool detected = roll < chance;
        return new ContestResult(detected, a, s, chance, detected ? Math.Max(0f, encounterDistance) : 0f);
    }

    /// <summary>未被发现·意图对应先手伤害乘数：Killer→1.5x 潜行先手，Looter→1（不打，走偷窃）。</summary>
    public static float PreemptiveDamageMultiplier(RaiderIntent intent)
        => intent == RaiderIntent.Killer ? PreemptiveStrikeMultiplier : 1f;

    /// <summary>
    /// 未被发现·劫掠意图 → 静默偷窃单位数。量级 [<see cref="SilentTheftMinUnits"/>,<see cref="SilentTheftMaxUnits"/>]，
    /// 并受可偷存量 <paramref name="stealableUnits"/> 封顶（空仓偷不到）。随机走 <see cref="IRandomSource"/>。
    /// "偷什么"由 night-response 从库存挑目标资源，本函数只出"偷多少"。
    /// </summary>
    public static int SilentTheftAmount(int stealableUnits, IRandomSource rng)
    {
        if (rng is null)
            throw new ArgumentNullException(nameof(rng));

        int cap = Math.Max(0, stealableUnits);
        if (cap == 0)
            return 0;

        int lo = Math.Min(SilentTheftMinUnits, cap);
        int hi = Math.Min(SilentTheftMaxUnits, cap);
        if (hi <= lo)
            return hi;

        // rng.Range 连续 [0,1] → 均匀映射到整数 [lo, hi]（含端点）。
        double r = rng.Range(0.0, 1.0);
        int span = hi - lo + 1;
        int pick = lo + (int)(r * span);
        return Math.Min(pick, hi);
    }

    /// <summary>
    /// 三档响应参战名册（用户口径）：
    ///   低危 = 仅站岗守卫；中危 = 除睡觉的探险队外全部（守卫+生产者）；高危 = 全员（含唤醒探险队）。
    /// 被唤醒的睡眠者（见 <see cref="AwokenSleepers"/>）由 shift-sleep 施次相位疲劳 debuff。
    /// </summary>
    public static IReadOnlyList<WatchMember> RespondersFor(RaidTier tier, IEnumerable<WatchMember> roster)
    {
        if (roster is null)
            return Array.Empty<WatchMember>();

        var all = roster.ToList();
        return tier switch
        {
            RaidTier.Low => all.Where(m => m.Duty == WatchDuty.Guard).ToList(),
            // 除"睡觉的探险队"外全部——非睡眠探险队者（守卫/生产者/万一清醒的探险队）皆入战。
            RaidTier.Medium => all.Where(m => !(m.Duty == WatchDuty.Expedition && m.Asleep)).ToList(),
            RaidTier.High => all,
            _ => (IReadOnlyList<WatchMember>)Array.Empty<WatchMember>(),
        };
    }

    /// <summary>本档响应中被拉入战斗的睡眠者（参战名册里 Asleep 者）→ 供 shift-sleep 施次相位疲劳 debuff。</summary>
    public static IReadOnlyList<WatchMember> AwokenSleepers(RaidTier tier, IEnumerable<WatchMember> roster)
        => RespondersFor(tier, roster).Where(m => m.Asleep).ToList();
}
