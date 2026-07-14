using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationCache.cs / GoldfingerDiscovery.cs / StoryFlags.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 调查点「大中小三级」规模 + 探索「完成度」聚合的纯逻辑（用户 [SPEC-B11-补] 拍板）：
//   · 规模三级 SizeTier{Small/Medium/Large}：用户口径"小 1~2 天 / 中 3~5 天 / 大 5 天以上"，
//     具体各点定级为数据（落在 WorldMapPanel.Destinations 的 Tier 字段，拟定待调），本类只出枚举 + 文案。
//   · 完成度：给定目的地，聚合其**登记的点位集**（物资搜刮点 + 剧情尸体发现点）对应的一次性 flag，
//     数已置(已搜/已发现) X / 登记总数 Y。跨访持久语义来自 StoryFlags（flag 一旦置即永久，二访 Resolve 返回 null=空搜），
//     故 (done,total) 天然反映"该点还剩几处没调查"。地图目的地悬停/标签据此显示"已调查 X/Y 点"。
//
// 计入完成度的点位（脱 Godot、按名+id 静态登记）：
//   · 物资搜刮点：ExplorationCache.CacheIdsFor(name) 的全部（含守林人小屋两处）；
//   · 剧情尸体发现点：金手指帮根据地 帮众尸体(恒在) + 克莉丝汀本人尸体(仅复仇线)；守林人小屋 哥顿上吊尸。
// **不计入**：城市之巅瞭望观景台的望远镜、广播台的发出设备——那是主线触发点（置 HordeSighted / 推进 RadioMainline 状态，
//   非"搜刮/发现"语义），刻意排除在"已调查 X/Y 物资/遗体点"之外；若日后要把主线点纳入完成度，另议（遗留）。

/// <summary>调查点规模三级（用户拍板：小 1~2 天 / 中 3~5 天 / 大 5 天以上）。具体各点定级为数据（拟定待调）。</summary>
public enum SizeTier
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// 调查点**危险度**三级（用户为每个新点位口述的那一档：低危 / 中危 / 高危）。具体各点定级为数据（落在
/// <c>WorldMapPanel.Destinations</c> 的 Danger 字段，拟定待调）。
///
/// <para>
/// 🔴 <b>危险度和「规模」是正交的两维，绝不能混为一谈</b>：规模说的是<b>这趟要花几天</b>（内容量），
/// 危险度说的是<b>这趟要拿多少伤病和人命去换</b>。废弃医院正是二者背离的样板——
/// <b>大地图 + 中危</b>：它有全游戏最多的丧尸（14 只），却<b>不是</b>高危，因为它是一栋**建筑**：
/// 有多个入口、每道分区有多个门洞、还有能关上的防火门 ⇒ <b>你可以绕、可以关门、可以选择不打</b>。
/// </para>
///
/// <para>
/// 危险度<b>不是"敌人数量"的同义词</b>。<c>docs/research/2026-07-14-combat-cost.md</c> 已经钉死：
/// <b>胜率不是成本</b>，而连场战斗<b>不能拿胜率相乘去想</b>（单场 68% 胜率、不治疗连打，能撑过第 3 个的只剩 3.5%）。
/// 所以一个点危不危险，取决于<b>它逼不逼你打</b>——逼你打光弹药、逼你带着骨折回家、逼你连打第三场的，才是高危。
/// 塞满敌人但**给了你绕过去的路**的地图，是中危。
/// </para>
/// </summary>
public enum DangerTier
{
    /// <summary>低危：遭遇稀疏，正常操作下不该有人受伤。</summary>
    Low,

    /// <summary>中危：会挨打，但**有得选**——绕路/关门/避战是可行解，不是运气。</summary>
    Medium,

    /// <summary>高危：躲不开的硬仗，去之前要算清楚拿什么去换。</summary>
    High,
}

/// <summary>调查点规模三级文案 + 探索完成度聚合（纯逻辑，可脱 Godot 单测）。</summary>
public static class ExplorationProgress
{
    // 脱 Godot 副本（与 WorldMapPanel 常量一致，务必同步）。
    /// <summary>金手指帮根据地目的地名，须与 <c>WorldMapPanel.GoldfingerBaseName</c> 一致。</summary>
    public const string GoldfingerBaseName = "金手指帮根据地";
    /// <summary>守林人小屋内部路由键（＝守望者森林小屋），须与 <c>WorldMapPanel.WatchersCabinName</c> 一致；显示名正名为「守林人小屋」。</summary>
    public const string WatchersCabinName = "守望者森林小屋";

    /// <summary>
    /// 规模标签（含预计探索天数，文案克制）：小·约1-2天 / 中·约3-5天 / 大·约5天+。
    /// 单一事实源在 <see cref="DisplayNames"/>。
    /// </summary>
    public static string TierLabel(SizeTier tier) => DisplayNames.Of(tier);

    /// <summary>危险度标签（低危 / 中危 / 高危）。单一事实源在 <see cref="DisplayNames"/>。</summary>
    public static string DangerLabel(DangerTier tier) => DisplayNames.Of(tier);

    /// <summary>
    /// 某目的地登记的"点位完成 flag"清单（物资搜刮点 + 剧情尸体发现点）。随复仇线上下文条件增删
    /// （克莉丝汀本人尸体仅 <paramref name="christineLeftForRevenge"/> 时才是一处登记点）。
    /// 非调查目的地返回空清单（total=0，标签不显示完成度）。
    /// </summary>
    public static IReadOnlyList<string> PointFlagsFor(string destinationName, bool christineLeftForRevenge)
    {
        var flags = new List<string>();

        // 剧情尸体发现点（found_* flag，跨访持久去重）。
        if (destinationName == GoldfingerBaseName)
        {
            flags.Add(GoldfingerDiscovery.GangMemberCorpseFlag); // 帮众尸体：各分支恒在
            if (christineLeftForRevenge)
                flags.Add(GoldfingerDiscovery.ChristineCorpseFlag); // 克莉丝汀本人尸体：仅复仇线登记
        }
        else if (destinationName == WatchersCabinName)
        {
            flags.Add(GoldfingerDiscovery.GordonHangedFlag); // 哥顿上吊尸（后院树上）
        }

        // 物资搜刮点（searched_* flag，跨访持久去重）。
        foreach (string cacheId in ExplorationCache.CacheIdsFor(destinationName))
        {
            string f = ExplorationCache.FlagForCache(cacheId);
            if (!string.IsNullOrEmpty(f))
                flags.Add(f);
        }

        return flags;
    }

    /// <summary>
    /// 某目的地探索完成度：(已调查点数, 登记点位总数)。数据源＝<see cref="PointFlagsFor"/> 的 flag 集 vs <paramref name="flags"/> 已置状态。
    /// total=0 表示该目的地无登记调查点（如纯背景点/主线触发点），调用方据此不显示完成度。
    /// </summary>
    public static (int done, int total) Completion(string destinationName, StoryFlags flags, bool christineLeftForRevenge)
    {
        var points = PointFlagsFor(destinationName, christineLeftForRevenge);
        int done = 0;
        foreach (string f in points)
        {
            if (flags != null && flags.Has(f))
                done++;
        }
        return (done, points.Count);
    }
}
