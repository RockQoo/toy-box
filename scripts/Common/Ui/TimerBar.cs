using Godot;

namespace ToyBox.Common;

/// <summary>
/// Remaining time as a shrinking bar, for children who cannot read "남은 시간 18초" yet. Shared by the timed games
/// (balloon, fruit). Set <see cref="Total"/> once and <see cref="Remaining"/> every frame; the bar turns orange
/// with a small pulse for the last <see cref="WarnSeconds"/> (orange, never red: excitement, not alarm).
/// </summary>
public partial class TimerBar : Control
{
    private const float Border = 3f;
    private const float Inset = 3f; // Gap between the track outline and the fill

    private float _remaining;
    private bool _warning;
    private Color _fill;
    private Tween? _tween;
    private readonly StyleBoxFlat _trackBox = UiTheme.Box(UiTheme.PaperDim, UiTheme.Ink, (int)Border, 0);
    private readonly StyleBoxFlat _fillBox = UiTheme.Box(Colors.White, Colors.White, 0, 0);
    private readonly StyleBoxFlat _shineBox = UiTheme.Box(Colors.White with { A = 0.35f }, Colors.Transparent, 0, 0);

    public float Total { get; set; } = 30f;
    public float WarnSeconds { get; set; } = 5f;
    public Color NormalColor { get; set; } = UiTheme.Green;
    public Color WarnColor { get; set; } = UiTheme.Orange;

    public float Remaining
    {
        get => _remaining;
        set
        {
            _remaining = Mathf.Clamp(value, 0f, Total);
            var warning = _remaining > 0f && _remaining <= WarnSeconds;
            if (warning != _warning)
                SetWarning(warning);
            QueueRedraw();
        }
    }

    public static TimerBar Create(float totalSeconds, Vector2? minSize = null)
    {
        var bar = new TimerBar
        {
            Total = totalSeconds,
            CustomMinimumSize = minSize ?? new Vector2(260, 26),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar._fill = bar.NormalColor;
        bar._remaining = totalSeconds;
        return bar;
    }

    /// <summary>Full bar in the normal color, e.g. when a new round starts.</summary>
    public void Reset()
    {
        _tween?.Kill();
        _warning = false;
        _fill = NormalColor;
        Scale = Vector2.One;
        Remaining = Total;
    }

    private void SetWarning(bool on)
    {
        _warning = on;
        _tween?.Kill();
        if (!on)
        {
            _fill = NormalColor;
            Scale = Vector2.One;
            return;
        }
        // Blend to orange and give one bounce
        PivotOffset = Size / 2;
        _tween = CreateTween().SetParallel();
        _tween.TweenMethod(Callable.From<Color>(c =>
        {
            _fill = c;
            QueueRedraw();
        }), NormalColor, WarnColor, 0.3f);
        _tween.TweenProperty(this, "scale", Vector2.One, 0.4f).From(new Vector2(1.08f, 1.3f))
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        var radius = (int)(Size.Y / 2);
        _trackBox.SetCornerRadiusAll(radius);
        DrawStyleBox(_trackBox, rect);

        var inner = rect.Grow(-(Border + Inset));
        var fraction = Total > 0 ? _remaining / Total : 0f;
        if (fraction <= 0f || inner.Size.X <= 0)
            return;
        // Keep the fill at least as wide as it is tall so the rounded end stays round
        var width = Mathf.Max(inner.Size.Y, inner.Size.X * fraction);
        var fill = new Rect2(inner.Position, new Vector2(width, inner.Size.Y));
        var innerRadius = (int)(inner.Size.Y / 2);
        _fillBox.BgColor = _fill;
        _fillBox.SetCornerRadiusAll(innerRadius);
        DrawStyleBox(_fillBox, fill);
        // Soft highlight on the upper part
        var shine = new Rect2(fill.Position + new Vector2(innerRadius * 0.6f, inner.Size.Y * 0.18f),
            new Vector2(Mathf.Max(0, fill.Size.X - innerRadius * 1.2f), inner.Size.Y * 0.22f));
        if (shine.Size.X > 0)
        {
            _shineBox.SetCornerRadiusAll((int)(shine.Size.Y / 2));
            DrawStyleBox(_shineBox, shine);
        }
    }
}
