using System;
using System.Collections.Generic;
using System.Text;

namespace ToyBox.Core;

/// <summary>그림 스도쿠 difficulty. Each has its own leaderboard (see <see cref="SudokuRules.Variants"/>).</summary>
public enum SudokuLevel { Easy, Normal, Hard }

/// <summary>Picture sudoku rules that do not need the engine (docs/games/15-picture-sudoku.md).</summary>
public static class SudokuRules
{
    /// <summary>The most animals a board uses (6 x 6).</summary>
    public const int MaxAnimals = 6;

    /// <summary>Leaderboard variants, easiest first: a 4 x 4 and a 6 x 6 board are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(SudokuLevel level) => Variants[(int)level].Id;

    /// <summary>GDD: 쉬움 4 x 4 / 보통·어려움 6 x 6.</summary>
    public static int Size(SudokuLevel level) => level == SudokuLevel.Easy ? 4 : 6;

    /// <summary>GDD: empty cells per puzzle (쉬움 6 / 보통 14 / 어려움 20).</summary>
    public static int Blanks(SudokuLevel level) => level switch
    {
        SudokuLevel.Easy => 6,
        SudokuLevel.Normal => 14,
        _ => 20,
    };

    /// <summary>The box shape: 4 x 4 has 2 x 2 boxes, 6 x 6 has boxes 3 wide and 2 tall.</summary>
    public static SudokuShape Shape(SudokuLevel level) => SudokuShape.For(Size(level));

    /// <summary>
    /// The curated puzzles: one generator seed per puzzle, in play order ("다음 문제" walks this list and wraps around).
    /// Every seed is checked by SudokuRulesTests (one answer, solvable step by step); change a seed only with them.
    /// </summary>
    private static readonly int[][] Seeds =
    {
        new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
        new[] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 },
        new[] { 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 },
    };

    public static int PuzzleCount(SudokuLevel level) => Seeds[(int)level].Length;

    public static int Seed(SudokuLevel level, int index) => Seeds[(int)level][Wrap(level, index)];

    /// <summary>Index into the level's list, wrapped (so a stale saved index still works).</summary>
    public static int Wrap(SudokuLevel level, int index)
    {
        var n = PuzzleCount(level);
        return ((index % n) + n) % n;
    }

    /// <summary>The <paramref name="index"/>-th curated puzzle of a level (always the same puzzle for the same index).</summary>
    public static SudokuPuzzle Get(SudokuLevel level, int index) =>
        SudokuGenerator.Generate(Shape(level), Blanks(level), Seed(level, index));

    /// <summary>Records list "42.3초".</summary>
    public static string FormatRecord(long timeMs) => $"{timeMs / 1000f:0.0}초";
}

/// <summary>Which kind of line a <see cref="SudokuUnit"/> is.</summary>
public enum SudokuUnitKind { Row, Column, Box }

/// <summary>A row, column or box: the cells (indices row * size + column) that must hold every animal once.</summary>
public sealed record SudokuUnit(SudokuUnitKind Kind, int Index, int[] Cells);

/// <summary>Board geometry: size, box shape, and the units and peers of each cell.</summary>
public sealed class SudokuShape
{
    private static readonly Dictionary<int, SudokuShape> Cache = new();
    private readonly List<SudokuUnit>[] _unitsOf;
    private readonly int[][] _peers;

    private SudokuShape(int size, int boxWidth, int boxHeight)
    {
        Size = size;
        BoxWidth = boxWidth;
        BoxHeight = boxHeight;
        var units = new List<SudokuUnit>();
        for (var r = 0; r < size; r++)
        {
            var cells = new int[size];
            for (var c = 0; c < size; c++)
                cells[c] = r * size + c;
            units.Add(new SudokuUnit(SudokuUnitKind.Row, r, cells));
        }
        for (var c = 0; c < size; c++)
        {
            var cells = new int[size];
            for (var r = 0; r < size; r++)
                cells[r] = r * size + c;
            units.Add(new SudokuUnit(SudokuUnitKind.Column, c, cells));
        }
        for (var b = 0; b < size; b++)
        {
            var cells = new int[size];
            var top = b / (size / boxWidth) * boxHeight;
            var left = b % (size / boxWidth) * boxWidth;
            var k = 0;
            for (var r = top; r < top + boxHeight; r++)
            for (var c = left; c < left + boxWidth; c++)
                cells[k++] = r * size + c;
            units.Add(new SudokuUnit(SudokuUnitKind.Box, b, cells));
        }
        Units = units;

        _unitsOf = new List<SudokuUnit>[CellCount];
        for (var i = 0; i < CellCount; i++)
            _unitsOf[i] = new List<SudokuUnit>(3);
        foreach (var u in units)
            foreach (var cell in u.Cells)
                _unitsOf[cell].Add(u);
        _peers = new int[CellCount][];
        for (var i = 0; i < CellCount; i++)
        {
            var set = new SortedSet<int>();
            foreach (var u in _unitsOf[i])
                foreach (var cell in u.Cells)
                    if (cell != i)
                        set.Add(cell);
            _peers[i] = new int[set.Count];
            set.CopyTo(_peers[i]);
        }
    }

