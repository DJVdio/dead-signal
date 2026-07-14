using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「<b>营地里每一件该减速的东西，都真的进了减速场</b>」的覆盖自检 —— 直接拿**真 camp.json** 过一遍。
///
/// <para>═══ <b>这组测试是为一个真踩过的坑写的，别删</b> ═══
/// T15 落地时，减速场只从 <c>_furniture</c> 重建，而 <c>_furniture</c> 只收<b>家具目录
/// （<see cref="FurnitureBuildCost"/>）里认得的、可拆的</b>东西。于是两类东西被<b>整个漏掉</b>：
/// <list type="number">
/// <item><b>座位</b>（<c>role="seat"</c>）—— 不可拆、不进 <c>_furniture</c>。
///       而<b>椅子恰恰是用户点名的那件可跨越家具</b>（原话：「椅子之类的别的家具都可以跨过」）。</item>
/// <item><b>门口的沙袋垒</b>（<c>role="cover"</c>，名叫"北门沙袋垒A"这类）—— authored 的，
///       <b>名字不在家具目录里</b>（目录里只有玩家造的那件"沙袋"）⇒ 永远进不了 <c>_furniture</c>。
///       ⇒ 用户拍板「沙袋也减速」时**权衡的那个效果**（涌门的丧尸被自家门口沙袋拖慢）<b>根本不会发生</b>。</item>
/// </list>
/// 两处都<b>不会报错、不会崩溃</b>，只是数值悄悄不对 —— 正是最难发现的那种 bug。
/// 本组把"该减速的都减速了"钉在**真 camp.json** 上：日后谁往营地加一件矮家具却忘了让它减速，这里直接红。
/// </para>
/// </summary>
public class CampPropSlowdownCoverageTests
{
    /// <summary>读 <c>godot/data/camp.json</c>（营地布局的唯一事实源；从本源文件路径往上找仓库根）。</summary>
    private static JsonElement CampJson([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "godot", "data", "camp.json")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "找不到 godot/data/camp.json —— 营地布局的事实源不该消失");
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir!.FullName, "godot", "data", "camp.json"))).RootElement.Clone();
    }

    private static IEnumerable<(string Name, string? Role)> Props()
    {
        foreach (JsonElement p in CampJson().GetProperty("props").EnumerateArray())
        {
            yield return (
                p.GetProperty("name").GetString()!,
                p.TryGetProperty("role", out JsonElement r) ? r.GetString() : null);
        }
    }

    /// <summary>
    /// <b>CampMain 收录减速块的两条路，原样镜像在这里</b>：
    /// ① 不进 <c>_furniture</c> 的可跨越矮物（<see cref="FurnitureTraversal.IsLooseTraversableProp"/>）；
    /// ② 进了 <c>_furniture</c> 的家具（在家具目录里 且 <see cref="FurnitureTraversal.IsTraversable"/>）。
    /// 两条路是<b>互斥</b>的（同一件东西被两边都收 ⇒ 减速被连乘两次 = 0.5625，见下方专测）。
    /// </summary>
    private static bool WouldSlow(string name, string? role)
        => FurnitureTraversal.IsLooseTraversableProp(role)
           || (FurnitureBuildCost.Of(name) is not null && FurnitureTraversal.IsTraversable(name));

    // ———————————————————— 该减速的，一件都不能漏 ————————————————————

    /// <summary>
    /// <b>门口那四垛 authored 沙袋垒必须减速</b> —— 这正是用户拍板「沙袋也减速」时权衡的那个效果：
    /// 涌门的丧尸被自家门口的沙袋拖慢（双向对称，自己人跨过去同样慢）。
    /// <para>⚠️ 修复前这条是<b>红的</b>：它们名叫"北门沙袋垒A"，不在家具目录里 ⇒ 进不了 <c>_furniture</c> ⇒ 从未被收录。</para>
    /// </summary>
    [Theory]
    [InlineData("北门沙袋垒A")]
    [InlineData("北门沙袋垒B")]
    [InlineData("南门沙袋垒A")]
    [InlineData("南门沙袋垒B")]
    public void 门口的沙袋垒必须减速(string name)
    {
        (string Name, string? Role) prop = Props().Single(p => p.Name == name);
        Assert.True(WouldSlow(prop.Name, prop.Role),
            $"{name} 没进减速场 —— 用户拍板「沙袋也减速」所图的正是它（涌门的丧尸被自家沙袋拖慢）");
    }

    /// <summary><b>椅子/座垫必须减速</b> —— 用户原话点名的就是「椅子」。</summary>
    [Theory]
    [InlineData("住宅-座椅A")]
    [InlineData("住宅-座椅B")]
    [InlineData("住宅-座垫C")]
    public void 椅子必须减速(string name)
    {
        (string Name, string? Role) prop = Props().Single(p => p.Name == name);
        Assert.True(WouldSlow(prop.Name, prop.Role), $"{name} 没进减速场 —— 椅子是用户点名的那件可跨越家具");
    }

    /// <summary>床（进 <c>_furniture</c> 的那条路）同样减速。</summary>
    [Fact]
    public void 床必须减速()
    {
        Assert.True(WouldSlow("床#1", "bed"));
    }

    // ———————————————————— 不该减速的，一件都不能混进来 ————————————————————

    /// <summary>
    /// <b>作业台不减速</b>：它们是实心的，人压根站不上去（用户点名改装台/烹饪台不可跨；工作台同类推定）。
    /// 给它们登记一块减速会是死代码，还会误导后人以为踩得上去。
    /// </summary>
    [Fact]
    public void 工作台不减速_它是实心的()
    {
        (string Name, string? Role) prop = Props().Single(p => p.Name == "工作台");
        Assert.False(WouldSlow(prop.Name, prop.Role));
    }

    /// <summary><b>尸体不是家具</b>：跨过祖母的尸体不减速（用户说的是"椅子之类的别的<b>家具</b>"）。零回归。</summary>
    [Fact]
    public void 尸体不减速_她不是家具()
    {
        (string Name, string? Role) prop = Props().Single(p => p.Name == "祖母的尸体");
        Assert.False(WouldSlow(prop.Name, prop.Role));
    }

    /// <summary><b>草垛/收音机不减速</b>：不在家具目录里 —— 它们不是"造出来的家具"，是实心道具，维持原样。</summary>
    [Theory]
    [InlineData("收音机")]
    [InlineData("牛棚-草垛A")]
    public void 非家具道具不减速(string name)
    {
        (string Name, string? Role) prop = Props().Single(p => p.Name == name);
        Assert.False(WouldSlow(prop.Name, prop.Role));
    }

    // ———————————————————— 两条收录路必须互斥 ————————————————————

    /// <summary>
    /// <b>同一件东西不许被两条路都收</b> —— 否则它的减速会被<b>连乘两次</b>（0.75 × 0.75 = 0.5625），
    /// 悄悄变成 −44% 而不是 −25%。床与玩家垒的沙袋走 <c>_furniture</c> 那条；座位与门口沙袋垒走 loose 那条。
    /// </summary>
    [Fact]
    public void 两条收录路互斥_不许同一件家具被连乘两次()
    {
        foreach ((string name, string? role) in Props())
        {
            bool loose = FurnitureTraversal.IsLooseTraversableProp(role);
            bool viaFurniture = FurnitureBuildCost.Of(name) is not null && FurnitureTraversal.IsTraversable(name);

            Assert.False(loose && viaFurniture,
                $"{name}（role={role}）被两条路同时收录 ⇒ 减速会连乘两次（0.5625），而不是 0.75");
        }
    }
}
