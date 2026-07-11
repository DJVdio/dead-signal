using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 医疗面板（模态）：给幸存者做手术（流血/骨折）与用药（感染/疾病）。骨架照 <see cref="CraftingPanel"/>：
/// CanvasLayer + <see cref="UiStyle.BuildModalShell"/>，**只发事件、不直接执行**（执行 + 扣材料由营地接入做）。
///
/// 顶部选【病人】+【施术者】（施术者==病人=自体手术，池打折）+【床上】开关。列出病人 <see cref="Pawn.Health"/> 的每条伤病：
///   · 流血/骨折未手术：勾选适用手术耗材（库存 Medical 材料，急救包独占与散件互斥），点「手术」→ emit <see cref="SurgeryRequested"/>。
///   · 流血/骨折已手术：只显"恢复中"，不再给手术入口。
///   · 感染：点「用抗生素」→ emit <see cref="TreatRequested"/>（药按伤类定，抗生素治感染）。
///   · 疾病：点「用成药」→ emit <see cref="TreatRequested"/>（成药治疾病）。
///
/// **所有手术数值（点数/roll/效率）不对玩家展示**：伤情/结果只给模糊描述（轻微/较重/危急、恢复顺利/尚可/勉强）。
/// 事件传 <see cref="Pawn"/> 与 <see cref="HealthCondition"/> 引用（同程序集）；营地据此算书加点/操作能力/自体系数再调
/// <see cref="HealthConditionSet.PerformSurgery"/>/<see cref="HealthConditionSet.TreatIllness"/>，扣耗材、提示、刷新。
/// </summary>
public sealed partial class MedicalPanel : CanvasLayer
{
    /// <summary>点「手术」：emit (病人, 目标伤=流血/骨折, 投入耗材 key 列表, 是否床上, 施术者)。营地据此实做手术+扣材料。</summary>
    public event Action<Pawn, HealthCondition, IReadOnlyList<string>, bool, Pawn>? SurgeryRequested;

    /// <summary>点「用药」：emit (病人, 目标病状=感染/疾病, 施术者)。营地按伤类取药（抗生素/成药）+ 施术者医疗技能实做。</summary>
    public event Action<Pawn, HealthCondition, Pawn>? TreatRequested;

    /// <summary>点「关闭」：CampMain 据此隐藏面板并恢复时标。</summary>
    public event Action? Closed;

    // ---- ShowFor 传入的只读依赖 ----
    private IReadOnlyList<Pawn> _pawns = Array.Empty<Pawn>();
    private InventoryStore _inventory = new();

    // ---- 面板瞬态 ----
    private Pawn? _patient;
    private Pawn? _surgeon;
    private bool _onBed;
    // 每条伤当前勾选的手术耗材（按伤条目引用；ShowFor 重刷时清空——伤病集已变）。
    private readonly Dictionary<HealthCondition, HashSet<string>> _matSel = new();

    // ---- 控件引用 ----
    private Control _root = null!;
    private OptionButton _patientDropdown = null!;
    private OptionButton _surgeonDropdown = null!;
    private CheckBox _bedCheck = null!;
    private VBoxContainer _listContainer = null!;
    private Label _footerLabel = null!;

