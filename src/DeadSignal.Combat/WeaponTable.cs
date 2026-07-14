namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威武器数据源。Godot 消费层、Sim 聚合模拟、Sim 对决战报全部从此读取，
/// 消除此前 CombatData / Sim.Program / Sim.DuelReport 三处各自维护武器表的数据源分裂。
///
/// 数值口径：重叠武器（匕首/手枪/爪击）统一采用 <b>Godot 侧现行值</b>；仅 Sim 侧存在的武器
/// （短剑/长剑/重剑/棍棒/尖头锤/破甲锤/冲锋枪/步枪/狙击枪）沿用其原值；「土制枪」已于批次18b 按用户拍板删除，
/// 由全新的可制作武器 <see cref="ImprovisedHuntingGun"/>（自制猎枪）取代。
/// 穿透/伤害类型来自设计文档第 5 节；伤害区间/攻速为原型期<b>拟定待调</b>，靠 Sim 拉表校准。
///
/// <para><b>全局慢节奏（用户拍板：多角色操控需要慢战斗）</b>：全表冷却重标定。
/// 近战锚点匕首 1.4 / 长剑 3.6，按 ×2~3.3 随沉重度插值（破甲锤外推离谱故按手感封顶 4.5）；爪击 2.3（敌方同步）。
/// <b>枪械倍率经用户回调 ×5~5.6→×3~4</b>（原步枪 4.5/狙击 7.5 两锚作废）：手枪 2.0 / 冲锋枪轮冷却 2.6 / 步枪 2.8 /
/// 狙击 5.0 / 自制猎枪 3.5，目标"压制多数近战但非碾压"（步枪vs长剑 目标带已作废，见下）。
/// 冲锋枪冷却由 1.8→2.6（用户拍板：削冷却不削伤害）以消除其对全表的碾压。
/// 新增民用<b>栓动猎枪</b>冷却锚 4.5（用户最初给它的）。均拟定待调。</para>
///
/// <para><b>批次18 伤害区间重标定（用户拍板）</b>：
/// ①<b>近战锐器区间整体下移</b>——下限压到 1，上限 = 原上限 −(原下限 −1)（宽度不变、平均下降 43~48%）。
/// 目的：低护甲值（布衣锐防 6、腐皮 3）此前<b>永远挡不下任何一击</b>（挡下门槛 = 护甲值×(1−穿透)/2 恒低于原下限），
/// 下移后低掷点才会被布甲吃掉。代价是 PvE 变慢变磨——<b>用户已知悉并选择此取向</b>，不要当平衡问题"修复"回去。
/// ①b <b>枪械不降下限</b>（用户拍板，[DECISION] 已关闭）：刀可以"轻划一下"（1 点伤害说得通），但
/// <b>子弹没有"擦破皮"这一说——打中就是打中</b>。故 6 把枪维持原区间（手枪 8~14 / 冲锋枪 10~18 /
/// 步枪 20~35 / 栓动猎枪 16~28 / 狙击枪 40~70；自制猎枪后由用户单独重定位为 4~16/穿25%），
/// 这同时保住了枪对近战的压制力。
/// ⚠ 后果：近战被削 43~48%、枪一点没削 → <b>枪相对近战变强</b>（步枪vs长剑 93.5%）。
/// <b>用户拍板接受</b>（原话：枪被近身只能用很弱的枪托近战，且子弹有限）——旧的 70~80% 目标带<b>作废</b>，以 93.5% 重定基。
/// ②<b>丧尸爪击 3~9 → 1~5</b>（用户手定，非公式平移）：敌我同步下调，实测反而把「匕首打丧尸」从 90.2% 抬到 92.6%。
/// ③<b>钝器提基础伤害</b>（棍棒/尖头锤）：钝器吃不到下移红利（钝防门槛低于其下限），靠提伤补位。
/// ④<b>破甲锤不动</b>（用户拍板）。见 <c>WeaponRecalibTests</c>。</para>
/// </summary>
public static class WeaponTable
{
    // ---- 近战锐器 ----

    /// <summary>匕首：近战锐器，穿透 9%（文档：匕首 9%）。批次18 区间下移 4~10 → 1~7（用户原案锚点，其余锐器照此公式推）。</summary>
    public static Weapon Dagger() => new()
    {
        Name = "匕首",
        Description = "小巧、贴身、安静——很多故事都是从背后一把匕首开始的。",
        DamageMin = 1,           // 拟定待调
        DamageMax = 7,          // 拟定待调
        Penetration = 0.09,
        DamageType = DamageType.Sharp,
        StructureFactor = 0.4,   // 砸墙系数：拿匕首捅墙是徒劳（锐器兜底档）
        NoiseRadius = 90,            // 拟定待调：全近战最静——短促捅刺，几乎不带风声。呼应它自己的 flavor「小巧、贴身、安静」
        CanDualWield = true,
        AttackInterval = 1.4,    // 拟定待调（全局慢节奏锚点：匕首 0.7→1.4，×2.0）
    };

    /// <summary>短剑：近战锐器，穿透 12%。</summary>
    public static Weapon Shortsword() => new()
    {
        Name = "短剑",
        Description = "一把趁手的短剑，比匕首多一寸，也多一分底气。",
        DamageMin = 1,           // 拟定待调
        DamageMax = 15,          // 拟定待调
        Penetration = 0.12,
        DamageType = DamageType.Sharp,
        StructureFactor = 0.4,   // 砸墙系数：刃口是拿来切开血肉的，木头铁皮不吃这套
        NoiseRadius = 105,           // 拟定待调：有挥砍幅度了，风声与金属碰撞都上来
        AttackInterval = 2.4,    // 拟定待调（近战倍率曲线 0.9→×2.64）
    };

    /// <summary>刺剑：单手近战锐器，突刺路线——伤害区间窄、穿透略高于短剑，攻速略快。可双持。</summary>
    public static Weapon Rapier() => new()
    {
        Name = "刺剑",
        Description = "轻巧的刺剑，讲究的是一击致命，而不是大力出奇迹。",
        DamageMin = 1,           // 拟定待调（刺击：区间窄）
        DamageMax = 12,          // 拟定待调
        Penetration = 0.15,      // 拟定待调（刺击穿透略高于短剑 12%）
        DamageType = DamageType.Sharp,
        StructureFactor = 0.3,   // 砸墙系数：刺击对结构尤其无用——扎个洞，墙纹丝不动
        NoiseRadius = 95,            // 拟定待调：轻剑突刺，动静略大于匕首
        CanDualWield = true,     // 拟定待调
        AttackInterval = 1.9,    // 拟定待调（近战倍率曲线 0.8→×2.32；略快于短剑）
    };

    /// <summary>长剑：近战锐器，穿透 18%。双手武器。</summary>
    public static Weapon Longsword() => new()
    {
        Name = "长剑",
        Description = "双手长剑，一寸长一寸强——前提是你抡得动。",
        DamageMin = 1,          // 拟定待调
        DamageMax = 21,          // 拟定待调
        Penetration = 0.18,
        DamageType = DamageType.Sharp,
        StructureFactor = 0.4,   // 砸墙系数：再长的刃砍在门上也只是崩口
        NoiseRadius = 120,           // 拟定待调：双手抡圆，破风声明显
        TwoHanded = true,        // 拟定待调
        AttackInterval = 3.6,    // 拟定待调（全局慢节奏锚点：长剑 1.1→3.6，×3.27）
    };

