using System;
using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 GoldfingerDiscovery.cs / CorpseLoot.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测；
//  **同时被 DeadSignal.Sim 以 Link 方式编入校准 harness** —— 编制表必须是单一事实源：
//  Sim 算出来的胜率与代价，跟玩家真正会碰上的那 8 个人，读的是同一张表。抄一份到 Sim 里就会漂移。）

/// <summary>金手指帮守备的持械档位。<b>他们是人，不是丧尸</b>——持械 ⇒ 杀了能扒（<see cref="CorpseLoot"/>）。</summary>
public enum GangArm
{
    /// <summary>匕首（全表最弱近战；劫掠者的常规手牌）。</summary>
    Dagger,

    /// <summary>短剑（比匕首像样，但离长剑/重剑还差得远）。</summary>
    Shortsword,

    /// <summary>手枪（掉下来是空的——敌方没有弹匣模型，子弹得自己找）。</summary>
    Pistol,
}

/// <summary>
/// 一名守备的<b>伤情</b>（"刚经历完异常战斗，状态都不是巅峰"的机械表达）。
/// <para>
/// 只用两条<b>引擎已有、且运行时与 Sim 都真消费</b>的轴，不发明新机制：
/// <list type="bullet">
/// <item><b>部位 HP 缺口</b>（<see cref="Body.ApplyDamage"/>）—— 挨打余量变小。扣在<b>致死池</b>（胸/腹）上才真正
/// "更容易被打死"：四肢归零只致残、不致死，光扣四肢等于没伤。</item>
/// <item><b>骨折</b>（<see cref="Body.MarkFractured"/>）—— 手骨折 ×0.70 操作（出手更慢、更不准），
/// 腿骨折 ×0.70 移动。运行时在 <c>Actor</c>（移动 / 攻击间隔）消费，Sim 在 <c>Arena</c>/<c>DuelEngine</c> 消费。</item>
/// </list>
/// </para>
/// <para>
/// 🔴 <b>刻意没用「预扣失血」那条轴</b>，尽管 <see cref="Body.LoseBlood"/> 就在手边——两个硬理由：
/// ① <see cref="Body.SetBloodMax"/> <b>会把血回满</b>，而 Sim 的 <c>Arena.MakeUnit</c>/<c>DuelEngine</c> 恰恰在
/// 身体工厂**之后**调它 ⇒ 预扣的失血会被静默擦掉，Sim 里根本传不进去；
/// ② 失血分级的攻速惩罚（<see cref="BloodLossTier"/> Mild/Moderate）<b>只有 Sim 消费</b>，
/// 运行时的 <c>Actor</c> 只把 BloodRatio 拿去画血条 ⇒ 用它会让 Sim 与实机对不上账。
/// 两条轴都躲开这个坑：<see cref="Body.ApplyDamage"/> 与 <see cref="Body.MarkFractured"/> 不被 SetBloodMax 影响。
/// </para>
/// <para>⚠️ 具体数值<b>拟定待调</b>：语义锚点是「不是巅峰」，<b>不是</b>「半死不活」——最重的那两个仍有一半胸腔血量，
/// 还打得动人。别把他们调成白送。</para>
/// </summary>
/// <param name="Name">档位名（战报/调试用）。</param>
/// <param name="PartDamage">部位 → 预先扣掉的 HP。</param>
/// <param name="Fractures">已骨折的部位（未治疗档，×0.70）。</param>
public readonly record struct GangInjury(
    string Name,
    IReadOnlyDictionary<string, double> PartDamage,
    IReadOnlyList<string> Fractures);

/// <summary>编制表里的一名守备：叫什么、拿什么、伤成什么样、站不站岗。</summary>
/// <param name="DisplayName">战斗日志 / 尸体容器名（<see cref="CorpseNaming"/> 会自动加序号防蒸发）。</param>
/// <param name="Arm">持械（⇒ 战利品）。</param>
/// <param name="Injury">伤情。</param>
/// <param name="IsSentry">true = 钉在岗位上扫视（<c>Raider.ConfigureSentry</c>）；false = 在据点里游荡。</param>
public sealed record GangGuard(string DisplayName, GangArm Arm, GangInjury Injury, bool IsSentry);

