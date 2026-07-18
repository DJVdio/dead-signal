using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【T67】采集 / 种植 / 诱捕 + <b>宰杀</b> —— 用户在 wiki 上搭的第三根支柱。
///
/// <para>🔴 <b>本文件存在的头号理由，是那条"覆盖自检"</b>：
/// 用户把<b>三种箭全部改成吃羽毛</b>，而「羽毛」此前<b>在代码库里根本不存在</b>
/// ⇒ 只同步箭配方 = <b>三种箭全造不出来、弓变烧火棍</b>。
/// <see cref="三种箭都造得出来_材料链从鸟一路通到箭"/> 就是钉死这条链的那颗钉子。</para>
/// </summary>
public sealed class ForageFarmingButcheryTests
{
    // ══════════════════════════════ ① 羽毛：登记全，且不许落兜底 ══════════════════════════════

    [Fact]
    public void 羽毛_四张表全登记_一张都不缺()
    {
        // 材料目录（漏了 ⇒ 配方引用一个不存在的键）
        Assert.Contains(Materials.All, m => m.Key == Materials.FeatherKey);
        Assert.Equal("羽毛", Materials.All.First(m => m.Key == Materials.FeatherKey).DisplayName);

        // 图标（漏了 ⇒ 库存里一片空白）
        Assert.NotEqual(ItemIcons.PlaceholderPath, ItemIcons.PathFor(Materials.FeatherKey));

        // 重量（漏了 ⇒ 落 DefaultMaterialKg 兜底）
        Assert.NotEqual(ItemWeights.DefaultMaterialKg, ItemWeights.MaterialKg(Materials.FeatherKey));
    }

    /// <summary>
    /// 🔴 <b>兜底护栏</b>：`DefaultMaterialKg = 0.5kg`。**一根羽毛重半斤是荒谬的** ——
    /// impl-iron 抓出过同一个坑（wiki 上「铁」的 0.5kg 其实是兜底冒充设计决策）。
    /// 这条测试专门钉死"羽毛的重量是**显式登记**的，不是兜底冒充的"。
    /// </summary>
    [Fact]
    public void 羽毛_重量是显式登记的_绝不许落到兜底的半公斤()
    {
        double kg = ItemWeights.MaterialKg(Materials.FeatherKey);
        Assert.NotEqual(ItemWeights.DefaultMaterialKg, kg);   // 不是兜底
        Assert.True(kg > 0.0 && kg < 0.1, $"羽毛应当是全表最轻的一档，实际 {kg}kg");
    }

    [Theory]
    [InlineData("rat_meat")]
    [InlineData("bird_meat")]
    [InlineData("leather_scrap")]
    [InlineData("feather")]
    public void 宰杀链的四样新材料_全部登记全_没有死物品(string key)
    {
        Assert.Contains(Materials.All, m => m.Key == key);                               // 目录
        Assert.NotEqual(ItemIcons.PlaceholderPath, ItemIcons.PathFor(key));              // 图标
        Assert.NotEqual(ItemWeights.DefaultMaterialKg, ItemWeights.MaterialKg(key));     // 重量（非兜底）
    }

    // ══════════════════════════════ ② 老鼠 / 鸟：下不了锅了 ══════════════════════════════

    /// <summary>
    /// 🔴 用户原话「<b>老鼠和鸟不能直接入锅了，而是要先宰杀</b>」。
    /// 走法与 [T59] 蒲公英同源：<b>从 FoodCalories 移除，材料保留</b>。
    /// </summary>
    [Fact]
    public void 老鼠和鸟_已从食物表移除_但材料仍在()
    {
        Assert.False(FoodCalories.Has("rat"), "老鼠已不能直接入锅（T67）");
        Assert.False(FoodCalories.Has("pigeon"), "鸟已不能直接入锅（T67）");

        // 但它们**没有消失**——陷阱还在抓、掉落表还在掉、库存还背得动
        Assert.Contains(Materials.All, m => m.Key == "rat");
        Assert.Contains(Materials.All, m => m.Key == "pigeon");
    }

    /// <summary>热量点<b>一点没蒸发</b>：老鼠的 6（用户给定的定值）搬到了老鼠肉上，鸟的 5 搬到了鸟肉上，兔子的 11 搬到了兔子肉上。</summary>
    [Fact]
    public void 热量点原样继承_宰杀不创造也不毁灭热量()
    {
        Assert.Equal(6, FoodCalories.Of(Materials.RatMeatKey));    // ← 老鼠原来的 6 点
        Assert.Equal(5, FoodCalories.Of(Materials.BirdMeatKey));   // ← 鸟原来的 5 点
        Assert.Equal(11, FoodCalories.Of(Materials.RabbitMeatKey)); // ← 兔子原来的 11 点
    }

    /// <summary>⚠️ <b>兔子改为宰杀台专属工序</b>：简易宰杀点仍只处理老鼠和鸟；兔子必须交给宰杀台。</summary>
    [Fact]
    public void 兔子先宰杀再下锅_仅宰杀台可处理()
    {
        Assert.False(FoodCalories.Has("rabbit"));
        Assert.True(FoodCalories.Has(Materials.RabbitMeatKey));
        Assert.False(ButcheryLogic.IsButcherable("rabbit"), "简易宰杀点不接兔子");
        Assert.True(ButcheryLogic.IsButcherable(ButcherTier.Table, "rabbit"), "宰杀台必须接兔子");
    }

    /// <summary>「鸽子」→「鸟」是<b>改显示名</b>，<b>键仍是 pigeon</b>（改键要迁存档，改显示名一行都不用迁）。</summary>
    [Fact]
    public void 鸽子改名为鸟_只改显示名不改键_老档不受影响()
    {
        Assert.Equal("鸟", Materials.All.First(m => m.Key == "pigeon").DisplayName);
        Assert.DoesNotContain(Materials.All, m => m.DisplayName == "鸽子");   // 全表已无「鸽子」这个显示名
    }

