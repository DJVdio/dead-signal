using System;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// **面向玩家的枚举显示名总表**（纯 C#，无 Godot 依赖 ⇒ 可单测）。
///
/// <para>
/// <b>为什么要有这个文件</b>：项目铁律是「面向用户的一切不出现类名/英文 id/引擎术语」。
/// 枚举的英文标识符（<c>DayPhase.DayPrep</c>、<c>CampStructureKind.Fence</c>）是**代码内部的名字**，
/// 一旦被 <c>ToString()</c> 或字符串插值直接喂进 UI，玩家就会看到 <c>[DayPrep]</c>、<c>拆完 Fence</c> 这种代码腔。
/// 历史上这类映射散落在各面板的 private switch 里（WeaponClass/WeaponPart 一度在两个文件里各写一份），
/// 且清一色带 <c>_ =&gt; x.ToString()</c> 兜底——**新增一个枚举值忘了加中文，就直接漏英文给玩家**。
/// </para>
///
/// <para>
/// <b>规矩</b>：
/// <list type="number">
/// <item>任何要显示给玩家的枚举，中文名**只在本文件**定义；各面板/HUD 一律调 <see cref="Of(DayPhase)"/> 这组重载
///       （旧的 <c>Label</c>/<c>TierLabel</c> 入口保留，但内部转发到这里，保证单一事实源）。</item>
/// <item>每个 switch **逐值穷举**，兜底一律给 <see cref="Unknown"/> 而**不是** <c>ToString()</c>——
///       即便哪天漏了，玩家看到的也是「未知」而非英文枚举名（fail-safe，不把代码腔泄给玩家）。</item>
/// <item>漏了会被测试抓住：<c>EnumDisplayNameTests</c> 反射遍历下方注册表里每个枚举的**每一个值**，
///       断言其显示名非空、不含 ASCII 字母、且不等于 <see cref="Unknown"/>。**新增枚举值忘了配中文 ⇒ 直接红**。</item>
/// </list>
/// </para>
///
/// <para>
/// <b>文风</b>：HUD/面板是功能性文本，取**朴实清楚**，不硬塞黑色幽默（幽默留给物品描述/剧情文案那一面）。
/// </para>
/// </summary>
public static class DisplayNames
{
    /// <summary>
    /// 兜底显示名。**绝不返回英文枚举名**——宁可显示「未知」也不把代码腔泄给玩家。
    /// 正常情况下永远不会被用到（测试保证每个枚举值都有中文名），它只是最后一道 fail-safe。
    /// </summary>
    public const string Unknown = "未知";

    // ———————————————————————————— 昼夜相位 ————————————————————————————

    /// <summary>
    /// 昼夜相位中文名（HUD 左上角状态行）。一天 8 相位顺序流转，语义见设计文档 §4：
    /// 白天筹备 → 出发路上 → 外出探索 → 返回营地 → 黄昏聚餐 → 夜间部署 → 夜间行动 → 清晨聚餐 →（回到）白天筹备。
    /// </summary>
    public static string Of(DayPhase phase) => phase switch
    {
        DayPhase.DawnMeal => "清晨聚餐",   // 全员聚集吃早饭（模态）
        DayPhase.DayPrep => "白天筹备",    // 新的一天，编探索队
        DayPhase.DayTravel => "出发路上",  // 8x 快进赶路
        DayPhase.DayExplore => "外出探索", // 探索关卡实时进行
        DayPhase.DayReturn => "返回营地",  // 卸载关卡、带人回营
        DayPhase.DuskMeal => "黄昏聚餐",   // 全员聚集吃晚饭（模态）
        DayPhase.NightPrep => "夜间部署",  // 排班站岗 + 指派读书
        DayPhase.NightAct => "夜间行动",   // 守夜/生产，可能袭营
        _ => Unknown,
    };

    // ———————————————————————————— 营地结构 ————————————————————————————

    /// <summary>营地可破坏结构的种类中文名。</summary>
    public static string Of(CampStructureKind kind) => kind switch
    {
        CampStructureKind.Fence => "围栏",
        CampStructureKind.Gate => "大门",
        CampStructureKind.Door => "门",
        _ => Unknown,
    };

    /// <summary>
    /// 一处结构报给玩家时的名字：有专名（如「厨房门」）就用专名，没有就退回种类中文名（「围栏」/「大门」/「门」）。
    /// 拆解播报等处用它，**杜绝退回 <c>kind.ToString()</c> 打出「拆完 Fence」**。
    /// </summary>
    public static string StructureName(string? properName, CampStructureKind kind)
        => string.IsNullOrEmpty(properName) ? Of(kind) : properName!;

