using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 聚餐食物分配面板（黎明/黄昏，用户拍板的新聚餐交互第一步）：画面暂停变暗后弹出，
/// 展示每个存活者的头像/名字/当前饥饿档位 + 剩余食物总数，玩家用头像旁的 −/+ 微调各人份数（0..2 份）。
/// 打开时按现行自动分粮策略预填（先保第一餐 → 余粮补最饿），玩家只做微调。确认后 emit 各人份数（原序对齐）。
///
/// 强制流程：与 <see cref="ChoicePanel"/> 同理**不入 ESC 集中派发**（<c>TryCloseTopModal</c>），无取消/无 ESC 逃逸；
/// 唯一出口是"确认"，且总量超库存时禁用确认（红字提示）。账目"换壳不换规则"：面板只出份数，
/// 结算走 <see cref="FoodEconomy.ResolveManual"/> → 与既有净零/补餐模型同构（份数≥1 净零、份数≥2 净 +1、进餐 −1 内嵌）。
/// </summary>
public sealed partial class MealAllocationPanel : CanvasLayer
{
    /// <summary>一行进餐者的展示数据（面板不引 Pawn，由营地层拍成纯数据传入）。</summary>
    public readonly record struct DinerRow(
        int PawnId,
        string Name,
        HungerLevel Level,
        int Cap,
        int InitialServings,
        bool Allocatable);

    private Panel _panel = null!;
    private Control _root = null!;
    private Label _title = null!;
    private RichTextLabel _foodLine = null!;
    private VBoxContainer _rowBox = null!;
    private Button _confirmBtn = null!;

    private int _stock;
    private DinerRow[] _rows = Array.Empty<DinerRow>();
    private int[] _servings = Array.Empty<int>();

    /// <summary>确认分配：回传各人份数（与 <see cref="ShowAllocation"/> 传入的 diners 原序对齐）。</summary>
    public event Action<int[]>? Confirmed;

    public override void _Ready()
    {
        _panel = UiStyle.BuildModalShell(
            this, out _root, "MealAllocationPanel",
            overlayAlpha: 0.72f,
            panelSize: new Vector2(560, 500),
            borderColor: new Color(0.55f, 0.42f, 0.2f));

        _title = new Label();
        _title.Position = new Vector2(24, 18);
        _title.AddThemeFontSizeOverride("font_size", 22);
        _title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        _panel.AddChild(_title);

        // 食物预算行（BBCode：超支时把"剩余"标红，账目中性不染）。附一句进餐账目提示。
        _foodLine = new RichTextLabel
        {
            Position = new Vector2(24, 54),
            Size = new Vector2(512, 44),
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.Arbitrary,
        };
        _foodLine.AddThemeFontSizeOverride("normal_font_size", 14);
        _foodLine.AddThemeColorOverride("default_color", new Color(0.85f, 0.82f, 0.75f));
        _panel.AddChild(_foodLine);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(24, 104),
            Size = new Vector2(512, 330),
        };
        _panel.AddChild(scroll);

