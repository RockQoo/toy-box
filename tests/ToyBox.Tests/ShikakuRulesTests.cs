using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class ShikakuRulesTests
{
    public static IEnumerable<object[]> AllPuzzles()
    {
        foreach (var level in Enum.GetValues<ShikakuLevel>())
            for (var i = 0; i < ShikakuRules.PuzzleCount(level); i++)
                yield return new object[] { level, i };
    }

    /// <summary>
    /// A tiny hand-made 3 x 3: ". 3 . / . . 4 / 2 . ." read row by row. Its one answer: the top row (3), the left
    /// column below it (2) and the 2 x 2 square bottom right (4).
    /// </summary>
    private static ShikakuPuzzle Small()
    {
        var clues = new[] { 0, 3, 0, 0, 0, 4, 2, 0, 0 };
        return new ShikakuPuzzle(3, clues,
            new[] { new ShikakuRect(0, 0, 1, 3), new ShikakuRect(1, 0, 2, 1), new ShikakuRect(1, 1, 2, 2) });
    }

    [Fact]
    public void VariantsSizesAndCluesFollowGdd()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, ShikakuRules.Variants.Select(v => v.Id));
        Assert.Equal(new[] { "쉬움", "보통", "어려움" }, ShikakuRules.Variants.Select(v => v.Label));
        Assert.Equal("hard", ShikakuRules.VariantId(ShikakuLevel.Hard));
        Assert.Equal(new[] { 4, 5, 7 }, Enum.GetValues<ShikakuLevel>().Select(ShikakuRules.Size));
        Assert.Equal(new[] { 6, 8, 10 }, Enum.GetValues<ShikakuLevel>().Select(ShikakuRules.MaxClue));
        foreach (var level in Enum.GetValues<ShikakuLevel>())
            Assert.Equal(10, ShikakuRules.PuzzleCount(level));
        Assert.Equal(3, ShikakuRules.Wrap(ShikakuLevel.Easy, 13));
        Assert.Equal(9, ShikakuRules.Wrap(ShikakuLevel.Easy, -1));
        Assert.Equal("42.3초", ShikakuRules.FormatRecord(42_300));
    }

    [Theory]
    [MemberData(nameof(AllPuzzles))]
    public void EveryPuzzleHasExactlyOneAnswer(ShikakuLevel level, int index)
    {
        var puzzle = ShikakuRules.Get(level, index);
        var n = ShikakuRules.Size(level);
        Assert.Equal(n, puzzle.Size);
        Assert.Equal(1, ShikakuSolver.CountSolutions(puzzle, 10));
        // The stored answer really is a split of the whole board, one number per piece, area = number
        Assert.True(ShikakuSolver.IsValidSolution(puzzle, puzzle.Solution), $"{level} #{index}: answer breaks a rule");
        Assert.Equal(puzzle.ClueCells.Count, puzzle.Solution.Count);
        Assert.Equal(n * n, puzzle.Solution.Sum(r => r.Area));
        Assert.Equal(n * n, puzzle.Clues.Sum());
        foreach (var rect in puzzle.Solution)
        {
            var check = ShikakuSolver.Check(puzzle, rect);
            Assert.Equal(ShikakuFit.Fits, check.Fit);
            Assert.Equal(1, check.ClueCount);
            Assert.Equal(rect.Area, check.Clue);
        }
    }

    [Theory]
    [MemberData(nameof(AllPuzzles))]
    public void NumbersStayInTheLevelsRange(ShikakuLevel level, int index)
    {
        var puzzle = ShikakuRules.Get(level, index);
        var max = ShikakuRules.MaxClue(level);
        Assert.All(puzzle.ClueCells, c => Assert.InRange(puzzle.Clues[c], 1, max));
        // At most one free 1-cell piece, and a number near the top of the range
        Assert.True(puzzle.ClueCells.Count(c => puzzle.Clues[c] == 1) <= 1);
        Assert.True(puzzle.ClueCells.Max(c => puzzle.Clues[c]) >= max - 2);
    }

    [Fact]
    public void PuzzlesAreDifferentWithinALevel()
    {
        foreach (var level in Enum.GetValues<ShikakuLevel>())
        {
            var pictures = Enumerable.Range(0, ShikakuRules.PuzzleCount(level))
                .Select(i => ShikakuRules.Get(level, i).ToAscii()).ToList();
            Assert.Equal(pictures.Count, pictures.Distinct().Count());
        }
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var a = ShikakuRules.Get(ShikakuLevel.Hard, 4);
        var b = ShikakuRules.Get(ShikakuLevel.Hard, 4);
        Assert.Equal(a.ToAscii(), b.ToAscii());
        Assert.Equal(a.Solution, b.Solution);
    }

    [Fact]
    public void FirstEasyPuzzleLooksTheSame()
    {
        // Pins the generator: if this changes, every saved "next puzzle" now points at a different board
        Assert.Equal(FirstEasyPicture, ShikakuRules.Get(ShikakuLevel.Easy, 0).ToAscii());
    }

    private const string FirstEasyPicture = "1..3\n6..2\n...4\n....\n";

    [Fact]
    public void CheckCountsNumbersAndCells()
    {
        var p = Small();
        Assert.Equal(ShikakuFit.Fits, ShikakuSolver.Check(p, new ShikakuRect(0, 0, 1, 3)).Fit);
        Assert.Equal(ShikakuFit.Fits, ShikakuSolver.Check(p, new ShikakuRect(1, 1, 2, 2)).Fit);
        Assert.Equal(ShikakuFit.Fits, ShikakuSolver.Check(p, new ShikakuRect(1, 0, 2, 1)).Fit);
        var wrong = ShikakuSolver.Check(p, new ShikakuRect(0, 0, 2, 2));
        Assert.Equal(ShikakuFit.WrongSize, wrong.Fit);
        Assert.Equal((3, 4, 1), (wrong.Clue, wrong.Area, wrong.ClueCell));
        Assert.Equal(ShikakuFit.NoClue, ShikakuSolver.Check(p, new ShikakuRect(1, 0, 1, 2)).Fit);
        var two = ShikakuSolver.Check(p, new ShikakuRect(0, 1, 2, 2));
        Assert.Equal(ShikakuFit.TooManyClues, two.Fit);
        Assert.Equal(2, two.ClueCount);
        Assert.Equal(new ShikakuRect(0, 1, 3, 2), ShikakuRect.FromCorners(2, 2, 0, 1));
    }

    [Fact]
    public void SolverCountsEveryWayToSplit()
    {
        // The 3 could also go down the middle column, but then the top-left cell is left with no number
        var p = Small();
        Assert.Equal(1, ShikakuSolver.CountSolutions(p, 10));
        Assert.True(ShikakuSolver.IsValidSolution(p, p.Solution));
        Assert.Equal(2, ShikakuSolver.Candidates(p, 1).Count);
        // "2 . / . 2": two ways (two rows or two columns)
        var open = new ShikakuPuzzle(2, new[] { 2, 0, 0, 2 }, Array.Empty<ShikakuRect>());
        Assert.Equal(2, ShikakuSolver.CountSolutions(open, 10));
        // A number with no room at all: no answer
        var none = new ShikakuPuzzle(2, new[] { 3, 0, 0, 1 }, Array.Empty<ShikakuRect>());
        Assert.Equal(0, ShikakuSolver.CountSolutions(none, 10));
    }

    [Fact]
    public void WrongRectanglesDoNotGoInAndTellTheNeededSize()
    {
        var board = new ShikakuBoard(ShikakuRules.Get(ShikakuLevel.Easy, 0));
        Assert.False(board.Started);
        var target = board.Puzzle.Solution.First(r => r.Area >= 2);
        var bigger = target with { Rows = target.Rows + (target.Bottom + 1 < board.Size ? 1 : -1) };
        if (bigger.Rows == 0)
            bigger = target with { Cols = target.Cols + (target.Right + 1 < board.Size ? 1 : -1) };
        var move = board.Draw(bigger);
        Assert.True(board.Started);
        Assert.Equal(ShikakuDrawKind.NotPlaced, move.Kind);
        Assert.Empty(board.Pieces);
        Assert.False(board.CanUndo); // Nothing changed, nothing to undo
        if (move.Check.ClueCount == 1)
            Assert.Equal(ShikakuFit.WrongSize, move.Check.Fit);
        Assert.Equal(ShikakuDrawKind.Ignored, board.Draw(new ShikakuRect(0, 0, 9, 9)).Kind);
    }

    [Fact]
    public void AnyFittingPieceGoesInEvenIfItIsNotTheAnswer()
    {
        // "2 . / . 2" has two answers; both splits are accepted
        var open = new ShikakuPuzzle(2, new[] { 2, 0, 0, 2 }, new[] { new ShikakuRect(0, 0, 1, 2), new ShikakuRect(1, 0, 1, 2) });
        var board = new ShikakuBoard(open);
        Assert.Equal(ShikakuDrawKind.Placed, board.Draw(new ShikakuRect(0, 0, 2, 1)).Kind);
        var last = board.Draw(new ShikakuRect(0, 1, 2, 1));
        Assert.Equal(ShikakuDrawKind.Placed, last.Kind);
        Assert.True(last.Solved);
        Assert.True(board.Solved);
        Assert.False(board.CanUndo);
    }

    [Fact]
    public void OverlappedPiecesComeOutAndUndoBringsThemBack()
    {
        var p = Small();
        var board = new ShikakuBoard(p);
        board.Draw(new ShikakuRect(1, 0, 2, 1)); // 2 going up
        board.Draw(new ShikakuRect(0, 0, 1, 3)); // 3
        Assert.Equal(2, board.Pieces.Count);
        var move = board.Draw(new ShikakuRect(2, 0, 1, 2)); // 2 going right: the old 2 comes out
        Assert.Equal(ShikakuDrawKind.Placed, move.Kind);
        Assert.Single(move.Removed);
        Assert.Equal(new ShikakuRect(1, 0, 2, 1), move.Removed[0].Rect);
        Assert.Equal(2, board.Pieces.Count);
        Assert.True(board.Undo());
        Assert.Contains(board.Pieces, x => x.Rect == new ShikakuRect(1, 0, 2, 1));
        Assert.DoesNotContain(board.Pieces, x => x.Rect == new ShikakuRect(2, 0, 1, 2));
    }

    [Fact]
    public void TapRemovesAPieceAndClearTakesAllOut()
    {
        var board = new ShikakuBoard(Small());
        Assert.Null(board.Remove(4));
        board.Draw(new ShikakuRect(0, 0, 1, 3));
        board.Draw(new ShikakuRect(2, 0, 1, 2));
        Assert.Equal(5, board.CoveredCount);
        var removed = board.Remove(7);
        Assert.Equal(new ShikakuRect(2, 0, 1, 2), removed!.Rect);
        Assert.Single(board.Pieces);
        Assert.True(board.Clear());
        Assert.Empty(board.Pieces);
        Assert.False(board.Clear());
        Assert.True(board.Undo());
        Assert.Single(board.Pieces);
        Assert.True(board.Undo());
        Assert.Equal(2, board.Pieces.Count);
    }

    [Fact]
    public void BoardIsSolvedOnlyWhenFullyCovered()
    {
        var puzzle = ShikakuRules.Get(ShikakuLevel.Normal, 0);
        var board = new ShikakuBoard(puzzle);
        for (var i = 0; i < puzzle.Solution.Count; i++)
        {
            Assert.False(board.Solved);
            var move = board.Draw(puzzle.Solution[i]);
            Assert.Equal(ShikakuDrawKind.Placed, move.Kind);
            Assert.Equal(i == puzzle.Solution.Count - 1, move.Solved);
        }
        Assert.True(board.Solved);
        Assert.Equal(ShikakuDrawKind.Ignored, board.Draw(puzzle.Solution[0]).Kind);
        Assert.Null(board.Remove(0));
    }

    [Theory]
    [MemberData(nameof(AllPuzzles))]
    public void HintsAloneFinishEveryPuzzle(ShikakuLevel level, int index)
    {
        var board = new ShikakuBoard(ShikakuRules.Get(level, index));
        var guard = 0;
        while (!board.Solved && guard++ < 100)
            Assert.NotNull(board.UseHint());
        Assert.True(board.Solved);
        Assert.Equal(board.Puzzle.Solution.Count, board.Hints);
        Assert.Null(board.UseHint());
    }

    [Fact]
    public void HintPrefersAPieceThatMovesNothingAndFixesAWrongPiece()
    {
        var puzzle = ShikakuRules.Get(ShikakuLevel.Easy, 0);
        var board = new ShikakuBoard(puzzle);
        // Draw a wrong but fitting piece for some number, if there is one
        ShikakuRect? wrong = null;
        foreach (var clue in puzzle.ClueCells)
            foreach (var rect in ShikakuSolver.Candidates(puzzle, clue))
                if (!puzzle.Solution.Contains(rect))
                {
                    wrong = rect;
                    break;
                }
        if (wrong is null)
            return;
        board.Draw(wrong.Value);
        var hint = board.HintPiece()!.Value;
        Assert.Contains(hint, puzzle.Solution);
        var untouched = puzzle.Solution.Any(r => !r.Overlaps(wrong.Value));
        if (untouched)
            Assert.False(hint.Overlaps(wrong.Value));
        // Hints keep going until the board is right, taking the wrong piece out on the way
        while (!board.Solved)
            board.UseHint();
        Assert.True(ShikakuSolver.IsValidSolution(puzzle, board.Pieces.Select(x => x.Rect)));
    }

    [Fact]
    public void ClockStartsWithTheFirstInputAndStopsWhenSolved()
    {
        var puzzle = ShikakuRules.Get(ShikakuLevel.Easy, 1);
        var board = new ShikakuBoard(puzzle);
        board.AddTime(5);
        Assert.Equal(0, board.TimeMs);
        board.Start();
        board.AddTime(1.25);
        Assert.Equal(1250, board.TimeMs);
        board.UseHint();
        foreach (var rect in puzzle.Solution)
            board.Draw(rect);
        Assert.True(board.Solved);
        board.AddTime(3);
        Assert.Equal(1250, board.TimeMs);
        var entry = board.Entry("다온", 99);
        Assert.Equal(("다온", 1, 1250L, 99L), (entry.Name, entry.Score, entry.TimeMs, entry.UnixTime));
    }
}
