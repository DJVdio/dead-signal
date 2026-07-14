using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.WikiExtract;

// ═══════════════════════════════════════════════════════════════════════════
// 「角色」与「角色数值」两个分区（impl-wiki-chars）。沿用 Program.cs 的 Category/Col 骨架，
// 网页侧一行都不用改：分区注册、筛选、排序、行内编辑、保存、头像位全是通用机制。
//
// ⚠️ 角色数据与物品表有一处**语义差异**，是本文件所有特殊处理的由来：
//   · 物品：C# 是唯一事实源，重跑抽取器"以代码为准"重写 JSON —— 天经地义。
//   · 角色：一半是代码派生数值（perk 阈值/系数/价率…），**另一半是 authored 剧情文本**
//     （山姆的祖母、克莉丝汀的过去、精英丧尸的来历）——**C# 代码里根本没有这些句子**，
//     它们只存在于用户手写的设计文档里。抽取器给的只是**种子**。
//
// ⇒ 由此定下「**表赢代码**」的回写安全规则：用户在网页上改完的背景故事，不该被下一次重跑静默删掉。
//   该规则最初为本分区而写，现已由 impl-wiki 提升成**全分区通用**的 <see cref="TableMerge"/>（连墓碑式软删除一并补齐），
//   本文件只管**播种**，合并/漂移/待同步标记全部交给它。要从代码强制重建：`-- --reset`。
// ═══════════════════════════════════════════════════════════════════════════

internal static class Characters
{
    private const string Survivor = "幸存者";
    private const string DogKind = "犬类";
    private const string MerchantKind = "中立商人";
    private const string StoryChar = "剧情角色";
    private const string Hostile = "敌对";
    private const string EliteZombie = "精英丧尸";

    /// <summary>百分比：0.10 → 10（表里给用户看的是 10%，不是 0.1）。</summary>
    private static double Pct(double ratio) => Math.Round(ratio * 100, 4);

    // ─────────────────────────── 分区①：角色 ───────────────────────────

