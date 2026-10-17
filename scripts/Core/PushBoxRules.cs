using System;
using System.Collections.Generic;
using System.Linq;

namespace ToyBox.Core;

/// <summary>상자 밀기 difficulty. Each has its own leaderboard (see <see cref="PushBoxRules.Variants"/>).</summary>
public enum PushBoxLevel { Easy, Normal, Hard }

/// <summary>Push-box (Sokoban) rules that do not need the engine (docs/games/17-push-box.md).</summary>
public static class PushBoxRules
{
    /// <summary>Leaderboard variants, easiest first: a one-box 6 x 6 board and a four-box 9 x 8 one are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(PushBoxLevel level) => Variants[(int)level].Id;

    /// <summary>GDD table: boxes per board.</summary>
    public static (int Min, int Max) BoxRange(PushBoxLevel level) => level switch
    {
        PushBoxLevel.Easy => (1, 2),
        PushBoxLevel.Normal => (2, 3),
        _ => (3, 4),
    };

    /// <summary>GDD table: the board size limit, counting the fence around it (width x height).</summary>
    public static (int Width, int Height) MaxSize(PushBoxLevel level) => level switch
    {
        PushBoxLevel.Easy => (6, 6),
        PushBoxLevel.Normal => (8, 7),
        _ => (9, 8),
    };

    /// <summary>GDD table: the fewest pushes that solve a board.</summary>
    public static (int Min, int Max) PushRange(PushBoxLevel level) => level switch
    {
        PushBoxLevel.Easy => (3, 8),
        PushBoxLevel.Normal => (8, 20),
        _ => (15, 35),
    };

    /// <summary>
    /// The boards, in play order ("다음 판" walks the list and wraps around). <c>#</c> wall (the outer ring is the fence,
    /// inner ones are bushes), <c>.</c> star spot, <c>$</c> box, <c>*</c> box on a star spot, <c>@</c> rabbit, <c>+</c> rabbit on a
    /// star spot, <c>-</c> or space floor. Every board is checked by the tests: solvable, the GDD ranges, no box stuck at the
    /// start. Picked from a generator run (each solved by breadth-first search), then laid out by hand in rising difficulty.
    /// </summary>
    private static readonly string[][][] Boards =
    {
        // Easy: 1~2 boxes, 6 x 6 at most, 3~8 pushes
        new[]
        {
            new[] { "######", "#@---#", "#-$--#", "#----#", "#--.-#", "######" },
            new[] { "######", "#---@#", "#----#", "##$--#", "#---.#", "######" },
            new[] { "######", "#.--##", "#-$$-#", "#-#--#", "#.--@#", "######" },
            new[] { "######", "#---##", "#-#$-#", "#--$@#", "#-.-.#", "######" },
            new[] { "######", "#----#", "#-$-@#", "#-$#-#", "#.-#.#", "######" },
        },
        // Normal: 2~3 boxes, 8 x 7 at most, 8~20 pushes
        new[]
        {
            new[] { "########", "#--#.--#", "#------#", "#-$$#.-#", "#----#@#", "#--#---#", "########" },
            new[] { "########", "#-#----#", "#---$$-#", "#---#--#", "#----@-#", "#..#---#", "########" },
            new[] { "########", "#-#----#", "#-$--$-#", "#.--####", "##----@#", "#--.---#", "########" },
            new[] { "########", "#----#-#", "#-$-$$-#", "#-@----#", "#---##.#", "#--.--.#", "########" },
            new[] { "########", "#-@--.-#", "#----.-#", "#-$-#--#", "#-$$#.##", "##-----#", "########" },
        },
        // Hard: 3~4 boxes, 9 x 8 at most, 15~35 pushes
        new[]
        {
            new[] { "#########", "#----#--#", "#---#@.##", "#-$$----#", "#--#----#", "#-$-----#", "#---#.-.#", "#########" },
            new[] { "#########", "##----.-#", "#------.#", "##--$---#", "##$---.-#", "#@$-##--#", "#-------#", "#########" },
            new[] { "#########", "#.$.$---#", "#-#---$-#", "#-------#", "#-##----#", "#-##-#--#", "#.--@##-#", "#########" },
            new[] { "#########", "#-.---#-#", "#-------#", "#.-#----#", "##-#@-$-#", "##-$$-$##", "#.-.----#", "#########" },
            new[] { "#########", "#------##", "##---.$##", "#@-#----#", "#-.-$#--#", "#--#--$-#", "#.---#--#", "#########" },
        },
    };

