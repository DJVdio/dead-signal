using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-T60] 难民营地 —— <b>光照与视野系统第一次真的疼</b>。
///
/// <para>用户原话（authored 唯一事实源）：「难民营地，规模中，临时建起的一片平房，内是大量的小房间，
/// <b>过道狭窄，光线昏暗，视野受限</b>，并且<b>物资分散在每一个房间中</b>，一同在房间中的还有<b>开门跳脸的丧尸</b>。」</para>
///
/// <para>
/// 🔴 用户这句话里的每一个词，在这里都是一个数字：
///   · <b>光线昏暗</b> ⇒ <see cref="ExplorationLighting.IsIndoorsDark"/> ⇒ 环境光 0.10
///     ⇒ 视距 300 → <b>约 124</b>、半角 60° → <b>33°</b>（<see cref="VisionLogic.ConeFor"/>）。
///     ⚠️ 这条常量（<c>VisionLogic.IndoorsDarkAmbient</c>）写好很久了，却<b>从来没有任何一关用过</b>。这是第一次。
///   · <b>过道狭窄</b> ⇒ 门 48px &lt; 2×丧尸直径(26) ⇒ <b>门口一次只过得来一只</b>
///     ⇒ 卡门口＝把围攻打成 1v1（胜率 82.6% vs 2 只围攻的 16.6%）。<b>「窄」是玩家的朋友。</b>
///   · <b>开门跳脸</b> ⇒ 门＝墙层 ⇒ 挡视线 ⇒ 门关着时可见数 <b>0</b>；<b>门一开，锁在里面那只当场醒过来扑上来</b>
///     （<see cref="ZombieActivation"/> 的门后特殊丧尸，一门唤醒一只）。
/// </para>
/// <para>
/// ⚠️ <b>[SPEC-T60·Phase2] 已放大到中档 3200×2200。</b>跳脸曾经靠"丧尸贴在门后 ≤90px（&lt; 暗视距 124）"这条
/// <b>固定像素</b>撑着——那正是用户不满的"门卡住丧尸"，也正是它把这一关钉死在 2400×1600。Phase1 把触发改成
/// <b>绑门实体</b>后与尺度无关 ⇒ 约束解绑 ⇒ 房间放得大了。<b>门宽 48／过道宽 72 刻意不缩放</b>：卡门口打 1v1
/// 的战术漏斗仍然有效且要保住。
/// </para>
/// </summary>
public class RefugeeCampTests
{
    /// <summary>
    /// 关卡画布＝<see cref="ExplorationLevelSize"/> 的登记值（**唯一事实源**，不再在测试里抄一份字面量）。
    /// Phase2 放大后＝中档 3200×2200；改档位只动 <c>ExplorationLevelSize.Overrides</c> 那一行，这里自动跟。
    /// </summary>
    private static readonly (float W, float H) Canvas = ExplorationLevelSize.SizeFor(RefugeeCamp.DestinationName);
    private static readonly WallRect Level = new(0f, 0f, Canvas.W, Canvas.H);

    /// <summary>返回区中心（与运行时 <c>TestExploration.SetupReturnZone</c> 的默认落点同源：画布正下方居中）。</summary>
    private static readonly (float X, float Y) ReturnZone = (Canvas.W / 2f, Canvas.H - 80f);

    /// <summary>丧尸碰撞直径（<c>Zombie.cs</c>：Radius = 13f）。过道能并排几只仍看它。</summary>
    private const float ZombieDiameter = 26f;

    private static List<WallRect> WallsWithAllDoorsClosed()
        => LevelReachability.WithDoorsClosed(RefugeeCamp.Walls(), RefugeeCamp.Doors());

    private static Vector2 V((float X, float Y) p) => new(p.X, p.Y);

    /// <summary>这一关的视野锥：<b>恒暗</b>（环境光被 IsIndoorsDark 锁在 0.10，压过昼夜相位）。</summary>
    private static VisionLogic.VisionCone DarkCone()
        => VisionLogic.ConeFor(VisionLogic.AmbientLight(
            DayPhase.DayExplore, ExplorationLighting.IsIndoorsDark(RefugeeCamp.DestinationName)));

    // ══════════════════════ 🔴 「光线昏暗，视野受限」——不是调色，是数字 ══════════════════════

