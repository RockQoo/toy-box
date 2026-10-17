using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;
using ToyBox.Core;

namespace ToyBox.Games.Snake;

/// <summary>
/// Draws the caterpillar in one node: tail, round body segments, then the head on top (so it crawls over its own body).
/// Each step glides every segment from its old cell to its new one. Effects: a crawling wave through the body, the mouth
/// opening for a leaf, a bulge that travels from head to tail after eating, a rainbow wave, a wall "깜짝" and the
/// curl-up at the end. Positions are in the parent's (the field's) pixels.
/// </summary>
public partial class Caterpillar : Node2D
{
    private const float SegmentScale = 1.08f; // Body segment size in cells (a little overlap)
    private const float HeadScale = 1.5f;     // The head picture carries a segment behind the face
    private const float HeadBack = 0.16f;     // How far the head picture sits behind the head cell, in cells
    private const float BulgeSpeed = 9f;      // Segments per second
    private const float RainbowSpeed = 16f;
    private const float BumpTime = 0.35f;
    private const float MaxHeadTilt = 0.85f;  // Radians the face tilts toward up / down

    private readonly Texture2D _headTex = Ui.Tex("res://assets/sprites/snake/head.png");
    private readonly Texture2D _headEatTex = Ui.Tex("res://assets/sprites/snake/head_eat.png");
    private readonly Texture2D _bodyTex = Ui.Tex("res://assets/sprites/snake/body.png");
    private readonly Texture2D _tailTex = Ui.Tex("res://assets/sprites/snake/tail.png");

    private readonly List<Vector2> _from = new();
    private readonly List<Vector2> _to = new();
    private readonly List<float> _bulges = new();

    private float _cell = 60f;
    private float _t = 1f;
    private float _duration = 0.25f;
    private float _time;
    private float _eatTimer;
    private float _rainbow = -1f;
    private float _bump = -1f;
    private Vector2 _bumpDir;
    private float _over;
    private bool _faceLeft;
    private bool _curled;
    private float _curlT;
    private Vector2[] _curlFrom = Array.Empty<Vector2>();
    private Vector2[] _curlTo = Array.Empty<Vector2>();

    /// <summary>True when the next cell holds a leaf: the mouth opens in anticipation.</summary>
    public bool MouthOpen { get; set; }

    /// <summary>True while the head is on top of its own body (drawn a little bigger, with a shadow).</summary>
    public bool OverBody { get; set; }

    /// <summary>True while it moves (the crawl wave runs faster).</summary>
    public bool Crawling { get; set; }

    public int Count => _to.Count;

    public Vector2 HeadPosition => Count > 0 ? PieceAt(0) : Vector2.Zero;

    public void SetCellSize(float cell) => _cell = cell;

    /// <summary>Snaps to the body (cell centers in pixels, head first) without gliding.</summary>
    public void Reset(IReadOnlyList<Vector2> body)
    {
        _from.Clear();
        _to.Clear();
        _from.AddRange(body);
        _to.AddRange(body);
        _t = 1f;
        _bulges.Clear();
        _rainbow = -1f;
        _bump = -1f;
        _eatTimer = 0;
        _curled = false;
        _over = 0;
        _faceLeft = false;
        QueueRedraw();
    }

    /// <summary>Glides from where it is now to the new body over <paramref name="duration"/> seconds.</summary>
    public void Advance(IReadOnlyList<Vector2> body, float duration)
    {
        var now = new List<Vector2>();
        for (var i = 0; i < Count; i++)
            now.Add(PieceAt(i));
        _from.Clear();
        _to.Clear();
        for (var i = 0; i < body.Count; i++)
            _from.Add(now.Count == 0 ? body[i] : now[Math.Min(i, now.Count - 1)]); // A new tail segment grows out of the old tail
        _to.AddRange(body);
        _t = 0f;
        _duration = Mathf.Max(duration, 0.02f);
    }

    public void Eat()
    {
        _eatTimer = 0.3f;
        _bulges.Add(0f);
    }

