using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.SwipeBrick;
using Vec = System.Numerics.Vector2;

namespace ToyBox.Games.Bubble;

/// <summary>
/// 방울 쏘기 (docs/games/13-bubble.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → the bubbles come down from above → aim (press on the board and drag: the
/// bubble goes toward the finger; ← → and Space work too) → release to shoot → it bounces off the side walls and sticks
/// next to the bubbles → 3 or more of a color pop, and whatever no longer hangs from the ceiling falls → … → every
/// bubble gone → "다 터뜨렸어요!" → shared ResultPanel (time, then shots). Press the next-bubble holder (or X / C) to swap.
/// 보통·어려움: missed shots bring the ceiling down a row (it shakes two shots ahead). Bubbles that reach the flower line
/// are not a loss: the rabbit pats the launcher ("영차!"), they turn into petals and the ceiling goes back up a row.
/// No countdown: the clock starts with the first shot.
/// </summary>
public partial class BubbleGame : Node2D
{
    private enum State { Setup, Intro, Aiming, Flying, Busy, Ending, Result }

    private const string GameId = "bubble";
    private const string SettingsSection = "bubble";
    private const float SetupChipHeight = 86f;
    private const float KeyTurnSpeed = 70f;     // Degrees per second with ← →

    private static readonly string[] PopCheers = { "뽁뽁!", "잘했어!", "최고야!", "멋져요!" };

    private readonly Random _rng = new();
    private readonly List<Vector2> _shotPath = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _boardNode = null!;
    private BubbleField _field = null!;
    private BubbleAim _aim = null!;
    private BubbleLauncher _launcher = null!;
    private ShotBubble _shot = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private BubbleHud _hud = null!;
    private Control? _setup;
    private Texture2D _starTexture = null!;

    private float _unit = 64f;
    private BubbleLevel _level;
    private int _index;
    private BubbleRound _round = new(new BubbleBoard(BubbleLevel.Easy), new Random());
    private BubbleTrace? _trace;
    private float _shotDist;
    private float _shotLength;
    private float _aimAngle = 90f;
    private bool _aiming;          // A finger is down (or the keys moved the aim)
    private Vec? _aimDir;          // Current aim direction (null: none yet)
    private Tween? _offsetTween;
    private int _token;            // Bumped on every new round: late tween callbacks of an old round do nothing

    // Autoplay
    private float _autoTimer;
    private BubbleShotPlan? _autoPlan;
    private bool _autoSwapped;

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
        _field = new BubbleField();
        _boardNode.AddChild(_field);
        _aim = new BubbleAim();
        _boardNode.AddChild(_aim);
        _launcher = new BubbleLauncher();
        _boardNode.AddChild(_launcher);
        _shot = new ShotBubble { Visible = false };
        _boardNode.AddChild(_shot);
        _fx = new Node2D();
        _boardNode.AddChild(_fx);

        _ui = new CanvasLayer();
        AddChild(_ui);
        _hud = BubbleHud.Create();
        _ui.AddChild(_hud);

