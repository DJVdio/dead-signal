using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 逐件搜刮（《三角洲行动》式）。用户拍板：「物品一件一件转出来，防止玩家在危险中快速交互完就跑，
// 每个物品搜刮速度可以一样，不用做分级」。
//
// 这批测试守的是四条不能塌的性质：
//   ① 一件一件出（不是一把全给）；② 每件耗时**一样**（不按价值/重量分级）；
//   ③ 可中断（走开/挨打即停）；④ 中断后**已拿到的保留、剩余的还在容器里**——回来能接着搜。
public class LootSessionTests
{
    private static List<LootItem> ThreeItems() => new()
    {
        LootItem.Material("wood", 4),
        LootItem.Weapon("消防斧"),
        LootItem.Food(2),
    };

    // ---------- ① 逐件转出 ----------

    [Fact]
    public void Advance_不足一件耗时_一件都不给()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);

        Assert.Empty(s.Advance(2.9f));
        Assert.Equal(3, s.RemainingCount);
        Assert.Equal(0, s.TakenCount);
    }

    [Fact]
    public void Advance_满一件耗时_只转出一件_不是全部()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);

        IReadOnlyList<LootItem> got = s.Advance(3f);

        Assert.Single(got);                                  // 关键：不是一把全给
        Assert.Equal(LootItem.Material("wood", 4), got[0]);  // 按登记序，头一件先出
        Assert.Equal(2, s.RemainingCount);
        Assert.Equal(1, s.TakenCount);
    }

    [Fact]
    public void Advance_逐帧推进_三件要三个整耗时才搜完()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);
        var taken = new List<LootItem>();

        for (int frame = 0; frame < 90; frame++)  // 90 帧 × 0.1s = 9s = 3 件 × 3s
        {
            taken.AddRange(s.Advance(0.1f));
        }

        Assert.Equal(3, taken.Count);
        Assert.True(s.IsComplete);
        Assert.Equal(0, s.RemainingCount);
    }

    [Fact]
    public void Advance_搜空后再推进_不再产出()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);
        s.Advance(9f);

        Assert.True(s.IsComplete);
        Assert.Empty(s.Advance(100f));
        Assert.Equal(3, s.TakenCount);
    }

    // ---------- ② 每件耗时一律相同（不做分级） ----------

    [Fact]
    public void 每件耗时一律相同_轻材料与重武器与食物没有区别()
    {
        // 4 个木料（堆叠）、一把斧子、2 份食物——价值/重量天差地别，耗时必须一样。
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);

        // 每次恰好喂一件的耗时，就该恰好出一件——三件都如此。
        Assert.Single(s.Advance(3f));
        Assert.Single(s.Advance(3f));
        Assert.Single(s.Advance(3f));
        Assert.True(s.IsComplete);
    }

    [Fact]
    public void 一个堆叠算一件_八发子弹一次转出不是八次()
    {
        var s = new LootSession("弹药箱", new[] { LootItem.Material("ammo_medium", 8) }, secondsPerItem: 3f);

        IReadOnlyList<LootItem> got = s.Advance(3f);

        Assert.Single(got);
        Assert.Equal(8, got[0].Quantity);   // 一堆＝一件，同《三角洲》口径
        Assert.True(s.IsComplete);
    }

    // ---------- 操作能力：人之间**分**（物品之间不分）。用户拍板：「搜刮速度要受操作能力影响」 ----------

    [Fact]
    public void 缺两指的山姆搜刮更慢_0点86倍_不是加算()
    {
        float healthy = LootSession.EffectiveSecondsPerItem(1.0);   // 健全饱食者
        float sam = LootSession.EffectiveSecondsPerItem(0.86);      // 山姆缺两指

        Assert.Equal(3.0f, healthy, 2);
        Assert.Equal(3.49f, sam, 2);          // 3.0 ÷ 0.86 —— **乘算**（若是加算会得到别的数）
        Assert.True(sam > healthy);
    }

    [Fact]
    public void 断了双手的人翻不了箱子_乘算的必然结果_绝不给下限兜底()
    {
        // 0 × 任何倍率 = 0。若给操作能力设个下限兜底，"没有手的人"会凭空获得搜刮速度——那是错的。
        Assert.Equal(float.PositiveInfinity, LootSession.EffectiveSecondsPerItem(0d));

        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);
        Assert.Empty(s.Advance(9999d, workEfficiency: 0d));   // 站到天荒地老也掏不出一件
        Assert.Equal(3, s.RemainingCount);
        Assert.Equal(float.PositiveInfinity, s.RemainingRealSeconds(0d));
    }

    [Fact]
    public void 效率乘子直接缩放推进_不是缩放每件耗时()
    {
        // 效率 0.5 的人：3 秒的活要站 6 秒。
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);

        Assert.Empty(s.Advance(5.9d, workEfficiency: 0.5d));    // 投入 2.95 工作秒，不够
        Assert.Single(s.Advance(0.2d, workEfficiency: 0.5d));   // 满 3 工作秒，出件（余 0.05 滚进下一件）
        Assert.Equal(11.9f, s.RemainingRealSeconds(0.5d), 1);   // 剩 (3-0.05)+3 = 5.95 工作秒 ÷ 0.5 = 11.9 实时秒
    }

    // ---------- ③④ 中断：已拿到的保留，剩余的还在容器里 ----------

    [Fact]
    public void 中断_当前这件的进度作废_那件还在容器里()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);
        s.Advance(3f);          // 第 1 件到手
        s.Advance(2.9f);        // 第 2 件差 0.1 秒——手伸到一半

        s.Interrupt();          // 挨了一口 / 玩家跑了

        Assert.Equal(0f, s.ItemElapsedSeconds);
        Assert.Equal(2, s.RemainingCount);                    // 那件没到手，还在柜子里
        Assert.Equal(1, s.TakenCount);                        // 已经转出来的那件带得走
        Assert.Equal(LootItem.Weapon("消防斧"), s.CurrentItem); // 回来还得从头翻起
    }

    [Fact]
    public void 中断后接着搜_那件要重新计满整份耗时()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);
        s.Advance(2.9f);
        s.Interrupt();

        Assert.Empty(s.Advance(2.9f));      // 中断前的 2.9 秒白花了，这 2.9 秒还是不够
        Assert.Single(s.Advance(0.1f));     // 满 3 秒才出件
    }

    [Fact]
    public void 中断不销毁会话_已取件数不回退()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);
        s.Advance(6f);      // 两件到手

        s.Interrupt();
        s.Interrupt();      // 反复中断也不该回吐

        Assert.Equal(2, s.TakenCount);
        Assert.Equal(1, s.RemainingCount);
    }

    // ---------- UI：玩家得一眼看出"还要等多久" ----------

    [Fact]
    public void 剩余秒数_是玩家做跑不跑决策的依据()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);

        Assert.Equal(9f, s.RemainingRealSeconds(1d), 2);    // 三件 × 3 秒

        s.Advance(3f);
        Assert.Equal(6f, s.RemainingRealSeconds(1d), 2);    // 拿走一件

        s.Advance(1.5f);
        Assert.Equal(4.5f, s.RemainingRealSeconds(1d), 2);  // 当前这件转了一半
    }

    [Fact]
    public void 当前件进度_零到一()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 4f);

        Assert.Equal(0f, s.ItemProgress, 2);
        s.Advance(1f);
        Assert.Equal(0.25f, s.ItemProgress, 2);
        s.Advance(2f);
        Assert.Equal(0.75f, s.ItemProgress, 2);
    }

    [Fact]
    public void 空容器_开局即完成_剩余零秒()
    {
        var s = new LootSession("空柜子", new List<LootItem>(), secondsPerItem: 3f);

        Assert.True(s.IsComplete);
        Assert.Equal(0f, s.RemainingRealSeconds(1d), 2);
        Assert.Null(s.CurrentItem);
    }

    // ---------- 大 delta：一帧卡顿不该吞件 ----------

    [Fact]
    public void 大帧长_一次转出多件_不丢件()
    {
        var s = new LootSession("储物柜", ThreeItems(), secondsPerItem: 3f);

        IReadOnlyList<LootItem> got = s.Advance(7f);   // 卡了一下：够两件还余 1 秒

        Assert.Equal(2, got.Count);
        Assert.Equal(1, s.RemainingCount);
        Assert.Equal(1f, s.ItemElapsedSeconds, 2);     // 余数滚进第三件，不白扔
    }

    // ---------- 与 ContainerLoot 的接线：容器才是事实源 ----------

    [Fact]
    public void TakeNext_逐件实扣_拿空那一刻才算已搜()
    {
        var loot = new ContainerLoot();
        loot.Register("储物柜", ThreeItems());

        Assert.Equal(3, loot.RemainingCount("储物柜"));
        Assert.False(loot.IsSearched("储物柜"));

        Assert.Equal(LootItem.Material("wood", 4), loot.TakeNext("储物柜"));
        Assert.Equal(2, loot.RemainingCount("储物柜"));
        Assert.False(loot.IsSearched("储物柜"));           // 还没搜完
        Assert.True(loot.IsPartiallySearched("储物柜"));   // 但动过了

        loot.TakeNext("储物柜");
        loot.TakeNext("储物柜");

        Assert.True(loot.IsSearched("储物柜"));            // 拿空了才算搜过
        Assert.Null(loot.TakeNext("储物柜"));              // 再拿也没有
        Assert.False(loot.IsPartiallySearched("储物柜"));
    }

    [Fact]
    public void 搜一半跑掉_回来接着搜_剩下的还在()
    {
        var loot = new ContainerLoot();
        loot.Register("废墟", ThreeItems());

        // 第一趟：只来得及转出一件就跑了。
        var first = new LootSession("废墟", loot.Remaining("废墟"), secondsPerItem: 3f);
        foreach (LootItem _ in first.Advance(3.5f))
        {
            loot.TakeNext("废墟");
        }
        first.Interrupt();

        Assert.Equal(2, loot.RemainingCount("废墟"));

        // 第二趟：容器里剩什么，就接着搜什么。
        var second = new LootSession("废墟", loot.Remaining("废墟"), secondsPerItem: 3f);
        Assert.Equal(2, second.RemainingCount);
        Assert.Equal(6f, second.RemainingRealSeconds(1d), 2);
        Assert.Equal(LootItem.Weapon("消防斧"), second.CurrentItem);

        foreach (LootItem _ in second.Advance(6f))
        {
            loot.TakeNext("废墟");
        }

        Assert.True(second.IsComplete);
        Assert.True(loot.IsSearched("废墟"));
        Assert.Equal(0, loot.RemainingCount("废墟"));
    }

    [Fact]
    public void 一次性Search仍可用_但会把容器抽干_玩家路径不该走它()
    {
        var loot = new ContainerLoot();
        loot.Register("开局储藏室", ThreeItems());

        Assert.Equal(3, loot.Search("开局储藏室").Count);   // storage 整批入库的老路径
        Assert.True(loot.IsSearched("开局储藏室"));
        Assert.Equal(0, loot.RemainingCount("开局储藏室"));
        Assert.Null(loot.TakeNext("开局储藏室"));
    }

    [Fact]
    public void Remove_注销动态容器_逐件态一并清干净()
    {
        var loot = new ContainerLoot();
        loot.Register("丧尸尸体#17", ThreeItems());
        loot.TakeNext("丧尸尸体#17");
        Assert.True(loot.IsPartiallySearched("丧尸尸体#17"));

        loot.Remove("丧尸尸体#17");   // CorpseYard 回收最老的尸体

        Assert.False(loot.Has("丧尸尸体#17"));
        Assert.False(loot.IsPartiallySearched("丧尸尸体#17"));
        Assert.False(loot.IsSearched("丧尸尸体#17"));
        Assert.Equal(0, loot.RemainingCount("丧尸尸体#17"));
    }

    // ---------- 「派下去的活」而非「打开的界面」：多角色并发各搜各的 ----------
    // 用户拍板：「允许玩家控制一个角色去搜刮转物品，**然后控制另一个角色**」。
    // ⇒ 搜刮是世界里的一个持续行为，不是模态交互。分工由此产生：一个人蹲着掏尸体，另一个人在门口盯着。

    [Fact]
    public void 两个人同时各搜各的_互不干扰()
    {
        var loot = new ContainerLoot();
        loot.Register("丧尸的尸体#7", new List<LootItem> { LootItem.Armor("牛仔外套"), LootItem.Armor("长裤") });
        loot.Register("储物柜", ThreeItems());

        // A 蹲着掏尸体，B 同时在翻柜子——两份会话各自独立推进。
        var a = new LootSession("丧尸的尸体#7", loot.Remaining("丧尸的尸体#7"), secondsPerItem: 3f);
        var b = new LootSession("储物柜", loot.Remaining("储物柜"), secondsPerItem: 3f);

        for (int frame = 0; frame < 30; frame++)   // 3 秒
        {
            foreach (LootItem _ in a.Advance(0.1d))
            {
                loot.TakeNext(a.Container);
            }
            foreach (LootItem _ in b.Advance(0.1d))
            {
                loot.TakeNext(b.Container);
            }
        }

        Assert.Equal(1, a.TakenCount);
        Assert.Equal(1, b.TakenCount);
        Assert.Equal(1, loot.RemainingCount("丧尸的尸体#7"));
        Assert.Equal(2, loot.RemainingCount("储物柜"));
    }

    [Fact]
    public void 一个人被打断_另一个人照搜不误()
    {
        var a = new LootSession("尸体", ThreeItems(), secondsPerItem: 3f);
        var b = new LootSession("柜子", ThreeItems(), secondsPerItem: 3f);

        a.Advance(3d);
        b.Advance(3d);

        a.Interrupt();          // A 挨了一口撒手了
        a.Advance(1d);          // （玩家把 A 拉走，不再推进 A）

        b.Advance(3d);          // B 在另一头继续掏——不受任何影响

        Assert.Equal(1, a.TakenCount);
        Assert.Equal(2, b.TakenCount);
    }

    [Fact]
    public void 两人各自效率不同_同一份活站的时间不同()
    {
        var sam = new LootSession("柜子", ThreeItems(), secondsPerItem: 3f);   // 缺两指 0.86
        var fit = new LootSession("柜子", ThreeItems(), secondsPerItem: 3f);   // 健全 1.0

        sam.Advance(3d, workEfficiency: 0.86d);
        fit.Advance(3d, workEfficiency: 1.0d);

        Assert.Equal(0, sam.TakenCount);   // 3 秒只投入 2.58 工作秒——还差一口气
        Assert.Equal(1, fit.TakenCount);
    }

    // ---------- 校准锚点：数值是拟定待调，但"疼"的量级不许被悄悄调没 ----------

    [Fact]
    public void 校准锚点_一趟白天搜不完一个大点()
    {
        // daynight.json：白天 720s，单程 travelTime 120s ⇒ 现场可用 ≈ 480s。
        const float onSiteSecondsPerDayTrip = 720f - 2 * 120f;
        const int villageItemCount = 68;   // 南林村庄（大点，30 处搜刮点）的掉落条目数

        float fullSweep = villageItemCount * LootSession.DefaultSecondsPerItem;

        Assert.True(fullSweep > onSiteSecondsPerDayTrip * 0.35f,
            "每件耗时被调到可忽略了——逐件搜刮的意义（暴露时间）就没了");
        Assert.True(fullSweep < onSiteSecondsPerDayTrip,
            "纯站桩就吃光整趟现场时间，玩家连路都走不动——过头了");
    }
}
