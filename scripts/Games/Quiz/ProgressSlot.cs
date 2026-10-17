using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Quiz;

/// <summary>One dot of the 10-question progress row: to do, current (pulsing), star (first try) or done (after a retry).</summary>
public partial class ProgressSlot : Control
{
    public enum Mark { Todo, Current, Star, Done }

    private Mark _mark;
    private float _time;
    private Texture2D _star = null!;

    public Mark State
    {
        get => _mark;
        set
        {
            _mark = value;
            Scale = Vector2.One;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        _star = Ui.Tex("res://assets/sprites/ui/star.png");
        PivotOffset = CustomMinimumSize / 2;
    }

    public override void _Process(double delta)
    {
        if (_mark != Mark.Current)
            return;
        _time += (float)delta;
        var s = 1f + 0.1f * Mathf.Sin(_time * 6f);
        Scale = new Vector2(s, s);
    }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = Mathf.Min(Size.X, Size.Y) / 2 - 3;
        switch (_mark)
        {
            case Mark.Todo:
                DrawCircle(c, r * 0.55f, UiTheme.PaperDim);
                break;
            case Mark.Current:
                DrawCircle(c, r * 0.75f, UiTheme.Orange);
                DrawCircle(c, r * 0.5f, UiTheme.Yellow.Lerp(Colors.White, 0.4f));
                break;
            case Mark.Star:
                DrawTextureRect(_star, new Rect2(Vector2.Zero, Size), false);
                break;
            case Mark.Done:
                DrawCircle(c, r * 0.62f, UiTheme.Blue.Lerp(Colors.White, 0.35f));
                DrawCircle(c, r * 0.3f, Colors.White);
                break;
        }
    }
}
