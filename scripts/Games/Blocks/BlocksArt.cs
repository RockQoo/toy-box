using System.Collections.Generic;
using System.Linq;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Blocks;

/// <summary>Block pictures (blocks/&lt;color&gt;.png, one per piece, each with its own pattern) and how to draw them.</summary>
public static class BlocksArt
{
    private static readonly Color[] Tints =
    {
        new(0.35f, 0.80f, 1f),    // I cyan
        new(1f, 0.84f, 0.25f),    // O yellow
        new(0.68f, 0.45f, 0.95f), // T purple
        new(0.45f, 0.82f, 0.35f), // S green
        new(1f, 0.50f, 0.70f),    // Z pink
        new(0.30f, 0.55f, 0.98f), // J blue
        new(1f, 0.62f, 0.25f),    // L orange
    };

    public static Texture2D Cell(PieceKind kind) =>
        Ui.Tex($"res://assets/sprites/blocks/{BlocksRules.PictureNames[(int)kind]}.png");

    public static Texture2D Ghost => Ui.Tex("res://assets/sprites/blocks/ghost.png");
    public static Texture2D Flash => Ui.Tex("res://assets/sprites/blocks/clear_flash.png");

    public static Color ColorOf(PieceKind kind) => Tints[(int)kind];

    /// <summary>One block filling the cell at <paramref name="topLeft"/> (a hair smaller, so neighbors show their outlines).</summary>
    public static void DrawCell(CanvasItem canvas, Vector2 topLeft, Vector2 size, PieceKind kind, Color? modulate = null)
    {
        var inset = size * 0.02f;
        canvas.DrawTextureRect(Cell(kind), new Rect2(topLeft + inset, size - inset * 2), false, modulate ?? Colors.White);
    }

    /// <summary>A whole piece (rotation 0) centered on <paramref name="center"/>, for the next / hold boxes.</summary>
    public static void DrawPiece(CanvasItem canvas, PieceKind kind, Vector2 center, float cell, Color modulate)
    {
        var cells = BlocksRules.Cells(kind, 0);
        var min = new Vector2(cells.Min(c => c.X), cells.Min(c => c.Y));
        var max = new Vector2(cells.Max(c => c.X) + 1, cells.Max(c => c.Y) + 1);
        var origin = center - (min + max) / 2 * cell;
        foreach (var c in cells)
            DrawCell(canvas, origin + new Vector2(c.X, c.Y) * cell, new Vector2(cell, cell), kind, modulate);
    }

    /// <summary>Width and height in cells of a piece as drawn in the boxes.</summary>
    public static Vector2 Extent(PieceKind kind)
    {
        IReadOnlyList<BlockCell> cells = BlocksRules.Cells(kind, 0);
        return new Vector2(cells.Max(c => c.X) - cells.Min(c => c.X) + 1, cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1);
    }
}
