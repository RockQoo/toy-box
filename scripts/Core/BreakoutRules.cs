using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToyBox.Core;

/// <summary>One step of the ball speed ramp (docs/games/10-breakout.md, "난이도"). Speed is board heights per second at "보통".</summary>
public readonly record struct BreakoutPhase(int Index, float StartTime, float Speed);

/// <summary>
/// One brick of a breakout board, in board units (the board is <see cref="BreakoutRules.Width"/> wide and 1 high, y down).
/// <see cref="Level"/> is how many hits it took when new; each hit lowers <see cref="HitsLeft"/>, and 0 breaks it.
/// </summary>
public sealed class BreakoutBrick
{
    public BreakoutBrick(int col, int row, int level)
    {
        Col = col;
        Row = row;
        Level = level;
        HitsLeft = level;
    }

    public int Col { get; }
    public int Row { get; }
    public int Level { get; }
    public int HitsLeft { get; internal set; }
    public bool Broken => HitsLeft <= 0;

    /// <summary>0 for a brick that has not been hit, then 1, 2 (which crack to draw).</summary>
    public int Damage => Level - Math.Max(HitsLeft, 0);

    public float X => BreakoutRules.BrickX(Col);
    public float Y => BreakoutRules.BrickY(Row);
    public float W => BreakoutRules.BrickWidth;
    public float H => BreakoutRules.BrickHeight;
    public Vector2 Center => new(X + W / 2, Y + H / 2);

    /// <summary>
    /// Which of the six brick colors (0..5: 연두, 하늘, 파랑, 보라, 분홍, 금색) to draw: two colors per hit level, so a hit
    /// drops it to the lower pair. Rows alternate within a pair.
    /// </summary>
    public int ColorIndex => BreakoutRules.ColorIndex(HitsLeft, Row);
}

/// <summary>The bricks of one board (판). Built from <see cref="BreakoutRules.Layouts"/>.</summary>
public sealed class BreakoutBoard
{
    public BreakoutBoard(int number)
    {
        Number = number;
        var layout = BreakoutRules.Layouts[number % BreakoutRules.Layouts.Count];
        var bonus = number / BreakoutRules.Layouts.Count; // Every pass through the five shapes is a level harder
        for (var row = 0; row < layout.Length; row++)
            for (var col = 0; col < layout[row].Length; col++)
                if (layout[row][col] is >= '1' and <= '3')
                    Bricks.Add(new BreakoutBrick(col, row, Math.Min(BreakoutRules.MaxLevel, layout[row][col] - '0' + bonus)));
        Remaining = Bricks.Count;
    }

    /// <summary>0 for the first board of a round.</summary>
    public int Number { get; }
    public List<BreakoutBrick> Bricks { get; } = new();
    public int Remaining { get; private set; }
    public bool Cleared => Remaining == 0;

    /// <summary>One hit on <paramref name="brick"/>. True if that broke it.</summary>
    public bool Hit(BreakoutBrick brick)
    {
        if (brick.Broken)
            return false;
        brick.HitsLeft--;
        if (!brick.Broken)
            return false;
        Remaining--;
        return true;
    }
}

/// <summary>A ball: center and unit direction of travel, in board units. Its speed comes from the phase table.</summary>
public struct BreakoutBall
{
    public Vector2 Position;
    public Vector2 Direction;

    public BreakoutBall(Vector2 position, Vector2 direction)
    {
        Position = position;
        Direction = direction;
    }
}

public enum BallHitKind { Wall, Ceiling, Paddle, Brick, Floor }

/// <summary>Something the ball touched during <see cref="BreakoutRules.Advance"/>. <see cref="Broke"/> for a brick that broke.</summary>
public readonly record struct BallHit(BallHitKind Kind, Vector2 At, BreakoutBrick? Brick = null, bool Broke = false);

/// <summary>
/// Breakout rules that do not need the engine: board shapes, sizes, the speed ramp, paddle bounce angles and the
/// ball movement with circle-rectangle collision (moved in small steps so a fast ball never passes through a brick).
/// Everything is in board units: x from 0 to <see cref="Width"/>, y from 0 (top) to 1 (bottom).
/// </summary>
public static class BreakoutRules
{
    public const float PlaySeconds = 60f;

