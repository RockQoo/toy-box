using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Shikaku;

/// <summary>
/// 네모 나누기 (docs/games/16-shikaku.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → the board slides in and the number badges pop in one by one → drag from one
/// cell to another to draw a rectangle (a dashed frame with "N칸" follows the finger) → let go: if it holds one number
/// and has that many cells it fills with a pastel color (pieces it overlaps come out); otherwise the frame shakes away
/// and a short note says how many cells the number wants. Tap a piece to take it out. → the whole board covered →
/// "완성!" → shared ResultPanel (time; 보조 줄 "힌트 N번"). Any split that fits the rule is accepted, not only the
/// generator's answer. No countdown: the clock starts with the first input and holds while the quit question is open.
/// </summary>
public partial class ShikakuGame : Node2D
{
    private enum State { Setup, Intro, Play, Finishing, Result }

    private const string GameId = "shikaku";
    private const string SettingsSection = "shikaku";
    private const float SetupChipHeight = 86f;
    private const float Margin = 18f;
    private const float LeftWidth = 250f;
    private const float RightWidth = 190f;
    private const int NoTouch = int.MinValue;
    private const int KeyTouch = -2;     // Drawing with the keyboard
    private const int AutoTouch = 999;   // Drawing by the autoplay

    private static readonly string[] PlaceCheers = { "딱 맞아요!", "좋아요!", "멋져요!", "잘했어요!" };

    private readonly Random _rng = new();
    private readonly Dictionary<ShikakuPiece, ShikakuPieceView> _pieceViews = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private CanvasLayer _ui = null!;
    private Control _hud = null!;
    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private Label _puzzleLabel = null!;
    private Label _clockLabel = null!;
    private Label _banner = null!;
    private TextureButton _undoButton = null!;
    private TextureButton _resetButton = null!;
    private TextureButton _hintButton = null!;
    private ShikakuBoardView? _view;
    private Control? _setup;
    private Label? _setupInfo;
    private Texture2D _starTexture = null!;

    private ShikakuLevel _level;
    private int _index;
    private ShikakuBoard _board = new(ShikakuRules.Get(ShikakuLevel.Easy, 0));
    private int _shownSeconds = -1;
    private float _time;
    private int _token;
    private bool _hintBusy;
    private bool _keyboardUsed;
    private float _lastTalk = -10f;

    // Drawing
    private int _dragTouch = NoTouch;
    private int _dragStart;
    private int _dragEnd;
    private bool _dragFits;
    private ShikakuDragFrame? _dragFrame;
    private Label? _countLabel;
    private Label? _note;

    // Autoplay
    private readonly Queue<(float Wait, Action Step)> _autoSteps = new();
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

