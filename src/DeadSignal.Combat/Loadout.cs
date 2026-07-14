namespace DeadSignal.Combat;

/// <summary>负重分档（用户拍板的三档 + 硬上限）。</summary>
public enum LoadoutTier
{
    /// <summary>&lt; 30kg：无影响，自由行动。</summary>
    Unencumbered,

    /// <summary>30 ~ 50kg：轻度 debuff（移速）。</summary>
    Encumbered,

    /// <summary>50 ~ 80kg：重度 debuff（移速加重 + 开始拖慢出手）。</summary>
    Strained,

    /// <summary>
    /// &gt; 80kg：**不允许**（硬上限，拾取处直接拦）。
    /// 正常玩法不可达；只在**上限中途下降**（关内断手/饿掉一档/狗跑了）时，已背在身上的东西会落进这一档——
    /// 东西不会凭空消失，但你会被拖到几乎走不动。
    /// </summary>
    Overloaded,
}

/// <summary>
/// 负重上限与分档惩罚（纯函数）。**用户口径**：30kg 以下无影响、30~50kg 有 debuff、50~80kg debuff 加重、**不能超过 80kg**。
/// <para>
/// [T45] 这本账里装的是**一个人身上的全部重量**：左右手的武器 + 11 槽护甲（消费层 <c>GearWeight</c>）+ 他分摊到的战利品。
/// 所以"把枪改装得很强"的代价是——**它吃掉你的搜刮余量**（满改装步枪比原厂多占 3.5kg，那就是少搬 3.5kg 货回家）。
/// <b>不是"出门就减速"</b>：普通配置出门离免罚线还远得很，是**你搜的东西**把你推过线的。
/// 见 <see cref="BaseCarryLimitKg"/> 上方的余量表。
/// </para>
/// <para>
/// 上限是**硬的**（<see cref="CanCarry"/>／消费层 <c>ExpeditionBag</c> 在拾取处拦截）——装不下就是拿不走，
/// 而不是"超重了慢慢挪"。硬上限才制造取舍；软惩罚负责让"背得多"本身有代价。
/// </para>
/// <para>
/// <b>阈值是上限的比例，不是写死的公斤数</b>（30/80＝<see cref="FreeRatio"/>、50/80＝<see cref="StrainRatio"/>）：
/// 这样任何抬高上限的乘子（山姆的 ×1.15、全营 ×1.03）都会把**三档整体上浮**，而不只是把终点线往后挪——
/// 他"从小帮祖母打理农庄"体现在每一档都比别人扛得住。反过来，残缺/饥饿把上限乘小，三档也一起收紧。
/// </para>
/// 本项目**没有"力量/体力"属性**（铁律：能力只由 authored 专属效果 + 读过的书承载），故 <see cref="CarryLimit"/>
/// 的基数对所有人一视同仁。曲线参数皆**拟定待调**；引擎只出数值，作用于移速/出手间隔由消费层消费。
/// </summary>
public static class Loadout
{
    // ---- 用户拍板的三个公斤数（基准人：无残缺、不饿、无专属加成）----
    //
    // 🔴 [T45·负重激活] **装备（武器 + 护甲）现在计入这本账**（此前只算搜刮来的战利品，出门是空包 ⇒ 负重恒 0kg）。
    //     三条线**保持 30/50/80 不动** —— 用户拍板。
    //
    // ⚠️ 曾经有人（我）提议把三条线随"装备进账"整体减半（30/50/80 → 15/25/40），理由是"账的口径变了"。
    //    **用户否掉了，而且他是对的。** 用户原话：
    //      「**不改啊，就应当是 30/50/80。带装备出门，随便搜点就超 30 了。如果全身重甲+重武器（单板甲就 15 了），
    //        那出门就差不多 30 了，能搜的空间会很小。**」
    //
    // 🔴 **负重的代价不是"出门就减速"，而是「装备把你的搜刮余量吃掉了」。** 这是本系统的设计核心，别搞反：
    //
    //   配置                              出门实重   免罚余量(到30)   硬余量(到80)   搬得空住宅区(66kg)吗
    //   开局(匕首+布衣+长裤+鞋)             1.30kg      28.7kg          78.7kg        ✅ 轻松
    //   中期(步枪+皮甲+军用头盔)            13.80kg      16.2kg          66.2kg        ✅ **刚好**（只富余 0.2kg）
    //   中期 + 满改装步枪(4.0→8.1)          17.90kg      12.1kg          62.1kg        ❌ 留 3.9kg 在原地
    //   重装(狙击+板甲+防暴头盔)            26.90kg       3.1kg          53.1kg        ❌ 留 12.9kg 在原地
    //   重装 + 满改装狙击(6.0→12.15)        33.05kg       0kg            47.0kg        ❌ 留 19.0kg 在原地
    //
    // 读法（每一行都是用户那句话的一个侧面）：
    // · **出门时没有一个配置在罚**（重装 26.9kg 也还在 30kg 线下）——"出门就 debuff"从来不是设计意图。
    // · 板甲重装的余量只剩 **3.1kg** ⇒ **搜两根木料(4kg)就掉进负重档**。这就是用户说的"能搜的空间会很小"。
    // · 改装的 **+4.1kg** 不是抽象数字：它正好是"**刚好搬得空最大点位**"与"**搬不空**"的分界线。
    //   ⇒ 「你可以把枪改装得很强，但你得接受空手而归」——**代价是搜刮余量，不是移速**。
    //
    // ⇒ 「**要么带甲、要么带货**」这个取舍照样成立，而且是通过**余量**实现的，不是"出门即罚"——
    //    这比出门就减速更好：**它把选择权留给玩家**。
    //
    // 三条线不动还有一个结构性好处：轻度档(30~50)与重度档(50~80)的宽度不变 ⇒
    // 「重度档每公斤更陡」的既有护栏（`MechanicsTests.HeavyTierIsSteeperThanLightTier`）自然成立，无需迁就。
    //
    // 装备重量的实测见 `CarryLoadWiringTests`（全部由 ArmorTable/ItemWeights 复算，不写死）；
    // 满改装武器的实重**权威在 `WeaponModCatalog`**（impl-weaponmod 所有），上表是快照。

