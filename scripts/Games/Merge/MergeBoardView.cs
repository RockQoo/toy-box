using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Merge;

/// <summary>
/// The board under the tiles: a rounded panel with an empty slot per cell. The node origin is the top-left of the grid;
/// tiles are its children placed at <see cref="CellCenter"/>.
/// </summary>
public partial class MergeBoardView : Node2D
{
    private const float Pad = 0.12f;      // Panel border around the grid, in cells

    private Texture2D _slot = null!;
    private StyleBoxFlat _panel = null!;

    public int Size { get; private set; }
    public float CellSize { get; private set; }
    public Vector2 GridSize => new(Size * CellSize, Size * CellSize);

    public static MergeBoardView Create(int size, float cell)
    {
        var view = new MergeBoardView { Size = size };
        view._slot = Ui.Tex("res://assets/sprites/ui/cell_empty.png");
        view._panel = UiTheme.RaisedBox(new Color(0.98f, 0.9f, 0.74f), UiTheme.Ink, 6, 0, 26);
        view.SetCellSize(cell);
        return view;
    }

    public void SetCellSize(float cell)
    {
        CellSize = cell;
        foreach (var child in GetChildren())
            if (child is MergeTile tile)
                tile.SetCellSize(cell);
        QueueRedraw();
    }

    public Vector2 CellCenter(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    public override void _Draw()
    {
        var pad = CellSize * Pad;
        DrawStyleBox(_panel, new Rect2(-pad, -pad, GridSize + new Vector2(pad, pad) * 2));
        var inset = CellSize * 0.03f;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                DrawTextureRect(_slot, new Rect2(x * CellSize + inset, y * CellSize + inset, CellSize - inset * 2, CellSize - inset * 2), false);
    }
}
