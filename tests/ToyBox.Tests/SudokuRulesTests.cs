using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class SudokuRulesTests
{
    public static IEnumerable<object[]> AllPuzzles()
    {
        foreach (var level in Enum.GetValues<SudokuLevel>())
            for (var i = 0; i < SudokuRules.PuzzleCount(level); i++)
                yield return new object[] { level, i };
    }

    [Fact]
    public void VariantsSizesAndBlanksFollowGdd()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, SudokuRules.Variants.Select(v => v.Id));
        Assert.Equal(new[] { "쉬움", "보통", "어려움" }, SudokuRules.Variants.Select(v => v.Label));
        Assert.Equal("hard", SudokuRules.VariantId(SudokuLevel.Hard));
        Assert.Equal(4, SudokuRules.Size(SudokuLevel.Easy));
        Assert.Equal(6, SudokuRules.Size(SudokuLevel.Normal));
        Assert.Equal(6, SudokuRules.Size(SudokuLevel.Hard));
        Assert.Equal(6, SudokuRules.Blanks(SudokuLevel.Easy));
        Assert.Equal(14, SudokuRules.Blanks(SudokuLevel.Normal));
        Assert.Equal(20, SudokuRules.Blanks(SudokuLevel.Hard));
        foreach (var level in Enum.GetValues<SudokuLevel>())
            Assert.Equal(10, SudokuRules.PuzzleCount(level));
        Assert.Equal(3, SudokuRules.Wrap(SudokuLevel.Easy, 13));
        Assert.Equal(9, SudokuRules.Wrap(SudokuLevel.Easy, -1));
        Assert.Equal("42.3초", SudokuRules.FormatRecord(42_300));
    }

    [Fact]
    public void BoxesAre2x2And3WideBy2Tall()
    {
        var four = SudokuShape.For(4);
        Assert.Equal((2, 2), (four.BoxWidth, four.BoxHeight));
        var six = SudokuShape.For(6);
        Assert.Equal((3, 2), (six.BoxWidth, six.BoxHeight));
        Assert.Equal(18, six.Units.Count);
        // Box 1 of a 6 x 6 board: rows 0-1, columns 3-5
        Assert.Equal(new[] { 3, 4, 5, 9, 10, 11 }, six.Units[12 + 1].Cells);
        Assert.Equal(1, six.Box(six.Cell(1, 4)));
        Assert.Equal(5, six.Box(six.Cell(5, 5)));
        Assert.Equal(2, six.Box(six.Cell(2, 0)));
        // Peers: 5 in the row + 5 in the column + 2 more in the box
        Assert.Equal(12, six.Peers(0).Count);
        Assert.Equal(7, four.Peers(0).Count);
    }

    [Theory]
    [MemberData(nameof(AllPuzzles))]
    public void EveryPuzzleHasOneAnswerAndCanBeSolvedStepByStep(SudokuLevel level, int index)
    {
        var puzzle = SudokuRules.Get(level, index);
        var shape = puzzle.Shape;
        Assert.Equal(SudokuRules.Size(level), shape.Size);
        Assert.True(SudokuSolver.IsValidSolution(shape, puzzle.Solution), $"{level} #{index}: answer breaks a rule");
        Assert.Equal(SudokuRules.Blanks(level), puzzle.BlankCount);
        for (var i = 0; i < shape.CellCount; i++)
            Assert.True(puzzle.Givens[i] == 0 || puzzle.Givens[i] == puzzle.Solution[i]);
        Assert.Equal(1, SudokuSolver.CountSolutions(shape, puzzle.Givens, 10));
        Assert.True(SudokuSolver.SolvableStepByStep(shape, puzzle.Givens), $"{level} #{index} needs guessing");
    }

    [Theory]
    [MemberData(nameof(AllPuzzles))]
    public void BlanksAreSpreadOverEveryBox(SudokuLevel level, int index)
    {
        var puzzle = SudokuRules.Get(level, index);
        var shape = puzzle.Shape;
        var perBox = new int[shape.Size];
        for (var i = 0; i < shape.CellCount; i++)
            if (puzzle.Givens[i] == 0)
                perBox[shape.Box(i)]++;
        // Emptied box by box in turn, so every box has something to fill
        Assert.All(perBox, n => Assert.True(n >= 1, $"{level} #{index}: a box with no blank"));
        // No row or column is completely empty either (there is always something to start from)
        foreach (var u in shape.Units)
            Assert.Contains(u.Cells, c => puzzle.Givens[c] != 0);
    }

    [Fact]
    public void PuzzlesOfALevelAreAllDifferentAndRepeatable()
    {
        foreach (var level in Enum.GetValues<SudokuLevel>())
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < SudokuRules.PuzzleCount(level); i++)
            {
                var a = SudokuRules.Get(level, i).ToAscii();
                Assert.True(seen.Add(a), $"{level} #{i} repeats an earlier puzzle");
                Assert.Equal(a, SudokuRules.Get(level, i).ToAscii());
            }
        }
    }

    [Fact]
    public void FirstPuzzlesStayTheSame()
    {
        Assert.Equal(FirstEasy, SudokuRules.Get(SudokuLevel.Easy, 0).ToAscii());
        Assert.Equal(FirstHard, SudokuRules.Get(SudokuLevel.Hard, 0).ToAscii());
    }

    private const string FirstEasy =
        "2.|.1\n" +
        ".4|.2\n" +
        "--+--\n" +
        "42|13\n" +
        ".1|.4\n";

    private const string FirstHard =
        ".63|...\n" +
        ".2.|.65\n" +
        "---+---\n" +
        "63.|5..\n" +
        ".4.|..6\n" +
        "---+---\n" +
        "312|.54\n" +
        "...|.1.\n";

    [Fact]
    public void CountSolutionsSeesSeveralAnswersAndClashes()
    {
        var shape = SudokuShape.For(4);
        Assert.Equal(2, SudokuSolver.CountSolutions(shape, new int[16], 2));
        var clash = new int[16];
        clash[0] = 1;
        clash[1] = 1;
        Assert.Equal(0, SudokuSolver.CountSolutions(shape, clash));
    }

    // ---- Playing ----

    private static (SudokuBoard Board, int Empty) EasyBoard()
    {
        var board = new SudokuBoard(SudokuRules.Get(SudokuLevel.Easy, 0));
        var empty = Array.IndexOf(board.Puzzle.Givens, 0);
        return (board, empty);
    }

    /// <summary>An animal that clashes in <paramref name="cell"/> (one already in its row, column or box).</summary>
    private static int ClashingValue(SudokuBoard board, int cell)
    {
        foreach (var p in board.Shape.Peers(cell))
            if (board.Value(p) != 0)
                return board.Value(p);
        throw new InvalidOperationException("No neighbor");
    }

    [Fact]
    public void ClashingAnimalDoesNotGoInAndNamesTheOther()
    {
        var (board, cell) = EasyBoard();
        var value = ClashingValue(board, cell);
        var move = board.Place(cell, value);
        Assert.Equal(SudokuPlaceKind.Clash, move.Kind);
        Assert.Equal(0, board.Value(cell));
        Assert.NotEmpty(move.Clashes);
        Assert.All(move.Clashes, c => Assert.Equal(value, board.Value(c)));
        Assert.Equal(0, board.Hints);
        Assert.False(board.CanUndo); // Nothing changed
    }

    [Fact]
    public void StartingAnimalsAreLocked()
    {
        var (board, _) = EasyBoard();
        var given = Array.FindIndex(board.Puzzle.Givens, v => v != 0);
        var before = board.Value(given);
        Assert.Equal(SudokuPlaceKind.Locked, board.Place(given, before % 4 + 1).Kind);
        Assert.False(board.Erase(given));
        Assert.Null(board.ToggleMemo(given, 1));
        Assert.Equal(before, board.Value(given));
    }

    [Fact]
    public void SameAnimalAgainTakesItOutAndEraseClears()
    {
        var (board, cell) = EasyBoard();
        var answer = board.Puzzle.Solution[cell];
        Assert.Equal(SudokuPlaceKind.Placed, board.Place(cell, answer).Kind);
        Assert.Equal(answer, board.Value(cell));
        Assert.Equal(SudokuPlaceKind.Removed, board.Place(cell, answer).Kind);
        Assert.Equal(0, board.Value(cell));
        board.Place(cell, answer);
        Assert.True(board.Erase(cell));
        Assert.Equal(0, board.Value(cell));
        Assert.False(board.Erase(cell)); // Already empty
    }

    [Fact]
    public void MemosToggleAndPlacingClearsThemNearby()
    {
        var board = new SudokuBoard(SudokuRules.Get(SudokuLevel.Hard, 0));
        var empties = Enumerable.Range(0, 36).Where(i => board.Puzzle.Givens[i] == 0).ToList();
        var a = empties[0];
        var value = board.Puzzle.Solution[a];
        // A peer of a that is empty
        var peer = board.Shape.Peers(a).First(p => board.Value(p) == 0);
        Assert.True(board.ToggleMemo(a, 1));
        Assert.True(board.ToggleMemo(a, value));
        Assert.True(board.ToggleMemo(peer, value));
        Assert.False(board.ToggleMemo(a, 1));
        Assert.True(board.HasMemo(a, value));
        Assert.False(board.HasMemo(a, 1));

        board.Place(a, value);
        Assert.Equal(0, board.Memo(a));
        Assert.False(board.HasMemo(peer, value));
        Assert.Null(board.ToggleMemo(a, 2)); // Has an animal
        // Erase on a memo-only cell clears the memos
        board.ToggleMemo(peer, Enumerable.Range(1, 6).First(v => v != value));
        Assert.True(board.Erase(peer));
        Assert.Equal(0, board.Memo(peer));
    }

    [Fact]
    public void UndoGoesBackOneStepAndNotAcrossAHint()
    {
        var (board, cell) = EasyBoard();
        var answer = board.Puzzle.Solution[cell];
        board.Place(cell, answer);
        Assert.True(board.CanUndo);
        Assert.True(board.Undo());
        Assert.Equal(0, board.Value(cell));
        Assert.False(board.Undo());

        board.Place(cell, answer);
        board.UseHint();
        Assert.False(board.CanUndo);
        Assert.Equal(answer, board.Value(cell));
    }

    [Fact]
    public void HintFillsThePreferredCellThenTheEasiestAndLocksIt()
    {
        var board = new SudokuBoard(SudokuRules.Get(SudokuLevel.Normal, 3));
        var empties = Enumerable.Range(0, 36).Where(i => board.Puzzle.Givens[i] == 0).ToList();
        var pick = empties[^1];
        var hint = board.UseHint(pick)!;
        Assert.Equal(pick, hint.Cell);
        Assert.Equal(board.Puzzle.Solution[pick], board.Value(pick));
        Assert.True(board.IsFixed(pick));
        Assert.True(board.IsHinted(pick));
        Assert.Equal(1, board.Hints);
        Assert.Empty(hint.Cleared);

        // Not given a cell (or a filled one): a cell where the fewest animals fit
        var next = board.UseHint(pick)!;
        Assert.NotEqual(pick, next.Cell);
        Assert.Equal(1, SudokuSolver.BitCount(SudokuSolver.Candidates(board.Shape, BeforeHint(board, next.Cell), next.Cell)));
        Assert.Equal(2, board.Hints);
    }

    private static int[] BeforeHint(SudokuBoard board, int cell)
    {
        var grid = board.Grid;
        grid[cell] = 0;
        return grid;
    }

    [Fact]
    public void HintsAloneFinishThePuzzle()
    {
        foreach (var level in Enum.GetValues<SudokuLevel>())
        {
            var board = new SudokuBoard(SudokuRules.Get(level, 1));
            var blanks = board.EmptyCount;
            SudokuHint? last = null;
            for (var i = 0; i < blanks; i++)
                last = board.UseHint();
            Assert.True(board.Solved);
            Assert.True(last!.Solved);
            Assert.Equal(blanks, board.Hints);
            Assert.Null(board.UseHint());
            Assert.Equal(board.Puzzle.Solution, board.Grid);
        }
    }

    [Fact]
    public void HintUntanglesAnimalsInEachOthersSpots()
    {
        // Two wrong but clash-free animals that block each other's right answer: the hint fixes one and takes the other out
        foreach (var level in Enum.GetValues<SudokuLevel>())
            for (var index = 0; index < SudokuRules.PuzzleCount(level); index++)
            {
                var board = new SudokuBoard(SudokuRules.Get(level, index));
                // Fill with wrong-but-legal animals as long as possible (a careless child), then hint until done
                for (var cell = 0; cell < board.Shape.CellCount; cell++)
                {
                    if (board.Value(cell) != 0)
                        continue;
                    for (var v = 1; v <= board.Shape.Size; v++)
                        if (v != board.Puzzle.Solution[cell] && board.Place(cell, v).Kind == SudokuPlaceKind.Placed)
                            break;
                }
                for (var guard = 0; guard < 100 && !board.Solved; guard++)
                {
                    var hint = board.UseHint();
                    Assert.NotNull(hint);
                    Assert.Equal(board.Puzzle.Solution[hint!.Cell], board.Value(hint.Cell));
                    Assert.All(hint.Cleared, c => Assert.Equal(0, board.Value(c)));
                    // Every hint keeps the board free of clashes
                    for (var c = 0; c < board.Shape.CellCount; c++)
                        if (board.Value(c) != 0)
                            Assert.Empty(SudokuSolver.Conflicts(board.Shape, board.Grid, c, board.Value(c)));
                    // With one answer, a full board without clashes is the answer
                    Assert.True(board.EmptyCount > 0 || board.Solved);
                }
                Assert.True(board.Solved, $"{level} #{index}");
            }
    }

    [Fact]
    public void FillingTheLastCellOfARowReportsItAndTheLastCellSolves()
    {
        var board = new SudokuBoard(SudokuRules.Get(SudokuLevel.Easy, 2));
        SudokuMove? last = null;
        var completedSomething = false;
        while (SudokuSolver.NextSure(board.Shape, board.Grid) is { } step)
        {
            last = board.Place(step.Cell, step.Value);
            Assert.Equal(SudokuPlaceKind.Placed, last.Kind);
            Assert.All(last.Completed, u => Assert.Contains(step.Cell, u.Cells));
            completedSomething |= last.Completed.Count > 0;
        }
        Assert.True(completedSomething);
        Assert.True(board.Solved);
        Assert.True(last!.Solved);
        Assert.Equal(3, last.Completed.Count); // The last cell finishes its row, column and box at once
        Assert.Equal(SudokuPlaceKind.Ignored, board.Place(0, 1).Kind);
    }

    [Fact]
    public void ClockStartsWithTheFirstInputAndStopsWhenSolved()
    {
        var (board, cell) = EasyBoard();
        board.AddTime(5);
        Assert.Equal(0, board.TimeMs);
        board.Start();
        board.AddTime(1.25);
        Assert.Equal(1250, board.TimeMs);
        while (board.UseHint() is not null) { }
        board.AddTime(3);
        Assert.Equal(1250, board.TimeMs);
        var entry = board.Entry("다온", 99);
        Assert.Equal(1250, entry.TimeMs);
        Assert.Equal(board.Hints, entry.Score);
        _ = cell;
    }

    [Fact]
    public void FirstActionStartsTheClockToo()
    {
        var (board, cell) = EasyBoard();
        board.Place(cell, ClashingValue(board, cell)); // Even a clash counts as starting
        Assert.True(board.Started);
    }
}
