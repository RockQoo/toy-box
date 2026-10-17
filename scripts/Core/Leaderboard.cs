using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ToyBox.Core;

/// <summary>How a game's records are ranked.</summary>
public enum RankOrder
{
    /// <summary>Higher score first (balloon, fruit, dino).</summary>
    ScoreDesc,
    /// <summary>Shorter time first (memory match, maze).</summary>
    TimeAsc,
    /// <summary>Higher score first, then shorter time (quiz).</summary>
    ScoreDescTimeAsc,
}

/// <summary>One leaderboard row. <see cref="TimeMs"/> is unused by score-only games.</summary>
public sealed record ScoreEntry(string Name, int Score, long TimeMs, long UnixTime);

/// <summary>A single game's top-N list. Ties keep the earlier record ahead.</summary>
public sealed class Leaderboard
{
    public const int Capacity = 10;

    private readonly List<ScoreEntry> _entries = new();

    public Leaderboard(RankOrder order) => Order = order;

    public RankOrder Order { get; }
    public IReadOnlyList<ScoreEntry> Entries => _entries;
    public ScoreEntry? Best => _entries.Count > 0 ? _entries[0] : null;

    /// <summary>Zero-based rank the entry would take, or -1 if it would not make the list.</summary>
    public int RankOf(ScoreEntry entry)
    {
        var index = 0;
        while (index < _entries.Count && Compare(_entries[index], entry) <= 0)
            index++;
        return index < Capacity ? index : -1;
    }

    /// <summary>True if the entry would beat (not tie) the current best, or the list is empty.</summary>
    public bool IsNewBest(ScoreEntry entry) => Best is null || Compare(entry, Best) < 0;

    /// <summary>Inserts the entry and drops anything past capacity. Returns its rank, or -1 if it did not make the list.</summary>
    public int Add(ScoreEntry entry)
    {
        var rank = RankOf(entry);
        if (rank < 0)
            return -1;
        _entries.Insert(rank, entry);
        if (_entries.Count > Capacity)
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        return rank;
    }

    public void Clear() => _entries.Clear();

    /// <summary>Negative if <paramref name="a"/> ranks ahead of <paramref name="b"/>.</summary>
    public int Compare(ScoreEntry a, ScoreEntry b) => Order switch
    {
        RankOrder.ScoreDesc => b.Score.CompareTo(a.Score),
        RankOrder.TimeAsc => a.TimeMs.CompareTo(b.TimeMs),
        _ => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.TimeMs.CompareTo(b.TimeMs),
    };

    internal void Load(IEnumerable<ScoreEntry> entries)
    {
        _entries.Clear();
        foreach (var e in entries)
            Add(e);
    }
}

/// <summary>All games' leaderboards, keyed by game id, with JSON (de)serialization for saving to disk.</summary>
public sealed class RecordBook
{
    private readonly Dictionary<string, Leaderboard> _boards = new();

    public Leaderboard Get(string gameId, RankOrder order)
    {
        if (!_boards.TryGetValue(gameId, out var board))
        {
            board = new Leaderboard(order);
            _boards[gameId] = board;
        }
        return board;
    }

    public string ToJson() => JsonSerializer.Serialize(
        _boards.ToDictionary(kv => kv.Key, kv => kv.Value.Entries.ToList()),
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Loads saved lists. Each list is re-ranked with the order given by <paramref name="orderOf"/>.
    /// Broken or missing data yields an empty book rather than an exception.</summary>
    public static RecordBook FromJson(string? json, Func<string, RankOrder> orderOf)
    {
        var book = new RecordBook();
        if (string.IsNullOrWhiteSpace(json))
            return book;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, List<ScoreEntry>>>(json);
            if (data is null)
                return book;
            foreach (var (id, entries) in data)
                book.Get(id, orderOf(id)).Load(entries.Where(e => e is not null && e.Name is not null));
        }
        catch (JsonException)
        {
        }
        return book;
    }
}
