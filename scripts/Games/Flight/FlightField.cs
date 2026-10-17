using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flight;

/// <summary>
/// Draws everything of a <see cref="FlightWorld"/> behind the plane in one node, straight from its pools: floating
/// items, the naughty friends (and the smiling clouds they become), the boss, the plane's bubbles and the stars.
/// No node per object, nothing allocated per frame.
/// </summary>
public partial class FlightField : Node2D
{
    private readonly Texture2D[] _enemies = new Texture2D[4];
    private readonly Texture2D[] _shots = new Texture2D[3];
    private readonly Texture2D[] _items = new Texture2D[4];
    private Texture2D _happy = null!;
    private Texture2D _bossIdle = null!, _bossCharge = null!, _bossHit = null!, _bossHappy = null!;
    private Texture2D _star = null!;
    private float _time;

    public FlightWorld? World { get; set; }

    public override void _Ready()
    {
        const string dir = "res://assets/sprites/flight/";
        _enemies[(int)FlightEnemyKind.Cloud] = Ui.Tex(dir + "enemy_cloud.png");
        _enemies[(int)FlightEnemyKind.Bee] = Ui.Tex(dir + "enemy_bee.png");
        _enemies[(int)FlightEnemyKind.Ufo] = Ui.Tex(dir + "enemy_ufo.png");
        _enemies[(int)FlightEnemyKind.BigCloud] = Ui.Tex(dir + "enemy_bigcloud.png");
        _shots[(int)FlightShotKind.Small] = Ui.Tex(dir + "shot_small.png");
        _shots[(int)FlightShotKind.Bubble] = Ui.Tex(dir + "shot_bubble.png");
        _shots[(int)FlightShotKind.Star] = Ui.Tex(dir + "shot_star.png");
        _items[(int)FlightItemKind.Power] = Ui.Tex(dir + "item_power.png");
        _items[(int)FlightItemKind.Bomb] = Ui.Tex(dir + "item_bomb.png");
        _items[(int)FlightItemKind.Heart] = Ui.Tex(dir + "item_heart.png");
        _items[(int)FlightItemKind.Candy] = Ui.Tex(dir + "item_score.png");
        _happy = Ui.Tex(dir + "enemy_happy.png");
        _bossIdle = Ui.Tex(dir + "boss_idle.png");
        _bossCharge = Ui.Tex(dir + "boss_charge.png");
        _bossHit = Ui.Tex(dir + "boss_hit.png");
        _bossHappy = Ui.Tex(dir + "boss_happy.png");
        _star = Ui.Tex("res://assets/sprites/ui/star.png");
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (World is not { } w)
            return;
        var h = GetViewportRect().Size.Y;
        DrawItems(w, h);
        DrawEnemies(w, h);
        DrawBoss(w.Boss, h);
        DrawShots(w, h);
        DrawStars(w, h);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawItems(FlightWorld w, float h)
    {
        foreach (var p in w.Pickups)
        {
            if (!p.Alive || p.Kind == FlightPickupKind.Star)
                continue;
            var at = new Vector2(p.Pos.X, p.Pos.Y) * h;
            var pulse = 1f + 0.08f * Mathf.Sin(p.Age * 6f);
            // A soft glow and a twinkle make items easy to spot
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawCircle(at, h * 0.06f * pulse, new Color(1, 1, 0.85f, 0.35f));
            var tex = _items[(int)ItemOf(p.Kind)];
            DrawFit(tex, at, h * 0.085f * pulse, 0.12f * Mathf.Sin(p.Age * 2.5f), Colors.White);
            var twinkle = Mathf.Max(0, Mathf.Sin(p.Age * 4f));
            DrawFit(_star, at + new Vector2(h * 0.04f, -h * 0.04f), h * 0.03f * twinkle, p.Age * 3f, Colors.White);
        }
    }

    private static FlightItemKind ItemOf(FlightPickupKind kind) => kind switch
    {
        FlightPickupKind.Power => FlightItemKind.Power,
        FlightPickupKind.Bomb => FlightItemKind.Bomb,
        FlightPickupKind.Heart => FlightItemKind.Heart,
        _ => FlightItemKind.Candy,
    };

    private void DrawEnemies(FlightWorld w, float h)
    {
        foreach (var e in w.Enemies)
        {
            if (e.State == FlightEnemyState.None)
                continue;
            var at = new Vector2(e.Pos.X, e.Pos.Y) * h;
            var height = FlightRules.EnemyHeight(e.Kind) * h;
            if (e.State == FlightEnemyState.Happy)
            {
                // Pops a little bigger, then floats away fading
                var pop = e.HappyAge < 0.2f ? 1.3f - 1.5f * e.HappyAge : 1f;
                var fade = 1f - Mathf.Clamp((e.HappyAge - (FlightRules.HappyTime - 0.7f)) / 0.7f, 0f, 1f);
                DrawPic(_happy, at, height * 0.85f * pop, 0.1f * Mathf.Sin(e.HappyAge * 5f), new Color(1, 1, 1, fade));
                continue;
            }
            var squash = 1f + 0.04f * Mathf.Sin(e.Age * 6f);
            var color = e.Flash > 0 ? new Color(1.5f, 1.5f, 1.5f) : Colors.White;
            var tilt = e.Kind == FlightEnemyKind.Bee ? 0.15f * Mathf.Cos(2 * Mathf.Pi * FlightRules.BeeFrequency * e.Age) : 0f;
            DrawPic(_enemies[(int)e.Kind], at, height * squash * (e.Flash > 0 ? 1.06f : 1f), tilt, color);
        }
    }

    private void DrawBoss(FlightBoss b, float h)
    {
        if (b.State == FlightBossState.Absent)
            return;
        var at = new Vector2(b.Pos.X, b.Pos.Y) * h;
        var scale = FlightRules.BossHeight * h / _bossIdle.GetSize().Y;
        Texture2D tex;
        var grow = 1f;
        var color = Colors.White;
        if (b.State == FlightBossState.Happy)
        {
            tex = _bossHappy;
            grow = 1f + 0.05f * Mathf.Sin(b.Age * 4f);
        }
        else if (b.Charging)
        {
            tex = _bossCharge;
            grow = 1f + 0.06f * Mathf.Sin(_time * 18f);
        }
        else if (b.Flash > 0)
        {
            tex = _bossHit;
            grow = 0.97f;
            color = new Color(1.3f, 1.3f, 1.3f);
        }
        else
        {
            tex = _bossIdle;
            grow = 1f + 0.025f * Mathf.Sin(_time * 3f);
        }
        var size = tex.GetSize();
        DrawSetTransform(at, 0, new Vector2(scale * grow, scale * grow));
        DrawTexture(tex, -size / 2, color);
    }

    private void DrawShots(FlightWorld w, float h)
    {
        foreach (var s in w.Shots)
        {
            if (!s.Alive)
                continue;
            // A little wobble as they fly
            var at = new Vector2(s.Pos.X, s.Pos.Y + 0.004f * Mathf.Sin(s.Age * 22f)) * h;
            var d = FlightRules.ShotRadius(s.Kind) * 2 * h * 1.3f;
            if (s.Kind == FlightShotKind.Star)
            {
                // A star inside a bubble, so it never looks like the stars to collect
                DrawFit(_shots[(int)FlightShotKind.Bubble], at, d, 0, new Color(1, 1, 1, 0.6f));
                DrawFit(_shots[(int)FlightShotKind.Star], at, d * 0.62f, s.Age * 8f, new Color(1, 1, 1, 0.95f));
                continue;
            }
            DrawFit(_shots[(int)s.Kind], at, d, 0, new Color(1, 1, 1, s.Kind == FlightShotKind.Bubble ? 0.9f : 1f));
        }
    }

    private void DrawStars(FlightWorld w, float h)
    {
        foreach (var p in w.Pickups)
        {
            if (!p.Alive || p.Kind != FlightPickupKind.Star)
                continue;
            var at = new Vector2(p.Pos.X, p.Pos.Y) * h;
            var size = h * (p.Value > 0 ? 0.045f : 0.03f);
            DrawFit(_star, at, size, p.Age * 5f, Colors.White);
        }
    }

    // ---- Helpers ----

    private void DrawPic(Texture2D tex, Vector2 center, float height, float angle, Color color)
    {
        var size = tex.GetSize();
        var s = height / size.Y;
        DrawSetTransform(center, angle, new Vector2(s, s));
        DrawTexture(tex, -size / 2, color);
    }

    /// <summary>Draws a picture so its longer side is <paramref name="extent"/> pixels.</summary>
    private void DrawFit(Texture2D tex, Vector2 center, float extent, float angle, Color color)
    {
        if (extent <= 0.5f)
            return;
        var size = tex.GetSize();
        var s = extent / Mathf.Max(size.X, size.Y);
        DrawSetTransform(center, angle, new Vector2(s, s));
        DrawTexture(tex, -size / 2, color);
    }
}
