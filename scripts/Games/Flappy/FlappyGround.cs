using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Flappy;

/// <summary>
/// The scrolling grass-and-dirt strip along the bottom, drawn over the pillars' feet. The tile's ends do not quite
/// match, so it repeats mirrored (every other copy flipped; drawn copy by copy because the picture's end columns are
/// see-through and would show as seams with a plain mirrored repeat). Wind streaks show for a moment on a speed-up.
/// </summary>
public partial class FlappyGround : Node2D
{
    private const string TilePath = "res://assets/sprites/dino/ground_tile.png";
    private const float TileHeight = 0.13f;   // Picture height, fraction of the screen height
    private const float GrassAbove = 0.02f;   // The grass top sits this far above the ground line (screen heights)
    private const float EdgeCut = 3f;          // See-through columns at each end of the picture
    private static readonly Color Dirt = new(0.62f, 0.36f, 0.20f);

    private Texture2D _tile = null!;
    private float _scroll;
    private float _wind;
    private float _time;

    /// <summary>The ground line in pixels from the top.</summary>
    public float GroundY { get; set; }

    public override void _Ready()
    {
        _tile = Ui.Tex(TilePath);
    }

    public void Scroll(float pixels)
    {
        _scroll += pixels;
        QueueRedraw();
    }

    /// <summary>Wind streaks for a moment (speed-up).</summary>
    public void Gust(float seconds = 1.2f) => _wind = seconds;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        if (_wind > 0)
        {
            _wind -= dt;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        var texSize = _tile.GetSize();
        var scale = size.Y * TileHeight / texSize.Y;
        var top = GroundY - size.Y * GrassAbove;
        // Dirt under the strip down to the screen bottom (taller screens)
        DrawRect(new Rect2(0, top + texSize.Y * scale * 0.7f, size.X, size.Y), Dirt);
        // The picture's first and last columns are see-through: use the opaque middle, every other copy mirrored so
        // the ends meet. One period is two copies, so the pattern stays put while scrolling.
        var src = new Rect2(EdgeCut, 0, texSize.X - 2 * EdgeCut, texSize.Y);
        var w = src.Size.X * scale;
        var h = texSize.Y * scale;
        var x = -Mathf.PosMod(_scroll, 2 * w);
        for (var i = 0; x < size.X; i++, x += w)
        {
            // Overlap by a pixel so no hairline of sky shows between copies
            var dest = new Rect2(x - 0.5f, top, w + 1f, h);
            if (i % 2 == 1)
                DrawSetTransform(new Vector2(2 * x + w, 0), 0, new Vector2(-1, 1));
            DrawTextureRectRegion(_tile, dest, src);
            if (i % 2 == 1)
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        if (_wind > 0)
            DrawWind(size);
    }

    private void DrawWind(Vector2 size)
    {
        var alpha = Mathf.Clamp(_wind / 0.5f, 0f, 1f) * 0.55f;
        for (var i = 0; i < 9; i++)
        {
            var y = size.Y * (0.12f + 0.08f * i);
            var length = size.X * (0.10f + 0.08f * Hash(i, 5));
            var x = size.X - Mathf.PosMod(_time * size.X * 2.2f + Hash(i, 9) * size.X, size.X + length);
            DrawLine(new Vector2(x, y), new Vector2(x + length, y), new Color(1, 1, 1, alpha), 4, true);
        }
    }

    private static float Hash(int i, int seed)
    {
        var h = (uint)(i * 374761393 + seed * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xffffff) / (float)0x1000000;
    }
}
