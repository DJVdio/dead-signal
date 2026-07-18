namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威武器数据源。Godot 消费层、Sim 聚合模拟、Sim 对决战报全部从此读取，
/// 消除此前 CombatData / Sim.Program / Sim.DuelReport 三处各自维护武器表的数据源分裂。
///
/// 可调数值全部从 Wiki 配置表投影；本类只负责按内部键取值，不在注释中复制数值。
/// 「土制枪」已按用户拍板删除，由全新的可制作武器 <see cref="ImprovisedHuntingGun"/>（自制猎枪）取代。
///
/// <para><b>全局慢节奏（用户拍板：多角色操控需要慢战斗）</b>：武器冷却与连发参数以 Wiki 配置为准，
/// 引擎消费配置，Sim 只用于校准，不在此维护数值快照。</para>
///
/// <para><b>伤害区间与护甲互动</b>：规则形态固定，具体伤害、穿透、攻速与护甲数值以 Wiki 配置表为准。
/// 历史批次的校准结果只保留在研究报告，不在武器工厂注释中维护。</para>
/// </summary>
public static class WeaponTable
{
    // ---- 近战锐器 ----

    // ⚠️ 不在方法注释中复制武器数值；当前值直接读 Wiki 配置投影。

    /// <summary>匕首：近战锐器，全表最快的锐器之一；具体属性以 Wiki 配置为准。</summary>
    public static Weapon Dagger() => CombatCatalog.Weapon("dagger");

    /// <summary>短剑：近战锐器；具体属性以 Wiki 配置为准。</summary>
    public static Weapon Shortsword() => CombatCatalog.Weapon("shortsword");

    /// <summary>刺剑：单手近战锐器，突刺路线——伤害区间窄、攻速略快。可双持。
    /// 具体属性以 Wiki 配置为准。</summary>
    public static Weapon Rapier() => CombatCatalog.Weapon("rapier");

    /// <summary>长剑：近战锐器，双手武器；具体属性以 Wiki 配置为准。</summary>
    public static Weapon Longsword() => CombatCatalog.Weapon("longsword");

    /// <summary>重剑：近战锐器，双手武器；具体属性以 Wiki 配置为准。</summary>
    public static Weapon Greatsword() => CombatCatalog.Weapon("greatsword");

    /// <summary>草叉：双手近战锐器，农具改造——多齿突刺，穿透中等、伤害区间较宽、攻速偏慢。照长剑数值风格。</summary>
    public static Weapon Pitchfork() => CombatCatalog.Weapon("pitchfork");

    /// <summary>
    /// 消防斧（[批次25·T44]，用户点名新建）：双手近战锐器 —— <b>劈砍</b>。
    ///
    /// <para><b>它的数值仍绑着利爪型</b>：它同时是「利爪型」枪械改装的<b>近战锚点</b>。
    /// 具体攻速与伤害由 Wiki 配置提供；<b>动这里的伤害或攻速 = 动利爪型的强度</b>，<c>AxeTests</c> 会拦住你。</para>
    ///
    /// <para><b>砸墙：它是全表唯一一把「破拆型锐器」</b>（本单拍板，数值层）。劈门本来就是斧子的正经用途，
    /// 故它打破了「锐器砸墙无用」这条通则 —— 砸墙三段梯度在它这里变成<b>四段</b>：
    /// 其余锐器 ＜ 枪托 ＜ 【消防斧】 ＜ 钝器。
    /// <b>为什么不让它进钝器档</b>：钝器打人是弱的，破拆是它们<b>唯一</b>的回报；
    /// 消防斧打人提升后，再让它砸墙也压过锤子，钝器就彻底没有存在理由了。
    /// <b>「砍不动金属门」</b>：要破金属门，还是得带把锤子。
    /// 见 <c>AxeTests</c> 与 <c>BluntStructureAdvantageTests</c> 的具名例外。</para>
    ///
    /// <para>劈砍吃甲；具体穿透与其他属性以 Wiki 配置为准。</para>
    /// </summary>
    public static Weapon Axe() => CombatCatalog.Weapon("axe");

