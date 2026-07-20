using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次21·impl-bedrest：卧床养病（上床/起床/床位占用）+ **白天睡觉也算治疗加成**。
/// 旧缺陷（本文件钉死）：休养是"整日一个布尔"且在黎明读到的是**昨夜**的角色 ⇒ 白天在营地睡了三个相位
/// 对治疗零贡献。修法=把布尔推广成按相位累计的占比（RestLedger → TickDay 的 restFraction/bedFraction）。
/// </summary>
public class BedrestTests
{
    // ---------------- 零回归护栏：布尔是占比的特例，端点必须逐比特等价 ----------------

    /// <summary>
    /// 一具带"已手术出血"的伤病集（愈合量随 rest/bed 两轴变化，便于比对）。
    /// 手术点数池 = 基础 15 + 床 10 + 急救包 60 = 85 ⇒ roll 区间 [0,85]（见 <see cref="HealthConditionSet.RollRange"/>）；
    /// 喂 80 ⇒ 稳定成功、恢复效率 80%。（SequenceRandomSource 喂的是**区间内的实际值**，不是 0..1 比例。）
    /// </summary>
    private static HealthConditionSet OperatedBleed(out HealthCondition wound)
    {
        var set = new HealthConditionSet();
        wound = new HealthCondition(HealthConditionType.Bleeding, 0.8, "躯干", onLimb: false);
        set.Add(wound);
        SurgeryResult r = set.PerformSurgery(wound, new[] { "first_aid_kit" }, onBed: true,
            new SequenceRandomSource(new[] { 80.0 }));
        Assert.True(r.Success);                 // 前提没成立就别往下比了
        Assert.Equal(80, wound.RecoveryEfficiency);
        return set;
    }

    /// <summary>本昼夜的愈合量（severity 降了多少）。感染 roll 喂 0.99 ⇒ 绝不感染，隔离出纯愈合量。</summary>
    private static double HealAfterTick(bool? resting, bool? inBed, double? restFraction, double? bedFraction)
    {
        HealthConditionSet set = OperatedBleed(out HealthCondition w);
        double before = w.Severity;
        set.TickDay(new SequenceRandomSource(new[] { 0.99 }), // 感染 roll 走 Range(0,1)：0.99 → 不中招
            resting ?? false, inBed ?? false, restFraction: restFraction, bedFraction: bedFraction);
        return before - w.Severity; // 本昼夜愈合量
    }

    [Fact]
    public void 主动卧床没床_不再获得通用休养倍率()
        => Assert.Equal(HealAfterTick(false, false, 0, 0), HealAfterTick(true, false, 1, 0), 12);

    [Fact]
    public void 睡床分钟按实际时长加权()
    {
        var ledger = new RestLedger();
        ledger.RecordMinutes(60, onBed: true);
        ledger.RecordMinutes(180, onBed: false);
        Assert.Equal(240, ledger.MinutesCounted);
        Assert.Equal(60, ledger.BedMinutes);
        Assert.Equal(0.25, ledger.BedFraction, 12);
    }

    [Fact]
    public void 没床的主动卧床_床加成仍为零()
    {
        var ledger = new RestLedger();
        ledger.RecordMinutes(720, onBed: false);
        Assert.Equal(0, ledger.BedMinutes);
        Assert.Equal(0, ledger.BedFraction);
    }

    [Fact]
    public void 清账后重新开始()
    {
        var ledger = new RestLedger();
        ledger.RecordMinutes(30, onBed: true);
        ledger.Reset();
        Assert.Equal(0, ledger.MinutesCounted);
        Assert.Equal(0.0, ledger.BedFraction);
    }

    // ---------------- 床位登记册：一人一床、一床一人 ----------------

