using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 斯图尔特家族庄园（[SPEC-T51]）——<b>「高风险不是永远高回报」的正面兑现</b>。
///
/// <para><b>用户原话（authored 唯一事实源，一字不改）</b>：「斯图尔特家族庄园（农庄，并不是很富裕，中地图，
/// 有盘踞的劫掠者和岗哨，高危，高风险不是永远高回报，这个调查点最富裕的地方是劫掠者们的装备和衣服，
/// 并且这里会有斯图尔特家的一些剧情，讲述了他们好心收留一些流浪者，结果被背刺，女儿妻子被奸杀，
/// 男性尸体吊挂在门口喂丧尸，在枯井底有抱着婴儿饿死的女性尸体）」</para>
///
/// <para><b>本类钉死的三条，都是"别把这一关平衡掉"的护栏</b>：
/// <list type="number">
/// <item><b>农庄是穷的</b>（§① 贫困护栏）：10 处搜刮点掉出来的东西<b>一把枪、一本书、一枚白银、
/// 一支抗生素、一只急救包都没有</b>——这不是"还没投放"，这是<b>设定</b>。用户明写「并不是很富裕」，
/// 谁日后为了"让高危配得上高回报"往这儿塞高价值物资，当场变红。</item>
///
/// <item><b>回报长在人身上</b>（§② 战利品护栏）：「最富裕的地方是<b>劫掠者们的装备和衣服</b>」⇒
/// 7 个劫掠者<b>人人持械、人人穿衣</b>，且每一件都<b>回查得到</b>（武器在 <see cref="ModdedWeaponRegistry"/>、
/// 衣服在 <see cref="ApparelCatalog"/>）⇒ <see cref="CorpseLoot.Strip"/> 必掉零掷骰。<b>先打赢，才有得扒。</b></item>
///
/// <item><b>开枪＝把整个庄园叫醒</b>（§③ 噪音几何）：哨位间距是<b>照着枪声半径设计的</b>——
/// 庭院中央放一箭没人听见（弓 70）、开一枪招来三个（手枪 350）、抡起步枪叫醒六个（600）。
/// 「枪纸面最强，但一开枪就没有『逐个清哨』了」在这一关同样成立。</item>
/// </list></para>
///
/// <para>🔴 <b>「打赢劫掠者白捡一身装备」这个场景不存在</b>（<c>docs/research/2026-07-14-combat-cost.md</c>）：
/// 中甲+长剑 vs 持棍棒劫掠者胜率 96.5% 看着白送，但 <b>66% 的胜场留下骨折</b>（愈合 7 昼夜，占床、不能干活、
/// 不能站岗）；vs 持破甲锤 70.8%、<b>13% 断肢</b>；vs 持手枪只有 <b>26.2%</b>。而连场<b>不能拿胜率相乘</b>——
/// 单场 68% 的对手，不治疗连打，能撑过第 3 个的只剩 3.5%。⇒ 玩家实际会清掉两三个就撤，
/// 这正是「高风险不是永远高回报」。</para>
///
/// <para><b>Sim 零漂移（结构性）</b>：本关<b>一个新武器、一件新护甲、一条新规则都没造</b>——
/// 武器全部走 <see cref="WeaponTable"/> 权威表、衣服全部走 <see cref="ArmorTable"/>（§② 的回查护栏就是这条的证明），
/// 且 <c>StuartManor.cs</c> <b>不被 <c>DeadSignal.Sim</c> Link</b>（与 <c>GoldfingerGang.cs</c> 不同）⇒
/// Sim 的结算路径根本读不到它。</para>
/// </summary>
public class StuartManorTests
{
    // ============ ① 农庄是穷的：这不是"还没投放"，这是设定 ============

    /// <summary>中地图 ⇒ 10 处搜刮点（Medium 下限，同加油站先例）：<b>点位数量合规，单点产出薄</b>。</summary>
    [Fact]
    public void Manor_IsAMediumSite_WithTenCaches()
    {
        IReadOnlyList<string> ids = ExplorationCache.CacheIdsFor(StuartManor.DestinationName);
        Assert.Equal(10, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count()); // id 不撞名（撞名＝后一处顶掉前一处，静默蒸发）
    }