    /// <summary>Board width over height (1280x720 → a 540x720 board in the middle).</summary>
    public const float Width = 0.75f;

    public const int Columns = 8;
    public const int MaxRows = 6;
    public const int MaxLevel = 3;

    public const float BrickTop = 0.07f;
    public const float RowHeight = 0.052f;
    public const float BrickGap = 0.008f;
    public const float SideMargin = 0.012f;
    public static float CellWidth => (Width - SideMargin * 2) / Columns;
    public static float BrickWidth => CellWidth - BrickGap;
    public const float BrickHeight = RowHeight - BrickGap;

    public const float BallRadius = 0.02f;

    /// <summary>Paddle: its top surface y, drawn width and height. It catches the ball over 115% of the drawn width.</summary>
    public const float PaddleY = 0.85f;
    public const float PaddleWidth = 0.22f;
    public const float PaddleHeight = 0.05f;
    public const float PaddleHitScale = 1.15f;
    public static float PaddleHitHalf => PaddleWidth * PaddleHitScale / 2;

    /// <summary>The flower line at the bottom: a ball that reaches it goes back to the paddle (no penalty).</summary>
    public const float FloorY = 0.955f;

    /// <summary>Off the paddle: straight up in the middle, up to this many degrees from vertical at the ends.</summary>
    public const float MaxBounceDegrees = 60f;

    /// <summary>The ball never travels flatter than this (degrees above the horizontal), so it does not crawl sideways.</summary>
    public const float MinDegreesFromHorizontal = 22f;

    /// <summary>A ball resting on the paddle sets off by itself after this long (a tap sends it at once).</summary>
    public const float LaunchDelay = 1f;

    /// <summary>Ball count limit with the 공 늘리기 item, the chance a broken brick drops one, its fall speed and size.</summary>
    public const int MaxBalls = 5;
    public const double ItemChance = 0.12;
    public const float ItemFallSpeed = 0.3f;
    public const float ItemSize = 0.065f;

    /// <summary>The split from the item turns the copies this many degrees off the ball's direction.</summary>
    public const float SplitDegrees = 25f;

    private static readonly BreakoutPhase[] Phases =
    {
        new(0, 0f, 0.70f),  // 0~20초
        new(1, 20f, 0.80f), // 20~40초
        new(2, 40f, 0.90f), // 40~60초
    };

    /// <summary>
    /// The five board shapes, in order ('1'..'3' = hits to break, '.' = empty). After the fifth the shapes repeat one
    /// level harder (capped at 3). Each row has <see cref="Columns"/> cells; at most <see cref="MaxRows"/> rows.
    /// </summary>
    public static readonly IReadOnlyList<string[]> Layouts = new[]
    {
        new[] // 세 줄
        {
            "11111111",
            "11111111",
            "11111111",
        },
        new[] // 산
        {
            "..2222..",
            ".111111.",
            "11111111",
        },
        new[] // 바둑판
        {
            "2.2.2.2.",
            ".1.1.1.1",
            "2.2.2.2.",
            ".1.1.1.1",
        },
        new[] // 하트
        {
            ".22..22.",
            "22222222",
            "21111112",
            ".211112.",
            "..2112..",
            "...22...",
        },
        new[] // 무지개 띠
        {
            "33333333",
            "22222222",
            "11111111",
            "11111111",
        },
    };

    public static int PhaseCount => Phases.Length;

    public static BreakoutPhase PhaseAt(float elapsed)
    {
        var phase = Phases[0];
        foreach (var p in Phases)
            if (elapsed >= p.StartTime)
                phase = p;
        return phase;
    }

    /// <summary>Ball speed in board heights per second at this time with the "속도" option.</summary>
    public static float BallSpeed(float elapsed, StartSpeed speed) =>
        PhaseAt(elapsed).Speed * RoundClock.SpeedScale(speed);

    public static float BrickX(int col) => SideMargin + col * CellWidth + BrickGap / 2;
    public static float BrickY(int row) => BrickTop + row * RowHeight + BrickGap / 2;

    /// <summary>Brick color (0..5) for the hits it has left: level 1 → 연두/하늘, 2 → 파랑/보라, 3 → 분홍/금색.</summary>
    public static int ColorIndex(int hitsLeft, int row) => (Math.Clamp(hitsLeft, 1, MaxLevel) - 1) * 2 + (row & 1);

