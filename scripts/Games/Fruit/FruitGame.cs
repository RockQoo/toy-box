using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.FruitCatch;

/// <summary>
/// 과일 받기 (docs/games/02-fruit-catch.md).
/// Flow: Setup (꾸미기) → Countdown → Playing (30s) → Ending (fruits fade, basket hops) → Result (shared ResultPanel).
/// The basket follows the finger / mouse horizontally, or moves with ← → / A D. All nodes are built in code.
/// </summary>
public partial class FruitGame : Node2D
{
    private enum State { Setup, Countdown, Playing, Ending, Result }

    private const string GameId = "fruit";
    private const string SettingsSection = "fruit";
    private const int MilestoneEvery = 10;
    private const float StreakWindow = 1.2f;
    private const float FruitSizeRatio = 0.12f;   // Fruit size as a fraction of the screen height
    private const float BasketWidthRatio = 0.36f; // Basket width as a fraction of the screen height
    private const float BasketBottomGap = 12f;
    private const float KeySpeed = 1.1f;          // Screen widths per second with the arrow keys
    private const float BuddyInset = 90f;         // Buddy x from the right edge
    private const float SetupChipHeight = 80f;    // Five rows must fit in 720 px
    private const float PointerIdleTime = 1.5f;   // Setup: the basket catches by itself after this long without input

    private static readonly string[] PhaseBanners = { "", "과일이 빨라져요!", "과일이 우르르!" };
    private static readonly string[] Cheers = { "최고야!", "대단해!", "멋져요!", "잘한다!", "우와!" };

    private readonly Random _rng = new();
    private readonly List<Fruit> _fruits = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Node2D _fruitLayer = null!;
    private Node2D _fxLayer = null!;
    private Basket _basket = null!;
    private Buddy _buddy = null!;
    private ScreenShake _shake = null!;
    private CanvasLayer _ui = null!;
    private Control? _setup;
    private Control? _setupPanel;
    private RoundHud _hud = null!;
    private Texture2D _starTexture = null!;

    private int _kinds;
    private int _basketColor;
    private StartSpeed _speed;
    private float _elapsed;
    private float _spawnTimer;
    private int _groupLeft;
    private float _groupTimer;
    private int _score;
    private int _streak;
    private float _lastCatchTime = -10f;
    private int _phaseIndex;
    private int _lastKind = -1;
    private readonly float[] _recentX = { -1000f, -1000f };
    private float _pointerIdle = 99f;

    private Vector2 ScreenSize => GetViewportRect().Size;
    private float FloorY => ScreenSize.Y - 6f;

