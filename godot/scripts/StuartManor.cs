using System;
using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 GoldfingerGang.cs / CorpseLoot.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// ⚠️ 与 GoldfingerGang.cs 的一处**刻意不同**：本文件**不被 DeadSignal.Sim Link**。
//    金手指帮那张表要进 Sim 是因为 Sim 在算"打赢那 8 个残兵要付多少代价"；本关不进 Sim 的结算路径，
//    Sim 也就**根本读不到它** ⇒ 既有武器×护甲基线**结构性零漂移**（这是本单的零漂移证明，不是靠跑一遍对 MD5）。

/// <summary>斯图尔特家族庄园劫掠者的持械档位。<b>给什么武器＝给了什么战利品</b>（<see cref="CorpseLoot"/> 必掉零掷骰）。</summary>
public enum ManorArm
{
    /// <summary>匕首（全表最弱近战；劫掠者的常规手牌）。</summary>
    Dagger,

    /// <summary>短剑。</summary>
    Shortsword,

    /// <summary>棍棒 —— <b>骨折工厂</b>：Sim 实测玩家胜率 96.5%（看着白送），但 <b>66% 的胜场留下骨折</b>（愈合 7 昼夜）。</summary>
    Club,

    /// <summary>尖头锤（钝器，同样打骨折）。</summary>
    SpikeHammer,

    /// <summary>破甲锤 —— 玩家胜率 70.8%、<b>13% 断肢/报废</b>。这一关最贵的一件近战战利品。</summary>
    Warhammer,

    /// <summary>草叉 —— <b>农具</b>。他是顺手从这家人的农具棚里抄的（农庄语境；全图别处拿不到）。</summary>
    Pitchfork,

    /// <summary>手枪 —— 玩家胜率<b>只有 26.2%</b>。全庄园<b>只有一把</b>；掉下来是空的（敌方无弹匣模型，子弹得自己找）。</summary>
    Pistol,
}

/// <summary>
/// 着装档（＝<b>另一半战利品</b>）。用户原话：「这个调查点最富裕的地方是<b>劫掠者们的装备和衣服</b>」——
/// 所以他们身上穿的不是占位，是<b>你要来拿的东西</b>：远远看见那个穿皮甲的，那就是<b>一副皮甲在那儿走着</b>。
/// <para>全部走 <see cref="ArmorTable"/> 权威表、全部在 <see cref="ApparelCatalog"/> 里登记得到（<b>扒下来真穿得上</b>）；
/// <b>不造任何新护甲数值</b>。成对品（鞋/手套）刻意不给——那要按左右侧裁剪覆盖，敌人身上没必要引这层复杂度。</para>
/// </summary>
public enum ManorOutfit
{
    /// <summary>抄家伙的那个：<b>只有一件贴身布衣 + 一条长裤</b>。他连外套都没有——这伙人也不是个个都发财了。</summary>
    Rags,

    /// <summary>常见档：粗布外套 + 长袖布衣 + 短裤。</summary>
    Common,

    /// <summary>布夹克档：布夹克 + 长袖布衣 + 长裤。</summary>
    Jacket,

    /// <summary>牛仔档：牛仔外套 + 长袖布衣 + 长裤。</summary>
    Denim,

    /// <summary>皮夹克档（外套层最强）：皮夹克 + 长袖布衣 + 长裤。</summary>
    Leather,

    /// <summary>
    /// <b>披甲档</b>：皮甲（装甲层）+ 皮夹克 + 长袖布衣 + 长裤 + 军用头盔。
    /// <para>🔴 这是<b>整座庄园最硬的一个人，也是最值钱的一具尸体</b>——一副皮甲 + 一顶军用头盔 + 他手里那把破甲锤。
    /// 他难打<b>不是意外</b>：看得见的价值，才构成决策（<see cref="CorpseLoot"/>）。玩家远远就看得出他值不值得动手。</para>
    /// </summary>
    Armored,
}

