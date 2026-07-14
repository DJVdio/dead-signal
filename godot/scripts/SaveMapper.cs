using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「纯逻辑状态类 ⇄ 存档 DTO」的双向映射。**存档的正确性主要就在这个文件里**——
// 一个字段忘了抄，玩家读档后就少一样东西，而且不会有任何报错。故这里的每一对 Capture/Restore
// 都在 SaveRoundTripTests 里有一条"存进去 → 读回来 → 状态一致"的往返测试钉着。
//
// Pawn/Corpse/CampMain 这些**Godot 节点**的组装不在这儿（那要引 Godot），在 CampMain 的存读档方法里；
// 但它们身上挂的每一个纯逻辑子结构（身体/穿戴/持械/伤病/饥饿/专属效果/阅读进度…）都由本文件负责。

/// <summary>纯逻辑状态 ⇄ 存档 DTO 的映射。全部是静态纯函数，无副作用之外的状态。</summary>
public static class SaveMapper
{
    // ---- 武器按名回查 ----
    // 走 ModdedWeaponRegistry：**先原厂表（WeaponTable）、后改装表**。
    // 此前这里只索引 Arsenal()+ArcheryArsenal() ⇒ 改装武器（"步枪（刺刀型）"不在原厂表里）回查落空 ⇒
    // **存档一读那把枪就没了**。改装变体的身份（基础武器名 + 改装名）另由 ModdedWeaponSpecSave 入档，
    // 读档时先 Restore 注册表、再复原 Pawn 持械，故此处一定查得到。

    /// <summary>按武器名回查武器定义（含改装变体）。查不到返回 null（武器被从表里删了——版本闸门本该拦住这种存档）。</summary>
    public static Weapon? WeaponByName(string? name)
        => ModdedWeaponRegistry.WeaponByName(name);

    // ---- 改装武器注册表（存档只落三个字符串，数值读档时按当前规则重算，见 ModdedWeaponRegistry 类注）----

    /// <summary>把全部已登记的改装变体身份抄进存档。</summary>
    public static List<ModdedWeaponSave> CaptureModdedWeapons()
        => ModdedWeaponRegistry.Specs
            .Select(s => new ModdedWeaponSave
            {
                VariantName = s.VariantName,
                BaseWeaponName = s.BaseWeaponName,
                ModNames = s.ModNames.ToList(),
            })
            .ToList();

    /// <summary>读档：按存档里的身份全量重建改装注册表（**必须在复原任何持械/库存之前调**）。</summary>
    public static void RestoreModdedWeapons(IReadOnlyList<ModdedWeaponSave>? saved)
        => ModdedWeaponRegistry.Restore(
            (saved ?? new List<ModdedWeaponSave>())
                .Select(s => new ModdedWeaponSpec(
                    s.VariantName ?? "",
                    s.BaseWeaponName ?? "",
                    s.ModNames ?? new List<string>())));

    // ---- Item ----

    public static ItemSave ToSave(Item item) => new()
    {
        Category = item.Category,
        DisplayName = item.DisplayName,
        Description = item.Description,
        RefKey = item.RefKey,
        FoodQuantity = item.FoodQuantity,
        MaterialQuantity = item.MaterialQuantity,
    };

    public static Item FromSave(ItemSave s)
        => Item.Restore(s.Category, s.DisplayName, s.Description, s.RefKey, s.FoodQuantity, s.MaterialQuantity);

    // ---- InventoryStore ----

    public static List<ItemSave> ToSave(InventoryStore inv) => inv.Items.Select(ToSave).ToList();

    /// <summary>读档：清空并灌回全部物品。<b>白银也在这里头</b>（它是一条 material item，不是独立字段）。</summary>
    public static void RestoreInventory(InventoryStore inv, IEnumerable<ItemSave> items)
    {
        foreach (Item existing in inv.Items.ToList())
        {
            inv.Remove(existing);
        }
        inv.AddRange(items.Select(FromSave));
    }

    // ---- HungerState ----

    public static HungerState RestoreHunger(int value, int cap) => new(value, cap);

    // ---- ApparelSlots ----

    public static List<WornSave> ToSave(ApparelSlots apparel)
        => apparel.Snapshot().Select(w => new WornSave
        {
            Item = w.Item,
            Slots = w.Slots.ToList(),
            Covers = w.Covers.ToList(),
        }).ToList();

    public static void RestoreApparel(ApparelSlots apparel, IEnumerable<WornSave> worn)
        => apparel.Restore(worn.Select(w => new ApparelSlots.WornSnapshot(
            w.Item,
            w.Slots,
            w.Covers)));