        _level = (ShikakuLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)ShikakuLevel.Easy), 0, 2);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        Layout();
        GetViewport().SizeChanged += Layout;
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    // ---- HUD: quit, which puzzle, a small faint clock (left); undo, start over, hint and the buddy (right) ----

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

        _right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _right.AddThemeConstantOverride("separation", 10);
        _hud.AddChild(_right);
        _undoButton = ToolButton("undo", "되돌리기", Undo);
        _resetButton = ToolButton("reset", "처음부터", ResetBoard);
        _hintButton = ToolButton("hint", "힌트", UseHint);

        _banner = Ui.Label("", 96, UiTheme.Yellow, 24, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _ui.AddChild(_banner);
    }

    private TextureButton ToolButton(string icon, string text, Action onPressed)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        var button = new TextureButton
        {
            TextureNormal = Ui.Tex($"res://assets/sprites/ui/{icon}.png"),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(88, 88),
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
        label.CustomMinimumSize = new Vector2(RightWidth - Margin, 0);
        column.AddChild(label);
        _right.AddChild(column);
        return button;
    }

    private void Layout()
    {
        var screen = ScreenSize;
        _left.Position = new Vector2(Margin, Margin);
        _left.Size = new Vector2(LeftWidth - Margin * 2, 0);
        _right.Position = new Vector2(screen.X - RightWidth, Margin);
        _right.Size = new Vector2(RightWidth - Margin, 0);
        _buddy.Position = new Vector2(screen.X - RightWidth / 2 - Margin / 2, screen.Y - 16);
        _banner.Position = Vector2.Zero;
        _banner.Size = screen;
        _banner.PivotOffset = screen / 2;
        PlaceBoard();
    }

    /// <summary>The board as big as fits between the side panels (cells about 140 / 112 / 85 px at 1280 x 720).</summary>
    private void PlaceBoard()
    {
        if (_view is null)
            return;
        var screen = ScreenSize;
        var area = new Rect2(LeftWidth, Margin, screen.X - LeftWidth - RightWidth - Margin, screen.Y - Margin * 2);
        var max = _view.Size switch { 4 => 140f, 5 => 112f, _ => 85f };
        var cell = ShikakuBoardView.FitCell(_view.Size, area.Size, max);
        if (!Mathf.IsEqualApprox(cell, _view.CellSize))
        {
            _view.SetCellSize(cell);
            RebuildPieceViews();
        }
        _view.Position = (area.GetCenter() - _view.GridSize / 2).Round();
    }

    // ---- Setup (꾸미기) ----

    private static string IndexKey(ShikakuLevel level) => $"next_{ShikakuRules.VariantId(level)}";

    private static int SavedIndex(ShikakuLevel level) =>
        ShikakuRules.Wrap(level, (int)App.I.GetSetting(SettingsSection, IndexKey(level), 0));

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
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/shikaku.png", 64));
        titleRow.AddChild(Ui.Label("네모 나누기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        const string badge = "res://assets/sprites/shikaku/clue_badge.png";
        var levelChoices = new[]
        {
            new Choice(ShikakuRules.Variants[0].Label, badge),
            new Choice(ShikakuRules.Variants[1].Label, badge),
            new Choice(ShikakuRules.Variants[2].Label, badge),
        };
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 150, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (ShikakuLevel)i;
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
        var n = ShikakuRules.Size(_level);
        _setupInfo.Text = $"{n} x {n} · 숫자 1~{ShikakuRules.MaxClue(_level)} · {SavedIndex(_level) + 1}번 문제";
    }

    private void StartFromSetup()
    {
        _index = SavedIndex(_level);
        StartRound();
    }

    private void ClearBoard()
    {
        CancelDrag();
        _view?.QueueFree();
        _view = null;
        _pieceViews.Clear();
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
        _board = new ShikakuBoard(ShikakuRules.Get(_level, _index));
        _shownSeconds = -1;
        _hintBusy = false;
        _autoSteps.Clear();
        _autoWait = 0;
        _autoBatch = 0;

        _view = ShikakuBoardView.Create(_board.Puzzle, 100f);
        _world.AddChild(_view);
        _world.MoveChild(_buddy, -1); // The buddy's speech bubble goes over the board
        _world.MoveChild(_fx, -1);
        PlaceBoard();
        _view.SetCursor(_keyboardUsed ? 0 : -1);

        _puzzleLabel.Text = $"{ShikakuRules.Variants[(int)_level].Label} · {_index + 1}번 문제";
        _clockLabel.Text = "끌면 시계가 가요";
        _hud.Visible = true;
        _banner.Visible = false;
        RefreshTools();
        foreach (var badge in _view.Badges)
            badge.Scale = Vector2.Zero;
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
        Ui.PopIn(_right, 0.15f);

        // 2. The number badges pop in one by one ("톡톡")
        await Wait(0.4f);
        if (!Alive(token) || _view is null)
            return;
        var delay = 0f;
        var step = _view.Size == 7 ? 0.06f : 0.09f;
        foreach (var cell in _board.Puzzle.ClueCells)
        {
            if (_view.Badge(cell) is not { } badge)
                continue;
            var pop = badge.CreateTween();
            pop.TweenInterval(delay);
            pop.TweenProperty(badge, "scale", Vector2.One, 0.25f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            delay += step;
        }
        PlopRun(token, (int)(delay / 0.09f) + 1);
        await Wait(delay + 0.3f);
        if (!Alive(token))
            return;
        _buddy.Say("숫자만큼 네모를 그려요!", 1.8f);
        _state = State.Play;
        RefreshTools();
    }

    /// <summary>Soft "톡" sounds while the badges pop in.</summary>
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
            case InputEventScreenTouch { Pressed: true } touch when _dragTouch == NoTouch:
                var cell = _view.CellAt(_view.ToLocal(touch.Position));
                if (cell >= 0)
                {
                    BeginDrag(cell, touch.Index);
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventScreenDrag drag when drag.Index == _dragTouch:
                MoveDrag(_view.CellAtClamped(_view.ToLocal(drag.Position)));
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch { Pressed: false } touch when touch.Index == _dragTouch:
                MoveDrag(_view.CellAtClamped(_view.ToLocal(touch.Position)));
                EndDrag();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true } key:
                if (HandleKey(key))
                    GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>
    /// PC keys: arrows / WASD move an orange cursor, Space or Enter starts a rectangle at the cursor and the second
    /// press lets go of it, Esc drops it, Delete / Backspace takes out the piece under the cursor, Z/U undo, R start
    /// over, H hint.
    /// </summary>
    private bool HandleKey(InputEventKey key)
    {
        if (_view is null)
            return false;
        var code = key.PhysicalKeycode;
        int? dx = code switch { Key.Left or Key.A => -1, Key.Right or Key.D => 1, _ => null };
        int? dy = code switch { Key.Up or Key.W => -1, Key.Down or Key.S => 1, _ => null };
        if (dx is not null || dy is not null)
        {
            if (_dragTouch is not (NoTouch or KeyTouch))
                return true;
            var n = _view.Size;
            var at = _view.Cursor;
            if (!_keyboardUsed || at < 0)
            {
                _keyboardUsed = true;
                at = 0;
            }
            else
                at = Mathf.Clamp(at / n + (dy ?? 0), 0, n - 1) * n + Mathf.Clamp(at % n + (dx ?? 0), 0, n - 1);
            _view.SetCursor(at);
            _board.Start();
            if (_dragTouch == KeyTouch)
                MoveDrag(at);
            else
                Audio.I.Tick(1.4f);
            return true;
        }
        if (key.Echo)
            return false;
        switch (code)
        {
            case Key.Space or Key.Enter or Key.KpEnter:
                if (_view.Cursor < 0)
                {
                    _keyboardUsed = true;
                    _view.SetCursor(0);
                }
                if (_dragTouch == KeyTouch)
                    EndDrag();
                else if (_dragTouch == NoTouch)
                    BeginDrag(_view.Cursor, KeyTouch);
                return true;
            case Key.Escape when _dragTouch == KeyTouch:
                FadeFrame(TakeFrame());
                return true;
            case Key.Delete or Key.Backspace:
                if (_view.Cursor >= 0 && _dragTouch == NoTouch)
                    RemovePiece(_view.Cursor);
                return true;
            case Key.Z or Key.U:
                Undo();
                return true;
            case Key.R:
                ResetBoard();
                return true;
            case Key.H:
                UseHint();
                return true;
        }
        return false;
    }

    // ---- Drawing ----

    private void BeginDrag(int cell, int touch)
    {
        if (_state != State.Play || _view is null || _hintBusy)
            return;
        CancelDrag();
        _board.Start();
        _dragTouch = touch;
        _dragStart = cell;
        _dragEnd = cell;
        _dragFits = false;
        _dragFrame = ShikakuDragFrame.Create(_view.CellSize);
        _view.DragLayer.AddChild(_dragFrame);
        _countLabel = Ui.Label("", (int)Mathf.Clamp(_view.CellSize * 0.36f, 28, 44), Colors.White, 10, UiTheme.Ink);
        _countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _view.DragLayer.AddChild(_countLabel);
        UpdateDragLook();
        Audio.I.Tick(1.2f);
    }

    private void MoveDrag(int cell)
    {
        if (_dragFrame is null || cell == _dragEnd)
            return;
        _dragEnd = cell;
        var wasFit = _dragFits;
        UpdateDragLook();
        // A tiny "틱" per cell change; a brighter one when the size matches the number
        Audio.I.Tick(_dragFits && !wasFit ? 2.0f : 1.0f + Mathf.Min(DragRect.Area, 12) * 0.05f);
    }

    private ShikakuRect DragRect
    {
        get
        {
            var n = _board.Size;
            return ShikakuRect.FromCorners(_dragStart / n, _dragStart % n, _dragEnd / n, _dragEnd % n);
        }
    }

    /// <summary>The frame snaps to the cells; blue while drawing, bright green when the size matches the one number inside.</summary>
    private void UpdateDragLook()
    {
        if (_view is null || _dragFrame is null || _countLabel is null)
            return;
        var rect = DragRect;
        var check = ShikakuSolver.Check(_board.Puzzle, rect);
        _dragFits = check.Fit == ShikakuFit.Fits;
        var pixels = _view.RectOf(rect);
        _dragFrame.SetRect(pixels);
        if (_dragFits)
            _dragFrame.SetColor(new Color(0.45f, 1f, 0.45f), new Color(0.6f, 1f, 0.6f, 0.30f));
        else
            _dragFrame.SetColor(new Color(0.35f, 0.62f, 1f), new Color(1f, 1f, 1f, 0.22f));
        _countLabel.Text = $"{rect.Area}칸";
        _countLabel.AddThemeColorOverride("font_color", _dragFits ? UiTheme.Yellow : Colors.White);
        _countLabel.ResetSize();
        // Beside the frame's bottom-right corner, kept on the screen
        var size = _countLabel.Size;
        var at = pixels.End + new Vector2(6, -size.Y * 0.4f);
        var screenRight = _view.ToLocal(new Vector2(ScreenSize.X - RightWidth, 0)).X;
        if (at.X + size.X > screenRight)
            at.X = pixels.Position.X - size.X - 6;
        var bottom = _view.ToLocal(new Vector2(0, ScreenSize.Y - 8)).Y;
        at.Y = Mathf.Min(at.Y, bottom - size.Y);
        _countLabel.Position = at;
    }

    /// <summary>Lets go of the drawing frame: a tap takes out a piece (or places a 1), a rectangle is checked.</summary>
    private void EndDrag()
    {
        if (_dragFrame is null || _view is null)
            return;
        var rect = DragRect;
        var tap = _dragStart == _dragEnd;
        var frame = TakeFrame();
        if (tap)
        {
            var cell = _dragStart;
            var clue = _board.Puzzle.Clues[cell];
            if (_board.PieceAt(cell) is not null)
            {
                FadeFrame(frame);
                RemovePiece(cell);
                return;
            }
            if (clue != 1)
            {
                FadeFrame(frame);
                if (clue > 1)
                {
                    _view.WiggleBadge(cell);
                    Note(cell, $"끌어서 {clue}칸 네모를 그려요");
                }
                return;
            }
        }
        TryDraw(rect, frame);
    }

    /// <summary>Detaches the current frame (and its count label) from the drawing state.</summary>
    private ShikakuDragFrame? TakeFrame()
    {
        var frame = _dragFrame;
        _countLabel?.QueueFree();
        _countLabel = null;
        _dragFrame = null;
        _dragTouch = NoTouch;
        return frame;
    }

    private void CancelDrag()
    {
        if (_dragFrame is not null)
            TakeFrame()?.QueueFree();
        _dragTouch = NoTouch;
    }

    private static void FadeFrame(ShikakuDragFrame? frame)
    {
        if (frame is null)
            return;
        var tween = frame.CreateTween();
        tween.TweenProperty(frame, "modulate:a", 0f, 0.15f);
        tween.TweenCallback(Callable.From(frame.QueueFree));
    }

    private void TryDraw(ShikakuRect rect, ShikakuDragFrame? frame)
    {
        var move = _board.Draw(rect);
        switch (move.Kind)
        {
            case ShikakuDrawKind.Placed:
                FadeFrame(frame);
                Placed(move, hinted: false);
                break;
            case ShikakuDrawKind.NotPlaced:
                NotPlaced(move, frame);
                break;
            default:
                FadeFrame(frame);
                break;
        }
    }

    /// <summary>A piece went in: its color spreads from the middle, the badge hops, a "뿅" that climbs with each piece.</summary>
    private void Placed(ShikakuMove move, bool hinted)
    {
        if (_view is null || move.Piece is null)
            return;
        Sync(move.Piece);
        _view.HopBadge(move.Piece.ClueCell, 0.08f);
        Audio.I.Pop(_board.Pieces.Count);
        App.Vibrate(10);
        var center = _view.Position + _view.RectOf(move.Rect).GetCenter();
        var size = _view.CellSize;
        _fx.AddChild(SpriteBurst.Create(_starTexture, center, hinted ? 6 : 3, 220f, size * 0.2f, 0.5f, _rng, 0.8f));
        if (move.Removed.Count > 0)
            Audio.I.Plop();
        RefreshTools();
        if (move.Solved)
        {
            Finish();
            return;
        }
        if (!hinted && _time - _lastTalk > 5f && _rng.Next(3) == 0)
        {
            _lastTalk = _time;
            _buddy.Say(PlaceCheers[_rng.Next(PlaceCheers.Length)], 1.0f);
        }
    }

    /// <summary>
    /// A rectangle that breaks the rule: the frame shakes a little and fades, and a short note says what is wanted
    /// ("6칸 → 4칸이 필요해요" next to the number). Nothing red, no penalty.
    /// </summary>
    private void NotPlaced(ShikakuMove move, ShikakuDragFrame? frame)
    {
        if (_view is null)
            return;
        if (frame is not null)
        {
            var x = frame.Position.X;
            var tween = frame.CreateTween();
            foreach (var dx in new[] { -10f, 9f, -6f, 4f, 0f })
                tween.TweenProperty(frame, "position:x", x + dx, 0.05f).SetTrans(Tween.TransitionType.Sine);
            tween.TweenProperty(frame, "modulate:a", 0f, 0.25f);
            tween.TweenCallback(Callable.From(frame.QueueFree));
        }
        Audio.I.WallBump();
        App.Vibrate(15);
        var check = move.Check;
        switch (check.Fit)
        {
            case ShikakuFit.WrongSize:
                _view.WiggleBadge(check.ClueCell);
                Note(check.ClueCell, $"{check.Area}칸 → {check.Clue}칸이 필요해요");
                break;
            case ShikakuFit.NoClue:
                NoteAt(_view.RectOf(move.Rect).GetCenter(), "숫자를 하나 넣어 그려요");
                break;
            case ShikakuFit.TooManyClues:
                foreach (var cell in move.Rect.Cells(_board.Size))
                    if (_board.Puzzle.Clues[cell] > 0)
                        _view.WiggleBadge(cell);
                NoteAt(_view.RectOf(move.Rect).GetCenter(), "숫자는 하나만 넣어요");
                break;
        }
    }

    private void Note(int cell, string text)
    {
        if (_view is null)
            return;
        NoteAt(_view.CellCenter(cell) - new Vector2(0, _view.CellSize * 0.55f), text);
    }

    /// <summary>A short note (board coordinates) that rises a little and fades: white with a dark outline, never red.</summary>
    private void NoteAt(Vector2 boardPoint, string text)
    {
        if (_view is null)
            return;
        if (_note is not null && IsInstanceValid(_note))
            _note.QueueFree(); // One note at a time
        var label = Ui.Label(text, 30, Colors.White, 10, UiTheme.Ink);
        _note = label;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        _fx.AddChild(label);
        label.ResetSize();
        var screen = ScreenSize;
        var at = _view.Position + boardPoint - new Vector2(label.Size.X / 2, label.Size.Y);
        at.X = Mathf.Clamp(at.X, LeftWidth - 20, screen.X - RightWidth - label.Size.X + 20);
        at.Y = Mathf.Clamp(at.Y, 8, screen.Y - label.Size.Y - 8);
        label.Position = at;
        label.PivotOffset = label.Size / 2;
        label.Scale = new Vector2(0.6f, 0.6f);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "scale", Vector2.One, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(1.3f);
        tween.TweenProperty(label, "position:y", at.Y - 24, 0.4f);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 0.4f);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    private void RemovePiece(int cell)
    {
        if (_state != State.Play || _hintBusy)
            return;
        if (_board.Remove(cell) is null)
            return;
        Sync(null);
        Audio.I.Plop();
        RefreshTools();
    }

    // ---- Tools ----

    private void Undo()
    {
        if (_state != State.Play || _hintBusy)
            return;
        CancelDrag();
        if (!_board.Undo())
            return;
        Sync(null);
        Audio.I.Whoosh();
        RefreshTools();
    }

    private void ResetBoard()
    {
        if (_state != State.Play || _hintBusy)
            return;
        CancelDrag();
        if (!_board.Clear())
            return;
        Sync(null);
        Audio.I.Whoosh();
        _buddy.Say("처음부터 다시!", 1.0f);
        RefreshTools();
    }

    /// <summary>The hint: an answer piece's dashed frame blinks gold, then the piece goes in. Only counted, no penalty.</summary>
    private async void UseHint()
    {
        if (_state != State.Play || _hintBusy || _view is null)
            return;
        CancelDrag();
        if (_board.HintPiece() is not { } rect)
            return;
        _board.Start();
        _hintBusy = true;
        var token = _token;
        var frame = ShikakuDragFrame.Create(_view.CellSize);
        frame.SetRect(_view.RectOf(rect));
        frame.SetColor(UiTheme.Gold, new Color(1f, 0.95f, 0.5f, 0.3f));
        _view.DragLayer.AddChild(frame);
        var blink = frame.CreateTween().SetLoops(3);
        blink.TweenProperty(frame, "modulate:a", 0.35f, 0.12f);
        blink.TweenProperty(frame, "modulate:a", 1f, 0.12f);
        Audio.I.StarPickup(2);
        await Wait(0.75f);
        if (!Alive(token))
            return;
        _hintBusy = false;
        FadeFrame(frame);
        if (_state != State.Play || _board.UseHint() is not { } move)
            return;
        _buddy.Say($"여기는 {move.Check.Clue}칸!", 1.2f);
        Placed(move, hinted: true);
    }

    // ---- Look ----

    private Color TintOf(ShikakuPiece piece)
    {
        var order = 0;
        var clues = _board.Puzzle.ClueCells;
        for (var i = 0; i < clues.Count; i++)
            if (clues[i] == piece.ClueCell)
                order = i;
        return ShikakuBoardView.Pastels[order % ShikakuBoardView.Pastels.Length];
    }

    /// <summary>Brings the piece nodes up to date with the board: gone pieces shrink away, new ones appear.</summary>
    private void Sync(ShikakuPiece? fresh)
    {
        if (_view is null)
            return;
        var current = new HashSet<ShikakuPiece>(_board.Pieces);
        foreach (var (piece, node) in _pieceViews.ToList())
        {
            if (current.Contains(piece))
                continue;
            node.Vanish();
            _pieceViews.Remove(piece);
        }
        foreach (var piece in _board.Pieces)
        {
            if (_pieceViews.ContainsKey(piece))
                continue;
            var node = ShikakuPieceView.Create(_view.RectOf(piece.Rect), TintOf(piece), _view.CellSize);
            _view.PieceLayer.AddChild(node);
            _pieceViews[piece] = node;
            node.Appear(big: piece == fresh);
        }
    }

    /// <summary>After a cell-size change: every piece node is made again at the new size.</summary>
    private void RebuildPieceViews()
    {
        if (_view is null)
            return;
        foreach (var node in _pieceViews.Values)
            node.QueueFree();
        _pieceViews.Clear();
        foreach (var piece in _board.Pieces)
        {
            var node = ShikakuPieceView.Create(_view.RectOf(piece.Rect), TintOf(piece), _view.CellSize);
            _view.PieceLayer.AddChild(node);
            _pieceViews[piece] = node;
        }
    }

    private void RefreshTools()
    {
        SetEnabled(_undoButton, _board.CanUndo);
        SetEnabled(_resetButton, _board.Pieces.Count > 0 && !_board.Solved);
        SetEnabled(_hintButton, !_board.Solved);
    }

    private static void SetEnabled(TextureButton button, bool enabled)
    {
        button.Disabled = !enabled;
        button.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.4f);
    }

    // ---- Finish ----

    /// <summary>Climax: the pieces sparkle one after another in a rainbow wave, fanfare, confetti, "완성!".</summary>
    private async void Finish()
    {
        if (_state != State.Play || _view is null)
            return;
        _state = State.Finishing;
        var token = _token;
        CancelDrag();
        _view.SetCursor(-1);
        App.I.SetSetting(SettingsSection, IndexKey(_level), ShikakuRules.Wrap(_level, _index + 1));
        _clockLabel.Text = $"{_board.TimeMs / 1000f:0.0}초";
        RefreshTools();

        await Wait(0.35f);
        if (!Alive(token) || _view is null)
            return;
        var pieces = _board.Pieces.OrderBy(p => p.Rect.Row + p.Rect.Col).ThenBy(p => p.Rect.Row).ToList();
        for (var i = 0; i < pieces.Count; i++)
        {
            if (!_pieceViews.TryGetValue(pieces[i], out var node))
                continue;
            var delay = i * 0.09f;
            var hue = (float)i / pieces.Count;
            node.Rainbow(delay, Color.FromHsv(hue, 0.55f, 1f));
            _view.HopBadge(pieces[i].ClueCell, delay);
            SparkleLater(_view.Position + _view.RectOf(pieces[i].Rect).GetCenter(), delay, _view.CellSize);
        }
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

    private async void SparkleLater(Vector2 at, float delay, float size)
    {
        var token = _token;
        await Wait(delay + 0.05f);
        if (!Alive(token))
            return;
        _fx.AddChild(SpriteBurst.Create(_starTexture, at, 3, 200f, size * 0.2f, 0.5f, _rng, 0.8f));
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
            VariantId = ShikakuRules.VariantId(_level),
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
                    _index = ShikakuRules.Wrap(_level, _index + 1);
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
    /// Draws the answer pieces about a second apart, dragging corner to corner like a finger (the piece a hint would
    /// pick next, so it never needs to take anything out). Every third piece it first draws one with a wrong size.
    /// Early on it also goes once through the other ways: tapping a piece out, undo, a hint, and start over + undo.
    /// </summary>
    private void AutoPlay(float dt)
    {
        if (_hintBusy)
            return;
        _autoWait -= dt;
        if (_autoWait > 0)
            return;
        if (_autoSteps.Count == 0)
            PlanAutoBatch();
        if (_autoSteps.Count == 0)
            return;
        var (wait, step) = _autoSteps.Dequeue();
        _autoWait = wait;
        step();
    }

    private void PlanAutoBatch()
    {
        if (_board.HintPiece() is not { } target)
            return;
        _autoBatch++;
        var n = _board.Size;
        switch (_autoBatch)
        {
            case 1:
                AutoDrawWrongThenRight(target);
                return;
            case 2: // Draw, tap it out, draw again
                AutoDraw(target);
                AutoTap(target.Row * n + target.Col);
                AutoDraw(target);
                return;
            case 3: // Draw, undo, then let a hint place one
                AutoDraw(target);
                _autoSteps.Enqueue((0.8f, Undo));
                _autoSteps.Enqueue((1.2f, UseHint));
                return;
            case 4 when _board.Pieces.Count > 0: // Start over, then undo brings everything back
                _autoSteps.Enqueue((0.8f, ResetBoard));
                _autoSteps.Enqueue((0.8f, Undo));
                return;
        }
        if (_autoBatch % 3 == 0)
            AutoDrawWrongThenRight(target);
        else
            AutoDraw(target);
    }

    private void AutoDrawWrongThenRight(ShikakuRect target)
    {
        var n = _board.Size;
        ShikakuRect wrong;
        if (target.Right + 1 < n)
            wrong = target with { Cols = target.Cols + 1 };
        else if (target.Col > 0)
            wrong = target with { Col = target.Col - 1, Cols = target.Cols + 1 };
        else if (target.Bottom + 1 < n)
            wrong = target with { Rows = target.Rows + 1 };
        else
            wrong = target with { Row = target.Row - 1, Rows = target.Rows + 1 };
        AutoDraw(wrong);
        AutoDraw(target);
    }

    /// <summary>Press on one corner, slide through the middle to the other corner, let go.</summary>
    private void AutoDraw(ShikakuRect rect)
    {
        var n = _board.Size;
        var from = rect.Row * n + rect.Col;
        var to = rect.Bottom * n + rect.Right;
        var mid = (rect.Row + rect.Rows / 2) * n + rect.Col + rect.Cols / 2;
        if (from == to)
        {
            AutoTap(from);
            return;
        }
        _autoSteps.Enqueue((0.2f, () => BeginDrag(from, AutoTouch)));
        _autoSteps.Enqueue((0.2f, () => MoveDrag(mid)));
        _autoSteps.Enqueue((0.35f, () => MoveDrag(to)));
        _autoSteps.Enqueue((0.6f, EndDrag));
    }

    private void AutoTap(int cell)
    {
        _autoSteps.Enqueue((0.15f, () => BeginDrag(cell, AutoTouch)));
        _autoSteps.Enqueue((0.6f, EndDrag));
    }

    // ---- Helpers ----

    /// <summary>False once the scene is gone or a newer round (or the setup screen) has started.</summary>
    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _token;

    /// <summary>Game-time wait: holds while the quit question pauses the tree.</summary>
    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
