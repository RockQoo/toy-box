using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToyBox.Core;

/// <summary>공 쏘기 벽돌깨기 difficulty. Each has its own leaderboard (see <see cref="SwipeBrickRules.Variants"/>).</summary>
public enum SwipeLevel { Easy, Normal }

/// <summary>
/// The 7x9 grid of a 공 쏘기 벽돌깨기 round (docs/games/12-swipe-brick.md): numbered bricks (hits left) and
/// 공 늘리기 bubbles, the turn count, the ball count and the score. Row 0 is the top; a new row always comes in there
/// after everything moves down one row. Whatever moves past the last row reaches the flower line and turns into
/// flowers (no score, no game over).
/// </summary>
public sealed class SwipeBrickBoard
{
    private readonly int[] _hits = new int[SwipeBrickRules.Columns * SwipeBrickRules.Rows];
    private readonly int[] _start = new int[SwipeBrickRules.Columns * SwipeBrickRules.Rows];
    private readonly bool[] _item = new bool[SwipeBrickRules.Columns * SwipeBrickRules.Rows];

    public SwipeBrickBoard(SwipeLevel level) => Level = level;

    public SwipeLevel Level { get; }

    /// <summary>Rows brought in so far (the turn number of the newest row).</summary>
    public int Turn { get; private set; }

    /// <summary>Shots fired so far (out of <see cref="SwipeBrickRules.Shots"/>).</summary>
    public int ShotsTaken { get; private set; }
    public int ShotsLeft => SwipeBrickRules.Shots - ShotsTaken;

    /// <summary>Balls for the next shot (1 at the start, +1 for each bubble, up to <see cref="SwipeBrickRules.MaxBalls"/>).</summary>
    public int Balls { get; private set; } = 1;

    /// <summary>Bricks broken (one point each).</summary>
    public int Score { get; private set; }

    private static int Index(int col, int row) => row * SwipeBrickRules.Columns + col;

    private static bool Inside(int col, int row) =>
        col >= 0 && col < SwipeBrickRules.Columns && row >= 0 && row < SwipeBrickRules.Rows;

    /// <summary>Hits left on the brick in that cell (0: no brick).</summary>
    public int HitsAt(int col, int row) => Inside(col, row) ? _hits[Index(col, row)] : 0;

    /// <summary>The number the brick had when it came in (for its cracks).</summary>
    public int StartAt(int col, int row) => Inside(col, row) ? _start[Index(col, row)] : 0;

    public bool ItemAt(int col, int row) => Inside(col, row) && _item[Index(col, row)];

    public int BrickCount
    {
        get
        {
            var n = 0;
            foreach (var h in _hits)
                if (h > 0)
                    n++;
            return n;
        }
    }

    public SwipeBrickBoard Clone()
    {
        var copy = new SwipeBrickBoard(Level) { Turn = Turn, ShotsTaken = ShotsTaken, Balls = Balls, Score = Score };
        Array.Copy(_hits, copy._hits, _hits.Length);
        Array.Copy(_start, copy._start, _start.Length);
        Array.Copy(_item, copy._item, _item.Length);
        return copy;
    }

    /// <summary>Puts a brick (or clears the cell with 0) — for tests and set-ups.</summary>
    public void SetBrick(int col, int row, int hits)
    {
        _hits[Index(col, row)] = Math.Max(0, hits);
        _start[Index(col, row)] = Math.Max(0, hits);
        if (hits > 0)
            _item[Index(col, row)] = false;
    }

    public void SetItem(int col, int row, bool on)
    {
        _item[Index(col, row)] = on;
        if (on)
            _hits[Index(col, row)] = _start[Index(col, row)] = 0;
    }

    /// <summary>The next turn's row in row 0 (which must be empty: call <see cref="ShiftDown"/> first after the first row).</summary>
    public void SpawnRow(Random rng)
    {
        Turn++;
        var count = SwipeBrickRules.RowBrickCount(Level, rng);
        var cols = new int[SwipeBrickRules.Columns];
        for (var i = 0; i < cols.Length; i++)
            cols[i] = i;
        for (var i = cols.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (cols[i], cols[j]) = (cols[j], cols[i]);
        }
        for (var i = 0; i < count; i++)
            SetBrick(cols[i], 0, SwipeBrickRules.RowValue(Level, Turn, rng));
        if (SwipeBrickRules.ItemEveryTurn(Level))
            SetItem(cols[count], 0, true);
    }

