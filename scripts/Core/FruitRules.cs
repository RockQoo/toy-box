using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>One step of the fruit difficulty ramp (docs/games/02-fruit-catch.md, "난이도").</summary>
/// <param name="FallSpeed">Viewport heights per second.</param>
/// <param name="SpawnInterval">Seconds between spawns (one spawn may drop a group of fruits).</param>
public readonly record struct FruitPhase(int Index, float StartTime, float FallSpeed, float SpawnInterval, int MinGroup,
    int MaxGroup);

/// <summary>Fruit Catch rules that do not need the engine: difficulty ramp, spawn groups, bonus fruit, scoring, catching.</summary>
public static class FruitRules
{
    public const float PlaySeconds = 30f;

    /// <summary>Number of fruit kinds the player can pick (apple, strawberry, banana, orange, grape, watermelon).</summary>
    public const int KindCount = 6;

    /// <summary>All kinds on. Settings store the picked kinds as a bit mask.</summary>
    public const int AllKinds = (1 << KindCount) - 1;

    /// <summary>The GDD's five fruits (watermelon is available but off by default).</summary>
    public const int DefaultKinds = AllKinds & ~(1 << 5);

    /// <summary>Chance that a spawned fruit is the golden bonus fruit (only one on screen at a time).</summary>
    public const float BonusChance = 0.07f;

    /// <summary>No bonus fruit in the first seconds, so the first catches teach the plain rule.</summary>
    public const float BonusFromTime = 3f;

    /// <summary>Seconds between the fruits of one group (20~30초: "거의 동시에").</summary>
    public const float GroupStagger = 0.18f;

    private static readonly FruitPhase[] Phases =
    {
        new(0, 0f, 0.24f, 1.10f, 1, 1),   // 0~10초: 하나씩, 느리게
        new(1, 10f, 0.32f, 0.85f, 1, 1),  // 10~20초: 빨라지고 자주
        new(2, 20f, 0.40f, 1.20f, 2, 3),  // 20~30초: 2~3개가 거의 동시에, 더 빠르게
    };

    public static int PhaseCount => Phases.Length;

    public static FruitPhase PhaseAt(float elapsed)
    {
        var phase = Phases[0];
        foreach (var p in Phases)
            if (elapsed >= p.StartTime)
                phase = p;
        return phase;
    }

    public static float FallSpeed(float elapsed, StartSpeed speed) =>
        PhaseAt(elapsed).FallSpeed * RoundClock.SpeedScale(speed);

    public static float SpawnInterval(float elapsed, StartSpeed speed) =>
        PhaseAt(elapsed).SpawnInterval / MathF.Sqrt(RoundClock.SpeedScale(speed));

    /// <summary>How many fruits drop together at this time. <paramref name="roll"/> is a random number in [0, 1).</summary>
    public static int GroupSize(float elapsed, double roll)
    {
        var phase = PhaseAt(elapsed);
        var size = phase.MinGroup + (int)(roll * (phase.MaxGroup - phase.MinGroup + 1));
        return Math.Clamp(size, phase.MinGroup, phase.MaxGroup);
    }

    /// <summary>Whether the next fruit is the golden one. <paramref name="roll"/> is a random number in [0, 1).</summary>
    public static bool IsBonus(float elapsed, bool bonusOnScreen, double roll) =>
        !bonusOnScreen && elapsed >= BonusFromTime && roll < BonusChance;

    /// <summary>Points for a catch: normal fruit +1, golden fruit +3.</summary>
    public static int Points(bool bonus) => bonus ? 3 : 1;

    /// <summary>True when going from <paramref name="before"/> to <paramref name="after"/> passes a multiple of <paramref name="every"/> (a +3 can jump over one).</summary>
    public static bool CrossedMilestone(int before, int after, int every = 10) =>
        every > 0 && after / every > before / every;

    public static int SecondsLeft(float elapsed) => RoundClock.SecondsLeft(elapsed, PlaySeconds);

    /// <summary>
    /// True if a fruit moving from <paramref name="prevY"/> to <paramref name="y"/> crossed the basket rim this frame
    /// while horizontally over the basket opening. Using the crossing (not overlap) means a fast fruit cannot skip the rim.
    /// </summary>
    public static bool IsCaught(float fruitX, float prevY, float y, float basketX, float rimY, float halfOpening) =>
        prevY < rimY && y >= rimY && MathF.Abs(fruitX - basketX) <= halfOpening;

    // ---- Picked kinds (bit mask) ----

    public static bool Has(int mask, int kind) => (mask & (1 << kind)) != 0;

    /// <summary>Cleans a mask read from settings: unknown bits dropped, and an empty mask falls back to the defaults.</summary>
    public static int Sanitize(int mask)
    {
        mask &= AllKinds;
        return mask == 0 ? DefaultKinds : mask;
    }

    /// <summary>Turns a kind on or off. Turning off the last picked kind is refused (at least one fruit must fall).</summary>
    public static int Toggle(int mask, int kind, out bool changed)
    {
        var next = mask ^ (1 << kind);
        changed = (next & AllKinds) != 0;
        return changed ? next : mask;
    }

    public static List<int> Kinds(int mask)
    {
        var kinds = new List<int>();
        for (var i = 0; i < KindCount; i++)
            if (Has(mask, i))
                kinds.Add(i);
        return kinds;
    }
}
