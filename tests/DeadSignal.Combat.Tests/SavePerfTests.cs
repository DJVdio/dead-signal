using System;
using System.Collections.Generic;
using System.Diagnostics;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;
using Xunit.Abstractions;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 存档开销：**满载**世界（尸体到 240 具上限、探索点 flag 全开、库存塞满）下的存档体积与读档路径。
/// 存档卡顿是会被玩家直接感受到的——自动存档挂在相位切换上，那一帧要是卡半秒，玩家会以为游戏崩了。
///
/// <para>
/// <b>为什么这个文件里一条「耗时 &lt; N 毫秒」的断言都没有。</b>
/// 本仓库的常态是多个 agent 并发构建，CPU 被打满时同一段代码的墙钟耗时会抖 10~20 倍——实测满载全档反序列化
/// 空载 2.9ms，并发构建下能飙到 51ms。任何拿墙钟当阈值的断言在这种环境里都是个随机数发生器：
/// 它变红的时候不代表存档变慢了，只代表隔壁在编译。**一条会随机变红的测试比没有测试更糟——它训练所有人忽略红色。**
/// </para>
/// <para>
/// ⇒ 这里只断言<b>与 CPU 争抢无关的量</b>：
/// <list type="bullet">
///   <item>存档<b>体积</b>——纯粹是数据的函数，同一棵树跑一万遍都是同一个字节数。</item>
///   <item><b>分配字节数</b>——是代码路径的函数。摘要不物化世界树，这件事在分配上是<b>数量级</b>的差距，
///         在墙钟上却只有 1.5 倍（两条路径都得把 347KB 文本 tokenize 一遍，那部分省不掉），
///         所以墙钟根本量不出这个结构性事实，分配才量得出。</item>
/// </list>
/// 耗时仍然测量并打印，当诊断信息看（回归时肉眼能发现），但<b>不作为断言</b>。
/// </para>
/// </summary>
public class SavePerfTests
{
    private readonly ITestOutputHelper _out;
    public SavePerfTests(ITestOutputHelper output) => _out = output;

    /// <summary>造一个"最坏情况"的世界：尸体满、flag 满、人满、库存满。</summary>
    private static SaveData BuildWorstCase()
    {
        var d = new SaveData();
        d.World.Day = 39;   // 尸潮前夜
        d.World.Phase = DayPhase.NightAct;

        // 尸体：CorpseYard.MaxCorpses = 240（场上并发上限），每具带 3 件战利品
        d.Corpses.PhaseTick = 312;
        for (int i = 0; i < 240; i++)
        {
            d.Corpses.Corpses.Add(new CorpseSave
            {
                ContainerId = $"丧尸的尸体 #{i}",
                X = 100 + i, Y = 200 + i,
                CellX = i % 60, CellY = i / 60,
                SpawnPhaseTick = 310,
                Loot =
                {
                    LootItem.Armor("牛仔外套"),
                    LootItem.Armor("劳保手套"),
                    LootItem.Weapon("匕首"),
                },
                TintR = 0.4f, TintG = 0.5f, TintB = 0.3f, Radius = 12f,
            });
        }

        // 剧情 flag：163 个探索点 × 搜刮完成度 + 剧情/提示/发现，取 600 条（比实际更狠）
        for (int i = 0; i < 600; i++)
        {
            d.StoryFlags[$"searched_cache_{i}"] = "true";
        }

        // 6 个幸存者，每人一具完整人体（~30 个部位）+ 装备 + 伤病
        for (int i = 0; i < 6; i++)
        {
            Body body = CombatData.NewHumanoidBody();
            body.ApplyDamage(HumanBody.Chest, 5);
            body.Sever(HumanBody.LeftIndex);
            var p = new PawnSave
            {
                Id = i,
                DisplayName = $"幸存者{i}",
                Body = body.Capture(),
                Hunger = 4,
                HungerCap = 5,
            };
            for (int k = 0; k < 11; k++)   // 11 个穿戴槽塞满
            {
                p.Apparel.Add(new WornSave { Item = $"装备{k}", Slots = { EquipSlot.Head }, Covers = { HumanBody.Chest } });
            }
            p.Conditions.Add(new ConditionSave { Type = HealthConditionType.Infection, Severity = 0.4 });
            d.Survivors.Add(p);
        }

        // 库存 200 件
        for (int i = 0; i < 200; i++)
        {
            d.Camp.Inventory.Add(new ItemSave
            {
                Category = ItemCategory.Material,
                DisplayName = $"材料{i}",
                Description = "一行不算短的黑色幽默描述，用来把 JSON 撑到真实体积。",
                RefKey = $"mat_{i}",
                MaterialQuantity = 10,
            });
        }

        // 结构：围栏已切格（每格 100px），一圈下来上百格
        for (int i = 0; i < 160; i++)
        {
            d.Camp.Structures.Add(new StructureSave
            {
                Id = $"{i * 100},0,100,26",
                Tier = StructureTier.FenceSheetMetal,
                Hp = 387.5,
            });
        }

        // 容器藏物：163 个探索点容器
        for (int i = 0; i < 163; i++)
        {
            d.Camp.ContainerLoot[$"容器{i}"] = new List<LootItem> { LootItem.Food(3), LootItem.Material("wood", 5) };
        }

        return d;
    }

