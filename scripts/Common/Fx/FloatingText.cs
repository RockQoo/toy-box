using Godot;

namespace ToyBox.Common;

/// <summary>Text that pops up, floats upward, and fades out, such as "+1" score popups. Adapted from color-blast.</summary>
public partial class FloatingText : Node2D
{
    private const float Life = 0.75f;
    private const float RiseSpeed = 80f;

    public string Text { get; init; } = "";
    public Color Color { get; init; } = Colors.White;
    public int FontSize { get; init; } = 36;

    private float _age;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _age += dt;
        if (_age >= Life)
        {
            QueueFree();
            return;
        }
        Position += new Vector2(0, -RiseSpeed * dt);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = _age / Life;
        var alpha = t < 0.6f ? 1f : 1 - (t - 0.6f) / 0.4f;
        // Starts large, then settles to normal size
        var size = (int)(FontSize * (t < 0.12f ? 1.5f - t / 0.12f * 0.5f : 1f));

        var font = ThemeDB.FallbackFont;
        var width = font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X;
        var pos = new Vector2(-width / 2, 0);
        DrawStringOutline(font, pos, Text, HorizontalAlignment.Left, -1, size, 10, UiTheme.Ink with { A = alpha });
        DrawString(font, pos, Text, HorizontalAlignment.Left, -1, size, Color with { A = alpha });
    }
}
