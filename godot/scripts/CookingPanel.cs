using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 烹饪台面板（模态）：往锅里放食材 → 开火。**只发事件、不直接执行**（执行由营地接入的服务做）。
/// 骨架照 <see cref="CraftingPanel"/>（CanvasLayer + <see cref="UiStyle.BuildModalShell"/>）；冻结/恢复时标由 CampMain 管。
///
/// <para>═══ ⚠️ <b>这个面板的核心设计是「什么都不告诉玩家」，别把它当 UX 缺陷修掉</b> ═══
/// 用户拍板：「<b>每种食材的热量点和制作一份食物需要的热量是玩家要自己探索试错的</b>」。
/// 所以本面板<b>刻意做不到</b>下面这些看起来很贴心的事：
/// <list type="bullet">
/// <item><b>不显示任何食材的热量点</b>——一行数字都没有。</item>
/// <item><b>不显示"一份要多少热量"</b>，也不显示当前锅里攒了多少。</item>
/// <item><b>不提示"还差 N 点"</b>：热量不够时，「开火」按钮就是灰的，<b>不给任何解释</b>
///       （连 tooltip 都没有——见 <see cref="CookingLogic.PlayerFacingText"/>，那个函数对"热量不够"返回 null 就是为此）。</item>
/// <item><b>不提示"你浪费了 N 点"</b>：放 31 点做出 1 份，多的 15 点静默蒸发。</item>
/// <item><b>不显示炊具的 -2 效果</b>——说了"装锅省 2 点"等于把整套热量制直接交底。槽位只写"已装/空"。</item>
/// <item><b>食材列表按名字排序，绝不按热量排序</b>——按热量排就是把答案免费送给玩家（见 <see cref="IngredientRows"/>）。</item>
/// </list>
/// <b>玩家能观察到的信号只有一个</b>：产物那一行的「食物 ×N」（N = 这锅能出几份）。
/// 这也是用户原话里唯一允许的反馈（"UI 上显示产物食物*2"）。
/// <br/>⚠️ 本仓有「首次触发一行提示」框架（hint-system）——<b>烹饪刻意不接它</b>，别顺手给它加解释性提示。
/// （数值本身给用户/wiki 看：wiki 是开发者调数值的设计表，游戏内 UI 是玩家的试错场，两者不矛盾。）
/// </para>
/// </summary>
public sealed partial class CookingPanel : CanvasLayer
{
    /// <summary>点「开火」：emit (锅里的食材：材料键 → 个数, 掌勺的人)。营地接入据此扣料 + 起工时任务。</summary>
    public event Action<IReadOnlyDictionary<string, int>, Pawn>? CookRequested;

    /// <summary>点某个槽的「安装」：emit 槽位。营地接入据此从库存扣一件炊具、装进烹饪台。</summary>
    public event Action<CookwareSlot>? CookwareInstallRequested;

    /// <summary>点某个槽的「卸下」：emit 槽位。营地接入据此把炊具还回库存。</summary>
    public event Action<CookwareSlot>? CookwareRemoveRequested;

    /// <summary>点「关闭」：CampMain 据此隐藏面板并恢复时标。</summary>
    public event Action? Closed;

    // ---- ShowFor 传入的只读依赖 ----
    private CookStationState _station = new();
    private IReadOnlyList<Pawn> _cooks = Array.Empty<Pawn>();
    private InventoryStore _inventory = new();
    private bool _hasStation;
    private CraftingJob? _activeJob;   // 全营单任务队列（与制作/拆解/改装互斥）：非 null ⇒ 有人正忙，不接新单

    // ---- 面板自身瞬态：锅里放了什么（材料键 → 个数），关面板不清（玩家配到一半去翻库存是常事）----
    private readonly Dictionary<string, int> _pot = new();
    private Pawn? _selectedCook;

    // ---- 控件引用 ----
    private Control _root = null!;
    private OptionButton _cookDropdown = null!;
    private VBoxContainer _slotContainer = null!;
    private VBoxContainer _listContainer = null!;
    private Label _outputLabel = null!;
    private Label _footerLabel = null!;
    private Button _cookBtn = null!;

