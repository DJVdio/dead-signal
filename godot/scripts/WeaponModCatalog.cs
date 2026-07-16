using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 武器改装目录（**数据表，非逻辑**）：14 条改装。合成走通用的 <see cref="WeaponMods.ApplyMods"/>。
///
/// <para>
/// 🔴 <b>这张表的单一事实源是用户的 wiki：<c>docs/wiki/data/weapon-mods.json</c></b>（[T47] 用户整表重设计）。
/// **表赢代码** —— 数值/白名单/材料/工时以 wiki 为准，代码向它看齐。改这里之前先看那张表。
/// </para>
///
/// <hr/>
/// <b>一、重量 = 这套设计的核心代价轴</b>（用户原话：「我希望重量在改装中是一个重要的因素」）
/// <para>
/// 每条改装带一个 <see cref="WeaponMod.WeightMultiplier"/>（−25% ~ +50%），**真的进负重账**
/// （<c>ItemWeights.WeaponKg</c> → <c>ModdedWeaponRegistry.WeightMultiplierOf</c>）。
/// 所以「轻质化枪托」那种**只减重、别的全是负面**的改装才成立：你买的就是那 15% 的重量。
/// </para>
///
/// <b>二、百分比一律乘算</b>（CLAUDE.md 铁律；用户复述过一遍：「穿透 −10% 指的是在原本数值上 −该数值的 10%，
/// 例如 20% 变成 18%」⇒ ×0.9，不是减 10 个百分点）。
/// <para>
/// **唯一例外 = 钉子强化的穿透 +0.03**（加算）。理由见 <see cref="NailStuds"/>：棍棒的穿透**本来就是 0**，
/// 乘算在零上永远是零 —— 这是"零陷阱"，用户明确点名要在这里破例。
/// </para>
/// <para>
/// **攻速 ↔ 攻击间隔的换算**：本表沿用用户自己在表里给出的等式（「防滑缠手：攻速 +5% ＝ 攻击间隔 ×0.95」）
/// ⇒ **攻速 +x% ⇒ 间隔 ×(1−x)**，攻速 −x% ⇒ 间隔 ×(1+x)。
/// </para>
///
/// <b>三、穿透 ≤ 100%</b>（用户拍板）。收口在 <c>WeaponMods.WeaponDraft.Build</c> 的 <c>Clamp(0,1)</c>，
/// 护栏见 <c>WeaponModPenetrationCapTests</c>。
///
/// <hr/>
/// <b>四、三种近战型态（刺刀 / 利爪 / 创伤）—— 口径已被用户整个换掉</b>
/// <para>
/// ⚠️ <b>旧口径（已作废，别照着推理）</b>：型态是"在这把枪自己的枪托数值上乘一个系数" ⇒ 重枪改出来的更猛。
/// </para>
/// <para>
/// <b>新口径（用户写在 wiki 上）</b>：「近战模式**等同于 85% 攻速的〈某把近战武器〉**」（[T68] 原 80%）——
/// 刺刀＝刺剑、利爪＝消防斧、创伤＝尖头锤、<b>[T68] 锋刃＝匕首</b>，一律 <b>覆盖（Set）</b>而非缩放。
/// ⇒ <b>所有枪的同一型态，枪托数值完全一样</b>（<b>你捅人用的是那把刀，不是那把枪</b>）。
/// 差异全部搬到**重量代价**上：刺刀 +10% / 利爪 +30% / 创伤 +50% / 锋刃 +5%。
/// </para>
/// <para>
/// [T68] 攻速 85% ⇒ 出手间隔 ÷0.85 ⇒ DPS ×0.85（原 80% 时为 ×0.8，四种型态一律提速）。
/// DPS 排序仍与重量代价排序单调一致（刺刀 ＜ 利爪 ＜ 创伤），且全部**低于同源匕首本体**：
/// 改装能让你"打空了还能打"，但**不能让你不必带近战武器**。
/// 🔴 具体 DPS 数不在此钉死（刺剑/消防斧/尖头锤/匕首的数值另有 agent 在同步，钉了就会过时）——
/// 它们从 <see cref="WeaponTable"/> 实读，用户调基准武器时四型态自动跟着变。
/// </para>
/// <para>
/// 🔴 <b>创伤型 2.286 ＞ 棍棒 2.04 = 用户有意为之</b>（它代价最大：重量 +50%、材料最贵、240 工时）。
/// 一把加装了铁锤头的步枪打不过一根木棍，那才荒唐。**这不是待修的平衡问题。**
/// </para>
/// <para>
/// ⚠️ <b>当前的真实状态：改装后近战反而比原厂枪托弱</b>（4 把重枪的原厂枪托是 2.80~2.84 ＞ 型态的 1.90~2.29）。
/// 根因**不在本表**：用户说过「枪械近战得到补足是应该的，但**我会对其做削弱**」「枪托先不管了，我后面自己调整」——
/// 原厂枪托的下调**还没发生**。他压下去之后三型态自然重新成为升级。
/// 见 <c>GunModBenchTests.ModdedStock_WeakerThanPlainStock_IsCurrentlyBroken_PendingUserStockNerf</c>（记录现状的测试）。
/// </para>
/// </summary>
public static class WeaponModCatalog
{
    // ═══════════════════════════════════════════════════════════════════════════
    // 装配约束：**逐把武器的白名单**（用户拍板，取代原来的「武器大类」）
    //
    // 迁移那一步要求**行为零变化**：每条改装的白名单 = 它原本那个大类的**全部**武器。
    // 之所以必须零变化 —— 老存档里的改装枪靠 ModdedWeaponRegistry 用**当前**规则重算，
    // 规则一收严，老组合就变非法。所以收窄是用户的活，不是迁移的活。
    // （收窄后老档怎么办：走 ModdedWeaponRegistry.RebuildOrBase **回落成基础武器**——
    //   弓还在、改装没了，不再"静默失效"。用户拍板，[T29]。）
    //
    // 🔴 【T29·用户已拍板收窄】枪械改装的白名单**不再包含 8 把弓弩**（见 <see cref="AllGuns"/>）。
    //    这修的是一个**真 bug**，不是平衡调整：WeaponMods.ClassOf 是 `IsRanged ? Firearm : …`
    //    ⇒ 弓弩被误归为"枪械" ⇒ 截短枪管真能装到短弓上。迁移期如实保留了它，用户现已明令划掉。
    //    刃类/钝类的 9 条改装**一格没动**（它们本来就不含弓弩）。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>全部可被改装的武器（常规 + 弓弩；天生武器不算，它们不是能拿起来的东西）。</summary>
    public static IReadOnlyList<Weapon> AllModdableWeapons()
        => WeaponTable.Arsenal()
            .Concat(WeaponTable.ArcheryArsenal())
            .GroupBy(w => w.Name)
            .Select(g => g.First())
            .ToList();

