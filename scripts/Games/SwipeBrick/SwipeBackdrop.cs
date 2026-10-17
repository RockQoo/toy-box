using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Breakout;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// The tall board behind the bricks: a soft translucent panel (open past the top of the screen) with faint cell dots,
/// and the flower line the balls land on. Origin is the board's top-left corner.
/// </summary>
public partial class SwipeBackdrop : Node2D
{
    private float _unit = 720f;

    public float Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            QueueRedraw();
        }
    }

    /// <summary>A short squash of the flowers (a ball landed or a brick turned into flowers).</summary>
    public float FloorBounce { get; set; }

    public override void _Process(double delta)
    {
        if (FloorBounce <= 0)
            return;
        FloorBounce = Mathf.Max(0, FloorBounce - (float)delta * 3f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var w = SwipeBrickRules.Width * _unit;
        var h = _unit;
        var box = UiTheme.Box(new Color(1f, 1f, 1f, 0.40f), new Color(1f, 1f, 1f, 0.85f), 5, 0, 26);
        DrawStyleBox(box, new Rect2(-8, -40, w + 16, h + 40 + 6));

        // Faint dots at the cell corners help to read the grid
        var dot = new Color(1f, 1f, 1f, 0.45f);
        for (var row = 1; row < SwipeBrickRules.Rows; row++)
            for (var col = 1; col < SwipeBrickRules.Columns; col++)
                DrawCircle(new Vector2(SwipeBrickRules.CellX(col), SwipeBrickRules.CellY(row)) * _unit, 2.5f, dot);

        var floor = BrickArt.FloorLine;
        var fh = w * floor.GetHeight() / floor.GetWidth();
        var squash = Mathf.Sin(FloorBounce * Mathf.Pi) * 0.25f;
        var rh = fh * (1 - squash);
        var y = SwipeBrickRules.FloorY * _unit;
        DrawTextureRect(floor, new Rect2(0, y + fh * 0.6f - rh, w, rh), false);
    }
}
