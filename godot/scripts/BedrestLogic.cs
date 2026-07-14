using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ShiftSchedule.cs / HealthConditions.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载「卧床养病」的全部规则（批次21，impl-bedrest）：
//
// ★ 为什么要有这个文件 —— 修的是三处旧账（见 journal 里程碑1）：
//   ① **休养是"整日一个布尔"**：CampMain 旧写法 `resting = p.Role != PawnRole.Guard`，且结算只在**黎明**跑一次。
//      而黎明时 PawnRoleManager 不重排角色（聚餐相位直接 return）⇒ 那个布尔读到的是**昨晚夜里的角色**。
//      于是"白天在营地睡了整整三个相位"对治疗**零贡献** —— 这就是"白天睡觉吃不到治疗加成"的真正根因。
//   ② **床是假的**：旧写法 `restedInBed = resting`，营地里根本没有床这个东西。
//   ③ **休养不是玩家的选择**：它由角色隐式推导，玩家无从主动命令一个伤员"去躺着"。
//
// ★ 解法 —— 布尔 → 占比（**推广而非改写**）：
//   一个人一昼夜要经过若干相位，每个相位他处于某种「休养质量」<see cref="RestQuality"/>（不休养/打地铺/睡床）。
//   把这些相位累计起来（<see cref="RestLedger"/>），得到当日的 **休养占比 RestFraction** 与 **睡床占比 BedFraction**。
//   喂给 <c>HealthConditionSet.TickDay(restFraction:, bedFraction:)</c> —— 旧的布尔 true/false 恰是占比 1.0/0.0
//   的特例，**端点逐比特等价**，故既有结算零回归。白天睡觉自此天然计入。
//
// ★ 与双班硬日程（ShiftSchedule）的关系 —— **无新语义、全是现有模型的推论**：
//   · 白天卧床**不顶掉任何工作时间**：白天营地本来就没有工时（生产仅 NightCrew×NightAct 推进，见
//     <see cref="ShiftSchedule.IsWorkPhaseFor"/>），且留守者白天本就被日程强制睡（PawnRoleManager）。
//     所以"白天躺床上"= 把他本来就在睡的觉，睡到床上去 —— 代价为零，收益是床的加成。这不是白送：**床要造、要占**。
//   · 夜里卧床**顶掉产出**：<see cref="PawnRole.Bedrest"/> 与 Guard/Reading 互斥（同为夜间指派角色，一人一个），
//     养病者夜里不站岗、不生产、不读书。这才是卧床的真代价 —— 一张床换一个人的夜班。
//   · 聚餐是模态相位，全员参加（爬起来吃饭，吃完继续躺）。
//
// 空间执行（走到床边/占床/起床）落 Godot 运行时层（CampMain）；本文件只出纯规则。数值全部「拟定待调」。

/// <summary>某人在**某一个相位**里的休养质量。占比累计见 <see cref="RestLedger"/>。</summary>
public enum RestQuality
{
    /// <summary>不休养：在勤（站岗/读书/生产）、出门在外、或只是待命。伤情该恶化恶化、术后该慢慢愈合。</summary>
    None,

    /// <summary>打地铺：在休养，但没床（床不够/没造）。吃休养加成，不吃睡床加成。</summary>
    Floor,

    /// <summary>睡床：在休养且占着一张床。休养加成 + 睡床加成全吃。</summary>
    Bed,
}

/// <summary>玩家下「上床养病」令的判定结果。</summary>
public enum BedrestOrderStatus
{
    /// <summary>可以躺：给他分一张床（或他本来就占着一张）。</summary>
    Ok,

    /// <summary>他不在营地（出门探索去了）——够不着。</summary>
    NotInCamp,

    /// <summary>营地里没有空床了（床是要造的，一人一张）。</summary>
    NoFreeBed,

    /// <summary>聚餐相位是模态过渡，全员在饭桌上，此刻不下令（吃完再躺）。</summary>
    MealPhase,

    /// <summary>人已经死了。</summary>
    Dead,
}