    // ══════════════════════════════ ③ 宰杀：加算的速度（用户的显式例外） ══════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>用户拍板的加算例外</b>：「<b>加算 ⇒ +100%（2 倍速，30 分钟）</b>」。
    /// <b>宰杀台(+50%) + 匕首(+50%) = +100% ⇒ 2.0 倍速 ⇒ 60 分 ÷ 2.0 = 30 分钟。</b>
    /// <para>若有人按 CLAUDE.md 的"百分比一律乘算"铁律把它改成 1.5 × 1.5 = 2.25 ⇒ 26.67 分钟 ⇒ <b>本测试当场红</b>。</para>
    /// </summary>
    [Fact]
    public void 宰杀台加匕首_是加算的两倍速_三十分钟_不是乘算的2点25()
    {
        Assert.Equal(2.00, ButcheryLogic.SpeedMultiplier(ButcherTier.Table, ButcherKnife.Dagger), 4);
        Assert.Equal(30, ButcheryLogic.MinutesFor(ButcherTier.Table, ButcherKnife.Dagger));

        // 反证：乘算会给出 2.25 倍速 / 26.67 分钟 —— 明确不是这个数
        Assert.NotEqual(2.25, ButcheryLogic.SpeedMultiplier(ButcherTier.Table, ButcherKnife.Dagger), 4);
    }

    [Theory]
    [InlineData(ButcherTier.SimplePoint, ButcherKnife.BoneKnife, 1.25, 48)]  // 1 + 0.25
    [InlineData(ButcherTier.SimplePoint, ButcherKnife.Dagger, 1.50, 40)]     // 1 + 0.50
    [InlineData(ButcherTier.Table, ButcherKnife.BoneKnife, 1.75, 34)]        // 1 + 0.50 + 0.25
    [InlineData(ButcherTier.Table, ButcherKnife.Dagger, 2.00, 30)]           // 1 + 0.50 + 0.50 ← 用户点名的那个数
    public void 速度倍率全表加算_耗时是基准除以倍率取倒数(
        ButcherTier tier, ButcherKnife knife, double expectedMult, int expectedMinutes)
    {
        Assert.Equal(expectedMult, ButcheryLogic.SpeedMultiplier(tier, knife), 4);
        Assert.Equal(expectedMinutes, ButcheryLogic.MinutesFor(tier, knife));

        // ⚠️ 换算方向：**耗时 = 基准 ÷ 倍率**（取倒数，不是减法）。写成"减 100% 时间"会得到 0 分钟。
        Assert.Equal(
            (int)Math.Round(ButcheryLogic.BaseMinutes / expectedMult, MidpointRounding.AwayFromZero),
            ButcheryLogic.MinutesFor(tier, knife));
    }

    /// <summary>🔴 <b>没刀不许宰</b>：那把刀不是加成，是**开工的前提**（用户："一个槽位，可以放入匕首或者骨刀"）。</summary>
    [Fact]
    public void 空槽宰不了_徒手撕不开一只老鼠()
    {
        Assert.False(ButcheryLogic.CanButcher(ButcherKnife.None, "rat"));
        Assert.Null(ButcheryLogic.Resolve(ButcherTier.Table, ButcherKnife.None, "rat", new SequenceRandomSource(0.0)));

        Assert.True(ButcheryLogic.CanButcher(ButcherKnife.BoneKnife, "rat"));
        Assert.True(ButcheryLogic.CanButcher(ButcherKnife.Dagger, "pigeon"));
    }

    /// <summary>🔴 <b>骨刀的定位从"废武器"变成"工具"</b>——它是全表最弱的近战（DPS 1.50），但它上得了案板。</summary>
    [Fact]
    public void 骨刀是合法的宰杀刀_它终于有了存在的理由()
    {
        Assert.Equal(ButcherKnife.BoneKnife, ButcherStation.KnifeOf("骨刀"));
        Assert.Equal(ButcherKnife.Dagger, ButcherStation.KnifeOf("匕首"));
        Assert.True(ButcheryLogic.SpeedBonusOf(ButcherKnife.BoneKnife) > 0);

        // ⚠️ 白名单，不是"凡是刃器都行"——别顺手把短剑/砍刀放进来
        Assert.Equal(ButcherKnife.None, ButcherStation.KnifeOf("短剑"));
        Assert.Equal(ButcherKnife.None, ButcherStation.KnifeOf("消防斧"));
    }

    // ══════════════════════════════ ④ 宰杀的产出 ══════════════════════════════

    [Fact]
    public void 宰杀老鼠_出老鼠肉一份加碎皮革一份()
    {
        ButcherYield? y = ButcheryLogic.Resolve(
            ButcherTier.SimplePoint, ButcherKnife.BoneKnife, "rat", new SequenceRandomSource(0.99));
        Assert.NotNull(y);
        Assert.Equal(Materials.RatMeatKey, y!.Value.MeatKey);
        Assert.Equal(1, y.Value.MeatQuantity);
        Assert.Equal(Materials.LeatherScrapKey, y.Value.ByproductKey);
        Assert.Equal(1, y.Value.ByproductQuantity);
        Assert.False(y.Value.Doubled);   // 简易宰杀点没有双倍产出
    }

    [Fact]
    public void 宰杀鸟_出鸟肉一份加羽毛一份_这是羽毛的唯一来源()
    {
        ButcherYield? y = ButcheryLogic.Resolve(
            ButcherTier.SimplePoint, ButcherKnife.Dagger, "pigeon", new SequenceRandomSource(0.99));
        Assert.NotNull(y);
        Assert.Equal(Materials.BirdMeatKey, y!.Value.MeatKey);
        Assert.Equal(Materials.FeatherKey, y.Value.ByproductKey);
        Assert.Equal(1, y.Value.ByproductQuantity);
    }

    /// <summary>宰杀台的 20% 双倍产出：<b>掷中 ⇒ 肉和副产物一起翻倍</b>；简易宰杀点<b>一次点都不掷</b>。</summary>
    [Fact]
    public void 宰杀台百分之二十双倍产出_掷中则两样一起翻倍()
    {
        // 0.10 < 0.20 ⇒ 掷中
        ButcherYield? hit = ButcheryLogic.Resolve(
            ButcherTier.Table, ButcherKnife.Dagger, "pigeon", new SequenceRandomSource(0.10));
        Assert.True(hit!.Value.Doubled);
        Assert.Equal(2, hit.Value.MeatQuantity);
        Assert.Equal(2, hit.Value.ByproductQuantity);

        // 0.50 ≥ 0.20 ⇒ 没中
        ButcherYield? miss = ButcheryLogic.Resolve(
            ButcherTier.Table, ButcherKnife.Dagger, "pigeon", new SequenceRandomSource(0.50));
        Assert.False(miss!.Value.Doubled);
        Assert.Equal(1, miss.Value.MeatQuantity);
    }

