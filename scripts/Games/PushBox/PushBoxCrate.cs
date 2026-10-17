using Godot;
using ToyBox.Common;

namespace ToyBox.Games.PushBox;

/// <summary>
/// A wooden box on the board (its node sits at the cell center, y-sorted with the rabbit). On a star spot it turns into
/// the sparkling box (<c>box_done</c>, drawn a little bigger so the crate itself keeps its size). A stuck box sways
/// sleepily once and stays a little dusky until it is free again.
/// </summary>
public partial class PushBoxCrate : Node2D
{
    private const float BoxWidth = 0.84f;             // Of a cell
    private const float DoneCratePixels = 195f;      // The crate's width inside box_done (it has sparkles around it)
    private static readonly Color Dusky = new(0.82f, 0.84f, 0.95f);

    private static Texture2D? _box, _done;

    private Sprite2D _sprite = null!;
    private Tween? _move;
    private Tween? _body;
    private float _cell = 80f;
    private bool _isDone;
    private bool _sleepy;

    public int Index { get; init; }
    public Vector2 Home { get; private set; }

    public override void _Ready()
    {
        _box ??= Ui.Tex("res://assets/sprites/pushbox/box.png");
        _done ??= Ui.Tex("res://assets/sprites/pushbox/box_done.png");
        _sprite = new Sprite2D { Texture = _box };
        AddChild(_sprite);
        SetCellSize(_cell);
    }

    private float BaseScale => _isDone
        ? _cell * BoxWidth / DoneCratePixels
        : _cell * BoxWidth / _box!.GetWidth();

    private Vector2 Rest => new(0, -_cell * 0.06f);

    public void SetCellSize(float cell)
    {
        _cell = cell;
        if (_sprite is null)
            return;
        _sprite.Scale = Vector2.One * BaseScale;
        _sprite.Position = Rest;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, _cell * 0.36f), 0, new Vector2(1, 0.3f));
        DrawCircle(Vector2.Zero, _cell * 0.4f, new Color(0, 0, 0, 0.16f), antialiased: true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    public void Snap(Vector2 position)
    {
        _move?.Kill();
        Home = position;
        Position = position;
    }

    public void SlideTo(Vector2 target, float duration)
    {
        Home = target;
        _move?.Kill();
        _move = CreateTween();
        _move.TweenProperty(this, "position", target, duration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>Swaps to the sparkling box (or back). <paramref name="hop"/>: a happy jump with the swap.</summary>
    public void SetDone(bool done, bool hop)
    {
        if (_isDone == done)
            return;
        _isDone = done;
        _sprite.Texture = done ? _done : _box;
        var b = BaseScale;
        _body?.Kill();
        _sprite.Rotation = 0;
        if (!hop)
        {
            _sprite.Scale = Vector2.One * b;
            _sprite.Position = Rest;
            return;
        }
        Hop(0f, 0.45f, 1.18f);
    }

    /// <summary>A jump in place (the box landing on its spot, or the finale wave).</summary>
    public void Hop(float delay, float height = 0.4f, float grow = 1.12f)
    {
        var b = BaseScale;
        var rest = Rest;
        _body?.Kill();
        _body = CreateTween();
        if (delay > 0)
            _body.TweenInterval(delay);
        _body.TweenProperty(_sprite, "scale", new Vector2(b * 1.12f, b * 0.88f), 0.06f);
        _body.TweenProperty(_sprite, "position", rest + new Vector2(0, -_cell * height), 0.16f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _body.Parallel().TweenProperty(_sprite, "scale", new Vector2(b * grow * 0.95f, b * grow * 1.06f), 0.16f);
        _body.TweenProperty(_sprite, "position", rest, 0.16f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _body.TweenProperty(_sprite, "scale", new Vector2(b * 1.1f, b * 0.9f), 0.05f);
        _body.TweenProperty(_sprite, "scale", Vector2.One * b, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>The rabbit pushed against it but it cannot go: a tiny shove and back.</summary>
    public void Nudge(Vector2 toward)
    {
        _move?.Kill();
        Position = Home;
        _move = CreateTween();
        _move.TweenProperty(this, "position", Home + toward * _cell * 0.05f, 0.05f);
        _move.TweenProperty(this, "position", Home, 0.15f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Stuck: sways side to side like a sleepy head, and stays a little dusky while stuck.</summary>
    public void SetSleepy(bool sleepy)
    {
        if (_sleepy == sleepy)
            return;
        _sleepy = sleepy;
        var tint = CreateTween();
        tint.TweenProperty(_sprite, "self_modulate", sleepy ? Dusky : Colors.White, 0.3f);
        if (!sleepy)
            return;
        _body?.Kill();
        _sprite.Position = Rest;
        _sprite.Scale = Vector2.One * BaseScale;
        _body = CreateTween();
        foreach (var angle in new[] { -0.12f, 0.1f, -0.08f, 0.05f, 0f })
            _body.TweenProperty(_sprite, "rotation", angle, 0.32f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>Drops onto its cell at the start of a round ("톡").</summary>
    public void DropIn(float delay)
    {
        var b = BaseScale;
        var rest = Rest;
        _sprite.Position = rest + new Vector2(0, -_cell * 1.4f);
        _sprite.Modulate = Colors.Transparent;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenInterval(delay);
        _body.TweenProperty(_sprite, "modulate", Colors.White, 0.06f);
        _body.TweenProperty(_sprite, "position", rest, 0.24f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _body.TweenProperty(_sprite, "scale", new Vector2(b * 1.15f, b * 0.85f), 0.05f);
        _body.TweenProperty(_sprite, "scale", Vector2.One * b, 0.25f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }
}
