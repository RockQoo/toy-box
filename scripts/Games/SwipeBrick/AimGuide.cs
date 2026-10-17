using Godot;
using ToyBox.Common;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// The aim line: dots (brick/aim_dot.png) from the launch spot to the first bounce, drifting outward, with the arrow
/// (brick/aim_arrow.png) at the end. Faded when the aim is too low to fire. Positions are in pixels of the board.
/// </summary>
public partial class AimGuide : Node2D
{
    private const float Spacing = 26f;
    private Texture2D _dot = null!;
    private Texture2D _arrow = null!;
    private Vector2 _from;
    private Vector2 _to;
    private bool _valid;
    private float _phase;

    public float DotSize { get; set; } = 14f;

    public override void _Ready()
    {
        _dot = Ui.Tex("res://assets/sprites/brick/aim_dot.png");
        _arrow = Ui.Tex("res://assets/sprites/brick/aim_arrow.png");
        Visible = false;
    }

    public void Show(Vector2 from, Vector2 to, bool valid)
    {
        _from = from;
        _to = to;
        _valid = valid;
        Visible = true;
        QueueRedraw();
    }

    public new void Hide() => Visible = false;

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        _phase = (_phase + (float)delta * 60f) % Spacing;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var span = _to - _from;
        var length = span.Length();
        if (length < 1f)
            return;
        var dir = span / length;
        var alpha = _valid ? 1f : 0.3f;
        var arrowSize = DotSize * 2.4f;
        var end = length - arrowSize * 0.6f;
        for (var d = Spacing * 0.8f + _phase; d < end; d += Spacing)
        {
            // Dots shrink a little toward the end
            var s = DotSize * (1f - 0.35f * d / length);
            DrawTextureRect(_dot, new Rect2(_from + dir * d - new Vector2(s, s) / 2, new Vector2(s, s)), false, Colors.White with { A = alpha });
        }
        DrawSetTransform(_to - dir * arrowSize * 0.4f, dir.Angle(), Vector2.One);
        DrawTextureRect(_arrow, new Rect2(-arrowSize / 2, -arrowSize / 2, arrowSize, arrowSize), false, Colors.White with { A = alpha });
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
