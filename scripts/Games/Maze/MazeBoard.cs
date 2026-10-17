using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Maze;

/// <summary>
/// The maze drawn in code (no tile art yet): a cream floor with a soft checker, thick rounded hedge walls with an ink
/// outline, a blue mat at the start, a gold mat with a waving flag (<see cref="MazeExit"/>) at the exit, and faint
/// footprints on the cells already walked. The origin is the top-left corner of the grid; the player and the stars are
/// children placed with <see cref="CellCenter"/>.
/// </summary>
public partial class MazeBoard : Node2D
{
    private static readonly Color Floor = new(1f, 0.98f, 0.92f);
    private static readonly Color FloorAlt = new(0.95f, 0.93f, 0.84f);
    private static readonly Color Wall = new(0.40f, 0.74f, 0.44f);
    private static readonly Color WallLight = new(0.62f, 0.88f, 0.58f);
    private static readonly Color StartMat = new(0.62f, 0.82f, 1f);
    private static readonly Color Footprint = UiTheme.Ink with { A = 0.13f };

    private readonly HashSet<MazeCell> _visited = new();
    private readonly List<(Vector2 A, Vector2 B)> _walls = new();
    private Core.Maze _maze = null!;
    private MazeExit _exit = null!;

    public float CellSize { get; private set; } = 60f;
    public Vector2 GridSize => new(_maze.Width * CellSize, _maze.Height * CellSize);
    /// <summary>The floor panel reaches this far past the grid (the outer walls sit on the grid edge).</summary>
    public float Pad => WallWidth * 0.5f + 6f;
    private float WallWidth => Mathf.Max(7f, CellSize * 0.2f);

    public MazeExit Exit => _exit;

    public static MazeBoard Create(Core.Maze maze, float cellSize)
    {
        var board = new MazeBoard { _maze = maze, CellSize = cellSize };
        board.BuildWalls();
        board._exit = new MazeExit();
        board.AddChild(board._exit);
        board.PlaceExit();
        return board;
    }

    public void SetCellSize(float size)
    {
        CellSize = size;
        PlaceExit();
        QueueRedraw();
    }

    public Vector2 CellCenter(MazeCell c) => new((c.X + 0.5f) * CellSize, (c.Y + 0.5f) * CellSize);

    public void MarkVisited(MazeCell c)
    {
        if (_visited.Add(c))
            QueueRedraw();
    }

    private void PlaceExit()
    {
        _exit.Position = CellCenter(_maze.Exit);
        _exit.CellSize = CellSize;
    }

    /// <summary>Wall segments in grid units: every closed side between cells, plus the outer frame.</summary>
    private void BuildWalls()
    {
        _walls.Clear();
        var w = _maze.Width;
        var h = _maze.Height;
        _walls.Add((new Vector2(0, 0), new Vector2(w, 0)));
        _walls.Add((new Vector2(0, h), new Vector2(w, h)));
        _walls.Add((new Vector2(0, 0), new Vector2(0, h)));
        _walls.Add((new Vector2(w, 0), new Vector2(w, h)));
        foreach (var c in _maze.Cells())
        {
            if (c.X < w - 1 && !_maze.CanMove(c, MazeDir.Right))
                _walls.Add((new Vector2(c.X + 1, c.Y), new Vector2(c.X + 1, c.Y + 1)));
            if (c.Y < h - 1 && !_maze.CanMove(c, MazeDir.Down))
                _walls.Add((new Vector2(c.X, c.Y + 1), new Vector2(c.X + 1, c.Y + 1)));
        }
    }

    public override void _Draw()
    {
        var grid = GridSize;
        var pad = Pad;
        var panel = new Rect2(-pad, -pad, grid + new Vector2(pad, pad) * 2);
        var radius = (int)Mathf.Clamp(CellSize * 0.35f, 12, 26);

        // Drop shadow and floor
        var shadow = UiTheme.Box(new Color(0, 0, 0, 0.16f), Colors.Transparent, 0, 0, radius);
        shadow.Draw(GetCanvasItem(), new Rect2(panel.Position + new Vector2(0, 9), panel.Size));
        UiTheme.Box(Floor, Colors.Transparent, 0, 0, radius).Draw(GetCanvasItem(), panel);
        foreach (var c in _maze.Cells())
            if ((c.X + c.Y) % 2 == 1)
                DrawRect(new Rect2(c.X * CellSize, c.Y * CellSize, CellSize, CellSize), FloorAlt);

        // Start mat and footprints (the exit mat and flag are drawn by MazeExit)
        DrawCircle(CellCenter(_maze.Start), CellSize * 0.36f, StartMat, antialiased: true);
        foreach (var c in _visited)
        {
            if (c == _maze.Start || c == _maze.Exit)
                continue;
            var center = CellCenter(c);
            var r = CellSize * 0.07f;
            DrawCircle(center + new Vector2(-r * 1.4f, r * 0.8f), r, Footprint, antialiased: true);
            DrawCircle(center + new Vector2(r * 1.4f, -r * 0.8f), r, Footprint, antialiased: true);
        }

        // Walls: ink outline first, then the green fill over all of them so joints merge, then a light top edge
        var width = WallWidth;
        foreach (var (a, b) in _walls)
            RoundLine(a * CellSize, b * CellSize, width + 7f, UiTheme.Ink);
        foreach (var (a, b) in _walls)
            RoundLine(a * CellSize, b * CellSize, width, Wall);
        var lift = new Vector2(0, -width * 0.17f);
        foreach (var (a, b) in _walls)
        {
            var dir = (b - a).Normalized();
            var from = a * CellSize + dir * width * 0.3f + lift;
            var to = b * CellSize - dir * width * 0.3f + lift;
            DrawLine(from, to, WallLight, width * 0.26f, true);
        }
    }

    private void RoundLine(Vector2 a, Vector2 b, float width, Color color)
    {
        DrawLine(a, b, color, width, true);
        DrawCircle(a, width / 2, color, antialiased: true);
        DrawCircle(b, width / 2, color, antialiased: true);
    }
}
