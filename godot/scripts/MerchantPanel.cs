using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 神秘商人交易面板（模态）：**购买/出售双页签**。
/// 购买页=货架列表（物品名/价格/库存）+ 每行买入按钮；出售页=玩家库存里**白名单可收购**之物（食物/材料/医疗品）
/// 聚合行（名/持有量/单位收购价=基准价60%）+ 每行卖出按钮。顶部页签切换，共享玩家持币量显示。
/// 结算由 CampMain 侧订阅 <see cref="BuyRequested"/> / <see cref="SellRequested"/> 走 <c>MerchantTrade</c> 实扣实产，再 <see cref="Show"/> 刷新。
/// 骨架照抄 <see cref="ExpeditionPanel"/>（列表行 + 按钮 + 灰显 + 事件回调），着色思路取自制作面板"够/不够"。
/// </summary>
public sealed partial class MerchantPanel : CanvasLayer
{
    /// <summary>当前页签。</summary>
    private enum Tab { Buy, Sell }

    private Control _root = null!;
    private VBoxContainer _listContainer = null!;
    private Label _emptyHint = null!;
    private Label _currencyLabel = null!;
    private Button _closeBtn = null!;
    private Button _buyTabBtn = null!;
    private Button _sellTabBtn = null!;

    private MerchantShelf? _shelf;
    private IReadOnlyList<SellRow> _sellRows = Array.Empty<SellRow>();
    private int _currencyOwned;
    private Tab _activeTab = Tab.Buy;

    /// <summary>玩家请求买入某条货架条目（参数=货架 <c>Offers</c> 下标）。CampMain 结算后应回调 <see cref="Show"/> 刷新。</summary>
    public event Action<int>? BuyRequested;

    /// <summary>玩家请求卖出某收购行的一单位（参数=该 <see cref="SellRow"/>，其 <c>UnitItem</c> 供 <c>MerchantTrade.SellOne</c>）。</summary>
    public event Action<SellRow>? SellRequested;

