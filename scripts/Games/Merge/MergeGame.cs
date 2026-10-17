using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Merge;

/// <summary>
/// 숫자 합치기 (docs/games/07-merge.md).
/// Flow: Setup (판 크기, 배경, 응원 친구) → two tiles pop in → push with arrow keys / WASD or a swipe on the board →
/// tiles slide and equal neighbors join (each tile once per push) → a new tile only after a push that moved something →
/// when no push can move, the buddy suggests 되돌리기 (3 per round) and "끝내기" appears → "끝!" → shared ResultPanel.
/// No timer and no game over: being stuck is just the end of the round. 128, 256 … 2048 are celebrated the first time.
/// </summary>
public partial class MergeGame : Node2D
{
    private enum State { Setup, Intro, Play, Stuck, Ending, Result }

    private const string GameId = "merge";
    private const string SettingsSection = "merge";
    private const float SetupChipHeight = 84f;
    private const float Margin = 18f;
    private const float ColumnWidth = 300f;
    private const float BoardGap = 28f;
    private const float SlideTime = 0.12f;
    private const float BumpTime = 0.16f;

    private static readonly string[] Cheers = { "좋아!", "잘한다!", "멋져!", "우와!" };

    private readonly Random _rng = new();
    private readonly Dictionary<(int, int), MergeTile> _tiles = new();
    private readonly HashSet<int> _celebrated = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private CanvasLayer _ui = null!;
    private Control _play = null!;
    private Label _scoreLabel = null!;
    private Label _bestLabel = null!;
    private Button _undo = null!;
    private Button _finish = null!;
    private Label _banner = null!;
    private Node2D _fx = null!;
    private Buddy _buddy = null!;
    private Control? _setup;

    private MergeLevel _level;
    private MergeBoard _model = new(4);
    private MergeBoardView? _board;
    private Rect2 _boardArea;
    private int _roundToken;
    private int _pushes;

    // Input
    private MergeDir? _pending;
    private float _cooldown;
    private Vector2? _swipeFrom;

    // Autoplay
    private float _autoWait;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _scenery = new Scenery();
        AddChild(_scenery);
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _ui = new CanvasLayer();
        AddChild(_ui);

