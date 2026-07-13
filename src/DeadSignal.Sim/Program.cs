using System.Globalization;
using System.Text;
using DeadSignal.Combat;

// Dead Signal — 战斗数值蒙特卡洛模拟器 v2
// 武器×护甲逐层结算 + 部位命中分配 + 效果触发（流血/切除/震荡/骨折）统计。
// 数值均为原型期拟定（标注"待调"），供数值微调用。
// 远程武器的期望伤害口径：假设弹道命中（几何 miss 不在模拟范围）。
//
// 模式（命令行开关）：
//   duel [outPath]   → 对决战报模式（1v1 到死叙事）
//   [outPath]        → 聚合蒙特卡洛模式（默认）

if (args.Length > 0 && args[0] == "duel")
{
    string duelOut = args.Length > 1 ? args[1] : "docs/research/2026-07-05-duel-log.md";
    DuelReport.Run(duelOut);
    return;
}

if (args.Length > 0 && args[0] == "dogcal")
{
    string dogOut = args.Length > 1 ? args[1] : "docs/research/2026-07-12-dog-calibration.md";
    DogCalibration.Run(dogOut);
    return;
}

if (args.Length > 0 && args[0] == "dogsweep")
{
    DogCalibration.Sweep();
    return;
}

if (args.Length > 0 && args[0] == "baselinecal")
{
    DogCalibration.Baselines();
    return;
}

if (args.Length > 0 && args[0] == "visioncal")
{
    VisionCalibration.Run();
    return;
}

if (args.Length > 0 && args[0] == "watchcal")
{
    WatchCalibration.Run();
    return;
}

if (args.Length > 0 && args[0] == "watchsweep")
{
    WatchCalibration.Sweep();
    return;
}

if (args.Length > 0 && args[0] == "endgamecal")
{
    EndgameCalibration.Run();
    return;
}

const int Iterations = 100_000;
int seed = 20260705;
string outPath = args.Length > 0
    ? args[0]
    : "docs/research/2026-07-05-combat-sim-v2.md";

// ---- 武器表（权威数据源 WeaponTable；穿透/类型来自设计文档第 5 节，伤害区间拟定待调）----
var weapons = WeaponTable.Arsenal().ToList();

// ---- 护甲层（权威数据源 ArmorTable；锐防≈2×钝防，板甲比例更高）----
var combos = new List<(string Name, ArmorLayer[] Layers)>
{
    ("无甲", Array.Empty<ArmorLayer>()),
    ("布衣", new[] { ArmorTable.Cloth() }),
    ("皮甲+布衣", new[] { ArmorTable.Leather(), ArmorTable.Cloth() }),
    ("板甲+皮甲+布衣", new[] { ArmorTable.Plate(), ArmorTable.Leather(), ArmorTable.Cloth() }),
};

var bodyParts = HumanBody.Parts();

// ---- 跑模拟（Parallel.ForEach over 单元，每单元 seed+idx*7919 独立 RNG）----
// 每个 (武器×护甲组合) 单元用独立 SystemRandomSource：随机流按单元切分后
// 与顺序执行位级一致（parbench 验证），聚合统计等价，且同 seed 同结果可复现。
// 单元之间无共享可变态（各写自己的 rowsArr[Idx] 槽位），线程安全。
var cells = new List<(int Idx, Weapon Weapon, string ComboName, ArmorLayer[] LayerTemplate)>();
int cellIdx = 0;
foreach (var w in weapons)
{
    foreach (var (comboName, layerTemplate) in combos)
    {
        cells.Add((cellIdx++, w, comboName, layerTemplate));
    }
}

