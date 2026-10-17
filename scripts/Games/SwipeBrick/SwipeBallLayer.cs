using Godot;
using ToyBox.Core;
using ToyBox.Games.Breakout;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// Draws every ball of the current <see cref="SwipeShot"/> in one node (up to 60 balls with short trails) instead of a
/// node per ball. Balls that landed roll along the flower line to where the first one landed and tuck away there.
/// All buffers are allocated once.
/// </summary>
public partial class SwipeBallLayer : Node2D
{
    private const int TrailLength = 4;
    private const int Max = SwipeBrickRules.MaxBalls;
    private const float RollSpeed = 1.4f; // Board heights per second

    private readonly Vector2[] _trail = new Vector2[Max * TrailLength]; // Pixels, newest first
    private readonly int[] _trailCount = new int[Max];
    private readonly float[] _rollX = new float[Max];
    private readonly bool[] _rolling = new bool[Max];
    private readonly bool[] _tucked = new bool[Max];

    public float Unit { get; set; } = 720f;
    public SwipeShot? Shot { get; private set; }

    public void Begin(SwipeShot shot)
    {
        Shot = shot;
        System.Array.Clear(_trailCount);
        System.Array.Clear(_rolling);
        System.Array.Clear(_tucked);
        QueueRedraw();
    }

    public void Clear()
    {
        Shot = null;
        QueueRedraw();
    }

    /// <summary>A ball just landed: it starts rolling toward the first landing spot.</summary>
    public void Landed(int ball)
    {
        if (Shot is null)
            return;
        _rolling[ball] = true;
        _rollX[ball] = Shot.Position(ball).X;
        _trailCount[ball] = 0;
    }

    /// <summary>Call once per frame after the shot ticked.</summary>
    public void Advance(float dt)
    {
        if (Shot is not { } shot)
            return;
        var target = shot.FirstLandX;
        for (var i = 0; i < shot.Launched; i++)
        {
            if (shot.State(i) == SwipeShot.BallState.Flying)
            {
                var b = i * TrailLength;
                for (var k = TrailLength - 1; k > 0; k--)
                    _trail[b + k] = _trail[b + k - 1];
                var p = shot.Position(i);
                _trail[b] = new Vector2(p.X * Unit, p.Y * Unit);
                _trailCount[i] = Mathf.Min(_trailCount[i] + 1, TrailLength);
            }
            else if (_rolling[i] && !_tucked[i] && target is { } x)
            {
                var step = RollSpeed * dt;
                if (Mathf.Abs(_rollX[i] - x) <= step)
                    _tucked[i] = true;
                else
                    _rollX[i] += Mathf.Sign(x - _rollX[i]) * step;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Shot is not { } shot)
            return;
        var radius = SwipeBrickRules.BallRadius * Unit;
        for (var i = 0; i < shot.Launched; i++)
        {
            switch (shot.State(i))
            {
                case SwipeShot.BallState.Flying:
                {
                    var b = i * TrailLength;
                    var recalled = shot.Recalled(i);
                    for (var k = _trailCount[i] - 1; k >= 1; k--)
                    {
                        var u = 1f - k / (float)TrailLength;
                        DrawCircle(_trail[b + k], radius * (0.35f + 0.5f * u), Colors.White with { A = 0.3f * u });
                    }
                    var p = shot.Position(i);
                    BrickArt.DrawBall(this, new Vector2(p.X * Unit, p.Y * Unit), radius, false, recalled ? 0.55f : 1f);
                    break;
                }
                case SwipeShot.BallState.Landed when !_tucked[i]:
                    BrickArt.DrawBall(this, new Vector2(_rollX[i] * Unit, SwipeBrickRules.LaunchY * Unit), radius);
                    break;
            }
        }
    }
}