        _level = (MergeLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "size", (int)MergeLevel.Easy), 0, 1);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park);

        BuildPlayUi();
        _buddy = new Buddy();
        _world.AddChild(_buddy);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));
        _fx = new Node2D();
        _ui.AddChild(_fx);
        Layout();
        GetViewport().SizeChanged += Layout;
        // A 5x5 round can run past 8000 frames: play it at triple speed in a headless smoke test
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 3.0;

        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    // ---- Layout: the board as big as fits on the left, the score column on the right ----

    private void Layout()
    {
        var screen = ScreenSize;
        var columnX = screen.X - Margin - ColumnWidth;
        _boardArea = new Rect2(Margin, Margin, columnX - BoardGap - Margin, screen.Y - Margin * 2);
        var column = _play.GetNode<VBoxContainer>("Column");
        column.Position = new Vector2(columnX, Margin);
        column.Size = new Vector2(ColumnWidth, screen.Y - Margin * 2 - 170);
        _banner.Position = _boardArea.Position;
        _banner.Size = _boardArea.Size;
        _banner.PivotOffset = _boardArea.Size / 2;
        _buddy.Position = new Vector2(columnX + ColumnWidth / 2, screen.Y - 16);
        PlaceBoard();
    }

    private float CellFor(int n) => Mathf.Floor(Mathf.Min(_boardArea.Size.X, _boardArea.Size.Y) / (n + 0.4f));

    private void PlaceBoard()
    {
        if (_board is null)
            return;
        _board.SetCellSize(CellFor(_board.Size));
        _board.Position = (_boardArea.GetCenter() - _board.GridSize / 2).Round();
        foreach (var ((x, y), tile) in _tiles)
            tile.Position = _board.CellCenter(x, y);
    }

    // ---- Play UI: quit, score, biggest tile, undo, finish ----

    private void BuildPlayUi()
    {
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_play);

        var column = new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 14);
        _play.AddChild(column);

        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(0, 56));
        column.AddChild(quit);

        var stats = Ui.Panel(Colors.White with { A = 0.94f }, 14, 4);
        var statBody = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        statBody.AddThemeConstantOverride("separation", 2);
        stats.AddChild(statBody);
        var caption = Ui.Label("점수", 26, UiTheme.InkSoft);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        statBody.AddChild(caption);
        _scoreLabel = Ui.Label("0", 56, UiTheme.Orange, 10, Colors.White);
        _scoreLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statBody.AddChild(_scoreLabel);
        _bestLabel = Ui.Label("", 24, UiTheme.InkSoft);
        _bestLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statBody.AddChild(_bestLabel);
        column.AddChild(stats);

        _undo = Ui.Button("되돌리기", Undo, UiTheme.Blue, 28, Ui.Tex("res://assets/sprites/ui/undo.png"), new Vector2(0, 80));
        column.AddChild(_undo);
        _finish = Ui.Button("끝내기", Finish, UiTheme.Pink, 28, null, new Vector2(0, 72));
        _finish.Visible = false;
        column.AddChild(_finish);

        _banner = Ui.Label("", 72, UiTheme.Yellow, 22, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _play.AddChild(_banner);
    }

    // ---- Setup (판 크기, 배경, 응원 친구) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _play.Visible = false;
        ResultPanel.CloseAll(_ui);
        ClearBoard();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var title = Ui.Label("숫자 합치기", 42, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);

        var sizeChoices = new List<Choice>();
        foreach (MergeLevel level in Enum.GetValues(typeof(MergeLevel)))
        {
            var n = MergeRules.Size(level);
            sizeChoices.Add(new Choice($"{MergeRules.Variants[(int)level].Label} {n}x{n}"));
        }
        var sizeRow = ChoiceRow.Create("판 크기", sizeChoices, (int)_level, 170, chipHeight: SetupChipHeight);
        sizeRow.Selected += i =>
        {
            _level = (MergeLevel)i;
            App.I.SetSetting(SettingsSection, "size", i);
        };
        body.AddChild(sizeRow);

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
            App.I.SetSetting(SettingsSection, "buddy", i);
            _buddy.SetCharacter(i);
            _buddy.Say("같이 하자!", 0.9f);
        };
        body.AddChild(buddyRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 76)));
        buttons.AddChild(Ui.Button("시작!", StartRound, UiTheme.Green, 40, Ui.Tex("res://assets/sprites/ui/play.png"),
            new Vector2(300, 76)));
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

    private async void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        ClearBoard();

        _roundToken++;
        var token = _roundToken;
        _state = State.Intro;
        var n = MergeRules.Size(_level);
        _model = new MergeBoard(n);
        _celebrated.Clear();
        _pending = null;
        _swipeFrom = null;
        _cooldown = 0;
        _pushes = 0;
        _autoWait = 0;

        _board = MergeBoardView.Create(n, CellFor(n));
        _world.AddChild(_board);
        PlaceBoard();
        UpdateHud();
        _finish.Visible = false;
        _banner.Visible = false;
        _play.Visible = true;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic();

        var rest = _board.Position;
        _board.Position = rest + new Vector2(0, 40);
        _board.Modulate = Colors.Transparent;
        var tween = _board.CreateTween().SetParallel();
        tween.TweenProperty(_board, "position", rest, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_board, "modulate", Colors.White, 0.2f);
        Audio.I.Whoosh();

        await Wait(0.4f);
        if (!Alive(token))
            return;
        for (var i = 0; i < 2; i++)
        {
            SpawnTile(i * 0.15f);
        }
        await Wait(0.4f);
        if (!Alive(token))
            return;
        ShowBanner("밀어서 합쳐요!", 60, UiTheme.Yellow, 0.7f);
        Audio.I.Go();
        _state = State.Play;
    }

    private void SpawnTile(float delay = 0f)
    {
        if (_board is null || _model.Spawn(_rng) is not { } spawned)
            return;
        var tile = MergeTile.Create(spawned.Value, _board.CellSize);
        tile.Position = _board.CellCenter(spawned.X, spawned.Y);
        _board.AddChild(tile);
        _tiles[(spawned.X, spawned.Y)] = tile;
        tile.PopIn(delay);
        Audio.I.Plop();
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _cooldown -= dt;
        if (App.I.Autoplay)
            AutoPlay(dt);
        if (_state != State.Play || _cooldown > 0 || _pending is not { } dir)
            return;
        _pending = null;
        Push(dir);
    }

    // ---- Input ----

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when KeyDir(key.PhysicalKeycode) is { } dir:
                if (_state == State.Play)
                    _pending = dir;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
                _swipeFrom = touch.Pressed && _board is not null && BoardRect().Grow(60).HasPoint(touch.Position)
                    ? touch.Position
                    : null;
                break;
            case InputEventScreenDrag drag when _swipeFrom is { } from && _state == State.Play:
                var delta = drag.Position - from;
                if (delta.Length() >= Mathf.Max(30f, (_board?.CellSize ?? 100f) * 0.3f))
                {
                    _pending = Mathf.Abs(delta.X) > Mathf.Abs(delta.Y)
                        ? delta.X > 0 ? MergeDir.Right : MergeDir.Left
                        : delta.Y > 0 ? MergeDir.Down : MergeDir.Up;
                    _swipeFrom = null; // One push per touch
                }
                break;
        }
    }

    private Rect2 BoardRect() => _board is null ? new Rect2() : new Rect2(_board.GlobalPosition, _board.GridSize);

    private static MergeDir? KeyDir(Key key) => key switch
    {
        Key.Up or Key.W => MergeDir.Up,
        Key.Right or Key.D => MergeDir.Right,
        Key.Down or Key.S => MergeDir.Down,
        Key.Left or Key.A => MergeDir.Left,
        _ => null,
    };

    // ---- Push ----

    private async void Push(MergeDir dir)
    {
        if (_board is null)
            return;
        var move = _model.Push(dir);
        if (!move.Moved)
        {
            // Nothing can go that way: the board leans a little, "통". No new tile.
            _cooldown = BumpTime;
            var (dx, dy) = MergeRules.Delta(dir);
            var rest = _board.Position;
            var nudge = _board.CreateTween();
            nudge.TweenProperty(_board, "position", rest + new Vector2(dx, dy) * 10f, 0.06f);
            nudge.TweenProperty(_board, "position", rest, 0.1f);
            Audio.I.WallBump();
            return;
        }

        _cooldown = SlideTime + 0.03f;
        _pushes++;
        var token = _roundToken;
        var board = _board;
        var old = new Dictionary<(int, int), MergeTile>(_tiles);
        _tiles.Clear();
        var leaving = new List<MergeTile>();
        foreach (var s in move.Slides)
        {
            if (!old.TryGetValue((s.FromX, s.FromY), out var tile))
                continue;
            tile.SlideTo(board.CellCenter(s.ToX, s.ToY), SlideTime);
            if (s.Merged)
                leaving.Add(tile);
            else
                _tiles[(s.ToX, s.ToY)] = tile;
        }
        Audio.I.Whoosh();

        await Wait(SlideTime);
        if (!Alive(token))
            return;
        foreach (var tile in leaving)
            tile.QueueFree();

        var biggestJoin = 0;
        foreach (var join in move.Joins)
        {
            var tile = MergeTile.Create(join.Value, board.CellSize);
            tile.Position = board.CellCenter(join.X, join.Y);
            board.AddChild(tile);
            _tiles[(join.X, join.Y)] = tile;
            tile.Punch();
            biggestJoin = Math.Max(biggestJoin, join.Value);
            var at = board.GlobalPosition + tile.Position;
            _fx.AddChild(Burst.Create(at, UiTheme.Yellow, 6, 220f, board.CellSize * 0.04f, board.CellSize * 0.4f, 0.35f, _rng));
            _fx.AddChild(new FloatingText
            {
                Text = $"+{join.Value}",
                Color = UiTheme.Orange,
                FontSize = 34,
                Position = at + new Vector2(0, -board.CellSize * 0.45f),
            });
        }
        if (biggestJoin > 0)
        {
            Audio.I.Pop(Math.Min(MergeRules.TileArt(biggestJoin), 10));
            App.Vibrate(15);
            if (_rng.NextDouble() < 0.12)
                _buddy.Hop();
        }

        SpawnTile();
        UpdateHud();
        Ui.Punch(_scoreLabel, move.Gained > 0 ? 0.25f : 0f);
        Celebrate(move, token);

        if (!_model.CanMove())
            Stuck(token);
    }

    /// <summary>The first 128, 256 … of the round: a sparkle shower, a word on the board, the buddy cheers.</summary>
    private void Celebrate(MergeMove move, int token)
    {
        var milestone = 0;
        foreach (var join in move.Joins)
            foreach (var m in MergeRules.Milestones)
                if (join.Value == m && !_celebrated.Contains(m))
                    milestone = Math.Max(milestone, m);
        if (milestone == 0 || _board is null || !Alive(token))
            return;
        _celebrated.Add(milestone);
        Audio.I.Cheer();
        Audio.I.StarPickup(Array.IndexOf(MergeRules.Milestones, milestone) + 1);
        _buddy.Say($"{milestone} 만들었다!");
        ShowBanner($"{milestone} 만들었다!", 72, UiTheme.Yellow, 0.9f);
        var star = Ui.Tex("res://assets/sprites/ui/star.png");
        _fx.AddChild(SpriteBurst.Create(star, _boardArea.GetCenter(), 10, 620f, _board.CellSize * 0.35f, 1f, _rng, 1f));
        if (milestone >= 2048)
            _fx.AddChild(Confetti.Create(ScreenSize, 80));
    }

    private void UpdateHud()
    {
        _scoreLabel.Text = _model.Score.ToString();
        var biggest = _model.BiggestTile;
        _bestLabel.Text = biggest > 0 ? $"가장 큰 타일 {biggest}" : "";
        _undo.Text = $"되돌리기 {_model.UndosLeft}";
        _undo.Disabled = !_model.CanUndo || _state is State.Ending or State.Result;
    }

    // ---- Stuck, undo, finish ----

    /// <summary>No push can move. With undos left the buddy suggests one and "끝내기" appears; otherwise the round ends.</summary>
    private async void Stuck(int token)
    {
        _state = State.Stuck;
        await Wait(0.4f);
        if (!Alive(token) || _state != State.Stuck)
            return;
        if (_model.CanUndo)
        {
            _buddy.Say("되돌려 볼까?", 2.2f);
            Ui.Wiggle(_undo);
            _finish.Visible = true;
            Ui.PopIn(_finish);
            Audio.I.Retry();
        }
        else
        {
            Finish();
        }
    }

    private void Undo()
    {
        if (_board is null || _state is not (State.Play or State.Stuck) || !_model.Undo())
            return;
        // Rebuild the tiles from the board as it was
        foreach (var tile in _tiles.Values)
            tile.QueueFree();
        _tiles.Clear();
        for (var y = 0; y < _model.Size; y++)
            for (var x = 0; x < _model.Size; x++)
                if (_model[x, y] is > 0 and var value)
                {
                    var tile = MergeTile.Create(value, _board.CellSize);
                    tile.Position = _board.CellCenter(x, y);
                    _board.AddChild(tile);
                    _tiles[(x, y)] = tile;
                    tile.PopIn((x + y) * 0.02f);
                }
        Audio.I.Whoosh();
        _finish.Visible = false;
        _state = State.Play;
        _cooldown = 0.2f;
        UpdateHud();
        Ui.Punch(_undo, 0.2f);
    }

    /// <summary>End of the round: the tiles bounce in a wave, "끝!", then the result screen.</summary>
    private async void Finish()
    {
        if (_state is State.Ending or State.Result || _board is null)
            return;
        _state = State.Ending;
        var token = _roundToken;
        _finish.Visible = false;
        UpdateHud();
        foreach (var ((x, y), tile) in _tiles)
        {
            var wave = tile.CreateTween();
            wave.TweenInterval((x + y) * 0.05f);
            wave.TweenCallback(Callable.From(tile.Punch));
        }
        Audio.I.TimeUp();
        Audio.I.DuckMusic(-12f);
        ShowBanner("끝!", 110, UiTheme.Yellow);
        _buddy.Say("잘했어!");

        await Wait(1.6f);
        if (!Alive(token))
            return;
        HideBanner();
        ShowResult();
    }

    private void ShowResult()
    {
        _state = State.Result;
        _play.Visible = false;
        var biggest = _model.BiggestTile;
        var score = _model.Score;
        ClearBoard();
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = MergeRules.VariantId(_level),
            Title = "끝!",
            Caption = "점수",
            Value = score,
            FormatValue = v => $"{v:0}점",
            Detail = $"가장 큰 타일 {biggest}",
            Entry = new ScoreEntry("", score, 0, App.Now()),
            Recordable = score > 0,
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("꾸미기", UiTheme.Blue, ShowSetup),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    private void ClearBoard()
    {
        _board?.QueueFree();
        _board = null;
        _tiles.Clear();
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
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
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
    /// Pushes toward the direction that leaves the most empty cells (ties go to a corner-friendly order). When stuck it
    /// uses one undo the first time, then presses "끝내기", so both paths run.
    /// </summary>
    private void AutoPlay(float dt)
    {
        _autoWait -= dt;
        if (_autoWait > 0)
            return;
        if (_state == State.Stuck && _finish.Visible)
        {
            _autoWait = 1f;
            if (_model.UndosLeft == MergeRules.UndoCount)
                Undo();
            else
                Finish();
            return;
        }
        if (_state != State.Play || _pending is not null || _cooldown > 0)
            return;
        _autoWait = 0.18f;
        MergeDir? best = null;
        var bestEmpty = -1;
        foreach (var dir in new[] { MergeDir.Down, MergeDir.Left, MergeDir.Right, MergeDir.Up })
        {
            var empty = _model.EmptyAfter(dir);
            if (empty > bestEmpty)
            {
                bestEmpty = empty;
                best = dir;
            }
        }
        // Push 3 is one that may move nothing (the "통" path); after 20 pushes play at random so the board fills up and
        // the stuck / undo / finish path runs within a smoke test
        if (_pushes >= 20)
            best = (MergeDir)_rng.Next(4);
        _pending = _pushes == 3 || best is null ? MergeDir.Up : best;
    }

    // ---- Helpers ----

    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _roundToken;

    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
