using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>Board size. Each size has its own leaderboard (see <see cref="MergeRules.Variants"/>).</summary>
public enum MergeLevel { Easy, Normal }

public enum MergeDir { Up, Right, Down, Left }

/// <summary>One tile's slide in a move: from a cell to a cell. <see cref="Merged"/> tiles end on a cell shared with another.</summary>
public readonly record struct MergeSlide(int FromX, int FromY, int ToX, int ToY, int Value, bool Merged);

/// <summary>A tile made by joining two: its cell and new value.</summary>
public readonly record struct MergeJoin(int X, int Y, int Value);

/// <summary>What one push did. Nothing changes on the board when <see cref="Moved"/> is false.</summary>
public sealed class MergeMove
{
    public bool Moved { get; init; }
    public IReadOnlyList<MergeSlide> Slides { get; init; } = Array.Empty<MergeSlide>();
    public IReadOnlyList<MergeJoin> Joins { get; init; } = Array.Empty<MergeJoin>();
    /// <summary>Points from this push: the sum of the joined tiles' new values.</summary>
    public int Gained { get; init; }
}

/// <summary>숫자 합치기 rules that do not need the engine (docs/games/07-merge.md).</summary>
public static class MergeRules
{
    /// <summary>Leaderboard variants, easiest first: a 5x5 and a 4x4 board do not give comparable scores.</summary>
    public static readonly IReadOnlyList<GameVariant> Variants = new[]
    {
        new GameVariant("easy", "쉬움"),
        new GameVariant("normal", "보통"),
    };

    public static string VariantId(MergeLevel level) => Variants[(int)level].Id;

    /// <summary>쉬움 5x5 (more room, lasts longer) / 보통 4x4 (the original).</summary>
    public static int Size(MergeLevel level) => level == MergeLevel.Easy ? 5 : 4;

    /// <summary>Undos per round, so a child can take back a slip.</summary>
    public const int UndoCount = 3;

    /// <summary>A new tile is a 4 this often, else a 2.</summary>
    public const double FourChance = 0.1;

    /// <summary>Tiles worth a celebration the first time they appear in a round (2048 is far away for a child).</summary>
    public static readonly int[] Milestones = { 128, 256, 512, 1024, 2048 };

    /// <summary>Tile art number (merge/tile_01 = 2 … tile_12 = 4096; bigger tiles keep the last one).</summary>
    public static int TileArt(int value)
    {
        var step = 0;
        while (value > 2 && step < 11)
        {
            value >>= 1;
            step++;
        }
        return step + 1;
    }

    public static (int dx, int dy) Delta(MergeDir dir) => dir switch
    {
        MergeDir.Up => (0, -1),
        MergeDir.Right => (1, 0),
        MergeDir.Down => (0, 1),
        _ => (-1, 0),
    };
}

/// <summary>
/// A 숫자 합치기 board. Cells hold 0 (empty) or a tile value, x left to right and y top to bottom.
/// A push slides every tile as far as it goes and joins touching equal tiles, each tile at most once per push
/// (2 2 2 . → 4 2 . .). A new tile appears only after a push that moved something (the game asks for it).
/// </summary>
public sealed class MergeBoard
{
    private readonly int[,] _cells;
    private readonly Stack<(int[,] Cells, int Score)> _history = new();

    public MergeBoard(int size)
    {
        Size = size;
        _cells = new int[size, size];
    }

    public int Size { get; }
    public int Score { get; private set; }
    public int UndosLeft { get; private set; } = MergeRules.UndoCount;
    public bool CanUndo => UndosLeft > 0 && _history.Count > 0;

    public int this[int x, int y]
    {
        get => _cells[x, y];
        set => _cells[x, y] = value;
    }

    public int BiggestTile
    {
        get
        {
            var best = 0;
            foreach (var v in _cells)
                best = Math.Max(best, v);
            return best;
        }
    }

