using Godot;

namespace ToyBox.Common;

/// <summary>A small expanding ring where the screen was touched, so a tap that hit nothing still feels registered.</summary>
public partial class Ripple : Node2D
{
    private const float Life = 0.3f;
    private const float Radius = 34f;

    private float _age;

    public static Ripple Create(Vector2 position) => new() { Position = position };

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Life)
            QueueFree();
        else
            QueueRedraw();
    }

    public override void _Draw()
    {
        var t = _age / Life;
        var r = Radius * (1 - Mathf.Pow(1 - t, 3));
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 32, new Color(1, 1, 1, 0.8f * (1 - t)), 6f * (1 - t) + 1, true);
    }
}
