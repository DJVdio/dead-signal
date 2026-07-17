using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>
/// 探索关画布尺寸的<b>单一登记处</b>（per-destination，零 Godot 依赖 ⇒ Link 进单测）。
/// <para>
/// 历史上所有探索关共用写死的 2400×1600（<c>TestExploration</c> 的 const）。本表把尺寸变成
/// 「按目的地取值」：<see cref="SizeFor"/> 按 <c>DestinationName</c> 查 <see cref="Overrides"/>，
/// <b>查不到就回退默认 2400×1600</b> —— 所以在没有任何目的地登记覆盖尺寸之前，每一关都仍是
/// 2400×1600，与改造前逐字节一致（零漂移基线）。
/// </para>
/// <para>
/// 🔴 <b>给某目的地设更大画布 = 往 <see cref="Overrides"/> 加一行</b>（后续 per-map impl 的唯一入口）。
/// 键 = 该关的 <c>DestinationName</c>（与 <c>TestExploration.Initialize</c> 那串 if-else 用的同一个字符串常量，
/// 如 <c>ExplorationCache.HospitalName</c>、<c>StuartManor.DestinationName</c>；<c>WorldMapPanel.*Name</c>
/// 是 Godot 类型、本文件不能引用，改用其字面量字符串值）。值 = (宽, 高)。
/// 档位拟定待调：小≈2400×1600（维持）/ 中≈3200×2200（约3天）/ 大≈4200×2800（约5天）。
/// </para>
/// </summary>
public static class ExplorationLevelSize
{
    /// <summary>历史固定画布宽（改造前的 <c>TestExploration.LevelW</c> const 值）。未登记覆盖的目的地回退到它。</summary>
    public const float DefaultWidth = 2400f;

    /// <summary>历史固定画布高（改造前的 <c>TestExploration.LevelH</c> const 值）。未登记覆盖的目的地回退到它。</summary>
    public const float DefaultHeight = 1600f;

