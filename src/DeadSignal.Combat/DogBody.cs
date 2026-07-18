namespace DeadSignal.Combat;

/// <summary>
/// 布鲁斯的专用躯体模板。
///
/// <para>
/// 这是一个真正的犬类身体，而不是把人形部位表借给狗：没有手臂、手掌、手指或人类面部细部位，
/// 只有胸腹、头，以及四条带脚的腿。胸/腹/头/腿的锚名沿用 <see cref="HumanBody"/>，使现有狗衣
/// 覆盖与四肢骨折/断肢消费无需另造一套字符串映射。
/// </para>
///
/// <para>
/// 部位数值是本批临时 authored 档（胸 24、腹 18、头 10、腿 8、脚 4；体积权重按犬体比例），
/// 规则形态先锁；日后数值可直接迁入 Wiki/body 配置，不改变拓扑或消费 API。
/// </para>
/// </summary>
public static class DogBody
{
    public static IReadOnlyList<BodyPart> Parts() => new[]
    {
        Part(HumanBody.Chest, BodyRegion.Torso, BodyMacroRegion.Torso, BodyPartCategory.Vital, null, 40, 24),
        Part(HumanBody.Abdomen, BodyRegion.Torso, BodyMacroRegion.Torso, BodyPartCategory.Vital, HumanBody.Chest, 30, 18),
        Part(HumanBody.Head, BodyRegion.Head, BodyMacroRegion.Head, BodyPartCategory.Vital, HumanBody.Chest, 20, 10),

        Part(HumanBody.LeftLeg, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, HumanBody.Abdomen, 9, 8),
        Part(HumanBody.LeftFoot, BodyRegion.Foot, BodyMacroRegion.Foot, BodyPartCategory.Limb, HumanBody.LeftLeg, 4, 4),
        Part(HumanBody.RightLeg, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, HumanBody.Abdomen, 9, 8),
        Part(HumanBody.RightFoot, BodyRegion.Foot, BodyMacroRegion.Foot, BodyPartCategory.Limb, HumanBody.RightLeg, 4, 4),
        Part(HumanBody.LeftCalf, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, HumanBody.Abdomen, 9, 8),
        Part(HumanBody.LeftCalf + "·脚", BodyRegion.Foot, BodyMacroRegion.Foot, BodyPartCategory.Limb, HumanBody.LeftCalf, 4, 4),
        Part(HumanBody.RightCalf, BodyRegion.Leg, BodyMacroRegion.Leg, BodyPartCategory.Limb, HumanBody.Abdomen, 9, 8),
        Part(HumanBody.RightCalf + "·脚", BodyRegion.Foot, BodyMacroRegion.Foot, BodyPartCategory.Limb, HumanBody.RightCalf, 4, 4),
    };

    /// <summary>新建一具满血狗身。储血上限沿用引擎默认，狗的低伤/高闪避仍由 Dog 消费层负责。</summary>
    public static Body NewBody() => new(Parts());

    private static BodyPart Part(
        string name,
        BodyRegion region,
        BodyMacroRegion macro,
        BodyPartCategory category,
        string? parent,
        double volumeWeight,
        double maxHp)
        => new()
        {
            Name = name,
            Region = region,
            MacroRegion = macro,
            Category = category,
            Parent = parent,
            VolumeWeight = volumeWeight,
            MaxHp = maxHp,
        };
}
