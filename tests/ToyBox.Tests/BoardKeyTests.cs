using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class BoardKeyTests
{
    private static readonly GameVariant[] Levels = { new("easy", "쉬움"), new("normal", "보통"), new("hard", "어려움") };

    [Fact]
    public void PlainGamesKeepTheirId()
    {
        Assert.Equal("balloon", BoardKey.Of("balloon", null));
        Assert.Equal("balloon", BoardKey.Of("balloon", ""));
        Assert.Equal("balloon", BoardKey.GameId("balloon"));
        Assert.Null(BoardKey.VariantId("balloon"));
    }

    [Fact]
    public void VariantKeysSplitBackIntoGameAndVariant()
    {
        var key = BoardKey.Of("quiz", "hard");
        Assert.Equal("quiz:hard", key);
        Assert.Equal("quiz", BoardKey.GameId(key));
        Assert.Equal("hard", BoardKey.VariantId(key));
    }

    [Fact]
    public void FeaturedIsTheHardestVariantWithARecord()
    {
        Assert.Null(BoardKey.Featured(Levels, _ => false));
        Assert.Null(BoardKey.Featured(Array.Empty<GameVariant>(), _ => true));
        Assert.Equal("normal", BoardKey.Featured(Levels, id => id is "easy" or "normal")!.Id);
        Assert.Equal("hard", BoardKey.Featured(Levels, _ => true)!.Id);
        Assert.Equal("easy", BoardKey.Featured(Levels, id => id == "easy")!.Id);
    }

    private static ScoreEntry E(string name, int score, long timeMs = 0) => new(name, score, timeMs, 1700000000);

    private static RankOrder OrderOf(string key) => BoardKey.GameId(key) switch
    {
        "quiz" => RankOrder.ScoreDescTimeAsc,
        "memory" => RankOrder.TimeAsc,
        _ => RankOrder.ScoreDesc,
    };

    [Fact]
    public void VariantBoardsAreSeparateAndRoundTrip()
    {
        var book = new RecordBook();
        book.Get("balloon", RankOrder.ScoreDesc).Add(E("민지", 32));
        book.Get(BoardKey.Of("quiz", "easy"), RankOrder.ScoreDescTimeAsc).Add(E("다온", 10, 20000));
        book.Get(BoardKey.Of("quiz", "hard"), RankOrder.ScoreDescTimeAsc).Add(E("아빠", 7, 60000));
        book.Get(BoardKey.Of("quiz", "hard"), RankOrder.ScoreDescTimeAsc).Add(E("엄마", 7, 50000));

        var loaded = RecordBook.FromJson(book.ToJson(), OrderOf);
        Assert.Equal("민지", loaded.Get("balloon", RankOrder.ScoreDesc).Best!.Name);
        Assert.Equal("다온", loaded.Get("quiz:easy", RankOrder.ScoreDescTimeAsc).Best!.Name);
        var hard = loaded.Get("quiz:hard", RankOrder.ScoreDescTimeAsc);
        Assert.Equal(new[] { "엄마", "아빠" }, hard.Entries.Select(e => e.Name)); // Re-ranked by score then time
        Assert.Empty(loaded.Get("quiz:normal", RankOrder.ScoreDescTimeAsc).Entries);
        Assert.Empty(loaded.Get("quiz", RankOrder.ScoreDescTimeAsc).Entries); // The plain key is a different board
        Assert.Equal(4, loaded.AllEntries.Count());
    }

    [Fact]
    public void OlderFilesWithoutVariantsStillLoad()
    {
        const string json = """
            {
              "balloon": [
                { "Name": "sinhye", "Score": 45, "TimeMs": 0, "UnixTime": 1792201096 },
                { "Name": "나", "Score": 44, "TimeMs": 0, "UnixTime": 1792200583 }
              ]
            }
            """;
        var book = RecordBook.FromJson(json, OrderOf);
        Assert.Equal(new[] { "sinhye", "나" }, book.Get("balloon", RankOrder.ScoreDesc).Entries.Select(e => e.Name));
        Assert.Empty(book.Get("quiz:hard", RankOrder.ScoreDescTimeAsc).Entries);
    }

    [Fact]
    public void ClearAllEmptiesVariantBoardsToo()
    {
        var book = new RecordBook();
        book.Get("balloon", RankOrder.ScoreDesc).Add(E("a", 3));
        book.Get("quiz:hard", RankOrder.ScoreDescTimeAsc).Add(E("b", 5, 1000));
        book.ClearAll();
        Assert.Empty(book.AllEntries);
    }
}
