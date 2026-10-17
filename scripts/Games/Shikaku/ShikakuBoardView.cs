using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Shikaku;

/// <summary>
/// The board: a wooden frame (`ui/board_frame.png`) on a warm backing, the empty cells (`ui/cell_empty.png`), then
/// layers for the placed pieces (`shikaku/region_fill.png` tinted pastel), the number badges (`shikaku/clue_badge.png`
/// with a bold label), the keyboard cursor and the dashed drawing frame (`shikaku/drag_frame.png`). The node's origin is
/// the top-left corner of the first cell.
/// </summary>
public partial class ShikakuBoardView : Node2D
{
    private const float Gap = 4f;          // Between cells
    private const float Pad = 8f;          // Backing beyond the outer cells
    private const float FrameBar = 24f;    // The frame's visible thickness on screen
    private const float FrameScale = 0.5f; // The frame art is drawn at half size, so its corners stay small

    /// <summary>Soft pastel tints for the pieces (one per number, in reading order).</summary>
    public static readonly Color[] Pastels =
    {
        new(1.00f, 0.72f, 0.74f), // pink
        new(0.66f, 0.84f, 1.00f), // sky
        new(1.00f, 0.88f, 0.55f), // butter
        new(0.70f, 0.92f, 0.70f), // mint
        new(0.84f, 0.74f, 1.00f), // lilac
        new(1.00f, 0.78f, 0.58f), // peach
        new(0.62f, 0.92f, 0.90f), // aqua
        new(0.96f, 0.78f, 0.94f), // orchid
        new(0.86f, 0.94f, 0.62f), // lime
        new(0.78f, 0.80f, 1.00f), // periwinkle
        new(1.00f, 0.82f, 0.68f), // apricot
        new(0.74f, 0.88f, 0.80f), // sage
    };

    private readonly List<Sprite2D> _cells = new();
    private readonly Dictionary<int, Node2D> _badges = new();
    private NinePatchRect _frame = null!;
    private Node2D _pieceLayer = null!;
    private Node2D _badgeLayer = null!;
    private Node2D _dragLayer = null!;
    private Texture2D _cellTexture = null!;
    private Texture2D _badgeTexture = null!;
    private float _cell = 100f;
    private int _cursor = -1;
    private CursorMark _cursorNode = null!;

    public ShikakuPuzzle Puzzle { get; private set; } = null!;
    public int Size => Puzzle.Size;
    public float CellSize => _cell;
    /// <summary>Where piece nodes go (the game owns them).</summary>
    public Node2D PieceLayer => _pieceLayer;
    /// <summary>Above the badges: the drawing frame, hint frames and the cursor.</summary>
    public Node2D DragLayer => _dragLayer;

    /// <summary>Space the frame takes around the grid on each side.</summary>
    public static float Border => Pad + FrameBar;

    public static ShikakuBoardView Create(ShikakuPuzzle puzzle, float cellSize)
    {
        var view = new ShikakuBoardView { Puzzle = puzzle, _cell = cellSize };
        view.Build();
        return view;
    }

    private void Build()
    {
        _cellTexture = Ui.Tex("res://assets/sprites/ui/cell_empty.png");
        _badgeTexture = Ui.Tex("res://assets/sprites/shikaku/clue_badge.png");
        _frame = new NinePatchRect
        {
            Texture = Ui.Tex("res://assets/sprites/ui/board_frame.png"),
            PatchMarginLeft = 72,
            PatchMarginRight = 72,
            PatchMarginTop = 72,
            PatchMarginBottom = 72,
            Scale = new Vector2(FrameScale, FrameScale),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_frame);
        for (var i = 0; i < Puzzle.CellCount; i++)
        {
            var cell = new Sprite2D { Texture = _cellTexture };
            AddChild(cell);
            _cells.Add(cell);
        }
        _pieceLayer = new Node2D();
        AddChild(_pieceLayer);
        _badgeLayer = new Node2D();
        AddChild(_badgeLayer);
        foreach (var c in Puzzle.ClueCells)
        {
            var badge = new Node2D();
            badge.AddChild(new Sprite2D { Texture = _badgeTexture });
            var label = Ui.Label(Puzzle.Clues[c].ToString(), 40, UiTheme.Ink, 0);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            badge.AddChild(label);
            _badgeLayer.AddChild(badge);
            _badges[c] = badge;
        }
        _cursorNode = new CursorMark { Visible = false };
        AddChild(_cursorNode);
        _dragLayer = new Node2D();
        AddChild(_dragLayer);
        SetCellSize(_cell);
    }

    /// <summary>Largest cell size that fits the board (frame included) in <paramref name="area"/>.</summary>
    public static float FitCell(int size, Vector2 area, float max)
    {
        var w = (area.X - 2 * Border - (size - 1) * Gap) / size;
        var h = (area.Y - 2 * Border - (size - 1) * Gap) / size;
        return Mathf.Floor(Mathf.Min(max, Mathf.Min(w, h)));
    }