    /// <summary>
    /// 某个武器大类下的全部武器名 —— **迁移用**：把旧的「大类约束」展开成等价的白名单。
    /// <para>⚠️ <c>WeaponMods.ClassOf</c> 把**弓弩也算作 Firearm**（<c>IsRanged</c> 为真即可），
    /// 所以「枪械」这一类展开出来是**会带上 8 把弓弩的**。这不是笔误，这是引擎的现行行为
    /// （现在真能把截短枪管装到短弓上）。如实展开，才叫零变化。</para>
    /// </summary>
    public static IReadOnlySet<string> AllOfClass(WeaponClass cls)
        => AllModdableWeapons()
            .Where(w => WeaponMods.ClassOf(w) == cls)
            .Select(w => w.Name)
            .ToHashSet();

    /// <summary>
    /// **真正的枪**（弓弩不算）—— 6 条枪械改装的白名单源，[T29] 用户拍板收窄后启用。
    /// <para>
    /// 判据是**引擎级**的 <c>AmmoKey == <see cref="AmmoKeys.Arrow"/></c>（吃箭的就是弓弩），
    /// 不按名字里有没有"弓/弩"字去猜——名字是 authored 文案，改个名就漏判，那是下一个 bug。
    /// </para>
    /// <para>
    /// 与 <see cref="AllOfClass"/>(<see cref="WeaponClass.Firearm"/>) 的差集恰好是 8 把弓弩。
    /// <c>AllOfClass</c> 保留原样（刃类/钝类仍用它，迁移护栏也还要读它）。
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> AllGuns()
        => AllModdableWeapons()
            .Where(w => WeaponMods.ClassOf(w) == WeaponClass.Firearm && !IsArchery(w))
            .Select(w => w.Name)
            .ToHashSet();

    /// <summary>是不是弓弩（吃箭的远程）——枪械改装装不到它们身上。</summary>
    public static bool IsArchery(Weapon w) => w.IsRanged && w.AmmoKey == AmmoKeys.Arrow;

    // ═══════════════════════════════════════════════════════════════════════════
    // 【T47】白名单改成**逐条列名**（不再 AllOfClass 一把梭）
    //
    // 原因：用户在 wiki 上把每条改装的「可装于哪些武器」**逐格勾过了**，而且**各不相同**：
    //   · 截短枪管 = 5 把枪（**冲锋枪被划掉**——它本来就短）
    //   · 三种近战型态 = 4 把重枪（**手枪/冲锋枪被划掉**——手枪装刺刀本来就荒诞）
    //   · 锯齿剑刃 / 镂空剑刃 = 5 把锐器（**刺剑被划掉**——突刺剑没有"开锯齿/开血槽"这回事）
    //   · 防滑缠手 = **锐器 6 把 + 钝器 3 把合成一条**（原来是同名两条，历史包袱，用户合并了）
    // ⇒ 再用 AllOfClass 派生就会**覆盖掉用户的手勾**。白名单从此是"用户填的数据"，不是"从大类推的"。
    //
    // ✅ **消防斧已按用户拍板勾进锐器改装**（「和长剑同档」的口径）—— 见下方那段的逐条语义过审。
    //    （此前它一条改装都装不上：用户的 wiki 表是在消防斧存在**之前**填的，勾选框里还没有"消防斧"这个选项。）
    // ═══════════════════════════════════════════════════════════════════════════

    private static IReadOnlySet<string> Names(params string[] weaponNames) => weaponNames.ToHashSet();

    /// <summary>六把真枪（= <see cref="AllGuns"/>，写成常量给逐条白名单用）。</summary>
    private static IReadOnlySet<string> Guns6()
        => Names("自制猎枪", "手枪", "冲锋枪", "步枪", "狙击枪", "自制霰弹枪");

    /// <summary>截短枪管的 5 把（用户划掉了冲锋枪）。</summary>
    private static IReadOnlySet<string> GunsSawnOff()
        => Names("自制猎枪", "手枪", "步枪", "狙击枪", "自制霰弹枪");

    /// <summary>能装**重枪型态**（刺刀/利爪/创伤）的 4 把重枪（用户划掉了手枪与冲锋枪）。</summary>
    private static IReadOnlySet<string> GunsMeleeForm()
        => Names("自制猎枪", "步枪", "狙击枪", "自制霰弹枪");

    /// <summary>
    /// [T68] 能装**短枪型态**（锋刃型）的 2 把短枪：手枪 / 冲锋枪。
    /// 🔴 **与 <see cref="GunsMeleeForm"/> 严格不相交** —— 短枪装不了刺刀/利爪/创伤，重枪装不了锋刃。
    /// 这是"手枪不可能同时挂两种近战型态"的**第一道闸**（白名单层）；型态互斥是第二道（防未来白名单变动）。
    /// </summary>
    private static IReadOnlySet<string> GunsBladeForm()
        => Names("手枪", "冲锋枪");

    // ═══════════════════════════════════════════════════════════════════════════
    // 【T47 追加·用户拍板】消防斧按「**和长剑同档**」的口径勾进锐器改装。
    //
    // 依据：消防斧 DPS 2.79 ≈ 长剑 2.81（同档）⇒ 长剑吃的那 6 条改装，消防斧原则上照搬一份。
    //
    // ⚠️ 但用户要的是「**同档的口径**」，不是"一个字不差照抄" ⇒ 逐条过了一遍语义，**跳掉 1 条**：
    //
    //   ✅ 锋刃研磨（开刃）      —— 消防斧当然要磨；斧子钝了就是根铁棍。
    //   ✅ 防滑缠手（缠手）      —— 斧柄缠布防滑，最自然不过。
    //   ✅ 加重剑柄（柄部配重）  —— 斧柄灌铅配重：消防斧本就靠惯性吃饭，更沉更狠，语义顺。
    //   ✅ 轻质化剑柄（换轻柄）  —— 换一副轻木柄，挥得快些。斧柄本来就是木头。
    //   ✅ 锯齿剑刃（刃上开齿）  —— 边缘案例但**不算荒谬**（消防斧/救援斧确有开齿的一段）。按"同档"默认勾上；
    //                             用户若嫌怪，在 wiki 上一键划掉即可。
    //   ❌ **镂空剑刃（开血槽减重 −25%、攻速 +15%、伤害 −9%）—— 唯一跳过的一条。**
    //        理由不是"消防斧没有'剑刃'"（那 4 条也都叫"剑X"，按名字否决会把 4 条一起误杀），
    //        而是**功能上自相矛盾**：**消防斧的杀伤力就是它的头部质量**（头重杆轻，靠惯性劈开东西）。
    //        给消防斧开血槽/镂空 = 把它赖以成立的那个东西挖掉 ⇒ 换来的是"更快但更轻更软的消防斧"，
    //        那不是消防斧，那是一把很差的剑。**这条是"明显不通"，故不硬勾。**
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>六把锐器 + **消防斧**（用户拍板：消防斧与长剑同档）。</summary>
    private static IReadOnlySet<string> Blades6WithAxe()
        => Names("匕首", "短剑", "刺剑", "长剑", "草叉", "重剑", "消防斧");

    /// <summary>锯齿剑刃能装的：5 把锐器（用户划掉了刺剑）+ **消防斧**。</summary>
    private static IReadOnlySet<string> SerratedFits()
        => Names("匕首", "短剑", "长剑", "草叉", "重剑", "消防斧");

