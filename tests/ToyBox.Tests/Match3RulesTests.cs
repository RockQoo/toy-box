using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class Match3RulesTests
{
    private static GemCell C(int x, int y) => new(x, y);

    /// <summary>
    /// A 7x7 board of 5 kinds with no move at all: kind = (x + 3y) mod 5 puts each kind only every 5 cells in every row
    /// and column, so no swap can line up 3. Tests change a few cells to set up exactly one situation.
    /// </summary>
    private static Match3Board Dead(int seed = 1)
    {
        var board = new Match3Board(7, 5, new Random(seed));
        for (var y = 0; y < 7; y++)
            for (var x = 0; x < 7; x++)
                board[x, y] = new Gem((x + 3 * y) % 5);
        return board;
    }

    private static Gem[] Snapshot(Match3Board board)
    {
        var cells = new Gem[board.Size * board.Size];
        for (var y = 0; y < board.Size; y++)
            for (var x = 0; x < board.Size; x++)
                cells[y * board.Size + x] = board[x, y];
        return cells;
    }

    private static void AssertSettled(Match3Board board)
    {
        for (var y = 0; y < board.Size; y++)
            for (var x = 0; x < board.Size; x++)
                Assert.False(board[x, y].IsEmpty, $"empty cell ({x},{y})");
        Assert.False(board.HasLine(), "a finished line was left on the board");
        Assert.True(board.HasMove(), "no move left on the board");
    }

    [Fact]
    public void VariantsKindsAndSizeFollowTheDesign()
    {
        Assert.Equal(new[] { "easy", "normal" }, Match3Rules.Variants.Select(v => v.Id));
        Assert.Equal(new[] { "쉬움", "보통" }, Match3Rules.Variants.Select(v => v.Label));
        Assert.Equal(5, Match3Rules.Kinds(Match3Level.Easy));
        Assert.Equal(6, Match3Rules.Kinds(Match3Level.Normal));
        Assert.Equal(7, Match3Rules.Size);
        Assert.Equal(60f, Match3Rules.PlaySeconds);
        Assert.Equal("watermelon", Match3Rules.KindNames[5]); // the one 쉬움 leaves out
        Assert.Equal("normal", Match3Rules.VariantId(Match3Level.Normal));
    }

    [Fact]
    public void ChainWordsGrowWithTheCascade()
    {
        Assert.Equal("", Match3Rules.ChainWord(1));
        Assert.Equal("또!", Match3Rules.ChainWord(2));
        Assert.Equal("또또!", Match3Rules.ChainWord(3));
        Assert.Equal("최고!", Match3Rules.ChainWord(4));
        Assert.Equal("최고!", Match3Rules.ChainWord(9));
    }

    [Fact]
    public void FreshBoardsHaveNoLineAndAtLeastOneMove()
    {
        foreach (Match3Level level in Enum.GetValues(typeof(Match3Level)))
            for (var seed = 0; seed < 300; seed++)
            {
                var board = Match3Board.Create(level, new Random(seed));
                AssertSettled(board);
                for (var y = 0; y < board.Size; y++)
                    for (var x = 0; x < board.Size; x++)
                    {
                        Assert.InRange(board[x, y].Kind, 0, Match3Rules.Kinds(level) - 1);
                        Assert.Equal(GemSpecial.None, board[x, y].Special);
                    }
            }
    }

    [Fact]
    public void TheTestBoardReallyHasNoMove()
    {
        var board = Dead();
        Assert.False(board.HasLine());
        Assert.False(board.HasMove());
        Assert.Empty(board.Moves());
    }

    [Fact]
    public void OnlyNeighborsThatMakeALineSwap()
    {
        var board = Dead();
        var before = Snapshot(board);
        Assert.False(board.Swap(C(0, 0), C(1, 0)).Valid);  // neighbors, but no line
        Assert.False(board.Swap(C(0, 0), C(2, 0)).Valid);  // not neighbors
        Assert.False(board.Swap(C(0, 0), C(1, 1)).Valid);  // diagonal
        Assert.False(board.Swap(C(6, 6), C(7, 6)).Valid);  // off the board
        Assert.Equal(before, Snapshot(board));             // a failed swap changes nothing
    }

    /// <summary>Row 0 gets 4 4 _ and a 4 under the gap: swapping it up makes 4 4 4.</summary>
    private static Match3Board ThreeInRow()
    {
        var board = Dead();
        board[0, 0] = new Gem(4);
        board[1, 0] = new Gem(4);
        board[2, 1] = new Gem(4);
        return board;
    }

    [Fact]
    public void ThreeInALineDisappearAndTheColumnsRefill()
    {
        var board = ThreeInRow();
        Assert.False(board.HasLine());
        Assert.True(board.CanSwap(C(2, 1), C(2, 0)));
        var result = board.Swap(C(2, 1), C(2, 0));

        Assert.True(result.Valid);
        var first = result.Steps[0];
        Assert.Equal(1, first.Chain);
        Assert.Equal(new[] { C(0, 0), C(1, 0), C(2, 0) }, first.Cleared.Select(c => c.At));
        Assert.All(first.Cleared, c => Assert.Equal(4, c.Gem.Kind));
        Assert.Empty(first.Made);
        Assert.Equal(3, first.Collected);
        // The top row was cleared: three new fruit come in from just above the board
        Assert.Equal(3, first.Falls.Count);
        Assert.All(first.Falls, f => Assert.Equal((-1, 0), (f.FromY, f.ToY)));
        AssertSettled(board);
    }

    [Fact]
    public void FourInALineMakeAStripedFruitWhereTheSwapLanded()
    {
        var board = Dead();
        board[1, 0] = new Gem(4);
        board[3, 0] = new Gem(4); // (4,0) is a 4 already
        board[2, 1] = new Gem(4);
        Assert.False(board.HasLine());
        var result = board.Swap(C(2, 0), C(2, 1));

        var first = result.Steps[0];
        Assert.Equal(new GemMade(C(2, 0), new Gem(4, GemSpecial.LineH)), Assert.Single(first.Made));
        Assert.Equal(3, first.Cleared.Count);
        Assert.Equal(4, first.Collected);
        Assert.DoesNotContain(C(2, 0), first.Cleared.Select(c => c.At));

        var vertical = Dead();
        vertical[0, 1] = new Gem(2);
        vertical[0, 3] = new Gem(2);
        vertical[0, 4] = new Gem(2);
        vertical[1, 2] = new Gem(2);
        Assert.False(vertical.HasLine());
        var down = vertical.Swap(C(1, 2), C(0, 2));
        Assert.Equal(GemSpecial.LineV, Assert.Single(down.Steps[0].Made).Gem.Special);
    }

    [Fact]
    public void AnLOrTMakesARingFruitAtTheCorner()
    {
        var board = Dead();
        board[0, 2] = new Gem(4);
        board[1, 2] = new Gem(4);
        board[2, 0] = new Gem(4);
        board[2, 1] = new Gem(4); // (3,2) is a 4 already
        Assert.False(board.HasLine());
        var result = board.Swap(C(3, 2), C(2, 2));

        var first = result.Steps[0];
        Assert.Equal(new GemMade(C(2, 2), new Gem(4, GemSpecial.Bomb)), Assert.Single(first.Made));
        Assert.Equal(new[] { C(2, 0), C(2, 1), C(0, 2), C(1, 2) }, first.Cleared.Select(c => c.At));
        Assert.Equal(5, first.Collected);
    }

    [Fact]
    public void FiveInALineMakeTheRainbowCandy()
    {
        var board = Dead();
        board[0, 0] = new Gem(4);
        board[1, 0] = new Gem(4);
        board[3, 0] = new Gem(4);
        board[2, 1] = new Gem(4);
        Assert.False(board.HasLine());
        var result = board.Swap(C(2, 0), C(2, 1));

        var made = Assert.Single(result.Steps[0].Made);
        Assert.Equal(C(2, 0), made.At);
        Assert.True(made.Gem.IsRainbow);
        Assert.Equal(5, result.Steps[0].Collected);
    }

    [Fact]
    public void AStripedFruitInALineClearsItsWholeRow()
    {
        var board = ThreeInRow();
        board[0, 0] = new Gem(4, GemSpecial.LineH);
        var first = board.Swap(C(2, 1), C(2, 0)).Steps[0];

        Assert.Equal(Enumerable.Range(0, 7).Select(x => C(x, 0)), first.Cleared.Select(c => c.At));
        Assert.Equal(new GemBlast(C(0, 0), GemSpecial.LineH, 4), Assert.Single(first.Blasts));
        Assert.Equal(7, first.Collected);
    }

    [Fact]
    public void ARingFruitClearsTheThreeByThreeAroundIt()
    {
        var board = Dead();
        board[1, 3] = new Gem(1, GemSpecial.Bomb); // (2,3) is a 1 already
        board[3, 4] = new Gem(1);
        Assert.False(board.HasLine());
        var first = board.Swap(C(3, 4), C(3, 3)).Steps[0];

        var cleared = first.Cleared.Select(c => c.At).ToHashSet();
        for (var y = 2; y <= 4; y++)
            for (var x = 0; x <= 2; x++)
                Assert.Contains(C(x, y), cleared);
        Assert.Contains(C(3, 3), cleared);
        Assert.Equal(10, cleared.Count);
        Assert.Equal(GemSpecial.Bomb, Assert.Single(first.Blasts).Special);
    }

    [Fact]
    public void TheRainbowCandyTakesEveryFruitOfTheSwappedKind()
    {
        var board = Dead();
        board[3, 3] = Gem.Rainbow;
        var kind = board[4, 3].Kind;
        var count = 0;
        for (var y = 0; y < 7; y++)
            for (var x = 0; x < 7; x++)
                if (board[x, y].Kind == kind)
                    count++;
        Assert.True(board.CanSwap(C(3, 3), C(4, 3))); // the rainbow candy swaps with anything
        var first = board.Swap(C(3, 3), C(4, 3)).Steps[0];

        Assert.Equal(count + 1, first.Cleared.Count);
        Assert.Single(first.Cleared, c => c.Gem.IsRainbow);
        Assert.All(first.Cleared.Where(c => !c.Gem.IsRainbow), c => Assert.Equal(kind, c.Gem.Kind));
        Assert.Equal(GemSpecial.Rainbow, first.Blasts[0].Special);
        Assert.Equal(kind, first.Blasts[0].Kind);
    }

    [Fact]
    public void ASpecialCaughtInABlastGoesOffToo()
    {
        var board = ThreeInRow();
        board[0, 0] = new Gem(4, GemSpecial.LineH);
        board[5, 0] = new Gem(0, GemSpecial.LineV); // in row 0: the row blast sets it off
        var first = board.Swap(C(2, 1), C(2, 0)).Steps[0];

        var cleared = first.Cleared.Select(c => c.At).ToHashSet();
        Assert.Equal(7 + 6, cleared.Count); // row 0 and the rest of column 5
        Assert.All(Enumerable.Range(0, 7), y => Assert.Contains(C(5, y), cleared));
        Assert.Equal(new[] { GemSpecial.LineH, GemSpecial.LineV }, first.Blasts.Select(b => b.Special));
    }

    [Fact]
    public void FallingFruitMakeACascadeAndTheBoardIsCheckedAgainAfterIt()
    {
        // Swapping (2,5) down makes 0 0 0 on the bottom row. Columns 0-2 drop by one, which brings 4 4 next to the 4
        // at (3,6): a second line (the cascade).
        var board = Dead();
        board[0, 6] = new Gem(0);
        board[1, 6] = new Gem(0);
        board[2, 6] = new Gem(4);
        board[2, 5] = new Gem(0);
        board[1, 5] = new Gem(4);
        board[3, 6] = new Gem(4);
        Assert.False(board.HasLine());
        var result = board.Swap(C(2, 5), C(2, 6));

        Assert.True(result.Chains >= 2);
        Assert.Equal(new[] { C(0, 6), C(1, 6), C(2, 6) }, result.Steps[0].Cleared.Select(c => c.At));
        Assert.Contains(new GemFall(1, 5, 6, new Gem(4)), result.Steps[0].Falls);
        var second = result.Steps[1];
        Assert.Equal(2, second.Chain);
        foreach (var c in new[] { C(1, 6), C(2, 6), C(3, 6) })
            Assert.Contains(c, second.Cleared.Select(g => g.At));
        Assert.Equal(result.Steps.Sum(s => s.Collected), result.Collected);
        // After the last cascade: full, no line left, and a move to make (shuffled if needed)
        AssertSettled(board);
    }

    [Fact]
    public void ABoardWithNoMoveIsShuffledIntoAPlayableOne()
    {
        var board = Dead(7);
        board[3, 3] = new Gem(2, GemSpecial.LineH);
        var before = Snapshot(board).OrderBy(g => g.Kind).ThenBy(g => g.Special).ToArray();
        var moves = board.Shuffle();

        AssertSettled(board);
        Assert.Equal(before, Snapshot(board).OrderBy(g => g.Kind).ThenBy(g => g.Special).ToArray()); // same pieces
        Assert.Equal(49, moves.Count);
        Assert.Equal(49, moves.Select(m => m.To).Distinct().Count());
        Assert.Equal(49, moves.Select(m => m.From).Distinct().Count());
    }

    [Fact]
    public void ASwapThatLeavesNoMoveShufflesAtTheEnd()
    {
        // The plain 3 at the top, with the three new fruit chosen: some choices leave the board without a move once
        // the cascade is over, and then the board must be shuffled into a playable one.
        var shuffledOnce = false;
        for (var t = 0; t < 125 && !shuffledOnce; t++)
        {
            var board = ThreeInRow();
            var rng = new ScriptedRandom(t % 5, t / 5 % 5, t / 25);
            var replay = new Match3Board(7, 5, rng);
            for (var y = 0; y < 7; y++)
                for (var x = 0; x < 7; x++)
                    replay[x, y] = board[x, y];
            var result = replay.Swap(C(2, 1), C(2, 0));
            Assert.Equal(new[] { t % 5, t / 5 % 5, t / 25 }, result.Steps[0].Falls.Select(f => f.Gem.Kind));
            AssertSettled(replay);
            if (result.Shuffle is not null)
            {
                shuffledOnce = true;
                Assert.Single(result.Steps);
            }
        }
        Assert.True(shuffledOnce);
    }

    /// <summary>Gives the scripted values first, then ordinary random numbers.</summary>
    private sealed class ScriptedRandom(params int[] values) : Random(5)
    {
        private int _next;

        public override int Next(int maxValue) =>
            _next < values.Length ? values[_next++] % maxValue : base.Next(maxValue);
    }

    [Fact]
    public void EverySwapInLongGamesEndsFullWithoutLinesAndWithAMove()
    {
        foreach (Match3Level level in Enum.GetValues(typeof(Match3Level)))
            for (var seed = 0; seed < 40; seed++)
            {
                var rng = new Random(seed);
                var board = Match3Board.Create(level, rng);
                var pick = new Random(seed + 1000);
                for (var turn = 0; turn < 60; turn++)
                {
                    var moves = board.Moves();
                    Assert.NotEmpty(moves);
                    var (a, b) = moves[pick.Next(moves.Count)];
                    var result = board.Swap(a, b);
                    Assert.True(result.Valid);
                    Assert.True(result.Collected >= 3);
                    Assert.All(result.Steps, (s, i) => Assert.Equal(i + 1, s.Chain));
                    AssertSettled(board);
                }
            }
    }

    [Fact]
    public void TheBestMoveCollectsTheMostAtOnce()
    {
        var board = Dead();
        // A plain 3 at the top, and a 5 lower down
        board[0, 0] = new Gem(4);
        board[1, 0] = new Gem(4);
        board[2, 1] = new Gem(4);
        board[0, 4] = new Gem(0);
        board[1, 4] = new Gem(0);
        board[3, 4] = new Gem(0);
        board[4, 4] = new Gem(0);
        board[2, 5] = new Gem(0);
        board[2, 6] = new Gem(3); // keeps column 2 from making a second big line
        Assert.False(board.HasLine());
        var best = board.BestMove();
        Assert.NotNull(best);
        Assert.Equal(new[] { C(2, 4), C(2, 5) }.ToHashSet(), new[] { best.Value.A, best.Value.B }.ToHashSet());
        Assert.NotNull(board.FirstMove());
    }
}