    /// <summary>
    /// 骨刀：**应急武器**。削骨磨出来的一片东西——比空手强，比任何一把真武器都弱。
    /// <para>
    /// 🔴 <b>它从前是个死物品</b>：有配方（<c>Recipe.cs</c> 的 <c>bone_knife</c>）、有图标、有风味文案，
    /// 但 <see cref="Arsenal"/> 里**没有它** ⇒ <c>ModdedWeaponRegistry.WeaponByName("骨刀")</c> 查不到
    /// ⇒ <c>Pawn.EquipWeapon</c> 直接 <c>return false</c> ⇒ **玩家花材料造出来的骨刀，永远拿不起来**
    /// （不报错、不崩溃，只是静默拿不动）。补进 Arsenal 才算真的把它接上。
    /// </para>
    /// <para>
    /// 🔴 <b>用户拍板：「保留双持，但大幅削弱数值」</b>。削弱理由是经济，不是手感：
    /// 骨刀材料容易获得、制作门槛低，因此只能作为应急武器，不能替代真正的近战武器。
    /// 伤害、攻速与双持表现以 Wiki 配置为准。
    /// </para>
    /// <para>
    /// 冷却与伤害共同决定它相对空手和匕首的位置；护栏只锁定位关系，不在注释中复制 DPS 快照。
    /// </para>
    /// </summary>
    public static Weapon BoneKnife() => CombatCatalog.Weapon("bone_knife");

    // ---- 近战钝器 ----
    //
    // 【钝器的存在理由 = 砸建筑】用户拍板：「钝器对建筑伤害应该要远高于锐器，这应当是钝器的一个优势」。
    //
    // 钝器打**人**是弱的，这是**有意的代价**，不是待修的平衡问题；具体表现以 Wiki 配置与 Sim 报告为准。
    // 🔴 **胜率不是成本**：它一个字都没说你为此付了什么（弹药、伤病、永久残缺、感染）。
    // 回报全在砸建筑这条轴上——**要破门，就得带把锤子**。三把钝器的「砸墙系数」故意拉到锐器的一个量级之上：
    //
    //   砸墙效率由武器伤害、攻速与建筑配置共同决定，具体结果见对应 Sim 报告。
    //
    // 梯度是三段的，每一段都有护栏钉死（BluntStructureAdvantageTests）：
    //   锐器 ＜ 枪托 ＜ 钝器。
    // 「最弱的钝器也要明显压住最强的锐器」是硬指标——这条一塌，钝器就没有存在理由了。
    //
    // ⚠ 改这里的伤害/冷却会连带改掉砸墙效率（砸墙 = 均伤 × 砸墙系数 ÷ 出手间隔），护栏会告诉你梯度有没有破。

    /// <summary>棍棒：近战钝器；穿透配置以 Wiki 为准。道格开局武器。
    ///
    /// <para><b>它是全表最弱的<u>真</u>近战武器，而且是有意的</b>——用户原话「开局武器就该很烂，不改」。
    /// 具体属性以 Wiki 配置为准。破甲能力的压力是设计的一部分，<b>别当平衡问题"修"</b>。</para>
    ///
    /// <para>⚠ <b>它同时是枪托近战的锚点</b>：原则「任一枪托 DPS ＜ 棍棒」⇒ <b>改棍棒 = 改枪托的天花板</b>。
    /// 该锚点由 <c>GunModBenchTests</c> 的正向硬护栏钉死
    /// （见下方「枪托近战数值」段）。<b>以后再动棍棒，记得枪托要跟着走</b>，否则那条护栏会当场红。</para>
    ///
    /// <para>历史 Sim 结果不在工厂注释中复述；需要胜率时运行对应 harness。</para>
    /// </summary>
    public static Weapon Club() => CombatCatalog.Weapon("club");

    /// <summary>尖头锤：近战钝器；具体属性以 Wiki 配置为准。</summary>
    public static Weapon SpikeHammer() => CombatCatalog.Weapon("spike_hammer");

