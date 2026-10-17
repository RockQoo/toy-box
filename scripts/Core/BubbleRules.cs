using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToyBox.Core;

/// <summary>방울 쏘기 difficulty. Each has its own leaderboard (see <see cref="BubbleRules.Variants"/>).</summary>
public enum BubbleLevel { Easy, Normal, Hard }

/// <summary>
/// A cell of the staggered bubble grid. Row 0 hangs from the ceiling. Even rows hold 8 bubbles, odd rows 7, shifted
/// half a bubble to the right (the parity belongs to the grid row, so it never changes when the ceiling moves).
/// </summary>
public readonly record struct BubbleCell(int Col, int Row);

/// <summary>
/// 방울 쏘기 rules that do not need the engine (docs/games/13-bubble.md). Distances are in bubble diameters: the board
/// is 8 wide and <see cref="Height"/> tall, (0, 0) is its top-left corner and y grows downward.
/// </summary>
public static class BubbleRules
{
    public const int Columns = 8;
    /// <summary>Grid rows kept in the model (more than ever fit above the flower line).</summary>
    public const int GridRows = 12;
    /// <summary>A bubble whose slot (grid row + ceiling drop) is this or more touches the flower line.</summary>
    public const int DangerSlot = 10;
    /// <summary>Bubble colors in the art: red, yellow, green, blue, purple, orange.</summary>
    public const int PaletteSize = 6;
    public const float Width = Columns;
    public const float TopMargin = 0.35f;
    public const float Height = 11.1f;
    public const float LaunchX = Width / 2;
    public const float LaunchY = 10.15f;
    /// <summary>A flying bubble stops when its center comes this close to a bubble on the board (a little forgiving).</summary>
    public const float CollideDistance = 0.88f;
    public const float TraceStep = 0.04f;
    /// <summary>Shots lower than this many degrees above the horizontal are not fired.</summary>
    public const float MinAngle = 12f;
    /// <summary>Flight speed in diameters per second.</summary>
    public const float ShotSpeed = 15f;
    /// <summary>The ceiling shakes this many missed shots before it comes down.</summary>
    public const int WarnShots = 2;
    /// <summary>Same-colored bubbles in one group that pop.</summary>
    public const int MatchCount = 3;

    public static readonly float RowHeight = MathF.Sqrt(3f) / 2f;

    /// <summary>Leaderboard variants, easiest first: more colors and rows are not comparable with fewer.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(BubbleLevel level) => Variants[(int)level].Id;

    /// <summary>Colors on a board: 3 / 4 / 5 (the art has 6; 6 is too many for a child).</summary>
    public static int ColorCount(BubbleLevel level) => level switch
    {
        BubbleLevel.Easy => 3,
        BubbleLevel.Normal => 4,
        _ => 5,
    };

    /// <summary>Rows of bubbles at the start: 4 / 5 / 6.</summary>
    public static int StartRows(BubbleLevel level) => level switch
    {
        BubbleLevel.Easy => 4,
        BubbleLevel.Normal => 5,
        _ => 6,
    };

    /// <summary>Missed shots (nothing popped) before the ceiling comes down one row. 0: never (쉬움).</summary>
    public static int DropEvery(BubbleLevel level) => level switch
    {
        BubbleLevel.Easy => 0,
        BubbleLevel.Normal => 6,
        _ => 4,
    };

    /// <summary>
    /// The curated boards: one generator seed per board, in play order ("다음 판" walks this list and wraps around).
    /// Picked from a scan of seeds: every color at least 3 times, no starting group bigger than 9 (no one-shot wipe-out),
    /// and the bot needs a fair number of shots (easy 8-14, normal 8-15, hard 11-20). BubbleRulesTests checks them.
    /// </summary>
    private static readonly int[][] Seeds =
    {
        new[] { 107, 104, 108, 141, 149 },
        new[] { 201, 218, 236, 213, 225 },
        new[] { 309, 329, 316, 347, 320 },
    };

    public static int BoardCount(BubbleLevel level) => Seeds[(int)level].Length;

    public static int Seed(BubbleLevel level, int index) => Seeds[(int)level][Wrap(level, index)];

    /// <summary>Index into the level's list, wrapped (so a stale saved index still works).</summary>
    public static int Wrap(BubbleLevel level, int index)
    {
        var n = BoardCount(level);
        return ((index % n) + n) % n;
    }

    /// <summary>The <paramref name="index"/>-th curated board of a level (always the same board for the same index).</summary>
    public static BubbleBoard Board(BubbleLevel level, int index) => Generate(level, Seed(level, index));

