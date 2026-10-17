using System;
using System.Collections.Generic;
using Godot;

namespace ToyBox.Common;

/// <summary>
/// One choice for a <see cref="ChoiceRow"/>: a picture and/or a word. <paramref name="IconMaterial"/> draws the picture
/// with a shader (e.g. a recolored basket).
/// </summary>
public sealed record Choice(string Text, string? IconPath = null, Color? Swatch = null, Material? IconMaterial = null)
{
    /// <summary>The "속도" row shared by the timed games, in <see cref="Core.StartSpeed"/> order.</summary>
    public static readonly IReadOnlyList<Choice> Speeds = new[]
    {
        new Choice("느림", "res://assets/sprites/animal/turtle.png"),
        new Choice("보통", "res://assets/sprites/animal/penguin.png"),
        new Choice("빠름", "res://assets/sprites/animal/rabbit.png"),
    };
}

/// <summary>
/// A labeled row of big chip buttons (the customization screens: balloon color, background, character, speed).
/// Single mode: exactly one is selected (raised and outlined in orange). Multi mode (<see cref="CreateMulti"/>):
/// each chip toggles, off chips are faded, and the last chip that is on cannot be turned off (it wiggles instead).
/// </summary>
public partial class ChoiceRow : BoxContainer
{
    public const float DefaultChipHeight = 96f;

    /// <summary>Single mode: the newly selected index.</summary>
    public event Action<int>? Selected;

    /// <summary>Multi mode: a chip was turned on or off (index, on).</summary>
    public event Action<int, bool>? Toggled;

    private readonly List<Button> _chips = new();
    private readonly List<Control?> _icons = new();
    private int _selected = -1;
    private bool _multi;
    private bool[] _on = Array.Empty<bool>();

    public int SelectedIndex => _selected;

    /// <param name="vertical">Stacks the chips top to bottom (used as tabs); each chip then shows its icon left of the text.</param>
    public static ChoiceRow Create(string caption, IReadOnlyList<Choice> choices, int selected, float chipWidth = 120,
        bool vertical = false, float chipHeight = DefaultChipHeight)
    {
        var row = Build(caption, choices, chipWidth, vertical, chipHeight);
        for (var i = 0; i < row._chips.Count; i++)
        {
            var index = i;
            row._chips[i].Pressed += () =>
            {
                Audio.I.Click();
                row.Select(index, notify: true);
            };
        }
        row.Select(Math.Clamp(selected, 0, choices.Count - 1), notify: false);
        return row;
    }

    /// <summary>A row where any number of chips can be on, but at least one always is.</summary>
    public static ChoiceRow CreateMulti(string caption, IReadOnlyList<Choice> choices, Func<int, bool> isOn,
        float chipWidth = 120, float chipHeight = DefaultChipHeight)
    {
        var row = Build(caption, choices, chipWidth, false, chipHeight);
        row._multi = true;
        row._on = new bool[choices.Count];
        for (var i = 0; i < row._chips.Count; i++)
        {
            var index = i;
            row._on[i] = isOn(i);
            row.Paint(i, row._on[i]);
            row._chips[i].Pressed += () => row.Toggle(index);
        }
        return row;
    }

    private static ChoiceRow Build(string caption, IReadOnlyList<Choice> choices, float chipWidth, bool vertical,
        float chipHeight)
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

        foreach (var choice in choices)
        {
            var chip = Chip(choice, chipWidth, vertical, chipHeight, out var icon);
            row._chips.Add(chip);
            row._icons.Add(icon);
            row.AddChild(chip);
        }
        return row;
    }

    public void Select(int index, bool notify)
    {
        if (index == _selected)
            return;
        _selected = index;
        for (var i = 0; i < _chips.Count; i++)
        {
            Paint(i, i == index);
            if (i == index)
                Ui.Punch(_chips[i], 0.12f);
        }
        if (notify)
            Selected?.Invoke(index);
    }

    private void Toggle(int index)
    {
        var on = !_on[index];
        if (!on && Array.FindAll(_on, x => x).Length <= 1)
        {
            // The last fruit (etc.) stays: a soft "not this one" instead of an empty game
            Audio.I.Bonk();
            Ui.Wiggle(_chips[index]);
            return;
        }
        Audio.I.Click();
        _on[index] = on;
        Paint(index, on);
        Ui.Punch(_chips[index], on ? 0.12f : -0.06f);
        Toggled?.Invoke(index, on);
    }

    private void Paint(int index, bool on)
    {
        var chip = _chips[index];
        chip.AddThemeStyleboxOverride("normal", ChipBox(on));
        chip.AddThemeStyleboxOverride("hover", ChipBox(on));
        chip.AddThemeStyleboxOverride("pressed", ChipBox(on));
        if (_multi && _icons[index] is { } icon)
            icon.Modulate = on ? Colors.White : new Color(1, 1, 1, 0.35f);
    }

    private static Button Chip(Choice choice, float width, bool wide, float height, out Control? icon)
    {
        var chip = new Button { CustomMinimumSize = new Vector2(width, wide ? 66 : height), FocusMode = FocusModeEnum.None };
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

        icon = null;
        if (choice.IconPath is not null)
        {
            var picture = Ui.Icon(choice.IconPath, wide ? 46 : Mathf.Min(52, height - 34));
            picture.Material = choice.IconMaterial;
            box.AddChild(picture);
            icon = picture;
        }
        else if (choice.Swatch is { } swatch)
        {
            var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
            var dot = new Panel { CustomMinimumSize = new Vector2(64, Mathf.Min(44, height - 40)), MouseFilter = MouseFilterEnum.Ignore };
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
