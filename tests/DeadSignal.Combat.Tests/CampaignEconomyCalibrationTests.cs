using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 第 1～40 天全局经济静态校准。它不是自动玩家，也不把胜率当成本；只把当前真实配置中的
/// 固定供给、日常消耗、可持续产能、搬运上限与主线时限放进同一张可重算账本。
/// </summary>
public sealed class CampaignEconomyCalibrationTests
{
    private const string ReportRelativePath = "docs/research/2026-07-21-campaign-economy.md";
    private const string UpdateVariable = "DEAD_SIGNAL_UPDATE_CAMPAIGN_ECONOMY_REPORT";

    [Fact]
    public void CampaignEconomyReport_MatchesCurrentRulesAndAuthoredData()
    {
        string root = RepoRoot();
        string expected = CampaignEconomyReport.Build(root);
        string reportPath = Path.Combine(root, ReportRelativePath);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        Assert.True(File.Exists(reportPath),
            $"缺少经济校准报告；显式运行 {UpdateVariable}=1 dotnet test --filter CampaignEconomyReport 可生成");
        Assert.Equal(expected, File.ReadAllText(reportPath));
    }

    [Fact]
    public void CalibrationCoversEveryWorldGraphCacheExactlyOnce()
    {
        string root = RepoRoot();
        WorldGraph graph = WorldGraph.FromJson(File.ReadAllText(Path.Combine(root, "godot/data/world_graph.json")));
        string[] ids = graph.Nodes.SelectMany(node => ExplorationCache.CacheIdsFor(node.Name)).ToArray();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.NotNull(ExplorationCache.Resolve(id, new StoryFlags())));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到 DeadSignal 仓库根目录");
    }
}

