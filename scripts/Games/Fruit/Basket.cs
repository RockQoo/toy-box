using System.Collections.Generic;
using Godot;
using ToyBox.Common;

namespace ToyBox.Games.FruitCatch;

/// <summary>
/// The player's basket. The node origin is the bottom center of the basket; <see cref="RimY"/> is the local height of the
/// opening, where falling fruit counts as caught. Moves toward <see cref="TargetX"/> with a little smoothing and tilts
/// with its speed. Only one basket picture exists, so the "바구니" choice recolors it with a hue-shift shader.
/// </summary>
public partial class Basket : Node2D
{
    public const string TexturePath = "res://assets/sprites/fruit/basket.png";

    /// <summary>Hue shifts for the basket colors (the art is orange-brown): 기본, 분홍, 파랑, 초록.</summary>
    private static readonly float[] HueShifts = { 0f, 0.86f, 0.50f, 0.22f };
    private static readonly string[] ColorNames = { "기본", "분홍", "파랑", "초록" };
    private static readonly Material?[] Materials = new Material?[HueShifts.Length];
    private static Shader? _hueShader;

    public static int ColorCount => HueShifts.Length;

    /// <summary>The "바구니" row: the basket picture in each color.</summary>
    public static IReadOnlyList<Choice> Choices
    {
        get
        {
            var list = new List<Choice>();
            for (var i = 0; i < HueShifts.Length; i++)
                list.Add(new Choice(ColorNames[i], TexturePath, IconMaterial: MaterialFor(i)));
            return list;
        }
    }

    /// <summary>Where the opening sits, as a fraction of the picture height from the top.</summary>
    private const float RimFraction = 0.30f;
    /// <summary>Half the opening width as a fraction of the basket width (generous: small hands, fast fruit).</summary>
    private const float OpeningFraction = 0.42f;
    private const float FollowRate = 20f;

    private Sprite2D _sprite = null!;
    private float _baseScale;
    private float _height;
    private Tween? _squash;

    public float Width { get; private set; }
    public float TargetX { get; set; }
    /// <summary>Opening height relative to the node origin (negative: above the bottom).</summary>
    public float RimY => -_height * (1 - RimFraction);
    public float HalfOpening => Width * OpeningFraction;
    public float Velocity { get; private set; }

    public static Basket Create(float width)
    {
        var basket = new Basket();
        var texture = Ui.Tex(TexturePath);
        var size = texture.GetSize();
        basket.Width = width;
        basket._baseScale = width / size.X;
        basket._height = size.Y * basket._baseScale;
        basket._sprite = new Sprite2D
        {
            Texture = texture,
            Scale = new Vector2(basket._baseScale, basket._baseScale),
            // Bottom center on the node origin, so squashing keeps it on the floor
            Offset = new Vector2(0, -size.Y / 2),
        };
        basket.AddChild(basket._sprite);
        return basket;
    }

    public void SetColor(int index) => _sprite.Material = MaterialFor(Mathf.Clamp(index, 0, HueShifts.Length - 1));

    public void Step(float dt, float minX, float maxX)
    {
        TargetX = Mathf.Clamp(TargetX, minX, maxX);
        var x = Mathf.Lerp(Position.X, TargetX, 1 - Mathf.Exp(-FollowRate * dt));
        Velocity = dt > 0 ? (x - Position.X) / dt : 0;
        Position = Position with { X = x };
        // Leans into the movement a little (follow-through)
        Rotation = Mathf.Lerp(Rotation, Mathf.Clamp(Velocity * 0.00012f, -0.14f, 0.14f), 1 - Mathf.Exp(-12f * dt));
    }

    /// <summary>Quick squash-and-stretch when something lands in it. <paramref name="amount"/> 1 for a normal catch.</summary>
    public void Squash(float amount = 1f)
    {
        _squash?.Kill();
        var b = _baseScale;
        _squash = CreateTween();
        _squash.TweenProperty(_sprite, "scale", new Vector2(b * (1 + 0.10f * amount), b * (1 - 0.12f * amount)), 0.05f);
        _squash.TweenProperty(_sprite, "scale", new Vector2(b, b), 0.3f)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A happy little jump (time's up).</summary>
    public void Hop()
    {
        Squash(0.8f);
        var tween = CreateTween();
        tween.TweenProperty(_sprite, "position:y", -40f, 0.18f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_sprite, "position:y", 0f, 0.2f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
    }

    private static Material? MaterialFor(int index)
    {
        if (HueShifts[index] == 0f)
            return null;
        if (Materials[index] is { } cached)
            return cached;
        _hueShader ??= new Shader
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
                    hsv.x = fract(hsv.x + hue_shift);
                    COLOR = vec4(hsv2rgb(hsv), c.a);
                }
                """,
        };
        var material = new ShaderMaterial { Shader = _hueShader };
        material.SetShaderParameter("hue_shift", HueShifts[index]);
        Materials[index] = material;
        return material;
    }
}
