using System;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>
/// In-round HUD shared by the timed games (balloon, fruit): score panel with an icon, "그만하기" (asks first, see
/// <see cref="QuitConfirm"/>), remaining time as text plus <see cref="TimerBar"/>, a banner line for difficulty steps,
/// and the "끝!" beat when time is up. Add it to the game's CanvasLayer; it fills the screen and ignores the mouse
/// except on its buttons.
/// </summary>
public partial class RoundHud : Control
{
    private float _total;
    private int _lastWholeSecond;
    private Label _scoreLabel = null!;
    private Label _timeLabel = null!;
    private TimerBar _timerBar = null!;
    private TextureRect _scoreIcon = null!;
    private Label _banner = null!;

    public Texture2D ScoreIcon
    {
        get => _scoreIcon.Texture;
        set => _scoreIcon.Texture = value;
    }

    /// <summary>The score label, e.g. to punch it harder for a milestone.</summary>
    public Control ScoreLabel => _scoreLabel;

    public static RoundHud Create(float totalSeconds, string scoreIconPath)
    {
        var hud = new RoundHud { _total = totalSeconds, MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsPreset(LayoutPreset.FullRect);
        hud.Build(scoreIconPath);
        return hud;
    }

    private void Build(string scoreIconPath)
    {
        // Full-screen margin + column: top bar (score, quit ... time), banner, flexible space
        var frame = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            frame.AddThemeConstantOverride($"margin_{side}", 18);
        AddChild(frame);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        frame.AddChild(column);

        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddChild(top);

        var scorePanel = Ui.Panel(Colors.White, 12, 4);
        var scoreRow = new HBoxContainer();
        scoreRow.AddThemeConstantOverride("separation", 10);
        _scoreIcon = Ui.Icon(scoreIconPath, 48);
        scoreRow.AddChild(_scoreIcon);
        _scoreLabel = Ui.Label("점수 0", 40, UiTheme.Ink);
        _scoreLabel.CustomMinimumSize = new Vector2(150, 0);
        scoreRow.AddChild(_scoreLabel);
        scorePanel.AddChild(scoreRow);
        top.AddChild(scorePanel);

        // Up here, away from the play area, and it asks before quitting
        top.AddChild(Ui.Spacer(0, 14));
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(GetParent()), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        top.AddChild(quit);

        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        var timePanel = Ui.Panel(Colors.White, 12, 4);
        var timeColumn = new VBoxContainer();
        timeColumn.AddThemeConstantOverride("separation", 4);
        _timeLabel = Ui.Label("", 40, UiTheme.Ink);
        _timeLabel.CustomMinimumSize = new Vector2(270, 0);
        _timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        timeColumn.AddChild(_timeLabel);
        _timerBar = TimerBar.Create(_total, new Vector2(270, 26));
        timeColumn.AddChild(_timerBar);
        timePanel.AddChild(timeColumn);
        top.AddChild(timePanel);

        _banner = Ui.Label("", 44, Colors.White, 14, UiTheme.Pink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.Modulate = Colors.Transparent;
        column.AddChild(Ui.Spacer(30));
        column.AddChild(_banner);

        Reset();
    }

    /// <summary>New round: score 0, full time in the normal color.</summary>
    public void Reset()
    {
        SetScore(0);
        _lastWholeSecond = RoundClock.SecondsLeft(0, _total);
        _timeLabel.Text = TimeText(_lastWholeSecond);
        _timeLabel.AddThemeColorOverride("font_color", UiTheme.Ink);
        _timerBar.Reset();
    }

    public void SetScore(int score, float punch = 0f)
    {
        _scoreLabel.Text = $"점수 {score}";
        if (punch > 0)
            Ui.Punch(_scoreLabel, punch);
    }

    /// <summary>Call every frame while playing. Updates the bar and text; the last 5 seconds tick and turn orange.</summary>
    public void SetElapsed(float elapsed)
    {
        _timerBar.Remaining = _total - elapsed;
        var secondsLeft = RoundClock.SecondsLeft(elapsed, _total);
        if (secondsLeft == _lastWholeSecond)
            return;
        _lastWholeSecond = secondsLeft;
        _timeLabel.Text = TimeText(secondsLeft);
        if (secondsLeft is > 0 and <= 5)
        {
            // Last seconds: a tick and a pulse, in orange (excitement, not alarm)
            Audio.I.Tick(1.25f);
            _timeLabel.AddThemeColorOverride("font_color", UiTheme.Orange);
            Ui.Punch(_timeLabel, 0.3f);
        }
    }

    private static string TimeText(int seconds) => $"남은 시간 {seconds}초";

    /// <summary>A short line under the top bar for a difficulty step ("더 빨라졌어요!").</summary>
    public void ShowBanner(string text)
    {
        if (text.Length == 0)
            return;
        Audio.I.Whoosh();
        _banner.Text = text;
        _banner.PivotOffset = _banner.Size / 2;
        _banner.Modulate = Colors.White;
        _banner.Scale = new Vector2(0.5f, 0.5f);
        var tween = _banner.CreateTween();
        tween.TweenProperty(_banner, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(1.1f);
        tween.TweenProperty(_banner, "modulate:a", 0f, 0.3f);
    }

    /// <summary>Time's up: zeroes the clock, plays the time-up sound and shows a big "끝!", then calls <paramref name="then"/>.</summary>
    public void ShowTimeUp(Action then)
    {
        SetElapsed(_total);
        Audio.I.TimeUp();
        Audio.I.DuckMusic(-12f);

        var done = Ui.Label("끝!", 160, UiTheme.Yellow, 28, UiTheme.Ink);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        done.VerticalAlignment = VerticalAlignment.Center;
        done.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(done);
        done.PivotOffset = GetViewportRect().Size / 2;
        done.Scale = new Vector2(0.3f, 0.3f);
        var tween = done.CreateTween();
        tween.TweenProperty(done, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(0.9f);
        tween.TweenProperty(done, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Callable.From(() =>
        {
            done.QueueFree();
            then();
        }));
    }
}
