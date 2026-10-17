using System;
using System.Collections.Generic;
using System.Linq;

namespace ToyBox.Core;

/// <summary>Number of fruit kinds. Each level has its own leaderboard (see <see cref="Match3Rules.Variants"/>).</summary>
public enum Match3Level { Easy, Normal }

/// <summary>What a piece does when it goes off. Plain fruit just disappear.</summary>
public enum GemSpecial { None, LineH, LineV, Bomb, Rainbow }

/// <summary>A board cell (x right, y down).</summary>
public readonly record struct GemCell(int X, int Y)
{
    public bool IsNeighbor(GemCell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y) == 1;
}

/// <summary>
/// One piece: a fruit kind (0 apple, 1 strawberry, 2 banana, 3 orange, 4 grape, 5 watermelon) with an optional special,
/// or the rainbow candy (<see cref="Match3Rules.RainbowKind"/>), or nothing (<see cref="Empty"/>).
/// </summary>
public readonly record struct Gem(int Kind, GemSpecial Special = GemSpecial.None)
{
    public static readonly Gem Empty = new(-1);
    public static readonly Gem Rainbow = new(Match3Rules.RainbowKind, GemSpecial.Rainbow);

    public bool IsEmpty => Kind < 0;
    public bool IsRainbow => Special == GemSpecial.Rainbow;

    /// <summary>Can line up with other fruit (the rainbow candy and empty cells never do).</summary>
    public bool IsFruit => Kind >= 0 && !IsRainbow;
}

/// <summary>A piece taken off the board in a step.</summary>
public readonly record struct GemClear(GemCell At, Gem Gem);

/// <summary>A special piece going off (for its own effect: a beam, a ring, the rainbow sparkle).</summary>
public readonly record struct GemBlast(GemCell At, GemSpecial Special, int Kind);

/// <summary>A special piece made by a line of 4, an L/T or a line of 5.</summary>
public readonly record struct GemMade(GemCell At, Gem Gem);

/// <summary>A piece dropping in its column. <see cref="FromY"/> is negative for a new piece coming in from above.</summary>
public readonly record struct GemFall(int X, int FromY, int ToY, Gem Gem)
{
    public bool IsNew => FromY < 0;
}

/// <summary>A piece moved by a shuffle.</summary>
public readonly record struct GemMove(GemCell From, GemCell To);

/// <summary>
/// One beat of a cascade, in play order: <see cref="Cleared"/> pieces disappear (and <see cref="Blasts"/> go off), the
/// <see cref="Made"/> specials appear, then <see cref="Falls"/> drop the pieces above and the new ones.
/// </summary>
public sealed class Match3Step
{
    /// <summary>1 for what the swap itself made, 2 for the first cascade, …</summary>
    public int Chain { get; init; }
    public IReadOnlyList<GemClear> Cleared { get; init; } = Array.Empty<GemClear>();
    public IReadOnlyList<GemBlast> Blasts { get; init; } = Array.Empty<GemBlast>();
    public IReadOnlyList<GemMade> Made { get; init; } = Array.Empty<GemMade>();
    public IReadOnlyList<GemFall> Falls { get; init; } = Array.Empty<GemFall>();

    /// <summary>Fruit collected in this step: every piece cleared plus every special made (the fruit that became it).</summary>
    public int Collected => Cleared.Count + Made.Count;
}

/// <summary>What one swap did. Nothing changes on the board when <see cref="Valid"/> is false.</summary>
public sealed class Match3Swap
{
    public bool Valid { get; init; }
    public GemCell A { get; init; }
    public GemCell B { get; init; }
    public IReadOnlyList<Match3Step> Steps { get; init; } = Array.Empty<Match3Step>();

    /// <summary>Set when the board had no move left after the cascade and was shuffled (applied after the steps).</summary>
    public IReadOnlyList<GemMove>? Shuffle { get; init; }

    public int Collected => Steps.Sum(s => s.Collected);
    public int Chains => Steps.Count;
}

