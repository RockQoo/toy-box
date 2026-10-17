using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class PushBoxRulesTests
{
    public static IEnumerable<object[]> AllBoards()
    {
        foreach (var level in Enum.GetValues<PushBoxLevel>())
            for (var i = 0; i < PushBoxRules.BoardCount(level); i++)
                yield return new object[] { level, i };
    }

    private static PushBoxRun Run(params string[] rows) => new(PushBoxPuzzle.Parse(rows));

    // ---- The boards ----

    [Fact]
    public void VariantsFollowGdd()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, PushBoxRules.Variants.Select(v => v.Id));
        Assert.Equal("hard", PushBoxRules.VariantId(PushBoxLevel.Hard));
        foreach (var level in Enum.GetValues<PushBoxLevel>())
            Assert.Equal(5, PushBoxRules.BoardCount(level));
        Assert.Equal(0, PushBoxRules.Wrap(PushBoxLevel.Easy, 5));
        Assert.Equal(4, PushBoxRules.Wrap(PushBoxLevel.Easy, -1));
    }

    [Theory]
    [MemberData(nameof(AllBoards))]
    public void BoardShapeFollowsTheTable(PushBoxLevel level, int index)
    {
        var p = PushBoxRules.Get(level, index);
        var (maxW, maxH) = PushBoxRules.MaxSize(level);
        Assert.InRange(p.Width, 4, maxW);
        Assert.InRange(p.Height, 4, maxH);
        var (minBoxes, maxBoxes) = PushBoxRules.BoxRange(level);
        Assert.InRange(p.Boxes.Count, minBoxes, maxBoxes);
        Assert.Equal(p.Boxes.Count, p.Goals.Count);

        // A closed fence all around (drawn as the fence), the rabbit and the boxes on free floor
        for (var x = 0; x < p.Width; x++)
            Assert.True(p.IsWall(new MazeCell(x, 0)) && p.IsWall(new MazeCell(x, p.Height - 1)));
        for (var y = 0; y < p.Height; y++)
            Assert.True(p.IsWall(new MazeCell(0, y)) && p.IsWall(new MazeCell(p.Width - 1, y)));
        Assert.True(p.IsFloor(p.Start));
        Assert.DoesNotContain(p.Start, p.Boxes);
        Assert.Equal(p.Boxes.Count, p.Boxes.Distinct().Count());

        // Every floor cell can be walked to (no hidden pockets)
        var floor = Enumerable.Range(0, p.CellCount).Select(p.CellAt).Where(p.IsFloor).ToList();
        var seen = new HashSet<MazeCell> { p.Start };
        var stack = new Stack<MazeCell>(seen);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            for (var d = 0; d < 4; d++)
                if (c.Step((MazeDir)d) is var n && p.IsFloor(n) && seen.Add(n))
                    stack.Push(n);
        }
        Assert.Equal(floor.Count, seen.Count);
    }

    [Theory]
    [MemberData(nameof(AllBoards))]
    public void NoBoxStartsStuckOrAlreadyHome(PushBoxLevel level, int index)
    {
        var p = PushBoxRules.Get(level, index);
        var run = new PushBoxRun(p);
        Assert.Empty(run.StuckBoxes());
        foreach (var b in p.Boxes)
        {
            Assert.False(p.IsGoal(b), $"{level} #{index}: a box starts on a star spot");
            Assert.False(p.IsDeadCell(b), $"{level} #{index}: a box starts on a dead cell {b}");
            var vertical = p.IsWall(b.Step(MazeDir.Up)) || p.IsWall(b.Step(MazeDir.Down));
            var horizontal = p.IsWall(b.Step(MazeDir.Left)) || p.IsWall(b.Step(MazeDir.Right));
            Assert.False(vertical && horizontal, $"{level} #{index}: a box starts in a corner {b}");
        }
        Assert.False(run.Solved);
    }

    [Theory]
    [MemberData(nameof(AllBoards))]
    public void EveryBoardSolvesWithinThePushRange(PushBoxLevel level, int index)
    {
        var p = PushBoxRules.Get(level, index);
        var pushes = PushBoxSolver.MinPushes(p);
        var (min, max) = PushBoxRules.PushRange(level);
        Assert.InRange(pushes, min, max);

        // The shortest walk (fewest steps) really solves it when played on a run
        var moves = PushBoxSolver.ShortestMoves(p, p.Start, p.Boxes);
        Assert.NotNull(moves);
        var run = new PushBoxRun(p);
        foreach (var dir in moves!)
            Assert.True(run.Move(dir).Changed);
        Assert.True(run.Solved);
        Assert.Equal(moves.Count, run.Steps);
        Assert.True(run.Pushes >= pushes);
        Assert.InRange(moves.Count, pushes, 120);
    }

    [Theory]
    [InlineData(PushBoxLevel.Easy)]
    [InlineData(PushBoxLevel.Normal)]
    [InlineData(PushBoxLevel.Hard)]
    public void BoardsGetHarderInPlayOrder(PushBoxLevel level)
    {
        var pushes = Enumerable.Range(0, PushBoxRules.BoardCount(level))
            .Select(i => PushBoxSolver.MinPushes(PushBoxRules.Get(level, i))).ToList();
        Assert.Equal(pushes.OrderBy(x => x), pushes);
    }

    [Fact]
    public void ParseReadsEveryCharacter()
    {
        var p = PushBoxPuzzle.Parse(new[] { "######", "#+*$.#", "#-  -#", "######" });
        Assert.Equal(new MazeCell(1, 1), p.Start);
        Assert.True(p.IsGoal(new MazeCell(1, 1)));
        Assert.Equal(new[] { new MazeCell(2, 1), new MazeCell(3, 1) }, p.Boxes);
        Assert.Equal(3, p.Goals.Count);
        Assert.True(p.IsWall(new MazeCell(-1, 0)));
        Assert.True(p.IsFloor(new MazeCell(2, 2)));
        Assert.Throws<FormatException>(() => PushBoxPuzzle.Parse(new[] { "###", "#$#", "###" }));
    }

    // ---- Moving and pushing ----

    [Fact]
    public void WalksAndPushesOneBox()
    {
        var run = Run("#######", "#@$-.-#", "#-----#", "#######");
        var step = run.Move(MazeDir.Down);
        Assert.Equal(PushBoxMoveKind.Moved, step.Kind);
        Assert.Equal(new MazeCell(1, 2), run.Player);
        run.Move(MazeDir.Up);
        Assert.True(run.Started);

        step = run.Move(MazeDir.Right);
        Assert.Equal(PushBoxMoveKind.Pushed, step.Kind);
        Assert.Equal(0, step.Box);
        Assert.Equal(new MazeCell(3, 1), step.BoxTo);
        Assert.Equal(new MazeCell(2, 1), run.Player);
        Assert.False(run.IsOnGoal(0));
        run.Move(MazeDir.Right);
        Assert.True(run.IsOnGoal(0));
        Assert.Equal(1, run.BoxesOnGoals);
        Assert.True(run.Solved);
        Assert.Equal(4, run.Steps);
        Assert.Equal(2, run.Pushes);

        // Finished: no more moves, no more undo
        Assert.Equal(PushBoxMoveKind.Ignored, run.Move(MazeDir.Left).Kind);
        Assert.False(run.CanUndo);
    }

    [Fact]
    public void WallsAndTwoBoxesCannotBePushed()
    {
        var run = Run("######", "#@$$.#", "#-$-.#", "#--#.#", "######");
        Assert.Equal(PushBoxMoveKind.Bumped, run.Move(MazeDir.Right).Kind); // Two boxes in a row
        Assert.Equal(PushBoxMoveKind.Bumped, run.Move(MazeDir.Up).Kind);    // The fence
        Assert.Equal(PushBoxMoveKind.Bumped, run.Move(MazeDir.Left).Kind);
        Assert.Equal(new MazeCell(1, 1), run.Player);
        Assert.Equal(0, run.Steps);
        Assert.Equal(3, run.Bumps);
        Assert.False(run.CanUndo);
        Assert.True(run.Started); // Bumping still starts the clock (the child is playing)
    }

    [Fact]
    public void BoxIntoABushIsABump()
    {
        var run = Run("######", "#@$#.#", "#---.#", "######");
        var step = run.Move(MazeDir.Right);
        Assert.Equal(PushBoxMoveKind.Bumped, step.Kind);
        Assert.Equal(new MazeCell(2, 1), run.Boxes[0]);
    }

    // ---- Undo and start over ----

    [Fact]
    public void UndoIsUnlimitedButUndoneStepsStillCount()
    {
        var run = Run("#######", "#@$--.#", "#-----#", "#######");
        run.Move(MazeDir.Right);
        run.Move(MazeDir.Right);
        run.Move(MazeDir.Down);
        Assert.Equal(3, run.Steps);

        var before = run.Undo();
        Assert.NotNull(before);
        Assert.Equal(new MazeCell(3, 2), before!.Player);
        Assert.Equal(new MazeCell(3, 1), run.Player);
        run.Undo();
        Assert.Equal(new MazeCell(2, 1), run.Player);
        Assert.Equal(new MazeCell(3, 1), run.Boxes[0]);
        run.Undo();
        Assert.Equal(new MazeCell(1, 1), run.Player);
        Assert.Equal(new MazeCell(2, 1), run.Boxes[0]);
        Assert.False(run.CanUndo);
        Assert.Null(run.Undo());
        Assert.Equal(3, run.Undos);
        Assert.Equal(3, run.Steps); // Honest record: undone steps are not taken off
    }

    [Fact]
    public void StartOverPutsEverythingBackAndOneUndoBringsItBack()
    {
        var run = Run("#######", "#@$--.#", "#-$--.#", "#-----#", "#######");
        Assert.False(run.CanReset);
        Assert.Null(run.Reset());
        run.Move(MazeDir.Right);
        run.Move(MazeDir.Down);
        Assert.True(run.CanReset);
        var layout = run.Snapshot();

        var before = run.Reset();
        Assert.NotNull(before);
        Assert.Equal(layout.Player, before!.Player);
        Assert.Equal(run.Puzzle.Start, run.Player);
        Assert.Equal(run.Puzzle.Boxes, run.Boxes);
        Assert.False(run.CanReset);

        run.Undo();
        Assert.Equal(layout.Player, run.Player);
        Assert.Equal(layout.Boxes, run.Boxes);
        Assert.Equal(2, run.Steps);
    }

    [Fact]
    public void ClockRunsFromTheFirstMoveToTheFinish()
    {
        var run = Run("#####", "#@$.#", "#####");
        run.AddTime(1.0);
        Assert.Equal(0, run.TimeMs);
        run.Move(MazeDir.Right);
        Assert.True(run.Solved);
        run.AddTime(1.0);
        Assert.Equal(0, run.TimeMs);

        run = Run("######", "#@$-.#", "######");
        run.Move(MazeDir.Right);
        run.AddTime(1.25);
        run.Move(MazeDir.Right);
        run.AddTime(5);
        Assert.Equal(1250, run.TimeMs);
        var entry = run.Entry("다온", 100);
        Assert.Equal(2, entry.Score);
        Assert.Equal(1250, entry.TimeMs);
        Assert.Equal("1.3초 · 2걸음", PushBoxRules.FormatRecord(1250, 2));
    }

    // ---- Stuck boxes ----

    [Fact]
    public void ABoxInACornerIsFrozen()
    {
        // Along the top fence (no star spot there): it can still slide, but never reach a star spot
        var run = Run("######", "#--$@#", "#----#", "#---.#", "######");
        Assert.Equal(new[] { new PushBoxStuck(0, false) }, run.StuckBoxes());
        run.Move(MazeDir.Left);
        run.Move(MazeDir.Left);
        Assert.Equal(new MazeCell(1, 1), run.Boxes[0]);
        Assert.Equal(new[] { new PushBoxStuck(0, true) }, run.StuckBoxes());

        // Pushed down onto the bottom row, which has the star spot: fine
        run = Run("######", "#----#", "#-$@-#", "#---.#", "######");
        run.Move(MazeDir.Up);
        run.Move(MazeDir.Left);
        run.Move(MazeDir.Down);
        Assert.Equal(new MazeCell(2, 3), run.Boxes[0]);
        Assert.Empty(run.StuckBoxes());
    }

    [Fact]
    public void CornersAndGoallessWallLinesAreDead()
    {
        var p = PushBoxPuzzle.Parse(new[] { "######", "#----#", "#-$-@#", "#---.#", "######" });
        Assert.True(p.IsDeadCell(new MazeCell(1, 1)));  // Corners
        Assert.True(p.IsDeadCell(new MazeCell(4, 1)));
        Assert.True(p.IsDeadCell(new MazeCell(1, 3)));
        Assert.True(p.IsDeadCell(new MazeCell(2, 1)));  // Top wall: no star spot along it
        Assert.True(p.IsDeadCell(new MazeCell(1, 2)));  // Left wall: no star spot along it
        Assert.False(p.IsDeadCell(new MazeCell(2, 3))); // Bottom wall has the star spot
        Assert.False(p.IsDeadCell(new MazeCell(4, 2))); // Right wall has it too
        Assert.False(p.IsDeadCell(new MazeCell(2, 2)));
        Assert.False(p.IsDeadCell(new MazeCell(4, 3))); // The star spot itself (even in a corner)
    }

    [Fact]
    public void StuckBoxesAreReportedOnlyOffTheStarSpots()
    {
        // Pushed up against the top wall (no star spot there): stuck but still movable sideways
        var run = Run("######", "#----#", "#-$--#", "#-@-.#", "######");
        run.Move(MazeDir.Up);
        var stuck = Assert.Single(run.StuckBoxes());
        Assert.Equal(0, stuck.Box);
        Assert.False(stuck.Frozen);
        // Then into the corner: frozen
        run.Move(MazeDir.Right);
        run.Move(MazeDir.Up);
        run.Move(MazeDir.Left);
        Assert.Equal(new MazeCell(1, 1), run.Boxes[0]);
        Assert.True(Assert.Single(run.StuckBoxes()).Frozen);
        // Undo frees it again
        run.Undo();
        Assert.False(Assert.Single(run.StuckBoxes()).Frozen);

        // Asking first changes nothing
        run = Run("######", "#----#", "#-$--#", "#-@-.#", "######");
        Assert.True(run.PushWouldStick(MazeDir.Up));
        Assert.False(run.PushWouldStick(MazeDir.Right));
        Assert.Equal(new MazeCell(2, 2), run.Boxes[0]);
        Assert.Equal(0, run.Steps);

        // A box resting on a star spot in a corner is fine
        run = Run("#####", "#*--#", "#-$.#", "#@--#", "#####");
        Assert.Empty(run.StuckBoxes());
    }

    [Fact]
    public void TwoBoxesSideBySideAgainstAWallAreJammed()
    {
        // Pushing the lower box up next to the other one along the top wall: both jam (2 x 2 of boxes and fence)
        var run = Run("#######", "#-$-.-#", "#--$--#", "#--@.-#", "#-----#", "#######");
        Assert.Empty(run.StuckBoxes());
        run.Move(MazeDir.Up);
        Assert.Equal(new MazeCell(3, 1), run.Boxes[1]);
        var stuck = run.StuckBoxes();
        Assert.Equal(2, stuck.Count);
        Assert.All(stuck, s => Assert.True(s.Frozen));

        // In the open, a 2 x 2 of four boxes is jammed too
        run = Run("########", "#------#", "#-$$---#", "#-$--$@#", "#-....-#", "########");
        run.Move(MazeDir.Left);
        Assert.Empty(run.StuckBoxes());
        run.Move(MazeDir.Left);
        Assert.Equal(new MazeCell(3, 3), run.Boxes[3]);
        stuck = run.StuckBoxes();
        Assert.Equal(4, stuck.Count);
        Assert.All(stuck, s => Assert.True(s.Frozen));
    }

    // ---- Solver ----

    [Fact]
    public void SolverFindsTheShortestWalk()
    {
        var p = PushBoxPuzzle.Parse(new[] { "#######", "#@-$-.#", "#######" });
        Assert.Equal(new[] { MazeDir.Right, MazeDir.Right, MazeDir.Right }, PushBoxSolver.ShortestMoves(p, p.Start, p.Boxes));
        Assert.Equal(2, PushBoxSolver.MinPushes(p));

        // Has to walk around the box to push it the other way
        p = PushBoxPuzzle.Parse(new[] { "#######", "#.-$-@#", "#-----#", "#######" });
        var moves = PushBoxSolver.ShortestMoves(p, p.Start, p.Boxes)!;
        Assert.Equal(3, moves.Count);
        Assert.Equal(2, PushBoxSolver.MinPushes(p));

        // Cannot be solved: the box is in a corner
        p = PushBoxPuzzle.Parse(new[] { "#####", "#$-.#", "#-@-#", "#####" });
        Assert.Null(PushBoxSolver.ShortestMoves(p, p.Start, p.Boxes));
        Assert.Equal(-1, PushBoxSolver.MinPushes(p));
    }
}