    /// <summary>重剑：近战锐器，穿透 24%。双手武器。</summary>
    public static Weapon Greatsword() => new()
    {
        Name = "重剑",
        Description = "沉重的大剑，挥一下费半条命，中一下要一条命。",
        DamageMin = 1,          // 拟定待调
        DamageMax = 27,          // 拟定待调
        Penetration = 0.24,
        DamageType = DamageType.Sharp,
        StructureFactor = 0.5,   // 砸墙系数：全靠自重砸出的那一点效果（仍是锐器：拿它当破门锤会先毁了刃）
        NoiseRadius = 135,           // 拟定待调：沉重挥击，抡一下半条街都听得见风
        TwoHanded = true,        // 拟定待调
        AttackInterval = 4.0,    // 拟定待调（重近战曲线 1.1→1.8 段插值，1.4→4.0，封顶带内）
    };

    /// <summary>草叉：双手近战锐器，农具改造——多齿突刺，穿透中等、伤害区间较宽、攻速偏慢。照长剑数值风格。</summary>
    public static Weapon Pitchfork() => new()
    {
        Name = "草叉",
        Description = "农具改的草叉，本来是叉草的，现在叉什么全看你。",
        DamageMin = 1,           // 拟定待调
        DamageMax = 18,          // 拟定待调
        Penetration = 0.16,      // 拟定待调（多齿刺击，略低于长剑 18%）
        DamageType = DamageType.Sharp,
        StructureFactor = 0.3,   // 砸墙系数：三根尖齿，戳墙等于挠痒
        NoiseRadius = 120,           // 拟定待调：长杆多齿，挥起来带风、扎中带响
        TwoHanded = true,        // 拟定待调
        AttackInterval = 3.7,    // 拟定待调（重近战曲线 1.2→3.7，略慢于长剑）
    };

    // ---- 近战钝器 ----

    /// <summary>棍棒：近战钝器，穿透 3%（文档：棍棒级 3%）。道格开局武器。
    /// 批次18 提伤 7~9 → 10~13（平均 8 → 11.5，+44%，落在 weapon-calib 建议带 +30~50% 内）。
    /// 新环境实测（锐器已下移、爪击已砍半）打丧尸 81.9% → 94.5%，仍低于同冷却(2.4s)的短剑 97.3%——
    /// 达成"接近但不反超同档锐器"。钝器的代价在破甲：打长剑手·中甲 38.3% ≪ 短剑 52.1%。</summary>
    public static Weapon Club() => new()
    {
        Name = "棍棒",
        Description = "一根结实的棍棒，简单、可靠、不讲道理。",
        DamageMin = 10,          // 拟定待调（批次18 提伤：7 → 10）
        DamageMax = 13,          // 拟定待调（批次18 提伤：9 → 13；区间仍窄=钝器稳定输出）
        Penetration = 0.03,
        DamageType = DamageType.Blunt,
        StructureFactor = 1.2,   // 砸墙系数：钝器，但轻——能砸开木板，砸不动铁皮
        NoiseRadius = 110,           // 拟定待调：钝击闷响——不脆但传得开
        AttackInterval = 2.4,    // 拟定待调（近战倍率曲线 0.9→×2.64）
    };

    /// <summary>尖头锤：近战钝器，穿透 5%。
    /// 批次18 提伤 12~16 → 15~20（平均 14 → 17.5，+25%，略低于 weapon-calib 旧建议 +30%——
    /// 因新环境已把它从 80.7% 抬到基线，+25% 即达 93.5%，再高就要反超同档长剑 95.3%）。</summary>
    public static Weapon SpikeHammer() => new()
    {
        Name = "尖头锤",
        Description = "带尖的锤子，砸不服的，就扎服。",
        DamageMin = 15,          // 拟定待调（批次18 提伤：12 → 15）
        DamageMax = 20,          // 拟定待调（批次18 提伤：16 → 20）
        Penetration = 0.05,
        DamageType = DamageType.Blunt,
        StructureFactor = 1.6,   // 砸墙系数：锤头集中受力，专治木门与钉板
        NoiseRadius = 130,           // 拟定待调：沉锤砸击，闷响里带一记脆的
        AttackInterval = 3.7,    // 拟定待调（重近战曲线 1.2→3.7）
    };

    /// <summary>破甲锤：近战钝器，穿透 35%（破甲路线，全近战最高穿透）。重型双手武器。
    /// 穿透 20%→35% 拟定待调（用户拍板"提升穿透力"）：坐实"破甲专精"身份（＞重剑 24%）。
    /// 注意：Sim 复验显示穿透对「破甲锤 vs 长剑·满甲」纯对决胜率几乎无影响（20%→70% 仅 9.9%→10.5%），
    /// 该对决瓶颈是攻速 1.8s 而非穿透——穿透买不回胜率。见回报，若要重甲对决表现需另议攻速/直击。</summary>
    /// <para>批次18：<b>用户拍板不动</b>——它已是钝器天花板（打丧尸 96.8%、打长剑手·中甲 57.8%，与重剑 57.6% 齐平）。</para>
    public static Weapon Warhammer() => new()
    {
        Name = "破甲锤",
        Description = "专治各种不服的破甲锤，铁皮罐头也照开不误。",
        DamageMin = 20,          // 拟定待调
        DamageMax = 28,          // 拟定待调
        Penetration = 0.35,      // 拟定待调（20%→35%，破甲专精，全近战最高穿透）
        DamageType = DamageType.Blunt,
        StructureFactor = 2.0,   // 砸墙系数：**全表最强破门武器**——「铁皮罐头也照开不误」这句介绍，从此有数值兜底
        NoiseRadius = 150,           // 拟定待调：**全近战最响**——砸甲当当作响，「铁皮罐头也照开不误」就是这个动静
        TwoHanded = true,        // 拟定待调
        // 拟定待调（全局慢节奏）：沿轻武器倍率外推 1.8×5.5≈9.9s 太离谱，按手感封顶到 4.5s
        // ——长剑 3.6 量级偏上、全近战最慢，与步枪(栓动猎枪)4.5 同档。
        AttackInterval = 4.5,
    };

    // ---- 远程 ----

