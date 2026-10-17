using Godot;
using ToyBox.Common;

namespace ToyBox.Games.DinoRunner;

/// <summary>
/// The scrolling parts of the run, drawn in code over the shared <see cref="Scenery"/> sky: two rows of mountains
/// (far and near, slower than the ground for parallax), the tiled grass-and-dirt ground, and wind streaks for a
/// speed-up. <see cref="Scroll"/> moves everything by the ground distance.
/// </summary>
public partial class Landscape : Node2D
{
    private const float FarFactor = 0.08f;
    private const float NearFactor = 0.22f;
    private const float TileHeight = 0.19f;     // Ground picture height, fraction of the screen height
    private const float TileSurface = 0.30f;    // Where feet stand, fraction of the picture from its top
    private const float TileOverlap = 0.08f;    // Tiles overlap a little so their rounded ends read as one strip
    private static readonly Color Dirt = new(0.58f, 0.33f, 0.20f);

    private Texture2D _ground = null!;
    private Texture2D _mountain = null!;
    private float _scroll;
    private float _wind;
    private float _time;

    /// <summary>The ground line (feet) in pixels from the top.</summary>
    public float GroundY { get; set; }

    public override void _Ready()
    {
        _ground = Ui.Tex("res://assets/sprites/dino/ground.png");
        _mountain = Ui.Tex("res://assets/sprites/dino/mountain.png");
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
        DrawMountains(size, FarFactor, 0.55f, 0.30f, new Color(0.74f, 0.84f, 1f), 3);
        DrawMountains(size, NearFactor, 0.8f, 0.46f, new Color(0.92f, 0.96f, 1f), 11);
        DrawGround(size);
        if (_wind > 0)
            DrawWind(size);
    }

    /// <summary>One row of mountains repeating with pseudo-random size and spacing (stable for each slot).</summary>
    private void DrawMountains(Vector2 size, float factor, float scale, float spacing, Color tint, int seed)
    {
        var texSize = _mountain.GetSize();
        var height = size.Y * 0.26f * scale;
        var slot = size.Y * spacing;
        var offset = _scroll * factor;
        var first = Mathf.FloorToInt(offset / slot) - 1;
        var last = first + Mathf.CeilToInt(size.X / slot) + 2;
        var baseY = GroundY - size.Y * TileHeight * TileSurface + 4;
        for (var i = first; i <= last; i++)
        {
            var r1 = Hash(i, seed);
            var r2 = Hash(i, seed + 1);
            if (r2 < 0.2f)
                continue; // A gap now and then
            var h = height * (0.75f + 0.45f * r1);
            var w = h * texSize.X / texSize.Y;
            var x = i * slot - offset + slot * 0.35f * r2;
            DrawTextureRect(_mountain, new Rect2(x - w / 2, baseY - h, w, h), false, tint);
        }
    }

    private void DrawGround(Vector2 size)
    {
        var texSize = _ground.GetSize();
        var th = size.Y * TileHeight;
        var tw = th * texSize.X / texSize.Y;
        var step = tw * (1 - TileOverlap);
        var top = GroundY - th * TileSurface;
        // Dirt under the tiles down to the screen bottom (taller screens)
        DrawRect(new Rect2(0, top + th * 0.8f, size.X, size.Y - top), Dirt);
        var x = -Mathf.PosMod(_scroll, step) - step;
        while (x < size.X + step)
        {
            DrawTextureRect(_ground, new Rect2(x, top, tw, th), false);
            x += step;
        }
    }

    private void DrawWind(Vector2 size)
    {
        var alpha = Mathf.Clamp(_wind / 0.5f, 0f, 1f) * 0.55f;
        for (var i = 0; i < 9; i++)
        {
            var y = GroundY - size.Y * (0.08f + 0.06f * i);
            var length = size.X * (0.10f + 0.08f * Hash(i, 5));
            var x = size.X - Mathf.PosMod(_time * size.X * 2.2f + Hash(i, 9) * size.X, size.X + length);
            DrawLine(new Vector2(x, y), new Vector2(x + length, y), new Color(1, 1, 1, alpha), 4, true);
        }
    }

    /// <summary>A stable pseudo-random number in [0, 1) for a slot.</summary>
    private static float Hash(int i, int seed)
    {
        var h = (uint)(i * 374761393 + seed * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xffffff) / (float)0x1000000;
    }
}
