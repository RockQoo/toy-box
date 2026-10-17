using System;
using System.Collections.Generic;

namespace ToyBox.Core;

/// <summary>One step of the caterpillar speed ramp (docs/games/08-snake.md, "난이도"). Speed is cells per second at "보통".</summary>
public readonly record struct SnakePhase(int Index, float StartTime, float CellsPerSecond);

/// <summary>A leaf on the board. A golden leaf is worth more and goes away after a while.</summary>
public readonly record struct SnakeLeaf(MazeCell Cell, bool Golden);

/// <summary>What one tick of the caterpillar did.</summary>
public enum SnakeMoveKind
{
    /// <summary>Not moving: before the first direction, or stopped at a wall with no new direction yet.</summary>
    Waiting,
    Moved,
    /// <summary>Moved onto a leaf and ate it (<see cref="SnakeStep.Leaf"/>, <see cref="SnakeStep.Points"/>).</summary>
    Ate,
    /// <summary>The next cell is outside the board: it stays put and waits for a new direction. No penalty.</summary>
    Bumped,
}

public readonly record struct SnakeStep(SnakeMoveKind Kind, SnakeLeaf? Leaf = null, int Points = 0, bool Grew = false);

/// <summary>Caterpillar (snake) rules that do not need the engine: board size, speed ramp, leaves, scoring.</summary>
public static class SnakeRules
{
    /// <summary>A wide board for the landscape screen (GDD: 16 x 9).</summary>
    public const int Width = 16;
    public const int Height = 9;

    public const float PlaySeconds = 60f;

    /// <summary>Head plus two body cells at the start.</summary>
    public const int StartLength = 3;

    /// <summary>The body stops growing here (more leaves still score), so the board never fills up.</summary>
    public const int MaxLength = 40;

    /// <summary>Turns typed between two steps that are kept (a quick ㄱ-turn is two).</summary>
    public const int QueueLimit = 2;

    /// <summary>From this time on there are two leaves on the board.</summary>
    public const float SecondLeafTime = 30f;

    /// <summary>Golden leaves appear only after this, every <see cref="GoldenDelay"/> seconds, and last <see cref="GoldenLife"/>.</summary>
    public const float GoldenFromTime = 10f;
    public const float GoldenLife = 6f;

    private static readonly SnakePhase[] Phases =
    {
        new(0, 0f, 4f),   // 0~20초: 초당 4칸
        new(1, 20f, 5f),  // 20~40초: 5칸
        new(2, 40f, 6f),  // 40~60초: 6칸
    };

    public static int PhaseCount => Phases.Length;

    public static SnakePhase PhaseAt(float elapsed)
    {
        var phase = Phases[0];
        foreach (var p in Phases)
            if (elapsed >= p.StartTime)
                phase = p;
        return phase;
    }

    /// <summary>Cells per second at this time with the "속도" option.</summary>
    public static float CellsPerSecond(float elapsed, StartSpeed speed) =>
        PhaseAt(elapsed).CellsPerSecond * RoundClock.SpeedScale(speed);

    /// <summary>Seconds between two steps.</summary>
    public static float StepInterval(float elapsed, StartSpeed speed) => 1f / CellsPerSecond(elapsed, speed);

    /// <summary>How many plain leaves the board keeps: 1, then 2 from 30 seconds.</summary>
    public static int LeafCount(float elapsed) => elapsed >= SecondLeafTime ? 2 : 1;

    /// <summary>Seconds until the next golden leaf. <paramref name="roll"/> is a random number in [0, 1).</summary>
    public static float GoldenDelay(double roll) => 7f + 5f * (float)roll;

    /// <summary>Leaf +1, golden leaf +3.</summary>
    public static int Points(bool golden) => golden ? 3 : 1;

    /// <summary>True when going from <paramref name="before"/> to <paramref name="after"/> passes a multiple of <paramref name="every"/> (a +3 can jump over one).</summary>
    public static bool CrossedMilestone(int before, int after, int every = 10) =>
        every > 0 && after / every > before / every;