        _rowBox = new VBoxContainer();
        _rowBox.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _rowBox.AddThemeConstantOverride("separation", 8);
        _rowBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_rowBox);

        _confirmBtn = new Button
        {
            Text = "开饭",
            Position = new Vector2(210, 446),
            Size = new Vector2(140, 44),
        };
        _confirmBtn.Pressed += OnConfirmPressed;
        UiStyle.StyleButton(_confirmBtn, new Color(0.75f, 0.55f, 0.2f),
            cornerRadius: 6, normalBg: new Color(0.12f, 0.12f, 0.14f), hoverAlpha: 0.25f, fontSize: 16);
        _panel.AddChild(_confirmBtn);
    }

    /// <summary>
    /// 填充并显示分配面板。<paramref name="stock"/> 为当前库存份数；<paramref name="diners"/> 各行的
    /// <see cref="DinerRow.InitialServings"/> 即预填值（营地层用 <see cref="FoodEconomy.Prefill"/> 算好）。
    /// </summary>
    public void ShowAllocation(string title, int stock, IReadOnlyList<DinerRow> diners)
    {
        _title.Text = title;
        _stock = Math.Max(0, stock);
        _rows = diners.ToArray();
        _servings = _rows
            .Select(r => r.Allocatable ? Math.Clamp(r.InitialServings, 0, FoodEconomy.MaxServingsPerMeal) : 0)
            .ToArray();

        Rebuild();
        Visible = true;
    }

    private void Rebuild()
    {
        UiStyle.ClearChildren(_rowBox);
        for (int i = 0; i < _rows.Length; i++)
        {
            _rowBox.AddChild(BuildRow(i));
        }
        RefreshFoodLine();
    }

    private Control BuildRow(int index)
    {
        DinerRow r = _rows[index];

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.CustomMinimumSize = new Vector2(0, 52);

        row.AddChild(BuildPortrait(r));

        // 名字 + 当前饥饿档位（档位越低越饿越醒目：饥饿及以下标红/黄）。
        var col = new VBoxContainer();
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var nameLabel = new Label { Text = r.Name };
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.9f, 0.82f));
        col.AddChild(nameLabel);
        var hungerLabel = new Label { Text = $"当前：{r.Level.Label()}" };
        hungerLabel.AddThemeFontSizeOverride("font_size", 12);
        hungerLabel.AddThemeColorOverride("font_color", HungerColor(r.Level));
        col.AddChild(hungerLabel);
        row.AddChild(col);

        // −/份数/+ 三件套。
        Button minus = SmallStepButton("−");
        minus.Disabled = !r.Allocatable || _servings[index] <= 0;
        minus.Pressed += () => Step(index, -1);
        row.AddChild(minus);

        var count = new Label
        {
            Text = _servings[index].ToString(),
            CustomMinimumSize = new Vector2(28, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        count.AddThemeFontSizeOverride("font_size", 18);
        count.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.8f));
        row.AddChild(count);

        Button plus = SmallStepButton("＋");
        plus.Disabled = !r.Allocatable || _servings[index] >= FoodEconomy.MaxServingsPerMeal;
        plus.Pressed += () => Step(index, +1);
        row.AddChild(plus);

        return row;
    }

    private static Button SmallStepButton(string text)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(40, 40),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        UiStyle.StyleButton(b, new Color(0.6f, 0.5f, 0.3f), cornerRadius: 6, fontSize: 20);
        return b;
    }

    /// <summary>头像块：复用 <see cref="SurvivorCardVisuals"/> 的 Id→图稳定映射；无导入用稳定色块占位。</summary>
    private static Control BuildPortrait(DinerRow r)
    {
        const int size = 44;
        string path = $"res://assets/portraits/{SurvivorCardVisuals.PortraitFileForId(r.PawnId)}";
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex is not null)
        {
            return new TextureRect
            {
                Texture = tex,
                CustomMinimumSize = new Vector2(size, size),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }

        (float pr, float pg, float pb) = SurvivorCardVisuals.StableColorForId(r.PawnId);
        var block = new ColorRect
        {
            Color = new Color(pr, pg, pb),
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var initial = new Label
        {
            Text = r.Name.Length > 0 ? r.Name.Substring(0, 1) : "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        initial.AnchorRight = 1f;
        initial.AnchorBottom = 1f;
        initial.AddThemeFontSizeOverride("font_size", 20);
        initial.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.9f));
        block.AddChild(initial);
        return block;
    }

    /// <summary>饥饿档位配色：饥饿(3)及以下走警示/危急语义色，其余中性绿。</summary>
    private static Color HungerColor(HungerLevel level) => level switch
    {
        <= HungerLevel.Ravenous => UiStyle.Danger,   // 极度饥饿/营养不良：红
        HungerLevel.Hungry => UiStyle.Warning,        // 饥饿：黄
        _ => new Color(0.7f, 0.78f, 0.68f),           // 有点饿/正常/吃撑：中性
    };

    private void Step(int index, int delta)
    {
        if (index < 0 || index >= _servings.Length || !_rows[index].Allocatable)
        {
            return;
        }
        _servings[index] = Math.Clamp(_servings[index] + delta, 0, FoodEconomy.MaxServingsPerMeal);
        Rebuild(); // 重建行以刷新计数/按钮禁用态 + 预算行
    }

    private int TotalServings => _servings.Sum();

    private void RefreshFoodLine()
    {
        int total = TotalServings;
        int remaining = _stock - total;
        string remainStr = remaining < 0
            ? $"[color=#{UiStyle.Danger.ToHtml(false)}]超支 {-remaining} 份[/color]"
            : $"剩余 {remaining} 份";
        _foodLine.Text =
            $"可分配食物 {_stock} 份 · 已分配 {total} 份 · {remainStr}\n" +
            $"本次进餐每人饥饿 −1，每吃一份 +1（每人至多 {FoodEconomy.MaxServingsPerMeal} 份）。";

        _confirmBtn.Disabled = remaining < 0;
    }

    private void OnConfirmPressed()
    {
        if (TotalServings > _stock)
        {
            return; // 超支不放行（按钮本应已禁用，双保险）
        }
        Confirmed?.Invoke((int[])_servings.Clone());
    }
}
