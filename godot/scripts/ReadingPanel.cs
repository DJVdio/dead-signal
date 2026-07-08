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
    }

    private Control _root = null!;
    private VBoxContainer _rowContainer = null!;
    private Button _confirmBtn = null!;
    private Button _cancelBtn = null!;

    private readonly List<PawnOption> _pawns = new();
    private readonly List<BookOption> _books = new();
    private readonly List<OptionButton> _dropdowns = new();

    public event Action<Dictionary<int, string>>? ReadingConfirmed; // pawnId→bookId
    public event Action? Cancelled;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "ReadingPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(520, 400),
            borderColor: new Color(0.25f, 0.22f, 0.18f));

        var title = new Label();
        title.Text = "夜晚读书指派";
        title.Position = new Vector2(24, 16);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(title);

        var listBg = new Panel();
        listBg.Position = new Vector2(24, 52);
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

    public void SetupReaders(IReadOnlyList<PawnOption> pawns, IReadOnlyList<BookOption> unreadBooks)
    {
        _pawns.Clear();
        _pawns.AddRange(pawns);
        _books.Clear();
        _books.AddRange(unreadBooks);
        _dropdowns.Clear();
        UiStyle.ClearChildren(_rowContainer);

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
                dropdown.AddItem(_books[b].Title, b);
            hbox.AddChild(dropdown);
            _dropdowns.Add(dropdown);

            var sep = new HSeparator();
            sep.AddThemeColorOverride("default_color", new Color(0.2f, 0.2f, 0.2f, 0.5f));
            _rowContainer.AddChild(hbox);
            _rowContainer.AddChild(sep);
        }
    }

    private void OnConfirm()
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

    /// <summary>
    /// 纯逻辑：把「每行选中的书序号」映射为 pawnId→bookId。
    /// bookIndex &lt; 0（“不读”）或越界的行略过不入。
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
