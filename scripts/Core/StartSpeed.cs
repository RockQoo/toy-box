using System;

namespace ToyBox.Core;

/// <summary>The "속도" option on the customization screens (balloon, fruit, dino). Stored in settings as its int value.</summary>
public enum StartSpeed { Slow, Normal, Fast }

/// <summary>Rules shared by the timed games: start-speed scaling and the HUD's whole seconds.</summary>
public static class RoundClock
{
    /// <summary>Multiplier from the start-speed option. Games scale movement speed by it and spawn intervals by its square root.</summary>
    public static float SpeedScale(StartSpeed speed) => speed switch
    {
        StartSpeed.Slow => 0.75f,
        StartSpeed.Fast => 1.3f,
        _ => 1f,
    };

    /// <summary>Whole seconds left to show on the HUD (rounds up, so "1초" shows until time is really over).</summary>
    public static int SecondsLeft(float elapsed, float total) =>
        Math.Max(0, (int)MathF.Ceiling(total - elapsed - 1e-4f));
}
