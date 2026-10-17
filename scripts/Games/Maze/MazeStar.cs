using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Maze;

/// <summary>A star waiting in the maze: bobs and sways over a soft yellow glow, so it reads as "pick me up".</summary>
public partial class MazeStar : Node2D
{
    private Sprite2D _sprite = null!;
    private float _time;
    private float _cell = 60f;

    public int Index { get; init; }

    public override void _Ready()
    {
        _sprite = new Sprite2D { Texture = Ui.Tex("res://assets/sprites/ui/star.png") };
        AddChild(_sprite);
        _time = Index * 0.9f;
        SetCellSize(_cell);
    }

    public void SetCellSize(float cell)
    {
        _cell = cell;
        if (_sprite is null)
            return;
        var s = cell * 0.6f / _sprite.Texture.GetSize().Y;
        _sprite.Scale = new Vector2(s, s);
        QueueRedraw();
    }

    public Texture2D Texture => _sprite.Texture;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _sprite.Position = new Vector2(0, Mathf.Sin(_time * 2.6f) * _cell * 0.05f);
        _sprite.Rotation = Mathf.Sin(_time * 1.7f) * 0.15f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var pulse = 0.5f + 0.5f * Mathf.Sin(_time * 3.2f);
        DrawCircle(Vector2.Zero, _cell * (0.3f + 0.04f * pulse), UiTheme.Yellow with { A = 0.22f + 0.12f * pulse }, antialiased: true);
    }
}
