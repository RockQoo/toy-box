using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.PushBox;

/// <summary>
/// The rabbit the child moves. Its picture follows the way it goes: walking sideways (flipped for left), its back going
/// up, its front coming down, the push pose against a box, and a cheer at the end. After a short rest it turns to the
/// front again. Steps are short slides with a little hop, like the maze character.
/// </summary>
public partial class PushBoxRabbit : Node2D
{
    public enum Pose { Idle, Walk, Push, Back, Front, Cheer }

    private const float ArtHeight = 265f;   // All poses share one scale (the pictures differ in size)
    private const float IdleAfter = 0.9f;

    private static readonly Dictionary<Pose, string> Paths = new()
    {
        [Pose.Idle] = "rabbit_idle",
        [Pose.Walk] = "rabbit_walk",
        [Pose.Push] = "rabbit_push",
        [Pose.Back] = "rabbit_back",
        [Pose.Front] = "rabbit_front_walk",
        [Pose.Cheer] = "rabbit_cheer",
    };

    private readonly Dictionary<Pose, Texture2D> _tex = new();
    private Sprite2D _sprite = null!;
    private Tween? _move;
    private Tween? _body;
    private float _cell = 80f;
    private float _base = 1f;
    private float _time;
    private float _still;
    private Pose _pose;
    private bool _holdPose;
    private Vector2 _home;

    public override void _Ready()
    {
        foreach (var (pose, name) in Paths)
            _tex[pose] = Ui.Tex($"res://assets/sprites/buddy/{name}.png");
        _sprite = new Sprite2D();
        AddChild(_sprite);
        SetPose(Pose.Idle, false);
        SetCellSize(_cell);
    }

    public void SetCellSize(float cell)
    {
        _cell = cell;
        if (_sprite is null)
            return;
        _base = cell * 0.98f / ArtHeight;
        _sprite.Scale = Vector2.One * _base;
        _sprite.Position = Rest;
        QueueRedraw();
    }

    private Vector2 Rest => new(0, -_cell * 0.14f);
    private bool Busy => _body?.IsRunning() == true;

    public void Snap(Vector2 position)
    {
        _move?.Kill();
        _home = position;
        Position = position;
    }

    public void SetPose(Pose pose, bool flip)
    {
        _pose = pose;
        _sprite.Texture = _tex[pose];
        _sprite.FlipH = flip;
        _still = 0;
    }

