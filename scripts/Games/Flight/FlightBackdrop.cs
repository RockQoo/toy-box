using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Flight;

/// <summary>
/// The sky behind the flight: the high-sky picture filling the screen, a far band of clouds drifting slowly and the
/// near forest-and-village band flowing faster (parallax). The two bands do not quite match end to end, so every other
/// copy is mirrored (each seam then meets its own reflection).
/// </summary>
public partial class FlightBackdrop : Node2D
{
    private const float FarFactor = 0.22f;   // The cloud band moves this much slower than the forest
    private const float FarHeight = 0.30f;   // Band heights as fractions of the screen height
    private const float NearHeight = 0.22f;
    private const float FarBottom = 0.90f;   // The cloud band's bottom edge, hidden behind the forest

    private Texture2D _sky = null!;
    private Texture2D _far = null!;
    private Texture2D _near = null!;
    private float _scroll;

    public override void _Ready()
    {
        ZIndex = -100;
        _sky = Ui.Tex("res://assets/backgrounds/sky_high.png");
        _far = Ui.Tex("res://assets/sprites/flight/bg_far.png");
        _near = Ui.Tex("res://assets/sprites/flight/bg_near.png");
    }

    /// <summary>Moves the forest by <paramref name="pixels"/> (the clouds follow more slowly).</summary>
    public void Scroll(float pixels)
    {
        _scroll += pixels;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        // Sky: cover the screen, keeping the picture's shape
        var skySize = _sky.GetSize();
        var cover = Mathf.Max(size.X / skySize.X, size.Y / skySize.Y);
        var drawn = skySize * cover;
        DrawTextureRect(_sky, new Rect2((size - drawn) / 2, drawn), false);

        var farH = size.Y * FarHeight;
        DrawBand(_far, size, farH, size.Y * FarBottom - farH, _scroll * FarFactor, new Color(1, 1, 1, 0.95f));
        var nearH = size.Y * NearHeight;
        DrawBand(_near, size, nearH, size.Y - nearH + 2, _scroll, Colors.White);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>One band tiled across the screen; odd copies are mirrored so the seams match.</summary>
    private void DrawBand(Texture2D texture, Vector2 size, float height, float top, float offset, Color tint)
    {
        var tex = texture.GetSize();
        var width = height * tex.X / tex.Y;
        var first = Mathf.FloorToInt(offset / width);
        var last = first + Mathf.CeilToInt(size.X / width) + 1;
        for (var i = first; i <= last; i++)
        {
            var x = i * width - offset;
            if ((i & 1) == 0)
            {
                DrawSetTransform(new Vector2(x, top), 0, Vector2.One);
            }
            else
            {
                DrawSetTransform(new Vector2(x + width, top), 0, new Vector2(-1, 1));
            }
            // One pixel wider so neighboring copies never show a hairline between them
            DrawTextureRect(texture, new Rect2(0, 0, width + 1, height), false, tint);
        }
    }
}
