using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Maze;

/// <summary>The arrow on a <see cref="DirPad"/> button: a white, ink-outlined triangle pointing in <see cref="Dir"/>.</summary>
public partial class DirArrow : Control
{
    public MazeDir Dir { get; init; }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = Mathf.Min(Size.X, Size.Y) * 0.27f;
        var angle = Dir switch
        {
            MazeDir.Up => -Mathf.Pi / 2,
            MazeDir.Right => 0f,
            MazeDir.Down => Mathf.Pi / 2,
            _ => Mathf.Pi,
        };
        DrawColoredPolygon(Triangle(c, r + 7f, angle), UiTheme.Ink);
        DrawColoredPolygon(Triangle(c, r, angle), Colors.White);
    }

    private static Vector2[] Triangle(Vector2 center, float radius, float angle)
    {
        var points = new Vector2[3];
        for (var i = 0; i < 3; i++)
            points[i] = center + Vector2.Right.Rotated(angle + i * Mathf.Tau / 3) * radius;
        return points;
    }
}
