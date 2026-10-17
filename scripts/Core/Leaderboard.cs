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
    /// <summary>Shorter time first (maze).</summary>
    TimeAsc,
    /// <summary>Higher score first, then shorter time (quiz).</summary>
    ScoreDescTimeAsc,
    /// <summary>Shorter time first, then lower score (memory match: the score holds the card flips, fewer is better).</summary>
    TimeAscScoreAsc,
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
        RankOrder.TimeAscScoreAsc => a.TimeMs != b.TimeMs ? a.TimeMs.CompareTo(b.TimeMs) : a.Score.CompareTo(b.Score),
        _ => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.TimeMs.CompareTo(b.TimeMs),
    };

    internal void Load(IEnumerable<ScoreEntry> entries)
    {
        _entries.Clear();
        foreach (var e in entries)
            Add(e);
    }
}

/// <summary>
/// All leaderboards, keyed by game id or by a variant key (see <see cref="BoardKey"/>), with JSON (de)serialization
/// for saving to disk.
/// </summary>
public sealed class RecordBook
{
    private readonly Dictionary<string, Leaderboard> _boards = new();

    /// <summary>Every row of every board (e.g. to seed recent names).</summary>
    public IEnumerable<ScoreEntry> AllEntries => _boards.Values.SelectMany(b => b.Entries);

    /// <summary>Empties every board, including variant boards of games no longer listed.</summary>
    public void ClearAll()
    {
        foreach (var board in _boards.Values)
            board.Clear();
    }

    /// <param name="key">A game id, or a variant key from <see cref="BoardKey.Of"/>.</param>
    public Leaderboard Get(string key, RankOrder order)
    {
        if (!_boards.TryGetValue(key, out var board))
        {
            board = new Leaderboard(order);
            _boards[key] = board;
        }
        return board;
    }

    public string ToJson() => JsonSerializer.Serialize(
        _boards.Where(kv => kv.Value.Entries.Count > 0) // Boards only looked at (e.g. empty variants) are not written
            .ToDictionary(kv => kv.Key, kv => kv.Value.Entries.ToList()),
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Loads saved lists. Each list is re-ranked with the order <paramref name="orderOf"/> gives for its key
    /// (a variant key such as "quiz:hard" is passed as is; use <see cref="BoardKey.GameId"/> to look the game up).
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