/// <summary>
/// 金手指帮根据地的<b>守备编制表</b>（authored 配置；空间布点在 <c>TestExploration.SpawnGoldfingerGuards</c>）。
///
/// <para><b>他们是人。</b>此前代码里这 8 个"守备"生成的是<b>丧尸</b>（<c>SpawnZombieAt</c>）——而丧尸不持械、
/// 掉不出武器，于是"打赢金手指帮"这条本该是玩家最重要装备通道的路，<b>一把枪都捡不到</b>。
/// 用户澄清：「金手指帮是人，不是丧尸，不过他们刚经历完异常战斗，大家的状态都不是巅峰。」</para>
///
/// <para><b>「状态不是巅峰」同时解释了两件事</b>（这不是平衡补丁，是设定）：
/// ① 为什么玩家打得过 8 个持械的活人；② 为什么一个守着军火的帮派没能守住自己的军火。</para>
/// </summary>
public static class GoldfingerGang
{
    /// <summary>守备的统一显示名。<b>没有具名头目</b>——帮主哥顿在玩家到达前就已上吊（§3），
    /// 谁在当家、他们跟谁打的那一场，都是 <b>authored 前史，待用户手写</b>，代码不替用户编人物。</summary>
    public const string GuardName = "金手指帮守备";

    // ── 关卡画布尺寸（脱 Godot 副本）────────────────────────────────────────
    // 🔴 单一事实源是 ExplorationLevelSize.SizeFor("金手指帮根据地")；下面两个 const 只是它的**副本**。
    //    为什么要副本：AlertedBy 的"招怪"是纯几何，Sim 的 GoldfingerCalibration 要算它，而
    //    **ExplorationLevelSize 刻意不被 DeadSignal.Sim Link**（见 DeadSignal.Sim.csproj 注释）——
    //    那条"不链接 ⇒ 结算路径读不到 ⇒ 既有 Sim 战斗基线结构性零漂移"的论证是项目纪律，
    //    不能为了取个画布尺寸就把 ExplorationLevelSize 拖进 Sim、把论证改弱。
    // 🔴 副本靠**测试焊死**，不靠注释提醒：GoldfingerGangTests.画布尺寸与ExplorationLevelSize焊死_改了那边这边必须跟着改。
    //    改画布 ⇒ 必须同步这里 ⇒ 否则当场红。（此前 Sim 里是硬编码 2400,1600，与真源之间**零保障**。）

    /// <summary>本关画布宽（＝<c>ExplorationLevelSize.SizeFor("金手指帮根据地")</c> 的副本，由测试焊死）。
    /// <para>🔴 <b>2400×1600 ＝ 用户裁决 C：金手指维持原尺寸不放大</b>——本关是全项目唯一"authored 招怪红线
    /// （弓/匕首叫醒 0、手枪 2、步枪 5）由布点绝对像素直接决定"的敌营，放大会当场改掉那条红线
    /// （实测：均匀放大到 3200×2200 ⇒ 破甲锤 1→0、冲锋枪 4→2、步枪 5→4、狙击 7→4）。
    /// 用户看过"逆缩放技术上可行且逐字节保住红线"的方案后<b>仍选择维持不放大</b>，此处非遗漏。</para></summary>
    public const double LevelW = 2400.0;

    /// <summary>本关画布高（同上）。</summary>
    public const double LevelH = 1600.0;

    // ── 招怪探针位置（authored 口径）────────────────────────────────────────

    /// <summary>噪音探针 X：<b>中段</b>，玩家推进必经。招怪表（research 文档）与护栏测试都以此为噪音源。</summary>
    public const double NoiseProbeX = 0.55;

    /// <summary>噪音探针 Y：同上。</summary>
    public const double NoiseProbeY = 0.40;

    // ── 伤情三档（拟定待调）────────────────────────────────────────────────
    // 参考量级：胸 MaxHp 20 / 腹 16 / 头 16 / 手臂 21 / 大腿 12（HumanBody）。致死池 = 胸+腹+头。

    /// <summary>轻伤：挂了点彩，致死余量基本还在。</summary>
    public static GangInjury Light { get; } = new(
        "轻伤",
        new Dictionary<string, double> { [HumanBody.Chest] = 4, [HumanBody.LeftArm] = 8 },
        Array.Empty<string>());

    /// <summary>中伤：致死池掉三成，且带一处骨折（出手慢 / 跑不动，二选一）。</summary>
    public static GangInjury ModerateHand { get; } = new(
        "中伤·右手骨折",
        new Dictionary<string, double> { [HumanBody.Chest] = 7, [HumanBody.Abdomen] = 5 },
        new[] { HumanBody.RightHand });

    /// <summary>中伤（腿）：同上，但骨折在腿——追不上人，也逃不掉。</summary>
    public static GangInjury ModerateLeg { get; } = new(
        "中伤·左腿骨折",
        new Dictionary<string, double> { [HumanBody.Chest] = 6, [HumanBody.Abdomen] = 6 },
        new[] { HumanBody.LeftLeg });

