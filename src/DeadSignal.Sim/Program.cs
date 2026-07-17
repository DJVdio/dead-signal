using System.Globalization;
using System.Text;
using DeadSignal.Combat;

// Dead Signal — 战斗数值蒙特卡洛模拟器 v2
// 武器×护甲逐层结算 + 部位命中分配 + 效果触发（流血/切除/震荡/骨折）统计。
// 数值均为原型期拟定（标注"待调"），供数值微调用。
// 远程武器的期望伤害口径：假设弹道命中（几何 miss 不在模拟范围）。
//
// 模式（命令行开关）—— 🔴 **加一个分支就补一行，别让这张表再落后于 dispatch**：
// 这张表此前只登记了 duel / zombiecloth / 默认三个，而下面实际注册了 18 个。`cost` 模式当年能以死代码状态
// 长期没人发现（见下方 [T53] 注释），根因之一就是**从这里根本看不见它存在**。
//
//   [outPath]           → 聚合蒙特卡洛模式（默认）
//   duel [outPath]      → 对决战报模式（1v1 到死叙事）
//   cost [outPath]      → 打赢之后还剩什么：阵亡/永久残缺/骨折的代价表（🔴 胜率不是成本）
//   lanchester [p]      → N 只丧尸围攻的平方律断崖
//   weaponsweep [p]     → 武器 × 甲组失衡诊断 + what-if 敏感度扫描
//   userplan [p]        → [批次18补] 锐器降下限/钝器提伤 what-if（⚠️ **追加**写入，默认与 weaponsweep 同路径）
//   bleedcal [p]        → 流血轴复验（锯齿剑刃梯度）
//   shotgun [p]         → 自制霰弹枪多弹丸校准
//   archery [p]         → 弓弩 8×4 组合校准
//   zombiecloth [p]     → 丧尸穿「生前的破衣服」前后对比（挡下率 0% → ?、玩家胜率变化）
//   wallcal [p]         → 围墙/大门破防校准
//   goldfinger          → 金手指帮之战（胜率 + 代价）
//   dogcal [outPath]    → 布鲁斯（狗）战斗单元校准
//   dogsweep            → 布鲁斯参数扫描（控制台）
//   baselinecal         → 战斗基线复核（控制台）
//   visioncal           → 视野曲线 & 光照解析校准（控制台）
//   watchcal            → 夜防发现率矩阵（控制台）
//   watchsweep          → 夜防覆盖假设扫描（控制台）
//   endgamecal          → 尸潮终局节奏解析校准（控制台）

if (args.Length > 0 && args[0] == "duel")
{
    string duelOut = args.Length > 1 ? args[1] : "docs/research/2026-07-05-duel-log.md";
    DuelReport.Run(duelOut);
    return;
}

if (args.Length > 0 && args[0] == "zombiecloth")
{
    string zcOut = args.Length > 1 ? args[1] : "docs/research/2026-07-13-zombie-cloth.md";
    ZombieClothCalibration.Run(zcOut);
    return;
}

if (args.Length > 0 && args[0] == "shotgun")
{
    string sgOut = args.Length > 1 ? args[1] : "docs/research/2026-07-13-shotgun-calibration.md";
    ShotgunCalibration.Run(sgOut);
    return;
}

