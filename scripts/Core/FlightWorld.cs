using System;
using System.Collections.Generic;
using System.Numerics;

namespace ToyBox.Core;

public enum FlightPhase { Flight, BossIntro, Boss, BossHappy, Done }

public enum FlightBossState { Absent, Entering, Fighting, Happy }

public enum FlightEnemyState { None, Active, Happy }

public enum FlightPickupKind { Star, Power, Bomb, Heart, Candy }

public enum SkyEventKind
{
    /// <summary>A bubble hit a friend (Value: kind).</summary>
    EnemyHit,
    /// <summary>A friend turned into a smiling cloud (Value: kind).</summary>
    EnemyHappy,
    /// <summary>An enemy bubble turned into a harmless bubble and popped.</summary>
    BulletPopped,
    PlaneSurprised,
    ShieldPopped,
    /// <summary>An item was taken (Value: <see cref="FlightItemKind"/>; Extra: the new bubble level for power).</summary>
    ItemTaken,
    /// <summary>An item the plane had no room for gave stars (Value: kind).</summary>
    ItemSpare,
    /// <summary>Stars reached the plane (Value: how many).</summary>
    StarTaken,
    BombUsed,
    BossAppears,
    /// <summary>The boss's attack changed (Value: phase 1~3).</summary>
    BossPhase,
    BossCharge,
    BossHit,
    BossSummon,
    /// <summary>The boss smiled (Value: 1 when it got tired by itself).</summary>
    BossHappy,
    Done,
}

public readonly record struct SkyEvent(SkyEventKind Kind, Vector2 Pos, int Value = 0, int Extra = 0);

/// <summary>Keyboard-style input for one step: a direction (length up to 1) and SHIFT.</summary>
public struct FlightInput
{
    public Vector2 Move;
    public bool Slow;
}

public struct FlightShot
{
    public bool Alive;
    public FlightShotKind Kind;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Age;
}

public struct FlightBullet
{
    public bool Alive;
    public FlightBulletLook Look;
    public FlightTint Tint;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Age;
}

public struct FlightEnemy
{
    public FlightEnemyState State;
    public FlightEnemyKind Kind;
    public Vector2 Pos;
    public float BaseY;
    public float Hp;
    public float Age;
    /// <summary>UFO / thunder cloud: 0 flying in, 1 waiting and shooting, 2 drifting away.</summary>
    public int Mode;
    public float Hover;
    public float FireTimer;
    public bool Fired;
    /// <summary>Called by the boss (flies straight and a little faster).</summary>
    public bool Summoned;
    /// <summary>Seconds of the "hit" flash left.</summary>
    public float Flash;
    public float HappyAge;
}

public struct FlightPickup
{
    public bool Alive;
    public FlightPickupKind Kind;
    public Vector2 Pos;
    public Vector2 Vel;
    public float BaseY;
    public float Age;
    public int Value;
    public bool Pulled;
}

public sealed class FlightBoss
{
    public FlightBossState State;
    public Vector2 Pos;
    public float Mood;
    public float MaxMood;
    public int Phase;
    /// <summary>Seconds of fighting (the 60-second rule).</summary>
    public float Clock;
    public float Age;
    public float RingTimer;
    public float FanTimer;
    public float SummonTimer;
    public float Flash;
    public bool Tired;
    public int NextItem;
    public float HitSoundGap;

    /// <summary>Puffing the cheeks right before a fan.</summary>
    public bool Charging => State == FlightBossState.Fighting && Phase == 2 && FanTimer <= FlightRules.BossChargeTime;
    public float MoodFraction => MaxMood <= 0 ? 0 : Math.Max(0, Mood) / MaxMood;
}

/// <summary>
/// One round of 하늘 비행 모험 without the engine: the plane, its bubbles, the friends from the appearance table,
/// enemy bubbles, items and stars, and the boss. The game calls <see cref="Step"/> with small time steps and reads
/// <see cref="Events"/> after each step for sounds and effects; it draws straight from the pools.
/// Nothing is ever lost: a bump only surprises the plane, and the boss smiles by itself after
/// <see cref="FlightRules.BossTimeLimit"/>, so every round reaches <see cref="FlightPhase.Done"/>.
/// </summary>
public sealed class FlightWorld
{
    public const int ShotPool = 120;
    public const int BulletPool = 80;
    public const int EnemyPool = 24;
    public const int PickupPool = 200;

    private readonly Random _rng;
    private readonly List<(float Time, FlightEnemyKind Kind, float Y)> _spawns = FlightRules.Spawns();
    private int _nextSpawn;
    private int _nextItem;
    private float _fireTimer;
    private float _introTime;
    private float _happyTime;
    private float _bombCooldown;
    private float _hitSoundGap;
    private bool _bossRingFlip;

