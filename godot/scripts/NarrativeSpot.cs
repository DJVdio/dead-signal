using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 GoldfingerDiscovery.cs / LookoutSighting.cs / ExplorationCache.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 叙事调查点框架（极乐迪斯科式，[SPEC-B12] 用户口径）：地图中分散铺不少「调查点」，
// 点击调查 或 靠近即触发，进入一段 CG/环境叙事文本（**不走游戏时间**——呈现期间时钟暂停）。
// 与「物资搜刮点」（ExplorationCache）、「主线触发点」（RadioMainline/VillageRescue/LookoutSighting）三类并存、命名空间隔离：
//   · 叙事点 id 前缀 narrative_、去重旗标前缀 seen_narrative_；
//   · 叙事点**不计入**物资完成度 X/Y（同瞭望望远镜/救援口径，见 ExplorationProgress，不入 CacheIdsFor）。
//
// 本类只保证"读注册表→按 id 解析→出（标题+多屏文本）+去重旗标"可跑、可测，不碰 Godot、不写 flag、不铺 Area2D：
//   · 铺设：关卡层迭代 NarrativeSpotRegistry.ForDestination(dest)，按 (X,Y) 造触发区（Proximity）/交互物（Click）。
//   · 呈现：CampMain.OnExplorationDiscovery 走 Resolve，返回则冻结时标 + 分页弹叙事面板 + 置去重旗标。
// 叙事文本全部为 draft 草稿（一段 2~4 屏、克制压抑、环境讲故事），最终由用户优化。

/// <summary>调查点触发方式。</summary>
public enum NarrativeTrigger
{
    /// <summary>靠近即触发：踏入触发 Area2D（walk-in，复用 AddDiscoveryPoint 先例）。</summary>
    Proximity,

    /// <summary>点击调查：点击调查物 → 角色走近后触发（探索关现无点击拾取先例，落地待「探索关正式化」专项，见关卡层 [HANDOFF]）。</summary>
    Click,
}

/// <summary>
/// 一处叙事调查点的静态定义（数据驱动、单一事实源）。位置以纯 float 承载（X,Y），
/// 关卡层转成 Godot 世界坐标铺点——保持本类零 Godot 依赖、位置可测。
/// </summary>
public sealed class NarrativeSpot
{
    /// <summary>调查点 id（前缀 narrative_）：关卡触发区上报此 id，CampMain 据此 Resolve。</summary>
    public required string Id { get; init; }

    /// <summary>所属目的地路由键（哪张图）：VillageRescue.DestinationName / ExplorationCache.*Name 等。</summary>
    public required string Destination { get; init; }

    /// <summary>关内世界坐标 X（关卡层铺点用）。</summary>
    public required float X { get; init; }

    /// <summary>关内世界坐标 Y。</summary>
    public required float Y { get; init; }

    /// <summary>触发方式（Proximity 已落地；Click 为框架字段，渲染待「探索关正式化」）。</summary>
    public required NarrativeTrigger Trigger { get; init; }

    /// <summary>地面标记旁的短标签（关卡层显示，如「祭台」「登记簿」）。</summary>
    public required string Label { get; init; }

    /// <summary>叙事标题（面板首行）。</summary>
    public required string Title { get; init; }

    /// <summary>多屏叙事文本：一屏一段，面板「继续」逐屏推进，末屏关闭。至少一屏。</summary>
    public required IReadOnlyList<string> Pages { get; init; }

    /// <summary>
    /// 可重读：true＝每次进关都可再触发（不置去重旗标）；false（默认）＝一次性，首次触发后置 <see cref="StoryFlag"/> 永久去重。
    /// </summary>
    public bool Repeatable { get; init; }

    /// <summary>一次性去重旗标键（前缀 seen_narrative_）：可重读点为空串。</summary>
    public string StoryFlag => Repeatable ? "" : "seen_" + Id;
}

/// <summary>一次叙事调查的落地结果：置哪个去重旗标（可重读为空）、弹什么标题+多屏文本。</summary>
public readonly record struct NarrativeSpotResult(string StoryFlag, string Title, IReadOnlyList<string> Pages, bool Repeatable);

/// <summary>
/// 叙事调查点注册表（各图共用，单一事实源）。<see cref="Resolve"/> 由 CampMain 在踏入/点击调查点时调用：
/// 返回 <see cref="NarrativeSpotResult"/> 则 CampMain 负责冻结时标、分页弹叙事面板、（一次性点）置去重旗标；
/// 返回 <c>null</c> 表示未知 id 或已看过（一次性且旗标已置）。<see cref="ForDestination"/> 供关卡层按目的地铺点。
/// </summary>
public static class NarrativeSpotRegistry
{
    /// <summary>本营的路由键（营地也能有调查点——祖母的尸体就在住宅门外）。</summary>
    public const string CampDestination = "幸存者营地";

    /// <summary>祖母的尸体（营地内唯一叙事点，authored 背景：山姆被迫杀死了尸变的祖母）。</summary>
    public const string GrandmotherCorpseId = "narrative_camp_grandmother";

    private static readonly List<NarrativeSpot> Spots = BuildSpots();

    /// <summary>全部叙事调查点（各图汇总）。</summary>
    public static IReadOnlyList<NarrativeSpot> All => Spots;

    /// <summary>某目的地（图）铺设的叙事调查点（关卡层迭代铺 Area2D）。</summary>
    public static IEnumerable<NarrativeSpot> ForDestination(string destination) =>
        Spots.Where(s => s.Destination == destination);