        _level = (BubbleLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)BubbleLevel.Easy), 0, 2);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 1));

        Layout();
        GetViewport().SizeChanged += Layout;
        // A whole board takes a while for the headless smoke test (it stops after 8000 frames): triple speed there only
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
        var avail = Mathf.Max(200f, screen.X - BubbleHud.LeftWidth - BubbleHud.RightWidth);
        _unit = Mathf.Floor(Mathf.Min(screen.Y / BubbleRules.Height, avail / BubbleRules.Width));
        var boardWidth = BubbleRules.Width * _unit;
        var x = Mathf.Clamp(screen.X / 2 - boardWidth / 2, BubbleHud.LeftWidth,
            Mathf.Max(BubbleHud.LeftWidth, screen.X - BubbleHud.RightWidth - boardWidth));
        _boardNode.Position = new Vector2(x, screen.Y - BubbleRules.Height * _unit).Round();

        _field.Unit = _unit;
        _aim.Unit = _unit;
        _launcher.Unit = _unit;
        _launcher.Position = Px(new Vec(BubbleRules.LaunchX, BubbleRules.LaunchY));
        _launcher.Relayout();
        _shot.Size = _unit;
        _buddy.Position = new Vector2(screen.X - BubbleHud.RightWidth / 2, screen.Y - 16);
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

    private static string IndexKey(BubbleLevel level) => $"next_{BubbleRules.VariantId(level)}";

    private static int SavedIndex(BubbleLevel level) =>
        BubbleRules.Wrap(level, (int)App.I.GetSetting(SettingsSection, IndexKey(level), 0));

    private void ShowSetup()
    {
        _state = State.Setup;
        _token++;
        _hud.Visible = false;
        ResultPanel.CloseAll(_ui);
        ClearRound(new BubbleBoard(_level));
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/bubble.png", 64));
        titleRow.AddChild(Ui.Label("방울 쏘기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        var levelChoices = new[]
        {
            new Choice(BubbleRules.Variants[0].Label, "res://assets/sprites/bubble/green.png"),
            new Choice(BubbleRules.Variants[1].Label, "res://assets/sprites/bubble/blue.png"),
            new Choice(BubbleRules.Variants[2].Label, "res://assets/sprites/bubble/purple.png"),
        };
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 150, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (BubbleLevel)i;
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
        buttons.AddChild(Ui.Button("시작!", StartFromSetup, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        _setup = Ui.Centered(panel);
        _ui.AddChild(_setup);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();

        if (App.I.Autoplay)
            GetTree().CreateTimer(1.2f).Timeout += () =>
            {
                if (_state == State.Setup)
                    StartFromSetup();
            };
    }

    private void StartFromSetup()
    {
        _index = SavedIndex(_level);
        StartRound();
    }

    /// <summary>A new board in place, nothing in the air, the effects cleared.</summary>
    private void ClearRound(BubbleBoard board)
    {
        _offsetTween?.Kill();
        _offsetTween = null;
        _round = new BubbleRound(board, _rng);
        _field.ResetEffects();
        _field.Board = board;
        _trace = null;
        _shot.Visible = false;
        _aim.Hide();
        _aiming = false;
        _aimDir = null;
        _aimAngle = 90f;
        _launcher.AimAngle = 90f;
        _launcher.Current = -1;
        _launcher.Next = -1;
        foreach (var child in _fx.GetChildren())
            child.QueueFree();
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _token++;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        ClearRound(BubbleRules.Board(_level, _index));
        _hud.Visible = true;
        _hud.SetBoard($"{BubbleRules.Variants[(int)_level].Label} · {_index + 1}번 판");
        _hud.SetClock(0);
        _hud.SetShots(0);
        _hud.SetLeft(_round.Board.Count);
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck

        // The bubbles come down from above the screen with the ceiling
        _state = State.Intro;
        _field.Offset = -(_boardNode.Position.Y + BubbleRules.CeilingY(0) * _unit + 7f * _unit);
        Audio.I.Whoosh();
        var token = _token;
        _offsetTween = CreateTween();
        _offsetTween.TweenMethod(Callable.From<float>(v => _field.Offset = v), _field.Offset, 0f, 0.8f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _offsetTween.TweenCallback(Callable.From(() =>
        {
            if (token != _token)
                return;
            _launcher.Current = _round.Current;
            _launcher.Next = _round.Next;
            _launcher.PopCurrent();
            _launcher.PopNext();
            Audio.I.Pop(0);
            _buddy.Say("끌어서 쏘아요!", 1.8f);
            BeginAiming();
        }));
    }

    private void BeginAiming()
    {
        _state = State.Aiming;
        _aiming = false;
        _field.Warning = _round.Board.CeilingWarning;
        _autoTimer = 0;
        _autoSwapped = false;
        _autoPlan = null;
        if (App.I.Autoplay)
            // Shots 3..7 are careless on purpose, so the ceiling and the flower line get used too
            _autoPlan = BubbleRules.BestShot(_round, careless: _round.Shots is >= 2 and <= 6);
        if (_aimDir is not null)
            UpdateAimGuide();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (_state is State.Aiming or State.Flying or State.Busy)
        {
            _round.Tick(dt);
            _hud.SetClock(_round.TimeMs);
        }
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
            _aimAngle = BubbleRules.ClampAimAngle(_aimAngle + turn * KeyTurnSpeed * dt);
            _aimDir = BubbleRules.DirectionFromAngle(_aimAngle);
            UpdateAimGuide();
        }
    }

    /// <summary>The dotted line (to the first bounce; 쉬움: all the way, with a faint bubble where it will stick).</summary>
    private void UpdateAimGuide()
    {
        if (_aimDir is not { } dir)
        {
            _aim.Hide();
            return;
        }
        var from = Px(new Vec(BubbleRules.LaunchX, BubbleRules.LaunchY));
        if (!BubbleRules.CanFire(dir))
        {
            _aim.Show(new[] { from, from + new Vector2(dir.X, dir.Y) * _unit * 2.2f }, false);
            return;
        }
        var trace = BubbleRules.Trace(_round.Board, dir);
        var easy = _level == BubbleLevel.Easy;
        var points = new List<Vector2>();
        foreach (var p in easy ? trace.Points : trace.UpToFirstBounce())
            points.Add(Px(p));
        if (easy)
            _aim.Show(points, true, _round.Current, Px(_round.Board.Center(trace.Cell)));
        else
            _aim.Show(points, true);
        _launcher.AimAngle = BubbleRules.AngleOf(dir);
    }

    private void Fire()
    {
        if (_state != State.Aiming || _aimDir is not { } dir || !BubbleRules.CanFire(dir))
            return;
        _aimAngle = BubbleRules.AngleOf(dir);
        _launcher.AimAngle = _aimAngle;
        _aim.Hide();
        _aiming = false;
        var trace = _round.Shoot(dir);
        _trace = trace;

        _shotPath.Clear();
        foreach (var p in trace.Points)
            _shotPath.Add(Px(p));
        _shotPath.Add(Px(_round.Board.Center(trace.Cell))); // The last little move into the cell
        _shotLength = 0;
        for (var i = 1; i < _shotPath.Count; i++)
            _shotLength += _shotPath[i - 1].DistanceTo(_shotPath[i]);
        _shotDist = 0;
        _shot.Color = _round.InFlight ?? 0;
        _shot.Reset(_shotPath[0]);
        _shot.Visible = true;

        _launcher.Current = _round.Current;
        _launcher.Next = _round.Next;
        _launcher.PopCurrent();
        _launcher.PopNext();
        _launcher.Throw();
        _hud.SetShots(_round.Shots);
        _state = State.Flying;
        Audio.I.Jump();
    }

    private void Swap()
    {
        if (_state != State.Aiming || _round.Current == _round.Next)
            return;
        _round.Swap();
        _launcher.Current = _round.Current;
        _launcher.Next = _round.Next;
        _launcher.PopCurrent();
        _launcher.PopNext();
        Audio.I.CardFlip();
        UpdateAimGuide();
    }

    // ---- Flight ----

    private void UpdateFlying(float dt)
    {
        if (_trace is not { } trace)
            return;
        var bounced = false;
        var before = _shotDist;
        _shotDist = Mathf.Min(_shotLength, _shotDist + BubbleRules.ShotSpeed * _unit * dt);
        // Walk the path to the new distance (a wall bounce on the way says "통")
        var travelled = 0f;
        var at = _shotPath[^1];
        for (var i = 1; i < _shotPath.Count; i++)
        {
            var len = _shotPath[i - 1].DistanceTo(_shotPath[i]);
            if (i < _shotPath.Count - 2 && before < travelled + len && _shotDist >= travelled + len)
                bounced = true;
            if (_shotDist <= travelled + len)
            {
                at = _shotPath[i - 1].Lerp(_shotPath[i], len > 0 ? (_shotDist - travelled) / len : 1f);
                break;
            }
            travelled += len;
        }
        _shot.MoveTo(at);
        if (bounced)
            Audio.I.Step(_rng.Next(2) == 0);
        if (_shotDist >= _shotLength)
            Landed(trace);
    }

    private void Landed(BubbleTrace trace)
    {
        var board = _round.Board;
        var dropOld = board.Drop;
        var result = _round.Land(trace);
        _trace = null;
        _shot.Visible = false;

        var down = result.CeilingDown ? 1 : 0;
        var up = result.CeilingUp ? 1 : 0;
        var rowPx = BubbleRules.RowHeight * _unit;
        // Draw as before (the model has already moved the ceiling); the tweens below catch up
        _field.Offset = (up - down) * rowPx;
        _field.Ghosts.Clear();
        _field.Ghosts.AddRange(result.Cleaned);
        _field.Warning = board.CeilingWarning;

        if (result.Missed)
        {
            if (board.ColorAt(result.Cell) >= 0)
                _field.Wobble(result.Cell);
            Audio.I.Click(); // "톡"
        }
        else
        {
            Popped(result, dropOld);
        }
        if (result.Dropped.Count > 0)
            Dropped(result, dropOld);

        _hud.SetLeft(board.Count + result.Cleaned.Count, !result.Missed);
        if (_launcher.Current != _round.Current || _launcher.Next != _round.Next)
        {
            // A queued color is gone from the board: it changes to one that is there
            if (_launcher.Current != _round.Current)
                _launcher.PopCurrent();
            if (_launcher.Next != _round.Next)
                _launcher.PopNext();
            _launcher.Current = _round.Current;
            _launcher.Next = _round.Next;
        }

        var token = _token;
        if (result.CeilingDown)
        {
            // The ceiling comes down one row: a soft thud, the bubbles bounce into place
            Audio.I.Whoosh();
            _shake.Add(3f);
            _offsetTween?.Kill();
            _offsetTween = CreateTween();
            _offsetTween.TweenInterval(0.15f);
            _offsetTween.TweenMethod(Callable.From<float>(v => _field.Offset = v), _field.Offset, up * rowPx, 0.45f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
            _offsetTween.TweenCallback(Callable.From(() =>
            {
                if (token == _token)
                    Audio.I.Step(false);
            }));
        }

        if (result.Cleaned.Count > 0)
        {
            _state = State.Busy;
            GetTree().CreateTimer(result.CeilingDown ? 0.7f : 0.25f).Timeout += () =>
            {
                if (token == _token)
                    Cleanup(result.Cleared);
            };
        }
        else if (result.Cleared)
        {
            AllPopped();
        }
        else
        {
            BeginAiming();
        }
    }

    /// <summary>The group pops one after another from the new bubble outward, with a rising "뽁뽁뽁".</summary>
    private void Popped(BubbleResult result, int dropOld)
    {
        var i = 0;
        foreach (var spot in result.Popped)
        {
            var at = Px(BubbleRules.CellCenter(spot.Cell, dropOld));
            var pop = BubblePop.Create(at, spot.Color, _unit, i * 0.055f);
            var step = i;
            pop.Burst += () =>
            {
                Audio.I.Pop(Mathf.Min(step, 10));
                if (step % 2 == 0)
                    _fx.AddChild(SpriteBurst.Create(_starTexture, at, 2, 220f, _unit * 0.28f, 0.45f, _rng, 0.9f));
            };
            _fx.AddChild(pop);
            i++;
        }
        App.Vibrate(15);
        _shake.Add(2f);
        if (result.Popped.Count >= 5 || result.Dropped.Count == 0)
            _buddy.Hop(result.Popped.Count >= 5 ? 32f : 16f);
        if (result.Popped.Count >= 5 && result.Dropped.Count == 0)
            _buddy.Say(PopCheers[_rng.Next(PopCheers.Length)], 1.0f);
    }

    /// <summary>The bubbles that lost their hold bounce down and turn into stars; "우와!" grows with how many fell.</summary>
    private void Dropped(BubbleResult result, int dropOld)
    {
        var floorY = BubbleRules.FlowerLineY * _unit - _unit * 0.3f;
        var center = Vector2.Zero;
        var landed = 0;
        foreach (var spot in result.Dropped)
        {
            var at = Px(BubbleRules.CellCenter(spot.Cell, dropOld));
            center += at;
            var fall = FallingBubble.Create(at, spot.Color, _unit, floorY + (float)_rng.NextDouble() * _unit * 0.2f, _rng);
            fall.Landed += f =>
            {
                landed++;
                _fx.AddChild(SpriteBurst.Create(_starTexture, f.Position, 3, 300f, _unit * 0.36f, 0.6f, _rng, 1.2f));
                if (landed % 2 == 1)
                    Audio.I.StarPickup(Mathf.Min(landed, 8));
                _field.BounceFloor();
            };
            _fx.AddChild(fall);
        }
        center /= result.Dropped.Count;
        var n = result.Dropped.Count;
        var size = Mathf.Min(40 + n * 6, 96);
        _fx.AddChild(new FloatingText { Text = n >= 6 ? "우와아!" : "우와!", Color = UiTheme.Yellow, FontSize = size, Position = center });
        _buddy.Say(n >= 6 ? "우와아아!" : "우와!", 1.2f);
        _buddy.Hop(40f);
        if (n >= 3)
            Audio.I.Cheer();
        _shake.Add(Mathf.Min(2f + n * 0.5f, 7f));
        App.Vibrate(25);
    }

    /// <summary>
    /// "영차!": the rabbit pats the launcher, what reached the flower line turns into petals ("퐁퐁") and the ceiling
    /// goes back up a row. The board carries on.
    /// </summary>
    private void Cleanup(bool cleared)
    {
        _launcher.Pat();
        var rabbitAt = _launcher.Position + new Vector2(2.4f, -1.25f) * _unit;
        _fx.AddChild(new FloatingText { Text = "영차!", Color = UiTheme.Yellow, FontSize = 48, Position = rabbitAt });
        Audio.I.Jump();
        var token = _token;
        var tween = CreateTween();
        tween.TweenInterval(0.45f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (token != _token)
                return;
            var i = 0;
            foreach (var g in _field.Ghosts)
            {
                var at = _field.CellPosition(g.Cell, _round.Board.Drop);
                _fx.AddChild(FlowerPop.Create(at, _unit * 0.95f, g.Cell.Col + g.Color));
                _fx.AddChild(Burst.Create(at, BubbleArt.Tints[g.Color].Lerp(Colors.White, 0.4f), 6, 200f, _unit * 0.06f, 0f, 0.4f, _rng));
                if (i++ % 2 == 0)
                    Audio.I.Pop(Mathf.Min(i, 8));
            }
            Audio.I.Plop();
            _field.Ghosts.Clear();
            _field.BounceFloor();
            _buddy.Say("꽃이 됐다!", 1.0f);
        }));
        tween.TweenInterval(0.2f);
        tween.TweenMethod(Callable.From<float>(v => _field.Offset = v), _field.Offset, 0f, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() =>
        {
            if (token != _token)
                return;
            _hud.SetLeft(_round.Board.Count, true);
            if (cleared)
                AllPopped();
            else
                BeginAiming();
        }));
    }

    // ---- End ----

    /// <summary>Every bubble is gone: fanfare, confetti, the rabbit jumps, "다 터뜨렸어요!", then the result screen.</summary>
    private void AllPopped()
    {
        _state = State.Ending;
        _aim.Hide();
        _field.Warning = false;
        _hud.SetClock(_round.TimeMs);
        _hud.SetLeft(0);
        // The next "시작!" from the setup screen plays the next board of this level
        App.I.SetSetting(SettingsSection, IndexKey(_level), BubbleRules.Wrap(_level, _index + 1));
        var token = _token;
        var tween = CreateTween();
        tween.TweenInterval(0.6f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (token != _token)
                return;
            Audio.I.Fanfare();
            App.Vibrate(40);
            _shake.Add(4f);
            _ui.AddChild(Confetti.Create(ScreenSize, 80));
            _launcher.Cheer();
            _buddy.Hop(50f);
            _buddy.Say("다 터뜨렸다!", 1.8f);
            _hud.ShowCleared(() =>
            {
                if (token == _token)
                    ShowResult();
            });
        }));
    }

    private void ShowResult()
    {
        _state = State.Result;
        _hud.Visible = false;
        var seconds = _round.TimeMs / 1000f;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = BubbleRules.VariantId(_level),
            Title = "다 터뜨렸어요!",
            Caption = "걸린 시간",
            Value = seconds,
            FormatValue = v => $"{v:0.0}초",
            Detail = $"쏜 방울 {_round.Shots}번",
            Entry = _round.Entry("", App.Now()),
            Buttons = new[]
            {
                new ResultButton("다음 판", UiTheme.Blue, () =>
                {
                    _index = BubbleRules.Wrap(_level, _index + 1);
                    StartRound();
                }, "res://assets/sprites/ui/play.png"),
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    // ---- Input: press on the board and drag to aim toward the finger, release to shoot; the holder swaps ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state != State.Aiming || App.I.Autoplay)
            return;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } touch when OnHolder(touch.Position):
                Swap();
                GetViewport().SetInputAsHandled();
                break;
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
                if (_aimDir is { } d && BubbleRules.CanFire(d))
                    Fire();
                else
                {
                    _aimDir = null;
                    UpdateAimGuide();
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key
                when key.PhysicalKeycode is Key.Space or Key.Enter or Key.KpEnter or Key.Up or Key.W:
                _aimDir ??= BubbleRules.DirectionFromAngle(_aimAngle);
                Fire();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key when key.PhysicalKeycode is Key.X or Key.C:
                Swap();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>Presses count on the board (the HUD columns at the sides have their own buttons).</summary>
    private bool OnBoard(Vector2 screen)
    {
        var p = ToBoard(screen);
        return p.X >= -0.05f && p.X <= BubbleRules.Width + 0.05f;
    }

    private bool OnHolder(Vector2 screen)
    {
        var local = screen - _world.Position - _boardNode.Position;
        return local.DistanceTo(_launcher.HolderCenter) < _unit * 0.85f;
    }

    private void AimAt(Vector2 screen)
    {
        _aimDir = BubbleRules.AimToward(ToBoard(screen)) ?? _aimDir;
        if (_aimDir is { } d && BubbleRules.CanFire(d))
            _aimAngle = BubbleRules.AngleOf(d);
        UpdateAimGuide();
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>Swaps if the plan says so, sweeps the aim line over to the planned angle, then shoots.</summary>
    private void AutoAim(float dt)
    {
        if (_autoPlan is not { } plan)
            return;
        _autoTimer += dt;
        if (_autoTimer < 0.25f)
            return;
        if (plan.Swap && !_autoSwapped)
        {
            _autoSwapped = true;
            Swap();
        }
        _aimAngle = Mathf.MoveToward(_aimAngle, plan.Angle, 160f * dt);
        _aimDir = BubbleRules.DirectionFromAngle(_aimAngle);
        UpdateAimGuide();
        if (Mathf.IsEqualApprox(_aimAngle, plan.Angle) && _autoTimer > 0.8f)
        {
            _aimDir = BubbleRules.DirectionFromAngle(plan.Angle);
            Fire();
        }
    }
}
