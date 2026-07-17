namespace DeadSignal.Combat;

/// <summary>
/// 一个部位上那**唯一**一处出血（[T58] 三级流血）。
/// <para><see cref="Severity"/>＝口子多大（小/中/大，合并后的结果）；
/// <see cref="RateMultiplier"/>＝造成它的武器让它流得多快（锯齿剑刃 1.4）。</para>
/// </summary>
public readonly record struct BleedWound(BleedModel.BleedSeverity Severity, double RateMultiplier);

/// <summary>出血伤口的致命性分级（按受伤部位）。</summary>
public enum BleedTier
{
    /// <summary>微小（指/趾/眼/面/耳）：擦伤级，战后自愈，战斗内几乎不失血、绝不致死。</summary>
    Micro,

    /// <summary>轻微（手/脚）：会失血、会让人虚弱，但**不致命**（战后只溃烂感染，需手术）。</summary>
    Minor,

    /// <summary>致命（躯干/头/颈/手臂/大腿等大部位）：深伤口，短时间内可放干致死。</summary>
    Lethal,
}

/// <summary>
/// 出血的部位分级模型 —— **战斗内失血与战后伤病建档共用的单一事实源**。
///
/// <para>
/// 用户口径：「流血应当是短时间内危险致死的，多个严重流血伤口可能会导致这场战斗还没打出胜负就流血致死了」；
/// 同时小伤口（手/脚/指）**不致命**，只溃烂感染。二者合起来 = **有梯度的致命性**：
/// 打中要害才放血放到死，划破手指不会。
/// </para>
///
/// <para>
/// 此前引擎的战斗内失血是**无梯度**的（每处伤口一律同速、且能把人放干），
/// 与 Godot 战后伤病系统 <c>HealthMapping</c> 已有的三层梯度（自愈/非致命/致命失血）对不上 ——
/// 一道手指划伤能把人流血流死。本类把那套分级下沉到引擎，两边现在读同一个函数。
/// </para>
///
/// 数值全部**拟定待调**。
/// </summary>
public static class BleedModel
{
    // ================= 【T58】三级流血（用户拍板，原话即规格）=================
    //
    // 「原本的一个部位最多造成三个伤口，改为三级流血。**小流血 中流血 大流血**。
    //   两个小流血变中流血，两个中流血变大流血，一个小一个中也变大流血。
    //   一次伤害造成了锐器伤害，就是小流血。一次大于该部位最大生命值的 30%，就中流血。
    //   一次超过 60% 就造成大流血。**封顶大流血。**」
    // 追加：「这样也能防止过多的伤口浪费手术时间」⇒ **合并 ⇒ 三个小口合成一个大口 = 一台手术，不是三台。**
    //
    // ⇒ **每个部位【最多只存在一处】出血**，等级 = 小/中/大，**合并是即时的**（每次挂新流血立刻并）。
    //   这是"封顶大流血"唯一自洽的读法：若允许一个部位同时存在多处流血，"封顶"就无从谈起
    //   （两处大流血比一处大流血更狠，等于没封顶）。「大 + 小」按封顶＝**仍是大**。
    //
    // 🔴 **它同时修掉了 [T53] 查明的根因**：旧制下伤口的流血速率与"口子有多深"**完全无关** ——
    //    一道穿过板甲只渗进去 1 点的剐蹭，和砍在裸背上的 15 点深劈，**流得一样快** ⇒ **流血对护甲免疫**。
    //    新制按 **单次进肉伤害 ÷ 部位最大生命值** 定级 ⇒ 穿甲后的剐蹭比例极小 ⇒ 只挂小流血；
    //    裸身深劈超 60% ⇒ 直接大流血。**护甲从此自动防住了流血**，这是机制的自然结果，不是补丁。

    /// <summary>
    /// 一处出血的**等级**（[T58]）。数值 1/2/3 **就是合并用的"点数"** —— 见 <see cref="Merge"/>。
    /// </summary>
    public enum BleedSeverity
    {
        /// <summary>小流血：任何锐器进了肉就至少是这一级（不看伤害大小）。</summary>
        Small = 1,

        /// <summary>中流血：单次伤害 &gt; 该部位最大生命值的 <see cref="MediumThreshold"/>（30%）。</summary>
        Medium = 2,