    // ---- WeaponLoadout ----

    public static LoadoutSave ToSave(WeaponLoadout l) => new()
    {
        LeftHand = l.LeftHand?.Name,
        RightHand = l.RightHand?.Name,
        TwoHandGrip = l.TwoHandGrip,
        LeftHandLost = l.LeftHandLost,
        RightHandLost = l.RightHandLost,
    };

    /// <summary>
    /// 读档：重建持械。断手状态走构造器（它决定了哪只手还能拿东西），再把武器放回去。
    /// <para>
    /// 双手武器要走 <see cref="WeaponLoadout.EquipTwoHanded"/> 而不是往两只手各塞一把——
    /// 那是同一把武器占了两只手，不是两把。
    /// </para>
    /// </summary>
    public static WeaponLoadout RestoreLoadout(LoadoutSave s)
    {
        var l = new WeaponLoadout(s.LeftHandLost, s.RightHandLost);

        if (s.TwoHandGrip)
        {
            // 双手握 = 同一把武器占两只手（不是两把）。
            Weapon? two = WeaponByName(s.RightHand ?? s.LeftHand);
            l.Restore(two, two, twoHandGrip: two is not null);
            return l;
        }

        l.Restore(WeaponByName(s.LeftHand), WeaponByName(s.RightHand), twoHandGrip: false);
        return l;
    }

    // ---- HeldLightState ----

    public static HeldLightSave? ToSave(HeldLightState light)
        => light.Held is LightProfile p && light.HandUsed is Hand h
            ? new HeldLightSave { LightKey = p.Key, Hand = h }
            : null;

    public static void RestoreHeldLight(HeldLightState light, HeldLightSave? s, WeaponLoadout loadout)
    {
        light.Drop();
        if (s is null)
        {
            return;
        }
        if (LightSource.Find(s.LightKey) is LightProfile profile)
        {
            light.TryHold(profile, s.Hand, loadout);
        }
    }

    // ---- SurvivorPerks ----

    public static PerkSave ToSave(SurvivorPerks p) => new()
    {
        HasBookworm = p.Bookworm is not null,
        BookwormReadingHours = p.Bookworm?.AccumulatedReadingHours ?? 0.0,
        IsNightingale = p.IsNightingale,
        IsSam = p.IsSam,
    };

    /// <summary>
    /// 读档：把专属效果摆回去。<b>等级不存、只重放累积量</b>——
    /// 书虫等级由累计小时推（<see cref="BookwormPerk.AddReadingTime"/> 内部自升级），
    /// 山姆/南丁格尔的等级更是按营地人数、主刀台数**动态算**的（山姆的还会倒退）。
    /// 存等级只会和累积量打架，两份真相必然对不上。
    /// </summary>
    public static void RestorePerks(SurvivorPerks p, PerkSave s)
    {
        if (s.HasBookworm)
        {
            p.GrantBookworm();
            p.Bookworm!.AddReadingTime(s.BookwormReadingHours);
        }
        if (s.IsNightingale)
        {
            p.GrantNightingale();
        }
        if (s.IsSam)
        {
            p.GrantSam();
        }
    }

    // ---- ReadBookSet / ReadingProgress ----

    public static void RestoreReadBooks(ReadBookSet set, IEnumerable<string> bookIds)
    {
        foreach (string id in bookIds)
        {
            set.MarkRead(id);
        }
    }