    /// <summary>Keeps the current picture (no turning to the front) until the next move: the finale cheer.</summary>
    public void HoldPose(bool hold) => _holdPose = hold;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        _still += dt;
        if (!_holdPose && _pose != Pose.Idle && _still > IdleAfter && !Busy)
            SetPose(Pose.Idle, false);
        if (Busy || _sprite.Texture is null)
            return;
        var s = Mathf.Sin(_time * 3.2f);
        _sprite.Position = Rest + new Vector2(0, -_cell * 0.025f * Mathf.Abs(s));
        _sprite.Scale = new Vector2(_base * (1 + 0.02f * s), _base * (1 - 0.02f * s));
    }

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, _cell * 0.3f), 0, new Vector2(1, 0.3f));
        DrawCircle(Vector2.Zero, _cell * 0.3f, new Color(0, 0, 0, 0.18f), antialiased: true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>The picture for walking (or pushing) this way.</summary>
    public void Face(MazeDir dir, bool pushing)
    {
        switch (dir)
        {
            case MazeDir.Up:
                SetPose(Pose.Back, false);
                break;
            case MazeDir.Down:
                SetPose(Pose.Front, false);
                break;
            default:
                SetPose(pushing ? Pose.Push : Pose.Walk, dir == MazeDir.Left);
                break;
        }
    }

    /// <summary>One cell: slides there with a small hop (a lower, heavier one when pushing).</summary>
    public void StepTo(Vector2 target, float duration, MazeDir dir, bool pushing)
    {
        Face(dir, pushing);
        _home = target;
        _move?.Kill();
        _move = CreateTween();
        _move.TweenProperty(this, "position", target, duration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

        var b = _base;
        var rest = Rest;
        var lift = pushing ? 0.04f : 0.11f;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "position", rest + new Vector2(0, -_cell * lift), duration * 0.5f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _body.Parallel().TweenProperty(_sprite, "scale", pushing ? new Vector2(b * 1.05f, b * 0.96f) : new Vector2(b * 0.95f, b * 1.06f), duration * 0.5f);
        _body.TweenProperty(_sprite, "position", rest, duration * 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _body.TweenProperty(_sprite, "scale", new Vector2(b * 1.08f, b * 0.92f), 0.04f);
        _body.TweenProperty(_sprite, "scale", Vector2.One * b, 0.1f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Undo: slides back to the cell without turning around.</summary>
    public void SlideBack(Vector2 target, float duration)
    {
        _home = target;
        _still = 0;
        _move?.Kill();
        _move = CreateTween();
        _move.TweenProperty(this, "position", target, duration).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>Something is in the way: a tiny push toward it, a squash, and back. No more than that.</summary>
    public void Bump(MazeDir dir, bool againstBox)
    {
        Face(dir, againstBox);
        var push = DirVector(dir);
        _move?.Kill();
        Position = _home;
        _move = CreateTween();
        _move.TweenProperty(this, "position", _home + push * _cell * 0.12f, 0.06f).SetEase(Tween.EaseType.Out);
        _move.TweenProperty(this, "position", _home, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        var b = _base;
        var squash = push.X != 0 ? new Vector2(b * 0.88f, b * 1.08f) : new Vector2(b * 1.08f, b * 0.88f);
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "position", Rest, 0.04f);
        _body.Parallel().TweenProperty(_sprite, "scale", squash, 0.06f);
        _body.TweenProperty(_sprite, "scale", Vector2.One * b, 0.22f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Pops out of one cell and into another (start over, undoing a start over).</summary>
    public void PopTo(Vector2 target)
    {
        _move?.Kill();
        _body?.Kill();
        SetPose(Pose.Idle, false);
        var b = _base;
        _body = CreateTween();
        _body.TweenProperty(_sprite, "scale", Vector2.One * b * 0.2f, 0.12f).SetEase(Tween.EaseType.In);
        _body.TweenCallback(Callable.From(() => Snap(target)));
        _body.TweenProperty(_sprite, "scale", Vector2.One * b, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>All boxes are home: the cheer pose and two big jumps, the second with a spin.</summary>
    public void Celebrate()
    {
        SetPose(Pose.Cheer, false);
        HoldPose(true);
        var b = _base;
        var big = b * 1.25f;
        var rest = Rest;
        _body?.Kill();
        _body = CreateTween();
        _body.TweenProperty(_sprite, "scale", new Vector2(big * 1.1f, big * 0.85f), 0.1f);
        for (var i = 0; i < 2; i++)
        {
            var height = _cell * (i == 0 ? 0.5f : 0.8f);
            _body.TweenProperty(_sprite, "position", rest + new Vector2(0, -height), 0.22f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            _body.Parallel().TweenProperty(_sprite, "scale", new Vector2(big * 0.95f, big * 1.08f), 0.12f);
            if (i == 1)
                _body.Parallel().TweenProperty(_sprite, "rotation", Mathf.Tau, 0.44f).From(0f);
            _body.TweenProperty(_sprite, "position", rest, 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            _body.TweenProperty(_sprite, "scale", new Vector2(big * 1.12f, big * 0.88f), 0.06f);
            _body.TweenProperty(_sprite, "scale", Vector2.One * big, 0.12f);
        }
        _body.TweenCallback(Callable.From(() => _sprite.Rotation = 0f));
    }

    /// <summary>Drops onto the start cell from above at the beginning of a round.</summary>
    public void DropIn(float delay)
    {
        HoldPose(false);
        SetPose(Pose.Idle, false);
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
        _body.TweenProperty(_sprite, "scale", Vector2.One * b, 0.3f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    public static Vector2 DirVector(MazeDir dir) => dir switch
    {
        MazeDir.Up => Vector2.Up,
        MazeDir.Right => Vector2.Right,
        MazeDir.Down => Vector2.Down,
        _ => Vector2.Left,
    };
}
