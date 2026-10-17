using Godot;
using ToyBox.Core;

namespace ToyBox.Games.Blocks;

/// <summary>A small box showing one piece (or nothing): the next pieces and the held one.</summary>
public partial class PiecePreview : Control
{
    private PieceKind? _kind;
    private bool _dim;

    public static PiecePreview Create(Vector2 size) =>
        new() { CustomMinimumSize = size, MouseFilter = MouseFilterEnum.Ignore };

    public void Show(PieceKind? kind, bool dim = false)
    {
        if (_kind == kind && _dim == dim)
            return;
        var changed = _kind != kind;
        _kind = kind;
        _dim = dim;
        QueueRedraw();
        if (changed && kind is not null)
        {
            PivotOffset = Size / 2;
            Scale = new Vector2(0.7f, 0.7f);
            CreateTween().TweenProperty(this, "scale", Vector2.One, 0.22f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    public override void _Draw()
    {
        if (_kind is not { } kind)
            return;
        var extent = BlocksArt.Extent(kind);
        var cell = Mathf.Min(Size.X * 0.9f / Mathf.Max(extent.X, 3f), Size.Y * 0.9f / Mathf.Max(extent.Y, 2f));
        BlocksArt.DrawPiece(this, kind, Size / 2, cell, Colors.White with { A = _dim ? 0.4f : 1f });
    }
}
