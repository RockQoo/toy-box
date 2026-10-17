using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>Difficulty: how many cards are dealt. Each level has its own leaderboard (see <see cref="MemoryRules.Variants"/>).</summary>
public enum MemoryLevel { Easy, Normal, Hard }

/// <summary>Animal match rules that do not need the engine (docs/games/03-animal-match.md).</summary>
public static class MemoryRules
{
    /// <summary>Card art ids (assets/sprites/animal/&lt;id&gt;.png), in the same order as <see cref="AnimalNames"/>.</summary>
    public static readonly string[] Animals = { "lion", "elephant", "penguin", "rabbit", "fox", "panda", "frog", "turtle" };

    public static readonly string[] AnimalNames = { "사자", "코끼리", "펭귄", "토끼", "여우", "판다", "개구리", "거북이" };

    /// <summary>How long every card stays face up before the play starts (GDD: about 2 seconds).</summary>
    public const float RevealSeconds = 2f;

    /// <summary>How long a mismatched pair stays open before both flip back (GDD: about 1 second).</summary>
    public const float MismatchSeconds = 1f;

    /// <summary>Leaderboard variants, easiest first: 6, 8 and 12 card games are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(MemoryLevel level) => Variants[(int)level].Id;

    /// <summary>GDD: 쉬움 3 / 보통 4 / 어려움 6 pairs.</summary>
    public static int PairCount(MemoryLevel level) => level switch
    {
        MemoryLevel.Easy => 3,
        MemoryLevel.Normal => 4,
        _ => 6,
    };

    public static int CardCount(MemoryLevel level) => PairCount(level) * 2;

    /// <summary>Grid columns: 6 → 3 x 2, 8 → 4 x 2, 12 → 4 x 3 (keeps the cards big and the grid compact).</summary>
    public static int Columns(MemoryLevel level) => level == MemoryLevel.Easy ? 3 : 4;

    public static int Rows(MemoryLevel level) => (CardCount(level) + Columns(level) - 1) / Columns(level);

    /// <summary>
    /// Deals a board: <see cref="PairCount"/> different animals picked at random, two cards each, shuffled.
    /// Returns the animal index (into <see cref="Animals"/>) of each card in grid order (row by row).
    /// </summary>
    public static int[] Deal(MemoryLevel level, Random rng)
    {
        var pairs = PairCount(level);
        var pool = new List<int>();
        for (var i = 0; i < Animals.Length; i++)
            pool.Add(i);
        Shuffle(pool, rng);

        var cards = new List<int>();
        for (var i = 0; i < pairs; i++)
        {
            cards.Add(pool[i]);
            cards.Add(pool[i]);
        }
        Shuffle(cards, rng);
        return cards.ToArray();
    }

    /// <summary>Records list "23.4초 · 18번" (time, then card flips).</summary>
    public static string FormatRecord(long timeMs, int flips) => $"{timeMs / 1000f:0.0}초 · {flips}번";

    private static void Shuffle(List<int> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>What a tap on a card did.</summary>
public enum MemoryFlip
{
    /// <summary>Nothing: the board is locked (comparing, or not started), or the card is already open.</summary>
    Ignored,
    /// <summary>The first card of a try is now face up.</summary>
    First,
    /// <summary>The second card matched the first; both stay open.</summary>
    Match,
    /// <summary>The second card is a different animal; both show until <see cref="MemoryBoard.Hide"/>.</summary>
    Mismatch,
}

/// <summary>
/// State of one game of animal match: which cards are face up or matched, the open first card, flip count and play
/// time. A mismatch locks the board until the game calls <see cref="Hide"/> (after showing the two cards). Misses
/// cost nothing; only the time (and, for ties, the number of flips) counts.
/// </summary>
public sealed class MemoryBoard
{
    private readonly int[] _cards;
    private readonly bool[] _matched;
    private int _first = -1;
    private int _second = -1;
    private double _seconds;

    public MemoryBoard(IReadOnlyList<int> cards)
    {
        _cards = new int[cards.Count];
        for (var i = 0; i < cards.Count; i++)
            _cards[i] = cards[i];
        _matched = new bool[_cards.Length];
    }

    public int Count => _cards.Length;
    public int PairTotal => _cards.Length / 2;
    public int Pairs { get; private set; }
    /// <summary>Cards turned face up so far (each card of each try counts once).</summary>
    public int Flips { get; private set; }
    /// <summary>Tries that did not match (for the result and autoplay checks; no penalty).</summary>
    public int Misses { get; private set; }
    public bool Finished => Pairs >= PairTotal;
    /// <summary>A mismatched pair is showing; taps are ignored until <see cref="Hide"/>.</summary>
    public bool Comparing => _second >= 0;
    /// <summary>The open first card of the current try, or -1.</summary>
    public int OpenCard => _first;
    public long TimeMs => (long)Math.Round(_seconds * 1000);

    public int AnimalAt(int index) => _cards[index];
    public bool IsMatched(int index) => _matched[index];

    /// <summary>True while the card shows its face (matched, the open first card, or a mismatched pair showing).</summary>
    public bool IsFaceUp(int index) => _matched[index] || index == _first || index == _second;

    /// <summary>Whether a tap on this card would flip it.</summary>
    public bool CanFlip(int index) =>
        !Finished && !Comparing && index >= 0 && index < _cards.Length && !IsFaceUp(index);

    public MemoryFlip Flip(int index)
    {
        if (!CanFlip(index))
            return MemoryFlip.Ignored;
        Flips++;
        if (_first < 0)
        {
            _first = index;
            return MemoryFlip.First;
        }
        if (_cards[_first] == _cards[index])
        {
            _matched[_first] = _matched[index] = true;
            _first = -1;
            Pairs++;
            return MemoryFlip.Match;
        }
        _second = index;
        Misses++;
        return MemoryFlip.Mismatch;
    }

    /// <summary>
    /// Turns a mismatched pair face down again and unlocks the board. Returns the two cards, or null if no pair was
    /// showing.
    /// </summary>
    public (int A, int B)? Hide()
    {
        if (!Comparing)
            return null;
        var pair = (_first, _second);
        _first = _second = -1;
        return pair;
    }

    /// <summary>Adds play time (the game calls this every frame while the board is in play).</summary>
    public void AddTime(double seconds)
    {
        if (!Finished && seconds > 0)
            _seconds += seconds;
    }

    /// <summary>The leaderboard row for a finished game: <see cref="ScoreEntry.Score"/> holds the flip count.</summary>
    public ScoreEntry Entry(string name, long unixTime) => new(name, Flips, TimeMs, unixTime);
}
