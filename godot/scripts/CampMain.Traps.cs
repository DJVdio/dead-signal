using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource / SystemRandomSource
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>圈套陷阱的消费层接线</b>（批次21·T26）—— 用户原话是按相位抓取，且随场上数量递减、设最低值；具体数值以 Wiki 配置为准。
///
/// <para>
/// 规则与数值在 <see cref="TrapLogic"/> / <see cref="TrapSpec"/>（纯逻辑、零 Godot 依赖、单测覆盖）；
/// <b>本文件只做空间执行</b>：把陷阱立到场上、<b>每个昼夜段掷一次点（白天/夜晚各一次，共 2/天）</b>、把猎物塞进库存。
/// </para>
///
/// <para>
/// <b>交互范式零发明</b>：造陷阱走<b>沙袋那条链</b>（配方 → 库存「摆放」→ 左键落位 → Shift+右键拆走）。
/// <c>CampMain.cs</c> 里只留四个<b>一行的调用点</b>（放置分发 / 左右键 / 相位钩子 / 悬停提示），正文全在这儿
/// —— 同 <c>CampMain.Placement.cs</c> / <c>CampMain.Bedrest.cs</c> 开的先例（CampMain.cs 是并发热点，谁都不用等谁）。
/// </para>
///
/// <para>
/// <b>三条接线各自的落点，别在别处重复一遍</b>：
/// <list type="bullet">
/// <item><b>放置校验</b> → <c>CheckFurniturePlacement</c>（<c>CampMain.Placement.cs</c>）：64px 禁建带由它统一管，
///       本文件<b>不自己写贴边判定</b>。</item>
/// <item><b>可跨越 + 减速</b> → <b>白拿的</b>：<c>RebuildTraversalField</c> 从 <c>_furniture</c>（唯一真源）重建减速场，
///       而 <see cref="FurnitureTraversal.IsTraversable"/> 对陷阱缺省为 true（它不在作业台名册里）
///       ⇒ 陷阱一进 <c>_furniture</c> 就自动吃到 Wiki 配置减速。<b>这儿一行都不用写</b>。</item>
/// <item><b>存档</b> → <c>CampSave.PlacedFurniture</c> 那条唯一出口（<c>CampMain.Save.cs</c>），同床/沙袋。</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>陷阱捕猎掷点（项目铁律：随机一律走可注入源；测试侧用 SequenceRandomSource 复现）。</summary>
    private readonly IRandomSource _trapRng = new SystemRandomSource();

    /// <summary>玩家摆下的陷阱的命名序号（"陷阱#1" 起）。存档要带上它，否则读档后新造的陷阱会与旧的重名。</summary>
    private int _trapSeq;

    /// <summary>正处于"摆放陷阱"模式（左键落位、右键取消）。同沙袋/床。</summary>
    private bool _placingTrap;

    // ──────────────────────────────── 建造 → 自由摆放 ────────────────────────────────

    /// <summary>
    /// 库存面板点「摆放」一个陷阱 → 进入放置模式（左键落位、右键取消）。
    /// 由 <c>CampMain.cs</c> 的 <c>OnStashPlaceRequested</c> 一行分发过来。
    /// </summary>
    private void BeginTrapPlacement()
    {
        if (_inventory.MaterialCount(TrapSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里没有陷阱——先去工作台扎一个。", CampToast.Bad);
            return;
        }
        _placingTrap = true;
        BeginFurniturePlacement(TrapSpec.PlaceSpec);   // 绿/红落位预览（impl-placement 白送的）
        CloseStash();
    }

    /// <summary>放置模式下左键落位。拒绝时**不退出放置模式**（换个地方接着点，同沙袋/床的范式）。</summary>
    private void TryPlaceTrap(Vector2 cart)
    {
        // 64px 禁建带 + 边界 + 压实心物 + 摞家具，全由这一行管（规则在 PlacementRules，我不自己写贴边判定）。
        if (!CheckFurniturePlacement(TrapSpec.PlaceSpec, cart))
        {
            return;   // 拒绝提示已由 CheckFurniturePlacement 弹过
        }
        if (!_inventory.TrySpendMaterial(TrapSpec.ItemKey, 1))
        {
            _campToast.Show("库里没有陷阱——先去工作台扎一个。", CampToast.Bad);
            EndTrapPlacement();
            return;
        }
        PlaceTrapAt(cart);
        EndTrapPlacement();
    }

    /// <summary>退出摆放陷阱模式（落位成功 / 右键作罢 都走这儿）。</summary>
    private void EndTrapPlacement()
    {
        _placingTrap = false;
        EndFurniturePlacement();
    }

    /// <summary>
    /// 陷阱落位。<b>刻意不建碰撞体、不挖导航洞</b>（<see cref="TrapSpec.IsSolid"/> / <see cref="TrapSpec.CarvesNavHole"/> 恒 false）
    /// —— 它是贴地矮物，人和丧尸都跨得过去（跨过减速由 Wiki 配置提供，由减速场自动接管）。谁给它加碰撞体，kill box 就回来了。
    /// <para>提示里<b>报出这是第几个</b>：几率按序号递减，玩家有权知道自己正在把新陷阱摆进哪一档。</para>
    /// </summary>
    private void PlaceTrapAt(Vector2 cart)
    {
        string name = $"{TrapSpec.FurnitureNamePrefix}{++_trapSeq}";
        var size = new Vector2(TrapSpec.Width, TrapSpec.Height);
        var rect = new Rect2(cart - size / 2f, size);
        SpawnTrap(name, rect);

        int count = TrapCount();
        double chance = TrapLogic.ChanceOf(count);
        string hint = chance <= TrapLogic.MinChance
            ? "——这片地已经没多少东西可抓了"
            : "";
        _campToast.Show(
            $"{name} 支好了。营地里第 {count} 个，白天/夜里各掷一次、每次 {chance:P0} 的机会{hint}。", CampToast.Ok);
    }

    /// <summary>读档：把陷阱原地立回来（位置与名字由存档给定，不重新分配序号）。</summary>
    private void RespawnTrap(string name, Rect2 rect)
    {
        SpawnTrap(name, rect);
        _trapSeq = Mathf.Max(_trapSeq, SeqOfTrap(name));   // 序号推到它之后，免得下次造陷阱重名
    }

    /// <summary>
    /// 把一个陷阱立到场上（新造/读档共用）：视觉 + 可点击容器（⇒ Shift+右键可拆）+ 可拆家具账。
    /// <b>不建碰撞体、不挖导航洞、不进掩体场</b>（躲在一圈铁丝套后面挡不了枪）。
    /// </summary>
    private void SpawnTrap(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new double[] { 0.42, 0.40, 0.34 }, jitter = 0.22 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 31 + _trapSeq, height: 6f, cell: 32f, collect: visuals);

        // Body=null：它压根没有碰撞体。进 _furniture ⇒ ① 减速场自动收录（可跨越，减速值由 Wiki 配置提供）
        // ② Shift+右键走 impl-salvage 的通用家具拆除（按 FurnitureBuildCost["陷阱"] 折半返还）
        // ③ 存档走 CampSave.PlacedFurniture 那条唯一出口。
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "trap" });
    }

    /// <summary>陷阱实例名（"陷阱#3"）里的序号；解不出来给 0。</summary>
    private static int SeqOfTrap(string key)
        => TrapSpec.IsTrapFurniture(key)
           && int.TryParse(key[TrapSpec.FurnitureNamePrefix.Length..], out int n) ? n : 0;

    /// <summary>
    /// 悬停提示（由 <c>CampMain.cs</c> 的 role switch 一行分发过来）。
    /// <para>
    /// <b>把这一个陷阱的当前几率直接报出来</b>：几率按"场上第几个"递减，而这条曲线在地上<b>没有任何痕迹</b>——
    /// 不报的话，不同序位的陷阱在玩家眼里长得一样，但命中率可能已因数量递减。当前数值以 Wiki 配置为准。
    /// 这不是剧透，是把一条<b>玩家做决策必须知道</b>的规则摆到台面上（区别于烹饪那条刻意隐藏的热量线：
    /// 那里藏的是"配方答案"，此处露的是"边际收益"——藏了它，玩家只会盲目多摆，而那正是这条规则要劝阻的事）。
    /// </para>
    /// </summary>
    private string TrapHoverText(ContainerRef c)
    {
        int ordinal = SeqOfTrap(c.Name);
        int count = TrapCount();
        // 序号 ≠ 名次：拆掉过陷阱就会出现 陷阱#1/陷阱#5 并存。几率只看**当前场上有几个**（见 TrapLogic 类注：
        // 一相位的期望产出只取决于数量，与谁排第几无关），故这里按"名次"取——把它排在末位是最诚实的报法：
        // 玩家最关心的是"我再摆一个值不值"，而下一个就是第 count+1 名。
        double mine = TrapLogic.ChanceOf(System.Math.Min(ordinal, count));
        double next = TrapLogic.ChanceOf(count + 1);
        return $"陷阱（营地共 {count} 个）· 白天/夜里各掷一次、每次约 {mine:P0} 抓到老鼠或兔子 · 再多摆一个只有 {next:P0} · Shift+右键拆走";
    }

    // ──────────────────────────────── 每昼夜段捕猎结算（2/天）────────────────────────────────

    /// <summary>场上陷阱数（几率按"第 n 个"递减 ⇒ 每次掷点都要数一遍）。</summary>
    private int TrapCount() => _furniture.Keys.Count(TrapSpec.IsTrapFurniture);

    /// <summary>
    /// <b>一个昼夜段的陷阱结算</b>：场上每个陷阱各掷一次点，抓到的老鼠/兔子<b>直接入共享库存</b>（可下锅，见 <see cref="FoodCalories"/>）。
    /// <para>由 <c>CampMain.cs</c> 的 <see cref="OnGamePhaseChanged"/> 一行调用，且<b>只在 <see cref="TrapLogic.RollsOnPhase"/> 为真的两个昼夜段边界</b>
    /// （白天 <see cref="DayPhase.DawnMeal"/> + 夜晚 <see cref="DayPhase.DuskMeal"/>）触发 ⇒ <b>一天 2 次</b>（用户拍板：白天 1 次 + 夜晚 1 次）。
    /// 频率的唯一事实源在 <see cref="TrapLogic.RollsOnPhase"/>；这里不自己判相位，避免"一个写 2、一个按 8 触发"的两处漂移。</para>
    /// <para>
    /// <b>一个陷阱都没有就彻底静默</b>：不掷点、不入库、不弹提示（<see cref="TrapLogic.RollPhase"/> 在 count≤0 时一次点都不掷）。
    /// </para>
    /// </summary>
    private void ResolveTrapsForPhase()
    {
        // 掷点 + 逐只入库都走纯编排 TrapRuntime.ResolveCatch（消费层与单测同一段代码，见其类注）。
        // 没陷阱 / 本段全空手 ⇒ caught 为空 ⇒ 静默（空陷阱是常态，天天播报只会变成噪音）。
        IReadOnlyList<string> caught = TrapRuntime.ResolveCatch(TrapCount(), _inventory, _trapRng);
        if (caught.Count == 0)
        {
            return;
        }

        _campToast.Show($"陷阱里有东西：{DescribeCatch(caught)}。", CampToast.Ok);
        if (_stashPanel.Visible)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);   // 库存面板开着就顺手刷新
        }
    }

    /// <summary>把一把材料键翻成一句人话（"2 只老鼠、1 只兔子"）。</summary>
    private static string DescribeCatch(IReadOnlyList<string> caught)
        => string.Join("、", caught
            .GroupBy(k => k)
            .Select(g => $"{g.Count()} 只{Materials.Find(g.Key)?.DisplayName ?? g.Key}"));
}
