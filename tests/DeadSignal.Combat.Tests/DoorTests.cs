using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 门系统。用户拍板三条口径：
/// <list type="number">
/// <item><b>丧尸不会开门，只会砸</b> —— 门对丧尸就是一堵墙（复用破防系统 <see cref="BreachLogic"/>）。<b>劫掠者会正常开门。</b></item>
/// <item><b>门有锁，且能撬 / 能砸</b> —— 撬锁（安静、要工具、耗时）vs 砸开（快、很响）= 又一个<b>噪音 vs 效率</b>的取舍。</item>
/// <item><b>玩家开门只有一种动作</b>（不分"轻推"/"踹开"）—— 一个固定噪音值，不做两档。</item>
/// </list>
/// <para>
/// <b>门的三态与"阻挡"的三合一</b>：开 / 关 / 锁。<b>关和锁都阻挡</b>，只有开不阻挡。而"阻挡"在本仓是**一件事**——
/// 关着的门在 Godot 层就是一个墙层（0b0100）StaticBody2D + 一条导航洞，于是 <b>挡人 / 挡视线（VisionOcclusion 对墙层
/// raycast）/ 阻断寻路（BakeNavPoly 挖洞）三件事同时成立</b>，不需要三套判定。本类只钉<b>规则</b>，空间执行在 CampMain。
/// </para>
/// </summary>
public class DoorTests
{
    // ---------------- 谁能开门（用户拍板 1：丧尸永远不能） ----------------

    [Fact]
    public void 丧尸永远开不了门_门对它就是一堵墙()
    {
        // 用户拍板：「丧尸不会开门，只会砸」。这是**硬规则**，不是难度旋钮——
        // 无论门是关着还是锁着，丧尸都不能操作它，只能走破防（砸）。
        Assert.False(DoorLogic.CanOperateDoors(Faction.Zombie, isAnimal: false));
        Assert.False(DoorLogic.CanOpen(DoorState.Closed, Faction.Zombie, isAnimal: false));
        Assert.False(DoorLogic.CanOpen(DoorState.Locked, Faction.Zombie, isAnimal: false));
        Assert.False(DoorLogic.CanClose(DoorState.Open, Faction.Zombie, isAnimal: false));
        Assert.False(DoorLogic.CanPick(DoorState.Locked, Faction.Zombie, isAnimal: false, lockpickCount: 99));
    }

    [Fact]
    public void 劫掠者会正常开门()
    {
        // 用户拍板原话：「劫掠者会正常开门」。
        Assert.True(DoorLogic.CanOperateDoors(Faction.Raider, isAnimal: false));
        Assert.True(DoorLogic.CanOpen(DoorState.Closed, Faction.Raider, isAnimal: false));
    }

    [Fact]
    public void 幸存者能开未锁的门()
    {
        Assert.True(DoorLogic.CanOpen(DoorState.Closed, Faction.Survivor, isAnimal: false));
    }

    [Fact]
    public void 狗开不了门_没有手()
    {
        // 布鲁斯是 Faction.Survivor（己方），光看阵营会误判成"能开门"——故 isAnimal 单独一维。
        // 狗没有手，开不了门；它撞上关着的门只能等人来开。
        Assert.False(DoorLogic.CanOperateDoors(Faction.Survivor, isAnimal: true));
        Assert.False(DoorLogic.CanOpen(DoorState.Closed, Faction.Survivor, isAnimal: true));
    }

    // ---------------- 门的状态机（开 / 关 / 锁） ----------------

    [Fact]
    public void 锁着的门不能直接开_要先撬或砸()
    {
        // 锁 ⊃ 关：锁着的门也是关着的（一样阻挡），但比关着多一道门槛——不能直接推开。
        Assert.False(DoorLogic.CanOpen(DoorState.Locked, Faction.Survivor, isAnimal: false));
        Assert.True(DoorLogic.CanPick(DoorState.Locked, Faction.Survivor, isAnimal: false, lockpickCount: 1));
        Assert.True(DoorLogic.CanBash(DoorState.Locked));
    }

    [Fact]
    public void 开着的门不能再开_关着的门不能再关()
    {
        Assert.False(DoorLogic.CanOpen(DoorState.Open, Faction.Survivor, isAnimal: false));
        Assert.False(DoorLogic.CanClose(DoorState.Closed, Faction.Survivor, isAnimal: false));
        Assert.False(DoorLogic.CanClose(DoorState.Locked, Faction.Survivor, isAnimal: false));
    }

