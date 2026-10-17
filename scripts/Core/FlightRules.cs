using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToyBox.Core;

/// <summary>Enemy bubble amount and speed, and the boss's mood. Each level has its own leaderboard.</summary>
public enum FlightLevel { Easy, Normal, Hard }

/// <summary>The four naughty friends (the original's four enemy types).</summary>
public enum FlightEnemyKind { Cloud, Bee, Ufo, BigCloud }

/// <summary>Items that float in from the right.</summary>
public enum FlightItemKind { Power, Bomb, Heart, Candy }

/// <summary>The plane's own bubbles: two small ones (level 1), rainbow bubbles (2~3), big stars (4).</summary>
public enum FlightShotKind { Small, Bubble, Star }

/// <summary>Enemy bubble pictures (all white with a pale rim, tinted pastel in the game).</summary>
public enum FlightBulletLook { Small, Big, Oval, Star }

/// <summary>Pastel tints for enemy bubbles. No red on purpose.</summary>
public enum FlightTint { Pink, Sky, Lavender }

/// <summary>
/// One row of the appearance table: <see cref="Count"/> friends of <see cref="Kind"/> enter from the right at height
/// <see cref="Y"/>, one every <see cref="Interval"/> seconds, each next one <see cref="Rise"/> lower (negative: higher).
/// Times are on the flight clock (seconds at the "보통" speed).
/// </summary>
public readonly record struct FlightWave(float Time, FlightEnemyKind Kind, float Y, int Count = 1, float Interval = 0.7f, float Rise = 0f);

/// <summary>An item that floats in from the right at <see cref="Time"/> (flight clock, or boss clock for boss items).</summary>
public readonly record struct FlightItemCue(float Time, FlightItemKind Kind, float Y);

/// <summary>One bubble of a volley from the plane: vertical offset from the nose and angle (degrees, + is down).</summary>
public readonly record struct FlightShotSpec(float OffsetY, float AngleDeg, FlightShotKind Kind);

/// <summary>
/// An enemy bubble pattern: <see cref="Count"/> bubbles <see cref="StepDeg"/> apart (a ring when they go all the way
/// round, a fan otherwise, one aimed bubble for a count of 1), fired every <see cref="Interval"/> seconds (infinity:
/// once) from <see cref="SourceX"/> (fraction of the screen width, the nearest the shooter gets to the plane).
/// </summary>
public readonly record struct FlightPattern(string Name, float SourceX, int Count, float StepDeg, float Interval,
    FlightBulletLook Look, FlightTint Tint, float Speed)
{
    public bool IsRing => Count > 1 && Math.Abs(Count * StepDeg - 360f) < 0.01f;
}

