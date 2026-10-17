using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Quiz;

/// <summary>
/// The big example next to the question text, so a child who cannot read yet still sees what to find:
/// a blob of paint for a color, a large digit for a number (deliberately not the answer card itself).
/// </summary>
public partial class QuizPicture : Control
{
    private QuizKind _kind;
    private int _value;

    public void Show(QuizKind kind, int value)
    {
        _kind = kind;
        _value = value;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size / 2;
        var r = Mathf.Min(Size.X, Size.Y) / 2 - 6;
        if (_kind == QuizKind.Color)
        {
            var color = QuizCard.ShapeColors[_value];
            DrawCircle(center + new Vector2(0, 4), r, new Color(0, 0, 0, 0.15f));
            DrawCircle(center, r, UiTheme.Ink);
            DrawCircle(center, r - 5, color);
            DrawCircle(center + new Vector2(-r * 0.35f, -r * 0.38f), r * 0.2f, new Color(1, 1, 1, 0.55f));
            return;
        }
        var font = UiTheme.Font;
        var text = _value.ToString();
        var fontSize = (int)(Size.Y * 1.05f);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X;
        var pos = new Vector2(center.X - width / 2, center.Y + (font.GetAscent(fontSize) - font.GetDescent(fontSize)) / 2f);
        DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, fontSize, 18, Colors.White);
        DrawString(font, pos, text, HorizontalAlignment.Left, -1, fontSize, UiTheme.Ink);
    }
}