/// <summary>编制表里的一个人：叫什么、拿什么、穿什么、站不站岗。</summary>
/// <param name="DisplayName">战斗日志 / 尸体容器名（<see cref="CorpseNaming"/> 会自动加序号防蒸发）。</param>
/// <param name="Arm">持械（⇒ 战利品的一半）。</param>
/// <param name="Outfit">着装（⇒ 战利品的另一半）。</param>
/// <param name="IsSentry">true = 钉在岗位上有规律扫视（<c>Raider.ConfigureSentry</c>）；false = 在庄园里游荡。</param>
public sealed record ManorRaider(string DisplayName, ManorArm Arm, ManorOutfit Outfit, bool IsSentry);

/// <summary>
/// 斯图尔特家族庄园的<b>劫掠者编制表 + 哨位布点</b>（authored 配置；空间铺设在 <c>TestExploration.SetupStuartManor</c>）。
///
/// <para><b>用户原话（authored 唯一事实源，一字不改）</b>：「斯图尔特家族庄园（农庄，并不是很富裕，中地图，
/// 有盘踞的劫掠者和岗哨，高危，高风险不是永远高回报，这个调查点最富裕的地方是劫掠者们的装备和衣服，
/// 并且这里会有斯图尔特家的一些剧情，讲述了他们好心收留一些流浪者，结果被背刺，女儿妻子被奸杀，
/// 男性尸体吊挂在门口喂丧尸，在枯井底有抱着婴儿饿死的女性尸体）」</para>
///
/// <para>🔴 <b>这一关的设计核心是"高风险不是永远高回报"，别把它平衡掉</b>：
/// <list type="bullet">
/// <item><b>农庄本身是穷的</b>（"并不是很富裕"）——10 处搜刮点里<b>一把枪、一本书、一枚白银、一支抗生素都没有</b>
/// （见 <c>ExplorationCache</c>；<c>StuartManorTests</c> 的贫困护栏钉死）。往这儿塞高价值物资<b>正好毁掉这一关的立意</b>。</item>
/// <item><b>回报全长在人身上</b>——「最富裕的地方是劫掠者们的装备和衣服」⇒ 你得<b>先打赢，才能扒</b>。</item>
/// <item><b>而这一身装备是拿伤病换的</b>（<c>docs/research/2026-07-14-combat-cost.md</c>）：持棍棒劫掠者胜率 96.5%
/// 看着白送，<b>66% 的胜场留下骨折</b>（愈合 7 昼夜，占床、不能干活、不能站岗）；持破甲锤 70.8%、<b>13% 断肢</b>；
/// 持手枪<b>只有 26.2%</b>。连场<b>不能拿胜率相乘</b>——不治疗连打，能撑过第 3 个的只剩 <b>3.5%</b>。
/// ⇒ <b>「打赢劫掠者白捡一身装备」这个场景不存在</b>：玩家现实里会清掉两三个就撤，或者干脆绕过去。
/// <b>允许他选择不打</b>（潜行绕过 / 只清边缘 / 撤退）是这一关的正确玩法之一，不是设计失败。</item>
/// </list></para>
///
/// <para><b>他们是健全的人</b>（不同于金手指帮那 8 个"刚经历完异常战斗"的残兵，见 <see cref="GoldfingerGang"/>）——
/// 这里没有 <c>GangInjury</c> 那一列，<b>结构性地</b>不可能给他们打折。高危就该是高危。</para>
/// </summary>
public static class StuartManor
{
    /// <summary>目的地名（＝内部路由键，须与 <c>WorldMapPanel</c> / <c>ExplorationCache.StuartManorName</c> 一致）。</summary>
    public const string DestinationName = "斯图尔特家族庄园";

    /// <summary>
    /// 危险度（<b>用户原话直给「高危」</b>，不是推的）：全图目前唯一的 <see cref="DangerTier.High"/>。
    /// <para><b>单一事实源</b>：<c>WorldMapPanel</c> 的地图行读这里（那个文件带 Godot 依赖、脱不了单测 ⇒
    /// 值放在纯逻辑侧才钉得死，见 <c>StuartManorTests</c>）。</para>
    /// </summary>
    public const DangerTier Danger = DangerTier.High;