    /// <summary>硬上限：**不能超过 80kg**（用户原话）。装备也算在里面 —— 穿得越重，能搬回来的越少。</summary>
    public const double BaseCarryLimitKg = 80.0;

    /// <summary>
    /// 无影响线：30kg 以下自由行动。
    /// [T45] 装备进账后，这条线的意义变了——它不再是"你搜了多少"，而是"**装备之外你还能搜多少**"：
    /// 板甲重装出门 26.9kg，离这条线只剩 3.1kg ⇒ 搜两根木料就越线。
    /// </summary>
    public const double FreeThresholdKg = 30.0;

    /// <summary>加重线：50kg 起 debuff 加重。</summary>
    public const double StrainThresholdKg = 50.0;

    // ---- 比例化（随上限乘子整体伸缩）----

    /// <summary>无影响线占上限的比例（30/80 = 0.375）。</summary>
    public const double FreeRatio = FreeThresholdKg / BaseCarryLimitKg;

    /// <summary>加重线占上限的比例（50/80 = 0.625）。</summary>
    public const double StrainRatio = StrainThresholdKg / BaseCarryLimitKg;

    // ---- debuff 曲线：🔴 **用户拍板的四个数，不是"拟定待调"** ----
    //
    // 用户原话：「**惩罚我目前预想的是：50kg 减少 20% 移动速度和攻击速度；80kg 减少 80% 移动速度和 50% 攻击速度；
    //             30-50，50-80 线性变化**」
    //
    //   负重      移速乘子   攻速乘子
    //   ≤30kg      1.00       1.00      ← 平坦，无惩罚
    //   50kg       0.80       0.80      ← 两条一起 −20%
    //   80kg       0.20       0.50      ← 移速只剩两成（**走不动了**）；攻速腰斩
    //   30→50      线性        线性
    //   50→80      线性        线性
    //
    // 🔴 **移速在满载时掉到 0.20 是有意的**（贪多 ⇒ 基本走不动 ⇒ 被丧尸追上）。别当笔误"修"回 0.5。
    //
    // 🔴 **攻速现在从 30kg 就开始罚**（旧口径是"轻度档不罚攻速、背 30kg 挥剑没影响"，**用户已推翻**）。
    //
    // 两档的每公斤梯度（这是理解这条曲线的关键）：
    //   移速：轻档 20%/20kg = **1.0%/kg** → 重档 60%/30kg = **2.0%/kg**（**加速恶化**）
    //   攻速：轻档 20%/20kg = **1.0%/kg** → 重档 30%/30kg = **1.0%/kg**（**两档等陡**）
    // ⇒ **负重压垮的首先是你的腿，不是你的手**：越重，走路的恶化越快，而挥刀的恶化是匀速的。
    //   故既有护栏 `HeavyTierIsSteeperThanLightTier`（只断言移速）**照样成立**：2.0 > 1.0。
    //   若日后有人把那条护栏推广到攻速，请用"重档 ≥ 轻档"（非严格）——**不许为了让护栏绿去动这四个数**。

