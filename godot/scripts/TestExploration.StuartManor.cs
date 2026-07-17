using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ================= [SPEC-T51] 斯图尔特家族庄园 =================
    //
    // 🔴 用户原话（authored 唯一事实源，一字不改）：「斯图尔特家族庄园（农庄，并不是很富裕，中地图，
    //    有盘踞的劫掠者和岗哨，高危，高风险不是永远高回报，这个调查点最富裕的地方是劫掠者们的装备和衣服，
    //    并且这里会有斯图尔特家的一些剧情，讲述了他们好心收留一些流浪者，结果被背刺，女儿妻子被奸杀，
    //    男性尸体吊挂在门口喂丧尸，在枯井底有抱着婴儿饿死的女性尸体）」
    //
    // 🔴 **别把这一关"平衡"掉**：
    //    · 农庄**是穷的**（10 处搜刮点全是布/木头/土豆，见 ExplorationCache）——那正是「高风险不是永远高回报」；
    //    · 回报**长在人身上**（7 个劫掠者的武器与衣服，见 StuartManor）——**先打赢，才有得扒**；
    //    · 而「打赢劫掠者白捡一身装备」这个场景**不存在**——**胜率不是成本**：打赢每一个都要拿骨折/断肢/弹药去换，
    //      而伤病会累积、不治疗连打会迅速失能（数见 docs/research/2026-07-14-combat-cost.md，harness =
    //      src/DeadSignal.Sim/CombatCostCalibration.cs；CLAUDE.md 通则③同此口径）。
    //      ⇒ **玩家现实里会清掉两三个就撤，或者干脆绕过去。**
    //      **允许他不打**（潜行绕过 / 只清边缘 / 撤退）是这一关的正确玩法之一，不是设计失败。
    //      ⚠️ **此处原先内联的那组数（棍棒 96.5%/骨折 66%、破甲锤 70.8%/断肢 13%、手枪 26.2%、"撑过第 3 个只剩 3.5%"）
    //         已删除：来源存疑、疑似跨报告拼凑**——96.5% 只见于 2026-07-04-combat-sim-baseline.md:37（破甲锤 vs 皮甲+布衣）、
    //         26.2% 只见于 2026-07-05-combat-sim-v2.md:188（长弓 vs 长袖布衣），在 combat-cost.md 里找不到对应组合；
    //         且与 CLAUDE.md 通则③（引 combat-cost.md·[T58/T59] 重跑）的同名场景冲突（棍棒 78.7%、破甲锤 40.0%）。
    //         🔴 **刻意不填新数**：combat-cost.md 自身正由 sweep-research-b 复跑（board 挂 [DECISION] 待裁），
    //         现在抄任何一版都是二次返工，也可能拿"未验证的数"替掉"可疑的数"。**结论不受影响**（上面那句"白捡不存在"
    //         由 CLAUDE.md 通则③独立背书），要引具体数字请直接读复跑后的 combat-cost.md，别再往这里内联。
    //
    // 🔴 **噪音是这一关的核心机制**：哨位间距是照着枪声半径设计的（见 StuartManor.Posts / AlertedBy）——
    //    从庭院中央动手：弓(70)→0 人 / 匕首(90)→1 人 / **手枪(350)→3 人 / 步枪(600)→6 人（整座庄园）**。
    //    ⇒ 「枪纸面最强，但**一开枪就没有『逐个清哨』了**」。真正的通关手段是弓弩 + 哨兵扫视的空窗。

    /// <summary>庄园大门（关内世界坐标，南侧）：探索队自此进关，**一进来就看见门口横梁上的人**。哨兵的扫视中心朝这儿。
    /// <para>实例属性（原静态字段）：Entrance 是归一化分数，乘本关 LevelW/LevelH ⇒ 画布放大时自动缩放；默认 2400×1600 下与改造前逐字节一致。</para></summary>
    private Vector2 StuartGate => new(
        (float)(StuartManor.Entrance.X * LevelW), (float)(StuartManor.Entrance.Y * LevelH));

    /// <summary>门口吊尸的位置（叙事点锚点；丧尸就聚在这一小片）。放大 3200×2200 后与 <c>NarrativeSpotRegistry</c> 里那处 (850,1850) 一致。</summary>
    private static readonly Vector2 StuartGallows = new(850f, 1850f);

    /// <summary>
    /// [SPEC-T51] 斯图尔特家族庄园：一座被劫掠者盘踞的<b>穷农庄</b>。
    /// 分区（南→北，近→深）：大门/前院晒场 → 谷仓/畜栏(东) → 主屋(西·里屋在最里头) → 后院枯井(东北·最深)。
    /// <para>占位美术：分区地台 + 主屋/谷仓占位墙体（<see cref="AddRoomOutline"/>）+ 枯井占位 + 10 处搜刮点标记；
    /// 掉落/叙事在 <see cref="ExplorationCache.Resolve"/>，四处 authored 叙事点由 <see cref="SetupNarrativeSpots"/> 按注册表自动铺（此处不重复铺）。</para>
    /// </summary>
    private void SetupStuartManor()
    {
        // 🔴 放大到 3200×2200（中·高危）：庄园防御核心（劫掠者/哨位）因噪音几何逆缩放仍是一块紧凑区域
        //    （见 StuartManor.Posts 注释），放大出来的空间＝周围田地/院外进路——更多步行、更多潜行绕哨的空间。
        //    地形/点位随缩放后的哨位铺开（主屋≈1040,940 / 谷仓≈1700,1400 / 后院枯井≈2200,800 / 大门≈920,1676）。
        //    分区占位地台（纯视觉）：前院/晒场(南)、谷仓/畜栏(东中)、主屋(中西)、后院(东北·枯井)。
        AddZonePad(new Vector2(650, 1400), new Vector2(1100, 500), new Color(0.26f, 0.24f, 0.18f, 0.55f)); // 前院/晒谷场（土黄）
        AddZonePad(new Vector2(1500, 1240), new Vector2(620, 440), new Color(0.24f, 0.21f, 0.16f, 0.58f));  // 谷仓/畜栏
        AddZonePad(new Vector2(2050, 580), new Vector2(560, 520), new Color(0.20f, 0.22f, 0.19f, 0.55f));   // 后院（枯井在此）

        // 主屋（含**里屋**——那扇从外头钉了闩的门在最里头）：占位墙体，南墙留门。
        AddRoomOutline(new Rect2(820, 720, 760, 520), new Color(0.34f, 0.30f, 0.24f, 0.95f), "主屋", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(900, 720, 320, 200), new Color(0.28f, 0.25f, 0.21f, 0.95f), "里屋", RoomEdge.Bottom);
        // 谷仓：占位墙体，西墙留门（从晒场那侧进）。
        AddRoomOutline(new Rect2(1520, 1280, 460, 360), new Color(0.32f, 0.27f, 0.20f, 0.95f), "谷仓", RoomEdge.Left);

        // 后院的枯井（占位视觉；authored 叙事点「枯井」由注册表铺在 2300,680）。
        DrawWellPlaceholder(new Vector2(2300f, 680f));
        // 门口的横梁（占位视觉：两根门柱 + 一道横梁；authored 叙事点「门口」由注册表铺在 850,1850）。
        DrawStuartGallowsPlaceholder();

        var yardC = new Color(0.55f, 0.48f, 0.36f);   // 前院/晒场（棕黄，物资）
        var houseC = new Color(0.52f, 0.46f, 0.38f);  // 主屋
        var barnC = new Color(0.50f, 0.44f, 0.32f);   // 谷仓/农具

        // 前院/晒场（近，3）
        AddDiscoveryPoint(ExplorationCache.StuartGateCartId, new Vector2(1000, 1780), markerColor: yardC, label: "门前板车");
        AddDiscoveryPoint(ExplorationCache.StuartThreshingYardId, new Vector2(1300, 1600), markerColor: yardC, label: "晒谷场");
        AddDiscoveryPoint(ExplorationCache.StuartChickenCoopId, new Vector2(750, 1550), markerColor: yardC, label: "鸡舍");

        // 主屋（中，4）
        AddDiscoveryPoint(ExplorationCache.StuartKitchenId, new Vector2(950, 1080), markerColor: houseC, label: "灶间");
        AddDiscoveryPoint(ExplorationCache.StuartHallCupboardId, new Vector2(1250, 980), markerColor: houseC, label: "堂屋碗柜");
        AddDiscoveryPoint(ExplorationCache.StuartWardrobeId, new Vector2(980, 850), markerColor: houseC, label: "卧室衣柜");
        AddDiscoveryPoint(ExplorationCache.StuartPantryId, new Vector2(1350, 880), markerColor: houseC, label: "储藏间");

        // 谷仓/农具（中，2）
        AddDiscoveryPoint(ExplorationCache.StuartHayLoftId, new Vector2(1650, 1450), markerColor: barnC, label: "草料阁");
        AddDiscoveryPoint(ExplorationCache.StuartToolShedId, new Vector2(1880, 1560), markerColor: barnC, label: "农具棚");

        // 后院菜窖（最深，1）——翻到底，也不过是一窖发芽的土豆和一卷绷带。
        AddDiscoveryPoint(ExplorationCache.StuartRootCellarId, new Vector2(2200, 950), markerColor: barnC, label: "后院菜窖");

        // [T67] 两处**采集点**：那家人的菜地还在，只是没人回来收了（在地上刨土豆，走 ForageLogic）。
        var forageC = new Color(0.42f, 0.62f, 0.36f);
        AddDiscoveryPoint(ForageLogic.StuartGardenPotatoId, new Vector2(2400, 800), markerColor: forageC, label: "菜地土豆");
        AddDiscoveryPoint(ForageLogic.StuartFurrowPotatoId, new Vector2(2500, 920), markerColor: forageC, label: "垄尾漏刨");
    }

    /// <summary>门口横梁的占位视觉（两根门柱 + 一道横梁 + 几条垂下的绳）：纯 Polygon2D，无碰撞。</summary>
    private void DrawStuartGallowsPlaceholder()
    {
        var wood = new Color(0.30f, 0.25f, 0.19f, 0.95f);
        Vector2 g = StuartGallows;

        // 横梁
        AddChild(new Polygon2D
        {
            Polygon = Quad(g + new Vector2(-150f, -14f), new Vector2(300f, 14f)),
            Color = wood,
            ZIndex = 5,
        });
        // 两根门柱
        AddChild(new Polygon2D
        {
            Polygon = Quad(g + new Vector2(-160f, -14f), new Vector2(16f, 90f)),
            Color = wood,
            ZIndex = 5,
        });
        AddChild(new Polygon2D
        {
            Polygon = Quad(g + new Vector2(144f, -14f), new Vector2(16f, 90f)),
            Color = wood,
            ZIndex = 5,
        });
        // 垂下的绳（四条，间隔匀——"这个高度是有人算过的"）
        for (int i = 0; i < 4; i++)
        {
            AddChild(new Polygon2D
            {
                Polygon = Quad(g + new Vector2(-108f + (i * 72f), 0f), new Vector2(3f, 46f)),
                Color = new Color(0.42f, 0.38f, 0.30f, 0.95f),
                ZIndex = 6,
            });
        }
    }

    /// <summary>
    /// [SPEC-T51] 庄园劫掠者布防：编制/持械/着装/是否站岗<b>全在 <see cref="StuartManor.Roster"/>（authored 表）</b>，
    /// 布点在 <see cref="StuartManor.Posts"/>（同一张表，与噪音几何共用）——本方法只管把它们摆到地图上。
    /// <b>数值改动去改那张表，别改这里。</b>
    ///
    /// <para><b>照 <c>SpawnGoldfingerGuards</c> 的范式做，没另起炉灶</b>：<see cref="Raider.Create"/> 点名持械 + 点名着装
    /// ⇒ 杀了能扒（<c>CorpseLoot.Strip</c> 必掉零掷骰，走 <c>SpawnLevelCorpse</c> 落尸通道，<b>零新规则</b>）；
    /// <see cref="Raider.ConfigureSentry"/> ⇒ 会站岗（扫视<b>周期固定、可观察、可预测</b>，玩家能数着拍子摸过去）。</para>
    ///
    /// <para><b>三个哨兵的扫视档</b>：近入口的大门哨走<b>警觉</b>档（难绕——他就是来防你从这个方向进来的）；
    /// 深处的谷仓/主屋哨走<b>懈怠</b>档（扫得慢、端点发呆久＝好绕）——<b>越深越好摸</b>，逼玩家在
    /// 「从门口硬啃」和「绕开大门、摸进深处」之间做取舍。</para>
    /// </summary>
    private void SpawnStuartManorRaiders()
    {
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);
        IReadOnlyList<ManorRaider> roster = StuartManor.Roster;

        for (int i = 0; i < roster.Count && i < StuartManor.Posts.Count; i++)
        {
            ManorRaider m = roster[i];
            (double px, double py) = StuartManor.Posts[i];
            var pos = new Vector2((float)(px * LevelW), (float)(py * LevelH));

            var r = Raider.Create(
                wander, LevelTargets,
                displayName: m.DisplayName,
                weapon: StuartManor.WeaponFor(m.Arm),
                outfit: StuartManor.ApparelFor(m.Outfit)); // 🔴 穿什么＝掉什么：这一关的回报就长在这一行上
            r.Inject(Combat, Clock);
            r.ConfigurePerception(localLightAt: SampleLevelLight);
            r.Position = pos;

            if (m.IsSentry)
            {
                float facing = (StuartGate - pos).Angle();          // 扫视中心朝大门——他们防的是从南边进来的人
                bool deep = pos.Y < LevelH * 0.62f;                  // 主屋/谷仓一线以北＝深处
                r.ConfigureSentry(pos, facing, deep ? SentrySweep.Slack : SentrySweep.Alert);
            }

            _actorLayer.AddChild(r);
            _levelRaiders.Add(r);
            _markers[r] = CreateActorMarker(r, new Color(0.72f, 0.26f, 0.22f)); // 暗红：敌对幸存者（与丧尸绿/己方一眼区分）
        }
    }

    /// <summary>
    /// [SPEC-T51] 门口的丧尸（<b>用户原话「男性尸体吊挂在门口<b>喂丧尸</b>」的空间落地</b>）：
    /// 4 只聚在大门横梁底下、给一个贴着门口的<b>紧凑</b>徘徊区 ⇒ 它们不会散进庄园，就在那儿仰着脸够。
    ///
    /// <para><b>它们和劫掠者互不攻击</b>——不是特判，是既有结构：关内敌对单位的目标池 <see cref="LevelTargets"/>
    /// 只有探索队（+随队布鲁斯）⇒ 丧尸看不见劫掠者、劫掠者也不理丧尸。落到玩法上恰好就是那句话的意思：
    /// <b>这道尸群是劫掠者给自己养的护城河，进门要先过它。</b></para>
    ///
    /// <para>⚠️ 在门口打这一架<b>不会惊动庄园</b>（近战噪音 90~150px，而最近的哨位在 250px 开外——见
    /// <see cref="StuartManor.AlertedBy"/>）：这是有意的<b>喘息位</b>——门口这一架是关卡的"学费"，不是死刑。
    /// 数量/布点拟定待调（归 param-calibration）。</para>
    /// </summary>
    private void SpawnStuartGateZombies()
    {
        // 贴着门口横梁的紧凑徘徊区（它们被吊尸拴在这儿，不往庄园里散）。
        var gateWander = new Rect2(StuartGallows.X - 220f, StuartGallows.Y - 170f, 440f, 300f);

        Vector2[] spots =
        {
            new(StuartGallows.X - 60f, StuartGallows.Y - 70f),
            new(StuartGallows.X + 80f, StuartGallows.Y - 60f),
            new(StuartGallows.X - 100f, StuartGallows.Y + 50f),
            new(StuartGallows.X + 120f, StuartGallows.Y + 30f),
        };
        foreach (Vector2 spot in spots)
            SpawnZombieAt(spot, gateWander);
    }
}
