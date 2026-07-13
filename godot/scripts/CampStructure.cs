using System;

namespace DeadSignal.Godot;

/// <summary>营地可破坏结构的种类：围栏（封闭墙段）/ 大门（营地关口）。</summary>
public enum CampStructureKind
{
    Fence,
    Gate,
}

/// <summary>
/// 围栏/大门的建造等级。血量数值全为「拟定待调」占位；升级机制（花料把低档换高档）后续做，
/// 本块只用「等级→血量」表在建图时给初始结构定血量。
/// </summary>
public enum StructureTier
{
    // —— 围栏档 ——
    FenceBasic,        // 基础围栏
    FenceReinforced,   // 支柱加固围栏
    FenceSheetMetal,   // 铁皮围栏
    FenceFullMetal,    // 全金属围栏
    // —— 大门档 ——
    GateBasic,         // 基础大门
    GateSheetMetal,    // 铁皮加固大门
    GateCastMetal,     // 金属浇筑大门
}

/// <summary>
/// 「围栏/大门等级 → 最大血量」纯数据表（拟定待调；升级机制后续做）。
/// 基础档：围栏 150 / 大门 250。升级档数值先存在此，供后续升级系统读取。
/// </summary>
public static class CampStructureTable
{
    public static int MaxHp(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic      => 150,
        StructureTier.FenceReinforced => 250,
        StructureTier.FenceSheetMetal => 400,
        StructureTier.FenceFullMetal  => 750,
        StructureTier.GateBasic       => 250,
        StructureTier.GateSheetMetal  => 400,
        StructureTier.GateCastMetal   => 800,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知结构等级"),
    };

    /// <summary>该等级属于围栏还是大门（供建图/升级校验：围栏档不能升成大门档）。</summary>
    public static CampStructureKind KindOf(StructureTier tier) => tier switch
    {
        StructureTier.GateBasic or StructureTier.GateSheetMetal or StructureTier.GateCastMetal
            => CampStructureKind.Gate,
        _ => CampStructureKind.Fence,
    };

    /// <summary>某类结构的基础（未升级）等级。</summary>
    public static StructureTier BaseTier(CampStructureKind kind) =>
        kind == CampStructureKind.Gate ? StructureTier.GateBasic : StructureTier.FenceBasic;

    /// <summary>
    /// 「结构等级 → 一行风味描述」（黑色幽默，玩家可见文案）。
    /// 注：营地结构不是库存物品、当前无建造/查看面板承接此文案（不同于武器/护甲经 StashPanel 上屏）——
    /// 字段与文案先备齐（含全 7 档），待日后建造 UI 落地即可直接展示（团队裁决：先落数据，无 UI 则说明取舍）。
    /// </summary>
    public static string Blurb(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic      => "几根木桩钉起来的围栏，能挡君子，挡不住饿疯的东西。",
        StructureTier.FenceReinforced => "加了支柱的围栏，结实了些——至少能让它们多撞几下。",
        StructureTier.FenceSheetMetal => "钉了铁皮的围栏，好看不好看两说，扛揍是真扛揍。",
        StructureTier.FenceFullMetal  => "全金属围栏，密不透风——把外面的世界关在外面，也把你关在里面。",
        StructureTier.GateBasic       => "一扇基础大门，关得住的时候是门，关不住的时候是纸。",
        StructureTier.GateSheetMetal  => "铁皮加固的大门，推起来沉，撞起来更沉。",
        StructureTier.GateCastMetal   => "金属浇筑的大门，固若金汤——只要开门的人还靠得住。",
        _ => "",
    };
}

/// <summary>
/// 一处可破坏结构的血量状态（纯逻辑，无 Godot 依赖）：持血量、承伤、判摧毁。
/// 空间/碰撞/导航开口由 Godot 消费层（CampMain）处理，本类只管数值——被摧毁后由消费层
/// 移除实心并重烘焙导航开出缺口。敌人主动攻击的 AI 属袭营/守卫块（后续）。
/// </summary>
public sealed class CampStructureState
{
    public CampStructureKind Kind { get; }
    public StructureTier Tier { get; }
    public int MaxHp { get; }
    public int Hp { get; private set; }

    /// <summary>已被摧毁（血量见底）。</summary>
    public bool IsDestroyed => Hp <= 0;

    /// <summary>存活血量占比 [0,1]（供受损视觉/守卫择敌优先级用）。</summary>
    public float HealthFraction => MaxHp > 0 ? (float)Hp / MaxHp : 0f;

    /// <summary>按等级建结构：血量取该等级上限，起始满血。</summary>
    public CampStructureState(StructureTier tier)
    {
        Tier = tier;
        Kind = CampStructureTable.KindOf(tier);
        MaxHp = CampStructureTable.MaxHp(tier);
        Hp = MaxHp;
    }

    /// <summary>
    /// 承受一次伤害（负/零伤害忽略；血量夹到 [0,MaxHp]）。
    /// 返回是否**因本次伤害被摧毁**——血量由 &gt;0 落到 ≤0 的那一击返回 <c>true</c>，
    /// 已摧毁后再打返回 <c>false</c>（消费层据此只开一次缺口、只重烘焙一次）。
    /// </summary>
    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDestroyed)
        {
            return false;
        }
        Hp = Math.Max(0, Hp - amount);
        return IsDestroyed; // 本击前 Hp>0，若此刻见底即本击摧毁
    }
}
