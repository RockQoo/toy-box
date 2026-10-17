using Godot;

namespace ToyBox.Games.SwipeBrick;

/// <summary>A flower that springs open, sways a moment, then floats up and fades (a brick that reached the flower line).</summary>
public partial class FlowerPop : Node2D
{
    private static readonly Color[] Petals =
    {
        new(1f, 0.55f, 0.75f),
        new(1f, 1f, 1f),
        new(0.75f, 0.6f, 1f),
        new(1f, 0.75f, 0.35f),
    };

    private const float Life = 1.1f;
    private float _age;
    private float _size;
    private Color _petal;

    public static FlowerPop Create(Vector2 position, float size, int seed) =>
        new() { Position = position, _size = size, _petal = Petals[Mathf.PosMod(seed, Petals.Length)] };

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Life)
        {
            QueueFree();
            return;
        }
        if (_age > 0.6f)
            Position += new Vector2(0, -60f * (float)delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = _age / Life;
        // Back-out spring for the opening, then a fade at the end
        var open = Mathf.Min(1f, _age / 0.25f);
        var scale = open < 1f ? 1.15f * Mathf.Sin(open * Mathf.Pi * 0.5f) + 0.0f : 1f + 0.06f * Mathf.Sin(_age * 12f) * (1 - t);
        var alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
        var r = _size * 0.5f * scale;
        DrawSetTransform(Vector2.Zero, _age * 1.5f, Vector2.One);
        var outline = new Color(0.2f, 0.22f, 0.36f, alpha);
        for (var i = 0; i < 5; i++)
        {
            var a = i * Mathf.Tau / 5;
            var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.55f;
            DrawCircle(c, r * 0.48f, outline);
        }
        for (var i = 0; i < 5; i++)
        {
            var a = i * Mathf.Tau / 5;
            var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.55f;
            DrawCircle(c, r * 0.40f, _petal with { A = alpha });
        }
        DrawCircle(Vector2.Zero, r * 0.33f, outline);
        DrawCircle(Vector2.Zero, r * 0.27f, new Color(1f, 0.84f, 0.2f, alpha));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