    public void Rainbow() => _rainbow = 0f;

    public void Bump(MazeDir dir)
    {
        _bump = 0f;
        _bumpDir = Dir(dir);
    }

    /// <summary>Time's up: curls into a ring around the head with a big smile.</summary>
    public void Curl(Rect2 bounds)
    {
        var n = Count;
        if (n == 0)
            return;
        _curlFrom = new Vector2[n];
        for (var i = 0; i < n; i++)
            _curlFrom[i] = PieceAt(i);
        var spacing = _cell * 0.62f;
        var radius = Mathf.Max(n * spacing / (Mathf.Tau * 0.92f), _cell * 0.7f);
        var step = spacing / radius;
        var bodyCenter = Vector2.Zero;
        foreach (var p in _curlFrom)
            bodyCenter += p;
        bodyCenter /= n;
        var center = new Vector2(
            Mathf.Clamp(bodyCenter.X, bounds.Position.X + radius, Mathf.Max(bounds.Position.X + radius, bounds.End.X - radius)),
            Mathf.Clamp(bodyCenter.Y, bounds.Position.Y + radius, Mathf.Max(bounds.Position.Y + radius, bounds.End.Y - radius)));
        _curlTo = new Vector2[n];
        var start = -Mathf.Pi / 2; // Head on top of the ring, facing right
        for (var i = 0; i < n; i++)
            _curlTo[i] = center + Vector2.Right.Rotated(start - i * step) * radius;
        _curled = true;
        _curlT = 0;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt * (Crawling ? 1f : 0.45f);
        if (_t < 1f)
            _t = Mathf.Min(1f, _t + dt / _duration);
        _eatTimer -= dt;
        for (var i = _bulges.Count - 1; i >= 0; i--)
        {
            _bulges[i] += dt * BulgeSpeed;
            if (_bulges[i] > Count + 1)
                _bulges.RemoveAt(i);
        }
        if (_rainbow >= 0)
        {
            _rainbow += dt * RainbowSpeed;
            if (_rainbow > Count + 6)
                _rainbow = -1f;
        }
        if (_bump >= 0)
        {
            _bump += dt / BumpTime;
            if (_bump >= 1f)
                _bump = -1f;
        }
        _over = Mathf.MoveToward(_over, OverBody ? 1f : 0f, dt * 6f);
        if (_curled)
            _curlT = Mathf.Min(1f, _curlT + dt / 0.7f);
        QueueRedraw();
    }

    private Vector2 PieceAt(int i)
    {
        if (_curled && i < _curlTo.Length)
        {
            var k = Ease(_curlT);
            return _curlFrom[i].Lerp(_curlTo[i], k);
        }
        return _from[i].Lerp(_to[i], _t);
    }

    private static float Ease(float t) => 1 - (1 - t) * (1 - t) * (1 - t);