    /// <summary>
    /// 🔴 <b>这是全图第一个室内恒暗关。</b>
    /// <c>VisionLogic.IndoorsDarkAmbient</c>（0.10）这条常量一直躺在那儿没人用——
    /// <c>TestExploration</c> 的三处环境光调用<b>一律硬编码 <c>indoorsDark: false</c></b>。
    /// 用户要「光线昏暗」，这条线才第一次真的接上。
    /// </summary>
    [Fact]
    public void TheRefugeeCampIsTheFirstLevelThatIsActuallyDark()
    {
        Assert.True(ExplorationLighting.IsIndoorsDark(RefugeeCamp.DestinationName));
        Assert.False(ExplorationLighting.IsIndoorsDark(RuinedChurch.DestinationName));
        Assert.False(ExplorationLighting.IsIndoorsDark(ExplorationCache.HospitalName));

        // 恒暗压过相位：白天进去，也还是 0.10。
        Assert.Equal(VisionLogic.IndoorsDarkAmbient,
            VisionLogic.AmbientLight(DayPhase.DayExplore, indoorsDark: true));
    }

    /// <summary>
    /// 🔴 <b>「视野受限」是算出来的：你的视野只剩一个约 124px 的窄锥，而一条过道有 1600px 长。</b>
    /// ⇒ <b>你看不见过道的另一头，更看不见门后有什么。</b>
    /// <para>Phase2 放大后竖脊 1248 → <b>1664</b>，这条只更狠、不更松（脊长取自几何，不再抄字面量）。</para>
    /// </summary>
    [Fact]
    public void InTheDark_YourSightIsAHundredAndTwentyPixels_AndACorridorIsTenTimesThat()
    {
        VisionLogic.VisionCone dark = DarkCone();
        VisionLogic.VisionCone day = VisionLogic.ConeFor(VisionLogic.DaylightAmbient);

        Assert.InRange(dark.Range, 115f, 135f);          // ≈124.5
        Assert.InRange(dark.HalfAngleDeg, 30f, 36f);     // ≈33°
        Assert.True(dark.Range < day.Range * 0.5f, "恒暗关的视距应当不到白天的一半，否则「昏暗」没有代价。");

        // 竖脊过道长逾 1600px ⇒ 视距连它的 1/13 都不到。
        Assert.Equal(1664f, RefugeeCamp.SpineLength);
        Assert.True(dark.Range * 8f < RefugeeCamp.SpineLength,
            "视距相对过道长度不够小 —— 玩家能一眼望穿整条过道，「视野受限」就没了。");
    }

    /// <summary>
    /// 🔴 <b>手电是个真选择，而它有代价。</b>（这三条规则引擎里早就有了；这一关只是第一次让它们真的疼）
    ///   ① 举着它，你<b>只能用单手武器</b>（<see cref="HeldLightState.BlocksWeaponEquip"/>：双手武器与光源互斥）；
    ///   ② 举着它，<b>黑暗里的东西也看清了你</b>（<see cref="VisionLogic.ExposureRangeMultiplier"/> ≈ 1.57）；
    ///   ③ 但你确实看得远得多（视距 124 → 约 280）。
    /// <b>看得见，还是打得动 —— 二选一。</b>
    /// </summary>
    [Fact]
    public void TheFlashlightTrade_YouCanSeeOrYouCanFight_NotBoth()
    {
        LightProfile torch = LightSource.Find(LightSource.FlashlightKey)!.Value;

        // ③ 看得远得多：自照亮＝满强度 ⇒ 局部光照 0.90
        float lit = VisionLogic.CombineLight(VisionLogic.IndoorsDarkAmbient, torch.Intensity);
        VisionLogic.VisionCone withTorch = VisionLogic.ConeFor(lit);
        Assert.True(withTorch.Range > DarkCone().Range * 2f,
            "举着手电还看不了多远，那没人会带它 —— 这个取舍就不存在了。");

        // ② 代价：黑暗中持光 ⇒ 被发现的距离被放大
        float exposure = VisionLogic.ExposureRangeMultiplier(VisionLogic.IndoorsDarkAmbient, torch.Intensity);
        Assert.True(exposure > 1.4f, $"黑暗中举着手电却几乎不增加暴露（×{exposure:F2}）—— 那它就是白送的。");

        // ① 代价：占一只手 ⇒ 双手武器装不上（互斥是**双向**的）
        var loadout = new WeaponLoadout();
        var light = new HeldLightState();
        Assert.True(light.TryHold(torch, Hand.Left, loadout));
        Weapon twoHanded = WeaponTable.Arsenal().First(w => w.TwoHanded);
        Assert.True(HeldLightState.BlocksWeaponEquip(light, twoHanded, Hand.Right),
            "举着手电还能装上双手武器 —— 「看得见还是打得动」的取舍就没了。");
        Assert.True(HeldLightState.BlocksTwoHandedEquip(light));
    }