    public static MazeDir Opposite(MazeDir dir) => (MazeDir)(((int)dir + 2) % 4);

    public static bool IsOpposite(MazeDir a, MazeDir b) => Opposite(a) == b;
}

/// <summary>
/// The caterpillar game state: the body cells (head first), the facing and the turn queue, the leaves and the score.
/// The caterpillar does not move until the first direction. Walls stop it (it waits for a new direction) and it may
/// crawl over its own body, so nothing ends the round early.
/// </summary>
public sealed class SnakeBoard
{
    private readonly List<MazeCell> _body = new();
    private readonly List<MazeDir> _queue = new();
    private readonly List<SnakeLeaf> _leaves = new();
    private int _grow;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Head first, tail last.</summary>
    public IReadOnlyList<MazeCell> Body => _body;
    public MazeCell Head => _body[0];
    public int Length => _body.Count;

    /// <summary>The direction of the last step (Right at the start: the body lies to the left of the head).</summary>
    public MazeDir Facing { get; private set; } = MazeDir.Right;

    /// <summary>False until the first direction, and again while stopped at a wall.</summary>
    public bool Moving { get; private set; }

    /// <summary>True once the first step has been taken.</summary>
    public bool Started { get; private set; }

    public IReadOnlyList<SnakeLeaf> Leaves => _leaves;
    public IReadOnlyList<MazeDir> Queued => _queue;
    public int Score { get; private set; }
    public int LeavesEaten { get; private set; }
    public int LongestLength { get; private set; }

    public SnakeBoard(int width = SnakeRules.Width, int height = SnakeRules.Height, int length = SnakeRules.StartLength,
        MazeCell? head = null)
    {
        Width = width;
        Height = height;
        var start = head ?? new MazeCell(Math.Max(length - 1, width / 4), height / 2);
        for (var i = 0; i < length; i++)
            _body.Add(new MazeCell(start.X - i, start.Y));
        LongestLength = length;
    }

    public bool InBounds(MazeCell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public bool Occupied(MazeCell c) => _body.Contains(c);

    /// <summary>
    /// Queues a turn. Ignored: a full queue, the reverse of the direction it would be going by then (no turning on the
    /// spot), and repeating that direction while moving. While standing still (before the first step, or stopped at a
    /// wall) any direction goes, so a child pressing "back" at a wall is never stuck. Returns whether it was taken.
    /// </summary>
    public bool Turn(MazeDir dir)
    {
        if (_queue.Count >= SnakeRules.QueueLimit)
            return false;
        var reference = _queue.Count > 0 ? _queue[^1] : Facing;
        if (SnakeRules.IsOpposite(reference, dir) && (_queue.Count > 0 || Moving))
            return false;
        if (dir == reference && (_queue.Count > 0 || Moving))
            return false;
        _queue.Add(dir);
        return true;
    }

    /// <summary>One tick: take the next queued turn, then move one cell (or bump the wall, or keep waiting).</summary>
    public SnakeStep Step()
    {
        if (_queue.Count > 0)
        {
            Facing = _queue[0];
            _queue.RemoveAt(0);
            Moving = true;
            Started = true;
        }
        if (!Moving)
            return new SnakeStep(SnakeMoveKind.Waiting);

        var next = Head.Step(Facing);
        if (!InBounds(next))
        {
            Moving = false;
            return new SnakeStep(SnakeMoveKind.Bumped);
        }

        _body.Insert(0, next);
        var leafIndex = _leaves.FindIndex(l => l.Cell == next);
        SnakeLeaf? eaten = null;
        var points = 0;
        var grew = false;
        if (leafIndex >= 0)
        {
            var leaf = _leaves[leafIndex];
            _leaves.RemoveAt(leafIndex);
            eaten = leaf;
            points = SnakeRules.Points(leaf.Golden);
            Score += points;
            LeavesEaten++;
            if (_body.Count - 1 + _grow < SnakeRules.MaxLength)
                _grow++;
        }
        if (_grow > 0)
        {
            _grow--;
            grew = true;
        }
        else
        {
            _body.RemoveAt(_body.Count - 1);
        }
        LongestLength = Math.Max(LongestLength, _body.Count);
        return eaten is { } ate
            ? new SnakeStep(SnakeMoveKind.Ate, ate, points, grew)
            : new SnakeStep(SnakeMoveKind.Moved, null, 0, grew);
    }

    public int PlainLeafCount => _leaves.FindAll(l => !l.Golden).Count;
    public bool HasGolden => _leaves.Exists(l => l.Golden);

    /// <summary>
    /// Puts a leaf on a random free cell (no body, no other leaf), avoiding the cells right next to the head when it
    /// can. <paramref name="next"/> returns a random int in [0, n). Null if the board is full.
    /// </summary>
    public SnakeLeaf? AddLeaf(bool golden, Func<int, int> next)
    {
        var free = new List<MazeCell>();
        var far = new List<MazeCell>();
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var c = new MazeCell(x, y);
                if (Occupied(c) || _leaves.Exists(l => l.Cell == c))
                    continue;
                free.Add(c);
                if (c.ManhattanTo(Head) >= 3)
                    far.Add(c);
            }
        var pool = far.Count > 0 ? far : free;
        if (pool.Count == 0)
            return null;
        var leaf = new SnakeLeaf(pool[next(pool.Count)], golden);
        _leaves.Add(leaf);
        return leaf;
    }