    [Fact]
    public void 宰杀台三条配方_老鼠兔子鸟产出按Wiki落地()
    {
        ButcherYield? rat = ButcheryLogic.Resolve(
            ButcherTier.Table, ButcherKnife.BoneKnife, "rat", new SequenceRandomSource(0.99));
        Assert.Equal(Materials.RatMeatKey, rat!.Value.MeatKey);
        Assert.Equal(1, rat.Value.MeatQuantity);
        Assert.Equal(Materials.LeatherScrapKey, rat.Value.ByproductKey);
        Assert.Equal(2, rat.Value.ByproductQuantity);

        ButcherYield? rabbit = ButcheryLogic.Resolve(
            ButcherTier.Table, ButcherKnife.BoneKnife, "rabbit", new SequenceRandomSource(0.99));
        Assert.Equal(Materials.RabbitMeatKey, rabbit!.Value.MeatKey);
        Assert.Equal(1, rabbit.Value.MeatQuantity);
        Assert.Equal(Materials.LeatherScrapKey, rabbit.Value.ByproductKey);
        Assert.Equal(3, rabbit.Value.ByproductQuantity);

        ButcherYield? bird = ButcheryLogic.Resolve(
            ButcherTier.Table, ButcherKnife.BoneKnife, "pigeon", new SequenceRandomSource(0.99));
        Assert.Equal(Materials.BirdMeatKey, bird!.Value.MeatKey);
        Assert.Equal(1, bird.Value.MeatQuantity);
        Assert.Equal(Materials.FeatherKey, bird.Value.ByproductKey);
        Assert.Equal(1, bird.Value.ByproductQuantity);
    }

    [Fact]
    public void 宰杀台运行时_兔子转成兔子肉和碎皮革()
    {
        var inv = new InventoryStore();
        inv.Add(Materials.Find("rabbit")!.Value.ToItem(1));

        ButcherYield? y = ButcheryRuntime.Butcher(
            ButcherTier.Table, ButcherKnife.BoneKnife, "rabbit", inv, new SequenceRandomSource(0.99));

        Assert.NotNull(y);
        Assert.Equal(0, inv.MaterialCount("rabbit"));
        Assert.Equal(1, inv.MaterialCount(Materials.RabbitMeatKey));
        Assert.Equal(3, inv.MaterialCount(Materials.LeatherScrapKey));
    }

    /// <summary>简易宰杀点<b>不掷双倍产出的点</b>（随机流干净——喂一个"必中"的随机源它也不该翻倍）。</summary>
    [Fact]
    public void 简易宰杀点不掷双倍点_随机流干净()
    {
        ButcherYield? y = ButcheryLogic.Resolve(
            ButcherTier.SimplePoint, ButcherKnife.Dagger, "rat", new SequenceRandomSource(0.0));   // 0.0 必中任何几率
        Assert.False(y!.Value.Doubled);
        Assert.Equal(1, y.Value.MeatQuantity);
    }

    [Fact]
    public void 宰杀台的期望副产物比简易点多两成()
    {
        Assert.Equal(1.0, ButcheryLogic.ExpectedByproductPerQuarry(ButcherTier.SimplePoint), 4);
        Assert.Equal(1.2, ButcheryLogic.ExpectedByproductPerQuarry(ButcherTier.Table), 4);
    }

    // ─────────── [T67] 宰杀运行时两层跑通（镜像 BirdTrapRuntime）：真库存扣猎物 + 出肉入库 ───────────

    /// <summary>
    /// 🔴 <b>宰杀运行时两层跑通</b>：库里放一只老鼠 → 简易宰杀点 + 骨刀 → <see cref="ButcheryRuntime.Butcher"/>
    /// （消费层与本测试同一段代码）→ 老鼠<b>真的从库里扣掉</b>、老鼠肉 + 碎皮革<b>真的进了库</b>。
    /// 这是"接线 bug"最容易改了纯逻辑却没跟上的地方——只测 <see cref="ButcheryLogic.Resolve"/> 测不到这一层。
    /// </summary>
    [Fact]
    public void 宰杀运行时_老鼠真的从库里变成老鼠肉加碎皮革()
    {
        var inv = new InventoryStore();
        inv.Add(Materials.Find("rat")!.Value.ToItem(1));

        ButcherYield? y = ButcheryRuntime.Butcher(
            ButcherTier.SimplePoint, ButcherKnife.BoneKnife, "rat", inv, new SequenceRandomSource(0.99));

        Assert.NotNull(y);
        Assert.Equal(0, inv.MaterialCount("rat"));                            // 猎物真扣了
        Assert.Equal(1, inv.MaterialCount(Materials.RatMeatKey));             // 肉真进库
        Assert.Equal(1, inv.MaterialCount(Materials.LeatherScrapKey));        // 副产物真进库
    }

    /// <summary>宰杀台掷中 20% 双倍 ⇒ 库里进的是**两份**肉 + 两份副产物（随机走可注入源，可复现）。</summary>
    [Fact]
    public void 宰杀运行时_宰杀台双倍掷中_两样一起翻倍进库()
    {
        var inv = new InventoryStore();
        inv.Add(Materials.Find("pigeon")!.Value.ToItem(1));

        ButcherYield? y = ButcheryRuntime.Butcher(
            ButcherTier.Table, ButcherKnife.Dagger, "pigeon", inv, new SequenceRandomSource(0.10));  // 0.10 < 0.20 ⇒ 中

        Assert.True(y!.Value.Doubled);
        Assert.Equal(0, inv.MaterialCount("pigeon"));
        Assert.Equal(2, inv.MaterialCount(Materials.BirdMeatKey));
        Assert.Equal(2, inv.MaterialCount(Materials.FeatherKey));
    }

    /// <summary>🔴 <b>没刀不许宰 / 库里没有这只猎物</b> ⇒ 运行时库存<b>零变化、一次点都不掷</b>（不半扣、不白送）。</summary>
    [Fact]
    public void 宰杀运行时_没刀或库里没猎物_库存零变化随机流干净()
    {
        // ① 没刀。给 rng 一个值，宰不成就不该动它 ⇒ Remaining 仍为 1（一次点都没掷、随机流干净）。
        var noKnife = new InventoryStore();
        noKnife.Add(Materials.Find("rat")!.Value.ToItem(1));
        var rng = new SequenceRandomSource(0.10);
        Assert.Null(ButcheryRuntime.Butcher(ButcherTier.Table, ButcherKnife.None, "rat", noKnife, rng));
        Assert.Equal(1, noKnife.MaterialCount("rat"));   // 猎物没被扣
        Assert.Equal(1, rng.Remaining);                  // 随机值原封未动

        // ② 库里根本没有这只猎物
        var empty = new InventoryStore();
        Assert.Null(ButcheryRuntime.Butcher(ButcherTier.SimplePoint, ButcherKnife.Dagger, "rat", empty, new SequenceRandomSource()));
        Assert.Equal(0, empty.MaterialCount(Materials.RatMeatKey));
    }