        /// <summary>大流血：单次伤害 &gt; <see cref="LargeThreshold"/>（60%）。**封顶级，再挨打也不会更高。**</summary>
        Large = 3,
    }

    /// <summary>中流血门槛：单次伤害占该部位最大生命值的比例（用户拍板 30%）。</summary>
    public const double MediumThreshold = 0.30;

    /// <summary>大流血门槛（用户拍板 60%）。</summary>
    public const double LargeThreshold = 0.60;

    /// <summary>
    /// 一次命中在该部位造成的出血等级。<paramref name="damage"/> 是**真正进到肉里**的伤害
    /// （护甲结算之后、且必须是**以锐器抵达**——钝伤不流血，由调用方门控）。
    /// </summary>
    public static BleedSeverity SeverityOf(double damage, double partMaxHp)
    {
        if (partMaxHp <= 0)
        {
            return BleedSeverity.Large; // 部位上限已被磨没：任何进肉伤害都按最狠算
        }

        double ratio = damage / partMaxHp;
        if (ratio > LargeThreshold)
        {
            return BleedSeverity.Large;
        }

        return ratio > MediumThreshold ? BleedSeverity.Medium : BleedSeverity.Small;
    }

    /// <summary>
    /// 合并两处出血（[T58] 用户的四条规则，**一行代码就是全部**）：
    /// 小(1)+小(1)=2=中 ✓；中(2)+中(2)=4→封顶 3=大 ✓；小(1)+中(2)=3=大 ✓；大+任何 ≥4→封顶=大 ✓。
    /// <para>把等级定义成 1/2/3 的"点数"后，用户那四条规则**恰好就是 <c>min(3, a+b)</c>** —— 不是巧合地凑出来的，
    /// 是这四条规则本身就唯一确定了这个加法（它同时也定死了用户没明说的"大+小 = 大"）。</para>
    /// </summary>
    public static BleedSeverity Merge(BleedSeverity a, BleedSeverity b)
        => (BleedSeverity)Math.Min((int)BleedSeverity.Large, (int)a + (int)b);

    /// <summary>
    /// 各级出血的**流速权重**（× <see cref="Body.BleedRatePerWound"/>，拟定待调）。
    ///
    /// <para><b>标定依据（写进 journal）</b>：</para>
    /// <list type="bullet">
    /// <item><b>大 = 3.0 ＝ 旧制的单部位封顶</b>（旧 <c>MaxWoundsPerPart</c> = 3 处 × 速率 1.0）——
    /// **单部位的流血上限口径一个字没变** ⇒ 丧尸围攻的平方律**不会因为本单变得更糟**（这是硬约束）。
    /// 一处大流血放干一个常人：100 ÷ (3.0 × 0.55) = <b>60.6 秒</b>（45.5 秒昏迷）。
    /// **两处大流血 = 30.3 秒放干 / 22.7 秒昏迷 ⇒ 落在均场 24~55 秒之内** ⇒ 用户那句
    /// 「多个严重流血伤口可能导致这场战斗还没打出胜负就流血致死」**第一次真的成立**
    /// （[T53] 实测它在旧制下从未成立：三处重伤要 60.7 秒 &gt; 均场）。</item>
    /// <item><b>中 = 1.0</b>：等于旧制的一处普通伤口（口径锚点）。</item>
    /// <item><b>小 = 0.3</b>：远低于旧制的每伤口 1.0 —— 穿甲后的剐蹭、丧尸的浅爪都落在这一档，
    /// 它们**几乎不放血**（一处小流血放干一个常人要 10 分钟），但**不处理仍会溃烂/逐日恶化**。
    /// <para><b>实测标定过程（不是拍脑袋）</b>：0.1 太低 ⇒「匕首靠放血耗死丧尸」这条既有玩法路径**当场消失**
    /// （对决里失血致死率塌到 0）；0.3 时两个约束**同时成立**（见下）。</para></item>
    /// </list>
    /// <para>
    /// 🔴 <b>本标定与「读法 B」（见 <c>CombatEffectResolver</c> 的流血分支）是一套，必须一起看</b>：
    /// 若改用字面的「读法 A（锐伤必挂流血）」，同一套速率会让 <b>2 只丧尸围攻的胜率崩到 15.7%</b>
    /// （因为爪击每下必挂 + 用户的合并规则 ⇒ 同一部位挨满 3 下就 ratchet 到大流血）。
    /// 🔴 <b>这个 15.7% 是「读法 A」臂、<u>同为 100/0.55 口径</u>的历史实验数</b> —— <b>与下方 [T53] 那个 16.6% 不是一回事</b>
    /// （那个是 <b>70/1.5 热口径</b>、与三级流血无关的更早配置）。两数巧合都在 ~16%，**别合并、别互相"修正"**。
    /// </para>
    /// <para>
    /// <b>实测（<c>lanchester</c>，同一棵树 A/B，同为 100/0.55 口径）—— 读法 B + 小 0.3 下，「封顶」真的打断了平方律</b>：
    /// <code>
    /// N 只丧尸围攻   改前(T59)   本标定
    ///   2 只          61.5%   →   84.5%
    ///   3 只          11.1%   →   22.0%
    /// </code>
    /// ⚠️ 「改前(T59)」列是**跑不出第二次的历史实验臂**（永久存档）；「本标定」列 = <b>当前实现，会漂</b>
    /// ⇒ <b>现值一律以 <c>docs/research/2026-07-14-lanchester.md</c> 为准</b>（上表＝2026-07-17 全仓重跑值；
    /// 此前写的 84.4% / 24.1% 抄自 <c>991b777</c>，那份报告已被证实是 born-stale 假重跑）。
    /// ⇒ <b>解耦达成</b>：单挑挨一记深劈 ⇒ 直接大流血 ⇒ 真的会死；群殴挨一堆浅爪 ⇒ 多半连血都挂不上、
    /// 挂上也只是小流血、且**封顶**在大 ⇒ **不会被线性放大**。旧制下这两件事由同一个数控制、互斥
    /// （[T53] 的死结），三级流血正是解开它的钥匙。
    /// </para>
    /// </summary>
    public static double SeverityRateOf(BleedSeverity s) => s switch
    {
        BleedSeverity.Small => 0.3,
        BleedSeverity.Medium => 1.0,
        BleedSeverity.Large => 3.0,
        _ => 1.0,
    };

