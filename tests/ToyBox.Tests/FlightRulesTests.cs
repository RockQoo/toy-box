using System.Numerics;
using ToyBox.Core;
using Xunit;
using Xunit.Abstractions;

namespace ToyBox.Tests;

public class FlightRulesTests
{
    private const float Dt = 1 / 60f;
    private const float Width = 16f / 9f;
    private static readonly FlightLevel[] Levels = { FlightLevel.Easy, FlightLevel.Normal, FlightLevel.Hard };
    private static readonly StartSpeed[] Starts = { StartSpeed.Slow, StartSpeed.Normal, StartSpeed.Fast };

    private readonly ITestOutputHelper _out;

    public FlightRulesTests(ITestOutputHelper output) => _out = output;

    // ---- Tables ----

    [Fact]
    public void VariantsAreEasyNormalHard()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, FlightRules.Variants.Select(v => v.Id));
        Assert.Equal("normal", FlightRules.VariantId(FlightLevel.Normal));
    }

    [Fact]
    public void LevelsScaleBubblesLikeTheGdd()
    {
        // Amount 1 / 1.5 / 2, speed slow → a little fast, boss mood small → large
        Assert.Equal(1f, FlightRules.FireMultiplier(FlightLevel.Easy));
        Assert.Equal(1.5f, FlightRules.FireMultiplier(FlightLevel.Normal));
        Assert.Equal(2f, FlightRules.FireMultiplier(FlightLevel.Hard));
        Assert.True(FlightRules.BulletSpeed(FlightLevel.Easy) < FlightRules.BulletSpeed(FlightLevel.Normal));
        Assert.True(FlightRules.BulletSpeed(FlightLevel.Normal) < FlightRules.BulletSpeed(FlightLevel.Hard));
        Assert.True(FlightRules.BossMood(FlightLevel.Easy) < FlightRules.BossMood(FlightLevel.Normal));
        Assert.True(FlightRules.BossMood(FlightLevel.Normal) < FlightRules.BossMood(FlightLevel.Hard));
        Assert.Equal(30, FlightRules.MaxBullets(FlightLevel.Easy));
        Assert.Equal(80, FlightRules.MaxBullets(FlightLevel.Hard));
        Assert.True(FlightRules.MaxBullets(FlightLevel.Hard) <= FlightWorld.BulletPool);
        // Easy: only the UFO and the thunder cloud (and the boss) shoot
        Assert.Null(FlightRules.BeeShot(FlightLevel.Easy));
        Assert.Null(FlightRules.CloudShot(FlightLevel.Easy));
        Assert.NotNull(FlightRules.BeeShot(FlightLevel.Normal));
    }

    [Fact]
    public void ShotLevelsGoTwoThreeFiveAndBigStars()
    {
        Assert.Equal(2, FlightRules.ShotPattern(1).Count);
        Assert.Equal(3, FlightRules.ShotPattern(2).Count);
        Assert.Equal(5, FlightRules.ShotPattern(3).Count);
        Assert.Equal(5, FlightRules.ShotPattern(4).Count);
        Assert.All(FlightRules.ShotPattern(4), s => Assert.Equal(FlightShotKind.Star, s.Kind));
        Assert.All(FlightRules.ShotPattern(1), s => Assert.Equal(0, s.AngleDeg));
        // Clamped
        Assert.Equal(FlightRules.ShotPattern(4), FlightRules.ShotPattern(9));
        Assert.Equal(FlightRules.ShotPattern(1), FlightRules.ShotPattern(0));
    }

    [Fact]
    public void AppearanceTableIsOrderedAndOnScreen()
    {
        var spawns = FlightRules.Spawns();
        Assert.True(spawns.Count >= 50);
        for (var i = 1; i < spawns.Count; i++)
            Assert.True(spawns[i].Time >= spawns[i - 1].Time);
        Assert.All(spawns, s =>
        {
            Assert.InRange(s.Time, 0f, FlightRules.FlightTime);
            Assert.InRange(s.Y, 0.15f, 0.85f);
        });
        foreach (var kind in Enum.GetValues<FlightEnemyKind>())
            Assert.Contains(spawns, s => s.Kind == kind);
        Assert.All(FlightRules.FlightItems, c => Assert.InRange(c.Time, 0f, FlightRules.FlightTime));
        foreach (var kind in Enum.GetValues<FlightItemKind>())
            Assert.Contains(FlightRules.FlightItems, c => c.Kind == kind);
        // Enough power items to reach the top level before the boss, with one to spare
        Assert.True(FlightRules.FlightItems.Count(c => c.Kind == FlightItemKind.Power) >= FlightRules.MaxPower);
        Assert.All(FlightRules.BossItems, c => Assert.InRange(c.Time, 0f, FlightRules.BossTimeLimit));
    }

    // ---- Patterns ----

    [Fact]
    public void PatternAnglesSpreadAroundTheCenter()
    {
        var fan = FlightRules.BigFan(FlightLevel.Easy);
        var angles = FlightRules.Angles(fan, MathF.PI);
        Assert.Equal(fan.Count, angles.Length);
        Assert.Equal(MathF.PI, angles[fan.Count / 2], 4); // The middle bubble flies at the target
        for (var i = 1; i < angles.Length; i++)
            Assert.Equal(fan.StepDeg * MathF.PI / 180f, angles[i] - angles[i - 1], 4);
        Assert.False(fan.IsRing);

        var ring = FlightRules.BossRing(FlightLevel.Normal);
        Assert.True(ring.IsRing);
        var ringAngles = FlightRules.Angles(ring, 0);
        Assert.Equal(2 * MathF.PI, (ringAngles[^1] - ringAngles[0]) + ring.StepDeg * MathF.PI / 180f, 3);

        Assert.Equal(MathF.PI, FlightRules.Aim(new Vector2(1, 0.5f), new Vector2(0, 0.5f)), 4);
        Assert.Equal(MathF.PI / 2, FlightRules.Aim(Vector2.Zero, new Vector2(0, 1)), 4);
    }

    [Theory]
    [InlineData(FlightLevel.Easy)]
    [InlineData(FlightLevel.Normal)]
    [InlineData(FlightLevel.Hard)]
    public void EveryPatternLeavesAGapAChildFitsThrough(FlightLevel level)
    {
        foreach (var p in FlightRules.Patterns(level))
        {
            // Where the plane can be at its closest to the shooter (narrowest screen), neighbors are far enough apart…
            var reach = FlightRules.NearestReach(p.SourceX);
            Assert.True(reach > 0.4f, $"{p.Name}: the plane can get within {reach:0.00} of the shooter");
            if (p.Count > 1)
            {
                var gap = FlightRules.NeighborGap(p, reach);
                _out.WriteLine($"{level} {p.Name}: side gap {gap:0.000} at {reach:0.00}");
                Assert.True(gap >= FlightRules.MinGap, $"{level} {p.Name}: side gap {gap:0.000} < {FlightRules.MinGap}");
            }
            // …and one volley is well behind the last (room to cross the line between two aimed bubbles)
            var follow = FlightRules.FollowGap(p);
            _out.WriteLine($"{level} {p.Name}: follow gap {follow:0.000}");
            Assert.True(follow >= FlightRules.MinGap * 1.5f, $"{level} {p.Name}: follow gap {follow:0.000}");
            // Slow bubbles: at least a second and a half to cross the space between shooter and plane
            Assert.True(reach / p.Speed >= 1.5f, $"{level} {p.Name}: {reach / p.Speed:0.0}s to reach the plane");
        }
    }

    [Fact]
    public void MinGapIsWiderThanThePlanesHitDotWithRoomToSpare()
    {
        Assert.True(FlightRules.MinGap >= 3 * 2 * FlightRules.PlaneHitRadius);
        Assert.Equal(FlightRules.PlaneWidth * 0.2f / 2, FlightRules.PlaneHitRadius, 5);
    }

    // ---- The boss ----

    [Theory]
    [InlineData(1.0f, 1)]
    [InlineData(0.71f, 1)]
    [InlineData(0.70f, 2)]
    [InlineData(0.41f, 2)]
    [InlineData(0.40f, 3)]
    [InlineData(0.01f, 3)]
    public void BossPhaseChangesAtSeventyAndFortyPercent(float fraction, int phase) =>
        Assert.Equal(phase, FlightRules.BossPhase(fraction * 100, 100));

    [Theory]
    [InlineData(FlightLevel.Easy, StartSpeed.Slow)]
    [InlineData(FlightLevel.Normal, StartSpeed.Normal)]
    [InlineData(FlightLevel.Hard, StartSpeed.Fast)]
    public void BossSmilesByItselfWhenNobodyShootsIt(FlightLevel level, StartSpeed speed)
    {
        // A plane that hides in a corner and never hits the boss: the round still ends
        var w = new FlightWorld(level, speed, Width);
        var phases = new HashSet<int>();
        var limit = FlightRules.FlightTime / RoundClock.SpeedScale(speed) + FlightRules.BossIntroMax + FlightRules.BossEnterTime
            + FlightRules.BossTimeLimit + FlightRules.BossHappyTime + 1;
        var t = 0f;
        while (w.Phase != FlightPhase.Done && t < limit)
        {
            w.MovePlane(new Vector2(-1, -1)); // Top left corner
            // Keep the boss out of reach of the plane's bubbles: undo any damage so it never changes phase by mood
            if (w.Boss.State == FlightBossState.Fighting)
                w.Boss.Mood = w.Boss.MaxMood;
            w.Step(Dt, default);
            foreach (var e in w.Events)
                if (e.Kind == SkyEventKind.BossPhase)
                    phases.Add(e.Value);
            w.ClearEvents();
            t += Dt;
        }
        Assert.Equal(FlightPhase.Done, w.Phase);
        Assert.True(w.Boss.Tired);
        Assert.True(w.Boss.Clock >= FlightRules.BossTimeLimit - 0.1f);
        Assert.True(w.Stars >= FlightRules.BossTiredStars);
    }

    [Theory]
    [InlineData(FlightLevel.Easy)]
    [InlineData(FlightLevel.Normal)]
    [InlineData(FlightLevel.Hard)]
    public void BossGoesThroughThreePhasesAsItCalmsDown(FlightLevel level)
    {
        var w = SkipToBoss(level);
        var seen = new List<int> { w.Boss.Phase }; // Phase 1 begins as it arrives
        var t = 0f;
        while (w.Phase == FlightPhase.Boss && t < 70)
        {
            if (w.Boss.State == FlightBossState.Fighting)
                w.Boss.Mood -= w.Boss.MaxMood * 0.02f * Dt; // Calms down over 50 s
            w.Step(Dt, default);
            foreach (var e in w.Events)
                if (e.Kind == SkyEventKind.BossPhase)
                    seen.Add(e.Value);
            w.ClearEvents();
            t += Dt;
        }
        Assert.Equal(new[] { 1, 2, 3 }, seen);
        Assert.Equal(FlightPhase.BossHappy, w.Phase);
        Assert.False(w.Boss.Tired);
    }

    // ---- Bumps, items, fireworks ----

    [Fact]
    public void ABumpSurprisesWithoutTakingAnything()
    {
        var w = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        Fill(w, FlightItemKind.Power);
        Fill(w, FlightItemKind.Candy);
        var power = w.Power;
        var stars = w.Stars;
        var bombs = w.Bombs;
        // An enemy bubble right on the plane, another close by, one far away
        Put(w, 0, w.Plane, Vector2.Zero);
        Put(w, 1, w.Plane + new Vector2(0.2f, 0), Vector2.Zero);
        Put(w, 2, w.Plane + new Vector2(0.9f, 0), Vector2.Zero);
        w.Step(Dt, default);
        Assert.Contains(w.Events, e => e.Kind == SkyEventKind.PlaneSurprised);
        Assert.Equal(1, w.Hits);
        Assert.Equal(power, w.Power);
        Assert.Equal(bombs, w.Bombs);
        Assert.True(w.Stars >= stars);
        Assert.True(w.Invincible > 1.4f);
        // The nearby bubble popped (no second bump in a row), the far one is still there
        Assert.False(w.Bullets[1].Alive);
        Assert.True(w.Bullets[2].Alive);

        // Blinking: another bubble on the plane does nothing
        Put(w, 3, w.Plane, Vector2.Zero);
        w.ClearEvents();
        w.Step(Dt, default);
        Assert.Equal(1, w.Hits);
        // After the blinking it counts again
        for (var t = 0f; t < FlightRules.HitInvincible; t += Dt)
            w.Step(Dt, default);
        Put(w, 4, w.Plane, Vector2.Zero);
        w.Step(Dt, default);
        Assert.Equal(2, w.Hits);
    }

    [Fact]
    public void TheHeartsShieldTakesOneBump()
    {
        var w = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        Fill(w, FlightItemKind.Heart);
        Assert.True(w.Shield);
        Put(w, 0, w.Plane, Vector2.Zero);
        w.ClearEvents();
        w.Step(Dt, default);
        Assert.False(w.Shield);
        Assert.Contains(w.Events, e => e.Kind == SkyEventKind.ShieldPopped);
        Assert.DoesNotContain(w.Events, e => e.Kind == SkyEventKind.PlaneSurprised);
        Assert.Equal(0f, w.Surprised);
    }

    [Fact]
    public void PowerGoesUpToFourAndThenGivesStars()
    {
        var w = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        Assert.Equal(1, w.Power);
        for (var i = 0; i < 3; i++)
            Fill(w, FlightItemKind.Power);
        Assert.Equal(4, w.Power);
        var stars = w.Stars;
        Fill(w, FlightItemKind.Power);
        Assert.Equal(4, w.Power);
        Assert.Equal(stars + FlightRules.SpareItemStars, w.Stars);
        Fill(w, FlightItemKind.Candy);
        Assert.Equal(stars + FlightRules.SpareItemStars + FlightRules.CandyStars, w.Stars);
    }

    [Fact]
    public void FireworksTurnBubblesIntoStars()
    {
        var w = new FlightWorld(FlightLevel.Hard, StartSpeed.Normal, Width);
        Assert.Equal(FlightRules.StartBombs, w.Bombs);
        for (var i = 0; i < 15; i++)
            Put(w, i, new Vector2(1f, 0.1f + i * 0.05f), Vector2.Zero);
        Assert.True(w.UseBomb());
        Assert.Equal(FlightRules.StartBombs - 1, w.Bombs);
        Assert.Equal(0, w.BulletCount);
        Assert.All(w.Bullets, b => Assert.False(b.Alive));
        Assert.Contains(w.Events, e => e.Kind == SkyEventKind.BombUsed);
        // The stars fly to the plane; only the first ten count
        for (var t = 0f; t < 3; t += Dt)
        {
            w.ClearEvents();
            w.Step(Dt, default);
        }
        Assert.Equal(FlightRules.BombStarCap, w.Stars);
        // Not again right away, and never below zero
        Assert.True(w.UseBomb());
        Assert.False(w.UseBomb());
        Assert.Equal(0, w.Bombs);
    }

    [Fact]
    public void BubblesMakeFriendsSmileAndDropStars()
    {
        var w = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        var t = 0f;
        var happy = new List<FlightEnemyKind>();
        while (t < 12)
        {
            // Stay in line with the first clouds
            var target = w.Enemies.FirstOrDefault(e => e.State == FlightEnemyState.Active);
            var dy = target.State == FlightEnemyState.Active ? target.Pos.Y - w.Plane.Y : 0;
            w.Step(Dt, new FlightInput { Move = new Vector2(0, Math.Clamp(dy * 8, -1, 1)) });
            foreach (var e in w.Events)
                if (e.Kind == SkyEventKind.EnemyHappy)
                    happy.Add((FlightEnemyKind)e.Value);
            w.ClearEvents();
            t += Dt;
        }
        Assert.True(happy.Count >= 3);
        Assert.True(w.Friends >= 3);
        Assert.True(w.Stars >= 3);
    }

    [Fact]
    public void ThePlaneStaysInTheLeftThird()
    {
        var w = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        w.MovePlane(new Vector2(5, 5));
        Assert.Equal(FlightRules.PlaneMaxX(Width), w.Plane.X, 4);
        Assert.Equal(FlightRules.PlaneMaxY, w.Plane.Y, 4);
        for (var i = 0; i < 300; i++)
            w.Step(Dt, new FlightInput { Move = new Vector2(-1, -1) });
        Assert.Equal(FlightRules.PlaneMinX, w.Plane.X, 4);
        Assert.Equal(FlightRules.PlaneMinY, w.Plane.Y, 4);
        // SHIFT moves slower
        var a = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        var b = new FlightWorld(FlightLevel.Easy, StartSpeed.Normal, Width);
        a.Step(0.1f, new FlightInput { Move = new Vector2(0, 1) });
        b.Step(0.1f, new FlightInput { Move = new Vector2(0, 1), Slow = true });
        Assert.True(a.Plane.Y - 0.5f > 2 * (b.Plane.Y - 0.5f));
    }

    // ---- Whole rounds ----

    [Theory]
    [InlineData(FlightLevel.Easy, StartSpeed.Normal)]
    [InlineData(FlightLevel.Normal, StartSpeed.Normal)]
    [InlineData(FlightLevel.Hard, StartSpeed.Normal)]
    [InlineData(FlightLevel.Hard, StartSpeed.Fast)]
    [InlineData(FlightLevel.Easy, StartSpeed.Slow)]
    public void ABotPlaysAWholeRound(FlightLevel level, StartSpeed speed)
    {
        foreach (var width in new[] { FlightRules.MinAspect, 16f / 9f, 20f / 9f })
        {
            var w = new FlightWorld(level, speed, width, seed: 3);
            var bot = new FlightBot();
            var t = 0f;
            var bossStart = 0f;
            var nearPeak = 0;
            while (w.Phase != FlightPhase.Done && t < 300)
            {
                if (w.Phase == FlightPhase.Boss && bossStart == 0)
                    bossStart = t;
                w.Step(Dt, bot.Decide(w, Dt));
                w.ClearEvents();
                Assert.True(w.BulletCount <= FlightRules.MaxBullets(level));
                nearPeak = Math.Max(nearPeak, w.Bullets.Count(b => b.Alive && Vector2.Distance(b.Pos, w.Plane) < 0.3f));
                t += Dt;
            }
            _out.WriteLine($"{level} {speed} {width:0.00}: {t:0}s, boss {t - bossStart:0}s{(w.Boss.Tired ? " (tired)" : "")}, " +
                $"stars {w.Stars}, friends {w.Friends}, power {w.Power}, bumps {w.Hits}, peak {w.PeakBullets}, near {nearPeak}");
            Assert.Equal(FlightPhase.Done, w.Phase);
            // An aiming child calms the boss before it gets tired
            Assert.False(w.Boss.Tired);
            Assert.Equal(FlightRules.MaxPower, w.Power);
            Assert.True(w.Stars >= 60, $"stars {w.Stars}");
            // A dodging pilot gets through with few bumps: the bubbles can be avoided
            Assert.True(w.Hits <= 10, $"{level} {width:0.00}: {w.Hits} bumps");
            Assert.True(nearPeak <= 12, $"{level}: {nearPeak} bubbles near the plane at once");
        }
    }

    [Fact]
    public void TheFlightClockFollowsTheSpeedOption()
    {
        foreach (var speed in Starts)
        {
            var w = new FlightWorld(FlightLevel.Easy, speed, Width);
            var t = 0f;
            while (w.Phase == FlightPhase.Flight)
            {
                w.MovePlane(new Vector2(-1, -1));
                w.Step(Dt, default);
                w.ClearEvents();
                t += Dt;
            }
            Assert.Equal(FlightRules.FlightTime / RoundClock.SpeedScale(speed), t, 0);
        }
    }

    // ---- Helpers ----

    private static FlightWorld SkipToBoss(FlightLevel level)
    {
        var w = new FlightWorld(level, StartSpeed.Fast, Width);
        while (w.Phase != FlightPhase.Boss || w.Boss.State != FlightBossState.Fighting)
        {
            w.MovePlane(new Vector2(-1, -1));
            w.Step(Dt, default);
            w.ClearEvents();
        }
        // Keep the plane's bubbles away from the boss: only the test calms it down
        return w;
    }

    private static void Put(FlightWorld w, int slot, Vector2 pos, Vector2 vel)
    {
        w.Bullets[slot] = new FlightBullet { Alive = true, Look = FlightBulletLook.Small, Pos = pos, Vel = vel };
        // Keep the count in step with the pool (the world counts what it emits itself)
        typeof(FlightWorld).GetProperty(nameof(FlightWorld.BulletCount))!.SetValue(w, w.Bullets.Count(b => b.Alive));
    }

    /// <summary>Drops an item on the plane and lets it be picked up.</summary>
    private static void Fill(FlightWorld w, FlightItemKind kind)
    {
        var slot = Array.FindIndex(w.Pickups, p => !p.Alive);
        var pk = kind switch
        {
            FlightItemKind.Power => FlightPickupKind.Power,
            FlightItemKind.Bomb => FlightPickupKind.Bomb,
            FlightItemKind.Heart => FlightPickupKind.Heart,
            _ => FlightPickupKind.Candy,
        };
        w.Pickups[slot] = new FlightPickup { Alive = true, Kind = pk, Pos = w.Plane, BaseY = w.Plane.Y };
        w.Step(Dt, default);
        w.ClearEvents();
    }
}
