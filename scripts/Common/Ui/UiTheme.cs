using Godot;

namespace ToyBox.Common;

/// <summary>Shared colors, font and the app-wide Theme. Every screen should take its colors and sizes from here.</summary>
public static class UiTheme
{
    public static readonly Color Ink = new(0.20f, 0.22f, 0.36f);
    public static readonly Color InkSoft = new(0.42f, 0.45f, 0.58f);
    public static readonly Color Paper = new(1f, 0.99f, 0.96f);
    public static readonly Color PaperDim = new(0.94f, 0.92f, 0.88f);
    public static readonly Color Blue = new(0.24f, 0.56f, 0.98f);
    public static readonly Color Green = new(0.30f, 0.76f, 0.36f);
    public static readonly Color Orange = new(1f, 0.58f, 0.18f);
    public static readonly Color Pink = new(1f, 0.42f, 0.62f);
    public static readonly Color Yellow = new(1f, 0.82f, 0.20f);
    public static readonly Color Purple = new(0.62f, 0.40f, 0.92f);
    public static readonly Color Gold = new(1f, 0.78f, 0.15f);
    public static readonly Color Silver = new(0.72f, 0.76f, 0.82f);
    public static readonly Color Bronze = new(0.86f, 0.56f, 0.32f);
    public static readonly Color Dim = new(0.10f, 0.12f, 0.22f, 0.55f);

    public const int CornerRadius = 22;

    /// <summary>System font with Korean glyphs (Malgun Gothic on Windows, Noto Sans CJK on Android), bold.</summary>
    public static SystemFont Font { get; } = new()
    {
        FontNames = new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Noto Sans KR", "Apple SD Gothic Neo", "sans-serif" },
        FontWeight = 700,
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    public static Theme Build()
    {
        var theme = new Theme
        {
            DefaultFont = Font,
            DefaultFontSize = 28,
        };

        theme.SetColor("font_color", "Label", Ink);

        foreach (var type in new[] { "Button" })
        {
            theme.SetStylebox("normal", type, Box(Paper, Ink, 4));
            theme.SetStylebox("hover", type, Box(Paper, Ink, 4));
            theme.SetStylebox("pressed", type, Box(PaperDim, Ink, 4));
            theme.SetStylebox("disabled", type, Box(PaperDim with { A = 0.7f }, InkSoft, 4));
            theme.SetStylebox("focus", type, new StyleBoxEmpty());
            theme.SetColor("font_color", type, Ink);
            theme.SetColor("font_hover_color", type, Ink);
            theme.SetColor("font_pressed_color", type, Ink);
            theme.SetColor("font_focus_color", type, Ink);
            theme.SetColor("font_disabled_color", type, InkSoft);
            theme.SetConstant("h_separation", type, 12);
        }

        theme.SetStylebox("panel", "PanelContainer", Box(Paper, Ink, 5));

        theme.SetStylebox("normal", "LineEdit", Box(Colors.White, Ink, 4, 14));
        theme.SetStylebox("focus", "LineEdit", Box(Colors.White, Blue, 5, 14));
        theme.SetColor("font_color", "LineEdit", Ink);
        theme.SetColor("caret_color", "LineEdit", Blue);
        theme.SetColor("font_placeholder_color", "LineEdit", InkSoft);
        theme.SetConstant("caret_width", "LineEdit", 3);
        theme.SetColor("selection_color", "LineEdit", Blue with { A = 0.25f });
        theme.SetColor("font_selected_color", "LineEdit", Ink);

        var track = Box(PaperDim, Ink, 3, 0);
        track.ContentMarginTop = track.ContentMarginBottom = 8;
        theme.SetStylebox("slider", "HSlider", track);
        var fill = Box(Blue, Ink, 3, 0);
        fill.ContentMarginTop = fill.ContentMarginBottom = 8;
        theme.SetStylebox("grabber_area", "HSlider", fill);
        theme.SetStylebox("grabber_area_highlight", "HSlider", fill);
        var grabber = GrabberIcon();
        theme.SetIcon("grabber", "HSlider", grabber);
        theme.SetIcon("grabber_highlight", "HSlider", grabber);

        return theme;
    }

    /// <summary>A rounded, outlined box: the one panel/button look used across the app.</summary>
    public static StyleBoxFlat Box(Color fill, Color border, int borderWidth, int margin = 16, int radius = CornerRadius)
    {
        var box = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            ShadowColor = new Color(0, 0, 0, 0.18f),
            ShadowSize = 0,
            ShadowOffset = new Vector2(0, 5),
            AntiAliasing = true,
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }

    /// <summary>Same as <see cref="Box"/> with a drop shadow, for buttons and floating panels.</summary>
    public static StyleBoxFlat RaisedBox(Color fill, Color border, int borderWidth, int margin = 16, int radius = CornerRadius)
    {
        var box = Box(fill, border, borderWidth, margin, radius);
        box.ShadowSize = 1;
        return box;
    }

    private static ImageTexture GrabberIcon()
    {
        const int size = 44;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var c = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = new Vector2(x, y).DistanceTo(c);
            var color = d < 16 ? Colors.White : d < 20 ? Ink : new Color(0, 0, 0, 0);
            if (d >= 20 && d < 21) color = Ink with { A = 21 - d };
            image.SetPixel(x, y, color);
        }
        return ImageTexture.CreateFromImage(image);
    }
}
