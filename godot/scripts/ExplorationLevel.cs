using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public abstract partial class ExplorationLevel : Node2D
{
    public GameClock Clock { get; set; } = null!;
    public List<Pawn> ExpeditionTeam { get; set; } = null!;

    /// <summary>本次探索的目的地名（CampMain 注入）。关卡据此决定是否铺设发现点（如金手指帮根据地）。</summary>
    public string DestinationName { get; set; } = "";

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
