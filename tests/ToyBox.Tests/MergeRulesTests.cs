using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class MergeRulesTests
{
    /// <summary>Builds a board from rows, top to bottom.</summary>
    private static MergeBoard Board(params int[][] rows)
    {
        var board = new MergeBoard(rows.Length);
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows.Length; x++)
                board[x, y] = rows[y][x];
        return board;
    }

    private static int[] Row(MergeBoard board, int y)
    {
        var row = new int[board.Size];
        for (var x = 0; x < board.Size; x++)
            row[x] = board[x, y];
        return row;
    }

    private static int[] Column(MergeBoard board, int x)
    {
        var column = new int[board.Size];
        for (var y = 0; y < board.Size; y++)
            column[y] = board[x, y];
        return column;
    }

    [Fact]
    public void VariantsAndSizesFollowTheDesign()
    {
        Assert.Equal(new[] { "easy", "normal" }, MergeRules.Variants.Select(v => v.Id));
        Assert.Equal(5, MergeRules.Size(MergeLevel.Easy));
        Assert.Equal(4, MergeRules.Size(MergeLevel.Normal));
        Assert.Equal("normal", MergeRules.VariantId(MergeLevel.Normal));
    }

    [Theory]
    [InlineData(new[] { 2, 2, 2, 0 }, new[] { 4, 2, 0, 0 }, 4)]   // A tile joins at most once per push
    [InlineData(new[] { 2, 2, 2, 2 }, new[] { 4, 4, 0, 0 }, 8)]
    [InlineData(new[] { 4, 4, 8, 0 }, new[] { 8, 8, 0, 0 }, 8)]   // The new 8 does not join the old 8 in the same push
    [InlineData(new[] { 0, 0, 0, 2 }, new[] { 2, 0, 0, 0 }, 0)]
    [InlineData(new[] { 2, 0, 2, 4 }, new[] { 4, 4, 0, 0 }, 4)]
    public void PushLeftSlidesAndJoinsOnce(int[] before, int[] after, int gained)
    {
        var board = Board(before, new int[4], new int[4], new int[4]);
        var move = board.Push(MergeDir.Left);
        Assert.Equal(after, Row(board, 0));
        Assert.Equal(gained, move.Gained);
        Assert.Equal(gained, board.Score);
    }

    [Fact]
    public void PushRightJoinsFromTheRightEdge()
    {
        var board = Board(new[] { 2, 2, 2, 0 }, new int[4], new int[4], new int[4]);
        board.Push(MergeDir.Right);
        Assert.Equal(new[] { 0, 0, 2, 4 }, Row(board, 0));
    }

    [Fact]
    public void PushUpAndDownWorkOnColumns()
    {
        var board = Board(new[] { 2, 0, 0, 0 }, new[] { 2, 0, 0, 0 }, new[] { 4, 0, 0, 0 }, new[] { 0, 0, 0, 0 });
        board.Push(MergeDir.Down);
        Assert.Equal(new[] { 0, 0, 4, 4 }, Column(board, 0));
        board.Push(MergeDir.Up);
        Assert.Equal(new[] { 8, 0, 0, 0 }, Column(board, 0));
    }

    [Fact]
    public void SlidesDescribeEveryTileAndMarkJoins()
    {
        var board = Board(new[] { 0, 2, 0, 2 }, new int[4], new int[4], new int[4]);
        var move = board.Push(MergeDir.Left);
        Assert.Equal(2, move.Slides.Count);
        Assert.All(move.Slides, s => Assert.True(s.Merged));
        Assert.All(move.Slides, s => Assert.Equal((0, 0), (s.ToX, s.ToY)));
        Assert.Equal(new MergeJoin(0, 0, 4), Assert.Single(move.Joins));
    }

    [Fact]
    public void PushThatMovesNothingChangesNothing()
    {
        var board = Board(new[] { 2, 4, 0, 0 }, new int[4], new int[4], new int[4]);
        var move = board.Push(MergeDir.Left);
        Assert.False(move.Moved);
        Assert.Equal(new[] { 2, 4, 0, 0 }, Row(board, 0));
        Assert.False(board.CanUndo); // Nothing to take back
    }

    [Fact]
    public void SpawnFillsOnlyEmptyCellsWithTwoOrFour()
    {
        var board = new MergeBoard(4);
        var rng = new Random(7);
        for (var i = 0; i < 16; i++)
        {
            var spawned = board.Spawn(rng);
            Assert.NotNull(spawned);
            Assert.Contains(spawned!.Value.Value, new[] { 2, 4 });
        }
        Assert.Equal(0, board.EmptyCount);
        Assert.Null(board.Spawn(rng));
    }

    [Fact]
    public void StuckOnlyWhenFullWithNoEqualNeighbors()
    {
        var stuck = Board(new[] { 2, 4, 2, 4 }, new[] { 4, 2, 4, 2 }, new[] { 2, 4, 2, 4 }, new[] { 4, 2, 4, 2 });
        Assert.False(stuck.CanMove());
        stuck[3, 3] = 4; // Now equal to its left neighbor
        Assert.True(stuck.CanMove());
        stuck[3, 3] = 0;
        Assert.True(stuck.CanMove());
    }

    [Fact]
    public void UndoRestoresBoardAndScoreAndIsLimited()
    {
        var board = Board(new[] { 2, 2, 0, 0 }, new int[4], new int[4], new int[4]);
        board.Push(MergeDir.Left);
        Assert.Equal(4, board.Score);
        Assert.True(board.Undo());
        Assert.Equal(new[] { 2, 2, 0, 0 }, Row(board, 0));
        Assert.Equal(0, board.Score);
        Assert.Equal(MergeRules.UndoCount - 1, board.UndosLeft);

        for (var i = 0; i < 5; i++)
        {
            board.Push(i % 2 == 0 ? MergeDir.Right : MergeDir.Left);
            board.Undo();
        }
        Assert.Equal(0, board.UndosLeft);
        board.Push(MergeDir.Right);
        Assert.False(board.Undo());
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(2048, 11)]
    [InlineData(4096, 12)]
    [InlineData(16384, 12)]
    public void TileArtFollowsTheValue(int value, int art) => Assert.Equal(art, MergeRules.TileArt(value));

    [Fact]
    public void EmptyAfterDoesNotTouchTheBoard()
    {
        var board = Board(new[] { 2, 2, 0, 0 }, new int[4], new int[4], new int[4]);
        Assert.Equal(15, board.EmptyAfter(MergeDir.Left));
        Assert.Equal(-1, board.EmptyAfter(MergeDir.Up));
        Assert.Equal(new[] { 2, 2, 0, 0 }, Row(board, 0));
    }
}
