using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.BalloonPop;

/// <summary>
/// 풍선 터뜨리기 (docs/games/01-balloon-pop.md).
/// Flow: Setup (꾸미기) → Countdown → Playing (30s) → Ending (balloons drift away) → Result (shared ResultPanel).
/// All nodes are built in code; the scene file only holds this root.
/// </summary>
public partial class BalloonGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Ending, Result }

    private const string GameId = "balloon";
    private const string SettingsSection = "balloon";
    private const int RandomColor = 4; // Index of "랜덤" in the color choices
    private const int MilestoneEvery = 10;
    private const float StreakWindow = 0.7f;
    private const float BalloonHeightRatio = 0.25f; // Balloon sprite height as a fraction of the screen height
    private const float SetupAreaStart = 0.70f;     // Preview balloons rise only right of this fraction of the width

    private static readonly IReadOnlyList<Choice> ColorChoices = new[]
    {
        new Choice("빨강", Balloon.Textures[0]),
        new Choice("노랑", Balloon.Textures[1]),
        new Choice("파랑", Balloon.Textures[2]),
        new Choice("초록", Balloon.Textures[3]),
        new Choice("랜덤", "res://assets/sprites/ui/star.png"),
    };

    private static readonly IReadOnlyList<Choice> SpeedChoices = new[]
    {
        new Choice("느림", "res://assets/sprites/animal/turtle.png"),
        new Choice("보통", "res://assets/sprites/animal/penguin.png"),
        new Choice("빠름", "res://assets/sprites/animal/rabbit.png"),
    };

    private static readonly string[] PhaseBanners = { "", "풍선이 많아져요!", "더 빨라졌어요!", "마지막이에요, 힘내요!" };
    private static readonly string[] Cheers = { "최고야!", "대단해!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();
    private readonly List<Balloon> _balloons = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _balloonLayer = null!;
    private Node2D _fxLayer = null!;
    private Buddy _buddy = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private Control _setup = null!;
    private Control _hud = null!;
    private Label _scoreLabel = null!;
    private Label _timeLabel = null!;
    private TimerBar _timerBar = null!;
    private TextureRect _scoreIcon = null!;
    private Control? _quitConfirm;
    private Label _banner = null!;
    private Texture2D _popTexture = null!;
    private Texture2D _starTexture = null!;

    private int _colorChoice;
    private BalloonStartSpeed _speed;
    private float _elapsed;
    private float _spawnTimer;
    private int _score;
    private int _streak;
    private float _lastPopTime = -10f;
    private int _phaseIndex;
    private int _lastWholeSecond;
    private float _lastSpawnX = -1000f;
    private float _autoplayTimer;

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        _popTexture = Ui.Tex("res://assets/sprites/fx/pop.png");
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);

        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _shake = ScreenShake.Create(_world);
        AddChild(_shake);

        _buddy = new Buddy();
        _world.AddChild(_buddy);
        _balloonLayer = new Node2D();
        _world.AddChild(_balloonLayer);
        _fxLayer = new Node2D();
        _world.AddChild(_fxLayer);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _colorChoice = (int)App.I.GetSetting(SettingsSection, "color", RandomColor);
        _speed = (BalloonStartSpeed)(int)App.I.GetSetting(SettingsSection, "speed", (int)BalloonStartSpeed.Normal);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

        BuildHud();
        PlaceBuddy();
        GetViewport().SizeChanged += PlaceBuddy;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= PlaceBuddy;
        GetTree().Paused = false; // In case the scene changes while the quit question is open
    }

    private void PlaceBuddy() => _buddy.Position = new Vector2(ScreenSize.X - 110, ScreenSize.Y - 24);

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _hud.Visible = false;
        ClearBalloons();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 24);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var title = Ui.Label("풍선 터뜨리기", 46, UiTheme.Pink, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);

        var colorRow = ChoiceRow.Create("풍선 색", ColorChoices, _colorChoice);
        colorRow.Selected += i =>
        {
            _colorChoice = i;
            App.I.SetSetting(SettingsSection, "color", i);
            SpawnPreviewBalloon();
        };
        body.AddChild(colorRow);

        var sceneryRow = ChoiceRow.Create("배경", Scenery.Choices, (int)_scenery.Kind);
        sceneryRow.Selected += i =>
        {
            _scenery.Kind = (SceneryKind)i;
            App.I.SetSetting(SettingsSection, "scenery", i);
        };
        body.AddChild(sceneryRow);

        var buddyRow = ChoiceRow.Create("응원 친구", Buddy.Choices, (int)App.I.GetSetting(SettingsSection, "buddy", 0), 110);
        buddyRow.Selected += i =>
        {
            _buddy.SetCharacter(i);
            _buddy.Say("같이 하자!", 0.9f);
            App.I.SetSetting(SettingsSection, "buddy", i);
        };
        body.AddChild(buddyRow);

        var speedRow = ChoiceRow.Create("속도", SpeedChoices, (int)_speed);
        speedRow.Selected += i =>
        {
            _speed = (BalloonStartSpeed)i;
            App.I.SetSetting(SettingsSection, "speed", i);
        };
        body.AddChild(speedRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 84)));
        buttons.AddChild(Ui.Button("시작!", StartRound, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 84)));
        body.AddChild(buttons);

        // Left side of the screen; the right side stays open for the buddy and preview balloons.
        var holder = new MarginContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        holder.AddThemeConstantOverride("margin_left", 28);
        var center = new CenterContainer();
        center.AddChild(panel);
        holder.AddChild(center);
        _setup = holder;
        _ui.AddChild(_setup);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();

        if (App.I.Autoplay)
            GetTree().CreateTimer(0.5f).Timeout += () =>
            {
                if (_state == State.Setup)
                    StartRound();
            };
    }

    private void SpawnPreviewBalloon()
    {
        var size = ScreenSize;
        var x = Mathf.Lerp(size.X * SetupAreaStart + 60, size.X - 60, (float)_rng.NextDouble());
        SpawnBalloon(x, size.Y + 100, BalloonRules.RiseSpeed(0, BalloonStartSpeed.Normal) * size.Y * 0.8f);
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null!;
        CloseResult();
        ClearBalloons();

        _score = 0;
        _elapsed = 0;
        _spawnTimer = 0.2f;
        _streak = 0;
        _phaseIndex = 0;
        _lastWholeSecond = BalloonRules.SecondsLeft(0);
        UpdateHud();
        _timeLabel.AddThemeColorOverride("font_color", UiTheme.Ink);
        _timerBar.Reset();
        _scoreIcon.Texture = Ui.Tex(Balloon.Textures[_colorChoice == RandomColor ? _rng.Next(Balloon.Textures.Length) : _colorChoice]);
        _hud.Visible = true;
        _state = State.Countdown;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic(); // "다시 하기": same track, so undo the result screen's duck and the last phase's tempo

        var countdown = Countdown.Create();
        countdown.Finished += () => _state = State.Playing;
        _ui.AddChild(countdown);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        for (var i = _balloons.Count - 1; i >= 0; i--)
        {
            var b = _balloons[i];
            b.Step(dt);
            if (b.IsGone)
            {
                _balloons.RemoveAt(i);
                b.QueueFree();
            }
        }

        switch (_state)
        {
            case State.Setup:
                // A few slow balloons in the open area so the color choice is visible (and poppable for fun)
                _spawnTimer -= dt;
                if (_spawnTimer <= 0)
                {
                    _spawnTimer = 1.6f;
                    SpawnPreviewBalloon();
                }
                break;
            case State.Playing:
                UpdatePlaying(dt);
                break;
        }
    }

    private void UpdatePlaying(float dt)
    {
        _elapsed += dt;
        _timerBar.Remaining = BalloonRules.PlaySeconds - _elapsed;

        var phase = BalloonRules.PhaseAt(_elapsed);
        if (phase.Index != _phaseIndex)
        {
            _phaseIndex = phase.Index;
            ShowBanner(PhaseBanners[phase.Index]);
            Audio.I.SetMusicTempo(1f + 0.05f * phase.Index);
        }

        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            _spawnTimer += BalloonRules.SpawnInterval(_elapsed, _speed);
            SpawnRoundBalloon();
        }

        var secondsLeft = BalloonRules.SecondsLeft(_elapsed);
        if (secondsLeft != _lastWholeSecond)
        {
            _lastWholeSecond = secondsLeft;
            UpdateHud();
            if (secondsLeft is > 0 and <= 5)
            {
                // Last seconds: a tick and a pulse, in orange (excitement, not alarm)
                Audio.I.Tick(1.25f);
                _timeLabel.AddThemeColorOverride("font_color", UiTheme.Orange);
                Ui.Punch(_timeLabel, 0.3f);
            }
        }

        if (App.I.Autoplay)
            AutoplayStep(dt);

        if (_elapsed >= BalloonRules.PlaySeconds)
            EndRound();
    }

    private void SpawnRoundBalloon()
    {
        var size = ScreenSize;
        const float margin = 90f;
        // Avoid stacking right on top of the previous balloon
        float x;
        var tries = 0;
        do
        {
            x = Mathf.Lerp(margin, size.X - margin, (float)_rng.NextDouble());
        } while (Mathf.Abs(x - _lastSpawnX) < 140 && ++tries < 6);
        _lastSpawnX = x;
        var speed = BalloonRules.RiseSpeed(_elapsed, _speed) * size.Y * (0.9f + 0.25f * (float)_rng.NextDouble());
        SpawnBalloon(x, size.Y + 110, speed);
    }

    private void SpawnBalloon(float x, float y, float speed)
    {
        var color = _colorChoice == RandomColor ? _rng.Next(Balloon.Textures.Length) : _colorChoice;
        var height = ScreenSize.Y * BalloonHeightRatio * (0.9f + 0.2f * (float)_rng.NextDouble());
        var balloon = Balloon.Create(color, new Vector2(x, y), height, speed, (float)_rng.NextDouble() * Mathf.Tau);
        _balloons.Add(balloon);
        _balloonLayer.AddChild(balloon);
    }

    private void EndRound()
    {
        _state = State.Ending;
        _elapsed = BalloonRules.PlaySeconds;
        _timerBar.Remaining = 0;
        UpdateHud();
        Audio.I.TimeUp();
        Audio.I.DuckMusic(-12f);
        Audio.I.Whoosh();
        foreach (var b in _balloons)
            b.Release();
        _balloons.Clear();

        var done = Ui.Label("끝!", 160, UiTheme.Yellow, 28, UiTheme.Ink);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        done.VerticalAlignment = VerticalAlignment.Center;
        done.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(done);
        done.PivotOffset = ScreenSize / 2;
        done.Scale = new Vector2(0.3f, 0.3f);
        var tween = done.CreateTween();
        tween.TweenProperty(done, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(0.9f);
        tween.TweenProperty(done, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Callable.From(() =>
        {
            done.QueueFree();
            ShowResult();
        }));
    }

    private void ShowResult()
    {
        _state = State.Result;
        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            Caption = "터뜨린 풍선",
            Value = _score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Entry = new ScoreEntry("", _score, 0, App.Now()),
            Recordable = _score > 0,
            NotRecordableMessage = "다음엔 풍선을 꼭 터뜨려 보자!",
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("꾸미기", UiTheme.Blue, () =>
                {
                    CloseResult();
                    ShowSetup();
                }),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    private void CloseResult()
    {
        foreach (var child in _ui.GetChildren())
            if (child is ResultPanel)
                child.QueueFree();
    }

    private void ClearBalloons()
    {
        foreach (var b in _balloons)
            b.QueueFree();
        _balloons.Clear();
    }

    // ---- Input & popping ----

    public override void _UnhandledInput(InputEvent e)
    {
        // Mouse clicks arrive as touches too (emulate_touch_from_mouse), so touch alone covers PC and phones.
        if (e is not InputEventScreenTouch { Pressed: true } touch)
            return;
        if (_state is not (State.Playing or State.Setup))
            return;
        var point = _balloonLayer.ToLocal(touch.Position);
        // Topmost (most recently added) balloon first
        for (var i = _balloons.Count - 1; i >= 0; i--)
        {
            if (_balloons[i].Alive && _balloons[i].Hits(point))
            {
                PopBalloon(i);
                return;
            }
        }
        _fxLayer.AddChild(Ripple.Create(_fxLayer.ToLocal(touch.Position)));
    }

    private void PopBalloon(int index)
    {
        var balloon = _balloons[index];
        _balloons.RemoveAt(index);
        var pos = balloon.Position;
        var tint = Balloon.Tints[balloon.ColorId];
        balloon.QueueFree();

        var now = Time.GetTicksMsec() / 1000f;
        _streak = now - _lastPopTime <= StreakWindow ? _streak + 1 : 1;
        _lastPopTime = now;

        SpawnPopEffect(pos, tint, balloon.BodyRadius.X);
        Audio.I.Pop(_streak);
        App.Vibrate(12);

        if (_state != State.Playing)
            return; // Preview balloons on the setup screen: just for fun, no score

        _score++;
        UpdateHud();
        if (_colorChoice == RandomColor)
            _scoreIcon.Texture = Ui.Tex(Balloon.Textures[balloon.ColorId]); // Random colors: show the last popped one
        Ui.Punch(_scoreLabel, 0.35f);
        _buddy.Hop();
        _fxLayer.AddChild(new FloatingText
        {
            Text = "+1",
            Color = tint.Lightened(0.35f),
            FontSize = 40,
            Position = pos + new Vector2(0, -balloon.BodyRadius.Y - 10),
        });

        if (_score % MilestoneEvery == 0)
            Milestone(pos);
    }

    private void SpawnPopEffect(Vector2 pos, Color tint, float radius)
    {
        var flash = new Sprite2D
        {
            Texture = _popTexture,
            Position = pos,
            Rotation = (float)_rng.NextDouble() * Mathf.Tau,
            Modulate = Colors.White.Lerp(tint, 0.3f),
        };
        var baseScale = radius * 2.4f * 1.3f / _popTexture.GetSize().X; // A bit larger than the balloon itself
        flash.Scale = new Vector2(baseScale * 0.5f, baseScale * 0.5f);
        _fxLayer.AddChild(flash);
        var tween = flash.CreateTween().SetParallel();
        tween.TweenProperty(flash, "scale", new Vector2(baseScale, baseScale), 0.16f).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "modulate:a", 0f, 0.22f).SetDelay(0.06f);
        tween.Chain().TweenCallback(Callable.From(flash.QueueFree));

        _fxLayer.AddChild(Burst.Create(pos, tint, 14, 520f, 9f, radius * 1.3f, 0.45f, _rng));
        // Torn rubber in the balloon color (no screen shake: popping happens all the time)
        _fxLayer.AddChild(Shards.Create(pos, tint, 6, 560f, radius * 0.7f, 0.75f, _rng));
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, pos, 3, 380f, 30f, 0.7f, _rng));
    }

    /// <summary>Every 10 balloons: a bigger moment (sparkle sound, star shower, buddy cheers).</summary>
    private void Milestone(Vector2 pos)
    {
        Audio.I.Cheer();
        _shake.Add(5f);
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, pos, 10, 650f, 44f, 1.0f, _rng, 0.8f));
        _buddy.Say(Cheers[_rng.Next(Cheers.Length)]);
        Ui.Punch(_scoreLabel, 0.6f);
    }

    private void AutoplayStep(float dt)
    {
        _autoplayTimer -= dt;
        if (_autoplayTimer > 0)
            return;
        _autoplayTimer = 0.28f + (float)_rng.NextDouble() * 0.25f;
        for (var i = 0; i < _balloons.Count; i++)
        {
            var y = _balloons[i].Position.Y;
            if (_balloons[i].Alive && y > 60 && y < ScreenSize.Y * 0.8f)
            {
                PopBalloon(i);
                return;
            }
        }
    }

    // ---- HUD ----

    private void BuildHud()
    {
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_hud);

        // Full-screen margin + column: top bar (score, quit ... time), banner, flexible space
        var frame = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            frame.AddThemeConstantOverride($"margin_{side}", 18);
        _hud.AddChild(frame);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(column);

        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(top);

        var scorePanel = Ui.Panel(Colors.White, 12, 4);
        var scoreRow = new HBoxContainer();
        scoreRow.AddThemeConstantOverride("separation", 10);
        _scoreIcon = Ui.Icon(Balloon.Textures[0], 48);
        scoreRow.AddChild(_scoreIcon);
        _scoreLabel = Ui.Label("점수 0", 40, UiTheme.Ink);
        _scoreLabel.CustomMinimumSize = new Vector2(150, 0);
        scoreRow.AddChild(_scoreLabel);
        scorePanel.AddChild(scoreRow);
        top.AddChild(scorePanel);

        // Up here, away from where balloons are popped, and it asks before quitting
        top.AddChild(Ui.Spacer(0, 14));
        var quit = Ui.Button("그만하기", AskQuit, null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);

        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        var timePanel = Ui.Panel(Colors.White, 12, 4);
        var timeColumn = new VBoxContainer();
        timeColumn.AddThemeConstantOverride("separation", 4);
        _timeLabel = Ui.Label("남은 시간 30초", 40, UiTheme.Ink);
        _timeLabel.CustomMinimumSize = new Vector2(270, 0);
        _timeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        timeColumn.AddChild(_timeLabel);
        _timerBar = TimerBar.Create(BalloonRules.PlaySeconds, new Vector2(270, 26));
        timeColumn.AddChild(_timerBar);
        timePanel.AddChild(timeColumn);
        top.AddChild(timePanel);

        _banner = Ui.Label("", 44, Colors.White, 14, UiTheme.Pink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.Modulate = Colors.Transparent;
        column.AddChild(Ui.Spacer(30));
        column.AddChild(_banner);

    }

    /// <summary>"정말 그만할까요?" The round is paused while the question is open.</summary>
    private void AskQuit()
    {
        if (_quitConfirm is not null)
            return;
        GetTree().Paused = true;

        var root = new Control { ProcessMode = ProcessModeEnum.Always, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(Ui.Backdrop());
        var panel = Ui.Panel(margin: 28);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 20);
        panel.AddChild(body);
        var question = Ui.Label("정말 그만할까요?", 44, UiTheme.Ink);
        question.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(question);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        // "Keep playing" is the big, bright choice
        buttons.AddChild(Ui.Button("계속하기", CloseQuit, UiTheme.Green, 34, Ui.Tex("res://assets/sprites/ui/play.png"),
            new Vector2(260, 84)));
        buttons.AddChild(Ui.Button("그만하기", () =>
        {
            GetTree().Paused = false;
            App.I.GoToMainMenu();
        }, null, 28, null, new Vector2(180, 84)));
        body.AddChild(buttons);
        root.AddChild(Ui.Centered(panel));
        _ui.AddChild(root);
        _quitConfirm = root;
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();
    }

    private void CloseQuit()
    {
        _quitConfirm?.QueueFree();
        _quitConfirm = null;
        GetTree().Paused = false;
    }

    private void UpdateHud()
    {
        _scoreLabel.Text = $"점수 {_score}";
        _timeLabel.Text = $"남은 시간 {BalloonRules.SecondsLeft(_elapsed)}초";
    }

    private void ShowBanner(string text)
    {
        if (text.Length == 0)
            return;
        Audio.I.Whoosh();
        _banner.Text = text;
        _banner.PivotOffset = _banner.Size / 2;
        _banner.Modulate = Colors.White;
        _banner.Scale = new Vector2(0.5f, 0.5f);
        var tween = _banner.CreateTween();
        tween.TweenProperty(_banner, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(1.1f);
        tween.TweenProperty(_banner, "modulate:a", 0f, 0.3f);
    }
}