    // ══════════════════════ 🔴 「过道狭窄」——这是玩家的朋友，不是难度旋钮 ══════════════════════

    /// <summary>
    /// 🔴 <b>门宽不再是"卡住丧尸"的物理机制（用户口径：不该门卡住丧尸，而是开门激活门后丧尸）。</b>
    /// 门只需**过得去人**（&gt; 2×导航体半径，否则寻路判此路不通、门成墙）；威胁来自"推开门＝唤醒门后那只"，
    /// 不再靠 48&lt;52 的像素卡位。
    /// <para>
    /// 这条<b>去掉了旧的"门宽 &lt; 2×丧尸直径"物理 bottleneck 依赖</b>——它正是用户不满的"门卡住丧尸"，
    /// 也正是它把这关钉死在固定像素上、放不大（Phase2 放大靠 <see cref="ZombieActivation"/> 的开门触发解绑此约束）。
    /// </para>
    /// </summary>
    [Fact]
    public void TheDoorwayOnlyNeedsToBeWalkable_NotAPhysicalChokepoint()
    {
        Assert.True(RefugeeCamp.DoorwayWidth > 2f * ExplorationWalls.NavAgentRadius,
            "门太窄，导航体过不去 —— 门成了墙。");
    }

    /// <summary>
    /// 🔴 <b>过道里最多两只并排（旷野是 16 只）—— 但两只仍是 16.6% 的死局。</b>
    /// ⇒ <b>过道只是比旷野强，不是安全。真正的正解是退到门口去打。</b>
    /// 这两条数字合起来，才是这一关的战术空间。
    /// </summary>
    [Fact]
    public void TheCorridorIsBetterThanTheOpen_ButItIsNotSafe_TheDoorwayIs()
    {
        Assert.True(RefugeeCamp.CorridorWidth >= 2f * ZombieDiameter,
            "过道连两只都并排不下，那它就是门，不是过道。");
        Assert.True(RefugeeCamp.CorridorWidth < 3f * ZombieDiameter,
            $"过道宽 {RefugeeCamp.CorridorWidth} 能并排三只（≥{3f * ZombieDiameter}）—— 3 只围攻胜率 0.8%，那不叫窄过道。");

        // 门口（1 只）严格优于过道（2 只）：这正是玩家该学会的那一步。
        Assert.True(RefugeeCamp.DoorwayWidth < RefugeeCamp.CorridorWidth);
    }

    // ══════════════════════ 🔴 「开门跳脸的丧尸」——机制，不是脚本 ══════════════════════

    /// <summary>
    /// 🔴 <b>门关着，你一只都看不见。</b>
    /// 门＝墙层（<c>VisionOcclusion.WallMask</c>）⇒ 站在门外，到房里那只丧尸的视线<b>被门板挡死</b>。
    /// 而且就算门开着，黑暗中你的视距也只有 124px ——<b>你没有任何办法提前知道门后有没有东西。</b>
    /// </summary>
    [Fact]
    public void BehindEveryClosedDoor_YouSeeNothing()
    {
        List<WallRect> closed = WallsWithAllDoorsClosed();
        VisionLogic.VisionCone cone = DarkCone();

        foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
        {
            RefugeeRoom room = RefugeeCamp.Room(a.RoomNumber);
            (float X, float Y) eye = RefugeeCamp.OutsideDoorPoint(room);
            (float X, float Y) f = RefugeeCamp.FacingIntoRoom(room);

            bool occluded = ExplorationWalls.SegmentHitsAnyWall(closed, eye.X, eye.Y, a.X, a.Y);
            Assert.True(occluded, $"{a.RoomNumber} 号房：门关着，却看得见门后那只 —— 跳脸就不存在了。");
            Assert.False(VisionLogic.CanSee(V(eye), V(f), V((a.X, a.Y)), cone, occluded));
        }
    }

