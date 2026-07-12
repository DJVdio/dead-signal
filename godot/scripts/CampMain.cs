using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 幸存者营地主场景（初级地图）：大平地上的农庄，四面围栏围合、正南+正北各一扇大门。围栏/大门为带 HP 的可破坏结构。
///
/// 【等距 Batch 1（世界视觉精致化）】在 B0 双层地基上把块状换成精致等距：
///  - LogicLayer（Visible=false）：StaticBody2D 墙 / CharacterBody2D actors / RoofFade Area2D，
///    坐标 = camp.json cartesian，物理/寻路/碰撞原封不动。
///  - IsoLayer（YSortEnabled）：iso 视觉——地面菱形地块、建筑地基+墙立面+屋顶、道具、门框/门槛。
/// 遮挡物用抬起的立体块（顶面平铺 + 前向左右立面做假 3D，见 <see cref="IsoTilePanel"/>），长墙/大建筑
/// 切小块各自 YSort 修 B0 擦身错序。屋顶常态 50% 半透、角色进入 80% 透（<see cref="RoofFade"/>）。
///
/// 角色视觉由 B2 的 ActorSprite 承担（Actor 自行经 "iso_layer" group 挂到 IsoLayer）。
/// 与 <see cref="Main"/> 刻意不抽公共基类（避免动战斗侧）。
/// </summary>
public sealed partial class CampMain : Node2D
{
    private Rect2 _mapBounds = new(0, 0, 2400, 1800);
    private float _navInset = 20f;
    private Vector2 _cameraCenter = new(1200, 900);

    // 所有实心矩形（山体/栅栏/建筑墙/道具）——既是碰撞，也作导航挖洞。
    private readonly List<Rect2> _navHoles = new();

    // 可破坏结构（围栏/大门）：HP + 碰撞体/视觉块/rect。HP→0 摧毁时逐一清场并重烘焙导航开出缺口。
    private readonly List<CampStructureInstance> _structures = new();
    private readonly List<Pawn> _survivors = new();
    // 选中：单选事实源。_selectedPawn 是当前唯一主选角色（可为 null=无选中）。
    // _selected 保留为 HashSet 只为兼容既有消费点（装备/读书取 FirstOrDefault），恒 ≤1，与 _selectedPawn 同步。
    private readonly HashSet<Pawn> _selected = new();
    private Pawn? _selectedPawn;
    // 伤病昼夜恶化用随机源（感染 roll 等）：营地生产用，非战斗结算，独立于 CombatEngine 的 rng。
    private readonly IRandomSource _healthRng = new SystemRandomSource();
    private bool _gameOver; // 全灭防重入：game-over 只触发一次。

    // 门缝连通性自检目标（建筑名 + 室内中心 cartesian），首个可用导航帧跑一次。
    private readonly List<(string name, Vector2 target)> _navTargets = new();
    private bool _navTested;

    private CombatEngine _combat = null!;
    private GameClock _clock = null!;
    private ExplorationLevel? _currentLevel;
    private Node? _levelRoot;
    private NavigationRegion2D _campNavRegion = null!;
    private PawnRoleManager _roleManager = null!;
    private double _nightLengthSeconds;   // 一夜实时长度（游戏内秒）：喂给读者把每帧 delta 换算成阅读小时。
    private CameraController _camera = null!;
    private CanvasModulate _ambient = null!;
    private Hud _hud = null!;
    private CharacterPanel _characterPanel = null!;  // 角色检视面板（挂 HUD CanvasLayer，不随相机移动）
    private SurvivorCardBar _cardBar = null!;         // 底部幸存者卡牌栏（挂 HUD CanvasLayer）：单击选中/双击聚焦
    private double _cardStatRefreshElapsed;           // 卡牌栏血/饥饿条节流刷新累计（时标冻结时 delta=0 自动停刷）
    private const double CardStatRefreshInterval = 0.25; // 卡牌栏状态条刷新间隔（秒，真实时标缩放后）

    // HUD 状态行脏缓存：SetStatus 每帧构造大段插值字符串（含 LINQ 计数）是稳定 GC 压力源。
    // 用一组廉价 int/enum/bool 信号做变更检测，唯有内容实际变化时才重建字符串并 SetStatus。
    // 袭营推进节流：守卫目标分派 + 破防/胜负统计不必每帧，~0.15s 一次（丧尸驻留破防圈内，晚 ≤0.15s 判定不漏事件）。
    private double _raidUpdateElapsed;
    private const double RaidUpdateInterval = 0.15;

    private bool _hudInit;
    private bool _hudExploring;
    private int _hudDay = -1;
    private int _hudPhase = -1;
    private int _hudClockKey = -1;
    private int _hudSpeedIndex = -1;
    private bool _hudPaused;
    private int _hudAlive = -1;
    private int _hudHordePhase = -1;   // 尸潮相位脏检信号（-1=未初始化；旗标翻转/到期不必每帧重算文案）
    private int _hudHordeDays = -1;     // 剩余天数脏检信号（仅随 _clock.Day 变）

    private WorldMapPanel _worldMapPanel = null!;
    private ExpeditionPanel _expeditionPanel = null!;
    private GuardPanel _guardPanel = null!;
    private ReadingPanel _readingPanel = null!;
    private ReturnWarningPopup _returnWarningPopup = null!;
    private MealAllocationPanel _mealAllocPanel = null!;
    private CampResources _resources = null!;
    private MealBubblePool _bubblePool = null!;

    // ---------------- 营地搜刮 / 共享库存 / 阅读（W3a） ----------------
    // 背包=营地共享库存（用户口径）：所有搜到的武器/护甲/书进这里，食物进 _resources.Food。
    private readonly InventoryStore _inventory = new();
    // loot 容器（衣柜/展示柜/草垛）的一次性搜刮登记；storage 容器（柜子）不入此表，其藏物开局即入库存。
    private readonly ContainerLoot _containerLoot = new();
    // 书 id → 同一 BookData 实例（阅读面板 MarkRead 后已读态共享，故必须持同一实例）。
    private readonly Dictionary<string, BookData> _bookRegistry = new();
    private Func<string, BookData?> _bookResolver = _ => null;
    // 场上可点击容器（cartesian rect + 角色 + 藏物），右键前往命中据此存取/搜刮。
    private readonly List<ContainerRef> _containers = new();
    private StashPanel _stashPanel = null!;
    private ReaderPanel _readerPanel = null!;
    private bool _stashOpen;             // 库存面板是否开着（时标冻结的唯一持有者）
    private int _prevStashSpeed;         // 开库存前 GameClock 的速度档（关闭时按此还原）
    private bool _prevStashPaused;       // 开库存前世界是否暂停（还原时保真：手动暂停仍还成暂停）

    // 右键前往并交互：记下(前往者, 目标容器)，_Process 轮询到达后开面板/搜刮（自带暂停）；寻路期间绝不暂停。
    private (Pawn pawn, ContainerRef target)? _pendingInteract;
    private double _pendingInteractElapsed;                 // 本次前往已耗时（真实秒，用于超时放弃）
    private const float PendingInteractStandoff = 20f;      // 停在容器最近边缘外的间距
    private const float PendingArriveMargin = 28f;          // 到达判定：进容器 rect 外扩此值内算到位
    private const double PendingInteractTimeout = 18.0;     // 前往超时（秒）：超时未到达则放弃 + 提示

    // ---------------- 配方 / 制作（工作台接入） ----------------
    // 全营共享一台工作台的工具装配态（field 初始化，早于 ApplyStorageInitialStock 就绪，供工具搜到即装）。
    private readonly WorkbenchState _workbench = new();
    private CraftingPanel _craftingPanel = null!;
    private bool _craftingOpen;             // 制作面板是否开着（与库存互斥地持有时标冻结）
    private int _prevCraftingSpeed;         // 开制作面板前 GameClock 的速度档（关闭时按此还原）
    private bool _prevCraftingPaused;       // 开制作面板前世界是否暂停（还原保真）
    // 工时制（批次6）：本工作台当前在制任务（单任务队列）；下单时开工扣料，夜间生产相位逐分钟推进，完工产出。
    private CraftingJob? _craftingJob;
    private int _craftLastMinuteKey = -1;   // 上帧游戏分钟键（ClockMinuteKey）；算夜间流逝分钟增量推进工时，非生产相位/无任务时置 -1
    private Pawn? _craftingJobWorker;        // 本在制任务的下单者（生产者身份，供 3 级光环生产系数；完工/清任务时置 null）
    private float _craftMinuteBudget;        // 工时推进的小数分钟预算（光环 ×1.10 等非整系数下累积不截断；复位点与 _craftLastMinuteKey 同步清零）

    // ---------------- 批次9 开局营地废墟挖掘 ----------------
    // 废墟点纯逻辑注册表（工时进度 + 一次性收获，见 RubbleField / RubbleSite）。
    private readonly RubbleField _rubble = new();
    // 废墟视觉/碰撞句柄（key = RubbleSite.Id）：挖净后逐块清场 + 移导航洞重烘焙 = 显露空地。
    private readonly Dictionary<string, RubbleInstance> _rubbleVisuals = new();
    // 当前挖掘指派：某幸存者在某废墟点作业。到位后逐游戏分钟推进（同工时制分钟键增量）；
    // 离场/死亡/改派 = 中断（进度留存在 RubbleSite，回来续挖）。★HANDOFF(night-response)：双班硬日程落地后，
    // 挖废墟须豁免或归入白天营地/探险队可做的活，否则夜班生产、白天营地无人可操作时废墟无人可挖会死锁（现状全天可操作，先按现状接）。
    private (Pawn pawn, string rubbleId)? _digging;
    private int _digLastMinuteKey = -1;      // 上帧游戏分钟键；算流逝分钟增量推进挖掘，离场/无指派时置 -1

    /// <summary>一处废墟的运行时句柄：碰撞体 + 切块视觉 + rect，挖净时逐一清场并重烘焙导航（镜像可破坏结构摧毁）。</summary>
    private sealed class RubbleInstance
    {
        public StaticBody2D? Body;
        public readonly List<Node2D> Visuals = new();
        public Rect2 Rect;
    }

    // ---------------- 医疗（手术/用药面板） ----------------
    private MedicalPanel _medicalPanel = null!;
    private bool _medicalOpen;              // 医疗面板是否开着（同样冻结时标）
    private int _prevMedicalSpeed;          // 开医疗面板前 GameClock 的速度档（关闭时按此还原）
    private bool _prevMedicalPaused;        // 开医疗面板前世界是否暂停（还原保真）
    private CampToast _campToast = null!;    // 制作/搜刮的一行瞬时提示（HUD 顶部，手动显隐——时标冻结下计时器不走）
    private DiscoveryPanel _discoveryPanel = null!;  // 探索发现环境叙事面板（模态，弹出时冻结时标）
    private double _prevDiscoveryTimeScale;          // 弹发现叙事前的时标（关闭时恢复）
    private double _prevLookoutTimeScale;            // 望远镜瞭望演出前的时标（演出期间冻结探索层，播完恢复）

    // ---------------- 神秘商人（中立到访者 + 货币交易） ----------------
    // 每 1~5 天夜晚到访、天亮离开（游戏白天=探险队视角、夜晚=营地视角，商人须与营地可操作窗口重合）；
    // 只卖《木匠入门》（架子数据驱动可扩展）；货币=白银，走共享库存实扣实产。
    private const int MerchantStartingCurrency = 40; // draft：开局起步白银（让交易闭环开箱即跑；掉落来源/经济量级待用户设计，见 TODO）
    private MerchantPanel _merchantPanel = null!;
    private MerchantSchedule _merchantSchedule = null!;
    private readonly MerchantShelf _merchantShelf = MerchantShelf.Default();
    private Merchant? _merchant;                      // 当前在场商人（null=未来访）
    private ContainerRef? _merchantContainer;         // 商人停留点的可交互登记（离场时从 _containers 移除）
    private bool _merchantOpen;                       // 交易面板是否开着（冻结时标）
    private int _prevMerchantSpeed;                   // 开面板前速度档
    private bool _prevMerchantPaused;                 // 开面板前是否暂停

    /// <summary>场上一个可点击容器：名字（稳定标识）+ cartesian 命中矩形 + 角色（storage/loot）+ 藏物清单。</summary>
    private sealed class ContainerRef
    {
        public string Name = "";
        public Rect2 Rect;
        public string Role = "";
        public List<LootItem> Loot = new();
    }
    // 剧情/世界 flag 存储：condition 谓词只读它、气泡 triggers 写它，推动用户手写剧情走向。
    // 内容（哪些 flag、取什么值）全部来自 meal_bubbles.json 里用户填的数据；此处仅持有存储实例。
    private readonly StoryFlags _storyFlags = new StoryFlags();
    // 近期已故名单：死者已从 _survivors 移除，这里另存**死亡当刻拍的快照**供聚餐气泡"死亡反应"读取（dead 谓词命中）。
    // 死时即拍（不持 Pawn 引用，避免节点被回收后 Inspect 失效）；每餐结算末清空——死亡只在紧随其后的一餐被提及（"近期"）。
    private readonly List<PawnSnapshot> _recentlyDeceased = new();
    private string _pendingDestination = "";
    private int _pendingTravelTime;

    private Node2D _logicLayer = null!;  // 物理/导航平面（cartesian，不可见）
    private Node2D _isoLayer = null!;     // 视觉层（iso，YSort）
    private Node2D _actorLayer = null!;   // actors（LogicLayer 下）

    // ---------------- D 守卫防御战 ----------------
    // 已建岗位（含预置 + 调试放置）。哨塔/屋顶=实心结构；暗哨=非碰撞站位标记。
    private readonly List<GuardPostInstance> _guardPosts = new();

    // ---------------- 批次4 光照与视野：营地固定光源场 ----------------
    // 读 camp.json lights → 固定光源(火堆/油灯)集合。供 vision-render 遮暗合成 / enemy-vision 感知
    // 按局部光照 L 查询：field.StrongestAt(x,y[,手持光源]) 出最强光源贡献，再 VisionLogic.CombineLight(环境光,贡献)。
    // 本类只做数据采集入口；发光视觉(Light2D/光晕)由 vision-render 渲染层负责。
    private readonly LightField _campLights = new();

    /// <summary>本帧持光幸存者手持光源采样缓冲（复用去 alloc，见 <see cref="CurrentHandheldLights"/>）。</summary>
    private readonly List<PlacedLight> _handheldScratch = new();

    // 视野揭示（CampRevealables）复用态：去 4Hz 逐 hostile 闭包/ValueTuple 分配（tech-review #2）。
    private readonly List<(Vector2 worldPos, Action<bool> setVisible)> _revealBuffer = new();
    private readonly Dictionary<Actor, Action<bool>> _revealDelegates = new();
    private readonly List<Actor> _revealPrune = new();

    /// <summary>营地固定光源场（供视野/遮暗按位置查询最强光源贡献）。</summary>
    public LightField CampLights => _campLights;
    private readonly List<Zombie> _raidZombies = new();  // 当前袭营波次（存活）
    private VisionMask? _campVisionMask;                   // 营地视野遮暗（批次4，夜间启用/白天豁免）
    private readonly List<Pawn> _raidGuards = new();       // 本夜上岗守卫（存活）
    private readonly List<Dog> _raidGuardDogs = new();     // 本夜上岗犬类守卫（布鲁斯，站岗效率 75%，与 _raidGuards 平行）

    // ---------------- 批次6 夜防对抗：袭击者潜入（警戒 vs 潜行，night-response）----------------
    // 与丧尸袭营(_raidActive)/尸潮围攻(_siegeActive)/教学关(_tutorialActive)并列的第四路：夜间袭击者(Raider)潜入，
    // 走「警戒力 vs 潜行力」对抗——发现→暂停+三档响应拉人入战；未发现→按意图潜行先手 1.5x（Killer）或静默偷物资（Looter）。
    private readonly List<Raider> _nightRaiders = new();    // 本夜潜入的袭击者（存活）
    private bool _nightRaidActive;                          // 夜间袭击者袭营进行中
    private RaiderIntent _nightRaidIntent = RaiderIntent.Killer;
    private double _nightRaidUpdateElapsed;                 // UpdateNightRaid 节流累计（复用 RaidUpdateInterval）
    private readonly HashSet<int> _respondingIds = new();   // 本次响应被点名参战的幸存者 id（三档响应名册译得）
    private int _pendingTheftAmount;                        // 夜里被静默偷走的食物份数（DawnMeal 晨间提示后清零）
    private readonly IRandomSource _raidContestRng = new SystemRandomSource(); // 对抗掷点（SPEC 口径 roll 走 IRandomSource）
    private readonly Dictionary<int, float> _guardPostSightById = new();       // 守卫/犬 id → 岗位视野倍率（岗哨建筑加成源，StationGuards 采集）
    private const float NightRaiderRaidChance = 0.35f;      // 合法夜袭击者潜入概率（拟定待调）
    private const int NightRaiderCountBase = 2;             // 袭击者基数（随天数缓增，拟定待调）
    // 对抗掷点时机（param-calibration 校准）：袭击者边缘生成时距守卫≈256px 超夜视134/听力220 → 单次掷点发现率≈0。
    // 改为随尖兵**逼近**到各距离带（对最近守卫的距离）时分段各掷一次（帧率无关，检测累积）；深入到营心 StrikeDistance 仍未发现 → 未发现后果兑现。
    // 带集 param-calibration 复扫定值（watchcal/watchsweep，真实营地几何）：{170,120,80} 三带累积 → 裸营(单守卫)63% 偏高；
    // 改 {150,90} 两带 → 裸营 48%（落目标 35-55%）、且若守卫覆盖逼近轴满配 85%（落 80-90%）。见 docs/research/2026-07-12-watch-calibration.md。
    private static readonly float[] NightRaidContestBands = { 150f, 90f }; // 尖兵→最近守卫距离带，逐带一次对抗掷点
    private const float NightRaidStrikeDistance = 150f;     // 尖兵深入到营心此半径内仍未被发现 → 兑现未发现后果（拟定待调）
    private bool _nightRaidResolved;                        // 本波对抗是否已决出（发现/未发现兑现后 true；决出前守卫未警觉不迎战）
    private readonly HashSet<int> _nightRaidRolledBands = new(); // 本波已掷过的距离带下标（防同带重复掷，帧率无关）
    private int _prevRaidResponseSpeed;                     // 响应面板冻结前时标（速度档）
    private bool _prevRaidResponsePaused;                  // 响应面板冻结前时标（暂停态）
    // 双班硬日程（SPEC-B6①）：当日探险名册。夜里 DayCrew=强制睡；ExpeditionIds 在 DayReturn 即清空，故本类自存作夜间班别真源。
    private readonly HashSet<int> _todaysExpeditionIds = new();
    // 次相位疲劳（被唤醒者次个在勤相位吃 debuff、过一相位清）：pending=待施加(id→debuff)、active=本相位生效中。
    private readonly Dictionary<int, FatigueDebuff> _pendingFatigueWake = new();
    private readonly Dictionary<int, FatigueDebuff> _activeFatigue = new();

    // ---------------- 批次5 道格与布鲁斯 ----------------
    // 道格=普通可入队幸存者（Pawn，在 _survivors 内，聚餐/读书/医疗/守卫/探索均照常）；布鲁斯=其犬类伙伴（Dog，
    // 独立 actor，不入 _survivors/不占卡牌）。二者由本类持引用配对；入队时机/剧情=用户手写，本批经调试键注入验证。
    private Pawn? _doug;   // 道格（Pawn），身故即置 null
    private Dog? _bruce;   // 布鲁斯（Dog），身故即置 null
    private bool _bruceExpedition; // 本次探索是否带上布鲁斯（须道格同队；决定关卡 CompanionDog 注入与回收）
    // 羁绊升级推进：道格&布鲁斯共同存活天数（每昼夜两者皆活 +1；任一死即停累加=冻结，见 AdvanceBondDay）。
    // 运行时态——本工程尚无存档系统，故不持久化（有存档后随现有状态模式落盘）。等级经 DougBruceBond.EvaluateLevel 现算。
    private int _bondDaysBothAlive;
    private bool _raidActive;
    private float _raidIntensity = 1f;
    // 尸潮终局：到期(day>=DeadlineDay)启动的无限围攻。复用袭营执行层(_raidActive+守卫锁敌+SpawnCampZombies)，
    // 但不走胜负结算——波次不停轮、无生还路线，唯一出口是全灭(GameOverCondition)。数值调度归 HordeTimeline。
    private bool _siegeActive;
    private int _siegeWaveIndex;        // 已投放波次序号（0=首波）
    private double _siegeWaveElapsed;    // 距上一波投放已过秒（逐帧累积，喂 HordeTimeline.NextWave）
    // 结局②军袭全灭上下文（预留）：军方白天来袭屠杀留守者时，若恰全员在营被屠尽 → 全灭走 CG②（背叛全灭变体）。
    // 军袭事件本体未实装（TryTriggerMilitaryRaid 目前 no-op），故此位恒 false；实装军袭时在其屠杀分支置位（见 TODO）。
    private bool _militaryRaidWipeContext;
    private GuardPostKind _debugPlaceKind = GuardPostKind.Watchtower; // 调试放置轮换类型
    private const float BreachRadius = 420f;  // 破防线：丧尸摸进营心此半径内 = 破防（随 2400×1800 地图放大调，拟定待调）

    // ---------------- 教学关：克莉丝汀反水（第 2 夜脚本人类袭击，自成一路，与丧尸袭营互斥）----------------
    private readonly List<Raider> _tutorialRaiders = new();  // 场上普通劫掠者（不含克莉丝汀）
    private Raider? _christine;                              // 克莉丝汀（Raider 实例；反水前敌、反水后友、招募后转 Pawn 移除）
    private bool _tutorialActive;                            // 教学关战斗进行中（逐帧 UpdateChristineTutorial）
    private bool _christineTurned;                           // 克莉丝汀是否已反水（切 Survivor 阵营）
    private const int TutorialRaiderCount = 2;               // 固定生成 2 个劫掠者（不走 RaidWave 概率）
    private const string ChristineName = "克莉丝汀";          // 招募后作为 Pawn 的显示名（请求线据此识别她）

    // ---------------- 座位家具（读书等指派活动的可坐点） ----------------
    // 营地预置的非实心"座位"点（cartesian 坐标 + 占用登记）。读者认领离自己最近的空座、寻路走去坐下读，读完释放。
    // 纯簿记（就近取空座/占用/释放）在 SeatRegistry；建造/寻路在本类 AddSeat + Claim/Release API。
    private readonly SeatRegistry _seats = new();

    /// <summary>
    /// 一处可破坏结构（围栏/大门）的运行时实例：血量状态 + cartesian rect + 碰撞体/视觉块。
    /// Blocking=true（围栏）建实心碰撞 + 导航洞；Blocking=false（大门）为可通行关口（无碰撞、不入导航洞，门控后续）。
    /// </summary>
    private sealed class CampStructureInstance
    {
        public CampStructureState State = null!;
        public Rect2 Rect;
        public bool Blocking;                       // true=围栏(碰撞+导航洞)；false=可通行大门
        public StaticBody2D? Body;                  // 仅 Blocking 时存在
        public readonly List<Node2D> Visuals = new();
        public bool Removed;                        // 已摧毁并清场（防重复摧毁/重烘焙）
    }

    /// <summary>单个已建岗位：类型 + 属性 + 守卫驻守站位（cartesian，须可寻路）。</summary>
    private sealed class GuardPostInstance
    {
        public GuardPostKind Kind;
        public GuardPostStats Stats;
        public Vector2 StandPos;
        public string Name = "";
    }

    private CampConfig _cfg = new();
    private Heights _heights;

    // 睡眠点（住宅室内中心 / 仓库室内中心，须跟随 camp.json buildings 放大后的 rect）。
    private static readonly Vector2[] SleepPositions =
    {
        new(630, 600),
        new(1610, 550),
    };

    // z 分层：地面 → 建筑地基/门槛 → 立体遮挡物/角色(YSort) → 屋顶。
    private const int ZGround = -20;
    private const int ZFloor = -18;
    private const int ZThreshold = -16;
    private const int ZWorld = 0;
    private const int ZRoof = 30;

    // 立体块切分粒度（cartesian 像素）——越小 YSort 越准但越碎/越多节点。「拟定待调」
    private const float CellWall = 96f;
    private const float CellMountain = 220f;
    private const float CellFence = 140f;

    public override void _Ready()
    {
        _combat = new CombatEngine();
        _cfg = LoadCampConfig();
        _mapBounds = ToRect(_cfg.mapBounds) ?? _mapBounds;
        _navInset = _cfg.navInset > 0 ? (float)_cfg.navInset : _navInset;
        _cameraCenter = ToVec(_cfg.cameraCenter) ?? _mapBounds.GetCenter();
        _heights = Heights.From(_cfg.heights);

        VerifyIsoRoundtrip();

        _isoLayer = new Node2D { Name = "IsoLayer", YSortEnabled = true };
        // B2 的 Actor 会用 GetTree().GetFirstNodeInGroup("iso_layer") 找到本层挂 ActorSprite。
        _isoLayer.AddToGroup("iso_layer");
        AddChild(_isoLayer);
        _logicLayer = new Node2D { Name = "LogicLayer", Visible = false };
        AddChild(_logicLayer);

        BuildWorld();
        BuildNavigation();
        SetupCampVisionMask();

        _clock = new GameClock();
        AddChild(_clock);
        GameClock.Config dayNightCfg = LoadDayNightConfig();
        _nightLengthSeconds = dayNightCfg.NightLengthSeconds; // 留一份给读者换算阅读小时
        _clock.Configure(dayNightCfg);
        _clock.OnPhaseChanged += OnGamePhaseChanged;

        _worldMapPanel = new WorldMapPanel();
        AddChild(_worldMapPanel);
        _worldMapPanel.Visible = false;
        _worldMapPanel.DestinationSelected += OnWorldMapDestinationSelected;
        _worldMapPanel.Cancelled += () => _worldMapPanel.Visible = false;

        _expeditionPanel = new ExpeditionPanel();
        AddChild(_expeditionPanel);
        _expeditionPanel.Visible = false;
        _expeditionPanel.SelectDestinationRequested += () => _worldMapPanel.Visible = true;
        _expeditionPanel.ExpeditionConfirmed += OnExpeditionConfirmed;
        // 「取消」语义：关窗留在营地（与 worldMapPanel/readingPanel 的 Cancelled 处理一致，不推进相位）。
        _expeditionPanel.Cancelled += () => _expeditionPanel.Visible = false;

        _guardPanel = new GuardPanel();
        AddChild(_guardPanel);
        _guardPanel.Visible = false;
        _guardPanel.GuardConfirmed += OnGuardConfirmed;

        // 读书指派面板：NightPrep 守卫面板确认后顺序弹出（MVP）。确认/取消后才进 NightAct（单一触发点，防双触发）。
        _readingPanel = new ReadingPanel();
        AddChild(_readingPanel);
        _readingPanel.Visible = false;
        _readingPanel.ReadingConfirmed += OnReadingConfirmed;
        _readingPanel.Cancelled += OnReadingCancelled;

        _returnWarningPopup = new ReturnWarningPopup();
        AddChild(_returnWarningPopup);
        _returnWarningPopup.Visible = false;
        _returnWarningPopup.ReturnNow += OnReturnNow;
        _clock.OnExploreWarning += OnExploreWarning;

        _resources = LoadCampResources();
        _bubblePool = LoadMealBubbles();
        _mealAllocPanel = new MealAllocationPanel();
        AddChild(_mealAllocPanel);
        _mealAllocPanel.Visible = false;
        _mealAllocPanel.Confirmed += OnMealAllocationConfirmed;

        // 营地搜刮/库存/阅读（W3a）。书解析器取 BookLibrary 的**单一快照实例**（每 id 一份，已读态共享）。
        var bookSnapshot = BookLibrary.All().ToDictionary(b => b.Id);
        _bookResolver = id => bookSnapshot.TryGetValue(id, out BookData? b) ? b : null;

        _stashPanel = new StashPanel { Layer = 20 };
        AddChild(_stashPanel);
        _stashPanel.Visible = false;
        _stashPanel.BookOpenRequested += OnBookOpenRequested;
        _stashPanel.EquipRequested += OnStashEquipRequested;
        // 手持光源「持起」接线待 Pawn.HeldLight/EquipLight 就绪（perf-ux-fix 正持 Pawn 锁），见 journal [HANDOFF]。
        _stashPanel.LightHoldRequested += OnStashLightHoldRequested; // 库存「持起」手持光源（手电/火把）→ Pawn.HeldLight
        _stashPanel.Closed += CloseStash;

        _readerPanel = new ReaderPanel { Layer = 21 }; // 叠在库存面板之上
        AddChild(_readerPanel);
        _readerPanel.Visible = false;
        _readerPanel.Closed += OnReaderClosed;

        _discoveryPanel = new DiscoveryPanel { Layer = 22 }; // 探索发现叙事，独立于库存链路
        AddChild(_discoveryPanel);
        _discoveryPanel.Visible = false;
        _discoveryPanel.Continued += OnDiscoveryContinued;

        // 工作台制作面板（点营地工作台打开；与库存面板一样冻结时标）。事件接 CraftingService 实扣实产。
        _craftingPanel = new CraftingPanel { Layer = 20 };
        AddChild(_craftingPanel);
        _craftingPanel.Visible = false;
        _craftingPanel.CraftRequested += OnCraftRequested;
        _craftingPanel.ModApplyRequested += OnModApplyRequested;
        _craftingPanel.Closed += CloseCrafting;

        // 神秘商人交易面板（右键前往在场商人打开；冻结时标）。买入事件走 MerchantTrade.Buy 实扣白银实产商品。
        _merchantPanel = new MerchantPanel { Layer = 20 };
        AddChild(_merchantPanel);
        _merchantPanel.Visible = false;
        _merchantPanel.BuyRequested += OnMerchantBuyRequested;
        _merchantPanel.SellRequested += OnMerchantSellRequested;
        _merchantPanel.Closed += CloseMerchant;

        // 医疗面板（按 M 打开；冻结时标）。事件接 Health.PerformSurgery / TreatIllness 实做 + 扣耗材。
        _medicalPanel = new MedicalPanel { Layer = 20 };
        AddChild(_medicalPanel);
        _medicalPanel.Visible = false;
        _medicalPanel.SurgeryRequested += OnSurgeryRequested;
        _medicalPanel.TreatRequested += OnTreatRequested;
        _medicalPanel.Closed += CloseMedical;

        // 制作/搜刮一行瞬时提示（HUD 之上，独立高层，时标冻结下靠手动显隐）。
        _campToast = new CampToast { Layer = 26 };
        AddChild(_campToast);

        // storage 容器（住宅柜子）的开局藏物：食物入 _resources.Food、书/武器/护甲入共享库存、材料入库存、工具装工作台。
        ApplyStorageInitialStock();

        // 神秘商人：给一点起步白银（draft，让交易开箱可跑；正式掉落来源/经济量级待用户设计）+ 初始化来访调度。
        // 首访排在 1~5 天后（不在开局当天）；随机走 SystemRandomSource（生产随机源）。
        if (MerchantStartingCurrency > 0)
        {
            string coinName = Materials.Find(Materials.CurrencyKey)?.DisplayName ?? "白银";
            _inventory.Add(Item.Material(Materials.CurrencyKey, coinName, MerchantStartingCurrency));
        }
        _merchantSchedule = new MerchantSchedule(new SystemRandomSource(), _clock.Day);

        _ambient = new CanvasModulate();
        AddChild(_ambient);

        // 相机在 iso 屏幕空间平移/缩放：中心与边界都投影到 iso。
        Rect2 isoBounds = Iso.ProjectBounds(_mapBounds);
        _camera = new CameraController { Position = Iso.Project(_cameraCenter) };
        _camera.SetBounds(isoBounds.Grow(-120));
        AddChild(_camera);
        _camera.MakeCurrent();

        _hud = new Hud();
        AddChild(_hud);

        // 角色面板挂到 HUD 这层 CanvasLayer（UI 层，不随相机变换）；初始隐藏，关闭按钮 → 收起。
        _characterPanel = GD.Load<PackedScene>("res://scenes/CharacterPanel.tscn").Instantiate<CharacterPanel>();
        _hud.AddChild(_characterPanel);
        _characterPanel.HidePanel();
        _characterPanel.CloseRequested += _characterPanel.HidePanel;

        // 战斗日志面板挂到 HUD 层（E③）：自订阅 CombatFeed，_ExitTree 自退订。
        _hud.AddChild(new CombatLogPanel());

        SpawnActors();

        // 底部幸存者卡牌栏（挂 HUD 这层 CanvasLayer，不随相机移动）：单击选中（走 SetSelection 弹右侧面板）、
        // 双击选中并平滑聚焦到该角色。选中高亮由 RefreshSelectionUi→SetSelected 自动同步。
        _cardBar = new SurvivorCardBar();
        _hud.AddChild(_cardBar);
        _cardBar.SetSurvivors(_survivors);
        _cardBar.CardClicked += p => SetSelection(p);
        _cardBar.CardDoubleClicked += p =>
        {
            SetSelection(p);
            _camera.FocusOn(Iso.Project(p.GlobalPosition));
        };

        _roleManager = new PawnRoleManager(_survivors, _clock);
        _roleManager.RolesChanged += OnRolesChanged;

        CallDeferred(nameof(StartFirstDay));
    }

    /// <summary>反投影正确性自检：一批 cartesian 点 project→unproject 应精确还原（误差 ~0）。</summary>
    private void VerifyIsoRoundtrip()
    {
        Vector2[] samples =
        {
            new(0, 0), _mapBounds.End, _cameraCenter,
            new(560, 720), new(320, 300), new(1259, 999), new(801, 1119),
        };
        float maxErr = 0f;
        foreach (Vector2 s in samples)
        {
            maxErr = Mathf.Max(maxErr, s.DistanceTo(Iso.Unproject(Iso.Project(s))));
        }
        GD.Print($"[Iso] project↔unproject 往返最大误差 = {maxErr:0.######} px（期望 ~0，验证鼠标反投影正确性）");
    }

    // ---------------- 建图 ----------------

    private void BuildWorld()
    {
        _actorLayer = new Node2D { Name = "Actors" };

        // 地面（iso 平铺菱形地块，恒底层）。仅视觉，无碰撞。
        var groundStyle = _cfg.ground ?? new PixelStyle { color = new[] { 0.20, 0.23, 0.18 }, jitter = 0.05 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = _mapBounds,
            Style = groundStyle,
            Seed = 1,
            Cell = 64f,
            ZIndex = ZGround,
        });

        // 地图边界墙（纯碰撞，防走出世界；山体已覆盖，无视觉）。
        float t = 20f;
        AddBorderWall(new Rect2(0, 0, _mapBounds.Size.X, t));
        AddBorderWall(new Rect2(0, _mapBounds.Size.Y - t, _mapBounds.Size.X, t));
        AddBorderWall(new Rect2(0, 0, t, _mapBounds.Size.Y));
        AddBorderWall(new Rect2(_mapBounds.Size.X - t, 0, t, _mapBounds.Size.Y));

