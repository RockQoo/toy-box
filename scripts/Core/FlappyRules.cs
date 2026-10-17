using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>Gap size. Each level has its own leaderboard (see <see cref="FlappyRules.Variants"/>).</summary>
public enum FlappyLevel { Easy, Normal, Hard }

/// <summary>One step of the speed ramp: from <see cref="FromScore"/> pillars on, the scroll runs at <see cref="Multiplier"/>.</summary>
public readonly record struct FlappySpeedStep(int Index, int FromScore, float Multiplier);

/// <summary>A pillar pair: the gap's center height and size (screen heights from the top), and its place in the run.</summary>
public readonly record struct FlappyGap(int Index, float Center, float Size)
{
    public float Top => Center - Size / 2;
    public float Bottom => Center + Size / 2;
}

/// <summary>
/// 아기새 날기 rules that do not need the engine (docs/games/09-flappy.md): flight physics, the gap table, pillar
/// spacing and heights, hitboxes and the speed ramp. Lengths are in screen heights with y growing downward from the
/// top of the screen (0 = ceiling, <see cref="GroundY"/> = ground), times in seconds. The bird stays at one x; the
/// pillars move left.
/// </summary>
public static class FlappyRules
{
    /// <summary>Leaderboard variants, easiest first: scores from different gap sizes are not comparable.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(FlappyLevel level) => Variants[(int)level].Id;

    // ---- Flight ----

    /// <summary>The ground line: the bottom 10% of the screen is ground.</summary>
    public const float GroundY = 0.9f;

    /// <summary>A flap sets the bird's upward speed to this (screen heights per second).</summary>
    public const float FlapSpeed = 0.8f;

    /// <summary>Gravity, weaker than the original so a child can find the rhythm.</summary>
    public const float Gravity = 2.4f;

    /// <summary>Falling never gets faster than this (lower than the original, too).</summary>
    public const float MaxFallSpeed = 0.75f;

    /// <summary>Upward speed after touching the ground: a little hop, so the bird "walks" along it bouncing.</summary>
    public const float BounceSpeed = 0.36f;

    /// <summary>Half the bird's picture height: how close its center comes to the ceiling and the ground.</summary>
    public const float BodyRadius = 0.05f;

    /// <summary>Bird picture width; the hit circle is only 55% of it across.</summary>
    public const float BirdWidth = 0.11f;
    public const float HitRadius = BirdWidth * 0.55f / 2;

    /// <summary>The bird must touch a pillar this long before it counts (a flap in that moment still gets clear).</summary>
    public const float CrashGrace = 0.08f;

    /// <summary>How high one flap lifts the bird from a standstill.</summary>
    public static float FlapHeight => FlapSpeed * FlapSpeed / (2 * Gravity);

    /// <summary>Seconds from a flap to the top of its arc.</summary>
    public static float FlapApexTime => FlapSpeed / Gravity;

    /// <summary>
    /// A child's steady climb: one flap at the top of each arc (no frantic tapping) rises one <see cref="FlapHeight"/>
    /// per <see cref="FlapApexTime"/>.
    /// </summary>
    public static float ClimbRate => FlapHeight / FlapApexTime;

    /// <summary>Seconds to climb <paramref name="height"/> with steady flaps.</summary>
    public static float ClimbTime(float height) => Math.Max(0f, height) / ClimbRate;

    /// <summary>Seconds to drop <paramref name="height"/> from a standstill (gravity up to the fall speed limit).</summary>
    public static float FallTime(float height)
    {
        if (height <= 0)
            return 0f;
        var tMax = MaxFallSpeed / Gravity;
        var dMax = MaxFallSpeed * tMax / 2;
        if (height <= dMax)
            return MathF.Sqrt(2 * height / Gravity);
        return tMax + (height - dMax) / MaxFallSpeed;
    }

    // ---- Pillars ----

    /// <summary>Trunk width that counts for hits (the leaves and the cap do not), and the picture width around it.</summary>
    public const float TrunkWidth = 0.10f;
    public const float PillarPictureWidth = 0.16f;

    /// <summary>Every pillar shows at least this much trunk at the top and the bottom.</summary>
    public const float EdgeMargin = 0.08f;

    /// <summary>The first few pillars are a warm-up: a wider gap and small height changes.</summary>
    public const int WarmupCount = 3;
    public const float WarmupGapScale = 1.3f;
    public const float WarmupDiffScale = 0.4f;

    /// <summary>The first gap is centered here; the bird waits at the same height.</summary>
    public const float StartY = 0.45f;

    /// <summary>Seconds a child gets after leaving one pillar before it must start moving toward the next gap.</summary>
    public const float Reaction = 0.4f;