    /// <summary>
    /// 🔴 <b>"开门跳脸"重定义（用户口径）：不靠"丧尸贴在门后 ≤90px"，靠"推开这扇房门＝唤醒锁在里面那只"。</b>
    /// 每只伏击丧尸是**门后特殊丧尸**（<see cref="ZombieActivation"/>）：门未开时**完全冻结**——免疫视野/噪音/靠近
    /// （你贴在关着的门外它也不动、也不闻你），有且仅有**推开它那扇房门**才唤醒它、转普通扑上来。**一门唤醒一只**。
    /// <para>
    /// 这条<b>去掉了旧的"跳脸距离 90 &lt; 暗视距 124"像素约束</b>——威胁绑门实体、与尺度无关，Phase2 才能把房间放大。
    /// </para>
    /// </summary>
    [Fact]
    public void OpeningARoomDoor_WakesThatRoomsFrozenAmbusher_AndOnlyThatOne()
    {
        // 门未开：伏击丧尸是冻结的门后特殊丧尸（对视野/噪音/靠近全免疫）。
        Assert.True(ZombieActivation.IsFrozen(doorLocked: true, activated: false));
        Assert.False(ZombieActivation.RespondsToPerception(explorationMode: true, doorLocked: true, activated: false));
        Assert.False(ZombieActivation.RespondsToNoise(explorationMode: true, doorLocked: true, activated: false));

        foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
        {
            string myDoor = RefugeeCamp.WakeDoorFor(a);
            Assert.Equal(RefugeeCamp.DoorNameOf(a.RoomNumber), myDoor);   // 唤醒门＝它那间房的门
            // 推开自己那扇房门 ⇒ 唤醒它；推开别人的房门 ⇒ 不唤醒它。
            Assert.True(ZombieActivation.DoorOpenActivates(new[] { myDoor }, myDoor, activated: false));
            int other = a.RoomNumber == 1 ? 2 : 1;
            Assert.False(ZombieActivation.DoorOpenActivates(new[] { myDoor }, RefugeeCamp.DoorNameOf(other), activated: false));
        }

        // 一门唤醒一只：房号互不相同 ⇒ 唤醒门互不相同（拓扑一一对应）。
        Assert.Equal(
            RefugeeCamp.AmbushZombies.Count,
            RefugeeCamp.AmbushZombies.Select(a => RefugeeCamp.WakeDoorFor(a)).Distinct().Count());
    }

    /// <summary>
    /// <b>刻意不是每扇门后都有。</b>18 间房、10 只伏击 —— 如果每扇门后都有，玩家两分钟就学会"开门即后撤"，
    /// 那就又变回脚本了。<b>不知道哪扇门后有</b>，才是这一关的全部。
    /// </summary>
    [Fact]
    public void NotEveryDoorHasSomethingBehindIt_ThatIsThePoint()
    {
        Assert.True(RefugeeCamp.AmbushZombies.Count < RefugeeCamp.Rooms.Count,
            "每间房都有伏击 ⇒ 玩家会学会一律后撤 ⇒ 未知没了，跳脸也就没了。");
        Assert.True(RefugeeCamp.AmbushZombies.Count >= 8, "伏击太少，18 扇门就成了 18 次白开。");

        // 每只伏击丧尸都真的在它那间房里（不在房里，就不叫"门后"）
        foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
        {
            WallRect r = RefugeeCamp.Room(a.RoomNumber).Rect;
            Assert.InRange(a.X, r.X, r.Right);
            Assert.InRange(a.Y, r.Y, r.Bottom);
        }
        // 一间房最多一只（两只在同一扇门后 ⇒ 卡门口的 1v1 保证就破了）
        Assert.Equal(
            RefugeeCamp.AmbushZombies.Count,
            RefugeeCamp.AmbushZombies.Select(a => a.RoomNumber).Distinct().Count());
    }

    // ══════════════════════ 🔴 「物资分散在每一个房间中」 ══════════════════════