    public int EmptyCount
    {
        get
        {
            var n = 0;
            foreach (var v in _cells)
                if (v == 0)
                    n++;
            return n;
        }
    }

    /// <summary>Puts a 2 (or sometimes a 4) on a random empty cell. Returns where, or null when the board is full.</summary>
    public (int X, int Y, int Value)? Spawn(Random rng)
    {
        var empty = new List<(int, int)>();
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (_cells[x, y] == 0)
                    empty.Add((x, y));
        if (empty.Count == 0)
            return null;
        var (cx, cy) = empty[rng.Next(empty.Count)];
        var value = rng.NextDouble() < MergeRules.FourChance ? 4 : 2;
        _cells[cx, cy] = value;
        return (cx, cy, value);
    }

    /// <summary>Pushes every tile toward <paramref name="dir"/>. Remembers the board for <see cref="Undo"/> when something moved.</summary>
    public MergeMove Push(MergeDir dir)
    {
        var before = (int[,])_cells.Clone();
        var slides = new List<MergeSlide>();
        var joins = new List<MergeJoin>();
        var gained = 0;
        var (dx, dy) = MergeRules.Delta(dir);

        for (var line = 0; line < Size; line++)
        {
            // Cells of this line ordered from the edge the tiles move toward
            var cells = new (int X, int Y)[Size];
            for (var i = 0; i < Size; i++)
            {
                var along = dx + dy > 0 ? Size - 1 - i : i;
                cells[i] = dx != 0 ? (along, line) : (line, along);
            }

            var placed = new int[Size];
            var joined = new bool[Size];
            var next = 0;
            foreach (var (x, y) in cells)
            {
                var value = before[x, y];
                if (value == 0)
                    continue;
                if (next > 0 && placed[next - 1] == value && !joined[next - 1])
                {
                    var (tx, ty) = cells[next - 1];
                    placed[next - 1] = value * 2;
                    joined[next - 1] = true;
                    gained += value * 2;
                    joins.Add(new MergeJoin(tx, ty, value * 2));
                    slides.Add(new MergeSlide(x, y, tx, ty, value, true));
                    // The tile it landed on joined too
                    for (var s = slides.Count - 2; s >= 0; s--)
                        if (slides[s].ToX == tx && slides[s].ToY == ty)
                        {
                            slides[s] = slides[s] with { Merged = true };
                            break;
                        }
                    continue;
                }
                var (px, py) = cells[next];
                placed[next] = value;
                slides.Add(new MergeSlide(x, y, px, py, value, false));
                next++;
            }
            for (var i = 0; i < Size; i++)
                _cells[cells[i].X, cells[i].Y] = placed[i];
        }

        var moved = false;
        foreach (var s in slides)
            if (s.FromX != s.ToX || s.FromY != s.ToY || s.Merged)
                moved = true;
        if (!moved)
            return new MergeMove();

        _history.Push((before, Score));
        Score += gained;
        return new MergeMove { Moved = true, Slides = slides, Joins = joins, Gained = gained };
    }

    /// <summary>True while some push would still move a tile: an empty cell or two equal neighbors.</summary>
    public bool CanMove()
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var v = _cells[x, y];
                if (v == 0)
                    return true;
                if (x + 1 < Size && _cells[x + 1, y] == v)
                    return true;
                if (y + 1 < Size && _cells[x, y + 1] == v)
                    return true;
            }
        return false;
    }

    /// <summary>Goes back to the board (and score) before the last push. Uses one of the round's undos.</summary>
    public bool Undo()
    {
        if (!CanUndo)
            return false;
        var (cells, score) = _history.Pop();
        Array.Copy(cells, _cells, cells.Length);
        Score = score;
        UndosLeft--;
        return true;
    }

    /// <summary>How many cells would be empty after a push (for the autoplay's simple greedy choice).</summary>
    public int EmptyAfter(MergeDir dir)
    {
        var copy = new MergeBoard(Size);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy.Push(dir).Moved ? copy.EmptyCount : -1;
    }
}
