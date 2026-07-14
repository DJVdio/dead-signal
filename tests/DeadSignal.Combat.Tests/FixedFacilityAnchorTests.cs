using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>固定设施锚点的设计期自检</b> —— 改装台（车间）/ 烹饪台（厨房）是**实心 + 固定**的设施：
/// 用户拍板它们不由玩家摆放，而是长在营地里的固定位置（"改装台放在车间，烹饪台放在厨房"）。
///
/// <para><b>为什么固定反而更需要这组测试，而不是更不需要</b>：
/// 玩家可摆放的家具摆错了，玩家自己能拆了重摆——错误是**可纠正**的。
/// 固定锚点摆错了，玩家**永远没有办法纠正**：它是实心的（挖导航洞、挡路、不可跨越），
/// 如果锚点压进围栏/大门边上那条 64px 的禁建带，就会在营地里留下一条**玩家无法消除的死路**——
/// 守卫走不到墙根、砌墙的人站不进施工位、逃命的路被自家的台子堵死。
/// 玩家能做的只有重开一局。**所以"能不能摆在这儿"这件事，从运行时校验变成了设计期校验——就是这组测试。**</para>
///
/// <para><b>本组直接读 <c>godot/data/camp.json</c></b>（而不是硬编码一份几何副本）：
/// 锚点和营地布局都住在那个文件里，只有真读它，"谁将来挪了锚点、或改了围栏/大门的位置"才会被抓住。
/// 抄一份坐标进测试 = 两份事实源 = 这个护栏第一天就失效。</para>
/// </summary>
public class FixedFacilityAnchorTests
{
    // ———————————————————————————— camp.json 读取 ————————————————————————————

