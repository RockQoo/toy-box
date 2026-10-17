using System;
using System.Collections.Generic;
using Godot;

namespace ToyBox.Common;

public enum SceneryKind { Sky, Park, Space, Sea }

/// <summary>
/// Animated full-screen background shared by the games. Park uses the painted sky image; the others are drawn in code
/// (gradient + drifting clouds / twinkling stars / rising bubbles) until dedicated art exists.
/// </summary>
public partial class Scenery : Node2D
{
    public static readonly IReadOnlyList<Choice> Choices = new[]
    {
        new Choice("하늘", Swatch: new Color(0.45f, 0.78f, 1f)),
        new Choice("공원", Swatch: new Color(0.45f, 0.82f, 0.35f)),
        new Choice("우주", Swatch: new Color(0.16f, 0.14f, 0.40f)),
        new Choice("바다", Swatch: new Color(0.10f, 0.55f, 0.85f)),
    };

    private struct Floater
    {
        public Vector2 Position;
        public float Speed;
        public float Size;
        public float Phase;
    }

    private readonly Random _rng = new(7);
    private SceneryKind _kind;
    private Floater[] _floaters = Array.Empty<Floater>();
    private Texture2D _cloud = null!;
    private Texture2D _park = null!;
    private float _time;

    /// <summary>Cloud drift in pixels per second (sky, park). Negative drifts left, e.g. as a far parallax layer in 공룡 점프.</summary>
    public float CloudDrift { get; set; } = 14f;