    /// <summary>自制猎枪（<b>全新武器</b>；旧的「土制枪」已按用户拍板<b>删除</b>，不是改名）：远程锐器，双手长枪，
    /// 伤害 4~16、穿透 25%。玩家<b>可自己制作</b>（配方 <c>improvised_hunting_gun</c>：卡尺精工 + 《土法化学笔记》）。
    /// <para><b>用户拍板值</b>：伤害 4~16、穿透 25%。<b>本方拟定值</b>（皆待调）：双手（长枪，不是单手土枪）、
    /// 冷却 3.5s、射程 260 / 满伤 90 / 末端 0.45、误差角 5°、枪托钝击 5~9。</para>
    /// <para><b>⚠ 定位口径已被同批次改动推翻</b>：派单原话是"穿透 25% 比步枪 21% 还高 → 专克穿甲目标"，
    /// 但同批用户又把<b>步枪穿透提到 40%</b> → 自制猎枪 25% <b>反而低于</b>步枪。故它不是"穿甲专精"，
    /// 真实定位是<b>唯一能自己造的枪</b>（不靠掉落）：伤害低、精度差、射程短，胜在可再生产。
    /// 另见 <c>CombatResolver</c>：能否击穿甲层主要由<b>伤害骰</b>决定，穿透系数只压低防御骰上限——
    /// 低伤高穿透在多层甲前并不"钻得深"（实测平均穿透层数低于步枪）。</para></summary>
    public static Weapon ImprovisedHuntingGun() => new()
    {
        Name = "自制猎枪",
        Description = "用水管和废铁凑出来的猎枪，准头听天由命，后坐力管够——开火之后，你和目标谁更慌还不一定。",
        DamageMin = 4,           // 用户拍板
        DamageMax = 16,          // 用户拍板
        Penetration = 0.25,      // 用户拍板
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪托（长枪身有分量），不是子弹——子弹打不穿承重墙
        TwoHanded = true,        // 拟定待调（长枪：抵肩双手；旧土制枪的单手是"管子枪"设定，已随其删除作废）
        IsRanged = true,
        AmmoKey = AmmoKeys.MediumBullet,   // 自制猎枪：中子弹，1 发/次。唯一能自己造的枪——枪能造、弹也能造，但都得先有子弹零件。
        NoiseRadius = 450,           // 拟定待调：土法枪管、粗糙闭锁——装药不多但炸得野，响度低于制式枪
        BaseSpreadDegrees = 5,   // 拟定待调（长枪管比土枪准，但土法工艺仍差：步枪 2 / 栓动猎枪 3 / 本枪 5）
        AttackInterval = 3.5,    // 拟定待调（单发折膛，无枪机可拉：慢于步枪 2.8，快于栓动猎枪 4.5）
        MaxRange = 260,          // 拟定待调（长枪管给出真实射程，但远不及民用栓动猎枪 420）
        FalloffStart = 90,       // 拟定待调
        FalloffFloor = 0.45,     // 拟定待调（土法枪管：出满伤段掉得比栓动猎枪 0.55 更狠）
        // 枪托近战（3.0kg，土法长枪身）：见下方「枪托近战数值定稿」总说明。
        StockMeleeDamageMin = 5,
        StockMeleeDamageMax = 9,
        StockMeleePenetration = 0.02,
        StockMeleeInterval = 2.5,       // DPS 2.80
        StockMeleeNoiseRadius = 105,
    };

    // ═══════════════════════════ 枪托近战数值定稿（批次21·T7）═══════════════════════════
    //
    // 【它是什么】"打空了 / 被贴脸了，只好抡枪托砸"——**绝望手段**，不是一条武器路线。
    //
    // 【定稿前的 bug】旧值让步枪枪托打出 6~10 / 1.5s ＝ **5.33 伤/秒**，而棍棒是 10~13 / 2.4s ＝ **4.79**。
    // 也就是说：**抡枪托比抡棍棒还猛**。那样的话"我该不该带把近战武器"根本不成其为一个选择——
    // 带把枪就够了，它自带一根比棍棒更好的棍子。
    //
    // 【定稿口径】把七把枪的枪托 DPS 全部压进 **2.65 ~ 2.84** 这条窄带，即：
    //
    //        拳脚 2.08  ＜  【枪托 2.65~2.84】  ＜  匕首 2.86  ＜  棍棒 4.79  ＜  长剑/锤…
    //
    //   一句话：**比拳头强，但不如一把真正的刀。** 打空了的枪还能救命，但救不了场——
    //   想打近战，就老老实实带把近战武器（或者去改装台把它改了，见 WeaponModCatalog 的三种型态）。
    //
    // 【重量怎么进数值】重量**不改变 DPS**（七把枪的 DPS 几乎一样平），只在「单击伤害 ↔ 冷却」之间**搬运**：
    //   重枪（狙击 6.0kg：8~13 / 3.7s）单击痛、抡得慢；轻枪（手枪 1.0kg：3~6 / 1.7s）单击轻、抡得快。
    //   这是对的——枪不是为砸人设计的，多重的枪你都是"拿反了在抡"，效率天花板一样低；
    //   重量只决定你这一下砸得多狠、以及你多久才能再抡起来。重量取自 CarryWeight 的武器表（单一事实源）。
    //
    // 【穿透】一律 0.02~0.03（近乎无）——没装刺刀的枪托砸不穿甲。破甲是**武器**的特权（同拳脚 0）。
    // 【噪音】按枪身质量 85（手枪柄）~125（狙击枪托），锚在棍棒 110。**不继承枪本体的枪声**
    //   （砸不是打，没有枪声：步枪枪声 600 vs 其枪托 115），但也绝不是哑剧。
    // 【弓弩没有这一段】它们没有枪托——空手/持弓的近战由 Unarmed（拳脚）承担，是另一位 agent 的活。
    // 全部数值**拟定待调**。改这一段**不会**惊动 Sim：Duel 是 1v1 无距离对决，枪永远走不到"贴脸抡枪托"
    //   那条分支 ⇒ Sim 的结算路径**读不到** StockMelee*（结构性零漂移，已 A/B 实证）。
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>手枪：远程锐器，穿透 15%（文档：手枪 15%）。数值采 Godot 值（8-14，此前 Sim 为 12-20）。</summary>
    public static Weapon Pistol() => new()
    {
        Name = "手枪",
        Description = "一把手枪，几次讲道理的机会，还能换只手接着讲。",
        DamageMin = 8,           // 拟定待调
        DamageMax = 14,          // 拟定待调
        Penetration = 0.15,
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪柄砸击（轻但快），不是子弹
        CanDualWield = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.ShortBullet,   // 手枪：短子弹，1 发/次。弹药代价最低的枪（1 个零件出 8 发）——杂兵用它，把好弹省给真要命的场面。
        NoiseRadius = 350,           // 拟定待调：全枪械最轻的响——短枪管、小装药。想安静又只有枪时，它是次优解
        BaseSpreadDegrees = 3,   // 拟定待调
        AttackInterval = 2.0,    // 拟定待调（枪械倍率回调 ×3~4：0.5→×4.0）
        MaxRange = 200,          // 拟定待调（手枪：近而陡）
        FalloffStart = 55,       // 拟定待调
        FalloffFloor = 0.5,      // 拟定待调
        // 枪托近战（1.0kg，全表最轻）：拿枪柄敲人——最快、最弱、最安静的枪托。DPS 2.65（全表最低）。
        StockMeleeDamageMin = 3,
        StockMeleeDamageMax = 6,
        StockMeleePenetration = 0.02,
        StockMeleeInterval = 1.7,
        StockMeleeNoiseRadius = 85,     // 全表最轻的枪托动静（枪声 350 → 砸击 85）
    };

