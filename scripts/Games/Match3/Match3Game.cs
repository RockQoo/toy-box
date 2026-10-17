using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Match3;

/// <summary>
/// 과일 맞추기 (docs/games/11-match3.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → the fruit tumble in and fill the board → Countdown → 60s of play → "끝!" →
/// shared ResultPanel. Swap two neighbors by dragging one toward the other, or by tapping one then the other (keys: move
/// the yellow border with the arrows, Space/Enter picks it up, an arrow then swaps). Three or more in a line disappear,
/// the rest fall and new fruit come in; cascades are cheered ("또!", "또또!", "최고!"). Lines of 4, L/T shapes and lines
/// of 5 make specials. A swap that makes no line slides back with a wiggle (no penalty); after 4 idle seconds a possible
/// swap wiggles as a hint; a board without moves is shuffled. Input waits while a cascade plays; if time runs out
/// during one, the round ends when it is over.
/// </summary>
public partial class Match3Game : Node2D
{
    private enum State { Setup, Intro, Countdown, Playing, Ending, Result }

    private const string GameId = "match3";
    private const string SettingsSection = "match3";
    private const float SetupChipHeight = 84f;
    private const float SwapTime = 0.15f;
    private const float ClearTime = 0.24f;
    private const float FallSpeed = 9f;      // Cells per second (plus a short base time)
    private const int MaxFlyers = 8;         // Collected fruit flying to the score per step

