using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flight;

/// <summary>
/// 하늘 비행 모험 (docs/games/18-sky-flight.md).
/// Flow: Setup (꾸미기: 난이도, 속도) → Countdown → Playing (bubbles fire by themselves; friends from the appearance
/// table; then "대장 먹구름이 나타났어요!" and the boss) → Ending (the boss smiles: fanfare, rainbow, confetti,
/// "모두 깨끗해졌어요!") → Result (shared ResultPanel, one board per level).
/// Drag anywhere to move the plane by the finger's movement; arrows / WASD move it, SHIFT slowly; B / X or the
/// button at the bottom right lights a firework. The rules and every object live in <see cref="FlightWorld"/>; this
/// node reads input, steps it, turns its events into sound and effects, and draws it with three nodes.
/// </summary>
public partial class FlightGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Ending, Result }

    private const string GameId = "flight";
    private const string SettingsSection = "flight";
    private const float MaxSubstep = 1f / 120f;
    private const float ScrollSpeed = 0.3f;   // Forest band, screen heights per second at the "보통" speed
    private const float AutoCarelessEvery = 28f;

    private static readonly IReadOnlyList<Choice> LevelChoices = new[]
    {
        new Choice("쉬움"),
        new Choice("보통"),
        new Choice("어려움"),
    };

    private readonly Random _rng = new();
    private readonly FlightBot _bot = new();

    private State _state;
    private FlightWorld? _world;
    private FlightLevel _level;
    private StartSpeed _speed;

    private FlightBackdrop _backdrop = null!;
    private Node2D _worldNode = null!;
    private ScreenShake _shake = null!;
    private FlightField _field = null!;
    private PlaneSprite _plane = null!;
    private FlightBullets _bullets = null!;
    private Node2D _fxLayer = null!;
    private Texture2D _starTexture = null!;

    private CanvasLayer _ui = null!;
    private Control? _setup;
    private Control _play = null!;
    private FlightBar _bar = null!;
    private Label _starLabel = null!;
    private Button _bombButton = null!;
    private Label _banner = null!;
    private Label _bigBanner = null!;
    private ColorRect _flash = null!;
    private Tween? _bannerTween;
    private Tween? _planeTween;

    private int _roundToken;
    private int _touchIndex = -1;
    private Vector2 _lastPlane;
    private float _scrollFactor = 1f;
    private int _shownStars = -1;
    private int _shownBombs = -1;
    private int _starStreak;
    private float _starSoundGap;
    private float _enemyHitGap;

    // Autoplay
    private float _autoCareless;
    private bool _autoBombUsed;

    private Vector2 ScreenSize => GetViewportRect().Size;
    private float H => ScreenSize.Y;
    private float Aspect => ScreenSize.X / ScreenSize.Y;
    private Vector2 SetupPlane => new(FlightRules.PlaneStart(Aspect).X * H, H * 0.5f);

    private Vector2 Px(System.Numerics.Vector2 v) => new(v.X * H, v.Y * H);

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _backdrop = new FlightBackdrop();
        AddChild(_backdrop);

        _worldNode = new Node2D { Name = "World" };
        AddChild(_worldNode);
        _shake = ScreenShake.Create(_worldNode);
        AddChild(_shake);

        // Back to front: friends, boss, bubbles and stars; the plane; enemy bubbles over it; effects
        _field = new FlightField();
        _worldNode.AddChild(_field);
        _plane = PlaneSprite.Create(FlightRules.PlaneWidth * H);
        _worldNode.AddChild(_plane);
        _bullets = new FlightBullets();
        _worldNode.AddChild(_bullets);
        _fxLayer = new Node2D();
        _worldNode.AddChild(_fxLayer);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _level = (FlightLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)FlightLevel.Easy), 0, 2);
        _speed = (StartSpeed)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "speed", (int)StartSpeed.Normal), 0, 2);

        BuildPlayUi();
        _plane.Position = SetupPlane;
        _lastPlane = _plane.Position;
        GetViewport().SizeChanged += Layout;
        // A whole round is about two and a half minutes: play it at triple speed in a headless smoke test (8000 frames)
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 3.0;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    private void Layout()
    {
        _plane.SetWidth(FlightRules.PlaneWidth * H);
        if (_world is { } w)
            w.Width = Aspect;
        else
            _plane.Position = SetupPlane;
    }

    // ---- Play UI: quit, road / mood bar, stars, firework button, banners ----

    private void BuildPlayUi()
    {
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_flash);

        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_play);

        var frame = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            frame.AddThemeConstantOverride($"margin_{side}", 16);
        _play.AddChild(frame);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(column);

        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 16);
        column.AddChild(top);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _bar = new FlightBar { CustomMinimumSize = new Vector2(440, 52), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        top.AddChild(_bar);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var starPanel = Ui.Panel(Colors.White with { A = 0.92f }, 10, 4);
        starPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var starRow = new HBoxContainer();
        starRow.AddThemeConstantOverride("separation", 6);
        starRow.AddChild(Ui.Icon("res://assets/sprites/ui/star.png", 40));
        _starLabel = Ui.Label("별 0", 32, UiTheme.Ink);
        _starLabel.CustomMinimumSize = new Vector2(96, 0);
        _starLabel.HorizontalAlignment = HorizontalAlignment.Center;
        starRow.AddChild(_starLabel);
        starPanel.AddChild(starRow);
        top.AddChild(starPanel);

        _banner = Ui.Label("", 44, Colors.White, 14, UiTheme.Blue);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.Modulate = Colors.Transparent;
        column.AddChild(Ui.Spacer(20));
        column.AddChild(_banner);

        // Firework button: bottom right, out of the plane's area
        _bombButton = Ui.Button("2", UseBomb, null, 34, Ui.Tex("res://assets/sprites/flight/item_bomb.png"), new Vector2(150, 96));
        _bombButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _bombButton.GrowHorizontal = Control.GrowDirection.Begin;
        _bombButton.GrowVertical = Control.GrowDirection.Begin;
        _bombButton.OffsetRight = -20;
        _bombButton.OffsetBottom = -20;
        _bombButton.OffsetLeft = -170;
        _bombButton.OffsetTop = -116;
        _bombButton.AddThemeConstantOverride("icon_max_width", 64);
        _play.AddChild(_bombButton);

        _bigBanner = Ui.Label("", 72, UiTheme.Yellow, 22, UiTheme.Ink);
        _bigBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _bigBanner.VerticalAlignment = VerticalAlignment.Center;
        _bigBanner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bigBanner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bigBanner.Visible = false;
        _play.AddChild(_bigBanner);
    }

    private void ResetHud()
    {
        _shownStars = -1;
        _shownBombs = -1;
        _bar.MoodMode = false;
        _bar.Progress = 0;
        _bar.ResetMood();
        _banner.Modulate = Colors.Transparent;
        _bigBanner.Visible = false;
        UpdateHud();
    }

    private void UpdateHud()
    {
        if (_world is not { } w)
            return;
        _bar.Progress = w.Progress;
        if (_bar.MoodMode)
            _bar.Mood = w.Boss.MoodFraction;
        if (w.Stars != _shownStars)
        {
            var punch = _shownStars >= 0;
            _shownStars = w.Stars;
            _starLabel.Text = $"별 {w.Stars}";
            if (punch)
                Ui.Punch(_starLabel, 0.2f);
        }
        if (w.Bombs != _shownBombs)
        {
            _shownBombs = w.Bombs;
            _bombButton.Text = $"{w.Bombs}";
            _bombButton.Modulate = w.Bombs > 0 ? Colors.White : new Color(1, 1, 1, 0.5f);
        }
    }

    /// <summary>A short message under the top bar ("방울이 세졌어요!").</summary>
    private void ShowBanner(string text, Color outline)
    {
        _bannerTween?.Kill();
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_outline_color", outline);
        _banner.PivotOffset = _banner.Size / 2;
        _banner.Modulate = Colors.White;
        _banner.Scale = new Vector2(0.5f, 0.5f);
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bannerTween.TweenInterval(1.3f);
        _bannerTween.TweenProperty(_banner, "modulate:a", 0f, 0.3f);
    }

    private void ShowBigBanner(string text)
    {
        _bigBanner.Text = text;
        _bigBanner.Visible = true;
        _bigBanner.Modulate = Colors.White;
        _bigBanner.PivotOffset = _bigBanner.Size / 2;
        _bigBanner.Scale = new Vector2(0.3f, 0.3f);
        _bigBanner.CreateTween().TweenProperty(_bigBanner, "scale", Vector2.One, 0.4f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void Flash(float alpha, float time)
    {
        _flash.Color = new Color(1, 1, 1, alpha);
        _flash.CreateTween().TweenProperty(_flash, "color:a", 0f, time);
    }

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _play.Visible = false;
        _world = null;
        _field.World = null;
        _bullets.World = null;
        _scrollFactor = 0.5f;
        Audio.I.PlayMusic(Music.Menu);
        MovePlane(SetupPlane);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/sky_flight.png", 64));
        titleRow.AddChild(Ui.Label("하늘 비행 모험", 42, UiTheme.Blue, 12, UiTheme.Ink));
        body.AddChild(titleRow);
        var how = Ui.Label("끌어서 움직여요! 방울은 저절로 나가요", 24, UiTheme.InkSoft);
        how.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(how);

        var levelRow = ChoiceRow.Create("난이도", LevelChoices, (int)_level, 130);
        levelRow.Selected += i =>
        {
            _level = (FlightLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
        };
        body.AddChild(levelRow);

        var speedRow = ChoiceRow.Create("속도", Choice.Speeds, (int)_speed);
        speedRow.Selected += i =>
        {
            _speed = (StartSpeed)i;
            App.I.SetSetting(SettingsSection, "speed", i);
        };
        body.AddChild(speedRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 76)));
        buttons.AddChild(Ui.Button("시작!", StartRound, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        // Right of the plane, which keeps flying on the left
        var holder = new MarginContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddThemeConstantOverride("margin_left", (int)(ScreenSize.X * 0.30f));
        holder.AddThemeConstantOverride("margin_right", 24);
        var center = new CenterContainer();
        center.AddChild(panel);
        holder.AddChild(center);
        _setup = holder;
        _ui.AddChild(holder);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();

        if (App.I.Autoplay)
        {
            var token = _roundToken;
            GetTree().CreateTimer(1.4f).Timeout += () =>
            {
                if (_state == State.Setup && token == _roundToken)
                    StartRound();
            };
        }
    }

    private void MovePlane(Vector2 to)
    {
        _planeTween?.Kill();
        _planeTween = CreateTween();
        _planeTween.TweenProperty(_plane, "position", to, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _roundToken++;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        foreach (var child in _fxLayer.GetChildren())
            child.QueueFree();

        _world = new FlightWorld(_level, _speed, Aspect, _rng.Next());
        _field.World = _world;
        _bullets.World = _world;
        _touchIndex = -1;
        _scrollFactor = 1f;
        _starStreak = 0;
        _autoCareless = AutoCarelessEvery;
        _autoBombUsed = false;
        _bot.Careless = 0;
        ResetHud();
        _play.Visible = true;
        MovePlane(Px(_world.Plane));
        _state = State.Countdown;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the boss's tempo

        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown)
                return;
            _state = State.Playing;
            ShowBanner("장난꾸러기들을 웃게 해 줘요!", UiTheme.Blue);
        };
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _starSoundGap = Mathf.Max(0, _starSoundGap - dt);
        _enemyHitGap = Mathf.Max(0, _enemyHitGap - dt);
        _backdrop.Scroll(ScrollSpeed * RoundClock.SpeedScale(_speed) * H * dt * _scrollFactor);

        var slow = !App.I.Autoplay && Input.IsPhysicalKeyPressed(Key.Shift);
        if (_world is { } w && _state is State.Playing or State.Ending)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxSubstep));
            var h = dt / steps;
            for (var i = 0; i < steps && _world == w; i++)
            {
                var input = App.I.Autoplay ? AutoPlay(w, h) : new FlightInput { Move = KeyMove(), Slow = slow };
                w.Step(h, input);
                HandleEvents(w);
            }
        }

        var world = _world;
        if (world is not null && _state is not State.Setup && _planeTween?.IsRunning() != true)
            _plane.Position = Px(world.Plane);
        var climb = dt > 0 ? (_plane.Position.Y - _lastPlane.Y) / H / dt : 0f;
        _lastPlane = _plane.Position;
        _plane.Step(dt, climb, world is { Invincible: > 0 }, world is { Surprised: > 0 }, world is { Shield: true });
        _bullets.ShowDot = slow && _state == State.Playing;
        UpdateHud();
    }

    private static System.Numerics.Vector2 KeyMove()
    {
        var x = 0f;
        var y = 0f;
        if (Input.IsPhysicalKeyPressed(Key.Left) || Input.IsPhysicalKeyPressed(Key.A))
            x -= 1;
        if (Input.IsPhysicalKeyPressed(Key.Right) || Input.IsPhysicalKeyPressed(Key.D))
            x += 1;
        if (Input.IsPhysicalKeyPressed(Key.Up) || Input.IsPhysicalKeyPressed(Key.W))
            y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.Down) || Input.IsPhysicalKeyPressed(Key.S))
            y += 1;
        return new System.Numerics.Vector2(x, y);
    }

    // ---- Touch and keys ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (App.I.Autoplay || _world is not { } w || _state is not (State.Playing or State.Countdown))
            return;
        // Mouse input arrives as touches too (emulate_touch_from_mouse), so touch alone covers PC and phones
        switch (e)
        {
            case InputEventScreenTouch touch:
                if (touch.Pressed && _touchIndex < 0)
                    _touchIndex = touch.Index;
                else if (!touch.Pressed && touch.Index == _touchIndex)
                    _touchIndex = -1;
                break;
            case InputEventScreenDrag drag when drag.Index == _touchIndex && _state == State.Playing:
                // The plane moves as much as the finger did (not to the finger), so the finger never hides it
                w.MovePlane(new System.Numerics.Vector2(drag.Relative.X / H, drag.Relative.Y / H));
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key when key.PhysicalKeycode is Key.B or Key.X:
                UseBomb();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void UseBomb()
    {
        if (_state != State.Playing || _world is not { } w)
            return;
        if (!w.UseBomb())
        {
            if (w.Bombs == 0)
                Ui.Wiggle(_bombButton);
            return;
        }
        HandleEvents(w);
    }

    // ---- Events → sound and effects ----

    private void HandleEvents(FlightWorld w)
    {
        foreach (var e in w.Events)
        {
            var at = Px(e.Pos);
            switch (e.Kind)
            {
                case SkyEventKind.EnemyHit:
                    // Most frequent after firing: tiny and quiet
                    if (_enemyHitGap <= 0)
                    {
                        _enemyHitGap = 0.12f;
                        Audio.I.Plop();
                    }
                    _fxLayer.AddChild(Burst.Create(at, new Color(1, 1, 1, 0.8f), 3, 140f, 4f, 0f, 0.25f, _rng));
                    break;
                case SkyEventKind.EnemyHappy:
                    FriendSmiles(at, (FlightEnemyKind)e.Value);
                    break;
                case SkyEventKind.BulletPopped:
                    _bullets.AddPop(at, H * 0.025f);
                    break;
                case SkyEventKind.PlaneSurprised:
                    Audio.I.Bonk();
                    App.Vibrate(30);
                    _shake.Add(3f);
                    _plane.Surprise();
                    _fxLayer.AddChild(new FloatingText
                    {
                        Text = "앗!",
                        Color = UiTheme.Yellow,
                        FontSize = 48,
                        Position = at + new Vector2(0, -H * 0.1f),
                    });
                    break;
                case SkyEventKind.ShieldPopped:
                    Audio.I.Pop(4);
                    _bullets.AddPop(at, H * 0.08f);
                    _fxLayer.AddChild(new FloatingText
                    {
                        Text = "방울이 지켜 줬어요!",
                        Color = UiTheme.Pink,
                        FontSize = 34,
                        Position = at + new Vector2(H * 0.15f, -H * 0.1f),
                    });
                    break;
                case SkyEventKind.ItemTaken:
                    ItemTaken((FlightItemKind)e.Value, e.Extra, at);
                    break;
                case SkyEventKind.ItemSpare:
                    Audio.I.Coin();
                    _fxLayer.AddChild(new FloatingText { Text = $"별 +{e.Extra}", Color = UiTheme.Yellow, FontSize = 36, Position = at });
                    break;
                case SkyEventKind.StarTaken:
                    if (_starSoundGap <= 0)
                    {
                        _starSoundGap = 0.06f;
                        Audio.I.Catch(_starStreak++ % 8, -9f);
                    }
                    break;
                case SkyEventKind.BombUsed:
                    Fireworks();
                    break;
                case SkyEventKind.BossAppears:
                    BossAppears();
                    break;
                case SkyEventKind.BossPhase when e.Value > 1:
                    Audio.I.Whoosh();
                    ShowBanner(e.Value == 2 ? "대장이 볼을 부풀렸어요!" : "꼬마 먹구름을 불렀어요!", UiTheme.Purple);
                    break;
                case SkyEventKind.BossCharge:
                    Audio.I.Tick(0.7f);
                    break;
                case SkyEventKind.BossHit:
                    Audio.I.Plop();
                    _fxLayer.AddChild(Burst.Create(at, new Color(1, 1, 1, 0.8f), 3, 160f, 4f, 0f, 0.25f, _rng));
                    break;
                case SkyEventKind.BossSummon:
                    Audio.I.Whoosh();
                    break;
                case SkyEventKind.BossHappy:
                    BossSmiles(at, e.Value == 1);
                    break;
                case SkyEventKind.Done:
                    Cleared();
                    break;
            }
        }
        w.ClearEvents();
    }

    /// <summary>A friend smiles: a puff of white steam, a "보잉", and its stars spill out (they fly to the plane).</summary>
    private void FriendSmiles(Vector2 at, FlightEnemyKind kind)
    {
        Audio.I.Jump();
        var big = kind == FlightEnemyKind.BigCloud;
        _fxLayer.AddChild(Burst.Create(at, new Color(1, 1, 1, 0.9f), big ? 12 : 7, big ? 320f : 220f, big ? 10f : 7f,
            H * (big ? 0.12f : 0.07f), 0.45f, _rng));
        if (big)
            _shake.Add(2f);
    }

    private void ItemTaken(FlightItemKind kind, int extra, Vector2 at)
    {
        Audio.I.Coin();
        _plane.Cheer();
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, at, 5, 260f, 24f, 0.6f, _rng, 0.6f));
        if (_state != State.Playing)
            return; // The boss's smile keeps the banner
        switch (kind)
        {
            case FlightItemKind.Power:
                ShowBanner(extra >= FlightRules.MaxPower ? "방울이 최고로 세졌어요!" : "방울이 세졌어요!", UiTheme.Blue);
                break;
            case FlightItemKind.Bomb:
                ShowBanner("폭죽을 얻었어요!", UiTheme.Orange);
                Ui.Punch(_bombButton, 0.25f);
                break;
            case FlightItemKind.Heart:
                ShowBanner("보호 방울이 생겼어요!", UiTheme.Pink);
                break;
            default:
                ShowBanner("사탕! 별 +5", UiTheme.Pink);
                break;
        }
    }

    /// <summary>Fireworks: the whole screen sparkles, enemy bubbles turn into stars, a big "펑-반짝".</summary>
    private void Fireworks()
    {
        Audio.I.Pop(6);
        Audio.I.Cheer();
        Audio.I.Whoosh();
        App.Vibrate(40);
        _shake.Add(6f);
        Flash(0.6f, 0.5f);
        var colors = new[] { UiTheme.Pink, UiTheme.Yellow, UiTheme.Blue, UiTheme.Green, UiTheme.Purple };
        for (var i = 0; i < 5; i++)
        {
            var at = new Vector2(ScreenSize.X * (0.35f + 0.15f * i), H * (0.3f + 0.2f * (i % 2)));
            _fxLayer.AddChild(Burst.Create(at, colors[i], 16, 420f, 8f, 0f, 0.8f, _rng));
            _fxLayer.AddChild(SpriteBurst.Create(_starTexture, at, 4, 360f, 26f, 0.8f, _rng, 0.3f));
        }
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, new Vector2(ScreenSize.X * 0.6f, H * 0.45f), 12, 700f, 34f, 1.0f, _rng, 0.5f));
    }

    private void BossAppears()
    {
        Audio.I.Whoosh();
        Audio.I.QuestionChime();
        Audio.I.SetMusicTempo(1.12f); // The music speeds up for the boss
        _shake.Add(4f);
        _bar.MoodMode = true;
        _bar.ResetMood();
        Ui.Punch(_bar, 0.15f);
        ShowBanner("대장 먹구름이 나타났어요!", UiTheme.Purple);
    }

    /// <summary>The boss smiles (climax): fanfare, a rainbow over it, confetti, every friend smiles too.</summary>
    private void BossSmiles(Vector2 at, bool tired)
    {
        _state = State.Ending;
        Audio.I.DuckMusic(-10f);
        Audio.I.Fanfare();
        Audio.I.Cheer();
        App.Vibrate(60);
        _shake.Add(5f);
        Flash(0.5f, 0.6f);
        _bar.Mood = 0;
        _fxLayer.AddChild(Rainbow.Create(at + new Vector2(0, H * 0.06f), H * 0.42f));
        _fxLayer.AddChild(Burst.Create(at, Colors.White, 20, 520f, 12f, H * 0.3f, 0.9f, _rng));
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, at, 14, 700f, 36f, 1.2f, _rng, 0.6f));
        _ui.AddChild(Confetti.Create(ScreenSize, 90));
        _plane.Cheer();
        ShowBanner(tired ? "대장 먹구름이 웃었어요!" : "대장 먹구름을 웃게 했어요!", UiTheme.Green);
    }

    /// <summary>All the stars are in: "모두 깨끗해졌어요!", then the result.</summary>
    private void Cleared()
    {
        var token = ++_roundToken;
        _plane.Cheer();
        ShowBigBanner("모두 깨끗해졌어요!");
        var slow = CreateTween();
        slow.TweenMethod(Callable.From<float>(v => _scrollFactor = v), 1f, 0.3f, 1.2f);
        GetTree().CreateTimer(1.8f, processAlways: false).Timeout += () =>
        {
            if (token == _roundToken)
                ShowResult();
        };
    }

    private void ShowResult()
    {
        if (_world is not { } w)
            return;
        _state = State.Result;
        var tween = _bigBanner.CreateTween();
        tween.TweenProperty(_bigBanner, "modulate:a", 0f, 0.2f);
        tween.TweenCallback(Callable.From(() => _bigBanner.Visible = false));

        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = FlightRules.VariantId(_level),
            Title = "모두 깨끗해졌어요!",
            Caption = "모은 별",
            Value = w.Stars,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Detail = $"웃게 한 친구 {w.Friends}명",
            Entry = new ScoreEntry("", w.Stars, (long)(w.Elapsed * 1000), App.Now()),
            Recordable = w.Stars > 0,
            NotRecordableMessage = "다음엔 별을 모아 보자!",
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("꾸미기", UiTheme.Blue, () =>
                {
                    ResultPanel.CloseAll(_ui);
                    ShowSetup();
                }),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// The shared <see cref="FlightBot"/> flies (lines up with friends, takes items, dodges bubbles). Every ~28 s it
    /// stops dodging for a moment to get bumped on purpose, and it lights one firework when bubbles pile up.
    /// </summary>
    private FlightInput AutoPlay(FlightWorld w, float dt)
    {
        _autoCareless -= dt;
        if (_autoCareless <= 0)
        {
            _autoCareless = AutoCarelessEvery;
            _bot.Careless = 2.5f;
        }
        if (!_autoBombUsed && _state == State.Playing && FlightBot.WantsBomb(w, _level == FlightLevel.Easy ? 8 : 12))
        {
            _autoBombUsed = true;
            UseBomb();
        }
        return _bot.Decide(w, dt);
    }
}