        // 山体（可选，抬起的立体块切块 YSort）。当前大平地布局 camp.json 无 mountains，此循环空跑；保留以便日后地图复用。
        var mtnStyle = _cfg.mountainStyle ?? new PixelStyle { color = new[] { 0.27, 0.32, 0.27 }, jitter = 0.14 };
        foreach (RectSpec m in _cfg.mountains ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(m.rect) is { } r)
            {
                AddSolid(r, mtnStyle, seed: 7, (float)_heights.mountain, CellMountain);
            }
        }

        // 四面围栏（可破坏结构：实心碰撞 + 导航洞 + HP）+ 南北大门缺口（门柱更高，非破坏锚点）。
        var fenceStyle = _cfg.fenceStyle ?? new PixelStyle { color = new[] { 0.40, 0.30, 0.19 }, jitter = 0.12 };
        foreach (RectSpec f in _cfg.fences ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(f.rect) is { } r)
            {
                AddStructure(r, fenceStyle, seed: 11, (float)_heights.fence, CellFence,
                    StructureTier.FenceBasic, blocking: true);
            }
        }
        foreach (RectSpec p in _cfg.gatePosts ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(p.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 13, (float)_heights.post, CellFence);
            }
        }
        // 南北大门（可破坏结构：HP=250；**默认关闭**——实心碰撞+挖导航洞+门槛视觉，敌人须砸门破防。门控后续）。
        foreach (GateSpec g in _cfg.gates ?? System.Array.Empty<GateSpec>())
        {
            if (ToRect(g.rect) is { } r)
            {
                AddGate(r);
            }
        }

        // 建筑（地基 + 墙立面 + 门 + 屋顶淡出）。
        foreach (BuildingSpec b in _cfg.buildings ?? System.Array.Empty<BuildingSpec>())
        {
            BuildBuilding(b);
        }

        // 道具（工作台等实心障碍，矮立体块）。带 role 的道具（storage/loot）另登记为可点击容器。
        foreach (PropSpec pr in _cfg.props ?? System.Array.Empty<PropSpec>())
        {
            if (ToRect(pr.rect) is { } r)
            {
                var style = new PixelStyle { color = pr.color, jitter = pr.jitter };
                if (pr.role == "seat")
                {
                    // 座位家具：非实心可站点（照暗哨路径——不建碰撞、不挖导航洞），读者据此可寻路走上就座。
                    AddSeat(r, style);
                }
                else
                {
                    AddSolid(r, style, seed: 17, (float)_heights.prop, cell: 200f);
                    RegisterContainer(pr, r);
                }
            }
        }

        // 守卫岗位（预置点，读 camp.json guardPosts）。哨塔/屋顶=实心结构进 _navHoles（随首次烘焙生效）；暗哨=非碰撞标记。
        BuildGuardPosts();

        // 固定光源（预置点，读 camp.json lights）。火堆/油灯灌进 _campLights 供视野/遮暗按位置查询；视觉留渲染层。
        BuildCampLights();

        // 开局废墟（预置点，读 camp.json rubble）。实心瓦砾块 + 登记 RubbleField + 可点击容器，供玩家挖掘开拓。
        BuildRubble();

        _logicLayer.AddChild(_actorLayer);
    }

    /// <summary>读 camp.json lights 一次性灌入固定光源场（key 对齐 LightSource 目录；未知 key 由 LightField 静默忽略）。</summary>
    private void BuildCampLights()
    {
        _campLights.Clear();
        foreach (LightSpec spec in _cfg.lights ?? System.Array.Empty<LightSpec>())
        {
            Vector2? pos = ToVec(spec.pos);
            if (spec.key is not null && pos is Vector2 p)
            {
                _campLights.AddFixed(spec.key, p.X, p.Y);
            }
        }
    }

    /// <summary>
    /// 营地某点合成局部光照 L∈[0,1]（环境光与固定光源+手持光源取 max），供袭营敌人 <c>ConeFor</c> 消费。
    /// 敌人生成处经 <c>ConfigurePerception(localLightAt: SampleCampLight)</c> 注入——灯/火堆/持光幸存者附近敌人视野更远更宽。
    /// </summary>
    private float SampleCampLight(Vector2 pos)
        => VisionLogic.CombineLight(
            VisionLogic.AmbientLight(_clock.CurrentPhase, indoorsDark: false),
            _campLights.StrongestAt(pos.X, pos.Y, CurrentHandheldLights()));

    /// <summary>本帧持光幸存者的手持光源快照（复用 <see cref="_handheldScratch"/> 去逐次 alloc）：喂 <see cref="LightField.StrongestAt(float,float,IEnumerable{PlacedLight})"/> 让手电/火把也照亮局部。</summary>
    private IEnumerable<PlacedLight> CurrentHandheldLights()
    {
        _handheldScratch.Clear();
        foreach (Pawn p in _survivors)
        {
            if (p.Alive && p.HeldLight.Held is LightProfile lp)
            {
                Vector2 pos = p.GlobalPosition;
                _handheldScratch.Add(new PlacedLight(pos.X, pos.Y, lp));
            }
        }
        return _handheldScratch;
    }

    // ---------------- D1 岗位建造/放置 ----------------

    /// <summary>读 camp.json 预置岗位一次性建起（首次导航烘焙前，实心结构随之进 _navHoles）。</summary>
    private void BuildGuardPosts()
    {
        _guardPosts.Clear();
        foreach (GuardPostSpec spec in _cfg.guardPosts ?? System.Array.Empty<GuardPostSpec>())
        {
            if (ToVec(spec.stand) is { } stand)
            {
                PlaceGuardPost(ParseKind(spec.kind), stand, rebake: false);
            }
        }
    }

    /// <summary>
    /// 放置一个岗位：实心类型（哨塔/屋顶平台）在站位南侧（朝大门侧）建实心结构块——挡路+挖导航洞+立体视觉，
    /// 站位本身留空地可寻路；非碰撞类型（暗哨）只落一个矮标记，不挡路。<paramref name="rebake"/> 用于运行时放置后重烘焙。
    /// </summary>
    private void PlaceGuardPost(GuardPostKind kind, Vector2 stand, bool rebake)
    {
        GuardPostStats stats = GuardPostStats.For(kind);
        var post = new GuardPostInstance
        {
            Kind = kind,
            Stats = stats,
            StandPos = stand,
            Name = $"{stats.DisplayName}{Circled(_guardPosts.Count + 1)}",
        };

        if (stats.IsSolid)
        {
            // 结构块偏移到站位南侧，避免困住守卫站位（站位保持可寻路）。
            Rect2 structRect = new(stand.X - 24f, stand.Y + 30f, 48f, 48f);
            var style = new PixelStyle
            {
                color = kind == GuardPostKind.Watchtower
                    ? new[] { 0.34, 0.30, 0.24 }
                    : new[] { 0.30, 0.32, 0.36 },
                jitter = 0.10,
            };
            AddSolid(structRect, style, seed: 53, (float)_heights.wall, cell: 48f);
        }
        else
        {
            AddHiddenPostMarker(stand);
        }

        _guardPosts.Add(post);

        // 运行时放置改了 _navHoles（仅实心岗位）→ 必须重烘焙，否则新障碍不挡寻路（已知隐坑）。
        if (rebake && stats.IsSolid)
        {
            RebakeNavigation();
        }
    }

    /// <summary>
    /// 建一把座位家具：非实心可站点（照暗哨标记路径——<b>不建 StaticBody 碰撞、不入 _navHoles 导航洞</b>，
    /// 故导航网格在此无洞、读者能寻路直接走上就座）。只落一个矮立体视觉作椅子/坐垫，并把座位中心
    /// 登记进 <see cref="_seats"/> 供读书等指派活动就近认领。座位建造经此、非 <see cref="AddSolid"/>。
    /// </summary>
    private void AddSeat(Rect2 rect, PixelStyle style)
    {
        // 矮立体块视觉（无碰撞、不挖洞）：height 取小值，读者站上时与之交叠即"坐下"观感（MVP）。
        AddOccluderVisual(rect, style, seed: 67, height: 12f, cell: 48f);
        Vector2 center = rect.Position + rect.Size / 2;
        _seats.Add(center.X, center.Y);
    }

    /// <summary>一次座位认领：座位下标 + 其世界坐标（cartesian，读者据此寻路走去坐下）。</summary>
    public readonly record struct SeatClaim(int Index, Vector2 Pos);

    /// <summary>
    /// 就近认领一个空座并标记占用（供读书等指派活动给读者派座）：返回该座世界坐标（寻路目标）；
    /// 无空座返回 null，调用方按"无座"惩罚处理（如读速 -10%）。读完/中断须 <see cref="ReleaseSeat"/> 释放。
    /// </summary>
    public SeatClaim? ClaimNearestFreeSeat(Vector2 fromPos)
    {
        int idx = _seats.ClaimNearest(fromPos.X, fromPos.Y);
        if (idx < 0)
        {
            return null;
        }
        (double x, double y) = _seats.PositionOf(idx);
        return new SeatClaim(idx, new Vector2((float)x, (float)y));
    }

    /// <summary>释放先前认领的座位（读完/中断调用；重复释放幂等）。</summary>
    public void ReleaseSeat(SeatClaim seat) => _seats.Release(seat.Index);

    /// <summary>暗哨站位标记：非碰撞、不挖洞的矮地面标记（纯视觉，深色小块）。</summary>
    private void AddHiddenPostMarker(Vector2 stand)
    {
        var style = new PixelStyle { color = new[] { 0.22, 0.20, 0.16 }, jitter = 0.15 };
        var marker = new Rect2(stand.X - 16f, stand.Y - 16f, 32f, 32f);
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = marker, Style = style, Seed = 59, Cell = 32f, ZIndex = ZThreshold,
        });
    }

    /// <summary>调试放置：把当前轮换类型的岗位放到鼠标反投影落点，随后类型轮换（B 键触发）。</summary>
    private void PlaceGuardPostAtMouse()
    {
        Vector2 cart = Iso.Unproject(GetGlobalMousePosition());
        PlaceGuardPost(_debugPlaceKind, cart, rebake: true);
        GD.Print($"[Raid] 调试放置岗位 {_debugPlaceKind} @ {cart:0}（下次轮换）。");
        _debugPlaceKind = (GuardPostKind)(((int)_debugPlaceKind + 1) % 3);
    }

    private static GuardPostKind ParseKind(string? s) => s switch
    {
        "roof" or "roofPlatform" => GuardPostKind.RoofPlatform,
        "hidden" or "hiddenPost" => GuardPostKind.HiddenPost,
        _ => GuardPostKind.Watchtower,
    };

    private static string Circled(int n) =>
        n is >= 1 and <= 9 ? ((char)('①' + n - 1)).ToString() : n.ToString();

    /// <summary>建筑：地基菱形 + 四面墙立面（门侧留缺口，室内可寻路）+ 门框门槛 + 屋顶淡出。</summary>
    private void BuildBuilding(BuildingSpec b)
    {
        if (ToRect(b.rect) is not { } foot)
        {
            return;
        }
        float wt = b.wallThickness > 0 ? (float)b.wallThickness : 18f;
        float wallH = b.wallHeight > 0 ? (float)b.wallHeight : (float)_heights.wall;

        // 地基/地板（平铺，比墙暗，屋顶半透时可见）。
        var floorStyle = new PixelStyle { color = ScaleColor(b.wallColor, 0.6f), jitter = 0.04 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = foot, Style = floorStyle, Seed = 31, Cell = 56f, ZIndex = ZFloor,
        });

        // 墙立面。
        var wallStyle = new PixelStyle { color = b.wallColor, jitter = 0.10 };
        foreach (Rect2 seg in WallSegments(foot, wt, b.door))
        {
            AddSolid(seg, wallStyle, seed: 23, wallH, CellWall);
        }

        // 门可见：门槛地面色带 + 两侧门柱。
        AddDoorDecor(foot, wt, wallH, b.door, b.wallColor);

        if (b.roof)
        {
            // 屋顶：抬到墙高的顶面（无裙边），恒顶层，RoofFade 目标。
            var roofStyle = new PixelStyle { color = b.roofColor, jitter = 0.09 };
            var roof = new IsoTilePanel
            {
                FootprintCart = foot, Style = roofStyle, Seed = 29, Cell = 44f,
                Height = wallH, Facade = false, ZIndex = ZRoof,
            };
            _isoLayer.AddChild(roof);

            // 淡出触发区（Area2D）在 logic 层，cartesian 平面探测幸存者，跨层淡出 iso 屋顶。
            var fade = new RoofFade { Position = foot.Position + foot.Size / 2 };
            Rect2 interiorLocal = new(
                -foot.Size / 2 + new Vector2(wt, wt),
                foot.Size - new Vector2(wt * 2, wt * 2));
            _logicLayer.AddChild(fade);
            fade.Setup(roof, interiorLocal);
        }

        _navTargets.Add((b.name ?? "建筑", foot.GetCenter()));
    }

    /// <summary>门框门槛：门缝处铺一条区分色地面 + 缝两端各一根门柱（纯视觉，不挡路）。</summary>
    private void AddDoorDecor(Rect2 foot, float wt, float wallH, DoorSpec? door, double[]? wallColor)
    {
        if (door is not { } d || d.gapWidth <= 0)
        {
            return;
        }
        float gapStart = (float)d.gapStart, gapW = (float)d.gapWidth;
        const float postW = 12f;
        Rect2 gap, postA, postB;

        switch (d.side)
        {
            case "south":
                gap = new Rect2(foot.Position.X + gapStart, foot.End.Y - wt, gapW, wt);
                postA = new Rect2(gap.Position.X - postW, gap.Position.Y, postW, wt);
                postB = new Rect2(gap.End.X, gap.Position.Y, postW, wt);
                break;
            case "north":
                gap = new Rect2(foot.Position.X + gapStart, foot.Position.Y, gapW, wt);
                postA = new Rect2(gap.Position.X - postW, gap.Position.Y, postW, wt);
                postB = new Rect2(gap.End.X, gap.Position.Y, postW, wt);
                break;
            case "west":
                gap = new Rect2(foot.Position.X, foot.Position.Y + gapStart, wt, gapW);
                postA = new Rect2(gap.Position.X, gap.Position.Y - postW, wt, postW);
                postB = new Rect2(gap.Position.X, gap.End.Y, wt, postW);
                break;
            case "east":
                gap = new Rect2(foot.End.X - wt, foot.Position.Y + gapStart, wt, gapW);
                postA = new Rect2(gap.Position.X, gap.Position.Y - postW, wt, postW);
                postB = new Rect2(gap.Position.X, gap.End.Y, wt, postW);
                break;
            default:
                return;
        }

        // 门槛地面色带（醒目木色），略高于地基。
        var sill = new PixelStyle { color = new[] { 0.62, 0.50, 0.30 }, jitter = 0.06 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = gap, Style = sill, Seed = 41, Cell = 40f, ZIndex = ZThreshold,
        });

        // 门柱（矮立体块，纯视觉；深木色）。
        var postStyle = new PixelStyle { color = ScaleColor(wallColor, 0.5f), jitter = 0.05 };
        _isoLayer.AddChild(new IsoTilePanel { FootprintCart = postA, Style = postStyle, Seed = 43, Cell = 48f, Height = wallH, Facade = true, ZIndex = ZWorld });
        _isoLayer.AddChild(new IsoTilePanel { FootprintCart = postB, Style = postStyle, Seed = 47, Cell = 48f, Height = wallH, Facade = true, ZIndex = ZWorld });
    }

    /// <summary>由 footprint + 门规格生成四面墙矩形，门所在边裂成两段留出缺口。</summary>
    private static List<Rect2> WallSegments(Rect2 foot, float t, DoorSpec? door)
    {
        string side = door?.side ?? "";
        float gapStart = door is { } d ? (float)d.gapStart : 0f;
        float gapW = door is { } d2 ? (float)d2.gapWidth : 0f;

        float x = foot.Position.X, y = foot.Position.Y, w = foot.Size.X, h = foot.Size.Y;
        var segs = new List<Rect2>();

        // 北 / 南（水平边），门在该边时沿 X 裂成左右两段。
        AddHWall(segs, new Rect2(x, y, w, t), side == "north" ? gapStart : -1, gapW, x);
        AddHWall(segs, new Rect2(x, y + h - t, w, t), side == "south" ? gapStart : -1, gapW, x);

        // 西 / 东（竖直边，去掉与横墙重叠的上下角），门在该边时沿 Y 裂成上下两段。
        AddVWall(segs, new Rect2(x, y + t, t, h - 2 * t), side == "west" ? gapStart : -1, gapW, y + t);
        AddVWall(segs, new Rect2(x + w - t, y + t, t, h - 2 * t), side == "east" ? gapStart : -1, gapW, y + t);

        return segs;
    }

    // 水平墙：gapStart<0 表示无门（整段）；否则从 footX+gapStart 起裂成左右两段。
    private static void AddHWall(List<Rect2> segs, Rect2 wall, float gapStart, float gapW, float footX)
    {
        if (gapStart < 0 || gapW <= 0)
        {
            segs.Add(wall);
            return;
        }
        float doorX = footX + gapStart;
        var left = new Rect2(wall.Position.X, wall.Position.Y, doorX - wall.Position.X, wall.Size.Y);
        var right = new Rect2(doorX + gapW, wall.Position.Y, wall.End.X - (doorX + gapW), wall.Size.Y);
        if (left.Size.X > 1) segs.Add(left);
        if (right.Size.X > 1) segs.Add(right);
    }

    // 竖直墙：gapStart<0 表示无门；否则从 footY+gapStart 起裂成上下两段。
    private static void AddVWall(List<Rect2> segs, Rect2 wall, float gapStart, float gapW, float footY)
    {
        if (gapStart < 0 || gapW <= 0)
        {
            segs.Add(wall);
            return;
        }
        float doorY = footY + gapStart;
        var top = new Rect2(wall.Position.X, wall.Position.Y, wall.Size.X, doorY - wall.Position.Y);
        var bottom = new Rect2(wall.Position.X, doorY + gapW, wall.Size.X, wall.End.Y - (doorY + gapW));
        if (top.Size.Y > 1) segs.Add(top);
        if (bottom.Size.Y > 1) segs.Add(bottom);
    }

    /// <summary>
    /// 加一堵实心矩形：LogicLayer 放 StaticBody2D（墙层，cartesian，挡角色 + 导航挖洞，整块一体），
    /// IsoLayer 放对应的**切块抬起立体块**视觉（逐块 YSort）。物理与视觉解耦、坐标各自平面。
    /// </summary>
    private void AddSolid(Rect2 rect, PixelStyle style, int seed, float height, float cell)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        _logicLayer.AddChild(body);

        AddOccluderVisual(rect, style, seed, height, cell);
        _navHoles.Add(rect);
    }

    /// <summary>把一个 cartesian 矩形切成 ≤cell 的小块，每块一个抬起立体块节点，逐块 YSort。
    /// <paramref name="collect"/> 非空时把每块视觉节点登记进去（供可破坏结构摧毁时逐块移除）。</summary>
    private void AddOccluderVisual(Rect2 rect, PixelStyle style, int seed, float height, float cell,
        List<Node2D>? collect = null)
    {
        int nx = Mathf.Max(1, Mathf.CeilToInt(rect.Size.X / cell));
        int ny = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y / cell));
        float cw = rect.Size.X / nx, ch = rect.Size.Y / ny;

        for (int iy = 0; iy < ny; iy++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                var sub = new Rect2(rect.Position.X + ix * cw, rect.Position.Y + iy * ch, cw, ch);
                var panel = new IsoTilePanel
                {
                    FootprintCart = sub,
                    Style = style,
                    Seed = seed + ix * 7 + iy * 13,
                    Cell = Mathf.Min(cell, 48f),
                    Height = height,
                    Facade = true,
                    ZIndex = ZWorld,
                };
                _isoLayer.AddChild(panel);
                collect?.Add(panel);
            }
        }
    }

    // ---------------- 可破坏结构（围栏/大门）：HP + 摧毁开口 ----------------

    /// <summary>
    /// 加一处可破坏结构（围栏）：与 <see cref="AddSolid"/> 同构建实心碰撞 + 切块立体视觉 + 导航洞，
    /// 但把碰撞体/视觉块/rect 记进 <see cref="CampStructureInstance"/>（含 HP 状态），供 HP→0 摧毁时逐一清场并重烘焙开口。
    /// </summary>
    private CampStructureInstance AddStructure(Rect2 rect, PixelStyle style, int seed, float height, float cell,
        StructureTier tier, bool blocking)
    {
        var inst = new CampStructureInstance
        {
            State = new CampStructureState(tier),
            Rect = rect,
            Blocking = blocking,
        };

        if (blocking)
        {
            var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
            body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
            body.CollisionMask = 0;
            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
            _logicLayer.AddChild(body);
            inst.Body = body;
            _navHoles.Add(rect);
        }

        AddOccluderVisual(rect, style, seed, height, cell, inst.Visuals);
        _structures.Add(inst);
        return inst;
    }

    /// <summary>
    /// 加一扇大门（可破坏结构，HP=基础大门 250）：**默认关闭**——和围栏一样建成实心屏障
    /// （StaticBody2D 碰撞 + 挖导航洞 + 切块立体视觉），纳入 <see cref="CampStructureInstance"/> 体系，
    /// 可被攻击摧毁（HP→0 移碰撞 + 重烘焙开出缺口，敌人穿破口涌入）。门下先铺一条门槛地面色带作视觉区分。
    /// 门控（花料/交互开关门）机制后续做。
    /// </summary>
    private void AddGate(Rect2 rect)
    {
        // 门槛地面色带（纯视觉，铺在门体之下，随门体摧毁一并清场）。
        double[] sillColor = _cfg.gateMarker?.color ?? new[] { 0.35, 0.28, 0.18 };
        var sill = new IsoTilePanel
        {
            FootprintCart = rect,
            Style = new PixelStyle { color = sillColor, jitter = 0.06 },
            Seed = 61,
            Cell = 40f,
            ZIndex = ZThreshold,
        };
        _isoLayer.AddChild(sill);

        // 门体：实心可破坏结构（用门体色，比围栏略高一点以示区分）。
        var gateStyle = new PixelStyle { color = sillColor, jitter = 0.08 };
        CampStructureInstance inst = AddStructure(
            rect, gateStyle, seed: 61, (float)_heights.post, CellFence,
            StructureTier.GateBasic, blocking: true);
        inst.Visuals.Add(sill); // 摧毁时门槛随门体一并 QueueFree
    }

    /// <summary>
    /// 结构攻击接口（供后续袭营/守卫块让敌人打围栏/大门）：对某结构施加伤害，HP→0 摧毁并开口。
    /// 返回该击是否摧毁了结构。敌人主动攻击结构的 AI 属袭营块（后续），本块只提供承伤入口 + 摧毁开口实现。
    /// </summary>
    private bool DamageStructure(CampStructureInstance s, int amount)
    {
        if (s.Removed)
        {
            return false;
        }
        bool destroyed = s.State.TakeDamage(amount);
        if (destroyed)
        {
            DestroyStructure(s);
        }
        return destroyed;
    }

    /// <summary>结构摧毁：移除碰撞体 + 视觉块 + 导航洞并重烘焙（仅阻挡型动导航），开出缺口供敌人穿过破口。</summary>
    private void DestroyStructure(CampStructureInstance s)
    {
        if (s.Removed)
        {
            return;
        }
        s.Removed = true;

        if (s.Body != null && IsInstanceValid(s.Body))
        {
            s.Body.QueueFree();
        }
        foreach (Node2D v in s.Visuals)
        {
            if (IsInstanceValid(v))
            {
                v.QueueFree();
            }
        }
        s.Visuals.Clear();

        if (s.Blocking)
        {
            _navHoles.Remove(s.Rect); // Rect2 值相等；移除后重烘焙在此处补出缺口
            RebakeNavigation();
        }
        GD.Print($"[结构] {s.State.Kind} 被摧毁 @ {s.Rect.Position:0}，已开出缺口（敌人可穿过）。");
    }

    /// <summary>就近取一处未摧毁结构（供敌人选定攻击目标 / 调试打击）：中心距落点最近且在半径内者。</summary>
    private CampStructureInstance? NearestStructure(Vector2 cart, float radius)
    {
        CampStructureInstance? best = null;
        float bestD = radius;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed)
            {
                continue;
            }
            float d = s.Rect.GetCenter().DistanceTo(cart);
            if (d < bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>
    /// 破防 AI 用：按**边缘距离**（非中心）在 <paramref name="radius"/> 内取离 <paramref name="from"/> 最近的未毁结构，
    /// 输出其最近边缘点与边缘距离。长围栏中心可能很远但边缘就在敌人脸上——故用边缘距离（<see cref="BreachLogic"/>）。
    /// 委托给 <see cref="BreachController"/>（结构类型不外泄）。
    /// </summary>
    private bool TryFindBreachTarget(Vector2 from, float radius, out Vector2 edgePoint, out float edgeDistance)
    {
        edgePoint = from;
        edgeDistance = float.PositiveInfinity;
        CampStructureInstance? best = NearestStructureByEdge(from, radius, out float bestDist, out Vector2 bestEdge);
        if (best == null)
        {
            return false;
        }
        edgePoint = bestEdge;
        edgeDistance = bestDist;
        return true;
    }

    /// <summary>破防 AI 用：对 <paramref name="from"/> 附近 <paramref name="radius"/> 内边缘最近的未毁结构施伤，返回是否本击摧毁。</summary>
    private bool DamageNearestStructureAt(Vector2 from, float radius, int amount)
    {
        CampStructureInstance? s = NearestStructureByEdge(from, radius, out _, out _);
        return s != null && DamageStructure(s, amount);
    }

    /// <summary>按边缘距离取最近未毁结构 + 其最近边缘点（供破防择目标/施伤共用）。</summary>
    private CampStructureInstance? NearestStructureByEdge(Vector2 from, float radius, out float bestDist, out Vector2 bestEdge)
    {
        CampStructureInstance? best = null;
        bestDist = radius;
        bestEdge = from;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed)
            {
                continue;
            }
            (double ex, double ey) = BreachLogic.NearestPointOnRect(
                from.X, from.Y, s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y);
            float d = (float)BreachLogic.Distance(from.X, from.Y, ex, ey);
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
                bestEdge = new Vector2((float)ex, (float)ey);
            }
        }
        return best;
    }

    /// <summary>调试（F 键）：对鼠标落点最近的结构打 50 伤害，验证承伤/摧毁→开口→重烘焙链路。</summary>
    private void DebugDamageStructureAtMouse()
    {
        Vector2 cart = Iso.Unproject(GetGlobalMousePosition());
        CampStructureInstance? s = NearestStructure(cart, 300f);
        if (s == null)
        {
            GD.Print("[结构] 鼠标附近无可破坏结构。");
            return;
        }
        bool destroyed = DamageStructure(s, 50);
        GD.Print($"[结构] 调试打击 {s.State.Kind} -50 → HP {s.State.Hp}/{s.State.MaxHp}" +
                 (destroyed ? "（摧毁！已开口）" : ""));
    }

    /// <summary>纯碰撞的边界墙（LogicLayer，无视觉、不登记导航洞——压在地图边缘，山体已覆盖）。</summary>
    private void AddBorderWall(Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100;
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        _logicLayer.AddChild(body);
    }

    private void BuildNavigation()
    {
        var region = new NavigationRegion2D();
        region.NavigationPolygon = BakeNavPoly();
        // 导航区直接挂根节点（cartesian 平面），避开 LogicLayer 不可见性的任何耦合顾虑。
        AddChild(region);
        _campNavRegion = region;
    }

    /// <summary>由当前 _navHoles 烘焙一张导航网格（挖墙洞）。初次建图与运行时放置岗位后重烘焙共用。</summary>
    private NavigationPolygon BakeNavPoly()
    {
        var navPoly = new NavigationPolygon { AgentRadius = 14f };

        Rect2 outer = new(
            _mapBounds.Position + new Vector2(_navInset, _navInset),
            _mapBounds.Size - new Vector2(_navInset * 2, _navInset * 2));

        // 门缝修复根因：B0（与原 Main.cs）用 navPoly.AddOutline + 空 source geometry 烘焙——
        // 这套**根本不挖洞**（AddOutline 的洞被 BakeFromSourceGeometryData 忽略），于是整张地图是一块
        // 无洞网格，寻路走直线穿墙、agent 撞 StaticBody 卡死在墙角找不到门。
        // 正解：把可行走区作 traversable outline、每堵墙作 obstruction outline 喂给 source geometry，
        // 再烘焙——真正在网格里挖出墙洞，门缝成为唯一通路。
        var src = new NavigationMeshSourceGeometryData2D();
        src.AddTraversableOutline(RectPoints(outer));
        foreach (Rect2 hole in _navHoles)
        {
            // 外扩仅 2px（B0 若用 4px + gapWidth 64 会把门缝吃到寻路进不去；本轮 gapWidth 已加宽到 100）。
            src.AddObstructionOutline(RectPoints(hole.Grow(2f)));
        }

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, src);
        return navPoly;
    }

    /// <summary>
    /// 运行时放置实心岗位改了 _navHoles 后重烘焙导航（已知隐坑：不重烘焙则新障碍不挡寻路）。
    /// nav region 同步有一帧滞后，_navTested 复位以在下个就绪帧重跑连通性自检。
    /// </summary>
    private void RebakeNavigation()
    {
        _campNavRegion.NavigationPolygon = BakeNavPoly();
        _navTested = false;
    }

    /// <summary>
    /// 门缝连通性自检：从营地中心到各建筑室内求路。既查终点能否抵达室内，又沿路径采样验证
    /// **不穿墙**（穿墙 = 导航洞没生效 → 寻路走直线撞墙卡死，正是用户反馈的 bug）。
    /// </summary>
    private void VerifyNavConnectivity()
    {
        Rid map = GetWorld2D().NavigationMap;
        Vector2 start = _cameraCenter;
        foreach ((string name, Vector2 target) in _navTargets)
        {
            Vector2[] path = NavigationServer2D.MapGetPath(map, start, target, true);
            float endDist = path.Length > 0 ? path[^1].DistanceTo(target) : -1f;
            bool reaches = path.Length > 0 && endDist < 40f;
            bool crossesWall = PathCrossesWall(path);
            bool ok = reaches && !crossesWall;
            GD.Print($"[Nav] 门缝连通 {name}: {(ok ? "OK" : "FAIL")}  终点距室内 {endDist:0.0}px，" +
                     $"路径点 {path.Length}，穿墙 {(crossesWall ? "是(导航洞失效!)" : "否")}");
        }
    }

    /// <summary>沿折线密集采样，任一采样点落在墙矩形内 → 路径穿墙（导航洞未生效）。</summary>
    private bool PathCrossesWall(Vector2[] path)
    {
        for (int i = 0; i + 1 < path.Length; i++)
        {
            Vector2 a = path[i], b = path[i + 1];
            int steps = Mathf.Max(1, (int)(a.DistanceTo(b) / 8f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 pt = a.Lerp(b, (float)s / steps);
                foreach (Rect2 w in _navHoles)
                {
                    if (w.HasPoint(pt))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // ---------------- 生成单位 ----------------

    private void SpawnActors()
    {
        foreach (SpawnSpec s in _cfg.spawns ?? System.Array.Empty<SpawnSpec>())
        {
            Color color = ToColor(s.color, new Color(0.5f, 0.7f, 0.9f));
            var p = Pawn.Create(s.name ?? "幸存者", s.pistol, color);
            if (ToVec(s.pos) is { } pos)
            {
                p.Position = pos; // cartesian
            }
            AddActor(p);
            _survivors.Add(p);
        }
    }

    private void AddActor(Actor actor)
    {
        actor.Inject(_combat, _clock);
        actor.Died += OnActorDied;
        _actorLayer.AddChild(actor); // LogicLayer 下（不可见，仅物理/寻路）
        // 角色视觉（iso 层的人形 ActorSprite）由 B2 接管：Actor 自行经 "iso_layer" group 挂载。
    }

    /// <summary>
    /// 口袋狗衣负重接缝（③）：本次探索若带上布鲁斯，其穿戴的口袋狗衣提供的额外携带容量（无则 0）。
    /// 容量数据/查询由 dog-gear 落定（<see cref="Dog.CarryCapacity"/>＝DogApparelSlots.TotalCarryCapacity）。
    /// TODO：探索搜刮的负重上限系统（Loadout 体系的探索消费方）尚未建立——此值为**已就位的留口**，
    /// 待该负重系统落地后由其叠加到队伍装载上限（届时调本访问器即可，无需再动布鲁斯侧）。
    /// </summary>
    public float ExpeditionDogCarryBonus =>
        _bruceExpedition && _bruce is { Alive: true } b ? b.CarryCapacity : 0f;

    /// <summary>
    /// 己方可被敌人攻击的单位池（幸存者 Pawn + 布鲁斯）。喂给丧尸/劫掠者的目标 provider，使布鲁斯也能被
    /// 敌人锁定攻击（"可被攻击可被杀"，狠辣世界观）。仅返回存活者。
    /// </summary>
    private IEnumerable<Actor> AlliedTargets()
    {
        foreach (Pawn s in _survivors)
            if (s.Alive)
                yield return s;
        if (_bruce is { Alive: true })
            yield return _bruce;
    }

    /// <summary>
    /// 布鲁斯自主缠斗的潜在敌对单位池（营地：袭营丧尸 + 教学劫掠者 + 克莉丝汀；探索关：关内丧尸等）。
    /// 狗自行按 <see cref="Factions.IsHostile"/> 过滤。营地/探索互斥（探索期营地列表空、营地期无关卡），故并列产出安全。
    /// </summary>
    private IEnumerable<Actor> CurrentHostiles()
    {
        foreach (Zombie z in _raidZombies)
            yield return z;
        foreach (Raider r in _tutorialRaiders)
            yield return r;
        foreach (Raider r in _nightRaiders) // 批次6 夜袭者：入敌对池供视野揭示 + 犬/守卫锁敌
            yield return r;
        if (_christine != null)
            yield return _christine;
        // 随队出探索时，布鲁斯的敌对目标来自当前关卡（营地敌对列表此时为空）。
        if (_currentLevel != null)
            foreach (Actor h in _currentLevel.LevelHostiles())
                yield return h;
    }

    // ---------------- 羁绊接线辅助（synergy-wiring，批次5）----------------

    /// <summary>道格&布鲁斯羁绊等级（1/2/3），由共同存活天数现算。未入队时值无意义（仅道格/布鲁斯本人消费其技能）。</summary>
    private int BondLevel => DougBruceBond.EvaluateLevel(_bondDaysBothAlive);

    /// <summary>
    /// 羁绊升级推进：共同存活天数每昼夜 +1，仅两者皆存活时累加（任一死亡即冻结＝停止累加，各技能再经 alive 门控失效）。
    /// 在 <see cref="OnGamePhaseChanged"/> 的黎明聚餐分支（每昼夜一次，紧随 <see cref="AdvanceSurvivorsHealthDay"/>）调用。
    /// </summary>
    private void AdvanceBondDay()
    {
        if (_doug is { Alive: true } && _bruce is { Alive: true })
            _bondDaysBothAlive++;
    }

    /// <summary>
    /// 3 级光环当前生效态：等级≥3、道格布鲁斯皆存活、互相距离 ≤ <see cref="DougBruceBond.DefaultAuraRadius"/> 时激活
    /// （生产 ×1.10、受伤 ×0.90）；否则中性（两系数 1.0）。一方死亡即永失（_doug/_bruce 死时置 null）。
    /// </summary>
    private AuraEffect BondAuraNow()
    {
        if (_doug is not { Alive: true } doug || _bruce is not { Alive: true } bruce)
            return AuraEffect.Inactive;
        float dist = doug.GlobalPosition.DistanceTo(bruce.GlobalPosition);
        return DougBruceBond.AuraActive(BondLevel, bothAlive: true, dist, DougBruceBond.DefaultAuraRadius);
    }

    /// <summary>
    /// 光环生产系数（供 craft-worktime 工时推进消费，见 [HANDOFF]）：工人是道格且羁绊光环激活 → <see cref="DougBruceBond.AuraProductionMult"/>(1.10)，
    /// 否则 1.0。布鲁斯非 Pawn 不进工作台，实际仅道格受益，符合「二者生产 ×1.10」。调用方把它乘进喂 CraftingJob.Advance 的分钟数。
    /// </summary>
    public float BondProductionMultFor(Pawn crafter)
        => crafter == _doug ? BondAuraNow().ProductionMult : 1f;

    /// <summary>营地视野观察者：在营存活幸存者（含道格）＋布鲁斯（犬类，其机敏也向玩家揭示敌人）。</summary>
    private IEnumerable<Actor> CampViewers()
    {
        foreach (Pawn p in _survivors)
            if (p.Alive)
                yield return p;
        if (_bruce is { Alive: true })
            yield return _bruce;
    }

    /// <summary>
    /// 羁绊视野系数（施到玩家侧 <see cref="VisionMask"/> 的逐观察者基础锥）：道格 1 级视角 ×1.10；
    /// 布鲁斯 1 级视角 ×1.10、2 级视距 ×1.10（皆依道格存活，经 DougBruceBond 门控）。其余观察者原样。
    /// 经 <see cref="VisionLogic.VisionCone.Scaled"/> 叠加，与光照/基准 R0 正交。
    /// </summary>
    private VisionLogic.VisionCone BondScaleCone(Actor viewer, VisionLogic.VisionCone cone)
    {
        int lv = BondLevel;
        if (viewer == _doug)
            return cone.Scaled(1f, DougBruceBond.DougAngleMult(lv));
        if (viewer == _bruce)
        {
            bool dougAlive = _doug is { Alive: true };
            return cone.Scaled(
                DougBruceBond.BruceRangeMult(lv, dougAlive),
                DougBruceBond.BruceAngleMult(lv, dougAlive));
        }
        return cone;
    }

    /// <summary>
    /// 注入道格（可入队幸存者）+ 布鲁斯（其犬类伙伴）到营地。**正史入队路径**＝南林村庄救援回营
    /// （<see cref="MaybeSpawnRescuedDougAndBruce"/> 以"饿昏迷"低饥饿调用本方法，[SPEC-B11]）；DEBUG 下 Key.G 仍保留
    /// 一条**满档即时注入**的验证路径。幂等（道格在场即跳过）。道格照常参与聚餐/读书/医疗/守卫/探索；布鲁斯跟随道格、自主缠斗、可站岗。
    /// </summary>
    /// <param name="dougHunger">道格入队时的初始饥饿刻度（默认满档；南林村庄救援传 <see cref="VillageRescue.DougEnlistHunger"/> 低档=饿昏迷）。</param>
    /// <param name="bruceHunger">布鲁斯入队时的初始饥饿刻度（默认满档；南林村庄救援传 <see cref="VillageRescue.BruceEnlistHunger"/> 低档）。</param>
    public void SpawnDougAndBruce(int dougHunger = HungerState.DefaultCap, int bruceHunger = DogHungerState.Cap)
    {
        if (_doug is { Alive: true })
            return; // 幂等：已在场不重复注入

        // 道格：普通 Pawn（持手枪，拟定待调；性格/台词/入队剧情=用户手写，不代写）。
        var doug = Pawn.Create("道格", usePistol: true, new Color(0.62f, 0.56f, 0.42f));
        doug.Position = _cameraCenter + new Vector2(-40f, 0f);
        AddActor(doug);
        _survivors.Add(doug);
        _doug = doug;

        // 布鲁斯：跟随道格——仅当二者同处一个场景（同父节点：营地 actor 层，或随队出探索时的关卡 actor 层）才贴随；
        // 道格离场而布鲁斯留守（未带狗出探索）则父节点不同→布鲁斯在营地待命。自主缠斗当前场上敌对单位。
        var bruce = Dog.Create(
            masterProvider: () =>
                _doug is { Alive: true } d && _bruce != null && d.GetParent() == _bruce.GetParent() ? d : null,
            hostileProvider: CurrentHostiles);
        bruce.Position = _cameraCenter + new Vector2(-8f, 24f);
        AddActor(bruce);
        _bruce = bruce;

        // 聚餐气泡门控用（draft·待用户细化）：布鲁斯在营存活旗标。日常/旁观气泡读它（meal_bubbles.json bruce_present）；
        // 布鲁斯身故清除 + 置 bruce_dead（见 OnActorDied 犬类分支）。运行时态，无存档故不持久化（同羁绊天数）。
        _storyFlags.Set("bruce_present", "true");

        // 羁绊技能接线（synergy-wiring，批次5）：
        // 注：2 级原「道格攻击布鲁斯目标 ×1.25 缠斗伤害」已被**用户 L2 修订**退役（doug-logic 改为
        // DougBruceBond.CanCraftDogGear 解锁狗装备制作，×1.25 条款移除，待确认可恢复）。新 2 级=狗装备门控，
        // 消费方在 dog-gear 制作系统落地后接 CanCraftDogGear，本批无该系统故 2 级伤害接线不再接（见 [DECISION]）。
        // · 3 级光环减伤：道格/布鲁斯相依为命时受伤 ×AuraDamageTakenMult(0.90)。二者各挂承伤系数。
        doug.SetIncomingDamageFactor(() => BondAuraNow().DamageTakenMult);
        bruce.SetIncomingDamageFactor(() => BondAuraNow().DamageTakenMult);
        // · 布鲁斯 2 级视距 +10%：自主缠斗侦测半径按 BruceRangeMult 缩放（依道格存活）。
        bruce.SetDetectRangeMultProvider(() => DougBruceBond.BruceRangeMult(BondLevel, _doug is { Alive: true }));

        // 入队饥饿设定（[SPEC-B11]）："饿昏迷"正史入队时把二人一狗压到低档（须靠聚餐喂回）；
        // 满档调用（DEBUG 键）时 DrainTo 仅降不升 → 无副作用。
        doug.Hunger.DrainTo(dougHunger);
        bruce.Hunger.DrainTo(bruceHunger);
        // 羁绊天数从入队日起算（本方法幂等只跑一次；显式归零以文档化"起点"，AdvanceBondDay 在道格入队前从不累加）。
        _bondDaysBothAlive = 0;

        _cardBar?.SetSurvivors(_survivors); // 道格入队：卡牌栏加卡（布鲁斯为犬类不占卡）
        GD.Print("[DougBruce] 道格 + 布鲁斯 已注入营地。");
    }

    /// <summary>
    /// 回营正史入队钩子（[SPEC-B11]）：探索队从南林村庄救出道格布鲁斯（关内已置 <see cref="VillageRescue.RescuedFlag"/>）后回营，
    /// 在此真正注入二人一狗——道格 + 布鲁斯饥饿压到"饿昏迷"低档，并置 <see cref="VillageRescue.EnlistedFlag"/> 硬守卫
    /// （注入一次，道格日后身故 _doug 置 null 也不因本钩子复注入）。判定与门控走纯逻辑 <see cref="VillageRescue.ShouldEnlistOnReturn"/>。
    /// 由 <see cref="UnloadExplorationLevel"/> 在探索队回营、营地恢复后调用。
    /// </summary>
    private void MaybeSpawnRescuedDougAndBruce()
    {
        if (!VillageRescue.ShouldEnlistOnReturn(_storyFlags))
            return;
        SpawnDougAndBruce(VillageRescue.DougEnlistHunger, VillageRescue.BruceEnlistHunger); // 饿昏迷低档
        _storyFlags.Set(VillageRescue.EnlistedFlag, "true"); // 注入一次硬守卫
        GD.Print("[DougBruce] 南林村庄救援回营 → 道格 + 布鲁斯正史入队（饿昏迷低档）。");
    }

    /// <summary>
    /// 药店护士招募对话（[SPEC-B13]，复用通用 <see cref="ChoicePanel"/>）：护士清醒可对话，探索队踏入其警戒区时弹出。
    /// 接受＝值 1 → 置 <see cref="NurseRecruit.AgreedFlag"/>（待回营注入标记 + 对话去重）+ 弹接受叙事；
    /// 婉拒＝值 0 → **不置任何旗**（可再访药店再谈）+ 弹婉拒叙事。冻结探索实时层、选完恢复。
    /// 真正的护士 Pawn 注入延到回营（见 <see cref="MaybeRecruitNurse"/>）——探索队出发时名单已定、不在关内临时增员。
    /// </summary>
    private void PromptNurseRecruit(NurseRecruitOffer offer)
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0; // 冻结探索实时层，专注对话

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            offer.Prompt,
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = offer.AcceptLabel, Description = offer.AcceptDescription,
                        Accent = new Color(0.3f, 0.6f, 0.3f) },
                new() { Value = 0, Label = offer.DeclineLabel, Description = offer.DeclineDescription,
                        Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            if (v == 1)
            {
                _storyFlags.Set(NurseRecruit.AgreedFlag, "true"); // 答应 → 待回营注入 + 相遇对话去重
                ShowDiscoveryNarrative(NurseRecruit.AcceptTitle, NurseRecruit.AcceptNarrative);
            }
            else
            {
                // 婉拒：不置任何旗 → ShouldOfferRecruitment 仍为真，可再访药店再谈。
                ShowDiscoveryNarrative(NurseRecruit.DeclineTitle, NurseRecruit.DeclineNarrative);
            }
        };
    }

    /// <summary>
    /// 回营正史入队钩子（[SPEC-B13]）：探索队在药店招募护士（关内已答应、置 <see cref="NurseRecruit.AgreedFlag"/>）后回营，
    /// 在此真正注入护士 Pawn（<see cref="SpawnNurse"/>）并置 <see cref="NurseRecruit.EnlistedFlag"/> 硬守卫（注入一次，日后身故也不复注入）。
    /// 判定与门控走纯逻辑 <see cref="NurseRecruit.ShouldEnlistOnReturn"/>。由 <see cref="UnloadExplorationLevel"/> 在探索队回营后调用。
    /// </summary>
    private void MaybeRecruitNurse()
    {
        if (!NurseRecruit.ShouldEnlistOnReturn(_storyFlags))
            return;
        SpawnNurse();
        _storyFlags.Set(NurseRecruit.EnlistedFlag, "true"); // 注入一次硬守卫
        GD.Print("[Nurse] 药店招募回营 → 南丁格尔正史入队。");
    }

    /// <summary>
    /// 注入护士 Pawn（[SPEC-B13]）：普通幸存者，医疗特长走 authored 专属 perk（<see cref="SurvivorPerks.NursePerk"/>，
    /// 由 <c>Pawn.Create</c> 按名 <see cref="NurseRecruit.NurseName"/> 授予，见 Pawn.cs）。加入 <c>_survivors</c> + 刷新卡牌栏后，
    /// 走标准管道（聚餐分粮/双班守夜/医疗面板施术者）自然生效。幂等：已在营（按名存活）则不重复注入。
    /// 姓名"南丁格尔"为占位关联名（draft·用户后改）；性格/台词=用户手写。
    /// </summary>
    private void SpawnNurse()
    {
        if (_survivors.Any(s => s.Alive && s.DisplayName == NurseRecruit.NurseName))
            return; // 幂等：已在场不重复

        var nurse = Pawn.Create(NurseRecruit.NurseName, usePistol: false, new Color(0.78f, 0.74f, 0.70f));
        nurse.Position = _cameraCenter + new Vector2(40f, 0f);
        AddActor(nurse);
        _survivors.Add(nurse);
        _cardBar?.SetSurvivors(_survivors); // 护士入队：卡牌栏加卡
        GD.Print("[Nurse] 南丁格尔 已注入营地。");
    }

#if DEBUG
    /// <summary>
    /// 调试：把库存里的狗装备（Item.Armor，RefKey∈<see cref="DogGearCatalog"/>）穿到布鲁斯身上，验证护甲进受击结算
    /// （<see cref="Dog.RefreshArmor"/> 喂 DefenderArmor）+ 口袋狗衣携带容量。每槽单件，顶替下来的旧件退回库存。
    /// 遗留（TODO dog-gear）：正式穿戴入口＝布鲁斯检视/角色面板（重 UI，待道格布鲁斯正式入队剧情落地一并做）。
    /// </summary>
    private void DebugEquipDogGearOnBruce()
    {
        if (_bruce is not { Alive: true } bruce)
        {
            _campToast.Show("布鲁斯不在场（先按 G 注入道格 + 布鲁斯）", CampToast.Bad);
            return;
        }
        var gear = _inventory.ByCategory(ItemCategory.Armor)
            .Where(i => DogGearCatalog.IsDogGear(i.RefKey))
            .ToList();
        if (gear.Count == 0)
        {
            _campToast.Show("库存无狗装备（先让道格制作五件套）", CampToast.Bad);
            return;
        }

        var worn = new List<string>();
        foreach (Item item in gear)
        {
            if (bruce.EquipGear(item.RefKey!, out string? displaced))
            {
                _inventory.Remove(item);
                worn.Add(item.DisplayName);
                if (displaced is not null)
                    _inventory.Add(Item.Armor(displaced)); // 顶替下来的旧件回库存
            }
        }
        if (worn.Count > 0)
        {
            string cap = bruce.CarryCapacity > 0 ? $"，携带容量 +{bruce.CarryCapacity:0}" : "";
            _campToast.Show($"布鲁斯穿上：{string.Join("、", worn)}{cap}", CampToast.Ok);
            GD.Print($"[DogGear] 布鲁斯穿戴 {string.Join("、", worn)}；护甲层 {bruce.Apparel.ArmorLayers().Count}");
        }
    }
#endif

    private void OnActorDied(Actor actor)
    {
        // 布鲁斯（狗）身故：移出上岗名单、断配对引用（主人跟随/敌人目标 provider 自动不再产出它）。
        // 非幸存者，不参与全灭判定。
        if (actor is Dog dog)
        {
            _raidGuardDogs.Remove(dog);
            if (_bruce == dog)
            {
                _bruce = null;
                // 聚餐气泡门控（draft·待用户细化）：布鲁斯身故 → 清在营旗标 + 置死亡旗标（道格的哀悼气泡读 bruce_dead）。
                _storyFlags.Set("bruce_present", null);
                _storyFlags.Set("bruce_dead", "true");
            }
            return;
        }

        if (actor is Pawn p)
        {
            _survivors.Remove(p);
            if (_doug == p)
            {
                _doug = null; // 道格身故：布鲁斯失去主人 → 原地待命（跟随 provider 返回 null）
                // 聚餐气泡门控（draft·待用户细化）：道格身故旗标 → 布鲁斯守空位/等门的旁观气泡读 doug_dead（须 bruce_present 仍在）。
                _storyFlags.Set("doug_dead", "true");
            }
            if (_selectedPawn == p)
                SetSelection(null); // 选中者身故：置空选中 + 收面板（ClearSelection 会移出 _selected）
            else
                _selected.Remove(p);
            _raidGuards.Remove(p); // 守卫阵亡：移出上岗名单（结算据存活数判守卫全倒）
            // 只**追加**一份死亡当刻快照供下一餐"死亡反应"气泡；不改上面 _survivors/_selected/_raidGuards 的既有清理语义。
            _recentlyDeceased.Add(PawnSnapshot.FromInspection(p.Inspect()));

            _cardBar?.SetSurvivors(_survivors); // 死亡移除：卡牌栏去掉该卡（避免显示过期名单）

            // 克莉丝汀若在请求线走完前身故：清空该支线全部 flag，彻底停播请求/离开（她已不在场）。
            if (p.DisplayName == ChristineName)
                ChristineRequestLogic.Abort(_storyFlags);

            // 玩家幸存者移出名单**之后**判全灭：无一存活 → game-over（只触发一次）。
            // 只玩家幸存者（_survivors 里的 Pawn）算数——盟友反水者/劫掠者/丧尸不进此判定。
            if (!_gameOver && GameOverCondition.AllSurvivorsDead(_survivors.Count(s => s.Alive)))
            {
                _gameOver = true;
                // 全灭结局路由（[SPEC-B11] 三结局 CG）：尸潮围攻全灭→CG①；军袭致全员在营全灭→CG②（预留，军袭本体待实装）；
                // 其余普通全灭→保留原 GameOverPanel「营地无人生还」行为。
                var kind = EndingCg.ForGameOver(_siegeActive, _militaryRaidWipeContext);
                if (kind == EndingKind.Normal)
                    GameOverPanel.Show(_hud);
                else
                    EndingPanel.Show(_hud, EndingCg.ForKind(kind),
                        kind == EndingKind.HordeSiege ? EndingCg.HordeSiegeTitle : EndingCg.MilitaryWipeTitle);
            }
        }
    }

    // ---------------- 每帧刷新 ----------------

    public override void _Process(double delta)
    {
        _ambient.Color = _clock.CurrentAmbientColor();
        bool exploring = _currentLevel != null;

        // HUD 状态行仅在内容实际变化的帧重建（脏缓存）——空闲营地不再每帧造大字符串+调 SetStatus。
        // 信号全是廉价 int/enum/bool：Count(s=>s.Alive) 的 lambda 无捕获，编译期缓存为静态委托不分配。
        int hudPhase = (int)_clock.CurrentPhase;
        int hudClockKey = _clock.ClockMinuteKey();
        bool hudPaused = _clock.Paused;
        int hudAlive = _survivors.Count(s => s.Alive);
        // 尸潮倒计时：未望见=Hidden（HUD 零痕迹），望见后常驻，到期转红警。旗标只置不撤，Has 即知情（持久）。
        HordePhase hordePhase = HordeTimeline.Evaluate(_clock.Day, _storyFlags.Has(HordeTimeline.SightedFlag));
        int hordeDays = HordeTimeline.DaysRemaining(_clock.Day);
        if (!_hudInit || exploring != _hudExploring || _clock.Day != _hudDay || hudPhase != _hudPhase
            || hudClockKey != _hudClockKey || _clock.SpeedIndex != _hudSpeedIndex
            || hudPaused != _hudPaused || hudAlive != _hudAlive
            || (int)hordePhase != _hudHordePhase || hordeDays != _hudHordeDays)
        {
            _hudInit = true;
            _hudExploring = exploring;
            _hudDay = _clock.Day;
            _hudPhase = hudPhase;
            _hudClockKey = hudClockKey;
            _hudSpeedIndex = _clock.SpeedIndex;
            _hudPaused = hudPaused;
            _hudAlive = hudAlive;
            _hudHordePhase = (int)hordePhase;
            _hudHordeDays = hordeDays;
            _hud.SetStatus(
                $"{(exploring ? "探索" : "营地")}  第 {_clock.Day} 天  {_clock.ClockString()}  [{_clock.CurrentPhase}]   速度 {_clock.SpeedLabel()}   " +
                $"幸存者 {hudAlive}");
            _hud.SetHordeCountdown(
                hordePhase != HordePhase.Hidden,
                hordePhase == HordePhase.Arrived,
                hordeDays);
        }

        if (_returnWarningPopup.Visible && _clock.CurrentPhase == DayPhase.DayExplore)
            _returnWarningPopup.SetRemainingTime(_clock.GetExploreTimeRemaining());

        if (_raidActive)
            UpdateRaid(delta);

        if (_nightRaidActive)
            UpdateNightRaid(delta);

        if (_tutorialActive)
            UpdateChristineTutorial(delta);

        UpdatePendingInteract(delta);   // 右键前往：轮询到达 → 开面板/搜刮（自带暂停）
        UpdateHover();                   // 悬停辨识：鼠标下容器 → 跟随小标签

        // 卡牌栏血/饥饿条节流刷新：修「袭营中失血、饥饿推进底部条不动」。
        // delta 已被 Engine.TimeScale 缩放——面板冻结时标时 delta≈0，累计不推进＝世界冻结即不刷新（正合期望）。
        _cardStatRefreshElapsed += delta;
        if (_cardStatRefreshElapsed >= CardStatRefreshInterval)
        {
            _cardStatRefreshElapsed = 0;
            _cardBar?.RefreshStats();
        }

        // 工时制夜间生产推进：面板冻结时标时 ClockMinuteKey 不变 → 无增量 → 世界冻结即不推进（正合期望）。
        TickCraftingWorktime();

        // 废墟挖掘推进：挖掘者在场时逐游戏分钟推进；挖满→收获+清场显露空地。面板冻结时标时同样不推进。
        TickRubbleDig();

        // 导航图首帧就绪后跑一次门缝连通性自检。
        if (!_navTested)
        {
            Rid map = GetWorld2D().NavigationMap;
            if (NavigationServer2D.MapGetIterationId(map) != 0)
            {
                VerifyNavConnectivity();
                _navTested = true;
            }
        }
    }

    // ---------------- 输入：选中 / 指令 / 时间 ----------------

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
        }
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                _clock.TogglePause();
                break;
            case Key.Key1:
                _clock.SetSpeedIndex(0);
                break;
            case Key.Key2:
                _clock.SetSpeedIndex(1);
                break;
            case Key.Key3:
                _clock.SetSpeedIndex(2);
                break;
            case Key.Escape:
                // 集中派发：关闭当前打开的最上层可取消模态（时标由各自 Close 恢复）。
                // GameOver（终局）/ Choice（强制抉择）/ Guard（守夜为强制指派，无取消路径）不在此列。
                TryCloseTopModal();
                break;
            case Key.M:
                // 打开医疗面板（手术/用药）：冻结时标，对幸存者做手术、治感染/疾病。
                OpenMedical();
                break;
#if DEBUG
            case Key.B:
                // 调试：在鼠标落点放置岗位（类型轮换 哨塔→屋顶平台→暗哨），实心岗位自动重烘焙导航。
                PlaceGuardPostAtMouse();
                break;
            case Key.R:
                // 调试：手动触发一波袭营（仅 NightAct 生效），把防御战跑通；正式由叙事事件调 TriggerRaid。
                TriggerRaid(_raidIntensity);
                break;
            case Key.F:
                // 调试：打击鼠标落点最近的围栏/大门，验证承伤→摧毁→开口→重烘焙链路（敌人打结构 AI 属袭营块，后续）。
                DebugDamageStructureAtMouse();
                break;
            case Key.G:
                // 调试：满档即时注入道格 + 布鲁斯（狗）到营地，验证跟随/缠斗/站岗。
                // 正式入队＝南林村庄救援回营（MaybeSpawnRescuedDougAndBruce，低饥饿）；本键为保留的即时验证路径。
                SpawnDougAndBruce();
                break;
            case Key.H:
                // 调试：把库存里的狗装备（五件套）穿到布鲁斯身上，验证护甲进受击结算 + 携带容量（正式穿戴 UI 待做，见遗留）。
                DebugEquipDogGearOnBruce();
                break;
#endif
        }
    }

    /// <summary>
    /// ESC 集中派发：按叠放层级从上到下关闭一个可取消模态，命中即返回 true（一次 ESC 只关一层）。
    /// 全由 CampMain 自身持有的状态/关闭方法驱动，不触碰各面板脚本内部；关面板的时标恢复沿用各 Close 方法。
    /// 排除：GameOver（终局不可退）、ChoicePanel（强制抉择，无字段引用）、GuardPanel（守夜强制指派，本就无取消路径）。
    /// </summary>
    private bool TryCloseTopModal()
    {
        // 阅读面板叠在库存之上：先关它，回到库存。
        if (_readerPanel.Visible)
        {
            OnReaderClosed();
            return true;
        }
        if (_stashOpen)
        {
            CloseStash();
            return true;
        }
        if (_craftingOpen)
        {
            CloseCrafting();
            return true;
        }
        if (_medicalOpen)
        {
            CloseMedical();
            return true;
        }
        // 探索发现叙事：等价于点「继续」，恢复被冻结的时标。
        if (_discoveryPanel.Visible)
        {
            OnDiscoveryContinued();
            return true;
        }
        // 世界地图叠在远征面板之上：先关地图（等价其取消）。
        if (_worldMapPanel.Visible)
        {
            _worldMapPanel.Visible = false;
            return true;
        }
        if (_expeditionPanel.Visible)
        {
            _expeditionPanel.Visible = false;
            return true;
        }
        // 读书指派面板：等价点「取消」（本夜无人读书后照常进夜）。
        if (_readingPanel.Visible)
        {
            OnReadingCancelled();
            return true;
        }
        return false;
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb is { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            // 单选：已禁框选，一次只选一个角色（走 SetSelection）。
            FinishSelection();
        }
        else if (mb is { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            // 右键落点在 iso 地面 → 反投影回 cartesian：命中容器→前往交互，空地→移动。
            HandleRightClick(Iso.Unproject(GetGlobalMousePosition()));
        }
    }

    /// <summary>
    /// 左键点选（单选）：落点反投影回 cartesian。命中容器 → 保留当前选中不动（交互改由右键前往）；
    /// 否则半径命中最近一个可操控角色 → 单选并开面板；点空白（未命中）→ 取消选择并收面板。
    /// </summary>
    private void FinishSelection()
    {
        Vector2 cart = Iso.Unproject(GetGlobalMousePosition());

        // 左键点在容器上：不再即时开面板（交互统一走右键前往），保留当前选中，避免误取消。
        if (HitContainerAt(cart) != null)
        {
            return;
        }

        Pawn? hit = _survivors
            .Where(p => p.IsControllable && p.GlobalPosition.DistanceTo(cart) <= p.Radius + 8)
            .OrderBy(p => p.GlobalPosition.DistanceTo(cart))
            .FirstOrDefault();
        SetSelection(hit); // 命中 → 单选并开面板；未命中(null) → 取消选择并收面板
    }

    /// <summary>
    /// 右键：命中容器且有可控选中角色（且导航已就绪）→ 记 <see cref="_pendingInteract"/> 并令其走向容器最近边缘，
    /// 到达后由 <see cref="UpdatePendingInteract"/> 开面板/搜刮；命中容器但无可控选中/导航未就绪 → 忽略（hover 已提示先选中角色）；
    /// 未命中容器 → 地面移动。
    /// </summary>
    private void HandleRightClick(Vector2 cart)
    {
        ContainerRef? hit = HitContainerAt(cart);
        if (hit != null)
        {
            if (_navTested && _selectedPawn is { IsControllable: true } pawn)
            {
                Vector2 stand = NearestEdgeStandPoint(hit.Rect, pawn.GlobalPosition);
                pawn.CommandMoveTo(stand);
                _pendingInteract = (pawn, hit);
                _pendingInteractElapsed = 0;
            }
            return; // 命中容器但不满足前往条件：忽略（不把角色移到容器上）
        }
        IssueMove(cart); // 空地 → 地面移动令
    }

    private void IssueMove(Vector2 cartPos)
    {
        _pendingInteract = null; // 新的地面移动令：取消未完成的前往交互
        // 单选：仅当前主选角色接受移动指令（无选中则无操作）。
        _selectedPawn?.CommandMoveTo(cartPos);
    }

    /// <summary>容器命中查询：落点（cartesian）命中某容器则返回它，否则 null（右键前往/悬停辨识共用）。</summary>
    // 普通 for 循环替代 LINQ FirstOrDefault：后者每帧新建捕获 cart 的闭包（稳定 GC 压力）；
    // UpdateHover 每帧调本方法，改 for 后零分配，扫描一小把容器 CPU 可忽略。
    private ContainerRef? HitContainerAt(Vector2 cart)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            if (_containers[i].Rect.Grow(8f).HasPoint(cart))
            {
                return _containers[i];
            }
        }
        return null;
    }

    /// <summary>容器最近边缘外的站位点：把 pawn 位置钳进 rect 得最近边缘点，再沿朝 pawn 方向外推 standoff，站在容器外沿。</summary>
    private static Vector2 NearestEdgeStandPoint(Rect2 rect, Vector2 from)
    {
        Vector2 edge = new(
            Mathf.Clamp(from.X, rect.Position.X, rect.End.X),
            Mathf.Clamp(from.Y, rect.Position.Y, rect.End.Y));
        Vector2 outward = from - edge;
        if (outward.LengthSquared() < 0.01f)
            outward = from - rect.GetCenter(); // pawn 落在 rect 内：以中心指向外
        if (outward.LengthSquared() < 0.01f)
            outward = Vector2.Right;           // 极端退化：随便给个方向
        return edge + outward.Normalized() * PendingInteractStandoff;
    }

    /// <summary>
    /// 右键前往交互轮询：目标者到位（进容器外扩 margin 且导航结束）→ 执行开面板/搜刮（自带暂停）；
    /// 目标者变不可控/身故/离场 → 取消；导航结束却没到位（走不到）或超时 → 放弃 + 一行提示。**寻路期间不暂停**。
    /// </summary>
    private void UpdatePendingInteract(double delta)
    {
        if (_pendingInteract is not { } pend)
            return;
        Pawn pawn = pend.pawn;
        if (!pawn.Alive || !pawn.IsControllable || !_survivors.Contains(pawn))
        {
            _pendingInteract = null; // 兜底取消（SetSelection/相位切换等路径通常已提前清）
            return;
        }

        _pendingInteractElapsed += delta;
        bool arrived = pend.target.Rect.Grow(pawn.Radius + PendingArriveMargin).HasPoint(pawn.GlobalPosition);
        if (arrived && pawn.IsNavigationFinished())
        {
            _pendingInteract = null;                 // 先清，避免 ExecuteContainerInteract 内暂停后重入
            ExecuteContainerInteract(pend.pawn, pend.target);
            return;
        }
        if (_pendingInteractElapsed > PendingInteractTimeout)
        {
            _campToast.Show($"前往{pend.target.Name}超时，已放弃。", CampToast.Bad);
            _pendingInteract = null;
            return;
        }
        // 导航结束却没到位 = 走不到（首帧 nav 同步滞后误报靠 0.5s 宽限过滤）。
        if (pawn.IsNavigationFinished() && _pendingInteractElapsed > 0.5)
        {
            _campToast.Show($"走不到{pend.target.Name}。", CampToast.Bad);
            _pendingInteract = null;
        }
    }

    /// <summary>
    /// 悬停辨识：反投影鼠标 → 命中容器则跟随鼠标显示按 role 定制的一行提示（无选中角色时追加"先选中角色"）；
    /// 未命中 / 面板打开 / 探索中 → 隐藏。与 <see cref="UpdatePendingInteract"/> 各自独立，互不干扰。
    /// </summary>
    private void UpdateHover()
    {
        if (_stashOpen || _craftingOpen || _medicalOpen || _currentLevel != null)
        {
            _hud.HideHoverLabel();
            return;
        }
        ContainerRef? hit = HitContainerAt(Iso.Unproject(GetGlobalMousePosition()));
        if (hit == null)
        {
            _hud.HideHoverLabel();
            return;
        }
        _hud.ShowHoverLabel(HoverTextFor(hit, _selectedPawn != null), GetViewport().GetMousePosition());
    }

    /// <summary>容器 role → 悬停提示文案。loot 已搜过标"已搜刮"（无操作提示）；其余可交互，无选中角色时追加"（先选中角色）"。</summary>
    private string HoverTextFor(ContainerRef c, bool hasSelection)
    {
        string noSel = hasSelection ? "" : "（先选中角色）";
        return c.Role switch
        {
            "workbench" => $"工作台 · 选中角色后右键前往{noSel}",
            "radio" => $"收音机 · 选中角色后右键前往{(RadioMainline.IsDecisionAvailable(_storyFlags) ? "抉择" : "收听")}{noSel}",
            "storage" => $"储物柜 · 选中角色后右键前往{noSel}",
            "merchant" => $"神秘商人 · 选中角色后右键前往交易{noSel}",
            "rubble" => RubbleHoverText(c, noSel),
            _ => _containerLoot.IsSearched(c.Name)
                ? $"{c.Name} · 已搜刮"
                : $"{c.Name} · 选中角色后右键前往{noSel}",
        };
    }

    /// <summary>
    /// 选中的唯一写入口（单选事实源）：先清空旧选中，再选中 p（非空即 ≤1），置 <see cref="_selectedPawn"/>，
    /// 并驱动右侧角色面板（p 非空→检视，null→收起）与卡牌栏选中高亮刷新。所有改选中都必须走这里。
    /// </summary>
    private void SetSelection(Pawn? p)
    {
        _pendingInteract = null; // 改选中：取消未完成的前往交互
        ClearSelection();
        if (p != null)
        {
            Select(p);
        }
        _selectedPawn = p;

        if (p != null)
        {
            // 右侧面板：检视该角色（只读健康快照 + 装备态 + 装假肢/卸下入口）。
            ShowInspect(p);
        }
        else
        {
            _characterPanel.HidePanel(); // 取消选择 → 收起面板。
        }

        RefreshSelectionUi();
    }

    /// <summary>选中变更后刷新依赖选中态的 UI。占位：卡牌栏（C 批）接入后在此按 <see cref="_selectedPawn"/> 刷高亮。</summary>
    private void RefreshSelectionUi()
    {
        // 卡牌栏选中高亮跟随单选事实源（null → 取消全部高亮）。
        _cardBar?.SetSelected(_selectedPawn);
    }

    private void Select(Pawn p)
    {
        ClearSelection(); // 双保险：单选恒 ≤1
        _selected.Add(p);
        p.Selected = true; // actor 自身 _Draw 不可见，选中环由 iso 标记表现
    }

    private void ClearSelection()
    {
        foreach (Pawn p in _selected)
        {
            p.Selected = false;
        }
        _selected.Clear();
        _selectedPawn = null;
    }

    private void OnRolesChanged()
    {
        // 选中者转为不可操控（上岗/远征离场等）→ 取消选择 + 收面板（单选下只可能是 _selectedPawn）。
        if (_selectedPawn != null && !_selectedPawn.IsControllable)
        {
            SetSelection(null);
        }
        _cardBar?.SetSurvivors(_survivors); // 角色变更：卡牌栏重建以刷新名单/状态显示
    }

    private async void StartSleepTransition()
    {
        var sleepers = _survivors.Where(p => p.Role == PawnRole.Expedition).ToList();
        if (sleepers.Count == 0)
        {
            _clock.TransitionTo(DayPhase.NightPrep);
            return;
        }

        Engine.TimeScale = 1;

        for (int i = 0; i < sleepers.Count; i++)
        {
            Vector2 spot = SleepPositions[i % SleepPositions.Length];
            sleepers[i].CommandMoveTo(spot);
        }

        while (sleepers.Any(p => !p.IsNavigationFinished()))
        {
            await ToSignal(GetTree().CreateTimer(0.25f), "timeout");
        }

        foreach (var p in sleepers)
        {
            p.Role = PawnRole.Sleeping;
            p.SetSleeping(true);
        }

        _clock.TransitionTo(DayPhase.NightPrep);
    }

    // ---------------- 聚餐 ----------------

    /// <summary>预填分粮策略：默认"先喂最饿"——库存不足时把口粮压在濒死者身上，最大化少死人（策略/数值拟定待调）。
    /// 仅用于分配面板的预填建议（<see cref="FoodEconomy.Prefill"/>），玩家可用 +/- 覆盖。</summary>
    private RationStrategy _rationStrategy = RationStrategy.HungriestFirst;

    /// <summary>缺口趋势预警阈值（昼夜）：存货全员吃饱撑不过这么多昼夜时红字告警，逼玩家搜刮。拟定待调。</summary>
    private const int FoodShortfallWarnDays = 2;

    /// <summary>吃饭动画：被分配者走到座位/餐区后的进食时长窗口（秒，拟定待调）。</summary>
    private const double MealEatSeconds = 3.0;

    /// <summary>吃饭动画：走到座位/餐区的寻路超时兜底（秒，走不到也照常吃/冒泡，不卡死流程）。</summary>
    private const double MealSeatTimeoutSeconds = 6.0;

    /// <summary>吃饭动画期间世界气泡的随机源（坐/站冒泡掷点、无名气泡随机指派吃饭者）。</summary>
    private readonly IRandomSource _mealRng = new SystemRandomSource();

    private string _mealTitle = "";
    private string _mealPhaseTag = "";

    private void EnterDuskMeal() => _clock.TransitionTo(DayPhase.DuskMeal);

    /// <summary>
    /// 聚餐第一步（发生在昼夜相位切换点，一天两次；相位 DawnMeal/DuskMeal 已由 GameClock 冻结 TimeScale=0＝"画面暂停变暗"）：
    /// 按现行自动分粮策略预填每人份数，弹出<b>食物分配面板</b>（每人头像/名字/当前饥饿档位 + 剩余食物 + 头像旁 −/+ 微调）。
    /// 玩家确认后走 <see cref="OnMealAllocationConfirmed"/> 结算并播放吃饭动画。强制流程：面板无取消/无 ESC（不入 TryCloseTopModal）。
    /// </summary>
    private void BeginMeal(string title, string phaseTag)
    {
        _mealTitle = title;
        _mealPhaseTag = phaseTag;

        var living = _survivors.Where(s => s.Alive).ToList();
        if (living.Count == 0)
        {
            FinishMeal(); // 无人可吃（理论上全灭已在别处判定），守一手直接收尾
            return;
        }

        // 预填 = 现行自动分粮策略（先保第一餐 → 余粮补最饿），折成每人 0/1/2 份，玩家用 +/- 微调。
        var diners = living.Select(ToDiner).ToList();
        int[] prefill = FoodEconomy.Prefill(_resources.Food, diners, allowSeconds: true, _rationStrategy);

        var rows = new List<MealAllocationPanel.DinerRow>(living.Count);
        for (int i = 0; i < living.Count; i++)
        {
            Pawn p = living[i];
            rows.Add(new MealAllocationPanel.DinerRow(
                p.Id, p.DisplayName, p.Hunger.Level, p.Hunger.Cap, prefill[i], !p.Hunger.IsStarved));
        }
        _mealAllocPanel.ShowAllocation(title, _resources.Food, rows);
    }

    /// <summary>
    /// 聚餐第二步：玩家确认分配 → 结算食物/饥饿/饿死（账目"换壳不换规则"：<see cref="FoodEconomy.ResolveManual"/> 落成
    /// 与既有净零/补餐同构的 Fed/SecondFed——份数≥1 走 <c>ResolvePhase</c>（净零，进餐 −1 内嵌）、份数≥2 走
    /// <c>ServeSecondMeal</c>（净 +1）；结算后刻度归 0 者饿死）→ 选气泡 → 播放吃饭动画（<see cref="PlayMealAnimation"/>）。
    /// </summary>
    private void OnMealAllocationConfirmed(int[] servings)
    {
        _mealAllocPanel.Visible = false;

        var living = _survivors.Where(s => s.Alive).ToList();
        var diners = living.Select(ToDiner).ToList();
        PhaseMealOutcome phase = FoodEconomy.ResolveManual(_resources.Food, diners, servings);
        RationOutcome ration = phase.First;

        // 食物扣减：第一餐 + 补餐总消耗实扣（不越界）。
        _resources.Consume(phase.TotalConsumed);

        // 净结算：份数≥1（Fed）→ ResolvePhase(true) 净零维持；份数=0 → ResolvePhase(false) 净 −1 前进一级。
        for (int i = 0; i < living.Count; i++)
        {
            living[i].ResolveHungerPhase(ration.Fed[i]);
        }
        // 补餐回升：份数≥2（SecondFed）→ +1（clamp 到各自上限；饿死终态由 Feed 内部守卫）。
        for (int i = 0; i < living.Count; i++)
        {
            if (phase.SecondFed[i])
            {
                living[i].ServeSecondMeal();
            }
        }

        // 饿死：刻度归 0 者走统一死亡路径（Died 事件会改 _survivors，先收集再逐个处理）。
        foreach (var starved in living.Where(d => d.IsStarvedToDeath).ToList())
        {
            starved.StarveToDeath();
        }

        // 布鲁斯（狗）进食：**不上桌**——不入分配面板/坐席/气泡，人类分配后从余粮自动喂。每聚餐相位 -1；
        // 未饱且尚有余粮则吃 1 份（+3、扣 1 粮，用户口径）；饿死走统一死亡路径。份额出自 §4 分粮同一存货。
        if (_bruce is { Alive: true } bruce)
        {
            bool dogAte = bruce.Hunger.Value < DogHungerState.Cap && _resources.Food >= 1;
            if (dogAte)
                _resources.Consume(1);
            if (bruce.ResolveHungerPhase(dogAte))
                bruce.StarveToDeath();
        }

        // 缺口预警：本餐有人挨饿→急告；否则按存货趋势提醒还能撑几昼夜，逼玩家搜刮补给。
        WarnFoodShortfall(ration, _survivors.Count(s => s.Alive));

        // 构造"世界只读快照"喂条件驱动选择器：相位 + 当前 flags + 存活者真实状态 + 食物。
        // 在场存活者 + 近期已故者快照都放进 Pawns：前者供伤/饥饿谓词，后者供 dead 死亡反应谓词。
        var pawnSnapshots = _survivors.Where(s => s.Alive)
                                      .Select(s => PawnSnapshot.FromInspection(s.Inspect()))
                                      .Concat(_recentlyDeceased)
                                      .ToList();
        var context = new MealWorldContext
        {
            Phase = _mealPhaseTag,
            Flags = _storyFlags,
            Pawns = pawnSnapshots,
            Food = _resources.Food,
        };
        var bubbles = _bubblePool.Pick(context, 3);
        MealBubblePool.ApplyTriggers(bubbles, _storyFlags); // 施加 triggers（改 flags）推动剧情
        _recentlyDeceased.Clear(); // 死亡只在紧随其后的一餐被提及，之后归入历史不再复播

        // 吃饭动画：吃到饭者（Fed 且存活）去找座/站着吃并冒世界气泡；结束回 FinishMeal。
        var eaters = new List<Pawn>();
        for (int i = 0; i < living.Count; i++)
        {
            if (ration.Fed[i] && living[i].Alive)
            {
                eaters.Add(living[i]);
            }
        }
        PlayMealAnimation(eaters, bubbles);
    }

    /// <summary>
    /// 吃饭动画（实时渲染）：解冻世界（相位仍 DawnMeal/DuskMeal，GameClock 在此不 tick，故置 TimeScale=1 安全）→
    /// 吃到饭者就近认领空座、走过去坐下；座位不足者走到餐区边缘站着吃 → 进食窗口内按坐/站冒世界气泡
    /// （坐着必冒、站着触发概率 ×0.5＝<see cref="MealBubbleDelivery"/>，漏听线索/支线的惩罚由概率承载）→
    /// 释放座位、清 Stationing → <see cref="FinishMeal"/>。走位/寻路照搬 <see cref="StationReaders"/> 的 Stationing 放行范式。
    /// </summary>
    private async void PlayMealAnimation(List<Pawn> eaters, IReadOnlyList<MealBubble> bubbles)
    {
        if (eaters.Count == 0)
        {
            FinishMeal();
            return;
        }

        Engine.TimeScale = 1; // 解冻，让吃饭者实时走位（相位不变，时钟不推进）

        Vector2 diningAnchor = DiningAnchor();
        var claimed = new List<SeatClaim>();
        var seatedFlags = new Dictionary<Pawn, bool>();
        int standIdx = 0;

        foreach (Pawn p in eaters)
        {
            p.Stationing = true; // 放行走向座位/餐区的移动令（覆盖 Guard/Reading 角色门控；Idle 无副作用）
            SeatClaim? seat = ClaimNearestFreeSeat(p.GlobalPosition);
            if (seat is { } s)
            {
                claimed.Add(s);
                seatedFlags[p] = true;
                p.CommandMoveTo(s.Pos); // 有座：走过去坐下
            }
            else
            {
                seatedFlags[p] = false;
                p.CommandMoveTo(StandingSpot(diningAnchor, standIdx++)); // 无座：走到餐区边缘站着吃
            }
        }

        // 等所有吃饭者到位（导航完成）或超时兜底（走不到也照常吃/冒泡，不卡死流程）。
        double elapsed = 0;
        while (elapsed < MealSeatTimeoutSeconds && eaters.Any(p => p.Alive && !p.IsNavigationFinished()))
        {
            await ToSignal(GetTree().CreateTimer(0.2f), "timeout");
            elapsed += 0.2;
        }

        // 到位后冒世界气泡（坐着必冒、站着 ×0.5）。
        EmitMealBubbles(eaters, seatedFlags, bubbles);

        // 进食窗口（让气泡飘一会儿）。
        await ToSignal(GetTree().CreateTimer((float)MealEatSeconds), "timeout");

        // 释放座位 + 清 Stationing。
        foreach (SeatClaim s in claimed)
        {
            ReleaseSeat(s);
        }
        foreach (Pawn p in eaters)
        {
            p.Stationing = false;
        }

        FinishMeal();
    }

    /// <summary>餐区锚点：有座位时取所有座位坐标的质心，否则取相机中心（cartesian）。</summary>
    private Vector2 DiningAnchor()
    {
        int c = _seats.Count;
        if (c == 0)
        {
            return _cameraCenter;
        }
        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < c; i++)
        {
            (double x, double y) = _seats.PositionOf(i);
            sum += new Vector2((float)x, (float)y);
        }
        return sum / c;
    }

    /// <summary>站着吃的落点：绕餐区锚点排一圈（cartesian），idx 越大越外圈，避免重叠。</summary>
    private static Vector2 StandingSpot(Vector2 anchor, int idx)
    {
        float ang = idx * 1.05f;                 // 弧度间隔，均匀撒开
        float radius = 46f + (idx / 6) * 28f;    // 每满 6 人往外扩一圈
        return anchor + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
    }

    /// <summary>
    /// 把选中气泡分派给吃饭者并冒世界内头顶气泡：具名气泡 → 对应说话人吃饭者（在座才发声），
    /// 无名气泡 → 随机未分派吃饭者；每条按该吃饭者坐/站掷点（坐着必冒、站着 ×0.5）决定是否真的冒出来。
    /// 气泡挂 iso 可视层，坐标 <c>Iso.Project(pawn.GlobalPosition)</c> + 头顶偏移。
    /// </summary>
    private void EmitMealBubbles(List<Pawn> eaters, IReadOnlyDictionary<Pawn, bool> seated, IReadOnlyList<MealBubble> bubbles)
    {
        if (bubbles.Count == 0 || eaters.Count == 0)
        {
            return;
        }

        var assigned = new Dictionary<Pawn, MealBubble>();
        var used = new HashSet<MealBubble>();

        // 具名气泡 → 同名吃饭者（该说话人须在餐桌前才由本人口吻发声）。
        foreach (MealBubble b in bubbles)
        {
            if (string.IsNullOrEmpty(b.speaker))
            {
                continue;
            }
            Pawn? sp = eaters.FirstOrDefault(p => p.Alive
                && !assigned.ContainsKey(p)
                && string.Equals(p.DisplayName, b.speaker, StringComparison.OrdinalIgnoreCase));
            if (sp != null)
            {
                assigned[sp] = b;
                used.Add(b);
            }
        }
        // 无名气泡 → 随机未分派吃饭者。
        var free = eaters.Where(p => p.Alive && !assigned.ContainsKey(p)).ToList();
        foreach (MealBubble b in bubbles)
        {
            if (used.Contains(b) || free.Count == 0)
            {
                continue;
            }
            int pick = (int)Math.Floor(_mealRng.Range(0, free.Count - 1e-9));
            Pawn p = free[pick];
            free.RemoveAt(pick);
            assigned[p] = b;
            used.Add(b);
        }

        // 掷点冒泡（坐着必冒、站着 ×0.5）。
        var accent = new Color(0.7f, 0.6f, 0.35f);
        foreach (var kv in assigned)
        {
            Pawn p = kv.Key;
            bool isSeated = seated.TryGetValue(p, out bool sv) && sv;
            if (!MealBubbleDelivery.RollDelivered(isSeated, _mealRng))
            {
                continue; // 站着漏听：这条没冒出来
            }
            Vector2 iso = Iso.Project(p.GlobalPosition) + new Vector2(0, -46);
            MealSpeechBubble.Spawn(_isoLayer, iso, kv.Value.speaker, kv.Value.text, accent);
        }
    }

    /// <summary>
    /// 每昼夜（黎明）推进全体存活幸存者的伤病演变：建档新战伤 → 恶化/愈合 → 封顶致残(断肢)/致死。
    /// 休养判定（MVP）：整夜值岗的守卫=不休养（伤情恶化更快、术后愈合更慢），其余（睡觉/留守）=卧床休养。
    /// 致死者走统一非战斗死亡路径（Died → <see cref="OnActorDied"/> 清名单，可能触发全灭）；先收集再逐个致死，避免遍历中改名单。
    /// 值得注意的事件（新感染/截肢/身故）汇总成一行 HUD 提示。
    /// </summary>
    private void AdvanceSurvivorsHealthDay()
    {
        var living = _survivors.Where(s => s.Alive).ToList();
        var toKill = new List<Pawn>();
        var notes = new List<string>();

        // 南丁格尔护士三级特长（[SPEC-B13-补]）：营地卫生减免全营感染率——2级(她在营存活,−15%)+3级(永续遗产,−10%,合计−25%)。
        // 默认只营地内休养的伤口吃减免（本处即营地每昼夜健康推进；探索关实时层伤口不经此，故天然满足"只营地伤口吃"，标待确认）。
        Pawn? nightingale = _survivors.FirstOrDefault(s => s.Perks.IsNightingale);
        int nurseLevel = NightingalePerk.LevelOf(_storyFlags); // 等级由持久化台数派生（她死后仍在，但下方 aliveInCamp 门控 L2）
        bool nurseAliveInCamp = nightingale is { Alive: true } && nightingale.Role != PawnRole.Expedition; // 在营存活=活着且未外出探索
        bool nurseL3Legacy = _storyFlags.Has(NurseRecruit.L3LegacyFlag);
        double infectionMult = NightingalePerk.CampInfectionMultiplier(nurseLevel, nurseAliveInCamp, nurseL3Legacy);

        foreach (Pawn p in living)
        {
            bool resting = p.Role != PawnRole.Guard; // 守卫整夜值岗=不休养；其余卧床休养
            // 睡床 +10pp 加算：地铺 vs 床尚未在营地建模（睡眠点=住宅/仓库室内中心，无床位容量），暂将卧床休养一律视作睡床。
            // 待确认：床位建模后应按该幸存者睡的是床/地铺区分（仅睡床者得加算）。
            bool restedInBed = resting;
            HealthTickResult r = p.AdvanceHealthDay(_healthRng, resting, restedInBed, infectionMult);

            foreach (HealthTickEvent e in r.Events)
            {
                if (e.Outcome == ConditionOutcome.Death)
                    notes.Add($"{p.DisplayName} 因伤重不治身故");
                else if (e.Outcome == ConditionOutcome.Maim)
                    notes.Add($"{p.DisplayName} 的{e.BodyPart}坏死，已截肢");
                else if (e.ContractedInfection)
                    notes.Add($"{p.DisplayName} 的伤口感染了");
            }

            if (r.AnyDeath)
                toKill.Add(p);
        }

        foreach (Pawn p in toKill)
            p.DieOfWounds();

        if (notes.Count > 0)
            _campToast.Show(string.Join("；", notes), CampToast.Bad);
    }

    /// <summary>把存活幸存者映射成分粮输入：饥饿刻度 + 是否伤员（急性伤：昏迷/出血/骨折需养伤，供 WoundedFirst 优先）。</summary>
    private static FoodDiner ToDiner(Pawn p)
    {
        var insp = p.Inspect();
        bool wounded = insp.IsUnconscious
            || insp.Parts.Any(part => part.IsBleeding || part.IsFractured);
        return new FoodDiner(p.Hunger.Value, wounded, p.Hunger.Cap);
    }

    /// <summary>缺口预警（HUD 顶栏红字，手动显隐持久到下次覆盖）：本餐挨饿→急告；否则存货撑不过阈值昼夜→趋势告警。</summary>
    private void WarnFoodShortfall(RationOutcome ration, int livingCount)
    {
        if (ration.StarvedCount > 0)
        {
            _campToast.Show($"食物告罄：{ration.StarvedCount} 人挨饿，尽快搜刮补给", CampToast.Bad);
            return;
        }

        int days = FoodEconomy.DaysUntilShortfall(_resources.Food, livingCount);
        if (days != int.MaxValue && days <= FoodShortfallWarnDays)
        {
            _campToast.Show($"食物见底：存货约撑 {days} 昼夜，尽快搜刮补给", CampToast.Bad);
        }
    }

    /// <summary>
    /// 聚餐收尾（吃饭动画结束后）：本餐若播了克莉丝汀的请求气泡（trigger 置了 pending）→ 先弹抉择面板，
    /// 相位推进推迟到玩家选完（AdvanceAfterMeal）。仅当她仍是在营存活幸存者时才逼问；
    /// 否则（已亡故/离场）静默清线，照常推进。
    /// </summary>
    private void FinishMeal()
    {
        if (ChristineRequestLogic.HasPendingRequest(_storyFlags))
        {
            if (ChristinePawn() != null)
            {
                PromptChristineHelpChoice();
                return;
            }
            ChristineRequestLogic.Abort(_storyFlags);
        }

        AdvanceAfterMeal();
    }

    /// <summary>聚餐结束后的相位推进（黎明→白天备战；黄昏→睡眠过渡）。抉择面板延迟场景亦复用它收尾。</summary>
    private void AdvanceAfterMeal()
    {
        switch (_clock.CurrentPhase)
        {
            case DayPhase.DawnMeal:
                _clock.TransitionTo(DayPhase.DayPrep);
                break;
            case DayPhase.DuskMeal:
                // 黄昏聚餐结束，交回原睡眠过渡（探险队走到床位就寝 → NightPrep）。
                CallDeferred(nameof(StartSleepTransition));
                break;
        }
    }

    // ---------------- 面板状态机 ----------------

    private void StartFirstDay()
    {
        // 数据驱动开局相位（daynight.json 的 startAtNight）。用户拍板「游戏应当从第一天晚上开始」：
        // true ⇒ 首日直接进 NightAct 夜晚推进（不弹守卫/读书编排面板、不强制暂停），并显式补首日 Day=1
        //        （NightAct 相位转换不自增 Day）；false ⇒ 保持原白天 DayPrep 编排（相位自增 0→1）。
        StartupPhaseLogic.Decision decision = StartupPhaseLogic.Resolve(_clock.StartAtNight);
        if (decision.SetDayToOne)
            _clock.SetDay(1); // 置于 TransitionTo 前，使 OnPhaseChanged 及 HUD 立即读到「第 1 天」。
        _clock.TransitionTo(decision.Phase);
    }

    /// <summary>
    /// 装配营地视野遮暗层（批次4）：营地白天全可见豁免、夜间（NightPrep/NightAct）启用。iso 投影绘制（营地世界为 iso），
    /// 观察者=在营存活幸存者，环境光按相位并入固定光源（<see cref="_campLights"/>）贡献→灯/火堆旁视野更远更宽；
    /// 视野外袭营敌人经 <see cref="Actor.SetVisualHidden"/> 不揭示。开局白天故初始禁用，由 <see cref="OnGamePhaseChanged"/> 按相位切换。
    /// </summary>
    private void SetupCampVisionMask()
    {
        _campVisionMask = new VisionMask();
        _campVisionMask.Configure(_mapBounds, VisionMask.ProjectionMode.Iso);
        _campVisionMask.SetViewersProvider(CampViewers); // 含道格＋布鲁斯（羁绊视野系数经 BondScaleCone 施加）
        _campVisionMask.SetViewerConeAdjuster(BondScaleCone);
        _campVisionMask.SetAmbientProvider(() => VisionLogic.AmbientLight(_clock.CurrentPhase, indoorsDark: false));
        _campVisionMask.SetSourceProvider(pos => _campLights.StrongestAt(pos.X, pos.Y));
        _campVisionMask.SetRevealablesProvider(CampRevealables);
        _campVisionMask.SetEnabled(false); // 开局为白天：全可见豁免
        AddChild(_campVisionMask);
    }

    /// <summary>
    /// 营地视野可揭示物：袭营敌对单位（丧尸/劫掠者/克莉丝汀）——视野外经 <see cref="Actor.SetVisualHidden"/> 隐其 iso sprite（物理/AI 照常）。
    /// 性能：复用 <see cref="_revealBuffer"/> + 每 hostile 的隐藏委托缓存 <see cref="_revealDelegates"/>（4Hz 调用不再逐 hostile 造闭包/ValueTuple，见 tech-review #2）。
    /// </summary>
    private IEnumerable<(Vector2 worldPos, Action<bool> setVisible)> CampRevealables()
    {
        // 缓存膨胀（历经数百丧尸的围攻）时修剪已死/失效条目，防字典无界增长；复用 _revealPrune 去 alloc。
        if (_revealDelegates.Count > 128)
        {
            _revealPrune.Clear();
            foreach (Actor a in _revealDelegates.Keys)
            {
                if (!IsInstanceValid(a) || !a.Alive)
                    _revealPrune.Add(a);
            }
            foreach (Actor a in _revealPrune)
                _revealDelegates.Remove(a);
        }

        _revealBuffer.Clear();
        foreach (Actor h in CurrentHostiles())
        {
            if (!IsInstanceValid(h) || !h.Alive)
                continue;
            if (!_revealDelegates.TryGetValue(h, out Action<bool>? setVis))
            {
                Actor captured = h; // 每 hostile 仅造一次隐藏委托（缓存），非每 recompute
                setVis = v =>
                {
                    if (IsInstanceValid(captured))
                        captured.SetVisualHidden(!v);
                };
                _revealDelegates[h] = setVis;
            }
            _revealBuffer.Add((h.GlobalPosition, setVis));
        }
        return _revealBuffer;
    }

    private void OnGamePhaseChanged(DayPhase phase)
    {
        _pendingInteract = null; // 相位切换：取消未完成的前往交互
        UpdateFatigueTimers(phase); // 批次6：被唤醒者次相位疲劳 debuff 的施加/过期（每相位切换维护）
        // 视野遮暗（批次4）：营地夜间（NightPrep/NightAct）启用；白天/暮光/探索相位全可见豁免。
        _campVisionMask?.SetEnabled(phase is DayPhase.NightPrep or DayPhase.NightAct);
        _expeditionPanel.Visible = false;
        _worldMapPanel.Visible = false;
        _guardPanel.Visible = false;
        _readingPanel.Visible = false;
        _returnWarningPopup.Visible = false;
        _mealAllocPanel.Visible = false;

        // 克莉丝汀累计 3 次"暂不"后不立即走：排期到下一次昼夜交替（相位切进聚餐）时自行离开。
        // 置于结算前，使她不再计入本餐用餐者。走"自愿离开"清理（非 Died，不触发全灭判定）。
        if ((phase == DayPhase.DawnMeal || phase == DayPhase.DuskMeal)
            && ChristineRequestLogic.ConsumeLeaving(_storyFlags))
        {
            ChristineLeaveVoluntary();
        }

        switch (phase)
        {
            case DayPhase.DawnMeal:
                // 黎明聚餐：全员已在 NightAct 起唤醒并度过实时夜晚，此处结算食物 + 气泡交流，结束进 DayPrep。
                EndRaid(); // 夜晚结束：清残留丧尸、守卫下岗（含收尾夜袭者 EndNightRaid）
                ReportNightTheftIfAny(); // 批次6：夜里若被静默偷窃，晨间清点提示（Looter 未发现后果）
                DismissMerchant(); // 天亮：夜访商人收摊走出画面（白天玩家转探险队视角，营地无操作，商人不逗留）
                ReleaseReaders(); // 夜晚结束：读者放座、清读书态（阅读进度已跨夜持久）
                AdvanceSurvivorsHealthDay(); // 又过一昼夜：伤病恶化/愈合、封顶致残/致死（须在聚餐结算前，死亡先反映到名单与全灭判定）
                AdvanceBondDay(); // 道格&布鲁斯共同存活又一昼夜 → 羁绊升级推进（两者皆活才 +1，任一死即冻结）
                TryTriggerMilitaryRaid(); // 电台主线：回复军方后第 3 天白天军袭到期（结局②，本批仅钩子+安全 no-op）
                BeginMeal("黎明聚餐", "dawn");
                break;
            case DayPhase.DayPrep:
                foreach (var p in _survivors)
                    p.SetSleeping(false);
                PopulateExpeditionPanel();
                _expeditionPanel.Visible = true;
                break;
            case DayPhase.DayTravel:
                break;
            case DayPhase.DayExplore:
                LoadExplorationLevel(_pendingDestination);
                break;
            case DayPhase.DayReturn:
                // 探索队已返回，卸载关卡后进入黄昏聚餐（睡眠过渡推迟到聚餐结束）。
                _returnWarningPopup.CancelDelay();
                _returnWarningPopup.Visible = false;
                UnloadExplorationLevel();
                CallDeferred(nameof(EnterDuskMeal));
                break;
            case DayPhase.DuskMeal:
                BeginMeal("黄昏聚餐", "dusk");
                break;
            case DayPhase.NightPrep:
                PopulateGuardPanel();
                _guardPanel.Visible = true;
                break;
            case DayPhase.NightAct:
                // 夜晚开始：唤醒全员（含白天留守者），守卫上岗由 PawnRoleManager 按站岗分配置 Guard。
                // 之后夜晚由 GameClock 实时流逝 NightLengthSeconds，到时自动 → DawnMeal（不再瞬跳）。
                foreach (var p in _survivors)
                    p.SetSleeping(false);
                // 双班硬日程（SPEC-B6①）：当日探险队(DayCrew)白天在外奔波，夜里强制睡；营地留守(NightCrew)清醒站岗/生产。
                ApplyNightShiftSleep();
                // 神秘商人夜访：到点且平安（非袭营/教学夜）则进场。置于 BeginChristineTutorial 之前——
                // 否则教学关会先置 tutorial flag，IsMerchantBlockedToday 的第 2 夜判据失效导致商人与反水撞车。
                TryMerchantVisit();
                StationGuards(); // D2：守卫走向各自岗位站位并挂上岗位加成
                StationReaders(); // 读者走向座位坐下读书（读书指派为空则无操作）
                // 教学关：第 2 夜一次性触发克莉丝汀反水关（StoryFlag 防重入）。这一晚是脚本人类袭击，
                // 不叠加丧尸袭营（TriggerRaid 会因 _tutorialActive 早退）。
                if (_clock.Day == 2 && !_storyFlags.Has("tutorial_raider_started"))
                    BeginChristineTutorial();
                // 尸潮时限到期(day>=DeadlineDay)：本夜起无限围攻直至全灭（无生还，有意为之的黑暗终局）。
                // 时限与教学关(第 2 夜)天数相去甚远，不冲突；发现与否不影响触发（Evaluate 到期一律 Arrived）。
                // 终局冻结门控：主线推进到终局抉择点后置 EndgameFreezeFlag → 结局流程接管，围攻不再触发（置位方留待主线系统）。
                else if (HordeTimeline.ShouldTriggerSiege(
                    _clock.Day, _storyFlags.Has(HordeTimeline.SightedFlag), _storyFlags.Has(HordeTimeline.EndgameFreezeFlag)))
                    TriggerHordeSiege();
                else
                    MaybeTriggerNightRaiderRaid(); // 批次6：常规夜（非教学/非围攻）按概率触发袭击者潜入 + 警戒对抗
                break;
        }
    }

    private void PopulateExpeditionPanel()
    {
        var entries = new List<ExpeditionPanel.PawnEntry>();
        for (int i = 0; i < _survivors.Count; i++)
        {
            var insp = _survivors[i].Inspect();
            entries.Add(new ExpeditionPanel.PawnEntry
            {
                Id = i,
                Name = insp.DisplayName,
                WeaponSummary = insp.Weapon?.Name ?? "徒手",
                ArmorSummary = string.Join(", ", insp.Armor.Select(a => a.Name)),
            });
        }
        // 布鲁斯（狗）可随队出探索（口袋狗衣提供负重）：以哨兵 Id=_survivors.Count 追加为 companion 条目（不占 3 人上限）。
        // 仅当道格在场（狗须道格同队才可带）时列出；实际"须道格勾选"的绑定在 OnExpeditionConfirmed 裁定。
        if (_bruce is { Alive: true } && _doug is { Alive: true })
        {
            entries.Add(new ExpeditionPanel.PawnEntry
            {
                Id = _survivors.Count,
                Name = _bruce.DisplayName + "（狗·需道格同队）",
                WeaponSummary = "撕咬",
                ArmorSummary = "",
                IsCompanion = true,
            });
        }
        _expeditionPanel.SetPawns(entries);
        if (!string.IsNullOrEmpty(_pendingDestination))
            _expeditionPanel.SetDestination(_pendingDestination, _pendingTravelTime);
    }

    private void PopulateGuardPanel()
    {
        // 岗位来自已建岗位（预置 + 调试放置）。PostId = 岗位在 _guardPosts 中的下标。
        var posts = new List<GuardPanel.GuardPostDef>();
        for (int i = 0; i < _guardPosts.Count; i++)
        {
            posts.Add(new GuardPanel.GuardPostDef { PostId = i, Name = _guardPosts[i].Name });
        }
        _guardPanel.SetupPosts(posts);

        var pawnOptions = new List<GuardPanel.PawnOption>();
        for (int i = 0; i < _survivors.Count; i++)
        {
            var insp = _survivors[i].Inspect();
            pawnOptions.Add(new GuardPanel.PawnOption
            {
                Id = i,
                Name = insp.DisplayName,
                EquipmentSummary = insp.Weapon?.Name ?? "徒手",
            });
        }
        // 布鲁斯（狗）可排岗（效率 75%）：以哨兵 Id=_survivors.Count 追加（选项 Id 为下标空间，狗不在 _survivors，
        // 用越界下标当哨兵，OnGuardConfirmed 据此译回 _bruce.Id）。
        if (_bruce is { Alive: true })
        {
            pawnOptions.Add(new GuardPanel.PawnOption
            {
                Id = _survivors.Count,
                Name = _bruce.DisplayName + "（狗·75%）",
                EquipmentSummary = "撕咬",
            });
        }
        _guardPanel.SetPawns(pawnOptions);
    }

    private void OnExpeditionConfirmed(int[] pawnIds, string destination)
    {
        var ids = new HashSet<int>();
        bool brucePicked = false;
        foreach (int idx in pawnIds)
        {
            if (idx >= 0 && idx < _survivors.Count)
                ids.Add(_survivors[idx].Id);
            else if (idx == _survivors.Count) // 哨兵下标=布鲁斯
                brucePicked = true;
        }

        // 带狗须道格同队：布鲁斯被勾选但道格不在本次队伍→撤下布鲁斯（提示玩家），不阻断出队。
        bool dougInTeam = _doug is { Alive: true } && ids.Contains(_doug.Id);
        _bruceExpedition = brucePicked && dougInTeam && _bruce is { Alive: true };
        if (brucePicked && !_bruceExpedition)
            _campToast.Show("布鲁斯需道格同队才能带上，本次留守营地。", CampToast.Bad);

        _roleManager.SetExpeditionIds(ids);
        // 夜间班别真源：ExpeditionIds 会在 DayReturn 相位被清空，故本类自存当日探险名册供夜里 ShiftFor/硬日程/名册判定。
        _todaysExpeditionIds.Clear();
        foreach (int id in ids)
            _todaysExpeditionIds.Add(id);
        _clock.TransitionTo(DayPhase.DayTravel);
    }

    private void OnWorldMapDestinationSelected(string name, int travelTime)
    {
        _pendingDestination = name;
        _pendingTravelTime = travelTime;
        _worldMapPanel.Visible = false;
        _expeditionPanel.SetDestination(name, travelTime);
    }

    private void OnExploreWarning()
    {
        if (_clock.CurrentPhase != DayPhase.DayExplore)
            return;
        _returnWarningPopup.ResetWarning();
        _returnWarningPopup.Visible = true;
    }

    private void OnReturnNow()
    {
        _returnWarningPopup.CancelDelay();
        _returnWarningPopup.Visible = false;
        _clock.TransitionTo(DayPhase.DayReturn);
    }

    private void OnExplorationReturn()
    {
        if (_clock.CurrentPhase == DayPhase.DayExplore)
            _clock.TransitionTo(DayPhase.DayReturn);
    }

    /// <summary>
    /// 探索队踏入一处发现点：先按剧情发现点（金手指帮根据地/守望者森林小屋尸体+日记）解析，
    /// 未命中再按探索点搜刮缓存（河边小屋/联合收割机仓库的枪柜/工具柜等）解析。
    /// 命中则置 flag（防重复）、把掉落经 <c>LootApplication</c> 入共享库存/食物/工作台、冻结时标弹环境叙事。
    /// 日记/书回营后在库存点开经 ReaderPanel 细读。
    /// </summary>
    private void OnExplorationDiscovery(string discoveryId)
    {
        // ——望远镜瞭望挂点（城市之巅瞭望观景台）——
        // 踏入望远镜发现区即到此。约定顺序：先播全屏瞭望演出（anim-lookout），播完回调 OnLookoutCinematicFinished
        // 里再出剧情文本（loot-story）+置 HordeSighted 旗标（core-timer）。见 [HANDOFF] anim-lookout → loot-story。
        if (discoveryId == TestExploration.LookoutTelescopeDiscoveryId)
        {
            // 已瞭望过则不重复全屏演出（core-timer 的持久去重旗标；旗标未置前每次踏入均重播）。
            if (_storyFlags.Has(HordeTimeline.SightedFlag))
                return;

            // 冻结探索实时层：演出全屏遮挡期间不让世界继续（避免看不见的丧尸交战）；播完回调里恢复。
            _prevLookoutTimeScale = Engine.TimeScale;
            Engine.TimeScale = 0;
            // 演出走真实时钟（不吃 TimeScale），全屏播放约 11s（可跳过），播完自毁并回调。
            HordeLookoutCinematic.Show(_hud, OnLookoutCinematicFinished);
            return;
        }

        // ——广播台「发出设备」挂点（电台主线）——
        // 踏入发射机发现区即到此：取得发出设备 → 推进主线状态（Unknown/Heard→HasTransmitter）+ 弹取设备叙事（不给书）。
        // 定点非随机（用户 D4 拍板：主线关键物资保底）。GrantTransmitter 幂等：已取得过返回 false → 不重复弹叙事。
        if (discoveryId == RadioMainline.TransmitterDiscoveryId)
        {
            if (RadioMainline.GrantTransmitter(_storyFlags))
                ShowDiscoveryNarrative(RadioMainline.TransmitterPickupTitle, RadioMainline.TransmitterPickupNarrative);
            return;
        }

        // ——超市幸存者骗局挂点（[SPEC-B13]，用户原话"轻信会被骗进密闭小房间背刺围攻"）——
        // 门口接触点：弹 ChoicePanel 二选一（骗局未决出时）；已决出（轻信打过/拒过）则据点门口不再招呼（no-op）。
        if (discoveryId == SupermarketAmbush.ContactDiscoveryId)
        {
            if (SupermarketAmbush.ShouldOfferContact(_storyFlags))
                ShowSupermarketContact();
            return;
        }
        // 内圈闯入点：拒绝招呼后踏入据点内圈抢被占物资 → 公平开战（无先手）；未拒绝/已生成过则 no-op。
        if (discoveryId == SupermarketAmbush.InnerRingDiscoveryId)
        {
            if (SupermarketAmbush.ShouldSpawnInnerRingFight(_storyFlags))
                SpringSupermarketInnerBreach();
            return;
        }

        // ——南林村庄「上锁的屋子」救援挂点（道格布鲁斯正史入队，[SPEC-B11]）——
        // 踏入锁屋门发现区即到此：出救援叙事 + 置 village_doug_rescued 旗标（去重）。
        // 真正的道格/布鲁斯注入**延到探索队回营**（见 UnloadExplorationLevel → MaybeSpawnRescuedDougAndBruce）——
        // 救援发生在关内、道格饿昏迷无法作战，叙事＝架回营地；延后注入也避免在关内把营地态布鲁斯注入后跨场景追敌。
        RescueOutcome? rescue = VillageRescue.Resolve(discoveryId, _storyFlags);
        if (rescue != null)
        {
            _storyFlags.Set(rescue.Value.StoryFlag, "true"); // 置 village_doug_rescued（去重 + 待回营注入标记）
            ShowDiscoveryNarrative(rescue.Value.Title, rescue.Value.Narrative);
            return;
        }

        // ——南丁格尔的小药店·护士相遇招募挂点（可招募护士角色，[SPEC-B13]）——
        // 踏入护士警戒区即到此：护士**清醒、可对话**（与道格饿昏迷被动救援不同）→ 弹 ChoicePanel 招募对话
        // （邀请入队 / 暂不）。接受→置 pharmacy_nurse_agreed（待回营注入 + 对话去重）；婉拒→**不置旗**（可再访药店再谈）。
        // 真正的护士注入**延到探索队回营**（见 UnloadExplorationLevel → MaybeRecruitNurse），同道格延后注入口径。
        NurseRecruitOffer? nurse = NurseRecruit.Resolve(discoveryId, _storyFlags);
        if (nurse != null)
        {
            PromptNurseRecruit(nurse.Value);
            return;
        }

        DiscoveryResult? r = GoldfingerDiscovery.Resolve(discoveryId, _storyFlags);
        if (r != null)
        {
            DiscoveryResult d = r.Value;
            _storyFlags.Set(d.StoryFlag, "true"); // 持久去重：本 flag 已置则下次 Resolve 返回 null

            // 日记入共享库存（同一 BookData 实例登记进 registry，回营阅读共享已读态）。
            // 空 BookId = 该发现点无书（如克莉丝汀本人尸体点，日记A 归帮众尸体），跳过入库。
            if (!string.IsNullOrEmpty(d.BookId))
                LootApplication.Apply(
                    new[] { LootItem.Book(d.BookId) }, _inventory, _bookRegistry, _bookResolver);

            ShowDiscoveryNarrative(d.Title, d.Narrative);
            return;
        }

        // ——叙事调查点（极乐迪斯科式，[SPEC-B12]）——
        // 踏入/点击调查点即到此：冻结时标 + **分页**弹环境叙事（不走时间）+ 一次性点置去重旗标。
        // 不给书、不入库、不计物资完成度（第三类，与物资搜刮/主线触发并存）。
        NarrativeSpotResult? spot = NarrativeSpotRegistry.Resolve(discoveryId, _storyFlags);
        if (spot != null)
        {
            NarrativeSpotResult s = spot.Value;
            if (!s.Repeatable && !string.IsNullOrEmpty(s.StoryFlag))
                _storyFlags.Set(s.StoryFlag, "true"); // 持久去重（可重读点空旗标不置）
            ShowNarrativeSpot(s.Title, s.Pages);
            return;
        }

        // 探索点搜刮缓存（河边小屋/联合收割机仓库）：整批掉落落地（武器/书/材料/食物/工具），同构营地搜刮。
        CacheResult? c = ExplorationCache.Resolve(discoveryId, _storyFlags);
        if (c == null)
            return; // 未知 id 或已搜过

        CacheResult cache = c.Value;
        _storyFlags.Set(cache.StoryFlag, "true"); // 持久去重

        var tools = new List<ToolSlot>();
        int food = LootApplication.Apply(cache.Loot, _inventory, _bookRegistry, _bookResolver, tools);
        if (food > 0)
            _resources.AddFood(food);
        InstallFoundTools(tools);

        ShowDiscoveryNarrative(cache.Title, cache.Narrative);
    }

    /// <summary>
    /// 望远镜瞭望演出播完（或被跳过）回调 —— anim-lookout 的「播完回调」挂点。约定顺序：演出→本回调。
    /// 本方法先恢复探索层时标（演出前冻结的），随后交给 loot-story/core-timer 接内容：
    ///   1) core-timer：置 HordeSighted 旗标 <c>_storyFlags.Set(HordeTimeline.SightedFlag, "true")</c>（解锁尸潮倒计时 HUD + 持久去重）；
    ///   2) loot-story：出瞭望所见剧情文本 <c>ShowDiscoveryNarrative(title, narrative)</c>（内含再次冻结/恢复时标，无碍）。
    /// 二者顺序不敏感，但都应在本方法内、时标恢复之后。见 [HANDOFF] anim-lookout → loot-story。
    /// </summary>
    private void OnLookoutCinematicFinished()
    {
        Engine.TimeScale = _prevLookoutTimeScale <= 0 ? 1 : _prevLookoutTimeScale;

        // 演出播完：置 HordeTimeline.SightedFlag(解锁尸潮倒计时 HUD + 持久去重) + 弹瞭望所见剧情文本(不给书)。
        // 旗标/文本单一事实源＝LookoutSighting.Resolve（脱 Godot 可测）。外层踏入已按同旗标去重，故此处 Resolve 恒非空。
        DiscoveryResult? sighting = LookoutSighting.Resolve(TestExploration.LookoutTelescopeDiscoveryId, _storyFlags);
        if (sighting != null)
        {
            DiscoveryResult s = sighting.Value;
            _storyFlags.Set(s.StoryFlag, "true"); // 置 horde_sighted
            ShowDiscoveryNarrative(s.Title, s.Narrative);
        }
    }

    /// <summary>冻结探索实时层、弹环境叙事面板（发现点/搜刮点共用）。</summary>
    private void ShowDiscoveryNarrative(string title, string narrative)
    {
        _prevDiscoveryTimeScale = Engine.TimeScale;
        Engine.TimeScale = 0; // 冻结探索实时层，专注读叙事
        _discoveryPanel.Show(title, narrative);
        _discoveryPanel.Visible = true;
    }

    /// <summary>
    /// 冻结探索实时层、**分页**弹叙事面板（叙事调查点 [SPEC-B12]，一段 2~4 屏，「不走时间」）。
    /// 复用同一冻结/恢复机制（<see cref="_prevDiscoveryTimeScale"/> + <see cref="OnDiscoveryContinued"/>）——
    /// 面板内部逐屏推进，末屏「继续」才回调关闭+恢复时标。
    /// </summary>
    private void ShowNarrativeSpot(string title, IReadOnlyList<string> pages)
    {
        _prevDiscoveryTimeScale = Engine.TimeScale;
        Engine.TimeScale = 0; // 冻结探索实时层，专注读 CG（呈现期间时钟暂停＝不走时间）
        _discoveryPanel.ShowPaged(title, pages);
        _discoveryPanel.Visible = true;
    }

    /// <summary>关发现叙事面板，恢复时标（冻结中打开的则回 1）。</summary>
    private void OnDiscoveryContinued()
    {
        _discoveryPanel.Visible = false;
        Engine.TimeScale = _prevDiscoveryTimeScale <= 0 ? 1 : _prevDiscoveryTimeScale;
    }

    // ============ 超市幸存者骗局（[SPEC-B13]，纯逻辑=SupermarketAmbush，空间执行=TestExploration.SpawnSupermarketRaiders）============

    /// <summary>据点门口接触对话：暂停世界 + 弹 ChoicePanel 二选一（对方招呼"进来说话"）。选项处理见 <see cref="OnSupermarketContactChosen"/>。</summary>
    private void ShowSupermarketContact()
    {
        CapturePanelTimeState(out int savedSpeed, out bool savedPaused);

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            SupermarketAmbush.ContactPrompt,
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = SupermarketAmbush.ChoiceTrust, Label = SupermarketAmbush.TrustLabel,
                        Description = SupermarketAmbush.TrustDescription, Accent = new Color(0.35f, 0.55f, 0.35f) },
                new() { Value = SupermarketAmbush.ChoiceRefuse, Label = SupermarketAmbush.RefuseLabel,
                        Description = SupermarketAmbush.RefuseDescription, Accent = new Color(0.6f, 0.5f, 0.25f) },
            });
        panel.Confirmed += v =>
        {
            panel.QueueFree();
            RestorePanelTimeState(savedSpeed, savedPaused);
            OnSupermarketContactChosen(v);
        };
    }

    /// <summary>
    /// 接触对话选定：骗局一次性置 <see cref="SupermarketAmbush.ScamResolvedFlag"/>。
    ///   · 轻信跟随 → 被诱入密室背刺围攻：生成敌对幸存者 + 1.5x 潜行先手（<see cref="TestExploration.SpawnSupermarketRaiders"/>），弹发难旁白；
    ///   · 不轻信 → 置 <see cref="SupermarketAmbush.RefusedFlag"/>（据点转占内圈的敌对方），弹警告旁白，外围可搜、内圈踏入闯入点方开战。
    /// </summary>
    private void OnSupermarketContactChosen(int value)
    {
        _storyFlags.Set(SupermarketAmbush.ScamResolvedFlag, "true"); // 一次性：门口不再招呼

        if (value == SupermarketAmbush.ChoiceTrust)
        {
            _storyFlags.Set(SupermarketAmbush.AmbushSprungFlag, "true"); // 去重：内圈闯入点不再另刷敌
            if (_currentLevel is TestExploration lvl)
                lvl.SpawnSupermarketRaiders(SupermarketAmbush.AmbushRaiderCount, preemptiveStrike: true);
            ShowDiscoveryNarrative(SupermarketAmbush.AmbushSprungTitle, SupermarketAmbush.AmbushSprungNarrative);
        }
        else
        {
            _storyFlags.Set(SupermarketAmbush.RefusedFlag, "true"); // 据点转敌对占内圈
            ShowDiscoveryNarrative(SupermarketAmbush.RefuseWarningTitle, SupermarketAmbush.RefuseWarningNarrative);
        }
    }

    /// <summary>拒绝招呼后踏入内圈闯入点：据点敌对化、公平开战（无先手）抢被占物资。去重靠 <see cref="SupermarketAmbush.AmbushSprungFlag"/>。</summary>
    private void SpringSupermarketInnerBreach()
    {
        _storyFlags.Set(SupermarketAmbush.AmbushSprungFlag, "true"); // 去重：只生成一次
        if (_currentLevel is TestExploration lvl)
            lvl.SpawnSupermarketRaiders(SupermarketAmbush.AmbushRaiderCount, preemptiveStrike: false);
        ShowDiscoveryNarrative(SupermarketAmbush.InnerRingBreachTitle, SupermarketAmbush.InnerRingBreachNarrative);
    }

    private void LoadExplorationLevel(string destinationName)
    {
        if (_levelRoot != null)
            return;

        var levelScene = GD.Load<PackedScene>("res://scenes/TestExploration.tscn");
        _levelRoot = levelScene.Instantiate();
        _currentLevel = (ExplorationLevel)_levelRoot;

        _currentLevel.Clock = _clock;
        // 关卡里新建的丧尸要拿到与营地单位相同的战斗引擎实例（否则 Inject 缺失、首帧 Think 崩）。
        if (_currentLevel is TestExploration testLevel)
            testLevel.Combat = _combat;
        _currentLevel.ExpeditionTeam = _survivors.Where(s => s.Role == PawnRole.Expedition).ToList();
        // 随队布鲁斯（本次带狗且道格同队时）：关卡据此放置/回收+纳入敌人目标池/视野观察者。狗非 Pawn、不入 ExpeditionTeam。
        _currentLevel.CompanionDog = _bruceExpedition && _bruce is { Alive: true } ? _bruce : null;
        // 探索侧羁绊视野系数：注入营地同款逐观察者视锥调整器（道格锥角/布鲁斯视距·锥角按 BondLevel 缩放），
        // 使道格带布鲁斯出探索时视野技能端到端生效（委托内部读 BondLevel，道格/布鲁斯为同一实例，跨场景命中）。
        _currentLevel.ViewerConeAdjuster = BondScaleCone;
        _currentLevel.DestinationName = destinationName; // 关卡据此决定是否铺发现点（金手指帮根据地）
        // 复仇线才在金手指帮根据地额外铺"克莉丝汀本人尸体"点（帮众尸体恒在，与此无关）。
        _currentLevel.ChristineLeftForRevenge = _storyFlags.Has(GoldfingerDiscovery.ChristineLeftForRevengeFlag);

        _currentLevel.OnReturnToCamp += OnExplorationReturn;
        _currentLevel.OnDiscovery += OnExplorationDiscovery;

        ClearSelection();
        _campNavRegion.Enabled = false;

        GetTree().Root.AddChild(_levelRoot);
        SetCampVisible(false);

        _currentLevel.Initialize();
    }

    private void UnloadExplorationLevel()
    {
        if (_levelRoot == null)
            return;

        _currentLevel!.OnReturnToCamp -= OnExplorationReturn;
        _currentLevel!.OnDiscovery -= OnExplorationDiscovery;
        _currentLevel!.Cleanup();

        // 卸载关卡时若发现叙事面板仍开着，收起并恢复时标，避免带回营地。
        if (_discoveryPanel.Visible)
            OnDiscoveryContinued();

        foreach (Pawn p in _survivors.Where(s => s.Role == PawnRole.Expedition))
        {
            if (_levelRoot.IsAncestorOf(p))
            {
                p.Reparent(_actorLayer, keepGlobalTransform: false);
                p.Position = _cameraCenter;
            }
        }

        // 随队布鲁斯回收：随队伍返回营地 actor 层（存活则跟随道格恢复，阵亡已在 OnActorDied 置 _bruce=null）。
        if (_bruce is { } bruce && _levelRoot.IsAncestorOf(bruce))
        {
            bruce.Reparent(_actorLayer, keepGlobalTransform: false);
            bruce.Position = _cameraCenter + new Vector2(-8f, 24f);
        }
        _bruceExpedition = false;

        _currentLevel = null;
        GetTree().Root.RemoveChild(_levelRoot);
        _levelRoot.QueueFree();
        _levelRoot = null;

        _campNavRegion.Enabled = true;
        _camera.MakeCurrent();
        SetCampVisible(true);

        // 南林村庄救援回营：营地恢复后，若本次（或此前）在村庄救出道格布鲁斯而尚未入队 → 正史注入二人一狗（饿昏迷低档）。
        MaybeSpawnRescuedDougAndBruce();
        // 药店招募回营：若在药店已答应护士入队而尚未注入 → 正史注入护士（[SPEC-B13]）。
        MaybeRecruitNurse();
    }

    private void SetCampVisible(bool visible)
    {
        _isoLayer.Visible = visible;
    }

    private void OnGuardConfirmed(Dictionary<int, int> posts)
    {
        var assignments = new Dictionary<int, int>();
        foreach (var kv in posts)
        {
            if (kv.Value >= 0 && kv.Value < _survivors.Count)
                assignments[kv.Key] = _survivors[kv.Value].Id;
            else if (kv.Value == _survivors.Count && _bruce != null)
                assignments[kv.Key] = _bruce.Id; // 哨兵下标=布鲁斯，译回其真 Id（不落 PawnRoleManager 的 Pawn 角色）
        }
        _roleManager.SetGuardAssignments(assignments);
        // 守卫确认后**顺序弹读书面板**（不直接进 NightAct）：由读书面板确认/取消统一触发进夜，单一触发点防双触发。
        _guardPanel.Visible = false;
        PopulateReadingPanel();
        _readingPanel.Visible = true;
    }

    /// <summary>
    /// 填充读书指派面板（仿 <see cref="PopulateGuardPanel"/>）：读者=存活可控幸存者中**未被指派守卫者**
    /// （守卫已在 GuardAssignments，排除避免同人两职）；书=营地拥有（<see cref="_bookRegistry"/>）且**未读**（全局
    /// <see cref="BookData.IsRead"/>）的书。读者 Id 直接用 pawn.Id（回传的 pawnId→bookId 直喂 SetReadingAssignments）。
    /// </summary>
    private void PopulateReadingPanel()
    {
        var guarded = new HashSet<int>(_roleManager.GuardAssignments.Values);

        var readers = new List<ReadingPanel.PawnOption>();
        foreach (Pawn p in _survivors)
        {
            if (!p.Alive || !p.IsControllable || guarded.Contains(p.Id))
                continue;
            readers.Add(new ReadingPanel.PawnOption { Id = p.Id, Name = p.DisplayName });
        }

        var books = new List<ReadingPanel.BookOption>();
        foreach (BookData b in _bookRegistry.Values)
        {
            if (b.IsRead)
                continue; // "未读"按营地全局已读标记（per-reader 未读细分待用户定，见遗留）
            // 前置书标题：优先解析实例标题供「未读《X》」提示；解析不到退化到 id。
            string? preTitle = b.PrerequisiteBookId is { } preId ? (_bookResolver(preId)?.Title ?? preId) : null;
            books.Add(new ReadingPanel.BookOption
            {
                BookId = b.Id,
                Title = b.Title,
                PrerequisiteBookId = b.PrerequisiteBookId,
                PrerequisiteTitle = preTitle,
            });
        }

        // 前置满足判定按读者本人已读（引擎侧 AccrueReading 亦以 Pawn.HasReadBook 判 ×0.2，口径一致）。
        _readingPanel.SetupReaders(readers, books,
            (pawnId, bookId) => _survivors.FirstOrDefault(s => s.Id == pawnId)?.HasReadBook(bookId) ?? false);
    }

    private void OnReadingConfirmed(Dictionary<int, string> assignments)
    {
        // assignments 键为 pawn.Id（PopulateReadingPanel 用真实 Id），直接喂 SetReadingAssignments（"不读"行已被面板略过）。
        _roleManager.SetReadingAssignments(assignments);
        _readingPanel.Visible = false;
        _clock.TransitionTo(DayPhase.NightAct);
    }

    private void OnReadingCancelled()
    {
        // 取消=本夜无人读书：清空读书指派后照常进夜。
        _roleManager.SetReadingAssignments(new Dictionary<int, string>());
        _readingPanel.Visible = false;
        _clock.TransitionTo(DayPhase.NightAct);
    }

    // ---------------- D2 驻守 AI + 岗位加成 ----------------

    /// <summary>
    /// 夜晚上岗：按 PawnRoleManager 的岗位分配（postId→pawnId），让每个守卫走向岗位站位并挂上岗位加成。
    /// 走向岗位靠 Stationing 令 Pawn.Think Guard 分支放行移动令；抵达即回原地驻守。
    /// </summary>
    private void StationGuards()
    {
        _raidGuards.Clear();
        _raidGuardDogs.Clear();
        _guardPostSightById.Clear(); // 批次6：重采集守卫 id → 岗位视野倍率（岗哨建筑加成源）
        foreach (var kv in _roleManager.GuardAssignments)
        {
            int postId = kv.Key, pawnId = kv.Value;
            if (postId < 0 || postId >= _guardPosts.Count)
                continue;
            GuardPostInstance post = _guardPosts[postId];

            Pawn? guard = _survivors.FirstOrDefault(p => p.Id == pawnId && p.Alive);
            if (guard != null)
            {
                guard.ApplyGuardPost(post.Stats);
                guard.Stationing = true;
                guard.CommandMoveTo(post.StandPos);
                _raidGuards.Add(guard);
                _guardPostSightById[guard.Id] = post.Stats.SightMultiplier;
                continue;
            }

            // 犬类守卫（布鲁斯）：站岗效率 75%，走向岗位靠 GuardStationing 让位跟随/侦测；巡防锁敌由 UpdateRaid 驱动。
            if (_bruce is { Alive: true } bruce && bruce.Id == pawnId && !_raidGuardDogs.Contains(bruce))
            {
                bruce.ApplyGuardPost(post.Stats);
                bruce.GuardStationing = true;
                bruce.CommandMoveTo(post.StandPos);
                _raidGuardDogs.Add(bruce);
                _guardPostSightById[bruce.Id] = post.Stats.SightMultiplier;
            }
        }
    }

    /// <summary>
    /// 夜晚读书上岗（仿 <see cref="StationGuards"/>）：按 PawnRoleManager 的读书指派（pawnId→bookId），让每个读者
    /// 认领就近空座、走过去坐下读；无空座就地读（-10%）。全营读速加成汇总一次算好喂给每个读者（含其自身贡献）。
    /// <b>不 gate 在 Role==Reading</b>——本方法在 NightAct 相位切换的 OnGamePhaseChanged 中先于 PawnRoleManager
    /// 置 Role 而运行（事件订阅顺序），与 StationGuards 同理靠 Stationing 标志放行移动令。
    /// </summary>
    private void StationReaders()
    {
        // 全营读速加成汇总：遍历全体存活幸存者的 CampWideReadingSpeedBonus 求和（含读者本人；仅满级书虫非 0）。
        double campWideSum = _survivors.Where(s => s.Alive).Sum(s => s.Perks.CampWideReadingSpeedBonus);

        // 守卫优先：同人若两处都被指派，让位守卫（与 PawnRoleManager NightAct 分支的优先级一致），不重复站岗。
        var guarded = new HashSet<int>(_roleManager.GuardAssignments.Values);

        foreach (var kv in _roleManager.ReadingAssignments)
        {
            int pawnId = kv.Key;
            string bookId = kv.Value;
            if (guarded.Contains(pawnId))
                continue;
            Pawn? reader = _survivors.FirstOrDefault(p => p.Id == pawnId && p.Alive);
            if (reader == null)
                continue;
            BookData? book = _bookResolver(bookId);
            if (book == null)
                continue;

            reader.BeginReading(book, campWideSum, _nightLengthSeconds);

            SeatClaim? seat = ClaimNearestFreeSeat(reader.GlobalPosition);
            if (seat is { } s)
            {
                reader.ReadingSeat = s;
                reader.Stationing = true;
                reader.CommandMoveTo(s.Pos);
            }
            else
            {
                reader.ReadingSeat = null; // 无空座：就地读，-10% 由 ReadingSpeed 施加
            }
        }
    }

    /// <summary>夜晚结束：释放所有读者认领的座位并清读书运行时态（跨夜进度已在 ReadingProgress 中持久，不受影响）。</summary>
    private void ReleaseReaders()
    {
        foreach (Pawn p in _survivors)
        {
            if (p.ReadingSeat is { } seat)
                ReleaseSeat(seat);
            p.EndReading();
        }
        _roleManager.SetReadingAssignments(new Dictionary<int, string>());
    }

    // ---------------- D3 袭营触发/生成 ----------------

    /// <summary>
    /// 公共袭营触发钩子——供未来叙事事件脚本调用（如某夜剧情事件 <c>TriggerRaid(1.5f)</c>）。
    /// 仅 NightAct 生效；波次规模随天数/营地规模递增（<see cref="RaidWave"/>），乘强度系数。
    /// </summary>
    public void TriggerRaid(float intensity = 1f)
    {
        if (_clock.CurrentPhase != DayPhase.NightAct)
        {
            GD.Print("[Raid] 非 NightAct 相位，忽略袭营触发。");
            return;
        }
        if (_raidActive)
            return;
        if (_tutorialActive)
        {
            GD.Print("[Raid] 教学关（克莉丝汀反水）进行中，忽略丧尸袭营，避免两种袭击叠加。");
            return;
        }

        _raidIntensity = intensity;
        int campSize = _survivors.Count(s => s.Alive);
        int count = RaidWave.ZombieCount(_clock.Day, campSize, intensity);
        SpawnCampZombies(count);
        _raidActive = true;
        GD.Print($"[Raid] 第 {_clock.Day} 天袭营：强度 {intensity:0.0}，{count} 只丧尸自大门涌入，上岗守卫 {_raidGuards.Count} 人。");
    }

    // ============ 批次6 夜防对抗：袭击者潜入（警戒力 vs 潜行力）============

    /// <summary>
    /// 常规夜（NightAct）尝试触发袭击者潜入：非教学/非围攻/未在进行中、<see cref="ShiftSchedule.RaidAllowedIn"/> 放行（常规仅夜里块）、
    /// 开局两夜后（留给教学），按 <see cref="NightRaiderRaidChance"/> 概率生成一波并跑对抗。数值皆拟定待调。
    /// </summary>
    private void MaybeTriggerNightRaiderRaid()
    {
        if (_nightRaidActive || _tutorialActive || _siegeActive || _raidActive)
            return;
        if (_clock.Day < 3) // 开局前两夜留给教学/熟悉，拟定待调
            return;
        if (!ShiftSchedule.RaidAllowedIn(_clock.CurrentPhase, authored: false))
            return;
        if (_raidContestRng.Range(0.0, 1.0) >= NightRaiderRaidChance)
            return;

        // 意图随机（拟定待调）：杀戮型（潜行先手 1.5x）/ 劫掠型（静默偷窃）各半。
        RaiderIntent intent = _raidContestRng.Range(0.0, 1.0) < 0.5 ? RaiderIntent.Killer : RaiderIntent.Looter;
        int count = NightRaiderCountBase + _clock.Day / 12; // 随天数缓增，拟定待调
        TriggerRaiderRaid(intent, count, authored: false);
    }

    /// <summary>
    /// 触发一波袭击者潜入并立即结算「警戒 vs 潜行」对抗（公共钩子供 authored 事件调用）。
    /// 发现 → 暂停+三档响应面板；未发现 → 按意图潜行先手 1.5x（Killer）或静默偷物资后撤离（Looter）。
    /// authored=true 绕过时段/概率门（如剧情军袭在别处走各自路径，本钩子供未来夜间 authored 潜入用）。
    /// </summary>
    public void TriggerRaiderRaid(RaiderIntent intent, int count = 2, bool authored = false)
    {
        if (_nightRaidActive || _tutorialActive || _siegeActive)
            return;
        if (!authored && !ShiftSchedule.RaidAllowedIn(_clock.CurrentPhase, authored: false))
        {
            GD.Print("[NightRaid] 非夜里块且非 authored，忽略袭击者潜入触发。");
            return;
        }

        _nightRaidIntent = intent;
        _respondingIds.Clear();
        _nightRaidResolved = false;
        _nightRaidRolledBands.Clear();
        SpawnNightRaiders(System.Math.Max(1, count));
        _nightRaidActive = true;
        // ★对抗掷点不再在生成瞬间（边缘 ~256px 超感知 → 恒未发现）：改由 UpdateNightRaid 随尖兵逼近到各距离带分段掷点，
        // 深入营心仍未发现才兑现未发现后果（param-calibration 校准）。决出前守卫未警觉、不迎战（潜行涌现）。
        GD.Print($"[NightRaid] 第 {_clock.Day} 天袭击者潜入：{_nightRaiders.Count} 人，意图 {intent}，逼近对抗中……");
    }

    /// <summary>门外错峰生成一波袭击者（目标池=幸存者+布鲁斯；持匕首=消音近战贴合潜入，拟定待调）。</summary>
    private void SpawnNightRaiders(int count)
    {
        Rect2 wander = new(
            _mapBounds.Position + new Vector2(200, 200),
            _mapBounds.Size - new Vector2(400, 400));
        for (int i = 0; i < count; i++)
        {
            var r = Raider.Create(wander, AlliedTargets, usePistol: false, displayName: "夜袭者");
            r.Inject(_combat, _clock);
            r.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter);
            r.ConfigurePerception(localLightAt: SampleCampLight);
            bool south = (i & 1) == 0;
            float gx = 1110f + (i * 47f) % 180f;
            float gy = south ? 1540f + (i % 3) * 12f : 260f - (i % 3) * 12f;
            r.Position = new Vector2(gx, gy);
            _actorLayer.AddChild(r);
            r.Died += OnNightRaiderDied;
            _nightRaiders.Add(r);
        }
    }

    private void OnNightRaiderDied(Actor a)
    {
        if (a is Raider r)
            _nightRaiders.Remove(r);
    }

    /// <summary>
    /// 结算一次「警戒 vs 潜行」对抗（对已选尖兵）：每个存活守卫（含犬，站岗效率 0.75）用其视力（VisionLogic 锥+光照+遮挡，
    /// 疲劳者视锥经 FatigueDebuff 缩放）、听力、岗哨建筑加成得警戒力，对尖兵潜行力（局部光照/距离/遮蔽）各自掷点——
    /// 任一守卫掷中即全营发现（ResolveCampDetection 聚合全营=该轴感知内守卫警戒有效、远处贡献≈0，覆盖「该轴所有守卫」口径）。
    /// 无守卫=没人望风=未发现。逼近分段调用（每距离带一次），检测跨带累积。
    /// </summary>
    private bool RunNightRaidContest(Raider point)
    {
        Vector2 pPos = point.GlobalPosition;
        float pLight = SampleCampLight(pPos);

        // 守卫（人+犬）名单及其站岗效率。
        var watchers = new List<(Actor actor, float watchEff)>();
        foreach (Pawn g in _raidGuards)
            if (g.Alive) watchers.Add((g, 1f));
        foreach (Dog d in _raidGuardDogs)
            if (d.Alive) watchers.Add((d, DougBruceBond.BruceGuardEfficiency)); // 布鲁斯 0.75
        if (watchers.Count == 0)
            return false; // 没人站岗 → 长驱直入，恒未发现

        float nearestGuardDist = watchers.Min(w => w.actor.GlobalPosition.DistanceTo(pPos));
        float coverWeight = SelfSightOccluded(watchers[0].actor.GlobalPosition, pPos) ? 0.5f : 0f; // 粗略遮蔽项（拟定待调）
        float stealth = NightWatchContest.ComputeStealth(pLight, apparelStealthSum: 0f, nearestGuardDist, coverWeight);

        var alertness = new List<float>(watchers.Count);
        foreach (var w in watchers)
        {
            float gLight = SampleCampLight(w.actor.GlobalPosition);
            VisionLogic.VisionCone cone = VisionLogic.ConeFor(gLight);
            // ★疲劳双路径（param-calibration 校准）：被唤醒守卫次相位——视力项经视锥缩窄(此处) + 听力项经系数削减(FatigueAdjustedAlertness)。
            bool fatigued = w.actor is Pawn wp && _activeFatigue.TryGetValue(wp.Id, out var fd) && fd.IsActive;
            if (fatigued && w.actor is Pawn wp2)
                cone = cone.Scaled(_activeFatigue[wp2.Id].VisionRangeMult, _activeFatigue[wp2.Id].VisionAngleMult);
            bool occluded = SelfSightOccluded(w.actor.GlobalPosition, pPos);
            bool canSee = VisionLogic.CanSee(Sn(w.actor.GlobalPosition), FacingUnit(w.actor.FacingAngle), Sn(pPos), cone, occluded);
            float dist = w.actor.GlobalPosition.DistanceTo(pPos);
            float acuity = NightWatchContest.VisionAcuity(canSee, dist, cone);
            float structBonus = NightRaidLogic.StructureBonusFrom(_guardPostSightById.GetValueOrDefault(ActorId(w.actor), 1f));
            // 疲劳双路径：视力已经视锥削(上)，听力项经 FatigueHearingMult 削(此)——不叠全局警戒标量，避免双重惩罚（单一真源与 Sim 校准同调）。
            alertness.Add(NightRaidLogic.FatigueAdjustedAlertness(
                acuity, dist, structBonus, w.watchEff, fatigued, NightWatchContest.HearingBaseRange));
        }

        return NightRaidLogic.ResolveCampDetection(alertness, stealth, _raidContestRng);
    }

    /// <summary>发现袭击者：暂停世界 + 弹三档响应面板（复用通用 <see cref="ChoicePanel"/>），展示守卫目击的模糊规模情报。</summary>
    private void ShowRaidResponsePanel()
    {
        CapturePanelTimeState(out _prevRaidResponseSpeed, out _prevRaidResponsePaused);

        int raiderCount = _nightRaiders.Count(r => r.Alive);
        string intel = NightRaidLogic.ThreatLabel(NightRaidLogic.BandFor(raiderCount));

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            $"守夜的人拉响了警报——{intel}。\n如何应对？",
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = (int)RaidTier.Low, Label = "低危应对",
                        Description = "只有站岗的人迎战，其余人继续休整",
                        Accent = new Color(0.35f, 0.55f, 0.35f) },
                new() { Value = (int)RaidTier.Medium, Label = "中危应对",
                        Description = "除熟睡的探险队外，全营投入战斗",
                        Accent = new Color(0.6f, 0.5f, 0.25f) },
                new() { Value = (int)RaidTier.High, Label = "高危应对",
                        Description = "叫醒所有人（含探险队）全员死战——次日疲劳",
                        Accent = new Color(0.65f, 0.25f, 0.22f) },
            });
        panel.Confirmed += v =>
        {
            panel.QueueFree();
            RestorePanelTimeState(_prevRaidResponseSpeed, _prevRaidResponsePaused);
            OnRaidTierChosen((RaidTier)v);
        };
    }

    /// <summary>
    /// 玩家选定响应档 → 拉人入战：<see cref="NightWatchContest.RespondersFor"/> 出参战名册，被点名者唤醒并锁最近袭击者；
    /// 被唤醒的睡眠者（High 档唤醒探险队）→ <see cref="ShiftSchedule.DebuffsForAwoken"/> 挂次相位疲劳 debuff（下个在勤相位兑现）。
    /// </summary>
    private void OnRaidTierChosen(RaidTier tier)
    {
        var roster = BuildWatchRoster();
        var responders = NightWatchContest.RespondersFor(tier, roster);
        _respondingIds.Clear();
        foreach (var m in responders)
            if (int.TryParse(m.Id, out int id))
                _respondingIds.Add(id);

        Raider? firstTarget = _nightRaiders.FirstOrDefault(r => r.Alive);
        foreach (Pawn p in _survivors)
        {
            if (!p.Alive || !_respondingIds.Contains(p.Id))
                continue;
            p.SetSleeping(false); // 唤醒参战者（守卫已醒；生产者/被唤醒探险队在此醒来）
            if (firstTarget != null && !p.HasActiveTarget)
                p.CommandAttack(firstTarget);
        }

        // 被唤醒睡眠者 → 次相位疲劳（duration=1 相位，UpdateFatigueTimers 在其次个在勤相位施加、过后清）。
        var debuffs = ShiftSchedule.DebuffsForAwoken(tier, roster);
        foreach (var kv in debuffs)
            if (int.TryParse(kv.Key, out int id))
                _pendingFatigueWake[id] = kv.Value;
        if (debuffs.Count > 0)
            GD.Print($"[NightRaid] {debuffs.Count} 名睡眠者被唤醒参战，次相位吃疲劳 debuff。");

        GD.Print($"[NightRaid] 响应档 {tier}：{_respondingIds.Count} 人迎战。");
    }

    /// <summary>未被发现的后果：Looter 静默偷物资后悄然撤离（晨间才发现）；Killer 潜行先手 1.5x 后惊动全营转入战斗。</summary>
    private void ResolveUndetected()
    {
        _nightRaidResolved = true; // 决出（未发现）：此后守卫方允许迎战（Killer 惊营后）/或已撤离（Looter）
        if (_nightRaidIntent == RaiderIntent.Looter)
        {
            ApplySilentTheft();
            EndNightRaid(); // 得手，悄然撤离（不开战）
            return;
        }
        // Killer：潜行先手一击落在最近幸存者，随后惊动全营 → 守卫自动迎战（无面板=未发现没得选，等价低危）。
        ApplyPreemptiveStrike();
    }

    /// <summary>潜行先手一击：以匕首（消音近战）对最近幸存者施一次 <see cref="NightWatchContest.PreemptiveDamageMultiplier"/>(1.5x) 承伤（走既有承伤管道，不改战斗规则）。</summary>
    private void ApplyPreemptiveStrike()
    {
        var raider = _nightRaiders.FirstOrDefault(r => r.Alive);
        if (raider is null)
            return;
        Pawn? victim = _survivors.Where(s => s.Alive)
            .OrderBy(s => s.GlobalPosition.DistanceSquaredTo(raider.GlobalPosition))
            .FirstOrDefault();
        if (victim is null)
            return;
        double mult = NightWatchContest.PreemptiveDamageMultiplier(_nightRaidIntent); // Killer → 1.5
        victim.ReceiveAttack(raider, CombatData.Dagger(), _combat, damageFactor: mult);
        _campToast.Show($"{victim.DisplayName} 在睡梦中遭到潜行偷袭！", CampToast.Bad);
        GD.Print($"[NightRaid] 潜行先手 ×{mult:0.0} 命中 {victim.DisplayName}。");
    }

    /// <summary>静默偷窃：按 <see cref="NightWatchContest.SilentTheftAmount"/> 从营地食物实扣（可偷存量封顶），记待晨间提示。</summary>
    private void ApplySilentTheft()
    {
        int stealable = _resources.Food; // 可偷存量=营地食物（最易变现；材料/白银偷窃后续可扩）
        int amount = NightWatchContest.SilentTheftAmount(stealable, _raidContestRng);
        if (amount <= 0)
            return;
        _resources.ApplyRaidLoss(amount);
        _pendingTheftAmount += amount;
        GD.Print($"[NightRaid] 静默偷窃：损失食物 {amount}（晨间提示）。");
    }

    /// <summary>DawnMeal 晨间清点：若夜里被静默偷窃则一行提示后清零（Looter 未发现后果的呈现）。</summary>
    private void ReportNightTheftIfAny()
    {
        if (_pendingTheftAmount <= 0)
            return;
        _campToast.Show($"清晨清点：夜里被摸走了 {_pendingTheftAmount} 份食物。", CampToast.Bad);
        _pendingTheftAmount = 0;
    }

    /// <summary>
    /// 夜袭者实时推进（节流）：<b>决出前</b>随尖兵逼近分段掷对抗（守卫未警觉不迎战——潜行涌现）；
    /// <b>决出后</b>守卫/犬/被点名参战者无目标时锁最近袭击者迎战。袭击者清光/撤离即收尾。防御方伤亡由实时战斗自然产生。
    /// </summary>
    private void UpdateNightRaid(double delta)
    {
        _nightRaidUpdateElapsed += delta;
        if (_nightRaidUpdateElapsed < RaidUpdateInterval)
            return;
        _nightRaidUpdateElapsed = 0;

        var raiders = _nightRaiders.Where(r => r.Alive).ToList();
        if (raiders.Count == 0)
        {
            EndNightRaid(); // 全部袭击者伏诛/撤离 → 收尾
            return;
        }

        // 决出前：逼近分段对抗（守卫尚未警觉，不迎战）。
        if (!_nightRaidResolved)
        {
            TickApproachContest(raiders);
            return;
        }

        foreach (Pawn g in _raidGuards)
        {
            if (!g.Alive || g.HasActiveTarget)
                continue;
            Raider? near = NearestRaiderTo(g.GlobalPosition, raiders);
            if (near != null) { g.TryFirstStrike(near); g.CommandAttack(near); }
        }
        foreach (Dog d in _raidGuardDogs)
        {
            if (!d.Alive || d.HasActiveTarget)
                continue;
            Raider? near = NearestRaiderTo(d.GlobalPosition, raiders);
            if (near != null) d.CommandAttack(near);
        }
        foreach (Pawn p in _survivors)
        {
            if (!p.Alive || !_respondingIds.Contains(p.Id) || p.HasActiveTarget)
                continue;
            Raider? near = NearestRaiderTo(p.GlobalPosition, raiders);
            if (near != null) p.CommandAttack(near);
        }
    }

    /// <summary>
    /// 逼近分段对抗（决出前每节流帧调，param-calibration 校准）：尖兵=离营心最近袭击者；对最近守卫的距离每跨过一条
    /// <see cref="NightRaidContestBands"/> 距离带即掷一次对抗（检测跨带累积，任一带掷中→发现→暂停+响应面板）；
    /// 若深入到营心 <see cref="NightRaidStrikeDistance"/> 仍未发现（含无守卫），兑现未发现后果（先手/偷窃）。
    /// </summary>
    private void TickApproachContest(List<Raider> raiders)
    {
        Raider point = raiders.OrderBy(r => r.GlobalPosition.DistanceSquaredTo(_cameraCenter)).First();
        Vector2 pPos = point.GlobalPosition;

        float nearestGuardDist = float.MaxValue;
        foreach (Pawn g in _raidGuards)
            if (g.Alive) nearestGuardDist = Mathf.Min(nearestGuardDist, g.GlobalPosition.DistanceTo(pPos));
        foreach (Dog d in _raidGuardDogs)
            if (d.Alive) nearestGuardDist = Mathf.Min(nearestGuardDist, d.GlobalPosition.DistanceTo(pPos));

        for (int b = 0; b < NightRaidContestBands.Length; b++)
        {
            if (_nightRaidRolledBands.Contains(b))
                continue;
            if (nearestGuardDist > NightRaidContestBands[b])
                continue; // 尖兵尚未逼近到本距离带
            _nightRaidRolledBands.Add(b);
            if (RunNightRaidContest(point))
            {
                _nightRaidResolved = true;
                GD.Print($"[NightRaid] 第 {b} 距离带（≤{NightRaidContestBands[b]:0}px）守卫掷中——发现袭击者！");
                ShowRaidResponsePanel();
                return;
            }
        }

        // 深入营心仍未发现 → 兑现未发现后果（先手/偷窃）。
        if (pPos.DistanceTo(_cameraCenter) <= NightRaidStrikeDistance)
            ResolveUndetected();
    }

    /// <summary>夜袭收尾：清残留袭击者、复位响应态（EndRaid 天亮调 + 全灭袭击者时调）。</summary>
    private void EndNightRaid()
    {
        if (!_nightRaidActive && _nightRaiders.Count == 0)
            return;
        _nightRaidActive = false;
        _respondingIds.Clear();
        foreach (Raider r in _nightRaiders)
            if (IsInstanceValid(r))
                r.QueueFree();
        _nightRaiders.Clear();
    }

    private static Raider? NearestRaiderTo(Vector2 from, List<Raider> raiders)
    {
        Raider? best = null;
        float bestSq = float.MaxValue;
        foreach (Raider r in raiders)
        {
            float d = from.DistanceSquaredTo(r.GlobalPosition);
            if (d < bestSq) { bestSq = d; best = r; }
        }
        return best;
    }

    /// <summary>构造夜防三档响应名册：探险队(在 _todaysExpeditionIds)=睡眠中的 Expedition；上岗守卫=Guard；其余在营者=Producer。</summary>
    private List<WatchMember> BuildWatchRoster()
    {
        var roster = new List<WatchMember>();
        var guardIds = new HashSet<int>(_raidGuards.Select(g => g.Id));
        foreach (Pawn p in _survivors.Where(s => s.Alive))
        {
            WatchDuty duty;
            bool asleep;
            if (_todaysExpeditionIds.Contains(p.Id)) { duty = WatchDuty.Expedition; asleep = true; }
            else if (guardIds.Contains(p.Id)) { duty = WatchDuty.Guard; asleep = false; }
            else { duty = WatchDuty.Producer; asleep = false; }
            roster.Add(new WatchMember(p.Id.ToString(), duty, asleep));
        }
        return roster;
    }

    /// <summary>双班硬日程（SPEC-B6①）：夜里让当日探险队(DayCrew)强制睡（白天在外奔波）；营地留守(NightCrew)保持清醒站岗/生产。</summary>
    private void ApplyNightShiftSleep()
    {
        foreach (Pawn p in _survivors)
            if (p.Alive && _todaysExpeditionIds.Contains(p.Id))
                p.SetSleeping(true);
    }

    /// <summary>
    /// 次相位疲劳的施加/过期（每相位切换调）：先清上一相位已生效的疲劳（duration=1 相位）；再把待施加者在其**次个在勤相位**兑现。
    /// 被唤醒者多为探险队(DayCrew)，其次个在勤相位=次日白天；过一相位后随下次清除。视野/攻速系数消费见 <see cref="FatigueMultiplierFor"/> 及生产工时。
    /// </summary>
    private void UpdateFatigueTimers(DayPhase phase)
    {
        _activeFatigue.Clear();
        foreach (var kv in _pendingFatigueWake.ToList())
        {
            int id = kv.Key;
            Pawn? p = _survivors.FirstOrDefault(s => s.Id == id && s.Alive);
            if (p is null) { _pendingFatigueWake.Remove(id); continue; }
            Shift shift = ShiftSchedule.ShiftFor(id, _todaysExpeditionIds);
            if (ShiftSchedule.CanOperate(shift, phase)) // 到达其在勤相位 → 兑现次相位代价
            {
                _activeFatigue[id] = kv.Value;
                _pendingFatigueWake.Remove(id);
            }
        }
    }

    /// <summary>某 Actor 当前的疲劳能力系数（作 <see cref="NightWatchContest.ComputeAlertness"/> 疲劳项 / 生产效率折减）：有生效疲劳→其 EfficiencyMult，否则 1。</summary>
    private float FatigueMultiplierFor(Actor a)
        => a is Pawn p && _activeFatigue.TryGetValue(p.Id, out var f) && f.IsActive ? f.EfficiencyMult : 1f;

    private static int ActorId(Actor a) => a switch { Pawn p => p.Id, Dog d => d.Id, _ => -1 };

    // VisionLogic 零 Godot 依赖，坐标用 System.Numerics.Vector2；此处转换（与 Actor 的同款）。
    private static System.Numerics.Vector2 Sn(Vector2 v) => new(v.X, v.Y);
    private static System.Numerics.Vector2 FacingUnit(float rad) => new(Mathf.Cos(rad), Mathf.Sin(rad));

    /// <summary>两点间视线是否被墙遮挡（复用批次4 遮挡唯一权威源 <see cref="VisionOcclusion.IsOccluded"/>，墙层 0b0100）。</summary>
    private bool SelfSightOccluded(Vector2 from, Vector2 to)
    {
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        return space is not null && VisionOcclusion.IsOccluded(space, from, to);
    }

    /// <summary>
    /// 尸潮抵达终局：启动无限围攻（波次不停轮、逐波递增，直至全灭）。到期夜由 <see cref="OnGamePhaseChanged"/> NightAct 调。
    /// 复用袭营执行层：置 <c>_raidActive</c> 借 <see cref="UpdateRaid"/> 的守卫锁敌 + 破防统计，但走 <c>_siegeActive</c>
    /// 分支不做胜负结算。首波由首个 UpdateRaid tick 按 <see cref="HordeTimeline.NextWave"/>（waveIndex=0 立即投）投放。
    /// **无生还路线，有意为之的黑暗设定，不软化。**
    /// </summary>
    private void TriggerHordeSiege()
    {
        if (_siegeActive)
            return;
        _siegeActive = true;
        _raidActive = true;
        _siegeWaveIndex = 0;
        _siegeWaveElapsed = 0;
        _storyFlags.Set(HordeTimeline.ArrivedFlag, "true");
        GD.Print($"[Horde] 第 {_clock.Day} 天：尸潮抵达，无限围攻开始——无生还路线，直至全灭。");
    }

    /// <summary>照 TestExploration 模板在大门缺口外生成一波丧尸：Inject(_combat,_clock)、挂 _actorLayer、涌入营地。</summary>
    private void SpawnCampZombies(int count)
    {
        // 游荡范围 = 营地内部（丧尸从门外向营心推进，被守卫/破防线拦截）。
        Rect2 wander = new(
            _mapBounds.Position + new Vector2(200, 200),
            _mapBounds.Size - new Vector2(400, 400));

        for (int i = 0; i < count; i++)
        {
            // 门外错峰生成：南北两门缺口 x∈[1100,1300]，交替从南门(y>1500)、北门(y<300)外涌入。
            bool south = (i & 1) == 0;
            float gx = 1110f + (i * 43f) % 180f;
            float gy = south ? 1540f + (i % 3) * 14f : 260f - (i % 3) * 14f;

            var z = Zombie.Create(wander, AlliedTargets); // 目标池含布鲁斯（可被丧尸攻击/杀）
            z.Inject(_combat, _clock); // 与营地单位同一 combat+clock，务必首帧 Think 前完成
            z.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter); // 门关闭→砸墙破防
            z.ConfigurePerception(localLightAt: SampleCampLight); // 固定+手持光源→局部光照喂给（暴露走目标 CarriedLightIntensity 回落）
            z.Position = new Vector2(gx, gy);
            _actorLayer.AddChild(z);
            z.Died += OnRaidZombieDied;
            _raidZombies.Add(z);
        }
    }

    private void OnRaidZombieDied(Actor a)
    {
        if (a is Zombie z)
            _raidZombies.Remove(z);
    }

    // ---------------- D4 结算 ----------------

    /// <summary>
    /// 袭营实时推进（仅 _raidActive 时逐帧调用）：守卫巡防锁敌（暗哨触发一次性首发）+ 破防/胜负检测。
    /// </summary>
    private void UpdateRaid(double delta)
    {
        // 尸潮终局：波次间隔计时逐帧累积（不受 0.15s 节流影响，喂 HordeTimeline.NextWave）。
        if (_siegeActive)
            _siegeWaveElapsed += delta;

        // 节流：守卫锁敌 + 破防/胜负统计每 ~0.15s 一次（非每帧）。守卫锁敌迟 ≤0.15s 反应可忽略，
        // 破防/全灭是状态检测非边沿事件（丧尸驻留圈内），晚一拍判定不漏。delta 已被时标缩放（暂停即冻结）。
        _raidUpdateElapsed += delta;
        if (_raidUpdateElapsed < RaidUpdateInterval)
            return;
        _raidUpdateElapsed = 0;

        // 守卫巡防：无目标时取侦测半径内最近丧尸交战（是否真正开火由 Pawn.Think Guard 的射程判定裁决）。
        // 距离比较用平方（DistanceSquaredTo）免开方。
        foreach (Pawn g in _raidGuards)
        {
            if (!g.Alive || g.HasActiveTarget)
                continue;
            Zombie? nearest = null;
            float best = g.GuardSightRadius * g.GuardSightRadius;
            foreach (Zombie z in _raidZombies)
            {
                if (!z.Alive)
                    continue;
                float d = g.GlobalPosition.DistanceSquaredTo(z.GlobalPosition);
                if (d < best)
                {
                    best = d;
                    nearest = z;
                }
            }
            if (nearest != null)
            {
                g.TryFirstStrike(nearest); // 暗哨首发（一次性）
                g.CommandAttack(nearest);
            }
        }

        // 犬类守卫（布鲁斯）巡防：无目标时取 75% 效率锁敌半径内最近丧尸缠斗（无暗哨首发）。
        foreach (Dog dog in _raidGuardDogs)
        {
            if (!dog.Alive || dog.HasActiveTarget)
                continue;
            Zombie? nearest = null;
            float best = dog.GuardSightRadiusScaled * dog.GuardSightRadiusScaled; // 75% 效率半径
            foreach (Zombie z in _raidZombies)
            {
                if (!z.Alive)
                    continue;
                float d = dog.GlobalPosition.DistanceSquaredTo(z.GlobalPosition);
                if (d < best)
                {
                    best = d;
                    nearest = z;
                }
            }
            if (nearest != null)
                dog.CommandAttack(nearest);
        }

        // 破防线 + 存活统计合成单遍：破防=任一丧尸摸进营心 BreachRadius（平方比较）内；同遍数存活丧尸。
        float breachSq = BreachRadius * BreachRadius;
        bool breached = false;
        int zombiesRemaining = 0;
        foreach (Zombie z in _raidZombies)
        {
            if (!z.Alive)
                continue;
            zombiesRemaining++;
            if (!breached && z.GlobalPosition.DistanceSquaredTo(_cameraCenter) < breachSq)
                breached = true;
        }
        int guardsAlive = 0;
        foreach (Pawn g in _raidGuards)
        {
            if (g.Alive)
                guardsAlive++;
        }
        foreach (Dog dog in _raidGuardDogs) // 犬类守卫也计入"守卫存活"（关系非围攻夜的守住/被破结算）
        {
            if (dog.Alive)
                guardsAlive++;
        }

        // 尸潮终局：不做胜负结算（无「守住」出口），只按调度补投下一波——波次不停轮、逐波递增。
        // 唯一终止是全灭（Pawn.Died → GameOverCondition，见 OnSurvivorDied）。破防/守卫全倒都不结束围攻。
        if (_siegeActive)
        {
            SiegeWave dec = HordeTimeline.NextWave(
                _siegeWaveIndex, zombiesRemaining, _siegeWaveElapsed, _survivors.Count(s => s.Alive),
                HordeTimeline.MaxConcurrentSiege); // 在场并发上限：达上限本波不投，封 day40 无界实体崩塌（波次仍轮询）
            if (dec.ShouldSpawn)
            {
                SpawnCampZombies(dec.Count);
                _siegeWaveIndex++;
                _siegeWaveElapsed = 0;
            }
            return;
        }

        RaidEvaluation eval = RaidResolution.Evaluate(zombiesRemaining, guardsAlive, breached);
        if (eval.State != RaidState.Ongoing)
            FinishRaid(eval);
    }

    /// <summary>结算收口：守住无损失；被攻破按 <see cref="RaidResolution.ConsequenceFor"/> 扣食物。</summary>
    private void FinishRaid(RaidEvaluation eval)
    {
        _raidActive = false;
        RaidConsequence cons = RaidResolution.ConsequenceFor(eval);
        if (eval.State == RaidState.Overrun)
        {
            _resources.ApplyRaidLoss(cons.FoodLoss);
            GD.Print($"[Raid] 防御战失败（{eval.Reason}）：损食物 {cons.FoodLoss}。伤亡在实时战斗中自然产生。");
        }
        else
        {
            GD.Print("[Raid] 防御战守住：丧尸全灭。");
        }
        // 残留丧尸（破防/守卫全倒时仍在场者）清场，避免拖到黎明。
        foreach (Zombie z in _raidZombies)
        {
            if (IsInstanceValid(z))
                z.QueueFree();
        }
        _raidZombies.Clear();
    }

    /// <summary>夜晚结束：清残留丧尸、守卫下岗撤销加成（DawnMeal 调用）。</summary>
    private void EndRaid()
    {
        _raidActive = false;
        EndNightRaid(); // 批次6：夜袭者若跨到黎明仍在场，天亮统一收尾（清残留袭击者、复位响应态）
        // 终局围攻若跨到黎明仍未全灭（罕见）：本夜收口，下一 Arrived 夜的 NightAct 会重新触发（仍无生还）。
        _siegeActive = false;
        foreach (Zombie z in _raidZombies)
        {
            if (IsInstanceValid(z))
                z.QueueFree();
        }
        _raidZombies.Clear();
        _revealDelegates.Clear(); // 夜袭结束：清揭示委托缓存，释放对已回收敌人的引用
        foreach (Pawn g in _raidGuards)
        {
            if (IsInstanceValid(g) && g.Alive)
            {
                g.ClearGuardPost();
                g.Stationing = false;
            }
        }
        _raidGuards.Clear();
        foreach (Dog dog in _raidGuardDogs) // 犬类守卫下岗：撤岗位加成 + 解除驻守（恢复跟随/自主缠斗）
        {
            if (IsInstanceValid(dog) && dog.Alive)
            {
                dog.ClearGuardPost();
                dog.GuardStationing = false;
            }
        }
        _raidGuardDogs.Clear();

        // 极端情况：夜晚耗尽仍未分胜负（教学关战斗未收口）→ 天亮统一清场，防止拖入白天。
        if (_tutorialActive || _tutorialRaiders.Count > 0 || _christine != null)
            CleanupChristineTutorial();
    }

    // ---------------- 教学关：克莉丝汀反水编排 ----------------

    /// <summary>
    /// 第 2 夜脚本化开场：门外固定生成 2 个劫掠者 + 克莉丝汀（皆 Raider 阵营、起手打幸存者）。
    /// 不走 <see cref="RaidWave"/> 概率。生成模板沿用 <see cref="SpawnCampZombies"/>（门缝错峰、Inject、挂 _actorLayer）。
    /// </summary>
    private void BeginChristineTutorial()
    {
        _storyFlags.Set("tutorial_raider_started", "true");
        _tutorialActive = true;
        _christineTurned = false;
        _tutorialRaiders.Clear();

        // 无目标时的门外游荡范围（与丧尸同款：向营心推进被守卫/破防线拦截）。
        Rect2 wander = new(
            _mapBounds.Position + new Vector2(200, 200),
            _mapBounds.Size - new Vector2(400, 400));

        // 2 个普通劫掠者：目标池含幸存者 + 克莉丝汀（反水后克莉丝汀变 Survivor，IsHostile 自动让劫掠者也打她）。
        for (int i = 0; i < TutorialRaiderCount; i++)
        {
            var r = Raider.Create(wander, TutorialRaiderTargets, usePistol: true);
            r.Inject(_combat, _clock); // 首帧 Think 前完成注入
            r.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter); // 门关闭→砸墙破防
            r.ConfigurePerception(localLightAt: SampleCampLight); // 光源→局部光照喂给
            r.Position = new Vector2(1120f + i * 60f, 1540f + (i % 2) * 12f); // 南门外错峰
            _actorLayer.AddChild(r);
            r.Died += OnTutorialRaiderDied;
            _tutorialRaiders.Add(r);
        }

        // 克莉丝汀：起手 Faction=Raider、targetProvider=己方池（幸存者+布鲁斯，打幸存者/狗），与两名同伙一致。
        _christine = Raider.Create(
            wander,
            AlliedTargets,
            usePistol: true,
            displayName: "克莉丝汀");
        _christine.Inject(_combat, _clock);
        _christine.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter); // 门关闭→砸墙破防
        _christine.ConfigurePerception(localLightAt: SampleCampLight); // 光源→局部光照喂给
        _christine.Position = new Vector2(1200f, 1560f); // 南门外
        _actorLayer.AddChild(_christine);
        _christine.Died += OnChristineDied;

        GD.Print($"[教学关] 第 {_clock.Day} 夜：{TutorialRaiderCount} 名劫掠者 + 克莉丝汀自大门袭击营地。");
    }

    /// <summary>劫掠者的择敌候选池：存活幸存者 + 克莉丝汀（反水后她变敌对，无需另改劫掠者）。</summary>
    private IEnumerable<Actor> TutorialRaiderTargets()
    {
        foreach (Pawn s in _survivors)
            if (s.Alive)
                yield return s;
        if (_christine is { Alive: true })
            yield return _christine;
        if (_bruce is { Alive: true })
            yield return _bruce; // 布鲁斯可被劫掠者攻击/杀
    }

    private void OnTutorialRaiderDied(Actor a)
    {
        if (a is Raider r)
            _tutorialRaiders.Remove(r);
    }

    private void OnChristineDied(Actor a)
    {
        // 克莉丝汀战死（Actor.Die 会随后 QueueFree 本节点）：立即置空引用，避免后续帧 use-after-free。
        // 若死在结算前 → 支线不触发（黑暗向，有意为之），FinishChristineTutorial 据 _christine==null 不弹抉择。
        _christine = null;
        GD.Print("[教学关] 克莉丝汀战死。");
    }

    /// <summary>
    /// 逐帧推进教学关：反水监测（未反水时）+ 胜负复用判定。仅 <see cref="_tutorialActive"/> 时调用。
    /// </summary>
    private void UpdateChristineTutorial(double delta)
    {
        // ① 反水监测：任一劫掠者受伤较重 或 克莉丝汀自己受伤 → 翻转。
        if (!_christineTurned && _christine is { Alive: true })
        {
            IEnumerable<float> raiderHps = _tutorialRaiders.Where(r => r.Alive).Select(r => r.HealthFraction);
            if (TutorialRaidLogic.ShouldTurncoat(raiderHps, _christine.HealthFraction))
                TurnChristine();
        }

        // ② 胜负：复用 RaidResolution，把"敌方剩余数"（劫掠者 + 未反水的克莉丝汀）当"敌人数"喂入。
        //    守卫全倒沿用"幸存者存活数"（全灭 = 负）；破防线沿用 BreachRadius。
        bool christineHostile = _christine is { Alive: true } && !_christineTurned;
        int raidersAlive = _tutorialRaiders.Count(r => r.Alive);
        int enemiesRemaining = TutorialRaidLogic.EnemiesRemaining(raidersAlive, christineHostile);
        int defendersAlive = _survivors.Count(s => s.Alive);
        bool breached = _tutorialRaiders.Any(r => r.Alive && r.GlobalPosition.DistanceTo(_cameraCenter) < BreachRadius)
                        || (christineHostile && _christine!.GlobalPosition.DistanceTo(_cameraCenter) < BreachRadius);

        RaidEvaluation eval = RaidResolution.Evaluate(enemiesRemaining, defendersAlive, breached);
        if (eval.State != RaidState.Ongoing)
            FinishChristineTutorial(eval);
    }

    /// <summary>
    /// 反水：头顶飘台词 + 切 Survivor 阵营 + 换目标池为劫掠者 → 她变**盟友 AI**（自动打劫掠者，玩家不可控）。
    /// 劫掠者目标池（<see cref="TutorialRaiderTargets"/>）已含她，切阵营后 IsHostile 自动令双方互为敌。
    /// </summary>
    private void TurnChristine()
    {
        _christineTurned = true;
        FloatingText.Spawn(_actorLayer, _christine!.GlobalPosition,
            "杀死这些劫掠者！我是好人！", new Color(1f, 0.9f, 0.4f));
        _christine.SetFaction(Faction.Survivor);
        _christine.SetTargetProvider(() => _tutorialRaiders.Where(r => r.Alive).Cast<Actor>());
        _christine.CancelOrders(); // 立即丢弃"打幸存者"的旧指令，下一帧 Think 重新对劫掠者择敌
        GD.Print("[教学关] 克莉丝汀反水：切换 Survivor 阵营，转而攻击劫掠者。");
    }

    /// <summary>
    /// 战斗收口：清残留劫掠者、置 done flag、被攻破按 RaidResolution 后果扣资源。
    /// **仅击退(Defended)且克莉丝汀存活**才弹抉择面板；失利(Overrun，含她独活)或她战死 → 支线不触发，直接收尾。
    /// </summary>
    private void FinishChristineTutorial(RaidEvaluation eval)
    {
        _tutorialActive = false;
        foreach (Raider r in _tutorialRaiders)
            if (IsInstanceValid(r))
                r.QueueFree();
        _tutorialRaiders.Clear();
        _storyFlags.Set("tutorial_raider_done", "true");

        if (eval.State == RaidState.Overrun)
        {
            RaidConsequence cons = RaidResolution.ConsequenceFor(eval);
            _resources.ApplyRaidLoss(cons.FoodLoss);
            GD.Print($"[教学关] 袭击失利（{eval.Reason}）：损食物 {cons.FoodLoss}。");
        }
        else
        {
            GD.Print("[教学关] 击退劫掠者。");
        }

        // 仅"击退劫掠者(Defended) 且 克莉丝汀存活"才弹抉择。失利(Overrun，向空营/败局收留无意义)
        // 或她战死 → 不弹，直接收尾。
        if (eval.State == RaidState.Defended && _christine is { Alive: true })
        {
            PromptChristineChoice();
        }
        else
        {
            // 失利或战死：支线不触发（黑暗向）。她若失利仍存活，清掉残留节点，不留孤儿单位。
            if (_christine != null && IsInstanceValid(_christine))
                _christine.QueueFree();
            _christine = null;
        }
    }

    /// <summary>战后暂停（TimeScale=0）弹收留/放逐/处决三选一。</summary>
    private void PromptChristineChoice()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.ForChristine(
            "劫掠者已被清剿。克莉丝汀瘫坐在血泊边，抬头望向你：\n" +
            "「我不是他们一伙的……是他们逼我带路的。求你，让我留下——我能帮上忙。」");
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            HandleChristineChoice((ChristineChoice)v);
            panel.QueueFree();
        };
    }

    private void HandleChristineChoice(ChristineChoice choice)
    {
        _storyFlags.Set("tutorial_raider_done", "true");
        if (_christine == null || !IsInstanceValid(_christine))
        {
            _christine = null;
            return;
        }

        switch (choice)
        {
            case ChristineChoice.Recruit:
                RecruitChristine();
                break;
            case ChristineChoice.Exile:
                // 放逐：让她走向门外后消失（活着离开，不留尸不流血）。
                WalkOutAndDespawn(_christine);
                _christine = null;
                GD.Print("[教学关] 放逐克莉丝汀：她走向门外，消失在营地外。");
                break;
            case ChristineChoice.Execute:
                // 处决：脚下留一摊浓血后当场消失（不做倒地尸体视觉）。
                SpawnDeathBlood(_christine);
                _christine.QueueFree();
                _christine = null;
                GD.Print("[教学关] 处决克莉丝汀：地上留下一摊血。");
                break;
        }
    }

    // ---------------- 克莉丝汀请求线：请求出兵清剿金手指帮 ----------------

    /// <summary>当前在营存活的克莉丝汀 Pawn（招募后）；不在场（未招募/已亡故/已离开）返回 null。</summary>
    private Pawn? ChristinePawn() =>
        _survivors.FirstOrDefault(p => p.Alive && p.DisplayName == ChristineName);

    // ---------------- 电台主线（收音机交互 / 抉择 / 军袭钩子） ----------------

    /// <summary>
    /// 营地收音机交互（右键前往到位时调，role=="radio"）：
    ///   · 已持发出设备且未做终局抉择 → 弹抉择入口（回复军方 / 呼叫南方 / 暂不）；
    ///   · 否则 → 收听军方循环广播（首次收听推进主线状态「已收听」，解锁主线知情）。
    /// 文案（广播/抉择/确认）皆 <see cref="RadioMainline"/> 的 draft 常量，供用户改。
    /// </summary>
    private void OpenRadio()
    {
        // 已呼叫南方（结局③）：电台成为南逃指挥入口——未答满三问则续答，答满则提供启程南逃。
        if (RadioMainline.Stage(_storyFlags) == RadioMainlineStage.CalledSouth)
        {
            if (!SouthTrial.IsComplete(_storyFlags))
                StartSouthTrial(); // 三问未答满（如中途离开）：从当前题续答
            else if (!SouthTrial.HasDeparted(_storyFlags))
                PromptSouthDeparture(); // 已通过考验、尚未启程：提供启程入口
            // 已启程则终局已由 CG③ 接管，不会再走到此
            return;
        }
        if (RadioMainline.IsDecisionAvailable(_storyFlags))
        {
            PromptRadioDecision();
            return;
        }
        // 收听：首次收听推进 Unknown→HeardBroadcast（已收听/已抉择则无操作，仍复播循环广播）。
        RadioMainline.MarkBroadcastHeard(_storyFlags);
        ShowDiscoveryNarrative("军方救援频段", RadioMainline.MilitaryBroadcastLoop);
    }

    /// <summary>
    /// 持发出设备后的电台抉择入口（复用通用 <see cref="ChoicePanel"/>）：回复军方 / 呼叫南方 / 暂不。
    /// 两条终局岔口不可逆，故选中后走二次确认；「暂不」直接关闭（可再来）。冻结时标、选完恢复。
    /// </summary>
    private void PromptRadioDecision()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            RadioMainline.DecisionPrompt,
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = RadioMainline.ReplyOptionLabel,
                        Description = "报出坐标，等军方派人前来", Accent = new Color(0.30f, 0.45f, 0.62f) },
                new() { Value = 2, Label = RadioMainline.CallSouthOptionLabel,
                        Description = "向南方营地求救，试着求一条生路", Accent = new Color(0.35f, 0.55f, 0.38f) },
                new() { Value = 0, Label = RadioMainline.DeferOptionLabel,
                        Description = "先不急，再想想", Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            switch (v)
            {
                case 1:
                    ConfirmRadioEnding(RadioMainline.ReplyConfirmPrompt, isReply: true);
                    break;
                case 2:
                    ConfirmRadioEnding(RadioMainline.CallSouthConfirmPrompt, isReply: false);
                    break;
                default:
                    break; // 暂不：直接关闭，可再来
            }
        };
    }

    /// <summary>
    /// 电台终局抉择的二次确认（不可逆，故独立一层确认面板）。确认→推进对应终态：
    ///   · 回复军方：<see cref="RadioMainline.ReplyToMilitary"/> 记录回复日（当天），第 3 天白天军袭到期（钩子在 <see cref="TryTriggerMilitaryRaid"/>）；
    ///   · 呼叫南方：<see cref="RadioMainline.CallSouth"/> 开启南逃线 flag（后续 authored）。
    /// 两者均不冻结尸潮时限（用户口径）。取消→回到未抉择态，可再来。
    /// </summary>
    private void ConfirmRadioEnding(string confirmPrompt, bool isReply)
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            confirmPrompt,
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = "确定", Description = "这个决定收不回来了",
                        Accent = new Color(0.62f, 0.25f, 0.22f) },
                new() { Value = 0, Label = "再想想", Description = "先回到上一步",
                        Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            if (v != 1)
            {
                PromptRadioDecision(); // 再想想：回到抉择入口
                return;
            }
            if (isReply)
            {
                RadioMainline.ReplyToMilitary(_storyFlags, _clock.Day);
                GD.Print($"[电台] 已回复军方（第 {_clock.Day} 天）。第 {_clock.Day + RadioMainline.MilitaryRaidDelayDays} 天白天军袭到期（结局②，事件本体待实装）。");
            }
            else
            {
                RadioMainline.CallSouth(_storyFlags);
                GD.Print("[电台] 已呼叫南方营地，南逃线开启（结局③）。开始南方三问考验。");
                StartSouthTrial(); // 呼叫南方 → 南方营地经电台抛来三问考验（结局③·南逃最小闭环）
            }
        };
    }

    /// <summary>
    /// 电台主线军袭到期钩子（每日黎明调）：回复军方后第 3 天（回复日+3）白天军袭到期。
    /// ★军袭事件本体不在本批（authored 待用户：军方白天屠杀留守者、外出探险队幸存归来见营地覆灭、
    ///   游戏不强制结束、续走结局①尸潮或③南逃）。本方法只在到期首次置事件钩子 flag（<see cref="RadioMainline.TryFireMilitaryRaidHook"/> 保证一次性），
    ///   TODO 挂点处安全 no-op（不改变现状；实装军袭时在此触发白天军袭演出/结算）。
    /// </summary>
    private void TryTriggerMilitaryRaid()
    {
        if (!RadioMainline.TryFireMilitaryRaidHook(_storyFlags, _clock.Day))
            return;
        // TODO(结局②·军方白天来袭)：此处触发 authored 军袭事件——白天屠杀留守者、外出探险队幸存归来见营地覆灭、
        //   游戏不强制结束（仅全员在营时才自然全灭）。军方动机保持不解释（[SPEC-B11] 留白）。
        //   ★军袭全灭 → CG②接线：实装时，若军袭屠尽留守者且此刻全员在营（无人外出）导致全灭，
        //     在屠杀分支置 _militaryRaidWipeContext = true，再让 Pawn.Died 走 OnSurvivorDied 全灭路由 → EndingCg.ForGameOver 选 CG②。
        //     CG② 文本已定稿（EndingCg.MilitaryWipe）、结局路由已就位，此处只差军袭事件本体（战斗结算）。
        // 本批为安全 no-op：仅记录钩子已触发，不改变现状（_militaryRaidWipeContext 保持 false）。
        GD.Print($"[电台] 第 {_clock.Day} 天：军方白天来袭到期（结局②事件钩子已触发，事件本体待实装，本批 no-op）。");
    }

    // ============ 结局③：南逃最小闭环（三问考验 → 启程 → CG③） ============

    /// <summary>
    /// 南方营地三问考验（呼叫南方后经电台逐题抛出，复用 <see cref="ChoicePanel"/>）。
    /// **叙事性拷问——任何选择都放行**（[SPEC-B11]），三次回答基调择启程旁白临别一句（<see cref="SouthTrial.Variant"/>）。
    /// 可从当前已答进度续答（中途离开再回电台续问）；答满三问 → 南方裁决 + 临时开路。
    /// </summary>
    private void StartSouthTrial()
    {
        var q = SouthTrial.CurrentQuestion(_storyFlags);
        if (q == null)
        {
            ShowSouthVerdict(); // 已答满（幂等兜底）：直接给裁决
            return;
        }
        AskSouthQuestion(q.Value);
    }

    /// <summary>抛出一道考题（三个基调各异的回答，选后记录并推进；未答满续下一题，答满走裁决）。</summary>
    private void AskSouthQuestion(SouthTrial.TrialQuestion question)
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var opts = new List<ChoicePanel.ChoiceOption>();
        foreach (var a in question.Answers)
            opts.Add(new ChoicePanel.ChoiceOption { Value = (int)a.Tone, Label = a.Label, Accent = new Color(0.4f, 0.42f, 0.46f) });

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(question.SouthLine + "\n\n" + question.Prompt, opts);
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            SouthTrial.RecordAnswer(_storyFlags, (SouthTrial.Tone)v);
            var next = SouthTrial.CurrentQuestion(_storyFlags);
            if (next != null)
                AskSouthQuestion(next.Value); // 下一题
            else
                ShowSouthVerdict(); // 三问答满 → 南方裁决 + 开路
        };
    }

    /// <summary>南方裁决（无对错放行）：告知路已开、回电台启程、尸潮不等人（须抢在第 40 天前）。</summary>
    private void ShowSouthVerdict()
        => ShowDiscoveryNarrative(SouthTrial.VerdictTitle, SouthTrial.VerdictNarrative);

    /// <summary>
    /// 南逃启程入口（考验通过后回营地电台交互时）：提供「启程南逃 / 再等等」。
    /// 选启程 → <see cref="ConfirmSouthDeparture"/> 二次确认。
    /// </summary>
    private void PromptSouthDeparture()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            SouthTrial.DeparturePrompt,
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = SouthTrial.DepartOptionLabel,
                        Description = "只带背得动的，走出营门就不再回头", Accent = new Color(0.35f, 0.55f, 0.38f) },
                new() { Value = 0, Label = SouthTrial.DepartDeferLabel,
                        Description = "先备好物资再来", Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            if (v == 1)
                ConfirmSouthDeparture();
        };
    }

    /// <summary>
    /// 南逃启程二次确认（不可逆）。尸潮已至（第 40 天到期或围攻已起）→ 错过窗口，路走不成（兜底叙事）。
    /// 确认 → 一次性置启程 flag + 停全灭路由 + 播 CG③（启程旁白含三问变体 + 南逃结尾段），终局由 EndingPanel 接管。
    /// </summary>
    private void ConfirmSouthDeparture()
    {
        // 尸潮 Arrived 后不可再逃（[SPEC-B11]：40 天时限内才可走）。
        if (_siegeActive || _clock.Day >= HordeTimeline.DeadlineDay)
        {
            ShowDiscoveryNarrative(SouthTrial.TooLateTitle, SouthTrial.TooLateNarrative);
            return;
        }

        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            SouthTrial.DepartConfirmPrompt,
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = "启程", Description = "这一步收不回来了",
                        Accent = new Color(0.35f, 0.55f, 0.38f) },
                new() { Value = 0, Label = "再想想", Description = "先回到上一步",
                        Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            if (v != 1)
            {
                PromptSouthDeparture(); // 再想想：回到启程入口
                return;
            }
            if (!SouthTrial.MarkDeparted(_storyFlags))
                return; // 已启程过（幂等去重）
            _gameOver = true; // 南逃成功＝终局，停掉其余全灭/围攻路由
            GD.Print($"[电台] 第 {_clock.Day} 天：南逃启程（结局③·唯一生路），播 CG③。");
            EndingPanel.Show(_hud, SouthTrial.EscapeCg(_storyFlags), EndingCg.SouthEscapeTitle);
        };
    }

    /// <summary>
    /// 弹出「答应出兵清剿 / 暂不」抉择面板（复用通用 <see cref="ChoicePanel"/>，非教学三选一）。
    /// 冻结时标、选完恢复：答应→请求线永久停播；暂不→计一次，满 3 次排期离开。收尾均走 <see cref="AdvanceAfterMeal"/>。
    /// </summary>
    private void PromptChristineHelpChoice()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        // draft 待用户改：三条请求台词见 meal_bubbles.json；此处为抉择面板提示与选项占位。
        panel.Setup(
            "克莉丝汀把你拉到一旁，声音压得很低：\n" +
            "「金手指帮……我夜里一闭眼就是他们的脸。带上人，跟我去把那个据点端了——求你了。」",
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = "答应出兵清剿",
                        Description = "等探明他们的据点，就动手", Accent = new Color(0.62f, 0.25f, 0.22f) },
                new() { Value = 0, Label = "暂不",
                        Description = "现在还不是时候", Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            bool agreed = v == 1;
            ChristineRequestLogic.Resolve(_storyFlags, agreed);
            GD.Print(agreed
                ? "[克莉丝汀] 答应出兵清剿金手指帮（请求线停播，待后续探索兑现）。"
                : $"[克莉丝汀] 暂不出兵（累计回绝 {ChristineRequestLogic.DeclineCount(_storyFlags)}/{ChristineRequestLogic.DeclinesToLeave}）。");
            panel.QueueFree();
            AdvanceAfterMeal(); // 抉择完成，接回被推迟的聚餐后相位推进
        };
    }

    /// <summary>
    /// 克莉丝汀累计 3 次回绝后自行离开：**不走 Died 事件**（避免触发全灭判定/记 _recentlyDeceased），
    /// 手动做与 <see cref="OnActorDied"/> 平行的名单清理（移出 _survivors/_selected/_raidGuards），再走向门外消失。
    /// </summary>
    private void ChristineLeaveVoluntary()
    {
        var christine = ChristinePawn();
        if (christine == null)
        {
            return; // 已不在场（如中途身故，Abort 已清 flag）——静默跳过
        }
        _survivors.Remove(christine);
        if (_selectedPawn == christine)
            SetSelection(null); // 选中者自行离场：置空选中 + 收面板
        else
            _selected.Remove(christine);
        _raidGuards.Remove(christine);
        _cardBar?.SetSurvivors(_survivors); // 自愿离场：卡牌栏去掉她的卡
        // draft 待用户改：三拒后独走复仇的离别台词（决绝，呼应反水时"我是好人！"）。
        FloatingText.Spawn(_actorLayer, christine.GlobalPosition,
            "杀死这些劫掠者……这一次，我一个人也去。", new Color(0.9f, 0.5f, 0.4f));
        WalkOutAndDespawn(christine);
        // 置"独自去复仇"flag：日后在金手指帮根据地发现其尸体时，环境叙事据此点名措辞（衔接 §7 时限失败态）。
        _storyFlags.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");
        GD.Print("[克莉丝汀] 三度回绝后，她收拾东西，独自走出了营地大门。");
    }

    /// <summary>
    /// 让某单位走向营地大门外并在淡出后从场上抹除（放逐/自愿离开共用；活着离开、无倒地尸体）。
    /// 朝"背离营地中心"方向走出（不依赖精确门坐标，任何地图都读作离开），同时令其 ActorSprite 真 alpha 淡出——
    /// 放逐时该单位仍 Alive，故 sprite 走 <see cref="ActorSprite.FadeOutAndFree"/> 独立分支自淡出/自毁（不被每帧重写 Modulate 打断）。
    /// 淡出结束后再 QueueFree actor 本体（sprite 已自毁；状态图标条随 actor 一并消失）。
    /// </summary>
    private void WalkOutAndDespawn(Actor who)
    {
        if (who == null || !IsInstanceValid(who))
        {
            return;
        }
        Vector2 outward = who.GlobalPosition - _cameraCenter;
        outward = outward.LengthSquared() > 1f ? outward.Normalized() : Vector2.Down;
        who.CommandMoveTo(who.GlobalPosition + outward * 420f);

        const float fadeSeconds = 2.0f;
        FindActorSprite(who)?.FadeOutAndFree(fadeSeconds);
        GetTree().CreateTimer(fadeSeconds + 0.2f).Timeout += () =>
        {
            if (IsInstanceValid(who))
            {
                who.QueueFree();
            }
        };
    }

    /// <summary>在 iso 层子节点里按绑定的 Actor 反查其 ActorSprite（放逐淡出用）；未挂载/未找到返回 null。</summary>
    private ActorSprite? FindActorSprite(Actor who)
    {
        foreach (Node child in _isoLayer.GetChildren())
        {
            if (child is ActorSprite s && s.BoundActor == who)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>处决表现：在单位脚点投一摊浓血（heavy）到 iso 层（与受击溅血同源），随后由调用方 QueueFree。</summary>
    private void SpawnDeathBlood(Actor who)
    {
        if (who == null || !IsInstanceValid(who))
        {
            return;
        }
        BloodDecal.Spawn(_isoLayer, Iso.Project(who.GlobalPosition), severity: 1f, heavy: true);
    }

    /// <summary>
    /// 收留：把克莉丝汀从 Raider-AI 实例转成玩家可控 Pawn 入营——在她原位移除 Raider、
    /// <see cref="Pawn.Create"/> 一个同名 Pawn，走 <see cref="AddActor"/>（Inject + Died 订阅 + 挂 _actorLayer）+ 加入 _survivors。
    /// 置 <c>christine_recruited</c> flag，供气泡框架接后续请求台词（本单不做那部分）。
    /// </summary>
    private void RecruitChristine()
    {
        Vector2 pos = _christine!.GlobalPosition;
        _christine.QueueFree();
        _christine = null;

        var pawn = Pawn.Create(ChristineName, usePistol: true, new Color(0.85f, 0.55f, 0.75f));
        pawn.Position = pos; // cartesian，原地入营
        AddActor(pawn);
        _survivors.Add(pawn);
        _cardBar?.SetSurvivors(_survivors); // 入营：卡牌栏新增她的卡

        _storyFlags.Set("christine_recruited", "true");
        ChristineRequestLogic.Begin(_storyFlags); // 开启"请求出兵清剿金手指帮"支线（聚餐里递进请求）
        GD.Print("[教学关] 收留克莉丝汀：转为可控幸存者入营。");
    }

    /// <summary>教学关未收口时的天亮兜底清场：移除双方残留、复位标志、置 done flag。</summary>
    private void CleanupChristineTutorial()
    {
        _tutorialActive = false;
        foreach (Raider r in _tutorialRaiders)
            if (IsInstanceValid(r))
                r.QueueFree();
        _tutorialRaiders.Clear();
        if (_christine != null && IsInstanceValid(_christine))
            _christine.QueueFree();
        _christine = null;
        _storyFlags.Set("tutorial_raider_done", "true");
    }

    // ---------------- 营地搜刮 / 共享库存 / 阅读（W3a） ----------------

    /// <summary>把一个带 role 的道具登记为可点击容器：loot 类进 <see cref="_containerLoot"/>（一次性搜刮），storage 类留藏物待开局入库。</summary>
    private void RegisterContainer(PropSpec pr, Rect2 rect)
    {
        if (string.IsNullOrEmpty(pr.role) || string.IsNullOrEmpty(pr.name))
        {
            return;
        }
        List<LootItem> loot = ParseLoot(pr.loot);
        _containers.Add(new ContainerRef { Name = pr.name!, Rect = rect, Role = pr.role!, Loot = loot });
        if (pr.role == "loot")
        {
            _containerLoot.Register(pr.name!, loot);
        }
    }

    /// <summary>camp.json 的 loot 规格 → 纯逻辑 <see cref="LootItem"/> 清单（未知 kind / 缺引用键的条目忽略）。</summary>
    private static List<LootItem> ParseLoot(LootSpec[]? specs)
    {
        var list = new List<LootItem>();
        foreach (LootSpec s in specs ?? System.Array.Empty<LootSpec>())
        {
            switch (s.kind)
            {
                case "food":
                    list.Add(LootItem.Food(s.qty > 0 ? s.qty : 1));
                    break;
                case "book" when !string.IsNullOrEmpty(s.id):
                    list.Add(LootItem.Book(s.id!));
                    break;
                case "weapon" when !string.IsNullOrEmpty(s.id):
                    list.Add(LootItem.Weapon(s.id!));
                    break;
                case "armor" when !string.IsNullOrEmpty(s.id):
                    list.Add(LootItem.Armor(s.id!));
                    break;
                case "material" when !string.IsNullOrEmpty(s.id):
                    // 材料（含医疗耗材/药品：绷带/针线/夹板/急救包/抗生素/成药，Materials.Key）→ 库存材料堆。
                    list.Add(LootItem.Material(s.id!, s.qty > 0 ? s.qty : 1));
                    break;
                case "tool" when !string.IsNullOrEmpty(s.id):
                    // 工具（calipers/sawblade/beaker）→ 落地时装进营地共享工作台对应槽。
                    list.Add(LootItem.Tool(s.id!));
                    break;
            }
        }
        return list;
    }

    /// <summary>建好库存/面板/时标钩子后，把 storage 容器（住宅柜子）的开局藏物一次性落地到共享库存/食物/工作台。</summary>
    private void ApplyStorageInitialStock()
    {
        foreach (ContainerRef c in _containers.Where(c => c.Role == "storage"))
        {
            var tools = new List<ToolSlot>();
            int food = LootApplication.Apply(c.Loot, _inventory, _bookRegistry, _bookResolver, tools);
            if (food > 0)
            {
                _resources.AddFood(food);
            }
            InstallFoundTools(tools);
        }
    }

    /// <summary>把搜到的工具装进营地共享工作台对应槽（幂等；解锁该类配方）。返回本次新装上的工具中文名。</summary>
    private List<string> InstallFoundTools(IEnumerable<ToolSlot> tools)
    {
        var installed = new List<string>();
        foreach (ToolSlot t in tools)
        {
            if (_workbench.InstallTool(t))
            {
                installed.Add(t.Label());
                GD.Print($"[工作台] 装上工具「{t.Label()}」，解锁对应类配方。");
            }
        }
        return installed;
    }

    /// <summary>
    /// 到达容器后执行交互（右键前往到位时调）：workbench→开制作面板；storage→开库存面板；
    /// loot→未搜过则搜出入库 + 一行反馈，随后开库存面板看结果（已搜过则仅提示）。各 OpenX 自带暂停冻结时标。
    /// </summary>
    private void ExecuteContainerInteract(Pawn arriver, ContainerRef hit)
    {
        if (hit.Role == "rubble")
        {
            BeginRubbleDig(arriver, hit);
            return;
        }

        if (hit.Role == "workbench")
        {
            OpenCrafting();
            return;
        }

        if (hit.Role == "radio")
        {
            OpenRadio();
            return;
        }

        if (hit.Role == "storage")
        {
            OpenStash(null);
            return;
        }

        if (hit.Role == "merchant")
        {
            OpenMerchantPanel();
            return;
        }

        // loot 容器：一次性搜刮。
        if (_containerLoot.IsSearched(hit.Name))
        {
            OpenStash($"{hit.Name}：已经搜过了。");
            return;
        }
        IReadOnlyList<LootItem> loot = _containerLoot.Search(hit.Name);
        var tools = new List<ToolSlot>();
        int food = LootApplication.Apply(loot, _inventory, _bookRegistry, _bookResolver, tools);
        if (food > 0)
        {
            _resources.AddFood(food);
        }
        List<string> installedTools = InstallFoundTools(tools);
        // 物品件数：入库存的非食物件（武器/护甲/书/材料）。工具进的是工作台不算库存件，另在提示里点名。
        int itemCount = loot.Count(l => l.Kind is not LootKind.Food and not LootKind.Tool);
        string toolNote = installedTools.Count > 0 ? $"，装上 {string.Join("、", installedTools)}" : "";
        string notice = loot.Count == 0
            ? $"{hit.Name}：空空如也。"
            : food > 0
                ? $"在{hit.Name}搜到 {itemCount} 件物品、{food} 份食物{toolNote}。"
                : $"在{hit.Name}搜到 {itemCount} 件物品{toolNote}。";
        GD.Print($"[搜刮] {notice}");
        OpenStash(notice);
    }

    // ---------------- 批次9 开局营地废墟挖掘 ----------------

    /// <summary>读 camp.json rubble[] 建开局废墟点：实心矮瓦砾块（碰撞 + 挖导航洞 + 切块视觉，句柄留存供挖净清场）
    /// + 登记 <see cref="RubbleField"/>（工时/产出/彩蛋位）+ 登记可点击容器（role=rubble，右键前往开挖）。drops 复用 ParseLoot→LootItem。</summary>
    private void BuildRubble()
    {
        foreach (RubbleSpec spec in _cfg.rubble ?? System.Array.Empty<RubbleSpec>())
        {
            if (string.IsNullOrEmpty(spec.name) || ToRect(spec.rect) is not { } rect)
            {
                continue;
            }
            var style = new PixelStyle { color = spec.color, tile = spec.tile, jitter = spec.jitter };

            // 实心瓦砾块（同 AddSolid 的碰撞 + 导航洞，但收集切块视觉节点句柄供挖净逐块清场）。
            var inst = new RubbleInstance { Rect = rect };
            var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
            body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
            body.CollisionMask = 0;
            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
            _logicLayer.AddChild(body);
            inst.Body = body;
            AddOccluderVisual(rect, style, seed: 29, (float)_heights.prop, cell: 60f, collect: inst.Visuals);
            _navHoles.Add(rect);
            _rubbleVisuals[spec.name!] = inst;

            // 纯逻辑：工时进度 + 一次性收获（drops 复用 ParseLoot→LootItem）。
            _rubble.Register(new RubbleSite(
                spec.name!,
                spec.workMinutes,
                ParseLoot(spec.drops),
                hasEggSlot: spec.eggSlot,
                eggContentId: spec.eggContentId ?? ""));

            // 可点击容器（右键前往开挖，走既有 _pendingInteract → ExecuteContainerInteract 路径）。
            _containers.Add(new ContainerRef { Name = spec.name!, Rect = rect, Role = "rubble", Loot = new List<LootItem>() });
        }
    }

    /// <summary>到达废墟后开挖：指派到位的前往者为挖掘者（占用其作业），随后由 <see cref="TickRubbleDig"/> 逐游戏分钟推进；
    /// 首次开挖弹一行提示教交互。不冻结时标——挖掘消耗真实游戏时间（白天营地活），被拉走则中断、进度留存（回来续挖）。</summary>
    private void BeginRubbleDig(Pawn digger, ContainerRef hit)
    {
        RubbleSite? site = _rubble.Find(hit.Name);
        if (site is null || site.Cleared)
        {
            return;
        }
        // 到位的前往者即挖掘者（UpdatePendingInteract 已确保是可控存活的选中角色）。
        _digging = (digger, hit.Name);
        _digLastMinuteKey = -1; // 下一挖掘帧从当前分钟起算

        // 首次开挖：一行提示教交互（hint-system 已在 HintTracker 加 rubble_dig 条目；已示过则 ShouldShow=false）。
        if (HintTracker.ShouldShow(HintTracker.RubbleDig, _storyFlags) && HintTracker.Text(HintTracker.RubbleDig) is { } tip)
        {
            _campToast.Show(tip, CampToast.Ok);
            HintTracker.MarkShown(HintTracker.RubbleDig, _storyFlags);
        }
        else
        {
            _campToast.Show($"{digger.DisplayName} 开始清挖{hit.Name}（工时 {site.RemainingWorkMinutes} 分）", CampToast.Ok);
        }
        GD.Print($"[废墟] {digger.DisplayName} 开挖 {hit.Name}，剩余工时 {site.RemainingWorkMinutes} 分。");
    }

    /// <summary>废墟挖掘逐分钟推进（每帧，_Process 调，同工时制分钟键增量）：挖掘者仍在该废墟点作业时按流逝游戏分钟推进；
    /// 挖满→收获落地 + 清场显露空地。挖掘者离场/死亡/改派=中断（进度留存，回来续挖）；面板冻结时标时分钟键不变→不推进。</summary>
    private void TickRubbleDig()
    {
        if (_digging is not { } d)
        {
            _digLastMinuteKey = -1;
            return;
        }
        RubbleSite? site = _rubble.Find(d.rubbleId);
        if (site is null || site.Cleared)
        {
            _digging = null;
            _digLastMinuteKey = -1;
            return;
        }

        // 挖掘者仍在场作业：存活可控 && 在名册 && 站在该废墟边缘附近（离开=中断，进度留存）。
        bool present =
            d.pawn.Alive && d.pawn.IsControllable && _survivors.Contains(d.pawn) &&
            _rubbleVisuals.TryGetValue(d.rubbleId, out RubbleInstance? inst) &&
            inst.Rect.Grow(d.pawn.Radius + PendingArriveMargin).HasPoint(d.pawn.GlobalPosition);

        int key = _clock.ClockMinuteKey();
        if (!present || _digLastMinuteKey < 0)
        {
            _digLastMinuteKey = present ? key : -1; // 离场清基线（不丢已推进的整分钟）
            return;
        }
        int delta = key - _digLastMinuteKey;
        if (delta < 0) delta += 24 * 60; // 跨午夜环绕
        _digLastMinuteKey = key;
        if (delta <= 0) return;

        site.Advance(delta, workerPresent: true);
        if (site.IsComplete)
        {
            CompleteRubbleDig(d.rubbleId);
        }
    }

    /// <summary>废墟挖满：收获产出落地（材料入共享库存/食物，复用 <see cref="LootApplication"/>）+ 清场显露空地 + 移除可点击容器 + 提示。
    /// 彩蛋位（<see cref="RubbleSite.HasEggSlot"/>）当前仅出普通产出 + 一行占位提示；authored 彩蛋内容待用户（见 RubbleSite.EggContentId TODO）。</summary>
    private void CompleteRubbleDig(string rubbleId)
    {
        RubbleSite? site = _rubble.Find(rubbleId);
        IReadOnlyList<LootItem> drops = _rubble.Harvest(rubbleId);
        var tools = new List<ToolSlot>();
        int food = LootApplication.Apply(drops, _inventory, _bookRegistry, _bookResolver, tools);
        if (food > 0)
        {
            _resources.AddFood(food);
        }
        InstallFoundTools(tools);

        // 清场：移碰撞体 + 视觉块 + 导航洞并重烘焙 → 显露空地（镜像 DestroyStructure）。
        ClearRubbleVisual(rubbleId);
        _containers.RemoveAll(c => c.Name == rubbleId);
        _digging = null;
        _digLastMinuteKey = -1;

        // 入库存的材料件数（drops 只含材料；食物/工具另计）。
        int itemCount = drops.Count(l => l.Kind is not LootKind.Food and not LootKind.Tool);
        // 彩蛋位：有 authored 叙事键（draft·待用户细化）→ 材料提示后再弹一段发现叙事（复用发现面板 ShowDiscoveryNarrative）；
        // 无键但 HasEggSlot（内容待填）→ 退回一行占位提示。普通废墟 eggNote 为空。
        (string title, string narrative)? egg =
            site is { HasEggSlot: true, EggContentId: { Length: > 0 } eid } ? EggContent(eid) : null;
        string eggNote = egg is null && site is { HasEggSlot: true } ? "，瓦砾深处似乎还压着什么……" : "";
        _campToast.Show($"{rubbleId}已清挖干净，翻出 {itemCount} 件材料，腾出一片空地{eggNote}", CampToast.Ok);
        GD.Print($"[废墟] {rubbleId} 挖净，产出 {itemCount} 件材料{(food > 0 ? $"+{food}份食物" : "")}{eggNote}");
        if (egg is { } e)
        {
            ShowDiscoveryNarrative(e.title, e.narrative);
        }
    }

    /// <summary>
    /// 废墟彩蛋 authored 叙事内容（draft·待用户细化）：按 <see cref="RubbleSite.EggContentId"/> 返回 (标题, 正文)；
    /// 未知键返回 null（退回占位提示）。文本对齐既有克制/压抑基调，不发明物件的煽情反应。
    /// 后续可扩展为多彩蛋位/加发实物纪念品；当前只做叙事揭示（"彩蛋物"= 铁盒里的全家福/蜡笔画，读后留在原处，不入库）。
    /// </summary>
    private (string title, string narrative)? EggContent(string eggContentId) => eggContentId switch
    {
        "egg_courtyard_family" => (
            "断墙里的铁盒",
            "断墙的夹层里嵌着一只生锈的铁皮饼干盒，边角被砸瘪了，盖子却还扣得严实。\n\n" +
            "里面没有值钱的东西。一叠用橡皮筋捆着的蜡笔画——歪歪扭扭的四口人，牵着手站在一栋涂成红色的房子前，" +
            "太阳画在角落，咧着嘴笑。最上面那张的背面，是大人的字迹：“给爸爸，等你回家。”\n\n" +
            "盒子底下压着一张全家福，边缘被摩挲得发白。照片里的房子，就是你们此刻脚下这片废墟。\n\n" +
            "你把盒盖轻轻扣回去，放回原处。有些东西，翻出来看过一眼就够了。"),
        _ => null,
    };

    /// <summary>废墟清场：QueueFree 碰撞体 + 切块视觉，移导航洞并重烘焙（显露空地可寻路）。幂等（已清空则空操作）。</summary>
    private void ClearRubbleVisual(string rubbleId)
    {
        if (!_rubbleVisuals.TryGetValue(rubbleId, out RubbleInstance? inst))
        {
            return;
        }
        _rubbleVisuals.Remove(rubbleId);
        if (inst.Body != null && IsInstanceValid(inst.Body))
        {
            inst.Body.QueueFree();
        }
        foreach (Node2D v in inst.Visuals)
        {
            if (IsInstanceValid(v))
            {
                v.QueueFree();
            }
        }
        inst.Visuals.Clear();
        _navHoles.Remove(inst.Rect); // Rect2 值相等；移除后重烘焙补出空地
        RebakeNavigation();
    }

    /// <summary>废墟悬停文案：显示挖掘进度；正在挖标"清挖中"，否则提示"选中角色后右键前往开挖"。</summary>
    private string RubbleHoverText(ContainerRef c, string noSel)
    {
        RubbleSite? site = _rubble.Find(c.Name);
        if (site is null)
        {
            return $"{c.Name}{noSel}";
        }
        bool digging = _digging is { } d && d.rubbleId == c.Name;
        string prog = site.ElapsedWorkMinutes > 0 ? $"（已挖 {(int)(site.Progress * 100)}%）" : "";
        return digging
            ? $"{c.Name} · 清挖中{prog}"
            : $"{c.Name} · 选中角色后右键前往开挖{prog}{noSel}";
    }

    // ---------------- 神秘商人到访 / 交易 ----------------

    /// <summary>
    /// 每晚营地视角起点（NightAct）调：到访调度到点且当晚平安（非袭营/教学夜）→ 派商人夜访进场；否则顺延。
    /// 挂夜晚是因为游戏白天=探险队视角（营地无操作）、夜晚=营地视角（玩家可右键调度角色）——商人须与可操作窗口重合。
    /// 已在场则不重复派。
    /// </summary>
    private void TryMerchantVisit()
    {
        if (_merchant != null) // 已在场（异常：上次未离场），不重复派
        {
            return;
        }
        // 接替链断商门控（用户拍板 [SPEC-B7]#5）：两位商人皆死于营地后永久断商，调度不再排访。
        if (!MerchantLineage.MerchantsAvailable(_storyFlags))
        {
            return;
        }
        if (_merchantSchedule.ShouldVisit(_clock.Day, IsMerchantBlockedToday()))
        {
            SpawnMerchant();
        }
    }

    /// <summary>
    /// 当晚是否不宜来访（袭营/异常夜 → 顺延到次晚再试）：夜访与袭营现同为夜晚窗口，故当晚有袭营则商人不来
    /// （避免"一边打劫一边摆摊"）——已有袭营在进行、或脚本化克莉丝汀反水袭击当晚（第 2 夜）算异常夜。
    /// 当前无"每晚概率袭营"表，故只挡这两类；日后接随机袭营表时在此并入。
    /// </summary>
    private bool IsMerchantBlockedToday()
        => _raidActive || (_clock.Day == 2 && !_storyFlags.Has("tutorial_raider_started"));

    /// <summary>
    /// 派神秘商人夜访进场：南门外边缘生成 → 夜里走向营地中心附近约定停留点（营地照明范围内）→ 登记停留点为
    /// merchant 容器（右键前往即开交易面板）。交互窗口 = 整个夜晚（NightAct 期间）。
    /// 照 <see cref="BeginChristineTutorial"/> 的边缘生成 + Inject + _actorLayer 范式；中立不参战。
    /// </summary>
    private void SpawnMerchant()
    {
        var merchant = Merchant.Create();
        merchant.Inject(_combat, _clock);
        merchant.Died += OnMerchantKilledAtCamp; // 死于营地 → 推进接替链（零掉落）+ 清引用
        Vector2 entry = new(1120f, 1540f);              // 南门外边缘（同克莉丝汀入场量级）
        Vector2 standPoint = _cameraCenter + new Vector2(160f, 120f); // 营地中心附近约定停留点（营地照明内，draft，避开正中拥挤区）
        merchant.Position = entry;
        _actorLayer.AddChild(merchant);
        merchant.CommandMoveTo(standPoint);
        _merchant = merchant;

        Rect2 rect = new(standPoint - new Vector2(20f, 20f), new Vector2(40f, 40f));
        _merchantContainer = new ContainerRef { Name = "神秘商人", Rect = rect, Role = "merchant" };
        _containers.Add(_merchantContainer);

        _campToast.Show("夜色里，神秘商人来到营地——右键让角色前往交易。", CampToast.Ok);
        // 第二（接替）商人首访：播 authored 开场白（协会想放弃此点/他信这里的人善良/力排众议来跑商），只播一次。
        if (MerchantLineage.ShouldPlaySecondIntro(_storyFlags))
        {
            FloatingText.Spawn(_actorLayer, merchant.GlobalPosition, MerchantLineage.SecondMerchantIntroLine,
                new Color(0.6f, 0.78f, 0.9f));
            MerchantLineage.MarkSecondIntroPlayed(_storyFlags);
        }
        GD.Print("[神秘商人] 夜访营地。");
    }

    /// <summary>
    /// 神秘商人**死于营地**（用户拍板 [SPEC-B7]#5）：推进接替链——第一商人死 → 第二商人将接替、第二商人死 → 今后永无商人。
    /// **零掉落**：商人 Pawn 不携货架库存、本作角色死亡也无自动尸体掉落，故此处不产任何搜刮物（杜绝杀商套利）。
    /// 死亡地点判定：商人为 Neutral、只在营地生成出现，故"死亡"即等价"死于营地"（若日后商人会出现在别处需在此加地点门控）。
    /// 死后滚下一次来访日：断商前尚存的下一位商人在未来某晚按调度到访。
    /// </summary>
    private void OnMerchantKilledAtCamp(Actor _)
    {
        MerchantLineageStage after = MerchantLineage.OnMerchantDiedAtCamp(_storyFlags);
        OnMerchantGone();
        _merchantSchedule.CompleteVisit(_clock.Day); // 死亡收束本次到访，排下一次（接替商人未来某晚到访）
        _campToast.Show(after == MerchantLineageStage.Extinct
            ? "商人倒在了营地里。协会不会再派人来了。"
            : "商人倒在了营地里——他什么也没能留下。", CampToast.Bad);
        GD.Print($"[神秘商人] 死于营地，接替链 → {after}。");
    }

    /// <summary>
    /// 送走商人（天亮 / 袭营）：从可交互登记移除、关掉可能开着的交易面板、走出画面淡出消失，并滚下一次来访日。
    /// 天亮离场因白天玩家转探险队视角、营地无操作，商人不逗留。
    /// 保守边界（待确认）：袭营时若在场亦立即离开（不设无敌/不掉落）。无在场商人则无操作。
    /// </summary>
    private void DismissMerchant()
    {
        if (_merchant == null)
        {
            return;
        }
        if (_merchantOpen)
        {
            CloseMerchant();
        }
        Merchant leaving = _merchant;
        OnMerchantGone();
        WalkOutAndDespawn(leaving);
        _merchantSchedule.CompleteVisit(_clock.Day); // 本次到访收束，排下一次（1~5 天后）
        _campToast.Show("神秘商人收摊离开了。", CampToast.Bad);
        GD.Print("[神秘商人] 离开营地。");
    }

    /// <summary>清商人在场引用 + 从可交互容器移除 + 作废正走向它的前往令（离场/意外消失共用）。</summary>
    private void OnMerchantGone()
    {
        if (_merchantContainer != null)
        {
            _containers.Remove(_merchantContainer);
            _merchantContainer = null;
        }
        if (_pendingInteract is { } pend && pend.target.Role == "merchant")
        {
            _pendingInteract = null;
        }
        _merchant = null;
    }

    /// <summary>打开交易面板（右键前往到位时调）：冻结时标 + 展示货架与当前持币量。</summary>
    private void OpenMerchantPanel()
    {
        if (!_merchantOpen)
        {
            CapturePanelTimeState(out _prevMerchantSpeed, out _prevMerchantPaused);
            _merchantOpen = true;
        }
        RefreshMerchantPanel();
        _merchantPanel.Visible = true;
    }

    /// <summary>用当前货架 + 可收购库存行 + 持币量刷新交易面板（买入/卖出结算后即时反映扣币/库存/灰显，保持当前页签）。</summary>
    private void RefreshMerchantPanel()
        => _merchantPanel.Show(_merchantShelf, MerchantBuyList.SellableRows(_inventory), _inventory.MaterialCount(Materials.CurrencyKey));

    /// <summary>买入某货架条目：<see cref="MerchantTrade.Buy"/> 实扣白银实产商品 → 结果 toast → 刷新面板。</summary>
    private void OnMerchantBuyRequested(int offerIndex)
    {
        if (offerIndex < 0 || offerIndex >= _merchantShelf.Offers.Count)
        {
            return;
        }
        MerchantOffer offer = _merchantShelf.Offers[offerIndex];
        switch (MerchantTrade.Buy(_inventory, offer))
        {
            case PurchaseStatus.Ok:
                _campToast.Show($"买下了「{offer.Good.DisplayName}」。", CampToast.Ok);
                break;
            case PurchaseStatus.NotEnoughMoney:
                _campToast.Show("白银不够。", CampToast.Bad);
                break;
            case PurchaseStatus.SoldOut:
                _campToast.Show("这件已经卖光了。", CampToast.Bad);
                break;
        }
        RefreshMerchantPanel();
    }

    /// <summary>
    /// 卖出某收购行的一单位（用户拍板：白名单收购、基准价 60%）：<see cref="MerchantTrade.SellOne"/> 实扣一单位物品、白银入账 → 结果 toast → 刷新面板。
    /// 收购白名单/单位收购价由 <see cref="MerchantBuyList"/> 定；商人不在场（断商）自然无此入口。
    /// </summary>
    private void OnMerchantSellRequested(SellRow row)
    {
        switch (MerchantTrade.SellOne(_inventory, row.UnitItem))
        {
            case SellStatus.Ok:
                _campToast.Show($"卖出「{row.DisplayName}」，进账 {row.UnitSellPrice} 白银。", CampToast.Ok);
                break;
            case SellStatus.NoneOwned:
                _campToast.Show($"没有多余的「{row.DisplayName}」可卖了。", CampToast.Bad);
                break;
            case SellStatus.NotBuying:
                _campToast.Show("商人不收这个。", CampToast.Bad);
                break;
        }
        RefreshMerchantPanel();
    }

    /// <summary>关交易面板：还原时标（不送走商人，可再次前往）。</summary>
    private void CloseMerchant()
    {
        if (!_merchantOpen)
        {
            return;
        }
        _merchantPanel.Visible = false;
        _merchantOpen = false;
        RestorePanelTimeState(_prevMerchantSpeed, _prevMerchantPaused);
    }

    /// <summary>
    /// 弹面板前捕获 GameClock 的速度档 + 暂停态并冻结时标（直接置 Engine.TimeScale=0）。
    /// 捕获须在冻结**之前**，才能记住"弹前世界是否已被玩家手动暂停"。
    /// </summary>
    private void CapturePanelTimeState(out int savedSpeed, out bool savedPaused)
    {
        savedSpeed = _clock.SpeedIndex;
        savedPaused = _clock.Paused; // Engine.TimeScale==0 ⇒ 手动暂停 or 相位强制暂停
        Engine.TimeScale = 0;
    }

    /// <summary>
    /// 关面板后按捕获的速度档 + 暂停态**保真还原**：SetSpeedIndex 重放相位时标（强制暂停相位会自持 0），
    /// 弹前若是手动暂停且当前相位未强制暂停，则 TogglePause 还原成暂停——而非旧代码裸恢复成 1x。
    /// </summary>
    private void RestorePanelTimeState(int savedSpeed, bool savedPaused)
    {
        _clock.SetSpeedIndex(savedSpeed);
        if (savedPaused && !_clock.Paused)
            _clock.TogglePause();
    }

    /// <summary>打开（或刷新）库存面板：首次打开冻结时标；<paramref name="notice"/> 为可空的一行搜刮反馈。</summary>
    private void OpenStash(string? notice)
    {
        if (!_stashOpen)
        {
            CapturePanelTimeState(out _prevStashSpeed, out _prevStashPaused);
            _stashOpen = true;
        }
        _stashPanel.ShowStash(_inventory, _resources.Food, notice, IsBookRead);
        _stashPanel.Visible = true;
    }

    /// <summary>
    /// 库存面板「装备」→ 装到当前选中的幸存者（单选取一，无选中则提示）。武器默认装右手（EquipToHand
    /// 会把双手武器自动分流占两手）；护甲走 EquipApparel。成功则从库存移除该件、刷新面板（含开着的角色面板）。
    /// </summary>
    private void OnStashEquipRequested(string refKey)
    {
        Pawn? target = _selected.FirstOrDefault(p => p.IsControllable);
        if (target == null)
        {
            OpenStash("请先选中一个幸存者，再点「装备」。");
            return;
        }

        Item? item = _inventory.Equippable.FirstOrDefault(i => i.RefKey == refKey);
        if (item == null)
        {
            return; // 库存里已无此件（并发/重复点），静默
        }

        // 顶替回库：装备前记下目标已占的武器/护甲名，装成功后把被顶替下来的旧件回库存（绝不静默丢）。
        bool isWeapon = item.Category == ItemCategory.Weapon;
        List<string> before = isWeapon ? HeldWeaponNames(target) : target.EquippedApparel.ToList();

        bool ok = isWeapon
            ? target.EquipWeapon(refKey, Hand.Right)  // 双手武器 EquipToHand 自动占两手
            : target.EquipApparel(refKey);
        if (!ok)
        {
            OpenStash($"{item.DisplayName} 无法装备（断肢禁槽/持握冲突/不适用）。");
            return;
        }

        _inventory.Remove(item);
        // 被顶替下来的旧件回库存（本次刚装上的 refKey 从 after 抵掉一份，不算顶替；同名替换也不丢件）。
        List<string> after = isWeapon ? HeldWeaponNames(target) : target.EquippedApparel.ToList();
        foreach (string displaced in DisplacedNames(before, after, refKey))
        {
            _inventory.Add(isWeapon ? Item.Weapon(displaced) : Item.Armor(displaced));
        }

        if (_characterPanel.IsShown)
        {
            ShowInspect(target); // 面板开着 → 刷新装备态
        }
        OpenStash($"已为 {target.DisplayName} 装备 {item.DisplayName}。");
    }

    /// <summary>库存「持起」手持光源（手电/火把）→ 选中幸存者 <see cref="Pawn.HeldLight"/>，占一只手；被顶替的旧光源回库存（绝不静默丢）。</summary>
    private void OnStashLightHoldRequested(string lightKey)
    {
        Pawn? target = _selected.FirstOrDefault(p => p.IsControllable);
        if (target == null)
        {
            OpenStash("请先选中一个幸存者，再点「持起」。");
            return;
        }

        Item? item = _inventory.ByCategory(ItemCategory.Light).FirstOrDefault(i => i.RefKey == lightKey);
        if (item == null)
        {
            return; // 库存里已无此件（并发/重复点），静默
        }

        string? previous = target.HeldLight.Held?.Key; // 顶替回库用
        if (!target.EquipLight(lightKey))
        {
            OpenStash($"{item.DisplayName} 无法持起（断手/双手武器占两手/该手已持械）。");
            return;
        }

        _inventory.Remove(item);
        // 被顶替下来的旧光源回库存（同源 refKey 不算顶替）。
        if (previous != null && previous != lightKey && LightSource.Find(previous) is LightProfile prev)
        {
            _inventory.Add(Item.Light(previous, prev.DisplayName));
        }

        if (_characterPanel.IsShown)
        {
            ShowInspect(target); // 面板开着 → 刷新持械/持光态
        }
        OpenStash($"{target.DisplayName} 持起了 {item.DisplayName}。");
    }

    /// <summary>卸某手武器 → 回库存 + 刷面板/库存列表（供角色面板「卸下」按钮回调）。</summary>
    private void UnequipWeaponToStash(Pawn p, Hand hand)
    {
        List<string> before = HeldWeaponNames(p);
        p.UnequipWeapon(hand);
        List<string> after = HeldWeaponNames(p);
        foreach (string name in DisplacedNames(before, after, null))
        {
            _inventory.Add(Item.Weapon(name));
        }
        RefreshAfterEquipmentChange(p);
    }

    /// <summary>卸某件穿戴品（按名）→ 回库存 + 刷面板/库存列表（供角色面板「卸下」按钮回调）。</summary>
    private void UnequipApparelToStash(Pawn p, string apparelName)
    {
        List<string> before = p.EquippedApparel.ToList();
        p.UnequipApparel(apparelName);
        List<string> after = p.EquippedApparel.ToList();
        foreach (string name in DisplacedNames(before, after, null))
        {
            _inventory.Add(Item.Armor(name));
        }
        RefreshAfterEquipmentChange(p);
    }

    /// <summary>装备变更后刷新：角色面板开着则重拍装备态，库存面板开着则重列（反映回库的旧件）。</summary>
    private void RefreshAfterEquipmentChange(Pawn p)
    {
        if (_characterPanel.IsShown)
        {
            ShowInspect(p);
        }
        if (_stashOpen)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);
        }
    }

    /// <summary>
    /// 目标当前实际持有的武器名（物理件计数）：双手握一把 → 只记一件（两手同一把，靠 <see cref="GripMode.TwoHanded"/>
    /// 判定，不能用引用比较——武器目录是共享实例，双持两把同名武器亦引用相等）。
    /// </summary>
    private static List<string> HeldWeaponNames(Pawn p)
    {
        var list = new List<string>();
        var left = p.WeaponInHand(Hand.Left);
        var right = p.WeaponInHand(Hand.Right);
        if (p.Grip == GripMode.TwoHanded)
        {
            var w = right ?? left;
            if (w is not null) list.Add(w.Name);
        }
        else
        {
            if (left is not null) list.Add(left.Name);
            if (right is not null) list.Add(right.Name);
        }
        return list;
    }

    /// <summary>
    /// 顶替/卸下后应回库存的旧件名（多重集差 before − after）：<paramref name="newlyEquipped"/> 为本次刚装上的件，
    /// 从 after 里先抵掉一份（它已由库存扣减记账，不算顶替），从而同名替换也能正确回收旧件；卸下场景传 null。
    /// </summary>
    private static IEnumerable<string> DisplacedNames(List<string> before, List<string> after, string? newlyEquipped)
    {
        var afterCounts = after.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        if (newlyEquipped is not null && afterCounts.TryGetValue(newlyEquipped, out int nc) && nc > 0)
        {
            afterCounts[newlyEquipped] = nc - 1;
        }
        foreach (string name in before)
        {
            if (afterCounts.TryGetValue(name, out int c) && c > 0)
            {
                afterCounts[name] = c - 1; // 仍在装备上的同名件，抵消
            }
            else
            {
                yield return name; // 顶替/卸下下来的
            }
        }
    }

    /// <summary>打开/刷新角色检视面板：喂只读健康快照 + 装备态快照 + 装假肢/卸下入口（皆死数据/闭包，面板不持 live Pawn）。</summary>
    private void ShowInspect(Pawn p)
        => _characterPanel.ShowFor(
            p.Inspect(),
            SnapshotEquipment(p),
            (region, grade) => p.EquipProsthetic(region, grade),
            hand => UnequipWeaponToStash(p, hand),
            name => UnequipApparelToStash(p, name));

    /// <summary>由 live Pawn 的只读装备 API 拍一份纯数据装备快照（11 穿戴槽 + 左右手持械 + 握持态）。</summary>
    private static EquipmentSnapshot SnapshotEquipment(Pawn p)
    {
        IReadOnlySet<EquipSlot> disabled = p.DisabledApparelSlots;
        var slots = Enum.GetValues<EquipSlot>()
            .Select(s => new ApparelSlotStatus { Slot = s, ItemName = p.ApparelAt(s), IsDisabled = disabled.Contains(s) })
            .ToList();
        return new EquipmentSnapshot
        {
            Slots = slots,
            LeftHandWeapon = WeaponInfo.From(p.WeaponInHand(Hand.Left)),
            RightHandWeapon = WeaponInfo.From(p.WeaponInHand(Hand.Right)),
            Grip = p.Grip,
        };
    }

    /// <summary>关库存面板：连带关阅读面板、恢复时标（暂停中打开的则回 1，避免冻死）。</summary>
    private void CloseStash()
    {
        _stashPanel.Visible = false;
        _readerPanel.Visible = false;
        _stashOpen = false;
        RestorePanelTimeState(_prevStashSpeed, _prevStashPaused);
    }

    /// <summary>
    /// 库存里点某本书的「阅读」：把这次阅读**归属到某个读者幸存者**（标其个人已读集，配方书门槛按此判定），
    /// 同时置全局已读（库存"已读"标记等营地视角仍读全局），弹阅读面板（叠在库存之上）。
    /// 读者 = 当前选中的可控幸存者（同"装备"入口的选人口径）；无选中时退化到首个可控幸存者，保证阅读始终可用。
    /// </summary>
    private void OnBookOpenRequested(string bookId)
    {
        if (!_bookRegistry.TryGetValue(bookId, out BookData? bd))
        {
            return;
        }
        // 归属读者：优先当前选中的可控幸存者，否则首个可控幸存者（MVP：库存无"读者槽"，复用选中口径）。
        Pawn? reader = _selected.FirstOrDefault(p => p.IsControllable)
            ?? _survivors.FirstOrDefault(p => p.Alive && p.IsControllable);
        reader?.MarkBookRead(bookId);
        bd.MarkRead(); // 全局已读保留（库存已读标记 / 其它营地视角消费点仍读它）
        if (bd.GrantsRecipeStub != null)
        {
            GD.Print($"[阅读] 读完《{bd.Title}》，获得配方（桩，配方系统后续接）：{bd.GrantsRecipeStub}");
        }
        _readerPanel.ShowBook(bd.Title, bd.Body);
        _readerPanel.Visible = true;
    }

    /// <summary>关阅读面板回到库存：刷新库存（反映刚置的「已读」标记），时标仍由库存面板持有。</summary>
    private void OnReaderClosed()
    {
        _readerPanel.Visible = false;
        if (_stashOpen)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);
        }
    }

    private bool IsBookRead(string bookId) =>
        _bookRegistry.TryGetValue(bookId, out BookData? b) && b.IsRead;

    // ---------------- 配方 / 制作（工作台接入） ----------------

    /// <summary>打开（或刷新）工作台制作面板：首次打开冻结时标。制作者=当前可控幸存者；书门槛按制作者本人已读（<see cref="Pawn.HasReadBook"/>）。</summary>
    private void OpenCrafting()
    {
        if (!_craftingOpen)
        {
            CapturePanelTimeState(out _prevCraftingSpeed, out _prevCraftingPaused);
            _craftingOpen = true;
        }
        RefreshCrafting();
        _craftingPanel.Visible = true;
    }

    /// <summary>重刷制作面板数据（下单/推进/完工/改装后调，反映扣掉的材料、新入库产物、在制任务进度）。</summary>
    private void RefreshCrafting()
        => _craftingPanel.ShowFor(_workbench, ControllableCrafters(), _inventory,
            (pawn, id) => pawn.HasReadBook(id), // 书门槛按制作者本人已读（非营地全局）
            _craftingJob,                        // 工时制：本工作台在制任务（顶部进度横幅/占用中置灰）
            DogGearGate);                        // 制作者门槛：狗装备需道格 + 羁绊≥2 级（灰显+双保险）

    /// <summary>当前可作制作者的幸存者（存活且空闲可控）。</summary>
    private List<Pawn> ControllableCrafters()
        => _survivors.Where(p => p.Alive && p.IsControllable).ToList();

    /// <summary>
    /// 狗装备制作者门槛判据（批次5，消费 <see cref="DougBruceBond.CanCraftDogGear"/>）：
    /// 门槛键＝<see cref="RecipeBook.DogGearCrafterGate"/> 时，要求**制作者是道格**且**与布鲁斯羁绊≥2 级**（且两者皆在世）；
    /// 满足返回 null、否则返回灰显文案。喂给 <see cref="CraftingLogic.CanCraft"/>/<see cref="CraftingService.StartJob"/> 的 crafterGate。
    /// 未识别的门槛键 fail-closed（返回文案）。羁绊等级经 <see cref="BondLevel"/>（共同存活天数现算）。
    /// </summary>
    private string? DogGearGate(Pawn crafter, string gateKey)
    {
        if (gateKey != RecipeBook.DogGearCrafterGate)
            return "未知制作门槛";
        if (crafter != _doug)
            return "狗装备只有道格能为布鲁斯制作";
        bool bothAlive = _doug is { Alive: true } && _bruce is { Alive: true };
        if (DougBruceBond.CanCraftDogGear(BondLevel, bothAlive))
            return null; // 满足
        return bothAlive
            ? $"需与布鲁斯羁绊达 {DougBruceBond.DogGearUnlockLevel} 级（继续共同存活）"
            : "需道格与布鲁斯皆在世";
    }

    /// <summary>关制作面板：恢复时标、清掉瞬时提示（与库存面板互斥地持有时标）。</summary>
    private void CloseCrafting()
    {
        _craftingPanel.Visible = false;
        _craftingOpen = false;
        _campToast.Hide();
        RestorePanelTimeState(_prevCraftingSpeed, _prevCraftingPaused);
    }

    /// <summary>
    /// 面板「下单」→ 查配方 → 走 <see cref="CraftingService.StartJob"/> 开工（工时制：**开工即扣料锁定**，
    /// 返回可推进的 <see cref="CraftingJob"/>，存为本工作台在制任务，产出留待夜间推进满工时后完工）。
    /// 每台单任务：已有在制任务时拒绝新单；成功：提示已下单+工时，刷新面板；门槛失败：提示中文缺项。
    /// </summary>
    private void OnCraftRequested(string recipeId, Pawn crafter)
    {
        RecipeData? recipe = RecipeBook.Find(recipeId);
        if (recipe is null)
        {
            _campToast.Show($"未知配方：{recipeId}", CampToast.Bad);
            return;
        }

        // 单任务队列：工作台被在制任务占用时不接新单（面板已置灰，此为双保险）。
        if (_craftingJob is not null)
        {
            _campToast.Show("工作台占用中：完工取出后再下新单。", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        CraftStartResult result = CraftingService.StartJob(
            recipe, id => crafter.HasReadBook(id), // 书门槛按制作者本人已读
            _workbench, _inventory, 1,
            crafterGate: gateKey => DogGearGate(crafter, gateKey)); // 狗装备制作者门槛（道格+羁绊≥2级）

        if (!result.Success)
        {
            string reason = string.Join("；", result.Blocks.Select(b => b.Detail));
            _campToast.Show($"做不了「{recipe.DisplayName}」：{reason}", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        _craftingJob = result.Job;
        _craftingJobWorker = crafter; // 记下单者作生产者身份（3 级光环生产系数按其判定）
        _craftLastMinuteKey = -1; // 重置增量基线，下一生产帧从当前分钟起算
        _craftMinuteBudget = 0f;  // 清小数预算，新任务从零累积
        string work = CraftingPanelFormat.FormatWorkDuration(recipe.WorkMinutes);
        _campToast.Show($"已下单：{recipe.DisplayName}（工时 {work}，夜间生产）", CampToast.Ok);
        GD.Print($"[制作] {crafter.DisplayName} 下单 {recipe.DisplayName}（工时 {recipe.WorkMinutes} 分）");
        RefreshCrafting();
    }

    /// <summary>
    /// 工时制夜间生产推进（每帧，_Process 调）：仅当有在制任务且处夜间生产相位（NightAct）时，
    /// 按游戏分钟增量推进工时；满工时即完工产出。面板冻结时标时分钟键不变→零增量→不推进。
    /// ★interim：workerPresent 暂等同于"处生产相位"——真正的"指派夜班生产者在工作台"判定归 night-response/shift-sleep（见 [HANDOFF]）。
    /// </summary>
    private void TickCraftingWorktime()
    {
        if (_craftingJob is null)
        {
            _craftLastMinuteKey = -1;
            _craftMinuteBudget = 0f;
            return;
        }

        // 夜间生产相位（shift-sleep 落定"夜班生产相位"API 后可替换此判据）。
        bool productionPhase = _clock.CurrentPhase == DayPhase.NightAct;
        int key = _clock.ClockMinuteKey();
        if (!productionPhase || _craftLastMinuteKey < 0)
        {
            _craftLastMinuteKey = productionPhase ? key : -1;
            if (!productionPhase) _craftMinuteBudget = 0f; // 中断相位清小数残值（不丢已推进的整分钟）
            return;
        }

        int delta = key - _craftLastMinuteKey;
        if (delta < 0) delta += 24 * 60; // 跨午夜环绕
        _craftLastMinuteKey = key;
        if (delta <= 0) return;

        // 3 级光环生产 +10%（synergy-wiring）+ 次相位疲劳折减（批次6）：把流逝分钟乘生产系数累积到小数预算，取整分钟喂 Advance，
        // 余数留存——避免 delta=1/min 时 (int)(1×1.10)=1 吞掉每分钟 0.10。光环系数=下单者是道格且光环激活→1.10，疲劳系数=被唤醒者次相位<1。
        float mult = _craftingJobWorker is { } worker ? BondProductionMultFor(worker) * FatigueMultiplierFor(worker) : 1f;
        _craftMinuteBudget += delta * mult;
        int wholeMinutes = (int)_craftMinuteBudget;
        if (wholeMinutes <= 0) return;
        _craftMinuteBudget -= wholeMinutes;

        // canWork（批次6，接 craft-worktime HANDOFF）：指派夜班生产者(NightCrew)在其生产相位(IsWorkPhaseFor)且未被袭营拉去战斗（在台语义：
        // 无战斗目标即视作在工作台）。被拉走(HasActiveTarget)→canWork=false，工时暂停不丢进度，袭营结束回台自动续作。
        bool workerPresent = _craftingJobWorker is { Alive: true } w2
            && ShiftSchedule.IsWorkPhaseFor(ShiftSchedule.ShiftFor(w2.Id, _todaysExpeditionIds), _clock.CurrentPhase)
            && !w2.HasActiveTarget;
        int applied = _craftingJob.Advance(wholeMinutes, canWork: workerPresent);
        if (applied <= 0) return;

        if (_craftingJob.IsComplete)
        {
            CompleteActiveCraftingJob();
        }
        else if (_craftingOpen)
        {
            RefreshCrafting(); // 面板开着时随进度更新横幅（1/分钟粒度）
        }
    }

    /// <summary>在制任务完工：产物按 <see cref="CraftOutputFactory"/> 分类入库、提示、清空任务、刷新面板。</summary>
    private void CompleteActiveCraftingJob()
    {
        CraftingJob job = _craftingJob!;
        _craftingJob = null;
        _craftingJobWorker = null;
        _craftLastMinuteKey = -1;
        _craftMinuteBudget = 0f;

        RecipeData? recipe = RecipeBook.Find(job.RecipeId);
        if (recipe is null)
        {
            GD.Print($"[制作] 完工但配方丢失：{job.RecipeId}");
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        IReadOnlyList<Item> produced = CraftingService.CompleteJob(job, recipe, _inventory, CraftOutputFactory.Create);
        string products = string.Join("、", produced.Select(p => p.DisplayName));
        _campToast.Show($"制作完成：{products}", CampToast.Ok);
        GD.Print($"[制作] 完工 {recipe.DisplayName} → {products}");
        if (_craftingOpen) RefreshCrafting();
    }

    /// <summary>面板「改装」→ 走 <see cref="CraftingService.ApplyWeaponMod"/> 消耗基础武器落地变体入库。成功/失败均提示 + 刷新。</summary>
    private void OnModApplyRequested(string baseWeaponRefKey, IReadOnlyList<string> modNames, Pawn crafter)
    {
        WeaponModResult result = CraftingService.ApplyWeaponMod(baseWeaponRefKey, modNames, _inventory);
        if (!result.Success)
        {
            _campToast.Show($"改装失败：{result.FailureReason}", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        string name = result.Produced?.DisplayName ?? "改装武器";
        _campToast.Show($"{crafter.DisplayName} 改装出 {name}", CampToast.Ok);
        GD.Print($"[改装] {baseWeaponRefKey} → {name}");
        RefreshCrafting();
    }

    // ---------------- 医疗面板（手术/用药） ----------------

    private void OpenMedical()
    {
        if (_gameOver)
            return;
        if (!_medicalOpen)
        {
            CapturePanelTimeState(out _prevMedicalSpeed, out _prevMedicalPaused);
            _medicalOpen = true;
        }
        RefreshMedical();
        _medicalPanel.Visible = true;
    }

    /// <summary>重刷医疗面板数据（手术/用药后调，反映扣掉的耗材与更新后的伤病集）。病人候选=存活幸存者。</summary>
    private void RefreshMedical()
        => _medicalPanel.ShowFor(_survivors.Where(p => p.Alive).ToList(), _inventory);

    /// <summary>关医疗面板：恢复时标、清瞬时提示。</summary>
    private void CloseMedical()
    {
        _medicalPanel.Visible = false;
        _medicalOpen = false;
        _campToast.Hide();
        RestorePanelTimeState(_prevMedicalSpeed, _prevMedicalPaused);
    }

    /// <summary>
    /// 面板「手术」→ 算 施术者医疗书加点 / 操作能力 / 是否自体，调 <see cref="HealthConditionSet.PerformSurgery"/>：
    /// 门槛未过→显示"现状不支持进行这场手术"（零消耗）；成功/失败→按 result 扣 <see cref="SurgeryResult.ConsumedMaterials"/>、
    /// 给模糊结果提示（**不显点数/roll/效率**）。随后刷新面板。
    /// </summary>
    private void OnSurgeryRequested(Pawn patient, HealthCondition condition, IReadOnlyList<string> materials, bool onBed, Pawn surgeon)
    {
        int bookBonus = MedicalBookPoints.SumFor(surgeon.ReadBookIds);
        double capability = surgeon.OperationCapability;
        bool self = ReferenceEquals(patient, surgeon);

        // 南丁格尔护士三级特长（[SPEC-B13-补]）：手术**基础点数** per-surgeon——她本人 30、常人 15；L3 后全营遗产 +5（读永续旗标）。
        // 不复活已删的通用医疗技能系统（authored 专属 perk，身份走 SurvivorPerks.IsNightingale）；数值 15/30/+5 为用户原话非拟定。
        bool surgeonIsNightingale = surgeon.Perks.IsNightingale;
        bool l3Legacy = _storyFlags.Has(NurseRecruit.L3LegacyFlag);
        int basePoints = NightingalePerk.SurgeryBasePoints(surgeonIsNightingale, l3Legacy);

        SurgeryResult result = patient.Health.PerformSurgery(
            condition, materials, onBed, _healthRng,
            surgeonBookBonus: bookBonus, selfSurgery: self, operationCapability: capability,
            surgeryBasePoints: basePoints);

        // 升级轴＝她本人执行过的手术台数（[SPEC-B13-补2]，成败都计、重做每次计；门槛未过/重做冷却未真正施术不计）。
        // 计数持久化走 StoryFlags（字符串承载整数，RadioMainline 回复日先例）；升到 L3 那一刻置永续遗产旗标（她死/离营后 3级效果仍生效）。
        if (surgeonIsNightingale
            && (result.Status == SurgeryStatus.Success || result.Status == SurgeryStatus.Failed))
        {
            int count = NightingalePerk.RecordSurgery(_storyFlags);
            if (NightingalePerk.EvaluateLevel(count) >= 3)
                _storyFlags.Set(NurseRecruit.L3LegacyFlag, "true");
        }

        if (result.Status == SurgeryStatus.NotAllowed)
        {
            _campToast.Show(result.PlayerMessage ?? SurgeryResult.NotAllowedMessage, CampToast.Bad);
            RefreshMedical();
            return;
        }

        ConsumeMaterials(result.ConsumedMaterials); // 成功失败都扣耗材

        _campToast.Show(
            $"{surgeon.DisplayName} 为 {patient.DisplayName} 手术：{FuzzySurgeryOutcome(result)}",
            result.Success ? CampToast.Ok : CampToast.Bad);
        GD.Print($"[手术] {surgeon.DisplayName}→{patient.DisplayName} {condition.Type} {(result.Success ? "成功" : "失败")}");
        RefreshMedical();
    }

    /// <summary>
    /// 面板「用药」→ 按伤类取药（感染→抗生素 / 疾病→成药），调 <see cref="HealthConditionSet.TreatIllness"/>（疗效固定基数，通用技能已删）；
    /// 见效则扣 1 份药、给模糊提示（好转/康复）。药不对症或缺药不消耗。随后刷新面板。
    /// </summary>
    private void OnTreatRequested(Pawn patient, HealthCondition condition, Pawn surgeon)
    {
        string medKey = condition.Type == HealthConditionType.Infection ? "antibiotics" : "medicine";
        if (CraftingService.MaterialTotal(_inventory.ByCategory(ItemCategory.Material), medKey) <= 0)
        {
            _campToast.Show($"缺{Materials.Find(medKey)?.DisplayName ?? medKey}", CampToast.Bad);
            RefreshMedical();
            return;
        }

        Medicine? medicine = MedicineCatalog.For(medKey);
        TreatmentResult result = patient.Health.TreatIllness(condition, medicine);

        if (result.Status == TreatmentStatus.NoEffect)
        {
            _campToast.Show("这药对症不上，没起作用。", CampToast.Bad);
            RefreshMedical();
            return;
        }

        ConsumeMaterials(new[] { medKey });
        string msg = result.Status == TreatmentStatus.Cured
            ? $"{patient.DisplayName} 康复了"
            : $"{patient.DisplayName} 用药后有所好转，但尚未痊愈";
        _campToast.Show(msg, CampToast.Ok);
        GD.Print($"[用药] {surgeon.DisplayName}→{patient.DisplayName} {condition.Type} {result.Status}");
        RefreshMedical();
    }

    /// <summary>从营地共享库存跨堆扣减一批材料 key（含重复=按次数扣）。复用 <see cref="CraftingService.Deduct"/> 的扣减语义。</summary>
    private void ConsumeMaterials(IEnumerable<string> materialKeys)
    {
        var demand = materialKeys
            .GroupBy(k => k)
            .ToDictionary(g => g.Key, g => g.Count());
        if (demand.Count == 0)
            return;

        List<Item> stacks = _inventory.ByCategory(ItemCategory.Material).ToList();
        IReadOnlyList<Item> remaining = CraftingService.Deduct(stacks, demand);
        foreach (Item m in stacks)
            _inventory.Remove(m);
        foreach (Item m in remaining)
            _inventory.Add(m);
    }

    /// <summary>手术结果 → 模糊描述（**不外显点数/roll/效率**，只给恢复观感）。</summary>
    private static string FuzzySurgeryOutcome(SurgeryResult result)
    {
        if (!result.Success)
            return "失败了，伤情没能好转，得重来";
        return result.Efficiency >= 100 ? "很成功，恢复顺利"
            : result.Efficiency >= 40 ? "成功，恢复尚可"
            : "勉强完成，恢复得慢";
    }

    // ---------------- 工具 ----------------

    private static Vector2[] RectPoints(Rect2 r) => new[]
    {
        r.Position,
        new Vector2(r.End.X, r.Position.Y),
        r.End,
        new Vector2(r.Position.X, r.End.Y),
    };

    private static Rect2? ToRect(double[]? r) =>
        r is { Length: >= 4 } ? new Rect2((float)r[0], (float)r[1], (float)r[2], (float)r[3]) : null;

    private static Vector2? ToVec(double[]? v) =>
        v is { Length: >= 2 } ? new Vector2((float)v[0], (float)v[1]) : null;

    private static Color ToColor(double[]? c, Color fallback) =>
        c is { Length: >= 3 } ? new Color((float)c[0], (float)c[1], (float)c[2]) : fallback;

    // 颜色数组等比缩放（做暗色地基/门柱），保留原数组不越界。
    private static double[]? ScaleColor(double[]? c, float mul) =>
        c is { Length: >= 3 } ? new[] { c[0] * mul, c[1] * mul, c[2] * mul } : c;

    // ---------------- 配置加载 ----------------

    private CampConfig LoadCampConfig()
    {
        const string path = "res://data/camp.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    return JsonSerializer.Deserialize<CampConfig>(f.GetAsText()) ?? new CampConfig();
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"camp.json 解析失败，用空营地：{e.Message}");
                }
            }
        }
        return new CampConfig();
    }

    private GameClock.Config LoadDayNightConfig()
    {
        DayNightRaw raw = DayNightRaw.Default();
        const string path = "res://data/daynight.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    raw = JsonSerializer.Deserialize<DayNightRaw>(f.GetAsText());
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"daynight.json 解析失败，用默认值：{e.Message}");
                }
            }
        }

        return new GameClock.Config
        {
            DayLengthSeconds = raw.dayLengthSeconds,
            NightLengthSeconds = raw.nightLengthSeconds,
            StartAtNight = raw.startAtNight,
            DayColor = ToColor(raw.dayColor, new Color(1, 0.98f, 0.92f)),
            NightColor = ToColor(raw.nightColor, new Color(0.18f, 0.22f, 0.38f)),
            TwilightFraction = raw.twilightFraction <= 0 ? 0.12 : raw.twilightFraction,
            TravelTimeSeconds = raw.travelTimeSeconds,
            WarningBufferSeconds = raw.warningBufferSeconds,
        };
    }

    private CampResources LoadCampResources()
    {
        CampResourcesRaw raw = CampResourcesRaw.Default();
        const string path = "res://data/camp_resources.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    raw = JsonSerializer.Deserialize<CampResourcesRaw>(f.GetAsText());
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"camp_resources.json 解析失败，用默认值：{e.Message}");
                }
            }
        }
        return new CampResources(raw.initialFood);
    }

    private MealBubblePool LoadMealBubbles()
    {
        MealBubble[]? bubbles = null;
        const string path = "res://data/meal_bubbles.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    bubbles = JsonSerializer.Deserialize<MealBubble[]>(f.GetAsText());
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"meal_bubbles.json 解析失败，气泡池为空：{e.Message}");
                }
            }
        }
        return new MealBubblePool(bubbles);
    }

    // ---------------- JSON 映射（字段名对应 camp.json/daynight.json，故意小写） ----------------

    private sealed class CampConfig
    {
        public double[]? mapBounds { get; set; }
        public double navInset { get; set; }
        public double[]? cameraCenter { get; set; }
        public Heights? heights { get; set; }
        public PixelStyle? ground { get; set; }
        public RectSpec[]? mountains { get; set; }
        public PixelStyle? mountainStyle { get; set; }
        public RectSpec[]? fences { get; set; }
        public GateSpec[]? gates { get; set; }
        public RectSpec[]? gatePosts { get; set; }
        public GateMarker? gateMarker { get; set; }
        public PixelStyle? fenceStyle { get; set; }
        public BuildingSpec[]? buildings { get; set; }
        public PropSpec[]? props { get; set; }
        public SpawnSpec[]? spawns { get; set; }
        public GuardPostSpec[]? guardPosts { get; set; }
        public LightSpec[]? lights { get; set; }
        public RubbleSpec[]? rubble { get; set; }
    }

    /// <summary>预置固定光源：key = 光源键（campfire/lamp，对齐 LightSource 目录）；pos = 世界坐标 [x,y]（cartesian）。</summary>
    private struct LightSpec
    {
        public string? key { get; set; }
        public double[]? pos { get; set; }
    }

    /// <summary>预置守卫岗位：kind = watchtower/roofPlatform/hidden；stand = 守卫驻守站位 [x,y]（cartesian，须可寻路）。</summary>
    private struct GuardPostSpec
    {
        public string? kind { get; set; }
        public double[]? stand { get; set; }
    }

    /// <summary>iso 立面/屋顶抬升高度（屏幕像素）。缺省用向后兼容默认值。</summary>
    private struct Heights
    {
        public double wall { get; set; }
        public double mountain { get; set; }
        public double fence { get; set; }
        public double post { get; set; }
        public double prop { get; set; }

        public static Heights From(Heights? c)
        {
            Heights h = c ?? default;
            return new Heights
            {
                wall = h.wall > 0 ? h.wall : 46,
                mountain = h.mountain > 0 ? h.mountain : 96,
                fence = h.fence > 0 ? h.fence : 26,
                post = h.post > 0 ? h.post : 40,
                prop = h.prop > 0 ? h.prop : 20,
            };
        }
    }

    private struct RectSpec
    {
        public double[]? rect { get; set; }
    }

    private struct GateMarker
    {
        public double[]? rect { get; set; }
        public double[]? color { get; set; }
    }

    /// <summary>大门规格：rect = cartesian [x,y,w,h]；side = 朝向（north/south，供门槛/门控用）。</summary>
    private struct GateSpec
    {
        public double[]? rect { get; set; }
        public string? side { get; set; }
    }

    private struct DoorSpec
    {
        public string? side { get; set; }
        public double gapStart { get; set; }
        public double gapWidth { get; set; }
    }

    private sealed class BuildingSpec
    {
        public string? name { get; set; }
        public double[]? rect { get; set; }
        public double wallThickness { get; set; }
        public double wallHeight { get; set; }
        public DoorSpec? door { get; set; }
        public double[]? wallColor { get; set; }
        public bool roof { get; set; }
        public double[]? roofColor { get; set; }
    }

    private struct PropSpec
    {
        public string? name { get; set; }
        public double[]? rect { get; set; }
        public double[]? color { get; set; }
        public double tile { get; set; }
        public double jitter { get; set; }
        // W3a 搜刮：role = storage（共享库存存取）/ loot（一次性搜刮）；loot = 藏物清单（storage 类即开局库存）。
        public string? role { get; set; }
        public LootSpec[]? loot { get; set; }
    }

    /// <summary>容器藏物一条：kind = food/book/weapon/armor；qty = 食物份数；id = 书 id / 武器名 / 护甲名。</summary>
    private struct LootSpec
    {
        public string? kind { get; set; }
        public int qty { get; set; }
        public string? id { get; set; }
    }

    /// <summary>开局废墟点（批次9）：name=稳定标识；rect=[x,y,w,h]；workMinutes=挖净总工时（游戏分钟）；
    /// eggSlot=是否彩蛋位；eggContentId=彩蛋 authored 叙事键（draft·待用户细化，映射 <see cref="EggContent"/>；空=仅占位提示）；drops=挖净产出（同 <see cref="LootSpec"/>，主要木/碎金属/布）。</summary>
    private struct RubbleSpec
    {
        public string? name { get; set; }
        public double[]? rect { get; set; }
        public int workMinutes { get; set; }
        public bool eggSlot { get; set; }
        public string? eggContentId { get; set; }
        public LootSpec[]? drops { get; set; }
        public double[]? color { get; set; }
        public double tile { get; set; }
        public double jitter { get; set; }
    }

    private struct SpawnSpec
    {
        public string? name { get; set; }
        public double[]? pos { get; set; }
        public bool pistol { get; set; }
        public double[]? color { get; set; }
    }

    private struct CampResourcesRaw
    {
        public int initialFood { get; set; }

        public static CampResourcesRaw Default() => new()
        {
            initialFood = 12,
        };
    }

    private struct DayNightRaw
    {
        public double dayLengthSeconds { get; set; }
        public double nightLengthSeconds { get; set; }
        public double travelTimeSeconds { get; set; }
        public double warningBufferSeconds { get; set; }
        public bool startAtNight { get; set; }
        public double[]? dayColor { get; set; }
        public double[]? nightColor { get; set; }
        public double twilightFraction { get; set; }

        public static DayNightRaw Default() => new()
        {
            dayLengthSeconds = 720,
            nightLengthSeconds = 480,
            travelTimeSeconds = 120,
            warningBufferSeconds = 300,
            startAtNight = false,
            twilightFraction = 0.12,
        };
    }
}
