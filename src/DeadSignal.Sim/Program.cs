using System.Globalization;
using System.Text;
using DeadSignal.Combat;

// Dead Signal — 战斗数值蒙特卡洛模拟器
// 内置设计文档武器表 + 典型护甲组合，每组合跑 N 次，输出 markdown 基线表。
// 数值均为原型期拟定（标注"待调"），供数值微调用。

const int Iterations = 100_000;
int seed = 20260704;
string outPath = args.Length > 0
    ? args[0]
    : "docs/research/2026-07-04-combat-sim-baseline.md";

// ---- 武器表（伤害区间为拟定值待调；穿透/类型来自设计文档第 5 节）----
var weapons = new List<Weapon>
{
    new() { Name = "匕首",   DamageMin = 4,  DamageMax = 14, Penetration = 0.09, DamageType = DamageType.Sharp },
    new() { Name = "短剑",   DamageMin = 6,  DamageMax = 20, Penetration = 0.12, DamageType = DamageType.Sharp },
    new() { Name = "长剑",   DamageMin = 10, DamageMax = 30, Penetration = 0.18, DamageType = DamageType.Sharp },
    new() { Name = "重剑",   DamageMin = 14, DamageMax = 40, Penetration = 0.24, DamageType = DamageType.Sharp },
    new() { Name = "棍棒",   DamageMin = 7,  DamageMax = 9,  Penetration = 0.03, DamageType = DamageType.Blunt }, // 文档明确 7-9
    new() { Name = "尖头锤", DamageMin = 12, DamageMax = 16, Penetration = 0.05, DamageType = DamageType.Blunt },
    new() { Name = "破甲锤", DamageMin = 20, DamageMax = 28, Penetration = 0.20, DamageType = DamageType.Blunt },
    new() { Name = "土制枪", DamageMin = 8,  DamageMax = 16, Penetration = 0.10, DamageType = DamageType.Sharp },
    new() { Name = "手枪",   DamageMin = 12, DamageMax = 20, Penetration = 0.15, DamageType = DamageType.Sharp },
    new() { Name = "冲锋枪", DamageMin = 10, DamageMax = 18, Penetration = 0.18, DamageType = DamageType.Sharp },
    new() { Name = "步枪",   DamageMin = 20, DamageMax = 35, Penetration = 0.21, DamageType = DamageType.Sharp },
    new() { Name = "狙击枪", DamageMin = 40, DamageMax = 70, Penetration = 0.70, DamageType = DamageType.Sharp },
};

// ---- 护甲层（防御为拟定值：锐防≈2×钝防，板甲比例更高）----
ArmorLayer Cloth() => new() { Name = "布衣", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2, Weight = 1 };
ArmorLayer Leather() => new() { Name = "皮甲", Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 4 };
ArmorLayer Plate() => new() { Name = "板甲", Slot = ArmorSlot.Plate, SharpDefense = 34, BluntDefense = 11, Weight = 12 };

var combos = new List<(string Name, ArmorLayer[] Layers)>
{
    ("无甲", Array.Empty<ArmorLayer>()),
    ("布衣", new[] { Cloth() }),
    ("皮甲+布衣", new[] { Leather(), Cloth() }),
    ("板甲+皮甲+布衣", new[] { Plate(), Leather(), Cloth() }),
};

var chest = new BodyPart { Name = "胸部", VolumeWeight = 40 };

// ---- 跑模拟 ----
var rng = new SystemRandomSource(seed);
var resolver = new CombatResolver(rng);

var rows = new List<Row>();
foreach (var w in weapons)
{
    foreach (var (comboName, layerTemplate) in combos)
    {
        var layers = CombatResolver.OrderOuterToInner(layerTemplate);
        long dmgSum = 0;
        long penLayerSum = 0;
        int full = 0, half = 0, blocked = 0;

        for (int i = 0; i < Iterations; i++)
        {
            var r = resolver.Resolve(w, layers, chest);
            dmgSum += r.FinalDamage;
            penLayerSum += r.LayersPenetrated;

            if (r.Terminated)
            {
                blocked++;
            }
            else if (HadHalf(r))
            {
                half++;
            }
            else
            {
                full++;
            }
        }

        rows.Add(new Row(
            w.Name, comboName,
            (double)dmgSum / Iterations,
            (double)full / Iterations,
            (double)half / Iterations,
            (double)blocked / Iterations,
            (double)penLayerSum / Iterations));
    }
}

