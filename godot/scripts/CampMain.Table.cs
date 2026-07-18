using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>桌子的消费层接线</b>（批次21·T25）—— 玩家可造、可自由摆放的<b>纯家具</b>。
///
/// <para>
/// 规格与规则在 <see cref="TableSpec"/>（纯逻辑、零 Godot 依赖、单测覆盖）；<b>本文件只做空间执行</b>：
/// 把桌子立在场上、从库存扣一件、拆走时抹干净。
/// </para>
///
/// <para>
/// <b>⚠️ 桌子没有任何玩法作用</b>（用户只说了要这个配方，没说它干什么用；营地里也没有"桌子"这个概念）——
/// 它现在就是一件可造、可摆、<b>可跨越（跨过减速由 Wiki 配置提供）</b>、可拆的家具。真正的用处待用户定，见 <see cref="TableSpec"/> 类注。
/// </para>
///
/// <para>
/// <b>交互范式零发明</b>：整条链照抄床/沙袋 —— 配方产出一件"桌子" → 库存点「摆放」→ 左键落位 → 右键作罢。
/// 放置校验走 <see cref="CampMain.CheckFurniturePlacement"/>（不许贴大门/围栏的 64px 禁建带，含绿/红落位预览）。
/// 减速<b>不在这儿登记</b>：<see cref="RebuildTraversalField"/> 从 <c>_furniture</c>（唯一真源）统一重建，
/// 谁往里加家具都自动吃到 Wiki 配置减速（见 <c>CampMain.Traversal.cs</c> 的类注：逐点登记迟早漏一个）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>玩家摆的桌子的命名序号（"桌子#1" 起；<c>camp.json</c> 里一张桌子都没有，故从 0 起）。</summary>
    private int _tableSeq;

    /// <summary>正处于"摆放桌子"模式（左键落位、右键取消）。同沙袋/床。</summary>
    private bool _placingTable;

    /// <summary>
    /// 库存面板点「摆放」一张桌子 → 进入放置模式。由 <c>CampMain.OnStashPlaceRequested</c> 一行分发过来。
    /// </summary>
    private void BeginTablePlacement()
    {
        if (_inventory.MaterialCount(TableSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里没有桌子——先去工作台造一张。", CampToast.Bad);
            return;
        }
        _placingTable = true;
        BeginFurniturePlacement(TableSpec.PlaceSpec); // 绿/红落位预览（impl-placement 白送的）
        CloseStash();
    }

    /// <summary>放置模式下左键落位。拒绝时**不退出放置模式**（换个地方接着点，同沙袋/床范式）。</summary>
    private void TryPlaceTable(Vector2 cart)
    {
        if (!CheckFurniturePlacement(TableSpec.PlaceSpec, cart))
        {
            return; // 拒绝提示已由 CheckFurniturePlacement 弹过
        }
        if (!_inventory.TrySpendMaterial(TableSpec.ItemKey, 1))
        {
            _campToast.Show("库里没有桌子——先去工作台造一张。", CampToast.Bad);
            EndTablePlacement();
            return;
        }

        string name = $"{TableSpec.FurnitureKey}#{++_tableSeq}";
        var rect = new Rect2(
            cart - new Vector2(TableSpec.Width / 2f, TableSpec.Height / 2f),
            new Vector2(TableSpec.Width, TableSpec.Height));
        SpawnTable(name, rect);
        _campToast.Show($"{name} 摆好了。可以绕过去，也可以直接跨过去——只是会慢一点。", CampToast.Ok);
        EndTablePlacement();
    }

    /// <summary>退出摆放桌子模式（落位成功 / 右键作罢 都走这儿）。</summary>
    private void EndTablePlacement()
    {
        _placingTable = false;
        EndFurniturePlacement();
    }

    /// <summary>读档：把一张桌子按存档里的**实例名 + 原位置**立回世界（不扣库存、不改流水号、不弹提示）。</summary>
    private void RespawnTable(string name, Rect2 rect)
    {
        SpawnTable(name, rect);
        _tableSeq = Mathf.Max(_tableSeq, SeqOfTable(name)); // 序号推到它之后，免得下次摆桌子重名
    }

    /// <summary>
    /// 把一张桌子立到场上（新摆/读档共用）：视觉 + 可拆家具账 + 可点击登记。
    /// <b>不建碰撞体、不挖导航洞</b>（<see cref="TableSpec.IsSolid"/> 恒 false ⇒ 跨得过去，摆不出 kill box），
    /// 故也<b>不必重烘焙导航</b>。减速由 <see cref="RebuildTraversalField"/> 从 <c>_furniture</c> 自动收。
    /// <para>
    /// <b><c>_containers</c> 那行不是可有可无的</b>：拆除的目标是<b>可点击的容器</b>（<c>CanSalvageTarget</c> 收的是
    /// <c>ContainerRef</c>）—— 不登记，这张桌子就<b>拆不走也点不着</b>，摆错了地方只能一直杵在那儿（同沙袋的 "sandbag" 登记）。
    /// </para>
    /// </summary>
    private void SpawnTable(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new double[] { 0.50, 0.40, 0.30 }, jitter = 0.07 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 41, height: 12f, cell: 36f, visuals);

        // 半身掩体登记（用户拍板：躲在桌子后挨远程有配置概率无伤）：贴着它的**双方**都吃同一配置效果；不拦近战（矮物）。
        _coverField.Add(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y,
            TableSpec.CoverChance, TableSpec.BlocksMelee);

        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "table" });
        // 注销不必在这儿写：RemoveFurniture（拆除的唯一出口）已经通用地 _containers.RemoveAll(名字)
        // + _coverField.RemoveRect(它的矩形) 了 —— 拆了桌子，掩体概率跟着没（不会对着一片空地白享掩体）。
    }

    /// <summary>桌子实例名（"桌子#3"）里的序号；解不出来给 0。</summary>
    private static int SeqOfTable(string key)
        => TableSpec.IsTableFurniture(key)
           && int.TryParse(key[(TableSpec.FurnitureKey.Length + 1)..], out int n) ? n : 0;
}
