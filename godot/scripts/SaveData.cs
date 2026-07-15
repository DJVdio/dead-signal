using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。存档的**数据模型**住在这里，
// 「怎么写盘」住在 SaveManager（Godot 层），「怎么变成字节」住在 SaveCodec（也是纯逻辑）。
//
// ★ 为什么 DTO 要脱离 Godot：存档一旦绑上 Godot 节点，就绑死了引擎版本——升个引擎，所有存档报废。
//   这里的每个 DTO 都是朴素 C# 对象，序列化的是**游戏状态**，不是**场景树**。

/// <summary>
/// 一份存档的完整内容。<b>顶层就是全部世界状态</b>——读档 = 造一个空世界 + 把这棵树摆回去。
/// <para>
/// 什么该进这棵树：<b>玩家做过的选择留下的痕迹</b>（谁死了、谁缺了根手指、哪个柜子搜了一半、商人后天来）。
/// 什么不该进：<b>能从别处算回来的东西</b>（掩体场从建筑重建、导航图从地图烘焙、护甲层从穿戴态推）——
/// 存派生量只会制造"两份真相不一致"的 bug。
/// </para>
/// </summary>
public sealed class SaveData
{
    /// <summary>存档格式版本。与 <see cref="SaveCodec.CurrentVersion"/> 不符即拒读（见那边的版本闸门）。</summary>
    public int Version { get; set; } = SaveCodec.CurrentVersion;

    /// <summary>存档列表要显示的摘要（不读全档就能列出来）。</summary>
    public SaveMeta Meta { get; set; } = new();

    /// <summary>世界时钟与相位。</summary>
    public WorldSave World { get; set; } = new();

    /// <summary>
    /// 全部剧情/发现/提示 flag。<b>这一个字典撑起了半个存档</b>——克莉丝汀支线、金手指发现、电台主线状态机、
    /// 叙事点已看过、首次提示已触发、各探索点搜刮完成度、尸潮已目击…全都是 <see cref="StoryFlags"/> 里的 key。
    /// 这不是偷懒，是那些系统本来就把状态写在这儿（它们自身零字段）。
    /// </summary>
    public Dictionary<string, string> StoryFlags { get; set; } = new();

    /// <summary>
    /// [T57] 调查点网状解锁：**去过哪些调查点**（内部路由键）。解锁 = 前置点【去过】且【探索度 &gt; 50%】——
    /// 探索度本身由 <see cref="StoryFlags"/> 里的 searched_*/found_* 推出来（<c>ExplorationProgress.Completion</c>），
    /// 唯一需要单独记的就是这份「去过」名单。
    ///
    /// <para>
    /// 🔴 <b>刻意可空，这是老档兜底的开关</b>：网状解锁是 [T57] 才有的东西，此前的存档（含 v3）压根没有这个键 ⇒
    /// 反序列化出来是 <c>null</c>，据此认出「这是 T57 之前的档」并**一律视为全部已解锁**（<c>legacyFullUnlock</c>），
    /// 不去剥夺玩家已经打下来的进度。新档一律写一份真列表（哪怕是空的 <c>[]</c>）⇒ 空列表＝新游戏（只有起点开着），
    /// 与 null＝老档，两者区分得开。
    /// <b>因此本字段不需要撞版本号</b>——它是往 v3 payload 里加的**向后兼容**字段（v3 刚被 impl-iron 撞过，再撞会把老档撞坏）。
    /// </para>
    /// </summary>
    public List<string>? VisitedDestinations { get; set; }

    /// <summary>营地：库存/结构/家具/工作台/在制品/容器藏物。</summary>
    public CampSave Camp { get; set; } = new();

    /// <summary>全体幸存者（含已死但还在名单上的）。</summary>
    public List<PawnSave> Survivors { get; set; } = new();

    /// <summary>布鲁斯（狗）。不在营地时为 null。</summary>
    public DogSave? Dog { get; set; }

    /// <summary>场上尸体（含它们身上还没被扒走的东西 + 还剩几个相位就烂没）。</summary>
    public CorpseYardSave Corpses { get; set; } = new();

    /// <summary>神秘商人：下次来访日 + 接替链。</summary>
    public MerchantSave Merchant { get; set; } = new();

