using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>What a single question asks for.</summary>
public enum QuizKind { Color, Number }

/// <summary>The question type picked on the setup screen ("섞어서" alternates color and number questions at random).</summary>
public enum QuizMode { Color, Number, Mix }

/// <summary>Difficulty: how many cards to choose from. Each level has its own leaderboard (see <see cref="QuizRules.Variants"/>).</summary>
public enum QuizLevel { Easy, Normal, Hard }

/// <summary>
/// One question. <see cref="Answer"/> and <see cref="Choices"/> are color indices (<see cref="QuizRules.ColorNames"/>
/// order) for color questions and the numbers themselves (1~9) for number questions. Choices are in display order.
/// </summary>
public sealed record QuizQuestion(QuizKind Kind, int Answer, IReadOnlyList<int> Choices)
{
    public int AnswerSlot => IndexOf(Choices, Answer);

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }
}

/// <summary>Number / color quiz rules that do not need the engine (docs/games/05-number-color-quiz.md).</summary>
public static class QuizRules
{
    public const int QuestionCount = 10;

    /// <summary>Shape cards: 0 red circle, 1 blue square, 2 yellow triangle, 3 green star, 4 purple heart, 5 orange oval.</summary>
    public const int ColorCount = 6;

    public static readonly string[] ColorNames = { "빨간색", "파란색", "노란색", "초록색", "보라색", "주황색" };

    /// <summary>Leaderboard variants, easiest first. Only the difficulty splits the boards; the question type does not.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(QuizLevel level) => Variants[(int)level].Id;

    /// <summary>GDD: 쉬움 2 / 보통 4 / 어려움 6 choices.</summary>
    public static int ChoiceCount(QuizLevel level) => level switch
    {
        QuizLevel.Easy => 2,
        QuizLevel.Normal => 4,
        _ => 6,
    };

    /// <summary>Largest number asked. 쉬움/보통 use 1~5 (the painted cards); 어려움 needs 6 different numbers, so 1~9.</summary>
    public static int MaxNumber(QuizLevel level) => level == QuizLevel.Hard ? 9 : 5;

    /// <summary>The values a question of this kind can use at this level.</summary>
    public static int[] Pool(QuizKind kind, QuizLevel level)
    {
        if (kind == QuizKind.Color)
        {
            var colors = new int[ColorCount];
            for (var i = 0; i < ColorCount; i++)
                colors[i] = i;
            return colors;
        }
        var numbers = new int[MaxNumber(level)];
        for (var i = 0; i < numbers.Length; i++)
            numbers[i] = i + 1;
        return numbers;
    }

    /// <summary>"빨간색을 찾아주세요!" / "숫자 3을 찾아주세요!" (을/를 follows how the number is read: 삼을, 사를).</summary>
    public static string Prompt(QuizKind kind, int value) => kind == QuizKind.Color
        ? $"{ColorNames[value]}을 찾아주세요!"
        : $"숫자 {value}{NumberParticle(value)} 찾아주세요!";

    /// <summary>Object particle after a digit 0~9 read in Sino-Korean (일 이 삼 사 오 육 칠 팔 구 영).</summary>
    public static string NumberParticle(int value) => (value % 10) switch
    {
        2 or 4 or 5 or 9 => "를",
        _ => "을",
    };
}

/// <summary>
/// Deals the questions of a round. Answers come from a shuffled bag per kind, so all values show up before any repeats,
/// and the same answer never comes twice in a row. Distractors are random other values; the answer's slot never stays
/// the same three times running.
/// </summary>
public sealed class QuizDeck
{
    private readonly QuizMode _mode;
    private readonly QuizLevel _level;
    private readonly Random _rng;
    private readonly Dictionary<QuizKind, List<int>> _bags = new();
    private int _lastAnswer = -1;
    private QuizKind? _lastKind;
    private int _lastSlot = -1;
    private int _sameSlotRun;
    private int _kindRun;