    /// <summary>
    /// 战后伤病建档时，各级出血对应的**初始严重度**（0~1，喂给 Godot 侧 <c>HealthCondition</c>，拟定待调）。
    /// 未手术的致命失血每昼夜恶化 0.10、到 1.0 死亡 ⇒ 本值直接决定**还剩几天可救**：
    /// 小 0.25 ⇒ 7.5 昼夜；中 0.45 ⇒ 5.5 昼夜；**大 0.70 ⇒ 只剩 3 昼夜**（与战斗内"大流血＝再不处理就死"同调）。
    /// <para>⚠️ 小流血取 0.25 而非 0.2：0.2 是"擦伤级自愈线"（<c>AbrasionSeverityThreshold</c>），
    /// 落在线上就**完全不需要手术**了——那会让整场丧尸战一台手术都不用做。0.25 让它仍需一台（但很轻）。</para>
    /// </summary>
    public static double ConditionSeverityOf(BleedSeverity s) => s switch
    {
        BleedSeverity.Small => 0.25,
        BleedSeverity.Medium => 0.45,
        BleedSeverity.Large => 0.70,
        _ => 0.35,
    };

    // ================= 流血口径：**实机与 Sim 的单一事实源** =================
    //
    // 🔴 [T53] 这两个常量是本次修的**根因**。此前它们是**两套数**，而且没人知道：
    //   · Sim/Duel 走 `DuelConfig` 的字段默认值：储血 70 / 每伤口 1.5 ⇒ 放干一个致命伤口 46.7s
    //   · **实机（Godot）从不设置流血口径**（`godot/scripts` 全仓 grep `BleedRatePerWound`/`SetBloodMax`
    //     命中 **0 次**）⇒ 跑的是 `Body` 的字段默认值：储血 100 / 每伤口 0.55 ⇒ 放干要 **181.8s**
    //   ⇒ **Sim 的流血比实机「热」3.9 倍。** 所有拿 Sim 数字得出的流血结论都套不到实机上：
    //     实测实机口径下「锯齿剑刃 vs 丧尸」的流血致死占比是 **0.0%**（丧尸 27s 就被直伤打死，
    //     而放干它要 545s）—— 两条流血改装件在实机里**等于没做**。
    //   ⇒ 「流血刚被大幅加强过」这句话**只对 Sim 成立**：`Body.BleedRatePerWound` 的注释自陈
    //     "0.8→0.55 **下调**"，实机不但没被加强，反而被调弱了。分级/闸门进了实机，**速率那一半没进**。
    //
    // 🔴 【T53 二次拍板】用户**否决了"实机对齐到 Sim"**（原话：「**不对齐了**」）。
    //    起因：`sim-lanchester` 证明丧尸围攻的**平方律根因就是失血积分**（丧尸翻倍 ⇒ 伤口速率翻倍 **且** 清场时长变长，
    //    两个因子都随 N 涨 ⇒ 天然平方；储血又是**硬阈值** ⇒ 一翻倍就冲破昏迷线，是**断崖不是斜坡**）。
    //    在 70/1.5 的热口径下：2 只丧尸胜率 16.6%、3 只 0.8%、4 只起 0% —— 用户不接受"两只丧尸就是死局"。
    //    ⇒ **口径回退到 100 / 0.55**（`Body` 的原默认值＝游戏一直在跑的那套）。
    //
    // ⚠️ **但"两份事实源焊死"这个【结构】保留** —— 它才是本单的价值。回退的只是**数值**。
    //
    // 🔴 **为什么 Sim（`DuelConfig`）也必须跟着回到 100/0.55，而不是让两边各留各的数**（我的专业判断，依据如下）：
    //    ① **Sim 的存在意义就是预测实机。** 本单查出的 bug 正是"Sim 量的是一套游戏根本不跑的口径"。
    //       如果现在让两边**故意**保持不同，等于**把这个 bug 制度化**——下一份 Sim 报告又会是关于一个不存在的游戏的。
    //    ② **驱动用户这次决策的 lanchester 数据本身就是在 70/1.5 下跑的**（比实机热 3.9 倍）
    //       ⇒ 「2 只丧尸 = 16.6% 胜率」**不是实机的数**。必须在 100/0.55 下重跑才知道真相
    //       （实机流血弱得多 ⇒ 失血积分的平方律效应会显著缓和，很可能远没有那么绝望）。
    //    ③ `DuelConfig` 那两个值的原注释自陈是**测量便利**（"比默认 100 略低以让失血分级/昏迷/致死真实出现"）——
    //       **为了让现象显形而调仪器**，这正是我们踩坑的方式。
    //    ⇒ **单一常量、单一口径**。`DuelConfig` 的字段仍是 `init`，需要做"热口径"专项研究的人可以**显式**传值，
    //       但**默认值＝实机真相**。代价：所有吃流血的 Sim 报告（combat-cost / lanchester / 武器 TTK）**必须重算**——
    //       这是**正确的漂移**：它们此前描述的是一个游戏并不运行的口径。

