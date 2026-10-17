using Godot;

namespace ToyBox.Games.Blocks;

/// <summary>A Node2D that draws whatever its <see cref="Painter"/> draws (layers of the board view).</summary>
public partial class DrawLayer : Node2D
{
    public System.Action<DrawLayer>? Painter { get; set; }

    public override void _Draw() => Painter?.Invoke(this);
}
