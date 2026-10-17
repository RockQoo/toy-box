using System;
using System.Collections.Generic;
using System.Linq;

namespace ToyBox.Core;

/// <summary>Board size and fall speed. Each level has its own leaderboard (see <see cref="BlocksRules.Variants"/>).</summary>
public enum BlocksLevel { Easy, Normal }

/// <summary>The seven pieces, in the order of their pictures (cyan, yellow, purple, green, pink, blue, orange).</summary>
public enum PieceKind { I, O, T, S, Z, J, L }

/// <summary>A cell offset or a board cell (x right, y down; rows 0 and 1 are the hidden rows above the visible board).</summary>
public readonly record struct BlockCell(int X, int Y);

/// <summary>A block on the board.</summary>
public readonly record struct PlacedBlock(int X, int Y, PieceKind Kind);

/// <summary>블록 쌓기 rules that do not need the engine (docs/games/14-blocks.md).</summary>
public static class BlocksRules
{
    /// <summary>Leaderboard variants, easiest first: the small board fills lines faster, so counts are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
    };

    public static string VariantId(BlocksLevel level) => Variants[(int)level].Id;

    public const float PlaySeconds = 180f;
    public const int PieceKinds = 7;

    /// <summary>Rows above the visible board where pieces appear.</summary>
    public const int HiddenRows = 2;

    /// <summary>Time a piece can still move after it touches the floor or the stack (more than the original's 0.5 s).</summary>
    public const float LockDelay = 0.8f;

    /// <summary>Moves / turns on the floor that restart <see cref="LockDelay"/>, per piece (reset when it gets lower).</summary>
    public const int MaxLockResets = 15;

    /// <summary>Fall step while ↓ is held.</summary>
    public const float SoftDropInterval = 0.05f;

    /// <summary>Holding ← / → moves once, then again after this delay …</summary>
    public const float RepeatDelay = 0.18f;

    /// <summary>… and then every this many seconds.</summary>
    public const float RepeatInterval = 0.05f;

    /// <summary>Rows taken away from the bottom when the stack reaches the top (no points, the round goes on).</summary>
    public const int RescueRows = 6;

    public const int LinesPerStage = 10;

    public static int Width(BlocksLevel level) => level == BlocksLevel.Easy ? 8 : 10;
    public static int Height(BlocksLevel level) => level == BlocksLevel.Easy ? 16 : 20;

    // Seconds per row by stage (10 lines each); the last entry is the floor
    private static readonly float[] EasyIntervals = { 1.2f, 1.1f, 1.0f, 0.9f, 0.8f, 0.7f, 0.6f };
    private static readonly float[] NormalIntervals = { 1.0f, 0.85f, 0.72f, 0.6f, 0.5f, 0.42f, 0.35f };

    private static float[] Intervals(BlocksLevel level) => level == BlocksLevel.Easy ? EasyIntervals : NormalIntervals;

    /// <summary>0 at the start, +1 every <see cref="LinesPerStage"/> lines, up to the fastest step.</summary>
    public static int Stage(BlocksLevel level, int lines) =>
        Math.Min(Math.Max(0, lines) / LinesPerStage, Intervals(level).Length - 1);

    /// <summary>Seconds per row of falling.</summary>
    public static float FallInterval(BlocksLevel level, int lines) => Intervals(level)[Stage(level, lines)];

    /// <summary>The word for lines cleared at once.</summary>
    public static string ClearWord(int lines) => lines switch
    {
        <= 0 => "",
        1 => "한 줄!",
        2 => "두 줄!",
        3 => "세 줄!",
        _ => "네 줄! 최고!",
    };

    /// <summary>Picture name per piece (assets/sprites/blocks/&lt;name&gt;.png).</summary>
    public static readonly string[] PictureNames = { "cyan", "yellow", "purple", "green", "pink", "blue", "orange" };

    // Spawn shapes (rotation 0) in their box, as in the standard rotation system
    private static readonly BlockCell[][] SpawnShapes =
    {
        new BlockCell[] { new(0, 1), new(1, 1), new(2, 1), new(3, 1) },  // I
        new BlockCell[] { new(0, 0), new(1, 0), new(0, 1), new(1, 1) },  // O
        new BlockCell[] { new(1, 0), new(0, 1), new(1, 1), new(2, 1) },  // T
        new BlockCell[] { new(1, 0), new(2, 0), new(0, 1), new(1, 1) },  // S
        new BlockCell[] { new(0, 0), new(1, 0), new(1, 1), new(2, 1) },  // Z
        new BlockCell[] { new(0, 0), new(0, 1), new(1, 1), new(2, 1) },  // J
        new BlockCell[] { new(2, 0), new(0, 1), new(1, 1), new(2, 1) },  // L
    };

    private static readonly BlockCell[][][] Shapes = BuildShapes();

    private static BlockCell[][][] BuildShapes()
    {
        var all = new BlockCell[PieceKinds][][];
        for (var k = 0; k < PieceKinds; k++)
        {
            var n = BoxSize((PieceKind)k);
            all[k] = new BlockCell[4][];
            all[k][0] = SpawnShapes[k];
            // Clockwise turn inside the box: (x, y) → (n-1-y, x)
            for (var r = 1; r < 4; r++)
                all[k][r] = all[k][r - 1].Select(c => new BlockCell(n - 1 - c.Y, c.X)).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        }
        return all;
    }

    /// <summary>The square the piece turns in: 4 for I, 2 for O, 3 for the rest.</summary>
    public static int BoxSize(PieceKind kind) => kind switch
    {
        PieceKind.I => 4,
        PieceKind.O => 2,
        _ => 3,
    };

    /// <summary>The four cells of a piece in its box, for rotation 0–3 (clockwise).</summary>
    public static IReadOnlyList<BlockCell> Cells(PieceKind kind, int rotation) => Shapes[(int)kind][Mod4(rotation)];

    public static int Mod4(int rotation) => ((rotation % 4) + 4) % 4;

    /// <summary>
    /// Shifts tried in order when a turn is blocked (the simple wall kick): in place, one cell aside, two for the long I,
    /// then one up (so a piece lying on the stack can still turn).
    /// </summary>
    public static IReadOnlyList<BlockCell> Kicks(PieceKind kind) => kind == PieceKind.I ? LongKicks : ShortKicks;

    private static readonly BlockCell[] ShortKicks = { new(0, 0), new(-1, 0), new(1, 0), new(0, -1) };
    private static readonly BlockCell[] LongKicks = { new(0, 0), new(-1, 0), new(1, 0), new(-2, 0), new(2, 0), new(0, -1) };

    /// <summary>Box column of a new piece: centered (left of center when it cannot be exact).</summary>
    public static int SpawnX(PieceKind kind, int width) => (width - BoxSize(kind)) / 2;

    /// <summary>Box row of a new piece: its lowest cells sit on the first visible row, the rest peeks in from above.</summary>
    public static int SpawnY(PieceKind kind) => HiddenRows - Cells(kind, 0).Max(c => c.Y);
}

