using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Bubble;

/// <summary>
/// Bubble pictures (bubble/*.png: a shape inside each color, so children who cannot tell the colors apart can match
/// the shapes) and their tints for effects. Color index = <see cref="Names"/> index.
/// </summary>
public static class BubbleArt
{
    private const string Dir = "res://assets/sprites/bubble/";

    /// <summary>red 하트, yellow 별, green 잎, blue 물방울, purple 달, orange 동그라미.</summary>
    public static readonly string[] Names = { "red", "yellow", "green", "blue", "purple", "orange" };

    public static readonly Color[] Tints =
    {
        new(1f, 0.36f, 0.36f),
        new(1f, 0.84f, 0.22f),
        new(0.38f, 0.84f, 0.30f),
        new(0.28f, 0.60f, 1f),
        new(0.70f, 0.45f, 1f),
        new(1f, 0.60f, 0.18f),
    };

    /// <summary>Drawn a little larger than one diameter so neighbors touch through the dark outline.</summary>
    public const float DrawScale = 1.04f;

    public static Texture2D Bubble(int color) => Ui.Tex($"{Dir}{Names[Mathf.Clamp(color, 0, Names.Length - 1)]}.png");

    public static Texture2D Pop(int frame) => Ui.Tex($"{Dir}pop_0{Mathf.Clamp(frame, 0, 3) + 1}.png");

    public static Texture2D LauncherBase => Ui.Tex(Dir + "launcher_base.png");
    public static Texture2D LauncherBarrel => Ui.Tex(Dir + "launcher_barrel.png");
    public static Texture2D NextHolder => Ui.Tex(Dir + "next_holder.png");
    public static Texture2D Ceiling => Ui.Tex(Dir + "ceiling.png");

    /// <summary>A bubble of <paramref name="diameter"/> pixels centered at <paramref name="center"/>.</summary>
    public static void Draw(CanvasItem canvas, int color, Vector2 center, float diameter, Color? modulate = null)
    {
        var tex = Bubble(color);
        var h = diameter * DrawScale;
        var w = h * tex.GetWidth() / tex.GetHeight();
        canvas.DrawTextureRect(tex, new Rect2(center - new Vector2(w, h) / 2, new Vector2(w, h)), false, modulate ?? Colors.White);
    }
}