    /// <summary>重伤：致死池掉一半、手腿各一处骨折。<b>仍站得住、还打得动</b>——这是"残兵"，不是"待宰"。</summary>
    public static GangInjury Heavy { get; } = new(
        "重伤",
        new Dictionary<string, double>
        {
            [HumanBody.Chest] = 10,
            [HumanBody.Abdomen] = 7,
            [HumanBody.RightArm] = 10,
        },
        new[] { HumanBody.LeftHand, HumanBody.RightLeg });

    /// <summary>
    /// 8 人各拿什么武器（authored 的经济决定，不是平衡参数）。
    /// <para>
    /// 🔴 <b>[T57] 用户拍板改过一次：手枪全撤，改成 4 短剑 + 4 匕首（原案 2 手枪 + 2 短剑 + 4 匕首）。</b>
    /// 起因是这一关被重排到<b>中期</b>（金手指帮不再是终局）。
    /// <para>
    /// ⚠️ <b>[T63] 的复核结论「这一刀砍对了，板不用翻」仍然成立</b>——但它引用的那组数字<b>是错的，已作废重写</b>。
    /// </para>
    /// <para>
    /// 🔴 <b>为什么旧注释的数不能信（2026-07-17 查明·勿再引用）</b>：原文写的「潜行清哨 99.5% / 正面 60.9% / 阵亡 1.35 /
    /// 惊动全据点 0.4%」等，全部出自 <c>991b777</c> 那份 <b>出生即错（born-stale）</b> 的报告——该 commit
    /// <b>同时重写了 <c>WeaponTable.cs</c>（163 行）并重跑报告</b>，而报告是在武器表改完<b>之前</b>生成的、改完之后没再重跑就一起提交
    /// ⇒ <b>那组数字没有任何一个 committed 代码状态产生过</b>（实测：把 991b777 检出到干净 worktree、<c>git clean -xfd</c> 后全新构建，
    /// 它自己的代码跑出 <b>85.3%</b>，而它自己提交的报告写 <b>60.9%</b>）。
    /// <br/>⚠️ <b>不是"后来引擎漂了"</b>：991b777 / 6887fe6 / 151dc8f / 8c5ccdc / 414adee / 816f63f / bd867a8 <b>逐个实测全部 85.3%</b>
    /// ⇒ 其后那批「数值外置·零漂移 A/B + Sim MD5」的声明<b>是对的，不是它们的锅</b>。
    /// </para>
    /// <para>
    /// 🔴 <b>数字一律以 <c>docs/research/2026-07-14-goldfinger-calibration.md</c> 为准，别再往注释里抄裸数字</b>——
    /// 抄一份就会像上面那样悄悄过期，而且过期了没人知道。该报告现由 <c>GoldfingerCalibrationDocTests</c>
    /// <b>焊死在引擎上</b>（报告里的数与实跑对不上就当场红 ⇒ 去重跑报告、并回来复核本段结论）。下面只留<b>结论</b>：
    /// </para>
    /// <list type="bullet">
    /// <item><b>潜行清哨仍是那条可行的路</b>（3 人同持<b>消防斧</b>＝中期玩家口径）：胜率 ≈99.9%，
    /// 但<b>全身而退只有 ≈19%</b>、平均 ≈2.24 处永久残缺 ⇒ 赢了，有人挂彩。<b>这才叫可行。</b></item>
    /// <item>🔴 <b>「正面很贵」仍然成立——但贵在成本面，不在胜率面</b>（§2 通则③：<b>胜率不是成本</b>，用户原话
    /// 「战斗难道不是成本吗」）。逐波推进胜率 ≈85.3% 听着软，可<b>同一格</b>里：平均<b>阵亡 ≈1.07 / 3 人</b>、
    /// <b>永久残缺 ≈2.68</b>、惨胜 96%、<b>全身而退只有 1%</b> ⇒ <b>打赢＝平均赔掉一条人命、外加两三处不可逆残缺。</b>
    /// 拿胜率当"软"的证据会读反这一关。</item>
    /// <item>🔴 <b>代价换了货币，不是消失了</b>：本关<b>骨折恒为 0.00</b>——<b>不是 bug、是 [T57] 那一刀的结构性后果</b>：
    /// 撤掉手枪后守备<b>清一色利器</b>（4 短剑 + 4 匕首，<c>weapons.json</c> 里 dagger/shortsword 的 damageType 皆 <c>Sharp</c>），
    /// 而骨折<b>只由天然钝器触发</b>（<c>Effects.cs</c>：<c>if (nativeBlunt &amp;&amp; dmg > 0)</c>）⇒ <b>他们切你，不打断你的骨头。</b>
    /// 代价于是从「骨折（7 昼夜愈合、占床、不能干活/站岗）」整个换成「<b>永久残缺（不可逆、长不回来）</b>」——<b>更贵，不是更便宜。</b></item>
    /// <item>🔴 <b>红线守住了</b>：「惊动全据点」近战仍≈0%（消防斧 2.2% / 长剑 0.3% / 棍棒 0.0%）。<b>枪一响还是死。</b>
    /// 噪音设计（弓/匕首叫醒 0 人、手枪 2 人、步枪 5 人）<b>一格没动</b>，且现由
    /// <c>GoldfingerGangTests.枪一响还是死_弓与匕首叫醒零人而枪招来一片</c> 钉死（此前全项目没有任何测试钉它）。</item>
    /// <item>⚠️ <b>「原案（2 手枪）」那组 A/B（94.9% / 全退 3% / 2.54 残缺）已删除、未重算也无法重算</b>——它是<b>反事实</b>
    /// （harness 只跑当前 authored 编制），且同出 born-stale 报告 ⇒ <b>只可当历史留痕，不得引用为事实</b>。
    /// 若日后要重做该 A/B，须临时改 Roster 重跑，属独立单。</item>
    /// </list>
    /// <para>
    /// 数在 <c>docs/research/2026-07-14-goldfinger-calibration.md</c>（生成口径：3 人探索队 / 2000 次蒙特卡洛 /
    /// Arena 无空间下界；<b>末次重跑 2026-07-17 @ bd867a8</b>），harness = <c>src/DeadSignal.Sim/GoldfingerCalibration.cs</c>。
    /// </para>
    /// <para>
    /// <b>剧情自洽</b>（这一刀不是"把他们改成病秧子"）：他们仍是"刚打完一场恶战"的残兵——
    /// <b>弹药打光了，空枪扔回枪械台，抄起短剑守着</b>。这比"守着军火库却端着自己的枪"更说得通。
    /// ⇒ 那两把手枪<b>没有消失</b>，它们躺在<c>枪械台</c>和<c>军械柜</c>里（见 <c>ExplorationCache</c>）：
    /// <b>玩家照样捡得到枪</b>，只是从"尸体上扒"变成"柜子里翻"——「中期拿到枪、但打不起」的张力一格不丢
    /// （枪的真实战力由弹药供给决定，而供给在 loot 里）。
    /// </para>
    /// <para>
    /// <b>没给他们好枪</b>：全图唯一的冲锋枪仍锁在他们自己的军械柜里——它是<b>打赢的奖赏</b>，不该长在守备手上。
    /// 8 个人里但凡有一个端着冲锋枪，这仗就从"硬仗"变成"必死局"（<c>GoldfingerGangTests.守备不拿他们自己看守的军火</c> 钉死）。
    /// <b>评估过但否掉的选项</b>：减编制 8→6 —— 布点表 <see cref="Placements"/> 与本表<b>同序同长</b>，减人会当场
    /// <c>IndexOutOfRange</c>，必须连关卡布点一起重排；动的面比减枪大、收益却更小。
    /// </para>
    /// </summary>
    public static IReadOnlyList<GangGuard> Roster { get; } = new[]
    {
        // 深处（军械柜 / 银库 / 头目区）：站岗的在这儿——好东西在他们背后。
        new GangGuard(GuardName, GangArm.Shortsword, ModerateHand, IsSentry: true),
        new GangGuard(GuardName, GangArm.Dagger, Light, IsSentry: false),
        new GangGuard(GuardName, GangArm.Shortsword, Heavy, IsSentry: true),
        // 中段（修械 / 弹药 / 皮件 / 铺位）
        new GangGuard(GuardName, GangArm.Dagger, ModerateLeg, IsSentry: false),
        new GangGuard(GuardName, GangArm.Shortsword, Light, IsSentry: false),
        new GangGuard(GuardName, GangArm.Dagger, Heavy, IsSentry: false),
        // 近入口（前院 / 岗哨）
        new GangGuard(GuardName, GangArm.Dagger, ModerateHand, IsSentry: false),
        new GangGuard(GuardName, GangArm.Shortsword, Light, IsSentry: true),
    };