    /// <summary>远征：待出发目的地 / 今日出队名单 / 背包。</summary>
    public ExpeditionSave Expedition { get; set; } = new();

    /// <summary>authored 羁绊与专属效果的累积量（等级是派生的，只存累积量）。</summary>
    public BondSave Bonds { get; set; } = new();
}

/// <summary>存档摘要：存档列表直接读它，不必反序列化整棵树。</summary>
public sealed class SaveMeta
{
    /// <summary>玩家给的存档名（自动存档为固定名）。</summary>
    public string Label { get; set; } = "";

    /// <summary>真实世界的存档时刻（ISO 8601 UTC），存档列表按它排序。</summary>
    public string SavedAtUtc { get; set; } = "";

    /// <summary>游戏内第几天（列表显示"第 12 天"）。</summary>
    public int Day { get; set; }

    /// <summary>当时的相位（列表显示"黄昏聚餐"，走 <see cref="DisplayNames"/> 出中文）。</summary>
    public DayPhase Phase { get; set; }

    /// <summary>当时活着几个人（列表显示"4 人存活"）。</summary>
    public int SurvivorsAlive { get; set; }

    /// <summary>是否自动存档（列表上要和手动存档区分开）。</summary>
    public bool IsAutosave { get; set; }
}

/// <summary>世界时钟。相位内进度也要存——否则读档会把"黄昏快过完了"变回"黄昏刚开始"，等于白送玩家半个相位。</summary>
public sealed class WorldSave
{
    public int Day { get; set; }
    public DayPhase Phase { get; set; }
    public double PhaseElapsed { get; set; }
    public double TravelElapsed { get; set; }
    public bool WarningFired { get; set; }
    public int SpeedIndex { get; set; }
}

/// <summary>营地全貌。</summary>
public sealed class CampSave
{
    /// <summary>食物份数。</summary>
    public int Food { get; set; }

    /// <summary>共享库存（白银也在里头，是一条 material item）。</summary>
    public List<ItemSave> Inventory { get; set; } = new();

    /// <summary>工作台已装的工具槽。</summary>
    public List<ToolSlot> WorkbenchTools { get; set; } = new();

    /// <summary>
    /// [批次21·T14] 烹饪台已装的炊具（锅 / 烤架）。**不存"每份要几点热量"**——那是按当前规则现算的
    /// （<c>CookingLogic.PortionCost</c>），日后调了减免幅度，老存档自动跟着改，不会腐化成一份旧数值。
    /// 烹饪台本体是家具（在 <see cref="PlacedFurniture"/> / <see cref="Furniture"/> 里），本表只存"台上装了什么"。
    /// </summary>
    public List<CookwareSlot> CookwareInstalled { get; set; } = new();

    /// <summary>在制品（没有就 null）。</summary>
    public CraftingJobSave? CraftingJob { get; set; }

    /// <summary>围栏/大门/门——<b>按格</b>逐个存（血量 + 档位 + 门的开关闩锁）。</summary>
    public List<StructureSave> Structures { get; set; } = new();

    /// <summary>还在场的家具（被拆掉的就不在列表里）。</summary>
    public List<string> Furniture { get; set; } = new();

    /// <summary>玩家摆的沙袋。</summary>
    public List<SandbagSave> Sandbags { get; set; } = new();

    /// <summary>沙袋命名序号（不存的话读档后新沙袋会和旧的重名）。</summary>
    public int SandbagSeq { get; set; }

    /// <summary>各容器**剩余**藏物（搜了一半的柜子，剩下的就在这儿）。</summary>
    public Dictionary<string, List<LootItem>> ContainerLoot { get; set; } = new();

    /// <summary>已搜空的容器。</summary>
    public List<string> ContainersSearched { get; set; } = new();

    /// <summary>动过但没搜完的容器（悬停提示"搜了一半"靠它）。</summary>
    public List<string> ContainersPartial { get; set; } = new();

    /// <summary>
    /// [批次21·impl-bedrest] 床位占用：床键（"床#1"）→ 占床者 pawnId。**一人一床、一床一人**（见 <see cref="BedRegistry"/>）。
    /// 床本体是家具（在上面的 <see cref="Furniture"/> 里，拆了就不在），本表只存"谁躺哪张"。
    /// </summary>
    public Dictionary<string, int> BedOccupancy { get; set; } = new();

