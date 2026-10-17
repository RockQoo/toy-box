using System;
using System.Collections.Generic;
using System.Linq;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>
/// A mini-game as listed on the main menu and in records. <see cref="ScenePath"/> is null until the game is built.
/// <see cref="Variants"/> (easiest first) gives a game one leaderboard per variant, e.g. per difficulty when results
/// from different settings are not comparable. Empty means one board under the plain game id.
/// </summary>
public sealed record GameInfo(
    string Id,
    string Title,
    string IconPath,
    string? ScenePath,
    RankOrder Order,
    string ValueHeader,
    IReadOnlyList<GameVariant>? VariantList = null,
    string? Short = null)
{
    /// <summary>One-word name for the records tabs ("숫자 합치기" → "합치기", so it differs from the quiz's "숫자").</summary>
    public string TabName => Short ?? Title.Split(' ')[0];

    public IReadOnlyList<GameVariant> Variants => VariantList ?? Array.Empty<GameVariant>();
    public bool HasVariants => Variants.Count > 0;

    public string? VariantLabel(string? variantId) => Variants.FirstOrDefault(v => v.Id == variantId)?.Label;

    /// <summary>"숫자 / 색깔 퀴즈 · 보통" for a variant board, the plain title otherwise.</summary>
    public string BoardTitle(string? variantId) =>
        VariantLabel(variantId) is { } label ? $"{Title} · {label}" : Title;

    /// <summary>The variant that stands for this game on the menu card: the hardest one with a record (null if none).</summary>
    public GameVariant? FeaturedVariant() =>
        BoardKey.Featured(Variants, v => App.I.Board(Id, v).Best is not null);

    /// <summary>How a record's value reads in the ranking list.</summary>
    public string FormatValue(ScoreEntry e) => Id switch
    {
        "balloon" => $"{e.Score}개",
        "fruit" => $"{e.Score}개",
        "snake" => $"잎 {e.Score}개",
        "flappy" => $"{e.Score}개",
        "breakout" => $"벽돌 {e.Score}개",
        "match3" => $"과일 {e.Score}개",
        "swipe" => $"벽돌 {e.Score}개",
        "bubble" => BubbleRules.FormatRecord(e.TimeMs, e.Score),
        "blocks" => $"줄 {e.Score}개",
        "sudoku" => SudokuRules.FormatRecord(e.TimeMs),
        "shikaku" => ShikakuRules.FormatRecord(e.TimeMs),
        "pushbox" => PushBoxRules.FormatRecord(e.TimeMs, e.Score),
        "flight" => FlightRules.FormatRecord(e.Score),
        "memory" => MemoryRules.FormatRecord(e.TimeMs, e.Score),
        "maze" => MazeRules.FormatRecord(e.Score, e.TimeMs),
        "quiz" => $"{e.Score}문제 · {FormatTime(e.TimeMs)}",
        _ => $"{e.Score}점",
    };

    public static string FormatTime(long ms) => $"{ms / 1000f:0.0}초";
}

/// <summary>The list of mini-games (GDD order). Add a game here and give it a scene to make it playable from the menu.</summary>
public static class GameCatalog
{
    public static readonly IReadOnlyList<GameInfo> All = new[]
    {
        new GameInfo("balloon", "풍선 터뜨리기", "res://assets/sprites/balloon/red.png",
            "res://scenes/games/Balloon.tscn", RankOrder.ScoreDesc, "터뜨린 풍선"),
        new GameInfo("fruit", "과일 받기", "res://assets/sprites/fruit/apple.png",
            "res://scenes/games/Fruit.tscn", RankOrder.ScoreDesc, "받은 과일"),
        new GameInfo("memory", "동물 짝 맞추기", "res://assets/sprites/animal/panda.png",
            "res://scenes/games/Memory.tscn", RankOrder.TimeAscScoreAsc, "걸린 시간", MemoryRules.Variants),
        new GameInfo("maze", "미로 탈출", "res://assets/sprites/animal/turtle.png",
            "res://scenes/games/Maze.tscn", RankOrder.ScoreDescTimeAsc, "별 · 걸린 시간", MazeRules.Variants),
        new GameInfo("quiz", "숫자 / 색깔 퀴즈", "res://assets/sprites/quiz/num_3.png",
            "res://scenes/games/Quiz.tscn", RankOrder.ScoreDescTimeAsc, "맞힌 문제", QuizRules.Variants),
        new GameInfo("dino", "공룡 점프", "res://assets/sprites/dino/dino_run.png",
            "res://scenes/games/Dino.tscn", RankOrder.ScoreDesc, "점수"),
        new GameInfo("merge", "숫자 합치기", "res://assets/sprites/menu/merge.png",
            "res://scenes/games/Merge.tscn", RankOrder.ScoreDesc, "점수", MergeRules.Variants, "합치기"),
        new GameInfo("snake", "애벌레 냠냠", "res://assets/sprites/menu/snake.png",
            "res://scenes/games/Snake.tscn", RankOrder.ScoreDesc, "먹은 잎"),
        new GameInfo("flappy", "아기새 날기", "res://assets/sprites/menu/flappy.png",
            "res://scenes/games/Flappy.tscn", RankOrder.ScoreDesc, "지나간 기둥", FlappyRules.Variants),
        new GameInfo("breakout", "벽돌깨기", "res://assets/sprites/menu/breakout.png",
            "res://scenes/games/Breakout.tscn", RankOrder.ScoreDesc, "깬 벽돌"),
        new GameInfo("match3", "과일 맞추기", "res://assets/sprites/menu/match3.png",
            "res://scenes/games/Match3.tscn", RankOrder.ScoreDesc, "모은 과일", Match3Rules.Variants, "맞추기"),
        new GameInfo("swipe", "공 쏘기 벽돌깨기", "res://assets/sprites/menu/swipe_brick.png",
            "res://scenes/games/SwipeBrick.tscn", RankOrder.ScoreDesc, "깬 벽돌", SwipeBrickRules.Variants, "공 쏘기"),
        new GameInfo("bubble", "방울 쏘기", "res://assets/sprites/menu/bubble.png",
            "res://scenes/games/Bubble.tscn", RankOrder.TimeAscScoreAsc, "걸린 시간 · 쏜 방울", BubbleRules.Variants),
        new GameInfo("blocks", "블록 쌓기", "res://assets/sprites/menu/blocks.png",
            "res://scenes/games/Blocks.tscn", RankOrder.ScoreDesc, "지운 줄", BlocksRules.Variants),
        new GameInfo("sudoku", "그림 스도쿠", "res://assets/sprites/menu/picture_sudoku.png",
            "res://scenes/games/Sudoku.tscn", RankOrder.TimeAsc, "걸린 시간", SudokuRules.Variants, "스도쿠"),
        new GameInfo("shikaku", "네모 나누기", "res://assets/sprites/menu/shikaku.png",
            "res://scenes/games/Shikaku.tscn", RankOrder.TimeAsc, "걸린 시간", ShikakuRules.Variants, "네모"),
        new GameInfo("pushbox", "상자 밀기", "res://assets/sprites/menu/push_box.png",
            "res://scenes/games/PushBox.tscn", RankOrder.TimeAscScoreAsc, "걸린 시간 · 걸음", PushBoxRules.Variants, "상자"),
        new GameInfo("flight", "하늘 비행 모험", "res://assets/sprites/menu/sky_flight.png",
            "res://scenes/games/Flight.tscn", RankOrder.ScoreDesc, "모은 별", FlightRules.Variants, "비행"),
    };

    public static GameInfo? Find(string id) => All.FirstOrDefault(g => g.Id == id);
}
