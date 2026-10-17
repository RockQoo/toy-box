using System;

namespace ToyBox.Core;

/// <summary>The obstacles of 공룡 점프. Small ones first; the tall cactus joins after the first speed-up.</summary>
public enum DinoObstacle { SmallCactus, Cactus, Rock }

/// <summary>An obstacle's hitbox in screen heights: width, and height above the ground line.</summary>
public readonly record struct ObstacleBox(float Width, float Height);

/// <summary>One step of the speed ramp (docs/games/06-dino-jump.md, "난이도 증가 예시").</summary>
public readonly record struct DinoSpeedStep(int Index, float StartTime, float Multiplier);

/// <summary>
/// Dino Jump rules that do not need the engine: speed ramp, jump arc, hitboxes, obstacle spacing and scoring.
/// Distances are in screen heights (the game multiplies by the viewport height) and times in seconds, so the
/// game plays the same on every aspect ratio. The dino stands still horizontally; the world moves left.
/// </summary>
public static class DinoRules
{
    /// <summary>Run speed at multiplier 1 with the "보통" start speed, in screen heights per second.</summary>
    public const float BaseRunSpeed = 0.55f;

    /// <summary>Jump apex, feet above the ground, in screen heights. Floaty on purpose: small hands react late.</summary>
    public const float JumpHeight = 0.40f;

    /// <summary>Seconds from take-off to landing. The same at every run speed.</summary>
    public const float JumpDuration = 0.75f;

    /// <summary>A press up to this long before landing still jumps, right on landing (jump buffering).</summary>
    public const float JumpBuffer = 0.15f;

    /// <summary>
    /// The dino has to touch an obstacle this long before it counts as a crash. A press in that moment still lifts
    /// the dino clear (a late-jump grace; the runner never walks off a ledge, so classic coyote time has nothing to do).
    /// </summary>
    public const float CrashGrace = 0.08f;

    /// <summary>The dino's hitbox: much narrower and shorter than the picture (the tail, head and feet never count).</summary>
    public const float DinoHitWidth = 0.10f;
    public const float DinoHitHeight = 0.16f;
    /// <summary>The hitbox bottom sits this far above the feet.</summary>
    public const float DinoHitLift = 0.02f;

    /// <summary>Time a child gets after landing before the next obstacle must be jumped at the latest.</summary>
    public const float Reaction = 0.4f;

    /// <summary>Seconds over which the speed eases to a new step (the banner shows at the start of it).</summary>
    public const float RampSeconds = 1.0f;

    public const int MilestoneEvery = 10;

    /// <summary>The GDD table (1.0 → 1.2 @10s → 1.5 @20s → 1.8 @30s), then one gentle last step and a cap.</summary>
    private static readonly DinoSpeedStep[] Steps =
    {
        new(0, 0f, 1.0f),
        new(1, 10f, 1.2f),
        new(2, 20f, 1.5f),
        new(3, 30f, 1.8f),
        new(4, 45f, 2.0f),
    };

    public static int StepCount => Steps.Length;
    public static float MaxMultiplier => Steps[^1].Multiplier;

    /// <summary>Launch speed and gravity of the jump arc (h = v·t − g·t²/2, apex <see cref="JumpHeight"/> at half time).</summary>
    public static float JumpVelocity => 4f * JumpHeight / JumpDuration;
    public static float Gravity => 8f * JumpHeight / (JumpDuration * JumpDuration);

    /// <summary>Feet height <paramref name="airTime"/> seconds after take-off (0 on the ground).</summary>
    public static float JumpHeightAt(float airTime) =>
        airTime <= 0 || airTime >= JumpDuration ? 0f : JumpVelocity * airTime - Gravity * airTime * airTime / 2f;

    public static DinoSpeedStep StepAt(float elapsed)
    {
        var step = Steps[0];
        foreach (var s in Steps)
            if (elapsed >= s.StartTime)
                step = s;
        return step;
    }

