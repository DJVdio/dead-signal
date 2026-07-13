using System.Collections.Generic;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「攻击位名额」纯逻辑：一处结构的可攻击面上只站得下有限个攻击者，挤不进的**去啃旁边的墙**。
/// 这既是"丧尸也打围栏"的落地方式，也是 Sim 一直缺的那条空间约束（每处最多 N 个攻击者）。
/// </summary>
public class BreachSlotsTests
{
    // 营地真实几何（godot/data/camp.json）：南大门 200×22，两侧围栏各 800×22（本单切成 100px 一格）。
    private const double Foot = BreachSlots.DefaultFootprint; // 26 = 丧尸直径（Radius 13 × 2）

    private static BreachCandidate Gate(int id) => new(id, 1100, 1478, 200, 22, BreachSlots.Capacity(200, 22, Foot));
    private static BreachCandidate Tile(int id, double x) => new(id, x, 1478, 100, 22, BreachSlots.Capacity(100, 22, Foot));

    [Fact]
    public void 名额由可攻击面的长边决定()
    {
        // 攻击者贴着墙面一字排开 ⇒ 名额 = 长边 / 占位宽度。
        Assert.Equal(7, BreachSlots.Capacity(200, 22, Foot));  // 大门 200px：7 只
        Assert.Equal(3, BreachSlots.Capacity(100, 22, Foot));  // 100px 围栏格：3 只（正是用户口径的"只站得下几只"）
        Assert.Equal(44, BreachSlots.Capacity(22, 1156, Foot)); // 东/西侧长围栏（竖着的）：长边是 1156
    }

    [Fact]
    public void 再小的结构也至少站得下一个()
    {
        Assert.Equal(1, BreachSlots.Capacity(10, 10, Foot)); // 否则会出现"谁都打不了的结构"
    }

    [Fact]
    public void 门口没满时就近砸门()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate> { Gate(0), Tile(1, 1300), Tile(2, 1000) };

        // 丧尸生成在南门正外方（camp.json 的真实生成带）。
        int t = BreachSlots.ChooseTarget(1200, 1540, cands, book, attacker: 1, radius: 320,
            out double dist, out _, out _);

