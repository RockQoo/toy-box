using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class FruitRulesTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(9.99f, 0)]
    [InlineData(10f, 1)]
    [InlineData(20f, 2)]
    [InlineData(29.9f, 2)]
    public void PhaseFollowsGddTable(float elapsed, int index) => Assert.Equal(index, FruitRules.PhaseAt(elapsed).Index);

    [Fact]
    public void FruitsFallFasterEachPhase()
    {
        var speeds = new[] { 0f, 10f, 20f }.Select(t => FruitRules.FallSpeed(t, StartSpeed.Normal)).ToArray();
        for (var i = 1; i < speeds.Length; i++)
            Assert.True(speeds[i] > speeds[i - 1]);
        // 10~20초: 생성 간격 감소
        Assert.True(FruitRules.SpawnInterval(10f, StartSpeed.Normal) < FruitRules.SpawnInterval(0f, StartSpeed.Normal));
    }

    [Fact]
    public void LastPhaseDropsTwoOrThree()
    {
        Assert.Equal(1, FruitRules.GroupSize(5f, 0.99));
        Assert.Equal(1, FruitRules.GroupSize(15f, 0.99));
        Assert.Equal(2, FruitRules.GroupSize(25f, 0.0));
        Assert.Equal(3, FruitRules.GroupSize(25f, 0.999));
        // More fruit per second at the end than in the middle
        var middle = 1f / FruitRules.SpawnInterval(15f, StartSpeed.Normal);
        var end = 2f / FruitRules.SpawnInterval(25f, StartSpeed.Normal);
        Assert.True(end > middle);
    }

    [Fact]
    public void StartSpeedScales()
    {
        Assert.True(FruitRules.FallSpeed(0, StartSpeed.Slow) < FruitRules.FallSpeed(0, StartSpeed.Normal));
        Assert.True(FruitRules.FallSpeed(0, StartSpeed.Fast) > FruitRules.FallSpeed(0, StartSpeed.Normal));
        Assert.True(FruitRules.SpawnInterval(0, StartSpeed.Fast) < FruitRules.SpawnInterval(0, StartSpeed.Normal));
    }

    [Fact]
    public void BonusIsRareLateAndAlone()
    {
        Assert.False(FruitRules.IsBonus(1f, false, 0.0));             // Not in the first seconds
        Assert.True(FruitRules.IsBonus(5f, false, 0.0));
        Assert.False(FruitRules.IsBonus(5f, true, 0.0));              // Only one on screen
        Assert.False(FruitRules.IsBonus(5f, false, FruitRules.BonusChance));
        Assert.Equal(1, FruitRules.Points(false));
        Assert.Equal(3, FruitRules.Points(true));
    }

    [Theory]
    [InlineData(9, 10, true)]
    [InlineData(8, 11, true)]   // +3 jumps over 10
    [InlineData(10, 11, false)]
    [InlineData(10, 13, false)]
    [InlineData(19, 22, true)]
    public void MilestoneCountsJumps(int before, int after, bool crossed) =>
        Assert.Equal(crossed, FruitRules.CrossedMilestone(before, after));

    [Fact]
    public void CatchNeedsRimCrossingOverOpening()
    {
        Assert.True(FruitRules.IsCaught(100, 490, 505, 110, 500, 80));
        Assert.False(FruitRules.IsCaught(100, 505, 520, 110, 500, 80));  // Already below the rim
        Assert.False(FruitRules.IsCaught(300, 490, 505, 110, 500, 80));  // Beside the basket
        Assert.True(FruitRules.IsCaught(100, 400, 600, 110, 500, 80));   // Fast frame still counts
    }

    [Fact]
    public void AtLeastOneKindStaysPicked()
    {
        var mask = 1 << 2;
        var after = FruitRules.Toggle(mask, 2, out var changed);
        Assert.False(changed);
        Assert.Equal(mask, after);

        after = FruitRules.Toggle(mask, 0, out changed);
        Assert.True(changed);
        Assert.Equal(new[] { 0, 2 }, FruitRules.Kinds(after));
        after = FruitRules.Toggle(after, 2, out changed);
        Assert.True(changed);
        Assert.Equal(new[] { 0 }, FruitRules.Kinds(after));
    }

    [Fact]
    public void SanitizeFallsBackToDefaults()
    {
        Assert.Equal(FruitRules.DefaultKinds, FruitRules.Sanitize(0));
        Assert.Equal(FruitRules.DefaultKinds, FruitRules.Sanitize(1 << 10));
        Assert.Equal(5, FruitRules.Kinds(FruitRules.DefaultKinds).Count);
        Assert.Equal(3, FruitRules.Sanitize(3));
    }
}