    private static readonly Dictionary<(PushBoxLevel, int), PushBoxPuzzle> Cache = new();

    public static int BoardCount(PushBoxLevel level) => Boards[(int)level].Length;

    /// <summary>Index into the level's list, wrapped (so a stale saved index still works).</summary>
    public static int Wrap(PushBoxLevel level, int index)
    {
        var n = BoardCount(level);
        return ((index % n) + n) % n;
    }

    public static PushBoxPuzzle Get(PushBoxLevel level, int index)
    {
        var key = (level, Wrap(level, index));
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var puzzle))
                Cache[key] = puzzle = PushBoxPuzzle.Parse(Boards[(int)level][key.Item2]);
            return puzzle;
        }
    }

    /// <summary>Ranking text: "38.2초 · 54걸음".</summary>
    public static string FormatRecord(long timeMs, int steps) => $"{timeMs / 1000f:0.0}초 · {steps}걸음";
}

/// <summary>
/// One board as laid out: walls, star spots (goals), where the boxes and the rabbit start. Also knows its dead cells:
/// floor cells from which a box (alone on the board) can never be pushed onto any star spot, such as a corner.
/// </summary>
public sealed class PushBoxPuzzle
{
    private readonly bool[] _wall;
    private readonly bool[] _goal;
    private readonly bool[] _dead;

    private PushBoxPuzzle(int width, int height, bool[] wall, bool[] goal, List<MazeCell> boxes, MazeCell start)
    {
        Width = width;
        Height = height;
        _wall = wall;
        _goal = goal;
        Boxes = boxes;
        Start = start;
        var goals = new List<MazeCell>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (goal[y * width + x])
                    goals.Add(new MazeCell(x, y));
        Goals = goals;
        _dead = FindDeadCells();
    }

    public int Width { get; }
    public int Height { get; }
    public MazeCell Start { get; }
    public IReadOnlyList<MazeCell> Boxes { get; }
    public IReadOnlyList<MazeCell> Goals { get; }
    public int CellCount => Width * Height;

    public bool InBounds(MazeCell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;
    public int Index(MazeCell c) => c.Y * Width + c.X;
    public MazeCell CellAt(int index) => new(index % Width, index / Width);

    /// <summary>Walls, and everything outside the board.</summary>
    public bool IsWall(MazeCell c) => !InBounds(c) || _wall[Index(c)];
    public bool IsGoal(MazeCell c) => InBounds(c) && _goal[Index(c)];
    public bool IsFloor(MazeCell c) => !IsWall(c);

    /// <summary>A box here (not on a star spot) can never reach a star spot, whatever the other boxes do.</summary>
    public bool IsDeadCell(MazeCell c) => InBounds(c) && _dead[Index(c)];

    /// <summary>On the outer ring of the board (drawn as the fence).</summary>
    public bool IsEdge(MazeCell c) => c.X == 0 || c.Y == 0 || c.X == Width - 1 || c.Y == Height - 1;

    public static PushBoxPuzzle Parse(IReadOnlyList<string> rows)
    {
        var height = rows.Count;
        var width = rows.Max(r => r.Length);
        var wall = new bool[width * height];
        var goal = new bool[width * height];
        var boxes = new List<MazeCell>();
        MazeCell? start = null;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var ch = x < rows[y].Length ? rows[y][x] : '#';
                var i = y * width + x;
                switch (ch)
                {
                    case '#': wall[i] = true; break;
                    case '.': goal[i] = true; break;
                    case '$': boxes.Add(new MazeCell(x, y)); break;
                    case '*': goal[i] = true; boxes.Add(new MazeCell(x, y)); break;
                    case '@': start = new MazeCell(x, y); break;
                    case '+': goal[i] = true; start = new MazeCell(x, y); break;
                    case '-' or ' ': break;
                    default: throw new FormatException($"Unknown board character '{ch}'");
                }
            }
        if (start is null)
            throw new FormatException("The board has no rabbit (@)");
        return new PushBoxPuzzle(width, height, wall, goal, boxes, start.Value);
    }

