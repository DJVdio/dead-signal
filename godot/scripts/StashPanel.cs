using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 营地共享库存面板（模态）：按类别列出 <see cref="InventoryStore"/> 的武器/护甲/书/食物，
/// 顶部显示食物份数与可选的一行通知（搜刮反馈）。书行有「阅读」按钮（触发 <see cref="BookOpenRequested"/>）；
/// 可装备项（武器/护甲）列出并留一个**禁用态**「装备」按钮作 W3b 占位（装备逻辑不在本面板做）。
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
    /// 刷新并展示库存内容。<paramref name="foodPortions"/> = 当前营地食物份数（搜到的食物归此，不列在物品里）；
    /// <paramref name="notice"/> = 可空的一行搜刮反馈；<paramref name="isBookRead"/> = 按书 id 查是否已读（标「已读」）。
    /// </summary>
    public void ShowStash(InventoryStore inventory, int foodPortions, string? notice, Func<string, bool> isBookRead)
    {
        _foodLabel.Text = $"食物：{foodPortions} 份";
        _noticeLabel.Text = notice ?? "";
        _descLabel.Text = "";
        UiStyle.ClearChildren(_listContainer);

        AddSection("武器", inventory.Weapons, isBookRead);
        AddSection("护甲", inventory.Armors, isBookRead);
        AddSection("光源", inventory.ByCategory(ItemCategory.Light), isBookRead);
        AddSection("书", inventory.Books, isBookRead);
        AddSection("食物", inventory.Foods, isBookRead);

        if (_listContainer.GetChildCount() == 0)
        {
            var empty = new Label();
            empty.Text = "空空如也。";
            empty.AddThemeFontSizeOverride("font_size", 14);
            empty.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.5f));
            _listContainer.AddChild(empty);
        }
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

        var nameLabel = new Label();
        string suffix = item.Category == ItemCategory.Food ? $" ×{item.FoodQuantity}" : "";
        string readTag = item.Category == ItemCategory.Book && item.RefKey != null && isBookRead(item.RefKey)
            ? "（已读）"
            : "";
        nameLabel.Text = $"{item.DisplayName}{suffix}{readTag}";
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.CustomMinimumSize = new Vector2(360, 30);
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
                equipBtn.Text = "装备";
                equipBtn.CustomMinimumSize = new Vector2(120, 30);
                equipBtn.TooltipText = "装备到当前选中的幸存者";
                string refKey = item.RefKey ?? "";
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

        return hbox;
    }
}
