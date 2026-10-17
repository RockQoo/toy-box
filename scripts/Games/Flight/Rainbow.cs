using Godot;

namespace ToyBox.Games.Flight;

/// <summary>A rainbow arc that sweeps open over the smiling boss, stays a moment, and fades. Frees itself.</summary>
public partial class Rainbow : Node2D
{
    private static readonly Color[] Bands =
    {
        new(1f, 0.55f, 0.60f), new(1f, 0.75f, 0.40f), new(1f, 0.93f, 0.45f),
        new(0.55f, 0.88f, 0.50f), new(0.50f, 0.75f, 1f), new(0.72f, 0.58f, 0.95f),
    };

    private const float Sweep = 0.7f;
    private const float Life = 3.4f;

    private float _radius;
    private float _age;

    public static Rainbow Create(Vector2 position, float radius) => new() { Position = position, _radius = radius };

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Life)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var open = Mathf.Clamp(_age / Sweep, 0f, 1f);
        open = 1 - (1 - open) * (1 - open);
        var fade = _age > Life - 0.8f ? (Life - _age) / 0.8f : 1f;
        var band = _radius * 0.07f;
        for (var i = 0; i < Bands.Length; i++)
        {
            var r = _radius - i * band;
            DrawArc(Vector2.Zero, r, Mathf.Pi, Mathf.Pi + Mathf.Pi * open, 48, Bands[i] with { A = 0.85f * fade }, band + 1, true);
        }
    }
}
