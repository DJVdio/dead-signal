using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 营地共享库存面板（模态）：按类别列出 <see cref="InventoryStore"/> 的武器/护甲/书/食物，
/// 顶部显示食物份数与可选的一行通知（搜刮反馈）。书行有「阅读」按钮（触发 <see cref="BookOpenRequested"/>）；
/// 可装备项（武器/护甲）每行一个**可用**的「装备」按钮 → <see cref="EquipRequested"/>；
/// 狗装备（键∈<see cref="DogGearCatalog"/>）同一按钮改写为「给布鲁斯穿」，由 CampMain 分流。
/// <b>面板只 emit 事件，不自己装备</b>——实扣实穿由持有 Pawn 的 CampMain 做。
/// 骨架照 <see cref="ChoicePanel"/>/<see cref="ExpeditionPanel"/>：CanvasLayer + <see cref="UiStyle.BuildModalShell"/>。
/// 冻结/恢复时标由 CampMain 管（弹出前 TimeScale=0，关闭恢复）。
/// </summary>
public sealed partial class StashPanel : CanvasLayer
{
    private Control _root = null!;
    private Label _foodLabel = null!;
    private Label _noticeLabel = null!;
    private Label _descLabel = null!;
    private VBoxContainer _listContainer = null!;

    /// <summary>点某本书的「阅读」按钮：emit 该书 id（<see cref="Item.RefKey"/>）。CampMain 据此弹阅读面板。</summary>
    public event Action<string>? BookOpenRequested;

    /// <summary>
    /// 点某件可装备物品的「装备」按钮：emit 其引用键（武器名/护甲名）。
    /// 由持有目标 Pawn 的一方（CampMain）接住，装到当前选中的幸存者并从库存移除。
    /// </summary>
    public event Action<string>? EquipRequested;

    /// <summary>
    /// 点某件光源（手电/火把）的「持起」按钮：emit 其光源键（<see cref="Item.RefKey"/>）。
    /// CampMain 据此让当前选中幸存者占一只手持起该光源（<see cref="HeldLightState"/>）并从库存移除。
    /// </summary>
    public event Action<string>? LightHoldRequested;

    /// <summary>
    /// 点某件东西的「拆解」按钮：emit 其引用键（<see cref="Item.RefKey"/>）。CampMain 据此走
    /// <see cref="SalvageService.Salvage"/> 把它拆回材料（返还比例以 Wiki 配置为准；木材走专门的副产物例外）。
    /// 只有**造得出来**的东西才有这个按钮（<see cref="SalvageLogic.CanSalvageKey"/>）——搜刮来的军用枪没有配方，也就无从拆起。
    /// </summary>
    public event Action<string>? SalvageRequested;

    /// <summary>
    /// 点「摆放」（沙袋）：emit 其引用键。CampMain 据此进入放置模式——下一次点地面就把它垒在那儿
    /// （校验见 <see cref="SandbagSpec.CanPlace"/>），并从库存扣掉这一件。
    /// </summary>
    public event Action<string>? PlaceRequested;

    /// <summary>
    /// 探索中点某件背包物品的「扔掉」：emit 它在 <c>ExpeditionBag.Contents</c> 里的下标。
    /// CampMain 据此从背包丢弃并刷新面板——腾出的容量立刻可以拿别的东西。
    /// </summary>
    public event Action<int>? BagDropRequested;

    /// <summary>
    /// 点某件狗装备的「脱下」按钮（布鲁斯装备区）：emit 其装备键（<see cref="DogGearCatalog"/> 键）。
    /// CampMain 据此从布鲁斯身上脱下并退回库存。穿戴入口复用 <see cref="EquipRequested"/>（狗装备也是 Item.Armor，
    /// 由 CampMain 按 <see cref="DogGearCatalog.IsDogGear"/> 分流到布鲁斯而非选中幸存者）。
    /// </summary>
    public event Action<string>? DogGearUnequipRequested;

