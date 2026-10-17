using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>What a game hands to the result screen.</summary>
public sealed class ResultSpec
{
    public required GameInfo Game { get; init; }
    public string Title { get; init; } = "게임 끝!";
    /// <summary>Caption above the big number, e.g. "터뜨린 풍선".</summary>
    public required string Caption { get; init; }
    /// <summary>The big number. Counted up from 0 when shown.</summary>
    public required float Value { get; init; }
    public required Func<float, string> FormatValue { get; init; }
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
/// </summary>
public partial class ResultPanel : Control
{
    private const int MaxNameLength = 8;
    private const string DefaultName = "친구";

    private ResultSpec _spec = null!;
    private Label _value = null!;
    private Label _best = null!;
    private Label _badge = null!;
    private Label _message = null!;
    private VBoxContainer _nameBox = null!;
    private LineEdit _nameEdit = null!;
    private HBoxContainer _buttons = null!;
    private RankingList _ranking = null!;
    private Control _confettiHost = null!;
    private bool _submitted;

    public static ResultPanel Create(ResultSpec spec)
    {
        var panel = new ResultPanel { _spec = spec };
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        return panel;
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

        _value = Ui.Label(_spec.FormatValue(0), 84, UiTheme.Blue, 14, Colors.White);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(_value);

        _badge = Ui.Label("최고 기록!", 40, UiTheme.Gold, 12, UiTheme.Ink);
        _badge.HorizontalAlignment = HorizontalAlignment.Center;
        _badge.Modulate = Colors.Transparent;
        left.AddChild(_badge);

        var board = App.I.Board(_spec.Game.Id);
        _best = Ui.Label(board.Best is { } best ? $"최고 기록  {_spec.Game.FormatValue(best)}" : "첫 기록에 도전!", 26, UiTheme.InkSoft);
        _best.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(_best);

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
        var header = Ui.Label($"{_spec.Game.Title} 순위", 28, UiTheme.Ink);
        header.HorizontalAlignment = HorizontalAlignment.Center;
        right.AddChild(header);
        _ranking = RankingList.Create(_spec.Game);
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

    private VBoxContainer BuildNameEntry()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var prompt = Ui.Label("이름을 적어 주세요!", 28, UiTheme.Ink);
        prompt.Name = "Prompt";
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(prompt);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        _nameEdit = new LineEdit
        {
            Text = App.I.LastName,
            PlaceholderText = DefaultName,
            MaxLength = MaxNameLength,
            CustomMinimumSize = new Vector2(280, 72),
            Alignment = HorizontalAlignment.Center,
            SelectAllOnFocus = true,
        };
        _nameEdit.AddThemeFontSizeOverride("font_size", 32);
        _nameEdit.TextSubmitted += _ => SubmitName();
        row.AddChild(_nameEdit);
        row.AddChild(Ui.Button("확인", SubmitName, UiTheme.Green, 28, null, new Vector2(120, 72)));
        box.AddChild(row);
        return box;
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
            Audio.I.CountStep(1f + 0.5f * i / steps);
            await Wait(duration / steps);
        }
        _value.Text = _spec.FormatValue(target);
        Ui.Punch(_value, 0.3f);

        // 2. Best record celebration
        var board = App.I.Board(_spec.Game.Id);
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
            _nameBox.GetNode<Label>("Prompt").Text = $"{rank + 1}등이에요! 이름을 적어 주세요";
            _nameBox.Visible = true;
            Ui.PopIn(_nameBox);
            _nameEdit.GrabFocus();
            if (App.I.Autoplay)
            {
                await Wait(0.8f);
                SubmitName();
            }
        }
        else
        {
            _message.Text = _spec.Recordable ? "다음엔 10등 안에 들어 보자!" : _spec.NotRecordableMessage;
            ShowButtons();
        }
    }

    private void SubmitName()
    {
        if (_submitted)
            return;
        _submitted = true;
        var name = _nameEdit.Text.Trim();
        if (name.Length == 0)
            name = DefaultName;
        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];
        App.I.LastName = name;
        _nameEdit.ReleaseFocus();

        var rank = App.I.SubmitRecord(_spec.Game.Id, _spec.Entry with { Name = name });
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
