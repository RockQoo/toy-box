using System;
using System.Collections.Generic;
using System.Text;

namespace ToyBox.Core;

/// <summary>네모 나누기 difficulty. Each has its own leaderboard (see <see cref="ShikakuRules.Variants"/>).</summary>
public enum ShikakuLevel { Easy, Normal, Hard }

/// <summary>Shikaku ("네모 나누기") rules that do not need the engine (docs/games/16-shikaku.md).</summary>
public static class ShikakuRules
{
    /// <summary>Leaderboard variants, easiest first: a 4 x 4 and a 7 x 7 board are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(ShikakuLevel level) => Variants[(int)level].Id;

    /// <summary>GDD: 쉬움 4 x 4 / 보통 5 x 5 / 어려움 7 x 7.</summary>
    public static int Size(ShikakuLevel level) => level switch
    {
        ShikakuLevel.Easy => 4,
        ShikakuLevel.Normal => 5,
        _ => 7,
    };

    /// <summary>GDD: the biggest number (쉬움 6 / 보통 8 / 어려움 10).</summary>
    public static int MaxClue(ShikakuLevel level) => level switch
    {
        ShikakuLevel.Easy => 6,
        ShikakuLevel.Normal => 8,
        _ => 10,
    };

    /// <summary>
    /// The curated puzzles: one generator seed per puzzle, in play order ("다음 문제" walks this list and wraps around).
    /// Every seed is checked by ShikakuRulesTests (exactly one answer); change a seed only with them.
    /// </summary>
    private static readonly int[][] Seeds =
    {
        new[] { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 },
        new[] { 201, 202, 203, 204, 205, 206, 207, 208, 209, 210 },
        new[] { 301, 302, 303, 304, 305, 306, 307, 308, 309, 310 },
    };

    public static int PuzzleCount(ShikakuLevel level) => Seeds[(int)level].Length;

    public static int Seed(ShikakuLevel level, int index) => Seeds[(int)level][Wrap(level, index)];

    /// <summary>Index into the level's list, wrapped (so a stale saved index still works).</summary>
    public static int Wrap(ShikakuLevel level, int index)
    {
        var n = PuzzleCount(level);
        return ((index % n) + n) % n;
    }

    /// <summary>The <paramref name="index"/>-th curated puzzle of a level (always the same puzzle for the same index).</summary>
    public static ShikakuPuzzle Get(ShikakuLevel level, int index) =>
        ShikakuGenerator.Generate(Size(level), MaxClue(level), Seed(level, index));

    /// <summary>Records list "42.3초".</summary>
    public static string FormatRecord(long timeMs) => $"{timeMs / 1000f:0.0}초";
}

/// <summary>A rectangle of cells: top-left row and column, and how many rows and columns it spans.</summary>
public readonly record struct ShikakuRect(int Row, int Col, int Rows, int Cols)
{
    public int Area => Rows * Cols;
    public int Bottom => Row + Rows - 1;
    public int Right => Col + Cols - 1;

    /// <summary>The rectangle with these two cells as opposite corners (in any order).</summary>
    public static ShikakuRect FromCorners(int row1, int col1, int row2, int col2) =>
        new(Math.Min(row1, row2), Math.Min(col1, col2), Math.Abs(row1 - row2) + 1, Math.Abs(col1 - col2) + 1);

    public bool Contains(int row, int col) => row >= Row && row <= Bottom && col >= Col && col <= Right;

    public bool Overlaps(ShikakuRect o) => Row <= o.Bottom && o.Row <= Bottom && Col <= o.Right && o.Col <= Right;

    /// <summary>The cell indices (row * size + column) inside, row by row.</summary>
    public IEnumerable<int> Cells(int size)
    {
        for (var r = Row; r <= Bottom; r++)
            for (var c = Col; c <= Right; c++)
                yield return r * size + c;
    }
}

/// <summary>A puzzle: the numbers (0 = no number) and the generator's answer (one piece per number).</summary>
public sealed class ShikakuPuzzle
{
    public ShikakuPuzzle(int size, int[] clues, IReadOnlyList<ShikakuRect> solution)
    {
        Size = size;
        Clues = clues;
        Solution = solution;
        var list = new List<int>();
        for (var i = 0; i < clues.Length; i++)
            if (clues[i] > 0)
                list.Add(i);
        ClueCells = list;
    }

