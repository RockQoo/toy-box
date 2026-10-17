using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>
/// One separately ranked setting of a game, e.g. the quiz's 쉬움/보통/어려움. Results from different variants are not
/// comparable (2 choices vs 6), so each variant keeps its own top 10.
/// </summary>
public sealed record GameVariant(string Id, string Label);

/// <summary>
/// Leaderboard keys in records.json. A game without variants uses its plain id ("balloon"); a variant board is
/// "&lt;gameId&gt;:&lt;variantId&gt;" ("quiz:hard"), so older files keep loading unchanged.
/// </summary>
public static class BoardKey
{
    public const char Separator = ':';

    public static string Of(string gameId, string? variantId) =>
        string.IsNullOrEmpty(variantId) ? gameId : $"{gameId}{Separator}{variantId}";

    /// <summary>The game id part of a key ("quiz:hard" → "quiz", "balloon" → "balloon").</summary>
    public static string GameId(string key)
    {
        var i = key.IndexOf(Separator);
        return i < 0 ? key : key[..i];
    }

    /// <summary>The variant part of a key, or null for a plain game key.</summary>
    public static string? VariantId(string key)
    {
        var i = key.IndexOf(Separator);
        return i < 0 ? null : key[(i + 1)..];
    }

    /// <summary>
    /// The variant whose best record stands for the game (main menu card, default records tab): the hardest one that
    /// has a record, where variants are listed easiest first. Null when there are no variants or no records yet.
    /// </summary>
    public static GameVariant? Featured(IReadOnlyList<GameVariant> variants, Func<string, bool> hasRecord)
    {
        for (var i = variants.Count - 1; i >= 0; i--)
            if (hasRecord(variants[i].Id))
                return variants[i];
        return null;
    }
}
