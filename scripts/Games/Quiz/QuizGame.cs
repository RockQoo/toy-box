using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Quiz;

/// <summary>
/// 숫자 / 색깔 퀴즈 (docs/games/05-number-color-quiz.md).
/// Flow: Setup (문제 종류, 난이도, 배경, 응원 친구) → 10 questions → Ending ("다 풀었어요!") → Result (shared ResultPanel).
/// Each question: the prompt pops in with a chime and a big example → cards deal in → a wrong pick wiggles and dims
/// ("한 번 더 찾아볼까요?") → the right pick rings "딩동댕!" and, on the first try, sends a star to the progress row.
/// No countdown and no clock on screen: only the time spent looking at answerable cards is recorded (tie-break).
/// </summary>
public partial class QuizGame : Node2D
{
    private enum State { Setup, Showing, Asking, Feedback, Ending, Result }

    private const string GameId = "quiz";
    private const string SettingsSection = "quiz";
    private const float BuddyInset = 90f;       // Buddy x from the right edge
    private const float SetupChipHeight = 84f;
    private const float CardsTop = 290f;        // Below the top bar and the question panel
    private const float CardsSideMargin = 170f; // Keeps the cards clear of the buddy in the corner
    private const float CardGap = 26f;
    private const float FeedbackPause = 1.1f;

    private static readonly string[] Cheers = { "최고야!", "대단해!", "멋져요!", "잘한다!", "척척박사!" };

    private static readonly IReadOnlyList<Choice> ModeChoices = new[]
    {
        new Choice("색깔", "res://assets/sprites/quiz/shape_red_circle.png"),
        new Choice("숫자", "res://assets/sprites/quiz/num_3.png"),
        new Choice("섞어서", "res://assets/sprites/ui/star.png"),
    };

    private readonly Random _rng = new();
    private readonly List<QuizCard> _cards = new();
    private readonly List<ProgressSlot> _slots = new();

    private State _state;
    private Scenery _scenery = null!;
    private Node2D _world = null!;
    private Buddy _buddy = null!;
    private CanvasLayer _ui = null!;
    private Control _play = null!;       // Top bar + question panel + cards (hidden while setting up)
    private Control _cardLayer = null!;
    private Node2D _fx = null!;
    private Control _questionPanel = null!;
    private QuizPicture _picture = null!;
    private Label _questionText = null!;
    private Label _hint = null!;
    private Control? _setup;
    private GridContainer? _preview;
    private Texture2D _starTexture = null!;

    private QuizMode _mode;
    private QuizLevel _level;
    private QuizDeck _deck = null!;
    private QuizRound _round = new();
    private QuizQuestion? _question;
    private int _roundToken;
    private int _streak;

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