    /// <summary>[批次21·impl-bedrest] 玩家造出来的床的命名序号（不存的话读档后新床会和旧的重名，同沙袋）。</summary>
    public int BedSeq { get; set; }

    /// <summary>
    /// [批次21·T26·impl-traps] 玩家摆下的陷阱的命名序号（不存的话读档后新陷阱会和旧的重名，同沙袋/床）。
    /// <para>
    /// <b>陷阱的"数量"不在这儿，也不该在</b>：捕获几率按"场上第 n 个"递减（<see cref="TrapLogic.ChanceOf"/>），
    /// 而 n 是从 <see cref="PlacedFurniture"/> **数出来的**（<c>CampMain.TrapCount</c>）——陷阱本体逐个摆回场上，
    /// 数量就自动回来了。单独再存一份数量只会制造出<b>第二个事实源</b>，早晚与场上实况分叉。
    /// </para>
    /// </summary>
    public int TrapSeq { get; set; }

    /// <summary>
    /// [T75] 玩家摆的**捕鸟陷阱**的命名序号（"捕鸟陷阱#N" 的 N 最大值）。同 <see cref="TrapSeq"/>：
    /// 数量不存（从 <see cref="PlacedFurniture"/> 数出来），只存序号防读档后新造的与场上已有的撞名。
    /// 老档没有这个字段 ⇒ 反序列化默认 0，读档时与 RespawnBirdTrap 推出的场上最大号取 Max（见 CampMain.Save.cs），不会回退撞名。
    /// </summary>
    public int BirdTrapSeq { get; set; }

    /// <summary>
    /// [批次21·impl-gunmod] 玩家改装出来的武器变体的**身份**（"步枪（刺刀型）"是什么做的）。
    /// <para>
    /// <b>为什么必须单独存</b>：库存里的一件武器只存一个名字（<see cref="ItemSave.RefKey"/>），
    /// 战斗/装备一律拿这个名字去 <c>WeaponTable</c> 回查——而改装变体**不在** WeaponTable 里。
    /// 不存这张表，读档后那把改装枪就是个查不到定义的空名字（装不上、也没数值）。
    /// </para>
    /// 只存三个字符串、**不存任何数值**：读档时按当前规则重新合成（见 <see cref="ModdedWeaponRegistry"/>）。
    /// </summary>
    public List<ModdedWeaponSave> ModdedWeapons { get; set; } = new();

    /// <summary>[批次21·impl-gunmod] 玩家自己摆到地上的家具（改装台…）：不存位置就找不回来了。</summary>
    public List<PlacedFurnitureSave> PlacedFurniture { get; set; } = new();
}

/// <summary>
/// 一件**玩家自己摆到地上的家具**（改装台…）：家具键 + 世界占位。
/// <para>
/// camp.json 预置的家具（工作台/柜子）不需要这张表——它们每次建图都在原地长出来，存档只需记"拆没拆"。
/// 而玩家摆的家具**位置是玩家定的**，不存就找不回来了。
/// </para>
/// ⚠️ 沙袋目前**没有**走这条路（<see cref="CampSave.Sandbags"/> 字段存在但 CaptureCamp 从未填过 ⇒
/// 摆好的沙袋读档后会消失）——那是本任务之前就有的缺口，已在 journal 记为遗留。
/// </summary>
public sealed class PlacedFurnitureSave
{
    /// <summary>家具键（= <see cref="FurnitureBuildCost"/> 键 / 场上容器名，如"改装台"）。</summary>
    public string? Key { get; set; }

    /// <summary>cartesian 世界占位（<b>不是</b> iso 屏幕坐标）。</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>
/// 一把改装武器的身份：变体名 + 基础武器名 + 改装名列表。数值不入档——读档按当前规则重算，
/// 故日后调了改装数值，老存档里的枪自动跟着改，不会腐化成一把旧数值的枪。
/// </summary>
public sealed class ModdedWeaponSave
{
    /// <summary>变体名（= 库存 <see cref="ItemSave.RefKey"/>，如"步枪（刺刀型）"）。</summary>
    public string? VariantName { get; set; }

    /// <summary>基础武器名（WeaponTable 里的原厂武器，如"步枪"）。</summary>
    public string? BaseWeaponName { get; set; }

