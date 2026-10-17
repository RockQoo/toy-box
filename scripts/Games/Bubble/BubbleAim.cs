using System.Collections.Generic;
using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Bubble;

/// <summary>
/// The aim line: dots (brick/aim_dot.png) along the predicted path, drifting outward, faded when the aim is too low.
/// On 쉬움 it goes all the way and a faint bubble shows the cell it will stick to. Positions are board pixels.
/// </summary>
public partial class BubbleAim : Node2D
{
    private readonly List<Vector2> _points = new();
    private Texture2D _dot = null!;
    private bool _valid;
    private float _phase;
    private int _ghostColor = -1;
    private Vector2 _ghostAt;

    public float Unit { get; set; } = 64f;

    public override void _Ready()
    {
        _dot = Ui.Tex("res://assets/sprites/brick/aim_dot.png");
        Visible = false;
    }

    /// <param name="ghostColor">Color of the faint bubble at <paramref name="ghostAt"/>, or -1 for none.</param>
    public void Show(IReadOnlyList<Vector2> points, bool valid, int ghostColor = -1, Vector2 ghostAt = default)
    {
        _points.Clear();
        _points.AddRange(points);
        _valid = valid;
        _ghostColor = ghostColor;
        _ghostAt = ghostAt;
        Visible = true;
        QueueRedraw();
    }

    public new void Hide() => Visible = false;

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        var spacing = Unit * 0.42f;
        _phase = (_phase + (float)delta * Unit * 1.1f) % spacing;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_points.Count < 2)
            return;
        if (_ghostColor >= 0 && _valid)
            BubbleArt.Draw(this, _ghostColor, _ghostAt, Unit, Colors.White with { A = 0.38f });

        var spacing = Unit * 0.42f;
        var alpha = _valid ? 0.95f : 0.3f;
        var total = 0f;
        for (var i = 1; i < _points.Count; i++)
            total += _points[i - 1].DistanceTo(_points[i]);
        // Skip the part over the launcher's own bubble
        var travelled = 0f;
        var next = Unit * 0.75f + _phase;
        for (var i = 1; i < _points.Count; i++)
        {
            var a = _points[i - 1];
            var b = _points[i];
            var len = a.DistanceTo(b);
            while (next <= travelled + len)
            {
                var p = a.Lerp(b, (next - travelled) / len);
                var s = Unit * 0.26f * (1f - 0.4f * next / Mathf.Max(total, 1f));
                DrawTextureRect(_dot, new Rect2(p - new Vector2(s, s) / 2, new Vector2(s, s)), false, Colors.White with { A = alpha });
                next += spacing;
            }
            travelled += len;
        }
    }
}
