using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;
using ToyBox.Games.Maze;

namespace ToyBox.Games.PushBox;

/// <summary>
/// 상자 밀기 (docs/games/17-push-box.md), Sokoban for children.
/// Flow: Setup (난이도, 배경, 응원 친구, with a preview of the next board) → the board slides in, the boxes drop "톡톡",
/// the rabbit drops onto its cell → "상자를 별 자리에 넣어요!" → walk / push one cell per input (arrow keys / WASD, the
/// on-screen pad, or a swipe on the board; holding repeats) → every box on a star spot → "다 넣었어요!" → shared
/// ResultPanel (다음 판 / 다시 하기 / 메인으로). Walls and box pairs only go "통". Undo is unlimited (Z / Backspace),
/// start over (R) can be undone too. A box that can no longer reach a star spot sways sleepily, the buddy suggests undo
/// and the undo button glows. The clock starts with the first move and holds while the quit question is open.
/// </summary>
public partial class PushBoxGame : Node2D
{
    private enum State { Setup, Intro, Play, Finishing, Result }

    private const string GameId = "pushbox";
    private const string SettingsSection = "pushbox";
    private const float SetupChipHeight = 84f;
    private const float Margin = 18f;
    private const float PadButton = 118f;
    private const float PadGap = 12f;
    private const float BoardGap = 28f;
    private const float MaxCell = 112f;
    private const float PreviewSize = 330f;

    private const float StepTime = 0.14f;
    private const float PushTime = 0.17f;
    private const float BumpTime = 0.24f;
    private const float RepeatDelay = 0.3f;
    private const float RepeatEvery = 0.19f;

    private static readonly string[] HomeWords = { "쏙!", "딱 맞아!", "들어갔다!" };
    private static readonly Color DimSlot = new(1f, 1f, 1f, 0.3f);
    private static readonly Color Dust = new(0.78f, 0.66f, 0.48f);

    private readonly Random _rng = new();
    private readonly List<PushBoxCrate> _crates = new();
    private readonly List<TextureRect> _slots = new();
    private readonly HashSet<int> _stuck = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private CanvasLayer _ui = null!;
    private Buddy _buddy = null!;
    private Control _play = null!;
    private VBoxContainer _side = null!;
    private HBoxContainer _slotRow = null!;
    private DirPad _pad = null!;
    private Label _boardLabel = null!;
    private Label _boxLabel = null!;
    private Label _clockLabel = null!;
    private Label _banner = null!;
    private TextureButton _undoButton = null!;
    private TextureButton _resetButton = null!;
    private Tween? _undoGlow;
    private Node2D _fx = null!;
    private Control? _setup;
    private Control? _previewHost;
    private Label? _previewCaption;
    private Label? _setupInfo;
    private Texture2D _starTexture = null!;
    private Texture2D _boxTexture = null!;
    private Texture2D _doneTexture = null!;

    private PushBoxLevel _level;
    private int _index;
    private PushBoxRun _run = null!;
    private PushBoxBoardView? _board;
    private PushBoxRabbit? _rabbit;
    private int _token;
    private int _shownSeconds = -1;
    private Rect2 _boardArea;

    // Input
    private MazeDir? _pending;
    private MazeDir? _padHeld;
    private MazeDir? _lastKey;
    private bool _pendingFromPad;
    private bool _pendingUndo;
    private float _cooldown;
    private float _repeat;
    private Vector2? _swipeFrom;

    // Autoplay
    private readonly Queue<MazeDir> _route = new();
    private float _autoWait;
    private int _autoMoves;
    private int _autoTrick;
    private int _autoStuck;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");
        _boxTexture = Ui.Tex("res://assets/sprites/pushbox/box.png");
        _doneTexture = Ui.Tex("res://assets/sprites/pushbox/box_done.png");

        _scenery = new Scenery();
        AddChild(_scenery);
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _ui = new CanvasLayer();
        AddChild(_ui);