    /// <summary>冲锋枪：远程锐器，穿透 18%。双手抵肩；枪托可贴脸钝击。
    /// 用户拍板：三连发（一次射击打 3 发）。攻击模型＝冷却→三连发→冷却→三连发。
    /// AttackInterval 语义为连发之后的冷却；DPS＝3 发/循环。拟定待调。</summary>
    public static Weapon Smg() => new()
    {
        Name = "冲锋枪",
        Description = "三连发的冲锋枪，子弹管够的时候，谁跟你讲道理。",
        DamageMin = 10,          // 拟定待调
        DamageMax = 18,          // 拟定待调
        Penetration = 0.18,
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪托，不是子弹
        // 双手抵肩、**不可双持**（用户拍板「保双手，放弃双持」）。这里曾挂过一行 `CanDualWield = true`，
        // 但双手武器在 WeaponLoadout.EquipToHand 里直接短路走 EquipTwoHanded（占满两手）、根本进不了双持分支，
        // 那行从来没生效过 ⇒ 已删。双手 +15% 攻速（DualWield.TwoHandedSpeedBonus）保留，B18「削冷却不削伤害」
        // 那轮的平衡就是按含 +15% 的 DPS 调的。护栏见 TwoHandEnforcementTests.TwoHandedWeapons_AreNeverDualWieldable。
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        AmmoKey = AmmoKeys.ShortBullet,   // 冲锋枪：短子弹，三连发 → **3 发/次**。泼水一样的伤害，也泼水一样地烧弹：1 个零件的 8 发，够它扣两次半扳机。
        NoiseRadius = 500,           // 拟定待调：三连发＝一次扣扳机响三声，把整条街的注意力都请过来
        BaseSpreadDegrees = 6,   // 拟定待调
        BurstCount = 3,          // 用户拍板：三连发
        BurstInterval = 0.06,    // 拟定待调（连发内每弹间隔，快速点射不随全局慢节奏放缓）
        // 冷却（连发之间）拟定待调：1.8s 实测每秒伤害 21.87、约为次名 2.2 倍（碾压）。
        // 用户拍板削冷却、不削伤害（保住三连发手感）：2.6s——一次开火打 3 发、约每 2.7s 一轮。
        // 三连发是它的特色（爆发点射换更长冷却）。
        AttackInterval = 2.6,
        MaxRange = 280,          // 拟定待调（冲锋枪：中距，衰减中等）
        FalloffStart = 70,       // 拟定待调
        FalloffFloor = 0.45,     // 拟定待调
        // 枪托近战（3.0kg，紧凑折叠托）：短促，但比手枪柄有分量。DPS 2.73。
        StockMeleeDamageMin = 4,
        StockMeleeDamageMax = 8,
        StockMeleePenetration = 0.02,
        StockMeleeInterval = 2.2,
        StockMeleeNoiseRadius = 100,
    };

    /// <summary>步枪（军用定位，与民用"栓动猎枪"并存——用户拍板两把都有）：远程锐器，穿透 <b>40%</b>。双手抵肩；枪托可贴脸钝击。
    /// <para>批次18b <b>用户拍板：穿透 21% → 40% + 改二连发</b>（伤害 20~35 / 冷却 2.8s 不动）：坐实"军用高穿透，专啃多层甲"——
    /// 现为全表第二高穿透（仅次狙击 70%），已超过破甲锤 35%。
    /// 注意 weapon-calib 实测<b>穿透是低杠杆轴</b>（匕首 9%→35% 打丧尸胜率纹丝不动），故本改动主要体现在
    /// <b>多层甲的穿透层数分布与挡下率</b>，整体胜率不会再飙多少。</para></summary>
    public static Weapon Rifle() => new()
    {
        Name = "步枪",
        Description = "军用步枪，站得远、打得准，让对话在安全距离进行。",
        DamageMin = 20,          // 拟定待调
        DamageMax = 35,          // 拟定待调
        Penetration = 0.40,      // 用户拍板（批次18b：0.21 → 0.40，军用高穿透）
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪托重砸，不是子弹
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        AmmoKey = AmmoKeys.MediumBullet,   // 步枪：中子弹，二连发 → **2 发/次**。93.5% 命中 + 穿透 40% 之所以可接受，正因为每扣一次扳机烧掉两发中子弹（1 个零件才出 5 发）——强，但打不起。
        NoiseRadius = 600,           // 拟定待调：制式全威力弹，枪声传得极远。它的强大在噪音上同样要还债
        BaseSpreadDegrees = 2,   // 拟定待调
        BurstCount = 2,          // 用户拍板：二连发（照冲锋枪三连发的既有机制，非另造）
        BurstInterval = 0.08,    // 拟定待调（连发内每弹间隔；略慢于冲锋枪 0.06——步枪点射节奏更沉）
        // ⚠ 用户明令「先直接做成二连发，数值以后再在表格里手调」：伤害 20~35 / 冷却 2.8s / 穿透 40%
        // 一概未动，没有为二连发做任何平衡补偿。后果如实记录：每轮出 2 发＝每秒伤害近乎翻倍，
        // 步枪本就 vs 长剑 93.5%，二连发后只会更强。用户已接受（军用武器难获得，强是正常的）。
        AttackInterval = 2.8,    // 拟定待调（枪械倍率回调 ×3~4：0.8→×3.5；目标步枪vs长剑~70-80%，harness校准）
        MaxRange = 550,          // 拟定待调（步枪：远而缓）
        FalloffStart = 200,      // 拟定待调
        FalloffFloor = 0.6,      // 拟定待调
        // 枪托近战（4.0kg，制式实木托）：抵肩枪托重砸。DPS 2.83。
        StockMeleeDamageMin = 6,
        StockMeleeDamageMax = 11,
        StockMeleePenetration = 0.03,
        StockMeleeInterval = 3.0,
        StockMeleeNoiseRadius = 115,
    };

    /// <summary>栓动猎枪（用户拍板新增，民用猎枪定位，与军用"步枪"并存）：远程锐器，单发郑重、栓动慢冷却。
    /// 伤害/射程介于步枪(20-35/远)与自制猎枪(4-16/近)之间，民用可得性更高。冷却锚 4.5s（用户最初给它的）。
    /// ⚠ 穿透 16% 现为<b>全枪械最低</b>（自制猎枪 25%、步枪已提到 40%），故"穿透夹在步枪与土制枪之间"已不成立。
    /// 全字段拟定待调（伤害/射程/衰减为 draft，靠 Sim 拉表校准）。</summary>
    public static Weapon BoltActionHuntingRifle() => new()
    {
        Name = "栓动猎枪",
        Description = "民用栓动猎枪，一枪一栓，郑重其事——毕竟每一发都得省着用。",
        DamageMin = 16,          // 拟定待调（介于步枪 20 与自制猎枪 4 之间）
        DamageMax = 28,          // 拟定待调（介于步枪 35 与自制猎枪 16 之间）
        Penetration = 0.16,      // 拟定待调（全枪械最低穿透：步枪已 40%、自制猎枪 25%）
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪托重砸，不是子弹
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        AmmoKey = AmmoKeys.MediumBullet,   // 栓动猎枪：中子弹，1 发/次。**用户未点名，我按口径常识判**——它是民用长枪，与步枪/自制猎枪同档。一发一拉栓 → 全表弹药效率最高的枪，穷人的远程解。
        NoiseRadius = 550,           // 拟定待调：大装药长枪管，一声闷雷
        BaseSpreadDegrees = 3,   // 拟定待调（民用光学，介于步枪 2 与自制猎枪 8）
        AttackInterval = 4.5,    // 拟定待调（栓动慢冷却锚点——用户最初给它的 4.5s）
        MaxRange = 420,          // 拟定待调（介于步枪 550 与自制猎枪 130）
        FalloffStart = 160,      // 拟定待调
        FalloffFloor = 0.55,     // 拟定待调
        // 枪托近战（3.5kg，厚重猎枪木托）：略轻于步枪。DPS 2.81。
        StockMeleeDamageMin = 6,
        StockMeleeDamageMax = 10,
        StockMeleePenetration = 0.03,
        StockMeleeInterval = 2.85,
        StockMeleeNoiseRadius = 110,
    };

