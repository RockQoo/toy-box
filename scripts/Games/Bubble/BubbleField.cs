using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Breakout;

namespace ToyBox.Games.Bubble;

/// <summary>
/// The tall board: a soft translucent panel, the flower line, the bubbles hanging from the ceiling plate, and the
/// ceiling plate itself (stacked strips of bubble/ceiling.png down to its lower edge). Origin is the board's top-left.
/// <see cref="Offset"/> moves the ceiling and the bubbles together (the intro drop and the ceiling moving a row).
/// Bubbles that reached the flower line can be kept on show as <see cref="Ghosts"/> until the rabbit cleans them up.
/// </summary>
public partial class BubbleField : Node2D
{
    private const float WobbleAmp = 0.09f;   // Diameters
    private const float StripWidth = 4f;     // Diameters per ceiling tile

    private readonly Dictionary<BubbleCell, (Vector2 Dir, float Age)> _wobble = new();
    private float _time;
    private float _floorBounce;

    public float Unit { get; set; } = 64f;
    public BubbleBoard? Board { get; set; }

    /// <summary>Pixels the ceiling and the bubbles are drawn below their place (negative: above).</summary>
    public float Offset { get; set; }

    /// <summary>The ceiling shakes now and then: it comes down soon.</summary>
    public bool Warning { get; set; }

    /// <summary>Bubbles no longer in the board that are still drawn (cell, color, drawn with <see cref="Offset"/>).</summary>
    public List<BubbleSpot> Ghosts { get; } = new();

    /// <summary>The bubbles around a newly stuck one sway away from it for a moment.</summary>
    public void Wobble(BubbleCell from)
    {
        if (Board is not { } board)
            return;
        var center = board.Center(from);
        foreach (var c in board.Cells())
        {
            if (c == from)
                continue;
            var d = board.Center(c) - center;
            var dist = d.Length();
            if (dist > 2.2f)
                continue;
            var dir = new Vector2(d.X, d.Y) / dist * (dist < 1.2f ? 1f : 0.45f);
            _wobble[c] = (dir, 0f);
        }
        _wobble[from] = (Vector2.Zero, 0f);
    }

    public void BounceFloor() => _floorBounce = 1f;

    public void ResetEffects()
    {
        _wobble.Clear();
        Ghosts.Clear();
        Offset = 0;
        Warning = false;
    }

    /// <summary>Where a cell's bubble is drawn now (board pixels).</summary>
    public Vector2 CellPosition(BubbleCell c, int drop) =>
        ToPx(BubbleRules.CellCenter(c, drop)) + new Vector2(ShakeX, Offset);

    private Vector2 ToPx(System.Numerics.Vector2 v) => new(v.X * Unit, v.Y * Unit);

    private float ShakeX => Warning && Mathf.PosMod(_time, 1.6f) < 0.5f ? Mathf.Sin(_time * 40f) * Unit * 0.03f : 0f;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        if (_wobble.Count > 0)
        {
            var done = new List<BubbleCell>();
            foreach (var (cell, (dir, age)) in _wobble)
            {
                if (age + dt > 0.7f)
                    done.Add(cell);
                else
                    _wobble[cell] = (dir, age + dt);
            }
            foreach (var c in done)
                _wobble.Remove(c);
        }
        _floorBounce = Mathf.Max(0, _floorBounce - dt * 3f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var w = BubbleRules.Width * Unit;
        var h = BubbleRules.Height * Unit;
        var box = UiTheme.Box(new Color(1f, 1f, 1f, 0.40f), new Color(1f, 1f, 1f, 0.85f), 5, 0, 26);
        DrawStyleBox(box, new Rect2(-8, -40, w + 16, h + 40 + 6));

        // The flower line
        var floor = BrickArt.FloorLine;
        var fh = w * floor.GetHeight() / floor.GetWidth();
        var squash = Mathf.Sin(_floorBounce * Mathf.Pi) * 0.25f;
        var rh = fh * (1 - squash);
        var y = BubbleRules.FlowerLineY * Unit;
        DrawTextureRect(floor, new Rect2(0, y + fh * 0.5f - rh, w, rh), false);

        if (Board is { } board)
        {
            var size = Unit;
            foreach (var c in board.Cells())
            {
                var at = CellPosition(c, board.Drop);
                if (_wobble.TryGetValue(c, out var wob))
                {
                    var t = wob.Age;
                    if (wob.Dir == Vector2.Zero)
                    {
                        // The new bubble itself: a quick squash
                        var s = 1f + 0.12f * Mathf.Sin(t * 22f) * Mathf.Exp(-t * 7f);
                        BubbleArt.Draw(this, board.ColorAt(c), at, size * s);
                        continue;
                    }
                    at += wob.Dir * WobbleAmp * Unit * Mathf.Sin(t * 22f) * Mathf.Exp(-t * 6f);
                }
                BubbleArt.Draw(this, board.ColorAt(c), at, size);
            }
            foreach (var g in Ghosts)
                BubbleArt.Draw(this, g.Color, CellPosition(g.Cell, board.Drop), size);
            DrawCeiling(board.Drop, w);
        }
    }

    /// <summary>Strips of the ceiling plate stacked from above the screen down to the ceiling's lower edge.</summary>
    private void DrawCeiling(int drop, float w)
    {
        var tex = BubbleArt.Ceiling;
        var tileW = StripWidth * Unit;
        var tileH = tileW * tex.GetHeight() / tex.GetWidth();
        var bottom = BubbleRules.CeilingY(drop) * Unit + Offset + Unit * 0.12f; // The scallops reach a little over row 0
        var x0 = ShakeX;
        var top = -GlobalPosition.Y - 20f;
        // Upper strips first, so each lower strip's scallops lie over the one above
        var step = tileH * 0.62f;
        var count = Mathf.CeilToInt((bottom - top) / step);
        for (var i = count; i >= 0; i--)
            for (var x = 0f; x < w - 0.5f; x += tileW)
            {
                var yb = bottom - i * step;
                var tw = Mathf.Min(tileW, w - x);
                DrawTextureRectRegion(tex, new Rect2(x + x0, yb - tileH, tw, tileH),
                    new Rect2(0, 0, tex.GetWidth() * tw / tileW, tex.GetHeight()));
            }
    }
}
