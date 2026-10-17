using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.DinoRunner;

/// <summary>
/// The running dino. The node origin is the feet on the ground line; the game sets <see cref="Lift"/> (pixels above
/// the ground) from <see cref="DinoJump"/>. There is a single run picture, so the run cycle is faked with a bob, a tilt
/// and a little squash in time with the steps. Jumps stretch on take-off and squash on landing; a crash tumbles into
/// the crouch pose with dizzy stars. Only a green dino exists, so the "공룡" choice recolors its skin with a shader.
/// </summary>
public partial class Dino : Node2D
{
    public const string RunPath = "res://assets/sprites/dino/dino_run.png";
    private const string JumpPath = "res://assets/sprites/dino/dino_jump.png";
    private const string CrouchPath = "res://assets/sprites/dino/dino_crouch.png";

    /// <summary>Hue shifts for the skin (the art is green): 초록, 파랑, 보라, 분홍, 노랑.</summary>
    private static readonly float[] HueShifts = { 0f, 0.30f, 0.45f, 0.62f, -0.15f };
    private static readonly string[] ColorNames = { "초록", "파랑", "보라", "분홍", "노랑" };
    private static readonly Material?[] Materials = new Material?[HueShifts.Length];
    private static Shader? _skinShader;

    public static int ColorCount => HueShifts.Length;

    /// <summary>The "공룡" row: the dino in each color.</summary>
    public static IReadOnlyList<Choice> Choices
    {
        get
        {
            var list = new List<Choice>();
            for (var i = 0; i < HueShifts.Length; i++)
                list.Add(new Choice(ColorNames[i], RunPath, IconMaterial: MaterialFor(i)));
            return list;
        }
    }

    private Node2D _body = null!;    // Bob, tilt, in-air stretch (set every frame)
    private Node2D _squash = null!;  // Take-off / landing squash (tweened)
    private Sprite2D _sprite = null!;
    private Node2D _dizzy = null!;
    private Texture2D _run = null!, _jump = null!, _crouch = null!;
    private Tween? _squashTween;
    private Tween? _tumbleTween;
    private float _height;
    private float _phase;
    private bool _tumbled;

    /// <summary>Feet above the ground in pixels.</summary>
    public float Lift { get; set; }
    /// <summary>Picture height in pixels while running.</summary>
    public float Height => _height;

    public static Dino Create(float height)
    {
        var dino = new Dino { _height = height };
        dino._run = Ui.Tex(RunPath);
        dino._jump = Ui.Tex(JumpPath);
        dino._crouch = Ui.Tex(CrouchPath);
        dino._body = new Node2D();
        dino.AddChild(dino._body);
        dino._squash = new Node2D();
        dino._body.AddChild(dino._squash);
        dino._sprite = new Sprite2D();
        dino._squash.AddChild(dino._sprite);
        dino._dizzy = new Node2D { Visible = false };
        dino.AddChild(dino._dizzy);
        var star = Ui.Tex("res://assets/sprites/ui/star.png");
        for (var i = 0; i < 3; i++)
        {
            var s = new Sprite2D { Texture = star };
            s.Scale = Vector2.One * (height * 0.14f / star.GetSize().Y);
            dino._dizzy.AddChild(s);
        }
        dino.Pose(dino._run, height);
        return dino;
    }

    public void SetColor(int index) =>
        _sprite.Material = MaterialFor(Mathf.Clamp(index, 0, HueShifts.Length - 1));

    /// <summary>Shows a picture scaled to <paramref name="height"/>, standing on the origin (bottom center).</summary>
    private void Pose(Texture2D texture, float height)
    {
        if (_sprite.Texture == texture)
            return;
        var size = texture.GetSize();
        _sprite.Texture = texture;
        _sprite.Scale = Vector2.One * (height / size.Y);
        // A few pixels into the ground: the art has a little transparent margin under the feet
        _sprite.Offset = new Vector2(0, -size.Y / 2 + size.Y * 0.02f);
    }

    /// <summary>
    /// Call every frame. <paramref name="stepRate"/> is steps per second (faster runs step faster);
    /// <paramref name="upSpeed"/> is the jump velocity as a fraction of the launch speed (1 at take-off, −1 at landing).
    /// </summary>
    public void Step(float dt, float stepRate, bool airborne, float upSpeed)
    {
        if (_tumbled)
        {
            _phase += dt * 5f;
            for (var i = 0; i < _dizzy.GetChildCount(); i++)
            {
                var a = _phase + i * Mathf.Tau / 3;
                var star = _dizzy.GetChild<Sprite2D>(i);
                star.Position = new Vector2(Mathf.Cos(a) * _height * 0.22f, Mathf.Sin(a) * _height * 0.07f);
                star.Rotation = a;
            }
            return;
        }
        if (airborne)
        {
            Pose(_jump, _height);
            // Stretch while rising fast, round out at the apex, lean nose-down on the way down
            var stretch = 0.07f * Mathf.Abs(upSpeed);
            _body.Scale = new Vector2(1 - stretch, 1 + stretch);
            _body.Rotation = -0.12f * upSpeed;
            _body.Position = new Vector2(0, -Lift);
            return;
        }
        Pose(_run, _height);
        _phase += dt * stepRate * Mathf.Pi;
        var s = Mathf.Sin(_phase);
        var bounce = Mathf.Abs(s);
        // Each half wave is one step: up between steps, squashed as a foot lands, a slight rock forward and back
        _body.Position = new Vector2(0, -Lift - bounce * _height * 0.05f);
        _body.Scale = new Vector2(1 + 0.035f * (1 - bounce), 1 - 0.035f * (1 - bounce));
        _body.Rotation = 0.05f * Mathf.Cos(_phase) + 0.03f;
    }

