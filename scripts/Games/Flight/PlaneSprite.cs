using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flight;

/// <summary>
/// The rabbit's plane. The node origin is the plane's hit dot; the game moves it. The picture follows the vertical
/// motion (nose up / down), bobs a little, and on a bump shows the surprised face, spins once and blinks while it is
/// safe. The heart's protective bubble is drawn around it.
/// </summary>
public partial class PlaneSprite : Node2D
{
    private const string Dir = "res://assets/sprites/flight/";
    private const float TurnSpeed = 0.25f; // Screen heights per second that count as climbing / diving

    private Texture2D _idle = null!, _up = null!, _down = null!, _surprised = null!;
    private Node2D _body = null!;
    private Sprite2D _sprite = null!;
    private Sprite2D _shield = null!;
    private Tween? _spin;
    private float _time;
    private float _tilt;
    private float _shieldAge;

    public static PlaneSprite Create(float width)
    {
        var plane = new PlaneSprite();
        plane._idle = Ui.Tex(Dir + "plane_idle.png");
        plane._up = Ui.Tex(Dir + "plane_up.png");
        plane._down = Ui.Tex(Dir + "plane_down.png");
        plane._surprised = Ui.Tex(Dir + "plane_surprised.png");
        plane._body = new Node2D();
        plane.AddChild(plane._body);
        plane._sprite = new Sprite2D { Texture = plane._idle };
        plane._body.AddChild(plane._sprite);
        plane._shield = new Sprite2D { Texture = Ui.Tex(Dir + "shot_bubble.png"), Visible = false };
        plane.AddChild(plane._shield);
        plane.SetWidth(width);
        return plane;
    }

    /// <summary>Picture width in pixels (one scale for all faces, from the plain one).</summary>
    public void SetWidth(float width)
    {
        _sprite.Scale = Vector2.One * (width / _idle.GetSize().X);
        _shield.Scale = Vector2.One * (width * 1.15f / _shield.Texture.GetSize().X);
    }

    /// <summary>
    /// Call every frame. <paramref name="climb"/> is the vertical speed in screen heights per second (negative = up);
    /// <paramref name="blinking"/> while it cannot be bumped; <paramref name="surprised"/> shows the surprised face.
    /// </summary>
    public void Step(float dt, float climb, bool blinking, bool surprised, bool shield)
    {
        _time += dt;
        _sprite.Texture = surprised ? _surprised
            : climb < -TurnSpeed ? _up
            : climb > TurnSpeed ? _down
            : _idle;
        // Lean into the motion, gently
        var target = surprised ? 0f : Mathf.Clamp(climb * 0.25f, -0.18f, 0.18f);
        _tilt = Mathf.Lerp(_tilt, target, Mathf.Min(1f, dt * 8f));
        if (_spin is null || !_spin.IsRunning())
            _body.Rotation = _tilt;
        _sprite.Position = new Vector2(0, 3f * Mathf.Sin(_time * 3.2f));
        _sprite.Modulate = blinking && Mathf.Sin(_time * 28f) > 0 ? new Color(1, 1, 1, 0.35f) : Colors.White;

        if (shield != _shield.Visible)
        {
            _shield.Visible = shield;
            _shieldAge = 0;
        }
        if (shield)
        {
            _shieldAge += dt;
            var pop = _shieldAge < 0.25f ? 0.6f + 1.6f * _shieldAge : 1f;
            var wobble = 1f + 0.04f * Mathf.Sin(_time * 5f);
            _shield.Scale = Vector2.One * (BaseShieldScale * pop * wobble);
            _shield.Modulate = new Color(1, 1, 1, 0.32f);
        }
    }

    private float BaseShieldScale => _sprite.Scale.X * _idle.GetSize().X * 1.15f / _shield.Texture.GetSize().X;

    /// <summary>Bumped: one quick spin.</summary>
    public void Surprise()
    {
        _spin?.Kill();
        _body.Rotation = 0;
        _spin = CreateTween();
        _spin.TweenProperty(_body, "rotation", Mathf.Tau, 0.6f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _spin.TweenCallback(Callable.From(() => _body.Rotation = 0));
    }

    /// <summary>A little hop of joy (items, the end).</summary>
    public void Cheer()
    {
        var tween = CreateTween();
        tween.TweenProperty(_body, "scale", new Vector2(1.15f, 1.15f), 0.12f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_body, "scale", Vector2.One, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
