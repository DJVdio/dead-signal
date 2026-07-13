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
    /// <summary>金手指帮根据地目的地名，须与 <c>WorldMapPanel.GoldfingerBaseName</c>/<c>ExplorationProgress.GoldfingerBaseName</c> 一致（本类脱 Godot 单测，故本地持有副本）。</summary>
    public const string GoldfingerBaseName = "金手指帮根据地";
    /// <summary>[SPEC-B13·拟设定待确认] 东部新村目的地名＝内部路由键「住宅区」（正名兼容，守林人小屋先例）；显示名「东部新村」由 WorldMapPanel.Destination.DisplayName 承载。</summary>
    public const string EastNewVillageName = "住宅区";
    /// <summary>[SPEC-B13·拟设定待确认] 加油站目的地名（无正名，本就叫加油站），须与 WorldMapPanel 一致。</summary>
    public const string GasStationName = "加油站";
    /// <summary>[SPEC-B13] 超市目的地名（中型，幸存者骗局据点），须与 WorldMapPanel 一致（本类脱 Godot 单测，故本地持有副本）。</summary>
    public const string SupermarketName = "超市";
    /// <summary>[SPEC-B13] 医院目的地名（大型，丧尸巢废墟·高风险高收益医疗），须与 WorldMapPanel 一致（本类脱 Godot 单测，故本地持有副本）。</summary>
    public const string HospitalName = "医院";

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
    // 金手指帮根据地（[SPEC-B12-补] 用户改口径"中型探索点·以战斗为主"：Large→Medium，铺 11 处帮派储备点）：
    //   弹药火药/碎金属/武器配件/白银/皮革布料为主，禁食物医疗灌水（医疗仅深藏头目急救箱 1 处封顶）；"打过才拿"——近入口少、gauntlet 后方与深处多。
    //   与克莉丝汀复仇线两具尸体发现点（GoldfingerDiscovery）共存：那是剧情尸体点(found_*)，id/flag 命名空间独立、互不干扰。
    public const string GoldfingerCheckpointId = "cache_goldfinger_checkpoint";     // 门口岗哨掩体（近）
    public const string GoldfingerYardWreckId = "cache_goldfinger_yard_wreck";      // 前院废车堆（近）
    public const string GoldfingerBunksId = "cache_goldfinger_bunks";              // 帮众铺位（中）
    public const string GoldfingerAmmoCrateId = "cache_goldfinger_ammo_crate";      // 弹药箱（中）
    public const string GoldfingerGunBenchId = "cache_goldfinger_gun_bench";        // 修械台（中）
    public const string GoldfingerHidePileId = "cache_goldfinger_hide_pile";        // 皮件堆（中）
    public const string GoldfingerFuelStashId = "cache_goldfinger_fuel_stash";      // 油料桶（中）
    public const string GoldfingerArmoryId = "cache_goldfinger_armory";            // 军械柜（深，←冲锋枪）
    public const string GoldfingerBossSafeId = "cache_goldfinger_boss_safe";        // 头目保险柜（深，←白银）
    public const string GoldfingerSilverCacheId = "cache_goldfinger_silver_cache";  // 银库暗格（最深，←白银）
    public const string GoldfingerBossMedkitId = "cache_goldfinger_boss_medkit";    // 头目急救箱（深，唯一医疗封顶）
    // 南丁格尔的小药店（[SPEC-B13]，小点 5 处；**小**药店＝基础药品/绷带为主但量薄，大头药品在医院）：
    //   小店面(前台收银台/店面货架，近) → 后屋药房(处方柜/冷藏箱，深) → 阁楼(储物，最深)。护士相遇点(NurseRecruit.MeetDiscoveryId)不在此列（招募触发点，非物资搜刮）。
    public const string PharmacyCounterId = "cache_pharmacy_counter";      // 前台收银台抽屉（近）
    public const string PharmacyShelfId = "cache_pharmacy_shelf";          // 店面货架（近，OTC 常备药）
    public const string PharmacyDispensaryId = "cache_pharmacy_dispensary";// 后屋药房处方柜（深，处方药——药店核心但量薄）
    public const string PharmacyColdBoxId = "cache_pharmacy_coldbox";      // 后屋冷藏箱（深，冷藏药/急救）
    public const string PharmacyAtticId = "cache_pharmacy_attic";         // 阁楼储物（最深，包装/杂物）

    // ==== [SPEC-B13-补3·拟设定待确认] 东部新村（Medium 顶格，30 处）——半建成迁建安置区。用户拍板"物资种类分散、量小，住宅区物资不单一不集中"：
    //   每点"杂而薄"(1~2件、品类混杂)、一户一户翻(每户厨房/衣柜/床底/阳台各一小点)；排屋/老屋加密为主，工地区维持偏建材但全图整体杂。
    //   排屋区(南/近，11)：一户户翻的住户杂物。
    public const string NewVillageShowroomId = "cache_newvillage_showroom";            // 排屋·样板间客厅（近）
    public const string NewVillageRowKitchenId = "cache_newvillage_row_kitchen";        // 排屋·A户厨房（近）
    public const string NewVillageRowAWardrobeId = "cache_newvillage_row_a_wardrobe";   // 排屋·A户衣柜（近）
    public const string NewVillageRowAUnderbedId = "cache_newvillage_row_a_underbed";   // 排屋·A户床底（近）
    public const string NewVillageRowBKitchenId = "cache_newvillage_row_b_kitchen";     // 排屋·B户厨房（近）
    public const string NewVillageRowBBalconyId = "cache_newvillage_row_b_balcony";     // 排屋·B户阳台（近）
    public const string NewVillageRowBClosetId = "cache_newvillage_row_b_closet";       // 排屋·B户储物间（近）
    public const string NewVillageUnfinishedId = "cache_newvillage_unfinished";         // 排屋·半成品单元（近）
    public const string NewVillageRowCShoeCabId = "cache_newvillage_row_c_shoecab";     // 排屋·C户玄关鞋柜（近）
    public const string NewVillageRowCBathId = "cache_newvillage_row_c_bath";           // 排屋·C户卫生间（近·偶发药品）
    public const string NewVillageRowDBalconyId = "cache_newvillage_row_d_balcony";     // 排屋·D户阳台杂物（近）
    //   工地区(中，8)：维持偏建材（木/钉/碎金属/线材/元件），但只是全图杂的一部分。
    public const string NewVillageLumberYardId = "cache_newvillage_lumber_yard";        // 工地·料场木料垛（中）
    public const string NewVillageScaffoldId = "cache_newvillage_scaffold";             // 工地·脚手架下料箱（中）
    public const string NewVillageToolShedId = "cache_newvillage_tool_shed";            // 工地·工具棚（中）
    public const string NewVillageRebarPileId = "cache_newvillage_rebar_pile";          // 工地·钢筋碎料堆（中）
    public const string NewVillageSiteOfficeId = "cache_newvillage_site_office";        // 工地·项目部工棚（中）
    public const string NewVillageCementPileId = "cache_newvillage_cement_pile";        // 工地·水泥料堆（中）
    public const string NewVillageElectricalBoxId = "cache_newvillage_electrical_box";  // 工地·配电箱（中）
    public const string NewVillageForemanLockerId = "cache_newvillage_foreman_locker";  // 工地·工头储物柜（深，稍杂但不夸张）
    //   老屋区(北/深，11)：已入住老户，一户户翻——家户食物/旧布/日用杂物+1处偶发药品。
    public const string NewVillageOldKitchenId = "cache_newvillage_old_kitchen";        // 老屋·灶间（深）
    public const string NewVillageOldWardrobeId = "cache_newvillage_old_wardrobe";      // 老屋·卧室衣柜（深）
    public const string NewVillageRootCellarId = "cache_newvillage_root_cellar";        // 老屋·菜窖（深）
    public const string NewVillageOldHallId = "cache_newvillage_old_hall";              // 老屋·堂屋（深）
    public const string NewVillageOldUnderbedId = "cache_newvillage_old_underbed";      // 老屋·床底（深）
    public const string NewVillageOldAtticId = "cache_newvillage_old_attic";            // 老屋·阁楼（深）
    public const string NewVillageOld2KitchenId = "cache_newvillage_old2_kitchen";      // 老屋·二号老屋厨房（深）
    public const string NewVillageOld2WoodshedId = "cache_newvillage_old2_woodshed";    // 老屋·二号老屋柴房（深）
    public const string NewVillageOld2YardId = "cache_newvillage_old2_yard";            // 老屋·二号老屋院子（深）
    public const string NewVillageOld2ShrineId = "cache_newvillage_old2_shrine";        // 老屋·神龛（深）
    public const string NewVillageOld2MedCabId = "cache_newvillage_old2_medcab";        // 老屋·药箱（最深·偶发单件药品）

    // ==== [SPEC-B13·拟设定待确认] 加油站（Medium 下限，10 处）——燃油大户：加油区(近)→便利店(中·食品少量)→修车棚(中·工具零件)→油罐区(深) ====
    public const string GasPumpIslandId = "cache_gas_pump_island";        // 加油区·加油岛油枪（近）
    public const string GasKioskId = "cache_gas_kiosk";                   // 加油区·收银亭（近）
    public const string GasStoreSnacksId = "cache_gas_store_snacks";      // 便利店·零食货架（中）
    public const string GasStoreDrinksId = "cache_gas_store_drinks";      // 便利店·冷饮柜（中）
    public const string GasStoreBackroomId = "cache_gas_store_backroom";  // 便利店·里屋（中）
    public const string GasRepairBayId = "cache_gas_repair_bay";          // 修车棚·工位（中）
    public const string GasPartsShelfId = "cache_gas_parts_shelf";        // 修车棚·零件货架（中）
    public const string GasOilRackId = "cache_gas_oil_rack";              // 修车棚·机油货架（中）
    public const string GasTankerId = "cache_gas_tanker";                 // 油罐区·油罐车（深）
    public const string GasUndergroundTankId = "cache_gas_underground_tank"; // 油罐区·地下储油间（最深，高价值燃油）

    // ==== [SPEC-B13] 超市（Medium，11 处）——幸存者骗局据点：外围卖场/仓储/后巷 7 处(货架残余·食物稍多但单点薄) + 内圈幸存者囤货 4 处(打赢才拿) ====
    //   骗局本体（接触对话/伏击/内圈闯入）由 SupermarketAmbush 另管；本处只是同址物资。内圈 4 点空间上落在幸存者据点内圈房间（打赢/闯入后可搜）。
    public const string SupermarketCheckoutId = "cache_supermarket_checkout";     // 外围·收银台前区（近）
    public const string SupermarketSnackAisleId = "cache_supermarket_snack_aisle"; // 外围·零食货架（近）
    public const string SupermarketCannedAisleId = "cache_supermarket_canned_aisle"; // 外围·罐头货架（近）
    public const string SupermarketHouseholdId = "cache_supermarket_household";   // 外围·日用百货架（中）
    public const string SupermarketHardwareId = "cache_supermarket_hardware";     // 外围·五金杂货角（中）
    public const string SupermarketStockroomId = "cache_supermarket_stockroom";   // 外围·仓储区货架（中）
    public const string SupermarketBackAlleyId = "cache_supermarket_back_alley";  // 外围·后巷卸货区/垃圾箱（中）
    public const string SupermarketHoardFoodId = "cache_supermarket_hoard_food";  // 内圈·他们的囤粮（打赢才拿）
    public const string SupermarketHoardMedsId = "cache_supermarket_hoard_meds";  // 内圈·他们的药箱（打赢才拿）
    public const string SupermarketHoardGearId = "cache_supermarket_hoard_gear";  // 内圈·缴获装备堆（打赢才拿）
    public const string SupermarketHoardStashId = "cache_supermarket_hoard_stash"; // 内圈·头目私囤（打赢才拿·白银）

    // ==== [SPEC-B13] 医院（Large，30 处·丧尸巢废墟）——高风险高收益：医疗物资集中投放于药房/手术层（打破"禁医疗灌水"的例外点，单点仍克制、总量靠集中） ====
    //   分区（近→深）：门诊/急诊大厅 7 → 住院部 8 → 药房 7(医疗深藏) → 手术层 8(手术耗材+高价值医疗，最深)。非医疗区物资克制。
    // 门诊/急诊大厅（近浅，7）：
    public const string HospitalReceptionId = "cache_hospital_reception";       // 挂号台（近）
    public const string HospitalTriageId = "cache_hospital_triage";            // 分诊台（近）
    public const string HospitalWaitingRoomId = "cache_hospital_waiting_room"; // 候诊区（近）
    public const string HospitalVendingId = "cache_hospital_vending";          // 大厅自动贩卖机（近）
    public const string HospitalErTrolleyId = "cache_hospital_er_trolley";     // 急诊抢救推车（近）
    public const string HospitalSecurityId = "cache_hospital_security";        // 大厅保安室（近）
    public const string HospitalCafeteriaId = "cache_hospital_cafeteria";      // 一层食堂（近）
    // 住院部（中，8）：
    public const string HospitalWardLinenId = "cache_hospital_ward_linen";     // 病房布草间（中）
    public const string HospitalWardLockerId = "cache_hospital_ward_locker";   // 病床储物柜（中）
    public const string HospitalNurseStationId = "cache_hospital_nurse_station"; // 护士站（中·医疗）
    public const string HospitalDoctorOfficeId = "cache_hospital_doctor_office"; // 医生办公室（中）
    public const string HospitalDirtyUtilityId = "cache_hospital_dirty_utility"; // 污物处置间（中）
    public const string HospitalKitchenetteId = "cache_hospital_kitchenette";  // 楼层配餐间（中）
    public const string HospitalFloorStoreId = "cache_hospital_floor_store";   // 楼层库房（中）
    public const string HospitalMorgueId = "cache_hospital_morgue";            // 太平间（中·深，惊悚）
    // 药房（深，7·医疗集中）：
    public const string HospitalPharmacyCounterId = "cache_hospital_pharmacy_counter"; // 药房前台（深·医疗）
    public const string HospitalPharmacyShelfId = "cache_hospital_pharmacy_shelf";     // 处方药架（深·医疗）
    public const string HospitalPharmacyFridgeId = "cache_hospital_pharmacy_fridge";   // 冷藏药柜（深·医疗）
    public const string HospitalPharmacyBackId = "cache_hospital_pharmacy_back";       // 药库后间（深·高价值抗生素）
    public const string HospitalNarcoticsCabinetId = "cache_hospital_narcotics_cabinet"; // 管制药柜（深·撬锁）
    public const string HospitalDispensaryId = "cache_hospital_dispensary";            // 配药室（深·医疗）
    public const string HospitalMedSupplyRoomId = "cache_hospital_med_supply_room";    // 医材库（深·手术耗材）
    // 手术层（最深，8·手术耗材+高价值医疗）：
    public const string HospitalOrScrubId = "cache_hospital_or_scrub";         // 手术准备/刷手间（最深）
    public const string HospitalOrTheatreId = "cache_hospital_or_theatre";     // 手术室（最深·急救包）
    public const string HospitalSterileStoreId = "cache_hospital_sterile_store"; // 无菌耗材库（最深）
    public const string HospitalIcuId = "cache_hospital_icu";                  // ICU 重症监护（最深·医疗）
    public const string HospitalBloodBankId = "cache_hospital_blood_bank";     // 血库（最深·急救包）
    public const string HospitalAnesthesiaId = "cache_hospital_anesthesia";    // 麻醉科（最深·成药）
    public const string HospitalSterilizerId = "cache_hospital_sterilizer";    // 器械灭菌室（最深·夹板）
    public const string HospitalChiefSafeId = "cache_hospital_chief_safe";     // 主任药品保险柜（最深·最高价值医疗）

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
    public const string GoldfingerCheckpointFlag = "searched_goldfinger_checkpoint";
    public const string GoldfingerYardWreckFlag = "searched_goldfinger_yard_wreck";
    public const string GoldfingerBunksFlag = "searched_goldfinger_bunks";
    public const string GoldfingerAmmoCrateFlag = "searched_goldfinger_ammo_crate";
    public const string GoldfingerGunBenchFlag = "searched_goldfinger_gun_bench";
    public const string GoldfingerHidePileFlag = "searched_goldfinger_hide_pile";
    public const string GoldfingerFuelStashFlag = "searched_goldfinger_fuel_stash";
    public const string GoldfingerArmoryFlag = "searched_goldfinger_armory";
    public const string GoldfingerBossSafeFlag = "searched_goldfinger_boss_safe";
    public const string GoldfingerSilverCacheFlag = "searched_goldfinger_silver_cache";
    public const string GoldfingerBossMedkitFlag = "searched_goldfinger_boss_medkit";
    // 南丁格尔的小药店 5 处一次性 flag（与上方 id 一一对应）：
    public const string PharmacyCounterFlag = "searched_pharmacy_counter";
    public const string PharmacyShelfFlag = "searched_pharmacy_shelf";
    public const string PharmacyDispensaryFlag = "searched_pharmacy_dispensary";
    public const string PharmacyColdBoxFlag = "searched_pharmacy_coldbox";
    public const string PharmacyAtticFlag = "searched_pharmacy_attic";
    // [SPEC-B13-补3] 东部新村 30 处一次性 flag（与上方 id 一一对应）：
    public const string NewVillageShowroomFlag = "searched_newvillage_showroom";
    public const string NewVillageRowKitchenFlag = "searched_newvillage_row_kitchen";
    public const string NewVillageRowAWardrobeFlag = "searched_newvillage_row_a_wardrobe";
    public const string NewVillageRowAUnderbedFlag = "searched_newvillage_row_a_underbed";
    public const string NewVillageRowBKitchenFlag = "searched_newvillage_row_b_kitchen";
    public const string NewVillageRowBBalconyFlag = "searched_newvillage_row_b_balcony";
    public const string NewVillageRowBClosetFlag = "searched_newvillage_row_b_closet";
    public const string NewVillageUnfinishedFlag = "searched_newvillage_unfinished";
    public const string NewVillageRowCShoeCabFlag = "searched_newvillage_row_c_shoecab";
    public const string NewVillageRowCBathFlag = "searched_newvillage_row_c_bath";
    public const string NewVillageRowDBalconyFlag = "searched_newvillage_row_d_balcony";
    public const string NewVillageLumberYardFlag = "searched_newvillage_lumber_yard";
    public const string NewVillageScaffoldFlag = "searched_newvillage_scaffold";
    public const string NewVillageToolShedFlag = "searched_newvillage_tool_shed";
    public const string NewVillageRebarPileFlag = "searched_newvillage_rebar_pile";
    public const string NewVillageSiteOfficeFlag = "searched_newvillage_site_office";
    public const string NewVillageCementPileFlag = "searched_newvillage_cement_pile";
    public const string NewVillageElectricalBoxFlag = "searched_newvillage_electrical_box";
    public const string NewVillageForemanLockerFlag = "searched_newvillage_foreman_locker";
    public const string NewVillageOldKitchenFlag = "searched_newvillage_old_kitchen";
    public const string NewVillageOldWardrobeFlag = "searched_newvillage_old_wardrobe";
    public const string NewVillageRootCellarFlag = "searched_newvillage_root_cellar";
    public const string NewVillageOldHallFlag = "searched_newvillage_old_hall";
    public const string NewVillageOldUnderbedFlag = "searched_newvillage_old_underbed";
    public const string NewVillageOldAtticFlag = "searched_newvillage_old_attic";
    public const string NewVillageOld2KitchenFlag = "searched_newvillage_old2_kitchen";
    public const string NewVillageOld2WoodshedFlag = "searched_newvillage_old2_woodshed";
    public const string NewVillageOld2YardFlag = "searched_newvillage_old2_yard";
    public const string NewVillageOld2ShrineFlag = "searched_newvillage_old2_shrine";
    public const string NewVillageOld2MedCabFlag = "searched_newvillage_old2_medcab";
    // [SPEC-B13] 加油站 10 处一次性 flag（与上方 id 一一对应）：
    public const string GasPumpIslandFlag = "searched_gas_pump_island";
    public const string GasKioskFlag = "searched_gas_kiosk";
    public const string GasStoreSnacksFlag = "searched_gas_store_snacks";
    public const string GasStoreDrinksFlag = "searched_gas_store_drinks";
    public const string GasStoreBackroomFlag = "searched_gas_store_backroom";
    public const string GasRepairBayFlag = "searched_gas_repair_bay";
    public const string GasPartsShelfFlag = "searched_gas_parts_shelf";
    public const string GasOilRackFlag = "searched_gas_oil_rack";
    public const string GasTankerFlag = "searched_gas_tanker";
    public const string GasUndergroundTankFlag = "searched_gas_underground_tank";
    // [SPEC-B13] 超市 11 处一次性 flag（与上方 id 一一对应）：
    public const string SupermarketCheckoutFlag = "searched_supermarket_checkout";
    public const string SupermarketSnackAisleFlag = "searched_supermarket_snack_aisle";
    public const string SupermarketCannedAisleFlag = "searched_supermarket_canned_aisle";
    public const string SupermarketHouseholdFlag = "searched_supermarket_household";
    public const string SupermarketHardwareFlag = "searched_supermarket_hardware";
    public const string SupermarketStockroomFlag = "searched_supermarket_stockroom";
    public const string SupermarketBackAlleyFlag = "searched_supermarket_back_alley";
    public const string SupermarketHoardFoodFlag = "searched_supermarket_hoard_food";
    public const string SupermarketHoardMedsFlag = "searched_supermarket_hoard_meds";
    public const string SupermarketHoardGearFlag = "searched_supermarket_hoard_gear";
    public const string SupermarketHoardStashFlag = "searched_supermarket_hoard_stash";
    // [SPEC-B13] 医院 30 处一次性 flag（与上方 id 一一对应）：
    public const string HospitalReceptionFlag = "searched_hospital_reception";
    public const string HospitalTriageFlag = "searched_hospital_triage";
    public const string HospitalWaitingRoomFlag = "searched_hospital_waiting_room";
    public const string HospitalVendingFlag = "searched_hospital_vending";
    public const string HospitalErTrolleyFlag = "searched_hospital_er_trolley";
    public const string HospitalSecurityFlag = "searched_hospital_security";
    public const string HospitalCafeteriaFlag = "searched_hospital_cafeteria";
    public const string HospitalWardLinenFlag = "searched_hospital_ward_linen";
    public const string HospitalWardLockerFlag = "searched_hospital_ward_locker";
    public const string HospitalNurseStationFlag = "searched_hospital_nurse_station";
    public const string HospitalDoctorOfficeFlag = "searched_hospital_doctor_office";
    public const string HospitalDirtyUtilityFlag = "searched_hospital_dirty_utility";
    public const string HospitalKitchenetteFlag = "searched_hospital_kitchenette";
    public const string HospitalFloorStoreFlag = "searched_hospital_floor_store";
    public const string HospitalMorgueFlag = "searched_hospital_morgue";
    public const string HospitalPharmacyCounterFlag = "searched_hospital_pharmacy_counter";
    public const string HospitalPharmacyShelfFlag = "searched_hospital_pharmacy_shelf";
    public const string HospitalPharmacyFridgeFlag = "searched_hospital_pharmacy_fridge";
    public const string HospitalPharmacyBackFlag = "searched_hospital_pharmacy_back";
    public const string HospitalNarcoticsCabinetFlag = "searched_hospital_narcotics_cabinet";
    public const string HospitalDispensaryFlag = "searched_hospital_dispensary";
    public const string HospitalMedSupplyRoomFlag = "searched_hospital_med_supply_room";
    public const string HospitalOrScrubFlag = "searched_hospital_or_scrub";
    public const string HospitalOrTheatreFlag = "searched_hospital_or_theatre";
    public const string HospitalSterileStoreFlag = "searched_hospital_sterile_store";
    public const string HospitalIcuFlag = "searched_hospital_icu";
    public const string HospitalBloodBankFlag = "searched_hospital_blood_bank";
    public const string HospitalAnesthesiaFlag = "searched_hospital_anesthesia";
    public const string HospitalSterilizerFlag = "searched_hospital_sterilizer";
    public const string HospitalChiefSafeFlag = "searched_hospital_chief_safe";

    // ——关键投放物标识（须与 WeaponTable / BookLibrary 一致）——
    /// <summary>栓动猎枪武器名，须与 <c>WeaponTable.BoltActionHuntingRifle().Name</c> 一致。</summary>
    public const string BoltActionRifleName = "栓动猎枪";

    /// <summary>金手指帮军械柜招牌武器＝冲锋枪，须与 <c>WeaponTable.Smg().Name</c> 一致（帮派火力，[SPEC-B12-补]"打过才拿"最深处奖励，量级拟定待调）。</summary>
    public const string GangSmgName = "冲锋枪";

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
        // 金手指帮根据地（中型·战斗为主，[SPEC-B12-补]）：11 处帮派储备点，近→深（岗哨/前院 少 → gauntlet 中 → 军械/头目区 深）。
        // 克莉丝汀复仇线两具尸体发现点(GoldfingerDiscovery)不在此列——那是剧情尸体点，命名空间独立、由 ExplorationProgress.PointFlagsFor 另行登记。
        GoldfingerBaseName => new[]
        {
            GoldfingerCheckpointId, GoldfingerYardWreckId,
            GoldfingerBunksId, GoldfingerAmmoCrateId, GoldfingerGunBenchId, GoldfingerHidePileId, GoldfingerFuelStashId,
            GoldfingerArmoryId, GoldfingerBossSafeId, GoldfingerSilverCacheId, GoldfingerBossMedkitId,
        },
        // [SPEC-B13-补3·拟设定待确认] 东部新村（中点顶格，30 处·杂而薄）：排屋(近·一户户翻11)→工地(中·偏建材8)→老屋(深·一户户翻11)，近→深登记序。
        EastNewVillageName => new[]
        {
            // 排屋区(南/近, 11·一户户翻)
            NewVillageShowroomId, NewVillageRowKitchenId, NewVillageRowAWardrobeId, NewVillageRowAUnderbedId,
            NewVillageRowBKitchenId, NewVillageRowBBalconyId, NewVillageRowBClosetId, NewVillageUnfinishedId,
            NewVillageRowCShoeCabId, NewVillageRowCBathId, NewVillageRowDBalconyId,
            // 工地区(中, 8·维持偏建材)
            NewVillageLumberYardId, NewVillageScaffoldId, NewVillageToolShedId, NewVillageRebarPileId,
            NewVillageSiteOfficeId, NewVillageCementPileId, NewVillageElectricalBoxId, NewVillageForemanLockerId,
            // 老屋区(北/深, 11·一户户翻，含最深药箱)
            NewVillageOldKitchenId, NewVillageOldWardrobeId, NewVillageRootCellarId, NewVillageOldHallId,
            NewVillageOldUnderbedId, NewVillageOldAtticId, NewVillageOld2KitchenId, NewVillageOld2WoodshedId,
            NewVillageOld2YardId, NewVillageOld2ShrineId, NewVillageOld2MedCabId,
        },
        // [SPEC-B13·拟设定待确认] 加油站（中点下限，10 处）：加油区(近)→便利店(中·食品少量)→修车棚(中·工具零件)→油罐区(深·燃油大户高价值)。
        GasStationName => new[]
        {
            // 加油区(近, 2)
            GasPumpIslandId, GasKioskId,
            // 便利店(中, 3·食品少量)
            GasStoreSnacksId, GasStoreDrinksId, GasStoreBackroomId,
            // 修车棚(中, 3·工具零件)
            GasRepairBayId, GasPartsShelfId, GasOilRackId,
            // 油罐区(深, 2·燃油大户)
            GasTankerId, GasUndergroundTankId,
        },
        // [SPEC-B13] 超市（中点，11 处）：外围卖场/仓储/后巷(近→中, 7·货架残余食物稍多但薄) → 内圈幸存者囤货(4·打赢才拿)。骗局本体由 SupermarketAmbush 管，不在此列。
        SupermarketName => new[]
        {
            // 外围(近→中, 7)
            SupermarketCheckoutId, SupermarketSnackAisleId, SupermarketCannedAisleId,
            SupermarketHouseholdId, SupermarketHardwareId, SupermarketStockroomId, SupermarketBackAlleyId,
            // 内圈·幸存者囤货(打赢才拿, 4)
            SupermarketHoardFoodId, SupermarketHoardMedsId, SupermarketHoardGearId, SupermarketHoardStashId,
        },
        // [SPEC-B13] 医院（大点，30 处·丧尸巢）：门诊/急诊(近, 7) → 住院部(中, 8) → 药房(深, 7·医疗集中) → 手术层(最深, 8·手术耗材+高价值医疗)。医疗集中投放＝高风险高收益身份。
        HospitalName => new[]
        {
            // 门诊/急诊大厅(近, 7)
            HospitalReceptionId, HospitalTriageId, HospitalWaitingRoomId, HospitalVendingId,
            HospitalErTrolleyId, HospitalSecurityId, HospitalCafeteriaId,
            // 住院部(中, 8)
            HospitalWardLinenId, HospitalWardLockerId, HospitalNurseStationId, HospitalDoctorOfficeId,
            HospitalDirtyUtilityId, HospitalKitchenetteId, HospitalFloorStoreId, HospitalMorgueId,
            // 药房(深, 7·医疗集中)
            HospitalPharmacyCounterId, HospitalPharmacyShelfId, HospitalPharmacyFridgeId, HospitalPharmacyBackId,
            HospitalNarcoticsCabinetId, HospitalDispensaryId, HospitalMedSupplyRoomId,
            // 手术层(最深, 8·手术耗材+高价值医疗)
            HospitalOrScrubId, HospitalOrTheatreId, HospitalSterileStoreId, HospitalIcuId,
            HospitalBloodBankId, HospitalAnesthesiaId, HospitalSterilizerId, HospitalChiefSafeId,
        },
        // 南丁格尔的小药店（[SPEC-B13]，小点 5 处）：前台收银台(近)→店面货架(近)→后屋处方柜(深)→后屋冷藏箱(深)→阁楼(最深)。
        // 护士相遇点(NurseRecruit.MeetDiscoveryId)不在此列——招募主线触发点，不计物资完成度（同瞭望望远镜/村庄救援口径）。
        NurseRecruit.DestinationName => new[]
        {
            PharmacyCounterId, PharmacyShelfId,
            PharmacyDispensaryId, PharmacyColdBoxId, PharmacyAtticId,
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
        GoldfingerCheckpointId => GoldfingerCheckpointFlag,
        GoldfingerYardWreckId => GoldfingerYardWreckFlag,
        GoldfingerBunksId => GoldfingerBunksFlag,
        GoldfingerAmmoCrateId => GoldfingerAmmoCrateFlag,
        GoldfingerGunBenchId => GoldfingerGunBenchFlag,
        GoldfingerHidePileId => GoldfingerHidePileFlag,
        GoldfingerFuelStashId => GoldfingerFuelStashFlag,
        GoldfingerArmoryId => GoldfingerArmoryFlag,
        GoldfingerBossSafeId => GoldfingerBossSafeFlag,
        GoldfingerSilverCacheId => GoldfingerSilverCacheFlag,
        GoldfingerBossMedkitId => GoldfingerBossMedkitFlag,
        // [SPEC-B13-补3] 东部新村 30：
        NewVillageShowroomId => NewVillageShowroomFlag,
        NewVillageRowKitchenId => NewVillageRowKitchenFlag,
        NewVillageRowAWardrobeId => NewVillageRowAWardrobeFlag,
        NewVillageRowAUnderbedId => NewVillageRowAUnderbedFlag,
        NewVillageRowBKitchenId => NewVillageRowBKitchenFlag,
        NewVillageRowBBalconyId => NewVillageRowBBalconyFlag,
        NewVillageRowBClosetId => NewVillageRowBClosetFlag,
        NewVillageUnfinishedId => NewVillageUnfinishedFlag,
        NewVillageRowCShoeCabId => NewVillageRowCShoeCabFlag,
        NewVillageRowCBathId => NewVillageRowCBathFlag,
        NewVillageRowDBalconyId => NewVillageRowDBalconyFlag,
        NewVillageLumberYardId => NewVillageLumberYardFlag,
        NewVillageScaffoldId => NewVillageScaffoldFlag,
        NewVillageToolShedId => NewVillageToolShedFlag,
        NewVillageRebarPileId => NewVillageRebarPileFlag,
        NewVillageSiteOfficeId => NewVillageSiteOfficeFlag,
        NewVillageCementPileId => NewVillageCementPileFlag,
        NewVillageElectricalBoxId => NewVillageElectricalBoxFlag,
        NewVillageForemanLockerId => NewVillageForemanLockerFlag,
        NewVillageOldKitchenId => NewVillageOldKitchenFlag,
        NewVillageOldWardrobeId => NewVillageOldWardrobeFlag,
        NewVillageRootCellarId => NewVillageRootCellarFlag,
        NewVillageOldHallId => NewVillageOldHallFlag,
        NewVillageOldUnderbedId => NewVillageOldUnderbedFlag,
        NewVillageOldAtticId => NewVillageOldAtticFlag,
        NewVillageOld2KitchenId => NewVillageOld2KitchenFlag,
        NewVillageOld2WoodshedId => NewVillageOld2WoodshedFlag,
        NewVillageOld2YardId => NewVillageOld2YardFlag,
        NewVillageOld2ShrineId => NewVillageOld2ShrineFlag,
        NewVillageOld2MedCabId => NewVillageOld2MedCabFlag,
        // [SPEC-B13] 加油站 10：
        GasPumpIslandId => GasPumpIslandFlag,
        GasKioskId => GasKioskFlag,
        GasStoreSnacksId => GasStoreSnacksFlag,
        GasStoreDrinksId => GasStoreDrinksFlag,
        GasStoreBackroomId => GasStoreBackroomFlag,
        GasRepairBayId => GasRepairBayFlag,
        GasPartsShelfId => GasPartsShelfFlag,
        GasOilRackId => GasOilRackFlag,
        GasTankerId => GasTankerFlag,
        GasUndergroundTankId => GasUndergroundTankFlag,
        // [SPEC-B13] 超市 11：
        SupermarketCheckoutId => SupermarketCheckoutFlag,
        SupermarketSnackAisleId => SupermarketSnackAisleFlag,
        SupermarketCannedAisleId => SupermarketCannedAisleFlag,
        SupermarketHouseholdId => SupermarketHouseholdFlag,
        SupermarketHardwareId => SupermarketHardwareFlag,
        SupermarketStockroomId => SupermarketStockroomFlag,
        SupermarketBackAlleyId => SupermarketBackAlleyFlag,
        SupermarketHoardFoodId => SupermarketHoardFoodFlag,
        SupermarketHoardMedsId => SupermarketHoardMedsFlag,
        SupermarketHoardGearId => SupermarketHoardGearFlag,
        SupermarketHoardStashId => SupermarketHoardStashFlag,
        // [SPEC-B13] 医院 30：
        HospitalReceptionId => HospitalReceptionFlag,
        HospitalTriageId => HospitalTriageFlag,
        HospitalWaitingRoomId => HospitalWaitingRoomFlag,
        HospitalVendingId => HospitalVendingFlag,
        HospitalErTrolleyId => HospitalErTrolleyFlag,
        HospitalSecurityId => HospitalSecurityFlag,
        HospitalCafeteriaId => HospitalCafeteriaFlag,
        HospitalWardLinenId => HospitalWardLinenFlag,
        HospitalWardLockerId => HospitalWardLockerFlag,
        HospitalNurseStationId => HospitalNurseStationFlag,
        HospitalDoctorOfficeId => HospitalDoctorOfficeFlag,
        HospitalDirtyUtilityId => HospitalDirtyUtilityFlag,
        HospitalKitchenetteId => HospitalKitchenetteFlag,
        HospitalFloorStoreId => HospitalFloorStoreFlag,
        HospitalMorgueId => HospitalMorgueFlag,
        HospitalPharmacyCounterId => HospitalPharmacyCounterFlag,
        HospitalPharmacyShelfId => HospitalPharmacyShelfFlag,
        HospitalPharmacyFridgeId => HospitalPharmacyFridgeFlag,
        HospitalPharmacyBackId => HospitalPharmacyBackFlag,
        HospitalNarcoticsCabinetId => HospitalNarcoticsCabinetFlag,
        HospitalDispensaryId => HospitalDispensaryFlag,
        HospitalMedSupplyRoomId => HospitalMedSupplyRoomFlag,
        HospitalOrScrubId => HospitalOrScrubFlag,
        HospitalOrTheatreId => HospitalOrTheatreFlag,
        HospitalSterileStoreId => HospitalSterileStoreFlag,
        HospitalIcuId => HospitalIcuFlag,
        HospitalBloodBankId => HospitalBloodBankFlag,
        HospitalAnesthesiaId => HospitalAnesthesiaFlag,
        HospitalSterilizerId => HospitalSterilizerFlag,
        HospitalChiefSafeId => HospitalChiefSafeFlag,
        PharmacyCounterId => PharmacyCounterFlag,
        PharmacyShelfId => PharmacyShelfFlag,
        PharmacyDispensaryId => PharmacyDispensaryFlag,
        PharmacyColdBoxId => PharmacyColdBoxFlag,
        PharmacyAtticId => PharmacyAtticFlag,
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
                    LootItem.Material("dandelion", 1),      // [SPEC-B14] 后院墙根的蒲公英
                    LootItem.Material("rosehip", 1),        // [SPEC-B14] 篱边野蔷薇的玫瑰果
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
                new[] { LootItem.Material("rope", 1), LootItem.Material("wire", 1), LootItem.Material("dandelion", 1), LootItem.Material("laojunxu", 1) }, // [SPEC-B14] 河边野草：蒲公英/老君须
                RiversideFishingTitle, RiversideFishingNarrative),

            RiversideCellarId when NotYet(flags, RiversideCellarFlag) => new CacheResult(
                RiversideCellarFlag,
                new[] { LootItem.Food(1), LootItem.Material("bone", 1), LootItem.Material("rosehip", 1) }, // [SPEC-B14] 窖里阴干的玫瑰果
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
                new[] { LootItem.Material("wood", 2), LootItem.Material("fuel", 1), LootItem.Material("laojunxu", 1), LootItem.Material("dandelion", 1) }, // [SPEC-B14] 后山坡的老君须/蒲公英
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
                new[] { LootItem.Food(1), LootItem.Material("bone", 1), LootItem.Material("dandelion", 1) }, // [SPEC-B14] 河滩草丛的蒲公英
                VillageRiverbankShackTitle, VillageRiverbankShackNarrative),

            VillageRiverbankPumpId when NotYet(flags, VillageRiverbankPumpFlag) => new CacheResult(
                VillageRiverbankPumpFlag,
                new[] { LootItem.Material("components", 1), LootItem.Material("fuel", 1), LootItem.Material("wire", 1) },
                VillageRiverbankPumpTitle, VillageRiverbankPumpNarrative),

            // —— [SPEC-B12-补] 金手指帮根据地 11 处（中型·战斗为主；帮派储备＝弹药火药/碎金属/武器配件/白银/皮革布料，无食物，医疗仅头目急救箱 1 处封顶）——
            // 近入口(2)：岗哨/前院，量薄。
            GoldfingerCheckpointId when NotYet(flags, GoldfingerCheckpointFlag) => new CacheResult(
                GoldfingerCheckpointFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("gunpowder", 1) },
                GoldfingerCheckpointTitle, GoldfingerCheckpointNarrative),

            GoldfingerYardWreckId when NotYet(flags, GoldfingerYardWreckFlag) => new CacheResult(
                GoldfingerYardWreckFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("fuel", 1) },
                GoldfingerYardWreckTitle, GoldfingerYardWreckNarrative),

            // 中区 gauntlet(5)：铺位/弹药/修械/皮件/油料。
            GoldfingerBunksId when NotYet(flags, GoldfingerBunksFlag) => new CacheResult(
                GoldfingerBunksFlag,
                new[] { LootItem.Material("cloth", 2), LootItem.Material("scrap_cloth", 1) },
                GoldfingerBunksTitle, GoldfingerBunksNarrative),

            GoldfingerAmmoCrateId when NotYet(flags, GoldfingerAmmoCrateFlag) => new CacheResult(
                GoldfingerAmmoCrateFlag,
                new[] { LootItem.Material("gunpowder", 2), LootItem.Material("components", 1) },
                GoldfingerAmmoCrateTitle, GoldfingerAmmoCrateNarrative),

            GoldfingerGunBenchId when NotYet(flags, GoldfingerGunBenchFlag) => new CacheResult(
                GoldfingerGunBenchFlag,
                new[] { LootItem.Material("components", 2), LootItem.Material("scrap_metal", 1) },
                GoldfingerGunBenchTitle, GoldfingerGunBenchNarrative),

            GoldfingerHidePileId when NotYet(flags, GoldfingerHidePileFlag) => new CacheResult(
                GoldfingerHidePileFlag,
                new[] { LootItem.Material("leather", 2), LootItem.Material("cloth", 1) },
                GoldfingerHidePileTitle, GoldfingerHidePileNarrative),

            GoldfingerFuelStashId when NotYet(flags, GoldfingerFuelStashFlag) => new CacheResult(
                GoldfingerFuelStashFlag,
                new[] { LootItem.Material("fuel", 2), LootItem.Material("wire", 1) },
                GoldfingerFuelStashTitle, GoldfingerFuelStashNarrative),

            // 深处(4)：军械柜(←冲锋枪)/头目保险柜(←白银)/银库暗格(←白银)/头目急救箱(唯一医疗)。"打过才拿"。
            GoldfingerArmoryId when NotYet(flags, GoldfingerArmoryFlag) => new CacheResult(
                GoldfingerArmoryFlag,
                new[] { LootItem.Weapon(GangSmgName), LootItem.Material("gunpowder", 2), LootItem.Material("components", 1) },
                GoldfingerArmoryTitle, GoldfingerArmoryNarrative),

            GoldfingerBossSafeId when NotYet(flags, GoldfingerBossSafeFlag) => new CacheResult(
                GoldfingerBossSafeFlag,
                new[] { LootItem.Material("silver", Silver.FromWhole(6)), LootItem.Material("components", 1) },
                GoldfingerBossSafeTitle, GoldfingerBossSafeNarrative),

            GoldfingerSilverCacheId when NotYet(flags, GoldfingerSilverCacheFlag) => new CacheResult(
                GoldfingerSilverCacheFlag,
                new[] { LootItem.Material("silver", Silver.FromWhole(4)), LootItem.Material("leather", 1) },
                GoldfingerSilverCacheTitle, GoldfingerSilverCacheNarrative),

            GoldfingerBossMedkitId when NotYet(flags, GoldfingerBossMedkitFlag) => new CacheResult(
                GoldfingerBossMedkitFlag,
                new[] { LootItem.Material("first_aid_kit", 1), LootItem.Material("bandage", 1) },
                GoldfingerBossMedkitTitle, GoldfingerBossMedkitNarrative),

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

            // —— [SPEC-B13-补3·拟设定待确认] 东部新村（30 处·杂而薄：每点 1~2 件、品类混杂；戒掉"建材大户"单一身份；食物克制；draft 待用户改）——
            // 排屋区(南/近, 11·一户户翻)：住户零碎食物/旧布/日用杂物，偶发单件药品。
            NewVillageShowroomId when NotYet(flags, NewVillageShowroomFlag) => new CacheResult(
                NewVillageShowroomFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("scrap_metal", 1) },
                NewVillageShowroomTitle, NewVillageShowroomNarrative),

            NewVillageRowKitchenId when NotYet(flags, NewVillageRowKitchenFlag) => new CacheResult(
                NewVillageRowKitchenFlag,
                new[] { LootItem.Food(1), LootItem.Material("cloth", 1) },
                NewVillageRowKitchenTitle, NewVillageRowKitchenNarrative),

            NewVillageRowAWardrobeId when NotYet(flags, NewVillageRowAWardrobeFlag) => new CacheResult(
                NewVillageRowAWardrobeFlag,
                new[] { LootItem.Material("cloth", 2) },
                NewVillageRowAWardrobeTitle, NewVillageRowAWardrobeNarrative),

            NewVillageRowAUnderbedId when NotYet(flags, NewVillageRowAUnderbedFlag) => new CacheResult(
                NewVillageRowAUnderbedFlag,
                new[] { LootItem.Material("scrap_cloth", 1), LootItem.Material("nails", 1) },
                NewVillageRowAUnderbedTitle, NewVillageRowAUnderbedNarrative),

            NewVillageRowBKitchenId when NotYet(flags, NewVillageRowBKitchenFlag) => new CacheResult(
                NewVillageRowBKitchenFlag,
                new[] { LootItem.Food(1), LootItem.Material("bandage", 1) },
                NewVillageRowBKitchenTitle, NewVillageRowBKitchenNarrative),

            NewVillageRowBBalconyId when NotYet(flags, NewVillageRowBBalconyFlag) => new CacheResult(
                NewVillageRowBBalconyFlag,
                new[] { LootItem.Material("wire", 1), LootItem.Material("rope", 1) },
                NewVillageRowBBalconyTitle, NewVillageRowBBalconyNarrative),

            NewVillageRowBClosetId when NotYet(flags, NewVillageRowBClosetFlag) => new CacheResult(
                NewVillageRowBClosetFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("wood", 1) },
                NewVillageRowBClosetTitle, NewVillageRowBClosetNarrative),

            NewVillageUnfinishedId when NotYet(flags, NewVillageUnfinishedFlag) => new CacheResult(
                NewVillageUnfinishedFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("wood", 1) },
                NewVillageUnfinishedTitle, NewVillageUnfinishedNarrative),

            NewVillageRowCShoeCabId when NotYet(flags, NewVillageRowCShoeCabFlag) => new CacheResult(
                NewVillageRowCShoeCabFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("scrap_metal", 1) },
                NewVillageRowCShoeCabTitle, NewVillageRowCShoeCabNarrative),

            NewVillageRowCBathId when NotYet(flags, NewVillageRowCBathFlag) => new CacheResult(
                NewVillageRowCBathFlag,
                new[] { LootItem.Material("bandage", 1) },
                NewVillageRowCBathTitle, NewVillageRowCBathNarrative),

            NewVillageRowDBalconyId when NotYet(flags, NewVillageRowDBalconyFlag) => new CacheResult(
                NewVillageRowDBalconyFlag,
                new[] { LootItem.Material("wire", 1), LootItem.Material("scrap_metal", 1) },
                NewVillageRowDBalconyTitle, NewVillageRowDBalconyNarrative),

            // 工地区(中, 8·维持偏建材，但只是全图杂的一部分)：木/钉/碎金属/线材/元件，仍薄。
            NewVillageLumberYardId when NotYet(flags, NewVillageLumberYardFlag) => new CacheResult(
                NewVillageLumberYardFlag,
                new[] { LootItem.Material("wood", 2), LootItem.Material("rope", 1) },
                NewVillageLumberYardTitle, NewVillageLumberYardNarrative),

            NewVillageScaffoldId when NotYet(flags, NewVillageScaffoldFlag) => new CacheResult(
                NewVillageScaffoldFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("nails", 1) },
                NewVillageScaffoldTitle, NewVillageScaffoldNarrative),

            NewVillageToolShedId when NotYet(flags, NewVillageToolShedFlag) => new CacheResult(
                NewVillageToolShedFlag,
                new[] { LootItem.Material("wire", 1), LootItem.Material("rope", 1) },
                NewVillageToolShedTitle, NewVillageToolShedNarrative),

            NewVillageRebarPileId when NotYet(flags, NewVillageRebarPileFlag) => new CacheResult(
                NewVillageRebarPileFlag,
                new[] { LootItem.Material("scrap_metal", 2) },
                NewVillageRebarPileTitle, NewVillageRebarPileNarrative),

            NewVillageSiteOfficeId when NotYet(flags, NewVillageSiteOfficeFlag) => new CacheResult(
                NewVillageSiteOfficeFlag,
                new[] { LootItem.Food(1), LootItem.Material("components", 1) },
                NewVillageSiteOfficeTitle, NewVillageSiteOfficeNarrative),

            NewVillageCementPileId when NotYet(flags, NewVillageCementPileFlag) => new CacheResult(
                NewVillageCementPileFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("wood", 1) },
                NewVillageCementPileTitle, NewVillageCementPileNarrative),

            NewVillageElectricalBoxId when NotYet(flags, NewVillageElectricalBoxFlag) => new CacheResult(
                NewVillageElectricalBoxFlag,
                new[] { LootItem.Material("wire", 1), LootItem.Material("components", 1) },
                NewVillageElectricalBoxTitle, NewVillageElectricalBoxNarrative),

            // 工头储物柜(工地深处)：稍杂但仍薄（2 件，非原"集中一柜"的封顶奖励）。
            NewVillageForemanLockerId when NotYet(flags, NewVillageForemanLockerFlag) => new CacheResult(
                NewVillageForemanLockerFlag,
                new[] { LootItem.Material("components", 1), LootItem.Material("scrap_metal", 2) },
                NewVillageForemanLockerTitle, NewVillageForemanLockerNarrative),

            // 老屋区(北/深, 11·一户户翻)：已入住老户的家户食物/旧布/日用杂物，最深一处药箱＝偶发单件药品(抗生素)。
            NewVillageOldKitchenId when NotYet(flags, NewVillageOldKitchenFlag) => new CacheResult(
                NewVillageOldKitchenFlag,
                new[] { LootItem.Food(1), LootItem.Material("cloth", 1) },
                NewVillageOldKitchenTitle, NewVillageOldKitchenNarrative),

            NewVillageOldWardrobeId when NotYet(flags, NewVillageOldWardrobeFlag) => new CacheResult(
                NewVillageOldWardrobeFlag,
                new[] { LootItem.Material("cloth", 2) },
                NewVillageOldWardrobeTitle, NewVillageOldWardrobeNarrative),

            NewVillageRootCellarId when NotYet(flags, NewVillageRootCellarFlag) => new CacheResult(
                NewVillageRootCellarFlag,
                new[] { LootItem.Food(1), LootItem.Material("wood", 1) },
                NewVillageRootCellarTitle, NewVillageRootCellarNarrative),

            NewVillageOldHallId when NotYet(flags, NewVillageOldHallFlag) => new CacheResult(
                NewVillageOldHallFlag,
                new[] { LootItem.Material("scrap_cloth", 1), LootItem.Material("rope", 1) },
                NewVillageOldHallTitle, NewVillageOldHallNarrative),

            NewVillageOldUnderbedId when NotYet(flags, NewVillageOldUnderbedFlag) => new CacheResult(
                NewVillageOldUnderbedFlag,
                new[] { LootItem.Material("nails", 1), LootItem.Material("cloth", 1) },
                NewVillageOldUnderbedTitle, NewVillageOldUnderbedNarrative),

            NewVillageOldAtticId when NotYet(flags, NewVillageOldAtticFlag) => new CacheResult(
                NewVillageOldAtticFlag,
                new[] { LootItem.Material("wire", 1), LootItem.Material("scrap_cloth", 1) },
                NewVillageOldAtticTitle, NewVillageOldAtticNarrative),

            NewVillageOld2KitchenId when NotYet(flags, NewVillageOld2KitchenFlag) => new CacheResult(
                NewVillageOld2KitchenFlag,
                new[] { LootItem.Food(1), LootItem.Material("bandage", 1) },
                NewVillageOld2KitchenTitle, NewVillageOld2KitchenNarrative),

            NewVillageOld2WoodshedId when NotYet(flags, NewVillageOld2WoodshedFlag) => new CacheResult(
                NewVillageOld2WoodshedFlag,
                new[] { LootItem.Material("wood", 2), LootItem.Material("nails", 1) },
                NewVillageOld2WoodshedTitle, NewVillageOld2WoodshedNarrative),

            NewVillageOld2YardId when NotYet(flags, NewVillageOld2YardFlag) => new CacheResult(
                NewVillageOld2YardFlag,
                new[] { LootItem.Material("scrap_metal", 1), LootItem.Material("bone", 1) },
                NewVillageOld2YardTitle, NewVillageOld2YardNarrative),

            NewVillageOld2ShrineId when NotYet(flags, NewVillageOld2ShrineFlag) => new CacheResult(
                NewVillageOld2ShrineFlag,
                new[] { LootItem.Food(1), LootItem.Material("cloth", 1) },
                NewVillageOld2ShrineTitle, NewVillageOld2ShrineNarrative),

            NewVillageOld2MedCabId when NotYet(flags, NewVillageOld2MedCabFlag) => new CacheResult(
                NewVillageOld2MedCabFlag,
                new[] { LootItem.Material("antibiotics", 1), LootItem.Material("rosehip", 1) }, // [SPEC-B14] 老屋药柜里存的干玫瑰果
                NewVillageOld2MedCabTitle, NewVillageOld2MedCabNarrative),

            // —— [SPEC-B13·拟设定待确认] 加油站（燃油大户：fuel 为主要产出；便利店食品少量+修车零件；draft 待用户改）——
            // 加油区(近)：加油岛/收银亭，油枪残油+零食。
            GasPumpIslandId when NotYet(flags, GasPumpIslandFlag) => new CacheResult(
                GasPumpIslandFlag,
                new[] { LootItem.Material("fuel", 3), LootItem.Material("dandelion", 1) }, // [SPEC-B14] 泵岛裂缝里钻出的蒲公英
                GasPumpIslandTitle, GasPumpIslandNarrative),

            GasKioskId when NotYet(flags, GasKioskFlag) => new CacheResult(
                GasKioskFlag,
                new[] { LootItem.Food(1), LootItem.Material("fuel", 1), LootItem.Material("scrap_metal", 1) },
                GasKioskTitle, GasKioskNarrative),

            // 便利店(中/食品少量)：零食/冷饮/里屋。
            GasStoreSnacksId when NotYet(flags, GasStoreSnacksFlag) => new CacheResult(
                GasStoreSnacksFlag,
                new[] { LootItem.Food(2) },
                GasStoreSnacksTitle, GasStoreSnacksNarrative),

            GasStoreDrinksId when NotYet(flags, GasStoreDrinksFlag) => new CacheResult(
                GasStoreDrinksFlag,
                new[] { LootItem.Food(2) },
                GasStoreDrinksTitle, GasStoreDrinksNarrative),

            GasStoreBackroomId when NotYet(flags, GasStoreBackroomFlag) => new CacheResult(
                GasStoreBackroomFlag,
                new[] { LootItem.Food(1), LootItem.Material("cloth", 1), LootItem.Material("bandage", 1) },
                GasStoreBackroomTitle, GasStoreBackroomNarrative),

            // 修车棚(中/工具零件)：工位/零件货架/机油货架。
            GasRepairBayId when NotYet(flags, GasRepairBayFlag) => new CacheResult(
                GasRepairBayFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("components", 1) },
                GasRepairBayTitle, GasRepairBayNarrative),

            GasPartsShelfId when NotYet(flags, GasPartsShelfFlag) => new CacheResult(
                GasPartsShelfFlag,
                new[] { LootItem.Material("components", 2), LootItem.Material("wire", 1) },
                GasPartsShelfTitle, GasPartsShelfNarrative),

            GasOilRackId when NotYet(flags, GasOilRackFlag) => new CacheResult(
                GasOilRackFlag,
                new[] { LootItem.Material("fuel", 2), LootItem.Material("scrap_metal", 1) },
                GasOilRackTitle, GasOilRackNarrative),

            // 油罐区(深/燃油大户高价值)：油罐车/地下储油间。
            GasTankerId when NotYet(flags, GasTankerFlag) => new CacheResult(
                GasTankerFlag,
                new[] { LootItem.Material("fuel", 4) },
                GasTankerTitle, GasTankerNarrative),

            GasUndergroundTankId when NotYet(flags, GasUndergroundTankFlag) => new CacheResult(
                GasUndergroundTankFlag,
                new[] { LootItem.Material("fuel", 5), LootItem.Material("components", 1) },
                GasUndergroundTankTitle, GasUndergroundTankNarrative),

            // —— [SPEC-B13] 超市（外围货架残余·食物稍多但单点薄；内圈幸存者囤货·打赢才拿；draft 待用户改）——
            // 外围(7)：卖场/仓储/后巷。
            SupermarketCheckoutId when NotYet(flags, SupermarketCheckoutFlag) => new CacheResult(
                SupermarketCheckoutFlag,
                new[] { LootItem.Food(1), LootItem.Material("cloth", 1) },
                SupermarketCheckoutTitle, SupermarketCheckoutNarrative),

            SupermarketSnackAisleId when NotYet(flags, SupermarketSnackAisleFlag) => new CacheResult(
                SupermarketSnackAisleFlag,
                new[] { LootItem.Food(2) },
                SupermarketSnackAisleTitle, SupermarketSnackAisleNarrative),

            SupermarketCannedAisleId when NotYet(flags, SupermarketCannedAisleFlag) => new CacheResult(
                SupermarketCannedAisleFlag,
                new[] { LootItem.Food(2) },
                SupermarketCannedAisleTitle, SupermarketCannedAisleNarrative),

            SupermarketHouseholdId when NotYet(flags, SupermarketHouseholdFlag) => new CacheResult(
                SupermarketHouseholdFlag,
                new[] { LootItem.Material("cloth", 2), LootItem.Material("rope", 1) },
                SupermarketHouseholdTitle, SupermarketHouseholdNarrative),

            SupermarketHardwareId when NotYet(flags, SupermarketHardwareFlag) => new CacheResult(
                SupermarketHardwareFlag,
                new[] { LootItem.Material("nails", 2), LootItem.Material("wire", 1), LootItem.Material("scrap_metal", 1) },
                SupermarketHardwareTitle, SupermarketHardwareNarrative),

            SupermarketStockroomId when NotYet(flags, SupermarketStockroomFlag) => new CacheResult(
                SupermarketStockroomFlag,
                new[] { LootItem.Food(1), LootItem.Material("fuel", 1), LootItem.Material("scrap_metal", 1) },
                SupermarketStockroomTitle, SupermarketStockroomNarrative),

            SupermarketBackAlleyId when NotYet(flags, SupermarketBackAlleyFlag) => new CacheResult(
                SupermarketBackAlleyFlag,
                new[] { LootItem.Material("scrap_metal", 2), LootItem.Material("fuel", 1) },
                SupermarketBackAlleyTitle, SupermarketBackAlleyNarrative),

            // 内圈·幸存者囤货(4, 打赢/闯入才拿；量稍厚但仍克制)：
            SupermarketHoardFoodId when NotYet(flags, SupermarketHoardFoodFlag) => new CacheResult(
                SupermarketHoardFoodFlag,
                new[] { LootItem.Food(3) },
                SupermarketHoardFoodTitle, SupermarketHoardFoodNarrative),

            SupermarketHoardMedsId when NotYet(flags, SupermarketHoardMedsFlag) => new CacheResult(
                SupermarketHoardMedsFlag,
                new[] { LootItem.Material("bandage", 2), LootItem.Material("first_aid_kit", 1) },
                SupermarketHoardMedsTitle, SupermarketHoardMedsNarrative),

            SupermarketHoardGearId when NotYet(flags, SupermarketHoardGearFlag) => new CacheResult(
                SupermarketHoardGearFlag,
                new[] { LootItem.Material("cloth", 2), LootItem.Material("leather", 1), LootItem.Material("components", 1) },
                SupermarketHoardGearTitle, SupermarketHoardGearNarrative),

            SupermarketHoardStashId when NotYet(flags, SupermarketHoardStashFlag) => new CacheResult(
                SupermarketHoardStashFlag,
                new[] { LootItem.Material("silver", Silver.FromWhole(3)), LootItem.Material("fuel", 1) },
                SupermarketHoardStashTitle, SupermarketHoardStashNarrative),

            // —— [SPEC-B13] 医院（丧尸巢废墟·高风险高收益；非医疗区克制，医疗集中药房/手术层——打破"禁医疗灌水"的例外点；draft 待用户改）——
            // 门诊/急诊大厅(近, 7·非医疗为主)：
            HospitalReceptionId when NotYet(flags, HospitalReceptionFlag) => new CacheResult(
                HospitalReceptionFlag,
                new[] { LootItem.Material("cloth", 1) },
                HospitalReceptionTitle, HospitalReceptionNarrative),

            HospitalTriageId when NotYet(flags, HospitalTriageFlag) => new CacheResult(
                HospitalTriageFlag,
                new[] { LootItem.Material("bandage", 1) },
                HospitalTriageTitle, HospitalTriageNarrative),

            HospitalWaitingRoomId when NotYet(flags, HospitalWaitingRoomFlag) => new CacheResult(
                HospitalWaitingRoomFlag,
                new[] { LootItem.Food(1) },
                HospitalWaitingRoomTitle, HospitalWaitingRoomNarrative),

            HospitalVendingId when NotYet(flags, HospitalVendingFlag) => new CacheResult(
                HospitalVendingFlag,
                new[] { LootItem.Food(2) },
                HospitalVendingTitle, HospitalVendingNarrative),

            HospitalErTrolleyId when NotYet(flags, HospitalErTrolleyFlag) => new CacheResult(
                HospitalErTrolleyFlag,
                new[] { LootItem.Material("bandage", 1), LootItem.Material("needle_thread", 1) },
                HospitalErTrolleyTitle, HospitalErTrolleyNarrative),

            HospitalSecurityId when NotYet(flags, HospitalSecurityFlag) => new CacheResult(
                HospitalSecurityFlag,
                new[] { LootItem.Material("scrap_metal", 1), LootItem.Material("wire", 1) },
                HospitalSecurityTitle, HospitalSecurityNarrative),

            HospitalCafeteriaId when NotYet(flags, HospitalCafeteriaFlag) => new CacheResult(
                HospitalCafeteriaFlag,
                new[] { LootItem.Food(2) },
                HospitalCafeteriaTitle, HospitalCafeteriaNarrative),

            // 住院部(中, 8)：
            HospitalWardLinenId when NotYet(flags, HospitalWardLinenFlag) => new CacheResult(
                HospitalWardLinenFlag,
                new[] { LootItem.Material("cloth", 2), LootItem.Material("scrap_cloth", 1) },
                HospitalWardLinenTitle, HospitalWardLinenNarrative),

            HospitalWardLockerId when NotYet(flags, HospitalWardLockerFlag) => new CacheResult(
                HospitalWardLockerFlag,
                new[] { LootItem.Food(1), LootItem.Material("bandage", 1) },
                HospitalWardLockerTitle, HospitalWardLockerNarrative),

            HospitalNurseStationId when NotYet(flags, HospitalNurseStationFlag) => new CacheResult(
                HospitalNurseStationFlag,
                new[] { LootItem.Material("bandage", 2), LootItem.Material("medicine", 1) },
                HospitalNurseStationTitle, HospitalNurseStationNarrative),

            HospitalDoctorOfficeId when NotYet(flags, HospitalDoctorOfficeFlag) => new CacheResult(
                HospitalDoctorOfficeFlag,
                new[] { LootItem.Material("medicine", 1), LootItem.Material("components", 1) },
                HospitalDoctorOfficeTitle, HospitalDoctorOfficeNarrative),

            HospitalDirtyUtilityId when NotYet(flags, HospitalDirtyUtilityFlag) => new CacheResult(
                HospitalDirtyUtilityFlag,
                new[] { LootItem.Material("scrap_cloth", 2) },
                HospitalDirtyUtilityTitle, HospitalDirtyUtilityNarrative),

            HospitalKitchenetteId when NotYet(flags, HospitalKitchenetteFlag) => new CacheResult(
                HospitalKitchenetteFlag,
                new[] { LootItem.Food(1) },
                HospitalKitchenetteTitle, HospitalKitchenetteNarrative),

            HospitalFloorStoreId when NotYet(flags, HospitalFloorStoreFlag) => new CacheResult(
                HospitalFloorStoreFlag,
                new[] { LootItem.Material("cloth", 1), LootItem.Material("wire", 1), LootItem.Material("nails", 1) },
                HospitalFloorStoreTitle, HospitalFloorStoreNarrative),

            HospitalMorgueId when NotYet(flags, HospitalMorgueFlag) => new CacheResult(
                HospitalMorgueFlag,
                new[] { LootItem.Material("bone", 2), LootItem.Material("medicine", 1) },
                HospitalMorgueTitle, HospitalMorgueNarrative),

            // 药房(深, 7·医疗集中——高价值)：
            HospitalPharmacyCounterId when NotYet(flags, HospitalPharmacyCounterFlag) => new CacheResult(
                HospitalPharmacyCounterFlag,
                new[] { LootItem.Material("medicine", 2), LootItem.Material("bandage", 2) },
                HospitalPharmacyCounterTitle, HospitalPharmacyCounterNarrative),

            HospitalPharmacyShelfId when NotYet(flags, HospitalPharmacyShelfFlag) => new CacheResult(
                HospitalPharmacyShelfFlag,
                new[] { LootItem.Material("antibiotics", 1), LootItem.Material("medicine", 1) },
                HospitalPharmacyShelfTitle, HospitalPharmacyShelfNarrative),

            HospitalPharmacyFridgeId when NotYet(flags, HospitalPharmacyFridgeFlag) => new CacheResult(
                HospitalPharmacyFridgeFlag,
                new[] { LootItem.Material("antibiotics", 1), LootItem.Material("first_aid_kit", 1) },
                HospitalPharmacyFridgeTitle, HospitalPharmacyFridgeNarrative),

            HospitalPharmacyBackId when NotYet(flags, HospitalPharmacyBackFlag) => new CacheResult(
                HospitalPharmacyBackFlag,
                new[] { LootItem.Material("antibiotics", 2) },
                HospitalPharmacyBackTitle, HospitalPharmacyBackNarrative),

            HospitalNarcoticsCabinetId when NotYet(flags, HospitalNarcoticsCabinetFlag) => new CacheResult(
                HospitalNarcoticsCabinetFlag,
                new[] { LootItem.Material("medicine", 2), LootItem.Material("antibiotics", 1) },
                HospitalNarcoticsCabinetTitle, HospitalNarcoticsCabinetNarrative),

            HospitalDispensaryId when NotYet(flags, HospitalDispensaryFlag) => new CacheResult(
                HospitalDispensaryFlag,
                new[] { LootItem.Material("needle_thread", 2), LootItem.Material("bandage", 2) },
                HospitalDispensaryTitle, HospitalDispensaryNarrative),

            HospitalMedSupplyRoomId when NotYet(flags, HospitalMedSupplyRoomFlag) => new CacheResult(
                HospitalMedSupplyRoomFlag,
                new[] { LootItem.Material("splint", 1), LootItem.Material("bandage", 2), LootItem.Material("needle_thread", 1) },
                HospitalMedSupplyRoomTitle, HospitalMedSupplyRoomNarrative),

            // 手术层(最深, 8·手术耗材+高价值医疗，最高风险最高收益)：
            HospitalOrScrubId when NotYet(flags, HospitalOrScrubFlag) => new CacheResult(
                HospitalOrScrubFlag,
                new[] { LootItem.Material("needle_thread", 2), LootItem.Material("splint", 1) },
                HospitalOrScrubTitle, HospitalOrScrubNarrative),

            HospitalOrTheatreId when NotYet(flags, HospitalOrTheatreFlag) => new CacheResult(
                HospitalOrTheatreFlag,
                new[] { LootItem.Material("first_aid_kit", 1), LootItem.Material("needle_thread", 1) },
                HospitalOrTheatreTitle, HospitalOrTheatreNarrative),

            HospitalSterileStoreId when NotYet(flags, HospitalSterileStoreFlag) => new CacheResult(
                HospitalSterileStoreFlag,
                new[] { LootItem.Material("first_aid_kit", 1), LootItem.Material("splint", 1), LootItem.Material("bandage", 2) },
                HospitalSterileStoreTitle, HospitalSterileStoreNarrative),

            HospitalIcuId when NotYet(flags, HospitalIcuFlag) => new CacheResult(
                HospitalIcuFlag,
                new[] { LootItem.Material("antibiotics", 1), LootItem.Material("medicine", 1), LootItem.Material("components", 1) },
                HospitalIcuTitle, HospitalIcuNarrative),

            HospitalBloodBankId when NotYet(flags, HospitalBloodBankFlag) => new CacheResult(
                HospitalBloodBankFlag,
                new[] { LootItem.Material("first_aid_kit", 1), LootItem.Material("medicine", 1) },
                HospitalBloodBankTitle, HospitalBloodBankNarrative),

            HospitalAnesthesiaId when NotYet(flags, HospitalAnesthesiaFlag) => new CacheResult(
                HospitalAnesthesiaFlag,
                new[] { LootItem.Material("medicine", 2), LootItem.Material("components", 1) },
                HospitalAnesthesiaTitle, HospitalAnesthesiaNarrative),

            HospitalSterilizerId when NotYet(flags, HospitalSterilizerFlag) => new CacheResult(
                HospitalSterilizerFlag,
                new[] { LootItem.Material("splint", 2), LootItem.Material("scrap_metal", 1) },
                HospitalSterilizerTitle, HospitalSterilizerNarrative),

            HospitalChiefSafeId when NotYet(flags, HospitalChiefSafeFlag) => new CacheResult(
                HospitalChiefSafeFlag,
                new[] { LootItem.Material("antibiotics", 2), LootItem.Material("first_aid_kit", 1), LootItem.Material("splint", 1) },
                HospitalChiefSafeTitle, HospitalChiefSafeNarrative),

            // —— [SPEC-B13] 南丁格尔的小药店（基础药品/绷带为主但量薄；大头药品在医院。掉落 draft 待用户改）——
            PharmacyCounterId when NotYet(flags, PharmacyCounterFlag) => new CacheResult(
                PharmacyCounterFlag,
                new[] { LootItem.Material("bandage", 2), LootItem.Food(1) },
                PharmacyCounterTitle, PharmacyCounterNarrative),

            PharmacyShelfId when NotYet(flags, PharmacyShelfFlag) => new CacheResult(
                PharmacyShelfFlag,
                new[] { LootItem.Material("medicine", 1), LootItem.Material("bandage", 1), LootItem.Material("cloth", 1) },
                PharmacyShelfTitle, PharmacyShelfNarrative),

            PharmacyDispensaryId when NotYet(flags, PharmacyDispensaryFlag) => new CacheResult(
                PharmacyDispensaryFlag,
                new[] { LootItem.Material("antibiotics", 1), LootItem.Material("medicine", 1), LootItem.Material("needle_thread", 1) },
                PharmacyDispensaryTitle, PharmacyDispensaryNarrative),

            PharmacyColdBoxId when NotYet(flags, PharmacyColdBoxFlag) => new CacheResult(
                PharmacyColdBoxFlag,
                new[] { LootItem.Material("first_aid_kit", 1), LootItem.Material("antibiotics", 1) },
                PharmacyColdBoxTitle, PharmacyColdBoxNarrative),

            PharmacyAtticId when NotYet(flags, PharmacyAtticFlag) => new CacheResult(
                PharmacyAtticFlag,
                new[] { LootItem.Material("bandage", 1), LootItem.Material("scrap_cloth", 2), LootItem.Material("components", 1) },
                PharmacyAtticTitle, PharmacyAtticNarrative),

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

    // —— 金手指帮根据地（中型·战斗为主；帮派储备语义，draft 待用户改）——
    private const string GoldfingerCheckpointTitle = "门口岗哨掩体";
    private const string GoldfingerCheckpointNarrative = "帮派在寨门口垒的沙袋掩体，射击孔后散着打空的弹壳，扒出几块碎铁和一小包没用完的火药。";
    private const string GoldfingerYardWreckTitle = "前院废车堆";
    private const string GoldfingerYardWreckNarrative = "前院当路障用的报废车堆成一片，拆下几块车壳碎金属，油箱里还能抽出点柴油。";
    private const string GoldfingerBunksTitle = "帮众铺位";
    private const string GoldfingerBunksNarrative = "一排帮众打地铺的通铺，脏被褥和换洗衣物堆得乱七八糟，扯下些还能用的布料。";
    private const string GoldfingerAmmoCrateTitle = "弹药箱";
    private const string GoldfingerAmmoCrateNarrative = "墙角摞着几只帮派的弹药箱，成品弹早搬空了，箱底还剩复装用的火药和几个撞针零件。";
    private const string GoldfingerGunBenchTitle = "修械台";
    private const string GoldfingerGunBenchNarrative = "帮里修枪的工作台，虎钳上还夹着支拆一半的枪，散落的枪机零件和钢料能拆走。";
    private const string GoldfingerHidePileTitle = "皮件堆";
    private const string GoldfingerHidePileNarrative = "帮众抢来的皮货堆在角落，几张鞣好的皮革和裁剩的布料还整齐，是做绑带护具的好料。";
    private const string GoldfingerFuelStashTitle = "油料桶";
    private const string GoldfingerFuelStashNarrative = "帮派囤的油料桶排在墙边，晃一晃还满，接出两桶柴油，旁边缠着几卷电线。";
    private const string GoldfingerArmoryTitle = "军械柜";
    private const string GoldfingerArmoryNarrative = "打到根据地深处，帮派的军械柜就锁在这——撬开柜门，一支还能打的冲锋枪斜靠在里头，底下压着复装火药和几个枪械零件。这是拿命换来的。";
    private const string GoldfingerBossSafeTitle = "头目保险柜";
    private const string GoldfingerBossSafeNarrative = "头目屋里那口老保险柜被人尝试撬过没成，这回连柜带砸——里头码着一小摞白银硬通货和几个精密零件，是帮派的家底。";
    private const string GoldfingerSilverCacheTitle = "银库暗格";
    private const string GoldfingerSilverCacheNarrative = "头目床板下还藏着个暗格，掀开是又一小袋白银和一卷上好的皮革——连自己人都没让看的私房。";
    private const string GoldfingerBossMedkitTitle = "头目急救箱";
    private const string GoldfingerBossMedkitNarrative = "头目枕边一只上锁的急救箱，帮里的伤药都攥在他手上——一只完整的急救包和一卷绷带，是这趟血战里难得的医疗补给。";

    // —— [SPEC-B13-补3·拟设定待确认] 东部新村（半建成迁建安置区，30 处·杂而薄·一户户翻；draft 待用户改）——
    // 排屋区(南/近, 11)：
    private const string NewVillageShowroomTitle = "样板间客厅";
    private const string NewVillageShowroomNarrative = "排屋最外一间是留给看房人的样板间，家具还包着塑料膜。抱枕的布套能拆下来当布料，角落一台没装完的排风扇还能拆点薄钢皮——搬迁的日子谁也没等到。";
    private const string NewVillageRowKitchenTitle = "A户厨房";
    private const string NewVillageRowKitchenNarrative = "已经交钥匙的那户，厨房里还留着开火过日子的痕迹。灶台边的吊柜里剩着一小罐没动过的干货，抽屉底压着几块叠好的抹布。";
    private const string NewVillageRowAWardrobeTitle = "A户衣柜";
    private const string NewVillageRowAWardrobeNarrative = "卧室的衣柜门半开着，里头的衣物被翻乱又没带走，一层叠一层。挑几件结实的棉布卷起来——冷起来能救命。";
    private const string NewVillageRowAUnderbedTitle = "A户床底";
    private const string NewVillageRowAUnderbedNarrative = "床底下拖出个纸箱，落满灰。里头是拆下来没扔的旧床单和一小把装修剩的钉子，混在一起。";
    private const string NewVillageRowBKitchenTitle = "B户厨房";
    private const string NewVillageRowBKitchenNarrative = "隔壁那户的厨房收拾得利索，走得却急。米缸里还剩小半袋米，墙上的小药箱没关，掉出一卷绷带。";
    private const string NewVillageRowBBalconyTitle = "B户阳台";
    private const string NewVillageRowBBalconyNarrative = "封闭阳台改成了小储物间，晾衣绳还横着。工具筐里剩一小卷电线和一段没用完的尼龙绳。";
    private const string NewVillageRowBClosetTitle = "B户储物间";
    private const string NewVillageRowBClosetNarrative = "入户旁的储物间塞满了搬家没拆的箱子，最上头一箱是装修余料——半盒木螺钉和一截修家具的木条。";
    private const string NewVillageUnfinishedTitle = "半成品单元";
    private const string NewVillageUnfinishedNarrative = "这一间还没封顶，水泥地上散着施工没收的料。踢脚线的木条码成小垛，散落一地的铁钉硌脚——工期停在了某个再没人回来续上的早晨。";
    private const string NewVillageRowCShoeCabTitle = "C户玄关鞋柜";
    private const string NewVillageRowCShoeCabNarrative = "玄关的鞋柜倒了，鞋子滚了一地。夹层里塞着换季的旧衣，柜体的铝合金边框也能撬下几块。";
    private const string NewVillageRowCBathTitle = "C户卫生间";
    private const string NewVillageRowCBathNarrative = "卫生间的镜柜没关，里头日用品被扫空了大半，只剩角落一卷还没拆封的医用绷带——搬家时漏下的。";
    private const string NewVillageRowDBalconyTitle = "D户阳台杂物";
    private const string NewVillageRowDBalconyNarrative = "这户的阳台堆着杂物，纸箱摞到齐腰。翻出一卷接线板拆下的电线，还有一段能改螺丝的金属条。";
    // 工地区(中, 8·维持偏建材)：
    private const string NewVillageLumberYardTitle = "料场木料垛";
    private const string NewVillageLumberYardNarrative = "工地料场堆着方木和模板，用防雨布勉强盖着，边角泡得发黑，芯子还干。捡两根干的，顺走垛顶捆料的粗绳。";
    private const string NewVillageScaffoldTitle = "脚手架下料箱";
    private const string NewVillageScaffoldNarrative = "半拉子的脚手架斜插进楼体，架下扣件和钢管的料箱翻倒了一地。扒出几块碎钢皮和一小把还没受潮的铁钉。";
    private const string NewVillageToolShedTitle = "工地工具棚";
    private const string NewVillageToolShedNarrative = "工具棚的挂钩上空了大半，值钱的电动家伙早被人顺走。剩下角落里一卷电线和一捆尼龙绳，凑合着也够修补一阵。";
    private const string NewVillageRebarPileTitle = "钢筋碎料堆";
    private const string NewVillageRebarPileNarrative = "切割区堆着截剩的钢筋头和扎丝，锈成一片红褐。挑出些还能回炉的碎金属——生锈归生锈，打磨过照样是材料。";
    private const string NewVillageSiteOfficeTitle = "项目部工棚";
    private const string NewVillageSiteOfficeNarrative = "蓝白铁皮的项目部工棚，墙上还钉着泛黄的施工进度表。抽屉里剩着监理没吃完的干粮，仪表盘后拆出个电子元件。";
    private const string NewVillageCementPileTitle = "水泥料堆";
    private const string NewVillageCementPileNarrative = "堆放区码着受潮结块的水泥袋，没法用了。压在袋子底下的是一小盒盖钉和几根裁短的木方——这倒还能拿。";
    private const string NewVillageElectricalBoxTitle = "临时配电箱";
    private const string NewVillageElectricalBoxNarrative = "工地临时配电箱被撬开过，铜排早没了。剩下一圈没拆完的线束和一个还完好的接触器模块。";
    private const string NewVillageForemanLockerTitle = "工头储物柜";
    private const string NewVillageForemanLockerNarrative = "项目部里屋一排储物柜，工头那格撬开——比别处稍像样些：一个精密元件压着几块成色好的碎钢。也就这点私藏，算不上什么家底。";
    // 老屋区(北/深, 11·一户户翻)：
    private const string NewVillageOldKitchenTitle = "老屋灶间";
    private const string NewVillageOldKitchenNarrative = "拆迁范围里剩下没搬的几户老屋，灶间的土灶还是老样子。碗柜深处一小袋粮食没舍得带走，抽屉里压着叠得整齐的旧布。";
    private const string NewVillageOldWardrobeTitle = "老屋卧室衣柜";
    private const string NewVillageOldWardrobeNarrative = "老两口的卧室，樟木衣柜里叠满了留给孙辈的衣物，一层压一层。挑几件厚实的棉布抱走，别的都嫌旧。";
    private const string NewVillageRootCellarTitle = "老屋菜窖";
    private const string NewVillageRootCellarNarrative = "后院一个盖着木板的地窖，掀开一股土腥气。窖里剩点过冬的红薯，还有几截垫底防潮的干木料。";
    private const string NewVillageOldHallTitle = "老屋堂屋";
    private const string NewVillageOldHallNarrative = "堂屋正中还挂着中堂画，八仙桌上一层灰。条案下塞着卷起来的旧棉絮和一盘捆柴的草绳——过日子的零碎。";
    private const string NewVillageOldUnderbedTitle = "老屋床底";
    private const string NewVillageOldUnderbedNarrative = "老式木床底下藏着家什，扒开落灰的布帘：一小盒补墙的洋钉，一床拆洗过的旧被面，卷得整整齐齐。";
    private const string NewVillageOldAtticTitle = "老屋阁楼";
    private const string NewVillageOldAtticNarrative = "顺着木梯上到低矮的阁楼，蛛网糊脸。堆着的旧物里翻出一卷电线和几块能撕来引火的破布。";
    private const string NewVillageOld2KitchenTitle = "二号老屋厨房";
    private const string NewVillageOld2KitchenNarrative = "另一户老屋的厨房，灶膛还留着没烧尽的柴灰。碗架上剩一袋挂面，窗台的旧药盒里翻出一卷绷带。";
    private const string NewVillageOld2WoodshedTitle = "二号老屋柴房";
    private const string NewVillageOld2WoodshedNarrative = "贴着厨房的柴房码着劈好的木柴，干得很。墙角一个铁皮罐里装着杂七杂八的钉子，也一并收了。";
    private const string NewVillageOld2YardTitle = "二号老屋院子";
    private const string NewVillageOld2YardNarrative = "小院里晾着没收的农具，锈了。墙根堆着废铁，还有一小捆晒干的牲口骨头——不知留着做什么，倒是能用。";
    private const string NewVillageOld2ShrineTitle = "老屋神龛";
    private const string NewVillageOld2ShrineNarrative = "堂屋角落供着个小神龛，香炉里插着烧尽的残香。供台上摆着没撤的供品——几个还没坏的干果，压着一块红布。";
    private const string NewVillageOld2MedCabTitle = "老屋药箱";
    private const string NewVillageOld2MedCabNarrative = "最里那户的床头柜上放着个铁皮药箱，锁扣锈死了，撬开——常用药早被翻走，底层压着一板没拆封的抗生素。这片住宅区里，难得的一件药。";

    // —— [SPEC-B13·拟设定待确认] 加油站（公路加油站·燃油大户；draft 待用户改）——
    private const string GasPumpIslandTitle = "加油岛油枪";
    private const string GasPumpIslandNarrative = "加油岛的几支油枪还挂在架上，胶管里存着抽不尽的余油。撬开加油机侧板，从分液管里放出几桶还算干净的燃油——这地方最不缺的就是它。";
    private const string GasKioskTitle = "收银亭";
    private const string GasKioskNarrative = "加油区中间的收银亭，玻璃被砸穿了。收银台下没搜空的货架上还剩点零食，抽屉里几只打火机的油壶和一堆能拆的薄铁皮。";
    private const string GasStoreSnacksTitle = "便利店零食货架";
    private const string GasStoreSnacksNarrative = "便利店的货架大半被扫空，翻倒在地。角落几包挂面和真空熟食滚到了货架底下，逃难的人手忙脚乱，没顾上弯腰去捡。";
    private const string GasStoreDrinksTitle = "冷饮柜";
    private const string GasStoreDrinksNarrative = "断电很久的冷饮柜里，瓶装水和饮料温吞吞地泡在化开又回潮的柜底。挑出几瓶没胀气的，够路上喝一阵。";
    private const string GasStoreBackroomTitle = "便利店里屋";
    private const string GasStoreBackroomNarrative = "便利店后头的里屋是店员歇脚的地方，行军床还铺着。纸箱里剩点囤货干粮和一叠抹布，急救盒里翻出一卷绷带。";
    private const string GasRepairBayTitle = "修车工位";
    private const string GasRepairBayNarrative = "修车棚的地沟工位上还架着一台没修完的车，工具车翻倒在旁。地上散着拆下的碎金属件和几个还能用的机械零件。";
    private const string GasPartsShelfTitle = "零件货架";
    private const string GasPartsShelfNarrative = "修车棚靠墙的零件货架，标签盒歪歪扭扭。翻出几个通用的机械元件和一卷线束——修车修不成，拿去改装台倒正好。";
    private const string GasOilRackTitle = "机油货架";
    private const string GasOilRackNarrative = "货架上码着成排的机油和润滑油罐，不少还是满的。倒进桶里能当燃料应急，架子本身的铁皮也顺手撬了两块。";
    private const string GasTankerTitle = "油罐车";
    private const string GasTankerNarrative = "场子里停着一辆没开走的油罐车，罐体上的液位窗还照出半罐油。接上底阀的放油口，哗哗地灌满了好几桶——够火堆和油灯烧上很久。";
    private const string GasUndergroundTankTitle = "地下储油间";
    private const string GasUndergroundTankNarrative = "掀开加油区地面的检修盖，底下是站里的地下储油罐间。人工泵还能压，一桶接一桶把余油全抽了上来，泵房角落还拆出几个计量表的元件。这一趟，光燃油就装满了半个背包。";

    // —— [SPEC-B13] 超市（外围货架残余·内圈幸存者囤货；draft 待用户改）——
    private const string SupermarketCheckoutTitle = "外围·收银台前区";
    private const string SupermarketCheckoutNarrative = "一排收银机被撬得七零八落，钱早没了意义。台面抽屉里翻出一两包压在最底下的糖果，货架尽头还挂着卷没人要的购物袋——布料能用。";
    private const string SupermarketSnackAisleTitle = "外围·零食货架";
    private const string SupermarketSnackAisleNarrative = "零食区被扫荡过好几遍，成排的货架空空荡荡。趴下身在最底层的缝隙里，还能摸出几包滚落进去、被遗忘的干粮。";
    private const string SupermarketCannedAisleTitle = "外围·罐头货架";
    private const string SupermarketCannedAisleNarrative = "罐头区是末世里最先被抢空的地方。翻遍倒地的货架，几只滚到角落、标签泡烂但没鼓胀的罐头还能吃。";
    private const string SupermarketHouseholdTitle = "外围·日用百货架";
    private const string SupermarketHouseholdNarrative = "百货区没人稀罕。整幅的桌布、毛巾、还没拆封的晾衣绳——不当吃不当喝，可缝补捆扎样样用得上。";
    private const string SupermarketHardwareTitle = "外围·五金杂货角";
    private const string SupermarketHardwareNarrative = "生活五金的小角落，盒装的铁钉、成卷的细铁丝、几副金属挂钩。撬下货架的钢边，又是一把碎金属。";
    private const string SupermarketStockroomTitle = "外围·仓储区";
    private const string SupermarketStockroomNarrative = "推开卖场后墙的弹簧门是理货的仓储区，成摞的周转箱大多空了。翻检一遍，一箱漏检的存货、叉车油箱里的一点余油、拆下的货架碎铁——聊胜于无。";
    private const string SupermarketBackAlleyTitle = "外围·后巷卸货区";
    private const string SupermarketBackAlleyNarrative = "卸货月台外的后巷堆着压扁的纸箱和废弃的托盘。翻倒的垃圾桶边，拆得下几块金属边角，一台报废叉车里还能抽出点柴油。";
    private const string SupermarketHoardFoodTitle = "内圈·他们的囤粮";
    private const string SupermarketHoardFoodNarrative = "帘子后的小房间里，一整面墙码着他们藏起来的存粮——罐头、干货、瓶装水，分门别类摞得整整齐齐。这是他们不肯与人分的家底，如今易了主。";
    private const string SupermarketHoardMedsTitle = "内圈·他们的药箱";
    private const string SupermarketHoardMedsNarrative = "据点角落一只上锁的铁皮药箱，撬开是他们攒下的应急医疗——几卷绷带和一只齐备的急救包。超市本没有这些，是他们从别处搜刮来、攥在手里的。";
    private const string SupermarketHoardGearTitle = "内圈·缴获装备堆";
    private const string SupermarketHoardGearNarrative = "墙根堆着他们从过路人身上扒下的行头——成幅的布料、一张鞣好的皮革、拆自各处的几个机械零件。每一件背后大概都有个像你一样、轻信了那句招呼的人。";
    private const string SupermarketHoardStashTitle = "内圈·头目私囤";
    private const string SupermarketHoardStashNarrative = "领头那人的铺位底下藏着个暗格，一小袋白银硬通货压在褥子下，旁边还塞着一桶备用燃料。设这个局，图的就是这些。";

    // —— [SPEC-B13] 医院（丧尸巢废墟·医疗集中药房手术层；draft 待用户改）——
    private const string HospitalReceptionTitle = "门诊·挂号台";
    private const string HospitalReceptionNarrative = "门诊大厅的挂号台后一片狼藉，叫号屏碎在地上。散落的病历纸没用，倒是柜子里几幅没拆的分诊分诊布巾还干净。";
    private const string HospitalTriageTitle = "急诊·分诊台";
    private const string HospitalTriageNarrative = "急诊入口的分诊台上，血压计的袖带耷拉着。翻开台面下的抽屉，一卷还封着的绷带滚在最里头。";
    private const string HospitalWaitingRoomTitle = "门诊·候诊区";
    private const string HospitalWaitingRoomNarrative = "成排的候诊椅东倒西歪，当初挤满了求医的人。椅缝和自动售水机底下，还能捡出几瓶没喝完、没被丧尸碰过的水和干粮。";
    private const string HospitalVendingTitle = "大厅·自动贩卖机";
    private const string HospitalVendingNarrative = "大厅角落的贩卖机玻璃被砸开，大半被掏空。伸手够到卡在弹簧最里侧的几样，还是没过期的零食和瓶装饮料。";
    private const string HospitalErTrolleyTitle = "急诊·抢救推车";
    private const string HospitalErTrolleyNarrative = "急诊室的抢救推车翻倒在地，散落一地器械。血迹早已发黑。拾起还能用的——一卷绷带、一套缝合的针线。";
    private const string HospitalSecurityTitle = "大厅·保安室";
    private const string HospitalSecurityNarrative = "大厅的保安值班室门虚掩着，监控墙一片雪花。拆下报废设备的外壳金属和一卷布线用的电线。";
    private const string HospitalCafeteriaTitle = "一层·食堂";
    private const string HospitalCafeteriaNarrative = "职工食堂的后厨还留着没来得及处理的存货。翻遍冷藏失效的橱柜，几样密封的干货和罐头勉强还能入口。";
    private const string HospitalWardLinenTitle = "住院部·布草间";
    private const string HospitalWardLinenNarrative = "病区的布草间摞满了床单被罩，大多还算干净。整幅的棉布和撕开的碎布，是缝补的好料。";
    private const string HospitalWardLockerTitle = "住院部·病床储物柜";
    private const string HospitalWardLockerNarrative = "病房床头的储物柜一个个翻过去，多是住院病人来不及带走的私物。一份没动的病号餐，柜底压着的一卷绷带。";
    private const string HospitalNurseStationTitle = "住院部·护士站";
    private const string HospitalNurseStationNarrative = "护士站的治疗车和药品抽屉还没被彻底搜空——几卷绷带，一板缓解发热的成药。医院深处的东西，开始值钱起来了。";
    private const string HospitalDoctorOfficeTitle = "住院部·医生办公室";
    private const string HospitalDoctorOfficeNarrative = "值班医生的办公室里，处方笺散了一桌。抽屉锁被撬开，几盒样品成药和一台拆得下零件的仪器还在。";
    private const string HospitalDirtyUtilityTitle = "住院部·污物处置间";
    private const string HospitalDirtyUtilityNarrative = "污物间的气味叫人作呕，没人愿意进来翻。屏住气，成捆待洗的旧布还能撕作碎布用。";
    private const string HospitalKitchenetteTitle = "住院部·配餐间";
    private const string HospitalKitchenetteNarrative = "楼层配餐间的推车上，几份封好的病号餐还没发下去。凑近闻了闻，没坏，收进背包。";
    private const string HospitalFloorStoreTitle = "住院部·楼层库房";
    private const string HospitalFloorStoreNarrative = "楼层的杂物库房堆着维修用料，成幅的窗帘布、一卷铁丝、半盒螺钉。医院自己的家底，也不全是药。";
    private const string HospitalMorgueTitle = "地下·太平间";
    private const string HospitalMorgueNarrative = "顺着最阴冷的通道摸到地下太平间，停尸抽屉半开着，里头早已空了——不敢想那些尸体去了哪。冷柜边的柜子里，是一板镇定的成药和几段可用的骨料。";
    private const string HospitalPharmacyCounterTitle = "药房·前台";
    private const string HospitalPharmacyCounterNarrative = "推开药房的卷帘，成排的药架扑面而来——这里是别人不敢深入、你拿命换来的地方。前台货架上还码着大量成药和整箱的绷带。";
    private const string HospitalPharmacyShelfTitle = "药房·处方药架";
    private const string HospitalPharmacyShelfNarrative = "处方药区按字母排得整整齐齐，大半还在。翻出一板紧俏的抗生素和几盒对症的成药——在外头，这些能换命。";
    private const string HospitalPharmacyFridgeTitle = "药房·冷藏药柜";
    private const string HospitalPharmacyFridgeNarrative = "断电已久的冷藏药柜里，需低温保存的药大多失效，唯独一板密封的抗生素和一只封装完好的急救包还顶用。";
    private const string HospitalPharmacyBackTitle = "药房·药库后间";
    private const string HospitalPharmacyBackNarrative = "药房最里的储备药库，一道铁栅门拦着，锁孔里还插着断掉的撬棍——前人没得手。你撬开了。整整两板抗生素码在架上，是这一趟最实在的收获。";
    private const string HospitalNarcoticsCabinetTitle = "药房·管制药柜";
    private const string HospitalNarcoticsCabinetNarrative = "墙上一只双锁的管制药柜，专锁麻醉镇痛之类的强效药。砸开它费了一番功夫——几盒成药，还有一板抗生素混在其中。";
    private const string HospitalDispensaryTitle = "药房·配药室";
    private const string HospitalDispensaryNarrative = "配药室的操作台上摊着没做完的配药活。抽屉里成排的缝合针线和整箱绷带还是无菌封装，正是流血救治的耗材。";
    private const string HospitalMedSupplyRoomTitle = "药房·医材库";
    private const string HospitalMedSupplyRoomNarrative = "医用耗材库房码得像小超市——固定断骨的夹板、成箱绷带、缝合针线。搬空一格，够营地的医疗撑上好一阵。";
    private const string HospitalOrScrubTitle = "手术层·刷手准备间";
    private const string HospitalOrScrubNarrative = "上到手术层，空气里还有消毒水的残味。刷手间的无菌柜里，成套的缝合针线和一副夹板整齐封着，等一台再也不会开始的手术。";
    private const string HospitalOrTheatreTitle = "手术层·手术室";
    private const string HospitalOrTheatreNarrative = "无影灯冷冷地悬在手术台上方，台上的一切早已凝固成暗褐。器械盘边，一只完整的急救包和一套缝合针线还静静躺着——这里的东西，是拿最深的风险换的。";
    private const string HospitalSterileStoreTitle = "手术层·无菌耗材库";
    private const string HospitalSterileStoreNarrative = "手术层的无菌耗材库是整座医院最干净的角落。密封的急救包、夹板、成箱绷带层层码放——高价值，也高风险，越往深处丧尸越密。";
    private const string HospitalIcuTitle = "手术层·ICU 重症监护";
    private const string HospitalIcuNarrative = "ICU 的一整排监护仪早已熄灭，管线垂落。床边药车里剩着一板抗生素和几盒成药，拆得下的监护模块里还有精密零件。";
    private const string HospitalBloodBankTitle = "手术层·血库";
    private const string HospitalBloodBankNarrative = "血库的冷藏架断电后一片狼藉，血袋大多报废。应急柜里倒是留着一只完整急救包和一板成药——最后的储备。";
    private const string HospitalAnesthesiaTitle = "手术层·麻醉科";
    private const string HospitalAnesthesiaNarrative = "麻醉科的药品柜锁得最严，里头是各类镇痛镇静的成药。撬开搜刮一空，连麻醉机上的精密元件也一并拆走。";
    private const string HospitalSterilizerTitle = "手术层·器械灭菌室";
    private const string HospitalSterilizerNarrative = "灭菌室的高压锅炉冷了很久，成套器械泡在失效的消毒液里。挑出还能用的钢制夹板和一块器械托盘的碎金属。";
    private const string HospitalChiefSafeTitle = "手术层·主任药品保险柜";
    private const string HospitalChiefSafeNarrative = "外科主任办公室里那口药品保险柜，锁着全院最金贵的储备——两板抗生素、一只齐备急救包、一副夹板。打到这最深处、砸开这最后一柜，这一趟九死一生才算值了。";

    // —— [SPEC-B13] 南丁格尔的小药店（环境叙事 draft·末日守店氛围；全文待用户验收/优化）——
    private const string PharmacyCounterTitle = "前台收银台";
    private const string PharmacyCounterNarrative = "收银台的抽屉半开着，硬币散了一地，没人要钱了。底下压着一盒拆开的绷带和店员没吃完的几块饼干——她大概是被叫走的，走得很急。";
    private const string PharmacyShelfTitle = "店面货架";
    private const string PharmacyShelfNarrative = "临街的货架早被翻了个底朝天，感冒药、止痛片的空盒踩得粉碎。够不着的最高层还漏下几板常备成药，一卷纱布卡在货架缝里，倒是没人惦记。";
    private const string PharmacyDispensaryTitle = "后屋处方柜";
    private const string PharmacyDispensaryNarrative = "后屋的处方药柜上着锁，锁孔却是干净的——有人一直在用它。里头按字母排得整整齐齐，抗生素只剩最后一盒，旁边搁着消好毒的针线，像是随时准备给谁缝合。";
    private const string PharmacyColdBoxTitle = "后屋冷藏箱";
    private const string PharmacyColdBoxNarrative = "断了电的冷藏箱用棉被裹着，勉强还留着点凉气。里头一个贴着红十字的急救包，压着一板舍不得用的抗生素——守店的人把最要紧的东西，都留到了最后。";
    private const string PharmacyAtticTitle = "阁楼储物间";
    private const string PharmacyAtticNarrative = "顺着后屋的爬梯上到阁楼，堆的全是进货的纸箱。拆开几个，多是包装和赠品毛巾，翻到底才摸出一卷绷带和一小盒电子秤拆下的元件。";
}
