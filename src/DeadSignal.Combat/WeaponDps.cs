namespace DeadSignal.Combat;

/// <summary>
/// 武器的**每秒伤害（DPS）**——纯函数，只读现有字段与现有规则，**不新增任何战斗规则**。
///
/// <para><b>它存在的唯一理由</b>：数值表 wiki 要显示 DPS 供调数值。DPS 必须**在引擎里算**，
/// 绝不能在网页 JS 里另写一遍公式——那会是**第二份事实源**：引擎哪天改了攻速/连发规则，
/// wiki 上的 DPS 就会**悄悄骗人**，而照着一个错的数字调平衡，比根本没有这一列更糟。</para>
///
/// <para><b>⚠️ 这个数字量不到什么（与 <see cref="Duel"/> 的局限同源，见 CLAUDE.md）</b>：
/// 它是**无甲 / 贴脸 / 无限弹药 / 单挑**下的**杀伤力天花板**，
/// <b>不含</b>护甲三段判定、距离衰减、噪音招怪、弹药消耗、多目标清群。
/// 枪的真实战力由**弹药供给**决定，而供给不在 DPS 里。读这一列时必须知道它测不到这些。</para>
/// </summary>
public static class WeaponDps
{
    /// <summary>
    /// 一次攻击的**期望伤害**（未经护甲）。伤害在 <c>[DamageMin, DamageMax]</c> 内**均匀 roll**
    /// （见 <see cref="CombatResolver.Resolve"/> 的第一层攻方 roll）⇒ 期望 = 区间中点。
    /// </summary>
    public static double ExpectedDamagePerShot(Weapon w) => (w.DamageMin + w.DamageMax) / 2.0;

    /// <summary>
    /// 一个完整攻击周期的**秒数**（含整轮连发 + 冷却）。
    ///
    /// <para><b>🔴 冷却是在整轮连发「之后」才起算的</b>（<see cref="Duel"/> 的时序：
    /// 一次"射击" = <see cref="Weapon.BurstCount"/> 发，发与发之间隔 <see cref="Weapon.BurstInterval"/>，
    /// <b>下次出手 = 末发时刻 + 有效冷却</b>）⇒ 周期 = <c>(连发数 − 1) × 连发间隔 + 有效冷却</c>。
    /// <b>不是</b> <c>1 / 攻击间隔</c>，也<b>不是</b>把连发间隔算进冷却里。</para>
    ///
    /// <para><b>🔴 持握系数只作用在冷却上，不作用在连发间隔上</b>
    /// （<c>GripCombat.EffectiveInterval</c> = 基础冷却 ÷ (操作能力 × 持握系数)）——
    /// 所以**双持不是简单地乘一个固定倍数**：连发间隔那一截不跟着变。这正是必须按周期公式算、
    /// 而不能用捷径的原因。<b>持握系数如今只剩双持一项</b>（双手握已无攻速加成，见 <see cref="DualWield"/>）。</para>
    /// </summary>
    /// <param name="operation">操作能力（残疾 × 饥饿）。本表按健康满值计算。</param>
    public static double CycleSeconds(Weapon w, GripMode grip, double operation = 1.0)
    {
        int burst = Math.Max(1, w.BurstCount);
        double burstGap = Math.Max(0, w.BurstInterval);

        // 与 GripCombat.EffectiveInterval 同式（操作能力 ≤0 时回落基础冷却，避免除零）。
        double speed = operation > 0 ? operation * DualWield.GripSpeedFactor(grip) : 1.0;
        double effectiveCooldown = w.AttackInterval / speed;

        return (burst - 1) * burstGap + effectiveCooldown;
    }