    /// <summary>刀槽状态机：一个槽，装第二把顶掉第一把（返还前一把由消费层做，这里只钉状态）。</summary>
    [Fact]
    public void 刀槽只有一个_装第二把顶掉第一把()
    {
        var st = new ButcherStationState();
        Assert.False(st.HasKnife);
        Assert.Equal(ButcherKnife.None, st.Install(ButcherKnife.BoneKnife));   // 空槽装骨刀，顶下来的是 None
        Assert.True(st.HasKnife);
        Assert.Equal(ButcherKnife.BoneKnife, st.Install(ButcherKnife.Dagger)); // 装匕首，顶下来的是骨刀
        Assert.Equal(ButcherKnife.Dagger, st.Slotted);
        Assert.Equal(ButcherKnife.Dagger, st.Remove());                        // 取下匕首
        Assert.False(st.HasKnife);
    }

    // ─────────── [T67] 🔴 弓线打通：陷阱抓鸟 → 宰杀 → 羽毛 → 三种箭都造得出（本单存在的理由）───────────

    /// <summary>
    /// 🔴🔴 <b>本单的头号验收</b>：一条端到端断言把整条弓线钉死——
    /// <b>捕鸟陷阱抓到鸟 → 宰杀 → 羽毛 → 削尖的木箭 / 自制箭 / 重头箭 三种箭全都造得出</b>。
    /// <para>接线前：宰杀整条未接 ⇒ 羽毛<b>永远进不了库</b> ⇒ 三种箭的材料门槛<b>永远差一根羽毛</b> ⇒ 弓是烧火棍。
    /// 本测试从"库里一根羽毛都没有、三种箭全卡在羽毛上"起步，走一遍宰杀，再断言三种箭的<b>羽毛门槛全部解除</b>。</para>
    /// </summary>
    [Fact]
    public void 陷阱抓鸟到三种箭_羽毛这条唯一来源真的把弓线接通了()
    {
        var inv = new InventoryStore();

        string[] arrowIds = { "ammo_arrow_stick", "ammo_arrow_handmade", "ammo_arrow_heavy" };

        // ① 先证明"缺羽毛就造不出" —— 给足其它一切（木料/铁/工具/书），唯独没有羽毛。
        inv.Add(Materials.Find("wood")!.Value.ToItem(9));
        inv.Add(Materials.Find("iron")!.Value.ToItem(9));
        var allTools = new HashSet<ToolSlot> { ToolSlot.Calipers, ToolSlot.Beaker, ToolSlot.SawBlade };
        bool AllBooksRead(string _) => true;   // 书全读了

        foreach (string id in arrowIds)
        {
            RecipeData r = RecipeBook.Find(id)!;
            CraftAvailability before = CraftingLogic.CanCraft(r, inv.MaterialCount, AllBooksRead, allTools);
            Assert.False(before.CanCraft);   // 差的正是羽毛
            Assert.Contains(before.Blocks, b => b.Detail.Contains(Materials.FeatherKey));
        }

        // ② 陷阱抓到鸟（捕鸟运行时，必中）→ 宰杀（运行时，出羽毛）——羽毛的唯一来源全程走真代码。
        BirdTrapRuntime.ResolveCatch(1, inv, new SequenceRandomSource(0.0));           // 网到 1 只鸟
        Assert.Equal(1, inv.MaterialCount("pigeon"));
        ButcherYield? y = ButcheryRuntime.Butcher(
            ButcherTier.SimplePoint, ButcherKnife.Dagger, "pigeon", inv, new SequenceRandomSource(0.99));
        Assert.NotNull(y);
        // 一只鸟只出 1 根羽毛；三种箭各吃 1 根 ⇒ 一次只够造一种。为把"三种都造得出"逐一钉死，
        // 直接把羽毛补到 3（模拟宰了 3 只鸟）——重点是**羽毛这条来源已经接通**，够不够量是玩家攒的事。
        Assert.True(inv.MaterialCount(Materials.FeatherKey) >= 1);
        while (inv.MaterialCount(Materials.FeatherKey) < 3)
        {
            inv.Add(Materials.Find("pigeon")!.Value.ToItem(1));
            ButcheryRuntime.Butcher(ButcherTier.SimplePoint, ButcherKnife.Dagger, "pigeon", inv, new SequenceRandomSource(0.99));
        }

        // ③ 现在三种箭全都造得出（羽毛门槛全解除）。
        foreach (string id in arrowIds)
        {
            RecipeData r = RecipeBook.Find(id)!;
            CraftAvailability after = CraftingLogic.CanCraft(r, inv.MaterialCount, AllBooksRead, allTools);
            Assert.True(after.CanCraft, $"宰杀接通后 {r.DisplayName} 仍造不出：{string.Join("；", after.Blocks.Select(b => b.Detail))}");
        }
    }

    /// <summary>🔴 <b>"摆得出来"的守栏</b>：简易宰杀点必须在 <see cref="PlaceableItems"/> 里，否则库存里就是个死按钮（HasButcherPoint 恒 false）。</summary>
    [Fact]
    public void 简易宰杀点进了可摆放表_否则玩家根本立不起宰杀设施()
    {
        Assert.True(PlaceableItems.IsPlaceable(ButcherStation.PointItemKey), "简易宰杀点不在可摆放表 ⇒ 摆放按钮不长出来、HasButcherPoint 恒 false");
        // 两档设施都要能拆（否则造完拆不了/白拆），且建造成本与配方一致（防"造一个拆一个"永动机）。
        Assert.NotNull(FurnitureBuildCost.Of(ButcherStation.PointFurnitureKey));
        Assert.NotNull(FurnitureBuildCost.Of(ButcherStation.TableFurnitureKey));
    }

    // ─────────── [T67] 鞣制配方补全：生皮 + 药水 → 皮革，"自产皮革"全线打通 ───────────

