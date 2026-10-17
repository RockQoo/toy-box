using System.Collections.Generic;
using Godot;

namespace ToyBox.Common;

/// <summary>
/// The cheering character the child picks. Bobs while idle, hops when the player scores,
/// and shows a speech bubble for milestones. Shared by the games that have a "캐릭터 선택".
/// </summary>
public partial class Buddy : Node2D
{
    public static readonly IReadOnlyList<Choice> Choices = new[]
    {
        new Choice("토끼", "res://assets/sprites/animal/rabbit.png"),
        new Choice("판다", "res://assets/sprites/animal/panda.png"),
        new Choice("펭귄", "res://assets/sprites/animal/penguin.png"),
        new Choice("사자", "res://assets/sprites/animal/lion.png"),
        new Choice("공룡", "res://assets/sprites/dino/dino_run.png"),
    };

    private const float Height = 150f;

    private Sprite2D _sprite = null!;
    private PanelContainer _bubble = null!;
    private Label _bubbleText = null!;
    private Tween? _hop;
    private Tween? _bubbleTween;
    private float _time;
    private float _baseScale = 1f;

    public override void _Ready()
    {
        _sprite = new Sprite2D { Offset = new Vector2(0, 0) };
        AddChild(_sprite);

        _bubble = new PanelContainer { Visible = false };
        _bubble.AddThemeStyleboxOverride("panel", UiTheme.RaisedBox(Colors.White, UiTheme.Ink, 4, 12, 20));
        _bubbleText = Ui.Label("", 28, UiTheme.Ink);
        _bubble.AddChild(_bubbleText);
        AddChild(_bubble);
    }

    public void SetCharacter(int index)
    {
        var choice = Choices[Mathf.Clamp(index, 0, Choices.Count - 1)];
        _sprite.Texture = Ui.Tex(choice.IconPath!);
        _baseScale = Height / _sprite.Texture.GetSize().Y;
        _sprite.Scale = new Vector2(_baseScale, _baseScale);
        _sprite.Position = new Vector2(0, -Height / 2);
        Hop();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_hop is null || !_hop.IsRunning())
        {
            // Idle breathing: a gentle bob with a little squash
            var s = Mathf.Sin(_time * 3f);
            _sprite.Position = new Vector2(0, -Height / 2 - 4 * Mathf.Abs(s));
            _sprite.Scale = new Vector2(_baseScale * (1 + 0.02f * s), _baseScale * (1 - 0.02f * s));
        }
    }

    /// <summary>Quick squash-and-jump, e.g. when the player scores.</summary>
    public void Hop(float height = 26f)
    {
        _hop?.Kill();
        var b = _baseScale;
        var rest = new Vector2(0, -Height / 2);
        _hop = CreateTween();
        _hop.TweenProperty(_sprite, "scale", new Vector2(b * 1.12f, b * 0.88f), 0.05f);
        _hop.Parallel().TweenProperty(_sprite, "position", rest, 0.05f);
        _hop.TweenProperty(_sprite, "scale", new Vector2(b * 0.92f, b * 1.1f), 0.12f);
        _hop.Parallel().TweenProperty(_sprite, "position", rest + new Vector2(0, -height), 0.12f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _hop.TweenProperty(_sprite, "position", rest, 0.14f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        _hop.Parallel().TweenProperty(_sprite, "scale", new Vector2(b, b), 0.14f);
    }

    /// <summary>Shows a speech bubble above the head for a moment.</summary>
    public void Say(string text, float seconds = 1.4f)
    {
        _bubbleText.Text = text;
        _bubble.Visible = true;
        _bubble.ResetSize();
        _bubble.Position = new Vector2(-30, -Height - _bubble.Size.Y - 18);
        _bubble.PivotOffset = new Vector2(30, _bubble.Size.Y);
        _bubbleTween?.Kill();
        _bubble.Scale = new Vector2(0.3f, 0.3f);
        _bubble.Modulate = Colors.White;
        _bubbleTween = CreateTween();
        _bubbleTween.TweenProperty(_bubble, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bubbleTween.TweenInterval(seconds);
        _bubbleTween.TweenProperty(_bubble, "modulate:a", 0f, 0.3f);
        _bubbleTween.TweenCallback(Callable.From(() => _bubble.Visible = false));
        Hop(40f);
    }
}