    /// <summary>
    /// 劫掠者的统一显示名。
    /// <para>🔴 <b>用户拍板：盘踞在这儿的这伙人，就是当年被斯图尔特家收留、然后背刺他们的那伙流浪者</b>
    /// （用户原文：「是——就是那伙人」）。⇒ <b>玩家杀的就是凶手</b>。</para>
    /// <para>但<b>没有具名头目</b>：他们是谁、叫什么、背刺的经过，仍是 <b>authored 前史，待用户手写</b>——
    /// 代码不替用户编人物，叙事点也<b>不替玩家下判断</b>（不会出现"你为他们复了仇"这种话）。</para>
    /// </summary>
    public const string RaiderName = "庄园劫掠者";

    // ── 关卡尺寸（脱 Godot 副本；🔴 须与 TestExploration 的 LevelW/LevelH 一致）──────────────
    //    放大到中图 ≈3 天量级：与 ExplorationLevelSize.Overrides["斯图尔特家族庄园"]=(3200,2200) 同步。
    //    ⚠️ Posts 已配套绕庭院中心逆缩放（见下）——哨位间/庭院噪音的**像素距离逐字节不变**，噪音几何护栏恒绿。
    /// <summary>关卡宽（px）。</summary>
    public const double LevelW = 3200.0;

    /// <summary>关卡高（px）。</summary>
    public const double LevelH = 2200.0;

    /// <summary>
    /// <b>庭院中央</b>（相对坐标）：晒谷场与主屋之间那片空地——玩家真正会在这儿被迫动手的地方。
    /// 噪音梯度就是照着"从这儿弄出动静"标定的（见 <see cref="AlertedBy"/> 与 <c>StuartManorTests</c>）。
    /// </summary>
    public const double CourtyardX = 0.40;

    /// <summary>庭院中央（相对坐标 Y）。</summary>
    public const double CourtyardY = 0.66;

    // ── 叙事调查点 id（authored 剧情四拍；正文在 NarrativeSpotRegistry，draft 待用户手写）─────
    // 🔴 全部 ascii snake_case ⇒ 与"可扒的战斗尸体"（容器名含中文「的尸体 #」）**命名空间不可能相交**：
    //    门口那两具吊尸、井底那具抱着婴儿的女尸，是**发现点**，不是战利品 —— 它们永远不会被当成尸体去搜。

    /// <summary>① 门口的吊尸（用户原话「男性尸体吊挂在门口喂丧尸」）：一进关就撞见。</summary>
    public const string GateHangedSpotId = "narrative_stuart_gate_hanged";

    /// <summary>② 好心收留流浪者的痕迹 → 被背刺（前史，靠场景说话）。</summary>
    public const string TakenInSpotId = "narrative_stuart_taken_in";

    /// <summary>③ 里屋（用户原话「女儿妻子被奸杀」）：<b>克制、不铺陈施暴细节</b>，只呈现"发现现场"。</summary>
    public const string InnerRoomSpotId = "narrative_stuart_inner_room";

    /// <summary>④ 枯井底（用户原话「在枯井底有抱着婴儿饿死的女性尸体」）：可探查。</summary>
    public const string DryWellSpotId = "narrative_stuart_dry_well";

