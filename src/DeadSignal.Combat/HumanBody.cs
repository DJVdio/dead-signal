namespace DeadSignal.Combat;

/// <summary>
/// 人类细部位表工厂（数据驱动）。HP 与体积命中权重均为**拟定待调**（参考 CDDA/RimWorld 量级）。
/// 树形层级用于切除连带：手挂在上臂下、脚挂在大腿下、眼/鼻/下巴挂在头下。
/// 归零后果：头/颈/躯干=致死；四肢/手/脚=致残；眼=致盲；鼻/下巴=毁容（无系统后果）。
/// </summary>
public static class HumanBody
{
    // 部位名常量，避免字符串散落。
    public const string Torso = "躯干";
    public const string Neck = "颈";
    public const string Head = "头";
    public const string LeftEye = "左眼";
    public const string RightEye = "右眼";
    public const string Nose = "鼻";
    public const string Chin = "下巴";
    public const string LeftEar = "左耳";
    public const string RightEar = "右耳";
    public const string LeftArm = "左上臂";
    public const string RightArm = "右上臂";
    public const string LeftHand = "左手";
    public const string RightHand = "右手";
    public const string LeftLeg = "左大腿";
    public const string RightLeg = "右大腿";
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

    /// <summary>返回全套人类部位定义（不可变模板）。数值拟定待调。</summary>
    public static IReadOnlyList<BodyPart> Parts() => new[]
    {
        // 躯干（根）
        new BodyPart { Name = Torso, VolumeWeight = 34, MaxHp = 55, Region = BodyRegion.Torso, MacroRegion = BodyMacroRegion.Torso, Category = BodyPartCategory.Vital, Parent = null },
        // 颈 → 头
        new BodyPart { Name = Neck, VolumeWeight = 2, MaxHp = 12, Region = BodyRegion.Neck, MacroRegion = BodyMacroRegion.Neck, Category = BodyPartCategory.Vital, Parent = Torso },
        new BodyPart { Name = Head, VolumeWeight = 6, MaxHp = 25, Region = BodyRegion.Head, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Vital, Parent = Neck },
        // 头部细部位（含左右耳；耳归零仅毁容、无系统后果）
        new BodyPart { Name = LeftEye, VolumeWeight = 0.4, MaxHp = 3, Region = BodyRegion.Eye, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Eye, Parent = Head },
        new BodyPart { Name = RightEye, VolumeWeight = 0.4, MaxHp = 3, Region = BodyRegion.Eye, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Eye, Parent = Head },
        new BodyPart { Name = Nose, VolumeWeight = 1, MaxHp = 5, Region = BodyRegion.Face, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Minor, Parent = Head },
        new BodyPart { Name = Chin, VolumeWeight = 1.5, MaxHp = 6, Region = BodyRegion.Face, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Minor, Parent = Head },
        new BodyPart { Name = LeftEar, VolumeWeight = 0.5, MaxHp = 4, Region = BodyRegion.Ear, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Minor, Parent = Head },
        new BodyPart { Name = RightEar, VolumeWeight = 0.5, MaxHp = 4, Region = BodyRegion.Ear, MacroRegion = BodyMacroRegion.Head, Category = BodyPartCategory.Minor, Parent = Head },
        // 上肢 → 手（手掌本体占手部大部分权重）→ 五指（低 HP 低权重，独立部位）
        new BodyPart { Name = LeftArm, VolumeWeight = 8, MaxHp = 18, Region = BodyRegion.Arm, MacroRegion = BodyMacroRegion.Arm, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = LeftHand, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Hand, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftArm },
        new BodyPart { Name = LeftThumb, VolumeWeight = 0.35, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftHand },
        new BodyPart { Name = LeftIndex, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftHand },
        new BodyPart { Name = LeftMiddle, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftHand },
        new BodyPart { Name = LeftRing, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftHand },
        new BodyPart { Name = LeftPinky, VolumeWeight = 0.25, MaxHp = 2, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftHand },
        new BodyPart { Name = RightArm, VolumeWeight = 8, MaxHp = 18, Region = BodyRegion.Arm, MacroRegion = BodyMacroRegion.Arm, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = RightHand, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Hand, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightArm },
        new BodyPart { Name = RightThumb, VolumeWeight = 0.35, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightHand },
        new BodyPart { Name = RightIndex, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightHand },
        new BodyPart { Name = RightMiddle, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightHand },
        new BodyPart { Name = RightRing, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightHand },
        new BodyPart { Name = RightPinky, VolumeWeight = 0.25, MaxHp = 2, Region = BodyRegion.Finger, MacroRegion = BodyMacroRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightHand },
        // 下肢 → 脚（脚掌本体占脚部大部分权重）→ 五趾（低 HP 低权重，独立部位）
        new BodyPart { Name = LeftLeg, VolumeWeight = 12, MaxHp = 22, Region = BodyRegion.Leg, MacroRegion = BodyMacroRegion.Leg, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = LeftFoot, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Foot, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftLeg },
        new BodyPart { Name = LeftBigToe, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftFoot },
        new BodyPart { Name = LeftToe2, VolumeWeight = 0.2, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftFoot },
        new BodyPart { Name = LeftToe3, VolumeWeight = 0.2, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftFoot },
        new BodyPart { Name = LeftToe4, VolumeWeight = 0.2, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftFoot },
        new BodyPart { Name = LeftToe5, VolumeWeight = 0.15, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftFoot },
        new BodyPart { Name = RightLeg, VolumeWeight = 12, MaxHp = 22, Region = BodyRegion.Leg, MacroRegion = BodyMacroRegion.Leg, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = RightFoot, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Foot, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightLeg },
        new BodyPart { Name = RightBigToe, VolumeWeight = 0.3, MaxHp = 3, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightFoot },
        new BodyPart { Name = RightToe2, VolumeWeight = 0.2, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightFoot },
        new BodyPart { Name = RightToe3, VolumeWeight = 0.2, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightFoot },
        new BodyPart { Name = RightToe4, VolumeWeight = 0.2, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightFoot },
        new BodyPart { Name = RightToe5, VolumeWeight = 0.15, MaxHp = 2, Region = BodyRegion.Toe, MacroRegion = BodyMacroRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightFoot },
    };

    /// <summary>新建一个满血人类 <see cref="Body"/>。</summary>
    public static Body NewBody() => new(Parts());
}