    /// <summary>
    /// 🔴 <b>鞣制配方补上前是造不出皮革的</b>（红→绿的红态）：`rawhide` + `tanning_solution` 都齐了，
    /// 却因为**根本没有一条以 `leather` 为产物的配方**而做不出皮革。本测试钉死这条配方现在存在、且吃对了料、产对了物。
    /// </summary>
    [Fact]
    public void 鞣制配方_生皮加药水做出皮革_这条链此前压根没实现()
    {
        RecipeData? tan = RecipeBook.All.FirstOrDefault(r => r.OutputKey == "leather");
        Assert.NotNull(tan);   // 补全前：全仓无任何产 leather 的配方 ⇒ 这里为 null（红）
        Assert.Equal("leather", tan!.OutputKey);
        Assert.Equal(1, tan.MaterialCosts["rawhide"]);
        Assert.Equal(1, tan.MaterialCosts["tanning_solution"]);

        // 库里备齐生皮 + 药水 ⇒ 造得出皮革（无书无工具门槛，同 leather_stitch）。
        var inv = new InventoryStore();
        inv.Add(Materials.Find("rawhide")!.Value.ToItem(1));
        inv.Add(Materials.Find("tanning_solution")!.Value.ToItem(1));
        CraftAvailability can = CraftingLogic.CanCraft(
            tan, inv.MaterialCount, _ => true, new HashSet<ToolSlot>());
        Assert.True(can.CanCraft, $"生皮+药水齐了仍鞣不出皮革：{string.Join("；", can.Blocks.Select(b => b.Detail))}");

        // 产物工厂真出皮革材料（leather 是材料键 ⇒ 走 Materials 分支）。
        var produced = CraftOutputFactory.Create("leather", 1).ToList();
        Assert.Single(produced);
        Assert.Equal("皮革", produced[0].DisplayName);
    }

    /// <summary>
    /// 🔴 <b>断链修复守栏</b>：`rawhide`（生皮）与 `tanning_solution`（鞣制药水）此前**都无消费方**（审计确认）——
    /// 现在必须各自至少被一条配方吃到，否则又是"能造能买却没处用"的死物品。
    /// </summary>
    [Fact]
    public void 生皮与鞣制药水_现在都有了消费方_不再是死物品()
    {
        Assert.Contains(RecipeBook.All, r => r.MaterialCosts.ContainsKey("rawhide"));
        Assert.Contains(RecipeBook.All, r => r.MaterialCosts.ContainsKey("tanning_solution"));
    }

    // ══════════════════════════════ ⑤ 捕鸟陷阱 ══════════════════════════════

    [Theory]
    [InlineData(0, 0.00)]
    [InlineData(1, 0.20)]
    [InlineData(2, 0.15)]
    [InlineData(3, 0.10)]
    [InlineData(4, 0.05)]   // 撞地板
    [InlineData(9, 0.05)]   // 再多也只有 5%
    public void 捕鸟陷阱几率递减_第n个等于max五分之一减五个点(int ordinal, double expected)
    {
        Assert.Equal(expected, BirdTrapLogic.ChanceOf(ordinal), 4);
    }

    /// <summary>🔴 <b>捕鸟陷阱只出鸟，不出羽毛</b>——羽毛要上案板才拿得到（这是用户改的第二版规格）。</summary>
    [Fact]
    public void 捕鸟陷阱只出整只的鸟_羽毛不在陷阱里出()
    {
        // 一个陷阱、必中（0.0 < 0.20）
        IReadOnlyList<string> caught = BirdTrapLogic.RollPhase(1, new SequenceRandomSource(0.0));
        Assert.Equal(new[] { "pigeon" }, caught);
        Assert.DoesNotContain(Materials.FeatherKey, caught);   // ← 羽毛绝不能从这儿掉出来
    }

    [Fact]
    public void 没有捕鸟陷阱_一次点都不掷()
    {
        var rng = new SequenceRandomSource(0.0);
        Assert.Empty(BirdTrapLogic.RollPhase(0, rng));
    }

    // ───────────── [T75] 消费层接线：两个陷阱真的摆得出来 + 真的掷点入库（不是死按钮/死机制）─────────────

    /// <summary>
    /// 🔴 <b>"摆得出来"的守栏</b>：两个陷阱都必须在 <see cref="PlaceableItems"/> 里 —— 库存面板「摆放」按钮只对这张表里的 key 长出来。
    /// 曾漏登记 ⇒ 圈套按钮不显、捕鸟整条未接 ⇒ 纯逻辑全绿但玩家<b>根本摆不出陷阱</b>（"纯逻辑绿≠功能生效"）。
    /// 顺带钉死两者都有 <see cref="FurnitureBuildCost"/> 条目（否则造完拆不了/白拆）。
    /// </summary>
    [Fact]
    public void 两个陷阱都进了可摆放表_否则库存里就是个死按钮()
    {
        Assert.True(PlaceableItems.IsPlaceable(TrapSpec.ItemKey), "圈套陷阱不在可摆放表 ⇒ 摆放按钮不长出来");
        Assert.True(PlaceableItems.IsPlaceable(BirdTrapSpec.ItemKey), "捕鸟陷阱不在可摆放表 ⇒ 玩家摆不出来");
        Assert.NotNull(FurnitureBuildCost.Of(TrapSpec.FurnitureKey));
        Assert.NotNull(FurnitureBuildCost.Of(BirdTrapSpec.FurnitureKey));
    }

    /// <summary>捕鸟陷阱一天掷 2 次点（白天 1 + 夜晚 1），与圈套/吃饭同频；单陷阱日均 = 0.20 × 2 = 0.4 只（旧 bug 值 0.20 × 8 = 1.6 已退役）。</summary>
    [Fact]
    public void 捕鸟陷阱一天只掷两次点_每日期望零点四()
    {
        Assert.Equal(2, BirdTrapLogic.RollsPerDay);
        Assert.Equal(TrapLogic.RollsPerDay, BirdTrapLogic.RollsPerDay);   // 两种陷阱共用一张掷点尺子（TrapLogic.RollsOnPhase）
        Assert.Equal(0.40, BirdTrapLogic.ExpectedCatchesPerPhase(1) * BirdTrapLogic.RollsPerDay, 10);
    }

    /// <summary>3 个捕鸟陷阱日均 = (0.20+0.15+0.10) × 2 = <b>0.9</b> 只/天（对齐报数；修前误值 3.6）。</summary>
    [Fact]
    public void 三个捕鸟陷阱日均零点九只()
    {
        Assert.Equal(0.90, BirdTrapLogic.ExpectedCatchesPerPhase(3) * BirdTrapLogic.RollsPerDay, 10);
    }

