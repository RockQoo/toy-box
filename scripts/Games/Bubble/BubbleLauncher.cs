using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Bubble;

/// <summary>
/// The bottom of the board: the launcher (bubble/launcher_base.png with the barrel turning toward the aim around its
/// lower end) holding the bubble to shoot, the next-bubble holder at the left (press it to swap), and the rabbit at the
/// right who throws. Position is the launch point in board pixels.
/// </summary>
public partial class BubbleLauncher : Node2D
{
    private const float BaseWidth = 1.65f;     // Diameters
    private const float BaseHole = 0.30f;      // Hole center, share of the base height from the top
    private const float BarrelHeight = 1.55f;
    private const float BarrelPivot = 0.85f;   // Turning point, share of the barrel height from the top
    private const float HolderHeight = 1.1f;
    private const float HolderBall = 0.40f;    // Center of the glass ball, share of the holder height from the top
    private const float RabbitHeight = 1.85f;

    /// <summary>Next-bubble holder center relative to the launch point, in diameters.</summary>
    public static readonly Vector2 HolderOffset = new(-2.6f, -0.05f);

    private static readonly Vector2 RabbitFeet = new(2.55f, 0.92f);

    private Sprite2D _rabbit = null!;
    private Tween? _rabbitTween;
    private float _angle = 90f;
    private float _currentScale = 1f;
    private float _nextScale = 1f;
    private float _barrelKick;
    private float _time;

    public float Unit { get; set; } = 64f;

    /// <summary>Barrel angle in degrees above the horizontal (90 straight up).</summary>
    public float AimAngle { get; set; } = 90f;

    /// <summary>Color of the bubble in the launcher, -1 for none (it is in the air).</summary>
    public int Current { get; set; } = -1;

    public int Next { get; set; } = -1;

    public Vector2 HolderCenter => Position + HolderOffset * Unit;

    public override void _Ready()
    {
        _rabbit = new Sprite2D { Texture = Ui.Tex("res://assets/sprites/buddy/rabbit_throw_empty.png") };
        AddChild(_rabbit);
        Relayout();
    }

    public void Relayout()
    {
        if (_rabbit is null)
            return;
        var tex = _rabbit.Texture;
        var s = RabbitHeight * Unit / tex.GetHeight();
        _rabbit.Scale = new Vector2(s, s);
        // Pivot at the feet: squashes and leans look like a body move
        _rabbit.Offset = new Vector2(0, -tex.GetHeight() / 2f);
        _rabbit.Position = RabbitFeet * Unit;
    }

    /// <summary>The bubble in the launcher springs in (a new one, or a recolored one).</summary>
    public void PopCurrent() => _currentScale = 0.2f;

    public void PopNext() => _nextScale = 0.2f;

    /// <summary>The rabbit throws: a squash, a lean toward the shot and a little hop. The barrel kicks.</summary>
    public void Throw()
    {
        _barrelKick = 1f;
        Rabbit(t =>
        {
            t.TweenProperty(_rabbit, "scale", BaseScale * new Vector2(1.12f, 0.86f), 0.07f);
            t.Parallel().TweenProperty(_rabbit, "rotation", Mathf.DegToRad(-12f), 0.07f);
            t.TweenProperty(_rabbit, "scale", BaseScale * new Vector2(0.94f, 1.08f), 0.10f);
            t.Parallel().TweenProperty(_rabbit, "position", RabbitFeet * Unit + new Vector2(0, -Unit * 0.18f), 0.10f);
            t.TweenProperty(_rabbit, "scale", BaseScale, 0.16f).SetTrans(Tween.TransitionType.Back);
            t.Parallel().TweenProperty(_rabbit, "position", RabbitFeet * Unit, 0.16f).SetTrans(Tween.TransitionType.Bounce);
            t.Parallel().TweenProperty(_rabbit, "rotation", 0f, 0.16f);
        });
    }