    /// <summary>破甲锤：近战钝器，重型双手武器；具体属性以 Wiki 配置为准。
    /// <para>它的核心定位是钝伤效果与砸墙能力，而非单纯依赖穿透。</para></summary>
    public static Weapon Warhammer() => CombatCatalog.Weapon("warhammer");

    // ---- 远程 ----

    /// <summary>自制猎枪（<b>全新武器</b>；旧的「土制枪」已按用户拍板<b>删除</b>，不是改名）：远程锐器，双手长枪。
    /// 玩家<b>可自己制作</b>（配方 <c>improvised_hunting_gun</c>：卡尺精工 + 《土法化学笔记》）。
    /// <para>伤害、穿透、冷却、射程、衰减与枪托属性均以 Wiki 配置为准。</para>
    /// <para><b>⚠ 它不是"穿甲专精"</b>：立项时的派单原话是"穿透比步枪还高 → 专克穿甲目标"，
    /// 真实定位是<b>唯一能自己造的枪</b>（不靠掉落）：伤害低、精度差、射程短，胜在可再生产；具体属性以 Wiki 为准。
    /// 另见 <c>CombatResolver</c>：能否击穿甲层主要由<b>伤害骰</b>决定，穿透系数只压低防御骰上限——
    /// 低伤高穿透在多层甲前并不"钻得深"（实测平均穿透层数低于步枪）。</para></summary>
    public static Weapon ImprovisedHuntingGun() => CombatCatalog.Weapon("improvised_hunting_gun");

    // ═══════════════════════════ 枪托近战数值（批次21·T7 定稿）═══════════════════════════
    //
    // 【它是什么】"打空了 / 被贴脸了，只好抡枪托砸"——**绝望手段**，不是一条武器路线。
    //
    // 【T7 定稿原则】枪托是绝望手段，不应替代真正的近战武器；具体 DPS 关系由 Wiki 配置和护栏测试维护。
    //   唯一在枪托之下的"武器"是骨刀（1.50）—— 那是无限材料的应急片，本就该垫底（见 BoneKnife 注释）。
    //   护栏已从当年的三条"记录现状"测试（`*_IsCurrentlyBroken_PendingUserDecision`，**已删除**）
    //   翻回**正向硬护栏**：`GunModBenchTests` 现断言「任一枪托 DPS ＜ 棍棒」，卡的是最坏一对
    //   （最强枪托 vs 最弱近战）。**再动棍棒，枪托要跟着走，否则那条护栏会当场红。**
    //
    // 【穿透仍是枪托的第二道天花板】枪托对**披甲**目标应当较差；具体穿透与护甲以 Wiki 配置为准。
    //
    // 【重量怎么进数值】重量**不改变 DPS**，只在「单击伤害 ↔ 冷却」之间**搬运**：
    //   重枪单击痛、抡得慢；轻枪单击轻、抡得快。重量取自 Wiki 武器表（单一事实源）。
    //   这是对的——枪不是为砸人设计的，多重的枪你都是"拿反了在抡"，效率天花板一样低；
    //   重量只决定你这一下砸得多狠、以及你多久才能再抡起来。重量取自 CarryWeight 的武器表（单一事实源）。
    //
    // 【穿透】未装刺刀的枪托砸不穿甲。具体穿透和噪音以 Wiki 武器表为准，且不继承枪本体枪声。
    // 【弓弩没有这一段】它们没有枪托——空手/持弓的近战由 Unarmed（拳脚）承担。
    // 【七把 → 六把】栓动猎枪已被用户从数值表删除（见下方墓碑注释），故"七把枪"的说法也已作废。
    // 全部数值**拟定待调**。改这一段**不会**惊动 Sim：Duel 是 1v1 无距离对决、且不建模弹药，枪永远走不到
    //   "贴脸抡枪托"那条分支 ⇒ Sim 的结算路径**读不到** StockMelee*（结构性零漂移，已 A/B 实证）。
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>手枪：远程锐器；具体属性以 Wiki 配置为准。</summary>
    public static Weapon Pistol() => CombatCatalog.Weapon("pistol");