/// <summary>The "7개 묶음": all seven pieces in a shuffled order, then the next seven, so none is missing for long.</summary>
public sealed class PieceBag
{
    private readonly Random _rng;
    private readonly List<PieceKind> _queue = new();

    public PieceBag(Random rng) => _rng = rng;

    public PieceKind Next()
    {
        Fill(1);
        var kind = _queue[0];
        _queue.RemoveAt(0);
        return kind;
    }

    /// <summary>The next <paramref name="count"/> pieces without taking them.</summary>
    public IReadOnlyList<PieceKind> Peek(int count)
    {
        Fill(count);
        return _queue.Take(count).ToArray();
    }

    private void Fill(int count)
    {
        while (_queue.Count < count)
        {
            var bag = Enumerable.Range(0, BlocksRules.PieceKinds).Select(i => (PieceKind)i).ToArray();
            for (var i = bag.Length - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            _queue.AddRange(bag);
        }
    }
}

/// <summary>The falling piece: which one, how it is turned, and where its box is.</summary>
public readonly record struct ActivePiece(PieceKind Kind, int Rotation, int X, int Y)
{
    public IEnumerable<BlockCell> Cells
    {
        get
        {
            var (x, y) = (X, Y);
            return BlocksRules.Cells(Kind, Rotation).Select(c => new BlockCell(x + c.X, y + c.Y));
        }
    }
}

/// <summary>
/// The well: <see cref="Width"/> columns, <see cref="Height"/> rows of which the top <see cref="BlocksRules.HiddenRows"/>
/// are hidden. A cell holds 0 (empty) or the piece kind + 1.
/// </summary>
public sealed class BlocksBoard
{
    private readonly int[,] _cells;

