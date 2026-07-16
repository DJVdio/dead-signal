namespace DeadSignal.Combat;

/// <summary>
/// 人类细部位表工厂（数据驱动）。HP 与体积命中权重均为**拟定待调**（参考 CDDA/RimWorld 量级）。
/// 树形层级用于切除连带：手挂在手臂下、脚挂在小腿下、小腿挂在大腿下、眼/鼻/下巴挂在头下；
/// 躯干细分为**胸+腹**（[SPEC-B17]）：胸为根，头/双臂挂胸，腹挂胸、双腿挂腹（解剖上头臂经胸廓、腿经骨盆/腹）。
/// 细分为**纯结构性**（[SPEC-B17-修]）：胸/腹沿原躯干通用档、大/小腿沿原腿通用档，性质由 Region/Category 通用规则自然归类，不做手工特化。
/// 归零后果：胸/腹（Vital，沿躯干）=致死；四肢/手/脚=致残；眼=致盲；鼻/下巴=毁容（无系统后果）。
/// </summary>
public static class HumanBody
{
    // 部位名常量，避免字符串散落。
    // 躯干细分（[SPEC-B17]）：胸=树根（头/双臂挂其下），腹挂胸下、双腿挂腹下。二者均沿原躯干档（Region.Torso / Vital）。
    public const string Chest = "胸";
    public const string Abdomen = "腹";
    public const string Head = "头";
    public const string LeftEye = "左眼";
    public const string RightEye = "右眼";
    public const string Nose = "鼻";
    public const string Chin = "下巴";
    public const string LeftEar = "左耳";
    public const string RightEar = "右耳";
    public const string LeftArm = "左手臂";
    public const string RightArm = "右手臂";
    public const string LeftHand = "左手";
    public const string RightHand = "右手";
    // 下肢细分（[SPEC-B17]）：大腿→小腿→脚，逐级挂载。大/小腿均 Region.Leg（沿原腿通用档，骨折/移动同档）。
    public const string LeftLeg = "左大腿";
    public const string RightLeg = "右大腿";
    public const string LeftCalf = "左小腿";
    public const string RightCalf = "右小腿";
    public const string LeftFoot = "左脚";
    public const string RightFoot = "右脚";

    // 五指部位名（左右各拇/食/中/无名/小）。切除按"该手累计操作惩罚"结算，见 Body.RecalculatePenalties。
    public const string LeftThumb = "左手拇指";
    public const string LeftIndex = "左手食指";
    public const string LeftMiddle = "左手中指";
    public const string LeftRing = "左手无名指";
    public const string LeftPinky = "左手小指";
    public const string RightThumb = "右手拇指";
    public const string RightIndex = "右手食指";
    public const string RightMiddle = "右手中指";
    public const string RightRing = "右手无名指";
    public const string RightPinky = "右手小指";

    // 五趾部位名（左右脚各 拇/二/三/四/五趾）。切除按"该脚累计移动惩罚"结算，见 Body.RecalculatePenalties。
    public const string LeftBigToe = "左脚拇趾";
    public const string LeftToe2 = "左脚二趾";
    public const string LeftToe3 = "左脚三趾";
    public const string LeftToe4 = "左脚四趾";
    public const string LeftToe5 = "左脚五趾";
    public const string RightBigToe = "右脚拇趾";
    public const string RightToe2 = "右脚二趾";
    public const string RightToe3 = "右脚三趾";
    public const string RightToe4 = "右脚四趾";
    public const string RightToe5 = "右脚五趾";

