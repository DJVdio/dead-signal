using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 「南逃谢幕」结局序列的**运行时接线**（消费 <see cref="SouthEscapeEnding"/> 纯逻辑内核）。
/// 🔴 REUSABLE：军袭结局（<see cref="TryTriggerMilitaryRaid"/>）+ 将来「40 天无限尸潮」结局共用同一序列，
/// 唯一入口 <see cref="BeginSouthEscapeEnding"/>（调用方选好南逃者 + 传触发上下文）。
///
/// <para>三幕：
/// <list type="number">
///   <item>CG-A（<see cref="PlayMassacreCinematic"/>）：冻结-脚本演出（军人占位演出体扫入屠营、其余幸存者当场倒下、
///     南逃者独自向南脱出）——复用 <see cref="CinematicSequence"/> 真实时基（仿 <see cref="PlayDeathCinematic"/>）。</item>
///   <item>玩家操作段（<see cref="LoadEscapeCorridor"/>）：载入 <see cref="EscapeCorridor"/>，玩家右键操作南逃者走单线廊道到峡谷前。</item>
///   <item>CG-B（<see cref="PlaySouthEscapeFarewell"/>）：峡谷前谢幕（大桥未落、两哨兵冷眼），<see cref="EndingPanel"/> 黑屏谢幕 → 终局。</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>南逃谢幕序列进行中（防重入 + 抑制聚餐/相位副作用，见 <see cref="OnGamePhaseChanged"/> 守卫）。</summary>
    private bool _southEscapeActive;

    /// <summary>随机南逃者掷点源（项目铁律：随机走可注入源）。</summary>
    private readonly IRandomSource _southEscapeRng = new SystemRandomSource();

    private EscapeCorridor? _escapeCorridor;
    private Node? _escapeCorridorRoot;

    /// <summary>南逃者半残移速乘子（占位：只做移速惩罚+视觉，南逃段无战斗、半残无其它玩法后果）。</summary>
    private const double SouthEscapeCrippleSpeed = 0.55;

    /// <summary>演出军人数量（占位纯演出体，不走战斗结算）。</summary>
    private const int MassacreSoldierCount = 5;

    /// <summary>
    /// 🔴 REUSABLE 入口：启动「南逃谢幕」强制终局序列。调用方选好 <paramref name="escapee"/>（军袭=随机存活者、
    /// 将来 40 天尸潮结局同）并传 <paramref name="trigger"/> 决定屠营演出/旁白语气。幂等（进行中再调无效）。
    /// </summary>
    public void BeginSouthEscapeEnding(Pawn escapee, SouthEscapeTrigger trigger)
    {
        if (_southEscapeActive || escapee == null || !IsInstanceValid(escapee))
            return;
        _southEscapeActive = true;
        _gameOver = true; // 强制终局：停掉全灭/围攻/其它路由（本序列不经全灭判定，末尾直接 EndingPanel.Show）

        // 持久化南逃者身份（随存档往返 → 第二幕「峡谷营地」桥梁角色接口）。
        SouthEscapeEnding.RecordEscapee(_storyFlags, escapee.DisplayName, escapee.Id.ToString(), trigger);
        GD.Print($"[南逃谢幕] 触发（{trigger}）：南逃者 = {escapee.DisplayName}（id {escapee.Id}）。播 CG-A 屠营演出。");

        // CG-A 屠营演出 → 演完载入南逃走廊（玩家操作段）。
        PlayMassacreCinematic(escapee, trigger, onComplete: () => LoadEscapeCorridor(escapee));
    }

    // ============ CG-A：屠营演出（冻结-脚本CG-恢复，仿 PlayDeathCinematic） ============

    /// <summary>
    /// 屠营演出：军人占位演出体自北扫入 → 其余幸存者当场倒下留血/隐去 → 南逃者独自向南脱出。
    /// 全程 <c>TimeScale=0</c>，走 <see cref="CinematicSequence"/> 真实时基。演完调 <paramref name="onComplete"/>。
    /// </summary>
    private void PlayMassacreCinematic(Pawn escapee, SouthEscapeTrigger trigger, Action onComplete)
    {
        _cinematicActive = true;
        double resumeScale = Engine.TimeScale <= 0 ? 1 : Engine.TimeScale;
        Engine.TimeScale = 0;

        Vector2 campIso = Iso.Project(_cameraCenter);
        Vector2 camReturnPos = _camera.Position;
        Vector2 camReturnZoom = _camera.Zoom;
        _camera.CinematicHold = true;

        // 军人占位演出体（Zombie.Create 空目标池，纯演出、不进战斗结算——用户拍板占位做法）。自北侧错峰生成、扫向营地中心。
        var soldiers = new List<Zombie>();
        var soldierFrom = new List<Vector2>();
        Rect2 wander = new(_mapBounds.Position + new Vector2(200, 200), _mapBounds.Size - new Vector2(400, 400));
        for (int i = 0; i < MassacreSoldierCount; i++)
        {
            var s = Zombie.Create(wander, () => System.Array.Empty<Actor>());
            s.Inject(_combat, _clock);
            Vector2 from = _cameraCenter + new Vector2(-220f + i * 110f, -260f - (i % 2) * 40f); // 北侧一排
            s.Position = from;
            s.Modulate = new Color(0.55f, 0.58f, 0.62f); // 军装深灰占位（与丧尸绿区分）
            _actorLayer.AddChild(s);
            soldiers.Add(s);
            soldierFrom.Add(from);
        }

        // 遇害者 = 除南逃者外的全部存活幸存者。
        var victims = _survivors.Where(p => p.Alive && p != escapee && IsInstanceValid(p)).ToList();
        var victimSprites = victims.ToDictionary(v => v, FindActorSprite);
        ActorSprite? escapeeSprite = FindActorSprite(escapee);
        escapeeSprite?.EnterCinematic();
        Vector2 escapeeHome = escapeeSprite != null ? escapeeSprite.Position : Iso.Project(escapee.GlobalPosition);

        void Finish()
        {
            foreach (Zombie s in soldiers)
                if (IsInstanceValid(s))
                    s.QueueFree();
            if (escapeeSprite != null && IsInstanceValid(escapeeSprite))
                escapeeSprite.QueueFree(); // 已 EnterCinematic 接管，显式回收（走廊里换新 marker 渲染）
            _camera.CinematicHold = false;
            _camera.Position = camReturnPos;
            _camera.Zoom = camReturnZoom;
            Engine.TimeScale = resumeScale;
            _cinematicActive = false;
            onComplete();
        }

        var seq = new CinematicSequence();
        AddChild(seq);

        // ① 相机推近营地中心。
        seq.Then(0.8f, onTick: t =>
        {
            float e = Smooth(t);
            _camera.Position = camReturnPos.Lerp(campIso, e);
            _camera.Zoom = camReturnZoom.Lerp(new Vector2(1.4f, 1.4f), e);
        });

        // ② 军人扫入。
        seq.Then(1.0f, onTick: t =>
        {
            float e = Smooth(t);
            for (int i = 0; i < soldiers.Count; i++)
                if (IsInstanceValid(soldiers[i]))
                    soldiers[i].Position = soldierFrom[i].Lerp(_cameraCenter + new Vector2(-160f + i * 80f, -40f), e);
        });

        // ③ 屠杀：相机微震 + 遇害者逐个倒下留血/隐去。
        seq.Then(1.1f, onTick: t =>
        {
            float k = 1f - t;
            _camera.Position = campIso + new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * (6f * k);
        }, onExit: () =>
        {
            _camera.Position = campIso;
            foreach (Pawn v in victims)
            {
                if (!IsInstanceValid(v)) continue;
                SpawnDeathBlood(v);
                if (victimSprites.TryGetValue(v, out var vs) && vs != null && IsInstanceValid(vs))
                    vs.Visible = false;
            }
        });

        // ④ 南逃者独自向南脱出（sprite 向南滑出 + 相机跟一程）。
        seq.Then(1.2f, onTick: t =>
        {
            if (escapeeSprite != null && IsInstanceValid(escapeeSprite))
                escapeeSprite.Position = escapeeHome + new Vector2(0f, 220f * Smooth(t)); // 屏幕下方＝南
            _camera.Position = campIso + new Vector2(0f, 120f * Smooth(t));
        });

        seq.Play(Finish);
    }

    // ============ 玩家操作段：载入南逃走廊 ============

    /// <summary>
    /// 载入 <see cref="EscapeCorridor"/>（玩家操作段）：南逃者进廊道、玩家右键操控向南；半残＝移速惩罚。
    /// 复用 CampMain 既有选中+移动机制（<c>_currentLevel != null</c> + 选中可控 pawn 即生效，level 通用）。
    /// 终点区触发 → CG-B。
    /// </summary>
    private void LoadEscapeCorridor(Pawn escapee)
    {
        if (_escapeCorridorRoot != null || !IsInstanceValid(escapee))
            return;

        var scene = GD.Load<PackedScene>("res://scenes/EscapeCorridor.tscn");
        _escapeCorridorRoot = scene.Instantiate();
        _escapeCorridor = (EscapeCorridor)_escapeCorridorRoot;
        _currentLevel = _escapeCorridor; // 令 CampMain 输入按"在关卡里"处理（营地容器点不到）

        _escapeCorridor.Clock = _clock;
        _escapeCorridor.ExpeditionTeam = new List<Pawn> { escapee };
        _escapeCorridor.OnReachedCanyon += PlaySouthEscapeFarewell;

        _campNavRegion.Enabled = false;
        GetTree().Root.AddChild(_escapeCorridorRoot);
        SetCampVisible(false);
        _escapeCorridor.Initialize();

        // 半残：只做移速惩罚（复用 authored 移速乘子钩子）+ 视觉（marker 偏暗，示意负伤）。南逃段无战斗，无其它后果。
        escapee.SetAuthoredMoveSpeedMult(() => SouthEscapeCrippleSpeed);

        // 选中南逃者 → 右键操控生效（走 CampMain 既有 IssueMove/CommandMoveTo）。
        SetSelection(escapee);

        // 解冻，进入可操作行走（DawnMeal 相位本为 TimeScale=0；此处放行让玩家走廊道）。
        Engine.TimeScale = 1;
        GD.Print("[南逃谢幕] 进入玩家操作段：右键操控南逃者向南走到峡谷前。");
    }

    // ============ CG-B：峡谷前谢幕 → 黑屏终局 ============

    /// <summary>
    /// 峡谷前谢幕（南逃者踏入终点区触发）：大桥未落、两哨兵冷眼——走 <see cref="EndingPanel"/> 播 CG-B 分段文本，
    /// 播完黑屏谢幕（重新开始/退出）。REUSABLE：军袭 + 40 天尸潮结局共用此谢幕。
    /// </summary>
    private void PlaySouthEscapeFarewell()
    {
        Engine.TimeScale = 0;
        GD.Print("[南逃谢幕] 抵达峡谷前：大桥没有落下，两个哨兵冷眼看着。播 CG-B 谢幕（终局）。");
        EndingPanel.Show(_hud, EndingCg.SouthEscapeFarewell, EndingCg.SouthEscapeFarewellTitle);
    }
}