    public FlightLevel Level { get; }
    public StartSpeed Speed { get; }
    /// <summary>Screen width in screen heights (aspect ratio). The game updates it when the window changes.</summary>
    public float Width { get; set; }

    public FlightPhase Phase { get; private set; } = FlightPhase.Flight;
    /// <summary>Flight clock: runs at the "속도" option's rate until <see cref="FlightRules.FlightTime"/>.</summary>
    public float FlightClock { get; private set; }
    /// <summary>Real seconds since the round started.</summary>
    public float Elapsed { get; private set; }
    public float Progress => Math.Clamp(FlightClock / FlightRules.FlightTime, 0f, 1f);

    public Vector2 Plane { get; private set; }
    public float Invincible { get; private set; }
    /// <summary>Seconds of the surprised face left.</summary>
    public float Surprised { get; private set; }
    public bool Shield { get; private set; }
    public int Power { get; private set; } = 1;
    public int Bombs { get; private set; } = FlightRules.StartBombs;
    /// <summary>Bumps so far (not shown to the child).</summary>
    public int Hits { get; private set; }
    public int Stars { get; private set; }
    /// <summary>Friends made to smile, the boss included.</summary>
    public int Friends { get; private set; }

    public readonly FlightShot[] Shots = new FlightShot[ShotPool];
    public readonly FlightBullet[] Bullets = new FlightBullet[BulletPool];
    public readonly FlightEnemy[] Enemies = new FlightEnemy[EnemyPool];
    public readonly FlightPickup[] Pickups = new FlightPickup[PickupPool];
    public FlightBoss Boss { get; } = new();

    /// <summary>What happened since the last <see cref="ClearEvents"/>.</summary>
    public List<SkyEvent> Events { get; } = new();

    public int BulletCount { get; private set; }
    /// <summary>The most enemy bubbles on screen at once so far.</summary>
    public int PeakBullets { get; private set; }

    private float SpeedScale => RoundClock.SpeedScale(Speed);
    private int BulletCap => Math.Min(FlightRules.MaxBullets(Level), BulletPool);

    public FlightWorld(FlightLevel level, StartSpeed speed, float width, int seed = 1)
    {
        Level = level;
        Speed = speed;
        Width = width;
        _rng = new Random(seed);
        Plane = FlightRules.PlaneStart(width);
        Boss.MaxMood = FlightRules.BossMood(level);
        Boss.Mood = Boss.MaxMood;
    }

    public bool CanFire => Phase is FlightPhase.Flight or FlightPhase.BossIntro or FlightPhase.Boss;

    // ---- Input ----

    /// <summary>Moves the plane by a finger's movement (relative, screen heights).</summary>
    public void MovePlane(Vector2 delta)
    {
        if (Phase == FlightPhase.Done)
            return;
        Plane = ClampPlane(Plane + delta);
    }

    private Vector2 ClampPlane(Vector2 p) => new(
        Math.Clamp(p.X, FlightRules.PlaneMinX, FlightRules.PlaneMaxX(Width)),
        Math.Clamp(p.Y, FlightRules.PlaneMinY, FlightRules.PlaneMaxY));

    /// <summary>Fireworks: enemy bubbles become stars, every friend gets a big bubble. False if none left (or just used).</summary>
    public bool UseBomb()
    {
        if (Bombs <= 0 || _bombCooldown > 0 || !CanFire)
            return false;
        Bombs--;
        _bombCooldown = 1f;
        Invincible = Math.Max(Invincible, FlightRules.BombInvincible);
        Events.Add(new SkyEvent(SkyEventKind.BombUsed, Plane));
        var counted = 0;
        for (var i = 0; i < Bullets.Length; i++)
        {
            ref var b = ref Bullets[i];
            if (!b.Alive)
                continue;
            b.Alive = false;
            var value = counted < FlightRules.BombStarCap ? 1 : 0;
            counted++;
            AddPickup(FlightPickupKind.Star, b.Pos, Vector2.Zero, value, FlightRules.StarLinger * 0.5f);
        }
        BulletCount = 0;
        for (var i = 0; i < Enemies.Length; i++)
        {
            ref var e = ref Enemies[i];
            if (e.State != FlightEnemyState.Active)
                continue;
            e.Hp -= FlightRules.BombDamage;
            e.Flash = 0.15f;
            if (e.Hp <= 0)
                MakeHappy(ref e);
        }
        if (Boss.State == FlightBossState.Fighting)
        {
            Boss.Mood -= Boss.MaxMood * FlightRules.BombBossShare;
            Boss.Flash = 0.2f;
        }
        return true;
    }

    // ---- Step ----

    /// <summary>The caller reads <see cref="Events"/> after each step (and after <see cref="UseBomb"/>) and then clears them.</summary>
    public void ClearEvents() => Events.Clear();

