using System;
using System.Collections.Generic;
using System.Text;

namespace ToyBox.Core;

/// <summary>Difficulty: maze size and how many dead ends. Each level has its own leaderboard (see <see cref="MazeRules.Variants"/>).</summary>
public enum MazeLevel { Easy, Normal, Hard }

/// <summary>A move direction, in the order the D-pad lists them.</summary>
public enum MazeDir { Up, Right, Down, Left }

/// <summary>A cell of the maze grid: x to the right, y downward, (0, 0) is the top-left cell.</summary>
public readonly record struct MazeCell(int X, int Y)
{
    public MazeCell Step(MazeDir dir) => dir switch
    {
        MazeDir.Up => new MazeCell(X, Y - 1),
        MazeDir.Right => new MazeCell(X + 1, Y),
        MazeDir.Down => new MazeCell(X, Y + 1),
        _ => new MazeCell(X - 1, Y),
    };

    public int ManhattanTo(MazeCell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

/// <summary>Maze escape rules that do not need the engine (docs/games/04-maze-escape.md).</summary>
public static class MazeRules
{
    /// <summary>Stars hidden in every maze ("별 3/3").</summary>
    public const int StarCount = 3;

    /// <summary>Leaderboard variants, easiest first: a 5 x 5 and a 10 x 10 maze are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(MazeLevel level) => Variants[(int)level].Id;

    /// <summary>GDD: 쉬움 5 x 5 / 보통 7 x 7 / 어려움 10 x 10.</summary>
    public static int Size(MazeLevel level) => level switch
    {
        MazeLevel.Easy => 5,
        MazeLevel.Normal => 7,
        _ => 10,
    };

    /// <summary>
    /// The curated mazes: one generator seed per maze, in play order ("다음 미로" walks this list and wraps around).
    /// Every seed was checked against the level's rules (see MazeRulesTests); change a seed only together with them.
    /// </summary>
    private static readonly int[][] Seeds =
    {
        new[] { 103, 107, 105, 104, 115 },
        new[] { 211, 218, 201, 221, 210 },
        new[] { 305, 304, 302, 303, 307 },
    };

    public static int MazeCount(MazeLevel level) => Seeds[(int)level].Length;

    public static int Seed(MazeLevel level, int index) => Seeds[(int)level][Wrap(level, index)];

    /// <summary>Index into the level's list, wrapped (so a stale saved index still works).</summary>
    public static int Wrap(MazeLevel level, int index)
    {
        var n = MazeCount(level);
        return ((index % n) + n) % n;
    }

    /// <summary>The <paramref name="index"/>-th curated maze of a level (always the same maze for the same index).</summary>
    public static Maze Get(MazeLevel level, int index) => MazeGenerator.Generate(level, Seed(level, index));

    /// <summary>How the generator shapes each level (see <see cref="MazeGenerator"/>).</summary>
    public static MazeStyle Style(MazeLevel level) => level switch
    {
        // Long winding corridors, then most dead ends are opened into loops: hard to get stuck
        MazeLevel.Easy => new MazeStyle(Branchiness: 0f, BraidChance: 0.7f, MaxStarDetour: 2),
        // Some side branches; about half of the dead ends stay
        MazeLevel.Normal => new MazeStyle(Branchiness: 0.25f, BraidChance: 0.45f, MaxStarDetour: 4),
        // More side branches, and every dead end stays
        _ => new MazeStyle(Branchiness: 0.3f, BraidChance: 0f, MaxStarDetour: 99),
    };

    /// <summary>Dead ends allowed per level (start and exit cells do not count).</summary>
    public static (int Min, int Max) DeadEndRange(MazeLevel level) => level switch
    {
        MazeLevel.Easy => (0, 2),
        MazeLevel.Normal => (3, 8),
        _ => (10, 30),
    };

    /// <summary>The shortest way out must be at least this many steps, so a maze is never a straight run.</summary>
    public static int MinPathSteps(MazeLevel level) => level switch
    {
        MazeLevel.Easy => 10,
        MazeLevel.Normal => 16,
        _ => 26,
    };

    /// <summary>Records list "별 3 · 25.3초" (stars, then time).</summary>
    public static string FormatRecord(int stars, long timeMs) => $"별 {stars} · {timeMs / 1000f:0.0}초";
}

/// <summary>Generator knobs for one level.</summary>
/// <param name="Branchiness">0 = pure depth-first carving (long corridors, few branches); higher picks random open
/// cells more often (more branches and dead ends).</param>
/// <param name="BraidChance">Chance that each dead end is opened into a neighbor, making a loop instead.</param>
/// <param name="MaxStarDetour">How many steps off the shortest way out a star may be.</param>
public readonly record struct MazeStyle(float Branchiness, float BraidChance, int MaxStarDetour);

/// <summary>
/// A rectangular maze: which walls are open between neighboring cells, the start (top-left), the exit
/// (bottom-right) and the star cells.
/// </summary>
public sealed class Maze
{
    private readonly bool[,] _openRight;  // Between (x, y) and (x + 1, y)
    private readonly bool[,] _openDown;   // Between (x, y) and (x, y + 1)
    private readonly List<MazeCell> _stars = new();

    public Maze(int width, int height)
    {
        Width = width;
        Height = height;
        _openRight = new bool[width, height];
        _openDown = new bool[width, height];
        Start = new MazeCell(0, 0);
        Exit = new MazeCell(width - 1, height - 1);
    }

    public int Width { get; }
    public int Height { get; }
    public MazeCell Start { get; }
    public MazeCell Exit { get; }
    public IReadOnlyList<MazeCell> Stars => _stars;

    public bool Contains(MazeCell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    /// <summary>True if there is no wall between <paramref name="cell"/> and its neighbor in <paramref name="dir"/>.</summary>
    public bool CanMove(MazeCell cell, MazeDir dir)
    {
        var to = cell.Step(dir);
        if (!Contains(cell) || !Contains(to))
            return false;
        return dir switch
        {
            MazeDir.Right => _openRight[cell.X, cell.Y],
            MazeDir.Left => _openRight[to.X, to.Y],
            MazeDir.Down => _openDown[cell.X, cell.Y],
            _ => _openDown[to.X, to.Y],
        };
    }

    /// <summary>Removes the wall between a cell and its neighbor (both must be inside).</summary>
    public void Open(MazeCell cell, MazeDir dir)
    {
        var to = cell.Step(dir);
        if (!Contains(cell) || !Contains(to))
            throw new ArgumentOutOfRangeException(nameof(dir), "Cannot open the outer wall");
        switch (dir)
        {
            case MazeDir.Right: _openRight[cell.X, cell.Y] = true; break;
            case MazeDir.Left: _openRight[to.X, to.Y] = true; break;
            case MazeDir.Down: _openDown[cell.X, cell.Y] = true; break;
            default: _openDown[to.X, to.Y] = true; break;
        }
    }

    public int Openings(MazeCell cell)
    {
        var n = 0;
        for (var d = 0; d < 4; d++)
            if (CanMove(cell, (MazeDir)d))
                n++;
        return n;
    }

    /// <summary>Cells with a single way in (excluding the start and the exit, which are meant to be at an end).</summary>
    public int DeadEnds()
    {
        var n = 0;
        foreach (var c in Cells())
            if (c != Start && c != Exit && Openings(c) == 1)
                n++;
        return n;
    }

    public IEnumerable<MazeCell> Cells()
    {
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            yield return new MazeCell(x, y);
    }

    public void SetStars(IEnumerable<MazeCell> stars)
    {
        _stars.Clear();
        _stars.AddRange(stars);
    }

    /// <summary>Steps from <paramref name="from"/> to every cell (-1 where it cannot be reached). <paramref name="avoid"/> is a cell the walk may not pass (e.g. the exit, which ends the game).</summary>
    public int[,] Distances(MazeCell from, MazeCell? avoid = null)
    {
        var dist = new int[Width, Height];
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            dist[x, y] = -1;
        var queue = new Queue<MazeCell>();
        dist[from.X, from.Y] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            for (var d = 0; d < 4; d++)
            {
                if (!CanMove(c, (MazeDir)d))
                    continue;
                var n = c.Step((MazeDir)d);
                if (dist[n.X, n.Y] >= 0 || n == avoid)
                    continue;
                dist[n.X, n.Y] = dist[c.X, c.Y] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    /// <summary>The shortest walk from <paramref name="from"/> to <paramref name="to"/>, both ends included (empty if unreachable).</summary>
    public List<MazeCell> ShortestPath(MazeCell from, MazeCell to, MazeCell? avoid = null)
    {
        var dist = Distances(to, avoid);
        var path = new List<MazeCell>();
        if (dist[from.X, from.Y] < 0)
            return path;
        var c = from;
        path.Add(c);
        while (c != to)
        {
            for (var d = 0; d < 4; d++)
            {
                if (!CanMove(c, (MazeDir)d))
                    continue;
                var n = c.Step((MazeDir)d);
                if (dist[n.X, n.Y] == dist[c.X, c.Y] - 1)
                {
                    c = n;
                    break;
                }
            }
            path.Add(c);
        }
        return path;
    }

    /// <summary>The directions of a path of neighboring cells (e.g. from <see cref="ShortestPath"/>).</summary>
    public static List<MazeDir> Directions(IReadOnlyList<MazeCell> path)
    {
        var dirs = new List<MazeDir>();
        for (var i = 1; i < path.Count; i++)
            for (var d = 0; d < 4; d++)
                if (path[i - 1].Step((MazeDir)d) == path[i])
                {
                    dirs.Add((MazeDir)d);
                    break;
                }
        return dirs;
    }

    /// <summary>Text picture like the GDD's: '#' walls, 'P' start, 'E' exit, '*' stars.</summary>
    public string ToAscii()
    {
        var sb = new StringBuilder();
        var w = Width * 2 + 1;
        for (var row = 0; row < Height * 2 + 1; row++)
        {
            for (var col = 0; col < w; col++)
                sb.Append(Glyph(col, row));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private char Glyph(int col, int row)
    {
        var x = col / 2;
        var y = row / 2;
        if (col % 2 == 1 && row % 2 == 1)
        {
            var c = new MazeCell(x, y);
            return c == Start ? 'P' : c == Exit ? 'E' : _stars.Contains(c) ? '*' : ' ';
        }
        if (col % 2 == 0 && row % 2 == 1) // Vertical wall left of cell (x, y)
            return x > 0 && x < Width && _openRight[x - 1, y] ? ' ' : '#';
        if (col % 2 == 1 && row % 2 == 0) // Horizontal wall above cell (x, y)
            return y > 0 && y < Height && _openDown[x, y - 1] ? ' ' : '#';
        return '#';
    }
}

/// <summary>
/// Deterministic maze generator: a "growing tree" carve (depth-first with some random branching) followed by
/// optional braiding (opening dead ends into loops), then star placement. Uses its own small random generator so a
/// seed gives the same maze on every platform and .NET version.
/// </summary>
public static class MazeGenerator
{
    public static Maze Generate(MazeLevel level, int seed) =>
        Generate(MazeRules.Size(level), MazeRules.Size(level), MazeRules.Style(level), seed);

    public static Maze Generate(int width, int height, MazeStyle style, int seed)
    {
        var rng = new SeededRandom(seed);
        var maze = new Maze(width, height);
        Carve(maze, style.Branchiness, rng);
        Braid(maze, style.BraidChance, rng);
        maze.SetStars(PlaceStars(maze, style.MaxStarDetour, rng));
        return maze;
    }

    /// <summary>Growing tree: usually continues from the newest cell (corridors), sometimes from a random one (branches).</summary>
    private static void Carve(Maze maze, float branchiness, SeededRandom rng)
    {
        var visited = new bool[maze.Width, maze.Height];
        var active = new List<MazeCell> { maze.Start };
        visited[0, 0] = true;
        var dirs = new List<MazeDir>(4);
        while (active.Count > 0)
        {
            var index = rng.NextFloat() < branchiness ? rng.Next(active.Count) : active.Count - 1;
            var cell = active[index];
            dirs.Clear();
            for (var d = 0; d < 4; d++)
            {
                var n = cell.Step((MazeDir)d);
                if (maze.Contains(n) && !visited[n.X, n.Y])
                    dirs.Add((MazeDir)d);
            }
            if (dirs.Count == 0)
            {
                active.RemoveAt(index);
                continue;
            }
            var dir = dirs[rng.Next(dirs.Count)];
            var next = cell.Step(dir);
            maze.Open(cell, dir);
            visited[next.X, next.Y] = true;
            active.Add(next);
        }
    }

    /// <summary>Opens each dead end (with the given chance) into a neighbor, preferring another dead end.</summary>
    private static void Braid(Maze maze, float chance, SeededRandom rng)
    {
        if (chance <= 0)
            return;
        foreach (var cell in maze.Cells())
        {
            if (cell == maze.Start || cell == maze.Exit || maze.Openings(cell) != 1 || rng.NextFloat() >= chance)
                continue;
            var closed = new List<MazeDir>();
            for (var d = 0; d < 4; d++)
            {
                var n = cell.Step((MazeDir)d);
                if (maze.Contains(n) && !maze.CanMove(cell, (MazeDir)d) && n != maze.Start && n != maze.Exit)
                    closed.Add((MazeDir)d);
            }
            if (closed.Count == 0)
                continue;
            var deadNeighbors = closed.FindAll(d => maze.Openings(cell.Step(d)) == 1);
            var pick = deadNeighbors.Count > 0 ? deadNeighbors : closed;
            maze.Open(cell, pick[rng.Next(pick.Count)]);
        }
    }

    /// <summary>
    /// Three stars off the shortest way out, one per third of that way (near the start, the middle, near the exit), so
    /// collecting them means small side trips. Dead ends and cells a little farther off the way are preferred.
    /// </summary>
    private static List<MazeCell> PlaceStars(Maze maze, int maxDetour, SeededRandom rng)
    {
        var path = maze.ShortestPath(maze.Start, maze.Exit);
        // Multi-source BFS from the path: how far off the way each cell is, and which path step it hangs from
        var detour = new int[maze.Width, maze.Height];
        var attach = new int[maze.Width, maze.Height];
        for (var x = 0; x < maze.Width; x++)
        for (var y = 0; y < maze.Height; y++)
            detour[x, y] = -1;
        var queue = new Queue<MazeCell>();
        for (var i = 0; i < path.Count; i++)
        {
            detour[path[i].X, path[i].Y] = 0;
            attach[path[i].X, path[i].Y] = i;
            queue.Enqueue(path[i]);
        }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            for (var d = 0; d < 4; d++)
            {
                if (!maze.CanMove(c, (MazeDir)d))
                    continue;
                var n = c.Step((MazeDir)d);
                if (detour[n.X, n.Y] >= 0)
                    continue;
                detour[n.X, n.Y] = detour[c.X, c.Y] + 1;
                attach[n.X, n.Y] = attach[c.X, c.Y];
                queue.Enqueue(n);
            }
        }

        // Not on any shortest way out either (a braided maze can have several)
        var fromStart = maze.Distances(maze.Start);
        var fromExit = maze.Distances(maze.Exit);
        var beforeExit = maze.Distances(maze.Start, maze.Exit); // Reachable without stepping on the exit
        var shortest = path.Count - 1;
        var candidates = new List<MazeCell>();
        foreach (var c in maze.Cells())
            if (detour[c.X, c.Y] >= 1 && detour[c.X, c.Y] <= maxDetour
                && fromStart[c.X, c.Y] + fromExit[c.X, c.Y] > shortest && beforeExit[c.X, c.Y] > 0)
                candidates.Add(c);

        double Score(MazeCell c)
        {
            var d = detour[c.X, c.Y];
            var deadEnd = maze.Openings(c) == 1 ? 2.0 : 0.0;
            return deadEnd + Math.Min(d, 4) * 0.5 + rng.NextFloat();
        }

        var stars = new List<MazeCell>();
        for (var part = 0; part < MazeRules.StarCount; part++)
        {
            var lo = path.Count * part / MazeRules.StarCount;
            var hi = path.Count * (part + 1) / MazeRules.StarCount;
            MazeCell? best = null;
            var bestScore = double.MinValue;
            foreach (var c in candidates)
            {
                var a = attach[c.X, c.Y];
                if (a < lo || a >= hi || stars.Contains(c) || stars.Exists(s => s.ManhattanTo(c) < 2))
                    continue;
                var s = Score(c);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = c;
                }
            }
            // A third with no side cell: take the best one anywhere
            if (best is null)
                foreach (var c in candidates)
                {
                    if (stars.Contains(c))
                        continue;
                    var s = Score(c);
                    if (s > bestScore)
                    {
                        bestScore = s;
                        best = c;
                    }
                }
            if (best is { } pick)
                stars.Add(pick);
        }
        return stars;
    }

    /// <summary>Mulberry32: tiny, fast and stable across runtimes.</summary>
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

/// <summary>What one move input did.</summary>
public enum MazeMoveKind
{
    /// <summary>Nothing (already out of the maze).</summary>
    Ignored,
    /// <summary>Walked one cell.</summary>
    Moved,
    /// <summary>A wall is in the way: no move, no penalty.</summary>
    Bumped,
}

/// <summary>The result of <see cref="MazeRun.Move"/>.</summary>
/// <param name="Star">Index into <see cref="Maze.Stars"/> of a star picked up on arrival, or -1.</param>
public readonly record struct MazeStep(MazeMoveKind Kind, MazeCell From, MazeCell To, int Star, bool Escaped);

/// <summary>
/// State of one play of a maze: where the player is, which stars are taken, and the clock. The clock starts with the
/// first move input (the child may look at the maze first) and stops at the exit; the game only adds time while the
/// tree runs (the quit question pauses it).
/// </summary>
public sealed class MazeRun
{
    private readonly bool[] _taken;
    private double _seconds;

    public MazeRun(Maze maze)
    {
        Maze = maze;
        Position = maze.Start;
        _taken = new bool[maze.Stars.Count];
    }

    public Maze Maze { get; }
    public MazeCell Position { get; private set; }
    public int Stars { get; private set; }
    public int Moves { get; private set; }
    public int Bumps { get; private set; }
    public bool Started { get; private set; }
    public bool Escaped { get; private set; }
    public long TimeMs => (long)Math.Round(_seconds * 1000);

    public bool IsTaken(int star) => _taken[star];

    public MazeStep Move(MazeDir dir)
    {
        var from = Position;
        if (Escaped)
            return new MazeStep(MazeMoveKind.Ignored, from, from, -1, true);
        Started = true;
        if (!Maze.CanMove(from, dir))
        {
            Bumps++;
            return new MazeStep(MazeMoveKind.Bumped, from, from, -1, false);
        }
        Position = from.Step(dir);
        Moves++;
        var star = -1;
        for (var i = 0; i < Maze.Stars.Count; i++)
            if (!_taken[i] && Maze.Stars[i] == Position)
            {
                _taken[i] = true;
                Stars++;
                star = i;
            }
        if (Position == Maze.Exit)
            Escaped = true;
        return new MazeStep(MazeMoveKind.Moved, from, Position, star, Escaped);
    }

    /// <summary>Adds play time (the game calls this every frame). Counts only between the first move and the exit.</summary>
    public void AddTime(double seconds)
    {
        if (Started && !Escaped && seconds > 0)
            _seconds += seconds;
    }

    /// <summary>The leaderboard row: <see cref="ScoreEntry.Score"/> holds the stars (more first, then less time).</summary>
    public ScoreEntry Entry(string name, long unixTime) => new(name, Stars, TimeMs, unixTime);
}