    /// <summary>
    /// 8 名守备的<b>布点</b>（关卡<b>相对</b>坐标 0~1，与 <see cref="Roster"/> <b>同序</b>）。
    /// 深处/中段加权：多数在关卡上半（y 小＝北，军械柜/银库/头目区所在），少数在中段与近入口 ⇒ "打过才拿"。
    /// <para>
    /// 放在这儿（而不是只写在 <c>TestExploration</c> 里）是因为<b>噪音招怪要算得出来</b>：一枪的半径能罩住几个人，
    /// 是纯几何问题（<see cref="AlertedBy"/>），而 Sim 够不着 Godot 的场景层。布点与编制必须是同一张表——
    /// 否则"算出来的据点"和"打进去的据点"不是一个据点。
    /// </para>
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Posts { get; } = new[]
    {
        (0.78, 0.18), // 深·头目/银库区   ← 哨兵
        (0.90, 0.15), // 深·银库暗格侧
        (0.70, 0.24), // 深·军械柜侧      ← 哨兵
        (0.55, 0.32), // 中深·修械/弹药区
        (0.62, 0.45), // 中·皮件/gauntlet
        (0.38, 0.40), // 中·铺位/油料区
        (0.30, 0.62), // 中前·前院
        (0.50, 0.72), // 近入口·岗哨侧    ← 哨兵
    };