    public BlocksBoard(int width, int visibleRows)
    {
        Width = width;
        VisibleRows = visibleRows;
        Height = visibleRows + BlocksRules.HiddenRows;
        _cells = new int[width, Height];
    }

    public int Width { get; }
    public int Height { get; }
    public int VisibleRows { get; }

    public int this[int x, int y]
    {
        get => _cells[x, y];
        set => _cells[x, y] = value;
    }

    public bool Inside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public bool IsFree(int x, int y) => Inside(x, y) && _cells[x, y] == 0;

    public bool Fits(PieceKind kind, int rotation, int x, int y)
    {
        var cells = BlocksRules.Cells(kind, rotation);
        for (var i = 0; i < cells.Count; i++)
            if (!IsFree(x + cells[i].X, y + cells[i].Y))
                return false;
        return true;
    }

    public bool Fits(ActivePiece piece) => Fits(piece.Kind, piece.Rotation, piece.X, piece.Y);

    /// <summary>Lowest box row the piece can drop to from where it is.</summary>
    public int DropY(ActivePiece piece)
    {
        var y = piece.Y;
        while (Fits(piece.Kind, piece.Rotation, piece.X, y + 1))
            y++;
        return y;
    }

    public void Place(ActivePiece piece)
    {
        foreach (var c in piece.Cells)
            _cells[c.X, c.Y] = (int)piece.Kind + 1;
    }

    public bool IsRowFull(int y)
    {
        for (var x = 0; x < Width; x++)
            if (_cells[x, y] == 0)
                return false;
        return true;
    }

    /// <summary>Takes out every full row (the rows above come down). Returns their indices, top to bottom.</summary>
    public List<int> ClearFullRows()
    {
        var cleared = new List<int>();
        for (var y = 0; y < Height; y++)
            if (IsRowFull(y))
                cleared.Add(y);
        if (cleared.Count > 0)
            Collapse(cleared);
        return cleared;
    }

    /// <summary>Takes out the bottom <paramref name="rows"/> rows and moves the stack down. Returns the blocks taken.</summary>
    public List<PlacedBlock> RemoveBottomRows(int rows)
    {
        rows = Math.Min(rows, Height);
        var removed = new List<PlacedBlock>();
        var indices = new List<int>();
        for (var y = Height - rows; y < Height; y++)
        {
            indices.Add(y);
            for (var x = 0; x < Width; x++)
                if (_cells[x, y] != 0)
                    removed.Add(new PlacedBlock(x, y, (PieceKind)(_cells[x, y] - 1)));
        }
        Collapse(indices);
        return removed;
    }

    private void Collapse(List<int> rows)
    {
        var drop = new HashSet<int>(rows);
        var write = Height - 1;
        for (var read = Height - 1; read >= 0; read--)
        {
            if (drop.Contains(read))
                continue;
            if (write != read)
                for (var x = 0; x < Width; x++)
                    _cells[x, write] = _cells[x, read];
            write--;
        }
        for (; write >= 0; write--)
            for (var x = 0; x < Width; x++)
                _cells[x, write] = 0;
    }

    /// <summary>A block sits in the hidden rows above the board: the stack has reached the top.</summary>
    public bool HiddenRowsUsed
    {
        get
        {
            for (var y = 0; y < BlocksRules.HiddenRows; y++)
                for (var x = 0; x < Width; x++)
                    if (_cells[x, y] != 0)
                        return true;
            return false;
        }
    }

    public int BlockCount
    {
        get
        {
            var n = 0;
            foreach (var v in _cells)
                if (v != 0)
                    n++;
            return n;
        }
    }

    public int[,] Snapshot() => (int[,])_cells.Clone();

