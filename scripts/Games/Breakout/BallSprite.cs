using Godot;

namespace ToyBox.Games.Breakout;

/// <summary>A ball with a short fading trail. Set <see cref="Node2D.Position"/> every frame; the trail follows by itself.</summary>
public partial class BallSprite : Node2D
{
    private const int TrailLength = 6;

    private readonly Vector2[] _trail = new Vector2[TrailLength];
    private int _trailCount;

    /// <summary>Collision radius in pixels.</summary>
    public float Radius { get; set; } = 14f;
    public bool Glow { get; set; }
    /// <summary>Off while it rests on the paddle or flies back as a bubble.</summary>
    public bool Trail { get; set; } = true;

    public override void _Process(double delta)
    {
        // Trail points in parent space, newest first
        for (var i = TrailLength - 1; i > 0; i--)
            _trail[i] = _trail[i - 1];
        _trail[0] = Position;
        _trailCount = Trail ? Mathf.Min(_trailCount + 1, TrailLength) : 0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (var i = _trailCount - 1; i >= 1; i--)
        {
            var u = 1f - i / (float)TrailLength;
            DrawCircle((_trail[i] - Position) / Scale, Radius * (0.4f + 0.5f * u),
                (Glow ? new Color(0.6f, 0.95f, 1f) : Colors.White) with { A = 0.35f * u });
        }
        BrickArt.DrawBall(this, Vector2.Zero, Radius, Glow);
    }
}