    /// <summary>冲锋枪：远程锐器，双手抵肩；枪托可贴脸钝击。
    /// 连发与冷却参数以 Wiki 配置为准。</summary>
    public static Weapon Smg() => CombatCatalog.Weapon("smg");

    /// <summary>步枪：军用远程锐器，双手抵肩；枪托可贴脸钝击。具体属性以 Wiki 配置为准。
    /// 穿透对多层护甲的影响以 Sim 报告为准。</summary>
    public static Weapon Rifle() => CombatCatalog.Weapon("rifle");

    // 【栓动猎枪·已删除】用户在数值表上把这一行划掉了（墓碑 sync=「删除·待同步进代码」），本批次执行。
    // 它原本是"民用猎枪"生态位，夹在军用步枪与自制猎枪之间。
    // 删除后枪械剩 6 把，"能自己造的枪"与"只能搜刮的军用枪"两档之间不再有民用中间档。
    // ⚠ 它是 Arsenal 的第 14 位（idx 13），删除后其后所有武器的 idx 前移一位 ⇒ Sim 按 idx 派生种子，
    //   狙击枪及其后全部武器的随机流因此重置（数值抖动是重新抽样的结果，不是规则变化，见本批次 Sim 复验）。
    //   这是"删武器"不可避免的代价——**新增**武器仍须一律追加末尾，才能保住既有基线。

    /// <summary>狙击枪：远程锐器，双手抵肩；枪托可贴脸钝击。具体属性以 Wiki 配置为准。</summary>
    public static Weapon SniperRifle() => CombatCatalog.Weapon("sniper_rifle");

    /// <summary>
    /// 自制霰弹枪（用户拍板新增）：**全表唯一的多弹丸武器**——弹丸独立计算，
    /// 射程较短，伤害衰减严重，锥形扩散较大」。
    ///
    /// <para><b>多颗弹丸「单独计算」</b>（<see cref="Weapon.PelletCount"/>）：每颗<b>各自</b>选命中部位、
    /// <b>各自</b>过护甲三段判定、<b>各自</b>结算伤害与效果——一枪可同时中头、胸、左臂，且每颗各自被挡下/半伤/全伤。
    /// 见 <see cref="CombatResolver.ResolveVolley"/>（引擎侧）与 Godot 侧的 N 枚独立 Projectile（空间侧）。</para>
    ///
    /// <para><b>定位：清丧尸的武器，不是打劫掠者的武器</b>（有意为之，别当平衡问题"修"成万金油）。
    /// 具体伤害、穿透、冷却、射程、衰减与扩散均以 Wiki 配置表为准；实时层负责距离、弹道与扩散。
    /// 与<b>自制猎枪</b>构成互补的两把自制枪：<b>猎枪钻甲、霰弹清群</b>。</para>
    /// ⑤锐伤：铅丸破皮撕裂，与其余枪械同类型（转钝逻辑照旧）。</para>
    /// </summary>
    public static Weapon ImprovisedShotgun() => CombatCatalog.Weapon("improvised_shotgun");

