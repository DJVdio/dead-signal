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

    /// <summary>
    /// 人形通用两层甲：皮夹克(外) + 贴身布衣(内)。**护甲无阵营归属**（[SPEC-B16-补·护甲纠错]）——
    /// 任何人形都能穿；本套现用作<b>劫掠者生成配置</b>（<see cref="ArmorLayer"/> 只有防御值、没有阵营字段，
    /// 敌人穿什么是生成侧的事）+ 可搜刮/掉落的战利品。
    /// 开局幸存者<b>不再</b>发这套——改发 <see cref="LongSleeveShirt"/> + <see cref="Trousers"/> 两件基础衣物。
    /// </summary>
    public static IReadOnlyList<ArmorLayer> SurvivorArmor() => new[]
    {
        new ArmorLayer { Name = "皮夹克", Description = "一件旧皮夹克，挡风、挡刀，也挡一点点运气不好。", SharpDefense = 6, BluntDefense = 3, Weight = 3, Slot = ArmorSlot.Outer },   // 拟定待调
        new ArmorLayer { Name = "贴身布衣", Description = "贴身的布衣，聊胜于裸。", SharpDefense = 2, BluntDefense = 1, Weight = 1, Slot = ArmorSlot.Skin }, // 拟定待调
    };

    // ---- 开局基础衣物（[SPEC-B16-补]：所有开局幸存者只穿这两件，无特殊护甲）----
    // 均为"仅蔽体"级：防护极低、轻。占槽分离——长袖布衣走贴身层(躯干)，长裤走裤子槽(护腿)。数值拟定待调。

    /// <summary>长袖布衣（贴身层，护上身：胸+腹+双臂）：开局基础衣物，仅蔽体。躯干细分后覆盖胸+腹（[SPEC-B17]）。</summary>
    public static ArmorLayer LongSleeveShirt() => new()
    {
        Name = "长袖布衣", Description = "一件长袖布衣。好消息是袖子确实是长的，坏消息是也就袖子长。",
        Slot = ArmorSlot.Skin, SharpDefense = 2, BluntDefense = 1, Weight = 1,   // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen, HumanBody.LeftArm, HumanBody.RightArm },
    };

    /// <summary>长裤（占裤子槽，护双腿：双大腿+双小腿）：开局基础衣物，仅蔽体。腿细分后覆盖大腿+小腿（[SPEC-B17]）。</summary>
    public static ArmorLayer Trousers() => new()
    {
        Name = "长裤", Description = "一条长裤，挡风、挡蚊子，挡不住任何长牙的东西。",
        Slot = ArmorSlot.Skin, SharpDefense = 2, BluntDefense = 1, Weight = 1,   // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.LeftLeg, HumanBody.RightLeg, HumanBody.LeftCalf, HumanBody.RightCalf },
    };

    /// <summary>运动鞋（占左右脚槽，护双脚：脚+脚趾子树）：开局基础衣物，仅蔽体。11 槽的脚槽承接（[SPEC-B16-补2]）。</summary>
    public static ArmorLayer Sneakers() => new()
    {
        Name = "运动鞋", Description = "一双运动鞋，跑起来轻快，也就轻快那么一点点。",
        Slot = ArmorSlot.Skin, SharpDefense = 1, BluntDefense = 1, Weight = 1,   // 拟定待调：仅蔽体
        CoversParts = HumanBody.SubtreeNames(HumanBody.LeftFoot, HumanBody.RightFoot),
    };

    // ---- 部位细分示例装备（[SPEC-B17-补]：覆盖取舍——更轻/更凉快，代价=露出未覆盖部位）----
    // 部位细分的核心目的是让护甲能只覆盖细分部位，重量与覆盖面挂钩，与既有甲形成梯度。数值拟定待调。

    /// <summary>胸甲（装甲层，仅护胸=不防腹）：刚性护心甲，防护高于布衣、低于全躯干板甲，重量轻于板甲（[SPEC-B17-补]）。</summary>
    public static ArmorLayer ChestPlate() => new()
    {
        Name = "胸甲", Description = "一块护心的胸甲，心是护住了，肚子只能靠信仰。",
        Slot = ArmorSlot.Plate, SharpDefense = 22, BluntDefense = 8, Weight = 6,   // 拟定待调：高于布衣、低于板甲(34/11/12)
        CoversParts = new HashSet<string> { HumanBody.Chest },
    };

    /// <summary>短裤（占裤子槽，仅护大腿=不防小腿）：比长裤轻、更凉快，代价是小腿裸露（[SPEC-B17-补]）。与长裤同占裤子槽、互斥。</summary>
    public static ArmorLayer Shorts() => new()
    {
        Name = "短裤", Description = "一条短裤，凉快，代价是小腿自求多福。",
        Slot = ArmorSlot.Skin, SharpDefense = 2, BluntDefense = 1, Weight = 0.5,   // 拟定待调：比长裤(重1)轻，覆盖面更小
        CoversParts = new HashSet<string> { HumanBody.LeftLeg, HumanBody.RightLeg },
    };

    /// <summary>
    /// 粗布背心（外套层，仅护胸+腹）：可制作布甲（读《裁缝手记》解锁）。数值沿其 craft 定位——高于裸布、
    /// 轻薄；无袖=不护手臂（[SPEC-B17]）。补此独立层前，粗布背心走 Item.Armor 无覆盖信息（全覆盖 null）。
    /// </summary>
    public static ArmorLayer CoarseClothVest() => new()
    {
        Name = "粗布背心", Description = "几块破布缝的背心，挡风、挡视线，挡不了太多真家伙。",
        Slot = ArmorSlot.Outer, SharpDefense = 5, BluntDefense = 2, Weight = 2,   // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen },
    };

    /// <summary>丧尸：一层腐烂硬皮（对钝器略韧）。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => new[]
    {
        new ArmorLayer { Name = "腐皮", SharpDefense = 1.5, BluntDefense = 3, Weight = 0, Slot = ArmorSlot.Skin }, // 拟定待调
    };

    // ---- Sim 参数化甲层（单层工厂，供聚合/对决按层组合）----

    /// <summary>布衣（贴身层）。</summary>
    public static ArmorLayer Cloth() => new() { Name = "布衣", Description = "一身布衣，能挡风，挡不了太多别的。", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2, Weight = 1 };    // 拟定待调

    /// <summary>皮甲（外套层）。</summary>
    public static ArmorLayer Leather() => new() { Name = "皮甲", Description = "鞣制皮甲，结实耐操，就是穿久了有点闷。", Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 4 }; // 拟定待调

    /// <summary>板甲（装甲层）。</summary>
    public static ArmorLayer Plate() => new() { Name = "板甲", Description = "沉甸甸的板甲，安全感十足，跑起来像口移动的棺材。", Slot = ArmorSlot.Plate, SharpDefense = 34, BluntDefense = 11, Weight = 12 }; // 拟定待调

    /// <summary>粗布外套（外套层）：照皮甲但更粗劣，防护显著偏低、更轻。</summary>
    public static ArmorLayer CoarseClothCoat() => new() { Name = "粗布外套", Description = "粗布外套，看着像件衣服，防护也就像件衣服。", Slot = ArmorSlot.Outer, SharpDefense = 4, BluntDefense = 2, Weight = 2 }; // 拟定待调

    /// <summary>
    /// 单只劳保手套：仅覆盖对应那一只手（含该手五指）的轻护甲；命中其它部位（含另一只手）不参与结算。
    /// 左右各一件独立护甲物品（装备槽按左右分，配合断肢）。防护极低。拟定待调。
    /// </summary>
    public static ArmorLayer WorkGlove(bool leftHand) => new()
    {
        Name = leftHand ? "左手套" : "右手套",
        Description = "一只劳保手套，护得住五根手指，护不住别的。",
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

    /// <summary>布制狗衣（贴身层，仅护躯干=胸+腹）：照人类布衣打折，防护偏低、轻。狗甲不拆细部位，整躯干覆盖（胸+腹，[SPEC-B17] 待确认）。</summary>
    public static ArmorLayer DogClothVest() => new()
    {
        Name = "布制狗衣", Slot = ArmorSlot.Skin, SharpDefense = 3, BluntDefense = 1.5, Weight = 1, // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen },
    };

    /// <summary>皮制狗衣（外套层，仅护躯干=胸+腹）：照人类皮甲打折，防护中、稍重。狗甲不拆细部位，整躯干覆盖（胸+腹，[SPEC-B17] 待确认）。</summary>
    public static ArmorLayer DogLeatherVest() => new()
    {
        Name = "皮制狗衣", Slot = ArmorSlot.Outer, SharpDefense = 8, BluntDefense = 4, Weight = 3, // 拟定待调
        CoversParts = new HashSet<string> { HumanBody.Chest, HumanBody.Abdomen },
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

    // ---- 玩家可见风味文案（黑色幽默）：护甲名 → 一行描述 ----
    // 由库存物品 UI 经 Item.Armor 自动填充展示，不参与战斗结算。仅覆盖**人类护甲**；
    // 布鲁斯狗装备（含无甲的口袋狗衣）文案在 DogGearCatalog.DogGearDef.Description，Item.Armor 优先查那边。
    private static readonly System.Collections.Generic.Dictionary<string, string> _flavorByName = BuildFlavor();

    private static System.Collections.Generic.Dictionary<string, string> BuildFlavor()
    {
        var d = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var layer in SurvivorArmor())
        {
            d[layer.Name] = layer.Description;
        }
        foreach (var layer in new[]
        {
            LongSleeveShirt(), Trousers(), Sneakers(), ChestPlate(), Shorts(), CoarseClothVest(),
            Cloth(), Leather(), Plate(), CoarseClothCoat(), WorkGlove(true), WorkGlove(false),
        })
        {
            d[layer.Name] = layer.Description;
        }
        return d;
    }

    /// <summary>按护甲显示名取一行风味描述（查不到返回空串）。供消费层 Item.Armor 自动填充库存物品描述。</summary>
    public static string DescriptionOf(string name)
        => name != null && _flavorByName.TryGetValue(name, out var d) ? d : "";
}