    /// <summary>
    /// 读 <c>godot/data/camp.json</c>（营地布局的唯一事实源）。
    /// <para>从**本源文件的路径**（<see cref="CallerFilePathAttribute"/>）往上找仓库根，而不是从
    /// <c>AppContext.BaseDirectory</c> —— 后者随输出目录跑（bin/、临时校验工程…），找不找得到仓库全看运气。</para>
    /// </summary>
    private static JsonElement CampJson([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "godot", "data", "camp.json")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "找不到 godot/data/camp.json —— 营地布局的事实源不该消失");
        string json = File.ReadAllText(Path.Combine(dir!.FullName, "godot", "data", "camp.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static PlacementRules.Box BoxOf(JsonElement rect) => new(
        rect[0].GetSingle(), rect[1].GetSingle(), rect[2].GetSingle(), rect[3].GetSingle());

    private static IEnumerable<JsonElement> Arr(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    /// <summary>营地边界。</summary>
    private static PlacementRules.Box Bounds(JsonElement camp) => BoxOf(camp.GetProperty("mapBounds"));

    /// <summary>
    /// 防线 = 围栏 + 大门 + 门。禁建带只沿它们展开（建筑墙不设带——设施靠自家屋里墙角天经地义）。
    /// </summary>
    private static List<PlacementRules.Box> Defenses(JsonElement camp)
    {
        var list = new List<PlacementRules.Box>();
        foreach (JsonElement f in Arr(camp, "fences")) list.Add(BoxOf(f.GetProperty("rect")));
        foreach (JsonElement g in Arr(camp, "gates")) list.Add(BoxOf(g.GetProperty("rect")));
        foreach (JsonElement d in Arr(camp, "doors"))
        {
            if (d.TryGetProperty("rect", out JsonElement r)) list.Add(BoxOf(r));
        }
        return list;
    }

    /// <summary>一件 prop 是不是**实心**的（半身掩体 cover / 座位 / 尸体都是非实心矮物，不挖导航洞）。</summary>
    private static bool IsSolidProp(JsonElement p)
    {
        if (p.TryGetProperty("cover", out JsonElement c) && c.ValueKind == JsonValueKind.True) return false;
        if (p.TryGetProperty("role", out JsonElement r) && r.GetString() is "seat" or "corpse") return false;
        return true;
    }

    private static string? NameOf(JsonElement p)
        => p.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;

    /// <summary>
    /// 一座建筑的**墙段**（不是整块外框！）。
    /// <para><b>这是个容易踩的坑</b>：建筑在 <c>CampMain.BuildBuilding</c> 里只把**四面墙**建成实心
    /// （<c>WallSegments</c>），**屋内地板是可以走的**。要是把整块外框当实心物，那"设施摆在屋里"
    /// 就会被判成"压在建筑上"——而设施本来就该摆在屋里（改装台在车间、烹饪台在厨房）。</para>
    /// <para>此处**不扣门洞**（把门口也算作墙）：偏保守，正合适——一台实心设施要是正好堵在门口，
    /// 那本来就该被抓出来。</para>
    /// </summary>
    private static IEnumerable<PlacementRules.Box> WallsOf(JsonElement building)
    {
        PlacementRules.Box f = BoxOf(building.GetProperty("rect"));
        float wt = building.TryGetProperty("wallThickness", out JsonElement t) ? t.GetSingle() : 18f;

        yield return new PlacementRules.Box(f.X, f.Y, f.W, wt);                 // 上
        yield return new PlacementRules.Box(f.X, f.MaxY - wt, f.W, wt);         // 下
        yield return new PlacementRules.Box(f.X, f.Y, wt, f.H);                 // 左
        yield return new PlacementRules.Box(f.MaxX - wt, f.Y, wt, f.H);         // 右
    }

    /// <summary>场上一切实心障碍：建筑**的墙** + 实心 props（不含被检设施自己）+ 废墟。</summary>
    private static List<PlacementRules.Box> Solids(JsonElement camp, string except)
    {
        var list = new List<PlacementRules.Box>();
        foreach (JsonElement b in Arr(camp, "buildings")) list.AddRange(WallsOf(b));
        foreach (JsonElement p in Arr(camp, "props"))
        {
            if (!p.TryGetProperty("rect", out JsonElement r)) continue;
            if (NameOf(p) == except) continue;          // 被检设施自己不算障碍
            if (IsSolidProp(p)) list.Add(BoxOf(r));
        }
        foreach (JsonElement u in Arr(camp, "rubble"))
        {
            if (u.TryGetProperty("rect", out JsonElement r)) list.Add(BoxOf(r));
        }
        return list;
    }

    /// <summary>camp.json 里叫 <paramref name="name"/> 的那件 prop 的占位；没有这件 prop ⇒ null。</summary>
    private static PlacementRules.Box? AnchorOf(JsonElement camp, string name)
    {
        foreach (JsonElement p in Arr(camp, "props"))
        {
            if (NameOf(p) == name && p.TryGetProperty("rect", out JsonElement r)) return BoxOf(r);
        }
        return null;
    }

    // ———————————————————————————— 规格不变量 ————————————————————————————

    /// <summary>
    /// 改装台是**实心**、且**不豁免**防线禁建带。
    /// <para>固定锚点之后这条不但没作废，反而更要紧：玩家摆不了它 ⇒ 也就**纠正不了**它。
    /// 谁给它填上 <c>AllowedAgainstDefenses: true</c>，等于宣布"这台子可以贴着大门长"，
    /// 而玩家连拆了重摆的机会都没有。</para>
    /// </summary>
    [Fact]
    public void 改装台是实心设施且不豁免防线禁建带()
    {
        PlaceableSpec spec = WeaponModLogic.BenchSpec;

        Assert.True(spec.IsSolid, "改装台是实心的（挖导航洞、挡路、不可跨越）");
        Assert.False(spec.AllowedAgainstDefenses, "改装台不豁免禁建带——豁免只给恒不挡路的沙袋");
        Assert.Equal(WeaponModLogic.BenchFurnitureKey, spec.TypeName);
    }

    /// <summary>
    /// <b>「实心」与「不可跨越」必须是同一件事——两份事实源不许分叉。</b>
    ///
    /// <para>这两条现在住在**不同的文件、不同的表示**里：
    /// <list type="bullet">
    /// <item><see cref="WeaponModLogic.BenchSpec"/> 的 <c>IsSolid=true</c>（我这组锚点自检、放置规则都读它）</item>
    /// <item><c>FurnitureTraversal</c> 里一份**硬编码的字符串集合** <c>{"工作台","改装台","烹饪台"}</c>
    ///       —— 它**不引用** <see cref="WeaponModLogic.BenchFurnitureKey"/>，只是**恰好写了一样的四个字**</item>
    /// </list>
    /// 谁哪天改了 <c>BenchFurnitureKey</c> 的值、或往那个字符串集合里手滑，两边就**悄悄分叉**：
    /// 改装台会变成"可以一脚跨过去"的东西 —— 而**本组全部锚点测试的前提正是"玩家绕不开它"**
    /// （绕不开 + 固定 + 玩家挪不动 ⇒ 锚点摆错就是一条无法纠正的死路）。前提一塌，护栏就成了摆设。</para>
    ///
    /// <para>所以这条把两份事实源**焊在一起**：实心 ⇔ 不可跨越。</para>
    /// </summary>
    [Fact]
    public void 实心与不可跨越必须一致_两份事实源不许分叉()
    {
        // 改装台：规格说它实心 ⇒ 跨越表必须说它不可跨越。
        Assert.True(WeaponModLogic.BenchSpec.IsSolid);
        Assert.False(
            FurnitureTraversal.IsTraversable(WeaponModLogic.BenchFurnitureKey),
            $"「{WeaponModLogic.BenchFurnitureKey}」在放置规格里是实心的，跨越表却说它能跨过去——" +
            "两份事实源分叉了。它一旦可跨越，玩家就能绕开它，本组锚点自检的前提（绕不开、纠正不了）当场失效。");

        // 烹饪台：同构（impl-cooking 的固定设施）。
        Assert.True(CookStation.Spec.IsSolid);
        Assert.False(FurnitureTraversal.IsTraversable(CookStation.PropName));

        // 对照组：沙袋是矮物，玩家跨得过去——它本来就不该进这个集合。
        Assert.True(FurnitureTraversal.IsTraversable("沙袋#1"));
    }

    /// <summary>放置被拒时必须给玩家中文整句，不把英文枚举名泄出去。</summary>
    [Fact]
    public void 放置被拒时给玩家中文整句()
    {
        string text = PlacementRules.RejectionText(PlacementVerdict.TooCloseToDefenses);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("TooCloseToDefenses", text);
    }

    // ———————————————————————————— 锚点自检（本组的正主）————————————————————————————

    /// <summary>
    /// 需要做锚点自检的**固定实心设施**。新增这类设施时往这里加一行——
    /// 忘了加，它的锚点就没人守，可能悄悄在营地里种下一条死路。
    /// </summary>
    public static TheoryData<string> FixedFacilities() => new()
    {
        WeaponModLogic.BenchFurnitureKey,   // 改装台（车间＝空牛棚）—— impl-gunmod
        CookStation.PropName,               // 烹饪台（厨房＝住宅西侧那个角）—— impl-cooking
    };

    /// <summary>
    /// 一件固定设施的锚点占位。**先问代码常量，再退回 camp.json 的 prop**：
    /// <list type="bullet">
    /// <item>改装台的锚点是**代码常量**（<see cref="WeaponModLogic.BenchAnchorX"/>／<c>Y</c>）——
    ///       它不是 camp.json 的 prop（造好才落地，开局并不存在），只查 camp.json 会**永远查不到 ⇒ 自检静默空转**。</item>
    /// <item>将来若有设施把锚点写进 camp.json 的 props，也照样能被查到。</item>
    /// </list>
    /// 两处都没有 ⇒ <c>null</c>（该设施还没做完，本条自动跳过）。
    /// </summary>
    private static PlacementRules.Box? AnchorFor(string facility, JsonElement camp)
    {
        if (facility == WeaponModLogic.BenchFurnitureKey)
        {
            return new PlacementRules.Box(
                WeaponModLogic.BenchAnchorX, WeaponModLogic.BenchAnchorY,
                WeaponModLogic.BenchWidth, WeaponModLogic.BenchHeight);
        }

        // [批次21·T14] 烹饪台：锚点也是**代码常量**（同改装台——它是玩家造出来的，开局不存在，
        // 故不能是 camp.json 的预置 prop，否则第一天就白送一座灶）。
        if (facility == CookStation.PropName)
        {
            return new PlacementRules.Box(
                CookStation.AnchorX, CookStation.AnchorY,
                CookStation.Width, CookStation.Height);
        }

        // 将来若有设施把锚点写进 camp.json 的 props，也照样查得到。
        return AnchorOf(camp, facility);
    }

    /// <summary>
    /// <b>每一件固定实心设施的锚点，都必须能通过放置校验。</b>
    /// 校验的是**同一套** <see cref="PlacementRules"/>（玩家摆放时用的那套）——
    /// 区别只在于：玩家摆错了能重摆，设计者摆错了玩家只能重开一局，所以这次校验挪到了设计期。
    ///
    /// <para>红了怎么办：<b>别自己挪锚点</b>（那是设施主人的地盘），把 <see cref="PlacementVerdict"/> 和坐标
    /// 报给对应的 agent（改装台=impl-gunmod / 烹饪台=impl-cooking），或上抛主 agent。</para>
    ///
    /// <para>设施还没进 camp.json 时本条自动跳过——它一进来，护栏立刻生效，无需改测试。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(FixedFacilities))]
    public void 固定设施的锚点必须通过放置校验(string facility)
    {
        JsonElement camp = CampJson();
        PlacementRules.Box? anchor = AnchorFor(facility, camp);

        if (anchor is null)
        {
            return; // 锚点还没定（设施没做完）；它一落地（代码常量或 camp.json prop），本条自动开始守它。
        }

        PlacementRules.Box a = anchor.Value;
        var spec = new PlaceableSpec(facility, a.W, a.H, IsSolid: true);
        var center = new Vector2(a.X + a.W / 2f, a.Y + a.H / 2f);

        PlacementVerdict verdict = PlacementRules.CanPlace(
            spec, center, Bounds(camp), Defenses(camp), Solids(camp, except: facility), new List<PlacementRules.Box>());

        Assert.True(
            verdict == PlacementVerdict.Ok,
            $"「{facility}」的固定锚点 [{a.X},{a.Y},{a.W},{a.H}] 通不过放置校验：{verdict}" +
            $"（{PlacementRules.RejectionText(verdict)}）。" +
            "它是实心设施且玩家摆不了它 ⇒ 这会在营地里留下一条玩家永远无法纠正的死路。" +
            "别在测试里改坐标——去找设施主人挪锚点。");
    }

    /// <summary>
    /// 用户拍板「**改装台放在车间**」，而车间 = **空牛棚**（camp.json 里本没有"车间"，用户选定把空牛棚改造成车间）。
    /// ⇒ 锚点必须**整个落在空牛棚的墙内**。谁把它挪到院子里、挪出棚外，这里就红——
    /// 那不再是"车间里的改装台"，而是"露天的改装台"，与用户拍的板不符。
    /// </summary>
    [Fact]
    public void 改装台的锚点必须落在车间_空牛棚_里()
    {
        JsonElement camp = CampJson();

        PlacementRules.Box barn = Arr(camp, "buildings")
            .Where(b => NameOf(b) == "空牛棚")
            .Select(b => BoxOf(b.GetProperty("rect")))
            .Single();

        var anchor = new PlacementRules.Box(
            WeaponModLogic.BenchAnchorX, WeaponModLogic.BenchAnchorY,
            WeaponModLogic.BenchWidth, WeaponModLogic.BenchHeight);

        Assert.True(
            anchor.InsideOf(barn),
            $"改装台锚点 [{anchor.X},{anchor.Y},{anchor.W},{anchor.H}] 不在空牛棚（车间）[{barn.X},{barn.Y},{barn.W},{barn.H}] 内——" +
            "用户拍板改装台放在车间，不是放在院子里。");
    }

    /// <summary>
    /// [批次21·T14] 用户拍板「**烹饪台放在厨房**」。camp.json 里没有单独的"厨房"房间（只有 住宅/仓库/空牛棚），
    /// 同"空牛棚改造成车间"的既有处置：<b>厨房 = 住宅里的那个角</b> ⇒ 锚点必须**整个落在住宅的墙内**。
    /// 谁把这座灶挪到院子里，这里就红——那不是"厨房里的灶"，那是露天的火堆。
    /// </summary>
    [Fact]
    public void 烹饪台的锚点必须落在厨房_住宅_里()
    {
        JsonElement camp = CampJson();

        PlacementRules.Box house = Arr(camp, "buildings")
            .Where(b => NameOf(b) == "住宅")
            .Select(b => BoxOf(b.GetProperty("rect")))
            .Single();

        var anchor = new PlacementRules.Box(
            CookStation.AnchorX, CookStation.AnchorY,
            CookStation.Width, CookStation.Height);

        Assert.True(
            anchor.InsideOf(house),
            $"烹饪台锚点 [{anchor.X},{anchor.Y},{anchor.W},{anchor.H}] 不在住宅（厨房）[{house.X},{house.Y},{house.W},{house.H}] 内——" +
            "用户拍板烹饪台放在厨房，不是放在院子里。");
    }

    /// <summary>
    /// [批次21·T14] 烹饪台不许长在住宅里既有的家具身上（柜子 / 展示柜 / 座椅 / 床 / 收音机 / 祖母的尸体）。
    /// <para>上面那条 Theory 的 <c>solids</c> 只喂了**建筑与防线**（camp.json 的 props 各有各的实心与否，
    /// 测试侧无从一一判断）⇒ 屋里那几件东西得单独钉一遍，否则灶可能正好砌在祖母的尸体上。</para>
    /// </summary>
    [Fact]
    public void 烹饪台不压住宅里既有的任何家具()
    {
        JsonElement camp = CampJson();

        PlacementRules.Box house = Arr(camp, "buildings")
            .Where(b => NameOf(b) == "住宅")
            .Select(b => BoxOf(b.GetProperty("rect")))
            .Single();

        var anchor = new PlacementRules.Box(
            CookStation.AnchorX, CookStation.AnchorY,
            CookStation.Width, CookStation.Height);

        foreach (JsonElement prop in Arr(camp, "props"))
        {
            if (!prop.TryGetProperty("rect", out JsonElement rect)) continue;
            PlacementRules.Box box = BoxOf(rect);
            if (!box.Overlaps(house)) continue;   // 只管屋里的

            Assert.False(
                anchor.Overlaps(box, PlacementRules.Clearance),
                $"烹饪台锚点压在住宅里的「{NameOf(prop)}」[{box.X},{box.Y},{box.W},{box.H}] 上了。");
        }
    }

    /// <summary>
    /// <b>自检的自检</b>（变异测试）：拿**真营地的防线**，把一台改装台故意按在北大门的门口，
    /// 上面那条 Theory 用的同一套判定**必须把它抓出来**。
    ///
    /// <para>为什么非要有这条：真锚点进 camp.json 之前，那条 Theory 是空转的——
    /// 一个从没红过的护栏，和没有护栏是一回事。这条证明"机器是响的"：
    /// 判定接对了、防线真的解析出来了、坏锚点确实会被判死。</para>
    /// </summary>
    [Fact]
    public void 自检能抓出坏锚点_把改装台按在大门口必被判死()
    {
        JsonElement camp = CampJson();

        // 北大门 [1100,300,200,22]：正门口内侧一步——玩家永远拆不掉，等于把自家大门堵死。
        PlacementRules.Box gate = Defenses(camp).First(d => d.W == 200 && d.H == 22);
        var atTheGate = new Vector2(
            gate.X + gate.W / 2f,
            gate.Y + gate.H + WeaponModLogic.BenchHeight / 2f + 1f);

        PlacementVerdict verdict = PlacementRules.CanPlace(
            WeaponModLogic.BenchSpec, atTheGate, Bounds(camp), Defenses(camp),
            new List<PlacementRules.Box>(), new List<PlacementRules.Box>());

        Assert.Equal(PlacementVerdict.TooCloseToDefenses, verdict);
    }

    /// <summary>
    /// 锚点自检**确实在读真营地**的自证：camp.json 的防线必须真的解析出来了。
    /// 没有这条，上面那条 Theory 可能在"防线列表是空的"情况下一路绿灯——
    /// 那它守的就是个寂寞（任何锚点都不会侵入一条不存在的围栏）。
    /// </summary>
    [Fact]
    public void 自检确实读到了真营地的防线与边界()
    {
        JsonElement camp = CampJson();

        List<PlacementRules.Box> defenses = Defenses(camp);
        PlacementRules.Box bounds = Bounds(camp);

        Assert.True(defenses.Count >= 6, $"真营地至少有 6 段围栏 + 2 扇大门，只解析出 {defenses.Count} 段");
        Assert.True(bounds.W > 0 && bounds.H > 0, "营地边界必须是个有面积的矩形");
        Assert.Contains(defenses, d => d.W == 200 && d.H == 22);   // 南北大门 200×22（camp.json 真实尺寸）
    }
}
