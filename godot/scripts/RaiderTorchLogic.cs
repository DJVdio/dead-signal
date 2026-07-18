using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>
/// 劫掠者战斗火把的纯消费核：决定火把是否点亮，以及把持光者投影为场光源。
/// 空间层只负责把结果交给 <see cref="LightField"/>；不在这里依赖 Godot 节点。
/// </summary>
public static class RaiderTorchLogic
{
    /// <summary>有活着的敌人且未撤退时点火；撤退先收火把，避免逃跑者成为移动信标。</summary>
    public static bool ShouldCarryTorch(bool hasLiveEnemy, bool retreating)
        => hasLiveEnemy && !retreating;

    /// <summary>
    /// 把一个劫掠者的战斗态投影为 <see cref="PlacedLight"/>。未点火返回 null，调用方可直接跳过。
    /// </summary>
    public static PlacedLight? PlaceTorch(Vector2 position, bool hasLiveEnemy, bool retreating)
    {
        if (!ShouldCarryTorch(hasLiveEnemy, retreating)
            || LightSource.Find(LightSource.TorchKey) is not LightProfile torch)
        {
            return null;
        }

        return new PlacedLight(position.X, position.Y, torch);
    }
}
