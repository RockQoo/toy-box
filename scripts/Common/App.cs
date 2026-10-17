using System;
using System.Linq;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>
/// Autoload. Owns app-wide state: settings (user://settings.cfg), records (user://records.json), the UI theme,
/// and scene changes with a fade. Access it through <see cref="I"/>.
/// </summary>
public partial class App : Node
{
    public const string MainMenuScene = "res://scenes/MainMenu.tscn";
    private const string SettingsPath = "user://settings.cfg";
    private const string RecordsPath = "user://records.json";
    private const float FadeTime = 0.22f;

    public static App I { get; private set; } = null!;

    private readonly ConfigFile _settings = new();
    private RecordBook _records = new();
    private ColorRect _fade = null!;
    private bool _changing;

    /// <summary>Dev flag (`-- --autoplay`): games play themselves, so frames can be captured without input.</summary>
    public bool Autoplay { get; private set; }

    /// <summary>Names used on the result screen, newest first (at most <see cref="RecentNames.Capacity"/>).</summary>
    public string[] RecentNames =>
        Core.RecentNames.Load((string[])GetSetting("player", "recent_names", Array.Empty<string>()),
            (string)GetSetting("player", "last_name", ""),
            _records.AllEntries);

    /// <summary>Remembers a name as the most recent one. Returns the cleaned name that was stored.</summary>
    public string RememberName(string name)
    {
        var list = Core.RecentNames.Push(RecentNames, name);
        _settings.SetValue("player", "last_name", list[0]); // Kept for older builds reading the same file
        SetSetting("player", "recent_names", list);
        return list[0];
    }

    public override void _EnterTree()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;
        _settings.Load(SettingsPath); // Missing file is fine: defaults are used
        // Variant boards ("quiz:hard") rank like their game
        _records = RecordBook.FromJson(ReadText(RecordsPath), key => OrderOf(BoardKey.GameId(key)));
        GetTree().Root.Theme = UiTheme.Build();
        ThemeDB.FallbackFont = UiTheme.Font; // Used by _Draw() text (FloatingText etc.)
    }

    public override void _Ready()
    {
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        _fade = new ColorRect
        {
            Color = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_fade);

        var args = OS.GetCmdlineUserArgs();
        Autoplay = args.Contains("--autoplay");
        var game = args.FirstOrDefault(a => a.StartsWith("--game="))?["--game=".Length..];
        if (game is not null && GameCatalog.Find(game)?.ScenePath is { } path)
            CallDeferred(MethodName.GoTo, path);
    }

    /// <summary>Changes scene with a short white fade. Input is blocked during the transition.</summary>
    public void GoTo(string scenePath)
    {
        if (_changing)
            return;
        _changing = true;
        _fade.MouseFilter = Control.MouseFilterEnum.Stop;
        var tween = CreateTween();
        tween.TweenProperty(_fade, "color:a", 1f, FadeTime);
        tween.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(scenePath)));
        tween.TweenInterval(0.05f);
        tween.TweenProperty(_fade, "color:a", 0f, FadeTime);
        tween.TweenCallback(Callable.From(() =>
        {
            _fade.MouseFilter = Control.MouseFilterEnum.Ignore;
            _changing = false;
        }));
    }

    public void GoToMainMenu() => GoTo(MainMenuScene);

    // ---- Settings ----

    public Variant GetSetting(string section, string key, Variant fallback) => _settings.GetValue(section, key, fallback);

    public void SetSetting(string section, string key, Variant value)
    {
        _settings.SetValue(section, key, value);
        _settings.Save(SettingsPath);
    }

    // ---- Records ----

    /// <summary>A game's top 10. Games with variants (see <see cref="GameInfo.Variants"/>) keep one per <paramref name="variantId"/>.</summary>
    public Leaderboard Board(string gameId, string? variantId = null) =>
        _records.Get(BoardKey.Of(gameId, variantId), OrderOf(gameId));

    private static RankOrder OrderOf(string gameId) => GameCatalog.Find(gameId)?.Order ?? RankOrder.ScoreDesc;

    /// <summary>Adds a record and saves to disk. Returns its zero-based rank, or -1 if it did not make the top 10.</summary>
    public int SubmitRecord(string gameId, string? variantId, ScoreEntry entry)
    {
        var rank = Board(gameId, variantId).Add(entry);
        if (rank >= 0)
            SaveRecords();
        return rank;
    }

    public void ClearAllRecords()
    {
        _records.ClearAll();
        SaveRecords();
    }

    private void SaveRecords()
    {
        using var file = FileAccess.Open(RecordsPath, FileAccess.ModeFlags.Write);
        file?.StoreString(_records.ToJson());
    }

    private static string? ReadText(string path)
    {
        if (!FileAccess.FileExists(path))
            return null;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    // ---- Misc ----

    /// <summary>Short vibration on phones. Does nothing on PC.</summary>
    public static void Vibrate(int ms)
    {
        if (OS.HasFeature("mobile"))
            Input.VibrateHandheld(ms);
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
