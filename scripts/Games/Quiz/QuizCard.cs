using System;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Quiz;

/// <summary>
/// One answer card: a shape card (color questions) or a number card (number questions). Numbers 1~5 use the painted
/// cards; 6~9 have no art yet, so they are drawn in code in the same style (pastel card, colored frame, big outlined
/// digit). A wrong pick dims the card and makes it untappable; the answer is never revealed.
/// </summary>
public partial class QuizCard : Button
{
    private static readonly string[] ShapePaths =
    {
        "res://assets/sprites/quiz/shape_red_circle.png",
        "res://assets/sprites/quiz/shape_blue_square.png",
        "res://assets/sprites/quiz/shape_yellow_triangle.png",
        "res://assets/sprites/quiz/shape_green_star.png",
        "res://assets/sprites/quiz/shape_purple_heart.png",
        "res://assets/sprites/quiz/shape_orange_oval.png",
    };

    /// <summary>Shape colors, in <see cref="QuizRules.ColorNames"/> order.</summary>
    public static readonly Color[] ShapeColors =
    {
        new(0.95f, 0.22f, 0.30f), new(0.22f, 0.55f, 0.98f), new(1f, 0.80f, 0.15f),
        new(0.35f, 0.80f, 0.25f), new(0.62f, 0.36f, 0.92f), new(1f, 0.56f, 0.15f),
    };

    /// <summary>Card colors for 1~9 (1~5 match the painted cards).</summary>
    private static readonly Color[] NumberColors =
    {
        new(0.95f, 0.22f, 0.30f), new(0.22f, 0.55f, 0.98f), new(1f, 0.78f, 0.12f), new(0.35f, 0.80f, 0.25f),
        new(0.58f, 0.32f, 0.90f), new(1f, 0.52f, 0.12f), new(1f, 0.40f, 0.66f), new(0.10f, 0.72f, 0.74f),
        new(0.45f, 0.36f, 0.86f),
    };

    private const int PaintedNumbers = 5;

    public QuizKind Kind { get; private set; }
    public int Value { get; private set; }
    public Color Tint => Kind == QuizKind.Color ? ShapeColors[Value] : NumberColors[Value - 1];
    public bool Dimmed { get; private set; }

    private Texture2D? _texture;

    public static QuizCard Create(QuizKind kind, int value, Vector2 size)
    {
        var card = new QuizCard
        {
            Kind = kind,
            Value = value,
            Size = size,
            CustomMinimumSize = size,
            PivotOffset = size / 2,
            FocusMode = FocusModeEnum.None,
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
            card.AddThemeStyleboxOverride(state, empty);
        if (kind == QuizKind.Color)
            card._texture = Ui.Tex(ShapePaths[value]);
        else if (value <= PaintedNumbers)
            card._texture = Ui.Tex($"res://assets/sprites/quiz/num_{value}.png");
        Ui.MakeSquishy(card);
        return card;
    }

    /// <summary>Wrong pick: wiggle, fade back and stop taking taps.</summary>
    public void Dim()
    {
        Dimmed = true;
        Disabled = true;
        Ui.Wiggle(this);
        CreateTween().TweenProperty(this, "modulate", new Color(1, 1, 1, 0.35f), 0.35f).SetDelay(0.2f);
    }

    public override void _Draw()
    {
        var rect = FitRect(Size);
        if (_texture is not null)
            DrawTextureRect(_texture, rect, false);
        else
            DrawNumberCard(this, rect, Value, NumberColors[Value - 1]);
    }

    /// <summary>The card picture's rect inside the button, keeping the art's aspect (about 0.85).</summary>
    private Rect2 FitRect(Vector2 size)
    {
        var aspect = _texture is { } t ? t.GetWidth() / (float)t.GetHeight() : 0.87f;
        var w = Mathf.Min(size.X, size.Y * aspect);
        var h = w / aspect;
        return new Rect2((size - new Vector2(w, h)) / 2, new Vector2(w, h));
    }

    /// <summary>Draws a number card like the painted ones: dark rim, colored frame, pastel inside, big outlined digit.</summary>
    public static void DrawNumberCard(CanvasItem canvas, Rect2 rect, int value, Color color)
    {
        var radius = (int)(rect.Size.X * 0.13f);
        var rim = UiTheme.Box(color.Lerp(Colors.White, 0.82f), color.Darkened(0.55f), Mathf.Max(2, (int)(rect.Size.X * 0.02f)), 0, radius);
        canvas.DrawStyleBox(rim, rect);
        var inset = rect.Size.X * 0.035f;
        var frame = UiTheme.Box(Colors.Transparent, color, Mathf.Max(3, (int)(rect.Size.X * 0.05f)), 0, (int)(radius * 0.8f));
        canvas.DrawStyleBox(frame, rect.Grow(-inset));

        var font = UiTheme.Font;
        var text = value.ToString();
        var fontSize = (int)(rect.Size.Y * 0.78f);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X;
        var baseline = rect.GetCenter().Y + (font.GetAscent(fontSize) - font.GetDescent(fontSize)) / 2f;
        var pos = new Vector2(rect.GetCenter().X - width / 2, baseline);
        // Chunky like the painted digits: a wide dark rim, the body fattened by an outline of its own color, then a
        // lighter face nudged up-left for a bit of shine
        var rimSize = Mathf.Max(6, (int)(rect.Size.X * 0.2f));
        var bodySize = Mathf.Max(3, (int)(rect.Size.X * 0.09f));
        canvas.DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, fontSize, rimSize, color.Darkened(0.6f));
        canvas.DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, fontSize, bodySize, color);
        canvas.DrawString(font, pos, text, HorizontalAlignment.Left, -1, fontSize, color);
        canvas.DrawString(font, pos + new Vector2(-1, -2) * (rect.Size.X / 120f), text, HorizontalAlignment.Left, -1, fontSize,
            color.Lightened(0.25f));
    }
}