    [Fact]
    public void 一床只能躺一个人()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        Assert.True(reg.TryClaimSpecific("床#1", pawnId: 1));
        Assert.False(reg.TryClaimSpecific("床#1", pawnId: 2)); // 被占了
        Assert.Equal(0, reg.FreeBeds);
    }

    [Fact]
    public void 一人只能占一张床_换床自动退旧()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        reg.AddBed("床#2");
        Assert.True(reg.TryClaimSpecific("床#1", 1));
        Assert.True(reg.TryClaimSpecific("床#2", 1)); // 换到 2 号
        Assert.Equal("床#2", reg.BedOf(1));
        Assert.Equal(1, reg.FreeBeds); // 1 号床被退回来了，不许攥两张
    }

    [Fact]
    public void 重复占同一张床是幂等的()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        Assert.True(reg.TryClaimSpecific("床#1", 1));
        Assert.True(reg.TryClaimSpecific("床#1", 1)); // 再下一次"去躺着"的令
        Assert.Equal(1, reg.TotalBeds - reg.FreeBeds);
    }

    [Fact]
    public void 自动分床取最早登记的空床()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        reg.AddBed("床#2");
        Assert.Equal("床#1", reg.TryClaim(1));
        Assert.Equal("床#2", reg.TryClaim(2));
        Assert.Null(reg.TryClaim(3)); // 没床了
    }

    [Fact]
    public void 已占床者再分床返回原床_不换床()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        reg.AddBed("床#2");
        reg.TryClaim(1);
        Assert.Equal("床#1", reg.TryClaim(1));
        Assert.Equal(1, reg.FreeBeds);
    }

    [Fact]
    public void 起床后床位释放()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        reg.TryClaim(1);
        reg.Release(1);
        Assert.False(reg.HasBed(1));
        Assert.Equal(1, reg.FreeBeds);
    }

    [Fact]
    public void 拆掉床把躺着的人赶下来()
    {
        var reg = new BedRegistry();
        reg.AddBed("床#1");
        reg.TryClaim(1);
        reg.RemoveBed("床#1");
        Assert.False(reg.HasBed(1)); // 床没了，他改打地铺（仍在休养，只是不吃床加成）
        Assert.Equal(0, reg.TotalBeds);
    }

    [Fact]
    public void 不存在的床占不了()
    {
        var reg = new BedRegistry();
        Assert.False(reg.TryClaimSpecific("床#9", 1));
    }

    // ---------------- 下令判定 ----------------

    [Fact]
    public void 有空床就能下令上床养病()
    {
        BedrestOrder o = BedrestLogic.CanOrderBedrest(
            alive: true, PawnRole.Idle, DayPhase.NightPrep, hasOwnBed: false, freeBeds: 1);
        Assert.True(o.Allowed);
        Assert.Equal(BedrestOrderStatus.Ok, o.Status);
    }

    [Fact]
    public void 没床不能下令_但要说清是因为没造床()
    {
        BedrestOrder o = BedrestLogic.CanOrderBedrest(true, PawnRole.Idle, DayPhase.NightPrep, false, freeBeds: 0);
        Assert.False(o.Allowed);
        Assert.Equal(BedrestOrderStatus.NoFreeBed, o.Status);
        Assert.Contains("床", o.Message); // 灰掉要给原因，别让玩家以为"这人不能养病"
    }

    [Fact]
    public void 已占床者即使零空床也能回去躺着()
    {
        BedrestOrder o = BedrestLogic.CanOrderBedrest(true, PawnRole.Idle, DayPhase.NightPrep, hasOwnBed: true, freeBeds: 0);
        Assert.True(o.Allowed);
    }

    [Fact]
    public void 出门探索的人够不着()
    {
        BedrestOrder o = BedrestLogic.CanOrderBedrest(true, PawnRole.Expedition, DayPhase.DayExplore, false, 5);
        Assert.Equal(BedrestOrderStatus.NotInCamp, o.Status);
    }

    [Fact]
    public void 聚餐相位不下令()
    {
        Assert.Equal(BedrestOrderStatus.MealPhase,
            BedrestLogic.CanOrderBedrest(true, PawnRole.Idle, DayPhase.DuskMeal, false, 5).Status);
    }

    [Fact]
    public void 死人不用养病()
    {
        Assert.Equal(BedrestOrderStatus.Dead,
            BedrestLogic.CanOrderBedrest(alive: false, PawnRole.Idle, DayPhase.NightPrep, false, 5).Status);
    }

    [Fact]
    public void 卧床的代价是夜班_白天躺着不顶工时()
    {
        Assert.True(BedrestLogic.CostsNightShift(DayPhase.NightAct));   // 夜里躺着=不站岗不生产
        Assert.False(BedrestLogic.CostsNightShift(DayPhase.DayExplore)); // 白天本就没工时，躺着不顶任何东西
        Assert.False(BedrestLogic.CostsNightShift(DayPhase.DayPrep));
    }

    // ---------------- 支使他去干别的 = 他得起床 ----------------

    [Fact]
    public void 叫他走去空地_他得起床()
        => Assert.True(BedrestLogic.WakesOnCommand(null)); // 地面移动令：没有目标容器

    [Theory]
    [InlineData("storage")]
    [InlineData("workbench")]
    [InlineData("door")]
    [InlineData("corpse")]
    public void 叫他去干别的活_他得起床(string role)
        => Assert.True(BedrestLogic.WakesOnCommand(role));

    [Fact]
    public void 但点床不叫醒他_否则起床toggle会失灵()
    {
        // 反例护栏：若这里返回 true，"点自己的床=起床"就会退化成"起床→走过去→又躺下"。
        Assert.False(BedrestLogic.WakesOnCommand(BedrestLogic.BedContainerRole));
        Assert.Equal("bed", BedrestLogic.BedContainerRole); // 与 camp.json 的 role 名对齐
    }

    // ---------------- 床是营地家具：可造可拆 ----------------

    [Fact]
    public void 床在家具表里_可造可拆()
    {
        Assert.NotNull(FurnitureBuildCost.Of("床"));
        Assert.NotNull(FurnitureBuildCost.BuildMinutes("床"));
        Assert.NotNull(FurnitureBuildCost.Of("床#3")); // 可重复摆放 → 实例名带流水号，按类型索引
    }

    // ---------------- 放置：床不许贴大门/围栏（用户拍板的禁建带） ----------------

    /// <summary>床喂给 PlacementRules 的规格。与 CampMain.Bedrest.cs 的 BedPlaceSpec 保持一致。</summary>
    private static PlaceableSpec BedSpecForPlacement()
        => new(BedSpec.FurnitureKey, BedSpec.Width, BedSpec.Height, IsSolid: BedSpec.IsSolid);

    [Fact]
    public void 床不许贴着围栏放_守64px禁建带()
    {
        // 用户原话：「为了防止玩家使用改装台、椅子等家具阻挡寻路，放置的时候就不允许贴着大门和围栏」。
        var bounds = new PlacementRules.Box(0, 0, 1000, 1000);
        var fence = new PlacementRules.Box(500, 0, 20, 1000);   // 一道竖着的围栏
        var defenses = new[] { fence };
        var none = System.Array.Empty<PlacementRules.Box>();

        // [T27] 本组测的是**禁建带**，不是"家具只能室内"那条 —— 故把整片测试区当成室内，
        // 免得床先撞上 OutdoorsNotAllowed 而测不到想测的东西。室内那条另有专测。
        var indoors = new[] { bounds };

        // 紧贴围栏 → 拒（禁建带 64px）
        Assert.Equal(PlacementVerdict.TooCloseToDefenses,
            PlacementRules.CanPlace(BedSpecForPlacement(),
                new System.Numerics.Vector2(460, 500), bounds, defenses, none, none, indoors));

        // 离远了 → 放行
        Assert.Equal(PlacementVerdict.Ok,
            PlacementRules.CanPlace(BedSpecForPlacement(),
                new System.Numerics.Vector2(200, 500), bounds, defenses, none, none, indoors));
    }

    [Fact]
    public void 床没有防线豁免_AllowedAgainstDefenses必须是false()
    {
        // 只有沙袋有豁免。床要是拿了豁免，玩家就能拿床贴着围栏堆——用户那条约束当场作废。
        Assert.False(BedSpecForPlacement().AllowedAgainstDefenses);
    }

    [Fact]
    public void 床是非实心的_人得走得上去躺下()
    {
        // 实心 + 挖导航洞 ⇒ 伤员被自己的床挡在外面。同座椅（非实心可站点）的既有口径。
        // 副作用是好的：不改寻路 ⇒ 零 kill box 风险。但**禁建带照守**（见上一条）——两件事无关。
        Assert.False(BedSpec.IsSolid);
        Assert.False(BedSpec.CarvesNavHole);
    }

    // ---------------- 中文名（DisplayNames 注册表） ----------------

    [Fact]
    public void 卧床养病有中文名()
    {
        Assert.Equal("卧床养病", DisplayNames.Of(PawnRole.Bedrest));
        Assert.DoesNotContain("Bedrest", DisplayNames.Of(PawnRole.Bedrest)); // 不许把英文枚举名泄给玩家
    }
}