    /// <summary>按 id 取定义；未知返回 null。</summary>
    public static NarrativeSpot? ById(string id) =>
        Spots.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// 解析一次调查。未知 id 返回 <c>null</c>；一次性点且去重旗标已置（已看过）返回 <c>null</c>；
    /// 可重读点恒返回结果。本方法**不写** flag（无副作用）；置 flag 由调用方在弹叙事后进行。
    /// </summary>
    public static NarrativeSpotResult? Resolve(string id, StoryFlags flags)
    {
        NarrativeSpot? spot = ById(id);
        if (spot == null)
            return null;
        if (!spot.Repeatable && flags != null && flags.Has(spot.StoryFlag))
            return null; // 一次性点已看过：不重复
        return new NarrativeSpotResult(spot.StoryFlag, spot.Title, spot.Pages, spot.Repeatable);
    }

    // ---------------- 样例点位（各图铺设，文本 draft 待用户细化） ----------------
    // 数量拟定待调（[SPEC-B12]"分散有不少的调查点"）：南林村庄 4 / 守林人小屋 2 / 瞭望台 1 / 广播台 2。
    // 坐标避开既有物资/主线触发点的 Area2D（关卡尺寸 2400×1600）。
    private static List<NarrativeSpot> BuildSpots() => new()
    {
        // ===== 南林村庄（大点，VillageRescue.DestinationName）：4 处 =====
        new NarrativeSpot
        {
            Id = "narrative_village_shrine_altar",
            Destination = VillageRescue.DestinationName,
            X = 2120f, Y = 470f,           // 祠堂内（避开祠堂搜刮点 2000,420）
            Trigger = NarrativeTrigger.Proximity,
            Label = "祭台",
            Title = "祠堂的祭台",
            Pages = new[]
            {
                "祠堂的门虚掩着，里头没点灯。供桌上的香灰积了厚厚一层，最后一炷香早已烧到底，" +
                "留下一小截焦黑的签脚，斜插在香炉里，没人来收。",

                "祭台正中摆着一排小小的牌位，新旧不一。最外侧那几块木牌，漆色还很鲜，" +
                "刻字的刀口边缘发白——是最近才添上去的，一块挨着一块，日子排得很密。",

                "供品也还在：几个干瘪的橘子、半碗结了硬壳的米饭。它们摆上来的时候，供奉的人大概还相信，" +
                "只要照着老规矩把香火续下去，屋外那些东西就进不了这道门。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_village_clinic_log",
            Destination = VillageRescue.DestinationName,
            X = 1740f, Y = 700f,           // 卫生所内（避开卫生所搜刮点 1850,760）
            Trigger = NarrativeTrigger.Click,   // 翻看登记簿——Click 意图，渲染暂 Proximity（见关卡层 [HANDOFF]）
            Label = "登记簿",
            Title = "卫生所的登记簿",
            Pages = new[]
            {
                "诊桌上摊着一本就诊登记簿，翻到最后几页。前头的字迹工整，一天不过三五行：" +
                "谁家孩子发烧，谁下地割了手。",

                "越往后，字越潦草。同一种症状开始反复出现——发热、乏力、被「咬伤」。" +
                "「咬伤」两个字后来干脆用一个圈代替，一页上圈了十几个。",

                "最后一行没写完，笔尖在纸上拖出一道长长的划痕，直直划出了纸页的边缘。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_village_notice_board",
            Destination = VillageRescue.DestinationName,
            X = 1340f, Y = 960f,           // 村中心公告栏（避开小卖部 1200,1080 / 水井 1120,860）
            Trigger = NarrativeTrigger.Proximity,
            Label = "公告栏",
            Title = "村口的公告栏",
            Pages = new[]
            {
                "村委会门口立着块公告栏，玻璃罩碎了一角。里头的纸被雨水泡得发皱，" +
                "一层盖着一层，能看出贴上去的先后。",

                "最底下是村规、农时、红白喜事的通知。往上，字号越来越大，语气越来越急：" +
                "先是「非必要不外出」，再是「各户清点存粮」，最后一张只有八个字——" +
                "「听从广播，等待撤离」。",

                "那张纸的一角被人撕掉了半截，像是有谁匆忙路过时顺手扯下，塞进了兜里，" +
                "揣着它去等那班永远没有来的车。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_village_well_shoe",
            Destination = VillageRescue.DestinationName,
            X = 1040f, Y = 780f,           // 水井边空地（避开水井搜刮点 1120,860）
            Trigger = NarrativeTrigger.Proximity,
            Label = "井台",
            Title = "水井边",
            Pages = new[]
            {
                "村中心那口老井还在，井绳垂在井口，绳头空着，水桶不知去了哪里。" +
                "井台的青石被无数只手和水磨得发亮，是全村人每天都要来的地方。",

                "石缝里卡着一只很小的鞋，红色的，鞋面上绣了朵褪色的花。就那么一只，" +
                "另一只没有找到。鞋子被踩过，沾着干涸发黑的东西。",

                "你没有往井里看。有些地方，不看，反而好些。",
            },
        },

        // ===== 守林人小屋（小点，ExplorationCache.WatchersCabinName）：2 处，与哥顿支线一致 =====
        new NarrativeSpot
        {
            Id = "narrative_cabin_gordon_desk",
            Destination = ExplorationCache.WatchersCabinName,
            X = 1060f, Y = 610f,           // 外屋书桌（避开里屋碗柜 1285,730）
            Trigger = NarrativeTrigger.Click,   // 翻看桌面——Click 意图，渲染暂 Proximity
            Label = "书桌",
            Title = "屋里的书桌",
            Pages = new[]
            {
                "窗边一张旧书桌，是这屋里唯一收拾得还算齐整的角落。一盏没油的马灯，" +
                "一台老式收音机，旋钮停在某个频段上——早没了电，也早没了信号。",

                "桌面压着几张手写的信纸，字迹一笔一画，很用力。抬头是个女人的名字，" +
                "写了「见字如面」，然后是很长的一段涂改，改到最后整段都被划掉了。" +
                "他大概想说很多，又觉得说什么都没用了。",

                "信纸底下压着一张全家福，边角磨得起了毛。照片里的人在笑。" +
                "他把这张照片，一直放在离手最近的地方。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_cabin_forest_map",
            Destination = ExplorationCache.WatchersCabinName,
            X = 1380f, Y = 580f,           // 外屋墙上护林图（避开哥顿上吊尸 1720,760）
            Trigger = NarrativeTrigger.Proximity,
            Label = "护林图",
            Title = "墙上的护林图",
            Pages = new[]
            {
                "小屋的一整面墙上钉着张手绘的护林地图，林子的沟沟坎坎都标得清清楚楚。" +
                "红笔画的巡逻路线一圈圈绕着，看得出走了很多年。",

                "地图右下角，靠近村子的方向，用力打了个叉，旁边写着一行小字，" +
                "墨水被手蹭花了：「别往那边去。」",

                "叉的外面，又圈了一个更大的圈，把整座林子和那栋孤零零的小屋都圈了进去。" +
                "圈线收口的地方，笔停顿了很久，洇开一小团墨。",
            },
        },

        // ===== 城市之巅瞭望观景台（小点，ExplorationCache.CityRooftopLookoutName）：1 处 =====
        new NarrativeSpot
        {
            Id = "narrative_lookout_love_locks",
            Destination = ExplorationCache.CityRooftopLookoutName,
            X = 900f, Y = 300f,            // 观景护栏西段（避开望远镜 1200,260）
            Trigger = NarrativeTrigger.Proximity,
            Label = "同心锁",
            Title = "护栏上的锁",
            Pages = new[]
            {
                "观景台的护栏上挂满了同心锁，一把挨着一把，密密麻麻，铜锁早已锈成了暗红色。" +
                "曾经，人们来到这座城市的最高处，锁上一把锁，再把钥匙抛下高楼，" +
                "好像这样就能把什么东西永远留住。",

                "锁身上大多刻着两个名字和一个日期。日期最晚的那几把，就停在这座城市失去声音的那个月。" +
                "再往后，没有人再来这里上锁了。",

                "风从楼下灌上来，穿过这一排排的锁，发出很轻的、金属相碰的声音。" +
                "像是许多人还站在这里，只是你看不见。",
            },
        },

        // ===== 广播台（中点，ExplorationCache.BroadcastStationName）：2 处，值机员最后的班次 =====
        new NarrativeSpot
        {
            Id = "narrative_radio_last_shift",
            Destination = ExplorationCache.BroadcastStationName,
            X = 900f, Y = 500f,            // 播音室（避开发射机 1200,300 / 茶水间 650,1230）
            Trigger = NarrativeTrigger.Click,   // 翻看播音台——Click 意图，渲染暂 Proximity
            Label = "播音台",
            Title = "播音室",
            Pages = new[]
            {
                "隔音玻璃后面是间小小的播音室。话筒还架在那儿，「ON AIR」的灯早已熄灭。" +
                "值机员的椅子拉开着，像是主人只是暂时离席，随时会回来接着播下一条。",

                "台面上钉着一张排班表，最后一栏的名字被反复圈了又圈——" +
                "别人都走了，只剩这一个人，把接下来所有的班次都签在了自己名下。",

                "话筒旁压着一叠播音稿，最上面一张写着军方撤离频段的循环通告。" +
                "稿子的空白处，有人用铅笔另写了一行，不是给听众的：" +
                "「只要还有一个人在听，就一直播下去。」",

                "录音机的磁带停在末尾。你没有按下播放。你已经知道，最后播出去的，是谁的声音。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_radio_staff_wall",
            Destination = ExplorationCache.BroadcastStationName,
            X = 1500f, Y = 700f,           // 走廊照片墙（避开发射机 / 备件仓库 1980,360）
            Trigger = NarrativeTrigger.Proximity,
            Label = "照片墙",
            Title = "走廊的照片墙",
            Pages = new[]
            {
                "通往机房的走廊墙上，挂着一整排相框——电台历年的员工合影。西装、笑脸、" +
                "举着话筒的手势，一年一张，记着这地方曾经的热闹。",

                "最近一张合影的玻璃碎了，裂纹从中间散开。照片里的人还是笑着，" +
                "只是有几张脸的位置，被人用笔轻轻圈了出来，圈旁画了个小小的十字。",

                "被圈起来的人越往后越多。到某一张，几乎整排人都被圈上了。" +
                "剩下没被圈的那一个，站在最边上——大概，就是最后还留在这里、给他们一个个画上十字的人。",
            },
        },

        // ===== [SPEC-B13·拟设定待确认] 东部新村（中点，ExplorationCache.EastNewVillageName）：2 处 =====
        new NarrativeSpot
        {
            Id = "narrative_newvillage_couplet",
            Destination = ExplorationCache.EastNewVillageName,
            X = 1300f, Y = 1080f,          // 排屋门口（避开半成品单元搜刮点 1300,1230）
            Trigger = NarrativeTrigger.Proximity,
            Label = "乔迁对联",
            Title = "没贴完的对联",
            Pages = new[]
            {
                "排屋的一户门口，红纸对联贴了一半。上联端端正正糊在门框右侧，" +
                "浆糊都还没干透；下联卷成一卷，搁在门槛上，被风掀着一角。",

                "上联写着「乔迁新居迎百福」，墨迹是新的。横批也裁好了，压在下联底下——" +
                "「安居乐业」。搬进来的头一天，总要图个吉利。",

                "刷浆糊的排刷还搭在半桶浆糊沿上，硬成了一块。贴到一半的人，" +
                "大概是听见了什么，撂下下联就走了，从此再没回来把它贴完。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_newvillage_punch_board",
            Destination = ExplorationCache.EastNewVillageName,
            X = 1520f, Y = 620f,           // 工地项目部外墙（避开项目部搜刮点 1520,760）
            Trigger = NarrativeTrigger.Click,   // 翻看打卡板——Click 意图，渲染暂 Proximity
            Label = "打卡板",
            Title = "工地的打卡板",
            Pages = new[]
            {
                "项目部工棚外墙挂着块考勤打卡板，一排排铁夹子夹着工人的纸卡，" +
                "名字是手写的，有的还标着「架子工」「泥工」「电工」。",

                "最后一天的出勤记录还夹在上头。上工那一栏几乎夹满了卡——那天来的人不少；" +
                "下工那一栏，却空空荡荡，只有零零散散几张。",

                "板子最底下用粉笔写着当天的施工计划：「三号楼封顶」。粉笔字被人潦草地划掉了，" +
                "底下补了两个字，力透板背——「跑」。",
            },
        },

        // ===== [SPEC-B13·拟设定待确认] 加油站（中点，ExplorationCache.GasStationName）：2 处 =====
        new NarrativeSpot
        {
            Id = "narrative_gas_car_queue",
            Destination = ExplorationCache.GasStationName,
            X = 1150f, Y = 1150f,          // 加油区通往公路的出口（避开加油岛 650,1230 / 收银亭 980,1180）
            Trigger = NarrativeTrigger.Proximity,
            Label = "车龙",
            Title = "堵死的车龙",
            Pages = new[]
            {
                "加油站连着公路的那个方向，车一辆顶一辆排成了长龙，一直堵到视线尽头。" +
                "车门大多敞着，有的还挂着挡，仿佛下一秒就能重新发动、往前挪上一格。",

                "排在最前头的几辆早没了油，被人合力推到路肩，好给后面让道——可后面根本没有路了。" +
                "喇叭大概响过很久，直到一辆接一辆地哑掉。",

                "有辆车的后座还系着儿童安全座椅，安全带扣得整整齐齐，座椅是空的。" +
                "他们最后都下了车，徒步汇进逃难的人流。往南，所有人都在往南——" +
                "沿着这条再也开不动的车龙，一直走进你身后那座城的记忆里。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_gas_price_sign",
            Destination = ExplorationCache.GasStationName,
            X = 500f, Y = 1150f,           // 加油区西侧立柱油价牌下（避开零食货架 720,1000 / 加油岛 650,1230 / 收银亭 980,1180，间距均 >150px）
            Trigger = NarrativeTrigger.Proximity,
            Label = "油价牌",
            Title = "立柱上的油价牌",
            Pages = new[]
            {
                "加油站高高的立柱招牌还立着，顶上的价格牌是那种能翻数字的老式牌子。" +
                "92 号、95 号、0 号，三行价格。",

                "价格的数字被人从下往上一路翻乱了——不是加油站翻的。最上面一格，" +
                "有人费力爬上去，把数字全拨成了同一个：一整排的「8」，像谁最后开的一个玩笑。",

                "招牌底座的水泥墩上用喷漆写着一行字，箭头指向公路南边：「有油也没用了，走。」" +
                "喷漆往下淌，拖出长长的泪痕。",
            },
        },

        // ===== [SPEC-B13] 超市（中点，ExplorationCache.SupermarketName）：2 处 =====
        new NarrativeSpot
        {
            Id = "narrative_supermarket_notice",
            Destination = ExplorationCache.SupermarketName,
            X = 360f, Y = 1300f,           // 入口自动门内侧告示栏（避开收银台/据点接触点）
            Trigger = NarrativeTrigger.Proximity,
            Label = "告示",
            Title = "门内的手写告示",
            Pages = new[]
            {
                "自动门早停了电，被人用购物车顶开一道缝。门内侧的立柱上贴着张手写告示，" +
                "字迹起初工整，越往后越潦草。",

                "「本店由幸存者互助会接管。凭劳动换取物资，欢迎加入，共渡难关。」——落款的日期停在很早以前。" +
                "下面又有人补了一行小字，笔锋很急：「别信。进去的没一个出来。」",

                "小字被人用红漆狠狠划掉了，划痕盖住了半张纸。红漆下头，隐约还能认出被涂掉的那句话，" +
                "像一句没人来得及听完的警告。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_supermarket_freezer",
            Destination = ExplorationCache.SupermarketName,
            X = 1450f, Y = 520f,           // 卖场深处冷柜区（避开货架搜刮点/内圈据点）
            Trigger = NarrativeTrigger.Proximity,
            Label = "冷柜",
            Title = "冷柜里的字条",
            Pages = new[]
            {
                "一长排卧式冷柜断电后成了闷罐，玻璃盖上蒙着一层化了又冻的白霜。" +
                "最靠里那台的盖子没关严，缝里塞着一张卷起的纸条。",

                "纸条是用记号笔写在收银小票背面的：「他们把新来的锁在里屋。听见敲声别应，" +
                "那是想让你去开门。」字被冻得发脆，边角一碰就掉渣。",

                "冷柜内壁的霜上有一道道抓痕，从里往外，指甲划过的方向清清楚楚。" +
                "写字条的人大概最后也没能从这排柜子之间走出去。",
            },
        },

        // ===== [SPEC-B13] 医院（大点，ExplorationCache.HospitalName）：2 处 =====
        new NarrativeSpot
        {
            Id = "narrative_hospital_triage_notice",
            Destination = ExplorationCache.HospitalName,
            X = 910f, Y = 2187.5f,         // [大图放大] 急诊入口分诊台旁公告板（原 520,1250 ×1.75，避开分诊搜刮点）
            Trigger = NarrativeTrigger.Proximity,
            Label = "公告",
            Title = "分诊台的最后公告",
            Pages = new[]
            {
                "急诊入口的电子叫号屏碎了，旁边的公告板上还夹着最后一张打印通知，" +
                "边角被无数只手翻得起了毛。",

                "「即日起停止普通门诊。发热、咳嗽、咬伤患者请由西侧通道单独分流，切勿进入住院部。」" +
                "红章盖得很重。通知底下，有人用笔加了一句：「西侧通道后来也封了。」",

                "公告板下方的地面上，粉笔画的分流箭头一层盖着一层，方向互相矛盾，" +
                "像所有人都在最后一刻改了主意，却没有一条线通向出口。" +
                "箭头最密的地方，粉笔被踩成了灰。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_hospital_ward",
            Destination = ExplorationCache.HospitalName,
            X = 2537.5f, Y = 1225f,        // [大图放大] 住院部一间病房（原 1450,700 ×1.75，避开病房搜刮点/护士站）
            Trigger = NarrativeTrigger.Proximity,
            Label = "病房",
            Title = "住院部的病房",
            Pages = new[]
            {
                "住院部一间四人病房，窗帘拉了一半，天光斜进来，照见空着的病床。" +
                "床头卡还插在卡槽里，名字被日晒褪成了浅浅一道印子。",

                "靠窗那张床收拾得整整齐齐，被角压得方方正正——是出院时才有的样子。" +
                "床头柜上摆着一只没剥的橘子和一副老花镜，主人像是只出去散个步，随时会回来。",

                "门背后的墙上，用铅笔画着一道道竖线，五根一组，记着什么的天数。" +
                "计数在某一组画到第三根时戛然而止，那根线拖得很长，一直划到踢脚线，" +
                "再也没有第四根。",
            },
        },

        // ===== [SPEC-B13] 南丁格尔的小药店（小点，NurseRecruit.DestinationName）：1 处 =====
        new NarrativeSpot
        {
            Id = "narrative_pharmacy_prescription_note",
            Destination = NurseRecruit.DestinationName,
            X = 1320f, Y = 780f,           // 前台旁的处方留言板（避开护士相遇点 1150,850 与收银台搜刮点 1000,950）
            Trigger = NarrativeTrigger.Click,   // 翻看留言板——Click 意图，渲染暂 Proximity（见关卡层 [HANDOFF]）
            Label = "留言板",
            Title = "柜台旁的留言板",
            Pages = new[]
            {
                "前台侧面钉着一块软木留言板，本该贴促销海报的地方，如今层层叠叠全是手写的便签，" +
                "字迹潦草，日期越来越近。",

                "「张阿姨的降压药，帮忙留一盒」——底下用另一支笔补了行小字：「已停诊」。" +
                "「三楼小孩发烧，退烧药还有吗」——旁边打了个勾。「胰岛素！急！」——这张被人反复描过，墨都透了纸背。",

                "留言板最下角别着一张没写收件人的便签，字迹和别的都不一样，工整、用力："
                + "「能治的我都治了。治不了的，我陪着。——护士台，值班」。",
            },
        },

        // ===== [SPEC-T51] 斯图尔特家族庄园（中点，StuartManor.DestinationName）：4 处 —— 用户给的四拍，一处一拍 =====
        //
        // 🔴 用户原话（authored 唯一事实源，一字不改）：「并且这里会有斯图尔特家的一些剧情，讲述了他们好心收留一些流浪者，
        //    结果被背刺，女儿妻子被奸杀，男性尸体吊挂在门口喂丧尸，在枯井底有抱着婴儿饿死的女性尸体」
        //
        // 🔴 **用户拍板（原文：「是——就是那伙人」）**：盘踞在这儿的劫掠者，**就是**当年被斯图尔特家收留、随后背刺他们的那伙流浪者。
        //    ⇒ **玩家杀的就是凶手。** 现场证据据此指向"这伙人住在这儿、而且住了很久"（地铺积灰、人挪到了主人的床上、
        //      主人的衣服没人动因为他们身上早换了更好的）。
        //    ⚠️ 但**不替玩家下判断**——文本里没有一句"你为他们复了仇"。看见什么、想什么，是玩家自己的事。
        //
        // 🔴 **本 agent 的权限边界**（CLAUDE.md：剧情是用户手写的 authored 内容，代码只做"按条件播放"的框架）：
        //    下面四段只写**你在这个地方看见了什么**——一个字的引申都没有。
        //    **仍然没有编造**（用户还没写，继续留白）：斯图尔特家的姓氏人名 / 性格 / 对话 / 日记正文 /
        //    这伙流浪者是谁、从哪来 / 背刺的经过 / 井底那个女人的身份。**待填清单见 journal。**
        //
        // ⚠️ **这是极黑暗的内容，用户有意为之**（CLAUDE.md：狠辣设定不要当平衡问题"修复"）——**没有软化、没有加转折**。
        //    同时**克制**：不铺陈施暴细节，只呈现"发现现场"。这既是唯一合适的处理，也是更好的叙事。
        //
        // 🔴 这四具尸体是**叙事点**（ascii narrative_ 前缀），不是可扒的战斗尸体（那种容器名含中文「的尸体 #」）——
        //    命名空间结构性不相交（见 CorpseNaming / StuartManorTests.AuthoredCorpses_AreNeverLootableTrophies）：
        //    **门口那几具吊尸、井底那对母子，永远不会被当成战利品去搜。** 那不是战利品，那是这家人。
        new NarrativeSpot
        {
            Id = StuartManor.GateHangedSpotId,
            Destination = StuartManor.DestinationName,
            X = 850f, Y = 1850f,           // 大门内侧（放大 3200×2200 后：入口在南缘，Y>LevelH*0.75=1650 一进关就撞见；避开门前板车搜刮点 1000,1780）
            Trigger = NarrativeTrigger.Proximity,
            Label = "门口",
            Title = "门口的横梁",
            Pages = new[]
            {
                "庄园的大门敞着，两根门柱之间横着一道粗木梁。梁上挂着几个人，都是男人，" +
                "绳子从梁上垂下来，一根一根，间隔得很匀。",

                "他们离地不高——高到脚尖够不着地，低到底下的东西够得着。" +
                "底下那些东西还在，仰着脸，一下一下地够。这个高度是有人算过的。",

                "门柱上钉着一块木牌，字被人用刀刮掉了，只剩下一道道白茬。" +
                "看得出原本刻的是几个字，是这户人家的姓。",
            },
        },
        new NarrativeSpot
        {
            Id = StuartManor.TakenInSpotId,
            Destination = StuartManor.DestinationName,
            X = 1150f, Y = 1050f,          // 主屋堂屋（放大后：避开堂屋碗柜 1250,980 / 灶间 950,1080）
            Trigger = NarrativeTrigger.Click,   // 翻看地铺——Click 意图，渲染暂 Proximity（见关卡层 [HANDOFF]）
            Label = "地铺",
            Title = "堂屋里多出来的地铺",
            Pages = new[]
            {
                "堂屋的地上打着地铺，一溜排开，五六个，挨着火塘。被褥是从各处凑的：" +
                "旧棉被、麻袋、拆开的帘子——不像给客人预备的，倒像是家里有什么就拿什么出来了。",

                "八仙桌上的碗筷比一家人该有的多出好几副。灶间的米缸空着，" +
                "而这些地铺一直铺到了堂屋门口——住的人不少，住了不短的日子。",

                "地铺上落了厚厚一层灰，被褥发了霉，早就没人睡在这儿了。" +
                "往里走，主屋的床上铺着新拆的干净被面，床脚堆着空罐头和没洗的碗——" +
                "睡的人没有走，只是从地上挪到了床上。",

                "这座庄园里现在住着的，就是当年在这几床被子上过夜的人。",
            },
        },
        new NarrativeSpot
        {
            Id = StuartManor.InnerRoomSpotId,
            Destination = StuartManor.DestinationName,
            X = 1000f, Y = 780f,           // 主屋里屋（放大后：主屋更深处，避开卧室衣柜 980,850 / 储藏间 1350,880）
            Trigger = NarrativeTrigger.Proximity,
            Label = "里屋",
            Title = "里屋",
            Pages = new[]
            {
                "里屋的门框上钉着一道新闩——木头的茬口还是白的，钉得歪，钉得急。" +
                "钉在门的外侧。是把人关在里头用的。",

                "门开着。屋里有两个人，一个大些，一个还很小。她们的衣服不在她们身上。" +
                "床柱上系着绳子，绳头磨得起了毛。",

                "地上的靴印不止一双，来回踩过很多遍，一直踩到门外的走廊上。",

                "你退出来，把门带上。",
            },
        },
        new NarrativeSpot
        {
            Id = StuartManor.DryWellSpotId,
            Destination = StuartManor.DestinationName,
            X = 2300f, Y = 680f,           // 后院枯井（放大后：后院最深处 NE，避开菜窖搜刮点 2200,950）
            Trigger = NarrativeTrigger.Proximity,
            Label = "枯井",
            Title = "后院的枯井",
            Pages = new[]
            {
                "后院角上一口老井，井绳还盘在辘轳上，桶不见了。探身往下看，井底是干的——" +
                "这口井已经枯了很久，久到井壁上长出了青苔。",

                "顺着井壁的石阶下到底，光只剩头顶一小块。",

                "井底坐着一个女人，背靠井壁。她怀里抱着一个婴儿，两条胳膊环得很紧，" +
                "紧到后来也没有松开。他们都很瘦。",

                "井壁上有抠过的痕迹，从她坐着的地方往上，一道一道，指甲抠出来的。" +
                "最高的那道，离井口还有很远。",
            },
        },

        // ===== 幸存者营地（本营，CampDestination）：1 处 —— 祖母的尸体（山姆 authored 背景，剧情严肃向） =====
        // 唯一一处铺在营地内的叙事点：她躺在住宅门外，从开局第一分钟起就在那儿。
        // 关卡层不铺 Area2D——由 camp.json 的 role=corpse 道具点击调查触发（CampMain.ExecuteContainerInteract）。
        new NarrativeSpot
        {
            Id = GrandmotherCorpseId,
            Destination = CampDestination,
            X = 635f, Y = 837f,            // 住宅南门外空地（门口 x∈[580,690]，屋墙 y=790）
            Trigger = NarrativeTrigger.Click,
            Label = "尸体",
            Title = "她还穿着那件花衬衫",
            Pages = new[]
            {
                "她脸朝下趴在住宅门外的空地上，一条胳膊压在身下，像是走到这儿才被绊倒的。" +
                "身上是那件花衬衫——大朵大朵的红花，配一条洗得发白的长裤。" +
                "这是院子里最鲜艳的一件东西：她穿着它下地、喂鸡、隔着半个院子喊你们回来洗手吃饭。",

                "山姆走到离她三步远的地方停下，没有再往前。他左手垂在身侧，小指和无名指的位置空着——" +
                "自九岁那年起就空着。当年是她给缠的纱布，一圈一圈，缠得太紧，他疼得一声没吭。" +
                "门被撞开的那个下午，屋里只有他和她。他做了必须做的事，然后把她拖到了门外——" +
                "屋里是她的屋子。",

                "诺蒂没有过来，只远远站着。当年扑向他的那条疯狗，是山姆挡下的；" +
                "这一回轮到山姆动手，他连挡都没得挡。" +
                "大门口那块木牌是那天翻过来的：「英雄庄园」四个字冲下扣进土里，" +
                "背面用炭笔写了新的名字——幸存者营地。",

                "铁锹一直靠在墙边。没有人提过要埋她，也没有人挪开过那把铁锹。",
            },
        },

        // ===== [批次25·T50] 消防站（小点·低危，ExplorationCache.FireStationName）：1 处 =====
        // ⚠️ 用户**没有**给消防站任何剧情梗概 ⇒ 只做**环境叙事**：写这地方留下的样子，
        //    **不编造角色、不编造前史、不引入新人物**（authored 内容归用户）。克制、简短。
        new NarrativeSpot
        {
            Id = "narrative_firestation_dispatch_board",
            Destination = ExplorationCache.FireStationName,
            X = 480f, Y = 1180f,           // 车库西墙的出车记录板（避开车库两处搜刮点：消防车 700,1300 / 器材墙 1120,1150）
            Trigger = NarrativeTrigger.Proximity,
            Label = "出车记录板",
            Title = "车库墙上的出车记录板",
            Pages = new[]
            {
                "车库西墙挂着一块白板，画着表格：日期、时间、地点、车组。字迹是蓝色白板笔写的，" +
                "前面几十行工工整整，一行一行往下排，中间偶尔有人用红笔补一句「已归队」。",

                "越往下，字越潦草，间隔也越密——同一天里挤了七八行，地点从街名变成了片区，" +
                "再往后干脆只剩数字。最后几行的「已归队」没有人补上。" +
                "板子最下方空了一大片，蓝笔就搁在托槽里，笔帽没盖，早干了。",
            },
        },

        // ===== [警察局] 警察局（小点·Medium，ExplorationCache.PoliceStationName）：1 处 =====
        // ⚠️ 用户**没有**给警察局任何剧情梗概 ⇒ 只做**环境叙事**：写这地方留下的样子，
        //    **不编造角色、不编造前史、不引入新人物**（authored 内容归用户）。克制、简短。
        new NarrativeSpot
        {
            Id = "narrative_police_duty_board",
            Destination = ExplorationCache.PoliceStationName,
            X = 640f, Y = 1420f,           // 门厅东墙的值班公告栏（避开门厅搜刮点 前台 420,1250）
            Trigger = NarrativeTrigger.Proximity,
            Label = "值班公告栏",
            Title = "门厅墙上的值班公告栏",
            Pages = new[]
            {
                "门厅东墙钉着一块软木公告栏，玻璃罩早碎了。上层是排班表，一格一格印着姓名和班次，" +
                "有人用记号笔在几行上打了叉，旁边注着「调休」「支援」。再往下，叉越打越多，注的字也从「支援」变成了同一个词，" +
                "反复出现：疏散。",

                "公告栏最下面钉着一张打印的通知，抬头是市局的红章，正文只有短短几行，要求各岗「原地待命，等候统一部署」，" +
                "落款日期之后，就再没有新的纸压上去。图钉还剩一整盒，摆在栏底的托槽里，没人再用得上了。",
            },
        },

        // ===== [SPEC-T60] 破败教堂（中点·高危，RuinedChurch.DestinationName）：2 处 —— 用户给的两样，一样一处 =====
        //
        // 🔴 用户原话（authored 唯一事实源，一字不改）：「在这里可以找到一些军方留下的被烧了一半的忏悔录
        //    和一些被军方屠杀的人用血写在墙上的对军方的辱骂。」
        //
        // 🔴 **这是"军方干了什么"的第一手证据** —— 而广播台那条主线的岔口正是「回复军方 / 呼叫南方」
        //    （<see cref="RadioMainline"/>）⇒ 这条线索链直通结局。**这一关最值钱的东西是这两处，不是银烛台。**
        //
        // 🔴 **本 agent 的权限边界**（CLAUDE.md：剧情是用户手写的 authored 内容，代码只做"按条件播放"的框架）：
        //    下面两段只写**你在这个地方看见了什么**，一个字的引申都没有。
        //    **一律没有编造**（用户还没写，留白）：
        //      · 忏悔录的**正文**（烧剩那几页上到底写了什么）
        //      · 写字的人**是谁**（哪支部队、哪个人）
        //      · **屠杀的经过**、军方**为什么**这么做
        //      · 墙上那些辱骂的**具体内容**
        //    **待填清单见 journal（[HANDOFF] impl-lategame → 用户）。**
        //    ⚠️ 游戏里**不留可见的 TODO 占位**（那会破坏叙事）——文本现在是完整可读的"发现现场"，
        //    只是**不含用户还没写的那一层**。用户补上之后，只需在这两段里加内容，机制一格不用动。
        //
        // ⚠️ **不软化，也不替玩家下判断**（CLAUDE.md：黑暗向设定是有意为之）：
        //    文本里**没有一句**"军方是恶魔"之类的结论（护栏 <c>RuinedChurchTests</c> 扫死这类词）。
        //    **让玩家看到证据，自己决定要不要回复军方。**
        new NarrativeSpot
        {
            Id = RuinedChurchConfessionSpotId,
            Destination = RuinedChurch.DestinationName,
            X = 600f, Y = 1860f,           // 门厅·西侧告解亭内（封闭小间：不迈进去，你不知道里头有什么）
                                           // [Phase2] 随教堂放大到 3200×2200 重排坐标（仍在西侧告解亭内）；**正文一字未动**。
            Trigger = NarrativeTrigger.Proximity,
            Label = "告解亭",
            Title = "告解亭里的本子",
            Pages = new[]
            {
                "西边那座告解亭的门虚掩着。里头很窄，一条跪凳，一面木格栅——格栅那侧是听的人坐的，" +
                "这一侧是说的人跪的。跪凳的漆磨掉了两块，正好是膝盖的位置。",

                "跪凳上摊着一个本子，硬壳，军绿色，边角压着钢印。它被烧过：封面焦了一半，" +
                "书脊那一侧烧得最狠，纸页卷成黑色的壳，一碰就碎。剩下的部分还能翻开，" +
                "上面是手写的字，蓝黑墨水，写得很密。",

                "告解亭的地板上有一小摊灰，灰里还剩着几片没烧透的纸角。" +
                "有人在这个只能跪着说话的小格子里，把这个本子点着了——然后没有烧完就走了。" +
                "你把烧剩的那些页夹进衣服里。",
            },
        },
        new NarrativeSpot
        {
            Id = RuinedChurchBloodWallSpotId,
            Destination = RuinedChurch.DestinationName,
            X = 448f, Y = 1540f,           // 中殿·西侧廊（长椅切出来的窄廊：你是挤进来才看见它的）
                                           // [Phase2] 随教堂放大重排（侧廊净宽 64 不缩放 ⇒ 仍是"挤进来才看见"）；**正文一字未动**。
            Trigger = NarrativeTrigger.Proximity,
            Label = "侧廊的墙",
            Title = "侧廊墙上的字",
            Pages = new[]
            {
                "西侧廊很窄，一边是长椅的背，一边是墙。挤到一半你才发现墙上有字——不是刻的，也不是写的，" +
                "是抹上去的。褐色，干透了，一层压着一层，从齐肩的高度一直铺到墙根。",

                "字是用手指写的。有几处按下去的手印很清楚：五个指头，然后往下拖出很长的一道。" +
                "写得最矮的那一行贴着地面，字也最小——写它的人当时大概已经站不起来了。",

                "你顺着墙把它们一行一行看完。看得出写字的人不止一个，也看得出他们知道自己在写给谁。",
            },
        },

        // ===== [SPEC-T60] 难民营地（中点·高危，RefugeeCamp.DestinationName）：2 处 =====
        // ⚠️ 用户**没有**给难民营地任何剧情梗概 ⇒ 只做**环境叙事**：写这地方留下的样子，
        //    **不编造角色、不编造前史、不引入新人物**（authored 内容归用户）。克制、简短。
        //    两处都铺在**没有物资、也没有伏击丧尸**的房间里（14/18 号房）——它们是这片排屋里
        //    仅有的两间"推开门之后不是收获、也不是危险，只是安静"的房。
        new NarrativeSpot
        {
            Id = "narrative_refugee_notice_wall",
            Destination = RefugeeCamp.DestinationName,
            X = 900f, Y = 330f,            // 14 号房（北排·最深；该房无搜刮点、无伏击丧尸）·随 Phase2 放大重排
            Trigger = NarrativeTrigger.Proximity,
            Label = "字条墙",
            Title = "贴满字条的那面墙",
            Pages = new[]
            {
                "这间屋子空着，只有一面墙不空：从门框一直到墙角，糊满了字条。作业本纸、烟盒背面、" +
                "撕下来的包装、还有几张医院的化验单——反面朝外，正面的名字被折了进去。",

                "每一张上都是一个名字，一个日期，和一小行字。写的都是同一件事：" +
                "我在这儿，我等到几号。新的贴在旧的上面，压了一层又一层，最底下那些已经黄透了，" +
                "翘着边。最上面那几张的墨还很黑。",

                "墙角堆着一小摞没贴出去的字条，压在一块砖头底下。它们是空白的。",
            },
        },
        new NarrativeSpot
        {
            Id = "narrative_refugee_last_room",
            Destination = RefugeeCamp.DestinationName,
            X = 2700f, Y = 340f,           // 18 号房（北排·东端，排屋的尽头；该房无搜刮点、无伏击丧尸）·随 Phase2 放大重排
            Trigger = NarrativeTrigger.Proximity,
            Label = "尽头那间",
            Title = "排屋尽头的那一间",
            Pages = new[]
            {
                "这是这排平房最里头的一间。屋里的东西全被推到了四面墙根：床架、箱子、椅子、" +
                "拆下来的门板，摞得整整齐齐，中间空出一大块地。",

                "空地中央的水泥上有一圈焦痕，直径一步多宽，里头是薄薄一层灰白色的灰。" +
                "围着焦痕，地上留着一圈压出来的印子，深浅不一，间隔很近——大家挨着坐了很久。",

                "灰是凉的。",
            },
        },
    };

    // ── [SPEC-T60] 破败教堂的两处 authored 证据（id 提出来当常量：ExplorationProgress/测试都要引）──
    /// <summary>告解亭里烧了一半的忏悔录（军方留下的）。</summary>
    public const string RuinedChurchConfessionSpotId = "narrative_church_confession";
    /// <summary>侧廊墙上用血写的字（被军方屠杀的人留下的）。</summary>
    public const string RuinedChurchBloodWallSpotId = "narrative_church_blood_wall";
}
