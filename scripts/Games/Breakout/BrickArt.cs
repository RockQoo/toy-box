using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Breakout;

/// <summary>
/// Drawing for the brick games (벽돌깨기, and later 공 쏘기 벽돌깨기): brick colors with cracks, the ball, the cloud
/// paddle, the flower line and the 공 늘리기 item, all from assets/sprites/brick. Stateless; call from a node's _Draw.
/// </summary>
public static class BrickArt
{
    public const int ColorCount = 6;
    private const string Dir = "res://assets/sprites/brick/";

    /// <summary>Main color of each brick (연두, 하늘, 파랑, 보라, 분홍, 금색), for shards and sparks.</summary>
    public static readonly Color[] Tints =
    {
        new(0.40f, 0.90f, 0.62f),
        new(0.45f, 0.82f, 0.98f),
        new(0.25f, 0.58f, 0.98f),
        new(0.70f, 0.45f, 0.95f),
        new(1.00f, 0.52f, 0.75f),
        new(1.00f, 0.78f, 0.18f),
    };

    public static Texture2D Brick(int colorIndex) => Ui.Tex($"{Dir}brick_{Mathf.Clamp(colorIndex, 0, ColorCount - 1) + 1}.png");
    public static Texture2D Ball => Ui.Tex(Dir + "ball.png");
    public static Texture2D BallGlow => Ui.Tex(Dir + "ball_glow.png");
    public static Texture2D Paddle => Ui.Tex(Dir + "paddle.png");
    public static Texture2D FloorLine => Ui.Tex(Dir + "floor_line.png");
    public static Texture2D ItemBallAdd => Ui.Tex(Dir + "item_ball_add.png");

    /// <summary>Crack overlay for a brick that has taken <paramref name="damage"/> hits (1 or 2; null for none).</summary>
    public static Texture2D? Crack(int damage) => damage switch
    {
        <= 0 => null,
        1 => Ui.Tex(Dir + "crack_1.png"),
        _ => Ui.Tex(Dir + "crack_2.png"),
    };

    /// <summary>A brick filling <paramref name="rect"/>, with the crack for its damage on top.</summary>
    public static void DrawBrick(CanvasItem canvas, Rect2 rect, int colorIndex, int damage, Color? modulate = null)
    {
        var m = modulate ?? Colors.White;
        canvas.DrawTextureRect(Brick(colorIndex), rect, false, m);
        if (Crack(damage) is { } crack)
            canvas.DrawTextureRect(crack, rect.Grow(rect.Size.Y * 0.04f), false, m with { A = m.A * 0.9f });
    }

    /// <summary>A ball of collision radius <paramref name="radius"/> (the sprite's outline sits just outside it).</summary>
    public static void DrawBall(CanvasItem canvas, Vector2 center, float radius, bool glow = false, float alpha = 1f)
    {
        var tex = glow ? BallGlow : Ball;
        // The glow sprite has a wider halo around the same ball
        var half = radius * (glow ? 1.35f : 1.12f);
        canvas.DrawTextureRect(tex, new Rect2(center - new Vector2(half, half), new Vector2(half * 2, half * 2)), false,
            Colors.White with { A = alpha });
    }
}