    // ==================== 弓弩（弓与弩的基础武器表） ====================
    //
    // 用户拍板：「弓可以分为：短弓/反曲弓/长弓/竞技复合弓（不可制作）/狩猎弓（不可制作）」
    //           「弩和弓共用弹药类型，可分为：单手轻弩/双手重弩/复合弩（不可制作）」
    //
    // 全表共性：
    //  ① **远程锐器**——护甲表「挡锐器」列写的就是"刀/箭"。
    //  ② 伤害下限与其他远程锐器的具体口径以 Wiki 配置为准：箭是「飞出去的刀」，
    //     靠锐利切入而非动能贯穿，斜面掠射（擦过划开一道口子）是常态。故适用用户的「刀可以轻划一下」，
    //     而**不**适用枪械的「子弹没有擦破皮，打中就是打中」（对照：自制猎枪下限 4，不降到 1）。
    //     1~上限 的极大方差正是「要么擦过、要么扎穿」的手感。——此三条推理由 impl-bow 提出，此处沿用。
    //  ③ **无枪托近战 profile**：所有枪贴脸都能砸，弓弩不能——被摸到就只能挨打。这是远程潜行武器该付的代价，
    //     是设计，不是遗漏。
    //  ④ **末端衰减浅**（FalloffFloor ≥ 0.65 ＞ 自制猎枪 0.45）：箭靠质量+锐利，不像弹丸那样被空气阻力
    //     迅速吃掉动能。
    //  ⑤ **吃箭矢**（AmmoKey = AmmoKeys.Arrow，1 支/次）。箭**不吃火药**（枪弹要火药→要燃料，与火把/发电竞争）
    //     且**能捡回来一些**（<see cref="Archery.ArrowRecoveryRate"/>，具体回收率以 Wiki 配置为准）——
    //     这是弓弩相对枪唯一实打实的优势：打不动，但打得起、打得久。
    //     ⚠ 回收率不意味着箭矢免费：**弓弩不是免费远程**，只是后勤压力小于枪。
    //  ⑥ **DPS 全部低于步枪**（连"最强弓×最好箭"也不例外，见 ArcheryTests）：弓弩的价值在潜行与后勤，不在输出。
    //
    // **箭会反过来改写这里的数值**——最终属性 = 本表基础属性 ⊗ 箭的乘法修正（见 <see cref="Archery.Combine"/>）。
    // 故本表的每一个数字都是「搭自制箭（基线）时的值」。
    //
    // **弓 vs 弩不是换皮**：弩靠机械储能 → 穿透高、上弦慢（平均冷却更长），且能做出唯一的单手远程弓弩；
    // 弓靠人力 → 出手快、射程远。各自的生态位见每把武器的注释。数值全部「拟定待调」。

    /// <summary>
    /// 短弓：**入门弓**，全部弓里最弱的一把——但最便宜、出手快，具体属性以 Wiki 配置为准。
    /// <para>
    /// <b>它承接 <c>RecipeBook</c> 的 <c>handmade_bow</c> 配方</b>（原名「自制弓」）。那条配方早于弓弩体系存在，
    /// 产物经 <c>CraftOutputFactory</c> 落地为 <c>Item.Weapon(配方显示名)</c>，却**查不到任何武器数值**＝悬空引用。
    /// 用户拍板的弓表里没有「自制弓」这个名字，保留它就是凭空多出一把弓；其配方本来就是最朴素的短弓。
    /// 故配方 <b>DisplayName 改为「短弓」</b>（Id/OutputKey 仍是
    /// <c>handmade_bow</c>，内部键不动），悬空引用由本工厂接上。
    /// </para>
    /// </summary>
    public static Weapon ShortBow() => CombatCatalog.Weapon("short_bow");

    /// <summary>
    /// 反曲弓：**标准均衡款**，可制作。弓臂末端反着弯回去，同样的长度多出几分力道——
    /// 读表时它是弓的"中位数"（伤害/穿透/冷却/射程都在短弓与长弓之间）。
    /// </summary>
    public static Weapon RecurveBow() => CombatCatalog.Weapon("recurve_bow");

    /// <summary>
    /// 长弓：**射程之王**，可制作。代价是拉满费时与体型，具体属性以 Wiki 配置为准。
    /// </summary>
    public static Weapon Longbow() => CombatCatalog.Weapon("longbow");

    /// <summary>
    /// 竞技复合弓：**精度之王**，且滑轮省力；具体属性以 Wiki 配置为准。
    /// <b>不可制作，只能搜刮</b>（超市运动区）。生态位＝**精确点名**：打得准、打得快，但伤害/穿透只是中上。
    /// </summary>
    public static Weapon CompetitionCompoundBow() => CombatCatalog.Weapon("competition_compound_bow");

