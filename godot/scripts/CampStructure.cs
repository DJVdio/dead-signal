using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>营地可破坏结构的种类：围栏（封闭墙段）/ 大门（营地关口）/ 门（可开关的门板，见 <see cref="DoorLogic"/>）。</summary>
public enum CampStructureKind
{
    Fence,
    Gate,

    /// <summary>
    /// 一扇可开关的门（建筑门 / 营地大门皆是）。
    /// <para>
    /// <b>门为什么是"可破坏结构"</b>：用户拍板「丧尸不会开门，只会砸」——<b>关着的门对丧尸就是一堵墙</b>。
    /// 把门纳入本体系后，丧尸砸门<b>一行新 AI 代码都不用写</b>：<c>BreachController</c> 的可达性探测失败 →
    /// <c>BreachLogic</c> 择最近结构 → 砸 → HP 归零 → 移碰撞 + 重烘焙导航开出缺口 → 涌入。整条链路原样复用。
    /// </para>
    /// <para>
    /// 门与围栏/大门的唯一区别是**它还能被人开关**（<see cref="DoorLogic.Blocks"/>）：开着时退出破防候选（不挡路的东西没人砸），
    /// 关着/锁着时与一段围栏无异。
    /// </para>
    /// </summary>
    Door,
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
    // —— 门体档（可开关的门板，见 DoorLogic）——
    DoorWood,          // 木门（民居的门，一脚就能踹裂那种）
    DoorReinforced,    // 加固木门
    DoorMetal,         // 金属门
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
        // 门体：**整道屏障上最薄的一环**——一堵基础围栏 150，而它上面那扇木门只有 60。
        // 这正是门"值得被砸"的原因（也是玩家该记得锁门/加固门的原因）。
        // 破防耗时不再由此表单独决定：砸墙伤害现在由**武器**派生（见 StructureDamage）——丧尸每爪 7.5（爪击均值 3 × 撕扯系数 2.5）
        // ⇒ 木门 8 爪破、金属门 30 爪破；换成一把破甲锤（每击 48）则木门 2 击、金属门 5 击。
        StructureTier.DoorWood        => 60,
        StructureTier.DoorReinforced  => 120,
        StructureTier.DoorMetal       => 220,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知结构等级"),
    };

    /// <summary>该等级属于围栏 / 大门 / 门体（供建图/升级校验：档位不能跨类互升）。</summary>
    public static CampStructureKind KindOf(StructureTier tier) => tier switch
    {
        StructureTier.GateBasic or StructureTier.GateSheetMetal or StructureTier.GateCastMetal
            => CampStructureKind.Gate,
        StructureTier.DoorWood or StructureTier.DoorReinforced or StructureTier.DoorMetal
            => CampStructureKind.Door,
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
        StructureTier.DoorWood        => "一扇普通的木门。它挡得住风，挡得住视线，也挡得住不会拧门把手的东西——至少能挡一会儿。",
        StructureTier.DoorReinforced  => "钉了横木的门。加固它花了半个下午，而这半个下午，可能就是你日后多活的那几分钟。",
        StructureTier.DoorMetal       => "一扇金属门。砸上去只会震得你虎口发麻——门外那位有的是时间，问题是你有没有。",
        _ => "",
    };
}