/// <summary>一次「上床养病」下令的裁定：能不能躺 + 给玩家看的话。</summary>
public readonly record struct BedrestOrder(BedrestOrderStatus Status, string Message)
{
    /// <summary>是否放行（调用方据此派人走去床边躺下）。</summary>
    public bool Allowed => Status == BedrestOrderStatus.Ok;
}

/// <summary>
/// 营地床位登记册（纯逻辑）：**一人一床、一床一人**。床本身是营地家具（见 <see cref="FurnitureBuildCost"/> 的「床」，
/// 可制作、可拆除），本册只管"谁占了哪张"。空间执行（走过去躺下）归 CampMain。
/// </summary>
public sealed class BedRegistry
{
    // bedKey（家具实例名，如 "床#2"）→ 占用者 pawnId。没进表的床=空床。
    private readonly Dictionary<string, int> _occupied = new();
    private readonly List<string> _beds = new();

    /// <summary>登记一张床（建图/建造时调）。重复登记同名床是无操作。</summary>
    public void AddBed(string bedKey)
    {
        if (!string.IsNullOrEmpty(bedKey) && !_beds.Contains(bedKey))
        {
            _beds.Add(bedKey);
        }
    }

    /// <summary>注销一张床（拆除时调）：连带把躺在上面的人赶下来（他改打地铺，不是不休养）。</summary>
    public void RemoveBed(string bedKey)
    {
        _beds.Remove(bedKey);
        _occupied.Remove(bedKey);
    }

    /// <summary>营地里的床位总数。</summary>
    public int TotalBeds => _beds.Count;

    /// <summary>还空着的床位数。</summary>
    public int FreeBeds => _beds.Count - _occupied.Count;

    /// <summary>该幸存者是否占着一张床。</summary>
    public bool HasBed(int pawnId) => _occupied.ContainsValue(pawnId);

    /// <summary>该幸存者占的床（没占 → null）。</summary>
    public string? BedOf(int pawnId)
        => _occupied.FirstOrDefault(kv => kv.Value == pawnId).Key;

    /// <summary>这张床上躺着谁（空床/不存在 → null）。供悬停提示写"××躺在上面"。</summary>
    public int? OccupantOf(string bedKey)
        => bedKey != null && _occupied.TryGetValue(bedKey, out int id) ? id : null;

    /// <summary>
    /// 给该幸存者分一张床：他已占着 → 原样返回那张（幂等，不换床）；有空床 → 占最早登记的那张；没空床 → null。
    /// </summary>
    public string? TryClaim(int pawnId)
    {
        string? owned = BedOf(pawnId);
        if (owned != null)
        {
            return owned;
        }
        string? free = _beds.FirstOrDefault(b => !_occupied.ContainsKey(b));
        if (free == null)
        {
            return null;
        }
        _occupied[free] = pawnId;
        return free;
    }

    /// <summary>
    /// 指定占某张床（玩家右键点的就是这张）：床不存在或已被**别人**占 → false；已被自己占 → true（幂等）。
    /// 成功时**先退掉他原来那张**（一人一床，不许攥两张）。
    /// </summary>
    public bool TryClaimSpecific(string bedKey, int pawnId)
    {
        if (!_beds.Contains(bedKey))
        {
            return false;
        }
        if (_occupied.TryGetValue(bedKey, out int holder))
        {
            return holder == pawnId; // 自己占着=幂等成功；别人占着=失败
        }
        Release(pawnId); // 换床：先退旧的
        _occupied[bedKey] = pawnId;
        return true;
    }

    /// <summary>该幸存者起床（离开床位）。没占床是无操作。</summary>
    public void Release(int pawnId)
    {
        string? owned = BedOf(pawnId);
        if (owned != null)
        {
            _occupied.Remove(owned);
        }
    }

    /// <summary>全部床位键（供 UI/存档遍历，登记顺序）。</summary>
    public IReadOnlyList<string> Beds => _beds;

    /// <summary>读档：把占用关系整体灌回来（床本身由建图/存档的家具列表 <see cref="AddBed"/> 重建）。</summary>
    internal void RestoreOccupancy(IEnumerable<KeyValuePair<string, int>> occupied)
    {
        _occupied.Clear();
        foreach (var kv in occupied)
        {
            if (_beds.Contains(kv.Key))
            {
                _occupied[kv.Key] = kv.Value;
            }
        }
    }