    /// <summary>
    /// 🔴 <b>高危</b>（用户原话直给，不是推的）。这条<b>不是装饰</b>：<c>WorldMapPanel.Destination.Danger</c> 是<b>可空</b>字段，
    /// 漏赋值就是"<b>未定级</b>"——地图上什么都不显示，而玩家<b>看不出这里会死人</b>。
    /// <para>值放在纯逻辑侧（<see cref="StuartManor.Danger"/>）而不是只写在地图行里，正是为了能被<b>钉死</b>：
    /// <c>WorldMapPanel</c> 带 Godot 依赖、脱不了单测，写在那儿的常量没人守得住（这一漏就漏过一次）。</para>
    /// </summary>
    [Fact]
    public void Manor_IsHighDanger_AndTheMapKnowsIt()
    {
        Assert.Equal(DangerTier.High, StuartManor.Danger);
        Assert.Equal("高危", ExplorationProgress.DangerLabel(StuartManor.Danger));
    }

    /// <summary>每处搜刮点都有一次性 flag（否则完成度算不出来、也会被重复搜）。</summary>
    [Fact]
    public void EveryCache_HasAOneShotFlag()
    {
        foreach (string id in ExplorationCache.CacheIdsFor(StuartManor.DestinationName))
        {
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), id);
        }
    }

    /// <summary>
    /// 🔴 <b>贫困护栏</b>：把这 10 处全搜一遍，掉出来的东西里<b>没有武器、没有书、没有白银、没有高阶医疗</b>。
    /// 用户明写「农庄……<b>并不是很富裕</b>」「高风险<b>不是</b>永远高回报」——这一关的立意就靠这条守着。
    /// 谁往这儿塞一把枪、一本书、一枚白银，这条当场红。
    /// </summary>
    [Fact]
    public void ManorCaches_AreDirtPoor_NoWeaponsNoBooksNoSilverNoHighTierMeds()
    {
        var flags = new StoryFlags();
        var everything = new List<LootItem>();
        foreach (string id in ExplorationCache.CacheIdsFor(StuartManor.DestinationName))
        {
            CacheResult r = Assert.IsType<CacheResult>(ExplorationCache.Resolve(id, flags)!);
            everything.AddRange(r.Loot);
        }

        Assert.DoesNotContain(everything, l => l.Kind == LootKind.Weapon);
        Assert.DoesNotContain(everything, l => l.Kind == LootKind.Book);
        Assert.DoesNotContain(everything, l => l.Kind == LootKind.Armor);

        string[] banned = { Materials.CurrencyKey, "first_aid_kit", "antibiotics", "gunpowder" };
        foreach (string key in banned)
        {
            Assert.DoesNotContain(everything, l => l.Kind == LootKind.Material && l.RefId == key);
        }

        // 弹药也没有：农庄不产子弹，枪在人手里（而人身上的枪掉下来是空的）。
        Assert.DoesNotContain(everything, l => l.Kind == LootKind.Material && l.RefId.StartsWith("ammo_", StringComparison.Ordinal));
    }

    /// <summary>搜过一次就没了（flag 去重）——二访是空搜，不是刷。</summary>
    [Fact]
    public void SearchedCache_YieldsNothingTheSecondTime()
    {
        var flags = new StoryFlags();
        string id = ExplorationCache.CacheIdsFor(StuartManor.DestinationName)[0];

        CacheResult first = Assert.IsType<CacheResult>(ExplorationCache.Resolve(id, flags)!);
        flags.Set(first.StoryFlag, "true");

        Assert.Null(ExplorationCache.Resolve(id, flags));
    }

    /// <summary>完成度按 10 处搜刮点聚合（庄园没有剧情尸体<b>发现点</b>之外的登记点；叙事点不计入，同既有口径）。</summary>
    [Fact]
    public void Completion_CountsTheTenCaches()
    {
        (int done, int total) = ExplorationProgress.Completion(StuartManor.DestinationName, new StoryFlags(), christineLeftForRevenge: false);
        Assert.Equal(0, done);
        Assert.Equal(10, total);
    }

    /// <summary>
    /// 主屋·储藏间是**通用薄材料点**（用户拍板：原「一罐豆子/罐头」点改成不涉具体人物/前史的通用杂物点，
    /// 保住庄园 10 处下限）：只掉薄材料（破布 + 木料），<b>不掉食物、不掉罐头</b>，且非空。
    /// </summary>
    [Fact]
    public void StuartPantry_IsGenericThinMaterialStoreroom_NoFood()
    {
        CacheResult r = Assert.IsType<CacheResult>(
            ExplorationCache.Resolve(ExplorationCache.StuartPantryId, new StoryFlags())!);

        Assert.NotEmpty(r.Loot);
        Assert.All(r.Loot, l => Assert.Equal(LootKind.Material, l.Kind));   // 全是材料：不再有食物/罐头
        Assert.DoesNotContain(r.Loot, l => l.RefId == "canned_food");
        Assert.DoesNotContain(r.Loot, l => l.RefId == "beans");
        Assert.Contains(r.Loot, l => l.RefId == "cloth");
        Assert.Contains(r.Loot, l => l.RefId == "wood");
    }

    // ============ ② 回报长在人身上：先打赢，才有得扒 ============

    /// <summary>编制＝7 名劫掠者，其中 3 名<b>岗哨</b>（用户原话「有盘踞的劫掠者<b>和岗哨</b>」）。</summary>
    [Fact]
    public void Roster_IsSevenRaiders_ThreeOfThemSentries()
    {
        Assert.Equal(7, StuartManor.Roster.Count);
        Assert.Equal(3, StuartManor.Roster.Count(r => r.IsSentry));
    }

    /// <summary>布点与编制<b>同序同长</b>（错位＝拿破甲锤的那个站到了大门口，"算出来的据点"就不是"打进去的据点"）。</summary>
    [Fact]
    public void Posts_AlignWithRoster()
    {
        Assert.Equal(StuartManor.Roster.Count, StuartManor.Posts.Count);
        Assert.All(StuartManor.Posts, p =>
        {
            Assert.InRange(p.X, 0.0, 1.0);
            Assert.InRange(p.Y, 0.0, 1.0);
        });
    }

    /// <summary>
    /// 🔴 <b>杀了能扒</b>：7 个人<b>人人持械、人人穿衣</b>，且身上每一件都<b>回查得到</b>
    /// （武器 → <see cref="ModdedWeaponRegistry.WeaponByName"/>；衣服 → <see cref="ApparelCatalog.IsApparel"/>）
    /// ⇒ <see cref="CorpseLoot.Strip"/> 必掉零掷骰。
    /// <para>这一条同时是 <b>Sim 零漂移的结构性证明</b>：全部装备都回查得到 ⇒ 本关<b>一个新武器/新护甲数值都没造</b>
    /// ⇒ <c>WeaponTable.Arsenal</c> 一格未动 ⇒ Sim 按 <c>seed+idx*7919</c> 切的单元逐位不变。</para>
    /// </summary>
    [Fact]
    public void EveryRaider_DropsHisWeaponAndHisClothes()
    {
        foreach (ManorRaider raider in StuartManor.Roster)
        {
            Weapon w = StuartManor.WeaponFor(raider.Arm);
            IReadOnlyList<ArmorLayer> worn = StuartManor.ApparelFor(raider.Outfit);

            IReadOnlyList<LootItem> loot = CorpseLoot.Strip(worn, new[] { w });

            Assert.Contains(loot, l => l.Kind == LootKind.Weapon && l.RefId == w.Name);
            Assert.True(loot.Count(l => l.Kind == LootKind.Armor) >= 2, $"{raider.DisplayName} 身上扒不出两件衣服");

            // 每一件都必须是"装得上/穿得上"的东西——扒下来却用不了的是垃圾，不该进背包。
            Assert.NotNull(ModdedWeaponRegistry.WeaponByName(w.Name));
            Assert.All(worn, l => Assert.True(ApparelCatalog.IsApparel(l.Name), l.Name));
        }
    }

    /// <summary>
    /// 「<b>最富裕的地方是劫掠者们的装备和衣服</b>」：一趟全清（理论上）＝<b>7 种不同的武器</b>
    /// ——全图武器投放最厚的一处，而它<b>一件都不在搜刮点里</b>，全长在人身上。
    /// </summary>
    [Fact]
    public void TheRealLoot_IsOnThePeople_SevenDistinctWeapons()
    {
        string[] weapons = StuartManor.Roster
            .Select(r => StuartManor.WeaponFor(r.Arm).Name)
            .Distinct()
            .ToArray();

        Assert.Equal(7, weapons.Length);
        // 这一趟真正值钱的三件（打得过才拿得到）：一把枪、一把破甲锤、一副装甲层的皮甲。
        Assert.Contains("手枪", weapons);
        Assert.Contains("破甲锤", weapons);
        Assert.Contains(
            StuartManor.Roster.SelectMany(r => StuartManor.ApparelFor(r.Outfit)).Select(l => l.Name),
            n => n == "皮甲");
    }

    /// <summary>
    /// 🔴 <b>而这一身装备是拿伤病换的</b>（<c>docs/research/2026-07-14-combat-cost.md</c>）：庄园里<b>只有一把枪</b>
    /// （持手枪劫掠者＝玩家胜率 26.2%——不是"少给点枪"的吝啬，是"多给一把就是必死局"），
    /// 且<b>没有任何人拿冲锋枪/步枪/狙击/重剑</b>（那几样只能靠玩家自己造或另寻，别让一处点位一次性抹平武器荒）。
    /// </summary>
    [Fact]
    public void OnlyOneGunOnTheWholeFarm_AndNoLongArms()
    {
        string[] weapons = StuartManor.Roster.Select(r => StuartManor.WeaponFor(r.Arm).Name).ToArray();

        Assert.Equal(1, weapons.Count(n => n == "手枪"));
        foreach (string forbidden in new[] { "冲锋枪", "步枪", "狙击枪", "重剑", "自制霰弹枪" })
        {
            Assert.DoesNotContain(forbidden, weapons);
        }
    }

    /// <summary>他们是<b>健全的</b>人（不同于金手指帮那 4 个残兵）——高危就该是高危，别拿伤情当折扣。</summary>
    [Fact]
    public void ManorRaiders_AreNotWounded()
    {
        // 编制表里根本没有"伤情"这一列（GangInjury 是金手指帮独有的设定）：结构性地不可能给他们打折。
        Assert.All(StuartManor.Roster, r => Assert.False(string.IsNullOrWhiteSpace(r.DisplayName)));
    }

    // ============ ③ 噪音：开一枪 = 把整个庄园叫醒 ============

    /// <summary>
    /// 🔴 <b>哨位间距是照着枪声半径设计的</b>（庭院中央 <see cref="StuartManor.CourtyardX"/>/<see cref="StuartManor.CourtyardY"/> 动手）：
    /// <list type="bullet">
    /// <item><b>弓（70）→ 0 人</b>：庭院里放一箭，整个庄园没有一个岗位被惊动 ⇒ <b>逐个清哨是可行的</b>。</item>
    /// <item><b>匕首（90）→ 1 人</b>：只有挨刀的那个知道。</item>
    /// <item><b>手枪（350）→ 3 人</b>：一枪招来三个 —— 而持械劫掠者<b>连打三个</b>的存活率是 3.5%。</item>
    /// <item><b>步枪（600）→ 6 人</b>：<b>整个庄园</b>（除了最远的后院那个）。这就是"枪纸面最强"的账单。</item>
    /// </list>
    /// 半径一律读<b>真武器表</b>（<see cref="WeaponTable"/> 的 NoiseRadius），不自造常数——武器一改数值，这条自动跟着变。
    /// </summary>
    [Fact]
    public void GunshotWakesTheFarm_ArrowDoesNot()
    {
        int Alerted(Weapon w) => StuartManor.AlertedBy(
            StuartManor.CourtyardX, StuartManor.CourtyardY, w.NoiseRadius, StuartManor.LevelW, StuartManor.LevelH);

        Assert.Equal(0, Alerted(WeaponTable.ShortBow()));   // 70
        Assert.Equal(0, Alerted(WeaponTable.LightCrossbow())); // 55——弩比弓还静
        Assert.Equal(1, Alerted(WeaponTable.Dagger()));     // 90
        Assert.Equal(3, Alerted(WeaponTable.Pistol()));     // 350
        Assert.Equal(6, Alerted(WeaponTable.Rifle()));      // 600
    }

    /// <summary>越响叫醒的人越多（单调不减）——这条是几何本身，任何布点改动都不该打破它。</summary>
    [Fact]
    public void LouderAlwaysWakesAtLeastAsMany()
    {
        int prev = -1;
        foreach (double radius in new[] { 50.0, 70.0, 90.0, 150.0, 350.0, 450.0, 600.0, 700.0, 2000.0 })
        {
            int n = StuartManor.AlertedBy(
                StuartManor.CourtyardX, StuartManor.CourtyardY, radius, StuartManor.LevelW, StuartManor.LevelH);
            Assert.True(n >= prev, $"半径 {radius} 反而叫醒得更少");
            prev = n;
        }
        Assert.Equal(StuartManor.Roster.Count, prev); // 半径够大 ⇒ 全庄园
    }

    /// <summary>
    /// <b>哨位之间真的隔得开</b>：任意两个岗位的间距都落在"枪声听得见、弓声听不见"的带里
    /// （&gt; 弓 70，且最近的一对 &lt; 手枪 350 ⇒ 手枪确实会串岗）。间距塌了，噪音机制就没了意义。
    /// </summary>
    [Fact]
    public void PostSpacing_MakesNoiseRadiiMeaningful()
    {
        var d = new List<double>();
        for (int i = 0; i < StuartManor.Posts.Count; i++)
        {
            for (int j = i + 1; j < StuartManor.Posts.Count; j++)
            {
                double dx = (StuartManor.Posts[i].X - StuartManor.Posts[j].X) * StuartManor.LevelW;
                double dy = (StuartManor.Posts[i].Y - StuartManor.Posts[j].Y) * StuartManor.LevelH;
                d.Add(Math.Sqrt((dx * dx) + (dy * dy)));
            }
        }

        Assert.True(d.Min() > WeaponTable.ShortBow().NoiseRadius, "有两个哨位挨得比弓声还近——弓就白静了");
        Assert.True(d.Min() < WeaponTable.Pistol().NoiseRadius, "所有哨位都比枪声还远——枪就白响了");
    }

    // ============ ④ authored 剧情：4 处叙事点，且与"可扒的战斗尸体"结构性隔离 ============

    /// <summary>
    /// 用户给的四处叙事骨架，一处不多一处不少：门口吊尸 / 收留流浪者 / 里屋 / 枯井底。
    /// <b>正文是 draft 骨架，等用户 authored 定稿</b>（本 agent 不编人名/对话/日记正文/背刺经过）。
    /// </summary>
    [Fact]
    public void FourNarrativeSpots_OnePerStoryBeat()
    {
        NarrativeSpot[] spots = NarrativeSpotRegistry.ForDestination(StuartManor.DestinationName).ToArray();
        Assert.Equal(4, spots.Length);

        Assert.Contains(spots, s => s.Id == StuartManor.GateHangedSpotId);
        Assert.Contains(spots, s => s.Id == StuartManor.TakenInSpotId);
        Assert.Contains(spots, s => s.Id == StuartManor.InnerRoomSpotId);
        Assert.Contains(spots, s => s.Id == StuartManor.DryWellSpotId);

        Assert.All(spots, s =>
        {
            Assert.False(s.Repeatable);                    // 一次性：看过就是看过了
            Assert.True(s.Pages.Count >= 2, s.Id);         // 至少两屏（环境讲故事，不是一行提示）
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
        });
    }

    /// <summary>
    /// 🔴 <b>用户拍板：盘踞的这伙人，就是当年被收留、然后背刺这家人的那伙流浪者</b>（原文「是——就是那伙人」）
    /// ⇒ <b>玩家杀的就是凶手</b>。现场证据必须<b>承载</b>这条：「收留」那处叙事点要指向"这伙人<b>住在这儿、住了很久</b>"
    /// （地铺积灰 → 人挪到了主人的床上）。
    /// <para>⚠️ 但<b>不替玩家下判断</b>：四处叙事点里<b>一句"复仇/报应/你为他们讨回公道"都不许有</b>——
    /// 看见什么、想什么，是玩家自己的事。这条护栏钉的就是这个分寸。</para>
    /// </summary>
    [Fact]
    public void TheSquattersAreTheBetrayers_ButTheTextNeverJudgesForYou()
    {
        NarrativeSpot taken = Assert.IsType<NarrativeSpot>(NarrativeSpotRegistry.ById(StuartManor.TakenInSpotId)!);
        string body = string.Concat(taken.Pages);

        // 证据指向"他们住下来了，而且还在"：地铺没人睡了，因为人挪进了主人的床。
        Assert.Contains("床", body);
        Assert.Contains("住", body);

        // 不替玩家下判断（也不替他动员）。
        foreach (NarrativeSpot s in NarrativeSpotRegistry.ForDestination(StuartManor.DestinationName))
        {
            string text = s.Title + string.Concat(s.Pages);
            foreach (string verdict in new[] { "复仇", "报应", "讨回", "公道", "罪有应得", "该死" })
            {
                Assert.DoesNotContain(verdict, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 🔴 <b>吊尸和井底女尸是 authored 叙事点，不是可扒的战利品尸体</b>——隔离是<b>结构性</b>的、不靠黑名单：
    /// authored id 一律 ascii <c>narrative_</c> 前缀，而战斗尸体容器名一律含中文
    /// <see cref="CorpseNaming.Marker"/>「的尸体 #」⇒ 两个命名空间不可能相交。
    /// <para>⇒ 踏上门口那具吊尸，<b>永远不会</b>被当成一个可搜刮的尸体去扒（那是<b>他们的人</b>，不是战利品）；
    /// 反过来，杀掉的劫掠者也<b>永远不会</b>误触发某段剧情。</para>
    /// </summary>
    [Fact]
    public void AuthoredCorpses_AreNeverLootableTrophies()
    {
        foreach (NarrativeSpot s in NarrativeSpotRegistry.ForDestination(StuartManor.DestinationName))
        {
            Assert.StartsWith("narrative_", s.Id, StringComparison.Ordinal);
            Assert.False(CorpseNaming.IsCorpseContainer(s.Id), s.Id);
            Assert.Matches("^[a-z0-9_]+$", s.Id); // 全 ascii snake_case
        }

        // 反向：一具真的劫掠者尸体，绝不会被路由去解析剧情/搜刮点。
        string corpse = CorpseNaming.ContainerName(StuartManor.Roster[0].DisplayName, 1);
        Assert.True(CorpseNaming.IsCorpseContainer(corpse));
        Assert.Null(NarrativeSpotRegistry.Resolve(corpse, new StoryFlags()));
        Assert.Null(ExplorationCache.Resolve(corpse, new StoryFlags()));
    }

    /// <summary>叙事点看过一次就不再弹（一次性去重旗标）。</summary>
    [Fact]
    public void NarrativeSpot_IsSeenOnlyOnce()
    {
        var flags = new StoryFlags();
        NarrativeSpotResult r = Assert.IsType<NarrativeSpotResult>(
            NarrativeSpotRegistry.Resolve(StuartManor.GateHangedSpotId, flags)!);
        flags.Set(r.StoryFlag, "true");
        Assert.Null(NarrativeSpotRegistry.Resolve(StuartManor.GateHangedSpotId, flags));
    }

    /// <summary>
    /// 门口的吊尸<b>就在门口</b>（用户原话「男性尸体吊挂在<b>门口</b>喂丧尸」）：它必须是玩家一进关就撞见的东西，
    /// 而不是藏在图里某个角落 —— 空间上落在入口那一侧（关卡南缘）。
    /// </summary>
    [Fact]
    public void TheHangedMen_AreAtTheGate_WhereYouWalkIn()
    {
        NarrativeSpot gate = Assert.IsType<NarrativeSpot>(NarrativeSpotRegistry.ById(StuartManor.GateHangedSpotId)!);
        Assert.True(gate.Y > StuartManor.LevelH * 0.75, "吊尸没落在入口侧——玩家不会一进来就看见");
        Assert.Equal(NarrativeTrigger.Proximity, gate.Trigger); // 走到跟前就撞见，不需要点它
    }

    // ══════════════════ 🔴 §④ 焊死画布副本 —— 这一条护住的是上面所有噪音断言本身 ══════════════════

    /// <summary>
    /// 🔴 <b>把 <see cref="StuartManor.LevelW"/>/<see cref="StuartManor.LevelH"/> 这份"脱 Godot 副本"焊死在真源上。</b>
    ///
    /// <para><b>真源只有一个</b>：<see cref="ExplorationLevelSize.SizeFor"/>(斯图尔特家族庄园)。运行时就是照它铺哨位的
    /// （<c>TestExploration.StuartManor.cs</c>：<c>pos = Posts[i] × LevelW</c>，而那个 <c>LevelW</c> 是实例字段＝<c>SizeFor</c> 的值）。
    /// <c>StuartManor</c> 里这两个 const 只是一份**副本**，存在的理由是这个类<b>不引 Godot 类型</b>、要能被纯逻辑单测算噪音几何。</para>
    ///
    /// <para>
    /// 🔴 <b>不焊死会怎样 —— 这不是"报告印错数"，是"护栏自己跟着一起漂"。</b>
    /// 上面 §③ 那三条噪音护栏（<see cref="GunshotWakesTheFarm_ArrowDoesNot"/> 的 弓0/匕首1/手枪3/步枪6、
    /// <see cref="PostSpacing_MakesNoiseRadiiMeaningful"/>、<see cref="LouderAlwaysWakesAtLeastAsMany"/>）
    /// <b>全都拿这份副本把归一化的 <see cref="StuartManor.Posts"/> 换算成像素</b>。
    /// 一旦有人改了真源画布却没同步这两个 const：<b>游戏里的哨位像素间距真的变了、开一枪招几个人真的变了，
    /// 而这些护栏拿旧副本算，一条都不会红</b> —— 绿得理直气壮，红线已经断了。
    /// 「跑绿不等于测到了东西」：没有这条焊缝，斯图这一侧<b>从来没有任何一个测试读过真源</b>（改造前 grep <c>SizeFor</c> 命中 0）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么用"副本 + 焊缝测试"，而不是让 <see cref="StuartManor"/> 直接读 <see cref="ExplorationLevelSize"/>？</b>
    /// 同 <c>GoldfingerGang</c> 的先例（impl-rooftop-seal 焊死 <c>GoldfingerCalibration</c> 硬编码那单）：
    /// 副本靠**测试**焊死、不靠注释提醒；而焊缝跑在<b>同时链接两者的本测试工程</b>里 ⇒ 真源一改就当场红，
    /// 且**不需要把 <see cref="ExplorationLevelSize"/> 拖进任何别的工程**。
    /// </para>
    /// </summary>
    [Fact]
    public void TheCanvasCopyIsWeldedToItsSingleSourceOfTruth()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(StuartManor.DestinationName);

        // 刻意用 Assert.True 而不是 Assert.Equal：这里两侧**都是"值"、没有一侧是"期望常量"**
        // （Assert.Equal 会被 xUnit2000 要求把 const 摆到 expected 位 ⇒ 失败信息读成"期望副本、实得真源"，正好说反）。
        // 脱焊时最该出现在屏幕上的不是两个数，是**"哪份是真源、漂了会死在哪"**。
        Assert.True(
            (float)StuartManor.LevelW == w && (float)StuartManor.LevelH == h,
            $"画布副本与真源脱焊：StuartManor.LevelW/LevelH = {StuartManor.LevelW}×{StuartManor.LevelH}，" +
            $"真源 ExplorationLevelSize.SizeFor(\"{StuartManor.DestinationName}\") = {w}×{h}。" +
            "⇒ 游戏里哨位的像素间距已经变了（**开一枪招几个人真的变了**），而本文件所有噪音护栏拿旧副本算、" +
            "一条都不会红——绿得理直气壮，红线已经断了。把 StuartManor.cs 那两个 const 同步到真源。");
    }
}