    /// <summary>
    /// 布鲁斯当前穿戴的狗装备键（<see cref="DogGearCatalog"/> 键）。由 CampMain 在接线时设好
    /// （返回 <c>_bruce.Apparel.EquippedKeys</c>；布鲁斯不在场返回空/null）。<see cref="ShowStash"/> 据此渲染「布鲁斯装备」区。
    /// null 或空 = 不显示该区（无狗/未穿戴）。
    /// </summary>
    public Func<IReadOnlyCollection<string>?>? DogGearProvider { get; set; }

    /// <summary>点「关闭」：CampMain 据此隐藏面板并恢复时标。</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "StashPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(620, 520),
            borderColor: new Color(0.28f, 0.24f, 0.18f));

        // 根 Control 吃掉背景点击，避免模态开着时点漏到营地世界（选人/移动）。
        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        var title = new Label();
        title.Text = "营地库存";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        _foodLabel = new Label();
        _foodLabel.Position = new Vector2(24, 50);
        _foodLabel.AddThemeFontSizeOverride("font_size", 14);
        _foodLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.72f, 0.6f));
        panel.AddChild(_foodLabel);

        _noticeLabel = new Label();
        _noticeLabel.Position = new Vector2(24, 74);
        _noticeLabel.Size = new Vector2(572, 22);
        _noticeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _noticeLabel.AddThemeFontSizeOverride("font_size", 14);
        _noticeLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.78f, 0.55f));
        panel.AddChild(_noticeLabel);

        var listBg = new Panel();
        listBg.Position = new Vector2(24, 104);
        listBg.Size = new Vector2(572, 344);
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

        // 悬停某物品行时，底部显示其一行风味描述（最小展示面；不做大 tooltip 系统）。
        _descLabel = new Label();
        _descLabel.Position = new Vector2(24, 452);
        _descLabel.Size = new Vector2(420, 56);
        _descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _descLabel.AddThemeFontSizeOverride("font_size", 13);
        _descLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.6f, 0.52f));
        panel.AddChild(_descLabel);

        var closeBtn = new Button();
        closeBtn.Text = "关闭";
        closeBtn.Position = new Vector2(456, 460);
        closeBtn.Size = new Vector2(140, 40);
        closeBtn.Pressed += () => Closed?.Invoke();
        UiStyle.StyleButton(closeBtn, new Color(0.5f, 0.4f, 0.3f), fontSize: 16);
        panel.AddChild(closeBtn);
    }

    /// <summary>
    /// 探索中：展示**远征背包**（这趟搬得动的东西），而不是营地库存。
    /// 顶部是「背了多少 / 上限多少」，每行一个「扔掉」——负重上限是硬的，想拿新东西就得先舍旧的。
    /// <para>
    /// 🔴 [T45] <b>装备与战利品分开列，并写明这让全队慢了多少</b>。玩家在这个面板上做的决定是"还拿不拿"，
    /// 而那笔账里有一半（身上的枪与甲）修复前**根本不在账上**——他会以为自己空着手，其实已经背着装备。
    /// 更要命的是：<b>装备重量是扔不掉的</b>（面板只能扔战利品）⇒ 板甲重装出门的人，余量天生就少一大截，
    /// 这必须在他决定"这桶燃料还是这把枪"之前就摆在脸上。
    /// </para>
    /// </summary>
    /// <param name="contents">背包内容（<c>ExpeditionBag.Contents</c>）。</param>
    /// <param name="gearKg">队伍**装备**总重（穿在身上/握在手里的，扔不掉）。</param>
    /// <param name="lootKg">背包里**搜刮来的**那部分（可逐件扔）。</param>
    /// <param name="capacityKg">本趟队伍运力上限。</param>
    /// <param name="notice">可空的一行搜刮反馈（如"背包塞不下，木料留在了原地"）。</param>
    public void ShowExpeditionBag(
        IReadOnlyList<LootItem> contents, double gearKg, double lootKg, double capacityKg, string? notice)
    {
        double carriedKg = gearKg + lootKg;
        bool full = carriedKg >= capacityKg - 1e-9;
        string penalty = CarryCapacity.PenaltyText(carriedKg, capacityKg);
        _foodLabel.Text = $"负重：{CarryCapacity.FormatBag(gearKg, lootKg, capacityKg)}"
                          + (penalty.Length > 0 ? $"　{penalty}" : "")
                          + (full ? "（已满）" : "");
        _foodLabel.AddThemeColorOverride(
            "font_color",
            full || penalty.Length > 0
                ? new Color(0.85f, 0.45f, 0.35f)
                : new Color(0.75f, 0.72f, 0.6f));
        _noticeLabel.Text = notice ?? "";
        _descLabel.Text = "";
        UiStyle.ClearChildren(_listContainer);

        if (contents.Count == 0)
        {
            var empty = new Label();
            empty.Text = "背包是空的。";
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.5f));
            _listContainer.AddChild(empty);
            return;
        }

        for (int i = 0; i < contents.Count; i++)
        {
            _listContainer.AddChild(BuildBagRow(contents[i], i));
        }
    }

    private Control BuildBagRow(LootItem loot, int index)
    {
        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.MouseFilter = Control.MouseFilterEnum.Pass;
        hbox.AddThemeConstantOverride("separation", 8);

        // 背包行的图标：LootItem 走 RefId（食物没有 RefId，由 ForLoot 转查那张统一的口粮图）。
        hbox.AddChild(ItemIconTextures.MakeIconForLoot(loot));

        var name = new Label();
        name.Text = LootDisplay.NameOf(loot);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.AddThemeFontSizeOverride("font_size", 14);
        name.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
        hbox.AddChild(name);

        var kg = new Label();
        kg.Text = $"{ItemWeights.OfLoot(loot):0.0} kg";
        kg.CustomMinimumSize = new Vector2(70, 0);
        kg.HorizontalAlignment = HorizontalAlignment.Right;
        kg.AddThemeFontSizeOverride("font_size", 13);
        kg.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.5f));
        hbox.AddChild(kg);

        var drop = new Button();
        drop.Text = "扔掉";
        drop.CustomMinimumSize = new Vector2(64, 28);
        int captured = index;
        drop.Pressed += () => BagDropRequested?.Invoke(captured);
        UiStyle.StyleButton(drop, new Color(0.5f, 0.3f, 0.2f));
        hbox.AddChild(drop);

        return hbox;
    }

    /// <summary>
    /// 刷新并展示库存内容。<paramref name="foodPortions"/> = 当前营地食物份数（搜到的食物归此，不列在物品里）；
    /// <paramref name="notice"/> = 可空的一行搜刮反馈；<paramref name="isBookRead"/> = 按书 id 查是否已读（标「已读」）。
    /// </summary>
    public void ShowStash(InventoryStore inventory, int foodPortions, string? notice, Func<string, bool> isBookRead)
    {
        _foodLabel.Text = $"食物：{foodPortions} 份";
        _foodLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.72f, 0.6f));
        _noticeLabel.Text = notice ?? "";
        _descLabel.Text = "";
        UiStyle.ClearChildren(_listContainer);

        AddSection("武器", inventory.Weapons, isBookRead);
        AddAmmoSection(inventory);
        AddSection("护甲", inventory.Armors, isBookRead);
        AddBruceGearSection();
        AddSection("光源", inventory.ByCategory(ItemCategory.Light), isBookRead);
        AddSection("书", inventory.Books, isBookRead);
        AddSection("食物", inventory.Foods, isBookRead);
        AddMaterialsSection(inventory);

        if (_listContainer.GetChildCount() == 0)
        {
            var empty = new Label();
            empty.Text = "空空如也。";
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.5f));
            _listContainer.AddChild(empty);
        }
    }

    /// <summary>
    /// 弹药分区（批次18）：紧跟「武器」之后——枪的战力现在完全由弹药供给决定，这个数字得和枪摆在一起看。
    /// 弹药是可堆叠材料，库存里可能散成好几堆，故按弹药类型**合计**成一行，而非逐堆列出。
    /// 余量为 0 的弹药类型仍然列出（灰字）：让"我打空了"这件事可见，而不是让那一行凭空消失。
    /// </summary>
    private void AddAmmoSection(InventoryStore inventory)
    {
        var ammoDefs = Materials.InCategory(MaterialCategory.Ammo).ToList();
        var carried = ammoDefs
            .Select(def => (Def: def, Count: inventory.MaterialCount(def.Key)))
            .Where(x => x.Count > 0)
            .ToList();

        if (carried.Count == 0)
        {
            return;
        }

        var head = new Label();
        head.Text = "弹药";
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.55f));
        _listContainer.AddChild(head);

        foreach ((MaterialDef def, int count) in carried)
        {
            _listContainer.AddChild(BuildRow(def.ToItem(count), _ => false));
        }
    }

    /// <summary>
    /// 通用材料总览区（造/宰/搜刮攒下的原料余量）：木/布/金属/精密零件/皮/化学/有机杂料/药材八类
    /// （<see cref="MaterialCategory"/>），逐类合计成一行。此前库存面板只列成品（武器/护甲/书/食物）
    /// 与弹药，背着一堆铁料木料零件却看不见数量——玩家查材料余量的唯一入口是制作面板逐配方"够/缺"行。
    /// <para>
    /// 刻意排除的三类：<b>弹药</b>（已由 <see cref="AddAmmoSection"/> 单列，枪的战力与弹药得摆一起看）、
    /// <b>货币</b>（白银是交易媒介不是原料）、<b>食材</b>（生料另归烹饪那条线）。余量为 0 的不列（不占屏）。
    /// </para>
    /// </summary>
    private void AddMaterialsSection(InventoryStore inventory)
    {
        // 通用制作原料的八类（顺序即展示序）；弹药/货币/食材刻意不在此列（见 doc）。
        MaterialCategory[] overviewCategories =
        {
            MaterialCategory.Wood, MaterialCategory.Cloth, MaterialCategory.Metal, MaterialCategory.Component,
            MaterialCategory.Leather, MaterialCategory.Chemical, MaterialCategory.Misc, MaterialCategory.Medical,
        };

        var carried = overviewCategories
            .SelectMany(Materials.InCategory)
            .Select(def => (Def: def, Count: inventory.MaterialCount(def.Key)))
            .Where(x => x.Count > 0)
            .ToList();

        if (carried.Count == 0)
        {
            return;
        }

        var head = new Label();
        head.Text = "材料";
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.55f));
        _listContainer.AddChild(head);

        foreach ((MaterialDef def, int count) in carried)
        {
            _listContainer.AddChild(BuildRow(def.ToItem(count), _ => false));
        }
    }

    /// <summary>
    /// 布鲁斯装备区：列出布鲁斯当前穿戴的狗装备（身体 + 头 2 槽），每行一个「脱下」→ 退回库存。
    /// 穿戴入口是「护甲」区里那些狗装备件的「装备」按钮（走 <see cref="EquipRequested"/>，CampMain 分流到布鲁斯）；
    /// 本区专管**脱下**——穿上后装备离开库存，不脱下就无处可见、也回收不了。布鲁斯不在场/未穿戴则整区不出现。
    /// </summary>
    private void AddBruceGearSection()
    {
        IReadOnlyCollection<string>? worn = DogGearProvider?.Invoke();
        if (worn is null || worn.Count == 0)
        {
            return;
        }

        var head = new Label();
        head.Text = "布鲁斯装备";
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.55f));
        _listContainer.AddChild(head);

        foreach (string key in worn)
        {
            _listContainer.AddChild(BuildBruceGearRow(key));
        }
    }

    /// <summary>一行布鲁斯已穿装备：图标 + 名 + 「脱下」按钮（emit 装备键 → CampMain 从布鲁斯脱下退库）。</summary>
    private HBoxContainer BuildBruceGearRow(string gearKey)
    {
        DogGearDef? def = DogGearCatalog.Get(gearKey);
        string displayName = def?.DisplayName ?? gearKey;

        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.MouseFilter = Control.MouseFilterEnum.Pass;
        hbox.AddThemeConstantOverride("separation", 8);

        string desc = def?.Description ?? "";
        hbox.MouseEntered += () => _descLabel.Text = desc;

        hbox.AddChild(ItemIconTextures.MakeIconForRefKey(gearKey, 32));

        var nameLabel = new Label();
        nameLabel.Text = displayName;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.CustomMinimumSize = new Vector2(320, 30);
        nameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
        nameLabel.MouseEntered += () => _descLabel.Text = desc;
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size", 14);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
        hbox.AddChild(nameLabel);

        var offBtn = new Button();
        offBtn.Text = "脱下";
        offBtn.CustomMinimumSize = new Vector2(120, 30);
        offBtn.TooltipText = "从布鲁斯身上脱下并放回营地库存";
        offBtn.Pressed += () => DogGearUnequipRequested?.Invoke(gearKey);
        UiStyle.StyleButton(offBtn, new Color(0.5f, 0.42f, 0.3f), fontSize: 13);
        hbox.AddChild(offBtn);

        return hbox;
    }

    private void AddSection(string header, IEnumerable<Item> items, Func<string, bool> isBookRead)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var head = new Label();
        head.Text = header;
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.55f));
        _listContainer.AddChild(head);

        foreach (Item item in list)
        {
            _listContainer.AddChild(BuildRow(item, isBookRead));
        }
    }

    private HBoxContainer BuildRow(Item item, Func<string, bool> isBookRead)
    {
        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.MouseFilter = Control.MouseFilterEnum.Pass;
        hbox.AddThemeConstantOverride("separation", 8);

        // 悬停整行（或其名字标签）时，把该物品描述打到底部 _descLabel。无描述则清空。
        string desc = item.Description ?? "";
        hbox.MouseEntered += () => _descLabel.Text = desc;

        // 物品图标。没配图标/PNG 还没生成的物品显示占位图，行高与对齐都不受影响。
        hbox.AddChild(ItemIconTextures.MakeIcon(item));

        var nameLabel = new Label();
        // 数量后缀：食物按份、材料（含弹药）按堆叠数。其余（武器/护甲/书/光源）单件无数量。
        string suffix = item.Category switch
        {
            ItemCategory.Food => $" ×{item.FoodQuantity}",
            ItemCategory.Material => $" ×{item.MaterialQuantity}",
            _ => "",
        };
        string readTag = item.Category == ItemCategory.Book && item.RefKey != null && isBookRead(item.RefKey)
            ? "（已读）"
            : "";
        nameLabel.Text = $"{item.DisplayName}{suffix}{readTag}";
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.CustomMinimumSize = new Vector2(320, 30); // 给图标与间距留位
        nameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
        nameLabel.MouseEntered += () => _descLabel.Text = desc;
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size", 14);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
        hbox.AddChild(nameLabel);

        switch (item.Category)
        {
            case ItemCategory.Book:
                var readBtn = new Button();
                readBtn.Text = "阅读";
                readBtn.CustomMinimumSize = new Vector2(120, 30);
                string bookId = item.RefKey ?? "";
                readBtn.Pressed += () => BookOpenRequested?.Invoke(bookId);
                UiStyle.StyleButton(readBtn, new Color(0.35f, 0.5f, 0.55f), fontSize: 13);
                hbox.AddChild(readBtn);
                break;

            case ItemCategory.Weapon:
            case ItemCategory.Armor:
                var equipBtn = new Button();
                string refKey = item.RefKey ?? "";
                // 狗装备（Item.Armor，键∈DogGearCatalog）穿到布鲁斯身上；其余护甲/武器穿到选中幸存者。
                // 两者共用「装备」按钮与 EquipRequested，由 CampMain 按 DogGearCatalog.IsDogGear 分流。
                bool isDogGear = DogGearCatalog.IsDogGear(refKey);
                equipBtn.Text = isDogGear ? "给布鲁斯穿" : "装备";
                equipBtn.CustomMinimumSize = new Vector2(120, 30);
                equipBtn.TooltipText = isDogGear
                    ? "给布鲁斯穿上（须道格与布鲁斯在营）"
                    : "装备到当前选中的幸存者";
                equipBtn.Pressed += () => EquipRequested?.Invoke(refKey);
                UiStyle.StyleButton(equipBtn, new Color(0.5f, 0.45f, 0.3f), fontSize: 13);
                hbox.AddChild(equipBtn);
                break;

            case ItemCategory.Light:
                var holdBtn = new Button();
                holdBtn.Text = "持起";
                holdBtn.CustomMinimumSize = new Vector2(120, 30);
                holdBtn.TooltipText = "让当前选中的幸存者持起（占一只手，与双手武器互斥）";
                string lightKey = item.RefKey ?? "";
                holdBtn.Pressed += () => LightHoldRequested?.Invoke(lightKey);
                UiStyle.StyleButton(holdBtn, new Color(0.5f, 0.42f, 0.22f), fontSize: 13);
                hbox.AddChild(holdBtn);
                break;

            default:
                // 食物：无操作，占位一个等宽空白使列对齐。
                var spacer = new Control();
                spacer.CustomMinimumSize = new Vector2(120, 30);
                hbox.AddChild(spacer);
                break;
        }

        AddPlaceButton(hbox, item);
        AddSalvageButton(hbox, item);
        return hbox;
    }

    /// <summary>
    /// 「摆放」按钮——玩家能自己往地上摆的东西（沙袋 / 床 / 桌子）。点它进入放置模式，再点地面落位
    /// （<c>CampMain.CheckFurniturePlacement</c> 校验位置：不许贴大门/围栏的禁建带）。
    /// <para>
    /// ⚠️ <b>谁能摆，问 <see cref="PlaceableItems"/> 这一张表</b>，别在这儿再硬写一遍 key ——
    /// 那正是**床此前摆不下去**的根因：<c>CampMain.OnStashPlaceRequested</c> 早就接好了床的分支，
    /// 而这儿硬写着"只有沙袋"，于是床造出来只能躺在库存里，按钮压根不长出来。
    /// </para>
    /// <para>
    /// ⚠️ <b>改装台 / 烹饪台刻意不在那张表里</b>：用户拍板它们是营地内的**固定位置**设施（车间/厨房），
    /// 造好即自动立在锚点上、**不进库存** ⇒ 玩家根本没有"摆放"这个动作。别好心给它们加回按钮：
    /// 那会把已经撤掉的 kill box 风险重新引进来（它们实心、挖导航洞、不可跨越）。
    /// </para>
    /// </summary>
    private void AddPlaceButton(HBoxContainer hbox, Item item)
    {
        string key = item.RefKey ?? "";
        if (!PlaceableItems.IsPlaceable(key))
        {
            hbox.AddChild(new Control { CustomMinimumSize = new Vector2(76, 30) });
            return;
        }

        var btn = new Button();
        btn.Text = "摆放";
        btn.CustomMinimumSize = new Vector2(76, 30);
        btn.TooltipText = PlaceTooltipOf(key);
        btn.Pressed += () => PlaceRequested?.Invoke(key);
        UiStyle.StyleButton(btn, new Color(0.45f, 0.42f, 0.3f), fontSize: 13);
        hbox.AddChild(btn);
    }

    /// <summary>「摆放」按钮的悬停提示（各摆件说各自的话；说清它挡不挡路——那是玩家最容易误会的一点）。</summary>
    private static string PlaceTooltipOf(string key) => key switch
    {
        SandbagSpec.ItemKey =>
            "选好位置垒起来。它挡不住任何人走过去——只是让子弹更可能打在它身上，而不是你身上。\n"
            + "（贴着它才算数；敌人绕到你背后，它就白垒了——而且他们也能蹲在它后面。）",
        BedSpec.ItemKey =>
            "找块地方铺下。一张床只睡一个人，躺着的那个夜里不站岗、不生产。\n"
            + "（别贴着围栏和大门摆——墙根下那条道得留着。）",
        TableSpec.ItemKey =>
            $"摆在屋里。贴着它挨远程有 {(int)Math.Round(CoverLogic.DefaultCoverChance * 100)}% 无效——和沙袋一样，只是它进不了院子。\n"
            + "（它不挡路：谁都能跨过去，只是会慢一点。绕到你背后就白摆了，敌人也能蹲它后面。）",
        CropPlotSpec.ItemKey =>
            "翻在院子里（屋里种不出土豆）。摆好后选中角色右键前往下种——种一颗吃 1 土豆，种下就不用管，几个昼夜后回来收。\n"
            + "（别贴着围栏和大门摆——墙根那条道得留着。它不挡路，谁都跨得过去。）",
        TrapSpec.ItemKey =>
            $"支在院子里套老鼠和兔子。白天、夜里各自动查一次网——多摆几个能多抓，但每多一个，新那个的机会都更小（最低 {(int)Math.Round(TrapLogic.MinChance * 100)}%）。\n"
            + "（别贴着围栏和大门摆——墙根那条道得留着。它不挡路，谁都跨得过去。抓到的老鼠要先上案板，兔子能直接下锅。）",
        BirdTrapSpec.ItemKey =>
            "支在院子里张网捕鸟。白天、夜里各自动查一次网——鸟是羽毛的唯一来源（鸟→宰杀→鸟肉+羽毛→箭）。多摆递减同圈套。\n"
            + "（别贴着围栏和大门摆——墙根那条道得留着。它不挡路，谁都跨得过去。抓到的鸟得上案板才出肉和羽毛。）",
        _ => "选好位置摆下（右键作罢）。",
    };

    /// <summary>
    /// 「拆解」按钮：只挂给**拆得动**的东西（有单件产物配方者，见 <see cref="SalvageLogic.CanSalvageKey"/>）。
    /// 悬停提示里直接把返还清单摊开——玩家该在按下去之前就知道自己会拿回什么、以及拿不回什么。
    /// </summary>
    private void AddSalvageButton(HBoxContainer hbox, Item item)
    {
        string key = item.RefKey ?? "";
        RecipeData? recipe = SalvageLogic.RecipeFor(key);
        if (recipe is null)
        {
            hbox.AddChild(new Control { CustomMinimumSize = new Vector2(76, 30) });
            return;
        }

        var btn = new Button();
        btn.Text = "拆解";
        btn.CustomMinimumSize = new Vector2(76, 30);
        btn.TooltipText = SalvagePreview(recipe);
        btn.Pressed += () => SalvageRequested?.Invoke(key);
        UiStyle.StyleButton(btn, new Color(0.42f, 0.34f, 0.3f), fontSize: 13);
        hbox.AddChild(btn);
    }

    /// <summary>拆解预览文案：拆多久、拿回什么。木材那条会明说"废木料要配胶水才变得回木料"——这是本作最容易被误解的一条规则。</summary>
    private static string SalvagePreview(RecipeData recipe)
    {
        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOfRecipe(recipe);
        if (yield.Count == 0)
        {
            return $"拆 {SalvageLogic.WorkMinutesOf(recipe)} 分钟，一点渣都剩不下——太小了。";
        }

        string list = string.Join("、", yield.Select(kv => $"{Materials.Find(kv.Key)?.DisplayName ?? kv.Key}×{kv.Value}"));
        string tail = yield.ContainsKey(SalvageLogic.ScrapWoodKey)
            ? "\n（废木料得在锯片工作台上配胶水，才粘得回木料）"
            : "";
        return $"拆 {SalvageLogic.WorkMinutesOf(recipe)} 分钟，拿回：{list}{tail}";
    }
}