    public int Size { get; }
    public int CellCount => Size * Size;
    /// <summary>The number in each cell (row * size + column), 0 where there is none.</summary>
    public int[] Clues { get; }
    /// <summary>The answer pieces (the only answer: see <see cref="ShikakuSolver.CountSolutions"/>).</summary>
    public IReadOnlyList<ShikakuRect> Solution { get; }
    /// <summary>Cells that hold a number, in reading order.</summary>
    public IReadOnlyList<int> ClueCells { get; }

    /// <summary>Text picture: numbers (10 as 'A'), '.' for cells without one.</summary>
    public string ToAscii()
    {
        var sb = new StringBuilder();
        for (var r = 0; r < Size; r++)
        {
            for (var c = 0; c < Size; c++)
            {
                var v = Clues[r * Size + c];
                sb.Append(v == 0 ? '.' : v < 10 ? (char)('0' + v) : (char)('A' + v - 10));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>Why a drawn rectangle does (or does not) go in.</summary>
public enum ShikakuFit
{
    /// <summary>Exactly one number, and the rectangle has that many cells.</summary>
    Fits,
    /// <summary>No number inside.</summary>
    NoClue,
    /// <summary>Two or more numbers inside.</summary>
    TooManyClues,
    /// <summary>One number, but a different number of cells.</summary>
    WrongSize,
}

/// <summary>What a rectangle covers: how many numbers, the (first) number and its cell, and the cell count.</summary>
public readonly record struct ShikakuCheck(ShikakuFit Fit, int ClueCount, int Clue, int ClueCell, int Area);

/// <summary>Rectangle checks and a full search that counts answers.</summary>
public static class ShikakuSolver
{
    /// <summary>The rule check of the GDD: exactly one number, and as many cells as the number says.</summary>
    public static ShikakuCheck Check(ShikakuPuzzle puzzle, ShikakuRect rect)
    {
        var count = 0;
        var clue = 0;
        var clueCell = -1;
        foreach (var cell in rect.Cells(puzzle.Size))
        {
            if (puzzle.Clues[cell] == 0)
                continue;
            if (count == 0)
            {
                clue = puzzle.Clues[cell];
                clueCell = cell;
            }
            count++;
        }
        var fit = count switch
        {
            0 => ShikakuFit.NoClue,
            > 1 => ShikakuFit.TooManyClues,
            _ => rect.Area == clue ? ShikakuFit.Fits : ShikakuFit.WrongSize,
        };
        return new ShikakuCheck(fit, count, clue, clueCell, rect.Area);
    }

    public static bool InBounds(int size, ShikakuRect r) =>
        r.Row >= 0 && r.Col >= 0 && r.Rows >= 1 && r.Cols >= 1 && r.Bottom < size && r.Right < size;

    /// <summary>Every rectangle that fits the rule for the number in <paramref name="clueCell"/> (it holds that number only).</summary>
    public static List<ShikakuRect> Candidates(ShikakuPuzzle puzzle, int clueCell)
    {
        var list = new List<ShikakuRect>();
        var n = puzzle.Size;
        var area = puzzle.Clues[clueCell];
        var row = clueCell / n;
        var col = clueCell % n;
        for (var h = 1; h <= area; h++)
        {
            if (area % h != 0)
                continue;
            var w = area / h;
            if (h > n || w > n)
                continue;
            for (var top = row - h + 1; top <= row; top++)
                for (var left = col - w + 1; left <= col; left++)
                {
                    var rect = new ShikakuRect(top, left, h, w);
                    if (InBounds(n, rect) && Check(puzzle, rect).Fit == ShikakuFit.Fits)
                        list.Add(rect);
                }
        }
        return list;
    }

    /// <summary>True if the pieces cover the board exactly once and every piece fits its number.</summary>
    public static bool IsValidSolution(ShikakuPuzzle puzzle, IEnumerable<ShikakuRect> pieces)
    {
        var covered = new bool[puzzle.CellCount];
        foreach (var p in pieces)
        {
            if (!InBounds(puzzle.Size, p) || Check(puzzle, p).Fit != ShikakuFit.Fits)
                return false;
            foreach (var cell in p.Cells(puzzle.Size))
            {
                if (covered[cell])
                    return false;
                covered[cell] = true;
            }
        }
        return Array.TrueForAll(covered, c => c);
    }

    /// <summary>
    /// How many ways the board splits into pieces that fit the rule, counting up to <paramref name="limit"/>.
    /// Fills cells in reading order: the first empty cell is always the top-left corner of its piece.
    /// </summary>
    public static int CountSolutions(ShikakuPuzzle puzzle, int limit = 2)
    {
        var n = puzzle.Size;
        // Every fitting rectangle, grouped by its top-left cell
        var byCorner = new List<ShikakuRect>[puzzle.CellCount];
        for (var i = 0; i < byCorner.Length; i++)
            byCorner[i] = new List<ShikakuRect>();
        foreach (var clue in puzzle.ClueCells)
            foreach (var rect in Candidates(puzzle, clue))
                byCorner[rect.Row * n + rect.Col].Add(rect);
        var covered = new bool[puzzle.CellCount];
        var count = 0;
        Search(n, byCorner, covered, 0, limit, ref count);
        return count;
    }

    private static void Search(int n, List<ShikakuRect>[] byCorner, bool[] covered, int from, int limit, ref int count)
    {
        var first = from;
        while (first < covered.Length && covered[first])
            first++;
        if (first == covered.Length)
        {
            count++;
            return;
        }
        foreach (var rect in byCorner[first])
        {
            if (count >= limit)
                return;
            var free = true;
            foreach (var cell in rect.Cells(n))
                if (covered[cell])
                {
                    free = false;
                    break;
                }
            if (!free)
                continue;
            foreach (var cell in rect.Cells(n))
                covered[cell] = true;
            Search(n, byCorner, covered, first + 1, limit, ref count);
            foreach (var cell in rect.Cells(n))
                covered[cell] = false;
        }
    }
}

/// <summary>
/// Deterministic puzzle generator: splits the board into random rectangles (sizes 2..max, a few 1s), puts each piece's
/// number in a random cell of it, and keeps the puzzle only if it has exactly one answer. Uses its own small random
/// generator so a seed gives the same puzzle on every platform and .NET version.
/// </summary>
public static class ShikakuGenerator
{
    private const int MaxAttempts = 5000;
    private const int CluePlacementsPerSplit = 6;

    public static ShikakuPuzzle Generate(int size, int maxClue, int seed)
    {
        var rng = new SeededRandom(seed);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var pieces = Split(size, maxClue, rng);
            if (pieces is null)
                continue;
            for (var k = 0; k < CluePlacementsPerSplit; k++)
            {
                var clues = new int[size * size];
                foreach (var p in pieces)
                {
                    var cells = new List<int>(p.Cells(size));
                    clues[cells[rng.Next(cells.Count)]] = p.Area;
                }
                var puzzle = new ShikakuPuzzle(size, clues, Sorted(pieces, size));
                if (ShikakuSolver.CountSolutions(puzzle) == 1)
                    return puzzle;
            }
        }
        throw new InvalidOperationException($"No {size} x {size} puzzle for seed {seed}");
    }

    private static List<ShikakuRect> Sorted(List<ShikakuRect> pieces, int size)
    {
        var list = new List<ShikakuRect>(pieces);
        list.Sort((a, b) => (a.Row * size + a.Col).CompareTo(b.Row * size + b.Col));
        return list;
    }

    /// <summary>
    /// One random split: the first empty cell (reading order) starts a rectangle of a random fitting shape. Rejects
    /// splits with too many 1-cell pieces (they are free) or too few pieces (nothing to think about).
    /// </summary>
    private static List<ShikakuRect>? Split(int size, int maxClue, SeededRandom rng)
    {
        var covered = new bool[size * size];
        var pieces = new List<ShikakuRect>();
        var ones = 0;
        for (var first = 0; first < covered.Length; first++)
        {
            if (covered[first])
                continue;
            var row = first / size;
            var col = first % size;
            // Free width along the row from this cell
            var maxW = 0;
            while (col + maxW < size && !covered[row * size + col + maxW])
                maxW++;
            var options = new List<ShikakuRect>();
            for (var h = 1; row + h - 1 < size; h++)
                for (var w = 1; w <= maxW; w++)
                {
                    if (h * w > maxClue)
                        break;
                    var rect = new ShikakuRect(row, col, h, w);
                    var free = true;
                    foreach (var cell in rect.Cells(size))
                        if (covered[cell])
                        {
                            free = false;
                            break;
                        }
                    if (free)
                        options.Add(rect);
                }
            // Prefer pieces of 2 or more cells; a 1-cell piece only when nothing else fits or by a small chance
            var big = options.FindAll(o => o.Area >= 2);
            ShikakuRect pick;
            if (big.Count > 0 && rng.Next(100) >= 6)
            {
                // Lean toward middle sizes: draw two and keep the bigger one now and then
                pick = big[rng.Next(big.Count)];
                if (rng.Next(3) == 0)
                {
                    var other = big[rng.Next(big.Count)];
                    if (other.Area > pick.Area)
                        pick = other;
                }
            }
            else
                pick = new ShikakuRect(row, col, 1, 1);
            if (pick.Area == 1)
                ones++;
            foreach (var cell in pick.Cells(size))
                covered[cell] = true;
            pieces.Add(pick);
        }
        var minPieces = size switch { 4 => 4, 5 => 5, _ => 8 };
        var maxPieces = size switch { 4 => 6, 5 => 8, _ => 12 };
        if (ones > 1 || pieces.Count < minPieces || pieces.Count > maxPieces)
            return null;
        // At least one piece uses (nearly) the biggest number, so the level's range is felt
        var biggest = 0;
        foreach (var p in pieces)
            biggest = Math.Max(biggest, p.Area);
        return biggest >= maxClue - 2 ? pieces : null;
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

/// <summary>A piece on the board: its rectangle and the cell of the number it holds.</summary>
public sealed record ShikakuPiece(ShikakuRect Rect, int ClueCell);

/// <summary>What one drawing input did.</summary>
public enum ShikakuDrawKind
{
    /// <summary>Nothing (the board is done, or the rectangle is off the board).</summary>
    Ignored,
    /// <summary>The piece went in (overlapped pieces came out).</summary>
    Placed,
    /// <summary>The rectangle breaks the rule: nothing changes (no penalty).</summary>
    NotPlaced,
}

/// <summary>The result of <see cref="ShikakuBoard.Draw"/>.</summary>
/// <param name="Removed">Pieces that the new one overlapped (they came out).</param>
public sealed record ShikakuMove(ShikakuDrawKind Kind, ShikakuRect Rect, ShikakuCheck Check, ShikakuPiece? Piece,
    IReadOnlyList<ShikakuPiece> Removed, bool Solved);

/// <summary>
/// State of one play of a puzzle: the pieces placed, hints, an undo list and the clock. Only pieces that fit the rule
/// go in, so a fully covered board is always a correct answer (whatever shapes the child chose). The clock starts with
/// the first input and stops when the board is covered.
/// </summary>
public sealed class ShikakuBoard
{
    private readonly List<ShikakuPiece> _pieces = new();
    private readonly Stack<ShikakuPiece[]> _undo = new();
    private double _seconds;

    public ShikakuBoard(ShikakuPuzzle puzzle) => Puzzle = puzzle;

    public ShikakuPuzzle Puzzle { get; }
    public int Size => Puzzle.Size;
    public IReadOnlyList<ShikakuPiece> Pieces => _pieces;
    public int Hints { get; private set; }
    public bool Started { get; private set; }
    public bool Solved { get; private set; }
    public bool CanUndo => _undo.Count > 0 && !Solved;
    public long TimeMs => (long)Math.Round(_seconds * 1000);

    public int CoveredCount
    {
        get
        {
            var n = 0;
            foreach (var p in _pieces)
                n += p.Rect.Area;
            return n;
        }
    }

    /// <summary>The piece over a cell, or null.</summary>
    public ShikakuPiece? PieceAt(int cell)
    {
        var r = cell / Size;
        var c = cell % Size;
        foreach (var p in _pieces)
            if (p.Rect.Contains(r, c))
                return p;
        return null;
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

    /// <summary>
    /// Tries a drawn rectangle. If it holds one number and has that many cells it goes in, and any piece it overlaps
    /// comes out. Otherwise nothing changes and the check says why (and which number wanted how many cells).
    /// </summary>
    public ShikakuMove Draw(ShikakuRect rect)
    {
        var none = Array.Empty<ShikakuPiece>();
        if (Solved || !ShikakuSolver.InBounds(Size, rect))
            return new ShikakuMove(ShikakuDrawKind.Ignored, rect, default, null, none, Solved);
        Start();
        var check = ShikakuSolver.Check(Puzzle, rect);
        if (check.Fit != ShikakuFit.Fits)
            return new ShikakuMove(ShikakuDrawKind.NotPlaced, rect, check, null, none, false);
        var existing = _pieces.Find(p => p.Rect == rect);
        if (existing is not null)
            return new ShikakuMove(ShikakuDrawKind.Placed, rect, check, existing, none, false); // Same piece again: no change
        Save();
        var piece = new ShikakuPiece(rect, check.ClueCell);
        var removed = Put(piece);
        return new ShikakuMove(ShikakuDrawKind.Placed, rect, check, piece, removed, Solved);
    }

    /// <summary>Takes out the piece over a cell. Null if there is none.</summary>
    public ShikakuPiece? Remove(int cell)
    {
        if (Solved || cell < 0 || cell >= Puzzle.CellCount)
            return null;
        Start();
        var piece = PieceAt(cell);
        if (piece is null)
            return null;
        Save();
        _pieces.Remove(piece);
        return piece;
    }

    /// <summary>"처음부터": every piece comes out (undo brings them back). False if the board was already empty.</summary>
    public bool Clear()
    {
        if (Solved || _pieces.Count == 0)
            return false;
        Start();
        Save();
        _pieces.Clear();
        return true;
    }

    /// <summary>Goes back one step (draw, remove, clear, hint).</summary>
    public bool Undo()
    {
        if (!CanUndo)
            return false;
        var saved = _undo.Pop();
        _pieces.Clear();
        _pieces.AddRange(saved);
        return true;
    }

    /// <summary>
    /// The answer piece a hint would show: one that is not on the board yet. Prefers pieces that overlap nothing
    /// (so nothing the child drew comes out), then the number with the fewest ways to draw it (the easiest to see).
    /// Null if the board is solved.
    /// </summary>
    public ShikakuRect? HintPiece()
    {
        if (Solved)
            return null;
        ShikakuRect? best = null;
        var bestScore = int.MaxValue;
        foreach (var rect in Puzzle.Solution)
        {
            if (_pieces.Exists(p => p.Rect == rect))
                continue;
            var overlaps = _pieces.Exists(p => p.Rect.Overlaps(rect));
            var clue = ShikakuSolver.Check(Puzzle, rect).ClueCell;
            var ways = ShikakuSolver.Candidates(Puzzle, clue).Count;
            var score = (overlaps ? 1000 : 0) + ways * 10 + rect.Area;
            if (score < bestScore)
            {
                best = rect;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Puts the hint piece in (overlapped pieces come out). No penalty: only <see cref="Hints"/> counts up.</summary>
    public ShikakuMove? UseHint()
    {
        if (HintPiece() is not { } rect)
            return null;
        Start();
        Hints++;
        Save();
        var check = ShikakuSolver.Check(Puzzle, rect);
        var piece = new ShikakuPiece(rect, check.ClueCell);
        var removed = Put(piece);
        return new ShikakuMove(ShikakuDrawKind.Placed, rect, check, piece, removed, Solved);
    }

    private List<ShikakuPiece> Put(ShikakuPiece piece)
    {
        var removed = _pieces.FindAll(p => p.Rect.Overlaps(piece.Rect));
        _pieces.RemoveAll(p => p.Rect.Overlaps(piece.Rect));
        _pieces.Add(piece);
        if (CoveredCount == Puzzle.CellCount)
        {
            Solved = true;
            _undo.Clear();
        }
        return removed;
    }

    private void Save() => _undo.Push(_pieces.ToArray());

    /// <summary>The leaderboard row: time decides (<see cref="RankOrder.TimeAsc"/>); <see cref="ScoreEntry.Score"/> keeps the hints for reference.</summary>
    public ScoreEntry Entry(string name, long unixTime) => new(name, Hints, TimeMs, unixTime);
}