    /// <summary>狙击枪：远程锐器，穿透 70%（碾压多层甲，弹药稀缺是唯一约束）。双手抵肩；枪托可贴脸钝击。</summary>
    public static Weapon SniperRifle() => new()
    {
        Name = "狙击枪",
        Description = "狙击枪，你还没听见响，事情就已经结束了。",
        DamageMin = 40,          // 拟定待调
        DamageMax = 70,          // 拟定待调
        Penetration = 0.70,
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪托——拿五十倍镜的枪去砸门，光荣但笨拙
        TwoHanded = true,        // 拟定待调
        IsRanged = true,
        AmmoKey = AmmoKeys.LongBullet,   // 狙击枪：长子弹，1 发/次。1 个零件只出 2 发 —— 全表最贵的一发。一颗子弹一条命，前提是你打得中。
        NoiseRadius = 700,           // 拟定待调：**全表最响**。大口径、高初速——它的 flavor 说「你还没听见响事情就结束了」，那是对**目标**而言；对周围一公里的所有东西来说，它响得像开了个宴会
        BaseSpreadDegrees = 0.5, // 拟定待调
        AttackInterval = 5.0,    // 拟定待调（枪械倍率回调 ×3~4：狙击 1.5→×3.33；栓动猎枪 4.5 接手全表最慢枪之一）
        MaxRange = 900,          // 拟定待调（狙击：远而缓，末端仍高伤）
        FalloffStart = 450,      // 拟定待调
        FalloffFloor = 0.8,      // 拟定待调
        // 枪托近战（6.0kg，**全表最重最长**）：单击最痛、抡得最慢、砸下去最响。DPS 2.84（仍低于匕首 2.86）。
        StockMeleeDamageMin = 8,
        StockMeleeDamageMax = 13,
        StockMeleePenetration = 0.03,
        StockMeleeInterval = 3.7,       // 与尖头锤 3.7 同档——你抡的是一根 6kg 的铁管
        StockMeleeNoiseRadius = 125,
    };

    /// <summary>
    /// 自制霰弹枪（用户拍板新增）：**全表唯一的多弹丸武器**——原话「霰弹枪采用 8 颗弹丸单独计算的 10% 穿透力武器，
    /// 射程较短，伤害衰减严重，锥形扩散较大」。
    ///
    /// <para><b>8 颗弹丸「单独计算」</b>（<see cref="Weapon.PelletCount"/>=8）：每颗<b>各自</b>选命中部位、
    /// <b>各自</b>过护甲三段判定、<b>各自</b>结算伤害与效果——一枪可同时中头、胸、左臂，且每颗各自被挡下/半伤/全伤。
    /// 见 <see cref="CombatResolver.ResolveVolley"/>（引擎侧）与 Godot 侧的 N 枚独立 Projectile（空间侧）。</para>
    ///
    /// <para><b>定位：清丧尸的武器，不是打劫掠者的武器</b>（有意为之，别当平衡问题"修"成万金油）。
    /// 数学根源——挡下门槛 = 护甲值×(1−穿透)/2，穿透仅 10% 故门槛几乎不被削：
    /// 腐皮 1.35 / 布衣 2.7 / 皮夹克 5.4 / <b>板甲 22.5</b>，而单颗弹丸只有 1~5。
    /// ⇒ 对无甲/薄衣（丧尸：只穿布衣，且头/手/脚恒裸）弹丸几乎全进肉；对板甲则颗颗被弹开。
    /// 与<b>自制猎枪</b>（4~16 / 穿透 25%，钻甲）构成互补的两把自制枪：<b>猎枪钻甲、霰弹清群</b>。</para>
    ///
    /// <para><b>数值理由</b>（全部拟定待调）：
    /// ①单颗 <b>1~5</b>——上限必须显著低于板甲门槛 22.5（对甲极差）、下限必须低于布衣门槛 2.7（布甲才挡得下"相当一部分"）；
    /// 8 颗合计期望 24（贴脸满命中），略高于步枪单发 27.5 的量级但吃满衰减/扩散惩罚。
    /// ②冷却 <b>4.0s</b>——土制单管、每发都要重新装填；贴脸 DPS≈6，介于手枪(5.5)与步枪(9.8)之间，不越级。
    /// ③射程 <b>90</b>（全表最短，＜自制猎枪 130）、满伤段仅 <b>18</b>、末端衰减 <b>0.2</b>（全表最重，＜自制猎枪 0.35）——
    /// "伤害衰减严重"。④扩散 <b>±18°</b>（全表最大，＞自制猎枪 8°）——"锥形扩散较大"，远距离弹丸铺开到打不中人。
    /// ⑤锐伤：铅丸破皮撕裂，与其余枪械同类型（转钝逻辑照旧）。</para>
    /// </summary>
    public static Weapon ImprovisedShotgun() => new()
    {
        Name = "自制霰弹枪",
        Description = "钢管、铁钉、一把火药——离得越近，讲的道理越充分。",
        DamageMin = 1,           // 拟定待调（单颗弹丸；低于布衣挡下门槛 2.7 → 布甲才挡得下一部分）
        DamageMax = 5,           // 拟定待调（单颗弹丸；远低于板甲门槛 22.5 → 对板甲颗颗被弹开）
        PelletCount = 8,         // 用户拍板：8 颗弹丸，单独计算
        Penetration = 0.10,      // 用户拍板：10% 穿透（低）
        // 弹药：霰弹专用壳。一次扣扳机烧掉**一发霰弹**（不是 8 发）——8 颗弹丸装在同一个壳里。
        AmmoKey = AmmoKeys.Buckshot,   // 自制霰弹枪：鹿弹。**8 颗弹丸只扣 1 发**（弹丸在同一发壳里）。1 个零件出 4 发——贴脸之王，但一枪下去心在滴血。
        NoiseRadius = 550,           // 拟定待调：大药量、敞口枪管，土制结构毫无消音可言
        DamageType = DamageType.Sharp,
        StructureFactor = 1.0,   // 砸墙系数：作用于枪托，不是弹丸（8 颗小弹丸打墙 = 一把沙子）
        TwoHanded = true,        // 拟定待调（长管双手抵肩）
        IsRanged = true,
        BaseSpreadDegrees = 18,  // 拟定待调（全表最大锥角——"锥形扩散较大"）
        AttackInterval = 4.0,    // 拟定待调（土制单管，每发重新装填）
        MaxRange = 90,           // 拟定待调（全表最短——"射程较短"）
        FalloffStart = 18,       // 拟定待调（出膛即开始掉，满伤只在贴脸）
        FalloffFloor = 0.2,      // 拟定待调（全表最重衰减——"伤害衰减严重"）
        // 枪托近战（3.2kg，敞口钢管）：一根管子，抡起来和自制猎枪同档。DPS 2.80。
        StockMeleeDamageMin = 5,
        StockMeleeDamageMax = 9,
        StockMeleePenetration = 0.02,
        StockMeleeInterval = 2.5,
        StockMeleeNoiseRadius = 105,
    };

