using System;
using System.Collections.Generic;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// <b>可复用「冻结-脚本CG-恢复」通路</b>（用户拍板 authored：置之不理/处决/放逐都<b>暂停游戏时间(TimeScale=0)
/// 用游戏内脚本 CG 展示，随后恢复时间流逝</b>）。皮特（男孩死亡）与克莉丝汀（处决/放逐）共用此一套。
///
/// <para>
/// <b>时基铁律</b>：CG 全程 <c>TimeScale=0</c>（游戏逻辑冻结），演出必须走<b>不受 TimeScale 影响的真实时基</b>——
/// 由 <see cref="CinematicSequence"/>（<c>GetTicksMsec</c> 真 delta + <c>ProcessMode=Always</c>）逐帧推进相机与演出角色，
/// 否则 TimeScale=0 会把 CG 自己也冻住。相机接管走 <see cref="CameraController.CinematicHold"/>（本控制器让位、CG 直接写
/// Position/Zoom），目标角色 sprite 走 <see cref="ActorSprite.EnterCinematic"/>（其 _Process 让位、CG 独占 Transform/Modulate/Visible）。
/// </para>
///
/// <para>
/// <b>参数化</b>：<see cref="PlayDeathCinematic"/> 收 目标 pawn、是否演三只追兵丧尸、死亡方式（当场死 / 走出门淡出）、
/// 收尾回调。皮特置之不理=演三丧尸+当场死；皮特处决=当场死无追兵；克莉丝汀处决=当场死无追兵；克莉丝汀放逐=走出门淡出。
/// 演出内复用现有 <see cref="SpawnDeathBlood"/>（当场死留血）与「走向门外」运动（放逐，仿 <see cref="WalkOutAndDespawn"/> 但走 CG 实时时基）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>脚本 CG 的死亡方式。</summary>
    private enum CinematicDeathKind
    {
        /// <summary>当场死：抱头蹲地→被攻击→死亡，脚下留血。</summary>
        Killed,
        /// <summary>走出门外淡出（放逐）：转身走向营外、alpha→0 消失，不留尸不流血。</summary>
        Exiled,
    }

    private bool _cinematicActive; // CG 播放中（防重入 + 供调用方识别）

    private const float CinematicPushZoom = 1.9f;   // 推近后的放大倍率（CameraController ZoomMax=2.2 内）

    /// <summary>
    /// 播一段「冻结-脚本CG-恢复」死亡/离开演出。全程 TimeScale=0，走 <see cref="CinematicSequence"/> 真实时基：
    /// <list type="number">
    ///   <item>相机推近目标 + 放大；</item>
    ///   <item>（可选）身后追来三只演出丧尸（滑向目标）；</item>
    ///   <item>目标抱头蹲地（sprite 下蹲挤压）；</item>
    ///   <item>被攻击→死亡（<see cref="CinematicDeathKind.Killed"/> 当场留血 / <see cref="CinematicDeathKind.Exiled"/> 走出门淡出）；</item>
    ///   <item>画幅缩回正常 → 恢复 TimeScale → 调 <paramref name="onComplete"/>（调用方做逻辑收尾/置 flag/清引用）。</item>
    /// </list>
    /// 目标/其 sprite 的视觉销毁由本通路统一在收尾处理；<paramref name="onComplete"/> 只管逻辑侧（QueueFree 逻辑节点、置 flag）。
    /// </summary>
    /// <param name="target">被演出的角色（皮特男孩 / 克莉丝汀）。</param>
    /// <param name="withThreeZombies">是否演三只追兵丧尸（皮特置之不理=true，其余=false）。</param>
    /// <param name="kind">死亡方式（当场死 / 走出门淡出）。</param>
    /// <param name="onComplete">CG 结束后的逻辑收尾（TimeScale 已恢复），可空。</param>
    private void PlayDeathCinematic(Actor target, bool withThreeZombies, CinematicDeathKind kind, Action? onComplete)
    {
        if (target == null || !IsInstanceValid(target))
        {
            onComplete?.Invoke(); // 目标已不在（reload/竞态）：跳过演出，直接收尾，语义不丢
            return;
        }

        _cinematicActive = true;
        double resumeScale = Engine.TimeScale <= 0 ? 1 : Engine.TimeScale;
        Engine.TimeScale = 0; // 冻结游戏逻辑（演出走 CinematicSequence 真实时基，不吃这个 0）

        // 相机接管：记下返回位/缩放，让控制器让位。
        Vector2 camReturnPos = _camera.Position;
        Vector2 camReturnZoom = _camera.Zoom;
        _camera.CinematicHold = true;
        Vector2 targetIso = Iso.Project(target.GlobalPosition);

        // 目标 sprite 接管（headless 无 iso_layer 时为 null，全程 null-safe）。
        ActorSprite? sprite = FindActorSprite(target);
        sprite?.EnterCinematic();
        Vector2 spriteHome = sprite != null ? sprite.Position : targetIso;

        // 演出丧尸（仅置之不理）：门外南侧错峰生成，滑向门口的目标。TimeScale=0 其 AI delta=0 不自行动，纯由 CG 驱位。
        var showZombies = new List<Zombie>();
        var zombieFrom = new List<Vector2>();
        Vector2 zombieTo = target.GlobalPosition; // cartesian
        if (withThreeZombies)
        {
            Rect2 wander = new(_mapBounds.Position + new Vector2(200, 200), _mapBounds.Size - new Vector2(400, 400));
            for (int i = 0; i < 3; i++)
            {
                var z = Zombie.Create(wander, () => System.Array.Empty<Actor>()); // 空目标池：纯演出，不参与战斗结算
                z.Inject(_combat, _clock);
                Vector2 from = target.GlobalPosition + new Vector2(-70f + i * 70f, 150f + (i % 2) * 40f); // 身后（南向）错峰
                z.Position = from;
                _actorLayer.AddChild(z);
                showZombies.Add(z);
                zombieFrom.Add(from);
            }
        }

        // 收尾：统一销毁演出视觉（目标 sprite + 演出丧尸）、交还相机、恢复 TimeScale，再调逻辑收尾。
        void FinishCinematic()
        {
            foreach (Zombie z in showZombies)
                if (IsInstanceValid(z))
                    z.QueueFree(); // 其 sprite 随 !Alive/失效自毁
            if (sprite != null && IsInstanceValid(sprite))
                sprite.QueueFree(); // 已接管（不会自毁），显式回收
            _camera.CinematicHold = false;
            Engine.TimeScale = resumeScale;
            _cinematicActive = false;
            onComplete?.Invoke();
        }

        var seq = new CinematicSequence();
        AddChild(seq);

        // ① 相机推近 + 放大（smoothstep 缓动）。
        seq.Then(0.9f, onTick: t =>
        {
            float e = Smooth(t);
            _camera.Position = camReturnPos.Lerp(targetIso, e);
            _camera.Zoom = camReturnZoom.Lerp(new Vector2(CinematicPushZoom, CinematicPushZoom), e);
        });

        if (kind == CinematicDeathKind.Exiled)
        {
            // 放逐：转身走向营外 + alpha 淡出（仿 WalkOutAndDespawn，但走 CG 实时时基）。
            Vector2 outward = spriteHome - Iso.Project(_cameraCenter);
            outward = outward.LengthSquared() > 1f ? outward.Normalized() : Vector2.Down;
            seq.Then(0.4f); // 定格一拍（她抬头望向你后转身）
            seq.Then(1.4f, onTick: t =>
            {
                if (sprite == null || !IsInstanceValid(sprite))
                    return;
                sprite.Position = spriteHome + outward * (220f * t);        // 边走
                sprite.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(1f - t, 0f, 1f)); // 边淡
            }, onExit: () =>
            {
                if (sprite != null && IsInstanceValid(sprite))
                    sprite.Visible = false;
            });
        }
        else
        {
            // 当场死：追兵滑入（可选）→ 抱头蹲地 → 被攻击 → 死亡留血。
            if (withThreeZombies)
            {
                seq.Then(0.9f, onTick: t =>
                {
                    float e = Smooth(t);
                    for (int i = 0; i < showZombies.Count; i++)
                        if (IsInstanceValid(showZombies[i]))
                            showZombies[i].Position = zombieFrom[i].Lerp(zombieTo + new Vector2(-40f + i * 40f, 34f), e);
                });
            }

            // 抱头蹲地：sprite 纵向挤压下蹲。
            seq.Then(0.6f, onTick: t =>
            {
                if (sprite != null && IsInstanceValid(sprite))
                    sprite.Scale = Vector2.One.Lerp(new Vector2(1.12f, 0.72f), Smooth(t));
            });

            // 被攻击：相机微震 + 目标颤抖（真实时基随机抖动，衰减）。
            seq.Then(0.7f, onTick: t =>
            {
                float k = 1f - t; // 衰减
                var jitter = new Vector2(
                    (float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * (7f * k);
                _camera.Position = targetIso + jitter;
                if (sprite != null && IsInstanceValid(sprite))
                    sprite.Position = spriteHome + jitter * 0.6f;
            }, onExit: () =>
            {
                // 死亡：脚下留血（复用 SpawnDeathBlood）+ 隐去目标。
                SpawnDeathBlood(target);
                _camera.Position = targetIso;
                if (sprite != null && IsInstanceValid(sprite))
                    sprite.Visible = false;
                foreach (Zombie z in showZombies)
                    if (IsInstanceValid(z))
                        z.Visible = false; // 追兵散去（演出结束即隐，收尾统一 free）
            });
        }

        // ⑤ 画幅缩回正常。
        seq.Then(0.9f, onTick: t =>
        {
            float e = Smooth(t);
            _camera.Position = targetIso.Lerp(camReturnPos, e);
            _camera.Zoom = new Vector2(CinematicPushZoom, CinematicPushZoom).Lerp(camReturnZoom, e);
        });

        seq.Play(FinishCinematic);
    }

    /// <summary>smoothstep 缓动（3t²-2t³）：让相机推拉/挤压有加减速，不生硬。</summary>
    private static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
