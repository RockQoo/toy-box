using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class RecentNamesTests
{
    [Fact]
    public void Push_PutsNewestFirst()
    {
        Assert.Equal(new[] { "다온", "규나" }, RecentNames.Push(new[] { "규나" }, "다온"));
    }

    [Fact]
    public void Push_MovesExistingNameToFrontWithoutDuplicate()
    {
        Assert.Equal(new[] { "신혜", "다온", "규나" }, RecentNames.Push(new[] { "다온", "규나", "신혜" }, "신혜"));
    }

    [Fact]
    public void Push_KeepsAtMostCapacity()
    {
        var list = RecentNames.Push(new[] { "a", "b", "c" }, "d");
        Assert.Equal(RecentNames.Capacity, list.Length);
        Assert.Equal(new[] { "d", "a", "b" }, list);
    }

    [Fact]
    public void Push_CleansTheName()
    {
        Assert.Equal(new[] { "친구" }, RecentNames.Push(Array.Empty<string>(), "   "));
        Assert.Equal("가나다라마바사아", RecentNames.Push(Array.Empty<string>(), " 가나다라마바사아자차 ")[0]);
    }

    [Fact]
    public void Load_FallsBackToLegacyLastName()
    {
        Assert.Equal(new[] { "daon" }, RecentNames.Load(null, "daon"));
        Assert.Empty(RecentNames.Load(null, ""));
        Assert.Equal(new[] { "규나" }, RecentNames.Load(new[] { "규나" }, "daon"));
    }

    [Fact]
    public void Load_SeedsFromLegacyNameThenNewestRecords()
    {
        var records = new[]
        {
            new ScoreEntry("kyuna", 42, 0, 100),
            new ScoreEntry("daon", 28, 0, 300),
            new ScoreEntry("친구", 10, 0, 400),
            new ScoreEntry("sinhye", 45, 0, 200),
        };
        Assert.Equal(new[] { "sinhye", "daon", "kyuna" }, RecentNames.Load(null, "sinhye", records));
        Assert.Equal(new[] { "daon", "sinhye", "kyuna" }, RecentNames.Load(null, null, records));
        Assert.Equal(new[] { "a" }, RecentNames.Load(new[] { "a" }, "sinhye", records));
    }

    [Fact]
    public void Load_DropsBlanksAndDuplicates()
    {
        Assert.Equal(new[] { "a", "b" }, RecentNames.Load(new[] { " ", "a", "a", "b" }, null));
    }
}
