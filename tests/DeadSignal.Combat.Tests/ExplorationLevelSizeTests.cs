using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 探索关画布尺寸 per-destination 查表（<see cref="ExplorationLevelSize"/>）的护栏。
/// <para>
/// 本任务是纯使能重构：把 <c>TestExploration</c> 写死的 2400×1600 画布改成按目的地取值，但
/// <b>覆盖表为空 ⇒ 每一关仍取默认 2400×1600</b>（零漂移基线）。这些测试把"默认 = 历史固定尺寸"
/// 与"未登记的目的地一律回退默认"钉死——将来 per-map impl 往覆盖表加行时，这里能挡住误改默认值/误破回退。
/// </para>
/// </summary>
public class ExplorationLevelSizeTests
{
    /// <summary>默认尺寸＝改造前 TestExploration 的 const 值（2400×1600）。任何人改动它都要过这条。</summary>
    [Fact]
    public void Default_Is_Historical_2400x1600()
    {
        Assert.Equal(2400f, ExplorationLevelSize.DefaultWidth);
        Assert.Equal(1600f, ExplorationLevelSize.DefaultHeight);
    }

    /// <summary>
    /// 零漂移核心断言：当前**所有**探索目的地都没登记覆盖尺寸 ⇒ 逐一 SizeFor 都必须回退到默认 2400×1600。
    /// 覆盖了 TestExploration.Initialize 那串 if-else 里出现的每一个目的地名。
    /// </summary>
    [Theory]
    // 超市/加油站/东部新村/联合收割机仓库/广播台/难民营地/破败教堂已登记放大覆盖（3200×2200，中图·≈3天）——
    //   从"回退默认"用例移出，尺寸由下方 MidMaps 专测钉。（难民营地＝Phase2：开门激活解绑"跳脸≤90px"像素约束后才放得大；
    //   破败教堂＝Phase2：开门唤醒解绑"12 只须同时挤进 300px 白昼锥"像素约束后才放得大。）
    // 医院/南林村庄已登记放大覆盖（4200×2800，大图·≈5天）——从"回退默认"用例移出，其尺寸由下方大图专测钉。
    // 河边小屋/守林人小屋/消防站/药店/城市之巅/警察局已登记适度放大覆盖（2800×1900，小图·≈1.17×/1.19×）——
    //   从"回退默认"用例移出，尺寸由下方 SmallMaps 专测钉。（城市之巅＝占位地台图无固定像素不变量，望远镜瞭望为 flag 式，故随小图放大。）
    // 🔴 policy A + 用户裁决 A：金手指帮根据地维持原尺寸零改动（均匀放大会破"开一枪招几人"噪音招怪，须另立单校准）
    //   ⇒ 仍在此回退默认 2400×1600。（斯图尔特改用绕庭院中心逆缩放放大，已移入下方 MidMaps 专测。）
    [InlineData(WorldMapPanel_GoldfingerBaseName)]
    [InlineData(ExplorationCache.SewerName)]
    public void Every_Destination_FallsBack_To_Default(string destination)
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(destination);
        Assert.Equal(ExplorationLevelSize.DefaultWidth, w);
        Assert.Equal(ExplorationLevelSize.DefaultHeight, h);
    }

    /// <summary>
    /// 废弃医院登记了放大覆盖：大图·目标≈5天探索量级，均匀 1.75×（4200×2800）。
    /// 数值拟定待调（方向对着 5 天锚点）——若日后调档，这条与 <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// </summary>
    [Fact]
    public void Hospital_Is_Enlarged_To_4200x2800()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(ExplorationCache.HospitalName);
        Assert.Equal(4200f, w);
        Assert.Equal(2800f, h);
    }

    /// <summary>
    /// 南林村庄登记了放大覆盖：大图·目标≈5天探索量级（4200×2800，村落院墙巷道 + 加密 30→42 + 游荡丧尸按比例上调）。
    /// 数值拟定待调（方向对着 5 天锚点）——若日后调档，这条与 <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// </summary>
    [Fact]
    public void SouthForestVillage_Is_Enlarged_To_4200x2800()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(VillageRescue.DestinationName);
        Assert.Equal(4200f, w);
        Assert.Equal(2800f, h);
    }

    /// <summary>
    /// 中图（超市/加油站/东部新村/联合收割机仓库/广播台）登记了放大覆盖：中图·目标≈3天探索量级，均匀 4/3×（3200×2200）。
    /// 数值拟定待调（方向对着 3 天锚点，≈1800–2160s 在关工作量）——若日后调档，这条与
    /// <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// <para>🔴 斯图尔特家族庄园（中·高危）也在此列：用**绕庭院中心逆缩放**放大到 3200×2200——StuartManor.cs 的 const
    /// 同步 + Posts 逐轴逆缩放 ⇒ 哨位间/庭院噪音的像素距离逐字节不变（StuartManorTests 噪音带恒绿），非硬缩放、不违 policy A。
    /// 金手指帮根据地按用户裁决 A 维持原尺寸（在上方回退 Theory）。</para>
    /// <para>🔴 难民营地（中·高危）＝[SPEC-T60] Phase2：这一关此前被<b>固定像素</b>钉死——旧「开门跳脸」要求伏击丧尸
    /// 贴在门后 ≤90px（&lt; 室内暗视距 124px），房间大不过那个窗口。Phase1 把威胁改成**绑门实体的开门唤醒**
    /// （<c>ZombieActivation</c>·一门一只）后触发与尺度无关 ⇒ 约束解绑 ⇒ 按用户口径「中→3天」放大到 3200×2200。
    /// authored 语义（18 房/一房一门/14 处物资分 14 间房/10 只各锁自己那扇门）只重排坐标；**门宽 48／过道宽 72 不缩放**
    /// ⇒ 卡门口打 1v1 的战术漏斗保住（RefugeeCampTests 钉死）。</para>
    /// <para>🔴 破败教堂（中·高危·authored 视野谜题关）＝[SPEC-T60] Phase2，且**曾被 policy A 裁定"维持不放大"**：
    /// 旧「吓一跳」要求墓地 12 只**同时挤进门洞站位的固定 300px 白昼锥**，墓地进深被锥半径钉死。Phase1 把它改成
    /// 「推开墓地边界两扇门 ⇒ 门后整片冻结丧尸唤醒涌来」（<c>ZombieActivation</c>）后触发与尺度无关 ⇒ 约束解绑、
    /// 该裁定作废 ⇒ 按用户口径「中→3天」放大到 3200×2200。authored 语义（墓地关得死/退路两洞永远敞着/两处证据正文）
    /// 只重排坐标；**门宽 72／中央走道 140／侧廊 64／长椅排距 90 不缩放**（RuinedChurchTests 钉死）。</para>
    /// </summary>
    [Theory]
    [InlineData(ExplorationCache.SupermarketName)]
    [InlineData(ExplorationCache.GasStationName)]
    [InlineData(ExplorationCache.EastNewVillageName)]
    [InlineData(ExplorationCache.HarvesterWarehouseName)]
    [InlineData(StuartManor.DestinationName)]          // 斯图尔特：逆缩放放大，噪音几何逐字节不变
    [InlineData(WorldMapPanel_BroadcastStationName)]   // [SPEC-T60] 广播台：占位地台⇒真广播站结构，3200×2200
    [InlineData(RefugeeCamp.DestinationName)]          // 🔴 [SPEC-T60] 难民营地（Phase2·开门激活解绑像素约束后放大）
    [InlineData(RuinedChurch.DestinationName)]         // 🔴 [SPEC-T60] 破败教堂（Phase2·开门唤醒解绑 300px 锥约束后放大，policy A 裁定作废）
    public void MidMaps_Enlarged_To_3200x2200(string destination)
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(destination);
        Assert.Equal(3200f, w);
        Assert.Equal(2200f, h);
    }

    /// <summary>
    /// 小图五张（河边小屋/守林人小屋/消防站/南丁格尔药店/城市之巅）登记了适度放大覆盖：小图·2800×1900
    /// （＝默认 2400×1600 放大 ~1.17×/1.19×，比原大一档但仍属小图，不追 3 天量级）。用户口径「地图尺寸都要更大一些」。
    /// 数值拟定待调——若日后调档，这条与 <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// <para>守林人小屋含 ForageLogic 采集点，重排坐标不动 id ⇒ ForageFarmingButcheryTests 恒绿。</para>
    /// <para>城市之巅＝占位地台图无 authored 固定像素不变量（望远镜瞭望为 flag 式发现点，LookoutSightingTests 纯逻辑无画布耦合）⇒ 随小图放大。</para>
    /// <para>🔴 警察局＝[SPEC-T60] Phase2：探索威胁模型改成"开门激活门后丧尸"后，威胁触发绑门实体、**与尺度无关**
    /// ⇒ 旧的固定像素约束已解绑，可随小图放大（authored 拓扑「中央脊廊+侧房·4 丧尸各藏一房·拘留区铁门后那只撬开才醒」
    /// 只重排坐标不改语义；门宽＝走廊宽 140 刻意不缩放）。核心护栏"任一可行走点感知≤1"放大后已重核（PoliceStationTests）。</para>
    /// </summary>
    [Theory]
    [InlineData(ExplorationCache.RiversideCabinName)]
    [InlineData(ExplorationCache.WatchersCabinName)]      // 守林人小屋（内部路由键"守望者森林小屋"）
    [InlineData(ExplorationCache.FireStationName)]
    [InlineData(NurseRecruit.DestinationName)]            // 南丁格尔的小药店（内部键"药店"）
    [InlineData(ExplorationCache.CityRooftopLookoutName)] // 城市之巅（占位地台·望远镜 flag 式瞭望）
    [InlineData(ExplorationCache.PoliceStationName)]      // 🔴 [SPEC-T60] 警察局（Phase2·开门激活解绑像素约束后放大）
    public void SmallMaps_Enlarged_To_2800x1900(string destination)
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(destination);
        Assert.Equal(2800f, w);
        Assert.Equal(1900f, h);
    }

    /// <summary>未知/未登记目的地名回退默认（防呆）。</summary>
    [Fact]
    public void Unknown_Destination_FallsBack_To_Default()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor("no_such_place_ever");
        Assert.Equal(2400f, w);
        Assert.Equal(1600f, h);
    }

    /// <summary>null 目的地名不炸、回退默认（DestinationName 基类默认空串，防御 null 传入）。</summary>
    [Fact]
    public void Null_Destination_FallsBack_To_Default()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(null!);
        Assert.Equal(2400f, w);
        Assert.Equal(1600f, h);
    }

    // WorldMapPanel 是 Godot 类型、未 Link 进本工程，其目的地名以字面量镜像（与 WorldMapPanel.*Name 逐字一致）。
    private const string WorldMapPanel_GoldfingerBaseName = "金手指帮根据地";
    private const string WorldMapPanel_WatchersCabinName = "守望者森林小屋";
    private const string WorldMapPanel_CityRooftopLookoutName = "城市之巅瞭望观景台";
    private const string WorldMapPanel_BroadcastStationName = "广播台";
}
