using ToyBox.Core;
using Xunit;

namespace ToyBox.Tests;

public class MazeRulesTests
{
    public static IEnumerable<object[]> AllMazes()
    {
        foreach (var level in Enum.GetValues<MazeLevel>())
            for (var i = 0; i < MazeRules.MazeCount(level); i++)
                yield return new object[] { level, i };
    }

    [Fact]
    public void VariantsAndSizesFollowGdd()
    {
        Assert.Equal(new[] { "easy", "normal", "hard" }, MazeRules.Variants.Select(v => v.Id));
        Assert.Equal("normal", MazeRules.VariantId(MazeLevel.Normal));
        Assert.Equal(5, MazeRules.Size(MazeLevel.Easy));
        Assert.Equal(7, MazeRules.Size(MazeLevel.Normal));
        Assert.Equal(10, MazeRules.Size(MazeLevel.Hard));
        foreach (var level in Enum.GetValues<MazeLevel>())
            Assert.Equal(5, MazeRules.MazeCount(level));
    }

    [Theory]
    [MemberData(nameof(AllMazes))]
    public void EveryCellIsReachableAndTheExitIsAtTheFarCorner(MazeLevel level, int index)
    {
        var maze = MazeRules.Get(level, index);
        var size = MazeRules.Size(level);
        Assert.Equal(size, maze.Width);
        Assert.Equal(size, maze.Height);
        Assert.Equal(new MazeCell(0, 0), maze.Start);
        Assert.Equal(new MazeCell(size - 1, size - 1), maze.Exit);
        var dist = maze.Distances(maze.Start);
        foreach (var c in maze.Cells())
            Assert.True(dist[c.X, c.Y] >= 0, $"{level} #{index}: {c} unreachable");
    }

    [Theory]
    [MemberData(nameof(AllMazes))]
    public void ShortestWayOutIsLongEnoughAndWalkable(MazeLevel level, int index)
    {
        var maze = MazeRules.Get(level, index);
        var path = maze.ShortestPath(maze.Start, maze.Exit);
        Assert.Equal(maze.Start, path[0]);
        Assert.Equal(maze.Exit, path[^1]);
        Assert.True(path.Count - 1 >= MazeRules.MinPathSteps(level), $"{level} #{index}: only {path.Count - 1} steps");

        // Walking the solver's directions escapes without a bump
        var run = new MazeRun(maze);
        foreach (var dir in Maze.Directions(path))
            Assert.Equal(MazeMoveKind.Moved, run.Move(dir).Kind);
        Assert.True(run.Escaped);
        Assert.Equal(0, run.Bumps);
    }

    [Theory]
    [MemberData(nameof(AllMazes))]
    public void DeadEndsFitTheLevel(MazeLevel level, int index)
    {
        var (min, max) = MazeRules.DeadEndRange(level);
        var deadEnds = MazeRules.Get(level, index).DeadEnds();
        Assert.InRange(deadEnds, min, max);
    }

    [Fact]
    public void HarderLevelsHaveMoreDeadEnds()
    {
        double Average(MazeLevel level) =>
            Enumerable.Range(0, MazeRules.MazeCount(level)).Average(i => MazeRules.Get(level, i).DeadEnds());
        Assert.True(Average(MazeLevel.Easy) < Average(MazeLevel.Normal));
        Assert.True(Average(MazeLevel.Normal) < Average(MazeLevel.Hard));
    }

    [Theory]
    [MemberData(nameof(AllMazes))]
    public void StarsAreReachableAndOffEveryShortestWay(MazeLevel level, int index)
    {
        var maze = MazeRules.Get(level, index);
        Assert.Equal(MazeRules.StarCount, maze.Stars.Count);
        Assert.Equal(maze.Stars.Count, maze.Stars.Distinct().Count());
        var fromStart = maze.Distances(maze.Start);
        var fromExit = maze.Distances(maze.Exit);
        var shortest = fromStart[maze.Exit.X, maze.Exit.Y];
        foreach (var star in maze.Stars)
        {
            Assert.NotEqual(maze.Start, star);
            Assert.NotEqual(maze.Exit, star);
            Assert.True(maze.Distances(maze.Start, maze.Exit)[star.X, star.Y] > 0, $"{level} #{index}: {star} only past the exit");
            // A side trip is needed: the star is on no shortest way from the start to the exit
            Assert.True(fromStart[star.X, star.Y] + fromExit[star.X, star.Y] > shortest, $"{level} #{index}: {star} on the way");
        }
    }

