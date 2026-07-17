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
            // 🔴 policy A（用户拍板通则）：authored 不变量图（固定像素距离 / 敌营噪音招怪校准）**维持原尺寸零改动、不硬缩放**——
            //   故 **金手指帮根据地** 与 **斯图尔特家族庄园** 两个敌营**不登记覆盖**（回退默认 2400×1600）：
            //   · 金手指：均匀放大会把 GoldfingerGang.Posts（归一化）守备间距×4/3，改变"开一枪招几人"的 authored 噪音招怪
            //     （docs/research/2026-07-14-goldfinger-calibration.md），即通则的"别破噪音几何"；
            //   · 斯图尔特：StuartManor.cs 自带 LevelW/H const，均匀放大会破 StuartManorTests 噪音带（弓0/匕首1/手枪3/步枪6）。
            //   两图仅仓库这类占位地台可自由放大。
            // 广播台（中·主线关键地点）：占位地台 ⇒ 建真广播站结构（播音楼/北端机房/中央脊廊房间/院内五外屋/东北天线塔）。
            //   🔴 authored 调查点（播音台/照片墙 NarrativeSpot）与发射机锚点 (1200,300) 坐标一字不动，本次只放大机制层地形/物资/点位。
            //   键＝ExplorationCache.BroadcastStationName（"广播台"，与 WorldMapPanel.BroadcastStationName 一字一致）。数值拟定待调。
            { ExplorationCache.BroadcastStationName, (3200f, 2200f) },
        };

    /// <summary>某目的地的画布尺寸：登记过覆盖则取覆盖值，否则回退默认 2400×1600。</summary>
    public static (float Width, float Height) SizeFor(string destinationName)
        => destinationName != null && Overrides.TryGetValue(destinationName, out (float Width, float Height) size)
            ? size
            : (DefaultWidth, DefaultHeight);
}
