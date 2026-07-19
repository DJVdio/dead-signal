namespace DeadSignal.Godot;

public enum PawnRole
{
    Idle,
    Expedition,
    Sleeping,
    Guard,
    Reading,

    /// <summary>
    /// 卧床养病（玩家主动下令，见 <see cref="BedrestLogic"/>）：跨相位持续，直到玩家叫他起来。
    /// 与 Guard/Reading 互斥——躺着的人夜里不站岗、不生产、不读书，这就是养病的代价。
    /// </summary>
    Bedrest,

    /// <summary>被一座生产设施占用；不可接其它生产或日常指令，离台/参战只暂停当前工时。</summary>
    Producing
}
