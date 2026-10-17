using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Blocks;

/// <summary>The big touch buttons beside the board.</summary>
public enum PadButton { Left, Down, Right, Hold, Drop, Rotate }

/// <summary>
/// The HUD at the sides of the tall board (docs/games/14-blocks.md 화면 구조). Left: "그만하기", the held piece and the
/// ◀ ▼ ▶ buttons. Right: the next three pieces, "줄 N개", the time left (text + <see cref="TimerBar"/>) and the
/// hold / drop / turn buttons. The buttons are pictures only: the game hit-tests touches itself (<see cref="ButtonAt"/>)
/// so several fingers can hold buttons at once (a held ◀ while tapping turn). The clock behaves like the shared
/// <see cref="RoundHud"/> (last 5 seconds tick in orange, a big "끝!").
/// </summary>
public partial class BlocksHud : Control
{
    private const float Margin = 18f;
    private const float Gap = 16f;
    private const float InfoWidth = 270f;
    private const float NextWidth = 132f;

    private float _total;
    private int _lastWholeSecond;
    private Button _quit = null!;
    private PanelContainer _holdPanel = null!;
    private PiecePreview _hold = null!;
    private PanelContainer _nextPanel = null!;
    private readonly List<PiecePreview> _next = new();
    private VBoxContainer _info = null!;
    private Label _linesLabel = null!;
    private Label _timeLabel = null!;
    private TimerBar _timerBar = null!;
    private readonly Dictionary<PadButton, TextureRect> _buttons = new();

    public Control LinesLabel => _linesLabel;

    /// <summary>Where the buddy stands (feet), above the right-hand buttons.</summary>
    public Vector2 BuddySpot { get; private set; }

