using System.Numerics;
using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class BubbleRulesTests
{
    public static IEnumerable<object[]> AllBoards()
    {
        foreach (var level in Enum.GetValues<BubbleLevel>())
            for (var i = 0; i < BubbleRules.BoardCount(level); i++)
                yield return new object[] { level, i };
    }

    private static BubbleCell C(int col, int row) => new(col, row);

    /// <summary>The bot (autoplay) plays a whole round; returns the round when the board is empty or the cap is hit.</summary>
    private static BubbleRound PlayOut(BubbleBoard board, int seed, int cap, Func<int, bool>? careless = null)
    {
        var round = new BubbleRound(board, new Random(seed));
        while (!round.Board.IsEmpty && round.Shots < cap)
        {
            var plan = BubbleRules.BestShot(round, careless?.Invoke(round.Shots) ?? false);
            if (plan.Swap)
                round.Swap();
            var trace = round.Shoot(BubbleRules.DirectionFromAngle(plan.Angle));
            round.Land(trace);
        }
        return round;
    }

    // ---- Table ----

    [Fact]
    public void VariantsAndLevelsFollowTheDesign()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, BubbleRules.Variants.Select(v => v.Id));
        Assert.Equal(new[] { 3, 4, 5 }, Enum.GetValues<BubbleLevel>().Select(BubbleRules.ColorCount));
        Assert.Equal(new[] { 4, 5, 6 }, Enum.GetValues<BubbleLevel>().Select(BubbleRules.StartRows));
        Assert.Equal(new[] { 0, 6, 4 }, Enum.GetValues<BubbleLevel>().Select(BubbleRules.DropEvery));
        foreach (var level in Enum.GetValues<BubbleLevel>())
            Assert.Equal(5, BubbleRules.BoardCount(level));
        Assert.Equal(2, BubbleRules.Wrap(BubbleLevel.Easy, 7));
        Assert.Equal(4, BubbleRules.Wrap(BubbleLevel.Easy, -1));
        Assert.Equal("45.2초 · 18번", BubbleRules.FormatRecord(45_200, 18));
    }

    // ---- Staggered grid ----

    [Fact]
    public void RowsAlternateEightAndSevenShiftedHalfABubble()
    {
        Assert.Equal(8, BubbleRules.CellCount(0));
        Assert.Equal(7, BubbleRules.CellCount(1));
        Assert.Equal(0.5f, BubbleRules.CellX(0, 0));
        Assert.Equal(7.5f, BubbleRules.CellX(7, 0));
        Assert.Equal(1f, BubbleRules.CellX(0, 1));
        Assert.Equal(7f, BubbleRules.CellX(6, 1));
        Assert.False(BubbleRules.Inside(C(7, 1)));
        // Neighbors in the next row touch exactly (one diameter apart)
        foreach (var n in BubbleRules.Neighbors(C(3, 2)))
            Assert.Equal(1f, Vector2.Distance(BubbleRules.CellCenter(C(3, 2), 0), BubbleRules.CellCenter(n, 0)), 3);
        // The ceiling moving down moves every row by one row height
        Assert.Equal(BubbleRules.RowHeight, BubbleRules.CellY(0, 1) - BubbleRules.CellY(0, 0), 4);
    }

    [Fact]
    public void NeighborsAreSixInsideFewerAtEdgesAndSymmetric()
    {
        Assert.Equal(new[] { C(2, 2), C(4, 2), C(2, 1), C(3, 1), C(2, 3), C(3, 3) }, BubbleRules.Neighbors(C(3, 2)));
        Assert.Equal(new[] { C(2, 1), C(4, 1), C(3, 0), C(4, 0), C(3, 2), C(4, 2) }, BubbleRules.Neighbors(C(3, 1)));
        Assert.Equal(new[] { C(1, 0), C(0, 1) }, BubbleRules.Neighbors(C(0, 0)));
        Assert.Equal(new[] { C(6, 0), C(6, 1) }, BubbleRules.Neighbors(C(7, 0)));
        for (var row = 0; row < BubbleRules.GridRows; row++)
            for (var col = 0; col < BubbleRules.CellCount(row); col++)
                foreach (var n in BubbleRules.Neighbors(C(col, row)))
                    Assert.Contains(C(col, row), BubbleRules.Neighbors(n));
    }

    // ---- Aiming and flight ----

    [Fact]
    public void ShotsBelowTwelveDegreesAreNotFired()
    {
        Assert.False(BubbleRules.CanFire(BubbleRules.DirectionFromAngle(11f)));
        Assert.True(BubbleRules.CanFire(BubbleRules.DirectionFromAngle(12f)));
        Assert.True(BubbleRules.CanFire(BubbleRules.DirectionFromAngle(168f)));
        Assert.False(BubbleRules.CanFire(BubbleRules.DirectionFromAngle(170f)));
        Assert.False(BubbleRules.CanFire(BubbleRules.DirectionFromAngle(-30f)));
        Assert.Equal(60f, BubbleRules.AngleOf(BubbleRules.DirectionFromAngle(60f)), 3);
        // Aiming at a point: toward the finger
        var dir = BubbleRules.AimToward(new Vector2(BubbleRules.LaunchX + 1, BubbleRules.LaunchY - 1))!.Value;
        Assert.Equal(45f, BubbleRules.AngleOf(dir), 3);
        Assert.Null(BubbleRules.AimToward(new Vector2(BubbleRules.LaunchX, BubbleRules.LaunchY)));
    }

    [Fact]
    public void ShotBouncesOffTheSideWallLikeAMirror()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        var trace = BubbleRules.Trace(board, BubbleRules.DirectionFromAngle(30f));
        Assert.True(trace.Bounces >= 1);
        var bounce = trace.Points[1];
        Assert.Equal(BubbleRules.Width - 0.5f, bounce.X, 3);
        // Before and after the wall the path has the same steepness, mirrored
        var before = Vector2.Normalize(bounce - trace.Points[0]);
        var after = Vector2.Normalize(trace.Points[2] - bounce);
        Assert.Equal(before.X, -after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
        // Never outside the walls
        foreach (var p in trace.Points)
            Assert.InRange(p.X, 0.5f - 1e-3f, BubbleRules.Width - 0.5f + 1e-3f);
        // An empty board: it flies up to the ceiling and sticks in the top row
        Assert.Equal(BubbleRules.CeilingY(0) + 0.5f, trace.End.Y, 3);
        Assert.Equal(0, trace.Cell.Row);
    }

    [Fact]
    public void MirroredAnglesStickInMirroredCells()
    {
        var board = BubbleRules.Board(BubbleLevel.Normal, 0);
        for (var a = 15; a < 90; a += 5)
        {
            var right = BubbleRules.Trace(board, BubbleRules.DirectionFromAngle(a));
            var left = BubbleRules.Trace(new BubbleBoard(BubbleLevel.Normal), BubbleRules.DirectionFromAngle(180 - a));
            var rightEmpty = BubbleRules.Trace(new BubbleBoard(BubbleLevel.Normal), BubbleRules.DirectionFromAngle(a));
            Assert.Equal(BubbleRules.Width - rightEmpty.End.X, left.End.X, 2);
            Assert.Equal(rightEmpty.End.Y, left.End.Y, 2);
            Assert.True(right.End.Y > rightEmpty.End.Y); // Bubbles stop it lower than the ceiling
        }
    }

    [Fact]
    public void ShotStopsAtTheFirstBubbleAndSticksInTheNearestFreeCell()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(3, 0), 0);
        board.Set(C(4, 0), 1);
        // Straight up from x = 4: the bubbles at x 3.5 and 4.5 stop it below them, in row 1 (x 4 is cell 3 of row 1)
        var trace = BubbleRules.Trace(board, BubbleRules.DirectionFromAngle(90f));
        Assert.Equal(0, trace.Bounces);
        Assert.Equal(C(3, 1), trace.Cell);
        Assert.True(Vector2.Distance(trace.End, board.Center(C(3, 0))) < BubbleRules.CollideDistance + 0.05f);

        // Nearest free cell: only cells touching the stack or the ceiling, never a taken one
        Assert.Equal(C(2, 0), board.NearestFreeCell(board.Center(C(3, 0)) + new Vector2(-0.6f, 0)));
        Assert.Equal(C(2, 1), board.NearestFreeCell(new Vector2(2.6f, 1.7f)));
        Assert.False(board.Attachable(C(0, 3)));  // Floating in the air
        Assert.False(board.Attachable(C(3, 0)));  // Taken
        Assert.True(board.Attachable(C(0, 0)));   // Under the ceiling
    }

    [Fact]
    public void AnEmptyGapLetsTheShotThroughToTheCeiling()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        for (var col = 0; col < 8; col++)
            if (col is not (3 or 4))
                board.Set(C(col, 0), 0);
        // Row 0 has a two-bubble gap above the launcher: straight up goes through to the ceiling
        var trace = BubbleRules.Trace(board, BubbleRules.DirectionFromAngle(90f));
        Assert.Equal(0, trace.Cell.Row);
        Assert.Contains(trace.Cell.Col, new[] { 3, 4 });
    }

    // ---- Pops and drops ----

    [Fact]
    public void ThreeOfAColorPopAndWhatHangsFromThemFalls()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(2, 0), 0);
        board.Set(C(3, 0), 0);
        board.Set(C(5, 0), 1);
        board.Set(C(2, 1), 1);   // Hangs from (2,0) and (3,0) only
        board.Set(C(2, 2), 2);   // Hangs from (2,1)
        var result = board.Place(C(4, 0), 0);
        Assert.Equal(3, result.Popped.Count);
        Assert.Equal(C(4, 0), result.Popped[0].Cell); // Nearest first: the new bubble starts the chain
        Assert.Equal(new[] { C(2, 1), C(2, 2) }, result.Dropped.Select(s => s.Cell));
        Assert.Equal(new[] { 1, 2 }, result.Dropped.Select(s => s.Color));
        Assert.Equal(new[] { C(5, 0) }, board.Cells());
        Assert.False(result.Missed);
        Assert.False(result.Cleared);
    }

    [Fact]
    public void TwoOfAColorStayAndCountAsAMiss()
    {
        var board = new BubbleBoard(BubbleLevel.Normal);
        board.Set(C(3, 0), 2);
        var result = board.Place(C(4, 0), 2);
        Assert.True(result.Missed);
        Assert.Empty(result.Popped);
        Assert.Equal(2, board.Count);
        Assert.Equal(1, board.Misses);
    }

    [Fact]
    public void BubblesHeldByAnotherWayDoNotFall()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(0, 0), 0);
        board.Set(C(1, 0), 0);
        board.Set(C(4, 0), 1);
        board.Set(C(1, 1), 2);   // Touches (1,0) and (2,0)
        board.Set(C(2, 1), 2);   // Touches (2,0) and (3,0)
        board.Set(C(3, 1), 2);   // Touches (3,0) and (4,0): holds the chain up
        var result = board.Place(C(0, 1), 0);
        Assert.Equal(3, result.Popped.Count);
        Assert.Empty(result.Dropped);
        Assert.Equal(4, board.Count);
    }

    [Fact]
    public void PoppingEverythingClearsTheBoard()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(0, 0), 1);
        board.Set(C(1, 0), 1);
        board.Set(C(0, 1), 2);
        var result = board.Place(C(2, 0), 1);
        Assert.Single(result.Dropped);
        Assert.True(result.Cleared);
        Assert.True(board.IsEmpty);
    }

    // ---- Ceiling and flower line ----

    [Fact]
    public void CeilingComesDownAfterMissesWithTwoShotsWarning()
    {
        foreach (var (level, every) in new[] { (BubbleLevel.Normal, 6), (BubbleLevel.Hard, 4) })
        {
            var board = new BubbleBoard(level);
            board.Set(C(0, 0), 0);
            var col = 1;
            var warned = new List<bool>();
            for (var i = 0; i < every; i++)
            {
                warned.Add(board.CeilingWarning);
                var result = board.Place(C(col++, 0), 1 + i % 2 * 2); // Alternating colors: never 3 in a row
                Assert.Equal(i == every - 1, result.CeilingDown);
            }
            Assert.Equal(1, board.Drop);
            Assert.Equal(0, board.Misses);
            // Shakes only for the last two shots before it comes down
            Assert.Equal(Enumerable.Range(0, every).Select(i => every - i <= 2), warned);
        }
    }

    [Fact]
    public void EasyCeilingNeverComesDown()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        for (var i = 0; i < 8; i++)
            board.Place(C(i, 0), i % 2);
        Assert.Equal(0, board.Drop);
        Assert.False(board.CeilingWarning);
    }

    [Fact]
    public void ReachingTheFlowerLineCleansUpInsteadOfLosing()
    {
        var board = new BubbleBoard(BubbleLevel.Hard);
        // A column of bubbles down to slot 9, ceiling 3 rows down
        board.SetCeiling(3, misses: 3);
        board.Set(C(0, 0), 0);
        for (var row = 1; row <= 6; row++)
            board.Set(C(0, row), row % 2 + 1);
        Assert.Equal(9, board.Slot(C(0, 6)));
        // The 4th miss brings the ceiling down: slot 10 touches the flower line
        var result = board.Place(C(1, 0), 4);
        Assert.True(result.CeilingDown);
        Assert.Equal(new[] { C(0, 6) }, result.Cleaned.Select(s => s.Cell));
        Assert.True(result.CeilingUp);
        Assert.Equal(3, board.Drop);
        Assert.Equal(7, board.Count);
        foreach (var c in board.Cells())
            Assert.True(board.Slot(c) < BubbleRules.DangerSlot);
    }

    [Fact]
    public void AShotStuckBelowTheLineIsCleanedWithTheRowsUnderIt()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(0, 0), 0);
        for (var row = 1; row <= 9; row++)
            board.Set(C(0, row), row % 2 + 1);
        var result = board.Place(C(1, 9), 1 + 1);
        // Slot 9 is still above the line: nothing happens
        Assert.Empty(result.Cleaned);
        Assert.False(board.Attachable(C(0, 11)));
        var deeper = board.Place(C(0, 10), 0);
        Assert.Single(deeper.Cleaned);
        Assert.False(deeper.CeilingUp); // The ceiling is already at the top
        Assert.Equal(0, board.Drop);
    }

    // ---- The launcher's queue ----

    [Fact]
    public void NewBubblesOnlyTakeColorsStillOnTheBoard()
    {
        foreach (var level in Enum.GetValues<BubbleLevel>())
            for (var seed = 0; seed < 4; seed++)
            {
                var round = new BubbleRound(BubbleRules.Board(level, seed), new Random(seed));
                var rng = new Random(seed + 100);
                while (!round.Board.IsEmpty && round.Shots < 300)
                {
                    var colors = round.Board.ColorsOnBoard();
                    Assert.Contains(round.Current, colors);
                    Assert.Contains(round.Next, colors);
                    var plan = BubbleRules.BestShot(round);
                    if (rng.Next(4) == 0)
                        round.Swap();
                    round.Land(round.Shoot(BubbleRules.DirectionFromAngle(rng.Next(3) == 0 ? rng.Next(15, 166) : plan.Angle)));
                }
                Assert.True(round.Board.IsEmpty);
            }
    }

    [Fact]
    public void SwapExchangesTheLauncherAndTheNextBubble()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(0, 0), 0);
        board.Set(C(1, 0), 1);
        board.Set(C(2, 0), 2);
        var round = new BubbleRound(board, new Random(3));
        var (a, b) = (round.Current, round.Next);
        round.Swap();
        Assert.Equal((b, a), (round.Current, round.Next));
    }

    [Fact]
    public void ClockStartsWithTheFirstShotAndStopsWhenCleared()
    {
        var board = new BubbleBoard(BubbleLevel.Easy);
        board.Set(C(3, 0), 0);
        board.Set(C(4, 0), 0);
        var round = new BubbleRound(board, new Random(1));
        round.Tick(5);
        Assert.Equal(0, round.TimeMs);
        Assert.Equal(0, round.Current);
        var trace = round.Shoot(BubbleRules.DirectionFromAngle(100f));
        round.Tick(1.5);
        var result = round.Land(trace);
        round.Tick(1);
        Assert.True(result.Cleared);
        Assert.Equal(1500, round.TimeMs);
        var entry = round.Entry("다온", 7);
        Assert.Equal(1, entry.Score);
    }

    // ---- Curated boards ----

    [Theory]
    [MemberData(nameof(AllBoards))]
    public void CuratedBoardsUseTheirPaletteInFullRows(BubbleLevel level, int index)
    {
        var board = BubbleRules.Board(level, index);
        var palette = BubbleRules.Palette(level, BubbleRules.Seed(level, index));
        Assert.Equal(BubbleRules.ColorCount(level), palette.Distinct().Count());
        Assert.Equal(palette.OrderBy(c => c), board.ColorsOnBoard());
        var rows = BubbleRules.StartRows(level);
        Assert.Equal(Enumerable.Range(0, rows).Sum(BubbleRules.CellCount), board.Count);
        Assert.Empty(board.Floating());
        // No color is only a lonely bubble or two: each has at least 3 on the board
        foreach (var color in palette)
            Assert.True(board.Cells().Count(c => board.ColorAt(c) == color) >= 3, $"{level} #{index}: color {color}");
        // No huge group that one shot would wipe out
        Assert.True(board.Cells().Max(c => board.SameColorGroup(c).Count) <= 9, $"{level} #{index}: big group");
        // The same seed gives the same board
        Assert.Equal(board.Cells().Select(board.ColorAt), BubbleRules.Board(level, index).Cells().Select(board.ColorAt));
    }

    [Fact]
    public void BoardsOfALevelUseDifferentColorSets()
    {
        foreach (var level in new[] { BubbleLevel.Easy, BubbleLevel.Normal })
        {
            var sets = Enumerable.Range(0, BubbleRules.BoardCount(level))
                .Select(i => string.Join(",", BubbleRules.Palette(level, BubbleRules.Seed(level, i)).OrderBy(c => c)))
                .Distinct().Count();
            Assert.True(sets >= 3, $"{level}: only {sets} color sets");
        }
    }

    [Theory]
    [MemberData(nameof(AllBoards))]
    public void TheBotClearsEveryCuratedBoard(BubbleLevel level, int index)
    {
        var cap = BubbleRules.StartRows(level) * 12;
        var round = PlayOut(BubbleRules.Board(level, index), index, cap);
        Assert.True(round.Board.IsEmpty, $"{level} #{index}: {round.Board.Count} left after {round.Shots} shots");
    }

    [Theory]
    [MemberData(nameof(AllBoards))]
    public void ACarelessPlayerStillFinishesThanksToTheFlowerLine(BubbleLevel level, int index)
    {
        // Ten careless shots first (they pile up, bring the ceiling down and reach the flower line), then good ones
        var round = PlayOut(BubbleRules.Board(level, index), index + 50, 300, shot => shot < 10);
        Assert.True(round.Board.IsEmpty, $"{level} #{index}: {round.Board.Count} left after {round.Shots} shots");
        foreach (var c in round.Board.Cells())
            Assert.True(round.Board.Slot(c) < BubbleRules.DangerSlot);
    }
}