    /// <summary>
    /// Everything moves down one row. Bricks and bubbles in the last row reach the flower line: they are taken off and
    /// listed in <paramref name="toFlowers"/> (column and whether it was a brick).
    /// </summary>
    public void ShiftDown(List<(int Col, bool Brick)>? toFlowers = null)
    {
        const int cols = SwipeBrickRules.Columns;
        var last = SwipeBrickRules.Rows - 1;
        for (var col = 0; col < cols; col++)
        {
            if (_hits[Index(col, last)] > 0)
                toFlowers?.Add((col, true));
            else if (_item[Index(col, last)])
                toFlowers?.Add((col, false));
        }
        Array.Copy(_hits, 0, _hits, cols, _hits.Length - cols);
        Array.Copy(_start, 0, _start, cols, _start.Length - cols);
        Array.Copy(_item, 0, _item, cols, _item.Length - cols);
        Array.Clear(_hits, 0, cols);
        Array.Clear(_start, 0, cols);
        Array.Clear(_item, 0, cols);
    }

    /// <summary>One hit on the brick in that cell. True if it broke (+1 point).</summary>
    public bool HitBrick(int col, int row)
    {
        var i = Index(col, row);
        if (_hits[i] <= 0)
            return false;
        _hits[i]--;
        if (_hits[i] > 0)
            return false;
        _start[i] = 0;
        Score++;
        return true;
    }

    /// <summary>A ball touched the bubble in that cell: one more ball from the next shot.</summary>
    public bool TakeItem(int col, int row)
    {
        var i = Index(col, row);
        if (!_item[i])
            return false;
        _item[i] = false;
        Balls = Math.Min(SwipeBrickRules.MaxBalls, Balls + 1);
        return true;
    }

    internal void CountShot() => ShotsTaken++;

    /// <summary>Takes every brick and bubble off (the end of the round: they all turn into flowers).</summary>
    public void Clear()
    {
        Array.Clear(_hits);
        Array.Clear(_start);
        Array.Clear(_item);
    }
}

public enum SwipeEventKind { Launch, Wall, Brick, Item, Land }

/// <summary>Something that happened during <see cref="SwipeShot.Tick"/>. Cells are (col, row); At is in board units.</summary>
public readonly record struct SwipeEvent(SwipeEventKind Kind, int Ball, Vector2 At, int Col = -1, int Row = -1, bool Broke = false);

/// <summary>
/// One shot: the balls leave <see cref="SwipeBrickRules.LaunchGap"/> seconds apart along the same direction, bounce off
/// the walls, the ceiling and the bricks, pick up bubbles, and stop on the flower line. Moved in fixed steps of
/// <see cref="SwipeBrickRules.FixedDt"/> so the game, the tests and the autoplay planner all see the same flight.
/// Balls still in the air after <see cref="SwipeBrickRules.RecallTime"/> come straight down (past the bricks), so a
/// shot always ends. No allocation per tick.
/// </summary>
public sealed class SwipeShot
{
    public enum BallState : byte { Waiting, Flying, Landed }

    private readonly SwipeBrickBoard _board;
    private readonly Vector2[] _pos;
    private readonly Vector2[] _dir;
    private readonly BallState[] _state;
    private readonly bool[] _recalled;
    private int _launched;
    private int _landed;

    public SwipeShot(SwipeBrickBoard board, float launchX, Vector2 direction, int balls)
    {
        _board = board;
        Count = Math.Clamp(balls, 1, SwipeBrickRules.MaxBalls);
        LaunchX = SwipeBrickRules.ClampLaunchX(launchX);
        Direction = Vector2.Normalize(direction);
        _pos = new Vector2[Count];
        _dir = new Vector2[Count];
        _state = new BallState[Count];
        _recalled = new bool[Count];
        var start = new Vector2(LaunchX, SwipeBrickRules.LaunchY);
        for (var i = 0; i < Count; i++)
        {
            _pos[i] = start;
            _dir[i] = Direction;
        }
        board.CountShot();
    }