    /// <summary>已施加的改装名（WeaponModCatalog 里的 <see cref="WeaponMod.Name"/>）。</summary>
    public List<string> ModNames { get; set; } = new();

    /// <summary>
    /// [T47] **消耗型改装还剩几次**（改装名 → 剩余攻击次数）。目前只有「锋刃研磨」（3 次）是消耗型。
    ///
    /// <para>
    /// <b>存档版本没有再撞</b>：这是往 <c>impl-iron</c> 已经开好的 **v3** payload 里**加一个字段**，
    /// 不是 v4。同一批次里撞两次版本号 = 让刚迁移过的老档二次作废。
    /// </para>
    /// <para>
    /// 🔴 <b>老档（v3 之前 / 没有这个字段）读出来是 <c>null</c> ⇒ 一律补成「满次数」，不是 0</b>
    /// （见 <c>ModdedWeaponRegistry.Restore</c>）。默认 0 会让老档一读进来，所有研磨过的刀当场全部脱落 ——
    /// 凭空没收玩家的东西，比读不了还糟。
    /// </para>
    /// <para>
    /// <b>为什么它在这里、而不在 spec 三兄弟旁边</b>：spec（变体名/基础武器/改装列表）是**不可变身份**，
    /// 读档时按当前规则重算；而剩余次数是**可变的实例状态**，必须原样存回来。两者语义不同，
    /// 只是恰好挂在同一条记录上（同一把武器）。
    /// </para>
    /// </summary>
    public Dictionary<string, int>? RemainingUses { get; set; }
}

/// <summary>
/// 一件物品。六个字段照抄 <see cref="Item"/>，<b>包括描述文案</b>——不走目录查表重建，
/// 免得改一句 flavor 就把旧存档里的物品悄悄改了（见 <see cref="Item.Restore"/> 的注释）。
/// </summary>
public sealed class ItemSave
{
    public ItemCategory Category { get; set; }
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string? RefKey { get; set; }
    public int FoodQuantity { get; set; }
    public int MaterialQuantity { get; set; }
}

/// <summary>在制品：配方 + 已投入工时。</summary>
public sealed class CraftingJobSave
{
    public string RecipeId { get; set; } = "";
    public int Times { get; set; }
    public int TotalWorkMinutes { get; set; }
    public int ElapsedWorkMinutes { get; set; }

    /// <summary>谁在做（Pawn.Id；无人接手为 -1）。</summary>
    public int WorkerId { get; set; } = -1;
}

/// <summary>
/// 一处结构（一段围栏 / 一扇大门 / 一扇门）。<b>按格存</b>——围栏是逐格可破坏的，
/// 只存"南墙还剩多少血"是不够的，玩家记得的是**哪一格**被啃开了。
/// </summary>
public sealed class StructureSave
{
    /// <summary>结构的唯一标识（建图时按格生成，读档据此对号入座）。</summary>
    public string Id { get; set; } = "";

    public StructureTier Tier { get; set; }

    /// <summary>当前血量（小数——砸墙伤害由武器派生，不取整）。</summary>
    public double Hp { get; set; }

    /// <summary>门态：开/关/锁/闩。<b>非门结构为 null</b>（围栏没有"开着"这回事）。</summary>
    public DoorState? DoorState { get; set; }

    /// <summary>锁的档位（非门结构忽略）。</summary>
    public LockTier LockTier { get; set; }
}

/// <summary>一个沙袋（玩家自由摆放的半身掩体）。</summary>
public sealed class SandbagSave
{
    public string Id { get; set; } = "";

    /// <summary>cartesian 世界坐标（<b>不是</b> iso 屏幕坐标——那是画出来的，不是世界的真相）。</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>一个幸存者。存档里最重的一棵子树。</summary>
public sealed class PawnSave
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public PawnRole Role { get; set; }
    public bool Stationing { get; set; }

    /// <summary>cartesian 世界坐标。</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>身体：部位血量/切除/骨折/失血/假肢。<b>山姆开局缺的那两根手指就在这里。</b></summary>
    public BodySnapshot Body { get; set; } = new();

    /// <summary>饥饿值（0=饿死线）。</summary>
    public int Hunger { get; set; }

    /// <summary>饥饿上限（吃撑过的人上限不同）。</summary>
    public int HungerCap { get; set; }

