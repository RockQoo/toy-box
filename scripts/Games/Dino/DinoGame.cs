using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.DinoRunner;

/// <summary>
/// 공룡 점프 (docs/games/06-dino-jump.md).
/// Flow: Setup (꾸미기: 공룡 색, 배경, 속도) → Countdown → Playing (auto-run; one button jumps; obstacles come faster
/// and closer over time) → Crashed (soft tumble, "앗! 다시 해볼까요?") → Result (shared ResultPanel).
/// SPACE / ↑ / W, a click or a tap anywhere jumps. All nodes are built in code.
/// </summary>
public partial class DinoGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Crashed, Result }

    private const string GameId = "dino";
    private const string SettingsSection = "dino";
    private const float GroundRatio = 0.83f;      // Ground line, fraction of the screen height from the top
    private const float DinoHeightRatio = 0.27f;  // Running dino picture height, fraction of the screen height
    private const float HitOffset = 0.03f;        // Hitbox center right of the feet (the body leans forward), screen heights
    private const float SpawnAhead = 0.15f;       // Obstacles appear this far right of the screen edge, screen heights
    private const float PteroFrom = 6f;           // First background pterodactyl after this many seconds

    private static readonly string[] SpeedBanners = { "", "빨라져요!", "더 빨라져요!", "엄청 빨라요!", "최고 속도!" };
    private static readonly string[] Cheers = { "최고야!", "대단해!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();
    private readonly List<Obstacle> _obstacles = new();
    private readonly List<(Sprite2D Sprite, float BaseY, float Phase)> _pteros = new();
    private readonly DinoJump _jump = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Landscape _landscape = null!;
    private Node2D _skyLayer = null!;
    private Node2D _obstacleLayer = null!;
    private Dino _dino = null!;
    private Node2D _fxLayer = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private Control? _setup;
    private Control _play = null!;
    private Label _scoreLabel = null!;
    private TextureRect _scoreIcon = null!;
    private Label _bestLabel = null!;
    private Label _speedBanner = null!;
    private Label _bigBanner = null!;
    private Label _hint = null!;
    private Texture2D _starTexture = null!;
    private Tween? _dinoMove;
    private Tween? _hintTween;

    private int _color;
    private StartSpeed _speed;
    private float _elapsed;
    private int _score;
    private int _stepIndex;
    private float _nextSpawn;    // Screen heights of ground still to scroll before the next obstacle
    private float _touching;     // Seconds the dino has been touching an obstacle (crash grace)
    private float _worldFactor = 1f;
    private float _pteroTimer;
    private bool _jumpedOnce;
    private int _roundToken;
    private int _autoCrashAt;

    private Vector2 ScreenSize => GetViewportRect().Size;
    private float H => ScreenSize.Y;
    private float GroundY => H * GroundRatio;
    private float PlayX => Mathf.Max(H * 0.34f, ScreenSize.X * 0.17f);
    private float SetupX => ScreenSize.X - H * 0.33f;

    /// <summary>Run speed right now in screen heights per second (setup and countdown run at the starting speed; a crash eases the current speed to a stop).</summary>
    private float RunSpeed => DinoRules.RunSpeed(_state is State.Playing or State.Crashed ? _elapsed : 0f, _speed) * _worldFactor;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);

        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _shake = ScreenShake.Create(_world);
        AddChild(_shake);

        // Back to front: mountains + ground, pterodactyls high up, obstacles, the dino, effects
        _landscape = new Landscape();
        _world.AddChild(_landscape);
        _skyLayer = new Node2D();
        _world.AddChild(_skyLayer);
        _obstacleLayer = new Node2D();
        _world.AddChild(_obstacleLayer);
        _dino = Dino.Create(H * DinoHeightRatio);
        _world.AddChild(_dino);
        _fxLayer = new Node2D();
        _world.AddChild(_fxLayer);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _color = Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "color", 0), 0, Dino.ColorCount - 1);
        _speed = (StartSpeed)(int)App.I.GetSetting(SettingsSection, "speed", (int)StartSpeed.Normal);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky);
        _dino.SetColor(_color);

        BuildPlayUi();
        Layout();
        _dino.Position = new Vector2(SetupX, GroundY);
        GetViewport().SizeChanged += Layout;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private void Layout()
    {
        _landscape.GroundY = GroundY;
        _landscape.QueueRedraw();
        _dino.Position = new Vector2(_state == State.Setup ? SetupX : PlayX, GroundY);
        foreach (var o in _obstacles)
            o.Position = o.Position with { Y = GroundY };
    }

    // ---- Play UI: score, quit, best, banners ----

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

        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(top);

        var scorePanel = Ui.Panel(Colors.White, 12, 4);
        var scoreRow = new HBoxContainer();
        scoreRow.AddThemeConstantOverride("separation", 10);
        _scoreIcon = Ui.Icon(Dino.RunPath, 48);
        scoreRow.AddChild(_scoreIcon);
        _scoreLabel = Ui.Label("점수 0", 40, UiTheme.Ink);
        _scoreLabel.CustomMinimumSize = new Vector2(150, 0);
        scoreRow.AddChild(_scoreLabel);
        scorePanel.AddChild(scoreRow);
        top.AddChild(scorePanel);

        // Up here, away from the play area, and it asks before quitting
        top.AddChild(Ui.Spacer(0, 14));
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
        column.AddChild(Ui.Spacer(24));
        column.AddChild(_speedBanner);

        _hint = Ui.Label("화면을 누르면 점프!", 36, Colors.White, 12, UiTheme.Ink);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _hint.Modulate = Colors.Transparent;
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
        _scoreIcon.Material = Dino.Choices[_color].IconMaterial;
        var best = App.I.Board(GameId).Best;
        _bestLabel.Text = best is null ? "첫 기록에 도전!" : $"최고 {best.Score}점";
        _speedBanner.Modulate = Colors.Transparent;
        _bigBanner.Visible = false;
    }

    private void SetScore(int score, float punch = 0f)
    {
        _scoreLabel.Text = $"점수 {score}";
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
            // Just above the dino's jump, centered over the open ground
            _hint.Position = new Vector2(ScreenSize.X / 2 - _hint.Size.X / 2, GroundY - H * 0.5f);
            _hint.Modulate = Colors.White;
            _hintTween = _hint.CreateTween().SetLoops();
            _hintTween.TweenProperty(_hint, "scale", new Vector2(1.08f, 1.08f), 0.4f);
            _hintTween.TweenProperty(_hint, "scale", Vector2.One, 0.4f);
            _hint.PivotOffset = _hint.Size / 2;
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
        ClearObstacles();
        _jump.Reset();
        _dino.Revive();
        _worldFactor = 1f;
        MoveDino(SetupX);
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(body);

        var title = Ui.Label("공룡 점프", 42, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);
        var how = Ui.Label("화면을 누르면 점프! 선인장과 돌을 넘어요", 24, UiTheme.InkSoft);
        how.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(how);

        var dinoRow = ChoiceRow.Create("공룡", Dino.Choices, _color, 110);
        dinoRow.Selected += i =>
        {
            _color = i;
            _dino.SetColor(i);
            App.I.SetSetting(SettingsSection, "color", i);
            Jump(); // A happy hop in the new color
        };
        body.AddChild(dinoRow);

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

        // Left side of the screen; the dino runs on the right, and tapping the open area there makes it jump
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
                    Jump(); // Shows the try-it jump in the captured frames
            };
            GetTree().CreateTimer(1.4f).Timeout += () =>
            {
                if (_state == State.Setup && token == _roundToken)
                    StartRound();
            };
        }
    }

    private void MoveDino(float x)
    {
        _dinoMove?.Kill();
        _dinoMove = CreateTween();
        _dinoMove.TweenProperty(_dino, "position:x", x, 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
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
        ClearObstacles();
        _jump.Reset();
        _dino.Revive();

        _score = 0;
        _elapsed = 0;
        _stepIndex = 0;
        _nextSpawn = 0.3f;
        _touching = 0;
        _worldFactor = 1f;
        _pteroTimer = PteroFrom;
        _jumpedOnce = false;
        _autoCrashAt = _rng.Next(25, 41);
        ResetHud();
        _play.Visible = true;
        MoveDino(PlayX);
        _state = State.Countdown;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the last speed-up's tempo

        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown)
                return;
            _state = State.Playing;
            // After "시작!" has faded, unless the child already jumped
            var token = _roundToken;
            GetTree().CreateTimer(0.6f, processAlways: false).Timeout += () =>
            {
                if (token == _roundToken && _state == State.Playing && !_jumpedOnce)
                    ShowHint(true);
            };
        };
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var speed = RunSpeed;
        var pixels = speed * H * dt;
        _landscape.Scroll(pixels);
        _scenery.CloudDrift = -speed * H * 0.05f; // The sky's clouds are the farthest layer
        MoveObstacles(pixels);
        StepPteros(dt, speed);
        StepDino(dt, speed);

        if (_state == State.Playing)
            UpdatePlaying(dt, speed);
    }

    private void StepDino(float dt, float speed)
    {
        if (_state is not (State.Crashed or State.Result))
        {
            switch (_jump.Step(dt))
            {
                case JumpEvent.Landed:
                    _dino.Land();
                    Dust(5, 0.6f);
                    break;
                case JumpEvent.Jumped:
                    TookOff(); // A buffered press: straight back up on landing
                    break;
            }
        }
        _dino.Lift = _jump.Height * H;
        var stepRate = 2.5f + speed * 5f;
        _dino.Step(dt, stepRate, !_jump.Grounded, _jump.Velocity / DinoRules.JumpVelocity);
        if (_jump.Grounded && _worldFactor > 0.5f && _state is not (State.Crashed or State.Result) && _dino.FootDown(dt, stepRate))
            Dust(2, 0.35f);
    }

    private void UpdatePlaying(float dt, float speed)
    {
        _elapsed += dt;

        var step = DinoRules.StepAt(_elapsed);
        if (step.Index != _stepIndex)
        {
            _stepIndex = step.Index;
            SpeedUp(step.Index);
        }

        _nextSpawn -= speed * dt;
        if (_nextSpawn <= 0)
        {
            SpawnObstacle(DinoRules.PickObstacle(_elapsed, _rng.NextDouble()));
            _nextSpawn += DinoRules.NextGap(_elapsed, _speed, _rng.NextDouble());
        }

        if (App.I.Autoplay)
            AutoPlay(dt, speed);

        var dinoLeft = DinoHitLeft();
        var feet = _jump.Height;
        Obstacle? hit = null;
        foreach (var o in _obstacles)
        {
            if (!o.Passed && DinoRules.Touches(dinoLeft, feet, HitLeft(o), o.Kind))
                hit = o;
        }
        // Touching counts only after a moment: a press in that moment still lifts the dino clear
        _touching = hit is null ? 0f : _touching + dt;
        if (hit is not null && _touching >= DinoRules.CrashGrace)
        {
            Crash(hit);
            return;
        }

        foreach (var o in _obstacles)
            if (!o.Passed && o != hit && DinoRules.Passed(dinoLeft, HitLeft(o), o.Kind))
                Pass(o);
    }

    /// <summary>The dino's hitbox left edge in screen heights.</summary>
    private float DinoHitLeft() => (_dino.Position.X / H) + HitOffset - DinoRules.DinoHitWidth / 2;

    /// <summary>An obstacle's hitbox left edge in screen heights (the hitbox is centered on the picture).</summary>
    private float HitLeft(Obstacle o) => o.Position.X / H - DinoRules.Hitbox(o.Kind).Width / 2;

    private void SpawnObstacle(DinoObstacle kind)
    {
        var obstacle = Obstacle.Create(kind, H);
        // Hitbox front edges always start at the same place, so the gap is exactly the distance between them
        var x = ScreenSize.X + H * (SpawnAhead + DinoRules.Hitbox(kind).Width / 2);
        obstacle.Position = new Vector2(x, GroundY);
        _obstacles.Add(obstacle);
        _obstacleLayer.AddChild(obstacle);
    }

    private void MoveObstacles(float pixels)
    {
        for (var i = _obstacles.Count - 1; i >= 0; i--)
        {
            var o = _obstacles[i];
            o.Position -= new Vector2(pixels, 0);
            if (o.Position.X + o.HalfWidth < -20)
            {
                _obstacles.RemoveAt(i);
                o.QueueFree();
            }
        }
    }

    private void ClearObstacles()
    {
        foreach (var o in _obstacles)
            o.QueueFree();
        _obstacles.Clear();
        foreach (var p in _pteros)
            p.Sprite.QueueFree();
        _pteros.Clear();
    }

    // ---- Jumping ----

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
        if (_state is State.Setup or State.Countdown or State.Playing)
        {
            Jump();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Jump()
    {
        if (_jump.Press())
            TookOff();
    }

    /// <summary>Take-off: light and quick (it happens all the time): stretch, "보잉", a puff of dust.</summary>
    private void TookOff()
    {
        _dino.TakeOff();
        Audio.I.Jump();
        Dust(4, 0.5f);
        if (!_jumpedOnce && _state == State.Playing)
        {
            _jumpedOnce = true;
            ShowHint(false);
        }
    }

    private void Dust(int count, float size)
    {
        var feet = new Vector2(_dino.Position.X - H * 0.02f, GroundY - 4);
        _fxLayer.AddChild(Burst.Create(feet, new Color(0.85f, 0.72f, 0.52f, 0.8f), count, 140f * size + 60f, 9f * size + 3f,
            0f, 0.3f, _rng));
    }

    // ---- Scoring ----

    /// <summary>An obstacle went by: light-medium (a rising "띵", "+1", a few stars, the obstacle wobbles).</summary>
    private void Pass(Obstacle o)
    {
        o.Passed = true;
        o.Wobble();
        var before = _score;
        _score++;
        SetScore(_score, 0.3f);
        Audio.I.PassTick((_score - 1) % DinoRules.MilestoneEvery + 1);
        App.Vibrate(10);

        var top = new Vector2(o.Position.X, GroundY - H * DinoRules.Hitbox(o.Kind).Height - H * 0.05f);
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, top, 3, 260f, 22f, 0.5f, _rng, 0.9f));
        _fxLayer.AddChild(new FloatingText
        {
            Text = "+1",
            Color = UiTheme.Yellow,
            FontSize = 40,
            Position = top + new Vector2(0, -20),
        });

        if (DinoRules.CrossedMilestone(before, _score))
            Milestone();
    }

    /// <summary>Every 10 points: important (sparkle run, star shower from the dino, a small shake, a big "N점!").</summary>
    private void Milestone()
    {
        Audio.I.Cheer();
        _shake.Add(3f);
        _dino.Flash();
        var head = new Vector2(_dino.Position.X, GroundY - _dino.Lift - H * DinoHeightRatio * 0.7f);
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, head, 10, 600f, 40f, 1.0f, _rng, 1.1f));
        _fxLayer.AddChild(new FloatingText
        {
            Text = $"{_score}점! {Cheers[_rng.Next(Cheers.Length)]}",
            Color = UiTheme.Gold,
            FontSize = 60,
            Position = new Vector2(ScreenSize.X / 2, H * 0.5f),
        });
        Ui.Punch(_scoreLabel, 0.6f);
    }

    /// <summary>Speed step: important (banner, wind streaks, the music speeds up a little).</summary>
    private void SpeedUp(int index)
    {
        ShowSpeedBanner(SpeedBanners[Mathf.Clamp(index, 0, SpeedBanners.Length - 1)]);
        _landscape.Gust();
        Audio.I.SetMusicTempo(1f + 0.04f * index);
    }

    // ---- Crash ----

    /// <summary>
    /// Bumped into an obstacle: climax but gentle. A soft cartoon tumble and sound, the world glides to a stop,
    /// "앗! 다시 해볼까요?", then the result screen with "다시 하기" first.
    /// </summary>
    private void Crash(Obstacle o)
    {
        _state = State.Crashed;
        var token = ++_roundToken;
        _touching = 0;
        _jump.Reset();
        ShowHint(false);
        Audio.I.Tumble();
        Audio.I.DuckMusic(-10f);
        App.Vibrate(40);
        _shake.Add(4f);
        o.Bumped();
        _dino.Tumble();
        var at = new Vector2((_dino.Position.X + o.Position.X) / 2, GroundY - H * 0.08f);
        _fxLayer.AddChild(Burst.Create(at, new Color(0.85f, 0.72f, 0.52f), 10, 260f, 10f, 0f, 0.45f, _rng));
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, at, 4, 280f, 26f, 0.6f, _rng, 1f));

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

        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            Title = "잘 달렸어요!",
            Caption = "점수",
            Value = _score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}점",
            Detail = $"{Mathf.FloorToInt(_elapsed)}초 동안 달렸어요",
            Entry = new ScoreEntry("", _score, (long)(_elapsed * 1000), App.Now()),
            Recordable = _score > 0,
            NotRecordableMessage = "다음엔 선인장을 꼭 넘어 보자!",
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("공룡 바꾸기", UiTheme.Blue, () =>
                {
                    ResultPanel.CloseAll(_ui);
                    ShowSetup();
                }),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    // ---- Background pterodactyls (harmless flavor) ----

    /// <summary>
    /// Now and then a pterodactyl flies by high above, slower than the ground (it is flying along with the dino).
    /// Never an obstacle: dodging it would need a second button (ducking).
    /// </summary>
    private void StepPteros(float dt, float speed)
    {
        if (_state == State.Playing)
        {
            _pteroTimer -= dt;
            if (_pteroTimer <= 0)
            {
                _pteroTimer = 14f + 8f * (float)_rng.NextDouble();
                SpawnPtero();
            }
        }
        for (var i = _pteros.Count - 1; i >= 0; i--)
        {
            var (sprite, baseY, phase) = _pteros[i];
            var t = phase + (float)Time.GetTicksMsec() / 1000f;
            var dx = Mathf.Max(speed * H * 0.35f, H * 0.08f) * dt;
            sprite.Position = new Vector2(sprite.Position.X - dx, baseY + Mathf.Sin(t * 2f) * H * 0.015f);
            // Wing beat: a quick vertical squash
            var flap = 1f + 0.1f * Mathf.Sin(t * 9f);
            var s = sprite.Scale;
            sprite.Scale = new Vector2(Mathf.Abs(s.X), Mathf.Abs(s.X) * flap);
            if (sprite.Position.X < -H * 0.4f)
            {
                sprite.QueueFree();
                _pteros.RemoveAt(i);
            }
        }
    }

    private void SpawnPtero()
    {
        var texture = Ui.Tex("res://assets/sprites/dino/pterodactyl.png");
        var scale = H * 0.11f / texture.GetSize().Y;
        var y = H * (0.18f + 0.08f * (float)_rng.NextDouble());
        var sprite = new Sprite2D
        {
            Texture = texture,
            Scale = new Vector2(scale, scale),
            Position = new Vector2(ScreenSize.X + H * 0.3f, y),
        };
        _skyLayer.AddChild(sprite);
        _pteros.Add((sprite, y, (float)_rng.NextDouble() * Mathf.Tau));
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// Jumps in the middle of each obstacle's timing window, so every jump path runs (sometimes a buffered press
    /// when two come close). Once the score reaches a random 25~40 it "looks away" and runs into the next one,
    /// so captures reach the crash and the result screen.
    /// </summary>
    private void AutoPlay(float dt, float speed)
    {
        var dinoFront = DinoHitLeft() + DinoRules.DinoHitWidth;
        foreach (var o in _obstacles)
        {
            if (o.Passed || o.Aimed)
                continue;
            var distance = HitLeft(o) - dinoFront;
            if (DinoRules.IdealJumpIn(o.Kind, distance, speed) > dt * 0.5f)
                break; // The nearest one is not due yet
            o.Aimed = true;
            if (_score < _autoCrashAt)
                Jump();
            break;
        }
    }
}
