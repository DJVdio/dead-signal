namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威护甲数据源，逐格对齐数据表 <c>docs/items-data.xlsx</c>『护甲表』（[SPEC-B18] 整表重做，[SPEC-B19] 补头盔）。
/// 表 26 件 = 人形 21（长袖布衣/花衬衫/长裤/运动鞋/短裤/皮革胸甲/粗布背心/粗布外套/布夹克/牛仔外套/皮夹克/皮甲/板甲/<b>军用头盔/防暴头盔</b>/劳保手套/腐皮
/// + [批次21·T26] <b>战争面具/粗布衬衫/粗布短裤/粗布长裤</b>）+ 狗 5（布制/皮制/口袋狗衣、铁皮/铁丝头甲）。**护甲不分阵营**（[SPEC-B16-补·护甲纠错]）——
/// <see cref="ArmorLayer"/> 只有防御值与覆盖部位，谁穿是生成侧的事。
/// <para>
/// <b>⚠️ <see cref="ArmorSlot"/> 是「伤害逐层结算的层序」，不是装备槽</b>——两者是不同的东西，别混。
/// 「谁跟谁互斥」由消费层的 <c>EquipSlot</c>（<c>ApparelSlots</c> 的 11 槽）决定；本枚举只决定
/// <see cref="CombatResolver.OrderOuterToInner"/> 里**先破哪一层**。
/// <para>
/// 表列 → 代码映射：「穿在哪层」贴身/裤装/脚/手 → <see cref="ArmorSlot.Skin"/>、外套 → <see cref="ArmorSlot.Outer"/>、
/// 装甲层 → <see cref="ArmorSlot.Plate"/>；「防护部位」空缺(全身) → <see cref="ArmorLayer.CoversParts"/> = null；
/// 「说明」→ <see cref="ArmorLayer.Description"/>。
/// </para>
/// <para>
/// <b>装甲层只管上身</b>（用户口径：「装甲层是只针对上身的装备层，是在贴身层、外套层之外的，头盔这类肯定不在装甲层」）——
/// 这句话说的是<b>装备槽</b>：表里那四顶头盔的「穿在哪层」列写的正是槽名（军用头盔=「头」、防暴头盔=「头、眼镜、面部」），
/// 而非「护甲层」。代码里头盔占 <c>EquipSlot.Head</c>（+ 防暴另占 Eyes/Face），<b>不占 <c>EquipSlot.PlateLayer</c></b>
/// ⇒ <b>戴头盔照样穿板甲</b>，用户要的就是这个。它们的 <see cref="ArmorSlot"/> 仍是 <see cref="ArmorSlot.Plate"/>，
/// 那只是说"头上这层甲在腐皮之外先被打穿"——头上只有一层甲，层序对它没有实际影响。
/// </para>
/// 防御值均为原型期<b>拟定待调</b>。传入 Resolve 前仍会经 <see cref="CombatResolver.OrderOuterToInner"/> 归一层序。
/// </summary>
public static class ArmorTable
{
    // ---- 覆盖部位常用集合（对齐表『防护部位』列写法）----

    /// <summary>躯干：胸 + 腹。</summary>
    private static HashSet<string> Torso() => new() { HumanBody.Chest, HumanBody.Abdomen };

    /// <summary>躯干 + 双臂（表写「胸、腹、左臂、右臂」；左右臂 = 上臂，手另有手套覆盖）。</summary>
    private static HashSet<string> TorsoAndArms() => new()
    {
        HumanBody.Chest, HumanBody.Abdomen, HumanBody.LeftArm, HumanBody.RightArm,
    };

    /// <summary>双腿：大腿 + 小腿（脚另有鞋覆盖）。</summary>
    private static HashSet<string> Legs() => new()
    {
        HumanBody.LeftLeg, HumanBody.RightLeg, HumanBody.LeftCalf, HumanBody.RightCalf,
    };

    // ---- 人形通用（表『类别/用途』= 通用(人形)）----

