using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Blocks;

/// <summary>
/// The well: a soft panel with a faint grid inside the wooden frame (ui/board_frame.png). The node origin is the top-left
/// of the visible grid. The stack, the ghost and the falling piece live in a clipped layer whose coordinates are board
/// cells (the hidden rows sit above the clip, so a new piece slides in from the top edge). <see cref="Fx"/> is above the
/// frame and not clipped.
/// </summary>
public partial class BlocksBoardView : Node2D
{
    private const float FrameScale = 0.36f;   // board_frame.png drawn at this scale
    private const float FrameRim = 46f;       // Texture pixels from the frame's outer edge to its inner edge
    private const float SquashTime = 0.22f;

    private Control _clip = null!;
    private Node2D _inner = null!;
    private DrawLayer _stack = null!;
    private DrawLayer _ghost = null!;
    private DrawLayer _piece = null!;
    private NinePatchRect _frame = null!;
    private StyleBoxFlat _panel = null!;

    private int[,] _grid = new int[1, 1];
    private ActivePiece? _shown;
    private int _ghostY;
    private bool _pieceVisible;
    private Vector2 _slide;               // Visual lag of the piece toward its cell (pixels)

    private HashSet<(int X, int Y)> _squash = new();
    private float _squashAge = SquashTime;
    private float _squashBottom;
    private float _squashCenterX;

    private HashSet<int> _flashRows = new();
    private float _flashAge;
    private float _flashTime;
    private Color _flashColor;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public float CellSize { get; private set; }
    public Vector2 GridSize => new(Columns * CellSize, Rows * CellSize);

    /// <summary>Effects in board-local pixels (see <see cref="CellCenter"/>), drawn above the frame.</summary>
    public Node2D Fx { get; private set; } = null!;

    /// <summary>Outer edge of the frame, in board-local pixels.</summary>
    public Rect2 FrameRect
    {
        get
        {
            var rim = FrameRim * FrameScale + Pad;
            return new Rect2(-rim, -rim, GridSize + new Vector2(rim, rim) * 2);
        }
    }

    private float Pad => CellSize * 0.12f;

