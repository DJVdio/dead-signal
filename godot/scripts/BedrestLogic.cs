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
// ★ 解法：按游戏分钟累计休息与真实睡床时长。伤口/骨折只吃睡床轴；旧休养 ×1.5 已删除。
//   休息分钟仅保留给自然补血基数，睡床分钟另提供最高 +10% 恢复加成。
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
/// 一个幸存者当日的分钟流水账。昼夜结算时输出休息占比与睡床占比，然后清账。
/// </summary>
public sealed class RestLedger
{
    private int _minutes;
    private int _restMinutes;
    private int _bedMinutes;

    /// <summary>本日已流逝的游戏分钟。</summary>
    public int MinutesCounted => _minutes;

    /// <summary>其中确实睡在床上的游戏分钟。</summary>
    public int BedMinutes => _bedMinutes;

    /// <summary>其中主动卧床或日程睡眠的游戏分钟。仅供自然补血，不再乘伤口/骨折愈合速度。</summary>
    public int RestMinutes => _restMinutes;

    /// <summary>按游戏分钟记账。主动卧床但没床，也只增加分母，不产生恢复加成。</summary>
    public void RecordMinutes(int minutes, bool onBed, bool resting = false)
    {
        int safe = System.Math.Max(0, minutes);
        _minutes += safe;
        if (resting || onBed)
        {
            _restMinutes += safe;
        }
        if (onBed)
        {
            _bedMinutes += safe;
        }
    }

    /// <summary>本日睡床占比 0..1（睡床分钟 ÷ 已流逝游戏分钟）。</summary>
    public double BedFraction => _minutes == 0 ? 0.0 : (double)_bedMinutes / _minutes;

    /// <summary>自然补血用休息占比；不参与伤口/骨折 ×1.5（该倍率已删除）。</summary>
    public double RestFraction => _minutes == 0 ? 0.0 : (double)_restMinutes / _minutes;

    /// <summary>清账，开下一昼夜（每日黎明结算完调）。</summary>
    public void Reset()
    {
        _minutes = 0;
        _restMinutes = 0;
        _bedMinutes = 0;
    }

    /// <summary>读档：把当日累计的流水直接灌回来（存档跨相位，账不能丢）。</summary>
    internal void Restore(int minutes, int restMinutes, int bedMinutes)
    {
        _minutes = System.Math.Max(0, minutes);
        _restMinutes = System.Math.Clamp(restMinutes, 0, _minutes);
        _bedMinutes = System.Math.Clamp(bedMinutes, 0, _minutes);
    }
}

/// <summary>卧床养病的纯规则（无 Godot 依赖、Link 进单测）。全静态纯函数。</summary>
public static class BedrestLogic
{
    /// <summary>把昼夜相位映射为单调世界游戏分钟；白昼/夜晚各 720 分钟，冻结过渡相位不推进。</summary>
    public static int WorldMinuteStamp(int day, DayPhase phase, double phaseElapsed, double dayLengthSeconds, double nightLengthSeconds)
    {
        int dayBase = System.Math.Max(0, day) * 1440;
        static int SegmentMinutes(double elapsed, double length)
            => length <= 0 ? 0 : System.Math.Clamp((int)System.Math.Floor(elapsed / length * 720.0), 0, 720);

        return phase switch
        {
            DayPhase.DayPrep or DayPhase.DayTravel => dayBase,
            DayPhase.DayExplore => dayBase + SegmentMinutes(phaseElapsed, dayLengthSeconds),
            DayPhase.DayReturn or DayPhase.DuskMeal or DayPhase.NightPrep => dayBase + 720,
            DayPhase.NightAct => dayBase + 720 + SegmentMinutes(phaseElapsed, nightLengthSeconds),
            DayPhase.DawnMeal => dayBase + 1440,
            _ => dayBase,
        };
    }

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
