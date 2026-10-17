using System;
using Godot;
using ToyBox.Common;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// The HUD at the sides of the tall board (docs/games/12-swipe-brick.md 화면 구조): "그만하기" and the shots left on the
/// left; bricks broken, the ball count and the fast-forward button on the right (the buddy stands under them).
/// A turn game, so there is no clock. <see cref="ShowDone"/> plays the big "끝!".
/// </summary>
public partial class SwipeHud : Control
{
    public const float LeftWidth = 240f;
    public const float RightWidth = 300f;
    private const float Margin = 18f;
    private const float FastSize = 104f;

    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private Label _shotsLabel = null!;
    private Label _scoreLabel = null!;
    private Label _ballsLabel = null!;
    private TextureRect _ballsIcon = null!;
    private TextureButton _fast = null!;
    private Label _fastLabel = null!;
    private Tween? _fastPulse;
    private bool _fastOn;

    public event Action? FastPressed;

    public Control ScoreLabel => _scoreLabel;

    /// <summary>Where collected bubbles fly in the HUD (screen coordinates).</summary>
    public Vector2 BallsTarget => _ballsIcon.GetGlobalRect().GetCenter();

    public static SwipeHud Create()
    {
        var hud = new SwipeHud { MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsPreset(LayoutPreset.FullRect);
        hud.Build();
        return hud;
    }

    private static PanelContainer InfoPanel() => Ui.Panel(Colors.White with { A = 0.95f }, 12, 4);

    private void Build()
    {
        _left = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _left.AddThemeConstantOverride("separation", 14);
        AddChild(_left);
        _left.AddChild(Ui.Button("그만하기", () => QuitConfirm.Open(GetParent()), null, 24, null, new Vector2(LeftWidth - Margin * 2, 64)));

        var shotsPanel = InfoPanel();
        var shotsColumn = new VBoxContainer();
        var shotsCaption = Ui.Label("남은 공 쏘기", 24, UiTheme.InkSoft);
        shotsCaption.HorizontalAlignment = HorizontalAlignment.Center;
        shotsColumn.AddChild(shotsCaption);
        _shotsLabel = Ui.Label("", 48, UiTheme.Orange, 8, UiTheme.Ink);
        _shotsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        shotsColumn.AddChild(_shotsLabel);
        shotsPanel.AddChild(shotsColumn);
        _left.AddChild(shotsPanel);

        _right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _right.AddThemeConstantOverride("separation", 12);
        AddChild(_right);

        var scorePanel = InfoPanel();
        var scoreRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        scoreRow.AddThemeConstantOverride("separation", 8);
        scoreRow.AddChild(Ui.Icon("res://assets/sprites/brick/brick_1.png", 46));
        _scoreLabel = Ui.Label("", 34, UiTheme.Ink);
        scoreRow.AddChild(_scoreLabel);
        scorePanel.AddChild(scoreRow);
        _right.AddChild(scorePanel);

        var ballsPanel = InfoPanel();
        var ballsRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        ballsRow.AddThemeConstantOverride("separation", 8);
        _ballsIcon = Ui.Icon("res://assets/sprites/brick/ball.png", 40);
        ballsRow.AddChild(_ballsIcon);
        _ballsLabel = Ui.Label("", 34, UiTheme.Ink);
        ballsRow.AddChild(_ballsLabel);
        ballsPanel.AddChild(ballsRow);
        _right.AddChild(ballsPanel);

        var fastBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        fastBox.AddThemeConstantOverride("separation", 0);
        _fast = new TextureButton
        {
            TextureNormal = Ui.Tex("res://assets/sprites/ui/fast.png"),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(FastSize, FastSize),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
        };
        Ui.MakeSquishy(_fast);
        _fast.Pressed += () =>
        {
            if (_fast.Disabled || _fastOn)
                return;
            Audio.I.Click();
            FastPressed?.Invoke();
        };
        fastBox.AddChild(_fast);
        _fastLabel = Ui.Label("빨리 감기", 24, Colors.White, 8, UiTheme.Ink);
        _fastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        fastBox.AddChild(_fastLabel);
        _right.AddChild(fastBox);

        Resized += Place;
        SetFast(available: false, on: false);
    }

    public override void _Ready() => Place();

    private void Place()
    {
        var size = GetViewportRect().Size;
        _left.Position = new Vector2(Margin, Margin);
        _left.Size = new Vector2(LeftWidth - Margin * 2, 0);
        _right.Position = new Vector2(size.X - RightWidth + Margin, Margin);
        _right.Size = new Vector2(RightWidth - Margin * 2, 0);
    }

    public void SetShotsLeft(int shots, bool punch = false)
    {
        _shotsLabel.Text = $"{shots}번";
        if (punch)
            Ui.Punch(_shotsLabel, 0.3f);
    }

    public void SetScore(int bricks, float punch = 0f)
    {
        _scoreLabel.Text = $"벽돌 {bricks}개";
        if (punch > 0)
            Ui.Punch(_scoreLabel, punch);
    }

    public void SetBalls(int balls, bool punch = false)
    {
        _ballsLabel.Text = $"공 {balls}개";
        if (punch)
            Ui.Punch(_ballsLabel, 0.35f);
    }

    /// <summary>Fast-forward: usable only while balls fly; lit while it is on.</summary>
    public void SetFast(bool available, bool on)
    {
        _fastOn = on;
        _fast.Disabled = !available;
        _fast.Modulate = !available ? Colors.White with { A = 0.35f } : on ? new Color(1.25f, 1.15f, 0.8f) : Colors.White;
        _fastLabel.Text = on ? "빨리빨리!" : "빨리 감기";
        _fastLabel.Modulate = available ? Colors.White : Colors.White with { A = 0.5f };
        if (!available || on)
            StopFastPulse();
    }

    /// <summary>The button bounces to say "press me" (after 3 seconds of flight).</summary>
    public void PulseFast()
    {
        if (_fastPulse is not null || _fast.Disabled || _fastOn)
            return;
        _fast.PivotOffset = _fast.Size / 2;
        _fastPulse = _fast.CreateTween().SetLoops();
        _fastPulse.TweenProperty(_fast, "scale", new Vector2(1.15f, 1.15f), 0.3f).SetTrans(Tween.TransitionType.Sine);
        _fastPulse.TweenProperty(_fast, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Sine);
    }

    private void StopFastPulse()
    {
        _fastPulse?.Kill();
        _fastPulse = null;
        _fast.Scale = Vector2.One;
    }

    /// <summary>A big "끝!", then <paramref name="then"/>.</summary>
    public void ShowDone(Action then)
    {
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