    /// <summary>
    /// 该武器在某种持握下的 DPS（**一只手/一把武器**的产出）。
    /// <para>
    /// 一个周期打出的总伤害 = 期望单发伤害 × <see cref="Weapon.PelletCount"/> × 连发数。
    /// </para>
    /// <para><b>⚠️ 多弹丸武器按「全中」算 ⇒ 这是理论上限</b>：引擎里每颗弹丸
    /// **独立选命中部位、独立走三段判定**（<see cref="CombatResolver.ResolveVolley"/>），
    /// 打披甲目标时挡下的那些颗**不算数**。所以该数字是「所有弹丸全中且目标无甲」的天花板，
    /// 实战永远达不到——**它不是假精确，是明确标注了上限语义**。</para>
    /// </summary>
    public static double Of(Weapon w, GripMode grip, double operation = 1.0)
    {
        double cycle = CycleSeconds(w, grip, operation);
        if (cycle <= 0) return 0;

        int burst = Math.Max(1, w.BurstCount);
        int pellets = Math.Max(1, w.PelletCount);
        return ExpectedDamagePerShot(w) * pellets * burst / cycle;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 对披甲目标的 DPS —— 裸 DPS 是**无甲天花板**，而**甲才是决定武器强弱的关键**。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 一次命中打在**披着 <paramref name="armor"/> 的人**身上的**期望伤害**（护甲结算之后）。
    ///
    /// <para><b>🔴 这里不推导任何护甲公式，而是直接跑引擎自己的 <see cref="CombatResolver.Resolve"/>。</b>
    /// 理由：护甲是**分层随机**的——每层攻方在区间内 roll、防方在 <c>[0, 护甲值×(1−穿透)]</c> 内 roll，
    /// 三段判定（全伤 / 半伤且锐器转钝且穿透归零 / 挡下并终止），下一层还要在 <c>[0, 上层伤害]</c> 内重新 roll。
    /// 这东西的解析期望**算不出闭式**，硬推必错。而"错的 DPS 比没有 DPS 更糟"——用户会照着它调平衡。
    /// 所以：**让引擎自己跑，我只负责取平均**。护甲逻辑变了，这个数自动跟着变。</para>
    ///
    /// <para><b>部位覆盖是自动的</b>：<see cref="CombatResolver.Resolve"/> 内部会把"没覆盖到这个部位"的甲层滤掉
    /// （皮甲只护胸/腹/双手臂 ⇒ 打到头/手/脚就等于打无甲）。而部位由引擎的
    /// <see cref="VolumeWeightedHitSelector"/> 按真实分布抽 —— 所以这个数**天然把"打到裸露部位"算进去了**，
    /// 既不会只算"打中甲上"（低估），也不会按裸伤算（高估）。</para>
    ///
    /// <para><b>多弹丸</b>：弹丸**逐颗独立选部位、独立三段判定**（同引擎 <c>ResolveVolley</c> 的语义），
    /// 打披甲会被挡掉好几颗 —— 这正是"霰弹枪对披甲极差"的来源，本函数如实反映，**不按全中算**。</para>
    ///
    /// <para><b>不含</b>乘算减伤层（<c>incomingDamageReduction</c>）：它现阶段唯一的非零来源是山姆的专属效果，
    /// 是**角色**属性而非武器属性，不该混进武器表。</para>
    /// </summary>
    /// <param name="samples">采样次数。够大才稳（默认 4 万次，全表跑完约百毫秒）。</param>
    /// <param name="seed">固定种子 ⇒ 同样的输入永远得同样的数（表不会自己跳来跳去）。</param>
    public static double ExpectedDamagePerHit(
        Weapon w, IReadOnlyList<ArmorLayer> armor, int samples = 40000, int seed = 20260714)
    {
        var rng = new SystemRandomSource(seed);
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);

        // 一具完好的身体：每次采样都打"满血的人"，不累积伤害（我们要的是"一击的期望"，不是击杀过程）。
        Body body = HumanBody.NewBody();
        var parts = body.Parts.Values.ToList();
        IReadOnlyList<ArmorLayer> layers = CombatResolver.OrderOuterToInner(armor);

        double total = 0;
        for (int i = 0; i < samples; i++)
        {
            BodyPart part = hit.Select(parts);
            total += resolver.Resolve(w, layers, part).FinalDamage;
        }
        return total / samples;
    }

