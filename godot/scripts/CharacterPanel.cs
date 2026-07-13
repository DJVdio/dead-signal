using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// RimWorld 风右侧检视侧栏：点选幸存者时从屏幕右侧出现的竖向面板，不遮挡世界中心。
/// 只消费只读快照 <see cref="PawnInspection"/>，永远拿不到可变的战斗对象引用。
///
/// 骨架在 <see cref="_Ready"/> 里程序化搭一次（与本工程 Hud.cs 一致的代码驱动 UI 风格），
/// 内容在 <see cref="ShowFor"/> 里按快照清空重填。挂载/点选触发由 CampMain 侧负责，本类只保证
/// 是可被 PackedScene 加载实例化的独立场景 + 下述对外 API 可用。
///
/// 挂载约定：根 Control 用锚点贴屏幕右侧，故须挂在 CanvasLayer 或铺满视口的 Control 下，
/// 锚点才会相对屏幕解析（挂在裸 Node2D 下锚点无参照）。
/// </summary>
public sealed partial class CharacterPanel : PanelContainer
{
    // —— 状态标记配色（狠辣致残是本游戏有意为之，用醒目色，不淡化）——
    private static readonly Color ColSevered = UiStyle.Danger;             // 切除/损毁：统一语义红
    private static readonly Color ColFractured = new(0.96f, 0.62f, 0.16f); // 骨折：橙（状态专属，不入通用语义）
    private static readonly Color ColBleeding = new(0.72f, 0.06f, 0.08f);  // 出血：深红（状态专属）
    private static readonly Color ColDisabled = new(0.56f, 0.56f, 0.58f);  // 失能：灰
    private static readonly Color ColText = new(0.93f, 0.95f, 1f);
    private static readonly Color ColMuted = new(0.66f, 0.70f, 0.78f);

    /// <summary>关闭按钮点击时 emit；由 trigger 连到业务关闭逻辑，本面板不自行决定关闭。</summary>
    [Signal]
    public delegate void CloseRequestedEventHandler();

    /// <summary>
    /// 装假肢请求处理器：把某等级假肢装到指定取代区域（<see cref="BodyRegion.Hand"/>/<see cref="BodyRegion.Leg"/>）的空槽，
    /// 装完返回装后的新快照供面板刷新。由持有 live Pawn 的一侧（如 CampMain）提供闭包；
    /// 面板本身仍只读快照、不持可变战斗对象。为 null 时面板不显示装备入口（功能休眠）。
    /// </summary>
    public delegate PawnInspection ProstheticEquipHandler(BodyRegion replacesRegion, ProstheticGrade grade);

    private ProstheticEquipHandler? _onEquip;

    /// <summary>当前展示的装备态快照（死数据，由持有 live Pawn 的一方拍好传入）；null = 不显示装备区。</summary>
    private EquipmentSnapshot? _equipment;

    /// <summary>卸某手武器回调（由持有 live Pawn 的一方提供，卸下并回库存 + 刷新）；null = 不显示「卸下」入口。</summary>
    private Action<Hand>? _onUnequipWeapon;

    /// <summary>卸某件穿戴品回调（按名）；null = 不显示「卸下」入口。</summary>
    private Action<EquipSlot>? _onUnequipApparel;

    /// <summary>true 时在 _Ready 用一份手工快照自测布局；发布保持默认 false。</summary>
    [Export]
    private bool _debugPreview = false;

    private Label _nameLabel = null!;
    private Label _statusLabel = null!;
    private VBoxContainer _overviewBox = null!;
    private VBoxContainer _healthBox = null!;

    /// <summary>当前面板是否显示。</summary>
    public bool IsShown => Visible;

