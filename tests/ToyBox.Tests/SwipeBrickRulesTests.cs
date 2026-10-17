using System.Numerics;
using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class SwipeBrickRulesTests
{
    private static int RowBricks(SwipeBrickBoard b, int row)
    {
        var n = 0;
        for (var c = 0; c < SwipeBrickRules.Columns; c++)
            if (b.HitsAt(c, row) > 0)
                n++;
        return n;
    }

    private static int RowItems(SwipeBrickBoard b, int row)
    {
        var n = 0;
        for (var c = 0; c < SwipeBrickRules.Columns; c++)
            if (b.ItemAt(c, row))
                n++;
        return n;
    }

    [Fact]
    public void BoardFitsTheScreenWithRoomForTheLauncher()
    {
        Assert.Equal(7, SwipeBrickRules.Columns);
        Assert.Equal(9, SwipeBrickRules.Rows);
        Assert.InRange(SwipeBrickRules.FloorY, 0.8f, 0.92f);
        Assert.Equal(new[] { "easy", "normal" }, SwipeBrickRules.Variants.Select(v => v.Id));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(10, 5)]
    [InlineData(20, 10)]
    public void EasyRowsAreHalfTheTurn(int turn, int value) =>
        Assert.Equal(value, SwipeBrickRules.RowValue(SwipeLevel.Easy, turn, new Random(1)));

    [Fact]
    public void NormalRowsAreTheTurnSometimesDoubled()
    {
        var rng = new Random(3);
        var doubled = 0;
        for (var i = 0; i < 1000; i++)
        {
            var v = SwipeBrickRules.RowValue(SwipeLevel.Normal, 7, rng);
            Assert.True(v is 7 or 14);
            if (v == 14)
                doubled++;
        }
        Assert.InRange(doubled, 100, 300);
    }

    [Theory]
    [InlineData(SwipeLevel.Easy, 2, 4)]
    [InlineData(SwipeLevel.Normal, 3, 5)]
    public void NewRowHasBricksAndABubble(SwipeLevel level, int min, int max)
    {
        var rng = new Random(5);
        var seen = new HashSet<int>();
        for (var i = 0; i < 300; i++)
        {
            var b = new SwipeBrickBoard(level);
            b.SpawnRow(rng);
            Assert.Equal(1, b.Turn);
            var n = RowBricks(b, 0);
            Assert.InRange(n, min, max);
            seen.Add(n);
            Assert.Equal(1, RowItems(b, 0));
            for (var row = 1; row < SwipeBrickRules.Rows; row++)
                Assert.Equal(0, RowBricks(b, row) + RowItems(b, row));
        }
        Assert.Equal(max - min + 1, seen.Count);
    }

    [Fact]
    public void ColorsFollowTheNumberRanges()
    {
        Assert.Equal(0, SwipeBrickRules.ColorIndex(1));
        Assert.Equal(0, SwipeBrickRules.ColorIndex(4));
        Assert.Equal(1, SwipeBrickRules.ColorIndex(5));
        Assert.Equal(2, SwipeBrickRules.ColorIndex(10));
        Assert.Equal(3, SwipeBrickRules.ColorIndex(29));
        Assert.Equal(4, SwipeBrickRules.ColorIndex(30));
        Assert.Equal(5, SwipeBrickRules.ColorIndex(50));
    }

    [Fact]
    public void CracksGrowAsTheNumberDrops()
    {
        Assert.Equal(0, SwipeBrickRules.Damage(1, 1));
        Assert.Equal(1, SwipeBrickRules.Damage(2, 1));
        Assert.Equal(0, SwipeBrickRules.Damage(9, 7));
        Assert.Equal(1, SwipeBrickRules.Damage(9, 6));
        Assert.Equal(2, SwipeBrickRules.Damage(9, 3));
    }

    [Fact]
    public void ShiftMovesEverythingDownAndTheLastRowTurnsIntoFlowers()
    {
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        b.SetBrick(2, 0, 5);
        b.SetItem(4, 3, true);
        b.SetBrick(1, SwipeBrickRules.Rows - 1, 7);
        b.SetItem(6, SwipeBrickRules.Rows - 1, true);
        var flowers = new List<(int Col, bool Brick)>();
        b.ShiftDown(flowers);
        Assert.Equal(5, b.HitsAt(2, 1));
        Assert.True(b.ItemAt(4, 4));
        Assert.Equal(0, b.HitsAt(2, 0));
        Assert.Contains((1, true), flowers);
        Assert.Contains((6, false), flowers);
        Assert.Equal(2, flowers.Count);
        Assert.Equal(1, b.BrickCount);
        Assert.Equal(0, b.Score); // Flowers give no points
    }

    [Fact]
    public void HitsCountDownAndBreakingScores()
    {
        var b = new SwipeBrickBoard(SwipeLevel.Easy);
        b.SetBrick(3, 2, 2);
        Assert.False(b.HitBrick(3, 2));
        Assert.Equal(1, b.HitsAt(3, 2));
        Assert.True(b.HitBrick(3, 2));
        Assert.Equal(0, b.HitsAt(3, 2));
        Assert.Equal(1, b.Score);
        Assert.False(b.HitBrick(3, 2));
    }

    [Theory]
    [InlineData(5f, false)]
    [InlineData(9f, false)]
    [InlineData(10f, true)]
    [InlineData(90f, true)]
    [InlineData(170f, true)]
    [InlineData(175f, false)]
    [InlineData(-30f, false)]
    public void TooLowAimsAreNotFired(float degrees, bool fire) =>
        Assert.Equal(fire, SwipeBrickRules.CanFire(SwipeBrickRules.DirectionFromAngle(degrees)));

    [Fact]
    public void AimPointsTowardTheFinger()
    {
        var x = SwipeBrickRules.Width / 2;
        var dir = SwipeBrickRules.AimToward(x, new Vector2(x, 0.2f))!.Value;
        Assert.Equal(90f, SwipeBrickRules.AngleOf(dir), 2);
        var right = SwipeBrickRules.AimToward(x, new Vector2(x + 0.3f, SwipeBrickRules.LaunchY - 0.3f))!.Value;
        Assert.Equal(45f, SwipeBrickRules.AngleOf(right), 2);
        Assert.Null(SwipeBrickRules.AimToward(x, new Vector2(x, SwipeBrickRules.LaunchY)));
    }

    [Fact]
    public void BallsLeaveOneAfterAnotherAndAllComeBack()
    {
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        var shot = new SwipeShot(b, 0.3f, SwipeBrickRules.DirectionFromAngle(70f), 5);
        var events = new List<SwipeEvent>();
        var launchTimes = new List<float>();
        while (!shot.Done && shot.Time < 30f)
        {
            events.Clear();
            var t = shot.Time;
            shot.Tick(events);
            foreach (var e in events)
                if (e.Kind == SwipeEventKind.Launch)
                    launchTimes.Add(t);
        }
        Assert.True(shot.Done);
        Assert.Equal(5, launchTimes.Count);
        for (var i = 1; i < launchTimes.Count; i++)
            Assert.InRange(launchTimes[i] - launchTimes[i - 1], SwipeBrickRules.LaunchGap - 0.01f, SwipeBrickRules.LaunchGap + 0.01f);
        Assert.NotNull(shot.FirstLandX);
        Assert.InRange(shot.FirstLandX!.Value, SwipeBrickRules.BallRadius, SwipeBrickRules.Width - SwipeBrickRules.BallRadius);
        Assert.Equal(1, b.ShotsTaken);
    }

    [Fact]
    public void BallBreaksTheBrickAboveAndPicksUpBubbles()
    {
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        b.SetBrick(3, 2, 3);
        b.SetItem(3, 5, true);
        var x = SwipeBrickRules.CellCenter(3, 0).X;
        var shot = new SwipeShot(b, x, SwipeBrickRules.DirectionFromAngle(90f), 3);
        var events = new List<SwipeEvent>();
        shot.RunToEnd(30f, events);
        Assert.True(shot.Done);
        Assert.Equal(0, b.HitsAt(3, 2));
        Assert.Equal(1, b.Score);
        Assert.Equal(2, b.Balls);
        Assert.Contains(events, e => e.Kind == SwipeEventKind.Brick && e.Broke);
        Assert.Single(events, e => e.Kind == SwipeEventKind.Item);
    }

    [Fact]
    public void BouncesNeverGoFlat()
    {
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        var rng = new Random(9);
        for (var row = 0; row < 6; row++)
            for (var col = 0; col < SwipeBrickRules.Columns; col++)
                if (rng.Next(2) == 0)
                    b.SetBrick(col, row, 50);
        var minY = MathF.Sin(SwipeBrickRules.MinTravelDegrees * MathF.PI / 180f) - 1e-4f;
        foreach (var angle in new[] { 11f, 33f, 61f, 89f, 121f, 168f })
        {
            var shot = new SwipeShot(b.Clone(), 0.2f, SwipeBrickRules.DirectionFromAngle(angle), 1);
            var events = new List<SwipeEvent>();
            for (var i = 0; i < 2000 && !shot.Done; i++)
            {
                shot.Tick(events);
                Assert.True(MathF.Abs(shot.DirectionOf(0).Y) >= minY, $"angle {angle}: {shot.DirectionOf(0)}");
            }
        }
    }

    [Fact]
    public void TrappedBallsAreCalledBackSoEveryShotEnds()
    {
        // A ceiling of very strong bricks with one gap: balls that get above can bounce a long time
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        for (var col = 0; col < SwipeBrickRules.Columns; col++)
            if (col != 3)
                b.SetBrick(col, 4, 999);
        var shot = new SwipeShot(b, SwipeBrickRules.CellCenter(3, 0).X, SwipeBrickRules.DirectionFromAngle(88f), 10);
        shot.RunToEnd(SwipeBrickRules.RecallTime + 3f);
        Assert.True(shot.Done);
    }

    [Fact]
    public void AimLineEndsWhereTheBallFirstBounces()
    {
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        b.SetBrick(2, 3, 10);
        b.SetBrick(5, 1, 10);
        foreach (var angle in new[] { 30f, 60f, 80f, 100f, 135f })
        {
            var dir = SwipeBrickRules.DirectionFromAngle(angle);
            var (end, _) = SwipeBrickRules.PredictFirstBounce(b, 0.3f, dir);
            var shot = new SwipeShot(b.Clone(), 0.3f, dir, 1);
            var events = new List<SwipeEvent>();
            while (!shot.Done && !events.Exists(e => e.Kind is SwipeEventKind.Wall or SwipeEventKind.Brick))
                shot.Tick(events);
            var first = events.Find(e => e.Kind is SwipeEventKind.Wall or SwipeEventKind.Brick);
            // The predicted ball center is within a ball radius of the real touch point
            Assert.True(Vector2.Distance(end, first.At) <= SwipeBrickRules.BallRadius * 1.6f,
                $"angle {angle}: {end} vs {first.At}");
        }
    }

    [Theory]
    [InlineData(SwipeLevel.Easy, 1)]
    [InlineData(SwipeLevel.Easy, 2)]
    [InlineData(SwipeLevel.Normal, 3)]
    [InlineData(SwipeLevel.Normal, 4)]
    public void PlannerPlaysAWholeRound(SwipeLevel level, int seed)
    {
        var rng = new Random(seed);
        var b = new SwipeBrickBoard(level);
        b.SpawnRow(rng);
        var launchX = SwipeBrickRules.Width / 2;
        var flowers = new List<(int Col, bool Brick)>();
        while (b.ShotsLeft > 0)
        {
            var angle = SwipeBrickRules.BestAngle(b, launchX);
            Assert.True(SwipeBrickRules.CanFire(SwipeBrickRules.DirectionFromAngle(angle)));
            var shot = new SwipeShot(b, launchX, SwipeBrickRules.DirectionFromAngle(angle), b.Balls);
            shot.RunToEnd(SwipeBrickRules.RecallTime + 5f);
            Assert.True(shot.Done);
            Assert.True(shot.Time < SwipeBrickRules.RecallTime + 3f);
            launchX = shot.FirstLandX!.Value;
            if (b.ShotsLeft > 0)
            {
                b.ShiftDown(flowers);
                b.SpawnRow(rng);
            }
        }
        Assert.Equal(SwipeBrickRules.Shots, b.ShotsTaken);
        Assert.Equal(SwipeBrickRules.Shots, b.Turn);
        Assert.InRange(b.Balls, 2, SwipeBrickRules.MaxBalls);
        Assert.True(b.Score >= 15, $"score {b.Score}");
    }

    [Fact]
    public void WeakestPlanBreaksNoMoreThanTheBest()
    {
        var rng = new Random(11);
        var b = new SwipeBrickBoard(SwipeLevel.Normal);
        for (var i = 0; i < 4; i++)
        {
            b.ShiftDown();
            b.SpawnRow(rng);
        }
        int Broken(float angle)
        {
            var copy = b.Clone();
            new SwipeShot(copy, 0.3f, SwipeBrickRules.DirectionFromAngle(angle), copy.Balls).RunToEnd();
            return copy.Score;
        }
        Assert.True(Broken(SwipeBrickRules.BestAngle(b, 0.3f, fewest: true)) <= Broken(SwipeBrickRules.BestAngle(b, 0.3f)));
    }
}
