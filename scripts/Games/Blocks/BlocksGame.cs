using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Blocks;

/// <summary>
/// 블록 쌓기 (docs/games/14-blocks.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → Countdown → 3 minutes of falling pieces → "끝!" → shared ResultPanel.
/// Move and turn the falling piece to fill whole rows; full rows flash and go away. The ghost always shows where it
/// lands, a piece on the stack can still move for 0.8 s, and when the stack reaches the top the buddy says "영차!" and
/// the bottom rows go away (no points), so a round never ends early.
/// Keys: ← → (held: repeat after 0.18 s), ↓ soft drop, Space hard drop, ↑ / X turn, Z turn back, C hold.
/// Touch: the buttons beside the board (several can be held at once), or on the board a tap turns and a drag moves
/// one cell per cell dragged.
/// </summary>
public partial class BlocksGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Ending, Result }

    private const string GameId = "blocks";
    private const string SettingsSection = "blocks";
    private const float Margin = 18f;
    private const float SideMin = 420f;          // Room kept at each side of the board for the HUD
    private const float SetupChipHeight = 84f;
    private const float ClearTime = 0.34f;
    private const float RescueTime = 0.7f;
    private const float TapTime = 0.3f;
    private const float StepSoundGap = 0.08f;

    private static readonly string[] Cheers = { "최고야!", "블록 박사!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private ScreenShake _shake = null!;
    private Buddy _buddy = null!;
    private CanvasLayer _ui = null!;
    private BlocksHud _hud = null!;
    private Label _banner = null!;
    private Control? _setup;
    private Texture2D _star = null!;

    private BlocksLevel _level;
    private BlocksRound _round = null!;
    private BlocksBoardView? _board;
    private int _roundToken;
    private float _elapsed;
    private bool _busy;          // A clear / rescue is playing; the next piece waits
    private bool _timeUp;
    private int _stage;
    private float _sinceStepSound;

    // Input
    private readonly KeyRepeat _repeat = new();
    private int _softKeys;
    private int _softTouch;
    private readonly Dictionary<int, PadButton> _touchButtons = new();
    private BoardTouch? _boardTouch;

    private sealed class BoardTouch
    {
        public int Index;
        public Vector2 Anchor;
        public ulong StartMs;
        public bool Moved;
        public int Axis; // 0 undecided, 1 sideways, 2 down
    }

    // Autoplay
    private BlocksPlan? _autoPlan;
    private float _autoWait;
    private int _autoPieces;
    private bool _autoSoft;
    private bool _autoGravity;

    private Vector2 ScreenSize => GetViewportRect().Size;

    private bool CanAct => _state == State.Playing && !_busy && !_timeUp && _board is not null;

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
        _hud = BlocksHud.Create(BlocksRules.PlaySeconds);
        _ui.AddChild(_hud);
        _banner = Ui.Label("", 64, UiTheme.Yellow, 18, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _ui.AddChild(_banner);

        _level = (BlocksLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)BlocksLevel.Easy), 0, 1);
        _scenery.Kind = (SceneryKind)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky), 0, 3);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        Layout();
        GetViewport().SizeChanged += Layout;
        // A 3-minute round is long for the headless smoke test (it stops after 8000 frames): five times faster there only
        if (App.I.Autoplay && DisplayServer.GetName() == "headless")
            Engine.TimeScale = 5.0;
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        Engine.TimeScale = 1.0;
    }

    public override void _Notification(int what)
    {
        // Fingers or keys let go while paused (quit question) or away would otherwise stay "held"
        if (what is (int)NotificationPaused or (int)NotificationApplicationFocusOut)
            ReleaseAll();
    }

    // ---- Layout: the tall board in the middle, the HUD at its sides ----

    private float CellFor()
    {
        var screen = ScreenSize;
        var cols = BlocksRules.Width(_level);
        var rows = BlocksRules.Height(_level);
        // Half a cell of frame above and below; at least SideMin at each side for the buttons and boxes
        var byHeight = (screen.Y - Margin * 2) / (rows + 1f);
        var byWidth = (screen.X - SideMin * 2) / (cols + 1f);
        return Mathf.Floor(Mathf.Max(16f, Mathf.Min(byHeight, byWidth)));
    }

    private void Layout()
    {
        PlaceBoard();
        if (_board is null)
        {
            // Setup: no board yet, keep the buddy where it will stand
            var cell = CellFor();
            var size = new Vector2(BlocksRules.Width(_level), BlocksRules.Height(_level)) * cell;
            _hud.Place(new Rect2((ScreenSize - size) / 2 - Vector2.One * cell * 0.5f, size + Vector2.One * cell));
        }
        _buddy.Position = _hud.BuddySpot;
    }

    private void PlaceBoard()
    {
        if (_board is null)
            return;
        _board.SetCellSize(CellFor());
        _board.Position = ((ScreenSize - _board.GridSize) / 2).Round();
        var frame = _board.FrameRect;
        _hud.Place(new Rect2(_board.Position + frame.Position, frame.Size));
        _buddy.Position = _hud.BuddySpot;
        var rect = BoardRect();
        _banner.Position = rect.Position + new Vector2(-40, rect.Size.Y * 0.22f);
        _banner.Size = new Vector2(rect.Size.X + 80, 120);
        _banner.PivotOffset = _banner.Size / 2;
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
        ReleaseAll();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(Ui.Icon("res://assets/sprites/menu/blocks.png", 64));
        titleRow.AddChild(Ui.Label("블록 쌓기", 42, UiTheme.Orange, 12, UiTheme.Ink));
        body.AddChild(titleRow);

        var levelChoices = new List<Choice>
        {
            new($"쉬움 {BlocksRules.Width(BlocksLevel.Easy)}×{BlocksRules.Height(BlocksLevel.Easy)}", "res://assets/sprites/blocks/yellow.png"),
            new($"보통 {BlocksRules.Width(BlocksLevel.Normal)}×{BlocksRules.Height(BlocksLevel.Normal)}", "res://assets/sprites/blocks/cyan.png"),
        };
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 200, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (BlocksLevel)i;
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

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        ResultPanel.CloseAll(_ui);
        ClearBoard();
        ReleaseAll();

        _roundToken++;
        var token = _roundToken;
        _round = new BlocksRound(_level, _rng);
        _elapsed = 0;
        _busy = false;
        _timeUp = false;
        _stage = 0;
        _autoPlan = null;
        _autoWait = 0.6f;
        _autoPieces = 0;
        _autoSoft = false;

        _board = BlocksBoardView.Create(_round.Board.Width, _round.Board.VisibleRows, CellFor());
        _world.AddChild(_board);
        _world.MoveChild(_board, 0); // Behind the buddy, so its speech bubble stays readable
        _board.SetGrid(_round.Board.Snapshot());
        PlaceBoard();
        _board.Scale = new Vector2(1f, 0.6f);
        _board.Modulate = Colors.White with { A = 0f };
        var tween = _board.CreateTween().SetParallel();
        tween.TweenProperty(_board, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_board, "modulate:a", 1f, 0.25f);

        _hud.Reset();
        _hud.Visible = true;
        _hud.SetButtonsVisible(true);
        _hud.SetNext(_round.Preview());
        _hud.SetHold(null, false);
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the last stage's tempo
        Audio.I.Whoosh();

        _state = State.Countdown;
        var countdown = Countdown.Create();
        countdown.Finished += () =>
        {
            if (_state != State.Countdown || token != _roundToken)
                return;
            _state = State.Playing;
            ShowNewPiece();
            _buddy.Say("줄을 꽉 채워요!", 1.4f);
        };
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        if (_state != State.Playing || _board is null)
            return;
        var dt = (float)delta;
        _sinceStepSound += dt;
        if (!_timeUp)
        {
            _elapsed += dt;
            _hud.SetElapsed(_elapsed);
            if (_elapsed >= BlocksRules.PlaySeconds)
                _timeUp = true;
        }
        if (_timeUp)
        {
            if (!_busy)
                EndRound();
            return;
        }
        if (_busy)
            return;

        if (App.I.Autoplay)
            AutoPlay(dt);
        if (!CanAct)
            return;

        // Held ← / →
        var steps = _repeat.Update(dt);
        for (var i = 0; i < steps; i++)
            if (!Move(_repeat.Direction))
                break;

        var soft = _softKeys > 0 || _softTouch > 0 || _autoSoft;
        var locked = _round.Update(dt, soft);
        if (locked is not null)
            OnLocked(locked, false);
        else
            _board.ShowPiece(_round.Piece, _round.GhostY, slide: true);
    }

    // ---- Actions ----

    private bool Move(int dir)
    {
        if (!CanAct || dir == 0)
            return false;
        if (!_round.Move(dir))
            return false;
        _board!.ShowPiece(_round.Piece, _round.GhostY, slide: true);
        _board.PunchPiece(1.06f);
        StepSound(false);
        return true;
    }

    private void Rotate(int dir)
    {
        if (!CanAct)
            return;
        if (_round.Rotate(dir))
        {
            _board!.ShowPiece(_round.Piece, _round.GhostY, slide: false);
            _board.PunchPiece(1.12f);
            StepSound(true);
        }
        else
        {
            _board!.WigglePiece();
            Audio.I.Plop();
        }
    }

    private void SoftStep()
    {
        if (!CanAct || !_round.SoftDrop())
            return;
        _board!.ShowPiece(_round.Piece, _round.GhostY, slide: true);
    }

    private void HardDrop()
    {
        if (!CanAct)
            return;
        var start = _round.Piece;
        var locked = _round.HardDrop();
        var end = start with { Y = start.Y + locked.DropRows };
        if (locked.DropRows > 0)
            _board!.DropTrail(start, end);
        Audio.I.Whoosh();
        _shake.Add(2f);
        OnLocked(locked, true);
    }

    private void Hold()
    {
        if (!CanAct)
            return;
        if (!_round.Hold())
        {
            _hud.WiggleHold();
            Audio.I.Plop();
            return;
        }
        Audio.I.CardFlip();
        _hud.SetHold(_round.Held, true);
        _hud.SetNext(_round.Preview());
        ShowNewPiece();
        _autoPlan = null;
    }

    /// <summary>A soft "톡" for moves and turns, not more often than every 0.08 s (held keys repeat faster).</summary>
    private void StepSound(bool turn)
    {
        if (_sinceStepSound < StepSoundGap)
            return;
        _sinceStepSound = 0;
        Audio.I.Step(turn);
    }

    private void ShowNewPiece()
    {
        if (_board is null)
            return;
        _board.ShowPiece(_round.Piece, _round.GhostY, slide: false);
        _board.PunchPiece(0.6f);
    }

    // ---- Lock, lines, rescue ----

    private void OnLocked(BlocksLock locked, bool hard)
    {
        var board = _board!;
        _autoPlan = null;
        _autoSoft = false;
        _autoWait = 0.25f;
        App.Vibrate(hard ? 18 : 10);
        Audio.I.WallBump(); // "통"
        board.SetGrid(locked.Before);
        board.Squash(locked.Placed);
        if (hard)
        {
            // A puff under the piece
            var bottom = locked.Placed.Max(b => b.Y);
            foreach (var b in locked.Placed.Where(b => b.Y == bottom))
                board.Fx.AddChild(Burst.Create(board.CellCenter(b.X, b.Y) + new Vector2(0, board.CellSize * 0.5f),
                    Colors.White, 3, 120f, 3f, 0f, 0.25f, _rng));
        }
        _hud.SetNext(_round.Preview());
        _hud.SetHold(_round.Held, false);

        if (locked.Lines == 0 && !locked.Rescued)
        {
            ShowNewPiece();
            return;
        }
        PlayClear(locked);
    }

    private async void PlayClear(BlocksLock locked)
    {
        var token = _roundToken;
        var board = _board!;
        _busy = true;
        board.HidePiece();

        if (locked.Lines > 0)
        {
            PlayLines(locked);
            await Wait(ClearTime);
            if (!Alive(token))
                return;
            board.SetGrid(locked.AfterClear ?? _round.Board.Snapshot());
            _shake.Add(locked.Lines >= 4 ? 5f : 1f + locked.Lines);
            if (_round.Stage != _stage)
            {
                _stage = _round.Stage;
                Audio.I.SetMusicTempo(1f + 0.04f * _stage);
                _buddy.Say(Cheers[_rng.Next(Cheers.Length)], 1.2f);
                await Wait(0.25f);
                if (!Alive(token))
                    return;
                ShowBanner("빨라져요!", 60);
                Audio.I.Whoosh();
            }
        }

        if (locked.Rescued)
        {
            // "영차!": the bottom rows sparkle and go away, the stack comes down. No points, the round goes on.
            await Wait(0.12f);
            if (!Alive(token))
                return;
            _buddy.Say("영차! 자리를 만들자!", 1.8f);
            _buddy.Hop(40f);
            var height = _round.Board.Height;
            board.FlashRows(Enumerable.Range(height - BlocksRules.RescueRows, BlocksRules.RescueRows), RescueTime,
                new Color(1f, 0.95f, 0.6f));
            Audio.I.Whoosh();
            await Wait(RescueTime);
            if (!Alive(token))
                return;
            var i = 0;
            foreach (var b in locked.RescuedBlocks)
                if (i++ % 2 == 0)
                    board.Fx.AddChild(SpriteBurst.Create(_star, board.CellCenter(b.X, b.Y), 2, 320f, board.CellSize * 0.85f, 0.7f, _rng, 0.8f));
            Audio.I.StarPickup(3);
            board.SetGrid(_round.Board.Snapshot());
            board.Squash(Enumerable.Range(0, _round.Board.Width)
                .Select(x => new PlacedBlock(x, height - 1, PieceKind.O)));
            await Wait(0.2f);
            if (!Alive(token))
                return;
        }

        _busy = false;
        if (!_timeUp)
            ShowNewPiece();
    }

    /// <summary>Full rows: a light sweeps along each, blocks burst into bits and stars, "한 줄!" … "네 줄! 최고!".</summary>
    private void PlayLines(BlocksLock locked)
    {
        var board = _board!;
        var lines = locked.Lines;
        board.FlashRows(locked.ClearedRows, ClearTime, Colors.White);
        _hud.SetLines(_round.Lines, 0.3f + 0.1f * lines);
        Audio.I.MatchChime(lines); // "땡", higher for more lines
        App.Vibrate(20 + lines * 8);

        var flash = BlocksArt.Flash;
        foreach (var y in locked.ClearedRows)
        {
            var sweep = new Sprite2D { Texture = flash, Position = board.RowCenter(y) };
            var across = board.CellSize * 1.6f / flash.GetHeight();
            var along = board.GridSize.X * 1.25f / flash.GetWidth();
            sweep.Scale = new Vector2(along * 0.1f, across);
            board.Fx.AddChild(sweep);
            var tween = sweep.CreateTween();
            tween.TweenProperty(sweep, "scale", new Vector2(along, across), 0.16f).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(sweep, "modulate:a", 0f, ClearTime);
            tween.TweenCallback(Callable.From(sweep.QueueFree));

            for (var x = 0; x < board.Columns; x++)
            {
                var v = locked.Before[x, y];
                if (v == 0)
                    continue;
                var kind = (PieceKind)(v - 1);
                board.Fx.AddChild(Shards.Create(board.CellCenter(x, y), BlocksArt.ColorOf(kind), 2, 240f,
                    board.CellSize * 0.18f, 0.6f, _rng));
            }
            board.Fx.AddChild(SpriteBurst.Create(_star, board.RowCenter(y), 3 + lines, 380f, board.CellSize * 0.55f, 0.8f, _rng, 0.9f));
        }

        ShowBanner(BlocksRules.ClearWord(lines), lines >= 4 ? 76 : 56 + lines * 4);
        _buddy.Hop(20f + lines * 8);
        if (lines >= 4)
        {
            // Four at once: confetti, a fanfare and the buddy cheering
            Audio.I.Fanfare();
            _ui.AddChild(Confetti.Create(ScreenSize, 70));
            _buddy.Say("최고!", 1.4f);
        }
        else if (lines >= 2)
        {
            _buddy.Say(BlocksRules.ClearWord(lines), 0.9f);
        }
    }

    // ---- End ----

    private void EndRound()
    {
        if (_state != State.Playing)
            return;
        _state = State.Ending;
        ReleaseAll();
        _board?.HidePiece();
        _hud.SetButtonsVisible(false);
        _buddy.Hop();
        _buddy.Say("잘했어!", 1.4f);
        _hud.ShowTimeUp(ShowResult);
    }

    private void ShowResult()
    {
        _state = State.Result;
        var lines = _round.Lines;
        var best = _round.BestAtOnce;
        _hud.Visible = false;
        _banner.Visible = false;
        ClearBoard();
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = GameCatalog.Find(GameId)!,
            VariantId = BlocksRules.VariantId(_level),
            Caption = "지운 줄",
            Value = lines,
            FormatValue = v => $"줄 {Mathf.RoundToInt(v)}개",
            Detail = best > 0 ? $"한 번에 가장 많이 {best}줄" : null,
            Entry = new ScoreEntry("", lines, 0, App.Now()),
            Recordable = lines > 0,
            NotRecordableMessage = "다음엔 한 줄을 꽉 채워 보자!",
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
    }

    // ---- Banner ("한 줄!", "빨라져요!") ----

    private Tween? _bannerTween;

    private void ShowBanner(string text, int size)
    {
        if (text.Length == 0)
            return;
        _bannerTween?.Kill();
        _banner.Text = text;
        _banner.AddThemeFontSizeOverride("font_size", size);
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
        _banner.Scale = new Vector2(0.3f, 0.3f);
        _banner.Rotation = (float)(_rng.NextDouble() * 0.14 - 0.07);
        _bannerTween = _banner.CreateTween();
        _bannerTween.TweenProperty(_banner, "scale", Vector2.One, 0.28f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bannerTween.TweenInterval(0.55f);
        _bannerTween.TweenProperty(_banner, "modulate:a", 0f, 0.25f);
        _bannerTween.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    // ---- Input ----

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Echo: false } key when HandleKey(key):
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
                HandleTouch(touch);
                break;
            case InputEventScreenDrag drag:
                HandleDrag(drag);
                break;
        }
    }

    private bool HandleKey(InputEventKey key)
    {
        var pressed = key.Pressed;
        switch (key.PhysicalKeycode)
        {
            case Key.Left or Key.A:
                Sideways(-1, pressed);
                return true;
            case Key.Right or Key.D:
                Sideways(1, pressed);
                return true;
            case Key.Down or Key.S:
                _softKeys = Math.Max(0, _softKeys + (pressed ? 1 : -1));
                if (pressed)
                {
                    SoftStep();
                    FlashIfPlaying(PadButton.Down);
                }
                return true;
            case Key.Up or Key.W or Key.X:
                if (pressed)
                {
                    Rotate(1);
                    FlashIfPlaying(PadButton.Rotate);
                }
                return true;
            case Key.Z:
                if (pressed)
                {
                    Rotate(-1);
                    FlashIfPlaying(PadButton.Rotate);
                }
                return true;
            case Key.Space:
                if (pressed)
                {
                    FlashIfPlaying(PadButton.Drop);
                    HardDrop();
                }
                return true;
            case Key.C or Key.Shift:
                if (pressed)
                {
                    FlashIfPlaying(PadButton.Hold);
                    Hold();
                }
                return true;
        }
        return false;
    }

    /// <summary>← / → from a key or a button: move now, keep repeating while held.</summary>
    private void Sideways(int dir, bool pressed)
    {
        if (!pressed)
        {
            _repeat.Release(dir);
            return;
        }
        _repeat.Press(dir);
        Move(dir);
        FlashIfPlaying(dir < 0 ? PadButton.Left : PadButton.Right);
    }

    private void FlashIfPlaying(PadButton button)
    {
        if (_state == State.Playing && !_touchButtons.ContainsValue(button))
            _hud.Flash(button);
    }

    private void HandleTouch(InputEventScreenTouch touch)
    {
        if (touch.Pressed)
        {
            if (_state != State.Playing)
                return;
            if (_hud.ButtonAt(touch.Position) is { } button)
            {
                _touchButtons[touch.Index] = button;
                _hud.SetPressed(button, true);
                PressButton(button);
                return;
            }
            if (_board is not null && BoardRect().Grow(_board.CellSize).HasPoint(touch.Position))
                _boardTouch = new BoardTouch { Index = touch.Index, Anchor = touch.Position, StartMs = Time.GetTicksMsec() };
            return;
        }

        if (_touchButtons.Remove(touch.Index, out var released))
        {
            _hud.SetPressed(released, false);
            ReleaseButton(released);
            return;
        }
        if (_boardTouch is { } bt && bt.Index == touch.Index)
        {
            _boardTouch = null;
            // A short tap without moving turns the piece
            if (!bt.Moved && (Time.GetTicksMsec() - bt.StartMs) / 1000f <= TapTime)
            {
                Rotate(1);
                FlashIfPlaying(PadButton.Rotate);
            }
        }
    }

    /// <summary>On the board: dragging moves the piece one cell per cell dragged (sideways, or down).</summary>
    private void HandleDrag(InputEventScreenDrag drag)
    {
        if (_boardTouch is not { } bt || bt.Index != drag.Index || _board is null)
            return;
        var cell = _board.CellSize;
        var delta = drag.Position - bt.Anchor;
        if (bt.Axis == 0)
        {
            if (delta.Length() < cell * 0.6f)
                return;
            bt.Axis = Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y) ? 1 : delta.Y > 0 ? 2 : 0;
            if (bt.Axis == 0)
                return; // Upward: nothing
        }
        bt.Moved = true;
        if (bt.Axis == 1)
        {
            while (Mathf.Abs(delta.X) >= cell)
            {
                var dir = Math.Sign(delta.X);
                Move(dir);
                bt.Anchor.X += dir * cell;
                delta.X -= dir * cell;
            }
        }
        else
        {
            while (delta.Y >= cell)
            {
                SoftStep();
                bt.Anchor.Y += cell;
                delta.Y -= cell;
            }
        }
    }

    private void PressButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.Left:
                _repeat.Press(-1);
                Move(-1);
                break;
            case PadButton.Right:
                _repeat.Press(1);
                Move(1);
                break;
            case PadButton.Down:
                _softTouch++;
                SoftStep();
                break;
            case PadButton.Rotate:
                Rotate(1);
                break;
            case PadButton.Drop:
                HardDrop();
                break;
            case PadButton.Hold:
                Hold();
                break;
        }
    }

    private void ReleaseButton(PadButton button)
    {
        switch (button)
        {
            case PadButton.Left:
                _repeat.Release(-1);
                break;
            case PadButton.Right:
                _repeat.Release(1);
                break;
            case PadButton.Down:
                _softTouch = Math.Max(0, _softTouch - 1);
                break;
        }
    }

    private void ReleaseAll()
    {
        _repeat.Clear();
        _softKeys = 0;
        _softTouch = 0;
        foreach (var button in _touchButtons.Values)
            _hud?.SetPressed(button, false);
        _touchButtons.Clear();
        _boardTouch = null;
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>
    /// Picks the place with few holes and a low stack (<see cref="BlocksBot"/>), then plays it like a child would with the
    /// buttons: hold if planned, turn, slide one cell at a time, hard drop. Every 6th piece is soft-dropped instead and
    /// left to lock by itself (the lock delay path), and once a round it stacks pieces in the middle up to the top
    /// (the rescue path).
    /// </summary>
    private void AutoPlay(float dt)
    {
        if (!CanAct)
            return;
        _autoWait -= dt;
        if (_autoWait > 0 || _autoSoft)
            return;
        // Once per round, from the 12th piece: drop straight down in the middle until the stack reaches the top, so
        // the "영차!" rescue runs too
        if (_autoPieces >= 12 && _round.Rescues == 0)
        {
            _autoPieces++;
            _hud.Flash(PadButton.Drop);
            HardDrop();
            _autoWait = 0.35f;
            return;
        }
        if (_autoPlan is null)
        {
            _autoPlan = BlocksBot.Best(_round);
            _autoPieces++;
            _autoGravity = _autoPieces % 6 == 0;
        }
        var plan = _autoPlan.Value;
        var piece = _round.Piece;
        if (plan.UseHold && !_round.HoldUsed)
        {
            _hud.Flash(PadButton.Hold);
            Hold();
            _autoWait = 0.25f;
            return;
        }
        var turns = BlocksRules.Mod4(plan.Rotation - piece.Rotation);
        if (turns != 0)
        {
            _hud.Flash(PadButton.Rotate);
            Rotate(turns == 3 ? -1 : 1);
            if (_round.Piece.Rotation == piece.Rotation)
                _autoPlan = plan with { Rotation = piece.Rotation }; // No room to turn: take it as it is
            _autoWait = 0.14f;
            return;
        }
        if (piece.X != plan.X)
        {
            var dir = Math.Sign(plan.X - piece.X);
            _hud.Flash(dir < 0 ? PadButton.Left : PadButton.Right);
            if (!Move(dir))
                _autoPlan = plan with { X = piece.X };
            _autoWait = 0.09f;
            return;
        }
        if (_autoGravity)
        {
            _autoSoft = true; // Falls fast, then waits out the lock delay
            return;
        }
        _hud.Flash(PadButton.Drop);
        HardDrop();
    }

    // ---- Helpers ----

    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _roundToken;

    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