    /// <summary>4 → 2 x 2 boxes, 6 → boxes 3 wide and 2 tall (the GDD's 2 x 3 boxes).</summary>
    public static SudokuShape For(int size)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(size, out var shape))
            {
                shape = size switch
                {
                    4 => new SudokuShape(4, 2, 2),
                    6 => new SudokuShape(6, 3, 2),
                    _ => throw new ArgumentOutOfRangeException(nameof(size), "Only 4 x 4 and 6 x 6 boards"),
                };
                Cache[size] = shape;
            }
            return shape;
        }
    }

    public int Size { get; }
    /// <summary>Columns per box.</summary>
    public int BoxWidth { get; }
    /// <summary>Rows per box.</summary>
    public int BoxHeight { get; }
    public int CellCount => Size * Size;
    /// <summary>All rows, then all columns, then all boxes.</summary>
    public IReadOnlyList<SudokuUnit> Units { get; }

    public int Row(int cell) => cell / Size;
    public int Column(int cell) => cell % Size;
    public int Box(int cell) => Row(cell) / BoxHeight * (Size / BoxWidth) + Column(cell) / BoxWidth;
    public int Cell(int row, int column) => row * Size + column;

    /// <summary>The row, column and box of a cell.</summary>
    public IReadOnlyList<SudokuUnit> UnitsOf(int cell) => _unitsOf[cell];

    /// <summary>Every other cell that shares a row, column or box with <paramref name="cell"/>.</summary>
    public IReadOnlyList<int> Peers(int cell) => _peers[cell];

    /// <summary>Bit mask with a bit for every animal 1..Size.</summary>
    public int AllMask => ((1 << Size) - 1) << 1;
}

/// <summary>A puzzle: the full answer and which cells start filled (0 = empty in <see cref="Givens"/>).</summary>
public sealed class SudokuPuzzle
{
    public SudokuPuzzle(SudokuShape shape, int[] solution, int[] givens)
    {
        Shape = shape;
        Solution = solution;
        Givens = givens;
    }

    public SudokuShape Shape { get; }
    public int[] Solution { get; }
    public int[] Givens { get; }

    public int BlankCount
    {
        get
        {
            var n = 0;
            foreach (var v in Givens)
                if (v == 0)
                    n++;
            return n;
        }
    }

    /// <summary>Text picture: animals as 1..6, '.' for empty cells, '|' and '-' between boxes.</summary>
    public string ToAscii() => SudokuSolver.ToAscii(Shape, Givens);
}

/// <summary>Candidates, conflicts, a full search (to count answers) and a "step by step" solver like a child's.</summary>
public static class SudokuSolver
{
    /// <summary>Bit mask of the animals that do not clash in a cell (bit v for animal v).</summary>
    public static int Candidates(SudokuShape shape, int[] grid, int cell)
    {
        var mask = shape.AllMask;
        foreach (var p in shape.Peers(cell))
            if (grid[p] != 0)
                mask &= ~(1 << grid[p]);
        return mask;
    }

    /// <summary>The cells that already hold <paramref name="value"/> in the row, column or box of <paramref name="cell"/>.</summary>
    public static List<int> Conflicts(SudokuShape shape, int[] grid, int cell, int value)
    {
        var list = new List<int>();
        foreach (var p in shape.Peers(cell))
            if (grid[p] == value)
                list.Add(p);
        return list;
    }