/// <summary>과일 맞추기 rules that do not need the engine (docs/games/11-match3.md).</summary>
public static class Match3Rules
{
    /// <summary>Leaderboard variants, easiest first: fewer kinds make lines more often, so scores are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
    };

    public static string VariantId(Match3Level level) => Variants[(int)level].Id;

    /// <summary>Fruit art names by kind.</summary>
    public static readonly string[] KindNames = { "apple", "strawberry", "banana", "orange", "grape", "watermelon" };

    /// <summary>쉬움 5 kinds (no watermelon), 보통 all 6.</summary>
    public static int Kinds(Match3Level level) => level == Match3Level.Easy ? 5 : 6;

    /// <summary>7x7 on both levels (8x8 makes the cells too small for a child's finger at 1280x720).</summary>
    public const int Size = 7;

    public const float PlaySeconds = 60f;

    /// <summary>Kind value of the rainbow candy (matches nothing).</summary>
    public const int RainbowKind = 100;

    /// <summary>Seconds without a move before the hint wiggles a possible swap.</summary>
    public const float HintDelay = 4f;

    /// <summary>A drag longer than this (in cells) swaps toward the neighbor.</summary>
    public const float DragCells = 0.35f;

    /// <summary>The buddy cheers every this many fruit.</summary>
    public const int CheerEvery = 50;

    /// <summary>The cascade word for a chain ("또!", "또또!", "최고!"); empty for the swap's own match.</summary>
    public static string ChainWord(int chain) => chain switch
    {
        <= 1 => "",
        2 => "또!",
        3 => "또또!",
        _ => "최고!",
    };

    public static string FormatRecord(int fruit) => $"과일 {fruit}개";
}

/// <summary>
/// The match-3 board: swaps, line finding (3, 4, 5, L/T), specials, falling and refilling (with an injected random),
/// moves left and shuffling. A swap is resolved all at once and returned as steps for the view to play back.
/// </summary>
public sealed class Match3Board
{
    private readonly Gem[,] _cells;
    private readonly Random _rng;

    public int Size { get; }
    public int Kinds { get; }

    public Match3Board(int size, int kinds, Random rng)
    {
        Size = size;
        Kinds = kinds;
        _rng = rng;
        _cells = new Gem[size, size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                _cells[x, y] = Gem.Empty;
    }

    /// <summary>A fresh board with no line already made and at least one possible swap.</summary>
    public static Match3Board Create(Match3Level level, Random rng)
    {
        var board = new Match3Board(Match3Rules.Size, Match3Rules.Kinds(level), rng);
        board.FillFresh();
        return board;
    }

    /// <summary>A board from rows of kinds, top to bottom (for tests). Use <see cref="this[int,int]"/> to add specials.</summary>
    public static Match3Board FromRows(int kinds, Random rng, params int[][] rows)
    {
        var board = new Match3Board(rows.Length, kinds, rng);
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows.Length; x++)
                board._cells[x, y] = rows[y][x] < 0 ? Gem.Empty : new Gem(rows[y][x]);
        return board;
    }

    public Gem this[int x, int y]
    {
        get => _cells[x, y];
        set => _cells[x, y] = value;
    }

    public Gem this[GemCell c]
    {
        get => _cells[c.X, c.Y];
        set => _cells[c.X, c.Y] = value;
    }

    public bool Inside(GemCell c) => c.X >= 0 && c.Y >= 0 && c.X < Size && c.Y < Size;

    public Match3Board Clone(Random rng)
    {
        var copy = new Match3Board(Size, Kinds, rng);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    // ---- Fresh board ----

    /// <summary>Random fruit with no three in a line (each cell avoids the two to its left and above), retried until a move exists.</summary>
    public void FillFresh()
    {
        for (var attempt = 0; ; attempt++)
        {
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    int kind;
                    do
                        kind = _rng.Next(Kinds);
                    while ((x >= 2 && _cells[x - 1, y].Kind == kind && _cells[x - 2, y].Kind == kind)
                           || (y >= 2 && _cells[x, y - 1].Kind == kind && _cells[x, y - 2].Kind == kind));
                    _cells[x, y] = new Gem(kind);
                }
            if (HasMove() || attempt > 50)
                return;
        }
    }

    // ---- Lines ----

    private sealed class Group
    {
        public readonly HashSet<GemCell> Cells = new();
        public int Kind;
        public int LongestH;
        public int LongestV;
        public readonly List<GemCell> Crossings = new();
        public List<GemCell> LongestRun = new();
    }

    /// <summary>True when some line of 3 or more same fruit is on the board.</summary>
    public bool HasLine() => FindGroups().Count > 0;

