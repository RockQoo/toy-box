using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Sudoku;

/// <summary>
/// 그림 스도쿠 (docs/games/15-picture-sudoku.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → the board slides in and the starting animals pop in one by one → pick a cell
/// and then an animal, or hold an animal and tap cells (both work) → … → every cell filled → "완성!" → shared
/// ResultPanel (time; 보조 줄 "힌트 N번"). An animal that is already in the row, column or box does not go in: both
/// animals tilt their heads, "통", and the new one hops back out. No penalty for clashes or hints. No countdown: the
/// clock starts with the first input and holds while the quit question is open.
/// </summary>
public partial class SudokuGame : Node2D
{
    private enum State { Setup, Intro, Play, Finishing, Result }

    private const string GameId = "sudoku";
    private const string SettingsSection = "sudoku";
    private const float SetupChipHeight = 86f;
    private const float Margin = 18f;
    private const float LeftWidth = 250f;

    private static readonly string[] LineCheers = { "한 줄 완성!", "잘했어요!", "멋져요!", "딩동!" };

    private readonly Random _rng = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private CanvasLayer _ui = null!;
    private Control _hud = null!;
    private VBoxContainer _left = null!;
    private Label _puzzleLabel = null!;
    private Label _clockLabel = null!;
    private Label _banner = null!;
    private SudokuPalette? _palette;
    private SudokuBoardView? _view;
    private Control? _setup;
    private Label? _setupInfo;
    private Texture2D _starTexture = null!;

    private SudokuLevel _level;
    private int _index;
    private SudokuBoard _board = new(SudokuRules.Get(SudokuLevel.Easy, 0));
    private int _selected = -1;      // Selected cell (-1: none)
    private int _held;               // Animal held for "animal first" (0: none)
    private bool _memoMode;
    private int _linesDone;
    private int _shownSeconds = -1;
    private float _lastClashTalk = -10f;
    private float _time;
    private int _token;

    // Autoplay
    private readonly Queue<Action> _autoSteps = new();
    private float _autoWait;
    private int _autoBatch;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _buddy = new Buddy();
        _world.AddChild(_buddy);
        _fx = new Node2D { ZIndex = 5 };
        _world.AddChild(_fx);

        _ui = new CanvasLayer();
        AddChild(_ui);
        BuildHud();

