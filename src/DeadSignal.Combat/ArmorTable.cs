namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威护甲数据源。原先 Godot(CombatData) 与 Sim(Program/DuelReport) 各自维护护甲工厂，
/// 此处统一收拢。SurvivorArmor / ZombieHide 采 Godot 现行值；布衣/皮甲/板甲为 Sim 参数化甲组
/// （Program 与 DuelReport 原本重复定义、数值一致，合并至此，输出不变）。
/// 防御值均为原型期<b>拟定待调</b>（锐防约 2×钝防，板甲比例更高）。
/// 传入 Resolve 前仍会经 <see cref="CombatResolver.OrderOuterToInner"/> 归一层序。
/// </summary>
public static class ArmorTable
{
    // ---- Godot 消费层套装 ----

    /// <summary>幸存者：皮夹克(外) + 贴身布衣(内) 两层。</summary>
    public static IReadOnlyList<ArmorLayer> SurvivorArmor() => new[]
    {
        new ArmorLayer { Name = "皮夹克", SharpDefense = 6, BluntDefense = 3, Weight = 3, Slot = ArmorSlot.Outer },   // 拟定待调
        new ArmorLayer { Name = "贴身布衣", SharpDefense = 2, BluntDefense = 1, Weight = 1, Slot = ArmorSlot.Skin }, // 拟定待调
    };

    /// <summary>丧尸：一层腐烂硬皮（对钝器略韧）。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => new[]
    {
        new ArmorLayer { Name = "腐皮", SharpDefense = 1.5, BluntDefense = 3, Weight = 0, Slot = ArmorSlot.Skin }, // 拟定待调
    };

    // ---- Sim 参数化甲层（单层工厂，供聚合/对决按层组合）----

    /// <summary>布衣（贴身层）。</summary>
    public static ArmorLayer Cloth() => new() { Name = "布衣", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2, Weight = 1 };    // 拟定待调

    /// <summary>皮甲（外套层）。</summary>
    public static ArmorLayer Leather() => new() { Name = "皮甲", Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 4 }; // 拟定待调

    /// <summary>板甲（装甲层）。</summary>
    public static ArmorLayer Plate() => new() { Name = "板甲", Slot = ArmorSlot.Plate, SharpDefense = 34, BluntDefense = 11, Weight = 12 }; // 拟定待调

    /// <summary>粗布外套（外套层）：照皮甲但更粗劣，防护显著偏低、更轻。</summary>
    public static ArmorLayer CoarseClothCoat() => new() { Name = "粗布外套", Slot = ArmorSlot.Outer, SharpDefense = 4, BluntDefense = 2, Weight = 2 }; // 拟定待调

    /// <summary>
    /// 单只劳保手套：仅覆盖对应那一只手（含该手五指）的轻护甲；命中其它部位（含另一只手）不参与结算。
    /// 左右各一件独立护甲物品（装备槽按左右分，配合断肢）。防护极低。拟定待调。
    /// </summary>
    public static ArmorLayer WorkGlove(bool leftHand) => new()
    {
        Name = leftHand ? "左手套" : "右手套",
        Slot = ArmorSlot.Skin, SharpDefense = 1, BluntDefense = 1, Weight = 0,   // 拟定待调
        CoversParts = HumanBody.SubtreeNames(leftHand ? HumanBody.LeftHand : HumanBody.RightHand),
    };

    /// <summary>一副劳保手套 = 左手套 + 右手套两件独立护甲（开局发放）。各只覆盖对应那一只手。</summary>
    public static IReadOnlyList<ArmorLayer> WorkGloves() => new[]
    {
        WorkGlove(leftHand: true),
        WorkGlove(leftHand: false),
    };

    // ---- 布鲁斯（狗）装备护甲层（批次5，道格 2 级解锁制作）----
    // 狗体型小、覆盖部位少：身体甲仅护躯干、头甲仅护头（狗借用人形躯体，部位名对齐 HumanBody）。
    // 防御量级参照人类布甲/皮甲**打折**（覆盖面小、单薄）；两档头甲按"铁皮高防 / 铁丝轻便"拉开差异。
    // 数值全**拟定待调**（Sim/用户校准）。口袋狗衣无护甲（只给携带容量），故不在此表。

    /// <summary>布制狗衣（贴身层，仅护躯干）：照人类布衣打折，防护偏低、轻。</summary>
    public static ArmorLayer DogClothVest() => new()
    {
        Name = "布制狗衣", Slot = ArmorSlot.Skin, SharpDefense = 3, BluntDefense = 1.5, Weight = 1, // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Torso },
    };

    /// <summary>皮制狗衣（外套层，仅护躯干）：照人类皮甲打折，防护中、稍重。</summary>
    public static ArmorLayer DogLeatherVest() => new()
    {
        Name = "皮制狗衣", Slot = ArmorSlot.Outer, SharpDefense = 8, BluntDefense = 4, Weight = 3, // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Torso },
    };

    /// <summary>铁皮头甲（装甲层，仅护头）：刚性铁皮，防护高、较重。</summary>
    public static ArmorLayer DogIronHelmet() => new()
    {
        Name = "铁皮头甲", Slot = ArmorSlot.Plate, SharpDefense = 10, BluntDefense = 6, Weight = 4, // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Head },
    };

    /// <summary>铁丝头甲（装甲层，仅护头）：铁丝编笼，轻便但防护弱于铁皮。</summary>
    public static ArmorLayer DogWireHelmet() => new()
    {
        Name = "铁丝头甲", Slot = ArmorSlot.Plate, SharpDefense = 5, BluntDefense = 2, Weight = 1.5, // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Head },
    };
}