    /// <summary>
    /// 🔴 <b>捕鸟陷阱运行时两层跑通（镜像 CropPlotRuntimeTests）</b>：一个捕鸟陷阱，走完一整天的 8 个 <see cref="DayPhase"/>，
    /// 消费层只在 <see cref="TrapLogic.RollsOnPhase"/> 为真的两段各掷一次点（<c>CampMain.cs</c> 用同一张谓词 gate）——
    /// 断言<b>恰好掷 2 次</b>（不是 8 次）、且捕到的鸟<b>真的进了真库存</b>（<see cref="BirdTrapRuntime.ResolveCatch"/> 是消费层与本测试同一段代码）。
    /// 随机源只给 2 个值：若相位门失效、8 个相位都掷 ⇒ 第 3 次抽取耗尽序列抛异常 ⇒ 反向钉死"一天只掷 2 次"。
    /// </summary>
    [Fact]
    public void 捕鸟陷阱运行时_一整天只掷两个昼夜段_鸟真的进库存()
    {
        var inv = new InventoryStore();
        var rng = new SequenceRandomSource(0.0, 0.0);   // 两段各 1 次命中判定（1 陷阱），0.0 < 20% ⇒ 都网到鸟
        int rolls = 0;
        foreach (DayPhase phase in Enum.GetValues<DayPhase>())
        {
            if (TrapLogic.RollsOnPhase(phase))
            {
                BirdTrapRuntime.ResolveCatch(1, inv, rng);
                rolls++;
            }
        }

        Assert.Equal(2, rolls);                                        // 一天恰好 2 个昼夜段掷点
        Assert.Equal(0, rng.Remaining);                               // 恰好用尽 2 个随机值 ⇒ 真的只掷了 2 次
        Assert.Equal(2, inv.MaterialCount(BirdTrapLogic.BirdKey));    // 两只鸟真的落进库存
    }

    /// <summary>场上一个捕鸟陷阱都没有 ⇒ 运行时一次点都不掷、库存零变化（空营地零开销、不白送）。</summary>
    [Fact]
    public void 没有捕鸟陷阱_运行时零掷点零入库()
    {
        var inv = new InventoryStore();
        IReadOnlyList<string> caught = BirdTrapRuntime.ResolveCatch(0, inv, new SequenceRandomSource());
        Assert.Empty(caught);
        Assert.Equal(0, inv.MaterialCount(BirdTrapLogic.BirdKey));
    }

    /// <summary>圈套陷阱的运行时编排同样两层跑通：掷点 + 老鼠/兔子入库走 <see cref="TrapRuntime.ResolveCatch"/>（消费层与本测试同一段代码）。</summary>
    [Fact]
    public void 圈套陷阱运行时_掷点命中_猎物真的进库存()
    {
        var inv = new InventoryStore();
        // 1 陷阱、必中（0.0 < 30%），物种点 0.999 ≥ RabbitShare ⇒ 老鼠。
        IReadOnlyList<string> caught = TrapRuntime.ResolveCatch(1, inv, new SequenceRandomSource(0.0, 0.999));
        Assert.Equal(new[] { TrapLogic.RatKey }, caught);
        Assert.Equal(1, inv.MaterialCount(TrapLogic.RatKey));   // 老鼠真的进了库存
    }

    /// <summary>
    /// <b>两种陷阱的计数器互不相干</b>（各按自己的实例名前缀数）——
    /// "捕鸟陷阱#1" <b>不以</b> "陷阱#" 开头，故它不会被圈套陷阱数进自己的递减档位。
    /// </summary>
    [Fact]
    public void 捕鸟陷阱与圈套陷阱的名字互不相交_两个计数器各数各的()
    {
        Assert.True(BirdTrapSpec.IsBirdTrapFurniture("捕鸟陷阱#1"));
        Assert.False(TrapSpec.IsTrapFurniture("捕鸟陷阱#1"));       // ← 关键：圈套的尺子量不到它

        Assert.True(TrapSpec.IsTrapFurniture("陷阱#1"));
        Assert.False(BirdTrapSpec.IsBirdTrapFurniture("陷阱#1"));
    }

    /// <summary>两种陷阱都<b>不实心、不挖导航洞</b> ⇒ 摆不出 kill box（用户拍板的"墙不能建"那条红线）。</summary>
    [Fact]
    public void 捕鸟陷阱与菜畦都不实心_摆不出killbox()
    {
        Assert.False(BirdTrapSpec.IsSolid);
        Assert.False(BirdTrapSpec.CarvesNavHole);
        Assert.False(BirdTrapSpec.PlaceSpec.IsSolid);
        Assert.False(CropPlotSpec.IsSolid);
        Assert.False(CropPlotSpec.PlaceSpec.IsSolid);

        // 且都**仍守 64px 禁建带**（只有沙袋有豁免）
        Assert.False(BirdTrapSpec.PlaceSpec.AllowedAgainstDefenses);
        Assert.False(CropPlotSpec.PlaceSpec.AllowedAgainstDefenses);

        // 但都允许户外（屋里没有鸟，屋里也长不出土豆）
        Assert.True(BirdTrapSpec.PlaceSpec.AllowedOutdoors);
        Assert.True(CropPlotSpec.PlaceSpec.AllowedOutdoors);
    }

    // ══════════════════════════════ ⑥ 菜园：用户拍板的种植设定 + 不许变成无限食物 ══════════════════════════════
    //
    // 用户设定（一字不改）：菜园最多种 16 颗；土豆——种 1 土豆 → 84 游戏小时成熟 → 50% 出 2 / 50% 出 1；
    //   成熟连续墙钟计时（昼夜都走、零维护、种下即倒计时）；种植动作 0.15 游戏小时/颗（走 CraftingJob 工时化）。

