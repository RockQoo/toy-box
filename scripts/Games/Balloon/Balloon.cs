using Godot;
using ToyBox.Common;

namespace ToyBox.Games.BalloonPop;

/// <summary>
/// One rising balloon. The node's origin is the center of the balloon body (not the sprite center), so hit tests
/// and pop effects line up with what the player sees. Movement is driven by <see cref="BalloonGame"/> via <see cref="Step"/>.
/// </summary>
public partial class Balloon : Node2D
{
    /// <summary>Sprite paths and particle colors, in the same order (index = color id).</summary>
    public static readonly string[] Textures =
    {
        "res://assets/sprites/balloon/red.png", "res://assets/sprites/balloon/yellow.png",
        "res://assets/sprites/balloon/blue.png", "res://assets/sprites/balloon/green.png",
        "res://assets/sprites/balloon/purple.png", "res://assets/sprites/balloon/pink.png",
    };

    public static readonly Color[] Tints =
    {
        new(0.95f, 0.22f, 0.25f), new(1f, 0.82f, 0.15f), new(0.20f, 0.55f, 0.98f),
        new(0.35f, 0.82f, 0.25f), new(0.65f, 0.35f, 0.92f), new(1f, 0.45f, 0.70f),
    };

    /// <summary>Where the body center sits in the sprite, as a fraction of its height from the top.</summary>
    private const float BodyCenterY = 0.31f;
    private const float BodyRadiusX = 0.48f;
    private const float BodyRadiusY = 0.31f;
    /// <summary>Hit area is larger than the drawn body: small fingers and fast balloons.</summary>
    private const float HitSlack = 1.3f;

    private Sprite2D _sprite = null!;
    private float _baseX;
    private float _swayPhase;
    private float _swayAmount;
    private float _age;
    private bool _released;

    public int ColorId { get; private set; }
    public float RiseSpeed { get; private set; }
    public Vector2 BodyRadius { get; private set; }
    public bool Alive => !_released;

    public static Balloon Create(int colorId, Vector2 position, float height, float riseSpeed, float swayPhase)
    {
        var balloon = new Balloon
        {
            ColorId = colorId,
            Position = position,
            RiseSpeed = riseSpeed,
            _baseX = position.X,
            _swayPhase = swayPhase,
            _swayAmount = 10f + 8f * Mathf.Sin(swayPhase * 3.1f),
        };
        var texture = Ui.Tex(Textures[colorId]);
        var size = texture.GetSize();
        var scale = height / size.Y;
        balloon._sprite = new Sprite2D
        {
            Texture = texture,
            Scale = new Vector2(scale, scale),
            // Shift so the body center lands on the node origin
            Offset = new Vector2(0, size.Y * (0.5f - BodyCenterY)),
        };
        balloon.AddChild(balloon._sprite);
        balloon.BodyRadius = new Vector2(size.X * BodyRadiusX, size.Y * BodyRadiusY) * scale;
        return balloon;
    }

    public void Step(float dt)
    {
        _age += dt;
        Position = new Vector2(
            _baseX + Mathf.Sin(_age * 1.7f + _swayPhase) * _swayAmount,
            Position.Y - RiseSpeed * dt);
        Rotation = Mathf.Cos(_age * 1.7f + _swayPhase) * 0.07f;
    }

    /// <summary>True if <paramref name="point"/> (in the parent's space) is on the balloon, with some slack.</summary>
    public bool Hits(Vector2 point)
    {
        var d = point - Position;
        var r = BodyRadius * HitSlack;
        return d.X * d.X / (r.X * r.X) + d.Y * d.Y / (r.Y * r.Y) <= 1f;
    }

    /// <summary>True once the whole balloon (including string) is above the top of the screen.</summary>
    public bool IsGone => Position.Y < -BodyRadius.Y * 3.5f;

    /// <summary>Time's up: the balloon drifts away quickly and fades instead of popping.</summary>
    public void Release()
    {
        _released = true;
        RiseSpeed *= 3f;
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.7f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
