using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 GoldfingerDiscovery.cs / ContainerLoot.cs / StoryFlags.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 探索点「搜刮点」的纯逻辑：把探索队走到某个搜刮点触发的 cacheId，
// 解析成 (一次性 flag / 一批掉落 LootItem / 环境叙事标题+正文)。已搜过（flag 已置）则返回 null 不重复。
// 与 GoldfingerDiscovery（剧情尸体/日记，单书）同构，区别是本类返回**一批掉落**（武器+书+材料/食物/医疗）。
//
// 现状（架构说明）：探索关（TestExploration）**没有关卡内点击式搜刮容器**（那是 CampMain 营地专属）；
// 探索点唯一取物路径是**发现点式**——探索队走入一处 Area2D 即触发。本类即那套发现点机制的通用 loot 版：
// 每个搜刮点是一处发现区，踏入即把整批掉落入库并弹一段环境叙事。位置/铺设在 TestExploration（Godot 层），
// 本类只负责「cacheId → 掉落+叙事」的纯判定，可脱 Godot 单测。
//
// 两个前中期探索点（用户拍板："加两个探索点 河边小屋 联合收割机仓库"）：
//   · 河边小屋（河边猎人小屋语境）：枪柜 ← 栓动猎枪；床底木箱 ← 通用搜刮（食物/医疗/材料）。
//   · 联合收割机仓库（农机棚/工具房语境）：工具柜（近入口）← 通用木工材料；阁楼铁皮箱（藏深）←《进阶木匠技术》。
// 注：《木匠入门》原拟放本仓库工具柜，用户改单撤出——改由「神秘商人」系统出售（见商人系统，另一系统负责），本类不再投放它。
// 投放搭配为用户拍板（"按直觉搭配投放"）；通用物资量级/位置深浅（难度梯度）为 draft 拟定待调。
// 环境叙事为 draft 草稿，最终由用户优化；本类只保证"读值→判定→出掉落+叙事"可跑、可测，不碰 Godot、不写 flag。

/// <summary>一次搜刮的落地结果：置哪个 flag、给哪批掉落、弹什么环境叙事（标题 + 正文）。</summary>
public readonly record struct CacheResult(string StoryFlag, IReadOnlyList<LootItem> Loot, string Title, string Narrative);

/// <summary>
/// 探索点搜刮解析。<see cref="Resolve"/> 由 CampMain 在探索队踏入搜刮点时调用：
/// 返回 <see cref="CacheResult"/> 则 CampMain 负责置 flag、经 <c>LootApplication</c> 落地整批掉落、弹叙事面板；
/// 返回 <c>null</c> 表示未知 id 或已搜过（flag 已置），什么都不做。
/// <see cref="CacheIdsFor"/> 供 TestExploration 按目的地铺出对应搜刮点。
/// </summary>
public static class ExplorationCache
{
    // ——目的地名（与 WorldMapPanel 的 Destination.Name 一致，务必同步）——
    public const string RiversideCabinName = "河边小屋";
    public const string HarvesterWarehouseName = "联合收割机仓库";
    /// <summary>城市之巅瞭望观景台目的地名，须与 <c>WorldMapPanel.CityRooftopLookoutName</c> 一致（本类脱 Godot 单测，故本地持有副本）。</summary>
    public const string CityRooftopLookoutName = "城市之巅瞭望观景台";

    // ——搜刮点 id（探索关内 Area2D 触发时上报）——
    public const string RiversideGunCabinetId = "cache_riverside_gun_cabinet";
    public const string RiversideBedChestId = "cache_riverside_bed_chest";
    public const string WarehouseToolCabinetId = "cache_warehouse_tool_cabinet";
    public const string WarehouseAtticChestId = "cache_warehouse_attic_chest";
    // 城市之巅瞭望观景台搜刮点（望远镜发现尸潮的剧情由 LookoutSighting 另管，本处只是同址物资）：
    //   · 游客服务台/礼品柜（近入口，浅）：观景台是旅游景点，游客遗留食水+医疗小物+纪念品店杂物。
    //   · 瞭望员值班室（藏深）：高空值守间的应急物资——燃油/急救/望远镜等光学信号设备拆出的电子件。
    public const string LookoutGiftShopId = "cache_lookout_gift_shop";
    public const string LookoutWardensRoomId = "cache_lookout_wardens_room";

