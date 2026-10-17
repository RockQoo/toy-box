using System;
using Godot;

namespace ToyBox.Common;

/// <summary>Full-screen "3, 2, 1, 시작!" overlay. Calls <see cref="Finished"/> on "시작!" and frees itself shortly after.</summary>
public partial class Countdown : Control
{
    public event Action? Finished;

    private Label _label = null!;

    public static Countdown Create()
    {
        var countdown = new Countdown { MouseFilter = MouseFilterEnum.Ignore };
        countdown.SetAnchorsPreset(LayoutPreset.FullRect);
        return countdown;
    }

    public override void _Ready()
    {
        _label = Ui.Label("", 200, Colors.White, 28, UiTheme.Ink);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_label);
        Run();
    }

    private async void Run()
    {
        var colors = new[] { UiTheme.Pink, UiTheme.Orange, UiTheme.Blue };
        for (var n = 3; n >= 1; n--)
        {
            Show($"{n}", colors[3 - n]);
            Audio.I.Tick(1f + (3 - n) * 0.12f);
            await ToSignal(GetTree().CreateTimer(App.I.Autoplay ? 0.3f : 1f), SceneTreeTimer.SignalName.Timeout);
        }
        Show("시작!", UiTheme.Green);
        Audio.I.Go();
        Finished?.Invoke();
        var tween = CreateTween();
        tween.TweenInterval(0.35f);
        tween.TweenProperty(_label, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    private void Show(string text, Color color)
    {
        _label.Text = text;
        _label.AddThemeColorOverride("font_color", color);
        _label.PivotOffset = GetViewportRect().Size / 2;
        _label.Scale = new Vector2(1.8f, 1.8f);
        _label.Modulate = Colors.White;
        CreateTween().TweenProperty(_label, "scale", Vector2.One, 0.35f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
