using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Breakout;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// Draws the numbered bricks and the 공 늘리기 bubbles of a <see cref="SwipeBrickBoard"/> (board units scaled by
/// <see cref="Unit"/>). A hit brick shakes and its number bounces; a broken one swells and fades. Fixed arrays, no
/// allocation per frame. The node's position is moved by the game for the row drop.
/// </summary>
public partial class SwipeBrickField : Node2D
{
    private const int Cells = SwipeBrickRules.Columns * SwipeBrickRules.Rows;
    private const float WobbleTime = 0.28f;
    private const float PopTime = 0.18f;

    private static readonly string[] Numbers = BuildNumbers();

    private readonly float[] _wobble = new float[Cells];   // Time left
    private readonly float[] _pop = new float[Cells];      // Time left
    private readonly int[] _popColor = new int[Cells];
    private float _time;

    public SwipeBrickBoard? Board { get; set; }
    public float Unit { get; set; } = 720f;

    private static string[] BuildNumbers()
    {
        var a = new string[1000];
        for (var i = 0; i < a.Length; i++)
            a[i] = i.ToString();
        return a;
    }

    private static int Index(int col, int row) => row * SwipeBrickRules.Columns + col;

    /// <summary>Forget running shakes and pops (after the rows moved).</summary>
    public void ResetEffects()
    {
        System.Array.Clear(_wobble);
        System.Array.Clear(_pop);
    }

    /// <summary>The ball touched this brick. Call after the board took the hit.</summary>
    public void Hit(int col, int row, bool broke, int colorBefore)
    {
        var i = Index(col, row);
        if (broke)
        {
            _pop[i] = PopTime;
            _popColor[i] = colorBefore;
        }
        else
        {
            _wobble[i] = WobbleTime;
        }
    }

    /// <summary>Pixel center of a cell, relative to this node.</summary>
    public Vector2 CellCenter(int col, int row)
    {
        var c = SwipeBrickRules.CellCenter(col, row);
        return new Vector2(c.X * Unit, c.Y * Unit);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        for (var i = 0; i < Cells; i++)
        {
            if (_wobble[i] > 0)
                _wobble[i] = Mathf.Max(0, _wobble[i] - dt);
            if (_pop[i] > 0)
                _pop[i] = Mathf.Max(0, _pop[i] - dt);
        }
        QueueRedraw(); // The bubbles bob all the time
    }

    public override void _Draw()
    {
        if (Board is not { } board)
            return;
        var font = UiTheme.Font;
        var item = BrickArt.ItemBallAdd;
        var itemSize = SwipeBrickRules.ItemRadius * 2.3f * Unit;
        for (var row = 0; row < SwipeBrickRules.Rows; row++)
        {
            for (var col = 0; col < SwipeBrickRules.Columns; col++)
            {
                var i = Index(col, row);
                var (bx, by, bs) = SwipeBrickRules.BrickRect(col, row);
                var rect = new Rect2(bx * Unit, by * Unit, bs * Unit, bs * Unit);
                var hits = board.HitsAt(col, row);
                if (hits > 0)
                {
                    var bump = 0f;
                    if (_wobble[i] > 0)
                    {
                        var u = 1 - _wobble[i] / WobbleTime;
                        rect.Position += new Vector2(Mathf.Sin(u * 26f) * (1 - u) * rect.Size.X * 0.06f, 0);
                        rect = rect.Grow(rect.Size.X * 0.04f * (1 - u));
                        bump = Mathf.Sin(u * Mathf.Pi) * 0.35f;
                    }
                    var damage = SwipeBrickRules.Damage(board.StartAt(col, row), hits);
                    BrickArt.DrawBrick(this, rect, SwipeBrickRules.ColorIndex(hits), damage);

                    var text = hits < Numbers.Length ? Numbers[hits] : hits.ToString();
                    var size = (int)(Unit * SwipeBrickRules.Cell * (text.Length >= 3 ? 0.34f : 0.42f) * (1 + bump));
                    var center = rect.GetCenter();
                    var baseline = new Vector2(rect.Position.X, center.Y + size * 0.36f);
                    DrawStringOutline(font, baseline, text, HorizontalAlignment.Center, rect.Size.X, size, Mathf.Max(4, size / 5), UiTheme.Ink);
                    DrawString(font, baseline, text, HorizontalAlignment.Center, rect.Size.X, size, Colors.White);
                }
                else if (_pop[i] > 0)
                {
                    var u = 1 - _pop[i] / PopTime;
                    BrickArt.DrawBrick(this, rect.Grow(rect.Size.X * 0.3f * u), _popColor[i], 0, Colors.White with { A = 1 - u });
                }
                else if (board.ItemAt(col, row))
                {
                    var c = rect.GetCenter() + new Vector2(0, Mathf.Sin(_time * 3f + col) * Unit * 0.004f);
                    var s = itemSize * (1 + 0.05f * Mathf.Sin(_time * 5f + row));
                    DrawTextureRect(item, new Rect2(c - new Vector2(s, s) / 2, new Vector2(s, s)), false);
                }
            }
        }
    }
}
