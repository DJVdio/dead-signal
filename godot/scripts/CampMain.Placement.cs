using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>家具放置的消费层接线</b> —— 用户拍板「<b>为了防止玩家使用改装台、椅子等家具阻挡寻路，放置的时候就不允许贴着大门和围栏</b>」。
///
/// <para>
/// 规则本身在 <see cref="PlacementRules"/>（纯逻辑、零 Godot 依赖、单测覆盖）；<b>本文件只做翻译</b>：
/// 把场上的围栏/大门/门、实心障碍、已放家具翻译成一堆矩形喂给它，再把判定翻译成一行玩家看得懂的中文。
/// </para>
///
/// <para>
/// <b>为什么单独开一个 partial 文件</b>：<c>CampMain.cs</c> 是并发热点（床/改装台/烹饪台三个消费方同时在改），
/// 而本块是**自成一体的新增能力、不改任何既有行**。放这儿 = 谁都不用等谁。
/// </para>
///
/// <para>
/// <b>给接它的人（改装台 / 床 / 烹饪台…）</b>：你的放置点击处只要一行 ——
/// <code>if (!CheckFurniturePlacement(spec, cart)) return;   // 拒绝提示已弹，别退出放置模式</code>
/// 想白送一个绿/红的落位预览，就在进入/退出放置模式时调 <see cref="BeginFurniturePlacement"/> /
/// <see cref="EndFurniturePlacement"/>（预览自己跟鼠标，不用你改 <c>_Process</c>）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>落位预览（绿=放得下/红=放不下）。首次进入放置模式时懒建。</summary>
    private PlacementGhost? _placementGhost;

    /// <summary>
    /// <b>放置校验 + 拒绝时弹一行中文提示</b>。<c>true</c> = 可以放（调用方随后扣料 + 落地）。
    /// <para>
    /// 拒绝时<b>不要退出放置模式</b> —— 让玩家换个地方接着点（沿用 <c>TryPlaceSandbag</c> 的既有范式）。
    /// </para>
    /// </summary>
    private bool CheckFurniturePlacement(in PlaceableSpec spec, Vector2 cart)
    {
        PlacementVerdict verdict = FurniturePlacementVerdict(spec, cart);
        if (verdict != PlacementVerdict.Ok)
        {
            _campToast.Show(PlacementRules.RejectionText(verdict), CampToast.Bad);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 放置判定（<b>不弹提示</b>）—— 落位预览每帧都要问一次，不能每帧弹一条 toast。
    /// </summary>
    private PlacementVerdict FurniturePlacementVerdict(in PlaceableSpec spec, Vector2 cart)
    {
        var bounds = new PlacementRules.Box(
            _mapBounds.Position.X, _mapBounds.Position.Y, _mapBounds.Size.X, _mapBounds.Size.Y);

        // 防线 = 围栏 / 大门 / 门（CampStructureKind 那三类，即 _structures）。缓冲带只沿它们展开，
        // **不沿建筑墙** —— 把工作台靠在自家屋里的墙角是天经地义的事（camp.json 的 props 本来就都在内墙角），
        // 堵不了任何人的路。
        //
        // ⚠️ **被砸没的格照样占位**（Removed 也算进来）：那儿现在是个洞，但玩家**不许拿一张床把它堵上** ——
        // 洞是要靠围栏修复补回来的（FenceUpgradeLogic.PlanRepair），而修墙的人得站得进去。
        // 「用家具堵墙洞」正是 kill box 最省事的一种搭法。
        var defenses = new List<PlacementRules.Box>(_structures.Count);
        foreach (CampStructureInstance s in _structures)
        {
            defenses.Add(new PlacementRules.Box(
                s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y));
        }

        // 实心障碍 = 一切挖了导航洞的东西（建筑墙 / 废墟 / 实心岗位 / 已放好的实心家具）。
        var solids = new List<PlacementRules.Box>(_navHoles.Count);
        foreach (Rect2 h in _navHoles)
        {
            solids.Add(new PlacementRules.Box(h.Position.X, h.Position.Y, h.Size.X, h.Size.Y));
        }

        // 已放好的家具（含沙袋 —— 它不挖导航洞，故不在 _navHoles 里，得单独数一遍）。
        var placed = new List<PlacementRules.Box>(_furniture.Count);
        foreach (FurnitureInstance f in _furniture.Values)
        {
            placed.Add(new PlacementRules.Box(
                f.Rect.Position.X, f.Rect.Position.Y, f.Rect.Size.X, f.Rect.Size.Y));
        }

        return PlacementRules.CanPlace(
            spec, new System.Numerics.Vector2(cart.X, cart.Y), bounds, defenses, solids, placed);
    }

    /// <summary>
    /// 进入放置模式：开一个跟着鼠标走的落位预览（<b>绿 = 放得下，红 = 放不下</b>）。
    /// <para>
    /// 那条 64px 禁建带在地上<b>没有任何痕迹</b>，没有预览玩家只能盲点 —— 所以这不是锦上添花。
    /// 预览自己跟鼠标、自己重画，<b>接它的人不用碰 <c>_Process</c></b>。落位/取消的点击仍归调用方自己处理。
    /// </para>
    /// </summary>
    private void BeginFurniturePlacement(in PlaceableSpec spec)
    {
        if (_placementGhost is null)
        {
            _placementGhost = new PlacementGhost { Name = "PlacementGhost" };
            AddChild(_placementGhost);
        }
        // 预览与真正落位问的是**同一个** FurniturePlacementVerdict ⇒ 画的是绿的就一定放得下，
        // 不会出现"看着能放、点下去被拒"的分裂。
        PlaceableSpec s = spec;
        _placementGhost.Begin(s, cart => FurniturePlacementVerdict(s, cart) == PlacementVerdict.Ok);
    }

    /// <summary>退出放置模式（放下了 / 右键作罢）：收起落位预览。</summary>
    private void EndFurniturePlacement() => _placementGhost?.End();
}