    public static BlocksHud Create(float totalSeconds)
    {
        var hud = new BlocksHud { _total = totalSeconds, MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsPreset(LayoutPreset.FullRect);
        hud.Build();
        return hud;
    }

    private static PanelContainer InfoPanel() => Ui.Panel(Colors.White with { A = 0.95f }, 12, 4);

    private static Label Caption(string text)
    {
        var label = Ui.Label(text, 24, UiTheme.InkSoft);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    private void Build()
    {
        _quit = Ui.Button("그만하기", () => QuitConfirm.Open(GetParent()), null, 24, null, new Vector2(170, 60));
        AddChild(_quit);

        _holdPanel = InfoPanel();
        var holdColumn = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        holdColumn.AddThemeConstantOverride("separation", 2);
        holdColumn.AddChild(Caption("보관"));
        _hold = PiecePreview.Create(new Vector2(110, 64));
        holdColumn.AddChild(_hold);
        _holdPanel.AddChild(holdColumn);
        AddChild(_holdPanel);

        _nextPanel = InfoPanel();
        var nextColumn = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        nextColumn.AddThemeConstantOverride("separation", 8);
        nextColumn.AddChild(Caption("다음"));
        for (var i = 0; i < 3; i++)
        {
            var preview = PiecePreview.Create(i == 0 ? new Vector2(NextWidth - 24, 70) : new Vector2(NextWidth - 24, 52));
            if (i > 0)
                preview.Modulate = Colors.White with { A = 0.8f };
            _next.Add(preview);
            nextColumn.AddChild(preview);
        }
        _nextPanel.AddChild(nextColumn);
        AddChild(_nextPanel);

        _info = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _info.AddThemeConstantOverride("separation", 12);
        AddChild(_info);
        var linesPanel = InfoPanel();
        var linesRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        linesRow.AddThemeConstantOverride("separation", 8);
        linesRow.AddChild(Ui.Icon("res://assets/sprites/menu/blocks.png", 48));
        _linesLabel = Ui.Label("줄 0개", 36, UiTheme.Ink);
        linesRow.AddChild(_linesLabel);
        linesPanel.AddChild(linesRow);
        _info.AddChild(linesPanel);

        var timePanel = InfoPanel();
        var timeColumn = new VBoxContainer();
        timeColumn.AddThemeConstantOverride("separation", 4);
        _timeLabel = Ui.Label("", 26, UiTheme.Ink);
        _timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        timeColumn.AddChild(_timeLabel);
        _timerBar = TimerBar.Create(_total, new Vector2(InfoWidth - 34, 22));
        timeColumn.AddChild(_timerBar);
        timePanel.AddChild(timeColumn);
        _info.AddChild(timePanel);

        foreach (var (button, picture) in new[]
                 {
                     (PadButton.Left, "btn_left"), (PadButton.Down, "btn_down"), (PadButton.Right, "btn_right"),
                     (PadButton.Hold, "btn_hold"), (PadButton.Drop, "btn_drop"), (PadButton.Rotate, "btn_rotate"),
                 })
        {
            var rect = new TextureRect
            {
                Texture = Ui.Tex($"res://assets/sprites/ui/{picture}.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _buttons[button] = rect;
            AddChild(rect);
        }
        Reset();
    }

    /// <summary>Lays everything out around the board's frame (screen rectangle).</summary>
    public void Place(Rect2 frame)
    {
        var screen = GetViewportRect().Size;
        var leftEnd = frame.Position.X - Gap;
        var rightStart = frame.End.X + Gap;
        var rightEnd = screen.X - Margin;
        var bottom = screen.Y - Margin;

        _quit.Position = new Vector2(Margin, Margin);
        _holdPanel.Size = Vector2.Zero;
        var holdSize = _holdPanel.GetCombinedMinimumSize();
        _holdPanel.Position = new Vector2(leftEnd - holdSize.X, Margin);

        _nextPanel.Size = Vector2.Zero;
        _nextPanel.Position = new Vector2(rightStart, Margin);
        _info.Size = new Vector2(InfoWidth, 0);
        _info.Position = new Vector2(rightEnd - InfoWidth, Margin);

        // Buttons: as big as the side allows (about 100 px at 1280x720), along the bottom
        var leftWidth = leftEnd - Margin;
        var rightWidth = rightEnd - rightStart;
        var size = Mathf.Clamp(Mathf.Min(leftWidth, rightWidth) / 3.4f, 72f, 118f);
        var gap = size * 0.14f;
        var row = size * 3 + gap * 2;
        var leftX = Margin + (leftWidth - row) / 2;
        PlaceButton(PadButton.Left, new Vector2(leftX, bottom - size), size);
        PlaceButton(PadButton.Down, new Vector2(leftX + size + gap, bottom - size), size);
        PlaceButton(PadButton.Right, new Vector2(leftX + (size + gap) * 2, bottom - size), size);
        var rightX = rightStart + (rightWidth - row) / 2;
        PlaceButton(PadButton.Hold, new Vector2(rightX, bottom - size * 0.86f), size * 0.86f);
        PlaceButton(PadButton.Drop, new Vector2(rightX + size + gap, bottom - size), size);
        // The turn button is the one used most: a little bigger, nearest the thumb
        var rotate = size * 1.1f;
        PlaceButton(PadButton.Rotate, new Vector2(rightX + (size + gap) * 2 - (rotate - size) / 2, bottom - rotate), rotate);

        BuddySpot = new Vector2(rightEnd - InfoWidth / 2, bottom - rotate - 8);
    }

    private void PlaceButton(PadButton button, Vector2 position, float size)
    {
        var rect = _buttons[button];
        rect.Position = position;
        rect.Size = new Vector2(size, size);
        rect.PivotOffset = rect.Size / 2;
    }

    /// <summary>The button under a screen point (a little larger than the picture, it is round and for small fingers).</summary>
    public PadButton? ButtonAt(Vector2 point)
    {
        if (!Visible)
            return null;
        PadButton? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (button, rect) in _buttons)
        {
            var center = rect.Position + rect.Size / 2;
            var distance = point.DistanceTo(center);
            if (distance <= rect.Size.X * 0.58f && distance < bestDistance)
            {
                best = button;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Held buttons look pressed in.</summary>
    public void SetPressed(PadButton button, bool pressed)
    {
        var rect = _buttons[button];
        rect.Scale = pressed ? new Vector2(0.88f, 0.88f) : Vector2.One;
        rect.Modulate = pressed ? new Color(0.92f, 0.92f, 0.92f) : Colors.White;
    }

    /// <summary>A quick press-and-spring for what a key or autoplay just did, so all inputs feel like one control.</summary>
    public void Flash(PadButton button)
    {
        var rect = _buttons[button];
        rect.Scale = new Vector2(0.88f, 0.88f);
        rect.CreateTween().TweenProperty(rect, "scale", Vector2.One, 0.2f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public void SetButtonsVisible(bool visible)
    {
        foreach (var rect in _buttons.Values)
            rect.Visible = visible;
    }

    public void SetNext(IReadOnlyList<PieceKind> next)
    {
        for (var i = 0; i < _next.Count; i++)
            _next[i].Show(i < next.Count ? next[i] : null);
    }

    public void SetHold(PieceKind? kind, bool used) => _hold.Show(kind, used);

    public void WiggleHold() => Ui.Wiggle(_holdPanel);

    public void Reset()
    {
        SetLines(0);
        _hold.Show(null);
        _lastWholeSecond = RoundClock.SecondsLeft(0, _total);
        _timeLabel.Text = TimeText(_lastWholeSecond);
        _timeLabel.AddThemeColorOverride("font_color", UiTheme.Ink);
        _timerBar.Reset();
        foreach (var button in _buttons.Keys)
            SetPressed(button, false);
    }

    public void SetLines(int lines, float punch = 0f)
    {
        _linesLabel.Text = $"줄 {lines}개";
        if (punch > 0)
            Ui.Punch(_linesLabel, punch);
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

    private static string TimeText(int seconds) =>
        seconds >= 60 ? $"남은 시간 {seconds / 60}분 {seconds % 60:00}초" : $"남은 시간 {seconds}초";

    /// <summary>Time's up: zeroes the clock and shows a big "끝!", then calls <paramref name="then"/>.</summary>
    public void ShowTimeUp(Action then)
    {
        SetElapsed(_total);
        Audio.I.TimeUp();
        Audio.I.DuckMusic(-12f);

        var done = Ui.Label("끝!", 160, UiTheme.Yellow, 28, UiTheme.Ink);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        done.VerticalAlignment = VerticalAlignment.Center;
        done.MouseFilter = MouseFilterEnum.Ignore;
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