    /// <summary>伤病/感染。</summary>
    public List<ConditionSave> Conditions { get; set; } = new();

    /// <summary>是否已病死（<see cref="HealthConditionSet.IsDead"/> 的终态）。</summary>
    public bool HealthDead { get; set; }

    /// <summary>穿戴：逐件存 (物品, 占哪些槽)——成对手套/鞋靠它区分左右。</summary>
    public List<WornSave> Apparel { get; set; } = new();

    /// <summary>持械。</summary>
    public LoadoutSave Loadout { get; set; } = new();

    /// <summary>手持光源（手电/火把占一只手）；没拿就是 null。</summary>
    public HeldLightSave? HeldLight { get; set; }

    /// <summary>authored 专属效果。<b>等级是派生的</b>（书虫按阅读时长、山姆按营地人数会倒退），只存累积量。</summary>
    public PerkSave Perks { get; set; } = new();

    /// <summary>读完的书。</summary>
    public List<string> ReadBooks { get; set; } = new();

    /// <summary>每本书读到哪了（跨夜持久，读一半的书要接着读）。</summary>
    public Dictionary<string, double> ReadingProgress { get; set; } = new();

    /// <summary>当前指派在读的书。</summary>
    public string? AssignedBookId { get; set; }

    /// <summary>感染疗程指派的药（断药会清疗程，故这是真状态）。</summary>
    public string? InfectionTreatmentMedKey { get; set; }

    /// <summary>
    /// [批次21·impl-bedrest] 玩家下的**卧床养病令**（跨相位持续的意图，非当下 Role——Role 每相位都会被重排掉）。
    /// 漏了它，读档回来伤员就自己从床上爬起来了。
    /// </summary>
    public bool BedrestOrdered { get; set; }

    /// <summary>[批次21·impl-bedrest] 当日休养流水账：已记相位数 / 其中休养的 / 其中睡床的。存档跨相位，账不能丢（否则读档等于当天白养）。</summary>
    public int RestPhases { get; set; }
    public int RestRestPhases { get; set; }
    public int RestBedPhases { get; set; }
}

/// <summary>一条伤病/感染。不变量走构造器，进度是可变量。</summary>
public sealed class ConditionSave
{
    public HealthConditionType Type { get; set; }
    public string? BodyPart { get; set; }
    public bool OnLimb { get; set; }
    public bool LethalBleed { get; set; }
    public double InfectionProneness { get; set; }
    public bool SelfHealing { get; set; }

    /// <summary>感染进度（死亡赛道）。</summary>
    public double Severity { get; set; }

    /// <summary>治疗进度（清除赛道）。<b>双进度条竞速的另一半，漏了它读档就等于把病治好了一半。</b></summary>
    public double CureProgress { get; set; }

    public int RecoveryEfficiency { get; set; }
    public bool Tended { get; set; }
    public int DaysElapsed { get; set; }

    /// <summary>上次手术是第几天（重做手术冷却靠它；-1 = 没动过刀）。</summary>
    public int LastSurgeryDay { get; set; } = -1;
}

/// <summary>一件在身装备：物品名 + 占的槽 + 覆盖的部位。</summary>
public sealed class WornSave
{
    public string Item { get; set; } = "";
    public List<EquipSlot> Slots { get; set; } = new();
    public List<string> Covers { get; set; } = new();
}

/// <summary>持械：左右手 + 是否双手握。武器只存名字（<c>WeaponTable</c> 查回——武器定义是代码里的表，不是玩家状态）。</summary>
public sealed class LoadoutSave
{
    public string? LeftHand { get; set; }
    public string? RightHand { get; set; }
    public bool TwoHandGrip { get; set; }
    public bool LeftHandLost { get; set; }
    public bool RightHandLost { get; set; }
}

/// <summary>手持光源：光源键 + 占了哪只手。</summary>
public sealed class HeldLightSave
{
    public string LightKey { get; set; } = "";
    public Hand Hand { get; set; }
}

/// <summary>
/// authored 专属效果。<b>只存累积量，不存等级</b>——等级全是算出来的：
/// 书虫按累计阅读小时、山姆按营地人数（会<b>倒退</b>）、南丁格尔按主刀台数（存在 StoryFlags 里）。
/// 存等级只会和累积量打架。
/// </summary>
public sealed class PerkSave
{
    public bool HasBookworm { get; set; }

