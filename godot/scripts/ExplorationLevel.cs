using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public abstract partial class ExplorationLevel : Node2D
{
    public GameClock Clock { get; set; } = null!;
    public List<Pawn> ExpeditionTeam { get; set; } = null!;

    public event Action? OnReturnToCamp;

    public virtual void Initialize() { }
    public virtual void Cleanup() { }

    protected void ReturnToCamp()
    {
        OnReturnToCamp?.Invoke();
    }
}