    public override void _Ready()
    {
        BuildSkeleton();
        Visible = false;

        if (_debugPreview)
        {
            // 自测：切左手 + 切右腿造两个空槽，配一个就地改 Body 的装配闭包，编辑器里可点按钮验证恢复。
            var body = HumanBody.NewBody();
            body.Sever(HumanBody.LeftHand);
            body.Sever(HumanBody.RightLeg);
            body.RecalculatePenalties();
            ShowFor(
                PawnInspection.FromBody(body, null, null, "预览·测试对象"),
                (region, grade) =>
                {
                    body.AttachProsthetic(Prosthetic.OfGrade(grade, region, grade.ToString()));
                    return PawnInspection.FromBody(body, null, null, "预览·测试对象");
                });
        }
    }

    /// <summary>显示面板并用快照填充（无装假肢入口/无装备快照）；重复调用即切换到新幸存者刷新内容。</summary>
    public void ShowFor(PawnInspection insp) => ShowFor(insp, null, null);

    /// <summary>
    /// 显示面板并用快照填充，同时接入装假肢入口 <paramref name="onEquip"/>（为 null 则不显示入口）。
    /// </summary>
    public void ShowFor(PawnInspection insp, ProstheticEquipHandler? onEquip) => ShowFor(insp, null, onEquip);

    /// <summary>
    /// 显示面板并用快照填充，附装备态快照 <paramref name="equipment"/>（11 槽/持械/握持，为 null 不显示装备区）、
    /// 装假肢入口 <paramref name="onEquip"/>、卸武器/卸穿戴回调（为 null 则该「卸下」入口不出现）。
    /// 全部为死数据/闭包，面板仍只读、拿不到 live Pawn。
    /// </summary>
    public void ShowFor(
        PawnInspection insp, EquipmentSnapshot? equipment, ProstheticEquipHandler? onEquip,
        Action<Hand>? onUnequipWeapon = null, Action<EquipSlot>? onUnequipApparel = null)
    {
        _equipment = equipment;
        _onEquip = onEquip;
        _onUnequipWeapon = onUnequipWeapon;
        _onUnequipApparel = onUnequipApparel;
        _nameLabel.Text = insp.DisplayName;
        _statusLabel.Text = HealthSummary(insp);
        _statusLabel.AddThemeColorOverride("font_color", insp.IsDead ? ColSevered : ColText);

        FillOverview(insp);
        FillHealth(insp);

        Visible = true;
    }

    /// <summary>隐藏面板。</summary>
    public void HidePanel() => Visible = false;

    // ———————————————————————————— 骨架 ————————————————————————————

    private void BuildSkeleton()
    {
        // 贴屏幕右侧、全高、约占 1/3 宽的竖向侧栏。
        AnchorLeft = 0.68f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetLeft = OffsetTop = OffsetRight = OffsetBottom = 0f;
        CustomMinimumSize = new Vector2(340, 0);
        MouseFilter = MouseFilterEnum.Stop; // 吃掉侧栏区域点击，避免穿透到世界

        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.94f),
            BorderColor = new Color(0.30f, 0.34f, 0.40f, 0.9f),
            BorderWidthLeft = 2,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        AddThemeStyleboxOverride("panel", bg);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        // —— 头部：姓名 + 关闭按钮 ——
        var header = new HBoxContainer();
        root.AddChild(header);

