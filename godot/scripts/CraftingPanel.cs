using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 工作台面板（模态）：两页——【制作】与【改装】，均**只发事件、不直接执行**（执行由营地接入的服务做）。
/// 骨架照 <see cref="StashPanel"/>：CanvasLayer + <see cref="UiStyle.BuildModalShell"/>；冻结/恢复时标由 CampMain 管。
///
/// 【制作页】按"已装工具"给配方分类（<see cref="CraftingPanelFormat.GroupByTool"/>）：组头标该类需要的工具与是否已装；
/// 每条配方列材料成本（够=绿/缺=红）与解锁条件（工具/技能/书，满足打勾），用 <see cref="CraftingLogic.CanCraft"/>
/// 按"当前选中制作者 + 库存 + 工作台"算可否制作，不可则逐条展示 <see cref="CraftAvailability.Blocks"/> 的中文原因。
/// 顶部一个「制作者」下拉选一个可控幸存者（技能门槛按其判定）。「制作」按钮：可制作时可点，emit <see cref="CraftRequested"/>。
///
/// 【改装页】选一把库存武器 → <see cref="WeaponModCatalog.For(Weapon)"/> 列可选改装，按 <see cref="WeaponMod.Part"/> 分组、
/// 同部位已选则其余置灰（对齐 <see cref="WeaponMods.ApplyMods"/> 的同部位互斥），<see cref="WeaponMod.Note"/> 作说明。
/// 选中若干 mod 后「改装」按钮 emit <see cref="ModApplyRequested"/>。
///
/// 事件用**稳定字符串键**传递（面板不改模型）：配方用 <see cref="RecipeData.Id"/>；改装用 <see cref="WeaponMod.Name"/>
/// （在单把武器的可选集内唯一）；基础武器用库存 <see cref="Item.RefKey"/>（= 武器名，可经 <see cref="WeaponTable"/> 解析）。
/// </summary>
public sealed partial class CraftingPanel : CanvasLayer
{
    /// <summary>点某条配方的「制作」：emit (配方 <see cref="RecipeData.Id"/>, 选中的制作者)。营地接入据此走服务实扣实产。</summary>
    public event Action<string, Pawn>? CraftRequested;

    /// <summary>
    /// 点「改装」：emit (基础武器 <see cref="Item.RefKey"/>=武器名, 选中 mod 的 <see cref="WeaponMod.Name"/> 列表, 制作者)。
    /// 营地接入据此解析武器 + <see cref="WeaponMods.ApplyMods"/> 合成、消耗基础武器落地新变体。
    /// </summary>
    public event Action<string, IReadOnlyList<string>, Pawn>? ModApplyRequested;

    /// <summary>点「关闭」：CampMain 据此隐藏面板并恢复时标。</summary>
    public event Action? Closed;

    /// <summary>库存武器名 → 引擎武器（取自 <see cref="WeaponTable.Arsenal"/>，与 <see cref="Pawn"/> 同源）。改装页按其派生大类。</summary>
    private static readonly IReadOnlyDictionary<string, Weapon> WeaponCatalog =
        WeaponTable.Arsenal().ToDictionary(w => w.Name);

    /// <summary>书 id → 标题（供制作页条件行显示书名；已读态另由外部谓词提供）。</summary>
    private static readonly IReadOnlyDictionary<string, string> BookTitles =
        BookLibrary.All().ToDictionary(b => b.Id, b => b.Title);

    private enum Page { Craft, Mod }

    // ---- ShowFor 传入的只读依赖（面板只读它们算可用性，不持久其它 live 状态）----
    private WorkbenchState _workbench = new();
    private IReadOnlyList<Pawn> _crafters = Array.Empty<Pawn>();
    private InventoryStore _inventory = new();
    private Func<Pawn, string, bool> _hasReadBook = (_, _) => false;

    // ---- 面板自身瞬态 ----
    private Page _page = Page.Craft;
    private Pawn? _selectedCrafter;
    private string? _selectedWeaponRefKey;                 // 改装页当前选中的库存武器名
    private readonly HashSet<string> _selectedMods = new(); // 改装页已选 mod（按 WeaponMod.Name）

    // ---- 控件引用 ----
    private Control _root = null!;
    private OptionButton _crafterDropdown = null!;
    private Button _craftTabBtn = null!;
    private Button _modTabBtn = null!;
    private VBoxContainer _listContainer = null!;
    private Label _footerLabel = null!;

