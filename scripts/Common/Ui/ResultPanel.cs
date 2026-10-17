using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>What a game hands to the result screen.</summary>
public sealed class ResultSpec
{
    public required GameInfo Game { get; init; }
    /// <summary>Which leaderboard of a game with variants (see <see cref="GameInfo.Variants"/>), e.g. the quiz difficulty.</summary>
    public string? VariantId { get; init; }
    public string Title { get; init; } = "게임 끝!";
    /// <summary>Caption above the big number, e.g. "터뜨린 풍선".</summary>
    public required string Caption { get; init; }
    /// <summary>The big number. Counted up from 0 when shown.</summary>
    public required float Value { get; init; }
    public required Func<float, string> FormatValue { get; init; }
    /// <summary>Smaller for a long value text (e.g. "9문제를 맞혔어요!").</summary>
    public int ValueFontSize { get; init; } = 84;
    /// <summary>An extra line under the big number (e.g. "뒤집은 횟수 18번"). Shown once the count-up ends. Null = none.</summary>
    public string? Detail { get; init; }
    /// <summary>A row of this many stars under the value, <see cref="StarsEarned"/> of them lit in step with the count-up. 0 = no row.</summary>
    public int StarsTotal { get; init; }
    public int StarsEarned { get; init; }
    /// <summary>Text next to the star row for a lit count. Null = "별 N개".</summary>
    public Func<int, string>? StarsLabel { get; init; }
    /// <summary>The record to rank. Its name is filled in from the name entry.</summary>
    public required ScoreEntry Entry { get; init; }
    /// <summary>False skips ranking entirely (e.g. a score of 0).</summary>
    public bool Recordable { get; init; } = true;
    public string NotRecordableMessage { get; init; } = "다음엔 꼭 해 보자!";
    public required IReadOnlyList<ResultButton> Buttons { get; init; }
}

public sealed record ResultButton(string Text, Color Color, Action OnPressed, string? IconPath = null);

/// <summary>
/// The shared result screen: count-up of the result → best-record celebration → name entry if it made the top 10
/// → ranking with the new row highlighted → next-action buttons. Add it on a CanvasLayer; it fills the screen.
/// Name entry shows the recent names as big one-tap buttons; typing is behind a "새 이름" button (or shown
/// directly when there are no recent names yet).
/// </summary>
public partial class ResultPanel : Control
{
    private static readonly Color[] NameColors = { UiTheme.Blue, UiTheme.Pink, UiTheme.Purple };

    private ResultSpec _spec = null!;
    private Label _value = null!;
    private Label? _detail;
    private Label _best = null!;
    private Label _badge = null!;
    private Label _message = null!;
    private VBoxContainer _nameBox = null!;
    private Label _namePrompt = null!;
    private GridContainer _namePicks = null!;
    private HBoxContainer _nameTyping = null!;
    private LineEdit? _nameEdit;        // Phones: OS keyboard
    private HangulField? _hangulField;  // PC: composes Hangul itself (the OS IME did not reach Godot)
    private string[] _recentNames = System.Array.Empty<string>();
    private int _rank = -1;
    private HBoxContainer _buttons = null!;
    private RankingList _ranking = null!;
    private Control _confettiHost = null!;
    private bool _submitted;
    private readonly List<TextureRect> _stars = new();
    private Label? _starCount;
    private int _litStars;
    private bool _typedSubmitPending;

    public static ResultPanel Create(ResultSpec spec)
    {
        var panel = new ResultPanel { _spec = spec };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        return panel;
    }

    /// <summary>Removes any result screen under <paramref name="parent"/> (e.g. "다시 하기" or "꾸미기").</summary>
    public static void CloseAll(Node parent)
    {
        foreach (var child in parent.GetChildren())
            if (child is ResultPanel)
                child.QueueFree();
    }

    public override void _Ready()
    {
        AddChild(Ui.Backdrop());

        var card = Ui.Panel(margin: 28);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 36);
        card.AddChild(columns);
        AddChild(Ui.Centered(card));

