using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Snake;

/// <summary>
/// 애벌레 냠냠 (docs/games/08-snake.md).
/// Flow: Setup (배경, 응원 친구, 속도) → Countdown → Play (60s; the caterpillar waits for the first direction) →
/// time's up: it curls up smiling, "끝!" → shared ResultPanel.
/// Turn with arrow keys / WASD, a swipe on the field or the big buttons at the bottom right. Walls stop it ("깜짝",
/// then it waits for a new direction) and it crawls over its own body, so nothing ends a round early.
/// </summary>
public partial class SnakeGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Ending, Result }

    private const string GameId = "snake";
    private const string SettingsSection = "snake";
    private const int MilestoneEvery = 10;
    private const float StreakWindow = 2.5f;
    private const float Margin = 18f;
    private const float TopBar = 132f;     // Room for the RoundHud's top row
    private const float PadButton = 88f;
    private const float PadColumnGap = 26f;
    private const float SetupChipHeight = 88f;

    private static readonly string[] PhaseBanners = { "", "빨라져요!", "더 빨라져요!" };
    private static readonly string[] Cheers = { "최고야!", "냠냠 대장!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();
    private readonly Dictionary<MazeCell, LeafSprite> _leafSprites = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private SnakeField _field = null!;
    private Node2D _leafLayer = null!;
    private Caterpillar _caterpillar = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private RoundHud _hud = null!;
    private SnakePad _pad = null!;
    private Label _prompt = null!;
    private Control? _setup;
    private Texture2D _starTexture = null!;

    private StartSpeed _speed;
    private SnakeBoard _board = new();
    private float _elapsed;
    private float _stepTimer;
    private int _phaseIndex;
    private float _nextGoldenAt;
    private float _goldenUntil;
    private int _streak;
    private float _lastEatTime = -10f;
    private float _promptTime;

    // Input
    private Vector2? _swipeFrom;

    // Autoplay
    private float _autoDelay;
    private float _nextWallTry;
    private bool _wallTry;

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
        _field = new SnakeField { Visible = false };
        _world.AddChild(_field);
        _leafLayer = new Node2D();
        _field.AddChild(_leafLayer);
        _caterpillar = new Caterpillar();
        _field.AddChild(_caterpillar);
        _fx = new Node2D();
        _field.AddChild(_fx);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _speed = (StartSpeed)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "speed", (int)StartSpeed.Normal), 0, 2);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        _hud = RoundHud.Create(SnakeRules.PlaySeconds, "res://assets/sprites/snake/leaf.png");
        _ui.AddChild(_hud);
        _pad = SnakePad.Create(PadButton);
        _pad.Pressed += dir => Turn(dir, false);
        _ui.AddChild(_pad);
        _prompt = Ui.Label("방향을 눌러요!", 52, UiTheme.Yellow, 16, UiTheme.Ink);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.MouseFilter = Control.MouseFilterEnum.Ignore;
        _prompt.Visible = false;
        _ui.AddChild(_prompt);

        Layout();
        GetViewport().SizeChanged += Layout;
        // A 60-second round is long for a headless smoke test (it runs at about 140 frames per second, and the test
        // stops after 8000 frames): play it at double speed there. Movie capture and real play keep normal time.
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 2.0;
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    // ---- Layout: the wide field on the left, the buddy over the direction buttons on the right ----

    private void Layout()
    {
        var screen = ScreenSize;
        var padSize = _pad.GetCombinedMinimumSize();
        var fieldRight = screen.X - Margin - padSize.X - PadColumnGap;
        var areaHeight = screen.Y - TopBar - Margin;
        var cell = Mathf.Floor(Mathf.Min((fieldRight - Margin) / SnakeRules.Width, areaHeight / SnakeRules.Height));
        _field.Setup(SnakeRules.Width, SnakeRules.Height, cell);
        _field.Position = new Vector2(Margin + 6, TopBar + (areaHeight - _field.GridSize.Y) / 2).Round();
        _caterpillar.SetCellSize(cell);

        var columnLeft = _field.Position.X + _field.GridSize.X + 10;
        var columnCenter = (columnLeft + screen.X - Margin) / 2;
        _pad.Size = padSize;
        _pad.Position = new Vector2(columnCenter - padSize.X / 2, screen.Y - Margin - padSize.Y);
        _buddy.Position = new Vector2(columnCenter, _pad.Position.Y - 14);

        _prompt.Size = new Vector2(_field.GridSize.X, 80);
        _prompt.Position = _field.Position + new Vector2(0, _field.GridSize.Y * 0.18f);

        // Keep the caterpillar and leaves on their cells after a resize
        if (_board.Length > 0)
            _caterpillar.Reset(BodyPixels());
        foreach (var (cell2, leaf) in _leafSprites)
            leaf.Position = _field.CellCenter(cell2);
    }

    private List<Vector2> BodyPixels() => _board.Body.Select(_field.CellCenter).ToList();

    private Rect2 FieldRect() => new(_field.GlobalPosition, _field.GridSize);

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _hud.Visible = false;
        _pad.Visible = false;
        _prompt.Visible = false;
        _field.Visible = false;
        ClearLeaves();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/snake.png", 64));
        titleRow.AddChild(Ui.Label("애벌레 냠냠", 42, UiTheme.Orange, 12, UiTheme.Ink));
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

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        ClearLeaves();

        _board = new SnakeBoard();
        _elapsed = 0;
        _stepTimer = 0;
        _phaseIndex = 0;
        _streak = 0;
        _nextGoldenAt = SnakeRules.GoldenFromTime + 3f * (float)_rng.NextDouble();
        _goldenUntil = 0;
        _swipeFrom = null;
        _autoDelay = 0.7f;
        _nextWallTry = 14f;
        _wallTry = false;

        _field.Visible = true;
        _caterpillar.Reset(BodyPixels());
        _caterpillar.Crawling = false;
        _caterpillar.MouthOpen = false;
        _caterpillar.OverBody = false;
        AddLeaves(_board.FillLeaves(0, _rng.Next));
        _hud.Reset();
        _hud.Visible = true;
        _pad.Visible = true;
        _prompt.Visible = false;
        _state = State.Countdown;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the last phase's tempo

        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown)
                return;
            _state = State.Playing;
            _promptTime = 0;
        };
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        if (_state != State.Playing)
            return;
        var dt = (float)delta;
        _elapsed += dt;
        _hud.SetElapsed(_elapsed);

        // "방향을 눌러요!" blinks until the first move
        _prompt.Visible = !_board.Started;
        if (_prompt.Visible)
        {
            _promptTime += dt;
            _prompt.Modulate = Colors.White with { A = 0.55f + 0.45f * Mathf.Cos(_promptTime * 5f) };
        }

        var phase = SnakeRules.PhaseAt(_elapsed);
        if (phase.Index != _phaseIndex)
        {
            _phaseIndex = phase.Index;
            _hud.ShowBanner(PhaseBanners[phase.Index]);
            Audio.I.SetMusicTempo(1f + 0.05f * phase.Index);
        }

        UpdateLeaves();
        if (App.I.Autoplay)
            AutoPlay(dt);

        _stepTimer -= dt;
        if (_stepTimer <= 0)
            Step();
        UpdateMouth();

        if (_elapsed >= SnakeRules.PlaySeconds)
            EndRound();
    }

    private void Step()
    {
        var interval = SnakeRules.StepInterval(_elapsed, _speed);
        var step = _board.Step();
        switch (step.Kind)
        {
            case SnakeMoveKind.Waiting:
                _stepTimer = 0; // Respond the moment a direction comes in
                _caterpillar.Crawling = false;
                return;
            case SnakeMoveKind.Bumped:
                Bump();
                _stepTimer = interval;
                return;
        }
        // Keep a steady beat; a long frame does not make it skip cells
        _stepTimer = Mathf.Max(_stepTimer + interval, interval * 0.5f);
        _caterpillar.Crawling = true;
        _caterpillar.Advance(BodyPixels(), interval);
        _caterpillar.OverBody = _board.Body.Skip(1).Contains(_board.Head);
        if (step.Kind == SnakeMoveKind.Ate && step.Leaf is { } leaf)
            Eat(leaf, step.Points);
    }

    private void UpdateMouth()
    {
        var ahead = _board.Head.Step(_board.Facing);
        _caterpillar.MouthOpen = _board.Moving && _board.Leaves.Any(l => l.Cell == ahead);
    }

    private void Bump()
    {
        // Light: a little "깜짝" toward the wall and a soft "통". No penalty; it waits for a new direction.
        _caterpillar.Bump(_board.Facing);
        _caterpillar.Crawling = false;
        Audio.I.WallBump();
        App.Vibrate(10);
        var wall = _field.CellCenter(_board.Head) + Caterpillar.Dir(_board.Facing) * _field.CellSize * 0.55f;
        _fx.AddChild(Burst.Create(wall, Colors.White, 6, 160f, 4f, 0f, 0.3f, _rng));
        if (_rng.Next(3) == 0)
            _buddy.Say("깜짝!", 0.7f);
    }

    // ---- Leaves ----

    private void AddLeaves(IEnumerable<SnakeLeaf> leaves)
    {
        foreach (var leaf in leaves)
            AddLeafSprite(leaf);
    }

    private void AddLeafSprite(SnakeLeaf leaf)
    {
        var sprite = LeafSprite.Create(_field.CellSize, leaf.Golden, (float)_rng.NextDouble() * 6f);
        sprite.Position = _field.CellCenter(leaf.Cell);
        if (leaf.Golden)
            sprite.TimeLeft = SnakeRules.GoldenLife;
        _leafLayer.AddChild(sprite);
        _leafSprites[leaf.Cell] = sprite;
    }

    private void UpdateLeaves()
    {
        // A second leaf from 30 seconds
        AddLeaves(_board.FillLeaves(_elapsed, _rng.Next));

        // Golden leaf: now and then after 10 seconds, gone again after a few seconds
        if (_board.HasGolden && _elapsed >= _goldenUntil)
        {
            var golden = _board.Leaves.First(l => l.Golden);
            _board.RemoveGolden();
            if (_leafSprites.Remove(golden.Cell, out var sprite))
                sprite.FadeAway();
        }
        if (!_board.HasGolden && _elapsed >= _nextGoldenAt && _elapsed < SnakeRules.PlaySeconds - 3f)
        {
            if (_board.AddLeaf(true, _rng.Next) is { } golden)
            {
                AddLeafSprite(golden);
                Audio.I.QuestionChime();
            }
            _goldenUntil = _elapsed + SnakeRules.GoldenLife;
            _nextGoldenAt = _goldenUntil + SnakeRules.GoldenDelay(_rng.NextDouble());
        }
    }

    private void Eat(SnakeLeaf leaf, int points)
    {
        var at = _field.CellCenter(leaf.Cell);
        if (_leafSprites.Remove(leaf.Cell, out var sprite))
            sprite.EatenBy(at);
        _caterpillar.Eat();

        var now = _elapsed;
        _streak = now - _lastEatTime <= StreakWindow ? _streak + 1 : 1;
        _lastEatTime = now;
        Audio.I.Catch(_streak); // "냠", higher for quick eats in a row
        App.Vibrate(leaf.Golden ? 25 : 12);

        var before = _board.Score - points;
        _hud.SetScore(_board.Score, leaf.Golden ? 0.5f : 0.3f);
        _buddy.Hop();
        var leafColor = new Color(0.45f, 0.78f, 0.25f);
        _fx.AddChild(Burst.Create(at, leaf.Golden ? UiTheme.Gold : leafColor, 8, 260f, 5f, 0f, 0.35f, _rng));

        if (leaf.Golden)
        {
            Audio.I.Coin();
            _fx.AddChild(SpriteBurst.Create(_starTexture, at, 6, 420f, 30f, 0.8f, _rng, 0.9f));
            _fx.AddChild(new FloatingText { Text = "+3", Color = UiTheme.Gold, FontSize = 54, Position = at + new Vector2(0, -30) });
            _buddy.Say("반짝반짝!", 0.9f);
        }
        else
        {
            _fx.AddChild(new FloatingText
            {
                Text = "+1",
                Color = Colors.White,
                FontSize = 40,
                Position = at + new Vector2(0, -24),
            });
        }

        if (SnakeRules.CrossedMilestone(before, _board.Score, MilestoneEvery))
        {
            // Every 10: chime, a rainbow wave down the body, a star shower and a cheer
            Audio.I.Cheer();
            _caterpillar.Rainbow();
            _shake.Add(3f);
            _fx.AddChild(SpriteBurst.Create(_starTexture, at, 10, 600f, 40f, 1.0f, _rng, 1.1f));
            _buddy.Say(Cheers[_rng.Next(Cheers.Length)]);
            Ui.Punch(_hud.ScoreLabel, 0.6f);
        }

        AddLeaves(_board.FillLeaves(_elapsed, _rng.Next));
    }

    private void ClearLeaves()
    {
        foreach (var sprite in _leafSprites.Values)
            sprite.QueueFree();
        _leafSprites.Clear();
    }

    // ---- End ----

    private void EndRound()
    {
        _state = State.Ending;
        _elapsed = SnakeRules.PlaySeconds;
        _prompt.Visible = false;
        _pad.Visible = false;
        foreach (var sprite in _leafSprites.Values)
            sprite.FadeAway();
        _leafSprites.Clear();
        _caterpillar.Crawling = false;
        _caterpillar.OverBody = false;
        _caterpillar.Curl(new Rect2(Vector2.Zero, _field.GridSize).Grow(-_field.CellSize * 0.3f));
        Audio.I.Whoosh();
        _buddy.Hop();
        _hud.ShowTimeUp(ShowResult);
    }

    private void ShowResult()
    {
        _state = State.Result;
        var score = _board.Score;
        var longest = _board.LongestLength;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            Caption = "먹은 잎",
            Value = score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Detail = $"가장 길 때 {longest}칸",
            Entry = new ScoreEntry("", score, 0, App.Now()),
            Recordable = score > 0,
            NotRecordableMessage = "다음엔 잎을 꼭 먹어 보자!",
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

    // ---- Input ----

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when KeyDir(key.PhysicalKeycode) is { } dir:
                Turn(dir, true);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
                // A swipe starts on (or right around) the field; the buttons handle their own touches
                _swipeFrom = touch.Pressed && _field.Visible && FieldRect().Grow(40).HasPoint(touch.Position)
                    ? touch.Position
                    : null;
                break;
            case InputEventScreenDrag drag when _swipeFrom is { } from:
                var delta = drag.Position - from;
                if (delta.Length() >= Mathf.Max(20f, _field.CellSize * 0.4f))
                {
                    Turn(Mathf.Abs(delta.X) > Mathf.Abs(delta.Y)
                        ? delta.X > 0 ? MazeDir.Right : MazeDir.Left
                        : delta.Y > 0 ? MazeDir.Down : MazeDir.Up, true);
                    _swipeFrom = drag.Position; // Keep dragging to turn again
                }
                break;
        }
    }

    private static MazeDir? KeyDir(Key key) => key switch
    {
        Key.Up or Key.W => MazeDir.Up,
        Key.Right or Key.D => MazeDir.Right,
        Key.Down or Key.S => MazeDir.Down,
        Key.Left or Key.A => MazeDir.Left,
        _ => null,
    };

    private void Turn(MazeDir dir, bool flashPad)
    {
        if (_state != State.Playing)
            return;
        if (_board.Turn(dir) && flashPad)
            _pad.Flash(dir);
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// Heads for the golden leaf, else the nearest leaf, along a shortest path (the body is no obstacle). Every so often
    /// it keeps going straight into a wall, so the bump path runs too.
    /// </summary>
    private void AutoPlay(float dt)
    {
        _autoDelay -= dt;
        if (_autoDelay > 0 || _board.Queued.Count > 0 || _board.Leaves.Count == 0)
            return;
        if (!_wallTry && _elapsed >= _nextWallTry && _board.Moving)
            _wallTry = true;
        if (_wallTry)
        {
            if (_board.Moving)
                return;
            _wallTry = false;
            _nextWallTry = _elapsed + 15f;
            _autoDelay = 0.5f; // A moment to show the "깜짝"
            return;
        }
        var target = _board.Leaves.OrderBy(l => l.Golden ? 0 : 1).ThenBy(l => l.Cell.ManhattanTo(_board.Head)).First();
        if (_board.DirToward(target.Cell) is { } dir)
            Turn(dir, true);
    }
}