    [Fact]
    public void 没锁的门撬不了_开着的门也撬不了()
    {
        Assert.False(DoorLogic.CanPick(DoorState.Closed, Faction.Survivor, isAnimal: false, lockpickCount: 9));
        Assert.False(DoorLogic.CanPick(DoorState.Open, Faction.Survivor, isAnimal: false, lockpickCount: 9));
    }

    [Fact]
    public void 门开着就不阻挡_关着和锁着都阻挡()
    {
        // **这一条是整个门系统的地基**：Blocks 同时决定「挡不挡人」「挡不挡视线」「断不断寻路」——
        // 因为 Godot 层用同一个墙层 StaticBody2D + 同一条导航洞承载这三件事。
        Assert.False(DoorLogic.Blocks(DoorState.Open));
        Assert.True(DoorLogic.Blocks(DoorState.Closed));
        Assert.True(DoorLogic.Blocks(DoorState.Locked));
    }

    [Fact]
    public void 开着的门不可砸_因为它已经不挡路了()
    {
        // 关键护栏：若开着的门仍可砸，袭营 AI 的择目标（NearestStructureByEdge）会跑去砸一扇敞开的门——
        // 荒谬且卡死。故 CanBash 恒等于 Blocks。
        Assert.False(DoorLogic.CanBash(DoorState.Open));
        Assert.True(DoorLogic.CanBash(DoorState.Closed));
        Assert.True(DoorLogic.CanBash(DoorState.Locked));
        // 恒等关系（钉死，防日后两处漂移）
        foreach (DoorState s in new[] { DoorState.Open, DoorState.Closed, DoorState.Locked })
        {
            Assert.Equal(DoorLogic.Blocks(s), DoorLogic.CanBash(s));
        }
    }

    // ---------------- 撬锁：工具 + 耗时 + 成功率（用户拍板 2） ----------------

    [Fact]
    public void 撬锁要工具_没铁丝撬不动()
    {
        // 撬锁工具 = **铁丝**（Materials 已有的 "wire"，不新造物品）。
        // 项目口径：通用技能系统已删（Recipe.cs:8），能力只由「工具/书/材料」承载 ⇒ 撬锁挂**工具**，不挂技能。
        Assert.Equal("wire", DoorLogic.LockpickMaterialKey);
        Assert.False(DoorLogic.CanPick(DoorState.Locked, Faction.Survivor, isAnimal: false, lockpickCount: 0));
        Assert.True(DoorLogic.CanPick(DoorState.Locked, Faction.Survivor, isAnimal: false, lockpickCount: 1));
    }

    [Fact]
    public void 锁越硬_越慢越难撬()
    {
        // 单调性（数值皆「拟定待调」，但**梯度方向**是规则，钉死）。
        Assert.True(DoorLogic.PickSeconds(LockTier.Simple) < DoorLogic.PickSeconds(LockTier.Standard));
        Assert.True(DoorLogic.PickSeconds(LockTier.Standard) < DoorLogic.PickSeconds(LockTier.Sturdy));

        Assert.True(DoorLogic.PickChance(LockTier.Simple) > DoorLogic.PickChance(LockTier.Standard));
        Assert.True(DoorLogic.PickChance(LockTier.Standard) > DoorLogic.PickChance(LockTier.Sturdy));

        // 概率恒在 (0,1]：再硬的锁也撬得开（否则玩家会被永久卡死），再简单的锁也不是白送。
        foreach (LockTier t in new[] { LockTier.Simple, LockTier.Standard, LockTier.Sturdy })
        {
            Assert.InRange(DoorLogic.PickChance(t), 0.01, 1.0);
            Assert.True(DoorLogic.PickSeconds(t) > 0);
        }
    }

    [Fact]
    public void 无锁档撬锁无意义_零耗时且必成()
    {
        // LockTier.None = 这扇门压根没装锁（camp.json 里绝大多数门）。它不会进撬锁流程，
        // 但把边界钉住，防调用方误传 None 时出现"耗时 0 却永远失败"的死循环。
        Assert.Equal(1.0, DoorLogic.PickChance(LockTier.None));
        Assert.Equal(0.0, DoorLogic.PickSeconds(LockTier.None));
    }

