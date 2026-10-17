using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Maze;

/// <summary>
/// The child's character in the maze (a <see cref="Buddy.Choices"/> picture) with a soft shadow. Each step is a
/// small hop with a squash on landing; a wall bump is a tiny nudge toward the wall and back; the escape is a big
/// spinning double jump.
/// </summary>
public partial class MazePlayer : Node2D
{
    private Sprite2D _sprite = null!;
    private Tween? _move;
    private Tween? _body;
    private float _cell = 60f;
    private float _base = 1f;
    private float _time;
    private Vector2 _home;

    public override void _Ready()
    {
        _sprite = new Sprite2D();
        AddChild(_sprite);
    }

    public void SetCharacter(int index)
    {
        var choice = Buddy.Choices[Mathf.Clamp(index, 0, Buddy.Choices.Count - 1)];
        _sprite.Texture = Ui.Tex(choice.IconPath!);
        SetCellSize(_cell);
    }

    public void SetCellSize(float cell)
    {
        _cell = cell;
        if (_sprite?.Texture is null)
            return;
        var size = _sprite.Texture.GetSize();
        _base = cell * 0.8f / Mathf.Max(size.X, size.Y);
        _sprite.Scale = new Vector2(_base, _base);
        _sprite.Position = Rest;
        QueueRedraw();
    }

    /// <summary>Places the player on a cell at once (layout changes, a new round).</summary>
    public void Snap(Vector2 position)
    {
        _move?.Kill();
        _home = position;
        Position = position;
    }

    private Vector2 Rest => new(0, -_cell * 0.04f);
    private bool Busy => _body?.IsRunning() == true;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (Busy || _sprite.Texture is null)
            return;
        // Idle breathing
        var s = Mathf.Sin(_time * 3.2f);
        _sprite.Position = Rest + new Vector2(0, -_cell * 0.03f * Mathf.Abs(s));
        _sprite.Scale = new Vector2(_base * (1 + 0.025f * s), _base * (1 - 0.025f * s));
    }

    public override void _Draw() =>
        DrawSetTransformAndEllipse(new Vector2(0, _cell * 0.32f), _cell * 0.3f, _cell * 0.09f, new Color(0, 0, 0, 0.18f));

    private void DrawSetTransformAndEllipse(Vector2 center, float rx, float ry, Color color)
    {
        DrawSetTransform(center, 0, new Vector2(1, ry / rx));
        DrawCircle(Vector2.Zero, rx, color, antialiased: true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>One cell: slides there with a little hop and lands with a squash. Faces left or right as it walks.</summary>
    public void StepTo(Vector2 target, float duration, MazeDir dir)
    {
        if (dir == MazeDir.Left)
            _sprite.FlipH = true;
        else if (dir == MazeDir.Right)
            _sprite.FlipH = false;
        _home = target;
        _move?.Kill();
        _move = CreateTween();
        _move.TweenProperty(this, "position", target, duration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        var b = _base;
        var rest = Rest;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "position", rest + new Vector2(0, -_cell * 0.12f), duration * 0.5f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _body.Parallel().TweenProperty(_sprite, "scale", new Vector2(b * 0.94f, b * 1.07f), duration * 0.5f);
        _body.TweenProperty(_sprite, "position", rest, duration * 0.5f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _body.TweenProperty(_sprite, "scale", new Vector2(b * 1.1f, b * 0.9f), 0.04f);
        _body.TweenProperty(_sprite, "scale", new Vector2(b, b), 0.1f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A wall is in the way: a tiny push toward it, a squash against it, and back. No more than that.</summary>
    public void Bump(MazeDir dir)
    {
        var push = dir switch
        {
            MazeDir.Up => Vector2.Up,
            MazeDir.Right => Vector2.Right,
            MazeDir.Down => Vector2.Down,
            _ => Vector2.Left,
        };
        _move?.Kill();
        Position = _home;
        _move = CreateTween();
        _move.TweenProperty(this, "position", _home + push * _cell * 0.13f, 0.06f).SetEase(Tween.EaseType.Out);
        _move.TweenProperty(this, "position", _home, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        var b = _base;
        var squash = push.X != 0 ? new Vector2(b * 0.86f, b * 1.1f) : new Vector2(b * 1.1f, b * 0.86f);
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "position", Rest, 0.04f);
        _body.Parallel().TweenProperty(_sprite, "scale", squash, 0.06f);
        _body.TweenProperty(_sprite, "scale", new Vector2(b, b), 0.22f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A happy little hop in place (a star picked up).</summary>
    public void Cheer()
    {
        var b = _base;
        var rest = Rest;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "position", rest + new Vector2(0, -_cell * 0.25f), 0.12f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _body.Parallel().TweenProperty(_sprite, "scale", new Vector2(b * 1.12f, b * 1.12f), 0.12f);
        _body.TweenProperty(_sprite, "position", rest, 0.14f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _body.TweenProperty(_sprite, "scale", new Vector2(b, b), 0.15f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>The escape: grows, two big jumps (the second with a full spin), lands with a squash, then settles back.</summary>
    public void Celebrate()
    {
        var b = _base;
        var big = b * 1.35f;
        var rest = Rest;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "scale", new Vector2(big * 1.1f, big * 0.85f), 0.1f);
        for (var i = 0; i < 2; i++)
        {
            var height = _cell * (i == 0 ? 0.6f : 0.9f);
            _body.TweenProperty(_sprite, "position", rest + new Vector2(0, -height), 0.22f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            _body.Parallel().TweenProperty(_sprite, "scale", new Vector2(big * 0.95f, big * 1.08f), 0.12f);
            if (i == 1)
                _body.Parallel().TweenProperty(_sprite, "rotation", Mathf.Tau, 0.44f).From(0f);
            _body.TweenProperty(_sprite, "position", rest, 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            _body.TweenProperty(_sprite, "scale", new Vector2(big * 1.12f, big * 0.88f), 0.06f);
            _body.TweenProperty(_sprite, "scale", new Vector2(big, big), 0.12f);
        }
        _body.TweenCallback(Callable.From(() => _sprite.Rotation = 0f));
        _body.TweenProperty(_sprite, "scale", new Vector2(b, b), 0.3f).SetDelay(0.5f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Drops onto the start cell from above at the beginning of a round.</summary>
    public void DropIn(float delay)
    {
        var b = _base;
        var rest = Rest;
        _sprite.Position = rest + new Vector2(0, -_cell * 2.2f);
        _sprite.Modulate = Colors.Transparent;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenInterval(delay);
        _body.TweenProperty(_sprite, "modulate", Colors.White, 0.08f);
        _body.TweenProperty(_sprite, "position", rest, 0.32f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _body.TweenProperty(_sprite, "scale", new Vector2(b * 1.2f, b * 0.8f), 0.05f);
        _body.TweenProperty(_sprite, "scale", new Vector2(b, b), 0.3f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }
}
