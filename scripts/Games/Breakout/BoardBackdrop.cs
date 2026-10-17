using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Breakout;

/// <summary>
/// The tall play board behind the bricks: a soft translucent panel with a border (open past the top of the screen) and
/// the flower line along the bottom, where a missed ball lands. Origin is the board's top-left corner.
/// </summary>
public partial class BoardBackdrop : Node2D
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

    /// <summary>A short bounce of the flowers when a ball lands on them.</summary>
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
        var w = BreakoutRules.Width * _unit;
        var h = _unit;
        var box = UiTheme.Box(new Color(1f, 1f, 1f, 0.40f), new Color(1f, 1f, 1f, 0.85f), 5, 0, 26);
        DrawStyleBox(box, new Rect2(-8, -40, w + 16, h + 40 + 6));

        var floor = BrickArt.FloorLine;
        var fh = w * floor.GetHeight() / floor.GetWidth();
        var squash = Mathf.Sin(FloorBounce * Mathf.Pi) * 0.25f;
        var rh = fh * (1 - squash);
        DrawTextureRect(floor, new Rect2(0, h - rh, w, rh), false);
    }
}
