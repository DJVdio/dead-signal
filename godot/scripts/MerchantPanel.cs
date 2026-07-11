using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 神秘商人交易面板（模态）：货架列表（物品名 / 价格 / 库存）+ 玩家持币量 + 每行买入按钮。
/// 钱不够或已售罄 → 按钮灰显、价格标 <see cref="UiStyle.Danger"/>（红=不足）；买得起标 <see cref="UiStyle.Success"/>。
/// 结算由 CampMain 侧订阅 <see cref="BuyRequested"/> 走 <c>MerchantTrade.Buy</c> 实扣实产，再 <see cref="ShowShelf"/> 刷新。
/// 骨架照抄 <see cref="ExpeditionPanel"/>（列表行 + 按钮 + 灰显 + 事件回调），着色思路取自制作面板"够/不够"。
/// </summary>
public sealed partial class MerchantPanel : CanvasLayer
{
    private Control _root = null!;
    private VBoxContainer _listContainer = null!;
    private Label _emptyHint = null!;
    private Label _currencyLabel = null!;
    private Button _closeBtn = null!;

    private MerchantShelf? _shelf;

    /// <summary>玩家请求买入某条货架条目（参数=货架 <c>Offers</c> 下标）。CampMain 结算后应回调 <see cref="ShowShelf"/> 刷新。</summary>
    public event Action<int>? BuyRequested;

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
        flavor.Text = "「东西不多，都是硬货。只收白银。」";
        flavor.Position = new Vector2(24, 46);
        flavor.AddThemeFontSizeOverride("font_size", 13);
        flavor.AddThemeColorOverride("font_color", new Color(0.6f, 0.62f, 0.6f));
        panel.AddChild(flavor);

        _currencyLabel = new Label();
        _currencyLabel.Position = new Vector2(24, 72);
        _currencyLabel.AddThemeFontSizeOverride("font_size", 15);
        _currencyLabel.AddThemeColorOverride("font_color", UiStyle.Warning);
        panel.AddChild(_currencyLabel);

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
        _emptyHint.Text = "货架空空——这一趟没什么可卖的了。";
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
    /// 用当前货架 + 玩家持币量刷新面板。每次买入结算后 CampMain 再调一次，令持币量/库存/灰显即时更新。
    /// </summary>
    public void ShowShelf(MerchantShelf shelf, int currencyOwned)
    {
        _shelf = shelf;
        string coinName = Materials.Find(Materials.CurrencyKey)?.DisplayName ?? "白银";
        _currencyLabel.Text = $"持有{coinName}：{currencyOwned}";

        UiStyle.ClearChildren(_listContainer);
        int count = shelf.Offers.Count;
        _emptyHint.Visible = count == 0;

        for (int i = 0; i < count; i++)
        {
            MerchantOffer offer = shelf.Offers[i];
            PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned);

            var hbox = new HBoxContainer();
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.MouseFilter = Control.MouseFilterEnum.Pass;

            var nameLabel = new Label();
            nameLabel.Text = offer.Good.DisplayName;
            nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            nameLabel.CustomMinimumSize = new Vector2(200, 32);
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            nameLabel.AddThemeFontSizeOverride("font_size", 15);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.88f, 0.85f, 0.78f));
            hbox.AddChild(nameLabel);

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

            var separator = new HSeparator();
            separator.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
            _listContainer.AddChild(hbox);
            _listContainer.AddChild(separator);
        }
    }
}
