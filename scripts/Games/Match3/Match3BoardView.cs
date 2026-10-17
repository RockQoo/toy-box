using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Match3;

/// <summary>
/// The board: a soft panel with a slot (match3/slot.png) per cell and the wooden frame (ui/board_frame.png) around it.
/// The node origin is the top-left of the grid. Pieces live in <see cref="Pieces"/>, which clips them to the grid so new
/// fruit slide in from the top edge. The yellow selection border sits above the pieces.
/// </summary>
public partial class Match3BoardView : Node2D
{
    private const float Pad = 0.16f; // Panel around the grid, in cells

    private Texture2D _slot = null!;
    private StyleBoxFlat _panel = null!;
    private Control _clip = null!;
    private NinePatchRect _frame = null!;
    private Sprite2D _select = null!;
    private Tween? _selectTween;

    public int Size { get; private set; }
    public float CellSize { get; private set; }
    public Vector2 GridSize => new(Size * CellSize, Size * CellSize);

    /// <summary>Parent for the piece views (positions are grid coordinates).</summary>
    public Node2D Pieces { get; private set; } = null!;

    public static Match3BoardView Create(int size, float cell)
    {
        var view = new Match3BoardView { Size = size };
        view._slot = Ui.Tex("res://assets/sprites/match3/slot.png");
        view._panel = UiTheme.Box(new Color(1f, 0.98f, 0.92f, 0.82f), new Color(1f, 1f, 1f, 0f), 0, 0, 18);
        view._clip = new Control { ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        view.AddChild(view._clip);
        view.Pieces = new Node2D();
        view._clip.AddChild(view.Pieces);
        view._frame = new NinePatchRect
        {
            Texture = Ui.Tex("res://assets/sprites/ui/board_frame.png"),
            PatchMarginLeft = 70,
            PatchMarginRight = 70,
            PatchMarginTop = 70,
            PatchMarginBottom = 70,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        view.AddChild(view._frame);
        view._select = new Sprite2D { Texture = Ui.Tex("res://assets/sprites/ui/cell_select.png"), Visible = false };
        view.AddChild(view._select);
        view.SetCellSize(cell);
        return view;
    }

    public void SetCellSize(float cell)
    {
        CellSize = cell;
        _clip.Size = GridSize;
        // The frame's inner edge lines up just outside the panel
        var pad = cell * Pad;
        var border = Mathf.Round(cell * 0.32f);
        _frame.Position = new Vector2(-pad - border, -pad - border);
        _frame.Size = GridSize + Vector2.One * (pad + border) * 2;
        _select.Scale = Vector2.One * (cell * 1.04f / _select.Texture.GetWidth());
        foreach (var child in Pieces.GetChildren())
            if (child is GemView gem)
                gem.SetCell(cell);
        QueueRedraw();
    }

    public Vector2 CellCenter(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    /// <summary>The cell under a point in board coordinates, or null outside the grid.</summary>
    public (int X, int Y)? CellAt(Vector2 local)
    {
        if (local.X < 0 || local.Y < 0)
            return null;
        var x = (int)(local.X / CellSize);
        var y = (int)(local.Y / CellSize);
        return x < Size && y < Size ? (x, y) : null;
    }

    /// <summary>Shows the yellow border on a cell (null hides it).</summary>
    public void Select((int X, int Y)? cell)
    {
        _selectTween?.Kill();
        _select.Visible = cell is not null;
        if (cell is not { } c)
            return;
        _select.Position = CellCenter(c.X, c.Y);
        var rest = _select.Scale;
        _select.Scale = rest * 1.2f;
        _selectTween = _select.CreateTween();
        _selectTween.TweenProperty(_select, "scale", rest, 0.15f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        var pad = CellSize * Pad;
        DrawStyleBox(_panel, new Rect2(-pad, -pad, GridSize + new Vector2(pad, pad) * 2));
        var inset = CellSize * 0.035f;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                DrawTextureRect(_slot, new Rect2(x * CellSize + inset, y * CellSize + inset, CellSize - inset * 2, CellSize - inset * 2),
                    false, Colors.White with { A = 0.85f });
    }
}
