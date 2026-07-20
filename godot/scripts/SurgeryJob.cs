using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 纯 C# 手术工时状态机。它只记录稳定键和时间，不持有 Pawn / HealthCondition / BodyPart 对象，
// 不碰库存，也不推进出血。患者的实时出血仍由 Actor.Body.TickBleed 唯一负责。

public enum SurgeryKind
{
    Treatment,
    Amputation,
    Prosthetic,
}

public enum SurgeryAdvanceStatus
{
    Approaching,
    InProgress,
    Paused,
    Ready,
    Interrupted,
    Settled,
}

/// <summary>可直接落档的手术快照；只含角色 id、目标稳定键和标量，不含运行时对象引用。</summary>
public sealed class SurgeryJobSnapshot
{
    public SurgeryKind Kind { get; set; }
    public int SurgeonId { get; set; }
    public int PatientId { get; set; }
    public HealthConditionType? ConditionType { get; set; }
    public string? BodyPartKey { get; set; }
    public BodyRegion? ProstheticRegion { get; set; }
    public ProstheticGrade? ProstheticGrade { get; set; }
    public List<string> MaterialKeys { get; set; } = new();
    public int TotalMinutes { get; set; }
    public int ElapsedMinutes { get; set; }
    public bool Interrupted { get; set; }
    public bool Settled { get; set; }
    public bool Operating { get; set; }
    public bool RequiresBedRetention { get; set; }
}

/// <summary>
/// 一台进行中的手术。基础耗时固定 30 游戏分钟（0.5 小时）；速度加成除总工时并向上取整。
/// 到点只开放一次结算权，实际扣料/掷点/伤情变更由消费层在取得结算权后执行。
/// </summary>
public sealed class SurgeryJob
{
    public const int BaseMinutes = 30;

    private bool _interrupted;
    private bool _settled;
    private bool _operating;