        _level = (SudokuLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)SudokuLevel.Easy), 0, 2);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        Layout();
        GetViewport().SizeChanged += Layout;
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    // ---- HUD: quit, which puzzle, a small faint clock (left); the palette (right) ----

    private void BuildHud()
    {
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_hud);

        _left = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _left.AddThemeConstantOverride("separation", 14);
        _hud.AddChild(_left);
        _left.AddChild(Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 24, null, new Vector2(LeftWidth - Margin * 2, 64)));

        var info = Ui.Panel(Colors.White with { A = 0.8f }, 12, 4);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 2);
        _puzzleLabel = Ui.Label("", 26, UiTheme.Ink);
        _puzzleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_puzzleLabel);
        _clockLabel = Ui.Label("", 22, UiTheme.InkSoft);
        _clockLabel.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_clockLabel);
        info.AddChild(body);
        _left.AddChild(info);

        _banner = Ui.Label("", 96, UiTheme.Yellow, 24, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _ui.AddChild(_banner);
    }

    private void Layout()
    {
        var screen = ScreenSize;
        _left.Position = new Vector2(Margin, Margin);
        _left.Size = new Vector2(LeftWidth - Margin * 2, 0);
        _buddy.Position = new Vector2(LeftWidth / 2, screen.Y - 16);
        _banner.Position = Vector2.Zero;
        _banner.Size = screen;
        _banner.PivotOffset = screen / 2;
        PlaceBoard();
    }

    /// <summary>The board as big as fits between the left panel and the palette (cells about 140 / 100 px at 1280 x 720).</summary>
    private void PlaceBoard()
    {
        var screen = ScreenSize;
        if (_palette is not null)
        {
            _palette.Size = new Vector2(SudokuPalette.Width, 0);
            _palette.ResetSize();
            _palette.Position = new Vector2(screen.X - Margin - SudokuPalette.Width,
                Mathf.Max(Margin, (screen.Y - _palette.Size.Y) / 2)).Round();
        }
        if (_view is null)
            return;
        var area = new Rect2(LeftWidth, Margin, screen.X - LeftWidth - SudokuPalette.Width - Margin * 2, screen.Y - Margin * 2);
        var max = _view.Shape.Size == 4 ? 140f : 104f;
        _view.SetCellSize(SudokuBoardView.FitCell(_view.Shape, area.Size, max));
        _view.Position = (area.GetCenter() - _view.GridSize / 2).Round();
    }

    // ---- Setup (꾸미기) ----

    private static string IndexKey(SudokuLevel level) => $"next_{SudokuRules.VariantId(level)}";

    private static int SavedIndex(SudokuLevel level) =>
        SudokuRules.Wrap(level, (int)App.I.GetSetting(SettingsSection, IndexKey(level), 0));

    private void ShowSetup()
    {
        _state = State.Setup;
        _token++;
        _hud.Visible = false;
        ResultPanel.CloseAll(_ui);
        ClearBoard();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/picture_sudoku.png", 64));
        titleRow.AddChild(Ui.Label("그림 스도쿠", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        var levelChoices = new[]
        {
            new Choice(SudokuRules.Variants[0].Label, SudokuArt.FacePath(1)),
            new Choice(SudokuRules.Variants[1].Label, SudokuArt.FacePath(5)),
            new Choice(SudokuRules.Variants[2].Label, SudokuArt.FacePath(6)),
        };
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 150, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (SudokuLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
            RefreshSetupInfo();
        };
        body.AddChild(levelRow);
        _setupInfo = Ui.Label("", 24, UiTheme.InkSoft);
        _setupInfo.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_setupInfo);
        RefreshSetupInfo();

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

    private void RefreshSetupInfo()
    {
        if (_setupInfo is null)
            return;
        var n = SudokuRules.Size(_level);
        _setupInfo.Text = $"{n} x {n} · 동물 {n}마리 · 빈 칸 {SudokuRules.Blanks(_level)}개 · {SavedIndex(_level) + 1}번 문제";
    }

    private void StartFromSetup()
    {
        _index = SavedIndex(_level);
        StartRound();
    }

    private void ClearBoard()
    {
        _view?.QueueFree();
        _view = null;
        _palette?.QueueFree();
        _palette = null;
        foreach (var child in _fx.GetChildren())
            child.QueueFree();
    }

    // ---- Round ----

    private async void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _token++;
        var token = _token;
        _setup?.QueueFree();
        _setup = null;
        _setupInfo = null;
        ResultPanel.CloseAll(_ui);
        ClearBoard();

        _state = State.Intro;
        _board = new SudokuBoard(SudokuRules.Get(_level, _index));
        _selected = -1;
        _held = 0;
        _memoMode = false;
        _linesDone = 0;
        _shownSeconds = -1;
        _autoSteps.Clear();
        _autoWait = 0;
        _autoBatch = 0;

        var shape = _board.Shape;
        _view = SudokuBoardView.Create(shape, 100f);
        _world.AddChild(_view);
        _world.MoveChild(_buddy, -1); // The buddy's speech bubble goes over the board
        _world.MoveChild(_fx, -1);
        _palette = SudokuPalette.Create(shape.Size);
        _palette.AnimalPressed += OnAnimalButton;
        _palette.ToolPressed += OnTool;
        _hud.AddChild(_palette);
        _palette.SetMemoOn(false);
        PlaceBoard();
        Callable.From(PlaceBoard).CallDeferred(); // The palette's size is known after a frame

        _puzzleLabel.Text = $"{SudokuRules.Variants[(int)_level].Label} · {_index + 1}번 문제";
        _clockLabel.Text = "누르면 시계가 가요";
        _hud.Visible = true;
        _banner.Visible = false;
        RefreshAll();
        foreach (var cell in _view.Cells)
            cell.SetAnimal(0);
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck

        // 1. The board slides up and fades in
        var rest = _view.Position;
        _view.Position = rest + new Vector2(0, 50);
        _view.Modulate = Colors.Transparent;
        var tween = _view.CreateTween().SetParallel();
        tween.TweenProperty(_view, "position", rest, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_view, "modulate", Colors.White, 0.25f);
        Audio.I.Whoosh();
        Ui.PopIn(_palette, 0.15f);

        // 2. The starting animals pop in one by one ("톡톡")
        await Wait(0.4f);
        var delay = 0f;
        var step = shape.Size == 4 ? 0.07f : 0.035f;
        for (var i = 0; i < shape.CellCount; i++)
        {
            if (!_board.IsGiven(i))
                continue;
            var cell = _view.Cells[i];
            cell.SetAnimal(_board.Value(i));
            cell.Pop(delay);
            delay += step;
        }
        PlopRun(token, (int)(delay / 0.09f) + 1);
        await Wait(delay + 0.3f);
        if (!Alive(token))
            return;
        _buddy.Say(shape.Size == 4 ? "빈 칸에 동물을 넣어요!" : "줄마다 한 번씩!", 1.8f);
        _state = State.Play;
        RefreshAll();
    }

    /// <summary>Soft "톡" sounds while the starting animals pop in (not one per animal: that would be a buzz).</summary>
    private async void PlopRun(int token, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!Alive(token))
                return;
            Audio.I.Plop();
            await Wait(0.09f);
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_state != State.Play)
            return;
        _board.AddTime(delta);
        if (_board.Started)
        {
            var seconds = (int)(_board.TimeMs / 1000);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _clockLabel.Text = $"{seconds}초";
            }
        }
        if (App.I.Autoplay)
            AutoPlay((float)delta);
    }

    // ---- Input ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state != State.Play || _view is null)
            return;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } touch:
                var cell = _view.CellAt(_view.ToLocal(touch.Position));
                if (cell >= 0)
                {
                    TapCell(cell);
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventKey { Pressed: true } key:
                if (HandleKey(key))
                    GetViewport().SetInputAsHandled();
                break;
        }
    }

    private bool HandleKey(InputEventKey key)
    {
        var code = key.PhysicalKeycode;
        int? dx = code switch { Key.Left or Key.A => -1, Key.Right or Key.D => 1, _ => null };
        int? dy = code switch { Key.Up or Key.W => -1, Key.Down or Key.S => 1, _ => null };
        if (dx is not null || dy is not null)
        {
            MoveSelection(dx ?? 0, dy ?? 0);
            return true;
        }
        if (key.Echo)
            return false;
        var digit = code switch
        {
            >= Key.Key1 and <= Key.Key6 => (int)(code - Key.Key1) + 1,
            >= Key.Kp1 and <= Key.Kp6 => (int)(code - Key.Kp1) + 1,
            _ => 0,
        };
        if (digit is > 0 && digit <= _board.Shape.Size)
        {
            // Keys always go to the selected cell (cell first)
            if (_selected < 0)
                MoveSelection(0, 0);
            PlaceInto(_selected, digit);
            return true;
        }
        switch (code)
        {
            case Key.Delete or Key.Backspace or Key.Key0:
                OnTool(SudokuTool.Erase);
                return true;
            case Key.M when _board.Shape.Size > 4:
                OnTool(SudokuTool.Memo);
                return true;
            case Key.H:
                OnTool(SudokuTool.Hint);
                return true;
            case Key.Z or Key.U:
                OnTool(SudokuTool.Undo);
                return true;
        }
        return false;
    }

    /// <summary>Arrow keys: move the selection (none yet: start on the first empty cell).</summary>
    private void MoveSelection(int dx, int dy)
    {
        var shape = _board.Shape;
        if (_selected < 0)
        {
            var first = Array.IndexOf(_board.Grid, 0);
            Select(first < 0 ? 0 : first);
            return;
        }
        var row = Mathf.Clamp(shape.Row(_selected) + dy, 0, shape.Size - 1);
        var col = Mathf.Clamp(shape.Column(_selected) + dx, 0, shape.Size - 1);
        Select(shape.Cell(row, col));
    }

    private void Select(int cell)
    {
        _board.Start();
        _selected = cell;
        Audio.I.Tick(1.4f);
        RefreshAll();
    }

    /// <summary>A tap on a cell: with an animal held it goes in, otherwise the cell is selected.</summary>
    private void TapCell(int cell)
    {
        if (_held != 0 && !_board.IsFixed(cell))
        {
            _selected = cell;
            PlaceInto(cell, _held);
            return;
        }
        if (_board.IsFixed(cell) && _held != 0)
            _view?.Cells[cell].Wiggle();
        Select(cell);
    }

    /// <summary>
    /// An animal button: while one is held, it switches (or lets go of) the held animal. Otherwise it goes into the
    /// selected cell; with no cell (or a starting one) selected it becomes the held animal, ready for tapping cells.
    /// </summary>
    private void OnAnimalButton(int animal)
    {
        if (_state != State.Play || _palette is null)
            return;
        _board.Start();
        if (_held == 0 && _selected >= 0 && !_board.IsFixed(_selected))
        {
            PlaceInto(_selected, animal);
            return;
        }
        _held = _held == animal ? 0 : animal;
        Audio.I.Tick(_held == 0 ? 1.0f : 1.6f);
        if (_held != 0)
            Ui.Punch(_palette.AnimalButton(animal), 0.15f);
        RefreshAll();
    }

    private void OnTool(SudokuTool tool)
    {
        if (_state != State.Play || _palette is null || _view is null)
            return;
        _board.Start();
        _palette.Pulse(tool);
        switch (tool)
        {
            case SudokuTool.Erase:
                if (_selected < 0)
                    break;
                var before = _board.Value(_selected);
                if (_board.Erase(_selected))
                {
                    if (before != 0)
                        HopOut(_selected, before);
                    else
                        Audio.I.Plop();
                }
                else if (_board.IsFixed(_selected))
                    _view.Cells[_selected].Wiggle();
                break;
            case SudokuTool.Memo:
                _memoMode = !_memoMode;
                _palette.SetMemoOn(_memoMode);
                Audio.I.Tick(_memoMode ? 1.6f : 1.0f);
                if (_memoMode)
                    _buddy.Say("연필로 살짝 적어 봐요", 1.2f);
                break;
            case SudokuTool.Hint:
                UseHint();
                return;
            case SudokuTool.Undo:
                if (_board.Undo())
                    Audio.I.Whoosh();
                break;
        }
        RefreshAll();
    }

    // ---- Placing ----

    private void PlaceInto(int cell, int animal)
    {
        if (_view is null || cell < 0)
            return;
        var view = _view.Cells[cell];
        if (_memoMode)
        {
            if (_board.ToggleMemo(cell, animal) is { } on)
                Audio.I.Tick(on ? 1.8f : 1.2f);
            else if (_board.IsFixed(cell))
                view.Wiggle();
            RefreshAll();
            return;
        }
        var move = _board.Place(cell, animal);
        switch (move.Kind)
        {
            case SudokuPlaceKind.Placed:
                view.SetAnimal(animal, pop: true);
                Audio.I.Pop(animal);
                App.Vibrate(10);
                if (move.Completed.Count > 0)
                    CelebrateLines(move.Completed, cell);
                break;
            case SudokuPlaceKind.Removed:
                HopOut(cell, animal);
                break;
            case SudokuPlaceKind.Clash:
                Clash(cell, animal, move.Clashes);
                break;
            case SudokuPlaceKind.Locked:
                view.Wiggle();
                Audio.I.Plop();
                break;
        }
        RefreshAll();
        if (move.Solved)
            Finish();
    }

    /// <summary>
    /// The clash: the new animal appears in the cell, it and the animal already there tilt their heads, "통", and the new
    /// one hops back out. Nothing red, no penalty.
    /// </summary>
    private async void Clash(int cell, int animal, IReadOnlyList<int> others)
    {
        if (_view is null)
            return;
        var token = _token;
        var view = _view;
        var size = view.CellSize;
        var ghost = new Node2D { Position = view.Position + view.CellCenter(cell) };
        var sprite = new Sprite2D { Texture = SudokuArt.Face(animal) };
        var k = SudokuArt.Fit(sprite.Texture, size * 0.8f);
        sprite.Scale = new Vector2(k, k);
        ghost.AddChild(sprite);
        ghost.Scale = new Vector2(0.3f, 0.3f);
        _fx.AddChild(ghost);
        Audio.I.Pop(animal);
        var tween = ghost.CreateTween();
        tween.TweenProperty(ghost, "scale", Vector2.One, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        foreach (var deg in new[] { -16f, 13f, -9f, 5f, 0f })
            tween.TweenProperty(ghost, "rotation_degrees", deg, 0.07f).SetTrans(Tween.TransitionType.Sine);
        foreach (var other in others)
            view.Cells[other].Wobble();

        await Wait(0.15f);
        if (!Alive(token))
            return;
        Audio.I.WallBump();
        App.Vibrate(15);
        if (_time - _lastClashTalk > 4f)
        {
            _lastClashTalk = _time;
            _buddy.Say($"{SudokuArt.Name(animal)}{Josa(animal)} 벌써 있어요!", 1.2f);
        }
        await Wait(0.2f);
        if (!IsInstanceValid(ghost))
            return;
        // The new animal hops back out
        var off = ghost.CreateTween().SetParallel();
        off.TweenProperty(ghost, "position", ghost.Position + new Vector2(size * 0.3f, -size * 0.45f), 0.16f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        off.TweenProperty(ghost, "scale", new Vector2(0.4f, 0.4f), 0.28f).SetDelay(0.08f);
        off.TweenProperty(ghost, "modulate:a", 0f, 0.2f).SetDelay(0.16f);
        off.Chain().TweenCallback(Callable.From(ghost.QueueFree));
        Audio.I.Plop();
    }

    /// <summary>"이/가" after an animal name: 사자가, 코끼리가, 펭귄이, 토끼가, 여우가, 판다가.</summary>
    private static string Josa(int animal) => animal == 3 ? "이" : "가";

    /// <summary>An animal that was taken out hops off and fades (the cell itself is already empty).</summary>
    private void HopOut(int cell, int animal)
    {
        if (_view is null)
            return;
        var size = _view.CellSize;
        var ghost = new Sprite2D
        {
            Texture = SudokuArt.Face(animal),
            Position = _view.Position + _view.CellCenter(cell),
        };
        var k = SudokuArt.Fit(ghost.Texture, size * 0.8f);
        ghost.Scale = new Vector2(k, k);
        _fx.AddChild(ghost);
        var tween = ghost.CreateTween().SetParallel();
        tween.TweenProperty(ghost, "position", ghost.Position + new Vector2(0, -size * 0.35f), 0.2f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(ghost, "scale", new Vector2(k * 0.3f, k * 0.3f), 0.25f).SetDelay(0.05f);
        tween.TweenProperty(ghost, "modulate:a", 0f, 0.2f).SetDelay(0.1f);
        tween.Chain().TweenCallback(Callable.From(ghost.QueueFree));
        Audio.I.Plop();
    }

    /// <summary>A finished row, column or box: its animals hop in a ripple with sparkles, and a rising "띵".</summary>
    private void CelebrateLines(IReadOnlyList<SudokuUnit> units, int from)
    {
        if (_view is null)
            return;
        var view = _view;
        var hopped = new HashSet<int>();
        foreach (var unit in units)
        {
            _linesDone++;
            Audio.I.MatchChime(_linesDone);
            // Ripple outward from the cell that finished it
            var cells = new List<int>(unit.Cells);
            cells.Sort((a, b) => view.CellCenter(a).DistanceTo(view.CellCenter(from))
                .CompareTo(view.CellCenter(b).DistanceTo(view.CellCenter(from))));
            for (var i = 0; i < cells.Count; i++)
            {
                if (!hopped.Add(cells[i]))
                    continue;
                view.Cells[cells[i]].Hop(0.12f + i * 0.06f);
                var at = view.Position + view.CellCenter(cells[i]);
                SparkleLater(at, 0.12f + i * 0.06f, view.CellSize);
            }
        }
        _buddy.Say(LineCheers[_rng.Next(LineCheers.Length)], 1.0f);
    }

    private async void SparkleLater(Vector2 at, float delay, float size)
    {
        var token = _token;
        await Wait(delay + 0.1f);
        if (!Alive(token))
            return;
        _fx.AddChild(SpriteBurst.Create(_starTexture, at + new Vector2(0, -size * 0.3f), 3, 180f, size * 0.18f, 0.45f, _rng, 0.8f));
    }

    // ---- Hint ----

    /// <summary>The right animal sparkles into a cell (the selected one if it is empty). No penalty: only counted.</summary>
    private void UseHint()
    {
        if (_view is null || _palette is null)
            return;
        var hint = _board.UseHint(_selected >= 0 ? _selected : null);
        if (hint is null)
            return;
        foreach (var c in hint.Cleared)
            if (LastShown(c) is var gone and > 0)
                HopOut(c, gone);
        if (LastShown(hint.Cell) is var wrong and > 0 && wrong != hint.Value)
            HopOut(hint.Cell, wrong);
        var cell = _view.Cells[hint.Cell];
        cell.SetAnimal(hint.Value, pop: true);
        _selected = hint.Cell;
        var at = _view.Position + _view.CellCenter(hint.Cell);
        var size = _view.CellSize;
        _fx.AddChild(Burst.Create(at, UiTheme.Yellow, 10, 260f, size * 0.05f, size * 0.7f, 0.45f, _rng));
        _fx.AddChild(SpriteBurst.Create(_starTexture, at, 5, 300f, size * 0.22f, 0.6f, _rng, 0.8f));
        Audio.I.StarPickup(2);
        _buddy.Say($"여기는 {SudokuArt.Name(hint.Value)}!", 1.2f);
        RefreshAll();
        if (hint.Completed.Count > 0)
            CelebrateLines(hint.Completed, hint.Cell);
        if (hint.Solved)
            Finish();
    }

    private readonly Dictionary<int, int> _shown = new();

    /// <summary>The animal a cell showed before the last change (for hopping a cleared animal out).</summary>
    private int LastShown(int cell) => _shown.TryGetValue(cell, out var v) ? v : 0;

    // ---- Look ----

    /// <summary>Brings every cell, the palette and the undo button up to date with the board.</summary>
    private void RefreshAll()
    {
        if (_view is null || _palette is null)
            return;
        var shape = _board.Shape;
        var focus = _held != 0 ? _held : _selected >= 0 ? _board.Value(_selected) : 0;
        var counts = new int[SudokuRules.MaxAnimals + 1];
        for (var i = 0; i < shape.CellCount; i++)
        {
            var value = _board.Value(i);
            counts[value]++;
            var cell = _view.Cells[i];
            if (_state != State.Intro)
                cell.SetAnimal(value);
            cell.SetMemos(_board.Memo(i));
            _shown[i] = value;
            var related = _selected >= 0 && i != _selected
                && (shape.Row(i) == shape.Row(_selected) || shape.Column(i) == shape.Column(_selected)
                    || shape.Box(i) == shape.Box(_selected));
            cell.SetLook(new CellLook(_board.IsFixed(i), related, focus != 0 && value == focus, i == _selected));
        }
        for (var v = 1; v <= shape.Size; v++)
            _palette.SetDone(v, counts[v] >= shape.Size);
        _palette.SetHeld(_held);
        _palette.SetUndoEnabled(_board.CanUndo);
    }

    // ---- Finish ----

    /// <summary>Climax: every animal hops in a wave across the board, fanfare, confetti, "완성!".</summary>
    private async void Finish()
    {
        if (_state != State.Play || _view is null)
            return;
        _state = State.Finishing;
        var token = _token;
        App.I.SetSetting(SettingsSection, IndexKey(_level), SudokuRules.Wrap(_level, _index + 1));
        _clockLabel.Text = $"{_board.TimeMs / 1000f:0.0}초";
        _held = 0;
        _selected = -1;
        RefreshAll();

        await Wait(0.35f);
        if (!Alive(token) || _view is null)
            return;
        var shape = _board.Shape;
        for (var i = 0; i < shape.CellCount; i++)
            _view.Cells[i].Hop(0.04f * (shape.Row(i) + shape.Column(i)), 0.35f);
        Audio.I.Fanfare();
        Audio.I.DuckMusic(-12f);
        App.Vibrate(60);
        _ui.AddChild(Confetti.Create(ScreenSize, 80));
        _buddy.Say("다 했다!", 1.6f);
        ShowBanner("완성!");

        await Wait(2.4f);
        if (!Alive(token))
            return;
        HideBanner();
        ShowResult();
    }

    private void ShowResult()
    {
        _state = State.Result;
        _hud.Visible = false;
        ClearBoard();
        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            VariantId = SudokuRules.VariantId(_level),
            Title = "완성!",
            Caption = "걸린 시간",
            Value = _board.TimeMs / 1000f,
            FormatValue = v => $"{v:0.0}초",
            Detail = _board.Hints == 0 ? "힌트 없이 했어요!" : $"힌트 {_board.Hints}번",
            Entry = _board.Entry("", App.Now()),
            Buttons = new[]
            {
                new ResultButton("다음 문제", UiTheme.Blue, () =>
                {
                    _index = SudokuRules.Wrap(_level, _index + 1);
                    StartRound();
                }, "res://assets/sprites/ui/play.png"),
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    // ---- Banner ----

    private Tween? _bannerTween;

    private void ShowBanner(string text)
    {
        _bannerTween?.Kill();
        _banner.Text = text;
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
        _banner.Scale = new Vector2(0.3f, 0.3f);
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.4f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
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
    /// Plays like a child who thinks it through: one "sure" cell at a time (an animal that is the only one to fit, or
    /// the only spot for an animal), about a second apart. Every other placement first tries an animal that is already
    /// in the row, column or box (to run the clash). Early on it also goes once through the other ways in: holding an
    /// animal and tapping, the pencil memo (6 x 6), a hint, undo and the eraser.
    /// </summary>
    private void AutoPlay(float dt)
    {
        _autoWait -= dt;
        if (_autoWait > 0)
            return;
        _autoWait = 0.9f;
        if (_autoSteps.Count == 0)
            PlanAutoBatch();
        if (_autoSteps.Count > 0)
            _autoSteps.Dequeue()();
    }

    private void PlanAutoBatch()
    {
        _autoBatch++;
        var shape = _board.Shape;
        if (SudokuSolver.NextSure(shape, _board.Grid) is not { } sure)
        {
            _autoSteps.Enqueue(() => OnTool(SudokuTool.Hint));
            return;
        }
        var (cell, value) = sure;
        var clash = 0;
        foreach (var p in shape.Peers(cell))
            if (_board.Value(p) != 0 && _board.Value(p) != value)
            {
                clash = _board.Value(p);
                break;
            }
        var other = value % shape.Size + 1;
        switch (_autoBatch)
        {
            case 2: // Animal first: hold it, tap the cell, let go
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                _autoSteps.Enqueue(() => TapCell(cell));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                return;
            case 3 when shape.Size > 4: // Pencil memo: two sketches, then the real animal (the sketches go away)
                _autoSteps.Enqueue(() => TapCell(cell));
                _autoSteps.Enqueue(() => OnTool(SudokuTool.Memo));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                _autoSteps.Enqueue(() => OnAnimalButton(other));
                _autoSteps.Enqueue(() => OnTool(SudokuTool.Memo));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                return;
            case 4: // A hint in the selected cell
                _autoSteps.Enqueue(() => TapCell(cell));
                _autoSteps.Enqueue(() => OnTool(SudokuTool.Hint));
                return;
            case 5: // Place, undo, place again
                _autoSteps.Enqueue(() => TapCell(cell));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                _autoSteps.Enqueue(() => OnTool(SudokuTool.Undo));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                return;
            case 6: // Place, erase, place again
                _autoSteps.Enqueue(() => TapCell(cell));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                _autoSteps.Enqueue(() => OnTool(SudokuTool.Erase));
                _autoSteps.Enqueue(() => OnAnimalButton(value));
                return;
        }
        _autoSteps.Enqueue(() => TapCell(cell));
        if (_autoBatch % 2 == 1 && clash != 0)
            _autoSteps.Enqueue(() => OnAnimalButton(clash));
        _autoSteps.Enqueue(() => OnAnimalButton(value));
    }

    // ---- Helpers ----

    /// <summary>False once the scene is gone or a newer round (or the setup screen) has started.</summary>
    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _token;

    /// <summary>Game-time wait: holds while the quit question pauses the tree.</summary>
    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
