using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flappy;

/// <summary>
/// 아기새 날기 (docs/games/09-flappy.md).
/// Flow: Setup (꾸미기: 난이도, 배경, 속도) → Countdown (the bird bobs in place) → Ready (waits for the first press) →
/// Playing (each press flaps; tree pillars come from the right; +1 for each one passed) → Crashed (dizzy, spins down
/// to the grass, "앗! 다시 해볼까요?") → Result (shared ResultPanel, one board per level).
/// SPACE / ↑ / W, a click or a tap anywhere flaps. The ceiling and the ground never end the run; only pillars do.
/// All nodes are built in code.
/// </summary>
public partial class FlappyGame : Node2D
{
    private enum State { Setup, Countdown, Ready, Playing, Crashed, Result }

    private const string GameId = "flappy";
    private const string SettingsSection = "flappy";
    private const float BirdXRatio = 0.3f;        // The bird flies at 30% of the screen width
    private const float SpawnAhead = 0.15f;       // Pillars appear this far right of the screen edge, screen heights
    private const float MaxSubstep = 1f / 120f;   // Physics steps per frame are at most this long

    private static readonly string[] SpeedBanners = { "", "빨라져요!", "더 빨라져요!", "최고 속도!" };
    private static readonly string[] Cheers = { "최고야!", "대단해!", "멋져요!", "잘한다!", "우와!" };
    private static readonly IReadOnlyList<Choice> LevelChoices = new[]
    {
        new Choice("쉬움"),
        new Choice("보통"),
        new Choice("어려움"),
    };

    private readonly Random _rng = new();
    private readonly List<Pillar> _pillars = new();
    private readonly FlappyBird _flight = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _pillarLayer = null!;
    private FlappyGround _ground = null!;
    private Bird _bird = null!;
    private Node2D _fxLayer = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private Control? _setup;
    private Control _play = null!;
    private Label _scoreLabel = null!;
    private Label _bestLabel = null!;
    private Label _speedBanner = null!;
    private Label _bigBanner = null!;
    private Label _hint = null!;
    private Texture2D _starTexture = null!;
    private Tween? _birdMove;
    private Tween? _hintTween;

    private FlappyLevel _level;
    private StartSpeed _speed;
    private float _elapsed;       // Seconds since the first flap
    private int _score;
    private int _stepIndex;
    private float _sinceStep;
    private FlappyGap? _queuedGap;  // The gap of the next pillar to place
    private float _nextLeft;      // Its left edge in screen heights
    private float _touching;      // Seconds the bird has been touching a pillar (crash grace)
    private float _worldFactor = 1f;
    private int _roundToken;
    private int _bounceCount;

    // Autoplay
    private int _autoCrashAt;
    private float _autoSinceFlap;

    private Vector2 ScreenSize => GetViewportRect().Size;
    private float H => ScreenSize.Y;
    private float GroundPixels => FlappyRules.GroundY * H;
    private float PlayX => ScreenSize.X * BirdXRatio;
    private float SetupX => ScreenSize.X - H * 0.4f;
    private float BirdX => _bird.Position.X / H;

