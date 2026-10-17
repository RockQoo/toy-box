using System;
using System.Collections.Generic;
using Godot;

namespace ToyBox.Common;

/// <summary>One choice for a <see cref="ChoiceRow"/>: a picture and/or a word.</summary>
public sealed record Choice(string Text, string? IconPath = null, Color? Swatch = null);

/// <summary>
/// A labeled row of big chip buttons where exactly one is selected (the customization screens: balloon color,
/// background, character, speed). The selected chip is raised and outlined in orange.
/// </summary>
public partial class ChoiceRow : BoxContainer
{
    public event Action<int>? Selected;

    private readonly List<Button> _chips = new();
    private int _selected = -1;

    public int SelectedIndex => _selected;

    /// <param name="vertical">Stacks the chips top to bottom (used as tabs); each chip then shows its icon left of the text.</param>
    public static ChoiceRow Create(string caption, IReadOnlyList<Choice> choices, int selected, float chipWidth = 120,
        bool vertical = false)
    {
        var row = new ChoiceRow { Vertical = vertical };
        row.AddThemeConstantOverride("separation", 12);

        if (caption.Length > 0)
        {
            var label = Ui.Label(caption, 28, UiTheme.Ink);
            label.CustomMinimumSize = new Vector2(150, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(label);
        }

        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var chip = Chip(choices[i], chipWidth, vertical);
            chip.Pressed += () =>
            {
                Audio.I.Click();
                row.Select(index, notify: true);
            };
            row._chips.Add(chip);
            row.AddChild(chip);
        }
        row.Select(Math.Clamp(selected, 0, choices.Count - 1), notify: false);
        return row;
    }

    public void Select(int index, bool notify)
    {
        if (index == _selected)
            return;
        _selected = index;
        for (var i = 0; i < _chips.Count; i++)
        {
            var on = i == index;
            var chip = _chips[i];
            chip.AddThemeStyleboxOverride("normal", ChipBox(on));
            chip.AddThemeStyleboxOverride("hover", ChipBox(on));
            chip.AddThemeStyleboxOverride("pressed", ChipBox(on));
            if (on)
                Ui.Punch(chip, 0.12f);
        }
        if (notify)
            Selected?.Invoke(index);
    }

    private static Button Chip(Choice choice, float width, bool wide)
    {
        var chip = new Button { CustomMinimumSize = new Vector2(width, wide ? 66 : 96), FocusMode = FocusModeEnum.None };
        Ui.MakeSquishy(chip);

        var box = new BoxContainer
        {
            Vertical = !wide,
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = wide ? AlignmentMode.Begin : AlignmentMode.Center,
        };
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", wide ? 10 : 0);
        if (wide)
            box.OffsetLeft = 14;
        chip.AddChild(box);

        if (choice.IconPath is not null)
        {
            var icon = Ui.Icon(choice.IconPath, wide ? 46 : 52);
            box.AddChild(icon);
        }
        else if (choice.Swatch is { } swatch)
        {
            var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            var dot = new Panel { CustomMinimumSize = new Vector2(64, 44), MouseFilter = MouseFilterEnum.Ignore };
            dot.AddThemeStyleboxOverride("panel", UiTheme.Box(swatch, UiTheme.Ink, 3, 0, 12));
            center.AddChild(dot);
            box.AddChild(center);
        }

        var text = Ui.Label(choice.Text, choice.IconPath is null && choice.Swatch is null ? 28 : wide ? 24 : 20, UiTheme.Ink);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        box.AddChild(text);
        return chip;
    }

    private static StyleBoxFlat ChipBox(bool on) => on
        ? UiTheme.RaisedBox(UiTheme.Yellow.Lerp(Colors.White, 0.55f), UiTheme.Orange, 6, 6, 18)
        : UiTheme.Box(Colors.White, UiTheme.PaperDim, 3, 6, 18);
}
