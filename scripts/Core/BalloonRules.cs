using System;

namespace ToyBox.Core;

/// <summary>One step of the balloon difficulty ramp (GDD section 1, "난이도 변화").</summary>
public readonly record struct BalloonPhase(int Index, float StartTime, float RiseSpeed, float SpawnInterval);

/// <summary>Balloon Pop rules that do not need the engine: play time, difficulty ramp, start-speed scaling.</summary>
public static class BalloonRules
{
    public const float PlaySeconds = 30f;

    /// <summary>Rise speed is in viewport heights per second, so it feels the same at any resolution.</summary>
    private static readonly BalloonPhase[] Phases =
    {
        new(0, 0f, 0.20f, 1.00f),   // 느림
        new(1, 10f, 0.28f, 0.70f),  // 보통
        new(2, 20f, 0.38f, 0.50f),  // 빠름
        new(3, 25f, 0.50f, 0.35f),  // 매우 빠름
    };

    public static int PhaseCount => Phases.Length;

    /// <summary>The phase active at <paramref name="elapsed"/> seconds into play.</summary>
    public static BalloonPhase PhaseAt(float elapsed)
    {
        var phase = Phases[0];
        foreach (var p in Phases)
            if (elapsed >= p.StartTime)
                phase = p;
        return phase;
    }

    public static float RiseSpeed(float elapsed, StartSpeed speed) =>
        PhaseAt(elapsed).RiseSpeed * RoundClock.SpeedScale(speed);

    public static float SpawnInterval(float elapsed, StartSpeed speed) =>
        PhaseAt(elapsed).SpawnInterval / MathF.Sqrt(RoundClock.SpeedScale(speed));

    /// <summary>Whole seconds left to show on the HUD (rounds up, so "1초" shows until time is really over).</summary>
    public static int SecondsLeft(float elapsed) => RoundClock.SecondsLeft(elapsed, PlaySeconds);
}