    /// <summary>
    /// 目的地 → (宽,高) 覆盖表。<b>当前为空 = 所有目的地一律走默认</b>（这是零漂移基线的保证）。
    /// per-map impl 在此按目的地填目标尺寸，例如：
    /// <code>
    /// { ExplorationCache.HospitalName, (4200f, 2800f) },   // 大·约5天
    /// { "watchers_forest_cabin",       (2400f, 1600f) },   // 小·维持（WorldMapPanel.*Name 用字面量）
    /// </code>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (float Width, float Height)> Overrides =
        new Dictionary<string, (float Width, float Height)>
        {
            // 废弃医院（大图·目标≈5天探索量级）：均匀放大 1.75×（4200/2400 == 2800/1600 == 1.75），
            // 保 authored 楼层布局比例不变（几何在 ExplorationWalls.Hospital* 按同系数缩放 + 补内部绕行墙/点位）。
            // 数值拟定待调，方向对着 5 天锚点（≈3000–3600s 在关工作量），最终靠实机校准。
            { ExplorationCache.HospitalName, (4200f, 2800f) },
            // 南林村庄（大点·道格布鲁斯救援地）：放大到 ≈5天探索量级（尺寸/村落地形/物资·丧尸密度按此画布铺开）。
            // 键＝VillageRescue.DestinationName（"南林村庄"，与 TestExploration.Initialize 分发同一字符串常量）。数值拟定待调。
            { VillageRescue.DestinationName, (4200f, 2800f) },
            // 中图三张（目标≈3天探索量级，3200×2200＝默认 2400×1600 均匀放大 4/3×）：占位地台图，
            // 放大画布 + 在各 partial 补真地形（货架/店铺/排屋墙体＋门洞 ⇒ 蛇形绕路）＋点位随纵深铺开，
            // 把步行/搜刮工作量抬到≈3天量级（1800–2160s，拟定待调，精调归 param-calibration）。
            { ExplorationCache.SupermarketName, (3200f, 2200f) },   // 超市（幸存者骗局据点）
            { ExplorationCache.GasStationName, (3200f, 2200f) },    // 加油站（燃油大户·地下储油间深处高价值）
            { ExplorationCache.EastNewVillageName, (3200f, 2200f) },// 东部新村（住宅区·30 点杂而薄，一户户翻）
            { ExplorationCache.HarvesterWarehouseName, (3200f, 2200f) }, // 联合收割机仓库（货架/堆垛/装卸区/北阁楼最深）
            // 🔴 policy A（用户拍板通则）：authored 不变量图（固定像素距离 / 敌营噪音招怪校准）不得**硬缩放破坏**其噪音几何。
            //   两个敌营用户裁决 A 分流：
            //   · **斯图尔特家族庄园＝放大 3200×2200**（下方登记）——用**绕庭院中心逆缩放**：StuartManor.cs 的 const 同步 3200×2200、
            //     Posts 逐轴逆缩放 ⇒ 哨位间/庭院噪音的**像素距离逐字节不变**（StuartManorTests 噪音带 弓0/匕首1/手枪3/步枪6 恒绿），
            //     防御核心保持紧凑、放大出来的是周围田地/院外进路。**不是硬缩放**，故不违 policy A。
            //   · **金手指帮根据地＝维持原尺寸 2400×1600 零改动**（不登记覆盖）——均匀放大会把 GoldfingerGang.Posts（归一化）
            //     守备间距×4/3、改变"开一枪招几人"的 authored 噪音招怪（docs/research/2026-07-14-goldfinger-calibration.md）；
            //     正确放大须重新校准该噪音招怪，另立 follow-on 单（见 docs/TODO.md）。
            { StuartManor.DestinationName, (3200f, 2200f) },       // 斯图尔特家族庄园（中·高危·逆缩放保噪音几何）
            // 广播台（中·主线关键地点）：占位地台 ⇒ 建真广播站结构（播音楼/北端机房/中央脊廊房间/院内五外屋/东北天线塔）。
            //   🔴 authored 调查点（播音台/照片墙 NarrativeSpot）与发射机锚点 (1200,300) 坐标一字不动，本次只放大机制层地形/物资/点位。
            //   键＝ExplorationCache.BroadcastStationName（"广播台"，与 WorldMapPanel.BroadcastStationName 一字一致）。数值拟定待调。
            { ExplorationCache.BroadcastStationName, (3200f, 2200f) },
            // 小图四张（河边小屋/南丁格尔药店/守林人小屋/消防站）：用户口径「地图尺寸都要更大一些」——
            //   小图给**适度放大**（2800×1900＝默认 2400×1600 放大 ~1.17×/1.19×，比原大一档但仍属小图体量，不追 3 天量级），
            //   放大画布 + 在各 partial 补一点结构/绕路（河滩栈道/店铺库房/林间柴棚/车库器材间）+ 既有搜刮点随纵深铺开
            //   （不新增搜刮 id ⇒ 各图 *CacheTests 计数恒绿；小图不必堆到 3 天量级）。数值拟定待调，精调归 param-calibration。
            //   🔴 守林人小屋含 ForageLogic 采集点（林下腐叶/柴堆蘑菇），重排坐标不动其 id ⇒ ForageFarmingButcheryTests 恒绿。
            //   键：河边小屋/消防站＝ExplorationCache.*Name；守林人小屋＝WatchersCabinName（＝WorldMapPanel.WatchersCabinName 字面量"守望者森林小屋"）；
            //   药店＝NurseRecruit.DestinationName（字面量"药店"，本文件不能引 Godot 类型 NurseRecruit，用字面量）。
            { ExplorationCache.RiversideCabinName, (2800f, 1900f) },  // 河边小屋（河滩/栈道/杂物）
            { ExplorationCache.WatchersCabinName, (2800f, 1900f) },   // 守林人小屋（林间小屋+柴棚+林下·含采集点）
            { ExplorationCache.FireStationName, (2800f, 1900f) },     // 消防站（车库+器材间+宿舍+后院）
            { "药店", (2800f, 1900f) },                                // 南丁格尔的小药店（店铺面+后屋药房+库房+阁楼）
            // 城市之巅瞭望观景台（占位地台图·无 authored 固定像素不变量：望远镜瞭望＝flag 式发现点、LookoutSightingTests 纯逻辑无画布耦合）⇒
            //   同四小图适度放大 2800×1900（天台观景平台补机房/值班室/服务楼结构 + 通风管道水箱绕路 + 5 搜刮点随纵深铺开、id 不新增）。
            //   🔴 LookoutTelescopePosition 静态锚点原钉 DefaultWidth/2，本关放大后已改为读 SizeFor(城市之巅).Width/2 自动同步到 1400（见 TestExploration.cs 注释）。
            { ExplorationCache.CityRooftopLookoutName, (2800f, 1900f) }, // 城市之巅（天台观景台·望远镜瞭望）
            // 🔴 [SPEC-T60·Phase2] 警察局：**Phase1「开门激活门后丧尸」落地后才解锁的放大**。旧版威胁靠"房间门洞遮挡+
            //   固定像素感知半径"的几何摆位钉死，尺寸一动就可能让某点同时暴露两只（＝断崖）；Phase1 把拘留区那只改成
            //   **绑门实体的门后特殊丧尸**（撬开铁门才唤醒）、另 3 只走普通感知唤醒后，触发**与尺度无关** ⇒ 可放大。
            //   档位＝小图（journal 地图分档把警察局列在「小图·维持1~2天」，scout-door 结论亦为「警察局→小适度」）⇒
            //   与已落地的五张小图对齐 2800×1900。authored 拓扑（中央脊廊+三侧房+最深禁闭区/4 丧尸各藏一房/两件甲）
            //   只重排坐标不改语义；**门宽＝走廊宽 140 刻意不缩放**（同 ExplorationWalls.cs:231 医院先例）。
            //   数值拟定待调，精调归实机校准。
            { ExplorationCache.PoliceStationName, (2800f, 1900f) },   // 警察局（脊廊+侧房·拘留区锁门后特殊丧尸）
            // 🔴 [SPEC-T60·Phase2] 破败教堂：**Phase1「开门激活门后丧尸」落地后才解锁的放大**——
            //   曾按 policy A 裁定「authored 视野谜题关·维持不放大」，**该裁定已被 Phase1 推翻**。
            //   旧约束是一条**固定像素**：「吓一跳」＝墓地 12 只必须**同时挤进门洞站位的 300px 白昼锥**
            //   ⇒ 墓地进深被锥半径钉死，一放大就散出锥外、惊吓当场没了 ⇒ 整关钉在 2400×1600。
            //   Phase1 把它重定义成「**推开墓地边界那两扇门 ⇒ 门后整片冻结丧尸唤醒涌来**」（ZombieActivation）后，
            //   **触发绑的是门实体、与尺度无关** ⇒ 像素约束解绑 ⇒ 可放大。档位＝用户口径「中→3天(3200×2200)」
            //   （scout-door 亦判「教堂→中」）。authored 语义只重排坐标不改：墓地必关得死/退路两个洞永远敞着/
            //   两处证据（忏悔录+血字）正文一字未动/GraveyardWakeDoors 拓扑不变。放大只加纵深与数量
            //   （长椅 5→6 排、立柱 3→4 根、墓地进深 364→534）；🔴 **门宽 72 / 中央走道 140 / 侧廊 64 / 排距 90
            //   一律不缩放**（按人体与丧尸直径 26 标定的物理常量，同 ExplorationWalls.cs 医院先例）。数值拟定待调。
            { RuinedChurch.DestinationName, (3200f, 2200f) },         // 破败教堂（门厅/中殿/圣坛/墓地·开门唤醒整片）
            // 🔴 [SPEC-T60·Phase2] 难民营地：**Phase1「开门激活门后丧尸」落地后才解锁的放大**。
            //   旧版「开门跳脸」靠一条**固定像素**撑着：伏击丧尸须贴在门后 ≤90px（< 室内暗视距 124px），
            //   否则门一开你还看不见它、跳脸就不成立 ⇒ **房间不能大过那几十像素的窗口** ⇒ 整关钉死 2400×1600。
            //   Phase1 把它改成**绑门实体的门后特殊丧尸**（推开那扇房门才唤醒、一门一只）后，触发**与尺度无关**
            //   ⇒ 像素约束解绑 ⇒ 房间随便多大都行。档位＝用户口径「中→3天(3200×2200)」（scout-door 亦判「难民→中」）。
            //   authored 语义只重排坐标不改：18 房/一房一门/14 处物资分在 14 间房/10 只伏击各锁自己那扇门（WakeDoorFor）。
            //   🔴 **门宽 48 / 过道宽 72 刻意不缩放**（同 ExplorationWalls.cs:231 医院先例）——48 < 2×丧尸直径 52 的
            //   **战术漏斗仍然有效且要保住**：它从来不是 blocker，只是不再是"跳脸"的承载物。数值拟定待调，靠实机校准。
            { RefugeeCamp.DestinationName, (3200f, 2200f) },          // 难民营地（排屋三排六列·室内恒暗·开门唤醒）
        };

    /// <summary>某目的地的画布尺寸：登记过覆盖则取覆盖值，否则回退默认 2400×1600。</summary>
    public static (float Width, float Height) SizeFor(string destinationName)
        => destinationName != null && Overrides.TryGetValue(destinationName, out (float Width, float Height) size)
            ? size
            : (DefaultWidth, DefaultHeight);
}