    internal static Category Roster()
    {
        var cols = new List<Col>
        {
            new("name", "名字", Primary: true),
            new("category", "分类", "chip"),
            new("faction", "阵营", "chip"),
            new("tagline", "一句话", "longtext"),
            new("perkName", "专属效果", Hint: "本作没有通用技能——能力只由 authored 专属效果 + 读过的书承载"),
            new("perkAxis", "怎么升级", "longtext", Hint: "每个角色的升级条件都不一样，是手写的"),
            new("perkL1", "1 级", "longtext"),
            new("perkL2", "2 级", "longtext"),
            new("perkL3", "3 级", "longtext"),
            new("join", "怎么加入", "longtext"),
            new("gear", "自带装备", "longtext"),
            new("backstory", "背景故事", "longtext", Hint: "authored 手写剧情——系统只按条件播放，不做程序化引申"),
            new("relations", "关系", "longtext", Hint: "手写剧情关系，没有好感度/关系值系统"),
            new("storyline", "支线与剧情", "longtext"),
            new("notes", "备注", "longtext"),
            new("draft", "内容待定稿", "bool", Hint: "勾上 = 文本/设定还是草稿，等你定"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        rows.Add(Sam());
        rows.Add(Nordi());
        rows.Add(Doug());
        rows.Add(Nightingale());
        rows.Add(Christine());
        rows.Add(Bruce());
        rows.AddRange(Merchants());
        rows.AddRange(StoryCorpses());
        rows.AddRange(Hostiles());
        rows.AddRange(Elites());

        return new Category("characters", "角色",
            "godot/scripts/SurvivorPerks.cs · SurvivorBackstory.cs · DougBruceBond.cs · NurseRecruit.cs · MerchantLineage.cs · src/DeadSignal.Combat/ZombieOutfit.cs",
            "全程登场 10~12 名手写幸存者，同时在营 ≤8 人；全员永久死亡、无主角。"
            + "剧情、性格、关系全是手写的——系统只提供「按条件播放」的框架，不生成角色档案，也没有好感度数值。"
            + "角色的数字在隔壁「角色数值」分区。",
            cols, rows);
    }

    // ─────────────────────────── 分区②：角色数值 ───────────────────────────

    internal static Category Stats()
    {
        var cols = new List<Col>
        {
            new("label", "数值项", Primary: true),
            new("who", "角色", "chip"),
            new("value", "值", "number"),
            new("unit", "单位", ReadOnly: true),
            new("settled", "已拍板", "bool", Hint: "勾上 = 你定过的值，别当「拟定待调」随手改；空 = 拟定待调"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        return new Category("character-stats", "角色数值",
            "godot/scripts/SurvivorPerks.cs 等（每行的「代码位置」列写明了各自的出处）",
            "角色身上所有能调的数字，一行一个。改完这里，agent 会照「代码位置」把它同步进代码。"
            + "「已拍板」打勾的是你亲口定过的（比如山姆 3 人升 2 级、护士手术点 15→30），不是可以随手调的草稿值。",
            cols, StatRows());
    }

    private static List<Dictionary<string, object?>> StatRows()
    {
        var rows = new List<Dictionary<string, object?>>();

        void Add(string who, string key, string label, double value, string unit, string anchor, bool settled = false)
            => rows.Add(new Dictionary<string, object?>
            {
                ["label"] = label,
                ["who"] = who,
                ["value"] = Math.Round(value, 4),
                ["unit"] = unit,
                ["settled"] = settled,
                ["_id"] = key,
                ["_anchor"] = anchor,
                // 一行是「一个数字」，不是一件东西——**没有图标**。显式置空，否则 Program.cs 会给它派生一个图标位，
                // 把 52 个永远不该存在的文件名塞进 icon-manifest.json（那是给美术侧的"要画哪些图"契约，不能污染）。
                ["_icon"] = "",
            });

        // —— 山姆·英雄风范（数值为用户原话拍板，非拟定）——
        const string samSrc = "godot/scripts/SurvivorPerks.cs :: SamPerk";
        Add("山姆", "sam_l2_pop", "升 2 级所需营地人数", SamPerk.Level2CampPopulation, "人", samSrc + ".Level2CampPopulation", settled: true);
        Add("山姆", "sam_l3_pop", "升 3 级所需营地人数", SamPerk.Level3CampPopulation, "人", samSrc + ".Level3CampPopulation", settled: true);
        Add("山姆", "sam_l1_damage_reduction", "1 级 自身减伤", Pct(SamPerk.Level1DamageReduction), "%", samSrc + ".Level1DamageReduction", settled: true);
        Add("山姆", "sam_l2_carry", "2 级 自身负重上限加成", Pct(SamPerk.Level2CarryBonus), "%", samSrc + ".Level2CarryBonus", settled: true);
        Add("山姆", "sam_aura_carry", "3 级光环 全营负重上限", Pct(SamPerk.AuraCarryBonus), "%", samSrc + ".AuraCarryBonus", settled: true);
        Add("山姆", "sam_aura_work", "3 级光环 全营干活效率", Pct(SamPerk.AuraWorkSpeedBonus), "%", samSrc + ".AuraWorkSpeedBonus", settled: true);
        Add("山姆", "sam_aura_heal", "3 级光环 全营恢复速度", Pct(SamPerk.AuraHealSpeedBonus), "%", samSrc + ".AuraHealSpeedBonus", settled: true);
        Add("山姆", "sam_aura_infection", "3 级光环 全营感染恶化减缓", Pct(SamPerk.AuraInfectionWorsenReduction), "%", samSrc + ".AuraInfectionWorsenReduction", settled: true);
        Add("山姆", "sam_operation", "开局操作能力（缺两指的代价）", Pct(SamOperationCapability()), "%",
            "src/DeadSignal.Combat/Body.cs :: FingerPenalty（−7%/指·该手累加）+ SurvivorBackstory.SeveredAtStart", settled: true);

        // —— 诺蒂·书虫 ——
        const string bookSrc = "godot/scripts/SurvivorPerks.cs :: BookwormPerk";
        Add("诺蒂", "nordi_l2_hours", "升 2 级所需累计阅读", BookwormPerk.Level2ThresholdHours, "小时", bookSrc + ".Level2ThresholdHours");
        Add("诺蒂", "nordi_l3_hours", "升 3 级所需累计阅读", BookwormPerk.Level3ThresholdHours, "小时", bookSrc + ".Level3ThresholdHours");
        Add("诺蒂", "nordi_l1_read", "1 级 自身读速加成", Pct(BookwormPerk.BonusForLevel(1)), "%", bookSrc + ".BonusForLevel");
        Add("诺蒂", "nordi_l2_read", "2 级 自身读速加成", Pct(BookwormPerk.BonusForLevel(2)), "%", bookSrc + ".BonusForLevel");
        Add("诺蒂", "nordi_l3_read", "3 级 自身读速加成", Pct(BookwormPerk.BonusForLevel(3)), "%", bookSrc + ".BonusForLevel");
        Add("诺蒂", "nordi_l3_campwide", "3 级 全营读速加成", Pct(BookwormPerk.CampWideBonusAtMax), "%", bookSrc + ".CampWideBonusAtMax");
        Add("诺蒂", "read_no_seat", "没座位读书的速度", Pct(ReadingSpeed.NoSeatMultiplier), "%", "godot/scripts/SurvivorPerks.cs :: ReadingSpeed.NoSeatMultiplier");

        // —— 道格 & 布鲁斯·人狗羁绊 ——
        const string bondSrc = "godot/scripts/DougBruceBond.cs :: DougBruceBond";
        Add("道格", "doug_l2_days", "升 2 级所需共同存活", DougBruceBond.Level2Days, "天", bondSrc + ".Level2Days");
        Add("道格", "doug_l3_days", "升 3 级所需共同存活", DougBruceBond.Level3Days, "天", bondSrc + ".Level3Days");
        Add("道格", "doug_angle", "1 级 道格视野角加成", Pct(DougBruceBond.DougAngleBonusMult - 1), "%", bondSrc + ".DougAngleBonusMult");
        Add("道格", "bruce_angle", "1 级 布鲁斯视野角加成", Pct(DougBruceBond.BruceAngleBonusMult - 1), "%", bondSrc + ".BruceAngleBonusMult");
        Add("道格", "bruce_range", "2 级 布鲁斯视距加成", Pct(DougBruceBond.BruceRangeBonusMult - 1), "%", bondSrc + ".BruceRangeBonusMult");
        Add("道格", "bond_aura_production", "3 级光环 生产效率", Pct(DougBruceBond.AuraProductionMult - 1), "%", bondSrc + ".AuraProductionMult");
        Add("道格", "bond_aura_damage", "3 级光环 受伤减免", Pct(1 - DougBruceBond.AuraDamageTakenMult), "%", bondSrc + ".AuraDamageTakenMult");
        Add("道格", "bond_aura_radius", "3 级光环 生效半径", DougBruceBond.DefaultAuraRadius, "像素", bondSrc + ".DefaultAuraRadius");
        Add("道格", "village_siege_zombies", "救援点围困丧尸数", VillageRescue.SiegeZombieCount, "只", "godot/scripts/VillageRescue.cs :: SiegeZombieCount");
        Add("道格", "village_bark_radius", "布鲁斯吠叫引路半径", VillageRescue.BarkTriggerRadius, "像素", "godot/scripts/VillageRescue.cs :: BarkTriggerRadius");

        // —— 布鲁斯（狗）——
        Add("布鲁斯", "bruce_guard_efficiency", "站岗效率（相对人类）", Pct(DougBruceBond.BruceGuardEfficiency), "%", bondSrc + ".BruceGuardEfficiency");
        Add("布鲁斯", "dog_gear_unlock_level", "解锁狗装备所需羁绊等级", DougBruceBond.DogGearUnlockLevel, "级", bondSrc + ".DogGearUnlockLevel", settled: true);
        Add("布鲁斯", "dog_hunger_cap", "饥饿上限", DogHungerState.Cap, "刻", "godot/scripts/DogHungerState.cs :: Cap");
        Add("布鲁斯", "dog_hunger_eat", "吃一份食物回复（人只回 1）", DogHungerState.EatGain, "刻", "godot/scripts/DogHungerState.cs :: EatGain", settled: true);
        Add("布鲁斯", "dog_hunger_drain", "每个聚餐相位消耗", 1, "刻", "godot/scripts/DogHungerState.cs :: ResolvePhase", settled: true);

        // —— 南丁格尔·医疗特长（数值为用户原话拍板，非拟定）——
        const string nurseSrc = "godot/scripts/SurvivorPerks.cs :: NightingalePerk";
        Add("南丁格尔", "nurse_l2_surgeries", "升 2 级所需手术台数", NightingalePerk.Level2ThresholdSurgeries, "台", nurseSrc + ".Level2ThresholdSurgeries");
        Add("南丁格尔", "nurse_l3_surgeries", "升 3 级所需手术台数", NightingalePerk.Level3ThresholdSurgeries, "台", nurseSrc + ".Level3ThresholdSurgeries");
        Add("南丁格尔", "surgery_base_default", "常人的手术基础点数", NightingalePerk.DefaultSurgeryBasePoints, "点", nurseSrc + ".DefaultSurgeryBasePoints", settled: true);
        Add("南丁格尔", "surgery_base_nurse", "1 级 她本人的手术基础点数", NightingalePerk.NightingaleSurgeryBasePoints, "点", nurseSrc + ".NightingaleSurgeryBasePoints", settled: true);
        Add("南丁格尔", "surgery_base_camp_bonus", "3 级 全营手术基础点加成（永续）", NightingalePerk.CampSurgeryBaseBonus, "点", nurseSrc + ".CampSurgeryBaseBonus", settled: true);
        Add("南丁格尔", "nurse_l2_infection", "2 级 全营感染率降低", Pct(NightingalePerk.Level2InfectionReduction), "%", nurseSrc + ".Level2InfectionReduction", settled: true);
        Add("南丁格尔", "nurse_l3_infection", "3 级 全营感染率再降低（永续）", Pct(NightingalePerk.Level3InfectionReduction), "%", nurseSrc + ".Level3InfectionReduction", settled: true);

        // —— 克莉丝汀 ——
        Add("克莉丝汀", "christine_declines", "累计几次「暂不」后她离开", ChristineRequestLogic.DeclinesToLeave, "次", "godot/scripts/ChristineRequestLogic.cs :: DeclinesToLeave");
        Add("克莉丝汀", "christine_raider_wounded", "劫掠者掉血到多少触发她反水", Pct(TutorialRaidLogic.RaiderWoundedThreshold), "%血量", "godot/scripts/TutorialRaidLogic.cs :: RaiderWoundedThreshold");
        Add("克莉丝汀", "christine_self_hurt", "她自己掉血到多少触发反水", Pct(TutorialRaidLogic.ChristineHurtThreshold), "%血量", "godot/scripts/TutorialRaidLogic.cs :: ChristineHurtThreshold");

        // —— 神秘商人（两位商人共用同一套价率与调度）——
        var sched = new MerchantSchedule(new SystemRandomSource(), currentDay: 0);
        Add("神秘商人", "merchant_buy_rate", "玩家买入价（占基准价）", MerchantTrade.BuyRatePercent, "%", "godot/scripts/MerchantTrade.cs :: BuyRatePercent", settled: true);
        Add("神秘商人", "merchant_sell_rate", "玩家卖出价（占基准价）", MerchantTrade.SellRatePercent, "%", "godot/scripts/MerchantTrade.cs :: SellRatePercent", settled: true);
        Add("神秘商人", "merchant_gap_min", "来访间隔下限", sched.MinGap, "天", "godot/scripts/MerchantSchedule.cs :: 构造默认 minGap", settled: true);
        Add("神秘商人", "merchant_gap_max", "来访间隔上限", sched.MaxGap, "天", "godot/scripts/MerchantSchedule.cs :: 构造默认 maxGap", settled: true);

        // —— 敌对 ——
        double dressed = ZombieOutfit.Presets.Where(p => p.Clothes().Count > 0).Sum(p => p.Weight);
        Add("丧尸", "zombie_dressed_rate", "至少还穿着一件的比例", Pct(dressed), "%", "src/DeadSignal.Combat/ZombieOutfit.cs :: Presets（权重和）", settled: true);
        Add("丧尸", "zombie_outfit_count", "日常着装预设套数", ZombieOutfit.Presets.Count, "套", "src/DeadSignal.Combat/ZombieOutfit.cs :: Presets", settled: true);
        Add("超市的「幸存者」", "supermarket_ambushers", "背刺围攻人数", SupermarketAmbush.AmbushRaiderCount, "名", "godot/scripts/SupermarketAmbush.cs :: AmbushRaiderCount", settled: true);

        foreach (ZombieOutfitPreset p in ZombieOutfit.ElitePresets)
        {
            Add(p.Name, "elite_weight_" + EliteId(p.Name), "随机抽取权重（0 = 永不随机出现）", p.Weight, "",
                "src/DeadSignal.Combat/ZombieOutfit.cs :: ElitePresets", settled: true);
            Add(p.Name, "elite_layers_" + EliteId(p.Name), "身上的衣物/护甲件数", p.Clothes().Count, "件",
                "src/DeadSignal.Combat/ZombieOutfit.cs :: ElitePresets", settled: true);
        }

        return rows;
    }

    // ─────────────────────────── 各角色 ───────────────────────────

    /// <summary>
    /// 山姆开局的操作能力：**不自己算**——照 <c>Pawn.Create</c> 的路径造一具身体、应用 authored 痕迹，再问引擎要净惩罚。
    /// （引擎里手指惩罚是"该手累加"的 −7%/指 ⇒ 两指 = 0.86。自己按乘算推会得出 0.8649 的假值。）
    /// </summary>
    private static double SamOperationCapability()
    {
        Body body = CombatData.NewHumanoidBody();
        SurvivorBackstory.ApplyTo(SurvivorBackstory.Sam, body);
        return 1 - body.DisabilityModifiers.OperationPenalty;
    }

    private static Dictionary<string, object?> Sam()
    {
        IReadOnlyList<string> severed = SurvivorBackstory.SeveredAtStart(SurvivorBackstory.Sam);

        return new Dictionary<string, object?>
        {
            ["name"] = SamPerk.SamName,
            ["category"] = Survivor,
            ["faction"] = "幸存者",
            ["tagline"] = "人称「小英雄」。营地的主人，也是那个把祖母拖到门外的人。",
            ["perkName"] = "英雄风范",
            ["perkAxis"] = "营地人数（花名册里活着的人，含当天出门探索的队员；狗不算人）。"
                           + "这是全系统唯一会倒退的效果——人少了，等级就掉回去。护得住多少人，就有多强。",
            ["perkL1"] = $"入队即得。他从小身强体壮、性格坚韧，比常人耐揍——自身受到的伤害 −{Pct(SamPerk.Level1DamageReduction)}%"
                         + "（在护甲挡完之后再减，被甲完全挡下时依然是 0）。",
            ["perkL2"] = $"营地 {SamPerk.Level2CampPopulation} 人。从小吃苦耐劳帮祖母打理农庄——自身负重上限 ×{1 + SamPerk.Level2CarryBonus:0.##}（1 级效果保留）。",
            ["perkL3"] = $"营地 {SamPerk.Level3CampPopulation} 人。他散发英雄风范、影响周边的人——只要他还活着，全营（含他自己）四项："
                         + $"干活效率 ×{1 + SamPerk.AuraWorkSpeedBonus:0.##}、负重上限 ×{1 + SamPerk.AuraCarryBonus:0.##}、"
                         + $"恢复速度 ×{1 + SamPerk.AuraHealSpeedBonus:0.##}、感染恶化 ×{1 - SamPerk.AuraInfectionWorsenReduction:0.##}。"
                         + "四项一律乘算——0 × 1.03 还是 0，断了双手的人，光环补不回来。",
            ["join"] = "开局就在营地（另一位是诺蒂）。",
            ["gear"] = "手枪 + 开局三件套（长袖布衣 / 长裤 / 一双运动鞋）",
            ["backstory"] =
                "祖父牺牲于反叛战争，家里的庄园因此得名「英雄庄园」。后来父亲去守边疆，再也没有回来；母亲赶往疫情隔离区，"
                + "也再也没有回来——于是他被称作英雄的后代。不久后，一个据说是父亲战友的儿子的同龄人（诺蒂）被祖母领了回来，"
                + "他们亲如兄弟。九岁那年，一条发疯的野狗扑上来撕咬诺蒂，山姆冲上去救下了他，"
                + "但失去了左手的小拇指和无名指——人们从此叫他「小英雄」。\n\n"
                + "今年他 25 岁，灾难爆发。他被迫杀死了尸变的祖母，把英雄庄园改名为「幸存者营地」。",
            ["relations"] = "诺蒂是他的义兄弟，亲如兄弟。（手写剧情关系，没有好感度数值。）",
            ["storyline"] = "祖母的尸体就躺在住宅南门外的空地上——开局出生点几步之遥，第一分钟就看得见。"
                            + "那是营地里唯一一处叙事调查点：第一次点开只弹叙事、不动她身上任何东西；看过之后再点，才当作可搜刮容器——"
                            + "那件花衬衫要不要扒，由玩家自己决定。",
            ["notes"] = $"开局左手缺 {severed.Count} 指（{string.Join("、", severed)}），操作能力 {Pct(SamOperationCapability())}%。"
                        + "走引擎既有的切除通则（−7%/指），不为他豁免、也不额外加惩罚。"
                        + "这是「小英雄」称号看得见的代价，是有意为之的设定，不是待平衡的问题。",
            ["draft"] = false,
            ["_id"] = "sam",
            ["_anchor"] = "godot/scripts/SurvivorPerks.cs :: SamPerk + SurvivorBackstory.cs + godot/data/camp.json（spawns）",
        };
    }

    private static Dictionary<string, object?> Nordi() => new()
    {
        ["name"] = SurvivorBackstory.Nordi,
        ["category"] = Survivor,
        ["faction"] = "幸存者",
        ["tagline"] = "书虫。营地里唯一一个还在读书的人。",
        ["perkName"] = "书虫",
        ["perkAxis"] = "累计阅读时间（游戏内小时）。只增不减。",
        ["perkL1"] = $"入队即得。自身阅读速度 +{Pct(BookwormPerk.BonusForLevel(1))}%。",
        ["perkL2"] = $"累计读满 {BookwormPerk.Level2ThresholdHours} 小时。自身阅读速度 +{Pct(BookwormPerk.BonusForLevel(2))}%。",
        ["perkL3"] = $"累计读满 {BookwormPerk.Level3ThresholdHours} 小时。自身阅读速度 +{Pct(BookwormPerk.BonusForLevel(3))}%，"
                     + $"且全营所有人阅读速度 +{Pct(BookwormPerk.CampWideBonusAtMax)}%（含他自己 ⇒ 他自己合计 +{Pct(BookwormPerk.BonusForLevel(3) + BookwormPerk.CampWideBonusAtMax)}%）。"
                     + "3 级的升级点在全营加成，不是自身再涨。",
        ["join"] = "开局就在营地（另一位是山姆）。",
        ["gear"] = "匕首 + 开局三件套（长袖布衣 / 长裤 / 一双运动鞋）",
        ["backstory"] = "据说是山姆父亲战友的儿子，被山姆的祖母领了回来，与山姆同龄，两人亲如兄弟。"
                        + "九岁那年他被一条发疯的野狗扑倒撕咬，山姆冲上来救下了他——代价是山姆左手的两根手指。\n\n"
                        + "诺蒂是男的。",
        ["relations"] = "山姆的义兄弟。（手写剧情关系，没有好感度数值。）",
        ["storyline"] = "是他发现电台只能收、不能发——主线由此开始。聚餐时他会提营地需求（想要书柜、书桌、沙发）。",
        ["notes"] = "",
        ["draft"] = false,
        ["_id"] = "nordi",
        ["_anchor"] = "godot/scripts/SurvivorPerks.cs :: BookwormPerk + godot/data/camp.json（spawns）",
    };

    private static Dictionary<string, object?> Doug() => new()
    {
        ["name"] = "道格",
        ["category"] = Survivor,
        ["faction"] = "幸存者",
        ["tagline"] = "带着一条叫布鲁斯的狗。一人一狗，是一组。",
        ["perkName"] = "人狗羁绊",
        ["perkAxis"] = "道格与布鲁斯共同活着的天数。一方死了，计时中断、等级冻结，靠伙伴的那部分效果失效。",
        ["perkL1"] = $"入队即得。道格视野角度比常人宽 +{Pct(DougBruceBond.DougAngleBonusMult - 1)}%；"
                     + $"布鲁斯在他带领下视野角度也 +{Pct(DougBruceBond.BruceAngleBonusMult - 1)}%——"
                     + "布鲁斯这份是道格带出来的，道格一死就没了。",
        ["perkL2"] = $"共同活过 {DougBruceBond.Level2Days} 天。布鲁斯视野距离 +{Pct(DougBruceBond.BruceRangeBonusMult - 1)}%；"
                     + "解锁道格给布鲁斯做狗装备（五件套）。",
        ["perkL3"] = $"共同活过 {DougBruceBond.Level3Days} 天。相依为命光环（两个在 {DougBruceBond.DefaultAuraRadius:0} 距离内时生效）："
                     + $"生产效率 ×{DougBruceBond.AuraProductionMult:0.##}、受到伤害 ×{DougBruceBond.AuraDamageTakenMult:0.##}。"
                     + "一方死亡即永久失去。",
        ["join"] = $"「{VillageRescue.DestinationName}」大调查点的一段救援：他和布鲁斯被困在一间上锁、被丧尸围困的屋子里，"
                   + "道格已经饿到昏迷。调查团靠近到中距离时，布鲁斯开始吠叫引路——清掉或绕开围困的丧尸、踏进去解救，"
                   + "回营时两个才正式入队（道格昏迷，当场没法作战）。入队时他饥饿被压到极低档，你得先把他喂回来。",
        ["gear"] = "棍棒（开局武器）+ 手枪",
        ["backstory"] = "（待你手写。道格昏迷的时候话极少。）",
        ["relations"] = "布鲁斯是他的狗，也是他的搭档。羁绊等级靠两个一起活下来的天数长出来。",
        ["storyline"] = "道格死 → 布鲁斯的视野加成失效。布鲁斯死 → 狗装备做不了了，3 级光环永久失去。",
        ["notes"] = "",
        ["draft"] = true,
        ["_id"] = "doug",
        ["_anchor"] = "godot/scripts/DougBruceBond.cs + VillageRescue.cs",
    };

    private static Dictionary<string, object?> Nightingale() => new()
    {
        ["name"] = NurseRecruit.NurseName,
        ["category"] = Survivor,
        ["faction"] = "幸存者",
        ["tagline"] = "护士。一个人守了那家药店快一个月。",
        ["perkName"] = "医疗特长",
        ["perkAxis"] = "她本人做过的手术台数（成功失败都算、重做每次都算）。她一死，计数自然冻结。",
        ["perkL1"] = $"入队即得。她本人的手术基础点数 {NightingalePerk.DefaultSurgeryBasePoints} → {NightingalePerk.NightingaleSurgeryBasePoints}"
                     + "（只有她主刀时才有，她死就没了）。",
        ["perkL2"] = $"她做满 {NightingalePerk.Level2ThresholdSurgeries} 台手术。卫生意识让床铺更干净——"
                     + $"全营感染率 −{Pct(NightingalePerk.Level2InfectionReduction)}%（要她在营活着才维持，不在营/死了就失效）。",
        ["perkL3"] = $"她做满 {NightingalePerk.Level3ThresholdSurgeries} 台手术。卫生意识深入人心——全营手术基础点 +{NightingalePerk.CampSurgeryBaseBonus}、"
                     + $"全营感染率再 −{Pct(NightingalePerk.Level3InfectionReduction)}%。"
                     + $"这是永续遗产：她死了、离开了，依旧生效（知识已经传下去了）。她还活着时与 2 级叠加，感染合计 −{Pct(NightingalePerk.Level2InfectionReduction + NightingalePerk.Level3InfectionReduction)}%。",
        ["join"] = $"在「{NurseRecruit.DisplayName}」探索时遇到她。她清醒、能说话（不像昏迷的道格）：弹一段招募对话。"
                   + "婉拒不会关门——你可以再来找她谈，直到她答应；答应后回营时正式入队。",
        ["gear"] = "匕首 + 开局三件套",
        ["backstory"] = "（待你手写。「南丁格尔」是占位名，等你改。）\n\n"
                        + "相遇时她说：「……你们不是那帮抢药的，也不是死人。」「这店我一个人守了快一个月了。」\n"
                        + "答应之后：「我叫南丁格尔——别笑，我妈起的。」「有伤员就交给我。我知道该怎么做。」",
        ["relations"] = "（待你手写。）",
        ["storyline"] = "她的 3 级是全游戏唯一的永续遗产——人没了，规矩留下了。",
        ["notes"] = "她这几个数字是你亲口定的，不是拟定待调，别当草稿随手改。"
                    + "她管的是「会不会感染」（预防），感染竞速的速度是另一条轴，两者不重复结算。",
        ["draft"] = true,
        ["_id"] = "nightingale",
        ["_anchor"] = "godot/scripts/SurvivorPerks.cs :: NightingalePerk + NurseRecruit.cs",
    };

    private static Dictionary<string, object?> Christine() => new()
    {
        ["name"] = "克莉丝汀",
        ["category"] = Survivor,
        ["faction"] = "劫掠者 → 幸存者",
        ["tagline"] = "混在劫掠者里的那个女人。「杀死这些劫掠者！我是好人！」",
        ["perkName"] = "",
        ["perkAxis"] = "",
        ["perkL1"] = "",
        ["perkL2"] = "",
        ["perkL3"] = "",
        ["join"] = "教学关（第 2 夜）：一伙劫掠者袭营，她混在里头，起手和劫掠者同阵营。"
                   + "战斗中任一劫掠者受伤较重、或她自己挂彩（谁先到算谁）→ 她喊出那句台词、阵中反水，"
                   + "转为自动战斗的盟友——但此时你操控不了她。战斗结束后你可以选收留 / 放逐 / 处决："
                   + "只有收留，她才变成你能操控的营地幸存者。",
        ["gear"] = "手枪",
        ["backstory"] = "她曾被金手指帮轮奸，好不容易逃出来，后来加入一个劫掠者团伙勉强活命。",
        ["relations"] = "（待你手写。）",
        ["storyline"] = $"请求清剿金手指帮（有时限的支线）：收留后她在聚餐里用气泡反复请求出兵，共 {ChristineRequestLogic.DeclinesToLeave} 次，"
                        + "每次那一餐结束后弹抉择面板（答应出兵 / 暂不）。答应 → 从此不再逼问；"
                        + $"累计 {ChristineRequestLogic.DeclinesToLeave} 次「暂不」→ 她在下一次昼夜交替时自己离开营地去复仇，"
                        + "日后你会在金手指帮根据地发现她的尸体。\n\n"
                        + "⚠️ 她在教学关里不受任何特殊保护——她可能当场战死；一旦战死，整条支线不触发。（黑暗向，有意为之。）",
        ["notes"] = "没有专属效果。反水阈值：任一劫掠者血量掉到 50% 以下，或她自己掉血（满血即触发）。",
        ["draft"] = true,
        ["_id"] = "christine",
        ["_anchor"] = "godot/scripts/ChristineRequestLogic.cs + TutorialRaidLogic.cs + GoldfingerDiscovery.cs",
    };

    private static Dictionary<string, object?> Bruce() => new()
    {
        ["name"] = "布鲁斯",
        ["category"] = DogKind,
        ["faction"] = "幸存者",
        ["tagline"] = "道格的狗。咬住了就不松口。",
        ["perkName"] = "（羁绊挂在道格身上）",
        ["perkAxis"] = "",
        ["perkL1"] = "",
        ["perkL2"] = "",
        ["perkL3"] = "",
        ["join"] = "跟着道格一起入队（南林村庄救援）。",
        ["gear"] = $"狗装备五件套——身体一件（{DogGearCatalog.ClothVestKey} / {DogGearCatalog.LeatherVestKey} / {DogGearCatalog.PocketVestKey}）"
                   + $"＋ 头一件（{DogGearCatalog.IronHelmetKey} / {DogGearCatalog.WireHelmetKey}），同槽互斥。"
                   + "要道格和他的羁绊到 2 级，才做得出来。",
        ["backstory"] = "（待你手写。）",
        ["relations"] = "道格的搭档。",
        ["storyline"] = "缠斗型特殊单元：高闪避、高移速、低伤害——难以独自杀死敌人，也难以被敌人短时间内杀死。"
                        + "他的定位是拖住敌人，不是斩杀：咬住并粘住目标，给道格创造安全的输出机会（一拖一打）。"
                        + "他会受伤，也会死。",
        ["notes"] = "狗不算人：不计入营地人数（山姆的升级轴看不见他）、吃饭不上桌（不占座、不产生聚餐气泡）、"
                    + "你不能直接给他下令（他是纯 AI 跟随/站岗，所以地图上也不给他画移动路径线）。"
                    + "但他比人耐饿——吃一份回 3 刻，人只回 1 刻。",
        ["draft"] = true,
        ["_id"] = "bruce",
        ["_anchor"] = "godot/scripts/Dog.cs + DogApparel.cs + DogHungerState.cs + DougBruceBond.cs",
    };

    private static IEnumerable<Dictionary<string, object?>> Merchants()
    {
        yield return new Dictionary<string, object?>
        {
            ["name"] = "神秘商人",
            ["category"] = MerchantKind,
            ["faction"] = "中立",
            ["tagline"] = "每隔几天来一趟，停在大门外。想做生意，你得开门。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。每隔几天自己来一趟，停在营地大门外。",
            ["gear"] = $"一个货架。你买他的东西按基准价 {MerchantTrade.BuyRatePercent}%，卖东西给他只给 {MerchantTrade.SellRatePercent}%。",
            ["backstory"] = "（待你手写。）",
            ["relations"] = "",
            ["storyline"] = "杀商人是有代价的：他若死在营地里，零掉落（杜绝杀商套利），然后由第二个商人接替。"
                            + "第二个再死在营地 → 今后永远没有商人了，这条经济线就此关闭。",
            ["notes"] = "⚠️ 硬约束：永远不要放开「闩着的门中立阵营也能开」——门外的陌生人凭什么抬得起你从里面插上的闩？"
                        + "而且中立是个开放阵营，日后随便加个中立 NPC 就自动获得了推开营地大门的权限。",
            ["draft"] = true,
            ["_id"] = "merchant_first",
            ["_anchor"] = "godot/scripts/MerchantTrade.cs + MerchantSchedule.cs + MerchantLineage.cs",
        };

        yield return new Dictionary<string, object?>
        {
            ["name"] = "第二商人",
            ["category"] = MerchantKind,
            ["faction"] = "中立",
            ["tagline"] = "他知道这个营地专屠商人。他还是来了。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。第一个商人死在营地之后，他接替上任。首次来访会说一段开场白（只说一次）。",
            ["gear"] = "同第一个商人（价率一样）。",
            ["backstory"] = MerchantLineage.SecondMerchantIntroLine + "\n\n（这段开场白是草稿，最终由你手写。）",
            ["relations"] = "",
            ["storyline"] = "他要是也死在营地里 → 今后永远没有商人了。",
            ["notes"] = "",
            ["draft"] = true,
            ["_id"] = "merchant_second",
            ["_anchor"] = "godot/scripts/MerchantLineage.cs :: SecondMerchantIntroLine",
        };
    }

    private static IEnumerable<Dictionary<string, object?>> StoryCorpses()
    {
        yield return new Dictionary<string, object?>
        {
            ["name"] = "哥顿",
            ["category"] = StoryChar,
            ["faction"] = "金手指帮·帮主（已死）",
            ["tagline"] = "金手指帮的帮主。在你到达之前，他早已把自己吊死在林子里。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。在「守林人小屋」后院的老树上发现他的上吊尸，旁边是日记 B。",
            ["gear"] = "日记 B（可以捡走，带回营地读）",
            ["backstory"] = "日记 B 揭示了三件事：金手指帮那套文化的起源；哥顿的身世——母亲极度强势，"
                            + "母亲丧尸化后，当着他的面杀死了他懦弱的父亲；以及他用暴戾与恐怖，掩盖自己的懦弱。\n\n"
                            + "最后他看透了自己的本性，发觉虐杀女性毫无意义，于是独自走进林子，自杀了。",
            ["relations"] = "",
            ["storyline"] = "他的尸体是 authored 剧情尸体——永远不会被尸体清理系统收走（收走了，那段剧情就没了）。",
            ["notes"] = "",
            ["draft"] = false,
            ["_id"] = "gordon",
            ["_anchor"] = "godot/scripts/GoldfingerDiscovery.cs :: GordonHangedId / DiaryBBookId",
        };

        yield return new Dictionary<string, object?>
        {
            ["name"] = "山姆的祖母",
            ["category"] = StoryChar,
            ["faction"] = "（已尸变，已被山姆杀死）",
            ["tagline"] = "穿着花衬衫和长裤，躺在住宅南门外的空地上。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。开局出生点几步之遥——第一分钟你就看得见她。",
            ["gear"] = "花衬衫（贴身层护甲，可以从她身上扒下来穿）。长裤留在她身上（和开局三件套重复，没有搜刮价值）。",
            ["backstory"] = "灾难爆发那天，山姆被迫杀死了尸变的祖母。他做完必须做的事，把她拖到了门外——因为屋里是她的屋子。\n\n"
                            + "然后他把「英雄庄园」改名为「幸存者营地」。",
            ["relations"] = "山姆的祖母。也是她把诺蒂领回了家。",
            ["storyline"] = "营地里唯一一处叙事调查点：第一次点开只弹叙事（4 屏，不走游戏时间，不动她身上任何东西）；"
                            + "看过之后再点，才当作可搜刮容器——那件花衬衫要不要扒，由玩家自己决定。",
            ["notes"] = "authored 剧情尸体——永远不会被尸体清理系统收走。叙事文本是草稿，等你定稿。",
            ["draft"] = true,
            ["_id"] = "grandmother",
            ["_anchor"] = "godot/scripts/NarrativeSpot.cs :: GrandmotherCorpseId + godot/data/camp.json（role=corpse）",
        };

        yield return new Dictionary<string, object?>
        {
            ["name"] = "被反杀的帮众",
            ["category"] = StoryChar,
            ["faction"] = "金手指帮（已死）",
            ["tagline"] = "克莉丝汀反杀的那一个。死状是利器近身反杀，现场有拼死反抗的痕迹。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。在金手指帮根据地发现他的尸体，旁边是日记 A。",
            ["gear"] = "日记 A（可以捡走）",
            ["backstory"] = "当年金手指帮对被掳的克莉丝汀「手软」，她趁隙反杀了其中一名帮众，其余帮众才把她彻底绑起来——就是这一个。\n\n"
                            + "日记 A 是帮众的自白：灾后两个普通帮众如何互助求生，如何参与对俘获女性的轮奸与折磨，"
                            + "以及成员视角的「金手指帮」命名由来。",
            ["relations"] = "",
            ["storyline"] = "这具尸体在克莉丝汀被绑走之前就已经存在，时间线上早于这次探索，所以不按她的去向门控——"
                            + "无论你是否收留她、她是否去复仇，你都能见到他（日记 A 因此在所有分支里都拿得到）。",
            ["notes"] = "authored 剧情尸体——永远不会被尸体清理系统收走。",
            ["draft"] = false,
            ["_id"] = "goldfinger_member",
            ["_anchor"] = "godot/scripts/GoldfingerDiscovery.cs :: GangMemberCorpseId / DiaryABookId",
        };
    }

    private static IEnumerable<Dictionary<string, object?>> Hostiles()
    {
        double dressed = ZombieOutfit.Presets.Where(p => p.Clothes().Count > 0).Sum(p => p.Weight);
        string pool = string.Join("、", ZombieOutfit.Presets.Select(p => $"{p.Name} {Pct(p.Weight)}%"));

        yield return new Dictionary<string, object?>
        {
            ["name"] = "丧尸",
            ["category"] = Hostile,
            ["faction"] = "丧尸",
            ["tagline"] = "穿着灾难发生那天身上的衣服。头和脚永远是光的。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。白天休眠，夜晚游荡。",
            ["gear"] = $"日常着装随机池（{ZombieOutfit.Presets.Count} 套，{Pct(dressed)}% 的丧尸身上至少还有一件）：{pool}。\n"
                       + "池子里一件护甲都没有——护甲只出现在你亲手设定的精英丧尸身上。",
            ["backstory"] = "腐皮本来就是烂肉——它的防护不来自皮，来自衣服。"
                            + "光靠腐皮，对全表任何武器都是 0% 阻挡（数学上恒等于零）。所以丧尸得穿衣服。",
            ["relations"] = "",
            ["storyline"] = "不会开门，只会砸门。除了视野，还有嗅觉兜底——闻得到近处的活人。",
            ["notes"] = "它们身上的衣服是重要的装备来源：搜刮尸体时逐件掷骰（软衣服 50%、刚性护甲件 90% 完好取下；腐皮永远掉不出来）。",
            ["draft"] = false,
            ["_id"] = "zombie",
            ["_anchor"] = "godot/scripts/Zombie.cs + src/DeadSignal.Combat/ZombieOutfit.cs :: Presets",
        };

        yield return new Dictionary<string, object?>
        {
            ["name"] = "劫掠者",
            ["category"] = Hostile,
            ["faction"] = "劫掠者",
            ["tagline"] = "会用掩体、会包抄、会撤退——也会安静地摸进你的营地。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。教学关（第 2 夜）来袭营；之后作为人类敌人出现。",
            ["gear"] = "手枪或匕首。打起来会掏火把照明。",
            ["backstory"] = "（待你手写。）",
            ["relations"] = "",
            ["storyline"] = "安静入侵：他们会撬锁、会轻手轻脚拆围栏，而不是一味砸门。"
                            + "（这是刻意设计的——只会砸门的劫掠者等于自己敲锣打鼓通知你，那样「不派守夜人」反而成了最优解。）",
            ["notes"] = "克莉丝汀在教学关里就是一个劫掠者实例，反水时运行时改阵营。",
            ["draft"] = true,
            ["_id"] = "raider",
            ["_anchor"] = "godot/scripts/Raider.cs + RaiderTactics.cs + IntrusionLogic.cs",
        };

        yield return new Dictionary<string, object?>
        {
            ["name"] = "超市的「幸存者」",
            ["category"] = Hostile,
            ["faction"] = "劫掠者（装成幸存者）",
            ["tagline"] = "他们朝你招手。跟过去，你就进了他们的口袋。",
            ["perkName"] = "",
            ["perkAxis"] = "",
            ["perkL1"] = "",
            ["perkL2"] = "",
            ["perkL3"] = "",
            ["join"] = "不可招募。走到超市据点门口就触发接触对话。",
            ["gear"] = "（同劫掠者）",
            ["backstory"] = "（待你手写。）",
            ["relations"] = "",
            ["storyline"] = $"骗局：轻信跟过去 → 进了内圈被背刺围攻（{SupermarketAmbush.AmbushRaiderCount} 名）；"
                            + "不轻信 → 他们转为占着内圈物资的敌对方，你踏进内圈就开战（公平战，谁都没先手）。"
                            + "两条路都通向打——区别只是谁先手。",
            ["notes"] = "",
            ["draft"] = true,
            ["_id"] = "supermarket_survivors",
            ["_anchor"] = "godot/scripts/SupermarketAmbush.cs",
        };
    }

    private static IEnumerable<Dictionary<string, object?>> Elites()
    {
        // 精英预设是 authored 具名内容：名字/装束一律问 ZombieOutfit.ElitePresets 要，不手抄。
        var flavor = new Dictionary<string, (string Tagline, string Story)>
        {
            ["防暴警察丧尸"] = (
                "从头到脚一处不露。全游戏最硬的一只东西——有意为之。",
                "板甲（全表最强）＋ 防暴头盔（护住头和整张脸）。能打的只剩耳朵、手（还戴着手套）、脚。\n\n"
                + "「爆头是精英的唯一软肋」这句话，到它这里为止。"),
            ["军人丧尸"] = (
                "军用头盔只护颅顶——脸是敞着的。",
                "皮甲 ＋ 皮夹克 ＋ 长袖布衣，上身三层叠满，但强度远低于板甲；腿上只有一条布裤。\n\n"
                + "眼睛、鼻子、下巴照打（挖眼致盲依然成立）。它比防暴警察好对付得多。"),
        };

        foreach (ZombieOutfitPreset preset in ZombieOutfit.ElitePresets)
        {
            flavor.TryGetValue(preset.Name, out var f);
            yield return new Dictionary<string, object?>
            {
                ["name"] = preset.Name,
                ["category"] = EliteZombie,
                ["faction"] = "丧尸",
                ["tagline"] = f.Tagline ?? "",
                ["perkName"] = "",
                ["perkAxis"] = "",
                ["perkL1"] = "",
                ["perkL2"] = "",
                ["perkL3"] = "",
                ["join"] = "不可招募。权重 0 = 永远不会被随机抽到——只能由关卡按名字点名摆放。",
                ["gear"] = string.Join(" ＋ ", preset.Clothes().Select(c => c.Name)) + "（＋ 腐皮，恒为最内层）",
                ["backstory"] = f.Story ?? "",
                ["relations"] = "",
                ["storyline"] = "生前的职业带着护甲一起变成了丧尸。这是你人为设定的高难度点——"
                                + "护甲件只在这条通路上出现，街上的普通丧尸一如既往地光着头。",
                ["notes"] = preset.IsDraft
                    ? "⚠️ 这套是样板草案，等你定稿——用的都是护甲表里已有的件，没新造装备。要加新的精英丧尸，在预设表里追加一条即可。"
                    : "",
                ["draft"] = preset.IsDraft,
                ["_id"] = EliteId(preset.Name),
                ["_anchor"] = "src/DeadSignal.Combat/ZombieOutfit.cs :: ElitePresets",
            };
        }
    }

    /// <summary>精英丧尸的内部 id：保持 ASCII（它要当头像文件名用）。表外的新预设按名字兜底成序号。</summary>
    private static string EliteId(string presetName) => presetName switch
    {
        "防暴警察丧尸" => "elite_riot_police",
        "军人丧尸" => "elite_soldier",
        _ => "elite_" + Math.Abs(presetName.GetHashCode() % 1000),
    };
}