    public BlocksBoard Clone()
    {
        var copy = new BlocksBoard(Width, VisibleRows);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    /// <summary>Rows from the floor up to the highest block in a column (0 for an empty column).</summary>
    public int ColumnHeight(int x)
    {
        for (var y = 0; y < Height; y++)
            if (_cells[x, y] != 0)
                return Height - y;
        return 0;
    }

    /// <summary>Empty cells with a block somewhere above them in the same column.</summary>
    public int Holes()
    {
        var holes = 0;
        for (var x = 0; x < Width; x++)
        {
            var covered = false;
            for (var y = 0; y < Height; y++)
            {
                if (_cells[x, y] != 0)
                    covered = true;
                else if (covered)
                    holes++;
            }
        }
        return holes;
    }
}

/// <summary>
/// What a piece locking did, in play order: <see cref="Placed"/> blocks went into the stack (<see cref="Before"/> is the
/// board at that moment), <see cref="ClearedRows"/> were full and went away, and if the stack then reached the top the
/// bottom <see cref="RescuedRows"/> rows were taken away too (<see cref="AfterClear"/> is the board just before that,
/// <see cref="RescuedBlocks"/> what was taken). <see cref="DropRows"/> is how far a hard drop fell.
/// </summary>
public sealed class BlocksLock
{
    public required IReadOnlyList<PlacedBlock> Placed { get; init; }
    public required int[,] Before { get; init; }
    public required IReadOnlyList<int> ClearedRows { get; init; }
    public int[,]? AfterClear { get; init; }
    public int RescuedRows { get; init; }
    public IReadOnlyList<PlacedBlock> RescuedBlocks { get; init; } = Array.Empty<PlacedBlock>();
    public int DropRows { get; init; }

    public int Lines => ClearedRows.Count;
    public bool Rescued => RescuedRows > 0;
}

/// <summary>
/// One round of 블록 쌓기: the board, the falling piece, the 7-bag, hold, gravity and the lock delay. The view calls
/// <see cref="Update"/> every frame and the input methods; a lock (from gravity or <see cref="HardDrop"/>) clears full
/// lines and, when the stack has reached the top, takes the bottom rows away so the round always goes on.
/// </summary>
public sealed class BlocksRound
{
    private readonly PieceBag _bag;
    private float _fallTimer;
    private float _lockTimer;
    private int _lockResets;
    private int _lowestY;

    public BlocksRound(BlocksLevel level, Random rng)
    {
        Level = level;
        Board = new BlocksBoard(BlocksRules.Width(level), BlocksRules.Height(level));
        _bag = new PieceBag(rng);
        Spawn(_bag.Next());
    }

    public BlocksLevel Level { get; }
    public BlocksBoard Board { get; }
    public ActivePiece Piece { get; private set; }
    public PieceKind? Held { get; private set; }

    /// <summary>Hold was used for this piece (once per piece).</summary>
    public bool HoldUsed { get; private set; }

    public int Lines { get; private set; }
    public int BestAtOnce { get; private set; }
    public int Rescues { get; private set; }
    public int PiecesLocked { get; private set; }

    public int Stage => BlocksRules.Stage(Level, Lines);
    public float FallInterval => BlocksRules.FallInterval(Level, Lines);

    /// <summary>The piece rests on the floor or the stack (the lock delay runs).</summary>
    public bool Grounded => !Board.Fits(Piece with { Y = Piece.Y + 1 });

    public float LockTimer => _lockTimer;

    /// <summary>Box row where the piece would land (the ghost).</summary>
    public int GhostY => Board.DropY(Piece);

    public IReadOnlyList<PieceKind> Preview(int count = 3) => _bag.Peek(count);

    public bool Move(int dx)
    {
        var moved = Piece with { X = Piece.X + dx };
        if (!Board.Fits(moved))
            return false;
        Piece = moved;
        Moved();
        return true;
    }

    /// <summary>Turns clockwise (+1) or counterclockwise (-1), shifting aside or up when blocked (see <see cref="BlocksRules.Kicks"/>).</summary>
    public bool Rotate(int dir)
    {
        var rotation = BlocksRules.Mod4(Piece.Rotation + Math.Sign(dir));
        foreach (var kick in BlocksRules.Kicks(Piece.Kind))
        {
            var turned = new ActivePiece(Piece.Kind, rotation, Piece.X + kick.X, Piece.Y + kick.Y);
            if (!Board.Fits(turned))
                continue;
            Piece = turned;
            Moved();
            return true;
        }
        return false;
    }

    /// <summary>One row down now (↓ pressed). False when it is already resting.</summary>
    public bool SoftDrop()
    {
        if (Grounded)
            return false;
        StepDown();
        _fallTimer = 0;
        return true;
    }

