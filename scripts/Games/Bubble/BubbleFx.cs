using System;
using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Bubble;

/// <summary>A popping bubble: bubble/pop_01..04 in turn, the first frames tinted with the bubble's color. Frees itself.</summary>
public partial class BubblePop : Node2D
{
    private const float FrameTime = 0.06f;
    private float _age;
    private float _delay;
    private float _size;
    private Color _tint;
    private int _color;

    public static BubblePop Create(Vector2 at, int color, float diameter, float delay) => new()
    {
        Position = at,
        _color = color,
        _size = diameter,
        _delay = delay,
        _tint = BubbleArt.Tints[Mathf.Clamp(color, 0, BubbleArt.Tints.Length - 1)],
    };

    /// <summary>Raised when the bubble actually bursts (after its delay).</summary>
    public event Action? Burst;

    public override void _Process(double delta)
    {
        var before = _age;
        _age += (float)delta;
        if (before < _delay && _age >= _delay)
            Burst?.Invoke();
        if (_age >= _delay + FrameTime * 4 + 0.05f)
            QueueFree();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_age < _delay)
        {
            // Still waiting its turn: the bubble swells a little
            var s = 1f + 0.12f * Mathf.Min(1f, _age / Mathf.Max(_delay, 0.01f));
            BubbleArt.Draw(this, _color, Vector2.Zero, _size * s);
            return;
        }
        var t = _age - _delay;
        var frame = Mathf.Min(3, (int)(t / FrameTime));
        var tex = BubbleArt.Pop(frame);
        var tint = Colors.White.Lerp(_tint, frame < 2 ? 0.55f : 0.25f);
        var h = _size * (1.15f + 0.12f * frame);
        var w = h * tex.GetWidth() / tex.GetHeight();
        var alpha = frame == 3 ? 1f - Mathf.Clamp((t - FrameTime * 3) / (FrameTime + 0.05f), 0f, 1f) : 1f;
        DrawTextureRect(tex, new Rect2(-w / 2, -h / 2, w, h), false, tint with { A = alpha });
    }
}

/// <summary>
/// A bubble that lost its hold: it falls, bounces once off the flower line, and turns into stars on the second touch
/// (<see cref="Landed"/>). Frees itself.
/// </summary>
public partial class FallingBubble : Node2D
{
    private const float Gravity = 2600f;
    private Vector2 _velocity;
    private float _floorY;
    private float _size;
    private float _spin;
    private int _color;
    private int _bounces;

    public static FallingBubble Create(Vector2 at, int color, float diameter, float floorY, Random rng) => new()
    {
        Position = at,
        _color = color,
        _size = diameter,
        _floorY = floorY,
        _velocity = new Vector2((float)(rng.NextDouble() * 2 - 1) * diameter * 3f, -(float)rng.NextDouble() * diameter * 4f),
        _spin = (float)(rng.NextDouble() * 2 - 1) * 5f,
    };

    public int Color => _color;

    /// <summary>Raised once when it turns into stars, with its position.</summary>
    public event Action<FallingBubble>? Landed;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _velocity.Y += Gravity * dt * (_size / 64f);
        Position += _velocity * dt;
        Rotation += _spin * dt;
        if (Position.Y >= _floorY && _velocity.Y > 0)
        {
            if (_bounces == 0)
            {
                _bounces++;
                Position = new Vector2(Position.X, _floorY);
                _velocity = new Vector2(_velocity.X * 0.6f, -_velocity.Y * 0.42f);
            }
            else
            {
                Landed?.Invoke(this);
                QueueFree();
                return;
            }
        }
        QueueRedraw();
    }

    public override void _Draw() => BubbleArt.Draw(this, _color, Vector2.Zero, _size);
}

/// <summary>The bubble in the air, with a short fading tail.</summary>
public partial class ShotBubble : Node2D
{
    private readonly Vector2[] _trail = new Vector2[6];
    private int _count;

    public int Color { get; set; }
    public float Size { get; set; } = 64f;

    public void Reset(Vector2 at)
    {
        Position = at;
        _count = 0;
        QueueRedraw();
    }

    public void MoveTo(Vector2 at)
    {
        for (var i = _trail.Length - 1; i > 0; i--)
            _trail[i] = _trail[i - 1];
        _trail[0] = Position;
        _count = Math.Min(_count + 1, _trail.Length);
        Position = at;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var tint = BubbleArt.Tints[Mathf.Clamp(Color, 0, BubbleArt.Tints.Length - 1)];
        for (var i = _count - 1; i >= 0; i--)
        {
            var k = 1f - (i + 1f) / (_trail.Length + 1f);
            DrawCircle(_trail[i] - Position, Size * 0.42f * k, tint with { A = 0.35f * k });
        }
        BubbleArt.Draw(this, Color, Vector2.Zero, Size);
    }
}