    // 配色（对齐 CraftingPanel 暗色调；语义红/绿/黄统一引 UiStyle，消除跨面板漂移）
    private static readonly Color TextColor = new(0.85f, 0.82f, 0.75f);
    private static readonly Color DimColor = new(0.55f, 0.55f, 0.5f);
    private static readonly Color HeadColor = new(0.72f, 0.68f, 0.55f);
    private static readonly Color OkColor = UiStyle.Success;
    private static readonly Color BadColor = UiStyle.Danger;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "MedicalPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(680, 600),
            borderColor: new Color(0.24f, 0.28f, 0.24f));

        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        var title = new Label();
        title.Text = "医务";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        // 顶部一行：病人 + 施术者 下拉，床上开关
        var patientCaption = MakeCaption("病人", new Vector2(24, 56));
        panel.AddChild(patientCaption);

        _patientDropdown = MakeDropdown(new Vector2(72, 52), new Vector2(200, 30));
        _patientDropdown.ItemSelected += OnPatientSelected;
        panel.AddChild(_patientDropdown);

        var surgeonCaption = MakeCaption("施术者", new Vector2(288, 56));
        panel.AddChild(surgeonCaption);

        _surgeonDropdown = MakeDropdown(new Vector2(348, 52), new Vector2(180, 30));
        _surgeonDropdown.ItemSelected += OnSurgeonSelected;
        panel.AddChild(_surgeonDropdown);

        _bedCheck = new CheckBox();
        _bedCheck.Text = "床上";
        _bedCheck.Position = new Vector2(548, 52);
        _bedCheck.AddThemeColorOverride("font_color", TextColor);
        _bedCheck.Toggled += on => { _onBed = on; Rebuild(); };
        panel.AddChild(_bedCheck);

        // 列表区（滚动）
        var listBg = new Panel();
        listBg.Position = new Vector2(24, 96);
        listBg.Size = new Vector2(632, 428);
        var listStyle = new StyleBoxFlat();
        listStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        listStyle.CornerRadiusTopLeft = 4;
        listStyle.CornerRadiusTopRight = 4;
        listStyle.CornerRadiusBottomLeft = 4;
        listStyle.CornerRadiusBottomRight = 4;
        listBg.AddThemeStyleboxOverride("panel", listStyle);
        panel.AddChild(listBg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        listBg.AddChild(scroll);

        _listContainer = new VBoxContainer();
        _listContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _listContainer.MouseFilter = Control.MouseFilterEnum.Pass;
        _listContainer.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_listContainer);

        _footerLabel = new Label();
        _footerLabel.Position = new Vector2(24, 534);
        _footerLabel.Size = new Vector2(480, 40);
        _footerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _footerLabel.AddThemeFontSizeOverride("font_size", 12);
        _footerLabel.AddThemeColorOverride("font_color", DimColor);
        panel.AddChild(_footerLabel);

        var closeBtn = new Button();
        closeBtn.Text = "关闭";
        closeBtn.Position = new Vector2(516, 544);
        closeBtn.Size = new Vector2(140, 40);
        closeBtn.Pressed += () => Closed?.Invoke();
        UiStyle.StyleButton(closeBtn, new Color(0.5f, 0.4f, 0.3f), fontSize: 16);
        panel.AddChild(closeBtn);
    }

    private static Label MakeCaption(string text, Vector2 pos)
    {
        var l = new Label();
        l.Text = text;
        l.Position = pos;
        l.AddThemeFontSizeOverride("font_size", 14);
        l.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.5f));
        return l;
    }

    private static OptionButton MakeDropdown(Vector2 pos, Vector2 size)
    {
        var d = new OptionButton();
        d.Position = pos;
        d.Size = size;
        d.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        d.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
        return d;
    }

    /// <summary>
    /// 打开/刷新面板：传入病人候选（存活幸存者）与营地共享库存。手术/用药后由营地再调一次以反映扣掉的耗材与更新后的伤病集。
    /// </summary>
    public void ShowFor(IReadOnlyList<Pawn> pawns, InventoryStore inventory)
    {
        _pawns = pawns ?? Array.Empty<Pawn>();
        _inventory = inventory ?? new InventoryStore();
        _matSel.Clear(); // 伤病集已变，清掉旧勾选

        if (_patient is null || !_pawns.Contains(_patient))
        {
            _patient = _pawns.FirstOrDefault();
        }
        if (_surgeon is null || !_pawns.Contains(_surgeon))
        {
            // 施术者默认取非病人的第一个健在者（避免开面板即默认自体手术的打折态）；仅剩病人一人时才回落自体。
            _surgeon = _pawns.FirstOrDefault(p => !ReferenceEquals(p, _patient)) ?? _patient;
        }

        PopulateDropdown(_patientDropdown, _patient);
        PopulateDropdown(_surgeonDropdown, _surgeon);
        _bedCheck.ButtonPressed = _onBed;
        Rebuild();
    }

    private void PopulateDropdown(OptionButton dropdown, Pawn? selected)
    {
        dropdown.Clear();
        for (int i = 0; i < _pawns.Count; i++)
        {
            dropdown.AddItem(_pawns[i].DisplayName, _pawns[i].Id);
        }
        if (selected is not null)
        {
            int idx = dropdown.GetItemIndex(selected.Id);
            if (idx >= 0) dropdown.Selected = idx;
        }
        dropdown.Disabled = _pawns.Count == 0;
    }

    private void OnPatientSelected(long index)
    {
        int id = _patientDropdown.GetItemId((int)index);
        _patient = _pawns.FirstOrDefault(p => p.Id == id);
        _matSel.Clear();
        Rebuild();
    }

    private void OnSurgeonSelected(long index)
    {
        int id = _surgeonDropdown.GetItemId((int)index);
        _surgeon = _pawns.FirstOrDefault(p => p.Id == id);
        Rebuild();
    }

    private void Rebuild()
    {
        UiStyle.ClearChildren(_listContainer);

        if (_patient is null || _surgeon is null)
        {
            _footerLabel.Text = "没有可医治的幸存者。";
            AddEmpty("无幸存者。");
            return;
        }

        bool self = ReferenceEquals(_patient, _surgeon);
        _footerLabel.Text = self
            ? "自体手术：无人搭手，成功率打折。手术数值不外显，只给恢复描述。"
            : "选耗材做手术、或对感染/疾病用药。手术数值不外显，只给恢复描述。";

        var conditions = _patient.Health.Conditions;
        if (conditions.Count == 0)
        {
            AddEmpty($"{_patient.DisplayName} 目前无伤病。");
            return;
        }

        foreach (HealthCondition c in conditions.ToList())
        {
            _listContainer.AddChild(BuildConditionCard(c));
        }
    }

    private Control BuildConditionCard(HealthCondition c)
    {
        var card = new VBoxContainer();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        card.MouseFilter = Control.MouseFilterEnum.Pass;
        card.AddThemeConstantOverride("separation", 3);

        // 首行：伤情描述（模糊）；已处置=绿，未处置按严重度着色（危急=红、较重=黄、轻微=常色）
        var head = new Label();
        head.Text = DescribeCondition(c);
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", ConditionHeadColor(c));
        card.AddChild(head);

        switch (c.Type)
        {
            case HealthConditionType.Bleeding:
            case HealthConditionType.Fracture:
                if (c.IsOperated)
                {
                    (string qualityText, Color qualityColor) = RecoveryQuality(c.RecoveryEfficiency);
                    var done = new Label();
                    done.Text = $"    已处置 · {qualityText} · 已恢复 {c.DaysSinceLastSurgery} 昼夜";
                    done.AddThemeFontSizeOverride("font_size", 12);
                    done.AddThemeColorOverride("font_color", qualityColor);
                    card.AddChild(done);

                    // 隔日可重做：距上次手术 > 冷却 → 再次手术入口（重做重 roll、覆盖当前恢复，双向风险）。
                    if (c.DaysSinceLastSurgery > HealthConditionSet.RedoSurgeryCooldownDays)
                    {
                        var redoHint = new Label();
                        redoHint.Text = "    可再次手术（重做会覆盖当前恢复，可能更好也可能更差）";
                        redoHint.AddThemeFontSizeOverride("font_size", 12);
                        redoHint.AddThemeColorOverride("font_color", DimColor);
                        card.AddChild(redoHint);
                        BuildSurgerySection(card, c); // 复用手术区（材料+按钮），走同一 SurgeryRequested 链路；PerformSurgery 内部按重做处理
                    }
                }
                else
                {
                    BuildSurgerySection(card, c);
                }
                break;

            case HealthConditionType.Infection:
                BuildTreatSection(card, c, "antibiotics", "用抗生素", "抗生素");
                break;

            case HealthConditionType.Disease:
                BuildTreatSection(card, c, "medicine", "用成药", "成药");
                break;
        }

        var sep = new HSeparator();
        sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
        card.AddChild(sep);
        return card;
    }

    // ---- 手术区（流血/骨折）：耗材勾选（急救包独占互斥）+ 手术按钮 ----
    private void BuildSurgerySection(VBoxContainer card, HealthCondition c)
    {
        HashSet<string> sel = _matSel.TryGetValue(c, out HashSet<string>? s) ? s : (_matSel[c] = new HashSet<string>());

        // 适用耗材：Medical 材料中，SurgeryCatalog 里能治该伤类的键。
        List<string> supplyKeys = Materials.InCategory(MaterialCategory.Medical)
            .Select(m => m.Key)
            .Where(k => SurgeryCatalog.For(k) is { } sup && sup.CanTreat(c.Type))
            .ToList();

        var matRow = new HBoxContainer();
        matRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        matRow.MouseFilter = Control.MouseFilterEnum.Pass;
        matRow.AddThemeConstantOverride("separation", 12);

        var checks = new List<(string key, CheckBox box)>();
        Label? hint = null;

        // 勾选后只就地重算这张卡的互斥置灰与耗材提示，不整列 Rebuild（保滚动位与其它卡状态）。
        void RefreshMatStates()
        {
            bool exclusiveSelected = sel.Any(k => SurgeryCatalog.For(k) is { Exclusive: true });
            bool nonExclusiveSelected = sel.Any(k => SurgeryCatalog.For(k) is { Exclusive: false });
            foreach ((string key, CheckBox box) in checks)
            {
                bool selected = sel.Contains(key);
                SurgerySupply sup = SurgeryCatalog.For(key)!.Value;
                int have = CraftingPanelFormat.MaterialCount(_inventory, key);
                bool exclusivityBlocked = !selected && (sup.Exclusive ? nonExclusiveSelected : exclusiveSelected);
                bool outOfStock = have <= 0 && !selected;
                box.Disabled = exclusivityBlocked || outOfStock;
                box.AddThemeColorOverride("font_color", box.Disabled ? DimColor : TextColor);
            }
            if (hint is not null)
            {
                hint.Text = sel.Count == 0 ? "徒手（无耗材，成功率低）" : "耗材：" + string.Join("、",
                    sel.Select(k => Materials.Find(k)?.DisplayName ?? k));
                hint.AddThemeColorOverride("font_color", sel.Count == 0 ? DimColor : OkColor);
            }
        }

        foreach (string key in supplyKeys)
        {
            int have = CraftingPanelFormat.MaterialCount(_inventory, key);
            bool selected = sel.Contains(key);

            var check = new CheckBox();
            string name = Materials.Find(key)?.DisplayName ?? key;
            check.Text = have > 0 ? $"{name}（{have}）" : name;
            check.ButtonPressed = selected;
            string capturedKey = key;
            check.Toggled += on =>
            {
                if (on) sel.Add(capturedKey); else sel.Remove(capturedKey);
                RefreshMatStates(); // 局部重算独占互斥置灰，不整列重建
            };
            checks.Add((key, check));
            matRow.AddChild(check);
        }
        if (supplyKeys.Count == 0)
        {
            var none = new Label();
            none.Text = "（无适用耗材）";
            none.AddThemeFontSizeOverride("font_size", 12);
            none.AddThemeColorOverride("font_color", DimColor);
            matRow.AddChild(none);
        }
        card.AddChild(matRow);

        var opRow = new HBoxContainer();
        opRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        opRow.MouseFilter = Control.MouseFilterEnum.Pass;
        opRow.AddThemeConstantOverride("separation", 8);

        hint = new Label();
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 12);
        opRow.AddChild(hint);
        RefreshMatStates(); // 初始态：置灰与提示（含首次进入时的独占/缺货判定）

        var opBtn = new Button();
        opBtn.Text = "手术";
        opBtn.CustomMinimumSize = new Vector2(120, 30);
        HealthCondition captured = c;
        List<string> mats = sel.ToList();
        opBtn.Pressed += () =>
        {
            if (_patient is not null && _surgeon is not null)
                SurgeryRequested?.Invoke(_patient, captured, mats, _onBed, _surgeon);
        };
        UiStyle.StyleButton(opBtn, new Color(0.4f, 0.5f, 0.4f), fontSize: 13);
        opRow.AddChild(opBtn);
        card.AddChild(opRow);
    }

    // ---- 用药区（感染/疾病）：一个"用药"按钮，药按伤类定，库存不足则灰 ----
    private void BuildTreatSection(VBoxContainer card, HealthCondition c, string medicineKey, string btnText, string medName)
    {
        int have = CraftingPanelFormat.MaterialCount(_inventory, medicineKey);

        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddThemeConstantOverride("separation", 8);

        var hint = new Label();
        hint.Text = have > 0 ? $"{medName}（{have}）" : $"缺{medName}";
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", have > 0 ? OkColor : BadColor);
        row.AddChild(hint);

        var btn = new Button();
        btn.Text = btnText;
        btn.CustomMinimumSize = new Vector2(120, 30);
        btn.Disabled = have <= 0;
        HealthCondition captured = c;
        btn.Pressed += () =>
        {
            if (_patient is not null && _surgeon is not null)
                TreatRequested?.Invoke(_patient, captured, _surgeon);
        };
        UiStyle.StyleButton(btn, new Color(0.4f, 0.5f, 0.4f), fontSize: 13);
        row.AddChild(btn);
        card.AddChild(row);
    }

    private static string DescribeCondition(HealthCondition c)
    {
        string part = c.BodyPart ?? "全身";
        string sev = SeverityWord(c.Severity);
        return c.Type switch
        {
            HealthConditionType.Bleeding => c.IsOperated ? $"{part}：伤口已缝合，恢复中" : $"{part}：流血不止（{sev}）",
            HealthConditionType.Fracture => c.IsOperated ? $"{part}：已固定，恢复中" : $"{part}：骨折（{sev}）",
            HealthConditionType.Infection => $"{part}：伤口感染（{sev}）",
            HealthConditionType.Disease => $"病症（{sev}）",
            _ => part,
        };
    }

    /// <summary>严重度 → 模糊描述（不显数值）。</summary>
    private static string SeverityWord(double s) => s < 0.34 ? "轻微" : s < 0.67 ? "较重" : "危急";

    /// <summary>恢复效率 → 模糊质量描述 + 色（**不显数值**，守"医疗点/效率不外显"红线）：供玩家判断是否值得重做。draft 分档。</summary>
    private static (string text, Color color) RecoveryQuality(int efficiency)
        => efficiency >= 50 ? ("恢复良好", OkColor)
         : efficiency >= 25 ? ("恢复平稳", TextColor)
         : ("恢复缓慢", UiStyle.Warning);

    /// <summary>伤情首行色：已处置=绿（恢复中），未处置按严重度——危急=红、较重=黄、轻微=常色。与 SeverityWord 阈值一致。</summary>
    private static Color ConditionHeadColor(HealthCondition c)
    {
        if (c.IsOperated) return OkColor;
        return c.Severity >= 0.67 ? UiStyle.Danger : c.Severity >= 0.34 ? UiStyle.Warning : TextColor;
    }

    private void AddEmpty(string text)
    {
        var empty = new Label();
        empty.Text = text;
        empty.AddThemeFontSizeOverride("font_size", 14);
        empty.AddThemeColorOverride("font_color", DimColor);
        _listContainer.AddChild(empty);
    }
}
