using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 宰杀设施面板（模态）：往刀槽装匕首/骨刀 → 把老鼠/鸟宰成肉 + 副产物。**只发事件、不直接执行**
/// （扣猎物 / 起工时任务 / 产物入库都由营地接入的 <c>CampMain.Butchery.cs</c> 做，走 <see cref="ButcheryRuntime"/>）。
/// 骨架照 <see cref="CookingPanel"/>（CanvasLayer + <see cref="UiStyle.BuildModalShell"/>）；冻结/恢复时标由 CampMain 管。
///
/// <para>═══ <b>刀槽的取舍是这个面板的核心</b>（用户拍板）═══
/// 装槽 = 把刀从库存里拿走钉在案板上（同烹饪台的锅/烤架）——今晚站岗的人就得换别的家伙。
/// 匕首快（+50%）但也是最好的近战刀之一；骨刀慢一档（+25%）但你不心疼。<b>没刀不许宰</b>。</para>
/// </summary>
public sealed partial class ButcheryPanel : CanvasLayer
{
    /// <summary>点某把刀的「装上」：emit 那把刀。营地据此从库存扣一把该武器、钉上案板（顶掉旧刀则返还旧刀）。</summary>
    public event Action<ButcherKnife>? KnifeInstallRequested;

    /// <summary>点「取下」：emit。营地据此把案板上的刀还回库存。</summary>
    public event Action? KnifeRemoveRequested;

    /// <summary>点某只猎物的「宰一只」：emit (猎物材料键, 掌刀的人)。营地据此起一条 butcher:&lt;猎物&gt; 工时任务。</summary>
    public event Action<string, Pawn>? ButcherRequested;

    /// <summary>点「关闭」：CampMain 据此隐藏面板并恢复时标。</summary>
    public event Action? Closed;

    // ---- ShowFor 传入的只读依赖 ----
    private ButcherStationState _station = new();
    private ButcherTier _tier = ButcherTier.SimplePoint;
    private IReadOnlyList<Pawn> _butchers = Array.Empty<Pawn>();
    private InventoryStore _inventory = new();
    private CraftingJob? _activeJob;   // 全营单任务队列（与制作/拆解/改装/烹饪互斥）：非 null ⇒ 有人正忙，不接新单
    private Pawn? _selectedButcher;

    // ---- 控件引用 ----
    private Control _root = null!;
    private Label _title = null!;
    private OptionButton _butcherDropdown = null!;
    private VBoxContainer _slotContainer = null!;
    private VBoxContainer _listContainer = null!;
    private Label _footerLabel = null!;