    public SceneryKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Scatter();
        }
    }

    private Vector2 ScreenSize => GetViewportRect().Size;

    public override void _Ready()
    {
        ZIndex = -100;
        _cloud = Ui.Tex("res://assets/sprites/dino/cloud.png");
        _park = Ui.Tex("res://assets/backgrounds/sky.png");
        GetViewport().SizeChanged += Scatter;
        Scatter();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Scatter;

    private void Scatter()
    {
        if (!IsInsideTree())
            return;
        var size = ScreenSize;
        var count = _kind switch { SceneryKind.Sky => 7, SceneryKind.Park => 3, SceneryKind.Space => 90, _ => 28 };
        _floaters = new Floater[count];
        for (var i = 0; i < count; i++)
        {
            _floaters[i] = new Floater
            {
                Position = new Vector2((float)_rng.NextDouble() * size.X, (float)_rng.NextDouble() * size.Y),
                Speed = 0.5f + (float)_rng.NextDouble(),
                Size = (float)_rng.NextDouble(),
                Phase = (float)_rng.NextDouble() * Mathf.Tau,
            };
            if (_kind is SceneryKind.Sky or SceneryKind.Park)
                _floaters[i].Position.Y = (float)_rng.NextDouble() * size.Y * (_kind == SceneryKind.Park ? 0.35f : 0.75f);
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _time += dt;
        var size = ScreenSize;
        for (var i = 0; i < _floaters.Length; i++)
        {
            ref var f = ref _floaters[i];
            switch (_kind)
            {
                case SceneryKind.Sky:
                case SceneryKind.Park:
                    f.Position.X += f.Speed * CloudDrift * dt;
                    if (f.Position.X > size.X + 150)
                        f.Position.X = -150;
                    else if (f.Position.X < -150)
                        f.Position.X = size.X + 150;
                    break;
                case SceneryKind.Sea:
                    f.Position.Y -= f.Speed * 45f * dt;
                    if (f.Position.Y < -20)
                    {
                        f.Position.Y = size.Y + 20;
                        f.Position.X = (float)_rng.NextDouble() * size.X;
                    }
                    break;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = ScreenSize;
        switch (_kind)
        {
            case SceneryKind.Sky: DrawSky(size); break;
            case SceneryKind.Park: DrawPark(size); break;
            case SceneryKind.Space: DrawSpace(size); break;
            case SceneryKind.Sea: DrawSea(size); break;
        }
    }

    private void DrawSky(Vector2 size)
    {
        Gradient(size, new Color(0.30f, 0.66f, 0.98f), new Color(0.78f, 0.93f, 1f));
        var sun = new Vector2(size.X - 140, 110);
        for (var i = 4; i >= 1; i--)
            DrawCircle(sun, 50 + i * 26, new Color(1f, 0.95f, 0.6f, 0.08f), antialiased: true);
        DrawCircle(sun, 56, new Color(1f, 0.88f, 0.35f), antialiased: true);
        DrawClouds(1f);
    }

    private void DrawPark(Vector2 size)
    {
        var tex = _park.GetSize();
        var scale = Mathf.Max(size.X / tex.X, size.Y / tex.Y);
        var drawn = tex * scale;
        DrawTextureRect(_park, new Rect2((size - drawn) / 2, drawn), false);
        DrawClouds(0.8f);
    }

    private void DrawClouds(float alpha)
    {
        var tex = _cloud.GetSize();
        foreach (var f in _floaters)
        {
            var w = 140 + f.Size * 160;
            var h = w * tex.Y / tex.X;
            DrawTextureRect(_cloud, new Rect2(f.Position - new Vector2(w, h) / 2, new Vector2(w, h)), false,
                new Color(1, 1, 1, alpha * (0.75f + 0.25f * f.Size)));
        }
    }

    private void DrawSpace(Vector2 size)
    {
        Gradient(size, new Color(0.06f, 0.05f, 0.20f), new Color(0.24f, 0.14f, 0.42f));
        foreach (var f in _floaters)
        {
            var twinkle = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(_time * (1.5f + f.Speed * 2) + f.Phase));
            var r = 1.5f + f.Size * 2.5f;
            DrawCircle(f.Position, r, new Color(1, 1, 0.9f, twinkle), antialiased: true);
            if (f.Size > 0.85f)
                DrawCircle(f.Position, r * 3, new Color(1, 1, 0.8f, twinkle * 0.15f), antialiased: true);
        }
        // A ringed planet and a small moon
        var planet = new Vector2(size.X * 0.82f, size.Y * 0.30f);
        DrawCircle(planet, 70, new Color(1f, 0.62f, 0.42f), antialiased: true);
        DrawCircle(planet + new Vector2(-18, -16), 46, new Color(1f, 0.72f, 0.52f), antialiased: true);
        DrawSetTransform(planet, -0.35f, new Vector2(1, 0.28f));
        DrawArc(Vector2.Zero, 118, Mathf.Pi * 1.02f, Mathf.Pi * 1.98f, 48, new Color(1f, 0.88f, 0.6f), 14, true);
        DrawArc(Vector2.Zero, 118, -Mathf.Pi * 0.02f, Mathf.Pi * 1.02f, 48, new Color(1f, 0.88f, 0.6f), 14, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawCircle(new Vector2(size.X * 0.14f, size.Y * 0.22f), 34, new Color(0.92f, 0.92f, 1f), antialiased: true);
        DrawCircle(new Vector2(size.X * 0.14f + 10, size.Y * 0.22f - 6), 8, new Color(0.8f, 0.8f, 0.92f), antialiased: true);
    }

    private void DrawSea(Vector2 size)
    {
        Gradient(size, new Color(0.35f, 0.82f, 0.95f), new Color(0.05f, 0.32f, 0.62f));
        // Light rays swaying from the surface
        for (var i = 0; i < 5; i++)
        {
            var x = size.X * (0.1f + i * 0.2f) + Mathf.Sin(_time * 0.4f + i) * 40;
            DrawColoredPolygon(new[]
            {
                new Vector2(x - 30, 0), new Vector2(x + 30, 0),
                new Vector2(x + 140, size.Y), new Vector2(x + 40, size.Y),
            }, new Color(1, 1, 1, 0.07f));
        }
        // Sand and swaying seaweed
        DrawColoredPolygon(new[]
        {
            new Vector2(0, size.Y - 50), new Vector2(size.X * 0.3f, size.Y - 70), new Vector2(size.X * 0.7f, size.Y - 45),
            new Vector2(size.X, size.Y - 65), new Vector2(size.X, size.Y), new Vector2(0, size.Y),
        }, new Color(0.96f, 0.85f, 0.55f));
        for (var i = 0; i < 9; i++)
        {
            var baseX = size.X * (0.05f + i * 0.115f);
            var points = new Vector2[8];
            var height = 90 + (i * 37 % 70);
            for (var k = 0; k < points.Length; k++)
            {
                var u = k / (float)(points.Length - 1);
                points[k] = new Vector2(baseX + Mathf.Sin(_time * 1.2f + i + u * 3) * 14 * u, size.Y - 55 - u * height);
            }
            DrawPolyline(points, new Color(0.18f, 0.62f, 0.36f), 10, true);
        }
        foreach (var f in _floaters)
        {
            var r = 4 + f.Size * 9;
            var p = f.Position + new Vector2(Mathf.Sin(_time * 2 + f.Phase) * 6, 0);
            DrawArc(p, r, 0, Mathf.Tau, 20, new Color(1, 1, 1, 0.55f), 2, true);
            DrawCircle(p + new Vector2(-r * 0.35f, -r * 0.35f), r * 0.25f, new Color(1, 1, 1, 0.6f), antialiased: true);
        }
    }

    private void Gradient(Vector2 size, Color top, Color bottom)
    {
        DrawPolygon(
            new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) },
            new[] { top, top, bottom, bottom });
    }
}
