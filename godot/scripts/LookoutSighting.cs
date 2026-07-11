namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 GoldfingerDiscovery.cs / ExplorationCache.cs / StoryFlags.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「城市之巅瞭望观景台」望远镜瞭望的纯逻辑：望远镜发现点触发 → 解析成 (置 HordeSighted 旗标 + 环境叙事标题+正文)。
// 已瞭望过（HordeSighted 已置）则返回 null 不重复（防重复演出/叙事）。
// 与 GoldfingerDiscovery 同构（复用 DiscoveryResult：BookId 为空＝不给书，本发现点只关乎"看到了什么"+置旗标）。
//
// 语义（主 agent 拍板，见 journal [SPEC]）：
//   · 望远镜瞭望到正北方黑压压上百万尸潮向南移动 → 置 HordeTimeline.SightedFlag("horde_sighted") → 解锁尸潮倒计时 HUD 显示。
//   · 时限本身从第 1 天起隐性计时、不依赖发现；本发现点只解锁"知情/显示"（发现与否时限照走）。
//   · 演出（望远镜瞭望动画，anim-lookout 负责）先播、播完落到本判定弹叙事+置旗标——
//     演出插在 CampMain.OnExplorationDiscovery 本分支之前（见 TestExploration.LookoutTelescopeDiscoveryId 注释）；
//     若演出尚未接入，本分支单独亦是完整可跑路径（踏入望远镜→弹叙事+置旗标，无演出的安全兜底）。
// 叙事为 draft 草稿，最终由用户优化；本类只保证"读值→判定→出叙事+旗标"可跑、可测，不碰 Godot、不写 flag。

/// <summary>
/// 望远镜瞭望发现点解析。<see cref="Resolve"/> 由 CampMain 在探索队踏入望远镜发现点（演出播完）时调用：
/// 返回 <see cref="DiscoveryResult"/> 则 CampMain 负责置 <see cref="HordeSightedFlag"/>、弹叙事面板；
/// 返回 <c>null</c> 表示非本发现点 id 或已瞭望过（旗标已置），什么都不做。
/// </summary>
public static class LookoutSighting
{
    /// <summary>望远镜瞭望发现点 id，须与 <c>TestExploration.LookoutTelescopeDiscoveryId</c> 一致（关内 Area2D 触发时上报）。</summary>
    public const string TelescopeDiscoveryId = "discovery_lookout_telescope";

    /// <summary>
    /// 已瞭望到尸潮的旗标键。置位＝解锁尸潮倒计时 HUD（HUD/时间线只读它）。
    /// canonical 常量归 core-timer（<see cref="HordeTimeline.SightedFlag"/>="horde_sighted"），本处只做转发别名保证两侧字面量对齐。
    /// </summary>
    public const string HordeSightedFlag = HordeTimeline.SightedFlag;

    /// <summary>本发现点不给书（只置旗标+叙事），以空串表示"无书"。</summary>
    public const string NoBookId = "";

    /// <summary>
    /// 解析一次望远镜瞭望。非本发现点 id 或 <see cref="HordeSightedFlag"/> 已置（已瞭望）返回 <c>null</c>。
    /// 本方法**不写** flag（无副作用）；置 flag 由调用方在弹叙事后进行。
    /// </summary>
    public static DiscoveryResult? Resolve(string discoveryId, StoryFlags flags)
    {
        if (discoveryId != TelescopeDiscoveryId)
        {
            return null;
        }
        if (flags != null && flags.Has(HordeSightedFlag))
        {
            return null; // 已瞭望过：不重复演出/叙事
        }
        return new DiscoveryResult(HordeSightedFlag, NoBookId, SightingTitle, SightingNarrative);
    }

    // draft 待用户改 —— 望远镜瞭望发现叙事标题
    private const string SightingTitle = "正北方，天际线尽头";

    // draft 待用户改 —— 望远镜瞭望环境叙事（正北黑压压上百万尸潮向南移动、压迫感、时限暗示；不发明角色对白/性格）
    private const string SightingNarrative =
        "你把眼睛凑上观景台那台老旧的投币望远镜，转动镜身，把焦距慢慢推向正北的天际线。\n\n" +
        "起初以为是地平线上压下来的一带阴云。可那片\"阴云\"在动——不是被风吹动，是它自己在爬、在涌、" +
        "在以一种缓慢却毫不迟疑的姿态，朝这座城市的方向漫过来。\n\n" +
        "你屏住呼吸，再看清些：那不是云。是尸潮。密密麻麻、望不到边、数不清的躯体，" +
        "填满了整条北面的公路与两侧的旷野，黑压压地铺展到目力的尽头。上百万具，也许更多。\n\n" +
        "它们全都朝着同一个方向——南方，你们的方向。\n\n" +
        "望远镜的镜筒里估不出还有多远，但有一件事你已经明白：从此刻起，你们不再有无限的时间。";
}
