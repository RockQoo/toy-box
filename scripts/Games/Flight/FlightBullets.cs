using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flight;

/// <summary>
/// Draws the enemy bubbles over the plane (so they are never hidden), tinted pastel, plus the little "pop" rings when
/// bubbles turn harmless, and the plane's hit dot while flying slowly. One node; the pop rings are a fixed ring buffer.
/// </summary>
public partial class FlightBullets : Node2D
{
    private const int MaxPops = 96;
    private const float PopLife = 0.35f;

    private static readonly Color Pink = new(1f, 0.66f, 0.84f);
    private static readonly Color Sky = new(0.60f, 0.83f, 1f);
    private static readonly Color Lavender = new(0.78f, 0.68f, 1f);

    private readonly Texture2D[] _looks = new Texture2D[4];
    private readonly Vector2[] _popAt = new Vector2[MaxPops];
    private readonly float[] _popAge = new float[MaxPops];
    private readonly float[] _popSize = new float[MaxPops];
    private int _nextPop;

    public FlightWorld? World { get; set; }
    /// <summary>Show the plane's hit dot (SHIFT held).</summary>
    public bool ShowDot { get; set; }

    public override void _Ready()
    {
        const string dir = "res://assets/sprites/flight/";
        _looks[(int)FlightBulletLook.Small] = Ui.Tex(dir + "ebullet_small.png");
        _looks[(int)FlightBulletLook.Big] = Ui.Tex(dir + "ebullet_big.png");
        _looks[(int)FlightBulletLook.Oval] = Ui.Tex(dir + "ebullet_oval.png");
        _looks[(int)FlightBulletLook.Star] = Ui.Tex(dir + "ebullet_star.png");
        for (var i = 0; i < MaxPops; i++)
            _popAge[i] = PopLife;
    }

    /// <summary>A bubble popping at <paramref name="at"/> (pixels). <paramref name="size"/> is its radius.</summary>
    public void AddPop(Vector2 at, float size)
    {
        _popAt[_nextPop] = at;
        _popAge[_nextPop] = 0;
        _popSize[_nextPop] = size;
        _nextPop = (_nextPop + 1) % MaxPops;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        for (var i = 0; i < MaxPops; i++)
            if (_popAge[i] < PopLife)
                _popAge[i] += dt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var h = GetViewportRect().Size.Y;
        if (World is { } w)
        {
            foreach (var b in w.Bullets)
            {
                if (!b.Alive)
                    continue;
                var at = new Vector2(b.Pos.X, b.Pos.Y) * h;
                var tex = _looks[(int)b.Look];
                var size = tex.GetSize();
                var extent = FlightRules.BulletRadius(b.Look) * 2 * h * (b.Look == FlightBulletLook.Oval ? 1.6f : 1.3f);
                var s = extent / Mathf.Max(size.X, size.Y) * (1f + 0.06f * Mathf.Sin(b.Age * 9f));
                var angle = b.Look switch
                {
                    FlightBulletLook.Oval => Mathf.Atan2(b.Vel.Y, b.Vel.X),
                    FlightBulletLook.Star => b.Age * 2.5f,
                    _ => 0f,
                };
                // Fade in over the first moment so new bubbles do not just appear on top of a friend
                var alpha = Mathf.Clamp(b.Age / 0.15f, 0.3f, 1f);
                DrawSetTransform(at, angle, new Vector2(s, s));
                DrawTexture(tex, -size / 2, Tint(b.Tint) with { A = alpha });
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            if (ShowDot && w.Phase != FlightPhase.Done)
            {
                var plane = new Vector2(w.Plane.X, w.Plane.Y) * h;
                var r = Mathf.Max(FlightRules.PlaneHitRadius * h, 5f);
                DrawCircle(plane, r + 3, UiTheme.Pink);
                DrawCircle(plane, r, Colors.White);
            }
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        for (var i = 0; i < MaxPops; i++)
        {
            if (_popAge[i] >= PopLife)
                continue;
            var u = _popAge[i] / PopLife;
            var r = _popSize[i] * (1f + 1.2f * u);
            var a = 1f - u;
            DrawArc(_popAt[i], r, 0, Mathf.Tau, 20, new Color(1, 1, 1, 0.9f * a), 3f, true);
            for (var k = 0; k < 4; k++)
            {
                var dir = Vector2.Right.Rotated(k * Mathf.Pi / 2 + 0.6f);
                DrawCircle(_popAt[i] + dir * r * 1.3f, 3f * a + 1f, new Color(0.85f, 0.95f, 1f, a));
            }
        }
    }

    private static Color Tint(FlightTint tint) => tint switch
    {
        FlightTint.Pink => Pink,
        FlightTint.Sky => Sky,
        _ => Lavender,
    };
}
