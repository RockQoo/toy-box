using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flappy;

/// <summary>
/// The baby bird. The node origin is the middle of its body (the center of the hit circle); the game moves it.
/// Three wing frames cycle slowly while gliding and quickly right after a flap; the bird stretches a little on a flap,
/// lifts its head while rising and dips it while falling. A bump shows the dizzy picture (stars already drawn over the
/// head) spinning down to the ground.
/// </summary>
public partial class Bird : Node2D
{
    private static readonly string[] FramePaths =
    {
        "res://assets/sprites/flappy/bird_01.png",
        "res://assets/sprites/flappy/bird_02.png",
        "res://assets/sprites/flappy/bird_03.png",
    };
    private const string BumpPath = "res://assets/sprites/flappy/bird_bump.png";

    private const float GlideFrameTime = 0.13f;
    private const float FlapFrameTime = 0.05f;
    private const float FlapAnimTime = 0.3f;

    private readonly Texture2D[] _frames = new Texture2D[3];
    private Texture2D _bump = null!;
    private Node2D _tilt = null!;    // Head up / down (set every frame)
    private Node2D _squash = null!;  // Flap stretch, bounce squash (tweened)
    private Sprite2D _sprite = null!;
    private Tween? _squashTween;
    private Tween? _dizzyTween;
    private float _height;
    private float _frameTimer;
    private int _frame;
    private float _flapAnim;
    private float _hoverTime;
    private bool _dizzy;
    private bool _landed;

    /// <summary>Picture height in pixels (the first frame).</summary>
    public float PictureHeight => _height;

    public static Bird Create(float height)
    {
        var bird = new Bird { _height = height };
        for (var i = 0; i < FramePaths.Length; i++)
            bird._frames[i] = Ui.Tex(FramePaths[i]);
        bird._bump = Ui.Tex(BumpPath);
        bird._tilt = new Node2D();
        bird.AddChild(bird._tilt);
        bird._squash = new Node2D();
        bird._tilt.AddChild(bird._squash);
        bird._sprite = new Sprite2D();
        bird._squash.AddChild(bird._sprite);
        bird.ShowFrame(0);
        return bird;
    }

    private void ShowFrame(int index)
    {
        _frame = index;
        var texture = _frames[index];
        _sprite.Texture = texture;
        // One pixel scale for every frame, so the body keeps its size while the wings move
        _sprite.Scale = Vector2.One * (_height / _frames[0].GetSize().Y);
        // The body sits right of and below the picture center (the wings spread up and left)
        _sprite.Offset = new Vector2(-texture.GetSize().X * 0.08f, -texture.GetSize().Y * 0.04f);
    }

    /// <summary>
    /// Call every frame. <paramref name="fall"/> is the downward speed as a fraction of the fall limit (−1 rising
    /// fast … 1 falling fast); <paramref name="hover"/> bobs gently in place (waiting for the first flap).
    /// </summary>
    public void Step(float dt, float fall, bool hover)
    {
        if (_dizzy)
        {
            // Sitting on the ground a little woozy
            if (_landed)
            {
                _hoverTime += dt;
                _tilt.Rotation = 0.12f * Mathf.Sin(_hoverTime * 4f);
            }
            return;
        }
        _flapAnim -= dt;
        _frameTimer -= dt;
        if (_frameTimer <= 0)
        {
            _frameTimer += _flapAnim > 0 ? FlapFrameTime : GlideFrameTime;
            if (_frameTimer <= 0)
                _frameTimer = GlideFrameTime;
            ShowFrame((_frame + 1) % _frames.Length);
        }
        var target = hover ? 0f : Mathf.Clamp(fall, -1f, 1f) * (fall < 0 ? 0.35f : 0.55f);
        _tilt.Rotation = Mathf.Lerp(_tilt.Rotation, target, Mathf.Min(1f, dt * 10f));
        if (hover)
        {
            _hoverTime += dt;
            _tilt.Position = new Vector2(0, Mathf.Sin(_hoverTime * 3.2f) * _height * 0.08f);
        }
        else
        {
            _tilt.Position = _tilt.Position.Lerp(Vector2.Zero, Mathf.Min(1f, dt * 8f));
        }
    }

    /// <summary>A flap: the wings beat fast for a moment and the body stretches up.</summary>
    public void Flap()
    {
        if (_dizzy)
            return;
        _flapAnim = FlapAnimTime;
        _frameTimer = 0;
        SquashTo(new Vector2(0.88f, 1.14f), 0.05f, 0.25f);
    }

    /// <summary>Head against the ceiling ("콩"): squashed flat for a moment.</summary>
    public void Bonk() => SquashTo(new Vector2(1.15f, 0.82f), 0.04f, 0.3f);

    /// <summary>A hop on the ground.</summary>
    public void Bounce() => SquashTo(new Vector2(1.12f, 0.88f), 0.04f, 0.22f);

    private void SquashTo(Vector2 scale, float into, float back)
    {
        _squashTween?.Kill();
        _squashTween = CreateTween();
        _squashTween.TweenProperty(_squash, "scale", scale, into);
        _squashTween.TweenProperty(_squash, "scale", Vector2.One, back)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Bumped into a pillar: the dizzy face, a little hop back while spinning once, then a soft landing at
    /// <paramref name="restY"/> (pixels, in the parent) with a gentle sway. Nothing breaks.
    /// </summary>
    public void Dizzy(float restY)
    {
        _dizzy = true;
        _landed = false;
        _squashTween?.Kill();
        _dizzyTween?.Kill();
        _sprite.Texture = _bump;
        _sprite.Scale = Vector2.One * (_height * 1.3f / _bump.GetSize().Y);
        _sprite.Offset = new Vector2(0, -_bump.GetSize().Y * 0.12f);
        _squash.Scale = new Vector2(1.2f, 0.85f);
        _tilt.Position = Vector2.Zero;

        _dizzyTween = CreateTween();
        _dizzyTween.TweenProperty(_squash, "scale", Vector2.One, 0.2f);
        _dizzyTween.Parallel().TweenProperty(this, "position", Position + new Vector2(-_height * 0.35f, -_height * 0.25f), 0.25f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _dizzyTween.Parallel().TweenProperty(_tilt, "rotation", _tilt.Rotation - Mathf.Tau, 0.9f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        var fall = Mathf.Max(0.35f, Mathf.Abs(restY - Position.Y) / (_height * 6f));
        _dizzyTween.TweenProperty(this, "position:y", restY, fall).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _dizzyTween.TweenCallback(Callable.From(() =>
        {
            _tilt.Rotation = 0;
            _squash.Scale = new Vector2(1.2f, 0.82f);
            _landed = true;
        }));
        _dizzyTween.TweenProperty(_squash, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Back to normal for a new round.</summary>
    public void Revive()
    {
        _dizzy = false;
        _landed = false;
        _dizzyTween?.Kill();
        _squashTween?.Kill();
        _tilt.Rotation = 0;
        _tilt.Position = Vector2.Zero;
        _squash.Scale = Vector2.One;
        ShowFrame(0);
    }

    /// <summary>A bright flash (milestones).</summary>
    public void Flash()
    {
        _sprite.SelfModulate = new Color(1.6f, 1.6f, 1.4f);
        CreateTween().TweenProperty(_sprite, "self_modulate", Colors.White, 0.4f);
    }

    /// <summary>The fall limit, for <see cref="Step"/>'s <c>fall</c> argument.</summary>
    public static float FallFraction(float velocity) => velocity / FlappyRules.MaxFallSpeed;
}