    public QuizDeck(QuizMode mode, QuizLevel level, Random rng)
    {
        _mode = mode;
        _level = level;
        _rng = rng;
    }

    public QuizQuestion Next()
    {
        var kind = NextKind();
        var answer = DrawAnswer(kind);
        var count = QuizRules.ChoiceCount(_level);

        // Distractors: other values from the pool, picked at random
        var others = new List<int>();
        foreach (var v in QuizRules.Pool(kind, _level))
            if (v != answer)
                others.Add(v);
        Shuffle(others);
        var choices = others.GetRange(0, Math.Min(count - 1, others.Count));

        var slot = _rng.Next(choices.Count + 1);
        if (slot == _lastSlot && _sameSlotRun >= 2)
            slot = (slot + 1 + _rng.Next(choices.Count)) % (choices.Count + 1);
        choices.Insert(slot, answer);
        _sameSlotRun = slot == _lastSlot ? _sameSlotRun + 1 : 1;
        _lastSlot = slot;

        _lastAnswer = answer;
        _kindRun = kind == _lastKind ? _kindRun + 1 : 1;
        _lastKind = kind;
        return new QuizQuestion(kind, answer, choices);
    }

    private QuizKind NextKind()
    {
        if (_mode != QuizMode.Mix)
            return _mode == QuizMode.Color ? QuizKind.Color : QuizKind.Number;
        // Random, but not more than two of the same kind in a row
        if (_lastKind is { } last && _kindRun >= 2)
            return last == QuizKind.Color ? QuizKind.Number : QuizKind.Color;
        return _rng.Next(2) == 0 ? QuizKind.Color : QuizKind.Number;
    }

    private int DrawAnswer(QuizKind kind)
    {
        if (!_bags.TryGetValue(kind, out var bag) || bag.Count == 0)
        {
            bag = new List<int>(QuizRules.Pool(kind, _level));
            Shuffle(bag);
            _bags[kind] = bag;
        }
        // No immediate repeat, also across a bag refill (a color and a number with the same index are different answers)
        var index = bag.Count - 1;
        if (kind == _lastKind && bag[index] == _lastAnswer && bag.Count > 1)
            index = _rng.Next(bag.Count - 1);
        var answer = bag[index];
        bag.RemoveAt(index);
        return answer;
    }

    private void Shuffle(List<int> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>How a tap on an answer card turned out.</summary>
public enum QuizAnswer { Retry, CorrectFirstTry, CorrectAfterRetry }

/// <summary>
/// Score keeping for one round. A wrong tap only means "try again" (no penalty, the question stays); the score counts
/// the questions answered right on the first try (= stars). <see cref="TimeMs"/> adds up only the time spent looking
/// at answerable cards, so animations and pauses do not count.
/// </summary>
public sealed class QuizRound
{
    private readonly List<bool> _firstTry = new();
    private int _misses;
    private double _seconds;

    /// <summary>Zero-based number of the question being asked (== questions done).</summary>
    public int Index => _firstTry.Count;
    public bool Finished => Index >= QuizRules.QuestionCount;
    public int Score { get; private set; }
    public bool Perfect => Finished && Score == QuizRules.QuestionCount;
    public long TimeMs => (long)Math.Round(_seconds * 1000);
    /// <summary>Wrong taps on the current question so far.</summary>
    public int Misses => _misses;
    /// <summary>For each finished question, whether it was right on the first try.</summary>
    public IReadOnlyList<bool> FirstTry => _firstTry;

    public void AddTime(double seconds)
    {
        if (!Finished && seconds > 0)
            _seconds += seconds;
    }

    public QuizAnswer Answer(bool correct)
    {
        if (Finished)
            throw new InvalidOperationException("The round is over.");
        if (!correct)
        {
            _misses++;
            return QuizAnswer.Retry;
        }
        var first = _misses == 0;
        _firstTry.Add(first);
        _misses = 0;
        if (first)
            Score++;
        return first ? QuizAnswer.CorrectFirstTry : QuizAnswer.CorrectAfterRetry;
    }
}