    /// <summary>
    /// <b>在这儿弄出这么大动静，会招来几个人。</b>纯几何：以 <paramref name="atX"/>/<paramref name="atY"/>（相对坐标）
    /// 为心、<paramref name="noiseRadiusPx"/> 为半径，罩住几个 <see cref="Posts"/>。
    /// <para>
    /// 🔴 <b>这是"枪的代价"第一次能被算出来。</b>设计里枪的制衡是<b>噪音</b>（战斗声不分阵营、劫掠者都听得见），
    /// 而 Sim 是无空间模型、根本测不到它 ⇒ 光看 Sim 的胜率表会得出"带枪最稳"的错误结论。
    /// 把它接上以后才看得见：<b>枪打得越狠，一枪叫醒的人越多，而人一多就是必死局。</b>
    /// </para>
    /// <para>⚠️ 只算直线距离，<b>不算墙体遮挡</b>（噪音本就穿墙传，见 NoiseLogic）——但也不算"听见了走过来要多久"，
    /// 所以这是"最终会被惊动的人数"的上界，不是"立刻扑上来的人数"。</para>
    /// </summary>
    public static int AlertedBy(double atX, double atY, double noiseRadiusPx, double levelW, double levelH)
    {
        int n = 0;
        foreach ((double X, double Y) p in Posts)
        {
            double dx = (p.X - atX) * levelW;
            double dy = (p.Y - atY) * levelH;
            if (Math.Sqrt((dx * dx) + (dy * dy)) <= noiseRadiusPx)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>持械档位 → 真武器（走 <see cref="WeaponTable"/> 权威表，不自造数值）。</summary>
    public static Weapon WeaponFor(GangArm arm) => arm switch
    {
        GangArm.Pistol => WeaponTable.Pistol(),
        GangArm.Shortsword => WeaponTable.Shortsword(),
        _ => WeaponTable.Dagger(),
    };

    /// <summary>
    /// 把伤情<b>预置</b>进一具刚造好的身体。运行时（<c>Raider</c> 生成时）与 Sim（身体工厂里）<b>调的是同一个函数</b>。
    /// <para>
    /// 用 <see cref="Body.ApplyDamage"/> 而非直接写 HP：走的是与"真被打了一刀"完全相同的那条路
    /// （含归零后的致残 / 致死判定），所以不会造出"HP 是 0 但人还好好的"这种引擎里不存在的状态。
    /// <b>不登记出血</b>（<c>RegisterBleed</c> 一次没调）—— 否则他们会在玩家赶到之前<b>自己流血流死</b>。
    /// </para>
    /// </summary>
    public static void ApplyInjuries(Body body, GangInjury injury)
    {
        if (body is null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        foreach (KeyValuePair<string, double> hit in injury.PartDamage)
        {
            body.ApplyDamage(hit.Key, hit.Value);
        }

        foreach (string part in injury.Fractures)
        {
            body.MarkFractured(part);
        }
    }

    /// <summary>造一具<b>带伤的</b>守备身体（<see cref="HumanBody"/> 全套部位 + 预置伤情）。Sim 的身体工厂直接用它。</summary>
    public static Body NewInjuredBody(GangInjury injury)
    {
        Body body = HumanBody.NewBody();
        ApplyInjuries(body, injury);
        return body;
    }
}