    public override void _Ready()
    {
        _starTexture = Ui.Tex("res://assets/sprites/ui/star.png");

        _scenery = new Scenery();
        AddChild(_scenery);

        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _shake = ScreenShake.Create(_world);
        AddChild(_shake);

        // Back to front: buddy, basket, falling fruit, effects. A caught fruit drops behind the basket (Fruit.Catch).
        _buddy = new Buddy();
        _world.AddChild(_buddy);
        _basket = Basket.Create(ScreenSize.Y * BasketWidthRatio);
        _world.AddChild(_basket);
        _fruitLayer = new Node2D();
        _world.AddChild(_fruitLayer);
        _fxLayer = new Node2D();
        _world.AddChild(_fxLayer);

        _ui = new CanvasLayer();
        AddChild(_ui);

        _kinds = FruitRules.Sanitize((int)App.I.GetSetting(SettingsSection, "fruits", FruitRules.DefaultKinds));
        _basketColor = Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "basket", 0), 0, Basket.ColorCount - 1);
        _speed = (StartSpeed)(int)App.I.GetSetting(SettingsSection, "speed", (int)StartSpeed.Normal);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));
        _basket.SetColor(_basketColor);

        _hud = RoundHud.Create(FruitRules.PlaySeconds, Fruit.Textures[0]);
        _ui.AddChild(_hud);
        Layout();
        _basket.Position = new Vector2(ScreenSize.X * 0.85f, _basket.Position.Y);
        _basket.TargetX = _basket.Position.X;
        GetViewport().SizeChanged += Layout;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private void Layout()
    {
        _buddy.Position = new Vector2(ScreenSize.X - BuddyInset, ScreenSize.Y - 24);
        _basket.Position = new Vector2(_basket.Position.X, ScreenSize.Y - BasketBottomGap);
    }

    // ---- Setup (꾸미기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _hud.Visible = false;
        ClearFruits();
        _spawnTimer = 0.4f;
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);

        var title = Ui.Label("과일 받기", 42, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);

        var basketRow = ChoiceRow.Create("바구니", Basket.Choices, _basketColor, 120, chipHeight: SetupChipHeight);
        basketRow.Selected += i =>
        {
            _basketColor = i;
            _basket.SetColor(i);
            _basket.Squash();
            App.I.SetSetting(SettingsSection, "basket", i);
        };
        body.AddChild(basketRow);

        var fruitChoices = new List<Choice>();
        for (var i = 0; i < FruitRules.KindCount; i++)
            fruitChoices.Add(new Choice(Fruit.Names[i], Fruit.Textures[i]));
        var fruitRow = ChoiceRow.CreateMulti("과일", fruitChoices, i => FruitRules.Has(_kinds, i), 96, SetupChipHeight);
        fruitRow.Toggled += (i, on) =>
        {
            _kinds = FruitRules.Toggle(_kinds, i, out _);
            App.I.SetSetting(SettingsSection, "fruits", _kinds);
            if (on)
                SpawnPreviewFruit(i);
        };
        body.AddChild(fruitRow);

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

        // Left side of the screen; the right side stays open for the basket catching preview fruit
        var holder = new MarginContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        holder.AddThemeConstantOverride("margin_left", 24);
        var center = new CenterContainer();
        center.AddChild(panel);
        holder.AddChild(center);
        _setup = holder;
        _setupPanel = panel;
        _ui.AddChild(holder);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();

        if (App.I.Autoplay)
            GetTree().CreateTimer(1.2f).Timeout += () =>
            {
                if (_state == State.Setup)
                    StartRound();
            };
    }

    private void SpawnPreviewFruit(int kind = -1)
    {
        var size = ScreenSize;
        var (minX, maxX) = BasketRange();
        var reach = _basket.HalfOpening * 0.8f;
        var x = Mathf.Lerp(minX - reach, maxX + reach, (float)_rng.NextDouble());
        if (kind < 0)
            kind = PickKind();
        SpawnFruit(kind, false, x, FruitRules.FallSpeed(0, StartSpeed.Normal) * size.Y);
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        _setupPanel = null;
        ResultPanel.CloseAll(_ui);
        ClearFruits();

        _score = 0;
        _elapsed = 0;
        _spawnTimer = 0.3f;
        _groupLeft = 0;
        _streak = 0;
        _phaseIndex = 0;
        _hud.Reset();
        _hud.ScoreIcon = Ui.Tex(Fruit.Textures[FruitRules.Kinds(_kinds)[0]]);
        _hud.Visible = true;
        _basket.TargetX = ScreenSize.X / 2;
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
        MoveBasket(dt);
        StepFruits(dt);

        switch (_state)
        {
            case State.Setup:
                // Fruit keeps dropping into the basket on the right, so the basket color and fruit picks are visible
                _spawnTimer -= dt;
                if (_spawnTimer <= 0)
                {
                    _spawnTimer = 1.5f;
                    SpawnPreviewFruit();
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
        _hud.SetElapsed(_elapsed);

        var phase = FruitRules.PhaseAt(_elapsed);
        if (phase.Index != _phaseIndex)
        {
            _phaseIndex = phase.Index;
            _hud.ShowBanner(PhaseBanners[phase.Index]);
            Audio.I.SetMusicTempo(1f + 0.05f * phase.Index);
        }

        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            _spawnTimer += FruitRules.SpawnInterval(_elapsed, _speed);
            _groupLeft = FruitRules.GroupSize(_elapsed, _rng.NextDouble());
            _groupTimer = 0;
        }
        // A group (20~30초) drops its fruits a moment apart: "거의 동시에"
        _groupTimer -= dt;
        if (_groupLeft > 0 && _groupTimer <= 0)
        {
            _groupLeft--;
            _groupTimer = FruitRules.GroupStagger;
            SpawnRoundFruit();
        }

        if (_elapsed >= FruitRules.PlaySeconds)
            EndRound();
    }

    private void SpawnRoundFruit()
    {
        var size = ScreenSize;
        var margin = size.Y * FruitSizeRatio;
        // Spread out: not right on top of the last two fruits
        float x;
        var tries = 0;
        do
        {
            x = Mathf.Lerp(margin, size.X - margin, (float)_rng.NextDouble());
        } while ((Mathf.Abs(x - _recentX[0]) < 170 || Mathf.Abs(x - _recentX[1]) < 120) && ++tries < 8);
        _recentX[1] = _recentX[0];
        _recentX[0] = x;

        var bonusOnScreen = _fruits.Exists(f => f.Bonus);
        var bonus = FruitRules.IsBonus(_elapsed, bonusOnScreen, _rng.NextDouble());
        var speed = FruitRules.FallSpeed(_elapsed, _speed) * size.Y * (0.9f + 0.2f * (float)_rng.NextDouble());
        if (bonus)
            speed *= 0.85f; // A little slower, so the special one is catchable
        SpawnFruit(PickKind(), bonus, x, speed);
    }

    private int PickKind()
    {
        var kinds = FruitRules.Kinds(_kinds);
        var kind = kinds[_rng.Next(kinds.Count)];
        if (kind == _lastKind && kinds.Count > 1)
            kind = kinds[_rng.Next(kinds.Count)]; // Fewer repeats in a row
        _lastKind = kind;
        return kind;
    }

    private void SpawnFruit(int kind, bool bonus, float x, float speed)
    {
        var size = ScreenSize.Y * FruitSizeRatio * (bonus ? 0.9f : 0.95f + 0.1f * (float)_rng.NextDouble());
        var fruit = Fruit.Create(kind, bonus, new Vector2(x, -size), size, speed, (float)_rng.NextDouble() * Mathf.Tau);
        _fruits.Add(fruit);
        _fruitLayer.AddChild(fruit);
    }

    private void StepFruits(float dt)
    {
        var rimY = _basket.Position.Y + _basket.RimY;
        for (var i = _fruits.Count - 1; i >= 0; i--)
        {
            var fruit = _fruits[i];
            fruit.Step(dt);
            if (!fruit.Alive)
            {
                _fruits.RemoveAt(i);
                continue;
            }
            var catching = _state is State.Setup or State.Playing or State.Countdown;
            // The lower part of the fruit crossing the rim counts, so the sparkle comes as it drops in (not after it has sunk)
            var bottom = fruit.Radius * 0.9f;
            if (catching && FruitRules.IsCaught(fruit.Position.X, fruit.PreviousY + bottom, fruit.Position.Y + bottom,
                    _basket.Position.X, rimY, _basket.HalfOpening + fruit.Radius * 0.4f))
            {
                _fruits.RemoveAt(i);
                CatchFruit(fruit);
            }
            else if (fruit.Position.Y + fruit.Radius * 0.8f >= FloorY)
            {
                _fruits.RemoveAt(i);
                fruit.Land(FloorY);
                if (_state == State.Playing)
                    Audio.I.Plop();
            }
        }
    }

    private void EndRound()
    {
        _state = State.Ending;
        _elapsed = FruitRules.PlaySeconds;
        Audio.I.Whoosh();
        foreach (var f in _fruits)
            f.FadeOut();
        _fruits.Clear();
        _basket.Hop();
        _hud.ShowTimeUp(ShowResult);
    }

    private void ShowResult()
    {
        _state = State.Result;
        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            Caption = "받은 과일",
            Value = _score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}개",
            Entry = new ScoreEntry("", _score, 0, App.Now()),
            Recordable = _score > 0,
            NotRecordableMessage = "다음엔 과일을 꼭 받아 보자!",
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

    private void ClearFruits()
    {
        foreach (var f in _fruits)
            f.QueueFree();
        _fruits.Clear();
    }

    // ---- Basket control ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (App.I.Autoplay)
            return;
        // Touch press / drag (mouse buttons arrive as these too), plus plain mouse movement on PC.
        // Mouse motion during a drag repeats the drag position, so handling both is harmless.
        var x = e switch
        {
            InputEventScreenTouch { Pressed: true } touch => touch.Position.X,
            InputEventScreenDrag drag => drag.Position.X,
            InputEventMouseMotion motion => motion.Position.X,
            _ => float.NaN,
        };
        if (float.IsNaN(x) || _state == State.Result)
            return;
        _basket.TargetX = _world.ToLocal(new Vector2(x, 0)).X;
        _pointerIdle = 0;
    }

    private void MoveBasket(float dt)
    {
        var size = ScreenSize;
        var (minX, maxX) = BasketRange();
        _pointerIdle += dt;

        var dir = 0f;
        if (Input.IsActionPressed("ui_left") || Input.IsPhysicalKeyPressed(Key.A))
            dir -= 1;
        if (Input.IsActionPressed("ui_right") || Input.IsPhysicalKeyPressed(Key.D))
            dir += 1;
        if (dir != 0 && _state != State.Result)
        {
            _basket.TargetX = Mathf.Clamp(_basket.TargetX, minX, maxX) + dir * KeySpeed * size.X * dt;
            _pointerIdle = 0;
        }

        if (App.I.Autoplay && _state is State.Playing or State.Countdown)
            AutoplayAim();
        else if (_state == State.Setup && (_pointerIdle > PointerIdleTime || App.I.Autoplay))
            AutoplayAim(); // Setup preview: the basket catches by itself until the child takes over

        _basket.Step(dt, minX, maxX);
    }

    /// <summary>
    /// Where the basket may go. While setting up it stays in the open strip between the panel and the buddy
    /// (it may not move at all on a 16:9 screen; wider screens give it room).
    /// </summary>
    private (float Min, float Max) BasketRange()
    {
        var width = ScreenSize.X;
        var half = _basket.Width / 2;
        if (_state != State.Setup || _setupPanel is null)
            return (half, width - half);
        var min = _setupPanel.GlobalPosition.X + _setupPanel.Size.X + half + 6;
        var max = width - BuddyInset - 70 - half;
        return (min, Mathf.Max(min, max));
    }

    /// <summary>Moves toward the fruit that reaches the rim next (golden fruit first). Used by --autoplay and the setup preview.</summary>
    private void AutoplayAim()
    {
        var rimY = _basket.Position.Y + _basket.RimY;
        Fruit? best = null;
        var bestTime = float.MaxValue;
        foreach (var f in _fruits)
        {
            if (!f.Alive || f.Position.Y > rimY || f.Position.Y < 0)
                continue;
            var time = (rimY - f.Position.Y) / Mathf.Max(f.FallSpeed, 1f) - (f.Bonus ? 0.6f : 0f);
            if (time < bestTime)
            {
                bestTime = time;
                best = f;
            }
        }
        if (best is not null)
            _basket.TargetX = best.Position.X;
    }

    // ---- Catching ----

    private void CatchFruit(Fruit fruit)
    {
        var rim = new Vector2(_basket.Position.X, _basket.Position.Y + _basket.RimY);
        fruit.Catch(rim + new Vector2((fruit.Position.X - rim.X) * 0.5f, 18));
        _basket.Squash(fruit.Bonus ? 1.4f : 1f);

        var now = Time.GetTicksMsec() / 1000f;
        _streak = now - _lastCatchTime <= StreakWindow ? _streak + 1 : 1;
        _lastCatchTime = now;

        var playing = _state == State.Playing;
        var sparkleAt = new Vector2(fruit.Position.X, rim.Y - 10);
        // A common action: short and light (a few sparks and stars, no shake)
        _fxLayer.AddChild(Burst.Create(sparkleAt, fruit.Tint, 8, 320f, 6f, 0f, 0.35f, _rng));
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, sparkleAt, 3, 300f, 24f, 0.55f, _rng, 0.9f));
        Audio.I.Catch(_streak, playing ? -2f : -10f);
        if (!playing)
            return; // Setup preview (or a fruit landing during the countdown): just for fun, no score

        App.Vibrate(fruit.Bonus ? 25 : 12);
        var before = _score;
        _score += FruitRules.Points(fruit.Bonus);
        _hud.SetScore(_score, fruit.Bonus ? 0.5f : 0.3f);
        _hud.ScoreIcon = fruit.Texture;
        _buddy.Hop();

        if (fruit.Bonus)
            BonusCatch(sparkleAt);
        else
            _fxLayer.AddChild(new FloatingText
            {
                Text = "+1",
                Color = fruit.Tint.Lightened(0.35f),
                FontSize = 40,
                Position = sparkleAt + new Vector2(0, -30),
            });

        if (FruitRules.CrossedMilestone(before, _score, MilestoneEvery))
            Milestone(sparkleAt);
    }

    /// <summary>Golden fruit: a step above a normal catch (chime, gold ring, "+3", buddy calls it out).</summary>
    private void BonusCatch(Vector2 pos)
    {
        Audio.I.Coin();
        _fxLayer.AddChild(Burst.Create(pos, Fruit.BonusTint, 16, 480f, 8f, 80f, 0.5f, _rng));
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, pos, 6, 480f, 34f, 0.8f, _rng, 0.9f));
        _fxLayer.AddChild(new FloatingText
        {
            Text = "+3",
            Color = UiTheme.Gold,
            FontSize = 56,
            Position = pos + new Vector2(0, -40),
        });
        _buddy.Say("반짝반짝!", 0.9f);
    }

    /// <summary>Every 10 fruits: a bigger moment (sparkle run, star shower, a small shake, buddy cheers).</summary>
    private void Milestone(Vector2 pos)
    {
        Audio.I.Cheer();
        _shake.Add(4f);
        _fxLayer.AddChild(SpriteBurst.Create(_starTexture, pos, 10, 650f, 44f, 1.0f, _rng, 1.1f));
        _buddy.Say(Cheers[_rng.Next(Cheers.Length)]);
        Ui.Punch(_hud.ScoreLabel, 0.6f);
    }
}