    /// <summary>
    /// **对披甲目标的 DPS**。与 <see cref="Single"/> 同一个周期公式，只是把"期望单发伤害"
    /// 从"区间中点"换成"**打在这身甲上的期望伤害**"（见 <see cref="ExpectedDamagePerHit"/>）。
    /// <para><b>含</b>：护甲三段判定、部位覆盖（打到裸露处 = 打无甲）、穿透、多弹丸逐颗独立判定。
    /// <b>不含</b>：距离衰减、噪音招怪、弹药消耗、多目标清群 —— 那些 <see cref="Duel"/> 也测不到。</para>
    /// </summary>
    public static double AgainstArmor(Weapon w, IReadOnlyList<ArmorLayer> armor, double operation = 1.0)
    {
        double cycle = CycleSeconds(w, w.TwoHanded ? GripMode.TwoHanded : GripMode.OneHanded, operation);
        if (cycle <= 0) return 0;

        int burst = Math.Max(1, w.BurstCount);
        int pellets = Math.Max(1, w.PelletCount);
        return ExpectedDamagePerHit(w, armor) * pellets * burst / cycle;
    }

    /// <summary>
    /// **「皮甲组」** —— wiki 武器表「对皮甲每秒伤害」那一列打的就是这身装束。
    /// <para>
    /// = <b>皮甲</b>（<see cref="ArmorTable.Leather"/>，装甲层，护胸/腹/双手臂）
    ///   + <b>长袖布衣</b>（贴身层）。
    /// </para>
    /// <para><b>为什么带一件布衣</b>：这是 <c>DeadSignal.Sim</c> 各处标定用的**标准皮甲组**
    /// （<c>new[] { ArmorTable.Leather(), ArmorTable.LongSleeveShirt() }</c>）—— 跟它一致，wiki 的数字
    /// 才能和 Sim 的表交叉对照。而且"只穿一件皮甲、里面什么都不穿"本来也不是个真实的目标。</para>
    /// <para>⚠️ 「皮甲」不是「皮革胸甲」（另一件，只护胸），也不是「皮夹克」（外套层）。</para>
    /// <para><b>头、手、脚都露着</b> —— 打到那儿就是打无甲。这正是为什么对甲 DPS 不会归零。</para>
    /// </summary>
    public static IReadOnlyList<ArmorLayer> LeatherArmorSet()
        => new[] { ArmorTable.Leather(), ArmorTable.LongSleeveShirt() };

    /// <summary>**对皮甲组的 DPS**（wiki 武器表那一列）。</summary>
    public static double AgainstLeatherArmor(Weapon w, double operation = 1.0)
        => AgainstArmor(w, LeatherArmorSet(), operation);

    /// <summary>
    /// **常规 DPS**（这把武器**按它唯一/默认的持法**拿着时的 DPS）：单持与双手握**同值**——
    /// 双手握已无攻速加成（见 <see cref="DualWield"/>），故这一列就是「不双持时的 DPS」。
    /// <para>保留 grip 形参与本重载，是因为双持仍有持握惩罚——持握依然会改变 DPS，只是方向只剩变慢一种。</para>
    /// </summary>
    public static double Single(Weapon w, double operation = 1.0)
        => Of(w, w.TwoHanded ? GripMode.TwoHanded : GripMode.OneHanded, operation);

    /// <summary>
    /// **双持 DPS**（两把同款一起打）：**不可双持的武器返回 <c>null</c>**——
    /// 该显示「—」，而不是 0、更不是编一个数出来。
    /// <para>
    /// 双持时**每只手**都按 <see cref="GripMode.DualWield"/> 算（冷却受持握系数影响 ⇒ 每只手更慢），
    /// 两只手各自独立出手 ⇒ 合计 = <b>2 × 单手在双持态下的 DPS</b>。
    /// </para>
    /// <para><b>🔴 它不等于常规 DPS 的固定倍数</b>：持握惩罚只作用在**冷却**上，
    /// **连发那一段时间不吃惩罚**（见 <see cref="CycleSeconds"/>）⇒ 连发占周期比重越大，
    /// 双持惩罚被连发间隔稀释；具体倍率随武器连发配置变化。
    /// （这条把作者本人也绊倒过一次，测试 <c>有连发的武器_双持DPS_不等于常规的1点4倍</c> 钉死了它。）</para>
    /// </summary>
    public static double? Dual(Weapon w, double operation = 1.0)
        => w.CanDualWield ? 2.0 * Of(w, GripMode.DualWield, operation) : null;
}