    /// <summary>存档：当前占用关系快照。</summary>
    internal IReadOnlyDictionary<string, int> Occupancy => _occupied;
}

/// <summary>
/// 一个幸存者**当日**的休养流水账：每过一个相位记一笔 <see cref="RestQuality"/>，
/// 昼夜结算时出 <see cref="RestFraction"/>（休养占比）与 <see cref="BedFraction"/>（睡床占比）喂给 TickDay，
/// 然后 <see cref="Reset"/> 开下一天的账。
/// <para>
/// 一天没记过任何相位（<see cref="PhasesCounted"/>=0）→ 两个占比都是 0（不休养），不做除零。
/// </para>
/// </summary>
public sealed class RestLedger
{
    private int _phases;
    private int _restPhases;
    private int _bedPhases;

    /// <summary>本日已记账的相位数。</summary>
    public int PhasesCounted => _phases;

    /// <summary>本日处于休养（地铺或床）的相位数。</summary>
    public int RestPhases => _restPhases;

    /// <summary>本日睡在**床上**的相位数。</summary>
    public int BedPhases => _bedPhases;

    /// <summary>记一个相位的休养质量。</summary>
    public void Record(RestQuality quality)
    {
        _phases++;
        if (quality != RestQuality.None)
        {
            _restPhases++;
        }
        if (quality == RestQuality.Bed)
        {
            _bedPhases++;
        }
    }

    /// <summary>本日休养占比 0..1（休养相位 ÷ 已记相位）。喂 <c>TickDay(restFraction:)</c>。</summary>
    public double RestFraction => _phases == 0 ? 0.0 : (double)_restPhases / _phases;

    /// <summary>本日睡床占比 0..1（睡床相位 ÷ 已记相位）。喂 <c>TickDay(bedFraction:)</c>。</summary>
    public double BedFraction => _phases == 0 ? 0.0 : (double)_bedPhases / _phases;

    /// <summary>清账，开下一昼夜（每日黎明结算完调）。</summary>
    public void Reset()
    {
        _phases = 0;
        _restPhases = 0;
        _bedPhases = 0;
    }

    /// <summary>读档：把当日累计的流水直接灌回来（存档跨相位，账不能丢）。</summary>
    internal void Restore(int phases, int restPhases, int bedPhases)
    {
        _phases = System.Math.Max(0, phases);
        _restPhases = System.Math.Clamp(restPhases, 0, _phases);
        _bedPhases = System.Math.Clamp(bedPhases, 0, _restPhases);
    }
}

/// <summary>卧床养病的纯规则（无 Godot 依赖、Link 进单测）。全静态纯函数。</summary>
public static class BedrestLogic
{
    /// <summary>
    /// 某人在某相位的休养质量。**这是全套规则的心脏**，白天睡觉的加成就落在这儿：
    /// <list type="bullet">
    /// <item><see cref="PawnRole.Bedrest"/>（玩家下令卧床养病）→ 有床睡床、没床打地铺。**任何相位都算**（含夜里，代价是不站岗不生产）。</item>
    /// <item><see cref="PawnRole.Sleeping"/>（日程强制睡：白天留守者、夜里探险队）→ 同上。
    ///       ⇒ **白天在营地睡的那三个相位（出发/探索/返回）自此天然吃到治疗加成**，无需任何额外下令。</item>
    /// <item><see cref="PawnRole.Guard"/> 整夜值岗 → 不休养（沿用旧口径）。</item>
    /// <item><see cref="PawnRole.Expedition"/> 人在野外 → 不休养（旧写法把他算作"休养"，是明显的错，顺手修掉）。</item>
    /// <item><see cref="PawnRole.Reading"/> 夜里挑灯读书 → 不休养（在勤，旧写法同样误算为休养）。</item>
    /// <item><see cref="PawnRole.Idle"/> 待命 → 不休养。**想休养就得下令去躺着** —— 这正是要玩家做的那个选择。</item>
    /// </list>
    /// 聚餐相位（模态，全员爬起来吃饭）不计入流水账，见 <see cref="CountsTowardLedger"/>。
    /// </summary>
    public static RestQuality QualityFor(PawnRole role, bool hasBed) => role switch
    {
        PawnRole.Bedrest or PawnRole.Sleeping => hasBed ? RestQuality.Bed : RestQuality.Floor,
        _ => RestQuality.None,
    };

