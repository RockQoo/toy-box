using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Flappy;

/// <summary>
/// A pair of tree pillars with a gap between them. The node's x is the trunk's center; y is 0 (the halves are placed in
/// screen pixels). The upper half is the lower one flipped upside down. The hit trunks (<see cref="FlappyRules.TrunkWidth"/>)
/// are narrower than the picture, and the leafy caps reach a little into the gap without counting.
/// </summary>
public partial class Pillar : Node2D
{
    private PillarHalf _upper = null!;
    private PillarHalf _lower = null!;

    public FlappyGap Gap { get; private set; }
    /// <summary>Hit trunk left edge in screen heights (the game moves it and the node follows).</summary>
    public float Left { get; set; }
    /// <summary>Scored (it went by without a crash).</summary>
    public bool Passed { get; set; }

    /// <param name="screenHeight">Pixels per screen height.</param>
    /// <param name="groundY">The ground line in pixels.</param>
    public static Pillar Create(FlappyGap gap, float left, float screenHeight, float groundY)
    {
        var pillar = new Pillar { Gap = gap, Left = left };
        var width = FlappyRules.PillarPictureWidth * screenHeight;
        // Lower: from the gap bottom down into the grass. Upper: from the gap top up past the screen top.
        pillar._lower = PillarHalf.Create(width, groundY - gap.Bottom * screenHeight + screenHeight * 0.04f);
        pillar._lower.Position = new Vector2(0, gap.Bottom * screenHeight);
        pillar._upper = PillarHalf.Create(width, gap.Top * screenHeight + screenHeight * 0.03f);
        pillar._upper.Position = new Vector2(0, gap.Top * screenHeight);
        pillar._upper.Scale = new Vector2(1, -1);
        pillar.AddChild(pillar._upper);
        pillar.AddChild(pillar._lower);
        return pillar;
    }

    /// <summary>Passed: the leaves on both caps rustle.</summary>
    public void Rustle()
    {
        _upper.Rustle(0.10f);
        _lower.Rustle(-0.10f);
    }

    /// <summary>The bird bumped into it: a small shake ("통!").</summary>
    public void Bumped()
    {
        var tween = CreateTween();
        foreach (var x in new[] { 6f, -5f, 3f, -1f, 0f })
        {
            tween.TweenProperty(_upper, "position:x", x, 0.06f);
            tween.Parallel().TweenProperty(_lower, "position:x", x, 0.06f);
        }
    }
}

/// <summary>
/// One half of a pillar, drawn from the gap edge (origin) downward over <see cref="_length"/> pixels: the leafy cap at
/// the gap, a trunk made of the picture's middle band repeated with every other copy mirrored (so the joins match),
/// and the grassy foot of the picture at the far end.
/// </summary>
public partial class PillarHalf : Node2D
{
    private const string BodyPath = "res://assets/sprites/flappy/pillar_body.png";
    private const string CapPath = "res://assets/sprites/flappy/pillar_cap.png";

    // Rows of pillar_body.png: the trunk band (between the rounded top and the grassy foot) and the foot
    private const float TrunkTop = 30f;
    private const float TrunkBottom = 180f;

    /// <summary>How much of the cap reaches past the trunk end into the gap.</summary>
    private const float CapIntoGap = 0.35f;

    private Texture2D _body = null!;
    private Sprite2D _cap = null!;
    private float _width;
    private float _length;
    private float _scale;
    private Vector2 _capScale;
    private Tween? _tween;

    public static PillarHalf Create(float width, float length)
    {
        var half = new PillarHalf { _width = width, _length = Mathf.Max(0, length) };
        half._body = Ui.Tex(BodyPath);
        half._scale = width / half._body.GetSize().X;
        var cap = Ui.Tex(CapPath);
        // Same pixel scale as the body (the cap picture is a little wider than the trunk picture)
        var capHeight = cap.GetSize().Y * half._scale;
        half._capScale = Vector2.One * half._scale;
        half._cap = new Sprite2D
        {
            Texture = cap,
            Scale = half._capScale,
            // Pivot on the cap's bottom rim, so a rustle sways the leaves
            Offset = new Vector2(0, -cap.GetSize().Y / 2),
            Position = new Vector2(0, capHeight * (1 - CapIntoGap)),
        };
        half.AddChild(half._cap);
        return half;
    }

    public override void _Draw()
    {
        var size = _body.GetSize();
        var footHeight = (size.Y - TrunkBottom) * _scale;
        var bandHeight = (TrunkBottom - TrunkTop) * _scale;
        // The foot at the far end (only its lower part if the half is short)
        var footTop = Mathf.Max(0, _length - footHeight);
        DrawBand(size.Y - (_length - footTop) / _scale, size.Y, footTop, _length, false);
        // Trunk bands from the foot back toward the gap; the first one is not mirrored so it meets the foot cleanly
        var bottom = footTop;
        var mirrored = false;
        while (bottom > 0)
        {
            var top = Mathf.Max(0, bottom - bandHeight);
            var rows = (bottom - top) / _scale;
            // Cropped at the gap end: keep the rows that touch the previous band
            if (mirrored)
                DrawBand(TrunkTop, TrunkTop + rows, top, bottom, true);
            else
                DrawBand(TrunkBottom - rows, TrunkBottom, top, bottom, false);
            bottom = top;
            mirrored = !mirrored;
        }
    }

    /// <summary>Draws picture rows [srcTop, srcBottom] into [top, bottom] (pixels below the origin), upside down if mirrored.</summary>
    private void DrawBand(float srcTop, float srcBottom, float top, float bottom, bool mirror)
    {
        if (bottom - top < 0.5f)
            return;
        var src = new Rect2(0, srcTop, _body.GetSize().X, srcBottom - srcTop);
        var dest = new Rect2(-_width / 2, top, _width, bottom - top);
        if (mirror)
            DrawSetTransform(new Vector2(0, top + bottom), 0, new Vector2(1, -1));
        DrawTextureRectRegion(_body, dest, src);
        if (mirror)
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    public void Rustle(float angle)
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_cap, "scale", new Vector2(_capScale.X * 1.08f, _capScale.Y * 1.12f), 0.07f);
        _tween.Parallel().TweenProperty(_cap, "rotation", angle, 0.07f);
        _tween.TweenProperty(_cap, "scale", _capScale, 0.45f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_cap, "rotation", 0f, 0.45f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }
}