    /// <summary>Records list "45.2초 · 18번" (time, then shots).</summary>
    public static string FormatRecord(long timeMs, int shots) => $"{timeMs / 1000f:0.0}초 · {shots}번";

    // ---- Grid geometry ----

    public static int CellCount(int row) => (row & 1) == 0 ? Columns : Columns - 1;

    public static bool Inside(BubbleCell c) => c.Row >= 0 && c.Row < GridRows && c.Col >= 0 && c.Col < CellCount(c.Row);

    public static float CellX(int col, int row) => ((row & 1) == 0 ? 0.5f : 1f) + col;

    /// <summary>Center height of a grid row when the ceiling is <paramref name="drop"/> rows down.</summary>
    public static float CellY(int row, int drop) => TopMargin + 0.5f + (row + drop) * RowHeight;

    public static Vector2 CellCenter(BubbleCell c, int drop) => new(CellX(c.Col, c.Row), CellY(c.Row, drop));

    /// <summary>The ceiling's lower edge (where row 0 hangs).</summary>
    public static float CeilingY(int drop) => TopMargin + drop * RowHeight;

    /// <summary>The flower line: the lower edge of the last slot above it.</summary>
    public static float FlowerLineY => TopMargin + 1f + (DangerSlot - 1) * RowHeight;

    /// <summary>The up to six touching cells.</summary>
    public static IEnumerable<BubbleCell> Neighbors(BubbleCell c)
    {
        var left = new BubbleCell(c.Col - 1, c.Row);
        if (Inside(left))
            yield return left;
        var right = new BubbleCell(c.Col + 1, c.Row);
        if (Inside(right))
            yield return right;
        // Even rows sit half a bubble left of odd rows: an even cell touches odd cells col-1 and col,
        // an odd cell touches even cells col and col+1
        var first = (c.Row & 1) == 0 ? c.Col - 1 : c.Col;
        for (var dy = -1; dy <= 1; dy += 2)
            for (var i = 0; i < 2; i++)
            {
                var n = new BubbleCell(first + i, c.Row + dy);
                if (Inside(n))
                    yield return n;
            }
    }

    // ---- Aiming ----

    /// <summary>Degrees above the horizontal, 90 straight up, measured from the right.</summary>
    public static Vector2 DirectionFromAngle(float degrees)
    {
        var r = degrees * MathF.PI / 180f;
        return new Vector2(MathF.Cos(r), -MathF.Sin(r));
    }

    public static float AngleOf(Vector2 dir) => MathF.Atan2(-dir.Y, dir.X) * 180f / MathF.PI;

    public static float ClampAimAngle(float degrees) => Math.Clamp(degrees, MinAngle, 180f - MinAngle);

    public static bool CanFire(Vector2 dir)
    {
        var a = AngleOf(dir);
        return a >= MinAngle - 1e-3f && a <= 180f - MinAngle + 1e-3f;
    }

    /// <summary>Direction from the launcher toward a point on the board (null when the point is on the launcher).</summary>
    public static Vector2? AimToward(Vector2 point)
    {
        var d = point - new Vector2(LaunchX, LaunchY);
        return d.LengthSquared() < 0.04f ? null : Vector2.Normalize(d);
    }

    /// <summary>
    /// Flies a bubble from the launcher in <paramref name="dir"/>: straight, mirrored at the side walls, until it
    /// touches the ceiling or comes within <see cref="CollideDistance"/> of a bubble; then it takes the nearest free
    /// cell next to the stack (<see cref="BubbleBoard.NearestFreeCell"/>).
    /// </summary>
    public static BubbleTrace Trace(BubbleBoard board, Vector2 dir)
    {
        var d = Vector2.Normalize(dir);
        var p = new Vector2(LaunchX, LaunchY);
        var points = new List<Vector2> { p };
        var ceiling = CeilingY(board.Drop) + 0.5f;
        const float left = 0.5f;
        const float right = Width - 0.5f;
        for (var step = 0; step < 6000; step++)
        {
            var q = p;
            p += d * TraceStep;
            if (p.X < left || p.X > right)
            {
                var wall = p.X < left ? left : right;
                var t = (wall - q.X) / (p.X - q.X);
                points.Add(new Vector2(wall, q.Y + (p.Y - q.Y) * t));
                p.X = 2 * wall - p.X;
                d.X = -d.X;
            }
            if (p.Y <= ceiling)
            {
                // Back along the way to the exact ceiling touch
                var t = (ceiling - q.Y) / (p.Y - q.Y);
                p = q + (p - q) * Math.Clamp(t, 0f, 1f);
                break;
            }
            if (board.Touches(p))
                break;
        }
        points.Add(p);
        return new BubbleTrace(points, board.NearestFreeCell(p));
    }

