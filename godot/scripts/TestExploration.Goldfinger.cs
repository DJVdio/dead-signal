using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 金手指帮守备布点：读 <see cref="GoldfingerGang.Posts"/>（相对坐标）投到本关尺寸。
    /// <b>布点的真源在那张表，不在这里</b>——因为"开一枪会招来几个人"是纯几何、Sim 要算它
    /// （<see cref="GoldfingerGang.AlertedBy"/>），而 Sim 够不着 Godot 场景层。两处各写一份就会漂移。
    /// </summary>
    private Vector2[] GoldfingerGuardSpots() => GoldfingerGang.Posts
        .Select(p => new Vector2((float)(LevelW * p.X), (float)(LevelH * p.Y)))
        .ToArray();

    /// <summary>据点入口方位（关内世界坐标，南侧）：哨兵的扫视中心朝这儿——他们防的是从这个方向进来的人。</summary>
    private Vector2 GoldfingerEntrance => new(LevelW * 0.5f, LevelH * 0.95f);

    /// <summary>
    /// 金手指帮根据地守备布防。
    ///
    /// <para>🔴 <b>他们是人，不是丧尸</b>（用户澄清：「金手指帮是人，不是丧尸，不过他们刚经历完异常战斗，
    /// 大家的状态都不是巅峰」）。此前据点守备生成的是 <c>Zombie</c> ⇒ 丧尸不持械 ⇒
    /// <b>打赢金手指帮一把武器都捡不到</b>，而这本该是玩家最重要的装备通道。</para>
    ///
    /// <para>改成 <see cref="Raider"/> 后三样东西<b>全部白拿、零新规则</b>：
    /// ① <b>持械</b> ⇒ 杀了能扒（<c>CorpseLoot.Strip</c> 必掉零掷骰，走 <c>SpawnLevelCorpse</c> 落尸通道）；
    /// ② <b>会站岗</b>（<c>Raider.ConfigureSentry</c> 早已实现、此前<b>全项目零调用点</b>——设计文档 §5 的
    /// 敌营岗哨/三角波扫视、TODO 里记的"岗哨没有调用点"，就是卡在"守备是丧尸，而丧尸不会站岗"上）；
    /// ③ <b>会开门/会砸墙/听得见动静</b>（Raider 本就有这些，丧尸只会砸）。</para>
    ///
    /// <para><b>编制、持械、伤情全在 <see cref="GoldfingerGang.Roster"/>（authored 表）</b>，本方法只管把它摆到地图上——
    /// 数值改动去改那张表，别改这里。</para>
    /// </summary>
    private void SpawnGoldfingerGuards()
    {
        Vector2[] spots = GoldfingerGuardSpots();
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);
        IReadOnlyList<GangGuard> roster = GoldfingerGang.Roster;

        for (int i = 0; i < roster.Count && i < spots.Length; i++)
        {
            GangGuard guard = roster[i];
            Vector2 pos = spots[i];

            var r = Raider.Create(
                wander, LevelTargets,
                displayName: guard.DisplayName,
                weapon: GoldfingerGang.WeaponFor(guard.Arm));
            r.Inject(Combat, Clock);
            r.ConfigurePerception(localLightAt: SampleLevelLight);
            r.ApplyInjury(guard.Injury); // 「刚经历完异常战斗」＝预置部位伤 + 骨折（不登记出血，否则他们会自己流血死）
            r.Position = pos;

            if (guard.IsSentry)
            {
                // 钉在岗位上、绕"朝向入口"的中心有规律地左右扫视 ⇒ 玩家可以蹲着数拍子、算准背对的那几秒摸过去。
                // 深处两个走懈怠档（扫得慢、端点发呆久＝好绕）——伤兵守内院；近入口那个走警觉档（难绕）。
                float facing = (GoldfingerEntrance - pos).Angle();
                bool deep = pos.Y < LevelH * 0.5f;
                r.ConfigureSentry(pos, facing, deep ? SentrySweep.Slack : SentrySweep.Alert);
            }

            _actorLayer.AddChild(r);
            _levelRaiders.Add(r);
            _markers[r] = CreateActorMarker(r, new Color(0.72f, 0.26f, 0.22f)); // 暗红：敌对幸存者（与丧尸绿/己方一眼区分）
        }
    }

    // ---------------- 探索发现点 ----------------

    /// <summary>
    /// 金手指帮根据地：两具尸体发现点（用户拍板，设计文档 §8.7）。
    /// · 帮众尸体（被克莉丝汀反杀，恒在、各分支可见）铺在靠近入口处，先被遇到；
    /// · 克莉丝汀本人尸体仅复仇线（<see cref="ExplorationLevel.ChristineLeftForRevenge"/>）铺出，位置更深，叙事递进（先帮众、再她本人）。
    /// 触发链路复用现有 AddDiscoveryPoint / DiscoveryPanel / GoldfingerDiscovery.Resolve。
    /// </summary>
    private void SetupGoldfingerCorpseDiscoveries()
    {
        SetupGoldfingerStructures();

        AddDiscoveryPoint(
            GoldfingerDiscovery.GangMemberCorpseId,
            new Vector2(1150, 350),
            markerColor: new Color(0.7f, 0.2f, 0.2f),
            label: "遗体");

        if (ChristineLeftForRevenge)
            AddDiscoveryPoint(
                GoldfingerDiscovery.ChristineCorpseId,
                new Vector2(1950, 380),
                markerColor: new Color(0.78f, 0.15f, 0.28f),
                label: "遗体");

        // [SPEC-B12-补] 中型·战斗为主：11 处帮派储备物资点（发现点式；掉落/叙事在 ExplorationCache.Resolve）。
        // "打过才拿"——近入口(南侧)少、gauntlet 中段与根据地深处(北侧远角)多；与上方两具尸体发现点命名空间独立不冲突。
        // 近入口(2)：岗哨/前院。
        AddCachePoint(ExplorationCache.GoldfingerCheckpointId, new Vector2(600f, 1200f), "岗哨掩体");
        AddCachePoint(ExplorationCache.GoldfingerYardWreckId, new Vector2(900f, 1090f), "前院废车堆");
        // 中区 gauntlet(5)。
        AddCachePoint(ExplorationCache.GoldfingerBunksId, new Vector2(780f, 850f), "帮众铺位");
        AddCachePoint(ExplorationCache.GoldfingerAmmoCrateId, new Vector2(1220f, 780f), "弹药箱");
        AddCachePoint(ExplorationCache.GoldfingerGunBenchId, new Vector2(1050f, 650f), "修械台");
        AddCachePoint(ExplorationCache.GoldfingerHidePileId, new Vector2(1420f, 900f), "皮件堆");
        AddCachePoint(ExplorationCache.GoldfingerFuelStashId, new Vector2(700f, 600f), "油料桶");
        // 深处(4，根据地北侧远角，打穿才拿)：军械柜/头目保险柜/银库暗格/头目急救箱。
        AddCachePoint(ExplorationCache.GoldfingerArmoryId, new Vector2(1600f, 300f), "军械柜");
        AddCachePoint(ExplorationCache.GoldfingerBossSafeId, new Vector2(1850f, 250f), "头目保险柜");
        AddCachePoint(ExplorationCache.GoldfingerSilverCacheId, new Vector2(2010f, 180f), "银库暗格");
        AddCachePoint(ExplorationCache.GoldfingerBossMedkitId, new Vector2(1750f, 480f), "头目急救箱");
    }

    /// <summary>
    /// 金手指帮据点外围的空间实体：围栏是 authored 静态屏障，寨门走关内门的通用链。
    /// <see cref="ExplorationWalls.GoldfingerFences"/> 与 <see cref="ExplorationWalls.GoldfingerDoors"/>
    /// 是几何唯一事实源；门会自动登记到 CampMain 的右键前往/撬锁菜单，开门时同样走导航洞与
    /// [SPEC-T60] 门后激活入口。围栏档位保留在纯逻辑表，后续补探索关结构损坏消费时无需重铺地图。
    /// </summary>
    private void SetupGoldfingerStructures()
    {
        var fenceColor = new Color(0.25f, 0.20f, 0.16f, 0.98f);
        foreach (ExplorationFence fence in ExplorationWalls.GoldfingerFences())
            AddSolidWall(fence.Rect, fenceColor, zIndex: -4);

        var gateColor = new Color(0.38f, 0.32f, 0.24f, 0.98f);
        foreach (ExplorationDoor door in ExplorationWalls.GoldfingerDoors())
            AddLevelDoor(door, gateColor);
    }
}
