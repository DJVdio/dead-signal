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
    // 南林村庄（**大点**，[SPEC-B12] 大=30+ 硬口径）：一个空间分区的聚落，铺 30 处搜刮点（救援锁屋另由 VillageRescue 管、不计物资完成度）。
    //   分区：村口/杂物(3) · 民居区(9) · 村中心(6) · 村尾/藏深(6) · 后山(3) · 河滩(3)。近入口在前、藏深在后。原 9 点见下，[SPEC-B12] 扩容 21 点见后半段常量。
    public const string VillageRoadsideCarId = "cache_village_roadside_car";     // 村口·废弃皮卡后备箱（近）
    public const string VillageKitchenId = "cache_village_kitchen";              // 民居·厨房碗柜
    public const string VillageWardrobeId = "cache_village_wardrobe";            // 民居·卧室衣柜
    public const string VillageBackRoomId = "cache_village_back_room";           // 民居·储藏间木箱
    public const string VillageShopShelfId = "cache_village_shop_shelf";         // 村中心·小卖部货架
    public const string VillageWellToolboxId = "cache_village_well_toolbox";     // 村中心·水井旁工具箱
    public const string VillageToolShedId = "cache_village_tool_shed";           // 村尾·农具棚（深）
    public const string VillageShrineId = "cache_village_shrine";                // 村尾·祠堂供桌（深）
    public const string VillageClinicId = "cache_village_clinic";                // 村尾·卫生所药柜（深）

    // ==== [SPEC-B12] 配额扩容新增搜刮点 id（各图扩到三级配额带下限；单点掉落调薄见 Resolve）====
    // 守林人小屋（小点 2→5，band 5~10 下限；密度克制、不破坏"内容很少"氛围）：阁楼/床底/门廊。
    public const string RangersCabinAtticId = "cache_rangers_cabin_attic";
    public const string RangersCabinUnderbedId = "cache_rangers_cabin_underbed";
    public const string RangersCabinPorchId = "cache_rangers_cabin_porch";
    // 河边小屋（小点 2→5）：灶膛橱柜/屋檐渔具/屋后菜窖。
    public const string RiversideHearthId = "cache_riverside_hearth";
    public const string RiversideFishingId = "cache_riverside_fishing";
    public const string RiversideCellarId = "cache_riverside_cellar";
    // 城市之巅瞭望观景台（小点 2→5）：自动贩卖机/员工储物柜/天台机房。
    public const string LookoutVendingId = "cache_lookout_vending";
    public const string LookoutStaffLockerId = "cache_lookout_staff_locker";
    public const string LookoutMachineRoomId = "cache_lookout_machine_room";
    // 联合收割机仓库（中点 2→10，band 10~30 下限）：工业材料为主，食物/医疗极少。
    public const string WarehouseWorkbenchId = "cache_warehouse_workbench";
    public const string WarehousePartsBinId = "cache_warehouse_parts_bin";
    public const string WarehouseFuelDrumId = "cache_warehouse_fuel_drum";
    public const string WarehouseHayLoftId = "cache_warehouse_hayloft";
    public const string WarehouseBreakCornerId = "cache_warehouse_break_corner";
    public const string WarehouseScrapPileId = "cache_warehouse_scrap_pile";
    public const string WarehouseCombineCabId = "cache_warehouse_combine_cab";
    public const string WarehouseLumberRackId = "cache_warehouse_lumber_rack";
    // 广播台（中点 2→10）：电子/线材/燃油 + 办公区，食物/医疗集中在食堂/更衣室各一处。
    public const string BroadcastOfficeId = "cache_broadcast_office";
    public const string BroadcastArchiveId = "cache_broadcast_archive";
    public const string BroadcastGeneratorId = "cache_broadcast_generator";
    public const string BroadcastLockersId = "cache_broadcast_lockers";
    public const string BroadcastCanteenId = "cache_broadcast_canteen";
    public const string BroadcastServerRackId = "cache_broadcast_server_rack";
    public const string BroadcastRoofAntennaId = "cache_broadcast_roof_antenna";
    public const string BroadcastStoreroomId = "cache_broadcast_storeroom";
    // 南林村庄（大点 9→30，band 30+ 硬口径）：既有四分区加密 + 新增后山/河滩两分区；单点调薄、食物医疗分散控量。
    //   民居区加密(6)：
    public const string VillageBedroom2Id = "cache_village_bedroom2";
    public const string VillageLoftId = "cache_village_loft";
    public const string VillageCourtyardId = "cache_village_courtyard";
    public const string VillageCoopId = "cache_village_coop";
    public const string VillagePantry2Id = "cache_village_pantry2";
    public const string VillageWoodpileId = "cache_village_woodpile";
    //   村中心加密(4)：
    public const string VillageCoopStoreId = "cache_village_coop_store";
    public const string VillageSchoolId = "cache_village_school";
    public const string VillageForgeId = "cache_village_forge";
    public const string VillageBusStopId = "cache_village_bus_stop";
    //   村尾加密(3)：
    public const string VillageBarnId = "cache_village_barn";
    public const string VillageGraveHutId = "cache_village_grave_hut";
    public const string VillageBeehiveId = "cache_village_beehive";
    //   村口加密(2)：
    public const string VillageGatePostId = "cache_village_gate_post";
    public const string VillageTrikeId = "cache_village_trike";
    //   新分区·后山(3)（藏深，山洞为医疗深藏点）：
    public const string VillageBackhillBlindId = "cache_village_backhill_blind";
    public const string VillageBackhillKilnId = "cache_village_backhill_kiln";
    public const string VillageBackhillCaveId = "cache_village_backhill_cave";
    //   新分区·河滩(3)：
    public const string VillageRiverbankBoatId = "cache_village_riverbank_boat";
    public const string VillageRiverbankShackId = "cache_village_riverbank_shack";
    public const string VillageRiverbankPumpId = "cache_village_riverbank_pump";

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
    // [SPEC-B12] 扩容新增点的一次性 flag（与上方 id 一一对应）：
    public const string RangersCabinAtticFlag = "searched_rangers_cabin_attic";
    public const string RangersCabinUnderbedFlag = "searched_rangers_cabin_underbed";
    public const string RangersCabinPorchFlag = "searched_rangers_cabin_porch";
    public const string RiversideHearthFlag = "searched_riverside_hearth";
    public const string RiversideFishingFlag = "searched_riverside_fishing";
    public const string RiversideCellarFlag = "searched_riverside_cellar";
    public const string LookoutVendingFlag = "searched_lookout_vending";
    public const string LookoutStaffLockerFlag = "searched_lookout_staff_locker";
    public const string LookoutMachineRoomFlag = "searched_lookout_machine_room";
    public const string WarehouseWorkbenchFlag = "searched_warehouse_workbench";
    public const string WarehousePartsBinFlag = "searched_warehouse_parts_bin";
    public const string WarehouseFuelDrumFlag = "searched_warehouse_fuel_drum";
    public const string WarehouseHayLoftFlag = "searched_warehouse_hayloft";
    public const string WarehouseBreakCornerFlag = "searched_warehouse_break_corner";
    public const string WarehouseScrapPileFlag = "searched_warehouse_scrap_pile";
    public const string WarehouseCombineCabFlag = "searched_warehouse_combine_cab";
    public const string WarehouseLumberRackFlag = "searched_warehouse_lumber_rack";
    public const string BroadcastOfficeFlag = "searched_broadcast_office";
    public const string BroadcastArchiveFlag = "searched_broadcast_archive";
    public const string BroadcastGeneratorFlag = "searched_broadcast_generator";
    public const string BroadcastLockersFlag = "searched_broadcast_lockers";
    public const string BroadcastCanteenFlag = "searched_broadcast_canteen";
    public const string BroadcastServerRackFlag = "searched_broadcast_server_rack";
    public const string BroadcastRoofAntennaFlag = "searched_broadcast_roof_antenna";
    public const string BroadcastStoreroomFlag = "searched_broadcast_storeroom";
    public const string VillageBedroom2Flag = "searched_village_bedroom2";
    public const string VillageLoftFlag = "searched_village_loft";
    public const string VillageCourtyardFlag = "searched_village_courtyard";
    public const string VillageCoopFlag = "searched_village_coop";
    public const string VillagePantry2Flag = "searched_village_pantry2";
    public const string VillageWoodpileFlag = "searched_village_woodpile";
    public const string VillageCoopStoreFlag = "searched_village_coop_store";
    public const string VillageSchoolFlag = "searched_village_school";
    public const string VillageForgeFlag = "searched_village_forge";
    public const string VillageBusStopFlag = "searched_village_bus_stop";
    public const string VillageBarnFlag = "searched_village_barn";
    public const string VillageGraveHutFlag = "searched_village_grave_hut";
    public const string VillageBeehiveFlag = "searched_village_beehive";
    public const string VillageGatePostFlag = "searched_village_gate_post";
    public const string VillageTrikeFlag = "searched_village_trike";
    public const string VillageBackhillBlindFlag = "searched_village_backhill_blind";
    public const string VillageBackhillKilnFlag = "searched_village_backhill_kiln";
    public const string VillageBackhillCaveFlag = "searched_village_backhill_cave";
    public const string VillageRiverbankBoatFlag = "searched_village_riverbank_boat";
    public const string VillageRiverbankShackFlag = "searched_village_riverbank_shack";
    public const string VillageRiverbankPumpFlag = "searched_village_riverbank_pump";

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
        // 河边小屋（小点，5 处）：枪柜(近)→灶膛→渔具→床底(深)→菜窖(深)。
        RiversideCabinName => new[]
        {
            RiversideGunCabinetId, RiversideHearthId, RiversideFishingId,
            RiversideBedChestId, RiversideCellarId,
        },
        // 联合收割机仓库（中点，10 处）：工具柜(近)→…→阁楼铁皮箱(最深←书)。
        HarvesterWarehouseName => new[]
        {
            WarehouseToolCabinetId, WarehouseWorkbenchId, WarehouseBreakCornerId,
            WarehousePartsBinId, WarehouseFuelDrumId, WarehouseLumberRackId,
            WarehouseHayLoftId, WarehouseScrapPileId, WarehouseCombineCabId,
            WarehouseAtticChestId,
        },
        // 望远镜发现点(尸潮剧情)不在此列——那是 LookoutSighting 管的置旗标+叙事，非物资搜刮。
        // 城市之巅瞭望观景台（小点，5 处）：游客服务台(近)→贩卖机→员工储物柜→天台机房(深)→值班室(最深)。
        CityRooftopLookoutName => new[]
        {
            LookoutGiftShopId, LookoutVendingId, LookoutStaffLockerId,
            LookoutMachineRoomId, LookoutWardensRoomId,
        },
        // 发出设备定点(TransmitterDiscoveryId)不在此列——那是 RadioMainline 管的取设备+推进状态，非物资搜刮。
        // 广播台（中点，10 处）：茶水间(近)→…→备件仓库(最深)。
        BroadcastStationName => new[]
        {
            BroadcastBreakRoomId, BroadcastOfficeId, BroadcastLockersId,
            BroadcastCanteenId, BroadcastStoreroomId, BroadcastGeneratorId,
            BroadcastServerRackId, BroadcastArchiveId, BroadcastRoofAntennaId,
            BroadcastPartsStoreId,
        },
        // 守林人小屋（小点，5 处；密度克制不破坏"内容很少"氛围）：里屋碗柜(近)→床底→门廊→阁楼(深)→后院柴房(深)。哥顿上吊尸+日记B 不在此列（GoldfingerDiscovery 管）。
        WatchersCabinName => new[]
        {
            RangersCabinPantryId, RangersCabinUnderbedId, RangersCabinPorchId,
            RangersCabinAtticId, RangersCabinShedId,
        },
        // 南林村庄（大点，30 处，[SPEC-B12] 大=30+ 硬口径）：近入口→藏深，村口→民居→村中心→村尾→后山→河滩。救援锁屋(VillageRescue)不在此列——主线入队触发点，不计物资完成度。
        VillageRescue.DestinationName => new[]
        {
            // 村口/杂物(3)
            VillageRoadsideCarId, VillageGatePostId, VillageTrikeId,
            // 民居区(9)
            VillageKitchenId, VillageWardrobeId, VillageBedroom2Id, VillageCourtyardId,
            VillageCoopId, VillagePantry2Id, VillageLoftId, VillageWoodpileId, VillageBackRoomId,
            // 村中心(6)
            VillageShopShelfId, VillageCoopStoreId, VillageBusStopId, VillageSchoolId,
            VillageWellToolboxId, VillageForgeId,
            // 村尾/藏深(6)
            VillageToolShedId, VillageBarnId, VillageBeehiveId, VillageGraveHutId,
            VillageShrineId, VillageClinicId,
            // 后山(3, 山洞医疗深藏)
            VillageBackhillBlindId, VillageBackhillKilnId, VillageBackhillCaveId,
            // 河滩(3)
            VillageRiverbankBoatId, VillageRiverbankShackId, VillageRiverbankPumpId,
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
        // [SPEC-B12] 扩容新增点：
        RangersCabinAtticId => RangersCabinAtticFlag,
        RangersCabinUnderbedId => RangersCabinUnderbedFlag,
        RangersCabinPorchId => RangersCabinPorchFlag,
        RiversideHearthId => RiversideHearthFlag,
        RiversideFishingId => RiversideFishingFlag,
        RiversideCellarId => RiversideCellarFlag,
        LookoutVendingId => LookoutVendingFlag,
        LookoutStaffLockerId => LookoutStaffLockerFlag,
        LookoutMachineRoomId => LookoutMachineRoomFlag,
        WarehouseWorkbenchId => WarehouseWorkbenchFlag,
        WarehousePartsBinId => WarehousePartsBinFlag,
        WarehouseFuelDrumId => WarehouseFuelDrumFlag,
        WarehouseHayLoftId => WarehouseHayLoftFlag,
        WarehouseBreakCornerId => WarehouseBreakCornerFlag,
        WarehouseScrapPileId => WarehouseScrapPileFlag,
        WarehouseCombineCabId => WarehouseCombineCabFlag,
        WarehouseLumberRackId => WarehouseLumberRackFlag,
        BroadcastOfficeId => BroadcastOfficeFlag,
        BroadcastArchiveId => BroadcastArchiveFlag,
        BroadcastGeneratorId => BroadcastGeneratorFlag,
        BroadcastLockersId => BroadcastLockersFlag,
        BroadcastCanteenId => BroadcastCanteenFlag,
        BroadcastServerRackId => BroadcastServerRackFlag,
        BroadcastRoofAntennaId => BroadcastRoofAntennaFlag,
        BroadcastStoreroomId => BroadcastStoreroomFlag,
        VillageBedroom2Id => VillageBedroom2Flag,
        VillageLoftId => VillageLoftFlag,
        VillageCourtyardId => VillageCourtyardFlag,
        VillageCoopId => VillageCoopFlag,
        VillagePantry2Id => VillagePantry2Flag,
        VillageWoodpileId => VillageWoodpileFlag,
        VillageCoopStoreId => VillageCoopStoreFlag,
        VillageSchoolId => VillageSchoolFlag,
        VillageForgeId => VillageForgeFlag,
        VillageBusStopId => VillageBusStopFlag,
        VillageBarnId => VillageBarnFlag,
        VillageGraveHutId => VillageGraveHutFlag,
        VillageBeehiveId => VillageBeehiveFlag,
        VillageGatePostId => VillageGatePostFlag,
        VillageTrikeId => VillageTrikeFlag,
        VillageBackhillBlindId => VillageBackhillBlindFlag,
        VillageBackhillKilnId => VillageBackhillKilnFlag,
        VillageBackhillCaveId => VillageBackhillCaveFlag,
        VillageRiverbankBoatId => VillageRiverbankBoatFlag,
        VillageRiverbankShackId => VillageRiverbankShackFlag,
        VillageRiverbankPumpId => VillageRiverbankPumpFlag,
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

            // —— [SPEC-B12] 守林人小屋补 3 处（小点，量级极克制：新增仅 1 处医疗小物）——
            RangersCabinAtticId when NotYet(flags, RangersCabinAtticFlag) => new CacheResult(
                RangersCabinAtticFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("wire", 1) },
                RangersCabinAtticTitle, RangersCabinAtticNarrative),

            RangersCabinUnderbedId when NotYet(flags, RangersCabinUnderbedFlag) => new CacheResult(
                RangersCabinUnderbedFlag,
                new[] { LootItem.Material("bandage", 1), LootItem.Material("scrap_metal", 1) },
                RangersCabinUnderbedTitle, RangersCabinUnderbedNarrative),

            RangersCabinPorchId when NotYet(flags, RangersCabinPorchFlag) => new CacheResult(
                RangersCabinPorchFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("wood", 1) },
                RangersCabinPorchTitle, RangersCabinPorchNarrative),

            // —— [SPEC-B12] 河边小屋补 3 处（小点）——
            RiversideHearthId when NotYet(flags, RiversideHearthFlag) => new CacheResult(
                RiversideHearthFlag,
                new[] { LootItem.Food(1) },
                RiversideHearthTitle, RiversideHearthNarrative),

            RiversideFishingId when NotYet(flags, RiversideFishingFlag) => new CacheResult(
                RiversideFishingFlag,
                new[] { LootItem.Material("rope", 1), LootItem.Material("wire", 1) },
                RiversideFishingTitle, RiversideFishingNarrative),

            RiversideCellarId when NotYet(flags, RiversideCellarFlag) => new CacheResult(
                RiversideCellarFlag,
                new[] { LootItem.Food(1), LootItem.Material("bone", 1) },
                RiversideCellarTitle, RiversideCellarNarrative),

            // —— [SPEC-B12] 瞭望台补 3 处（小点）——
            LookoutVendingId when NotYet(flags, LookoutVendingFlag) => new CacheResult(
                LookoutVendingFlag,
                new[] { LootItem.Food(1) },
                LookoutVendingTitle, LookoutVendingNarrative),

            LookoutStaffLockerId when NotYet(flags, LookoutStaffLockerFlag) => new CacheResult(
                LookoutStaffLockerFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("bandage", 1) },
                LookoutStaffLockerTitle, LookoutStaffLockerNarrative),

            LookoutMachineRoomId when NotYet(flags, LookoutMachineRoomFlag) => new CacheResult(
                LookoutMachineRoomFlag,
                new[] { LootItem.Material("components", 1), LootItem.Material("wire", 1), LootItem.Material("fuel", 1) },
                LookoutMachineRoomTitle, LookoutMachineRoomNarrative),

            // —— [SPEC-B12] 联合收割机仓库补 8 处（中点；工业材料为主，食物/医疗仅休息角 1 处）——
            WarehouseWorkbenchId when NotYet(flags, WarehouseWorkbenchFlag) => new CacheResult(
                WarehouseWorkbenchFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("wood", 1) },
                WarehouseWorkbenchTitle, WarehouseWorkbenchNarrative),

            WarehousePartsBinId when NotYet(flags, WarehousePartsBinFlag) => new CacheResult(
                WarehousePartsBinFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("components", 1) },
                WarehousePartsBinTitle, WarehousePartsBinNarrative),

            WarehouseFuelDrumId when NotYet(flags, WarehouseFuelDrumFlag) => new CacheResult(
                WarehouseFuelDrumFlag,
                new[] { LootItem.Material("fuel", 2) },
                WarehouseFuelDrumTitle, WarehouseFuelDrumNarrative),

            WarehouseHayLoftId when NotYet(flags, WarehouseHayLoftFlag) => new CacheResult(
                WarehouseHayLoftFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("rope", 1) },
                WarehouseHayLoftTitle, WarehouseHayLoftNarrative),

            WarehouseBreakCornerId when NotYet(flags, WarehouseBreakCornerFlag) => new CacheResult(
                WarehouseBreakCornerFlag,
                new[] { LootItem.Food(1), LootItem.Material("bandage", 1) },
                WarehouseBreakCornerTitle, WarehouseBreakCornerNarrative),

            WarehouseScrapPileId when NotYet(flags, WarehouseScrapPileFlag) => new CacheResult(
                WarehouseScrapPileFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("wire", 1) },
                WarehouseScrapPileTitle, WarehouseScrapPileNarrative),

            WarehouseCombineCabId when NotYet(flags, WarehouseCombineCabFlag) => new CacheResult(
                WarehouseCombineCabFlag,
                new[] { LootItem.Material("components", 1), LootItem.Material("fuel", 1) },
                WarehouseCombineCabTitle, WarehouseCombineCabNarrative),

            WarehouseLumberRackId when NotYet(flags, WarehouseLumberRackFlag) => new CacheResult(
                WarehouseLumberRackFlag,
                new[] { LootItem.Material("wood", 3), LootItem.Material("nails", 1) },
                WarehouseLumberRackTitle, WarehouseLumberRackNarrative),

            // —— [SPEC-B12] 广播台补 8 处（中点；电子/线材为主，食物仅食堂 1、医疗仅更衣室 1）——
            BroadcastOfficeId when NotYet(flags, BroadcastOfficeFlag) => new CacheResult(
                BroadcastOfficeFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("components", 1) },
                BroadcastOfficeTitle, BroadcastOfficeNarrative),

            BroadcastArchiveId when NotYet(flags, BroadcastArchiveFlag) => new CacheResult(
                BroadcastArchiveFlag,
                new[] { LootItem.Material("cloth", 2), LootItem.Material("wire", 1) },
                BroadcastArchiveTitle, BroadcastArchiveNarrative),

            BroadcastGeneratorId when NotYet(flags, BroadcastGeneratorFlag) => new CacheResult(
                BroadcastGeneratorFlag,
                new[] { LootItem.Material("fuel", 2), LootItem.Material("components", 1) },
                BroadcastGeneratorTitle, BroadcastGeneratorNarrative),

            BroadcastLockersId when NotYet(flags, BroadcastLockersFlag) => new CacheResult(
                BroadcastLockersFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("bandage", 1) },
                BroadcastLockersTitle, BroadcastLockersNarrative),

            BroadcastCanteenId when NotYet(flags, BroadcastCanteenFlag) => new CacheResult(
                BroadcastCanteenFlag,
                new[] { LootItem.Food(2) },
                BroadcastCanteenTitle, BroadcastCanteenNarrative),

            BroadcastServerRackId when NotYet(flags, BroadcastServerRackFlag) => new CacheResult(
                BroadcastServerRackFlag,
                new[] { LootItem.Material("components", 2), LootItem.Material("wire", 2) },
                BroadcastServerRackTitle, BroadcastServerRackNarrative),

            BroadcastRoofAntennaId when NotYet(flags, BroadcastRoofAntennaFlag) => new CacheResult(
                BroadcastRoofAntennaFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("wire", 1) },
                BroadcastRoofAntennaTitle, BroadcastRoofAntennaNarrative),

            BroadcastStoreroomId when NotYet(flags, BroadcastStoreroomFlag) => new CacheResult(
                BroadcastStoreroomFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("rope", 1), LootItem.Material("scrap_metal", 1) },
                BroadcastStoreroomTitle, BroadcastStoreroomNarrative),

            // —— [SPEC-B12] 南林村庄补 21 处（大点 30；单点调薄，食物散布 7 处、医疗集中候车棚 1+后山洞深藏 1）——
            // 村口(2)
            VillageGatePostId when NotYet(flags, VillageGatePostFlag) => new CacheResult(
                VillageGatePostFlag,
                new[] { LootItem.Material("scrap_metal", 1), LootItem.Material("wire", 1) },
                VillageGatePostTitle, VillageGatePostNarrative),

            VillageTrikeId when NotYet(flags, VillageTrikeFlag) => new CacheResult(
                VillageTrikeFlag,
                new[] { LootItem.Material("scrap_metal", 1), LootItem.Material("fuel", 1) },
                VillageTrikeTitle, VillageTrikeNarrative),

            // 民居(6)
            VillageBedroom2Id when NotYet(flags, VillageBedroom2Flag) => new CacheResult(
                VillageBedroom2Flag,
                new[] { LootItem.Material("cloth", 1) },
                VillageBedroom2Title, VillageBedroom2Narrative),

            VillageCourtyardId when NotYet(flags, VillageCourtyardFlag) => new CacheResult(
                VillageCourtyardFlag,
                new[] { LootItem.Food(1) },
                VillageCourtyardTitle, VillageCourtyardNarrative),

            VillageCoopId when NotYet(flags, VillageCoopFlag) => new CacheResult(
                VillageCoopFlag,
                new[] { LootItem.Food(1), LootItem.Material("bone", 1) },
                VillageCoopTitle, VillageCoopNarrative),

            VillagePantry2Id when NotYet(flags, VillagePantry2Flag) => new CacheResult(
                VillagePantry2Flag,
                new[] { LootItem.Food(1) },
                VillagePantry2Title, VillagePantry2Narrative),

            VillageLoftId when NotYet(flags, VillageLoftFlag) => new CacheResult(
                VillageLoftFlag,
                new[] { LootItem.Material("scrap_cloth", 1), LootItem.Material("wood", 1) },
                VillageLoftTitle, VillageLoftNarrative),

            VillageWoodpileId when NotYet(flags, VillageWoodpileFlag) => new CacheResult(
                VillageWoodpileFlag,
                new[] { LootItem.Material("wood", 2), LootItem.Material("rope", 1) },
                VillageWoodpileTitle, VillageWoodpileNarrative),

            // 村中心(4)
            VillageCoopStoreId when NotYet(flags, VillageCoopStoreFlag) => new CacheResult(
                VillageCoopStoreFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("cloth", 1) },
                VillageCoopStoreTitle, VillageCoopStoreNarrative),

            VillageSchoolId when NotYet(flags, VillageSchoolFlag) => new CacheResult(
                VillageSchoolFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("wire", 1) },
                VillageSchoolTitle, VillageSchoolNarrative),

            VillageForgeId when NotYet(flags, VillageForgeFlag) => new CacheResult(
                VillageForgeFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("nails", 2) },
                VillageForgeTitle, VillageForgeNarrative),

            VillageBusStopId when NotYet(flags, VillageBusStopFlag) => new CacheResult(
                VillageBusStopFlag,
                new[] { LootItem.Food(1), LootItem.Material("bandage", 1) },
                VillageBusStopTitle, VillageBusStopNarrative),

            // 村尾(3)
            VillageBarnId when NotYet(flags, VillageBarnFlag) => new CacheResult(
                VillageBarnFlag,
                new[] { LootItem.Food(1), LootItem.Material("wood", 1) },
                VillageBarnTitle, VillageBarnNarrative),

            VillageGraveHutId when NotYet(flags, VillageGraveHutFlag) => new CacheResult(
                VillageGraveHutFlag,
                new[] { LootItem.Material("nails", 1), LootItem.Material("rope", 1) },
                VillageGraveHutTitle, VillageGraveHutNarrative),

            VillageBeehiveId when NotYet(flags, VillageBeehiveFlag) => new CacheResult(
                VillageBeehiveFlag,
                new[] { LootItem.Food(1) },
                VillageBeehiveTitle, VillageBeehiveNarrative),

            // 后山(3, 山洞暗格＝医疗深藏奖励)
            VillageBackhillBlindId when NotYet(flags, VillageBackhillBlindFlag) => new CacheResult(
                VillageBackhillBlindFlag,
                new[] { LootItem.Material("bone", 2), LootItem.Material("rope", 1) },
                VillageBackhillBlindTitle, VillageBackhillBlindNarrative),

            VillageBackhillKilnId when NotYet(flags, VillageBackhillKilnFlag) => new CacheResult(
                VillageBackhillKilnFlag,
                new[] { LootItem.Material("wood", 2), LootItem.Material("fuel", 1) },
                VillageBackhillKilnTitle, VillageBackhillKilnNarrative),

            VillageBackhillCaveId when NotYet(flags, VillageBackhillCaveFlag) => new CacheResult(
                VillageBackhillCaveFlag,
                new[] { LootItem.Material("antibiotics", 1), LootItem.Material("first_aid_kit", 1) },
                VillageBackhillCaveTitle, VillageBackhillCaveNarrative),

            // 河滩(3)
            VillageRiverbankBoatId when NotYet(flags, VillageRiverbankBoatFlag) => new CacheResult(
                VillageRiverbankBoatFlag,
                new[] { LootItem.Material("rope", 1), LootItem.Material("scrap_metal", 1) },
                VillageRiverbankBoatTitle, VillageRiverbankBoatNarrative),

            VillageRiverbankShackId when NotYet(flags, VillageRiverbankShackFlag) => new CacheResult(
                VillageRiverbankShackFlag,
                new[] { LootItem.Food(1), LootItem.Material("bone", 1) },
                VillageRiverbankShackTitle, VillageRiverbankShackNarrative),

            VillageRiverbankPumpId when NotYet(flags, VillageRiverbankPumpFlag) => new CacheResult(
                VillageRiverbankPumpFlag,
                new[] { LootItem.Material("components", 1), LootItem.Material("fuel", 1), LootItem.Material("wire", 1) },
                VillageRiverbankPumpTitle, VillageRiverbankPumpNarrative),

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

    // ==== [SPEC-B12] 配额扩容新增点的环境叙事 draft（一句话级草稿，末日日常氛围；全文待用户验收/优化）====

    // —— 守林人小屋补点 ——
    private const string RangersCabinAtticTitle = "阁楼杂物";
    private const string RangersCabinAtticNarrative = "钻进低矮的阁楼，积灰的木箱里翻出些叠好的旧布和一小卷铁丝。";
    private const string RangersCabinUnderbedTitle = "床底铁盒";
    private const string RangersCabinUnderbedNarrative = "床板底下拖出个上了锁的铁盒，撬开只有一卷绷带和几块用剩的碎铁。";
    private const string RangersCabinPorchTitle = "门廊工具架";
    private const string RangersCabinPorchNarrative = "门廊角落的工具架上还挂着几枚钉子、一截没用完的木料。";

    // —— 河边小屋补点 ——
    private const string RiversideHearthTitle = "灶膛橱柜";
    private const string RiversideHearthNarrative = "灶台边的橱柜里，还剩一小袋没受潮的干粮。";
    private const string RiversideFishingTitle = "屋檐渔具箱";
    private const string RiversideFishingNarrative = "屋檐下挂着猎人的渔具箱，成卷的鱼线和钩绳还能拆下来用。";
    private const string RiversideCellarTitle = "屋后菜窖";
    private const string RiversideCellarNarrative = "屋后半塌的菜窖里，腌菜坛底压着点存粮，墙角还堆着几根处理干净的兽骨。";

    // —— 瞭望台补点 ——
    private const string LookoutVendingTitle = "自动贩卖机";
    private const string LookoutVendingNarrative = "观景层的自动贩卖机被撬过，玻璃后头还卡着两瓶饮料、一包没掉出来的零食。";
    private const string LookoutStaffLockerTitle = "员工储物柜";
    private const string LookoutStaffLockerNarrative = "员工休息室的储物柜里挂着景区制服，夹层里塞着一小包创可贴。";
    private const string LookoutMachineRoomTitle = "天台机房";
    private const string LookoutMachineRoomNarrative = "天台机房堆着通风和信号设备的零件，拆下些电子件、线材，还接了半桶余油。";

    // —— 联合收割机仓库补点 ——
    private const string WarehouseWorkbenchTitle = "工作台抽屉";
    private const string WarehouseWorkbenchNarrative = "工作台的抽屉没关严，里头散着一把铁钉和一截刨好的木料。";
    private const string WarehousePartsBinTitle = "零件料架";
    private const string WarehousePartsBinNarrative = "靠墙的零件料架翻倒了，捡出几块农机拆下的碎金属和一小盒电子元件。";
    private const string WarehouseFuelDrumTitle = "油料桶区";
    private const string WarehouseFuelDrumNarrative = "仓库尽头码着几只油桶，晃了晃还有响，接出两桶柴油。";
    private const string WarehouseHayLoftTitle = "草料阁";
    private const string WarehouseHayLoftNarrative = "草料阁堆着发霉的干草，翻出几块盖粮的旧帆布和一卷捆草绳。";
    private const string WarehouseBreakCornerTitle = "工人休息角";
    private const string WarehouseBreakCornerNarrative = "机棚一角是工人歇脚的地方，桌上剩着没吃完的干粮，抽屉里有一卷绷带。";
    private const string WarehouseScrapPileTitle = "废铁堆";
    private const string WarehouseScrapPileNarrative = "墙根堆着报废农具的废铁，扒拉出几块结实的碎金属和一段电线。";
    private const string WarehouseCombineCabTitle = "收割机驾驶室";
    private const string WarehouseCombineCabNarrative = "钻进那台大收割机的驾驶室，仪表盘后拆出些电子件，油箱里还剩一点余油。";
    private const string WarehouseLumberRackTitle = "木料架";
    private const string WarehouseLumberRackNarrative = "靠里的木料架上码着整齐的板材，抱下几根，顺手扫了盒散钉。";

    // —— 广播台补点 ——
    private const string BroadcastOfficeTitle = "台长办公室";
    private const string BroadcastOfficeNarrative = "台长办公室的抽屉被翻乱了，剩下一块窗帘布和一小盒备用电子元件。";
    private const string BroadcastArchiveTitle = "资料室";
    private const string BroadcastArchiveNarrative = "资料室的架子上尽是发霉的磁带和文件，扯下几幅遮尘布，顺走一卷捆线。";
    private const string BroadcastGeneratorTitle = "发电机房";
    private const string BroadcastGeneratorNarrative = "地下发电机房还留着两桶备用柴油，控制柜里拆出块电子件。";
    private const string BroadcastLockersTitle = "员工更衣室";
    private const string BroadcastLockersNarrative = "更衣室的铁皮柜里挂着工装，最上层压着一小盒常备的绷带。";
    private const string BroadcastCanteenTitle = "食堂后厨";
    private const string BroadcastCanteenNarrative = "台里的小食堂后厨，米面早霉了，冷库门后倒还留着几听没胀气的罐头。";
    private const string BroadcastServerRackTitle = "机架间";
    private const string BroadcastServerRackNarrative = "满墙的机架被拆过一轮，拆板间还能撬下几块电子件、几卷成盘的信号线。";
    private const string BroadcastRoofAntennaTitle = "屋顶天线基座";
    private const string BroadcastRoofAntennaNarrative = "爬上屋顶，天线基座锈成一片，敲下几块结实的碎金属和一段拉线。";
    private const string BroadcastStoreroomTitle = "杂物储藏间";
    private const string BroadcastStoreroomNarrative = "走廊尽头的储藏间堆着杂物，翻出一把铁钉、一卷麻绳，还有块能回炉的碎铁。";

    // —— 南林村庄补点（村口/民居/村中心/村尾/后山/河滩）——
    private const string VillageGatePostTitle = "村口岗亭";
    private const string VillageGatePostNarrative = "村口一间搭起来又废弃的岗亭，桌上撂着值守用的手电和一卷电线，都还能用。";
    private const string VillageTrikeTitle = "村口废三轮";
    private const string VillageTrikeNarrative = "路边歪着一辆爆胎的农用三轮，车斗空了，撬下几块车壳碎铁，油箱还余一点。";
    private const string VillageBedroom2Title = "民居·梳妆台";
    private const string VillageBedroom2Narrative = "另一户人家的主卧，梳妆台的抽屉里叠着几件没带走的衣裳。";
    private const string VillageCourtyardTitle = "民居·院子菜畦";
    private const string VillageCourtyardNarrative = "院子里的菜畦荒了，翻土时倒扒出几颗还能吃的番薯。";
    private const string VillageCoopTitle = "民居·鸡窝棚";
    private const string VillageCoopNarrative = "后院的鸡窝棚早空了，草料底下压着几枚风干的蛋，墙角散着些禽骨。";
    private const string VillagePantry2Title = "民居·灶房米缸";
    private const string VillagePantry2Narrative = "灶房那口大米缸盖得严实，缸底还剩小半袋没生虫的糙米。";
    private const string VillageLoftTitle = "民居·阁楼";
    private const string VillageLoftNarrative = "顺着木梯上阁楼，堆的尽是破烂，扯下些能引火的碎布、抽走一根房梁旧料。";
    private const string VillageWoodpileTitle = "民居·柴垛";
    private const string VillageWoodpileNarrative = "屋檐下码着过冬的柴垛，抱走几捆干柴，顺手拆下捆柴的粗绳。";
    private const string VillageCoopStoreTitle = "村中心·供销社仓";
    private const string VillageCoopStoreNarrative = "供销社后头的库房翻得七零八落，货架缝里还夹着一盒铁钉、几匹积灰的布。";
    private const string VillageSchoolTitle = "村中心·村小教室";
    private const string VillageSchoolNarrative = "村小的教室黑板还留着半句没擦的字，教具柜里剩些旧布和实验用的电线。";
    private const string VillageForgeTitle = "村中心·铁匠铺";
    private const string VillageForgeNarrative = "村口的铁匠铺炉子早凉了，砧板边散着打剩的碎铁料和一盒手打的钉子。";
    private const string VillageBusStopTitle = "村中心·候车棚";
    private const string VillageBusStopNarrative = "候车棚长椅下遗着个没人认领的行李袋，翻出点干粮和一卷急救绷带。";
    private const string VillageBarnTitle = "村尾·打谷场谷仓";
    private const string VillageBarnNarrative = "打谷场旁的谷仓门虚掩着，仓底还堆着没搬空的谷袋和几根晒谷用的木杆。";
    private const string VillageGraveHutTitle = "村尾·坟场看守屋";
    private const string VillageGraveHutNarrative = "村尾坟场边一间守墓人的矮屋，工具房里剩着几枚锈钉和一卷下棺用的粗绳。";
    private const string VillageBeehiveTitle = "村尾·养蜂棚";
    private const string VillageBeehiveNarrative = "半山脚下几只废弃的蜂箱，撬开一只，巢脾里还封着没被掏空的蜜。";
    private const string VillageBackhillBlindTitle = "后山·猎人窝棚";
    private const string VillageBackhillBlindNarrative = "后山林子里藏着个猎人守夜的窝棚，晾架上挂着处理好的兽骨，墙上盘着捕兽的绳套。";
    private const string VillageBackhillKilnTitle = "后山·炭窑";
    private const string VillageBackhillKilnNarrative = "半塌的炭窑还留着没烧完的木料，窑边一只铁皮桶里晃着引火的油。";
    private const string VillageBackhillCaveTitle = "后山·山洞暗格";
    private const string VillageBackhillCaveNarrative = "钻进后山最深处的山洞，石缝里嵌着个防潮的铁皮匣——有人把一小板抗生素和一只完整急救包藏在了这最难找的地方。";
    private const string VillageRiverbankBoatTitle = "河滩·搁浅小船";
    private const string VillageRiverbankBoatNarrative = "河滩上一条搁浅的乌篷船翻倒着，船舱里盘着缆绳，船钉能撬下几块碎铁。";
    private const string VillageRiverbankShackTitle = "河滩·晒鱼棚";
    private const string VillageRiverbankShackNarrative = "河边搭着个晒鱼的草棚，架上还挂着几条风干的鱼，脚下散着剔净的鱼骨。";
    private const string VillageRiverbankPumpTitle = "河滩·抽水泵房";
    private const string VillageRiverbankPumpNarrative = "灌溉用的抽水泵房锈迹斑斑，拆开泵机取出电机零件、一段线，油壶里还余点柴油。";
}
