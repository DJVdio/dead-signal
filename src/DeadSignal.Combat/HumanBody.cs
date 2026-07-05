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
    public const string LeftArm = "左上臂";
    public const string RightArm = "右上臂";
    public const string LeftHand = "左手";
    public const string RightHand = "右手";
    public const string LeftLeg = "左大腿";
    public const string RightLeg = "右大腿";
    public const string LeftFoot = "左脚";
    public const string RightFoot = "右脚";

    /// <summary>返回全套人类部位定义（不可变模板）。数值拟定待调。</summary>
    public static IReadOnlyList<BodyPart> Parts() => new[]
    {
        // 躯干（根）
        new BodyPart { Name = Torso, VolumeWeight = 34, MaxHp = 55, Region = BodyRegion.Torso, Category = BodyPartCategory.Vital, Parent = null },
        // 颈 → 头
        new BodyPart { Name = Neck, VolumeWeight = 2, MaxHp = 12, Region = BodyRegion.Neck, Category = BodyPartCategory.Vital, Parent = Torso },
        new BodyPart { Name = Head, VolumeWeight = 6, MaxHp = 25, Region = BodyRegion.Head, Category = BodyPartCategory.Vital, Parent = Neck },
        // 头部细部位
        new BodyPart { Name = LeftEye, VolumeWeight = 0.4, MaxHp = 3, Region = BodyRegion.Eye, Category = BodyPartCategory.Eye, Parent = Head },
        new BodyPart { Name = RightEye, VolumeWeight = 0.4, MaxHp = 3, Region = BodyRegion.Eye, Category = BodyPartCategory.Eye, Parent = Head },
        new BodyPart { Name = Nose, VolumeWeight = 1, MaxHp = 5, Region = BodyRegion.Face, Category = BodyPartCategory.Minor, Parent = Head },
        new BodyPart { Name = Chin, VolumeWeight = 1.5, MaxHp = 6, Region = BodyRegion.Face, Category = BodyPartCategory.Minor, Parent = Head },
        // 上肢 → 手
        new BodyPart { Name = LeftArm, VolumeWeight = 8, MaxHp = 18, Region = BodyRegion.Arm, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = LeftHand, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Hand, Category = BodyPartCategory.Limb, Parent = LeftArm },
        new BodyPart { Name = RightArm, VolumeWeight = 8, MaxHp = 18, Region = BodyRegion.Arm, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = RightHand, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Hand, Category = BodyPartCategory.Limb, Parent = RightArm },
        // 下肢 → 脚
        new BodyPart { Name = LeftLeg, VolumeWeight = 12, MaxHp = 22, Region = BodyRegion.Leg, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = LeftFoot, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Foot, Category = BodyPartCategory.Limb, Parent = LeftLeg },
        new BodyPart { Name = RightLeg, VolumeWeight = 12, MaxHp = 22, Region = BodyRegion.Leg, Category = BodyPartCategory.Limb, Parent = Torso },
        new BodyPart { Name = RightFoot, VolumeWeight = 3, MaxHp = 10, Region = BodyRegion.Foot, Category = BodyPartCategory.Limb, Parent = RightLeg },
    };

    /// <summary>新建一个满血人类 <see cref="Body"/>。</summary>
    public static Body NewBody() => new(Parts());
}
