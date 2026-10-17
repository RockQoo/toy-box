using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Snake;

/// <summary>
/// A leaf on the field: pops in, sways a little; the golden one is recolored gold, glows, pulses and blinks before it
/// goes away. <see cref="EatenBy"/> shrinks it into the caterpillar's mouth.
/// </summary>
public partial class LeafSprite : Node2D
{
    private static Shader? _goldShader;

    private Sprite2D _picture = null!;
    private float _size;
    private float _time;
    private bool _golden;
    private bool _leaving;

    /// <summary>Seconds left for a golden leaf (it blinks in the last two). Negative = not timed.</summary>
    public float TimeLeft { get; set; } = -1f;

    public static LeafSprite Create(float cell, bool golden, float phase)
    {
        var tex = Ui.Tex("res://assets/sprites/snake/leaf.png");
        var size = cell * (golden ? 0.95f : 0.82f);
        var leaf = new LeafSprite { _size = size, _golden = golden, _time = phase, Scale = Vector2.Zero };
        leaf._picture = new Sprite2D { Texture = tex, Scale = Vector2.One * size / tex.GetWidth() };
        if (golden)
            leaf._picture.Material = GoldMaterial();
        leaf.AddChild(leaf._picture);
        leaf.CreateTween().TweenProperty(leaf, "scale", Vector2.One, 0.35f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        return leaf;
    }

    /// <summary>Maps the green leaf's brightness onto golds (dark outline stays dark), so it reads as a golden leaf.</summary>
    private static ShaderMaterial GoldMaterial()
    {
        _goldShader ??= new Shader
        {
            Code = """
                shader_type canvas_item;

                void fragment() {
                    vec4 c = texture(TEXTURE, UV) * COLOR;
                    float l = dot(c.rgb, vec3(0.30, 0.59, 0.11));
                    vec3 gold = mix(vec3(0.55, 0.30, 0.02), vec3(1.0, 0.80, 0.15), smoothstep(0.15, 0.55, l));
                    gold = mix(gold, vec3(1.0, 0.97, 0.70), smoothstep(0.70, 0.95, l));
                    COLOR = vec4(gold, c.a);
                }
                """,
        };
        return new ShaderMaterial { Shader = _goldShader };
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        if (TimeLeft > 0)
            TimeLeft -= dt;
        var alpha = 1f;
        if (_golden && TimeLeft is > 0 and < 2f && !_leaving)
            alpha = Mathf.Sin(_time * 18f) > 0 ? 1f : 0.45f;
        _picture.Rotation = Mathf.Sin(_time * 2.4f) * 0.12f;
        _picture.Modulate = Colors.White with { A = alpha };
        if (_golden)
            _picture.Scale = Vector2.One * _size / _picture.Texture.GetWidth() * (1f + 0.08f * Mathf.Sin(_time * 7f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_golden)
            return;
        var alpha = _picture.Modulate.A;
        var pulse = 1f + 0.08f * Mathf.Sin(_time * 7f);
        DrawCircle(Vector2.Zero, _size * 0.66f * pulse, new Color(1f, 0.9f, 0.35f, 0.35f * alpha));
        DrawCircle(Vector2.Zero, _size * 0.5f * pulse, new Color(1f, 0.95f, 0.6f, 0.5f * alpha));
        // Two twinkles circling the leaf
        for (var i = 0; i < 2; i++)
        {
            var a = _time * 2.5f + i * Mathf.Pi;
            var p = Vector2.Right.Rotated(a) * _size * 0.6f;
            var r = _size * (0.1f + 0.04f * Mathf.Sin(_time * 9f + i));
            DrawLine(p - new Vector2(r, 0), p + new Vector2(r, 0), Colors.White with { A = alpha }, 3f);
            DrawLine(p - new Vector2(0, r), p + new Vector2(0, r), Colors.White with { A = alpha }, 3f);
        }
    }

    /// <summary>Eaten: slides into the mouth while shrinking, then frees itself.</summary>
    public void EatenBy(Vector2 mouth)
    {
        _leaving = true;
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "position", mouth, 0.12f);
        tween.TweenProperty(this, "scale", Vector2.Zero, 0.14f).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>Not eaten in time (golden) or the round ended: fades and shrinks away.</summary>
    public void FadeAway()
    {
        _leaving = true;
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "modulate:a", 0f, 0.35f);
        tween.TweenProperty(this, "scale", new Vector2(0.4f, 0.4f), 0.35f);
        tween.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
