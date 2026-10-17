using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Flight;

/// <summary>
/// The bar at the top. During the flight it is a little road: the plane icon travels right toward the boss at the end.
/// When the boss comes it turns into the boss's mood bar: a gray storm part that shrinks as the boss calms down, with
/// the grumpy boss on the left and the smiling cloud on the right (instead of a red HP bar).
/// </summary>
public partial class FlightBar : Control
{
    private static readonly Color Track = new(1f, 1f, 1f, 0.9f);
    private static readonly Color Road = new(0.62f, 0.82f, 1f);
    private static readonly Color Storm = new(0.56f, 0.53f, 0.70f);
    private static readonly Color Clear = new(0.86f, 0.96f, 1f);

    private Texture2D _plane = null!;
    private Texture2D _boss = null!;
    private Texture2D _happy = null!;
    private float _shown = 1f;

    /// <summary>Flight progress 0..1 (road mode).</summary>
    public float Progress { get; set; }
    /// <summary>The boss's mood 0..1 (mood mode).</summary>
    public float Mood { get; set; } = 1f;
    public bool MoodMode { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _plane = Ui.Tex("res://assets/sprites/flight/plane_idle.png");
        _boss = Ui.Tex("res://assets/sprites/flight/boss_idle.png");
        _happy = Ui.Tex("res://assets/sprites/flight/enemy_happy.png");
    }

    public override void _Process(double delta)
    {
        // The mood part eases toward the real value so hits read as a smooth drain
        _shown = Mathf.Lerp(_shown, Mood, Mathf.Min(1f, (float)delta * 6f));
        QueueRedraw();
    }

    public void ResetMood()
    {
        Mood = 1f;
        _shown = 1f;
    }

    public override void _Draw()
    {
        var size = Size;
        var iconH = size.Y * 1.0f;
        var left = iconH * 1.1f;
        var right = size.X - iconH * 1.1f;
        var mid = size.Y / 2;
        var thick = size.Y * 0.42f;
        var outer = new Rect2(left - 6, mid - thick / 2 - 4, right - left + 12, thick + 8);
        DrawStyleBox(UiTheme.Box(Track, UiTheme.Ink, 3, 0, (int)(thick / 2 + 4)), outer);

        if (!MoodMode)
        {
            // Dotted road, filled up to the plane
            var x = Mathf.Lerp(left, right, Mathf.Clamp(Progress, 0f, 1f));
            DrawStyleBox(UiTheme.Box(Road, Road, 0, 0, (int)(thick / 2)), new Rect2(left, mid - thick / 2, Mathf.Max(thick, x - left), thick));
            for (var dx = x + thick; dx < right - thick / 2; dx += thick * 1.2f)
                DrawCircle(new Vector2(dx, mid), thick * 0.18f, UiTheme.InkSoft with { A = 0.5f });
            DrawIcon(_boss, new Vector2(size.X - iconH * 0.62f, mid), iconH * 0.95f, false);
            DrawIcon(_plane, new Vector2(x, mid - 2), iconH * 1.1f, false);
        }
        else
        {
            var x = Mathf.Lerp(left, right, Mathf.Clamp(_shown, 0f, 1f));
            DrawStyleBox(UiTheme.Box(Clear, Clear, 0, 0, (int)(thick / 2)), new Rect2(left, mid - thick / 2, right - left, thick));
            if (x > left + 1)
                DrawStyleBox(UiTheme.Box(Storm, Storm, 0, 0, (int)(thick / 2)), new Rect2(left, mid - thick / 2, Mathf.Max(thick, x - left), thick));
            // Where the boss's attack changes
            foreach (var f in new[] { 0.7f, 0.4f })
            {
                var mx = Mathf.Lerp(left, right, f);
                DrawLine(new Vector2(mx, mid - thick / 2 + 2), new Vector2(mx, mid + thick / 2 - 2), Colors.White with { A = 0.8f }, 2);
            }
            DrawIcon(_boss, new Vector2(iconH * 0.62f, mid), iconH * 0.95f, false);
            DrawIcon(_happy, new Vector2(size.X - iconH * 0.62f, mid), iconH * 0.9f, false);
        }
    }

    private void DrawIcon(Texture2D tex, Vector2 center, float height, bool flip)
    {
        var size = tex.GetSize();
        var w = height * size.X / size.Y;
        DrawTextureRect(tex, new Rect2(center.X - w / 2, center.Y - height / 2, flip ? -w : w, height), false);
    }
}
