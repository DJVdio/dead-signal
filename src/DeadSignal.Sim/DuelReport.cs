using System.Globalization;
using System.Text;
using DeadSignal.Combat;

/// <summary>
/// 对决战报模式：跑若干组 1v1 对决，渲染中文逐回合战报到 markdown。
/// 让用户以叙事视角检验规则合理性。武器攻速/装备名/对局配置均拟定待调。
/// 远程武器在对决里"假设弹道命中"（几何 miss 属实时层，不在本模拟范围）。
/// </summary>
public static class DuelReport
{
    // ---- 武器/护甲（权威数据源 WeaponTable / ArmorTable，本处仅转发）----
    private static Weapon Dagger() => WeaponTable.Dagger();
    private static Weapon Longsword() => WeaponTable.Longsword();
    private static Weapon Club() => WeaponTable.Club();
    private static Weapon Warhammer() => WeaponTable.Warhammer();
    private static Weapon Pistol() => WeaponTable.Pistol();
    private static Weapon ZombieClaw() => WeaponTable.ZombieClaw();

    // 三档甲组同 Program.cs（数据表『护甲表』[SPEC-B18]）：贴身=长袖布衣、外套=皮夹克/粗布外套、护甲层=板甲。
    private static ArmorLayer Shirt() => ArmorTable.LongSleeveShirt();
    private static ArmorLayer Jacket() => ArmorTable.LeatherJacket();
    private static ArmorLayer Coat() => ArmorTable.CoarseClothCoat();
    private static ArmorLayer Plate() => ArmorTable.Plate();

    // ---- 装备名映射（部位→掉落物名，供切除战报）----
    private static Dictionary<string, string> Kit(string sleeve, string glove, string legwear) => new()
    {
        [HumanBody.LeftArm] = sleeve, [HumanBody.RightArm] = sleeve,
        [HumanBody.LeftHand] = glove, [HumanBody.RightHand] = glove,
        [HumanBody.LeftLeg] = legwear, [HumanBody.RightLeg] = legwear,
        [HumanBody.LeftCalf] = legwear, [HumanBody.RightCalf] = legwear,
    };

    private static (DuelFighter, DuelFighter)[] Matchups() => new[]
    {
        (
            new DuelFighter { Name = "匕首幸存者", Weapons = new[] { new WeaponMount { Weapon = Dagger() } }, Armor = new[] { Shirt() }, Equipment = Kit("布袖", "布手套", "布裤") },
            new DuelFighter { Name = "游荡丧尸", Weapons = new[] { new WeaponMount { Weapon = ZombieClaw(), RequiresHand = false } }, BodyFactory = HumanBody.NewZombieBody, ArmorFactory = ZombieOutfit.RollArmor }
        ),
        (
            new DuelFighter { Name = "长剑手", Weapons = new[] { new WeaponMount { Weapon = Longsword() } }, Armor = new[] { Jacket(), Shirt() }, Equipment = Kit("皮护臂", "皮手套", "皮护腿") },
            new DuelFighter { Name = "棍棒劫掠者", Weapons = new[] { new WeaponMount { Weapon = Club() } }, Armor = new[] { Shirt() }, Equipment = Kit("布袖", "布手套", "布裤") }
        ),
        (
            new DuelFighter { Name = "破甲锤手", Weapons = new[] { new WeaponMount { Weapon = Warhammer() } }, Armor = new[] { Jacket(), Shirt() }, Equipment = Kit("皮护臂", "皮手套", "皮护腿") },
            new DuelFighter { Name = "板甲重装", Weapons = new[] { new WeaponMount { Weapon = Longsword() } }, Armor = new[] { Plate(), Coat(), Shirt() }, Equipment = Kit("板护臂", "板手套", "板护腿") }
        ),
        (
            new DuelFighter { Name = "双持枪手", DualWielding = true, Weapons = new[] { new WeaponMount { Weapon = Pistol() }, new WeaponMount { Weapon = Pistol() } }, Armor = new[] { Shirt() }, Equipment = Kit("布袖", "布手套", "布裤") },
            new DuelFighter { Name = "匕首刺客", Weapons = new[] { new WeaponMount { Weapon = Dagger() } }, Armor = Array.Empty<ArmorLayer>() }
        ),
    };

