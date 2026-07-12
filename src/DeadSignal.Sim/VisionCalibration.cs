using System.Globalization;
using System.Text;
using DeadSignal.Godot; // VisionLogic / DayPhase（Link 进本工程的真实纯逻辑）

/// <summary>
/// 视野曲线 &amp; 光照解析校准（param-calibration，里程碑3）。用真实 <see cref="VisionLogic"/> 常量/函数扫表，据表调常量。
/// 校验点（用户口径）：
///  ① 白天玩家视距(BaseRange 300) vs 丧尸兜底(490) 的昼夜曲线，夜间前向视距是否≈旧 220 半径雷达。
///  ② 夜间持火把 vs 不持的"被发现距离"差是否形成有意义决策（目标差距 ≥~40%）。
///  ③ 嗅觉 70px vs 夜锥是否互补不重叠（短程全向补锥形侧后死角）。
/// 光源值取 LightProfile 目录：火把 I=0.70/R=240、火堆 I=0.95/R=460；夜间环境光 NightAmbient。
/// </summary>
public static class VisionCalibration
{
    private const float PlayerBase = VisionLogic.BaseRange; // 300
    private const float ZombieBase = 490f;                  // Zombie.BaseSightRange
    private const float TorchIntensity = 0.70f;
    private const float ZombieSmell = 70f;                  // Zombie.SmellSenseRadius

    public static void Run()
    {
        var sb = new StringBuilder();
        float night = VisionLogic.AmbientLight(DayPhase.NightAct, indoorsDark: false);
        float day = VisionLogic.AmbientLight(DayPhase.DayExplore, indoorsDark: false);
        float twilight = VisionLogic.AmbientLight(DayPhase.DuskMeal, indoorsDark: false);

        sb.AppendLine("# 视野曲线 & 光照解析校准");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"环境光：白天 {day} / 暮光 {twilight} / 夜间 {night}　DarkRangeFactor={VisionLogic.DarkRangeFactor}　半角 {VisionLogic.DarkHalfAngleDeg}~{VisionLogic.DayHalfAngleDeg}°　MaxExposureBonus={VisionLogic.MaxExposureBonus}"));
        sb.AppendLine();

        // ① 昼夜视距曲线
        sb.AppendLine("## ① 昼夜视距/锥角曲线（前向视距=cone.Range）");
        sb.AppendLine("| 相位·光照L | 玩家视距(R0=300) | 丧尸视距(R0=490) | 锥半角° |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var (name, L) in new[] { ("白天", day), ("暮光", twilight), ("夜间", night) })
        {
            var pc = VisionLogic.ConeFor(L, PlayerBase);
            var zc = VisionLogic.ConeFor(L, ZombieBase);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} L={L} | {pc.Range:F0} | {zc.Range:F0} | {pc.HalfAngleDeg:F1} |"));
        }
        var zNight = VisionLogic.ConeFor(night, ZombieBase);
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 丧尸夜间前向视距 {zNight.Range:F0}px（对齐旧半径雷达 220 的设计意图：{(System.Math.Abs(zNight.Range - 220) < 30 ? "达标≈220" : "偏离220")}）。"));
        var pNight = VisionLogic.ConeFor(night, PlayerBase);
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 夜间玩家(不持光)前向视距 {pNight.Range:F0}px < 丧尸 {zNight.Range:F0}px（比例 {pNight.Range / zNight.Range:P0}）——夜里玩家视野劣于丧尸，须靠光源/潜行，方向正确。"));
        sb.AppendLine();

        // ② 持火把暴露代价
        sb.AppendLine("## ② 夜间持火把的暴露代价（被发现距离加成）");
        float expo = VisionLogic.ExposureRangeMultiplier(night, TorchIntensity);
        // 持火把者本体局部光照抬升 → 自身视锥变大（决策的正面收益）。
        float litLocal = VisionLogic.CombineLight(night, TorchIntensity);
        var litCone = VisionLogic.ConeFor(litLocal, PlayerBase);
        sb.AppendLine("| 状态 | 本体局部光照 | 自身前向视距 | 被他人发现距离系数 |");
        sb.AppendLine("|---|---:|---:|---:|");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| 不持光 | {night} | {pNight.Range:F0} | ×1.00 |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| 持火把(I=0.70) | {litLocal:F2} | {litCone.Range:F0} | ×{expo:F3}（+{(expo - 1) * 100:F1}%） |"));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 火把自身视距 {pNight.Range:F0}→{litCone.Range:F0}（+{(litCone.Range / pNight.Range - 1) * 100:F0}%），代价被发现距离 +{(expo - 1) * 100:F1}%。目标差距 ≥40%：{((expo - 1) >= 0.40f ? "达标" : "未达标，建议上调 MaxExposureBonus")}。"));
        sb.AppendLine();

        // ③ 嗅觉 vs 夜锥互补
        sb.AppendLine("## ③ 丧尸嗅觉(70px 全向) vs 夜锥 互补性");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 夜锥：前向 {zNight.Range:F0}px / 半角 {zNight.HalfAngleDeg:F1}°（全角 {zNight.HalfAngleDeg * 2:F0}°），侧后有 {360 - zNight.HalfAngleDeg * 2:F0}° 盲区。"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"- 嗅觉：{ZombieSmell}px 全向兜底，补盲区。嗅觉半径 {ZombieSmell} 仅为夜锥前向 {zNight.Range:F0} 的 {ZombieSmell / zNight.Range:P0}——短程不盖长锥，互补不重叠：{(ZombieSmell < zNight.Range * 0.5f ? "达标" : "嗅觉过长，与锥形重叠")}。"));
        sb.AppendLine();

        var report = sb.ToString();
        System.IO.File.WriteAllText("docs/research/2026-07-12-vision-calibration.md", report);
        Console.Write(report);
    }
}