    /// <summary>The paddle center stays where the whole drawn paddle is on the board.</summary>
    public static float ClampPaddle(float x) => Math.Clamp(x, PaddleWidth / 2, Width - PaddleWidth / 2);

    /// <summary>Where a ball rests on the paddle before it sets off.</summary>
    public static Vector2 RestPosition(float paddleX) => new(paddleX, PaddleY - BallRadius);

    /// <summary>Direction off the paddle for a ball hitting it at <paramref name="ballX"/> (middle: straight up).</summary>
    public static Vector2 PaddleBounce(float ballX, float paddleX)
    {
        var offset = Math.Clamp((ballX - paddleX) / PaddleHitHalf, -1f, 1f);
        var angle = offset * MaxBounceDegrees * MathF.PI / 180f;
        return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
    }

    /// <summary>Launch direction from the paddle: up, 8~25 degrees to one side. <paramref name="roll"/> is in [0, 1).</summary>
    public static Vector2 LaunchDirection(double roll)
    {
        var degrees = (float)(roll * 2 - 1) * 25f;
        if (MathF.Abs(degrees) < 8f)
            degrees = degrees < 0 ? -8f : 8f;
        var angle = degrees * MathF.PI / 180f;
        return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
    }

    /// <summary>Keeps a direction at least <see cref="MinDegreesFromHorizontal"/> off the horizontal (unit length).</summary>
    public static Vector2 ClampAngle(Vector2 dir)
    {
        if (dir.LengthSquared() < 1e-12f)
            return new Vector2(0, -1);
        dir = Vector2.Normalize(dir);
        var minY = MathF.Sin(MinDegreesFromHorizontal * MathF.PI / 180f);
        if (MathF.Abs(dir.Y) >= minY)
            return dir;
        var sy = dir.Y > 0 ? 1f : -1f; // A perfectly flat ball goes up
        var sx = dir.X < 0 ? -1f : 1f;
        return new Vector2(sx * MathF.Sqrt(1 - minY * minY), sy * minY);
    }

    /// <summary>The two extra balls from the 공 늘리기 item: <paramref name="dir"/> turned each way.</summary>
    public static (Vector2 Left, Vector2 Right) SplitDirections(Vector2 dir)
    {
        var a = SplitDegrees * MathF.PI / 180f;
        return (ClampAngle(Rotate(dir, -a)), ClampAngle(Rotate(dir, a)));
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var (s, c) = MathF.SinCos(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Whether a broken brick drops a 공 늘리기 item. <paramref name="roll"/> is in [0, 1).</summary>
    public static bool DropsItem(int ballsInPlay, bool itemFalling, double roll) =>
        !itemFalling && ballsInPlay + 2 <= MaxBalls && roll < ItemChance;

    /// <summary>Whether the paddle catches a falling item centered at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static bool PaddleCatches(float x, float y, float paddleX)
    {
        var half = ItemSize / 2;
        return y + half >= PaddleY && y - half <= PaddleY + PaddleHeight && MathF.Abs(x - paddleX) <= PaddleHitHalf + half * 0.6f;
    }

    /// <summary>
    /// Where a ball moving down will cross height <paramref name="y"/>, bouncing off the side walls (bricks ignored).
    /// Null if it is not moving down.
    /// </summary>
    public static float? PredictX(Vector2 position, Vector2 direction, float y)
    {
        if (direction.Y <= 1e-4f)
            return null;
        var x = position.X + direction.X * (y - position.Y) / direction.Y;
        var span = Width - 2 * BallRadius;
        var u = (x - BallRadius) % (2 * span);
        if (u < 0)
            u += 2 * span;
        if (u > span)
            u = 2 * span - u;
        return BallRadius + u;
    }

    /// <summary>
    /// Moves a ball <paramref name="distance"/> along its direction in steps of at most half its radius, bouncing off
    /// the walls, the ceiling, the paddle (centered at <paramref name="paddleX"/>) and the bricks of <paramref name="board"/>
    /// (null while a new board slides in). Every touch goes into <paramref name="hits"/>. Stops at the flower line
    /// (a <see cref="BallHitKind.Floor"/> hit); the caller then takes the ball away.
    /// </summary>
    public static void Advance(ref BreakoutBall ball, float distance, BreakoutBoard? board, float paddleX, List<BallHit> hits)
    {
        if (distance <= 0)
            return;
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / (BallRadius * 0.5f)));
        var step = distance / steps;
        for (var i = 0; i < steps; i++)
        {
            ball.Position += ball.Direction * step;
            if (StepCollisions(ref ball, board, paddleX, hits))
                return;
        }
    }

