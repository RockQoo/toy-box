using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Memory;

/// <summary>
/// 동물 짝 맞추기 (docs/games/03-animal-match.md).
/// Flow: Setup (난이도, 배경, 응원 친구) → cards are dealt face down → all flip up for a look ("잘 기억해요!") → all flip
/// down → "시작!" and the clock runs → tap two cards: a match stays open with a chime, a hop and the animal's name; a
/// mismatch shows for about a second and quietly turns back → last pair → "모두 찾았어요!" → shared ResultPanel.
/// No countdown and no penalty for misses; the record is the time (fewer flips break ties).
/// </summary>
public partial class MemoryGame : Node2D
{
    private enum State { Setup, Dealing, Reveal, Play, Ending, Result }

    private const string GameId = "memory";
    private const string SettingsSection = "memory";
    private const float BuddyInset = 90f;        // Buddy x from the right edge
    private const float SetupChipHeight = 84f;
    private const float CardsTop = 96f;          // Below the top bar
    private const float CardsBottom = 16f;
    private const float CardsSideMargin = 170f;  // Keeps the cards clear of the buddy in the corner
    private const float CardGap = 18f;
    private const float MaxCardHeight = 300f;

    private static readonly string[] Cheers = { "척척!", "기억력 최고!", "대단해!", "또 찾았다!" };

    private readonly Random _rng = new();
    private readonly List<MemoryCard> _cards = new();
    private readonly List<(Control Slot, TextureRect Icon)> _slots = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Buddy _buddy = null!;
    private CanvasLayer _ui = null!;
    private Control _play = null!;       // HUD + cards (hidden while setting up)
    private Control _cardLayer = null!;
    private Node2D _fx = null!;
    private HBoxContainer _slotRow = null!;
    private Control _slotPanel = null!;
    private Label _timeLabel = null!;
    private Label _flipLabel = null!;
    private Label _banner = null!;
    private Control? _setup;
    private GridContainer? _preview;
    private Label? _previewCaption;
    private Texture2D _starTexture = null!;

    private MemoryLevel _level;
    private MemoryBoard _board = new(Array.Empty<int>());
    private int _roundToken;
    private int _streak;      // Pairs found in a row without a miss
    private int _shownSeconds = -1;

    // Autoplay: what the "player" remembers (card index → animal), imperfectly
    private readonly Dictionary<int, int> _remembered = new();

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

        _ui = new CanvasLayer();
        AddChild(_ui);