    /// <summary>Distance between two pillars' front edges when time allows (it grows at high speed, see <see cref="MinSpacing"/>).</summary>
    public const float BaseSpacing = 0.85f;

    /// <summary>The GDD table: gap height and the largest height change to the next gap.</summary>
    public static float GapSize(FlappyLevel level) => level switch
    {
        FlappyLevel.Easy => 0.42f,
        FlappyLevel.Normal => 0.36f,
        _ => 0.31f,
    };

    public static float MaxDiff(FlappyLevel level) => level switch
    {
        FlappyLevel.Easy => 0.20f,
        FlappyLevel.Normal => 0.28f,
        _ => 0.35f,
    };

    public static bool IsWarmup(int index) => index < WarmupCount;

    public static float GapSizeAt(FlappyLevel level, int index) =>
        GapSize(level) * (IsWarmup(index) ? WarmupGapScale : 1f);

    public static float MaxDiffAt(FlappyLevel level, int index) =>
        MaxDiff(level) * (IsWarmup(index) ? WarmupDiffScale : 1f);

    /// <summary>Where a gap of <paramref name="size"/> may be centered, leaving trunk at both ends.</summary>
    public static (float Min, float Max) CenterRange(float size) =>
        (EdgeMargin + size / 2, GroundY - EdgeMargin - size / 2);

    /// <summary>
    /// The gap of pillar number <paramref name="index"/>. The first is centered at <see cref="StartY"/>; each next one
    /// moves up or down by up to the level's height change. <paramref name="roll"/> is a random number in [0, 1).
    /// </summary>
    public static FlappyGap NextGap(FlappyLevel level, FlappyGap? previous, double roll)
    {
        var index = previous is { } p ? p.Index + 1 : 0;
        var size = GapSizeAt(level, index);
        var (min, max) = CenterRange(size);
        if (previous is not { } prev)
            return new FlappyGap(0, Math.Clamp(StartY, min, max), size);
        var diff = MaxDiffAt(level, index);
        var center = prev.Center + diff * (float)(roll * 2 - 1);
        // Pushed off an edge: go the other way instead, so the walls do not make runs of same-height gaps
        if (center < min)
            center = Math.Min(max, min + (min - center));
        else if (center > max)
            center = Math.Max(min, max - (center - max));
        return new FlappyGap(index, center, size);
    }

    /// <summary>
    /// Seconds to get from anywhere in one gap to the far edge of the next, the slower way (climbing or falling).
    /// The bird can be anywhere a hit circle fits, so the worst case is the full height change plus the room it had;
    /// on the way down it may also have just flapped (the rise and the fall back come on top).
    /// </summary>
    public static float TravelTime(FlappyGap from, FlappyGap to)
    {
        // Worst start: the far side of the first gap; target: just inside the near edge of the next
        var down = (to.Top + HitRadius) - (from.Top + HitRadius);
        var up = (from.Bottom - HitRadius) - (to.Bottom - HitRadius);
        // Going down, the child may have flapped just before: that flap's rise and fall back come first
        var fall = down > 0 ? FlapApexTime + FallTime(down + FlapHeight) : 0f;
        return Math.Max(fall, up > 0 ? ClimbTime(up) : 0f);
    }

    /// <summary>
    /// The shortest allowed distance between the front edges of two pillars at <paramref name="speed"/>: after the bird
    /// clears the first trunk it gets <see cref="Reaction"/> seconds, then enough time to reach the next gap.
    /// </summary>
    public static float MinSpacing(float speed, FlappyGap from, FlappyGap to) =>
        TrunkWidth + 2 * HitRadius + speed * (Reaction + TravelTime(from, to));

    /// <summary>Distance between the front edges of two pillars at <paramref name="speed"/>.</summary>
    public static float Spacing(float speed, FlappyGap from, FlappyGap to) =>
        Math.Max(BaseSpacing, MinSpacing(speed, from, to));

    // ---- Speed ----

    /// <summary>Scroll speed at multiplier 1 with the "보통" start speed, in screen heights per second.</summary>
    public const float BaseSpeed = 0.40f;

    /// <summary>A little faster every 10 pillars, no faster after 30.</summary>
    private static readonly FlappySpeedStep[] Steps =
    {
        new(0, 0, 1.0f),
        new(1, 10, 1.1f),
        new(2, 20, 1.2f),
        new(3, 30, 1.3f),
    };

    public const int MilestoneEvery = 10;

    /// <summary>Seconds over which the speed eases to a new step.</summary>
    public const float RampSeconds = 1.0f;

    public static int StepCount => Steps.Length;
    public static float MaxMultiplier => Steps[^1].Multiplier;

    public static FlappySpeedStep StepAt(int score)
    {
        var step = Steps[0];
        foreach (var s in Steps)
            if (score >= s.FromScore)
                step = s;
        return step;
    }

