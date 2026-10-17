using System;
using Godot;

namespace ToyBox.Common;

/// <summary>Colored paper pieces that burst up from the bottom corners and flutter down. For best records and big wins.</summary>
public partial class Confetti : Node2D
{
    private struct Paper
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Angle;
        public float Spin;
        public float Flip;
        public Color Color;
        public Vector2 Size;
    }

    private const float Life = 3.2f;
    private const float Gravity = 520f;

    private static readonly Color[] Colors =
    {
        UiTheme.Pink, UiTheme.Yellow, UiTheme.Blue, UiTheme.Green, UiTheme.Orange, UiTheme.Purple,
    };

    private Paper[] _papers = Array.Empty<Paper>();
    private float _age;

    public static Confetti Create(Vector2 screenSize, int count)
    {
        var rng = new Random();
        var confetti = new Confetti { _papers = new Paper[count] };
        for (var i = 0; i < count; i++)
        {
            var fromLeft = i % 2 == 0;
            var origin = new Vector2(fromLeft ? -10 : screenSize.X + 10, screenSize.Y * 0.85f);
            var angle = Mathf.DegToRad(fromLeft ? -60 - (float)rng.NextDouble() * 25 : -120 + (float)rng.NextDouble() * 25);
            confetti._papers[i] = new Paper
            {
                Position = origin,
                Velocity = Vector2.Right.Rotated(angle) * (700 + (float)rng.NextDouble() * 650),
                Angle = (float)rng.NextDouble() * Mathf.Tau,
                Spin = (float)(rng.NextDouble() * 2 - 1) * 9f,
                Flip = (float)rng.NextDouble() * Mathf.Tau,
                Color = Colors[rng.Next(Colors.Length)],
                Size = new Vector2(10 + (float)rng.NextDouble() * 8, 16 + (float)rng.NextDouble() * 10),
            };
        }
        return confetti;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _age += dt;
        if (_age >= Life)
        {
            QueueFree();
            return;
        }
        for (var i = 0; i < _papers.Length; i++)
        {
            ref var p = ref _papers[i];
            // Strong air drag gives the fluttering fall
            p.Velocity = p.Velocity * Mathf.Max(0, 1 - 1.8f * dt) + new Vector2(0, Gravity * dt);
            p.Velocity.Y = Mathf.Min(p.Velocity.Y, 160f);
            p.Position += p.Velocity * dt + new Vector2(Mathf.Sin(_age * 4 + p.Flip) * 40 * dt, 0);
            p.Angle += p.Spin * dt;
            p.Flip += 7f * dt;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var fade = Mathf.Clamp((Life - _age) / 0.6f, 0, 1);
        foreach (var p in _papers)
        {
            var size = p.Size with { X = p.Size.X * Mathf.Abs(Mathf.Cos(p.Flip)) + 2 };
            DrawSetTransform(p.Position, p.Angle, Vector2.One);
            DrawRect(new Rect2(-size / 2, size), p.Color with { A = fade });
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