    /// <summary>Straight down to the ghost and locks at once.</summary>
    public BlocksLock HardDrop()
    {
        var y = GhostY;
        var rows = y - Piece.Y;
        Piece = Piece with { Y = y };
        return Lock(rows);
    }

    /// <summary>Puts the piece aside (or swaps with the one already there). Once per piece; false if not allowed.</summary>
    public bool Hold()
    {
        if (HoldUsed)
            return false;
        var incoming = Held ?? _bag.Peek(1)[0];
        var spawned = new ActivePiece(incoming, 0, BlocksRules.SpawnX(incoming, Board.Width), BlocksRules.SpawnY(incoming));
        if (!Board.Fits(spawned))
            return false;
        if (Held is null)
            _bag.Next();
        Held = Piece.Kind;
        Spawn(incoming);
        HoldUsed = true;
        return true;
    }

    /// <summary>Gravity and the lock delay. Returns the lock when the piece was fixed this frame.</summary>
    public BlocksLock? Update(float dt, bool softDrop)
    {
        if (!Grounded)
        {
            _lockTimer = 0;
            var interval = softDrop ? Math.Min(FallInterval, BlocksRules.SoftDropInterval) : FallInterval;
            _fallTimer += dt;
            while (_fallTimer >= interval && !Grounded)
            {
                _fallTimer -= interval;
                StepDown();
            }
            if (Grounded)
                _fallTimer = 0;
            return null;
        }
        _fallTimer = 0;
        _lockTimer += dt;
        return _lockTimer >= BlocksRules.LockDelay ? Lock(0) : null;
    }

    private void StepDown()
    {
        Piece = Piece with { Y = Piece.Y + 1 };
        if (Piece.Y > _lowestY)
        {
            // A new lowest point: the piece gets its full set of lock-delay restarts again
            _lowestY = Piece.Y;
            _lockResets = 0;
        }
    }

    private void Moved()
    {
        if (_lockTimer > 0 && _lockResets < BlocksRules.MaxLockResets)
        {
            _lockTimer = 0;
            _lockResets++;
        }
    }

    private BlocksLock Lock(int dropRows)
    {
        var placed = Piece.Cells.Select(c => new PlacedBlock(c.X, c.Y, Piece.Kind)).ToList();
        Board.Place(Piece);
        PiecesLocked++;
        var before = Board.Snapshot();
        var cleared = Board.ClearFullRows();
        Lines += cleared.Count;
        BestAtOnce = Math.Max(BestAtOnce, cleared.Count);

        // Reached the top: instead of "game over", the bottom rows go away until the next piece has room
        int[,]? afterClear = null;
        var rescued = new List<PlacedBlock>();
        var rescuedRows = 0;
        var next = _bag.Peek(1)[0];
        while (NeedsRescue(next) && Board.BlockCount > 0)
        {
            afterClear ??= Board.Snapshot();
            // Positions as on AfterClear: a later pass sees the stack already moved down by the earlier ones
            var shift = rescuedRows;
            rescued.AddRange(Board.RemoveBottomRows(BlocksRules.RescueRows).Select(b => b with { Y = b.Y - shift }));
            rescuedRows += BlocksRules.RescueRows;
        }
        if (rescuedRows > 0)
            Rescues++;

        Spawn(_bag.Next());
        HoldUsed = false;
        return new BlocksLock
        {
            Placed = placed,
            Before = before,
            ClearedRows = cleared,
            AfterClear = afterClear,
            RescuedRows = rescuedRows,
            RescuedBlocks = rescued,
            DropRows = dropRows,
        };
    }

    private bool NeedsRescue(PieceKind next) =>
        Board.HiddenRowsUsed || !Board.Fits(next, 0, BlocksRules.SpawnX(next, Board.Width), BlocksRules.SpawnY(next));

    private void Spawn(PieceKind kind)
    {
        Piece = new ActivePiece(kind, 0, BlocksRules.SpawnX(kind, Board.Width), BlocksRules.SpawnY(kind));
        _fallTimer = 0;
        _lockTimer = 0;
        _lockResets = 0;
        _lowestY = Piece.Y;
    }
}

/// <summary>
/// Held-key repeat for ← / → (keys and touch buttons share one): the caller moves once on the press, then
/// <see cref="Update"/> returns extra steps after <see cref="BlocksRules.RepeatDelay"/>, one every
/// <see cref="BlocksRules.RepeatInterval"/>. The latest press wins; letting it go returns to a direction still held.
/// </summary>
public sealed class KeyRepeat
{
    private readonly int[] _held = new int[2]; // [0] left, [1] right: how many sources hold it
    private float _time;
    private int _done;