    [Fact]
    public void 撬锁成功_不消耗铁丝()
    {
        // 撬开了 = 铁丝没断，还能接着用。
        var rng = new SequenceRandomSource(0.10); // < Simple 的 0.70 → 成功
        DoorPickAttempt a = DoorLogic.TryPick(LockTier.Simple, rng);
        Assert.True(a.Success);
        Assert.False(a.ToolBroken);
        Assert.Equal(DoorLogic.PickSeconds(LockTier.Simple), a.Seconds);
    }

    [Fact]
    public void 撬锁失败_铁丝断在锁里_时间照样花掉()
    {
        // 失败的代价是**双重**的：一根铁丝 + 一段时间。这正是"撬锁不是白嫖"的地方——
        // 你蹲在门口撬第四次的时候，屋外那群东西并没有停下来等你。
        var rng = new SequenceRandomSource(0.90); // > Simple 的 0.70 → 失败
        DoorPickAttempt a = DoorLogic.TryPick(LockTier.Simple, rng);
        Assert.False(a.Success);
        Assert.True(a.ToolBroken);
        Assert.Equal(DoorLogic.PickSeconds(LockTier.Simple), a.Seconds);
    }

    [Fact]
    public void 撬锁走可注入随机源_可复现()
    {
        // 同一串随机数 → 同一串结果（项目铁律：随机必须走 IRandomSource）。
        var rngA = new SequenceRandomSource(0.10, 0.90, 0.10);
        var rngB = new SequenceRandomSource(0.10, 0.90, 0.10);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(
                DoorLogic.TryPick(LockTier.Standard, rngA).Success,
                DoorLogic.TryPick(LockTier.Standard, rngB).Success);
        }
    }

    // ---------------- 噪音三值：开门 / 撬锁 / 砸门（用户拍板 2、3） ----------------

    [Fact]
    public void 撬锁必须比丧尸嗅觉安静_否则撬锁毫无意义()
    {
        // **整条撬锁机制的存在理由**：撬锁若比丧尸的嗅觉兜底半径（70）还响，那你撬到一半就把门外那群东西
        // 招到脸上了 —— "安静地进去"这件事根本不成立，撬锁沦为"慢速版砸门"。这一条是**硬护栏**。
        Assert.True(NoiseLogic.LockpickNoiseRadius < NoiseLogic.ZombieSmellRadius);
        // 甚至比走路（40）还轻：金属细碎的刮擦声 —— 你走到门口这一路都比撬锁本身吵。
        Assert.True(NoiseLogic.LockpickNoiseRadius < NoiseLogic.WalkNoiseRadius);
    }

    [Fact]
    public void 噪音三值梯度_撬锁远静于开门_开门远静于砸门()
    {
        // 用户拍板的取舍：**撬锁（安静、慢）vs 砸开（快、很响）**。梯度必须成立，否则取舍不存在。
        Assert.True(NoiseLogic.LockpickNoiseRadius < NoiseLogic.DoorNoiseRadius);
        Assert.True(NoiseLogic.DoorNoiseRadius < NoiseLogic.BreachNoiseRadius);
    }

    [Fact]
    public void 玩家开门只有一种动作_一个固定噪音值()
    {
        // 用户拍板 3：**不分"轻推"和"踹开"**。故只有 DoorNoiseRadius 这一个开门噪音常量，
        // 且 NoiseOfOpening 不接受任何"力度/模式"参数 —— 用签名把"两档"这件事从源头挡住。
        Assert.Equal(NoiseLogic.DoorNoiseRadius, DoorLogic.NoiseOfOpening());
    }

    // ---------------- 丧尸砸门 = 复用破防系统（用户拍板 1） ----------------

    [Fact]
    public void 丧尸够得着关着的门就砸_够不着就先贴近_完全复用BreachLogic()
    {
        // 关着的门在 Godot 层就是一个 CampStructureInstance（同围栏/大门），于是丧尸的"砸门"
        // **一行新 AI 代码都不需要**：BreachController 的 CanReach 探测失败 → 走 BreachLogic 择最近结构 → 砸。
        // 门只是它眼里的又一堵墙。这里钉的是"门被纳入了同一套破防几何"。
        Assert.Equal(BreachAction.Hammer, BreachLogic.Decide(edgeDistance: 10, attackReach: 40));
        Assert.Equal(BreachAction.MoveToApproach, BreachLogic.Decide(edgeDistance: 200, attackReach: 40));
    }

    [Fact]
    public void 门有耐久_能被砸烂_走同一张结构血量表()
    {
        // 门纳入 CampStructure 体系（新增 CampStructureKind.Door + 三档门体），
        // ⇒ 承伤/摧毁/开缺口/重烘焙导航**整条链路原样复用**，不另造一套门的 HP。
        Assert.Equal(CampStructureKind.Door, CampStructureTable.KindOf(StructureTier.DoorWood));
        Assert.Equal(CampStructureKind.Door, CampStructureTable.KindOf(StructureTier.DoorReinforced));
        Assert.Equal(CampStructureKind.Door, CampStructureTable.KindOf(StructureTier.DoorMetal));

        // 门比围栏脆（门是整个屏障上最薄的一环——这正是它值得被砸的原因）。
        Assert.True(CampStructureTable.MaxHp(StructureTier.DoorWood) < CampStructureTable.MaxHp(StructureTier.FenceBasic));
        // 门体三档单调递增
        Assert.True(CampStructureTable.MaxHp(StructureTier.DoorWood) < CampStructureTable.MaxHp(StructureTier.DoorReinforced));
        Assert.True(CampStructureTable.MaxHp(StructureTier.DoorReinforced) < CampStructureTable.MaxHp(StructureTier.DoorMetal));

        var door = new CampStructureState(StructureTier.DoorWood);
        Assert.False(door.TakeDamage(door.MaxHp - 1));
        Assert.True(door.TakeDamage(1));          // 这一击砸穿
        Assert.True(door.IsDestroyed);
        Assert.False(door.TakeDamage(50));        // 砸烂后再砸不重复开口
    }

    // ---------------- 门闩：营地大门必须闩得上（用户拍板「要，能闩上」） ----------------
    //
    // 【这一节钉的是一个真 bug】此前营地大门是「关着 + 没锁」，而**劫掠者会开门** ⇒ 判定链
    // 「关着 + 没锁 + 够得着 → 推开」三条全中 ⇒ **劫掠者直接推门进营，250HP 形同虚设**。
    // 「自家大门靠『关着』说话」这句话，**对会拧门把手的敌人根本不成立**。
    //
    // 【闩 ≠ 锁】这是本节的全部设计：门闩是**自家的横木，从里面插上的**。
    //   · 自己人：一抬就开（**一个动作**，不用铁丝、不用撬——呼应用户「开门只有一种动作」）
    //   · 劫掠者：推不开，**也撬不了**（撬锁撬的是锁芯，横木在门的内侧），**只能砸**
    //   · 丧尸：永远只会砸
    // 于是 250HP 对劫掠者重新生效，而砸门声 180（Combat，不分阵营）会把附近的丧尸一并招来——
    // **攻方自己制造的动静反噬攻方**。这是设计意图。

    [Fact]
    public void 闩着的门_劫掠者推不开_只能砸()
    {
        // ⬅ 这就是那个洞的护栏。闩上之后，劫掠者的"会开门"优势被取消，250HP 重新说话。
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Raider, isAnimal: false));
        // 也撬不了：横木不是锁芯，撬锁的技术在这儿没用武之地。
        Assert.False(DoorLogic.CanPick(DoorState.Barred, Faction.Raider, isAnimal: false, lockpickCount: 99));
        // 只剩一条路：砸。
        Assert.True(DoorLogic.CanBash(DoorState.Barred));
    }

    [Fact]
    public void 关着但没闩的门_劫掠者推得开_这正是大门必须闩上的理由()
    {
        // 把 bug 的成因本身钉死，防日后有人把大门改回"关着不闩"而不自知：
        // 对**会开门的敌人**来说，「关着」和「没关」是一回事。
        Assert.True(DoorLogic.CanOpen(DoorState.Closed, Faction.Raider, isAnimal: false));
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Raider, isAnimal: false));
    }

    [Fact]
    public void 闩着的门_自己人一抬就开_不需要铁丝也不需要撬()
    {
        // 自家的门闩，走的就是**普通开门那一个动作**（用户：开门只有一种动作，不分轻推和踹开）。
        // 若要求玩家拿铁丝撬自家大门，那是荒谬的。
        Assert.True(DoorLogic.CanOpen(DoorState.Barred, Faction.Survivor, isAnimal: false));
        // 闩不是锁：连"撬"这条路都不存在（即便你有一箱铁丝）。
        Assert.False(DoorLogic.CanPick(DoorState.Barred, Faction.Survivor, isAnimal: false, lockpickCount: 99));
    }

    [Fact]
    public void 闩着的门_丧尸照样开不了_它永远只会砸()
    {
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Zombie, isAnimal: false));
        Assert.False(DoorLogic.CanPick(DoorState.Barred, Faction.Zombie, isAnimal: false, lockpickCount: 99));
        Assert.True(DoorLogic.CanBash(DoorState.Barred));
    }

    [Fact]
    public void 闩着的门_狗也开不了_没有手()
    {
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Survivor, isAnimal: true));
    }

    [Fact]
    public void 闩着的门_挡人挡视线断寻路_和锁着关着一样()
    {
        Assert.True(DoorLogic.Blocks(DoorState.Barred));
        // CanBash 恒等于 Blocks 这条铁律，闩着也不例外。
        Assert.Equal(DoorLogic.Blocks(DoorState.Barred), DoorLogic.CanBash(DoorState.Barred));
    }

    [Fact]
    public void 关上一扇能闩的门_直接就闩上了_不分两步()
    {
        // 用户偏好简化（「开门只有一种动作」）⇒ **不做单独的"闩门"交互**：
        // 营地大门关上的那一刻门闩就落下（barrable=true）；民居的门没有闩，关上就只是关着。
        Assert.Equal(DoorState.Barred, DoorLogic.ClosedRestingState(barrable: true));
        Assert.Equal(DoorState.Closed, DoorLogic.ClosedRestingState(barrable: false));
    }

    [Fact]
    public void 闩着的门可以再打开_但不能再关_它已经是关着的()
    {
        Assert.True(DoorLogic.CanClose(DoorState.Open, Faction.Survivor, isAnimal: false));
        Assert.False(DoorLogic.CanClose(DoorState.Barred, Faction.Survivor, isAnimal: false));
    }

    [Fact]
    public void 劫掠者砸营地大门_要砸很久_而砸门声会把丧尸招来()
    {
        // 闩上之后，250HP 重新生效：劫掠者每击 12 伤 → 约 21 击才破基础大门。
        // 那是一段**很长的、很响的**时间（噪音 180，Combat 不分阵营）——足够守卫反应，
        // 也足够把附近闲逛的丧尸招过来咬他们。攻方的动静反噬攻方，这是设计意图，不是 bug。
        const int raiderHit = 12;
        int hits = (int)System.Math.Ceiling(CampStructureTable.MaxHp(StructureTier.GateBasic) / (double)raiderHit);
        Assert.True(hits >= 20, $"闩上的基础大门该让劫掠者砸上一阵子，实际只要 {hits} 击");
        Assert.True(NoiseLogic.BreachNoiseRadius > NoiseLogic.ZombieSmellRadius); // 砸门声远超丧尸嗅觉 → 招得来
    }

    [Fact]
    public void 砸门比撬锁快得多_但把全场都招来_取舍成立()
    {
        // 把用户要的取舍**量化钉死**（数值拟定待调，但取舍的**方向**是规则）：
        //   撬坚固锁：期望耗时 = 单次耗时 / 成功率，安静（30，谁也惊动不了）
        //   砸木门：  HP / 每击伤害 × 冷却，很响（180，压过最响的近战）
        double pickSturdy = DoorLogic.PickSeconds(LockTier.Sturdy) / DoorLogic.PickChance(LockTier.Sturdy);

        const int hitDamage = 12;    // 一把中等近战的量级
        const double cooldown = 1.0; // 一次挥击的量级
        double bashWood = CampStructureTable.MaxHp(StructureTier.DoorWood) / (double)hitDamage * cooldown;

        Assert.True(bashWood < pickSturdy);  // 砸更**快**
        Assert.True(NoiseLogic.BreachNoiseRadius > NoiseLogic.LockpickNoiseRadius * 5); // 但**响得多**
    }
}