    // ---- Board generation ----

    /// <summary>Chance that a generated bubble copies a neighbor's color (makes little same-color groups).</summary>
    private const float Clump = 0.3f;

    /// <summary>The colors a seed uses: a seeded shuffle of the six, first <see cref="ColorCount"/>.</summary>
    public static int[] Palette(BubbleLevel level, int seed)
    {
        var rng = new SeededRandom(seed);
        return PickPalette(level, rng);
    }

    private static int[] PickPalette(BubbleLevel level, SeededRandom rng)
    {
        var all = new int[PaletteSize];
        for (var i = 0; i < all.Length; i++)
            all[i] = i;
        for (var i = all.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }
        var palette = new int[ColorCount(level)];
        Array.Copy(all, palette, palette.Length);
        return palette;
    }

    /// <summary>Full rows (<see cref="StartRows"/>) of the seed's palette, with little same-color groups.</summary>
    public static BubbleBoard Generate(BubbleLevel level, int seed)
    {
        var rng = new SeededRandom(seed);
        var palette = PickPalette(level, rng);
        var board = new BubbleBoard(level);
        var near = new List<int>(4);
        for (var row = 0; row < StartRows(level); row++)
            for (var col = 0; col < CellCount(row); col++)
            {
                near.Clear();
                foreach (var n in Neighbors(new BubbleCell(col, row)))
                    if (board.ColorAt(n) >= 0)
                        near.Add(board.ColorAt(n));
                var color = near.Count > 0 && rng.NextFloat() < Clump
                    ? near[rng.Next(near.Count)]
                    : palette[rng.Next(palette.Length)];
                board.Set(new BubbleCell(col, row), color);
            }
        return board;
    }

    // ---- The autoplay bot (also used by the tests to show every curated board can be cleared) ----

    /// <summary>
    /// The best shot for the current or the next bubble: tries every whole degree, simulates where it sticks and
    /// prefers pops and drops, then a spot next to the same color, high up. <paramref name="careless"/> picks the
    /// least useful spot instead (the autoplay does that a few times to show the ceiling and the flower line).
    /// </summary>
    public static BubbleShotPlan BestShot(BubbleRound round, bool careless = false)
    {
        var board = round.Board;
        var best = new BubbleShotPlan(90f, false, careless ? float.MaxValue : float.MinValue);
        var colors = round.Next != round.Current ? new[] { round.Current, round.Next } : new[] { round.Current };
        var scores = new Dictionary<(BubbleCell, int), float>();
        for (var c = 0; c < colors.Length; c++)
        {
            var color = colors[c];
            for (var angle = (int)MinAngle; angle <= 180 - (int)MinAngle; angle++)
            {
                var cell = Trace(board, DirectionFromAngle(angle)).Cell;
                if (!scores.TryGetValue((cell, color), out var score))
                {
                    score = Evaluate(board, cell, color);
                    // Straight-ish shots a little first: easier to watch
                    scores[(cell, color)] = score;
                }
                score -= MathF.Abs(angle - 90f) * 0.001f;
                if (careless ? score < best.Score : score > best.Score)
                    best = new BubbleShotPlan(angle, c == 1, score);
            }
        }
        return best;
    }

    private static float Evaluate(BubbleBoard board, BubbleCell cell, int color)
    {
        var sameNear = 0;
        foreach (var n in Neighbors(cell))
            if (board.ColorAt(n) == color)
                sameNear++;
        var copy = board.Clone();
        var result = copy.Place(cell, color);
        if (result.Popped.Count > 0)
            return 100f + result.Popped.Count * 10f + result.Dropped.Count * 15f;
        var score = sameNear * 6f - (cell.Row + board.Drop) * 0.8f;
        if (result.Cleaned.Count > 0)
            score -= 20f;
        if (result.CeilingDown)
            score -= 3f;
        return score;
    }

    /// <summary>Mulberry32: tiny, fast and stable across runtimes (same boards on every platform).</summary>
    private sealed class SeededRandom
    {
        private uint _state;

        public SeededRandom(int seed) => _state = unchecked((uint)seed * 2654435761u + 0x9E3779B9u);

