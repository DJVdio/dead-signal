using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class ReadingPanel : CanvasLayer
{
    public struct PawnOption
    {
        public int Id;
        public string Name;
    }

    public struct BookOption
    {
        public string BookId;
        public string Title;
        /// <summary>前置书 id（可空=无前置）。未读完前置时的读速效果见 Wiki 配置表，预指派界面据此提示。</summary>
        public string? PrerequisiteBookId;
        /// <summary>前置书标题（供提示文案「未读《X》」用；无前置时为 null）。</summary>
        public string? PrerequisiteTitle;
    }

    private Control _root = null!;
    private VBoxContainer _rowContainer = null!;
    private Label _emptyHint = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;

    private readonly List<PawnOption> _pawns = new();
    private readonly List<BookOption> _books = new();
    private readonly List<OptionButton> _dropdowns = new();
    private Func<int, string, bool>? _readerHasReadBook; // (pawnId, bookId)→已读；判前置是否满足
    private Func<int, string, double>? _readerHoursOnBook;

    // ---- character-first mode (先选角色再选书, with progress) ----
    private bool _useCharacterFirstMode;
    private HBoxContainer _selectorBar = null!;
    private OptionButton _pawnSelector = null!;
    private int _selectedPawnIndex;
    private readonly Dictionary<int, string> _assignmentMap = new();
    private List<BookDisplayOption> _characterBooks = new();
    private readonly List<Button> _bookAssignBtns = new();

    public event Action<Dictionary<int, string>>? ReadingConfirmed; // pawnId→bookId
    public event Action? Cancelled;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "ReadingPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(520, 440),
            borderColor: new Color(0.25f, 0.22f, 0.18f));

        var title = new Label();
        title.Text = "夜晚读书指派";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        // ---- pawn selector bar (hidden by default, shown in character-first mode) ----
        _selectorBar = new HBoxContainer();
        _selectorBar.Position = new Vector2(24, 48);
        _selectorBar.Size = new Vector2(472, 28);
        _selectorBar.Visible = false;
        panel.AddChild(_selectorBar);

        var selectorLabel = new Label();
        selectorLabel.Text = "读者：";
        selectorLabel.CustomMinimumSize = new Vector2(50, 0);
        selectorLabel.AddThemeFontSizeOverride("font_size", 15);
        selectorLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
        _selectorBar.AddChild(selectorLabel);

        _pawnSelector = new OptionButton();
        _pawnSelector.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pawnSelector.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
        _pawnSelector.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
        _pawnSelector.ItemSelected += OnPawnSelected;
        _selectorBar.AddChild(_pawnSelector);

        var listBg = new Panel();
        listBg.Position = new Vector2(24, 80);
        listBg.Size = new Vector2(472, 240);
        var listStyle = new StyleBoxFlat();
        listStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        listStyle.CornerRadiusTopLeft = 4;
        listStyle.CornerRadiusTopRight = 4;
        listStyle.CornerRadiusBottomLeft = 4;
        listStyle.CornerRadiusBottomRight = 4;
        listBg.AddThemeStyleboxOverride("panel", listStyle);
        panel.AddChild(listBg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        listBg.AddChild(scroll);

        _rowContainer = new VBoxContainer();
        _rowContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _rowContainer.MouseFilter = Control.MouseFilterEnum.Pass;
        scroll.AddChild(_rowContainer);

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

        _confirmBtn = new Button();
        _confirmBtn.Text = "确认";
        _confirmBtn.Position = new Vector2(340, 340);
        _confirmBtn.Size = new Vector2(140, 36);
        _confirmBtn.Pressed += OnConfirm;
        UiStyle.StyleButton(_confirmBtn, new Color(0.3f, 0.6f, 0.3f));
        panel.AddChild(_confirmBtn);

        _cancelBtn = new Button();
        _cancelBtn.Text = "取消";
        _cancelBtn.Position = new Vector2(180, 340);
        _cancelBtn.Size = new Vector2(120, 36);
        _cancelBtn.Pressed += () => Cancelled?.Invoke();
        UiStyle.StyleButton(_cancelBtn, new Color(0.5f, 0.3f, 0.2f));
        panel.AddChild(_cancelBtn);
    }

    /// <param name="readerHasReadBook">(读者 pawnId, 书 bookId) → 该读者是否已读完此书；用于判定选某书时前置是否满足（未满足则标「阅读极慢」）。传 null 视作一律未读。</param>
    public void SetupReaders(IReadOnlyList<PawnOption> pawns, IReadOnlyList<BookOption> unreadBooks,
        Func<int, string, bool>? readerHasReadBook = null)
    {
        _useCharacterFirstMode = false;
        _selectorBar.Visible = false;
        _readerHasReadBook = readerHasReadBook;
        _pawns.Clear();
        _pawns.AddRange(pawns);
        _books.Clear();
        _books.AddRange(unreadBooks);
        _dropdowns.Clear();
        _assignmentMap.Clear();
        UiStyle.ClearChildren(_rowContainer);

        if (_pawns.Count == 0 || _books.Count == 0)
        {
            _emptyHint.Text = _pawns.Count == 0
                ? "没有可读书的人（全员在外/伤重/已另有安排）。"
                : "书架上没有未读的书了。";
            _emptyHint.Visible = true;
            _confirmBtn.Disabled = true;
            return;
        }
        _emptyHint.Visible = false;
        _confirmBtn.Disabled = false;

        foreach (var pawn in _pawns)
        {
            var hbox = new HBoxContainer();
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.MouseFilter = Control.MouseFilterEnum.Pass;

            var nameLabel = new Label();
            nameLabel.Text = pawn.Name;
            nameLabel.CustomMinimumSize = new Vector2(120, 0);
            nameLabel.AddThemeFontSizeOverride("font_size", 15);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
            hbox.AddChild(nameLabel);

            var dropdown = new OptionButton();
            dropdown.CustomMinimumSize = new Vector2(260, 0);
            dropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            dropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.75f));
            dropdown.AddThemeColorOverride("font_hover_color", new Color(1, 0.9f, 0.7f));
            dropdown.AddItem("不读", -1);
            for (int b = 0; b < _books.Count; b++)
            {
                BookOption book = _books[b];
                // 前置未读 → 该读者读此书变慢，项名后缀提示（不禁止，仅耗时；系数见 Wiki 配置表）。
                bool slow = book.PrerequisiteBookId is { } preId
                    && !(_readerHasReadBook?.Invoke(pawn.Id, preId) ?? false);
                string label = slow
                    ? $"{book.Title}（未读《{book.PrerequisiteTitle ?? "前置书"}》：阅读极慢）"
                    : book.Title;
                dropdown.AddItem(label, b);
            }
            hbox.AddChild(dropdown);
            _dropdowns.Add(dropdown);

            var sep = new HSeparator();
            sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
            _rowContainer.AddChild(hbox);
            _rowContainer.AddChild(sep);
        }
    }

    /// <summary>
    /// 先选角色再选书：展示所有书并显示当前角色的已读标识和进度。
    /// </summary>
    public void SetupCharacterBooks(IReadOnlyList<ReaderOption> pawns, IReadOnlyList<BookDisplayOption> books,
        Func<int, string, bool>? readerHasReadBook = null,
        Func<int, string, double>? readerHoursOnBook = null)
    {
        _useCharacterFirstMode = true;
        _selectorBar.Visible = true;
        _readerHasReadBook = readerHasReadBook;
        _readerHoursOnBook = readerHoursOnBook;
        _pawns.Clear();
        foreach (var p in pawns)
            _pawns.Add(new PawnOption { Id = p.Id, Name = p.Name });
        _characterBooks.Clear();
        _characterBooks.AddRange(books);
        _assignmentMap.Clear();
        _dropdowns.Clear();
        _bookAssignBtns.Clear();

        _pawnSelector.Clear();
        for (int i = 0; i < _pawns.Count; i++)
            _pawnSelector.AddItem(_pawns[i].Name, i);

        UiStyle.ClearChildren(_rowContainer);

        if (_pawns.Count == 0)
        {
            _emptyHint.Text = "没有可读书的人（全员在外/伤重/已另有安排）。";
            _emptyHint.Visible = true;
            _confirmBtn.Disabled = true;
            return;
        }
        _emptyHint.Visible = false;
        _confirmBtn.Disabled = _characterBooks.Count == 0;

        _selectedPawnIndex = 0;
        _pawnSelector.Select(0);
        RebuildBookListForSelectedPawn();
    }

    private void OnPawnSelected(long index)
    {
        int pawnId = _pawns[_selectedPawnIndex].Id;
        // store any pending assignment before switching
        // (already stored in _assignmentMap via button clicks)

        _selectedPawnIndex = (int)index;
        RebuildBookListForSelectedPawn();
    }

    private void RebuildBookListForSelectedPawn()
    {
        UiStyle.ClearChildren(_rowContainer);
        _bookAssignBtns.Clear();

        if (_selectedPawnIndex < 0 || _selectedPawnIndex >= _pawns.Count)
            return;

        int pawnId = _pawns[_selectedPawnIndex].Id;

        for (int b = 0; b < _characterBooks.Count; b++)
        {
            BookDisplayOption book = _characterBooks[b];

            var hbox = new HBoxContainer();
            hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.MouseFilter = Control.MouseFilterEnum.Pass;

            var titleLabel = new Label();
            titleLabel.Text = book.Title;
            titleLabel.CustomMinimumSize = new Vector2(200, 0);
            titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            titleLabel.AddThemeFontSizeOverride("font_size", 14);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
            hbox.AddChild(titleLabel);

            // status label: "已读" or "x.x/y.y 小时"
            var statusLabel = new Label();
            bool isRead = _readerHasReadBook?.Invoke(pawnId, book.BookId) ?? book.IsRead;
            double readHours = _readerHoursOnBook?.Invoke(pawnId, book.BookId) ?? book.ReadHours;
            if (isRead)
            {
                statusLabel.Text = "已读";
                statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 0.4f));
            }
            else
            {
                statusLabel.Text = $"{readHours:F1}/{book.RequiredHours} 小时";
                statusLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
            }
            statusLabel.CustomMinimumSize = new Vector2(100, 0);
            statusLabel.AddThemeFontSizeOverride("font_size", 13);
            hbox.AddChild(statusLabel);

            // assign toggle button
            bool isAssigned = _assignmentMap.TryGetValue(pawnId, out string? assignedId)
                && assignedId == book.BookId;
            var assignBtn = new Button();
            assignBtn.Text = isAssigned ? "取消指派" : "指派";
            assignBtn.CustomMinimumSize = new Vector2(70, 0);

            int capturedIndex = b;
            string capturedBookId = book.BookId;
            assignBtn.Pressed += () =>
            {
                if (_assignmentMap.TryGetValue(pawnId, out string? current) && current == capturedBookId)
                    _assignmentMap.Remove(pawnId);
                else
                    _assignmentMap[pawnId] = capturedBookId;
                RebuildBookListForSelectedPawn();
            };

            hbox.AddChild(assignBtn);
            _bookAssignBtns.Add(assignBtn);

            var sep = new HSeparator();
            sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
            _rowContainer.AddChild(hbox);
            _rowContainer.AddChild(sep);
        }
    }

    private void OnConfirm()
    {
        if (_useCharacterFirstMode)
        {
            ReadingConfirmed?.Invoke(new Dictionary<int, string>(_assignmentMap));
        }
        else
        {
            var bookIndices = new List<int>(_dropdowns.Count);
            foreach (var dd in _dropdowns)
                bookIndices.Add(dd.GetItemId(dd.Selected));

            var pawnIds = new List<int>(_pawns.Count);
            foreach (var p in _pawns)
                pawnIds.Add(p.Id);

            var bookIds = new List<string>(_books.Count);
            foreach (var b in _books)
                bookIds.Add(b.BookId);

            ReadingConfirmed?.Invoke(BuildAssignment(pawnIds, bookIndices, bookIds));
        }
    }

    /// <summary>
    /// 纯逻辑：把「每行选中的书序号」映射为 pawnId→bookId。
    /// bookIndex &lt; 0（"不读"）或越界的行略过不入。
    /// </summary>
    public static Dictionary<int, string> BuildAssignment(
        IReadOnlyList<int> pawnIds,
        IReadOnlyList<int> bookIndices,
        IReadOnlyList<string> bookIds)
    {
        var map = new Dictionary<int, string>();
        int n = Math.Min(pawnIds.Count, bookIndices.Count);
        for (int i = 0; i < n; i++)
        {
            int bi = bookIndices[i];
            if (bi < 0 || bi >= bookIds.Count)
                continue;
            map[pawnIds[i]] = bookIds[bi];
        }
        return map;
    }
}
