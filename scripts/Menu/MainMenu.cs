using System.Linq;
using Godot;
using ToyBox.Common;

namespace ToyBox.Menu;

/// <summary>Main menu: game cards, records (top 10 per game) and settings (volumes, clear records).</summary>
public partial class MainMenu : Control
{
    private Control _popupLayer = null!;
    private Label _title = null!;
    private float _time;

    public override void _Ready()
    {
        Audio.I.PlayMusic(Music.Menu);
        AddChild(new Scenery { Kind = SceneryKind.Park });

        var root = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 22);
        AddChild(root);

        _title = Ui.Label("토이 박스", 80, UiTheme.Orange, 18, Colors.White);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_title);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 22);
        var gridCenter = new CenterContainer();
        gridCenter.AddChild(grid);
        root.AddChild(gridCenter);

        var cards = GameCatalog.All.Select(GameCard).ToList();
        foreach (var card in cards)
            grid.AddChild(card);

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bottom.AddThemeConstantOverride("separation", 20);
        bottom.AddChild(Ui.Button("기록", ShowRecords, UiTheme.Purple, 30, Ui.Tex("res://assets/sprites/ui/star.png"), new Vector2(200, 76)));
        bottom.AddChild(Ui.Button("설정", ShowSettings, UiTheme.Blue, 30, null, new Vector2(200, 76)));
        root.AddChild(bottom);

        _popupLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _popupLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_popupLayer);

        // Staggered entrance once the containers have sized everything
        CallDeferred(MethodName.AnimateIn, new Godot.Collections.Array<Control>(cards));

        // Dev: `-- --open=records` / `--open=settings` opens a popup at start (for screenshots)
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--open=records"))
            ShowRecords();
        else if (args.Contains("--open=settings"))
            ShowSettings();
    }

    private void AnimateIn(Godot.Collections.Array<Control> cards)
    {
        Ui.PopIn(_title, 0f, 0.5f);
        for (var i = 0; i < cards.Count; i++)
            Ui.PopIn(cards[i], 0.12f + i * 0.06f);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _title.PivotOffset = _title.Size / 2;
        _title.Rotation = Mathf.Sin(_time * 1.6f) * 0.03f;
    }

    private Control GameCard(GameInfo game)
    {
        var ready = game.ScenePath is not null;
        var card = new Button { CustomMinimumSize = new Vector2(300, 200), FocusMode = FocusModeEnum.None };
        Ui.Paint(card, ready ? Colors.White : UiTheme.PaperDim, UiTheme.Ink);
        Ui.MakeSquishy(card);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);

        var icon = Ui.Icon(game.IconPath, 110);
        if (!ready)
            icon.Modulate = new Color(1, 1, 1, 0.45f);
        box.AddChild(icon);

        var name = Ui.Label(game.Title, 28, ready ? UiTheme.Ink : UiTheme.InkSoft);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(name);

        if (!ready)
        {
            var tag = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(180, 12) };
            tag.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Orange, UiTheme.Ink, 3, 6, 12));
            tag.AddChild(Ui.Label("준비 중", 20, Colors.White, 6));
            card.AddChild(tag);
        }
        else if (App.I.Board(game.Id).Best is { } best)
        {
            var bestLabel = Ui.Label($"최고 {game.FormatValue(best)}", 20, UiTheme.Orange);
            bestLabel.HorizontalAlignment = HorizontalAlignment.Center;
            box.AddChild(bestLabel);
        }

        card.Pressed += () =>
        {
            if (ready)
            {
                Audio.I.Click();
                App.I.GoTo(game.ScenePath!);
            }
            else
            {
                Audio.I.Bonk();
                Ui.Wiggle(card);
            }
        };
        return card;
    }

    // ---- Popups ----

    private (Control root, VBoxContainer body) OpenPopup(string title)
    {
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(Ui.Backdrop());

        var panel = Ui.Panel(margin: 28);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        panel.AddChild(body);
        root.AddChild(Ui.Centered(panel));

        var header = Ui.Label(title, 44, UiTheme.Orange, 12, UiTheme.Ink);
        header.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(header);

        _popupLayer.AddChild(root);
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();
        return (root, body);
    }

    private static Button CloseButton(Control popup) =>
        Ui.Button("닫기", popup.QueueFree, UiTheme.Pink, 28, null, new Vector2(180, 70));

    private void ShowRecords()
    {
        var (popup, body) = OpenPopup("기록");

        // Left: game tabs with the close button under them. Right: the selected game's top 10.
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        body.AddChild(columns);

        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 12);
        columns.AddChild(side);
        var tabs = ChoiceRow.Create("", GameCatalog.All.Select(g => new Choice(g.Title.Split(' ')[0], g.IconPath)).ToList(),
            0, 180, vertical: true);
        side.AddChild(tabs);
        side.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var close = CloseButton(popup);
        close.CustomMinimumSize = new Vector2(180, 64);
        side.AddChild(close);

        var listHolder = new MarginContainer { CustomMinimumSize = new Vector2(600, 0) };
        columns.AddChild(listHolder);

        void ShowGame(int index)
        {
            foreach (var child in listHolder.GetChildren())
                child.QueueFree();
            var list = RankingList.Create(GameCatalog.All[index]);
            listHolder.AddChild(list);
            list.Refresh();
        }

        tabs.Selected += ShowGame;
        ShowGame(0);
    }

    private void ShowSettings()
    {
        var (popup, body) = OpenPopup("설정");
        body.CustomMinimumSize = new Vector2(620, 0);

        body.AddChild(VolumeRow("배경음악", Audio.I.BgmVolume, v => Audio.I.BgmVolume = v, null));
        body.AddChild(VolumeRow("효과음", Audio.I.SfxVolume, v => Audio.I.SfxVolume = v, Audio.I.Click));

        body.AddChild(Ui.Spacer(10));
        var armed = false;
        Button clear = null!;
        clear = Ui.Button("기록 모두 지우기", () =>
        {
            if (!armed)
            {
                armed = true;
                clear.Text = "정말 지울까요? 한 번 더 누르세요";
                Ui.Paint(clear, UiTheme.Orange);
                return;
            }
            App.I.ClearAllRecords();
            clear.Text = "기록을 지웠어요";
            clear.Disabled = true;
        }, null, 24, null, new Vector2(0, 64));
        Ui.Paint(clear, UiTheme.PaperDim, UiTheme.InkSoft);
        body.AddChild(clear);

        var close = CloseButton(popup);
        close.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        body.AddChild(close);
        // Closing settings rebuilds the menu so "최고" labels reflect cleared records
        close.Pressed += () => GetTree().ReloadCurrentScene();
    }

    /// <param name="preview">Played when the knob is released, so the player hears the new volume.</param>
    private static Control VolumeRow(string caption, float value, System.Action<float> onChanged, System.Action? preview)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        var label = Ui.Label(caption, 30, UiTheme.Ink);
        label.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(label);
        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.05, Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 48),
            FocusMode = FocusModeEnum.None,
        };
        slider.ValueChanged += v => onChanged((float)v);
        slider.DragEnded += _ => preview?.Invoke();
        row.AddChild(slider);
        return row;
    }
}