    /// <summary>Speed multiplier: holds each step, easing up over <see cref="RampSeconds"/> when a new step starts.</summary>
    public static float SpeedMultiplier(float elapsed)
    {
        var step = StepAt(elapsed);
        if (step.Index == 0)
            return step.Multiplier;
        var previous = Steps[step.Index - 1].Multiplier;
        var u = Math.Clamp((elapsed - step.StartTime) / RampSeconds, 0f, 1f);
        return previous + (step.Multiplier - previous) * u * u * (3 - 2 * u);
    }

    /// <summary>Run speed in screen heights per second.</summary>
    public static float RunSpeed(float elapsed, StartSpeed speed) =>
        BaseRunSpeed * SpeedMultiplier(elapsed) * RoundClock.SpeedScale(speed);

    public static float SlowestRunSpeed => BaseRunSpeed * RoundClock.SpeedScale(StartSpeed.Slow);
    public static float FastestRunSpeed => BaseRunSpeed * MaxMultiplier * RoundClock.SpeedScale(StartSpeed.Fast);

    // ---- Obstacles ----

    public static ObstacleBox Hitbox(DinoObstacle kind) => kind switch
    {
        DinoObstacle.SmallCactus => new ObstacleBox(0.05f, 0.115f),
        DinoObstacle.Cactus => new ObstacleBox(0.065f, 0.16f),
        _ => new ObstacleBox(0.11f, 0.07f), // Rock: low and wide
    };

    public static float MaxObstacleWidth
    {
        get
        {
            var max = 0f;
            foreach (DinoObstacle kind in Enum.GetValues(typeof(DinoObstacle)))
                max = Math.Max(max, Hitbox(kind).Width);
            return max;
        }
    }

    /// <summary>
    /// Which obstacle comes next. <paramref name="roll"/> is a random number in [0, 1). Before the first speed-up only
    /// the small ones (small cactus, rock); after that the tall cactus too.
    /// </summary>
    public static DinoObstacle PickObstacle(float elapsed, double roll)
    {
        if (StepAt(elapsed).Index == 0)
            return roll < 0.6 ? DinoObstacle.SmallCactus : DinoObstacle.Rock;
        return roll < 0.35 ? DinoObstacle.SmallCactus : roll < 0.75 ? DinoObstacle.Cactus : DinoObstacle.Rock;
    }

    /// <summary>How long one jump keeps the hitbox bottom above <paramref name="height"/> (0 if the apex is lower).</summary>
    public static float AirTimeAbove(float height)
    {
        if (height <= 0)
            return JumpDuration;
        if (height >= JumpHeight)
            return 0f;
        return JumpDuration * MathF.Sqrt(1 - height / JumpHeight);
    }

    private static float ClearHeight(DinoObstacle kind) => Hitbox(kind).Height - DinoHitLift;

    /// <summary>Seconds the dino's hitbox overlaps the obstacle horizontally while it passes at <paramref name="speed"/>.</summary>
    public static float OverlapTime(DinoObstacle kind, float speed) => (Hitbox(kind).Width + DinoHitWidth) / speed;

    /// <summary>
    /// The timing window: how many seconds of take-off moments clear this obstacle at <paramref name="speed"/>
    /// (negative: impossible). Wider with faster speed, since the jump covers more ground.
    /// </summary>
    public static float ClearWindow(DinoObstacle kind, float speed) =>
        AirTimeAbove(ClearHeight(kind)) - OverlapTime(kind, speed);

    /// <summary>
    /// When to take off for the obstacle whose front edge is <paramref name="distance"/> ahead of the hitbox front:
    /// earliest and latest moment from now that still clears it.
    /// </summary>
    public static (float Earliest, float Latest) JumpWindow(DinoObstacle kind, float distance, float speed)
    {
        var above = AirTimeAbove(ClearHeight(kind));
        var rise = (JumpDuration - above) / 2;  // From take-off until the hitbox is above the obstacle
        var enter = distance / speed;
        var exit = enter + OverlapTime(kind, speed);
        return (exit - (rise + above), enter - rise);
    }

    /// <summary>Seconds from now until the middle of the jump window (autoplay jumps when this reaches 0).</summary>
    public static float IdealJumpIn(DinoObstacle kind, float distance, float speed)
    {
        var (earliest, latest) = JumpWindow(kind, distance, speed);
        return (earliest + latest) / 2;
    }

