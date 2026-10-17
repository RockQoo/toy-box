using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Sudoku;

/// <summary>How a cell should look right now (set by the game after every change).</summary>
public readonly record struct CellLook(bool Fixed, bool Related, bool SameAnimal, bool Selected);

/// <summary>
/// One square of the board, drawn around its center: the tile, a soft glow when it holds the animal being looked at,
/// pencil sketches, the animal face, and the yellow selection frame. Animations: pop in, wobble (a clash: "고개 갸웃"),
/// hop (a finished line, the finished board) and wiggle (a starting animal that cannot change).
/// </summary>
public partial class SudokuCell : Node2D
{
    private static readonly Color FixedTint = new(0.86f, 0.80f, 0.72f);
    private static readonly Color RelatedTint = new(1f, 1f, 0.72f);

    private Sprite2D _tile = null!;
    private Glow _glow = null!;
    private Node2D _memoLayer = null!;
    private readonly Sprite2D[] _memos = new Sprite2D[6];
    private Node2D _face = null!;          // Pivot for the animal's animations
    private Sprite2D _animal = null!;
    private Sprite2D _select = null!;
    private Tween? _faceTween;
    private float _size = 100f;
    private int _value;
    private int _memoMask;

    public int Index { get; init; }
    /// <summary>Board size (4 or 6): where the pencil sketches sit.</summary>
    public int Animals { get; init; } = 6;

    public override void _Ready()
    {
        _tile = new Sprite2D { Texture = Ui.Tex("res://assets/sprites/ui/cell_empty.png") };
        AddChild(_tile);
        _glow = new Glow { Visible = false };
        AddChild(_glow);
        _memoLayer = new Node2D();
        AddChild(_memoLayer);
        for (var i = 0; i < _memos.Length; i++)
        {
            _memos[i] = new Sprite2D { Visible = false, Modulate = new Color(0.5f, 0.5f, 0.58f) };
            _memoLayer.AddChild(_memos[i]);
        }
        _face = new Node2D();
        AddChild(_face);
        _animal = new Sprite2D { Visible = false };
        _face.AddChild(_animal);
        _select = new Sprite2D { Texture = Ui.Tex("res://assets/sprites/ui/cell_select.png"), Visible = false };
        AddChild(_select);
        SetSize(_size);
    }

    public void SetSize(float size)
    {
        _size = size;
        if (_tile is null)
            return;
        var t = _tile.Texture.GetSize();
        _tile.Scale = new Vector2(size / t.X, size / t.Y);
        var s = _select.Texture.GetSize();
        _select.Scale = new Vector2(size * 1.1f / s.X, size * 1.1f / s.Y);
        _glow.Radius = size * 0.42f;
        _glow.QueueRedraw();
        ApplyAnimal();
        ApplyMemos();
    }

    /// <summary>Shows <paramref name="value"/> (0: none). <paramref name="pop"/> plays the "톡" pop-in.</summary>
    public void SetAnimal(int value, bool pop = false)
    {
        if (value == _value && !pop)
            return;
        _value = value;
        ApplyAnimal();
        if (pop && value != 0)
            Pop();
    }

    public void SetMemos(int mask)
    {
        if (mask == _memoMask)
            return;
        _memoMask = mask;
        ApplyMemos();
    }

    public void SetLook(CellLook look)
    {
        var tint = Colors.White;
        if (look.Fixed)
            tint *= FixedTint;
        if (look.Related || look.Selected)
            tint *= RelatedTint;
        _tile.Modulate = tint;
        _glow.Visible = look.SameAnimal && _value != 0;
        _select.Visible = look.Selected;
    }

    private void ApplyAnimal()
    {
        _animal.Visible = _value != 0;
        _memoLayer.Visible = _value == 0;
        if (_value == 0)
            return;
        _animal.Texture = SudokuArt.Face(_value);
        var k = SudokuArt.Fit(_animal.Texture, _size * 0.8f);
        _animal.Scale = new Vector2(k, k);
    }

    private void ApplyMemos()
    {
        _memoLayer.Visible = _value == 0;
        // 3 x 2 sketches in a 6 x 6 board, 2 x 2 in a 4 x 4 one
        var columns = Animals == 4 ? 2 : 3;
        const int rows = 2;
        var slot = _size * 0.94f / columns;
        for (var i = 0; i < _memos.Length; i++)
        {
            var sprite = _memos[i];
            var on = i < Animals && (_memoMask & (1 << (i + 1))) != 0;
            sprite.Visible = on;
            if (!on)
                continue;
            sprite.Texture = SudokuArt.Memo(i + 1);
            var k = SudokuArt.Fit(sprite.Texture, slot * 1.02f);
            sprite.Scale = new Vector2(k, k);
            var col = i % columns;
            var row = i / columns;
            sprite.Position = new Vector2((col - (columns - 1) / 2f) * slot, (row - (rows - 1) / 2f) * slot * 1.3f);
        }
    }

    private Tween Face()
    {
        _faceTween?.Kill();
        _face.Position = Vector2.Zero;
        _face.Rotation = 0;
        _face.Scale = Vector2.One;
        _faceTween = _face.CreateTween();
        return _faceTween;
    }

    /// <summary>The animal pops in with a little overshoot.</summary>
    public void Pop(float delay = 0f)
    {
        var tween = Face();
        _face.Scale = delay > 0 ? Vector2.Zero : new Vector2(0.2f, 0.2f);
        if (delay > 0)
            tween.TweenInterval(delay);
        tween.TweenProperty(_face, "scale", Vector2.One, 0.32f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A puzzled head tilt from side to side (the animal that is already there when a clash happens).</summary>
    public void Wobble()
    {
        var tween = Face();
        foreach (var deg in new[] { -16f, 13f, -9f, 5f, 0f })
            tween.TweenProperty(_face, "rotation_degrees", deg, 0.09f).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>A quick side-to-side "no" (a starting animal cannot change).</summary>
    public void Wiggle()
    {
        var tween = Face();
        foreach (var x in new[] { -5f, 5f, -3f, 0f })
            tween.TweenProperty(_face, "position:x", x, 0.05f);
    }

    /// <summary>A happy hop after <paramref name="delay"/> (a finished line ripples cell by cell).</summary>
    public void Hop(float delay, float height = 0.28f)
    {
        var tween = Face();
        if (delay > 0)
            tween.TweenInterval(delay);
        var up = new Vector2(0, -_size * height);
        tween.TweenProperty(_face, "scale", new Vector2(1.15f, 0.85f), 0.06f);
        tween.TweenProperty(_face, "position", up, 0.14f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(_face, "scale", new Vector2(0.92f, 1.12f), 0.14f);
        tween.TweenProperty(_face, "position", Vector2.Zero, 0.16f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(_face, "scale", Vector2.One, 0.16f);
    }

    /// <summary>Soft round light behind the animal (the same animal as the one being looked at).</summary>
    private partial class Glow : Node2D
    {
        public float Radius { get; set; } = 40f;

        public override void _Draw()
        {
            DrawCircle(Vector2.Zero, Radius, new Color(1f, 0.92f, 0.45f, 0.55f));
            DrawCircle(Vector2.Zero, Radius * 0.75f, new Color(1f, 0.97f, 0.7f, 0.6f));
        }
    }
}