    public void Step(float dt, FlightInput input)
    {
        if (Phase == FlightPhase.Done)
            return;
        Elapsed += dt;
        Invincible = Math.Max(0, Invincible - dt);
        Surprised = Math.Max(0, Surprised - dt);
        _bombCooldown = Math.Max(0, _bombCooldown - dt);
        _hitSoundGap = Math.Max(0, _hitSoundGap - dt);

        var move = input.Move;
        if (move.LengthSquared() > 1)
            move = Vector2.Normalize(move);
        Plane = ClampPlane(Plane + move * (input.Slow ? FlightRules.SlowSpeed : FlightRules.MoveSpeed) * dt);

        UpdateTimeline(dt);
        if (CanFire)
            Fire(dt);
        UpdateShots(dt);
        UpdateEnemies(dt);
        UpdateBoss(dt);
        UpdateBullets(dt);
        UpdatePickups(dt);

        if (Phase == FlightPhase.BossHappy)
        {
            _happyTime += dt;
            if (_happyTime >= FlightRules.BossHappyTime)
                Finish();
        }
    }

    private void UpdateTimeline(float dt)
    {
        switch (Phase)
        {
            case FlightPhase.Flight:
                FlightClock += dt * SpeedScale;
                while (_nextSpawn < _spawns.Count && _spawns[_nextSpawn].Time <= FlightClock)
                {
                    var s = _spawns[_nextSpawn++];
                    SpawnEnemy(s.Kind, new Vector2(Width + 0.12f, s.Y), false);
                }
                while (_nextItem < FlightRules.FlightItems.Count && FlightRules.FlightItems[_nextItem].Time <= FlightClock)
                    SpawnItem(FlightRules.FlightItems[_nextItem++]);
                if (FlightClock >= FlightRules.FlightTime)
                {
                    FlightClock = FlightRules.FlightTime;
                    Phase = FlightPhase.BossIntro;
                    _introTime = 0;
                }
                break;
            case FlightPhase.BossIntro:
                _introTime += dt;
                if ((_introTime >= FlightRules.BossIntroMin && ActiveEnemies() == 0) || _introTime >= FlightRules.BossIntroMax)
                {
                    Phase = FlightPhase.Boss;
                    Boss.State = FlightBossState.Entering;
                    Boss.Age = 0;
                    Boss.Pos = new Vector2(Width + 0.45f, 0.45f);
                    Events.Add(new SkyEvent(SkyEventKind.BossAppears, Boss.Pos));
                }
                break;
        }
    }

    public int ActiveEnemies()
    {
        var n = 0;
        foreach (var e in Enemies)
            if (e.State == FlightEnemyState.Active)
                n++;
        return n;
    }

    // ---- The plane's bubbles ----

    private void Fire(float dt)
    {
        _fireTimer -= dt;
        if (_fireTimer > 0)
            return;
        _fireTimer += FlightRules.FireInterval;
        if (_fireTimer < 0)
            _fireTimer = 0;
        foreach (var spec in FlightRules.ShotPattern(Power))
        {
            var slot = Array.FindIndex(Shots, s => !s.Alive);
            if (slot < 0)
                return;
            var angle = spec.AngleDeg * MathF.PI / 180f;
            Shots[slot] = new FlightShot
            {
                Alive = true,
                Kind = spec.Kind,
                Pos = Plane + FlightRules.Muzzle + new Vector2(0, spec.OffsetY),
                Vel = FlightRules.Direction(angle) * FlightRules.ShotSpeed,
            };
        }
    }

    private void UpdateShots(float dt)
    {
        for (var i = 0; i < Shots.Length; i++)
        {
            ref var s = ref Shots[i];
            if (!s.Alive)
                continue;
            s.Age += dt;
            s.Pos += s.Vel * dt;
            if (s.Pos.X > Width + 0.1f || s.Pos.Y < -0.1f || s.Pos.Y > 1.1f)
            {
                s.Alive = false;
                continue;
            }
            var r = FlightRules.ShotRadius(s.Kind);
            var damage = FlightRules.ShotDamage(s.Kind);
            for (var k = 0; k < Enemies.Length; k++)
            {
                ref var e = ref Enemies[k];
                if (e.State != FlightEnemyState.Active || e.Pos.X > Width - FlightRules.HittableMargin)
                    continue;
                var reach = FlightRules.EnemyRadius(e.Kind) + r;
                if (Vector2.DistanceSquared(e.Pos, s.Pos) > reach * reach)
                    continue;
                s.Alive = false;
                e.Hp -= damage;
                e.Flash = 0.1f;
                if (e.Hp <= 0)
                    MakeHappy(ref e);
                else if (_hitSoundGap <= 0)
                {
                    _hitSoundGap = 0.08f;
                    Events.Add(new SkyEvent(SkyEventKind.EnemyHit, s.Pos, (int)e.Kind));
                }
                break;
            }
            if (s.Alive && Boss.State == FlightBossState.Fighting && HitsBoss(s.Pos, r))
            {
                s.Alive = false;
                Boss.Mood -= damage;
                Boss.Flash = 0.1f;
                if (Boss.HitSoundGap <= 0)
                {
                    Boss.HitSoundGap = 0.25f;
                    Events.Add(new SkyEvent(SkyEventKind.BossHit, s.Pos));
                }
            }
        }
    }

