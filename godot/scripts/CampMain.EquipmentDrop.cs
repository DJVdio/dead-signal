using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 断肢装备的空间落点：营地落一具可搜刮地面容器（复用 CorpseYard 存档/衰 decay），
/// 探索关落一枚可重复搜刮发现点（复用关内尸体容器，离场随关卡清理）。
/// </summary>
public sealed partial class CampMain
{
    private int _equipmentDropSeq;

    /// <summary>
    /// 由 Actor 断肢消费层调用。掉落物只走 ContainerLoot/LootApplication，不能直接塞库存，
    /// 因而保留同一套搜刮时间、负重、读档与防重复语义。
    /// </summary>
    public void ReceiveEquipmentDrop(Actor actor, IReadOnlyList<LootItem> loot)
    {
        if (actor is null || loot is null || loot.Count == 0)
        {
            return;
        }

        string container = NextEquipmentDropContainer(actor);

        if (_currentLevel is TestExploration level)
        {
            // 探索关没有营地尸场/持久存档；沿用动态尸体容器，回营时 ClearLevelCorpses 统一注销。
            _containerLoot.Register(container, loot);
            _levelCorpseContainers.Add(container);
            level.AddCorpseSearchPoint(container, actor.GlobalPosition);
            GD.Print($"[探索关·断肢掉落] {container}：{string.Join("、", loot)}");
            return;
        }

        if (_corpseYard?.Spawn(actor.GlobalPosition, actor.BodyTint, actor.Radius) is not { } ground)
        {
            // 视觉层尚未就位时不凭空登记一个永远搜不到的容器；正常运行时 CorpseYard 已在 _Ready 创建。
            return;
        }

        ground.Loot.AddRange(loot);
        ground.ContainerId = container;
        RegisterCorpseContainer(ground); // 同一搜刮链 + 相位回收注销 + 存档 CaptureCorpses
        GD.Print($"[营地·断肢掉落] {container}：{string.Join("、", loot)}");
    }

    private string NextEquipmentDropContainer(Actor actor)
    {
        string name = CorpseYard.NameOf(actor);
        string container;
        do
        {
            _equipmentDropSeq++;
            // 保留 CorpseNaming.Marker（“的尸体 #”）以复用探索关的尸体路由，后缀区分“断肢遗落”而非死者尸体。
            container = $"{name}{CorpseNaming.Marker}断肢-{_equipmentDropSeq}";
        }
        while (_containerLoot.Has(container)); // 读档后水位从现有表避让，防覆盖旧掉落

        return container;
    }
}
