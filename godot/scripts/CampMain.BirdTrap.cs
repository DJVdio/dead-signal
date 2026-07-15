using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource / SystemRandomSource
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>捕鸟陷阱的消费层接线</b>（T75）—— 形态照抄 <see cref="CampMain"/> 的圈套陷阱（<c>CampMain.Traps.cs</c>）：
/// 造陷阱走沙袋那条链（配方 → 库存「摆放」→ 左键落位 → Shift+右键拆走），<b>本文件只做空间执行</b>：
/// 把陷阱立到场上、每个昼夜段掷一次点（白天/夜晚各一次，共 2/天）、把捕到的鸟塞进库存。
///
/// <para>
/// 🔴 <b>此前这一整条接线是缺的</b>：<see cref="BirdTrapSpec"/> / <see cref="BirdTrapLogic"/> 纯逻辑早就在盘上，
/// 但既不在 <see cref="PlaceableItems"/>、<c>OnStashPlaceRequested</c> 也无分支、<c>OnGamePhaseChanged</c> 也没掷点
/// ⇒ 玩家<b>根本摆不出来</b>、就算摆出来也<b>永远抓不到鸟</b>。这是"纯逻辑绿≠功能生效"的又一例，本单一并补上。
/// </para>
///
/// <para>
/// 规则与数值在 <see cref="BirdTrapLogic"/>（几率递减、掷点）与 <see cref="BirdTrapRuntime"/>（掷点 + 入库的纯编排，
/// 消费层与单测同一段代码）。掷点频率由 <see cref="TrapLogic.RollsOnPhase"/> 在 <c>CampMain.cs</c> 的 <c>OnGamePhaseChanged</c> 统一 gate
/// —— 与圈套陷阱共用一张尺子，一天 2 次（白天 1 + 夜晚 1）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>捕鸟陷阱捕猎掷点（项目铁律：随机一律走可注入源；测试侧用 SequenceRandomSource 复现）。</summary>
    private readonly IRandomSource _birdTrapRng = new SystemRandomSource();

    /// <summary>玩家摆下的捕鸟陷阱的命名序号（"捕鸟陷阱#1" 起）。存档要带上它，否则读档后新造的会与旧的重名。</summary>
    private int _birdTrapSeq;

    /// <summary>正处于"摆放捕鸟陷阱"模式（左键落位、右键取消）。同圈套陷阱/沙袋/床。</summary>
    private bool _placingBirdTrap;

    // ──────────────────────────────── 建造 → 自由摆放 ────────────────────────────────

    /// <summary>库存面板点「摆放」一个捕鸟陷阱 → 进入放置模式。由 <c>CampMain.cs</c> 的 <c>OnStashPlaceRequested</c> 一行分发过来。</summary>
    private void BeginBirdTrapPlacement()
    {
        if (_inventory.MaterialCount(BirdTrapSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里没有捕鸟陷阱——先去工作台扎一个。", CampToast.Bad);
            return;
        }
        _placingBirdTrap = true;
        BeginFurniturePlacement(BirdTrapSpec.PlaceSpec);   // 绿/红落位预览（impl-placement 白送的）
        CloseStash();
    }

    /// <summary>放置模式下左键落位。拒绝时**不退出放置模式**（换个地方接着点，同圈套陷阱）。</summary>
    private void TryPlaceBirdTrap(Vector2 cart)
    {
        // 64px 禁建带 + 边界 + 压实心物 + 摞家具，全由这一行管（规则在 PlacementRules，不自己写贴边判定）。
        if (!CheckFurniturePlacement(BirdTrapSpec.PlaceSpec, cart))
        {
            return;   // 拒绝提示已由 CheckFurniturePlacement 弹过
        }
        if (!_inventory.TrySpendMaterial(BirdTrapSpec.ItemKey, 1))
        {
            _campToast.Show("库里没有捕鸟陷阱——先去工作台扎一个。", CampToast.Bad);
            EndBirdTrapPlacement();
            return;
        }
        PlaceBirdTrapAt(cart);
        EndBirdTrapPlacement();
    }

    /// <summary>退出摆放捕鸟陷阱模式（落位成功 / 右键作罢 都走这儿）。</summary>
    private void EndBirdTrapPlacement()
    {
        _placingBirdTrap = false;
        EndFurniturePlacement();
    }

    /// <summary>
    /// 捕鸟陷阱落位。<b>刻意不建碰撞体、不挖导航洞</b>（<see cref="BirdTrapSpec.IsSolid"/> / <see cref="BirdTrapSpec.CarvesNavHole"/> 恒 false）
    /// —— 它是贴地矮物，人和丧尸都跨得过去。提示里报出这是第几个（几率按序号递减）。
    /// </summary>
    private void PlaceBirdTrapAt(Vector2 cart)
    {
        string name = $"{BirdTrapSpec.FurnitureNamePrefix}{++_birdTrapSeq}";
        var size = new Vector2(BirdTrapSpec.Width, BirdTrapSpec.Height);
        var rect = new Rect2(cart - size / 2f, size);
        SpawnBirdTrap(name, rect);

        int count = BirdTrapCount();
        double chance = BirdTrapLogic.ChanceOf(count);
        string hint = chance <= BirdTrapLogic.MinChance
            ? "——这片天已经没多少鸟可网了"
            : "";
        _campToast.Show(
            $"{name} 支好了。营地里第 {count} 个，白天/夜里各查一次网、每次 {chance:P0} 的机会{hint}。", CampToast.Ok);
    }

    /// <summary>读档：把捕鸟陷阱原地立回来（位置与名字由存档给定，不重新分配序号）。</summary>
    private void RespawnBirdTrap(string name, Rect2 rect)
    {
        SpawnBirdTrap(name, rect);
        _birdTrapSeq = Mathf.Max(_birdTrapSeq, SeqOfBirdTrap(name));   // 序号推到它之后，免得下次造陷阱重名
    }

    /// <summary>
    /// 把一个捕鸟陷阱立到场上（新造/读档共用）：视觉 + 可点击容器（⇒ Shift+右键可拆）+ 可拆家具账。
    /// <b>不建碰撞体、不挖导航洞、不进掩体场</b>（一张网挡不了枪）。
    /// </summary>
    private void SpawnBirdTrap(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new double[] { 0.30, 0.36, 0.30 }, jitter = 0.24 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 53 + _birdTrapSeq, height: 7f, cell: 32f, collect: visuals);

        // Body=null：它没有碰撞体。进 _furniture ⇒ 减速场自动收录 / Shift+右键通用拆除 / 存档走 PlacedFurniture 唯一出口。
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "bird_trap" });
    }

    /// <summary>捕鸟陷阱实例名（"捕鸟陷阱#3"）里的序号；解不出来给 0。</summary>
    private static int SeqOfBirdTrap(string key)
        => BirdTrapSpec.IsBirdTrapFurniture(key)
           && int.TryParse(key[BirdTrapSpec.FurnitureNamePrefix.Length..], out int n) ? n : 0;

    /// <summary>悬停提示（由 <c>CampMain.cs</c> 的 role switch 一行分发过来）。把这一个的当前几率报出来（曲线在地上没痕迹）。</summary>
    private string BirdTrapHoverText(ContainerRef c)
    {
        int ordinal = SeqOfBirdTrap(c.Name);
        int count = BirdTrapCount();
        double mine = BirdTrapLogic.ChanceOf(System.Math.Min(ordinal, count));
        double next = BirdTrapLogic.ChanceOf(count + 1);
        return $"捕鸟陷阱（营地共 {count} 个）· 白天/夜里各查一次网、每次约 {mine:P0} 网到一只鸟 · 再多摆一个只有 {next:P0} · Shift+右键拆走";
    }

    // ──────────────────────────────── 每昼夜段捕鸟结算（2/天）────────────────────────────────

    /// <summary>场上捕鸟陷阱数（几率按"第 n 个"递减 ⇒ 每次掷点都要数一遍）。</summary>
    private int BirdTrapCount() => _furniture.Keys.Count(BirdTrapSpec.IsBirdTrapFurniture);

    /// <summary>
    /// <b>一个昼夜段的捕鸟结算</b>：掷点 + 逐只入库都走纯编排 <see cref="BirdTrapRuntime.ResolveCatch"/>
    /// （消费层与单测同一段代码）。由 <c>CampMain.cs</c> 的 <c>OnGamePhaseChanged</c> 在 <see cref="TrapLogic.RollsOnPhase"/> 为真时调用
    /// ⇒ 一天 2 次（白天 1 + 夜晚 1）。没陷阱 / 本段全空手 ⇒ caught 为空 ⇒ 静默（空网是常态，天天播报只会变噪音）。
    /// </summary>
    private void ResolveBirdTrapsForPhase()
    {
        IReadOnlyList<string> caught = BirdTrapRuntime.ResolveCatch(BirdTrapCount(), _inventory, _birdTrapRng);
        if (caught.Count == 0)
        {
            return;
        }

        _campToast.Show($"网里有东西：{caught.Count} 只鸟。", CampToast.Ok);
        if (_stashPanel.Visible)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);   // 库存面板开着就顺手刷新
        }
    }
}
