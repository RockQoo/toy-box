using System;
using System.Collections.Generic;
using Godot;

namespace ToyBox.Common;

/// <summary>Factory helpers for the code-built UI. Keeps buttons, labels and panels looking and reacting the same everywhere.</summary>
public static class Ui
{
    private static readonly Dictionary<string, Texture2D> TextureCache = new();

    public static Texture2D Tex(string path)
    {
        if (!TextureCache.TryGetValue(path, out var tex))
        {
            tex = GD.Load<Texture2D>(path);
            TextureCache[path] = tex;
        }
        return tex;
    }

    public static Label Label(string text, int size, Color? color = null, int outline = 0, Color? outlineColor = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        if (outline > 0)
        {
            label.AddThemeConstantOverride("outline_size", outline);
            label.AddThemeColorOverride("font_outline_color", outlineColor ?? UiTheme.Ink);
        }
        return label;
    }

    /// <summary>
    /// A big, rounded button. Squashes on press and springs back on release, with a click sound.
    /// <paramref name="fill"/> null keeps the theme's paper look.
    /// </summary>
    public static Button Button(string text, Action onPressed, Color? fill = null, int fontSize = 30,
        Texture2D? icon = null, Vector2? minSize = null)
    {
        var button = new Button
        {
            Text = text,
            Icon = icon,
            CustomMinimumSize = minSize ?? new Vector2(0, 76),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeConstantOverride("icon_max_width", (int)(fontSize * 1.6f));
        if (fill is { } f)
            Paint(button, f);
        else
            Paint(button, UiTheme.Paper, UiTheme.Ink);
        MakeSquishy(button);
        button.Pressed += () =>
        {
            Audio.I.Click();
            onPressed();
        };
        return button;
    }

    /// <summary>Colors a button: a fill color with white text and outline, or a light fill with dark text.</summary>
    public static void Paint(Button button, Color fill, Color? text = null)
    {
        var fontColor = text ?? Colors.White;
        button.AddThemeStyleboxOverride("normal", UiTheme.RaisedBox(fill, UiTheme.Ink, 4));
        button.AddThemeStyleboxOverride("hover", UiTheme.RaisedBox(fill, UiTheme.Ink, 4));
        button.AddThemeStyleboxOverride("pressed", UiTheme.Box(fill.Darkened(0.12f), UiTheme.Ink, 4));
        button.AddThemeStyleboxOverride("disabled", UiTheme.Box(fill.Lerp(UiTheme.PaperDim, 0.6f), UiTheme.InkSoft, 4));
        foreach (var key in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(key, fontColor);
        if (text is null)
        {
            button.AddThemeConstantOverride("outline_size", 8);
            button.AddThemeColorOverride("font_outline_color", UiTheme.Ink);
        }
    }

    /// <summary>Press squash and release spring on any control. Pivot follows the control's center.</summary>
    public static void MakeSquishy(BaseButton button)
    {
        button.Resized += () => button.PivotOffset = button.Size / 2;
        Tween? tween = null;
        button.ButtonDown += () =>
        {
            tween?.Kill();
            tween = button.CreateTween();
            tween.TweenProperty(button, "scale", new Vector2(0.92f, 0.92f), 0.06f).SetEase(Tween.EaseType.Out);
        };
        button.ButtonUp += () =>
        {
            tween?.Kill();
            tween = button.CreateTween();
            tween.TweenProperty(button, "scale", Vector2.One, 0.35f)
                .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        };
    }

    public static PanelContainer Panel(Color? fill = null, int margin = 24, int border = 5)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.RaisedBox(fill ?? UiTheme.Paper, UiTheme.Ink, border, margin));
        return panel;
    }

    public static TextureRect Icon(string path, float size)
    {
        return new TextureRect
        {
            Texture = Tex(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    public static Control Spacer(float height = 0, float width = 0) =>
        new() { CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Full-screen dark backdrop that eats input behind a popup.</summary>
    public static ColorRect Backdrop()
    {
        var dim = new ColorRect { Color = UiTheme.Dim, MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return dim;
    }

    /// <summary>Wraps a control so it sits centered in the full screen.</summary>
    public static CenterContainer Centered(Control child)
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(child);
        return center;
    }

    /// <summary>Pop-in: scales from small with a slight overshoot. Call after the control is in the tree.</summary>
    public static Tween PopIn(Control control, float delay = 0f, float duration = 0.35f)
    {
        control.PivotOffset = control.Size / 2;
        control.Scale = new Vector2(0.6f, 0.6f);
        control.Modulate = control.Modulate with { A = 0 };
        var tween = control.CreateTween().SetParallel();
        tween.TweenProperty(control, "scale", Vector2.One, duration)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out).SetDelay(delay);
        tween.TweenProperty(control, "modulate:a", 1f, duration * 0.5f).SetDelay(delay);
        return tween;
    }

    /// <summary>Quick scale punch, e.g. when a score number changes.</summary>
    public static void Punch(Control control, float amount = 0.25f)
    {
        control.PivotOffset = control.Size / 2;
        control.Scale = new Vector2(1 + amount, 1 + amount);
        control.CreateTween().TweenProperty(control, "scale", Vector2.One, 0.25f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Side-to-side wiggle, e.g. for a locked item ("not yet").</summary>
    public static void Wiggle(Control control)
    {
        control.PivotOffset = control.Size / 2;
        var tween = control.CreateTween();
        foreach (var angle in new[] { -7f, 6f, -4f, 2f, 0f })
            tween.TweenProperty(control, "rotation_degrees", angle, 0.06f);
    }
}
