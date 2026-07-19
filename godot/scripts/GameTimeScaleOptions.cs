using System;

namespace DeadSignal.Godot;

public static class GameTimeScaleOptions
{
    public static readonly double[] Speeds = { 1.0, 2.0, 3.0 };

    public static int MaxIndex => Speeds.Length - 1;

    public static double SpeedAt(int index) => Speeds[Math.Clamp(index, 0, MaxIndex)];

    public static string LabelAt(int index) => PausedLabel(index, false);

    public static string PausedLabel(int index, bool paused) => paused ? "暂停" : $"{(int)SpeedAt(index)}x";
}
