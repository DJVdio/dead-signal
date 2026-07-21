using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 CorpseLoot.cs / CorpseDecay.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 尸体可搜刮点的<b>命名与路由</b>（营地与探索关<b>共用</b>）。
///
/// <para><b>一、序号不是装饰，是防蒸发的</b>：容器登记按名覆盖（<see cref="ContainerLoot.Register"/>），
/// 而超市据点一次刷 4 个<b>同名</b>的「据点幸存者」。名字里不带序号 ⇒ 第二具尸体的登记直接顶掉第一具 ⇒
/// 玩家杀了 4 个人只搜得到 1 具的东西，另外 3 把匕首静默蒸发。序号由调用方（<see cref="CorpseYard"/> 的
/// <c>_nextId</c> / 营地层的关内尸体序号）单调递增地喂进来。</para>
///
/// <para><b>二、这个名字同时是探索关里那具尸体的 discoveryId</b>：探索关没有 iso 人形层、也没有点击拾取
/// （见 <c>docs/TODO.md</c> 的「探索关正式化专项」），它唯一的搜刮交互就是<b>踏进一个 Area2D 触发区</b>
/// （物资点/剧情点全走这条）。所以关内的尸体也铺成一个触发区，上报的 id <b>就用这个容器名</b>——
/// 一个字符串同时当"路由键"和"玩家看见的字面"，不必再维护一张 id→显示名的映射表。</para>
///
/// <para><b>三、与 authored 发现点的隔离是结构性的，不是靠黑名单</b>：authored 点的 id 一律 ascii snake_case
/// （<c>cache_</c> / <c>discovery_</c> / <c>narrative_</c> 前缀），而尸体容器名一律含中文
/// <see cref="Marker"/>「的尸体 #」⇒ 两个命名空间不可能相交。于是：
/// <list type="bullet">
/// <item>踏上一具战斗尸体，绝不会误触发某段剧情、也绝不会去置某个剧情 flag；</item>
/// <item>🔴 反过来，「帮众尸体」「树上的哥顿」「祖母的尸体」这些 <b>authored 剧情尸体</b>是<b>发现点</b>，
/// 不是本通道造出来的战斗尸体——它们永远不会被当成战斗尸体去清理/去搜（<c>CorpseDecay</c> 的
/// authored 永不过期是另一道保险）。</item>
/// <item>日后新增任何 authored 点，只要照旧用 ascii id，就自动继续隔离——<b>不需要有人记得回来加一行黑名单</b>。</item>
/// </list></para>
/// </summary>
public static class CorpseNaming
{
    /// <summary>
    /// 尸体容器名的中缀（<b>路由判据</b>）。含中文 ⇒ 与全部 ascii 的 authored 发现点 id 结构性不相交
    /// （<c>LevelCorpseLootTests</c> 扫全表钉死）。
    /// </summary>
    public const string Marker = "的尸体 #";

    /// <summary>
    /// 一具尸体的容器名 ＝「<b>谁</b>的尸体 #<b>序号</b>」（如「据点幸存者的尸体 #1」「丧尸的尸体 #12」）。
    /// <paramref name="seq"/> 必须<b>唯一</b>（见类注释：撞名＝前一具尸体的战利品静默蒸发）。
    /// </summary>
    public static string ContainerName(string who, int seq)
        => $"{(string.IsNullOrWhiteSpace(who) ? "无名者" : who)}{Marker}{Math.Max(1, seq)}";

    /// <summary>探索关尸体使用独立「远征」序号域，避免与营地同名尸体在共享容器表中互相覆盖。</summary>
    public static string ExplorationContainerName(string who, int seq)
        => $"{(string.IsNullOrWhiteSpace(who) ? "无名者" : who)}{Marker}远征{Math.Max(1, seq)}";

    /// <summary>
    /// 这个 id 是一具尸体吗（探索关的发现点上报走同一个字符串，营地层据此把它路由到"搜尸体"而不是
    /// "解析剧情点/缓存点"）。空 id ⇒ false。
    /// </summary>
    public static bool IsCorpseContainer(string? id)
        => !string.IsNullOrEmpty(id) && id!.Contains(Marker, StringComparison.Ordinal);
}