    /// <summary>累计阅读小时（书虫等级由它推）。</summary>
    public double BookwormReadingHours { get; set; }

    public bool IsNightingale { get; set; }
    public bool IsSam { get; set; }
}

/// <summary>布鲁斯。</summary>
public sealed class DogSave
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public BodySnapshot Body { get; set; } = new();
    public int Hunger { get; set; }

    /// <summary>狗装备（身体槽 + 头槽）。</summary>
    public Dictionary<DogEquipSlot, string> Apparel { get; set; } = new();

    public bool GuardStationing { get; set; }
}

/// <summary>尸场：相位计数 + 全部尸体。</summary>
public sealed class CorpseYardSave
{
    /// <summary>单调相位计数。<b>必须先摆回它，再摆尸体</b>——尸体的"还剩几个相位烂没"是差值算的。</summary>
    public int PhaseTick { get; set; }

    /// <summary>尸体容器 id 水位（新尸体的编号从这儿接着往下走，免得和恢复出来的撞号）。</summary>
    public int NextId { get; set; }

    public List<CorpseSave> Corpses { get; set; } = new();
}

/// <summary>一具尸体：躺在哪、身上还剩什么、什么时候烂。</summary>
public sealed class CorpseSave
{
    /// <summary>可搜刮容器 id（身上没东西的尸体没有 id，也就不是可搜刮点）。</summary>
    public string ContainerId { get; set; } = "";

    /// <summary>cartesian 落点。</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>占的尸体格（同格不堆叠，落尸推挤靠它）。</summary>
    public int CellX { get; set; }
    public int CellY { get; set; }

    /// <summary>落地时的相位计数。与 <see cref="CorpseYardSave.PhaseTick"/> 的差值 = 已经躺了几个相位。</summary>
    public int SpawnPhaseTick { get; set; }

    /// <summary>身上还没被扒走的东西（穿什么扒什么）。</summary>
    public List<LootItem> Loot { get; set; } = new();

    /// <summary>尸体颜色（丧尸/劫掠者/同伴的尸体画出来不一样）。</summary>
    public float TintR { get; set; }
    public float TintG { get; set; }
    public float TintB { get; set; }
    public float Radius { get; set; }
}

/// <summary>神秘商人。</summary>
public sealed class MerchantSave
{
    /// <summary>下次来访日。<b>不能重滚</b>——存档时商人后天来，读回来必须还是后天来，否则 S/L 成了刷商人日程的作弊器。</summary>
    public int NextVisitDay { get; set; }

    /// <summary>此刻商人是否就在营地里。</summary>
    public bool Present { get; set; }

    /// <summary>
    /// 在场商人的货架（他这趟带了什么、卖多少、还剩几件）。
    /// <b>库存要存</b>——不然 S/L 就能把买空的货架刷回满货。
    /// </summary>
    public List<OfferSave> Shelf { get; set; } = new();
}

/// <summary>货架上的一条：卖什么、多少钱（分）、还剩几件。</summary>
public sealed class OfferSave
{
    public ItemSave Good { get; set; } = new();

    /// <summary>售价（<b>分</b>——白银 2dp 分制，存整数分，绝不存浮点银）。</summary>
    public int Price { get; set; }

    /// <summary>剩余库存（0 = 已售罄）。</summary>
    public int Stock { get; set; }
}

/// <summary>远征态。</summary>
public sealed class ExpeditionSave
{
    /// <summary>已选定、还没出发的目的地。</summary>
    public string? PendingDestination { get; set; }

    /// <summary>路上要走多久。</summary>
    public double PendingTravelTime { get; set; }

    /// <summary>今日出队的人（Pawn.Id）。</summary>
    public List<int> TodaysExpeditionIds { get; set; } = new();

    /// <summary>远征背包里的东西（负重上限由狗衣/人推，不存）。</summary>
    public List<LootItem> Bag { get; set; } = new();

    /// <summary>本趟是否带了布鲁斯。</summary>
    public bool BruceAlong { get; set; }
}

/// <summary>authored 羁绊的累积量。</summary>
public sealed class BondSave
{
    /// <summary>道格与布鲁斯"两个都活着"的天数（羁绊等级由它推）。</summary>
    public int BondDaysBothAlive { get; set; }
}