    /// <summary>
    /// 镂空剑刃能装的：5 把锐器（用户划掉了刺剑）—— <b>唯一不含消防斧的锐器改装</b>，
    /// 理由见上方那段（镂空会把消防斧赖以成立的头部质量挖掉）。
    /// </summary>
    private static IReadOnlySet<string> FullerFits()
        => Names("匕首", "短剑", "长剑", "草叉", "重剑");

    /// <summary>防滑缠手：锐器 6 + **消防斧** + 钝器 3（用户把原来同名的两条合并成了一条）。</summary>
    private static IReadOnlySet<string> BladesAndBlunts()
        => Names("匕首", "短剑", "刺剑", "长剑", "草叉", "重剑", "消防斧", "棍棒", "尖头锤", "破甲锤");

    /// <summary>棍棒（铁丝/钉子强化是它独有的）。</summary>
    private static IReadOnlySet<string> ClubOnly() => Names("棍棒");

    /// <summary>
    /// 这条改装**迁移前**属于哪个大类 —— 只给迁移护栏测试用（钉死"新白名单 ≡ 旧大类"）。
    /// 业务代码一律走 <see cref="WeaponMod.FitsWeapons"/>，不要再按大类判定。
    /// </summary>
    public static WeaponClass LegacyClassOf(WeaponMod mod)
    {
        // ⚠️ 不能按 WeaponPart 推 —— 「防滑缠手」有刃类和钝类**两条**，都占 WeaponPart.Grip。
        //    改按 For(WeaponClass) 那三组**硬编码分组**判：那就是迁移前真实的大类归属。
        foreach (WeaponClass cls in new[] { WeaponClass.Firearm, WeaponClass.Blade, WeaponClass.Blunt })
        {
            if (For(cls).Any(m => m.Id == mod.Id)) return cls;
        }
        return WeaponClass.Blunt;
    }

    private static IReadOnlyDictionary<string, int> Cost(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (key, qty) in items)
        {
            d[key] = qty;
        }
        return d;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 【近战型态的数值来源】"等同于 85% 攻速的〈某把武器〉" —— 直接从 WeaponTable **读那把武器**，
    // 覆盖（Set）到枪托近战的五个字段上。**不抄数字**：用户日后调刺剑/消防斧/尖头锤/匕首，四个型态自动跟着变。
    //
    // [T68·用户手改] 攻速 80% → **85%**（四种型态一律）：出手间隔 ÷ 0.85（≈ ×1.176）。伤害/穿透/噪音**原样照抄**那把武器。
    // ⚠ 砸墙系数不在此列（Weapon.MeleeProfile 不复制它，枪托砸墙恒为默认 1.0）——wiki 表也没写。
    // ═══════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════
    // 【可调数值 = 外置到 godot/data/config/weaponmods.json】（config-weaponmods 单）
    //
    // 🔴 只搬「可调数字」：各改装的 StatMod 乘子/加值、WeightMultiplier、防御否决几率、MeleeFormSpeed。
    //    运算方式(Mul/Add/Set)、作用的 WeaponStat、适配武器白名单/WeaponPart/id 都是**结构**，仍写死在本文件。
    //    读法：Cfg = catalog 段；S(id,stat)=取某条改装某个 StatMod 的数值；T(id)=取整条 Tuning(重量/否决字段)。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>武器改装数值段（weaponmods.json）。首次访问触发懒加载（宿主 Bootstrapper 已注册）。</summary>
    private static WeaponModConfig Cfg => GameConfigCatalog.Section<WeaponModConfig>();

    /// <summary>取某条改装某个 <see cref="WeaponStat"/> 的**数值**（乘子/加值）；运算方式留调用处。</summary>
    private static double S(string id, WeaponStat stat) => Cfg.Stat(id, stat.ToString());

    /// <summary>取某条改装的整条可调数值（重量/否决几率/半角等）；缺 id fail-fast。</summary>
    private static WeaponModTuning T(string id) => Cfg.Get(id);

    /// <summary>攻速折扣：型态的枪托近战 = 该武器的 <see cref="MeleeFormSpeed"/> 倍攻速（0.85 ⇒ 间隔 ÷0.85 ≈ ×1.176）。</summary>
    private static double MeleeFormSpeed => Cfg.MeleeFormSpeed;   // [T68] 用户手改原 0.8；[config] 外置 weaponmods.json

    /// <summary>把一把近战武器**覆盖**成枪托近战的五条 Set（伤害下限/上限、穿透、间隔、噪音）。</summary>
    private static StatMod[] StockMeleeLike(Weapon melee) => new[]
    {
        StatMod.Set(WeaponStat.StockMeleeDamageMin, melee.DamageMin),
        StatMod.Set(WeaponStat.StockMeleeDamageMax, melee.DamageMax),
        StatMod.Set(WeaponStat.StockMeleePenetration, melee.Penetration),
        StatMod.Set(WeaponStat.StockMeleeInterval, melee.AttackInterval / MeleeFormSpeed),
        StatMod.Set(WeaponStat.StockMeleeNoiseRadius, melee.NoiseRadius),
    };

    // ============ 枪械改装（7 条）============

