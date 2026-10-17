using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class LeaderboardTests
{
    private static ScoreEntry E(string name, int score, long timeMs = 0, long at = 0) => new(name, score, timeMs, at);

    [Fact]
    public void ScoreDesc_SortsHighestFirst()
    {
        var board = new Leaderboard(RankOrder.ScoreDesc);
        board.Add(E("a", 5));
        board.Add(E("b", 12));
        board.Add(E("c", 8));
        Assert.Equal(new[] { "b", "c", "a" }, board.Entries.Select(e => e.Name));
        Assert.Equal("b", board.Best!.Name);
    }

    [Fact]
    public void Ties_KeepEarlierRecordAhead()
    {
        var board = new Leaderboard(RankOrder.ScoreDesc);
        board.Add(E("first", 10));
        Assert.Equal(1, board.Add(E("second", 10)));
        Assert.Equal("first", board.Best!.Name);
    }

    [Fact]
    public void KeepsOnlyTopTen()
    {
        var board = new Leaderboard(RankOrder.ScoreDesc);
        for (var i = 1; i <= 12; i++)
            board.Add(E($"p{i}", i));
        Assert.Equal(Leaderboard.Capacity, board.Entries.Count);
        Assert.Equal(12, board.Entries[0].Score);
        Assert.Equal(3, board.Entries[^1].Score);
    }

    [Fact]
    public void RankOf_IsMinusOneWhenNotInTopTen()
    {
        var board = new Leaderboard(RankOrder.ScoreDesc);
        for (var i = 0; i < 10; i++)
            board.Add(E("p", 10));
        Assert.Equal(-1, board.RankOf(E("low", 10))); // Tie with the 10th: earlier record keeps the spot
        Assert.Equal(-1, board.Add(E("low", 9)));
        Assert.Equal(0, board.RankOf(E("high", 11)));
    }

    [Fact]
    public void IsNewBest_RequiresBeatingNotTying()
    {
        var board = new Leaderboard(RankOrder.ScoreDesc);
        Assert.True(board.IsNewBest(E("x", 1)));
        board.Add(E("a", 10));
        Assert.False(board.IsNewBest(E("x", 10)));
        Assert.True(board.IsNewBest(E("x", 11)));
    }

    [Fact]
    public void TimeAsc_SortsFastestFirst()
    {
        var board = new Leaderboard(RankOrder.TimeAsc);
        board.Add(E("slow", 0, 42000));
        board.Add(E("fast", 0, 18000));
        Assert.Equal("fast", board.Best!.Name);
    }

    [Fact]
    public void ScoreThenTime_BreaksScoreTiesByTime()
    {
        var board = new Leaderboard(RankOrder.ScoreDescTimeAsc);
        board.Add(E("a", 9, 30000));
        board.Add(E("b", 9, 20000));
        board.Add(E("c", 10, 50000));
        Assert.Equal(new[] { "c", "b", "a" }, board.Entries.Select(e => e.Name));
    }

    [Fact]
    public void TimeThenScore_BreaksTimeTiesByFewerFlips()
    {
        var board = new Leaderboard(RankOrder.TimeAscScoreAsc);
        board.Add(E("many", 20, 30000));
        board.Add(E("few", 14, 30000));
        board.Add(E("fast", 30, 25000));
        board.Add(E("slow", 12, 40000));
        Assert.Equal(new[] { "fast", "few", "many", "slow" }, board.Entries.Select(e => e.Name));
        Assert.False(board.IsNewBest(E("tie", 30, 25000)));
    }

    [Fact]
    public void RecordBook_RoundTripsThroughJson()
    {
        var book = new RecordBook();
        book.Get("balloon", RankOrder.ScoreDesc).Add(E("민지", 32, 0, 1700000000));
        book.Get("memory", RankOrder.TimeAsc).Add(E("아빠", 0, 41500, 1700000100));

        var loaded = RecordBook.FromJson(book.ToJson(), id => id == "memory" ? RankOrder.TimeAsc : RankOrder.ScoreDesc);
        Assert.Equal(E("민지", 32, 0, 1700000000), loaded.Get("balloon", RankOrder.ScoreDesc).Best);
        Assert.Equal(41500, loaded.Get("memory", RankOrder.TimeAsc).Best!.TimeMs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[]")]
    public void RecordBook_BrokenDataGivesEmptyBook(string? json)
    {
        var book = RecordBook.FromJson(json, _ => RankOrder.ScoreDesc);
        Assert.Empty(book.Get("balloon", RankOrder.ScoreDesc).Entries);
    }
}
