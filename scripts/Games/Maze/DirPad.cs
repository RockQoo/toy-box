using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Maze;

/// <summary>
/// The big on-screen direction buttons in the GDD's layout (▲ over ◀ ▼ ▶). A button reports when it goes down and
/// up, so holding it walks on (the game repeats the move). The arrows are drawn in code.
/// </summary>
public partial class DirPad : GridContainer
{
    public event Action<MazeDir>? Down;
    public event Action<MazeDir>? Up;

    private readonly Dictionary<MazeDir, Button> _buttons = new();

    public static DirPad Create(float buttonSize)
    {
        var pad = new DirPad { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        pad.AddThemeConstantOverride("h_separation", 12);
        pad.AddThemeConstantOverride("v_separation", 12);
        var size = new Vector2(buttonSize, buttonSize);
        pad.AddChild(Ui.Spacer(buttonSize, buttonSize));
        pad.AddChild(pad.Arrow(MazeDir.Up, size));
        pad.AddChild(Ui.Spacer(buttonSize, buttonSize));
        pad.AddChild(pad.Arrow(MazeDir.Left, size));
        pad.AddChild(pad.Arrow(MazeDir.Down, size));
        pad.AddChild(pad.Arrow(MazeDir.Right, size));
        return pad;
    }

    private Button Arrow(MazeDir dir, Vector2 size)
    {
        var button = new Button { CustomMinimumSize = size, FocusMode = FocusModeEnum.None };
        Ui.Paint(button, UiTheme.Blue);
        Ui.MakeSquishy(button);
        var glyph = new DirArrow { Dir = dir, MouseFilter = MouseFilterEnum.Ignore };
        glyph.SetAnchorsPreset(LayoutPreset.FullRect);
        button.AddChild(glyph);
        button.ButtonDown += () => Down?.Invoke(dir);
        button.ButtonUp += () => Up?.Invoke(dir);
        _buttons[dir] = button;
        return button;
    }

    /// <summary>Shows which button a key (or autoplay) just used, so the pad and the keys feel like one control.</summary>
    public void Flash(MazeDir dir)
    {
        if (!_buttons.TryGetValue(dir, out var button))
            return;
        button.PivotOffset = button.Size / 2;
        button.Scale = new Vector2(0.9f, 0.9f);
        button.CreateTween().TweenProperty(button, "scale", Vector2.One, 0.25f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
