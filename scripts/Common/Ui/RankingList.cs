using System;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>Top-10 table for one game (or one variant board of it). Rows 1-3 get gold/silver/bronze badges; an optional row is highlighted (a new record).</summary>
public partial class RankingList : VBoxContainer
{
    private const int RowHeight = 46;

    private GameInfo _game = null!;
    private string? _variantId;

    /// <param name="variantId">Which board of a game with variants (see <see cref="GameInfo.Variants"/>).</param>
    public static RankingList Create(GameInfo game, string? variantId = null)
    {
        var list = new RankingList { _game = game, _variantId = variantId };
        list.AddThemeConstantOverride("separation", 5);
        return list;
    }

    /// <summary>Rebuilds the rows. <paramref name="highlight"/> is the zero-based rank to make pulse, or -1.</summary>
    public void Refresh(int highlight = -1)
    {
        foreach (var child in GetChildren())
            child.QueueFree();

        var entries = App.I.Board(_game.Id, _variantId).Entries;
        for (var i = 0; i < Leaderboard.Capacity; i++)
        {
            var row = Row(i, i < entries.Count ? entries[i] : null, i == highlight);
            AddChild(row);
            if (i == highlight)
                HighlightLater(row);
        }
    }

    private Control Row(int rank, ScoreEntry? entry, bool highlight)
    {
        var fill = highlight ? UiTheme.Yellow.Lerp(Colors.White, 0.45f) : rank % 2 == 0 ? Colors.White : UiTheme.Paper;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(fill, highlight ? UiTheme.Orange : UiTheme.PaperDim, highlight ? 4 : 2, 4, 14));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);

        row.AddChild(Badge(rank));

        var name = Ui.Label(entry?.Name ?? "-", 24, entry is null ? UiTheme.InkSoft : UiTheme.Ink);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(name);

        if (entry is not null)
        {
            var value = Ui.Label(_game.FormatValue(entry), 24, UiTheme.Ink);
            value.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(value);

            var date = DateTimeOffset.FromUnixTimeSeconds(entry.UnixTime).ToLocalTime();
            var when = Ui.Label($"{date.Month}/{date.Day}", 18, UiTheme.InkSoft);
            when.CustomMinimumSize = new Vector2(56, 0);
            when.HorizontalAlignment = HorizontalAlignment.Right;
            when.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(when);
        }
        return panel;
    }

    private static Control Badge(int rank)
    {
        var color = rank switch { 0 => UiTheme.Gold, 1 => UiTheme.Silver, 2 => UiTheme.Bronze, _ => UiTheme.PaperDim };
        var badge = new PanelContainer { CustomMinimumSize = new Vector2(38, 38) };
        badge.AddThemeStyleboxOverride("panel", UiTheme.Box(color, rank < 3 ? UiTheme.Ink : UiTheme.PaperDim, rank < 3 ? 3 : 0, 0, 19));
        badge.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var label = Ui.Label($"{rank + 1}", 20, rank < 3 ? Colors.White : UiTheme.InkSoft, rank < 3 ? 5 : 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        badge.AddChild(label);
        return badge;
    }

    private static async void HighlightLater(Control row)
    {
        // Wait one frame so the container has laid the row out (pivot needs its size).
        await row.ToSignal(row.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(row))
            return;
        Ui.PopIn(row, 0.05f, 0.45f);
        var tween = row.CreateTween().SetLoops();
        tween.TweenProperty(row, "self_modulate", new Color(1.15f, 1.1f, 0.9f), 0.5f);
        tween.TweenProperty(row, "self_modulate", Colors.White, 0.5f);
    }
}
