using System;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Match3;

/// <summary>
/// The round HUD laid out at the sides of the square board (docs/games/11-match3.md 화면 구조): "그만하기" on the
/// left, "과일 N개" and the remaining time (text + <see cref="TimerBar"/>) on the right. The clock behaves like the
/// shared <see cref="RoundHud"/> (last 5 seconds tick in orange, a big "끝!" at the end), which puts everything in one
/// top bar that would cover this board.
/// </summary>
public partial class Match3Hud : Control
{
    public const float LeftWidth = 200f;
    public const float RightWidth = 290f;
    private const float Margin = 18f;

    private float _total;
    private int _lastWholeSecond;
    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private Label _scoreLabel = null!;
    private TextureRect _scoreIcon = null!;
    private Label _timeLabel = null!;
    private TimerBar _timerBar = null!;

    /// <summary>Where collected fruit fly to (screen coordinates).</summary>
    public Vector2 ScoreTarget => _scoreIcon.GetGlobalRect().GetCenter();

    public Control ScoreLabel => _scoreLabel;

    public static Match3Hud Create(float totalSeconds)
    {
        var hud = new Match3Hud { _total = totalSeconds, MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsPreset(LayoutPreset.FullRect);
        hud.Build();
        return hud;
    }

    private void Build()
    {
        _left = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_left);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(GetParent()), null, 24, null, new Vector2(LeftWidth - Margin * 2, 64));
        _left.AddChild(quit);

        _right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _right.AddThemeConstantOverride("separation", 14);
        AddChild(_right);

        var scorePanel = Ui.Panel(Colors.White with { A = 0.95f }, 12, 4);
        var scoreRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        scoreRow.AddThemeConstantOverride("separation", 8);
        _scoreIcon = Ui.Icon("res://assets/sprites/match3/apple.png", 52);
        scoreRow.AddChild(_scoreIcon);
        _scoreLabel = Ui.Label("과일 0개", 38, UiTheme.Ink);
        scoreRow.AddChild(_scoreLabel);
        scorePanel.AddChild(scoreRow);
        _right.AddChild(scorePanel);

        var timePanel = Ui.Panel(Colors.White with { A = 0.95f }, 12, 4);
        var timeColumn = new VBoxContainer();
        timeColumn.AddThemeConstantOverride("separation", 4);
        _timeLabel = Ui.Label("", 30, UiTheme.Ink);
        _timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        timeColumn.AddChild(_timeLabel);
        _timerBar = TimerBar.Create(_total, new Vector2(RightWidth - Margin * 2 - 30, 24));
        timeColumn.AddChild(_timerBar);
        timePanel.AddChild(timeColumn);
        _right.AddChild(timePanel);

        Resized += Place;
        Reset();
    }

    public override void _Ready() => Place();

    private void Place()
    {
        var size = GetViewportRect().Size;
        _left.Position = new Vector2(Margin, Margin);
        _left.Size = new Vector2(LeftWidth - Margin * 2, 0);
        _right.Position = new Vector2(size.X - RightWidth + Margin * 0.5f, Margin);
        _right.Size = new Vector2(RightWidth - Margin * 1.5f, 0);
    }

    public void Reset()
    {
        SetScore(0);
        _lastWholeSecond = RoundClock.SecondsLeft(0, _total);
        _timeLabel.Text = TimeText(_lastWholeSecond);
        _timeLabel.AddThemeColorOverride("font_color", UiTheme.Ink);
        _timerBar.Reset();
    }

    public void SetScore(int fruit, float punch = 0f)
    {
        _scoreLabel.Text = $"과일 {fruit}개";
        if (punch > 0)
            Ui.Punch(_scoreLabel, punch);
    }

    /// <summary>Call every frame while playing. The last 5 seconds tick and turn orange (excitement, not alarm).</summary>
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
            Audio.I.Tick(1.25f);
            _timeLabel.AddThemeColorOverride("font_color", UiTheme.Orange);
            Ui.Punch(_timeLabel, 0.3f);
        }
    }

    private static string TimeText(int seconds) => $"남은 시간 {seconds}초";

    /// <summary>Time's up: zeroes the clock and shows a big "끝!", then calls <paramref name="then"/>.</summary>
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