    /// <summary>轻度档末（＝加重线，基准人 50kg）的移速乘子：**−20%**，负重行军，累但还跑得动。</summary>
    public const double SpeedAtStrain = 0.80;

    /// <summary>
    /// 重度档末（＝满上限，基准人 80kg）的移速乘子：**−80%，只剩两成速度**。
    /// 走得动，但**逃不掉**——贪心的代价（用户拍板，有意的极重惩罚）。
    /// </summary>
    public const double SpeedAtLimit = 0.20;

    /// <summary>
    /// 轻度档末（＝加重线，基准人 50kg）的出手间隔乘子：**−20%**（与移速同步）。
    /// ⚠️ 旧口径「轻度档不罚攻速」**已被用户推翻**——攻速从免罚线（30kg）起就开始线性掉。
    /// </summary>
    public const double AttackSpeedAtStrain = 0.80;

    /// <summary>重度档末（＝满上限，基准人 80kg）的出手间隔乘子：**−50%，攻速腰斩**。</summary>
    public const double AttackSpeedAtLimit = 0.50;

    /// <summary>超上限（正常不可达）每超 100% 额外扣的移速斜率（陡峭）。</summary>
    public const double OverloadSlope = 0.80;

    /// <summary>速度乘子下限（再重也不至于完全钉死在地上）。</summary>
    public const double MinMultiplier = 0.10;

    /// <summary>
    /// 一个人的负重上限（kg）＝ <see cref="BaseCarryLimitKg"/> × 承载能力 × authored 专属乘子。
    /// </summary>
    /// <param name="carryCapability">
    /// 承载能力 0~1：断手/饿肚子背不动。消费层直接喂 <c>Pawn.OperationCapability</c>
    /// （＝<c>HungerState.CombineCapability(残疾操作惩罚, 饥饿惩罚)</c>，与战斗出手间隔同源口径），不另造一套数学。
    /// 用户只拍了三档公斤数、没提残缺——按**乘算通则**接在这里：断一只手 → 上限（连同三档阈值）对折。
    /// </param>
    /// <param name="capacityMultiplier">
    /// authored 专属效果乘子（默认 1.0＝无加成）。现阶段唯一来源是**山姆"英雄风范"**：
    /// L2 他自己 ×1.15、L3 全营 ×1.03，山姆本人两者**连乘** ×1.15×1.03（≠ 加算的 ×1.18）。
    /// 负数按 0 钳制。
    /// </param>
    public static double CarryLimit(double carryCapability = 1.0, double capacityMultiplier = 1.0)
        => BaseCarryLimitKg * Math.Clamp(carryCapability, 0.0, 1.0) * Math.Max(0, capacityMultiplier);

    /// <summary>此人的"无影响线"（kg）——上限的 <see cref="FreeRatio"/>；山姆的线比别人高。</summary>
    public static double FreeThresholdFor(double carryLimit) => Math.Max(0, carryLimit) * FreeRatio;

    /// <summary>此人的"加重线"（kg）——上限的 <see cref="StrainRatio"/>。</summary>
    public static double StrainThresholdFor(double carryLimit) => Math.Max(0, carryLimit) * StrainRatio;

