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
    /// <summary>守林人小屋目的地名（＝内部路由键，须与 <c>WorldMapPanel.WatchersCabinName</c> 一致；显示名正名为「守林人小屋」，本类脱 Godot 单测故持副本）。</summary>
    public const string WatchersCabinName = "守望者森林小屋";
    /// <summary>城市之巅瞭望观景台目的地名，须与 <c>WorldMapPanel.CityRooftopLookoutName</c> 一致（本类脱 Godot 单测，故本地持有副本）。</summary>
    public const string CityRooftopLookoutName = "城市之巅瞭望观景台";
    /// <summary>广播台目的地名，须与 <c>WorldMapPanel.BroadcastStationName</c> 一致（本类脱 Godot 单测，故本地持有副本）。</summary>
    public const string BroadcastStationName = "广播台";

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
    // 广播台普通搜刮点（主线「发出设备」定点投放由 RadioMainline.TransmitterDiscoveryId 另管，本处只是同址普通物资，量级对齐现有点）：
    //   · 值班室茶水间（浅/近入口）：台里值班人员遗留的食水+急救小物。
    //   · 备件仓库（藏深）：广播设备维护间的电子件/线材/燃油等零碎。
    public const string BroadcastBreakRoomId = "cache_broadcast_break_room";
    public const string BroadcastPartsStoreId = "cache_broadcast_parts_store";
    // 守林人小屋（小点样板，2 处搜刮点＝小点物资量级）：屋中屋暗间的碗柜（近，屋内里屋）+ 后院柴房（藏深，与哥顿上吊尸同区）。
    //   · 里屋碗柜（暗间）：守林人独居的储粮/急救小物；· 后院柴房：劈柴/修屋的木料工具。哥顿上吊尸+日记B 由 GoldfingerDiscovery 另管（非物资搜刮）。
    public const string RangersCabinPantryId = "cache_rangers_cabin_pantry";
    public const string RangersCabinShedId = "cache_rangers_cabin_shed";
    // 南林村庄（**大点**，[SPEC-B11-补]"5天+探索量"）：一个空间分区的小聚落，铺 9 处搜刮点（救援锁屋另由 VillageRescue 管、不计物资完成度）。
    //   分区：村口/杂物(1) · 民居区(3) · 村中心(2) · 村尾/藏深(3)。近入口在前、藏深在后。
    public const string VillageRoadsideCarId = "cache_village_roadside_car";     // 村口·废弃皮卡后备箱（近）
    public const string VillageKitchenId = "cache_village_kitchen";              // 民居·厨房碗柜
    public const string VillageWardrobeId = "cache_village_wardrobe";            // 民居·卧室衣柜
    public const string VillageBackRoomId = "cache_village_back_room";           // 民居·储藏间木箱
    public const string VillageShopShelfId = "cache_village_shop_shelf";         // 村中心·小卖部货架
    public const string VillageWellToolboxId = "cache_village_well_toolbox";     // 村中心·水井旁工具箱
    public const string VillageToolShedId = "cache_village_tool_shed";           // 村尾·农具棚（深）
    public const string VillageShrineId = "cache_village_shrine";                // 村尾·祠堂供桌（深）
    public const string VillageClinicId = "cache_village_clinic";                // 村尾·卫生所药柜（深）

    // ——一次性 flag（防重复搜刮，跨关持久）——
    public const string RiversideGunCabinetFlag = "searched_riverside_gun_cabinet";
    public const string RiversideBedChestFlag = "searched_riverside_bed_chest";
    public const string WarehouseToolCabinetFlag = "searched_warehouse_tool_cabinet";
    public const string WarehouseAtticChestFlag = "searched_warehouse_attic_chest";
    public const string LookoutGiftShopFlag = "searched_lookout_gift_shop";
    public const string LookoutWardensRoomFlag = "searched_lookout_wardens_room";
    public const string BroadcastBreakRoomFlag = "searched_broadcast_break_room";
    public const string BroadcastPartsStoreFlag = "searched_broadcast_parts_store";
    public const string RangersCabinPantryFlag = "searched_rangers_cabin_pantry";
    public const string RangersCabinShedFlag = "searched_rangers_cabin_shed";
    public const string VillageRoadsideCarFlag = "searched_village_roadside_car";
    public const string VillageKitchenFlag = "searched_village_kitchen";
    public const string VillageWardrobeFlag = "searched_village_wardrobe";
    public const string VillageBackRoomFlag = "searched_village_back_room";
    public const string VillageShopShelfFlag = "searched_village_shop_shelf";
    public const string VillageWellToolboxFlag = "searched_village_well_toolbox";
    public const string VillageToolShedFlag = "searched_village_tool_shed";
    public const string VillageShrineFlag = "searched_village_shrine";
    public const string VillageClinicFlag = "searched_village_clinic";

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
        // 发出设备定点(TransmitterDiscoveryId)不在此列——那是 RadioMainline 管的取设备+推进状态，非物资搜刮。
        BroadcastStationName => new[] { BroadcastBreakRoomId, BroadcastPartsStoreId },
        // 守林人小屋（小点）：里屋碗柜(近) + 后院柴房(深)。哥顿上吊尸+日记B 不在此列——那是 GoldfingerDiscovery 管的剧情发现点。
        WatchersCabinName => new[] { RangersCabinPantryId, RangersCabinShedId },
        // 南林村庄（大点）：9 处搜刮点，近入口→藏深排序（村口→民居→村中心→村尾）。救援锁屋(VillageRescue)不在此列——那是主线入队触发点，不计物资完成度。
        VillageRescue.DestinationName => new[]
        {
            VillageRoadsideCarId,
            VillageKitchenId, VillageWardrobeId, VillageBackRoomId,
            VillageShopShelfId, VillageWellToolboxId,
            VillageToolShedId, VillageShrineId, VillageClinicId,
        },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// 搜刮点 id → 其一次性 flag（供完成度聚合 <c>ExplorationProgress</c> 反查；未知 id 返回空串）。
    /// 与 <see cref="Resolve"/> 内的 id↔flag 配对保持同步。
    /// </summary>
    public static string FlagForCache(string cacheId) => cacheId switch
    {
        RiversideGunCabinetId => RiversideGunCabinetFlag,
        RiversideBedChestId => RiversideBedChestFlag,
        WarehouseToolCabinetId => WarehouseToolCabinetFlag,
        WarehouseAtticChestId => WarehouseAtticChestFlag,
        LookoutGiftShopId => LookoutGiftShopFlag,
        LookoutWardensRoomId => LookoutWardensRoomFlag,
        BroadcastBreakRoomId => BroadcastBreakRoomFlag,
        BroadcastPartsStoreId => BroadcastPartsStoreFlag,
        RangersCabinPantryId => RangersCabinPantryFlag,
        RangersCabinShedId => RangersCabinShedFlag,
        VillageRoadsideCarId => VillageRoadsideCarFlag,
        VillageKitchenId => VillageKitchenFlag,
        VillageWardrobeId => VillageWardrobeFlag,
        VillageBackRoomId => VillageBackRoomFlag,
        VillageShopShelfId => VillageShopShelfFlag,
        VillageWellToolboxId => VillageWellToolboxFlag,
        VillageToolShedId => VillageToolShedFlag,
        VillageShrineId => VillageShrineFlag,
        VillageClinicId => VillageClinicFlag,
        _ => "",
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

            BroadcastBreakRoomId when NotYet(flags, BroadcastBreakRoomFlag) => new CacheResult(
                BroadcastBreakRoomFlag,
                new[]
                {
                    LootItem.Food(2),                       // 值班人员留下的口粮/瓶装水
                    LootItem.Material("bandage", 2),        // 茶水间常备急救小物
                    LootItem.Material("first_aid_kit", 1),  // 台里应急急救包
                },
                BroadcastBreakRoomTitle, BroadcastBreakRoomNarrative),

            BroadcastPartsStoreId when NotYet(flags, BroadcastPartsStoreFlag) => new CacheResult(
                BroadcastPartsStoreFlag,
                new[]
                {
                    LootItem.Material("components", 2),     // 广播设备维护备件
                    LootItem.Material("wire", 3),           // 成卷的信号线材
                    LootItem.Material("fuel", 2),           // 备用发电机燃油
                    LootItem.Material("scrap_metal", 2),    // 拆解机架的碎金属
                },
                BroadcastPartsStoreTitle, BroadcastPartsStoreNarrative),

            // 守林人小屋（小点，量级克制）：里屋碗柜＝独居储粮+急救小物；后院柴房＝木料/绳/钉。
            RangersCabinPantryId when NotYet(flags, RangersCabinPantryFlag) => new CacheResult(
                RangersCabinPantryFlag,
                new[]
                {
                    LootItem.Food(2),                       // 守林人独居的过冬存粮
                    LootItem.Material("bandage", 1),        // 应急急救小物
                },
                RangersCabinPantryTitle, RangersCabinPantryNarrative),

            RangersCabinShedId when NotYet(flags, RangersCabinShedFlag) => new CacheResult(
                RangersCabinShedFlag,
                new[]
                {
                    LootItem.Material("wood", 3),           // 劈好的柴/木料
                    LootItem.Material("rope", 1),           // 修屋用的绳
                    LootItem.Material("nails", 2),          // 零散铁钉
                },
                RangersCabinShedTitle, RangersCabinShedNarrative),

            // —— 南林村庄（大点，量级中档、分区铺设；draft 待用户改）——
            VillageRoadsideCarId when NotYet(flags, VillageRoadsideCarFlag) => new CacheResult(
                VillageRoadsideCarFlag,
                new[]
                {
                    LootItem.Food(1),                       // 逃难者车里没带走的干粮
                    LootItem.Material("scrap_metal", 2),    // 撬下的车壳碎金属
                    LootItem.Material("fuel", 1),           // 油箱里抽出的一点余油
                },
                VillageRoadsideCarTitle, VillageRoadsideCarNarrative),

            VillageKitchenId when NotYet(flags, VillageKitchenFlag) => new CacheResult(
                VillageKitchenFlag,
                new[]
                {
                    LootItem.Food(2),                       // 碗柜里没坏的罐头/干货
                    LootItem.Material("cloth", 1),          // 抽屉里的旧桌布/抹布
                },
                VillageKitchenTitle, VillageKitchenNarrative),

            VillageWardrobeId when NotYet(flags, VillageWardrobeFlag) => new CacheResult(
                VillageWardrobeFlag,
                new[]
                {
                    LootItem.Material("cloth", 2),          // 衣柜里的衣物
                    LootItem.Material("scrap_cloth", 2),    // 撕开的碎布
                },
                VillageWardrobeTitle, VillageWardrobeNarrative),

            VillageBackRoomId when NotYet(flags, VillageBackRoomFlag) => new CacheResult(
                VillageBackRoomFlag,
                new[]
                {
                    LootItem.Food(1),                       // 储藏间角落的存粮
                    LootItem.Material("nails", 2),          // 杂物里的铁钉
                    LootItem.Material("rope", 1),           // 一卷麻绳
                },
                VillageBackRoomTitle, VillageBackRoomNarrative),

            VillageShopShelfId when NotYet(flags, VillageShopShelfFlag) => new CacheResult(
                VillageShopShelfFlag,
                new[]
                {
                    LootItem.Food(2),                       // 小卖部货架上没抢光的食水
                    LootItem.Material("bandage", 2),        // 柜台后的常备创可贴/绷带
                },
                VillageShopShelfTitle, VillageShopShelfNarrative),

            VillageWellToolboxId when NotYet(flags, VillageWellToolboxFlag) => new CacheResult(
                VillageWellToolboxFlag,
                new[]
                {
                    LootItem.Material("nails", 2),          // 修井/修屋的铁钉
                    LootItem.Material("wood", 2),           // 井台边码的木料
                    LootItem.Material("wire", 1),           // 一卷铁丝
                },
                VillageWellToolboxTitle, VillageWellToolboxNarrative),

            VillageToolShedId when NotYet(flags, VillageToolShedFlag) => new CacheResult(
                VillageToolShedFlag,
                new[]
                {
                    LootItem.Material("wood", 3),           // 农具棚的成堆木料
                    LootItem.Material("rope", 1),           // 捆农具的粗绳
                    LootItem.Material("nails", 3),          // 一盒铁钉
                },
                VillageToolShedTitle, VillageToolShedNarrative),

            VillageShrineId when NotYet(flags, VillageShrineFlag) => new CacheResult(
                VillageShrineFlag,
                new[]
                {
                    LootItem.Food(2),                       // 祠堂供桌上没腐坏的供品/存粮
                    LootItem.Material("bone", 2),           // 祭祀用的骨器/兽骨
                },
                VillageShrineTitle, VillageShrineNarrative),

            VillageClinicId when NotYet(flags, VillageClinicFlag) => new CacheResult(
                VillageClinicFlag,
                new[]
                {
                    LootItem.Material("bandage", 2),        // 卫生所药柜的绷带
                    LootItem.Material("antibiotics", 1),    // 没被搜空的抗生素
                    LootItem.Material("first_aid_kit", 1),  // 一只完整的急救包
                },
                VillageClinicTitle, VillageClinicNarrative),

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

    // —— 广播台（draft 待用户改；只写普通物资氛围，主线「发出设备」的取得叙事归 RadioMainline）——
    private const string BroadcastBreakRoomTitle = "值班室茶水间";

    private const string BroadcastBreakRoomNarrative =
        "进门不远是值班人员的茶水间，断电前显然还有人守在这儿——桌上摊着没喝完的水、几盒受潮的口粮，" +
        "抽屉里塞着台里常备的绷带和一只没拆封的急救包。\n\n" +
        "墙上的排班表停在了某一天，往后的格子全是空的。";

    private const string BroadcastPartsStoreTitle = "备件仓库";

    private const string BroadcastPartsStoreNarrative =
        "顺着走廊往里，是广播设备的备件仓库。大件的发射组件早被拆走或砸烂，" +
        "货架上却还剩不少零碎：一盒备用的电子元件、几卷粗细不一的信号线材、两桶给备用发电机预留的燃油。\n\n" +
        "被掀翻的机架散在地上，撬下几块结实的碎金属带走。";

    // —— 守林人小屋（小点样板；draft 待用户改；只写日常物资氛围，哥顿上吊尸+日记B 的叙事归 GoldfingerDiscovery）——
    private const string RangersCabinPantryTitle = "里屋的碗柜";

    private const string RangersCabinPantryNarrative =
        "外屋一道虚掩的内门通向里屋——一个没有窗的暗间，守林人大概拿它当储藏。" +
        "墙角的碗柜里，还码着几听没胀气的罐头，抽屉底下压着一小卷干净的绷带。\n\n" +
        "东西不多，看得出这屋子的主人独自过活，日子过得紧巴。";

    private const string RangersCabinShedTitle = "后院的柴房";

    private const string RangersCabinShedNarrative =
        "绕到屋后，紧挨着那棵老树是一间半塌的柴房。" +
        "劈好的木料还码得整整齐齐，墙上挂着一卷修屋用的麻绳，地上散着几枚生锈的铁钉。\n\n" +
        "斧子不见了——大概是主人最后用它做了别的事。";

    // —— 南林村庄（大点；draft 待用户改；只写日常物资氛围，救援锁屋的剧情归 VillageRescue）——
    private const string VillageRoadsideCarTitle = "村口的皮卡";

    private const string VillageRoadsideCarNarrative =
        "村口斜停着一辆爆了胎的皮卡，车门大敞——逃难的人走得急，后备箱却没来得及搬空。" +
        "翻出几包还没受潮的干粮，撬下几块车壳的碎金属。\n\n" +
        "油箱里晃荡着一点余油，用管子接了出来。";

    private const string VillageKitchenTitle = "农家的厨房";

    private const string VillageKitchenNarrative =
        "推开头一户人家的门，灶台还是凉的。碗柜里码着几听没胀气的罐头、一小袋干货，" +
        "抽屉里塞着几块洗得发白的旧桌布。\n\n" +
        "墙上的全家福蒙了灰，玻璃裂了道缝。";

    private const string VillageWardrobeTitle = "卧室的衣柜";

    private const string VillageWardrobeNarrative =
        "里屋的衣柜半开着，挂着几件还能穿的旧衣裳，叠得整整齐齐——像是主人临走前还想着回来。" +
        "扯下几件厚实的，又撕了些能当绷带、引火的碎布。\n\n" +
        "床头柜上摆着一张没带走的照片。";

    private const string VillageBackRoomTitle = "储藏间的木箱";

    private const string VillageBackRoomNarrative =
        "屋子最里头是间堆杂物的储藏间，角落一只木箱压在麻袋底下。" +
        "箱里还剩点没搬空的存粮，混着一把铁钉和一卷没用完的麻绳。\n\n" +
        "灰尘厚得能写字，看得出很久没人动过。";

    private const string VillageShopShelfTitle = "村口小卖部";

    private const string VillageShopShelfNarrative =
        "村里唯一一间小卖部，货架被抢过一轮，横七竖八地倒着。" +
        "弯下腰在缝里翻，还能捡出几瓶滚到墙根的水、没被踩烂的干粮，" +
        "柜台后头的抽屉里压着一叠常备的创可贴和绷带。\n\n" +
        "收银台的抽屉大开着，里头的零钱一分不剩。";

    private const string VillageWellToolboxTitle = "水井旁的工具箱";

    private const string VillageWellToolboxNarrative =
        "村子中央一口老水井，井台边搁着户人家修井留下的工具箱。" +
        "大件的家伙没了，只剩些零碎——一把铁钉、几段木料、一卷生了锈的铁丝。\n\n" +
        "井绳还在，井里却早没了打水的人。";

    private const string VillageToolShedTitle = "村尾的农具棚";

    private const string VillageToolShedNarrative =
        "村子尽头一间敞口的农具棚，藏在几棵歪脖子树后头，不走到近前不容易发现。" +
        "棚里成堆的木料还没沤烂，捆农具的粗绳、一盒散开的铁钉都还能用。\n\n" +
        "锄头镰刀之类的铁器早不见了——不知是搬走了，还是被谁拿去当了别的。";

    private const string VillageShrineTitle = "村头的祠堂";

    private const string VillageShrineNarrative =
        "村头一间旧祠堂，藏在最深处，门虚掩着。供桌上还摆着没腐坏的供品和一小袋存粮——" +
        "灾变来时，大概有人在这儿求过最后一回平安。\n\n" +
        "案上几件祭祀的骨器还在，收了能派上别的用场。神龛后蒙着厚灰，再没人来上香。";

    private const string VillageClinicTitle = "村卫生所";

    private const string VillageClinicNarrative =
        "村尾一间挂着褪色红十字牌的卫生所，是村里唯一像样的医处，藏在巷子最里头。" +
        "药柜被翻过，但没搜干净——玻璃门后还剩两卷绷带、一小板没过期的抗生素，" +
        "抽屉底压着一只没拆封的急救包。\n\n" +
        "诊桌上的登记本停在灾变那几天，最后几行字迹潦草得几乎认不出。";
}
