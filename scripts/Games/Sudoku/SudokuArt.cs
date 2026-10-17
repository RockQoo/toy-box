using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Sudoku;

/// <summary>The animal faces (animal 1..6) and their pencil sketches, plus the names the buddy uses.</summary>
public static class SudokuArt
{
    private static readonly string[] Ids = { "lion", "elephant", "penguin", "rabbit", "fox", "panda" };
    private static readonly string[] Names = { "사자", "코끼리", "펭귄", "토끼", "여우", "판다" };

    public static Texture2D Face(int animal) => Ui.Tex($"res://assets/sprites/sudoku/{Ids[animal - 1]}.png");

    public static Texture2D Memo(int animal) => Ui.Tex($"res://assets/sprites/sudoku/{Ids[animal - 1]}_memo.png");

    public static string FacePath(int animal) => $"res://assets/sprites/sudoku/{Ids[animal - 1]}.png";

    public static string Name(int animal) => Names[animal - 1];

    /// <summary>Scale that fits a texture inside a square of <paramref name="size"/> pixels, keeping its shape.</summary>
    public static float Fit(Texture2D texture, float size)
    {
        var s = texture.GetSize();
        return size / Mathf.Max(s.X, s.Y);
    }
}
