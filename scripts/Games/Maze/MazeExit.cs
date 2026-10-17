using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Maze;

/// <summary>
/// The exit, drawn in code: a glowing gold mat with a little flag waving on a pole, so a child sees "go here" at a
/// glance. <see cref="Hop"/> makes the flag jump when the player arrives.
/// </summary>
public partial class MazeExit : Node2D
{
    private static readonly Color Mat = new(1f, 0.86f, 0.38f);
    private static readonly Color Flag = new(1f, 0.38f, 0.45f);

    private float _time;
    private float _jump;      // Extra lift of the flag (pixels), tweened by Hop
    private float _cell = 60f;

    public float CellSize
    {
        get => _cell;
        set
        {
            _cell = value;
            QueueRedraw();
        }
    }

    public float Jump
    {
        get => _jump;
        set
        {
            _jump = value;
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public void Hop()
    {
        var tween = CreateTween();
        var set = Callable.From<float>(v => Jump = v);
        tween.TweenMethod(set, 0f, _cell * 0.35f, 0.16f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenMethod(set, _cell * 0.35f, 0f, 0.4f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        var c = _cell;
        // Pulsing glow and the mat
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 3f);
        DrawCircle(Vector2.Zero, c * (0.44f + 0.05f * pulse), Mat with { A = 0.25f + 0.2f * pulse }, antialiased: true);
        var mat = new Rect2(-c * 0.36f, -c * 0.36f, c * 0.72f, c * 0.72f);
        UiTheme.Box(Mat, UiTheme.Orange, Mathf.Max(2, (int)(c * 0.05f)), 0, (int)(c * 0.18f)).Draw(GetCanvasItem(), mat);

        // Pole standing on the mat, a bit left of center
        var baseY = c * 0.24f;
        var top = new Vector2(-c * 0.14f, -c * 0.42f - _jump);
        var foot = new Vector2(-c * 0.14f, baseY - _jump * 0.2f);
        DrawLine(foot, top, UiTheme.Ink, Mathf.Max(3f, c * 0.07f), true);
        DrawCircle(top, Mathf.Max(3f, c * 0.06f), UiTheme.Yellow, antialiased: true);

        // Waving flag: a strip whose far edge ripples with time
        const int steps = 8;
        var length = c * 0.46f;
        var height = c * 0.3f;
        var upper = new Vector2[steps + 1];
        var lower = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (float)steps;
            var wave = Mathf.Sin(_time * 6f - u * 4f) * c * 0.04f * u;
            upper[i] = top + new Vector2(u * length, c * 0.04f + wave);
            lower[i] = top + new Vector2(u * length * 0.92f, c * 0.04f + height * (1 - 0.25f * u) + wave);
        }
        var shape = new Vector2[(steps + 1) * 2];
        for (var i = 0; i <= steps; i++)
        {
            shape[i] = upper[i];
            shape[shape.Length - 1 - i] = lower[i];
        }
        DrawColoredPolygon(shape, Flag);
        var outline = new Vector2[shape.Length + 1];
        shape.CopyTo(outline, 0);
        outline[^1] = shape[0];
        DrawPolyline(outline, UiTheme.Ink, Mathf.Max(2f, c * 0.035f), true);
    }
}
