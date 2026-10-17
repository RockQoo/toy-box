using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Breakout;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// The launch spot: the rabbit (buddy/rabbit_throw_empty.png) under the flower line with its paws up, holding the ball
/// while aiming (with "x N" for the ball count), leaning toward the aim. It hops to a new spot after a shot.
/// Position is the ball's resting point in board pixels; set <see cref="TargetX"/> to move it.
/// </summary>
public partial class Launcher : Node2D
{
    private const float HopTime = 0.35f;
    private const float RabbitHeight = 0.155f; // Board heights
    private const float PawLine = 0.40f;       // Paws, as a share of the sprite height from the top

    private Sprite2D _rabbit = null!;
    private Label _count = null!;
    private float _hopFrom;
    private float _hopTo;
    private float _hopT = 1f;
    private float _lean;
    private float _time;

    public float Unit { get; set; } = 720f;
    public bool Holding { get; set; } = true;
    /// <summary>Aim angle in degrees (90 straight up), or null when not aiming.</summary>
    public float? AimAngle { get; set; }

    public override void _Ready()
    {
        _rabbit = new Sprite2D { Texture = Ui.Tex("res://assets/sprites/buddy/rabbit_throw_empty.png"), ShowBehindParent = true };
        AddChild(_rabbit);
        _count = Ui.Label("", 26, Colors.White, 8, UiTheme.Ink);
        _count.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_count);
        Relayout();
    }

    public void Relayout()
    {
        if (_rabbit is null)
            return;
        var tex = _rabbit.Texture;
        var h = RabbitHeight * Unit;
        var s = h / tex.GetHeight();
        _rabbit.Scale = new Vector2(s, s);
        // Pivot at the feet so the lean looks like a body tilt
        _rabbit.Offset = new Vector2(0, -tex.GetHeight() / 2f);
        _rabbit.Position = new Vector2(0, h * (1 - PawLine) + SwipeBrickRules.BallRadius * Unit);
    }

    public void SetBalls(int balls) => _count.Text = $"x{balls}";

    /// <summary>Moves to <paramref name="x"/> (pixels in the board) with a little hop, or at once.</summary>
    public void MoveTo(float x, bool hop)
    {
        if (!hop || Mathf.Abs(x - Position.X) < 1f)
        {
            Position = new Vector2(x, Position.Y);
            _hopT = 1f;
            return;
        }
        _hopFrom = Position.X;
        _hopTo = x;
        _hopT = 0f;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        var hopY = 0f;
        if (_hopT < 1f)
        {
            _hopT = Mathf.Min(1f, _hopT + dt / HopTime);
            var e = 1 - (1 - _hopT) * (1 - _hopT);
            Position = new Vector2(Mathf.Lerp(_hopFrom, _hopTo, e), Position.Y);
            hopY = -Mathf.Sin(_hopT * Mathf.Pi) * Unit * 0.03f;
        }
        var targetLean = AimAngle is { } a ? Mathf.DegToRad((90f - a) * 0.3f) : 0f;
        _lean = Mathf.Lerp(_lean, targetLean, 1 - Mathf.Exp(-12f * dt));
        _rabbit.Rotation = _lean;
        var bob = AimAngle is null ? Mathf.Abs(Mathf.Sin(_time * 3f)) * Unit * 0.004f : 0f;
        _rabbit.Position = new Vector2(0, RabbitHeight * Unit * (1 - PawLine) + SwipeBrickRules.BallRadius * Unit + hopY - bob);

        _count.Visible = Holding;
        var r = SwipeBrickRules.BallRadius * Unit;
        _count.Position = new Vector2(r * 1.9f, -r * 2.2f + hopY);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Holding)
            return;
        var hopY = _hopT < 1f ? -Mathf.Sin(_hopT * Mathf.Pi) * Unit * 0.03f : 0f;
        BrickArt.DrawBall(this, new Vector2(0, hopY), SwipeBrickRules.BallRadius * Unit);
    }
}