    /// <summary>
    /// The shortest allowed distance between the front edges of two obstacles at <paramref name="speed"/>: a child
    /// who takes off at the last moment for one still lands with <see cref="Reaction"/> seconds left to jump the next.
    /// </summary>
    public static float MinGap(float speed) => speed * (JumpDuration + Reaction) + MaxObstacleWidth + DinoHitWidth;

    /// <summary>
    /// Distance (screen heights) from this obstacle's front edge to the next one's. A random time gap that shrinks as
    /// the run speeds up, but never below <see cref="MinGap"/>. <paramref name="roll"/> is in [0, 1).
    /// </summary>
    public static float NextGap(float elapsed, StartSpeed start, double roll)
    {
        var k = (SpeedMultiplier(elapsed) - 1f) / (MaxMultiplier - 1f);
        var shortest = Lerp(1.6f, 1.05f, k);
        var longest = Lerp(2.6f, 1.7f, k);
        var speed = RunSpeed(elapsed, start);
        var gap = speed * Lerp(shortest, longest, (float)roll);
        return Math.Max(gap, MinGap(speed));
    }

    /// <summary>
    /// Whether the dino's hitbox touches the obstacle. <paramref name="dinoLeft"/> and <paramref name="obstacleLeft"/>
    /// are hitbox left edges; <paramref name="feet"/> is the dino's feet height above the ground.
    /// </summary>
    public static bool Touches(float dinoLeft, float feet, float obstacleLeft, DinoObstacle kind)
    {
        var box = Hitbox(kind);
        var overlapX = obstacleLeft < dinoLeft + DinoHitWidth && obstacleLeft + box.Width > dinoLeft;
        return overlapX && feet + DinoHitLift < box.Height;
    }

    /// <summary>The obstacle is behind the dino: worth a point if it was not hit.</summary>
    public static bool Passed(float dinoLeft, float obstacleLeft, DinoObstacle kind) =>
        obstacleLeft + Hitbox(kind).Width < dinoLeft;

    public static bool CrossedMilestone(int before, int after, int every = MilestoneEvery) => after / every > before / every;

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}

/// <summary>What happened to the jump during one <see cref="DinoJump.Step"/>.</summary>
public enum JumpEvent { None, Landed, Jumped }

/// <summary>
/// The dino's vertical motion: one jump at a time (no jumping in the air), the GDD's rise → apex → fall → land on a
/// fixed arc, and a short input buffer so a press just before landing jumps again right away.
/// </summary>
public sealed class DinoJump
{
    private float _airTime = -1f; // < 0: on the ground
    private float _buffer;

    public bool Grounded => _airTime < 0;
    /// <summary>Feet above the ground in screen heights.</summary>
    public float Height => Grounded ? 0f : DinoRules.JumpHeightAt(_airTime);
    /// <summary>Upward speed in screen heights per second (negative while falling, 0 on the ground).</summary>
    public float Velocity => Grounded ? 0f : DinoRules.JumpVelocity - DinoRules.Gravity * _airTime;
    public bool Buffered => _buffer > 0;

    /// <summary>The jump button. Jumps now if on the ground (true); in the air the press is held for a moment.</summary>
    public bool Press()
    {
        if (Grounded)
        {
            _airTime = 0;
            _buffer = 0;
            return true;
        }
        _buffer = DinoRules.JumpBuffer;
        return false;
    }

    public JumpEvent Step(float dt)
    {
        if (Grounded)
            return JumpEvent.None;
        _airTime += dt;
        _buffer -= dt;
        if (_airTime < DinoRules.JumpDuration)
            return JumpEvent.None;
        // Landed. A buffered press takes off again at once, carrying over the leftover time.
        if (_buffer > 0)
        {
            _airTime -= DinoRules.JumpDuration;
            _buffer = 0;
            return JumpEvent.Jumped;
        }
        _airTime = -1f;
        _buffer = 0;
        return JumpEvent.Landed;
    }

    public void Reset()
    {
        _airTime = -1f;
        _buffer = 0;
    }
}