internal static class CampaignEconomyReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] InputFiles =
    {
        "godot/data/camp.json",
        "godot/data/camp_resources.json",
        "godot/data/world_graph.json",
        "godot/scripts/Butchery.cs",
        "godot/scripts/CampMain.cs",
        "godot/scripts/CarryWeight.cs",
        "godot/scripts/CookingLogic.cs",
        "godot/scripts/DayPhase.cs",
        "godot/scripts/DayPhaseSegments.cs",
        "godot/scripts/ExplorationCache.cs",
        "godot/scripts/Farming.cs",
        "godot/scripts/FarmingConfig.cs",
        "godot/scripts/FoodEconomy.cs",
        "godot/scripts/GameConfig.cs",
        "godot/scripts/HealthConditions.cs",
        "godot/scripts/HealthConfig.cs",
        "godot/scripts/HordeConfig.cs",
        "godot/scripts/HordeTimeline.cs",
        "godot/scripts/Item.cs",
        "godot/scripts/ItemDef.cs",
        "godot/scripts/MaterialConfig.cs",
        "godot/scripts/Materials.cs",
        "godot/scripts/MedicalOrder.cs",
        "godot/scripts/MerchantBuyList.cs",
        "godot/scripts/MerchantConfig.cs",
        "godot/scripts/MerchantShelf.cs",
        "godot/scripts/MerchantTrade.cs",
        "godot/scripts/RadioMainline.cs",
        "godot/scripts/RecipeConfig.cs",
        "godot/scripts/Silver.cs",
        "godot/scripts/StoryFlags.cs",
        "godot/scripts/TrapLogic.cs",
        "godot/scripts/WorldGraph.cs",
        "tests/DeadSignal.Combat.Tests/CampaignEconomyCalibrationTests.cs",
    };

    public static string Build(string root)
    {
        WorldGraph graph = WorldGraph.FromJson(File.ReadAllText(Path.Combine(root, "godot/data/world_graph.json")));
        var byDestination = graph.Nodes.ToDictionary(node => node, node =>
        {
            string[] ids = ExplorationCache.CacheIdsFor(node.Name).ToArray();
            LootItem[] loot = ids
                .Select(id => ExplorationCache.Resolve(id, new StoryFlags()))
                .Where(result => result.HasValue)
                .SelectMany(result => result!.Value.Loot)
                .ToArray();
            return new DestinationLoot(ids.Length, loot);
        });
        LootItem[] worldLoot = byDestination.Values.SelectMany(snapshot => snapshot.Loot).ToArray();
        CampSnapshot camp = ReadCamp(root);

        int WorldMaterial(string key) => worldLoot
            .Where(item => item.Kind == LootKind.Material && item.RefId == key)
            .Sum(item => item.Quantity);
        int FixedMaterial(string key) => camp.StartingMaterials.GetValueOrDefault(key)
            + camp.LocalLootMaterials.GetValueOrDefault(key) + WorldMaterial(key);

        int worldReadyFood = worldLoot.Where(item => item.Kind == LootKind.Food).Sum(item => item.Quantity);
        int worldCalories = IngredientCalories(worldLoot);
        int startingCalories = camp.StartingMaterials.Sum(pair => FoodCalories.Of(pair.Key) * pair.Value);
        int localCalories = camp.LocalLootMaterials.Sum(pair => FoodCalories.Of(pair.Key) * pair.Value);
        int baseCost = CookingLogic.PortionCost(null);
        var fullCookware = new HashSet<CookwareSlot> { CookwareSlot.Pot, CookwareSlot.Grill };
        int fullCookwareCost = CookingLogic.PortionCost(fullCookware);
        int fixedReadyFood = camp.StartingReadyFood + camp.LocalLootReadyFood + worldReadyFood;
        int allIngredientCalories = startingCalories + localCalories + worldCalories;

        int cacheCount = graph.Nodes.Sum(node => ExplorationCache.CacheIdsFor(node.Name).Count);
        double totalWorldKg = byDestination.Values.Sum(snapshot => ItemWeights.TotalOfLoot(snapshot.Loot));
        var destinationWeights = byDestination
            .Select(pair => new DestinationWeight(
                pair.Key.Display,
                pair.Value.CacheCount,
                pair.Value.Loot.Count,
                ItemWeights.TotalOfLoot(pair.Value.Loot)))
            .OrderByDescending(row => row.Kg)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToArray();

        int startingSilverCents = Silver.FromWhole(ReadStartingSilver(root));
        int worldSilverCents = WorldMaterial(Materials.CurrencyKey);
        int fixedSilverCents = startingSilverCents + worldSilverCents;

        string[] ammoKeys = { AmmoKeys.ShortBullet, AmmoKeys.MediumBullet, AmmoKeys.Buckshot, AmmoKeys.LongBullet,
            ArrowKeys.SharpenedStick, ArrowKeys.Handmade, ArrowKeys.Heavy, ArrowKeys.Carbon };
        string[] medicalKeys = Materials.All
            .Where(def => MedicalOrderLogic.IsMedicalSupply(def.Key))
            .Select(def => def.Key)
            .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("# Dead Signal — 第 1～40 天全局经济与流程校准");
        sb.AppendLine();
        sb.AppendLine("日期：2026-07-21　性质：机器生成静态账本（不启动 Godot、不模拟玩家决策）");
        sb.AppendLine();
        sb.AppendLine($"<!-- campaign-economy-input-sha256: {InputFingerprint(root)} -->");
        sb.AppendLine();
        sb.AppendLine("> 本报告只证明当前配置下的供给、需求和结构边界。探索战斗伤亡、弹药实际消耗、逐件搜刮耗时、走位与玩家路线必须实机验收；胜率不等于战斗成本。所有生产量均为期望值，不是每局保底。");
        sb.AppendLine();

        sb.AppendLine("## 1. 四十天口粮账");
        sb.AppendLine();
        sb.AppendLine($"- 开局可直接吃的口粮：{camp.StartingReadyFood} 份（基础库存 + storage 柜）。");
        sb.AppendLine($"- 营地内一次性搜刮 {camp.LocalLootReadyFood} 份、世界固定搜刮 {worldReadyFood} 份；连同开局共 {fixedReadyFood} 份。");
        sb.AppendLine($"- 开局食材：{startingCalories} 热量；营地内一次性搜刮：{localCalories} 热量；世界固定食材：{worldCalories} 热量；合计 {allIngredientCalories} 热量。");
        sb.AppendLine($"- 无炊具每份 {baseCost} 热量，全部炊具每份 {fullCookwareCost} 热量。把全图食材无损合锅只是理论上界：分别可做 {allIngredientCalories / baseCost} / {allIngredientCalories / fullCookwareCost} 份，实际还会受建造、工时和每锅余数浪费影响。");
        sb.AppendLine();
        sb.AppendLine("| 常住人口 | 每天最低口粮 | 40 天最低需求 | 固定口粮+食材上界可撑天数（无炊具 / 全炊具） |");
        sb.AppendLine("|---:|---:|---:|---:|");
        foreach (int people in new[] { 3, 5, 7 })
        {
            int daily = FoodEconomy.DemandFor(people) * 2;
            int demand = daily * HordeTimeline.DeadlineDay;
            int baseEnvelope = fixedReadyFood + allIngredientCalories / baseCost;
            int equippedEnvelope = fixedReadyFood + allIngredientCalories / fullCookwareCost;
            sb.AppendLine($"| {people} | {daily} | {demand} | {F(baseEnvelope / (double)daily)} / {F(equippedEnvelope / (double)daily)} |");
        }
        sb.AppendLine();
        sb.AppendLine("结论：固定地图食物不是 40 天闭环；玩家必须持续依赖诱捕、种植、采集或交易。此处没有擅自假定招募日、陷阱数量或菜园开工日。");
        sb.AppendLine();

        sb.AppendLine("### 可持续生产的边际产能");
        sb.AppendLine();
        sb.AppendLine("| 设施规模 | 每日期望猎物 | 每日期望热量 | 折合基础口粮/天 |");
        sb.AppendLine("|---|---:|---:|---:|");
        double snareCaloriesPerCatch = TrapLogic.RabbitShare * FoodCalories.Of(Materials.RabbitMeatKey)
            + (1.0 - TrapLogic.RabbitShare) * FoodCalories.Of(Materials.RatMeatKey);
        foreach (int count in new[] { 1, 3, 6 })
        {
            double catches = TrapLogic.ExpectedCatchesPerPhase(count) * TrapLogic.RollsPerDay;
            double calories = catches * snareCaloriesPerCatch;
            sb.AppendLine($"| 圈套陷阱 ×{count} | {F(catches)} | {F(calories)} | {F(calories / baseCost)} |");
        }
        foreach (int count in new[] { 1, 3, 6 })
        {
            double catches = BirdTrapLogic.ExpectedCatchesPerPhase(count) * BirdTrapLogic.RollsPerDay;
            double calories = catches * FoodCalories.Of(Materials.BirdMeatKey);
            sb.AppendLine($"| 捕鸟陷阱 ×{count} | {F(catches)} | {F(calories)} | {F(calories / baseCost)} |");
        }
        double gardenCaloriesPerDay = CropPlotLogic.NetExpectedCaloriesPerGarden / CropPlotLogic.MaturesInDayNightCycles;
        sb.AppendLine($"| 满种菜园 ×1 | — | {F(gardenCaloriesPerDay)} | {F(gardenCaloriesPerDay / baseCost)} |");
        sb.AppendLine();
        sb.AppendLine("说明：诱捕还要宰杀，菜园数值已扣种薯但未扣建造材料与人工；三者都不能当成免费口粮。单个满种菜园和 6 个圈套都只覆盖约半个人的两餐需求，方向上保持了搜刮压力。");
        sb.AppendLine();

        sb.AppendLine("## 2. 弹药供给");
        sb.AppendLine();
        sb.AppendLine("| 弹药 | 固定搜刮量 | 每个子弹零件产量 |");
        sb.AppendLine("|---|---:|---:|");
        foreach (string key in ammoKeys)
        {
            int yield = key.StartsWith("ammo_arrow_", StringComparison.Ordinal)
                ? 0
                : CombatCatalog.Section<AmmoConfig>().Get(key).YieldPerBulletPart;
            sb.AppendLine($"| {Display(key)} | {FixedMaterial(key)} | {(yield == 0 ? "—" : yield.ToString(Inv))} |");
        }
        int bulletParts = FixedMaterial(Materials.BulletPartsKey);
        int gunpowder = FixedMaterial("gunpowder");
        int bulletCrafts = Math.Min(bulletParts, gunpowder);
        sb.AppendLine();
        sb.AppendLine($"固定子弹零件 {bulletParts} 个、火药 {gunpowder} 份；两者一比一共同限制为最多 {bulletCrafts} 次枪弹制作。若全押同一种弹，可额外制作：短 {bulletCrafts * BulletParts.YieldPer(AmmoKeys.ShortBullet)} / 中 {bulletCrafts * BulletParts.YieldPer(AmmoKeys.MediumBullet)} / 鹿弹 {bulletCrafts * BulletParts.YieldPer(AmmoKeys.Buckshot)} / 长 {bulletCrafts * BulletParts.YieldPer(AmmoKeys.LongBullet)} 发（四选一上界，不可相加）。");
        sb.AppendLine();
        sb.AppendLine("结论：报告能给出弹药库存天花板，但不能给出“够打几天”；那取决于玩家选枪、命中、撤退与每场敌人数，必须结合实机战斗成本验收。");
        sb.AppendLine();

        sb.AppendLine("## 3. 医疗供给");
        sb.AppendLine();
        sb.AppendLine("| 可直接使用的医疗物资 | 开局 | 营地内搜刮 | 世界固定搜刮 | 合计 |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (string key in medicalKeys)
        {
            int start = camp.StartingMaterials.GetValueOrDefault(key);
            int local = camp.LocalLootMaterials.GetValueOrDefault(key);
            int world = WorldMaterial(key);
            sb.AppendLine($"| {Display(key)} | {start} | {local} | {world} | {start + local + world} |");
        }
        sb.AppendLine();
        sb.AppendLine("结论：医疗库存是逐伤口消耗，不能换算成固定“生存天数”。骨折、开放伤和感染由战斗产生；应与 combat-cost 报告联合读，不能拿胜率代替耗材、卧床和永久残缺成本。");
        sb.AppendLine();

        sb.AppendLine("## 4. 白银与商人门槛");
        sb.AppendLine();
        sb.AppendLine($"- 开局：{Silver.Format(startingSilverCents)} 银；世界固定掉落：{Silver.Format(worldSilverCents)} 银；不卖物品时总上限：{Silver.Format(fixedSilverCents)} 银。");
        sb.AppendLine($"- 固定货架：《木匠入门》{MerchantShelf.CarpentryBasicsPrice}.00 银；互斥货物为损坏的狙击枪 {MerchantShelf.DamagedSniperRiflePrice}.00 银或《枪械维修指南》{MerchantShelf.GunsmithRepairGuidePrice}.00 银。");
        sb.AppendLine($"- 开局银足够单买《木匠入门》，但与互斥货物合购时还缺：狙击枪分支 {Math.Max(0, MerchantShelf.CarpentryBasicsPrice + MerchantShelf.DamagedSniperRiflePrice - ReadStartingSilver(root))}.00 银；维修指南分支 {Math.Max(0, MerchantShelf.CarpentryBasicsPrice + MerchantShelf.GunsmithRepairGuidePrice - ReadStartingSilver(root))}.00 银。卖出系统可补差，但会消耗别的资源，未计作免费收入。");
        sb.AppendLine();
        sb.AppendLine("本轮修复：7 处后期白银掉落曾把整银直接写进分制库存，导致 5～18 银实际变成 0.05～0.18 银；现已统一经整银→分转换，并由全图护栏拦截复发。");
        sb.AppendLine();

        sb.AppendLine("## 5. 探索搬运与主线时限");
        sb.AppendLine();
        sb.AppendLine($"- 世界图 {graph.Nodes.Count} 个目的地、{cacheCount} 个固定搜刮点；固定掉落总重 {F(totalWorldKg)} kg。");
        sb.AppendLine($"- 单人满操作能力硬负重上限 {F(CarryCapacity.For(1.0))} kg，且武器护甲先占这份额度。");
        sb.AppendLine("- 最重的五个目的地（把该点所有固定掉落一次搬空的静态重量，不含尸体装备与动态事件）：");
        foreach (DestinationWeight row in destinationWeights.Take(5))
        {
            sb.AppendLine($"  - {row.Name}：{row.CacheCount} 个搜刮点 / {row.LootStacks} 条固定掉落，{F(row.Kg)} kg");
        }
        WorldNode radio = graph.Find(ExplorationCache.BroadcastStationName)
            ?? throw new InvalidOperationException("世界图缺少广播台");
        sb.AppendLine($"- 主线硬时限：第 {HordeTimeline.DeadlineDay} 天；广播台配置行程 {radio.TravelSeconds / 60.0:0.#} 分钟，且要求前置“{string.Join(" + ", radio.Prereq)}”全部满足。发出设备是广播台固定发现点 `{RadioMainline.TransmitterDiscoveryId}`，不是随机 loot；取得后冻结尸潮时限。");
        sb.AppendLine($"- 回复军方后 {RadioMainline.MilitaryRaidDelayDays} 天触发军袭结局链。");
        sb.AppendLine();
        sb.AppendLine("结论：主线结构上可达、关键设备有固定投放且不会被随机经济卡死；但“第几天能抵达”取决于每关探索度、战斗、负重返程和玩家停留，静态账本不能伪造一个通关日数。");
        sb.AppendLine();
        sb.AppendLine("### 全部目的地固定掉落账");
        sb.AppendLine();
        sb.AppendLine("| 目的地 | 搜刮点 | 固定掉落条目 | 固定掉落重量 kg |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (DestinationWeight row in destinationWeights)
        {
            sb.AppendLine($"| {row.Name} | {row.CacheCount} | {row.LootStacks} | {F(row.Kg)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 6. 当前需要实机确认的风险");
        sb.AppendLine();
        sb.AppendLine("1. 3→5→7 人扩编时，固定食物很快被人口吞没；需要实测玩家能否在第一轮短缺前理解并建起烹饪/诱捕链。");
        sb.AppendLine("2. 多个重物资点单人无法一趟搬空；需要实测返程次数、逐件搜刮动画与地图停留是否形成压力，而不是纯拖时。");
        sb.AppendLine("3. 枪弹上限明确但消耗率未知；需要按真实关卡几何、命中、噪音引怪和撤退策略记录每趟净消耗。");
        sb.AppendLine("4. 医疗总量必须按伤口而非胜场评估；实机记录每趟带回的出血、骨折、感染、卧床天数和耗材。");
        sb.AppendLine("5. 第 40 天主线缓冲只能由完整路线实机跑出；报告不替代这项验收。");

        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static CampSnapshot ReadCamp(string root)
    {
        using JsonDocument resources = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "godot/data/camp_resources.json")));
        int readyFood = resources.RootElement.GetProperty("initialFood").GetInt32();
        var materials = new Dictionary<string, int>(StringComparer.Ordinal);
        var localMaterials = new Dictionary<string, int>(StringComparer.Ordinal);
        int localReadyFood = 0;

        using JsonDocument camp = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "godot/data/camp.json")));
        foreach (JsonElement prop in camp.RootElement.GetProperty("props").EnumerateArray())
        {
            if (!prop.TryGetProperty("role", out JsonElement role)
                || !prop.TryGetProperty("loot", out JsonElement loot))
            {
                continue;
            }
            bool isStarting = role.GetString() == "storage";
            bool isLocalLoot = role.GetString() == "loot";
            if (!isStarting && !isLocalLoot) continue;
            foreach (JsonElement item in loot.EnumerateArray())
            {
                string kind = item.GetProperty("kind").GetString() ?? "";
                int quantity = item.TryGetProperty("qty", out JsonElement qty) ? qty.GetInt32() : 1;
                if (kind == "food")
                {
                    if (isStarting) readyFood += quantity;
                    else localReadyFood += quantity;
                }
                if (kind == "material")
                {
                    string key = item.GetProperty("id").GetString() ?? "";
                    Dictionary<string, int> target = isStarting ? materials : localMaterials;
                    target[key] = target.GetValueOrDefault(key) + quantity;
                }
            }
        }
        return new CampSnapshot(readyFood, materials, localReadyFood, localMaterials);
    }

    private static int ReadStartingSilver(string root)
    {
        string source = File.ReadAllText(Path.Combine(root, "godot/scripts/CampMain.cs"));
        Match match = Regex.Match(source, @"MerchantStartingCurrency\s*=\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value, Inv) : throw new InvalidDataException("找不到开局白银常量");
    }

    private static int IngredientCalories(IEnumerable<LootItem> loot) => loot
        .Where(item => item.Kind == LootKind.Material && FoodCalories.Has(item.RefId))
        .Sum(item => FoodCalories.Of(item.RefId) * item.Quantity);

    private static string Display(string key) => Materials.Find(key)?.DisplayName ?? key;
    private static string F(double value) => value.ToString("0.##", Inv);

    private static string InputFingerprint(string root)
    {
        var files = new List<string>(InputFiles);
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "godot/data/config"), "*.json")
            .Where(path => !path.EndsWith(".schema.json", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path)));
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "src/DeadSignal.Combat"), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)));

        using var sha = SHA256.Create();
        foreach (string relative in files.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            byte[] name = Encoding.UTF8.GetBytes(relative.Replace('\\', '/'));
            sha.TransformBlock(name, 0, name.Length, null, 0);
            byte[] content = File.ReadAllBytes(Path.Combine(root, relative));
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private sealed record CampSnapshot(
        int StartingReadyFood,
        IReadOnlyDictionary<string, int> StartingMaterials,
        int LocalLootReadyFood,
        IReadOnlyDictionary<string, int> LocalLootMaterials);
    private sealed record DestinationLoot(int CacheCount, IReadOnlyList<LootItem> Loot);
    private sealed record DestinationWeight(string Name, int CacheCount, int LootStacks, double Kg);
}