    /// <summary>
    /// 狩猎弓：**全表最快的弓**。<b>不可制作，只能搜刮</b>（守林人小屋 / 河边小屋枪柜）。
    /// <para>
    /// 🔴 <b>用户拍板的设计意图</b>：「竞技复合弓和狩猎弓是**同级别武器**，区别是**竞技复合弓远而准，狩猎弓快**」。
    /// 两把弓在射程、精度、穿透和攻速上互补，不是简单的强弱关系；具体轴值以 Wiki 配置为准。
    /// </para>
    /// <para>
    /// 伤害与攻速取舍由 Wiki 配置表达，具体调整不在注释中复制。
    /// </para>
    /// <para>
    /// ⚠ 「全表最快的弓」是它<b>唯一</b>的人设，具体冷却以 Wiki 配置为准。
    /// 压的是伤害，不是快。
    /// </para>
    /// <para>
    /// 🔴 改弓的基础伤害**不会**打乱任何箭的相对关系——<see cref="Archery.Combine"/> 里箭对弓是**全乘算**修正
    /// （见该函数注释：乘法是为了让同一支箭在不同弓上语义一致）。
    /// </para>
    /// </summary>
    public static Weapon HuntingBow() => CombatCatalog.Weapon("hunting_bow");

    /// <summary>
    /// 单手轻弩：**唯一的单手弓弩，可双持**，可制作。扣扳机像开枪一样容易（不需要一直拉着弦），
    /// 麻烦的是打完之后——上弦要两只手，还要时间。
    /// <para>
    /// 生态位＝**全表最弱的远程**，但它是唯一能腾出另一只手的弓弩
    /// （配匕首 / 火把 / 另一把轻弩）。双持轻弩＝先射两发，然后就是漫长的绝望。
    /// </para>
    /// </summary>
    public static Weapon LightCrossbow() => CombatCatalog.Weapon("light_crossbow");

    /// <summary>
    /// 双手重弩：**破甲重炮**，可制作；具体属性与制作成本以 Wiki 配置为准。
    /// </summary>
    public static Weapon HeavyCrossbow() => CombatCatalog.Weapon("heavy_crossbow");

    /// <summary>
    /// 复合弩：弩的天花板——**破甲之王**，具体属性以 Wiki 配置为准。
    /// <b>不可制作，只能搜刮</b>（金手指帮根据地）。
    /// <para>
    /// ⚠ 它与重头箭的穿透叠加受 <see cref="Archery.MaxPenetration"/> 封顶，
    /// 防止护甲系统被组合效果绕过；具体封顶值以 Wiki 配置为准。
    /// 而它需要一把搜刮来的稀有弩 + 一支手工重箭才凑得出来。
    /// </para>
    /// </summary>
    public static Weapon CompoundCrossbow() => CombatCatalog.Weapon("compound_crossbow");

    /// <summary>弓弩全集（8 把：5 弓 + 3 弩）。<see cref="Archery"/> 的组合修正与 Sim 生态位校准按此遍历。</summary>
    public static IReadOnlyList<Weapon> ArcheryArsenal() => new[]
    {
        ShortBow(), RecurveBow(), Longbow(), CompetitionCompoundBow(), HuntingBow(),
        LightCrossbow(), HeavyCrossbow(), CompoundCrossbow(),
    };

    // ---- 天生武器 ----

    /// <summary>丧尸爪击：近战锐器。名沿用"爪击"。
    /// <para>
    /// 🔴 <b>单只丧尸就该弱——这是设计意图，不是平衡事故</b>。用户原话：「丧尸虽然伤害低了，但是丧尸本就是
    /// <b>以量取胜</b>，三打一的战力比是九比一，所以<b>单一丧尸不该很强</b>。」
    /// <b>兰彻斯特平方律在引擎里成立，梯度是断崖不是斜坡</b>——<b>现值一律以
    /// <c>docs/research/2026-07-14-lanchester.md</c> 为准，别把它抄进别处</b>（这类值会随引擎/数值改动而漂，
    /// 抄下来就变成下一条 born-stale）。参考量级（<b>2026-07-17 全仓重跑</b>、good 端 <c>a4b09b1</c>）：
    /// 围攻梯度是断崖不是斜坡；具体结果以 <c>docs/research/2026-07-14-lanchester.md</c> 和当前 harness 为准。
    /// 历次武器调参一律<b>不要当 bug"修复"</b>，当前伤害与穿透以 Wiki 配置为准。
    /// </para>
    /// 当前值＝用户在 Wiki 表上定的（表赢代码），具体数值不在此处复制。</summary>
    public static Weapon ZombieClaw() => CombatCatalog.Weapon("zombie_claw");