    private static readonly string[] Cheers = { "최고야!", "과일 부자!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private ScreenShake _shake = null!;
    private Buddy _buddy = null!;
    private CanvasLayer _ui = null!;
    private Match3Hud _hud = null!;
    private Node2D _fx = null!;       // Board effects (in board space, shakes with the world)
    private Node2D _flyers = null!;   // Fruit flying to the score (screen space)
    private Label _banner = null!;
    private Control? _setup;
    private Texture2D _star = null!;

    private Match3Level _level;
    private Match3Board _model = null!;
    private Match3BoardView? _board;
    private GemView?[,] _views = new GemView?[Match3Rules.Size, Match3Rules.Size];
    private int _roundToken;
    private float _elapsed;
    private int _score;
    private int _longestChain;
    private int _nextCheer;
    private bool _busy;               // A swap / cascade / shuffle is playing
    private bool _timeUp;
    private float _idle;
    private float _hintAgain;

    // Input
    private (int X, int Y)? _selected;
    private (int X, int Y)? _pressCell;
    private Vector2 _pressAt;
    private (int X, int Y) _cursor = (3, 3);
    private bool _keyCursor;

    // Autoplay
    private float _autoWait;
    private int _autoSwaps;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _star = Ui.Tex("res://assets/sprites/ui/star.png");
        _scenery = new Scenery();
        AddChild(_scenery);
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _shake = ScreenShake.Create(_world);
        AddChild(_shake);
        _buddy = new Buddy();
        _world.AddChild(_buddy);

        _ui = new CanvasLayer();
        AddChild(_ui);
        _hud = Match3Hud.Create(Match3Rules.PlaySeconds);
        _ui.AddChild(_hud);
        _flyers = new Node2D();
        _ui.AddChild(_flyers);
        _banner = Ui.Label("", 96, UiTheme.Yellow, 24, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _ui.AddChild(_banner);

        _level = (Match3Level)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)Match3Level.Easy), 0, 1);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        Layout();
        GetViewport().SizeChanged += Layout;
        // 60 seconds of play is long for the headless smoke test (it stops after 8000 frames): double speed there only
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 2.0;
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    // ---- Layout: the square board between the quit column (left) and the score/time column (right) ----

    private Rect2 BoardArea()
    {
        var screen = ScreenSize;
        var x0 = Match3Hud.LeftWidth;
        var x1 = screen.X - Match3Hud.RightWidth;
        return new Rect2(x0, 0, Mathf.Max(200f, x1 - x0), screen.Y);
    }

    private float CellFor()
    {
        var area = BoardArea();
        return Mathf.Floor(Mathf.Min(area.Size.X, area.Size.Y - 20) / (Match3Rules.Size + 0.9f));
    }

    private void Layout()
    {
        var screen = ScreenSize;
        _buddy.Position = new Vector2(screen.X - Match3Hud.RightWidth / 2, screen.Y - 16);
        PlaceBoard();
    }

    private void PlaceBoard()
    {
        if (_board is null)
            return;
        _board.SetCellSize(CellFor());
        _board.Position = (BoardArea().GetCenter() - _board.GridSize / 2).Round();
        for (var y = 0; y < Match3Rules.Size; y++)
            for (var x = 0; x < Match3Rules.Size; x++)
                if (_views[x, y] is { } view)
                    view.Position = _board.CellCenter(x, y);
        var rect = BoardRect();
        _banner.Position = rect.Position;
        _banner.Size = rect.Size;
        _banner.PivotOffset = rect.Size / 2;
    }

    private Rect2 BoardRect() => _board is null ? new Rect2() : new Rect2(_board.GlobalPosition, _board.GridSize);

    // ---- Setup (난이도, 배경, 응원 친구) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _hud.Visible = false;
        _banner.Visible = false;
        ResultPanel.CloseAll(_ui);
        ClearBoard();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/match3.png", 64));
        titleRow.AddChild(Ui.Label("과일 맞추기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        var levelChoices = new List<Choice>();
        foreach (Match3Level level in Enum.GetValues(typeof(Match3Level)))
            levelChoices.Add(new Choice($"{Match3Rules.Variants[(int)level].Label} {Match3Rules.Kinds(level)}가지",
                level == Match3Level.Easy ? "res://assets/sprites/match3/apple.png" : "res://assets/sprites/match3/watermelon.png"));
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 190, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (Match3Level)i;
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
        _elapsed = 0;
        _score = 0;
        _longestChain = 0;
        _nextCheer = Match3Rules.CheerEvery;
        _busy = false;
        _timeUp = false;
        _idle = 0;
        _selected = null;
        _pressCell = null;
        _keyCursor = false;
        _autoWait = 0.8f;
        _autoSwaps = 0;
        _hud.Reset();
        _hud.Visible = true;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck

        _model = Match3Board.Create(_level, _rng);
        _board = Match3BoardView.Create(Match3Rules.Size, CellFor());
        _world.AddChild(_board);
        _fx = new Node2D();
        _board.AddChild(_fx); // Above the pieces and not clipped, so bursts spill over the frame
        PlaceBoard();

        // The fruit tumble in, bottom row first, each column a little after the last
        var n = Match3Rules.Size;
        var longest = 0f;
        for (var y = n - 1; y >= 0; y--)
            for (var x = 0; x < n; x++)
            {
                var view = NewView(_model[x, y], x, y);
                var target = view.Position;
                view.Position = target - new Vector2(0, _board.CellSize * (n + 0.5f));
                var delay = (n - 1 - y) * 0.06f + x * 0.025f;
                var tween = view.CreateTween();
                tween.TweenInterval(delay);
                tween.TweenProperty(view, "position", target, 0.45f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
                longest = Mathf.Max(longest, delay + 0.45f);
            }
        Audio.I.Whoosh();
        await Wait(longest);
        if (!Alive(token))
            return;

        _state = State.Countdown;
        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown || !Alive(token))
                return;
            _state = State.Playing;
            _buddy.Say("같은 과일 셋!", 1.2f);
        };
        _ui.AddChild(countdown);
    }

    private GemView NewView(Gem gem, int x, int y)
    {
        var view = GemView.Create(gem, _board!.CellSize);
        view.Position = _board.CellCenter(x, y);
        _board.Pieces.AddChild(view);
        _views[x, y] = view;
        return view;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (_state != State.Playing)
            return;
        if (!_timeUp)
        {
            _elapsed += dt;
            _hud.SetElapsed(_elapsed);
            if (_elapsed >= Match3Rules.PlaySeconds)
            {
                _timeUp = true;
                Deselect();
            }
        }
        if (_timeUp)
        {
            if (!_busy)
                EndRound();
            return;
        }

        if (!_busy)
        {
            _idle += dt;
            if (_idle >= Match3Rules.HintDelay && (_hintAgain -= dt) <= 0)
                ShowHint();
        }
        if (App.I.Autoplay)
            AutoPlay(dt);
    }

    private bool CanAct => _state == State.Playing && !_busy && !_timeUp && _board is not null;

    // ---- Swap and its playback ----

    private async void TrySwap((int X, int Y) from, (int X, int Y) to)
    {
        if (!CanAct || _board is null)
            return;
        var a = new GemCell(from.X, from.Y);
        var b = new GemCell(to.X, to.Y);
        if (!_model.Inside(a) || !_model.Inside(b) || !a.IsNeighbor(b))
            return;
        Deselect();
        _idle = 0;
        _hintAgain = 0;
        _busy = true;
        var token = _roundToken;
        var board = _board;
        var va = _views[a.X, a.Y]!;
        var vb = _views[b.X, b.Y]!;
        var pa = board.CellCenter(a.X, a.Y);
        var pb = board.CellCenter(b.X, b.Y);

        var result = _model.Swap(a, b);
        if (!result.Valid)
        {
            // No line: over and back with a wiggle and a soft "뾱". Nothing else happens.
            va.SwapBack(pb, pa, SwapTime);
            vb.SwapBack(pa, pb, SwapTime);
            Audio.I.Plop();
            await Wait(SwapTime * 2 + 0.22f);
            if (Alive(token))
                _busy = false;
            return;
        }

        Audio.I.Whoosh();
        va.ZIndex = 1;
        va.MoveTo(pb, SwapTime);
        vb.MoveTo(pa, SwapTime);
        _views[a.X, a.Y] = vb;
        _views[b.X, b.Y] = va;
        await Wait(SwapTime + 0.02f);
        if (!Alive(token))
            return;
        va.ZIndex = 0;

        foreach (var step in result.Steps)
        {
            await PlayStep(step, token);
            if (!Alive(token))
                return;
        }
        _longestChain = Math.Max(_longestChain, result.Chains);

        if (result.Shuffle is { } moves)
        {
            await PlayShuffle(moves, token);
            if (!Alive(token))
                return;
        }
        SyncViews();
        _busy = false;
    }

    private async System.Threading.Tasks.Task PlayStep(Match3Step step, int token)
    {
        var board = _board!;
        var cell = board.CellSize;

        // Cascade words and a little shake from the second chain on
        if (Match3Rules.ChainWord(step.Chain) is { Length: > 0 } word)
        {
            ShowBanner(word, step.Chain >= 4 ? 120 : 96 + step.Chain * 6);
            _shake.Add(2f + step.Chain);
            _buddy.Hop(20f + step.Chain * 6);
            if (step.Chain >= 3)
                _buddy.Say(word, 0.8f);
        }

        foreach (var blast in step.Blasts)
            PlayBlast(blast);

        // Collected fruit swell and pop, with fruit-colored bits and a few stars; some fly to the score
        Audio.I.Pop(step.Chain);
        App.Vibrate(12);
        var flyers = 0;
        foreach (var clear in step.Cleared)
        {
            var at = board.CellCenter(clear.At.X, clear.At.Y);
            if (_views[clear.At.X, clear.At.Y] is { } view)
            {
                view.Burst(ClearTime);
                _views[clear.At.X, clear.At.Y] = null;
            }
            _fx.AddChild(Shards.Create(at, GemView.ColorOf(clear.Gem), 4, 260f, cell * 0.14f, 0.55f, _rng));
            if (flyers < MaxFlyers)
            {
                flyers++;
                FlyToScore(clear.Gem, board.GlobalPosition + at, flyers * 0.03f);
            }
        }
        if (step.Cleared.Count > 0)
        {
            var center = step.Cleared.Aggregate(Vector2.Zero, (s, c) => s + board.CellCenter(c.At.X, c.At.Y)) / step.Cleared.Count;
            _fx.AddChild(SpriteBurst.Create(_star, center, Math.Min(3 + step.Cleared.Count / 2, 10), 300f, cell * 0.25f, 0.6f, _rng, 0.9f));
        }

        // New specials appear where the line was made
        foreach (var made in step.Made)
        {
            var view = _views[made.At.X, made.At.Y];
            if (view is null)
                view = NewView(made.Gem, made.At.X, made.At.Y);
            view.SetGem(made.Gem);
            view.Punch();
            _fx.AddChild(Burst.Create(board.CellCenter(made.At.X, made.At.Y), UiTheme.Yellow, 10, 240f, cell * 0.05f, cell * 0.3f, 0.4f, _rng));
            Audio.I.StarPickup(made.Gem.IsRainbow ? 5 : 3);
            if (made.Gem.IsRainbow)
                _buddy.Say("무지개 사탕!", 1.1f);
        }

        AddScore(step.Collected);
        await Wait(ClearTime + 0.02f);
        if (!Alive(token))
            return;

        // Falls: pieces above drop into the gaps, new fruit slide in from the top edge
        var moving = new List<(GemView View, GemFall Fall)>();
        foreach (var fall in step.Falls)
        {
            if (fall.IsNew)
            {
                var view = GemView.Create(fall.Gem, cell);
                view.Position = board.CellCenter(fall.X, fall.FromY);
                board.Pieces.AddChild(view);
                moving.Add((view, fall));
            }
            else if (_views[fall.X, fall.FromY] is { } view)
            {
                moving.Add((view, fall));
            }
        }
        foreach (var (_, fall) in moving)
            if (!fall.IsNew)
                _views[fall.X, fall.FromY] = null;
        var longest = 0f;
        foreach (var (view, fall) in moving)
        {
            _views[fall.X, fall.ToY] = view;
            var time = 0.12f + (fall.ToY - fall.FromY) / FallSpeed;
            view.MoveTo(board.CellCenter(fall.X, fall.ToY), time, bounce: true);
            longest = Mathf.Max(longest, time);
        }
        if (moving.Count > 0)
        {
            await Wait(longest * 0.85f);
            if (Alive(token))
                Audio.I.Step(step.Chain % 2 == 0);
        }
    }

    /// <summary>A special going off: a beam along its row / column, a ring, or a sparkle over the whole board.</summary>
    private void PlayBlast(GemBlast blast)
    {
        var board = _board!;
        var cell = board.CellSize;
        var at = board.CellCenter(blast.At.X, blast.At.Y);
        switch (blast.Special)
        {
            case GemSpecial.LineH:
            case GemSpecial.LineV:
            {
                var horizontal = blast.Special == GemSpecial.LineH;
                var tex = Ui.Tex($"res://assets/sprites/match3/{(horizontal ? "fx_line_h" : "fx_line_v")}.png");
                var beam = new Sprite2D { Texture = tex, Position = horizontal ? new Vector2(board.GridSize.X / 2, at.Y) : new Vector2(at.X, board.GridSize.Y / 2) };
                var along = board.GridSize.X * 1.15f / (horizontal ? tex.GetWidth() : tex.GetHeight());
                var across = cell * 1.3f / (horizontal ? tex.GetHeight() : tex.GetWidth());
                var full = horizontal ? new Vector2(along, across) : new Vector2(across, along);
                beam.Scale = horizontal ? new Vector2(0.1f, across) : new Vector2(across, 0.1f);
                _fx.AddChild(beam);
                var tween = beam.CreateTween();
                tween.TweenProperty(beam, "scale", full, 0.16f).SetEase(Tween.EaseType.Out);
                tween.TweenProperty(beam, "modulate:a", 0f, 0.3f);
                tween.TweenCallback(Callable.From(beam.QueueFree));
                Audio.I.PassTick(4);
                _shake.Add(3f);
                break;
            }
            case GemSpecial.Bomb:
            {
                var tex = Ui.Tex("res://assets/sprites/match3/fx_bomb.png");
                var ring = new Sprite2D { Texture = tex, Position = at, Scale = Vector2.One * (cell * 0.6f / tex.GetWidth()) };
                _fx.AddChild(ring);
                var tween = ring.CreateTween().SetParallel();
                tween.TweenProperty(ring, "scale", Vector2.One * (cell * 3.4f / tex.GetWidth()), 0.35f).SetEase(Tween.EaseType.Out);
                tween.TweenProperty(ring, "rotation", 1.2f, 0.35f);
                tween.TweenProperty(ring, "modulate:a", 0f, 0.25f).SetDelay(0.15f);
                tween.Chain().TweenCallback(Callable.From(ring.QueueFree));
                Audio.I.Bonk();
                _shake.Add(4f);
                break;
            }
            case GemSpecial.Rainbow:
            {
                // The whole board sparkles, with a fanfare
                var flash = new ColorRect { Color = new Color(1f, 1f, 0.85f, 0.6f), Size = board.GridSize, MouseFilter = Control.MouseFilterEnum.Ignore };
                _fx.AddChild(flash);
                var tween = flash.CreateTween();
                tween.TweenProperty(flash, "color:a", 0f, 0.5f);
                tween.TweenCallback(Callable.From(flash.QueueFree));
                _fx.AddChild(SpriteBurst.Create(_star, board.GridSize / 2, 16, 700f, cell * 0.4f, 1.0f, _rng, 1f));
                Audio.I.Fanfare();
                _shake.Add(6f);
                _buddy.Say("반짝반짝!", 1.0f);
                break;
            }
        }
    }

    private void FlyToScore(Gem gem, Vector2 from, float delay)
    {
        var tex = GemView.TextureOf(gem);
        var size = (_board?.CellSize ?? 80f) * 0.6f / Mathf.Max(tex.GetWidth(), tex.GetHeight());
        var sprite = new Sprite2D { Texture = tex, Position = from, Scale = Vector2.One * size };
        _flyers.AddChild(sprite);
        var to = _hud.ScoreTarget;
        var control = (from + to) / 2 + new Vector2(-60, -120);
        var tween = sprite.CreateTween();
        tween.TweenInterval(delay);
        tween.TweenMethod(Callable.From<float>(t =>
        {
            sprite.Position = from.Lerp(control, t).Lerp(control.Lerp(to, t), t);
            sprite.Scale = Vector2.One * size * (1f - 0.5f * t);
        }), 0f, 1f, 0.55f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(sprite.QueueFree));
    }

    private void AddScore(int gained)
    {
        if (gained <= 0)
            return;
        _score += gained;
        _hud.SetScore(_score, 0.25f);
        if (_score < _nextCheer)
            return;
        while (_nextCheer <= _score)
            _nextCheer += Match3Rules.CheerEvery;
        Audio.I.Cheer();
        _buddy.Say(Cheers[_rng.Next(Cheers.Length)], 1.3f);
        _buddy.Hop(40f);
        Ui.Punch(_hud.ScoreLabel, 0.6f);
    }

    /// <summary>No move was left: "섞어요!", and every piece spins to its new cell.</summary>
    private async System.Threading.Tasks.Task PlayShuffle(IReadOnlyList<GemMove> moves, int token)
    {
        var board = _board!;
        await Wait(0.2f);
        if (!Alive(token))
            return;
        ShowBanner("섞어요!", 96);
        Audio.I.Whoosh();
        _buddy.Say("섞어요!", 1.0f);
        var old = (GemView?[,])_views.Clone();
        foreach (var move in moves)
        {
            var view = old[move.From.X, move.From.Y];
            _views[move.To.X, move.To.Y] = view;
            view?.SpinTo(board.CellCenter(move.To.X, move.To.Y), 0.6f);
        }
        await Wait(0.65f);
    }

    /// <summary>Makes every view show the model's piece (and fills any gap), in case a step left one behind.</summary>
    private void SyncViews()
    {
        if (_board is null)
            return;
        for (var y = 0; y < Match3Rules.Size; y++)
            for (var x = 0; x < Match3Rules.Size; x++)
            {
                var gem = _model[x, y];
                if (_views[x, y] is not { } view || !IsInstanceValid(view))
                {
                    NewView(gem, x, y).PopIn();
                    continue;
                }
                if (view.Gem != gem)
                    view.SetGem(gem);
                view.Position = _board.CellCenter(x, y);
                view.Rotation = 0;
                view.Scale = Vector2.One;
            }
    }

    // ---- Hint ----

    private void ShowHint()
    {
        _hintAgain = 1.6f;
        if (_model.FirstMove() is not { } move)
            return;
        _views[move.A.X, move.A.Y]?.Wiggle();
        _views[move.B.X, move.B.Y]?.Wiggle();
    }

    // ---- End ----

    private void EndRound()
    {
        if (_state != State.Playing)
            return;
        _state = State.Ending;
        Deselect();
        _buddy.Hop();
        _buddy.Say("잘했어!", 1.4f);
        _hud.ShowTimeUp(ShowResult);
    }

    private void ShowResult()
    {
        _state = State.Result;
        var score = _score;
        var chain = _longestChain;
        _hud.Visible = false;
        ClearBoard();
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = Match3Rules.VariantId(_level),
            Caption = "모은 과일",
            Value = score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Detail = chain >= 2 ? $"가장 긴 연쇄 {chain}번" : null,
            Entry = new ScoreEntry("", score, 0, App.Now()),
            Recordable = score > 0,
            NotRecordableMessage = "다음엔 같은 과일 셋을 맞춰 보자!",
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
        _views = new GemView?[Match3Rules.Size, Match3Rules.Size];
        _selected = null;
        _pressCell = null;
    }

    // ---- Banner (cascade words, "섞어요!") ----

    private Tween? _bannerTween;

    private void ShowBanner(string text, int size)
    {
        _bannerTween?.Kill();
        _banner.Text = text;
        _banner.AddThemeFontSizeOverride("font_size", size);
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
        _banner.PivotOffset = _banner.Size / 2;
        _banner.Scale = new Vector2(0.3f, 0.3f);
        _banner.Rotation = (float)(_rng.NextDouble() * 0.16 - 0.08);
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bannerTween.TweenInterval(0.45f);
        _bannerTween.TweenProperty(_banner, "modulate:a", 0f, 0.25f);
        _bannerTween.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    // ---- Input: drag toward a neighbor, or tap one then a neighbor; keys move a border and swap ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (_state != State.Playing || _board is null || App.I.Autoplay)
            return;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } touch:
                Press(touch.Position);
                break;
            case InputEventScreenTouch { Pressed: false }:
                _pressCell = null;
                break;
            case InputEventScreenDrag drag when _pressCell is { } from:
                var delta = drag.Position - _pressAt;
                if (delta.Length() < _board.CellSize * Match3Rules.DragCells)
                    break;
                _pressCell = null;
                var to = Mathf.Abs(delta.X) > Mathf.Abs(delta.Y)
                    ? (from.X + Math.Sign(delta.X), from.Y)
                    : (from.X, from.Y + Math.Sign(delta.Y));
                if (to.Item1 >= 0 && to.Item2 >= 0 && to.Item1 < Match3Rules.Size && to.Item2 < Match3Rules.Size)
                    TrySwap(from, to);
                else
                    Deselect();
                break;
            case InputEventKey { Pressed: true } key:
                if (HandleKey(key))
                    GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void Press(Vector2 screen)
    {
        if (!CanAct || _board is null)
            return;
        var local = screen - _board.GlobalPosition;
        if (_board.CellAt(local) is not { } cell)
        {
            Deselect();
            return;
        }
        _keyCursor = false;
        if (_selected is { } sel && sel != cell && new GemCell(sel.X, sel.Y).IsNeighbor(new GemCell(cell.X, cell.Y)))
        {
            // Tap one, then a neighbor
            TrySwap(sel, cell);
            return;
        }
        if (_selected == cell)
        {
            Deselect();
            _pressCell = cell; // A drag from here still swaps
            _pressAt = screen;
            return;
        }
        Select(cell);
        Audio.I.Click();
        _pressCell = cell;
        _pressAt = screen;
    }

    /// <summary>Arrows move the border (or swap once a fruit is picked up with Space / Enter).</summary>
    private bool HandleKey(InputEventKey key)
    {
        (int, int)? dir = key.PhysicalKeycode switch
        {
            Godot.Key.Up or Godot.Key.W => (0, -1),
            Godot.Key.Down or Godot.Key.S => (0, 1),
            Godot.Key.Left or Godot.Key.A => (-1, 0),
            Godot.Key.Right or Godot.Key.D => (1, 0),
            _ => null,
        };
        var pick = key.PhysicalKeycode is Godot.Key.Space or Godot.Key.Enter or Godot.Key.KpEnter;
        if (dir is null && !pick)
            return false;
        if (!CanAct)
            return true;
        if (!_keyCursor)
        {
            // First key: show the border where the keyboard is
            _keyCursor = true;
            _board!.Select(_cursor);
            _selected = null;
            return true;
        }
        if (pick && !key.Echo)
        {
            if (_selected is null)
            {
                _selected = _cursor;
                Audio.I.Click();
                _board!.Select(_cursor);
            }
            else
            {
                _selected = null;
            }
            return true;
        }
        if (dir is not { } d)
            return true;
        var next = (Math.Clamp(_cursor.X + d.Item1, 0, Match3Rules.Size - 1), Math.Clamp(_cursor.Y + d.Item2, 0, Match3Rules.Size - 1));
        if (_selected is { } from)
        {
            if (next != from)
                TrySwap(from, next);
            _cursor = next;
            return true;
        }
        _cursor = next;
        _board!.Select(_cursor);
        return true;
    }

    private void Select((int X, int Y) cell)
    {
        _selected = cell;
        _board?.Select(cell);
    }

    private void Deselect()
    {
        _selected = null;
        _board?.Select(_keyCursor && _state == State.Playing ? _cursor : null);
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// Every 0.6–1.2 s picks the swap that collects the most at once. The 3rd swap is one that makes no line (the
    /// slide-back path), and every 7th waits long enough for the hint to show.
    /// </summary>
    private void AutoPlay(float dt)
    {
        if (!CanAct)
            return;
        _autoWait -= dt;
        if (_autoWait > 0)
            return;
        _autoSwaps++;
        _autoWait = (float)(0.6 + _rng.NextDouble() * 0.6);
        if (_autoSwaps % 7 == 0)
            _autoWait += Match3Rules.HintDelay;
        if (_autoSwaps == 3)
        {
            for (var y = 0; y < Match3Rules.Size; y++)
                for (var x = 0; x + 1 < Match3Rules.Size; x++)
                    if (!_model.CanSwap(new GemCell(x, y), new GemCell(x + 1, y)))
                    {
                        Select((x, y));
                        TrySwap((x, y), (x + 1, y));
                        return;
                    }
        }
        if (_model.BestMove() is { } best)
        {
            Select((best.A.X, best.A.Y));
            TrySwap((best.A.X, best.A.Y), (best.B.X, best.B.Y));
        }
    }

    // ---- Helpers ----

    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _roundToken;

    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