        _nameLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _nameLabel.AddThemeFontSizeOverride("font_size", 24);
        _nameLabel.AddThemeColorOverride("font_color", ColText);
        _nameLabel.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_nameLabel);

        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(34, 30) };
        close.TooltipText = "关闭";
        close.Pressed += () => EmitSignal(SignalName.CloseRequested);
        header.AddChild(close);

        // —— 健康聚合态（常驻头部下方，一眼可见）——
        _statusLabel = new Label();
        _statusLabel.AddThemeFontSizeOverride("font_size", 15);
        _statusLabel.AddThemeColorOverride("font_color", ColText);
        root.AddChild(_statusLabel);

        root.AddChild(new HSeparator());

        // —— 概览 / 健康 两页签 ——
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(tabs);

        _overviewBox = MakeTabPage(tabs, "概览");
        _healthBox = MakeTabPage(tabs, "健康");
    }

    /// <summary>造一个带滚动的页签，返回其内容 VBox。</summary>
    private static VBoxContainer MakeTabPage(TabContainer tabs, string title)
    {
        var scroll = new ScrollContainer
        {
            Name = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        tabs.AddChild(scroll);

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);
        return box;
    }

    // ———————————————————————————— 概览页 ————————————————————————————

    private void FillOverview(PawnInspection insp)
    {
        ClearChildren(_overviewBox);

        // —— 状态：饥饿等级（一行，随等级加深配色）——
        _overviewBox.AddChild(SectionTitle("状态"));
        _overviewBox.AddChild(Line($"饥饿：{insp.HungerLabel}", HungerColor(insp.HungerStage), 15));

        // —— 能力：操作 / 移动（能力 = 1 − 惩罚净值）——
        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("能力"));
        _overviewBox.AddChild(AbilityRow("操作能力", 1.0 - insp.OperationPenalty));
        _overviewBox.AddChild(AbilityRow("移动能力", 1.0 - insp.MobilityPenalty));

        // —— 义肢：仅当有被切除的手/腿槽时显示（装上恢复能力）——
        FillProsthetics(insp);

        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("武器"));
        if (insp.Weapon is { } w)
        {
            _overviewBox.AddChild(Line(w.Name, ColText, 15));
            var kind = w.IsRanged ? "远程" : "近战";
            var hands = w.TwoHanded ? "双手" : "单手";
            _overviewBox.AddChild(Line($"伤害 {w.DamageMin:0.#}~{w.DamageMax:0.#}   穿透 {w.Penetration:0.#}", ColMuted, 13));
            _overviewBox.AddChild(Line($"{kind} · {hands} · 攻击间隔 {w.AttackInterval:0.##}s", ColMuted, 13));
        }
        else
        {
            _overviewBox.AddChild(Line("徒手（无武器）", ColMuted, 13));
        }

        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("护甲"));
        if (insp.Armor.Count == 0)
        {
            _overviewBox.AddChild(Line("无护甲", ColMuted, 13));
        }
        else
        {
            foreach (var a in insp.Armor)
            {
                _overviewBox.AddChild(Line($"{a.Name}  〔{SlotLabel(a.Slot)}〕", ColText, 14));
                _overviewBox.AddChild(Line($"利器防御 {a.SharpDefense:0.#}   钝器防御 {a.BluntDefense:0.#}", ColMuted, 13));
            }
        }

        // —— 装备槽位：持械（左右手 + 握持态）+ 11 穿戴槽（断肢槽灰显）——
        FillEquipment();
    }

    // ———————————————————————————— 装备槽位 ————————————————————————————

    /// <summary>
    /// 展示装备态快照：左右手持械 + 握持态，再列全部 11 穿戴槽（各槽已装物/空/断肢禁用）。
    /// 无快照（<see cref="_equipment"/> 为 null，如调试预览）则整块跳过。
    /// </summary>
    private void FillEquipment()
    {
        if (_equipment is not { } eq)
        {
            return;
        }

        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("持械"));
        _overviewBox.AddChild(HandRow("左手", Hand.Left, eq.LeftHandWeapon));
        _overviewBox.AddChild(HandRow("右手", Hand.Right, eq.RightHandWeapon));
        _overviewBox.AddChild(Line($"握持：{GripLabel(eq.Grip)}", ColMuted, 13));

        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("穿戴槽"));
        foreach (var slot in eq.Slots)
        {
            _overviewBox.AddChild(SlotRow(slot));
        }
    }

    /// <summary>一行持械：手名 + 武器名（空手灰显「空手」）；有卸武器回调且该手持械时给「卸下」按钮。</summary>
    private HBoxContainer HandRow(string label, Hand hand, WeaponInfo? weapon)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        var name = new Label { Text = label, CustomMinimumSize = new Vector2(72, 0) };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", ColMuted);
        row.AddChild(name);

        var val = new Label { Text = weapon?.Name ?? "空手", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        val.AddThemeFontSizeOverride("font_size", 13);
        val.AddThemeColorOverride("font_color", weapon is null ? ColMuted : ColText);
        // 悬停已装武器名 → 原生 tooltip 显示其一行风味描述（自然位置，不另起一行挤压紧凑排布）。
        if (weapon is not null && !string.IsNullOrEmpty(weapon.Description))
        {
            val.TooltipText = weapon.Description;
        }
        row.AddChild(val);

        if (weapon is not null && _onUnequipWeapon is { } unequip)
        {
            row.AddChild(UnequipButton(() => unequip(hand)));
        }

        return row;
    }

    /// <summary>一行穿戴槽：槽名 + 已装物（空槽/断肢禁用灰显）；有卸穿戴回调且该槽有装备时给「卸下」按钮。</summary>
    private HBoxContainer SlotRow(ApparelSlotStatus slot)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        var name = new Label { Text = SlotLabel(slot.Slot), CustomMinimumSize = new Vector2(72, 0) };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", slot.IsDisabled ? ColDisabled : ColMuted);
        row.AddChild(name);

        string text = slot.IsDisabled ? "（断肢）" : slot.ItemName ?? "空";
        Color col = slot.IsDisabled ? ColDisabled : slot.ItemName is null ? ColMuted : ColText;
        var val = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        val.AddThemeFontSizeOverride("font_size", 13);
        val.AddThemeColorOverride("font_color", col);
        // 悬停已装护甲名 → 原生 tooltip 显示其一行风味描述（狗装备优先查目录，其次人类护甲表）。
        if (slot.ItemName is { } equipped && !slot.IsDisabled)
        {
            string flavor = DogGearCatalog.Get(equipped)?.Description is { Length: > 0 } dg
                ? dg
                : ArmorTable.DescriptionOf(equipped);
            if (!string.IsNullOrEmpty(flavor))
            {
                val.TooltipText = flavor;
            }
        }
        row.AddChild(val);

        if (slot.ItemName is not null && !slot.IsDisabled && _onUnequipApparel is { } unequip)
        {
            // 按**槽**卸（不是按名）：成对品（手套/鞋）同名两只各占一槽，只脱这一只（[SPEC-B18-补]）。
            row.AddChild(UnequipButton(() => unequip(slot.Slot)));
        }

        return row;
    }

    /// <summary>「卸下」小按钮：点击执行卸下回调（回调内负责卸下 + 回库存 + 重刷面板）。</summary>
    private static Button UnequipButton(Action onPressed)
    {
        var b = new Button { Text = "卸下", CustomMinimumSize = new Vector2(0, 24) };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.TooltipText = "卸下并放回营地库存";
        b.Pressed += onPressed;
        return b;
    }

    private static string GripLabel(GripMode grip) => grip switch
    {
        GripMode.TwoHanded => "双手",
        GripMode.DualWield => "双持",
        _ => "单手",
    };

    private static string SlotLabel(EquipSlot slot) => DisplayNames.Of(slot);

    // ———————————————————————————— 义肢 ————————————————————————————

    /// <summary>
    /// 列出被切除的手/腿槽：已装假肢显示等级标；空槽（<see cref="ProstheticSlot.CanEquip"/>）在有装备入口时
    /// 给出木制/简易/仿生三个按钮，点击 → 装假肢闭包 → 拿新快照重填面板（能力条即时反映恢复）。
    /// </summary>
    private void FillProsthetics(PawnInspection insp)
    {
        var slots = insp.ProstheticSlots.Where(s => s.IsAmputated).ToList();
        if (slots.Count == 0)
        {
            return; // 四肢俱全，不显示义肢区
        }

        _overviewBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _overviewBox.AddChild(SectionTitle("义肢"));

        foreach (var slot in slots)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6);

            var name = new Label
            {
                Text = SlotLabel(slot),
                CustomMinimumSize = new Vector2(96, 0),
            };
            name.AddThemeFontSizeOverride("font_size", 13);
            name.AddThemeColorOverride("font_color", ColText);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(name);

            if (slot.HasProsthetic)
            {
                row.AddChild(MarkTag(slot.ProstheticName ?? GradeLabel(slot.Grade), GradeColor(slot.Grade)));
            }
            else if (_onEquip is not null)
            {
                // 空槽：三个等级按钮，点击即装。
                foreach (var grade in new[] { ProstheticGrade.Wooden, ProstheticGrade.Simple, ProstheticGrade.Bionic })
                {
                    row.AddChild(EquipButton(slot, grade));
                }
            }
            else
            {
                row.AddChild(Line("空槽", ColMuted, 13));
            }

            _overviewBox.AddChild(row);
        }
    }

    /// <summary>一个装假肢按钮：点击调用装备闭包，用返回的新快照重填面板（连同装备入口一起保留）。</summary>
    private Button EquipButton(ProstheticSlot slot, ProstheticGrade grade)
    {
        var b = new Button
        {
            Text = GradeLabel(grade),
            CustomMinimumSize = new Vector2(0, 26),
        };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.TooltipText = $"给{SlotLabel(slot)}装{GradeLabel(grade)}假肢";
        var handler = _onEquip;
        b.Pressed += () =>
        {
            if (handler is null)
            {
                return;
            }
            PawnInspection updated = handler(slot.ReplacesRegion, grade);
            ShowFor(updated, handler); // 重填：能力条/义肢区即时反映恢复
        };
        return b;
    }

    /// <summary>槽位展示名：手直接用部位名；脚（腿假肢）用侧别 + "腿"。</summary>
    private static string SlotLabel(ProstheticSlot slot)
    {
        if (slot.ReplacesRegion == BodyRegion.Hand)
        {
            return slot.UnitPartName; // 如 "左手"
        }
        // 脚部单位映射到"腿"槽（腿假肢）：左脚→左腿 / 右脚→右腿。
        var side = slot.UnitPartName.StartsWith("左") ? "左" : slot.UnitPartName.StartsWith("右") ? "右" : "";
        return $"{side}腿";
    }

    private static string GradeLabel(ProstheticGrade? grade) => grade switch
    {
        ProstheticGrade.Wooden => "木制",
        ProstheticGrade.Simple => "简易",
        ProstheticGrade.Bionic => "仿生",
        _ => "假肢",
    };

    /// <summary>假肢优劣色阶：仿生=绿（优）、简易=黄（中）、木制=灰（差），一眼看出好坏。</summary>
    private static Color GradeColor(ProstheticGrade? grade) => grade switch
    {
        ProstheticGrade.Bionic => UiStyle.Success,
        ProstheticGrade.Simple => UiStyle.Warning,
        _ => ColDisabled,
    };

    // ———————————————————————————— 健康页 ————————————————————————————

    private void FillHealth(PawnInspection insp)
    {
        ClearChildren(_healthBox);

        // —— 感染（此前仅医疗面板可见）：健康页常驻列出，配合头顶 glyph / 卡牌病征点补齐"抗生素赌局"的可见性 ——
        if (insp.Infections.Count > 0)
        {
            _healthBox.AddChild(SectionTitle("感染"));
            foreach (InfectionStatus inf in insp.Infections)
            {
                _healthBox.AddChild(InfectionRow(inf));
            }
        }

        // 用 ParentName 重建父子层级：根 = ParentName 为 null 者（通常躯干）。
        var byParent = insp.Parts
            .GroupBy(p => p.ParentName)
            .ToDictionary(g => g.Key ?? "", g => g.ToList());
        var byName = insp.Parts.ToDictionary(p => p.Name, p => p);

        var roots = insp.Parts.Where(p => p.ParentName is null || !byName.ContainsKey(p.ParentName)).ToList();
        if (roots.Count == 0)
        {
            _healthBox.AddChild(Line("无部位数据", ColMuted, 13));
            return;
        }
        foreach (var r in roots)
        {
            AddPartRow(r, 0, byParent);
        }
    }

    /// <summary>递归：一部位一行（缩进表层级）+ 其子部位。</summary>
    private void AddPartRow(PartStatus part, int depth, IReadOnlyDictionary<string, List<PartStatus>> byParent)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        if (depth > 0)
        {
            row.AddChild(new Control { CustomMinimumSize = new Vector2(depth * 14, 0) });
        }

        var name = new Label
        {
            Text = part.Name,
            CustomMinimumSize = new Vector2(72, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", part.IsDestroyed || part.IsSevered ? ColDisabled : ColText);
        row.AddChild(name);

        // HP 条 + 数值
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = part.MaxHp <= 0 ? 1 : part.MaxHp,
            Value = part.Hp,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(96, 16),
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HpColor(part) });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.16f, 0.17f, 0.2f) });
        row.AddChild(bar);

        var hp = new Label { Text = $"{part.Hp:0}/{part.MaxHp:0}", CustomMinimumSize = new Vector2(52, 0) };
        hp.AddThemeFontSizeOverride("font_size", 12);
        hp.AddThemeColorOverride("font_color", ColMuted);
        hp.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(hp);

        _healthBox.AddChild(row);

        // 状态短标（同一行下方的标记条）
        var marks = PartMarks(part);
        if (marks.Count > 0)
        {
            var markRow = new HBoxContainer();
            markRow.AddThemeConstantOverride("separation", 5);
            if (depth > 0)
            {
                markRow.AddChild(new Control { CustomMinimumSize = new Vector2(depth * 14, 0) });
            }
            foreach (var (text, col) in marks)
            {
                markRow.AddChild(MarkTag(text, col));
            }
            _healthBox.AddChild(markRow);
        }

        if (byParent.TryGetValue(part.Name, out var kids))
        {
            foreach (var k in kids)
            {
                AddPartRow(k, depth + 1, byParent);
            }
        }
    }

    /// <summary>一行感染状态：部位（系统性="全身"）+ 严重度标签（早期/恶化/危重，按严重度着色）。只映射引擎真实病状。</summary>
    private static Control InfectionRow(InfectionStatus inf)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        var name = new Label
        {
            Text = string.IsNullOrEmpty(inf.BodyPart) ? "全身" : inf.BodyPart,
            CustomMinimumSize = new Vector2(72, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", ColText);
        row.AddChild(name);

        row.AddChild(MarkTag(InfectionSeverityLabel(inf.Severity), InfectionColor(inf.Severity)));
        return row;
    }

    /// <summary>感染严重度中文档：早期/恶化/危重（对齐终态坏疽/败血症前兆）。</summary>
    private static string InfectionSeverityLabel(double severity) => severity switch
    {
        >= 0.66 => "危重",
        >= 0.33 => "恶化",
        _ => "早期",
    };

    /// <summary>感染配色：早期黄→恶化橙红→危重品红（与 StatusIconStrip/SurvivorCardBar 感染指示同口径）。</summary>
    private static Color InfectionColor(double severity) => severity switch
    {
        >= 0.66 => new Color(0.80f, 0.15f, 0.55f),
        >= 0.33 => new Color(0.90f, 0.45f, 0.35f),
        _ => new Color(0.85f, 0.72f, 0.30f),
    };

    private static List<(string, Color)> PartMarks(PartStatus p)
    {
        var marks = new List<(string, Color)>();
        if (p.IsSevered) marks.Add(("切除", ColSevered));
        if (p.IsDestroyed) marks.Add(("损毁", ColSevered));
        if (p.IsFractured) marks.Add(("骨折", ColFractured));
        if (p.IsBleeding) marks.Add(("出血", ColBleeding));
        if (p.IsDisabled) marks.Add(("失能", ColDisabled));
        return marks;
    }

    // ———————————————————————————— 小工具 ————————————————————————————

    private static string HealthSummary(PawnInspection insp)
    {
        if (insp.IsDead)
        {
            return "已死亡";
        }

        var parts = new List<string> { insp.IsUnconscious ? "昏迷" : "存活" };
        if (insp.IsFullyBlind)
        {
            parts.Add("全盲");
        }

        var blood = insp.BloodTier switch
        {
            BloodLossTier.Mild => "失血:轻度",
            BloodLossTier.Moderate => "失血:中度",
            BloodLossTier.Severe => "失血:重度",
            BloodLossTier.Dead => "失血致死",
            _ => null,
        };
        if (blood is not null)
        {
            parts.Add(blood);
        }

        return string.Join(" · ", parts) + $"　血量 {insp.BloodRatio * 100:0}%";
    }

    /// <summary>一条能力条：名称 + 百分比进度条 + 数值（沿用健康页 HP 行的条形风格）。ability∈[0,1]。</summary>
    private static HBoxContainer AbilityRow(string label, double ability)
    {
        var pct = Mathf.Clamp((float)ability, 0f, 1f);

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        var name = new Label { Text = label, CustomMinimumSize = new Vector2(72, 0) };
        name.AddThemeFontSizeOverride("font_size", 13);
        name.AddThemeColorOverride("font_color", ColText);
        row.AddChild(name);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = pct,
            ShowPercentage = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(96, 16),
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = AbilityColor(pct) });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.16f, 0.17f, 0.2f) });
        row.AddChild(bar);

        var val = new Label { Text = $"{pct * 100:0}%", CustomMinimumSize = new Vector2(52, 0) };
        val.AddThemeFontSizeOverride("font_size", 12);
        val.AddThemeColorOverride("font_color", ColMuted);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(val);

        return row;
    }

    /// <summary>能力条配色：高=绿、低=红（与 HP 条同一绿→黄→红梯度）。ability∈[0,1]。</summary>
    private static Color AbilityColor(float ability)
    {
        return ability > 0.5f
            ? new Color(Mathf.Lerp(0.85f, 0.35f, (ability - 0.5f) * 2f), 0.78f, 0.28f)
            : new Color(0.85f, Mathf.Lerp(0.25f, 0.78f, ability * 2f), 0.22f);
    }

    /// <summary>饥饿配色：正常/吃撑=柔绿，逐级向红加深，饿死=切除红。stage 0=饿死…5=正常…6=吃撑。</summary>
    private static Color HungerColor(int stage) => stage switch
    {
        <= 0 => ColSevered,                     // 0 饿死（终态）
        1 => new Color(0.93f, 0.35f, 0.20f),    // 营养不良
        2 => new Color(0.95f, 0.55f, 0.22f),    // 极度饥饿
        3 => new Color(0.92f, 0.75f, 0.30f),    // 饥饿
        4 => new Color(0.80f, 0.85f, 0.40f),    // 有点饿
        _ => new Color(0.55f, 0.82f, 0.45f),    // 正常 / 吃撑
    };

    private static Color HpColor(PartStatus p)
    {
        if (p.IsDestroyed || p.IsSevered)
        {
            return ColSevered;
        }
        var ratio = p.MaxHp <= 0 ? 0 : Mathf.Clamp((float)(p.Hp / p.MaxHp), 0f, 1f);
        // 绿 → 黄 → 红
        return ratio > 0.5f
            ? new Color(Mathf.Lerp(0.85f, 0.35f, (ratio - 0.5f) * 2f), 0.78f, 0.28f)
            : new Color(0.85f, Mathf.Lerp(0.25f, 0.78f, ratio * 2f), 0.22f);
    }

    private static string SlotLabel(ArmorSlot slot) => DisplayNames.Of(slot);

    private static Label SectionTitle(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 16);
        l.AddThemeColorOverride("font_color", new Color(0.62f, 0.82f, 1f));
        return l;
    }

    private static Label Line(string text, Color col, int size)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private static Label MarkTag(string text, Color col)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", new Color(0.98f, 0.98f, 0.98f));
        var box = new StyleBoxFlat
        {
            BgColor = col,
            ContentMarginLeft = 5,
            ContentMarginRight = 5,
            ContentMarginTop = 1,
            ContentMarginBottom = 1,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
        };
        l.AddThemeStyleboxOverride("normal", box);
        return l;
    }

    private static void ClearChildren(Node node)
    {
        foreach (var c in node.GetChildren())
        {
            c.QueueFree();
        }
    }
}
