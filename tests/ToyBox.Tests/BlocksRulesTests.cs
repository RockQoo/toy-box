using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class BlocksRulesTests
{
    private static readonly PieceKind[] AllKinds = Enum.GetValues<PieceKind>();

    /// <summary>A round whose falling piece is replaced by the given one at its spawn place (the bag is unchanged).</summary>
    private static BlocksRound RoundWith(PieceKind kind, BlocksLevel level = BlocksLevel.Normal, int seed = 1)
    {
        for (var s = seed; s < seed + 500; s++)
        {
            var round = new BlocksRound(level, new Random(s));
            if (round.Piece.Kind == kind)
                return round;
        }
        throw new InvalidOperationException("no seed starts with " + kind);
    }

    private static void FillRow(BlocksBoard board, int y, params int[] gaps)
    {
        for (var x = 0; x < board.Width; x++)
            board[x, y] = gaps.Contains(x) ? 0 : 1;
    }

    private static HashSet<BlockCell> CellSet(ActivePiece piece) => piece.Cells.ToHashSet();

    // ---- Pieces ----

    [Fact]
    public void VariantsAndBoardSizesFollowTheDesign()
    {
        Assert.Equal(new[] { "easy", "normal" }, BlocksRules.Variants.Select(v => v.Id));
        Assert.Equal(new[] { "쉬움", "보통" }, BlocksRules.Variants.Select(v => v.Label));
        Assert.Equal((8, 16), (BlocksRules.Width(BlocksLevel.Easy), BlocksRules.Height(BlocksLevel.Easy)));
        Assert.Equal((10, 20), (BlocksRules.Width(BlocksLevel.Normal), BlocksRules.Height(BlocksLevel.Normal)));
        Assert.Equal(180f, BlocksRules.PlaySeconds);
        Assert.Equal(0.8f, BlocksRules.LockDelay);
        Assert.Equal(0.18f, BlocksRules.RepeatDelay);
        Assert.Equal("normal", BlocksRules.VariantId(BlocksLevel.Normal));
        Assert.Equal(7, BlocksRules.PictureNames.Length);
    }

    [Fact]
    public void EveryPieceHasFourCellsInItsBoxInEveryTurn()
    {
        foreach (var kind in AllKinds)
            for (var r = 0; r < 4; r++)
            {
                var cells = BlocksRules.Cells(kind, r);
                Assert.Equal(4, cells.Distinct().Count());
                var n = BlocksRules.BoxSize(kind);
                Assert.All(cells, c => Assert.True(c.X >= 0 && c.Y >= 0 && c.X < n && c.Y < n));
            }
    }

    [Fact]
    public void SevenDifferentShapes()
    {
        // Compare the shapes moved to their top-left corner, in any turn
        static string Norm(IEnumerable<BlockCell> cells)
        {
            var list = cells.ToList();
            var minX = list.Min(c => c.X);
            var minY = list.Min(c => c.Y);
            return string.Join(";", list.Select(c => (c.X - minX, c.Y - minY)).OrderBy(c => c));
        }
        var shapes = AllKinds.Select(k => Enumerable.Range(0, 4).Select(r => Norm(BlocksRules.Cells(k, r))).ToHashSet()).ToList();
        for (var a = 0; a < shapes.Count; a++)
            for (var b = a + 1; b < shapes.Count; b++)
                Assert.Empty(shapes[a].Intersect(shapes[b]));
        Assert.Single(shapes[(int)PieceKind.O]);
        Assert.Equal(2, shapes[(int)PieceKind.I].Count);
        Assert.Equal(4, shapes[(int)PieceKind.T].Count);
    }

    [Fact]
    public void FourTurnsComeBackAndOppositeTurnsUndo()
    {
        foreach (var kind in AllKinds)
            for (var r = 0; r < 4; r++)
            {
                Assert.Equal(BlocksRules.Cells(kind, r).ToHashSet(), BlocksRules.Cells(kind, r + 4).ToHashSet());
                Assert.Equal(BlocksRules.Cells(kind, r - 1).ToHashSet(), BlocksRules.Cells(kind, r + 3).ToHashSet());
            }
        var round = RoundWith(PieceKind.T);
        var start = CellSet(round.Piece);
        Assert.True(round.Rotate(1));
        Assert.NotEqual(start, CellSet(round.Piece));
        Assert.True(round.Rotate(-1));
        Assert.Equal(start, CellSet(round.Piece));
    }

    [Fact]
    public void NewPiecesAppearCenteredWithTheirBottomOnTheFirstVisibleRow()
    {
        foreach (var level in Enum.GetValues<BlocksLevel>())
            foreach (var kind in AllKinds)
            {
                var width = BlocksRules.Width(level);
                var piece = new ActivePiece(kind, 0, BlocksRules.SpawnX(kind, width), BlocksRules.SpawnY(kind));
                var cells = piece.Cells.ToList();
                Assert.Equal(BlocksRules.HiddenRows, cells.Max(c => c.Y));
                var left = cells.Min(c => c.X);
                var right = width - 1 - cells.Max(c => c.X);
                Assert.InRange(left - right, -1, 1);
                Assert.True(new BlocksBoard(width, BlocksRules.Height(level)).Fits(piece));
            }
    }

    // ---- 7-bag ----

    [Fact]
    public void EverySevenPiecesAreAllSevenKinds()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var bag = new PieceBag(new Random(seed));
            for (var group = 0; group < 20; group++)
            {
                var seven = Enumerable.Range(0, 7).Select(_ => bag.Next()).ToHashSet();
                Assert.Equal(7, seven.Count);
            }
        }
    }

    [Fact]
    public void NoPieceIsMissingForLong()
    {
        var bag = new PieceBag(new Random(7));
        var last = new Dictionary<PieceKind, int>();
        for (var i = 0; i < 2000; i++)
        {
            var kind = bag.Next();
            if (last.TryGetValue(kind, out var before))
                Assert.True(i - before <= 13, $"{kind} came back after {i - before}");
            last[kind] = i;
        }
    }

    [Fact]
    public void PeekShowsTheNextPiecesWithoutTakingThem()
    {
        var bag = new PieceBag(new Random(3));
        var peek = bag.Peek(10);
        Assert.Equal(peek, Enumerable.Range(0, 10).Select(_ => bag.Next()));
        var orders = Enumerable.Range(0, 20).Select(s => string.Join("", new PieceBag(new Random(s)).Peek(7))).ToHashSet();
        Assert.True(orders.Count > 10, "the bag is not shuffled");
    }

    // ---- Moving, turning, wall kicks ----

    [Fact]
    public void WallsStopMoves()
    {
        var round = RoundWith(PieceKind.T);
        var moves = 0;
        while (round.Move(-1))
            moves++;
        Assert.Equal(0, round.Piece.Cells.Min(c => c.X));
        Assert.Equal(3, moves);
        while (round.Move(1)) { }
        Assert.Equal(round.Board.Width - 1, round.Piece.Cells.Max(c => c.X));
    }

    [Fact]
    public void StackedBlocksStopMoves()
    {
        var round = RoundWith(PieceKind.O);
        var bottom = round.Piece.Cells.Max(c => c.Y);
        var left = round.Piece.Cells.Min(c => c.X);
        round.Board[left - 2, bottom] = 1;
        Assert.True(round.Move(-1));
        Assert.False(round.Move(-1));
        Assert.Equal(left - 1, round.Piece.Cells.Min(c => c.X));
    }

    [Fact]
    public void ABlockedTurnShiftsOneCellAside()
    {
        // T pointing right against the right wall: turning to point down needs one step left
        var round = RoundWith(PieceKind.T);
        Assert.True(round.Rotate(-1)); // pointing left: box column 2 is empty
        while (round.Move(1)) { }
        var x = round.Piece.X;
        Assert.True(round.Rotate(-1));
        Assert.Equal(x - 1, round.Piece.X);
        Assert.Equal(round.Board.Width - 1, round.Piece.Cells.Max(c => c.X));
    }

    [Fact]
    public void TheLongPieceKicksTwoCellsFromTheWall()
    {
        var round = RoundWith(PieceKind.I);
        for (var i = 0; i < 3; i++)
            round.SoftDrop();
        Assert.True(round.Rotate(1)); // standing up in box column 2
        while (round.Move(-1)) { }
        Assert.Equal(0, round.Piece.Cells.Min(c => c.X));
        Assert.True(round.Rotate(1)); // lying down again needs room to the right
        Assert.Equal(0, round.Piece.Cells.Min(c => c.X));
        Assert.Single(round.Piece.Cells.Select(c => c.Y).Distinct());
    }

    [Fact]
    public void AFullyBlockedTurnDoesNothing()
    {
        var round = RoundWith(PieceKind.T);
        var board = round.Board;
        // Wall the piece in: everything around it is filled
        var cells = CellSet(round.Piece);
        for (var y = 0; y < board.Height; y++)
            for (var x = 0; x < board.Width; x++)
                if (!cells.Contains(new BlockCell(x, y)))
                    board[x, y] = 1;
        var before = round.Piece;
        Assert.False(round.Rotate(1));
        Assert.False(round.Rotate(-1));
        Assert.Equal(before, round.Piece);
    }

    [Fact]
    public void ATurnOnTheStackCanStepUp()
    {
        // An S lying on a flat floor turns standing up; it cannot go lower, so it steps up a row
        var round = RoundWith(PieceKind.S);
        var board = round.Board;
        while (round.SoftDrop()) { }
        Assert.True(round.Grounded);
        Assert.True(round.Rotate(1));
        Assert.True(board.Fits(round.Piece));
    }

    // ---- Falling, ghost, lock delay ----

    [Fact]
    public void GravityUsesTheFallIntervalAndSoftDropIsFaster()
    {
        var round = RoundWith(PieceKind.T);
        var y = round.Piece.Y;
        round.Update(0.95f, false);
        Assert.Equal(y, round.Piece.Y);
        round.Update(0.1f, false);
        Assert.Equal(y + 1, round.Piece.Y);

        var soft = RoundWith(PieceKind.T);
        soft.Update(0.26f, true);
        Assert.Equal(y + 5, soft.Piece.Y);
    }

    [Fact]
    public void TheGhostIsWhereAHardDropLands()
    {
        var round = RoundWith(PieceKind.L);
        round.Board[round.Piece.X + 1, 15] = 1;
        var ghost = round.Piece with { Y = round.GhostY };
        var expected = ghost.Cells.Select(c => new PlacedBlock(c.X, c.Y, PieceKind.L)).ToHashSet();
        var lockResult = round.HardDrop();
        Assert.Equal(expected, lockResult.Placed.ToHashSet());
        Assert.Equal(ghost.Y - BlocksRules.SpawnY(PieceKind.L), lockResult.DropRows);
        Assert.All(lockResult.Placed, b => Assert.Equal((int)PieceKind.L + 1, round.Board[b.X, b.Y]));
    }

    [Fact]
    public void APieceOnTheFloorLocksAfterTheDelay()
    {
        var round = RoundWith(PieceKind.O);
        while (round.SoftDrop()) { }
        Assert.Null(round.Update(0.5f, false));
        Assert.Null(round.Update(0.25f, false));
        var locked = round.Update(0.1f, false);
        Assert.NotNull(locked);
        Assert.Equal(1, round.PiecesLocked);
        Assert.Equal(4, round.Board.BlockCount);
    }

    [Fact]
    public void MovingOnTheFloorRestartsTheDelayALimitedNumberOfTimes()
    {
        var round = RoundWith(PieceKind.O);
        while (round.SoftDrop()) { }
        var dir = -1;
        for (var i = 0; i < BlocksRules.MaxLockResets; i++)
        {
            Assert.Null(round.Update(0.7f, false));
            if (!round.Move(dir))
            {
                dir = -dir;
                Assert.True(round.Move(dir));
            }
        }
        // Out of restarts: the next move does not help any more
        Assert.Null(round.Update(0.7f, false));
        round.Move(dir);
        Assert.NotNull(round.Update(0.15f, false));
    }

    [Fact]
    public void SlidingOffALedgeFallsAgain()
    {
        var round = RoundWith(PieceKind.O);
        var board = round.Board;
        // A one-row shelf under the piece, open to the left
        for (var x = round.Piece.X; x < board.Width; x++)
            board[x, 10] = 1;
        while (round.SoftDrop()) { }
        Assert.True(round.Grounded);
        round.Update(0.5f, false);
        round.Move(-1);
        round.Move(-1);
        Assert.False(round.Grounded);
        Assert.Null(round.Update(1.5f, false));
        Assert.True(round.Piece.Y > 8);
    }

    // ---- Lines ----

    [Fact]
    public void AFullRowClearsAndTheRowsAboveComeDown()
    {
        var round = RoundWith(PieceKind.I);
        var board = round.Board;
        var bottom = board.Height - 1;
        FillRow(board, bottom, 0, 1, 2, 3);
        board[5, bottom - 1] = 1;
        while (round.Move(-1)) { }
        var result = round.HardDrop();
        Assert.Equal(new[] { bottom }, result.ClearedRows);
        Assert.Equal(1, round.Lines);
        Assert.Equal(1, board[5, bottom]);
        Assert.Equal(1, board.BlockCount);
        Assert.Equal(1, result.Before[9, bottom]); // the view shows the full row before it goes
    }

    [Fact]
    public void FourRowsAtOnce()
    {
        var round = RoundWith(PieceKind.I);
        var board = round.Board;
        for (var y = board.Height - 4; y < board.Height; y++)
            FillRow(board, y, 9);
        Assert.True(round.Rotate(1));
        while (round.Move(1)) { }
        var result = round.HardDrop();
        Assert.Equal(4, result.Lines);
        Assert.Equal(4, round.Lines);
        Assert.Equal(4, round.BestAtOnce);
        Assert.Equal(0, board.BlockCount);
        Assert.Equal("네 줄! 최고!", BlocksRules.ClearWord(4));
        Assert.Equal("한 줄!", BlocksRules.ClearWord(1));
    }

    [Fact]
    public void RowsWithAGapStay()
    {
        var board = new BlocksBoard(10, 20);
        FillRow(board, 21, 4);
        FillRow(board, 20);
        FillRow(board, 19, 0);
        Assert.Equal(new[] { 20 }, board.ClearFullRows());
        Assert.Equal(0, board[4, 21]);
        Assert.Equal(0, board[0, 20]); // the gap row came down by one
        Assert.Equal(1, board[1, 20]);
    }

    // ---- Hold ----

    [Fact]
    public void HoldOncePerPiece()
    {
        var round = new BlocksRound(BlocksLevel.Normal, new Random(5));
        var first = round.Piece.Kind;
        var next = round.Preview(2);
        Assert.True(round.Hold());
        Assert.Equal(first, round.Held);
        Assert.Equal(next[0], round.Piece.Kind);
        Assert.Equal(next[1], round.Preview(1)[0]);
        Assert.False(round.Hold());

        round.HardDrop();
        Assert.False(round.HoldUsed);
        var current = round.Piece.Kind;
        Assert.True(round.Hold());
        Assert.Equal(first, round.Piece.Kind);
        Assert.Equal(current, round.Held);
        Assert.Equal(BlocksRules.SpawnY(first), round.Piece.Y);
    }

    // ---- Reaching the top ----

    [Fact]
    public void ReachingTheTopClearsTheBottomRowsAndGoesOn()
    {
        var round = new BlocksRound(BlocksLevel.Normal, new Random(11));
        var board = round.Board;
        // A tall stack with a gap in every row (no line), up to the second visible row
        for (var y = BlocksRules.HiddenRows + 1; y < board.Height; y++)
            FillRow(board, y, y % board.Width);
        var blocks = board.BlockCount;
        var result = round.HardDrop();

        Assert.True(result.Rescued);
        Assert.Equal(0, result.Lines);
        Assert.Equal(0, round.Lines); // no points for it
        Assert.Equal(1, round.Rescues);
        Assert.Equal(BlocksRules.RescueRows, result.RescuedRows);
        Assert.NotNull(result.AfterClear);
        Assert.Equal(blocks + 4 - board.BlockCount, result.RescuedBlocks.Count);
        Assert.False(board.HiddenRowsUsed);
        Assert.True(board.Fits(round.Piece));
        Assert.All(result.RescuedBlocks, b => Assert.NotEqual(0, result.AfterClear![b.X, b.Y]));
        Assert.All(result.RescuedBlocks, b => Assert.True(b.Y >= board.Height - BlocksRules.RescueRows));
    }

    [Fact]
    public void AStackInTheHiddenRowsIsAlsoRescued()
    {
        var board = new BlocksBoard(8, 16);
        board[3, 1] = 1;
        board[3, 17] = 2;
        board.RemoveBottomRows(BlocksRules.RescueRows);
        Assert.False(board.HiddenRowsUsed);
        Assert.Equal(1, board[3, 7]);
        Assert.Equal(1, board.BlockCount);
    }

    [Fact]
    public void ARoundNeverEndsEvenWithOnlyHardDrops()
    {
        foreach (var level in Enum.GetValues<BlocksLevel>())
        {
            var round = new BlocksRound(level, new Random(2));
            for (var i = 0; i < 400; i++)
            {
                Assert.True(round.Board.Fits(round.Piece));
                round.HardDrop(); // all in the middle: the stack keeps reaching the top
            }
            Assert.True(round.Rescues > 5);
            Assert.False(round.Board.HiddenRowsUsed);
        }
    }

    // ---- Speed ----

    [Fact]
    public void FallSpeedStepsEveryTenLinesDownToTheFloor()
    {
        Assert.Equal(1.2f, BlocksRules.FallInterval(BlocksLevel.Easy, 0));
        Assert.Equal(1.0f, BlocksRules.FallInterval(BlocksLevel.Normal, 9));
        Assert.Equal(1, BlocksRules.Stage(BlocksLevel.Normal, 10));
        Assert.Equal(0.6f, BlocksRules.FallInterval(BlocksLevel.Easy, 500));
        Assert.Equal(0.35f, BlocksRules.FallInterval(BlocksLevel.Normal, 500));
        foreach (var level in Enum.GetValues<BlocksLevel>())
            for (var lines = 0; lines < 200; lines++)
                Assert.True(BlocksRules.FallInterval(level, lines + 1) <= BlocksRules.FallInterval(level, lines));
        Assert.True(BlocksRules.FallInterval(BlocksLevel.Easy, 30) > BlocksRules.FallInterval(BlocksLevel.Normal, 30));
    }

    // ---- Held keys ----

    [Fact]
    public void HeldDirectionRepeatsAfterTheDelay()
    {
        var repeat = new KeyRepeat();
        repeat.Press(-1);
        Assert.Equal(-1, repeat.Direction);
        Assert.Equal(0, repeat.Update(0.17f));
        Assert.Equal(1, repeat.Update(0.02f));
        Assert.Equal(0, repeat.Update(0.02f));
        Assert.Equal(1, repeat.Update(0.04f));
        Assert.Equal(4, repeat.Update(0.2f));
        repeat.Release(-1);
        Assert.Equal(0, repeat.Direction);
        Assert.Equal(0, repeat.Update(1f));
    }

    [Fact]
    public void TheLatestDirectionWinsAndLettingGoReturnsToTheOther()
    {
        var repeat = new KeyRepeat();
        repeat.Press(-1);
        repeat.Update(0.5f);
        repeat.Press(1);
        Assert.Equal(1, repeat.Direction);
        Assert.Equal(0, repeat.Update(0.1f)); // a fresh delay
        repeat.Release(1);
        Assert.Equal(-1, repeat.Direction);
        Assert.Equal(0, repeat.Update(0.1f));
        // Key and touch button holding the same way: both must let go
        repeat.Press(-1);
        repeat.Release(-1);
        Assert.Equal(-1, repeat.Direction);
        repeat.Release(-1);
        Assert.Equal(0, repeat.Direction);
        repeat.Press(1);
        repeat.Clear();
        Assert.Equal(0, repeat.Direction);
    }

    // ---- Autoplay bot ----

    [Theory]
    [InlineData(BlocksLevel.Easy)]
    [InlineData(BlocksLevel.Normal)]
    public void TheBotClearsLinesWithoutReachingTheTop(BlocksLevel level)
    {
        for (var seed = 0; seed < 3; seed++)
        {
            var round = new BlocksRound(level, new Random(seed));
            for (var i = 0; i < 200; i++)
                PlayLikeTheView(round);
            Assert.Equal(0, round.Rescues);
            Assert.True(round.Lines >= 200 * 4 / round.Board.Width - 10, $"seed {seed}: {round.Lines} lines");
        }
    }

    /// <summary>What the autoplay does: hold if the plan says so, turn, slide, then drop.</summary>
    private static void PlayLikeTheView(BlocksRound round)
    {
        var plan = BlocksBot.Best(round);
        if (plan.UseHold)
        {
            Assert.True(round.Hold());
            plan = BlocksBot.Best(round); // hold is used now: a plan for the piece that came in
        }
        var turns = BlocksRules.Mod4(plan.Rotation - round.Piece.Rotation);
        for (var t = 0; t < (turns == 3 ? 1 : turns); t++)
            round.Rotate(turns == 3 ? -1 : 1);
        for (var guard = 0; guard < 20 && round.Piece.X != plan.X; guard++)
            if (!round.Move(Math.Sign(plan.X - round.Piece.X)))
                break;
        round.HardDrop();
    }
}
