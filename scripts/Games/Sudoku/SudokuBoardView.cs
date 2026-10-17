using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Sudoku;

/// <summary>
/// The board: a wooden frame (`ui/board_frame.png`), a dark backing that shows through as thin lines between cells and
/// thick lines between boxes, and the cells. The node's origin is the top-left corner of the first cell.
/// </summary>
public partial class SudokuBoardView : Node2D
{
    private const float Gap = 4f;          // Between cells
    private const float BoxGap = 14f;      // Between boxes (the "굵은 선")
    private const float Pad = 8f;          // Backing beyond the outer cells
    private const float FrameBar = 24f;    // The frame's visible thickness on screen
    private const float FrameScale = 0.5f; // The frame art is drawn at half size, so its corners stay small

    private readonly List<SudokuCell> _cells = new();
    private NinePatchRect _frame = null!;
    private float _cell = 100f;

    public SudokuShape Shape { get; private set; } = SudokuShape.For(4);
    public float CellSize => _cell;
    public IReadOnlyList<SudokuCell> Cells => _cells;

    /// <summary>Space the frame takes around the grid on each side.</summary>
    public static float Border => Pad + FrameBar;

    public static SudokuBoardView Create(SudokuShape shape, float cellSize)
    {
        var view = new SudokuBoardView { Shape = shape, _cell = cellSize };
        view.Build();
        return view;
    }

    private void Build()
    {
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
        for (var i = 0; i < Shape.CellCount; i++)
        {
            var cell = new SudokuCell { Index = i, Animals = Shape.Size };
            AddChild(cell);
            _cells.Add(cell);
        }
        SetCellSize(_cell);
    }

    /// <summary>Total extra width (or height) of the gaps across a row (or column).</summary>
    private static float Gaps(int n, int box) => (n - 1) * Gap + (n / box - 1) * (BoxGap - Gap);

    /// <summary>Largest cell size that fits the board (frame included) in <paramref name="area"/>.</summary>
    public static float FitCell(SudokuShape shape, Vector2 area, float max)
    {
        var n = shape.Size;
        var w = (area.X - 2 * Border - Gaps(n, shape.BoxWidth)) / n;
        var h = (area.Y - 2 * Border - Gaps(n, shape.BoxHeight)) / n;
        return Mathf.Floor(Mathf.Min(max, Mathf.Min(w, h)));
    }

    public Vector2 GridSize => new(
        Shape.Size * _cell + Gaps(Shape.Size, Shape.BoxWidth),
        Shape.Size * _cell + Gaps(Shape.Size, Shape.BoxHeight));

    /// <summary>The whole board with its frame, in this node's coordinates.</summary>
    public Rect2 OuterRect => new Rect2(Vector2.Zero, GridSize).Grow(Border);

    public void SetCellSize(float size)
    {
        _cell = size;
        foreach (var cell in _cells)
        {
            cell.Position = CellCenter(cell.Index);
            cell.SetSize(size);
        }
        var outer = new Rect2(Vector2.Zero, GridSize).Grow(Pad + FrameBar);
        _frame.Position = outer.Position;
        _frame.Size = outer.Size / FrameScale;
        QueueRedraw();
    }

    private float Offset(int index, int box) => index * (_cell + Gap) + index / box * (BoxGap - Gap);

    public Vector2 CellCenter(int index) => new(
        Offset(Shape.Column(index), Shape.BoxWidth) + _cell / 2,
        Offset(Shape.Row(index), Shape.BoxHeight) + _cell / 2);

    /// <summary>The cell under a point in this node's coordinates (gaps count toward the nearest cell), or -1.</summary>
    public int CellAt(Vector2 local)
    {
        if (!new Rect2(Vector2.Zero, GridSize).Grow(Pad).HasPoint(local))
            return -1;
        var best = -1;
        var bestDist = float.MaxValue;
        for (var i = 0; i < Shape.CellCount; i++)
        {
            var d = CellCenter(i) - local;
            var dist = Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y));
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    public override void _Draw()
    {
        // The backing: dark warm brown, seen as the lines between cells and boxes
        var back = new Rect2(Vector2.Zero, GridSize).Grow(Pad + FrameBar * 0.5f);
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.45f, 0.30f, 0.20f),
            CornerDetail = 8,
        };
        box.SetCornerRadiusAll(18);
        DrawStyleBox(box, back);
    }
}
