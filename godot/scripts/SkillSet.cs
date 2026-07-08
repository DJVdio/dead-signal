using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载幸存者技能的全部规则：技能项 → 等级的持有态、门槛查询、经验累积升级（含从"未掌握"习得初级）、
// authored 初始分布直设。配方系统后续按"制作者"技能门槛解锁配方、并在制作成功时回喂经验，均消费本对象。

/// <summary>
/// 技能项（可扩）。当前含制作相关三项（纺织/机械/化学）与三项通用（医疗/烹饪/战斗）。
/// 配方解锁的门槛主要读制作相关三项；其余留作后续玩法接口。
/// </summary>
public enum SkillType
{
    /// <summary>纺织：布料/衣物（如粗布背心）。</summary>
    Textile,

    /// <summary>机械/机工：金属件/工具/假肢等。</summary>
    Mechanical,

    /// <summary>化学：药剂/燃料/爆材等。</summary>
    Chemistry,

    /// <summary>医疗：手术/救治（后续消费）。</summary>
    Medical,

    /// <summary>烹饪：食物加工（后续消费）。</summary>
    Cooking,

    /// <summary>战斗：武器熟练（后续消费）。</summary>
    Combat,
}

/// <summary>
/// 技能等级（数值序，供门槛 &gt;= 比较）：未掌握=0，初级=1，中级=2，高级=3。
/// 配方门槛用 <see cref="SkillSet.HasSkill"/> 表达（如"制作者有初级纺织"= 纺织 &gt;= Novice）。
/// </summary>
public enum SkillLevel
{
    /// <summary>未掌握（尚未习得该技能）。</summary>
    None = 0,

    /// <summary>初级。</summary>
    Novice = 1,

    /// <summary>中级。</summary>
    Adept = 2,

    /// <summary>高级。</summary>
    Expert = 3,
}

/// <summary>技能等级中文显示名（供 UI/日志读取）。</summary>
public static class SkillLevelExtensions
{
    public static string Label(this SkillLevel level) => level switch
    {
        SkillLevel.Novice => "初级",
        SkillLevel.Adept => "中级",
        SkillLevel.Expert => "高级",
        _ => "未掌握",
    };
}

/// <summary>技能项中文显示名（供 UI/日志读取）。</summary>
public static class SkillTypeExtensions
{
    public static string Label(this SkillType skill) => skill switch
    {
        SkillType.Textile => "纺织",
        SkillType.Mechanical => "机械",
        SkillType.Chemistry => "化学",
        SkillType.Medical => "医疗",
        SkillType.Cooking => "烹饪",
        SkillType.Combat => "战斗",
        _ => skill.ToString(),
    };
}

/// <summary>
/// 单个幸存者的技能集（纯逻辑，无 Godot 依赖）：技能项 → (等级 + 当前级内经验进度)。
/// 模型：<see cref="GainExperience"/> 累积经验，每满一格 <see cref="ExperiencePerLevel"/> 升一级
/// （未掌握→初级→中级→高级），高级封顶、余量丢弃；<see cref="Train"/> 直设 authored 初始等级。
/// 阈值/等级数皆"拟定待调"，用于走通规则形态；配方门槛读 <see cref="HasSkill"/>/<see cref="LevelOf"/>。
/// </summary>
public sealed class SkillSet
{
    /// <summary>技能等级上限（封顶=高级）。</summary>
    public const SkillLevel MaxLevel = SkillLevel.Expert;

    /// <summary>升一级所需经验（拟定待调；每级等量）。</summary>
    public const int ExperiencePerLevel = 100;

    /// <summary>单条技能状态：等级 + 朝下一级累积的经验（[0, <see cref="ExperiencePerLevel"/>)）。</summary>
    private sealed class Entry
    {
        public SkillLevel Level;
        public int Experience;
    }

    /// <summary>仅登记已"接触过"（被 Train 或 GainExperience 触及）的技能；其余视为 <see cref="SkillLevel.None"/>。</summary>
    private readonly Dictionary<SkillType, Entry> _skills = new();

    /// <summary>某技能当前等级（未登记 = 未掌握）。</summary>
    public SkillLevel LevelOf(SkillType skill)
        => _skills.TryGetValue(skill, out Entry? e) ? e.Level : SkillLevel.None;

    /// <summary>某技能是否达到门槛等级（默认门槛=初级）。配方解锁的权威判据。</summary>
    public bool HasSkill(SkillType skill, SkillLevel minLevel = SkillLevel.Novice)
        => LevelOf(skill) >= minLevel;

    /// <summary>某技能朝下一级已累积的经验（未登记/封顶 = 0）。供 UI 进度条读取。</summary>
    public int ExperienceToward(SkillType skill)
        => _skills.TryGetValue(skill, out Entry? e) ? e.Experience : 0;

    /// <summary>
    /// 直设 authored 初始等级（占位人设数据 / 掉落传授等）：clamp 到 [None, <see cref="MaxLevel"/>]，级内经验清零。
    /// 与经验成长解耦——这是"直接习得/提级"的口子，不走阈值累积。
    /// </summary>
    public void Train(SkillType skill, SkillLevel level)
    {
        SkillLevel clamped = level < SkillLevel.None ? SkillLevel.None
            : level > MaxLevel ? MaxLevel : level;
        if (clamped == SkillLevel.None)
        {
            _skills.Remove(skill);
            return;
        }
        Entry e = Get(skill);
        e.Level = clamped;
        e.Experience = 0;
    }

    /// <summary>
    /// 加经验并结算升级：每满 <see cref="ExperiencePerLevel"/> 升一级（可一次跨多级），余量转入下一级进度；
    /// 到 <see cref="MaxLevel"/> 封顶、丢弃多余经验；非正 <paramref name="amount"/> 空操作。
    /// 返回本次升的级数（供制作成功回喂时提示"XX 升级了"）。
    /// </summary>
    public int GainExperience(SkillType skill, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }
        Entry e = Get(skill);
        int levelsGained = 0;
        e.Experience += amount;
        while (e.Level < MaxLevel && e.Experience >= ExperiencePerLevel)
        {
            e.Experience -= ExperiencePerLevel;
            e.Level++;
            levelsGained++;
        }
        if (e.Level >= MaxLevel)
        {
            e.Experience = 0; // 封顶不再累积
        }
        return levelsGained;
    }

    /// <summary>已掌握（等级 &gt;= 初级）的技能 → 等级 只读快照，供 UI/角色面板读取。未掌握不入表。</summary>
    public IReadOnlyDictionary<SkillType, SkillLevel> Snapshot()
    {
        var snap = new Dictionary<SkillType, SkillLevel>();
        foreach (KeyValuePair<SkillType, Entry> kv in _skills)
        {
            if (kv.Value.Level >= SkillLevel.Novice)
            {
                snap[kv.Key] = kv.Value.Level;
            }
        }
        return snap;
    }

    private Entry Get(SkillType skill)
    {
        if (!_skills.TryGetValue(skill, out Entry? e))
        {
            e = new Entry();
            _skills[skill] = e;
        }
        return e;
    }
}