    /// <summary>
    /// 🔴 <b>成熟 = 84 游戏小时连续倒计时</b>（用户拍板）：昼夜都走、零维护、种下即倒计时，到点即熟。
    /// <para><b>时间基准（钉死）</b>：游戏钟一整昼夜 = 24 游戏小时（<see cref="GameClock"/>.ClockHm：白天 6:00→18:00 = 12h、
    /// 夜晚 18:00→6:00 = 12h）⇒ <b>84 游戏小时 = 3.5 个昼夜</b>。</para>
    /// </summary>
    [Fact]
    public void 菜园成熟84游戏小时_连续倒计时_三个半昼夜_零维护()
    {
        Assert.Equal(84.0, CropPlotLogic.GrowGameHours);
        Assert.Equal(24.0, CropPlotLogic.GameHoursPerDayNightCycle);      // 白天 12h + 夜晚 12h
        Assert.Equal(3.5, CropPlotLogic.MaturesInDayNightCycles, 9);      // 84 / 24 = 3.5 昼夜
        Assert.Equal(84.0, CropPlotLogic.InitialRemainingHours);

        // 连续倒计时：种下即开始，任意 elapsed 游戏小时逐帧扣（消费层按当前相位把 delta 折成游戏小时喂进来）
        double r = CropPlotLogic.InitialRemainingHours;
        r = CropPlotLogic.Tick(r, 24.0);                                 // 走了一个昼夜(24h)，还剩 60h
        Assert.False(CropPlotLogic.IsRipe(r));
        Assert.Equal(60.0, r, 9);
        r = CropPlotLogic.Tick(r, 24.0);                                 // 两个昼夜，剩 36h
        r = CropPlotLogic.Tick(r, 24.0);                                 // 三个昼夜，剩 12h（半个昼夜）
        Assert.False(CropPlotLogic.IsRipe(r));
        Assert.Equal(12.0, r, 9);
        r = CropPlotLogic.Tick(r, 12.0);                                 // 满 3.5 昼夜 = 84h → 到点即熟
        Assert.True(CropPlotLogic.IsRipe(r));
        Assert.Equal(0.0, r, 9);

        // 逐帧 delta 越过剩余量 ⇒ 钳到 0（真实运行里一帧的 delta 不会正好落在 0 上，靠过冲收尾）
        Assert.Equal(0.0, CropPlotLogic.Tick(0.3, 5.0), 9);
        Assert.True(CropPlotLogic.IsRipe(CropPlotLogic.Tick(0.3, 5.0)));

        // 熟了就一直熟着等你来收（不跌破 0，不烂在地里——烂菜是引擎新轴，本单不开）
        Assert.Equal(0.0, CropPlotLogic.Tick(0.0, 100.0), 9);
        Assert.True(CropPlotLogic.IsRipe(CropPlotLogic.Tick(0.0, 5.0)));

        // 面板"还要几天" = ceil(剩余 / 24)：满 84h → 4 个显示日（3.5 向上取整）
        Assert.Equal(4, CropPlotLogic.DaysLeft(84.0));
        Assert.Equal(1, CropPlotLogic.DaysLeft(0.1));
        Assert.Equal(0, CropPlotLogic.DaysLeft(0.0));
    }

    /// <summary>
    /// 🔴 <b>收获 50/50：50% 出 2、50% 出 1</b>（用户拍板；随机走可注入 <see cref="IRandomSource"/>、可复现）。
    /// <b>下行最差 = 收 1 = 种薯 1 ⇒ 永不亏种子</b>（零风险，是"低维护稳定口粮"的代价对冲）。
    /// </summary>
    [Fact]
    public void 菜园收获50出2_25出3_25出1_永不亏种子_种植动作0_15小时()
    {
        Assert.Equal(1, CropPlotLogic.SeedCost);                          // 种 1 土豆
        // 🔴 用户最终拍板分布（旧口径是 50/50 出 2/1，期望 1.5）：50% 出 2 / 25% 出 3 / 25% 出 1 ⇒ 期望 2.0
        Assert.Equal(0.50, CropPlotLogic.Out2Chance, 9);
        Assert.Equal(0.25, CropPlotLogic.Out3Chance, 9);
        Assert.Equal(0.25, CropPlotLogic.Out1Chance, 9);
        Assert.Equal(1, CropPlotLogic.LowYield);
        Assert.Equal(2, CropPlotLogic.MidYield);
        Assert.Equal(3, CropPlotLogic.HighYield);

        // 三段分布边界钉死：[0,0.50) ⇒ 2 颗 · [0.50,0.75) ⇒ 3 颗 · [0.75,1) ⇒ 1 颗（可复现）
        Assert.Equal(2, CropPlotLogic.RollHarvest(new SequenceRandomSource(0.0)));
        Assert.Equal(2, CropPlotLogic.RollHarvest(new SequenceRandomSource(0.49)));
        Assert.Equal(3, CropPlotLogic.RollHarvest(new SequenceRandomSource(0.50)));   // 边界归到 3
        Assert.Equal(3, CropPlotLogic.RollHarvest(new SequenceRandomSource(0.74)));
        Assert.Equal(1, CropPlotLogic.RollHarvest(new SequenceRandomSource(0.75)));   // 边界归到 1
        Assert.Equal(1, CropPlotLogic.RollHarvest(new SequenceRandomSource(0.99)));

        // 🔴 永不亏种子：下限产出 ≥ 种薯
        Assert.True(CropPlotLogic.LowYield >= CropPlotLogic.SeedCost);

        // 期望 = 0.5×2 + 0.25×3 + 0.25×1 = 2.0、净 +1.0/颗
        Assert.Equal(2.0, CropPlotLogic.ExpectedYieldPerPlant, 9);
        Assert.Equal(1.0, CropPlotLogic.NetExpectedYieldPerPlant, 9);

        // 种植动作（人力，走 CraftingJob 工时化）= 0.15 游戏小时/颗 = 9 游戏分钟
        Assert.Equal(0.15, CropPlotLogic.PlantActionGameHours, 9);
        Assert.Equal(9, CropPlotLogic.PlantWorkMinutes);
    }