if (args.Length > 0 && args[0] == "archery")
{
    string arOut = args.Length > 1 ? args[1] : "docs/research/2026-07-13-archery-calibration.md";
    ArcheryCalibration.Run(arOut);
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

if (args.Length > 0 && args[0] == "goldfinger")
{
    // 金手指帮之战：不只出胜率，还出**赢下来要付的代价**（阵亡/永久残缺/骨折）——战斗本身就是成本。
    GoldfingerCalibration.Run();
    return;
}

if (args.Length > 0 && args[0] == "lanchester")
{
    // 兰彻斯特平方律：N 只丧尸围攻一个人的胜率断崖。[T58] 三级流血的「封顶大流血」正是为了打断它——
    // 群殴挨的一堆浅爪全是小流血、且封顶，不会像旧制那样被线性放大。**这份表就是验收判据。**
    // ⚠️ 此前 harness 在、但 Program 里**没注册这个分支**（死代码）⇒ 报告里的数是旧口径的。[T58] 补上。
    string lcOut = args.Length > 1 ? args[1] : "docs/research/2026-07-14-lanchester.md";
    LanchesterCalibration.Run(lcOut);
    return;
}

if (args.Length > 0 && args[0] == "wallcal")
{
    // ⚠ 默认出口**不能**指向人写的分析报告（`2026-07-13-wall-hp-analysis.md`）——那份是手写结论，
    // 复跑 harness 会把它整篇覆盖掉（本单踩过这颗雷）。harness 只写自己的机器生成表。
    string wcOut = args.Length > 1 ? args[1] : "docs/research/2026-07-13-wall-breach-calibration.md";
    WallCalibration.Run(wcOut);
    return;
}

// ⚠️ **`userplan` 与 `weaponsweep` 默认写同一个文件**，且两者写法相反：`userplan` 是**追加**
//    （`UserPlanCalibration.Run` 尾部 existing + sb），`weaponsweep` 是**整篇覆盖**。后果：
//    跑 weaponsweep 会抹掉 userplan 追加的章节；连跑两次 userplan 会把自己的章节叠两份。
//    ⇒ 复跑任一个之前先想清楚要哪份，或显式传 outPath 分开落盘。（默认路径要不要拆是 docs/research 侧的决定，未擅改。）
if (args.Length > 0 && args[0] == "userplan")
{
    string upOut = args.Length > 1 ? args[1] : "docs/research/2026-07-13-weapon-recalib.md";
    UserPlanCalibration.Run(upOut);
    return;
}

if (args.Length > 0 && args[0] == "weaponsweep")
{
    string wcOut = args.Length > 1 ? args[1] : "docs/research/2026-07-13-weapon-recalib.md";
    WeaponCalibration.Run(wcOut);
    return;
}

if (args.Length > 0 && args[0] == "bleedcal")
{
    string bcOut = args.Length > 1 ? args[1] : "docs/research/2026-07-14-bleed-axis.md";
    BleedCalibration.Run(bcOut);
    return;
}

// 🔴 [T53] `cost` 模式此前**根本没注册**（CLAUDE.md 与 combat-cost.md 都说有，`CombatCostCalibration.cs` 也在，
//    但 Program.cs 里没有这个分支 ⇒ 它是**死代码**，报告无法重算）。
//    后果很阴险：`dotnet run … cost <路径>` 会掉进下面的默认分支，把 **"cost" 当成 outPath**，
//    于是在仓库根目录**写出一个名叫 `cost` 的垃圾文件**，而 combat-cost.md 纹丝不动 ——
//    看起来"重跑了且零漂移"，实际上一次都没跑。（我自己就先被它骗过一次。）
if (args.Length > 0 && args[0] == "cost")
{
    string ccOut = args.Length > 1 ? args[1] : "docs/research/2026-07-14-combat-cost.md";
    CombatCostCalibration.Run(ccOut);
    return;
}

const int Iterations = 100_000;
int seed = 20260705;
string outPath = args.Length > 0
    ? args[0]
    : "docs/research/2026-07-05-combat-sim-v2.md";

// ---- 武器表（权威数据源 WeaponTable；穿透/类型来自设计文档第 5 节，伤害区间拟定待调）----
var weapons = WeaponTable.Arsenal().ToList();

// ---- 护甲层（权威数据源 ArmorTable = 数据表『护甲表』[SPEC-B18]）----
// 三档甲组按"层"叠穿（贴身/外套/护甲层各一件），覆盖部位以表为准——护甲不再全身覆盖，
// 头/手/脚等未覆盖部位为裸命中，故甲组的实际减伤低于其防御值字面量。
var combos = new List<(string Name, ArmorLayer[] Layers)>
{
    ("无甲", Array.Empty<ArmorLayer>()),
    ("长袖布衣", new[] { ArmorTable.LongSleeveShirt() }),
    ("皮夹克+长袖布衣", new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() }),
    ("板甲+粗布外套+长袖布衣", new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }),
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
sb.AppendLine("> 护甲组合（从外到内）：板甲(锐50/钝25，护躯干+双臂+双腿) → 粗布外套(锐6/钝3，护胸腹双臂) → 长袖布衣(锐6/钝3，护胸腹双臂)。**头/手/脚不在覆盖内**（[SPEC-B18] 覆盖收窄）。");
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
    $"1. **锐器无甲流血高发**：匕首/长剑/重剑无甲流血率 {Get("匕首", "无甲").BleedRate:P0}/{Get("长剑", "无甲").BleedRate:P0}/{Get("重剑", "无甲").BleedRate:P0}——锐器\"见血\"手感成立；套满甲后长剑流血率降到 {Get("长剑", "板甲+粗布外套+长袖布衣").BleedRate:P0}（甲把伤害压到降解阈值以下）。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"2. **切除偏高，需拍板【存疑】**：无甲切除率 狙击枪 {Get("狙击枪", "无甲").SeverRate:P1}、步枪 {Get("步枪", "无甲").SeverRate:P1}、重剑 {Get("重剑", "无甲").SeverRate:P1}——因规则\"单击≥部位MaxHP即切除\"叠加细部位低血量（眼6/指趾10、手脚16，胸20/腹16/大腿12/小腿11/手臂21/头16；[SPEC-B17] 躯干腿已细分），强武器打中小部位几乎必断。若不想角色被频繁肢解，需上调细部位 HP 或把切除门槛收紧（如仅四肢可断、或 dmg≥MaxHP×系数）。满甲后大幅回落（狙击 {Get("狙击枪", "板甲+粗布外套+长袖布衣").SeverRate:P1}）。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"3. **钝器震荡隔甲生效**：破甲锤对满甲震荡率 {Get("破甲锤", "板甲+粗布外套+长袖布衣").ConcussionRate:P1} 与无甲 {Get("破甲锤", "无甲").ConcussionRate:P1} 接近——因震荡用初始武器 roll、不吃护甲，验证\"甲没破人被锤懵\"落地正确。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"4. **钝器满甲仍能骨折**：破甲锤满甲骨折率 {Get("破甲锤", "板甲+粗布外套+长袖布衣").FractureRate:P1}、期望伤害 {Get("破甲锤", "板甲+粗布外套+长袖布衣").ExpectedDamage:F2}；同期锐器长剑满甲骨折率 0（骨折仅天然钝器）。钝器作为\"破甲/致残\"路线的定位在数值上成立。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"5. **高穿透碾压多层甲**：狙击枪满甲期望穿透层数 {Get("狙击枪", "板甲+粗布外套+长袖布衣").ExpectedPenLayers:F2}（满层=3）、期望伤害 {Get("狙击枪", "板甲+粗布外套+长袖布衣").ExpectedDamage:F2}，与匕首满甲 {Get("匕首", "板甲+粗布外套+长袖布衣").ExpectedDamage:F2} 形成数量级差，弹药稀缺是唯一约束——需靠掉落/上弹时间限制。");
sb.AppendLine();
sb.AppendLine("> 效果概率系数（流血 k=1.0/cap0.9、骨折 k=0.8/cap0.6、震荡头 k=0.9/cap0.85、震荡躯干 k=0.25/cap0.4）与部位 HP 均拟定待调；上表用于校准方向，非终值。");

var report = sb.ToString();
SimReport.Write(outPath, report); // 出处戳 + 落盘（含建目录）

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