    /// <summary>
    /// Lines of 3+ same fruit, joined into groups where a row and a column line share a cell (an L or a T).
    /// </summary>
    private List<Group> FindGroups()
    {
        var runs = new List<(List<GemCell> Cells, bool Horizontal)>();
        for (var y = 0; y < Size; y++)
            CollectRuns(runs, true, y);
        for (var x = 0; x < Size; x++)
            CollectRuns(runs, false, x);

        // Union runs that share a cell
        var parent = Enumerable.Range(0, runs.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var owner = new Dictionary<GemCell, int>();
        var crossings = new List<GemCell>();
        for (var i = 0; i < runs.Count; i++)
            foreach (var c in runs[i].Cells)
            {
                if (owner.TryGetValue(c, out var other))
                {
                    parent[Find(i)] = Find(other);
                    crossings.Add(c);
                }
                else
                {
                    owner[c] = i;
                }
            }

        var groups = new Dictionary<int, Group>();
        for (var i = 0; i < runs.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var g))
                groups[root] = g = new Group { Kind = this[runs[i].Cells[0]].Kind };
            foreach (var c in runs[i].Cells)
                g.Cells.Add(c);
            var len = runs[i].Cells.Count;
            if (runs[i].Horizontal)
                g.LongestH = Math.Max(g.LongestH, len);
            else
                g.LongestV = Math.Max(g.LongestV, len);
            if (len > g.LongestRun.Count)
                g.LongestRun = runs[i].Cells;
        }
        foreach (var c in crossings)
            groups[Find(owner[c])].Crossings.Add(c);
        return groups.Values.ToList();
    }

    private void CollectRuns(List<(List<GemCell>, bool)> runs, bool horizontal, int line)
    {
        var start = 0;
        for (var i = 1; i <= Size; i++)
        {
            var a = horizontal ? new GemCell(start, line) : new GemCell(line, start);
            var b = horizontal ? new GemCell(i, line) : new GemCell(line, i);
            if (i < Size && this[a].IsFruit && this[b].Kind == this[a].Kind)
                continue;
            if (i - start >= 3 && this[a].IsFruit)
            {
                var cells = new List<GemCell>();
                for (var j = start; j < i; j++)
                    cells.Add(horizontal ? new GemCell(j, line) : new GemCell(line, j));
                runs.Add((cells, horizontal));
            }
            start = i;
        }
    }

    // ---- Moves ----

    /// <summary>A swap of two neighbors works when either is the rainbow candy or it makes a line.</summary>
    public bool CanSwap(GemCell a, GemCell b)
    {
        if (!Inside(a) || !Inside(b) || !a.IsNeighbor(b) || this[a].IsEmpty || this[b].IsEmpty)
            return false;
        if (this[a].IsRainbow || this[b].IsRainbow)
            return true;
        if (this[a].Kind == this[b].Kind)
            return false;
        Exchange(a, b);
        var line = LineThrough(a) || LineThrough(b);
        Exchange(a, b);
        return line;
    }

    private bool LineThrough(GemCell c)
    {
        var kind = this[c].Kind;
        if (!this[c].IsFruit)
            return false;
        int Count(int dx, int dy)
        {
            var n = 0;
            for (var p = new GemCell(c.X + dx, c.Y + dy); Inside(p) && this[p].IsFruit && this[p].Kind == kind; p = new GemCell(p.X + dx, p.Y + dy))
                n++;
            return n;
        }
        return Count(-1, 0) + Count(1, 0) >= 2 || Count(0, -1) + Count(0, 1) >= 2;
    }

    private void Exchange(GemCell a, GemCell b) => (_cells[a.X, a.Y], _cells[b.X, b.Y]) = (_cells[b.X, b.Y], _cells[a.X, a.Y]);

    /// <summary>Every swap that works (each pair once, right and down neighbors).</summary>
    public List<(GemCell A, GemCell B)> Moves()
    {
        var moves = new List<(GemCell, GemCell)>();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var a = new GemCell(x, y);
                if (x + 1 < Size && CanSwap(a, new GemCell(x + 1, y)))
                    moves.Add((a, new GemCell(x + 1, y)));
                if (y + 1 < Size && CanSwap(a, new GemCell(x, y + 1)))
                    moves.Add((a, new GemCell(x, y + 1)));
            }
        return moves;
    }

    public bool HasMove() => FirstMove() is not null;

    /// <summary>The first working swap in reading order (the hint), or null.</summary>
    public (GemCell A, GemCell B)? FirstMove()
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var a = new GemCell(x, y);
                if (x + 1 < Size && CanSwap(a, new GemCell(x + 1, y)))
                    return (a, new GemCell(x + 1, y));
                if (y + 1 < Size && CanSwap(a, new GemCell(x, y + 1)))
                    return (a, new GemCell(x, y + 1));
            }
        return null;
    }

    /// <summary>The swap that collects the most fruit right away (the first step only, so new random fruit do not count).</summary>
    public (GemCell A, GemCell B)? BestMove()
    {
        (GemCell, GemCell)? best = null;
        var bestCount = -1;
        foreach (var (a, b) in Moves())
        {
            var trial = Clone(new Random(0));
            var result = trial.Swap(a, b, maxSteps: 1);
            var count = result.Steps.Count > 0 ? result.Steps[0].Collected : 0;
            if (count > bestCount)
            {
                bestCount = count;
                best = (a, b);
            }
        }
        return best;
    }

    // ---- Swap and cascade ----

    /// <summary>
    /// Swaps two neighbors and resolves everything: the lines (or the rainbow candy), specials going off, falling,
    /// refilling and every cascade until the board is still. A swap that makes no line changes nothing
    /// (<see cref="Match3Swap.Valid"/> false). If no move is left at the end, the board is shuffled.
    /// </summary>
    public Match3Swap Swap(GemCell a, GemCell b, int maxSteps = int.MaxValue)
    {
        if (!CanSwap(a, b))
            return new Match3Swap { Valid = false, A = a, B = b };

        Exchange(a, b);
        var steps = new List<Match3Step>();

        // The rainbow candy: everything of the other piece's kind (two rainbows: the whole board)
        var rainbowAt = this[a].IsRainbow ? a : this[b].IsRainbow ? b : (GemCell?)null;
        if (rainbowAt is { } r)
        {
            var other = r == a ? b : a;
            var targets = new HashSet<GemCell> { r };
            var kind = this[other].Kind;
            if (this[other].IsRainbow)
                kind = -1;
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                    if (kind < 0 ? !_cells[x, y].IsEmpty : _cells[x, y].IsFruit && _cells[x, y].Kind == kind)
                        targets.Add(new GemCell(x, y));
            var blasts = new List<GemBlast> { new(r, GemSpecial.Rainbow, kind) };
            steps.Add(Resolve(1, targets, new List<GemMade>(), blasts, alreadyFired: new HashSet<GemCell> { r }));
        }

        while (steps.Count < maxSteps)
        {
            var groups = FindGroups();
            if (groups.Count == 0)
                break;
            var made = new List<GemMade>();
            var cleared = new HashSet<GemCell>();
            foreach (var g in groups)
            {
                foreach (var c in g.Cells)
                    cleared.Add(c);
                if (SpecialFor(g) is { } special && SpotFor(g, steps.Count == 0 ? new[] { a, b } : null) is { } spot)
                    made.Add(new GemMade(spot, special == GemSpecial.Rainbow ? Gem.Rainbow : new Gem(g.Kind, special)));
            }
            steps.Add(Resolve(steps.Count + 1, cleared, made, new List<GemBlast>(), new HashSet<GemCell>()));
        }

        IReadOnlyList<GemMove>? shuffle = null;
        if (maxSteps == int.MaxValue && !HasMove())
            shuffle = Shuffle();
        return new Match3Swap { Valid = true, A = a, B = b, Steps = steps, Shuffle = shuffle };
    }

    /// <summary>4 in a line → a striped fruit along that line, L/T → a ring fruit, 5 in a line → the rainbow candy.</summary>
    private static GemSpecial? SpecialFor(Group g)
    {
        if (g.LongestH >= 5 || g.LongestV >= 5)
            return GemSpecial.Rainbow;
        if (g.LongestH >= 3 && g.LongestV >= 3)
            return GemSpecial.Bomb;
        if (g.LongestH == 4)
            return GemSpecial.LineH;
        if (g.LongestV == 4)
            return GemSpecial.LineV;
        return null;
    }

    /// <summary>
    /// Where a special appears: the swapped cell when it is part of the group, else the corner of an L/T, else the
    /// middle of the longest line. A cell already holding a special is skipped (that one goes off instead).
    /// </summary>
    private GemCell? SpotFor(Group g, GemCell[]? swapped)
    {
        var candidates = new List<GemCell>();
        if (swapped is not null)
            candidates.AddRange(swapped.Where(g.Cells.Contains));
        candidates.AddRange(g.Crossings);
        var run = g.LongestRun;
        candidates.Add(run[(run.Count - 1) / 2]);
        candidates.AddRange(run);
        candidates.AddRange(g.Cells);
        foreach (var c in candidates)
            if (this[c].Special == GemSpecial.None)
                return c;
        return null;
    }

    /// <summary>
    /// Clears <paramref name="targets"/> (specials among them go off and add their area), puts the made specials down,
    /// drops and refills. Returns the step.
    /// </summary>
    private Match3Step Resolve(int chain, HashSet<GemCell> targets, List<GemMade> made, List<GemBlast> blasts, HashSet<GemCell> alreadyFired)
    {
        var madeAt = made.Select(m => m.At).ToHashSet();
        var cleared = new HashSet<GemCell>();
        var queue = new Queue<GemCell>(targets);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (madeAt.Contains(c) || this[c].IsEmpty || !cleared.Add(c))
                continue;
            var gem = this[c];
            if (gem.Special == GemSpecial.None || alreadyFired.Contains(c))
                continue;
            alreadyFired.Add(c);
            var area = BlastArea(c, gem, out var kind);
            blasts.Add(new GemBlast(c, gem.Special, kind));
            foreach (var t in area)
                queue.Enqueue(t);
        }

        var clears = cleared.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => new GemClear(c, this[c])).ToList();
        foreach (var c in cleared)
            this[c] = Gem.Empty;
        foreach (var m in made)
            this[m.At] = m.Gem;
        return new Match3Step { Chain = chain, Cleared = clears, Blasts = blasts, Made = made, Falls = Collapse() };
    }

    /// <summary>The cells a special clears when it goes off.</summary>
    private List<GemCell> BlastArea(GemCell c, Gem gem, out int kind)
    {
        kind = gem.Kind;
        var area = new List<GemCell>();
        switch (gem.Special)
        {
            case GemSpecial.LineH:
                for (var x = 0; x < Size; x++)
                    area.Add(new GemCell(x, c.Y));
                break;
            case GemSpecial.LineV:
                for (var y = 0; y < Size; y++)
                    area.Add(new GemCell(c.X, y));
                break;
            case GemSpecial.Bomb:
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        if (Inside(new GemCell(c.X + dx, c.Y + dy)))
                            area.Add(new GemCell(c.X + dx, c.Y + dy));
                break;
            case GemSpecial.Rainbow:
                // Set off by another blast: takes the most common fruit on the board
                kind = MostCommonKind();
                for (var y = 0; y < Size; y++)
                    for (var x = 0; x < Size; x++)
                        if (_cells[x, y].IsFruit && _cells[x, y].Kind == kind)
                            area.Add(new GemCell(x, y));
                break;
        }
        return area;
    }

    private int MostCommonKind()
    {
        var counts = new int[Kinds];
        foreach (var g in _cells)
            if (g.IsFruit && g.Kind < Kinds)
                counts[g.Kind]++;
        return Array.IndexOf(counts, counts.Max());
    }

    /// <summary>Drops pieces into the gaps below them and fills each column from the top with new random fruit.</summary>
    private List<GemFall> Collapse()
    {
        var falls = new List<GemFall>();
        for (var x = 0; x < Size; x++)
        {
            var write = Size - 1;
            for (var y = Size - 1; y >= 0; y--)
            {
                if (_cells[x, y].IsEmpty)
                    continue;
                if (y != write)
                {
                    falls.Add(new GemFall(x, y, write, _cells[x, y]));
                    _cells[x, write] = _cells[x, y];
                    _cells[x, y] = Gem.Empty;
                }
                write--;
            }
            var missing = write + 1;
            for (var y = write; y >= 0; y--)
            {
                var gem = new Gem(_rng.Next(Kinds));
                _cells[x, y] = gem;
                falls.Add(new GemFall(x, y - missing, y, gem));
            }
        }
        return falls;
    }

    // ---- Shuffle ----

    /// <summary>
    /// Mixes the pieces already on the board (specials stay with their fruit) until no line is made and a move exists.
    /// Falls back to fresh fruit in the unlikely case no arrangement is found. Returns where each piece went.
    /// </summary>
    public IReadOnlyList<GemMove> Shuffle()
    {
        var cells = new List<GemCell>();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                cells.Add(new GemCell(x, y));
        var original = cells.Select(c => this[c]).ToArray();

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var order = Enumerable.Range(0, cells.Count).ToArray();
            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (var i = 0; i < cells.Count; i++)
                this[cells[i]] = original[order[i]];
            if (!HasLine() && HasMove())
                return cells.Select((to, i) => new GemMove(cells[order[i]], to)).ToList();
        }

        // Fresh fruit: every piece "moves" onto itself and the view rebuilds from the board
        FillFresh();
        return cells.Select(c => new GemMove(c, c)).ToList();
    }
}