    /// <summary>One small step's collisions. True when the ball reached the floor.</summary>
    private static bool StepCollisions(ref BreakoutBall ball, BreakoutBoard? board, float paddleX, List<BallHit> hits)
    {
        const float r = BallRadius;
        ref var p = ref ball.Position;
        ref var d = ref ball.Direction;

        if (p.X < r && d.X < 0)
        {
            p.X = r;
            d.X = -d.X;
            hits.Add(new BallHit(BallHitKind.Wall, new Vector2(0, p.Y)));
        }
        else if (p.X > Width - r && d.X > 0)
        {
            p.X = Width - r;
            d.X = -d.X;
            hits.Add(new BallHit(BallHitKind.Wall, new Vector2(Width, p.Y)));
        }
        if (p.Y < r && d.Y < 0)
        {
            p.Y = r;
            d.Y = -d.Y;
            hits.Add(new BallHit(BallHitKind.Ceiling, new Vector2(p.X, 0)));
        }

        // Paddle: generous (115% wide, and it still counts a little below the top surface)
        if (d.Y > 0 && p.Y + r >= PaddleY && p.Y <= PaddleY + PaddleHeight * 0.6f
            && MathF.Abs(p.X - paddleX) <= PaddleHitHalf + r * 0.6f)
        {
            p.Y = PaddleY - r;
            d = ClampAngle(PaddleBounce(p.X, paddleX));
            hits.Add(new BallHit(BallHitKind.Paddle, new Vector2(p.X, PaddleY)));
            return false;
        }

        if (p.Y + r >= FloorY && d.Y > 0)
        {
            hits.Add(new BallHit(BallHitKind.Floor, new Vector2(p.X, FloorY)));
            return true;
        }

        if (board is not null && TryBrick(ref ball, board) is { } hit)
            hits.Add(hit);
        return false;
    }

    /// <summary>Bounces off the brick the ball overlaps most (one brick per step) and hits it.</summary>
    private static BallHit? TryBrick(ref BreakoutBall ball, BreakoutBoard board)
    {
        const float r = BallRadius;
        var p = ball.Position;
        BreakoutBrick? best = null;
        var bestDepth = 0f;
        var bestNormal = Vector2.Zero;
        var bestPoint = Vector2.Zero;
        foreach (var brick in board.Bricks)
        {
            if (brick.Broken)
                continue;
            var closest = new Vector2(Math.Clamp(p.X, brick.X, brick.X + brick.W), Math.Clamp(p.Y, brick.Y, brick.Y + brick.H));
            var delta = p - closest;
            var dist2 = delta.LengthSquared();
            if (dist2 >= r * r)
                continue;
            Vector2 normal;
            float depth;
            if (dist2 > 1e-12f)
            {
                var dist = MathF.Sqrt(dist2);
                normal = delta / dist;
                depth = r - dist;
            }
            else
            {
                // Center inside the brick: out through the nearest side
                var left = p.X - brick.X;
                var right = brick.X + brick.W - p.X;
                var top = p.Y - brick.Y;
                var bottom = brick.Y + brick.H - p.Y;
                var min = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                normal = min == left ? new Vector2(-1, 0) : min == right ? new Vector2(1, 0)
                    : min == top ? new Vector2(0, -1) : new Vector2(0, 1);
                depth = r + min;
            }
            if (depth > bestDepth)
            {
                best = brick;
                bestDepth = depth;
                bestNormal = normal;
                bestPoint = closest;
            }
        }
        if (best is null)
            return null;

        ball.Position += bestNormal * bestDepth;
        var dot = Vector2.Dot(ball.Direction, bestNormal);
        if (dot < 0)
            ball.Direction = ClampAngle(ball.Direction - 2 * dot * bestNormal);
        var broke = board.Hit(best);
        return new BallHit(BallHitKind.Brick, bestPoint, best, broke);
    }
}
