using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ——超市骗局伏击（[SPEC-B13]）——
    /// <summary>超市幸存者据点内圈中心（关内世界坐标）：接触点在门口、内圈房间在此、伏击/闯入的 Raider 在此周围生成。
    /// [放大·中3天] 画布 3200×2200 后据点后撤到北部深处（卖场纵深拉长＝更多步行）。坐标拟定待调。</summary>
    private static readonly Vector2 SupermarketDenCenter = new(1700f, 600f);

    /// <summary>
    /// [SPEC-B13] 生成超市骗局的敌对幸存者（Raider 阵营，近战匕首＝"背刺"语义），并可选施加潜行先手一击。
    /// CampMain 在玩家「轻信跟随」被诱入内圈（<paramref name="preemptiveStrike"/>=true）、或「拒绝」后闯入内圈抢物资（false，公平战）时调用。
    /// 先手一击复用 <see cref="NightWatchContest.PreemptiveStrikeMultiplier"/>(1.5x)、走既有承伤管道（<c>ReceiveAttack(damageFactor)</c>），不改战斗规则。
    /// 去重由 CampMain 侧 <see cref="SupermarketAmbush.AmbushSprungFlag"/> 负责（同一趟探索不重复刷敌）。
    /// </summary>
    public void SpawnSupermarketRaiders(int count, bool preemptiveStrike)
    {
        Vector2 center = SupermarketDenCenter;
        // 紧凑徘徊区（贴内圈房间），使他们围着玩家打转、堵住退路。
        var wander = new Rect2(center.X - 300f, center.Y - 220f, 600f, 440f);
        Raider? first = null;
        for (int i = 0; i < count; i++)
        {
            float ang = Mathf.Tau * i / System.Math.Max(1, count) - Mathf.Pi / 2f;
            Vector2 pos = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 90f;
            var r = Raider.Create(wander, LevelTargets, usePistol: false, displayName: "据点幸存者");
            r.Inject(Combat, Clock);
            r.ConfigurePerception(localLightAt: SampleLevelLight);
            r.Position = pos;
            _actorLayer.AddChild(r);
            _levelRaiders.Add(r);
            r.SetPresentationLayer(_isoLayer);
            first ??= r;
        }

        if (preemptiveStrike && first is { } ambusher)
            ApplySupermarketPreemptiveStrike(ambusher, center);
    }

    /// <summary>潜行先手一击：以匕首对最近探索队员施一次 1.5x 承伤（背刺）。走既有承伤管道，不改战斗规则。</summary>
    private void ApplySupermarketPreemptiveStrike(Raider ambusher, Vector2 center)
    {
        Pawn? victim = null;
        float best = float.MaxValue;
        foreach (Pawn p in ExpeditionTeam)
        {
            if (!p.Alive)
                continue;
            float d = p.GlobalPosition.DistanceSquaredTo(center);
            if (d < best) { best = d; victim = p; }
        }
        if (victim is null)
            return;
        ambusher.PlayScriptedMeleeVisual(CombatData.Dagger(), victim);
        victim.ReceiveAttack(ambusher, CombatData.Dagger(), Combat, damageFactor: NightWatchContest.PreemptiveStrikeMultiplier);
        GD.Print($"[Supermarket] 背刺先手 ×{NightWatchContest.PreemptiveStrikeMultiplier:0.0} 命中 {victim.DisplayName}。");
    }

    /// <summary>
    /// [SPEC-B13] 超市：一伙幸存者据守的卖场——外围卖场/仓储/后巷可搜刮（货架残余，食物身份但单点薄），内圈是他们的据点囤货。
    /// 骗局（用户原话"轻信会被骗进密闭小房间背刺围攻"）：门口接触点弹 <see cref="ChoicePanel"/> 二选一——
    ///   · 轻信跟随 → 被诱入内圈密室（<see cref="AddRoomOutline"/> 占位房，"走到房间触发"语义）→ 背刺围攻（<see cref="SpawnSupermarketRaiders"/> 施 1.5x 先手）；
    ///   · 不轻信 → 警告后可搜外围，内圈物资被占——踏入内圈闯入点即公平开战抢货。
    /// 分支时序/文本/去重旗标由 <see cref="SupermarketAmbush"/>（纯逻辑）+ CampMain.OnExplorationDiscovery 驱动；本方法只铺空间。
    ///
    /// <para>
    /// [放大·中3天·拟定待调] 画布覆盖 3200×2200（见 <see cref="ExplorationLevelSize.Overrides"/>）后，卖场纵深从
    /// 一片彩色地台重排成一座**真卖场**：南侧入口 → 货架阵（<see cref="AddSolidWall"/> 实体货架，逐排留错位缺口＝
    /// 蛇形动线，逼玩家绕行、也断视线）→ 东北仓储间 / 西北后巷卸货间（<see cref="AddRoomOutline"/> 带门房间）→
    /// 最北的据点里屋。工作量（步行像素＋搜刮件数）随纵深拉长与货架绕路显著抬升到"≈3天量级"（数值拟定待调，
    /// 精细校准归 param-calibration；点位<b>数量</b>维持 11 处不新增＝叙事 draft 归用户）。
    /// </para>
    /// 占位美术：分区地台 + 货架实体 + 据点密室墙体 + 搜刮点标记（正式空间/美术待后续）。骗局后果细节 draft 待确认。
    /// </summary>
    private void SetupSupermarket()
    {
        // 分区占位地台（纯视觉）：卖场(南/主·纵贯)、仓储(东北)、后巷(西北)。据点内圈另用密室墙体（AddRoomOutline）示意。
        AddZonePad(new Vector2(500, 1350), new Vector2(2200, 760), new Color(0.24f, 0.25f, 0.27f, 0.55f)); // 卖场
        AddZonePad(new Vector2(2340, 840), new Vector2(600, 460), new Color(0.22f, 0.21f, 0.19f, 0.6f));   // 仓储区
        AddZonePad(new Vector2(300, 740), new Vector2(460, 460), new Color(0.18f, 0.19f, 0.18f, 0.62f));   // 后巷卸货区

        var shelfC = new Color(0.55f, 0.48f, 0.36f);   // 外围货架残余（棕黄，物资）
        var hoardC = new Color(0.60f, 0.50f, 0.34f);   // 内圈幸存者囤货（略暖，缴获感）
        var dangerC = new Color(0.78f, 0.30f, 0.24f);  // 据点接触/闯入点（暗红：这里有人，危险）
        var rackC = new Color(0.30f, 0.28f, 0.24f, 0.95f); // 实体货架（挡路+断视线）

        // 货架阵（实体·挡路+阻断寻路+断视线）：两排错位货架，逐排缺口不对齐 ⇒ 玩家须蛇形绕过、且看不穿整片卖场。
        // 下排（南，y≈1760）缺口在东；上排（北，y≈1560）缺口在西 ⇒ 强制左右折返。
        AddSolidWall(new WallRect(650f, 1760f, 660f, 40f), rackC, zIndex: 6);   // 下排·西段
        AddSolidWall(new WallRect(1500f, 1760f, 700f, 40f), rackC, zIndex: 6);  // 下排·东段（与西段间留过道）
        AddSolidWall(new WallRect(900f, 1560f, 620f, 40f), rackC, zIndex: 6);   // 上排·中段
        AddSolidWall(new WallRect(1760f, 1560f, 640f, 40f), rackC, zIndex: 6);  // 上排·东段

        // 外围（南→北纵深，7 处，货架残余）
        AddDiscoveryPoint(ExplorationCache.SupermarketCheckoutId, new Vector2(1600, 1950), markerColor: shelfC, label: "收银台前区");
        AddDiscoveryPoint(ExplorationCache.SupermarketSnackAisleId, new Vector2(1000, 1660), markerColor: shelfC, label: "零食货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketCannedAisleId, new Vector2(1600, 1660), markerColor: shelfC, label: "罐头货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketHouseholdId, new Vector2(2150, 1660), markerColor: shelfC, label: "日用百货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketHardwareId, new Vector2(650, 1430), markerColor: shelfC, label: "五金杂货角");
        AddDiscoveryPoint(ExplorationCache.SupermarketStockroomId, new Vector2(2650, 1100), markerColor: shelfC, label: "仓储区货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketBackAlleyId, new Vector2(500, 950), markerColor: shelfC, label: "后巷卸货区");

        // 东北仓储间 / 西北后巷卸货间（带门房间：踏入要走门洞，制造绕路+遮挡）。
        AddRoomOutline(new Rect2(2340f, 840f, 600f, 460f), new Color(0.30f, 0.27f, 0.23f, 0.95f), "仓储间", RoomEdge.Bottom, RoomEdge.Left);
        AddRoomOutline(new Rect2(300f, 740f, 460f, 460f), new Color(0.26f, 0.25f, 0.22f, 0.95f), "后巷卸货间", RoomEdge.Bottom, RoomEdge.Right);

        // 内圈·幸存者据点密室（占位墙体，南墙留门＝"密闭小房间"；囤货点落其中，打赢/闯入后可搜）。
        var den = SupermarketDenCenter; // (1700, 600)
        AddRoomOutline(new Rect2(den.X - 200f, den.Y - 150f, 400f, 300f), new Color(0.34f, 0.30f, 0.26f, 0.95f), "里屋", RoomEdge.Bottom);
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardFoodId, new Vector2(den.X - 80f, den.Y - 50f), markerColor: hoardC, label: "他们的囤粮");
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardMedsId, new Vector2(den.X + 100f, den.Y - 30f), markerColor: hoardC, label: "他们的药箱");
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardGearId, new Vector2(den.X - 80f, den.Y + 80f), markerColor: hoardC, label: "缴获装备堆");
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardStashId, new Vector2(den.X + 100f, den.Y + 80f), markerColor: hoardC, label: "头目私囤");

        // 骗局接触点（门口，靠内圈南侧）：踏入弹接触对话（CampMain 走 SupermarketAmbush）。zone 略大稳稳接住。
        AddDiscoveryPoint(SupermarketAmbush.ContactDiscoveryId, new Vector2(den.X, den.Y + 240f), markerColor: dangerC, label: "有人招呼", zoneSize: new Vector2(150f, 130f));
        // 内圈闯入点（门槛处，介于接触点与囤货之间）：拒绝招呼后踏入即公平开战抢被占物资。
        AddDiscoveryPoint(SupermarketAmbush.InnerRingDiscoveryId, new Vector2(den.X, den.Y + 120f), markerColor: dangerC, label: "里屋（有人把守）", zoneSize: new Vector2(140f, 90f));
    }
}
