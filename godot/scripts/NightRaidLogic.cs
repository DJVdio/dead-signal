using System;
using System.Collections.Generic;
using DeadSignal.Combat; // IRandomSource（纯 C# 引擎，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 NightWatchContest.cs / ShiftSchedule.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载批次6 夜防对抗的运行时**编排辅助纯函数**（night-response）：把 Godot 消费层里可抽出的判定
// （岗哨建筑加成映射、多守卫对抗聚合、威胁规模估计）落成纯函数先红后绿，空间执行（拉人入战/施伤/偷物资）仍归运行时层。
// 单守卫对抗规则本体在 NightWatchContest（watch-contest 权威源）；本类只做「营地整体」层面的聚合与映射。

/// <summary>
/// 夜防对抗运行时编排辅助纯逻辑（night-response）。全静态纯函数 + draft 常量。数值皆「拟定待调」。
/// </summary>
public static class NightRaidLogic
{
    /// <summary>岗哨建筑加成缩放：把岗位视野系数（<see cref="GuardPostStats"/>.SightMultiplier，如哨塔 1.10）
    /// 超过 1 的部分折成 <see cref="NightWatchContest.ComputeAlertness"/> 的 structureBonus 加法项。拟定待调。</summary>
    public const float StructureBonusScale = 1.0f;

    /// <summary>
    /// 岗哨建筑加成：SightMultiplier(≥1) → 警戒力加法项 = (mult-1)×Scale，clamp ≥0。
    /// 平地岗（mult=1）无加成；哨塔/屋顶（mult&gt;1）给正加成。喂 ComputeAlertness 的 structureBonus。
    /// </summary>
    public static float StructureBonusFrom(float sightMultiplier)
        => Math.Max(0f, (sightMultiplier - 1f) * StructureBonusScale);

    /// <summary>威胁规模估计档位（守卫目击的**模糊估计**，非精确数——潜行情报只给个大概）。</summary>
    public enum ThreatBand
    {
        /// <summary>零星（≤2）。</summary>
        Small,
        /// <summary>成群（3~4）。</summary>
        Medium,
        /// <summary>大批（≥5）。</summary>
        Large,
    }

    /// <summary>真实袭击者数 → 模糊规模档（阈值拟定待调）。</summary>
    public static ThreatBand BandFor(int raiderCount) => raiderCount switch
    {
        <= 2 => ThreatBand.Small,
        <= 4 => ThreatBand.Medium,
        _ => ThreatBand.Large,
    };

    /// <summary>规模档 → 面板情报文案（草稿供用户改）。</summary>
    public static string ThreatLabel(ThreatBand band) => band switch
    {
        ThreatBand.Small => "零星几个黑影摸近营地",
        ThreatBand.Medium => "一小群袭击者正逼近",
        ThreatBand.Large => "大批袭击者压上来了",
        _ => "有袭击者逼近",
    };

    /// <summary>
    /// 营地整体对抗聚合：每个守卫用其 alertness 各自对同一 stealth 独立掷点（<see cref="NightWatchContest.Resolve"/>），
    /// **任一**守卫掷中即全营发现。为保证随机序列确定（测试可复现），即便已发现仍走完每个守卫一次掷点。
    /// 守卫名单为空（无人站岗）→ 恒未发现（没人望风，袭击者长驱直入）。
    /// </summary>
    public static bool ResolveCampDetection(IEnumerable<float> guardAlertness, float stealth, IRandomSource rng)
    {
        if (rng is null)
            throw new ArgumentNullException(nameof(rng));
        if (guardAlertness is null)
            return false;

        bool detected = false;
        foreach (float a in guardAlertness)
        {
            ContestResult r = NightWatchContest.Resolve(a, stealth, rng);
            if (r.Detected)
                detected = true; // 不 break：每守卫消耗一次 roll，随机序列确定
        }
        return detected;
    }
}