        _mode = (QuizMode)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "mode", (int)QuizMode.Color), 0, 2);
        _level = (QuizLevel)Mathf.Clamp((int)App.I.GetSetting(SettingsSection, "level", (int)QuizLevel.Normal), 0, 2);
        _scenery.Kind = (SceneryKind)(int)App.I.GetSetting(SettingsSection, "scenery", (int)SceneryKind.Sky);
        _buddy.SetCharacter((int)App.I.GetSetting(SettingsSection, "buddy", 0));

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
        if (_state is State.Showing or State.Asking or State.Feedback)
            PlaceCards(_cards, false);
    }

    // ---- Play UI: top bar (quit, progress) and the question panel ----

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
        column.AddThemeConstantOverride("separation", 14);
        frame.AddChild(column);

        // Top bar: quit on the left, the 10-question progress row centered
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(top);
        var quit = Ui.Button("그만하기", () => QuitConfirm.Open(_ui), null, 22, null, new Vector2(130, 56));
        quit.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(quit);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var progress = Ui.Panel(Colors.White, 10, 4);
        var dots = new HBoxContainer();
        dots.AddThemeConstantOverride("separation", 4);
        for (var i = 0; i < QuizRules.QuestionCount; i++)
        {
            var slot = new ProgressSlot { CustomMinimumSize = new Vector2(40, 40), MouseFilter = Control.MouseFilterEnum.Ignore };
            _slots.Add(slot);
            dots.AddChild(slot);
        }
        progress.AddChild(dots);
        top.AddChild(progress);
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        top.AddChild(Ui.Spacer(0, 130)); // Balances the quit button so the progress row sits in the middle

        // Question: big example picture + the sentence, and a line for "한 번 더 찾아볼까요?"
        var panel = Ui.Panel(UiTheme.Paper with { A = 0.96f }, 14);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 22);
        panel.AddChild(row);
        _picture = new QuizPicture { CustomMinimumSize = new Vector2(130, 130), MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(_picture);
        var texts = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        texts.AddThemeConstantOverride("separation", 4);
        row.AddChild(texts);
        _questionText = Ui.Label("", 52, UiTheme.Ink);
        texts.AddChild(_questionText);
        _hint = Ui.Label("한 번 더 찾아볼까요?", 32, UiTheme.Pink, 8, Colors.White);
        _hint.Modulate = Colors.Transparent;
        texts.AddChild(_hint);
        row.AddChild(Ui.Spacer(0, 8));
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.AddChild(panel);
        column.AddChild(center);
        _questionPanel = panel;
    }

    // ---- Setup (문제 고르기) ----

    private void ShowSetup()
    {
        _state = State.Setup;
        _roundToken++;
        _play.Visible = false;
        ClearCards();
        Audio.I.PlayMusic(Music.Menu);

        var panel = Ui.Panel(UiTheme.Paper with { A = 0.94f }, 22);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(body);

        var title = Ui.Label("숫자 / 색깔 퀴즈", 42, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);

        var modeRow = ChoiceRow.Create("문제", ModeChoices, (int)_mode, 130, chipHeight: SetupChipHeight);
        modeRow.Selected += i =>
        {
            _mode = (QuizMode)i;
            App.I.SetSetting(SettingsSection, "mode", i);
            RefreshPreview();
        };
        body.AddChild(modeRow);

        var levelChoices = new List<Choice>();
        foreach (var v in QuizRules.Variants)
            levelChoices.Add(new Choice(v.Label));
        var levelRow = ChoiceRow.Create("난이도", levelChoices, (int)_level, 130, chipHeight: SetupChipHeight);
        levelRow.Selected += i =>
        {
            _level = (QuizLevel)i;
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
        buttons.AddChild(Ui.Button("시작!", StartRound, UiTheme.Green, 40,
            Ui.Tex("res://assets/sprites/ui/play.png"), new Vector2(300, 76)));
        body.AddChild(buttons);

        // Left: the options. Right: a sample of the cards the child will choose from (2 / 4 / 6), updated live.
        var holder = new HBoxContainer();
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddThemeConstantOverride("separation", 0);
        holder.AddChild(Ui.Spacer(0, 24));
        var left = new CenterContainer();
        left.AddChild(panel);
        holder.AddChild(left);
        var right = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        _preview = new GridContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _preview.AddThemeConstantOverride("h_separation", 10);
        _preview.AddThemeConstantOverride("v_separation", 10);
        right.AddChild(_preview);
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

    /// <summary>Deals a sample question's cards on the setup screen, so the picked type and card count are visible.</summary>
    private void RefreshPreview()
    {
        if (_preview is null)
            return;
        foreach (var child in _preview.GetChildren())
            child.QueueFree();
        var question = new QuizDeck(_mode, _level, _rng).Next();
        var count = question.Choices.Count;
        _preview.Columns = count <= 2 ? 2 : count <= 4 ? 2 : 3;
        var size = count <= 2 ? new Vector2(120, 140) : new Vector2(96, 112);
        for (var i = 0; i < count; i++)
        {
            var card = QuizCard.Create(question.Kind, question.Choices[i], size);
            card.MouseFilter = Control.MouseFilterEnum.Ignore;
            _preview.AddChild(card);
            DealIn(card, i * 0.05f);
        }
    }

    // ---- Round ----

    private void StartRound()
    {
        if (_state is not (State.Setup or State.Result))
            return;
        _setup?.QueueFree();
        _setup = null;
        _preview = null;
        ResultPanel.CloseAll(_ui);
        ClearCards();

        _roundToken++;
        _round = new QuizRound();
        _deck = new QuizDeck(_mode, _level, _rng);
        _streak = 0;
        foreach (var slot in _slots)
            slot.State = ProgressSlot.Mark.Todo;
        _play.Visible = true;
        _play.Modulate = Colors.White;
        Audio.I.PlayMusic(Music.Play);
        Audio.I.ResetMusic();
        Audio.I.Go();
        NextQuestion(_roundToken, 0.35f);
    }

    public override void _Process(double delta)
    {
        if (_state == State.Asking)
            _round.AddTime(delta);
    }

    private async void NextQuestion(int token, float delay = 0f)
    {
        _state = State.Showing;
        if (delay > 0)
        {
            await Wait(delay);
            if (!Alive(token))
                return;
        }
        ClearCards();
        _question = _deck.Next();
        _slots[_round.Index].State = ProgressSlot.Mark.Current;

        _questionText.Text = QuizRules.Prompt(_question.Kind, _question.Answer);
        _picture.Show(_question.Kind, _question.Answer);
        _hint.Modulate = Colors.Transparent;
        Audio.I.QuestionChime();
        // The panel resizes to the new sentence; pop it once the container has laid it out
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!Alive(token))
            return;
        Ui.PopIn(_questionPanel, 0f, 0.3f);
        Ui.Punch(_picture, 0.35f);

        var cards = new List<QuizCard>();
        for (var i = 0; i < _question.Choices.Count; i++)
        {
            var card = QuizCard.Create(_question.Kind, _question.Choices[i], Vector2.One);
            card.Pressed += () => OnCardPressed(card);
            cards.Add(card);
            _cardLayer.AddChild(card);
        }
        _cards.AddRange(cards);
        PlaceCards(cards, true);

        await Wait(0.3f + 0.07f * cards.Count);
        if (!Alive(token))
            return;
        _state = State.Asking;
        if (App.I.Autoplay)
            AutoAnswer(token);
    }

    /// <summary>
    /// Lays the cards out under the question: one row for 2 and 4 cards, 3 x 2 for 6. Cards are as big as fit,
    /// capped so 2-card (쉬움) cards are not huge.
    /// </summary>
    private void PlaceCards(List<QuizCard> cards, bool dealIn)
    {
        var n = cards.Count;
        if (n == 0)
            return;
        var cols = n <= 4 ? n : 3;
        var rows = (n + cols - 1) / cols;
        var screen = ScreenSize;
        var areaW = screen.X - CardsSideMargin * 2;
        var areaH = screen.Y - CardsTop - 24;
        var h = Mathf.Min(rows == 1 ? 330f : 200f, (areaH - CardGap * (rows - 1)) / rows);
        var w = Mathf.Min(h * 0.87f, (areaW - CardGap * (cols - 1)) / cols);
        h = Mathf.Min(h, w / 0.87f);
        var size = new Vector2(w, h);
        var totalW = cols * w + (cols - 1) * CardGap;
        var totalH = rows * h + (rows - 1) * CardGap;
        var origin = new Vector2((screen.X - totalW) / 2, CardsTop + (areaH - totalH) / 2);
        for (var i = 0; i < n; i++)
        {
            var card = cards[i];
            card.Size = size;
            card.CustomMinimumSize = size;
            card.PivotOffset = size / 2;
            card.Position = origin + new Vector2(i % cols * (w + CardGap), i / cols * (h + CardGap));
            if (dealIn)
                DealIn(card, i * 0.07f);
        }
    }

    private static void DealIn(Control card, float delay)
    {
        card.Scale = new Vector2(0.2f, 0.2f);
        card.Modulate = Colors.Transparent;
        var tween = card.CreateTween().SetParallel();
        tween.TweenProperty(card, "scale", Vector2.One, 0.35f).SetDelay(delay)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "modulate", Colors.White, 0.15f).SetDelay(delay);
    }

    // ---- Answers ----

    private void OnCardPressed(QuizCard card)
    {
        if (_state != State.Asking || card.Dimmed || _question is null)
            return;
        var result = _round.Answer(card.Value == _question.Answer);
        if (result == QuizAnswer.Retry)
            WrongPick(card);
        else
            RightPick(card, result == QuizAnswer.CorrectFirstTry);
    }

    /// <summary>Gentle "try again": the card wiggles and fades back, the example pulses, nothing reveals the answer.</summary>
    private void WrongPick(QuizCard card)
    {
        _streak = 0;
        Audio.I.Retry();
        card.Dim();
        ShowHint("한 번 더 찾아볼까요?", UiTheme.Pink);
        Ui.Punch(_picture, 0.3f);
        if (App.I.Autoplay)
            AutoAnswer(_roundToken, 0.7f);
    }

    /// <summary>
    /// The key moment: "딩동댕!", the card jumps with a check mark and sparks, the others clear away, and a first-try
    /// answer sends a star up to the progress row. The 10th answer gets a bigger burst before the ending.
    /// </summary>
    private async void RightPick(QuizCard card, bool firstTry)
    {
        var token = _roundToken;
        _state = State.Feedback;
        var slot = _slots[_round.Index - 1];
        var last = _round.Finished;
        var center = card.Position + card.Size / 2;

        Audio.I.DingDongDang();
        App.Vibrate(30);
        // Same line as "한 번 더 찾아볼까요?", so the panel keeps its size
        ShowHint("딩동댕!", UiTheme.Green);

        card.MoveToFront();
        card.Disabled = true;
        var jump = card.CreateTween();
        jump.TweenProperty(card, "scale", new Vector2(1.18f, 1.18f), 0.12f).SetEase(Tween.EaseType.Out);
        jump.TweenProperty(card, "scale", new Vector2(1.08f, 1.08f), 0.3f)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        var check = Ui.Icon("res://assets/sprites/ui/check.png", card.Size.X * 0.42f);
        check.Position = new Vector2(card.Size.X * 0.62f, -card.Size.X * 0.12f);
        card.AddChild(check);
        Ui.PopIn(check, 0.05f, 0.3f);

        foreach (var other in _cards)
            if (other != card)
            {
                var fade = other.CreateTween().SetParallel();
                fade.TweenProperty(other, "modulate:a", 0f, 0.25f);
                fade.TweenProperty(other, "scale", new Vector2(0.8f, 0.8f), 0.25f);
            }

        var big = last ? 1.5f : 1f;
        _fx.AddChild(Burst.Create(center, card.Tint, (int)(14 * big), 520f * big, 8f, 110f * big, 0.5f, _rng));
        _fx.AddChild(SpriteBurst.Create(_starTexture, center, (int)(6 * big), 520f * big, 38f, 0.8f, _rng, 0.9f));
        _buddy.Hop();

        if (firstTry)
        {
            _streak++;
            FlyStar(center, slot, token);
            if (_streak >= 3 && _streak % 3 == 0 && !last)
                _buddy.Say(Cheers[_rng.Next(Cheers.Length)]);
        }
        else
        {
            // Found it after a retry: the slot is filled (no star), still a happy moment
            slot.State = ProgressSlot.Mark.Done;
            Ui.Punch(slot, 0.4f);
            _buddy.Say("찾았다!", 0.8f);
        }

        if (last)
        {
            // Climax beat: the round is complete
            await Wait(0.45f);
            if (!Alive(token))
                return;
            Audio.I.Cheer();
            _fx.AddChild(SpriteBurst.Create(_starTexture, center, 12, 700f, 46f, 1.1f, _rng, 1.1f));
        }

        await Wait(FeedbackPause);
        if (!Alive(token))
            return;
        var away = card.CreateTween().SetParallel();
        away.TweenProperty(card, "scale", new Vector2(0.3f, 0.3f), 0.22f).SetEase(Tween.EaseType.In);
        away.TweenProperty(card, "modulate:a", 0f, 0.22f);
        if (last)
            EndRound(token);
        else
            NextQuestion(token, 0.25f);
    }

    /// <summary>A star flies from the answer card to its progress slot, which then lights up.</summary>
    private void FlyStar(Vector2 from, ProgressSlot slot, int token)
    {
        var size = new Vector2(64, 64);
        var star = new TextureRect
        {
            Texture = _starTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = size,
            PivotOffset = size / 2,
            Position = from - size / 2,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _play.AddChild(star);
        var target = slot.GlobalPosition + slot.Size / 2 - size / 2;
        var tween = star.CreateTween();
        tween.TweenProperty(star, "scale", new Vector2(1.4f, 1.4f), 0.15f).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(star, "position", target, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(star, "scale", new Vector2(0.65f, 0.65f), 0.5f);
        tween.Parallel().TweenProperty(star, "rotation", Mathf.Tau, 0.5f);
        tween.TweenCallback(Callable.From(() =>
        {
            star.QueueFree();
            if (!Alive(token))
                return;
            slot.State = ProgressSlot.Mark.Star;
            Ui.Punch(slot, 0.6f);
            Audio.I.CountStep(1f + 0.06f * _round.Score);
            _fx.AddChild(SpriteBurst.Create(_starTexture, slot.GlobalPosition + slot.Size / 2, 4, 220f, 18f, 0.45f, _rng, 0.3f));
        }));
    }

    // ---- End ----

    /// <summary>"다 풀었어요!" (or "모두 맞혔어요!" with confetti for 10 out of 10), then the result screen.</summary>
    private async void EndRound(int token)
    {
        _state = State.Ending;
        var perfect = _round.Perfect;
        var fade = _questionPanel.CreateTween();
        fade.TweenProperty(_questionPanel, "modulate:a", 0f, 0.25f);

        var banner = Ui.Label(perfect ? "모두 맞혔어요!" : "다 풀었어요!", 110, UiTheme.Yellow, 26, UiTheme.Ink);
        banner.HorizontalAlignment = HorizontalAlignment.Center;
        banner.VerticalAlignment = VerticalAlignment.Center;
        banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _play.AddChild(banner);
        banner.PivotOffset = ScreenSize / 2;
        banner.Scale = new Vector2(0.3f, 0.3f);
        banner.CreateTween().TweenProperty(banner, "scale", Vector2.One, 0.45f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        Audio.I.TimeUp();
        Audio.I.DuckMusic(-12f);
        _buddy.Say(perfect ? "척척박사!" : "잘했어요!", 1.2f);
        if (perfect)
            _fx.AddChild(Confetti.Create(ScreenSize, 70));

        await Wait(perfect ? 1.9f : 1.5f);
        if (!Alive(token))
            return;
        banner.QueueFree();
        _questionPanel.Modulate = Colors.White;
        ShowResult();
    }

    private void ShowResult()
    {
        _state = State.Result;
        _play.Visible = false;
        ClearCards();
        var score = _round.Score;
        var game = GameCatalog.Find(GameId)!;
        _ui.AddChild(ResultPanel.Create(new ResultSpec
        {
            Game = game,
            VariantId = QuizRules.VariantId(_level),
            Title = _round.Perfect ? "모두 맞혔어요!" : "다 풀었어요!",
            Caption = $"{QuizRules.QuestionCount}문제 중",
            Value = score,
            FormatValue = v => $"{Mathf.RoundToInt(v)}문제를 맞혔어요!",
            ValueFontSize = 58,
            StarsTotal = QuizRules.QuestionCount,
            StarsEarned = score,
            Entry = new ScoreEntry("", score, _round.TimeMs, App.Now()),
            Recordable = score > 0,
            NotRecordableMessage = "다음엔 한 번에 찾아서 별을 모아 보자!",
            Buttons = new[]
            {
                new ResultButton("다시 하기", UiTheme.Green, StartRound, "res://assets/sprites/ui/retry.png"),
                new ResultButton("다른 문제", UiTheme.Blue, () =>
                {
                    ResultPanel.CloseAll(_ui);
                    ShowSetup();
                }),
                new ResultButton("메인으로", UiTheme.Pink, App.I.GoToMainMenu),
            },
        }));
    }

    private void ClearCards()
    {
        foreach (var card in _cards)
            card.QueueFree();
        _cards.Clear();
    }

    // ---- Autoplay (-- --autoplay) ----

    /// <summary>Taps a card after a short look. Sometimes picks a wrong card first (always on the 2nd question) to run the retry path.</summary>
    private async void AutoAnswer(int token, float delay = 0.6f)
    {
        await Wait(delay);
        if (!Alive(token) || _state != State.Asking || _question is null)
            return;
        QuizCard? pick = null;
        if (_round.Misses == 0 && (_round.Index == 1 || _rng.NextDouble() < 0.25))
        {
            var wrong = _cards.FindAll(c => !c.Dimmed && c.Value != _question.Answer);
            if (wrong.Count > 0)
                pick = wrong[_rng.Next(wrong.Count)];
        }
        pick ??= _cards.Find(c => c.Value == _question.Answer);
        if (pick is null)
            return;
        Ui.Punch(pick, -0.08f);
        OnCardPressed(pick);
    }

    // ---- Helpers ----

    private void ShowHint(string text, Color color)
    {
        _hint.Text = text;
        _hint.AddThemeColorOverride("font_color", color);
        _hint.Modulate = Colors.White;
        Ui.PopIn(_hint, 0f, 0.3f);
    }

    /// <summary>False once the scene is gone or a newer round (or the setup screen) has started.</summary>
    private bool Alive(int token) => IsInstanceValid(this) && IsInsideTree() && token == _roundToken;

    /// <summary>Game-time wait: holds while the quit question pauses the tree.</summary>
    private SignalAwaiter Wait(float seconds) =>
        ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);
}