        private uint NextUInt()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                var z = _state;
                z = (z ^ (z >> 15)) * (z | 1u);
                z ^= z + (z ^ (z >> 7)) * (z | 61u);
                return z ^ (z >> 14);
            }
        }

        public float NextFloat() => (NextUInt() >> 8) / 16777216f;

        public int Next(int max) => (int)(NextUInt() % (uint)max);
    }
}

/// <summary>A shot's path: the launch point, each wall bounce, the stop point; and the cell it sticks to.</summary>
public sealed record BubbleTrace(IReadOnlyList<Vector2> Points, BubbleCell Cell)
{
    public Vector2 Start => Points[0];
    public Vector2 End => Points[^1];
    public int Bounces => Points.Count - 2;

    public float Length
    {
        get
        {
            var sum = 0f;
            for (var i = 1; i < Points.Count; i++)
                sum += Vector2.Distance(Points[i - 1], Points[i]);
            return sum;
        }
    }

    /// <summary>The path up to the first wall bounce (or the whole path without one): the aim line of 보통·어려움.</summary>
    public IReadOnlyList<Vector2> UpToFirstBounce() =>
        Points.Count > 2 ? new[] { Points[0], Points[1] } : Points;
}

/// <summary>What the bot would do: the angle, whether to swap to the next bubble first, and how good it looked.</summary>
public readonly record struct BubbleShotPlan(float Angle, bool Swap, float Score);

/// <summary>A bubble and the color it had (for effects after it is gone).</summary>
public readonly record struct BubbleSpot(BubbleCell Cell, int Color);

/// <summary>Everything one landed shot did, in order.</summary>
public sealed class BubbleResult
{
    public BubbleCell Cell { get; init; }
    public int Color { get; init; }
    /// <summary>The same-color group of 3 or more that popped (including the new bubble), nearest first.</summary>
    public List<BubbleSpot> Popped { get; } = new();
    /// <summary>Bubbles no longer hanging from the ceiling after the pop: they fall.</summary>
    public List<BubbleSpot> Dropped { get; } = new();
    /// <summary>The missed-shot count reached the level's limit: the ceiling came down one row.</summary>
    public bool CeilingDown { get; set; }
    /// <summary>Bubbles that reached the flower line (and any left hanging by that): they turn into petals.</summary>
    public List<BubbleSpot> Cleaned { get; } = new();
    /// <summary>The flower-line clean-up lifted the ceiling one row.</summary>
    public bool CeilingUp { get; set; }
    /// <summary>The board is empty: the round is won.</summary>
    public bool Cleared { get; set; }
    public bool Missed => Popped.Count == 0;
}

/// <summary>
/// The bubbles on the board and the ceiling. <see cref="Place"/> runs the whole rule chain for a landed bubble:
/// pop a same-color group of 3+, drop what no longer hangs from the ceiling, count a miss (the ceiling comes down after
/// <see cref="BubbleRules.DropEvery"/> misses), and clean up anything that reached the flower line (no game over).
/// </summary>
public sealed class BubbleBoard
{
    private readonly int[] _cells = new int[BubbleRules.GridRows * BubbleRules.Columns];

    public BubbleBoard(BubbleLevel level)
    {
        Level = level;
        Array.Fill(_cells, -1);
    }

    public BubbleLevel Level { get; }

    /// <summary>Rows the ceiling has come down.</summary>
    public int Drop { get; private set; }

    /// <summary>Missed shots since the ceiling last came down.</summary>
    public int Misses { get; private set; }

    public int DropEvery => BubbleRules.DropEvery(Level);

    /// <summary>Missed shots left before the ceiling comes down (int.MaxValue when it never does).</summary>
    public int ShotsUntilDrop => DropEvery > 0 ? DropEvery - Misses : int.MaxValue;

    /// <summary>The ceiling shakes as a warning: it comes down within <see cref="BubbleRules.WarnShots"/> missed shots.</summary>
    public bool CeilingWarning => ShotsUntilDrop <= BubbleRules.WarnShots;

    private static int Index(BubbleCell c) => c.Row * BubbleRules.Columns + c.Col;

    /// <summary>Color index 0..5, or -1 for an empty cell (or outside the grid).</summary>
    public int ColorAt(BubbleCell c) => BubbleRules.Inside(c) ? _cells[Index(c)] : -1;

    public int ColorAt(int col, int row) => ColorAt(new BubbleCell(col, row));

    public void Set(BubbleCell c, int color) => _cells[Index(c)] = color;

    public void Clear(BubbleCell c) => _cells[Index(c)] = -1;

