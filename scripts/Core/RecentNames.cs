using System;
using System.Collections.Generic;
using System.Linq;

namespace ToyBox.Core;

/// <summary>
/// The short list of names typed on the result screen, newest first. Shown as one-tap buttons so a child who
/// cannot write yet can still put their name on the ranking.
/// </summary>
public static class RecentNames
{
    public const int Capacity = 3;
    public const int MaxLength = 8;
    public const string DefaultName = "친구";

    /// <summary>Trims and shortens a typed name. Empty input becomes <see cref="DefaultName"/>.</summary>
    public static string Clean(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0)
            return DefaultName;
        return name.Length > MaxLength ? name[..MaxLength] : name;
    }

    /// <summary>Moves <paramref name="name"/> to the front (no duplicates) and keeps at most <see cref="Capacity"/> names.</summary>
    public static string[] Push(IEnumerable<string> current, string name)
    {
        name = Clean(name);
        return new[] { name }
            .Concat(current.Select(n => n.Trim()).Where(n => n.Length > 0 && n != name))
            .Distinct(StringComparer.Ordinal)
            .Take(Capacity)
            .ToArray();
    }

    /// <summary>
    /// The stored list. Settings from before this list existed only have the last-used name, so then the list is
    /// seeded from that name followed by the names on the saved records, newest record first (the default name
    /// "친구" is skipped there). Blank and duplicate entries are dropped.
    /// </summary>
    public static string[] Load(IEnumerable<string>? stored, string? legacyLastName = null,
        IEnumerable<ScoreEntry>? records = null)
    {
        var list = Distinct(stored ?? Array.Empty<string>());
        if (list.Length > 0)
            return list;
        var seed = new List<string>();
        if (!string.IsNullOrWhiteSpace(legacyLastName))
            seed.Add(Clean(legacyLastName));
        if (records is not null)
            seed.AddRange(records.OrderByDescending(r => r.UnixTime).Select(r => r.Name).Where(n => n.Trim() != DefaultName));
        return Distinct(seed);
    }

    private static string[] Distinct(IEnumerable<string> names) =>
        names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Take(Capacity).ToArray();
}