    public Vector2 GridSize => Vector2.One * (Size * _cell + (Size - 1) * Gap);

    public void SetCellSize(float size)
    {
        _cell = size;
        var k = size / _cellTexture.GetWidth();
        for (var i = 0; i < _cells.Count; i++)
        {
            _cells[i].Position = CellCenter(i);
            _cells[i].Scale = new Vector2(k, k);
        }
        var badgeSize = size * 0.66f;
        foreach (var (c, badge) in _badges)
        {
            badge.Position = CellCenter(c);
            var sprite = badge.GetChild<Sprite2D>(0);
            var bk = badgeSize / _badgeTexture.GetWidth();
            sprite.Scale = new Vector2(bk, bk);
            var label = badge.GetChild<Label>(1);
            label.AddThemeFontSizeOverride("font_size", (int)(size * (Puzzle.Clues[c] >= 10 ? 0.32f : 0.38f)));
            label.Size = new Vector2(badgeSize, badgeSize);
            label.Position = -label.Size / 2 + new Vector2(0, -size * 0.01f);
        }
        SetCursor(_cursor);
        var outer = new Rect2(Vector2.Zero, GridSize).Grow(Pad + FrameBar);
        _frame.Position = outer.Position;
        _frame.Size = outer.Size / FrameScale;
        QueueRedraw();
    }

    public Vector2 CellTopLeft(int index) => new(index % Size * (_cell + Gap), index / Size * (_cell + Gap));

    public Vector2 CellCenter(int index) => CellTopLeft(index) + Vector2.One * _cell / 2;

    /// <summary>The rectangle of cells in this node's coordinates (edge to edge, gaps inside included).</summary>
    public Rect2 RectOf(ShikakuRect r)
    {
        var tl = new Vector2(r.Col * (_cell + Gap), r.Row * (_cell + Gap));
        return new Rect2(tl, new Vector2(r.Cols * _cell + (r.Cols - 1) * Gap, r.Rows * _cell + (r.Rows - 1) * Gap));
    }

    /// <summary>The cell under a point in this node's coordinates, or -1 off the board.</summary>
    public int CellAt(Vector2 local)
    {
        if (!new Rect2(Vector2.Zero, GridSize).Grow(Pad).HasPoint(local))
            return -1;
        return CellAtClamped(local);
    }

    /// <summary>The nearest cell to a point (a finger dragged past the edge stays on the edge cell).</summary>
    public int CellAtClamped(Vector2 local)
    {
        var col = Mathf.Clamp(Mathf.FloorToInt((local.X + Gap / 2) / (_cell + Gap)), 0, Size - 1);
        var row = Mathf.Clamp(Mathf.FloorToInt((local.Y + Gap / 2) / (_cell + Gap)), 0, Size - 1);
        return row * Size + col;
    }

    public Node2D? Badge(int clueCell) => _badges.TryGetValue(clueCell, out var b) ? b : null;

    public IEnumerable<Node2D> Badges => _badges.Values;