    /// <summary>Scroll speed in screen heights per second (before the first flap the world drifts at the starting speed).</summary>
    private float ScrollSpeed => FlappyRules.Speed(_score, _speed, _sinceStep) * _worldFactor;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);

        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _shake = ScreenShake.Create(_world);
        AddChild(_shake);

        // Back to front: pillars, the ground over their feet, the bird, effects
        _pillarLayer = new Node2D();
        _world.AddChild(_pillarLayer);
        _ground = new FlappyGround();
        _world.AddChild(_ground);
        _bird = Bird.Create(H * FlappyRules.BirdWidth);
        _world.AddChild(_bird);
        _fxLayer = new Node2D();
        _world.AddChild(_fxLayer);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _level = (FlappyLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)FlappyLevel.Easy), 0, 2);
        _speed = (StartSpeed)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "speed", (int)StartSpeed.Normal), 0, 2);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky);

        BuildPlayUi();
        _bird.Position = new Vector2(SetupX, FlappyRules.StartY * H);
        Layout();
        GetViewport().SizeChanged += Layout;
        // A long run is slow for a headless smoke test (it stops after 8000 frames): play it at double speed there.
        // Movie capture and real play keep normal time.
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 2.0;

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
        _ground.GroundY = GroundPixels;
        _ground.QueueRedraw();
        if (_state is State.Setup)
            _bird.Position = new Vector2(SetupX, FlappyRules.StartY * H);
        else if (_state is not (State.Crashed or State.Result))
            _bird.Position = new Vector2(PlayX, _flight.Y * H);
    }

    // ---- Play UI: quit, score, best, banners ----

    private void BuildPlayUi()
    {
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_play);

        var frame = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            frame.AddThemeConstantOverride($"margin_{side}", 18);
        _play.AddChild(frame);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(column);

        // Top row: quit on the left, best on the right (the score sits in the middle, see below)
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(top);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var bestPanel = Ui.Panel(Colors.White with { A = 0.92f }, 10, 4);
        bestPanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _bestLabel = Ui.Label("", 26, UiTheme.InkSoft);
        bestPanel.AddChild(_bestLabel);
        top.AddChild(bestPanel);

        _speedBanner = Ui.Label("", 48, Colors.White, 14, UiTheme.Pink);
        _speedBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _speedBanner.Modulate = Colors.Transparent;
        column.AddChild(Ui.Spacer(36));
        column.AddChild(_speedBanner);

        // Score: centered at the top
        var scoreRowHolder = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        scoreRowHolder.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        scoreRowHolder.OffsetTop = 14;
        _play.AddChild(scoreRowHolder);
        var scorePanel = Ui.Panel(Colors.White, 12, 4);
        var scoreRow = new HBoxContainer();
        scoreRow.AddThemeConstantOverride("separation", 10);
        scoreRow.AddChild(Ui.Icon("res://assets/sprites/flappy/bird_01.png", 48));
        _scoreLabel = Ui.Label("0개", 40, UiTheme.Ink);
        _scoreLabel.CustomMinimumSize = new Vector2(90, 0);
        _scoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
        scoreRow.AddChild(_scoreLabel);
        scorePanel.AddChild(scoreRow);
        scoreRowHolder.AddChild(scorePanel);

        _hint = Ui.Label("화면을 누르면 날아요!", 40, Colors.White, 12, UiTheme.Ink);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.Modulate = Colors.Transparent;
        _hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _play.AddChild(_hint);

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
        SetScore(0);
        var best = App.I.Board(GameId, FlappyRules.VariantId(_level)).Best;
        _bestLabel.Text = best is null ? "첫 기록에 도전!" : $"최고 {best.Score}개";
        _speedBanner.Modulate = Colors.Transparent;
        _bigBanner.Visible = false;
    }

    private void SetScore(int score, float punch = 0f)
    {
        _scoreLabel.Text = $"{score}개";
        if (punch > 0)
            Ui.Punch(_scoreLabel, punch);
    }

    private void ShowSpeedBanner(string text)
    {
        Audio.I.Whoosh();
        _speedBanner.Text = text;
        _speedBanner.PivotOffset = _speedBanner.Size / 2;
        _speedBanner.Modulate = Colors.White;
        _speedBanner.Scale = new Vector2(0.5f, 0.5f);
        var tween = _speedBanner.CreateTween();
        tween.TweenProperty(_speedBanner, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(1.1f);
        tween.TweenProperty(_speedBanner, "modulate:a", 0f, 0.3f);
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

    private void ShowHint(bool on)
    {
        _hintTween?.Kill();
        if (on)
        {
            // Over the open sky ahead of the bird
            _hint.Size = Vector2.Zero;
            _hint.ResetSize();
            _hint.Position = new Vector2(ScreenSize.X * 0.58f - _hint.Size.X / 2, H * 0.24f);
            _hint.PivotOffset = _hint.Size / 2;
            _hint.Modulate = Colors.White;
            _hintTween = _hint.CreateTween().SetLoops();
            _hintTween.TweenProperty(_hint, "modulate:a", 0.45f, 0.45f);
            _hintTween.TweenProperty(_hint, "modulate:a", 1f, 0.45f);
        }
        else
        {
            _hintTween = _hint.CreateTween();
            _hintTween.TweenProperty(_hint, "modulate:a", 0f, 0.3f);
        }
    }

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _play.Visible = false;
        ClearPillars();
        _flight.Reset();
        _bird.Revive();
        _worldFactor = 1f;
        _score = 0;
        _sinceStep = FlappyRules.RampSeconds;
        MoveBird(SetupX, FlappyRules.StartY * H);
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/flappy.png", 64));
        titleRow.AddChild(Ui.Label("아기새 날기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);
        var how = Ui.Label("누를 때마다 파닥! 나무 사이로 날아가요", 24, UiTheme.InkSoft);
        how.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(how);

        var levelRow = ChoiceRow.Create("난이도", LevelChoices, (int)_level, 130);
        levelRow.Selected += i =>
        {
            _level = (FlappyLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
        };
        body.AddChild(levelRow);

        var sceneryRow = ChoiceRow.Create("배경", Scenery.Choices, (int)_scenery.Kind);
        sceneryRow.Selected += i =>
        {
            _scenery.Kind = (SceneryKind)i;
            App.I.SetSetting(SettingsSection, "scenery", i);
        };
        body.AddChild(sceneryRow);

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

        // Left side of the screen; the bird flutters on the right, and tapping the open area there makes it flap
        var holder = new MarginContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        holder.AddThemeConstantOverride("margin_left", 24);
        var center = new CenterContainer();
        center.AddChild(panel);
        holder.AddChild(center);
        _setup = holder;
        _ui.AddChild(holder);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();

        if (App.I.Autoplay)
        {
            var token = _roundToken;
            GetTree().CreateTimer(0.6f).Timeout += () =>
            {
                if (_state == State.Setup && token == _roundToken)
                    FlapForShow();
            };
            GetTree().CreateTimer(1.4f).Timeout += () =>
            {
                if (_state == State.Setup && token == _roundToken)
                    StartRound();
            };
        }
    }

    private void MoveBird(float x, float y)
    {
        _birdMove?.Kill();
        _birdMove = CreateTween();
        _birdMove.TweenProperty(_bird, "position", new Vector2(x, y), 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
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
        ClearPillars();
        _flight.Reset();
        _bird.Revive();

        _score = 0;
        _elapsed = 0;
        _stepIndex = 0;
        _sinceStep = FlappyRules.RampSeconds;
        _queuedGap = null;
        _touching = 0;
        _worldFactor = 1f;
        _bounceCount = 0;
        _autoCrashAt = _rng.Next(20, 36);
        _autoSinceFlap = 0;
        ResetHud();
        _play.Visible = true;
        MoveBird(PlayX, FlappyRules.StartY * H);
        _state = State.Countdown;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the last speed-up's tempo

        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown)
                return;
            _state = State.Ready;
            var token = _roundToken;
            // After "시작!" has faded
            GetTree().CreateTimer(0.5f, processAlways: false).Timeout += () =>
            {
                if (token == _roundToken && _state == State.Ready)
                    ShowHint(true);
            };
            if (App.I.Autoplay)
                GetTree().CreateTimer(1.2f, processAlways: false).Timeout += () =>
                {
                    if (token == _roundToken && _state == State.Ready)
                        Press();
                };
        };
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var speed = ScrollSpeed;
        var pixels = speed * H * dt;
        _ground.Scroll(pixels);
        _scenery.CloudDrift = -speed * H * 0.06f; // The sky's clouds are the farthest layer

        if (_state == State.Playing)
            UpdatePlaying(dt);
        else if (_state == State.Crashed)
            MovePillars(speed * dt);

        if (_state is not (State.Crashed or State.Result))
        {
            var hover = _state is State.Setup or State.Countdown or State.Ready;
            if (_state is State.Ready or State.Playing)
            {
                _birdMove?.Kill();
                _bird.Position = new Vector2(PlayX, _flight.Y * H);
            }
            _bird.Step(dt, Bird.FallFraction(_flight.Velocity), hover);
        }
    }

    private void UpdatePlaying(float dt)
    {
        _elapsed += dt;
        _sinceStep += dt;

        // Physics in small steps, so a slow frame (or a 5 fps capture) cannot skip through a pillar
        var steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxSubstep));
        var h = dt / steps;
        for (var i = 0; i < steps && _state == State.Playing; i++)
            Substep(h);
    }

    private void Substep(float dt)
    {
        var speed = ScrollSpeed;
        if (App.I.Autoplay)
            AutoPlay(dt);

        switch (_flight.Step(dt))
        {
            case FlightEvent.Ceiling:
                Bonk();
                break;
            case FlightEvent.Bounced:
                GroundHop();
                break;
        }

        MovePillars(speed * dt);
        SpawnPillars();

        var birdX = PlayX / H;
        Pillar? hit = null;
        foreach (var p in _pillars)
            if (!p.Passed && FlappyRules.Touches(birdX, _flight.Y, p.Left, p.Gap))
                hit = p;
        // Touching counts only after a moment: a flap in that moment still gets clear
        _touching = hit is null ? 0f : _touching + dt;
        if (hit is not null && _touching >= FlappyRules.CrashGrace)
        {
            Crash(hit);
            return;
        }

        foreach (var p in _pillars)
            if (!p.Passed && p != hit && FlappyRules.Passed(birdX, p.Left))
                Pass(p);
    }

    private void SpawnPillars()
    {
        var right = ScreenSize.X / H + SpawnAhead;
        if (_queuedGap is null)
        {
            _queuedGap = FlappyRules.NextGap(_level, null, _rng.NextDouble());
            _nextLeft = right;
        }
        while (_nextLeft < right && _queuedGap is { } gap)
        {
            var pillar = Pillar.Create(gap, _nextLeft, H, GroundPixels);
            PlacePillar(pillar);
            _pillars.Add(pillar);
            _pillarLayer.AddChild(pillar);
            // The one after it: how far away depends on how far apart the two gaps are
            var next = FlappyRules.NextGap(_level, gap, _rng.NextDouble());
            _nextLeft += FlappyRules.Spacing(FlappyRules.PlanningSpeed(_score, _speed), gap, next);
            _queuedGap = next;
        }
    }

    private void PlacePillar(Pillar p) => p.Position = new Vector2((p.Left + FlappyRules.TrunkWidth / 2) * H, 0);

    private void MovePillars(float distance)
    {
        _nextLeft -= distance;
        for (var i = _pillars.Count - 1; i >= 0; i--)
        {
            var p = _pillars[i];
            p.Left -= distance;
            PlacePillar(p);
            if ((p.Left + FlappyRules.PillarPictureWidth) * H < -20)
            {
                _pillars.RemoveAt(i);
                p.QueueFree();
            }
        }
    }

    private void ClearPillars()
    {
        foreach (var p in _pillars)
            p.QueueFree();
        _pillars.Clear();
        _queuedGap = null;
    }

    // ---- Flapping ----

    public override void _UnhandledInput(InputEvent e)
    {
        // Mouse clicks arrive as touches too (emulate_touch_from_mouse), so touch alone covers PC and phones
        var press = e switch
        {
            InputEventScreenTouch { Pressed: true } => true,
            InputEventKey { Pressed: true, Echo: false } key => key.PhysicalKeycode is Key.Space or Key.Up or Key.W,
            _ => false,
        };
        if (!press || App.I.Autoplay)
            return;
        if (_state is State.Setup or State.Countdown or State.Ready or State.Playing)
        {
            Press();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Press()
    {
        switch (_state)
        {
            case State.Setup:
            case State.Countdown:
                FlapForShow(); // Just the wings and the sound: the run starts after the countdown
                break;
            case State.Ready:
                // The first press starts the run
                _state = State.Playing;
                ShowHint(false);
                Flap();
                break;
            case State.Playing:
                Flap();
                break;
        }
    }

    private void FlapForShow()
    {
        _bird.Flap();
        Audio.I.CardFlip();
    }

    /// <summary>A flap: the most frequent thing, so light (wings, a stretch, a small "파닥", two feathers of air).</summary>
    private void Flap()
    {
        _flight.Flap();
        _bird.Flap();
        _autoSinceFlap = 0;
        Audio.I.CardFlip();
        var tail = _bird.Position + new Vector2(-_bird.PictureHeight * 0.4f, _bird.PictureHeight * 0.15f);
        _fxLayer.AddChild(Burst.Create(tail, new Color(1f, 1f, 1f, 0.7f), 3, 120f, 4f, 0f, 0.25f, _rng));
    }

    /// <summary>Head against the top: a soft "콩", no penalty.</summary>
    private void Bonk()
    {
        _bird.Bonk();
        Audio.I.WallBump();
        var head = _bird.Position + new Vector2(0, -_bird.PictureHeight * 0.45f);
        _fxLayer.AddChild(Burst.Create(head, Colors.White, 4, 120f, 4f, 0f, 0.25f, _rng));
    }

    /// <summary>A hop along the grass: a tiny "톡" and a puff of dust.</summary>
    private void GroundHop()
    {
        _bird.Bounce();
        Audio.I.Step(_bounceCount++ % 2 == 1);
        var feet = new Vector2(_bird.Position.X, GroundPixels - 4);
        _fxLayer.AddChild(Burst.Create(feet, new Color(0.85f, 0.72f, 0.52f, 0.8f), 3, 100f, 5f, 0f, 0.3f, _rng));
    }

    // ---- Scoring ----

    /// <summary>A pillar passed: light-medium (a rising "띵", "+1" and little stars in the gap, the leaves rustle).</summary>
    private void Pass(Pillar p)
    {
        p.Passed = true;
        p.Rustle();
        var before = _score;
        _score++;
        SetScore(_score, 0.3f);
        Audio.I.PassTick((_score - 1) % FlappyRules.MilestoneEvery + 1);
        App.Vibrate(10);

        var middle = new Vector2(p.Position.X, p.Gap.Center * H);
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, middle, 3, 220f, 22f, 0.5f, _rng, 0.6f));
        _fxLayer.AddChild(new FloatingText
        {
            Text = "+1",
            Color = UiTheme.Yellow,
            FontSize = 40,
            Position = middle + new Vector2(0, -H * 0.06f),
        });

        var step = FlappyRules.StepAt(_score);
        if (step.Index != _stepIndex)
        {
            _stepIndex = step.Index;
            _sinceStep = 0;
            ShowSpeedBanner(SpeedBanners[Mathf.Clamp(step.Index, 0, SpeedBanners.Length - 1)]);
            _ground.Gust();
            Audio.I.SetMusicTempo(1f + 0.04f * step.Index);
        }
        if (FlappyRules.CrossedMilestone(before, _score))
            Milestone();
    }

    /// <summary>Every 10 pillars: important (sparkle, a star shower from the bird, a small shake, "10개! 최고야!").</summary>
    private void Milestone()
    {
        Audio.I.Cheer();
        _shake.Add(3f);
        _bird.Flash();
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, _bird.Position, 10, 600f, 40f, 1.0f, _rng, 1.1f));
        _fxLayer.AddChild(new FloatingText
        {
            Text = $"{_score}개! {Cheers[_rng.Next(Cheers.Length)]}",
            Color = UiTheme.Gold,
            FontSize = 60,
            Position = new Vector2(ScreenSize.X / 2, H * 0.5f),
        });
        Ui.Punch(_scoreLabel, 0.6f);
    }

    // ---- Crash ----

    /// <summary>
    /// Bumped into a pillar: climax but gentle. The dizzy face spins down onto the grass with a cartoon "뿌우웅", the
    /// world glides to a stop, "앗! 다시 해볼까요?", then the result screen with "다시 하기" first.
    /// </summary>
    private void Crash(Pillar p)
    {
        _state = State.Crashed;
        var token = ++_roundToken;
        _touching = 0;
        ShowHint(false);
        Audio.I.Tumble();
        Audio.I.DuckMusic(-10f);
        App.Vibrate(40);
        _shake.Add(4f);
        p.Bumped();
        _bird.Position = new Vector2(PlayX, _flight.Y * H);
        _bird.Dizzy(GroundPixels - _bird.PictureHeight * 0.45f);
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, _bird.Position, 4, 280f, 26f, 0.6f, _rng, 1f));

        var stop = CreateTween();
        stop.TweenMethod(Callable.From<float>(v => _worldFactor = v), 1f, 0f, 0.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        GetTree().CreateTimer(0.45f, processAlways: false).Timeout += () =>
        {
            if (token == _roundToken)
                ShowBigBanner("앗! 다시 해볼까요?");
        };
        GetTree().CreateTimer(2.0f, processAlways: false).Timeout += () =>
        {
            if (token == _roundToken)
                ShowResult();
        };
    }

    private void ShowResult()
    {
        _state = State.Result;
        _worldFactor = 0f;
        var tween = _bigBanner.CreateTween();
        tween.TweenProperty(_bigBanner, "modulate:a", 0f, 0.2f);
        tween.TweenCallback(Callable.From(() => _bigBanner.Visible = false));

        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = FlappyRules.VariantId(_level),
            Title = "잘 날았어요!",
            Caption = "지나간 기둥",
            Value = _score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Detail = $"{Mathf.FloorToInt(_elapsed)}초 동안 날았어요",
            Entry = new ScoreEntry("", _score, (long)(_elapsed * 1000), App.Now()),
            Recordable = _score > 0,
            NotRecordableMessage = "다음엔 나무 사이로 꼭 지나가 보자!",
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
    /// Flaps whenever the bird sinks below the middle of the next gap (at most once per short moment). Once the score
    /// reaches a random 20~35 it stops flapping, hops along the grass into the next pillar, and the run ends, so
    /// captures reach the crash and the result screen.
    /// </summary>
    private void AutoPlay(float dt)
    {
        _autoSinceFlap += dt;
        if (_score >= _autoCrashAt)
            return;
        var target = FlappyRules.StartY;
        foreach (var p in _pillars)
        {
            if (!p.Passed)
            {
                target = p.Gap.Center;
                break;
            }
        }
        if (_flight.Y > target + 0.03f && _autoSinceFlap >= 0.18f)
            Flap();
    }
}
