using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Maze;

/// <summary>
/// 미로 탈출 (docs/games/04-maze-escape.md).
/// Flow: Setup (난이도, 캐릭터, 배경, with a preview of the next maze) → the maze appears, stars pop in and the
/// character drops onto the start → walk one cell per input (arrow keys / WASD, the on-screen pad, or a swipe on the
/// maze; holding repeats) → stars are picked up by walking over them → the exit → "탈출 성공!" → shared ResultPanel
/// (다음 미로 / 다시 하기 / 메인으로). Walls only go "통" with a nudge; there is no penalty. The clock starts with the
/// first move and holds while the quit question is open.
/// </summary>
public partial class MazeGame : Node2D
{
    private enum State { Setup, Intro, Play, Escaping, Result }

    private const string GameId = "maze";
    private const string SettingsSection = "maze";
    private const float SetupChipHeight = 84f;
    private const float Margin = 18f;
    private const float PadButton = 118f;
    private const float PadGap = 12f;
    private const float BoardGap = 28f;     // Between the maze and the right column
    private const float MaxCell = 120f;
    private const float PreviewSize = 320f;

    // Movement feel: one step is a short slide; holding repeats a little slower than a step, after a pause
    private const float StepTime = 0.14f;
    private const float BumpTime = 0.24f;
    private const float RepeatDelay = 0.3f;
    private const float RepeatEvery = 0.19f;

    private static readonly string[] StarWords = { "반짝!", "별이다!", "찾았다!" };

    private readonly Random _rng = new();
    private readonly List<MazeStar?> _stars = new();
    private readonly List<TextureRect> _starSlots = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private CanvasLayer _ui = null!;
    private Control _play = null!;        // Right column + banner (hidden while setting up)
    private VBoxContainer _side = null!;
    private DirPad _pad = null!;
    private Label _mazeLabel = null!;
    private Label _starLabel = null!;
    private Label _timeLabel = null!;
    private Label _banner = null!;
    private Node2D _fx = null!;
    private Control? _setup;
    private Control? _previewHost;
    private Label? _previewCaption;
    private MazePlayer? _previewPlayer;
    private Texture2D _starTexture = null!;

    private MazeLevel _level;
    private int _character;
    private int _index;                    // Which maze of the level's list is being played
    private Core.Maze _maze = null!;
    private MazeRun _run = null!;
    private MazeBoard? _board;
    private MazePlayer? _player;
    private int _roundToken;
    private int _shownSeconds = -1;
    private Rect2 _boardArea;

    // Input
    private MazeDir? _pending;             // A fresh press (key, pad, swipe, autoplay) waiting for the next free moment
    private MazeDir? _padHeld;
    private MazeDir? _lastKey;
    private bool _pendingFromPad;
    private float _cooldown;               // Until the current step / bump has played out
    private float _repeat;                 // Until a held direction walks again
    private Vector2? _swipeFrom;
    private int _steps;

    // Autoplay
    private readonly Queue<MazeDir> _route = new();
    private float _autoWait;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _ui = new CanvasLayer();
        AddChild(_ui);