    private static readonly Color TextColor = new(0.85f, 0.82f, 0.75f);
    private static readonly Color DimColor = new(0.55f, 0.55f, 0.5f);
    private static readonly Color HeadColor = new(0.72f, 0.68f, 0.55f);
    private static readonly Color OkColor = UiStyle.Success;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "CookingPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(680, 600),
            borderColor: new Color(0.30f, 0.22f, 0.16f));

        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        var title = new Label();
        title.Text = CookStation.PropName;
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        var cookCaption = new Label();
        cookCaption.Text = "掌勺";
        cookCaption.Position = new Vector2(24, 54);
        cookCaption.AddThemeFontSizeOverride("font_size", 14);
        cookCaption.AddThemeColorOverride("font_color", DimColor);
        panel.AddChild(cookCaption);

        _cookDropdown = new OptionButton();
        _cookDropdown.Position = new Vector2(84, 50);
        _cookDropdown.Size = new Vector2(220, 30);
        _cookDropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        _cookDropdown.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
        _cookDropdown.ItemSelected += OnCookSelected;
        panel.AddChild(_cookDropdown);

        // 炊具槽区（两个槽：锅 / 烤架）。**只写"已装/空"，一个数字都不写。**
        _slotContainer = new VBoxContainer();
        _slotContainer.Position = new Vector2(330, 44);
        _slotContainer.Size = new Vector2(326, 44);
        _slotContainer.AddThemeConstantOverride("separation", 2);
        panel.AddChild(_slotContainer);

        // 食材列表（滚动）
        var listBg = new Panel();
        listBg.Position = new Vector2(24, 96);
        listBg.Size = new Vector2(632, 396);
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

        // 产物行：**玩家唯一能拿到的信号**（「食物 ×N」）。
        _outputLabel = new Label();
        _outputLabel.Position = new Vector2(24, 500);
        _outputLabel.Size = new Vector2(300, 28);
        _outputLabel.AddThemeFontSizeOverride("font_size", 17);
        _outputLabel.AddThemeColorOverride("font_color", TextColor);
        panel.AddChild(_outputLabel);

        _footerLabel = new Label();
        _footerLabel.Position = new Vector2(24, 530);
        _footerLabel.Size = new Vector2(460, 44);
        _footerLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _footerLabel.AddThemeFontSizeOverride("font_size", 12);
        _footerLabel.AddThemeColorOverride("font_color", DimColor);
        panel.AddChild(_footerLabel);

        _cookBtn = new Button();
        _cookBtn.Text = "开火";
        _cookBtn.Position = new Vector2(360, 500);
        _cookBtn.Size = new Vector2(140, 36);
        _cookBtn.Pressed += OnCookPressed;
        UiStyle.StyleButton(_cookBtn, new Color(0.55f, 0.35f, 0.2f), fontSize: 16);
        panel.AddChild(_cookBtn);

        var closeBtn = new Button();
        closeBtn.Text = "关闭";
        closeBtn.Position = new Vector2(516, 544);
        closeBtn.Size = new Vector2(140, 40);
        closeBtn.Pressed += () => Closed?.Invoke();
        UiStyle.StyleButton(closeBtn, new Color(0.5f, 0.4f, 0.3f), fontSize: 16);
        panel.AddChild(closeBtn);
    }

    /// <summary>
    /// 打开/刷新面板。<paramref name="activeJob"/> = 全营在制任务（非 null ⇒ 有人正在干活，不接新单）。
    /// </summary>
    public void ShowFor(
        CookStationState station,
        IReadOnlyList<Pawn> cooks,
        InventoryStore inventory,
        bool hasStation,
        CraftingJob? activeJob = null)
    {
        _station = station ?? new CookStationState();
        _cooks = cooks ?? Array.Empty<Pawn>();
        _inventory = inventory ?? new InventoryStore();
        _hasStation = hasStation;
        _activeJob = activeJob;

        if (_selectedCook is null || !_cooks.Contains(_selectedCook))
        {
            _selectedCook = _cooks.FirstOrDefault();
        }
        PopulateCookDropdown();

        ClampPotToInventory();
        Rebuild();
    }

    /// <summary>锅里的量不许超过库存现有量（做完一锅、或别处消耗掉食材后，回来时自动收敛）。</summary>
    private void ClampPotToInventory()
    {
        foreach (string key in _pot.Keys.ToList())
        {
            int have = _inventory.MaterialCount(key);
            if (have <= 0) _pot.Remove(key);
            else if (_pot[key] > have) _pot[key] = have;
        }
    }

    private void PopulateCookDropdown()
    {
        _cookDropdown.Clear();
        for (int i = 0; i < _cooks.Count; i++)
        {
            _cookDropdown.AddItem(_cooks[i].DisplayName, _cooks[i].Id);
        }
        if (_selectedCook is not null)
        {
            int idx = _cookDropdown.GetItemIndex(_selectedCook.Id);
            if (idx >= 0) _cookDropdown.Selected = idx;
        }
        _cookDropdown.Disabled = _cooks.Count == 0;
    }

    private void OnCookSelected(long index)
    {
        int id = _cookDropdown.GetItemId((int)index);
        _selectedCook = _cooks.FirstOrDefault(p => p.Id == id);
        Rebuild();
    }

    /// <summary>
    /// 库存里有的食材（<b>按显示名排序</b>）。
    /// <para>⚠️ <b>绝不按热量排序</b>：那等于把"哪种最顶饱"的答案免费打印在屏幕上，
    /// 而这正是用户要玩家自己试出来的东西。</para>
    /// </summary>
    private List<(string Key, string Name, int Have)> IngredientRows()
        => FoodCalories.All
            .Select(f => (Key: f.Key, Def: Materials.Find(f.Key)))
            .Where(x => x.Def is not null)
            .Select(x => (x.Key, Name: x.Def!.Value.DisplayName, Have: _inventory.MaterialCount(x.Key)))
            .Where(x => x.Have > 0 || _pot.ContainsKey(x.Key))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

    private void Rebuild()
    {
        BuildSlots();
        BuildIngredientList();
        BuildOutputRow();
    }

    // ================= 炊具槽（两个：锅 / 烤架）=================

    private void BuildSlots()
    {
        UiStyle.ClearChildren(_slotContainer);
        foreach (CookwareSlot slot in Enum.GetValues<CookwareSlot>())
        {
            bool installed = _station.Has(slot);
            string itemKey = CookStation.ItemKeyOf(slot);
            int inStash = _inventory.MaterialCount(itemKey);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            // 炊具图标（锅/烤架）。没装的槽也照样显示图标——玩家得先认得出"我缺的是这个"。
            row.AddChild(ItemIconTextures.MakeIconForRefKey(itemKey, 24));

            var label = new Label();
            // **只说装了没装**——不说它省几点热量（说了就把整套热量制交底了）。
            label.Text = installed ? $"{DisplayNames.Of(slot)}：已装" : $"{DisplayNames.Of(slot)}：空";
            label.CustomMinimumSize = new Vector2(96, 24); // 让出 24px 图标 + 6px 间距
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_color", installed ? OkColor : DimColor);
            row.AddChild(label);

            var btn = new Button();
            btn.Text = installed ? "卸下" : "安装";
            btn.CustomMinimumSize = new Vector2(64, 24);
            // 没有烹饪台就没地方装；库里没这件炊具也装不上（这两条玩家都看得见，说了不泄密）。
            btn.Disabled = !_hasStation || (!installed && inStash <= 0);
            CookwareSlot captured = slot;
            btn.Pressed += () =>
            {
                if (installed) CookwareRemoveRequested?.Invoke(captured);
                else CookwareInstallRequested?.Invoke(captured);
            };
            UiStyle.StyleButton(btn, new Color(0.42f, 0.38f, 0.3f), fontSize: 12);
            row.AddChild(btn);

            _slotContainer.AddChild(row);
        }
    }

    // ================= 食材列表 =================

    private void BuildIngredientList()
    {
        UiStyle.ClearChildren(_listContainer);

        var head = new Label();
        head.Text = "食材（往锅里放）";
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", HeadColor);
        _listContainer.AddChild(head);

        List<(string Key, string Name, int Have)> rows = IngredientRows();
        if (rows.Count == 0)
        {
            var empty = new Label();
            empty.Text = "库里一样能吃的东西都没有。";
            empty.AddThemeFontSizeOverride("font_size", 13);
            empty.AddThemeColorOverride("font_color", DimColor);
            _listContainer.AddChild(empty);
            return;
        }

        foreach ((string key, string name, int have) in rows)
        {
            int put = _pot.TryGetValue(key, out int n) ? n : 0;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            // 食材图标（24px：这一行是 14 号字，32px 会把行撑高）。图标只认"这是什么东西"，
            // 不携带任何热量信息——热量制仍然靠玩家自己试。
            row.AddChild(ItemIconTextures.MakeIconForRefKey(key, 24));

            var nameLabel = new Label();
            // **只有名字和库存数**——库存数是玩家本来就看得见的（库存面板里就写着），不泄露任何热量信息。
            nameLabel.Text = $"{name}　（库存 {have}）";
            nameLabel.CustomMinimumSize = new Vector2(328, 24); // 让出 24px 图标 + 8px 间距
            nameLabel.AddThemeFontSizeOverride("font_size", 14);
            nameLabel.AddThemeColorOverride("font_color", put > 0 ? OkColor : TextColor);
            row.AddChild(nameLabel);

            var minus = new Button();
            minus.Text = "−";
            minus.CustomMinimumSize = new Vector2(34, 24);
            minus.Disabled = put <= 0;
            string k1 = key;
            minus.Pressed += () => AdjustPot(k1, -1);
            UiStyle.StyleButton(minus, new Color(0.42f, 0.34f, 0.3f), fontSize: 14);
            row.AddChild(minus);

            var count = new Label();
            count.Text = put.ToString();
            count.CustomMinimumSize = new Vector2(40, 24);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            count.AddThemeFontSizeOverride("font_size", 14);
            count.AddThemeColorOverride("font_color", put > 0 ? OkColor : DimColor);
            row.AddChild(count);

            var plus = new Button();
            plus.Text = "＋";
            plus.CustomMinimumSize = new Vector2(34, 24);
            plus.Disabled = put >= have;
            string k2 = key;
            plus.Pressed += () => AdjustPot(k2, +1);
            UiStyle.StyleButton(plus, new Color(0.4f, 0.46f, 0.32f), fontSize: 14);
            row.AddChild(plus);

            _listContainer.AddChild(row);
        }
    }

    private void AdjustPot(string key, int delta)
    {
        int have = _inventory.MaterialCount(key);
        int now = _pot.TryGetValue(key, out int n) ? n : 0;
        int next = Math.Clamp(now + delta, 0, have);
        if (next <= 0) _pot.Remove(key);
        else _pot[key] = next;
        Rebuild();
    }

    // ================= 产物行 + 开火 =================

    private void BuildOutputRow()
    {
        CookPlan plan = CookingLogic.Plan(
            _hasStation, _pot, _station.Installed, _inventory.MaterialCount);

        // ★★ 玩家唯一能观察到的信号：产物「食物 ×N」。N=0 时照样显示 ×0（而按钮灰着）——
        //    **绝不在这里补一句"还差 4 点"**，那正是本机制要拒绝的东西。
        _outputLabel.Text = $"产物：食物 ×{plan.Portions}";
        _outputLabel.AddThemeColorOverride("font_color", plan.Portions > 0 ? OkColor : DimColor);

        bool busy = _activeJob is not null;
        _cookBtn.Disabled = busy || _selectedCook is null || !plan.CanCook;

        if (busy)
        {
            _footerLabel.Text = "有人正忙着别的活——完工之后再开火。";
            return;
        }

        // 拿得出口的原因才说（没灶/空锅/库里不够——这三条玩家本来就看得见）；
        // **"热量不够"一个字都不说**（PlayerFacingText 对它返回 null）——玩家只看到按钮是灰的。
        string? why = CookingLogic.PlayerFacingText(plan.Blocks);
        _footerLabel.Text = why
            ?? (plan.CanCook
                ? $"下锅之后要花 {CraftingPanelFormat.FormatWorkDuration(CookingLogic.WorkMinutesFor(plan.Portions))}，夜间生产时段推进。"
                : "");
    }

    private void OnCookPressed()
    {
        if (_selectedCook is null) return;

        CookPlan plan = CookingLogic.Plan(
            _hasStation, _pot, _station.Installed, _inventory.MaterialCount);
        if (!plan.CanCook) return;   // 双保险（按钮已灰）

        var snapshot = new Dictionary<string, int>(_pot);
        CookRequested?.Invoke(snapshot, _selectedCook);
        _pot.Clear();   // 下单即扣料（同制作/拆解/改装的既有语义）⇒ 锅清空
    }
}