        _level = (PushBoxLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)PushBoxLevel.Easy), 0, 2);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park), 0, 3);

        BuildPlayUi();
        _fx = new Node2D();
        _ui.AddChild(_fx);
        _buddy = new Buddy();
        _ui.AddChild(_buddy); // Over the HUD, so its speech bubble is never hidden
        _buddy.SetCharacter(BuddyIndex());
        Layout();
        GetViewport().SizeChanged += Layout;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    /// <summary>The cheering buddy (panda by default: the player is already a rabbit).</summary>
    private static int BuddyIndex() =>
        Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "buddy", 1), 0, Buddy.Choices.Count - 1);

    // ---- Layout: the board as big as fits on the left, the HUD and the D-pad in a column on the right ----

    private float ColumnWidth => PadButton * 3 + PadGap * 2;

    private void Layout()
    {
        var screen = ScreenSize;
        var columnX = screen.X - Margin - ColumnWidth;
        _side.Position = new Vector2(columnX, Margin);
        _side.Size = new Vector2(ColumnWidth, screen.Y - Margin * 2);
        _boardArea = new Rect2(Margin, Margin, columnX - BoardGap - Margin, screen.Y - Margin * 2);
        _banner.Position = _boardArea.Position;
        _banner.Size = _boardArea.Size;
        _banner.PivotOffset = _boardArea.Size / 2;
        // The buddy stands right above the pad
        var padTop = screen.Y - Margin - (PadButton * 2 + PadGap);
        _buddy.Position = new Vector2(columnX + ColumnWidth / 2, padTop - 6);
        PlaceBoard();
    }

    private float CellFor(PushBoxPuzzle p) =>
        Mathf.Floor(Mathf.Min(MaxCell, Mathf.Min(_boardArea.Size.X / p.Width, _boardArea.Size.Y / p.Height)));

    private void PlaceBoard()
    {
        if (_board is null || _rabbit is null)
            return;
        _board.SetCellSize(CellFor(_run.Puzzle));
        _board.Position = (_boardArea.GetCenter() - _board.GridSize / 2).Round();
        _rabbit.SetCellSize(_board.CellSize);
        _rabbit.Snap(_board.CellCenter(_run.Player));
        foreach (var crate in _crates)
        {
            crate.SetCellSize(_board.CellSize);
            crate.Snap(_board.CellCenter(_run.Boxes[crate.Index]));
        }
    }

    // ---- Play UI: board name, quit, box slots, clock and steps, undo / start over, the D-pad ----

    private void BuildPlayUi()
    {
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_play);

        _side = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _side.AddThemeConstantOverride("separation", 14);
        _play.AddChild(_side);

        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 10);
        var namePanel = Ui.Panel(Colors.White with { A = 0.92f }, 10, 4);
        namePanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        namePanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _boardLabel = Ui.Label("", 24, UiTheme.InkSoft);
        _boardLabel.HorizontalAlignment = HorizontalAlignment.Center;
        namePanel.AddChild(_boardLabel);
        top.AddChild(namePanel);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);
        _side.AddChild(top);

        // Box slots "📦📦▢ 상자 2/3" and the clock with the steps
        var stats = Ui.Panel(Colors.White with { A = 0.94f }, 14, 4);
        var statBody = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        statBody.AddThemeConstantOverride("separation", 6);
        stats.AddChild(statBody);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        _slotRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _slotRow.AddThemeConstantOverride("separation", 2);
        row.AddChild(_slotRow);
        _boxLabel = Ui.Label("", 32, UiTheme.Orange, 8, Colors.White);
        _boxLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_boxLabel);
        statBody.AddChild(row);
        _clockLabel = Ui.Label("", 24, UiTheme.InkSoft);
        _clockLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statBody.AddChild(_clockLabel);
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
        // Undo and start over fill the two empty corners beside the pad's ▲ (the buddy stands above, its speech
        // bubble never covers them)
        _undoButton = ToolButton(0, "undo", "되돌리기", () => _pendingUndo = true);
        _resetButton = ToolButton(2, "reset", "처음부터", ResetBoard);
        _side.AddChild(_pad);

        _banner = Ui.Label("", 72, UiTheme.Yellow, 22, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _play.AddChild(_banner);
    }

    /// <summary>A tool button with its name under it, put in place of the pad's spacer at <paramref name="slot"/>.</summary>
    private TextureButton ToolButton(int slot, string icon, string text, Action onPressed)
    {
        var column = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(PadButton, PadButton),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", 0);
        var button = new TextureButton
        {
            TextureNormal = Ui.Tex($"res://assets/sprites/ui/{icon}.png"),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(80, 80),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        Ui.MakeSquishy(button);
        button.Pressed += () =>
        {
            Ui.Punch(button, 0.2f);
            onPressed();
        };
        column.AddChild(button);
        var label = Ui.Label(text, 22, UiTheme.Ink, 6, Colors.White);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(label);
        var spacer = _pad.GetChild(slot);
        _pad.RemoveChild(spacer);
        spacer.QueueFree();
        _pad.AddChild(column);
        _pad.MoveChild(column, slot);
        return button;
    }

    // ---- Setup (난이도, 배경, 응원 친구) ----

    private static string IndexKey(PushBoxLevel level) => $"next_{PushBoxRules.VariantId(level)}";

    /// <summary>The board of this level to play next (advances when a board is finished).</summary>
    private static int SavedIndex(PushBoxLevel level) =>
        PushBoxRules.Wrap(level, (int)App.I.GetSetting(SettingsSection, IndexKey(level), 0));

    private void ShowSetup()
    {
        _state = State.Setup;
        _token++;
        _play.Visible = false;
        ClearBoard();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/push_box.png", 64));
        titleRow.AddChild(Ui.Label("상자 밀기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        const string box = "res://assets/sprites/pushbox/box.png";
        var levelChoices = PushBoxRules.Variants.Select(v => new Choice(v.Label, box)).ToList();
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 150, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (PushBoxLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
            RefreshPreview();
        };
        body.AddChild(levelRow);
        _setupInfo = Ui.Label("", 24, UiTheme.InkSoft);
        _setupInfo.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_setupInfo);

        var sceneryRow = ChoiceRow.Create("배경", Scenery.Choices, (int)_scenery.Kind, chipHeight: SetupChipHeight);
        sceneryRow.Selected += i =>
        {
            _scenery.Kind = (SceneryKind)i;
            App.I.SetSetting(SettingsSection, "scenery", i);
        };
        body.AddChild(sceneryRow);

        var buddyRow = ChoiceRow.Create("응원 친구", Buddy.Choices, BuddyIndex(), 110, chipHeight: SetupChipHeight);
        buddyRow.Selected += i =>
        {
            _buddy.SetCharacter(i);
            App.I.SetSetting(SettingsSection, "buddy", i);
        };
        body.AddChild(buddyRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 76)));
        buttons.AddChild(Ui.Button("시작!", StartFromSetup, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        // Left: the options. Right: the board "시작!" will play.
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
        _buddy.Visible = false; // The setup panel has the buddy chips; it stands by the pad in play
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();
        RefreshPreview();

        if (App.I.Autoplay)
            GetTree().CreateTimer(1.2f).Timeout += () =>
            {
                if (_state == State.Setup)
                    StartFromSetup();
            };
    }

    /// <summary>Draws the board "시작!" will play (the level's next board) at a small size, popping in.</summary>
    private void RefreshPreview()
    {
        if (_previewHost is null || _previewCaption is null)
            return;
        foreach (var child in _previewHost.GetChildren())
            child.QueueFree();
        var index = SavedIndex(_level);
        var p = PushBoxRules.Get(_level, index);
        _previewCaption.Text = $"{index + 1}번 판";
        if (_setupInfo is not null)
            _setupInfo.Text = $"상자 {PushBoxRules.BoxRange(_level).Min}~{PushBoxRules.BoxRange(_level).Max}개 · 판 5개";
        var cell = Mathf.Floor(PreviewSize / Mathf.Max(p.Width, p.Height));
        var view = PushBoxBoardView.Create(p, cell);
        view.Position = ((new Vector2(PreviewSize, PreviewSize) - view.GridSize) / 2).Round();
        _previewHost.AddChild(view);
        for (var i = 0; i < p.Boxes.Count; i++)
        {
            var crate = new PushBoxCrate { Index = i };
            view.Pieces.AddChild(crate);
            crate.SetCellSize(cell);
            crate.Snap(view.CellCenter(p.Boxes[i]));
        }
        var rabbit = new PushBoxRabbit();
        view.Pieces.AddChild(rabbit);
        rabbit.SetCellSize(cell);
        rabbit.Snap(view.CellCenter(p.Start));
        Ui.PopIn(_previewHost, 0f, 0.3f);
    }

    private void StartFromSetup()
    {
        _index = SavedIndex(_level);
        StartRound();
    }

    // ---- Round ----

    private async void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        _previewHost = null;
        _previewCaption = null;
        _setupInfo = null;
        ResultPanel.CloseAll(_ui);
        ClearBoard();

        _token++;
        var token = _token;
        _state = State.Intro;
        _run = new PushBoxRun(PushBoxRules.Get(_level, _index));
        _pending = null;
        _padHeld = null;
        _pendingUndo = false;
        _swipeFrom = null;
        _cooldown = 0;
        _shownSeconds = -1;
        _stuck.Clear();
        _route.Clear();
        _autoWait = 0;
        _autoMoves = 0;
        _autoTrick = 0;
        _autoStuck = 0;

        var p = _run.Puzzle;
        _board = PushBoxBoardView.Create(p, CellFor(p));
        _world.AddChild(_board);
        for (var i = 0; i < p.Boxes.Count; i++)
        {
            var crate = new PushBoxCrate { Index = i };
            _board.Pieces.AddChild(crate);
            _crates.Add(crate);
        }
        _rabbit = new PushBoxRabbit();
        _board.Pieces.AddChild(_rabbit);
        PlaceBoard();

        // HUD
        _boardLabel.Text = $"{PushBoxRules.Variants[(int)_level].Label} · {_index + 1}번 판";
        foreach (var slot in _slots)
            slot.QueueFree();
        _slots.Clear();
        var slotSize = p.Boxes.Count >= 4 ? 44f : 50f;
        for (var i = 0; i < p.Boxes.Count; i++)
        {
            var slot = new TextureRect
            {
                Texture = _boxTexture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(slotSize, slotSize),
                SelfModulate = DimSlot,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _slotRow.AddChild(slot);
            _slots.Add(slot);
        }
        RefreshBoxCount(false);
        _clockLabel.Text = "움직이면 시계가 가요";
        _banner.Visible = false;
        _play.Visible = true;
        _buddy.Visible = true;
        StopUndoGlow();
        RefreshTools();
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic();

        // 1. The board fades and slides in
        var rest = _board.Position;
        _board.Position = rest + new Vector2(0, 40);
        _board.Modulate = Colors.Transparent;
        var tween = _board.CreateTween().SetParallel();
        tween.TweenProperty(_board, "position", rest, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_board, "modulate", Colors.White, 0.2f);
        Audio.I.Whoosh();

        // 2. The boxes drop "톡톡", then the rabbit
        for (var i = 0; i < _crates.Count; i++)
        {
            var delay = 0.35f + i * 0.16f;
            _crates[i].DropIn(delay);
            var pitch = 1.1f + 0.12f * i;
            GetTree().CreateTimer(delay + 0.3f).Timeout += () =>
            {
                if (Alive(token))
                    Audio.I.CountStep(pitch);
            };
        }
        var drop = 0.45f + _crates.Count * 0.16f;
        _rabbit.DropIn(drop);
        await Wait(drop + 0.38f);
        if (!Alive(token))
            return;
        Audio.I.Step(false);
        ShowBanner("상자를 별 자리에 넣어요!", 56, UiTheme.Yellow, 0.9f);
        _buddy.Say("영차영차!", 1.2f);
        Audio.I.Go();
        _state = State.Play;
    }

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
                RefreshClock();
            }
        }

        _cooldown -= dt;
        _repeat -= dt;
        if (App.I.Autoplay)
            AutoPlay(dt);
        if (_cooldown > 0)
            return;
        if (_pendingUndo)
        {
            _pendingUndo = false;
            _pending = null;
            Undo();
        }
        else if (_pending is { } fresh)
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

    private void RefreshClock() =>
        _clockLabel.Text = _run.Started ? $"{_run.TimeMs / 1000}초 · {_run.Steps}걸음" : "움직이면 시계가 가요";

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
            case InputEventKey { Pressed: true } key when key.PhysicalKeycode is Key.Z or Key.Backspace:
                // Holding the key repeats (the OS echo), one step back each time
                if (_state == State.Play)
                    _pendingUndo = true;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key when key.PhysicalKeycode == Key.R:
                if (_state == State.Play)
                    ResetBoard();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
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
        if (_board is null || _rabbit is null)
            return;
        var step = _run.Move(dir);
        if (!fromPad)
            _pad.Flash(dir);
        switch (step.Kind)
        {
            case PushBoxMoveKind.Bumped:
            {
                // Light: "통", a nudge. A box that cannot go gives a tiny shove too. No penalty.
                _cooldown = BumpTime;
                var ahead = step.From.Step(dir);
                var box = _run.BoxAt(ahead);
                _rabbit.Bump(dir, box >= 0);
                Audio.I.WallBump();
                App.Vibrate(15);
                if (box >= 0)
                    _crates[box].Nudge(PushBoxRabbit.DirVector(dir));
                else
                {
                    var wall = (_board.CellCenter(step.From) + _board.CellCenter(ahead)) / 2;
                    _fx.AddChild(Burst.Create(_board.GlobalPosition + wall, UiTheme.Green, 5, 140f, _board.CellSize * 0.05f, 0f, 0.3f, _rng));
                }
                RefreshClock();
                return;
            }
            case PushBoxMoveKind.Moved:
                _cooldown = StepTime;
                _rabbit.StepTo(_board.CellCenter(step.To), StepTime, dir, false);
                Audio.I.Step(_run.Steps % 2 == 0);
                break;
            case PushBoxMoveKind.Pushed:
            {
                // Push: the rabbit leans in, the box scrapes along ("드르륵") with a little dust
                _cooldown = PushTime;
                _rabbit.StepTo(_board.CellCenter(step.To), PushTime, dir, true);
                var crate = _crates[step.Box];
                crate.SlideTo(_board.CellCenter(step.BoxTo), PushTime);
                Audio.I.Step(_run.Steps % 2 == 0);
                Audio.I.CardFlip(false);
                var back = _board.GlobalPosition + _board.CellCenter(step.To) + PushBoxRabbit.DirVector(dir) * _board.CellSize * 0.2f
                    + new Vector2(0, _board.CellSize * 0.3f);
                _fx.AddChild(Burst.Create(back, Dust, 6, 110f, _board.CellSize * 0.05f, _board.CellSize * 0.2f, 0.35f, _rng));
                var onGoal = _run.IsOnGoal(step.Box);
                if (onGoal != _run.Puzzle.IsGoal(step.To))
                    BoxChanged(step.Box, onGoal, _token);
                break;
            }
            default:
                return;
        }
        RefreshClock();
        RefreshTools();
        if (_run.Solved)
        {
            Finish();
            return;
        }
        CheckStuck();
    }

    /// <summary>
    /// A box went onto a star spot (the important moment: it lands, hops into the sparkling box, stars burst, a rising
    /// "띵동", its HUD slot lights up) or came off one (quietly back to the plain box).
    /// </summary>
    private async void BoxChanged(int box, bool onGoal, int token)
    {
        await Wait(PushTime * 0.9f);
        if (!Alive(token) || _board is null || _state is not (State.Play or State.Finishing))
            return;
        if (_run.IsOnGoal(box) != onGoal)
            return; // Undone meanwhile
        var crate = _crates[box];
        if (!onGoal)
        {
            crate.SetDone(false, false);
            RefreshBoxCount(false);
            return;
        }
        crate.SetDone(true, true);
        var count = _run.BoxesOnGoals;
        Audio.I.MatchChime(count);
        App.Vibrate(30);
        var at = _board.GlobalPosition + _board.CellCenter(_run.Boxes[box]);
        var cell = _board.CellSize;
        _fx.AddChild(Burst.Create(at, UiTheme.Yellow, 10, 300f, cell * 0.06f, cell * 0.6f, 0.45f, _rng));
        _fx.AddChild(SpriteBurst.Create(_starTexture, at, 5, 340f, cell * 0.26f, 0.6f, _rng, 0.8f));
        if (!_run.Solved)
            _fx.AddChild(new FloatingText
            {
                Text = HomeWords[_rng.Next(HomeWords.Length)],
                Color = UiTheme.Yellow,
                FontSize = 38,
                Position = at + new Vector2(0, -cell * 0.7f),
            });
        RefreshBoxCount(true);
    }

    /// <summary>"상자 2/3" and the slot icons; <paramref name="punch"/> pops the newest lit slot.</summary>
    private void RefreshBoxCount(bool punch)
    {
        var count = _run.BoxesOnGoals;
        for (var i = 0; i < _slots.Count; i++)
        {
            var lit = i < count;
            var slot = _slots[i];
            slot.Texture = lit ? _doneTexture : _boxTexture;
            slot.SelfModulate = lit ? Colors.White : DimSlot;
            if (punch && i == count - 1)
            {
                Ui.Punch(slot, 0.6f);
                _fx.AddChild(SpriteBurst.Create(_starTexture, slot.GlobalPosition + slot.Size / 2, 4, 220f, 18f, 0.45f, _rng, 0.3f));
            }
        }
        _boxLabel.Text = $"상자 {count}/{_slots.Count}";
        if (punch)
            Ui.Punch(_boxLabel, 0.3f);
    }

    // ---- Undo / start over ----

    private void Undo()
    {
        if (_state != State.Play || _board is null || _rabbit is null)
            return;
        var before = _run.Undo();
        if (before is null)
        {
            Audio.I.WallBump();
            return;
        }
        Ui.Punch(_undoButton, 0.15f);
        ShowLayout(before, StepTime);
        _cooldown = StepTime;
        Audio.I.CardFlip(false);
        RefreshTools();
        CheckStuck();
    }

    private void ResetBoard()
    {
        if (_state != State.Play || _board is null || _rabbit is null)
            return;
        var before = _run.Reset();
        if (before is null)
            return;
        ShowLayout(before, 0.35f);
        _cooldown = 0.35f;
        _pending = null;
        Audio.I.Whoosh();
        RefreshTools();
        CheckStuck();
    }

    /// <summary>Moves the pieces from the layout <paramref name="before"/> to the run's current one.</summary>
    private void ShowLayout(PushBoxSnapshot before, float duration)
    {
        if (_board is null || _rabbit is null)
            return;
        var target = _board.CellCenter(_run.Player);
        if (before.Player.ManhattanTo(_run.Player) <= 1)
            _rabbit.SlideBack(target, duration);
        else
            _rabbit.PopTo(target);
        foreach (var crate in _crates)
        {
            var now = _run.Boxes[crate.Index];
            if (now != before.Boxes[crate.Index])
                crate.SlideTo(_board.CellCenter(now), duration);
            crate.SetDone(_run.IsOnGoal(crate.Index), false);
        }
        RefreshBoxCount(false);
        RefreshClock();
    }

    private void RefreshTools()
    {
        SetEnabled(_undoButton, _run.CanUndo);
        SetEnabled(_resetButton, _run.CanReset);
    }

    private static void SetEnabled(TextureButton button, bool enabled)
    {
        button.Disabled = !enabled;
        button.SelfModulate = enabled ? Colors.White : new Color(1, 1, 1, 0.4f);
    }

    // ---- Stuck boxes ----

    /// <summary>
    /// A box that can no longer reach a star spot: it sways sleepily, the buddy suggests undo and the undo button glows
    /// until nothing is stuck. Said once per newly stuck box, never in red, no penalty.
    /// </summary>
    private void CheckStuck()
    {
        var stuck = _run.StuckBoxes();
        var fresh = stuck.Where(s => !_stuck.Contains(s.Box)).ToList();
        _stuck.Clear();
        foreach (var s in stuck)
            _stuck.Add(s.Box);
        foreach (var crate in _crates)
            crate.SetSleepy(_stuck.Contains(crate.Index));
        if (fresh.Count > 0)
        {
            var frozen = fresh.Any(s => s.Frozen);
            _buddy.Say(frozen ? "이 상자는 못 움직여요.\n되돌려 볼까?" : "이 상자는 별 자리에 못 가요.\n되돌려 볼까?", 2.6f);
            Audio.I.Retry();
            if (_board is not null)
            {
                var at = _board.GlobalPosition + _board.CellCenter(_run.Boxes[fresh[0].Box]);
                _fx.AddChild(new FloatingText
                {
                    Text = "쿨쿨",
                    Color = new Color(0.75f, 0.82f, 1f),
                    FontSize = 30,
                    Position = at + new Vector2(_board.CellSize * 0.3f, -_board.CellSize * 0.6f),
                });
            }
        }
        if (_stuck.Count > 0)
            StartUndoGlow();
        else
            StopUndoGlow();
    }

    private void StartUndoGlow()
    {
        if (_undoGlow is not null)
            return;
        _undoButton.PivotOffset = _undoButton.Size / 2;
        _undoGlow = _undoButton.CreateTween().SetLoops();
        _undoGlow.TweenProperty(_undoButton, "modulate", new Color(1.35f, 1.3f, 0.75f), 0.4f).SetTrans(Tween.TransitionType.Sine);
        _undoGlow.Parallel().TweenProperty(_undoButton, "scale", new Vector2(1.12f, 1.12f), 0.4f).SetTrans(Tween.TransitionType.Sine);
        _undoGlow.TweenProperty(_undoButton, "modulate", Colors.White, 0.4f).SetTrans(Tween.TransitionType.Sine);
        _undoGlow.Parallel().TweenProperty(_undoButton, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Sine);
    }

    private void StopUndoGlow()
    {
        _undoGlow?.Kill();
        _undoGlow = null;
        _undoButton.Modulate = Colors.White;
        _undoButton.Scale = Vector2.One;
    }

    // ---- Finish ----

    /// <summary>Climax: every box hops in turn with sparkles, the rabbit cheers, fanfare, confetti, "다 넣었어요!".</summary>
    private async void Finish()
    {
        _state = State.Finishing;
        var token = _token;
        _padHeld = null;
        _pending = null;
        StopUndoGlow();
        foreach (var crate in _crates)
            crate.SetSleepy(false);
        App.I.SetSetting(SettingsSection, IndexKey(_level), PushBoxRules.Wrap(_level, _index + 1));
        _clockLabel.Text = $"{_run.TimeMs / 1000f:0.0}초 · {_run.Steps}걸음";

        await Wait(PushTime + 0.45f);
        if (!Alive(token) || _board is null)
            return;
        for (var i = 0; i < _crates.Count; i++)
        {
            var delay = i * 0.12f;
            _crates[i].Hop(delay, 0.5f, 1.15f);
            var at = _board.GlobalPosition + _board.CellCenter(_run.Boxes[i]);
            SparkleLater(at, delay, _board.CellSize, token);
        }
        _rabbit?.Celebrate();
        Audio.I.Fanfare();
        Audio.I.DuckMusic(-12f);
        App.Vibrate(60);
        _ui.AddChild(Confetti.Create(ScreenSize, 80));
        _buddy.Say("다 넣었다!", 1.6f);
        ShowBanner("다 넣었어요!", 100, UiTheme.Yellow);

        await Wait(2.4f);
        if (!Alive(token))
            return;
        HideBanner();
        ShowResult();
    }

    private async void SparkleLater(Vector2 at, float delay, float size, int token)
    {
        await Wait(delay + 0.12f);
        if (!Alive(token))
            return;
        _fx.AddChild(SpriteBurst.Create(_starTexture, at, 4, 260f, size * 0.24f, 0.55f, _rng, 0.8f));
    }

    private void ShowResult()
    {
        _state = State.Result;
        _play.Visible = false;
        _buddy.Visible = false;
        ClearBoard();
        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            VariantId = PushBoxRules.VariantId(_level),
            Title = "다 넣었어요!",
            Caption = "걸린 시간",
            Value = _run.TimeMs / 1000f,
            FormatValue = v => $"{v:0.0}초",
            Detail = $"{_run.Steps}걸음",
            Entry = _run.Entry("", App.Now()),
            Buttons = new[]
            {
                new ResultButton("다음 판", UiTheme.Blue, () =>
                {
                    _index = PushBoxRules.Wrap(_level, _index + 1);
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
        _rabbit = null;
        _crates.Clear();
        foreach (var child in _fx.GetChildren())
            child.QueueFree();
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
    /// Follows the solver's shortest walk like a child who knows the way. Along the way it tries the other paths once
    /// each: a step the wrong way taken back with undo, a box pushed where it gets stuck (the notice) taken back, a bump,
    /// and start over taken back with undo.
    /// </summary>
    private void AutoPlay(float dt)
    {
        _autoWait -= dt;
        if (_pending is not null || _pendingUndo || _cooldown > 0 || _autoWait > 0)
            return;
        _autoWait = 0.22f + (float)_rng.NextDouble() * 0.12f;

        if (_route.Count == 0 && _autoMoves == 0)
        {
            var moves = PushBoxSolver.ShortestMoves(_run.Puzzle, _run.Player, _run.Boxes) ?? new List<MazeDir>();
            foreach (var dir in moves)
                _route.Enqueue(dir);
        }
        if (_route.Count == 0)
            return;

        // Once: push a box where it gets stuck, let the buddy and the glowing undo button speak, then undo
        if (_autoStuck == 1)
        {
            _autoStuck = 2;
            _pendingUndo = true;
            _autoWait = 0.6f;
            return;
        }
        if (_autoStuck == 0 && _autoTrick is not (1 or 4) && _autoMoves >= 1)
            for (var d = 0; d < 4; d++)
                if (_run.PushWouldStick((MazeDir)d))
                {
                    _pending = (MazeDir)d;
                    _autoStuck = 1;
                    _autoWait = 3f;
                    return;
                }

        switch (_autoTrick)
        {
            case 0 when _autoMoves == 2: // A wrong step, then undo
            {
                var next = _route.Peek();
                for (var d = 0; d < 4; d++)
                {
                    var dir = (MazeDir)d;
                    if (dir == next || _run.Puzzle.IsWall(_run.Player.Step(dir)) || _run.BoxAt(_run.Player.Step(dir)) >= 0)
                        continue;
                    _pending = dir;
                    _autoTrick = 1;
                    return;
                }
                _autoTrick = 2;
                break;
            }
            case 1:
                _pendingUndo = true;
                _autoTrick = 2;
                _autoWait = 0.5f;
                return;
            case 2 when _autoMoves == 4: // A bump
            {
                _autoTrick = 3;
                for (var d = 0; d < 4; d++)
                    if (_run.Puzzle.IsWall(_run.Player.Step((MazeDir)d)))
                    {
                        _pending = (MazeDir)d;
                        return;
                    }
                break;
            }
            case 3 when _autoMoves >= 6 && _route.Count > 2 && _run.CanReset: // Start over, then undo brings it all back
                _autoTrick = 4;
                ResetBoard();
                _autoWait = 0.8f;
                return;
            case 4:
                _autoTrick = 5;
                _pendingUndo = true;
                _autoWait = 0.6f;
                return;
        }
        _pending = _route.Dequeue();
        _autoMoves++;
    }

    // ---- Helpers ----

    /// <summary>False once the scene is gone or a newer round (or the setup screen) has started.</summary>
    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _token;

    /// <summary>Game-time wait: holds while the quit question pauses the tree.</summary>
    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
