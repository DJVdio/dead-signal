using System;
using System.Collections.Generic;
using System.Diagnostics;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;
using Xunit.Abstractions;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 存档性能：**满载**世界（尸体到 240 具上限、探索点 flag 全开、库存塞满）下的序列化/反序列化耗时。
/// 存档卡顿是会被玩家直接感受到的——自动存档挂在相位切换上，那一帧要是卡半秒，玩家会以为游戏崩了。
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
            p.Conditions.Add(new ConditionSave { Type = HealthConditionType.Infection, Severity = 0.4, CureProgress = 0.2 });
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

    [Fact]
    public void 满载存档的序列化与反序列化都在一帧预算内()
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

        double serMs = swSer.Elapsed.TotalMilliseconds / Runs;
        double deMs = swDe.Elapsed.TotalMilliseconds / Runs;
        double kb = json.Length / 1024.0;

        _out.WriteLine($"满载存档：{kb:F0} KB");
        _out.WriteLine($"  序列化   {serMs:F1} ms");
        _out.WriteLine($"  反序列化 {deMs:F1} ms");
        _out.WriteLine($"  （尸体 240 具 / flag 600 条 / 幸存者 6 人 / 库存 200 件 / 围栏 160 格 / 容器 163 个）");

        // 自动存档挂在相位切换上（一昼夜 8 次）。60fps 下一帧 16.7ms——
        // 存档不必挤进一帧（相位切换本来就是个卡点），但也不能让玩家察觉到停顿。
        // 100ms 是"感觉不到"的上界，这里留足余量。
        Assert.True(serMs < 100, $"序列化 {serMs:F1}ms 超预算——玩家会在相位切换时感到卡顿");
        Assert.True(deMs < 100, $"反序列化 {deMs:F1}ms 超预算");
    }

    [Fact]
    public void 只读摘要远快于读全档()
    {
        // 存档列表要列 8 个槽——若每个都反序列化整棵世界树，开个菜单就是 8 倍全档读取。
        string json = SaveCodec.Serialize(BuildWorstCase());

        for (int i = 0; i < 3; i++) { SaveCodec.PeekMeta(json); SaveCodec.Deserialize(json); }

        const int Runs = 20;
        var swPeek = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++) { SaveCodec.PeekMeta(json); }
        swPeek.Stop();

        var swFull = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++) { SaveCodec.Deserialize(json); }
        swFull.Stop();

        double peekMs = swPeek.Elapsed.TotalMilliseconds / Runs;
        double fullMs = swFull.Elapsed.TotalMilliseconds / Runs;
        _out.WriteLine($"摘要 {peekMs:F2} ms  vs  全档 {fullMs:F2} ms  （{fullMs / Math.Max(peekMs, 0.001):F1}× 快）");

        Assert.True(peekMs < fullMs, "摘要必须比读全档快，否则 PeekMeta 就白写了");
    }
}
