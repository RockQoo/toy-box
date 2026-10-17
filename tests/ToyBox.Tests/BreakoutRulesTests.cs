using System.Numerics;
using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class BreakoutRulesTests
{
    private const float Deg = MathF.PI / 180f;

    [Theory]
    [InlineData(0f, 0.70f)]
    [InlineData(19.9f, 0.70f)]
    [InlineData(20f, 0.80f)]
    [InlineData(40f, 0.90f)]
    [InlineData(59.9f, 0.90f)]
    public void SpeedFollowsGddTable(float elapsed, float speed) =>
        Assert.Equal(speed, BreakoutRules.BallSpeed(elapsed, StartSpeed.Normal), 3);

    [Fact]
    public void StartSpeedScales()
    {
        Assert.True(BreakoutRules.BallSpeed(0, StartSpeed.Slow) < BreakoutRules.BallSpeed(0, StartSpeed.Normal));
        Assert.True(BreakoutRules.BallSpeed(0, StartSpeed.Fast) > BreakoutRules.BallSpeed(0, StartSpeed.Normal));
    }

    [Fact]
    public void LayoutsFitTheBoard()
    {
        Assert.Equal(5, BreakoutRules.Layouts.Count);
        foreach (var layout in BreakoutRules.Layouts)
        {
            Assert.InRange(layout.Length, 1, BreakoutRules.MaxRows);
            foreach (var row in layout)
            {
                Assert.Equal(BreakoutRules.Columns, row.Length);
                Assert.All(row, c => Assert.Contains(c, ".123"));
            }
        }
        // Bricks stay inside the board and well above the paddle
        var last = BreakoutRules.BrickX(BreakoutRules.Columns - 1) + BreakoutRules.BrickWidth;
        Assert.True(BreakoutRules.BrickX(0) > 0 && last < BreakoutRules.Width);
        Assert.True(BreakoutRules.BrickY(BreakoutRules.MaxRows) < BreakoutRules.PaddleY - 0.3f);
    }

    [Fact]
    public void BoardsComeInOrderAndGetHarder()
    {
        var first = new BreakoutBoard(0);
        Assert.Equal(24, first.Remaining);
        Assert.All(first.Bricks, b => Assert.Equal(1, b.Level));
        Assert.Equal(new BreakoutBoard(1).Bricks.Count, new BreakoutBoard(6).Bricks.Count); // Shape 2 again
        // Second pass: one level more, capped at 3
        var again = new BreakoutBoard(5);
        Assert.All(again.Bricks, b => Assert.Equal(2, b.Level));
        Assert.All(new BreakoutBoard(14).Bricks, b => Assert.Equal(3, b.Level));
        // Boards get a little harder on the first pass
        Assert.True(new BreakoutBoard(4).Bricks.Average(b => b.Level) > new BreakoutBoard(0).Bricks.Average(b => b.Level));
    }

    [Fact]
    public void BrickBreaksAfterItsLevelInHitsAndDropsAColorEachHit()
    {
        var board = new BreakoutBoard(4); // Top row needs 3 hits
        var brick = board.Bricks.First(b => b.Level == 3);
        var before = board.Remaining;
        Assert.Equal(0, brick.Damage);
        var colors = new List<int> { brick.ColorIndex };
        Assert.False(board.Hit(brick));
        colors.Add(brick.ColorIndex);
        Assert.False(board.Hit(brick));
        colors.Add(brick.ColorIndex);
        Assert.Equal(2, brick.Damage);
        Assert.True(board.Hit(brick));
        Assert.True(brick.Broken);
        Assert.Equal(before - 1, board.Remaining);
        Assert.False(board.Hit(brick)); // Already broken
        Assert.True(colors[0] > colors[1] && colors[1] > colors[2]);
        Assert.Equal(before - 1, board.Remaining);
    }

    [Fact]
    public void ColorsUseAllSixFromGreenToGold()
    {
        var all = new HashSet<int>();
        for (var hits = 1; hits <= 3; hits++)
            for (var row = 0; row < 2; row++)
                all.Add(BreakoutRules.ColorIndex(hits, row));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, all.OrderBy(i => i));
    }

    [Fact]
    public void PaddleBounceDependsOnWhereItHits()
    {
        var x = 0.4f;
        var middle = BreakoutRules.PaddleBounce(x, x);
        Assert.Equal(0f, middle.X, 4);
        Assert.Equal(-1f, middle.Y, 4);

        var right = BreakoutRules.PaddleBounce(x + BreakoutRules.PaddleHitHalf, x);
        Assert.Equal(60f, MathF.Atan2(right.X, -right.Y) / Deg, 2);
        var left = BreakoutRules.PaddleBounce(x - BreakoutRules.PaddleHitHalf * 2, x); // Past the edge: still 60
        Assert.Equal(-60f, MathF.Atan2(left.X, -left.Y) / Deg, 2);

        var half = BreakoutRules.PaddleBounce(x + BreakoutRules.PaddleHitHalf / 2, x);
        Assert.Equal(30f, MathF.Atan2(half.X, -half.Y) / Deg, 2);
    }

    [Fact]
    public void BallBouncesOffThePaddleWhereItLands()
    {
        var hits = new List<BallHit>();
        var paddle = 0.375f;
        var ball = new BreakoutBall(new Vector2(paddle + 0.05f, 0.7f), new Vector2(0, 1));
        BreakoutRules.Advance(ref ball, 0.2f, null, paddle, hits);
        Assert.Contains(hits, h => h.Kind == BallHitKind.Paddle);
        Assert.True(ball.Direction.Y < 0 && ball.Direction.X > 0);
        Assert.True(ball.Position.Y < BreakoutRules.PaddleY);
    }

    [Fact]
    public void PaddleCatchesOver115PercentOfItsWidth()
    {
        var paddle = 0.375f;
        var reach = BreakoutRules.PaddleWidth * 0.55f; // Past the drawn edge (50%), inside 57.5%
        var hits = new List<BallHit>();
        var ball = new BreakoutBall(new Vector2(paddle + reach, 0.7f), new Vector2(0, 1));
        BreakoutRules.Advance(ref ball, 0.25f, null, paddle, hits);
        Assert.Contains(hits, h => h.Kind == BallHitKind.Paddle);

        hits.Clear();
        ball = new BreakoutBall(new Vector2(paddle + 0.2f, 0.7f), new Vector2(0, 1));
        BreakoutRules.Advance(ref ball, 0.5f, null, paddle, hits);
        Assert.DoesNotContain(hits, h => h.Kind == BallHitKind.Paddle);
        Assert.Equal(BallHitKind.Floor, hits[^1].Kind);
    }

    [Fact]
    public void ClampAngleKeepsTheBallFromGoingFlat()
    {
        var min = MathF.Sin(BreakoutRules.MinDegreesFromHorizontal * Deg);
        foreach (var dir in new[] { new Vector2(1, 0), new Vector2(-1, 0.01f), new Vector2(1, -0.05f), new Vector2(0, 0) })
        {
            var c = BreakoutRules.ClampAngle(dir);
            Assert.Equal(1f, c.Length(), 4);
            Assert.True(MathF.Abs(c.Y) >= min - 1e-4f);
        }
        Assert.True(BreakoutRules.ClampAngle(new Vector2(1, 0)).Y < 0); // Flat goes up
        var steep = Vector2.Normalize(new Vector2(0.3f, -1));
        Assert.True(Vector2.Distance(steep, BreakoutRules.ClampAngle(steep)) < 1e-5f); // Steep enough: unchanged
    }

    [Fact]
    public void LaunchGoesUpAndToASide()
    {
        for (var roll = 0.0; roll < 1.0; roll += 0.05)
        {
            var d = BreakoutRules.LaunchDirection(roll);
            var degrees = MathF.Abs(MathF.Atan2(d.X, -d.Y) / Deg);
            Assert.True(d.Y < 0);
            Assert.InRange(degrees, 7.99f, 25.01f);
        }
    }

    [Fact]
    public void BallHitsTheBrickAboveAndComesBackDown()
    {
        var board = new BreakoutBoard(0);
        var target = board.Bricks.First(b => b.Row == 2 && b.Col == 3);
        var ball = new BreakoutBall(new Vector2(target.Center.X, 0.6f), new Vector2(0, -1));
        var hits = new List<BallHit>();
        BreakoutRules.Advance(ref ball, 0.5f, board, 0.375f, hits);
        var brickHit = Assert.Single(hits, h => h.Kind == BallHitKind.Brick);
        Assert.Same(target, brickHit.Brick);
        Assert.True(brickHit.Broke);
        Assert.True(ball.Direction.Y > 0);
        Assert.True(ball.Position.Y > target.Y + target.H);
    }

    [Fact]
    public void FastBallDoesNotPassThroughABrick()
    {
        var board = new BreakoutBoard(0);
        var ball = new BreakoutBall(new Vector2(0.3f, 0.6f), new Vector2(0, -1));
        var hits = new List<BallHit>();
        // One huge frame: further than the whole board is tall
        BreakoutRules.Advance(ref ball, 1.5f, board, 0.375f, hits);
        Assert.Equal(BallHitKind.Brick, hits.First(h => h.Kind != BallHitKind.Wall).Kind);
        Assert.Equal(2, hits.First(h => h.Kind == BallHitKind.Brick).Brick!.Row); // The lowest row first
    }

    [Fact]
    public void WallsAndCeilingBounce()
    {
        var hits = new List<BallHit>();
        var ball = new BreakoutBall(new Vector2(0.1f, 0.5f), Vector2.Normalize(new Vector2(-1, -1)));
        BreakoutRules.Advance(ref ball, 1.2f, null, 0.375f, hits);
        Assert.Contains(hits, h => h.Kind == BallHitKind.Wall);
        Assert.Contains(hits, h => h.Kind == BallHitKind.Ceiling);
        Assert.InRange(ball.Position.X, 0f, BreakoutRules.Width);
        Assert.InRange(ball.Position.Y, 0f, 1f);
    }

    [Fact]
    public void MissedBallStopsAtTheFlowerLine()
    {
        var hits = new List<BallHit>();
        var ball = new BreakoutBall(new Vector2(0.05f, 0.5f), new Vector2(0, 1));
        BreakoutRules.Advance(ref ball, 3f, null, 0.6f, hits);
        Assert.Equal(BallHitKind.Floor, Assert.Single(hits).Kind);
        Assert.True(ball.Position.Y + BreakoutRules.BallRadius >= BreakoutRules.FloorY);
        Assert.True(ball.Position.Y < 1f);
    }

    [Fact]
    public void PredictXFoldsAtTheWalls()
    {
        var r = BreakoutRules.BallRadius;
        Assert.Null(BreakoutRules.PredictX(new Vector2(0.3f, 0.5f), new Vector2(0, -1), 0.8f));
        Assert.Equal(0.3f, BreakoutRules.PredictX(new Vector2(0.3f, 0.5f), new Vector2(0, 1), 0.8f)!.Value, 4);
        // 45 degrees to the right from near the right wall: bounces back
        var x = BreakoutRules.PredictX(new Vector2(0.65f, 0.5f), Vector2.Normalize(new Vector2(1, 1)), 0.8f)!.Value;
        var wall = BreakoutRules.Width - r;
        Assert.Equal(wall - (0.65f + 0.3f - wall), x, 4);

        // Matches the real movement (no bricks, no paddle in the way)
        var ball = new BreakoutBall(new Vector2(0.2f, 0.1f), Vector2.Normalize(new Vector2(-0.8f, 0.4f)));
        var predicted = BreakoutRules.PredictX(ball.Position, ball.Direction, 0.8f)!.Value;
        var hits = new List<BallHit>();
        while (ball.Position.Y < 0.8f)
            BreakoutRules.Advance(ref ball, 0.001f, null, 100f, hits);
        Assert.Equal(predicted, ball.Position.X, 2);
    }

    [Fact]
    public void ItemDropsSometimesAndSplitsIntoThree()
    {
        Assert.True(BreakoutRules.DropsItem(1, false, 0.0));
        Assert.False(BreakoutRules.DropsItem(1, true, 0.0));        // One at a time
        Assert.False(BreakoutRules.DropsItem(4, false, 0.0));       // Would pass the ball limit
        Assert.False(BreakoutRules.DropsItem(1, false, 0.5));
        var (l, r) = BreakoutRules.SplitDirections(new Vector2(0, -1));
        Assert.True(l.X < 0 && r.X > 0 && l.Y < 0 && r.Y < 0);
        Assert.True(BreakoutRules.PaddleCatches(0.4f, BreakoutRules.PaddleY, 0.4f));
        Assert.False(BreakoutRules.PaddleCatches(0.4f, 0.5f, 0.4f));
        Assert.False(BreakoutRules.PaddleCatches(0.1f, BreakoutRules.PaddleY, 0.6f));
    }

    /// <summary>
    /// Fairness: a bot that keeps the ball in play (hitting the paddle at varied spots, as a child would) clears every
    /// board shape, both passes, at the fastest speed. No brick is out of reach and the ball never escapes the board.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    public void EveryBoardCanBeCleared(int number)
    {
        var rng = new Random(number + 11);
        var board = new BreakoutBoard(number);
        var paddle = BreakoutRules.Width / 2;
        var ball = new BreakoutBall(BreakoutRules.RestPosition(paddle), BreakoutRules.LaunchDirection(rng.NextDouble()));
        var offset = 0f;
        var hits = new List<BallHit>();
        const float dt = 1f / 60f;
        var speed = BreakoutRules.BallSpeed(59f, StartSpeed.Fast);
        var time = 0f;
        while (!board.Cleared && time < 240f)
        {
            if (BreakoutRules.PredictX(ball.Position, ball.Direction, BreakoutRules.PaddleY) is { } land)
                paddle = BreakoutRules.ClampPaddle(land - offset * BreakoutRules.PaddleHitHalf);
            hits.Clear();
            BreakoutRules.Advance(ref ball, speed * dt, board, paddle, hits);
            time += dt;
            Assert.InRange(ball.Position.X, 0f, BreakoutRules.Width);
            Assert.InRange(ball.Position.Y, 0f, 1f);
            foreach (var h in hits)
            {
                if (h.Kind == BallHitKind.Paddle)
                    offset = (float)(rng.NextDouble() * 1.6 - 0.8);
                Assert.NotEqual(BallHitKind.Floor, h.Kind); // The bot never misses
            }
        }
        Assert.True(board.Cleared, $"board {number}: {board.Remaining} left after {time:0}s");
    }
}
