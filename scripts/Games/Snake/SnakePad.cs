using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Snake;

/// <summary>
/// The big direction buttons in the GDD's layout (▲ over ◀ ▼ ▶), using the round arrow pictures (ui/btn_*.png).
/// A press turns the caterpillar at once; holding does nothing more (it keeps crawling by itself).
/// </summary>
public partial class SnakePad : GridContainer
{
    public event Action<MazeDir>? Pressed;

    private readonly Dictionary<MazeDir, TextureButton> _buttons = new();

    public static SnakePad Create(float buttonSize)
    {
        var pad = new SnakePad { Columns = 3, MouseFilter = MouseFilterEnum.Ignore };
        pad.AddThemeConstantOverride("h_separation", 10);
        pad.AddThemeConstantOverride("v_separation", 8);
        var size = new Vector2(buttonSize, buttonSize);
        pad.AddChild(Ui.Spacer(buttonSize, buttonSize));
        pad.AddChild(pad.Arrow(MazeDir.Up, "btn_up", size));
        pad.AddChild(Ui.Spacer(buttonSize, buttonSize));
        pad.AddChild(pad.Arrow(MazeDir.Left, "btn_left", size));
        pad.AddChild(pad.Arrow(MazeDir.Down, "btn_down", size));
        pad.AddChild(pad.Arrow(MazeDir.Right, "btn_right", size));
        return pad;
    }

    private TextureButton Arrow(MazeDir dir, string picture, Vector2 size)
    {
        var button = new TextureButton
        {
            TextureNormal = Ui.Tex($"res://assets/sprites/ui/{picture}.png"),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = size,
            FocusMode = FocusModeEnum.None,
        };
        Ui.MakeSquishy(button);
        button.ButtonDown += () => Pressed?.Invoke(dir);
        _buttons[dir] = button;
        return button;
    }

    /// <summary>Shows which button a key, a swipe or autoplay just used, so they feel like one control.</summary>
    public void Flash(MazeDir dir)
    {
        if (!_buttons.TryGetValue(dir, out var button))
            return;
        button.PivotOffset = button.Size / 2;
        button.Scale = new Vector2(0.88f, 0.88f);
        button.CreateTween().TweenProperty(button, "scale", Vector2.One, 0.25f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