    /// <summary>
    /// 储血量上限的**单一事实源**（拟定待调）。<see cref="Body"/> 的构造默认值与
    /// <c>DuelConfig.BloodMax</c> **都读这一个常量** —— 实机与 Sim 不可能再漂开。
    /// </summary>
    public const double DefaultBloodMax = 100;

    /// <summary>
    /// 每处伤口每秒失血量的**单一事实源**（拟定待调）。<see cref="Body.BleedRatePerWound"/> 的字段默认值与
    /// <c>DuelConfig.BleedRatePerWound</c> **都读这一个常量**。
    /// </summary>
    public const double DefaultBleedRatePerWound = 0.55;

    /// <summary>
    /// 休养（卧床养病）时每昼夜恢复的血量（拟定待调）。用户拍板（T53）：**补——休养自然回血**
    /// （不做输血/血袋；医院「血库」保留为叙事，不接机制）。
    ///
    /// <para>
    /// <b>数值依据</b>：**从零回满 = <see cref="FullBloodRefillDays"/>(7) 昼夜** —— 与「骨折愈合 7 昼夜」
    /// **同一量级**（占床、不能干活、不能站岗），代价对得上。
    /// </para>
    /// <para>
    /// ⚠️ 本值**由储血上限推导**（<c>DefaultBloodMax / FullBloodRefillDays</c>），不写死 ——
    /// 否则调了储血上限（[T53] 就调过一次 100→70→100）而忘了改这里，「7 昼夜回满」会静默失真。
    /// </para>
    /// <para>
    /// 🔴 <b>只在伤口止住（＝手术缝合）之后才回血</b>：还在流的口子边流边补是自欺欺人，
    /// 也会架空用户「任何时候只要伤口没被手术治疗就会流血」这条规则。判据 = <see cref="Body.BleedingWoundCount"/> == 0。
    /// </para>
    /// </summary>
    public const double BloodRegenPerRestDay = DefaultBloodMax / FullBloodRefillDays;