    // ———————————————————————————— 装备与护甲 ————————————————————————————

    /// <summary>人物装备槽中文名（角色面板）。四肢按左右分槽（断肢会让该侧槽失效）。</summary>
    public static string Of(EquipSlot slot) => slot switch
    {
        EquipSlot.Head => "头部",
        EquipSlot.Eyes => "眼镜",
        EquipSlot.Face => "面部",
        EquipSlot.SkinLayer => "贴身层",
        EquipSlot.OuterLayer => "外套层",
        EquipSlot.PlateLayer => "装甲层",
        EquipSlot.LeftHand => "左手",
        EquipSlot.RightHand => "右手",
        EquipSlot.Pants => "裤子",
        EquipSlot.LeftFoot => "左脚",
        EquipSlot.RightFoot => "右脚",
        _ => Unknown,
    };

    /// <summary>护甲层中文名（由外到内：装甲层 → 外套层 → 贴身层）。</summary>
    public static string Of(ArmorSlot slot) => slot switch
    {
        ArmorSlot.Plate => "装甲层",
        ArmorSlot.Outer => "外套层",
        ArmorSlot.Skin => "贴身层",
        _ => Unknown,
    };

    /// <summary>狗的穿戴槽中文名（布鲁斯只有两槽，远少于人类 11 槽）。</summary>
    public static string Of(DogEquipSlot slot) => slot switch
    {
        DogEquipSlot.Body => "躯干",
        DogEquipSlot.Head => "头部",
        _ => Unknown,
    };

    // ———————————————————————————— 武器与改装 ————————————————————————————

    /// <summary>武器大类中文名（改装面板按大类适用）。</summary>
    public static string Of(WeaponClass weaponClass) => weaponClass switch
    {
        WeaponClass.Firearm => "枪械",
        WeaponClass.Blade => "近战锐器",
        WeaponClass.Blunt => "近战钝器",
        _ => Unknown,
    };

    /// <summary>可改装的武器部位中文名。</summary>
    public static string Of(WeaponPart part) => part switch
    {
        WeaponPart.Stock => "枪托",
        WeaponPart.Barrel => "枪管",
        WeaponPart.Muzzle => "枪口",
        WeaponPart.Blade => "刃",
        WeaponPart.Handle => "剑柄",
        WeaponPart.Grip => "缠手",
        // [wiki→代码同步] 棍棒的两个槽：上部（铁丝缠的那段杆）/ 顶端（钉子砸的那圈钉）——
        //   用户在 wiki 上把它们拆成两个部位（「不占用同一个槽，可以一起安装」），显示名照 wiki 的写法。
        WeaponPart.Shaft => "棍棒上部",
        WeaponPart.ClubHead => "棍棒顶端",
        // [T69] 弓弩专属部位。LimbWrap 与 Grip 同显示名"缠手"——两者从不同现于一把武器，无碍（见 WeaponPart.LimbWrap）。
        WeaponPart.Bow => "弓",
        WeaponPart.String => "弦",
        WeaponPart.CrossbowBody => "弩身",
        WeaponPart.LimbWrap => "缠手",
        _ => Unknown,
    };

    /// <summary>枪械近战型态中文名（改装后"贴脸抡枪托"打出来的东西；一把枪至多一种，[T68] 共四种）。</summary>
    public static string Of(MeleeForm form) => form switch
    {
        MeleeForm.Claw => "利爪型",
        MeleeForm.Trauma => "创伤型",
        MeleeForm.Bayonet => "刺刀型",
        MeleeForm.Blade => "锋刃型",   // [T68] 短枪（手枪/冲锋枪）专属
        _ => Unknown,
    };

    // ———————————————————————————— 制作 ————————————————————————————

    /// <summary>工作台工具插槽中文名。</summary>
    public static string Of(ToolSlot slot) => slot switch
    {
        ToolSlot.Calipers => "卡尺",
        ToolSlot.SawBlade => "锯片",
        ToolSlot.Beaker => "烧杯",
        _ => Unknown,
    };

    /// <summary>
    /// [批次21·T14] 烹饪台炊具槽中文名（面板上写「锅：已装」「烤架：空」）。
    /// <para>⚠️ 这里<b>只给名字</b>——它省几点热量**不许**写进显示名（那是玩家要自己试出来的，见 <c>CookingLogic</c> 类注）。</para>
    /// </summary>
    public static string Of(CookwareSlot slot) => slot switch
    {
        CookwareSlot.Pot => "锅",
        CookwareSlot.Grill => "烤架",
        _ => Unknown,
    };