/// <summary>
/// 「结构等级 → 建造成本 + 建造工时」纯数据表（<b>[批次20] 新增</b>，数值拟定待调）。
/// <para>
/// <b>围栏那几档只服务「升级」，不服务「新建」，更不服务「拆除」</b>（用户拍板的三条）：
/// <list type="number">
/// <item><b>墙不能建</b>——玩家不能新建围墙、不能自由划线布局。</item>
/// <item><b>只能升级开局自带的围栏</b>（基础 → 加固 → 铁皮 → 全金属，成本即本表）。</item>
/// <item><b>墙不可拆，只能砸</b>（走破防那条路：<see cref="StructureDamage"/> / <c>BreachLogic</c>）——<b>零回收</b>。</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ <b>「墙不能建」的理由不是"没做"，是刻意的设计防御</b>：可自由摆墙 ⇒ 玩家能搭 kill box
/// （用墙的迷宫牵着敌人寻路，把一场战斗变成一道几何题），会<b>架空视野锥 / 噪音 / 包抄 / 掩体 / 岗哨</b>
/// 这一整套刚建起来的系统。「没有墙可摆」是个终局解法——寻路 bug 修不完，而没有墙就没有迷宫。
/// <b>不要"好心"把建墙加回来</b>，也不要为了体验友好给墙开"拆一半回来"的后门。
/// ⇒ 围墙是<b>不可逆的投入</b>：建错了位置只能砸掉，材料一点回不来。
/// </para>
/// <para>
/// <b>门与大门则可拆</b>（<see cref="SalvageLogic.CanSalvageStructure"/>）——它们是装上去的东西，卸得下来。
/// 家具的成本表另见 <see cref="FurnitureBuildCost"/>（用户例子里那 16 木料落在工作台上）。
/// </para>
/// 拆解返还与工时见 <see cref="SalvageLogic"/>；材料键对齐 <see cref="Materials"/> 目录。
/// </summary>
public static class StructureBuildCost
{
    private static IReadOnlyDictionary<string, int> Cost(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (key, qty) in items)
        {
            d[key] = qty;
        }
        return d;
    }

    /// <summary>
    /// 某档结构的建造材料（材料键 → 数量）。
    /// <b>围栏档＝升级到该档要付的料</b>（不是"新建"——墙不能建）；门/大门档兼作拆除返还的依据。
    /// <para>
    /// ⚠️ <b>围栏那几档是「<b>一格 100px</b>」的料，不是一整面墙的</b>（围栏已切格，见 <c>CampMain.FenceSegment</c>；
    /// 升级下令按边、结算按格，见 <see cref="FenceUpgradeLogic"/>）。一条 16 格的南墙升到支柱加固 = 16 × (4 木料 + 2 铁钉)
    /// ＝ <b>64 木料 + 32 铁钉</b>。<b>大门则是单独一处</b>（不切格），故它那几行就是整扇门的料 —— 这正是
    /// 「<b>先大门，后围栏</b>」这条硬顺序的来处：同样一笔料，砸在大门上厚了 400-250=150 血，
    /// 摊到 16 格围栏上却只够升不到两格。
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, int> Of(StructureTier tier) => tier switch
    {
        // 围栏（**每格 100px**）：木桩 → 加支柱 → 钉铁皮 → 全金属。木料逐档让位给金属。**只能升上去，拆不下来。**
        StructureTier.FenceBasic      => Cost(("wood", 4)),
        StructureTier.FenceReinforced => Cost(("wood", 4), ("nails", 2)),
        StructureTier.FenceSheetMetal => Cost(("wood", 2), ("iron", 3), ("nails", 2)),
        StructureTier.FenceFullMetal  => Cost(("iron", 8), ("components", 1)),        // [T46] 铁 8（原：金属锭 4）。仍严格贵于铁皮围栏（铁 3）。
        // 大门：比同档围栏更费料（它得又大又能开）。
        StructureTier.GateBasic       => Cost(("wood", 24), ("nails", 8)),
        StructureTier.GateSheetMetal  => Cost(("wood", 12), ("iron", 16), ("nails", 8)),
        StructureTier.GateCastMetal   => Cost(("iron", 48), ("components", 6)),      // [T46] 铁 48（原：金属锭 24）。全表最贵的一笔——终局工程，见下方注释。
        // 门体：整道屏障上最薄也最便宜的一环（血量同理，见 MaxHp）。
        StructureTier.DoorWood        => Cost(("wood", 8), ("nails", 4)),
        StructureTier.DoorReinforced  => Cost(("wood", 12), ("nails", 8)),
        StructureTier.DoorMetal       => Cost(("iron", 20), ("components", 2)),      // [T46] 铁 20（原：废金属 12 + 金属锭 4 = 12 + 8）。
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知结构等级"),
    };

    // ⚠️ [T46·拟定待调，用户可一个数字改掉] **铸铁大门（铁 48）是全表最贵的一笔，接近全游戏铁总量。**
    // 盘过供给：搜刮点掉落 ≈ 44 铁 + 营地废墟 ≈ 9 铁 ⇒ 一周目能拿到的铁 ≈ **53**（再加拆除回收的一点）。
    // ⇒ 造这扇门 = 把几乎所有的铁都焊在一扇门上（不修枪、不做箭、不升围栏）。**这是刻意的终局工程**，
    //   也忠实于它合并前的原始定价（金属锭 24 —— 而按"锭要熔炼提纯"的原设定，那本来就意味着更多的废金属）。
    //   合并前它**根本造不出来**（金属锭没有任何获取途径）；现在它至少是"贵得要下决心"，而不是"不可能"。
    // 若用户觉得太狠：改这一个数字即可，不牵动别处（各档位序只要求 铸铁 > 铁皮大门的铁 16）。

    /// <summary>
    /// 某档结构的建造工时（游戏分钟；拆解取其一半，见 <see cref="SalvageLogic.WorkMinutesOfStructure"/>）。
    /// <para>
    /// ⚠️ <b>围栏那几行是死数</b>：墙拆不了（<see cref="SalvageLogic.WorkMinutesOfStructure"/> 对围栏恒 0），
    /// 而**砌墙是站在墙边干的活、按实时秒推进**（与搜刮/撬锁/静默拆除同一形态，可中断、非模态），
    /// 不是工作台上的工时制 —— 故围栏升级/修复的耗时另有单一真源：<see cref="FenceUpgradeLogic.BuildWorkSeconds"/>。
    /// </para>
    /// </summary>
    public static int BuildMinutes(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic      => 120,
        StructureTier.FenceReinforced => 180,
        StructureTier.FenceSheetMetal => 240,
        StructureTier.FenceFullMetal  => 400,
        StructureTier.GateBasic       => 180,
        StructureTier.GateSheetMetal  => 300,
        StructureTier.GateCastMetal   => 480,
        StructureTier.DoorWood        => 60,
        StructureTier.DoorReinforced  => 90,
        StructureTier.DoorMetal       => 150,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "未知结构等级"),
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

    /// <summary>
    /// 当前血量。<b>小数</b>——砸墙伤害由武器派生（<see cref="StructureDamage"/>），出来的是 7.5 / 43.2 这类值，
    /// 全程不取整（精度通则：伤害不取整）。展示层自行按需格式化。
    /// </summary>
    public double Hp { get; private set; }

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
    /// 读档：按等级重建结构并直接把血量灌回去（夹到 [0, MaxHp]）。
    /// <para>
    /// 本类刻意<b>只有 <see cref="TakeDamage"/>、没有加血通道</b>——围栏砸坏了就是砸坏了，这是设计（墙不可修不可拆）。
    /// 读档不是"修墙"，是把世界摆回它本来的样子，所以走这个独立入口，而不是给 TakeDamage 开一个负伤害的后门。
    /// </para>
    /// </summary>
    public static CampStructureState Restore(StructureTier tier, double hp)
    {
        var s = new CampStructureState(tier);
        s.Hp = Math.Clamp(hp, 0, s.MaxHp);
        return s;
    }

    /// <summary>
    /// 承受一次伤害（负/零伤害忽略；血量夹到 [0,MaxHp]）。
    /// 返回是否**因本次伤害被摧毁**——血量由 &gt;0 落到 ≤0 的那一击返回 <c>true</c>，
    /// 已摧毁后再打返回 <c>false</c>（消费层据此只开一次缺口、只重烘焙一次）。
    /// </summary>
    public bool TakeDamage(double amount)
    {
        if (amount <= 0 || IsDestroyed)
        {
            return false;
        }
        Hp = Math.Max(0, Hp - amount);
        return IsDestroyed; // 本击前 Hp>0，若此刻见底即本击摧毁
    }
}