    private bool HitsBoss(Vector2 p, float r)
    {
        var d = p - Boss.Pos;
        var rx = FlightRules.BossRadii.X + r;
        var ry = FlightRules.BossRadii.Y + r;
        return d.X * d.X / (rx * rx) + d.Y * d.Y / (ry * ry) < 1;
    }

    // ---- Friends ----

    private void SpawnEnemy(FlightEnemyKind kind, Vector2 pos, bool summoned)
    {
        var slot = Array.FindIndex(Enemies, e => e.State == FlightEnemyState.None);
        if (slot < 0)
            return;
        Enemies[slot] = new FlightEnemy
        {
            State = FlightEnemyState.Active,
            Kind = kind,
            Pos = pos,
            BaseY = pos.Y,
            Hp = FlightRules.EnemyHp(kind),
            FireTimer = kind == FlightEnemyKind.Ufo ? 0.6f : 0.8f,
            Summoned = summoned,
        };
    }

    private void UpdateEnemies(float dt)
    {
        var scale = SpeedScale;
        for (var i = 0; i < Enemies.Length; i++)
        {
            ref var e = ref Enemies[i];
            if (e.State == FlightEnemyState.None)
                continue;
            e.Age += dt;
            e.Flash = Math.Max(0, e.Flash - dt);
            if (e.State == FlightEnemyState.Happy)
            {
                e.HappyAge += dt;
                e.Pos += FlightRules.HappyDrift * dt;
                if (e.HappyAge >= FlightRules.HappyTime || e.Pos.Y < -0.2f)
                    e.State = FlightEnemyState.None;
                continue;
            }

            MoveEnemy(ref e, dt, scale);
            ShootFromEnemy(ref e, dt);

            if (e.Pos.X < -0.25f)
            {
                e.State = FlightEnemyState.None; // Flew past: no penalty
                continue;
            }
            var reach = FlightRules.EnemyRadius(e.Kind) * 0.8f + FlightRules.PlaneHitRadius;
            if (Vector2.DistanceSquared(e.Pos, Plane) < reach * reach)
                BumpPlane();
        }
    }

    private void MoveEnemy(ref FlightEnemy e, float dt, float scale)
    {
        switch (e.Kind)
        {
            case FlightEnemyKind.Cloud:
                e.Pos.X -= FlightRules.EnemySpeed(e.Kind) * (e.Summoned ? 1.2f : scale) * dt;
                break;
            case FlightEnemyKind.Bee:
                e.Pos.X -= FlightRules.EnemySpeed(e.Kind) * scale * dt;
                e.Pos.Y = e.BaseY + FlightRules.BeeAmplitude * MathF.Sin(2 * MathF.PI * FlightRules.BeeFrequency * e.Age);
                break;
            default:
                if (e.Mode == 0)
                {
                    // Fly in quickly, slowing down near the stop
                    var stop = FlightRules.StopX(e.Kind) * Width;
                    var speed = FlightRules.EnemySpeed(e.Kind) * scale * Math.Clamp((e.Pos.X - stop) / 0.25f + 0.25f, 0.25f, 1f);
                    e.Pos.X -= speed * dt;
                    if (e.Pos.X <= stop)
                    {
                        e.Pos.X = stop;
                        e.Mode = 1;
                        e.Hover = 0;
                    }
                }
                else if (e.Mode == 1)
                {
                    e.Hover += dt;
                    e.Pos.Y = e.BaseY + 0.03f * MathF.Sin(2.2f * e.Hover);
                    if (e.Hover >= FlightRules.HoverTime(e.Kind))
                        e.Mode = 2;
                }
                else
                {
                    e.Pos.X -= FlightRules.LeaveSpeed * scale * dt;
                }
                break;
        }
    }