        _level = (MazeLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)MazeLevel.Easy), 0, 2);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park);
        _character = Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "character", 0), 0, Buddy.Choices.Count - 1);

        BuildPlayUi();
        _fx = new Node2D();
        _ui.AddChild(_fx);
        Layout();
        GetViewport().SizeChanged += Layout;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    // ---- Layout: the maze as big as fits on the left, the HUD and the D-pad in a column on the right ----

    private void Layout()
    {
        var screen = ScreenSize;
        var columnWidth = PadButton * 3 + PadGap * 2;
        var columnX = screen.X - Margin - columnWidth;
        _side.Position = new Vector2(columnX, Margin);
        _side.Size = new Vector2(columnWidth, screen.Y - Margin * 2);
        _boardArea = new Rect2(Margin, Margin, columnX - BoardGap - Margin, screen.Y - Margin * 2);
        _banner.Position = _boardArea.Position;
        _banner.Size = _boardArea.Size;
        _banner.PivotOffset = _boardArea.Size / 2;
        if (_board is not null)
            PlaceBoard();
    }

    /// <summary>Cell size for an n x n maze in the board area (the floor panel reaches a little past the grid).</summary>
    private float CellFor(int n) =>
        Mathf.Floor(Mathf.Min(MaxCell, Mathf.Min(_boardArea.Size.X - 16, _boardArea.Size.Y - 16) / (n + 0.25f)));

    private void PlaceBoard()
    {
        if (_board is null || _player is null)
            return;
        _board.SetCellSize(CellFor(_maze.Width));
        _board.Position = (_boardArea.GetCenter() - _board.GridSize / 2).Round();
        _player.SetCellSize(_board.CellSize);
        _player.Snap(_board.CellCenter(_run.Position));
        for (var i = 0; i < _stars.Count; i++)
            if (_stars[i] is { } star)
            {
                star.Position = _board.CellCenter(_maze.Stars[i]);
                star.SetCellSize(_board.CellSize);
            }
    }

    // ---- Play UI: maze name, quit, stars, time, the D-pad ----

    private void BuildPlayUi()
    {
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_play);

        _side = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _side.AddThemeConstantOverride("separation", 14);
        _play.AddChild(_side);

        // Which maze (small) and quit
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 10);
        var namePanel = Ui.Panel(Colors.White with { A = 0.92f }, 10, 4);
        namePanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        namePanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _mazeLabel = Ui.Label("", 24, UiTheme.InkSoft);
        _mazeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        namePanel.AddChild(_mazeLabel);
        top.AddChild(namePanel);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);
        _side.AddChild(top);

        // Stars "★★☆ 별 2/3" and the clock (small: not a race)
        var stats = Ui.Panel(Colors.White with { A = 0.94f }, 14, 4);
        var statBody = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        statBody.AddThemeConstantOverride("separation", 6);
        stats.AddChild(statBody);
        var starRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        starRow.AddThemeConstantOverride("separation", 4);
        for (var i = 0; i < MazeRules.StarCount; i++)
        {
            var slot = Ui.Icon("res://assets/sprites/ui/star.png", 54);
            _starSlots.Add(slot);
            starRow.AddChild(slot);
        }
        starRow.AddChild(Ui.Spacer(0, 8));
        _starLabel = Ui.Label("별 0/3", 34, UiTheme.Orange, 8, Colors.White);
        _starLabel.VerticalAlignment = VerticalAlignment.Center;
        starRow.AddChild(_starLabel);
        statBody.AddChild(starRow);
        _timeLabel = Ui.Label("", 26, UiTheme.InkSoft);
        _timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statBody.AddChild(_timeLabel);
        _side.AddChild(stats);

        _side.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _pad = DirPad.Create(PadButton);
        _pad.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _pad.Down += dir =>
        {
            _padHeld = dir;
            _pending = dir;
            _pendingFromPad = true;
        };
        _pad.Up += dir =>
        {
            if (_padHeld == dir)
                _padHeld = null;
        };
        _side.AddChild(_pad);

        _banner = Ui.Label("", 72, UiTheme.Yellow, 22, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _play.AddChild(_banner);
    }

    // ---- Setup (난이도, 캐릭터, 배경) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _play.Visible = false;
        ClearBoard();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var title = Ui.Label("미로 탈출", 42, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);

        var levelChoices = new List<Choice>();
        foreach (var v in MazeRules.Variants)
            levelChoices.Add(new Choice(v.Label));
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 130, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (MazeLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
            RefreshPreview();
        };
        body.AddChild(levelRow);

        var characterRow = ChoiceRow.Create("캐릭터", Buddy.Choices, _character, 110, chipHeight: SetupChipHeight);
        characterRow.Selected += i =>
        {
            _character = i;
            App.I.SetSetting(SettingsSection, "character", i);
            _previewPlayer?.SetCharacter(i);
            _previewPlayer?.Cheer();
        };
        body.AddChild(characterRow);

        var sceneryRow = ChoiceRow.Create("배경", Scenery.Choices, (int)_scenery.Kind, chipHeight: SetupChipHeight);
        sceneryRow.Selected += i =>
        {
            _scenery.Kind = (SceneryKind)i;
            App.I.SetSetting(SettingsSection, "scenery", i);
        };
        body.AddChild(sceneryRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 76)));
        buttons.AddChild(Ui.Button("시작!", () =>
        {
            _index = SavedIndex(_level);
            StartRound();
        }, UiTheme.Green, 40, Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        // Left: the options. Right: the maze that "시작!" will play, with the character and the stars on it.
        var holder = new HBoxContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddThemeConstantOverride("separation", 0);
        holder.AddChild(Ui.Spacer(0, 24));
        var left = new CenterContainer();
        left.AddChild(panel);
        holder.AddChild(left);
        var right = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        var previewBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        previewBox.AddThemeConstantOverride("separation", 14);
        _previewCaption = Ui.Label("", 30, UiTheme.Ink, 8, Colors.White);
        _previewCaption.HorizontalAlignment = HorizontalAlignment.Center;
        previewBox.AddChild(_previewCaption);
        _previewHost = new Control
        {
            CustomMinimumSize = new Vector2(PreviewSize, PreviewSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        previewBox.AddChild(_previewHost);
        right.AddChild(previewBox);
        holder.AddChild(right);
        holder.AddChild(Ui.Spacer(0, 24));
        _setup = holder;
        _ui.AddChild(holder);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();
        RefreshPreview();

        if (App.I.Autoplay)
            GetTree().CreateTimer(1.2f).Timeout += () =>
            {
                if (_state != State.Setup)
                    return;
                _index = SavedIndex(_level);
                StartRound();
            };
    }

    /// <summary>Draws the maze "시작!" will play (the level's next maze) at a small size, popping in.</summary>
    private void RefreshPreview()
    {
        if (_previewHost is null || _previewCaption is null)
            return;
        foreach (var child in _previewHost.GetChildren())
            child.QueueFree();
        var index = SavedIndex(_level);
        var maze = MazeRules.Get(_level, index);
        var n = maze.Width;
        _previewCaption.Text = $"{n} x {n} · {index + 1}번 미로";
        var cell = Mathf.Floor((PreviewSize - 20) / n);
        var board = MazeBoard.Create(maze, cell);
        board.Position = new Vector2(PreviewSize - board.GridSize.X, PreviewSize - board.GridSize.Y) / 2;
        _previewHost.AddChild(board);
        for (var i = 0; i < maze.Stars.Count; i++)
        {
            var star = new MazeStar { Index = i, Position = board.CellCenter(maze.Stars[i]) };
            board.AddChild(star);
            star.SetCellSize(cell);
        }
        _previewPlayer = new MazePlayer { Position = board.CellCenter(maze.Start) };
        board.AddChild(_previewPlayer);
        _previewPlayer.SetCellSize(cell);
        _previewPlayer.SetCharacter(_character);
        Ui.PopIn(_previewHost, 0f, 0.3f);
    }

    private static string IndexKey(MazeLevel level) => $"next_{MazeRules.VariantId(level)}";

    /// <summary>The maze of this level to play next (advances when a maze is escaped).</summary>
    private static int SavedIndex(MazeLevel level) =>
        MazeRules.Wrap(level, (int)App.I.GetSetting(SettingsSection, IndexKey(level), 0));

    // ---- Round ----

    private async void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        _previewHost = null;
        _previewCaption = null;
        _previewPlayer = null;
        ResultPanel.CloseAll(_ui);
        ClearBoard();

        _roundToken++;
        var token = _roundToken;
        _state = State.Intro;
        _maze = MazeRules.Get(_level, _index);
        _run = new MazeRun(_maze);
        _pending = null;
        _padHeld = null;
        _swipeFrom = null;
        _cooldown = 0;
        _steps = 0;
        _shownSeconds = -1;
        _route.Clear();
        _autoWait = 0;

        _board = MazeBoard.Create(_maze, CellFor(_maze.Width));
        _world.AddChild(_board);
        for (var i = 0; i < _maze.Stars.Count; i++)
        {
            var star = new MazeStar { Index = i };
            _board.AddChild(star);
            _stars.Add(star);
        }
        _player = new MazePlayer();
        _board.AddChild(_player);
        _player.SetCharacter(_character);
        PlaceBoard();

        // HUD
        _mazeLabel.Text = $"{MazeRules.Variants[(int)_level].Label} · {_index + 1}번 미로";
        foreach (var slot in _starSlots)
            slot.SelfModulate = DimStar;
        _starLabel.Text = $"별 0/{MazeRules.StarCount}";
        _timeLabel.Text = "움직이면 시계가 가요";
        _banner.Visible = false;
        _play.Visible = true;
        _play.Modulate = Colors.White;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic();

        // 1. The maze fades and slides in
        var rest = _board.Position;
        _board.Position = rest + new Vector2(0, 40);
        _board.Modulate = Colors.Transparent;
        var tween = _board.CreateTween().SetParallel();
        tween.TweenProperty(_board, "position", rest, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_board, "modulate", Colors.White, 0.2f);
        Audio.I.Whoosh();

        // 2. Stars pop in one by one, then the character drops onto the start
        for (var i = 0; i < _stars.Count; i++)
        {
            var star = _stars[i]!;
            star.Scale = Vector2.Zero;
            var delay = 0.35f + i * 0.14f;
            var pop = star.CreateTween();
            pop.TweenInterval(delay);
            pop.TweenCallback(Callable.From(() => Audio.I.CountStep(1.2f + 0.15f * star.Index)));
            pop.TweenProperty(star, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        var drop = 0.4f + _stars.Count * 0.14f;
        _player.DropIn(drop);
        await Wait(drop + 0.35f);
        if (!Alive(token))
            return;
        Audio.I.Step(false);
        _board.Exit.Hop();
        ShowBanner("출구까지 가요!", 64, UiTheme.Yellow, 0.7f);
        Audio.I.Go();
        _state = State.Play;
    }

    private static readonly Color DimStar = new(0.2f, 0.22f, 0.36f, 0.22f);

    public override void _Process(double delta)
    {
        if (_state != State.Play)
            return;
        var dt = (float)delta;
        _run.AddTime(delta);
        if (_run.Started)
        {
            var seconds = (int)(_run.TimeMs / 1000);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _timeLabel.Text = $"{seconds}초";
            }
        }

        _cooldown -= dt;
        _repeat -= dt;
        if (App.I.Autoplay)
            AutoPlay(dt);
        if (_cooldown > 0)
            return;
        if (_pending is { } fresh)
        {
            var fromPad = _pendingFromPad;
            _pending = null;
            _pendingFromPad = false;
            _repeat = RepeatDelay;
            Step(fresh, fromPad);
        }
        else if (HeldDir() is { } held && _repeat <= 0)
        {
            _repeat = RepeatEvery;
            Step(held, _padHeld == held);
        }
    }

    // ---- Input ----

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when KeyDir(key.PhysicalKeycode) is { } dir:
                _lastKey = dir;
                if (_state == State.Play)
                {
                    _pending = dir;
                    _pendingFromPad = false;
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
                // A swipe starts on (or right around) the maze; the D-pad buttons handle their own touches
                _swipeFrom = touch.Pressed && _board is not null && BoardRect().Grow(40).HasPoint(touch.Position)
                    ? touch.Position
                    : null;
                break;
            case InputEventScreenDrag drag when _swipeFrom is { } from && _state == State.Play:
                var delta = drag.Position - from;
                var reach = Mathf.Max(36f, (_board?.CellSize ?? 60f) * 0.6f);
                if (delta.Length() >= reach)
                {
                    _pending = Mathf.Abs(delta.X) > Mathf.Abs(delta.Y)
                        ? delta.X > 0 ? MazeDir.Right : MazeDir.Left
                        : delta.Y > 0 ? MazeDir.Down : MazeDir.Up;
                    _pendingFromPad = false;
                    _swipeFrom = drag.Position; // Keep dragging to keep walking, one cell per stretch
                }
                break;
        }
    }

    private Rect2 BoardRect() => _board is null ? new Rect2() : new Rect2(_board.GlobalPosition, _board.GridSize);

    private static MazeDir? KeyDir(Key key) => key switch
    {
        Key.Up or Key.W => MazeDir.Up,
        Key.Right or Key.D => MazeDir.Right,
        Key.Down or Key.S => MazeDir.Down,
        Key.Left or Key.A => MazeDir.Left,
        _ => null,
    };

    private static bool KeyHeld(MazeDir dir) => dir switch
    {
        MazeDir.Up => Input.IsPhysicalKeyPressed(Key.Up) || Input.IsPhysicalKeyPressed(Key.W),
        MazeDir.Right => Input.IsPhysicalKeyPressed(Key.Right) || Input.IsPhysicalKeyPressed(Key.D),
        MazeDir.Down => Input.IsPhysicalKeyPressed(Key.Down) || Input.IsPhysicalKeyPressed(Key.S),
        _ => Input.IsPhysicalKeyPressed(Key.Left) || Input.IsPhysicalKeyPressed(Key.A),
    };

    /// <summary>The direction being held down: a pad button, else the most recent key that is still down.</summary>
    private MazeDir? HeldDir()
    {
        if (_padHeld is { } pad)
            return pad;
        if (_lastKey is { } last && KeyHeld(last))
            return last;
        return null;
    }

    // ---- Moves ----

    private void Step(MazeDir dir, bool fromPad)
    {
        if (_board is null || _player is null)
            return;
        var step = _run.Move(dir);
        if (!fromPad)
            _pad.Flash(dir);
        switch (step.Kind)
        {
            case MazeMoveKind.Bumped:
                // Light: "통", a nudge toward the wall, a few leaves off the hedge. No penalty.
                _cooldown = BumpTime;
                _player.Bump(dir);
                Audio.I.WallBump();
                App.Vibrate(15);
                var toward = step.From.Step(dir);
                var wall = (_board.CellCenter(step.From) + _board.CellCenter(toward)) / 2;
                _fx.AddChild(Burst.Create(_board.GlobalPosition + wall, UiTheme.Green, 5, 140f, _board.CellSize * 0.05f, 0f, 0.3f, _rng));
                break;
            case MazeMoveKind.Moved:
                // Light: a hop and a soft "톡"; footprints stay behind
                _cooldown = StepTime;
                _steps++;
                _board.MarkVisited(step.From);
                _player.StepTo(_board.CellCenter(step.To), StepTime, dir);
                Audio.I.Step(_steps % 2 == 0);
                if (step.Star >= 0)
                    PickUpStar(step.Star, _roundToken);
                if (step.Escaped)
                    Escape(_roundToken);
                break;
        }
    }

    /// <summary>
    /// The important moment: as the character lands, the star bursts into sparkles with a bell run, flies to its slot in
    /// the HUD (which lights up), the character hops, and a word pops up. The third star adds a cheer.
    /// </summary>
    private async void PickUpStar(int index, int token)
    {
        await Wait(StepTime * 0.8f);
        if (!Alive(token) || _board is null || _stars[index] is not { } star)
            return;
        var count = _run.Stars;
        var at = _board.GlobalPosition + star.Position;
        _stars[index] = null;
        star.QueueFree();

        Audio.I.StarPickup(count);
        App.Vibrate(30);
        _player?.Cheer();
        var cell = _board.CellSize;
        _fx.AddChild(Burst.Create(at, UiTheme.Yellow, 10, 320f, cell * 0.06f, cell * 0.7f, 0.45f, _rng));
        _fx.AddChild(SpriteBurst.Create(_starTexture, at, 5, 360f, cell * 0.28f, 0.6f, _rng, 0.8f));
        var all = count >= MazeRules.StarCount;
        _fx.AddChild(new FloatingText
        {
            Text = all ? "별을 다 모았어요!" : StarWords[_rng.Next(StarWords.Length)],
            Color = UiTheme.Yellow,
            FontSize = all ? 44 : 38,
            Position = at + new Vector2(0, -cell * 0.6f),
        });
        FlyStar(at, count - 1, token);
        if (all)
        {
            await Wait(0.35f);
            if (!Alive(token))
                return;
            Audio.I.Cheer();
            _board.Exit.Hop(); // "Now the exit!"
        }
    }

    /// <summary>A star flies from the maze to its HUD slot, which then lights up with a punch.</summary>
    private void FlyStar(Vector2 from, int slotIndex, int token)
    {
        if (slotIndex < 0 || slotIndex >= _starSlots.Count)
            return;
        var slot = _starSlots[slotIndex];
        var size = new Vector2(64, 64);
        var flyer = new TextureRect
        {
            Texture = _starTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = size,
            PivotOffset = size / 2,
            Position = from - size / 2,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _play.AddChild(flyer);
        var target = slot.GlobalPosition + slot.Size / 2 - size / 2;
        var tween = flyer.CreateTween();
        tween.TweenProperty(flyer, "scale", new Vector2(1.5f, 1.5f), 0.14f).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flyer, "position", target, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(flyer, "scale", new Vector2(0.85f, 0.85f), 0.5f);
        tween.Parallel().TweenProperty(flyer, "rotation", Mathf.Tau, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            flyer.QueueFree();
            if (!Alive(token))
                return;
            slot.SelfModulate = Colors.White;
            Ui.Punch(slot, 0.6f);
            _starLabel.Text = $"별 {Math.Min(slotIndex + 1, _run.Stars)}/{MazeRules.StarCount}";
            Ui.Punch(_starLabel, 0.3f);
            Audio.I.CountStep(1.2f + 0.15f * slotIndex);
            _fx.AddChild(SpriteBurst.Create(_starTexture, slot.GlobalPosition + slot.Size / 2, 4, 220f, 18f, 0.45f, _rng, 0.3f));
        }));
    }

    // ---- Escape ----

    /// <summary>Climax: the flag jumps, the character does a spinning double jump, fanfare, confetti, "탈출 성공!".</summary>
    private async void Escape(int token)
    {
        _state = State.Escaping;
        _padHeld = null;
        _pending = null;
        // The next "시작!" from the setup screen plays the next maze of this level
        App.I.SetSetting(SettingsSection, IndexKey(_level), MazeRules.Wrap(_level, _index + 1));
        _timeLabel.Text = $"{_run.TimeMs / 1000f:0.0}초";

        await Wait(StepTime);
        if (!Alive(token) || _board is null)
            return;
        var exit = _board.GlobalPosition + _board.CellCenter(_maze.Exit);
        _board.Exit.Hop();
        _player?.Celebrate();
        Audio.I.Fanfare();
        Audio.I.DuckMusic(-12f);
        App.Vibrate(60);
        _fx.AddChild(SpriteBurst.Create(_starTexture, exit, 10, 620f, _board.CellSize * 0.4f, 1f, _rng, 1.1f));
        _fx.AddChild(Burst.Create(exit, UiTheme.Orange, 16, 520f, _board.CellSize * 0.08f, _board.CellSize * 1.6f, 0.6f, _rng));
        _fx.AddChild(Confetti.Create(ScreenSize, 80));
        ShowBanner("탈출 성공!", 104, UiTheme.Yellow);

        await Wait(2.2f);
        if (!Alive(token))
            return;
        HideBanner();
        ShowResult();
    }

    private void ShowResult()
    {
        _state = State.Result;
        _play.Visible = false;
        ClearBoard();
        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            VariantId = MazeRules.VariantId(_level),
            Title = "탈출 성공!",
            Caption = "걸린 시간",
            Value = _run.TimeMs / 1000f,
            FormatValue = v => $"{v:0.0}초",
            StarsTotal = MazeRules.StarCount,
            StarsEarned = _run.Stars,
            StarsLabel = n => $"별 {n}/{MazeRules.StarCount}",
            Entry = _run.Entry("", App.Now()),
            Buttons = new[]
            {
                new ResultButton("다음 미로", UiTheme.Blue, () =>
                {
                    _index = MazeRules.Wrap(_level, _index + 1);
                    StartRound();
                }, "res://assets/sprites/ui/play.png"),
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    private void ClearBoard()
    {
        _board?.QueueFree();
        _board = null;
        _player = null;
        _stars.Clear();
    }

    // ---- Banner ----

    private Tween? _bannerTween;

    private void ShowBanner(string text, int size, Color color, float hold = 0f)
    {
        _bannerTween?.Kill();
        _banner.Text = text;
        _banner.AddThemeFontSizeOverride("font_size", size);
        _banner.AddThemeColorOverride("font_color", color);
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
        _banner.PivotOffset = _banner.Size / 2;
        _banner.Scale = new Vector2(0.3f, 0.3f);
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.4f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (hold > 0)
        {
            _bannerTween.TweenInterval(hold);
            _bannerTween.TweenProperty(_banner, "modulate:a", 0f, 0.25f);
            _bannerTween.TweenCallback(Callable.From(() => _banner.Visible = false));
        }
    }

    private void HideBanner()
    {
        _bannerTween?.Kill();
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenProperty(_banner, "modulate:a", 0f, 0.2f);
        _bannerTween.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// Walks like a child who knows the way: to the nearest star still in the maze (never across the exit), then to the
    /// exit. Bumps into a wall early on and now and then, to run that path too.
    /// </summary>
    private void AutoPlay(float dt)
    {
        _autoWait -= dt;
        if (_pending is not null || _cooldown > 0 || _autoWait > 0)
            return;
        _autoWait = 0.2f + (float)_rng.NextDouble() * 0.15f; // A bit slower than holding a key

        var here = _run.Position;
        if ((_run.Moves == 2 && _run.Bumps == 0) || _rng.NextDouble() < 0.05)
        {
            var blocked = new List<MazeDir>();
            for (var d = 0; d < 4; d++)
                if (!_maze.CanMove(here, (MazeDir)d))
                    blocked.Add((MazeDir)d);
            if (blocked.Count > 0)
            {
                _pending = blocked[_rng.Next(blocked.Count)];
                return;
            }
        }

        if (_route.Count == 0)
        {
            MazeCell? target = null;
            var best = int.MaxValue;
            var dist = _maze.Distances(here, _maze.Exit);
            for (var i = 0; i < _maze.Stars.Count; i++)
            {
                var s = _maze.Stars[i];
                if (!_run.IsTaken(i) && dist[s.X, s.Y] >= 0 && dist[s.X, s.Y] < best)
                {
                    best = dist[s.X, s.Y];
                    target = s;
                }
            }
            var path = target is { } star
                ? _maze.ShortestPath(here, star, _maze.Exit)
                : _maze.ShortestPath(here, _maze.Exit);
            foreach (var dir in Core.Maze.Directions(path))
                _route.Enqueue(dir);
        }
        if (_route.Count > 0)
            _pending = _route.Dequeue();
    }

    // ---- Helpers ----

    /// <summary>False once the scene is gone or a newer round (or the setup screen) has started.</summary>
    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _roundToken;

    /// <summary>Game-time wait: holds while the quit question pauses the tree.</summary>
    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
