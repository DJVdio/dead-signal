using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 沙发的 Godot 消费层接线：配方产出 → 库存摆放 → 室内落位 → 作为升级座位登记 → 可拆/读档恢复。
/// 规则与数值在 <see cref="SofaSpec"/> / <see cref="SeatRegistry"/>，本文件只做空间执行。
/// </summary>
public sealed partial class CampMain
{
    private int _sofaSeq;
    private bool _placingSofa;
    private readonly Dictionary<string, int> _sofaSeatIndices = new();

    private static readonly PlaceableSpec SofaPlaceSpec =
        new(SofaSpec.FurnitureKey, SofaSpec.Width, SofaSpec.Height, SofaSpec.IsSolid);

    private void BeginSofaPlacement()
    {
        if (_inventory.MaterialCount(SofaSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里没有沙发——先去工作台造一张。", CampToast.Bad);
            return;
        }
        _placingSofa = true;
        BeginFurniturePlacement(SofaPlaceSpec);
        CloseStash();
    }

    private void TryPlaceSofa(Vector2 cart)
    {
        if (!CheckFurniturePlacement(SofaPlaceSpec, cart))
        {
            return;
        }
        if (!_inventory.TrySpendMaterial(SofaSpec.ItemKey, 1))
        {
            _campToast.Show("库里没有沙发——先去工作台造一张。", CampToast.Bad);
            EndSofaPlacement();
            return;
        }

        string name = $"{SofaSpec.FurnitureKey}#{++_sofaSeq}";
        var rect = new Rect2(
            cart - new Vector2(SofaSpec.Width / 2f, SofaSpec.Height / 2f),
            new Vector2(SofaSpec.Width, SofaSpec.Height));
        SpawnSofa(name, rect);
        _campToast.Show($"{name} 摆好了。坐上去读书更快，恢复也更快。", CampToast.Ok);
        EndSofaPlacement();
    }

    private void EndSofaPlacement()
    {
        _placingSofa = false;
        EndFurniturePlacement();
    }

    private void RespawnSofa(string name, Rect2 rect)
    {
        SpawnSofa(name, rect);
        _sofaSeq = Mathf.Max(_sofaSeq, SeqOfSofa(name));
    }

    private void SpawnSofa(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new double[] { 0.40, 0.28, 0.33 }, jitter = 0.08 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 73, height: 14f, cell: 40f, visuals);

        Vector2 center = rect.GetCenter();
        int seatIndex = _seats.Add(center.X, center.Y, SeatKind.Sofa);
        _sofaSeatIndices[name] = seatIndex;
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "sofa" });
    }

    /// <summary>家具通用拆除出口调用：注销沙发座位，避免拆后仍有幽灵座位/错误加成。</summary>
    private void RemoveSofaIfAny(string furnitureName)
    {
        if (!_sofaSeatIndices.Remove(furnitureName, out int seatIndex))
        {
            return;
        }
        _seats.Remove(seatIndex);
    }

    private static int SeqOfSofa(string key)
        => SofaSpec.IsSofaFurniture(key)
           && int.TryParse(key[(SofaSpec.FurnitureKey.Length + 1)..], out int n) ? n : 0;
}
