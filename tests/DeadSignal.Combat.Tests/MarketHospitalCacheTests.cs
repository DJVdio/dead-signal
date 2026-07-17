using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 超市(中,11)/医院(大,30) 搜刮点解析纯逻辑单测（[SPEC-B13]）：配额落表、四处同步(CacheIdsFor/FlagForCache/Resolve/flag)、
// 医院医疗集中投放(打破"禁医疗灌水"例外点)、超市外围食物/内圈囤货语义。全部脱 Godot：只用 StoryFlags + ExplorationCache。
public class MarketHospitalCacheTests
{
    private static readonly string[] Medical =
        { "bandage", "needle_thread", "splint", "first_aid_kit", "antibiotics", "medicine" };

    [Fact]
    public void Supermarket_Has11Points_AllResolveOnce()
    {
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.SupermarketName);
        Assert.Equal(11, ids.Count);
        foreach (string id in ids)
        {
            var f = new StoryFlags();
            CacheResult? first = ExplorationCache.Resolve(id, f);
            Assert.NotNull(first);
            f.Set(first!.Value.StoryFlag, "true");
            Assert.Null(ExplorationCache.Resolve(id, f)); // 已搜过 → null
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Title));
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Narrative));
        }
    }

    [Fact]
    public void Supermarket_FlagForCache_RoundTripsAllIds()
    {
        foreach (string id in ExplorationCache.CacheIdsFor(ExplorationCache.SupermarketName))
        {
            string flag = ExplorationCache.FlagForCache(id);
            Assert.False(string.IsNullOrEmpty(flag));
            Assert.Equal(flag, ExplorationCache.Resolve(id, new StoryFlags())!.Value.StoryFlag);
        }
    }

    [Fact]
    public void Supermarket_OuterRing_HasFood_InnerHoard_HasSilverAndMeds()
    {
        // 外围货架残余：食物身份。
        CacheResult snacks = ExplorationCache.Resolve(ExplorationCache.SupermarketSnackAisleId, new StoryFlags())!.Value;
        Assert.Contains(snacks.Loot, l => l.Kind == LootKind.Food);

        // 内圈幸存者囤货（打赢才拿）：头目私囤含白银，药箱含急救包。
        CacheResult stash = ExplorationCache.Resolve(ExplorationCache.SupermarketHoardStashId, new StoryFlags())!.Value;
        Assert.Contains(stash.Loot, l => l.Kind == LootKind.Material && l.RefId == "silver");

        CacheResult meds = ExplorationCache.Resolve(ExplorationCache.SupermarketHoardMedsId, new StoryFlags())!.Value;
        Assert.Contains(meds.Loot, l => l.Kind == LootKind.Material && l.RefId == "first_aid_kit");
    }

    [Fact]
    public void Hospital_Has30Points_AllResolveOnce_WithSyncedFlags()
    {
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.HospitalName);
        Assert.Equal(44, ids.Count); // [大图放大] 原 30 → 44（补 14 点抬工作量到 ≈5 天量级）
        foreach (string id in ids)
        {
            var f = new StoryFlags();
            CacheResult? first = ExplorationCache.Resolve(id, f);
            Assert.NotNull(first);
            Assert.Equal(ExplorationCache.FlagForCache(id), first!.Value.StoryFlag);
            f.Set(first.Value.StoryFlag, "true");
            Assert.Null(ExplorationCache.Resolve(id, f));
        }
    }

    [Fact]
    public void Hospital_MedicalConcentratedInPharmacyAndOr_NotFrontDesk()
    {
        // 药房后间 = 深藏高价值抗生素。
        CacheResult back = ExplorationCache.Resolve(ExplorationCache.HospitalPharmacyBackId, new StoryFlags())!.Value;
        Assert.Contains(back.Loot, l => l.RefId == "antibiotics");

        // 主任保险柜（最深）= 最高价值医疗。
        CacheResult chief = ExplorationCache.Resolve(ExplorationCache.HospitalChiefSafeId, new StoryFlags())!.Value;
        Assert.Contains(chief.Loot, l => l.RefId == "antibiotics");
        Assert.Contains(chief.Loot, l => l.RefId == "first_aid_kit");

        // 门诊挂号台（近，非医疗区）= 无高价值医疗（只布料）。
        CacheResult reception = ExplorationCache.Resolve(ExplorationCache.HospitalReceptionId, new StoryFlags())!.Value;
        Assert.DoesNotContain(reception.Loot, l => l.RefId == "antibiotics" || l.RefId == "first_aid_kit");
    }

    [Fact]
    public void Hospital_IsTheMedicalException_FarMoreMedicalThanVillage()
    {
        // 医院医疗总量应显著高于南林村庄（大点对大点，"高风险高收益"的医疗例外身份）。
        int hospitalMeds = TotalMedical(ExplorationCache.HospitalName);
        int villageMeds = TotalMedical(VillageRescue.DestinationName);
        Assert.True(hospitalMeds > villageMeds * 3,
            $"医院医疗总量({hospitalMeds})应远超村庄({villageMeds})——医疗集中投放是医院身份。");
    }

    private static int TotalMedical(string destination)
    {
        int total = 0;
        foreach (string id in ExplorationCache.CacheIdsFor(destination))
        {
            CacheResult? r = ExplorationCache.Resolve(id, new StoryFlags());
            if (r == null) continue;
            total += r.Value.Loot
                .Where(l => l.Kind == LootKind.Material && Medical.Contains(l.RefId))
                .Sum(l => l.Quantity);
        }
        return total;
    }
}