    /// <summary>
    /// 庄园里的<b>七个人</b>（<b>3 名岗哨</b>——用户原话「有盘踞的劫掠者<b>和岗哨</b>」）。
    ///
    /// <para><b>持械是经济决定，不是随手参数</b>（拟定待调，用户可直接改这张表）：
    /// <list type="bullet">
    /// <item><b>全庄园只有一把枪</b>：持手枪劫掠者＝玩家胜率 <b>26.2%</b>。这不是吝啬，是<b>多给一把就是必死局</b>。
    /// 同时它是"开枪＝叫醒整个庄园"这条机制的<b>唯一枪声来源</b>——他站在主屋前，位置正在庭院噪音半径的中心。</item>
    /// <item><b>没有长枪、没有重剑</b>（冲锋枪/步枪/狙击/霰弹/重剑一把都没有）：一处点位不该一次性抹平武器荒。</item>
    /// <item><b>钝器给了三把</b>（棍棒/尖头锤/破甲锤）：这是本关"惨胜"的主要来源——赢了，但骨头断了。
    /// 「胜率不是成本」在这一关是能被玩家<b>亲身</b>算明白的。</item>
    /// <item><b>草叉</b>：他是从这家人的农具棚里抄的。农庄语境，也是全图唯一能拿到草叉的地方。</item>
    /// </list></para>
    /// </summary>
    public static IReadOnlyList<ManorRaider> Roster { get; } = new[]
    {
        // 大门/晒场（近入口，先撞见）——门口那具吊尸就归他"看着"。
        new ManorRaider(RaiderName, ManorArm.Club, ManorOutfit.Denim, IsSentry: true),
        new ManorRaider(RaiderName, ManorArm.SpikeHammer, ManorOutfit.Jacket, IsSentry: false),
        // 畜栏/谷仓（中段）——谷仓哨兵是**披甲的那个**：全庄园最硬，也最值钱。
        new ManorRaider(RaiderName, ManorArm.Dagger, ManorOutfit.Common, IsSentry: false),
        new ManorRaider(RaiderName, ManorArm.Warhammer, ManorOutfit.Armored, IsSentry: true),
        // 主屋（深）——唯一的枪在主屋门口。里屋那扇门后面的事，是他们干的。
        new ManorRaider(RaiderName, ManorArm.Pistol, ManorOutfit.Leather, IsSentry: true),
        new ManorRaider(RaiderName, ManorArm.Shortsword, ManorOutfit.Leather, IsSentry: false),
        // 后院/枯井（最深，最远）——抄着农具棚里那把草叉的穷鬼。枯井就在他脚边。
        new ManorRaider(RaiderName, ManorArm.Pitchfork, ManorOutfit.Rags, IsSentry: false),
    };

    /// <summary>
    /// 七个人的<b>布点</b>（关卡<b>相对</b>坐标 0~1，与 <see cref="Roster"/> <b>同序</b>）：
    /// 大门(南) → 晒谷场 → 畜栏 → 谷仓 → 主屋前 → 主屋内 → 后院枯井(北，最深)。
    ///
    /// <para>🔴 <b>间距是照着枪声半径设计的</b>（相邻哨位 320~560px，正落在"弓 70 听不见、手枪 350 听得见"的带里）——
    /// 这不是随手撒的点：<b>哨位挨太近，枪就白响了；散太开，枪就白静了</b>。<see cref="AlertedBy"/> 与
    /// <c>StuartManorTests.PostSpacing_MakesNoiseRadiiMeaningful</c> 把这条钉死。</para>
    ///
    /// <para>布点放在这儿（而不是只写在 <c>TestExploration</c> 里）是因为<b>噪音招怪要算得出来</b>：
    /// 一枪的半径能罩住几个人是纯几何，而它必须<b>可测</b>——两处各写一份就会漂移。</para>
    /// </summary>
    // 🔴 画布 2400×1600→3200×2200 后，为让"哨位间/庭院噪音的像素距离"逐字节不变（噪音带 弓0/匕首1/手枪3/步枪6
    //    是这一关的核心机制），Posts 绕庭院中心 (CourtyardX,CourtyardY)=(0.40,0.66) 逐轴逆缩放：
    //      X' = 0.40 + (X-0.40)×(2400/3200)      Y' = 0.66 + (Y-0.66)×(1600/2200)
    //    ⇒ 任意两点及各点到庭院中心的**绝对像素距离**与放大前完全一致（相对差×逆系数×放大后 LevelW = 原绝对距离）。
    //    庄园防御核心因此仍是一块紧凑的 ~600px（步枪半径）区域，放大出来的空间＝周围的田地/院外进路（更多步行）。
    //    注释里的中文方位仍成立（逆缩放只是把整簇往庭院中心收，相对朝向不变）。
    public static IReadOnlyList<(double X, double Y)> Posts { get; } = new[]
    {
        (0.2875, 0.7618182), // 大门·门柱旁      ← 哨兵（棍棒）
        (0.385,  0.6890909), // 晒谷场            （尖头锤）
        (0.49,   0.7327273), // 畜栏              （匕首）
        (0.565,  0.6018182), // 谷仓门口          ← 哨兵（破甲锤·披甲）
        (0.40,   0.5145455), // 主屋前廊          ← 哨兵（手枪 —— 全庄园唯一的枪声）
        (0.325,  0.4272727), // 主屋内            （短剑）
        (0.685,  0.3836364), // 后院·枯井旁        （草叉）
    };