    /// <summary>满载存档的体积上界。读写成本大体正比于体积，所以守住体积就守住了停顿。</summary>
    private const int WorstCaseBudgetKb = 512;

    [Fact]
    public void 满载存档的体积在预算内()
    {
        SaveData world = BuildWorstCase();

        // 预热（JIT）——不预热测的是编译耗时，不是序列化耗时
        for (int i = 0; i < 3; i++)
        {
            SaveCodec.Deserialize(SaveCodec.Serialize(world));
        }

        const int Runs = 20;
        var swSer = Stopwatch.StartNew();
        string json = "";
        for (int i = 0; i < Runs; i++)
        {
            json = SaveCodec.Serialize(world);
        }
        swSer.Stop();

        var swDe = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++)
        {
            SaveCodec.Deserialize(json);
        }
        swDe.Stop();

        double kb = json.Length / 1024.0;

        // 耗时只打印、不断言（见类注释：并发构建下墙钟抖 10~20 倍）。空载参考值：序列化 ~2.5ms、反序列化 ~2.9ms。
        _out.WriteLine($"满载存档：{kb:F0} KB  （预算 {WorstCaseBudgetKb} KB）");
        _out.WriteLine($"  序列化   {swSer.Elapsed.TotalMilliseconds / Runs:F1} ms   （仅诊断，不断言）");
        _out.WriteLine($"  反序列化 {swDe.Elapsed.TotalMilliseconds / Runs:F1} ms   （仅诊断，不断言）");
        _out.WriteLine($"  （尸体 240 具 / flag 600 条 / 幸存者 6 人 / 库存 200 件 / 围栏 160 格 / 容器 163 个）");

        // 体积是可断言的那一半：它是数据的纯函数，不受隔壁编译影响。
        // 自动存档挂在昼夜两个边界上（一天两次），读写成本大体正比于体积——守住体积就守住了停顿。
        // 实测满载 347 KB；预算 512 KB 留了约 1.5 倍余量，够接住"又加了几个字段"，
        // 但接不住"每具尸体存一整棵部位树"这类真正的体积爆炸（那会翻好几倍）——那正是这条断言要拦的东西。
        Assert.True(kb < WorstCaseBudgetKb,
            $"满载存档 {kb:F0} KB 超出 {WorstCaseBudgetKb} KB 预算——存档体积爆炸了，相位切换会卡给玩家看");
    }

    [Fact]
    public void 只读摘要不物化整棵世界树()
    {
        // 存档列表要列 8 个槽——若每个都反序列化整棵世界树，开个菜单就是 8 倍全档读取。
        // PeekMeta 的价值在于**跳过 POCO 物化**（JSON 文本还是要 tokenize 一遍，那部分省不掉）。
        // 这个"跳过"在**分配字节**上是数量级的差距，在墙钟上却几乎看不见——所以这里量分配，不量耗时。
        string json = SaveCodec.Serialize(BuildWorstCase());

        for (int i = 0; i < 3; i++) { SaveCodec.PeekMeta(json); SaveCodec.Deserialize(json); }   // 预热，别把 JIT 的分配算进去

        const int Runs = 20;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Runs; i++) { SaveCodec.PeekMeta(json); }
        long peekBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Runs;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Runs; i++) { SaveCodec.Deserialize(json); }
        long fullBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Runs;

        _out.WriteLine($"摘要 {peekBytes / 1024.0:F1} KB 分配  vs  全档 {fullBytes / 1024.0:F1} KB 分配  " +
                       $"（{(double)fullBytes / Math.Max(peekBytes, 1):F1}× 省）");

        // 实测：全档稳定 785 KB（整棵 POCO 世界树）；摘要**稳态只有 0.1 KB**——JsonDocument 的缓冲区是从
        // ArrayPool 租的，using 归还后重复 Parse 几乎不碰 GC 堆，剩下的只有一个 SaveMeta 对象。
        // （摘要的绝对值会随池子冷热浮动：若前面刚跑过大量全档反序列化、GC 把池子刮了，摘要要重新租数组，
        //   实测会到 ~25 KB。所以别把 0.1 KB 当固定值——**要断言的是比值，不是绝对分配量**。）
        // 两种冷热情形下比值分别是 ~6700× 与 ~30×，都远在闸门之上；阈值取 4 倍，环境怎么抖都误报不了。
        // 而一旦 PeekMeta 退化成"顺手把整棵树也反序列化了"，比值会当场塌到 1× 左右，这条就红。
        Assert.True(peekBytes * 4 < fullBytes,
            $"摘要分配 {peekBytes / 1024.0:F1} KB，全档 {fullBytes / 1024.0:F1} KB——" +
            $"摘要开始物化世界树了，PeekMeta 就白写了（列表 8 个槽会付 8 倍全档读取）");
    }
}