    /// <summary>关闭面板。</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "MerchantPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(560, 440),
            borderColor: new Color(0.28f, 0.34f, 0.42f)); // 青灰边：呼应商人中立色

        var title = new Label();
        title.Text = "神秘商人";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.82f, 0.88f, 0.95f));
        panel.AddChild(title);

        var flavor = new Label();
        flavor.Text = "「东西不多，都是硬货。只收白银——也收你手上的余粮杂料。」";
        flavor.Position = new Vector2(24, 46);
        flavor.AddThemeFontSizeOverride("font_size", 13);
        flavor.AddThemeColorOverride("font_color", new Color(0.6f, 0.62f, 0.6f));
        panel.AddChild(flavor);

        _currencyLabel = new Label();
        _currencyLabel.Position = new Vector2(24, 72);
        _currencyLabel.AddThemeFontSizeOverride("font_size", 15);
        _currencyLabel.AddThemeColorOverride("font_color", UiStyle.Warning);
        panel.AddChild(_currencyLabel);

        // 购买/出售 页签按钮（顶部右侧）。
        _buyTabBtn = new Button();
        _buyTabBtn.Text = "购买";
        _buyTabBtn.Position = new Vector2(300, 64);
        _buyTabBtn.Size = new Vector2(114, 30);
        _buyTabBtn.Pressed += () => SwitchTab(Tab.Buy);
        panel.AddChild(_buyTabBtn);

        _sellTabBtn = new Button();
        _sellTabBtn.Text = "出售";
        _sellTabBtn.Position = new Vector2(422, 64);
        _sellTabBtn.Size = new Vector2(114, 30);
        _sellTabBtn.Pressed += () => SwitchTab(Tab.Sell);
        panel.AddChild(_sellTabBtn);

        var listBg = new Panel();
        listBg.Position = new Vector2(24, 100);
        listBg.Size = new Vector2(512, 270);
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
        scroll.AddChild(_listContainer);

        _emptyHint = new Label();
        _emptyHint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.VerticalAlignment = VerticalAlignment.Center;
        _emptyHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _emptyHint.AddThemeFontSizeOverride("font_size", 14);
        _emptyHint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.55f));
        _emptyHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _emptyHint.Visible = false;
        listBg.AddChild(_emptyHint);

        _closeBtn = new Button();
        _closeBtn.Text = "离开";
        _closeBtn.Position = new Vector2(416, 388);
        _closeBtn.Size = new Vector2(120, 38);
        _closeBtn.Pressed += () => Closed?.Invoke();
        UiStyle.StyleButton(_closeBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_closeBtn);
    }

    /// <summary>
    /// 用当前货架 + 玩家可收购库存行 + 持币量刷新面板（保持当前页签）。每次买入/卖出结算后 CampMain 再调一次，令持币/库存/灰显即时更新。
    /// </summary>
    public void Show(MerchantShelf shelf, IReadOnlyList<SellRow> sellRows, int currencyOwned)
    {
        _shelf = shelf;
        _sellRows = sellRows ?? Array.Empty<SellRow>();
        _currencyOwned = currencyOwned;
        Render();
    }

    private void SwitchTab(Tab tab)
    {
        if (_activeTab == tab)
        {
            return;
        }
        _activeTab = tab;
        Render();
    }

    private void Render()
    {
        string coinName = Materials.Find(Materials.CurrencyKey)?.DisplayName ?? "白银";
        _currencyLabel.Text = $"持有{coinName}：{_currencyOwned}";

        // 页签选中态：激活页高亮青、非激活暗。
        UiStyle.StyleButton(_buyTabBtn, _activeTab == Tab.Buy ? new Color(0.3f, 0.5f, 0.6f) : new Color(0.22f, 0.24f, 0.28f));
        UiStyle.StyleButton(_sellTabBtn, _activeTab == Tab.Sell ? new Color(0.3f, 0.5f, 0.6f) : new Color(0.22f, 0.24f, 0.28f));

        UiStyle.ClearChildren(_listContainer);
        if (_activeTab == Tab.Buy)
        {
            RenderBuyList(coinName);
        }
        else
        {
            RenderSellList(coinName);
        }
    }

    // —— 购买页 ——
    private void RenderBuyList(string coinName)
    {
        int count = _shelf?.Offers.Count ?? 0;
        _emptyHint.Text = "货架空空——这一趟没什么可卖的了。";
        _emptyHint.Visible = count == 0;

        for (int i = 0; i < count; i++)
        {
            MerchantOffer offer = _shelf!.Offers[i];
            PurchaseCheck check = MerchantTrade.Check(offer, _currencyOwned);

            HBoxContainer hbox = MakeRow(offer.Good.DisplayName);

            var priceLabel = new Label();
            priceLabel.Text = $"{offer.Price} {coinName}";
            priceLabel.CustomMinimumSize = new Vector2(110, 32);
            priceLabel.VerticalAlignment = VerticalAlignment.Center;
            priceLabel.AddThemeFontSizeOverride("font_size", 14);
            // 买得起=绿、买不起=红（语义色只映射真实判定）。
            priceLabel.AddThemeColorOverride("font_color", check.CanBuy ? UiStyle.Success : UiStyle.Danger);
            hbox.AddChild(priceLabel);

            var stockLabel = new Label();
            stockLabel.Text = offer.SoldOut ? "已售罄" : $"库存 {offer.Stock}";
            stockLabel.CustomMinimumSize = new Vector2(80, 32);
            stockLabel.VerticalAlignment = VerticalAlignment.Center;
            stockLabel.AddThemeFontSizeOverride("font_size", 13);
            stockLabel.AddThemeColorOverride("font_color",
                offer.SoldOut ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.7f, 0.7f, 0.65f));
            hbox.AddChild(stockLabel);

            var buyBtn = new Button();
            buyBtn.CustomMinimumSize = new Vector2(96, 32);
            int idx = i; // 捕获稳定下标
            buyBtn.Pressed += () => BuyRequested?.Invoke(idx);
            switch (check.Status)
            {
                case PurchaseStatus.Ok:
                    buyBtn.Text = "买入";
                    buyBtn.Disabled = false;
                    break;
                case PurchaseStatus.SoldOut:
                    buyBtn.Text = "已售罄";
                    buyBtn.Disabled = true;
                    break;
                case PurchaseStatus.NotEnoughMoney:
                    buyBtn.Text = $"差{check.Shortfall}"; // 缺口提示
                    buyBtn.Disabled = true;
                    break;
            }
            UiStyle.StyleButton(buyBtn, new Color(0.3f, 0.5f, 0.6f));
            hbox.AddChild(buyBtn);

            AppendRow(hbox);
        }
    }

    // —— 出售页 ——
    private void RenderSellList(string coinName)
    {
        int count = _sellRows.Count;
        _emptyHint.Text = "你身上没有商人肯收的东西——他只要食物、材料和药品。";
        _emptyHint.Visible = count == 0;

        for (int i = 0; i < count; i++)
        {
            SellRow row = _sellRows[i];
            HBoxContainer hbox = MakeRow(row.DisplayName);

            var priceLabel = new Label();
            priceLabel.Text = $"{row.UnitSellPrice} {coinName}/个";
            priceLabel.CustomMinimumSize = new Vector2(110, 32);
            priceLabel.VerticalAlignment = VerticalAlignment.Center;
            priceLabel.AddThemeFontSizeOverride("font_size", 14);
            priceLabel.AddThemeColorOverride("font_color", UiStyle.Success); // 卖出=进账，恒绿
            hbox.AddChild(priceLabel);

            var ownedLabel = new Label();
            ownedLabel.Text = $"持有 {row.OwnedCount}";
            ownedLabel.CustomMinimumSize = new Vector2(80, 32);
            ownedLabel.VerticalAlignment = VerticalAlignment.Center;
            ownedLabel.AddThemeFontSizeOverride("font_size", 13);
            ownedLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.65f));
            hbox.AddChild(ownedLabel);

            var sellBtn = new Button();
            sellBtn.CustomMinimumSize = new Vector2(96, 32);
            sellBtn.Text = "卖出";
            SellRow captured = row; // 捕获稳定行
            sellBtn.Pressed += () => SellRequested?.Invoke(captured);
            UiStyle.StyleButton(sellBtn, new Color(0.4f, 0.55f, 0.4f)); // 卖出=暖绿，与买入青蓝区分
            hbox.AddChild(sellBtn);

            AppendRow(hbox);
        }
    }

    // —— 行构造共用 ——
    private static HBoxContainer MakeRow(string name)
    {
        var hbox = new HBoxContainer();
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.MouseFilter = Control.MouseFilterEnum.Pass;

        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.CustomMinimumSize = new Vector2(200, 32);
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size", 15);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.88f, 0.85f, 0.78f));
        hbox.AddChild(nameLabel);
        return hbox;
    }

    private void AppendRow(HBoxContainer hbox)
    {
        var separator = new HSeparator();
        separator.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
        _listContainer.AddChild(hbox);
        _listContainer.AddChild(separator);
    }
}
