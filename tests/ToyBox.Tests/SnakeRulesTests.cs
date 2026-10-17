using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class SnakeRulesTests
{
    private static Func<int, int> First => _ => 0;

    [Theory]
    [InlineData(0f, 4f)]
    [InlineData(19.9f, 4f)]
    [InlineData(20f, 5f)]
    [InlineData(40f, 6f)]
    [InlineData(59.9f, 6f)]
    public void SpeedFollowsGddTable(float elapsed, float cells) =>
        Assert.Equal(cells, SnakeRules.CellsPerSecond(elapsed, StartSpeed.Normal), 3);

    [Fact]
    public void StartSpeedScales()
    {
        Assert.True(SnakeRules.StepInterval(0, StartSpeed.Slow) > SnakeRules.StepInterval(0, StartSpeed.Normal));
        Assert.True(SnakeRules.StepInterval(0, StartSpeed.Fast) < SnakeRules.StepInterval(0, StartSpeed.Normal));
    }

    [Fact]
    public void WaitsForTheFirstDirection()
    {
        var board = new SnakeBoard();
        var head = board.Head;
        Assert.Equal(SnakeMoveKind.Waiting, board.Step().Kind);
        Assert.Equal(head, board.Head);
        Assert.True(board.Turn(MazeDir.Up));
        Assert.Equal(SnakeMoveKind.Moved, board.Step().Kind);
        Assert.Equal(head.Step(MazeDir.Up), board.Head);
        Assert.True(board.Started);
        // Keeps going the same way without input
        board.Step();
        Assert.Equal(head.X, board.Head.X);
        Assert.Equal(head.Y - 2, board.Head.Y);
        Assert.Equal(SnakeRules.StartLength, board.Length);
    }

    [Fact]
    public void ReverseIsIgnoredWhileMoving()
    {
        var board = new SnakeBoard();
        board.Turn(MazeDir.Right);
        board.Step();
        Assert.False(board.Turn(MazeDir.Left));
        Assert.False(board.Turn(MazeDir.Right)); // Same direction: nothing to queue
        board.Step();
        Assert.Equal(MazeDir.Right, board.Facing);
    }

    [Fact]
    public void TwoQuickTurnsAreQueued()
    {
        var board = new SnakeBoard(head: new MazeCell(5, 4));
        board.Turn(MazeDir.Right);
        board.Step();
        // ㄱ-turn between two steps: up, then left (left is not the reverse of up)
        Assert.True(board.Turn(MazeDir.Up));
        Assert.True(board.Turn(MazeDir.Left));
        Assert.False(board.Turn(MazeDir.Down)); // Queue full
        board.Step();
        Assert.Equal(new MazeCell(6, 3), board.Head);
        board.Step();
        Assert.Equal(new MazeCell(5, 3), board.Head);
        Assert.Equal(MazeDir.Left, board.Facing);
    }

    [Fact]
    public void QueuedReverseOfQueuedTurnIsIgnored()
    {
        var board = new SnakeBoard();
        board.Turn(MazeDir.Right);
        board.Step();
        Assert.True(board.Turn(MazeDir.Up));
        Assert.False(board.Turn(MazeDir.Down));
    }

    [Fact]
    public void WallStopsAndWaitsForANewDirection()
    {
        var board = new SnakeBoard(head: new MazeCell(SnakeRules.Width - 2, 4));
        board.Turn(MazeDir.Right);
        Assert.Equal(SnakeMoveKind.Moved, board.Step().Kind);
        var head = board.Head;
        Assert.Equal(SnakeMoveKind.Bumped, board.Step().Kind);
        Assert.Equal(head, board.Head);
        Assert.False(board.Moving);
        Assert.Equal(SnakeMoveKind.Waiting, board.Step().Kind);
        Assert.Equal(head, board.Head);
        // Pressing into the wall again bumps again; a new direction goes on
        Assert.True(board.Turn(MazeDir.Right));
        Assert.Equal(SnakeMoveKind.Bumped, board.Step().Kind);
        Assert.True(board.Turn(MazeDir.Down));
        Assert.Equal(SnakeMoveKind.Moved, board.Step().Kind);
        Assert.Equal(head.Step(MazeDir.Down), board.Head);
        Assert.Equal(0, board.Score);
    }

    [Fact]
    public void StoppedAtAWallItMayTurnBack()
    {
        var board = new SnakeBoard(head: new MazeCell(SnakeRules.Width - 1, 4));
        board.Turn(MazeDir.Right);
        Assert.Equal(SnakeMoveKind.Bumped, board.Step().Kind);
        Assert.True(board.Turn(MazeDir.Left));
        Assert.Equal(SnakeMoveKind.Moved, board.Step().Kind);
        Assert.Equal(new MazeCell(SnakeRules.Width - 2, 4), board.Head);
    }

    [Fact]
    public void CrawlsOverItsOwnBody()
    {
        var board = new SnakeBoard(width: 10, height: 10, length: 5, head: new MazeCell(5, 5));
        // Body: (5,5) (4,5) (3,5) (2,5) (1,5). Turn down, left, up: the head comes back onto the body
        board.Turn(MazeDir.Down);
        board.Step();
        board.Turn(MazeDir.Left);
        board.Step();
        board.Turn(MazeDir.Up);
        var step = board.Step();
        Assert.Equal(SnakeMoveKind.Moved, step.Kind);
        Assert.Equal(new MazeCell(4, 5), board.Head);
        Assert.Contains(new MazeCell(4, 5), board.Body.Skip(1));
        board.Step();
        Assert.Equal(new MazeCell(4, 4), board.Head);
    }

    [Fact]
    public void EatingGrowsAndScores()
    {
        var board = new SnakeBoard(width: 4, height: 1, length: 1, head: new MazeCell(0, 0));
        board.AddLeaf(false, First); // Only (3,0) is 3+ cells away
        Assert.Equal(new MazeCell(3, 0), board.Leaves[0].Cell);
        board.Turn(MazeDir.Right);
        board.Step();
        board.Step();
        var step = board.Step();
        Assert.Equal(SnakeMoveKind.Ate, step.Kind);
        Assert.Equal(1, step.Points);
        Assert.True(step.Grew);
        Assert.Equal(1, board.Score);
        Assert.Equal(2, board.Length);
        Assert.Empty(board.Leaves);
        Assert.Equal(2, board.LongestLength);
    }

    [Fact]
    public void GoldenLeafIsWorthThree()
    {
        var board = new SnakeBoard(width: 4, height: 1, length: 1, head: new MazeCell(0, 0));
        board.AddLeaf(true, First);
        Assert.True(board.HasGolden);
        board.Turn(MazeDir.Right);
        board.Step();
        board.Step();
        var step = board.Step();
        Assert.Equal(3, step.Points);
        Assert.Equal(3, board.Score);
        Assert.Equal(1, board.LeavesEaten);
        Assert.False(board.HasGolden);
    }

    [Fact]
    public void GoldenLeafCanBeTakenAway()
    {
        var board = new SnakeBoard();
        board.AddLeaf(true, First);
        board.AddLeaf(false, First);
        Assert.True(board.RemoveGolden());
        Assert.Single(board.Leaves);
        Assert.False(board.RemoveGolden());
    }

    [Fact]
    public void LengthStopsAtMaxButScoreGoesOn()
    {
        var board = new SnakeBoard(width: 60, height: 1, length: SnakeRules.MaxLength - 1, head: new MazeCell(40, 0));
        board.Turn(MazeDir.Right);
        for (var i = 0; i < 5; i++)
        {
            // A leaf right in front every step
            Assert.True(board.AddLeafAt(board.Head.Step(MazeDir.Right), false));
            Assert.Equal(SnakeMoveKind.Ate, board.Step().Kind);
        }
        Assert.Equal(5, board.Score);
        Assert.Equal(SnakeRules.MaxLength, board.Length);
        Assert.Equal(SnakeRules.MaxLength, board.LongestLength);
    }

    [Fact]
    public void LeavesGoOnFreeCellsAwayFromTheHead()
    {
        var board = new SnakeBoard();
        var rng = new Random(7);
        for (var i = 0; i < 50; i++)
        {
            var leaf = board.AddLeaf(false, rng.Next)!.Value;
            Assert.False(board.Occupied(leaf.Cell));
            Assert.True(board.InBounds(leaf.Cell));
            Assert.True(leaf.Cell.ManhattanTo(board.Head) >= 3);
        }
        // No two leaves on one cell
        Assert.Equal(board.Leaves.Count, board.Leaves.Select(l => l.Cell).Distinct().Count());
    }

    [Fact]
    public void FullBoardGivesNoLeaf()
    {
        var board = new SnakeBoard(width: 2, height: 1, length: 2, head: new MazeCell(1, 0));
        Assert.Null(board.AddLeaf(false, First));
    }

    [Fact]
    public void SecondLeafFromThirtySeconds()
    {
        var board = new SnakeBoard();
        var rng = new Random(3);
        Assert.Single(board.FillLeaves(0f, rng.Next));
        Assert.Empty(board.FillLeaves(10f, rng.Next));
        Assert.Single(board.FillLeaves(SnakeRules.SecondLeafTime, rng.Next));
        Assert.Equal(2, board.PlainLeafCount);
        // A golden leaf does not count toward the plain ones
        board.AddLeaf(true, rng.Next);
        Assert.Empty(board.FillLeaves(45f, rng.Next));
    }

    [Fact]
    public void GoldenTimingAndMilestones()
    {
        Assert.InRange(SnakeRules.GoldenDelay(0), 5f, 15f);
        Assert.InRange(SnakeRules.GoldenDelay(0.999), 5f, 15f);
        Assert.Equal(1, SnakeRules.Points(false));
        Assert.Equal(3, SnakeRules.Points(true));
        Assert.True(SnakeRules.CrossedMilestone(9, 10));
        Assert.True(SnakeRules.CrossedMilestone(8, 11));
        Assert.False(SnakeRules.CrossedMilestone(10, 11));
    }

    [Fact]
    public void AutoplayReachesEveryLeafWithoutReversing()
    {
        var rng = new Random(11);
        var board = new SnakeBoard();
        board.FillLeaves(0, rng.Next);
        var eaten = 0;
        for (var step = 0; step < 2000 && eaten < 30; step++)
        {
            var before = board.Facing;
            var moving = board.Moving;
            if (board.DirToward(board.Leaves[0].Cell) is { } dir)
            {
                Assert.True(board.Turn(dir));
                if (moving)
                    Assert.False(SnakeRules.IsOpposite(before, dir));
            }
            var result = board.Step();
            Assert.NotEqual(SnakeMoveKind.Bumped, result.Kind);
            if (result.Kind == SnakeMoveKind.Ate)
            {
                eaten++;
                board.FillLeaves(0, rng.Next);
            }
        }
        Assert.Equal(30, eaten);
        Assert.Equal(SnakeRules.StartLength + 30, board.Length);
    }

    [Fact]
    public void OppositeDirections()
    {
        Assert.Equal(MazeDir.Down, SnakeRules.Opposite(MazeDir.Up));
        Assert.Equal(MazeDir.Left, SnakeRules.Opposite(MazeDir.Right));
        Assert.True(SnakeRules.IsOpposite(MazeDir.Left, MazeDir.Right));
        Assert.False(SnakeRules.IsOpposite(MazeDir.Left, MazeDir.Up));
    }
}