    public int Slot(BubbleCell c) => c.Row + Drop;

    public Vector2 Center(BubbleCell c) => BubbleRules.CellCenter(c, Drop);

    public int Count
    {
        get
        {
            var n = 0;
            foreach (var v in _cells)
                if (v >= 0)
                    n++;
            return n;
        }
    }

    public bool IsEmpty => Count == 0;

    /// <summary>Every occupied cell, top row first.</summary>
    public IEnumerable<BubbleCell> Cells()
    {
        for (var row = 0; row < BubbleRules.GridRows; row++)
            for (var col = 0; col < BubbleRules.CellCount(row); col++)
                if (_cells[row * BubbleRules.Columns + col] >= 0)
                    yield return new BubbleCell(col, row);
    }

    /// <summary>The distinct colors still on the board, ascending.</summary>
    public List<int> ColorsOnBoard()
    {
        var seen = new bool[BubbleRules.PaletteSize];
        foreach (var v in _cells)
            if (v >= 0)
                seen[v] = true;
        var list = new List<int>();
        for (var i = 0; i < seen.Length; i++)
            if (seen[i])
                list.Add(i);
        return list;
    }

    public bool HasColor(int color) => Array.IndexOf(_cells, color) >= 0;

    public BubbleBoard Clone()
    {
        var copy = new BubbleBoard(Level) { Drop = Drop, Misses = Misses };
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    /// <summary>Moves the ceiling (tests and set-ups).</summary>
    public void SetCeiling(int drop, int misses = 0)
    {
        Drop = Math.Max(0, drop);
        Misses = Math.Max(0, misses);
    }

    /// <summary>Whether a bubble centered at <paramref name="p"/> touches one on the board.</summary>
    public bool Touches(Vector2 p)
    {
        var limit = BubbleRules.CollideDistance * BubbleRules.CollideDistance;
        var approx = (int)MathF.Round((p.Y - BubbleRules.TopMargin - 0.5f) / BubbleRules.RowHeight) - Drop;
        for (var row = Math.Max(0, approx - 1); row <= Math.Min(BubbleRules.GridRows - 1, approx + 1); row++)
            for (var col = 0; col < BubbleRules.CellCount(row); col++)
            {
                if (_cells[row * BubbleRules.Columns + col] < 0)
                    continue;
                var c = new BubbleCell(col, row);
                if (Vector2.DistanceSquared(Center(c), p) < limit)
                    return true;
            }
        return false;
    }

    /// <summary>An empty cell a bubble can stick to: under the ceiling or next to a bubble, not past the flower line.</summary>
    public bool Attachable(BubbleCell c)
    {
        if (!BubbleRules.Inside(c) || ColorAt(c) >= 0 || Slot(c) > BubbleRules.DangerSlot)
            return false;
        if (c.Row == 0)
            return true;
        foreach (var n in BubbleRules.Neighbors(c))
            if (ColorAt(n) >= 0)
                return true;
        return false;
    }

    /// <summary>The attachable cell whose center is nearest to <paramref name="p"/> (where a stopped bubble sticks).</summary>
    public BubbleCell NearestFreeCell(Vector2 p)
    {
        var best = new BubbleCell(0, 0);
        var bestDist = float.MaxValue;
        for (var row = 0; row < BubbleRules.GridRows; row++)
            for (var col = 0; col < BubbleRules.CellCount(row); col++)
            {
                var c = new BubbleCell(col, row);
                if (!Attachable(c))
                    continue;
                var dist = Vector2.DistanceSquared(Center(c), p);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }
        return best;
    }

    /// <summary>The same-color group connected to <paramref name="start"/> (breadth first, so nearest first).</summary>
    public List<BubbleCell> SameColorGroup(BubbleCell start)
    {
        var group = new List<BubbleCell>();
        var color = ColorAt(start);
        if (color < 0)
            return group;
        var seen = new HashSet<BubbleCell> { start };
        var queue = new Queue<BubbleCell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            group.Add(c);
            foreach (var n in BubbleRules.Neighbors(c))
                if (ColorAt(n) == color && seen.Add(n))
                    queue.Enqueue(n);
        }
        return group;
    }

