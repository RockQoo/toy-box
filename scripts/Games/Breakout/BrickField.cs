using System.Collections.Generic;
using Godot;
using ToyBox.Core;

namespace ToyBox.Games.Breakout;

/// <summary>
/// Draws the bricks of a <see cref="BreakoutBoard"/>. Board units are scaled by <see cref="Unit"/> (pixels per board
/// height). A hit brick shakes for a moment; a broken one swells and fades out.
/// </summary>
public partial class BrickField : Node2D
{
    private const float WobbleTime = 0.3f;
    private const float PopTime = 0.16f;

    private readonly Dictionary<BreakoutBrick, float> _wobble = new();
    private readonly Dictionary<BreakoutBrick, float> _popping = new();

    public BreakoutBoard? Board { get; private set; }
    public float Unit { get; set; } = 720f;

    public void Show(BreakoutBoard board)
    {
        Board = board;
        _wobble.Clear();
        _popping.Clear();
        QueueRedraw();
    }

    /// <summary>Pixel rect of a brick, relative to this node.</summary>
    public Rect2 BrickRect(BreakoutBrick b) => new(b.X * Unit, b.Y * Unit, b.W * Unit, b.H * Unit);

    public void Hit(BreakoutBrick brick)
    {
        if (brick.Broken)
            _popping[brick] = 0f;
        else
            _wobble[brick] = 0f;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_wobble.Count == 0 && _popping.Count == 0)
            return;
        var dt = (float)delta;
        Advance(_wobble, dt, WobbleTime);
        Advance(_popping, dt, PopTime);
        QueueRedraw();
    }

    private static void Advance(Dictionary<BreakoutBrick, float> timers, float dt, float life)
    {
        var done = new List<BreakoutBrick>();
        foreach (var key in new List<BreakoutBrick>(timers.Keys))
        {
            var t = timers[key] + dt;
            if (t >= life)
                done.Add(key);
            else
                timers[key] = t;
        }
        foreach (var key in done)
            timers.Remove(key);
    }

    public override void _Draw()
    {
        if (Board is null)
            return;
        foreach (var brick in Board.Bricks)
        {
            var rect = BrickRect(brick);
            if (brick.Broken)
            {
                if (!_popping.TryGetValue(brick, out var pt))
                    continue;
                var u = pt / PopTime;
                rect = rect.Grow(rect.Size.Y * 0.35f * u);
                // The color it had just before breaking (level 1 pair)
                BrickArt.DrawBrick(this, rect, BreakoutRules.ColorIndex(1, brick.Row), 0, Colors.White with { A = 1 - u });
                continue;
            }
            if (_wobble.TryGetValue(brick, out var wt))
            {
                var u = wt / WobbleTime;
                var shake = Mathf.Sin(u * 28f) * (1 - u) * rect.Size.Y * 0.12f;
                rect.Position += new Vector2(shake, 0);
                rect = rect.Grow(rect.Size.Y * 0.06f * (1 - u));
            }
            BrickArt.DrawBrick(this, rect, brick.ColorIndex, brick.Damage);
        }
    }
}
