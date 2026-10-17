using System;
using Godot;

namespace ToyBox.Common;

/// <summary>A one-shot burst: particles flying outward, plus an optional shockwave ring and flash. Frees itself when done.</summary>
public partial class Burst : Node2D
{
    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius;
    }

    private const float Gravity = 900f;
    private const float Drag = 3.5f;

    private Particle[] _particles = Array.Empty<Particle>();
    private Color _color;
    private float _life;
    private float _ringRadius;
    private float _age;

    /// <param name="ringRadius">Maximum radius of the shockwave ring. 0 means no ring and no flash, like a hit spark.</param>
    public static Burst Create(Vector2 position, Color color, int count, float speed, float particleRadius,
        float ringRadius, float life, Random rng)
    {
        var burst = new Burst
        {
            Position = position,
            _color = color,
            _life = life,
            _ringRadius = ringRadius,
            _particles = new Particle[count],
        };
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(rng.NextDouble() * Mathf.Tau);
            var s = speed * (0.4f + (float)rng.NextDouble() * 0.8f);
            burst._particles[i] = new Particle
            {
                Velocity = Vector2.Right.Rotated(angle) * s,
                Radius = particleRadius * (0.6f + (float)rng.NextDouble() * 0.7f),
            };
        }
        return burst;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _age += dt;
        if (_age >= _life)
        {
            QueueFree();
            return;
        }

        var drag = Mathf.Max(0, 1 - Drag * dt);
        for (var i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            p.Velocity = p.Velocity * drag + new Vector2(0, Gravity * dt);
            p.Position += p.Velocity * dt;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = _age / _life;
        var fade = 1 - t;

        if (_ringRadius > 0)
        {
            if (t < 0.12f)
                DrawCircle(Vector2.Zero, _ringRadius * 0.55f, new Color(1, 1, 1, 0.9f * (1 - t / 0.12f)), antialiased: true);
            var ringT = 1 - Mathf.Pow(1 - t, 3); // ease-out
            DrawArc(Vector2.Zero, _ringRadius * ringT, 0, Mathf.Tau, 48, _color with { A = fade }, 8f * fade + 1, true);
        }

        foreach (var p in _particles)
            DrawCircle(p.Position, p.Radius * (0.4f + 0.6f * fade), _color with { A = fade }, antialiased: true);
    }
}