    /// <summary>背得动吗（**硬上限**：超过就是拿不走）。</summary>
    public static bool CanCarry(double totalWeight, double carryLimit)
        => totalWeight <= carryLimit + 1e-9;

    public static LoadoutTier TierOf(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);
        if (ratio <= FreeRatio)
        {
            return LoadoutTier.Unencumbered;
        }

        if (ratio <= StrainRatio)
        {
            return LoadoutTier.Encumbered;
        }

        return ratio <= 1.0 ? LoadoutTier.Strained : LoadoutTier.Overloaded;
    }

    /// <summary>
    /// 移速乘子（1.0 = 无惩罚）。
    /// &lt;30kg: 1.0；30~50kg: 线性降到 <see cref="SpeedAtStrain"/>；50~80kg: 更陡地降到 <see cref="SpeedAtLimit"/>；
    /// &gt;80kg（硬上限外，仅上限中途下降时可达）：以 <see cref="OverloadSlope"/> 陡降，下限 <see cref="MinMultiplier"/>。
    /// </summary>
    public static double SpeedMultiplier(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);

        if (ratio <= FreeRatio)
        {
            return 1.0;
        }

        if (ratio <= StrainRatio)
        {
            double t = (ratio - FreeRatio) / (StrainRatio - FreeRatio); // 0..1
            return 1.0 - t * (1.0 - SpeedAtStrain);
        }

        if (ratio <= 1.0)
        {
            double t = (ratio - StrainRatio) / (1.0 - StrainRatio); // 0..1
            return SpeedAtStrain - t * (SpeedAtStrain - SpeedAtLimit);
        }

        double over = ratio - 1.0;
        return Math.Max(MinMultiplier, SpeedAtLimit - over * OverloadSlope);
    }

    /// <summary>
    /// 出手间隔乘子（1.0 = 无惩罚，&lt;1 = 攻速变慢）。**与移速同构的两段线性**（用户拍板）：
    /// &lt;30kg: 1.0；30~50kg: 线性降到 <see cref="AttackSpeedAtStrain"/>（0.80）；
    /// 50~80kg: 线性降到 <see cref="AttackSpeedAtLimit"/>（0.50，**攻速腰斩**）。
    /// <para>
    /// ⚠️ **旧口径「只有重度档才罚攻速、背 30kg 挥剑没什么影响」已被用户推翻**：
    /// 现在攻速和移速一样，从免罚线（30kg）起就开始掉。
    /// 但两者的**恶化速度不同**——移速 1%/kg → 2%/kg（加速），攻速恒 1%/kg（匀速）：
    /// <b>负重压垮的首先是你的腿，不是你的手</b>。
    /// </para>
    /// <para>
    /// 刻意**不碰工时/操作能力**——那是残缺与饥饿的地盘（<c>Pawn.OperationCapability</c>）。
    /// 负重已经通过 <see cref="CarryLimit"/> 的 carryCapability 与它们相乘过一次，再扣一遍就是双重惩罚。
    /// </para>
    /// </summary>
    public static double AttackSpeedMultiplier(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);

        if (ratio <= FreeRatio)
        {
            return 1.0;
        }

        if (ratio <= StrainRatio)
        {
            double t = (ratio - FreeRatio) / (StrainRatio - FreeRatio); // 0..1
            return 1.0 - t * (1.0 - AttackSpeedAtStrain);
        }

        if (ratio <= 1.0)
        {
            double t = (ratio - StrainRatio) / (1.0 - StrainRatio); // 0..1
            return AttackSpeedAtStrain - t * (AttackSpeedAtStrain - AttackSpeedAtLimit);
        }

        double over = ratio - 1.0;
        return Math.Max(MinMultiplier, AttackSpeedAtLimit - over * OverloadSlope);
    }

    private static double Ratio(double totalWeight, double carryLimit)
    {
        if (carryLimit <= 0)
        {
            return totalWeight > 0 ? double.PositiveInfinity : 0;
        }

        return Math.Max(0, totalWeight) / carryLimit;
    }
}