    /// <summary>
    /// Pulls a box backward from every star spot (the rabbit needs a free cell behind it): every cell reached is alive,
    /// every other floor cell is dead. This catches corners and wall lines without a star spot.
    /// </summary>
    private bool[] FindDeadCells()
    {
        var alive = new bool[CellCount];
        var queue = new Queue<MazeCell>();
        foreach (var g in Goals)
        {
            alive[Index(g)] = true;
            queue.Enqueue(g);
        }
        while (queue.Count > 0)
        {
            var b = queue.Dequeue();
            for (var d = 0; d < 4; d++)
            {
                var dir = (MazeDir)d;
                var from = b.Step(Opposite(dir));     // The box was here and got pushed toward dir...
                var behind = from.Step(Opposite(dir)); // ...by the rabbit standing here
                if (IsWall(from) || IsWall(behind) || alive[Index(from)])
                    continue;
                alive[Index(from)] = true;
                queue.Enqueue(from);
            }
        }
        var dead = new bool[CellCount];
        for (var i = 0; i < CellCount; i++)
            dead[i] = !_wall[i] && !alive[i];
        return dead;
    }

    public static MazeDir Opposite(MazeDir dir) => (MazeDir)(((int)dir + 2) % 4);
}

public enum PushBoxMoveKind { Ignored, Bumped, Moved, Pushed }

/// <summary>The result of <see cref="PushBoxRun.Move"/>.</summary>
/// <param name="Box">Index into <see cref="PushBoxRun.Boxes"/> of the box pushed, or -1.</param>
public readonly record struct PushBoxStep(PushBoxMoveKind Kind, MazeDir Dir, MazeCell From, MazeCell To, int Box, MazeCell BoxTo)
{
    public bool Changed => Kind is PushBoxMoveKind.Moved or PushBoxMoveKind.Pushed;
}

/// <summary>Where the rabbit and the boxes are. Box order stays the same for a whole play (a box keeps its index).</summary>
public sealed record PushBoxSnapshot(MazeCell Player, MazeCell[] Boxes);

/// <summary>A box that cannot reach a star spot any more. <see cref="Frozen"/>: it cannot even move (a corner, a 2 x 2 jam).</summary>
public readonly record struct PushBoxStuck(int Box, bool Frozen);

/// <summary>
/// One play of a board: moving and pushing, unlimited undo (start over is undoable too), the step count and the clock.
/// The clock starts with the first move input and stops when every box is on a star spot. Undone steps still count in
/// <see cref="Steps"/> (the record stays honest; undo itself is free).
/// </summary>
public sealed class PushBoxRun
{
    private readonly MazeCell[] _boxes;
    private readonly Stack<PushBoxSnapshot> _history = new();
    private double _seconds;

    public PushBoxRun(PushBoxPuzzle puzzle)
    {
        Puzzle = puzzle;
        Player = puzzle.Start;
        _boxes = puzzle.Boxes.ToArray();
    }

    public PushBoxPuzzle Puzzle { get; }
    public MazeCell Player { get; private set; }
    public IReadOnlyList<MazeCell> Boxes => _boxes;
    public int Steps { get; private set; }
    public int Pushes { get; private set; }
    public int Bumps { get; private set; }
    public int Undos { get; private set; }
    public bool Started { get; private set; }
    public bool Solved { get; private set; }
    public long TimeMs => (long)Math.Round(_seconds * 1000);
    public bool CanUndo => _history.Count > 0 && !Solved;
    /// <summary>Anything to start over from (the board differs from its starting layout).</summary>
    public bool CanReset => !Solved && !Snapshot().SameAs(Puzzle.Start, Puzzle.Boxes);

    public bool IsOnGoal(int box) => Puzzle.IsGoal(_boxes[box]);
    public int BoxesOnGoals => _boxes.Count(Puzzle.IsGoal);

    public int BoxAt(MazeCell c) => Array.IndexOf(_boxes, c);

    public PushBoxSnapshot Snapshot() => new(Player, (MazeCell[])_boxes.Clone());