    /// <summary>True on the frame a foot touches down (for dust puffs).</summary>
    public bool FootDown(float dt, float stepRate)
    {
        var before = Mathf.FloorToInt((_phase - dt * stepRate * Mathf.Pi) / Mathf.Pi);
        return Mathf.FloorToInt(_phase / Mathf.Pi) != before;
    }

    /// <summary>Take-off: a quick crouch then a stretch upward.</summary>
    public void TakeOff() => SquashTo(new Vector2(0.86f, 1.16f), 0.06f, 0.3f);

    /// <summary>Landing: squashed flat for a moment, then springs back.</summary>
    public void Land() => SquashTo(new Vector2(1.2f, 0.8f), 0.05f, 0.35f);

    private void SquashTo(Vector2 scale, float into, float back)
    {
        _squashTween?.Kill();
        _squashTween = CreateTween();
        _squashTween.TweenProperty(_squash, "scale", scale, into);
        _squashTween.TweenProperty(_squash, "scale", Vector2.One, back)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Crash: a small hop backward with a half roll, then lying in the crouch pose with stars circling the head.
    /// Cartoonish and soft: nothing breaks, the dino just sits down dizzy.
    /// </summary>
    public void Tumble()
    {
        _tumbled = true;
        _squashTween?.Kill();
        _tumbleTween?.Kill();
        Pose(_jump, _height);
        var startLift = Lift;
        _body.Scale = Vector2.One;
        _tumbleTween = CreateTween();
        // Hop up and back while rolling backward
        _tumbleTween.TweenProperty(_body, "position", new Vector2(-_height * 0.35f, -startLift - _height * 0.35f), 0.25f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tumbleTween.Parallel().TweenProperty(_body, "rotation", -0.9f, 0.25f);
        _tumbleTween.TweenProperty(_body, "position", new Vector2(-_height * 0.6f, 0), 0.25f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _tumbleTween.Parallel().TweenProperty(_body, "rotation", -0.2f, 0.25f);
        _tumbleTween.TweenCallback(Callable.From(() =>
        {
            // Plop: lying down, squashed, then settles
            Pose(_crouch, _height * 0.62f);
            _body.Rotation = 0;
            _squash.Scale = new Vector2(1.25f, 0.75f);
            _dizzy.Position = new Vector2(_body.Position.X + _height * 0.4f, -_height * 0.7f);
            _dizzy.Visible = true;
        }));
        _tumbleTween.TweenProperty(_squash, "scale", Vector2.One, 0.4f)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Back on its feet for a new run.</summary>
    public void Revive()
    {
        _tumbled = false;
        _tumbleTween?.Kill();
        _squashTween?.Kill();
        _dizzy.Visible = false;
        _body.Position = Vector2.Zero;
        _body.Rotation = 0;
        _body.Scale = Vector2.One;
        _squash.Scale = Vector2.One;
        Lift = 0;
        Pose(_run, _height);
        SquashTo(new Vector2(0.9f, 1.12f), 0.06f, 0.3f);
    }

    /// <summary>A bright flash (milestones).</summary>
    public void Flash()
    {
        _sprite.SelfModulate = new Color(1.6f, 1.6f, 1.4f);
        CreateTween().TweenProperty(_sprite, "self_modulate", Colors.White, 0.4f);
    }

    private static Material? MaterialFor(int index)
    {
        if (HueShifts[index] == 0f)
            return null;
        if (Materials[index] is { } cached)
            return cached;
        // Only the green skin turns; the orange spikes, cream belly, eyes and outline keep their colors
        _skinShader ??= new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform float hue_shift = 0.0;

                vec3 rgb2hsv(vec3 c) {
                    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
                    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
                    float d = q.x - min(q.w, q.y);
                    float e = 1.0e-10;
                    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
                }

                vec3 hsv2rgb(vec3 c) {
                    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
                    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
                }

                void fragment() {
                    vec4 c = texture(TEXTURE, UV) * COLOR;
                    vec3 hsv = rgb2hsv(c.rgb);
                    float green = 1.0 - smoothstep(0.09, 0.15, abs(hsv.x - 0.29));
                    float mask = green * smoothstep(0.18, 0.35, hsv.y);
                    vec3 shifted = hsv2rgb(vec3(fract(hsv.x + hue_shift), hsv.y, hsv.z));
                    COLOR = vec4(mix(c.rgb, shifted, mask), c.a);
                }
                """,
        };
        var material = new ShaderMaterial { Shader = _skinShader };
        material.SetShaderParameter("hue_shift", HueShifts[index]);
        Materials[index] = material;
        return material;
    }
}