    public int Count { get; }
    public float LaunchX { get; }
    public Vector2 Direction { get; }

    /// <summary>Seconds since the first ball left.</summary>
    public float Time { get; private set; }

    /// <summary>Where the first ball landed: the next launch spot (null until then).</summary>
    public float? FirstLandX { get; private set; }

    public int Launched => _launched;
    public int LandedCount => _landed;
    public bool Done => _landed == Count;
    public bool Recalling => Time >= SwipeBrickRules.RecallTime && !Done;

    public Vector2 Position(int ball) => _pos[ball];
    public Vector2 DirectionOf(int ball) => _dir[ball];
    public BallState State(int ball) => _state[ball];
    public bool Recalled(int ball) => _recalled[ball];

    /// <summary>One fixed step. Events go into <paramref name="events"/> (may be null).</summary>
    public void Tick(List<SwipeEvent>? events)
    {
        if (Done)
            return;
        // Launch every ball whose time has come
        while (_launched < Count && Time >= _launched * SwipeBrickRules.LaunchGap)
        {
            _state[_launched] = BallState.Flying;
            events?.Add(new SwipeEvent(SwipeEventKind.Launch, _launched, _pos[_launched]));
            _launched++;
        }
        var recall = Time >= SwipeBrickRules.RecallTime;
        var distance = SwipeBrickRules.BallSpeed * SwipeBrickRules.FixedDt;
        for (var i = 0; i < _launched; i++)
        {
            if (_state[i] != BallState.Flying)
                continue;
            if (recall && !_recalled[i])
            {
                _recalled[i] = true;
                _dir[i] = new Vector2(0, 1);
            }
            if (Move(i, distance, events))
            {
                _state[i] = BallState.Landed;
                _landed++;
                var x = _pos[i].X;
                FirstLandX ??= x;
                events?.Add(new SwipeEvent(SwipeEventKind.Land, i, _pos[i]));
            }
        }
        Time += SwipeBrickRules.FixedDt;
    }

    /// <summary>Runs the whole shot (for tests and the autoplay planner). Gives up after <paramref name="maxSeconds"/>.</summary>
    public void RunToEnd(float maxSeconds = 60f, List<SwipeEvent>? events = null)
    {
        var steps = (int)(maxSeconds / SwipeBrickRules.FixedDt);
        for (var s = 0; s < steps && !Done; s++)
            Tick(events);
    }

    /// <summary>Moves one ball in steps of at most half its radius. True when it reached the flower line.</summary>
    private bool Move(int i, float distance, List<SwipeEvent>? events)
    {
        const float r = SwipeBrickRules.BallRadius;
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / (r * 0.5f)));
        var step = distance / steps;
        for (var s = 0; s < steps; s++)
        {
            _pos[i] += _dir[i] * step;
            ref var p = ref _pos[i];
            ref var d = ref _dir[i];
            if (_recalled[i])
            {
                if (p.Y >= SwipeBrickRules.LaunchY)
                {
                    p.Y = SwipeBrickRules.LaunchY;
                    return true;
                }
                continue;
            }
            if (SwipeBrickRules.BounceWalls(ref p, ref d) is { } wall)
                events?.Add(new SwipeEvent(SwipeEventKind.Wall, i, wall));
            if (p.Y >= SwipeBrickRules.LaunchY && d.Y > 0)
            {
                p.Y = SwipeBrickRules.LaunchY;
                return true;
            }
            if (SwipeBrickRules.TouchItem(_board, p) is { } item && _board.TakeItem(item.Col, item.Row))
                events?.Add(new SwipeEvent(SwipeEventKind.Item, i, SwipeBrickRules.CellCenter(item.Col, item.Row), item.Col, item.Row));
            if (SwipeBrickRules.BounceBrick(_board, ref p, ref d) is { } hit)
            {
                var broke = _board.HitBrick(hit.Col, hit.Row);
                events?.Add(new SwipeEvent(SwipeEventKind.Brick, i, hit.At, hit.Col, hit.Row, broke));
            }
        }
        return false;
    }
}