    private SurgeryJob(
        SurgeryKind kind,
        int surgeonId,
        int patientId,
        HealthConditionType? conditionType,
        string? bodyPartKey,
        BodyRegion? prostheticRegion,
        ProstheticGrade? prostheticGrade,
        IEnumerable<string>? materialKeys,
        int totalMinutes,
        int elapsedMinutes,
        bool interrupted,
        bool settled,
        bool operating,
        bool requiresBedRetention)
    {
        if (surgeonId < 0) throw new ArgumentOutOfRangeException(nameof(surgeonId));
        if (patientId < 0) throw new ArgumentOutOfRangeException(nameof(patientId));
        if (totalMinutes < 1) throw new ArgumentOutOfRangeException(nameof(totalMinutes));
        if (kind == SurgeryKind.Prosthetic && (prostheticRegion is null || prostheticGrade is null))
            throw new ArgumentException("安装假肢必须保存部位与品级稳定键");
        if (kind != SurgeryKind.Prosthetic && conditionType is null)
            throw new ArgumentException("治疗/截肢必须保存伤情类别稳定键");

        Kind = kind;
        SurgeonId = surgeonId;
        PatientId = patientId;
        ConditionType = conditionType;
        BodyPartKey = bodyPartKey;
        ProstheticRegion = prostheticRegion;
        ProstheticGrade = prostheticGrade;
        MaterialKeys = (materialKeys ?? Array.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray();
        TotalMinutes = totalMinutes;
        ElapsedMinutes = Math.Clamp(elapsedMinutes, 0, totalMinutes);
        _interrupted = interrupted;
        _settled = settled;
        _operating = operating;
        RequiresBedRetention = requiresBedRetention;
    }

    public SurgeryKind Kind { get; }
    public int SurgeonId { get; }
    public int PatientId { get; }
    public HealthConditionType? ConditionType { get; }
    public string? BodyPartKey { get; }
    public BodyRegion? ProstheticRegion { get; }
    public ProstheticGrade? ProstheticGrade { get; }
    public IReadOnlyList<string> MaterialKeys { get; }
    public int TotalMinutes { get; }
    public int ElapsedMinutes { get; private set; }
    public int RemainingMinutes => Math.Max(0, TotalMinutes - ElapsedMinutes);
    public bool IsReady => !_interrupted && !_settled && ElapsedMinutes >= TotalMinutes;
    public bool IsInterrupted => _interrupted;
    public bool IsSettled => _settled;
    public bool IsOperating => _operating && !_interrupted && !_settled;
    public bool RequiresBedRetention { get; }

    public static int DurationMinutes(double surgerySpeedMultiplier)
    {
        if (double.IsNaN(surgerySpeedMultiplier) || double.IsInfinity(surgerySpeedMultiplier) || surgerySpeedMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(surgerySpeedMultiplier));
        return Math.Max(1, (int)Math.Ceiling(BaseMinutes / surgerySpeedMultiplier));
    }

    public static SurgeryJob ForCondition(
        SurgeryKind kind,
        int surgeonId,
        int patientId,
        HealthConditionType conditionType,
        string? bodyPartKey,
        IEnumerable<string>? materialKeys,
        double surgerySpeedMultiplier,
        bool requiresBedRetention = false)
    {
        if (kind == SurgeryKind.Prosthetic)
            throw new ArgumentException("假肢安装请使用 ForProsthetic", nameof(kind));
        return new SurgeryJob(kind, surgeonId, patientId, conditionType, bodyPartKey,
            null, null, materialKeys, DurationMinutes(surgerySpeedMultiplier), 0, false, false,
            operating: false, requiresBedRetention: requiresBedRetention);
    }

    public static SurgeryJob ForProsthetic(
        int surgeonId,
        int patientId,
        string bodyPartKey,
        BodyRegion region,
        ProstheticGrade grade,
        IEnumerable<string>? materialKeys,
        double surgerySpeedMultiplier,
        bool requiresBedRetention = false)
        => new(SurgeryKind.Prosthetic, surgeonId, patientId, null,
            string.IsNullOrWhiteSpace(bodyPartKey) ? throw new ArgumentException("假肢目标部位稳定键不可空", nameof(bodyPartKey)) : bodyPartKey,
            region, grade,
            materialKeys, DurationMinutes(surgerySpeedMultiplier), 0, false, false,
            operating: false, requiresBedRetention: requiresBedRetention);

    /// <summary>医生到达锁定手术位后开工。接近阶段不推进工时，也不开放结算。</summary>
    public SurgeryAdvanceStatus TryStartOperating(
        bool surgeonArrived,
        bool patientAtLockedPosition,
        bool bedStillOccupied)
    {
        if (_settled) return SurgeryAdvanceStatus.Settled;
        if (_interrupted) return SurgeryAdvanceStatus.Interrupted;
        if (!surgeonArrived || !patientAtLockedPosition || (RequiresBedRetention && !bedStillOccupied))
            return SurgeryAdvanceStatus.Approaching;
        _operating = true;
        return SurgeryAdvanceStatus.InProgress;
    }

    /// <summary>
    /// 带空间姿态的唯一运行时推进入口。未抵达时绝不推进；开工后医生/病人离开锁定位置，
    /// 或开工时锁定的床位不再由病人占用，立即中断。
    /// </summary>
    public SurgeryAdvanceStatus AdvanceSpatial(
        int gameMinutes,
        bool clockPaused,
        bool patientAlive,
        bool surgeonAlive,
        bool targetExists,
        bool surgeonAtLockedPosition,
        bool patientAtLockedPosition,
        bool bedStillOccupied)
    {
        if (_settled) return SurgeryAdvanceStatus.Settled;
        if (_interrupted) return SurgeryAdvanceStatus.Interrupted;
        if (!patientAlive || !surgeonAlive || !targetExists)
        {
            _interrupted = true;
            return SurgeryAdvanceStatus.Interrupted;
        }
        if (!_operating)
            return SurgeryAdvanceStatus.Approaching;
        if (!surgeonAtLockedPosition || !patientAtLockedPosition
            || (RequiresBedRetention && !bedStillOccupied))
        {
            _interrupted = true;
            return SurgeryAdvanceStatus.Interrupted;
        }
        return AdvanceCore(gameMinutes, clockPaused);
    }

    /// <summary>
    /// 推进游戏分钟。患者死亡或目标消失优先中断；暂停仅冻结工时。
    /// 本方法绝不扣血——实时出血由 Actor.Body.TickBleed 的既有世界循环推进。
    /// </summary>
    public SurgeryAdvanceStatus Advance(int gameMinutes, bool clockPaused, bool patientAlive, bool targetExists)
    {
        if (_settled) return SurgeryAdvanceStatus.Settled;
        if (_interrupted) return SurgeryAdvanceStatus.Interrupted;
        if (!patientAlive || !targetExists)
        {
            _interrupted = true;
            return SurgeryAdvanceStatus.Interrupted;
        }
        if (!_operating) return SurgeryAdvanceStatus.Approaching;
        return AdvanceCore(gameMinutes, clockPaused);
    }

    private SurgeryAdvanceStatus AdvanceCore(int gameMinutes, bool clockPaused)
    {
        if (IsReady) return SurgeryAdvanceStatus.Ready;
        if (clockPaused) return SurgeryAdvanceStatus.Paused;
        if (gameMinutes > 0)
            ElapsedMinutes = Math.Min(TotalMinutes, ElapsedMinutes + gameMinutes);
        return IsReady ? SurgeryAdvanceStatus.Ready : SurgeryAdvanceStatus.InProgress;
    }

    /// <summary>到点后只允许一个调用方取得结算权，防止重复扣料和重复累计南丁格尔手术台数。</summary>
    public bool TryClaimSettlement()
    {
        if (!IsReady) return false;
        _settled = true;
        return true;
    }

    public SurgeryJobSnapshot Snapshot() => new()
    {
        Kind = Kind,
        SurgeonId = SurgeonId,
        PatientId = PatientId,
        ConditionType = ConditionType,
        BodyPartKey = BodyPartKey,
        ProstheticRegion = ProstheticRegion,
        ProstheticGrade = ProstheticGrade,
        MaterialKeys = MaterialKeys.ToList(),
        TotalMinutes = TotalMinutes,
        ElapsedMinutes = ElapsedMinutes,
        Interrupted = _interrupted,
        Settled = _settled,
        Operating = _operating,
        RequiresBedRetention = RequiresBedRetention,
    };

    public static SurgeryJob Restore(SurgeryJobSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SurgeryJob(snapshot.Kind, snapshot.SurgeonId, snapshot.PatientId,
            snapshot.ConditionType, snapshot.BodyPartKey, snapshot.ProstheticRegion, snapshot.ProstheticGrade,
            snapshot.MaterialKeys, snapshot.TotalMinutes, snapshot.ElapsedMinutes,
            snapshot.Interrupted, snapshot.Settled, snapshot.Operating, snapshot.RequiresBedRetention);
    }
}
