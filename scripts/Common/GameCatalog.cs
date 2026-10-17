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
    IReadOnlyList<GameVariant>? VariantList = null)
{
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
            null, RankOrder.ScoreDesc, "점수"),
    };

    public static GameInfo? Find(string id) => All.FirstOrDefault(g => g.Id == id);
}
