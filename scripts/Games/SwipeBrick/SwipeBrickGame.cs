using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Breakout;
using Vec = System.Numerics.Vector2;

namespace ToyBox.Games.SwipeBrick;

/// <summary>
/// 공 쏘기 벽돌깨기 (docs/games/12-swipe-brick.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → the first row comes down → aim (press on the board and drag: the balls go
/// toward the finger; ← → and Space work too) → release to fire every ball one after another → they bounce off the
/// walls and the numbered bricks (each hit takes one off the number) and stop on the flower line → the rows move down
/// one step and a new row comes in → … → after the 20th shot "끝!" → shared ResultPanel. Bricks that reach the flower
/// line turn into flowers (no game over, no points). Bubbles give one more ball from the next shot. A turn game: no
/// countdown and no clock. The fast-forward button doubles the flight; it bounces after 3 seconds of flight.
/// </summary>
public partial class SwipeBrickGame : Node2D
{
    private enum State { Setup, Intro, Aiming, Flying, Settling, Ending, Result }

    private const string GameId = "swipe";
    private const string SettingsSection = "swipe";
    private const float SetupChipHeight = 86f;
    private const float KeyTurnSpeed = 70f;     // Degrees per second with ← →
    private const float FastHintAfter = 3f;     // Seconds of flight before the fast-forward button bounces
    private const int MaxTicksPerFrame = 48;
    private const int MilestoneEvery = 10;