    /// <summary>
    /// 布鲁斯（狗）撕咬：天生近战锐器。**极低伤害**（用户口径：布鲁斯难以独自击杀敌人，靠缠斗拖住给道格
    /// 创造输出窗口）。天生武器（同爪击不入 <see cref="Arsenal"/>，玩家不可穿脱）。
    /// <para>伤害、穿透与冷却以 Wiki 配置为准；历史版本说明不在工厂注释中复制。</para>
    /// <para>校准依据（param-calibration，dogcal）：设计定义以「高闪避+低伤」为核心对，「咬比爪快」为最弱 flavor 故让出
    /// ——咬合节奏与伤害以 Wiki 配置为准，配合 <c>Dog.DodgeChance</c>（见 <c>Dog.cs</c>）。
    /// <para>
    /// 🔴 <b>数字一律以真源报告为准，别在这里抄快照</b>：<c>docs/research/2026-07-12-dog-calibration.md</c>
    /// （harness＝<c>src/DeadSignal.Sim/DogCalibration.cs</c>，重跑：<c>dotnet run --project src/DeadSignal.Sim -- dogcal</c>）。
    /// 历史校准结果不在工厂注释中复制，当前结论以 dogcal 报告为准。
    /// </para>
    /// <para>
    /// ⚠ <b>已知偏离，待用户拍板</b>（2026-07-17 由 sweep-actors 重跑复现，与 committed 报告一致）：
    /// 当前校准是否需要回调属于数值决策，未自裁；改动会影响感知与 Sim 基线，须走 [DECISION]。
    /// </para></para>
    /// </summary>
    public static Weapon DogBite() => CombatCatalog.Weapon("dog_bite");

    /// <summary>
    /// 拳脚：人的天生武器 —— <b>空手近战</b>（用户拍板：「空手和持弓近战都视作空手近战，造成钝伤」）。
    /// 拳打脚踢：钝伤、低伤害、快冷却、噪音全表最小、破甲为零。规则见 <see cref="Unarmed.MeleeFor"/>。
    /// <para>
    /// 同爪击/撕咬，是<b>天生武器</b>：<b>不入 <see cref="Arsenal"/></b>（不可穿脱、不进库存、不参与 Sim 遍历）
    /// ⇒ 既有 Sim 基线结构性零漂移。
    /// </para>
    /// <para>
    /// 数值与梯度以 Wiki 配置为准：空手应是低伤、低噪音的天生武器，不能替代真正的刀。
    /// 布鲁斯的战力不只来自咬合 DPS，还来自 <c>Dog.DodgeChance</c> 的高闪避缠斗（见 <see cref="DogBite"/>）。
    /// </para>
    /// </summary>
    public static Weapon Fists() => CombatCatalog.Weapon("fists");