    /// <summary>
    /// 返回全套人类部位定义（不可变模板）。<b>结构在代码、数值在 <c>body.json</c></b>：
    /// 部位名/分类（Region/MacroRegion/Category）/父子拓扑写死于此（结构，不可数值化）；
    /// 每部位的体积权重与最大 HP 经 <see cref="Part"/> 从 <see cref="BodyConfig"/> 取（数值，拟定待调）。
    /// </summary>
    public static IReadOnlyList<BodyPart> Parts() => new[]
    {
        // 躯干细分为胸+腹（[SPEC-B17]）：权重 20+16=36（=原躯干，按体表拆胸略大）。HP 胸20/腹16=36（拟定待调，值在 body.json）。
        // 均 Region.Torso / Macro.Torso / Vital（沿原躯干通用档，纯结构性拆分）。
        // ⚠HP 由原躯干 28 上调至 36（Sim 校准）：细分把可流血部位数近乎翻倍（躯干 1→2、腿 2→4），
        //   而失血 = Σ伤口数×流速（Body.TickBleed 按 _bleeding 部位集合计数），故等 HP 下战斗因失血显著变短、
        //   拉低对决基线（匕首vs丧尸 91%→86%）。上调致死池 HP 令时长/胜率回到 91%/79%±3pp（详见 return/journal 的 [DECISION] 记录）。
        // 胸=树根（Parent=null）：头/双臂经胸廓挂胸下；腹经胸下、双腿再挂腹（解剖：腿经骨盆/腹）。
        Part(Chest, BodyRegion.Torso, BodyMacroRegion.Torso, BodyPartCategory.Vital, null),
        Part(Abdomen, BodyRegion.Torso, BodyMacroRegion.Torso, BodyPartCategory.Vital, Chest),
        // 头（颅）→ 挂胸（经颈/胸廓；已移除颈部位：颈无对应装备槽防护）
        Part(Head, BodyRegion.Head, BodyMacroRegion.Head, BodyPartCategory.Vital, Chest),
        // 头部细部位（含左右耳；耳归零仅毁容、无系统后果）
        Part(LeftEye, BodyRegion.Eye, BodyMacroRegion.Head, BodyPartCategory.Eye, Head),
        Part(RightEye, BodyRegion.Eye, BodyMacroRegion.Head, BodyPartCategory.Eye, Head),
        Part(Nose, BodyRegion.Face, BodyMacroRegion.Head, BodyPartCategory.Minor, Head),
        Part(Chin, BodyRegion.Face, BodyMacroRegion.Head, BodyPartCategory.Minor, Head),
        Part(LeftEar, BodyRegion.Ear, BodyMacroRegion.Head, BodyPartCategory.Minor, Head),
        Part(RightEar, BodyRegion.Ear, BodyMacroRegion.Head, BodyPartCategory.Minor, Head),
        // 上肢 → 手（手掌本体占手部大部分权重）→ 五指（低权重，独立部位）
        Part(LeftArm, BodyRegion.Arm, BodyMacroRegion.Arm, BodyPartCategory.Limb, Chest),
        Part(LeftHand, BodyRegion.Hand, BodyMacroRegion.Hand, BodyPartCategory.Limb, LeftArm),
        Part(LeftThumb, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, LeftHand),
        Part(LeftIndex, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, LeftHand),
        Part(LeftMiddle, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, LeftHand),
        Part(LeftRing, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, LeftHand),
        Part(LeftPinky, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, LeftHand),
        Part(RightArm, BodyRegion.Arm, BodyMacroRegion.Arm, BodyPartCategory.Limb, Chest),
        Part(RightHand, BodyRegion.Hand, BodyMacroRegion.Hand, BodyPartCategory.Limb, RightArm),
        Part(RightThumb, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, RightHand),
        Part(RightIndex, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, RightHand),
        Part(RightMiddle, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, RightHand),
        Part(RightRing, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, RightHand),
        Part(RightPinky, BodyRegion.Finger, BodyMacroRegion.Hand, BodyPartCategory.Limb, RightHand),
        // 下肢细分为大腿+小腿（[SPEC-B17]）→ 脚 → 五趾。权重 大腿(7)+小腿(5)=原腿 12（按体表拆大腿略粗）。HP 大腿12/小腿11=23（拟定待调，值在 body.json）。
        // 大/小腿均 Region.Leg / Macro.Leg / Limb（沿原腿通用档，骨折与移动同档）。
        // 小腿 HP 11>匕首上限 10：避免细分后小腿被单击秒切、平添流血伤口（腿非致死池，HP 对胜率不敏感，仅经切除→流血；Sim 验证腿 21→23 胜率≈不变）。
        // 双腿挂腹（解剖：经骨盆/腹）。切除按解剖连带：截大腿 → 连带远端小腿+脚+趾。
        Part(LeftLeg, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, Abdomen),
        Part(LeftCalf, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, LeftLeg),
        Part(LeftFoot, BodyRegion.Foot, BodyMacroRegion.Foot, BodyPartCategory.Limb, LeftCalf),
        Part(LeftBigToe, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, LeftFoot),
        Part(LeftToe2, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, LeftFoot),
        Part(LeftToe3, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, LeftFoot),
        Part(LeftToe4, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, LeftFoot),
        Part(LeftToe5, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, LeftFoot),
        Part(RightLeg, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, Abdomen),
        Part(RightCalf, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, RightLeg),
        Part(RightFoot, BodyRegion.Foot, BodyMacroRegion.Foot, BodyPartCategory.Limb, RightCalf),
        Part(RightBigToe, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, RightFoot),
        Part(RightToe2, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, RightFoot),
        Part(RightToe3, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, RightFoot),
        Part(RightToe4, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, RightFoot),
        Part(RightToe5, BodyRegion.Toe, BodyMacroRegion.Foot, BodyPartCategory.Limb, RightFoot),
    };