    /// <summary>A small hop of a number badge (when its piece goes in).</summary>
    public void HopBadge(int clueCell, float delay = 0f)
    {
        if (Badge(clueCell) is not { } badge)
            return;
        var rest = CellCenter(clueCell);
        var tween = badge.CreateTween();
        tween.TweenInterval(delay);
        tween.TweenProperty(badge, "position", rest + new Vector2(0, -_cell * 0.16f), 0.12f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(badge, "scale", new Vector2(1.15f, 1.15f), 0.12f);
        tween.TweenProperty(badge, "position", rest, 0.18f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(badge, "scale", Vector2.One, 0.18f);
    }

    /// <summary>A gentle head shake of a badge ("this number wants a different size").</summary>
    public void WiggleBadge(int clueCell)
    {
        if (Badge(clueCell) is not { } badge)
            return;
        var tween = badge.CreateTween();
        foreach (var deg in new[] { -12f, 10f, -6f, 3f, 0f })
            tween.TweenProperty(badge, "rotation_degrees", deg, 0.07f).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>The keyboard cursor (-1: hidden): an orange rounded outline above the pieces.</summary>
    public void SetCursor(int cell)
    {
        _cursor = cell;
        _cursorNode.Visible = cell >= 0;
        if (cell < 0)
            return;
        _cursorNode.Position = CellTopLeft(cell);
        _cursorNode.Extent = _cell;
        _cursorNode.QueueRedraw();
    }

    public int Cursor => _cursor;

    public override void _Draw()
    {
        // The backing: warm brown, seen as thin lines between the cells
        var back = new Rect2(Vector2.Zero, GridSize).Grow(Pad + FrameBar * 0.5f);
        var box = new StyleBoxFlat { BgColor = new Color(0.62f, 0.45f, 0.30f), CornerDetail = 8 };
        box.SetCornerRadiusAll(18);
        DrawStyleBox(box, back);
    }

    private partial class CursorMark : Node2D
    {
        public float Extent { get; set; } = 100f;

        public override void _Draw()
        {
            var style = new StyleBoxFlat { DrawCenter = false, BorderColor = UiTheme.Orange, CornerDetail = 6 };
            style.SetBorderWidthAll(5);
            style.SetCornerRadiusAll((int)(Extent * 0.16f));
            DrawStyleBox(style, new Rect2(Vector2.Zero, Vector2.One * Extent).Grow(-3));
        }
    }
}

/// <summary>A placed piece: a 9-slice `region_fill` tinted pastel, scaled from its middle when it appears.</summary>
public partial class ShikakuPieceView : Node2D
{
    private const int Margin = 56;
    private NinePatchRect _fill = null!;

    public Color Tint { get; private set; }

    public static ShikakuPieceView Create(Rect2 rect, Color tint, float cellSize)
    {
        var view = new ShikakuPieceView { Tint = tint };
        var k = Mathf.Clamp(cellSize / 280f, 0.22f, 0.5f);
        var grown = rect.Grow(1);
        view.Position = grown.GetCenter();
        view._fill = new NinePatchRect
        {
            Texture = Ui.Tex("res://assets/sprites/shikaku/region_fill.png"),
            PatchMarginLeft = Margin,
            PatchMarginRight = Margin,
            PatchMarginTop = Margin,
            PatchMarginBottom = Margin,
            Scale = new Vector2(k, k),
            Size = grown.Size / k,
            Position = -grown.Size / 2,
            SelfModulate = tint,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        view.AddChild(view._fill);
        return view;
    }

    /// <summary>Spreads out from the middle (or just pops for undo and the like).</summary>
    public void Appear(bool big)
    {
        Scale = big ? new Vector2(0.55f, 0.55f) : new Vector2(0.9f, 0.9f);
        Modulate = Colors.White with { A = 0f };
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "scale", Vector2.One, big ? 0.32f : 0.15f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "modulate:a", 1f, big ? 0.18f : 0.1f);
    }

    /// <summary>Shrinks and fades, then frees itself.</summary>
    public void Vanish()
    {
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "scale", new Vector2(0.7f, 0.7f), 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(this, "modulate:a", 0f, 0.2f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>The finish wave: flashes through a rainbow color and back to its pastel.</summary>
    public void Rainbow(float delay, Color flash)
    {
        var tween = CreateTween();
        tween.TweenInterval(delay);
        tween.TweenProperty(_fill, "self_modulate", flash, 0.18f);
        tween.Parallel().TweenProperty(this, "scale", new Vector2(1.06f, 1.06f), 0.18f);
        tween.TweenProperty(_fill, "self_modulate", Tint, 0.4f);
        tween.Parallel().TweenProperty(this, "scale", Vector2.One, 0.3f);
    }
}

/// <summary>
/// The dashed drawing frame (`shikaku/drag_frame.png`, 9-slice with tiled edges) over a faint fill. Blue while
/// drawing, bright green when the size matches the number inside; gold for a hint.
/// </summary>
public partial class ShikakuDragFrame : Node2D
{
    private const int Margin = 44;
    private NinePatchRect _dots = null!;
    private ColorRect _fill = null!;
    private float _k;

    public static ShikakuDragFrame Create(float cellSize)
    {
        var frame = new ShikakuDragFrame { _k = Mathf.Clamp(cellSize / 300f, 0.22f, 0.45f) };
        frame._fill = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(frame._fill);
        frame._dots = new NinePatchRect
        {
            Texture = Ui.Tex("res://assets/sprites/shikaku/drag_frame.png"),
            PatchMarginLeft = Margin,
            PatchMarginRight = Margin,
            PatchMarginTop = Margin,
            PatchMarginBottom = Margin,
            AxisStretchHorizontal = NinePatchRect.AxisStretchMode.TileFit,
            AxisStretchVertical = NinePatchRect.AxisStretchMode.TileFit,
            Scale = new Vector2(frame._k, frame._k),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(frame._dots);
        return frame;
    }

    /// <summary>Frames <paramref name="rect"/> (board coordinates); the node sits at its center so it can shake and pulse.</summary>
    public void SetRect(Rect2 rect)
    {
        var grown = rect.Grow(3);
        Position = grown.GetCenter();
        _fill.Position = -rect.Size / 2;
        _fill.Size = rect.Size;
        _dots.Position = -grown.Size / 2;
        _dots.Size = grown.Size / _k;
    }

    public void SetColor(Color dots, Color fill)
    {
        _dots.SelfModulate = dots;
        _fill.Color = fill;
    }
}