    /// <summary>
    /// 🔴 <b>14 处搜刮点分在 14 个不同的房间里 —— 一间房绝不放两处。</b>
    /// 用户要的是「分散」：这一关的回报不是某个大堆，是<b>十四次"要不要推开这扇门"</b>。
    /// </summary>
    [Fact]
    public void FourteenCaches_InFourteenDifferentRooms_NeverTwoInOne()
    {
        IReadOnlyList<string> ids = ExplorationCache.CacheIdsFor(RefugeeCamp.DestinationName);
        Assert.InRange(ids.Count, 10, 16);   // 规模「中」
        Assert.Equal(14, ids.Count);

        var rooms = RefugeeCamp.CacheSpots.Select(s => s.RoomNumber).ToList();
        Assert.Equal(rooms.Count, rooms.Distinct().Count());
        Assert.Equal(14, rooms.Count);

        // 每处都真的落在它登记的那间房里
        foreach (RefugeeCacheSpot s in RefugeeCamp.CacheSpots)
        {
            WallRect r = RefugeeCamp.Room(s.RoomNumber).Rect;
            Assert.InRange(s.X, r.X, r.Right);
            Assert.InRange(s.Y, r.Y, r.Bottom);
        }

        // 四处登记同步
        Assert.Equal(
            ids.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            RefugeeCamp.CacheSpots.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal).ToList());