    /// <summary>Puts a leaf on <paramref name="cell"/> if it is free (no body, no leaf). Returns whether it was placed.</summary>
    public bool AddLeafAt(MazeCell cell, bool golden)
    {
        if (!InBounds(cell) || Occupied(cell) || _leaves.Exists(l => l.Cell == cell))
            return false;
        _leaves.Add(new SnakeLeaf(cell, golden));
        return true;
    }

    /// <summary>Adds plain leaves until there are as many as the rules ask for at this time. Returns the new ones.</summary>
    public List<SnakeLeaf> FillLeaves(float elapsed, Func<int, int> next)
    {
        var added = new List<SnakeLeaf>();
        while (PlainLeafCount < SnakeRules.LeafCount(elapsed) && AddLeaf(false, next) is { } leaf)
            added.Add(leaf);
        return added;
    }

    /// <summary>Takes the golden leaf away (it was not eaten in time). Returns whether there was one.</summary>
    public bool RemoveGolden() => _leaves.RemoveAll(l => l.Golden) > 0;

    /// <summary>
    /// The autoplay's choice: a turn toward <paramref name="target"/> along a shortest path (the body is no obstacle), never
    /// the reverse (a leaf straight behind: step aside first). Null when the current facing already gets closer.
    /// </summary>
    public MazeDir? DirToward(MazeCell target)
    {
        var dx = target.X - Head.X;
        var dy = target.Y - Head.Y;
        var good = new List<MazeDir>();
        if (dx > 0) good.Add(MazeDir.Right);
        if (dx < 0) good.Add(MazeDir.Left);
        if (dy > 0) good.Add(MazeDir.Down);
        if (dy < 0) good.Add(MazeDir.Up);
        if (Moving && good.Contains(Facing))
            return null;
        foreach (var d in good)
            if (!SnakeRules.IsOpposite(Facing, d))
                return d;
        // The target is straight behind: step aside first (toward the roomier side)
        if (good.Count == 0)
            return null;
        var sideways = Facing is MazeDir.Left or MazeDir.Right
            ? (Head.Y < Height / 2 ? MazeDir.Down : MazeDir.Up)
            : (Head.X < Width / 2 ? MazeDir.Right : MazeDir.Left);
        return sideways;
    }
}