    // ==================== 弓弩（8 把：5 弓 + 3 弩） ====================
    //
    // 用户拍板：「弓可以分为：短弓/反曲弓/长弓/竞技复合弓（不可制作）/狩猎弓（不可制作）」
    //           「弩和弓共用弹药类型，可分为：单手轻弩/双手重弩/复合弩（不可制作）」
    //
    // 全表共性（8 把一个不落）：
    //  ① **远程锐器**——护甲表「挡锐器」列写的就是"刀/箭"。
    //  ② **伤害下限恒为 1**（近战锐器通则，不同于枪械）：箭是「飞出去的刀」，初速比子弹低一个数量级，
    //     靠锐利切入而非动能贯穿，斜面掠射（擦过划开一道口子）是常态。故适用用户的「刀可以轻划一下」，
    //     而**不**适用枪械的「子弹没有擦破皮，打中就是打中」（对照：自制猎枪下限 4，不降到 1）。
    //     1~上限 的极大方差正是「要么擦过、要么扎穿」的手感。——此三条推理由 impl-bow 提出，此处沿用。
    //  ③ **无枪托近战 profile**：所有枪贴脸都能砸，弓弩不能——被摸到就只能挨打。这是远程潜行武器该付的代价，
    //     是设计，不是遗漏。
    //  ④ **末端衰减浅**（FalloffFloor ≥ 0.65 ＞ 自制猎枪 0.45）：箭靠质量+锐利，不像弹丸那样被空气阻力
    //     迅速吃掉动能。
    //  ⑤ **吃箭矢**（AmmoKey = AmmoKeys.Arrow，1 支/次）。箭**不吃火药**（枪弹要火药→要燃料，与火把/发电竞争）
    //     且**能捡回来一些**（<see cref="Archery.ArrowRecoveryRate"/>：基础 25%，读过《弓与箭之道》后 50%）——
    //     这是弓弩相对枪唯一实打实的优势：打不动，但打得起、打得久。
    //     ⚠ 但 25% 意味着射四支只捡回一支：**弓弩不是免费远程**，只是后勤压力小于枪。
    //  ⑥ **DPS 全部低于步枪**（连"最强弓×最好箭"也不例外，见 ArcheryTests）：弓弩的价值在潜行与后勤，不在输出。
    //
    // **箭会反过来改写这里的数值**——最终属性 = 本表基础属性 ⊗ 箭的乘法修正（见 <see cref="Archery.Combine"/>）。
    // 故本表的每一个数字都是「搭自制箭（基线）时的值」。
    //
    // **弓 vs 弩不是换皮**：弩靠机械储能 → 穿透高、上弦慢（平均冷却更长），且能做出唯一的单手远程弓弩；
    // 弓靠人力 → 出手快、射程远。各自的生态位见每把武器的注释。数值全部「拟定待调」。

    /// <summary>
    /// 短弓：**入门弓**，全部弓里最弱的一把——但最便宜（木料 2 + 绳 1）、出手最快（3.2s）。
    /// <para>
    /// <b>它承接 <c>RecipeBook</c> 的 <c>handmade_bow</c> 配方</b>（原名「自制弓」）。那条配方早于弓弩体系存在，
    /// 产物经 <c>CraftOutputFactory</c> 落地为 <c>Item.Weapon(配方显示名)</c>，却**查不到任何武器数值**＝悬空引用。
    /// 用户拍板的 5 把弓里没有「自制弓」这个名字，保留它就是凭空多出第 6 把弓；而「木料 2 + 绳 1」＝
    /// 一根木头 + 一根弦，本来就是最朴素的短弓。故配方 <b>DisplayName 改为「短弓」</b>（Id/OutputKey 仍是
    /// <c>handmade_bow</c>，内部键不动），悬空引用由本工厂接上。
    /// </para>
    /// </summary>
    public static Weapon ShortBow() => new()
    {
        Name = "短弓",
        Description = "一根木头，一根弦。射程短、力道小，但它安静——安静到你能听清，第二只丧尸是从哪个方向过来的。",
        DamageMin = 1,           // 拟定待调（锐器通则：箭能"擦过"）
        DamageMax = 18,          // 拟定待调（全弓最低——它是应急/开局货）
        Penetration = 0.30,      // 拟定待调
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：**全表最低档**——射箭砸墙是全游戏最徒劳的行为
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 70,            // 拟定待调：弓弦震颤 + 箭破空——**有声，但传不出丧尸的嗅觉半径(70px)**：听得见你放箭的丧尸，本来就已经闻得到你
        BaseSpreadDegrees = 5,   // 拟定待调（土法削出来的，不准）
        AttackInterval = 3.2,    // 拟定待调（弓臂软 → 拉得快，全弓弩最快）
        MaxRange = 220,          // 拟定待调（最近）
        FalloffStart = 90,       // 拟定待调
        FalloffFloor = 0.70,     // 拟定待调
        // 无 StockMelee*：弓没有枪托（共性③）。
    };

    /// <summary>
    /// 反曲弓：**标准均衡款**，可制作。弓臂末端反着弯回去，同样的长度多出几分力道——
    /// 读表时它是弓的"中位数"（伤害/穿透/冷却/射程都在短弓与长弓之间）。
    /// </summary>
    public static Weapon RecurveBow() => new()
    {
        Name = "反曲弓",
        Description = "弓臂末端反着弯回去，同样长度多出几分力道。做弓的人懂物理，用弓的人只需要懂呼吸。",
        DamageMin = 1,
        DamageMax = 26,          // 拟定待调
        Penetration = 0.40,      // 拟定待调
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 70,            // 拟定待调：同短弓：弓弦回弹更快更脆，但仍在嗅觉半径内
        BaseSpreadDegrees = 4,   // 拟定待调
        AttackInterval = 3.8,    // 拟定待调
        MaxRange = 300,          // 拟定待调
        FalloffStart = 120,      // 拟定待调
        FalloffFloor = 0.72,     // 拟定待调
    };

    /// <summary>
    /// 长弓：**射程之王**（480＝全弓弩最远，仅次于步枪 550 / 狙击 900 的军用远程），可制作。
    /// 代价是拉满费时（5.0s，全弓第二慢）与体型（要多一倍的木料）。生态位＝**站得远，一箭一箭点**。
    /// </summary>
    public static Weapon Longbow() => new()
    {
        Name = "长弓",
        Description = "比人还高的长弓。它能把箭送到很远的地方——远到你射完还有充裕的时间，看清自己是不是射偏了。",
        DamageMin = 1,
        DamageMax = 34,          // 拟定待调
        Penetration = 0.50,      // 拟定待调
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 70,            // 拟定待调：长弓弦震颤最沉，但仍压在嗅觉半径内——拉得越满，声音越像一声叹息
        BaseSpreadDegrees = 4.5, // 拟定待调（长弓无瞄具，纯靠手感）
        AttackInterval = 5.0,    // 拟定待调（拉满一张长弓要力气）
        MaxRange = 480,          // 拟定待调（**射程之王**）
        FalloffStart = 200,      // 拟定待调
        FalloffFloor = 0.78,     // 拟定待调
    };

    /// <summary>
    /// 竞技复合弓：**精度之王**（误差角 1.5°，比军用步枪的 2° 还准），且滑轮省力 → 出手快（3.0s）。
    /// <b>不可制作，只能搜刮</b>（超市运动区）。生态位＝**精确点名**：打得准、打得快，但伤害/穿透只是中上。
    /// </summary>
    public static Weapon CompetitionCompoundBow() => new()
    {
        Name = "竞技复合弓",
        Description = "滑轮、准星、稳定杆，一整套让人百发百中的精密玩意。它原来的主人拿它拿过奖——奖杯还在，人不在了。",
        DamageMin = 1,
        DamageMax = 30,          // 拟定待调
        Penetration = 0.48,      // 拟定待调
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 65,            // 拟定待调：滑轮组吸收了大半弓弦震动，是**最安静的弓**（现代工艺的好处）
        BaseSpreadDegrees = 1.5, // 拟定待调（**精度之王**：＜步枪 2°）
        // 拟定待调（Sim 重标定 3.0 → 3.6）：初版 3.0s 让它的裸 DPS（5.17）**反超了「伤害之王」狩猎弓（4.64）**，
        // 于是它在每一列对决里都压着狩猎弓——"伤害之王"这个生态位就名不副实了。
        // 竞技弓的卖点是**打得准**，不是打得快。放缓到 3.6s 后 DPS 4.31 ＜ 狩猎弓 4.64，两把各归其位。
        AttackInterval = 3.6,
        MaxRange = 380,          // 拟定待调
        FalloffStart = 170,      // 拟定待调
        FalloffFloor = 0.75,     // 拟定待调
    };

