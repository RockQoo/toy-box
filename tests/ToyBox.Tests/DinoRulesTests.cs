using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class DinoRulesTests
{
    private static readonly DinoObstacle[] Kinds = { DinoObstacle.SmallCactus, DinoObstacle.Cactus, DinoObstacle.Rock };
    private static readonly StartSpeed[] Starts = { StartSpeed.Slow, StartSpeed.Normal, StartSpeed.Fast };

    [Theory]
    [InlineData(0f, 1.0f)]
    [InlineData(9.9f, 1.0f)]
    [InlineData(11f, 1.2f)]
    [InlineData(21f, 1.5f)]
    [InlineData(31f, 1.8f)]
    [InlineData(46f, 2.0f)]
    [InlineData(300f, 2.0f)]
    public void SpeedFollowsGddTable(float elapsed, float multiplier) =>
        Assert.Equal(multiplier, DinoRules.SpeedMultiplier(elapsed), 3);

    [Fact]
    public void SpeedEasesUpAndNeverDrops()
    {
        var last = 0f;
        for (var t = 0f; t < 120f; t += 0.05f)
        {
            var m = DinoRules.SpeedMultiplier(t);
            Assert.True(m >= last - 1e-5f, $"speed dropped at {t}");
            last = m;
        }
        // Halfway through a ramp it is between the two steps, not a jump
        var mid = DinoRules.SpeedMultiplier(10f + DinoRules.RampSeconds / 2);
        Assert.InRange(mid, 1.05f, 1.15f);
        Assert.Equal(2.0f, DinoRules.MaxMultiplier, 3);
    }

    [Fact]
    public void StepIndexChangesAtTableTimes()
    {
        Assert.Equal(0, DinoRules.StepAt(9.99f).Index);
        Assert.Equal(1, DinoRules.StepAt(10f).Index);
        Assert.Equal(3, DinoRules.StepAt(30f).Index);
        Assert.Equal(DinoRules.StepCount - 1, DinoRules.StepAt(999f).Index);
    }

    [Fact]
    public void JumpArcRisesPeaksAndLands()
    {
        Assert.Equal(0f, DinoRules.JumpHeightAt(0f));
        Assert.Equal(DinoRules.JumpHeight, DinoRules.JumpHeightAt(DinoRules.JumpDuration / 2), 4);
        Assert.Equal(0f, DinoRules.JumpHeightAt(DinoRules.JumpDuration), 4);
        Assert.True(DinoRules.JumpHeightAt(0.1f) < DinoRules.JumpHeightAt(0.2f));
    }

    [Fact]
    public void ApexClearsEveryObstacleWithRoom()
    {
        foreach (var kind in Kinds)
            Assert.True(DinoRules.JumpHeight > DinoRules.Hitbox(kind).Height * 2, kind.ToString());
    }

    [Fact]
    public void EveryObstacleIsJumpableAtEverySpeed()
    {
        // Even the slowest run gives a comfortable timing window (wider at higher speed)
        foreach (var kind in Kinds)
        {
            Assert.True(DinoRules.ClearWindow(kind, DinoRules.SlowestRunSpeed) >= 0.18f, kind.ToString());
            Assert.True(DinoRules.ClearWindow(kind, DinoRules.FastestRunSpeed) > DinoRules.ClearWindow(kind, DinoRules.SlowestRunSpeed));
        }
    }

    [Fact]
    public void JumpInTheWindowClearsAndOutsideHits()
    {
        // Simulate: obstacle front edge 1.0 ahead of the dino hitbox front, take off at a chosen moment
        foreach (var kind in Kinds)
        foreach (var speed in new[] { DinoRules.SlowestRunSpeed, DinoRules.BaseRunSpeed, DinoRules.FastestRunSpeed })
        {
            const float distance = 1.0f;
            var (earliest, latest) = DinoRules.JumpWindow(kind, distance, speed);
            Assert.False(Crashes(kind, distance, speed, (earliest + latest) / 2), $"{kind} @ {speed}: middle");
            Assert.False(Crashes(kind, distance, speed, earliest + 0.01f), $"{kind} @ {speed}: early edge");
            Assert.False(Crashes(kind, distance, speed, latest - 0.01f), $"{kind} @ {speed}: late edge");
            Assert.True(Crashes(kind, distance, speed, latest + 0.06f), $"{kind} @ {speed}: too late");
            Assert.True(Crashes(kind, distance, speed, null), $"{kind} @ {speed}: no jump");
        }
    }

    private static bool Crashes(DinoObstacle kind, float distance, float speed, float? takeOff)
    {
        const float dt = 0.002f;
        var obstacleLeft = DinoRules.DinoHitWidth + distance; // Dino hitbox at [0, width]
        for (var t = 0f; t < distance / speed + 2f; t += dt)
        {
            var feet = takeOff is { } s ? DinoRules.JumpHeightAt(t - s) : 0f;
            if (DinoRules.Touches(0f, feet, obstacleLeft - speed * t, kind))
                return true;
        }
        return false;
    }

    [Fact]
    public void GapsAlwaysLeaveTimeToLandAndJumpAgain()
    {
        foreach (var start in Starts)
        for (var t = 0f; t <= 90f; t += 0.5f)
        for (var roll = 0.0; roll < 1.0; roll += 0.05)
        {
            var speed = DinoRules.RunSpeed(t, start);
            var gap = DinoRules.NextGap(t, start, roll);
            Assert.True(gap >= DinoRules.MinGap(speed) - 1e-5f);
            foreach (var first in Kinds)
            foreach (var second in Kinds)
            {
                // Worst case: the latest possible take-off for the first obstacle
                var landing = DinoRules.JumpWindow(first, 0.5f, speed).Latest + DinoRules.JumpDuration;
                var (earliest, latest) = DinoRules.JumpWindow(second, 0.5f + gap, speed);
                Assert.True(latest - landing >= DinoRules.Reaction - 1e-4f,
                    $"{first}->{second} at t={t} {start}: {latest - landing:0.000}s after landing");
                Assert.True(latest > earliest);
            }
        }
    }

    [Fact]
    public void GapsShrinkAsTheRunSpeedsUp()
    {
        // Measured in time (what the child feels), the gaps get shorter
        var early = DinoRules.NextGap(0f, StartSpeed.Normal, 0.5) / DinoRules.RunSpeed(0f, StartSpeed.Normal);
        var late = DinoRules.NextGap(60f, StartSpeed.Normal, 0.5) / DinoRules.RunSpeed(60f, StartSpeed.Normal);
        Assert.True(late < early);
        Assert.True(DinoRules.MinGap(2f) > DinoRules.MinGap(1f));
    }

    [Fact]
    public void TallCactusOnlyAfterFirstSpeedUp()
    {
        for (var roll = 0.0; roll < 1.0; roll += 0.01)
            Assert.NotEqual(DinoObstacle.Cactus, DinoRules.PickObstacle(5f, roll));
        Assert.Equal(DinoObstacle.Cactus, DinoRules.PickObstacle(15f, 0.5));
    }

    [Fact]
    public void HitboxesAndPassing()
    {
        Assert.True(DinoRules.Touches(0f, 0f, 0.05f, DinoObstacle.Cactus));
        Assert.False(DinoRules.Touches(0f, 0.3f, 0.05f, DinoObstacle.Cactus)); // High above
        Assert.False(DinoRules.Touches(0f, 0f, DinoRules.DinoHitWidth + 0.01f, DinoObstacle.Rock)); // Not there yet
        Assert.False(DinoRules.Passed(0.5f, 0.45f, DinoObstacle.Rock));
        Assert.True(DinoRules.Passed(0.5f, 0.38f, DinoObstacle.Rock));
    }

    [Fact]
    public void MilestoneEveryTen()
    {
        Assert.False(DinoRules.CrossedMilestone(8, 9));
        Assert.True(DinoRules.CrossedMilestone(9, 10));
        Assert.False(DinoRules.CrossedMilestone(10, 11));
        Assert.True(DinoRules.CrossedMilestone(19, 20));
    }

    // ---- DinoJump ----

    [Fact]
    public void NoDoubleJumpInTheAir()
    {
        var jump = new DinoJump();
        Assert.True(jump.Press());
        jump.Step(0.1f);
        Assert.False(jump.Press()); // In the air: buffered, not a second jump
        Assert.True(jump.Height > 0);
        var events = Run(jump, 0.7f);
        Assert.DoesNotContain(JumpEvent.Jumped, events); // The early press ran out before landing
        Assert.Contains(JumpEvent.Landed, events);
        Assert.True(jump.Grounded);
    }

    [Fact]
    public void PressJustBeforeLandingJumpsOnLanding()
    {
        var jump = new DinoJump();
        jump.Press();
        Run(jump, DinoRules.JumpDuration - 0.1f);
        Assert.False(jump.Grounded);
        jump.Press();
        var events = Run(jump, 0.15f);
        Assert.Contains(JumpEvent.Jumped, events);
        Assert.DoesNotContain(JumpEvent.Landed, events);
        Assert.False(jump.Grounded);
        Assert.True(jump.Velocity > 0); // Going up again
    }

    [Fact]
    public void FullJumpReachesApexAndLands()
    {
        var jump = new DinoJump();
        jump.Press();
        var top = 0f;
        var landed = false;
        for (var i = 0; i < 100 && !landed; i++)
        {
            landed = jump.Step(1 / 60f) == JumpEvent.Landed;
            top = System.MathF.Max(top, jump.Height);
        }
        Assert.True(landed);
        Assert.InRange(top, DinoRules.JumpHeight * 0.98f, DinoRules.JumpHeight + 1e-4f);
        Assert.Equal(0f, jump.Height);
        Assert.Equal(0f, jump.Velocity);
    }

    private static List<JumpEvent> Run(DinoJump jump, float seconds)
    {
        var events = new List<JumpEvent>();
        for (var t = 0f; t < seconds; t += 1 / 60f)
            events.Add(jump.Step(1 / 60f));
        return events;
    }
}
