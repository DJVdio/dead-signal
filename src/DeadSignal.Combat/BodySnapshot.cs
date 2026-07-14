using System.Collections.Generic;

namespace DeadSignal.Combat;

/// <summary>
/// <see cref="Body"/> 的持久化快照（存档用）。<b>不含部位模板</b>（<see cref="BodyPart"/> 树是代码里的数据表，
/// 由 <c>CombatData.NewHumanoidBody()</c> 之类的工厂重建）——快照只带**那些无法从模板推回来的东西**：
/// 每部位当前/最大 HP、切除/损毁/失能/出血/骨折集合、储血量、生死、假肢。
/// <para>
/// 恢复流程恒为两步：<b>先按模板造一具全新的 Body，再 <see cref="Body.Restore"/> 覆盖状态</b>。
/// 这样部位树的演化（新增部位/改 MaxHp）不会把旧存档变成一具畸形的身体——新部位会以模板默认值出现，
/// 而不是缺失。（当然，跨版本读档本身是被 <c>SaveCodec</c> 拒绝的，见那边的版本闸门；此处只是不给自己挖坑。）
/// </para>
/// <para>
/// ⚠️ <b>切除必须无损往返</b>：山姆开局就缺两根手指，读档后要是长回来了，那是把 authored 叙事改写了。
/// </para>
/// </summary>
public sealed class BodySnapshot
{
    /// <summary>每部位当前 HP（部位名 → HP）。</summary>
    public Dictionary<string, double> Hp { get; set; } = new();

    /// <summary>每部位当前最大 HP（会被 <see cref="Body.ErodeMaxHp"/> 永久侵蚀，故必须存，不能从模板推）。</summary>
    public Dictionary<string, double> MaxHp { get; set; } = new();

    /// <summary>已切除的部位（锯掉的）。</summary>
    public List<string> Severed { get; set; } = new();

    /// <summary>已损毁的部位（砸烂的，MaxHp 磨损归零）。</summary>
    public List<string> Destroyed { get; set; } = new();

    /// <summary>已失能的部位（致残/致盲，但还长在身上）。</summary>
    public List<string> Disabled { get; set; } = new();

    /// <summary>
    /// 正在流血的部位。[T58] 三级流血之后**每部位只有一处出血** ⇒ 本表**不再有重复项**。
    /// <para>
    /// 🔴 **老存档（T58 之前）里同一部位会重复出现 N 次**（那时一个部位最多 3 处伤口）——
    /// <see cref="Body.Restore"/> 会把重复项**逐个按小流血合并**：1 次 ⇒ 小、2 次 ⇒ 中、3 次 ⇒ 大。
    /// 这**恰好**把旧的"1/2/3 处伤口"三档映射到新的"小/中/大"三级，且旧的封顶（3 处 × 速率 1.0 = 3.0）
    /// 与新的大流血速率（3.0）**完全相等** ⇒ 老档里最重的那档流血**一分不差**。故**无需存档版本闸门**。
    /// </para>
    /// </summary>
    public List<string> Bleeding { get; set; } = new();

    /// <summary>
    /// 与 <see cref="Bleeding"/> **按下标一一对应**的流血速率乘数（[T53] 伤口带属性）：
    /// 普通 1.0；锯齿剑刃造成的 1.4。（老存档没有此字段 ⇒ 空表 ⇒ 回落 1.0。）
    /// </summary>
    public List<double> BleedingRates { get; set; } = new();

    /// <summary>
    /// 与 <see cref="Bleeding"/> **按下标一一对应**的出血**等级**（[T58]：1=小 / 2=中 / 3=大）。
    /// <para>
    /// **老存档没有这个字段** ⇒ 反序列化成空表 ⇒ <see cref="Body.Restore"/> 取不到时**回落成"小流血"**，
    /// 再靠重复项合并还原出等级（见 <see cref="Bleeding"/> 的说明）。**无需版本闸门。**
    /// </para>
    /// </summary>
    public List<int> BleedingLevels { get; set; } = new();

    /// <summary>骨折的部位。</summary>
    public List<string> Fractured { get; set; } = new();

    /// <summary>已接受处理（上夹板）的骨折部位。</summary>
    public List<string> TreatedFractures { get; set; } = new();

    /// <summary>当前储血量。</summary>
    public double Blood { get; set; }

    /// <summary>储血量上限。</summary>
    public double BloodMax { get; set; }

    /// <summary>每处伤口每秒失血量（可被上层调参，故存）。</summary>
    public double BleedRatePerWound { get; set; }

    /// <summary>实体级失血抗性倍率（丧尸 1/3）。旧存档缺此字段 → 0 → <see cref="Body.Restore"/> 回落成 1.0。</summary>
    public double BleedRateMultiplier { get; set; } = 1.0;

    /// <summary>是否已失血致死。</summary>
    public bool BledOut { get; set; }

    /// <summary>是否已死亡。</summary>
    public bool IsDead { get; set; }

    /// <summary>已安装的假肢。</summary>
    public List<ProstheticSnapshot> Prosthetics { get; set; } = new();
}

/// <summary>
/// 一具假肢的快照。<see cref="Prosthetic.RestoreRatio"/> 由 <see cref="Prosthetic.Grade"/> 派生，
/// 但仍**逐字段存**——手术台上装的假肢可能被上层微调过比例，存派生结果比存"重算依据"诚实。
/// </summary>
public sealed class ProstheticSnapshot
{
    public string Name { get; set; } = "";
    public ProstheticGrade Grade { get; set; }
    public BodyRegion ReplacesRegion { get; set; }
    public double RestoreRatio { get; set; }
}