    private void ShootFromEnemy(ref FlightEnemy e, float dt)
    {
        switch (e.Kind)
        {
            case FlightEnemyKind.Ufo when e.Mode == 1:
            case FlightEnemyKind.BigCloud when e.Mode == 1:
            {
                var pattern = e.Kind == FlightEnemyKind.Ufo ? FlightRules.UfoAim(Level) : FlightRules.BigFan(Level);
                e.FireTimer -= dt;
                if (e.FireTimer <= 0)
                {
                    e.FireTimer += pattern.Interval;
                    Volley(pattern, e.Pos, FlightRules.Aim(e.Pos, Plane), pattern.Count > 1 ? FlightRules.FanMuzzle : 0f);
                }
                break;
            }
            case FlightEnemyKind.Bee when !e.Fired && !e.Summoned:
                if (FlightRules.BeeShot(Level) is { } bee && e.Pos.X <= bee.SourceX * Width)
                {
                    e.Fired = true;
                    Volley(bee, e.Pos, MathF.PI, 0f); // Straight ahead (left)
                }
                break;
            case FlightEnemyKind.Cloud when !e.Fired && !e.Summoned:
                if (FlightRules.CloudShot(Level) is { } cloud && e.Pos.X <= cloud.SourceX * Width)
                {
                    e.Fired = true;
                    Volley(cloud, e.Pos, FlightRules.Aim(e.Pos, Plane), 0f);
                }
                break;
        }
    }

    private void MakeHappy(ref FlightEnemy e)
    {
        e.State = FlightEnemyState.Happy;
        e.HappyAge = 0;
        e.Flash = 0;
        Friends++;
        Events.Add(new SkyEvent(SkyEventKind.EnemyHappy, e.Pos, (int)e.Kind));
        DropStars(e.Pos, FlightRules.EnemyStars(e.Kind));
    }

    // ---- The boss ----

    private void UpdateBoss(float dt)
    {
        var b = Boss;
        b.Flash = Math.Max(0, b.Flash - dt);
        b.HitSoundGap = Math.Max(0, b.HitSoundGap - dt);
        switch (b.State)
        {
            case FlightBossState.Entering:
            {
                b.Age += dt;
                var u = Math.Clamp(b.Age / FlightRules.BossEnterTime, 0f, 1f);
                var ease = 1 - (1 - u) * (1 - u) * (1 - u);
                var home = FlightRules.BossX * Width;
                b.Pos = new Vector2(Width + 0.45f + (home - Width - 0.45f) * ease, 0.45f);
                if (u >= 1)
                {
                    b.State = FlightBossState.Fighting;
                    b.Clock = 0;
                    b.Phase = 1;
                    b.RingTimer = 0.8f;
                    b.FanTimer = FlightRules.BossFan(Level).Interval;
                    b.SummonTimer = 1f;
                    Events.Add(new SkyEvent(SkyEventKind.BossPhase, b.Pos, 1));
                }
                break;
            }
            case FlightBossState.Fighting:
                FightBoss(b, dt);
                break;
            case FlightBossState.Happy:
                b.Age += dt;
                b.Pos += new Vector2(0, -0.03f) * dt;
                break;
        }
    }

    private void FightBoss(FlightBoss b, float dt)
    {
        b.Clock += dt;
        b.Age += dt;
        b.Pos = new Vector2(FlightRules.BossX * Width + FlightRules.BossSway * MathF.Sin(0.8f * b.Clock),
            0.45f + 0.14f * MathF.Sin(0.55f * b.Clock));

        if (b.NextItem < FlightRules.BossItems.Count && FlightRules.BossItems[b.NextItem].Time <= b.Clock)
            SpawnItem(FlightRules.BossItems[b.NextItem++]);

        if (b.Mood <= 0 || b.Clock >= FlightRules.BossTimeLimit)
        {
            BossSmiles(b.Mood > 0);
            return;
        }

        var phase = FlightRules.BossPhase(b.Mood, b.MaxMood);
        if (phase != b.Phase)
        {
            b.Phase = phase;
            b.RingTimer = 0.6f;
            b.FanTimer = FlightRules.BossChargeTime + 0.6f;
            b.SummonTimer = 1f;
            Events.Add(new SkyEvent(SkyEventKind.BossPhase, b.Pos, phase));
        }

        switch (b.Phase)
        {
            case 1:
                b.RingTimer -= dt;
                if (b.RingTimer <= 0)
                {
                    var ring = FlightRules.BossRing(Level);
                    b.RingTimer += ring.Interval;
                    BossRingVolley(ring);
                }
                break;
            case 2:
            {
                var fan = FlightRules.BossFan(Level);
                var wasCharging = b.Charging;
                b.FanTimer -= dt;
                if (!wasCharging && b.Charging)
                    Events.Add(new SkyEvent(SkyEventKind.BossCharge, b.Pos));
                if (b.FanTimer <= 0)
                {
                    b.FanTimer += fan.Interval;
                    // From the boss's middle (the rays the fairness test measures), appearing at its front
                    Volley(fan, b.Pos, FlightRules.Aim(b.Pos, Plane), FlightRules.BossMuzzle + FlightRules.FanMuzzle);
                }
                break;
            }
            default:
            {
                var ring = FlightRules.BossRing3(Level);
                b.RingTimer -= dt;
                if (b.RingTimer <= 0)
                {
                    b.RingTimer += ring.Interval;
                    BossRingVolley(ring);
                }
                b.SummonTimer -= dt;
                if (b.SummonTimer <= 0)
                {
                    b.SummonTimer += FlightRules.BossSummonInterval;
                    Events.Add(new SkyEvent(SkyEventKind.BossSummon, b.Pos));
                    SpawnEnemy(FlightEnemyKind.Cloud, b.Pos + new Vector2(-0.18f, -0.17f), true);
                    SpawnEnemy(FlightEnemyKind.Cloud, b.Pos + new Vector2(-0.18f, 0.17f), true);
                }
                break;
            }
        }
    }

