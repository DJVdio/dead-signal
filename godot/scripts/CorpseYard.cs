using System;
using System.Collections.Generic;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 场上尸体的管家（空间执行层）：接死亡事件 → 走 <see cref="CorpseField"/> 纯逻辑解出落点 → 在 iso 层
/// 生成一具 <see cref="Corpse"/>。规则（同格不堆叠 / 外扩找空位 / 不落进墙）全在纯逻辑里，本类只负责
/// 「把通行性问答接到导航图上」「把尸体节点放到那个坐标」「场上尸体太多时回收最老的」。
///
/// <b>尸体不阻挡移动</b>：本类从不建碰撞体、不改导航图、不加 NavigationObstacle——尸体格只决定「下一具
/// 尸体往哪躺」，谁都能从尸体上走过去。（战术后果：门前的尸体不挡丧尸，但会把后来的**尸体**推得越来越远，
/// 尸堆自然铺开成一片；「一扇门前只挤得下 3~4 只丧尸」是丧尸自己的碰撞体积造成的，与尸体格无关。）
/// </summary>
public sealed partial class CorpseYard : Node
{
    /// <summary>场上并发尸体上限：超限即回收最老的一具（并还格）。尸潮后满地尸体时护住节点数。「拟定待调」</summary>
    public const int MaxCorpses = 240;

    /// <summary>
    /// 「可通行」判定的容差（像素）：格中心到导航面最近点的距离在此之内即算可躺。
    /// 导航面按 agent 半径内缩过，故容差取一个人形半径量级——尸体可以紧贴墙根躺下，但躺不进墙里。「拟定待调」
    /// </summary>
    public const float PassableTolerance = 12f;

    private readonly CorpseField _field = new();
    private readonly List<Corpse> _live = new();   // 生成顺序（表头最老），超限从头回收
    private int _nextId;                           // 尸体容器 id 序号（同名容器会互相顶掉登记，故必须唯一）
    private int _phaseTick;                        // 单调相位计数（每次相位切换 +1），尸体据此到期

    /// <summary>场上尸体数（= 被占的尸体格数）。</summary>
    public int Count => _live.Count;

    /// <summary>当前相位计数（单调递增，仅供尸体到期判定/测试用）。</summary>
    public int PhaseTick => _phaseTick;

    /// <summary>
    /// 这个可搜刮点还剩几个相位就烂没了（供悬停提示）。<b>不在场上的返回 -1</b>——
    /// 祖母那具 authored 尸体不是本 Yard 的尸体（她永远躺在那儿，没有倒计时），已清走的也一样。
    /// 「还剩多久」是纯粹的决策信息（我先扒哪具？值不值得为它多待一个相位），藏起来只会制造挫败感。
    /// </summary>
    public int PhasesRemainingFor(string containerId)
    {
        if (string.IsNullOrEmpty(containerId))
        {
            return -1;
        }
        foreach (Corpse c in _live)
        {
            if (c.ContainerId == containerId)
            {
                var entry = new CorpseDecayEntry(containerId, c.SpawnPhaseTick, Authored: false);
                return CorpseDecay.PhasesRemaining(entry, _phaseTick);
            }
        }
        return -1;
    }

    /// <summary>
    /// 一具尸体被回收（超限淘汰 / 清走 / 清场）。营地层订阅它注销该尸体的可搜刮容器登记——
    /// 尸体没了，地图上那个可点击的点也必须跟着消失。
    /// </summary>
    public event Action<Corpse>? Recycled;

    public override void _Ready() => AddToGroup("corpse_yard");

    /// <summary>
    /// 某单位倒下：在其脚下落一具尸体（同格已有尸体则自动挤到旁边最近的空地），并把它身上穿的东西
    /// <b>原样</b>变成这具尸体的战利品（<see cref="CorpseLoot.Strip"/>：**穿什么扒什么，零掷骰**）。
    /// <para>
    /// 于是"那只穿牛仔外套的丧尸"倒下之后，地上躺着的就是**一件牛仔外套**——玩家在动手之前就看得见它值多少，
    /// 值不值得冒险自己算。<b>这里没有随机源，是有意的</b>（用户拍板推翻了掷骰分档）。
    /// </para>
    /// </summary>
    public Corpse? SpawnFor(Actor actor)
    {
        Corpse? corpse = Spawn(actor.GlobalPosition, actor.BodyTint, actor.Radius);
        if (corpse is null)
        {
            return null;
        }

        corpse.Loot.AddRange(CorpseLoot.Strip(actor.WornArmor));
        if (corpse.Loot.Count > 0)
        {
            // 只有**身上真有东西**的尸体才拿 id（= 才会被登记成可搜刮点）。衣不蔽体的那些不进容器表——
            // 既不让悬停命中去遍历一堆"点了没反应"的点，也不给玩家一地假的可交互提示。
            corpse.ContainerId = $"{NameOf(actor)}的尸体 #{++_nextId}";
        }
        return corpse;
    }

    /// <summary>死者在搜刮提示里怎么称呼（丧尸没有名字，其余单位有）。序号由调用方补，保证容器名唯一。</summary>
    private static string NameOf(Actor actor) => actor switch
    {
        Pawn p => p.DisplayName,
        Raider r => r.DisplayName,
        Dog d => d.DisplayName,
        _ => "丧尸",
    };

