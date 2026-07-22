using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>玩家摆下的家具，读档后必须一件不少、一件不多、一寸不挪。</b>
///
/// <para><b>这里修的是什么</b>：<c>CampSave.PlacedFurniture</c> 这张表原本只装改装台和床，
/// 沙袋曾经漏存；当时那张平行 <c>Sandbags</c> 表从未被写入，导致
/// 玩家垒的沙袋读档后**整片消失**（只剩一个空的流水号）。沙袋要布 + 石料、要工时，
/// 还是玩家唯一能自己经营的防御工事——现已统一并入 <c>PlacedFurniture</c>，旧空表已删除。</para>
///
/// <para><b>还有对称的一半</b>：读档是**就地覆盖世界**（<c>ApplySave</c>），不是重载场景。
/// 所以不清场就复原的话，本局摆下、而存档里并没有的家具会**赖在场上不走**——
/// 读一个"还没造改装台"的旧档，台子照样杵在那儿，<c>HasModBench</c> 还是 true。
/// 一个少东西、一个多东西，根因是同一个：**存与复原没有对称**。</para>
///
/// <para>本组测的是**可测的那一半**：家具键的分类判定 + 存档数据模型的往返。
/// 空间侧（实体重新立起来、掩体场登记、导航洞回填）在 Godot 运行时层，靠 <c>RemoveFurniture</c>
/// 这个唯一出口保证不漏，不在纯逻辑测试的射程内。</para>
/// </summary>
public class PlacedFurnitureSaveTests
{
    [Fact]
    public void 沙袋只有PlacedFurniture一张位置表_禁止平行空表复活()
    {
        Assert.Null(typeof(CampSave).GetProperty("Sandbags"));
        Assert.NotNull(typeof(CampSave).GetProperty(nameof(CampSave.PlacedFurniture)));
    }

    // ———————————————————————————— 谁该进存档：分类判定 ————————————————————————————

    [Theory]
    [InlineData("沙袋#1", true)]
    [InlineData("沙袋#12", true)]
    [InlineData("沙袋", false)]        // 类型名（FurnitureBuildCost 的键），不是场上的实例
    [InlineData("工作台", false)]      // camp.json 预置：建图时原地长出来，不必存位置
    [InlineData("住宅-柜子", false)]
    [InlineData("改装台", false)]      // 是玩家摆的，但走它自己那条分支（键是唯一的，不带流水号）
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 只有玩家垒的沙袋才认作沙袋家具(string? name, bool expected)
    {
        Assert.Equal(expected, SandbagSpec.IsSandbagFurniture(name));
    }

    /// <summary>
    /// 沙袋**恒不挡路**——这是它获准自由摆放的全部理由，读档复原当然也不能偷偷把它变成实心的。
    /// 复原走的是"非实心"分支（不 AddSolid、不进 _navHoles、不重烘焙导航）；这条钉死那个前提。
    /// </summary>
    [Fact]
    public void 沙袋恒不挡路_读档复原也不该把它变成实心()
    {
        Assert.False(SandbagSpec.IsSolid);
        Assert.False(SandbagSpec.CarvesNavHole);

        // 对照：改装台是实心的 ⇒ 它复原后必须重烘焙导航，沙袋不必。
        Assert.True(WeaponModLogic.BenchSpec.IsSolid);
    }

    // ———————————————————————————— 存档数据模型：往返 ————————————————————————————

    /// <summary>
    /// 三垛沙袋 + 一台改装台 + 一张玩家造的床，存进去读回来：**一件不少，坐标一寸不挪**。
    /// 位置是玩家自己定的——错一个像素，他垒在门口的那道墙就不在门口了。
    /// </summary>
    [Fact]
    public void 沙袋与改装台的位置读档后逐个一致()
    {
        var saved = new List<PlacedFurnitureSave>
        {
            new() { Key = "改装台", X = 680, Y = 1000, W = 110, H = 74 },
            new() { Key = "沙袋#1", X = 1150, Y = 400, W = SandbagSpec.Width, H = SandbagSpec.Height },
            new() { Key = "沙袋#2", X = 1210, Y = 400, W = SandbagSpec.Width, H = SandbagSpec.Height },
            new() { Key = "床#3", X = 500, Y = 500, W = 40, H = 70 },
        };

        List<PlacedFurnitureSave> restored = RoundTrip(saved);

        Assert.Equal(saved.Count, restored.Count);
        foreach (PlacedFurnitureSave want in saved)
        {
            PlacedFurnitureSave? got = restored.FirstOrDefault(r => r.Key == want.Key);
            Assert.True(got is not null, $"「{want.Key}」读档后不见了");
            Assert.Equal(want.X, got!.X);
            Assert.Equal(want.Y, got.Y);
            Assert.Equal(want.W, got.W);
            Assert.Equal(want.H, got.H);
        }
    }

    /// <summary>
    /// 沙袋进的是**通用的 PlacedFurniture 表**（和改装台/床同一张），不再另开一张。
    /// 三垛沙袋就该在表里占三行——按**实例名**对号入座，摆了三垛就还三垛。
    /// </summary>
    [Fact]
    public void 三垛沙袋在存档里占三行且实例名各不相同()
    {
        var saved = new List<PlacedFurnitureSave>
        {
            new() { Key = "沙袋#1", X = 100, Y = 100, W = SandbagSpec.Width, H = SandbagSpec.Height },
            new() { Key = "沙袋#2", X = 200, Y = 100, W = SandbagSpec.Width, H = SandbagSpec.Height },
            new() { Key = "沙袋#3", X = 300, Y = 100, W = SandbagSpec.Width, H = SandbagSpec.Height },
        };

        List<PlacedFurnitureSave> restored = RoundTrip(saved);

        List<string> sandbags = restored
            .Where(r => SandbagSpec.IsSandbagFurniture(r.Key))
            .Select(r => r.Key!)
            .ToList();

        Assert.Equal(3, sandbags.Count);
        Assert.Equal(3, sandbags.Distinct().Count());
    }

    /// <summary>空营地（什么都没摆）存读一遍仍是空的——不该凭空长出家具。</summary>
    [Fact]
    public void 什么都没摆时读档不会凭空长出家具()
    {
        Assert.Empty(RoundTrip(new List<PlacedFurnitureSave>()));
    }

    /// <summary>把 <see cref="PlacedFurnitureSave"/> 列表塞进整份存档，走真正的编解码往返。</summary>
    private static List<PlacedFurnitureSave> RoundTrip(List<PlacedFurnitureSave> placed)
    {
        var data = new SaveData();
        data.Camp.PlacedFurniture = placed;

        string json = SaveCodec.Serialize(data);
        SaveLoadResult result = SaveCodec.Deserialize(json);

        Assert.True(result.Ok, $"存档读不回来：{result.Error}");
        return result.Data!.Camp.PlacedFurniture;
    }
}