        _level = (MemoryLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)MemoryLevel.Easy), 0, 2);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Park);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 1));

        BuildPlayUi();
        _fx = new Node2D();
        _ui.AddChild(_fx);
        Layout();
        GetViewport().SizeChanged += Layout;

        Audio.I.PlayMusic(Music.Menu);
        ShowSetup();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private void Layout()
    {
        _buddy.Position = new Vector2(ScreenSize.X - BuddyInset, ScreenSize.Y - 24);
        if (_state is State.Reveal or State.Play or State.Ending)
            PlaceCards();
    }

    // ---- Play UI: quit, found pairs, time and flips ----

    private void BuildPlayUi()
    {
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_play);

        _cardLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _cardLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _play.AddChild(_cardLayer);

        var frame = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            frame.AddThemeConstantOverride($"margin_{side}", 18);
        _play.AddChild(frame);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(column);

        // Top bar: quit on the left, time and flips on the right (small: not a race against the clock)
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(top);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var stats = Ui.Panel(Colors.White with { A = 0.92f }, 10, 4);
        var statRow = new HBoxContainer();
        statRow.AddThemeConstantOverride("separation", 18);
        _timeLabel = Ui.Label("0초", 26, UiTheme.Ink);
        _flipLabel = Ui.Label("뒤집기 0번", 26, UiTheme.InkSoft);
        statRow.AddChild(_timeLabel);
        statRow.AddChild(_flipLabel);
        stats.AddChild(statRow);
        stats.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(stats);

        // Found pairs, centered on the screen: one slot per pair, filled with the animal when found
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        center.OffsetTop = 14;
        center.OffsetBottom = 82;
        _play.AddChild(center);
        var slotPanel = Ui.Panel(Colors.White with { A = 0.92f }, 8, 4);
        _slotRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _slotRow.AddThemeConstantOverride("separation", 6);
        slotPanel.AddChild(_slotRow);
        center.AddChild(slotPanel);
        _slotPanel = slotPanel;

        _banner = Ui.Label("", 72, UiTheme.Yellow, 22, UiTheme.Ink);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
        _banner.Visible = false;
        _play.AddChild(_banner);
    }

    private void BuildSlots(int pairs)
    {
        foreach (var child in _slotRow.GetChildren())
            child.QueueFree();
        _slots.Clear();
        for (var i = 0; i < pairs; i++)
        {
            var slot = new Panel { CustomMinimumSize = new Vector2(46, 52), MouseFilter = Control.MouseFilterEnum.Ignore };
            slot.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PaperDim, UiTheme.PaperDim.Darkened(0.12f), 3, 0, 10));
            var icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            slot.AddChild(icon);
            _slotRow.AddChild(slot);
            _slots.Add((slot, icon));
        }
    }

    // ---- Setup (난이도 고르기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _play.Visible = false;
        ClearCards();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var title = Ui.Label("동물 짝 맞추기", 42, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);

        var levelChoices = new List<Choice>();
        foreach (var v in MemoryRules.Variants)
            levelChoices.Add(new Choice(v.Label));
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 130, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (MemoryLevel)i;
            App.I.SetSetting(SettingsSection, "level", i);
            RefreshPreview();
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
            _buddy.Say("같이 찾자!", 0.9f);
            App.I.SetSetting(SettingsSection, "buddy", i);
        };
        body.AddChild(buddyRow);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        buttons.AddChild(Ui.Button("메인으로", App.I.GoToMainMenu, null, 28, null, new Vector2(180, 76)));
        buttons.AddChild(Ui.Button("시작!", StartRound, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        // Left: the options. Right: the cards of the picked difficulty (3 x 2 / 4 x 2 / 4 x 3), updated live.
        var holder = new HBoxContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddThemeConstantOverride("separation", 0);
        holder.AddChild(Ui.Spacer(0, 24));
        var left = new CenterContainer();
        left.AddChild(panel);
        holder.AddChild(left);
        var right = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        var previewBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        previewBox.AddThemeConstantOverride("separation", 12);
        _previewCaption = Ui.Label("", 30, UiTheme.Ink, 8, Colors.White);
        _previewCaption.HorizontalAlignment = HorizontalAlignment.Center;
        previewBox.AddChild(_previewCaption);
        _preview = new GridContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _preview.AddThemeConstantOverride("h_separation", 8);
        _preview.AddThemeConstantOverride("v_separation", 8);
        previewBox.AddChild(_preview);
        right.AddChild(previewBox);
        holder.AddChild(right);
        holder.AddChild(Ui.Spacer(0, 60)); // Buddy corner
        _setup = holder;
        _ui.AddChild(holder);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();
        RefreshPreview();

        if (App.I.Autoplay)
            GetTree().CreateTimer(1.2f).Timeout += () =>
            {
                if (_state == State.Setup)
                    StartRound();
            };
    }

    /// <summary>Shows a sample board of the picked difficulty, face up, so the card count is visible before starting.</summary>
    private void RefreshPreview()
    {
        if (_preview is null || _previewCaption is null)
            return;
        foreach (var child in _preview.GetChildren())
            child.QueueFree();
        var deal = MemoryRules.Deal(_level, _rng);
        _preview.Columns = MemoryRules.Columns(_level);
        _previewCaption.Text = $"카드 {deal.Length}장 · {deal.Length / 2}쌍";
        var size = _level == MemoryLevel.Hard ? new Vector2(78, 90) : new Vector2(90, 104);
        for (var i = 0; i < deal.Length; i++)
        {
            var card = MemoryCard.Create(i, deal[i]);
            card.CustomMinimumSize = size;
            card.MouseFilter = Control.MouseFilterEnum.Ignore;
            card.SetFace(true);
            _preview.AddChild(card);
            card.PivotOffset = size / 2;
            card.Scale = new Vector2(0.2f, 0.2f);
            card.Modulate = Colors.Transparent;
            var tween = card.CreateTween().SetParallel();
            tween.TweenProperty(card, "scale", Vector2.One, 0.3f).SetDelay(i * 0.04f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(card, "modulate", Colors.White, 0.12f).SetDelay(i * 0.04f);
        }
    }

    // ---- Round ----

    private async void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        _preview = null;
        _previewCaption = null;
        ResultPanel.CloseAll(_ui);
        ClearCards();

        _roundToken++;
        var token = _roundToken;
        _state = State.Dealing;
        _streak = 0;
        _shownSeconds = -1;
        _remembered.Clear();
        var deal = MemoryRules.Deal(_level, _rng);
        _board = new MemoryBoard(deal);
        BuildSlots(_board.PairTotal);
        _slotPanel.Visible = false;
        UpdateStats();
        _banner.Visible = false;
        _play.Visible = true;
        _play.Modulate = Colors.White;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic();

        // 1. Deal: the cards fly face down from a pile at the bottom to their places, one after another
        for (var i = 0; i < deal.Length; i++)
        {
            var card = MemoryCard.Create(i, deal[i]);
            card.Pressed += () => OnCardPressed(card);
            card.Disabled = true;
            _cards.Add(card);
            _cardLayer.AddChild(card);
        }
        PlaceCards();
        var pile = new Vector2(ScreenSize.X / 2, ScreenSize.Y + 40);
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            var target = card.Position;
            var delay = 0.15f + i * 0.07f;
            card.Position = pile - card.Size / 2;
            card.Rotation = (float)(_rng.NextDouble() - 0.5) * 0.8f;
            card.Modulate = Colors.Transparent;
            var tween = card.CreateTween().SetParallel();
            tween.TweenProperty(card, "position", target, 0.38f).SetDelay(delay)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(card, "rotation", 0f, 0.38f).SetDelay(delay).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(card, "modulate", Colors.White, 0.1f).SetDelay(delay);
            tween.TweenCallback(Callable.From(() => Audio.I.CardFlip(false))).SetDelay(delay);
        }
        await Wait(0.6f + _cards.Count * 0.07f);
        if (!Alive(token))
            return;

        // 2. Everything face up for a look, in a quick wave
        _state = State.Reveal;
        for (var i = 0; i < _cards.Count; i++)
            _cards[i].Turn(true, Wave(i), sound: i % 2 == 0);
        ShowBanner("잘 기억해요!", 56, UiTheme.Yellow, top: true);
        _buddy.Say("잘 봐 둬!", 1.2f);
        if (App.I.Autoplay)
            foreach (var card in _cards)
                if (_rng.NextDouble() < 0.35)
                    _remembered[card.Index] = card.Animal;
        await Wait(MemoryRules.RevealSeconds + 0.3f);
        if (!Alive(token))
            return;

        // 3. All face down again, then play
        HideBanner();
        for (var i = 0; i < _cards.Count; i++)
            _cards[i].Turn(false, Wave(i), sound: i % 2 == 0);
        await Wait(0.45f + Wave(_cards.Count - 1));
        if (!Alive(token))
            return;
        foreach (var card in _cards)
            card.Disabled = false;
        _state = State.Play;
        Audio.I.Go();
        ShowBanner("시작!", 80, UiTheme.Green, top: false, hold: 0.45f);
        _slotPanel.Visible = true;
        Ui.PopIn(_slotPanel, 0f, 0.3f);
        if (App.I.Autoplay)
            AutoTurn(token, 0.6f);
    }

    /// <summary>Delay of card <paramref name="i"/> in a left-to-right, top-to-bottom wave.</summary>
    private float Wave(int i)
    {
        var cols = MemoryRules.Columns(_level);
        return (i % cols + i / cols) * 0.06f;
    }

    public override void _Process(double delta)
    {
        if (_state != State.Play)
            return;
        _board.AddTime(delta);
        var seconds = (int)(_board.TimeMs / 1000);
        if (seconds != _shownSeconds)
        {
            _shownSeconds = seconds;
            _timeLabel.Text = $"{seconds}초";
        }
    }

    /// <summary>
    /// Lays the cards out in the level's grid (3 x 2, 4 x 2, 4 x 3) between the top bar and the bottom, as big as fit,
    /// keeping clear of the buddy's corner.
    /// </summary>
    private void PlaceCards()
    {
        var n = _cards.Count;
        if (n == 0)
            return;
        var cols = MemoryRules.Columns(_level);
        var rows = (n + cols - 1) / cols;
        var screen = ScreenSize;
        var areaW = screen.X - CardsSideMargin * 2;
        var areaH = screen.Y - CardsTop - CardsBottom;
        var h = Mathf.Min(MaxCardHeight, (areaH - CardGap * (rows - 1)) / rows);
        var w = Mathf.Min(h * 0.87f, (areaW - CardGap * (cols - 1)) / cols);
        h = w / 0.87f;
        var size = new Vector2(w, h);
        var totalW = cols * w + (cols - 1) * CardGap;
        var totalH = rows * h + (rows - 1) * CardGap;
        var origin = new Vector2((screen.X - totalW) / 2, CardsTop + (areaH - totalH) / 2);
        for (var i = 0; i < n; i++)
        {
            var card = _cards[i];
            card.Size = size;
            card.CustomMinimumSize = size;
            card.PivotOffset = size / 2;
            card.Position = origin + new Vector2(i % cols * (w + CardGap), i / cols * (h + CardGap));
        }
    }

    // ---- Taps ----

    private void OnCardPressed(MemoryCard card)
    {
        if (_state != State.Play)
            return;
        var result = _board.Flip(card.Index);
        if (result == MemoryFlip.Ignored)
            return;
        card.Turn(true);
        UpdateStats();
        if (App.I.Autoplay && _rng.NextDouble() < 0.7)
            _remembered[card.Index] = card.Animal;

        switch (result)
        {
            case MemoryFlip.Match:
                Matched(_cards[LastPartner(card)], card);
                break;
            case MemoryFlip.Mismatch:
                Mismatched(_roundToken);
                break;
        }
    }

    /// <summary>The other card of the pair that was just matched with <paramref name="card"/>.</summary>
    private int LastPartner(MemoryCard card) =>
        _cards.First(c => c != card && c.Animal == card.Animal).Index;

    /// <summary>
    /// The important moment: a bright chime that climbs with each pair, both cards hop with a gold frame and stars,
    /// the animal's name floats up, and the pair's slot at the top fills in. The last pair is the climax.
    /// </summary>
    private async void Matched(MemoryCard first, MemoryCard second)
    {
        var token = _roundToken;
        var last = _board.Finished;
        _streak++;
        _remembered.Remove(first.Index);
        _remembered.Remove(second.Index);

        await Wait(0.2f); // Let the second card finish turning over
        if (!Alive(token))
            return;
        Audio.I.MatchChime(_board.Pairs);
        App.Vibrate(30);
        first.MoveToFront();
        second.MoveToFront();
        first.Celebrate(0f, last);
        second.Celebrate(0.06f, last);
        var big = last ? 1.6f : 1f;
        foreach (var card in new[] { first, second })
            _fx.AddChild(SpriteBurst.Create(_starTexture, card.Center, (int)(5 * big), 420f * big, 34f, 0.7f, _rng, 0.9f));
        var mid = (first.Center + second.Center) / 2;
        var top = Mathf.Min(first.Position.Y, second.Position.Y);
        _fx.AddChild(new FloatingText
        {
            Text = $"{second.AnimalName}!",
            Color = UiTheme.Yellow,
            FontSize = 60,
            Position = new Vector2(mid.X, Mathf.Max(top + 30, CardsTop + 50)),
        });
        _buddy.Hop();
        FillSlot(_board.Pairs - 1, second.Animal, token);

        if (last)
        {
            EndRound(token);
            return;
        }
        if (_streak >= 2 && _rng.NextDouble() < 0.6)
            _buddy.Say(Cheers[_rng.Next(Cheers.Length)], 0.9f);
        if (App.I.Autoplay)
            AutoTurn(token, 0.55f);
    }

    private void FillSlot(int index, int animal, int token)
    {
        if (index < 0 || index >= _slots.Count)
            return;
        var (slot, icon) = _slots[index];
        GetTree().CreateTimer(0.25f, processAlways: false).Timeout += () =>
        {
            if (!Alive(token))
                return;
            icon.Texture = Ui.Tex($"res://assets/sprites/animal/{MemoryRules.Animals[animal]}.png");
            icon.Visible = true;
            slot.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Yellow.Lerp(Colors.White, 0.55f), UiTheme.Orange, 3, 0, 10));
            Ui.Punch(slot, 0.5f);
            Audio.I.CountStep(1f + 0.08f * _board.Pairs);
        };
    }

    /// <summary>Not the same animal: both stay up about a second, then quietly turn back. No failure sound or shake.</summary>
    private async void Mismatched(int token)
    {
        _streak = 0;
        await Wait(MemoryRules.MismatchSeconds + 0.2f);
        if (!Alive(token))
            return;
        if (_board.Hide() is { } pair)
        {
            _cards[pair.A].Turn(false);
            _cards[pair.B].Turn(false, 0.05f, sound: false);
        }
        if (App.I.Autoplay)
            AutoTurn(token, 0.6f);
    }

    private void UpdateStats()
    {
        _flipLabel.Text = $"뒤집기 {_board.Flips}번";
        if (_state != State.Play)
            _timeLabel.Text = $"{_board.TimeMs / 1000}초";
    }

    // ---- End ----

    /// <summary>Climax: all cards hop in a wave, "모두 찾았어요!" with a fanfare and confetti, then the result screen.</summary>
    private async void EndRound(int token)
    {
        _state = State.Ending;
        await Wait(0.85f); // After the last animal name has floated away
        if (!Alive(token))
            return;
        for (var i = 0; i < _cards.Count; i++)
            _cards[i].Hop(Wave(i), 1, 26f);
        Audio.I.Fanfare();
        Audio.I.DuckMusic(-12f);
        App.Vibrate(60);
        _fx.AddChild(Confetti.Create(ScreenSize, 80));
        ShowBanner("모두 찾았어요!", 100, UiTheme.Yellow, top: false);
        _buddy.Say("모두 찾았다!", 1.4f);

        await Wait(2.1f);
        if (!Alive(token))
            return;
        HideBanner();
        ShowResult();
    }

    private void ShowResult()
    {
        _state = State.Result;
        _play.Visible = false;
        ClearCards();
        var game = GameCatalog.Find(GameId)!;
        var seconds = _board.TimeMs / 1000f;
        var hardest = _level == MemoryLevel.Hard;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            VariantId = MemoryRules.VariantId(_level),
            Title = "모든 동물을 찾았어요!",
            Caption = "걸린 시간",
            Value = seconds,
            FormatValue = v => $"{v:0.0}초",
            Detail = $"뒤집은 횟수 {_board.Flips}번",
            Entry = _board.Entry("", App.Now()),
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                hardest
                    ? new ResultButton("난이도 고르기", UiTheme.Blue, () =>
                    {
                        ResultPanel.CloseAll(_ui);
                        ShowSetup();
                    })
                    : new ResultButton("다음 난이도", UiTheme.Blue, NextLevel),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    private void NextLevel()
    {
        _level = (MemoryLevel)Mathf.Min((int)_level + 1, (int)MemoryLevel.Hard);
        App.I.SetSetting(SettingsSection, "level", (int)_level);
        StartRound();
    }

    private void ClearCards()
    {
        foreach (var card in _cards)
            card.QueueFree();
        _cards.Clear();
    }

    // ---- Banner ----

    private Tween? _bannerTween;

    /// <summary>Big text over the board. <paramref name="top"/> puts it above the cards (the reveal hint).</summary>
    private void ShowBanner(string text, int size, Color color, bool top, float hold = 0f)
    {
        _bannerTween?.Kill();
        _banner.Text = text;
        _banner.AddThemeFontSizeOverride("font_size", size);
        _banner.AddThemeColorOverride("font_color", color);
        _banner.Visible = true;
        _banner.Modulate = Colors.White;
        // The reveal hint sits in the top bar (the pair slots are hidden until play starts), clear of the cards
        _banner.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
        _banner.OffsetTop = top ? 6 : 0;
        _banner.PivotOffset = top ? new Vector2(ScreenSize.X / 2, 45) : ScreenSize / 2;
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
    /// Plays one try like a child with a so-so memory: a remembered pair if it knows one, otherwise a new card, then its
    /// remembered partner or a guess. Cards are remembered only some of the time, so mismatches happen too.
    /// </summary>
    private async void AutoTurn(int token, float delay)
    {
        await Wait(delay);
        if (!Alive(token) || _state != State.Play || _board.Comparing)
            return;

        var open = _cards.Where(c => _board.CanFlip(c.Index)).ToList();
        if (open.Count == 0)
            return;
        MemoryCard first;
        var known = open.Where(c => _remembered.ContainsKey(c.Index)).ToList();
        var knownPair = known.FirstOrDefault(a => known.Any(b => b != a && b.Animal == a.Animal));
        if (knownPair is not null)
            first = knownPair;
        else
        {
            var fresh = open.Where(c => !_remembered.ContainsKey(c.Index)).ToList();
            first = (fresh.Count > 0 ? fresh : open)[_rng.Next(fresh.Count > 0 ? fresh.Count : open.Count)];
        }
        Tap(first);

        await Wait(0.5f);
        if (!Alive(token) || _state != State.Play || _board.OpenCard != first.Index)
            return;
        var rest = _cards.Where(c => _board.CanFlip(c.Index)).ToList();
        var partner = rest.FirstOrDefault(c => _remembered.ContainsKey(c.Index) && c.Animal == first.Animal);
        if (partner is null)
        {
            var guesses = rest.Where(c => !_remembered.ContainsKey(c.Index)).ToList();
            if (guesses.Count == 0)
                guesses = rest;
            partner = guesses[_rng.Next(guesses.Count)];
        }
        Tap(partner);
    }

    private void Tap(MemoryCard card)
    {
        Ui.Punch(card, -0.06f);
        OnCardPressed(card);
    }

    // ---- Helpers ----

    /// <summary>False once the scene is gone or a newer round (or the setup screen) has started.</summary>
    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _roundToken;

    /// <summary>Game-time wait: holds while the quit question pauses the tree.</summary>
    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