    /// <summary>
    /// 狩猎弓：**伤害之王**（上限 38＝全弓弩最高），穿透也是弓里最高的 55%。
    /// <b>不可制作，只能搜刮</b>（守林人小屋 / 河边小屋枪柜）。生态位＝**一箭放倒大家伙**：慢、重、狠。
    /// </summary>
    public static Weapon HuntingBow() => new()
    {
        Name = "狩猎弓",
        Description = "货真价实的猎弓，专为放倒大型动物设计。设计它的时候没人想过，有一天最大的猎物会是邻居。",
        DamageMin = 1,
        DamageMax = 38,          // 拟定待调（**伤害之王**）
        Penetration = 0.55,      // 拟定待调（全弓最高）
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档（伤害之王也砸不动一堵栅栏）
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 70,            // 拟定待调：专为狩猎调校，弦音已尽力压低——猎物听见就没得吃了
        BaseSpreadDegrees = 3,   // 拟定待调（有瞄具，但弓臂硬、后坐感强）
        AttackInterval = 4.2,    // 拟定待调
        MaxRange = 420,          // 拟定待调
        FalloffStart = 180,      // 拟定待调
        FalloffFloor = 0.80,     // 拟定待调（重箭簇，末端仍然有劲）
    };

    /// <summary>
    /// 单手轻弩：**唯一的单手弓弩，可双持**，可制作。扣扳机像开枪一样容易（不需要一直拉着弦），
    /// 麻烦的是打完之后——上弦要两只手，还要时间。
    /// <para>
    /// 生态位＝**全表最弱的远程**（伤害 16 / 射程 180），但它是唯一能腾出另一只手的弓弩
    /// （配匕首 / 火把 / 另一把轻弩）。双持轻弩＝先射两发，然后就是漫长的绝望。
    /// </para>
    /// </summary>
    public static Weapon LightCrossbow() => new()
    {
        Name = "单手轻弩",
        Description = "单手就能端的小弩，扣下扳机跟开枪一样容易。麻烦的是开完这一发——上弦得用两只手，还得用点时间。",
        DamageMin = 1,
        DamageMax = 16,          // 拟定待调（全弓弩最低）
        Penetration = 0.35,      // 拟定待调（弩机储能，仍高于同档的短弓 30%）
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档
        TwoHanded = false,       // **唯一单手**
        CanDualWield = true,     // 拟定待调（双持＝两发之后全是绝望）
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 55,            // 拟定待调：机括释放比弓弦更闷、行程更短——**弩比弓安静**
        BaseSpreadDegrees = 3,   // 拟定待调（弩托稳，比同档短弓 5° 准得多）
        AttackInterval = 4.0,    // 拟定待调（上弦慢——弩的通病）
        MaxRange = 180,          // 拟定待调（弩臂短，最近）
        FalloffStart = 70,       // 拟定待调
        FalloffFloor = 0.65,     // 拟定待调
    };

    /// <summary>
    /// 双手重弩：穿透 65%（仅次于复合弩 68% 与狙击枪 70%），冷却 6.0s ＝ **全武器表最慢**。
    /// 可制作（贵：木料 4 + 金属锭 2 + 绳 2 + 机械零件 2）。
    /// 生态位＝**破甲重炮**：能钉穿铁皮，代价是每分钟只能得罪一个人。搭重头箭时穿透 94%，是唯一能可靠打穿板甲的可制作远程。
    /// </summary>
    public static Weapon HeavyCrossbow() => new()
    {
        Name = "双手重弩",
        Description = "沉得像块砖的重弩，弦硬到要用脚踩着上。它能钉穿铁皮，代价是你每分钟只能得罪一个人。",
        DamageMin = 1,
        DamageMax = 36,          // 拟定待调
        Penetration = 0.65,      // 拟定待调（机械储能：可制作里最高）
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 55,            // 拟定待调：重弩弦粗但行程短，仍闷于长弓
        BaseSpreadDegrees = 2.5, // 拟定待调
        AttackInterval = 6.0,    // 拟定待调（**全表最慢**：脚踩上弦）
        MaxRange = 320,          // 拟定待调
        FalloffStart = 140,      // 拟定待调
        FalloffFloor = 0.75,     // 拟定待调
    };

    /// <summary>
    /// 复合弩：弩的天花板——**破甲之王**（68%，全弓弩最高），又准（1.8°）又不算慢（4.5s）。
    /// <b>不可制作，只能搜刮</b>（金手指帮根据地）。
    /// <para>
    /// ⚠ 它 × 重头箭（穿透 ×1.45）裸乘会冲到 98.6% ＝ 护甲系统对它形同不存在，
    /// 故 <see cref="Archery.MaxPenetration"/> 把穿透封在 95%。这是全游戏破甲的绝对上限，
    /// 而它需要一把搜刮来的稀有弩 + 一支手工重箭才凑得出来。
    /// </para>
    /// </summary>
    public static Weapon CompoundCrossbow() => new()
    {
        Name = "复合弩",
        Description = "带滑轮的复合弩，省力、精准、致命——工业文明留给猎人的最后一份好意。",
        DamageMin = 1,
        DamageMax = 34,          // 拟定待调
        Penetration = 0.68,      // 拟定待调（**破甲之王**：＞重弩 65%，＜狙击 70%）
        DamageType = DamageType.Sharp,
        StructureFactor = 0.1,   // 砸墙系数：同全弓弩最低档
        TwoHanded = true,
        IsRanged = true,
        AmmoKey = AmmoKeys.Arrow,
        NoiseRadius = 50,            // 拟定待调：滑轮＋机括双重吸震，**全表最安静的远程武器**——它的代价在冷却与稀缺
        BaseSpreadDegrees = 1.8, // 拟定待调（＞竞技复合弓 1.5°，仍是全表第二准）
        AttackInterval = 4.5,    // 拟定待调（滑轮省力，比重弩 6.0 快得多）
        MaxRange = 400,          // 拟定待调
        FalloffStart = 180,      // 拟定待调
        FalloffFloor = 0.80,     // 拟定待调
    };

    /// <summary>弓弩全集（8 把：5 弓 + 3 弩）。<see cref="Archery"/> 的组合修正与 Sim 生态位校准按此遍历。</summary>
    public static IReadOnlyList<Weapon> ArcheryArsenal() => new[]
    {
        ShortBow(), RecurveBow(), Longbow(), CompetitionCompoundBow(), HuntingBow(),
        LightCrossbow(), HeavyCrossbow(), CompoundCrossbow(),
    };

    // ---- 天生武器 ----

