namespace DeadSignal.Godot;

/// <summary>
/// HUD 左上角状态行的**纯拼装**（无 Godot 依赖 ⇒ 可单测）。
///
/// <para>
/// 从 <c>CampMain</c> 里抽出来单独成函数，是为了让「HUD 到底显示了什么字」这件事**可被测试直接断言**——
/// 此前这行字是在 <c>CampMain._Process</c> 里就地插值的，测试够不着，于是把相位打成英文枚举名
/// （<c>[DayPrep]</c>）这种代码腔泄漏一直没人发现。现在相位一律走 <see cref="DisplayNames.Of(DayPhase)"/>。
/// </para>
///
/// <para>格式：<c>营地  第 3 天  08:20  [外出探索]   速度 3x   幸存者 4   背包 12.0 / 50.0 kg（轻装）</c></para>
/// </summary>
public static class HudStatusLine
{
    /// <summary>
    /// 拼出状态行。<paramref name="clock"/>（时钟串）、<paramref name="speed"/>（速度档）、
    /// <paramref name="bagLine"/>（背包，仅探索时有；无则传空串）均为调用方已格式化好的文本。
    /// </summary>
    public static string Compose(
        bool exploring, int day, string clock, DayPhase phase, string speed, int survivors, string bagLine)
        => $"{(exploring ? "探索" : "营地")}  第 {day} 天  {clock}  [{DisplayNames.Of(phase)}]   速度 {speed}   " +
           $"幸存者 {survivors}{bagLine}";
}
