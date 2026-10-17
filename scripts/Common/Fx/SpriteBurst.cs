using System;
using Godot;

namespace ToyBox.Common;

/// <summary>A handful of sprites (stars, hearts) that fly out, spin, shrink and fade. Frees itself when done.</summary>
public partial class SpriteBurst : Node2D
{
    private struct Piece
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Angle;
        public float Spin;
        public float Size;
    }

    private const float Gravity = 700f;

    private Texture2D _texture = null!;
    private Piece[] _pieces = Array.Empty<Piece>();
    private float _life;
    private float _age;

    /// <param name="size">Pixel height of each sprite at the start.</param>
    public static SpriteBurst Create(Texture2D texture, Vector2 position, int count, float speed, float size, float life,
        Random rng, float upBias = 0.4f)
    {
        var burst = new SpriteBurst { Position = position, _texture = texture, _life = life, _pieces = new Piece[count] };
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(rng.NextDouble() * Mathf.Tau);
            var dir = Vector2.Right.Rotated(angle) + new Vector2(0, -upBias);
            burst._pieces[i] = new Piece
            {
                Velocity = dir.Normalized() * speed * (0.6f + (float)rng.NextDouble() * 0.6f),
                Angle = (float)rng.NextDouble() * Mathf.Tau,
                Spin = (float)(rng.NextDouble() * 2 - 1) * 8f,
                Size = size * (0.7f + (float)rng.NextDouble() * 0.5f),
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
        for (var i = 0; i < _pieces.Length; i++)
        {
            ref var p = ref _pieces[i];
            p.Velocity = p.Velocity * Mathf.Max(0, 1 - 2.5f * dt) + new Vector2(0, Gravity * dt);
            p.Position += p.Velocity * dt;
            p.Angle += p.Spin * dt;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = _age / _life;
        var fade = t < 0.6f ? 1f : 1 - (t - 0.6f) / 0.4f;
        var texSize = _texture.GetSize();
        foreach (var p in _pieces)
        {
            var scale = p.Size / texSize.Y * (1 - 0.5f * t);
            DrawSetTransform(p.Position, p.Angle, new Vector2(scale, scale));
            DrawTexture(_texture, -texSize / 2, new Color(1, 1, 1, fade));
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