    /// <summary>
    /// 造一个部位：<b>结构</b>（名/分类/父子拓扑）由参数写死，<b>两个可调数字</b>（体积权重/最大 HP）
    /// 从 <c>body.json</c>（<see cref="BodyConfig"/>）按部位名取。数值真源从 C# 常量搬到了 json。
    /// </summary>
    private static BodyPart Part(string name, BodyRegion region, BodyMacroRegion macro, BodyPartCategory category, string? parent)
    {
        BodyPartStats stats = CombatCatalog.Section<BodyConfig>().Part(name);
        return new BodyPart
        {
            Name = name,
            VolumeWeight = stats.VolumeWeight,
            MaxHp = stats.MaxHp,
            Region = region,
            MacroRegion = macro,
            Category = category,
            Parent = parent,
        };
    }

    /// <summary>新建一个满血人类 <see cref="Body"/>。</summary>
    public static Body NewBody() => new(Parts());

    /// <summary>
    /// 新建一个丧尸 <see cref="Body"/>：与人类同一套部位树，但**失血流速只有 1/3**
    /// （<see cref="BleedModel.ZombieBleedRateMultiplier"/>，用户口径「丧尸没那么容易流血致死」）。
    /// 丧尸的 <see cref="DuelFighter.BodyFactory"/> / Godot 的 <c>Zombie</c> 一律走这个工厂。
    /// </summary>
    public static Body NewZombieBody()
    {
        var body = new Body(Parts());
        body.BleedRateMultiplier = BleedModel.ZombieBleedRateMultiplier;
        return body;
    }

    /// <summary>
    /// 展开给定根部位及其全部后代的部位名集合（含根自身）。供护甲覆盖表达用：
    /// 如 <see cref="SubtreeNames"/>("左手") = { 左手 + 左手五指 }，让手套连带护住手指。
    /// </summary>
    public static IReadOnlySet<string> SubtreeNames(params string[] roots)
    {
        var byParent = Parts().ToLookup(p => p.Parent);
        var result = new HashSet<string>();
        var stack = new Stack<string>(roots);
        while (stack.Count > 0)
        {
            string name = stack.Pop();
            if (!result.Add(name))
            {
                continue;
            }
            foreach (BodyPart child in byParent[name])
            {
                stack.Push(child.Name);
            }
        }
        return result;
    }
}