    /// <summary>Speed multiplier <paramref name="sinceStep"/> seconds after the current step began (eases in).</summary>
    public static float SpeedMultiplier(int score, float sinceStep = RampSeconds)
    {
        var step = StepAt(score);
        if (step.Index == 0)
            return step.Multiplier;
        var previous = Steps[step.Index - 1].Multiplier;
        var u = Math.Clamp(sinceStep / RampSeconds, 0f, 1f);
        return previous + (step.Multiplier - previous) * u * u * (3 - 2 * u);
    }

    public static float Speed(int score, StartSpeed start, float sinceStep = RampSeconds) =>
        BaseSpeed * SpeedMultiplier(score, sinceStep) * RoundClock.SpeedScale(start);

    /// <summary>
    /// The speed to plan the next pillar's spacing with: the step the run may have reached by the time the bird gets
    /// there (two more pillars are still ahead of it on the screen), so a speed-up never makes a spacing too short.
    /// </summary>
    public static float PlanningSpeed(int score, StartSpeed start) =>
        BaseSpeed * StepAt(score + 2).Multiplier * RoundClock.SpeedScale(start);

    public static float FastestSpeed => BaseSpeed * MaxMultiplier * RoundClock.SpeedScale(StartSpeed.Fast);
    public static float SlowestSpeed => BaseSpeed * RoundClock.SpeedScale(StartSpeed.Slow);

    // ---- Hits ----

    /// <summary>
    /// Whether the bird's hit circle (center <paramref name="birdX"/>, <paramref name="birdY"/>) touches either trunk of
    /// the pillar whose left edge is at <paramref name="pillarLeft"/>. The trunks run from the gap edges to beyond the
    /// screen, so the bird cannot slip over or under a pillar.
    /// </summary>
    public static bool Touches(float birdX, float birdY, float pillarLeft, FlappyGap gap)
    {
        var nearestX = Math.Clamp(birdX, pillarLeft, pillarLeft + TrunkWidth);
        var dx = birdX - nearestX;
        // Upper trunk: everything above the gap top; lower trunk: everything below the gap bottom
        var dyTop = birdY < gap.Top ? 0f : birdY - gap.Top;
        var dyBottom = birdY > gap.Bottom ? 0f : gap.Bottom - birdY;
        var r2 = HitRadius * HitRadius;
        return dx * dx + dyTop * dyTop < r2 || dx * dx + dyBottom * dyBottom < r2;
    }

    /// <summary>The pillar is behind the bird: a point if it was not hit.</summary>
    public static bool Passed(float birdX, float pillarLeft) => pillarLeft + TrunkWidth < birdX - HitRadius;

    public static bool CrossedMilestone(int before, int after, int every = MilestoneEvery) => after / every > before / every;
}

/// <summary>What happened to the bird during one <see cref="FlappyBird.Step"/>.</summary>
public enum FlightEvent { None, Ceiling, Bounced }

/// <summary>
/// The bird's vertical motion. It hovers until the first flap; after that gravity pulls it down, a flap sets a fixed
/// upward speed, the ceiling stops it with a soft "콩" and the ground bounces it in little hops (neither ends the run).
/// </summary>
public sealed class FlappyBird
{
    /// <summary>Center height in screen heights from the top.</summary>
    public float Y { get; private set; } = FlappyRules.StartY;
    /// <summary>Downward speed in screen heights per second (negative while rising).</summary>
    public float Velocity { get; private set; }
    /// <summary>The first flap happened (until then it just hovers).</summary>
    public bool Started { get; private set; }
    public bool OnGround => Y >= FlappyRules.GroundY - FlappyRules.BodyRadius - 1e-4f;

    public void Flap()
    {
        Started = true;
        Velocity = -FlappyRules.FlapSpeed;
    }

    public FlightEvent Step(float dt)
    {
        if (!Started)
            return FlightEvent.None;
        Velocity = Math.Min(Velocity + FlappyRules.Gravity * dt, FlappyRules.MaxFallSpeed);
        Y += Velocity * dt;
        var top = FlappyRules.BodyRadius;
        var bottom = FlappyRules.GroundY - FlappyRules.BodyRadius;
        if (Y < top)
        {
            Y = top;
            var wasRising = Velocity < 0;
            Velocity = 0;
            return wasRising ? FlightEvent.Ceiling : FlightEvent.None;
        }
        if (Y > bottom)
        {
            Y = bottom;
            Velocity = -FlappyRules.BounceSpeed;
            return FlightEvent.Bounced;
        }
        return FlightEvent.None;
    }

    public void Reset(float y = FlappyRules.StartY)
    {
        Y = y;
        Velocity = 0;
        Started = false;
    }
}
