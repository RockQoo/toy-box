using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class BalloonRulesTests
{
    [Theory]
    [InlineData(0f, 0, 1.00f)]
    [InlineData(9.99f, 0, 1.00f)]
    [InlineData(10f, 1, 0.70f)]
    [InlineData(20f, 2, 0.50f)]
    [InlineData(25f, 3, 0.35f)]
    [InlineData(29.9f, 3, 0.35f)]
    public void PhaseFollowsGddTable(float elapsed, int index, float interval)
    {
        var phase = BalloonRules.PhaseAt(elapsed);
        Assert.Equal(index, phase.Index);
        Assert.Equal(interval, phase.SpawnInterval, 3);
    }

    [Fact]
    public void BalloonsGetFasterEachPhase()
    {
        var speeds = new[] { 0f, 10f, 20f, 25f }.Select(t => BalloonRules.RiseSpeed(t, BalloonStartSpeed.Normal)).ToArray();
        for (var i = 1; i < speeds.Length; i++)
            Assert.True(speeds[i] > speeds[i - 1]);
    }

    [Fact]
    public void StartSpeedScalesRiseSpeed()
    {
        Assert.True(BalloonRules.RiseSpeed(0, BalloonStartSpeed.Slow) < BalloonRules.RiseSpeed(0, BalloonStartSpeed.Normal));
        Assert.True(BalloonRules.RiseSpeed(0, BalloonStartSpeed.Fast) > BalloonRules.RiseSpeed(0, BalloonStartSpeed.Normal));
        Assert.True(BalloonRules.SpawnInterval(0, BalloonStartSpeed.Fast) < BalloonRules.SpawnInterval(0, BalloonStartSpeed.Normal));
    }

    [Theory]
    [InlineData(0f, 30)]
    [InlineData(0.5f, 30)]
    [InlineData(12f, 18)]
    [InlineData(29.5f, 1)]
    [InlineData(30f, 0)]
    [InlineData(31f, 0)]
    public void SecondsLeftRoundsUp(float elapsed, int expected) =>
        Assert.Equal(expected, BalloonRules.SecondsLeft(elapsed));
}
