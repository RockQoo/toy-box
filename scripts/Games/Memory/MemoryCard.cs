using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Memory;

/// <summary>
/// One animal card. Draws the back (rainbow) or the animal face; <see cref="Turn"/> flips it like a real card: it lifts
/// a little while squeezing to zero width, swaps the picture at the edge-on moment, then springs back with a small
/// squash. A matched card gets a gold frame and a happy hop. All of this is drawn inside the button rect (no Position or
/// Scale changes), so layout and the press squish never fight the flip.
/// </summary>
public partial class MemoryCard : Button
{
    private const string BackPath = "res://assets/sprites/animal/card_back.png";
    private const float Aspect = 0.87f; // The animal art is about 186 x 215; the back is drawn to the same shape
    private const float LiftHeight = 16f;

    public int Index { get; private set; }
    public int Animal { get; private set; }
    public bool FaceUp { get; private set; }
    public bool Matched { get; private set; }
    public string AnimalName => MemoryRules.AnimalNames[Animal];
    /// <summary>Center of the card in the parent's coordinates (for effects).</summary>
    public Vector2 Center => Position + Size / 2;

    private Texture2D _face = null!;
    private Texture2D _back = null!;
    private float _width = 1f;   // Horizontal scale of the picture: 1 flat, 0 edge-on
    private float _lift;         // Pixels above the resting place
    private float _squash;       // + wide and short, - narrow and tall
    private float _glow;         // Gold frame of a matched card, 0..1
    private float _time;
    private Tween? _turn;
    private Tween? _hop;

    public static MemoryCard Create(int index, int animal)
    {
        var card = new MemoryCard
        {
            Index = index,
            Animal = animal,
            FocusMode = FocusModeEnum.None,
            _face = Ui.Tex($"res://assets/sprites/animal/{MemoryRules.Animals[animal]}.png"),
            _back = Ui.Tex(BackPath),
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
            card.AddThemeStyleboxOverride(state, empty);
        Ui.MakeSquishy(card);
        return card;
    }

    /// <summary>Face up or down at once, without the flip animation.</summary>
    public void SetFace(bool faceUp)
    {
        _turn?.Kill();
        FaceUp = faceUp;
        _width = 1f;
        _lift = 0f;
        _squash = 0f;
        QueueRedraw();
    }

    /// <summary>Flips the card over to <paramref name="faceUp"/>. Turning down is a bit slower and softer.</summary>
    public void Turn(bool faceUp, float delay = 0f, bool sound = true)
    {
        _turn?.Kill();
        var half = faceUp ? 0.11f : 0.14f;
        _turn = CreateTween();
        if (delay > 0)
            _turn.TweenInterval(delay);
        if (sound)
            _turn.TweenCallback(Callable.From(() => Audio.I.CardFlip(faceUp)));
        var startWidth = _width;
        var startLift = _lift;
        _turn.TweenMethod(Callable.From<float>(p =>
        {
            _width = Mathf.Lerp(startWidth, 0f, p);
            _lift = Mathf.Lerp(startLift, LiftHeight, p);
            _squash = -0.05f * p;
            QueueRedraw();
        }), 0f, 1f, half).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _turn.TweenCallback(Callable.From(() => FaceUp = faceUp));
        _turn.TweenMethod(Callable.From<float>(p =>
        {
            _width = p;
            _lift = LiftHeight * (1 - p);
            _squash = -0.05f * (1 - p);
            QueueRedraw();
        }), 0f, 1f, half).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        // Lands with a small squash
        _turn.TweenMethod(Callable.From<float>(s =>
        {
            _squash = s;
            QueueRedraw();
        }), 0.06f, 0f, 0.22f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Found its pair: a gold frame grows in, and the card hops (twice when <paramref name="big"/>).</summary>
    public void Celebrate(float delay = 0f, bool big = false)
    {
        Matched = true;
        Disabled = true;
        var glow = CreateTween();
        glow.TweenMethod(Callable.From<float>(g =>
        {
            _glow = g;
            QueueRedraw();
        }), 0f, 1f, 0.3f).SetDelay(delay);
        Hop(delay, big ? 2 : 1, big ? 40f : 30f);
    }

    /// <summary>A happy jump with a squash on take-off and landing.</summary>
    public void Hop(float delay = 0f, int times = 1, float height = 30f)
    {
        _hop?.Kill();
        _hop = CreateTween();
        if (delay > 0)
            _hop.TweenInterval(delay);
        for (var i = 0; i < times; i++)
        {
            var h = height * (i == 0 ? 1f : 0.6f);
            _hop.TweenMethod(Callable.From<float>(SetSquash), 0f, 0.1f, 0.06f);
            _hop.TweenMethod(Callable.From<float>(p =>
            {
                _lift = h * Mathf.Sin(p * Mathf.Pi);
                _squash = p < 0.5f ? -0.08f * (1 - p * 2) : 0f;
                QueueRedraw();
            }), 0f, 1f, 0.34f);
            _hop.TweenMethod(Callable.From<float>(SetSquash), 0.08f, 0f, 0.25f)
                .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        }
    }

    private void SetSquash(float s)
    {
        _squash = s;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_glow > 0)
        {
            _time += (float)delta;
            QueueRedraw(); // The gold frame shimmers
        }
    }

    public override void _Draw()
    {
        var rect = FitRect(Size);
        var center = rect.GetCenter();

        // Shadow on the table: smaller and lighter while the card is up in the air
        var lifted = Mathf.Clamp(_lift / 40f, 0f, 1f);
        var shadowSize = rect.Size * new Vector2(_width * 0.92f + 0.08f, 1f) * (1f - 0.12f * lifted);
        var shadow = new Rect2(center - shadowSize / 2 + new Vector2(0, 7), shadowSize);
        DrawStyleBox(UiTheme.Box(new Color(0.1f, 0.12f, 0.25f, 0.22f - 0.1f * lifted), Colors.Transparent, 0, 0,
            (int)(rect.Size.X * 0.12f)), shadow);

        var scale = new Vector2(_width * (1 + _squash), 1 - _squash);
        // Squash keeps the bottom edge on the table
        var pivot = center + new Vector2(0, rect.Size.Y / 2 - _lift);
        DrawSetTransform(pivot, 0f, scale);
        var local = new Rect2(-rect.Size.X / 2, -rect.Size.Y, rect.Size.X, rect.Size.Y);
        if (_glow > 0)
        {
            var pulse = 0.85f + 0.15f * Mathf.Sin(_time * 4f);
            var grow = rect.Size.X * 0.05f * _glow;
            var frame = UiTheme.Box(UiTheme.Gold with { A = 0.45f * _glow * pulse }, UiTheme.Gold with { A = _glow },
                Mathf.Max(3, (int)(rect.Size.X * 0.035f)), 0, (int)(rect.Size.X * 0.16f));
            DrawStyleBox(frame, local.Grow(grow));
        }
        DrawTextureRect(FaceUp ? _face : _back, local, false);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>The card picture's rect inside the button: the art's aspect, with headroom for the lift.</summary>
    private static Rect2 FitRect(Vector2 size)
    {
        var w = Mathf.Min(size.X, size.Y * Aspect);
        var h = w / Aspect;
        return new Rect2((size - new Vector2(w, h)) / 2, new Vector2(w, h));
    }
}