    public override void _Draw()
    {
        var n = Count;
        if (n == 0)
            return;
        var pos = new Vector2[n];
        for (var i = 0; i < n; i++)
            pos[i] = PieceAt(i);

        // Direction of each piece: toward the piece in front (head: away from the next piece)
        var angles = new float[n];
        var last = 0f;
        for (var i = n - 1; i >= 0; i--)
        {
            var d = i == 0 ? pos[0] - (n > 1 ? pos[1] : pos[0] - Vector2.Right) : pos[i - 1] - pos[i];
            if (d.LengthSquared() > 1f)
                last = d.Angle();
            angles[i] = last;
        }
        // Stacked pieces (just grown) take their neighbor's direction
        for (var i = 1; i < n; i++)
            if ((pos[i - 1] - pos[i]).LengthSquared() <= 1f)
                angles[i] = angles[i - 1];
        if (n > 1 && (pos[0] - pos[1]).LengthSquared() <= 1f)
            angles[0] = angles[1];

        // Tail
        if (n > 1)
        {
            var tailSize = new Vector2(_cell * 1.15f, _cell * 1.15f * _tailTex.GetHeight() / _tailTex.GetWidth());
            DrawPiece(_tailTex, pos[n - 1], angles[n - 1], tailSize, n - 1, false);
        }
        for (var i = n - 2; i >= 1; i--)
            DrawPiece(_bodyTex, pos[i], angles[i], new Vector2(_cell, _cell) * SegmentScale, i, false);

        // Head (bump: forward into the wall and back)
        var head = pos[0];
        if (_bump >= 0)
            head += _bumpDir * _cell * 0.22f * Mathf.Sin(_bump * Mathf.Tau) * (1 - _bump);
        if (_over > 0)
        {
            var shadow = _cell * 0.45f;
            DrawSetTransform(head + new Vector2(0, _cell * 0.18f), 0, new Vector2(1f, 0.55f));
            DrawCircle(Vector2.Zero, shadow, new Color(0, 0, 0, 0.18f * _over));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        var tex = _eatTimer > 0 || MouthOpen || _curled ? _headEatTex : _headTex;
        var headSize = new Vector2(_cell, _cell) * HeadScale * (1f + 0.12f * _over);
        var back = Vector2.Right.Rotated(angles[0]) * _cell * HeadBack;
        // The face stays upright: it looks left or right (mirrored, never upside down) and only tilts toward up / down
        var cos = Mathf.Cos(angles[0]);
        if (cos > 0.3f)
            _faceLeft = false;
        else if (cos < -0.3f)
            _faceLeft = true;
        var drawn = _faceLeft
            ? Mathf.Pi + Mathf.Clamp(Mathf.AngleDifference(Mathf.Pi, angles[0]), -MaxHeadTilt, MaxHeadTilt)
            : Mathf.Clamp(Mathf.AngleDifference(0f, angles[0]), -MaxHeadTilt, MaxHeadTilt);
        DrawPiece(tex, head - back, drawn, headSize, 0, true, _faceLeft);
    }

    private void DrawPiece(Texture2D tex, Vector2 at, float angle, Vector2 size, int index, bool isHead, bool? mirror = null)
    {
        // Crawl wave: each piece stretches and squeezes a moment after the one in front
        var wave = Mathf.Sin(_time * 11f - index * 0.9f);
        var stretch = isHead ? 1f + 0.03f * wave : 1f + 0.07f * wave;
        var squeeze = isHead ? 1f - 0.02f * wave : 1f - 0.05f * wave;
        var bulge = 0f;
        foreach (var b in _bulges)
            bulge += 0.32f * Mathf.Exp(-(index - b) * (index - b) / 0.9f);
        var scale = new Vector2(stretch, squeeze) * (1f + bulge);
        if (isHead && _bump >= 0)
            scale *= new Vector2(1f - 0.15f * Mathf.Sin(_bump * Mathf.Pi), 1f + 0.1f * Mathf.Sin(_bump * Mathf.Pi));
        // Pieces going left are mirrored instead of turned upside down (the shine stays on top)
        if (mirror ?? Mathf.Cos(angle) < -0.01f)
            scale.Y = -scale.Y;

        DrawSetTransform(at, angle, scale);
        DrawTextureRect(tex, new Rect2(-size / 2, size), false);
        if (_rainbow >= 0 && !isHead)
        {
            // Rainbow wave: a bright color glaze passing from head to tail
            var w = Mathf.Exp(-(index - _rainbow) * (index - _rainbow) / 8f);
            var hue = Mathf.PosMod(index * 0.09f + _time * 0.5f, 1f);
            if (w > 0.02f)
                DrawCircle(Vector2.Zero, Mathf.Min(size.X, size.Y) * 0.42f, Color.FromHsv(hue, 0.75f, 1f, 0.6f * w));
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    public static Vector2 Dir(MazeDir dir) => dir switch
    {
        MazeDir.Up => Vector2.Up,
        MazeDir.Right => Vector2.Right,
        MazeDir.Down => Vector2.Down,
        _ => Vector2.Left,
    };
}