    /// <summary>"영차!": the rabbit hops to the launcher and pats it twice (the flower line clean-up).</summary>
    public void Pat()
    {
        Rabbit(t =>
        {
            var near = RabbitFeet * Unit + new Vector2(-Unit * 0.9f, 0);
            t.TweenProperty(_rabbit, "position", near + new Vector2(0, -Unit * 0.4f), 0.14f).SetEase(Tween.EaseType.Out);
            t.TweenProperty(_rabbit, "position", near, 0.12f).SetEase(Tween.EaseType.In);
            for (var i = 0; i < 2; i++)
            {
                t.TweenProperty(_rabbit, "rotation", Mathf.DegToRad(-22f), 0.08f);
                t.Parallel().TweenProperty(_rabbit, "scale", BaseScale * new Vector2(1.1f, 0.9f), 0.08f);
                t.TweenCallback(Callable.From(() => _barrelKick = 0.7f));
                t.TweenProperty(_rabbit, "rotation", 0f, 0.1f);
                t.Parallel().TweenProperty(_rabbit, "scale", BaseScale, 0.1f);
            }
            t.TweenProperty(_rabbit, "position", RabbitFeet * Unit + new Vector2(0, -Unit * 0.3f), 0.14f).SetEase(Tween.EaseType.Out);
            t.TweenProperty(_rabbit, "position", RabbitFeet * Unit, 0.12f).SetEase(Tween.EaseType.In);
        });
    }

    /// <summary>A big happy jump (the board is cleared).</summary>
    public void Cheer()
    {
        Rabbit(t =>
        {
            for (var i = 0; i < 3; i++)
            {
                t.TweenProperty(_rabbit, "position", RabbitFeet * Unit + new Vector2(0, -Unit * 0.9f), 0.22f)
                    .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                t.Parallel().TweenProperty(_rabbit, "rotation", Mathf.DegToRad(i % 2 == 0 ? 10f : -10f), 0.22f);
                t.TweenProperty(_rabbit, "position", RabbitFeet * Unit, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            }
            t.TweenProperty(_rabbit, "rotation", 0f, 0.1f);
        });
    }

    private Vector2 BaseScale => new Vector2(1, 1) * (RabbitHeight * Unit / _rabbit.Texture.GetHeight());

    private void Rabbit(System.Action<Tween> build)
    {
        _rabbitTween?.Kill();
        _rabbit.Scale = BaseScale;
        _rabbit.Rotation = 0;
        _rabbit.Position = RabbitFeet * Unit;
        _rabbitTween = CreateTween();
        build(_rabbitTween);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        _angle = Mathf.Lerp(_angle, AimAngle, 1 - Mathf.Exp(-18f * dt));
        _currentScale = Mathf.Min(1f, _currentScale + dt * 5f);
        _nextScale = Mathf.Min(1f, _nextScale + dt * 5f);
        _barrelKick = Mathf.Max(0f, _barrelKick - dt * 5f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Next-bubble holder with its bubble inside the glass ball
        var holder = BubbleArt.NextHolder;
        var hh = HolderHeight * Unit;
        var hw = hh * holder.GetWidth() / holder.GetHeight();
        var hc = HolderOffset * Unit;
        var ballAt = hc + new Vector2(0, (HolderBall - 0.5f) * hh);
        if (Next >= 0)
            BubbleArt.Draw(this, Next, ballAt, Unit * 0.78f * Spring(_nextScale));
        DrawTextureRect(holder, new Rect2(hc - new Vector2(hw, hh) / 2, new Vector2(hw, hh)), false, Colors.White with { A = 0.92f });
        DrawString(ThemeDB.FallbackFont, hc + new Vector2(-hw / 2, hh / 2 + Unit * 0.36f), "다음", HorizontalAlignment.Center, hw,
            Mathf.RoundToInt(Unit * 0.34f), UiTheme.Ink);

        // Barrel (behind the base's hole), then the base, then the bubble
        var barrel = BubbleArt.LauncherBarrel;
        var bh = BarrelHeight * Unit * (1f - 0.08f * Mathf.Sin(_barrelKick * Mathf.Pi));
        var bw = BarrelHeight * Unit * barrel.GetWidth() / barrel.GetHeight();
        DrawSetTransform(Vector2.Zero, Mathf.DegToRad(90f - _angle), Vector2.One);
        DrawTextureRect(barrel, new Rect2(-bw / 2, -bh * BarrelPivot, bw, bh), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        var bas = BubbleArt.LauncherBase;
        var w = BaseWidth * Unit;
        var h = w * bas.GetHeight() / bas.GetWidth();
        DrawTextureRect(bas, new Rect2(-w / 2, -h * BaseHole, w, h), false);

        if (Current >= 0)
        {
            var bob = Mathf.Sin(_time * 3f) * Unit * 0.02f;
            BubbleArt.Draw(this, Current, new Vector2(0, bob), Unit * Spring(_currentScale));
        }
    }

    /// <summary>0..1 growth with a little overshoot.</summary>
    private static float Spring(float t) => t >= 1f ? 1f : 1f + 0.18f * Mathf.Sin(t * Mathf.Pi) - (1 - t) * 0.9f;
}
