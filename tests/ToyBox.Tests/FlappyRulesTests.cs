using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class FlappyRulesTests
{
    private static readonly FlappyLevel[] Levels = { FlappyLevel.Easy, FlappyLevel.Normal, FlappyLevel.Hard };
    private static readonly StartSpeed[] Starts = { StartSpeed.Slow, StartSpeed.Normal, StartSpeed.Fast };

    [Theory]
    [InlineData(FlappyLevel.Easy, 0.42f, 0.20f)]
    [InlineData(FlappyLevel.Normal, 0.36f, 0.28f)]
    [InlineData(FlappyLevel.Hard, 0.31f, 0.35f)]
    public void GapTableFollowsGdd(FlappyLevel level, float gap, float diff)
    {
        Assert.Equal(gap, FlappyRules.GapSize(level), 4);
        Assert.Equal(diff, FlappyRules.MaxDiff(level), 4);
        Assert.Equal(new[] { "easy", "normal", "hard" }, FlappyRules.Variants.Select(v => v.Id));
        Assert.Equal("hard", FlappyRules.VariantId(FlappyLevel.Hard));
    }

    [Fact]
    public void FlapRisesAndGravityPullsDown()
    {
        var bird = new FlappyBird();
        // Hovers until the first flap
        for (var i = 0; i < 60; i++)
            bird.Step(1 / 60f);
        Assert.Equal(FlappyRules.StartY, bird.Y, 4);
        Assert.False(bird.Started);

        bird.Flap();
        Assert.True(bird.Started);
        var start = bird.Y;
        var top = start;
        for (var t = 0f; t < 0.6f; t += 1 / 240f)
        {
            bird.Step(1 / 240f);
            top = Math.Min(top, bird.Y);
        }
        Assert.Equal(FlappyRules.FlapHeight, start - top, 2);
        Assert.True(bird.Y > top); // Coming down again
        // Falling never gets faster than the limit
        for (var i = 0; i < 30; i++)
            bird.Step(1 / 60f);
        Assert.True(bird.Velocity <= FlappyRules.MaxFallSpeed + 1e-5f);
    }

    [Fact]
    public void GentlerThanTheOriginal()
    {
        // One flap is a clear lift but well under the smallest gap; falls are slow enough to follow
        Assert.InRange(FlappyRules.FlapHeight, 0.10f, 0.16f);
        Assert.True(FlappyRules.MaxFallSpeed < 1f);
        Assert.True(FlappyRules.HitRadius * 2 < FlappyRules.BirdWidth * 0.6f);
    }

    [Fact]
    public void CeilingAndGroundNeverEndTheRun()
    {
        var bird = new FlappyBird();
        var sawCeiling = false;
        for (var i = 0; i < 400; i++)
        {
            bird.Flap();
            sawCeiling |= bird.Step(1 / 60f) == FlightEvent.Ceiling;
            Assert.True(bird.Y >= FlappyRules.BodyRadius - 1e-5f);
        }
        Assert.True(sawCeiling);

        var bounces = 0;
        for (var i = 0; i < 600; i++)
        {
            if (bird.Step(1 / 60f) == FlightEvent.Bounced)
                bounces++;
            Assert.True(bird.Y <= FlappyRules.GroundY - FlappyRules.BodyRadius + 1e-5f);
        }
        Assert.True(bounces > 3); // Keeps hopping along the ground
        bird.Flap();
        bird.Step(1 / 60f);
        Assert.True(bird.Velocity < 0); // And takes off again
    }

    [Fact]
    public void GapsStayOnScreenAndChangeWithinTheLimit()
    {
        var rng = new Random(3);
        foreach (var level in Levels)
        {
            FlappyGap? previous = null;
            for (var i = 0; i < 400; i++)
            {
                var gap = FlappyRules.NextGap(level, previous, rng.NextDouble());
                Assert.Equal(i, gap.Index);
                Assert.True(gap.Top >= FlappyRules.EdgeMargin - 1e-5f, $"{level} #{i} top {gap.Top}");
                Assert.True(gap.Bottom <= FlappyRules.GroundY - FlappyRules.EdgeMargin + 1e-5f, $"{level} #{i} bottom");
                if (previous is { } p)
                    Assert.True(Math.Abs(gap.Center - p.Center) <= FlappyRules.MaxDiffAt(level, i) + 1e-5f);
                previous = gap;
            }
        }
    }

    [Fact]
    public void WarmupPillarsAreWiderAndCalmer()
    {
        foreach (var level in Levels)
        {
            var first = FlappyRules.NextGap(level, null, 0.5);
            Assert.Equal(FlappyRules.StartY, first.Center, 4);
            Assert.Equal(FlappyRules.GapSize(level) * 1.3f, first.Size, 4);
            Assert.Equal(FlappyRules.GapSize(level) * 1.3f, FlappyRules.GapSizeAt(level, 2), 4);
            Assert.Equal(FlappyRules.GapSize(level), FlappyRules.GapSizeAt(level, 3), 4);
            Assert.True(FlappyRules.MaxDiffAt(level, 1) < FlappyRules.MaxDiff(level) * 0.5f);
        }
    }

    [Fact]
    public void OneFlapFitsInsideEveryGap()
    {
        // Steady flapping inside a gap moves the bird up and down by one flap: that has to fit with room to spare
        foreach (var level in Levels)
            Assert.True(FlappyRules.FlapHeight + 2 * FlappyRules.HitRadius < FlappyRules.GapSize(level) * 0.75f, level.ToString());
    }

    [Theory]
    [InlineData(0, 1.0f)]
    [InlineData(9, 1.0f)]
    [InlineData(10, 1.1f)]
    [InlineData(25, 1.2f)]
    [InlineData(30, 1.3f)]
    [InlineData(99, 1.3f)]
    public void SpeedStepsEveryTenUntilThirty(int score, float multiplier) =>
        Assert.Equal(multiplier, FlappyRules.SpeedMultiplier(score), 3);

    [Fact]
    public void SpeedEasesIntoANewStep()
    {
        var mid = FlappyRules.SpeedMultiplier(10, FlappyRules.RampSeconds / 2);
        Assert.InRange(mid, 1.02f, 1.08f);
        Assert.Equal(1.0f, FlappyRules.SpeedMultiplier(10, 0f), 3);
        Assert.True(FlappyRules.Speed(0, StartSpeed.Fast) > FlappyRules.Speed(0, StartSpeed.Slow));
        Assert.True(FlappyRules.PlanningSpeed(8, StartSpeed.Normal) >= FlappyRules.Speed(10, StartSpeed.Normal));
    }

    [Fact]
    public void SpacingAlwaysLeavesTimeToReachTheNextGap()
    {
        var rng = new Random(11);
        foreach (var level in Levels)
        foreach (var speed in new[] { FlappyRules.SlowestSpeed, FlappyRules.BaseSpeed, FlappyRules.FastestSpeed })
        {
            FlappyGap? previous = null;
            for (var i = 0; i < 300; i++)
            {
                var gap = FlappyRules.NextGap(level, previous, rng.NextDouble());
                if (previous is { } p)
                {
                    var spacing = FlappyRules.Spacing(speed, p, gap);
                    var free = (spacing - FlappyRules.TrunkWidth - 2 * FlappyRules.HitRadius) / speed;
                    Assert.True(free >= FlappyRules.Reaction + FlappyRules.TravelTime(p, gap) - 1e-4f);
                    Assert.True(spacing >= FlappyRules.BaseSpacing);
                }
                previous = gap;
            }
        }
        // Worst case at the fastest speed: the largest drop and climb still fit
        var high = new FlappyGap(5, 0.3f, FlappyRules.GapSize(FlappyLevel.Hard));
        var low = new FlappyGap(6, 0.65f, FlappyRules.GapSize(FlappyLevel.Hard));
        Assert.True(FlappyRules.Spacing(FlappyRules.FastestSpeed, low, high) > FlappyRules.BaseSpacing);
    }

    [Fact]
    public void ACarefulChildCanFlyThroughEveryLevelAtEverySpeed()
    {
        // A bot that flaps no faster than once per arc and waits a reaction time after each pillar before heading for
        // the next gap, on pillars spaced by the rules, never hits one.
        foreach (var level in Levels)
        foreach (var start in Starts)
        for (var seed = 0; seed < 12; seed++)
        {
            var passed = Fly(level, start, new Random(seed * 31 + (int)level), 80, flaps: true);
            Assert.True(passed == 80, $"{level} {start} seed {seed}: crashed after {passed}");
        }
    }

    [Fact]
    public void NotFlappingHitsAPillar()
    {
        Assert.True(Fly(FlappyLevel.Easy, StartSpeed.Normal, new Random(1), 10, flaps: false) < 10);
    }

    /// <summary>Flies a run like the game does; returns how many pillars were passed before the first hit.</summary>
    private static int Fly(FlappyLevel level, StartSpeed start, Random rng, int count, bool flaps)
    {
        const float dt = 1 / 120f;
        const float birdX = 0f;
        var bird = new FlappyBird();
        var pillars = new List<(float Left, FlappyGap Gap)>();
        FlappyGap? last = null;
        var nextLeft = 1.2f;
        var score = 0;
        var sinceFlap = 10f;
        var sincePass = 10f;
        var touching = 0f;
        var holdY = FlappyRules.StartY;
        bird.Flap();
        for (var t = 0f; t < 400f && score < count; t += dt)
        {
            var speed = FlappyRules.Speed(score, start);
            while (pillars.Count < 3)
            {
                var gap = FlappyRules.NextGap(level, last, rng.NextDouble());
                if (last is { } p)
                    nextLeft += FlappyRules.Spacing(FlappyRules.PlanningSpeed(score, start), p, gap);
                pillars.Add((nextLeft, gap));
                last = gap;
            }

            // Target: the pillar ahead, but only a reaction time after passing the last one (until then, the old gap)
            var ahead = pillars[0];
            var target = sincePass < FlappyRules.Reaction ? holdY : ahead.Gap.Center;
            sinceFlap += dt;
            sincePass += dt;
            if (flaps && bird.Y > target + 0.02f && sinceFlap >= FlappyRules.FlapApexTime)
            {
                bird.Flap();
                sinceFlap = 0;
            }
            bird.Step(dt);

            for (var i = 0; i < pillars.Count; i++)
                pillars[i] = (pillars[i].Left - speed * dt, pillars[i].Gap);
            nextLeft -= speed * dt;

            var hit = pillars.Exists(p => FlappyRules.Touches(birdX, bird.Y, p.Left, p.Gap));
            touching = hit ? touching + dt : 0f;
            if (touching >= FlappyRules.CrashGrace)
                return score;
            if (FlappyRules.Passed(birdX, pillars[0].Left))
            {
                holdY = pillars[0].Gap.Center;
                pillars.RemoveAt(0);
                score++;
                sincePass = 0;
            }
        }
        return score;
    }

    [Fact]
    public void HitsUseTheSmallCircleAndOnlyTheTrunks()
    {
        var gap = new FlappyGap(0, 0.45f, 0.36f); // 0.27 .. 0.63
        Assert.True(FlappyRules.Touches(0.05f, 0.2f, 0f, gap));   // In the upper trunk
        Assert.True(FlappyRules.Touches(0.05f, 0.7f, 0f, gap));   // In the lower trunk
        Assert.False(FlappyRules.Touches(0.05f, 0.45f, 0f, gap)); // Through the middle
        // Just inside the gap edge by more than the hit radius: clear (the picture may overlap the leaves)
        Assert.False(FlappyRules.Touches(0.05f, 0.27f + FlappyRules.HitRadius + 0.002f, 0f, gap));
        // Beside the pillar, level with a trunk, but not touching
        Assert.False(FlappyRules.Touches(-FlappyRules.HitRadius - 0.01f, 0.1f, 0f, gap));
        Assert.True(FlappyRules.Touches(-FlappyRules.HitRadius + 0.01f, 0.1f, 0f, gap));
        // Corner: diagonal distance counts, not the box
        Assert.False(FlappyRules.Touches(-FlappyRules.HitRadius * 0.8f, 0.27f + FlappyRules.HitRadius * 0.8f, 0f, gap));

        Assert.False(FlappyRules.Passed(0f, -FlappyRules.TrunkWidth));
        Assert.True(FlappyRules.Passed(0f, -FlappyRules.TrunkWidth - FlappyRules.HitRadius - 0.01f));
        Assert.True(FlappyRules.CrossedMilestone(9, 10));
        Assert.False(FlappyRules.CrossedMilestone(10, 11));
    }
}
