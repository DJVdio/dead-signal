using System.Linq;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

// Pawn 的**存档面**（partial，独立文件——不动 Pawn.cs 主体）。
//
// 为什么住在 Pawn 里而不是 SaveMapper：幸存者身上那些纯逻辑子结构（穿戴/持械/身体/伤病…）
// 全是 **private 字段**，外人够不着。partial 让存档代码既能碰到它们，又不用把封装拆开给所有人看。
// 每个子结构的实际映射仍然委托给 SaveMapper（那边是纯逻辑、能单测），这里只负责"取出来 / 装回去"。

public sealed partial class Pawn
{
    /// <summary>
    /// 导出这个幸存者的全部状态。
    /// <b>位置存 cartesian</b>（<see cref="Node2D.Position"/>）——不是 iso 屏幕坐标，那是画出来的，不是世界的真相。
    /// </summary>
    public PawnSave CaptureSave() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Role = Role,
        Stationing = Stationing,
        X = Position.X,
        Y = Position.Y,

        // 身体：部位血量 / 切除 / 骨折 / 失血 / 假肢。山姆缺的那两根手指就在这里。
        Body = Body.Capture(),

        Hunger = Hunger.Value,
        HungerCap = Hunger.Cap,

        Conditions = SaveMapper.ToSave(Health),
        HealthDead = Health.IsDead,
        ImmunityProgress = Health.ImmunityProgress,                       // [感染重做] set 级全局免疫条
        ImmuneWindowRemainingDays = Health.ImmuneWindowRemainingDays,     // [感染重做] set 级免疫窗剩余

        Apparel = SaveMapper.ToSave(_apparel),
        Loadout = SaveMapper.ToSave(_loadout),
        HeldLight = SaveMapper.ToSave(_heldLight),

        Perks = SaveMapper.ToSave(Perks),
        ReadBooks = _readBooks.ReadBooks.ToList(),
        ReadingProgress = SaveMapper.ToSave(_readingProgress),
        AssignedBookId = AssignedBookId,

        InfectionTreatmentMedKey = InfectionTreatmentMedKey,

        // [批次21·impl-bedrest] 卧床令 + 当日休养流水账（漏了它，读档回来伤员自己爬起来、当天的觉白睡）。
        BedrestOrdered = BedrestOrdered,
        RestPhases = Rest.PhasesCounted,
        RestRestPhases = Rest.RestPhases,
        RestBedPhases = Rest.BedPhases,
    };

    /// <summary>
    /// 把存档状态盖回这个幸存者身上。调用方须先按工厂造出一个**全新**的 Pawn（部位模板从代码里的表来），
    /// 本方法只覆盖可变状态。
    /// </summary>
    public void ApplySave(PawnSave s)
    {
        DisplayName = s.DisplayName;
        Role = s.Role;
        Stationing = s.Stationing;
        Position = new Vector2((float)s.X, (float)s.Y);

        Body.Restore(s.Body);

        // 用 Restore 而非 DrainTo——后者**只饿不喂**，会静默丢掉"吃撑"这类高于默认值的刻度。
        Hunger.Restore(s.Hunger);

        SaveMapper.RestoreHealth(Health, s.Conditions, s.HealthDead, s.ImmunityProgress, s.ImmuneWindowRemainingDays);

        SaveMapper.RestoreApparel(_apparel, s.Apparel);
        RestoreLoadoutFrom(s.Loadout);
        SaveMapper.RestoreHeldLight(_heldLight, s.HeldLight, _loadout);

        SaveMapper.RestorePerks(Perks, s.Perks);
        SaveMapper.RestoreReadBooks(_readBooks, s.ReadBooks);
        SaveMapper.RestoreReadingProgress(_readingProgress, s.ReadingProgress);
        AssignedBookId = s.AssignedBookId;

        InfectionTreatmentMedKey = s.InfectionTreatmentMedKey;

        // [批次21·impl-bedrest] 卧床令 + 当日休养流水账。床位占用是营地级的（CampSave.BedOccupancy），不在这儿。
        BedrestOrdered = s.BedrestOrdered;
        Rest.Restore(s.RestPhases, s.RestRestPhases, s.RestBedPhases);

        // 护甲**数值层**是派生缓存（穿戴态 + 护甲表 → 层），不进存档——存了反而会和护甲表打架。
        // 穿戴态摆回来之后，照着它把缓存重建一次，再投影出生效战斗数据。
        RebuildApparelLayers();
        SyncCombatFromEquipment();
    }

    /// <summary>
    /// 按当前穿戴态重建"装备名 → 护甲层"缓存。护甲的**防御数值**来自 <c>ArmorLayerCatalog</c>（代码里的表），
    /// 存档只存"穿了什么、穿在哪"——数值改了，旧存档里的甲跟着改，这是对的。
    /// </summary>
    private void RebuildApparelLayers()
    {
        _apparelLayers.Clear();
        foreach (ApparelSlots.WornSnapshot w in _apparel.Snapshot())
        {
            if (ArmorLayerCatalog.TryGetValue(w.Item, out ArmorLayer? layer) && layer is not null)
            {
                _apparelLayers[w.Item] = layer;   // 纯覆盖品（无防御数值）不登记，与 EquipApparel 同口径
            }
        }
    }

    /// <summary>
    /// 把持械态摆回去。<see cref="_loadout"/> 是 readonly 字段（不能整个换掉），
    /// 故逐字段灌回而不是 new 一个。
    /// </summary>
    private void RestoreLoadoutFrom(LoadoutSave s)
    {
        if (s.LeftHandLost)
        {
            _loadout.NotifyHandLost(Hand.Left);
        }
        if (s.RightHandLost)
        {
            _loadout.NotifyHandLost(Hand.Right);
        }

        Weapon? left = SaveMapper.WeaponByName(s.LeftHand);
        Weapon? right = SaveMapper.WeaponByName(s.RightHand);
        if (s.TwoHandGrip)
        {
            Weapon? two = right ?? left;
            _loadout.Restore(two, two, twoHandGrip: two is not null);
        }
        else
        {
            _loadout.Restore(left, right, twoHandGrip: false);
        }
    }
}