    /// <summary>休养回血：从零回满所需的昼夜数（拟定待调）。对齐「骨折愈合 7 昼夜」。</summary>
    public const double FullBloodRefillDays = 7.0;

    /// <summary>
    /// 丧尸的失血流速倍率（用户口径：「丧尸的流血速度只有 1/3，没那么容易流血致死」，拟定待调）。
    ///
    /// <para>
    /// 语义上：行尸走肉，血液循环本就不该像活人一样。机制上：这是**受害者侧**的实体属性
    /// （<see cref="Body.BleedRateMultiplier"/>），不是"锐器打丧尸时流血少"的武器属性——所以精英丧尸、
    /// 动物、别的敌人将来都能各自设定，不需要在代码里写 <c>if (name == "丧尸")</c>。
    /// </para>
    ///
    /// <para>
    /// 它同时是"流血加强"这条改动的**配平闸门**：没有它，锐器只要划两刀站着等丧尸流干就行，
    /// 伤害与护甲都会失去意义。
    /// </para>
    /// </summary>
    public const double ZombieBleedRateMultiplier = 1.0 / 3.0;

    /// <summary>
    /// 非致命伤口（<see cref="BleedTier.Minor"/>/<see cref="BleedTier.Micro"/>）的失血下限（占储血上限的比例，拟定待调）。
    /// 它们只能把人抽到此线为止 —— 会虚弱（掉进轻度失血、攻速下降），但**永远到不了昏迷线（25%），更到不了 0**。
    /// 这就是「小伤口不致命」的机械保证。
    /// </summary>
    public const double NonLethalBloodFloorRatio = 0.5;

    /// <summary>该部位出血的分级。部位表查不到 → 按致命（从狠，与 <c>HealthMapping</c> 旧口径一致）。</summary>
    public static BleedTier TierOf(Body body, string part)
        => body.Parts.TryGetValue(part, out BodyPart? p) ? TierOf(p) : BleedTier.Lethal;

    /// <inheritdoc cref="TierOf(Body, string)"/>
    public static BleedTier TierOf(BodyPart part) => part.Region switch
    {
        BodyRegion.Finger or BodyRegion.Toe or BodyRegion.Eye or BodyRegion.Face or BodyRegion.Ear
            => BleedTier.Micro,
        BodyRegion.Hand or BodyRegion.Foot => BleedTier.Minor,
        _ => BleedTier.Lethal, // 躯干/头/颈/手臂/大腿 等大部位
    };

    /// <summary>该部位的出血是否为致命失血（拖久会放干）。</summary>
    public static bool IsLethalPart(Body body, string part) => TierOf(body, part) == BleedTier.Lethal;

    /// <summary>该部位的出血是否为微小伤（战后自愈，不需手术）。</summary>
    public static bool IsMicroPart(Body body, string part) => TierOf(body, part) == BleedTier.Micro;

    /// <summary>
    /// 各**部位**分级的流速权重（× <see cref="Body.BleedRatePerWound"/>，拟定待调）。
    /// 与 <see cref="SeverityRateOf"/> **正交相乘**：等级＝「口子多大」、部位权重＝「砍在哪」。
    /// </summary>
    public static double RateWeightOf(BleedTier tier) => tier switch
    {
        BleedTier.Lethal => 1.0,
        BleedTier.Minor => 0.5,
        BleedTier.Micro => 0.2,
        _ => 1.0,
    };

    /// <summary>
    /// 一处出血伤口的**实际**分级：断口（部位已被切除/损毁）一律按致命 —— 手掌被划一刀不致命，
    /// 但整只手被砍下来的断口是会把人放干的。**微小部位除外**（断一根手指不该流血流死）。
    /// 致命性由已持久化的状态（部位是否 <see cref="Body.IsGone"/>）推导，故不需要给存档加字段。
    /// </summary>
    public static BleedTier WoundTierOf(Body body, string part)
    {
        var tier = TierOf(body, part);
        if (tier == BleedTier.Micro)
        {
            return BleedTier.Micro; // 断指仍是断指
        }

        return body.IsGone(part) ? BleedTier.Lethal : tier;
    }
}
