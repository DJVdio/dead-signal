using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 存档往返测试：**存进去 → 读回来 → 状态一致**。每个系统至少一条。
/// 存档的 bug 有个特别恶劣的性质——它不报错，只是让玩家的世界悄悄变了个样。
/// 所以这里的每条测试都在钉一件"读档后绝不能变"的事。
/// </summary>
public class SaveRoundTripTests
{
    // ---- 身体：切除 / 部位血量 / 假肢 ----

    [Fact]
    public void 切除的手指读档后不会长回来()
    {
        // 山姆开局就缺两根手指。这是 authored 叙事，不是可以被存档抹掉的东西。
        Body body = CombatData.NewHumanoidBody();
        body.Sever(HumanBody.LeftIndex);
        body.Sever(HumanBody.LeftMiddle);

        Body restored = RoundTripBody(body);

        Assert.True(restored.IsSevered(HumanBody.LeftIndex));
        Assert.True(restored.IsSevered(HumanBody.LeftMiddle));
        Assert.True(restored.IsGone(HumanBody.LeftIndex));
        // 没切的手指还在
        Assert.False(restored.IsSevered(HumanBody.LeftThumb));
        Assert.False(restored.IsSevered(HumanBody.RightIndex));
    }

    [Fact]
    public void 切除带来的操作惩罚读档后一致()
    {
        Body body = CombatData.NewHumanoidBody();
        body.Sever(HumanBody.LeftIndex);
        body.Sever(HumanBody.LeftMiddle);
        body.RecalculatePenalties();   // Sever 不自动重算，由调用方触发（既有约定）
        double before = body.DisabilityModifiers.OperationPenalty;
        Assert.True(before > 0, "缺两指该有操作惩罚，否则这条测试没在测东西");

        Body restored = RoundTripBody(body);

        // 惩罚是派生量——不存，靠 Restore 结尾的 RecalculatePenalties 从切除集合重算。
        // 这正是"派生量不该进存档"的示范：存了反而会和切除集合打架。
        Assert.Equal(before, restored.DisabilityModifiers.OperationPenalty, 6);
    }

    [Fact]
    public void 部位当前血量与被侵蚀的最大血量都读得回来()
    {
        Body body = CombatData.NewHumanoidBody();
        body.ApplyDamage(HumanBody.Chest, 7.5);
        body.ErodeMaxHp(HumanBody.LeftArm, 4.0);   // 永久侵蚀：不存就会凭空回满

        double chestHp = body.HpOf(HumanBody.Chest);
        double armMax = body.MaxHpOf(HumanBody.LeftArm);

        Body restored = RoundTripBody(body);

        Assert.Equal(chestHp, restored.HpOf(HumanBody.Chest), 6);
        Assert.Equal(armMax, restored.MaxHpOf(HumanBody.LeftArm), 6);
    }

    [Fact]
    public void 失血量与出血伤口读档后一致()
    {
        Body body = CombatData.NewHumanoidBody();
        body.LoseBlood(31.5);
        body.RegisterBleed(HumanBody.Abdomen, BleedModel.BleedSeverity.Medium);

        Body restored = RoundTripBody(body);

        Assert.Equal(body.Blood, restored.Blood, 6);
        Assert.Contains(HumanBody.Abdomen, restored.BleedingWounds);
        Assert.Equal(1, restored.BleedingWoundCount);
    }

    [Fact]
    public void 骨折与已上夹板的骨折分得清()
    {
        Body body = CombatData.NewHumanoidBody();
        body.MarkFractured(HumanBody.LeftLeg);
        body.MarkFractured(HumanBody.RightArm);
        body.MarkFractureTreated(HumanBody.LeftLeg);   // 只有左腿上了夹板

        Body restored = RoundTripBody(body);

        Assert.True(restored.IsFractured(HumanBody.LeftLeg));
        Assert.True(restored.IsFractured(HumanBody.RightArm));
        Assert.True(restored.IsFractureTreated(HumanBody.LeftLeg));
        Assert.False(restored.IsFractureTreated(HumanBody.RightArm));   // 右臂没上夹板，读回来也不该有
    }