    public static BlocksBoardView Create(int columns, int rows, float cell)
    {
        var view = new BlocksBoardView { Columns = columns, Rows = rows };
        view._panel = UiTheme.Box(new Color(1f, 0.98f, 0.93f, 0.92f), new Color(1f, 1f, 1f, 0f), 0, 0, 14);
        view._clip = new Control { ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        view.AddChild(view._clip);
        view._inner = new Node2D();
        view._clip.AddChild(view._inner);
        view._stack = new DrawLayer { Painter = view.PaintStack };
        view._inner.AddChild(view._stack);
        view._ghost = new DrawLayer { Painter = view.PaintGhost };
        view._inner.AddChild(view._ghost);
        view._piece = new DrawLayer { Painter = view.PaintPiece };
        view._inner.AddChild(view._piece);
        view._frame = new NinePatchRect
        {
            Texture = Ui.Tex("res://assets/sprites/ui/board_frame.png"),
            PatchMarginLeft = 70,
            PatchMarginRight = 70,
            PatchMarginTop = 70,
            PatchMarginBottom = 70,
            Scale = new Vector2(FrameScale, FrameScale),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        view.AddChild(view._frame);
        view.Fx = new Node2D();
        view.AddChild(view.Fx);
        view._grid = new int[columns, rows + BlocksRules.HiddenRows];
        view.SetCellSize(cell);
        return view;
    }

    public void SetCellSize(float cell)
    {
        CellSize = cell;
        _clip.Size = GridSize;
        _inner.Position = new Vector2(0, -BlocksRules.HiddenRows * cell);
        var rim = FrameRim * FrameScale + Pad;
        _frame.Position = new Vector2(-rim, -rim);
        _frame.Size = (GridSize + new Vector2(rim, rim) * 2) / FrameScale;
        PlacePiece();
        QueueRedraw();
        _stack.QueueRedraw();
        _ghost.QueueRedraw();
    }

    /// <summary>Center of a board cell (board rows count the hidden ones), in board-local pixels.</summary>
    public Vector2 CellCenter(int x, int y) =>
        new((x + 0.5f) * CellSize, (y - BlocksRules.HiddenRows + 0.5f) * CellSize);

    /// <summary>Center of a board row, in board-local pixels.</summary>
    public Vector2 RowCenter(int y) => new(GridSize.X / 2, (y - BlocksRules.HiddenRows + 0.5f) * CellSize);

    // ---- Stack ----

    public void SetGrid(int[,] grid)
    {
        _grid = (int[,])grid.Clone();
        _flashRows.Clear();
        _stack.QueueRedraw();
    }

    /// <summary>The blocks just placed get squashed flat and spring back.</summary>
    public void Squash(IEnumerable<PlacedBlock> blocks)
    {
        var list = blocks.ToList();
        if (list.Count == 0)
            return;
        _squash = list.Select(b => (b.X, b.Y)).ToHashSet();
        _squashAge = 0;
        _squashBottom = list.Max(b => b.Y) + 1;
        _squashCenterX = (list.Min(b => b.X) + list.Max(b => b.X) + 1) / 2f;
    }

    /// <summary>Rows pulse with a light color (lines going away, or the bottom rows taken when the stack is full).</summary>
    public void FlashRows(IEnumerable<int> rows, float time, Color color)
    {
        _flashRows = rows.ToHashSet();
        _flashAge = 0;
        _flashTime = time;
        _flashColor = color;
        _stack.QueueRedraw();
    }

    private void PaintStack(DrawLayer layer)
    {
        var cell = CellSize;
        var size = new Vector2(cell, cell);
        var squashing = _squashAge < SquashTime;
        var s = squashing ? Mathf.Sin(Mathf.Pi * _squashAge / SquashTime) : 0f;
        var flashing = _flashRows.Count > 0 && _flashAge < _flashTime;
        var flashT = flashing ? _flashAge / _flashTime : 0f;
        for (var y = 0; y < _grid.GetLength(1); y++)
        {
            var rowFlash = flashing && _flashRows.Contains(y);
            for (var x = 0; x < _grid.GetLength(0); x++)
            {
                var v = _grid[x, y];
                if (v == 0)
                    continue;
                var pos = new Vector2(x * cell, y * cell);
                var cellSize = size;
                if (squashing && _squash.Contains((x, y)))
                {
                    // Flattened toward the piece's bottom, widened from its middle
                    var sy = 1f - 0.22f * s;
                    var sx = 1f + 0.12f * s;
                    var bottom = _squashBottom * cell;
                    var mid = _squashCenterX * cell;
                    pos = new Vector2(mid + (pos.X - mid) * sx, bottom - (bottom - pos.Y) * sy);
                    cellSize = new Vector2(cell * sx, cell * sy);
                }
                if (rowFlash)
                {
                    // Swell a little, then shrink away at the end
                    var k = flashT < 0.7f ? 1f + 0.06f * Mathf.Sin(flashT / 0.7f * Mathf.Pi) : 1f - (flashT - 0.7f) / 0.3f * 0.7f;
                    var center = pos + cellSize / 2;
                    cellSize *= k;
                    pos = center - cellSize / 2;
                }
                BlocksArt.DrawCell(layer, pos, cellSize, (PieceKind)(v - 1));
            }
            if (rowFlash)
            {
                var a = 0.65f * Mathf.Abs(Mathf.Sin(flashT * Mathf.Pi * 2.5f)) + 0.15f;
                layer.DrawRect(new Rect2(0, y * cell, _grid.GetLength(0) * cell, cell), _flashColor with { A = a });
            }
        }
    }

    // ---- Falling piece and ghost ----

    /// <summary>
    /// Shows the falling piece and its ghost. With <paramref name="slide"/>, a one-cell move glides over a few frames
    /// instead of jumping (moves, gravity steps); a new piece or a turn appears in place.
    /// </summary>
    public void ShowPiece(ActivePiece piece, int ghostY, bool slide)
    {
        if (slide && _shown is { } old && old.Kind == piece.Kind && old.Rotation == piece.Rotation)
        {
            var dx = old.X - piece.X;
            var dy = old.Y - piece.Y;
            if (Mathf.Abs(dx) <= 2 && Mathf.Abs(dy) <= 2)
                _slide += new Vector2(dx, dy) * CellSize;
        }
        else
        {
            _slide = Vector2.Zero;
        }
        _shown = piece;
        _ghostY = ghostY;
        _pieceVisible = true;
        PlacePiece();
        _piece.QueueRedraw();
        _ghost.QueueRedraw();
    }

    public void HidePiece()
    {
        _pieceVisible = false;
        _piece.QueueRedraw();
        _ghost.QueueRedraw();
    }

    /// <summary>A little bounce of the falling piece (moves, turns) — bigger for a new piece popping in.</summary>
    public void PunchPiece(float from)
    {
        _piece.Scale = new Vector2(from, from);
        _piece.CreateTween().TweenProperty(_piece, "scale", Vector2.One, 0.14f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A turn that could not happen: a small shake of the head ("no room").</summary>
    public void WigglePiece()
    {
        var tween = _piece.CreateTween();
        tween.TweenProperty(_piece, "rotation", 0.12f, 0.05f);
        tween.TweenProperty(_piece, "rotation", -0.12f, 0.08f);
        tween.TweenProperty(_piece, "rotation", 0f, 0.05f);
    }

    /// <summary>Where the piece's box center is (the piece layer turns and scales around it).</summary>
    private Vector2 PieceCenter(ActivePiece piece)
    {
        var n = BlocksRules.BoxSize(piece.Kind);
        return new Vector2((piece.X + n / 2f) * CellSize, (piece.Y + n / 2f) * CellSize);
    }

    private void PlacePiece()
    {
        if (_shown is { } piece)
            _piece.Position = PieceCenter(piece) + _slide;
    }

    private void PaintPiece(DrawLayer layer)
    {
        if (!_pieceVisible || _shown is not { } piece)
            return;
        var n = BlocksRules.BoxSize(piece.Kind);
        var half = new Vector2(n, n) * CellSize / 2;
        foreach (var c in BlocksRules.Cells(piece.Kind, piece.Rotation))
            BlocksArt.DrawCell(layer, new Vector2(c.X, c.Y) * CellSize - half, new Vector2(CellSize, CellSize), piece.Kind);
    }

    private void PaintGhost(DrawLayer layer)
    {
        if (!_pieceVisible || _shown is not { } piece || _ghostY <= piece.Y)
            return;
        var ghost = BlocksArt.Ghost;
        var tint = BlocksArt.ColorOf(piece.Kind).Darkened(0.15f) with { A = 0.95f };
        foreach (var c in (piece with { Y = _ghostY }).Cells)
        {
            var rect = new Rect2(c.X * CellSize, c.Y * CellSize, CellSize, CellSize);
            layer.DrawRect(rect.Grow(-CellSize * 0.08f), tint with { A = 0.22f });
            layer.DrawTextureRect(ghost, rect.Grow(-CellSize * 0.02f), false, tint);
        }
    }

    /// <summary>Light streaks from where a hard drop started down to where it landed.</summary>
    public void DropTrail(ActivePiece from, ActivePiece to)
    {
        var color = BlocksArt.ColorOf(from.Kind).Lerp(Colors.White, 0.5f);
        var columns = to.Cells.GroupBy(c => c.X).Select(g => (X: g.Key, Top: from.Y + (g.Min(c => c.Y) - to.Y), Bottom: g.Min(c => c.Y)));
        var cell = CellSize;
        var trail = new DrawLayer();
        trail.Painter = layer =>
        {
            foreach (var (x, top, bottom) in columns)
            {
                if (bottom <= top)
                    continue;
                var x0 = x * cell + cell * 0.18f;
                var x1 = (x + 1) * cell - cell * 0.18f;
                var y0 = top * cell;
                var y1 = bottom * cell;
                layer.DrawPolygon(
                    new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) },
                    new[] { color with { A = 0f }, color with { A = 0f }, color with { A = 0.75f }, color with { A = 0.75f } });
            }
        };
        _inner.AddChild(trail);
        _inner.MoveChild(trail, _ghost.GetIndex());
        var tween = trail.CreateTween();
        tween.TweenProperty(trail, "modulate:a", 0f, 0.28f);
        tween.TweenCallback(Callable.From(trail.QueueFree));
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (_slide != Vector2.Zero)
        {
            _slide *= Mathf.Exp(-dt * 28f);
            if (_slide.LengthSquared() < 0.25f)
                _slide = Vector2.Zero;
            PlacePiece();
        }
        if (_squashAge < SquashTime)
        {
            _squashAge += dt;
            _stack.QueueRedraw();
        }
        if (_flashRows.Count > 0 && _flashAge < _flashTime)
        {
            _flashAge += dt;
            _stack.QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var pad = Pad;
        DrawStyleBox(_panel, new Rect2(-pad, -pad, GridSize + new Vector2(pad, pad) * 2));
        // Faint grid so a child can count cells
        var line = UiTheme.Ink with { A = 0.06f };
        for (var x = 1; x < Columns; x++)
            DrawLine(new Vector2(x * CellSize, 0), new Vector2(x * CellSize, GridSize.Y), line, 1f);
        for (var y = 1; y < Rows; y++)
            DrawLine(new Vector2(0, y * CellSize), new Vector2(GridSize.X, y * CellSize), line, 1f);
    }
}
