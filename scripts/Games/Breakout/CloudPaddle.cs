using Godot;
using ToyBox.Core;

namespace ToyBox.Games.Breakout;

/// <summary>
/// The cloud paddle. Its origin is the middle of the top surface (where the ball bounces); it squashes a little on
/// every bounce. Size follows <see cref="BreakoutRules.PaddleWidth"/> times <see cref="Unit"/>.
/// </summary>
public partial class CloudPaddle : Node2D
{
    private Tween? _squash;
    private float _unit = 720f;

    public float Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var tex = BrickArt.Paddle;
        var w = BreakoutRules.PaddleWidth * _unit;
        var h = w * tex.GetHeight() / tex.GetWidth();
        // A soft shadow under the cloud, then the cloud; its puffy top sits a little above the bounce line
        DrawSetTransform(new Vector2(0, h * 0.95f), 0, new Vector2(1, 0.22f));
        DrawCircle(Vector2.Zero, w * 0.42f, new Color(0.1f, 0.15f, 0.3f, 0.16f));
        DrawSetTransform(Vector2.Zero);
        DrawTextureRect(tex, new Rect2(-w / 2, -h * 0.12f, w, h), false);
    }

    public void Squash(float amount = 1f)
    {
        _squash?.Kill();
        Scale = new Vector2(1 + 0.12f * amount, 1 - 0.22f * amount);
        _squash = CreateTween();
        _squash.TweenProperty(this, "scale", Vector2.One, 0.25f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }
}