    private void BossRingVolley(FlightPattern ring)
    {
        // Each ring sits half a step turned from the last, so bubbles do not line up into walls (no spirals)
        _bossRingFlip = !_bossRingFlip;
        var offset = _bossRingFlip ? ring.StepDeg * MathF.PI / 360f : 0f;
        Volley(ring, Boss.Pos, MathF.PI + offset, FlightRules.BossMuzzle);
    }

    private void BossSmiles(bool tired)
    {
        var b = Boss;
        b.State = FlightBossState.Happy;
        b.Tired = tired;
        b.Age = 0;
        b.Flash = 0;
        Phase = FlightPhase.BossHappy;
        _happyTime = 0;
        Friends++;
        Events.Add(new SkyEvent(SkyEventKind.BossHappy, b.Pos, tired ? 1 : 0));
        PopBullets(b.Pos, float.MaxValue);
        // Everyone still around smiles too
        for (var i = 0; i < Enemies.Length; i++)
            if (Enemies[i].State == FlightEnemyState.Active)
                MakeHappy(ref Enemies[i]);
        DropStars(b.Pos, tired ? FlightRules.BossTiredStars : FlightRules.BossStars, 0.5f);
    }

    private void Finish()
    {
        // Stars still on their way count too
        for (var i = 0; i < Pickups.Length; i++)
        {
            ref var p = ref Pickups[i];
            if (!p.Alive)
                continue;
            if (p.Kind == FlightPickupKind.Star)
                Stars += p.Value;
            p.Alive = false;
        }
        Phase = FlightPhase.Done;
        Events.Add(new SkyEvent(SkyEventKind.Done, Plane));
    }

    // ---- Enemy bubbles ----

    private void Volley(FlightPattern pattern, Vector2 from, float center, float muzzle)
    {
        var tint = pattern.Tint;
        foreach (var angle in FlightRules.Angles(pattern, center))
        {
            var dir = FlightRules.Direction(angle);
            Emit(from + dir * muzzle, dir * pattern.Speed, pattern.Look, tint);
            // Boss rings alternate two pastel colors
            if (pattern.IsRing && pattern.Look == FlightBulletLook.Star)
                tint = tint == FlightTint.Lavender ? FlightTint.Pink : FlightTint.Lavender;
        }
    }

    private void Emit(Vector2 pos, Vector2 vel, FlightBulletLook look, FlightTint tint)
    {
        if (BulletCount >= BulletCap)
            return;
        var slot = Array.FindIndex(Bullets, b => !b.Alive);
        if (slot < 0)
            return;
        Bullets[slot] = new FlightBullet { Alive = true, Look = look, Tint = tint, Pos = pos, Vel = vel };
        BulletCount++;
        PeakBullets = Math.Max(PeakBullets, BulletCount);
    }

    private void UpdateBullets(float dt)
    {
        for (var i = 0; i < Bullets.Length; i++)
        {
            ref var b = ref Bullets[i];
            if (!b.Alive)
                continue;
            b.Age += dt;
            b.Pos += b.Vel * dt;
            if (b.Pos.X < -0.1f || b.Pos.X > Width + 0.5f || b.Pos.Y < -0.1f || b.Pos.Y > 1.1f)
            {
                b.Alive = false;
                BulletCount--;
                continue;
            }
            var reach = FlightRules.BulletHitRadius(b.Look) + FlightRules.PlaneHitRadius;
            if (Invincible <= 0 && Vector2.DistanceSquared(b.Pos, Plane) < reach * reach)
            {
                b.Alive = false;
                BulletCount--;
                Events.Add(new SkyEvent(SkyEventKind.BulletPopped, b.Pos));
                BumpPlane();
            }
        }
    }

    /// <summary>Enemy bubbles near <paramref name="center"/> turn into harmless bubbles and pop.</summary>
    private void PopBullets(Vector2 center, float radius)
    {
        var r2 = radius == float.MaxValue ? float.MaxValue : radius * radius;
        for (var i = 0; i < Bullets.Length; i++)
        {
            ref var b = ref Bullets[i];
            if (!b.Alive || Vector2.DistanceSquared(b.Pos, center) > r2)
                continue;
            b.Alive = false;
            BulletCount--;
            Events.Add(new SkyEvent(SkyEventKind.BulletPopped, b.Pos));
        }
    }