    public static int BitCount(int mask)
    {
        var n = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            n++;
        }
        return n;
    }

    /// <summary>The lowest animal in a mask (0 if empty).</summary>
    public static int Lowest(int mask)
    {
        for (var v = 1; v <= SudokuRules.MaxAnimals; v++)
            if ((mask & (1 << v)) != 0)
                return v;
        return 0;
    }

    /// <summary>True if every unit holds every animal exactly once.</summary>
    public static bool IsValidSolution(SudokuShape shape, int[] grid)
    {
        foreach (var u in shape.Units)
        {
            var seen = 0;
            foreach (var cell in u.Cells)
            {
                var v = grid[cell];
                if (v < 1 || v > shape.Size || (seen & (1 << v)) != 0)
                    return false;
                seen |= 1 << v;
            }
        }
        return true;
    }

    /// <summary>How many answers the grid has, counting up to <paramref name="limit"/> (backtracking, fewest choices first).</summary>
    public static int CountSolutions(SudokuShape shape, int[] grid, int limit = 2)
    {
        var work = (int[])grid.Clone();
        for (var i = 0; i < work.Length; i++)
            if (work[i] != 0 && (Candidates(shape, work, i) & (1 << work[i])) == 0)
                return 0; // Already clashes
        var count = 0;
        Search(shape, work, limit, ref count);
        return count;
    }

    private static void Search(SudokuShape shape, int[] grid, int limit, ref int count)
    {
        var best = -1;
        var bestMask = 0;
        var bestCount = int.MaxValue;
        for (var i = 0; i < grid.Length; i++)
        {
            if (grid[i] != 0)
                continue;
            var mask = Candidates(shape, grid, i);
            var n = BitCount(mask);
            if (n == 0)
                return;
            if (n < bestCount)
            {
                best = i;
                bestMask = mask;
                bestCount = n;
            }
        }
        if (best < 0)
        {
            count++;
            return;
        }
        for (var v = 1; v <= shape.Size && count < limit; v++)
        {
            if ((bestMask & (1 << v)) == 0)
                continue;
            grid[best] = v;
            Search(shape, grid, limit, ref count);
            grid[best] = 0;
        }
    }

    /// <summary>
    /// The next cell a child can be sure of, without guessing: a cell where only one animal fits ("naked single"), else an
    /// animal that fits only one cell of a row, column or box ("hidden single"). Null if neither exists (or no empty cell).
    /// </summary>
    public static (int Cell, int Value)? NextSure(SudokuShape shape, int[] grid)
    {
        for (var i = 0; i < grid.Length; i++)
        {
            if (grid[i] != 0)
                continue;
            var mask = Candidates(shape, grid, i);
            if (BitCount(mask) == 1)
                return (i, Lowest(mask));
        }
        foreach (var u in shape.Units)
            for (var v = 1; v <= shape.Size; v++)
            {
                var spot = -1;
                var spots = 0;
                var present = false;
                foreach (var cell in u.Cells)
                {
                    if (grid[cell] == v)
                    {
                        present = true;
                        break;
                    }
                    if (grid[cell] == 0 && (Candidates(shape, grid, cell) & (1 << v)) != 0)
                    {
                        spot = cell;
                        spots++;
                    }
                }
                if (!present && spots == 1)
                    return (spot, v);
            }
        return null;
    }

    /// <summary>True if <see cref="NextSure"/> steps alone fill the whole grid (no guessing needed).</summary>
    public static bool SolvableStepByStep(SudokuShape shape, int[] grid)
    {
        var work = (int[])grid.Clone();
        while (true)
        {
            var empty = Array.IndexOf(work, 0) >= 0;
            if (!empty)
                return IsValidSolution(shape, work);
            if (NextSure(shape, work) is not { } step)
                return false;
            work[step.Cell] = step.Value;
        }
    }

    public static string ToAscii(SudokuShape shape, int[] grid)
    {
        var sb = new StringBuilder();
        for (var r = 0; r < shape.Size; r++)
        {
            if (r > 0 && r % shape.BoxHeight == 0)
            {
                for (var c = 0; c < shape.Size; c++)
                {
                    if (c > 0 && c % shape.BoxWidth == 0)
                        sb.Append('+');
                    sb.Append('-');
                }
                sb.Append('\n');
            }
            for (var c = 0; c < shape.Size; c++)
            {
                if (c > 0 && c % shape.BoxWidth == 0)
                    sb.Append('|');
                var v = grid[shape.Cell(r, c)];
                sb.Append(v == 0 ? '.' : (char)('0' + v));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>
/// Deterministic puzzle generator: a shuffled full answer (relabelled animals, rows swapped within bands, bands swapped,
/// columns within stacks, stacks), then cells are emptied box by box in turn (so the blanks spread over the board) as
/// long as the puzzle keeps exactly one answer and can be solved step by step without guessing. Uses its own small
/// random generator so a seed gives the same puzzle on every platform and .NET version.
/// </summary>
public static class SudokuGenerator
{
    private const int MaxAttempts = 400;

    public static SudokuPuzzle Generate(SudokuShape shape, int blanks, int seed)
    {
        var rng = new SeededRandom(seed);
        var solution = Solution(shape, rng);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
            if (TryEmpty(shape, solution, blanks, rng) is { } givens)
                return new SudokuPuzzle(shape, solution, givens);
        throw new InvalidOperationException($"No {shape.Size} x {shape.Size} puzzle with {blanks} blanks for seed {seed}");
    }

    private static int[] Solution(SudokuShape shape, SeededRandom rng)
    {
        var n = shape.Size;
        var bw = shape.BoxWidth;
        var bh = shape.BoxHeight;
        var animals = Permutation(n, rng);
        // Rows: shuffle the bands (groups of box-height rows) and the rows inside each band; columns likewise by stacks
        var rows = Grouped(n, bh, rng);
        var cols = Grouped(n, bw, rng);
        var grid = new int[n * n];
        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++)
        {
            var sr = rows[r];
            var sc = cols[c];
            var pattern = (bw * (sr % bh) + sr / bh + sc) % n; // A valid base answer
            grid[r * n + c] = animals[pattern] + 1;
        }
        return grid;
    }

    private static int[] Grouped(int n, int group, SeededRandom rng)
    {
        var groups = Permutation(n / group, rng);
        var order = new int[n];
        var k = 0;
        foreach (var g in groups)
            foreach (var i in Permutation(group, rng))
                order[k++] = g * group + i;
        return order;
    }

    private static int[] Permutation(int n, SeededRandom rng)
    {
        var a = new int[n];
        for (var i = 0; i < n; i++)
            a[i] = i;
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
        return a;
    }

    private static int[]? TryEmpty(SudokuShape shape, int[] solution, int blanks, SeededRandom rng)
    {
        // Each box's cells in random order, then taken one box at a time in a random box order (round robin)
        var boxes = new List<Queue<int>>();
        foreach (var b in Permutation(shape.Size, rng))
        {
            var cells = shape.Units[2 * shape.Size + b].Cells;
            var q = new Queue<int>();
            foreach (var i in Permutation(cells.Length, rng))
                q.Enqueue(cells[i]);
            boxes.Add(q);
        }
        var order = new List<int>();
        while (order.Count < shape.CellCount)
            foreach (var q in boxes)
                if (q.Count > 0)
                    order.Add(q.Dequeue());

        var grid = (int[])solution.Clone();
        var removed = 0;
        foreach (var cell in order)
        {
            if (removed == blanks)
                break;
            var keep = grid[cell];
            grid[cell] = 0;
            if (!LeavesUnitEmpty(shape, grid, cell)
                && SudokuSolver.SolvableStepByStep(shape, grid) && SudokuSolver.CountSolutions(shape, grid) == 1)
                removed++;
            else
                grid[cell] = keep;
        }
        return removed == blanks ? grid : null;
    }

    /// <summary>Every row, column and box keeps at least one starting animal (something to begin from everywhere).</summary>
    private static bool LeavesUnitEmpty(SudokuShape shape, int[] grid, int cell)
    {
        foreach (var u in shape.UnitsOf(cell))
            if (Array.TrueForAll(u.Cells, c => grid[c] == 0))
                return true;
        return false;
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

        public int Next(int max) => (int)(NextUInt() % (uint)max);
    }
}

/// <summary>What one placing input did.</summary>
public enum SudokuPlaceKind
{
    /// <summary>Nothing (the board is done, or the cell is out of range).</summary>
    Ignored,
    /// <summary>A starting (or hinted) animal: it cannot change.</summary>
    Locked,
    /// <summary>The animal went in.</summary>
    Placed,
    /// <summary>The same animal again: it came out (toggle).</summary>
    Removed,
    /// <summary>The same animal is already in the row, column or box: it does not go in (no penalty).</summary>
    Clash,
}

/// <summary>The result of <see cref="SudokuBoard.Place"/>.</summary>
/// <param name="Clashes">The cells holding the same animal (for <see cref="SudokuPlaceKind.Clash"/>).</param>
/// <param name="Completed">Rows, columns and boxes this placement just filled.</param>
public sealed record SudokuMove(SudokuPlaceKind Kind, int Cell, int Value, IReadOnlyList<int> Clashes,
    IReadOnlyList<SudokuUnit> Completed, bool Solved);

/// <summary>The result of <see cref="SudokuBoard.UseHint"/>: the cell that got its animal, and any animals that had to make room.</summary>
public sealed record SudokuHint(int Cell, int Value, IReadOnlyList<int> Cleared, IReadOnlyList<SudokuUnit> Completed, bool Solved);

/// <summary>
/// State of one play of a puzzle: the animals placed, pencil memos, hints, an undo list and the clock. Clashing animals
/// never go in, so the board never holds a clash; with one answer, a full board is always the answer. The clock starts
/// with the first input (<see cref="Start"/> or any action) and stops when the board is full.
/// </summary>
public sealed class SudokuBoard
{
    private readonly int[] _values;
    private readonly int[] _memos;
    private readonly bool[] _hinted;
    private readonly Stack<(int[] Values, int[] Memos)> _undo = new();
    private double _seconds;

    public SudokuBoard(SudokuPuzzle puzzle)
    {
        Puzzle = puzzle;
        _values = (int[])puzzle.Givens.Clone();
        _memos = new int[puzzle.Shape.CellCount];
        _hinted = new bool[puzzle.Shape.CellCount];
    }

    public SudokuPuzzle Puzzle { get; }
    public SudokuShape Shape => Puzzle.Shape;
    public int Hints { get; private set; }
    public bool Started { get; private set; }
    public bool Solved { get; private set; }
    public bool CanUndo => _undo.Count > 0 && !Solved;
    public long TimeMs => (long)Math.Round(_seconds * 1000);

    /// <summary>A copy of the current grid (0 = empty).</summary>
    public int[] Grid => (int[])_values.Clone();

    public int Value(int cell) => _values[cell];
    public bool IsGiven(int cell) => Puzzle.Givens[cell] != 0;
    public bool IsHinted(int cell) => _hinted[cell];
    /// <summary>Starting and hinted animals cannot be changed.</summary>
    public bool IsFixed(int cell) => IsGiven(cell) || _hinted[cell];
    /// <summary>Pencil memo bits (bit v for animal v).</summary>
    public int Memo(int cell) => _memos[cell];
    public bool HasMemo(int cell, int value) => (_memos[cell] & (1 << value)) != 0;

    public int EmptyCount
    {
        get
        {
            var n = 0;
            foreach (var v in _values)
                if (v == 0)
                    n++;
            return n;
        }
    }

    /// <summary>The first input (also called by every action): starts the clock.</summary>
    public void Start()
    {
        if (!Solved)
            Started = true;
    }

    /// <summary>Adds play time (the game calls this every frame). Counts only between the first input and the end.</summary>
    public void AddTime(double seconds)
    {
        if (Started && !Solved && seconds > 0)
            _seconds += seconds;
    }

    private bool Valid(int cell) => cell >= 0 && cell < _values.Length;

    /// <summary>
    /// Puts <paramref name="value"/> in <paramref name="cell"/>. The same animal again takes it out. A clash leaves the
    /// board as it was and lists the cells that hold the same animal.
    /// </summary>
    public SudokuMove Place(int cell, int value)
    {
        var none = Array.Empty<int>();
        var noUnits = Array.Empty<SudokuUnit>();
        if (Solved || !Valid(cell) || value < 1 || value > Shape.Size)
            return new SudokuMove(SudokuPlaceKind.Ignored, cell, value, none, noUnits, Solved);
        Start();
        if (IsFixed(cell))
            return new SudokuMove(SudokuPlaceKind.Locked, cell, value, none, noUnits, false);
        if (_values[cell] == value)
        {
            Save();
            _values[cell] = 0;
            return new SudokuMove(SudokuPlaceKind.Removed, cell, value, none, noUnits, false);
        }
        var clashes = SudokuSolver.Conflicts(Shape, _values, cell, value);
        if (clashes.Count > 0)
            return new SudokuMove(SudokuPlaceKind.Clash, cell, value, clashes, noUnits, false);
        Save();
        Put(cell, value);
        return new SudokuMove(SudokuPlaceKind.Placed, cell, value, none, CompletedBy(cell), Solved);
    }

    /// <summary>Clears a cell's animal, or its memos if it has no animal. False if there was nothing to clear.</summary>
    public bool Erase(int cell)
    {
        if (Solved || !Valid(cell))
            return false;
        Start();
        if (IsFixed(cell) || (_values[cell] == 0 && _memos[cell] == 0))
            return false;
        Save();
        _values[cell] = 0;
        _memos[cell] = 0;
        return true;
    }

    /// <summary>Pencil memo on an empty cell: toggles a sketch of the animal. Returns whether it is now on (null: not allowed).</summary>
    public bool? ToggleMemo(int cell, int value)
    {
        if (Solved || !Valid(cell) || value < 1 || value > Shape.Size)
            return null;
        Start();
        if (IsFixed(cell) || _values[cell] != 0)
            return null;
        Save();
        _memos[cell] ^= 1 << value;
        return HasMemo(cell, value);
    }

    /// <summary>Goes back one step (place, take out, erase, memo). Hints are not undone.</summary>
    public bool Undo()
    {
        if (!CanUndo)
            return false;
        var (values, memos) = _undo.Pop();
        Array.Copy(values, _values, values.Length);
        Array.Copy(memos, _memos, memos.Length);
        return true;
    }

    /// <summary>
    /// Puts the right animal in one cell (it then stays like a starting animal). Picks <paramref name="preferred"/> if
    /// it is empty, else the empty cell with the fewest animals that fit. Its animal must not clash with what is on the
    /// board; if every empty cell's animal clashes (a placed animal is in the wrong spot), a wrong animal is replaced
    /// instead and the animals it clashed with come out. No penalty: only <see cref="Hints"/> counts up.
    /// </summary>
    public SudokuHint? UseHint(int? preferred = null)
    {
        if (Solved)
            return null;
        Start();
        var cleared = new List<int>();
        var cell = PickHintCell(preferred);
        if (cell < 0)
        {
            // A placed animal is in another's spot: fix the first such cell and take out what clashes with it
            for (var i = 0; i < _values.Length && cell < 0; i++)
                if (_values[i] != 0 && _values[i] != Puzzle.Solution[i])
                    cell = i;
            if (cell < 0)
                return null;
            foreach (var c in SudokuSolver.Conflicts(Shape, _values, cell, Puzzle.Solution[cell]))
            {
                _values[c] = 0;
                cleared.Add(c);
            }
        }
        var value = Puzzle.Solution[cell];
        Hints++;
        _hinted[cell] = true;
        Put(cell, value);
        _undo.Clear(); // Undo never reaches back across a hint
        return new SudokuHint(cell, value, cleared, CompletedBy(cell), Solved);
    }

    private int PickHintCell(int? preferred)
    {
        bool Fits(int c) => _values[c] == 0 && SudokuSolver.Conflicts(Shape, _values, c, Puzzle.Solution[c]).Count == 0;

        if (preferred is { } p && Valid(p) && Fits(p))
            return p;
        var best = -1;
        var bestCount = int.MaxValue;
        for (var i = 0; i < _values.Length; i++)
        {
            if (!Fits(i))
                continue;
            var n = SudokuSolver.BitCount(SudokuSolver.Candidates(Shape, _values, i));
            if (n < bestCount)
            {
                best = i;
                bestCount = n;
            }
        }
        return best;
    }

    private void Put(int cell, int value)
    {
        _values[cell] = value;
        _memos[cell] = 0;
        // The animal is now known in this row, column and box: its pencil sketches there are no longer needed
        foreach (var p in Shape.Peers(cell))
            _memos[p] &= ~(1 << value);
        if (Array.IndexOf(_values, 0) < 0)
        {
            Solved = true;
            _undo.Clear();
        }
    }

    private void Save() => _undo.Push(((int[])_values.Clone(), (int[])_memos.Clone()));

    /// <summary>The units of <paramref name="cell"/> that are now full (no clash is possible, so full means done).</summary>
    private List<SudokuUnit> CompletedBy(int cell)
    {
        var list = new List<SudokuUnit>();
        foreach (var u in Shape.UnitsOf(cell))
        {
            var full = true;
            foreach (var c in u.Cells)
                if (_values[c] == 0)
                {
                    full = false;
                    break;
                }
            if (full)
                list.Add(u);
        }
        return list;
    }

    /// <summary>The leaderboard row: time decides (<see cref="RankOrder.TimeAsc"/>); <see cref="ScoreEntry.Score"/> keeps the hints for reference.</summary>
    public ScoreEntry Entry(string name, long unixTime) => new(name, Hints, TimeMs, unixTime);
}
