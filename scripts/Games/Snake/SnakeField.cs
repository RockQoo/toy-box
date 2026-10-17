using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Snake;

/// <summary>
/// The play field: a rounded frame around a soft grass checkerboard (drawn in code: the received grass tile has ragged
/// edges, see docs/resources.md). Children (leaves, the caterpillar) use its local pixels; cell (0, 0) is top-left.
/// </summary>
public partial class SnakeField : Node2D
{
    private static readonly Color GrassA = new(0.74f, 0.89f, 0.52f);
    private static readonly Color GrassB = new(0.68f, 0.85f, 0.46f);
    private static readonly Color Edge = new(0.36f, 0.62f, 0.28f);

    private StyleBoxFlat _frame = null!;

    public int Columns { get; private set; } = SnakeRules.Width;
    public int Rows { get; private set; } = SnakeRules.Height;
    public float CellSize { get; private set; } = 60f;
    public Vector2 GridSize => new(Columns * CellSize, Rows * CellSize);

    public override void _Ready()
    {
        _frame = UiTheme.Box(GrassA, Edge, 8, 0, 18);
        _frame.ShadowColor = new Color(0, 0, 0, 0.18f);
        _frame.ShadowSize = 8;
        _frame.ShadowOffset = new Vector2(0, 5);
    }

    public void Setup(int columns, int rows, float cell)
    {
        Columns = columns;
        Rows = rows;
        CellSize = cell;
        QueueRedraw();
    }

    public Vector2 CellCenter(MazeCell c) => new((c.X + 0.5f) * CellSize, (c.Y + 0.5f) * CellSize);

    public override void _Draw()
    {
        var grid = new Rect2(Vector2.Zero, GridSize);
        _frame.Draw(GetCanvasItem(), grid.Grow(10));
        // Checker cells, rounded at the four board corners by the frame drawn over the edge below
        for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
                if ((x + y) % 2 == 1)
                    DrawRect(new Rect2(x * CellSize, y * CellSize, CellSize, CellSize), GrassB);
        // A few blades of grass for texture (fixed pattern, no randomness per frame)
        for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
            {
                var h = (x * 73 + y * 37) % 7;
                if (h > 1)
                    continue;
                var at = new Vector2((x + 0.25f + 0.1f * h) * CellSize, (y + 0.75f) * CellSize);
                var blade = CellSize * 0.14f;
                var color = Edge with { A = 0.35f };
                DrawLine(at, at + new Vector2(-blade * 0.4f, -blade), color, 2f);
                DrawLine(at + new Vector2(4, 0), at + new Vector2(4 + blade * 0.3f, -blade * 1.2f), color, 2f);
            }
    }
}