    public PushBoxStep Move(MazeDir dir)
    {
        var from = Player;
        if (Solved)
            return new PushBoxStep(PushBoxMoveKind.Ignored, dir, from, from, -1, default);
        Started = true;
        var to = from.Step(dir);
        if (Puzzle.IsWall(to))
            return Bump(dir, from);
        var box = BoxAt(to);
        if (box < 0)
        {
            _history.Push(Snapshot());
            Player = to;
            Steps++;
            return new PushBoxStep(PushBoxMoveKind.Moved, dir, from, to, -1, default);
        }
        var boxTo = to.Step(dir);
        if (Puzzle.IsWall(boxTo) || BoxAt(boxTo) >= 0)
            return Bump(dir, from);
        _history.Push(Snapshot());
        _boxes[box] = boxTo;
        Player = to;
        Steps++;
        Pushes++;
        Solved = _boxes.All(Puzzle.IsGoal);
        return new PushBoxStep(PushBoxMoveKind.Pushed, dir, from, to, box, boxTo);
    }

    private PushBoxStep Bump(MazeDir dir, MazeCell from)
    {
        Bumps++;
        return new PushBoxStep(PushBoxMoveKind.Bumped, dir, from, from, -1, default);
    }

    /// <summary>Takes back the last step (or the last start over). Returns the layout before the undo, or null if none.</summary>
    public PushBoxSnapshot? Undo()
    {
        if (!CanUndo)
            return null;
        var before = Snapshot();
        Restore(_history.Pop());
        Undos++;
        return before;
    }

    /// <summary>Puts everything back where it started. One undo brings the board back. Returns the layout before.</summary>
    public PushBoxSnapshot? Reset()
    {
        if (!CanReset)
            return null;
        var before = Snapshot();
        _history.Push(before);
        Restore(new PushBoxSnapshot(Puzzle.Start, Puzzle.Boxes.ToArray()));
        return before;
    }

    private void Restore(PushBoxSnapshot s)
    {
        Player = s.Player;
        Array.Copy(s.Boxes, _boxes, _boxes.Length);
    }

    /// <summary>Adds play time (the game calls this every frame). Counts only between the first move and the finish.</summary>
    public void AddTime(double seconds)
    {
        if (Started && !Solved && seconds > 0)
            _seconds += seconds;
    }

    /// <summary>
    /// Boxes that can no longer reach a star spot, in the simple cases a child can see: a box off the star spots on a
    /// dead cell (a corner, along a wall with no star spot), or in a 2 x 2 block of boxes and walls with a box off the
    /// star spots. Boxes on star spots are never listed.
    /// </summary>
    public List<PushBoxStuck> StuckBoxes()
    {
        var stuck = new List<PushBoxStuck>();
        for (var i = 0; i < _boxes.Length; i++)
        {
            var b = _boxes[i];
            if (Puzzle.IsGoal(b))
                continue;
            var frozen = InCorner(b) || InJam(b);
            if (frozen || Puzzle.IsDeadCell(b))
                stuck.Add(new PushBoxStuck(i, frozen));
        }
        return stuck;
    }

    /// <summary>Would this move push a box somewhere it gets stuck (see <see cref="StuckBoxes"/>)? Nothing changes.</summary>
    public bool PushWouldStick(MazeDir dir)
    {
        if (Solved)
            return false;
        var to = Player.Step(dir);
        var box = BoxAt(to);
        var boxTo = to.Step(dir);
        if (box < 0 || Puzzle.IsWall(boxTo) || BoxAt(boxTo) >= 0)
            return false;
        _boxes[box] = boxTo;
        var stuck = StuckBoxes().Exists(s => s.Box == box);
        _boxes[box] = to;
        return stuck;
    }

    private bool InCorner(MazeCell b)
    {
        var vertical = Puzzle.IsWall(b.Step(MazeDir.Up)) || Puzzle.IsWall(b.Step(MazeDir.Down));
        var horizontal = Puzzle.IsWall(b.Step(MazeDir.Left)) || Puzzle.IsWall(b.Step(MazeDir.Right));
        return vertical && horizontal;
    }

    /// <summary>A 2 x 2 square around the box made only of walls and boxes (none of them can ever move again).</summary>
    private bool InJam(MazeCell b)
    {
        for (var dy = -1; dy <= 0; dy++)
            for (var dx = -1; dx <= 0; dx++)
            {
                var full = true;
                for (var y = 0; y < 2 && full; y++)
                    for (var x = 0; x < 2 && full; x++)
                    {
                        var c = new MazeCell(b.X + dx + x, b.Y + dy + y);
                        full = Puzzle.IsWall(c) || BoxAt(c) >= 0;
                    }
                if (full)
                    return true;
            }
        return false;
    }

