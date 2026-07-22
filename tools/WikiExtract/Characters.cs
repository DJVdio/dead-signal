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
        rows.Add(Pete());
        rows.Add(Rat());
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
            // 「值」列 = 某个设置对象（perks.json 为主，商人价率落 merchant.json）的一个字段（configScalar：cfg[_configId] 即值）。
            // 只有真外置进 config 的行带 _configId；道格/克莉丝汀/丧尸等仍是代码常量、无 _configId ⇒ wiki-serve 自动跳过、只当展示。
            new("value", "值", "number", ConfigScalar: true),
            // 「单位」原本是只读的，但它只是个说明性的标签（"%"、"天"、"游戏小时"…），没有引擎依据，
            // 也没有理由不让人改。用户要求全 wiki 可编辑 ⇒ 放开。
            new("unit", "单位", Hint: "这个数字的单位（%、天、小时…）。只是给人看的标签。"),
            new("settled", "已拍板", "bool", Hint: "勾上 = 你定过的值，别当「拟定待调」随手改；空 = 拟定待调"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        return new Category("character-stats", "角色数值",
            "godot/scripts/SurvivorPerks.cs 等（每行的「代码位置」列写明了各自的出处）",
            "角色身上所有能调的数字，一行一个。改完这里，agent 会照「代码位置」把它同步进代码。"
            + "「已拍板」打勾的是你亲口定过的（比如山姆 3 人升 2 级、护士手术点 15→30），不是可以随手调的草稿值。",
            // 表级 perks.json；商人两行用行级 _configFile 覆盖到 merchant.json（一表跨两源）。
            cols, StatRows(), ConfigFile: "perks.json");
    }

    private static List<Dictionary<string, object?>> StatRows()
    {
        var rows = new List<Dictionary<string, object?>>();

        // cfgKey 非空 ⇒ 这一行外置在 config（默认 perks.json，cfgFile 可覆盖到 merchant.json）：_configId=该字段名，
        // wiki-serve 按 configScalar 双向搬（cfg[cfgKey] 即值）。pct=true ⇒ 显示值是 config 分数 *100（双向 /100/*100）。
        // domain = 能力域（波2 页内分块 group）：战斗/生存/操作与生产/感知/经济/升级门槛/生成配置 七组之一。
        void Add(string who, string domain, string key, string label, double value, string unit, string anchor, bool settled = false,
                 string? cfgKey = null, bool pct = false, string? cfgFile = null)
        {
            var row = new Dictionary<string, object?>
            {
                ["group"] = domain,
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
            };
            if (cfgKey is not null) row["_configId"] = cfgKey;
            if (pct) row["_configPercent"] = true;
            if (cfgFile is not null) row["_configFile"] = cfgFile;
            rows.Add(row);
        }

        // —— 山姆·英雄风范（数值为用户原话拍板，非拟定）——
        const string samSrc = "godot/scripts/SurvivorPerks.cs :: SamPerk";
        Add("山姆", "升级门槛", "sam_l2_pop", "升 2 级所需营地人数", SamPerk.Level2CampPopulation, "人", samSrc + ".Level2CampPopulation", settled: true, cfgKey: "SamLevel2CampPopulation");
        Add("山姆", "升级门槛", "sam_l3_pop", "升 3 级所需营地人数", SamPerk.Level3CampPopulation, "人", samSrc + ".Level3CampPopulation", settled: true, cfgKey: "SamLevel3CampPopulation");
        Add("山姆", "战斗", "sam_l2_damage_reduction", "2 级 自身减伤", Pct(SamPerk.Level1DamageReduction), "%", samSrc + ".Level1DamageReduction", settled: true, cfgKey: "SamLevel1DamageReduction", pct: true);
        Add("山姆", "操作与生产", "sam_l1_carry", "1 级 自身负重上限加成", Pct(SamPerk.Level2CarryBonus), "%", samSrc + ".Level2CarryBonus", settled: true, cfgKey: "SamLevel2CarryBonus", pct: true);
        Add("山姆", "操作与生产", "sam_l1_operation_bonus", "1 级 自身操作能力加成", Pct(SamPerk.Level1OperationBonus), "%", samSrc + ".Level1OperationBonus", settled: true, cfgKey: "SamLevel1OperationBonus", pct: true);
        Add("山姆", "生存", "sam_l2_heal_speed", "2 级 自身恢复速度加成", Pct(SamPerk.Level2HealSpeedBonus), "%", samSrc + ".Level2HealSpeedBonus", settled: true, cfgKey: "SamLevel2HealSpeedBonus", pct: true);
        Add("山姆", "战斗", "sam_l3_concussion_reduction", "3 级 震荡概率降低", Pct(SamPerk.Level3ConcussionReduction), "%", samSrc + ".Level3ConcussionReduction", settled: true, cfgKey: "SamLevel3ConcussionReduction", pct: true);
        Add("山姆", "战斗", "sam_l3_fracture_penalty_reduction", "3 级 骨折惩罚减轻", Pct(SamPerk.Level3FracturePenaltyReduction), "%", samSrc + ".Level3FracturePenaltyReduction", settled: true, cfgKey: "SamLevel3FracturePenaltyReduction", pct: true);
        Add("山姆", "操作与生产", "sam_operation", "开局操作能力（缺两指的代价）", Pct(SamOperationCapability()), "%",
            "src/DeadSignal.Combat/Body.cs :: FingerPenalty（−7%/指·该手累加）+ SurvivorBackstory.SeveredAtStart", settled: true);

        // —— 诺蒂·书虫 ——
        const string bookSrc = "godot/scripts/SurvivorPerks.cs :: BookwormPerk";
        Add("诺蒂", "升级门槛", "nordi_l2_hours", "升 2 级所需累计阅读", BookwormPerk.Level2ThresholdHours, "小时", bookSrc + ".Level2ThresholdHours", cfgKey: "BookwormLevel2ThresholdHours");
        Add("诺蒂", "升级门槛", "nordi_l3_hours", "升 3 级所需累计阅读", BookwormPerk.Level3ThresholdHours, "小时", bookSrc + ".Level3ThresholdHours", cfgKey: "BookwormLevel3ThresholdHours");
        Add("诺蒂", "操作与生产", "nordi_l1_read", "1 级 自身读速加成", Pct(BookwormPerk.BonusForLevel(1)), "%", bookSrc + ".BonusForLevel", cfgKey: "BookwormSelfBonusL1", pct: true);
        // 🔴 L2、L3 自身读速**共用同一个 config 字段** BookwormSelfBonusL2Plus（BonusForLevel(2)==BonusForLevel(3)）。
        //    只把 L2 行接双向（写它即改该字段，L3 下次抽取自动跟着变）；L3 行不带 _configId，避免两行写同一字段互相覆盖。
        Add("诺蒂", "操作与生产", "nordi_l2_read", "2 级 自身读速加成", Pct(BookwormPerk.BonusForLevel(2)), "%", bookSrc + ".BonusForLevel", cfgKey: "BookwormSelfBonusL2Plus", pct: true);
        Add("诺蒂", "操作与生产", "nordi_l3_read", "3 级 自身读速加成", Pct(BookwormPerk.BonusForLevel(3)), "%", bookSrc + ".BonusForLevel（= L2 同一字段 BookwormSelfBonusL2Plus，改 L2 行即改它）");
        Add("诺蒂", "操作与生产", "nordi_l3_campwide", "3 级 全营读速加成", Pct(BookwormPerk.CampWideBonusAtMax), "%", bookSrc + ".CampWideBonusAtMax", cfgKey: "BookwormCampWideBonusAtMax", pct: true);
        // 🔴 「没座位读书 *0.9」**不是诺蒂的专属效果，是全员通则** —— 用户澄清，代码本来就是对的：
        //    `ReadingSpeed.Effective(..., hasSeat, ...)` 对**每一个** pawn 都算，没有任何按名/按 perk 的门控。
        //    把它列在诺蒂名下，会让人以为"没座位只影响她"，从而调错数值。
        //    已移到新的「全局规则」分区（Program.GlobalRules）。诺蒂的专属效果只有书虫那三条（读速/全营/升级轴）。

        // —— 道格 & 布鲁斯·人狗羁绊 ——
        const string bondSrc = "godot/scripts/DougBruceBond.cs :: DougBruceBond";
        Add("道格", "升级门槛", "doug_l2_days", "升 2 级所需共同存活", DougBruceBond.Level2Days, "天", bondSrc + ".Level2Days");
        Add("道格", "升级门槛", "doug_l3_days", "升 3 级所需共同存活", DougBruceBond.Level3Days, "天", bondSrc + ".Level3Days");
        Add("道格", "感知", "doug_angle", "1 级 道格视野角加成", Pct(DougBruceBond.DougAngleBonusMult - 1), "%", bondSrc + ".DougAngleBonusMult");
        Add("道格", "感知", "bruce_angle", "1 级 布鲁斯视野角加成", Pct(DougBruceBond.BruceAngleBonusMult - 1), "%", bondSrc + ".BruceAngleBonusMult");
        Add("道格", "感知", "bruce_range", "2 级 布鲁斯视距加成", Pct(DougBruceBond.BruceRangeBonusMult - 1), "%", bondSrc + ".BruceRangeBonusMult");
        Add("道格", "操作与生产", "bond_aura_operation", "3 级光环 操作能力", Pct(DougBruceBond.AuraOperationMult - 1), "%", bondSrc + ".AuraOperationMult");
        Add("道格", "生存", "bond_aura_damage", "3 级光环 受伤减免", Pct(1 - DougBruceBond.AuraDamageTakenMult), "%", bondSrc + ".AuraDamageTakenMult");
        Add("布鲁斯", "战斗", "bruce_attack_speed", "2 级 攻击速度加成", Pct(DougBruceBond.BruceAttackSpeedMult - 1), "%", bondSrc + ".BruceAttackSpeedMult");
        Add("布鲁斯", "生存", "bruce_move_speed", "2 级 移动速度加成", Pct(DougBruceBond.BruceMoveSpeedMult - 1), "%", bondSrc + ".BruceMoveSpeedMult");
        Add("道格", "操作与生产", "bond_aura_radius", "3 级光环 生效半径", DougBruceBond.DefaultAuraRadius, "像素", bondSrc + ".DefaultAuraRadius");
        Add("道格", "生成配置", "village_siege_zombies", "救援点围困丧尸数", VillageRescue.SiegeZombieCount, "只", "godot/scripts/VillageRescue.cs :: SiegeZombieCount");
        Add("道格", "感知", "village_bark_radius", "布鲁斯吠叫引路半径", VillageRescue.BarkTriggerRadius, "像素", "godot/scripts/VillageRescue.cs :: BarkTriggerRadius");

        // —— 布鲁斯（狗）——
        Add("布鲁斯", "战斗", "bruce_guard_efficiency", "站岗效率（相对人类）", Pct(DougBruceBond.BruceGuardEfficiency), "%", bondSrc + ".BruceGuardEfficiency");
        Add("布鲁斯", "升级门槛", "dog_gear_unlock_level", "解锁狗装备所需羁绊等级", DougBruceBond.DogGearUnlockLevel, "级", bondSrc + ".DogGearUnlockLevel", settled: true);
        Add("布鲁斯", "生存", "dog_hunger_cap", "饥饿上限", DogHungerState.Cap, "刻", "godot/scripts/DogHungerState.cs :: Cap");
        Add("布鲁斯", "生存", "dog_hunger_eat", "吃一份食物回复（人只回 1）", DogHungerState.EatGain, "刻", "godot/scripts/DogHungerState.cs :: EatGain", settled: true);
        Add("布鲁斯", "生存", "dog_hunger_drain", "每次聚餐消耗", 1, "刻", "godot/scripts/DogHungerState.cs :: ResolvePhase", settled: true);

        // —— 南丁格尔·医疗特长（数值为用户原话拍板，非拟定）——
        const string nurseSrc = "godot/scripts/SurvivorPerks.cs :: NightingalePerk";
        Add("南丁格尔", "升级门槛", "nurse_l2_surgeries", "升 2 级所需手术台数", NightingalePerk.Level2ThresholdSurgeries, "台", nurseSrc + ".Level2ThresholdSurgeries", cfgKey: "NightingaleLevel2ThresholdSurgeries");
        Add("南丁格尔", "升级门槛", "nurse_l3_surgeries", "升 3 级所需手术台数", NightingalePerk.Level3ThresholdSurgeries, "台", nurseSrc + ".Level3ThresholdSurgeries", cfgKey: "NightingaleLevel3ThresholdSurgeries");
        // 🔴 「常人的手术基础点数 = 15」**不是南丁格尔的特长，是所有人的基线**（人人自带 15 点，不看技能）。
        //    挂在她名下，会让人以为那 15 点也是她给的。她的特长是**她本人 30 点** + **3 级全营 +5**（下面两条）。
        //    已移到「全局规则」分区。
        Add("南丁格尔", "生存", "surgery_base_nurse", "1 级 她本人的手术基础点数", NightingalePerk.NightingaleSurgeryBasePoints, "点", nurseSrc + ".NightingaleSurgeryBasePoints", settled: true, cfgKey: "NightingaleSurgeryBasePoints");
        Add("南丁格尔", "生存", "surgery_base_camp_bonus", "3 级 全营手术基础点加成（永续）", NightingalePerk.CampSurgeryBaseBonus, "点", nurseSrc + ".CampSurgeryBaseBonus", settled: true, cfgKey: "NightingaleCampSurgeryBaseBonus");
        Add("南丁格尔", "生存", "nurse_l2_infection", "2 级 全营感染率降低", Pct(NightingalePerk.Level2InfectionReduction), "%", nurseSrc + ".Level2InfectionReduction", settled: true, cfgKey: "NightingaleLevel2InfectionReduction", pct: true);
        // config/runtime 以百分点存储（20），页面直接显示 20，不再经 `_configPercent` 放大（谢菲选秀：人话 20% 而不是 2000%）。
        Add("南丁格尔", "生存", "nurse_l2_bed_heal", "2 级 干净床铺恢复速度加成", NightingalePerk.Level2BedSleepHealBonusPct, "%", nurseSrc + ".Level2BedSleepHealBonusPct", settled: true, cfgKey: "NightingaleBedSleepHealBonusPct");
        Add("南丁格尔", "生存", "nurse_l3_infection", "3 级 全营感染率再降低（永续）", Pct(NightingalePerk.Level3InfectionReduction), "%", nurseSrc + ".Level3InfectionReduction", settled: true, cfgKey: "NightingaleLevel3InfectionReduction", pct: true);

        // —— 克莉丝汀 ——
        Add("克莉丝汀", "生成配置", "christine_declines", "累计几次「暂不」后她离开", ChristineRequestLogic.DeclinesToLeave, "次", "godot/scripts/ChristineRequestLogic.cs :: DeclinesToLeave");
        Add("克莉丝汀", "战斗", "christine_raider_wounded", "劫掠者掉血到多少触发她反水", Pct(TutorialRaidLogic.RaiderWoundedThreshold), "%血量", "godot/scripts/TutorialRaidLogic.cs :: RaiderWoundedThreshold");
        Add("克莉丝汀", "战斗", "christine_self_hurt", "她自己掉血到多少触发反水", Pct(TutorialRaidLogic.ChristineHurtThreshold), "%血量", "godot/scripts/TutorialRaidLogic.cs :: ChristineHurtThreshold");
        const string christineSrc = "godot/scripts/ChristinePerk.cs :: ChristinePerk";
        // WikiExtract 未 Link ChristinePerk.cs（它只为 CampMain/单测消费），这里直接读同一 perks.json 段，避免生成器另造常量。
        var perkConfig = GameConfigCatalog.Section<PerkConfig>();
        Add("克莉丝汀", "升级门槛", "christine_l2_days", "升 2 级所需存活天数", perkConfig.ChristineLevel2ThresholdDays, "天", christineSrc + ".Level2ThresholdDays", cfgKey: "ChristineLevel2ThresholdDays");
        Add("克莉丝汀", "生存", "christine_l1_hunger_skip", "1 级 不掉饥饿概率", Pct(perkConfig.ChristineL1HungerSkipChance), "%", christineSrc + ".L1HungerSkipChance", settled: true, cfgKey: "ChristineL1HungerSkipChance", pct: true);
        Add("克莉丝汀", "经济", "christine_l2_buy_discount", "2 级 买入折扣", Pct(perkConfig.ChristineLevel2BuyDiscount), "%", christineSrc + ".Level2BuyDiscount", settled: true, cfgKey: "ChristineLevel2BuyDiscount", pct: true);
        Add("克莉丝汀", "生存", "christine_l3_hunger_skip", "3 级 额外不掉饥饿概率", Pct(perkConfig.ChristineL3ExtraHungerSkipChance), "%", christineSrc + ".L3ExtraHungerSkipChance", settled: true, cfgKey: "ChristineL3ExtraHungerSkipChance", pct: true);
        Add("克莉丝汀", "经济", "christine_l3_sell_rate", "3 级 卖出价率", perkConfig.ChristineLevel3SellRatePercent, "%", christineSrc + ".Level3SellRatePercent", settled: true, cfgKey: "ChristineLevel3SellRatePercent");

        // —— 皮特·田径队大男孩（效果值用户拍板·非拟定；升级阈值拟定待调）——
        const string peteSrc = "godot/scripts/PetePerk.cs :: PetePerk";
        Add("皮特", "生存", "pete_l1_movespeed", "1 级 移速倍率", PetePerk.Level1MoveSpeedMultiplier, "倍", peteSrc + ".Level1MoveSpeedMultiplier", settled: true, cfgKey: "PeteLevel1MoveSpeedMultiplier");
        Add("皮特", "生存", "pete_l2_movespeed", "2 级 移速倍率", PetePerk.Level2MoveSpeedMultiplier, "倍", peteSrc + ".Level2MoveSpeedMultiplier", settled: true, cfgKey: "PeteLevel2MoveSpeedMultiplier");
        Add("皮特", "生存", "pete_l3_movespeed", "3 级 移速倍率", PetePerk.Level3MoveSpeedMultiplier, "倍", peteSrc + ".Level3MoveSpeedMultiplier", settled: true, cfgKey: "PeteLevel3MoveSpeedMultiplier");
        Add("皮特", "操作与生产", "pete_l2_operation", "2 级 操作能力加成", Pct(PetePerk.OperationCapabilityBonus), "%", peteSrc + ".OperationCapabilityBonus", settled: true, cfgKey: "PeteOperationCapabilityBonus", pct: true);
        Add("皮特", "战斗", "pete_l3_dodge", "3 级 受击闪避概率", Pct(PetePerk.DodgeChanceValue), "%", peteSrc + ".DodgeChanceValue", settled: true, cfgKey: "PeteDodgeChanceValue", pct: true);
        Add("皮特", "战斗", "pete_l3_dodge_maxkg", "3 级 闪避的负重上限（严格小于才可闪）", PetePerk.DodgeMaxCarriedKg, "公斤", peteSrc + ".DodgeMaxCarriedKg", settled: true, cfgKey: "PeteDodgeMaxCarriedKg");
        Add("皮特", "生存", "pete_extra_hunger", "额外掉 2 饥饿的概率（1 级起常驻）", Pct(PetePerk.ExtraHungerDropChance), "%", peteSrc + ".ExtraHungerDropChance", settled: true, cfgKey: "PeteExtraHungerDropChance", pct: true);
        Add("皮特", "升级门槛", "pete_hunger_streak_floor", "连续计数的饥饿下限（≥它才续连续）", PetePerk.HungerThresholdForStreak, "饥饿值", peteSrc + ".HungerThresholdForStreak", cfgKey: "PeteHungerThresholdForStreak");
        Add("皮特", "升级门槛", "pete_l2_phases", "升 2 级 所需连续相位（= 5 天·每天 2 相位）", PetePerk.Level2ConsecutivePhases, "相位", peteSrc + ".Level2ConsecutivePhases", cfgKey: "PeteLevel2ConsecutivePhases");
        Add("皮特", "升级门槛", "pete_l3_departure_ceiling", "升 3 级 出行计数的饥饿上限", PetePerk.DepartureHungerCeiling, "饥饿值", peteSrc + ".DepartureHungerCeiling", cfgKey: "PeteDepartureHungerCeiling");
        Add("皮特", "升级门槛", "pete_l3_departures", "升 3 级 所需合格出行次数", PetePerk.Level3DepartureCount, "次", peteSrc + ".Level3DepartureCount", cfgKey: "PeteLevel3DepartureCount");

        // —— 耗子·下水道拾荒（数值用户原话·非拟定；L3 已接入探索感知与攻击消费层）——
        const string ratSrc = "godot/scripts/SurvivorPerks.cs :: RatPerk";
        Add("耗子", "升级门槛", "rat_l2_items", "升 2 级 累计搜出件数", RatPerk.Level2ThresholdItems, "件", ratSrc + ".Level2ThresholdItems", settled: true, cfgKey: "RatLevel2ThresholdItems");
        Add("耗子", "升级门槛", "rat_l3_items", "升 3 级 累计搜出件数", RatPerk.Level3ThresholdItems, "件", ratSrc + ".Level3ThresholdItems", settled: true, cfgKey: "RatLevel3ThresholdItems");
        Add("耗子", "感知", "rat_l1_noise", "1 级 动作噪音半径乘子（脚步/开门/撬锁/拆除）", RatPerk.Level1ActionNoiseMultiplier, "倍", ratSrc + ".Level1ActionNoiseMultiplier", settled: true, cfgKey: "RatLevel1ActionNoiseMultiplier");
        Add("耗子", "操作与生产", "rat_l1_loot", "1 级 翻找搜刮速度加成", Pct(RatPerk.Level1LootSpeedBonus), "%", ratSrc + ".Level1LootSpeedBonus", settled: true, cfgKey: "RatLevel1LootSpeedBonus", pct: true);
        Add("耗子", "操作与生产", "rat_l2_loot", "2 级 翻找搜刮速度再加成（指定加算例外）", Pct(RatPerk.Level2LootSpeedBonus), "%", ratSrc + ".Level2LootSpeedBonus", settled: true, cfgKey: "RatLevel2LootSpeedBonus", pct: true);
        Add("耗子", "感知", "rat_l3_darkness", "3 级 黑暗隐匿点加成", Pct(RatPerk.Level3DarknessStealthBonus), "%", ratSrc + ".Level3DarknessStealthBonus", settled: true, cfgKey: "RatLevel3DarknessStealthBonus", pct: true);
        Add("耗子", "战斗", "rat_l3_ambush", "3 级 破隐先手额外伤害", Pct(RatPerk.Level3AmbushDamageBonus), "%", ratSrc + ".Level3AmbushDamageBonus", settled: true, cfgKey: "RatLevel3AmbushDamageBonus", pct: true);

        // —— 神秘商人（两位商人共用同一套价率与调度）——
        var sched = new MerchantSchedule(new SystemRandomSource(), currentDay: 0);
        // 商人价率外置在 merchant.json（不在 perks.json）⇒ 行级 _configFile 覆盖表级；值本身就是整数百分比(100/60)，config 也存整数 ⇒ 不 pct。
        Add("神秘商人", "经济", "merchant_buy_rate", "玩家买入价（占基准价）", MerchantTrade.BuyRatePercent, "%", "godot/scripts/MerchantTrade.cs :: BuyRatePercent", settled: true, cfgKey: "BuyRatePercent", cfgFile: "merchant.json");
        Add("神秘商人", "经济", "merchant_sell_rate", "玩家卖出价（占基准价）", MerchantTrade.SellRatePercent, "%", "godot/scripts/MerchantTrade.cs :: SellRatePercent", settled: true, cfgKey: "SellRatePercent", cfgFile: "merchant.json");
        Add("神秘商人", "经济", "merchant_gap_min", "来访间隔下限", sched.MinGap, "天", "godot/scripts/MerchantSchedule.cs :: 构造默认 minGap", settled: true);
        Add("神秘商人", "经济", "merchant_gap_max", "来访间隔上限", sched.MaxGap, "天", "godot/scripts/MerchantSchedule.cs :: 构造默认 maxGap", settled: true);

        // —— 敌对 ——
        double dressed = ZombieOutfit.Presets.Where(p => p.Clothes().Count > 0).Sum(p => p.Weight);
        Add("丧尸", "生成配置", "zombie_dressed_rate", "至少还穿着一件的比例", Pct(dressed), "%", "src/DeadSignal.Combat/ZombieOutfit.cs :: Presets（权重和）", settled: true);
        Add("丧尸", "生成配置", "zombie_outfit_count", "日常着装预设套数", ZombieOutfit.Presets.Count, "套", "src/DeadSignal.Combat/ZombieOutfit.cs :: Presets", settled: true);
        Add("超市的「幸存者」", "生成配置", "supermarket_ambushers", "背刺围攻人数", SupermarketAmbush.AmbushRaiderCount, "名", "godot/scripts/SupermarketAmbush.cs :: AmbushRaiderCount", settled: true);

        foreach (ZombieOutfitPreset p in ZombieOutfit.ElitePresets)
        {
            Add(p.Name, "生成配置", "elite_weight_" + EliteId(p.Name), "随机抽取权重（0 = 永不随机出现）", p.Weight, "",
                "src/DeadSignal.Combat/ZombieOutfit.cs :: ElitePresets", settled: true);
            Add(p.Name, "生成配置", "elite_layers_" + EliteId(p.Name), "身上的衣物/护甲件数", p.Clothes().Count, "件",
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
            ["perkL1"] = $"入队即得。山姆从小在农场帮奶奶干活，负重+{Pct(SamPerk.Level2CarryBonus)}%，操作能力+{Pct(SamPerk.Level1OperationBonus)}%。",
            ["perkL2"] = $"营地 {SamPerk.Level2CampPopulation} 人。他从小身强体壮、性格坚韧，比常人耐揍——自身受到的伤害 −{Pct(SamPerk.Level1DamageReduction)}%"
                         + $"（在护甲挡完之后再减，被甲完全挡下时依然是 0）。并且山姆的恢复速度*{1 + SamPerk.Level2HealSpeedBonus:0.##}。",
            ["perkL3"] = $"营地 {SamPerk.Level3CampPopulation} 人。山姆将英雄气概与坚韧不拔展示的淋漓尽致，山姆的受到的所有大流血降级为中流血，被震荡的概率-{Pct(SamPerk.Level3ConcussionReduction)}%，受到骨折的负面影响-30%。",
            ["join"] = "开局就在营地（另一位是诺蒂）。",
            ["gear"] = "开局三件套（长袖布衣 / 长裤 / 一双运动鞋）",
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
        ["perkL2"] = $"累计读满 {BookwormPerk.Level2ThresholdHours} 小时。自身阅读速度加成变为{Pct(BookwormPerk.BonusForLevel(2))}%。",
        ["perkL3"] = $"累计读满 {BookwormPerk.Level3ThresholdHours}小时。自身阅读速度加成为 {Pct(BookwormPerk.BonusForLevel(3))}%，"
                     + $"且全营所有人阅读速度 +{Pct(BookwormPerk.CampWideBonusAtMax)}%（含他自己 ⇒ 他自己合计 +{Pct(BookwormPerk.BonusForLevel(3) + BookwormPerk.CampWideBonusAtMax)}%）。",
        ["join"] = "开局就在营地（另一位是山姆）。",
        ["gear"] = "开局三件套（长袖布衣 / 长裤 / 一双运动鞋）",
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
                     + "解锁道格给布鲁斯做狗装备（五件套）。布鲁斯的攻击速度和移动速度都+12%。",
        ["perkL3"] = $"共同活过 {DougBruceBond.Level3Days} 天。相依为命光环（两个在 {DougBruceBond.DefaultAuraRadius:0} 距离内时生效）："
                     + $"操作能力*{DougBruceBond.AuraOperationMult:0.##}、受到伤害 *{DougBruceBond.AuraDamageTakenMult:0.##}。"
                     + "一方死亡即永久失去。",
        ["join"] = $"「{VillageRescue.DestinationName}」大调查点的一段救援：他和布鲁斯被困在一间上锁、被丧尸围困的屋子里，"
                   + "道格已经饿到昏迷。调查团靠近到中距离时，布鲁斯开始吠叫引路——清掉或绕开围困的丧尸、踏进去解救，"
                   + "回营时两个才正式入队（道格昏迷，当场没法作战）。入队时他饥饿被压到极低档，你得先把他喂回来。",
        ["gear"] = "棍棒（开局武器）+墨镜+开局三件套",
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
                     + $"全营感染率 −{Pct(NightingalePerk.Level2InfectionReduction)}%（要她在营活着才维持，不在营/死了就失效）。并且干净的床铺让睡在上面的人恢复速度加成从10%变到{NightingalePerk.Level2BedSleepHealBonusPct}%。",
        ["perkL3"] = $"她做满 {NightingalePerk.Level3ThresholdSurgeries} 台手术。卫生意识深入人心——全营手术基础点 +{NightingalePerk.CampSurgeryBaseBonus}、"
                     + $"全营感染率再 −{Pct(NightingalePerk.Level3InfectionReduction)}%。"
                     + $"这是永续遗产：她死了、离开了，依旧生效（知识已经传下去了）。她还活着时与 2 级叠加，感染合计 −{Pct(NightingalePerk.Level2InfectionReduction + NightingalePerk.Level3InfectionReduction)}%。",
        ["join"] = $"在「{NurseRecruit.DisplayName}」探索时遇到她。她清醒、能说话（不像昏迷的道格）：弹一段招募对话。"
                   + "婉拒不会关门——你可以再来找她谈，直到她答应；答应后回营时正式入队。",
        ["gear"] = "开局三件套",
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
        ["perkName"] = "巧舌如簧",
        ["perkAxis"] = "加入营地是一级，存活三天后升到二级，清剿金手指帮后升三级",
        ["perkL1"] = "懂得挨饿，才能在流浪中活下来，每个相位变化时，有25%的几率不掉饥饿值。",
        ["perkL2"] = "从商人处买东西时有6.25%的折扣。",
        ["perkL3"] = "大仇得报！她回归了销售员的本质，卖出时的价格从60%上涨到了70%。并且她对热量的消耗似乎更少了，每个相位变化时，有额外10%的几率不掉饥饿值。",
        ["join"] = "教学关（第 2 夜）：一伙劫掠者袭营，她混在里头，起手和劫掠者同阵营。"
                   + "战斗中任一劫掠者受伤较重、或她自己挂彩（谁先到算谁）→ 她喊出那句台词、阵中反水，"
                   + "转为自动战斗的盟友——但此时你操控不了她。战斗结束后你可以选收留 / 放逐 / 处决："
                   + "只有收留，她才变成你能操控的营地幸存者。",
        ["gear"] = "匕首+开局三件套+皮革胸甲",
        ["backstory"] = "她曾被金手指帮轮奸，好不容易逃出来，后来加入一个劫掠者团伙勉强活命。",
        ["relations"] = "（待你手写。）",
        ["storyline"] = $"请求清剿金手指帮（有时限的支线）：收留后她在聚餐里用气泡反复请求出兵，共 {ChristineRequestLogic.DeclinesToLeave} 次，"
                        + "每次那一餐结束后弹抉择面板（答应出兵 / 暂不）。答应 → 从此不再逼问；"
                        + $"累计 {ChristineRequestLogic.DeclinesToLeave} 次「暂不」→ 她在下一次昼夜交替时自己离开营地去复仇，"
                        + "日后你会在金手指帮根据地发现她的尸体。\n\n"
                        + "⚠️ 她在教学关里不受任何特殊保护——她可能当场战死；一旦战死，整条支线不触发。（黑暗向，有意为之。）",
        ["notes"] = "专属效果数值为用户拍板值；L1 与 L3 的不掉饥饿概率是同一专属效果内的加算例外，合计 35%。"
                    + "买入折扣和卖出价率都要求她仍在营存活。反水阈值：任一劫掠者血量掉到 50% 以下，或她自己掉血（满血即触发）。",
        ["draft"] = true,
        ["_id"] = "christine",
        ["_anchor"] = "godot/scripts/ChristineRequestLogic.cs + TutorialRaidLogic.cs + GoldfingerDiscovery.cs",
    };

    private static Dictionary<string, object?> Pete() => new()
    {
        ["name"] = PetePerk.PeteName,
        ["category"] = Survivor,
        ["faction"] = "幸存者",
        ["tagline"] = "十来岁的大男孩，曾是学校田径队。深夜连滚带爬扑到大门外求救的那个。",
        ["perkName"] = "疾行如风",
        ["perkAxis"] = "两条不同形态的轴（升级不倒退）：\n"
                       + $"· L1→L2「连续饿着」：相位级查——每相位饥饿 ≥{PetePerk.HungerThresholdForStreak} 连续计数 +1，任一相位 小于 {PetePerk.HungerThresholdForStreak} 清零重记；"
                       + $"连续 {PetePerk.Level2ConsecutivePhases} 相位（= 5 天·每天 2 相位）不断 → 永久升 L2（latch，此后再饿一顿也不掉回去）。\n"
                       + $"· L2→L3「饿着还出门」：出发瞬间饥饿 ≤{PetePerk.DepartureHungerCeiling} 计一次（单调累计只增不减），累计 {PetePerk.Level3DepartureCount} 次 → L3。",
        ["perkL1"] = $"入队即得。移速 {PetePerk.Level1MoveSpeedMultiplier:0.##} 倍。\n"
                     + $"且不论几级都常驻：大男孩代谢快，每相位 {Pct(PetePerk.ExtraHungerDropChance)}% 概率额外掉 1 饥饿（合计一相位掉 2）。",
        ["perkL2"] = $"连续 5 天饥饿 ≥{PetePerk.HungerThresholdForStreak}。移速升到 {PetePerk.Level2MoveSpeedMultiplier:0.##} 倍；操作能力 *{1 + PetePerk.OperationCapabilityBonus:0.##}（+{Pct(PetePerk.OperationCapabilityBonus)}%，与残疾、饥饿、骨折及其他来源乘算）。",
        ["perkL3"] = $"达 L2 后饥饿 ≤{PetePerk.DepartureHungerCeiling} 出发累计 {PetePerk.Level3DepartureCount} 次。移速升到 {PetePerk.Level3MoveSpeedMultiplier:0.##} 倍；"
                     + $"保留 2 级操作加成；负重小于 {PetePerk.DodgeMaxCarriedKg:0}kg 时，受击有 {Pct(PetePerk.DodgeChanceValue)}% 概率闪避（整次攻击无效，背太重就闪不动）。",
        ["join"] = $"第 7 天夜一开局，一个男孩跑到大门外拍门大喊求救，弹三选一：开门救援 / 置之不理 / 攻击他。\n"
                   + $"· 开门救援 → 追在他身后的三只普通丧尸（非精英）一起涌到门口；三尸全歼且男孩存活 → 他作为「{PetePerk.PeteName}」入营；男孩战死 → 救援失败（不入营）。\n"
                   + "· 置之不理 / 攻击他 → 男孩死亡、事件结束（整条招募不再触发）。",
        ["gear"] = "开局三件套",
        ["backstory"] = "青春期大男孩，曾是学校田径队。\n\n（其余前史/性格待你手写——代码只给了「田径队大男孩」这一条 authored 事实，不引申。）",
        ["relations"] = "（待你手写。）",
        ["storyline"] = "移速/操作/闪避全按等级自动接通。L3 闪避只在负重 小于 30kg 时生效——背得太重就闪不动。",
        ["notes"] = "数值口径：效果值（移速/操作/闪避/饥饿）是用户拍板·非拟定；升级阈值（连续相位/出行次数）是拟定待调。"
                    + "专属效果名「田径队大男孩」取自代码注释的 authored 描述，非正式命名，待你定。数字在隔壁「角色数值」分区。",
        ["draft"] = true,
        ["_id"] = "pete",
        ["_anchor"] = "godot/scripts/PetePerk.cs :: PetePerk + CampMain.PeteEvent.cs（第 7 夜敲门救援招募）",
    };

    private static Dictionary<string, object?> Rat() => new()
    {
        ["name"] = RatPerk.RatName,
        ["category"] = Survivor,
        ["faction"] = "幸存者",
        ["tagline"] = "下水道最深处那个浑身恶臭、穿着潮湿破布夹克的女人。没有名字，叫「耗子」。",
        ["perkName"] = "拾荒智慧",
        ["perkAxis"] = "她本人累计搜出的物品件数（一件 = 藏物清单里的一个条目，不按数量/重量/价值——8 发子弹是一堆、一次转出算一件）。只增不减。",
        ["perkL1"] = $"入队即得。脚步和动作（脚步 / 开门 / 撬锁 / 静默拆除）噪音 *{RatPerk.Level1ActionNoiseMultiplier:0.##}（减 {Pct(1 - RatPerk.Level1ActionNoiseMultiplier)}%）——"
                     + "战斗、开枪、破坏这些不减；翻找搜刮速度 +" + Pct(RatPerk.Level1LootSpeedBonus) + "%。",
        ["perkL2"] = $"累计搜出 {RatPerk.Level2ThresholdItems} 件。翻找搜刮速度再 +{Pct(RatPerk.Level2LootSpeedBonus)}%（合计 +{Pct(RatPerk.Level1LootSpeedBonus + RatPerk.Level2LootSpeedBonus)}% ⇒ {1 + RatPerk.Level1LootSpeedBonus + RatPerk.Level2LootSpeedBonus:0.##} 倍）；"
                     + "并且她翻找东西不会产生任何噪音。",
        ["perkL3"] = $"累计搜出 {RatPerk.Level3ThresholdItems} 件。黑暗带来的隐匿点 +{Pct(RatPerk.Level3DarknessStealthBonus)}%；破隐先手攻击额外再造成 +{Pct(RatPerk.Level3AmbushDamageBonus)}% 伤害。"
                     + "黑暗隐匿、服装、负重、掩体四项会共同影响被发现距离；未被敌方感知时的第一次攻击获得破隐先手加成。",
        ["join"] = $"探索到「{RatRecruit.DestinationName}」最深处遇到她，弹招募对话。婉拒不关门（可再来谈，直到她答应）；"
                   + "答应后回营时正式入队（出行队伍名单已定，不在关内临时增员，同护士/村庄救援口径）。",
        ["gear"] = "刺剑+开局三件套",
        ["backstory"] = "🔴 用户只给了四条事实：浑身恶臭、穿着潮湿破布夹克、是个女人、没有名字叫「耗子」、可招募。\n\n"
                        + "她的前史 / 性格 / 为什么在下水道 / 和谁认识——用户一个字都没写，代码不许编造，一律留白等你手写。",
        ["relations"] = "（待你手写。）",
        ["storyline"] = "搜刮速度加成、动作噪音减免（L1/L2）已生效；L3 的黑暗隐匿点 +50% 与破隐先手 +35% 伤害已接入探索感知与攻击消费层。",
        ["notes"] = "数值为用户原话·非拟定（75/250 件、−40% 噪音、+50% 搜刮、再 +100%）。"
                    + "L2 搜刮速度 2.50 倍 是用户明确指定的**加算例外**（同一 perk 自己的两级台阶按总量口述），不是漏网的加算残留，别顺手改成乘算。"
                    + "数字在隔壁「角色数值」分区。",
        ["draft"] = true,
        ["_id"] = "rat",
        ["_anchor"] = "godot/scripts/SurvivorPerks.cs :: RatPerk + RatRecruit.cs（下水道最深处招募）",
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