    // 配色（对齐 StashPanel 暗色调）
    private static readonly Color TextColor = new(0.85f, 0.82f, 0.75f);
    private static readonly Color DimColor = new(0.55f, 0.55f, 0.5f);
    private static readonly Color HeadColor = new(0.72f, 0.68f, 0.55f);
    private static readonly Color OkColor = new(0.62f, 0.78f, 0.55f);
    private static readonly Color BadColor = new(0.82f, 0.45f, 0.4f);

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "CraftingPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(680, 600),
            borderColor: new Color(0.28f, 0.24f, 0.18f));

        // 根 Control 吃掉背景点击，避免模态开着时点漏到营地世界。
        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        var title = new Label();
        title.Text = "工作台";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        // 顶部一行：制作者下拉 + 页签（制作/改装）
        var crafterCaption = new Label();
        crafterCaption.Text = "制作者";
        crafterCaption.Position = new Vector2(24, 54);
        crafterCaption.AddThemeFontSizeOverride("font_size", 14);
        crafterCaption.AddThemeColorOverride("font_color", DimColor);
        panel.AddChild(crafterCaption);

        _crafterDropdown = new OptionButton();
        _crafterDropdown.Position = new Vector2(84, 50);
        _crafterDropdown.Size = new Vector2(220, 30);
        _crafterDropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        _crafterDropdown.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
        _crafterDropdown.ItemSelected += OnCrafterSelected;
        panel.AddChild(_crafterDropdown);

        _craftTabBtn = new Button();
        _craftTabBtn.Text = "制作";
        _craftTabBtn.Position = new Vector2(400, 50);
        _craftTabBtn.Size = new Vector2(120, 30);
        _craftTabBtn.Pressed += () => SwitchPage(Page.Craft);
        UiStyle.StyleButton(_craftTabBtn, new Color(0.45f, 0.5f, 0.35f), fontSize: 15);
        panel.AddChild(_craftTabBtn);

        _modTabBtn = new Button();
        _modTabBtn.Text = "改装";
        _modTabBtn.Position = new Vector2(528, 50);
        _modTabBtn.Size = new Vector2(120, 30);
        _modTabBtn.Pressed += () => SwitchPage(Page.Mod);
        UiStyle.StyleButton(_modTabBtn, new Color(0.45f, 0.5f, 0.35f), fontSize: 15);
        panel.AddChild(_modTabBtn);

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
        _footerLabel.Size = new Vector2(500, 40);
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

    /// <summary>
    /// 打开面板：传入只读依赖（工作台工具态、可控幸存者作制作者候选、营地共享库存、"某人是否读完某书"谓词），刷新两页数据。
    /// <paramref name="hasReadBook"/> 按 (制作者, 书 id) 查该幸存者本人是否读完（配方书门槛判据，按**制作者**判定；
    /// 面板对当前选中 crafter 求值喂给 <see cref="CraftingLogic.CanCraft"/>，切换制作者时重算）。
    /// </summary>
    public void ShowFor(
        WorkbenchState workbench,
        IReadOnlyList<Pawn> crafters,
        InventoryStore inventory,
        Func<Pawn, string, bool> hasReadBook)
    {
        _workbench = workbench ?? new WorkbenchState();
        _crafters = crafters ?? Array.Empty<Pawn>();
        _inventory = inventory ?? new InventoryStore();
        _hasReadBook = hasReadBook ?? ((_, _) => false);

        // 制作者候选：保留已选（若仍在名单），否则取第一个。
        if (_selectedCrafter is null || !_crafters.Contains(_selectedCrafter))
        {
            _selectedCrafter = _crafters.FirstOrDefault();
        }
        PopulateCrafterDropdown();

        // 改装页选中的武器若已不在库存则清空。
        if (_selectedWeaponRefKey is not null &&
            !_inventory.Weapons.Any(w => w.RefKey == _selectedWeaponRefKey))
        {
            _selectedWeaponRefKey = null;
            _selectedMods.Clear();
        }

        Rebuild();
    }

    private void PopulateCrafterDropdown()
    {
        _crafterDropdown.Clear();
        for (int i = 0; i < _crafters.Count; i++)
        {
            _crafterDropdown.AddItem(_crafters[i].DisplayName, _crafters[i].Id);
        }
        if (_selectedCrafter is not null)
        {
            int idx = _crafterDropdown.GetItemIndex(_selectedCrafter.Id);
            if (idx >= 0) _crafterDropdown.Selected = idx;
        }
        _crafterDropdown.Disabled = _crafters.Count == 0;
    }

    private void OnCrafterSelected(long index)
    {
        int id = _crafterDropdown.GetItemId((int)index);
        _selectedCrafter = _crafters.FirstOrDefault(p => p.Id == id);
        Rebuild(); // 可制作性依赖制作者技能，切人须重算
    }

    private void SwitchPage(Page page)
    {
        _page = page;
        Rebuild();
    }

    private void Rebuild()
    {
        // 页签高亮：当前页禁用（视觉上"按下")。
        _craftTabBtn.Disabled = _page == Page.Craft;
        _modTabBtn.Disabled = _page == Page.Mod;

        UiStyle.ClearChildren(_listContainer);
        if (_page == Page.Craft)
        {
            BuildCraftPage();
        }
        else
        {
            BuildModPage();
        }
    }

    // ================= 制作页 =================

    private void BuildCraftPage()
    {
        if (_selectedCrafter is null)
        {
            _footerLabel.Text = "没有可作制作者的幸存者。";
            AddEmpty("无可用制作者。");
            return;
        }
        _footerLabel.Text = "材料/条件不足的配方会灰显并列出缺项；满足即可制作。";

        IReadOnlySet<ToolSlot> installed = _workbench.InstalledTools;
        foreach (RecipeToolGroup group in CraftingPanelFormat.GroupByTool(RecipeBook.All))
        {
            bool toolsOk = CraftingPanelFormat.ToolsInstalled(group.Tools, installed);
            AddCraftGroupHeader(group, toolsOk);
            foreach (RecipeData recipe in group.Recipes)
            {
                _listContainer.AddChild(BuildCraftRow(recipe, installed));
            }
        }
    }

    private void AddCraftGroupHeader(RecipeToolGroup group, bool toolsOk)
    {
        var head = new Label();
        string reqLabel = CraftingPanelFormat.ToolRequirementLabel(group.Tools);
        head.Text = group.Tools.Count == 0
            ? reqLabel
            : toolsOk ? $"{reqLabel}（已装）" : $"{reqLabel}（未装 —— 需先在工作台装上）";
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", toolsOk ? HeadColor : DimColor);
        _listContainer.AddChild(head);
    }

    private Control BuildCraftRow(RecipeData recipe, IReadOnlySet<ToolSlot> installed)
    {
        CraftAvailability avail = CraftingLogic.CanCraft(
            recipe,
            key => CraftingPanelFormat.MaterialCount(_inventory, key),
            skill => _selectedCrafter!.SkillLevelOf(skill),
            bookId => _hasReadBook(_selectedCrafter!, bookId), // 书门槛按当前选中制作者本人已读
            installed);

        var card = new VBoxContainer();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        card.MouseFilter = Control.MouseFilterEnum.Pass;
        card.AddThemeConstantOverride("separation", 2);

        // 首行：名称(+产出) | 制作按钮
        var top = new HBoxContainer();
        top.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.MouseFilter = Control.MouseFilterEnum.Pass;
        top.AddThemeConstantOverride("separation", 8);

        var nameLabel = new Label();
        string qty = recipe.OutputQuantity > 1 ? $" ×{recipe.OutputQuantity}" : "";
        nameLabel.Text = $"{recipe.DisplayName}{qty}";
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.CustomMinimumSize = new Vector2(300, 26);
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.AddThemeColorOverride("font_color", avail.CanCraft ? TextColor : DimColor);
        top.AddChild(nameLabel);

        var craftBtn = new Button();
        craftBtn.Text = "制作";
        craftBtn.CustomMinimumSize = new Vector2(120, 30);
        craftBtn.Disabled = !avail.CanCraft;
        string recipeId = recipe.Id;
        craftBtn.Pressed += () =>
        {
            if (_selectedCrafter is not null) CraftRequested?.Invoke(recipeId, _selectedCrafter);
        };
        UiStyle.StyleButton(craftBtn, new Color(0.4f, 0.5f, 0.35f), fontSize: 13);
        top.AddChild(craftBtn);
        card.AddChild(top);

        // 材料行（够=绿、缺=红）
        card.AddChild(BuildMaterialLabel(recipe));

        // 条件行（工具/技能/书，满足打勾）
        Label? cond = BuildConditionLabel(recipe, installed);
        if (cond is not null) card.AddChild(cond);

        // 阻断原因（直接展示 CanCraft 的中文 Detail）
        if (!avail.CanCraft)
        {
            var blocks = new Label();
            blocks.Text = "缺：" + string.Join("；", avail.Blocks.Select(b => b.Detail));
            blocks.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            blocks.CustomMinimumSize = new Vector2(600, 0);
            blocks.AddThemeFontSizeOverride("font_size", 12);
            blocks.AddThemeColorOverride("font_color", BadColor);
            card.AddChild(blocks);
        }

        var sep = new HSeparator();
        sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
        card.AddChild(sep);
        return card;
    }

    private Label BuildMaterialLabel(RecipeData recipe)
    {
        var parts = new List<string>();
        foreach (KeyValuePair<string, int> cost in recipe.MaterialCosts)
        {
            int have = CraftingPanelFormat.MaterialCount(_inventory, cost.Key);
            string name = Materials.Find(cost.Key)?.DisplayName ?? cost.Key;
            string mark = have >= cost.Value ? "" : $"（有{have}）";
            parts.Add($"{name}×{cost.Value}{mark}");
        }
        bool allOk = recipe.MaterialCosts.All(c => CraftingPanelFormat.MaterialCount(_inventory, c.Key) >= c.Value);

        var label = new Label();
        label.Text = "材料：" + (parts.Count == 0 ? "无" : string.Join("　", parts));
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(600, 0);
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", allOk ? OkColor : BadColor);
        return label;
    }

    private Label? BuildConditionLabel(RecipeData recipe, IReadOnlySet<ToolSlot> installed)
    {
        var parts = new List<string>();
        foreach (ToolSlot tool in recipe.RequiredTools.OrderBy(t => (int)t))
        {
            parts.Add($"{Check(installed.Contains(tool))}{tool.Label()}");
        }
        foreach (SkillRequirement req in recipe.RequiredSkills)
        {
            bool ok = _selectedCrafter!.SkillLevelOf(req.Skill) >= req.MinLevel;
            parts.Add($"{Check(ok)}{req.Skill.Label()}·{req.MinLevel.Label()}");
        }
        foreach (string bookId in recipe.RequiredBookIds)
        {
            string title = BookTitles.TryGetValue(bookId, out string? t) ? t : bookId;
            parts.Add($"{Check(_hasReadBook(_selectedCrafter!, bookId))}读《{title}》");
        }
        if (parts.Count == 0) return null;

        var label = new Label();
        label.Text = "条件：" + string.Join("　", parts);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(600, 0);
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", DimColor);
        return label;
    }

    private static string Check(bool ok) => ok ? "✓" : "✗";

    // ================= 改装页 =================

    private void BuildModPage()
    {
        _footerLabel.Text = "选一把库存武器，勾选改装（同部位互斥），再点改装。";

        // 武器选择行
        var selRow = new HBoxContainer();
        selRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        selRow.MouseFilter = Control.MouseFilterEnum.Pass;
        selRow.AddThemeConstantOverride("separation", 8);

        var caption = new Label();
        caption.Text = "武器";
        caption.CustomMinimumSize = new Vector2(48, 30);
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.AddThemeFontSizeOverride("font_size", 14);
        caption.AddThemeColorOverride("font_color", HeadColor);
        selRow.AddChild(caption);

        var weaponDropdown = new OptionButton();
        weaponDropdown.CustomMinimumSize = new Vector2(280, 30);
        weaponDropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        weaponDropdown.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));

        var weapons = _inventory.Weapons.ToList();
        weaponDropdown.AddItem("（选择武器）", -1);
        for (int i = 0; i < weapons.Count; i++)
        {
            weaponDropdown.AddItem(weapons[i].DisplayName, i);
        }
        if (_selectedWeaponRefKey is not null)
        {
            int sel = weapons.FindIndex(w => w.RefKey == _selectedWeaponRefKey);
            if (sel >= 0) weaponDropdown.Selected = weaponDropdown.GetItemIndex(sel);
        }
        weaponDropdown.ItemSelected += idx =>
        {
            int id = weaponDropdown.GetItemId((int)idx);
            _selectedWeaponRefKey = id >= 0 && id < weapons.Count ? weapons[id].RefKey : null;
            _selectedMods.Clear();
            Rebuild();
        };
        selRow.AddChild(weaponDropdown);
        _listContainer.AddChild(selRow);

        if (_selectedWeaponRefKey is null)
        {
            AddEmpty("先选一把武器。");
            return;
        }
        if (!WeaponCatalog.TryGetValue(_selectedWeaponRefKey, out Weapon? weapon))
        {
            AddEmpty($"无法解析武器「{_selectedWeaponRefKey}」（不在武器库）。");
            return;
        }

        // 大类提示
        var clsLabel = new Label();
        clsLabel.Text = $"大类：{ClassLabel(WeaponMods.ClassOf(weapon))}";
        clsLabel.AddThemeFontSizeOverride("font_size", 13);
        clsLabel.AddThemeColorOverride("font_color", DimColor);
        _listContainer.AddChild(clsLabel);

        IReadOnlyList<WeaponMod> mods = WeaponModCatalog.For(weapon);
        // 已被占用的部位（当前已选 mod 覆盖的部位）
        var occupiedParts = new HashSet<WeaponPart>(
            mods.Where(m => _selectedMods.Contains(m.Name)).Select(m => m.Part));

        // 按部位分组（保持目录顺序）
        foreach (IGrouping<WeaponPart, WeaponMod> partGroup in mods
                     .Select((m, i) => (m, i))
                     .GroupBy(x => x.m.Part, x => x.m))
        {
            var partHead = new Label();
            partHead.Text = PartLabel(partGroup.Key);
            partHead.AddThemeFontSizeOverride("font_size", 14);
            partHead.AddThemeColorOverride("font_color", HeadColor);
            _listContainer.AddChild(partHead);

            foreach (WeaponMod mod in partGroup)
            {
                _listContainer.AddChild(BuildModRow(mod, occupiedParts));
            }
        }

        // 改装按钮 + 已选摘要
        var applyRow = new HBoxContainer();
        applyRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        applyRow.MouseFilter = Control.MouseFilterEnum.Pass;
        applyRow.AddThemeConstantOverride("separation", 8);

        var summary = new Label();
        summary.Text = _selectedMods.Count == 0 ? "未选改装" : "已选：" + string.Join("、", _selectedMods);
        summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.AddThemeFontSizeOverride("font_size", 12);
        summary.AddThemeColorOverride("font_color", _selectedMods.Count == 0 ? DimColor : OkColor);
        applyRow.AddChild(summary);

        var applyBtn = new Button();
        applyBtn.Text = "改装";
        applyBtn.CustomMinimumSize = new Vector2(120, 32);
        applyBtn.Disabled = _selectedMods.Count == 0 || _selectedCrafter is null;
        string baseKey = _selectedWeaponRefKey;
        applyBtn.Pressed += () =>
        {
            if (_selectedCrafter is not null && _selectedMods.Count > 0)
            {
                ModApplyRequested?.Invoke(baseKey, _selectedMods.ToList(), _selectedCrafter);
            }
        };
        UiStyle.StyleButton(applyBtn, new Color(0.4f, 0.5f, 0.35f), fontSize: 14);
        applyRow.AddChild(applyBtn);
        _listContainer.AddChild(applyRow);
    }

    private Control BuildModRow(WeaponMod mod, IReadOnlySet<WeaponPart> occupiedParts)
    {
        bool selected = _selectedMods.Contains(mod.Name);
        // 同部位已被别的 mod 占用（且不是自己）→ 置灰互斥。
        bool blockedByPart = !selected && occupiedParts.Contains(mod.Part);

        var row = new VBoxContainer();
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddThemeConstantOverride("separation", 1);

        var check = new CheckBox();
        check.Text = mod.Name;
        check.ButtonPressed = selected;
        check.Disabled = blockedByPart;
        check.AddThemeColorOverride("font_color", blockedByPart ? DimColor : TextColor);
        string modName = mod.Name;
        check.Toggled += on =>
        {
            if (on) _selectedMods.Add(modName);
            else _selectedMods.Remove(modName);
            Rebuild(); // 重算同部位互斥的置灰
        };
        row.AddChild(check);

        if (!string.IsNullOrEmpty(mod.Note))
        {
            var note = new Label();
            note.Text = "    " + mod.Note + (blockedByPart ? "（该部位已选其它改装）" : "");
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.CustomMinimumSize = new Vector2(600, 0);
            note.AddThemeFontSizeOverride("font_size", 11);
            note.AddThemeColorOverride("font_color", DimColor);
            row.AddChild(note);
        }
        return row;
    }

    private void AddEmpty(string text)
    {
        var empty = new Label();
        empty.Text = text;
        empty.AddThemeFontSizeOverride("font_size", 14);
        empty.AddThemeColorOverride("font_color", DimColor);
        _listContainer.AddChild(empty);
    }

    private static string ClassLabel(WeaponClass c) => c switch
    {
        WeaponClass.Firearm => "枪械",
        WeaponClass.Blade => "近战锐器",
        WeaponClass.Blunt => "近战钝器",
        _ => c.ToString(),
    };

    private static string PartLabel(WeaponPart p) => p switch
    {
        WeaponPart.Stock => "枪托",
        WeaponPart.Barrel => "枪管",
        WeaponPart.Muzzle => "枪口",
        WeaponPart.Blade => "刃",
        WeaponPart.Handle => "柄",
        WeaponPart.Grip => "缠手",
        WeaponPart.Shaft => "杆",
        _ => p.ToString(),
    };
}