    /// <summary>
    /// 该相位是否计入休养流水账：**聚餐相位不计**（模态过渡，全员在饭桌上，既不算休养也不算干活——
    /// 计进去只会把两个占比一起稀释，凭空惩罚所有人）。其余 6 个相位皆计。
    /// </summary>
    public static bool CountsTowardLedger(DayPhase phase)
        => ShiftSchedule.BlockOf(phase) != PhaseBlock.Meal;

    /// <summary>
    /// 玩家能不能命令这个人「上床养病」。
    /// 拒绝理由**都写成人话给玩家看**（照 <see cref="SiteActionOption"/> 的规矩：不藏选项，灰掉并说明原因，
    /// 否则玩家会以为"这人不能养病"，而真相是"你还没造床"）。
    /// </summary>
    /// <param name="alive">还活着。</param>
    /// <param name="role">当前角色（出探险的人够不着）。</param>
    /// <param name="phase">当前相位（聚餐是模态，不下令）。</param>
    /// <param name="hasOwnBed">他已经占着一张床（幂等：再下一次令也允许，等于"回去躺着"）。</param>
    /// <param name="freeBeds">营地空床数。</param>
    public static BedrestOrder CanOrderBedrest(bool alive, PawnRole role, DayPhase phase, bool hasOwnBed, int freeBeds)
    {
        if (!alive)
        {
            return new BedrestOrder(BedrestOrderStatus.Dead, "他已经不在了。");
        }
        if (role == PawnRole.Expedition)
        {
            return new BedrestOrder(BedrestOrderStatus.NotInCamp, "他在外面——等他回来再说。");
        }
        if (ShiftSchedule.BlockOf(phase) == PhaseBlock.Meal)
        {
            return new BedrestOrder(BedrestOrderStatus.MealPhase, "都在饭桌上呢，吃完再躺。");
        }
        if (!hasOwnBed && freeBeds <= 0)
        {
            return new BedrestOrder(BedrestOrderStatus.NoFreeBed, "没有空床了——床是要造的，一人一张。");
        }
        return new BedrestOrder(BedrestOrderStatus.Ok, "去床上躺着。");
    }

    /// <summary>
    /// 卧床养病是否**顶掉夜班产出**（供 UI 在下令时把代价讲清楚）：夜里行动相位是唯一推进工时/站岗的相位，
    /// 躺在床上的人自然什么都不干。白天卧床不顶任何东西（白天本来就没有工时，见文件头）。
    /// </summary>
    public static bool CostsNightShift(DayPhase phase) => phase == DayPhase.NightAct;

    /// <summary>床这个可点击容器的 role 名（camp.json 的 <c>role="bed"</c>，也是 CampMain 容器表里的 Role）。</summary>
    public const string BedContainerRole = "bed";

    /// <summary>
    /// 玩家给一个**正躺着养病**的人下了别的令（走去某处 / 去搜柜子 / 去开门…）——他该不该起床？
    ///
    /// <para><b>该。</b>"去那边站着"和"继续躺着养病"是矛盾的两件事；不起床的话，他会**人走了却还占着床、还挂着养病令**
    /// （床位册漏一张床，休养流水账还在给他记分）。</para>
    ///
    /// <para><b>唯一的例外是床本身</b>：点床是养病流程自己的入口——点**别人的空床**=换张床躺，
    /// 点**自己那张**=起床（那个 toggle 在 <c>ExecuteBedInteract</c> 里判）。要是连这个也自动叫醒，
    /// "点自己的床=起床"就会变成"起床→走过去→又躺下"，toggle 直接失灵。</para>
    /// </summary>
    /// <param name="targetContainerRole">这条令的目标容器 role；<c>null</c>/空 = 地面移动令（走去空地）。</param>
    public static bool WakesOnCommand(string? targetContainerRole)
        => targetContainerRole != BedContainerRole;
}