    /// <summary>
    /// 玩家/敌方可用武器全集（不含天生的丧尸爪击/撕咬/拳脚），Sim 聚合模拟按此顺序遍历。
    /// 「栓动猎枪」已按用户在数值表上的删除撤下（见上方墓碑注释）。
    /// 顺序与旧 Sim 行内武器表一致，便于对照基线；新武器一律**追加在末尾**，不插队——既有行的 Sim 数字不受影响
    /// （Sim 按 <c>cell.Idx</c> 派生随机种子，插队会打乱其后所有武器的随机流 ⇒ 基线漂移）。
    /// <para>
    /// 唯一的例外是原「自制弓」<b>原地</b>换成 <see cref="ShortBow"/>（同一把武器改名 + 重标定，
    /// 见 <see cref="ShortBow"/> 注释）。原地替换而非移位，正是为了不打乱其后 <see cref="ImprovisedShotgun"/> 的随机流。
    /// 其余弓弩全部追加在末尾。
    /// </para>
    /// <para>
    /// ⚠ 本表列的是弓弩的**基础属性**。实战里弓弩的属性会被搭上的箭改写（<see cref="Archery.Combine"/>），
    /// 故 Sim 跑弓弩时须显式指定用哪种箭；本表的值＝搭「自制箭」（基线）时的值。
    /// </para>
    /// </summary>
    public static IReadOnlyList<Weapon> Arsenal() => new[]
    {
        Dagger(), Shortsword(), Rapier(), Longsword(), Pitchfork(), Greatsword(),
        Club(), SpikeHammer(), Warhammer(),
        ImprovisedHuntingGun(), Pistol(), Smg(), Rifle(), SniperRifle(),
        ShortBow(), ImprovisedShotgun(),
        RecurveBow(), Longbow(), CompetitionCompoundBow(), HuntingBow(),
        LightCrossbow(), HeavyCrossbow(), CompoundCrossbow(),
        // [批次25·T44] 消防斧 —— **追加在末尾，不插队**（CLAUDE.md 铁律）。
        // Sim 按 idx 派生种子：把它插到近战那一段（它在源码里就写在那儿，读起来才顺）会让其后 20+ 把武器
        // 的随机流整体前移，既有基线当场漂移。**源码位置随语义，Arsenal 顺序随历史** —— 这两件事故意分开。
        Axe(),
        // [T56] 骨刀 —— 同样**追加在末尾**（它在源码里写在近战锐器那一段，读起来才顺；这里只按历史排队）。
        // 它从前有配方却不在本表 ⇒ 造得出来、拿不起来（详见 BoneKnife() 注释）。
        BoneKnife(),
    };

    // ---- 玩家可见风味文案（黑色幽默）：武器名 → 一行描述 ----
    // 由库存物品 UI（StashPanel/CharacterPanel）经 Item.Weapon 自动填充展示，不参与战斗结算。
    // 覆盖 Arsenal 全表 + 天生武器（爪击/撕咬）。骨刀与短弓（原「自制弓」）已各自补上工厂并入 Arsenal，
    // 其风味文案随 foreach 自动收录，不再单列（详见下方 BuildFlavor 说明）。

    // [锚点·预留] 「长矛」这把武器游戏中暂无（最近的是草叉/长剑）；用户锚点原句「用尖的那端」在此登记，
    // 一旦 WeaponTable 新增 Spear() 工厂，把它的 Description 填成该句、并从下方补一条 "长矛" 即可。
    private static readonly System.Collections.Generic.Dictionary<string, string> _flavorByName = BuildFlavor();

    private static System.Collections.Generic.Dictionary<string, string> BuildFlavor()
    {
        var d = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var w in Arsenal())
        {
            d[w.Name] = w.Description;
        }
        d[ZombieClaw().Name] = ZombieClaw().Description;
        d[DogBite().Name] = DogBite().Description;
        // 「自制弓」与「骨刀」原先都在这里硬编码——因为它们有配方却没有 Weapon 工厂。
        // 两者现已分别补上 HandmadeBow() / BoneKnife() 并入 Arsenal，描述由上面的 foreach 自动收录
        // （单一真源），故此处的硬编码全部删去，避免两份文案漂移。
        // ⇒ 本字典现在**只**兜天生武器（爪击/撕咬）；可制作武器一律走 Arsenal。
        return d;
    }

    /// <summary>按武器显示名取一行风味描述（查不到返回空串）。供消费层 Item.Weapon 自动填充库存物品描述。</summary>
    public static string DescriptionOf(string name)
        => name != null && _flavorByName.TryGetValue(name, out var d) ? d : "";
}