    /// <summary>
    /// 🔴 <b>设计红线：菜园绝不能变成"无限食物"。</b>
    /// 四道闸换了形态但仍在：① 每颗要种薯（吃 1 土豆）② 要地皮（菜园占院子、守 64px 禁建带、与陷阱抢地）
    /// ③ 要工时（0.15h/颗 种植动作）④ <b>16 颗上限 + 每颗收完要重新下种</b>（不是"菜园消失"，而是空出一格、再种要再吃 1 土豆 + 再等 84h）。
    /// <para>这条钉死那本账：<b>满种净收益为正</b>（否则种地纯亏、机制没意义），
    /// 但<b>满种 16 颗的日均净热量必须远远喂不饱一个人</b>（一人一天两餐 = 2 × 16 = 32 点）。</para>
    /// </summary>
    [Fact]
    public void 菜园16颗上限_满种净收益为正_但喂不饱一个人_不是无限食物()
    {
        Assert.Equal(16, CropPlotLogic.MaxPlants);
        Assert.Equal(16, CropPlotSpec.MaxPlants);

        // 满种净 = 16 颗 × 净 1.0 = 16 颗土豆 / 周期；× 土豆 4 点 = 64 热量点 / 84h(=3.5 昼夜)
        Assert.Equal(16.0, CropPlotLogic.NetExpectedYieldPerGarden, 9);
        Assert.Equal(64.0, CropPlotLogic.NetExpectedCaloriesPerGarden, 9);

        // ① 净收益为正（种地不是纯亏）
        Assert.True(CropPlotLogic.NetExpectedCaloriesPerGarden > 0);

        // ② 但满种菜园的**日均**净热量 ≈ 18.3 点，仍喂不饱一个人（一人一天两餐 32 点）—— 约养活 4/7 人（用户增强后口径）
        double onePersonPerDay = 2 * CookingLogic.BasePortionCost;       // 两餐/昼夜 = 32 点
        double netPerCycleDay = CropPlotLogic.NetExpectedCaloriesPerGarden / CropPlotLogic.MaturesInDayNightCycles;
        Assert.True(netPerCycleDay < onePersonPerDay,
            $"满种菜园日均净 {netPerCycleDay:0.0} 点，必须喂不饱一个人（一天 {onePersonPerDay} 点）——否则种地就是无限食物");

        // ③ 菜园本身**不因收获消失**（它是持久种植区）；防无限食物靠"每颗要种薯 + 16 上限 + 84h 连续等待"
        Assert.False(CropPlotSpec.GardenConsumedOnHarvest);
    }

    // ══════════════════════════════ ⑦ 采集 ══════════════════════════════

    /// <summary>《野外生存指南》让你采得更多：产量 <b>×1.5 乘算</b>（项目铁律），向下取整。<b>不读书也采得到。</b></summary>
    [Theory]
    [InlineData(ForageLogic.RangersCabinMushroomId, "mushroom", 2, 3)]        // 2 → floor(2 × 1.5) = 3
    [InlineData(ForageLogic.StuartGardenPotatoId, "potato", 3, 4)]            // 3 → floor(4.5)   = 4
    [InlineData(ForageLogic.StuartFurrowPotatoId, "potato", 2, 3)]
    public void 采集点_读过野外生存指南产量乘一点五_向下取整(string id, string mat, int baseQty, int guidedQty)
    {
        var raw = ForageLogic.Resolve(id, hasWildernessGuide: false);
        Assert.Equal(mat, raw.MaterialKey);
        Assert.Equal(baseQty, raw.Quantity);       // 不读书也有基准量

        var guided = ForageLogic.Resolve(id, hasWildernessGuide: true);
        Assert.Equal(guidedQty, guided.Quantity);  // 乘算 ×1.5、向下取整
    }

    [Fact]
    public void 未知采集点_不白送东西()
    {
        var none = ForageLogic.Resolve("forage_does_not_exist", hasWildernessGuide: true);
        Assert.Equal(0, none.Quantity);
        Assert.False(ForageLogic.IsForageSpot("forage_does_not_exist"));
        Assert.False(ForageLogic.IsForageSpot(null));
    }

    [Fact]
    public void 采集点id唯一_且都认得出来()
    {
        Assert.Equal(ForageLogic.All.Count, ForageLogic.All.Select(s => s.Id).Distinct().Count());
        foreach (var s in ForageLogic.All)
        {
            Assert.True(ForageLogic.IsForageSpot(s.Id));
            Assert.True(s.BaseQuantity > 0);
            // 采集点只出既有材料，不发明新东西
            Assert.Contains(Materials.All, m => m.Key == s.MaterialKey);
        }
    }

    /// <summary>采集点的书门槛挂的是《野外生存指南》（不是《农场主》——那本管的是陷阱与菜畦）。</summary>
    [Fact]
    public void 采集挂野外生存指南_种植与诱捕挂农场主()
    {
        Assert.Equal("wilderness_survival_guide", ForageLogic.GuideBookId);

        var readBooks = new ReadBookSet();
        var before = ForageLogic.Resolve(ForageLogic.RangersCabinMushroomId, readBooks);
        readBooks.MarkRead(ForageLogic.GuideBookId);
        var after = ForageLogic.Resolve(ForageLogic.RangersCabinMushroomId, readBooks);
        Assert.True(after.Quantity > before.Quantity);
    }

    [Fact]
    public void 尖峰时刻三_解锁葛根大黄识别并限制交互采集()
    {
        Assert.Equal(6, FoodCalories.Of(Materials.KudzuRootKey));
        Assert.Equal(3, FoodCalories.Of(Materials.RhubarbKey));

        var kudzu = ForageLogic.Resolve(ForageLogic.RangersKudzuRootId, _ => false);
        var rhubarb = ForageLogic.Resolve(ForageLogic.StuartRhubarbId, _ => false);
        Assert.Equal(0, kudzu.Quantity);
        Assert.Equal(0, rhubarb.Quantity);

        Func<string, bool> readPeakHourThree = id => id == RecipeBook.PeakHourThreeBookId;
        kudzu = ForageLogic.Resolve(ForageLogic.RangersKudzuRootId, readPeakHourThree);
        rhubarb = ForageLogic.Resolve(ForageLogic.StuartRhubarbId, readPeakHourThree);
        Assert.Equal(Materials.KudzuRootKey, kudzu.MaterialKey);
        Assert.Equal(Materials.RhubarbKey, rhubarb.MaterialKey);
        Assert.Equal(2, kudzu.Quantity);
        Assert.Equal(2, rhubarb.Quantity);
    }

    [Fact]
    public void 尖峰时刻二三_书正文与配方门槛完整()
    {
        Assert.DoesNotContain("正文待补", BookLibrary.MechanicalBeauty().Body);
        Assert.DoesNotContain("正文待补", BookLibrary.BowCraftingGuide().Body);
        Assert.DoesNotContain("正文待补", BookLibrary.PeakHourTwo().Body);
        Assert.DoesNotContain("正文待补", BookLibrary.PeakHourThree().Body);
        Assert.Contains(BookLibrary.PeakHourTwoId, RecipeBook.Find("heavy_trousers")!.RequiredBookIds);
        Assert.Contains(BookLibrary.PeakHourTwoId, RecipeBook.Find("heavy_cape")!.RequiredBookIds);
        Assert.Contains(BookLibrary.PeakHourThreeId, RecipeBook.Find("snow_boots")!.RequiredBookIds);
    }
}
