namespace DeadSignal.Combat;

/// <summary>
/// 一套丧尸装束预设：显示名 + 抽中权重 + 身上的衣物/护甲（外层在前）。
/// <see cref="Weight"/> = 0 表示**不参与随机抽取**（authored 具名预设，只能被点名）。
/// </summary>
public sealed class ZombieOutfitPreset
{
    public required string Name { get; init; }

    /// <summary>抽中权重。日常池全表之和 = 1；**精英预设恒为 0**（不进随机池，见 <see cref="ZombieOutfit.ElitePresets"/>）。</summary>
    public required double Weight { get; init; }

    /// <summary>本套身上的衣物/护甲，**由外到内**排列（每次调用返回新实例，避免共享可变层）。</summary>
    public required Func<IReadOnlyList<ArmorLayer>> Clothes { get; init; }

    /// <summary>
    /// 样板草案：本 agent 铺的占位设定，<b>待用户定稿</b>，不是既定内容。
    /// （精英丧尸属 authored 范畴——按项目铁律，剧情/设定由用户手写，系统只提供"能被指定"的框架。）
    /// </summary>
    public bool IsDraft { get; init; }
}

/// <summary>
/// 丧尸身上穿什么。两条<b>互不相干</b>的通路（用户口径）：
/// <para>
/// ① <b>日常着装（随机）</b>——「大部分丧尸应当都是基础的布衣/夹克/长裤/短裤等灾难发生时的日常着装」。
/// 生成一只丧尸时从 <see cref="Presets"/> 加权抽一套（<see cref="RollArmor"/>），叠在腐皮之外。
/// </para>
/// <para>
/// ② <b>精英丧尸（authored·具名）</b>——「只有少部分我人为设定的高难度丧尸会穿护甲」。
/// <see cref="ElitePresets"/> <b>不进随机池</b>（权重 0），由关卡/Spawn 侧按名字点名
/// （<see cref="ArmorOf"/> / <see cref="Fixed"/>）。护甲件（皮革胸甲/皮甲/板甲）只在这条通路上出现。
/// </para>
/// <para>
/// 为什么丧尸需要衣服：护甲值、覆盖部位、武器区间与穿透均来自 Wiki 配置表。
/// 逐层结算里 atk ∈ [伤害下限,上限]、def ∈ [0, 护甲值×(1−穿透)]，<c>atk &lt; def/2</c> 才算挡下。
/// 光靠腐皮的防护有限，丧尸的实际防御还来自生前衣物。
/// 用户拒绝抬腐皮（"腐皮本来就是烂肉"），理由是<b>丧尸也会穿衣服</b>——本类即是把那句话变成现实。
/// </para>
/// <para>
/// 通用口径：<b>不做破损折损</b>，防护值一律取 <see cref="ArmorTable"/> 表值；"破损"由<b>部分覆盖</b>表达
/// （多数丧尸只剩一两件、头脚全裸），而非给防御值打折——打折会把刚够着的挡下门槛又压回零，等于白做。
/// （用户后来把**掉落**那一侧也一并推翻了：尸体<b>穿什么扒什么，零掷骰、不折损</b>，见消费层 <c>CorpseLoot</c>。
/// 所以"衣服被砍坏了"在本作里<b>哪一侧都不表达</b>——防护不打折，战利品也不打折。）
/// <para>
/// <b>日常丧尸头/脚恒裸</b>：日常池里没有头盔也没有鞋（运动鞋是玩家开局装备）。
/// <b>但精英不再是</b>（[SPEC-B19]）：护甲表补进了人形头盔（军用头盔 / 防暴头盔），两套精英预设各戴一顶——
/// "爆头是精英的唯一软肋"这句话到此为止。具体部位属性以部位配置为准，
/// 一个从头到脚披板甲、唯独脑袋光着的防暴警察，实战里等于没穿甲。头盔<b>只在精英通路上出现</b>，
/// 不进随机池 ⇒ 街上的普通丧尸一如既往地光着头。
/// </para>
/// 预设表<b>硬编码在 C#</b> 而非 json：<see cref="ArmorTable"/>/<see cref="WeaponTable"/> 这两张权威表本身就是
/// 硬编码 C#，且 DeadSignal.Combat 是零依赖纯类库（无 IO）——为一张预设表引入文件加载不划算。
/// 放引擎侧（而非 godot/scripts 消费层）是因为 <c>DeadSignal.Sim</c> 与 Godot 运行时**都要用**它生成丧尸。
/// </summary>
public static class ZombieOutfit
{
    /// <summary>
    /// ① 日常着装随机池（权重合计 1）。顺序即累积权重在 [0,1) 上的排布顺序，
    /// 每套内部**由外到内**（外套 → 贴身），使腐皮附加在末尾后恒为最内层。
    /// 日常池的权重与衣物组合均以 Wiki 配置表为准；只有日常衣物，**没有任何护甲件**。
    /// </summary>
    public static IReadOnlyList<ZombieOutfitPreset> Presets { get; } = new ZombieOutfitPreset[]
    {
        new()
        {
            Name = "衣不蔽体", Weight = 0.15,
            Clothes = () => Array.Empty<ArmorLayer>(),
        },
        new()
        {
            Name = "仅剩长裤", Weight = 0.18,
            Clothes = () => new[] { ArmorTable.Trousers() },
        },
        new()
        {
            Name = "仅剩上衣", Weight = 0.13,
            Clothes = () => new[] { ArmorTable.LongSleeveShirt() },
        },
        new()
        {
            Name = "寻常打扮", Weight = 0.24,
            Clothes = () => new[] { ArmorTable.LongSleeveShirt(), ArmorTable.Trousers() },
        },
        // 用户点名的「夹克」：灾难当天身上本来就穿着的那件，不是搜刮来的战利品。
        // 外套层四件互斥，具体数值与权重以 Wiki 配置表为准；
        // 频率按**灾难当天现实里谁穿得多**排：布夹克/牛仔外套是最常见的日常外套 → 皮夹克次之
        // → 粗布外套（自制感、末世产物）反而最少见。顺带也满足"越挡刀的越稀有"。
        new()
        {
            // 布夹克的表述本身就是为这一刻写的：「上班穿它，开会穿它，被咬那天也穿着它。」
            Name = "穿布夹克上班的", Weight = 0.08,
            Clothes = () => new[]
            {
                ArmorTable.ClothJacket(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
            },
        },
        new()
        {
            Name = "穿牛仔外套的", Weight = 0.06,
            Clothes = () => new[]
            {
                ArmorTable.DenimJacket(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
            },
        },
        new()
        {
            Name = "穿皮夹克的", Weight = 0.04,
            Clothes = () => new[]
            {
                ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
            },
        },
        new()
        {
            Name = "套着粗布外套", Weight = 0.02,
            Clothes = () => new[]
            {
                ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(),
            },
        },
        new()
        {
            Name = "夏日打扮", Weight = 0.10,
            Clothes = () => new[] { ArmorTable.CoarseClothVest(), ArmorTable.Shorts() },
        },
    };

    /// <summary>
    /// ② 精英丧尸（authored·具名）：<b>权重 0 = 永不被随机抽到</b>，只能由关卡/Spawn 侧点名
    /// （见 <see cref="ArmorOf"/>）。这是"生前职业带着护甲变成的丧尸"，是<b>用户人为设定的高难度点</b>。
    /// <para>
    /// 下列两套均为 <see cref="ZombieOutfitPreset.IsDraft"/> = true 的<b>样板草案，待用户定稿</b>——
    /// 用的都是护甲表里已有的件，未新造装备。要加新的精英丧尸：在此追加一条即可（关卡侧用名字点名）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<ZombieOutfitPreset> ElitePresets { get; } = new ZombieOutfitPreset[]
    {
        new()
        {
            // 板甲（护甲值与覆盖部位来自 Wiki）= 全表最强 + 防暴头盔（护头 + 整张脸）。
            // 从头到脚一处不露：能打的只剩耳朵、手（有手套）、脚。这是全游戏最硬的一只东西——**有意为之**。
            Name = "防暴警察丧尸", Weight = 0, IsDraft = true,
            Clothes = () => new[]
            {
                ArmorTable.RiotHelmet(), ArmorTable.Plate(), ArmorTable.CoarseClothCoat(),
                ArmorTable.LongSleeveShirt(), ArmorTable.WorkGloves(),
            },
        },
        new()
        {
            // 皮甲（装甲层）+ 皮夹克（外套）+ 长袖布衣（贴身）= 上身三层叠满，但强度远低于板甲；腿只有布裤。
            // 军用头盔只护颅顶 ⇒ **脸是敞着的**：眼/鼻/下巴照打（挖眼致盲仍然成立）。它比防暴警察好对付得多。
            Name = "军人丧尸", Weight = 0, IsDraft = true,
            Clothes = () => new[]
            {
                ArmorTable.MilitaryHelmet(), ArmorTable.Leather(), ArmorTable.LeatherJacket(),
                ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(), ArmorTable.WorkGloves(),
            },
        },
    };

    /// <summary>按名字查一套预设（日常池 + 精英池都能查到）。</summary>
    public static bool TryGet(string name, out ZombieOutfitPreset preset)
    {
        preset = Presets.Concat(ElitePresets).FirstOrDefault(p => p.Name == name)!;
        return preset is not null;
    }

    /// <summary>按权重从<b>日常池</b>抽一套（消耗恰好一次 roll）。精英预设权重为 0，永远抽不到。</summary>
    public static ZombieOutfitPreset RollPreset(IRandomSource rng)
    {
        double roll = rng.Range(0, 1);
        double cumulative = 0;

        foreach (ZombieOutfitPreset preset in Presets)
        {
            cumulative += preset.Weight;
            if (roll < cumulative)
            {
                return preset;
            }
        }

        // roll == 1.0（或累积权重的浮点尾巴）落到这里：归到最后一套，不越界。
        return Presets[^1];
    }

    /// <summary>
    /// 随机生成一只<b>日常</b>丧尸的完整 <c>DefenderArmor</c>：抽中那套的衣物（由外到内）+ **末尾恒为腐皮**。
    /// 腐皮必须排最后——它与布类同占 <see cref="ArmorSlot.Skin"/> 槽，而
    /// <see cref="CombatResolver.OrderOuterToInner"/> 是按槽的**稳定排序**，同槽内靠输入顺序定内外：
    /// 排在末尾 ⇒ 归一后仍是最内层（先破衣服，再破皮）。
    /// </summary>
    public static IReadOnlyList<ArmorLayer> RollArmor(IRandomSource rng) => WithHide(RollPreset(rng));

    /// <summary>
    /// <b>点名</b>一套预设的完整 <c>DefenderArmor</c>（确定性，不掷骰）。关卡/Spawn 侧用它摆放 authored 的
    /// 精英丧尸，如 <c>ZombieOutfit.ArmorOf("防暴警察丧尸")</c>；日常预设也可点名（剧情要一只"只剩长裤"的）。
    /// </summary>
    /// <exception cref="KeyNotFoundException">没有这个名字的预设——名字拼错应当立刻炸，而不是静默发一套光身。</exception>
    public static IReadOnlyList<ArmorLayer> ArmorOf(string name)
    {
        if (!TryGet(name, out ZombieOutfitPreset preset))
        {
            throw new KeyNotFoundException(
                $"没有名为「{name}」的丧尸装束预设。日常池：{string.Join("/", Presets.Select(p => p.Name))}；" +
                $"精英池：{string.Join("/", ElitePresets.Select(p => p.Name))}。");
        }

        return WithHide(preset);
    }

    /// <summary>
    /// 把一套具名预设包成 <c>DuelFighter.ArmorFactory</c> 能吃的工厂（<b>忽略随机源、不消耗 roll</b>）。
    /// 供 Sim 侧点名精英丧尸：<c>new DuelFighter { ArmorFactory = ZombieOutfit.Fixed("防暴警察丧尸") }</c>。
    /// </summary>
    public static Func<IRandomSource, IReadOnlyList<ArmorLayer>> Fixed(string name)
    {
        ArmorOf(name); // 提前校验名字：拼错在建 fighter 时就炸，而不是等开打
        return _ => ArmorOf(name);
    }

    /// <summary>衣物（由外到内）+ 末尾追加腐皮（恒为最内层）。</summary>
    private static IReadOnlyList<ArmorLayer> WithHide(ZombieOutfitPreset preset)
    {
        var armor = new List<ArmorLayer>(preset.Clothes());
        armor.AddRange(ArmorTable.ZombieHide());
        return armor;
    }
}