/// <summary>
/// 공 쏘기 벽돌깨기 rules that do not need the engine (docs/games/12-swipe-brick.md): the grid and sizes, new rows per
/// difficulty, aiming, ball movement with circle-rectangle bounces and the aim line prediction (the same bounce code).
/// Everything is in board units: x from 0 to <see cref="Width"/>, y from 0 (top) to 1 (bottom of the screen).
/// </summary>
public static class SwipeBrickRules
{
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
    };

    public static string VariantId(SwipeLevel level) => Variants[(int)level].Id;

    public const int Shots = 20;
    public const int Columns = 7;
    public const int Rows = 9;
    public const int MaxBalls = 60;

    /// <summary>Board width over height (1280x720 → a 490x720 board in the middle).</summary>
    public const float Width = 0.68f;
    public const float Cell = Width / Columns;
    public const float Top = 0.012f;
    /// <summary>The flower line: balls stop on it, and bricks that move past the last row reach it.</summary>
    public const float FloorY = Top + Rows * Cell;
    /// <summary>Space between a brick and its cell edge (the gap between two bricks is twice this).</summary>
    public const float BrickInset = 0.005f;

    public const float BallRadius = 0.016f;
    public const float ItemRadius = 0.03f;
    /// <summary>Where a ball sits before it leaves and where it stops (resting on the flower line).</summary>
    public const float LaunchY = FloorY - BallRadius;

    /// <summary>Ball speed in board heights per second (the fast-forward button doubles the game clock, not this).</summary>
    public const float BallSpeed = 1.5f;
    public const float LaunchGap = 0.08f;
    public const float FixedDt = 1f / 120f;
    /// <summary>Balls still flying after this many seconds come straight down.</summary>
    public const float RecallTime = 12f;

    /// <summary>Aims flatter than this (degrees above the horizontal) are not fired.</summary>
    public const float MinAimDegrees = 10f;
    /// <summary>After a bounce the ball never travels flatter than this, so it does not crawl sideways for long.</summary>
    public const float MinTravelDegrees = 8f;

    // ---- Rows ----

    /// <summary>Bricks in a new row: 쉬움 2~4, 보통 3~5 (always leaves a cell for the bubble).</summary>
    public static int RowBrickCount(SwipeLevel level, Random rng) =>
        level == SwipeLevel.Easy ? rng.Next(2, 5) : rng.Next(3, 6);

    /// <summary>A new brick's number: 쉬움 half the turn (at least 1), 보통 the turn (one brick in five doubled).</summary>
    public static int RowValue(SwipeLevel level, int turn, Random rng) => level == SwipeLevel.Easy
        ? Math.Max(1, turn / 2)
        : Math.Max(1, turn) * (rng.Next(5) == 0 ? 2 : 1);

    /// <summary>Both levels get a 공 늘리기 bubble in every new row (기획서 표).</summary>
    public static bool ItemEveryTurn(SwipeLevel level) => true;

    /// <summary>Brick color (0..5 → brick_1..6) for its number: 1~4, 5~9, 10~19, 20~29, 30~49, 50+.</summary>
    public static int ColorIndex(int hits) => hits switch
    {
        < 5 => 0,
        < 10 => 1,
        < 20 => 2,
        < 30 => 3,
        < 50 => 4,
        _ => 5,
    };

    /// <summary>Crack to draw (0 none, 1, 2) for how much of its starting number a brick has lost.</summary>
    public static int Damage(int start, int left)
    {
        if (start <= 1 || left <= 0 || left >= start)
            return 0;
        var ratio = left / (float)start;
        return ratio <= 1f / 3f ? 2 : ratio <= 2f / 3f ? 1 : 0;
    }

    // ---- Geometry ----

    public static float CellX(int col) => col * Cell;
    public static float CellY(int row) => Top + row * Cell;
    public static Vector2 CellCenter(int col, int row) => new(CellX(col) + Cell / 2, CellY(row) + Cell / 2);

    /// <summary>A brick's rectangle (x, y, size) inside its cell.</summary>
    public static (float X, float Y, float Size) BrickRect(int col, int row) =>
        (CellX(col) + BrickInset, CellY(row) + BrickInset, Cell - BrickInset * 2);

    public static float ClampLaunchX(float x) => Math.Clamp(x, BallRadius, Width - BallRadius);

    // ---- Aiming ----

    /// <summary>Unit direction for an aim angle in degrees (90 = straight up, smaller = to the right).</summary>
    public static Vector2 DirectionFromAngle(float degrees)
    {
        var a = degrees * MathF.PI / 180f;
        return new Vector2(MathF.Cos(a), -MathF.Sin(a));
    }

    public static float AngleOf(Vector2 dir) => MathF.Atan2(-dir.Y, dir.X) * 180f / MathF.PI;

    public static float ClampAimAngle(float degrees) => Math.Clamp(degrees, MinAimDegrees, 180f - MinAimDegrees);

    /// <summary>Whether a direction points up steeply enough to fire.</summary>
    public static bool CanFire(Vector2 dir)
    {
        if (dir.LengthSquared() < 1e-10f)
            return false;
        var angle = AngleOf(Vector2.Normalize(dir));
        return angle >= MinAimDegrees - 1e-3f && angle <= 180f - MinAimDegrees + 1e-3f;
    }

    /// <summary>Aim from the launch spot toward a finger at <paramref name="target"/> (null when it is too close).</summary>
    public static Vector2? AimToward(float launchX, Vector2 target)
    {
        var d = target - new Vector2(launchX, LaunchY);
        return d.LengthSquared() < 0.0004f ? null : Vector2.Normalize(d);
    }

    /// <summary>Keeps a travel direction at least <see cref="MinTravelDegrees"/> off the horizontal (unit length).</summary>
    public static Vector2 ClampTravel(Vector2 dir)
    {
        if (dir.LengthSquared() < 1e-12f)
            return new Vector2(0, -1);
        dir = Vector2.Normalize(dir);
        var minY = MathF.Sin(MinTravelDegrees * MathF.PI / 180f);
        if (MathF.Abs(dir.Y) >= minY)
            return dir;
        var sy = dir.Y > 0 ? 1f : -1f;
        var sx = dir.X < 0 ? -1f : 1f;
        return new Vector2(sx * MathF.Sqrt(1 - minY * minY), sy * minY);
    }

    // ---- Collisions (shared by the shot and the aim line) ----

    /// <summary>Bounces off the side walls and the ceiling. Returns the touch point, or null.</summary>
    internal static Vector2? BounceWalls(ref Vector2 p, ref Vector2 d)
    {
        const float r = BallRadius;
        Vector2? at = null;
        if (p.X < r && d.X < 0)
        {
            p.X = r;
            d.X = -d.X;
            at = new Vector2(0, p.Y);
        }
        else if (p.X > Width - r && d.X > 0)
        {
            p.X = Width - r;
            d.X = -d.X;
            at = new Vector2(Width, p.Y);
        }
        if (p.Y < r && d.Y < 0)
        {
            p.Y = r;
            d.Y = -d.Y;
            at = new Vector2(p.X, 0);
        }
        if (at is not null)
            d = ClampTravel(d);
        return at;
    }

    /// <summary>The bubble a ball at <paramref name="p"/> touches, if any.</summary>
    internal static (int Col, int Row)? TouchItem(SwipeBrickBoard board, Vector2 p)
    {
        var col = (int)MathF.Floor(p.X / Cell);
        var row = (int)MathF.Floor((p.Y - Top) / Cell);
        const float reach = (BallRadius + ItemRadius) * (BallRadius + ItemRadius);
        for (var r = row - 1; r <= row + 1; r++)
            for (var c = col - 1; c <= col + 1; c++)
                if (board.ItemAt(c, r) && Vector2.DistanceSquared(p, CellCenter(c, r)) <= reach)
                    return (c, r);
        return null;
    }

    /// <summary>
    /// Bounces off the brick the ball overlaps most (one per step; only the cells around the ball are checked).
    /// Returns that brick's cell and the touch point; the caller decides whether to hit it.
    /// </summary>
    internal static (int Col, int Row, Vector2 At)? BounceBrick(SwipeBrickBoard board, ref Vector2 p, ref Vector2 d)
    {
        const float r = BallRadius;
        var c0 = (int)MathF.Floor((p.X - r) / Cell);
        var c1 = (int)MathF.Floor((p.X + r) / Cell);
        var r0 = (int)MathF.Floor((p.Y - r - Top) / Cell);
        var r1 = (int)MathF.Floor((p.Y + r - Top) / Cell);
        var bestDepth = 0f;
        var bestNormal = Vector2.Zero;
        var bestPoint = Vector2.Zero;
        int bestCol = -1, bestRow = -1;
        for (var row = r0; row <= r1; row++)
        {
            for (var col = c0; col <= c1; col++)
            {
                if (board.HitsAt(col, row) <= 0)
                    continue;
                var (bx, by, size) = BrickRect(col, row);
                var closest = new Vector2(Math.Clamp(p.X, bx, bx + size), Math.Clamp(p.Y, by, by + size));
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
                    var left = p.X - bx;
                    var right = bx + size - p.X;
                    var top = p.Y - by;
                    var bottom = by + size - p.Y;
                    var min = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                    normal = min == left ? new Vector2(-1, 0) : min == right ? new Vector2(1, 0)
                        : min == top ? new Vector2(0, -1) : new Vector2(0, 1);
                    depth = r + min;
                }
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    bestNormal = normal;
                    bestPoint = closest;
                    bestCol = col;
                    bestRow = row;
                }
            }
        }
        if (bestCol < 0)
            return null;
        p += bestNormal * bestDepth;
        var dot = Vector2.Dot(d, bestNormal);
        if (dot < 0)
            d = ClampTravel(d - 2 * dot * bestNormal);
        return (bestCol, bestRow, bestPoint);
    }

    /// <summary>
    /// The aim line: from the launch spot along <paramref name="dir"/> to where the ball first touches a wall, the
    /// ceiling or a brick (bubbles are passed through, like the real ball). Uses the same bounce code as the shot.
    /// Returns the ball center at that touch and its direction just after.
    /// </summary>
    public static (Vector2 End, Vector2 After) PredictFirstBounce(SwipeBrickBoard board, float launchX, Vector2 dir)
    {
        var p = new Vector2(ClampLaunchX(launchX), LaunchY);
        var d = Vector2.Normalize(dir);
        const float step = BallRadius * 0.5f;
        for (var i = 0; i < 400; i++)
        {
            p += d * step;
            var before = d;
            if (BounceWalls(ref p, ref d) is not null || BounceBrick(board, ref p, ref d) is not null)
                return (p, d);
            if (p.Y >= LaunchY && before.Y > 0)
                return (p, d);
        }
        return (p, d);
    }

    // ---- Autoplay planner ----

    /// <summary>
    /// The aim angle (degrees) that breaks the most bricks this shot (then the most bubbles and hits), found by
    /// flying the whole shot on a copy of the board for angles from 12 to 168 degrees.
    /// <paramref name="fewest"/> picks the weakest shot instead (the autoplay uses it to let rows reach the flowers).
    /// </summary>
    public static float BestAngle(SwipeBrickBoard board, float launchX, float stepDegrees = 4f, bool fewest = false)
    {
        var bestAngle = 90f;
        var bestValue = float.MinValue;
        var events = new List<SwipeEvent>();
        for (var angle = MinAimDegrees + 2f; angle <= 180f - MinAimDegrees - 2f + 1e-3f; angle += stepDegrees)
        {
            var copy = board.Clone();
            var shot = new SwipeShot(copy, launchX, DirectionFromAngle(angle), copy.Balls);
            events.Clear();
            shot.RunToEnd(SwipeBrickRules.RecallTime + 4f, events);
            var value = (copy.Score - board.Score) * 100f + (copy.Balls - board.Balls) * 60f;
            foreach (var e in events)
                if (e.Kind == SwipeEventKind.Brick)
                    value += 1f;
            // A slight lean toward steeper shots breaks ties
            value -= MathF.Abs(angle - 90f) * 0.01f;
            if (fewest)
                value = -value;
            if (value > bestValue)
            {
                bestValue = value;
                bestAngle = angle;
            }
        }
        return bestAngle;
    }
}
