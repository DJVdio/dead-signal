namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威护甲数据源。<b>[数值外置]</b> 数值已从本类 C# 常量搬到 <c>godot/data/config/armor.json</c>（id → <see cref="ArmorLayer"/>），
/// 工厂方法（<c>ArmorTable.Plate()</c> …）身体改为 <c>=&gt; Cfg("plate")</c>——方法名/文档/组表遍历序全保留、~调用点零改。
/// 逐格对齐数据表『护甲表』（[SPEC-B18] 整表重做，[SPEC-B19] 补头盔）。
/// 表 31 件 = 人形 26（长袖布衣/花衬衫/长裤/运动鞋/短裤/皮革胸甲/粗布背心/粗布外套/布夹克/牛仔外套/皮夹克/皮甲/板甲/<b>军用头盔/防暴头盔</b>/劳保手套/腐皮
/// + [批次21·T26] <b>战争面具/粗布衬衫/粗布短裤/粗布长裤</b> + [T59] <b>棉帽</b> + <b>恐怖装甲/墨镜/平光眼镜/自制简易墨镜(雪镜)</b>）+ 狗 5（布制/皮制/口袋狗衣、铁皮/铁丝头甲）。**护甲不分阵营**（[SPEC-B16-补·护甲纠错]）——
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
    /// <summary>护甲数值取用糖：按 id 从 <c>armor.json</c>（<see cref="ArmorConfig"/> 段）取一件护甲层，缺失 fail-fast。</summary>
    private static ArmorLayer Cfg(string id) => CombatCatalog.Section<ArmorConfig>().Get(id);

    // ---- 人形通用（表『类别/用途』= 通用(人形)）----

    /// <summary>长袖布衣（贴身层，护胸+腹+双臂）：开局三件套之一。</summary>
    public static ArmorLayer LongSleeveShirt() => Cfg("long_sleeve_shirt");

    /// <summary>
    /// 花衬衫（贴身层，护胸+腹+双臂）：数值与长袖布衣同档——它就是一件衬衫，只是花的。
    /// 开局营地里那具尸体身上穿的就是这件（山姆的祖母，见 NarrativeSpotRegistry 祖母调查点）：可以扒下来穿。
    /// </summary>
    public static ArmorLayer FloralShirt() => Cfg("floral_shirt");

    /// <summary>长裤（裤装槽，护双大腿+双小腿）：开局三件套之一。</summary>
    public static ArmorLayer Trousers() => Cfg("trousers");

    /// <summary>
    /// 运动鞋（脚槽，表口径护双脚含脚趾）：开局三件套之一。<b>成对品</b>——鞋不分左右，但一只只占一只脚槽、
    /// 只护那一只脚；护住双脚要两只（[SPEC-B18-补]）。此处 CoversParts 是表口径（两侧合计），
    /// 实际生效覆盖按穿戴那一侧裁剪（见 ApparelCatalog.CoversFor）。
    /// </summary>
    public static ArmorLayer Sneakers() => Cfg("sneakers");

    /// <summary>短裤（裤装槽，仅护大腿=不防小腿）：与长裤同槽互斥，更轻，代价是小腿裸露。</summary>
    public static ArmorLayer Shorts() => Cfg("shorts");

    /// <summary>皮革胸甲（装甲层，仅护胸=不防腹）：刚性护心甲，与皮甲/板甲同占装甲层、互斥。</summary>
    public static ArmorLayer ChestPlate() => Cfg("chest_plate");

    /// <summary>粗布背心（外套层，仅护胸+腹）：可制作布甲（读《裁缝手记》解锁）。无袖=不护臂。</summary>
    public static ArmorLayer CoarseClothVest() => Cfg("coarse_cloth_vest");

    /// <summary>粗布外套（外套层，护胸+腹+双臂）：夜间潜行加成的那件（见 NightWatchContest）。</summary>
    public static ArmorLayer CoarseClothCoat() => Cfg("coarse_cloth_coat");

    /// <summary>布夹克（外套层，护胸+腹+双臂）：灾难当天最常见的日常外套，外套层梯度第二档。</summary>
    public static ArmorLayer ClothJacket() => Cfg("cloth_jacket");

    /// <summary>牛仔外套（外套层，护胸+腹+双臂）：厚牛仔布，外套层梯度第三档——比布夹克更挡刀，也更沉。</summary>
    public static ArmorLayer DenimJacket() => Cfg("denim_jacket");

    /// <summary>皮夹克（外套层，护胸+腹+双臂）：外套层最强，仍很轻。</summary>
    public static ArmorLayer LeatherJacket() => Cfg("leather_jacket");

    /// <summary>皮甲（装甲层，护胸+腹+双臂）：与皮革胸甲/板甲同占装甲层，可叠在皮夹克等外套之上。</summary>
    public static ArmorLayer Leather() => Cfg("leather");

    /// <summary>板甲（装甲层 + 裤装双槽，护躯干+双臂+双腿）：全表最强也最重，占裤装槽=与长裤/短裤互斥。</summary>
    public static ArmorLayer Plate() => Cfg("plate");

    // ---- 头部护甲（[SPEC-B19]）：护甲表第一次给人形件出头盔 ----
    // 为什么头盔是重物：`头` 是 Vital、MaxHp 仅 16、命中权重 6（全身 ≈103.4 ⇒ **5.8% 的命中落在头上，且头归零致死**）。
    // 在此之前人形件里一顶头盔都没有，"爆头"是所有护甲齐全的敌人（含精英丧尸）唯一的软肋。
    //
    // 🔴 **[T68] 两顶盔不再是"防护相同、只差重量与护脸"了**（旧口径作废，用户拍板改）。用户原话：
    //    「防暴头盔应当更重防御更强，还有一些 debuff。军用头盔应当更泛用一些。」现在是**两条不同的路线**：
    //    · **军用头盔** = **泛用款**：28 / **14**、**2.5kg**、只护颅顶、**无 debuff**。轻、没有副作用，
    //      眼/面还空着（能再扣防毒面具）⇒ 什么场合都能戴，代价是脸完全裸露（挖眼、戳脸放血照旧成立）。
    //    · **防暴头盔** = **重装款**：35 / **22**（防御全面更高）、**4.5kg**（军用的近两倍）、护整张脸，
    //      外加 **debuff：视野距离与范围 −10% / 听力 −10%**（钢壳闷在头上的代价）。
    //      ⚠️ 那两条 debuff 是**引擎新轴**（视野/听觉系数目前不吃穿戴品）⇒ **未落地、已挂起统一立项**；
    //         用户写的字保留在数值表的备注里，别当它已经生效。
    // ⇒ 取舍从"要脸还是要 2kg"升级成 **"要泛用的轻盔，还是要更硬但更笨重、还会削感知的重盔"**。
    // 面罩护脸换来的到底是什么：`左/右眼` 命中权重合计仅 0.77%、且归零只致盲不致死——**它不是"防瞎"**，
    // 而是**堵掉"戳脸放血"**：脸上那几处（眼/鼻/下巴）低 HP 又无甲，是低伤武器在重甲敌人身上唯一能反复扎出
    // 流血伤口的地方（Sim：匕首打重甲精英的胜利 100% 来自失血）。
    // 二者同占**头槽**（EquipSlot.Head，互斥），而头槽**不是装甲层槽** ⇒ 戴着头盔照样能穿板甲（见 ApparelCatalog）。

    /// <summary>
    /// 军用头盔（头槽，<b>只护颅顶</b>）：**泛用款**（28 / 14、2.5kg、无 debuff）。比防暴盔防御低一档，
    /// 但**轻得多**（2.5kg vs 4.5kg）、**没有任何副作用**，且眼/面还空着 ⇒ 能再扣一张防毒面具。
    /// 代价是**脸完全裸露**——眼/鼻/下巴照旧挨打（挖眼致盲、戳脸放血都仍然成立）。
    /// </summary>
    public static ArmorLayer MilitaryHelmet() => Cfg("military_helmet");

    /// <summary>
    /// 防暴头盔（头+眼镜+面部三槽，护<b>头 + 双眼 + 鼻 + 下巴</b>）：**重装款**——面罩把整张脸罩住
    /// （不含耳——面罩不包耳，且耳归零无系统后果）。[T68] 防护**全面强于军用**（35 / 22 vs 28 / 14），
    /// 代价是 <b>4.5kg</b>（军用的近两倍，吃负重上限）+ 占眼/面槽（与防毒面具互斥）+ <b>debuff：视野 −10% / 听力 −10%</b>
    /// （⚠️ 引擎新轴，未落地、已挂起——见上方头部护甲区块的说明）。
    /// </summary>
    public static ArmorLayer RiotHelmet() => Cfg("riot_helmet");

    /// <summary>
    /// 劳保手套（手槽，表口径护双手含五指）：<b>成对品</b>——手套不分左右（表里只有这一行，无左/右手套之分），
    /// 但一只只占一只手槽、只护那一只手；护住双手要<b>两件</b>（[SPEC-B18-补]，重量按件计=0.05×2）。
    /// 此处 CoversParts 是表口径（两侧合计），实际生效覆盖按穿戴那一侧裁剪（见 ApparelCatalog.CoversFor）。
    /// </summary>
    public static ArmorLayer WorkGloves() => Cfg("work_gloves");

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
    public static ArmorLayer WarMask() => Cfg("war_mask");

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
    public static ArmorLayer CottonHat() => Cfg("cotton_hat");

    /// <summary>粗布衬衫（贴身层，护胸+腹+双臂）：<b>可制作</b>版的长袖布衣，数值同档（同槽互斥）。</summary>
    public static ArmorLayer CoarseClothShirt() => Cfg("coarse_cloth_shirt");

    /// <summary>粗布短裤（裤装槽，<b>仅护大腿</b>——小腿裸着，同既有短裤的覆盖取舍）。</summary>
    public static ArmorLayer CoarseShorts() => Cfg("coarse_shorts");

    /// <summary>粗布长裤（裤装槽，护大腿+小腿）：<b>可制作</b>版的长裤，数值同档（同槽互斥）。</summary>
    public static ArmorLayer CoarseTrousers() => Cfg("coarse_trousers");

    // ---- [T68] 用户在 wiki 上新加的三件（追加在末尾、不插队）----
    //
    // ⚠️ **对 Sim 结构性零漂移**：Sim 的护甲套是 `Program.cs` 里逐条点名的具名组合（长袖布衣 / 皮夹克+长袖布衣 / 板甲…），
    // 它**按名点菜、不遍历本表** ⇒ 新增的工厂方法进不了 `Duel` 的结算路径，既有基线一个字节都不会动。

    /// <summary>
    /// <b>恐怖装甲</b>（装甲层，护胸 + 腹）—— 20 / 10、3kg。
    /// <para>
    /// 数值取用户填的。它**卡在皮革胸甲（25/12.5、4kg）之下、皮夹克（18/9、0.5kg）之上**，
    /// 定位是<b>「装甲层的廉价档」</b>：比皮革胸甲弱一档，但<b>轻 1kg、且不吃皮革产能</b>
    /// （皮革是鞣制链的瓶颈——生皮→鞣制药水→皮革，每一步都要工时）。
    /// </para>
    /// <para>
    /// 与皮革胸甲/皮甲/板甲同占<b>装甲层</b>（互斥）：它不是"再加一层"，是"这一层你先穿得起什么"。
    /// </para>
    /// </summary>
    public static ArmorLayer HorrorArmor() => Cfg("horror_armor");

    /// <summary>
    /// <b>墨镜</b>（眼镜槽，护双眼）—— 1 / 1、0.1kg。
    /// <para>
    /// 🔴 <b>它的价值不在那 1 点防御，在它占的槽</b>：眼镜槽上还坐着<b>防暴头盔 / 战争面具 / 防毒面具</b>三件
    /// （它们都要连着占面部槽）⇒ <b>戴墨镜 = 放弃这三件里的任何一件</b>。这是个真取舍，且是本表第一次出现
    /// 「只占眼镜槽、不占面部槽」的东西 —— 在它之前，眼镜槽从来没有过独立的候选人。
    /// </para>
    /// <para>
    /// ⚠️ 用户写的效果「<b>白天 +5% 视野范围</b>」是<b>引擎新轴</b>（视野系数目前不吃穿戴品）⇒ <b>效果未做</b>，
    /// 已列入挂起的新轴清单统一立项。**在效果落地之前，它就是一件"用一个宝贵槽位换 1 点防御"的负收益装备** ——
    /// 这是<b>已知的、临时的</b>状态，不是数值失衡，别去"修"它。
    /// </para>
    /// </summary>
    public static ArmorLayer Sunglasses() => Cfg("sunglasses");

    /// <summary>
    /// <b>平光眼镜</b>（眼镜槽，护双眼）—— 1 / 1、0.1kg。与<see cref="Sunglasses"/> 同槽互斥、同数值。
    /// <para>
    /// ✅ 用户写的效果「<b>+5% 阅读速度</b>」<b>已落地</b>（它当年就是"最容易接的一条"，也确实是第一条被接上的）：
    /// 效果挂在消费层 <c>ApparelCatalog.ApparelDef.Effects</c>（<c>EquipEffect.ReadingSpeed(0.05)</c>，
    /// <b>不进本引擎的 <see cref="ArmorLayer"/></b>），经 <c>ApparelCatalog.ApparelEffectMultiplier</c>
    /// 从真实穿戴品名乘算汇总，由 <c>Pawn</c> 喂进 <c>ReadingSpeed.Effective</c> 的 apparelMult（×1.05，乘算）。
    /// <b>它是本作第一件"穿戴 → 能力"供数的装备</b>。
    /// <para>⚠️ 与 <see cref="Sunglasses"/> 的「白天视野」不同 —— <b>那条仍未做</b>（视野是另一条链，待单独立项）。</para>
    /// </para>
    /// </summary>
    public static ArmorLayer PlainGlasses() => Cfg("plain_glasses");

    /// <summary>
    /// [T71] <b>自制简易墨镜</b>（眼镜槽，护双眼）—— 锐 12 / 钝 6、0.1kg。用户 authored（数值表『护甲表』new_armor_2）。
    /// <para>
    /// 🔴 <b>它是<see cref="Sunglasses"/>「墨镜」的可制作对应物，不与之冲突</b>：墨镜要磨一片光学镜片（工业活）⇒
    /// **故意不可造、只能搜刮**；而这件是<b>木制眼罩，只在木片上留两条缝进光</b>（因纽特式雪镜）——一个拿骨头缝甲的人
    /// 削得出来 ⇒ 它<b>有配方</b>（读《尖峰时刻》解锁，见 <c>RecipeBook</c> 的 <c>snow_goggles</c>）。两者同占眼镜槽、互斥。
    /// </para>
    /// <para>
    /// ⚠️ 用户写的效果「<b>白天 +5% 视野范围</b>」是<b>引擎新轴</b>（视野系数目前不吃穿戴品）⇒ <b>效果未做</b>，
    /// 与墨镜/平光眼镜的两条挂起效果同批统一立项（同 <see cref="Sunglasses"/> 注释口径）。在它落地前，这件的<b>实打实</b>
    /// 价值就是那 12/6 的护眼——比 1/1 的墨镜硬得多，代价是同样要放弃眼镜槽上的头盔/面具。
    /// </para>
    /// </summary>
    public static ArmorLayer SelfMadeSnowGoggles() => Cfg("self_made_snow_goggles");

    /// <summary>
    /// [T72] <b>护踝鞋具</b>（成对·脚槽，护小腿+脚含趾）—— 锐 12 / 钝 6、0.75kg。用户 authored（数值表『护甲表』new_armor_2）。
    /// <para>
    /// <b>成对品</b>（同运动鞋/劳保手套，[SPEC-B18-补]）：物品定义不分左右，一件<b>只占一只脚槽</b>、护那一侧的
    /// 小腿+脚（含五趾）——双侧要<b>两件</b>。与运动鞋同占脚槽、互斥（穿了护踝就穿不了运动鞋）。
    /// </para>
    /// <para>
    /// CoversParts 为两侧合计（表口径/UI）：小腿子树天然含脚+趾（脚挂小腿下，见 <see cref="HumanBody"/> 层级），
    /// 故 <c>SubtreeNames(LeftCalf, RightCalf)</c> 即『左右小腿+左右脚+十趾』。实际生效覆盖按装入那一侧裁剪
    /// （见 <c>ApparelCatalog.CoversFor</c>）。
    /// </para>
    /// </summary>
    public static ArmorLayer AnkleGuard() => Cfg("ankle_guard");

    /// <summary>
    /// [警察局] <b>防弹背心</b>（<b>贴身层</b>·护胸 + 腹）—— 锐 24 / 钝 6、2.5kg。用户 authored（新探索关「警察局」掉落）。
    /// <para>
    /// 🔴 <b>它是贴身层（<see cref="ArmorSlot.Skin"/>），不是装甲层</b>——这一点是刻意的：抗弹背心贴身穿，
    /// 占的是<b>贴身层槽</b>（EquipSlot.SkinLayer，与长袖布衣/花衬衫/粗布衬衫互斥），因此<b>能与皮甲/板甲等
    /// 装甲层护甲叠穿</b>（防弹背心打底 + 板甲罩外）。它<b>不</b>与装甲层三件互斥——别把它当成"再抢一个装甲层名额"。
    /// </para>
    /// <para>
    /// Wiki 当前数值是锐 24 / 钝 6；另有「对子弹获得额外 30 锐器防御」备注，后者仍是 Wiki 备注，未接入统一伤害类型规则。
    /// </para>
    /// <para>
    /// ⚠️ 对 Sim <b>结构性零漂移</b>：Sim 的护甲套是 <c>Program.cs</c> 里逐条点名的具名组合，<b>按名点菜、不遍历本表</b>
    /// ⇒ 本工厂进不了 <c>Duel</c> 结算路径，既有基线一个字节不动（护栏见 <c>NewEquipmentDataTests.BallisticVest_不进任何生成套_保Sim基线零漂移</c>）。
    /// </para>
    /// </summary>
    public static ArmorLayer BallisticVest() => Cfg("ballistic_vest");

    /// <summary>厚重裤子（裤装槽，护双大腿+双小腿）：双层加绒，数值来自 Wiki。</summary>
    public static ArmorLayer HeavyTrousers() => Cfg("heavy_trousers");

    /// <summary>厚重披风（只占装甲层，不占裤装槽；护胸腹、双臂、双大腿）：数值来自 Wiki。</summary>
    public static ArmorLayer HeavyCape() => Cfg("heavy_cape");

    /// <summary>雪地靴（成对·脚槽，护双脚及脚趾）：数值来自 Wiki。</summary>
    public static ArmorLayer SnowBoots() => Cfg("snow_boots");

    // ---- 生物·天生（不可穿戴）----

    /// <summary>丧尸：一层腐烂硬皮，覆盖全身（表『防护部位』= 全身 → CoversParts=null）。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => new[] { Cfg("zombie_hide") };

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
    public static ArmorLayer DogClothVest() => Cfg("dog_cloth_vest");

    /// <summary>皮制狗衣（身体·外套层，护胸+腹）。</summary>
    public static ArmorLayer DogLeatherVest() => Cfg("dog_leather_vest");

    /// <summary>
    /// 口袋狗衣（身体·贴身层，护胸+腹）：[SPEC-B18] 起<b>不再是无甲纯容器</b>——表给了 2/1 薄甲，
    /// 同时为狗提供 6kg 负重（容量常量在消费层 DogGearCatalog.PocketVestCapacity）。拿防护换驮运。
    /// </summary>
    public static ArmorLayer DogPocketVest() => Cfg("dog_pocket_vest");

    /// <summary>铁皮头甲（狗·头槽，仅护头）：刚性铁皮，防护高、较重。</summary>
    public static ArmorLayer DogIronHelmet() => Cfg("dog_iron_helmet");

    /// <summary>铁丝头甲（狗·头槽，仅护头）：铁丝编笼，轻便，锐防弱于铁皮、钝防持平。</summary>
    public static ArmorLayer DogWireHelmet() => Cfg("dog_wire_helmet");

    // ---- 玩家可见风味文案（黑色幽默）：护甲名 → 一行描述 ----
    // 由库存物品 UI 经 Item.Armor 自动填充展示，不参与战斗结算。表『说明』列即此文案（含狗装备）。

    /// <summary>
    /// 按护甲显示名取一行风味描述（查不到返回空串）。供消费层 Item.Armor 自动填充库存物品描述。
    /// <para>
    /// <b>[根治]</b> 直接从 <c>armor.json</c>（<see cref="ArmorConfig"/> 段）按 <see cref="ArmorLayer.Name"/>
    /// 取 <see cref="ArmorLayer.Description"/>——config 是护甲文案的<b>唯一权威源</b>。此前走一份手维护的
    /// <c>_flavorByName</c> 字典（只列了部分件），每加一件新护甲若忘补字典 ⇒ 库存 UI 描述<b>静默空白</b>；
    /// 现在无第三份会腐烂的源，config 里任何一件都自动可取。防腐护栏见
    /// <c>ArmorDescriptionCatalogTests.DescriptionOf_NonEmpty_ForEveryArmorInCatalog</c>（遍历 catalog 断言非空）。
    /// </para>
    /// </summary>
    public static string DescriptionOf(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }
        foreach (ArmorLayer layer in CombatCatalog.Section<ArmorConfig>().ById.Values)
        {
            if (layer.Name == name)
            {
                return layer.Description;
            }
        }
        return "";
    }
}
