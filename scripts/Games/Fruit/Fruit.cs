using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.FruitCatch;

/// <summary>
/// One falling fruit (or the golden bonus coin). The node origin is the center of the picture. Movement is driven by
/// <see cref="FruitGame"/> via <see cref="Step"/>; <see cref="Catch"/> and <see cref="Land"/> play the short exit
/// animations and free the node.
/// </summary>
public partial class Fruit : Node2D
{
    /// <summary>Kind index = bit in the picked-fruit mask (see <see cref="FruitRules"/>).</summary>
    public static readonly string[] Textures =
    {
        "res://assets/sprites/fruit/apple.png", "res://assets/sprites/fruit/strawberry.png",
        "res://assets/sprites/fruit/banana.png", "res://assets/sprites/fruit/orange.png",
        "res://assets/sprites/fruit/grape.png", "res://assets/sprites/fruit/watermelon.png",
    };

    public static readonly string[] Names = { "사과", "딸기", "바나나", "오렌지", "포도", "수박" };

    /// <summary>Particle colors per kind.</summary>
    public static readonly Color[] Tints =
    {
        new(0.95f, 0.25f, 0.22f), new(1f, 0.30f, 0.42f), new(1f, 0.86f, 0.25f),
        new(1f, 0.60f, 0.15f), new(0.62f, 0.34f, 0.85f), new(0.36f, 0.78f, 0.30f),
    };

    /// <summary>No golden apple picture yet: the coin stands in for the "황금 과일".</summary>
    public const string BonusTexture = "res://assets/sprites/fruit/coin.png";
    public static readonly Color BonusTint = new(1f, 0.82f, 0.2f);

    private Sprite2D _sprite = null!;
    private float _baseScale;
    private float _age;
    private float _spin;
    private bool _done;

    public int Kind { get; private set; }
    public bool Bonus { get; private set; }
    public float FallSpeed { get; private set; }
    public float Radius { get; private set; }
    public float PreviousY { get; private set; }
    /// <summary>False once caught, landed or faded out (no longer collides).</summary>
    public bool Alive => !_done;
    public Color Tint => Bonus ? BonusTint : Tints[Kind];
    public Texture2D Texture => _sprite.Texture;

    /// <param name="size">Pixel size of the longer side of the picture.</param>
    public static Fruit Create(int kind, bool bonus, Vector2 position, float size, float fallSpeed, float spin)
    {
        var fruit = new Fruit
        {
            Kind = kind,
            Bonus = bonus,
            Position = position,
            PreviousY = position.Y,
            FallSpeed = fallSpeed,
            _spin = spin,
        };
        var texture = Ui.Tex(bonus ? BonusTexture : Textures[kind]);
        var tex = texture.GetSize();
        fruit._baseScale = size / Mathf.Max(tex.X, tex.Y);
        fruit._sprite = new Sprite2D { Texture = texture, Scale = new Vector2(fruit._baseScale, fruit._baseScale) };
        fruit.AddChild(fruit._sprite);
        fruit.Radius = size * 0.45f;
        return fruit;
    }

    public void Step(float dt)
    {
        _age += dt;
        PreviousY = Position.Y;
        if (_done)
            return;
        Position = Position with { Y = Position.Y + FallSpeed * dt };
        if (Bonus)
        {
            // The coin turns like a spinning coin and glows
            _sprite.Scale = new Vector2(_baseScale * (0.35f + 0.65f * Mathf.Abs(Mathf.Cos(_age * 4f))), _baseScale);
            QueueRedraw();
        }
        else
        {
            _sprite.Rotation = Mathf.Sin(_age * 2.2f + _spin) * 0.25f;
        }
    }

    public override void _Draw()
    {
        if (!Bonus || _done)
            return;
        var pulse = 0.5f + 0.5f * Mathf.Sin(_age * 6f);
        DrawCircle(Vector2.Zero, Radius * (1.35f + 0.15f * pulse), BonusTint with { A = 0.18f + 0.1f * pulse }, antialiased: true);
        DrawCircle(Vector2.Zero, Radius * 1.12f, new Color(1, 1, 0.8f, 0.22f), antialiased: true);
    }

    /// <summary>
    /// Caught: drops into the basket opening at <paramref name="into"/> (parent space) while shrinking. Falling fruit is
    /// drawn in front of the basket; from here on it goes behind it, so it sinks in behind the front rim.
    /// </summary>
    public void Catch(Vector2 into)
    {
        _done = true;
        ZIndex = -1;
        QueueRedraw();
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "position", into, 0.1f).SetEase(Tween.EaseType.In);
        tween.TweenProperty(_sprite, "scale", _sprite.Scale * 0.6f, 0.1f);
        tween.TweenProperty(this, "modulate:a", 0f, 0.08f).SetDelay(0.06f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>Missed: lands on the floor with a soft squash and fades. No penalty, nothing alarming.</summary>
    public void Land(float floorY)
    {
        _done = true;
        QueueRedraw();
        Position = Position with { Y = floorY - Radius * 0.8f };
        var b = _sprite.Scale;
        var tween = CreateTween();
        tween.TweenProperty(_sprite, "scale", new Vector2(b.X * 1.2f, b.Y * 0.8f), 0.06f);
        tween.TweenProperty(_sprite, "scale", b, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(0.25f);
        tween.TweenProperty(this, "modulate:a", 0f, 0.35f);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>Time's up: fades away where it is.</summary>
    public void FadeOut()
    {
        _done = true;
        QueueRedraw();
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.3f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
