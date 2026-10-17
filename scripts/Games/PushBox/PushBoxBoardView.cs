using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Blocks;

namespace ToyBox.Games.PushBox;

/// <summary>
/// The push-box board: a lightened, mirror-tiled grass floor, faint star spots laid flat on it (so they do not look like
/// the maze's pick-up stars), bushes for the inner walls and a wooden fence (rails with a post at every cell) along the
/// outer ring. The boxes and the rabbit live in <see cref="Pieces"/> (y-sorted); the bottom fence row is drawn in a front
/// layer so it stays in front of them. The origin is the top-left corner of the grid.
/// </summary>
public partial class PushBoxBoardView : Node2D
{
    private const float GrassCells = 2.6f;           // One grass tile spans this many cells
    private static readonly Color GrassVeil = new(1f, 0.99f, 0.9f, 0.5f);
    private static readonly Color GoalTint = new(1f, 1f, 1f, 0.5f);

    private Texture2D _grass = null!, _bush = null!, _fenceH = null!, _fenceV = null!, _post = null!, _goal = null!;
    private PushBoxPuzzle _puzzle = null!;
    private DrawLayer _front = null!;

    public float CellSize { get; private set; } = 80f;
    public Vector2 GridSize => new(_puzzle.Width * CellSize, _puzzle.Height * CellSize);
    /// <summary>Y-sorted layer for the boxes and the rabbit (children placed at cell centers).</summary>
    public Node2D Pieces { get; private set; } = null!;

    public static PushBoxBoardView Create(PushBoxPuzzle puzzle, float cellSize)
    {
        var view = new PushBoxBoardView
        {
            _puzzle = puzzle,
            CellSize = cellSize,
            TextureRepeat = TextureRepeatEnum.Mirror,
            _grass = Ui.Tex("res://assets/sprites/maze/floor_grass.png"),
            _bush = Ui.Tex("res://assets/sprites/maze/wall.png"),
            _fenceH = Ui.Tex("res://assets/sprites/maze/fence_h.png"),
            _fenceV = Ui.Tex("res://assets/sprites/maze/fence_v.png"),
            _post = Ui.Tex("res://assets/sprites/maze/post.png"),
            _goal = Ui.Tex("res://assets/sprites/pushbox/goal.png"),
        };
        view.Pieces = new Node2D { YSortEnabled = true };
        view.AddChild(view.Pieces);
        view._front = new DrawLayer { Painter = view.DrawFront };
        view.AddChild(view._front);
        return view;
    }

    public void SetCellSize(float size)
    {
        CellSize = size;
        QueueRedraw();
        _front.QueueRedraw();
    }

    public Vector2 CellCenter(MazeCell c) => new((c.X + 0.5f) * CellSize, (c.Y + 0.5f) * CellSize);

    public override void _Draw()
    {
        var c = CellSize;
        var grid = GridSize;
        var w = _puzzle.Width;
        var h = _puzzle.Height;

        // Drop shadow, then the grass over the whole board (mirrored so the tile edges meet), veiled lighter
        var inner = new Rect2(c * 0.5f, c * 0.5f, grid - new Vector2(c, c));
        var radius = (int)Mathf.Clamp(c * 0.3f, 10, 28);
        UiTheme.Box(new Color(0, 0, 0, 0.16f), Colors.Transparent, 0, 0, radius)
            .Draw(GetCanvasItem(), new Rect2(inner.Position + new Vector2(0, c * 0.12f), inner.Size));
        var tile = c * GrassCells / _grass.GetWidth();
        DrawTextureRectRegion(_grass, inner, new Rect2(Vector2.Zero, inner.Size / tile));
        DrawRect(inner, GrassVeil);

        // Star spots flat on the floor (faint)
        foreach (var g in _puzzle.Goals)
            DrawFitted(_goal, CellCenter(g) + new Vector2(0, c * 0.04f), c * 0.74f, GoalTint);

        // Inner bushes
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
                if (_puzzle.IsWall(new MazeCell(x, y)))
                    DrawFitted(_bush, CellCenter(new MazeCell(x, y)) + new Vector2(0, -c * 0.03f), c * 1.04f, Colors.White);

        // The fence: top row, then the two sides (the bottom row is in the front layer)
        for (var x = 0; x < w - 1; x++)
            Rail(new MazeCell(x, 0), true);
        for (var y = 0; y < h - 1; y++)
        {
            Rail(new MazeCell(0, y), false);
            Rail(new MazeCell(w - 1, y), false);
        }
        for (var x = 0; x < w; x++)
            Post(new MazeCell(x, 0));
        for (var y = 1; y < h - 1; y++)
        {
            Post(new MazeCell(0, y));
            Post(new MazeCell(w - 1, y));
        }
    }

    private void DrawFront(DrawLayer layer)
    {
        var y = _puzzle.Height - 1;
        for (var x = 0; x < _puzzle.Width - 1; x++)
            Rail(new MazeCell(x, y), true, layer);
        for (var x = 0; x < _puzzle.Width; x++)
            Post(new MazeCell(x, y), layer);
    }

    /// <summary>A fence piece from this ring cell's center to the next one (right, or down).</summary>
    private void Rail(MazeCell from, bool horizontal, CanvasItem? on = null)
    {
        on ??= this;
        var c = CellSize;
        var a = CellCenter(from);
        if (horizontal)
        {
            var size = new Vector2(c * 1.32f, c * 1.32f * _fenceH.GetHeight() / _fenceH.GetWidth());
            on.DrawTextureRect(_fenceH, new Rect2(a + new Vector2(c * 0.5f, -c * 0.08f) - size / 2, size), false);
        }
        else
        {
            var size = new Vector2(c * 1.5f * _fenceV.GetWidth() / _fenceV.GetHeight(), c * 1.5f);
            on.DrawTextureRect(_fenceV, new Rect2(a + new Vector2(0, c * 0.5f) - size / 2, size), false);
        }
    }

    private void Post(MazeCell at, CanvasItem? on = null) =>
        DrawFittedOn(on ?? this, _post, CellCenter(at) + new Vector2(0, -CellSize * 0.1f), CellSize * 0.5f, Colors.White);

    private void DrawFitted(Texture2D tex, Vector2 center, float box, Color modulate) => DrawFittedOn(this, tex, center, box, modulate);

    /// <summary>Draws a texture centered, scaled to fit a square of <paramref name="box"/> pixels.</summary>
    private static void DrawFittedOn(CanvasItem on, Texture2D tex, Vector2 center, float box, Color modulate)
    {
        var size = tex.GetSize();
        var s = box / Mathf.Max(size.X, size.Y);
        on.DrawTextureRect(tex, new Rect2(center - size * s / 2, size * s), false, modulate);
    }
}
