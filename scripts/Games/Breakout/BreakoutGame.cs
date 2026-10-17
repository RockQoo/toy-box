using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;
using Vec = System.Numerics.Vector2;

namespace ToyBox.Games.Breakout;

/// <summary>
/// 벽돌깨기 (docs/games/10-breakout.md).
/// Flow: Setup (배경, 응원 친구, 속도) → Countdown → the ball rests on the cloud paddle and sets off after 1s (or a tap)
/// → 60s of play → "끝!" → shared ResultPanel. A tall board sits in the middle of the screen (HUD and buddy at the sides).
/// A missed ball lands on the flower line and flies back to the paddle as a bubble (no lives, no penalty). Clearing a
/// board brings the next shape down from the top. Now and then a broken brick drops 공 늘리기 (the ball splits in three).
/// The paddle follows a finger (or a mouse drag) anywhere on the screen, or ← → / A D.
/// </summary>
public partial class BreakoutGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Ending, Result }

    private sealed class LiveBall
    {
        public BreakoutBall Model;
        public required BallSprite View;
    }

    private sealed class FallingItem
    {
        public Vec Position;
        public required Sprite2D View;
        public float Time;
    }

    private const string GameId = "breakout";
    private const string SettingsSection = "breakout";
    private const string ScoreIcon = "res://assets/sprites/brick/brick_1.png";
    private const int MilestoneEvery = 10;
    private const float LeftReserve = 410f;   // RoundHud's score panel and "그만하기" on the left
    private const float RightReserve = 330f;  // Time panel and the buddy on the right
    private const float SetupChipHeight = 88f;
    private const float KeySpeed = 1.2f;      // Board widths per second with the arrow keys
    private const float PaddleFollow = 18f;   // How quickly the paddle catches up with the finger (1/s)
    private const float ReturnTime = 0.6f;    // A missed ball's bubble flight back to the paddle

    private static readonly string[] PhaseBanners = { "", "빨라져요!", "더 빨라져요!" };
    private static readonly string[] Cheers = { "최고야!", "와르르!", "멋져요!", "잘한다!", "우와!" };
    private static readonly string[] MissLines = { "다시 퐁!", "괜찮아!", "한 번 더!" };

    private readonly Random _rng = new();
    private readonly List<LiveBall> _balls = new();
    private readonly List<BallHit> _hits = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _boardNode = null!;
    private BoardBackdrop _backdrop = null!;
    private Node2D _brickHolder = null!;
    private BrickField _bricks = null!;
    private Node2D _itemLayer = null!;
    private CloudPaddle _paddle = null!;
    private Node2D _ballLayer = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private RoundHud _hud = null!;
    private Label _clearLabel = null!;
    private Control? _setup;
    private Texture2D _starTexture = null!;

    private float _unit = 720f;       // Pixels per board height
    private StartSpeed _speed;
    private BreakoutBoard _board = new(0);
    private float _elapsed;
    private int _phaseIndex;
    private int _score;
    private int _boardsCleared;
    private int _streak;
    private float _paddleX = BreakoutRules.Width / 2;
    private float _paddleTarget = BreakoutRules.Width / 2;
    private bool _parked;             // The one ball rests on the paddle
    private float _launchTimer;
    private bool _arriving;           // A new board is sliding in (no brick collisions yet)
    private bool _clearing;           // "다 깼다!" between two boards
    private Tween? _boardTween;
    private BallSprite? _returning;   // A missed ball flying back as a bubble
    private Vector2 _returnFrom;
    private float _returnT;
    private FallingItem? _item;

    // Autoplay
    private float _autoOffset;
    private bool _autoMiss;
    private float _nextAutoMiss;

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
        _backdrop = new BoardBackdrop();
        _boardNode.AddChild(_backdrop);
        _brickHolder = new Node2D();
        _boardNode.AddChild(_brickHolder);
        _bricks = new BrickField();
        _brickHolder.AddChild(_bricks);
        _itemLayer = new Node2D();
        _boardNode.AddChild(_itemLayer);
        _paddle = new CloudPaddle();
        _boardNode.AddChild(_paddle);
        _ballLayer = new Node2D();
        _boardNode.AddChild(_ballLayer);
        _fx = new Node2D();
        _boardNode.AddChild(_fx);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _speed = (StartSpeed)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "speed", (int)StartSpeed.Normal), 0, 2);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        _hud = RoundHud.Create(BreakoutRules.PlaySeconds, ScoreIcon);
        _ui.AddChild(_hud);
        _clearLabel = Ui.Label("다 깼다!", 84, UiTheme.Yellow, 18, UiTheme.Ink);
        _clearLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _clearLabel.VerticalAlignment = VerticalAlignment.Center;
        _clearLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _clearLabel.Visible = false;
        _ui.AddChild(_clearLabel);

        Layout();
        GetViewport().SizeChanged += Layout;
        // A 60-second round is long for a headless smoke test (the test stops after 8000 frames): double speed there
        // only. Movie capture and real play keep normal time.
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 2.0;
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    // ---- Layout: a tall board in the middle, kept clear of the HUD panels at both sides ----

    private void Layout()
    {
        var screen = ScreenSize;
        var avail = Mathf.Max(200f, screen.X - LeftReserve - RightReserve);
        _unit = Mathf.Floor(Mathf.Min(screen.Y, avail / BreakoutRules.Width));
        var boardWidth = BreakoutRules.Width * _unit;
        var x = Mathf.Clamp(screen.X / 2 - boardWidth / 2, LeftReserve, Mathf.Max(LeftReserve, screen.X - RightReserve - boardWidth));
        _boardNode.Position = new Vector2(x, screen.Y - _unit).Round();

        _backdrop.Unit = _unit;
        _bricks.Unit = _unit;
        _bricks.QueueRedraw();
        _paddle.Unit = _unit;
        foreach (var ball in _balls)
            ball.View.Radius = BreakoutRules.BallRadius * _unit;
        if (_item is not null)
            _item.View.Scale = ItemScale();

        _buddy.Position = new Vector2(screen.X - RightReserve / 2, screen.Y - 20);
        _clearLabel.Position = _boardNode.Position + new Vector2(0, _unit * 0.42f);
        _clearLabel.Size = new Vector2(boardWidth, 120);
        _clearLabel.PivotOffset = _clearLabel.Size / 2;
        SyncViews();
    }

    private Vector2 Px(Vec v) => new(v.X * _unit, v.Y * _unit);

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _hud.Visible = false;
        _clearLabel.Visible = false;
        ResetBoard();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/breakout.png", 64));
        titleRow.AddChild(Ui.Label("벽돌깨기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        var sceneryRow = ChoiceRow.Create("배경", Scenery.Choices, (int)_scenery.Kind, chipHeight: SetupChipHeight);
        sceneryRow.Selected += i =>
        {
            _scenery.Kind = (SceneryKind)i;
            App.I.SetSetting(SettingsSection, "scenery", i);
        };
        body.AddChild(sceneryRow);

        var buddyRow = ChoiceRow.Create("응원 친구", Buddy.Choices, (int)App.I.GetSetting(SettingsSection, "buddy", 0), 110,
            chipHeight: SetupChipHeight);
        buddyRow.Selected += i =>
        {
            _buddy.SetCharacter(i);
            _buddy.Say("같이 하자!", 0.9f);
            App.I.SetSetting(SettingsSection, "buddy", i);
        };
        body.AddChild(buddyRow);

        var speedRow = ChoiceRow.Create("속도", Choice.Speeds, (int)_speed, chipHeight: SetupChipHeight);
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

    /// <summary>The first board, the paddle in the middle and a ball resting on it.</summary>
    private void ResetBoard()
    {
        _boardTween?.Kill();
        _boardTween = null;
        ClearBalls();
        ClearItem();
        _board = new BreakoutBoard(0);
        _bricks.Show(_board);
        _brickHolder.Position = Vector2.Zero;
        _arriving = false;
        _clearing = false;
        _paddleX = _paddleTarget = BreakoutRules.Width / 2;
        Park();
        SyncViews();
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        ResetBoard();

        _elapsed = 0;
        _phaseIndex = 0;
        _score = 0;
        _boardsCleared = 0;
        _streak = 0;
        _autoOffset = 0.3f;
        _autoMiss = false;
        _nextAutoMiss = 14f;
        _hud.Reset();
        _hud.Visible = true;
        _clearLabel.Visible = false;
        _state = State.Countdown;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the last phase's tempo

        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown)
                return;
            _state = State.Playing;
            _launchTimer = BreakoutRules.LaunchDelay;
        };
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (_state is State.Countdown or State.Playing)
            MovePaddle(dt);
        if (_state == State.Playing)
            UpdatePlaying(dt);
        SyncViews();
    }

    private void UpdatePlaying(float dt)
    {
        _elapsed += dt;
        _hud.SetElapsed(_elapsed);

        var phase = BreakoutRules.PhaseAt(_elapsed);
        if (phase.Index != _phaseIndex)
        {
            _phaseIndex = phase.Index;
            _hud.ShowBanner(PhaseBanners[phase.Index]);
            Audio.I.SetMusicTempo(1f + 0.05f * phase.Index);
            foreach (var ball in _balls)
                ball.View.Glow = true;
        }

        if (_parked)
        {
            if (CanLaunch)
            {
                _launchTimer -= dt;
                if (_launchTimer <= 0)
                    Launch();
            }
        }
        else
        {
            MoveBalls(BreakoutRules.BallSpeed(_elapsed, _speed) * dt);
        }
        UpdateReturn(dt);
        UpdateItem(dt);

        if (_elapsed >= BreakoutRules.PlaySeconds)
            EndRound();
    }

    private void MoveBalls(float distance)
    {
        for (var i = _balls.Count - 1; i >= 0; i--)
        {
            var ball = _balls[i];
            _hits.Clear();
            BreakoutRules.Advance(ref ball.Model, distance, _arriving || _clearing ? null : _board, _paddleX, _hits);
            foreach (var hit in _hits)
            {
                switch (hit.Kind)
                {
                    case BallHitKind.Wall or BallHitKind.Ceiling:
                        Audio.I.Step(_rng.Next(2) == 0);
                        _fx.AddChild(Burst.Create(Px(hit.At), Colors.White, 4, 120f, 3f, 0f, 0.22f, _rng));
                        break;
                    case BallHitKind.Paddle:
                        PaddleBounce(hit);
                        break;
                    case BallHitKind.Brick:
                        BrickHit(hit);
                        break;
                    case BallHitKind.Floor:
                        _balls.RemoveAt(i);
                        Miss(ball);
                        break;
                }
                if (_clearing)
                    return; // The board was cleared: every ball is gone
            }
        }
    }

    private bool CanLaunch => _state == State.Playing && _parked && _balls.Count > 0 && !_arriving && !_clearing;

    private void Launch()
    {
        if (!CanLaunch)
            return;
        _parked = false;
        var ball = _balls[0];
        ball.Model.Direction = BreakoutRules.LaunchDirection(_rng.NextDouble());
        ball.View.Trail = true;
        Audio.I.Jump();
        _paddle.Squash(0.6f);
    }

    /// <summary>One ball resting on the paddle (it follows the paddle until it sets off).</summary>
    private void Park()
    {
        ClearBalls();
        var view = NewBallView();
        view.Trail = false;
        _balls.Add(new LiveBall { Model = new BreakoutBall(BreakoutRules.RestPosition(_paddleX), new Vec(0, -1)), View = view });
        _parked = true;
        _launchTimer = BreakoutRules.LaunchDelay;
    }

    private BallSprite NewBallView()
    {
        var view = new BallSprite { Radius = BreakoutRules.BallRadius * _unit, Glow = _phaseIndex > 0 };
        _ballLayer.AddChild(view);
        return view;
    }

    private void ClearBalls()
    {
        foreach (var ball in _balls)
            ball.View.QueueFree();
        _balls.Clear();
        _returning?.QueueFree();
        _returning = null;
        _parked = false;
    }

    // ---- Hits ----

    private void PaddleBounce(BallHit hit)
    {
        _streak = 0;
        _paddle.Squash();
        Audio.I.WallBump();
        _fx.AddChild(Burst.Create(Px(hit.At), new Color(0.85f, 0.93f, 1f), 6, 160f, 4f, 0f, 0.3f, _rng));
        if (App.I.Autoplay)
            _autoOffset = (float)(_rng.NextDouble() * 1.4 - 0.7);
    }

    private void BrickHit(BallHit hit)
    {
        var brick = hit.Brick!;
        _bricks.Hit(brick);
        var center = _brickHolder.Position + Px(brick.Center);
        if (!hit.Broke)
        {
            // Only a level down: a crack, a little shake and a "똑"
            Audio.I.Click();
            _fx.AddChild(Burst.Create(Px(hit.At), BrickArt.Tints[brick.ColorIndex], 5, 160f, 4f, 0f, 0.25f, _rng));
            return;
        }

        _score++;
        _streak++;
        _hud.SetScore(_score, 0.3f);
        Audio.I.PassTick(_streak); // "땡", higher for each brick in a row before the ball comes back to the paddle
        App.Vibrate(12);
        _buddy.Hop(18f);
        var tint = BrickArt.Tints[BreakoutRules.ColorIndex(1, brick.Row)];
        _fx.AddChild(Shards.Create(center, tint, 7, 360f, brick.H * _unit * 0.55f, 0.8f, _rng));
        _fx.AddChild(SpriteBurst.Create(_starTexture, center, 2, 260f, 22f, 0.55f, _rng, 0.9f));
        _fx.AddChild(new FloatingText { Text = "+1", Color = Colors.White, FontSize = 36, Position = center + new Vector2(0, -10) });

        if (_score % MilestoneEvery == 0)
        {
            Audio.I.Cheer();
            _shake.Add(3f);
            _fx.AddChild(SpriteBurst.Create(_starTexture, center, 8, 520f, 34f, 0.9f, _rng, 1.0f));
            _buddy.Say(Cheers[_rng.Next(Cheers.Length)]);
            Ui.Punch(_hud.ScoreLabel, 0.6f);
        }

        if (_board.Cleared)
        {
            BoardCleared();
            return;
        }
        if (BreakoutRules.DropsItem(_balls.Count, _item is not null, _rng.NextDouble()))
            DropItem(brick.Center);
    }

    /// <summary>A ball reached the flower line: "퐁", and if it was the last one it flies back to the paddle as a bubble.</summary>
    private void Miss(LiveBall ball)
    {
        _autoMiss = false;
        var at = ball.View.Position;
        Audio.I.Plop();
        _backdrop.FloorBounce = 1f;
        _fx.AddChild(Burst.Create(at, new Color(0.75f, 0.95f, 1f), 8, 200f, 5f, 0f, 0.4f, _rng));
        if (_balls.Count > 0 || _clearing)
        {
            // Other balls are still in play (or the board is changing): this one just pops
            _fx.AddChild(SpriteBurst.Create(_starTexture, at, 3, 220f, 20f, 0.5f, _rng));
            ball.View.QueueFree();
            return;
        }
        Audio.I.Whoosh();
        ball.View.Trail = false;
        _returning = ball.View;
        _returnFrom = at;
        _returnT = 0;
        if (_rng.Next(3) == 0)
            _buddy.Say(MissLines[_rng.Next(MissLines.Length)], 0.9f);
    }

    /// <summary>The bubble arcs up and lands on the paddle, wherever the paddle is by then.</summary>
    private void UpdateReturn(float dt)
    {
        if (_returning is not { } view)
            return;
        _returnT = Mathf.Min(1f, _returnT + dt / ReturnTime);
        var to = Px(BreakoutRules.RestPosition(_paddleX));
        var control = (_returnFrom + to) / 2 + new Vector2(0, -_unit * 0.25f);
        var t = _returnT;
        view.Position = _returnFrom.Lerp(control, t).Lerp(control.Lerp(to, t), t);
        var bubble = 0.6f + 0.4f * t;
        view.Scale = new Vector2(bubble, bubble) * (1 + 0.08f * Mathf.Sin(t * 18f));
        view.Modulate = Colors.White with { A = 0.65f + 0.35f * t };
        if (_returnT < 1f)
            return;
        view.QueueFree();
        _returning = null;
        Park();
        _paddle.Squash(0.5f);
        Audio.I.Catch(1, -8f);
    }

    // ---- Board cleared ----

    private void BoardCleared()
    {
        _clearing = true;
        _boardsCleared++;
        _streak = 0;
        Audio.I.Fanfare();
        App.Vibrate(30);
        _shake.Add(5f);
        _ui.AddChild(Confetti.Create(ScreenSize, 70));
        _buddy.Say("다 깼다!", 1.6f);
        _buddy.Hop(40f);

        // Balls and the item pop into stars
        foreach (var ball in _balls)
        {
            _fx.AddChild(SpriteBurst.Create(_starTexture, ball.View.Position, 4, 300f, 24f, 0.7f, _rng));
            ball.View.QueueFree();
        }
        _balls.Clear();
        _parked = false;
        ClearItem();

        _clearLabel.Visible = true;
        _clearLabel.Modulate = Colors.White;
        _clearLabel.Scale = new Vector2(0.3f, 0.3f);
        _boardTween?.Kill();
        _boardTween = CreateTween();
        _boardTween.TweenProperty(_clearLabel, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _boardTween.TweenInterval(0.8f);
        _boardTween.TweenProperty(_clearLabel, "modulate:a", 0f, 0.25f);
        _boardTween.TweenCallback(Callable.From(NextBoard));
    }

    /// <summary>The next shape comes down from above the screen; the ball sets off once it is in place.</summary>
    private void NextBoard()
    {
        _clearLabel.Visible = false;
        if (_state != State.Playing)
            return;
        _clearing = false;
        _arriving = true;
        _board = new BreakoutBoard(_board.Number + 1);
        _bricks.Show(_board);
        var lowest = _board.Bricks.Max(b => b.Y + b.H);
        _brickHolder.Position = new Vector2(0, -(lowest * _unit + _boardNode.Position.Y + 20));
        if (_returning is null)
            Park();
        Audio.I.Whoosh();
        _boardTween = CreateTween();
        _boardTween.TweenProperty(_brickHolder, "position", Vector2.Zero, 0.7f)
            .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _boardTween.TweenCallback(Callable.From(() =>
        {
            _arriving = false;
            _launchTimer = BreakoutRules.LaunchDelay;
            _buddy.Say("새 판이다!", 0.9f);
        }));
    }

    // ---- 공 늘리기 item ----

    private Vector2 ItemScale()
    {
        var tex = BrickArt.ItemBallAdd;
        var s = BreakoutRules.ItemSize * _unit * 1.25f / tex.GetHeight();
        return new Vector2(s, s);
    }

    private void DropItem(Vec at)
    {
        var view = new Sprite2D { Texture = BrickArt.ItemBallAdd, Scale = ItemScale() };
        _itemLayer.AddChild(view);
        _item = new FallingItem { Position = at, View = view };
        Audio.I.QuestionChime();
    }

    private void UpdateItem(float dt)
    {
        if (_item is not { } item)
            return;
        item.Time += dt;
        item.Position += new Vec(0, BreakoutRules.ItemFallSpeed * dt);
        if (BreakoutRules.PaddleCatches(item.Position.X, item.Position.Y, _paddleX))
        {
            CatchItem(item);
            return;
        }
        if (item.Position.Y - BreakoutRules.ItemSize / 2 > BreakoutRules.FloorY)
        {
            // Missed: it just fades into the flowers
            var view = item.View;
            var tween = view.CreateTween();
            tween.TweenProperty(view, "modulate:a", 0f, 0.3f);
            tween.TweenCallback(Callable.From(view.QueueFree));
            _item = null;
        }
    }

    private void CatchItem(FallingItem item)
    {
        var at = Px(item.Position);
        ClearItem();
        Audio.I.Coin();
        _paddle.Squash(1.2f);
        _buddy.Say("공이 셋!", 1.0f);
        _fx.AddChild(SpriteBurst.Create(_starTexture, at, 6, 380f, 28f, 0.8f, _rng, 0.9f));
        if (_clearing)
            return;

        // Split the highest ball in play (or send the resting one off first)
        Launch();
        if (_parked || _balls.Count == 0)
            return;
        var source = _balls.OrderBy(b => b.Model.Position.Y).First();
        var dir = source.Model.Direction.Y > 0 ? new Vec(source.Model.Direction.X, -source.Model.Direction.Y) : source.Model.Direction;
        source.Model.Direction = BreakoutRules.ClampAngle(dir);
        var (left, right) = BreakoutRules.SplitDirections(source.Model.Direction);
        foreach (var d in new[] { left, right })
        {
            var view = NewBallView();
            view.Position = source.View.Position;
            _balls.Add(new LiveBall { Model = new BreakoutBall(source.Model.Position, d), View = view });
        }
        _fx.AddChild(Burst.Create(source.View.Position, UiTheme.Yellow, 10, 260f, 5f, 0f, 0.4f, _rng));
    }

    private void ClearItem()
    {
        _item?.View.QueueFree();
        _item = null;
    }

    // ---- Views ----

    private void SyncViews()
    {
        _paddle.Position = Px(new Vec(_paddleX, BreakoutRules.PaddleY));
        if (_parked && _balls.Count > 0)
            _balls[0].Model.Position = BreakoutRules.RestPosition(_paddleX);
        foreach (var ball in _balls)
            ball.View.Position = Px(ball.Model.Position);
        if (_item is { } item)
        {
            item.View.Position = Px(item.Position);
            item.View.Rotation = 0.25f * Mathf.Sin(item.Time * 5f);
        }
    }

    // ---- End ----

    private void EndRound()
    {
        _state = State.Ending;
        _elapsed = BreakoutRules.PlaySeconds;
        _boardTween?.Kill();
        _clearLabel.Visible = false;
        foreach (var ball in _balls)
            FadeOut(ball.View);
        _balls.Clear();
        if (_returning is not null)
            FadeOut(_returning);
        _returning = null;
        _parked = false;
        if (_item is not null)
            FadeOut(_item.View);
        _item = null;
        Audio.I.Whoosh();
        _buddy.Hop();
        _hud.ShowTimeUp(ShowResult);
    }

    private static void FadeOut(Node2D node)
    {
        var tween = node.CreateTween();
        tween.TweenProperty(node, "modulate:a", 0f, 0.4f);
        tween.TweenCallback(Callable.From(node.QueueFree));
    }

    private void ShowResult()
    {
        _state = State.Result;
        var score = _score;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            Caption = "깬 벽돌",
            Value = score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Detail = $"다 깬 판 {_boardsCleared}개",
            Entry = new ScoreEntry("", score, 0, App.Now()),
            Recordable = score > 0,
            NotRecordableMessage = "다음엔 벽돌을 꼭 깨 보자!",
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

    // ---- Input: the paddle follows a finger anywhere (or the arrow keys); a tap sends a resting ball off ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state is not (State.Countdown or State.Playing) || App.I.Autoplay)
            return;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } touch:
                AimAt(touch.Position.X);
                if (CanLaunch)
                    Launch();
                break;
            case InputEventScreenDrag drag:
                AimAt(drag.Position.X);
                break;
            case InputEventKey { Pressed: true, Echo: false } key
                when key.PhysicalKeycode is Key.Space or Key.Up or Key.W or Key.Enter:
                if (CanLaunch)
                    Launch();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void AimAt(float screenX) =>
        _paddleTarget = BreakoutRules.ClampPaddle((screenX - _world.Position.X - _boardNode.Position.X) / _unit);

    private void MovePaddle(float dt)
    {
        var dir = 0f;
        if (Input.IsActionPressed("ui_left") || Input.IsPhysicalKeyPressed(Key.A))
            dir -= 1;
        if (Input.IsActionPressed("ui_right") || Input.IsPhysicalKeyPressed(Key.D))
            dir += 1;
        if (dir != 0 && !App.I.Autoplay)
            _paddleTarget = BreakoutRules.ClampPaddle(_paddleTarget + dir * KeySpeed * BreakoutRules.Width * dt);
        if (App.I.Autoplay && _state == State.Playing)
            AutoAim();
        _paddleTarget = BreakoutRules.ClampPaddle(_paddleTarget);
        _paddleX += (_paddleTarget - _paddleX) * (1 - Mathf.Exp(-PaddleFollow * dt));
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// Heads for where the next falling ball will reach the paddle (bouncing off the walls), hitting it a little off
    /// center so the angle varies, or for a falling item when no ball is near. Now and then it stays away on purpose so
    /// the missed-ball path (bubble back to the paddle) runs too.
    /// </summary>
    private void AutoAim()
    {
        float? land = null;
        var soonest = float.MaxValue;
        foreach (var ball in _balls)
        {
            if (_parked || BreakoutRules.PredictX(ball.Model.Position, ball.Model.Direction, BreakoutRules.PaddleY) is not { } x)
                continue;
            var time = (BreakoutRules.PaddleY - ball.Model.Position.Y) / ball.Model.Direction.Y;
            if (time < soonest)
            {
                soonest = time;
                land = x;
            }
        }
        if (!_autoMiss && _elapsed >= _nextAutoMiss && _balls.Count == 1 && land is not null && soonest > 0.35f)
        {
            _autoMiss = true;
            _nextAutoMiss = _elapsed + 20f;
        }

        if (land is { } l)
        {
            _paddleTarget = _autoMiss
                ? (l < BreakoutRules.Width / 2 ? BreakoutRules.Width : 0)
                : l - _autoOffset * BreakoutRules.PaddleHitHalf;
        }
        else if (_item is { } item)
        {
            _paddleTarget = item.Position.X;
        }
    }
}
