using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// <b>皮特·剧情事件（招募路径）</b>——第 7 天夜一开局，一个男孩跑来敲门大喊求救，弹三选一面板：
/// <b>开门救援 / 置之不理 / 攻击他</b>（authored，用户拍板，原话，不引申）。
///
/// <list type="bullet">
///   <item><b>开门救援</b> → 面对追在他身后的<b>三只普通丧尸</b>（普通随机丧尸，非精英）；活下来后男孩加入营地（即皮特）。</item>
///   <item><b>置之不理</b> → 冻结-脚本CG-恢复：相机推近男孩→放大→三丧尸追来→抱头蹲地→被攻击→死亡→缩回（男孩死亡 + 事件结束）。</item>
///   <item><b>攻击他</b> → 冻结-脚本CG-恢复：相机推近男孩→抱头蹲地→被攻击→死亡→缩回（他不还手、无追兵；男孩死亡 + 事件结束）。</item>
/// </list>
///
/// <para>
/// 置之不理/攻击的"相机推近→（三丧尸→）抱头蹲地→死亡→缩回"CG 演出走可复用的
/// <see cref="PlayDeathCinematic"/> 通路（<c>CampMain.CinematicCg</c>，与克莉丝汀处决/放逐共用）：
/// 全程 <c>TimeScale=0</c> 冻结、CG 走真实时基播放、播完恢复时间流逝。CG 结束回调里做逻辑收尾
/// （<see cref="RemovePeteBoy"/> + <see cref="ResolvePeteEvent"/>）。
/// </para>
///
/// <para>
/// <b>交互范式零发明</b>：触发照 <see cref="BeginChristineTutorial"/> 的定时钩子（NightAct 分支）、三选项复用通用
/// <see cref="ChoicePanel"/>、三丧尸生成仿 <see cref="SpawnCampZombies"/>（门外错峰 + Inject + 破防/感知 + <c>_actorLayer</c>）、
/// 招募仿 <see cref="RecruitChristine"/>（<c>Pawn.Create("皮特")</c> 走 <see cref="AddActor"/> 漏斗 ⇒ perk 按名 <see cref="PetePerk.PeteName"/>
/// 自动授予、移速/操作/闪避/饥饿全部自动接通，本单不碰任何 perk 接线）。守卫巡防锁敌仿 <see cref="UpdateRaid"/> 自建一小圈
/// （对 <c>_peteZombies</c>），不借 <c>_raidActive</c>（不惊动袭营胜负结算/后果）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    // ---------------- 皮特事件字段（第 7 夜脚本敲门救援，自成一路）----------------
    private const string PeteBoyName = "男孩";                 // 救援前门外男孩的显示名（获救后转生为 Pawn.Create(PetePerk.PeteName)）
    private const string PeteEventDoneFlag = "pete_event_done"; // 事件已结算（三分支任一 resolve 后置位；NightAct 据此防重触发）
    private const string PeteRescuedFlag = "pete_rescued";      // 开门救援成功、皮特入营（供后续叙事/气泡识别）
    private const string PeteIgnoredFlag = "pete_ignored";      // 置之不理：男孩被丧尸杀死（CG 占位，待 pete-cg-engine）
    private const string PeteAttackedFlag = "pete_attacked";    // 攻击他：男孩不还手被打死（CG 占位，待 pete-cg-engine）
    private const int PeteRescueZombieCount = 3;                // 开门救援固定三只普通丧尸（authored，非 RaidWave 概率、非精英）

    private bool _peteEventActive;                             // 开门救援战斗进行中（逐帧 UpdatePeteRescue）
    private Pawn? _peteBoy;                                    // 门外男孩（Survivor 阵营、无角色⇒不还手；获救转生成新皮特 Pawn 后置空）
    private readonly List<Zombie> _peteZombies = new();        // 追在男孩身后的三只普通丧尸

    /// <summary>
    /// 第 7 夜一开局触发：门外冒出求救男孩 + 冻结弹三选一面板。仅由 <see cref="OnGamePhaseChanged"/> NightAct 分支调，
    /// <see cref="PeteEventDoneFlag"/> 防重入（照 <see cref="BeginChristineTutorial"/> 起手即置 flag 的口径）。
    /// </summary>
    private void BeginPeteRescueEvent()
    {
        _storyFlags.Set(PeteEventDoneFlag, "true"); // 起手即置位防重触发（本夜 NightAct 只入一次；NightAct 相位不落自动存档，reload 天然重放）
        _peteZombies.Clear();

        // 门外男孩：Survivor 阵营的普通 Pawn，但**不入 _survivors**（PawnRoleManager 不排他的角色 ⇒ Role 默认、
        // Pawn.Think 不择敌不出手 ⇒ 天然"抱头蹲地不还手"）。仅 Inject + 订阅 Died + 挂 _actorLayer（不走 AddActor，
        // 避免弹药源/改装脱落等玩家幸存者接线；获救时再造一个正式皮特 Pawn 走 AddActor 漏斗）。
        _peteBoy = Pawn.Create(PeteBoyName, usePistol: false, new Color(0.70f, 0.75f, 0.85f));
        _peteBoy.Position = new Vector2(1200f, 1560f); // 南门外（同克莉丝汀起手位）
        _peteBoy.Inject(_combat, _clock);
        _peteBoy.Died += OnPeteBoyDied;
        _actorLayer.AddChild(_peteBoy);

        GD.Print($"[皮特事件] 第 {_clock.Day} 夜：一个男孩跑到大门外，抱着头大喊求救。");
        PromptPeteRescueChoice();
    }

    /// <summary>男孩死亡（开门救援中被丧尸咬死 / 占位分支移除）：置空引用避免后续帧 use-after-free。</summary>
    private void OnPeteBoyDied(Actor a)
    {
        _peteBoy = null;
        GD.Print("[皮特事件] 男孩死亡。");
    }

    /// <summary>战前暂停（TimeScale=0）弹开门救援/置之不理/攻击他三选一（复用通用 <see cref="ChoicePanel"/>）。</summary>
    private void PromptPeteRescueChoice()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.Setup(
            "深夜，一个十来岁的大男孩连滚带爬地扑到营地大门外，拍着门板嘶喊：\n" +
            "「求你们！开门！它们……它们就在我后面——！」",
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 0, Label = "开门救援",
                        Description = "放他进来——但追在他身后的三只丧尸也会一起涌到门口",
                        Accent = new Color(0.30f, 0.60f, 0.32f) },
                new() { Value = 1, Label = "置之不理",
                        Description = "当作没听见。他会在门外抱头蹲下，直到被丧尸拖走",
                        Accent = new Color(0.55f, 0.50f, 0.28f) },
                new() { Value = 2, Label = "攻击他",
                        Description = "隔门了结他——他不会还手，只会抱头等着",
                        Accent = new Color(0.65f, 0.22f, 0.20f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            panel.QueueFree();
            HandlePeteChoice(v);
        };
    }

    private void HandlePeteChoice(int choice)
    {
        switch (choice)
        {
            case 0: // 开门救援：三只普通丧尸追到门口，进入逐帧救援战
                BeginPeteRescueFight();
                break;
            case 1: // 置之不理：冻结-脚本CG-恢复——相机推近男孩→放大→三丧尸追来→抱头蹲地→被攻击→死亡→缩回。
                _storyFlags.Set(PeteIgnoredFlag, "true");
                GD.Print("[皮特事件] 置之不理：男孩在门外抱头蹲下，被追来的丧尸拖走杀死（播死亡 CG）。");
                PlayDeathCinematic(_peteBoy!, withThreeZombies: true, CinematicDeathKind.Killed,
                    onComplete: () => { RemovePeteBoy(); ResolvePeteEvent(); });
                break;
            case 2: // 攻击他：冻结-脚本CG-恢复——相机推近男孩→抱头蹲地→被攻击→死亡→缩回（他不还手，无追兵）。
                _storyFlags.Set(PeteAttackedFlag, "true");
                GD.Print("[皮特事件] 攻击他：男孩不还手，抱头蹲地等着被打死（播处决 CG）。");
                PlayDeathCinematic(_peteBoy!, withThreeZombies: false, CinematicDeathKind.Killed,
                    onComplete: () => { RemovePeteBoy(); ResolvePeteEvent(); });
                break;
        }
    }

    /// <summary>
    /// 开门救援：门外错峰生成三只普通丧尸（追男孩 + 幸存者），进入 <see cref="UpdatePeteRescue"/> 逐帧战。
    /// 丧尸生成仿 <see cref="SpawnCampZombies"/>（Inject + 破防 + 感知 + <c>_actorLayer</c>）；目标池走 <see cref="PeteZombieTargets"/>
    /// （含男孩），故三只径直扑向门口的男孩，玩家须调守卫/幸存者迎战护住他。
    /// </summary>
    private void BeginPeteRescueFight()
    {
        _storyFlags.Set(PeteRescuedFlag, "true"); // 已择"开门救援"（成败在战后：全歼三尸=皮特入营，男孩战死=救援失败无入营）
        _peteEventActive = true;

        Rect2 wander = new(
            _mapBounds.Position + new Vector2(200, 200),
            _mapBounds.Size - new Vector2(400, 400));

        for (int i = 0; i < PeteRescueZombieCount; i++)
        {
            // 南门外错峰（紧跟男孩身后涌入）：普通随机丧尸，非精英（默认随机着装，不点名预设）。
            var z = Zombie.Create(wander, PeteZombieTargets);
            z.Inject(_combat, _clock);
            z.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter, ShouldSuppressBreach, ReleaseBreachSlotFor);
            z.ConfigurePerception(localLightAt: SampleCampLight);
            z.Position = new Vector2(1150f + i * 55f, 1548f + (i % 2) * 14f);
            _actorLayer.AddChild(z);
            z.Died += OnPeteZombieDied;
            _peteZombies.Add(z);
        }

        GD.Print($"[皮特事件] 开门救援：{PeteRescueZombieCount} 只普通丧尸追着男孩涌到门口——护住他！");
    }

    /// <summary>皮特救援丧尸的择敌池：门外男孩（若在场）+ 存活幸存者 + 布鲁斯（丧尸径直扑向门口的男孩，幸存者迎战）。</summary>
    private IEnumerable<Actor> PeteZombieTargets()
    {
        if (_peteBoy is { Alive: true })
            yield return _peteBoy;
        foreach (Pawn s in _survivors)
            if (s.Alive)
                yield return s;
        if (_bruce is { Alive: true })
            yield return _bruce;
    }

    private void OnPeteZombieDied(Actor a)
    {
        _breachSlots.Release(a.GetInstanceId()); // 砸墙途中被打死 → 让出攻击位（同 OnRaidZombieDied）
        if (a is Zombie z)
            _peteZombies.Remove(z);
    }

    /// <summary>
    /// 逐帧推进开门救援战（仅 <see cref="_peteEventActive"/> 时调）：守卫巡防锁敌（仿 <see cref="UpdateRaid"/> 一小圈，
    /// 对 <c>_peteZombies</c>）+ 胜负判定——三尸全歼且男孩存活 ⇒ 招募皮特；男孩战死 ⇒ 救援失败（不入营）。
    /// </summary>
    private void UpdatePeteRescue(double delta)
    {
        // 守卫巡防：无目标时取侦测半径内最近的救援丧尸交战（是否真开火由 Pawn.Think Guard 射程判定裁决）。
        // 借 StationGuards 在 NightAct 已站好的 _raidGuards / _raidGuardDogs（任何夜晚都站岗），只是这里锁的是 _peteZombies。
        foreach (Pawn g in _raidGuards)
        {
            if (!g.Alive || g.HasActiveTarget)
                continue;
            Zombie? nearest = NearestPeteZombieTo(g.GlobalPosition, g.GuardSightRadius);
            if (nearest != null)
            {
                g.TryFirstStrike(nearest);
                g.CommandAttack(nearest);
            }
        }
        foreach (Dog dog in _raidGuardDogs)
        {
            if (!dog.Alive || dog.HasActiveTarget)
                continue;
            Zombie? nearest = NearestPeteZombieTo(dog.GlobalPosition, dog.GuardSightRadiusScaled);
            if (nearest != null)
                dog.CommandAttack(nearest);
        }

        // 胜负判定：男孩战死 → 救援失败收尾（不入营）；三尸全歼且男孩存活 → 招募皮特。
        if (_peteBoy is not { Alive: true })
        {
            GD.Print("[皮特事件] 救援失败：男孩在门口被丧尸咬死，没能救下他。");
            ResolvePeteEvent();
            return;
        }
        if (_peteZombies.All(z => !z.Alive))
        {
            RecruitPete();
            ResolvePeteEvent();
        }
    }

    private Zombie? NearestPeteZombieTo(Vector2 from, float sightRadius)
    {
        Zombie? nearest = null;
        float best = sightRadius * sightRadius;
        foreach (Zombie z in _peteZombies)
        {
            if (!z.Alive)
                continue;
            float d = from.DistanceSquaredTo(z.GlobalPosition);
            if (d < best)
            {
                best = d;
                nearest = z;
            }
        }
        return nearest;
    }

    /// <summary>
    /// 救援成功：把门外男孩转生为正式玩家幸存者<b>皮特</b>入营——原位移除男孩，<c>Pawn.Create(PetePerk.PeteName)</c>
    /// 走 <see cref="AddActor"/> 漏斗（GrantPete 按名自动授予 + 移速/闪避 lambda 自动注入）+ 加入 <c>_survivors</c> + 刷卡牌栏。
    /// 照 <see cref="RecruitChristine"/> 口径，本单不碰任何 perk 接线（pete-runtime 已铺好按名接通）。
    /// </summary>
    private void RecruitPete()
    {
        Vector2 pos = _peteBoy is { } boy && IsInstanceValid(boy) ? boy.GlobalPosition : new Vector2(1200f, 1500f);
        if (_peteBoy != null && IsInstanceValid(_peteBoy))
            _peteBoy.QueueFree();
        _peteBoy = null;

        var pawn = Pawn.Create(PetePerk.PeteName, usePistol: false, new Color(0.62f, 0.72f, 0.86f));
        pawn.Position = pos; // cartesian，原地入营
        AddActor(pawn);
        _survivors.Add(pawn);
        _cardBar?.SetSurvivors(_survivors); // 入营：卡牌栏新增他的卡

        GD.Print($"[皮特事件] 开门救援成功：男孩获救，作为幸存者「{PetePerk.PeteName}」入营（田径队大男孩，perk 按名自动接通）。");
    }

    /// <summary>
    /// 置之不理/攻击他的<b>逻辑侧收尾</b>：移除门外男孩的逻辑节点（QueueFree + 置空引用）。
    /// 死亡视觉（脚下留血/隐去/相机演出）由 <see cref="PlayDeathCinematic"/> 在 CG 内负责，故此处不再溅血，
    /// 只做逻辑清理——在 CG 结束回调里调（TimeScale 已恢复）。
    /// </summary>
    private void RemovePeteBoy()
    {
        if (_peteBoy != null && IsInstanceValid(_peteBoy))
            _peteBoy.QueueFree();
        _peteBoy = null;
    }

    /// <summary>事件收口：停逐帧战、清残留丧尸、置 done flag（三分支共用）。done flag 起手已置，这里幂等重申。</summary>
    private void ResolvePeteEvent()
    {
        _peteEventActive = false;
        foreach (Zombie z in _peteZombies)
            if (IsInstanceValid(z))
                z.QueueFree();
        _peteZombies.Clear();
        _storyFlags.Set(PeteEventDoneFlag, "true");
    }
}