var rowsArr = new Row[cells.Count];
Parallel.ForEach(cells, cell =>
{
    var rng = new SystemRandomSource(seed + cell.Idx * 7919);
    var resolver = new CombatResolver(rng);
    var hitSelector = new VolumeWeightedHitSelector(rng);
    var effectResolver = new CombatEffectResolver(rng);

    var layers = CombatResolver.OrderOuterToInner(cell.LayerTemplate);
    double dmgSum = 0; // 伤害改小数后（[SPEC-B14-补6]）累计器随之 double，期望伤害不再吞小数
    long penLayerSum = 0;
    int full = 0, half = 0, blocked = 0;
    int bleed = 0, sever = 0, concuss = 0, fracture = 0;

    for (int i = 0; i < Iterations; i++)
    {
        var part = hitSelector.Select(bodyParts);
        var r = resolver.Resolve(cell.Weapon, layers, part);
        dmgSum += r.FinalDamage;
        penLayerSum += r.LayersPenetrated;

        if (r.Terminated) blocked++;
        else if (HadHalf(r)) half++;
        else full++;

        // 效果统计：每次命中打一具满血人体
        var body = new Body(bodyParts);
        var outcome = effectResolver.Apply(body, cell.Weapon, r);
        bool hasBleed = false, hasSever = false, hasConc = false, hasFrac = false;
        foreach (var e in outcome.Effects)
        {
            switch (e.Kind)
            {
                case DamageEffectKind.Bleed: hasBleed = true; break;
                case DamageEffectKind.Sever: hasSever = true; break;
                case DamageEffectKind.Concussion: hasConc = true; break;
                case DamageEffectKind.Fracture: hasFrac = true; break;
            }
        }

        if (hasBleed) bleed++;
        if (hasSever) sever++;
        if (hasConc) concuss++;
        if (hasFrac) fracture++;
    }

    rowsArr[cell.Idx] = new Row(
        cell.Weapon.Name, cell.ComboName,
        dmgSum / Iterations,
        (double)full / Iterations,
        (double)half / Iterations,
        (double)blocked / Iterations,
        (double)penLayerSum / Iterations,
        (double)bleed / Iterations,
        (double)sever / Iterations,
        (double)concuss / Iterations,
        (double)fracture / Iterations);
});

var rows = rowsArr.ToList();

// ---- 输出 markdown ----
Row Get(string weapon, string combo) => rows.First(x => x.Weapon == weapon && x.Combo == combo);

var sb = new StringBuilder();
sb.AppendLine("# Dead Signal 战斗数值基线 v2（蒙特卡洛）");
sb.AppendLine();
sb.AppendLine(CultureInfo.InvariantCulture, $"日期：2026-07-05　样本：每组合 {Iterations:N0} 次　种子：{seed}");
sb.AppendLine();
sb.AppendLine("> 武器伤害区间、护甲防御值、效果概率系数、部位 HP/命中权重均为原型期**拟定值待调**（穿透/伤害类型来自设计文档第 5 节）。");
sb.AppendLine("> 护甲组合（从外到内）：板甲(锐34/钝11) → 皮甲(锐12/钝6) → 布衣(锐4/钝2)，套在全身各部位。");
sb.AppendLine("> 命中部位按人体细部位表体积加权随机分配（躯干/头/四肢/手脚/眼/鼻/下巴）。效果打在满血人体上统计。");
sb.AppendLine("> **远程武器（⌖）期望伤害假设弹道命中**——几何误差角 miss 不在本模拟范围（属实时层）。");
sb.AppendLine();

sb.AppendLine("## 表 1：伤害与穿透");
sb.AppendLine();
sb.AppendLine("| 武器 | 类型 | 护甲组合 | 期望伤害 | 全伤率 | 半伤率 | 无伤率 | 期望穿透层数 |");
sb.AppendLine("|------|------|----------|---------:|-------:|-------:|-------:|-------------:|");
foreach (var w in weapons)
{
    string type = (w.DamageType == DamageType.Sharp ? "锐" : "钝") + (w.IsRanged ? "⌖" : "");
    foreach (var (comboName, _) in combos)
    {
        var r = Get(w.Name, comboName);
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"| {w.Name} | {type} | {comboName} | {r.ExpectedDamage:F2} | {r.FullRate:P1} | {r.HalfRate:P1} | {r.BlockedRate:P1} | {r.ExpectedPenLayers:F2} |");
    }
}
sb.AppendLine();