    /// <summary>The leaderboard row: <see cref="ScoreEntry.Score"/> holds the steps (less time first, then fewer steps).</summary>
    public ScoreEntry Entry(string name, long unixTime) => new(name, Steps, TimeMs, unixTime);
}

internal static class PushBoxSnapshotExtensions
{
    public static bool SameAs(this PushBoxSnapshot s, MazeCell player, IReadOnlyList<MazeCell> boxes)
    {
        if (s.Player != player)
            return false;
        for (var i = 0; i < boxes.Count; i++)
            if (s.Boxes[i] != boxes[i])
                return false;
        return true;
    }
}

/// <summary>
/// Breadth-first solvers (tests and autoplay). States are the rabbit's cell plus the sorted box cells packed in a long
/// (7 bits a cell, so boards up to 128 cells and 8 boxes). Pushes onto dead cells and into 2 x 2 jams are skipped: such
/// states can never be solved, so the answers stay the shortest.
/// </summary>
public static class PushBoxSolver
{
    private const int Bits = 7;
    private const long Mask = (1 << Bits) - 1;

    /// <summary>The fewest steps (walking and pushing) from this layout to the finish, as directions. Null if none.</summary>
    public static List<MazeDir>? ShortestMoves(PushBoxPuzzle p, MazeCell player, IReadOnlyList<MazeCell> boxes, int maxStates = 20_000_000)
    {
        var ctx = new Context(p);
        var startBoxes = boxes.Select(p.Index).ToArray();
        Array.Sort(startBoxes);
        var start = Pack(p.Index(player), startBoxes);
        if (ctx.AllOnGoals(startBoxes))
            return new List<MazeDir>();
        var parent = new Dictionary<long, (long From, byte Dir)> { [start] = (-1, 0) };
        var queue = new Queue<long>();
        queue.Enqueue(start);
        var n = startBoxes.Length;
        var bx = new int[n];
        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            var at = Unpack(key, bx);
            for (var d = 0; d < 4; d++)
            {
                var next = ctx.Next[at * 4 + d];
                if (next < 0)
                    continue;
                long child;
                var bi = Array.IndexOf(bx, next);
                if (bi < 0)
                    child = Pack(next, bx);
                else
                {
                    var beyond = ctx.Next[next * 4 + d];
                    if (beyond < 0 || Array.IndexOf(bx, beyond) >= 0 || ctx.Dead[beyond])
                        continue;
                    var moved = (int[])bx.Clone();
                    moved[bi] = beyond;
                    if (!ctx.Goal[beyond] && ctx.Jammed(beyond, moved))
                        continue;
                    Array.Sort(moved);
                    child = Pack(next, moved);
                    if (ctx.AllOnGoals(moved))
                    {
                        parent[child] = (key, (byte)d);
                        return Path(parent, child);
                    }
                }
                if (parent.ContainsKey(child))
                    continue;
                parent[child] = (key, (byte)d);
                if (parent.Count > maxStates)
                    return null;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    /// <summary>The fewest pushes that solve the board from its start (walking is free). -1 if it cannot be solved.</summary>
    public static int MinPushes(PushBoxPuzzle p)
    {
        var ctx = new Context(p);
        var boxes = p.Boxes.Select(p.Index).ToArray();
        Array.Sort(boxes);
        if (ctx.AllOnGoals(boxes))
            return 0;
        var seen = new HashSet<long>();
        var layer = new List<long> { Pack(ctx.Normalize(p.Index(p.Start), boxes), boxes) };
        seen.Add(layer[0]);
        var bx = new int[boxes.Length];
        for (var pushes = 1; layer.Count > 0; pushes++)
        {
            var nextLayer = new List<long>();
            foreach (var key in layer)
            {
                var at = Unpack(key, bx);
                var reach = ctx.Reach(at, bx);
                for (var i = 0; i < bx.Length; i++)
                    for (var d = 0; d < 4; d++)
                    {
                        var stand = ctx.Next[bx[i] * 4 + (d + 2) % 4];
                        var beyond = ctx.Next[bx[i] * 4 + d];
                        if (stand < 0 || !reach[stand] || beyond < 0 || ctx.Dead[beyond] || Array.IndexOf(bx, beyond) >= 0)
                            continue;
                        var moved = (int[])bx.Clone();
                        var from = moved[i];
                        moved[i] = beyond;
                        if (!ctx.Goal[beyond] && ctx.Jammed(beyond, moved))
                            continue;
                        if (ctx.AllOnGoals(moved))
                            return pushes;
                        Array.Sort(moved);
                        var child = Pack(ctx.Normalize(from, moved), moved);
                        if (seen.Add(child))
                            nextLayer.Add(child);
                    }
            }
            layer = nextLayer;
        }
        return -1;
    }

    private static long Pack(int player, int[] sortedBoxes)
    {
        long key = player;
        for (var i = 0; i < sortedBoxes.Length; i++)
            key |= (long)sortedBoxes[i] << (Bits * (i + 1));
        return key;
    }

    private static int Unpack(long key, int[] boxes)
    {
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = (int)((key >> (Bits * (i + 1))) & Mask);
        return (int)(key & Mask);
    }

    private static List<MazeDir> Path(Dictionary<long, (long From, byte Dir)> parent, long end)
    {
        var dirs = new List<MazeDir>();
        for (var key = end; parent[key].From >= 0; key = parent[key].From)
            dirs.Add((MazeDir)parent[key].Dir);
        dirs.Reverse();
        return dirs;
    }

    private sealed class Context
    {
        public readonly int[] Next;   // Neighbour cell index per (cell, direction), -1 for a wall
        public readonly bool[] Dead;
        public readonly bool[] Goal;
        private readonly PushBoxPuzzle _p;

        public Context(PushBoxPuzzle p)
        {
            _p = p;
            if (p.CellCount > 1 << Bits)
                throw new ArgumentException("Board too big for the solver");
            Next = new int[p.CellCount * 4];
            Dead = new bool[p.CellCount];
            Goal = new bool[p.CellCount];
            for (var i = 0; i < p.CellCount; i++)
            {
                var c = p.CellAt(i);
                Dead[i] = p.IsDeadCell(c);
                Goal[i] = p.IsGoal(c);
                for (var d = 0; d < 4; d++)
                {
                    var n = c.Step((MazeDir)d);
                    Next[i * 4 + d] = p.IsWall(c) || p.IsWall(n) ? -1 : p.Index(n);
                }
            }
        }

        public bool AllOnGoals(int[] boxes) => boxes.All(b => Goal[b]);

        /// <summary>Cells the rabbit can walk to without pushing.</summary>
        public bool[] Reach(int from, int[] boxes)
        {
            var seen = new bool[_p.CellCount];
            var stack = new Stack<int>();
            seen[from] = true;
            stack.Push(from);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                for (var d = 0; d < 4; d++)
                {
                    var n = Next[c * 4 + d];
                    if (n < 0 || seen[n] || Array.IndexOf(boxes, n) >= 0)
                        continue;
                    seen[n] = true;
                    stack.Push(n);
                }
            }
            return seen;
        }

        /// <summary>The smallest reachable cell index stands for every rabbit position in the same open area.</summary>
        public int Normalize(int from, int[] boxes)
        {
            var reach = Reach(from, boxes);
            return Array.IndexOf(reach, true);
        }

        /// <summary>A 2 x 2 square around the cell made only of walls and boxes, with a box off the star spots.</summary>
        public bool Jammed(int cell, int[] boxes)
        {
            var c = _p.CellAt(cell);
            for (var dy = -1; dy <= 0; dy++)
                for (var dx = -1; dx <= 0; dx++)
                {
                    var full = true;
                    var offGoal = false;
                    for (var y = 0; y < 2 && full; y++)
                        for (var x = 0; x < 2 && full; x++)
                        {
                            var q = new MazeCell(c.X + dx + x, c.Y + dy + y);
                            if (_p.IsWall(q))
                                continue;
                            var qi = _p.Index(q);
                            if (Array.IndexOf(boxes, qi) < 0)
                                full = false;
                            else if (!Goal[qi])
                                offGoal = true;
                        }
                    if (full && offGoal)
                        return true;
                }
            return false;
        }
    }
}