    /// <summary>在 cartesian 世界点落一具尸体。返回生成的尸体节点（iso 层缺失时返回 null，纯视觉降级）。</summary>
    public Corpse? Spawn(Vector2 cartPos, Color bodyTint, float radius)
    {
        if (GetTree()?.GetFirstNodeInGroup("iso_layer") is not Node2D isoLayer)
        {
            return null;   // 视觉层未就位（headless/worktree）：不落尸体节点，也不占格
        }

        var placement = _field.Place(new System.Numerics.Vector2(cartPos.X, cartPos.Y), IsPassable);
        var landing = new Vector2(placement.Position.X, placement.Position.Y);

        var corpse = Corpse.Spawn(isoLayer, Iso.Project(landing), placement.Cell, bodyTint, radius);
        corpse.CartPosition = landing;        // 容器命中矩形用的是 cartesian，不是 iso
        corpse.SpawnPhaseTick = _phaseTick;   // 落地时的相位计数：此后过 CorpseDecay.LifetimePhases 个相位就烂没了
        _live.Add(corpse);

        while (_live.Count > MaxCorpses)
        {
            var oldest = _live[0];
            _live.RemoveAt(0);
            RecycleCorpse(oldest);
        }

        return corpse;
    }

    /// <summary>
    /// 相位切换（<see cref="DayPhase"/> 每变一次，营地层调一次）：相位计数 +1，并把**到期的尸体**清掉。
    /// <para>
    /// 用户拍板：尸体过三个相位自动清理——既缓解性能压力，也给了足够的时间去搜刮。搜刮窗口因此是硬的：
    /// 尸潮打完那一地尸体，三个相位内扒不完，就是扒不完（也顺带堵掉了挂机刷装备）。
    /// </para>
    /// <para>
    /// 🔴 <b>祖母的尸体不经这里</b>：她是 camp.json 的 role=corpse 静态 prop（authored 剧情），从不进
    /// <see cref="_live"/>——本 Yard 只装 <see cref="SpawnFor"/> 造出来的战斗尸体。规则口径另在
    /// <see cref="CorpseDecay"/>（authored 永不过期）钉死为第二道保险。
    /// </para>
    /// </summary>
    public void AdvancePhase()
    {
        _phaseTick++;

        // 一次相位切换扫一遍全场（≤ MaxCorpses=240 具，每相位一次，开销可忽略）；判定走纯逻辑。
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Corpse c = _live[i];
            var entry = new CorpseDecayEntry(c.ContainerId, c.SpawnPhaseTick, Authored: false);
            if (CorpseDecay.IsExpired(entry, _phaseTick))
            {
                _live.RemoveAt(i);
                RecycleCorpse(c);   // 与数量封顶同一个出口 ⇒ 容器登记必被注销，不泄漏
            }
        }
    }

    /// <summary>尸体被清走（焚烧/搬走）：移除节点并还格。</summary>
    public void Remove(Corpse corpse)
    {
        _live.Remove(corpse);
        RecycleCorpse(corpse);
    }

    /// <summary>清场（换关/重开）。</summary>
    public void ClearAll()
    {
        var all = new List<Corpse>(_live);
        _live.Clear();
        foreach (Corpse c in all)
        {
            Recycled?.Invoke(c);
            if (IsInstanceValid(c))
            {
                c.QueueFree();
            }
        }
        _field.Clear();
    }

    /// <summary>
    /// <b>唯一的回收出口</b>（数量封顶淘汰 / 相位到期清理 / 手动清走都走这里）：还格 → 发
    /// <see cref="Recycled"/>（营地层据此注销可搜刮容器登记 + 藏物清单，否则玩家会去搜一具已经不在的
    /// 尸体、且字典里的登记永远清不掉）→ 销毁节点。**任何新的清理路径都必须经过它。**
    /// 调用前请先把它从 <see cref="_live"/> 摘掉。
    /// </summary>
    private void RecycleCorpse(Corpse corpse)
    {
        if (!IsInstanceValid(corpse))
        {
            return;
        }
        // 堆叠兜底可能让两具尸体共用一格：仅当场上再无同格尸体时才还格，避免把还在用的格子放出去。
        if (!_live.Exists(c => c.Cell == corpse.Cell))
        {
            _field.Remove(corpse.Cell);
        }
        Recycled?.Invoke(corpse);
        corpse.QueueFree();
    }

    /// <summary>
    /// 该点能不能躺尸体：向导航图问「离你最近的可走点有多远」——墙里/水里/不可通行区的最近可走点在边界上，
    /// 距离超出容差即判不可躺，推挤逻辑会继续外扩。导航图未就绪（开局前几帧）→ 一律可躺，不阻断落尸。
    /// </summary>
    private bool IsPassable(System.Numerics.Vector2 w)
    {
        var world = GetTree()?.Root?.World2D;
        if (world is null)
        {
            return true;
        }
        Rid map = world.NavigationMap;
        if (!map.IsValid || NavigationServer2D.MapGetIterationId(map) == 0)
        {
            return true;   // nav 尚未 bake/同步（既往坑：region 同步滞后）——别把尸体全推走
        }

        var p = new Vector2(w.X, w.Y);
        Vector2 closest = NavigationServer2D.MapGetClosestPoint(map, p);
        return closest.DistanceTo(p) <= PassableTolerance;
    }
}