// ---- 输出 markdown ----
var sb = new StringBuilder();
sb.AppendLine("# Dead Signal 战斗数值基线（蒙特卡洛）");
sb.AppendLine();
sb.AppendLine(CultureInfo.InvariantCulture, $"日期：2026-07-04　样本：每组合 {Iterations:N0} 次　种子：{seed}");
sb.AppendLine();
sb.AppendLine("> 武器伤害区间、护甲防御值均为原型期**拟定值待调**（穿透/伤害类型来自设计文档第 5 节）。");
sb.AppendLine("> 护甲组合（从外到内）：板甲(锐34/钝11) → 皮甲(锐12/钝6) → 布衣(锐4/钝2)。");
sb.AppendLine("> 分类口径：`全伤`=穿透全部护甲且无任何层降解；`半伤`=穿透但至少一层发生半伤降解；`无伤`=被某层挡下终止。");
sb.AppendLine();
sb.AppendLine("| 武器 | 类型 | 护甲组合 | 期望伤害 | 全伤率 | 半伤率 | 无伤率 | 期望穿透层数 |");
sb.AppendLine("|------|------|----------|---------:|-------:|-------:|-------:|-------------:|");
foreach (var w in weapons)
{
    string type = w.DamageType == DamageType.Sharp ? "锐" : "钝";
    foreach (var (comboName, _) in combos)
    {
        var r = rows.First(x => x.Weapon == w.Name && x.Combo == comboName);
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"| {w.Name} | {type} | {comboName} | {r.ExpectedDamage:F2} | {r.FullRate:P1} | {r.HalfRate:P1} | {r.BlockedRate:P1} | {r.ExpectedPenLayers:F2} |");
    }
}
sb.AppendLine();

// ---- 数值观察（自动计算若干关键值，供人工核对/补充）----
sb.AppendLine("## 数值观察");
sb.AppendLine();

double PlateBlocked(string weapon) =>
    rows.First(x => x.Weapon == weapon && x.Combo == "板甲+皮甲+布衣").BlockedRate;
double NoArmorDmg(string weapon) =>
    rows.First(x => x.Weapon == weapon && x.Combo == "无甲").ExpectedDamage;
double PlateDmg(string weapon) =>
    rows.First(x => x.Weapon == weapon && x.Combo == "板甲+皮甲+布衣").ExpectedDamage;

sb.AppendLine(CultureInfo.InvariantCulture,
    $"1. **板甲对锐器免伤（无伤率）**：匕首 {PlateBlocked("匕首"):P1}、长剑 {PlateBlocked("长剑"):P1}、狙击枪 {PlateBlocked("狙击枪"):P1}。高穿透狙击枪几乎无视板甲，符合\"靠弹药稀缺约束\"的设计意图。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"2. **钝器克甲是否成立**：破甲锤(钝,穿透20%)对满甲无伤率 {PlateBlocked("破甲锤"):P1}、期望伤害 {PlateDmg("破甲锤"):F2}；同级锐器长剑对满甲期望伤害 {PlateDmg("长剑"):F2}。破甲锤打满甲的期望伤害应显著高于同级锐器才算\"钝器克重甲\"成立。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"3. **护甲衰减梯度**：长剑无甲 {NoArmorDmg("长剑"):F2} → 满甲 {PlateDmg("长剑"):F2}（衰减 {(1 - PlateDmg("长剑") / NoArmorDmg("长剑")):P0}）；棍棒无甲 {NoArmorDmg("棍棒"):F2} → 满甲 {PlateDmg("棍棒"):F2}。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"4. **低端钝器对重甲近乎无效**：棍棒(7-9,穿透3%)对满甲无伤率 {PlateBlocked("棍棒"):P1}，白手起家武器\"打不动板甲\"的手感成立。");
sb.AppendLine(CultureInfo.InvariantCulture,
    $"5. **穿透深度**：狙击枪对满甲期望穿透层数 {rows.First(x => x.Weapon == "狙击枪" && x.Combo == "板甲+皮甲+布衣").ExpectedPenLayers:F2}（满层=3），高穿透使多层护甲边际收益骤降。");
sb.AppendLine();
sb.AppendLine("> 以上为自动计算值，用于快速核对拟定数值方向；正式调参时结合击杀所需命中次数与武器稀有度综合评估。");

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
    double ExpectedPenLayers);
