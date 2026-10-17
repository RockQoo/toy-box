using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class MemoryRulesTests
{
    [Theory]
    [InlineData(MemoryLevel.Easy, 6, 3, 2)]
    [InlineData(MemoryLevel.Normal, 8, 4, 2)]
    [InlineData(MemoryLevel.Hard, 12, 4, 3)]
    public void CountsAndGridFollowGdd(MemoryLevel level, int cards, int columns, int rows)
    {
        Assert.Equal(cards, MemoryRules.CardCount(level));
        Assert.Equal(cards / 2, MemoryRules.PairCount(level));
        Assert.Equal(columns, MemoryRules.Columns(level));
        Assert.Equal(rows, MemoryRules.Rows(level));
        Assert.Equal(cards, columns * rows);
    }

    [Fact]
    public void VariantsAreOnePerLevelEasiestFirst()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, MemoryRules.Variants.Select(v => v.Id));
        Assert.Equal("hard", MemoryRules.VariantId(MemoryLevel.Hard));
        Assert.Equal(MemoryRules.Animals.Length, MemoryRules.AnimalNames.Length);
    }

    [Theory]
    [InlineData(MemoryLevel.Easy)]
    [InlineData(MemoryLevel.Normal)]
    [InlineData(MemoryLevel.Hard)]
    public void DealGivesDistinctAnimalsInPairs(MemoryLevel level)
    {
        var seen = new HashSet<string>();
        for (var seed = 0; seed < 100; seed++)
        {
            var cards = MemoryRules.Deal(level, new Random(seed));
            Assert.Equal(MemoryRules.CardCount(level), cards.Length);
            var groups = cards.GroupBy(c => c).ToList();
            Assert.Equal(MemoryRules.PairCount(level), groups.Count);
            Assert.All(groups, g => Assert.Equal(2, g.Count()));
            Assert.All(cards, c => Assert.InRange(c, 0, MemoryRules.Animals.Length - 1));
            seen.Add(string.Join(",", cards));
        }
        Assert.True(seen.Count > 50); // Shuffled, not a fixed layout
    }

    private static MemoryBoard Board() => new(new[] { 0, 1, 2, 0, 1, 2 });

    [Fact]
    public void MatchKeepsBothOpenAndCountsThePair()
    {
        var board = Board();
        Assert.Equal(MemoryFlip.First, board.Flip(0));
        Assert.Equal(0, board.OpenCard);
        Assert.Equal(MemoryFlip.Match, board.Flip(3));
        Assert.Equal(1, board.Pairs);
        Assert.Equal(2, board.Flips);
        Assert.True(board.IsMatched(0) && board.IsMatched(3));
        Assert.Equal(-1, board.OpenCard);
        Assert.False(board.Comparing);
    }

    [Fact]
    public void MismatchLocksUntilHiddenWithoutPenalty()
    {
        var board = Board();
        board.Flip(0);
        Assert.Equal(MemoryFlip.Mismatch, board.Flip(1));
        Assert.True(board.Comparing);
        Assert.True(board.IsFaceUp(0) && board.IsFaceUp(1));
        Assert.Equal(MemoryFlip.Ignored, board.Flip(2)); // Locked while comparing
        Assert.Equal(2, board.Flips);
        Assert.Equal(1, board.Misses);

        Assert.Equal((0, 1), board.Hide());
        Assert.False(board.Comparing);
        Assert.False(board.IsFaceUp(0) || board.IsFaceUp(1));
        Assert.Null(board.Hide());
        Assert.Equal(0, board.Pairs);
        Assert.Equal(MemoryFlip.First, board.Flip(2));
    }

    [Fact]
    public void OpenOrMatchedCardsIgnoreTaps()
    {
        var board = Board();
        board.Flip(0);
        Assert.Equal(MemoryFlip.Ignored, board.Flip(0)); // Same card twice is not a try
        board.Flip(3);
        Assert.Equal(MemoryFlip.Ignored, board.Flip(3));
        Assert.Equal(MemoryFlip.Ignored, board.Flip(-1));
        Assert.Equal(MemoryFlip.Ignored, board.Flip(6));
        Assert.Equal(2, board.Flips);
    }

    [Fact]
    public void FinishesWhenAllPairsAreFoundAndStopsTheClock()
    {
        var board = Board();
        board.AddTime(1.25);
        foreach (var (a, b) in new[] { (0, 3), (1, 4), (2, 5) })
        {
            board.Flip(a);
            board.Flip(b);
        }
        Assert.True(board.Finished);
        Assert.Equal(6, board.Flips);
        board.AddTime(5);
        Assert.Equal(1250, board.TimeMs);
        Assert.False(board.CanFlip(0));
        var entry = board.Entry("다온", 1700000000);
        Assert.Equal(6, entry.Score);
        Assert.Equal(1250, entry.TimeMs);
    }

    [Fact]
    public void RecordFormatShowsTimeAndFlips() => Assert.Equal("23.4초 · 18번", MemoryRules.FormatRecord(23400, 18));
}