    /// <summary>-1, 0 or +1.</summary>
    public int Direction { get; private set; }

    public void Press(int dir)
    {
        if (dir == 0)
            return;
        _held[Index(dir)]++;
        Restart(Math.Sign(dir));
    }

    public void Release(int dir)
    {
        if (dir == 0)
            return;
        var i = Index(dir);
        _held[i] = Math.Max(0, _held[i] - 1);
        if (_held[i] > 0 || Direction != Math.Sign(dir))
            return;
        var other = -Math.Sign(dir);
        Restart(_held[Index(other)] > 0 ? other : 0);
    }

    public void Clear()
    {
        _held[0] = _held[1] = 0;
        Restart(0);
    }

    /// <summary>Extra steps due this frame in <see cref="Direction"/>.</summary>
    public int Update(float dt)
    {
        if (Direction == 0)
            return 0;
        _time += dt;
        if (_time < BlocksRules.RepeatDelay)
            return 0;
        var total = 1 + (int)((_time - BlocksRules.RepeatDelay) / BlocksRules.RepeatInterval);
        var steps = total - _done;
        _done = total;
        return steps;
    }

    private void Restart(int dir)
    {
        Direction = dir;
        _time = 0;
        _done = 0;
    }

    private static int Index(int dir) => dir < 0 ? 0 : 1;
}

/// <summary>A place for the falling piece: turned <see cref="Rotation"/> times, box at column <see cref="X"/>.</summary>
public readonly record struct BlocksPlan(int Rotation, int X, bool UseHold, double Score);

/// <summary>
/// The autoplay (and test) player: tries every turn and column for the piece (and the held / next piece), drops it,
/// looks one piece ahead, and picks the board with few holes, a low and even stack and many lines.
/// </summary>
public static class BlocksBot
{
    public static BlocksPlan Best(BlocksRound round)
    {
        var preview = round.Preview(2);
        var plan = BestFor(round.Board, round.Piece.Kind, false, preview[0]);
        if (!round.HoldUsed)
        {
            var other = round.Held ?? preview[0];
            var after = round.Held is null ? preview[1] : preview[0];
            var held = BestFor(round.Board, other, true, after);
            if (held.Score > plan.Score + 0.5)
                plan = held;
        }
        return plan;
    }

    /// <param name="next">The piece after this one, to look one step ahead (null: judge this placement alone).</param>
    public static BlocksPlan BestFor(BlocksBoard board, PieceKind kind, bool useHold, PieceKind? next = null)
    {
        var best = new BlocksPlan(0, BlocksRules.SpawnX(kind, board.Width), useHold, double.NegativeInfinity);
        var spawnY = BlocksRules.SpawnY(kind);
        var turns = kind == PieceKind.O ? 1 : 4;
        for (var r = 0; r < turns; r++)
            for (var x = -3; x < board.Width; x++)
            {
                var piece = new ActivePiece(kind, r, x, spawnY);
                if (!board.Fits(piece))
                    continue;
                piece = piece with { Y = board.DropY(piece) };
                var copy = board.Clone();
                copy.Place(piece);
                var lines = copy.ClearFullRows().Count;
                var score = next is { } n
                    ? LineWeight * lines + BestFor(copy, n, false).Score
                    : Evaluate(copy, lines);
                if (score > best.Score)
                    best = new BlocksPlan(r, x, useHold, score);
            }
        // No place at all (cannot happen after a rescue, but keep the next piece's search sane)
        if (double.IsNegativeInfinity(best.Score))
            best = best with { Score = -1e6 };
        return best;
    }

    private const double LineWeight = 0.76;

    private static double Evaluate(BlocksBoard board, int lines)
    {
        var heights = Enumerable.Range(0, board.Width).Select(board.ColumnHeight).ToArray();
        var bumpiness = 0;
        for (var x = 0; x + 1 < heights.Length; x++)
            bumpiness += Math.Abs(heights[x] - heights[x + 1]);
        return -0.51 * heights.Sum() + LineWeight * lines - 0.36 * board.Holes() - 0.18 * bumpiness;
    }
}