    // ———————————————————————————— 状态刻度 ————————————————————————————

    /// <summary>饥饿刻度中文名（0 饿死 ~ 6 吃撑）。</summary>
    public static string Of(HungerLevel level) => level switch
    {
        HungerLevel.Starved => "饿死",
        HungerLevel.Malnourished => "营养不良",
        HungerLevel.Ravenous => "极度饥饿",
        HungerLevel.Hungry => "饥饿",
        HungerLevel.Peckish => "有点饿",
        HungerLevel.Sated => "正常",
        HungerLevel.Stuffed => "吃撑",
        _ => Unknown,
    };

    /// <summary>负重档位中文名（30/50/80kg 三条线）。</summary>
    public static string Of(LoadoutTier tier) => tier switch
    {
        LoadoutTier.Unencumbered => "轻装",
        LoadoutTier.Encumbered => "负重",
        LoadoutTier.Strained => "重负",
        LoadoutTier.Overloaded => "超载",
        _ => Unknown,
    };

    /// <summary>调查点规模中文名（含预计探索天数，世界地图用）。</summary>
    public static string Of(SizeTier tier) => tier switch
    {
        SizeTier.Small => "小",
        SizeTier.Medium => "中",
        SizeTier.Large => "大",
        _ => Unknown,
    };

    /// <summary>
    /// 调查点危险度中文名（世界地图用）。<b>只给一个词，不给数字</b>——玩家出发前不该知道那里到底有几只丧尸，
    /// 他只该知道"这地方危不危险"。把敌人数量摊在地图上，等于把侦查这件事从游戏里删掉。
    /// </summary>
    public static string Of(DangerTier tier) => tier switch
    {
        DangerTier.Low => "低危",
        DangerTier.Medium => "中危",
        DangerTier.High => "高危",
        _ => Unknown,
    };

    /// <summary>夜袭威胁规模的模糊情报文案（守卫目击给的是大概，不是精确数）。</summary>
    public static string Of(NightRaidLogic.ThreatBand band) => band switch
    {
        NightRaidLogic.ThreatBand.Small => "零星几个黑影摸近营地",
        NightRaidLogic.ThreatBand.Medium => "一小群袭击者正逼近",
        NightRaidLogic.ThreatBand.Large => "大批袭击者压上来了",
        _ => Unknown,
    };

    /// <summary>[批次21] 医疗物资的用途类别（医务面板分组/提示用）。</summary>
    public static string Of(MedicalUseKind kind) => kind switch
    {
        MedicalUseKind.InfectionCourse => "感染疗程用药",
        MedicalUseKind.DiseaseDose => "疾病用药",
        MedicalUseKind.RecoveryTonic => "恢复补剂",
        MedicalUseKind.SurgerySupply => "手术耗材",
        _ => Unknown,
    };

    /// <summary>
    /// [批次21] 「为什么不能给他用这个」——按钮置灰时把原因直接说给玩家听。
    /// 照 <c>SiteActionPopup</c> 的规矩：**不藏选项，灰掉并说明为什么**（玩家该知道是"没药了"还是"他压根没这病"）。
    /// </summary>
    public static string Of(MedicalRefusal refusal) => refusal switch
    {
        MedicalRefusal.None => "可以使用",
        MedicalRefusal.OutOfStock => "没有存货了",
        MedicalRefusal.NoTarget => "他没有对症的伤病，用了也是白扔",
        MedicalRefusal.AlreadyActive => "已经在用了，再来一份没有意义",
        MedicalRefusal.PatientDead => "人已经没了",
        MedicalRefusal.NotMedical => "这不是能直接用在人身上的医疗物资",
        _ => Unknown,
    };

    /// <summary>[批次21·impl-bedrest] 幸存者当前在干什么（角色卡片/悬停提示直接显示这几个字）。</summary>
    public static string Of(PawnRole role) => role switch
    {
        PawnRole.Idle => "待命",
        PawnRole.Expedition => "外出探索",
        PawnRole.Sleeping => "睡觉",
        PawnRole.Guard => "站岗",
        PawnRole.Producing => "生产",
        PawnRole.Reading => "读书",
        PawnRole.Bedrest => "卧床养病",
        _ => Unknown,
    };

    /// <summary>[批次21·impl-bedrest] 休养质量（养病提示里区分"睡床"和"打地铺"——床是要造的，地铺不吃睡床加成）。</summary>
    public static string Of(RestQuality quality) => quality switch
    {
        RestQuality.None => "没在休养",
        RestQuality.Floor => "打地铺",
        RestQuality.Bed => "睡床",
        _ => Unknown,
    };
}
