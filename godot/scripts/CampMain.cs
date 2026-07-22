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
/// 切小块各自 YSort 修 B0 擦身错序。屋顶透视效果由表现配置控制（<see cref="RoofFade"/>）。
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

    // 破防「攻击位名额」账本（谁占着哪堵墙）：一处结构前只站得下几个攻击者，挤不进的去啃旁边的墙。见 BreachSlots。
    private readonly BreachSlotBook _breachSlots = new();
    // 破防候选缓存（结构毁了 / 门开关了才重建 —— 被阻隔的敌人每帧都要问一次，不能每次都重扫全表）。
    private readonly List<BreachCandidate> _breachCandidates = new();
    private bool _breachCandidatesDirty = true;
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
    // 门缝连通自检跑没跑过（建图期一次性的地图健全性检查）。与 _navTested 解耦：后者是"导航就绪"闸，
    // 每次重烘焙都会复位；而连通自检**只该在建图后跑一次**——门落地后建筑可达性是动态的（关门=本该不可达），
    // 再跑就会把"门关上了"误报成"导航洞失效"。
    private bool _navVerified;

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
    // 袭营推进节流：守卫目标分派 + 破防/胜负统计不必每帧，按运行时节流配置执行。
    private double _raidUpdateElapsed;
    private const double RaidUpdateInterval = 0.15;

    private bool _hudInit;
    private bool _hudExploring;
    private int _hudBagKey = -1;   // 远征背包已背重量的脏键（营地无背包 = -1）
    private int _hudLoadKey = -1;  // [T45] 最慢队员的负重移速惩罚脏键——上限随伤/饿变时 CarriedKg 可能一动不动，需独立键
    private int _hudDay = -1;
    private int _hudPhase = -1;
    private int _hudClockKey = -1;
    private int _hudSpeedIndex = -1;
    private bool _hudPaused;
    private int _hudAlive = -1;
    private int _hudHordePhase = -1;   // 尸潮相位脏检信号（-1=未初始化；旗标翻转/到期不必每帧重算文案）
    private int _hudHordeDays = -1;     // 剩余天数脏检信号（仅随 _clock.Day 变）
    private double _audioMoodElapsed;    // 自适应音乐脏检节流；听感状态不参与任何玩法 tick。

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
    // 弹药源（批次18）：全体玩家幸存者共用一个，直接读写营地共享库存（弹药=可堆叠材料）。
    // 懒构造是因为 C# 字段初始化器不能引用别的实例字段（_inventory）。
    private InventoryAmmoSource? _ammoSource;
    private InventoryAmmoSource AmmoSource => _ammoSource ??= new InventoryAmmoSource(_inventory);
    // loot 容器（衣柜/展示柜/草垛/尸体/废墟）的藏物登记；storage 容器（柜子）不入此表，其藏物开局即入库存。
    // 玩家搜它们走**逐件搜刮**（TakeNext 一件件扣），不是一把抽干。
    private readonly ContainerLoot _containerLoot = new();
    // 正在进行的逐件搜刮：**一人一份**（用户拍板「允许玩家控制一个角色去搜刮转物品，**然后控制另一个角色**」）。
    // ⇒ 搜刮是**派下去的一件持续的活**，不是把玩家锁进去的模态交互：A 蹲着掏尸体的同时，玩家可以切去控制 B
    //   放哨/关门/搜另一个点，**A 在后台自己接着掏**。多人并发就在这个字典里各占一格，互不干扰。
    //   分工由此产生 ⇒ **人手第一次成为战术资源**。
    private readonly Dictionary<Pawn, LootJob> _lootJobs = new();

    /// <summary>一个人身上正在跑的搜刮活：进度模型 + 他头顶那条世界内进度条（不是 HUD 面板）。</summary>
    private sealed record LootJob(LootSession Session, LootProgressBar Bar);
    // 远征背包（**只在探索关内存在**，回营即倾倒进共享库存后清空）：这一趟搬得动的东西。
    // 营地里没有上限（家就是仓库）；出门在外才有"背不下就是拿不走"的硬上限（上限以 Wiki 配置为准）。
    private ExpeditionBag? _bag;
    // 探索地点遗留尸体：跨关卡保留三个半天；普通遗物随尸体过期消失，关键设备过期后回广播站原点。
    private readonly List<ExplorationCorpseSave> _explorationCorpses = new();
    // 关键设备的本趟携带态：活人回营才提交 RadioMainline；全灭则转移到最后倒下的队员尸体。
    private bool _expeditionHasTransmitter;
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
    private (Pawn pawn, ContainerRef target, bool salvage)? _pendingInteract;
    private double _pendingInteractElapsed;                 // 本次前往已耗时（真实秒，用于超时放弃）

    // 场上**可拆家具**的句柄账本（键 = camp.json 的 prop name）：拆完要把碰撞体/视觉块从世界上抹掉、补回导航。
    // 只登记 FurnitureBuildCost 目录里的那几件——收音机/草垛这类"不是造出来的东西"拆不动，也就不占这份账。
    private readonly Dictionary<string, FurnitureInstance> _furniture = new();

    /// <summary>一件可拆家具在世界上的句柄（拆除清场用）。</summary>
    private sealed class FurnitureInstance
    {
        public Rect2 Rect;
        public StaticBody2D? Body;
        public List<Node2D> Visuals = new();
    }

    // 撬锁进行中（门系统）：谁 / 撬哪扇门 / 本次尝试已耗时 / 本次尝试需时。**实时秒**——撬锁是战术动作，
    // 不是工时制作业：门外那群东西不会停下来等你把工时耗完。失败=断一根铁丝、时间照花，还有铁丝就自动接着撬。
    // 右键动作菜单（撬锁/静默拆除/破坏）。**非模态、不暂停**——你在菜单前犹豫的每一秒，外面那群东西都在往前挪。
    private SiteActionPopup _actionPopup = null!;
    private (Pawn pawn, CampStructureInstance site)? _siteMenuTarget;             // 菜单弹出时的目标
    private (Pawn pawn, CampStructureInstance site, SiteAction action)? _pendingSite; // 已选定、正走过去
    private double _pendingSiteElapsed;
    // 静默拆除 / 破坏 的在办作业（实时秒）。**中断即作废当前这段**——与逐件搜刮(LootSession)、撬锁同一条规则。
    private (Pawn pawn, CampStructureInstance site, double elapsed, double need)? _dismantling;
    private (Pawn pawn, CampStructureInstance site, double timer)? _bashing;

    // 砌墙（加固 / 修补）在办作业：一条边一份，**逐格推进**。见 BeginFenceWork / TickFenceWork。
    private FenceWorkSession? _fenceWork;
    private int _nextRunId; // 建图时给每条边（每整条围栏 / 每扇大门）发一个号

    private (Pawn pawn, CampStructureInstance door, double elapsed, double need)? _picking;
    private readonly IRandomSource _pickRng = new SystemRandomSource(); // 撬锁 roll（项目铁律：随机走可注入源）
    private const float PendingInteractStandoff = 20f;      // 停在容器最近边缘外的间距
    private const float PendingArriveMargin = 28f;          // 到达判定：进容器 rect 外扩此值内算到位
    private const double PendingInteractTimeout = 18.0;     // 前往超时（秒）：超时未到达则放弃 + 提示

    // ---------------- 配方 / 制作（工作台接入） ----------------

    private CraftingJob? JobAt(string slotKey) => _facilityJobs.FindBySlot(slotKey)?.Job;
    private Pawn? WorkerAt(string slotKey)
    {
        FacilityJobSlot? slot = _facilityJobs.FindBySlot(slotKey);
        return slot is null ? null : _survivors.FirstOrDefault(p => p.Id == slot.WorkerId);
    }

    private void SyncProductionAssignments()
        => _roleManager.SetProductionAssignments(_facilityJobs.Jobs.Select(x => x.WorkerId));

    private Rect2? FacilityRectForSlot(string slotKey)
    {
        string? role = slotKey switch
        {
            FacilityJobKeys.MainWorkbench => "workbench",
            FacilityJobKeys.MainWeaponBench => "weaponbench",
            FacilityJobKeys.MainModBench => "modbench",
            FacilityJobKeys.MainCookStation => "cookstation",
            FacilityJobKeys.MainButcherStation => "butcher",
            _ => null,
        };
        if (role is not null)
            return _containers.FirstOrDefault(c => c.Role == role)?.Rect;

        const string cropPrefix = "cropplot:";
        if (slotKey.StartsWith(cropPrefix, StringComparison.Ordinal))
        {
            string name = slotKey[cropPrefix.Length..];
            return _containers.FirstOrDefault(c => c.Role == "cropplot" && c.Name == name)?.Rect;
        }

        const string worksitePrefix = "worksite:";
        if (slotKey.StartsWith(worksitePrefix, StringComparison.Ordinal))
        {
            string target = slotKey[worksitePrefix.Length..];
            if (SalvageLogic.StructureIndexOf(target) is int idx && idx >= 0 && idx < _structures.Count)
                return _structures[idx].Rect;
            if (SalvageLogic.FurnitureNameOf(target) is string furniture)
                return _containers.FirstOrDefault(c => c.Name == furniture)?.Rect;
        }
        return null;
    }

    private bool CanStartFacilityJob(string slotKey, Pawn worker, out string why)
    {
        why = "";
        if (worker.Role != PawnRole.Idle || !worker.Alive)
        {
            why = worker.Role == PawnRole.Guard ? "站岗的人不能参与生产。" : "这个角色正在执行别的任务。";
            return false;
        }
        if (_facilityJobs.FindBySlot(slotKey) is not null)
        {
            why = "这座设施占用中。";
            return false;
        }
        if (_facilityJobs.FindByWorker(worker.Id) is not null)
        {
            why = "这个角色已经在另一座设施生产。";
            return false;
        }
        if (FacilityRectForSlot(slotKey) is null)
        {
            why = "对应的生产设施不在营地。";
            return false;
        }
        return true;
    }

    private void StartFacilityJob(string slotKey, CraftingJob job, Pawn worker)
    {
        FacilityJobStartResult start = _facilityJobs.TryStart(slotKey, job, worker.Id, workerMayProduce: true);
        if (!start.Started)
            throw new InvalidOperationException($"生产任务登记失败：{start.Failure}");
        _craftMinuteBudgets[slotKey] = 0f;
        SyncProductionAssignments();
        // SetProductionAssignments 只更新下一次相位重排的数据源；当前相位下单必须立刻占住角色，
        // 否则 TickCraftingWorktime 会一直看到 Idle、直到跨相位才开始做工。
        worker.Role = PawnRole.Producing;
        if (FacilityRectForSlot(slotKey) is Rect2 rect)
        {
            worker.ProducingStationing = true;
            worker.CommandMoveTo(NearestEdgeStandPoint(rect, worker.GlobalPosition));
        }
    }
    // 全营共享一台工作台的工具装配态（field 初始化，早于 ApplyStorageInitialStock 就绪，供工具搜到即装）。
    private readonly WorkbenchState _workbench = new();
    private CraftingPanel _craftingPanel = null!;
    private bool _craftingOpen;             // 制作面板是否开着（与库存互斥地持有时标冻结）
    private int _prevCraftingSpeed;         // 开制作面板前 GameClock 的速度档（关闭时按此还原）
    private bool _prevCraftingPaused;       // 开制作面板前世界是否暂停（还原保真）
    // 工时制：每座真实设施各一槽；FacilityJobBoard 同时焊死“一人不可被多槽占用”。
    private FacilityJobBoard _facilityJobs = new();
    private string _craftingPanelSlotKey = FacilityJobKeys.MainWorkbench;
    private int _craftLastMinuteKey = -1;   // 上帧游戏分钟键（ClockMinuteKey）；算夜间流逝分钟增量推进工时，非生产相位/无任务时置 -1
    private readonly Dictionary<string, float> _craftMinuteBudgets = new(StringComparer.Ordinal);

    // 手术工时：只持稳定 id/目标键的纯状态机。医疗面板点下后立即关闭并恢复世界时间；
    // 患者流血仍由 Actor.Body.TickBleed 推进，本层绝不重复扣血。
    private SurgeryJob? _surgeryJob;
    private int _surgeryLastMinuteKey = -1;
    private Vector2 _surgeryStandPoint;
    private Vector2 _surgeryPatientLockedPosition;
    private string? _surgeryLockedBedKey;
    private double _surgeryApproachElapsed;

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
    private float _digMinuteBudget;          // 挖掘工时的小数分钟预算（非整系数下累积不截断；随 _digLastMinuteKey 一并复位）

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
    private CreditsPanel _creditsPanel = null!;      // 素材出处（F1，CC BY 署名）。只读，**不冻结时标**——看一眼出处而已
    private DiscoveryPanel _discoveryPanel = null!;  // 探索发现环境叙事面板（模态，弹出时冻结时标）
    private double _prevDiscoveryTimeScale;          // 弹发现叙事前的时标（关闭时恢复）
    private double _prevLookoutTimeScale;            // 望远镜瞭望演出前的时标（演出期间冻结探索层，播完恢复）

    // ---------------- 神秘商人（中立到访者 + 货币交易） ----------------
    // 商人夜晚到访、天亮离开（游戏白天=探险队视角、夜晚=营地视角，商人须与营地可操作窗口重合）；
    // 只卖《木匠入门》（架子数据驱动可扩展）；货币=白银，走共享库存实扣实产。
    private const int MerchantStartingCurrency = 40; // draft：开局起步白银（**整银**，grant 时经 Silver.FromWhole 转分；让交易闭环开箱即跑；掉落来源/经济量级待用户调参（见 TODO §6））
    private MerchantPanel _merchantPanel = null!;
    private MerchantSchedule _merchantSchedule = null!;
    private MerchantShelf _merchantShelf = null!; // 在冷启动 / 读档时初始化（含互斥随机选择）
    private Merchant? _merchant;                      // 当前在场商人（null=未来访）
    private ContainerRef? _merchantContainer;         // 商人停留点的可交互登记（离场时从 _containers 移除）
    private Pawn? _merchantTrader;                    // 本次实际走到商人面前的交易者；逐角色已读书效果只看此人
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
        /// <summary>关内发现点的玩家可见短标签；营地容器为空。</summary>
        public string Hint = "";
        /// <summary>关内发现点触发方式；仅 LevelDiscoveryRole 消费。</summary>
        public NarrativeTrigger DiscoveryTrigger = NarrativeTrigger.Proximity;
        /// <summary>role=corpse：先弹的那段叙事的调查点 id（看过之后才退化成普通 loot）。其余 role 为空。</summary>
        public string NarrativeId = "";
    }
    // 剧情/世界 flag 存储：condition 谓词只读它、气泡 triggers 写它，推动用户手写剧情走向。
    // 内容（哪些 flag、取什么值）全部来自 meal_bubbles.json 里用户填的数据；此处仅持有存储实例。
    private readonly StoryFlags _storyFlags = new StoryFlags();
    // 近期已故名单：死者已从 _survivors 移除，这里另存**死亡当刻拍的快照**供聚餐气泡"死亡反应"读取（dead 谓词命中）。
    // 死时即拍（不持 Pawn 引用，避免节点被回收后 Inspect 失效）；每餐结算末清空——死亡只在紧随其后的一餐被提及（"近期"）。
    private readonly List<PawnSnapshot> _recentlyDeceased = new();
    private string _pendingDestination = "";
    private int _pendingTravelTime;

    // [T57] 调查点网状解锁：**去过哪些调查点**（内部路由键）。解锁 = 前置点【去过】且达到 Wiki 配置探索度，
    // 探索度那一半由 _storyFlags 里的 searched_*/found_* 推出（ExplorationProgress.Completion），
    // 这里只需记「去过」这一半。判定全部走 WorldGraphUnlock —— 本文件不许另写一份"能不能去"。
    private readonly HashSet<string> _visitedDestinations = new(StringComparer.Ordinal);

    // 老档兜底：T57 之前的存档没有「去过」名单（SaveData.VisitedDestinations == null）⇒ 那时候全图本来就都能去，
    // 一律视为**全部已解锁**，不剥夺玩家已有的进度。新游戏/新档为 false。
    private bool _legacyFullUnlock;

    private Node2D _logicLayer = null!;  // 物理/导航平面（cartesian，不可见）
    private Node2D _isoLayer = null!;     // 视觉层（iso，YSort）
    private Node2D _actorLayer = null!;   // actors（LogicLayer 下）

    // 尸体管家（impl-corpse-push）：谁倒下就在其脚下落一具尸体，同格已有尸体则自动挤到旁边最近的空地
    // （RimWorld 式）。尸体**无碰撞体积**——不建碰撞体、不挖导航洞，活人和丧尸从尸体上直接走过去；
    // 它只占 CorpseField 的「尸体格」，决定下一具尸体往哪躺。规则见 CorpseField（纯逻辑，有单测）。
    private CorpseYard _corpseYard = null!;

    // ---------------- D 守卫防御战 ----------------
    // 已建岗位（含预置 + 调试放置）。哨塔/屋顶=实心结构；暗哨=非碰撞站位标记。
    private readonly List<GuardPostInstance> _guardPosts = new();

    // ---------------- 批次4 光照与视野：营地固定光源场 ----------------
    // 读 camp.json lights → 固定光源(火堆/油灯)集合。供 vision-render 遮暗合成 / enemy-vision 感知
    // 按局部光照 L 查询：field.StrongestAt(x,y[,手持光源]) 出最强光源贡献，再 VisionLogic.CombineLight(环境光,贡献)。
    // 本类只做数据采集入口；发光视觉(Light2D/光晕)由 vision-render 渲染层负责。
    private readonly LightField _campLights = new();

    /// <summary>本帧移动光源采样缓冲（幸存者手持光 + 交战中的劫掠者火把；复用去 alloc）。</summary>
    private readonly List<PlacedLight> _handheldScratch = new();

    // 视野揭示（CampRevealables）复用态：去 4Hz 逐 hostile 闭包/ValueTuple 分配（tech-review #2）。
    private readonly List<(Vector2 worldPos, Action<bool> setVisible)> _revealBuffer = new();
    private readonly Dictionary<Actor, Action<bool>> _revealDelegates = new();
    private readonly List<Actor> _revealPrune = new();

    private DoorStateOverlay _doorOverlay = null!;         // 门态徽标（开/关/闩/锁 四态四形状；ZIndex 4080 压在遮暗之上，夜里也读得到）

    /// <summary>营地固定光源场（供视野/遮暗按位置查询最强光源贡献）。</summary>
    public LightField CampLights => _campLights;

    // ---- 半身掩体（桌子/椅子/沙袋）：远程按 Wiki 配置整发无效 ----
    // camp.json 里 cover:true 的 prop 登记于此（**非实心**矮物：不建碰撞/不挖导航洞/不断视线 ⇒ 看得见、
    // 打得到、走得过，只是远程命中后按配置判无效）。承伤入口 Actor.ReceiveAttack 经 static Actor.Covers 查询；
    // 判定含**方向性**（掩体须落在射击者与目标连线上，绕后即失效）与**双向对称**（敌人也躲得）。
    private readonly CoverField _coverField = new();

    /// <summary>半身掩体物件的立体块高度（视觉上"半身"——明显矮于实心道具 _heights.prop=20；拟定待调）。</summary>
    private const float CoverPropHeight = 12f;

    // ---- 沙袋：玩家可建造、可**自由摆放**的半身掩体（用户拍板）----
    // 为什么沙袋能自由摆而**墙不能建**（"墙不能建"是用户为防 kill box 拍的板，别当成规则不一致而"统一"掉）：
    // 沙袋**不建碰撞体、不挖导航洞** ⇒ 不阻挡移动、不改变寻路 ⇒ 敌人照样直线冲过来、不会被迷宫牵着走
    // ⇒ **摆不出 kill box**。而且它双向对称——敌人也能蹲在你垒的沙袋后面获得同一配置效果。完整论证见 SandbagSpec 类注。
    /// <summary>正处于"摆放沙袋"模式（左键落位、右键取消）。</summary>
    private bool _placingSandbag;
    /// <summary>已摆沙袋的流水号（容器名 "沙袋#N" 需唯一；拆除时按类型名归一，见 FurnitureBuildCost.Of）。</summary>
    private int _sandbagSeq;

    // ---- 改装台（批次21·T7/T10）：玩家可建造，但**位置是固定的**（用户拍板）----
    // 用户原话：「改装台、烹饪台**不允许跨越**，但是他们是营地内**固定位置**。改装台放在**车间**。」
    // 而 camp.json 里本来没有车间（只有 住宅/仓库/**空牛棚**）⇒ 用户选定：**空牛棚改造成车间**。
    // ⇒ 玩家**摆不了**改装台：在工作台造完，它自动落在车间（空牛棚）的固定锚点上。
    //   没有"放置"这个动作 ⇒ 也就没有"放置时不许贴围栏"那回事（PlacementRules 是给可摆放家具的）。
    //   但它**实心、挖导航洞、不可跨越**，且玩家挪不动 ⇒ 锚点本身仍按禁建带口径自检
    //   （见 WeaponModLogic.BenchAnchorX/Y 的论证 + impl-modbench 的 FixedFacilityAnchorTests）。

    /// <summary>营地里有没有改装台（武器改造的唯一场所；拆了就没了）。</summary>
    private bool HasModBench => _furniture.ContainsKey(WeaponModLogic.BenchFurnitureKey);
    private bool HasWeaponBench => _furniture.ContainsKey(WeaponBench.FurnitureKey);

    // [T67] 宰杀设施在场判定（单座设施，落 _furniture 时键＝家具名，同烹饪台/改装台的既有做法）。
    private bool HasButcherPoint => _furniture.ContainsKey(ButcherStation.PointFurnitureKey);
    private bool HasButcherTable => _furniture.ContainsKey(ButcherStation.TableFurnitureKey);
    private bool HasButcherStation => HasButcherPoint || HasButcherTable;
    private readonly List<Zombie> _raidZombies = new();  // 当前袭营波次（存活）
    private VisionMask? _campVisionMask;                   // 营地视野遮暗（批次4，夜间启用/白天豁免）
    private PathOverlay? _pathOverlay;                     // 选中角色的移动路径线（RimWorld 式，画导航真实路径；ZIndex 4090 压在遮暗之上）
    private readonly List<Pawn> _raidGuards = new();       // 本夜上岗守卫（存活）
    private readonly List<Dog> _raidGuardDogs = new();     // 本夜上岗犬类守卫（布鲁斯，站岗效率读取 Wiki 配置，与 _raidGuards 平行）

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
    // 夜事件掷点独占随机流：入夜只掷一次，不能因守卫数量/对抗掷点次数改变当夜事件类型与触发时刻。
    private readonly IRandomSource _nightEventRng = new SystemRandomSource();
    private NightEventSchedule _nightEventSchedule = NightEventSchedule.None;
    // 岗哨初始扫视相位使用独立随机源，避免多一个守卫改变夜袭对抗的随机序列。
    private readonly IRandomSource _guardSweepRng = new SystemRandomSource();
    private readonly Dictionary<int, float> _guardPostSightById = new();       // 守卫/犬 id → 岗位视野倍率（岗哨建筑加成源，StationGuards 采集）
    private const int NightRaiderCountBase = 2;             // 袭击者基数（随天数缓增，拟定待调）
    // 对抗掷点时机：袭击者边缘生成时距守卫超出当前感知范围 → 单次掷点发现率很低。
    // 改为随尖兵**逼近**到各距离带（对最近守卫的距离）时分段各掷一次（帧率无关，检测累积）；深入到营心 StrikeDistance 仍未发现 → 未发现后果兑现。
    // 距离带与校准结果属于历史实验；当前参数以 Wiki 配置与对应校准报告为准。
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
    // 累积态——随 BondSave 落盘；等级经 DougBruceBond.EvaluateLevel 现算，不另存派生等级。
    private int _bondDaysBothAlive;

    // 克莉丝汀「巧舌如簧」升级轴：她入队后**在营存活天数**（每昼夜她在营存活 +1；死/离营天然停累加=冻结，见 AdvanceChristineDay）。
    // 累积态——与羁绊天数一同落 BondSave；等级经 ChristinePerk.EvaluateLevel（+灭金手指帮旗标）现算，不另存派生等级。
    private int _christineDaysInCamp;
    private bool _raidActive;
    private float _raidIntensity = 1f;
    // 尸潮终局：到期(day>=DeadlineDay)启动的无限围攻。复用袭营执行层(_raidActive+守卫锁敌+SpawnCampZombies)，
    // 但不走胜负结算——波次不停轮、无生还路线，唯一出口是全灭(GameOverCondition)。数值调度归 HordeTimeline。
    private bool _siegeActive;
    private int _siegeWaveIndex;        // 已投放波次序号（0=首波）
    private double _siegeWaveElapsed;    // 距上一波投放已过秒（逐帧累积，喂 HordeTimeline.NextWave）
    // 【已删】旧 _militaryRaidWipeContext（结局②"全员在营被军袭屠尽 → 全灭走 CG②"上下文）：该设计已推翻——
    // 军袭走强制终局南逃谢幕序列（BeginSouthEscapeEnding，不经全灭判定）。该字段全仓零赋值、恒 false，
    // 连同整个全灭结局路由（EndingKind/ForGameOver/ForKind）一并退役（[用户裁决·选项B]）。
    private GuardPostKind _debugPlaceKind = GuardPostKind.Watchtower; // 调试放置轮换类型
    private const float BreachRadius = 420f;  // 破防线：丧尸摸进营心此半径内 = 破防（地图调整时同步校准）

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
    /// 一处可破坏结构（围栏 / 大门 / 门）的运行时实例：血量状态 + cartesian rect + 碰撞体/视觉块。
    /// <para>
    /// <b>Blocking = 挡人 + 挡视线 + 断寻路，三合一</b>。围栏恒 true；**门的 Blocking 随开关动态变化**
    /// （<see cref="SetDoorBlocking"/>）——因为这三件事在本仓由**同一个墙层 StaticBody2D + 同一条导航洞**承载：
    /// 碰撞天然挡人；<see cref="VisionOcclusion"/> 正是对墙层打 raycast，故自动挡视线；<see cref="BakeNavPoly"/>
    /// 把 <c>_navHoles</c> 挖成障碍，故自动断寻路。开门 = 摘掉这两样，关门 = 装回去。
    /// </para>
    /// </summary>
    private sealed class CampStructureInstance
    {
        public CampStructureState State = null!;
        public Rect2 Rect;
        public bool Blocking;                       // 当前是否挡路（门会动态变；围栏/关着的大门恒 true）
        public StaticBody2D? Body;                  // 门：常驻（靠 CollisionLayer 开关）；围栏/大门：仅 Blocking 时存在
        public readonly List<Node2D> Visuals = new();
        public bool Removed;                        // 已摧毁并清场（防重复摧毁/重烘焙）

        /// <summary>是围栏（网格：看得穿/射得穿/挡移动/挡近战/是半身掩体），而非实心大门。摧毁时据此撤掩体登记。</summary>
        public bool IsFence;

        /// <summary>
        /// 这一格属于哪"<b>一条边</b>"（camp.json 里的一整条围栏 = 一条边；每扇大门自成一条边）。
        /// <b>-1 = 不属于任何可加固的边</b>（民居的门体、山体…）。
        /// <para>
        /// 升级/修复<b>按边下令、按格结算</b>（见 <see cref="FenceUpgradeLogic.PlanUpgrade"/>）：敌人是**就近**挑一格砸的，
        /// 你事先不知道它撞哪一格 ⇒「防线强度 = 最弱那一格」⇒ 只升一格等于白花料。<b>能改变结果的最小单位是一整面墙。</b>
        /// </para>
        /// </summary>
        public int RunId = -1;

        /// <summary>建这一格用的样式/随机种/高度/瓦片尺寸 —— <b>升级换档、修复补洞时要照着重建视觉</b>，故留档。</summary>
        public PixelStyle Style = null!;
        public int Seed;
        public float Height;
        public float Cell;

        // ---- 以下仅门（Door）用 ----
        /// <summary>非 null = 这是一扇门（可开关）。围栏/不可开关的结构为 null。</summary>
        public DoorState? Door;
        /// <summary>锁的档次（<see cref="LockTier.None"/> = 没装锁）。</summary>
        public LockTier Lock;
        /// <summary>门名（"住宅的门" / "北大门"），供交互提示/日志。</summary>
        public string DoorName = "";

        /// <summary>
        /// 这扇门有没有门闩（营地大门 = 有；民居的门 = 没有）。
        /// <b>关上即闩上</b>——不做单独的"闩门"交互（用户偏好简化：开门只有一种动作）。见 <see cref="DoorLogic.ClosedRestingState"/>。
        /// </summary>
        public bool Barrable;

        public bool IsDoor => Door.HasValue;
    }

    /// <summary>单个已建岗位：类型 + 属性 + 守卫驻守站位（cartesian，须可寻路）。</summary>
    private sealed class GuardPostInstance
    {
        public GuardPostKind Kind;
        public GuardPostStats Stats;
        public Vector2 StandPos;
        public string Name = "";
        // 同一夜同一岗位只掷一次；重复调用 StationGuards 也不得让守卫瞬移式换相位。
        public int SweepPhaseDay = int.MinValue;
        public double SweepPhaseSeconds;
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

    /// <summary>
    /// 墙碰撞层（Actor 只与此层碰撞）。<b>这一层同时承载三件事</b>：挡角色（物理）、挡视线
    /// （<see cref="VisionOcclusion"/> 正是对本层打 raycast）、以及配合 <c>_navHoles</c> 断寻路。
    /// 门的开关就是切这一层（<see cref="SetDoorBlocking"/>）—— 一处开关，三件事同时生效。
    /// 单一真源 <see cref="VisionOcclusion.WallMask"/>，防两处漂移。
    /// </summary>
    private const uint WallCollisionLayer = VisionOcclusion.WallMask;

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

        // 全场死亡的**唯一**落尸入口。必须走静态 Actor.AnyDied 而不是逐实例的 Died：
        // 逐实例订阅散落在各生成点，而**丧尸与劫掠者根本不经过 OnActorDied**（它们各自挂
        // OnRaidZombieDied / OnNightRaiderDied 只做名单清理）——若只在 OnActorDied 落尸，
        // 尸潮里成片倒下的丧尸会一具尸体都不留，正是最需要尸体的那个场合。
        Actor.AnyDied += OnAnyActorDied;

        // 挨打即撒手：正在逐件搜刮的人挨了一下 → 当场中断（见 OnAnyActorDamaged）。
        // 搜刮是站着不动的一段暴露时间，被咬了还能把抽屉翻完，那这机制就白设了。
        Actor.AnyDamaged += OnAnyActorDamaged;

        BuildWorld();
        BuildNavigation();
        _actionPopup = new SiteActionPopup();
        _actionPopup.ActionChosen += OnSiteActionChosen;
        AddChild(_actionPopup);

        SetupCampVisionMask();

        // 移动路径线：与 VisionMask 同为本节点直接子（各自 TopLevel 画在 iso 世界坐标），ZIndex 4090 > 遮暗 4000
        // → 夜里也读得清。**己方每个幸存者各一条**（各用自己的 camp.json 配色），敌方永不入供给。
        // 布鲁斯（Dog）不给：他是纯 AI 跟随（Dog.cs 每帧跟主人 / 站岗由系统指派），玩家无法直接下令，画了是噪音。
        // 选中只影响醒目程度（RefreshSelectionUi→SetSelected），不影响画不画。
        _pathOverlay = new PathOverlay { Name = "PathOverlay" };
        _pathOverlay.SetUnitsProvider(PathVisibleUnits);
        AddChild(_pathOverlay);

        // 门态徽标：每扇门头顶一枚小牌子，把「开 / 关 / 闩 / 锁」画成**四个不同的形状**（不靠颜色区分）。
        // 此前闩着和关着在画面上长得一模一样——而「只是关着」的门劫掠者一推就开，「闩着」的他只能砸，
        // 两者天差地别。ZIndex 4080 > 遮暗层 4000：**夜里也读得到**（门闩恰恰是夜里才决定生死）。
        _doorOverlay = new DoorStateOverlay { Name = "DoorStateOverlay" };
        _doorOverlay.SetProvider(DoorBadges);
        AddChild(_doorOverlay);

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
        _expeditionPanel.SelectDestinationRequested += () => OpenWorldMap();
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
        _stashPanel.BagDropRequested += OnBagDropRequested;
        // 手持光源「持起」接线待 Pawn.HeldLight/EquipLight 就绪（perf-ux-fix 正持 Pawn 锁），见 journal [HANDOFF]。
        _stashPanel.LightHoldRequested += OnStashLightHoldRequested; // 库存「持起」手持光源（手电/火把）→ Pawn.HeldLight
        _stashPanel.SalvageRequested += OnStashSalvageRequested;     // 库存「拆解」→ 走工时制拆回材料（返还规则以 Wiki 配置为准）
        _stashPanel.PlaceRequested += OnStashPlaceRequested;       // 库存「摆放」→ 沙袋放置模式（左键落位/右键取消）
        _stashPanel.DogGearUnequipRequested += OnStashDogGearUnequipRequested; // 库存「布鲁斯装备」区「脱下」→ 从布鲁斯脱下退库
        // 布鲁斯当前穿戴态：给库存面板「布鲁斯装备」区提供实时数据（布鲁斯不在场则空 → 该区不出现）。
        _stashPanel.DogGearProvider = () => _bruce is { Alive: true } b ? b.Apparel.EquippedKeys : null;
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

        // [批次21·T14] 烹饪面板（右键前往厨房的烹饪台打开；冻结时标）。接线见 CampMain.Cooking.cs。
        SetupCookingPanel();
        SetupButcheryPanel();   // [T67] 宰杀设施面板（正文在 CampMain.Butchery.cs）

        // [impl-furniture-registry] 可摆放家具注册表：把散落的 ~5 处平行分派链收成一张表（正文在 CampMain.Placeables.cs）。
        BuildPlaceables();

        // 神秘商人交易面板（右键前往在场商人打开；冻结时标）。买入事件走 MerchantTrade.Buy 实扣白银实产商品。
        _merchantPanel = new MerchantPanel { Layer = 20 };
        AddChild(_merchantPanel);
        _merchantPanel.Visible = false;
        _merchantPanel.BuyRequested += OnMerchantBuyRequested;
        _merchantPanel.SellRequested += OnMerchantSellRequested;
        _merchantPanel.Closed += CloseMerchant;

        // 医疗面板（按 M 打开；冻结时标）。事件接手术与感染疗程。
        _medicalPanel = new MedicalPanel { Layer = 20 };
        AddChild(_medicalPanel);
        _medicalPanel.Visible = false;
        _medicalPanel.SurgeryRequested += OnSurgeryRequested;
        _medicalPanel.TreatmentAssigned += OnInfectionCourseAssigned;
        _medicalPanel.TreatmentCancelled += OnInfectionCourseCancelled;
        _medicalPanel.RosehipTeaRequested += OnRosehipTeaRequested;
        _medicalPanel.AmputationRequested += OnAmputationRequested;
        _medicalPanel.ProstheticSurgeryRequested += OnProstheticSurgeryRequested;
        _medicalPanel.Closed += CloseMedical;

        // 制作/搜刮一行瞬时提示（HUD 之上，独立高层，时标冻结下靠手动显隐）。
        _campToast = new CampToast { Layer = 26 };
        AddChild(_campToast);

        SetupSavePanel();   // 存档 / 读档面板（F5），见 CampMain.Save.cs

        // 素材出处面板（F1）：物品图标取自 game-icons.net，授权 CC BY 3.0——**署名必须让玩家看得见**，
        // 只在仓库 CREDITS.md 里写一份是不够的。文本单一真源在 CreditsContent.cs。
        _creditsPanel = new CreditsPanel();
        AddChild(_creditsPanel);

        // [TODO 21①] 冷启动读档：上层（主菜单）在切场景前把要读的存档槽写进 CampMain.PendingColdLoadSlot。
        // 读成功 ⇒ **跳过开局物资与商人起步白银**（ApplySave 会把存档物资灌回来；不跳会在其上再叠一份，
        //          见 ApplySave 的调用前提），并在 _Ready 末尾走 StartFromColdLoad 而非 StartFirstDay。
        // 读失败 / 无请求（主菜单点「新开局」⇒ 不设槽）⇒ _coldLoadData 为 null，照常新开局，路径逐字节不变。
        _coldLoadData = TakeColdLoadRequest();
        bool coldLoad = _coldLoadData is not null;

        if (!coldLoad)
        {
            // storage 容器（住宅柜子）的开局藏物：食物入 _resources.Food、书/武器/护甲入共享库存、材料入库存、工具装工作台。
            ApplyStorageInitialStock();

            // 神秘商人：给一点起步白银（draft，让交易开箱可跑；正式掉落来源/经济量级待用户设计）。
            if (MerchantStartingCurrency > 0)
            {
                string coinName = Materials.Find(Materials.CurrencyKey)?.DisplayName ?? "白银";
                _inventory.Add(Item.Material(Materials.CurrencyKey, coinName, Silver.FromWhole(MerchantStartingCurrency))); // 整银→分（[SPEC-B14-补6]）
            }
        }
        // 初始化来访调度：首访按 Wiki 配置排程；随机走 SystemRandomSource（生产随机源）。
        // 冷启动读档时这只是过渡值——ApplySave 里的 MerchantSchedule.Restore 会把它按存档覆盖掉。
        _merchantSchedule = new MerchantSchedule(new SystemRandomSource(), _clock.Day);

        // [wiki-character-sync] 神秘商人货架初始化：含互斥刷新项（损坏的狙击枪 / 《枪械维修指南》二选一）。
        // 冷启动时随机选择；读档走 RestoreMerchantShelf 覆盖（见 CampMain.Save.cs），不影响保存的选择。
        _merchantShelf = MerchantShelf.Default(new SystemRandomSource());

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

        // 冷启动读档时改灌存档（时序与 StartFirstDay 同为 deferred，走既有 ApplySave 就地覆盖路径）；否则正常开局。
        CallDeferred(_coldLoadData is not null ? nameof(StartFromColdLoad) : nameof(StartFirstDay));
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

        // 半身掩体场清空（防重入累积）。**必须在这里、在建围栏之前**——围栏(AddStructure)与 props 都会往里登记，
        // 而围栏建得比 props 早；若把 Clear 放在 props 循环前，会把刚登记好的围栏掩体一并清掉。
        _coverField.Clear();

        // 家具减速场同理清空（防重入累积）。不进 _furniture 的可跨越矮物（座位+门口沙袋垒）在 props 循环里记进 _looseTraversableRects，其余家具由
        // RebuildTraversalField() 从 _furniture 统一重建（见 CampMain.Traversal.cs）。
        _traversal.Clear();
        _looseTraversableRects.Clear();

        // 室内可用区同理清空（防重入累积）——BuildBuilding 逐座重新登记。
        _indoorAreas.Clear();

        // 尸体管家：无碰撞体、不改导航图，只维护「尸体格」占用（同格不堆叠，落点冲突自动挤到旁边）。
        // 尸体身上穿的东西**原样**成为它的战利品（CorpseLoot.Strip，零掷骰）；被回收时注销它的可搜刮点。
        _corpseYard = new CorpseYard { Name = "CorpseYard" };
        _corpseYard.Recycled += DeregisterCorpseContainer;
        AddChild(_corpseYard);

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
        // camp.json 里的一整段围栏这里**切成 FenceSegment 一格**再建 —— 见 FenceSegment。
        var fenceStyle = _cfg.fenceStyle ?? new PixelStyle { color = new[] { 0.40, 0.30, 0.19 }, jitter = 0.12 };
        foreach (RectSpec f in _cfg.fences ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(f.rect) is { } r)
            {
                // camp.json 里的**一整条围栏 = 一条边**（"南墙"）：切成的这些格共用一个 RunId，
                // 玩家日后就是对着这条边下"加固/修补"的令（按边下令、按格结算，见 FenceUpgradeLogic）。
                int runId = _nextRunId++;
                foreach (Rect2 seg in SplitFence(r))
                {
                    AddStructure(seg, fenceStyle, seed: 11, (float)_heights.fence, CellFence,
                        StructureTier.FenceBasic, blocking: true, fence: true, runId: runId);
                }
            }
        }
        foreach (RectSpec p in _cfg.gatePosts ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(p.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 13, (float)_heights.post, CellFence);
            }
        }
        // 南北大门（**真门**：默认关闭、可开关、HP=250 可砸）。玩家白天开门放探索队出去，忘了关——
        // 那就是玩家自己的事了。丧尸不会开门，只会砸（用户拍板）。
        foreach (GateSpec g in _cfg.gates ?? System.Array.Empty<GateSpec>())
        {
            if (ToRect(g.rect) is { } r)
            {
                AddGate(r, g.side ?? "");
            }
        }

        // 建筑（地基 + 墙立面 + 门 + 屋顶淡出）。
        foreach (BuildingSpec b in _cfg.buildings ?? System.Array.Empty<BuildingSpec>())
        {
            BuildBuilding(b);
        }

        // 道具（工作台等实心障碍，矮立体块）。带 role 的道具（storage/loot）另登记为可点击容器；
        // 带 cover:true 的（椅子/沙袋）建成非实心矮物并登记进半身掩体场。
        foreach (PropSpec pr in _cfg.props ?? System.Array.Empty<PropSpec>())
        {
            if (ToRect(pr.rect) is { } r)
            {
                var style = new PixelStyle { color = pr.color, jitter = pr.jitter };
                if (pr.role == "bed")
                {
                    // 床（批次21·impl-bedrest）：非实心可站点 + 可点击容器。正文在 CampMain.Bedrest.cs。
                    AddBedProp(pr, r);
                }
                else if (pr.role == "seat")
                {
                    // 座位家具：非实心可站点（照暗哨路径——不建碰撞、不挖导航洞），读者据此可寻路走上就座。
                    AddSeat(r, style);
                }
                else if (pr.role == "corpse")
                {
                    // 尸体：贴地的非实心物（不建碰撞、不挖导航洞——她躺在门口，不该把人挡在自家门外），仍可点击调查。
                    AddOccluderVisual(r, style, seed: 41, height: 8f, cell: 40f);
                    RegisterContainer(pr, r);
                }
                else if (pr.cover)
                {
                    // 半身掩体（沙袋等，cover:true 且非 seat）：**非实心矮物**——不建碰撞、不挖导航洞、不断视线
                    // ⇒ 看得见、打得到、走得过，只是远程命中后按 Wiki 配置判定无效（见下方 _coverField 登记）。
                    // **与 AddSolid 互斥**：实心物在墙层，子弹撞上就没了，掩体概率会是死代码。
                    AddOccluderVisual(r, style, seed: 19, height: CoverPropHeight, cell: 48f);
                }
                else if (pr.name != null && SalvageLogic.CanSalvageFurniture(pr.name)
                         && FurnitureTraversal.IsTraversable(pr.name))
                {
                    // [T15] **可跨越家具**（柜子/衣柜/展示柜…）：用户拍板「椅子之类的别的家具都可以跨过，
                    // 但是跨过时会按 Wiki 配置降低移动速度」⇒ 它们**不再是实心墙**。
                    // **刻意不调 AddSolid、刻意不进 _navHoles**：不建碰撞体、不挖导航洞 ⇒ 不挡移动、不改寻路。
                    // 这一改把「实心家具」这个 kill box 的后门从根上关死了（一排柜子曾经和一堵墙对寻路毫无区别）。
                    // 代价挂在减速场上（见 FurnitureTraversal / Actor.Slowdowns）。
                    // 减速场不在这儿登记：它由 RebuildTraversalField() 从 _furniture（唯一真源）统一重建，
                    // 这样**任何人**日后往 _furniture 里加家具（床/沙袋/新家具）都自动吃到减速，不必记得来这儿加一行。
                    var visuals = new List<Node2D>();
                    AddOccluderVisual(r, style, seed: 17, height: (float)_heights.prop, cell: 200f, collect: visuals);
                    _furniture[pr.name] = new FurnitureInstance { Rect = r, Body = null, Visuals = visuals };
                    RegisterContainer(pr, r);
                }
                else
                {
                    // 实心物：**作业台**（工作台/改装台/烹饪台——用户点名不可跨的固定大型设施）与**非家具道具**
                    // （草垛/收音机之类，压根不在 FurnitureBuildCost 目录里，不是"造出来的家具"）。照旧建碰撞 + 挖导航洞。
                    // 可拆的那几件额外记下碰撞体与视觉块句柄，拆完才抹得掉（见 RemoveFurniture）。
                    bool salvageable = pr.name != null && SalvageLogic.CanSalvageFurniture(pr.name);
                    var visuals = salvageable ? new List<Node2D>() : null;
                    StaticBody2D body = AddSolid(r, style, seed: 17, (float)_heights.prop, cell: 200f, visuals);
                    if (salvageable)
                    {
                        _furniture[pr.name!] = new FurnitureInstance { Rect = r, Body = body, Visuals = visuals! };
                    }
                    RegisterContainer(pr, r);
                }

                AddFormalPropVisual(pr, r);

                // [T15] 家具减速场：登记那些**不进 _furniture** 的可跨越矮物（座位 + 门口的 authored 沙袋垒）。
                // 判定归纯逻辑（FurnitureTraversal.IsLooseTraversableProp）—— **唯一登记点**，不在各分支里各加一行。
                // 进 _furniture 的那些（床/玩家垒的沙袋/柜子…）由 RebuildTraversalField() 从 _furniture 统一收，
                // **别在这儿重复登记**，否则同一件家具会被重复施加减速。
                if (FurnitureTraversal.IsLooseTraversableProp(pr.role))
                {
                    _looseTraversableRects.Add(r);
                }

                // 半身掩体登记（cover:true 的 prop，含椅子/座垫这类 seat）：远程命中后按 Wiki 配置整发判无效。
                // 实心物走不到这（上面 else 分支的 AddSolid 类不带 cover），故场里的掩体一律是打得到的非实心物。
                if (pr.cover)
                {
                    _coverField.Add(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
                }
            }
        }

        // 守卫岗位（预置点，读 camp.json guardPosts）。哨塔/屋顶=实心结构进 _navHoles（随首次烘焙生效）；暗哨=非碰撞标记。
        BuildGuardPosts();

        // 固定光源（预置点，读 camp.json lights）。火堆/油灯灌进 _campLights 供视野/遮暗按位置查询；视觉留渲染层。
        BuildCampLights();

        // 开局废墟（预置点，读 camp.json rubble）。实心瓦砾块 + 登记 RubbleField + 可点击容器，供玩家挖掘开拓。
        BuildRubble();

        // 半身掩体场接线：本关掩体已随 props 登记完毕，挂到场级 static 供一切 Actor 的承伤入口查询
        // （双向对称——玩家/幸存者/丧尸/劫掠者/狗都躲得、也都躲得过）。退场时置 null，见 _ExitTree。
        Actor.Covers = _coverField;

        // 家具减速场接线：从 _furniture + _looseTraversableRects 建出全部可跨越矮物的减速块，挂到场级 static 供**一切 Actor**
        // 的移速链查询。用户拍板「跨过家具减速」没有限定玩家 ⇒ 丧尸/劫掠者/布鲁斯跨过你的柜子一样慢
        //（双向对称，同掩体口径）。见 CampMain.Traversal.cs。
        RebuildTraversalField();
        Actor.Slowdowns = _traversal;

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
    /// 营地某点合成局部光照 L∈[0,1]（环境光与固定/移动光源取 max），供袭营敌人 <c>ConeFor</c> 消费。
    /// 敌人生成处经 <c>ConfigurePerception(localLightAt: SampleCampLight)</c> 注入——灯/火堆/持光单位附近敌人视野更远更宽。
    /// </summary>
    private float SampleCampLight(Vector2 pos)
        => VisionLogic.CombineLight(
            VisionLogic.AmbientLight(_clock.CurrentPhase, indoorsDark: false),
            _campLights.StrongestAt(pos.X, pos.Y, CurrentHandheldLights()));

    /// <summary>
    /// 本帧移动光源快照（复用 <see cref="_handheldScratch"/> 去逐次 alloc）：幸存者的手电/火把，以及
    /// 战斗态劫掠者的火把都进入 <see cref="LightField.StrongestAt(float,float,IEnumerable{PlacedLight})"/>。
    /// </summary>
    private IEnumerable<PlacedLight> CurrentHandheldLights()
    {
        _handheldScratch.Clear();
        foreach (Pawn p in _survivors)
        {
            if (p.Alive && p.HeldLight.ActiveHeld is LightProfile lp)
            {
                Vector2 pos = p.GlobalPosition;
                _handheldScratch.Add(new PlacedLight(pos.X, pos.Y, lp));
            }
        }

        foreach (Raider r in _nightRaiders)
        {
            if (r.Alive && r.ActiveTorchLight is { } torch)
            {
                _handheldScratch.Add(torch);
            }
        }
        foreach (Raider r in _tutorialRaiders)
        {
            if (r.Alive && r.ActiveTorchLight is { } torch)
            {
                _handheldScratch.Add(torch);
            }
        }
        if (_christine is { Alive: true } christine && christine.ActiveTorchLight is { } christineTorch)
        {
            _handheldScratch.Add(christineTorch);
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
    public readonly record struct SeatClaim(int Index, Vector2 Pos, SeatKind Kind = SeatKind.Standard);

    /// <summary>
    /// 就近认领一个空座并标记占用（供读书等指派活动给读者派座）：返回该座世界坐标（寻路目标）；
    /// 无空座返回 null，调用方按 Wiki 配置处理"无座"惩罚。读完/中断须 <see cref="ReleaseSeat"/> 释放。
    /// </summary>
    public SeatClaim? ClaimNearestFreeSeat(Vector2 fromPos)
    {
        int idx = _seats.ClaimNearest(fromPos.X, fromPos.Y);
        if (idx < 0)
        {
            return null;
        }
        (double x, double y) = _seats.PositionOf(idx);
        return new SeatClaim(idx, new Vector2((float)x, (float)y), _seats.KindOf(idx));
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

        // [T27] 室内可用区 = 外框**缩进墙厚**（墙本身不是室内）。这是「家具不能放到室外」的事实源。
        // 只有四面墙段是实心的（见 WallSegments），屋内地板可走 ⇒ "在屋里" = 占位整个落在这个内框里。
        _indoorAreas.Add(new Rect2(
            foot.Position + new Vector2(wt, wt), foot.Size - new Vector2(wt * 2, wt * 2)));

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

        // 门：门槛地面色带 + 两侧门柱 + **一扇真门**（碰撞/挡视线/断寻路/可开关/可撬可砸）。
        AddDoorDecor(foot, wt, wallH, b.door, b.wallColor, b.name ?? "建筑");

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

    /// <summary>
    /// 建筑的门：门槛地面色带 + 两侧门柱（纯视觉）+ <b>一扇真门</b>（门体）。
    /// <para>
    /// <b>此前这里只有装饰</b>——门就是墙上一个缺口，门柱"纯视觉，不挡路"，走过去直接穿过。
    /// 现在缺口里放的是真门体：关着时挡人 + 挡视线 + 断寻路（同一个墙层 body + 同一条导航洞），
    /// 可开关、可上锁（撬/砸），丧尸撞上它只会砸（用户拍板）。
    /// </para>
    /// <para>
    /// ⚠️ <b>营地建筑的门默认「开着」</b>（<c>camp.json</c> 的 <c>door.state</c> 缺省 = open）。这是**刻意的零回归默认**：
    /// 营内幸存者 AI 要自己寻路进屋读书/睡觉/干活，若门默认关着而 AI 不会自己开门，全营的人会卡死在门外。
    /// 开着 = 与改造前的"墙上缺口"在寻路上**完全等价**。玩家想关就关（那是新拿到的战术选择：把丧尸关在门外），
    /// 探索关的门则可以在关卡数据里默认关着/锁着。
    /// </para>
    /// </summary>
    private void AddDoorDecor(Rect2 foot, float wt, float wallH, DoorSpec? door, double[]? wallColor, string ownerName)
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

        // 门柱（矮立体块，纯视觉；深木色）。门柱是门**框**，不随门开关，也不随门被砸烂而消失。
        var postStyle = new PixelStyle { color = ScaleColor(wallColor, 0.5f), jitter = 0.05 };
        _isoLayer.AddChild(new IsoTilePanel { FootprintCart = postA, Style = postStyle, Seed = 43, Cell = 48f, Height = wallH, Facade = true, ZIndex = ZWorld });
        _isoLayer.AddChild(new IsoTilePanel { FootprintCart = postB, Style = postStyle, Seed = 47, Cell = 48f, Height = wallH, Facade = true, ZIndex = ZWorld });

        // ——门体（真门：碰撞 + 挡视线 + 断寻路 + 可开关 + 可撬可砸）——
        // 门板色比墙深一档（一眼能从墙里认出门来）。门板视觉在开门时隐藏（你看到的就是一个洞）。
        var doorStyle = new PixelStyle { color = ScaleColor(wallColor, 0.72f), jitter = 0.07 };
        AddDoor(
            gap, doorStyle, seed: 59, wallH, cell: 48f,
            tier: ParseDoorTier(d.tier),
            initial: ParseDoorState(d.state),
            lockTier: ParseLockTier(d.lockTier),
            doorName: $"{ownerName}的门",
            barrable: d.barrable);
    }

    // ---- camp.json 的门配置解析（数据驱动：哪些门锁着 / 撬锁难度 / 门体耐久皆是数据，代码只写规则）----

    /// <summary>
    /// 门的初始状态。<b>缺省 = 开着</b> —— 见 <see cref="AddDoorDecor"/> 类注的零回归理由
    /// （营内幸存者 AI 自己寻路进屋，门默认关着会把全营的人卡在门外）。
    /// </summary>
    private static DoorState ParseDoorState(string? s) => s switch
    {
        "closed" => DoorState.Closed,
        "locked" => DoorState.Locked,
        "barred" => DoorState.Barred, // 闩着（自家的门）：自己人一抬就开，外人只能砸
        _ => DoorState.Open,
    };

    /// <summary>锁的档次。缺省 = 没装锁。</summary>
    private static LockTier ParseLockTier(string? s) => s switch
    {
        "simple" => LockTier.Simple,
        "standard" => LockTier.Standard,
        "sturdy" => LockTier.Sturdy,
        _ => LockTier.None,
    };

    /// <summary>门体耐久档。缺省 = 木门（60HP，丧尸 5 爪破）。</summary>
    private static StructureTier ParseDoorTier(string? s) => s switch
    {
        "reinforced" => StructureTier.DoorReinforced,
        "metal" => StructureTier.DoorMetal,
        _ => StructureTier.DoorWood,
    };

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
    /// <summary>
    /// 建一处实心物（碰撞体 + 立体视觉 + 导航洞）。
    /// <paramref name="collect"/> 非空时把视觉块登记进去，<b>返回碰撞体</b>——两者合起来是**拆除时清场所需的全部句柄**
    /// （见 <see cref="RemoveFurniture"/>：家具拆完要把它从世界上抹掉）。不关心句柄的调用点直接忽略返回值即可。
    /// </summary>
    private StaticBody2D AddSolid(Rect2 rect, PixelStyle style, int seed, float height, float cell,
        List<Node2D>? collect = null)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        _logicLayer.AddChild(body);

        AddOccluderVisual(rect, style, seed, height, cell, collect);
        _navHoles.Add(rect);
        return body;
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

    /// <summary>
    /// 给 camp.json 中可辨识的 authored 道具叠加正式图集外观。底下既有块体仍保留并独占所有玩法职责，
    /// 因而替换素材不会改变碰撞、导航、掩体、拆除或容器判定。
    /// </summary>
    private void AddFormalPropVisual(PropSpec prop, Rect2 rect)
    {
        string name = prop.name ?? string.Empty;
        int index = prop.role switch
        {
            "workbench" => 0,
            "radio" => 1,
            "storage" => 2,
            "seat" when name.Contains("座垫", StringComparison.Ordinal) => 7,
            "seat" => 6,
            "cover" => 8,
            "bed" => 9,
            _ when name.Contains("衣柜", StringComparison.Ordinal) => 3,
            _ when name.Contains("展示柜", StringComparison.Ordinal) => 4,
            _ when name.Contains("草垛", StringComparison.Ordinal) => 5,
            _ => -1,
        };
        if (index < 0)
            return;

        var visual = new IsoPropSprite
        {
            FootprintCart = rect,
            AtlasIndex = index,
            ZIndex = ZWorld,
        };
        _isoLayer.AddChild(visual);

        // 可拆家具必须把正式贴图纳入同一份 Visuals 句柄账本，否则拆除后会留下幽灵外观。
        if (name.Length > 0 && _furniture.TryGetValue(name, out FurnitureInstance? furniture))
            furniture.Visuals.Add(visual);
    }

    // ---------------- 可破坏结构（围栏/大门）：HP + 摧毁开口 ----------------

    /// <summary>
    /// 一格围栏的长度（像素）。<b>拟定待调。</b>
    /// <para>
    /// 分格尺寸决定<b>破防开出来的洞有多大</b>与<b>一格墙前的攻击名额</b>；当前值以 Wiki/场景配置为准。
    /// </para>
    /// </summary>
    private const float FenceSegment = 100f;

    /// <summary>
    /// 把 camp.json 里的一整条围栏切成一格一格（每格一处独立结构：独立 HP、独立攻击位名额、独立缺口）。
    /// <para>
    /// <b>为什么必须切</b>：整段围栏若共用一处血量，破坏会一次性抹掉过长墙体，升级围墙失去意义。
    /// 切成格后每格独立承伤、独立开口，具体尺寸与血量以 Wiki/场景配置为准。
    /// </para>
    /// <para>
    /// 沿长边等分成 <c>ceil(长边 / FenceSegment)</c> 格（余数摊进各格，不留碎边）。墙厚方向不切。
    /// </para>
    /// </summary>
    private static List<Rect2> SplitFence(Rect2 r)
    {
        var segs = new List<Rect2>();
        bool horizontal = r.Size.X >= r.Size.Y;
        float length = horizontal ? r.Size.X : r.Size.Y;
        int n = System.Math.Max(1, (int)System.Math.Ceiling(length / FenceSegment));
        float step = length / n;

        for (int i = 0; i < n; i++)
        {
            float off = i * step;
            segs.Add(horizontal
                ? new Rect2(r.Position.X + off, r.Position.Y, step, r.Size.Y)
                : new Rect2(r.Position.X, r.Position.Y + off, r.Size.X, step));
        }
        return segs;
    }

    /// <summary>
    /// 加一处可破坏结构（围栏）：与 <see cref="AddSolid"/> 同构建实心碰撞 + 切块立体视觉 + 导航洞，
    /// 但把碰撞体/视觉块/rect 记进 <see cref="CampStructureInstance"/>（含 HP 状态），供 HP→0 摧毁时逐一清场并重烘焙开口。
    /// </summary>
    /// <param name="fence">
    /// true = <b>围栏</b>（网格状，用户口径「围栏中间有网格空洞」）：碰撞体落在**层5 围栏层**而非墙层3
    /// ⇒ <b>仍挡移动</b>（Actor 的 mask 含层5）、<b>仍挖导航洞</b>，但 <see cref="VisionOcclusion"/> 与
    /// <c>Projectile</c> 的 mask 都不含层5 ⇒ <b>看得穿、射得穿</b>。同时登记为**半身掩体**（贴着它的双方
    /// 各按 Wiki 配置享远程无伤概率）且**阻断近战**（不许隔栏捅）。
    /// false = <b>大门</b>：实心，留在墙层3，照常挡视线挡弹道，也不是掩体。
    /// </param>
    private CampStructureInstance AddStructure(Rect2 rect, PixelStyle style, int seed, float height, float cell,
        StructureTier tier, bool blocking, bool fence = false, int runId = -1)
    {
        var inst = new CampStructureInstance
        {
            State = new CampStructureState(tier),
            Rect = rect,
            Blocking = blocking,
            IsFence = fence,
            RunId = runId,
            Style = style,
            Seed = seed,
            Height = height,
            Cell = cell,
        };

        if (blocking)
        {
            var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
            // 围栏 → 层5（挡移动，但不挡视线/弹道）；大门等实心结构 → 层3 墙（挡一切）。
            body.CollisionLayer = fence ? 0b1_0000u : 0b0100u;
            body.CollisionMask = 0;
            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
            _logicLayer.AddChild(body);
            inst.Body = body;
            _navHoles.Add(rect);
        }

        // 围栏 = 半身掩体：贴着它的**双方**都按 Wiki 配置获得远程无效概率（你隔着网射它，它隔着网啃你，
        // 中间那层网谁都占不到便宜），且**隔着它不能近战**（blocksMelee）。摧毁时须 RemoveRect，见 DestroyStructure。
        if (fence)
        {
            _coverField.Add(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y,
                CoverLogic.DefaultCoverChance, blocksMelee: true);
        }

        AddOccluderVisual(rect, TierStyle(tier, style), seed, height, cell, inst.Visuals);
        _structures.Add(inst);
        return inst;
    }

    /// <summary>
    /// 档次 → 这一格<b>长什么样</b>（升级后墙必须<b>看得出来变了</b>：木头 → 加了支柱 → 钉了铁皮 → 全金属）。
    /// 一次看不出差别的升级，玩家会怀疑自己那 64 木料是不是白花了。
    /// </summary>
    private static PixelStyle TierStyle(StructureTier tier, PixelStyle fallback) => tier switch
    {
        StructureTier.FenceReinforced => new PixelStyle { color = new[] { 0.33, 0.24, 0.15 }, jitter = 0.12 }, // 深一号的木头
        StructureTier.FenceSheetMetal => new PixelStyle { color = new[] { 0.45, 0.44, 0.42 }, jitter = 0.10 }, // 灰铁皮
        StructureTier.FenceFullMetal  => new PixelStyle { color = new[] { 0.55, 0.56, 0.58 }, jitter = 0.07 }, // 冷金属
        StructureTier.GateSheetMetal  => new PixelStyle { color = new[] { 0.42, 0.40, 0.36 }, jitter = 0.09 },
        StructureTier.GateCastMetal   => new PixelStyle { color = new[] { 0.52, 0.53, 0.55 }, jitter = 0.06 },
        _ => fallback,
    };

    /// <summary>
    /// 加一扇大门（可破坏结构，HP=基础大门 250）：**默认关闭**——和围栏一样建成实心屏障
    /// （StaticBody2D 碰撞 + 挖导航洞 + 切块立体视觉），纳入 <see cref="CampStructureInstance"/> 体系，
    /// 可被攻击摧毁（HP→0 移碰撞 + 重烘焙开出缺口，敌人穿破口涌入）。门下先铺一条门槛地面色带作视觉区分。
    /// 门控（花料/交互开关门）机制后续做。
    /// </summary>
    private void AddGate(Rect2 rect, string side)
    {
        // 门槛地面色带（纯视觉，铺在门体之下）。
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

        // 门体：**默认闩上的真门**（可开关 + 可砸）。
        // 结构 Kind 仍是 Gate（保住它自己那条升级阶梯：基础 250 / 铁皮 400 / 浇筑 800），
        // "能不能被人开关"是**正交**的一维（DoorState），不夺走它的 Gate 身份。
        //
        // ⚠️ **为什么必须是「闩上」而不是「关上」**（用户拍板「要，能闩上」；这里堵的是一个真实存在过的洞）：
        // 大门先前是「关着 + 没锁」，而**劫掠者会开门** ⇒ 判定链「关着 + 没锁 + 够得着 → 推开」**三条全中**
        // ⇒ **劫掠者大摇大摆推门进营，250HP 完全不存在**。旧注释还写着"自家大门不上锁——靠'关着'和 250HP 说话"，
        // 但**对会拧门把手的敌人来说，「关着」就等于「没关」**。
        // 闩上之后：劫掠者推不开、也撬不了（横木在内侧，不是锁芯）⇒ **只能砸** ⇒ 250HP 重新说话，
        // 而砸门声 180（Combat，不分阵营）会把附近的丧尸一并招来咬他们。攻方的动静反噬攻方——设计意图。
        var gateStyle = new PixelStyle { color = sillColor, jitter = 0.08 };
        string name = side == "north" ? "北大门" : side == "south" ? "南大门" : "大门";
        AddDoor(
            rect, gateStyle, seed: 61, (float)_heights.post, CellFence,
            tier: StructureTier.GateBasic,
            initial: DoorState.Barred,   // 自家的门当然闩着（零回归：它此前就是默认挡路的实心结构）
            lockTier: LockTier.None,     // 闩不是锁：自己人一抬就开，不需要铁丝（见 DoorState.Barred）
            doorName: name,
            barrable: true,              // 关上即闩上——不做单独的"闩门"交互（用户偏好简化）
            // **每扇大门自成一条边**（它不切格，就是孤零零一处）⇒ 它也能加固（250 → 400 → 800）。
            // 「先大门，后围栏」这条硬顺序正是从这儿来的：一笔料砸在大门上是实打实的 +150 血，
            // 摊到 16 格的围栏上却连两格都升不满。
            runId: _nextRunId++);
        // 门槛不随门体走：门被砸烂后，地上那条门槛还在（那是地面，不是门板）。
    }

    // ---------------- 门系统（开 / 关 / 锁；规则在 DoorLogic，此处只做空间执行） ----------------

    /// <summary>
    /// 建一扇门。门 = <b>一处可破坏结构</b>（复用 <see cref="AddStructure"/> 的 HP/碰撞/视觉/导航洞整套），
    /// 外加一个可切换的 <see cref="DoorState"/>。
    /// <para>
    /// <b>门为什么直接是可破坏结构</b>：用户拍板「丧尸不会开门，只会砸」⇒ 关着的门对丧尸<b>就是一堵墙</b>。
    /// 纳入结构体系后，丧尸砸门<b>不需要一行新 AI 代码</b>——<see cref="BreachController"/> 可达性探测失败 →
    /// <see cref="BreachLogic"/> 择最近结构（门就在里头）→ 砸 → HP 归零 → <see cref="DestroyStructure"/> 移碰撞
    /// + 重烘焙开缺口 → 涌入。整条链路原样复用。
    /// </para>
    /// <para>
    /// 门体碰撞用 <b>常驻 Body + 切 CollisionLayer</b> 开关（不 QueueFree/重建）：开门 = 层置 0（射线/角色都碰不到它，
    /// 于是<b>不挡人、不挡视线</b>），关门 = 层置回墙层 0b0100。比销毁重建省事，也不会在开关瞬间丢引用。
    /// </para>
    /// </summary>
    private CampStructureInstance AddDoor(
        Rect2 rect, PixelStyle style, int seed, float height, float cell,
        StructureTier tier, DoorState initial, LockTier lockTier, string doorName, bool barrable = false,
        int runId = -1)
    {
        // 先按"关着"建（实心 + 导航洞），再按初始态开/关——这样只有一条建造路径，开着的门也是同一个 Body。
        CampStructureInstance inst = AddStructure(rect, style, seed, height, cell, tier, blocking: true, runId: runId);
        inst.Door = DoorState.Closed;
        inst.Lock = lockTier;
        inst.DoorName = doorName;
        inst.Barrable = barrable;

        // 门可点击：登记进容器体系 ⇒ 右键前往 / 悬停提示 / 到达执行**整套玩家交互白捡**（见 ExecuteContainerInteract 的 "door" 分支）。
        _containers.Add(new ContainerRef { Name = doorName, Rect = rect, Role = "door" });

        if (initial != DoorState.Closed)
        {
            SetDoorState(inst, initial, silent: true); // 建图期：不发噪音、不重烘焙（BuildNavigation 尚未跑）
        }
        return inst;
    }

    /// <summary>
    /// 切换门的状态（<b>唯一</b>的门状态写入口）。<paramref name="silent"/>=true 用于建图期（不发噪音、不重烘焙）。
    /// <para>
    /// ⚠️ <b>nav region 同步滞后（本仓已知隐坑）怎么绕的</b>：<see cref="RebakeNavigation"/> 之后，
    /// NavigationServer 的地图要到**下一次服务器同步**才生效——这一帧内 <see cref="Actor.CanReach"/> 拿到的还是旧网格。
    /// 后果会很难看：劫掠者开了门，却在同一帧被告知"还是走不通"，于是转身开始砸这扇它刚打开的门。
    /// <b>绕法见 <see cref="DoorNavSyncGraceMs"/></b>（认下这一帧，用宽限期让破防 AI 停手；
    /// <b>不</b>用已废弃的 <c>NavigationServer2D.MapForceUpdate</c>）。
    /// </para>
    /// </summary>
    private void SetDoorState(CampStructureInstance door, DoorState next, bool silent = false)
    {
        if (!door.IsDoor || door.Removed || door.Door == next)
        {
            return;
        }

        bool wasBlocking = DoorLogic.Blocks(door.Door!.Value);
        bool nowBlocking = DoorLogic.Blocks(next);
        door.Door = next;

        if (wasBlocking == nowBlocking)
        {
            return; // 关 ↔ 锁：阻挡不变（都挡），只是能不能推开变了。无需碰碰撞/导航。
        }

        SetDoorBlocking(door, nowBlocking, silent);
        if (!silent)
            CombatVfxBurst.SpawnDoor(_isoLayer, door.Rect.GetCenter(), opening: !nowBlocking);
    }

    /// <summary>
    /// 门的「挡 / 不挡」实际落地：碰撞层 + 导航洞 + 门板视觉，三样一起切。
    /// 见 <see cref="CampStructureInstance"/> 类注：这三样在本仓由同一对东西承载，故**挡人/挡视线/断寻路是同一个开关**。
    /// </summary>
    private void SetDoorBlocking(CampStructureInstance door, bool blocking, bool silent)
    {
        door.Blocking = blocking;
        _breachCandidatesDirty = true; // 门开/关 → 进出破防候选池（敞开的门没人砸）

        // ① 碰撞 + 视线：切墙层。置 0 = 角色穿得过去，且 VisionOcclusion 的 raycast（mask=0b0100）打不到它 ⇒ 不挡视线。
        if (door.Body != null && IsInstanceValid(door.Body))
        {
            door.Body.CollisionLayer = blocking ? WallCollisionLayer : 0u;
        }

        // ② 门板视觉：开着就把门板藏起来（你看到的是一个洞）。门槛/门柱是另外的节点，不动。
        foreach (Node2D v in door.Visuals)
        {
            if (IsInstanceValid(v))
            {
                v.Visible = blocking;
            }
        }

        // ③ 寻路：增删导航洞并重烘焙。
        if (blocking)
        {
            if (!_navHoles.Contains(door.Rect)) _navHoles.Add(door.Rect);
        }
        else
        {
            _navHoles.Remove(door.Rect); // Rect2 值相等
        }

        if (silent)
        {
            return; // 建图期：BuildNavigation 还没跑，等它一次性烘焙即可
        }

        RebakeNavigation();
        _doorNavDirtyUntil = Time.GetTicksMsec() + DoorNavSyncGraceMs; // ⚠️ nav 同步滞后宽限，见下
    }

    /// <summary>
    /// 开关门后「导航还没同步完」的宽限期（毫秒）。
    /// <para>
    /// ⚠️ <b>本仓已知隐坑：nav region 同步滞后</b>。<see cref="RebakeNavigation"/> 只是换掉
    /// <see cref="NavigationRegion2D.NavigationPolygon"/>，NavigationServer 要到**下一次服务器同步**才让新网格生效——
    /// 这中间 <see cref="Actor.CanReach"/> 拿到的仍是旧网格。
    /// </para>
    /// <para>
    /// <b>为什么不用 <c>NavigationServer2D.MapForceUpdate</c> 强制同步</b>：Godot 4.7 已把它标记为
    /// <b>obsolete</b>（"incompatible with asynchronous updates, single-threaded context only, at your own risk"）——
    /// 用它既破 0W0E 门禁，也是在跟引擎的异步导航对着干。
    /// </para>
    /// <para>
    /// <b>正解是认下这一帧、把唯一真会出错的场景挡住</b>：唯一的失败模式是
    /// <b>「劫掠者刚开了门，同一帧的可达性探测却说还是不通，于是它转身开始砸这扇自己刚打开的门」</b>。
    /// 故门一开关就打上这个时间戳，<see cref="ShouldSuppressBreach"/> 在宽限期内让**所有**破防 AI 停手——
    /// 等导航同步完再判。玩家侧不需要任何处理：从"推开门"到"下达移动指令"至少隔着一次输入事件，早就同步完了。
    /// </para>
    /// </summary>
    private const ulong DoorNavSyncGraceMs = 250;

    private ulong _doorNavDirtyUntil;

    /// <summary>
    /// 破防 AI 该不该停手：刚有门开关过、导航尚未同步完 ⇒ 是（见 <see cref="DoorNavSyncGraceMs"/>）。
    /// 注入给 <see cref="BreachController"/>，防"敌人砸一扇已经敞开的门"。
    /// </summary>
    private bool ShouldSuppressBreach() => Time.GetTicksMsec() < _doorNavDirtyUntil;

    /// <summary>门口有没有人站着（关门前必查）：谁站在门缝里，门就关不上——否则会把他实心夹在门板里。</summary>
    private bool IsDoorwayOccupied(CampStructureInstance door)
    {
        Rect2 span = door.Rect.Grow(10f);
        foreach (Actor a in _actorLayer.GetChildren().OfType<Actor>())
        {
            if (a.Alive && span.HasPoint(a.GlobalPosition))
            {
                return true;
            }
        }
        return false;
    }



    /// <summary>就近取一扇门（供玩家交互 / 劫掠者开门 AI 共用）：落点在门 rect（外扩 <paramref name="pad"/>）内即命中。</summary>
    private CampStructureInstance? DoorAt(Vector2 cart, float pad = 8f)
    {
        foreach (CampStructureInstance s in _structures)
        {
            if (s.IsDoor && !s.Removed && s.Rect.Grow(pad).HasPoint(cart))
            {
                return s;
            }
        }
        return null;
    }

    // ---- 劫掠者开门 AI 的两个注入委托（丧尸不注入这两个：它不会开门，只会砸）----

    /// <summary>
    /// 找 <paramref name="from"/> 附近最近的**能开的门**（关着 + 没锁 + 未毁）。给 <see cref="Raider"/> 用。
    /// <para>
    /// <b>锁着的门不在候选里</b> —— 那样劫掠者就会走过去干瞪眼。开不了的门交给破防去砸（它不在乎吵，
    /// 而且砸门声 180 还会把附近的丧尸招来，攻方的动静反噬攻方）。<b>不给 AI 做撬锁</b>：撬锁是**玩家的**
    /// 取舍工具（拿寂静换时间），劫掠者是来抢东西的，没这个耐心。
    /// </para>
    /// </summary>
    // ──────────────── 劫掠者的安静入侵（撬锁 / 轻声拆围栏）────────────────
    // 用户口径：「劫掠者会花一段时间撬锁，或者轻声拆除围栏进入，以避免玩家不派任何岗哨只等着敌人砸门。」
    // ⚠️ 反退化设计：只会砸门的劫掠者 = 会自己敲锣打鼓通知你 ⇒ 玩家的最优解变成「根本不派守夜人」。
    //    有了安静入侵，你不派人看着，他们就无声无息地进来了。判定全在纯逻辑 IntrusionLogic，这里只做空间执行。

    /// <summary>
    /// 找最近的<b>可安静入侵</b>目标：锁着的门（可撬）或一格围栏（可轻声拆）。
    /// <b>门优先</b>（撬锁快得多——劫掠者的暴露窗口更小，这是"带工具 = 有优势"的正确表达）。
    /// </summary>
    private bool TryFindQuietTarget(
        Vector2 from, float radius,
        out Vector2 point, out bool isDoor, out LockTier lockTier, out StructureTier fenceTier)
    {
        point = from;
        isDoor = false;
        lockTier = LockTier.None;
        fenceTier = StructureTier.FenceBasic;

        CampStructureInstance? bestDoor = null, bestFence = null;
        float doorDist = radius, fenceDist = radius;

        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed)
            {
                continue;
            }
            (double ex, double ey) = BreachLogic.NearestPointOnRect(
                from.X, from.Y, s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y);
            float d = (float)BreachLogic.Distance(from.X, from.Y, ex, ey);

            if (s.IsDoor)
            {
                // 只要**锁着**的门（没锁的门 TryOpenDoor 已经推开了，轮不到撬）。
                if (s.Door == DoorState.Locked && s.Lock != LockTier.None && d < doorDist)
                {
                    doorDist = d;
                    bestDoor = s;
                }
            }
            else if (s.Blocking && s.State.Kind == CampStructureKind.Fence && d < fenceDist)
            {
                fenceDist = d;
                bestFence = s;
            }
        }

        if (bestDoor is not null)
        {
            point = bestDoor.Rect.GetCenter();
            isDoor = true;
            lockTier = bestDoor.Lock;
            return true;
        }
        if (bestFence is not null)
        {
            point = bestFence.Rect.GetCenter();
            isDoor = false;
            fenceTier = bestFence.State.Tier;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 劫掠者撬一次锁：<b>规则走 <see cref="DoorLogic.TryPick"/>，与玩家撬锁同一处判定</b>
    /// （不给 AI 另开一套成功率）。撬开 → 解锁并推门；撬断 → 返回 false（Raider 自己扣一根铁丝、记一次失败）。
    /// </summary>
    private bool PickDoorLockByAi(Vector2 doorPos, Actor picker)
    {
        CampStructureInstance? door = DoorAt(doorPos, pad: 16f);
        if (door is null || !door.IsDoor || door.Door != DoorState.Locked || !picker.CanOperateDoors)
        {
            return false;
        }

        DoorPickAttempt attempt = DoorLogic.TryPick(door.Lock, _combat.Rng);
        if (!attempt.Success)
        {
            return false; // 撬断了。他刚才在门口蹲的那几秒——你本来有机会看见他。
        }

        door.Lock = LockTier.None;
        door.Door = DoorState.Closed;
        OpenDoorByAi(doorPos, picker); // 锁开了，推门（这一下才是 100 的开门声）
        GD.Print($"[劫掠者] 悄悄撬开了{door.DoorName}的锁。");
        return true;
    }

    /// <summary>
    /// 劫掠者<b>轻声拆掉一格围栏</b>：作业已经在 Raider 那边耗满了 Wiki 配置时长，这里只做落地——
    /// 对那一格施加<b>整格满血</b>的伤害 ⇒ 走既有摧毁链路 ⇒ 一个货真价实的洞。
    /// 不另造"洞"的概念（围栏分格复用 FenceSegment）。
    /// <para>
    /// ⚠️ 洞是<b>永久</b>的：玩家要补，得走<b>升级围栏</b>那条路（用户拍板"墙不能建，只能升级开局自带的围栏"）。
    /// </para>
    /// </summary>
    private bool DismantleFenceByAi(Vector2 point, Actor who)
    {
        CampStructureInstance? fence = NearestStructure(point, radius: 48f);
        if (fence is null || fence.Removed || fence.IsDoor || fence.State.Kind != CampStructureKind.Fence)
        {
            return false;
        }
        // 走**通用**静默拆除 API（SilentDismantleLogic）——玩家右键"静默拆除"走的是同一处，
        // 所以玩家和劫掠者拆同一格围栏的结果逐字相同。AI 不许在这儿另算。
        if (!SilentDismantleLogic.CanDismantle(fence.State.Kind, fence.Removed))
        {
            return false;
        }
        fence.State.TakeDamage(SilentDismantleLogic.DamageFor(fence.State.Tier));
        if (fence.State.IsDestroyed)
        {
            DestroyStructure(fence);
        }
        return true;
    }

    /// <summary>
    /// 天亮前还剩多少<b>实时</b>秒（劫掠者据此判断"还来不来得及慢慢撬/慢慢拆"）。
    /// 不在夜里（教学关/超市伏击等白天场景）→ 给一个大值，<b>不构成时间压力</b>（那些场景本就不该逼他砸门）。
    /// </summary>
    private double SecondsUntilDawnForRaiders()
    {
        double left = _clock?.GetNightTimeRemaining() ?? 0;
        return left > 0 ? left : 600.0;
    }

    private bool TryFindOpenableDoor(Vector2 from, float radius, out Vector2 doorPos)
    {
        doorPos = from;
        float best = radius;
        foreach (CampStructureInstance s in _structures)
        {
            // 只挑"关着且没锁"的：DoorLogic.CanOpen 一处判定，规则不散写。
            if (s.Removed || !s.IsDoor ||
                !DoorLogic.CanOpen(s.Door!.Value, Faction.Raider, isAnimal: false))
            {
                continue;
            }
            (double ex, double ey) = BreachLogic.NearestPointOnRect(
                from.X, from.Y, s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y);
            float d = (float)BreachLogic.Distance(from.X, from.Y, ex, ey);
            if (d < best)
            {
                best = d;
                doorPos = s.Rect.GetCenter();
            }
        }
        return best < radius;
    }

    /// <summary>AI 推门（劫掠者）：切碰撞/导航 + 发开门噪音（100 —— 附近闲逛的丧尸听得见它进屋）。</summary>
    private bool OpenDoorByAi(Vector2 doorPos, Actor opener)
    {
        CampStructureInstance? door = DoorAt(doorPos, pad: 16f);
        if (door is null || !opener.CanOperateDoors ||
            !DoorLogic.CanOpen(door.Door!.Value, opener.Faction, isAnimal: false))
        {
            return false;
        }
        SetDoorState(door, DoorState.Open);
        opener.EmitDoorNoise();
        GD.Print($"[门] {door.DoorName} 被 {opener.Faction} 推开了。");
        return true;
    }

    /// <summary>
    /// 玩家走到门前后的门交互（由 <see cref="ExecuteContainerInteract"/> 的 "door" 分支调用）。
    /// <b>开门只有一种动作</b>（用户拍板：不分轻推/踹开）—— 故这里没有任何"力度"选项，
    /// 也<b>没有单独的"闩门"动作</b>：门开着就关上（能闩的顺手就闩上了），关着/闩着就推开，锁着就撬。
    /// </summary>
    private void ExecuteDoorInteract(Pawn arriver, CampStructureInstance door)
    {
        if (door.Removed || !door.IsDoor)
        {
            return;
        }

        switch (door.Door!.Value)
        {
            case DoorState.Open:
                if (IsDoorwayOccupied(door))
                {
                    // 门缝里站着人/狗/丧尸：关不上。否则会把他实心夹进门板里。
                    _campToast.Show($"{door.DoorName}关不上——门口还站着人。", CampToast.Bad);
                    return;
                }
                // 关上即闩上（能闩的门）：一个动作。若拆成"关门"+"闩门"两步，玩家迟早会忘了闩，
                // 而"忘了闩"的后果正是这套东西要堵的洞（劫掠者推门直入）。
                SetDoorState(door, DoorLogic.ClosedRestingState(door.Barrable));
                arriver.EmitDoorNoise();
                _campToast.Show(
                    door.Barrable
                        ? $"{arriver.DisplayName} 关上并闩好了{door.DoorName}。"
                        : $"{arriver.DisplayName} 关上了{door.DoorName}。",
                    CampToast.Ok);
                break;

            case DoorState.Closed:
                SetDoorState(door, DoorState.Open);
                arriver.EmitDoorNoise();
                _campToast.Show($"{arriver.DisplayName} 推开了{door.DoorName}。", CampToast.Ok);
                break;

            case DoorState.Barred:
                // 自家的门闩：一抬就开，**不用铁丝、不用撬**（要玩家撬自家大门是荒谬的）。
                // 走的就是普通开门那一个动作 —— DoorLogic.CanOpen 已把"只有自己人抬得起"这条钉死。
                if (!DoorLogic.CanOpen(DoorState.Barred, arriver.Faction, isAnimal: false))
                {
                    return;
                }
                SetDoorState(door, DoorState.Open);
                arriver.EmitDoorNoise();
                _campToast.Show($"{arriver.DisplayName} 抬起门闩，推开了{door.DoorName}。", CampToast.Ok);
                break;

            case DoorState.Locked:
                BeginPickLock(arriver, door);
                break;
        }
    }

    /// <summary>
    /// 开撬。<b>撬锁 = 安静 + 慢 + 要铁丝</b>，具体噪音、耗时、失败消耗以 Wiki 配置为准。
    /// 没铁丝就只剩砸这一条路——而砸很响（180，把半条街招来）。这就是用户要的<b>噪音 vs 效率</b>取舍。
    /// </summary>
    private void BeginPickLock(Pawn picker, CampStructureInstance door)
    {
        int wire = _inventory.MaterialCount(DoorLogic.LockpickMaterialKey);
        if (!DoorLogic.CanPick(door.Door!.Value, picker.Faction, isAnimal: false, wire))
        {
            _campToast.Show($"{door.DoorName}锁着，而你没有铁丝——只能砸开了（很响）。", CampToast.Bad);
            return;
        }

        _picking = (picker, door, 0.0, DoorLogic.PickSeconds(door.Lock));
        _campToast.Show(
            $"{picker.DisplayName} 开始撬{door.DoorName}的锁（{LockLabel(door.Lock)}，铁丝 ×{wire}）……", CampToast.Ok);
    }

    /// <summary>
    /// 撬锁逐帧推进（<b>实时秒</b>，不是工时制分钟——撬锁是战术动作，门外那群东西不会停下来等你把工时耗完）。
    /// 撬锁者死亡/被改派/离开门口 = 中断（<b>不留进度</b>：手一抖，前功尽弃）。
    /// </summary>
    private void TickLockPick(double delta)
    {
        if (_picking is not { } p)
        {
            return;
        }

        // 中断：人没了 / 不可控 / 门没了 / 门已不锁 / 走开了。
        bool aborted =
            !p.pawn.Alive || !p.pawn.IsControllable || !_survivors.Contains(p.pawn) ||
            p.door.Removed || p.door.Door != DoorState.Locked ||
            !p.door.Rect.Grow(p.pawn.Radius + PendingArriveMargin).HasPoint(p.pawn.GlobalPosition);
        if (aborted)
        {
            _picking = null;
            return;
        }

        double elapsed = p.elapsed + delta;
        if (elapsed < p.need)
        {
            _picking = (p.pawn, p.door, elapsed, p.need);
            return;
        }

        // 一次尝试走完：出结果。噪音极轻（30）——撬锁本身惊动不了任何东西，那正是它存在的意义。
        _picking = null;
        p.pawn.EmitLockpickNoise();

        DoorPickAttempt r = DoorLogic.TryPick(p.door.Lock, _pickRng);
        if (r.Success)
        {
            SetDoorState(p.door, DoorState.Open);
            _campToast.Show($"咔哒——{p.pawn.DisplayName} 撬开了{p.door.DoorName}。", CampToast.Ok);
            GD.Print($"[门] {p.pawn.DisplayName} 撬开 {p.door.DoorName}（{LockLabel(p.door.Lock)}）。");
            return;
        }

        // 失败：铁丝断在锁里（扣 1 根），时间照样花掉。还有铁丝就自动接着撬——玩家不必一遍遍点。
        _inventory.TrySpendMaterial(DoorLogic.LockpickMaterialKey, 1);
        int left = _inventory.MaterialCount(DoorLogic.LockpickMaterialKey);
        if (left > 0)
        {
            _campToast.Show($"铁丝断在了锁里。（还剩 {left} 根，继续撬……）", CampToast.Bad);
            _picking = (p.pawn, p.door, 0.0, DoorLogic.PickSeconds(p.door.Lock));
        }
        else
        {
            _campToast.Show($"最后一根铁丝也断了。{p.door.DoorName}还锁着——要么找铁丝，要么砸。", CampToast.Bad);
        }
    }

    /// <summary>锁的档次 → 玩家可见文案。</summary>
    private static string LockLabel(LockTier tier) => tier switch
    {
        LockTier.Simple => "简单锁",
        LockTier.Standard => "普通锁",
        LockTier.Sturdy => "坚固锁",
        _ => "没上锁",
    };

    /// <summary>门的悬停提示：门名 + 当前状态 + 该右键干什么。</summary>
    private string DoorHoverText(CampStructureInstance door, bool hasSelection)
    {
        string noSel = hasSelection ? "" : "（先选中角色）";
        string stateHint = door.Door switch
        {
            DoorState.Open => door.Barrable
                ? $"（**开着**——外人可长驱直入）· 右键前往关门闩上{noSel}"
                : $"（开着）· 右键前往关门{noSel}",
            DoorState.Closed => $"（关着，没闩）· 右键前往开门{noSel}",
            DoorState.Barred => $"（闩着）· 右键前往抬闩开门 —— 劫掠者推不开，只能砸{noSel}",
            DoorState.Locked =>
                $"（{LockLabel(door.Lock)}）· 右键前往撬锁 —— 安静但慢；" +
                $"铁丝 ×{_inventory.MaterialCount(DoorLogic.LockpickMaterialKey)}{noSel}",
            _ => door.DoorName,
        };
        return $"{CampHoverText.Structure(door.DoorName, door.State.Tier, door.State.Hp, door.Removed)} · {stateHint}";
    }

    /// <summary>
    /// 结构攻击接口（供后续袭营/守卫块让敌人打围栏/大门）：对某结构施加伤害，HP→0 摧毁并开口。
    /// 返回该击是否摧毁了结构。敌人主动攻击结构的 AI 属袭营块（后续），本块只提供承伤入口 + 摧毁开口实现。
    /// </summary>
    private bool DamageStructure(CampStructureInstance s, double amount)
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

        // 围栏被啃穿 → 这段的**半身掩体也要跟着没**：否则墙没了，掩体判定还留在原地，
        // 玩家会对着一个空洞白享掩体，隔着缺口还捅不到人。
        if (s.IsFence)
        {
            _coverField.RemoveRect(s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y);
        }

        // 攻击位：这处砸穿了 → 占着它的全部松开（下一帧改走缺口），候选表置脏重建。
        _breachSlots.ReleaseTarget(_structures.IndexOf(s));
        _breachCandidatesDirty = true;

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
    /// 破防候选表（下标 = <see cref="_structures"/> 下标，作攻击位账本的稳定 Id）：未毁且挡路的结构 + 各自的攻击位名额。
    /// 结构被砸穿 / 门被开关时置脏重建（见 <see cref="_breachCandidatesDirty"/>）——被阻隔的敌人每帧都要问一次，不能每次重扫全表。
    /// </summary>
    private List<BreachCandidate> BreachCandidates()
    {
        if (!_breachCandidatesDirty)
        {
            return _breachCandidates;
        }
        _breachCandidatesDirty = false;
        _breachCandidates.Clear();
        for (int i = 0; i < _structures.Count; i++)
        {
            CampStructureInstance s = _structures[i];
            if (s.Removed || !s.Blocking)
            {
                continue; // 敞开的门不挡路，没人砸（见 NearestStructureByEdge 同款过滤）
            }
            _breachCandidates.Add(new BreachCandidate(
                i, s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y,
                BreachSlots.Capacity(s.Rect.Size.X, s.Rect.Size.Y, BreachSlots.DefaultFootprint)));
        }
        return _breachCandidates;
    }

    /// <summary>
    /// 破防 AI 用：为该敌人**认领一个攻击位**——在 <paramref name="radius"/> 内按边缘距离挑一处**还站得下人**的未毁结构，
    /// 占位并输出其最近边缘点。半径内全满则 false（它挤不上，在后面顶着，<b>不绕路</b>）。
    /// <para>
    /// 用边缘距离而非中心距离：长围栏中心可能很远、但其边缘就在敌人脸上（<see cref="BreachLogic"/>）。
    /// 名额与择目标是纯逻辑（<see cref="BreachSlots"/>，可单测）；此处只做 Rect2/Vector2 适配，结构类型不外泄。
    /// </para>
    /// </summary>
    private bool TryFindBreachTarget(ulong attackerId, Vector2 from, float radius, out Vector2 edgePoint, out float edgeDistance)
    {
        edgePoint = from;
        edgeDistance = float.PositiveInfinity;

        int id = BreachSlots.ChooseTarget(
            from.X, from.Y, BreachCandidates(), _breachSlots, attackerId, radius,
            out double dist, out double ex, out double ey);
        if (id < 0)
        {
            return false;
        }
        edgePoint = new Vector2((float)ex, (float)ey);
        edgeDistance = (float)dist;
        return true;
    }

    /// <summary>
    /// 破防 AI 用：对该敌人**当前占着的那处结构**施伤，返回是否本击摧毁。
    /// <para>
    /// 打的是<b>认领的</b>那处、不是"脚下最近的"那处：被挤到围栏边的丧尸可能同时够到其他结构——
    /// 若按最近算，它的伤害会记到门上，攻击位名额就白设了（受攻击面又缩回两扇门）。
    /// </para>
    /// </summary>
    private bool DamageNearestStructureAt(ulong attackerId, Vector2 from, float radius, double amount)
    {
        if (_breachSlots.HeldBy(attackerId) is not int id || id < 0 || id >= _structures.Count)
        {
            return false; // 没占位就不该在砸（TryBreach 只在认领成功后才落到这里）
        }
        return DamageStructure(_structures[id], amount);
    }

    /// <summary>破防 AI 用：放开该敌人占着的攻击位（墙破了转常规追击 / 停手 / 死亡）——后面挤着的那只顶上来。</summary>
    private void ReleaseBreachSlotFor(ulong attackerId) => _breachSlots.Release(attackerId);

    /// <summary>
    /// 按边缘距离取最近未毁结构 + 其最近边缘点（供破防择目标/施伤共用）。
    /// <para>
    /// ⚠️ <b>跳过不阻挡的结构（＝敞开的门）</b>：否则袭营 AI 会挑中一扇**开着的门**去砸——
    /// 敌人站在洞开的门口一下一下砸门框，而门洞就在旁边。<see cref="DoorLogic.CanBash"/> 恒等于
    /// <see cref="DoorLogic.Blocks"/> 正是为此。（此前全仓结构皆 <c>Blocking=true</c>，本过滤对围栏/关着的门零影响。）
    /// </para>
    /// </summary>
    private CampStructureInstance? NearestStructureByEdge(Vector2 from, float radius, out float bestDist, out Vector2 bestEdge)
    {
        CampStructureInstance? best = null;
        bestDist = radius;
        bestEdge = from;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed || !s.Blocking)
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
        GD.Print($"[结构] 调试打击 {s.State.Kind} -50 → HP {s.State.Hp:F1}/{s.State.MaxHp}" +
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
            // 外扩保持导航可通行；门缝宽度与导航参数由场景配置维护。
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
            // 起始武器由 camp.json 的 weapon 字段（无/pistol/dagger/club）解析（旧的 pistol 布尔撑不起 authored 的"空手/棍棒"）。
            var p = Pawn.Create(s.name ?? "幸存者", StartingWeaponInfo.FromKey(s.weapon), color);
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
        // 山姆·英雄风范 1 级"比常人耐揍"：**护甲后**乘算减伤（仅山姆，其余角色不注入 → 恒为零，引擎零回归；数值见 Wiki）。
        // 注入的是 lambda 而非定值：等级每次命中时现算，故他一死（SamLevelNow→0）减伤当场消失，无需在死亡路径上手工清。
        // 与道格 3 级的 SetIncomingDamageFactor 分属两层：那个是护甲**前**缩放武器伤害，这个是护甲**后**乘剩余伤害。
        if (actor is Pawn { Perks.IsSam: true } sam)
        {
            sam.SetIncomingDamageReduction(() => SamPerk.IncomingDamageReduction(SamLevelNow(), isSam: true));
            // 山姆·英雄风范 3 级：大流血降为中流血，震荡概率按 authored 乘子降低。
            // 骨折两条惩罚链（上肢操作、下肢移动）按 Wiki 配置的“惩罚缺口减轻”由 Actor 消费。
            sam.SetConcussionChanceMultiplier(() =>
                SamLevelNow() >= 3 ? 1.0 - SamPerk.Level3ConcussionReduction : 1.0);
            sam.SetLargeBleedDowngradeProvider(() => SamLevelNow() >= 3);
            sam.SetFracturePenaltyReductionProvider(() =>
                SamPerk.FracturePenaltyReduction(SamLevelNow(), isSam: true));
        }
        // 皮特·青春期田径队：移速乘子与闪避效果由 Wiki 配置提供。
        // 都注入读实时等级的 lambda（同山姆减伤口径）：升级即时生效、无缓存可失效；非皮特不注入 → 保持零回归。
        if (actor is Pawn { Perks.IsPete: true } pete)
        {
            pete.SetAuthoredMoveSpeedMult(() => PetePerk.MoveSpeedMultiplier(PeteLevelNow(), isPete: true));
            pete.SetPeteLevelProvider(PeteLevelNow);
        }
        // [T61] 耗子 L3 实时潜行/破隐先手：Pawn 只持有身份，等级由营地 StoryFlags 每次查询派生。
        // 统一挂在 AddActor 漏斗，开局/救援/招募/读档恢复的耗子都不会漏；非耗子不注入，按 Pawn 的 L1 fallback。
        if (actor is Pawn { Perks.IsRat: true } rat)
        {
            rat.SetRatLevelProvider(RatLevelNow);
        }
        // [T47] 消耗型改装脱落（锋刃研磨砍满三下 ⇒ 刃磨没了）：**玩家必须看得见**，不能静默失效。
        // 挂在 AddActor 这一个漏斗上 —— 幸存者不管从哪条路入营（开局/道格/克莉丝汀/护士/村庄救援）都走它。
        if (actor is Pawn moddedUser)
        {
            moddedUser.WeaponModBroken += OnWeaponModBroken;
        }
        // 弹药源（批次18）：玩家幸存者的枪吃**营地共享库存**里的弹药——没子弹就只能抡枪托。
        // 丧尸/劫掠者/布鲁斯保持默认 UnlimitedAmmoSource（它们没有库存模型，既有行为零回归；
        // 劫掠者的弹药以战利品形式回流给玩家，而不是去模拟他们的弹匣）。
        if (actor is Pawn)
        {
            actor.Ammo = AmmoSource;
        }
        actor.Died += OnActorDied;
        _actorLayer.AddChild(actor); // LogicLayer 下（不可见，仅物理/寻路）
        // 角色视觉（iso 层的人形 ActorSprite）由 B2 接管：Actor 自行经 "iso_layer" group 挂载。
    }

    /// <summary>
    /// 口袋狗衣负重接缝（③）：本次探索若带上布鲁斯，其穿戴的口袋狗衣提供的额外携带容量（无则 0）。
    /// 容量数据/查询由 dog-gear 落定（<see cref="Dog.CarryCapacity"/>＝DogApparelSlots.TotalCarryCapacity）。
    /// <para>
    /// <b>已接线</b>（[T45] 探索负重系统落地时接上）：本值经 <see cref="PartyCarryLimit"/> 喂给
    /// <c>ExpeditionBag.PartyCapacity(队员上限, 狗容量)</c> ⇒ 进 <c>_bag.SetCapacity</c>；并作为
    /// <c>dogCapacityKg</c> 喂进逐人分档的 <c>ExpeditionLoad.For</c>。⇒ 布鲁斯驮的那几 kg 真的抬高了这一趟的硬上限。
    /// </para>
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

    // ---------------- 克莉丝汀「巧舌如簧」接线辅助 ----------------

    /// <summary>
    /// 克莉丝汀升级推进：在营存活天数每昼夜 +1，仅她在营存活时累加（未招募/已亡/已离营即冻结＝停止累加）。
    /// 在 <see cref="OnGamePhaseChanged"/> 的黎明聚餐分支（每昼夜一次，紧随 <see cref="AdvanceBondDay"/>）调用。
    /// </summary>
    private void AdvanceChristineDay()
    {
        if (ChristinePawn() != null)
            _christineDaysInCamp++;
    }

    /// <summary>克莉丝汀当前等级（1/2/3），由在营存活天数 + 灭金手指帮旗标现算（<see cref="ChristinePerk.EvaluateLevel"/>）。未招募时值无意义（消费点另经在营门控）。</summary>
    private int ChristineLevelNow()
        => ChristinePerk.EvaluateLevel(_christineDaysInCamp, GoldfingerDiscovery.GangCleared(_storyFlags));

    /// <summary>克莉丝汀是否在营存活（招募后未亡故/未离营）——商人折扣/卖价加成的存活门控（[Q2] 需她在营维持）。</summary>
    private bool ChristineAliveInCamp() => ChristinePawn() != null;

    /// <summary>
    /// 3 级光环当前生效态：等级≥3、道格布鲁斯皆存活、互相距离 ≤ <see cref="DougBruceBond.DefaultAuraRadius"/> 时激活
    /// （生产/受伤乘子以 Wiki 配置为准）；否则中性。一方死亡即永失（_doug/_bruce 死时置 null）。
    /// </summary>
    private AuraEffect BondAuraNow()
    {
        if (_doug is not { Alive: true } doug || _bruce is not { Alive: true } bruce)
            return AuraEffect.Inactive;
        float dist = doug.GlobalPosition.DistanceTo(bruce.GlobalPosition);
        return DougBruceBond.AuraActive(BondLevel, bothAlive: true, dist, DougBruceBond.DefaultAuraRadius);
    }

    // ================= 山姆·"英雄风范"（authored 三级专属效果 · **可倒退**）=================
    // 与诺蒂/道格/南丁格尔的**单调**升级轴（累计阅读时长 / 共同存活天数 / 累计手术台数，只增不减）不同：
    // 山姆的等级由**当前营地人数**派生（3 人→L2、6 人→L3），人数一跌就**倒退**（用户原话）。
    // 实现上**不缓存等级**——下面几个访问器每次调用都现算，故"人死的那一刻光环就退"是天然结果，
    // 没有需要失效/回滚的缓存（可倒退机制的全部代价就是"别缓存"）。规则本体在纯逻辑 SamPerk（已单测）。

    /// <summary>营地里的山姆（未入队/已死则 null 或 Alive=false）。</summary>
    private Pawn? Sam => _survivors.FirstOrDefault(s => s.Perks.IsSam);

    /// <summary>
    /// 当前**营地人数**（山姆升级轴的输入）＝花名册里**活着的人**，含山姆本人、含当天外出探索的队员。
    /// **狗不算人**（布鲁斯不在 <c>_survivors</c>，天然不计入 —— 主 agent 裁决："布鲁斯是狗，不是营地的一个人"）。
    /// **外出探索者仍计入**：用户口径的"营地人数减少"指的是**死人/离队**（花名册变小），不是"今天有 3 个人出门了"；
    /// 若按"此刻站在营地里的人"算，探险队一出门山姆就掉级、而 L3 的负重加成恰恰是给背东西的探险队用的，自相矛盾。
    /// （要改成"在营"口径只需在此加 <c>&amp;&amp; s.Role != PawnRole.Expedition</c> 一句。）
    /// </summary>
    private int CampPopulationNow() => _survivors.Count(s => s.Alive);

    /// <summary>
    /// 山姆当前等级（0=他死了/不在队 → 一切效果含全营光环消失；1/2/3 见 <see cref="SamPerk.EvaluateLevel"/>）。
    /// **每次查询实时派生**，故人数涨落即时生效、无缓存可失效。
    /// </summary>
    private int SamLevelNow() => SamPerk.EvaluateLevel(CampPopulationNow(), samAlive: Sam is { Alive: true });

    /// <summary>
    /// 某工人当前的**有效干活效率**＝他的实际操作能力 × 山姆 3 级光环（[通则·乘算]，见 <see cref="SamPerk.OperationCapabilityWithAura"/>）。
    /// 乘进喂工时 <c>Advance</c> 的分钟数（制作 / 建造 / 挖废墟）。健全饱食者 = 1.0 × 1.0 → 既有行为零回归；
    /// 断双手者 = 0 × 1.03 = 0（干不了活，光环补不回来）。**搜刮**的工时模型尚不存在（探索点"踏入即入库"），
    /// 待其建立后同样乘本函数即可。
    /// </summary>
    private double WorkEfficiencyOf(Pawn worker)
        // 皮特操作加成（[通则·乘算]，见 <see cref="PetePerk.OperationCapabilityWithBonus"/>）：**乘在山姆光环折算后的实际操作能力上**。
        // 不 clamp 截断，挂在**唯一的干活效率漏斗** WorkEfficiencyOf；非皮特/L1 保持零回归。
        => BondOperationMultFor(worker)
            * PetePerk.OperationCapabilityWithBonus(
            SamPerk.OperationCapabilityWithAura(
                worker.OperationCapability * SamPerk.PersonalOperationCapabilityMultiplier(SamLevelNow(), worker.Perks.IsSam),
                SamLevelNow()),
            PeteLevelNow(), worker.Perks.IsPete);

    /// <summary>
    /// [T61] 耗子当前等级（1/2/3）——**每次查询实时派生**：读 <c>StoryFlags</c> 里她累计搜出的件数
    /// （<see cref="RatPerk.ScavengeCountFlag"/>）⇒ 无缓存可失效，**存档天然覆盖**（该旗标本就在 SaveData.StoryFlags 里）。
    /// </summary>
    private int RatLevelNow() => RatPerk.LevelOf(_storyFlags);

    /// <summary>
    /// 皮特当前等级（1/2/3）——**每次查询实时派生**：读 <c>StoryFlags</c> 里的 L2 latch（<see cref="PetePerk.Level2ReachedFlag"/>）
    /// + 饥饿≤5 出行累计（<see cref="PetePerk.DepartureCountFlag"/>）⇒ 无缓存可失效、**存档天然覆盖**（旗标本就在 SaveData.StoryFlags 里）。
    /// 供移速 lambda / 操作消费点 / 闪避 override 现算，故升级即时生效。
    /// </summary>
    private int PeteLevelNow() => PetePerk.LevelOf(_storyFlags);

    /// <summary>
    /// [T61] 某工人的**搜刮**效率 ＝ 通用干活效率 <see cref="WorkEfficiencyOf"/> × 耗子的搜刮专属乘子。
    /// <para>
    /// 🔴 <b>耗子的加成绝不能塞进 <see cref="WorkEfficiencyOf"/></b> —— 那条乘子链是**制作/砌墙/挖废墟/搜刮共用**的，
    /// 塞进去会让她在搜刮上获得 Wiki 配置的速度加成。用户原话是「**翻找搜刮**速度」⇒ 只在搜刮这条路上多乘一层。
    /// </para>
    /// 非耗子 <see cref="RatPerk.LootSpeedMultiplier"/> ≡ 1.0 ⇒ 所有既有角色的搜刮耗时**一秒不变**（零回归）。
    /// </summary>
    private double LootEfficiencyOf(Pawn worker)
        => WorkEfficiencyOf(worker)
           * RatPerk.LootSpeedMultiplier(worker.Perks.IsRat, RatLevelNow());

    /// <summary>
    /// 某角色的负重上限乘子（山姆各等级效果按 Wiki 配置**连乘**，不是加算）。由 <see cref="CarryLimitOf"/> 喂给引擎 <c>Loadout.CarryLimit</c>。
    /// </summary>
    public double SamCarryCapacityMultFor(Pawn who)
        => SamPerk.CarryCapacityMultiplier(SamLevelNow(), isSam: who.Perks.IsSam);

    /// <summary>
    /// 某角色的负重上限（kg）＝ Wiki 配置基数 × 承载能力 × authored 专属乘子。
    /// <list type="bullet">
    /// <item>基数对所有人一样（本项目**无"力量"属性**：能力只由 authored 专属效果 + 读过的书承载）；</item>
    /// <item>承载能力 = <c>Pawn.OperationCapability</c>（残缺 × 饥饿，与战斗出手间隔同源）——断手、挨饿的人背不动东西；</item>
    /// <item>专属乘子 = <see cref="SamCarryCapacityMultFor"/>（目前唯一来源是山姆）。</item>
    /// </list>
    /// 三档阈值（30/50/80）随本上限按比例伸缩，故山姆抬的是**每一档**、残缺压的也是**每一档**。
    /// </summary>
    private double CarryLimitOf(Pawn who)
        => CarryCapacity.For(who.OperationCapability, SamCarryCapacityMultFor(who));

    /// <summary>这一趟探索队的总运力（kg）＝ 每个队员的上限之和 + 随队布鲁斯口袋狗衣的驮运容量。</summary>
    private double PartyCarryLimit()
        => ExpeditionBag.PartyCapacity(
            _survivors.Where(s => s.Role == PawnRole.Expedition && s.Alive).Select(CarryLimitOf),
            ExpeditionDogCarryBonus);

    /// <summary>这一趟的探索队员（活着的）。负重账逐人算，所以这个名单要能被反复枚举。</summary>
    private List<Pawn> ExpeditionMembers()
        => _survivors.Where(s => s.Role == PawnRole.Expedition && s.Alive).ToList();

    /// <summary>
    /// [T45] 探索队里**被负重压得最狠的那个人**（移速乘子最低）。HUD 要点他的名——
    /// 负重是**逐人**分档的，只报一个队伍平均数，玩家不知道该给谁减负。队伍为空 ⇒ null。
    /// </summary>
    private (string Name, MemberLoad Load)? WorstCarryLoad()
    {
        Pawn? worst = null;
        foreach (Pawn m in ExpeditionMembers())
        {
            if (worst is null || m.CarryLoad.SpeedMultiplier < worst.CarryLoad.SpeedMultiplier)
            {
                worst = m;
            }
        }
        return worst is null ? null : (worst.DisplayName, worst.CarryLoad);
    }

    /// <summary>
    /// 🔴 [T45·负重激活] **把负重账刷一遍**——这是"装备进账"与"debuff 真作用到人身上"两条链的**唯一收口**。
    ///
    /// <para>═══ 这个方法修的是一个真断链，别删 ═══
    /// 修复前：出门 <c>_bag = new ExpeditionBag(PartyCarryLimit())</c> 是**空包** ⇒ 装备重量未计入；
    /// 而 <c>Loadout.SpeedMultiplier</c> / <c>AttackSpeedMultiplier</c> **全仓无人消费** ⇒ 移速常数 95f。
    /// 两条链都断着，负重系统实际上**不存在**。用户原话：「玩家可以把一把武器改装得很强，
    /// **但是一出门就进入负重 debuff**」——那句话在断链下无论如何都不可能成立。</para>
    ///
    /// <para>每帧刷（关内 ~3 个 Pawn 的加法，可忽略）而不是只在出门时刷一次：<b>关内这三个量都会变</b>——
    /// 断了手 ⇒ 上限掉、装备掉；捡起一把枪 ⇒ 装备重；搜刮/丢弃 ⇒ 战利品变。只刷一次就会是陈旧的账。</para>
    /// </summary>
    private void SyncExpeditionLoad()
    {
        if (_bag == null) // 营地：没有背包，也就没有负重档
        {
            return;
        }

        List<Pawn> members = ExpeditionMembers();

        // ① 上限随队员当下的伤/饿实时变化（断手/挨饿 ⇒ 背不动）
        _bag.SetCapacity(PartyCarryLimit());

        // ② 装备进账：每个人手上的武器（含左右手/双持去重）+ 身上 11 槽的护甲。
        //    改装后的实重走 ItemWeights.WeaponKg（改装变体名 → 实重），本处不认识"改装"这回事——见 [HANDOFF] impl-weaponmod。
        double dogCap = ExpeditionDogCarryBonus;
        _bag.SetGear(ExpeditionLoad.PartyGearKg(members.Select(m => m.GearKg)));

        // ③ 逐人分档：他自己的装备 + 按运力占比分摊的战利品，对着**他自己的上限**。
        //    逐人（而不是全队一个数）是要害——用户要的是"你把你的枪改装得很强 ⇒ 你走得慢"，
        //    全队摊薄会把这条代价稀释掉；具体重量与阈值以 Wiki 配置为准。
        double lootKg = _bag.LootKg;
        double totalLimit = members.Sum(CarryLimitOf);
        foreach (Pawn m in members)
        {
            m.SetCarryLoad(ExpeditionLoad.For(
                gearKg: m.GearKg,
                lootKg: lootKg,
                dogCapacityKg: dogCap,
                memberLimitKg: CarryLimitOf(m),
                totalMemberLimitKg: totalLimit));
        }
    }

    /// <summary>
    /// 战利品落地。**营地**：直接入共享库存（家就是仓库，无上限）。**探索关内**：先进远征背包，受硬上限拦截——
    /// 背不下的整件留在原地，成堆材料只拿得走装得下的几件。回营时由 <see cref="DumpExpeditionBag"/> 统一倾倒进库存。
    /// <para>工具（游标卡尺/锯片/烧杯）重量为 0，但探索中仍先进背包；只有活人回营后才装进工作台。</para>
    /// </summary>
    /// <returns>本次入账的食物份数（营地口径；探索中食物先留在背包，此处返 0）。</returns>
    private int CollectLoot(IReadOnlyList<LootItem> loot, out List<ToolSlot> tools, out string leftBehindNote)
    {
        tools = new List<ToolSlot>();
        leftBehindNote = "";

        if (_bag == null) // 营地：老路径，无上限
        {
            return LootApplication.Apply(loot, _inventory, _bookRegistry, _bookResolver, tools);
        }

        var leftBehind = new List<string>();
        foreach (LootItem l in loot)
        {
            int taken = _bag.AddAsManyAsFit(l);
            if (taken < l.Quantity)
            {
                LootItem missed = l.Kind is LootKind.Food
                    ? LootItem.Food(l.Quantity - taken)
                    : l.Kind is LootKind.Material
                        ? LootItem.Material(l.RefId, l.Quantity - taken)
                        : l;
                leftBehind.Add(LootDisplay.NameOf(missed));
            }
        }

        if (leftBehind.Count > 0)
            leftBehindNote = $"背包塞不下，{string.Join("、", leftBehind)}留在了原地。";

        return 0; // 食物先躺在背包里，回营才进营地存粮
    }

    /// <summary>回营：把远征背包整个倾倒进共享库存/存粮/工作台，然后销毁背包（营地内无上限）。</summary>
    private void DumpExpeditionBag()
    {
        if (_bag == null)
            return;

        var tools = new List<ToolSlot>();
        int food = LootApplication.Apply(_bag.Contents, _inventory, _bookRegistry, _bookResolver, tools);
        if (food > 0)
            _resources.AddFood(food);
        InstallFoundTools(tools);

        GD.Print($"[负重] 探索队回营，卸下 {_bag.LootKg:0.0}kg 战利品（{_bag.Contents.Count} 件）"
                 + $"，另有 {_bag.GearKg:0.0}kg 装备穿在身上。");
        _bag = null;

        // [T45] 回营 ⇒ 清账，恢复无罚：营地里没有背包，也就没有负重档（负重是**出门**的代价）。
        // 全员清（不止探索队）——队员回来后会改 Role，逐个清最省心，且对没有账的人是幂等的。
        foreach (Pawn s in _survivors)
        {
            s.ClearCarryLoad();
        }
    }

    /// <summary>
    /// 光环生产系数（供 craft-worktime 工时推进消费，见 [HANDOFF]）：工人是道格且羁绊光环激活 → <see cref="DougBruceBond.AuraProductionMult"/>(1.10)，
    /// 否则为中性值。布鲁斯非 Pawn 不进工作台，实际仅道格受益；具体生产效果以 Wiki 配置为准。调用方把它乘进喂 CraftingJob.Advance 的分钟数。
    /// </summary>
    public float BondProductionMultFor(Pawn crafter)
        => crafter == _doug ? BondAuraNow().ProductionMult : 1f;

    /// <summary>道格 3 级相依为命光环的操作能力乘子；仅道格作为消费层工作者时生效。</summary>
    public float BondOperationMultFor(Pawn worker)
        => worker == _doug ? BondAuraNow().OperationMult : 1f;

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
    /// 羁绊视野系数（施到玩家侧 <see cref="VisionMask"/> 的逐观察者基础锥）：道格与布鲁斯各等级效果以 Wiki 配置为准，皆依道格存活门控。其余观察者原样。
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
        // 道格 authored「自带装备」：棍棒（主手·骨折工厂）+ 墨镜（眼镜槽）+ 开局三件套。走 extraApparel 统一穿墨镜。
        var doug = Pawn.Create("道格", StartingWeapon.Club, new Color(0.62f, 0.56f, 0.42f), extraApparel: new[] { "墨镜" });
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
        // 布鲁斯身故清除 + 置 bruce_dead（见 OnActorDied 犬类分支）。旗标在 StoryFlags 中随档持久化。
        _storyFlags.Set("bruce_present", "true");

        // 羁绊技能接线（synergy-wiring，批次5）：
        // 注：2 级原「道格攻击布鲁斯目标伤害加成」已被**用户 L2 修订**退役（doug-logic 改为
        // DougBruceBond.CanCraftDogGear 解锁狗装备制作，旧伤害条款移除，待确认可恢复）。新 2 级=狗装备门控，
        // 消费方在 dog-gear 制作系统落地后接 CanCraftDogGear，本批无该系统故 2 级伤害接线不再接（见 [DECISION]）。
        // · 3 级光环减伤：道格/布鲁斯相依为命时受伤 ×AuraDamageTakenMult(0.85)。二者各挂承伤系数。
        doug.SetIncomingDamageFactor(() => BondAuraNow().DamageTakenMult);
        bruce.SetIncomingDamageFactor(() => BondAuraNow().DamageTakenMult);
        // · 布鲁斯 2 级视距效果：自主缠斗侦测半径按 BruceRangeMult 缩放（依道格存活）。
        bruce.SetDetectRangeMultProvider(() => DougBruceBond.BruceRangeMult(BondLevel, _doug is { Alive: true }));
        // · 布鲁斯 2 级狗装备效果：攻击速度、移动速度加成由 Wiki 配置提供，且随道格存活门控。
        bruce.SetAuthoredAttackSpeedMult(() =>
            DougBruceBond.CanCraftDogGear(BondLevel, _doug is { Alive: true })
                ? DougBruceBond.BruceAttackSpeedMult : 1.0);
        bruce.SetAuthoredMoveSpeedMult(() =>
            DougBruceBond.CanCraftDogGear(BondLevel, _doug is { Alive: true })
                ? DougBruceBond.BruceMoveSpeedMult : 1.0);

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
    /// [T61] 耗子招募对话（下水道最深处）—— 与 <see cref="PromptNurseRecruit"/> 逐行同构，一格没另起炉灶。
    /// 接受＝值 1 → 置 <see cref="RatRecruit.AgreedFlag"/>；婉拒＝值 0 → **不置任何旗**（日后再下来还能再谈）。
    /// 真正的 Pawn 注入延到回营（<see cref="MaybeRecruitRat"/>）。
    /// </summary>
    private void PromptRatRecruit(RatRecruitOffer offer)
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
                _storyFlags.Set(RatRecruit.AgreedFlag, "true"); // 答应 → 待回营注入 + 相遇对话去重
                ShowDiscoveryNarrative(RatRecruit.AcceptTitle, RatRecruit.AcceptNarrative);
            }
            else
            {
                // 婉拒：不置任何旗 → ShouldOfferRecruitment 仍为真，可再下来再谈。
                ShowDiscoveryNarrative(RatRecruit.DeclineTitle, RatRecruit.DeclineNarrative);
            }
        };
    }

    /// <summary>
    /// [T61] 回营正史入队钩子（耗子）：关内已答应 ⇒ 回营真正注入 Pawn + 置 <see cref="RatRecruit.EnlistedFlag"/> 硬守卫。
    /// 由 <see cref="UnloadExplorationLevel"/> 在探索队回营后调用（与护士/道格同一条既有口径）。
    /// </summary>
    private void MaybeRecruitRat()
    {
        if (!RatRecruit.ShouldEnlistOnReturn(_storyFlags))
            return;
        SpawnRat();
        _storyFlags.Set(RatRecruit.EnlistedFlag, "true"); // 注入一次硬守卫
        GD.Print("[Rat] 下水道招募回营 → 耗子正史入队。");
    }

    /// <summary>
    /// [T61] 注入耗子 Pawn：普通幸存者，专属效果走 authored perk（<see cref="RatPerk"/>，由 <c>Pawn.Create</c> 按名
    /// <see cref="RatPerk.RatName"/> 授予，见 Pawn.cs）。此后走标准管道（聚餐分粮/双班守夜/搜刮）自然生效。
    /// 幂等：已在营（按名存活）则不重复注入。
    /// <para>
    /// 🔴 <b>装备：她穿的是开局那套标准布衣，<u>不</u>带"潮湿破布夹克"入队。</b>
    /// 用户写的「浑身恶臭穿着**潮湿破布夹克**的女人」是**外观描写**，不是装备表条目 —— 目录里没有这件护甲，
    /// 而新增一件护甲要走**五处登记**（ArmorTable / ApparelCatalog / NightWatchContest.ApparelStealth / 重量 / 图标），
    /// 其中"潮湿"和它的潜行值**是引擎里不存在的语义** ⇒ 现编就是**程序化引申 authored 内容**。
    /// 已在 journal 列为"待用户拍板"：这件夹克是**纯描写**，还是**要做成一件真装备**（若要，请给防护/重量/潜行值）。
    /// </para>
    /// </summary>
    private void SpawnRat()
    {
        if (_survivors.Any(s => s.Alive && s.DisplayName == RatPerk.RatName))
            return; // 幂等：已在场不重复

        var rat = Pawn.Create(RatPerk.RatName, StartingWeapon.Rapier, new Color(0.46f, 0.44f, 0.40f)); // 泥灰色·刺剑+开局三件套
        rat.Position = _cameraCenter + new Vector2(-40f, 0f);
        AddActor(rat);
        _survivors.Add(rat);
        _cardBar?.SetSurvivors(_survivors); // 耗子入队：卡牌栏加卡（同护士）
        GD.Print("[Rat] 耗子 已注入营地。");
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

        var nurse = Pawn.Create(NurseRecruit.NurseName, StartingWeapon.None, new Color(0.78f, 0.74f, 0.70f)); // 空手入队
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
    /// <para>
    /// <b>正式穿戴入口已落地</b>＝<b>库存面板的「布鲁斯装备」区</b>（<c>StashPanel</c>）：穿走 <c>OnStashEquipRequested</c>
    /// 按 <see cref="DogGearCatalog.IsDogGear"/> 分流到布鲁斯，脱走 <c>DogGearUnequipRequested</c> →
    /// <see cref="OnStashDogGearUnequipRequested"/>，穿戴态由 <c>DogGearProvider</c> 实时喂回面板。
    /// ⇒ 本调试键<b>只剩"一键塞满快速验收"</b>的用途，不再是唯一入口。
    /// </para>
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

    /// <summary>静态事件必须退订：<see cref="Actor.AnyDied"/> 是全局的，换场景后不退订会继续调到已回收的本实例。</summary>
    public override void _ExitTree()
    {
        Actor.AnyDied -= OnAnyActorDied;
        Actor.AnyDamaged -= OnAnyActorDamaged;   // 静态事件必须退订，否则换场景后旧 CampMain 还会被叫醒
        // 半身掩体场是 static：不清就会把营地的桌椅沙袋带进下一个场景（探索关/主菜单）。
        if (Actor.Covers == _coverField)
        {
            Actor.Covers = null;
        }
        // 家具减速场同为 static：不清就会把营地的椅子柜子带进下一个场景（探索关里凭空多出减速带）。
        if (Actor.Slowdowns == _traversal)
        {
            Actor.Slowdowns = null;
        }
    }

    /// <summary>
    /// 场上**任何**单位倒下（丧尸/劫掠者/幸存者/布鲁斯/商人，走 <see cref="Actor.AnyDied"/> 静态事件）：
    /// 倒地留尸 + 把它**手里拿的和身上穿的**变成可搜刮的战利品。
    /// <para>
    /// <b>倒地留尸</b>（impl-corpse-push 的规则）：谁死在哪就躺在哪；同格已有尸体 → 自动挤到旁边最近的空地
    /// （RimWorld 式）。尸体**没有碰撞体积**，不挡人也不断寻路，只占一个「尸体格」。
    /// </para>
    /// <para>
    /// <b>可搜刮</b>（<see cref="CorpseLoot.Strip"/>：**持什么掉什么、穿什么扒什么，零掷骰、必掉**）：
    /// 丧尸穿着生前的日常着装，那件衣服是重要的装备来源（牛仔外套只能搜刮、缝不出来；精英丧尸头上那顶盔
    /// 更是主要获取途径）；**劫掠者本来就持械**，他那把匕首/手枪同样直接落进尸体——这是全图近战武器的主要来源。
    /// 扒得出东西的才登记成可点击容器。
    /// </para>
    /// <para>
    /// <b>营地与探索关各走各的空间执行</b>（规则同一条、显示都走 faux-iso）：营地走 <see cref="CorpseYard"/>
    /// （尸体格 + 相位过期）；探索关走
    /// <see cref="SpawnLevelCorpse"/>（关内一个可搜刮触发点，随关卡消失）。<b>出门探索恰恰是玩家该拿装备的
    /// 地方</b>——那 4 个持匕首的据点幸存者、日后关内任何持械敌人，杀了就该掉。
    /// （处决/放逐走各自的 QueueFree/淡出路径，不经死亡事件 → 既有「不留尸体」口径不受影响。）
    /// </para>
    /// </summary>
    private void OnAnyActorDied(Actor actor)
    {
        if (_currentLevel is not null)
        {
            SpawnLevelCorpse(actor);
            MaybeMarkGoldfingerGangCleared(actor);
            return;
        }
        if (_corpseYard is null)
        {
            return;
        }
        if (_corpseYard.SpawnFor(actor) is { Loot.Count: > 0 } corpse)
        {
            RegisterCorpseContainer(corpse);
        }
    }

    // ---------------- 探索关的尸体（T36：杀敌 ⇒ 落尸 ⇒ 可搜刮 ⇒ 里面是他的武器+护甲） ----------------
    //
    // 用户拍板：「敌人掉武器的，他的武器直接落在他的可搜刮尸体里。」——这条此前**只在营地生效**，
    // 而营地夜袭的劫掠者只有匕首和手枪 ⇒ 玩家真正该拿装备的地方（出门探索）一件都掉不出来。
    //
    // 三样东西全是**复用**，一行新规则都没有：
    //   · 扒什么 → CorpseLoot.Strip（持什么掉什么、穿什么扒什么，武器在前，零掷骰）——与营地同一个函数；
    //   · 叫什么 → CorpseNaming.ContainerName（与营地同一条命名；序号唯一，否则同名敌人互相顶掉登记）；
    //   · 怎么搜 → _containerLoot + BeginLootSession（逐件转出、走开即停手）——与关内物资搜刮点同一条链路。
    // 新增的只有空间执行：关内铺一个可踏入的尸体点（TestExploration.AddCorpseSearchPoint，cartesian 坐标系）。
    //
    // ⏱ 探索尸体不进营地 CorpseYard，但位置/遗物/半天计数跨关卡保存；满三个半天才刷没。
    // 🔴 authored 剧情尸体（帮众/树上的哥顿/克莉丝汀/祖母）是**发现点**、不是本通道造出来的战斗尸体，
    //    命名空间结构性隔离（CorpseNaming：ascii id vs 中文「的尸体 #」），一根汗毛都没碰。

    // ============ 关内可关的门（[T49] 废弃医院的防火门/安全门/卷帘门）============
    //
    // 🔴 **为什么这套东西值得存在**：医院有全图最多的一批丧尸，而用户给它定的是**中危**——
    // 这两句只有在"能绕、能隔开"时才同时成立。关门就是"隔开"的那一手：把追你的丧尸关在门后，
    // 它得绕到这道边界的**另一个门洞**去（很远），那段路就是你换来的时间。
    // （连场战斗的代价见历史报告；报告数字不是当前配置。）
    //
    // **分层**：门的几何/状态/碰撞/导航洞全部归关卡（TestExploration.ToggleLevelDoor），
    // CampMain 只做它本来就在做的那件事——**把它登记成一个可右键前往的容器**，人走到了就转发一下动作。
    // 噪音走 Actor.EmitDoorNoise（100）——**和营地开门、和劫掠者开门是同一个 100**，不给玩家开后门。

    /// <summary>关内门的容器 role（与营地门的 "door" 分开：营地门走 CampStructureInstance，关内门归关卡持有）。</summary>
    private const string LevelDoorRole = "leveldoor";

    /// <summary>关内发现点的临时容器 role：右键先走到点位，再触发与 Area2D 相同的 OnDiscovery。</summary>
    private const string LevelDiscoveryRole = "leveldiscovery";

    /// <summary>把关卡里的门登记成可右键前往的容器（探索队入关后调用；回营时随 <see cref="ClearLevelDoorContainers"/> 注销）。</summary>
    private void RegisterLevelDoorContainers()
    {
        if (_currentLevel is not TestExploration level)
        {
            return;
        }

        foreach ((string name, Rect2 rect) in level.LevelDoorTargets())
        {
            _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = LevelDoorRole });
        }
    }

    /// <summary>回营：注销关内门的登记（门随关卡一起没了，登记不该漏进营地/存档）。</summary>
    private void ClearLevelDoorContainers()
    {
        _containers.RemoveAll(c => c.Role == LevelDoorRole);
    }

    /// <summary>
    /// 玩家走到关内门前：<b>锁着的门</b>去撬（见 <see cref="ExecuteLevelDoorPick"/>）；没锁的门开着就关上、关着就推开
    /// （<b>开门只有一种动作</b>，与营地同口径）。推/关都发 Wiki 配置的门声。
    /// </summary>
    private void ExecuteLevelDoorInteract(Pawn arriver, ContainerRef hit)
    {
        if (_currentLevel is not TestExploration level)
        {
            return;
        }

        // 锁着的门推不开——走撬锁（消耗铁丝·按 LockTier 掷成功率·失败断丝）。全项目首次把撬锁接进探索关。
        if (level.LevelDoorState(hit.Name) == DoorState.Locked)
        {
            ExecuteLevelDoorPick(arriver, level, hit.Name);
            return;
        }

        if (!level.ToggleLevelDoor(hit.Name, out string message))
        {
            if (!string.IsNullOrEmpty(message))
            {
                _campToast.Show(message, CampToast.Bad); // 门缝里站着东西：关不上
            }
            return;
        }

        arriver.EmitDoorNoise(); // 100 —— 与营地开门/劫掠者开门同一个数，单一真源 NoiseLogic.DoorNoiseRadius
        _campToast.Show($"{arriver.DisplayName} {message}", CampToast.Ok);
    }

    /// <summary>
    /// 撬一扇关内的锁门（复用纯逻辑 <see cref="DoorLogic"/>，与营地门 <see cref="BeginPickLock"/> 同一套规则）。
    /// <b>一次交互 = 一次尝试</b>：撬锁很安静（30 噪音，惊动不了东西）——这正是它相对「砸开(180 招整层)」的价值。
    /// <list type="bullet">
    /// <item>身上<b>没铁丝</b>：撬不动（只剩砸这一条路，而砸很响）。</item>
    /// <item><b>撬开</b>：门开（<see cref="TestExploration.UnlockLevelDoor"/> 撤墙层 + 摘导航洞 + 重烘焙），禁闭区从此可达。</item>
    /// <item><b>失败</b>：铁丝断在锁里（扣 1 根）；还有铁丝就再走一趟接着撬——门外那群东西不会停下来等你。</item>
    /// </list>
    /// 撬锁 roll 走可注入的 <see cref="_pickRng"/>（项目铁律：随机可复现）。
    /// </summary>
    private void ExecuteLevelDoorPick(Pawn arriver, TestExploration level, string doorName)
    {
        LockTier tier = level.LevelDoorLockTier(doorName);
        int wire = _inventory.MaterialCount(DoorLogic.LockpickMaterialKey);
        if (!DoorLogic.CanPick(DoorState.Locked, arriver.Faction, isAnimal: false, wire))
        {
            _campToast.Show($"{doorName}锁着，而你没有铁丝——只能砸开了（很响）。", CampToast.Bad);
            return;
        }

        arriver.EmitLockpickNoise(); // 30 —— 撬锁本身惊动不了任何东西
        DoorPickAttempt r = DoorLogic.TryPick(tier, _pickRng);
        if (r.Success)
        {
            level.UnlockLevelDoor(doorName);
            _campToast.Show($"咔哒——{arriver.DisplayName} 撬开了{doorName}。", CampToast.Ok);
            GD.Print($"[关内门] {arriver.DisplayName} 撬开 {doorName}（{LockLabel(tier)}）。");
            return;
        }

        // 失败：铁丝断在锁里（扣 1 根）。
        _inventory.TrySpendMaterial(DoorLogic.LockpickMaterialKey, 1);
        int left = _inventory.MaterialCount(DoorLogic.LockpickMaterialKey);
        _campToast.Show(
            left > 0
                ? $"铁丝断在了锁里。（还剩 {left} 根，走回去再撬……）"
                : $"最后一根铁丝也断了。{doorName}还锁着——要么找铁丝，要么砸。",
            CampToast.Bad);
    }

    /// <summary>本次已铺进关卡的尸体容器名；卸载时注销临时登记，持久态另存于 _explorationCorpses。</summary>
    private readonly List<string> _levelCorpseContainers = new();

    /// <summary>关内尸体序号（跨探索单调递增并进存档；重访地点时不能与尚未腐烂的尸体撞名）。</summary>
    private int _levelCorpseSeq;

    /// <summary>
    /// 关内某单位倒下：把它<b>手里拿的 + 身上穿的</b>原样变成一个可搜刮的尸体点（就落在它咽气的那个点上）。
    /// <b>光尸体不登记</b>（衣不蔽体、赤手空拳）——与营地 <c>CorpseYard.SpawnFor</c> 同一条闸门：
    /// 地图上不该多出一个点了没反应的可交互点。
    /// </summary>
    /// <summary>
    /// 金手指帮据点：一名守备（<see cref="GoldfingerGang.GuardName"/>）倒下后，若场上再无存活守备 ⇒ 置「灭帮」永久旗标
    /// （<see cref="GoldfingerDiscovery.GangClearedFlag"/>），克莉丝汀 L3「大仇得报」升级读它。幂等（旗标已置也无害）。
    /// 仅金手指帮根据地关、且倒下的是守备时才检查；排除刚倒下的这名（<paramref name="justDied"/>，防死亡事件时序把自己算作存活）。
    /// </summary>
    private void MaybeMarkGoldfingerGangCleared(Actor justDied)
    {
        if (_currentLevel is not TestExploration level
            || level.DestinationName != WorldMapPanel.GoldfingerBaseName
            || CorpseYard.NameOf(justDied) != GoldfingerGang.GuardName)
        {
            return;
        }
        bool anyGuardAlive = level.LevelHostiles()
            .Any(h => h != justDied && h.Alive && CorpseYard.NameOf(h) == GoldfingerGang.GuardName);
        if (!anyGuardAlive)
        {
            GoldfingerDiscovery.MarkGangCleared(_storyFlags);
        }
    }

    private void SpawnLevelCorpse(Actor actor)
    {
        if (_currentLevel is not TestExploration level)
        {
            return;
        }

        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(actor.WornArmor, actor.HeldWeapons);
        // 幸存者断肢背包内的装备随尸体可搜出（非 Pawn 走 Actor.SeveredBackpackItems = 空列表，零开销）。
        IReadOnlyList<LootItem> backpack = actor.SeveredBackpackItems;
        if (loot.Count == 0 && backpack.Count == 0)
        {
            return;   // 身上什么也没有 ⇒ 不登记成可搜刮点（是否有衣物由 Wiki 配置决定）
        }

        // 背包装备合并进尸体 loot
        if (backpack.Count > 0)
        {
            var combined = new List<LootItem>(loot.Count + backpack.Count);
            combined.AddRange(loot);
            combined.AddRange(backpack);
            loot = combined;
        }

        string container = CorpseNaming.ExplorationContainerName(CorpseYard.NameOf(actor), ++_levelCorpseSeq);
        _containerLoot.Register(container, loot);
        _levelCorpseContainers.Add(container);
        level.AddCorpseSearchPoint(container, actor.GlobalPosition);
        _explorationCorpses.Add(new ExplorationCorpseSave
        {
            Destination = level.DestinationName,
            ContainerId = container,
            OwnerPawnId = actor is Pawn pawn ? pawn.Id : -1,
            X = actor.GlobalPosition.X,
            Y = actor.GlobalPosition.Y,
            SpawnPhaseTick = _corpseYard.PhaseTick,
            Loot = loot.ToList(),
        });

        GD.Print($"[探索关·落尸] {container}：{string.Join("、", loot.Select(LootDisplay.NameOf))}");
    }

    /// <summary>
    /// 卸载探索关：把本关尸体容器的剩余物同步回跨关卡账本，再注销本次场景登记。
    /// </summary>
    private void ClearLevelCorpses()
    {
        foreach (string container in _levelCorpseContainers)
        {
            _containerLoot.Remove(container);
        }
        _levelCorpseContainers.Clear();
    }

    private void SyncLevelCorpseLoot()
    {
        foreach (ExplorationCorpseSave corpse in _explorationCorpses)
        {
            if (_levelCorpseContainers.Contains(corpse.ContainerId))
                corpse.Loot = _containerLoot.Remaining(corpse.ContainerId).ToList();
        }
        _explorationCorpses.RemoveAll(c => c.Loot.Count == 0 && !c.HasTransmitter);
    }

    private void RestoreLevelCorpses(TestExploration level)
    {
        foreach (ExplorationCorpseSave corpse in _explorationCorpses.Where(c => c.Destination == level.DestinationName))
        {
            if (corpse.Loot.Count == 0 && !corpse.HasTransmitter)
                continue;
            _containerLoot.Register(corpse.ContainerId, corpse.Loot);
            _levelCorpseContainers.Add(corpse.ContainerId);
            level.AddCorpseSearchPoint(corpse.ContainerId, new Vector2((float)corpse.X, (float)corpse.Y));
        }
    }

    private void SweepExplorationCorpses()
    {
        List<ExplorationCorpseSave> expired = ExplorationRemains.SweepExpired(
            _explorationCorpses, _corpseYard.PhaseTick);
        foreach (ExplorationCorpseSave corpse in expired)
        {
            RecordPlaytestEvent(PlaytestEventKind.CorpseRecovery, "遗体腐化", corpse.Destination,
                corpse.HasTransmitter
                    ? $"{corpse.ContainerId} 消失；关键设备回到原位"
                    : $"{corpse.ContainerId} 消失；未取回遗物永久损失");
            GD.Print(corpse.HasTransmitter
                ? $"[探索遗体] {corpse.ContainerId} 已腐烂；关键设备回到广播站原位。"
                : $"[探索遗体] {corpse.ContainerId} 已腐烂；未取回遗物消失。");
        }
    }

    private void OnActorDied(Actor actor)
    {
        // 倒地留尸 + 战利品**不在这里**：本回调只挂在幸存者/狗身上（AddActor），
        // 丧尸与劫掠者根本不经过它 —— 落尸统一走 OnAnyActorDied（Actor.AnyDied，覆盖全体）。

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
            RecordPlaytestEvent(PlaytestEventKind.SurvivorDied, "幸存者死亡",
                _currentLevel?.DestinationName ?? "营地", p.DisplayName);
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
                RecordPlaytestEvent(PlaytestEventKind.Ending, "普通全灭", "营地", "营地无人生还");
                // 全灭 → 普通全灭面板「营地无人生还」。
                // 🔴 两条"全灭走 CG"路由（CG② 军袭 / CG① 尸潮）均**不经此判定**：它们各走强制终局南逃谢幕序列
                // （TryTriggerMilitaryRaid / TryTriggerHordeSiegeEnding → BeginSouthEscapeEnding，序列内先置 _gameOver）。
                // 旧 EndingCg.ForGameOver/ForKind/EndingKind 全灭结局路由已整条退役（[用户裁决·选项B]）——它生产不可达却被单测测绿。
                GameOverPanel.Show(_hud);
            }
        }
    }

    // ---------------- 每帧刷新 ----------------

    public override void _Process(double delta)
    {
        _ambient.Color = _clock.CurrentAmbientColor();
        bool exploring = _currentLevel != null;

        _audioMoodElapsed += Math.Max(0, delta);
        if (_audioMoodElapsed >= 0.25)
        {
            _audioMoodElapsed = 0;
            bool explorationCombat = _currentLevel is not null
                && _currentLevel.LevelHostiles().Any(h => h.Alive && h.HasActiveTarget);
            bool combat = _raidActive || _nightRaidActive || _tutorialActive || _peteEventActive || explorationCombat;
            bool horde = _siegeActive || _southEscapeActive;
            bool ending = _gameOver && !_southEscapeActive;
            GameAudioRuntime.SetMusic(GameAudioCatalog.MusicFor(
                ending, horde, combat, exploring, _clock.IsNight));
            string destination = _currentLevel?.DestinationName ?? "";
            GameAudioRuntime.SetAmbience(GameAudioCatalog.AmbienceFor(
                exploring,
                destination == ExplorationCache.SewerName,
                exploring && ExplorationLighting.IsIndoorsDark(destination),
                _clock.IsNight));
        }

        // [T15] 家具减速场跟住 _furniture 的增删（摆了张床/垒了垛沙袋/拆走一个柜子）。只在**件数变了**的帧重建，
        // 空闲营地零开销。放这儿而不是逐个调用点加一行 —— 那样每个新增家具的作者都得记得来登记一次，迟早漏。
        SyncTraversalField();

        // 战斗跨过了存档相位 ⇒ 存档欠着，打完这一仗立刻补上（见 CampMain.Save.cs）。
        TickPendingAutosave();

        // 🔴 [T45] 负重账每帧刷：关内断手/掉甲/捡枪/搜刮/丢弃都会改账，逐人的移速与攻速惩罚要跟着当下走。
        // （营地内 `_bag == null`，本调用直接返回 ⇒ 零开销、零回归。）
        SyncExpeditionLoad();

        // HUD 状态行仅在内容实际变化的帧重建（脏缓存）——空闲营地不再每帧造大字符串+调 SetStatus。
        // 信号全是廉价 int/enum/bool：Count(s=>s.Alive) 的 lambda 无捕获，编译期缓存为静态委托不分配。
        int hudPhase = (int)_clock.CurrentPhase;
        int hudClockKey = _clock.ClockMinuteKey();
        bool hudPaused = _clock.Paused;
        int hudAlive = _survivors.Count(s => s.Alive);
        // 尸潮倒计时：未望见=Hidden（HUD 零痕迹），望见后常驻，到期转红警。旗标只置不撤，Has 即知情（持久）。
        HordePhase hordePhase = HordeTimeline.Evaluate(_clock.Day, _storyFlags.Has(HordeTimeline.SightedFlag));
        int hordeDays = HordeTimeline.DaysRemaining(_clock.Day);
        // 背包脏键：按配置精度缓存整数（探索中才有背包；营地恒为无背包状态）。
        int hudBagKey = _bag != null ? (int)Math.Round(_bag.CarriedKg * 10) : -1;
        // [T45] 最慢队员的移速惩罚脏键：上限随伤/饿变化时 CarriedKg 可能一动不动，但惩罚变了 ⇒ 单独一个键。
        (string Name, MemberLoad Load)? worstLoad = _bag != null ? WorstCarryLoad() : null;
        int hudLoadKey = worstLoad is { } wl ? (int)Math.Round(wl.Load.SpeedMultiplier * 100) : -1;
        if (!_hudInit || exploring != _hudExploring || _clock.Day != _hudDay || hudPhase != _hudPhase
            || hudClockKey != _hudClockKey || _clock.SpeedIndex != _hudSpeedIndex
            || hudPaused != _hudPaused || hudAlive != _hudAlive
            || (int)hordePhase != _hudHordePhase || hordeDays != _hudHordeDays
            || hudBagKey != _hudBagKey || hudLoadKey != _hudLoadKey)
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
            _hudBagKey = hudBagKey;
            _hudLoadKey = hudLoadKey;
            // 🔴 [T45] 探索中显示装备/战利品分项、当前负重状态与对应减速（数值来自 Wiki 配置）。
            // 修复前只有背包一项——玩家看得到自己背了多少，**看不到这让他慢了多少**，
            // 而"慢了多少"恰恰是负重唯一的实际后果。现在三件事一眼可见：
            //   ① 装备 vs 战利品**分开列**（装备重量不是捡来的）；
            //   ② 负重档位（决定"还拿不拿"）；
            //   ③ **最慢的那个人是谁、慢了多少**（负重逐人分档 ⇒ 得点名，玩家才知道该给谁减负）。
            string bagLine = "";
            if (_bag != null)
            {
                bagLine = $"   负重 {CarryCapacity.FormatBag(_bag.GearKg, _bag.LootKg, _bag.CapacityKg)}";
                if (worstLoad is { } w && CarryCapacity.FormatMember(w.Name, w.Load) is { Length: > 0 } memberLine)
                {
                    bagLine += $"   ⚠ {memberLine}";
                }
            }
            _hud.SetStatus(HudStatusLine.Compose(
                exploring, _clock.Day, _clock.ClockString(), _clock.CurrentPhase,
                _clock.SpeedLabel(), hudAlive, bagLine));
            _hud.SetHordeCountdown(
                hordePhase != HordePhase.Hidden,
                hordePhase == HordePhase.Arrived,
                hordeDays);
        }

        if (_returnWarningPopup.Visible && _clock.CurrentPhase == DayPhase.DayExplore)
            _returnWarningPopup.SetRemainingTime(_clock.GetExploreTimeRemaining());

        // 每帧把敌袭战斗态同步给守卫：平时由岗哨 AI 扫视，战斗开始后玩家移动/瞄准完整接管。
        SyncGuardCombatControl();
        SyncProductionCombatControl();

        if (_raidActive)
            UpdateRaid(delta);

        if (_nightRaidActive)
            UpdateNightRaid(delta);

        if (_tutorialActive)
            UpdateChristineTutorial(delta);

        if (_peteEventActive)
            UpdatePeteRescue(delta); // 皮特开门救援逐帧战（守卫锁三尸 + 胜负→招募/失败），正文在 CampMain.PeteEvent.cs

        UpdatePendingInteract(delta);   // 右键前往：轮询到达 → 开面板/开搜（自带暂停）
        UpdateLootJobs(delta);          // 逐件搜刮：**后台**推进（可多人并发各搜各的；玩家的控制权始终自由）
        TickLockPick(delta);            // 撬锁推进（实时秒；撬开=门开，失败=断根铁丝接着撬）
        UpdatePendingSite(delta);       // 右键动作菜单：轮询"走到门/围栏前"的到达 → 开干
        TickDismantle(delta);           // 静默拆除推进（安静 30、慢；拆满=一个真的洞）
        TickFenceWork(delta);           // 砌墙推进（加固/补窟窿；逐格结算，中断即作废当前这格）
        TickBash(delta);                // 破坏推进（武器派生伤害/节奏；每下 180 噪音）
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

        // 恢复加成按游戏分钟记账；只有角色确实睡在真实床上的分钟计入分子。
        TickBedSleepMinutes();

        // 0.5 游戏小时手术：世界钟走才推进；暂停冻结；患者失血死亡/目标伤情消失立即中断。
        TickSurgeryWorktime();

        // 常规夜袭在入夜瞬间排程、到游戏内时刻才落地；读档恢复同一排程，不重掷。
        TickNightEventSchedule();

        // 废墟挖掘推进：挖掘者在场时逐游戏分钟推进；挖满→收获+清场显露空地。面板冻结时标时同样不推进。
        TickRubbleDig();

        // [T72] 菜园生长：每帧把 delta 折成游戏小时喂给计时器（昼夜都走、零维护）。一座菜园都没有时整段跳过。正文在 CampMain.Farming.cs。
        TickCropGrowth(delta);

        // 导航图首帧就绪后跑一次门缝连通性自检。
        if (!_navTested)
        {
            Rid map = GetWorld2D().NavigationMap;
            if (NavigationServer2D.MapGetIterationId(map) != 0)
            {
                // 门缝连通自检是**建图期的地图健全性检查**（"每栋建筑都进得去吗"），**只跑一次**。
                // ⚠️ 不能每次重烘焙都跑：门落地后，"建筑可达性"变成了**运行时动态的**——玩家把住宅的门一关，
                // 屋里就**本该**不可达，此时该自检会喊"FAIL 穿墙=是(导航洞失效!)"，那是**假警报**
                // （导航洞非但没失效，恰恰是它生效了）。而 RebakeNavigation 会复位 _navTested，
                // 于是每开关一次门就误报一次。故把"跑没跑过"单独记，与 _navTested（右键交互的就绪闸）解耦。
                if (!_navVerified)
                {
                    VerifyNavConnectivity();
                    _navVerified = true;
                }
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
            case Key.F1:
                // 素材出处（CC BY 署名）。只读，不冻结时标——看一眼出处而已，没理由把世界按停。
                _creditsPanel.Toggle();
                break;
            case Key.F5:
                ToggleSavePanel();   // 存档 / 读档面板（见 CampMain.Save.cs）
                break;
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
        // 素材出处（F1）：只读一页，最先关掉——它不冻结时标，也不叠在任何东西之下。
        if (_creditsPanel.IsOpen)
        {
            _creditsPanel.Close();
            return true;
        }
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
        // [批次21·T14] 烹饪面板：ESC 关掉（锅里配了一半的食材留着，下次打开还在——不白配一遍）。
        if (_cookingOpen)
        {
            CloseCooking();
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
        // [impl-furniture-registry] 摆放模式：左键落位、右键作罢，抢在一切常规点选/前往之前。
        // 此前是六块逐类型同形的 if(_placingX){左键TryPlaceX / 右键EndX+提示}——加一种家具就照抄一块，漏了就摆不下去。
        // 收成遍历注册表：命中正处于放置模式的那一种（同一时刻至多一种为真），把左右键派回它登记的 TryPlace/Cancel。
        // 正文（校验/扣料/落地）全在各自 partial 文件里，一字未改；沙袋仍走它自己的 SandbagSpec.CanPlace 特殊校验。
        foreach (PlaceableFurnitureDef def in _placeables)
        {
            if (def.IsPlacing is null || !def.IsPlacing())
            {
                continue;
            }
            if (mb is { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                def.TryPlace!(MouseWorldPoint());
                return;
            }
            if (mb is { ButtonIndex: MouseButton.Right, Pressed: true })
            {
                def.Cancel!();
                if (def.CancelToast is not null)
                {
                    _campToast.Show(def.CancelToast, CampToast.Ok);
                }
                return;
            }
        }

        if (mb is { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            // RimWorld-style: 左键点敌人 → 攻击；否则走既有点选逻辑。
            if (CanControlPawn(_selectedPawn) && HostileAt(MouseWorldPoint()) is { } enemy)
            {
                _selectedPawn!.CommandAttack(enemy);
                return;
            }
            // 单选：已禁框选，一次只选一个角色（走 SetSelection）。
            FinishSelection();
        }
        else if (mb is { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            // 营地与常规探索关都先从 faux-iso 画布反投影回玩法 cartesian 平面。
            HandleRightClick(MouseWorldPoint());
        }
    }

    /// <summary>
    /// 鼠标画布点到游戏世界点：营地与探索关统一反投影 faux-iso。
    /// </summary>
    private Vector2 MouseWorldPoint()
        => Iso.Unproject(GetGlobalMousePosition());

    /// <summary>
    /// 左键点选（单选）：落点反投影回 cartesian。命中容器 → 保留当前选中不动（交互改由右键前往）；
    /// 否则半径命中最近一个可操控角色 → 单选并开面板；点空白（未命中）→ 取消选择并收面板。
    /// </summary>
    private void FinishSelection()
    {
        Vector2 cart = MouseWorldPoint();

        // 左键点在容器上：不再即时开面板（交互统一走右键前往），保留当前选中，避免误取消。
        if (HitContainerAt(cart) != null)
        {
            return;
        }

        Pawn? hit = _survivors
            .Where(p => CanControlPawn(p) && p.GlobalPosition.DistanceTo(cart) <= p.Radius + 8)
            .OrderBy(p => p.GlobalPosition.DistanceTo(cart))
            .FirstOrDefault();
        SetSelection(hit); // 命中 → 单选并开面板；未命中(null) → 取消选择并收面板
    }

    /// <summary>
    /// 当前场景可接收玩家指令的 Pawn。营地通常只有 Role=Idle；站岗者仅在敌袭已经进入实时战斗后临时可控；
    /// 探索关则是本次远征队的 Role=Expedition。不要直接读取 <see cref="Pawn.IsControllable"/>，那会让探索队恒不可选。
    /// </summary>
    private bool CanControlPawn(Pawn? pawn)
        => pawn is not null
           && _survivors.Contains(pawn)
           && pawn.Alive
           && (_currentLevel is not null
               ? ExplorationInteractionLogic.CanControl(pawn.Role, true)
               : pawn.IsControllable || GuardCombatControlAllowed(pawn) || ProductionCombatControlAllowed(pawn));

    /// <summary>
    /// 守卫平时由岗哨 AI 接管；只有敌袭已经进入战斗才交还玩家。
    /// 人类夜袭在发现/潜行先手结算前仍不可控，不能靠手动移动提前泄露或规避潜入结果。
    /// </summary>
    private bool GuardCombatControlAllowed(Pawn pawn)
        => pawn.Role == PawnRole.Guard
           && _raidGuards.Contains(pawn)
           && (_raidActive || _tutorialActive || _siegeActive
               || (_nightRaidActive && _nightRaidResolved));

    private void SyncGuardCombatControl()
    {
        foreach (Pawn guard in _raidGuards)
        {
            if (IsInstanceValid(guard) && guard.Alive)
                guard.GuardCombatControlEnabled = GuardCombatControlAllowed(guard);
        }
    }

    private bool ProductionCombatControlAllowed(Pawn pawn)
        => pawn.Role == PawnRole.Producing
           && _facilityJobs.FindByWorker(pawn.Id) is not null
           && (_raidActive || _tutorialActive || _siegeActive
               || (_nightRaidActive && _nightRaidResolved));

    private void SyncProductionCombatControl()
    {
        foreach (FacilityJobSlot slot in _facilityJobs.Jobs)
        {
            Pawn? worker = _survivors.FirstOrDefault(p => p.Id == slot.WorkerId);
            if (worker is null || !worker.Alive) continue;
            bool allowed = ProductionCombatControlAllowed(worker);
            bool wasAllowed = worker.ProductionCombatControlEnabled;
            worker.ProductionCombatControlEnabled = allowed;
            if (wasAllowed && !allowed && FacilityRectForSlot(slot.SlotKey) is Rect2 rect)
            {
                worker.ProducingStationing = true;
                worker.CommandMoveTo(NearestEdgeStandPoint(rect, worker.GlobalPosition));
            }
        }
    }

    /// <summary>
    /// 右键：命中容器且有可控选中角色（且导航已就绪）→ 记 <see cref="_pendingInteract"/> 并令其走向容器最近边缘，
    /// 到达后由 <see cref="UpdatePendingInteract"/> 开面板/搜刮；命中容器但无可控选中/导航未就绪 → 忽略（hover 已提示先选中角色）；
    /// 未命中容器 → 地面移动。
    /// </summary>
    private void HandleRightClick(Vector2 cart)
    {
        // 手术参与者收到玩家新命令，视为主动破坏姿态：先明确中断并释放两人，再按本次右键正常下令。
        if (_surgeryJob is { } active && _selectedPawn is { } selected
            && (active.SurgeonId == selected.Id || active.PatientId == selected.Id))
        {
            InterruptTimedSurgery("手术中断：玩家下达了新命令，施术者或病人离开手术姿态。");
        }

        // 门 / 围栏 → **动作菜单**（撬锁 / 静默拆除 / 破坏）。用户拍板：
        //「右键点击敌营门时选择撬锁/破坏，点击围栏时静默拆除/破坏」。
        // 真有得选才弹菜单，否则右键 = 直接干那一件事（没锁的门就是"推开"，不弹菜单）。
        // ⚠️ Shift+右键留给 impl-salvage 的**拆除回收**，不夺它的操作 —— 故这里排除掉 Shift。
        if (_currentLevel is null && _navTested && !Input.IsKeyPressed(Key.Shift)
            && CanControlPawn(_selectedPawn)
            && SiteAt(cart) is { } site)
        {
            OpenSiteMenuOrAct(_selectedPawn!, site);
            return;
        }

        ContainerRef? hit = HitContainerAt(cart);
        if (hit != null)
        {
            if (_navTested && CanControlPawn(_selectedPawn))
            {
                Pawn pawn = _selectedPawn!;
                Vector2 stand = NearestEdgeStandPoint(hit.Rect, pawn.GlobalPosition);

                // 商人停在**大门外**（用户拍板：想做生意，你得开门）。门闩着 ⇒ 寻路过不去。
                // 此时若照常发出前往令，玩家只会等来一句干巴巴的"走不到神秘商人"——那没教会他任何事。
                // 直接告诉他门闩着、该去开门：**说清代价，但不替他开门**（开门的风险要由他自己按下）。
                if (hit.Role == "merchant" && !pawn.CanReach(stand))
                {
                    _campToast.Show("大门闩着，过不去——先派人去开门（门一开，外面听得见）。", CampToast.Bad);
                    return;
                }

                // Shift + 右键 = **拆除令**（门 / 家具）。普通右键仍是原来的交互（开门 / 搜刮 / 开面板），
                // 不夺走任何既有操作。墙不在容器体系里（围栏没登记为可点击物）⇒ 玩家**根本点不到墙**，
                // 这与"墙不可拆、只能砸"天然一致，无需额外拦截。
                bool salvage = Input.IsKeyPressed(Key.Shift);
                if (salvage && !CanSalvageTarget(hit, out string? why))
                {
                    _campToast.Show(why ?? "拆不了。", CampToast.Bad);
                    return;
                }

                pawn.CommandMoveTo(stand);
                _pendingInteract = (pawn, hit, salvage);
                _pendingInteractElapsed = 0;
            }
            return; // 命中容器但不满足前往条件：忽略（不把角色移到容器上）
        }

        // [批次21·impl-medicine·T11] 右键点**自己人** = 医务下令（病人=被点的人，施术者=当前选中的人）。
        // 这是用户要的"选中角色 → 给他用医疗物资"那条路，沿用既有的右键下令范式，不发明新交互。
        // ⚠️ 排在容器判定**之后**：左键点选也是容器优先（FinishSelection），两边保持一致，
        //   免得有人站在储物柜上时右键就打不开柜子了。
        // ⚠️ 不按 IsControllable 过滤病人：**躺床上养病的、正在守夜的都得能被医**——那个闸门管的是"能否接受新指令"，
        //   而病人在这里是被动方，不需要能接指令。施术者才要求 IsControllable（他得腾得出手）。
        if (_navTested && !Input.IsKeyPressed(Key.Shift)
            && CanControlPawn(_selectedPawn)
            && SurvivorAt(cart) is { } patient)
        {
            OpenMedicalFor(patient, _selectedPawn!);
            return;
        }

        // RimWorld-style: 右键点敌人 → 攻击（排在幸存者医疗之后：自己人优先于敌人）。
        if (CanControlPawn(_selectedPawn) && HostileAt(cart) is { } enemy)
        {
            _selectedPawn!.CommandAttack(enemy);
            return;
        }

        IssueMove(cart); // 空地 → 地面移动令
    }

    /// <summary>落点（cartesian）命中的**存活幸存者**（就近取一个）；没命中→null。医务下令的目标查询，
    /// 判定半径与左键点选一致（<see cref="FinishSelection"/>）。布鲁斯（狗）不在此列——他没有伤病集。</summary>
    private Pawn? SurvivorAt(Vector2 cart)
        => _survivors
            .Where(p => p.Alive && p.GlobalPosition.DistanceTo(cart) <= p.Radius + 8)
            .OrderBy(p => p.GlobalPosition.DistanceTo(cart))
            .FirstOrDefault();

    /// <summary>
    /// 落点（cartesian）命中的**敌对单位**（探索关的丧尸/关内Raider、营地的夜间袭扰者）；
    /// 没命中→null。判定半径与幸存者点选一致。供 RimWorld 式左右键攻击链路。
    /// </summary>
    private Actor? HostileAt(Vector2 cart)
    {
        if (_currentLevel is TestExploration level)
        {
            foreach (Actor h in level.LevelHostiles())
            {
                if (h.Alive && h.GlobalPosition.DistanceTo(cart) <= h.Radius + 8)
                    return h;
            }
        }
        foreach (Zombie z in _raidZombies)
        {
            if (z.Alive && IsInstanceValid(z) && z.GlobalPosition.DistanceTo(cart) <= z.Radius + 8)
                return z;
        }
        foreach (Raider r in _nightRaiders)
        {
            if (r.Alive && IsInstanceValid(r) && r.GlobalPosition.DistanceTo(cart) <= r.Radius + 8)
                return r;
        }
        return null;
    }

    // ================= 玩家侧右键动作菜单（撬锁 / 静默拆除 / 破坏） =================
    // 用户拍板：「玩家也可以控制角色，右键点击敌营门时选择**撬锁/破坏**，点击围栏时**静默拆除/破坏**」。
    //
    // 规则全在纯逻辑 SiteActions；本段只做空间执行：弹菜单 → 派人走过去 → 到位后干活。
    // **对称性（硬要求）**：耗时/噪音/伤害**一个数都不在这儿定** ——
    //   撬锁走 DoorLogic.TryPick（与劫掠者同一处判定）、静默拆除走 IntrusionLogic（同上）、
    //   破坏走 StructureDamage.PerHit/Interval（**由武器派生，与丧尸/劫掠者砸墙同一套**）。
    //   玩家没有后门，AI 也没有。

    /// <summary>
    /// 右键命中的门 / 围栏（门优先）。
    /// <para>
    /// <b>⚠️ 被砸穿的围栏格（缺口）也算命中</b> —— 那正是玩家要点开来<b>修</b>的地方（"墙不能建"，补窟窿只能走升级/修补这条路）。
    /// 缺口用<b>不外扩</b>的判定（不像完好的墙那样 Grow(8)）：缺口就是条能走人的通道，判定放宽会抢走"右键穿过缺口"的移动令。
    /// </para>
    /// </summary>
    private CampStructureInstance? SiteAt(Vector2 cart)
    {
        if (DoorAt(cart) is { } d)
        {
            return d;
        }
        foreach (CampStructureInstance s in _structures)
        {
            if (s.State.Kind == CampStructureKind.Door)
            {
                continue; // 民居的门体：活着的归 DoorAt，砸烂的不走"加固墙"这条路（门是装上去的东西，见 FenceUpgradeLogic.CanImprove）
            }
            if (s.IsDoor && !s.Removed)
            {
                continue; // 活着的大门归 DoorAt
            }
            if (s.Rect.Grow(s.Removed ? 0f : 8f).HasPoint(cart))
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>
    /// 这个人对着这处门/围栏，此刻能干什么（纯逻辑出内容；铁丝数从共享库存现取）。
    /// <para>
    /// <b>两张单子拼起来</b>：<see cref="SiteActions.ForFence"/>/<see cref="SiteActions.ForDoor"/> 是「对着<b>一处结构</b>你能干什么」
    /// （撬 / 拆 / 砸——潜入手段），<see cref="SiteActions.ForOwnStructure"/> 是「对着<b>自家这面墙</b>你能干什么」
    /// （加固 / 修补——建造经济）。后者要看的是<b>整条边</b>的状态，不是这一格。
    /// </para>
    /// </summary>
    private IReadOnlyList<SiteActionOption> OptionsFor(Pawn pawn, CampStructureInstance site)
    {
        var opts = new List<SiteActionOption>();
        opts.AddRange(site.IsDoor && !site.Removed
            ? SiteActions.ForDoor(site.Door!.Value, site.Lock, pawn.Faction, isAnimal: false,
                _inventory.MaterialCount(DoorLogic.LockpickMaterialKey))
            : SiteActions.ForFence(pawn.Faction, isAnimal: false,
                site.State.Kind, site.State.Tier, site.Removed));

        if (site.RunId >= 0)
        {
            opts.AddRange(SiteActions.ForOwnStructure(
                pawn.Faction, isAnimal: false, site.State.Kind, RunStates(site), _inventory.MaterialCount));
        }
        return opts;
    }

    private static string SiteName(CampStructureInstance s) => s.IsDoor ? s.DoorName : "围栏";

    // ================= 砌墙：加固（升一档）/ 修补（补回原档）=================
    //
    // 用户拍板：「**墙不能建，只能升级开局自带的围栏**」（理由是防 kill box —— 可自由摆墙 ⇒ 玩家用墙的迷宫
    // 牵着敌人寻路，架空视野锥/噪音/包抄/掩体/岗哨整套系统）。这一段就是那句话**唯一的落点**。
    //
    // ⚠️ **这里没有、也永远不会有"新建一格墙"的代码路径**：本段只对着 _structures 里**已经存在**的格下手
    //（它们只在建图时由 BuildFences 从 camp.json 切出来，全仓唯一一处 `fence: true`）。
    //  被砸穿的缺口之所以补得回来，是因为**那一格本来就在名册上**（Removed=true，位置/档次都还记着）——
    //  玩家能补的洞，只有"原来那儿本来就有墙"的洞。**kill box 的门从这里就是关死的**（有单测钉着，见 FenceUpgradeTests）。
    //
    // 规则/成本/工时全在纯逻辑 FenceUpgradeLogic；本段只做空间执行：派人走过去、逐格立墙、扣料、重建碰撞/导航/掩体/视觉。

    /// <summary>一趟砌墙作业：一个人，一条边，一张<b>逐格</b>的施工计划。</summary>
    private sealed class FenceWorkSession
    {
        public Pawn Worker = null!;
        public List<CampStructureInstance> Run = null!; // 这条边上的每一格（下标对齐 FenceWorkStep.SegmentIndex）
        public FenceWorkPlan Plan = null!;
        public int StepIndex;                           // 干到第几步了
        public double Elapsed;                          // 当前这一格已投入的**工作秒**（中断即作废）
        public string RunName = "";
    }

    /// <summary>这一格所属的**那一条边**（按 <see cref="_structures"/> 的顺序 = 沿墙的顺序）。</summary>
    private List<CampStructureInstance> RunOf(CampStructureInstance site)
        => _structures.Where(s => s.RunId >= 0 && s.RunId == site.RunId).ToList();

    /// <summary>把一条边翻译成纯逻辑看得懂的样子（<b>只有档次和血量，没有位置</b>——规则不该知道墙在哪，见 FenceSegmentState）。</summary>
    private List<FenceSegmentState> RunStates(CampStructureInstance site)
        => RunOf(site)
            .Select(s => new FenceSegmentState(s.State.Tier, s.Removed ? 0d : s.State.HealthFraction))
            .ToList();

    /// <summary>这条边叫什么（"南墙" / "北大门"）——玩家得知道自己在给哪面墙花料。</summary>
    private string RunName(CampStructureInstance site)
    {
        if (site.IsDoor)
        {
            return site.DoorName;
        }

        List<CampStructureInstance> run = RunOf(site);
        Vector2 mid = run.Aggregate(Vector2.Zero, (acc, s) => acc + s.Rect.GetCenter()) / Mathf.Max(1, run.Count);
        Vector2 campMid = _structures.Where(s => s.IsFence).Aggregate(Vector2.Zero, (acc, s) => acc + s.Rect.GetCenter())
                          / Mathf.Max(1, _structures.Count(s => s.IsFence));

        Rect2 first = run[0].Rect, last = run[^1].Rect;
        bool horizontal = Mathf.Abs(last.Position.X - first.Position.X) >= Mathf.Abs(last.Position.Y - first.Position.Y);
        return horizontal
            ? (mid.Y >= campMid.Y ? "南墙" : "北墙")
            : (mid.X >= campMid.X ? "东墙" : "西墙");
    }

    /// <summary>
    /// 下"加固 / 修补"的令：算出这条边的施工计划 → 校验料 → 派人去干第一格。
    /// <para><b>料在每一格<b>完工那一刻</b>才扣</b>（不是开工就扣）——中断即作废当前这一格的进度，但<b>不白亏料</b>。
    /// 与逐件搜刮"已经取出来的件带得走、手上这件作废"是同一条规则。</para>
    /// </summary>
    private void BeginFenceWork(Pawn pawn, CampStructureInstance site, FenceWorkKind kind)
    {
        List<CampStructureInstance> run = RunOf(site);
        List<FenceSegmentState> states = RunStates(site);
        FenceWorkPlan plan = kind == FenceWorkKind.Upgrade
            ? FenceUpgradeLogic.PlanUpgrade(site.State.Kind, states)
            : FenceUpgradeLogic.PlanRepair(site.State.Kind, states);

        if (plan.IsEmpty)
        {
            _campToast.Show(kind == FenceWorkKind.Upgrade ? "这面墙没得升了。" : "这面墙好好的，没什么可补。", CampToast.Bad);
            return;
        }
        if (!FenceUpgradeLogic.CanAfford(plan.TotalCost, _inventory.MaterialCount))
        {
            _campToast.Show(
                $"料不够——还缺 {SiteActions.Bill(FenceUpgradeLogic.Missing(plan.TotalCost, _inventory.MaterialCount))}。",
                CampToast.Bad);
            return;
        }

        _fenceWork = new FenceWorkSession
        {
            Worker = pawn,
            Run = run,
            Plan = plan,
            RunName = RunName(site),
        };
        string what = kind == FenceWorkKind.Upgrade
            ? $"加固{_fenceWork.RunName}（{SiteActions.TierName(plan.TargetTier!.Value)}）"
            : $"修补{_fenceWork.RunName}";
        _campToast.Show(
            $"{pawn.DisplayName} 开始{what}：{plan.Steps.Count} 格 · 料 {SiteActions.Bill(plan.TotalCost)} · 约 {plan.TotalWorkSeconds:0} 秒。",
            CampToast.Ok);
        GoToStep(_fenceWork);
    }

    /// <summary>派人走到"当前这一格"跟前（每干完一格就往下一格挪——他是沿着墙一格一格砌过去的）。</summary>
    private void GoToStep(FenceWorkSession w)
    {
        CampStructureInstance seg = w.Run[w.Plan.Steps[w.StepIndex].SegmentIndex];
        w.Elapsed = 0;
        w.Worker.CommandMoveTo(NearestEdgeStandPoint(seg.Rect, w.Worker.GlobalPosition));
    }

    /// <summary>
    /// 砌墙推进（<b>实时秒 × 工作效率</b>——与搜刮/制作同一条乘子链 <see cref="WorkEfficiencyOf"/>，<b>乘算通则</b>，
    /// 山姆缺两指就砌得慢）。
    /// <para>
    /// <b>为什么是实时秒、不是工作台那套工时制</b>：砌墙是<b>站在墙边</b>干的活。走工时制就意味着它只能在生产相位推进 ——
    /// 而"趁两波尸潮之间把缺口补上"恰恰是这机制存在的全部意义。它跟撬锁/静默拆除/逐件搜刮是同一个形态：
    /// <b>非模态、可派人、站着不动就是暴露、中断即作废</b>。
    /// </para>
    /// <para><b>逐格结算</b>：每立好一格才扣那一格的料。中途被袭营拉走 ⇒ <b>已经立好的格保住</b>，
    /// 手上这格的进度作废、料没扣。半面加固的墙是个诚实的结果，不是 bug。</para>
    /// </summary>
    private void TickFenceWork(double delta)
    {
        if (_fenceWork is not { } w)
        {
            return;
        }
        if (!w.Worker.Alive || !w.Worker.IsControllable || !_survivors.Contains(w.Worker) || w.StepIndex >= w.Plan.Steps.Count)
        {
            _fenceWork = null;
            return;
        }

        FenceWorkStep step = w.Plan.Steps[w.StepIndex];
        CampStructureInstance seg = w.Run[step.SegmentIndex];

        // 还没走到这一格跟前：不推进（他得站在墙边才砌得了墙）。
        if (!seg.Rect.Grow(w.Worker.Radius + PendingArriveMargin).HasPoint(w.Worker.GlobalPosition))
        {
            if (w.Worker.IsNavigationFinished())
            {
                _campToast.Show($"走不到{w.RunName}那一段，活停了。", CampToast.Bad);
                _fenceWork = null;
            }
            return;
        }

        w.Elapsed += delta * WorkEfficiencyOf(w.Worker); // 效率≤0（断了双手的人）⇒ 永远砌不完，这是乘算的必然结果
        if (w.Elapsed < step.WorkSeconds)
        {
            return;
        }

        // ---- 这一格立起来了：扣料 → 换档/补洞 ----
        // 再校验一次料（开工到现在，别的活可能把木料吃掉了）：不够就停在这儿，不凭空造墙。
        if (!FenceUpgradeLogic.CanAfford(step.Cost, _inventory.MaterialCount))
        {
            _campToast.Show($"料用光了——{w.RunName}只砌到一半。", CampToast.Bad);
            _fenceWork = null;
            return;
        }
        foreach (KeyValuePair<string, int> kv in step.Cost)
        {
            _inventory.TrySpendMaterial(kv.Key, kv.Value);
        }
        RaiseSegment(seg, step.TargetTier);

        w.StepIndex++;
        if (w.StepIndex >= w.Plan.Steps.Count)
        {
            _fenceWork = null;
            _campToast.Show(
                w.Plan.Kind == FenceWorkKind.Upgrade
                    ? $"{w.RunName}加固完了（{SiteActions.TierName(step.TargetTier)}）——它们得多啃一阵子了。"
                    : $"{w.RunName}补好了。缺口没了，但那道疤还在。",
                CampToast.Ok);
            return;
        }
        GoToStep(w);
    }

    /// <summary>
    /// <b>把一格墙按某档重新立起来</b>（升级换档 / 补上缺口 / 修回满血，<b>是同一件事</b>）：
    /// 换 <see cref="CampStructureState"/>（新档 + 满血）→ 重画视觉 → 若原先是个洞，则<b>把碰撞体、导航洞、半身掩体一并装回来</b>。
    /// <para>
    /// ⚠️ <b>它只认一个已经在 <see cref="_structures"/> 名册上的格</b> —— 不新增任何结构、不接受任何坐标。
    /// 这就是"修复不等于建墙"这句话在消费层的兑现（全仓生出围栏格的地方只有 <c>BuildFences</c> 那一处 `fence: true`）。
    /// </para>
    /// </summary>
    private void RaiseSegment(CampStructureInstance s, StructureTier tier)
    {
        bool wasHole = s.Removed;
        s.State = new CampStructureState(tier); // 新档、满血（"按新档重立一遍"，旧料一点不退）

        foreach (Node2D v in s.Visuals)
        {
            if (IsInstanceValid(v))
            {
                v.QueueFree();
            }
        }
        s.Visuals.Clear();
        AddOccluderVisual(s.Rect, TierStyle(tier, s.Style), s.Seed, s.Height, s.Cell, s.Visuals);

        if (!wasHole)
        {
            return; // 墙还在，只是换了档：碰撞/导航/掩体原地不动
        }

        // 缺口补回来了：把当初 DestroyStructure 摘掉的那几样一件件装回去。
        s.Removed = false;
        s.Blocking = true;
        if (s.IsDoor)
        {
            s.Door = DoorLogic.ClosedRestingState(s.Barrable); // 重立的大门当然是闩着的
        }

        var body = new StaticBody2D { Position = s.Rect.Position + s.Rect.Size / 2 };
        body.CollisionLayer = s.IsFence ? 0b1_0000u : 0b0100u; // 围栏→层5（看得穿射得穿）；大门→层3 墙（挡一切）
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = s.Rect.Size } });
        _logicLayer.AddChild(body);
        s.Body = body;

        if (s.IsFence)
        {
            // 半身掩体跟着墙一起回来（掩体属性**不随档次变**——围栏所有档位都是半身掩体，impl-cover 的口径）。
            _coverField.Add(s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y,
                CoverLogic.DefaultCoverChance, blocksMelee: true);
        }

        _navHoles.Add(s.Rect);
        _breachCandidatesDirty = true; // 这一格又挡路了 ⇒ 重新进破防候选表（丧尸得重新把它当墙啃）
        RebakeNavigation();
    }

    /// <summary>
    /// 右键门/围栏：<b>真有得选才弹菜单</b>，否则直接干那一件事。
    /// 一扇没锁的门只有"推开"这一个动作（用户拍板「开门只有一种动作」）——给它弹一个只有一项的菜单，
    /// 是拿仪式感惩罚玩家。见 <see cref="SiteActions.NeedsMenu"/>。
    /// </summary>
    private void OpenSiteMenuOrAct(Pawn pawn, CampStructureInstance site)
    {
        IReadOnlyList<SiteActionOption> opts = OptionsFor(pawn, site);
        if (opts.Count == 0)
        {
            return;
        }
        if (SiteActions.NeedsMenu(opts))
        {
            _siteMenuTarget = (pawn, site);
            _actionPopup.ShowFor(SiteName(site), opts, GetViewport().GetMousePosition());
            return;
        }
        if (SiteActions.SoleAction(opts) is { } sole)
        {
            CommandSiteAction(pawn, site, sole);
        }
    }

    /// <summary>菜单里选定了一项：派人走过去（到位后由 <see cref="UpdatePendingSite"/> 执行）。</summary>
    private void OnSiteActionChosen(SiteAction action)
    {
        if (_siteMenuTarget is not { } t)
        {
            return;
        }
        _siteMenuTarget = null;
        CommandSiteAction(t.pawn, t.site, action);
    }

    /// <summary>派人去干这件事：走到目标外沿站位，到了再干。<b>非模态</b>——发完令你就能去控制别人了。</summary>
    private void CommandSiteAction(Pawn pawn, CampStructureInstance site, SiteAction action)
    {
        Vector2 stand = NearestEdgeStandPoint(site.Rect, pawn.GlobalPosition);
        pawn.CommandMoveTo(stand);
        _pendingSite = (pawn, site, action);
        _pendingSiteElapsed = 0;
    }

    /// <summary>轮询"前往干活"的到达（同 <see cref="UpdatePendingInteract"/> 的形态）。</summary>
    private void UpdatePendingSite(double delta)
    {
        if (_pendingSite is not { } p)
        {
            return;
        }
        if (!p.pawn.Alive || !p.pawn.IsControllable || !_survivors.Contains(p.pawn) || p.site.Removed)
        {
            _pendingSite = null;
            return;
        }
        _pendingSiteElapsed += delta;

        bool arrived = p.site.Rect.Grow(p.pawn.Radius + PendingArriveMargin).HasPoint(p.pawn.GlobalPosition);
        if (arrived && p.pawn.IsNavigationFinished())
        {
            _pendingSite = null;
            ExecuteSiteAction(p.pawn, p.site, p.action);
            return;
        }
        if (_pendingSiteElapsed > PendingInteractTimeout)
        {
            _campToast.Show($"前往{SiteName(p.site)}超时，已放弃。", CampToast.Bad);
            _pendingSite = null;
            return;
        }
        if (p.pawn.IsNavigationFinished() && _pendingSiteElapsed > 0.5)
        {
            _campToast.Show($"走不到{SiteName(p.site)}。", CampToast.Bad);
            _pendingSite = null;
        }
    }

    /// <summary>到位了，开干。</summary>
    private void ExecuteSiteAction(Pawn pawn, CampStructureInstance site, SiteAction action)
    {
        switch (action)
        {
            case SiteAction.OpenDoor:
            case SiteAction.CloseDoor:
                ExecuteDoorInteract(pawn, site); // 复用既有开关门（含"关上即闩上"、门口有人不夹人）
                break;

            case SiteAction.PickLock:
                BeginPickLock(pawn, site);       // 复用既有撬锁（实时秒、非模态、失败断铁丝）
                break;

            case SiteAction.SilentDismantle:
                BeginDismantle(pawn, site);
                break;

            case SiteAction.Bash:
                BeginBash(pawn, site);
                break;

            case SiteAction.Upgrade:
                BeginFenceWork(pawn, site, FenceWorkKind.Upgrade); // 加固整条边（升一档）
                break;

            case SiteAction.Repair:
                BeginFenceWork(pawn, site, FenceWorkKind.Repair);  // 补窟窿（回原档，不升档）
                break;
        }
    }

    /// <summary>
    /// 开始<b>静默拆除</b>一段围栏：安静、慢，具体参数以 Wiki 配置为准。
    /// 耗时/伤害走 <see cref="IntrusionLogic"/> —— <b>与劫掠者同一套</b>，玩家没有后门。
    /// </summary>
    private void BeginDismantle(Pawn pawn, CampStructureInstance site)
    {
        double need = SiteActions.DismantleSecondsFor(site.State.Tier);
        _dismantling = (pawn, site, 0.0, need);
        _campToast.Show($"{pawn.DisplayName} 开始悄悄拆这段围栏（约 {need:0} 秒——这期间他站着不动）。", CampToast.Ok);
    }

    /// <summary>
    /// 静默拆除推进（<b>实时秒</b>）。拆满 ⇒ 这一格<b>整段没了</b>（<see cref="SilentDismantleLogic.DamageFor"/>
    /// = 满血一次性抹掉）⇒ 走既有摧毁链路开出一个真的洞。
    /// <para>
    /// <b>不掷骰</b>：花够时间就一定拆开（没有 <c>IRandomSource</c>）。取舍是「<b>时间 + 被撞见的风险</b>」，不是运气 ——
    /// 正好与撬锁（会失败、会断铁丝）形成对照：<b>两条安静路子，一条赌运气，一条赌时间。</b>
    /// </para>
    /// <para>走的是 <b>通用机制层</b> <see cref="SilentDismantleLogic"/>（与劫掠者的
    /// <see cref="DismantleFenceByAi"/> 同一处）—— 不绕道 <c>IntrusionLogic</c>，那是**劫掠者的决策层**（该撬/该拆/该砸），
    /// 是 AI 的心思，玩家不该从它那里取数。</para>
    /// <para><b>中断即作废</b>（走开/死了/结构没了）：与 <c>LootSession</c>（逐件搜刮）、撬锁同一条规则——
    /// 手上的活干到一半被打断，那一段进度就没了。这正是"站着不动 = 暴露"的赌注所在，<b>不给保护</b>。</para>
    /// </summary>
    private void TickDismantle(double delta)
    {
        if (_dismantling is not { } d)
        {
            return;
        }
        bool aborted =
            !d.pawn.Alive || !d.pawn.IsControllable || !_survivors.Contains(d.pawn) || d.site.Removed ||
            !d.site.Rect.Grow(d.pawn.Radius + PendingArriveMargin).HasPoint(d.pawn.GlobalPosition);
        if (aborted)
        {
            _dismantling = null;
            return;
        }

        double elapsed = d.elapsed + delta;
        if (elapsed < d.need)
        {
            _dismantling = (d.pawn, d.site, elapsed, d.need);
            return;
        }

        _dismantling = null;
        d.pawn.EmitLockpickNoise(); // 静默作业的动静（30 —— 惊动不了任何东西，那正是它的意义）
        d.site.State.TakeDamage(SilentDismantleLogic.DamageFor(d.site.State.Tier)); // 满血一次性抹掉
        CombatVfxBurst.SpawnWorkDust(_isoLayer, d.site.Rect.GetCenter(), 1.15f);
        if (d.site.State.IsDestroyed)
        {
            DestroyStructure(d.site); // 复用既有链路：移碰撞 + 重烘焙 ⇒ 一个货真价实的洞
        }
        _campToast.Show($"{d.pawn.DisplayName} 悄悄拆开了一段围栏——没人听见。", CampToast.Ok);
    }

    /// <summary>
    /// 开始<b>破坏</b>（门 / 围栏）：快，但<b>很响</b>（180 —— 半条街都听得见）。
    /// 伤害与节奏<b>由手上的武器派生</b>（<see cref="StructureDamage"/>）—— <b>与丧尸/劫掠者砸墙完全同一套</b>。
    /// </summary>
    private void BeginBash(Pawn pawn, CampStructureInstance site)
    {
        _bashing = (pawn, site, 0.0);
        _campToast.Show($"{pawn.DisplayName} 抡起家伙砸{SiteName(site)}——**很响**。", CampToast.Bad);
    }

    /// <summary>破坏推进（实时秒）：按武器节奏一下一下砸，每下发 180 噪音，砸穿即摧毁开洞。中断即停（进度＝已扣的血，留着）。</summary>
    private void TickBash(double delta)
    {
        if (_bashing is not { } b)
        {
            return;
        }
        bool aborted =
            !b.pawn.Alive || !b.pawn.IsControllable || !_survivors.Contains(b.pawn) || b.site.Removed ||
            !b.site.Rect.Grow(b.pawn.Radius + PendingArriveMargin).HasPoint(b.pawn.GlobalPosition);
        if (aborted)
        {
            _bashing = null;
            return;
        }

        double timer = b.timer - delta;
        if (timer > 0)
        {
            _bashing = (b.pawn, b.site, timer);
            return;
        }

        // 一击：伤害与节奏皆由武器派生（与 AI 破防同一处真源）。
        Weapon w = b.pawn.CurrentAttackWeapon;
        b.site.State.TakeDamage(StructureDamage.PerHit(w));
        CombatVfxBurst.SpawnImpact(_isoLayer, Iso.Project(b.site.Rect.GetCenter()), ImpactVfxKind.Wall, 1.2f);
        b.pawn.EmitBreachNoise(); // 180 —— 攻方自己制造的动静会把东西招来，玩家也一样
        if (b.site.State.IsDestroyed)
        {
            _bashing = null;
            DestroyStructure(b.site);
            _campToast.Show($"{SiteName(b.site)}被砸开了——刚才那阵动静，附近全听见了。", CampToast.Bad);
            return;
        }
        _bashing = (b.pawn, b.site, StructureDamage.Interval(w));
    }

    private void IssueMove(Vector2 cartPos)
    {
        _pendingInteract = null; // 新的地面移动令：取消未完成的前往交互
        InterruptLootIf(_selectedPawn); // 走开即停手——"拿到第几件就跑"这个决策，就是在这一下右键里做出来的
        // [批次21·impl-bedrest] "去那边站着"和"继续躺着养病"是矛盾的两件事 ⇒ 支使他走 = 他起床。
        // 不叫醒的话，他会**人走了却还占着床、休养流水账还在给他记分**（见 BedrestLogic.WakesOnCommand）。
        WakeIfBedrest(_selectedPawn, targetContainerRole: null);
        // 单选：仅当前主选角色接受移动指令（无选中则无操作）。
        _selectedPawn?.CommandMoveTo(cartPos);
    }

    /// <summary>容器命中查询：落点（cartesian）命中某容器则返回它，否则 null（右键前往/悬停辨识共用）。</summary>
    // 普通 for 循环替代 LINQ FirstOrDefault：后者每帧新建捕获 cart 的闭包（稳定 GC 压力）；
    // UpdateHover 每帧调本方法，改 for 后零分配，扫描一小把容器 CPU 可忽略。
    private ContainerRef? HitContainerAt(Vector2 cart)
    {
        // 🔴 **出门在外，营地的容器一概点不到。**
        // <c>_containers</c> 是营地的登记表，探索期间它并没有被清空（营地只是隐藏了、结构都还在）。
        // 而探索关用的是**同一套 cartesian 坐标系** ⇒ 不拦的话，玩家在医院里右键点一下，
        // 命中的可能是**营地里某扇门/某张床**（坐标恰好重叠），于是队员在医院当场"上床养病"。
        // 关内的门和发现点是可点容器；营地容器在关里一概不参与命中。
        bool inLevel = _currentLevel != null;

        for (int i = 0; i < _containers.Count; i++)
        {
            if (inLevel && _containers[i].Role != LevelDoorRole)
            {
                continue;
            }
            if (_containers[i].Rect.Grow(8f).HasPoint(cart))
            {
                return _containers[i];
            }
        }

        // 发现点不登记进营地容器表（它们随关卡销毁、部分允许重复搜），由关卡持有几何与去重。
        if (inLevel && _currentLevel is TestExploration level
            && level.TryGetDiscoveryTarget(cart, out string discoveryId, out Rect2 discoveryRect,
                out string label, out NarrativeTrigger trigger))
        {
            return new ContainerRef
            {
                Name = discoveryId,
                Rect = discoveryRect,
                Role = LevelDiscoveryRole,
                Hint = label,
                DiscoveryTrigger = trigger,
            };
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
        if (!CanControlPawn(pawn))
        {
            _pendingInteract = null; // 兜底取消（SetSelection/相位切换等路径通常已提前清）
            return;
        }

        _pendingInteractElapsed += delta;
        bool arrived = pend.target.Rect.Grow(pawn.Radius + PendingArriveMargin).HasPoint(pawn.GlobalPosition);
        if (arrived && pawn.IsNavigationFinished())
        {
            _pendingInteract = null;                 // 先清，避免 ExecuteContainerInteract 内暂停后重入
            if (pend.salvage)
            {
                BeginSalvageAt(pend.pawn, pend.target);
            }
            else
            {
                ExecuteContainerInteract(pend.pawn, pend.target);
            }
            return;
        }
        if (_pendingInteractElapsed > PendingInteractTimeout)
        {
            _campToast.Show($"前往{pend.target.Name}超时，已放弃。", CampToast.Bad);
            _pendingInteract = null;
            return;
        }
        // 导航结束却没到位 = 走不到（首帧 nav 同步滞后误报靠运行时宽限过滤）。
        if (pawn.IsNavigationFinished() && _pendingInteractElapsed > 0.5)
        {
            _campToast.Show($"走不到{pend.target.Name}。", CampToast.Bad);
            _pendingInteract = null;
        }
    }

    /// <summary>
    /// 悬停辨识：反投影鼠标 → 命中容器则跟随鼠标显示按 role 定制的一行提示（无选中角色时追加"先选中角色"）；
    /// 未命中 / 面板打开 → 隐藏。探索中只查询已被视野揭示的关内门/发现点，不会命中隐藏营地容器。
    /// 与 <see cref="UpdatePendingInteract"/> 各自独立，互不干扰。
    /// </summary>
    private void UpdateHover()
    {
        if (_stashOpen || _craftingOpen || _medicalOpen)
        {
            _hud.HideHoverLabel();
            return;
        }
        ContainerRef? hit = HitContainerAt(Iso.Unproject(GetGlobalMousePosition()));
        if (hit == null)
        {
            // 围栏不是容器（故不能被普通搜刮/拆除按钮误命中），但仍是可操作的结构：
            // SiteAt 负责右键动作菜单，hover 这里补上它已有的 authored 简介与当前耐久，
            // 让「有数据但玩家看不见」的结构文案真正进入消费链。
            if (SiteAt(Iso.Unproject(GetGlobalMousePosition())) is { } site)
            {
                string action = site.Removed ? " · 右键前往修补" : " · 右键查看/操作";
                _hud.ShowHoverLabel(
                    CampHoverText.Structure(SiteName(site), site.State.Tier, site.State.Hp, site.Removed) + action,
                    GetViewport().GetMousePosition());
                return;
            }
            _hud.HideHoverLabel();
            return;
        }
        _hud.ShowHoverLabel(HoverTextFor(hit, _selectedPawn != null), GetViewport().GetMousePosition());
    }

    /// <summary>容器 role → 悬停提示文案。loot 已搜过标"已搜刮"（无操作提示）；其余可交互，无选中角色时追加"（先选中角色）"。</summary>
    private string HoverTextFor(ContainerRef c, bool hasSelection)
    {
        string noSel = hasSelection ? "" : "（先选中角色）";
        if (c.Role == LevelDiscoveryRole)
        {
            string label = string.IsNullOrWhiteSpace(c.Hint) ? "发现点" : c.Hint;
            return c.DiscoveryTrigger == NarrativeTrigger.Click
                ? $"{label} · 调查点 · 选中角色后右键前往调查{noSel}"
                : $"{label} · 发现点 · 选中角色后右键前往{noSel}";
        }
        if (c.Role == "door" && DoorAt(c.Rect.GetCenter()) is { } door)
        {
            return DoorHoverText(door, hasSelection);
        }
        // 关内的门（[T49] 废弃医院）：状态归关卡。提示里**写明关门是干什么用的**——
        // 玩家不会自己想到"关门可以把丧尸挡在门后"，除非你告诉他。
        if (c.Role == LevelDoorRole && _currentLevel is TestExploration lvl && lvl.LevelDoorState(c.Name) is { } ls)
        {
            // 锁着的门：撬（安静但要铁丝）——把当前铁丝数直接报出来，让玩家在点之前就知道够不够撬。
            if (ls == DoorState.Locked)
            {
                int wire = _inventory.MaterialCount(DoorLogic.LockpickMaterialKey);
                return $"{c.Name}（{LockLabel(lvl.LevelDoorLockTier(c.Name))}）· 选中角色后右键前往撬锁 —— 安静但要铁丝（×{wire}），失败断一根" +
                       $"{(hasSelection ? "" : "（先选中角色）")}";
            }
            string act = DoorLogic.Blocks(ls) ? "推开" : "关上（把门后的东西挡住）";
            return $"{c.Name} · 选中角色后右键前往{(hasSelection ? "" : "（先选中角色）")} · {act} · 有动静（{NoiseLogic.DoorNoiseRadius:0} 半径）";
        }
        string hint = c.Role switch
        {
            "workbench" => $"工作台 · 选中角色后右键前往{noSel}",
            // 床：谁躺着/空着/该干什么（批次21·impl-bedrest）。
            "bed" => BedHoverText(c, hasSelection),
            "modbench" => $"改装台 · 武器改造只能在这儿做 · 选中角色后右键前往{noSel} · Shift+右键拆走",
            // [批次21·T14] ⚠️ 这里**不写**"一份饭要多少热量"——那是玩家该自己试出来的（见 CookingLogic 类注）。
            "cookstation" => $"烹饪台 · 做饭只能在这儿做 · 选中角色后右键前往{noSel} · Shift+右键拆走",
            // 沙袋：半身掩体。提示里把"紧贴才算"和"敌人也能用"说清楚——概率由 Wiki 配置读取。
            "sandbag" => "沙袋 · 贴着它挨远程有概率无效（绕到你背后就白垒了，敌人也能蹲它后面）· Shift+右键拆走",
            // [批次21·T26] 陷阱：几率按"场上第几个"递减 ⇒ 提示里**把这一个的当前几率报出来**。
            // 玩家看不见这条递减曲线的话，第 7 个陷阱和第 1 个在他眼里长得一模一样（而收益差了六倍）。
            "trap" => TrapHoverText(c),
            // [T75] 捕鸟陷阱：同圈套，几率按"场上第几个"递减 ⇒ 把当前几率报出来（曲线在地上没痕迹）。
            "bird_trap" => BirdTrapHoverText(c),
            // [T72] 菜园：进度在地上没痕迹 ⇒ 把"熟了几颗/种了几颗/最快几天熟"直接摊在提示里。
            "cropplot" => CropPlotHoverText(c),
            // [T67] 宰杀设施：把档位 + 刀槽现状报出来（正文在 CampMain.Butchery.cs）。
            "butcher" => ButcherHoverText(c),
            // [批次21·T25] 桌子：室内的半身掩体（用户拍板）。把"紧贴才算"和"敌人也能用"说清楚——同沙袋，
            // 概率与减速幅度由 Wiki 配置读取。顺带说清它不挡路（跨得过去，只是会减速）。
            "table" => "桌子 · 贴着它挨远程有概率无效（绕到你背后就白摆了，敌人也能蹲它后面）· 跨得过去但会减速 · Shift+右键拆走",
            "sofa" => "沙发 · 木椅升级版 · 坐着读书读速×1.12、恢复速度×1.09 · 跨得过去但会减速 · Shift+右键拆走",
            "radio" => $"收音机 · 选中角色后右键前往{(RadioMainline.IsDecisionAvailable(_storyFlags) ? "抉择" : "收听")}{noSel}",
            "storage" => $"储物柜 · 选中角色后右键前往{noSel}",
            // 商人在**门外**：把代价直接写在提示里（大门闩着时先说"得开门"，别让玩家点了才发现走不过去）。
            "merchant" => MerchantGateBarred()
                ? $"神秘商人（在大门外）· 大门闩着——得先开门才能出去交易{noSel}"
                : $"神秘商人（在大门外）· 选中角色后右键出门交易 —— 门开着，营地没有防线{noSel}",
            "rubble" => RubbleHoverText(c, noSel),
            // 尸体：扒过了 → 就剩一具尸体（无操作提示）。否则——
            //   带叙事的（祖母）：没看过那段叙事 → 提示"调查"；看过之后 → 提示"搜刮"（衣服还在她身上）。
            //   战场上倒下的（丧尸/劫掠者/自己人，NarrativeId 为空）：没有叙事那一步，直接就能搜。
            "corpse" => _containerLoot.IsSearched(c.Name)
                ? $"{c.Name}"
                : string.IsNullOrEmpty(c.NarrativeId) || _storyFlags.Has("seen_" + c.NarrativeId)
                    ? $"{c.Name}{CorpseGoods(c)}{LootTimeHint(c.Name)}{CorpseDecayHint(c.Name)} · 选中角色后右键前往搜刮{noSel}"
                    : $"{c.Name} · 选中角色后右键前往调查{noSel}",
            _ => _containerLoot.IsSearched(c.Name)
                ? $"{c.Name} · 已搜刮"
                : $"{c.Name}{LootTimeHint(c.Name)} · 选中角色后右键前往{noSel}",
        };
        // 家具目录中的 authored 简介（含床/桌子/沙发/工作台/柜子等）统一在这里消费。
        // 特殊 role 自己提供规则提示，简介作为尾缀叠加；非家具容器由 helper 原样返回。
        return CampHoverText.AppendFurnitureDescription(c.Name, hint);
    }

    /// <summary>
    /// 悬停时就写明「还剩 N 件 · 约 M 秒」——<b>走过去之前</b>就该知道这一趟要站多久。
    /// 决定"值不值得冒这个险"必须发生在冒险**之前**，不是站到柜子前才发现要等待配置时长。
    /// 搜过一半的容器只报剩下的（前一趟拿走的不再计时）。已搜空/未登记 → 空串。
    /// </summary>
    private string LootTimeHint(string container)
    {
        int n = _containerLoot.RemainingCount(container);
        if (n <= 0)
        {
            return "";
        }
        // 派谁去搜，就按谁的手算（同一条乘子链：操作能力 × 山姆光环…）。没选人时按健全者 1.0 报个基准。
        float perItem = LootSession.EffectiveSecondsPerItem(
            _selectedPawn is { } sel ? LootEfficiencyOf(sel) : 1d);   // [T61] 含耗子的搜刮专属乘子——UI 报的秒数必须与实际一致
        string half = _containerLoot.IsPartiallySearched(container) ? "还" : "";
        return float.IsFinite(perItem)
            ? $" · {half}剩 {n} 件（约 {n * perItem:0} 秒）"
            : $" · {half}剩 {n} 件（这双手搜不动）";   // 断手：如实说，别印假秒数
    }

    /// <summary>
    /// 尸体身上有什么，<b>悬停时直接写出来</b>（如「· 牛仔外套、长袖布衣、长裤」）。
    /// <para>
    /// 这是"<b>所见即所得</b>"在 UI 上的落点：用户口径是「丧尸穿的是什么，就原原本本的写出来，可以直接扒下来」——
    /// 既然掉落零掷骰、穿什么扒什么，那玩家就该<b>在走过去之前</b>看得见这一趟值不值。
    /// 藏着不说等于把"看货下手"又变回了开盲盒。
    /// </para>
    /// <para>
    /// <b>不新增美术</b>（尸体目前是占位块，给"有货的尸体"画个高光属于过度设计）：一行悬停文字已经把信息给全了。
    /// 超过 <c>MaxShown</c> 件只列前几件 + "等 N 件"，免得精英尸体拉出一条横跨屏幕的提示。
    /// </para>
    /// </summary>

    /// <summary>
    /// 悬停时就写明这具尸体<b>还能躺几个相位</b>——「先扒哪一具」「值不值得为它多待一个相位」是纯粹的决策信息，
    /// 藏起来只会制造挫败感（"我明明记得那边还有一具"），制造不出张力。张力来自**知道时间不够**。
    /// <para>
    /// 只报引擎里真有的状态（<see cref="CorpseDecay.PhasesRemaining"/>），不发明"腐烂度"之类的假数值。
    /// 祖母那具 authored 尸体不在 <see cref="CorpseYard"/> 里（返回 -1）⇒ <b>没有倒计时</b>，她本来就永远躺在那儿。
    /// </para>
    /// </summary>
    private string CorpseDecayHint(string containerId)
    {
        int left = _corpseYard?.PhasesRemainingFor(containerId) ?? -1;
        return left switch
        {
            1 => " · 快烂没了，最后半天",
            2 => " · 还能躺两个半天",
            3 => " · 还能躺三个半天",
            _ => "",   // -1=不在场上（剧情尸体/已清走）；0=这次相位切换就没了（提示已无意义）
        };
    }

    private static string CorpseGoods(ContainerRef c)
    {
        const int MaxShown = 3;
        if (c.Loot.Count == 0)
        {
            return "";
        }
        var names = c.Loot.Select(l => l.RefId).ToList();
        string shown = string.Join("、", names.Take(MaxShown));
        return names.Count > MaxShown ? $" · {shown} 等 {names.Count} 件" : $" · {shown}";
    }

    /// <summary>
    /// 选中的唯一写入口（单选事实源）：先清空旧选中，再选中 p（非空即 ≤1），置 <see cref="_selectedPawn"/>，
    /// 并驱动右侧角色面板（p 非空→检视，null→收起）与卡牌栏选中高亮刷新。所有改选中都必须走这里。
    /// </summary>
    private void SetSelection(Pawn? p)
    {
        _pendingInteract = null; // 改选中：取消未完成的前往交互
        // ⚠️ **改选中角色绝不中断任何人的搜刮**（用户拍板：「允许玩家控制一个角色去搜刮转物品，**然后控制另一个角色**」）。
        // 搜刮是**派下去的活**，不是锁住玩家的模态交互：派 A 去掏尸体，切去控制 B 放哨，A 在后台自己接着掏。
        // 曾在此处写过"改选人 = 撇下那位、停手"——**那是错的，等于夺走控制权**，已删，别复辟。
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
        // 路径线：全体幸存者恒画，选中者只是画得更醒目（更粗更实），未选中者细而半透明。
        _pathOverlay?.SetSelected(_selectedPawn);
    }

    /// <summary>
    /// 该画路径线的己方单位：**活着的幸存者全员**（不只选中那个 —— 用户口径「己方可控制角色的路径都要展示」）。
    /// <para>
    /// 刻意**不**按 <see cref="Pawn.IsControllable"/>（=Role 为 Idle）过滤：那是"此刻能否接受玩家新指令"的瞬时闸门，
    /// 守夜/读书/就餐途中的人会被它挡掉——而他们恰恰正在走路，正是玩家最需要看见的那几条线（谁挡谁的路、
    /// 谁的路线横穿丧尸视野锥、谁贴着墙走会惊动屋里的东西）。人还是玩家自己的人，路径就该看得见。
    /// </para>
    /// 布鲁斯（<see cref="Dog"/>）不入列：纯 AI 跟随，玩家无法直接下令移动。敌方（丧尸/劫掠者）永不入列——作弊级信息。
    /// 站着不动/已到达者由 <see cref="PathOverlay"/> 按"有无未走完的导航路径"自动略过，此处不必筛。
    /// </summary>
    private IEnumerable<Actor> PathVisibleUnits()
    {
        foreach (Pawn p in _survivors)
        {
            if (p.Alive)
            {
                yield return p;
            }
        }
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
        // 皮特（青春期大男孩代谢快）走 PetePerk.ResolveHungerPhase 变体：额外饥饿消耗概率与幅度由 Wiki 配置提供；
        // 随机走生产 _mealRng（SystemRandomSource），非皮特不触 rng（PetePerk 内部 isPete 门控）、行为与旧 ResolveHungerPhase 逐位一致。
        for (int i = 0; i < living.Count; i++)
        {
            Pawn diner = living[i];
            if (diner.Perks.IsPete)
            {
                PetePerk.ResolveHungerPhase(diner.Hunger, ration.Fed[i], _mealRng, isPete: true);
            }
            else if (diner.Perks.IsChristine)
            {
                // 克莉丝汀「懂得挨饿」：本相位本会掉饥饿时，以本级 Wiki 配置几率跳过这次衰减。
                // 随机走同一 _mealRng；非克莉丝汀不触 rng（ChristinePerk 内部 isChristine 门控）。
                ChristinePerk.ResolveHungerPhase(diner.Hunger, ration.Fed[i], _mealRng, isChristine: true, ChristineLevelNow());
            }
            else
            {
                diner.ResolveHungerPhase(ration.Fed[i]);
            }
        }
        // 补餐回升：份数≥2（SecondFed）→ +1（clamp 到各自上限；饿死终态由 Feed 内部守卫）。
        for (int i = 0; i < living.Count; i++)
        {
            if (phase.SecondFed[i])
            {
                living[i].ServeSecondMeal();
            }
        }
        // 皮特升级轴 L1→L2「连续五天饥饿≥3」：每次聚餐把本次最终饥饿值喂给 PetePerk.RecordPhaseHunger
        // （≥3 续连续、<3 清零、达 10 餐 latch L2 永久旗标）。放在补餐回升之后 ⇒ 记的是本次结算完的最终刻度。
        foreach (Pawn diner in living.Where(d => d.Perks.IsPete))
        {
            PetePerk.RecordPhaseHunger(_storyFlags, diner.Hunger.Value);
        }

        // 饿死：刻度归 0 者走统一死亡路径（Died 事件会改 _survivors，先收集再逐个处理）。
        foreach (var starved in living.Where(d => d.IsStarvedToDeath).ToList())
        {
            starved.StarveToDeath();
        }

        // 布鲁斯（狗）进食：**不上桌**——不入分配面板/坐席/气泡，人类分配后从余粮自动喂。每次聚餐 -1；
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
    /// （坐着必冒、站着触发概率按 <see cref="MealBubbleDelivery"/> 配置缩放，漏听线索/支线的惩罚由概率承载）→
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

        // 到位后冒世界气泡（坐着必冒、站着倍率由配置提供）。
        foreach (Pawn p in eaters)
        {
            p.Stationing = false;
            if (p.Alive && seatedFlags.GetValueOrDefault(p))
                p.VisualActivity = PawnVisualActivity.Sitting;
        }
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
            p.VisualActivity = PawnVisualActivity.None;
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
    /// 无名气泡 → 随机未分派吃饭者；每条按该吃饭者坐/站掷点（站着倍率由 Wiki 配置提供）决定是否真的冒出来。
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

        // 掷点冒泡（坐着必冒、站着倍率由配置提供）。
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

        // 南丁格尔护士三级特长（[SPEC-B13-补]）：营地卫生减免全营感染率；等级效果与合并口径由 Wiki 配置提供。
        // **乘算**（CLAUDE.md 铁律，禁加算）。判据见 NightingalePerk.CampInfectionMultiplier。
        // 默认只营地内休养的伤口吃减免（本处即营地每昼夜健康推进；探索关实时层伤口不经此，故天然满足"只营地伤口吃"，标待确认）。
        Pawn? nightingale = _survivors.FirstOrDefault(s => s.Perks.IsNightingale);
        int nurseLevel = NightingalePerk.LevelOf(_storyFlags); // 等级由持久化台数派生（她死后仍在，但下方 aliveInCamp 门控 L2）
        bool nurseAliveInCamp = nightingale is { Alive: true } && nightingale.Role != PawnRole.Expedition; // 在营存活=活着且未外出探索
        bool nurseL3Legacy = _storyFlags.Has(NurseRecruit.L3LegacyFlag);
        double infectionMult = NightingalePerk.CampInfectionMultiplier(nurseLevel, nurseAliveInCamp, nurseL3Legacy)
            * BookPassiveEffects.CampInfectionChanceMultiplier(
                new[] { AnyCamperHasReadBook(BookLibrary.MedicalFacilityStandardsId) });

        // 南丁格尔床铺恢复加成与山姆本人恢复速度加成分别在每个患者结算时门控：前者是南丁格尔 L2 且在营，
        // 后者是山姆本人 L2 起。两者都只作用恢复，不改感染几率。
        double healSpeedMult = SamPerk.CampHealSpeedMultiplier(SamLevelNow());

        foreach (Pawn p in living)
        {
            // [批次21·impl-bedrest] 休养/睡床由**整日一个布尔**换成**按相位累计的占比**（RestLedger，见 CampMain.Bedrest.cs）。
            //
            // 旧写法 `resting = p.Role != PawnRole.Guard` 有三处错：
            //   ① 本方法在**黎明**跑，而 PawnRoleManager 在聚餐流程不重排角色 ⇒ 那个布尔读到的是**昨夜**的角色，
            //      白天在营地睡的三个相位（出发/探索/返回）对它**零贡献** —— 这就是"白天睡觉吃不到治疗加成"的根因；
            //   ② 床没建模，`restedInBed = resting` 是张空头支票；
            //   ③ 出门探险的人和挑灯读书的人也被算作"卧床休养"。
            // 现在三处都由流水账据实记账：每过一个相位记一笔（谁在睡、睡的是床还是地铺），黎明结账。
            (double restFraction, double bedFraction) = RecoveryFractionsFor(p);
            double personalHealSpeedMult = healSpeedMult
                * SamPerk.PersonalHealSpeedMultiplier(SamLevelNow(), p.Perks.IsSam);
            double bedHealBonusPct = NightingalePerk.BedSleepHealBonusPct(nurseLevel, nurseAliveInCamp);
            HealthTickResult r = p.AdvanceHealthDay(_healthRng, resting: false, restedInBed: false,
                infectionMult, personalHealSpeedMult, restFraction, bedFraction, bedHealBonusPct);
            p.Rest.Reset(); // 结完账，开下一天的流水

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

            // [SPEC-B14/终稿·护栏①] 感染双进度竞速：黎明扣一份指派药（或断药中断），推进 感染进度 vs 治疗进度 一整日(dt=1.0)。
            AdvanceInfectionRaceDay(p, notes, toKill);
        }

        foreach (Pawn p in toKill)
            p.DieOfWounds();

        if (notes.Count > 0)
            _campToast.Show(string.Join("；", notes), CampToast.Bad);
    }

    /// <summary>
    /// [SPEC-B14/终稿·护栏①] 感染双进度竞速一整日推进（每昼夜黎明，dt=1.0）：
    ///   · 无感染 → 若挂着疗程则静默清疗程，返回；
    ///   · 有感染：按疗程指派扣一份药（<see cref="InfectionCourseLogic.DecideDose"/>）——缺药则清疗程+醒目断药提示（防冤死）；
    ///   · 推进竞速一整日（<see cref="Pawn.AdvanceInfectionRace"/>，用药期间按档减缓恶化+累进治疗进度）：治愈清疗程+记事 / 坏疽截肢记事 / 败血症致死入 <paramref name="toKill"/>。
    /// 注：相位级细分（每昼夜多次）是后续项；当前整日推进——全程 double 不取整，累积数值等价，仅跨阈值解析在整日边界（草药膏胜负窗见回报待办）。
    /// </summary>
    private void AdvanceInfectionRaceDay(Pawn p, List<string> notes, List<Pawn> toKill)
    {
        bool infected = p.Health.Conditions.Any(c => c.Type == HealthConditionType.Infection);
        if (!infected)
        {
            if (p.InfectionTreatmentMedKey is not null)
                p.ClearInfectionTreatment(); // 感染已不在（治愈/截肢清除）→ 静默结束疗程
            return;
        }

        string? medKey = p.InfectionTreatmentMedKey;
        int stock = medKey is null ? 0 : CraftingService.MaterialTotal(_inventory.ByCategory(ItemCategory.Material), medKey);
        InfectionDoseDecision dose = InfectionCourseLogic.DecideDose(p.Health, medKey, stock);
        if (dose.ConsumedDose)
            ConsumeMaterials(new[] { medKey! });
        if (dose.Step == InfectionCourseStep.OutOfStock)
        {
            p.ClearInfectionTreatment();
            notes.Add($"{p.DisplayName} 的{Materials.Find(medKey!)?.DisplayName ?? medKey}用完了，治疗中断"); // 断药醒目提示
        }

        // 山姆 3 级光环·全营感染条上升速度乘子（与用药的 WorsenMultiplier 相乘、互不吞没；失效时回中性值）。
        InfectionRaceResult rr = p.AdvanceInfectionRace(1.0, dose.Medicated, dose.Medicine,
            SamPerk.CampInfectionWorsenMultiplier(SamLevelNow()));
        if (rr.Cured)
        {
            p.ClearInfectionTreatment();
            notes.Add($"{p.DisplayName} 的感染痊愈了");
        }
        else if (rr.Outcome == ConditionOutcome.Death) // [SPEC-B14-补7] 感染到顶=立刻死（不再自动截肢；保命须玩家主动截肢）
        {
            notes.Add($"{p.DisplayName} 因感染不治身故");
            if (!toKill.Contains(p))
                toKill.Add(p);
        }
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

    /// <summary>聚餐结束后的流程推进（黎明→白天备战；黄昏→睡眠过渡）。抉择面板延迟场景亦复用它收尾。</summary>
    private void AdvanceAfterMeal()
    {
        RecordPlaytestHalfDay();
        switch (_clock.CurrentPhase)
        {
            case DayPhase.DawnMeal:
                _nightEventSchedule = NightEventSchedule.None;
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
        _campVisionMask.SetSourceProvider(pos => _campLights.StrongestAt(pos.X, pos.Y, CurrentHandheldLights()));
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
        // 「南逃谢幕」强制终局序列进行中：营地已被屠、玩家在南逃走廊操作——一切聚餐/相位记账/清尸/陷阱副作用一律停摆
        // （序列自管画面与时标，末尾走 EndingPanel 谢幕）。放在最前，避免走廊行走途中若发生相位切换再触发营地逻辑。
        if (_southEscapeActive)
            return;

        _pendingInteract = null; // 相位切换：取消未完成的前往交互
        // 相位边界先 flush 分钟账：此刻角色仍属于离场相位，避免边界最后一分钟被新角色状态吃掉。
        TickBedSleepMinutes();

        UpdateFatigueTimers(phase); // 批次6：被唤醒者次相位疲劳 debuff 的施加/过期（每相位切换维护）
        // 尸体过三个半天烂没（用户拍板）：只在清晨/黄昏推进计数，清理走 CorpseYard 的唯一回收出口
        // ⇒ 必经 Recycled → DeregisterCorpseContainer（可搜刮点 + 藏物登记一并注销，不泄漏）。
        // 所有战斗尸体统一按三个半天腐烂；祖母等 authored 尸体不进该时钟，永不被清理。
        if (CorpseDecay.AdvancesOn(phase))
        {
            _corpseYard?.AdvancePhase();
            SweepExplorationCorpses();
        }
        // [批次21·T26 修复] 圈套陷阱：跟着**昼夜段**掷点（用户拍板，与吃饭/饥饿同频）。
        // 具体掷点频率与命中率由 TrapLogic/Wiki 配置裁定；不要在消费层复制数字。
        // 哪两个相位掷由 TrapLogic.RollsOnPhase 唯一裁定（与每日期望换算焊死同一规则，见 TrapLogic.RollsPerDay）。
        // 抓到的老鼠/兔子直接入共享库存；期望值由 TrapLogic/Wiki 配置决定。
        // 场上一个陷阱都没有时彻底静默（不掷点、不弹提示）。正文在 CampMain.Traps.cs。
        if (TrapLogic.RollsOnPhase(phase))
        {
            ResolveTrapsForPhase();
            ResolveBirdTrapsForPhase();   // [T75] 捕鸟陷阱同频掷点（此前整条未接，鸟从来出不来），正文在 CampMain.BirdTrap.cs
        }
        RefreshPhaseVisuals(phase);
        _expeditionPanel.Visible = false;
        _worldMapPanel.Visible = false;
        _guardPanel.Visible = false;
        _readingPanel.Visible = false;
        _returnWarningPopup.Visible = false;
        _mealAllocPanel.Visible = false;

        // 克莉丝汀累计 3 次"暂不"后不立即走：排期到下一次昼夜交替（进入聚餐边界流程）时自行离开。
        // 置于结算前，使她不再计入本餐用餐者。走"自愿离开"清理（非 Died，不触发全灭判定）。
        if (DayPhaseSegments.IsMeal(phase)
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
                AdvanceChristineDay(); // 克莉丝汀在营存活又一昼夜 → 「巧舌如簧」升级推进（她在营活着才 +1，L2＝存活三天）
                // 电台主线：回复军方后（回复日+2）白天军袭到期 → 屠营+南逃谢幕强制终局序列接管画面，跳过本次聚餐。
                if (TryTriggerMilitaryRaid())
                    break;
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
                // [T57] 「去过」在**真的踏进这张图**的这一刻记账（不是选中的那一刻——选了又取消/半路折返不算去过）。
                // 它是网状解锁的两个条件之一；另一半（探索度达到 Wiki 门槛）由关内搜刮写 _storyFlags 自动长出来。
                if (!string.IsNullOrEmpty(_pendingDestination))
                    _visitedDestinations.Add(_pendingDestination);
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
                {
                    _nightEventSchedule = NightEventSchedule.Restore(NightEventKind.ScriptNight, 0, true);
                    BeginChristineTutorial();
                }
                // 皮特事件：第 7 夜一开局脚本触发（男孩敲门求救 + 三选一）。同教学关口径独占本夜（进 else 链 ⇒
                // 抑制围攻/夜袭，脚本夜不叠加常规威胁）；正文在 CampMain.PeteEvent.cs。
                else if (_clock.Day == 7 && !_storyFlags.Has(PeteEventDoneFlag))
                {
                    _nightEventSchedule = NightEventSchedule.Restore(NightEventKind.ScriptNight, 0, true);
                    BeginPeteRescueEvent();
                }
                // 尸潮时限到期(day>=DeadlineDay)：**无限丧尸屠营 → 随机一名幸存者半残南逃 → 南逃谢幕**（用户 authored
                // 强制终局，与军袭同一套单角色南逃谢幕，只是触发源＝丧尸[HordeSiege]。已推翻旧"可玩无限围攻直至全灭"路由，
                // 详见 TryTriggerHordeSiegeEnding；旧 TriggerHordeSiege 保留为遗留、不再从 day-40 可达）。
                // 时限与教学关(第 2 夜)天数相去甚远，不冲突；发现与否不影响触发（Evaluate 到期一律 Arrived）。
                // 终局冻结门控：主线推进到终局抉择点后置 EndgameFreezeFlag → 结局流程接管，尸潮终局不再触发（置位方留待主线系统）。
                else if (HordeTimeline.ShouldTriggerSiege(
                    _clock.Day, _storyFlags.Has(HordeTimeline.SightedFlag), _storyFlags.Has(HordeTimeline.EndgameFreezeFlag)))
                {
                    _nightEventSchedule = NightEventSchedule.Restore(NightEventKind.HordeNight, 0, true);
                    TryTriggerHordeSiegeEnding();
                }
                else
                    _nightEventSchedule = NightEventSchedule.Roll(_nightEventRng);
                break;
        }

        // 自动闩门：出发 / 回营 / 入夜三个时刻，替玩家把营地大门闩上（见 DoorSecurityLogic）。
        // **置于 switch 之后**是有原因的：DayReturn 分支里的 UnloadExplorationLevel 才刚把营地导航区重新启用，
        // 早于它去闩门就会在一个 Enabled=false 的 NavigationRegion 上重烘焙。
        SecureCampDoors(phase);

        // 自动存档（**唯一的存档途径**，玩家没有手动存档）：只在清晨/黄昏两个昼夜边界落地，一天两次。
        // **必须是本方法的最后一步**——上面那些分支还在改世界（触发围攻/夜袭/闩门），
        // 早于它们存档，存下的就是一个"半个相位"的世界。战斗中欠着，打完再补（见 AutosaveOnPhaseChange）。
        AutosaveOnPhaseChange(phase);
    }

    /// <summary>
    /// <b>自动闩门</b>：把没闩上的营地大门闩上（<see cref="DoorSecurityLogic"/> 出规则，此处只做空间执行）。
    ///
    /// <para>
    /// <b>它堵的洞</b>：门闩此前纯靠玩家手动 ⇒ 玩家派人出去探索，<b>营地大门就那么敞着</b>，
    /// 夜里劫掠者散步进来把营地端了，而他正在地图另一头搜刮，毫不知情。<b>这不是硬核，是无法预期的陷阱。</b>
    /// </para>
    ///
    /// <para>
    /// <b>只在三个时刻发生</b>（<see cref="DoorSecurityLogic.ShouldSecureAt"/>：出发 / 回营 / 入夜），
    /// <b>不是每帧</b>——否则玩家推开门的下一帧它就自己闩回去，他将永远开不了自家的门。
    /// 过了这一刻，整个相位内玩家爱怎么开门就怎么开门。
    /// </para>
    ///
    /// <para>
    /// <b>不发噪音</b>（不调 <c>EmitDoorNoise</c>）：这代表的是营内的人日常把门带上，不是一次战术开门；
    /// 给玩家没亲手做的动作发一圈 100 半径的噪音去招丧尸，是拿自动化去罚他。
    /// </para>
    /// </summary>
    private void SecureCampDoors(DayPhase phase)
    {
        if (!DoorSecurityLogic.ShouldSecureAt(phase))
        {
            return;
        }

        int barred = 0;
        int blocked = 0;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed || !s.IsDoor)
            {
                continue;
            }

            bool occupied = IsDoorwayOccupied(s); // 门口站着人就不闩——否则会把他实心夹在门板里
            if (!DoorSecurityLogic.ShouldAutoBar(s.Door!.Value, s.Barrable, occupied))
            {
                // 只统计"本该闩上、却因为门口有人而没闩成"的那一种——这是玩家必须知道的（他以为门闩着）。
                if (s.Barrable && occupied && DoorSecurityLogic.ShouldAutoBar(s.Door!.Value, s.Barrable, doorwayOccupied: false))
                {
                    blocked++;
                }
                continue;
            }

            SetDoorState(s, DoorLogic.ClosedRestingState(s.Barrable)); // 复用"关门"的落点，规则不散写两份
            barred++;
        }

        if (blocked > 0)
        {
            // ⚠️ 攸关生死：玩家会默认门已经闩上了。闩不上必须当场说出来。
            _campToast.Show($"有 {blocked} 道大门没能闩上——门口还站着人。", CampToast.Bad);
        }
        else if (barred > 0)
        {
            _campToast.Show(barred == 1 ? "大门已自动闩上。" : $"{barred} 道大门已自动闩上。", CampToast.Ok);
        }
    }

    /// <summary>门态徽标的供给：所有还立着的门（被砸毁的门没有状态可言）。<see cref="DoorStateOverlay"/> 据此每帧脏检。</summary>
    private IEnumerable<DoorStateOverlay.DoorBadge> DoorBadges()
    {
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed || !s.IsDoor)
            {
                continue;
            }
            yield return new DoorStateOverlay.DoorBadge(
                s.Rect.Position + s.Rect.Size / 2, (float)_heights.post, s.Door!.Value, s.Lock);
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
                CapacityKg = CarryLimitOf(_survivors[i]), // 断手/挨饿的人这里明显偏低 → 选谁去＝选带多少东西回来
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
                CapacityKg = _bruce.CarryCapacity, // 口袋狗衣驮运能力由 Wiki 配置，统一进同一套负重账
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
            // 布鲁斯（狗）可排岗（效率由 Wiki 配置）：以哨兵 Id=_survivors.Count 追加（选项 Id 为下标空间，狗不在 _survivors，
        // 用越界下标当哨兵，OnGuardConfirmed 据此译回 _bruce.Id）。
        if (_bruce is { Alive: true })
        {
            pawnOptions.Add(new GuardPanel.PawnOption
            {
                Id = _survivors.Count,
                Name = _bruce.DisplayName + "（狗）",
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

        // 皮特升级轴 L2→L3「饥饿≤5 出行三次」：出发瞬间若皮特在本队且饥饿≤5 → RecordDeparture 单调累计一次。
        // **gate 在 L2**（主 agent 拍板）：仅已达 L2 后才累计——L3 以 L2 为前提，未达 L2 的出行不进 L3 计数。
        if (PeteLevelNow() >= 2)
        {
            foreach (Pawn m in _survivors.Where(s => s.Alive && s.Perks.IsPete && ids.Contains(s.Id)))
            {
                PetePerk.RecordDeparture(_storyFlags, m.Hunger.Value);
            }
        }

        _clock.TransitionTo(DayPhase.DayTravel);
    }

    /// <summary>
    /// [T57] 打开世界地图前，把**当下的解锁上下文**灌进去（去过哪些点 / 剧情旗标 / 老档兜底），
    /// 地图据此重算全图的锁。🔴 面板里那道闸门只认这份上下文——不灌就等于全锁死，所以开图必须走这里。
    /// </summary>
    private void OpenWorldMap()
    {
        _worldMapPanel.SetUnlockContext(
            _visitedDestinations,
            _storyFlags,
            _storyFlags.Has(GoldfingerDiscovery.ChristineLeftForRevengeFlag),
            _legacyFullUnlock);
        _worldMapPanel.Visible = true;
    }

    private void OnWorldMapDestinationSelected(string name, int travelTime)
    {
        // 面板那边已经过了 WorldGraphUnlock 的闸门（点击 + 确认两道），这里不再重判——
        // 再判一次就成了第二份规则，两份规则迟早会各说各话。
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
    /// <param name="finder">
    /// 踏进发现点的那个人（由 <c>ExplorationLevel.OnDiscovery</c> 带入）。**物资搜刮点要用它**：
    /// 踏进去的人就是站在那儿一件件往外掏的人（<see cref="LootSession"/>）。剧情/叙事点不按人分流。
    /// </param>
    private void OnExplorationDiscovery(string discoveryId, Pawn? finder)
    {
        // ——关内尸体（T36：杀敌落尸，见 SpawnLevelCorpse）——
        // 踏上一具尸体 ⇒ 站在那儿一件件把他的家伙和衣服掏出来（与关内物资点同一条逐件搜刮链路）。
        // 路由判据是**结构性**的（CorpseNaming：尸体名含中文「的尸体 #」，authored 点一律 ascii id ⇒ 不可能相交），
        // 故这一支放最前面：既短路掉后面一串 Resolve，也保证剧情点永远不会被当成尸体、尸体永远不会触发剧情。
        // **可重复踏入**（关卡侧不去重）：掏到一半被咬跑了，回头还能接着掏——剩下的东西还在他身上。
        if (CorpseNaming.IsCorpseContainer(discoveryId))
        {
            ExplorationCorpseSave? remains = _explorationCorpses.FirstOrDefault(c => c.ContainerId == discoveryId);
            bool recoveredTransmitter = remains is not null && ExplorationRemains.RecoverTransmitter(remains);
            if (recoveredTransmitter)
            {
                _expeditionHasTransmitter = true;
                RecordPlaytestEvent(PlaytestEventKind.CorpseRecovery, "遗体找回",
                    _currentLevel?.DestinationName ?? "探索关", $"从 {discoveryId} 找回关键设备");
                _campToast.Show("从遗体上找回了关键设备——得有人活着把它带回营地。", CampToast.Ok);
            }
            if (_containerLoot.IsSearched(discoveryId))
            {
                if (!recoveredTransmitter)
                    _campToast.Show($"{discoveryId}：已经搜过了。", CampToast.Bad);
                return;
            }
            if (finder is { Alive: true } looter)
            {
                RecordPlaytestEvent(PlaytestEventKind.CorpseRecovery, "开始搜刮遗体",
                    _currentLevel?.DestinationName ?? "探索关",
                    $"{discoveryId}；剩余 {_containerLoot.RemainingCount(discoveryId)} 件");
                BeginLootSession(looter, discoveryId);
            }
            return;
        }

        _ = finder;

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
            // 演出走真实时钟（不吃 TimeScale），播放时长由演出配置决定，可跳过，播完自毁并回调。
            HordeLookoutCinematic.Show(_hud, OnLookoutCinematicFinished);
            return;
        }

        // ——广播台「发出设备」挂点（电台主线）——
        // 踏入发射机发现区：先进入本趟携带态并弹叙事；只有活人回营才 GrantTransmitter 推进主线。
        // 全灭则设备随最后倒下的队员尸体保留三个半天；尸体过期后设备回此 authored 原点。
        if (discoveryId == RadioMainline.TransmitterDiscoveryId)
        {
            bool unavailable = RadioMainline.HasTransmitter(_storyFlags)
                || _expeditionHasTransmitter
                || ExplorationRemains.HasLostTransmitter(_explorationCorpses, ExplorationCache.BroadcastStationName);
            if (!unavailable)
            {
                _expeditionHasTransmitter = true;
                ShowDiscoveryNarrative(RadioMainline.TransmitterPickupTitle, RadioMainline.TransmitterPickupNarrative);
            }
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

        // ——[T61] 下水道·最深处·耗子相遇招募挂点（可招募幸存者）——
        // 与护士**逐行同构**：踏到最深处 → 弹 ChoicePanel（邀请入队 / 暂不）。接受→置 sewer_rat_agreed（待回营注入 + 对话去重）；
        // 婉拒→**不置旗**（日后再下来还能再谈）。真正的 Pawn 注入延到回营（UnloadExplorationLevel → MaybeRecruitRat）。
        RatRecruitOffer? rat = RatRecruit.Resolve(discoveryId, _storyFlags);
        if (rat != null)
        {
            PromptRatRecruit(rat.Value);
            return;
        }

        DiscoveryResult? r = GoldfingerDiscovery.Resolve(discoveryId, _storyFlags);
        if (r != null)
        {
            DiscoveryResult d = r.Value;
            _storyFlags.Set(d.StoryFlag, "true"); // 持久去重：本 flag 已置则下次 Resolve 返回 null

            // 日记也必须随远征携带：活人回营后才进入共享库存；全灭则和背包一起留在队员尸体上。
            // 空 BookId = 该发现点无书（如克莉丝汀本人尸体点，日记A 归帮众尸体），跳过。
            if (!string.IsNullOrEmpty(d.BookId))
                CollectLoot(new[] { LootItem.Book(d.BookId) }, out _, out _);

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

        // [T67] 野外采集点：**弯腰在地上薅一把就走**（不进逐件搜刮会话——它是"这片地自己长出来的"，不是谁柜子里的）。
        //   与搜刮点的区别见 ForageLogic 类注：搜刮＝翻别人的东西（逐件计时），采集＝薅一把即走（即时）。
        //   读过《野外生存指南》的采集者产量按 Wiki 配置乘算，向下取整。一次性：薅过就没了（持久去重）。
        if (ForageLogic.IsForageSpot(discoveryId))
        {
            string forageFlag = "foraged:" + discoveryId;
            if (_storyFlags.Has(forageFlag))
            {
                _campToast.Show("这儿已经薅干净了。", CampToast.Bad);
                return;
            }
            (string matKey, int qty) = ForageLogic.Resolve(
                discoveryId,
                finder is null ? null : new Func<string, bool>(finder.HasReadBook));
            if (qty <= 0 || string.IsNullOrEmpty(matKey))
                return;
            _storyFlags.Set(forageFlag, "true");   // 薅过就没了
            int took = _bag?.AddAsManyAsFit(LootItem.Material(matKey, qty)) ?? 0;
            string matName = Materials.Find(matKey)?.DisplayName ?? matKey;
            _campToast.Show(
                took >= qty ? $"采到 {matName} ×{took}" : $"采到 {matName} ×{took}（背包满了，剩下的留在地里）",
                took > 0 ? CampToast.Ok : CampToast.Bad);
            return;
        }

        // 探索点搜刮缓存：**踏进去不再是整批白捡**——那是逐件搜刮最该管的地方，因为危险全在关里。
        // 现在踏入 = 弹一段环境叙事（你看见了什么）+ 开一段**逐件搜刮**（你得站在那儿一件件往外掏）。
        // 走开就停手，剩下的还在原地，回头能接着搜（一趟搜不完一个大点，这是设计意图，不是缺陷）。
        CacheResult? c = ExplorationCache.Resolve(discoveryId, _storyFlags);
        if (c == null)
        {
            // 物资缓存的“发现”旗标只置一次，但搜刮可能被打断。重复踏入/点击同一点
            // 应继续接着掏，而不是因为 Resolve 已去重就把剩余物资锁死。
            if (!string.IsNullOrEmpty(ExplorationCache.FlagForCache(discoveryId))
                && finder is { Alive: true } resumedSearcher
                && _containerLoot.RemainingCount(discoveryId) > 0)
            {
                BeginLootSession(resumedSearcher, discoveryId);
            }
            return; // 未知 id 或已搜空
        }

        CacheResult cache = c.Value;
        _storyFlags.Set(cache.StoryFlag, "true"); // 持久去重（搜刮点只"发现"一次；东西拿没拿完另算，见 _containerLoot）

        ShowDiscoveryNarrative(cache.Title, cache.Narrative);

        // 掉落交给容器体系托管（键＝cacheId），由 LootSession 一件件往外掏。
        // 踏进去的那个人就是搜的人；万一没传（老链路兜底）就派当前选中的人。
        Pawn? searcher = finder ?? _selectedPawn;
        if (cache.Loot.Count == 0 || searcher is not { Alive: true })
            return;

        _containerLoot.Register(discoveryId, cache.Loot);
        BeginLootSession(searcher, discoveryId);
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
    /// 冻结探索实时层、**分页**弹叙事面板（叙事调查点 [SPEC-B12]，页数由 authored 文本决定，「不走时间」）。
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
        {
            testLevel.Combat = _combat;
            testLevel.TransmitterAvailableAtOrigin = !RadioMainline.HasTransmitter(_storyFlags)
                && !ExplorationRemains.HasLostTransmitter(_explorationCorpses, ExplorationCache.BroadcastStationName);
        }
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

        // 远征背包：出门这一刻定运力（队员上限之和 + 布鲁斯口袋狗衣）。此后关内搜刮都受它的硬上限拦。
        _bag = new ExpeditionBag(PartyCarryLimit());
        // 🔴 [T45] **出门这一刻就把装备灌进账**（修复前这里是个空包 ⇒ 负重 0kg，身上的枪和甲一克都不算）。
        // 同时把逐人的负重 debuff 落到 Pawn 上——「一出门就进入负重 debuff」从这一行开始成立。
        SyncExpeditionLoad();
        RecordPlaytestExpeditionStart(destinationName);
        GD.Print($"[负重] 探索队出发，本趟运力 {_bag.CapacityKg:0.0}kg，"
                 + $"身上装备已占 {_bag.GearKg:0.0}kg（{CarryCapacity.TierLabel(_bag.Tier)}）。");

        ClearSelection();
        _campNavRegion.Enabled = false;

        GetTree().Root.AddChild(_levelRoot);
        SetCampVisible(false);

        _currentLevel.Initialize();

        if (_currentLevel is TestExploration initializedLevel)
            RestoreLevelCorpses(initializedLevel);

        // 关内可关的门（当前只有废弃医院）：Initialize 之后才有门实体，故在此登记成可右键前往的容器。
        RegisterLevelDoorContainers();
    }

    private void UnloadExplorationLevel()
    {
        if (_levelRoot == null)
            return;

        _currentLevel!.OnReturnToCamp -= OnExplorationReturn;
        _currentLevel!.OnDiscovery -= OnExplorationDiscovery;
        _currentLevel!.Cleanup();

        // 先把逐件搜刮后的剩余物同步回尸体账本；全灭时背包与关键设备再挂到最后倒下的队员尸体上。
        SyncLevelCorpseLoot();
        bool expeditionSurvived = _survivors.Any(p => p.Alive && _todaysExpeditionIds.Contains(p.Id));
        RecordPlaytestExpeditionReturn(_currentLevel.DestinationName, expeditionSurvived);
        if (expeditionSurvived)
        {
            DumpExpeditionBag();
            if (_expeditionHasTransmitter)
                RadioMainline.GrantTransmitter(_storyFlags);
        }
        else
        {
            ExplorationCorpseSave? carrier = ExplorationRemains.AttachPartyLoss(
                _explorationCorpses,
                _currentLevel.DestinationName,
                _todaysExpeditionIds,
                _bag?.Contents ?? Array.Empty<LootItem>(),
                _expeditionHasTransmitter);
            GD.Print(carrier is null
                ? "[探索全灭] 未找到队员尸体；背包物资损失，关键设备将在原位重新出现。"
                : $"[探索全灭] 背包与关键设备留在 {carrier.ContainerId}，三个半天内可找回。");
            _bag = null;
            foreach (Pawn survivor in _survivors)
                survivor.ClearCarryLoad();
        }
        _expeditionHasTransmitter = false;

        // 场景登记随关卡卸载；尸体与未取回遗物仍在 _explorationCorpses，重访同一地点时恢复。
        ClearLevelCorpses();

        // 关内门的登记同样随关卡消失（[T49]）：不注销的话，回营后营地里会凭空多出几扇点得到、
        // 却已经不存在的"防火门"（且它们的 rect 落在营地坐标系里，会挡住真正的营地容器）。
        ClearLevelDoorContainers();

        // 卸载关卡时若发现叙事面板仍开着，收起并恢复时标，避免带回营地。
        if (_discoveryPanel.Visible)
            OnDiscoveryContinued();

        foreach (Pawn p in _survivors.Where(s => s.Role == PawnRole.Expedition))
        {
            if (_levelRoot.IsAncestorOf(p))
            {
                p.Reparent(_actorLayer, keepGlobalTransform: false);
                p.Position = _cameraCenter;
                p.SetPresentationLayer(_isoLayer);
            }
        }

        // 随队布鲁斯回收：随队伍返回营地 actor 层（存活则跟随道格恢复，阵亡已在 OnActorDied 置 _bruce=null）。
        if (_bruce is { } bruce && _levelRoot.IsAncestorOf(bruce))
        {
            bruce.Reparent(_actorLayer, keepGlobalTransform: false);
            bruce.Position = _cameraCenter + new Vector2(-8f, 24f);
            bruce.SetPresentationLayer(_isoLayer);
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
        MaybeRecruitRat();          // [T61] 下水道招募的耗子（同护士：关内答应 → 回营才真正入队）
    }

    private void SetCampVisible(bool visible)
    {
        _isoLayer.Visible = visible;
        // 门态徽标是 TopLevel（自己画在 iso 世界坐标上，不吃 _isoLayer 的可见性）——
        // 不在这里一并收起，探索关卡期间营地的门徽标会浮在关卡画面上。
        _doorOverlay.Visible = visible;
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

        var readers = new List<ReaderOption>();
        foreach (Pawn p in _survivors)
        {
            if (!p.Alive || !p.IsControllable || guarded.Contains(p.Id))
                continue;
            readers.Add(new ReaderOption { Id = p.Id, Name = p.DisplayName });
        }

        var books = new List<BookDisplayOption>();
        foreach (BookData b in _bookRegistry.Values)
        {
            // 🔴 [T59] **日记不是书，不能派人去"读"**（用户拍板：书给角色读、日记给玩家读）。
            // 此前日记混在这份列表里 ⇒ 可以派一个幸存者整夜坐着读一本**什么也不给**的叙事文本
            // （那一夜他不能站岗、不能干活）—— 给玩家看的文本被做成了角色的劳动。
            // 日记照旧在库存里点开即看（ReaderPanel，游戏冻结），零角色时间。
            if (b.IsDiary)
                continue;
            // 前置书标题：优先解析实例标题供「未读《X》」提示；解析不到退化到 id。
            string? preTitle = b.PrerequisiteBookId is { } preId ? (_bookResolver(preId)?.Title ?? preId) : null;
            books.Add(new BookDisplayOption
            {
                BookId = b.Id,
                Title = b.Title,
                IsRead = false,
                ReadHours = 0,
                RequiredHours = b.ReadHours,
                PrerequisiteBookId = b.PrerequisiteBookId,
                PrerequisiteTitle = preTitle,
            });
        }

        // 前置满足判定按读者本人已读（引擎侧 AccrueReading 亦以 Pawn.HasReadBook 判读速配置，口径一致）。
        _readingPanel.SetupCharacterBooks(readers, books,
            (pawnId, bookId) => _survivors.FirstOrDefault(s => s.Id == pawnId)?.HasReadBook(bookId) ?? false,
            (pawnId, bookId) => _survivors.FirstOrDefault(s => s.Id == pawnId)?.ReadingHoursFor(bookId) ?? 0.0);
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
                if (post.SweepPhaseDay != _clock.Day)
                {
                    post.SweepPhaseSeconds = SentryLogic.RollSweepPhase(_guardSweepRng, SentrySweep.Default);
                    post.SweepPhaseDay = _clock.Day;
                }
                // 中心朝向由营心指向岗位（向外警戒）；相位本夜本岗稳定，Pawn 到岗后按规律自行扫视。
                float outwardFacing = (post.StandPos - _cameraCenter).Angle();
                guard.BeginGuardDuty(post.StandPos, outwardFacing, post.SweepPhaseSeconds);
                guard.CommandMoveTo(post.StandPos);
                _raidGuards.Add(guard);
                _guardPostSightById[guard.Id] = post.Stats.SightMultiplier;
                continue;
            }

            // 犬类守卫（布鲁斯）：站岗效率按 Wiki 配置，走向岗位靠 GuardStationing 让位跟随/侦测；巡防锁敌由 UpdateRaid 驱动。
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
    /// 认领就近空座、走过去坐下读；无空座就地读（惩罚值由 Wiki 配置提供）。全营读速加成汇总一次算好喂给每个读者（含其自身贡献）。
    /// <b>不 gate 在 Role==Reading</b>——本方法在 NightAct 相位切换的 OnGamePhaseChanged 中先于 PawnRoleManager
    /// 置 Role 而运行（事件订阅顺序），与 StationGuards 同理靠 Stationing 标志放行移动令。
    /// </summary>
    private void StationReaders()
    {
        // 全营读速加成汇总：遍历全体存活幸存者，把各 L3 书虫的 CampWideReadingSpeedBonus 逐个 ×(1+贡献) **连乘**成乘子
        // （含读者本人；仅满级书虫非 0，无书虫 = 1.0）。§2 通则①全乘算——不是旧的求和（那是加算残留，已整改）。
        double campWideMult = _survivors.Where(s => s.Alive)
            .Aggregate(1.0, (acc, s) => acc * (1.0 + s.Perks.CampWideReadingSpeedBonus));

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

            reader.BeginReading(book, campWideMult, _nightLengthSeconds);

            SeatClaim? seat = ClaimNearestFreeSeat(reader.GlobalPosition);
            if (seat is { } s)
            {
                reader.ReadingSeat = s;
                reader.Stationing = true;
                reader.CommandMoveTo(s.Pos);
            }
            else
            {
                reader.ReadingSeat = null; // 无空座：就地读，惩罚由 ReadingSpeed/Wiki 配置施加
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
    /// 常规夜排程到点后触发袭击者潜入。概率与前三小时内的触发点已由 <see cref="NightEventSchedule"/>
    /// 在入夜瞬间一次性掷定；这里不能再掷一次概率，否则实际概率会被重复相乘。
    /// </summary>
    private void TriggerScheduledNightRaiderRaid()
    {
        if (_nightRaidActive || _tutorialActive || _siegeActive || _raidActive)
            return;
        if (!ShiftSchedule.RaidAllowedIn(_clock.CurrentPhase, authored: false))
            return;

        // 意图随机（拟定待调）：杀戮型（潜行先手 1.5x）/ 劫掠型（静默偷窃）各半。
        RaiderIntent intent = _raidContestRng.Range(0.0, 1.0) < 0.5 ? RaiderIntent.Killer : RaiderIntent.Looter;
        int count = NightRaiderCountBase + _clock.Day / 12; // 随天数缓增，拟定待调
        TriggerRaiderRaid(intent, count, authored: false);
    }

    /// <summary>把 NightAct 的真实相位进度换成从入夜起算的游戏小时，并在排程到点时仅触发一次。</summary>
    private void TickNightEventSchedule()
    {
        if (_clock.CurrentPhase != DayPhase.NightAct || _nightLengthSeconds <= 0)
            return;

        const double nightGameHours = 12.0;
        double currentNightGameHour = _clock.PhaseElapsed / _nightLengthSeconds * nightGameHours;
        if (!_nightEventSchedule.ShouldTrigger(currentNightGameHour))
            return;

        // 先落 fired，再进入生成逻辑：即便战斗门控拒绝，也不会下一帧重复触发或读档重掷。
        NightEventKind kind = _nightEventSchedule.EventKind;
        _nightEventSchedule = _nightEventSchedule.WithFired();
        _clock.SetSpeedIndex(0); // 任何袭击落地时自动回到 1 倍速。

        if (kind == NightEventKind.HumanRaid)
            TriggerScheduledNightRaiderRaid();
        else if (kind == NightEventKind.ZombieRaid)
            TriggerRaid();
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
        // ★对抗掷点不再在生成瞬间（边缘距离带可能恒未发现）：改由 UpdateNightRaid 随尖兵逼近到各距离带分段掷点，
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
            r.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter, ShouldSuppressBreach, ReleaseBreachSlotFor);
            r.ConfigureDoors(TryFindOpenableDoor, OpenDoorByAi); // 劫掠者会正常开门（用户拍板）；开不了的才砸
            // 安静入侵（反退化）：锁着的门他会**撬**，围栏他会**轻声拆**——不派守夜人，他就无声无息地进来了。
            r.ConfigureQuietIntrusion(
                TryFindQuietTarget, PickDoorLockByAi, DismantleFenceByAi, SecondsUntilDawnForRaiders);
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
            // 先叠观察者本人的羁绊视野，再叠疲劳：道格 L1 的视野角、布鲁斯 L1 视野角 / L2 视距
            // 不只用于遮暗揭示，也必须进入岗哨「警戒力 vs 潜行力」的真实目击判定。
            VisionLogic.VisionCone cone = BondScaleCone(w.actor, VisionLogic.ConeFor(gLight));
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
                acuity, dist, structBonus, w.watchEff, fatigued, NightWatchContest.HearingBaseRange,
                visionCapability: (float)w.actor.VisionCapability,
                hearingCapability: (float)w.actor.HearingCapability));
        }

        return NightRaidLogic.ResolveCampDetection(alertness, stealth, _raidContestRng);
    }

    /// <summary>发现袭击者：暂停世界 + 弹三档响应面板（复用通用 <see cref="ChoicePanel"/>），展示守卫目击的模糊规模情报。</summary>
    private void ShowRaidResponsePanel()
    {
        CapturePanelTimeState(out _prevRaidResponseSpeed, out _prevRaidResponsePaused);

        int raiderCount = _nightRaiders.Count(r => r.Alive);
        string intel = DisplayNames.Of(NightRaidLogic.BandFor(raiderCount));

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
        raider.PlayScriptedMeleeVisual(CombatData.Dagger(), victim);
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
    /// 【遗留·不再从 day-40 可达】旧"尸潮抵达＝可玩无限围攻直至全灭"路由。已被 <see cref="TryTriggerHordeSiegeEnding"/>
    /// （无限丧尸屠营 CG → 单角色南逃谢幕）**推翻**——day-40 到期改走南逃谢幕，本方法不再被 NightAct 调用。
    /// <para>🔴 <b>本方法零调用者，<c>_siegeActive = true</c> 只在此方法体内</b> ⇒ <c>_siegeActive</c> 生产恒 false。
    /// 其配套的全灭结局路由（旧 <c>EndingCg.ForGameOver</c>/<c>ForKind</c>/<c>EndingKind.HordeSiege</c>）已随军袭路由一并**整条退役**
    /// （[用户裁决·选项B]，因生产不可达却被单测测绿）。<b>本方法体与 <c>_siegeActive</c> 字段仍保留</b>：字段被
    /// <see cref="UpdateRaid"/>/聚餐·相位·自动存档抑制等多处运行时守卫读取（恒 false 下仍是有效的短路条件），删字段牵连过广，非本次范围。</para>
    /// 复用袭营执行层：置 <c>_raidActive</c> 借守卫锁敌 + 破防统计，走 <c>_siegeActive</c> 分支不做胜负结算。
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
            z.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter, ShouldSuppressBreach, ReleaseBreachSlotFor); // 门关闭→砸墙破防（丧尸不开门，只砸）
            z.ConfigurePerception(localLightAt: SampleCampLight); // 固定+手持光源→局部光照喂给（暴露走目标 CarriedLightIntensity 回落）
            z.Position = new Vector2(gx, gy);
            _actorLayer.AddChild(z);
            z.Died += OnRaidZombieDied;
            _raidZombies.Add(z);
        }
    }

    private void OnRaidZombieDied(Actor a)
    {
        _breachSlots.Release(a.GetInstanceId()); // 砸墙途中被守卫打死 → 让出攻击位，后面挤着的那只顶上来
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

        // 节流：守卫锁敌 + 破防/胜负统计按运行时节流配置执行（非每帧）。延迟反应可忽略，
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

        // 犬类守卫（布鲁斯）巡防：无目标时取 Wiki 配置效率锁敌半径内最近丧尸缠斗（无暗哨首发）。
        foreach (Dog dog in _raidGuardDogs)
        {
            if (!dog.Alive || dog.HasActiveTarget)
                continue;
            Zombie? nearest = null;
            float best = dog.GuardSightRadiusScaled * dog.GuardSightRadiusScaled; // 配置效率半径
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
                g.EndGuardDuty();
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
            r.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter, ShouldSuppressBreach, ReleaseBreachSlotFor); // 门关闭→砸墙破防
            r.ConfigureDoors(TryFindOpenableDoor, OpenDoorByAi); // 劫掠者会正常开门（用户拍板）；开不了的才砸
            // 安静入侵（反退化）：锁着的门他会**撬**，围栏他会**轻声拆**——不派守夜人，他就无声无息地进来了。
            r.ConfigureQuietIntrusion(
                TryFindQuietTarget, PickDoorLockByAi, DismantleFenceByAi, SecondsUntilDawnForRaiders);
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
        _christine.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter, ShouldSuppressBreach, ReleaseBreachSlotFor); // 门关闭→砸墙破防
        _christine.ConfigureDoors(TryFindOpenableDoor, OpenDoorByAi); // 反水后的克莉丝汀也是劫掠者：会开门
        _christine.ConfigureQuietIntrusion(
            TryFindQuietTarget, PickDoorLockByAi, DismantleFenceByAi, SecondsUntilDawnForRaiders);
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
            {
                // 放逐：冻结-脚本CG-恢复——相机推近→她转身走向门外、淡出消失（活着离开，不留尸不流血）。
                // 与皮特共用 PlayDeathCinematic 通路（Exiled 走出门淡出复用 WalkOutAndDespawn 的"背离营心走出"运动，
                // 但改走 CG 真实时基）。语义不变：放逐＝离营。CG 结束回调 QueueFree 逻辑节点。
                Actor leaving = _christine;
                _christine = null;
                PlayDeathCinematic(leaving, withThreeZombies: false, CinematicDeathKind.Exiled,
                    onComplete: () =>
                    {
                        if (IsInstanceValid(leaving))
                            leaving.QueueFree();
                        GD.Print("[教学关] 放逐克莉丝汀：她走向门外，消失在营地外。");
                    });
                break;
            }
            case ChristineChoice.Execute:
            {
                // 处决：冻结-脚本CG-恢复——相机推近→她抱头蹲地→被了结→脚下留血（复用 SpawnDeathBlood）→缩回。
                // 与皮特处决共用通路（Killed，无追兵）。语义不变：处决＝死。CG 结束回调 QueueFree 逻辑节点。
                Actor executed = _christine;
                _christine = null;
                PlayDeathCinematic(executed, withThreeZombies: false, CinematicDeathKind.Killed,
                    onComplete: () =>
                    {
                        if (IsInstanceValid(executed))
                            executed.QueueFree();
                        GD.Print("[教学关] 处决克莉丝汀：地上留下一摊血。");
                    });
                break;
            }
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
        // 已呼叫南方（结局③）：电台成为南逃指挥入口——未答满三问则续答，通过则提供启程南逃。
        // （三问失败会经 ReopenAfterSouthFailure 退回 HasTransmitter，不再命中本分支。）
        if (RadioMainline.Stage(_storyFlags) == RadioMainlineStage.CalledSouth)
        {
            if (!SouthTrial.IsComplete(_storyFlags))
                StartSouthTrial(); // 三问未答满（如中途离开）：从当前题续答
            else if (SouthTrial.IsPassed(_storyFlags) && !SouthTrial.HasDeparted(_storyFlags))
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

        // 南方已回绝（三问失败过）则隐藏"呼叫南方"选项——南方是一次性机会（[SPEC-B11]）。
        bool southRefused = RadioMainline.IsSouthRefused(_storyFlags);
        var options = new List<ChoicePanel.ChoiceOption>
        {
            new() { Value = 1, Label = RadioMainline.ReplyOptionLabel,
                    Description = "报出坐标，等军方派人前来", Accent = new Color(0.30f, 0.45f, 0.62f) },
        };
        if (!southRefused)
            options.Add(new() { Value = 2, Label = RadioMainline.CallSouthOptionLabel,
                    Description = "向南方营地求救，试着求一条生路", Accent = new Color(0.35f, 0.55f, 0.38f) });
        options.Add(new() { Value = 0, Label = RadioMainline.DeferOptionLabel,
                    Description = "先不急，再想想", Accent = new Color(0.45f, 0.42f, 0.4f) });

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            southRefused ? RadioMainline.DecisionPromptSouthRefused : RadioMainline.DecisionPrompt,
            options);
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
    ///   · 回复军方：<see cref="RadioMainline.ReplyToMilitary"/> 记录回复日（当天），回复日+2 白天军袭到期（钩子在 <see cref="TryTriggerMilitaryRaid"/>）；
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
                GD.Print($"[电台] 已回复军方（第 {_clock.Day} 天）。第 {_clock.Day + RadioMainline.MilitaryRaidDelayDays} 天白天军袭到期（结局② → 屠营 + 单角色南逃谢幕，见 TryTriggerMilitaryRaid）。");
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
    /// 电台主线军袭到期钩子（每日黎明调）：回复军方后**回复日 + 2 的白天**军袭到期（<see cref="RadioMainline.MilitaryRaidDelayDays"/>）。
    /// ★用户 authored 强制终局（已推翻旧"全员在营才全灭"设计）：大量军人带顶级装备屠尽全营、**随机一名幸存者半残南逃**
    ///   → 进「南逃谢幕」序列（<see cref="BeginSouthEscapeEnding"/>，REUSABLE）。军方动机保持不解释（[SPEC-B11] 留白）。
    /// 返回是否已启动军袭序列（true → 调用方跳过本次黎明聚餐，序列接管画面）。一次性（<see cref="RadioMainline.TryFireMilitaryRaidHook"/> 保证）。
    /// </summary>
    private bool TryTriggerMilitaryRaid()
    {
        // 先确认确有南逃者，再消费一次性军袭钩子。否则健康日结算刚好杀光全营时，不能留下
        // “军袭已触发但终局序列没启动”的半终局持久态；全灭由 GameOverCondition 接管。
        var alive = _survivors.Where(s => s.Alive).ToList();
        if (alive.Count == 0)
        {
            GD.Print($"[电台] 第 {_clock.Day} 天：军袭到期但无存活幸存者，跳过南逃谢幕。");
            return false;
        }
        if (!RadioMainline.TryFireMilitaryRaidHook(_storyFlags, _clock.Day))
            return false;

        // 随机一名存活幸存者半残南逃（掷点走可注入源）。
        Pawn escapee = SouthEscapeEnding.SelectEscapee(alive, _southEscapeRng)!;

        BeginSouthEscapeEnding(escapee, SouthEscapeTrigger.MilitaryRaid);
        return true;
    }

    // ============ 结局③：南逃最小闭环（三问考验 → 启程 → CG③） ============

    /// <summary>
    /// 南方营地三问考验（呼叫南方后经电台逐题抛出，复用 <see cref="ChoicePanel"/>）。
    /// **有真对错门槛**（[SPEC-B11] 新矩阵）：每题三答记 0/1/2 分，三题满 <see cref="SouthTrial.PassThreshold"/> 分才通过。
    /// 可从当前已答进度续答（中途离开再回电台续问）；答满三问 → <see cref="ResolveSouthTrial"/> 判通过/失败。
    /// </summary>
    private void StartSouthTrial()
    {
        var q = SouthTrial.CurrentQuestion(_storyFlags);
        if (q == null)
        {
            ResolveSouthTrial(); // 已答满（幂等兜底）：直接结算
            return;
        }
        AskSouthQuestion(q.Value);
    }

    /// <summary>抛出一道考题（三个记分各异的回答，选后记该题得分并推进；未答满续下一题，答满走结算）。</summary>
    private void AskSouthQuestion(SouthTrial.TrialQuestion question)
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        // Value = 答案下标（据下标取该答案的得分，避免分值撞车）。
        var opts = new List<ChoicePanel.ChoiceOption>();
        for (int i = 0; i < question.Answers.Count; i++)
            opts.Add(new ChoicePanel.ChoiceOption { Value = i, Label = question.Answers[i].Label, Accent = new Color(0.4f, 0.42f, 0.46f) });

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(question.SouthLine + "\n\n" + question.Prompt, opts);
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            int score = (v >= 0 && v < question.Answers.Count) ? question.Answers[v].Score : 0;
            SouthTrial.RecordAnswer(_storyFlags, score);
            var next = SouthTrial.CurrentQuestion(_storyFlags);
            if (next != null)
                AskSouthQuestion(next.Value); // 下一题
            else
                ResolveSouthTrial(); // 三问答满 → 判通过/失败
        };
    }

    /// <summary>
    /// 三问结算（[SPEC-B11] 新矩阵）：满 <see cref="SouthTrial.PassThreshold"/> 分通过、否则失败。
    ///   · 通过 → 置 <see cref="SouthTrial.MarkPassed"/> 入口 flag（family-escape-win 挂"举家南逃 WIN"结局本体）
    ///           + 南方裁决开路（现占位走启程→CG③，待 family-escape-win 替换）。
    ///   · 失败 → <see cref="RadioMainline.ReopenAfterSouthFailure"/> 退回电台"持设备"态、解锁回复军方
    ///           （南方已拒，不可再呼叫南方）+ 南方回绝叙事；**不结束游戏**，续走坏结局（军袭/尸潮）。
    /// </summary>
    private void ResolveSouthTrial()
    {
        if (SouthTrial.IsPassed(_storyFlags))
        {
            SouthTrial.MarkPassed(_storyFlags);
            GD.Print($"[电台] 南方三问通过（总分 {SouthTrial.TotalScore(_storyFlags)}/{SouthTrial.QuestionCount * SouthTrial.MaxScorePerQuestion}），南方开路。");
            ShowSouthVerdict();
        }
        else
        {
            RadioMainline.ReopenAfterSouthFailure(_storyFlags); // 退回持设备态、解锁回复军方、南方线关闭（南方已拒 flag 承载失败态）
            GD.Print($"[电台] 南方三问失败（总分 {SouthTrial.TotalScore(_storyFlags)}/{SouthTrial.QuestionCount * SouthTrial.MaxScorePerQuestion}），南方回绝；解锁回复军方，游戏继续。");
            ShowSouthFailure();
        }
    }

    /// <summary>南方裁决（通过后放行）：告知路已开、回电台启程、尸潮不等人（须抢在 Wiki 配置时限前）。</summary>
    private void ShowSouthVerdict()
        => ShowDiscoveryNarrative(SouthTrial.VerdictTitle, SouthTrial.VerdictNarrative);

    /// <summary>南方回绝（三问失败）：路封了，退回电台可回复军方或死守；不结束游戏。</summary>
    private void ShowSouthFailure()
        => ShowDiscoveryNarrative(SouthTrial.FailureTitle, SouthTrial.FailureNarrative);

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
    /// 南逃启程二次确认（不可逆）。尸潮已至（时限到期或围攻已起）→ 错过窗口，路走不成（兜底叙事）。
    /// 确认 → **举家南逃 WIN 好结局序列**（<see cref="BeginFamilyEscapeWin"/>：先确认全营非空并一次性置启程 flag，
    /// 再全员行军 → 大桥落下被迎接 → 胜利谢幕）。取代旧单人 text CG③ 占位。
    /// </summary>
    private void ConfirmSouthDeparture()
    {
        // 尸潮 Arrived 后不可再逃（[SPEC-B11]：配置时限内才可走）。
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
            // 🟢 好结局：举家南逃 WIN（全员行军 → 大桥落下被迎接 → 胜利谢幕）。取代旧单人 text CG③ 占位。
            if (BeginFamilyEscapeWin())
                GD.Print($"[电台] 第 {_clock.Day} 天：南方三问通过，举家南逃启程（好结局 WIN），进全员行军序列。");
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
            AdvanceAfterMeal(); // 抉择完成，接回被推迟的聚餐后流程推进
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

        var pawn = Pawn.Create(
            ChristineName,
            StartingWeapon.Dagger,
            new Color(0.85f, 0.55f, 0.75f),
            extraApparel: new[] { "皮革胸甲" }); // 克莉丝汀：匕首+开局三件套+皮革胸甲
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
        _containers.Add(new ContainerRef
        {
            Name = pr.name!, Rect = rect, Role = pr.role!, Loot = loot, NarrativeId = pr.narrativeId ?? "",
        });
        // 尸体也走一次性搜刮账（扒下来的衣服只有一件），只是要先看过那段叙事才扒得动。
        if (pr.role is "loot" or "corpse")
        {
            _containerLoot.Register(pr.name!, loot);
        }
    }

    /// <summary>
    /// 一具刚落地、身上**还扒得出东西**的尸体 → 登记成可搜刮容器（走与祖母的尸体、储物柜完全相同的那条链路：
    /// 右键前往 → 到位 → <see cref="ExecuteContainerInteract"/> → 一次性搜刮 → <see cref="LootApplication"/> 入库）。
    /// <para>
    /// 与 camp.json 里那具静态尸体的唯一区别是 <c>NarrativeId</c> 为空 ⇒ 没有"先看叙事"那一步，直接就能扒。
    /// 命中矩形取死者的一个身位（尸体是躺着的，给得比站着时略宽），中心用 <b>cartesian</b> 落点。
    /// </para>
    /// <para>
    /// <b>只有身上有东西的才登记</b>（CorpseYard 已过滤）：尸潮之后满地尸体，光着的那些不进容器表——
    /// 既不让悬停命中去遍历几百个"点了没反应"的点，也不给玩家一地假的可交互提示。
    /// </para>
    /// </summary>
    private void RegisterCorpseContainer(Corpse corpse)
    {
        const float Half = 16f;   // 尸体命中半宽（≈一个身位；Actor 半径 12~13）
        var rect = new Rect2(corpse.CartPosition - new Vector2(Half, Half), new Vector2(Half * 2, Half * 2));

        _containers.Add(new ContainerRef
        {
            Name = corpse.ContainerId, Rect = rect, Role = "corpse", Loot = corpse.Loot, NarrativeId = "",
        });
        _containerLoot.Register(corpse.ContainerId, corpse.Loot);
    }

    /// <summary>尸体被回收（超限淘汰/清走/清场）→ 那个可点击的点必须跟着消失，否则玩家会去搜一具已经不在的尸体。</summary>
    private void DeregisterCorpseContainer(Corpse corpse)
    {
        if (!string.IsNullOrEmpty(corpse.ContainerId))
        {
            _containers.RemoveAll(c => c.Name == corpse.ContainerId);
            _containerLoot.Remove(corpse.ContainerId);   // 藏物登记也一并清，否则一局几百具尸体的清单永远留在字典里
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
                    // 材料（含医疗耗材/感染药）→ 库存材料堆。
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
        // 门：开着就关上、关着就推开、锁着就撬（开门只有一种动作——用户拍板不分轻推/踹开）。
        // 床：躺下养病 / 再点一次自己的床=起来（批次21·impl-bedrest，正文在 CampMain.Bedrest.cs）。
        if (hit.Role == "bed")
        {
            ExecuteBedInteract(arriver, hit);
            return;
        }

        // [批次21·impl-bedrest] 被支使去干别的活（搜柜子/开门/搜尸体…）⇒ 起床。
        // **床的分支在上面、已经 return 了** ⇒ "点自己那张床=起床"的 toggle 不会被这里叫醒后又躺回去。
        WakeIfBedrest(arriver, hit.Role);

        if (hit.Role == "door")
        {
            if (DoorAt(hit.Rect.GetCenter()) is { } door)
            {
                ExecuteDoorInteract(arriver, door);
            }
            return;
        }

        // 关内的门（废弃医院的防火门/安全门/卷帘门）：状态与几何都归关卡持有，这里只转发"开/关"这一个动作。
        // **和营地的门同一口径**：开着就关上、关着就推开；关内的门都没上锁，不需要铁丝。
        if (hit.Role == LevelDoorRole)
        {
            ExecuteLevelDoorInteract(arriver, hit);
            return;
        }

        // 关内发现点：右键点击后走到位，再走与 Area2D 踏入完全相同的解析链。
        // 物资缓存/战斗尸体在关卡侧允许重复触发，叙事/主线则由关卡自带 flag 去重。
        if (hit.Role == LevelDiscoveryRole && _currentLevel is TestExploration level)
        {
            level.TriggerDiscovery(hit.Name, arriver);
            return;
        }

        if (hit.Role == "rubble")
        {
            BeginRubbleDig(arriver, hit);
            return;
        }

        // [T72] 菜园：熟了就收、没熟就下种（走 CraftingJob 工时队列）。正文在 CampMain.Farming.cs。
        if (hit.Role == "cropplot")
        {
            ExecuteCropPlotInteract(arriver, hit);
            return;
        }

        if (hit.Role == "workbench")
        {
            OpenCrafting(FacilityJobKeys.MainWorkbench);
            return;
        }

        if (hit.Role == "weaponbench")
        {
            OpenCrafting(FacilityJobKeys.MainWeaponBench);
            return;
        }

        // 改装台：同一张面板，直接落在【改装】页（工作台开的是【制作】页）。
        // **改装只在这儿能做**——工作台的改装页会因"没有改装台"而整页灰掉（见 CraftingPanel）。
        if (hit.Role == "modbench")
        {
            OpenCrafting(FacilityJobKeys.MainModBench, openModPage: true);
            return;
        }

        // [批次21·T14] 烹饪台：饭**只在这儿**能做（同改装台之于改装）。它固定在厨房，玩家挪不动。
        if (hit.Role == "cookstation")
        {
            OpenCooking();
            return;
        }

        // [T67] 宰杀设施：老鼠/兔子/鸟宰成肉 + 副产物（羽毛/碎皮革）；刀槽放匕首/骨刀。正文在 CampMain.Butchery.cs。
        if (hit.Role == "butcher")
        {
            OpenButchery();
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
            OpenMerchantPanel(arriver);
            return;
        }

        // 尸体（祖母）：首次走近调查 → 只弹叙事，不动她身上任何东西（不走游戏时间，同叙事调查点口径）。
        // 看过之后再来，才当作普通 loot 容器——那件花衬衫得由玩家自己决定要不要扒。
        if (hit.Role == "corpse" && !string.IsNullOrEmpty(hit.NarrativeId))
        {
            NarrativeSpotResult? seen = NarrativeSpotRegistry.Resolve(hit.NarrativeId, _storyFlags);
            if (seen is { } s)
            {
                _storyFlags.Set(s.StoryFlag, "true");
                ShowNarrativeSpot(s.Title, s.Pages);
                return;
            }
        }

        // loot 容器：**逐件搜刮**（不再是点一下全拿走，见 BeginLootSession / LootSession）。
        if (_containerLoot.IsSearched(hit.Name))
        {
            _campToast.Show($"{hit.Name}：已经搜过了。", CampToast.Bad);
            return;
        }
        BeginLootSession(arriver, hit.Name);
    }

    // ---------------- 逐件搜刮（《三角洲行动》式，见 LootSession / LootProgressBar） ----------------
    //
    // 用户拍板（两条，缺一不可）：
    //   ①「物品一件一件转出来，**防止玩家在危险中快速交互完就跑**，每个物品搜刮速度可以一样，不用做分级」
    //   ②「**允许玩家控制一个角色去搜刮转物品，然后控制另一个角色。**」
    //
    // ⇒ ① 搜刮 = **一段暴露时间**：搜的人站着不动，站在丧尸的视野锥里，门外啃围栏的那位不会等你翻完抽屉。
    //    玩家真正要做的决策是：**拿到第几件就跑？**
    // ⇒ ② 搜刮 = **派下去的一件持续的活**（像 RimWorld 派人干活、像《这是我的战争》夜里派人搜刮），
    //    **绝不是打开一块锁住操作的模态面板**。玩家指派 A 去掏尸体后，控制权立刻自由——他可以切去控制 B
    //    放哨/关门/搜另一个点，**A 在后台一件件接着掏**。**多人可以同时各搜各的**（见 _lootJobs 字典）。
    //    ⇒ **分工产生了**：一个人蹲着掏，另一个人在门口盯着围栏外的动静 ⇒ **人手第一次成为战术资源**。
    //
    // 这跟本作既有的两道约束咬合成一副钳子（方向不同）：
    //   · 负重上限限制你**能带走**多少（当前值以 Wiki 配置为准）；
    //   · 逐件搜刮限制你**有时间拿**多少。
    //   ⇒ "背得动，但来不及拿" —— 这是本作独有的紧张感，**耗时绝不能为了流畅而调到可忽略**。
    //
    // ⚠️ **搜刮中的角色是脆弱的**：站着不动、专注、暴露在视野锥里。**这是设计意图，不给他任何保护。**
    //
    // 中断（走开 / 挨打 / 死了 / 离场）：已经**完整取出**的件已经进包了，谁也拿不走；
    // **正在取的那件掉回容器里**、进度作废。⚠️ **"改选中角色"不是中断** —— 那正是要允许的操作。

    /// <summary>
    /// 派这个人去搜这个容器：他当场站住（撤销移动令），此后每帧由 <see cref="UpdateLootJobs"/> 在后台推进。
    /// <b>不夺控制权</b>——派完这一下，玩家立刻可以去点别人。进度长在他头顶（<see cref="LootProgressBar"/>）。
    /// </summary>
    private void BeginLootSession(Pawn searcher, string container)
    {
        IReadOnlyList<LootItem> remaining = _containerLoot.Remaining(container);
        if (remaining.Count == 0)
        {
            _campToast.Show($"{container}：空空如也。", CampToast.Bad);
            return;
        }

        EndLootJob(searcher, "换了个地方翻");  // 同一个人改去搜别处：先收掉他手上那件（进度作废）

        searcher.CancelOrders();  // 搜刮 = 站着不动。这一句就是"暴露时间"的物理落点。

        // 每件的基础工作秒数对所有人都一样（物品之间不分级）；**人之间**的差别走效率乘子，见 UpdateLootJobs。
        var session = new LootSession(container, remaining);

        var bar = new LootProgressBar();
        if (GetTree().GetFirstNodeInGroup("iso_layer") is Node2D layer)
        {
            layer.AddChild(bar);   // 世界内、跟着人走（同 StatusIconStrip 的挂法），**不是** HUD 面板
            bar.Bind(searcher);
        }
        _lootJobs[searcher] = new LootJob(session, bar);
    }

    /// <summary>
    /// 所有在跑的搜刮活每帧推进一遍（<see cref="_Process"/> 调）——**并发**：谁在搜谁推进，互不干扰。
    /// 合规就推进、满一件就实扣实收；人走了/死了/挨打了 → 那一份收场（<b>不影响别人那份</b>）。
    /// </summary>
    private void UpdateLootJobs(double delta)
    {
        if (_lootJobs.Count == 0)
        {
            return;
        }

        // 快照键：推进过程中会改字典（收场 / 容器搜空）。
        foreach (Pawn searcher in _lootJobs.Keys.ToList())
        {
            if (!_lootJobs.TryGetValue(searcher, out LootJob? job))
            {
                continue;
            }
            LootSession session = job.Session;

            // 中断闸门（挨打走 Actor.AnyDamaged 事件，不在这里轮询）：
            //   · 人没了 / 干不了活了 / 离场 → 收场
            //   · 移动了（玩家右键把他调走，或被 AI 拉走）→ 走开即停手，这正是要的
            // ⚠️ **面板开着不是中断**：面板冻结时标 ⇒ delta≈0 ⇒ 本就不推进，收掉反而是错的。
            // ⚠️ **他没被选中也不是中断**：那正是"派他去搜、我去控制别人"这条核心价值本身。
            // 🔴 **探索中的队员不是"不可操控"**（T36 抓到）：`IsControllable` ＝ `Role == Idle`，而出门在外的人
            //    `Role == Expedition` ⇒ 照字面读，**关内任何搜刮会话都会在下一帧被当成"离场了"收掉**
            //    （不只是尸体，连既有的物资搜刮点也一样，一件都掏不出来）。这个闸门问的是"他还干得了活吗"，
            //    不是"玩家此刻能不能指挥他"——而**探索正是他此刻在干的活**。
            if (!searcher.Alive || !_survivors.Contains(searcher) || !CanKeepSearching(searcher))
            {
                EndLootJob(searcher, "离场了");
                continue;
            }
            if (!searcher.IsNavigationFinished())
            {
                session.Interrupt();   // 走开：正在取的那件掉回容器
                EndLootJob(searcher, "走开了");
                continue;
            }

            // 效率乘子链**与制作/挖废墟同源**（WorkEfficiencyOf = 操作能力 × 山姆光环…，别另立一套）。
            // 用户拍板"搜刮速度要受操作能力影响"：**乘算** ⇒ 断了双手的人（0）站到天荒地老也翻不动。
            // [T61] **搜刮专属**乘子在此并入（耗子各等级效果以 Wiki 配置为准）；非耗子保持零回归。
            double eff = LootEfficiencyOf(searcher);

            // 一件一件转出来：session 报出哪几件转完了，容器（事实源）逐件实扣，背包/库存逐件实收。
            foreach (LootItem _ in session.Advance(delta, eff))
            {
                if (_containerLoot.TakeNext(session.Container) is not { } taken)
                {
                    break; // 容器被别处清空（尸体回收等）——立刻收手，别凭空造物
                }
                TakeOneLootItem(searcher, taken);
            }

            if (session.IsComplete)
            {
                EndLootJob(searcher, "搜空了");
                continue;
            }

            // 三样东西必须一眼可见：正在取什么、这件还差多少、**全部搜完还要多久**（第三样才是决策依据）。
            job.Bar.Refresh(
                LootDisplay.NameOf(session.CurrentItem!.Value),
                session.ItemProgress,
                session.RemainingCount,
                session.RemainingRealSeconds(eff));
        }
    }

    /// <summary>
    /// 这个人还翻得动箱子吗（<see cref="UpdateLootJobs"/> 的中断闸门之一）。
    /// <para>
    /// 营地里 ＝ <see cref="Pawn.IsControllable"/>（Role 为 Idle：没在守夜/没在睡/没被派去别处）。
    /// <b>关里 ＝ 探索队员本人</b>——他的 <c>Role</c> 恰恰是 <see cref="PawnRole.Expedition"/>，
    /// 按营地那条判据读会恒假，于是<b>关内一切搜刮（尸体、物资点）都会在下一帧被误判成"离场了"</b>。
    /// 搜刮是他此刻正在干的活，不是别人替他干的。
    /// </para>
    /// </summary>
    private bool CanKeepSearching(Pawn searcher)
        => searcher.IsControllable
           || (_currentLevel is not null && searcher.Role == PawnRole.Expedition);

    /// <summary>转出来一件：入背包/库存（工具进工作台），一行反馈。<b>逐件</b>结算——这一件到手了就是到手了，跑也带得走。</summary>
    private void TakeOneLootItem(Pawn searcher, LootItem item)
    {
        // [T61] 耗子的升级计数：**每转出一件记一件**；等级阈值以 Wiki 配置为准。
        // 「一件」＝ 一个 LootItem **条目**（一堆弹药一次转出 —— LootSession 的既有口径），不按数量/重量/价值。
        // 🔴 记在**转出的那一刻**，不是"收进背包的那一刻"：背不下而留在地上的那件，她也**确实翻出来了**。
        // 只计她本人搜出的（这是**她的**生存秘诀，别人搜的不算）。计数落在 StoryFlags ⇒ 存档天然覆盖。
        if (searcher.Perks.IsRat)
        {
            RatPerk.RecordScavenged(_storyFlags);
        }

        CombatVfxBurst.SpawnLoot(searcher.PresentationLayer, searcher.GlobalPosition);

        var one = new[] { item };
        int food = CollectLoot(one, out List<ToolSlot> tools, out string leftBehind);
        if (food > 0)
        {
            _resources.AddFood(food);
        }
        List<string> installedTools = InstallFoundTools(tools);

        if (!string.IsNullOrEmpty(leftBehind))
        {
            // 背不下 ⇒ 这一件白花了搜刮时间还是没拿走。必须让玩家看见——**负重与工时两道约束在此撞上**。
            _campToast.Show(leftBehind, CampToast.Bad);
            return;
        }
        if (installedTools.Count > 0)
        {
            _campToast.Show($"装上 {string.Join("、", installedTools)}。", CampToast.Ok);
            return;
        }
        _campToast.Show($"{searcher.DisplayName} 取出 {LootDisplay.NameOf(item)}。", CampToast.Ok);
    }

    /// <summary>
    /// 收掉某个人的搜刮活（搜空 / 走开 / 挨打 / 离场）：撤他头顶那条进度条 + 一行总账。**别人的活不受影响。**
    /// <b>已取出的件不回吐</b>；没取完的**原样留在容器里**，回头能接着搜
    /// （<see cref="ContainerLoot.TakeNext"/> 只在拿空时才标已搜）。
    /// </summary>
    private void EndLootJob(Pawn searcher, string reason)
    {
        if (!_lootJobs.Remove(searcher, out LootJob? job))
        {
            return;
        }

        if (GodotObject.IsInstanceValid(job.Bar))
        {
            job.Bar.QueueFree();
        }

        LootSession session = job.Session;
        if (session.TakenCount > 0 || session.RemainingCount > 0)
        {
            string tail = session.RemainingCount > 0
                ? $"，{session.Container}里还剩 {session.RemainingCount} 件"
                : $"，{session.Container}搜空了";
            string head = session.TakenCount > 0
                ? $"{searcher.DisplayName} 拿到 {session.TakenCount} 件"
                : $"{searcher.DisplayName} 什么都没来得及拿";
            _campToast.Show($"{head}{tail}。", session.TakenCount > 0 ? CampToast.Ok : CampToast.Bad);
            GD.Print($"[逐件搜刮] {reason}：{head}{tail}");
        }
    }

    /// <summary>
    /// 有人挨打了（<see cref="Actor.AnyDamaged"/>）：如果挨打的正是**正在翻箱倒柜的那个人**，当场撒手。
    /// 已经掏出来的归你，正在掏的那件掉回箱子里——这是"搜刮＝暴露"最直接的兑现。
    /// <b>别人的搜刮不受影响</b>（他挨的打，不是你挨的）。
    /// </summary>
    private void OnAnyActorDamaged(Actor victim)
    {
        InterruptLootIf(victim as Pawn);
    }

    /// <summary>那个人正在搜刮 → 立刻停手（当前这件的进度作废，它还在容器里）。不是他 / 没人在搜 → 无操作。</summary>
    private void InterruptLootIf(Pawn? p)
    {
        if (p == null || !_lootJobs.TryGetValue(p, out LootJob? job))
        {
            return;
        }
        job.Session.Interrupt();   // 正在取的那件：进度作废，它还在容器里
        EndLootJob(p, "停手");
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
        _digMinuteBudget = 0f; // 同步清小数预算（不丢已推进的整分钟）

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
            _digMinuteBudget = 0f; // 同步清小数预算（不丢已推进的整分钟）
            return;
        }
        RubbleSite? site = _rubble.Find(d.rubbleId);
        if (site is null || site.Cleared)
        {
            _digging = null;
            _digLastMinuteKey = -1;
            _digMinuteBudget = 0f; // 同步清小数预算（不丢已推进的整分钟）
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
            if (!present) _digMinuteBudget = 0f; // 离场清小数残值（同 _craftMinuteBudget 先例）
            return;
        }
        int delta = key - _digLastMinuteKey;
        if (delta < 0) delta += 24 * 60; // 跨午夜环绕
        _digLastMinuteKey = key;
        if (delta <= 0) return;

        // 挖废墟＝建造/开拓类工时，同属"要花时间的活"：与制作同一条链——流逝分钟 × 操作能力 × 山姆光环（连乘，[通则·乘算]）。
        // 走小数分钟预算、余数留存，避免非整效率被吞掉（同 _craftMinuteBudget 先例）。
        _digMinuteBudget += delta * (float)WorkEfficiencyOf(d.pawn);
        int digMinutes = (int)_digMinuteBudget;
        if (digMinutes <= 0) return;
        _digMinuteBudget -= digMinutes;

        site.Advance(digMinutes, workerPresent: true);
        if (site.IsComplete)
        {
            CompleteRubbleDig(d.rubbleId);
        }
    }

    /// <summary>废墟挖满：收获产出落地（材料入共享库存/食物，复用 <see cref="LootApplication"/>）+ 清场显露空地 + 移除可点击容器 + 提示。
    /// 彩蛋位（<see cref="RubbleSite.HasEggSlot"/>）先落普通产出，再按已登记的 authored 键弹发现叙事；空键仅提示仍有东西。</summary>
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
        _digMinuteBudget = 0f; // 同步清小数预算（不丢已推进的整分钟）

        // 入库存的材料件数（drops 只含材料；食物/工具另计）。
        int itemCount = drops.Count(l => l.Kind is not LootKind.Food and not LootKind.Tool);
        // 彩蛋位：有已登记 authored 叙事键 → 材料提示后再弹发现叙事；无键但 HasEggSlot → 退回一行提示。
        (string title, string narrative)? egg =
            site is { HasAuthoredEgg: true } ? EggContent(site.EggContentId) : null;
        string eggNote = egg is null && site is { HasEggSlot: true } ? "，瓦砾深处似乎还压着什么……" : "";
        _campToast.Show($"{rubbleId}已清挖干净，翻出 {itemCount} 件材料，腾出一片空地{eggNote}", CampToast.Ok);
        GD.Print($"[废墟] {rubbleId} 挖净，产出 {itemCount} 件材料{(food > 0 ? $"+{food}份食物" : "")}{eggNote}");
        if (egg is { } e)
        {
            ShowDiscoveryNarrative(e.title, e.narrative);
        }
    }

    /// <summary>
    /// 废墟彩蛋 authored 叙事内容：按 <see cref="RubbleSite.EggContentId"/> 返回 (标题, 正文)；
    /// 未知键返回 null（退回占位提示）。文本对齐既有克制/压抑基调，不发明物件的煽情反应。
    /// 当前只做叙事揭示（“彩蛋物”=铁盒里的全家福/蜡笔画，读后留在原处，不入库）。
    /// </summary>
    private (string title, string narrative)? EggContent(string eggContentId) => eggContentId switch
    {
        RubbleSite.CourtyardFamilyEggContentId => (
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

    /// <summary>通往商人的那道大门此刻是不是还挡着（闩着/关着/锁着）—— 供悬停提示先把代价说清。</summary>
    private bool MerchantGateBarred()
        => TryGetMerchantGate(out Rect2 g)
           && DoorAt(g.GetCenter(), pad: 4f) is { Door: { } st }
           && DoorLogic.Blocks(st);

    /// <summary>
    /// 商人来访要停的那道门：营地**最南**的那扇可闩大门（他从南边的路上来）。
    /// 从 <see cref="_structures"/> 现取而非硬编码坐标 —— camp.json 改了门位，商人自动跟着走。
    /// </summary>
    private bool TryGetMerchantGate(out Rect2 gateRect)
    {
        gateRect = default;
        float bestY = float.NegativeInfinity;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.IsDoor && !s.Removed && s.Barrable && s.Rect.GetCenter().Y > bestY)
            {
                bestY = s.Rect.GetCenter().Y;
                gateRect = s.Rect;
            }
        }
        return bestY > float.NegativeInfinity;
    }

    private void SpawnMerchant()
    {
        var merchant = Merchant.Create();
        merchant.Inject(_combat, _clock);
        merchant.Died += OnMerchantKilledAtCamp; // 死于营地 → 推进接替链（零掉落）+ 清引用

        // 【用户拍板：商人停在门外，你得开门】
        // 他**不进来**。停留点 = 南大门正外方（MerchantStand 纯几何：沿门的薄轴、背离营心推出去）。
        //
        // ⚠️ 这同时修掉一个**比门系统更老的 bug**：此前停留点是**营心**，而围栏是闭合矩形、唯二缺口是两道大门
        //    ⇒ 他寻路根本进不来，只会卡在门外发呆。（门系统之前大门是纯实心结构，他一样进不来——只是从没人发现。）
        //
        // ⚠️ **绝不能**改成"让中立阵营也能开闩着的门"——那会毁掉门闩（横木是从里面插的，门外的陌生人凭什么抬得起？
        //    且 Faction.Neutral 是**开放阵营**，日后任何中立 NPC 都会自动获得推开营地大门的权限）。
        //    正解就是这里：**动商人的停留点，一个字都不动 DoorLogic。**
        Vector2 standPoint;
        Vector2 entry;
        if (TryGetMerchantGate(out Rect2 gate))
        {
            (double sx, double sy) = MerchantStand.OutsideGate(
                gate.Position.X, gate.Position.Y, gate.Size.X, gate.Size.Y,
                _cameraCenter.X, _cameraCenter.Y, MerchantStand.GateStandoff);
            standPoint = new Vector2((float)sx, (float)sy);
            // 入场点：沿同一方向再往外一截，让他从夜色里走上来（而不是凭空出现在门口）。
            entry = standPoint + (standPoint - gate.GetCenter()).Normalized() * 220f;
        }
        else
        {
            // 兜底（数据里没有可闩大门）：退回旧的南门外坐标，至少不崩。
            standPoint = new Vector2(1200f, 1560f);
            entry = new Vector2(1120f, 1780f);
        }

        merchant.Position = entry;
        _actorLayer.AddChild(merchant);
        merchant.CommandMoveTo(standPoint);
        _merchant = merchant;

        Rect2 rect = new(standPoint - new Vector2(20f, 20f), new Vector2(40f, 40f));
        _merchantContainer = new ContainerRef { Name = "神秘商人", Rect = rect, Role = "merchant" };
        _containers.Add(_merchantContainer);

        _campToast.Show("夜色里，神秘商人在大门外停下了——他不进来。想做生意，你得开门。", CampToast.Ok);
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
        _merchantSchedule.CompleteVisit(_clock.Day); // 本次到访收束，按 Wiki 配置排下一次
        _campToast.Show("神秘商人收摊离开了。", CampToast.Bad);
        GD.Print("[神秘商人] 离开营地。");
    }

    /// <summary>清商人在场引用 + 从可交互容器移除 + 作废正走向它的前往令（离场/意外消失共用）。</summary>
    private void OnMerchantGone()
    {
        if (_merchantOpen)
        {
            CloseMerchant();
        }
        _merchantTrader = null;
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
    private void OpenMerchantPanel(Pawn trader)
    {
        _merchantTrader = trader;
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
    {
        // 克莉丝汀「巧舌如簧」：L2/L3 买卖效果由 Wiki 配置提供（[Q2] 需她在营存活）。
        // 卖价率传解析后的配置值；折扣未激活为零。
        int level = ChristineLevelNow();
        bool alive = ChristineAliveInCamp();
        double buyDiscount = ChristinePerk.MerchantBuyDiscount(level, alive);
        int sellRate = ChristinePerk.MerchantSellRatePercent(level, alive, MerchantTrade.SellRatePercent);
        double sellPriceMultiplier = _merchantTrader is null
            ? 1.0
            : BookPassiveEffects.SellPriceMultiplier(_merchantTrader.HasReadBook);
        _merchantPanel.Show(
            _merchantShelf,
            MerchantBuyList.SellableRows(_inventory, sellRate, sellPriceMultiplier),
            _inventory.MaterialCount(Materials.CurrencyKey),
            buyDiscount);
    }

    /// <summary>买入某货架条目：<see cref="MerchantTrade.Buy"/> 实扣白银实产商品 → 结果 toast → 刷新面板。</summary>
    private void OnMerchantBuyRequested(int offerIndex)
    {
        if (offerIndex < 0 || offerIndex >= _merchantShelf.Offers.Count)
        {
            return;
        }
        MerchantOffer offer = _merchantShelf.Offers[offerIndex];
        double buyDiscount = ChristinePerk.MerchantBuyDiscount(ChristineLevelNow(), ChristineAliveInCamp()); // 克莉丝汀买入折扣由 Wiki 配置提供
        switch (MerchantTrade.Buy(_inventory, offer, buyDiscount: buyDiscount))
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
    /// 卖出某收购行的一单位（用户拍板：白名单收购、基准价由 Wiki 配置提供）：<see cref="MerchantTrade.SellOne"/> 实扣一单位物品、白银入账 → 结果 toast → 刷新面板。
    /// 收购白名单/单位收购价由 <see cref="MerchantBuyList"/> 定；商人不在场（断商）自然无此入口。
    /// </summary>
    private void OnMerchantSellRequested(SellRow row)
    {
        // 克莉丝汀 L3 在营 → 卖出价率由 Wiki 配置提供；展示价 row.UnitSellPrice 与实付同源。
        int sellRate = ChristinePerk.MerchantSellRatePercent(ChristineLevelNow(), ChristineAliveInCamp(), MerchantTrade.SellRatePercent);
        double sellPriceMultiplier = _merchantTrader is null
            ? 1.0
            : BookPassiveEffects.SellPriceMultiplier(_merchantTrader.HasReadBook);
        switch (MerchantTrade.SellOne(_inventory, row.UnitItem,
                    sellRatePercentOverride: sellRate, sellPriceMultiplier: sellPriceMultiplier))
        {
            case SellStatus.Ok:
                _campToast.Show($"卖出「{row.DisplayName}」，进账 {Silver.Format(row.UnitSellPrice)} 白银。", CampToast.Ok); // 分→两位小数（[SPEC-B14-补6]）
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
        _merchantTrader = null;
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

        // 探索中看到的是**这一趟的背包**（背了多少/上限多少/哪一档 + 逐件「扔掉」），不是营地库存——
        // 关内能处置的只有背上这些东西。营地里才看共享库存。
        if (_bag != null)
        {
            SyncExpeditionLoad(); // [T45] 上限/装备/逐人惩罚都随队员当下的伤/饿/持械实时刷新
            _stashPanel.ShowExpeditionBag(
                _bag.Contents, _bag.GearKg, _bag.LootKg, _bag.CapacityKg, notice);
        }
        else
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, notice, IsBookRead);
        }
        _stashPanel.Visible = true;
    }

    /// <summary>探索中从背包扔掉一件（腾出容量去拿更值钱的东西——取舍的另一半）。</summary>
    private void OnBagDropRequested(int index)
    {
        if (_bag == null || index < 0 || index >= _bag.Contents.Count)
            return;

        LootItem dropped = _bag.Contents[index];
        _bag.Drop(dropped);
        OpenStash($"扔掉了{LootDisplay.NameOf(dropped)}。");
    }

    /// <summary>
    /// 库存面板「装备」→ 装到当前选中的幸存者（单选取一，无选中则提示）。武器默认装右手（EquipToHand
    /// 会把双手武器自动分流占两手）；护甲走 EquipApparel。成功则从库存移除该件、刷新面板（含开着的角色面板）。
    /// </summary>
    private void OnStashEquipRequested(string refKey)
    {
        // 狗装备（护甲件，键∈DogGearCatalog）分流到布鲁斯，不装到幸存者身上（正式穿戴入口，取代 debug Key.H）。
        if (DogGearCatalog.IsDogGear(refKey))
        {
            EquipDogGearOnBruce(refKey);
            return;
        }

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
            // 正持光源时最常见的拒因就是「双手武器与手持光源互斥」(HeldLightState.BlocksWeaponEquip)——
            // 若不点破，玩家只会看到"持握冲突"却不知道是火把的锅。仅在确实持光时才追加这句。
            string why = target.HeldLight.IsActive
                ? "断肢禁槽 / 持握冲突 / 不适用 —— 注意：他正手持光源，双手武器要占两只手，请先放下光源"
                : "断肢禁槽 / 持握冲突 / 不适用";
            OpenStash($"{item.DisplayName} 无法装备（{why}）。");
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

    /// <summary>
    /// 库存里点某件狗装备的「给布鲁斯穿」→ 穿到布鲁斯身上（正式穿戴入口，取代 debug <c>DebugEquipDogGearOnBruce</c>/Key.H）。
    /// 走 <see cref="Dog.EquipGear"/>：从库存扣该件、同槽旧件顶替回库；成功即刷新 <c>DefenderArmor</c>（护甲进受击结算）。
    /// 布鲁斯不在场（未入队/身故）则提示。口袋狗衣顺带加探索携带容量（<see cref="Dog.CarryCapacity"/>）。
    /// </summary>
    private void EquipDogGearOnBruce(string gearKey)
    {
        if (_bruce is not { Alive: true } bruce)
        {
            OpenStash("布鲁斯不在场——狗装备得等道格与布鲁斯入队后才能穿。");
            return;
        }

        Item? item = _inventory.Armors.FirstOrDefault(i => i.RefKey == gearKey);
        if (item == null)
        {
            return; // 库存里已无此件（并发/重复点），静默
        }

        if (!bruce.EquipGear(gearKey, out string? displaced))
        {
            OpenStash($"{item.DisplayName} 穿不上布鲁斯。");
            return;
        }

        _inventory.Remove(item);
        if (displaced is not null)
        {
            _inventory.Add(Item.Armor(displaced)); // 同槽顶替下来的旧件回库存（绝不静默丢）
        }

        string cap = bruce.CarryCapacity > 0 ? $"，携带容量 +{bruce.CarryCapacity:0}kg" : "";
        OpenStash($"布鲁斯穿上了 {item.DisplayName}{cap}。");
    }

    /// <summary>库存「布鲁斯装备」区点「脱下」→ 从布鲁斯脱下该件并退回库存（<see cref="Dog.UnequipGear"/> 刷新护甲）。</summary>
    private void OnStashDogGearUnequipRequested(string gearKey)
    {
        if (_bruce is not { Alive: true } bruce)
        {
            OpenStash("布鲁斯不在场。");
            return;
        }
        if (!bruce.UnequipGear(gearKey))
        {
            return; // 已不在身（并发/重复点），静默
        }
        _inventory.Add(Item.Armor(gearKey));
        OpenStash($"布鲁斯脱下了 {DogGearCatalog.Get(gearKey)?.DisplayName ?? gearKey}。");
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

    /// <summary>卸某槽的穿戴品 → 回库存 + 刷面板/库存列表（供角色面板「卸下」按钮回调）。
    /// 按槽而非按名：成对品（手套/鞋）同名两只各占一槽，只脱点中的那一只（[SPEC-B18-补]）。</summary>
    private void UnequipApparelToStash(Pawn p, EquipSlot slot)
    {
        List<string> before = p.EquippedApparel.ToList();
        p.UnequipApparelAt(slot);
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

    /// <summary>打开/刷新角色检视面板：喂只读健康快照 + 装备态快照 + 卸下入口。
    /// [SPEC-B14-补8] 装假肢改为医务面板手术（不再即时装配），故此处**不再给角色面板装假肢入口**（onEquip=null，只显示缺肢/已装假肢态）。</summary>
    private void ShowInspect(Pawn p)
    {
        PawnInspection insp = p.Inspect();
        _characterPanel.ShowFor(
            insp,
            SnapshotEquipment(p),
            null, // 假肢安装走 MedicalPanel 手术（补8），角色面板不再即时装
            hand => UnequipWeaponToStash(p, hand),
            slot => UnequipApparelToStash(p, slot),
            // [批次21·impl-medicine·T11] 「医务」下令：选中角色 → 一键开医务面板、病人预选为他。
            // 看见他伤成什么样的下一眼就是"去治他"，不必再按 M 回名单里找人。
            onMedical: () => OpenMedicalFor(p, null),
            needsMedical: MedicalOrderLogic.NeedsMedicalAttention(p.Health, insp.ProstheticSlots.Any(s => s.CanEquip)));
    }

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

    /// <summary>全营书籍被动只看仍存活且未外出的读者；多人阅读仍是同一个布尔门。</summary>
    private bool AnyCamperHasReadBook(string bookId)
        => _survivors.Any(p => p.Alive && p.Role != PawnRole.Expedition && p.HasReadBook(bookId));

    // ---------------- 配方 / 制作（工作台接入） ----------------

    /// <summary>
    /// 打开（或刷新）工作台制作面板：首次打开冻结时标。制作者=当前可控幸存者；书门槛按制作者本人已读（<see cref="Pawn.HasReadBook"/>）。
    /// <paramref name="openModPage"/>=true 时直接落在【改装】页（点营地里的改装台走这条）。
    /// </summary>
    private void OpenCrafting(string slotKey = FacilityJobKeys.MainWorkbench, bool openModPage = false)
    {
        if (!_craftingOpen)
        {
            CapturePanelTimeState(out _prevCraftingSpeed, out _prevCraftingPaused);
            _craftingOpen = true;
        }
        _craftingPanelSlotKey = slotKey;
        RefreshCrafting();
        if (openModPage)
        {
            _craftingPanel.OpenModPage();
        }
        _craftingPanel.Visible = true;
    }

    /// <summary>重刷制作面板数据（下单/推进/完工/改装后调，反映扣掉的材料、新入库产物、在制任务进度）。</summary>
    private void RefreshCrafting()
        => _craftingPanel.ShowFor(_workbench, ControllableCrafters(), _inventory,
            (pawn, id) => pawn.HasReadBook(id), // 书门槛按制作者本人已读（非营地全局）
            JobAt(_craftingPanelSlotKey),
            DogGearGate,                         // 制作者门槛：狗装备需道格 + 羁绊≥2 级（灰显+双保险）
            HasModBench,
            recipeFilter: recipe => _craftingPanelSlotKey == FacilityJobKeys.MainWeaponBench
                ? WeaponBench.IsWeaponRecipe(recipe.Id)
                : !WeaponBench.IsWeaponRecipe(recipe.Id));

    /// <summary>当前可作制作者的幸存者（存活且空闲可控）。</summary>
    private List<Pawn> ControllableCrafters()
        => _survivors.Where(p => p.Alive && p.IsControllable && p.Role != PawnRole.Guard).ToList();

    /// <summary>
    /// 狗装备制作者门槛判据（批次5，消费 <see cref="DougBruceBond.CanCraftDogGear"/>）：
    /// 门槛键＝<see cref="RecipeBook.DogGearCrafterGate"/> 时，要求**制作者是道格**且**与布鲁斯羁绊≥2 级**（且两者皆在世）；
    /// 满足返回 null、否则返回灰显文案。喂给 <see cref="CraftingLogic.CanCraft"/>/<see cref="CraftingService.StartJob"/> 的 crafterGate。
    /// 未识别的门槛键 fail-closed（返回文案）。羁绊等级经 <see cref="BondLevel"/>（共同存活天数现算）。
    /// </summary>
    private string? DogGearGate(Pawn crafter, string gateKey)
    {
        // 改装台「一台就够」：营地已有一台时灰掉配方——第二台案子毫无用处，别让玩家把料喂进去。
        if (gateKey == RecipeBook.ModBenchAbsentGate)
        {
            return HasModBench ? "营地已经有一台改装台了" : null;
        }

        if (gateKey == WeaponBench.AbsentGate)
        {
            return HasWeaponBench ? "营地已经有一台武器台了" : null;
        }

        // [批次21·T14] 烹饪台「一座就够」：同理——厨房只有那么一个角，第二座灶毫无用处。
        if (gateKey == CookStation.AbsentGate)
        {
            return HasCookStation ? "营地已经有一座烹饪台了" : null;
        }

        // [T67] 烹饪台「在场」：茶要在灶上煮（用户拍板）。方向与上一条相反——这里要求**已有**烹饪台。
        if (gateKey == RecipeBook.CookStationPresentGate)
        {
            return HasCookStation ? null : "得先有一座烹饪台，茶要在灶上煮";
        }

        // [T67] 宰杀设施「一座就够」：营地已有任一档宰杀设施时，灰掉"简易宰杀点"配方。
        if (gateKey == ButcherStation.AbsentGate)
        {
            return HasButcherStation ? "营地已经有一处宰杀设施了" : null;
        }

        // [T67] 宰杀台升级：要求营地**已有简易宰杀点、且还没升级成宰杀台**（升级不新开引擎轴，用 gate 表达）。
        if (gateKey == ButcherStation.UpgradeGate)
        {
            if (HasButcherTable) return "营地已经有一张宰杀台了";
            return HasButcherPoint ? null : "得先造一个简易宰杀点才能升级";
        }

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
        // 双保险：敌袭战斗中 Guard 虽可被玩家选中，但站岗职责绝不能与任何生产叠加。
        if (crafter.Role == PawnRole.Guard)
        {
            _campToast.Show("站岗的人不能离岗生产。", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        RecipeData? recipe = RecipeBook.Find(recipeId);
        if (recipe is null)
        {
            _campToast.Show($"未知配方：{recipeId}", CampToast.Bad);
            return;
        }

        string slotKey = WeaponBench.IsWeaponRecipe(recipe.Id)
            ? FacilityJobKeys.MainWeaponBench
            : FacilityJobKeys.MainWorkbench;
        if (_craftingPanelSlotKey != slotKey)
        {
            _campToast.Show(WeaponBench.IsWeaponRecipe(recipe.Id)
                ? "武器只能在武器台制作。"
                : "这条配方不在武器台制作。", CampToast.Bad);
            RefreshCrafting();
            return;
        }
        if (!CanStartFacilityJob(slotKey, crafter, out string busyWhy))
        {
            _campToast.Show(busyWhy, CampToast.Bad);
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

        StartFacilityJob(slotKey, result.Job!, crafter);
        _craftLastMinuteKey = -1;
        // 报**这一单的真工时**（result.Job.TotalWorkMinutes），不是配方上那个死数——
        // 读过《木匠入门》的人做家具会打 95 折（CraftWorkTime），报配方原值等于当面骗人。
        int ordered = result.Job!.TotalWorkMinutes;
        string work = CraftingPanelFormat.FormatWorkDuration(ordered);
        string faster = ordered < recipe.WorkMinutes ? "，手熟，快些" : "";
        _campToast.Show($"已下单：{recipe.DisplayName}（工时 {work}{faster}，夜间生产）", CampToast.Ok);
        GD.Print($"[制作] {crafter.DisplayName} 下单 {recipe.DisplayName}（工时 {ordered} 分）");
        RefreshCrafting();
    }

    /// <summary>
    /// 库存面板「拆解」→ 把一件**造得出来的东西**拆回材料（返还规则以 Wiki 配置为准）。
    /// <para>
    /// <b>走的是制作那条工时队列</b>（任务 id = <c>salvage:&lt;物品键&gt;</c>，见 <see cref="SalvageLogic.JobIdFor"/>）：
    /// 拆解也是活儿，得有人站在工作台前干；一座工作台一次只干一件事，故与制作互斥。
    /// 语义对齐制作：**下单即把那件东西拿走**（锁定，防重复下单），材料留待完工才返还。
    /// </para>
    /// </summary>
    private void OnStashSalvageRequested(string itemKey)
    {
        Pawn? worker = _selected.FirstOrDefault(p => p.IsControllable && p.Role != PawnRole.Guard);
        if (worker == null)
        {
            OpenStash("请先选中一个幸存者，再点「拆解」——拆东西也得有人动手。");
            return;
        }

        if (!CanStartFacilityJob(FacilityJobKeys.MainWorkbench, worker, out string busyWhy))
        {
            OpenStash(busyWhy);
            return;
        }

        SalvageResult started = SalvageService.StartSalvage(itemKey, _inventory);
        if (!started.Success)
        {
            OpenStash(started.FailureReason ?? "拆不动。");
            return;
        }

        StartFacilityJob(FacilityJobKeys.MainWorkbench,
            new CraftingJob(SalvageLogic.JobIdFor(itemKey), started.WorkMinutes), worker);
        _craftLastMinuteKey = -1;

        string name = SalvageLogic.RecipeFor(itemKey)?.DisplayName ?? itemKey;
        string work = CraftingPanelFormat.FormatWorkDuration(started.WorkMinutes);
        GD.Print($"[拆解] {worker.DisplayName} 开拆 {name}（工时 {started.WorkMinutes} 分）");
        OpenStash($"开始拆解 {name}（工时 {work}，拆完材料入库）。");
    }

    // ════════════════ 门 / 家具的拆除（Shift + 右键）════════════════
    //
    // ⚠️ **墙不在这条路上，也不该在**：墙不能建、不可拆，只能砸（走破防那条路，零回收）——
    // 理由是刻意的设计防御，见 SalvageLogic / StructureBuildCost 里那段 kill box 注释。
    // 围栏根本没登记进 _containers（点不到），故这里连拦都不用拦；能点到的门与家具才谈得上拆。

    /// <summary>Shift+右键的目标拆得动吗？拆不动时给出**人话**的原因（门/家具各有各的拆不动法）。</summary>
    private bool CanSalvageTarget(ContainerRef c, out string? why)
    {
        why = null;

        // 关内的门拆不了：拆除是**营地经济**（把自家造的东西拆回料），而关卡里的门不是你造的，
        // 你也不可能扛着一扇门走回家。它只有推开/关上（锁着的还能撬）。
        if (c.Role == LevelDoorRole)
        {
            why = "关卡里的门拆不走——它只能推开、关上，或者撬开。";
            return false;
        }

        if (c.Role == "door")
        {
            if (DoorAt(c.Rect.GetCenter()) is not { } door)
            {
                why = "这扇门不在结构表里。";
                return false;
            }
            if (!SalvageLogic.CanSalvageStructure(door.State.Tier))
            {
                why = "墙拆不了——只能砸掉，而砸掉什么也剩不下。";
                return false;
            }
            return true;
        }

        if (SalvageLogic.CanSalvageFurniture(c.Name))
        {
            return true;
        }

        // 收音机 / 废墟 / 尸体 / 草垛：不是造出来的东西，没有建造成本可依，自然拆不出料。
        why = $"{c.Name}拆不出什么来——它本来就不是造出来的。";
        return false;
    }

    /// <summary>
    /// 到达目标 → 下拆除令：起一条工时任务（借制作那条队列，见 <see cref="SalvageLogic.JobIdFor"/>）。
    /// 门/家具的**本体留在原地**，等工时满了才抹掉（人还在拆呢）——中途被袭营拉走，进度不丢。
    /// </summary>
    private void BeginSalvageAt(Pawn worker, ContainerRef target)
    {
        if (worker.Role == PawnRole.Guard)
        {
            _campToast.Show("站岗的人不能离岗拆解生产。", CampToast.Bad);
            return;
        }

        if (!CanSalvageTarget(target, out string? why))
        {
            _campToast.Show(why ?? "拆不了。", CampToast.Bad);
            return;
        }

        string jobTarget;
        int workMinutes;
        if (target.Role == "door" && DoorAt(target.Rect.GetCenter()) is { } door)
        {
            jobTarget = SalvageLogic.StructureTargetPrefix + _structures.IndexOf(door);
            workMinutes = SalvageLogic.WorkMinutesOfStructure(door.State.Tier);
        }
        else
        {
            jobTarget = SalvageLogic.FurnitureTargetPrefix + target.Name;
            workMinutes = SalvageLogic.WorkMinutesOfFurniture(target.Name);
        }

        string slotKey = FacilityJobKeys.For("worksite", jobTarget);
        if (!CanStartFacilityJob(slotKey, worker, out string busyWhy))
        {
            _campToast.Show(busyWhy, CampToast.Bad);
            return;
        }
        StartFacilityJob(slotKey, new CraftingJob(SalvageLogic.JobIdFor(jobTarget), workMinutes), worker);
        _craftLastMinuteKey = -1;

        string work = CraftingPanelFormat.FormatWorkDuration(workMinutes);
        _campToast.Show($"{worker.DisplayName} 开始拆 {target.Name}（工时 {work}）。", CampToast.Ok);
        GD.Print($"[拆解] {worker.DisplayName} 开拆 {target.Name}（工时 {workMinutes} 分）");
    }

    /// <summary>门拆完：返还材料 + 把门从世界上抹掉（复用 <see cref="DestroyStructure"/> 的清场路径，导航自动补出通道）。</summary>
    private void CompleteStructureSalvage(int structureIndex)
    {
        if (structureIndex < 0 || structureIndex >= _structures.Count)
        {
            GD.Print($"[拆解] 完工但结构已不在：#{structureIndex}");
            return;
        }

        CampStructureInstance s = _structures[structureIndex];
        SalvageResult done = SalvageService.SalvageStructure(s.State.Tier, _inventory);
        if (!done.Success)
        {
            _campToast.Show($"拆解失败：{done.FailureReason}", CampToast.Bad);
            return;
        }

        string name = DisplayNames.StructureName(s.DoorName, s.State.Kind);
        DestroyStructure(s); // 移碰撞 + 移视觉 + 补导航（与被砸穿走同一条清场路径）
        AnnounceSalvage(name, done);
    }

    // ================= 改装台：在工作台造出来 → **自动落在车间的固定锚点** =================
    //
    // 【用户拍板】"改装台、烹饪台**不允许跨越**，但是他们是营地内**固定位置**。改装台放在**车间**。"
    // camp.json 本来没有车间（只有 住宅/仓库/**空牛棚**）⇒ 用户选定：**空牛棚改造成车间**。
    // ⇒ 玩家**摆不了**它：配方完工的那一刻，它就立在车间里（锚点见 WeaponModLogic.BenchAnchorX/Y）。
    //   故**没有**放置模式、没有摆放按钮、也不接 PlacementRules（那套是给可摆放家具的）。
    //   但锚点本身是按禁建带口径挑的（距最近围栏/大门明显超出禁建带），论证见 WeaponModLogic。

    /// <summary>改装台的固定锚点矩形（车间＝空牛棚内）。</summary>
    private static Rect2 ModBenchAnchorRect => new(
        WeaponModLogic.BenchAnchorX, WeaponModLogic.BenchAnchorY,
        WeaponModLogic.BenchWidth, WeaponModLogic.BenchHeight);

    /// <summary>
    /// 配方「改装台」完工：直接在车间的固定锚点立起来（**不进库存**——它不是能揣兜里的东西）。
    /// </summary>
    private void CompleteModBenchBuild()
    {
        if (HasModBench)
        {
            return;   // 一台就够（配方本来就被 ModBenchAbsentGate 灰掉了，此为双保险）
        }

        SpawnModBench(ModBenchAnchorRect);
        RebakeNavigation();   // 它实心、挖了导航洞、不可跨越 —— 寻路图得知道
        _campToast.Show("改装台造好了，就摆在车间（原来的空牛棚）里。", CampToast.Ok);
        GD.Print($"[改装台] 完工，落于车间锚点 {ModBenchAnchorRect.Position}");
    }

    /// <summary>
    /// 在 <paramref name="rect"/> 立起改装台的实体（碰撞 + 视觉 + 导航洞 + 可拆登记 + 可点击容器）。
    /// **实心**（与工作台/柜子同构，和沙袋相反——沙袋刻意不建碰撞、不挖洞）。
    /// 供**完工建造**与**读档复原**共用（读档的导航重烘焙由 RestorePlacedFurniture 统一做）。
    /// </summary>
    private void SpawnModBench(Rect2 rect)
    {
        string key = WeaponModLogic.BenchFurnitureKey;
        var style = new PixelStyle { color = new[] { 0.38, 0.36, 0.40 }, jitter = 0.12 };
        var visuals = new List<Node2D>();

        StaticBody2D body = AddSolid(rect, style, seed: 23, (float)_heights.prop, cell: 200f, visuals);
        _furniture[key] = new FurnitureInstance { Rect = rect, Body = body, Visuals = visuals };

        // 可点击：选中角色右键前往 → 打开【改装】页（见 ExecuteContainerInteract 的 "modbench" 分支）。
        _containers.Add(new ContainerRef { Name = key, Rect = rect, Role = "modbench" });
    }

    private static Rect2 WeaponBenchAnchorRect => new(
        WeaponBench.AnchorX, WeaponBench.AnchorY, WeaponBench.Width, WeaponBench.Height);

    private void CompleteWeaponBenchBuild()
    {
        if (HasWeaponBench) return;
        SpawnWeaponBench(WeaponBenchAnchorRect);
        RebakeNavigation();
        _campToast.Show("武器台造好了，固定落在车间里。", CampToast.Ok);
    }

    private void SpawnWeaponBench(Rect2 rect)
    {
        var style = new PixelStyle { color = new[] { 0.34, 0.32, 0.29 }, jitter = 0.12 };
        var visuals = new List<Node2D>();
        StaticBody2D body = AddSolid(rect, style, seed: 29, (float)_heights.prop, cell: 200f, visuals);
        _furniture[WeaponBench.FurnitureKey] = new FurnitureInstance { Rect = rect, Body = body, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = WeaponBench.FurnitureKey, Rect = rect, Role = "weaponbench" });
    }

    // ================= 沙袋：建造 → 自由摆放 → 拆走重摆 =================

    /// <summary>库存面板点了「摆放」：进入放置模式（左键落位、右键取消）。**改装台不在此列**——它是固定位置，玩家摆不了。</summary>
    private void OnStashPlaceRequested(string key)
    {
        // [impl-furniture-registry] 分派收进一张表（正文在 CampMain.Placeables.cs）：库存「摆放」哪一种家具、
        // 就调它登记的 Begin（= 既有 BeginXPlacement）。此前这里是一串逐类型的平行 if——漏接一条就是"摆不出来"的死按钮
        // （床/捕鸟陷阱/圈套/宰杀点都曾漏过）。沙袋的 Begin = 下面的 BeginSandbagPlacement。
        TryBeginPlacementFor(key);
    }

    /// <summary>
    /// 进入摆放沙袋模式（由注册表的沙袋 <c>Begin</c> 委托调）。沙袋是最早那件可摆放物、走的是它自己的
    /// <see cref="SandbagSpec.CanPlace"/>（不查禁建带 / 室内外——它的本职就是垒在门口防线后）——这条特殊校验路径
    /// 保持不变，见 <see cref="TryPlaceSandbag"/>。
    /// </summary>
    private void BeginSandbagPlacement()
    {
        if (_inventory.MaterialCount(SandbagSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里一垛沙袋都没有——先做出来。", CampToast.Bad);
            return;
        }
        _placingSandbag = true;
        _campToast.Show("挑个地方垒起来（右键作罢）。它挡不住任何人走过去，只是让子弹更可能打在它身上。", CampToast.Ok);
    }

    /// <summary>
    /// 试着把一垛沙袋垒在 <paramref name="cart"/>：校验位置（<see cref="SandbagSpec.CanPlace"/>）→ 扣库存 → 落地。
    /// 校验的是"摆得下吗"，**不是"会不会挡路"**——沙袋永远不挡路，那正是它获准自由摆放的理由。
    /// </summary>
    private void TryPlaceSandbag(Vector2 cart)
    {
        var bounds = new SandbagSpec.Box(
            _mapBounds.Position.X, _mapBounds.Position.Y, _mapBounds.Size.X, _mapBounds.Size.Y);

        // 实心障碍 = 场上一切挖了导航洞的东西（墙/建筑/围栏/大门/实心家具/废墟）——沙袋不该长在它们身体里。
        var solids = new List<SandbagSpec.Box>(_navHoles.Count);
        foreach (Rect2 h in _navHoles)
        {
            solids.Add(new SandbagSpec.Box(h.Position.X, h.Position.Y, h.Size.X, h.Size.Y));
        }

        // 已摆好的沙袋（不能摞第二层）。
        var bags = new List<SandbagSpec.Box>();
        foreach (ContainerRef c in _containers)
        {
            if (c.Role == "sandbag")
            {
                bags.Add(new SandbagSpec.Box(c.Rect.Position.X, c.Rect.Position.Y, c.Rect.Size.X, c.Rect.Size.Y));
            }
        }

        var center = new System.Numerics.Vector2(cart.X, cart.Y);
        SandbagSpec.PlacementResult verdict = SandbagSpec.CanPlace(center, bounds, solids, bags);
        if (verdict != SandbagSpec.PlacementResult.Ok)
        {
            _campToast.Show(SandbagSpec.RejectionText(verdict), CampToast.Bad);
            return;   // 不退出放置模式——换个地方接着点
        }

        if (!_inventory.TrySpendMaterial(SandbagSpec.ItemKey, 1))
        {
            _campToast.Show("库里一垛沙袋都没有——先做出来。", CampToast.Bad);
            _placingSandbag = false;
            return;
        }

        PlaceSandbagAt(cart);
        _placingSandbag = false;
        if (_stashPanel.Visible)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead); // 扣掉那一垛后刷新库存面板
        }
    }

    /// <summary>
    /// 把一垛沙袋落到世界上。<b>刻意不调 AddSolid、刻意不往 _navHoles 里加</b>——
    /// 不建碰撞体、不挖导航洞 ⇒ 不挡移动、不改寻路 ⇒ **摆不出 kill box**，这是沙袋获准自由建造的全部理由
    /// （对照：用户拍板"墙不能建"正因为墙会改寻路）。谁要给它加碰撞体，请先读 SandbagSpec 的类注。
    /// </summary>
    private void PlaceSandbagAt(Vector2 cart)
    {
        var size = new Vector2(SandbagSpec.Width, SandbagSpec.Height);
        var rect = new Rect2(cart - size / 2f, size);
        string name = $"沙袋#{++_sandbagSeq}";

        var style = new PixelStyle { color = new[] { 0.56, 0.51, 0.36 }, jitter = 0.18 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 19 + _sandbagSeq, height: CoverPropHeight, cell: 48f, collect: visuals);

        // 半身掩体登记：贴着它的**双方**都按 Wiki 配置获得远程无效概率；不阻断近战（矮物，绕过去就能砍）。
        _coverField.Add(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y,
            SandbagSpec.CoverChance, SandbagSpec.BlocksMelee);

        // 可拆句柄（Body=null：它压根没有碰撞体）+ 可点击登记 ⇒ Shift+右键即走 impl-salvage 的通用家具拆除，
        // 返还一半材料（布 1 + 石料 2）。摆错了就拆走重摆——这是"自由摆放"该配的退出机制。
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "sandbag" });

        _campToast.Show("沙袋垒好了。记得贴着它——站在旁边三步远，它保不了你。", CampToast.Ok);
    }

    /// <summary>家具拆完：返还材料 + 把它从世界上抹掉（碰撞体/视觉块/导航洞/可点击登记一并清）。</summary>
    private void CompleteFurnitureSalvage(string furnitureName)
    {
        SalvageResult done = SalvageService.SalvageFurniture(furnitureName, _inventory);
        if (!done.Success)
        {
            _campToast.Show($"拆解失败：{done.FailureReason}", CampToast.Bad);
            return;
        }

        // [T72] 玩家主动拆掉菜园 ⇒ 清干净它名下所有格的生长计时器（未收的作物随菜园一起没了，同"拆家具东西不退"）。
        // 只在这条**玩家拆除**路清，不在 RemoveFurniture 里清（那条也被读档前清场调用，会误删刚读回的存档计时器）。
        if (CropPlotSpec.IsCropPlotFurniture(furnitureName))
        {
            CropPlotRuntime.ClearPlot(_storyFlags, furnitureName);
        }

        RemoveFurniture(furnitureName);
        AnnounceSalvage(furnitureName, done);
    }

    /// <summary>把一件家具从世界上抹掉：碰撞体 + 视觉块 QueueFree、导航洞补回、可点击登记撤掉、账本清除。</summary>
    private void RemoveFurniture(string furnitureName)
    {
        if (!_furniture.TryGetValue(furnitureName, out FurnitureInstance? f))
        {
            return;
        }
        _furniture.Remove(furnitureName);

        // [批次21·impl-bedrest] 拆的要是一张床：从床位册注销，**把躺在上面的人赶下来**（他改打地铺——
        // 仍在休养，只是不再吃睡床加成）。不注销的话，床位册会留着一张已经不存在的床的占用记录。
        RemoveBedIfAny(furnitureName);

        // 沙发是座位家具：拆除时必须让升级座位一起退场，不能留下幽灵座位给读书分配。
        RemoveSofaIfAny(furnitureName);

        // [T72] 拆的要是一座菜园：维护数目闸（每帧生长的开销闸）。
        // ⚠️ **生长计时器不在这儿清**：RemoveFurniture 也被读档前的 ClearPlayerPlacedFurniture 调用，
        //    而那一步跑在 RestoreStoryFlags **之后** ⇒ 在这清会把刚读回来的存档计时器误删。
        //    真正的"玩家拆掉菜园 ⇒ 清计时器"落在 CompleteFurnitureSalvage（玩家主动拆除那一条路）。
        if (CropPlotSpec.IsCropPlotFurniture(furnitureName))
        {
            _cropPlotCount = Math.Max(0, _cropPlotCount - 1);
        }

        if (f.Body != null && IsInstanceValid(f.Body))
        {
            f.Body.QueueFree();
        }
        foreach (Node2D v in f.Visuals)
        {
            if (IsInstanceValid(v))
            {
                v.QueueFree();
            }
        }
        f.Visuals.Clear();

        _containers.RemoveAll(c => c.Name == furnitureName); // 拆没了就不该再点得到（也不该再能搜刮/开面板）

        // 半身掩体（沙袋/椅子）：拆走了掩体判定也得跟着没，否则对着一片空地还白享掩体概率。
        _coverField.RemoveRect(f.Rect.Position.X, f.Rect.Position.Y, f.Rect.Size.X, f.Rect.Size.Y);

        // **只有实心家具才需要补导航**。沙袋压根没挖过导航洞（它不挡路——这正是它获准自由摆放的理由），
        // 拆了它寻路图一个字节都不用动。这条 if 本身就是"沙袋不改寻路"的运行时证据。
        if (_navHoles.Remove(f.Rect))
        {
            RebakeNavigation();                               // 家具占的地方现在能走人了
        }
        GD.Print($"[拆解] {furnitureName} 已从营地移除。");
    }

    /// <summary>
    /// [T47] **消耗型改装脱落**（锋刃研磨：砍满三下，刃就磨钝了）。
    ///
    /// <para>
    /// 🔴 <b>存在的全部意义是"玩家看得见"</b>：一把武器的数值**在战斗中途悄悄变回原样**，是最糟的一类失效——
    /// 玩家会以为是 bug。所以这里必须报一行，而且要说清楚**是哪条改装、掉在了谁手上**。
    /// </para>
    /// <para>
    /// 库存通常**不用动**：装在手上的武器早在装备时就从库存里拿走了（见 EquipFromStash），
    /// <c>Pawn</c> 已经把手上那把换成脱落后的武器。只有"换不回去"这种理论上不该发生的情况才兜底回库存，
    /// 免得那把武器凭空蒸发。
    /// </para>
    /// </summary>
    private void OnWeaponModBroken(Pawn who, string oldName, string newName, IReadOnlyList<string> brokenMods)
    {
        string mods = brokenMods.Count > 0 ? string.Join("、", brokenMods) : "改装";
        _campToast.Show($"{who.DisplayName} 的{mods}用尽了——{newName} 恢复原样。", CampToast.Bad);
        GD.Print($"[改装] {who.DisplayName}：{oldName} 的「{mods}」已耗尽脱落 → {newName}");

        // 兜底：万一没换回手上（不该发生），别让这把武器蒸发——退回库存。
        if (who.PrimaryWeapon?.Name != newName && ModdedWeaponRegistry.WeaponByName(newName) is not null)
        {
            _inventory.Add(Item.Weapon(newName));
        }

        if (_stashOpen) OpenStash(null);
    }

    /// <summary>拆解完工的统一播报（含"废木料还得配胶水"那句——玩家第一次看见木头一半变碎料时最该看到它）。</summary>
    private void AnnounceSalvage(string name, SalvageResult done)
    {
        if (done.Refunded.Count == 0)
        {
            _campToast.Show($"拆完了 {name}——太小了，一点渣都没剩下。", CampToast.Bad);
            return;
        }

        string got = string.Join("、", done.Refunded.Select(i => $"{i.DisplayName}×{i.MaterialQuantity}"));
        string tail = done.Refunded.Any(i => i.RefKey == SalvageLogic.ScrapWoodKey)
            ? "（废木料要配胶水才粘得回木料）"
            : "";
        _campToast.Show($"拆完 {name}：{got}{tail}", CampToast.Ok);
        GD.Print($"[拆解] 完工 {name} → {got}");
    }

    /// <summary>
    /// 工时制夜间生产推进（每帧，_Process 调）：仅当有在制任务且处夜间生产相位（NightAct）时，
    /// 按游戏分钟增量推进工时；满工时即完工产出。面板冻结时标时分钟键不变→零增量→不推进。
    /// 每槽按真实工人、真实设施矩形、Producing 角色与战斗态核对，离台/参战只暂停，不补算离线工时。
    /// </summary>
    private void TickCraftingWorktime()
    {
        if (_facilityJobs.Count == 0)
        {
            _craftLastMinuteKey = -1;
            _craftMinuteBudgets.Clear();
            return;
        }

        // 夜间生产相位（shift-sleep 落定"夜班生产相位"API 后可替换此判据）。
        bool productionPhase = _clock.CurrentPhase == DayPhase.NightAct;
        int key = _clock.ClockMinuteKey();
        if (!productionPhase || _craftLastMinuteKey < 0)
        {
            _craftLastMinuteKey = productionPhase ? key : -1;
            if (!productionPhase) _craftMinuteBudgets.Clear();
            return;
        }

        int delta = key - _craftLastMinuteKey;
        if (delta < 0) delta += 24 * 60; // 跨午夜环绕
        _craftLastMinuteKey = key;
        if (delta <= 0) return;

        // 3 级光环生产乘子（synergy-wiring）+ 次相位疲劳折减（批次6）：把流逝分钟乘生产系数累积到小数预算，取整分钟喂 Advance，
        // 余数留存，避免小数效率被吞掉。光环/疲劳系数由 Wiki 配置与状态提供。
        // 干活效率＝**操作能力**驱动（[通则·乘算]）：工时推进 = 流逝分钟 × 操作能力 × 道格光环 × 疲劳 × 山姆光环，**全程连乘**。
        // 操作能力 = Pawn.OperationCapability（残疾×饥饿×骨折的实时净值，健全且饱食者恰为 1.0 → 既有行为零回归）。
        // 山姆效果乘在这个**折损后的实际值**上（SamPerk.OperationCapabilityWithAura），而不是加到基准上——
        // 故断双手者操作能力为零时仍不能干活，残缺的代价不被光环补偿。
        var completedKeys = new List<string>();
        bool anyApplied = false;
        foreach (FacilityJobSlot slot in _facilityJobs.Jobs)
        {
            Pawn? worker = _survivors.FirstOrDefault(p => p.Id == slot.WorkerId);
            float mult = worker is not null
                ? (float)WorkEfficiencyOf(worker) * BondProductionMultFor(worker) * FatigueMultiplierFor(worker)
                : 1f;
            float budget = _craftMinuteBudgets.GetValueOrDefault(slot.SlotKey) + delta * mult;
            int wholeMinutes = (int)budget;
            _craftMinuteBudgets[slot.SlotKey] = budget - wholeMinutes;
            if (wholeMinutes <= 0) continue;

            Rect2? rect = FacilityRectForSlot(slot.SlotKey);
            bool atFacility = worker is { Alive: true } && rect is Rect2 r
                && r.Grow(PendingArriveMargin).HasPoint(worker.GlobalPosition);
            bool inCombat = worker?.HasActiveTarget == true || worker?.ProductionCombatControlEnabled == true;
            bool scheduled = worker is { Role: PawnRole.Producing }
                && ShiftSchedule.IsWorkPhaseFor(ShiftSchedule.ShiftFor(worker.Id, _todaysExpeditionIds), _clock.CurrentPhase);
            int applied = _facilityJobs.Advance(slot.SlotKey, wholeMinutes,
                workerAtAssignedFacility: atFacility,
                productionPhaseAllowsWork: scheduled,
                workerInCombat: inCombat);
            anyApplied |= applied > 0;
            if (slot.Job.IsComplete) completedKeys.Add(slot.SlotKey);
        }

        foreach (string slotKey in completedKeys)
        {
            FacilityJobSlot? completed = _facilityJobs.TakeCompleted(slotKey);
            if (completed is null) continue;
            _craftMinuteBudgets.Remove(slotKey);
            Pawn? worker = _survivors.FirstOrDefault(p => p.Id == completed.WorkerId);
            if (worker is not null)
            {
                worker.ProducingStationing = false;
                worker.ProductionCombatControlEnabled = false;
                if (worker.Role == PawnRole.Producing)
                    worker.Role = PawnRole.Idle;
            }
            SyncProductionAssignments();
            CompleteFacilityJob(completed);
        }

        if (anyApplied && _craftingOpen) RefreshCrafting();
    }

    /// <summary>在制任务完工：产物按 <see cref="CraftOutputFactory"/> 分类入库、提示、清空任务、刷新面板。</summary>
    private void CompleteFacilityJob(FacilityJobSlot completed)
    {
        PlaytestResourceSnapshot before = BeginPlaytestProductionSnapshot();
        CompleteFacilityJobCore(completed);
        RecordPlaytestProduction(completed.Job.RecipeId, before);
    }

    private void CompleteFacilityJobCore(FacilityJobSlot completed)
    {
        CraftingJob job = completed.Job;
        Pawn? completingWorker = _survivors.FirstOrDefault(p => p.Id == completed.WorkerId);
        if (completingWorker is not null)
            CombatVfxBurst.SpawnWorkDust(completingWorker.PresentationLayer, completingWorker.GlobalPosition, 1.4f);

        // 拆解分流：任务 id 带 "salvage:" 前缀的不是配方，是"把东西变回材料"。三种拆法各走各的清场：
        //   door#<下标> → 门（返还 + DestroyStructure 抹掉本体）
        //   prop#<名字> → 家具（返还 + RemoveFurniture 抹掉本体）
        //   其余        → 库存里的物品（那件东西开工时已拿走，这里只把返还入库）
        if (SalvageLogic.TargetKeyOf(job.RecipeId) is { } salvageTarget)
        {
            if (SalvageLogic.StructureIndexOf(salvageTarget) is { } structureIndex)
            {
                CompleteStructureSalvage(structureIndex);
            }
            else if (SalvageLogic.FurnitureNameOf(salvageTarget) is { } furnitureName)
            {
                CompleteFurnitureSalvage(furnitureName);
            }
            else
            {
                CompleteSalvageJob(salvageTarget);
            }
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // [批次21·T14] 烹饪分流：任务 id 带 "cook:" 前缀的不是配方，是"把一锅生料炖成 N 份饭"。
        // 食材开工时已扣，这里只把份数加进营地食物。份数在下单那一刻就编进了 id ⇒ 中途卸锅也不缩水。
        if (CookingLogic.PortionsOf(job.RecipeId) is { } cookedPortions)
        {
            CompleteCookJob(cookedPortions);
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // [T72] 种植分流：任务 id 带 "plant:" 前缀的不是配方，是"往某座菜园下一颗种薯"。
        // 种薯开工时已扣，这里只把下一个空格的 84h 计时器点亮（正文在 CampMain.Farming.cs）。
        if (job.RecipeId.StartsWith(CropPlotLogic.PlantJobPrefix, StringComparison.Ordinal))
        {
            CompletePlantJob(job.RecipeId[CropPlotLogic.PlantJobPrefix.Length..], completingWorker);
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // [T67] 宰杀分流：任务 id 带 "butcher:" 前缀的不是配方，是"把一只猎物宰成肉 + 副产物"。
        // 猎物在完工那一刻由 ButcheryRuntime.Butcher 原子扣+产（含双倍掷点）。正文在 CampMain.Butchery.cs。
        if (job.RecipeId.StartsWith(ButcherJobPrefix, StringComparison.Ordinal))
        {
            CompleteButcherJob(job.RecipeId[ButcherJobPrefix.Length..]);
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // 改装分流：任务 id 带 "weaponmod:" 前缀的不是配方，是"把一把枪改成另一把枪"。
        // 基础武器与材料开工时已扣，这里只合成变体 → 登记身份（不登记就装不上、存不住）→ 入库。
        if (WeaponModLogic.TargetOf(job.RecipeId) is not null)
        {
            WeaponModResult mod = CraftingService.CompleteWeaponModJob(job.RecipeId, _inventory);
            if (mod.Success)
            {
                string modName = mod.Produced?.DisplayName ?? "改装武器";
                _campToast.Show($"改装完成：{modName}", CampToast.Ok);
                GD.Print($"[改装] 完工 → {modName}");
            }
            else
            {
                // 走到这说明规则在开工后变了（改装被删了之类）。料已扣，只能如实报——不静默吞掉。
                _campToast.Show($"改装失败：{mod.FailureReason}", CampToast.Bad);
                GD.Print($"[改装] 完工失败：{mod.FailureReason}");
            }
            if (_craftingOpen) RefreshCrafting();
            if (_stashOpen) OpenStash(null);
            return;
        }

        RecipeData? recipe = RecipeBook.Find(job.RecipeId);
        if (recipe is null)
        {
            GD.Print($"[制作] 完工但配方丢失：{job.RecipeId}");
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // 改装台分流：它**不进库存**（一张实心工作案不是能揣兜里的东西），完工即立在车间的固定锚点上。
        if (recipe.Id == WeaponModLogic.BenchRecipeId)
        {
            CompleteModBenchBuild();
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        if (recipe.Id == WeaponBench.RecipeId)
        {
            CompleteWeaponBenchBuild();
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // [批次21·T14] 烹饪台分流：同上——一座砌好的灶揣不进兜里，完工即砌在厨房的固定锚点上。
        if (recipe.Id == CookStation.RecipeId)
        {
            CompleteCookStationBuild();
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        // [T67] 宰杀台分流：它是简易宰杀点的**升级**——不进库存，完工即在简易点原地顶替它（正文在 CampMain.Butchery.cs）。
        if (recipe.Id == ButcherStation.TableRecipeId)
        {
            CompleteButcherTableBuild();
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        IReadOnlyList<Item> produced = CraftingService.CompleteJob(job, recipe, _inventory, CraftOutputFactory.Create);
        string products = string.Join("、", produced.Select(p => p.DisplayName));
        _campToast.Show($"制作完成：{products}", CampToast.Ok);
        GD.Print($"[制作] 完工 {recipe.DisplayName} → {products}");
        if (_craftingOpen) RefreshCrafting();
    }

    /// <summary>
    /// 拆解完工：返还材料入库（那件东西在开工时已拿走）。木材那份会明说"废木料还得配胶水才粘得回木料"——
    /// 玩家第一次拆完木头看见一半变成碎料时，最该看到的就是这句话。
    /// </summary>
    private void CompleteSalvageJob(string itemKey)
    {
        SalvageResult done = SalvageService.CompleteSalvage(itemKey, _inventory);
        string name = SalvageLogic.RecipeFor(itemKey)?.DisplayName ?? itemKey;

        if (!done.Success)
        {
            _campToast.Show($"拆解失败：{done.FailureReason}", CampToast.Bad);
            GD.Print($"[拆解] 完工但结算失败：{itemKey}（{done.FailureReason}）");
            if (_craftingOpen) RefreshCrafting();
            return;
        }

        if (done.Refunded.Count == 0)
        {
            _campToast.Show($"拆完了 {name}——太小了，一点渣都没剩下。", CampToast.Bad);
        }
        else
        {
            string got = string.Join("、", done.Refunded.Select(i => $"{i.DisplayName}×{i.MaterialQuantity}"));
            bool hasScrap = done.Refunded.Any(i => i.RefKey == SalvageLogic.ScrapWoodKey);
            string tail = hasScrap ? "（废木料要配胶水才粘得回木料）" : "";
            _campToast.Show($"拆完 {name}：{got}{tail}", CampToast.Ok);
        }

        GD.Print($"[拆解] 完工 {name} → {string.Join("、", done.Refunded.Select(i => $"{i.DisplayName}×{i.MaterialQuantity}"))}");
        if (_craftingOpen) RefreshCrafting();
        if (_stashOpen) OpenStash(null); // 库存面板开着 → 材料立刻上屏
    }

    /// <summary>
    /// 面板「改装」→ 下一单改装（<see cref="CraftingService.StartWeaponModJob"/>）：过门槛（改装台/材料/合成合法性）→
    /// **开工即扣**（拿走基础武器 + 扣材料）→ 存为在制任务，由夜间生产相位推进工时，满了在
    /// <see cref="CompleteActiveCraftingJob"/> 里出货。改装**不再是点一下白送**。
    /// <para>
    /// 与制作/拆解**共用同一条在制队列**（一座营地一次只干一件活）——沿用拆解与制作互斥的既有语义。
    /// </para>
    /// </summary>
    private void OnModApplyRequested(string baseWeaponRefKey, IReadOnlyList<string> modNames, Pawn crafter)
    {
        if (modNames.Contains(WeaponModCatalog.CrossbowShield().Name)
            && !BookPassiveEffects.WeaponModUnlocked("crossbow_shield", crafter.HasReadBook))
        {
            _campToast.Show("弩盾改装需要制作者先读完《意大利编年史》。", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        if (!CanStartFacilityJob(FacilityJobKeys.MainModBench, crafter, out string busyWhy))
        {
            _campToast.Show(busyWhy, CampToast.Bad);
            return;
        }

        WeaponModStartResult start = CraftingService.StartWeaponModJob(
            baseWeaponRefKey, modNames, _inventory, HasModBench);

        if (!start.Success)
        {
            _campToast.Show($"改装失败：{start.FailureText}", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        StartFacilityJob(FacilityJobKeys.MainModBench, start.Job!, crafter);
        _craftLastMinuteKey = -1;

        _campToast.Show(
            $"{crafter.DisplayName} 开始改装 {baseWeaponRefKey}（{start.Job!.TotalWorkMinutes} 分钟）",
            CampToast.Ok);
        GD.Print($"[改装] 下单 {baseWeaponRefKey} + {string.Join("・", modNames)}（{start.Job.TotalWorkMinutes} 分）");
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

    // [批次21·impl-medicine] 「给谁用什么」的下令上下文：由角色侧入口（右键点伤员 / 角色面板「医务」）指定，
    // 覆盖 RefreshMedical 默认的"病人=当前选中者"。关面板即清——一次下令只管这一次。
    private Pawn? _medicalPatient;  // 本次下令要治的人（null=回落到 _selectedPawn）
    private Pawn? _medicalMedic;    // 本次下令派去动手的人（null=面板按默认挑一个非病人）

    /// <summary>
    /// [批次21·impl-medicine·T11] **角色侧医务下令**：「让 <paramref name="medic"/> 去治 <paramref name="patient"/>」——
    /// 开医务面板并把病人/施术者**预选好**，玩家进去就能直接给他用药/做手术/指派感染疗程，不必在两个下拉里再找一遍人。
    /// <para>
    /// 两个入口都走这里（不发明新交互，沿用既有的右键下令 + 右侧角色面板两条老路）：
    ///   · 右键点场上的自己人（<see cref="HandleRightClick"/>）：病人=被点的人，施术者=当前选中的人；
    ///   · 角色面板的「医务」按钮（<see cref="ShowInspect"/>）：病人=面板里这个人，施术者交给面板挑。
    /// </para>
    /// 没伤没病也没缺肢的人**不开面板**，只回一句"他好得很"——空面板不是信息（判定走
    /// <see cref="MedicalOrderLogic.NeedsMedicalAttention"/>，与库存无关：没药也要让玩家看见他伤成什么样）。
    /// </summary>
    private void OpenMedicalFor(Pawn patient, Pawn? medic)
    {
        if (_gameOver || !patient.Alive)
            return;

        if (!MedicalOrderLogic.NeedsMedicalAttention(patient.Health, patient.Inspect().ProstheticSlots.Any(s => s.CanEquip)))
        {
            _campToast.Show($"{patient.DisplayName} 没伤没病，用不着医务。", CampToast.Ok);
            return;
        }

        _medicalPatient = patient;
        _medicalMedic = ReferenceEquals(medic, patient) ? null : medic; // 点自己 ⇒ 施术者交给面板挑别人（自体手术池要打 0.6 折）
        OpenMedical();
    }

    /// <summary>
    /// 重刷医疗面板数据（手术/用药后调，反映扣掉的耗材与更新后的伤病集）。病人候选=存活幸存者。
    /// <para>
    /// [批次21·impl-bedrest] 两处补强：
    ///   · <c>hasBed</c> = 真实床位实况 ⇒「床上」不再是玩家随手勾的空头支票，得先让他走过去躺下（右键点床）；
    ///   · <c>focus</c> = **当前选中的角色** ⇒ 这就是"选中角色 → 给他吃药/用医疗物资"那条路：选好人再按 M，
    ///     面板直接翻到他那一页，而不是回到名单第一个再让玩家自己找。
    /// </para>
    /// </summary>
    private void RefreshMedical()
        => _medicalPanel.ShowFor(
            _survivors.Where(p => p.Alive).ToList(), _inventory,
            hasBed: p => _beds.HasBed(p.Id),
            // [批次21·impl-medicine] 角色侧下令（右键点伤员 / 角色面板「医务」）指定的病人与施术者优先；
            // 没有下令上下文时回落到 impl-bedrest 的老口径：病人=当前选中者（选好人再按 M，面板直接翻到他）。
            focus: _medicalPatient ?? _selectedPawn,
            medic: _medicalMedic);

    /// <summary>关医疗面板：恢复时标、清瞬时提示、清掉本次下令的病人/施术者上下文（一次下令只管这一次）。</summary>
    private void CloseMedical()
    {
        _medicalPanel.Visible = false;
        _medicalOpen = false;
        _medicalPatient = null;
        _medicalMedic = null;
        _medicalPanel.ResetPreselect(); // 关一次面板 = 一次下令结束：下回右键点谁，面板就该翻到谁
        _campToast.Hide();
        RestorePanelTimeState(_prevMedicalSpeed, _prevMedicalPaused);
    }

    /// <summary>
    /// 手术开工预检：只读伤情、床位、库存与施术者能力，绝不 roll、扣料、改伤情或累计护士台数。
    /// 真正结算只在 <see cref="CompleteTimedSurgery"/> 取得状态机的一次性结算权后发生。
    /// </summary>
    private void TryBeginTimedSurgery(
        SurgeryKind kind,
        Pawn patient,
        Pawn surgeon,
        HealthCondition? condition,
        (string Key, BodyRegion Region)? prostheticTarget,
        ProstheticGrade? prostheticGrade,
        IReadOnlyList<string> requestedMaterials)
    {
        if (_surgeryJob is not null)
        {
            _campToast.Show("已有一台手术正在进行。", CampToast.Bad);
            return;
        }
        if (!patient.Alive || !surgeon.Alive)
        {
            _campToast.Show("病人或施术者已无法进行手术。", CampToast.Bad);
            return;
        }
        bool selfSurgery = ReferenceEquals(patient, surgeon);
        if ((!surgeon.IsControllable && !(selfSurgery && surgeon.Role == PawnRole.Bedrest))
            || patient.SurgeryOccupied || surgeon.SurgeryOccupied
            || (!selfSurgery && patient.Role is not (PawnRole.Idle or PawnRole.Bedrest)))
        {
            _campToast.Show("病人或施术者正在执行别的任务。", CampToast.Bad);
            return;
        }

        if (kind == SurgeryKind.Treatment
            && (condition is null
                || !patient.Health.Conditions.Contains(condition)
                || condition.Type is not (HealthConditionType.Bleeding or HealthConditionType.Fracture)))
        {
            _campToast.Show("目标伤情已经不存在，手术无法开始。", CampToast.Bad);
            RefreshMedical();
            return;
        }
        if (kind == SurgeryKind.Treatment && condition!.IsOperated && !patient.Health.CanRedoSurgery(condition))
        {
            _campToast.Show(SurgeryResult.RedoTooSoonMessage, CampToast.Bad);
            return;
        }
        if (kind == SurgeryKind.Amputation
            && (condition is null
                || !patient.Health.Conditions.Contains(condition)
                || condition.Type != HealthConditionType.Infection
                || !condition.OnLimb))
        {
            _campToast.Show("目标感染已经不存在，截肢无法开始。", CampToast.Bad);
            RefreshMedical();
            return;
        }
        if (kind == SurgeryKind.Prosthetic
            && (prostheticTarget is null
                || prostheticGrade is null
                || !patient.Inspect().ProstheticSlots.Any(s => s.CanEquip
                    && s.UnitPartName == prostheticTarget.Value.Key
                    && s.ReplacesRegion == prostheticTarget.Value.Region)))
        {
            _campToast.Show("目标缺肢已经不再是可安装状态。", CampToast.Bad);
            RefreshMedical();
            return;
        }

        HealthConditionType supplyTarget = kind == SurgeryKind.Treatment
            ? condition!.Type
            : HealthConditionType.Bleeding;
        List<SurgerySupply> supplies = requestedMaterials
            .Select(SurgeryCatalog.For)
            .Where(s => s is { } supply && supply.CanTreat(supplyTarget))
            .Select(s => s!.Value)
            .ToList();
        if (supplies.Any(s => s.Exclusive) && supplies.Count > 1)
        {
            _campToast.Show("急救包为独占耗材，不可与其他耗材叠加。", CampToast.Bad);
            return;
        }
        List<string> materialKeys = supplies.Select(s => s.MaterialKey).ToList();
        if (!HasSurgeryMaterials(materialKeys))
        {
            _campToast.Show("手术耗材不足。", CampToast.Bad);
            RefreshMedical();
            return;
        }

        // 与 HealthConditionSet 三个结算入口同一公式，但这里只预览门槛，零随机、零写入。
        int alwaysBookPoints = MedicalBookPoints.SumAlways(surgeon.ReadBookIds);
        int noSupplyBookPoints = materialKeys.Count == 0 ? MedicalBookPoints.SumWithoutSupplies(surgeon.ReadBookIds) : 0;
        int basePoints = NightingalePerk.SurgeryBasePoints(
            surgeon.Perks.IsNightingale, _storyFlags.Has(NurseRecruit.L3LegacyFlag));
        int rawPoints = basePoints
            + (RealOnBed(patient) ? HealthConditionSet.BedBonusPoints : 0)
            + supplies.Sum(s => s.Points)
            + alwaysBookPoints
            + noSupplyBookPoints;
        double selfFactor = selfSurgery ? HealthConditionSet.SelfSurgeryFactor : 1.0;
        int pool = (int)Math.Round(rawPoints * Math.Clamp(surgeon.OperationCapability, 0.0, 1.0) * selfFactor,
            MidpointRounding.AwayFromZero);
        if (pool < HealthConditionSet.SurgeryMinPoints)
        {
            _campToast.Show(SurgeryResult.NotAllowedMessage, CampToast.Bad);
            return;
        }

        double speed = BookPassiveEffects.SurgerySpeedMultiplier(surgeon.HasReadBook);
        string? lockedBed = _beds.BedOf(patient.Id);
        SurgeryJob job = kind == SurgeryKind.Prosthetic
            ? SurgeryJob.ForProsthetic(surgeon.Id, patient.Id, prostheticTarget!.Value.Key,
                prostheticTarget.Value.Region, prostheticGrade!.Value, materialKeys, speed,
                requiresBedRetention: lockedBed is not null)
            : SurgeryJob.ForCondition(kind, surgeon.Id, patient.Id, condition!.Type, condition.BodyPart, materialKeys, speed,
                requiresBedRetention: lockedBed is not null);

        if (!BeginSurgeryApproach(job, patient, surgeon, lockedBed))
            return;

        // 医疗面板是冻结模态；下令后关闭并恢复原时标，医生先真实走到手术位，抵达后才计工时。
        CloseMedical();
        _campToast.Show(
            selfSurgery
                ? $"{surgeon.DisplayName} 正在准备自体手术。"
                : $"{surgeon.DisplayName} 正在走向 {patient.DisplayName} 的手术位。",
            CampToast.Ok);
    }

    private bool BeginSurgeryApproach(SurgeryJob job, Pawn patient, Pawn surgeon, string? lockedBed)
    {
        Vector2 stand;
        if (ReferenceEquals(patient, surgeon))
        {
            stand = surgeon.GlobalPosition;
        }
        else if (lockedBed is not null)
        {
            ContainerRef? bed = _containers.FirstOrDefault(c => c.Role == "bed" && c.Name == lockedBed);
            if (bed is null)
            {
                _campToast.Show("找不到病人所在的床，手术无法开始。", CampToast.Bad);
                return false;
            }
            // 病人已经躺在床边交互点；医生锁定同侧相邻位，沿床沿错开一个角色身位，避免两人互撞顶死。
            Vector2 outward = patient.GlobalPosition - bed.Rect.GetCenter();
            if (outward.LengthSquared() < 0.01f) outward = Vector2.Right;
            Vector2 tangent = new(-outward.Y, outward.X);
            if (tangent.Dot(surgeon.GlobalPosition - patient.GlobalPosition) < 0f) tangent = -tangent;
            stand = patient.GlobalPosition + tangent.Normalized() * (surgeon.Radius + patient.Radius + 8f);
        }
        else
        {
            Vector2 outward = surgeon.GlobalPosition - patient.GlobalPosition;
            if (outward.LengthSquared() < 0.01f) outward = Vector2.Right;
            stand = patient.GlobalPosition + outward.Normalized() * (surgeon.Radius + patient.Radius + 8f);
        }

        if (!surgeon.CanReach(stand))
        {
            _campToast.Show("无法到达手术位。", CampToast.Bad);
            return false;
        }

        _surgeryJob = job;
        _surgeryStandPoint = stand;
        _surgeryPatientLockedPosition = patient.GlobalPosition;
        _surgeryLockedBedKey = lockedBed;
        _surgeryApproachElapsed = 0;
        _surgeryLastMinuteKey = -1;
        patient.CancelOrders();
        surgeon.CancelOrders();
        patient.SurgeryOccupied = true;
        surgeon.SurgeryOccupied = true;
        ReleaseNightAssignmentsOf(patient);
        if (!ReferenceEquals(patient, surgeon)) ReleaseNightAssignmentsOf(surgeon);
        if (!ReferenceEquals(patient, surgeon)) surgeon.CommandMoveTo(stand);
        return true;
    }

    private bool HasSurgeryMaterials(IEnumerable<string> materialKeys)
        => materialKeys.GroupBy(k => k).All(g =>
            CraftingService.MaterialTotal(_inventory.ByCategory(ItemCategory.Material), g.Key) >= g.Count());

    private HealthCondition? FindSurgeryCondition(Pawn patient, SurgeryJob job)
        => patient.Health.Conditions.FirstOrDefault(c =>
            c.Type == job.ConditionType && c.BodyPart == job.BodyPartKey);

    private bool SurgeryTargetExists(Pawn patient, SurgeryJob job)
        => job.Kind == SurgeryKind.Prosthetic
            ? patient.Inspect().ProstheticSlots.Any(s => s.CanEquip
                && s.UnitPartName == job.BodyPartKey
                && s.ReplacesRegion == job.ProstheticRegion)
            : FindSurgeryCondition(patient, job) is not null;

    /// <summary>按世界游戏分钟推进在制手术。这里不调用 TickBleed；Actor 的既有物理循环是唯一流血事实源。</summary>
    private void TickSurgeryWorktime()
    {
        if (_surgeryJob is null)
        {
            _surgeryLastMinuteKey = -1;
            return;
        }

        SurgeryJob job = _surgeryJob;
        Pawn? patient = _survivors.FirstOrDefault(p => p.Id == job.PatientId);
        Pawn? surgeon = _survivors.FirstOrDefault(p => p.Id == job.SurgeonId);
        bool patientAlive = patient is { Alive: true };
        bool surgeonAlive = surgeon is { Alive: true };
        bool targetExists = patient is not null && SurgeryTargetExists(patient, job);
        bool patientAtPosition = patient is not null
            && patient.GlobalPosition.DistanceTo(_surgeryPatientLockedPosition) <= 12f;
        bool surgeonAtPosition = surgeon is not null
            && surgeon.GlobalPosition.DistanceTo(_surgeryStandPoint) <= PendingArriveMargin;
        bool bedStillOccupied = _surgeryLockedBedKey is null
            || (_beds.BedOf(job.PatientId) == _surgeryLockedBedKey
                && _beds.OccupantOf(_surgeryLockedBedKey) == job.PatientId);

        if (!patientAlive || !surgeonAlive || !targetExists || !patientAtPosition || !bedStillOccupied)
        {
            InterruptTimedSurgery("手术中断：病人、施术者、床位或目标伤情已不再满足条件。");
            return;
        }

        if (!job.IsOperating)
        {
            if (!HasSurgeryMaterials(job.MaterialKeys))
            {
                InterruptTimedSurgery("手术取消：抵达前耗材已经不足。");
                return;
            }
            _surgeryApproachElapsed += GetProcessDeltaTime();
            if (surgeonAtPosition && surgeon!.IsNavigationFinished())
            {
                SurgeryAdvanceStatus start = job.TryStartOperating(true, patientAtPosition, bedStillOccupied);
                if (start == SurgeryAdvanceStatus.InProgress)
                {
                    surgeon.CancelOrders();
                    patient!.VisualActivity = PawnVisualActivity.Lying;
                    surgeon.VisualActivity = ReferenceEquals(patient, surgeon)
                        ? PawnVisualActivity.Lying
                        : PawnVisualActivity.Working;
                    _surgeryLastMinuteKey = _clock.ClockMinuteKey();
                    _campToast.Show($"{surgeon.DisplayName} 已抵达手术位，开始为 {patient!.DisplayName} 手术（约 {job.TotalMinutes} 游戏分钟）。", CampToast.Ok);
                }
                return;
            }
            if (_surgeryApproachElapsed > PendingInteractTimeout
                || (surgeon!.IsNavigationFinished() && _surgeryApproachElapsed > 0.5))
            {
                InterruptTimedSurgery("无法到达手术位，手术已取消。");
            }
            return;
        }

        if (!SurgerySpatiallyValid(surgeon!, patient!, surgeonAtPosition, patientAtPosition, bedStillOccupied))
        {
            InterruptTimedSurgery("手术中断：施术者或病人离开了手术位。");
            return;
        }

        int key = _clock.ClockMinuteKey();
        int elapsedMinutes = 0;
        if (_surgeryLastMinuteKey < 0)
        {
            _surgeryLastMinuteKey = key;
        }
        else if (!_clock.Paused)
        {
            elapsedMinutes = key - _surgeryLastMinuteKey;
            if (elapsedMinutes < 0) elapsedMinutes += 24 * 60;
            _surgeryLastMinuteKey = key;
        }

        SurgeryAdvanceStatus status = job.AdvanceSpatial(elapsedMinutes, _clock.Paused,
            patientAlive, surgeonAlive, targetExists, surgeonAtPosition, patientAtPosition, bedStillOccupied);
        if (status == SurgeryAdvanceStatus.Interrupted)
        {
            InterruptTimedSurgery("手术中断：施术者或病人离开了手术位。");
            return;
        }
        if (status != SurgeryAdvanceStatus.Ready)
            return;

        // 到点仍需确认耗材没被其它行为花掉；不足则中断，绝不凭空扣负数或照常结算。
        if (!HasSurgeryMaterials(job.MaterialKeys))
        {
            InterruptTimedSurgery("手术中断：预留耗材已经不足。");
            return;
        }
        if (!job.TryClaimSettlement())
            return;

        ReleaseSurgeryParticipants();
        _surgeryJob = null; // 先清在制，再调用副作用结算；即使下一帧重入也不可能重复扣料/计台数。
        _surgeryLastMinuteKey = -1;
        CompleteTimedSurgery(job, patient!, surgeon!, FindSurgeryCondition(patient!, job));
    }

    private static bool SurgerySpatiallyValid(Pawn surgeon, Pawn patient,
        bool surgeonAtPosition, bool patientAtPosition, bool bedStillOccupied)
        => surgeon.Alive && patient.Alive && surgeonAtPosition && patientAtPosition && bedStillOccupied;

    private void InterruptTimedSurgery(string message)
    {
        ReleaseSurgeryParticipants();
        _surgeryJob = null;
        _surgeryLastMinuteKey = -1;
        _surgeryLockedBedKey = null;
        _surgeryApproachElapsed = 0;
        if (!_gameOver) _campToast.Show(message, CampToast.Bad);
    }

    private void ReleaseSurgeryParticipants()
    {
        if (_surgeryJob is not { } job) return;
        Pawn? patient = _survivors.FirstOrDefault(p => p.Id == job.PatientId);
        Pawn? surgeon = _survivors.FirstOrDefault(p => p.Id == job.SurgeonId);
        if (patient is not null)
        {
            patient.SurgeryOccupied = false;
            patient.VisualActivity = PawnVisualActivity.None;
        }
        if (surgeon is not null)
        {
            surgeon.SurgeryOccupied = false;
            surgeon.VisualActivity = PawnVisualActivity.None;
            surgeon.CancelOrders();
        }
    }

    private void CompleteTimedSurgery(SurgeryJob job, Pawn patient, Pawn surgeon, HealthCondition? condition)
    {
        int bookBonus = MedicalBookPoints.SumAlways(surgeon.ReadBookIds);
        int barehandedBookBonus = MedicalBookPoints.SumWithoutSupplies(surgeon.ReadBookIds);
        bool self = ReferenceEquals(patient, surgeon);
        int basePoints = NightingalePerk.SurgeryBasePoints(
            surgeon.Perks.IsNightingale, _storyFlags.Has(NurseRecruit.L3LegacyFlag));

        SurgeryResult result = job.Kind switch
        {
            SurgeryKind.Treatment => patient.Health.PerformSurgery(
                condition!, job.MaterialKeys, RealOnBed(patient), _healthRng,
                bookBonus, self, surgeon.OperationCapability, basePoints, barehandedBookBonus),
            SurgeryKind.Amputation => patient.AmputateInfectedLimb(
                condition!, job.MaterialKeys, RealOnBed(patient), _healthRng,
                bookBonus, self, surgeon.OperationCapability, basePoints, barehandedBookBonus),
            SurgeryKind.Prosthetic => patient.InstallProstheticSurgery(
                job.ProstheticRegion!.Value, job.ProstheticGrade!.Value, job.MaterialKeys,
                RealOnBed(patient), _healthRng,
                bookBonus, self, surgeon.OperationCapability, basePoints, barehandedBookBonus),
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (result.Status is not (SurgeryStatus.Success or SurgeryStatus.Failed))
        {
            _campToast.Show(result.PlayerMessage ?? SurgeryResult.NotAllowedMessage, CampToast.Bad);
            return;
        }

        // 真正施术才计一台；状态机的一次性结算权保证这里至多执行一次。
        if (surgeon.Perks.IsNightingale)
        {
            int count = NightingalePerk.RecordSurgery(_storyFlags);
            if (NightingalePerk.EvaluateLevel(count) >= 3)
                _storyFlags.Set(NurseRecruit.L3LegacyFlag, "true");
        }
        ConsumeMaterials(result.ConsumedMaterials);

        string message = job.Kind switch
        {
            SurgeryKind.Treatment => $"{surgeon.DisplayName} 为 {patient.DisplayName} 手术：{FuzzySurgeryOutcome(result)}",
            SurgeryKind.Amputation => result.Success
                ? $"{surgeon.DisplayName} 为 {patient.DisplayName} 截肢：切除了{job.BodyPartKey}，感染中止"
                : $"{surgeon.DisplayName} 为 {patient.DisplayName} 截肢失败，得重来",
            SurgeryKind.Prosthetic => result.Success
                ? $"{surgeon.DisplayName} 为 {patient.DisplayName} 装上了假肢，行动恢复了些"
                : $"{surgeon.DisplayName} 为 {patient.DisplayName} 安装假肢失败，得重来",
            _ => "手术结束",
        };
        _campToast.Show(message, result.Success ? CampToast.Ok : CampToast.Bad);
        GD.Print($"[手术结算] {surgeon.DisplayName}→{patient.DisplayName} {job.Kind}/{job.BodyPartKey} {(result.Success ? "成功" : "失败")}");
    }

    /// <summary>
    /// 面板「手术」→ 算 施术者医疗书加点 / 操作能力 / 是否自体，调 <see cref="HealthConditionSet.PerformSurgery"/>：
    /// 门槛未过→显示"现状不支持进行这场手术"（零消耗）；成功/失败→按 result 扣 <see cref="SurgeryResult.ConsumedMaterials"/>、
    /// 给模糊结果提示（**不显点数/roll/效率**）。随后刷新面板。
    /// </summary>
    private void OnSurgeryRequested(Pawn patient, HealthCondition condition, IReadOnlyList<string> materials, bool onBed, Pawn surgeon)
    {
        _ = onBed; // 床位只认实时 BedRegistry，不信面板快照。
        TryBeginTimedSurgery(SurgeryKind.Treatment, patient, surgeon, condition, null, null, materials);
    }

    /// <summary>
    /// [SPEC-B14-补7] 面板「截肢」→ 对感染的肢体走 <see cref="Pawn.AmputateInfectedLimb"/>（既有手术点数池/耗材/成败流程，护士基础点数同手术）：
    /// 成功=断肢+感染双条清零、失败=耗材照耗需重来；门槛未过给提示、零消耗。玩家抉择，无自动触发。随后刷新面板。
    /// </summary>
    private void OnAmputationRequested(Pawn patient, HealthCondition infection, IReadOnlyList<string> materials, bool onBed, Pawn surgeon)
    {
        _ = onBed;
        TryBeginTimedSurgery(SurgeryKind.Amputation, patient, surgeon, infection, null, null, materials);
    }

    /// <summary>
    /// [SPEC-B14-补8] 面板「安装假肢」→ 走 <see cref="Pawn.InstallProstheticSurgery"/>（既有手术点数池/耗材/成败，护士基础点数同手术）：
    /// 成功=假肢就位（能力恢复）、失败=按手术失败惯例（假肢本体默认不损耗，可重试——现假肢非库存物品，本体消耗待假肢物品化后接，标待确认）。随后刷新面板。
    /// </summary>
    private void OnProstheticSurgeryRequested(Pawn patient, string bodyPartKey, BodyRegion region, ProstheticGrade grade, IReadOnlyList<string> materials, bool onBed, Pawn surgeon)
    {
        _ = onBed;
        TryBeginTimedSurgery(SurgeryKind.Prosthetic, patient, surgeon, null, (bodyPartKey, region), grade, materials);
    }

    /// <summary>[SPEC-B14-补3] 面板「指派感染疗程」→ 记该幸存者的感染疗程药档（每昼夜黎明自动用药，见 <see cref="AutoTreatInfectionCourse"/>）。当昼夜不立即用药。</summary>
    private void OnInfectionCourseAssigned(Pawn patient, string medKey)
    {
        patient.AssignInfectionTreatment(medKey);
        _campToast.Show($"已为 {patient.DisplayName} 指派疗程：{Materials.Find(medKey)?.DisplayName ?? medKey}（每日黎明自动用药）", CampToast.Ok);
        RefreshMedical();
    }

    /// <summary>[SPEC-B14-补3] 面板「停止感染疗程」→ 清指派，当昼夜起不再自动用药。</summary>
    private void OnInfectionCourseCancelled(Pawn patient)
    {
        patient.ClearInfectionTreatment();
        _campToast.Show($"已停止 {patient.DisplayName} 的感染疗程", CampToast.Ok);
        RefreshMedical();
    }

    /// <summary>[SPEC-B14-补2] 面板「喝玫瑰果茶」→ 扣一份玫瑰果茶、激活 Wiki 配置的恢复加成。缺茶/已在 buff 中不消耗。</summary>
    private void OnRosehipTeaRequested(Pawn patient)
    {
        if (patient.HasRosehipTeaHealBuff)
        {
            RefreshMedical();
            return;
        }
        if (CraftingService.MaterialTotal(_inventory.ByCategory(ItemCategory.Material), "rosehip_tea") <= 0)
        {
            _campToast.Show("缺玫瑰果茶", CampToast.Bad);
            RefreshMedical();
            return;
        }
        ConsumeMaterials(new[] { "rosehip_tea" });
        patient.DrinkRosehipTea();
        _campToast.Show($"{patient.DisplayName} 喝下玫瑰果茶，恢复加快（+{Pawn.RosehipTeaHealBonusPct:0.#}% · 持续至下一次黎明）", CampToast.Ok);
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

    /// <summary>
    /// 建筑门规格。side/gapStart/gapWidth 定门在哪面墙、开多宽；后三项是**门系统的数据驱动配置**
    /// （项目铁律：代码只写规则，数值是数据）。
    /// </summary>
    private struct DoorSpec
    {
        public string? side { get; set; }
        public double gapStart { get; set; }
        public double gapWidth { get; set; }

        /// <summary>初始状态："open"（缺省）/ "closed" / "locked" / "barred"。营内建筑一律缺省 open，见 AddDoorDecor 的零回归理由。</summary>
        public string? state { get; set; }

        /// <summary>
        /// 这扇门有没有门闩（缺省 false）。有闩的门**关上即闩上**（不做单独的"闩门"交互）。
        /// 闩着 = 自己人一抬就开，**劫掠者推不开也撬不了、只能砸**。营地大门恒为 true（见 AddGate）。
        /// </summary>
        public bool barrable { get; set; }

        /// <summary>锁的档次："simple" / "standard" / "sturdy"；缺省 = 没装锁。只有 state=locked 时才有意义。</summary>
        public string? lockTier { get; set; }

        /// <summary>门体耐久档："wood"（缺省，60HP）/ "reinforced"（120）/ "metal"（220）。决定丧尸砸穿它要几爪。</summary>
        public string? tier { get; set; }
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
        // role=corpse 专用：先弹哪段叙事（NarrativeSpotRegistry 的调查点 id）。看过之后该点才退化成普通 loot（扒衣服）。
        public string? narrativeId { get; set; }
        // 半身掩体（桌子/椅子/沙袋）：true = 建成**非实心矮物**（不建碰撞/不挖导航洞/不断视线）并登记进
        // 掩体场——躲其后挨的**远程**攻击按 Wiki 配置整发判无效（方向性，见 CoverLogic）。**与实心互斥**：
        // 标了 cover 就不会走 AddSolid（实心物在墙层、子弹撞上就没了，掩体概率会是死代码）。默认 false=旧行为。
        public bool cover { get; set; }
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
        public string? weapon { get; set; }   // 起始武器：无/pistol/dagger/club（旧 pistol 布尔已退役，见 StartingWeaponInfo.FromKey）
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