    /// <summary>Bumped by a bubble or a friend: surprised and safe for a moment. Nothing is lost.</summary>
    private void BumpPlane()
    {
        if (Invincible > 0 || Phase is FlightPhase.BossHappy or FlightPhase.Done)
            return;
        Hits++;
        PopBullets(Plane, FlightRules.ClearRadius);
        if (Shield)
        {
            Shield = false;
            Invincible = FlightRules.ShieldInvincible;
            Events.Add(new SkyEvent(SkyEventKind.ShieldPopped, Plane));
        }
        else
        {
            Invincible = FlightRules.HitInvincible;
            Surprised = FlightRules.HitInvincible;
            Events.Add(new SkyEvent(SkyEventKind.PlaneSurprised, Plane));
        }
    }

    // ---- Stars and items ----

    private void DropStars(Vector2 at, int count, float spread = 0.3f)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(_rng.NextDouble() * Math.PI * 2);
            var speed = spread * (0.5f + (float)_rng.NextDouble() * 0.7f);
            AddPickup(FlightPickupKind.Star, at, FlightRules.Direction(angle) * speed + new Vector2(0, -0.1f), 1, 0);
        }
    }

    private void SpawnItem(FlightItemCue cue)
    {
        var kind = cue.Kind switch
        {
            FlightItemKind.Power => FlightPickupKind.Power,
            FlightItemKind.Bomb => FlightPickupKind.Bomb,
            FlightItemKind.Heart => FlightPickupKind.Heart,
            _ => FlightPickupKind.Candy,
        };
        AddPickup(kind, new Vector2(Width + 0.08f, cue.Y), Vector2.Zero, 0, 0);
    }

    private void AddPickup(FlightPickupKind kind, Vector2 pos, Vector2 vel, int value, float age)
    {
        var slot = Array.FindIndex(Pickups, p => !p.Alive);
        if (slot < 0)
        {
            Stars += value; // Never lose a star, even if the pool is full
            return;
        }
        Pickups[slot] = new FlightPickup { Alive = true, Kind = kind, Pos = pos, Vel = vel, BaseY = pos.Y, Value = value, Age = age };
    }

    private void UpdatePickups(float dt)
    {
        var scale = SpeedScale;
        for (var i = 0; i < Pickups.Length; i++)
        {
            ref var p = ref Pickups[i];
            if (!p.Alive)
                continue;
            p.Age += dt;
            var toPlane = Plane - p.Pos;
            var distance = toPlane.Length();
            if (p.Kind == FlightPickupKind.Star)
            {
                if (p.Age < FlightRules.StarLinger)
                {
                    p.Pos += p.Vel * dt;
                    p.Vel *= Math.Max(0, 1 - 3 * dt);
                }
                else
                {
                    p.Pulled = true;
                    var speed = Math.Min(3f, 0.6f + (p.Age - FlightRules.StarLinger) * 4f);
                    p.Pos += distance > 1e-4f ? toPlane / distance * Math.Min(distance, speed * dt) : Vector2.Zero;
                }
            }
            else
            {
                if (!p.Pulled && distance < FlightRules.MagnetRadius)
                    p.Pulled = true;
                if (p.Pulled)
                    p.Pos += distance > 1e-4f ? toPlane / distance * Math.Min(distance, 1.4f * dt) : Vector2.Zero;
                else
                {
                    p.Pos.X -= FlightRules.ItemSpeed * scale * dt;
                    p.Pos.Y = p.BaseY + 0.04f * MathF.Sin(2.5f * p.Age);
                }
                if (p.Pos.X < -0.15f)
                {
                    p.Alive = false;
                    continue;
                }
            }
            if (Vector2.DistanceSquared(p.Pos, Plane) < FlightRules.CollectRadius * FlightRules.CollectRadius)
            {
                p.Alive = false;
                Collect(p);
            }
        }
    }

    private void Collect(in FlightPickup p)
    {
        switch (p.Kind)
        {
            case FlightPickupKind.Star:
                Stars += p.Value;
                Events.Add(new SkyEvent(SkyEventKind.StarTaken, p.Pos, p.Value));
                break;
            case FlightPickupKind.Power:
                if (Power < FlightRules.MaxPower)
                {
                    Power++;
                    Events.Add(new SkyEvent(SkyEventKind.ItemTaken, p.Pos, (int)FlightItemKind.Power, Power));
                }
                else
                    Spare(p.Pos, FlightItemKind.Power);
                break;
            case FlightPickupKind.Bomb:
                if (Bombs < FlightRules.MaxBombs)
                {
                    Bombs++;
                    Events.Add(new SkyEvent(SkyEventKind.ItemTaken, p.Pos, (int)FlightItemKind.Bomb, Bombs));
                }
                else
                    Spare(p.Pos, FlightItemKind.Bomb);
                break;
            case FlightPickupKind.Heart:
                if (!Shield)
                {
                    Shield = true;
                    Events.Add(new SkyEvent(SkyEventKind.ItemTaken, p.Pos, (int)FlightItemKind.Heart));
                }
                else
                    Spare(p.Pos, FlightItemKind.Heart);
                break;
            default:
                Stars += FlightRules.CandyStars;
                Events.Add(new SkyEvent(SkyEventKind.ItemTaken, p.Pos, (int)FlightItemKind.Candy, FlightRules.CandyStars));
                break;
        }
    }

    private void Spare(Vector2 at, FlightItemKind kind)
    {
        Stars += FlightRules.SpareItemStars;
        Events.Add(new SkyEvent(SkyEventKind.ItemSpare, at, (int)kind, FlightRules.SpareItemStars));
    }
}