    /// <summary>
    /// 轻质化枪托：<b>整把枪减重 15%</b>，代价是散布 +10%。
    /// <para>
    /// ⚠️ 用户把它原有的"射速↑/伤害↓"**全部删了** —— 现在它**只做一件事：减重**。
    /// 这在重量不进负重账的年代等于"付 180 工时换一把更差的枪"（纯负收益）；
    /// 而 [T47] 把重量接进了负重账之后，**减重本身就是它的收益**——枪轻 15%，人跑得动。
    /// </para>
    /// </summary>
    public static WeaponMod LightenedStock() => new()
    {
        Id = "lightened_stock",
        Name = "轻质化枪托",
        FitsWeapons = Guns6(),
        Part = WeaponPart.Stock,
        Note = "掏空枪托：整枪减重 15%，代价是握持变虚、散布变大。",
        Description = "枪轻了，你举得更久，也晃得更凶。跑起来时它谢你，瞄准时它记恨你。",
        MaterialCosts = Cost(("wood", 1)),
        WorkMinutes = 180,
        WeightMultiplier = T("lightened_stock").WeightMultiplier,   // 重量 −15%
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("lightened_stock", WeaponStat.BaseSpreadDegrees)),   // 散布 +10%
        },
    };

    /// <summary>
    /// 截短枪管：锯短到能**单手抡**，代价是全方位变差（射程/衰减/穿透/散布/伤害）。
    /// <para>材料为空是**对的**（用户清空）：锯掉一截不消耗任何东西——你只是失去了一段枪管。</para>
    /// <para>⚠️ <b>只解除双手，不开双持</b>（用户原话只有"允许单手持有"）——见 <see cref="WeaponMod.AllowsOneHanded"/>。</para>
    /// </summary>
    public static WeaponMod SawnOffBarrel() => new()
    {
        Id = "sawn_off_barrel",
        Name = "截短枪管",
        FitsWeapons = GunsSawnOff(),   // 用户划掉了冲锋枪（它本来就短）
        Part = WeaponPart.Barrel,
        Note = "锯掉半截枪管：单手就能抡，但射程、精度、威力一样不剩。",
        Description = "锯短枪管，换来一只空出来的手。至于那只手能不能替它补上没打中的那一刀，就看你这锯子下得值不值。",
        MaterialCosts = Cost(),        // 用户清空：锯掉一截不消耗材料
        WorkMinutes = 60,
        WeightMultiplier = T("sawn_off_barrel").WeightMultiplier,   // 重量 −20%
        AllowsOneHanded = true,                                   // 「允许单手持有」
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.MaxRange, S("sawn_off_barrel", WeaponStat.MaxRange)),               // 射程 −20%
            StatMod.Mul(WeaponStat.FalloffStart, S("sawn_off_barrel", WeaponStat.FalloffStart)),       // 衰减起点 −20%
            StatMod.Mul(WeaponStat.Penetration, S("sawn_off_barrel", WeaponStat.Penetration)),         // 穿透 −15%（乘算：40% → 34%，不是减 15 个点）
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("sawn_off_barrel", WeaponStat.BaseSpreadDegrees)), // 散布 +25%
            StatMod.Mul(WeaponStat.DamageMin, S("sawn_off_barrel", WeaponStat.DamageMin)),             // 伤害 −10%
            StatMod.Mul(WeaponStat.DamageMax, S("sawn_off_barrel", WeaponStat.DamageMax)),
        },
    };

    /// <summary>加长枪管：射得更远更准更狠，代价是重了 35%、出手慢了 10%。</summary>
    public static WeaponMod ExtendedBarrel() => new()
    {
        Id = "extended_barrel",
        Name = "加长枪管",
        FitsWeapons = Guns6(),
        Part = WeaponPart.Barrel,
        Note = "接一截长管：打得远、打得准、打得透——代价是它沉，而且抬枪慢半拍。",
        Description = "多接的这截铁，让你在他看清你之前就够得着他——前提是你先把枪抬起来。",
        MaterialCosts = Cost(("iron", 3)),   // [T46] 铁 3（原：金属锭 1 + 废金属 1 = 2 + 1）。
        WorkMinutes = 240,
        WeightMultiplier = T("extended_barrel").WeightMultiplier,   // 重量 +35%
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.MaxRange, S("extended_barrel", WeaponStat.MaxRange)),               // 射程 +20%
            StatMod.Mul(WeaponStat.FalloffStart, S("extended_barrel", WeaponStat.FalloffStart)),       // 衰减起点 +20%
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("extended_barrel", WeaponStat.BaseSpreadDegrees)), // 散布 −20%
            StatMod.Mul(WeaponStat.AttackInterval, S("extended_barrel", WeaponStat.AttackInterval)),   // 攻速 −10% ⇒ 间隔 ×1.10
            StatMod.Mul(WeaponStat.Penetration, S("extended_barrel", WeaponStat.Penetration)),         // 穿透 +10%（乘算）
        },
    };

    // ---- 三种近战型态（刺刀 / 利爪 / 创伤）：一把枪三选一 ----
    // 部位安排使这条规则**双重成立**：利爪与创伤都占「枪托」⇒ 天然互斥；刺刀占「枪口」，
    // 与前两者不同部位，故还要靠 MeleeForm 的"至多一种型态"规则挡住（见 WeaponMods.ApplyMods）。
    //
    // 三者**都不再随枪身缩放**（用户改口径）：同一型态装在手枪还是狙击枪上，枪托数值一模一样。
    // 差异全在**重量代价**：刺刀 +10% ＜ 利爪 +30% ＜ 创伤 +50%，与 DPS 排序单调一致。

    /// <summary>
    /// 刺刀型（枪口）：近战 = <b>85% 攻速的刺剑</b>（[T68] 原 80%；数值从 <see cref="WeaponTable.Rapier"/> 实读）。
    /// 重枪三型态里**最快、最安静、穿透最高、伤害最低**，重量代价也最小（+10%）。
    /// </summary>
    public static WeaponMod Bayonet() => new()
    {
        Id = "bayonet",
        Name = "刺刀型",
        FitsWeapons = GunsMeleeForm(),
        Part = WeaponPart.Muzzle,
        Form = MeleeForm.Bayonet,
        Note = "枪口挂一把刺剑：捅得快、捅得透、捅得安静——就是不太痛。",
        Description = "子弹会引来一整条街，这一下不会。它安静得像一句没说出口的话。",
        MaterialCosts = Cost(("iron", 4), ("rope", 1)),   // [T46] 铁 4（原：金属锭 1 + 废金属 2 = 2 + 2）。
        WorkMinutes = 240,
        WeightMultiplier = T("bayonet").WeightMultiplier,        // 重量 +10%
        Stats = StockMeleeLike(WeaponTable.Rapier())
            .Append(StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("bayonet", WeaponStat.BaseSpreadDegrees)))   // 散布 +3%
            .ToArray(),
    };

    /// <summary>
    /// 利爪型（枪托）：近战 = <b>85% 攻速的消防斧</b>（[T68] 原 80%；数值从 <see cref="WeaponTable.Axe"/> 实读）。
    /// **单击最重**（消防斧伤害上限全型态最高），穿透中等，重量代价 +30%。
    /// </summary>
    public static WeaponMod ClawStock() => new()
    {
        Id = "claw_stock",
        Name = "利爪型",
        FitsWeapons = GunsMeleeForm(),
        Part = WeaponPart.Stock,
        Form = MeleeForm.Claw,
        Note = "枪托绑一把斧子：一下砍开的口子，比这把枪打出的洞还大。",
        Description = "打不响的时候它还能砍。末日里一把枪最该学会的，是别只把自己当枪用。",
        MaterialCosts = Cost(("iron", 3), ("leather", 1), ("nails", 2)),
        WorkMinutes = 240,
        WeightMultiplier = T("claw_stock").WeightMultiplier,     // 重量 +30%
        Stats = StockMeleeLike(WeaponTable.Axe())
            .Append(StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("claw_stock", WeaponStat.BaseSpreadDegrees)))   // 散布 +10%
            .ToArray(),
    };

    /// <summary>
    /// 创伤型（枪托）：近战 = <b>85% 攻速的尖头锤</b>（[T68] 原 80%；数值从 <see cref="WeaponTable.SpikeHammer"/> 实读）。
    /// 重枪三型态里 **DPS 最高**（<b>用户有意为之</b>：它代价最大），也**最重**（+50%）、最慢、最响。
    /// 唯一一条**反向改善射击**的型态（散布 −3%：铁疙瘩压枪更稳）。
    /// </summary>
    public static WeaponMod TraumaStock() => new()
    {
        Id = "trauma_stock",
        Name = "创伤型",
        FitsWeapons = GunsMeleeForm(),
        Part = WeaponPart.Stock,
        Form = MeleeForm.Trauma,
        Note = "枪托焊成一柄尖头锤：抡起来像铁疙瘩，砸下去也像。压枪倒是更稳了。",
        Description = "子弹留个洞，这头铁疙瘩留个印子——凹进去的那种。压枪稳了，是顺带的。",
        MaterialCosts = Cost(("iron", 4)),   // [T68·用户手改] 去掉「钉子*4」，只留铁 4（wiki 材料列 = 铁*4）。
        WorkMinutes = 240,
        WeightMultiplier = T("trauma_stock").WeightMultiplier,   // 重量 +50%（全表最重的代价）
        Stats = StockMeleeLike(WeaponTable.SpikeHammer())
            .Append(StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("trauma_stock", WeaponStat.BaseSpreadDegrees)))   // 散布 −3%（用户手改：配重让枪更稳）
            .ToArray(),
    };

    /// <summary>
    /// [T68·用户在 wiki 上新加] 锋刃型（枪托）：近战 = <b>85% 攻速的匕首</b>（数值从 <see cref="WeaponTable.Dagger"/> 实读）。
    /// <para>
    /// 🔴 <b>这是给短枪（手枪 / 冲锋枪）的近战型态 —— 重枪的刺刀/利爪/创伤它们一件都装不了</b>（白名单不相交）。
    /// 手枪贴脸时枪托本就打不出什么，固定一把匕首至少能捅；代价极小（重量 +5%、铁 1 + 绳 1、90 工时），
    /// 换来的匕首本身也是全近战最弱的一档 —— 它补的是"短枪贴脸没有近战手段"这个洞，不是给短枪加一个强力近战。
    /// </para>
    /// <para>
    /// <b>它是第四种 <see cref="MeleeForm"/></b>（<see cref="MeleeForm.Blade"/>）⇒ 自动进
    /// <see cref="WeaponMods.ApplyMods"/> 的"至多一种近战型态"互斥组（那条规则认<b>任何</b>带 Form 的改装，非硬编码三种）。
    /// 占**枪托**部位（与利爪/创伤同部位，但它们白名单不相交、装不到同一把枪上，故部位不会真冲突）。
    /// </para>
    /// </summary>
    public static WeaponMod BladeStock() => new()
    {
        Id = "blade_stock",
        Name = "锋刃型",
        FitsWeapons = GunsBladeForm(),                           // 手枪 / 冲锋枪（与重枪三型态不相交）
        Part = WeaponPart.Stock,
        Form = MeleeForm.Blade,
        Note = "将利刃固定在握把上，在近身时出其不意。",
        Description = "握手的地方藏了刀，问候语就变了。等他伸手推开你的枪口，才发现推错了地方。",
        MaterialCosts = Cost(("iron", 1), ("rope", 1)),
        WorkMinutes = 90,
        WeightMultiplier = T("blade_stock").WeightMultiplier,    // 重量 +5%
        Stats = StockMeleeLike(WeaponTable.Dagger()),            // 无额外散布改动（wiki 只写了重量 +5%）
    };

    // ============ 近战锐器改装（6 条）============

    /// <summary>
    /// 锯齿剑刃：穿透 −20%，**造成的流血速度 +40%**。✅ [T53] 流血轴已落地，两条效果都真了。
    ///
    /// <para>
    /// <b>用户的设计意图（事实源，一字不改）</b>：「**我的设计是，锯齿剑刃是用来对付无甲或者轻甲目标，边打边跑**」
    /// ⇒ 它**不是**通用上位替代，而是一把**挑食**的武器：对无甲/轻甲强，对重甲弱。
    /// </para>
    /// <para>
    /// <b>穿透 −20% 是这条梯度的唯一来源</b>，而且它是一根**只对重甲有抓地力**的精准杠杆（Sim 实测，见
    /// <c>docs/research/2026-07-14-bleed-axis.md</c> §4）：
    /// <list type="bullet">
    /// <item>**无甲**：身上一层护甲都没有 ⇒ 穿透**在结算里根本不被读取** ⇒ 穿透惩罚对它**完全免费**。</item>
    /// <item>**丧尸**：腐皮太薄（锐防 1.5）⇒ 穿透对它**也没有抓地力**。丧尸＝轻目标＝设计正靶，本就该强。</item>
    /// <item>**重甲**：唯一需要修、也是唯一修得动的一格。</item>
    /// </list>
    /// ⇒ 划不进甲 ⇒ 没有伤口 ⇒ 没有流血。**护甲正是它的克星**，所以 <c>BleedModel</c> 那条闸门注释
    /// 担心的"伤害与护甲失去意义"不会发生 —— 前提是穿透惩罚**够大**。
    /// </para>
    /// <para>
    /// ⚠️ <b>穿透 −20% 是【乘算】</b>（用户口径：「在原本数值上 −该数值的 10%，例如 20% 变成 18%」）——
    /// 长剑 24% → 19.2%，**不是** 24% − 20% = 4%。别手一贱改成 <c>Add(-0.20)</c>。
    /// </para>
    /// </summary>
    public static WeaponMod SerratedBlade() => new()
    {
        Id = "serrated_blade",
        Name = "锯齿剑刃",
        FitsWeapons = SerratedFits(),     // 用户划掉刺剑（突刺剑开锯齿没意义）；消防斧按"同档"勾上
        Part = WeaponPart.Blade,
        Note = "刃上开一排锯齿：不好破甲，但它撕开的口子不肯收。打无甲和轻甲的，边打边跑。",
        Description = "破不开甲，可只要划进肉里，那道口子就再也合不拢。它不杀人，它让人慢慢想通。",
        MaterialCosts = Cost(),           // 用户清空
        WorkMinutes = 240,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.Penetration, S("serrated_blade", WeaponStat.Penetration)),            // 穿透 −20%（**乘算**）
            StatMod.Mul(WeaponStat.BleedRateMultiplier, S("serrated_blade", WeaponStat.BleedRateMultiplier)),    // 造成的流血速度 +40%（[T53] 引擎轴）
        },
    };

    /// <summary>
    /// 锋刃研磨：<b>穿透 +75%，攻击三次后失去该改装</b> —— 全表**唯一的消耗型改装**（用户点名）。
    ///
    /// <para>
    /// <b>用光就脱落</b>：第 3 下砍完，刀刃就磨钝了，改装从武器上摘掉（回到没研磨的状态），玩家会收到提示。
    /// 次数是**武器实例上的状态**（见 <c>ModdedWeaponRegistry</c> 的耐久层），不是目录数据，也不在
    /// <c>ModdedWeaponSpec</c> 里 —— 那样会污染 <c>Rebuild</c> 的纯函数语义。
    /// </para>
    /// <para>
    /// 材料为空 + 只要 60 工时 = 它就该是**出门前磨一次刀**这种小事：便宜、快、可反复做，用完再磨。
    /// </para>
    /// </summary>
    public static WeaponMod HonedEdge() => new()
    {
        Id = "honed_edge",
        Name = "锋刃研磨",
        FitsWeapons = Blades6WithAxe(),   // 六把锐器 + 消防斧都能磨；棍棒不行（钝器没有"刃"可开）
        Part = WeaponPart.Blade,
        Note = "把刃口磨到能刮胡子。它能透甲——但也就三下的事。",
        Description = "磨到能刮胡子的刃，能剖开任何甲——三下之内。之后它只记得自己曾经很锋利，仅此而已。",
        MaterialCosts = Cost(),    // 用户清空：一块磨刀石反复用，不算消耗
        WorkMinutes = 60,
        UsesBeforeBreak = 3,       // 🔴 攻击三次后失去该改装（用户拍板）
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.Penetration, S("honed_edge", WeaponStat.Penetration)),            // 穿透 +75%（乘算；上限 100% 由 Build 的 Clamp 兜住）
        },
    };

    /// <summary>镂空剑刃：开血槽减重 25% → 攻速 +15%、伤害 −9%。</summary>
    public static WeaponMod FullerBlade() => new()
    {
        Id = "fuller_blade",
        Name = "镂空剑刃",
        FitsWeapons = FullerFits(),       // 用户划掉刺剑；**消防斧刻意不勾**（镂空会挖掉消防斧赖以成立的头部质量）
        Part = WeaponPart.Blade,
        Note = "剑身上开一道血槽：轻了四分之一，挥得更快——砍得也更浅。",
        Description = "剑身掏空四分之一，出手快了一截，砍进去也浅了一截。快是给活人看的，深是留给敌人的。",
        MaterialCosts = Cost(("iron", 1)),
        WorkMinutes = 240,
        WeightMultiplier = T("fuller_blade").WeightMultiplier,    // 重量 −25%（全表减重最多）
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, S("fuller_blade", WeaponStat.AttackInterval)),         // 攻速 +15% ⇒ 间隔 ×0.85
            StatMod.Mul(WeaponStat.DamageMin, S("fuller_blade", WeaponStat.DamageMin)),              // 伤害 −9%
            StatMod.Mul(WeaponStat.DamageMax, S("fuller_blade", WeaponStat.DamageMax)),
        },
    };

    /// <summary>加重剑柄：柄里灌铅 → 伤害 +6%，重量 +18%。（用户删掉了原有的攻速惩罚——重量就是它的代价。）</summary>
    public static WeaponMod WeightedHandle() => new()
    {
        Id = "weighted_handle",
        Name = "加重剑柄",
        FitsWeapons = Blades6WithAxe(),   // 含消防斧（用户拍板：与长剑同档）
        Part = WeaponPart.Handle,
        Note = "柄里灌铅配重：每一下都更沉。你的胳膊也知道。",
        Description = "柄里灌了铅，每一下都更实在。你的肩膀替这份实在付账，付一整天。",
        MaterialCosts = Cost(("iron", 1)),
        WorkMinutes = 120,
        WeightMultiplier = T("weighted_handle").WeightMultiplier,  // 重量 +18%
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.DamageMin, S("weighted_handle", WeaponStat.DamageMin)),              // 伤害 +6%
            StatMod.Mul(WeaponStat.DamageMax, S("weighted_handle", WeaponStat.DamageMax)),
        },
    };

    /// <summary>轻质化剑柄：木柄换皮缠 → 攻速 +3%，重量 −12%。</summary>
    public static WeaponMod LightenedHandle() => new()
    {
        Id = "lightened_handle",
        Name = "轻质化剑柄",
        FitsWeapons = Blades6WithAxe(),   // 含消防斧（用户拍板：与长剑同档）
        Part = WeaponPart.Handle,
        Note = "换一副轻木柄：省下的那点分量，胳膊记得住。",
        Description = "省下的那点分量攒到傍晚，就是你还举得动、而对面举不动的那点差别。",
        MaterialCosts = Cost(("wood", 1), ("leather", 1)),
        WorkMinutes = 120,
        WeightMultiplier = T("lightened_handle").WeightMultiplier, // 重量 −12%
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, S("lightened_handle", WeaponStat.AttackInterval)),         // 攻速 +3% ⇒ 间隔 ×0.97
        },
    };

    /// <summary>
    /// 防滑缠手：攻速 +5%。<b>锐器 6 把 + 钝器 3 把共用这一条</b>（用户把原来同名的两条合并了）——
    /// 历史包袱（"同名两条改装、按名索引会撞"）就此消除。
    /// </summary>
    public static WeaponMod GripWrapBlade() => new()
    {
        Id = "grip_wrap_blade",
        Name = "防滑缠手",
        FitsWeapons = BladesAndBlunts(),   // 用户合并：刃类 6 + 钝类 3
        Part = WeaponPart.Grip,
        Note = "缠一圈布和绳：手不打滑，出手就利落。近战必中，所以「更准」在这里只能是「更快」。",
        Description = "近战一挥必中，所以缠手买不来准头，只买来快。手不打滑的人，收刀总比别人早半拍。",
        MaterialCosts = Cost(("rope", 1)),
        WorkMinutes = 60,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, S("grip_wrap_blade", WeaponStat.AttackInterval)),         // 攻速 +5% ⇒ 间隔 ×0.95（用户在表上写的就是这个等式）
        },
    };

    // ============ 近战钝器改装（2 条，棍棒独有）============
    //
    // 🔴 用户拍板的互斥：「**钉子强化是棍棒独有的，而棍棒不能锋刃研磨**」
    //    ⇒ 钉子/铁丝强化的白名单 = 只有棍棒；锋刃研磨的白名单 = 六把锐器（不含棍棒）。
    //    两边各自钉死，护栏见 WeaponModWhitelistTests。语义也自洽：钝器没有"刃"可开。

    /// <summary>铁丝强化：棍身缠铁丝 → 伤害 +10%，重量 +5%。</summary>
    public static WeaponMod WireWrap() => new()
    {
        Id = "wire_wrap",
        Name = "铁丝强化",
        FitsWeapons = ClubOnly(),
        Part = WeaponPart.Shaft,
        Note = "棍身缠满铁丝：还是一根棍子，但它现在咬人。",
        Description = "还是那根打人的棍子，只是现在它咬回来。",
        MaterialCosts = Cost(("wire", 2)),
        WorkMinutes = 60,
        WeightMultiplier = T("wire_wrap").WeightMultiplier,       // 重量 +12%（用户在 wiki 上又调过一次）
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.DamageMin, S("wire_wrap", WeaponStat.DamageMin)),              // 伤害 +15%（用户在 wiki 上又调过一次）
            StatMod.Mul(WeaponStat.DamageMax, S("wire_wrap", WeaponStat.DamageMax)),
        },
    };

    /// <summary>
    /// 钉子强化：棍头钉钉子 → <b>穿透 +0.03（加算）</b>。
    ///
    /// <para>
    /// 🔴🔴 <b>这是全项目"百分比一律乘算"铁律的【唯一例外】，而且是用户亲自点名的：</b>
    /// 用户原话「**钉子强化：穿透 +0.03 是因为棍棒原本是 0 穿透**」。
    /// </para>
    /// <para>
    /// <b>为什么必须加算 —— "零陷阱"</b>：棍棒的 <c>Penetration</c> 是 <b>0</b>（一根木头，破甲为零）。
    /// 乘算在零上**永远是零**：<c>0 × 1.75 = 0</c>、<c>0 × 100 = 0</c>。
    /// 想让"钉尖能破一点甲"这件事成立，只能加算。别人手一贱把它改成 <c>Mul</c>，这条改装当场变成一件废件
    /// （护栏 <c>WeaponModTests.NailStuds_OnClub_AddsPenetration</c> 会红）。
    /// </para>
    /// <para>
    /// ✅ <b>[T53]「造成伤害时 25% 几率造成小流血」已落地</b>（<see cref="WeaponStat.BleedOnHitChance"/>）。
    /// 🔴 <b>[T58]「小流血」的语义已改</b>：它现在就是三级流血里最轻的那一级
    /// （<c>BleedModel.BleedSeverity.Small</c>，流速 0.3；旧的 <c>SmallWoundRateMultiplier = 0.5</c>
    /// 「速率减半的普通伤口」**已退役**）。
    /// ⇒ 钉子扎出的口子和别的小流血**按同一套规则合并**：同一部位扎两下 ⇒ **中流血**、再来一下 ⇒ **大流血**（封顶）。
    /// 它仍按命中部位定致命性（躯干上的小流血照样能把人放干，只是很慢）。
    /// </para>
    /// <para>
    /// 这是**钝器唯一的流血来源**：棍棒本来一处伤口都造不出来（引擎的流血资格要求**锐器抵达**），
    /// 钉子强化正是要给钝器开一个小口子。
    /// </para>
    /// </summary>
    public static WeaponMod NailStuds() => new()
    {
        Id = "nail_studs",
        Name = "钉子强化",
        FitsWeapons = ClubOnly(),   // 用户拍板：钉子强化是棍棒独有的
        Part = WeaponPart.Shaft,
        Note = "棍头砸进一圈钉子：木头砸不穿的，钉尖能——而且它会留下不肯收口的小口子。",
        Description = "木头砸不穿的地方，钉尖替它记着。砸下去的每一下，都在里面留了个纪念品。",
        MaterialCosts = Cost(("nails", 4)),
        WorkMinutes = 60,
        Stats = new[]
        {
            // 🔴 加算，不是乘算 —— 见类注的"零陷阱"。这是 CLAUDE.md 乘算铁律的唯一例外，用户点名的。
            //    运算方式 Add 留代码（结构），只有 +0.03 这个数外置。
            StatMod.Add(WeaponStat.Penetration, S("nail_studs", WeaponStat.Penetration)),

            // [T53] 造成伤害时 25% 几率造成小流血。
            // 🔴 这里**也必须是 Set/Add 而非 Mul**：基础武器的 BleedOnHitChance 是 **0** ——
            //    又一个"零陷阱"，乘算在零上永远是零（0 × 0.25 = 0），改装会静默变成废件。
            StatMod.Set(WeaponStat.BleedOnHitChance, S("nail_studs", WeaponStat.BleedOnHitChance)),
        },
    };

    // ============ [T69] 近身锐器·防御型改装（1 条：护手挡格）============
    //
    // 🔴 它带的不是 StatMod，而是**承伤入口的一次整发否决**（拿武器的手被选为受击部位 → 50% 无效）。
    //    落成 WeaponMod.HandGuardNegateChance，判定走 WeaponModDefense.HandGuardNegates，
    //    接线在 CombatEngine.ResolveHit（部位在那里选定、伤害其后施加，故否决必须落在选部位之后）。

    /// <summary>护手挡格：拿武器的手（连同手指）被选为受击部位时 50% 整发无效；重量 +10%。适配匕首/短剑/刺剑。</summary>
    public static WeaponMod Handguard() => new()
    {
        Id = "handguard",
        Name = "护手挡格",
        FitsWeapons = Names("匕首", "短剑", "刺剑"),
        Part = WeaponPart.Grip,
        Note = "不仅是装饰品，更能保护你脆弱的手。",
        Description = "花哨的护手不为好看——是为了让你握枪的那只手，明天还握得住枪。",
        MaterialCosts = Cost(("iron", 2), ("leather", 1)),
        WorkMinutes = 90,
        WeightMultiplier = T("handguard").WeightMultiplier,        // 重量 +10%
        HandGuardNegateChance = T("handguard").HandGuardNegateChance, // 武器手受击时 50% 整发否决（数值拟定待调）
    };

    // ============ [T69] 弓弩专属改装（4 条：弓臂缠手 / 复合弓臂 / 重磅弓弦 / 弩盾）============
    //
    // 归类进新的 Archery 组（见 For(WeaponClass) 之外的 ArcheryMods()）。它们的白名单是**弓/弩**（吃箭的远程）——
    // 与 [T29]「弓弩不吃枪械改装」不冲突：枪械改装白名单不含弓弩，这四条是弓弩自己的专属改装。
    // 部位：弓臂缠手=缠手(LimbWrap)、复合弓臂=弓(Bow)、重磅弓弦=弦(String)、弩盾=弩身(CrossbowBody)。
    // 🔴 用户拍板：弓臂缠手(缠手) 与 复合弓臂(弓) **不互斥、可同装一把弓**（两个不同部位）。

    /// <summary>弓臂缠手：弓臂缠一层布，攻速 +5%（间隔 ×0.95）。适配短弓/反曲弓/长弓/狩猎弓。</summary>
    public static WeaponMod LimbWrap() => new()
    {
        Id = "limb_wrap",
        Name = "弓臂缠手",
        FitsWeapons = Names("短弓", "反曲弓", "长弓", "狩猎弓"),
        Part = WeaponPart.LimbWrap,
        Note = "更加柔和的手感，更好把握弓臂。",
        Description = "缠一层布在弓臂上，冰冷的木头就有了点体温。射出去的还是要命的东西，握着的却不那么硌手了。",
        MaterialCosts = Cost(("cloth", 2), ("leather", 1)),
        WorkMinutes = 60,
        // wiki「攻击速度+5%，。」末尾空悬顿号疑似漏填，用户未补 ⇒ 只落攻速 +5%（无重量、无其他）。
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, S("limb_wrap", WeaponStat.AttackInterval)),         // 攻速 +5% ⇒ 间隔 ×0.95
        },
    };

    /// <summary>复合弓臂：重量 +15% / 攻速 −6% / 伤害 +8% / 穿透 +8% / 弹丸飞速 +12%。适配短弓/长弓。</summary>
    public static WeaponMod CompoundLimbs() => new()
    {
        Id = "compound_limbs",
        Name = "复合弓臂",
        FitsWeapons = Names("短弓", "长弓"),
        Part = WeaponPart.Bow,
        Note = "拉开它可需要不小的力气，但相信我，辛苦是值得的。",
        Description = "拉开它得用上全身的劲，松手那一刻却安静得可怕。辛苦都堆在拉弦的三秒里，飞出去的那一下替你把话说完。",
        MaterialCosts = Cost(("wood", 2), ("glue", 2)),
        WorkMinutes = 180,
        WeightMultiplier = T("compound_limbs").WeightMultiplier,   // 重量 +15%
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, S("compound_limbs", WeaponStat.AttackInterval)),         // 攻速 −6% ⇒ 间隔 ×1.06
            StatMod.Mul(WeaponStat.DamageMin, S("compound_limbs", WeaponStat.DamageMin)),              // 伤害 +8%
            StatMod.Mul(WeaponStat.DamageMax, S("compound_limbs", WeaponStat.DamageMax)),
            StatMod.Mul(WeaponStat.Penetration, S("compound_limbs", WeaponStat.Penetration)),            // 穿透 +8%
            StatMod.Mul(WeaponStat.FlightSpeed, S("compound_limbs", WeaponStat.FlightSpeed)),            // 弹丸飞速 +12%（与《弓与箭之道》+20% 自动连乘）
        },
    };

    /// <summary>
    /// 重磅弓弦：攻速 −6% / 伤害 +4% / 散布 −8% / 弹丸飞速 +12% / **衰减率 −18%**（映射为末端伤害系数提高）。
    /// 适配 5 把弓（短弓/反曲弓/长弓/竞技复合弓/狩猎弓）。
    /// <para>
    /// 🔴 「衰减率 −18%」的落点（用户拍板：映射为 FalloffFloor 末端保留比例提高）：以单条
    /// <c>Mul(FalloffFloor, 1.18)</c> 近似——末端保留系数提高即"打远了伤害掉得更少"。
    /// **数值拟定待 Sim 校准**：精确等价于"衰减率 −18%"需 Sim 拉表核对（末端保留的合适档位由 Build 的
    /// Clamp(0.01,1) 兜住不越界）。
    /// </para>
    /// </summary>
    public static WeaponMod HeavyBowstring() => new()
    {
        Id = "heavy_bowstring",
        Name = "重磅弓弦",
        FitsWeapons = Names("短弓", "反曲弓", "长弓", "竞技复合弓", "狩猎弓"),
        Part = WeaponPart.String,
        Note = "当心手指。",
        Description = "弦更硬，射得更远也更狠。当心手指——它不分敌我。",
        MaterialCosts = Cost(("rope", 2)),
        WorkMinutes = 90,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, S("heavy_bowstring", WeaponStat.AttackInterval)),         // 攻速 −6% ⇒ 间隔 ×1.06
            StatMod.Mul(WeaponStat.DamageMin, S("heavy_bowstring", WeaponStat.DamageMin)),              // 伤害 +4%
            StatMod.Mul(WeaponStat.DamageMax, S("heavy_bowstring", WeaponStat.DamageMax)),
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, S("heavy_bowstring", WeaponStat.BaseSpreadDegrees)),      // 散布 −8%
            StatMod.Mul(WeaponStat.FlightSpeed, S("heavy_bowstring", WeaponStat.FlightSpeed)),            // 弹丸飞速 +12%
            StatMod.Mul(WeaponStat.FalloffFloor, S("heavy_bowstring", WeaponStat.FalloffFloor)),           // 衰减率 −18% ≈ 末端保留提高（拟定待 Sim 校准）
        },
    };

    /// <summary>
    /// 弩盾：重量 +50%；举弩时来自**正面 120°**（半角 60°）的**远程**攻击 25% 整发无效。适配双手重弩/复合弩。
    /// <para>否决不是 StatMod —— 见 <see cref="WeaponMod.FrontalRangedNegateChance"/>，判定走
    /// <see cref="WeaponModDefense.FrontalRangedNegates"/>，接线在 <c>Actor.ReceiveAttack</c>（与半身掩体同层）。</para>
    /// </summary>
    public static WeaponMod CrossbowShield() => new()
    {
        Id = "crossbow_shield",
        Name = "弩盾",
        FitsWeapons = Names("双手重弩", "复合弩"),
        Part = WeaponPart.CrossbowBody,
        Note = "灵感来自意大利。",
        Description = "意大利人早想通了：与其射得快，不如活到能再射一发。这块铁挡在弩前，也挡在你和一支飞来的箭之间。",
        MaterialCosts = Cost(("iron", 4), ("leather", 2), ("nails", 4)),
        WorkMinutes = 180,
        WeightMultiplier = T("crossbow_shield").WeightMultiplier,  // 重量 +50%
        FrontalRangedNegateChance = T("crossbow_shield").FrontalRangedNegateChance, // 正面远程 25% 整发否决（数值拟定待调）
        FrontalNegateHalfAngleDeg = T("crossbow_shield").FrontalNegateHalfAngleDeg, // 正面 120° ⇒ 半角 60°
    };

    /// <summary>弓弩专属改装（4 条）。归入 <see cref="All"/>，UI 单独分栏；不进 <see cref="LegacyClassOf"/> 的迁移三组（它们是收窄后新加的）。</summary>
    public static IReadOnlyList<WeaponMod> Archery() => new[]
    {
        LimbWrap(), CompoundLimbs(), HeavyBowstring(), CrossbowShield(),
    };

    /// <summary>
    /// 这条改装**归哪一组** —— 只剩两个用途：给 <see cref="LegacyClassOf"/> 定组、给改装 UI 分栏。
    /// <para>
    /// ⚠️ <b>它已经不是装配约束了</b>：能不能装看 <see cref="WeaponMod.FitsWeapons"/>（用户逐格勾的白名单）。
    /// 「防滑缠手」现在**同时能装锐器和钝器**，却只归在 Blade 这一组 —— 分组与白名单已经**不再等价**，
    /// 别再拿分组去推"这条改装能装什么"。
    /// </para>
    /// <para>
    /// [T47] <b>「防滑缠手（钝器）」<c>grip_wrap_blunt</c> 已按用户的删除标记撤下</b>：
    /// 它的职能并进了 <see cref="GripWrapBlade"/>（白名单扩到含棍棒/尖头锤/破甲锤）。
    /// ⇒ 「同名两条改装」这个历史包袱（按名索引会撞）就此消失，全表 <b>14 条</b>。
    /// </para>
    /// </summary>
    public static IReadOnlyList<WeaponMod> For(WeaponClass cls) => cls switch
    {
        WeaponClass.Firearm => new[]
        {
            LightenedStock(), SawnOffBarrel(), ExtendedBarrel(), Bayonet(), ClawStock(), TraumaStock(),
            BladeStock(),   // [T68] 短枪（手枪/冲锋枪）专属近战型态
        },
        WeaponClass.Blade => new[]
        {
            SerratedBlade(), HonedEdge(), FullerBlade(), WeightedHandle(), LightenedHandle(), GripWrapBlade(),
            Handguard(),   // [T69] 护手挡格（匕首/短剑/刺剑）：武器手受击 50% 整发否决
        },
        WeaponClass.Blunt => new[]
        {
            WireWrap(), NailStuds(),
        },
        _ => System.Array.Empty<WeaponMod>(),
    };

    /// <summary>
    /// 某**具体武器**可用的全部改装 —— **按白名单查，不再按大类派生**。
    /// <para>这是装配约束的唯一查询入口：<c>CraftingService</c>（能不能做）与
    /// <c>ModdedWeaponRegistry.Rebuild</c>（老存档能不能还原）都走它。
    /// 用户在 wiki 上把某把枪从某条改装的白名单里划掉，这里立刻就不再列出它。</para>
    /// </summary>
    public static IReadOnlyList<WeaponMod> For(Weapon weapon)
        => weapon is null
            ? System.Array.Empty<WeaponMod>()
            : All().Where(m => m.FitsWeapons.Contains(weapon.Name)).ToList();

    /// <summary>全部改装（含跨类同名"防滑缠手"两条）。</summary>
    public static IReadOnlyList<WeaponMod> All()
    {
        var all = new List<WeaponMod>();
        all.AddRange(For(WeaponClass.Firearm));
        all.AddRange(For(WeaponClass.Blade));
        all.AddRange(For(WeaponClass.Blunt));
        all.AddRange(Archery());   // [T69] 弓弩专属 4 条（弓臂缠手/复合弓臂/重磅弓弦/弩盾）
        return all;
    }
}
