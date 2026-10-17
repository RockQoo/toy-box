using System;
using Godot;

namespace ToyBox.Common;

/// <summary>Shakes a target node by offsetting its position. Strength decays quickly; use small values for a kids' game.</summary>
public partial class ScreenShake : Node
{
    private const float MaxStrength = 14f;
    private const float Decay = 40f;

    private readonly Random _rng = new();
    private Node2D _target = null!;
    private Vector2 _rest;
    private float _strength;

    public static ScreenShake Create(Node2D target) => new() { _target = target, _rest = target.Position };

    public void Add(float amount) => _strength = Mathf.Min(Mathf.Max(_strength, amount), MaxStrength);

    public override void _Process(double delta)
    {
        if (_strength <= 0)
            return;
        _strength = Mathf.MoveToward(_strength, 0, Decay * (float)delta);
        var offset = new Vector2((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 2 - 1)) * _strength;
        _target.Position = _rest + offset;
    }
}