    public static void Run(string outPath)
    {
        const int runsPerMatchup = 3;
        var matchups = Matchups();

        var sb = new StringBuilder();
        sb.AppendLine("# Dead Signal 对决战报（1v1 到死）");
        sb.AppendLine();
        sb.AppendLine("日期：2026-07-05　每组固定种子跑 3 场可复现");
        sb.AppendLine();
        sb.AppendLine("> 武器攻速、流血/失血/攻速惩罚参数、装备名均**拟定待调**。远程武器**假设弹道命中**（几何误差角 miss 属实时层）。");
        sb.AppendLine("> 丧尸**穿着生前的破衣服**（每场按加权预设现抽：80% 至少还剩一件布衣/裤，头手脚恒裸），布衣叠在腐皮之外——故同一对局不同场次，丧尸的实际防护会不一样。");
        sb.AppendLine("> 效果实际生效：流血按 tick 扣储血量（不扣部位 HP），储血分级 <75%轻度/<50%中度/<25%重度昏迷/归零出血致死；震荡与手部骨折降攻速；切除/损毁移除部位（连带下游）；致死部位归零或出血致死判死。");
        sb.AppendLine();

        for (int mi = 0; mi < matchups.Length; mi++)
        {
            var (a, b) = matchups[mi];
            sb.AppendLine(CultureInfo.InvariantCulture, $"## 对局 {mi + 1}：{a.Name} vs {b.Name}");
            sb.AppendLine();

            for (int run = 0; run < runsPerMatchup; run++)
            {
                int seed = 424200 + mi * 100 + run;
                // 每场重建 fighter（Body 有状态），种子固定可复现
                var (fa, fb) = Matchups()[mi];
                var engine = new DuelEngine(new SystemRandomSource(seed));
                var result = engine.Run(fa, fb);

                sb.AppendLine(CultureInfo.InvariantCulture, $"### 第 {run + 1} 场（种子 {seed}）");
                sb.AppendLine();
                sb.AppendLine("```");
                foreach (var e in result.Events)
                {
                    sb.AppendLine(RenderLine(e));
                }

                sb.AppendLine(Outcome(result));
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"已写出 {outPath}（{matchups.Length} 组 × {runsPerMatchup} 场）。");
    }

    private static string RenderLine(DuelEvent e)
    {
        // 失血状态事件（Weapon 为空）：分级 / 昏迷 / 致死
        if (string.IsNullOrEmpty(e.Weapon))
        {
            var tag = e.Tags.FirstOrDefault(t => t.StartsWith("失血:", StringComparison.Ordinal)) ?? "失血:?:0";
            var f = tag.Split(':');
            string label = f.Length > 1 ? f[1] : "?";
            string pct = f.Length > 2 ? f[2] : "0";
            string body = label switch
            {
                "重度昏迷" => $"**因失血过多昏迷倒地**（储血 {pct}%）",
                "致死" => $"**因失血过多死亡**（储血 {pct}%）",
                _ => $"失血加重：{label}出血（储血 {pct}%）",
            };
            return string.Create(CultureInfo.InvariantCulture, $"[t={e.Time:F1}s] {e.Attacker} {body}");
        }

        // 闪避事件（整发躲开，无伤无效果）
        if (e.Tags.Contains("闪避"))
        {
            return string.Create(CultureInfo.InvariantCulture, $"[t={e.Time:F1}s] {e.Attacker} {e.Weapon}→{e.Defender}：**被闪避**（无伤）");
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[t={e.Time:F1}s] {e.Attacker} {e.Weapon}→{e.Defender}·{e.Part}：{e.PenetrationDesc}，伤害{e.Damage:0.#}");

        bool severed = e.Tags.Any(t => t.StartsWith("切除:", StringComparison.Ordinal));
        bool destroyed = e.Tags.Any(t => t.StartsWith("损毁:", StringComparison.Ordinal));
        if ((severed || destroyed) && e.Damage >= e.PartMaxHp && e.PartMaxHp > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"（≥部位满血{e.PartMaxHp:0.#}）");
        }

        var clauses = new List<string>();
        var linked = e.Tags.Where(t => t.StartsWith("连带:", StringComparison.Ordinal)).Select(t => t[3..]).ToList();
        var drops = e.Tags.Where(t => t.StartsWith("掉落:", StringComparison.Ordinal)).Select(t => t[3..]).ToList();

        foreach (var t in e.Tags)
        {
            if (t.StartsWith("切除:", StringComparison.Ordinal))
            {
                string c = $"**切除{t[3..]}**";
                if (linked.Count > 0) c += $"（{string.Join("、", linked)}连带失去）";
                clauses.Add(c);
            }
            else if (t.StartsWith("损毁:", StringComparison.Ordinal))
            {
                string c = $"**损毁{t[3..]}（碾碎）**";
                if (linked.Count > 0) c += $"（{string.Join("、", linked)}连带失去）";
                clauses.Add(c);
            }
            else if (t.StartsWith("磨损:", StringComparison.Ordinal))
            {
                var parts = t.Split(':');
                clauses.Add($"磨损{parts[1]}上限{parts[2]}");
            }
            else if (t.StartsWith("流血:", StringComparison.Ordinal)) clauses.Add($"{t[3..]}流血");
            else if (t.StartsWith("骨折:", StringComparison.Ordinal)) clauses.Add($"{t[3..]}骨折");
            else if (t.StartsWith("震荡:", StringComparison.Ordinal)) clauses.Add($"{t[3..]}震荡");
        }

        if (clauses.Count > 0)
        {
            sb.Append(" → ").Append(string.Join("、", clauses));
        }

        if (drops.Count > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"，装备掉落：{string.Join("、", drops)}");
        }

        if (e.Tags.Contains("死亡"))
        {
            sb.Append(" → **倒地身亡**");
        }

        return sb.ToString();
    }

    private static string Outcome(DuelResult r)
    {
        string reason = r.EndReason switch
        {
            DuelEndReason.VitalDown => "致命部位被摧毁",
            DuelEndReason.Bleedout => "失血过多",
            DuelEndReason.Stalemate => "双方均丧失战斗力（僵局）",
            _ => "超时未分胜负",
        };

        if (r.Winner is null)
        {
            return $"—— 结局：{reason}，无人生还/平局；耗时 {r.DurationSeconds:F1}s，共 {r.TotalActions} 次出手。";
        }

        return $"—— 结局：{r.Winner} 胜（{r.Loser} 因{reason}倒下）；耗时 {r.DurationSeconds:F1}s，共 {r.TotalActions} 次出手。";
    }
}