    /// <summary>据点入口方位（关内世界坐标系的<b>相对</b>坐标，南侧偏西＝探索队入关处）：哨兵的扫视中心朝这儿。</summary>
    public static (double X, double Y) Entrance => (0.25, 0.95);

    /// <summary>
    /// <b>在这儿弄出这么大动静，会招来几个人。</b>纯几何：以 (<paramref name="atX"/>,<paramref name="atY"/>)（相对坐标）
    /// 为心、<paramref name="noiseRadiusPx"/> 为半径，罩住几个 <see cref="Posts"/>。
    ///
    /// <para>🔴 <b>这一关的核心机制</b>（金手指帮那关已经量化过同一条）：从庭院中央动手 ⇒
    /// <b>弓（70）叫醒 0 个 / 匕首（90）1 个 / 手枪（350）3 个 / 步枪（600）6 个 —— 也就是整座庄园。</b>
    /// 「枪纸面最强，但<b>一开枪就没有『逐个清哨』了</b>」——而"连打三个持械劫掠者"的存活率是 <b>3.5%</b>。
    /// ⇒ 这一关真正的通关手段是<b>弓/弩 + 岗哨扫视的空窗</b>，不是火力。</para>
    ///
    /// <para>⚠️ 只算直线距离，<b>不算墙体遮挡</b>（噪音本就穿墙传，见 <c>NoiseLogic</c>），也不算"听见了走过来要多久"
    /// ⇒ 这是"<b>最终</b>会被惊动的人数"的上界，不是"立刻扑上来的人数"。</para>
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

    /// <summary>持械档位 → 真武器（走 <see cref="WeaponTable"/> 权威表，<b>不自造数值</b>）。</summary>
    public static Weapon WeaponFor(ManorArm arm) => arm switch
    {
        ManorArm.Shortsword => WeaponTable.Shortsword(),
        ManorArm.Club => WeaponTable.Club(),
        ManorArm.SpikeHammer => WeaponTable.SpikeHammer(),
        ManorArm.Warhammer => WeaponTable.Warhammer(),
        ManorArm.Pitchfork => WeaponTable.Pitchfork(),
        ManorArm.Pistol => WeaponTable.Pistol(),
        _ => WeaponTable.Dagger(),
    };

    /// <summary>
    /// 着装档 → 他身上那几层（走 <see cref="ArmorTable"/> 权威表，<b>不自造数值</b>；层序由外到内，
    /// 与 <see cref="CorpseLoot.Strip"/> 的扒法一致）。<b>每一件都在 <see cref="ApparelCatalog"/> 里登记得到
    /// ⇒ 扒下来真穿得上</b>（扒下来却装不上的东西是纯粹的垃圾，不该进背包）。
    /// </summary>
    public static IReadOnlyList<ArmorLayer> ApparelFor(ManorOutfit outfit) => outfit switch
    {
        ManorOutfit.Rags => new[]
        {
            ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
        },
        ManorOutfit.Common => new[]
        {
            ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt(), ArmorTable.Shorts(),
        },
        ManorOutfit.Jacket => new[]
        {
            ArmorTable.ClothJacket(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
        },
        ManorOutfit.Denim => new[]
        {
            ArmorTable.DenimJacket(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
        },
        ManorOutfit.Leather => new[]
        {
            ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
        },
        // 披甲档：装甲层的皮甲 + 头槽的军用头盔（两者不互斥——头盔不占装甲层，见 ApparelCatalog）。
        ManorOutfit.Armored => new[]
        {
            ArmorTable.MilitaryHelmet(), ArmorTable.Leather(), ArmorTable.LeatherJacket(),
            ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
        },
        _ => new[] { ArmorTable.LongSleeveShirt(), ArmorTable.Trousers() },
    };
}