    public static Dictionary<string, double> ToSave(ReadingProgress rp)
        => rp.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);

    public static void RestoreReadingProgress(ReadingProgress rp, IEnumerable<KeyValuePair<string, double>> entries)
        => rp.Restore(entries);

    // ---- HealthConditions ----

    public static ConditionSave ToSave(HealthCondition c) => new()
    {
        Type = c.Type,
        BodyPart = c.BodyPart,
        OnLimb = c.OnLimb,
        LethalBleed = c.LethalBleed,
        InfectionProneness = c.InfectionProneness,
        SelfHealing = c.SelfHealing,
        Severity = c.Severity,
        CureProgress = c.CureProgress,
        RecoveryEfficiency = c.RecoveryEfficiency,
        Tended = c.Tended,
        DaysElapsed = c.DaysElapsed,
        LastSurgeryDay = c.LastSurgeryDay,
    };

    public static List<ConditionSave> ToSave(HealthConditionSet set)
        => set.Conditions.Select(ToSave).ToList();

    /// <summary>
    /// 读档：重建一条伤病。不变量走构造器，进度走 <see cref="HealthCondition.RestoreState"/>。
    /// <b>治疗进度（CureProgress）和感染进度（Severity）两条赛道都要摆回去</b>——
    /// 这是双进度条竞速，漏掉任何一条，读档就等于偷偷改了赛况。
    /// </summary>
    public static HealthCondition FromSave(ConditionSave s)
    {
        var c = new HealthCondition(s.Type, s.Severity, s.BodyPart, s.OnLimb, s.LethalBleed, s.InfectionProneness, s.SelfHealing);
        c.RestoreState(s.Severity, s.RecoveryEfficiency, s.CureProgress, s.Tended, s.DaysElapsed, s.LastSurgeryDay);
        return c;
    }

    public static void RestoreHealth(HealthConditionSet set, IEnumerable<ConditionSave> conditions, bool isDead)
        => set.Restore(conditions.Select(FromSave), isDead);

    // ---- ContainerLoot ----

    public static void RestoreContainerLoot(ContainerLoot loot, CampSave camp)
        => loot.Restore(
            camp.ContainerLoot.Select(kv =>
                new KeyValuePair<string, IReadOnlyList<LootItem>>(kv.Key, kv.Value)),
            camp.ContainersSearched,
            camp.ContainersPartial);

    public static void CaptureContainerLoot(ContainerLoot loot, CampSave camp)
    {
        camp.ContainerLoot = loot.SnapshotTables().ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        camp.ContainersSearched = loot.SnapshotSearched().ToList();
        camp.ContainersPartial = loot.SnapshotPartial().ToList();
    }

    // ---- WorkbenchState ----

    public static List<ToolSlot> ToSave(WorkbenchState wb) => wb.InstalledTools.ToList();

    public static void RestoreWorkbench(WorkbenchState wb, IEnumerable<ToolSlot> tools)
    {
        foreach (ToolSlot t in wb.InstalledTools.ToList())
        {
            wb.RemoveTool(t);
        }
        foreach (ToolSlot t in tools)
        {
            wb.InstallTool(t);
        }
    }

    // ---- CookStationState（批次21·T14 烹饪台的两个炊具槽）----

    public static List<CookwareSlot> ToSave(CookStationState station) => station.Installed.ToList();

    /// <summary>读档：先卸干净再照存档装回（幂等，避免"读两次档装出两口锅"——虽然 HashSet 本来也不让）。</summary>
    public static void RestoreCookStation(CookStationState station, IEnumerable<CookwareSlot>? installed)
    {
        foreach (CookwareSlot s in station.Installed.ToList())
        {
            station.Remove(s);
        }
        foreach (CookwareSlot s in installed ?? Enumerable.Empty<CookwareSlot>())
        {
            station.Install(s);
        }
    }

    // ---- CraftingJob ----

    public static CraftingJobSave? ToSave(CraftingJob? job, int workerId)
        => job is null ? null : new CraftingJobSave
        {
            RecipeId = job.RecipeId,
            Times = job.Times,
            TotalWorkMinutes = job.TotalWorkMinutes,
            ElapsedWorkMinutes = job.ElapsedWorkMinutes,
            WorkerId = workerId,
        };

    /// <summary>读档：重建在制品，并把已投入的工时重放回去（<see cref="CraftingJob.Advance"/> 是唯一的推进通道）。</summary>
    public static CraftingJob? FromSave(CraftingJobSave? s)
    {
        if (s is null)
        {
            return null;
        }
        var job = new CraftingJob(s.RecipeId, s.TotalWorkMinutes, s.Times);
        job.Advance(s.ElapsedWorkMinutes, canWork: true);
        return job;
    }

    // ---- DogApparelSlots ----

    public static Dictionary<DogEquipSlot, string> ToSave(DogApparelSlots apparel)
    {
        var d = new Dictionary<DogEquipSlot, string>();
        foreach (DogEquipSlot slot in new[] { DogEquipSlot.Body, DogEquipSlot.Head })
        {
            string? key = apparel.ItemAt(slot);
            if (key is not null)
            {
                d[slot] = key;
            }
        }
        return d;
    }

    public static void RestoreDogApparel(DogApparelSlots apparel, IReadOnlyDictionary<DogEquipSlot, string> saved)
    {
        foreach (string key in apparel.EquippedKeys.ToList())
        {
            apparel.Unequip(key);
        }
        foreach (var kv in saved)
        {
            apparel.TryEquip(kv.Value, out _);
        }
    }
}
