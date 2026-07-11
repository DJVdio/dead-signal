using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public abstract partial class ExplorationLevel : Node2D
{
    public GameClock Clock { get; set; } = null!;
    public List<Pawn> ExpeditionTeam { get; set; } = null!;

    /// <summary>
    /// 随队犬类伙伴（布鲁斯）——仅当本次探索带上狗时由 CampMain 注入，否则 null。狗非 Pawn、不入
    /// <see cref="ExpeditionTeam"/>，故独立一格；关卡据此把它放置/回收、纳入敌人目标池与视野观察者。
    /// </summary>
    public Dog? CompanionDog { get; set; }

    /// <summary>
    /// 本关当前存活的敌对单位（供随队布鲁斯自主缠斗——它的敌对 provider 在营地/关卡间切换时读此）。
    /// 基类默认空；有敌人的关卡（如 <see cref="TestExploration"/>）覆盖返回其丧尸等。
    /// </summary>
    public virtual IEnumerable<Actor> LevelHostiles() => System.Array.Empty<Actor>();

    /// <summary>
    /// 逐观察者视锥后处理钩子（CampMain 注入，通常＝其 BondScaleCone）：给道格/布鲁斯的视野锥按羁绊等级缩放
    /// （道格锥角、布鲁斯视距/锥角），使道格带狗出探索时的视野技能端到端生效。null＝不缩放（营地外无羁绊上下文）。
    /// 关卡视野遮罩装配时挂给 <c>VisionMask.SetViewerConeAdjuster</c>。羁绊等级态由委托内部读 CampMain.BondLevel，
    /// 跨场景无需另传（道格/布鲁斯为同一实例，reparent 进关卡后委托仍命中）。
    /// </summary>
    public Func<Actor, VisionLogic.VisionCone, VisionLogic.VisionCone>? ViewerConeAdjuster { get; set; }

    /// <summary>本次探索的目的地名（CampMain 注入）。关卡据此决定是否铺设发现点（如金手指帮根据地）。</summary>
    public string DestinationName { get; set; } = "";

    /// <summary>
    /// 克莉丝汀是否已独走复仇（CampMain 注入 <c>StoryFlags</c> 的 christine_left_for_revenge）。
    /// 金手指帮根据地据此决定是否**额外**铺出"克莉丝汀本人尸体"发现点（帮众尸体恒在、与此无关）。
    /// </summary>
    public bool ChristineLeftForRevenge { get; set; }

    public event Action? OnReturnToCamp;

    /// <summary>探索队触发一处发现点时上报 discoveryId；CampMain 据此置 flag、入库日记、弹环境叙事。</summary>
    public event Action<string>? OnDiscovery;

    public virtual void Initialize() { }
    public virtual void Cleanup() { }

    protected void ReturnToCamp()
    {
        OnReturnToCamp?.Invoke();
    }

    /// <summary>子类在发现点被探索队踏入时调用，上报 discoveryId。</summary>
    protected void RaiseDiscovery(string discoveryId)
    {
        OnDiscovery?.Invoke(discoveryId);
    }
}
