namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationCache / GoldfingerDiscovery / VillageRescue 一样被 DeadSignal.Combat.Tests 以 Link 编入单测）。
//
// 超市「幸存者骗局」的纯逻辑（[SPEC-B13]，用户原话："超市有一帮幸存者，轻信他们会被骗进密闭小房间背刺围攻"）：
//   · 接触点（据点门口，Proximity 发现区）→ CampMain 弹 ChoicePanel 二选一（对方招呼"进来说话"）。
//   · 轻信跟随 → 被诱入据点内圈小房间（占位墙即"密闭小房间"，走到房间触发语义）→ 背刺围攻：一伙敌对幸存者
//     （Raider 阵营）伏击，首击吃潜行先手 1.5x（复用 NightWatchContest.PreemptiveStrikeMultiplier，Godot 层施加）。
//   · 不轻信 → 他们警告别靠近内圈；外围可正常搜刮。内圈物资被他们占着——踏入内圈闯入点→敌对化开战（可选冲突路线，公平战、无先手惩罚）。
//   · 骗局一次性：接触对话答过（无论轻信/拒绝）即置 ScamResolvedFlag，不再弹接触对话；伏击生成经 AmbushSprungFlag 去重。
//
// 本类只负责「旗标判定 + 草稿文本 + 数值常量」的纯逻辑，可脱 Godot 单测。空间执行（ChoicePanel/生成 Raider/施加首击）
// 落 Godot 层（CampMain + TestExploration）。文本为 draft 草稿，最终由用户优化；分支后果细节 draft 待确认。
public static class SupermarketAmbush
{
    // ——发现点 id（探索关内 Area2D 触发时上报）——
    /// <summary>据点门口接触点：踏入即弹接触对话（若骗局未决出）。</summary>
    public const string ContactDiscoveryId = "discovery_supermarket_contact";
    /// <summary>内圈闯入点：拒绝招呼后踏入据点内圈（去抢被占物资）即敌对化开战（公平战，无先手）。</summary>
    public const string InnerRingDiscoveryId = "discovery_supermarket_inner";

    // ——旗标（持久去重 + 分支状态）——
    /// <summary>骗局已决出：接触对话答过（轻信或拒绝均置），此后不再弹接触对话。</summary>
    public const string ScamResolvedFlag = "supermarket_scam_resolved";
    /// <summary>选了「不轻信」：据点转为占内圈的敌对方，踏入内圈闯入点可开战抢物资。</summary>
    public const string RefusedFlag = "supermarket_scam_refused";
    /// <summary>伏击/闯入战已生成过一次（无论轻信伏击还是拒绝后闯入），去重不重复刷敌。</summary>
    public const string AmbushSprungFlag = "supermarket_ambush_sprung";

    // ——数值（拟定待调）——
    /// <summary>背刺围攻的敌对幸存者数（用户口径 3~4 名）。</summary>
    public const int AmbushRaiderCount = 4;

    // ——ChoiceOption.Value 约定（CampMain cast 回本枚举）——
    public const int ChoiceTrust = 1;   // 轻信跟随（进内圈）
    public const int ChoiceRefuse = 0;  // 不轻信（保持距离）

    /// <summary>是否还该弹接触对话（骗局未决出）。已决出（轻信打过/拒过）→ false，据点门口不再招呼。</summary>
    public static bool ShouldOfferContact(StoryFlags flags)
        => flags == null || !flags.Has(ScamResolvedFlag);

    /// <summary>踏入内圈闯入点时是否应生成敌对幸存者：仅当已拒绝招呼且尚未生成过战斗（占内圈的敌人还在）。</summary>
    public static bool ShouldSpawnInnerRingFight(StoryFlags flags)
        => flags != null && flags.Has(RefusedFlag) && !flags.Has(AmbushSprungFlag);

    // ==== 草稿文本（draft 待用户优化）====

    /// <summary>接触对话标题。</summary>
    public const string ContactTitle = "超市里的人";

    /// <summary>接触对话正文（据点门口一个陪笑脸的人招呼"进来说话"）。</summary>
    public const string ContactPrompt =
        "货架后头闪出个人，摊着两手表示没有恶意，脸上堆着笑：\n" +
        "「别紧张、别紧张——外头乱，我们这儿有墙有顶，还匀得出口吃的。里边说话，都是活人，何必在门口吹冷风？」\n" +
        "他侧身让开，指了指卖场深处那道拉着帘子的门。帘子后头很安静，安静得有点过头。";

    public const string TrustLabel = "跟他进去";
    public const string TrustDescription = "人多好办事，说不定真能匀点补给";
    public const string RefuseLabel = "不进去，保持距离";
    public const string RefuseDescription = "只在外围转转，别往里边凑";

    /// <summary>轻信跟随 → 被诱入密室、背刺发难的旁白（伏击发生前的一屏，CampToast/叙事二选一由 Godot 层定）。</summary>
    public const string AmbushSprungTitle = "帘子后头";
    public const string AmbushSprungNarrative =
        "帘子在身后落下。小房间没有窗，只有一盏晃眼的应急灯。\n" +
        "带路的人闪到一边，笑容不见了。四下里的阴影同时站起来——他们一直就候在这儿。\n" +
        "第一下是从背后来的。";

    /// <summary>不轻信 → 对方悻悻然的警告叙事（外围可搜刮、内圈勿近）。</summary>
    public const string RefuseWarningTitle = "话不投机";
    public const string RefuseWarningNarrative =
        "那人脸上的笑僵了一下，随即冷下来：\n" +
        "「随你。外头那些空架子你爱翻就翻，翻不出什么油水。」\n" +
        "他退回帘子后，临了撂下一句——「就是别往里边来。里边的东西，是我们的。」\n" +
        "帘子后头，不止一个人的脚步声。";

    /// <summary>拒绝后踏入内圈闯入点 → 据点敌对化、公平开战的旁白。</summary>
    public const string InnerRingBreachTitle = "闯进去";
    public const string InnerRingBreachNarrative =
        "警告没拦住你。掀开帘子的一刻，里边囤着的东西一览无余——也一览无余地看见了你。\n" +
        "他们抄起家伙围上来。这回没有笑脸，也没有背后的偷袭，只有明摆着的一场硬仗。";
}
