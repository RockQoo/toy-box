using System;
using Godot;

namespace ToyBox.Common;

/// <summary>
/// Torn pieces (balloon rubber, fruit peel) that fly out, tumble, fall and fade. Each piece is a small irregular
/// four-sided shape in the given color with a lighter edge. Frees itself when done.
/// </summary>
public partial class Shards : Node2D
{
    private struct Piece
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Angle;
        public float Spin;
        public Vector2[] Shape;
    }

    private const float Gravity = 1100f;
    private const float Drag = 2.2f;

    private Piece[] _pieces = Array.Empty<Piece>();
    private Color _color;
    private float _life;
    private float _age;

    /// <param name="size">Approximate pixel length of a piece.</param>
    public static Shards Create(Vector2 position, Color color, int count, float speed, float size, float life, Random rng)
    {
        var shards = new Shards { Position = position, _color = color, _life = life, _pieces = new Piece[count] };
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(rng.NextDouble() * Mathf.Tau);
            var dir = (Vector2.Right.Rotated(angle) + new Vector2(0, -0.35f)).Normalized();
            var s = size * (0.7f + (float)rng.NextDouble() * 0.6f);
            shards._pieces[i] = new Piece
            {
                Velocity = dir * speed * (0.6f + (float)rng.NextDouble() * 0.6f),
                Angle = (float)rng.NextDouble() * Mathf.Tau,
                Spin = (float)(rng.NextDouble() * 2 - 1) * 12f,
                // A stretched, slightly crooked quad looks like a torn strip
                Shape = new[]
                {
                    new Vector2(-s * 0.5f, -s * (0.12f + 0.1f * (float)rng.NextDouble())),
                    new Vector2(s * (0.35f + 0.15f * (float)rng.NextDouble()), -s * 0.2f),
                    new Vector2(s * 0.5f, s * (0.1f + 0.1f * (float)rng.NextDouble())),
                    new Vector2(-s * (0.3f + 0.15f * (float)rng.NextDouble()), s * 0.22f),
                },
            };
        }
        return shards;
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
        for (var i = 0; i < _pieces.Length; i++)
        {
            ref var p = ref _pieces[i];
            p.Velocity = p.Velocity * drag + new Vector2(0, Gravity * dt);
            p.Position += p.Velocity * dt;
            p.Angle += p.Spin * dt;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = _age / _life;
        var alpha = t < 0.6f ? 1f : 1 - (t - 0.6f) / 0.4f;
        var fill = _color with { A = alpha };
        var edge = _color.Lightened(0.45f) with { A = alpha };
        var points = new Vector2[4];
        foreach (var p in _pieces)
        {
            var xform = new Transform2D(p.Angle, p.Position);
            for (var i = 0; i < 4; i++)
                points[i] = xform * p.Shape[i];
            DrawColoredPolygon(points, fill);
            DrawPolyline(new[] { points[0], points[1], points[2], points[3], points[0] }, edge, 2f, true);
        }
    }
}