/// <summary>
/// A simple pilot for <c>--autoplay</c> and the tests: lines up with the nearest friend (or the boss), drifts to items,
/// and steers away from enemy bubbles that would pass close within the next moment. While <see cref="Careless"/> it
/// flies into the nearest bubble instead (the autoplay gets bumped on purpose now and then).
/// </summary>
public sealed class FlightBot
{
    private const float Look = 0.7f;      // Seconds ahead
    private const float Danger = 0.1f;    // Closest approach that needs dodging

    public float Careless { get; set; }

    public FlightInput Decide(FlightWorld w, float dt)
    {
        Careless = Math.Max(0, Careless - dt);
        var plane = w.Plane;
        var target = new Vector2(Math.Min(0.2f, FlightRules.PlaneMaxX(w.Width)), 0.5f);

        var bestX = float.MaxValue;
        foreach (var e in w.Enemies)
            if (e.State == FlightEnemyState.Active && e.Pos.X > plane.X + 0.1f && e.Pos.X < w.Width && e.Pos.X < bestX)
            {
                bestX = e.Pos.X;
                target.Y = e.Pos.Y;
            }
        if (w.Boss.State == FlightBossState.Fighting && bestX == float.MaxValue)
            target.Y = w.Boss.Pos.Y;
        foreach (var p in w.Pickups)
            if (p.Alive && p.Kind != FlightPickupKind.Star && p.Pos.X < w.Width - 0.05f)
            {
                target = new Vector2(Math.Min(p.Pos.X, FlightRules.PlaneMaxX(w.Width)), p.Pos.Y);
                break;
            }

        // Careless: fly into the nearest bubble to see what a bump looks like
        if (Careless > 0)
        {
            var nearest = float.MaxValue;
            foreach (var b in w.Bullets)
                if (b.Alive && Vector2.DistanceSquared(b.Pos, plane) < nearest)
                {
                    nearest = Vector2.DistanceSquared(b.Pos, plane);
                    target = b.Pos;
                }
        }

        var want = (target - plane) * 6f;
        if (want.LengthSquared() > 1)
            want = Vector2.Normalize(want);

        var dodge = Vector2.Zero;
        if (Careless <= 0)
        {
            foreach (var b in w.Bullets)
                if (b.Alive)
                    dodge += Away(plane, b.Pos, b.Vel, Danger);
            foreach (var e in w.Enemies)
                if (e.State == FlightEnemyState.Active)
                    dodge += Away(plane, e.Pos, new Vector2(-FlightRules.EnemySpeed(e.Kind), 0), FlightRules.EnemyRadius(e.Kind) + 0.06f);
            // Walls: do not get pinned against the top or bottom
            if (plane.Y < 0.15f)
                dodge.Y += 0.5f;
            if (plane.Y > 0.85f)
                dodge.Y -= 0.5f;
        }

        var move = dodge.LengthSquared() > 0.01f ? want * 0.3f + dodge * 3f : want;
        if (move.LengthSquared() > 1)
            move = Vector2.Normalize(move);
        return new FlightInput { Move = move };
    }

    /// <summary>Wants a firework: lots of enemy bubbles around.</summary>
    public static bool WantsBomb(FlightWorld w, int threshold) => w.Bombs > 0 && w.BulletCount >= threshold;

    private static Vector2 Away(Vector2 plane, Vector2 pos, Vector2 vel, float danger)
    {
        var r = pos - plane;
        var v2 = vel.LengthSquared();
        var t = v2 > 1e-6f ? Math.Clamp(-Vector2.Dot(r, vel) / v2, 0f, Look) : 0f;
        var closest = r + vel * t;
        var d = closest.Length();
        if (d >= danger || r.Length() > 0.6f)
            return Vector2.Zero;
        Vector2 away;
        if (d > 1e-4f)
            away = -closest / d;
        else
            away = vel.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(-vel.Y, vel.X)) : new Vector2(0, 1);
        return away * (1 - d / danger) / (t + 0.25f);
    }
}