    // ——一次性 flag（防重复搜刮，跨关持久）——
    public const string RiversideGunCabinetFlag = "searched_riverside_gun_cabinet";
    public const string RiversideBedChestFlag = "searched_riverside_bed_chest";
    public const string WarehouseToolCabinetFlag = "searched_warehouse_tool_cabinet";
    public const string WarehouseAtticChestFlag = "searched_warehouse_attic_chest";
    public const string LookoutGiftShopFlag = "searched_lookout_gift_shop";
    public const string LookoutWardensRoomFlag = "searched_lookout_wardens_room";

    // ——关键投放物标识（须与 WeaponTable / BookLibrary 一致）——
    /// <summary>栓动猎枪武器名，须与 <c>WeaponTable.BoltActionHuntingRifle().Name</c> 一致。</summary>
    public const string BoltActionRifleName = "栓动猎枪";

    // 注：《木匠入门》(carpentry_basics) 改由「神秘商人」系统出售，不由本类投放，故此处不再持有其常量。

    /// <summary>《进阶木匠技术》书 id，须与 <c>BookLibrary.AdvancedCarpentry</c> 一致。</summary>
    public const string AdvancedCarpentryBookId = "advanced_carpentry";

    /// <summary>
    /// 某目的地铺出的搜刮点 id 清单（近入口在前、藏深在后；TestExploration 按此序铺 Area2D）。
    /// 非搜刮点目的地返回空清单（行为不变）。
    /// </summary>
    public static IReadOnlyList<string> CacheIdsFor(string destinationName) => destinationName switch
    {
        RiversideCabinName => new[] { RiversideGunCabinetId, RiversideBedChestId },
        HarvesterWarehouseName => new[] { WarehouseToolCabinetId, WarehouseAtticChestId },
        // 望远镜发现点(尸潮剧情)不在此列——那是 LookoutSighting 管的置旗标+叙事，非物资搜刮。
        CityRooftopLookoutName => new[] { LookoutGiftShopId, LookoutWardensRoomId },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// 解析一次搜刮。未知 id 或对应 flag 已置（已搜过）返回 <c>null</c>。
    /// 本方法**不写** flag（无副作用）；置 flag 由调用方在落地掉落后进行。
    /// </summary>
    public static CacheResult? Resolve(string cacheId, StoryFlags flags)
    {
        return cacheId switch
        {
            RiversideGunCabinetId when NotYet(flags, RiversideGunCabinetFlag) => new CacheResult(
                RiversideGunCabinetFlag,
                new[]
                {
                    LootItem.Weapon(BoltActionRifleName),
                    LootItem.Material("scrap_cloth", 2),
                },
                RiversideGunCabinetTitle, RiversideGunCabinetNarrative),

            RiversideBedChestId when NotYet(flags, RiversideBedChestFlag) => new CacheResult(
                RiversideBedChestFlag,
                new[]
                {
                    LootItem.Food(2),
                    LootItem.Material("bandage", 2),
                    LootItem.Material("antibiotics", 1),
                    LootItem.Material("bone", 2),
                },
                RiversideBedChestTitle, RiversideBedChestNarrative),

            WarehouseToolCabinetId when NotYet(flags, WarehouseToolCabinetFlag) => new CacheResult(
                WarehouseToolCabinetFlag,
                new[]
                {
                    LootItem.Material("nails", 3),
                    LootItem.Material("wood", 3),
                    LootItem.Material("rope", 1),
                },
                WarehouseToolCabinetTitle, WarehouseToolCabinetNarrative),

            WarehouseAtticChestId when NotYet(flags, WarehouseAtticChestFlag) => new CacheResult(
                WarehouseAtticChestFlag,
                new[]
                {
                    LootItem.Book(AdvancedCarpentryBookId),
                    LootItem.Material("fuel", 2),
                    LootItem.Material("first_aid_kit", 1),
                    LootItem.Food(1),
                },
                WarehouseAtticChestTitle, WarehouseAtticChestNarrative),

            LookoutGiftShopId when NotYet(flags, LookoutGiftShopFlag) => new CacheResult(
                LookoutGiftShopFlag,
                new[]
                {
                    LootItem.Food(2),                       // 游客遗留的瓶装水/零食
                    LootItem.Material("bandage", 2),        // 服务台常备急救小物
                    LootItem.Material("cloth", 2),          // 礼品店纪念围巾/布艺
                    LootItem.Material("scrap_metal", 2),    // 投币望远镜零钱箱/纪念币撬出的碎金属
                },
                LookoutGiftShopTitle, LookoutGiftShopNarrative),

            LookoutWardensRoomId when NotYet(flags, LookoutWardensRoomFlag) => new CacheResult(
                LookoutWardensRoomFlag,
                new[]
                {
                    LootItem.Material("fuel", 2),           // 应急发电/信号灯的燃油
                    LootItem.Material("first_aid_kit", 1),  // 值班室急救包
                    LootItem.Material("components", 1),     // 望远镜/信号设备拆出的电子件
                    LootItem.Material("wire", 2),           // 光学/信号线材
                    LootItem.Food(1),                       // 瞭望员的口粮
                },
                LookoutWardensRoomTitle, LookoutWardensRoomNarrative),

            _ => null,
        };
    }

    /// <summary>该搜刮点尚未搜过（flag 未置）。</summary>
    private static bool NotYet(StoryFlags flags, string flag) => flags == null || !flags.Has(flag);

    // ==== 环境叙事 draft（全文待用户验收/优化；末日生存氛围，不引入新剧情人物/伏笔）====

    // —— 河边小屋 ——
    private const string RiversideGunCabinetTitle = "墙上的枪柜";

    private const string RiversideGunCabinetNarrative =
        "小屋靠河的一面墙上钉着个上了锁的木质枪柜，锁扣早被人撬过又胡乱合上。" +
        "拉开柜门，里头立着一支还算齐整的栓动猎枪——枪身有些锈斑，枪机拉栓还算顺滑，" +
        "看得出原主人是个爱惜家伙的猎人。\n\n" +
        "柜底垫着几块擦枪用的旧棉布，顺手一并收了。";

    private const string RiversideBedChestTitle = "床底的木箱";

    private const string RiversideBedChestNarrative =
        "床板底下拖出一只受潮发胀的木箱。原主人把过冬的物件都藏在了这儿：" +
        "几听没胀气的罐头、一卷还算干净的绷带、一小瓶舍不得用的抗生素。\n\n" +
        "箱角还堆着几根处理干净的兽骨——是这河边猎人打来的猎物留下的，能派上别的用场。";

    // —— 联合收割机仓库 ——
    private const string WarehouseToolCabinetTitle = "墙边的工具柜";

    private const string WarehouseToolCabinetNarrative =
        "仓库一角隔出个小小的工具房，墙边立着排工具柜。" +
        "大件的农机零件早被搬空了，只剩些零碎——大概是哪个农闲的手艺人用剩的。\n\n" +
        "抽屉里散着一把铁钉、几段没用完的木料，柜门后还挂着一卷结实的麻绳。";

    private const string WarehouseAtticChestTitle = "阁楼的铁皮箱";

    private const string WarehouseAtticChestNarrative =
        "顺着摇晃的木梯爬上仓库阁楼，积灰的角落里压着一只上了锁的铁皮箱。" +
        "撬开锁扣，最上面是一本《进阶木匠技术》，书页间还夹着几张手绘的榫卯草图——" +
        "藏得这样深，原主人显然把它当成了看家的本事。\n\n" +
        "箱底还剩两罐燃油和一只没拆封的急救包。";

    // —— 城市之巅瞭望观景台 ——
    private const string LookoutGiftShopTitle = "游客服务台";

    private const string LookoutGiftShopNarrative =
        "观景层入口处是一圈落满灰的游客服务台，玻璃柜里还摆着这座城市的明信片和纪念摆件。" +
        "疏散得很急——柜台后遗下几瓶没开封的水、半盒受潮的零食，还有一小卷服务台常备的绷带。\n\n" +
        "墙角那台投币望远镜的零钱箱被人砸开过，箱底散着些没人再要的硬币和纪念币，" +
        "刮下来倒是几块能回炉的碎金属。";

    private const string LookoutWardensRoomTitle = "瞭望员值班室";

    private const string LookoutWardensRoomNarrative =
        "观景台最高处隔出一间狭小的值班室，是当年瞭望员盯风向、看火情的地方。" +
        "断电前这里显然被当成过临时据点：墙边码着两罐应急燃油，抽屉里压着一只没拆封的急救包。\n\n" +
        "架子上一台拆了一半的信号望远镜——镜筒被卸开，露出里头的线路板和成卷的细线。" +
        "这些光学和信号设备的电子件、线材，在如今比镜片值钱得多。桌上还剩瞭望员没吃完的一份口粮。";
}