/// <summary>
/// 하늘 비행 모험 rules that do not need the engine (docs/games/18-sky-flight.md): sizes and speeds, the appearance
/// table, the plane's bubble levels, enemy bubble patterns and their fairness, the boss's three phases and its 60-second
/// rule, the per-level multipliers and the score. Lengths are in screen heights (x from the left edge, y down from the
/// top, so the screen is <c>width × 1</c> with width = aspect ratio), times in seconds.
/// The running game lives in <see cref="FlightWorld"/>.
/// </summary>
public static class FlightRules
{
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
        new GameVariant("hard", "어려움"),
    };

    public static string VariantId(FlightLevel level) => Variants[(int)level].Id;

    /// <summary>The narrowest screen the game is laid out for (4:3 tablets); wider screens only add room.</summary>
    public const float MinAspect = 4f / 3f;

    // ---- The plane ----

    /// <summary>Plane picture width. The hit circle is 20% of it across (the original's small hit box).</summary>
    public const float PlaneWidth = 0.16f;
    public const float PlaneHitRadius = PlaneWidth * 0.2f / 2;

    /// <summary>Movement area: the left third of the screen (room to see what comes), clear of the top and bottom.</summary>
    public const float PlaneMinX = 0.08f;
    public const float PlaneMinY = 0.09f;
    public const float PlaneMaxY = 0.91f;
    public static float PlaneMaxX(float width) => width / 3f;
    public static Vector2 PlaneStart(float width) => new(Math.Min(0.22f, PlaneMaxX(width)), 0.5f);

    /// <summary>Key movement speed, and with SHIFT held (slow, the hit dot shows).</summary>
    public const float MoveSpeed = 0.85f;
    public const float SlowSpeed = 0.38f;

    /// <summary>After a bump: surprised and blinking this long, nothing can bump it again.</summary>
    public const float HitInvincible = 1.5f;
    /// <summary>After the heart's shield bubble pops instead.</summary>
    public const float ShieldInvincible = 1.0f;
    /// <summary>Enemy bubbles this close to the plane turn into harmless bubbles when it is bumped.</summary>
    public const float ClearRadius = 0.4f;

    // ---- The plane's bubbles ----

    public const int MaxPower = 4;
    public const float FireInterval = 0.18f;
    public const float ShotSpeed = 1.5f;
    /// <summary>Where the bubbles leave the plane, from its center.</summary>
    public static readonly Vector2 Muzzle = new(0.07f, 0.02f);

    private static readonly FlightShotSpec[][] ShotTable =
    {
        // Lv1: two side by side
        new FlightShotSpec[] { new(-0.025f, 0, FlightShotKind.Small), new(0.025f, 0, FlightShotKind.Small) },
        // Lv2: three ways
        new FlightShotSpec[] { new(0, -8, FlightShotKind.Bubble), new(0, 0, FlightShotKind.Bubble), new(0, 8, FlightShotKind.Bubble) },
        // Lv3: five ways
        new FlightShotSpec[]
        {
            new(0, -16, FlightShotKind.Bubble), new(0, -8, FlightShotKind.Bubble), new(0, 0, FlightShotKind.Bubble),
            new(0, 8, FlightShotKind.Bubble), new(0, 16, FlightShotKind.Bubble),
        },
        // Lv4: five ways of big stars
        new FlightShotSpec[]
        {
            new(0, -16, FlightShotKind.Star), new(0, -8, FlightShotKind.Star), new(0, 0, FlightShotKind.Star),
            new(0, 8, FlightShotKind.Star), new(0, 16, FlightShotKind.Star),
        },
    };

    /// <summary>The volley at bubble level 1~4 (clamped).</summary>
    public static IReadOnlyList<FlightShotSpec> ShotPattern(int power) => ShotTable[Math.Clamp(power, 1, MaxPower) - 1];

    public static float ShotRadius(FlightShotKind kind) => kind switch
    {
        FlightShotKind.Small => 0.022f,
        FlightShotKind.Bubble => 0.03f,
        _ => 0.036f,
    };

    public static float ShotDamage(FlightShotKind kind) => kind == FlightShotKind.Star ? 1.5f : 1f;

    // ---- Naughty friends ----

    public static float EnemyHp(FlightEnemyKind kind) => kind switch
    {
        FlightEnemyKind.Cloud => 5,
        FlightEnemyKind.Bee => 5,
        FlightEnemyKind.Ufo => 12,
        _ => 26,
    };

    /// <summary>Picture height (the hit circle is a little smaller than the picture).</summary>
    public static float EnemyHeight(FlightEnemyKind kind) => kind switch
    {
        FlightEnemyKind.Cloud => 0.13f,
        FlightEnemyKind.Bee => 0.12f,
        FlightEnemyKind.Ufo => 0.11f,
        _ => 0.17f,
    };

    public static float EnemyRadius(FlightEnemyKind kind) => kind switch
    {
        FlightEnemyKind.Cloud => 0.058f,
        FlightEnemyKind.Bee => 0.052f,
        FlightEnemyKind.Ufo => 0.062f,
        _ => 0.08f,
    };

    /// <summary>Stars a friend drops when it turns into a smiling cloud.</summary>
    public static int EnemyStars(FlightEnemyKind kind) => kind switch
    {
        FlightEnemyKind.Ufo => 2,
        FlightEnemyKind.BigCloud => 3,
        _ => 1,
    };

    /// <summary>Leftward speed while flying in (screen heights per second at the "보통" speed).</summary>
    public static float EnemySpeed(FlightEnemyKind kind) => kind switch
    {
        FlightEnemyKind.Cloud => 0.32f,
        FlightEnemyKind.Bee => 0.28f,
        FlightEnemyKind.Ufo => 0.45f,
        _ => 0.42f,
    };

    /// <summary>The UFO and the thunder cloud stop here (fraction of the width), wait and shoot, then drift away.</summary>
    public const float UfoStopX = 0.72f;
    public const float BigStopX = 0.76f;
    public const float UfoHover = 5f;
    public const float BigHover = 7f;
    public const float LeaveSpeed = 0.26f;

    /// <summary>Friends can be hit only once they are this far inside the right edge (the child sees them first).</summary>
    public const float HittableMargin = 0.05f;

    public static float StopX(FlightEnemyKind kind) => kind == FlightEnemyKind.Ufo ? UfoStopX : BigStopX;
    public static float HoverTime(FlightEnemyKind kind) => kind == FlightEnemyKind.Ufo ? UfoHover : BigHover;
    public static bool Hovers(FlightEnemyKind kind) => kind is FlightEnemyKind.Ufo or FlightEnemyKind.BigCloud;

    /// <summary>Bees wave up and down.</summary>
    public const float BeeAmplitude = 0.1f;
    public const float BeeFrequency = 0.45f;

    /// <summary>A smiling cloud floats up and away for this long.</summary>
    public const float HappyTime = 2.5f;
    public static readonly Vector2 HappyDrift = new(-0.08f, -0.22f);

    // ---- Enemy bubbles ----

    /// <summary>Drawn radius of an enemy bubble.</summary>
    public static float BulletRadius(FlightBulletLook look) => look switch
    {
        FlightBulletLook.Small => 0.022f,
        FlightBulletLook.Big => 0.034f,
        FlightBulletLook.Oval => 0.024f,
        _ => 0.03f,
    };

    /// <summary>The part that counts: a bit smaller than the picture, so grazes do not count.</summary>
    public static float BulletHitRadius(FlightBulletLook look) => BulletRadius(look) * 0.75f;

    /// <summary>Enemy bubble speed: slow / normal / a little fast.</summary>
    public static float BulletSpeed(FlightLevel level) => level switch
    {
        FlightLevel.Easy => 0.25f,
        FlightLevel.Normal => 0.30f,
        _ => 0.34f,
    };

    /// <summary>How often enemies shoot compared to easy (the GDD's 1 / 1.5 / 2).</summary>
    public static float FireMultiplier(FlightLevel level) => level switch
    {
        FlightLevel.Easy => 1f,
        FlightLevel.Normal => 1.5f,
        _ => 2f,
    };

    /// <summary>Enemy bubbles on screen at most (new ones are skipped beyond this).</summary>
    public static int MaxBullets(FlightLevel level) => level switch
    {
        FlightLevel.Easy => 30,
        FlightLevel.Normal => 55,
        _ => 80,
    };

    /// <summary>Bees (normal and hard) and small clouds (hard) blow one bubble when they pass this x.</summary>
    public const float BeeFireX = 0.8f;
    public const float CloudFireX = 0.85f;

    public const float UfoInterval = 1.6f;
    public const float BigInterval = 2.6f;
    public const int BigFanCount = 5;
    public const float FanStepDeg = 18f;

    /// <summary>The narrowest gap a child should have to slip through, edge to edge (about the plane's body height).</summary>
    public const float MinGap = 0.12f;

    public static FlightPattern UfoAim(FlightLevel level) =>
        new("UFO 조준", UfoStopX, 1, 0, UfoInterval / FireMultiplier(level), FlightBulletLook.Small, FlightTint.Pink, BulletSpeed(level));

    public static FlightPattern BigFan(FlightLevel level) =>
        new("번개구름 부채꼴", BigStopX, BigFanCount, FanStepDeg, BigInterval / FireMultiplier(level), FlightBulletLook.Big,
            FlightTint.Sky, BulletSpeed(level) * 0.9f);

    /// <summary>A bee's single straight bubble (normal and hard only).</summary>
    public static FlightPattern? BeeShot(FlightLevel level) => level == FlightLevel.Easy
        ? null
        : new("꿀벌 한 발", BeeFireX, 1, 0, float.PositiveInfinity, FlightBulletLook.Oval, FlightTint.Lavender, BulletSpeed(level));

    /// <summary>A small cloud's single aimed bubble (hard only).</summary>
    public static FlightPattern? CloudShot(FlightLevel level) => level == FlightLevel.Hard
        ? new("먹구름 한 발", CloudFireX, 1, 0, float.PositiveInfinity, FlightBulletLook.Oval, FlightTint.Lavender, BulletSpeed(level))
        : null;

    // ---- The boss ----

    /// <summary>Boss mood ("기분"): how many bubble hits it takes to make it smile.</summary>
    public static float BossMood(FlightLevel level) => level switch
    {
        FlightLevel.Easy => 340,
        FlightLevel.Normal => 460,
        _ => 580,
    };

    /// <summary>After this long the boss gets tired and smiles by itself: every round reaches the end.</summary>
    public const float BossTimeLimit = 60f;
    public const float BossX = 0.8f;
    public const float BossSway = 0.03f;
    public const float BossHeight = 0.30f;
    /// <summary>The boss's body as an ellipse (half width, half height) for the plane's bubbles.</summary>
    public static readonly Vector2 BossRadii = new(0.24f, 0.12f);
    public const float BossEnterTime = 2.5f;
    /// <summary>Smiling, rainbow and confetti before the result.</summary>
    public const float BossHappyTime = 3.5f;
    /// <summary>After the flight the boss comes once the sky is clear, or after this long anyway.</summary>
    public const float BossIntroMin = 1.5f;
    public const float BossIntroMax = 5f;
    public const int BossStars = 20;
    public const int BossTiredStars = 10;

    /// <summary>Phase 1 above 70% mood, phase 2 above 40%, phase 3 below.</summary>
    public static int BossPhase(float mood, float maxMood)
    {
        var f = maxMood <= 0 ? 0 : mood / maxMood;
        return f > 0.7f ? 1 : f > 0.4f ? 2 : 3;
    }

    public static int BossRingCount(FlightLevel level) => level switch
    {
        FlightLevel.Easy => 8,
        FlightLevel.Normal => 10,
        _ => 12,
    };

    public static int BossFanCount(FlightLevel level) => level == FlightLevel.Hard ? 7 : 5;

    public const float BossRingInterval = 2.6f;
    public const float BossFanInterval = 3.2f;
    /// <summary>Puffing the cheeks before a fan.</summary>
    public const float BossChargeTime = 0.9f;
    public const float BossSummonInterval = 5f;
    /// <summary>Phase 3 rings come this much less often (the summoned clouds are the main thing).</summary>
    public const float Phase3RingSlowdown = 1.5f;
    /// <summary>Boss bubbles start from the edge of its body, not its middle.</summary>
    public const float BossMuzzle = 0.1f;
    /// <summary>Fan bubbles start this far out along their own direction, already spread (not one blob).</summary>
    public const float FanMuzzle = 0.07f;

    public static FlightPattern BossRing(FlightLevel level) =>
        new("대장 원형", BossX - BossSway, BossRingCount(level), 360f / BossRingCount(level),
            BossRingInterval / FireMultiplier(level), FlightBulletLook.Star, FlightTint.Lavender, BulletSpeed(level));

    public static FlightPattern BossFan(FlightLevel level) =>
        new("대장 부채꼴", BossX - BossSway, BossFanCount(level), FanStepDeg, Math.Max(BossFanInterval / FireMultiplier(level), BossChargeTime + 0.5f),
            FlightBulletLook.Big, FlightTint.Pink, BulletSpeed(level) * 0.9f);

    public static FlightPattern BossRing3(FlightLevel level) =>
        new("대장 원형(3단계)", BossX - BossSway, BossRingCount(level), 360f / BossRingCount(level),
            BossRingInterval * Phase3RingSlowdown / FireMultiplier(level), FlightBulletLook.Small, FlightTint.Sky, BulletSpeed(level));

    /// <summary>Every enemy bubble pattern used at <paramref name="level"/>.</summary>
    public static IEnumerable<FlightPattern> Patterns(FlightLevel level)
    {
        yield return UfoAim(level);
        yield return BigFan(level);
        if (BeeShot(level) is { } bee)
            yield return bee;
        if (CloudShot(level) is { } cloud)
            yield return cloud;
        yield return BossRing(level);
        yield return BossFan(level);
        yield return BossRing3(level);
    }

    // ---- Pattern geometry ----

    /// <summary>Angle (radians) from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    public static float Aim(Vector2 from, Vector2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);

    public static Vector2 Direction(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    /// <summary>Angles of a pattern's bubbles: centered on <paramref name="center"/>, <see cref="FlightPattern.StepDeg"/> apart.</summary>
    public static float[] Angles(FlightPattern pattern, float center)
    {
        var angles = new float[pattern.Count];
        var step = pattern.StepDeg * MathF.PI / 180f;
        for (var i = 0; i < pattern.Count; i++)
            angles[i] = center + (i - (pattern.Count - 1) / 2f) * step;
        return angles;
    }

    /// <summary>
    /// The closest an emitter at <paramref name="sourceX"/> (fraction of the width) ever is to where the plane can fly,
    /// on the narrowest screen.
    /// </summary>
    public static float NearestReach(float sourceX) => sourceX * MinAspect - PlaneMaxX(MinAspect);

    /// <summary>Open space between two neighboring bubbles of a volley (edge to edge) once they are <paramref name="distance"/> from the source.</summary>
    public static float NeighborGap(FlightPattern p, float distance) =>
        2 * distance * MathF.Sin(p.StepDeg * MathF.PI / 360f) - 2 * BulletHitRadius(p.Look);

    /// <summary>Open space between one volley and the next along the same line.</summary>
    public static float FollowGap(FlightPattern p) =>
        float.IsPositiveInfinity(p.Interval) ? float.PositiveInfinity : p.Speed * p.Interval - 2 * BulletHitRadius(p.Look);

    // ---- Items ----

    public const int StartBombs = 2;
    public const int MaxBombs = 5;
    public const int CandyStars = 5;
    /// <summary>An item the plane does not need any more (full) gives a couple of stars instead.</summary>
    public const int SpareItemStars = 2;
    public const float ItemSpeed = 0.16f;
    public const float MagnetRadius = 0.2f;
    public const float CollectRadius = 0.08f;
    public const float BombDamage = 10f;
    /// <summary>A firework calms the boss by this share of its mood.</summary>
    public const float BombBossShare = 0.06f;
    /// <summary>A firework turns enemy bubbles into stars; only this many of them count.</summary>
    public const int BombStarCap = 10;
    public const float BombInvincible = 1.0f;
    /// <summary>Dropped stars wait a moment, then fly to the plane.</summary>
    public const float StarLinger = 0.6f;

    // ---- The appearance table ----

    /// <summary>The flight before the boss (flight clock seconds; the "속도" option runs the clock faster or slower).</summary>
    public const float FlightTime = 90f;

    public static readonly IReadOnlyList<FlightWave> Waves = new FlightWave[]
    {
        new(2, FlightEnemyKind.Cloud, 0.30f, 3, 0.7f),
        new(6, FlightEnemyKind.Cloud, 0.70f, 3, 0.7f),
        new(10, FlightEnemyKind.Bee, 0.50f, 3, 0.8f),
        new(15, FlightEnemyKind.Ufo, 0.35f),
        new(19, FlightEnemyKind.Cloud, 0.20f, 4, 0.6f, 0.1f),
        new(23, FlightEnemyKind.Bee, 0.68f, 4, 0.8f),
        new(28, FlightEnemyKind.Ufo, 0.25f),
        new(29, FlightEnemyKind.Ufo, 0.72f),
        new(34, FlightEnemyKind.Cloud, 0.50f, 5, 0.55f),
        new(39, FlightEnemyKind.BigCloud, 0.45f),
        new(47, FlightEnemyKind.Bee, 0.28f, 4, 0.8f),
        new(49, FlightEnemyKind.Cloud, 0.75f, 3, 0.7f),
        new(54, FlightEnemyKind.Ufo, 0.55f),
        new(58, FlightEnemyKind.Cloud, 0.22f, 4, 0.6f),
        new(60, FlightEnemyKind.Bee, 0.70f, 3, 0.8f),
        new(64, FlightEnemyKind.BigCloud, 0.30f),
        new(66, FlightEnemyKind.Ufo, 0.75f),
        new(73, FlightEnemyKind.Bee, 0.50f, 5, 0.7f),
        new(77, FlightEnemyKind.Ufo, 0.30f),
        new(78, FlightEnemyKind.Ufo, 0.70f),
        new(82, FlightEnemyKind.Cloud, 0.80f, 4, 0.6f, -0.12f),
        new(85, FlightEnemyKind.Bee, 0.30f, 3, 0.8f),
    };

    public static readonly IReadOnlyList<FlightItemCue> FlightItems = new FlightItemCue[]
    {
        new(12, FlightItemKind.Power, 0.45f),
        new(26, FlightItemKind.Heart, 0.50f),
        new(32, FlightItemKind.Power, 0.60f),
        new(44, FlightItemKind.Candy, 0.35f),
        new(52, FlightItemKind.Power, 0.40f),
        new(62, FlightItemKind.Bomb, 0.55f),
        new(70, FlightItemKind.Candy, 0.65f),
        new(76, FlightItemKind.Power, 0.50f),
        new(88, FlightItemKind.Heart, 0.45f),
    };

    /// <summary>Items during the boss, on the boss clock.</summary>
    public static readonly IReadOnlyList<FlightItemCue> BossItems = new FlightItemCue[]
    {
        new(15, FlightItemKind.Heart, 0.3f),
        new(30, FlightItemKind.Bomb, 0.7f),
        new(45, FlightItemKind.Candy, 0.5f),
    };

    /// <summary>The table spelled out one friend at a time, in order.</summary>
    public static List<(float Time, FlightEnemyKind Kind, float Y)> Spawns()
    {
        var list = new List<(float Time, FlightEnemyKind Kind, float Y)>();
        foreach (var w in Waves)
            for (var i = 0; i < w.Count; i++)
                list.Add((w.Time + i * w.Interval, w.Kind, w.Y + i * w.Rise));
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    // ---- Score ----

    public static string FormatRecord(int stars) => $"별 {stars}개";
}