sb.AppendLine("## 表 2：效果触发率（每次命中，命中部位随机）");
sb.AppendLine();
sb.AppendLine("| 武器 | 类型 | 护甲组合 | 流血率 | 切除率 | 震荡率 | 骨折率 |");
sb.AppendLine("|------|------|----------|-------:|-------:|-------:|-------:|");
foreach (var w in weapons)
{
    string type = (w.DamageType == DamageType.Sharp ? "锐" : "钝") + (w.IsRanged ? "⌖" : "");
    foreach (var (comboName, _) in combos)
    {
        var r = Get(w.Name, comboName);
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"| {w.Name} | {type} | {comboName} | {r.BleedRate:P1} | {r.SeverRate:P2} | {r.ConcussionRate:P1} | {r.FractureRate:P1} |");
    }
}
sb.AppendLine();

// ---- 数值观察 ----
sb.AppendLine("## 数值观察");
sb.AppendLine();
sb.AppendLine(CultureInfo.InvariantCulture,
    $"1. **锐器无甲流血高发**：匕首/长剑/重剑无甲流血率 {Get("匕首", "无甲").BleedRate:P0}/{Get("长剑", "无甲").BleedRate:P0}/{Get("重剑", "无甲").BleedRate:P0}——锐器\"见血\"手感成立；套满甲后长剑流血率降到 {Get("长剑", "板甲+皮甲+布衣").BleedRate:P0}（甲把伤害压到降解阈值以下）。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"2. **切除偏高，需拍板【存疑】**：无甲切除率 狙击枪 {Get("狙击枪", "无甲").SeverRate:P1}、步枪 {Get("步枪", "无甲").SeverRate:P1}、重剑 {Get("重剑", "无甲").SeverRate:P1}——因规则\"单击≥部位MaxHP即切除\"叠加细部位低血量（眼6/指趾10、手脚16，胸20/腹16/大腿12/小腿11/上臂21/头16；[SPEC-B17] 躯干腿已细分），强武器打中小部位几乎必断。若不想角色被频繁肢解，需上调细部位 HP 或把切除门槛收紧（如仅四肢可断、或 dmg≥MaxHP×系数）。满甲后大幅回落（狙击 {Get("狙击枪", "板甲+皮甲+布衣").SeverRate:P1}）。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"3. **钝器震荡隔甲生效**：破甲锤对满甲震荡率 {Get("破甲锤", "板甲+皮甲+布衣").ConcussionRate:P1} 与无甲 {Get("破甲锤", "无甲").ConcussionRate:P1} 接近——因震荡用初始武器 roll、不吃护甲，验证\"甲没破人被锤懵\"落地正确。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"4. **钝器满甲仍能骨折**：破甲锤满甲骨折率 {Get("破甲锤", "板甲+皮甲+布衣").FractureRate:P1}、期望伤害 {Get("破甲锤", "板甲+皮甲+布衣").ExpectedDamage:F2}；同期锐器长剑满甲骨折率 0（骨折仅天然钝器）。钝器作为\"破甲/致残\"路线的定位在数值上成立。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"5. **高穿透碾压多层甲**：狙击枪满甲期望穿透层数 {Get("狙击枪", "板甲+皮甲+布衣").ExpectedPenLayers:F2}（满层=3）、期望伤害 {Get("狙击枪", "板甲+皮甲+布衣").ExpectedDamage:F2}，与匕首满甲 {Get("匕首", "板甲+皮甲+布衣").ExpectedDamage:F2} 形成数量级差，弹药稀缺是唯一约束——需靠掉落/上弹时间限制。");
sb.AppendLine();
sb.AppendLine("> 效果概率系数（流血 k=1.0/cap0.9、骨折 k=0.8/cap0.6、震荡头 k=0.9/cap0.85、震荡躯干 k=0.25/cap0.4）与部位 HP 均拟定待调；上表用于校准方向，非终值。");

var report = sb.ToString();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, report);

Console.WriteLine($"已写出 {outPath}（{weapons.Count} 武器 × {combos.Count} 组合，各 {Iterations:N0} 次）。");
Console.WriteLine();
Console.Write(report);

return;

static bool HadHalf(CombatResult r)
{
    foreach (var l in r.Layers)
    {
        if (l.Outcome == LayerOutcome.Half)
        {
            return true;
        }
    }

    return false;
}

record Row(
    string Weapon,
    string Combo,
    double ExpectedDamage,
    double FullRate,
    double HalfRate,
    double BlockedRate,
    double ExpectedPenLayers,
    double BleedRate,
    double SeverRate,
    double ConcussionRate,
    double FractureRate);
