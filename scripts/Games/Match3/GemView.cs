using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Match3;

/// <summary>
/// One piece on the board: the fruit picture, with a glowing overlay for a special (a striped beam across or down, a
/// golden ring) or the rainbow candy. The node origin is the cell center. Moves, pops, wiggles and spins with tweens.
/// </summary>
public partial class GemView : Node2D
{
    private static readonly Color[] KindColors =
    {
        new(0.95f, 0.30f, 0.30f), // apple
        new(1.00f, 0.38f, 0.50f), // strawberry
        new(1.00f, 0.86f, 0.30f), // banana
        new(1.00f, 0.60f, 0.20f), // orange
        new(0.62f, 0.38f, 0.88f), // grape
        new(0.40f, 0.80f, 0.36f), // watermelon
    };

    private Sprite2D _fruit = null!;
    private Sprite2D _overlay = null!;
    private float _cell;
    private float _time;
    private Tween? _move;
    private Tween? _fx;

    public Gem Gem { get; private set; }

    public static Color ColorOf(Gem gem) =>
        gem.IsFruit && gem.Kind < KindColors.Length ? KindColors[gem.Kind] : new Color(1f, 0.95f, 0.7f);

    public static Texture2D TextureOf(Gem gem) => gem.IsRainbow
        ? Ui.Tex("res://assets/sprites/match3/rainbow.png")
        : Ui.Tex($"res://assets/sprites/match3/{Match3Rules.KindNames[Mathf.Clamp(gem.Kind, 0, 5)]}.png");

    public static GemView Create(Gem gem, float cell)
    {
        var view = new GemView();
        view._fruit = new Sprite2D();
        view.AddChild(view._fruit);
        view._overlay = new Sprite2D { Visible = false };
        view.AddChild(view._overlay);
        view._cell = cell;
        view.SetGem(gem);
        return view;
    }

    public void SetCell(float cell)
    {
        _cell = cell;
        SetGem(Gem);
    }

    public void SetGem(Gem gem)
    {
        Gem = gem;
        _fruit.Texture = TextureOf(gem);
        var fruitSize = gem.IsRainbow ? 0.82f : 0.78f;
        _fruit.Scale = Vector2.One * (_cell * fruitSize / Mathf.Max(_fruit.Texture.GetWidth(), _fruit.Texture.GetHeight()));

        string? overlay = gem.Special switch
        {
            GemSpecial.LineH => "fx_line_h",
            GemSpecial.LineV => "fx_line_v",
            GemSpecial.Bomb => "fx_bomb",
            _ => null,
        };
        _overlay.Visible = overlay is not null;
        if (overlay is null)
            return;
        _overlay.Texture = Ui.Tex($"res://assets/sprites/match3/{overlay}.png");
        // The beams reach just past the fruit along their line; the ring wraps the fruit
        var size = gem.Special == GemSpecial.Bomb ? _cell * 1.0f : _cell * 1.3f;
        _overlay.Scale = Vector2.One * (size / Mathf.Max(_overlay.Texture.GetWidth(), _overlay.Texture.GetHeight()));
    }

    public override void _Process(double delta)
    {
        if (!_overlay.Visible && !Gem.IsRainbow)
            return;
        _time += (float)delta;
        if (_overlay.Visible)
        {
            // A gentle shimmer so a special stands out from plain fruit
            _overlay.Modulate = Colors.White with { A = 0.85f + 0.15f * Mathf.Sin(_time * 6f) };
            if (Gem.Special == GemSpecial.Bomb)
                _overlay.Rotation = _time * 0.8f;
        }
        if (Gem.IsRainbow)
            _fruit.Rotation = _time * 1.6f;
    }

    /// <summary>Slides to a position (a swap, or a fall with a small bounce).</summary>
    public Tween MoveTo(Vector2 target, float time, bool bounce = false)
    {
        _move?.Kill();
        _move = CreateTween();
        _move.TweenProperty(this, "position", target, time)
            .SetTrans(bounce ? Tween.TransitionType.Bounce : Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        return _move;
    }

    /// <summary>A swap that made no line: over to the other cell, a little wiggle, and back.</summary>
    public void SwapBack(Vector2 other, Vector2 home, float time)
    {
        _move?.Kill();
        _move = CreateTween();
        _move.TweenProperty(this, "position", other, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _move.TweenProperty(this, "rotation", 0.18f, 0.06f);
        _move.TweenProperty(this, "rotation", -0.18f, 0.08f);
        _move.TweenProperty(this, "rotation", 0f, 0.06f);
        _move.TweenProperty(this, "position", home, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Grows out of the cell (a new special, a new board).</summary>
    public void PopIn(float delay = 0f)
    {
        Scale = Vector2.Zero;
        _fx?.Kill();
        _fx = CreateTween();
        _fx.TweenInterval(delay);
        _fx.TweenProperty(this, "scale", Vector2.One, 0.22f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Swells and goes: the fruit was collected. Frees itself.</summary>
    public void Burst(float time)
    {
        _move?.Kill();
        _fx?.Kill();
        _fx = CreateTween();
        _fx.TweenProperty(this, "scale", new Vector2(1.3f, 1.3f), time * 0.45f).SetEase(Tween.EaseType.Out);
        _fx.TweenProperty(this, "scale", Vector2.Zero, time * 0.55f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Back);
        _fx.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>The hint: a little side-to-side wiggle.</summary>
    public void Wiggle()
    {
        _fx?.Kill();
        Scale = Vector2.One;
        _fx = CreateTween();
        for (var i = 0; i < 2; i++)
        {
            _fx.TweenProperty(this, "rotation", 0.22f, 0.08f);
            _fx.TweenProperty(this, "rotation", -0.22f, 0.12f);
        }
        _fx.TweenProperty(this, "rotation", 0f, 0.08f);
        _fx.Parallel().TweenProperty(this, "scale", new Vector2(1.1f, 1.1f), 0.08f);
        _fx.TweenProperty(this, "scale", Vector2.One, 0.1f);
    }

    /// <summary>A quick swell (the piece just turned into a special).</summary>
    public void Punch()
    {
        _fx?.Kill();
        _fx = CreateTween();
        _fx.TweenProperty(this, "scale", new Vector2(1.35f, 1.35f), 0.08f).SetEase(Tween.EaseType.Out);
        _fx.TweenProperty(this, "scale", Vector2.One, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>The shuffle: spins round while it travels to its new cell.</summary>
    public void SpinTo(Vector2 target, float time)
    {
        _move?.Kill();
        _fx?.Kill();
        Rotation = 0;
        _move = CreateTween().SetParallel();
        _move.TweenProperty(this, "position", target, time).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _move.TweenProperty(this, "rotation", Mathf.Tau, time).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        var squeeze = CreateTween();
        squeeze.TweenProperty(this, "scale", new Vector2(0.6f, 0.6f), time / 2);
        squeeze.TweenProperty(this, "scale", Vector2.One, time / 2);
        _move.Chain().TweenCallback(Callable.From(() => Rotation = 0));
    }
}