        // Left: result
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0), Alignment = BoxContainer.AlignmentMode.Center };
        left.AddThemeConstantOverride("separation", 8);
        columns.AddChild(left);

        var title = Ui.Label(_spec.Title, 52, UiTheme.Orange, 12, UiTheme.Ink);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(title);

        var caption = Ui.Label(_spec.Caption, 30, UiTheme.InkSoft);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(caption);

        _value = Ui.Label(_spec.FormatValue(0), _spec.ValueFontSize, UiTheme.Blue, 14, Colors.White);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(_value);

        if (_spec.Detail is { } detail)
        {
            _detail = Ui.Label(detail, 32, UiTheme.Ink);
            _detail.HorizontalAlignment = HorizontalAlignment.Center;
            _detail.Modulate = Colors.Transparent;
            left.AddChild(_detail);
        }

        if (_spec.StarsTotal > 0)
            left.AddChild(BuildStars());

        // The previous best and the "최고 기록!" badge share one slot: the badge replaces the line when beaten
        var bestSlot = new MarginContainer();
        left.AddChild(bestSlot);
        var board = App.I.Board(_spec.Game.Id, _spec.VariantId);
        _best = Ui.Label(board.Best is { } best ? $"최고 기록  {_spec.Game.FormatValue(best)}" : "첫 기록에 도전!", 26, UiTheme.InkSoft);
        _best.HorizontalAlignment = HorizontalAlignment.Center;
        _best.VerticalAlignment = VerticalAlignment.Center;
        bestSlot.AddChild(_best);
        _badge = Ui.Label("최고 기록!", 40, UiTheme.Gold, 12, UiTheme.Ink);
        _badge.HorizontalAlignment = HorizontalAlignment.Center;
        _badge.Modulate = Colors.Transparent;
        bestSlot.AddChild(_badge);

        _message = Ui.Label("", 26, UiTheme.Ink);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(_message);

        _nameBox = BuildNameEntry();
        _nameBox.Visible = false;
        left.AddChild(_nameBox);

        _buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
        _buttons.AddThemeConstantOverride("separation", 14);
        foreach (var b in _spec.Buttons)
        {
            var icon = b.IconPath is null ? null : Ui.Tex(b.IconPath);
            _buttons.AddChild(Ui.Button(b.Text, b.OnPressed, b.Color, 26, icon, new Vector2(140, 76)));
        }
        left.AddChild(Ui.Spacer(8));
        left.AddChild(_buttons);

        // Right: ranking
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        right.AddThemeConstantOverride("separation", 8);
        columns.AddChild(right);
        var header = Ui.Label($"{_spec.Game.BoardTitle(_spec.VariantId)} 순위", 28, UiTheme.Ink);
        header.HorizontalAlignment = HorizontalAlignment.Center;
        right.AddChild(header);
        _ranking = RankingList.Create(_spec.Game, _spec.VariantId);
        right.AddChild(_ranking);
        _ranking.Refresh();

        _confettiHost = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _confettiHost.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_confettiHost);

        Ui.PopIn(card, 0f, 0.4f);
        Audio.I.Whoosh();
        Audio.I.DuckMusic(-10f);
        RunSequence();
    }

    /// <summary>A row of dim stars plus "별 N개" (filled in as they light up).</summary>
    private Control BuildStars()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 2);
        var size = Mathf.Min(_spec.StarsTotal <= 5 ? 52f : 34f, 340f / _spec.StarsTotal); // A few stars (maze: 3) are shown bigger
        for (var i = 0; i < _spec.StarsTotal; i++)
        {
            var star = Ui.Icon("res://assets/sprites/ui/star.png", size);
            star.SelfModulate = DimStar;
            row.AddChild(star);
            _stars.Add(star);
        }
        row.AddChild(Ui.Spacer(0, 10));
        _starCount = Ui.Label(StarsText(0), 28, UiTheme.Orange, 8, Colors.White);
        row.AddChild(_starCount);
        return row;
    }

    private static readonly Color DimStar = new(0.2f, 0.22f, 0.36f, 0.22f);

    private string StarsText(int count) => _spec.StarsLabel?.Invoke(count) ?? $"별 {count}개";

    private void LightStars(int count)
    {
        count = Mathf.Clamp(count, 0, _stars.Count);
        for (; _litStars < count; _litStars++)
        {
            var star = _stars[_litStars];
            star.SelfModulate = Colors.White;
            star.PivotOffset = star.Size / 2;
            star.Scale = new Vector2(1.6f, 1.6f);
            star.CreateTween().TweenProperty(star, "scale", Vector2.One, 0.25f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        if (_starCount is not null)
            _starCount.Text = StarsText(count);
    }

    private VBoxContainer BuildNameEntry()
    {
        _recentNames = App.I.RecentNames;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _namePrompt = Ui.Label("", 28, UiTheme.Ink);
        _namePrompt.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_namePrompt);

        // One-tap recent names (2 columns), then "새 이름" which opens the keyboard row
        _namePicks = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        _namePicks.AddThemeConstantOverride("h_separation", 12);
        _namePicks.AddThemeConstantOverride("v_separation", 10);
        for (var i = 0; i < _recentNames.Length; i++)
        {
            var name = _recentNames[i];
            var pick = Ui.Button(name, () => SubmitName(name), NameColors[i % NameColors.Length], 30, null, new Vector2(240, 76));
            pick.ClipText = true;
            pick.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _namePicks.AddChild(pick);
        }
        _namePicks.AddChild(Ui.Button("새 이름", ShowTyping, null, 28, null, new Vector2(240, 76)));
        box.AddChild(_namePicks);

        _nameTyping = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
        _nameTyping.AddThemeConstantOverride("separation", 10);
        if (OS.HasFeature("mobile"))
        {
            // No select-all-on-focus and no prefilled text: with an IME, the composing syllable is kept apart
            // from Text until it is committed, so the field starts empty and is committed explicitly on submit.
            _nameEdit = new LineEdit
            {
                PlaceholderText = RecentNames.DefaultName,
                MaxLength = RecentNames.MaxLength,
                CustomMinimumSize = new Vector2(_recentNames.Length > 0 ? 230 : 300, 72),
                Alignment = HorizontalAlignment.Center,
            };
            _nameEdit.AddThemeFontSizeOverride("font_size", 32);
            _nameEdit.TextSubmitted += _ => SubmitTyped();
            _nameTyping.AddChild(_nameEdit);
        }
        else
        {
            _hangulField = HangulField.Create(RecentNames.MaxLength, RecentNames.DefaultName,
                new Vector2(_recentNames.Length > 0 ? 200 : 250, 72));
            _hangulField.Submitted += SubmitTyped;
            _nameTyping.AddChild(_hangulField);
            Button mode = null!;
            mode = Ui.Button("한", () =>
            {
                _hangulField.Korean = !_hangulField.Korean;
                _hangulField.GrabFocus();
            }, UiTheme.Purple, 26, null, new Vector2(64, 72));
            _hangulField.ModeChanged += () => mode.Text = _hangulField.Korean ? "한" : "A";
            mode.TooltipText = "한글 / 영어";
            _nameTyping.AddChild(mode);
        }
        _nameTyping.AddChild(Ui.Button("확인", SubmitTyped, UiTheme.Green, 28, null, new Vector2(120, 72)));
        if (_recentNames.Length > 0)
            _nameTyping.AddChild(Ui.Button("뒤로", ShowPicks, null, 24, null, new Vector2(100, 72)));
        box.AddChild(_nameTyping);
        return box;
    }

    private void ShowPicks()
    {
        _nameEdit?.CancelIme();
        _nameEdit?.ReleaseFocus();
        _hangulField?.ReleaseFocus();
        _nameTyping.Visible = false;
        _namePicks.Visible = true;
        _namePrompt.Text = $"{_rank + 1}등이에요! 누구예요?";
    }

    private void ShowTyping()
    {
        _namePicks.Visible = false;
        _nameTyping.Visible = true;
        _namePrompt.Text = $"{_rank + 1}등이에요! 이름을 적어 주세요";
        if (_hangulField is not null)
        {
            _hangulField.Clear();
            Callable.From(() => _hangulField.GrabFocus()).CallDeferred();
            return;
        }
        _nameEdit!.Text = "";
        // Focus after layout so the IME window is placed at the caret
        Callable.From(() =>
        {
            _nameEdit.GrabFocus();
            _nameEdit.CaretColumn = _nameEdit.Text.Length;
        }).CallDeferred();
    }

    private async void RunSequence()
    {
        // 1. Count the number up
        var target = _spec.Value;
        var duration = Mathf.Clamp(target * 0.05f, 0.3f, 1.2f);
        var steps = Mathf.Clamp((int)target, 1, 24);
        await Wait(0.35f);
        for (var i = 1; i <= steps; i++)
        {
            _value.Text = _spec.FormatValue(target * i / steps);
            LightStars(Mathf.RoundToInt((float)_spec.StarsEarned * i / steps));
            Audio.I.CountStep(1f + 0.5f * i / steps);
            await Wait(duration / steps);
        }
        _value.Text = _spec.FormatValue(target);
        LightStars(_spec.StarsEarned);
        Ui.Punch(_value, 0.3f);
        if (_detail is not null)
        {
            await Wait(0.15f);
            _detail.Modulate = Colors.White;
            Ui.PopIn(_detail, 0f, 0.3f);
            Audio.I.CountStep(1.6f);
        }

        // 2. Best record celebration
        var board = App.I.Board(_spec.Game.Id, _spec.VariantId);
        var rank = _spec.Recordable ? board.RankOf(_spec.Entry) : -1;
        var isBest = _spec.Recordable && board.IsNewBest(_spec.Entry);
        await Wait(0.25f);
        if (isBest)
        {
            _best.Visible = false;
            _badge.Modulate = Colors.White;
            Ui.PopIn(_badge, 0f, 0.5f);
            _badge.CreateTween().SetLoops().TweenProperty(_badge, "rotation_degrees", 3f, 0.4f).From(-3f)
                .SetTrans(Tween.TransitionType.Sine);
            Audio.I.Fanfare();
            App.Vibrate(60);
            _confettiHost.AddChild(Confetti.Create(GetViewportRect().Size, 90));
            await Wait(0.6f);
        }

        // 3. Name entry if it made the list
        if (rank >= 0)
        {
            if (!isBest)
            {
                Audio.I.Cheer();
                _confettiHost.AddChild(Confetti.Create(GetViewportRect().Size, 35));
            }
            _rank = rank;
            _nameBox.Visible = true;
            if (_recentNames.Length > 0)
                ShowPicks();
            else
                ShowTyping();
            Ui.PopIn(_nameBox);
            if (App.I.Autoplay)
            {
                await Wait(0.8f);
                SubmitName(_recentNames.Length > 0 ? _recentNames[0] : "");
            }
        }
        else
        {
            _message.Text = _spec.Recordable ? "다음엔 10등 안에 들어 보자!" : _spec.NotRecordableMessage;
            ShowButtons();
        }
    }

    /// <summary>
    /// Submits the typed name. With the Korean IME the syllable being composed is not part of
    /// <see cref="LineEdit.Text"/> yet (it was dropped before, so "다온" was saved as "다"). Waiting a frame lets
    /// the IME's commit (Enter) arrive as a character first; <see cref="LineEdit.ApplyIme"/> then commits whatever
    /// is still composing (e.g. when 확인 is clicked mid-syllable).
    /// </summary>
    private async void SubmitTyped()
    {
        if (_submitted || _typedSubmitPending)
            return;
        if (_hangulField is not null)
        {
            SubmitName(_hangulField.Text);
            return;
        }
        _typedSubmitPending = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _typedSubmitPending = false;
        if (_nameEdit!.HasImeText())
            _nameEdit.ApplyIme();
        SubmitName(_nameEdit.Text);
    }

    private void SubmitName(string typed)
    {
        if (_submitted)
            return;
        _submitted = true;
        _nameEdit?.CancelIme();
        _nameEdit?.ReleaseFocus();
        _hangulField?.ReleaseFocus();
        var name = App.I.RememberName(typed);

        var rank = App.I.SubmitRecord(_spec.Game.Id, _spec.VariantId, _spec.Entry with { Name = name });
        _nameBox.Visible = false;
        _message.Text = $"{name}, 정말 잘했어요!";
        Ui.PopIn(_message);
        _ranking.Refresh(rank);
        ShowButtons();
    }

    private void ShowButtons()
    {
        _buttons.Visible = true;
        Ui.PopIn(_buttons, 0.1f);
    }

    private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
