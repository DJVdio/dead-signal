using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>家具可跨越 + 跨过减速的消费层接线</b> —— 用户拍板「<b>椅子之类的别的家具都可以跨过，但是跨过时会减少 25% 的移动速度</b>」
/// 与「<b>改装台、烹饪台不允许跨越</b>」。
///
/// <para>
/// 规则与数值在 <see cref="FurnitureTraversal"/>（纯逻辑、零 Godot 依赖、单测覆盖）；<b>本文件只做两件事</b>：
/// ① 把场上的可跨越家具翻译成一堆减速矩形（<see cref="TraversalField"/>）；② 挂到 <see cref="Actor.Slowdowns"/>
/// 供一切 Actor 的移速链查询。减速的<b>乘算</b>发生在 <c>Actor</c> 的那条既有乘子链里（残疾 × 饥饿 × 骨折 × 战斗减速 × <b>家具</b>）。
/// </para>
///
/// <para>
/// <b>为什么单独开一个 partial 文件</b>：同 <see cref="CampMain"/> 的放置接线（<c>CampMain.Placement.cs</c>）——
/// <c>CampMain.cs</c> 是并发热点，而本块是自成一体的新增能力。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>家具减速场：可跨越家具的占地 + 移速乘子。挂给 <see cref="Actor.Slowdowns"/>。</summary>
    private readonly TraversalField _traversal = new();

    /// <summary>
    /// <b>不进 <c>_furniture</c> 的可跨越矮物</b>的占地矩形（<see cref="FurnitureTraversal.IsLooseTraversableProp"/>）：
    /// <b>座位</b>（不可拆，只登记在 <c>_seats</c> 那份"只有中心点、没有矩形"的册子里）与
    /// <b>门口的沙袋垒</b>（authored，名字"北门沙袋垒A"不在家具目录里 ⇒ 永远进不了 <c>_furniture</c>）。
    /// <para>
    /// 这两类若不单记一份，就会被减速场<b>整个漏掉</b> —— 而椅子正是用户点名的那件可跨越家具，
    /// 门口沙袋垒则正是「沙袋也减速」那条拍板所图的东西（涌门的丧尸被自家沙袋拖慢）。
    /// </para>
    /// </summary>
    private readonly List<Rect2> _looseTraversableRects = new();

    /// <summary>上次重建减速场时的家具件数（脏检查用；-1 = 还没建过）。</summary>
    private int _traversalFurnitureCount = -1;

    /// <summary>
    /// 家具增删了就重建减速场（摆了张床 / 垒了垛沙袋 / 拆走一个柜子）。<b>只在件数变了的帧重建</b>，
    /// 空闲营地零开销。
    ///
    /// <para>
    /// <b>为什么是"轮询件数"而不是在每个增删点各加一行登记</b>：家具的增删点散在好几个文件里
    /// （<c>CampMain.cs</c> 的建图与沙袋、<c>CampMain.Bedrest.cs</c> 的床、拆解那条路…），而且还会不断有新的
    /// —— 逐点登记意味着<b>每个新增家具的作者都得记得来这儿加一行，迟早漏一个</b>，而漏掉的那件家具会**悄无声息**
    /// 地不减速（没有报错、没有崩溃，只是数值不对）。从 <c>_furniture</c>（唯一真源）重建则<b>不可能漏</b>。
    /// </para>
    /// </summary>
    private void SyncTraversalField()
    {
        if (_traversalFurnitureCount == _furniture.Count)
        {
            return;
        }
        RebuildTraversalField();
    }

    /// <summary>
    /// 从<b>唯一真源</b>重建减速场：座位（建图时固定）+ 当前 <c>_furniture</c> 里所有<b>可跨越</b>的家具。
    /// <para>
    /// 作业台（工作台 / 改装台 / 烹饪台）<b>不在场里</b> —— 它们是实心的，人压根站不上去
    /// （<see cref="FurnitureTraversal.IsTraversable"/> 对它们为 false），给它们登记一块减速会是死代码。
    /// </para>
    /// </summary>
    private void RebuildTraversalField()
    {
        _traversal.Clear();

        // 不进 _furniture 的可跨越矮物：座位（用户点名的椅子）+ 门口的 authored 沙袋垒。
        foreach (Rect2 s in _looseTraversableRects)
        {
            _traversal.Add(s.Position.X, s.Position.Y, s.Size.X, s.Size.Y);
        }

        // 其余家具（床 / 沙袋 / 柜子 / 衣柜 / 展示柜…）：_furniture 是唯一真源，谁往里加都自动吃到减速。
        foreach (KeyValuePair<string, FurnitureInstance> kv in _furniture)
        {
            if (!FurnitureTraversal.IsTraversable(kv.Key))
            {
                continue; // 作业台：实心，站不上去
            }
            Rect2 r = kv.Value.Rect;
            _traversal.Add(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
        }

        _traversalFurnitureCount = _furniture.Count;
    }
}