    /// <summary>Bubbles not connected (through any bubbles) to the top row: they would fall. Top row first.</summary>
    public List<BubbleCell> Floating()
    {
        var held = new HashSet<BubbleCell>();
        var queue = new Queue<BubbleCell>();
        for (var col = 0; col < BubbleRules.CellCount(0); col++)
        {
            var c = new BubbleCell(col, 0);
            if (ColorAt(c) >= 0 && held.Add(c))
                queue.Enqueue(c);
        }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var n in BubbleRules.Neighbors(c))
                if (ColorAt(n) >= 0 && held.Add(n))
                    queue.Enqueue(n);
        }
        var floating = new List<BubbleCell>();
        foreach (var c in Cells())
            if (!held.Contains(c))
                floating.Add(c);
        return floating;
    }

    /// <summary>Sticks a bubble to <paramref name="cell"/> and runs the rule chain (see the class summary).</summary>
    public BubbleResult Place(BubbleCell cell, int color)
    {
        Set(cell, color);
        var result = new BubbleResult { Cell = cell, Color = color };
        var group = SameColorGroup(cell);
        if (group.Count >= BubbleRules.MatchCount)
        {
            foreach (var c in group)
            {
                result.Popped.Add(new BubbleSpot(c, color));
                Clear(c);
            }
            DropFloating(result.Dropped);
        }
        else if (DropEvery > 0)
        {
            Misses++;
            if (Misses >= DropEvery)
            {
                Misses = 0;
                Drop++;
                result.CeilingDown = true;
            }
        }

        // The flower line: whatever reaches it turns into petals and the ceiling goes back up a row
        foreach (var c in Cells())
            if (Slot(c) >= BubbleRules.DangerSlot)
                result.Cleaned.Add(new BubbleSpot(c, ColorAt(c)));
        if (result.Cleaned.Count > 0)
        {
            foreach (var s in result.Cleaned)
                Clear(s.Cell);
            DropFloating(result.Cleaned);
            if (Drop > 0)
            {
                Drop--;
                result.CeilingUp = true;
            }
        }
        result.Cleared = IsEmpty;
        return result;
    }

    private void DropFloating(List<BubbleSpot> into)
    {
        foreach (var c in Floating())
        {
            into.Add(new BubbleSpot(c, ColorAt(c)));
            Clear(c);
        }
    }
}

/// <summary>
/// One play of a board: the bubbles, the bubble on the launcher and the next one, the shot count and the clock.
/// New bubbles only take colors still on the board; a queued bubble whose color has gone is recolored. The clock starts
/// with the first shot (the child may look first) and stops when the board is empty.
/// </summary>
public sealed class BubbleRound
{
    private readonly Random _rng;
    private double _seconds;

    public BubbleRound(BubbleBoard board, Random rng)
    {
        Board = board;
        _rng = rng;
        Current = PickColor();
        Next = PickColor();
    }

    public BubbleBoard Board { get; }
    public int Current { get; private set; }
    public int Next { get; private set; }
    /// <summary>The color of the bubble in the air (null when none).</summary>
    public int? InFlight { get; private set; }
    public int Shots { get; private set; }
    public bool Started => Shots > 0;
    public long TimeMs => (long)Math.Round(_seconds * 1000);

    /// <summary>Adds play time (only between the first shot and the end).</summary>
    public void Tick(double seconds)
    {
        if (Started && !Board.IsEmpty)
            _seconds += seconds;
    }

    public void Swap() => (Current, Next) = (Next, Current);

    /// <summary>Fires the launcher's bubble: the path it takes, and the queue moves up.</summary>
    public BubbleTrace Shoot(Vector2 dir)
    {
        var trace = BubbleRules.Trace(Board, dir);
        InFlight = Current;
        Current = Next;
        Next = PickColor();
        Shots++;
        return trace;
    }

    /// <summary>The bubble in the air reached its cell: runs the rules and fixes the queue's colors.</summary>
    public BubbleResult Land(BubbleTrace trace)
    {
        var result = Board.Place(trace.Cell, InFlight ?? Current);
        InFlight = null;
        RefreshQueue();
        return result;
    }

    /// <summary>Recolors queued bubbles whose color is no longer on the board. True if one changed.</summary>
    public bool RefreshQueue()
    {
        if (Board.IsEmpty)
            return false;
        var changed = false;
        if (!Board.HasColor(Current))
        {
            Current = PickColor();
            changed = true;
        }
        if (!Board.HasColor(Next))
        {
            Next = PickColor();
            changed = true;
        }
        return changed;
    }

    private int PickColor()
    {
        var colors = Board.ColorsOnBoard();
        return colors.Count == 0 ? 0 : colors[_rng.Next(colors.Count)];
    }

    public ScoreEntry Entry(string name, long unixTime) => new(name, Shots, TimeMs, unixTime);
}