    private static readonly Color TextColor = new(0.85f, 0.82f, 0.75f);
    private static readonly Color DimColor = new(0.55f, 0.55f, 0.5f);
    private static readonly Color HeadColor = new(0.72f, 0.68f, 0.55f);
    private static readonly Color OkColor = UiStyle.Success;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "ButcheryPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(680, 560),
            borderColor: new Color(0.32f, 0.16f, 0.14f));

        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        _title = new Label();
        _title.Text = ButcherStation.PointFurnitureKey;
        _title.Position = new Vector2(24, 16);
        _title.AddThemeFontSizeOverride("font_size", 22);
        _title.AddThemeColorOverride("font_color", new Color(0.9f, 0.82f, 0.7f));
        panel.AddChild(_title);

        var caption = new Label();
        caption.Text = "掌刀";
        caption.Position = new Vector2(24, 54);
        caption.AddThemeFontSizeOverride("font_size", 14);
        caption.AddThemeColorOverride("font_color", DimColor);
        panel.AddChild(caption);

        _butcherDropdown = new OptionButton();
        _butcherDropdown.Position = new Vector2(84, 50);
        _butcherDropdown.Size = new Vector2(220, 30);
        _butcherDropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        _butcherDropdown.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
        _butcherDropdown.ItemSelected += OnButcherSelected;
        panel.AddChild(_butcherDropdown);

        // 刀槽区（一个槽：匕首 / 骨刀 二选一）。
        var slotCaption = new Label();
        slotCaption.Text = "刀槽（匕首 +50% / 骨刀 +25%）";
        slotCaption.Position = new Vector2(24, 92);
        slotCaption.AddThemeFontSizeOverride("font_size", 15);
        slotCaption.AddThemeColorOverride("font_color", HeadColor);
        panel.AddChild(slotCaption);

        _slotContainer = new VBoxContainer();
        _slotContainer.Position = new Vector2(24, 120);
        _slotContainer.Size = new Vector2(632, 96);
        _slotContainer.AddThemeConstantOverride("separation", 4);
        panel.AddChild(_slotContainer);

        // 猎物列表（滚动）
        var listBg = new Panel();
        listBg.Position = new Vector2(24, 228);
        listBg.Size = new Vector2(632, 240);
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
        _listContainer.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_listContainer);

        _footerLabel = new Label();
        _footerLabel.Position = new Vector2(24, 476);
        _footerLabel.Size = new Vector2(460, 44);
        _footerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _footerLabel.AddThemeFontSizeOverride("font_size", 12);
        _footerLabel.AddThemeColorOverride("font_color", DimColor);
        panel.AddChild(_footerLabel);

        var closeBtn = new Button();
        closeBtn.Text = "关闭";
        closeBtn.Position = new Vector2(516, 508);
        closeBtn.Size = new Vector2(140, 40);
        closeBtn.Pressed += () => Closed?.Invoke();
        UiStyle.StyleButton(closeBtn, new Color(0.5f, 0.4f, 0.3f), fontSize: 16);
        panel.AddChild(closeBtn);
    }

    /// <summary>
    /// 打开/刷新面板。<paramref name="activeJob"/> = 全营在制任务（非 null ⇒ 有人正在干活，不接新单）。
    /// </summary>
    public void ShowFor(
        ButcherStationState station,
        ButcherTier tier,
        IReadOnlyList<Pawn> butchers,
        InventoryStore inventory,
        CraftingJob? activeJob = null)
    {
        _station = station ?? new ButcherStationState();
        _tier = tier;
        _butchers = butchers ?? Array.Empty<Pawn>();
        _inventory = inventory ?? new InventoryStore();
        _activeJob = activeJob;

        if (_selectedButcher is null || !_butchers.Contains(_selectedButcher))
        {
            _selectedButcher = _butchers.FirstOrDefault();
        }
        PopulateButcherDropdown();
        Rebuild();
    }

    private void PopulateButcherDropdown()
    {
        _butcherDropdown.Clear();
        for (int i = 0; i < _butchers.Count; i++)
        {
            _butcherDropdown.AddItem(_butchers[i].DisplayName, _butchers[i].Id);
        }
        if (_selectedButcher is not null)
        {
            int idx = _butcherDropdown.GetItemIndex(_selectedButcher.Id);
            if (idx >= 0) _butcherDropdown.Selected = idx;
        }
        _butcherDropdown.Disabled = _butchers.Count == 0;
    }

    private void OnButcherSelected(long index)
    {
        int id = _butcherDropdown.GetItemId((int)index);
        _selectedButcher = _butchers.FirstOrDefault(p => p.Id == id);
        Rebuild();
    }

    private void Rebuild()
    {
        _title.Text = ButcherStation.FurnitureKeyOf(_tier);
        BuildSlot();
        BuildQuarryList();
        BuildFooter();
    }

    // ================= 刀槽（一个槽：匕首 / 骨刀）=================

    private void BuildSlot()
    {
        UiStyle.ClearChildren(_slotContainer);

        // 当前装了哪把（或空）。
        var status = new Label();
        status.Text = _station.HasKnife
            ? $"案板上：{ButcherStation.WeaponNameOf(_station.Slotted)}（+{(int)(ButcheryLogic.SpeedBonusOf(_station.Slotted) * 100)}% 宰杀速度）"
            : "案板上：空——没刀宰不了（先装一把匕首或骨刀）";
        status.AddThemeFontSizeOverride("font_size", 14);
        status.AddThemeColorOverride("font_color", _station.HasKnife ? OkColor : DimColor);
        _slotContainer.AddChild(status);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        // 有活在做时不许动刀（那把刀正在案板上被用；换刀会打乱完工时的产出结算）。
        bool busy = _activeJob is not null;

        foreach (ButcherKnife knife in new[] { ButcherKnife.Dagger, ButcherKnife.BoneKnife })
        {
            string weaponName = ButcherStation.WeaponNameOf(knife);
            bool inStash = _inventory.Weapons.Any(w => w.DisplayName == weaponName);
            bool isSlotted = _station.Slotted == knife;

            var btn = new Button();
            btn.Text = isSlotted ? $"{weaponName}（已装）" : $"装 {weaponName}";
            btn.CustomMinimumSize = new Vector2(150, 30);
            // 已装的这把不必再装；没装的那把库里得有一把该武器才装得上；有活在做时不许换刀。
            btn.Disabled = isSlotted || !inStash || busy;
            ButcherKnife captured = knife;
            btn.Pressed += () => KnifeInstallRequested?.Invoke(captured);
            UiStyle.StyleButton(btn, new Color(0.42f, 0.38f, 0.3f), fontSize: 13);
            row.AddChild(btn);
        }

        var remove = new Button();
        remove.Text = "取下";
        remove.CustomMinimumSize = new Vector2(90, 30);
        remove.Disabled = !_station.HasKnife || busy;
        remove.Pressed += () => KnifeRemoveRequested?.Invoke();
        UiStyle.StyleButton(remove, new Color(0.42f, 0.34f, 0.3f), fontSize: 13);
        row.AddChild(remove);

        _slotContainer.AddChild(row);
    }

    // ================= 猎物列表（按设施档展示对应宰杀配方）=================

    /// <summary>库里能宰的猎物（按当前设施档），按显示名排序。</summary>
    private List<(string Key, string Name, int Have)> QuarryRows()
        => ButcheryLogic.ButcherableKeysFor(_tier)
            .Select(k => (Key: k, Def: Materials.Find(k)))
            .Where(x => x.Def is not null)
            .Select(x => (x.Key, Name: x.Def!.Value.DisplayName, Have: _inventory.MaterialCount(x.Key)))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

    private void BuildQuarryList()
    {
        UiStyle.ClearChildren(_listContainer);

        var head = new Label();
        int minutes = ButcheryLogic.MinutesFor(_tier, _station.Slotted);
        head.Text = _station.HasKnife
            ? $"猎物（一刀 {CraftingPanelFormat.FormatWorkDuration(minutes)}"
              + (_tier == ButcherTier.Table ? "、宰杀台 20% 双倍产出" : "") + "）"
            : "猎物（先装刀才能宰）";
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", HeadColor);
        _listContainer.AddChild(head);

        List<(string Key, string Name, int Have)> rows = QuarryRows();
        bool busy = _activeJob is not null;

        foreach ((string key, string name, int have) in rows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(ItemIconTextures.MakeIconForRefKey(key, 24));

            var nameLabel = new Label();
            nameLabel.Text = $"{name}　（库存 {have}）→ {ProductText(key)}";
            nameLabel.CustomMinimumSize = new Vector2(420, 24);
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", have > 0 ? TextColor : DimColor);
            row.AddChild(nameLabel);

            var btn = new Button();
            btn.Text = "宰一只";
            btn.CustomMinimumSize = new Vector2(90, 26);
            // 没刀 / 没有这只猎物 / 有人正忙 / 没选掌刀人 ⇒ 灰。
            btn.Disabled = !_station.HasKnife || have <= 0 || busy || _selectedButcher is null;
            string k = key;
            btn.Pressed += () =>
            {
                if (_selectedButcher is not null) ButcherRequested?.Invoke(k, _selectedButcher);
            };
            UiStyle.StyleButton(btn, new Color(0.5f, 0.32f, 0.24f), fontSize: 13);
            row.AddChild(btn);

            _listContainer.AddChild(row);
        }
    }

    /// <summary>某只猎物在当前设施档宰出来是什么（产物那行——玩家看得见去处，不神秘）。</summary>
    private string ProductText(string quarryKey)
    {
        ButcherRecipe? recipe = ButcheryLogic.FindRecipe(_tier, quarryKey);
        if (recipe is null) return "肉 + 副产物";

        string meat = Materials.Find(recipe.Value.MeatKey)?.DisplayName ?? recipe.Value.MeatKey;
        string byproduct = Materials.Find(recipe.Value.ByproductKey)?.DisplayName ?? recipe.Value.ByproductKey;
        return $"{byproduct}×{recipe.Value.ByproductQuantity} + {meat}×{recipe.Value.MeatQuantity}";
    }

    private void BuildFooter()
    {
        if (_activeJob is not null)
        {
            _footerLabel.Text = "有人正忙着别的活——完工之后再宰。";
        }
        else if (!_station.HasKnife)
        {
            _footerLabel.Text = "刀槽空着：装一把匕首或骨刀（它会从库存里拿走，钉在案板上，卸下才还回来）。";
        }
        else
        {
            _footerLabel.Text = "宰好的肉与皮会进库存；宰杀是夜间生产时段推进的工时活。";
        }
    }
}