        var flags = new StoryFlags();
        foreach (string id in ids)
        {
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)));
            Assert.NotNull(ExplorationCache.Resolve(id, flags));
        }
    }

    /// <summary>
    /// 「分散」的字面意思：<b>每一处都不大</b>。难民随身带的东西——吃的、布、绷带、一点白银。
    /// <b>没有枪、没有护甲。</b>（「高风险不是永远高回报」——别因为"后期"就无脑塞高价值物资）
    /// </summary>
    [Fact]
    public void ScatteredMeansSmall_AndNotOneGunInTheWholeCamp()
    {
        var flags = new StoryFlags();
        foreach (string id in ExplorationCache.CacheIdsFor(RefugeeCamp.DestinationName))
        {
            CacheResult r = ExplorationCache.Resolve(id, flags)!.Value;
            Assert.NotEmpty(r.Loot);
            Assert.True(r.Loot.Count <= 3, $"{id} 一处放了 {r.Loot.Count} 样 —— 那不叫分散，那叫大堆。");
            foreach (LootItem it in r.Loot)
                Assert.NotEqual(LootKind.Weapon, it.Kind);
        }
    }

    /// <summary>一次性：搜过就是空的。</summary>
    [Fact]
    public void EachCacheIsOneShot()
    {
        var flags = new StoryFlags();
        foreach (string id in ExplorationCache.CacheIdsFor(RefugeeCamp.DestinationName))
        {
            CacheResult? first = ExplorationCache.Resolve(id, flags);
            Assert.NotNull(first);
            flags.Set(first!.Value.StoryFlag, "true");
            Assert.Null(ExplorationCache.Resolve(id, flags));
        }
    }

    // ══════════════════════ 几何硬不变量 ══════════════════════

    /// <summary>
    /// 🔴 <b>[SPEC-T60·Phase2] 难民营地放大到中档 3200×2200（≈3天探索量级，数值拟定待调）。</b>
    /// <para>
    /// 这一关此前被<b>固定像素</b>钉死在 2400×1600：跳脸要求"丧尸贴在门后 ≤90px"、而暗视距只有 124px
    /// ⇒ 房间不能大过这几十像素的窗口。Phase1 把威胁改成<b>绑门实体的开门唤醒</b>（<see cref="ZombieActivation"/>）后，
    /// 触发<b>与尺度无关</b> ⇒ 像素约束解绑 ⇒ 房间可以任意大。<b>这才是这一关能放大的唯一理由。</b>
    /// </para>
    /// <para>
    /// 🔴 <b>门宽 48 / 过道宽 72 刻意不缩放</b>（同 <c>ExplorationWalls.cs:231</c> 医院先例）：
    /// 门口一次只过得来一只的**战术漏斗仍然有效且要保住**——它不再是"跳脸"的承载物，但它仍是玩家的正解。
    /// </para>
    /// </summary>
    [Fact]
    public void Phase2_TheCampWasEnlargedToTheMidTier_AndTheDoorwayWasNot()
    {
        Assert.Equal(3200f, Canvas.W);
        Assert.Equal(2200f, Canvas.H);

        // 门宽/过道宽是**不缩放**的：放大的是纵深，不是漏斗。
        Assert.Equal(48f, RefugeeCamp.DoorwayWidth);
        Assert.Equal(72f, RefugeeCamp.CorridorWidth);
        Assert.True(RefugeeCamp.DoorwayWidth < 2f * ZombieDiameter,
            "门宽放大到能并排两只 ⇒ 卡门口的 1v1 保证没了 —— 战术漏斗是要保住的，不是要拆的。");

        // 竖脊仍正对营门；西侧豁口仍正对北横道（放大后两个入口的对位关系不变）。
        Assert.Equal(RefugeeCamp.SpineLeft + RefugeeCamp.CorridorWidth / 2f, RefugeeCamp.GateX);
        Assert.Equal(RefugeeCamp.CorridorNorthY + RefugeeCamp.CorridorWidth / 2f, RefugeeCamp.WestBreachY);
        Assert.Equal(RefugeeCamp.CorridorWidth, RefugeeCamp.SpineRight - RefugeeCamp.SpineLeft);

        // 整片营区（含外墙）落在画布内，且没有贴边（返回区要在营门正南的空地上）。
        WallRect interior = RefugeeCamp.Interior;
        Assert.All(RefugeeCamp.Walls(), w =>
        {
            Assert.InRange(w.X, 0f, Canvas.W);
            Assert.InRange(w.Right, 0f, Canvas.W);
            Assert.InRange(w.Y, 0f, Canvas.H);
            Assert.InRange(w.Bottom, 0f, Canvas.H);
        });
        Assert.True(interior.Bottom < ReturnZone.Y, "营区压到返回区上了 —— 出口会被砌进排屋里。");

        // 三排 × 六列的房间全在 Interior 里（Interior 是给视觉地台用的**同一个**事实源，不许两头对数）。
        Assert.All(RefugeeCamp.Rooms, r =>
        {
            Assert.InRange(r.Rect.X, interior.X, interior.Right);
            Assert.InRange(r.Rect.Right, interior.X, interior.Right);
            Assert.InRange(r.Rect.Y, interior.Y, interior.Bottom);
            Assert.InRange(r.Rect.Bottom, interior.Y, interior.Bottom);
        });
    }

    /// <summary>「大量的小房间」＋每间<b>恰好一扇门</b>（房间是死胡同 —— 这正是"卡门口"成立的前提）。</summary>
    [Fact]
    public void EighteenRooms_EachWithExactlyOneDoor()
    {
        Assert.Equal(18, RefugeeCamp.Rooms.Count);
        Assert.Equal(18, RefugeeCamp.Doors().Count);
        Assert.Equal(
            RefugeeCamp.Rooms.Select(r => r.DoorName).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            RefugeeCamp.Doors().Select(d => d.Name).OrderBy(x => x, StringComparer.Ordinal).ToList());
        Assert.All(RefugeeCamp.Doors(), d => Assert.Equal(DoorState.Closed, d.Initial));
    }

    /// <summary>
    /// 🔴 <b>退路永远在：18 扇门全关死，你照样走得出去。</b>
    /// 两个入口<b>都关不上</b>，而 18 扇门<b>全是房门</b>（死胡同）—— <b>一扇也不在退路上</b>。
    /// </summary>
    [Fact]
    public void EvenWithAllEighteenDoorsShut_TheWayOutIsStillOpen()
    {
        var entrances = RefugeeCamp.Entrances();
        Assert.True(entrances.Count >= 2);
        Assert.All(entrances, e => Assert.Null(e.DoorName));

        List<WallRect> closed = WallsWithAllDoorsClosed();
        (float X, float Y)[] inCorridors =
        {
            RefugeeCamp.GatePoint,                                             // 营门内侧
            (700f, RefugeeCamp.CorridorNorthY + RefugeeCamp.CorridorWidth / 2f), // 北横道
            (2500f, RefugeeCamp.CorridorSouthY + RefugeeCamp.CorridorWidth / 2f),// 南横道
            (RefugeeCamp.GateX, 420f),                                         // 竖脊·最深
        };
        foreach ((float X, float Y) p in inCorridors)
        {
            Assert.True(LevelReachability.PathExists(closed, Level, p, ReturnZone),
                $"18 扇门全关上之后，({p.X},{p.Y}) 走不出关外 —— 玩家被关死在排屋里了。");
        }
    }

    /// <summary>每个房间关上门就是**死胡同**（进得去、出不来的房间是陷阱；门没上锁 ⇒ 推开就出得来）。</summary>
    [Fact]
    public void EveryRoomIsADeadEnd_AndOpeningItsDoorAlwaysGetsYouBackOut()
    {
        foreach (RefugeeRoom room in RefugeeCamp.Rooms)
        {
            (float X, float Y) inside = (room.Rect.X + room.Rect.Width / 2f, room.Rect.Y + room.Rect.Height / 2f);

            // 门全关 ⇒ 房里的东西出不来（丧尸被关在门后，这一关才成立）
            Assert.False(
                LevelReachability.PathExists(WallsWithAllDoorsClosed(), Level, inside, ReturnZone),
                $"{room.Number} 号房关着门却通着外面 —— 那门后的丧尸根本关不住。");

            // 只推开它自己那扇 ⇒ 出得来（门没上锁，绝不会被永久卡死）
            List<WallRect> opened = LevelReachability.WithOneDoorOpen(
                RefugeeCamp.Walls(), RefugeeCamp.Doors(), room.DoorName);
            Assert.True(
                LevelReachability.PathExists(opened, Level, inside, ReturnZone),
                $"{room.Number} 号房：推开自己那扇门也走不出去 —— 玩家会被永久卡死。");
        }
    }

    /// <summary>没有一处搜刮点/丧尸被砌进墙里。</summary>
    [Fact]
    public void NothingIsBuriedInsideAWall()
    {
        IReadOnlyList<WallRect> walls = RefugeeCamp.Walls();
        foreach (RefugeeCacheSpot s in RefugeeCamp.CacheSpots)
            Assert.False(LevelReachability.IsBlocked(walls, s.X, s.Y, 0f), $"搜刮点「{s.Label}」被砌进墙里了。");
        foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
            Assert.False(LevelReachability.IsBlocked(walls, a.X, a.Y, 0f), $"{a.RoomNumber} 号房的丧尸被砌进墙里了。");
        foreach ((float X, float Y) z in RefugeeCamp.CorridorZombieSpots)
            Assert.False(LevelReachability.IsBlocked(walls, z.X, z.Y, 0f), $"过道丧尸 ({z.X},{z.Y}) 被砌进墙里了。");
    }

    // ══════════════════════ authored：只做环境叙事 ══════════════════════

    /// <summary>
    /// 用户<b>没有</b>给难民营地任何剧情梗概 ⇒ <b>只做环境叙事</b>：写这地方留下的样子，
    /// <b>不编造角色、不编造前史、不引入新人物</b>。克制、简短。
    /// </summary>
    [Fact]
    public void NoStoryWasGiven_SoNoStoryWasInvented()
    {
        var spots = NarrativeSpotRegistry.ForDestination(RefugeeCamp.DestinationName).ToList();
        Assert.Equal(2, spots.Count);
        Assert.All(spots, s =>
        {
            Assert.NotEmpty(s.Pages);
            Assert.True(s.Pages.Count <= 3, "环境叙事要克制、简短。");
            Assert.StartsWith("narrative_", s.Id, StringComparison.Ordinal);
        });

        // 两处都铺在**没有物资、也没有伏击丧尸**的房间里（这一关仅有的两间"安静的房"）
        var lootRooms = RefugeeCamp.CacheSpots.Select(s => s.RoomNumber).ToHashSet();
        var ambushRooms = RefugeeCamp.AmbushZombies.Select(a => a.RoomNumber).ToHashSet();
        foreach (int n in new[] { 14, 18 })
        {
            Assert.DoesNotContain(n, lootRooms);
            Assert.DoesNotContain(n, ambushRooms);
        }
    }

    // ══════════════════════ Sim 零漂移（结构性） ══════════════════════

    /// <summary>
    /// 新点位<b>不在 Sim 的结算路径上</b>：<c>RefugeeCamp</c> / <c>RuinedChurch</c> / <c>LevelReachability</c> /
    /// <c>ExplorationLighting</c> 都不在 <c>DeadSignal.Sim.csproj</c> 的 Link 清单里 ⇒ Sim 根本读不到它们。
    /// </summary>
    [Fact]
    public void SimCannotEvenSeeTheseLevels()
    {
        Assert.Empty(typeof(RefugeeCamp).Assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null && a.Name.Contains("DeadSignal.Sim", StringComparison.Ordinal)));
    }
}
