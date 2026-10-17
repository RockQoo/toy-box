using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.DinoRunner;

/// <summary>
/// A cactus or rock standing on the ground line (node origin: bottom center). The world moves it left; the hitbox
/// (<see cref="DinoRules.Hitbox"/>) is centered on it and much smaller than the picture.
/// </summary>
public partial class Obstacle : Node2D
{
    private Sprite2D _sprite = null!;
    private Tween? _tween;
    private Vector2 _baseScale;

    public DinoObstacle Kind { get; private set; }
    /// <summary>Scored (it went by without a crash).</summary>
    public bool Passed { get; set; }
    /// <summary>Autoplay already decided about this one.</summary>
    public bool Aimed { get; set; }
    /// <summary>Half the picture width in pixels (for spawning just off screen and freeing just after).</summary>
    public float HalfWidth { get; private set; }

    /// <summary>Picture height as a fraction of the screen height.</summary>
    private static float PictureHeight(DinoObstacle kind) => kind switch
    {
        DinoObstacle.SmallCactus => 0.16f,
        DinoObstacle.Cactus => 0.22f,
        _ => 0.12f,
    };

    private static string PicturePath(DinoObstacle kind) =>
        kind == DinoObstacle.Rock ? "res://assets/sprites/dino/rock.png" : "res://assets/sprites/dino/cactus.png";

    public static Obstacle Create(DinoObstacle kind, float screenHeight)
    {
        var texture = Ui.Tex(PicturePath(kind));
        var size = texture.GetSize();
        var scale = PictureHeight(kind) * screenHeight / size.Y;
        var obstacle = new Obstacle { Kind = kind, HalfWidth = size.X * scale / 2 };
        obstacle._sprite = new Sprite2D
        {
            Texture = texture,
            Scale = new Vector2(scale, scale),
            // Sits a little into the grass
            Offset = new Vector2(0, -size.Y / 2 + size.Y * 0.04f),
        };
        obstacle._baseScale = obstacle._sprite.Scale;
        obstacle.AddChild(obstacle._sprite);
        return obstacle;
    }

    /// <summary>Passed: a happy little wobble as it goes by.</summary>
    public void Wobble()
    {
        _tween?.Kill();
        var s = _baseScale;
        _tween = CreateTween();
        _tween.TweenProperty(_sprite, "scale", new Vector2(s.X * 1.1f, s.Y * 0.88f), 0.06f);
        _tween.TweenProperty(_sprite, "scale", s, 0.35f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>The one the dino bumped into: shakes a little ("통!").</summary>
    public void Bumped()
    {
        _tween?.Kill();
        _tween = CreateTween();
        foreach (var angle in new[] { 0.12f, -0.09f, 0.05f, -0.02f, 0f })
            _tween.TweenProperty(_sprite, "rotation", angle, 0.07f);
    }
}
