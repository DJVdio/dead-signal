using System;
using System.IO;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「读书时没座位 ⇒ 读速 ×0.90」是**全角色通则**（用户拍板），<b>不是诺蒂（书虫）的专属效果</b>。
/// 诺蒂的 authored 专属效果只有"书虫"（自身读速加成 + 满级全营加成），与座位无关。
///
/// 本组是防退化护栏，钉死两层：
/// ①<b>规则层</b>：任何角色——无 perk 的普通人、L1/L3 书虫——无座一律吃 ×0.90、有座一律 ×1.0；
///   座位系数与 perk **正交**（无座/有座之比恒为 0.90，不随 perk 变），谁也别把它挪回某个角色身上。
/// ②<b>接线层</b>：拿**真实 <c>godot/data/camp.json</c>** 的 <c>role=seat</c> props 灌进座位册，
///   验证座位真的登记得出来、且读者多于座位时后来者认领不到 ⇒ 真的走进 ×0.90 分支。
///   防的是"纯逻辑绿、消费层从没接线，惩罚永不触发"的空转。
/// </summary>
public class SeatReadingRuleTests
{
    /// <summary>从测试程序集向上找仓库根，定位 <c>godot/data/camp.json</c>（不写死绝对路径/工作目录）。</summary>
    private static string CampJsonPath()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "godot", "data", "camp.json");
            if (File.Exists(p))
                return p;
        }
        throw new FileNotFoundException("从测试程序集向上未找到 godot/data/camp.json");
    }

    private sealed class Prop
    {
        public string? name { get; set; }
        public string? role { get; set; }
        public double[]? rect { get; set; }
    }

    private sealed class Cfg
    {
        public Prop[]? props { get; set; }
    }

    /// <summary>照 CampMain.AddSeat 的口径：role=seat 的 prop 取矩形中心点登记进座位册。</summary>
    private static SeatRegistry LoadRealCampSeats()
    {
        Cfg cfg = JsonSerializer.Deserialize<Cfg>(File.ReadAllText(CampJsonPath()))!;
        var seats = new SeatRegistry();
        foreach (Prop p in cfg.props!)
        {
            if (p.role != "seat" || p.rect is not { Length: 4 })
                continue;
            seats.Add(p.rect[0] + p.rect[2] / 2, p.rect[1] + p.rect[3] / 2);
        }
        return seats;
    }

    /// <summary>无 perk 的普通角色（不是诺蒂）：无座就是 ×0.90——这条通则不挂在任何人的专属效果上。</summary>
    [Fact]
    public void 普通角色无座读书_照吃九折()
    {
        var 普通人 = new SurvivorPerks();
        Assert.Equal(0.0, 普通人.SelfReadingSpeedBonus); // 无任何 perk
        Assert.Null(普通人.Bookworm);

        double 无座 = ReadingSpeed.Effective(1.0, 普通人.SelfReadingSpeedBonus, hasSeat: false, campWideBonusSum: 0.0);
        double 有座 = ReadingSpeed.Effective(1.0, 普通人.SelfReadingSpeedBonus, hasSeat: true, campWideBonusSum: 0.0);

        Assert.Equal(0.90, 无座, precision: 10);
        Assert.Equal(1.00, 有座, precision: 10);
    }

    /// <summary>座位系数与 perk 正交：无 perk / L1 书虫 / L3 书虫，无座对有座之比**恒为 0.90**。</summary>
    [Theory]
    [InlineData(0.0)]   // 普通角色（山姆等，无读书 perk）
    [InlineData(0.25)]  // 诺蒂 L1/L2 书虫
    [InlineData(0.50)]  // 诺蒂 L3 书虫
    public void 无座九折对任何perk等级都成立(double selfBonus)
    {
        double 无座 = ReadingSpeed.Effective(1.0, selfBonus, hasSeat: false, campWideBonusSum: 0.0);
        double 有座 = ReadingSpeed.Effective(1.0, selfBonus, hasSeat: true, campWideBonusSum: 0.0);

        Assert.Equal(ReadingSpeed.NoSeatMultiplier, 无座 / 有座, precision: 10);
        Assert.Equal(0.90, 无座 / 有座, precision: 10);
    }

    /// <summary>连诺蒂本人（满级书虫）没座位也照样吃九折——她的专属效果买不到座位豁免。</summary>
    [Fact]
    public void 诺蒂满级书虫无座_也照吃九折()
    {
        var 诺蒂 = new SurvivorPerks();
        诺蒂.GrantBookworm();
        诺蒂.Bookworm!.AddReadingTime(BookwormPerk.Level3ThresholdHours); // 读满级
        Assert.Equal(3, 诺蒂.Bookworm.Level);

        double campWide = 诺蒂.CampWideReadingSpeedBonus; // L3 满级全营 +0.25（含她自己）
        double 有座 = ReadingSpeed.Effective(1.0, 诺蒂.SelfReadingSpeedBonus, hasSeat: true, campWideBonusSum: campWide);
        double 无座 = ReadingSpeed.Effective(1.0, 诺蒂.SelfReadingSpeedBonus, hasSeat: false, campWideBonusSum: campWide);

        Assert.Equal(有座 * 0.90, 无座, precision: 10);
    }

    /// <summary>真实 camp.json：3 把座位（座椅A/B + 座垫C）真的登记进座位册。</summary>
    [Fact]
    public void 真实camp_json_读出三个座位()
    {
        SeatRegistry seats = LoadRealCampSeats();

        Assert.Equal(3, seats.Count);
        Assert.Equal(3, seats.FreeCount);
    }

    /// <summary>
    /// 端到端接线：真营地 3 座、4 个人同夜读书 ⇒ 前 3 人认领到座位（×1.0），第 4 人认领失败（-1）
    /// ⇒ 消费层按"无座"把 hasSeat=false 喂给 <see cref="ReadingSpeed.Effective"/> ⇒ 真的读到 ×0.90。
    /// 第 4 人是谁都一样：这里让**诺蒂**当那个没抢到座位的人，她照样只有九折。
    /// </summary>
    [Fact]
    public void 真营地三座四读者_没抢到座位的第四人真的吃到九折()
    {
        SeatRegistry seats = LoadRealCampSeats();

        // 4 个读者依次就近认领（照 CampMain.StationReaders 的口径）。
        int[] claims = new int[4];
        for (int i = 0; i < 4; i++)
            claims[i] = seats.ClaimNearest(fromX: 500 + i * 10, fromY: 500);

        Assert.All(new[] { claims[0], claims[1], claims[2] }, idx => Assert.True(idx >= 0)); // 前 3 人有座
        Assert.Equal(-1, claims[3]);                                                          // 第 4 人无座
        Assert.Equal(0, seats.FreeCount);

        // 第 4 人 = 诺蒂（满级书虫），照 Pawn.AccrueReading：hasSeat = 座位认领成功与否。
        var 诺蒂 = new SurvivorPerks();
        诺蒂.GrantBookworm();
        诺蒂.Bookworm!.AddReadingTime(BookwormPerk.Level3ThresholdHours);

        bool hasSeat = claims[3] >= 0;
        double 她的读速 = ReadingSpeed.Effective(1.0, 诺蒂.SelfReadingSpeedBonus, hasSeat, 诺蒂.CampWideReadingSpeedBonus);
        double 若有座 = ReadingSpeed.Effective(1.0, 诺蒂.SelfReadingSpeedBonus, hasSeat: true, 诺蒂.CampWideReadingSpeedBonus);

        Assert.False(hasSeat);
        Assert.Equal(若有座 * ReadingSpeed.NoSeatMultiplier, 她的读速, precision: 10);

        // 读完释放后座位回到池子里，下一个人抢得到（防幽灵占座）。
        seats.Release(claims[0]);
        Assert.Equal(1, seats.FreeCount);
        Assert.True(seats.ClaimNearest(500, 500) >= 0);
    }
}