    private static readonly string[] Cheers = { "최고야!", "와르르!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();
    private readonly List<SwipeEvent> _events = new();
    private readonly List<(int Col, bool Brick)> _flowers = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _boardNode = null!;
    private SwipeBackdrop _backdrop = null!;
    private SwipeBrickField _field = null!;
    private AimGuide _aim = null!;
    private Launcher _launcher = null!;
    private SwipeBallLayer _balls = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private SwipeHud _hud = null!;
    private Control? _setup;
    private Texture2D _starTexture = null!;

    private float _unit = 720f;
    private SwipeLevel _level;
    private SwipeBrickBoard _board = new(SwipeLevel.Easy);
    private SwipeShot? _shot;
    private float _launchX = SwipeBrickRules.Width / 2;
    private float _aimAngle = 90f;
    private bool _aiming;          // A finger is down (or the keys moved the aim)
    private Vec? _aimDir;          // Current aim direction (null: none yet)
    private float _accumulator;
    private bool _fast;
    private bool _recallSaid;
    private bool _landSeen;
    private int _streak;           // Bricks broken in this shot (rising "땡")
    private int _bestBalls;
    private Tween? _rowTween;

    // Autoplay
    private float _autoTimer;
    private float? _autoTarget;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _shake = ScreenShake.Create(_world);
        AddChild(_shake);

        _buddy = new Buddy();
        _world.AddChild(_buddy);
        _boardNode = new Node2D();
        _world.AddChild(_boardNode);
        _backdrop = new SwipeBackdrop();
        _boardNode.AddChild(_backdrop);
        _field = new SwipeBrickField();
        _boardNode.AddChild(_field);
        _aim = new AimGuide();
        _boardNode.AddChild(_aim);
        _launcher = new Launcher();
        _boardNode.AddChild(_launcher);
        _balls = new SwipeBallLayer();
        _boardNode.AddChild(_balls);
        _fx = new Node2D();
        _boardNode.AddChild(_fx);

        _ui = new CanvasLayer();
        AddChild(_ui);
        _hud = SwipeHud.Create();
        _hud.FastPressed += TurnOnFast;
        _ui.AddChild(_hud);

        _level = (SwipeLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)SwipeLevel.Easy), 0, 1);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 1));

        Layout();
        GetViewport().SizeChanged += Layout;
        // 20 shots take a while for the headless smoke test (it stops after 8000 frames): triple speed there only
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 3.0;
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    // ---- Layout: the tall board in the middle, clear of the HUD columns ----

    private void Layout()
    {
        var screen = ScreenSize;
        var avail = Mathf.Max(200f, screen.X - SwipeHud.LeftWidth - SwipeHud.RightWidth);
        _unit = Mathf.Floor(Mathf.Min(screen.Y, avail / SwipeBrickRules.Width));
        var boardWidth = SwipeBrickRules.Width * _unit;
        var x = Mathf.Clamp(screen.X / 2 - boardWidth / 2, SwipeHud.LeftWidth,
            Mathf.Max(SwipeHud.LeftWidth, screen.X - SwipeHud.RightWidth - boardWidth));
        _boardNode.Position = new Vector2(x, screen.Y - _unit).Round();

        _backdrop.Unit = _unit;
        _field.Unit = _unit;
        _balls.Unit = _unit;
        _launcher.Unit = _unit;
        _launcher.Relayout();
        _launcher.Position = Px(new Vec(_launchX, SwipeBrickRules.LaunchY));
        _aim.DotSize = _unit * 0.022f;
        _buddy.Position = new Vector2(screen.X - SwipeHud.RightWidth / 2, screen.Y - 16);
        if (_state == State.Aiming)
            UpdateAimGuide();
    }

    private Vector2 Px(Vec v) => new(v.X * _unit, v.Y * _unit);

    private Vec ToBoard(Vector2 screen)
    {
        var local = (screen - _world.Position - _boardNode.Position) / _unit;
        return new Vec(local.X, local.Y);
    }

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _hud.Visible = false;
        ResultPanel.CloseAll(_ui);
        ClearRound();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/swipe_brick.png", 64));
        titleRow.AddChild(Ui.Label("공 쏘기 벽돌깨기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        var levelChoices = new[]
        {
            new Choice(SwipeBrickRules.Variants[0].Label, "res://assets/sprites/brick/brick_1.png"),
            new Choice(SwipeBrickRules.Variants[1].Label, "res://assets/sprites/brick/brick_3.png"),
        };
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 150, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (SwipeLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
        };
        body.AddChild(levelRow);

        var sceneryRow = ChoiceRow.Create("배경", Scenery.Choices, (int)_scenery.Kind, chipHeight: SetupChipHeight);
        sceneryRow.Selected += i =>
        {
            _scenery.Kind = (SceneryKind)i;
            App.I.SetSetting(SettingsSection, "scenery", i);
        };
        body.AddChild(sceneryRow);

        var buddyRow = ChoiceRow.Create("응원 친구", Buddy.Choices, (int)App.I.GetSetting(SettingsSection, "buddy", 1), 110,
            chipHeight: SetupChipHeight);
        buddyRow.Selected += i =>
        {
            _buddy.SetCharacter(i);
            _buddy.Say("같이 하자!", 0.9f);
            App.I.SetSetting(SettingsSection, "buddy", i);
        };
        body.AddChild(buddyRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 76)));
        buttons.AddChild(Ui.Button("시작!", StartRound, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        _setup = Ui.Centered(panel);
        _ui.AddChild(_setup);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();

        if (App.I.Autoplay)
            GetTree().CreateTimer(1.2f).Timeout += () =>
            {
                if (_state == State.Setup)
                    StartRound();
            };
    }

    /// <summary>An empty board with the rabbit in the middle.</summary>
    private void ClearRound()
    {
        _rowTween?.Kill();
        _rowTween = null;
        _board = new SwipeBrickBoard(_level);
        _field.Board = _board;
        _field.ResetEffects();
        _field.Position = Vector2.Zero;
        _shot = null;
        _balls.Clear();
        _aim.Hide();
        _aiming = false;
        _aimDir = null;
        _fast = false;
        _launchX = SwipeBrickRules.Width / 2;
        _launcher.MoveTo(Px(new Vec(_launchX, 0)).X, false);
        _launcher.Position = Px(new Vec(_launchX, SwipeBrickRules.LaunchY));
        _launcher.Holding = true;
        _launcher.AimAngle = null;
        _launcher.SetBalls(1);
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        ClearRound();
        _bestBalls = 1;
        _hud.Visible = true;
        _hud.SetShotsLeft(_board.ShotsLeft);
        _hud.SetScore(0);
        _hud.SetBalls(1);
        _hud.SetFast(false, false);
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck

        // The first row comes down from above the screen
        _state = State.Intro;
        _board.SpawnRow(_rng);
        _field.Position = new Vector2(0, -(_boardNode.Position.Y + SwipeBrickRules.CellY(1) * _unit + 20));
        Audio.I.Whoosh();
        _rowTween = CreateTween();
        _rowTween.TweenProperty(_field, "position", Vector2.Zero, 0.7f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _rowTween.TweenCallback(Callable.From(() =>
        {
            _buddy.Say("끌어서 쏘아요!", 1.8f);
            BeginAiming();
        }));
    }

    private void BeginAiming()
    {
        _state = State.Aiming;
        _aiming = false;
        _aimDir = null;
        _launcher.Holding = true;
        _launcher.SetBalls(_board.Balls);
        _hud.SetFast(false, false);
        _autoTimer = 0;
        _autoTarget = null;
        if (App.I.Autoplay)
            // Turns 2..11 are played badly on purpose, so rows pile up and the flower line gets used too
            _autoTarget = SwipeBrickRules.BestAngle(_board, _launchX, fewest: _board.Turn is >= 2 and <= 11);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        switch (_state)
        {
            case State.Aiming:
                UpdateAiming(dt);
                break;
            case State.Flying:
                UpdateFlying(dt);
                break;
        }
    }

    // ---- Aiming ----

    private void UpdateAiming(float dt)
    {
        if (App.I.Autoplay)
        {
            AutoAim(dt);
            return;
        }
        var turn = 0f;
        if (Input.IsActionPressed("ui_left") || Input.IsPhysicalKeyPressed(Key.A))
            turn += 1;
        if (Input.IsActionPressed("ui_right") || Input.IsPhysicalKeyPressed(Key.D))
            turn -= 1;
        if (turn != 0)
        {
            _aimAngle = SwipeBrickRules.ClampAimAngle(_aimAngle + turn * KeyTurnSpeed * dt);
            _aimDir = SwipeBrickRules.DirectionFromAngle(_aimAngle);
            UpdateAimGuide();
        }
    }

    private void UpdateAimGuide()
    {
        if (_aimDir is not { } dir)
        {
            _aim.Hide();
            _launcher.AimAngle = null;
            return;
        }
        var valid = SwipeBrickRules.CanFire(dir);
        var from = Px(new Vec(_launchX, SwipeBrickRules.LaunchY));
        Vector2 to;
        if (valid)
        {
            var (end, _) = SwipeBrickRules.PredictFirstBounce(_board, _launchX, dir);
            to = Px(end);
        }
        else
        {
            to = from + new Vector2(dir.X, dir.Y) * _unit * 0.25f;
        }
        _aim.Show(from, to, valid);
        var angle = SwipeBrickRules.AngleOf(dir);
        _launcher.AimAngle = SwipeBrickRules.ClampAimAngle(angle < -90f ? 180f : angle);
    }

    private void Fire()
    {
        if (_state != State.Aiming || _aimDir is not { } dir || !SwipeBrickRules.CanFire(dir))
            return;
        _aimAngle = SwipeBrickRules.AngleOf(dir);
        _aim.Hide();
        _aiming = false;
        _launcher.AimAngle = null;
        _launcher.Holding = false;
        _shot = new SwipeShot(_board, _launchX, dir, _board.Balls);
        _balls.Begin(_shot);
        _accumulator = 0;
        _fast = false;
        _recallSaid = false;
        _landSeen = false;
        _streak = 0;
        _state = State.Flying;
        _hud.SetShotsLeft(_board.ShotsLeft, true);
        _hud.SetFast(true, false);
        Audio.I.Jump();
    }

    // ---- Flight ----

    private void UpdateFlying(float dt)
    {
        if (_shot is not { } shot)
            return;
        _accumulator += dt * (_fast ? 2f : 1f);
        var ticks = 0;
        _events.Clear();
        while (_accumulator >= SwipeBrickRules.FixedDt && ticks < MaxTicksPerFrame && !shot.Done)
        {
            shot.Tick(_events);
            _accumulator -= SwipeBrickRules.FixedDt;
            ticks++;
        }
        if (ticks >= MaxTicksPerFrame)
            _accumulator = 0;
        HandleEvents(shot);
        _balls.Advance(dt * (_fast ? 2f : 1f));

        if (shot.Time >= FastHintAfter && !_fast)
            _hud.PulseFast();
        if (App.I.Autoplay && !_fast && shot.Time > 0.4f)
            TurnOnFast();
        if (shot.Recalling && !_recallSaid)
        {
            _recallSaid = true;
            _buddy.Say("공들아 돌아와!", 1.4f);
            Audio.I.Whoosh();
        }
        if (shot.Done)
            ShotFinished();
    }

    private void HandleEvents(SwipeShot shot)
    {
        var tock = false;
        var wall = false;
        var launches = 0;
        foreach (var e in _events)
        {
            switch (e.Kind)
            {
                case SwipeEventKind.Launch:
                    launches++;
                    break;
                case SwipeEventKind.Wall:
                    wall = true;
                    break;
                case SwipeEventKind.Brick:
                    if (e.Broke)
                        BrickBroke(e);
                    else
                        tock = true;
                    _field.Hit(e.Col, e.Row, e.Broke, SwipeBrickRules.ColorIndex(1));
                    break;
                case SwipeEventKind.Item:
                    ItemTaken(e);
                    break;
                case SwipeEventKind.Land:
                    BallLanded(shot, e);
                    break;
            }
        }
        // One sound per kind per frame, however many balls touched something
        if (tock)
            Audio.I.Click();
        else if (wall)
            Audio.I.Step(_rng.Next(2) == 0);
        if (launches > 0 && shot.Launched > 1)
            Audio.I.Pop(Mathf.Min(shot.Launched / 3, 6));
    }

    private void BrickBroke(SwipeEvent e)
    {
        _streak++;
        _hud.SetScore(_board.Score, 0.3f);
        Audio.I.PassTick(Mathf.Min(_streak, 12)); // "땡", rising through the shot
        App.Vibrate(10);
        var center = _field.Position + _field.CellCenter(e.Col, e.Row);
        var tint = BrickArt.Tints[SwipeBrickRules.ColorIndex(1)];
        _fx.AddChild(Shards.Create(center, tint, 6, 340f, SwipeBrickRules.Cell * _unit * 0.4f, 0.75f, _rng));
        _fx.AddChild(SpriteBurst.Create(_starTexture, center, 2, 240f, 20f, 0.5f, _rng, 0.9f));
        _fx.AddChild(new FloatingText { Text = "+1", Color = Colors.White, FontSize = 32, Position = TextSpot(center) });
        if (_board.Score % MilestoneEvery == 0)
        {
            Audio.I.Cheer();
            _shake.Add(3f);
            _fx.AddChild(SpriteBurst.Create(_starTexture, center, 8, 480f, 30f, 0.85f, _rng, 1.0f));
            _buddy.Say(Cheers[_rng.Next(Cheers.Length)]);
            _buddy.Hop(30f);
            Ui.Punch(_hud.ScoreLabel, 0.6f);
        }
        else
        {
            _buddy.Hop(14f);
        }
    }

    /// <summary>A bubble popped: little balls fly to the rabbit and "공 +1".</summary>
    private void ItemTaken(SwipeEvent e)
    {
        Audio.I.Coin();
        var at = _field.Position + _field.CellCenter(e.Col, e.Row);
        _fx.AddChild(Burst.Create(at, new Color(0.7f, 0.92f, 1f), 10, 260f, 5f, 0f, 0.4f, _rng));
        _fx.AddChild(new FloatingText { Text = "공 +1", Color = UiTheme.Yellow, FontSize = 32, Position = TextSpot(at) });
        _hud.SetBalls(_board.Balls, true);
        _bestBalls = Mathf.Max(_bestBalls, _board.Balls);
        var radius = SwipeBrickRules.BallRadius * _unit;
        for (var i = 0; i < 2; i++)
        {
            var ball = new Sprite2D { Texture = BrickArt.Ball, Position = at };
            var s = radius * 1.4f / BrickArt.Ball.GetHeight();
            ball.Scale = new Vector2(s, s);
            _fx.AddChild(ball);
            var to = _launcher.Position + new Vector2((i - 0.5f) * radius * 2, 0);
            var mid = (at + to) / 2 + new Vector2((i - 0.5f) * _unit * 0.12f, -_unit * 0.08f);
            var tween = ball.CreateTween();
            tween.TweenMethod(Callable.From<float>(t =>
            {
                ball.Position = at.Lerp(mid, t).Lerp(mid.Lerp(to, t), t);
            }), 0f, 1f, 0.55f + i * 0.08f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
            tween.TweenCallback(Callable.From(ball.QueueFree));
        }
    }

    /// <summary>Floating text start: low enough near the top edge that it does not rise off the screen.</summary>
    private Vector2 TextSpot(Vector2 at) => new(at.X, Mathf.Max(at.Y - 8f, 70f - _boardNode.Position.Y));

    private void BallLanded(SwipeShot shot, SwipeEvent e)
    {
        _balls.Landed(e.Ball);
        if (_landSeen)
            return;
        // The first ball to land sets the next launch spot: the rabbit hops there
        _landSeen = true;
        _backdrop.FloorBounce = 1f;
        Audio.I.Plop();
        _launchX = shot.FirstLandX ?? _launchX;
        _launcher.MoveTo(Px(new Vec(_launchX, 0)).X, true);
    }

    private void TurnOnFast()
    {
        if (_state != State.Flying || _fast)
            return;
        _fast = true;
        _hud.SetFast(true, true);
        Audio.I.Whoosh();
    }

    // ---- Between shots ----

    private void ShotFinished()
    {
        _state = State.Settling;
        _shot = null;
        _hud.SetFast(false, false);
        _launcher.Holding = true;
        _launcher.SetBalls(_board.Balls);
        var tween = CreateTween();
        tween.TweenInterval(0.3f);
        if (_board.ShotsLeft <= 0)
        {
            tween.TweenCallback(Callable.From(EndRound));
            return;
        }
        tween.TweenCallback(Callable.From(NextRow));
    }

    /// <summary>The rows bounce down one step; whatever reaches the flower line turns into flowers; a new row comes in.</summary>
    private void NextRow()
    {
        _balls.Clear();
        _flowers.Clear();
        _board.ShiftDown(_flowers);
        _board.SpawnRow(_rng);
        _field.ResetEffects();
        if (_flowers.Count > 0)
        {
            var any = false;
            foreach (var (col, brick) in _flowers)
            {
                var at = Px(new Vec(SwipeBrickRules.CellCenter(col, 0).X, SwipeBrickRules.FloorY - SwipeBrickRules.Cell * 0.35f));
                _fx.AddChild(FlowerPop.Create(at, SwipeBrickRules.Cell * _unit * (brick ? 0.95f : 0.6f), col + _board.Turn));
                any |= brick;
            }
            Audio.I.Plop();
            _backdrop.FloorBounce = 1f;
            if (any)
                _buddy.Say("꽃이 됐다!", 1.0f);
        }

        _field.Position = new Vector2(0, -SwipeBrickRules.Cell * _unit);
        Audio.I.Whoosh();
        _rowTween?.Kill();
        _rowTween = CreateTween();
        _rowTween.TweenProperty(_field, "position", Vector2.Zero, 0.5f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _rowTween.TweenCallback(Callable.From(() =>
        {
            if (_state == State.Settling)
                BeginAiming();
        }));
    }

    // ---- End ----

    /// <summary>All remaining bricks turn into flowers one after another, a fanfare, then "끝!".</summary>
    private void EndRound()
    {
        _state = State.Ending;
        _balls.Clear();
        _launcher.Holding = false;
        _buddy.Hop(40f);
        _buddy.Say("잘했어!", 1.6f);
        var cells = new List<(int Col, int Row, bool Brick)>();
        for (var row = SwipeBrickRules.Rows - 1; row >= 0; row--)
            for (var col = 0; col < SwipeBrickRules.Columns; col++)
                if (_board.HitsAt(col, row) > 0 || _board.ItemAt(col, row))
                    cells.Add((col, row, _board.HitsAt(col, row) > 0));

        var tween = CreateTween();
        var gap = Mathf.Clamp(1.2f / Mathf.Max(1, cells.Count), 0.03f, 0.09f);
        var i = 0;
        foreach (var (col, row, brick) in cells)
        {
            var index = i++;
            tween.TweenCallback(Callable.From(() =>
            {
                var at = _field.CellCenter(col, row);
                if (brick)
                    _board.SetBrick(col, row, 0);
                else
                    _board.SetItem(col, row, false);
                _fx.AddChild(FlowerPop.Create(at, SwipeBrickRules.Cell * _unit * (brick ? 0.95f : 0.6f), col + row));
                if (index % 3 == 0)
                    Audio.I.Pop(Mathf.Min(index / 3, 8));
            }));
            tween.TweenInterval(gap);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            Audio.I.Fanfare();
            App.Vibrate(30);
            _shake.Add(4f);
            _ui.AddChild(Confetti.Create(ScreenSize, 70));
        }));
        tween.TweenInterval(0.6f);
        tween.TweenCallback(Callable.From(() => _hud.ShowDone(ShowResult)));
    }

    private void ShowResult()
    {
        _state = State.Result;
        var score = _board.Score;
        _hud.Visible = false;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = SwipeBrickRules.VariantId(_level),
            Caption = "깬 벽돌",
            Value = score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Detail = $"공 {_bestBalls}개까지 모았어요",
            Entry = new ScoreEntry("", score, 0, App.Now()),
            Recordable = score > 0,
            NotRecordableMessage = "다음엔 벽돌을 꼭 깨 보자!",
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("꾸미기", UiTheme.Blue, ShowSetup),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    // ---- Input: press on the board and drag to aim toward the finger, release to fire; ← → and Space ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state != State.Aiming || App.I.Autoplay)
            return;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } touch when OnBoard(touch.Position):
                _aiming = true;
                AimAt(touch.Position);
                break;
            case InputEventScreenDrag drag when _aiming:
                AimAt(drag.Position);
                break;
            case InputEventScreenTouch { Pressed: false } release when _aiming:
                AimAt(release.Position);
                _aiming = false;
                if (_aimDir is { } d && SwipeBrickRules.CanFire(d))
                    Fire();
                else
                {
                    _aimDir = null;
                    UpdateAimGuide();
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key
                when key.PhysicalKeycode is Key.Space or Key.Enter or Key.KpEnter or Key.Up or Key.W:
                _aimDir ??= SwipeBrickRules.DirectionFromAngle(_aimAngle);
                Fire();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>Presses count on the board (the HUD columns at the sides have their own buttons).</summary>
    private bool OnBoard(Vector2 screen)
    {
        var p = ToBoard(screen);
        return p.X >= -0.02f && p.X <= SwipeBrickRules.Width + 0.02f;
    }

    private void AimAt(Vector2 screen)
    {
        _aimDir = SwipeBrickRules.AimToward(_launchX, ToBoard(screen)) ?? _aimDir;
        if (_aimDir is { } d && SwipeBrickRules.CanFire(d))
            _aimAngle = SwipeBrickRules.AngleOf(d);
        UpdateAimGuide();
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>Sweeps the aim line over to the planned angle (the one that breaks the most), then fires.</summary>
    private void AutoAim(float dt)
    {
        if (_autoTarget is not { } target)
            return;
        _autoTimer += dt;
        if (_autoTimer < 0.25f)
            return;
        _aimAngle = Mathf.MoveToward(_aimAngle, target, 160f * dt);
        _aimDir = SwipeBrickRules.DirectionFromAngle(_aimAngle);
        UpdateAimGuide();
        if (Mathf.IsEqualApprox(_aimAngle, target) && _autoTimer > 0.9f)
        {
            _aimDir = SwipeBrickRules.DirectionFromAngle(target);
            Fire();
        }
    }
}
