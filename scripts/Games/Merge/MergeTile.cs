using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Merge;

/// <summary>
/// One number tile: the painted tile for its value (merge/tile_NN.png) with the number on top. The node origin is the
/// cell center. Slides, pops in, and punches when it is the result of a join.
/// </summary>
public partial class MergeTile : Node2D
{
    private Sprite2D _sprite = null!;
    private Label _label = null!;
    private float _cell;

    public int Value { get; private set; }

    public static MergeTile Create(int value, float cell)
    {
        var tile = new MergeTile();
        tile._sprite = new Sprite2D();
        tile.AddChild(tile._sprite);
        tile._label = Ui.Label("", 40, Colors.White, 10, UiTheme.Ink);
        tile._label.HorizontalAlignment = HorizontalAlignment.Center;
        tile._label.VerticalAlignment = VerticalAlignment.Center;
        tile._label.MouseFilter = Control.MouseFilterEnum.Ignore;
        tile.AddChild(tile._label);
        tile.SetCellSize(cell);
        tile.SetValue(value);
        return tile;
    }

    public void SetCellSize(float cell)
    {
        _cell = cell;
        if (_sprite.Texture is { } tex)
            _sprite.Scale = Vector2.One * (cell * 0.94f / Mathf.Max(tex.GetWidth(), tex.GetHeight()));
        _label.Size = new Vector2(cell, cell);
        _label.Position = -_label.Size / 2;
        FitText();
    }

    public void SetValue(int value)
    {
        Value = value;
        _sprite.Texture = Ui.Tex($"res://assets/sprites/merge/tile_{MergeRules.TileArt(value):00}.png");
        _label.Text = value.ToString();
        SetCellSize(_cell);
    }

    /// <summary>Big digits for small numbers; four digits still fit inside the tile.</summary>
    private void FitText()
    {
        var digits = Value.ToString().Length;
        var size = _cell * (digits <= 2 ? 0.46f : digits == 3 ? 0.36f : 0.28f);
        _label.AddThemeFontSizeOverride("font_size", Mathf.Max(12, (int)size));
        _label.AddThemeConstantOverride("outline_size", Mathf.Max(4, (int)(size * 0.22f)));
    }

    /// <summary>A new tile: grows out of the cell.</summary>
    public void PopIn(float delay = 0f)
    {
        Scale = Vector2.Zero;
        var tween = CreateTween();
        tween.TweenInterval(delay);
        tween.TweenProperty(this, "scale", Vector2.One, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A joined tile: swells and settles.</summary>
    public void Punch()
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.22f, 1.22f), 0.07f).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "scale", Vector2.One, 0.16f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Slides to a cell center; a small squash where it stops.</summary>
    public void SlideTo(Vector2 target, float time)
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "position", target, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }
}