    /// <summary>长袖布衣（贴身层，护胸+腹+双臂）：开局三件套之一。</summary>
    public static ArmorLayer LongSleeveShirt() => new()
    {
        Name = "长袖布衣", Description = "袖子确实是长的。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>
    /// 花衬衫（贴身层，护胸+腹+双臂）：数值与长袖布衣同档——它就是一件衬衫，只是花的。
    /// 开局营地里那具尸体身上穿的就是这件（山姆的祖母，见 NarrativeSpotRegistry 祖母调查点）：可以扒下来穿。
    /// </summary>
    public static ArmorLayer FloralShirt() => new()
    {
        Name = "花衬衫", Description = "夏威夷风格，足够喜庆。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>长裤（裤装槽，护双大腿+双小腿）：开局三件套之一。</summary>
    public static ArmorLayer Trousers() => new()
    {
        Name = "长裤", Description = "挡风挡蚊子，挡不住长牙的东西。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = Legs(),
    };

    /// <summary>
    /// 运动鞋（脚槽，表口径护双脚含脚趾）：开局三件套之一。<b>成对品</b>——鞋不分左右，但一只只占一只脚槽、
    /// 只护那一只脚；护住双脚要两只（[SPEC-B18-补]）。此处 CoversParts 是表口径（两侧合计），
    /// 实际生效覆盖按穿戴那一侧裁剪（见 ApparelCatalog.CoversFor）。
    /// </summary>
    public static ArmorLayer Sneakers() => new()
    {
        Name = "运动鞋", Description = "跑快一点点——但愿比丧尸快点。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.25,
        CoversParts = HumanBody.SubtreeNames(HumanBody.LeftFoot, HumanBody.RightFoot),
    };

    /// <summary>短裤（裤装槽，仅护大腿=不防小腿）：与长裤同槽互斥，更轻，代价是小腿裸露。</summary>
    public static ArmorLayer Shorts() => new()
    {
        Name = "短裤", Description = "夏日风格。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.1,
        CoversParts = new HashSet<string> { HumanBody.LeftLeg, HumanBody.RightLeg },
    };

    /// <summary>皮革胸甲（装甲层，仅护胸=不防腹）：刚性护心甲，与皮甲/板甲同占装甲层、互斥。</summary>
    public static ArmorLayer ChestPlate() => new()
    {
        Name = "皮革胸甲", Description = "保护你的心。",
        Slot = ArmorSlot.Plate, SharpDefense = 18, BluntDefense = 9, Weight = 4,
        CoversParts = new HashSet<string> { HumanBody.Chest },
    };

    /// <summary>粗布背心（外套层，仅护胸+腹）：可制作布甲（读《裁缝手记》解锁）。无袖=不护臂。</summary>
    public static ArmorLayer CoarseClothVest() => new()
    {
        Name = "粗布背心", Description = "“缝缝补补又三年。”——奶奶",
        Slot = ArmorSlot.Outer, SharpDefense = 6, BluntDefense = 3, Weight = 0.1,
        CoversParts = Torso(),
    };

    /// <summary>粗布外套（外套层，护胸+腹+双臂）：夜间潜行加成的那件（见 NightWatchContest）。</summary>
    public static ArmorLayer CoarseClothCoat() => new()
    {
        Name = "粗布外套", Description = "天气转凉了，记得添外套。",
        Slot = ArmorSlot.Outer, SharpDefense = 6, BluntDefense = 3, Weight = 0.25,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>布夹克（外套层，护胸+腹+双臂）：灾难当天最常见的日常外套，外套层梯度第二档。</summary>
    public static ArmorLayer ClothJacket() => new()
    {
        Name = "布夹克", Description = "上班穿它，开会穿它，被咬那天也穿着它。",
        Slot = ArmorSlot.Outer, SharpDefense = 8, BluntDefense = 4, Weight = 0.3,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>牛仔外套（外套层，护胸+腹+双臂）：厚牛仔布，外套层梯度第三档——比布夹克更挡刀，也更沉。</summary>
    public static ArmorLayer DenimJacket() => new()
    {
        Name = "牛仔外套", Description = "耐磨、耐脏、耐撕咬——前两样是真的。",
        Slot = ArmorSlot.Outer, SharpDefense = 10, BluntDefense = 5, Weight = 0.6,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>皮夹克（外套层，护胸+腹+双臂）：外套层最强，仍很轻。</summary>
    public static ArmorLayer LeatherJacket() => new()
    {
        Name = "皮夹克", Description = "骑上摩托，倍有范儿。",
        Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 0.5,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>皮甲（装甲层，护胸+腹+双臂）：与皮革胸甲/板甲同占装甲层，可叠在皮夹克等外套之上。</summary>
    public static ArmorLayer Leather() => new()
    {
        Name = "皮甲", Description = "结实的鞣皮甲。",
        Slot = ArmorSlot.Plate, SharpDefense = 18, BluntDefense = 9, Weight = 6,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>板甲（装甲层 + 裤装双槽，护躯干+双臂+双腿）：全表最强也最重，占裤装槽=与长裤/短裤互斥。</summary>
    public static ArmorLayer Plate()
    {
        var covers = TorsoAndArms();
        covers.UnionWith(Legs());
        return new ArmorLayer
        {
            Name = "板甲", Description = "重吗？他能保护你脆弱的肉体。",
            Slot = ArmorSlot.Plate, SharpDefense = 50, BluntDefense = 25, Weight = 15,
            CoversParts = covers,
        };
    }

    // ---- 头部护甲（[SPEC-B19]）：护甲表第一次给人形件出头盔 ----
    // 为什么头盔是重物：`头` 是 Vital、MaxHp 仅 16、命中权重 6（全身 ≈103.4 ⇒ **5.8% 的命中落在头上，且头归零致死**）。
    // 在此之前人形件里一顶头盔都没有，"爆头"是所有护甲齐全的敌人（含精英丧尸）唯一的软肋。
    //
    // **两顶盔的防护完全相同（28/14，用户定表）——唯一的差别是「重量」与「护不护脸」。**
    // 军用 2.5kg，只护颅顶；防暴 4.5kg（几乎两倍），多护双眼/鼻/下巴。
    // ⇒ 玩家不是在挑"更好的头盔"，是在挑 **"要脸，还是要那 2kg 负重"**（负重有硬上限，见 CarryWeight）。
    // 面罩换来的到底是什么：`左/右眼` 命中权重合计仅 0.77%、且归零只致盲不致死——**它不是"防瞎"**，
    // 而是**堵掉"戳脸放血"**：脸上那几处（眼/鼻/下巴）低 HP 又无甲，是低伤武器在重甲敌人身上唯一能反复扎出
    // 流血伤口的地方（Sim：匕首打重甲精英的胜利 100% 来自失血）。
    // 二者同占**头槽**（EquipSlot.Head，互斥），而头槽**不是装甲层槽** ⇒ 戴着头盔照样能穿板甲（见 ApparelCatalog）。

    /// <summary>
    /// 军用头盔（头槽，<b>只护颅顶</b>）：防护与防暴头盔完全相同（28/14），但**轻得多**（2.5kg vs 4.5kg），
    /// 代价是**脸完全裸露**——眼/鼻/下巴照旧挨打（挖眼致盲、戳脸放血都仍然成立）。
    /// 只占头槽 ⇒ 眼/面还空着，能再扣一张防毒面具。
    /// </summary>
    public static ArmorLayer MilitaryHelmet() => new()
    {
        Name = "军用头盔", Description = "钢盔挡得住从天上掉下来的一切。麻烦在于，咬人的东西是从正面扑过来的。",
        Slot = ArmorSlot.Plate, SharpDefense = 28, BluntDefense = 14, Weight = 2.5,
        CoversParts = new HashSet<string> { HumanBody.Head },
    };

    /// <summary>
    /// 防暴头盔（头+眼镜+面部三槽，护<b>头 + 双眼 + 鼻 + 下巴</b>）：面罩把整张脸罩住（不含耳——面罩不包耳，
    /// 且耳归零无系统后果）。防护值与军用头盔完全相同（28/14），<b>贵在覆盖，不在数值</b>：
    /// 代价是 <b>4.5kg</b>（军用的近两倍，吃负重上限）+ 占眼/面槽 ⇒ 与防毒面具互斥。
    /// </summary>
    public static ArmorLayer RiotHelmet() => new()
    {
        Name = "防暴头盔", Description = "面罩挡得下砖头、棍棒和唾沫星子——上一任主人遇上的，不在这三样里。",
        Slot = ArmorSlot.Plate, SharpDefense = 28, BluntDefense = 14, Weight = 4.5,
        CoversParts = new HashSet<string>
        {
            HumanBody.Head, HumanBody.LeftEye, HumanBody.RightEye, HumanBody.Nose, HumanBody.Chin,
        },
    };

    /// <summary>
    /// 劳保手套（手槽，表口径护双手含五指）：<b>成对品</b>——手套不分左右（表里只有这一行，无左/右手套之分），
    /// 但一只只占一只手槽、只护那一只手；护住双手要<b>两件</b>（[SPEC-B18-补]，重量按件计=0.05×2）。
    /// 此处 CoversParts 是表口径（两侧合计），实际生效覆盖按穿戴那一侧裁剪（见 ApparelCatalog.CoversFor）。
    /// </summary>
    public static ArmorLayer WorkGloves() => new()
    {
        Name = "劳保手套", Description = "“劳动人民最光荣。”——奶奶",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.05,
        CoversParts = HumanBody.SubtreeNames(HumanBody.LeftHand, HumanBody.RightHand),
    };

    // ---- [批次21·T26] 新增可制作穿戴品（《野外生存指南》/《裁缝手记》解锁；见 RecipeBook）----
    //
    // ⚠️ **追加在末尾、不插队**（同"新武器追加末尾"那条铁律）：本节四件对 Sim 是**结构性零漂移**——
    // Sim 的护甲套是 Program.cs 里逐条列出的具名组合（长袖布衣 / 皮夹克+长袖布衣 / 板甲… ），
    // 它**按名点菜、不遍历本表** ⇒ 新增工厂方法根本进不了 Duel 的结算路径，既有基线一个字节都不会动。

    /// <summary>
    /// <b>战争面具</b>（面部 + 眼镜两槽；骨与木缝的面罩，护鼻 + 下巴 + <b>双眼</b>）。
    /// <para>
    /// 🔴 <b>[T59] 它现在遮眼了</b>（此前是「只占面部槽、刻意不遮眼」）。用户在 wiki 上把它的槽位
    /// 从「面部」改成了「<b>面部 + 眼镜</b>」——而<b>光加一个槽是没有意义的</b>：
    /// 眼镜槽上现存的两件（防暴头盔 / 防毒面具）<b>本来就都要占面部槽</b> ⇒ 它们与本件早已互斥，
    /// 游戏里也没有任何"只占眼镜槽"的护目镜可被它挤掉 ⇒ <b>多占这个槽，一条互斥关系都不会改变</b>。
    /// 于是这一格改动唯一说得通的读法是：<b>这张面具本来就该罩住眼睛</b>（"战争面具"的字面语义）。
    /// 若只占槽而不护眼，玩家付出一个槽位却什么也没换到 —— 那是纯负收益，不可能是用户的意思。
    /// 护栏见 <c>WikiSyncT59Tests.凡占眼镜槽者_必须覆盖双眼</c>。
    /// </para>
    /// <para>
    /// 与<b>防毒面具</b>互斥（都要这张脸）：那件遮眼+鼻但**不给防护**（毒气才是它的正业），这件给防护 —— 仍是个真取舍。
    /// 与<b>防暴头盔</b>也互斥（那顶头盔的面罩已经罩住整张脸）；<b>军用头盔</b>只占头槽，可与本件同戴。
    /// </para>
    /// </summary>
    public static ArmorLayer WarMask() => new()
    {
        Name = "战争面具", Description = "骨片与木头缝成的脸。戴上它你不会更有勇气——但扑上来的东西第一口咬到的是骨头，不是你的鼻子。",
        Slot = ArmorSlot.Plate, SharpDefense = 8, BluntDefense = 4, Weight = 0.3,
        CoversParts = new HashSet<string>
        {
            HumanBody.Nose, HumanBody.Chin, HumanBody.LeftEye, HumanBody.RightEye,
        },
    };

    /// <summary>
    /// <b>棉帽</b>（头槽，护 头 + 双耳）—— [T59] <b>用户在 wiki 上新加的一件</b>。
    /// <para>
    /// 数值取用户填的：<b>6 / 3、0.15kg</b>，与全部布类穿戴品同一条基线（粗布衬衫/长裤皆 6/3）。
    /// 它补的是**头槽的布甲缺口**：在此之前头槽只有军用头盔与防暴头盔两件<b>重甲</b>（4.5kg 级），
    /// 开局根本够不着 ⇒ 头（本体最要命的部位之一）在整个前期完全裸着。一顶 0.15kg 的布帽子
    /// 挡不下什么，但它把"头部零覆盖"这个洞堵上了，且几乎不吃负重 —— 这正是布甲的定位。
    /// </para>
    /// <para>与两顶头盔同占头槽（互斥）：戴了钢盔就不必再戴帽子，反之亦然。</para>
    /// </summary>
    public static ArmorLayer CottonHat() => new()
    {
        Name = "棉帽", Description = "毛茸茸的，冬季必备。",   // ← 用户在 wiki 上写的原话（表赢代码，不许替他润色）
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = new HashSet<string> { HumanBody.Head, HumanBody.LeftEar, HumanBody.RightEar },
    };

    /// <summary>粗布衬衫（贴身层，护胸+腹+双臂）：<b>可制作</b>版的长袖布衣，数值同档（同槽互斥）。</summary>
    public static ArmorLayer CoarseClothShirt() => new()
    {
        Name = "粗布衬衫", Description = "自己缝的衬衫，针脚歪得像条走投无路的路。它挡不住多少东西，但它至少是你的。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = TorsoAndArms(),
    };

    /// <summary>粗布短裤（裤装槽，<b>仅护大腿</b>——小腿裸着，同既有短裤的覆盖取舍）。</summary>
    public static ArmorLayer CoarseShorts() => new()
    {
        Name = "粗布短裤", Description = "布不够长，就成了短裤。小腿从此归风、蚊子和一切有牙齿的东西共有。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.1,
        CoversParts = new HashSet<string> { HumanBody.LeftLeg, HumanBody.RightLeg },
    };

    /// <summary>粗布长裤（裤装槽，护大腿+小腿）：<b>可制作</b>版的长裤，数值同档（同槽互斥）。</summary>
    public static ArmorLayer CoarseTrousers() => new()
    {
        Name = "粗布长裤", Description = "多缝了一截，小腿就有了着落。末日里的奢侈就是这么算的。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = Legs(),
    };

    // ---- 生物·天生（不可穿戴）----

    /// <summary>丧尸：一层腐烂硬皮，覆盖全身（表『防护部位』= 全身 → CoversParts=null）。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => new[]
    {
        new ArmorLayer
        {
            Name = "腐皮", Description = "丧尸自带的一层烂皮。",
            Slot = ArmorSlot.Skin, SharpDefense = 3, BluntDefense = 3, Weight = 0,
        },
    };

    // ---- 套装（生成侧配置）----

    /// <summary>
    /// 人形通用两层甲：皮夹克(外套) + 长袖布衣(贴身)。现用作<b>劫掠者生成配置</b> + 可搜刮/掉落的战利品。
    /// 开局幸存者不发这套——改发长袖布衣 + 长裤 + 运动鞋三件套。
    /// </summary>
    public static IReadOnlyList<ArmorLayer> SurvivorArmor() => new[] { LeatherJacket(), LongSleeveShirt() };

    // ---- 布鲁斯（狗）装备（表『类别/用途』= 狗专用(体型)）----
    // 狗体型小、覆盖部位少：身体甲仅护躯干(胸+腹)、头甲仅护头（狗借用人形躯体，部位名对齐 HumanBody）。
    // 身体三件（布制/皮制/口袋狗衣）同占身体槽互斥；头甲两件同占头槽互斥（DogEquipSlot.Head，不占身体槽）。

    /// <summary>布制狗衣（身体·贴身层，护胸+腹）。</summary>
    public static ArmorLayer DogClothVest() => new()
    {
        Name = "布制狗衣", Description = "给可爱的狗狗穿上可爱的衣服。",
        Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3, Weight = 0.15,
        CoversParts = Torso(),
    };

    /// <summary>皮制狗衣（身体·外套层，护胸+腹）。</summary>
    public static ArmorLayer DogLeatherVest() => new()
    {
        Name = "皮制狗衣", Description = "狗皮外的另一层皮。",
        Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 0.5,
        CoversParts = Torso(),
    };

    /// <summary>
    /// 口袋狗衣（身体·贴身层，护胸+腹）：[SPEC-B18] 起<b>不再是无甲纯容器</b>——表给了 2/1 薄甲，
    /// 同时为狗提供 6kg 负重（容量常量在消费层 DogGearCatalog.PocketVestCapacity）。拿防护换驮运。
    /// </summary>
    public static ArmorLayer DogPocketVest() => new()
    {
        Name = "口袋狗衣", Description = "好狗，好狗。",
        Slot = ArmorSlot.Skin, SharpDefense = 2, BluntDefense = 1, Weight = 0.25,
        CoversParts = Torso(),
    };

    /// <summary>铁皮头甲（狗·头槽，仅护头）：刚性铁皮，防护高、较重。</summary>
    public static ArmorLayer DogIronHelmet() => new()
    {
        Name = "铁皮头甲", Description = "狗狗也戴头盔。",
        Slot = ArmorSlot.Plate, SharpDefense = 18, BluntDefense = 12, Weight = 3,
        CoversParts = new HashSet<string> { HumanBody.Head },
    };

    /// <summary>铁丝头甲（狗·头槽，仅护头）：铁丝编笼，轻便，锐防弱于铁皮、钝防持平。</summary>
    public static ArmorLayer DogWireHelmet() => new()
    {
        Name = "铁丝头甲", Description = "曾经他是不让狗咬你的，现在他是用来保护狗咬你的。",
        Slot = ArmorSlot.Plate, SharpDefense = 12, BluntDefense = 12, Weight = 1.5,
        CoversParts = new HashSet<string> { HumanBody.Head },
    };

    // ---- 玩家可见风味文案（黑色幽默）：护甲名 → 一行描述 ----
    // 由库存物品 UI 经 Item.Armor 自动填充展示，不参与战斗结算。表『说明』列即此文案（含狗装备）。
    private static readonly System.Collections.Generic.Dictionary<string, string> _flavorByName = BuildFlavor();

    private static System.Collections.Generic.Dictionary<string, string> BuildFlavor()
    {
        var d = new System.Collections.Generic.Dictionary<string, string>();
        foreach (ArmorLayer layer in new[]
        {
            LongSleeveShirt(), FloralShirt(), Trousers(), Sneakers(), Shorts(), ChestPlate(), CoarseClothVest(),
            CoarseClothCoat(), ClothJacket(), DenimJacket(), LeatherJacket(), Leather(), Plate(),
            MilitaryHelmet(), RiotHelmet(), WorkGloves(),
            DogClothVest(), DogLeatherVest(), DogPocketVest(), DogIronHelmet(), DogWireHelmet(),
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
