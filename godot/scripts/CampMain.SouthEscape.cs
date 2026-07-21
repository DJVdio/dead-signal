using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 「南逃谢幕」结局序列的**运行时接线**（消费 <see cref="SouthEscapeEnding"/> 纯逻辑内核）。
/// 🔴 REUSABLE：军袭结局（<see cref="TryTriggerMilitaryRaid"/>）+ 将来「配置时限无限尸潮」结局共用同一序列，
/// 唯一入口 <see cref="BeginSouthEscapeEnding"/>（调用方选好南逃者 + 传触发上下文）。
///
/// <para>三幕：
/// <list type="number">
///   <item>CG-A（<see cref="PlayMassacreCinematic"/>）：冻结-脚本演出（施暴方占位演出体扫入屠营——军袭＝军人、尸潮＝丧尸、其余幸存者当场倒下、
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

    /// <summary>军袭演出军人数量（占位纯演出体，不走战斗结算）。</summary>
    private const int MassacreSoldierCount = 5;

    /// <summary>尸潮演出丧尸数量（占位纯演出体，无限丧尸屠营 → 比军袭更多具压场，不走战斗结算）。</summary>
    private const int MassacreZombieCount = 8;

    /// <summary>CG-A 开场字幕每段停留时长（秒，真实时基；拟定待调，GUI 目视校准）。</summary>
    private const float OpeningNarrationSecondsPerSegment = 3.2f;

    /// <summary>CG-A 开场字幕每段淡入占比（0→此比例内 alpha 拉满，其后保持）。</summary>
    private const float OpeningNarrationFadeInPortion = 0.28f;

    /// <summary>
    /// 🔴 REUSABLE 入口：启动「南逃谢幕」强制终局序列。调用方选好 <paramref name="escapee"/>（军袭=随机存活者、
    /// 将来尸潮结局同）并传 <paramref name="trigger"/> 决定屠营演出/旁白语气。幂等（进行中再调无效）。
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

        // CG-A：先播 authored 开场字幕（叠层旁白）→ 屠营演出 → 演完载入南逃走廊（玩家操作段）。
        // 🔴 字幕文本按触发源分叉：军袭＝EndingCg.MilitaryRaidMassacre（6 段用户手写旁白，此前从无展示路径，本单接线）。
        //    尸潮版 CG-A 开场旁白＝authored 缺口（EndingCg 无"丧尸屠营"专用段；HordeSiege 语气偏"守住到最后一人"的全员战死，
        //    与新"随机一人半残南逃"结局未必吻合）⇒ 暂传空，PlayOpeningNarration 空即跳过、直接演出（零回归）。见 [DECISION]。
        IReadOnlyList<string> openingNarration = trigger == SouthEscapeTrigger.MilitaryRaid
            ? EndingCg.MilitaryRaidMassacre
            : Array.Empty<string>();
        PlayOpeningNarration(openingNarration, onComplete: () =>
            PlayMassacreCinematic(escapee, trigger, onComplete: () => LoadEscapeCorridor(escapee)));
    }

    /// <summary>
    /// 尸潮到期终局钩子（配置时限到达时由 NightAct 调，由 <see cref="HordeTimeline.ShouldTriggerSiege"/> 门控）：
    /// **推翻旧"可玩无限围攻直至全灭"路由**（用户 authored）——无限丧尸踏平营地、随机一名幸存者半残南逃
    /// → 进「南逃谢幕」序列（<see cref="BeginSouthEscapeEnding"/> 传 <see cref="SouthEscapeTrigger.HordeSiege"/>，
    /// 与军袭 <see cref="TryTriggerMilitaryRaid"/> 共用同一单角色南逃谢幕，只是触发源＝丧尸、CG-A 施暴方＝丧尸）。
    /// 随机走可注入源 <see cref="_southEscapeRng"/>（复用 military-impl-core 的 <see cref="SouthEscapeEnding.SelectEscapee{T}"/>）。
    /// 无人存活则兜底不启动（全灭另有 GameOverCondition 路由，不应发生）。
    /// </summary>
    private void TryTriggerHordeSiegeEnding()
    {
        var alive = _survivors.Where(s => s.Alive).ToList();
        Pawn? escapee = SouthEscapeEnding.SelectEscapee(alive, _southEscapeRng);
        if (escapee == null)
        {
            GD.Print($"[Horde] 第 {_clock.Day} 天：尸潮到期但无存活幸存者，跳过南逃谢幕（全灭另有路由）。");
            return;
        }

        _storyFlags.Set(HordeTimeline.ArrivedFlag, "true"); // 保留"尸潮已抵达"旗标语义（HUD/存档识别本局尸潮终局）
        BeginSouthEscapeEnding(escapee, SouthEscapeTrigger.HordeSiege);
    }

    // ============ CG-A 开场字幕：authored 旁白逐屏叠层（EndingCg.cs 注"本段作开场字幕叠层"） ============

    /// <summary>
    /// 屠营演出**前置**的开场字幕：把 <paramref name="segments"/>（如 <see cref="EndingCg.MilitaryRaidMassacre"/> 6 段
    /// authored 旁白）在全屏黑底上**逐段渐显**，定时推进（过场性质，非玩家逐段按键），播完淡出 → 调 <paramref name="onComplete"/>
    /// 接屠营演出。忠实 <c>EndingCg.cs</c> 注「CG-A 主体是冻结脚本演出，本段作开场字幕叠层」+ wiki endings.json「逐屏文本」。
    /// <para>🔴 **不用 <see cref="EndingPanel"/>**：EndingPanel 是**终局载体**（播完浮出「重开/退出」、TimeScale=0 不恢复），
    /// 而 CG-A **不是终局**（后接南逃走廊玩家操作段 + CG-B 才终局）——照抄会截断南逃流程。故此处自建非终局字幕层，
    /// 复用 <see cref="CinematicSequence"/> 真实时基（TimeScale=0 下照走，仿 <see cref="PlayMassacreCinematic"/>）。</para>
    /// <paramref name="segments"/> 为空 → 直接 <paramref name="onComplete"/>（零回归：尸潮版文本待 author，见 BeginSouthEscapeEnding 注）。
    /// </summary>
    private void PlayOpeningNarration(IReadOnlyList<string> segments, Action onComplete)
    {
        if (segments == null || segments.Count == 0)
        {
            onComplete();
            return;
        }

        _cinematicActive = true;
        Engine.TimeScale = 0;

        var layer = new CanvasLayer { Layer = 190, ProcessMode = Node.ProcessModeEnum.Always }; // 高于 HUD、低于 EndingPanel(200)
        var overlay = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f) }; // 近纯黑，同 EndingPanel 底色
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop; // 吃掉点击，独占（开场段无交互）
        layer.AddChild(overlay);

        var label = new Label();
        label.AnchorLeft = 0.5f; label.AnchorRight = 0.5f; label.AnchorTop = 0.5f; label.AnchorBottom = 0.5f;
        label.OffsetLeft = -420; label.OffsetRight = 420; label.OffsetTop = -120; label.OffsetBottom = 160;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeFontSizeOverride("font_size", 20);
        label.AddThemeColorOverride("font_color", new Color(0.86f, 0.83f, 0.77f)); // 同 EndingPanel 正文色
        label.Modulate = new Color(1, 1, 1, 0);
        overlay.AddChild(label);
        AddChild(layer);

        var seq = new CinematicSequence();
        AddChild(seq);
        foreach (string seg in segments)
        {
            string text = seg; // 闭包捕获逐段快照
            seq.Then(OpeningNarrationSecondsPerSegment,
                onEnter: () => label.Text = text,
                onTick: t => label.Modulate = new Color(1, 1, 1, Mathf.Clamp(t / OpeningNarrationFadeInPortion, 0f, 1f)));
        }
        seq.Play(() =>
        {
            if (IsInstanceValid(layer))
                layer.QueueFree();
            _cinematicActive = false;
            onComplete();
        });
    }

    // ============ CG-A：屠营演出（冻结-脚本CG-恢复，仿 PlayDeathCinematic） ============

    /// <summary>
    /// 屠营演出：施暴方占位演出体自北扫入（军袭＝军人深灰、尸潮＝丧尸本色更多具，按 <paramref name="trigger"/> 分叉）
    /// → 其余幸存者当场倒下留血/隐去 → 南逃者独自向南脱出。
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

        // 施暴方占位演出体（Zombie.Create 空目标池，纯演出、不进战斗结算——用户拍板占位做法）。自北侧错峰生成、扫向营地中心。
        // 按触发源分叉：军袭＝军装深灰占位；尸潮＝丧尸本色 + 更多具（无限丧尸压场）。均为占位，美术待 author。
        bool horde = trigger == SouthEscapeTrigger.HordeSiege;
        int attackerCount = horde ? MassacreZombieCount : MassacreSoldierCount;
        var attackers = new List<Zombie>();
        var attackerFrom = new List<Vector2>();
        Rect2 wander = new(_mapBounds.Position + new Vector2(200, 200), _mapBounds.Size - new Vector2(400, 400));
        for (int i = 0; i < attackerCount; i++)
        {
            var s = Zombie.Create(wander, () => System.Array.Empty<Actor>());
            s.Inject(_combat, _clock);
            Vector2 from = _cameraCenter + new Vector2(-220f + i * 90f, -260f - (i % 2) * 40f); // 北侧一排（步距随具数收窄以容更多丧尸）
            s.Position = from;
            if (!horde)
                s.Modulate = new Color(0.55f, 0.58f, 0.62f); // 军装深灰占位（军袭·与丧尸绿区分）；尸潮版保留丧尸本色
            _actorLayer.AddChild(s);
            attackers.Add(s);
            attackerFrom.Add(from);
        }

        // 遇害者 = 除南逃者外的全部存活幸存者。
        var victims = _survivors.Where(p => p.Alive && p != escapee && IsInstanceValid(p)).ToList();
        var victimSprites = victims.ToDictionary(v => v, FindActorSprite);
        ActorSprite? escapeeSprite = FindActorSprite(escapee);
        escapeeSprite?.EnterCinematic();
        Vector2 escapeeHome = escapeeSprite != null ? escapeeSprite.Position : Iso.Project(escapee.GlobalPosition);

        void Finish()
        {
            foreach (Zombie s in attackers)
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

        // ② 施暴方扫入（军人／丧尸）。
        seq.Then(1.0f, onTick: t =>
        {
            float e = Smooth(t);
            for (int i = 0; i < attackers.Count; i++)
                if (IsInstanceValid(attackers[i]))
                    attackers[i].Position = attackerFrom[i].Lerp(_cameraCenter + new Vector2(-160f + i * 70f, -40f), e);
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
    /// 播完黑屏谢幕（重新开始/退出）。REUSABLE：军袭 + 尸潮结局共用此谢幕。
    /// </summary>
    private void PlaySouthEscapeFarewell()
    {
        Engine.TimeScale = 0;
        GD.Print("[南逃谢幕] 抵达峡谷前：大桥没有落下，两个哨兵冷眼看着。播 CG-B 谢幕（终局）。");
        string cg = SouthEscapeEnding.TriggerOf(_storyFlags) == SouthEscapeTrigger.HordeSiege
            ? "res://assets/cg/horde-escape.png"
            : "res://assets/cg/military-escape.png";
        EndingPanel.Show(_hud, EndingCg.SouthEscapeFarewell, EndingCg.SouthEscapeFarewellTitle, cg);
    }
}
