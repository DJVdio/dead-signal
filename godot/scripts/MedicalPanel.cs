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

    /// <summary>点「用药」：emit (病人, 目标病状=疾病, 所选药 key, 施术者)。**仅疾病走单发用药**（成药）；感染改疗程指派（见 <see cref="TreatmentAssigned"/>）。</summary>
    public event Action<Pawn, HealthCondition, string, Pawn>? TreatRequested;

    /// <summary>[SPEC-B14-补3] 指派感染疗程：emit (病人, 所选药 key)。营地记指派，每昼夜黎明自动扣药累进治疗进度，直到治愈/断药/撤销。</summary>
    public event Action<Pawn, string>? TreatmentAssigned;

    /// <summary>[SPEC-B14-补3] 停止感染疗程：emit (病人)。营地清指派、当昼夜起不再自动用药。</summary>
    public event Action<Pawn>? TreatmentCancelled;

    /// <summary>[SPEC-B14-补2] 给病人喝玫瑰果茶：emit (病人)。营地扣 1 份玫瑰果茶、激活其 24 小时 +9pp 恢复加成。</summary>
    public event Action<Pawn>? RosehipTeaRequested;

    /// <summary>[SPEC-B14-补7] 主动截肢感染的肢体（玩家抉择的保命手术）：emit (病人, 目标感染, 止血耗材, 是否床上, 施术者)。营地走 PerformAmputation 判成败+扣材料+断肢。</summary>
    public event Action<Pawn, HealthCondition, IReadOnlyList<string>, bool, Pawn>? AmputationRequested;

    /// <summary>[SPEC-B14-补8] 给缺失的肢体安装假肢（也是手术）：emit (病人, 取代区域 手/腿, 假肢等级, 止血耗材, 是否床上, 施术者)。营地走 PerformProstheticSurgery 判成败，成功才装上。</summary>
    public event Action<Pawn, BodyRegion, ProstheticGrade, IReadOnlyList<string>, bool, Pawn>? ProstheticSurgeryRequested;

    /// <summary>点「关闭」：CampMain 据此隐藏面板并恢复时标。</summary>
    public event Action? Closed;

    // ---- ShowFor 传入的只读依赖 ----
    private IReadOnlyList<Pawn> _pawns = Array.Empty<Pawn>();
    private InventoryStore _inventory = new();

    // ---- 面板瞬态 ----
    private Pawn? _patient;
    private Pawn? _surgeon;
    private bool _onBed;

    /// <summary>[批次21·impl-bedrest] 查"这人是否真躺在床上"（营地注入 <see cref="BedRegistry.HasBed"/>）；null=无营地上下文，回落手勾开关。</summary>
    private Func<Pawn, bool>? _hasBed;

    // [批次21·impl-medicine] 上一次 ShowFor 收到的预选对象：用来分辨"这是一条新的下令"还是"只是又刷新了一次面板"。
    // 只有新下令才动病人/施术者，否则玩家在下拉里的手动选择会被每次刷新吞掉（见 ShowFor 里的注释）。
    private Pawn? _lastFocus;
    private Pawn? _lastMedic;
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
    /// <param name="hasBed">
    /// [批次21·impl-bedrest] 该幸存者当前是否**真的躺在床上**（营地传 <see cref="BedRegistry.HasBed"/>）。
    /// 「床上」从此不再是玩家随手勾的开关 —— 床是营地里实打实的家具，得先让他走过去躺下（右键点床）。
    /// 传 null 时回落成旧的手勾开关（供无营地上下文的调用点/测试）。
    /// </param>
    /// <param name="focus">
    /// [批次21·impl-bedrest] 直接以此人为病人打开（营地传**当前选中的角色**）：这就是"选中角色 → 给他吃药/用医疗物资"
    /// 那条路 —— 玩家选好人再按 M，面板就该已经翻到他那一页，而不是回到名单第一个再让他自己找。
    /// null=沿用旧的默认选人。
    /// </param>
    /// <param name="medic">
    /// [批次21·impl-medicine] 直接以此人为**施术者**打开（营地在"右键点伤员下医务令"时传**当前选中的角色**）：
    /// 玩家选中 A、右键点 B，意思就是"让 A 去治 B"——面板不该再把施术者甩回名单第一个人。
    /// 与 <paramref name="focus"/> 同为病人的（=点自己）时忽略，让下面的默认逻辑挑个别人来动刀（自体手术池要打 0.6 折）。
    /// null=沿用旧的默认选人。
    /// </param>
    public void ShowFor(IReadOnlyList<Pawn> pawns, InventoryStore inventory, Func<Pawn, bool>? hasBed = null,
        Pawn? focus = null, Pawn? medic = null)
    {
        _pawns = pawns ?? Array.Empty<Pawn>();
        _inventory = inventory ?? new InventoryStore();
        _hasBed = hasBed;
        _matSel.Clear(); // 伤病集已变，清掉旧勾选

        // [批次21·impl-medicine] 预选只在**换了一个新的下令对象**时生效，重复同一个 focus 不再覆盖。
        // 为什么必须这样：营地在每次手术/用药之后都会再调一遍 ShowFor 刷新（RefreshMedical），
        // 若每次都无条件把病人拽回 focus，玩家在下拉里手动切到别人身上的选择就会被**当场吞掉**——
        // 他刚点开"给老李看看"，一按手术，面板又跳回原来那个人。预选是"帮你翻到那一页"，不是"锁死这一页"。
        bool newFocus = focus is not null && !ReferenceEquals(focus, _lastFocus);
        bool newMedic = medic is not null && !ReferenceEquals(medic, _lastMedic);
        _lastFocus = focus;
        _lastMedic = medic;

        if (newFocus && _pawns.Contains(focus!))
        {
            _patient = focus;
        }
        if (_patient is null || !_pawns.Contains(_patient))
        {
            _patient = _pawns.FirstOrDefault();
        }
        if (newMedic && _pawns.Contains(medic!) && !ReferenceEquals(medic, _patient))
        {
            _surgeon = medic;
        }
        if (_surgeon is null || !_pawns.Contains(_surgeon))
        {
            // 施术者默认取非病人的第一个健在者（避免开面板即默认自体手术的打折态）；仅剩病人一人时才回落自体。
            _surgeon = _pawns.FirstOrDefault(p => !ReferenceEquals(p, _patient)) ?? _patient;
        }

        PopulateDropdown(_patientDropdown, _patient);
        PopulateDropdown(_surgeonDropdown, _surgeon);
        SyncBedCheck();
        Rebuild();
    }

    /// <summary>
    /// [批次21·impl-medicine] 关面板时清掉"上次的下令对象"记忆（营地在 <c>CloseMedical</c> 里调）。
    /// 不清的话会出这种事：玩家开面板给甲下令、在下拉里改成乙、关掉，再右键甲下一次令——面板认得这还是"上次那个甲"，
    /// 于是不翻页，他看到的还是乙。**关一次面板 = 一次下令结束**，下次右键谁就该看见谁。
    /// </summary>
    public void ResetPreselect()
    {
        _lastFocus = null;
        _lastMedic = null;
    }

    /// <summary>
    /// 把「床上」同步成病人的**真实床位状态**：躺着就是躺着，没躺就是没躺，玩家勾不出来。
    /// 没床可躺时把原因写在提示里 —— 照 SiteActionOption 的规矩：不藏选项，灰掉并说明为什么。
    /// </summary>
    private void SyncBedCheck()
    {
        if (_hasBed is null)
        {
            _bedCheck.Disabled = false;
            _bedCheck.ButtonPressed = _onBed;
            _bedCheck.TooltipText = "";
            return;
        }

        _onBed = _patient is not null && _hasBed(_patient);
        _bedCheck.Disabled = true; // 由床位实况驱动，不给手勾
        _bedCheck.ButtonPressed = _onBed;
        _bedCheck.TooltipText = _onBed
            ? "病人正躺在床上——手术更稳。"
            : "病人没躺在床上。先选中他、右键点一张床让他躺下，手术会更稳。";
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
        SyncBedCheck(); // 换了病人 → 「床上」跟着他的实况走（这个躺着、那个没躺）
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

        // [SPEC-B14-补2] 玫瑰果茶恢复加成（病人级动作，不挂具体伤条）：喝下 24 小时恢复 +9%。
        AddRosehipTeaRow();

        // [SPEC-B14-补8] 安装假肢也是手术：为任何原因缺失的肢体（战斗切除/截肢）列"安装假肢"手术入口。
        AddProstheticInstallSection();

        var conditions = _patient.Health.Conditions;
        if (conditions.Count == 0)
        {
            if (_patient.Inspect().ProstheticSlots.Any(s => s.CanEquip))
                return; // 无伤病但有可装假肢的缺肢：已在上方列出手术入口，不再报"无伤病"
            AddEmpty($"{_patient.DisplayName} 目前无伤病。");
            return;
        }

        foreach (HealthCondition c in conditions.ToList())
        {
            _listContainer.AddChild(BuildConditionCard(c));
        }
    }

    // [SPEC-B14-补8] 假肢等级选项（沿现有 Grade 体系：木制/简易/仿生）。
    private static readonly (ProstheticGrade Grade, string Label)[] ProstheticGrades =
    {
        (ProstheticGrade.Wooden, "木制"), (ProstheticGrade.Simple, "简易"), (ProstheticGrade.Bionic, "仿生"),
    };

    /// <summary>[SPEC-B14-补8] 缺肢的"安装假肢"手术入口：每个空槽一行——部位 + 三档假肢按钮（点击=走手术判成败，成功才装上）。</summary>
    private void AddProstheticInstallSection()
    {
        if (_patient is null)
            return;
        var slots = _patient.Inspect().ProstheticSlots.Where(s => s.CanEquip).ToList();
        if (slots.Count == 0)
            return;

        var head = new Label();
        head.Text = "缺失的肢体（安装假肢也是手术，可能失败）";
        head.AddThemeFontSizeOverride("font_size", 13);
        head.AddThemeColorOverride("font_color", HeadColor);
        _listContainer.AddChild(head);

        foreach (ProstheticSlot slot in slots)
        {
            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.MouseFilter = Control.MouseFilterEnum.Pass;
            row.AddThemeConstantOverride("separation", 8);

            var name = new Label();
            name.Text = $"    {slot.UnitPartName}：";
            name.AddThemeFontSizeOverride("font_size", 12);
            name.AddThemeColorOverride("font_color", TextColor);
            row.AddChild(name);

            foreach ((ProstheticGrade grade, string label) in ProstheticGrades)
            {
                var btn = new Button();
                btn.Text = $"装{label}";
                btn.CustomMinimumSize = new Vector2(84, 28);
                BodyRegion region = slot.ReplacesRegion;
                ProstheticGrade capturedGrade = grade;
                btn.Pressed += () =>
                {
                    if (_patient is not null && _surgeon is not null)
                        ProstheticSurgeryRequested?.Invoke(_patient, region, capturedGrade, Array.Empty<string>(), _onBed, _surgeon);
                };
                UiStyle.StyleButton(btn, new Color(0.4f, 0.5f, 0.4f), fontSize: 12);
                row.AddChild(btn);
            }
            _listContainer.AddChild(row);
        }

        var sep = new HSeparator();
        sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
        _listContainer.AddChild(sep);
    }

    /// <summary>
    /// [批次21·impl-medicine] 「现在能不能给这位病人用这件医疗物资」——**唯一判定入口**。
    /// 从前这条规则散在各按钮的 <c>Disabled</c> 表达式里（缺货？疗程中？没对症的伤？各写各的），
    /// 现在统一走纯逻辑 <see cref="MedicalOrderLogic.Evaluate"/>（有单测），面板只负责把拒绝理由说给玩家听。
    /// </summary>
    private MedicalUseOption Evaluate(string materialKey)
    {
        Pawn p = _patient!;
        return MedicalOrderLogic.Evaluate(
            materialKey, p.Health,
            CraftingPanelFormat.MaterialCount(_inventory, materialKey),
            p.HasRosehipTeaHealBuff, p.InfectionTreatmentMedKey, p.Alive);
    }

    /// <summary>不可用时给按钮挂上"为什么不能用"（照 SiteActionPopup 的规矩：**不藏选项，灰掉并说明原因**）。</summary>
    private static void ApplyUsability(Button btn, MedicalUseOption option)
    {
        btn.Disabled = !option.Usable;
        btn.TooltipText = option.Usable ? "" : DisplayNames.Of(option.Refusal);
    }

    /// <summary>玫瑰果茶病人级动作行：显示库存与当前 buff 态；有茶、未在 buff 中、且身上有养得好的伤才可喝（没伤喝了是白扔一份）。</summary>
    private void AddRosehipTeaRow()
    {
        if (_patient is null)
            return;
        MedicalUseOption tonic = Evaluate(MedicalOrderLogic.RecoveryTonicKey);
        int have = tonic.Stock;
        bool active = _patient.HasRosehipTeaHealBuff;

        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddThemeConstantOverride("separation", 8);

        var hint = new Label();
        hint.Text = active ? "玫瑰果茶恢复加成生效中（+9% · 24 小时）" : $"玫瑰果茶（恢复+9% · 24 小时）（{have}）";
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", active ? OkColor : have > 0 ? TextColor : DimColor);
        row.AddChild(hint);

        var btn = new Button();
        btn.Text = "喝玫瑰果茶";
        btn.CustomMinimumSize = new Vector2(120, 28);
        ApplyUsability(btn, tonic);
        btn.Pressed += () => { if (_patient is not null) RosehipTeaRequested?.Invoke(_patient); };
        UiStyle.StyleButton(btn, new Color(0.45f, 0.45f, 0.5f), fontSize: 12);
        row.AddChild(btn);
        _listContainer.AddChild(row);

        var sep = new HSeparator();
        sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
        _listContainer.AddChild(sep);
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
                // [SPEC-B14-补3] 感染=双进度竞速 + 疗程指派：显示两条精确进度，选一档药指派疗程（每日自动用药）。
                BuildInfectionSection(card, c);
                break;

            case HealthConditionType.Disease:
                BuildTreatSection(card, c, "medicine", "用成药");
                break;
        }

        var sep = new HSeparator();
        sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
        card.AddChild(sep);
        return card;
    }

    // ---- 手术区（流血/骨折；截肢复用，见 supplyType/buttonText/onConfirm）：耗材勾选（急救包独占互斥）+ 操作按钮 ----
    // supplyType=按哪种伤类列耗材（默认目标伤本身；截肢用 Bleeding 关合残端）；buttonText/onConfirm 覆盖默认"手术"/SurgeryRequested。
    private void BuildSurgerySection(VBoxContainer card, HealthCondition c,
        HealthConditionType? supplyType = null, string buttonText = "手术", Action<List<string>>? onConfirm = null)
    {
        HealthConditionType stype = supplyType ?? c.Type;
        HashSet<string> sel = _matSel.TryGetValue(c, out HashSet<string>? s) ? s : (_matSel[c] = new HashSet<string>());

        // 适用耗材：Medical 材料中，SurgeryCatalog 里能治该伤类的键。
        List<string> supplyKeys = Materials.InCategory(MaterialCategory.Medical)
            .Select(m => m.Key)
            .Where(k => SurgeryCatalog.For(k) is { } sup && sup.CanTreat(stype))
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
            // 耗材图标挂在勾选框与文字之间（Button.Icon）。TextureFilter 走 Nearest，
            // 否则 Godot 会把这张 32×32 的硬边图标线性插值成一团糊。
            check.Icon = ItemIconTextures.ForRefKey(key);
            check.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
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
        opBtn.Text = buttonText;
        opBtn.CustomMinimumSize = new Vector2(160, 30);
        HealthCondition captured = c;
        List<string> mats = sel.ToList();
        opBtn.Pressed += () =>
        {
            if (_patient is null || _surgeon is null)
                return;
            if (onConfirm is not null)
                onConfirm(mats);
            else
                SurgeryRequested?.Invoke(_patient, captured, mats, _onBed, _surgeon);
        };
        UiStyle.StyleButton(opBtn, new Color(0.4f, 0.5f, 0.4f), fontSize: 13);
        opRow.AddChild(opBtn);
        card.AddChild(opRow);
    }

    // ---- 感染区（双进度竞速 + 疗程指派）：两条精确进度 + 三档药指派（每日自动用药）+ 停止疗程 ----
    private static readonly (string Key, string Name)[] InfectionRemedies =
    {
        ("antibiotics", "抗生素"), ("herbal_salve", "草药膏"), ("dandelion_tea", "蒲公英茶"),
    };

    private void BuildInfectionSection(VBoxContainer card, HealthCondition c)
    {
        // 两条精确进度：感染进度（死亡赛道，按分档着色）/ 治疗进度（治愈赛道）。玩家核心决策信息，精确显示。
        int infPct = (int)Math.Round(c.Severity * 100.0);
        int curePct = (int)Math.Round(c.CureProgress * 100.0);
        Color infColor = c.Severity >= 0.67 ? UiStyle.Danger : c.Severity >= 0.34 ? UiStyle.Warning : TextColor;

        var infBar = new Label();
        infBar.Text = $"    感染进度 {infPct}%（{HealthConditionSet.InfectionStageWord(c.Severity)}） · 到 100% 坏疽/败血症";
        infBar.AddThemeFontSizeOverride("font_size", 12);
        infBar.AddThemeColorOverride("font_color", infColor);
        card.AddChild(infBar);

        var cureBar = new Label();
        cureBar.Text = $"    治疗进度 {curePct}% · 到 100% 清除感染（用药同时减缓感染恶化，两条赛跑先到顶者胜）";
        cureBar.AddThemeFontSizeOverride("font_size", 12);
        cureBar.AddThemeColorOverride("font_color", curePct > 0 ? OkColor : DimColor);
        card.AddChild(cureBar);

        string? course = _patient?.InfectionTreatmentMedKey;
        if (course is not null)
        {
            var status = new Label();
            status.Text = $"    疗程中：{Materials.Find(course)?.DisplayName ?? course}（每日黎明自动用药，缺药即中断）";
            status.AddThemeFontSizeOverride("font_size", 12);
            status.AddThemeColorOverride("font_color", OkColor);
            card.AddChild(status);
        }

        // 指派/换用行：三档药各一按钮（按库存与效率标注，缺货灰）；当前档标注"换用"。
        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddThemeConstantOverride("separation", 8);
        foreach ((string key, string name) in InfectionRemedies)
        {
            MedicalUseOption opt = Evaluate(key);
            bool isCurrent = course == key;
            var btn = new Button();
            btn.Text = $"{(isCurrent ? "疗程中·" : "指派")}{name}{EfficacyTag(key)}（{opt.Stock}）";
            btn.CustomMinimumSize = new Vector2(180, 30);
            ApplyUsability(btn, opt);
            string capturedKey = key;
            btn.Pressed += () => { if (_patient is not null) TreatmentAssigned?.Invoke(_patient, capturedKey); };
            UiStyle.StyleButton(btn, new Color(0.4f, 0.5f, 0.4f), fontSize: 12);
            row.AddChild(btn);
        }
        card.AddChild(row);

        if (course is not null)
        {
            var stop = new Button();
            stop.Text = "停止疗程";
            stop.CustomMinimumSize = new Vector2(120, 28);
            stop.Pressed += () => { if (_patient is not null) TreatmentCancelled?.Invoke(_patient); };
            UiStyle.StyleButton(stop, new Color(0.5f, 0.4f, 0.3f), fontSize: 12);
            card.AddChild(stop);
        }

        // [SPEC-B14-补7] 感染的肢体：主动截肢=最后的保命手术（玩家抉择，系统不自动、不建议）。走止血耗材关合残端、既有手术成败流程。
        if (c.OnLimb)
        {
            var ampHint = new Label();
            ampHint.Text = $"    截肢（保命手段·可能失败）：切除{c.BodyPart ?? "该肢"}以中止感染，残端仍需善后。";
            ampHint.AddThemeFontSizeOverride("font_size", 12);
            ampHint.AddThemeColorOverride("font_color", UiStyle.Warning);
            card.AddChild(ampHint);
            HealthCondition infCap = c;
            BuildSurgerySection(card, c, HealthConditionType.Bleeding, "截肢",
                mats => { if (_patient is not null && _surgeon is not null) AmputationRequested?.Invoke(_patient, infCap, mats, _onBed, _surgeon); });
        }
    }

    // ---- 用药区（仅疾病单发）：一档药一行——名+库存，缺货则灰 ----
    private void BuildTreatSection(VBoxContainer card, HealthCondition c, string medicineKey, string btnText)
    {
        MedicalUseOption opt = Evaluate(medicineKey);
        int have = opt.Stock;
        string medName = Materials.Find(medicineKey)?.DisplayName ?? medicineKey;

        var row = new HBoxContainer();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddThemeConstantOverride("separation", 8);

        var hint = new Label();
        hint.Text = $"{medName}{EfficacyTag(medicineKey)}" + (have > 0 ? $"（{have}）" : "（缺）");
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", have > 0 ? OkColor : BadColor);
        row.AddChild(hint);

        var btn = new Button();
        btn.Text = btnText;
        btn.CustomMinimumSize = new Vector2(120, 30);
        ApplyUsability(btn, opt);
        HealthCondition captured = c;
        string capturedKey = medicineKey;
        btn.Pressed += () =>
        {
            if (_patient is not null && _surgeon is not null)
                TreatRequested?.Invoke(_patient, captured, capturedKey, _surgeon);
        };
        UiStyle.StyleButton(btn, new Color(0.4f, 0.5f, 0.4f), fontSize: 13);
        row.AddChild(btn);
        card.AddChild(row);
    }

    /// <summary>药品治疗效率标注（如 " · 效率45%"），单一事实源取自 <see cref="MedicineCatalog"/>；满效(100%)不啰嗦标注。</summary>
    private static string EfficacyTag(string medicineKey)
    {
        Medicine? med = MedicineCatalog.For(medicineKey);
        if (med is null) return "";
        int pct = (int)Math.Round(med.Value.Efficacy * 100.0);
        return pct >= 100 ? "" : $" · 效率{pct}%";
    }

    private static string DescribeCondition(HealthCondition c)
    {
        string part = c.BodyPart ?? "全身";
        string sev = SeverityWord(c.Severity);
        return c.Type switch
        {
            HealthConditionType.Bleeding => c.IsOperated ? $"{part}：伤口已缝合，恢复中" : $"{part}：流血不止（{sev}）",
            HealthConditionType.Fracture => c.IsOperated ? $"{part}：已固定，恢复中" : $"{part}：骨折（{sev}）",
            HealthConditionType.Infection => $"{part}：伤口感染（{HealthConditionSet.InfectionStageWord(c.Severity)}）",
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