    [Theory]
    [MemberData(nameof(AllMazes))]
    public void CollectingEveryStarThenEscapingWorks(MazeLevel level, int index)
    {
        var maze = MazeRules.Get(level, index);
        var run = new MazeRun(maze);
        var picked = new List<int>();
        foreach (var target in maze.Stars.Append(maze.Exit))
            foreach (var dir in Maze.Directions(maze.ShortestPath(run.Position, target, target == maze.Exit ? null : maze.Exit)))
            {
                var step = run.Move(dir);
                Assert.Equal(MazeMoveKind.Moved, step.Kind);
                if (step.Star >= 0)
                    picked.Add(step.Star);
            }
        Assert.True(run.Escaped);
        Assert.Equal(MazeRules.StarCount, run.Stars);
        Assert.Equal(new[] { 0, 1, 2 }, picked.Order());
    }

    [Fact]
    public void SameSeedGivesTheSameMaze()
    {
        foreach (var level in Enum.GetValues<MazeLevel>())
            Assert.Equal(MazeRules.Get(level, 2).ToAscii(), MazeRules.Get(level, 2).ToAscii());
        Assert.NotEqual(MazeRules.Get(MazeLevel.Hard, 0).ToAscii(), MazeRules.Get(MazeLevel.Hard, 1).ToAscii());
    }

    [Fact]
    public void FirstEasyMazeIsPinned()
    {
        // Guards against accidental generator changes: the curated mazes must not silently change
        Assert.Equal(
            "###########\n" +
            "#P  #     #\n" +
            "### # ### #\n" +
            "#   #     #\n" +
            "# ### # ###\n" +
            "#    *#  *#\n" +
            "# ####### #\n" +
            "# #  *    #\n" +
            "# # ##### #\n" +
            "#        E#\n" +
            "###########\n",
            MazeRules.Get(MazeLevel.Easy, 0).ToAscii());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(7, 2)]
    [InlineData(-1, 4)]
    public void IndexWrapsAround(int index, int wrapped) => Assert.Equal(wrapped, MazeRules.Wrap(MazeLevel.Easy, index));

    [Fact]
    public void WallBumpCostsNothingAndClockStartsWithTheFirstMove()
    {
        var maze = MazeRules.Get(MazeLevel.Easy, 0);
        var run = new MazeRun(maze);
        run.AddTime(5);
        Assert.Equal(0, run.TimeMs); // Looking at the maze before moving is free
        Assert.False(maze.CanMove(maze.Start, MazeDir.Up)); // Outer wall
        var bump = run.Move(MazeDir.Up);
        Assert.Equal(MazeMoveKind.Bumped, bump.Kind);
        Assert.Equal(maze.Start, run.Position);
        Assert.Equal(1, run.Bumps);
        Assert.Equal(0, run.Moves);
        Assert.True(run.Started);
        run.AddTime(1.25);
        Assert.Equal(1250, run.TimeMs);
    }

    [Fact]
    public void ClockStopsAtTheExitAndEntryHoldsStarsAndTime()
    {
        var maze = MazeRules.Get(MazeLevel.Normal, 0);
        var run = new MazeRun(maze);
        foreach (var dir in Maze.Directions(maze.ShortestPath(maze.Start, maze.Exit)))
        {
            run.Move(dir);
            run.AddTime(0.5);
        }
        Assert.True(run.Escaped);
        var time = run.TimeMs;
        run.AddTime(10);
        Assert.Equal(time, run.TimeMs);
        Assert.Equal(MazeMoveKind.Ignored, run.Move(MazeDir.Left).Kind);
        var entry = run.Entry("다온", 99);
        Assert.Equal(run.Stars, entry.Score);
        Assert.Equal(time, entry.TimeMs);
    }

    [Fact]
    public void MoreStarsRankFirstThenFasterTime()
    {
        var board = new Leaderboard(RankOrder.ScoreDescTimeAsc);
        board.Add(new ScoreEntry("a", 2, 10_000, 1));
        board.Add(new ScoreEntry("b", 3, 40_000, 2));
        board.Add(new ScoreEntry("c", 3, 30_000, 3));
        Assert.Equal(new[] { "c", "b", "a" }, board.Entries.Select(e => e.Name));
    }

    [Fact]
    public void FormatRecordShowsStarsThenTime() => Assert.Equal("별 3 · 25.3초", MazeRules.FormatRecord(3, 25_300));

    [Fact]
    public void OpenAndCanMoveAreSymmetric()
    {
        var maze = new Maze(3, 3);
        Assert.False(maze.CanMove(new MazeCell(1, 1), MazeDir.Left));
        maze.Open(new MazeCell(1, 1), MazeDir.Left);
        Assert.True(maze.CanMove(new MazeCell(1, 1), MazeDir.Left));
        Assert.True(maze.CanMove(new MazeCell(0, 1), MazeDir.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => maze.Open(new MazeCell(0, 0), MazeDir.Up));
    }
}