    /// <summary>丧尸爪击：近战锐器，穿透 5%（用户拍板：锐器/0.05，沿用旧 Sim 值，非 Godot 钝器值）。名沿用"爪击"。
    /// 批次18 <b>用户手定 3~9 → 1~5</b>（平均 6 → 3，砍半；不是锐器下移公式的结果——那会得出 1~7）。
    /// 这一手是全局平衡的关键：玩家锐器被砍 43% 的同时丧尸也被砍半，故 PvE 锚点非但没崩，反而 90.2% → 92.6%。</summary>
    public static Weapon ZombieClaw() => new()
    {
        Name = "爪击",
        Description = "腐烂的指甲，钝、脏、带菌，被挠一下够你担惊受怕好几天。",
        DamageMin = 1,           // 用户手定（批次18：3 → 1）
        DamageMax = 5,           // 用户手定（批次18：9 → 5）
        Penetration = 0.05,
        DamageType = DamageType.Sharp,
        StructureFactor = 2.5,   // 砸墙系数「撕扯」：丧尸砸墙不是用爪尖划，是整只扑上去撞、扒、咬——故显式高于锐器兜底（爪击均值 3 × 2.5 = 每爪 7.5）
        NoiseRadius = 100,           // 拟定待调：撕抓与嘶吼。⚠ 只会引来与丧尸敌对的 AI（劫掠者），引不来其他丧尸（同阵营不互相吸引，见回报）
        // 拟定待调（全局慢节奏必须含敌方，否则玩家武器单方放慢→人vs丧尸 91%→57%）：
        // 爪击 1.3→2.3（×1.77），Sim 复验人vs丧尸恢复 ~90%≈旧 91%（保持平衡而非改难度；
        // ×2→2.6 会让丧尸偏易 94%）。⚠ 注意 Godot 运行时丧尸冷却硬编码在 Zombie.cs(1.3)、不读此值，见回报决策项。
        AttackInterval = 2.3,
    };

    /// <summary>
    /// 布鲁斯（狗）撕咬：天生近战锐器，穿透 10%。**极低伤害**（用户口径：布鲁斯难以独自击杀敌人，靠缠斗拖住给道格
    /// 创造输出窗口）。天生武器（同爪击不入 <see cref="Arsenal"/>，玩家不可穿脱）。
    /// 校准依据（param-calibration，dogcal 4000 种子）：三身份特质（高闪避+低伤+快咬）全拉满 → solo 胜率 84%（远超目标 30~45%）；
    /// 设计定义以「高闪避+低伤」为核心对，「咬比爪快」为最弱 flavor 故让出——咬合放缓到 2.3s（=爪击同速）、伤害压到 1-4，
    /// 配 <c>Dog.DodgeChance</c>=0.45 → solo 胜率 39%（band 中位）、平均时长 33s（≫匕首基线 14.5s，缠斗感成立）。
    /// </summary>
    public static Weapon DogBite() => new()
    {
        Name = "撕咬",
        Description = "一口尖牙，咬住了就不松口——布鲁斯拖住敌人，剩下的交给道格。",
        DamageMin = 1,           // 校准：低伤→极低伤（难独自击杀），2→1
        DamageMax = 4,           // 校准：6→4
        Penetration = 0.10,      // 拟定待调
        DamageType = DamageType.Sharp,
        StructureFactor = 2.5,   // 砸墙系数「撕扯」：同爪击口径（牙齿撕扯死物）。布鲁斯无破防 AI，实战不会触发——填在此只为一条规则管全表，不开特例
        NoiseRadius = 90,            // 拟定待调：犬类缠斗的咆哮与撕扯——派布鲁斯去缠住敌人是要付出动静代价的
        AttackInterval = 2.3,    // 校准：1.6→2.3（=爪击同速，让出"咬比爪快"以把 solo 胜率压进 30~45%）
    };

    /// <summary>
    /// 拳脚：人的天生武器 —— <b>空手近战</b>（用户拍板：「空手和持弓近战都视作空手近战，造成钝伤」）。
    /// 拳打脚踢：钝伤、低伤害、快冷却、噪音全表最小、破甲为零。规则见 <see cref="Unarmed.MeleeFor"/>。
    /// <para>
    /// 同爪击/撕咬，是<b>天生武器</b>：<b>不入 <see cref="Arsenal"/></b>（不可穿脱、不进库存、不参与 Sim 遍历）
    /// ⇒ 既有 Sim 基线结构性零漂移。
    /// </para>
    /// <para>
    /// 数值拟定待调，梯度锚在既有表上：伤害 1~4（均值 2.5，低于全表最弱武器匕首 1~7 的 4.0，
    /// 与狗咬 1~4 同档——赤手打人本就不该比一把刀更行）；穿透 0（全表唯一——皮夹克都能让拳头变成挠痒痒，
    /// 破甲是<b>武器</b>的特权）；冷却 1.2s（全表最快，快过匕首 1.4s——拳头没有重量）；
    /// 噪音 60（低于匕首 90、低于弓 70、高于走路 40——空手扭打是最安静的<b>攻击</b>，但绝不是哑剧）。
    /// </para>
    /// </summary>
    public static Weapon Fists() => new()
    {
        Name = "拳脚",
        Description = "你还有一双手。它们打不穿任何东西，但至少能让扑上来的那位知道你还没打算躺下。",
        DamageMin = 1,           // 拟定待调
        DamageMax = 4,           // 拟定待调（均值 2.5 < 匕首 4.0）
        Penetration = 0,         // 赤手无破甲——全表唯一的 0
        DamageType = DamageType.Blunt,   // 用户拍板：空手 = 钝伤
        StructureFactor = 0.2,   // 砸墙系数：徒手拆墙近乎徒劳（只比射箭砸墙的 0.1 强一点）
        NoiseRadius = 60,        // 拟定待调：全表最小的攻击噪音（< 匕首 90、< 弓 70）
        AttackInterval = 1.2,    // 拟定待调：全表最快（< 匕首 1.4）
    };

    /// <summary>
    /// 玩家/敌方可用武器全集（24 种：9 近战 + 7 枪 + 8 弓弩，不含天生的丧尸爪击/撕咬/拳脚），Sim 聚合模拟按此顺序遍历。
    /// 顺序与旧 Sim 行内武器表一致，便于对照基线；新武器一律**追加在末尾**，不插队——既有行的 Sim 数字不受影响
    /// （Sim 按 <c>cell.Idx</c> 派生随机种子，插队会打乱其后所有武器的随机流 ⇒ 基线漂移）。
    /// <para>
    /// 唯一的例外是第 16 位：原「自制弓」<b>原地</b>换成 <see cref="ShortBow"/>（同一把武器改名 + 重标定，
    /// 见 <see cref="ShortBow"/> 注释）。原地替换而非移位，正是为了不打乱其后 <see cref="ImprovisedShotgun"/> 的随机流。
    /// 其余 7 把弓弩全部追加在末尾。
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
        ImprovisedHuntingGun(), Pistol(), Smg(), Rifle(), BoltActionHuntingRifle(), SniperRifle(),
        ShortBow(), ImprovisedShotgun(),
        RecurveBow(), Longbow(), CompetitionCompoundBow(), HuntingBow(),
        LightCrossbow(), HeavyCrossbow(), CompoundCrossbow(),
    };

    // ---- 玩家可见风味文案（黑色幽默）：武器名 → 一行描述 ----
    // 由库存物品 UI（StashPanel/CharacterPanel）经 Item.Weapon 自动填充展示，不参与战斗结算。
    // 覆盖 Arsenal 全表 + 天生武器（爪击/撕咬）+ 无 Weapon 工厂的可制作武器（骨刀/自制弓）。

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
        // 可制作武器（无 Weapon 工厂，产物走 CraftOutputFactory）：
        // 「自制弓」原先也在这里硬编码——因为它有配方却没有 Weapon 工厂。现已补上 HandmadeBow() 并入 Arsenal，
        // 其描述由上面的 foreach 自动收录（单一真源），故此处删去，避免两份文案漂移。
        d["骨刀"] = "削骨磨出的刀，粗糙、发黄，但捅进去一样疼。";
        return d;
    }

    /// <summary>按武器显示名取一行风味描述（查不到返回空串）。供消费层 Item.Weapon 自动填充库存物品描述。</summary>
    public static string DescriptionOf(string name)
        => name != null && _flavorByName.TryGetValue(name, out var d) ? d : "";
}
