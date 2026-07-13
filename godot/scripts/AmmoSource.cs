using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 InventoryStore.cs / Materials.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 弹药源：开火时"这个单位有多少弹、扣得掉吗"的抽象。判定走引擎纯函数 AmmoLogic.PlanShot，
// 实扣由本接口落到具体载体（玩家=营地共享库存；敌方=无库存模型→无限）。

/// <summary>
/// 一个开火单位的弹药来源。<see cref="Actor"/> 持有一个，开火前查余量、开火后实扣。
/// 拆成接口是因为**玩家与敌方的弹药载体根本不同**：玩家吃营地共享库存（<see cref="InventoryStore"/>），
/// 而丧尸/劫掠者没有库存模型，若强行给它们建库存等于凭空造一套 AI 补给系统。
/// </summary>
public interface IAmmoSource
{
    /// <summary>该弹药键当前余量。</summary>
    int Count(string ammoKey);

    /// <summary>实扣 <paramref name="amount"/> 发；不足则原样不动返回 <c>false</c>。</summary>
    bool Spend(string ammoKey, int amount);

    /// <summary>回收入库 <paramref name="amount"/> 发（箭矢捡回）。不支持回收的源可空实现。</summary>
    void Recover(string ammoKey, int amount);
}

/// <summary>
/// 无限弹药源：敌方单位（劫掠者等）与一切无库存模型的单位用它 —— **既有行为零回归**，
/// 开火判定恒通过、不扣任何东西。设计上这是有意的：敌人的补给不是玩家要管的资源，
/// 且劫掠者身上的弹药以**战利品**形式回流给玩家（见战利品投放），比模拟他们的弹匣更有意义。
/// </summary>
public sealed class UnlimitedAmmoSource : IAmmoSource
{
    /// <summary>共享单例（无状态）。</summary>
    public static readonly UnlimitedAmmoSource Instance = new();

    public int Count(string ammoKey) => int.MaxValue;

    public bool Spend(string ammoKey, int amount) => true;

    public void Recover(string ammoKey, int amount)
    {
        // 无限源无需回收。
    }
}

/// <summary>
/// 库存弹药源：玩家幸存者用它，直接读写**营地共享库存**（用户拍板：背包=营地共享库存，不做每人背包）。
/// 弹药是可堆叠材料（<see cref="ItemCategory.Material"/>），故余量/实扣直接复用既有的
/// <see cref="InventoryStore.MaterialCount"/> / <see cref="InventoryStore.TrySpendMaterial"/>，不新造扣减路径。
/// </summary>
public sealed class InventoryAmmoSource : IAmmoSource
{
    private readonly InventoryStore _inventory;

    public InventoryAmmoSource(InventoryStore inventory) => _inventory = inventory;

    public int Count(string ammoKey) => _inventory.MaterialCount(ammoKey);

    public bool Spend(string ammoKey, int amount) => _inventory.TrySpendMaterial(ammoKey, amount);

    /// <summary>捡回的箭入库（落地为一堆该弹药材料，与搜刮/制作产出同构）。</summary>
    public void Recover(string ammoKey, int amount)
    {
        if (amount <= 0 || Materials.Find(ammoKey) is not { } def)
        {
            return;
        }

        _inventory.Add(def.ToItem(amount));
    }
}