    [Fact]
    public void 假肢读档后还在身上且恢复比例不变()
    {
        Body body = CombatData.NewHumanoidBody();
        body.Sever(HumanBody.LeftHand);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Bionic, BodyRegion.Hand));
        double penaltyBefore = body.DisabilityModifiers.OperationPenalty;

        Body restored = RoundTripBody(body);

        Assert.Single(restored.Prosthetics);
        Assert.Equal(ProstheticGrade.Bionic, restored.Prosthetics[0].Grade);
        Assert.Equal(0.75, restored.Prosthetics[0].RestoreRatio, 6);
        Assert.Equal(penaltyBefore, restored.DisabilityModifiers.OperationPenalty, 6);
    }

    [Fact]
    public void 死了的人读档后不会诈尸()
    {
        Body body = CombatData.NewHumanoidBody();
        body.ApplyDamage(HumanBody.Head, 9999);   // 爆头
        Assert.True(body.IsDead);

        Body restored = RoundTripBody(body);

        Assert.True(restored.IsDead);
    }

    // ---- 感染：双进度条竞速 ----

    [Fact]
    public void 感染的恶化进度与治疗进度两条赛道都读得回来()
    {
        // 双进度条竞速：恶化(Severity) 与 治疗(CureProgress) 各跑各的。
        // 漏掉任何一条，读档就等于偷偷改了赛况。
        var set = new HealthConditionSet();
        var inf = new HealthCondition(HealthConditionType.Infection, 0.42, HumanBody.LeftHand);
        inf.RestoreState(severity: 0.42, recoveryEfficiency: 30, cureProgress: 0.31,
                         tended: true, daysElapsed: 3, lastSurgeryDay: 1);
        set.Add(inf);

        HealthConditionSet restored = RoundTripHealth(set, isDead: false);

        HealthCondition r = Assert.Single(restored.Conditions);
        Assert.Equal(0.42, r.Severity, 6);       // 死亡赛道
        Assert.Equal(0.31, r.CureProgress, 6);   // 清除赛道 —— 就是这条最容易被漏掉
        Assert.Equal(30, r.RecoveryEfficiency);
        Assert.True(r.Tended);
    }

    [Fact]
    public void 上次手术是哪一天要按历史日还原而不是记成刚动过刀()
    {
        // 重做手术有冷却。若读档把"三天前动过刀"记成"此刻动过刀"，冷却就会凭空重置。
        var set = new HealthConditionSet();
        var c = new HealthCondition(HealthConditionType.Bleeding, 0.5, HumanBody.Chest);
        c.RestoreState(0.5, 40, 0.0, true, daysElapsed: 5, lastSurgeryDay: 2);
        set.Add(c);

        HealthConditionSet restored = RoundTripHealth(set, isDead: false);

        HealthCondition r = Assert.Single(restored.Conditions);
        Assert.Equal(2, r.LastSurgeryDay);
        Assert.Equal(5, r.DaysElapsed);
        Assert.Equal(3, r.DaysSinceLastSurgery);   // 5 - 2，不是 0
    }

    [Fact]
    public void 病死的终态读得回来()
    {
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Infection, 1.0, HumanBody.LeftHand));

        HealthConditionSet restored = RoundTripHealth(set, isDead: true);

        Assert.True(restored.IsDead);
    }

    // ---- 装备：成对装备的左右 ----

    [Fact]
    public void 一双手套读档后仍是一左一右而不是两只左手套()
    {
        // 手套/鞋的物品定义不分左右，同名可在身两件——只存物品名会丢"哪只在哪边"。
        var apparel = new ApparelSlots();
        apparel.TryEquip("劳保手套", new HashSet<EquipSlot> { EquipSlot.LeftHand }, out _);
        apparel.TryEquip("劳保手套", new HashSet<EquipSlot> { EquipSlot.RightHand }, out _);

        var restored = new ApparelSlots();
        SaveMapper.RestoreApparel(restored, SaveMapper.ToSave(apparel));

        Assert.Equal("劳保手套", restored.ItemAt(EquipSlot.LeftHand));
        Assert.Equal("劳保手套", restored.ItemAt(EquipSlot.RightHand));
        Assert.Equal(2, restored.Snapshot().Count);   // 两件独立实例，不是一件
    }

    [Fact]
    public void 多槽装备读档后仍占着它的每一个槽()
    {
        var apparel = new ApparelSlots();
        apparel.TryEquip("防毒面具", new HashSet<EquipSlot> { EquipSlot.Eyes, EquipSlot.Face }, out _);

        var restored = new ApparelSlots();
        SaveMapper.RestoreApparel(restored, SaveMapper.ToSave(apparel));

        Assert.Equal("防毒面具", restored.ItemAt(EquipSlot.Eyes));
        Assert.Equal("防毒面具", restored.ItemAt(EquipSlot.Face));
        Assert.Single(restored.Snapshot());   // 一件占两槽，不是两件
    }

    [Fact]
    public void 护甲覆盖的部位读档后一致()
    {
        var apparel = new ApparelSlots();
        apparel.TryEquip("防弹背心", new HashSet<EquipSlot> { EquipSlot.PlateLayer }, out _,
            coversParts: new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen });

        var restored = new ApparelSlots();
        SaveMapper.RestoreApparel(restored, SaveMapper.ToSave(apparel));

        Assert.Contains(HumanBody.Chest, restored.CoveredParts());
        Assert.Contains(HumanBody.Abdomen, restored.CoveredParts());
    }

    // ---- 持械 ----

    [Fact]
    public void 双手武器读档后仍是双手握而不是两只手各拿一把()
    {
        var l = new WeaponLoadout();
        l.EquipTwoHanded(WeaponTable.Greatsword());
        Assert.Equal(GripMode.TwoHanded, l.Grip);

        WeaponLoadout restored = SaveMapper.RestoreLoadout(SaveMapper.ToSave(l));

        Assert.Equal(GripMode.TwoHanded, restored.Grip);
        Assert.True(restored.TwoHandGrip);
        Assert.Equal(WeaponTable.Greatsword().Name, restored.PrimaryWeapon?.Name);
    }

    [Fact]
    public void 双持读档后左右手各自的武器不串位()
    {
        // 双持要求两把都 CanDualWield（游戏规则），故用两把匕首——这是真实可达的状态。
        var l = new WeaponLoadout();
        l.EquipToHand(WeaponTable.Dagger(), Hand.Left);
        l.EquipToHand(WeaponTable.Dagger(), Hand.Right);
        Assert.Equal(GripMode.DualWield, l.Grip);

        WeaponLoadout restored = SaveMapper.RestoreLoadout(SaveMapper.ToSave(l));

        Assert.Equal(WeaponTable.Dagger().Name, restored.LeftHand?.Name);
        Assert.Equal(WeaponTable.Dagger().Name, restored.RightHand?.Name);
        Assert.Equal(GripMode.DualWield, restored.Grip);
        Assert.False(restored.TwoHandGrip);   // 双持 ≠ 双手握
    }

    [Fact]
    public void 单手一把武器读档后不串到另一只手()
    {
        // 用匕首（真单手）——长剑是双手武器，EquipToHand 会自动转成双手握，测不出"另一只手是空的"。
        var l = new WeaponLoadout();
        l.EquipToHand(WeaponTable.Dagger(), Hand.Right);
        Assert.Equal(GripMode.OneHanded, l.Grip);

        WeaponLoadout restored = SaveMapper.RestoreLoadout(SaveMapper.ToSave(l));

        Assert.Equal(WeaponTable.Dagger().Name, restored.RightHand?.Name);
        Assert.Null(restored.LeftHand);
        Assert.Equal(GripMode.OneHanded, restored.Grip);
    }

    [Fact]
    public void 读档绝不静默丢掉手上的武器()
    {
        // 存档最恶劣的 bug：读档把武器悄悄弄没了——不报错、不提示，玩家只是发现自己空着手。
        // 早先的实现用 EquipToHand 重放，一旦撞上装备规则校验（双持约束等）就会正好干出这事。
        var save = new LoadoutSave
        {
            LeftHand = WeaponTable.Dagger().Name,
            RightHand = WeaponTable.Shortsword().Name,   // 短剑不可双持——重放会被拒
            TwoHandGrip = false,
        };

        WeaponLoadout restored = SaveMapper.RestoreLoadout(save);

        // 读档是"把状态摆回去"，不是"重新装备一遍"：两把都必须还在手上。
        Assert.Equal(WeaponTable.Dagger().Name, restored.LeftHand?.Name);
        Assert.Equal(WeaponTable.Shortsword().Name, restored.RightHand?.Name);
    }

    [Fact]
    public void 断手的人读档后那只手还是断的()
    {
        var l = new WeaponLoadout(leftHandLost: true, rightHandLost: false);
        l.EquipToHand(WeaponTable.Dagger(), Hand.Right);

        WeaponLoadout restored = SaveMapper.RestoreLoadout(SaveMapper.ToSave(l));

        Assert.True(restored.LeftHandLost);
        Assert.False(restored.RightHandLost);
        Assert.Null(restored.LeftHand);
        Assert.Equal(WeaponTable.Dagger().Name, restored.RightHand?.Name);
    }

    // ---- 库存 / 白银 ----

    [Fact]
    public void 背包里的东西连同白银一起读得回来()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon(WeaponTable.Dagger().Name));
        inv.Add(Item.Food(12));
        // 白银 = 一条 material item，按"分"存（2dp 分制）。1.80 银 = 180 分。
        inv.Add(Item.Material(Materials.CurrencyKey, "白银", Silver.FromWhole(40)));

        var restored = new InventoryStore();
        SaveMapper.RestoreInventory(restored, SaveMapper.ToSave(inv));

        Assert.Equal(3, restored.Count);
        Assert.Equal(12, restored.TotalFood);
        Assert.Equal(Silver.FromWhole(40), restored.MaterialCount(Materials.CurrencyKey));
        Assert.Contains(restored.Weapons, i => i.RefKey == WeaponTable.Dagger().Name);
    }

    [Fact]
    public void 白银的分位不会在往返中被抹掉()
    {
        // 2dp 分制：1 银 = 100 分。存成整数银会把 1.80 抹成 1 或 2。
        var inv = new InventoryStore();
        inv.Add(Item.Material(Materials.CurrencyKey, "白银", 180));   // 1.80 银

        var restored = new InventoryStore();
        SaveMapper.RestoreInventory(restored, SaveMapper.ToSave(inv));

        Assert.Equal(180, restored.MaterialCount(Materials.CurrencyKey));
        Assert.Equal("1.80", Silver.Format(restored.MaterialCount(Materials.CurrencyKey)));
    }

    // ---- 饥饿 ----

    [Fact]
    public void 饿到什么程度读得回来()
    {
        var h = new HungerState(value: 2, cap: HungerState.DefaultCap);

        HungerState restored = SaveMapper.RestoreHunger(h.Value, h.Cap);

        Assert.Equal(2, restored.Value);
        Assert.Equal(HungerState.DefaultCap, restored.Cap);
        Assert.Equal(h.AbilityPenalty, restored.AbilityPenalty, 6);
    }

    // ---- 专属效果（authored perk）----

    [Fact]
    public void 书虫的等级不是存下来的而是从累计阅读小时重新算出来的()
    {
        var p = new SurvivorPerks();
        p.GrantBookworm();
        p.Bookworm!.AddReadingTime(BookwormPerk.Level2ThresholdHours + 1);   // 够 2 级
        int levelBefore = p.Bookworm.Level;
        Assert.Equal(2, levelBefore);

        var restored = new SurvivorPerks();
        SaveMapper.RestorePerks(restored, SaveMapper.ToSave(p));

        Assert.Equal(2, restored.Bookworm!.Level);
        Assert.Equal(p.Bookworm.AccumulatedReadingHours, restored.Bookworm.AccumulatedReadingHours, 6);
        Assert.Equal(p.SelfReadingSpeedBonus, restored.SelfReadingSpeedBonus, 6);
    }

    [Fact]
    public void 山姆与南丁格尔的身份标记读得回来()
    {
        var p = new SurvivorPerks();
        p.GrantSam();
        p.GrantNightingale();

        var restored = new SurvivorPerks();
        SaveMapper.RestorePerks(restored, SaveMapper.ToSave(p));

        Assert.True(restored.IsSam);
        Assert.True(restored.IsNightingale);
    }

    // ---- 阅读 ----

    [Fact]
    public void 读了一半的书读档后接着读而不是从头来过()
    {
        var rp = new ReadingProgress();
        rp.Advance("book_medicine", 6.5);
        rp.Advance("book_archery", 2.0);

        var restored = new ReadingProgress();
        SaveMapper.RestoreReadingProgress(restored, SaveMapper.ToSave(rp));

        Assert.Equal(6.5, restored.HoursOn("book_medicine"), 6);
        Assert.Equal(2.0, restored.HoursOn("book_archery"), 6);
        Assert.Equal(0.0, restored.HoursOn("never_touched"), 6);
    }

    [Fact]
    public void 读完的书读档后仍是读完的()
    {
        var set = new ReadBookSet();
        set.MarkRead("book_medicine");

        var restored = new ReadBookSet();
        SaveMapper.RestoreReadBooks(restored, set.ReadBooks);

        Assert.True(restored.HasRead("book_medicine"));
        Assert.False(restored.HasRead("book_archery"));
    }

    // ---- 搜刮会话进度 ----

    [Fact]
    public void 搜了一半的柜子读档后剩下的东西还在里面()
    {
        var loot = new ContainerLoot();
        loot.Register("储物柜", new[] { LootItem.Food(3), LootItem.Material("wood", 5), LootItem.Book("book_x") });
        loot.TakeNext("储物柜");   // 拿走第一件就跑了

        Assert.Equal(2, loot.RemainingCount("储物柜"));
        Assert.True(loot.IsPartiallySearched("储物柜"));

        var camp = new CampSave();
        SaveMapper.CaptureContainerLoot(loot, camp);
        var restored = new ContainerLoot();
        SaveMapper.RestoreContainerLoot(restored, camp);

        Assert.Equal(2, restored.RemainingCount("储物柜"));
        Assert.True(restored.IsPartiallySearched("储物柜"));
        Assert.False(restored.IsSearched("储物柜"));
        // 剩下的**具体是哪两件**也要对
        Assert.Equal(LootKind.Material, restored.Remaining("储物柜")[0].Kind);
        Assert.Equal(LootKind.Book, restored.Remaining("储物柜")[1].Kind);
    }

    [Fact]
    public void 搜空了的柜子读档后不会重新长出东西()
    {
        var loot = new ContainerLoot();
        loot.Register("空柜", new[] { LootItem.Food(1) });
        loot.TakeNext("空柜");
        Assert.True(loot.IsSearched("空柜"));

        var camp = new CampSave();
        SaveMapper.CaptureContainerLoot(loot, camp);
        var restored = new ContainerLoot();
        SaveMapper.RestoreContainerLoot(restored, camp);

        Assert.True(restored.IsSearched("空柜"));
        Assert.Equal(0, restored.RemainingCount("空柜"));
    }

    // ---- 营地结构（按格）----

    [Fact]
    public void 被啃了一半的围栏格读档后血量不变()
    {
        var s = new CampStructureState(StructureTier.FenceBasic);
        s.TakeDamage(37.5);   // 小数伤害（砸墙伤害由武器派生，不取整）
        double hp = s.Hp;

        CampStructureState restored = CampStructureState.Restore(s.Tier, hp);

        Assert.Equal(hp, restored.Hp, 6);
        Assert.Equal(StructureTier.FenceBasic, restored.Tier);
        Assert.False(restored.IsDestroyed);
    }

    [Fact]
    public void 升过级的围栏格读档后还是升过级的()
    {
        var s = CampStructureState.Restore(StructureTier.FenceFullMetal, 700.0);

        Assert.Equal(StructureTier.FenceFullMetal, s.Tier);
        Assert.Equal(CampStructureTable.MaxHp(StructureTier.FenceFullMetal), s.MaxHp);
        Assert.Equal(700.0, s.Hp, 6);
    }

    // ---- 商人 ----

    [Fact]
    public void 商人的到访日读档后不会被重新掷骰()
    {
        // 若读档重滚，S/L 就成了刷商人日程的作弊器。
        var rng = new SequenceRandomSource(new[] { 0.9 });
        MerchantSchedule restored = MerchantSchedule.Restore(rng, nextVisitDay: 17);

        Assert.Equal(17, restored.NextVisitDay);
        Assert.False(restored.ShouldVisit(currentDay: 16, dayBlocked: false));
        Assert.True(restored.ShouldVisit(currentDay: 17, dayBlocked: false));
    }

    // ---- 制作 ----

    [Fact]
    public void 做了一半的东西读档后工时不清零()
    {
        var job = new CraftingJob("recipe_torch", totalWorkMinutes: 60, times: 2);
        job.Advance(25, canWork: true);

        CraftingJob restored = SaveMapper.FromSave(SaveMapper.ToSave(job, workerId: 3))!;

        Assert.Equal(25, restored.ElapsedWorkMinutes);
        Assert.Equal(35, restored.RemainingWorkMinutes);
        Assert.Equal(2, restored.Times);
        Assert.False(restored.IsComplete);
    }

    [Fact]
    public void 工作台装的工具读档后还在()
    {
        var wb = new WorkbenchState();
        wb.InstallTool(ToolSlot.SawBlade);
        wb.InstallTool(ToolSlot.Beaker);

        var restored = new WorkbenchState();
        SaveMapper.RestoreWorkbench(restored, SaveMapper.ToSave(wb));

        Assert.True(restored.HasTool(ToolSlot.SawBlade));
        Assert.True(restored.HasTool(ToolSlot.Beaker));
        Assert.False(restored.HasTool(ToolSlot.Calipers));
    }

    // ---- 狗 ----

    [Fact]
    public void 布鲁斯身上的狗衣读档后还穿着()
    {
        var apparel = new DogApparelSlots();
        apparel.TryEquip(DogGearCatalog.PocketVestKey, out _);
        apparel.TryEquip(DogGearCatalog.IronHelmetKey, out _);

        var restored = new DogApparelSlots();
        SaveMapper.RestoreDogApparel(restored, SaveMapper.ToSave(apparel));

        Assert.Equal(DogGearCatalog.PocketVestKey, restored.ItemAt(DogEquipSlot.Body));
        Assert.Equal(DogGearCatalog.IronHelmetKey, restored.ItemAt(DogEquipSlot.Head));
    }

    [Fact]
    public void 狗的饥饿读得回来()
    {
        var h = new DogHungerState(2);
        var restored = new DogHungerState(h.Value);
        Assert.Equal(2, restored.Value);
        Assert.Equal(h.AbilityPenalty, restored.AbilityPenalty, 6);
    }

    // ---- 剧情 flag（半个存档都靠它）----

    [Fact]
    public void 剧情旗标全量往返()
    {
        var flags = new StoryFlags();
        flags.Set("christine_req", "pending");
        flags.Set("radio_mainline", "3");
        flags.Set("horde_sighted", "true");
        flags.Set("searched_hospital_cache_7", "true");

        var restored = new StoryFlags(flags.Snapshot());

        Assert.Equal(4, restored.Count);
        Assert.Equal("pending", restored.Get("christine_req"));
        Assert.Equal("3", restored.Get("radio_mainline"));
        Assert.True(restored.Has("searched_hospital_cache_7"));
    }

    // ---- 辅助 ----

    private static Body RoundTripBody(Body original)
    {
        // 存档不存部位模板（那是代码里的数据表）——恢复恒为：按模板造新身体，再把状态盖回去。
        BodySnapshot snap = original.Capture();
        string json = System.Text.Json.JsonSerializer.Serialize(snap);
        BodySnapshot back = System.Text.Json.JsonSerializer.Deserialize<BodySnapshot>(json)!;

        Body fresh = CombatData.NewHumanoidBody();
        fresh.Restore(back);
        return fresh;
    }

    private static HealthConditionSet RoundTripHealth(HealthConditionSet original, bool isDead)
    {
        List<ConditionSave> saved = SaveMapper.ToSave(original);
        var restored = new HealthConditionSet();
        SaveMapper.RestoreHealth(restored, saved, isDead);
        return restored;
    }
}