        Assert.Equal(0, t);          // 选了门
        Assert.Equal(40, dist, 3);   // 门边缘就在 40px 外（正是它一出生就够得着门的原因）
        Assert.Equal(1, book.Occupancy(0));
    }

    [Fact]
    public void 挤不进门口的去啃旁边的墙()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate> { Gate(0), Tile(1, 1300), Tile(2, 1000) };

        // 前 7 只占满大门（capacity=7）。
        for (ulong i = 0; i < 7; i++)
        {
            Assert.Equal(0, BreachSlots.ChooseTarget(1200, 1540, cands, book, i, 320, out _, out _, out _));
        }
        Assert.Equal(7, book.Occupancy(0));

        // 第 8 只：门口满了 ⇒ 它**不排队干耗**，转身啃紧挨着门的那格围栏。
        int t = BreachSlots.ChooseTarget(1200, 1540, cands, book, attacker: 99, radius: 320,
            out _, out _, out _);
        Assert.True(t == 1 || t == 2);            // 门两侧任一格围栏
        Assert.Equal(1, book.Occupancy(t));
        Assert.Equal(7, book.Occupancy(0));       // 没把门挤爆
    }

    [Fact]
    public void 一群丧尸会摊到整条墙线上而不是全叠在门口()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate>
        {
            Gate(0), Tile(1, 1300), Tile(2, 1000), Tile(3, 1400), Tile(4, 900),
        };

        // 20 只沿着门缝生成带铺开（x∈[1110,1290]，y=1540）——就是 SpawnCampZombies 的口径。
        int squeezedOut = 0;
        for (ulong i = 0; i < 20; i++)
        {
            double px = 1110 + (i * 43) % 180;
            if (BreachSlots.ChooseTarget(px, 1540, cands, book, i, 320, out _, out _, out _) < 0)
            {
                squeezedOut++;
            }
        }

        Assert.Equal(7, book.Occupancy(0)); // 门只吃得下 7 只（200px ÷ 26px）
        int onWalls = book.Occupancy(1) + book.Occupancy(2) + book.Occupancy(3) + book.Occupancy(4);
        Assert.Equal(12, onWalls);          // 其余摊到 4 格围栏上（每格 3 只）—— 受攻击面从"两扇门"变成"整条墙线"
        Assert.True(book.Occupancy(1) > 0 && book.Occupancy(2) > 0); // 门两侧都被啃

        // 本例只摆了 5 处结构（名额共 19），第 20 只挤不上 ⇒ 就在后面顶着（不会绕路去找空位）。
        // 真实营地南墙一线有十几格围栏，摊得开。
        Assert.Equal(1, squeezedOut);
    }

    [Fact]
    public void 已经占住的位子是黏的_不会来回横跳()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate> { Gate(0), Tile(1, 1300) };

        int first = BreachSlots.ChooseTarget(1200, 1540, cands, book, 1, 320, out _, out _, out _);
        int again = BreachSlots.ChooseTarget(1200, 1540, cands, book, 1, 320, out _, out _, out _);

        Assert.Equal(first, again);
        Assert.Equal(1, book.Occupancy(first)); // 重复认领不能把自己数两遍
    }

    [Fact]
    public void 攻击者退场要把名额还回来()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate> { Gate(0) };

        for (ulong i = 0; i < 7; i++)
        {
            BreachSlots.ChooseTarget(1200, 1540, cands, book, i, 320, out _, out _, out _);
        }
        Assert.Equal(7, book.Occupancy(0));

        book.Release(3); // 这只被守卫打死了
        Assert.Equal(6, book.Occupancy(0));
        Assert.Null(book.HeldBy(3));

        // 空出来的位子立刻有人补上（后面挤着的那只顶上来）。
        Assert.Equal(0, BreachSlots.ChooseTarget(1200, 1540, cands, book, 100, 320, out _, out _, out _));
        Assert.Equal(7, book.Occupancy(0));
    }

    [Fact]
    public void 结构被砸穿后名额全清空()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate> { Gate(0), Tile(1, 1300) };

        for (ulong i = 0; i < 7; i++)
        {
            BreachSlots.ChooseTarget(1200, 1540, cands, book, i, 320, out _, out _, out _);
        }

        book.ReleaseTarget(0); // 门砸穿了 → 开口
        Assert.Equal(0, book.Occupancy(0));
        Assert.Null(book.HeldBy(2)); // 原来占着门的都松开了（下一帧它们改走缺口）
    }

    [Fact]
    public void 丧尸不会绕路包抄_够不着就够不着()
    {
        // 它仍然是"直线冲上来的蠢货"：只在**面前搜索半径内**挑一个还站得下人的东西砸。
        // 门口满了、旁边也没有别的结构 ⇒ 它就在原地挤着，**绝不会绕到营地另一头去找空位**。
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate>
        {
            Gate(0),
            Tile(1, 300), // 营地另一头的围栏（有空位，但远在 900px 外，超出 320 搜索半径）
        };

        for (ulong i = 0; i < 7; i++)
        {
            BreachSlots.ChooseTarget(1200, 1540, cands, book, i, 320, out _, out _, out _);
        }

        int t = BreachSlots.ChooseTarget(1200, 1540, cands, book, 99, 320, out _, out _, out _);
        Assert.Equal(-1, t); // 没得砸 —— 交回常规行为（挤在后面），而不是聪明地绕远路
    }

    [Fact]
    public void 边缘点输出的是墙上最近的那一点()
    {
        var book = new BreachSlotBook();
        var cands = new List<BreachCandidate> { Gate(0) };

        BreachSlots.ChooseTarget(1200, 1540, cands, book, 1, 320, out double d, out double ex, out double ey);

        Assert.Equal(1200, ex, 3);  // 正对着它的那一点
        Assert.Equal(1500, ey, 3);  // 门的外沿 (y = 1478 + 22)
        Assert.Equal(40, d, 3);
    }
}
