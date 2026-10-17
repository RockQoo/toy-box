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

        _title = Ui.Label(App.DisplayName, 80, UiTheme.Orange, 18, Colors.White);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(_title);

        // Cards in pages of 6 (3 x 2) with arrows on both sides and a dot per page under them
        _pageCount = Mathf.Max(1, (GameCatalog.All.Count + PerPage - 1) / PerPage);
        _page = Mathf.Clamp(_page, 0, _pageCount - 1);
        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pager.AddThemeConstantOverride("separation", 18);
        _prev = PageArrow("res://assets/sprites/ui/btn_left.png", -1);
        pager.AddChild(_prev);
        _grid = new GridContainer { Columns = 3 };
        _grid.AddThemeConstantOverride("h_separation", 24);
        _grid.AddThemeConstantOverride("v_separation", 22);
        pager.AddChild(_grid);
        _next = PageArrow("res://assets/sprites/ui/btn_right.png", 1);
        pager.AddChild(_next);
        root.AddChild(pager);

        _dots = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = _pageCount > 1 };
        _dots.AddThemeConstantOverride("separation", 12);
        root.AddChild(_dots);

        var cards = FillPage();

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bottom.AddThemeConstantOverride("separation", 20);
        bottom.AddChild(Ui.Button("기록", ShowRecords, UiTheme.Purple, 30, Ui.Tex("res://assets/sprites/ui/star.png"), new Vector2(200, 76)));
        bottom.AddChild(Ui.Button("설정", ShowSettings, UiTheme.Blue, 30, null, new Vector2(200, 76)));
        // Phones close apps with the system back/home gesture; a quit button there is unusual
        if (!OS.HasFeature("mobile"))
            bottom.AddChild(Ui.Button("나가기", ShowQuit, null, 30, null, new Vector2(200, 76)));
        root.AddChild(bottom);

        _popupLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _popupLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_popupLayer);

        // Staggered entrance once the containers have sized everything
        CallDeferred(MethodName.AnimateIn, new Godot.Collections.Array<Control>(cards));

        // Dev: `-- --open=records` (or `--open=records:<gameId>` for that game's tab) / `--open=settings` opens a popup
        // at start (for screenshots)
        var args = OS.GetCmdlineUserArgs();
        if (args.FirstOrDefault(a => a.StartsWith("--open=records")) is { } open)
            ShowRecords(open.Contains(':') ? open[(open.IndexOf(':') + 1)..] : null);
        else if (args.Contains("--open=settings"))
            ShowSettings();
        else if (args.Contains("--open=quit"))
            ShowQuit();
    }

    private void AnimateIn(Godot.Collections.Array<Control> cards)
    {
        Ui.PopIn(_title, 0f, 0.5f);
        for (var i = 0; i < cards.Count; i++)
            Ui.PopIn(cards[i], 0.12f + i * 0.06f);
    }

    // ---- Pages ----

    private const int PerPage = 6;

    /// <summary>Kept across menu visits, so coming back from a game on page 2 shows page 2.</summary>
    private static int _page;

    private int _pageCount;
    private GridContainer _grid = null!;
    private HBoxContainer _dots = null!;
    private TextureButton _prev = null!, _next = null!;

    private TextureButton PageArrow(string texture, int step)
    {
        var arrow = new TextureButton
        {
            TextureNormal = Ui.Tex(texture),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(84, 84),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
        };
        Ui.MakeSquishy(arrow);
        arrow.Pressed += () => TurnPage(step);
        return arrow;
    }

    /// <summary>Puts the current page's cards in the grid and updates the arrows and dots. Returns the new cards.</summary>
    private System.Collections.Generic.List<Control> FillPage()
    {
        foreach (var child in _grid.GetChildren())
            child.QueueFree();
        var cards = GameCatalog.All.Skip(_page * PerPage).Take(PerPage).Select(GameCard).ToList();
        foreach (var card in cards)
            _grid.AddChild(card);
        // Hidden arrows keep their space so the grid does not shift between pages
        _prev.Modulate = _page > 0 ? Colors.White : Colors.Transparent;
        _prev.Disabled = _page == 0;
        _next.Modulate = _page < _pageCount - 1 ? Colors.White : Colors.Transparent;
        _next.Disabled = _page >= _pageCount - 1;
        foreach (var child in _dots.GetChildren())
            child.QueueFree();
        for (var i = 0; i < _pageCount; i++)
        {
            var dot = new Panel { CustomMinimumSize = new Vector2(22, 22), MouseFilter = MouseFilterEnum.Ignore };
            dot.AddThemeStyleboxOverride("panel",
                UiTheme.Box(i == _page ? UiTheme.Orange : Colors.White, UiTheme.Ink, 3, 0, 11));
            _dots.AddChild(dot);
        }
        return cards;
    }

    private void TurnPage(int step)
    {
        var page = Mathf.Clamp(_page + step, 0, _pageCount - 1);
        if (page == _page)
            return;
        _page = page;
        Audio.I.Whoosh();
        var cards = FillPage();
        for (var i = 0; i < cards.Count; i++)
            Ui.PopIn(cards[i], i * 0.04f, 0.28f);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && _popupLayer.GetChildCount() == 0)
        {
            if (key.PhysicalKeycode is Key.Right or Key.D)
                TurnPage(1);
            else if (key.PhysicalKeycode is Key.Left or Key.A)
                TurnPage(-1);
        }
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
        else if (BestFor(game) is { } found)
        {
            var (best, variantLabel) = found;
            var text = variantLabel is null ? $"최고 {game.FormatValue(best)}" : $"{variantLabel} 최고 {game.FormatValue(best)}";
            var bestLabel = Ui.Label(text, 20, UiTheme.Orange);
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

    /// <summary>The record a menu card shows. Games with variants show the hardest variant that has a record.</summary>
    private static (Core.ScoreEntry Best, string? VariantLabel)? BestFor(GameInfo game)
    {
        if (!game.HasVariants)
            return App.I.Board(game.Id).Best is { } best ? (best, null) : null;
        var variant = game.FeaturedVariant();
        return variant is null ? null : (App.I.Board(game.Id, variant.Id).Best!, variant.Label);
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

    /// <summary>"(app name)를 끝낼까요?" — asked once so a child tapping around does not close the app.</summary>
    private void ShowQuit()
    {
        var (popup, body) = OpenPopup("나가기");
        var question = Ui.Label($"{App.DisplayName}를 끝낼까요?", 34, UiTheme.Ink);
        question.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(question);
        body.AddChild(Ui.Spacer(6));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        // The safe choice is the big green one, like the in-game quit question
        buttons.AddChild(Ui.Button("계속 놀기", popup.QueueFree, UiTheme.Green, 30, null, new Vector2(220, 80)));
        buttons.AddChild(Ui.Button("끝내기", () => GetTree().Quit(), null, 28, null, new Vector2(180, 80)));
        body.AddChild(buttons);
    }

    private static Button CloseButton(Control popup) =>
        Ui.Button("닫기", popup.QueueFree, UiTheme.Pink, 28, null, new Vector2(180, 70));

    private void ShowRecords() => ShowRecords(null);

    private void ShowRecords(string? startGameId)
    {
        var (popup, body) = OpenPopup("기록");

        // Left: game tabs with the close button under them. Right: the selected game's top 10.
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        body.AddChild(columns);

        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 12);
        columns.AddChild(side);
        var tabs = ChoiceRow.Create("", GameCatalog.All.Select(g => new Choice(g.TabName, g.IconPath)).ToList(),
            Mathf.Max(0, GameCatalog.All.ToList().FindIndex(g => g.Id == startGameId)), 180, vertical: true, chipHeight: 72);
        // More games than fit: the tabs scroll (drag or wheel) above the close button
        var tabScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(200, 430),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        tabScroll.AddChild(tabs);
        side.AddChild(tabScroll);
        // Bring the starting tab into view once the chips have their sizes (a game past the first few)
        var startTab = tabs.SelectedIndex;
        GetTree().ProcessFrame += ScrollToStart;
        void ScrollToStart()
        {
            GetTree().ProcessFrame -= ScrollToStart;
            if (IsInstanceValid(tabScroll) && tabs.GetChildCount() > 0)
                tabScroll.ScrollVertical = (int)(tabs.Size.Y * startTab / GameCatalog.All.Count);
        }
        side.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var close = CloseButton(popup);
        close.CustomMinimumSize = new Vector2(180, 64);
        side.AddChild(close);

        var listHolder = new MarginContainer { CustomMinimumSize = new Vector2(600, 0) };
        columns.AddChild(listHolder);

        // A game with variants (quiz difficulty) gets a row of variant tabs above its list
        void ShowGame(int index)
        {
            foreach (var child in listHolder.GetChildren())
                child.QueueFree();
            var game = GameCatalog.All[index];
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 8);
            listHolder.AddChild(column);
            var listSlot = new MarginContainer();

            void ShowBoard(string? variantId)
            {
                foreach (var child in listSlot.GetChildren())
                    child.QueueFree();
                var list = RankingList.Create(game, variantId);
                listSlot.AddChild(list);
                list.Refresh();
            }

            if (game.HasVariants)
            {
                var start = game.FeaturedVariant() is { } featured ? IndexOf(game.Variants, featured) : 0;
                var variantTabs = ChoiceRow.Create("", game.Variants.Select(v => new Choice(v.Label)).ToList(), start, 130,
                    chipHeight: 56);
                variantTabs.Alignment = BoxContainer.AlignmentMode.Center;
                variantTabs.Selected += i => ShowBoard(game.Variants[i].Id);
                column.AddChild(variantTabs);
                ShowBoard(game.Variants[start].Id);
            }
            else
            {
                // Same height as the variant tabs, so the popup does not jump when switching games
                var name = Ui.Label(game.Title, 30, UiTheme.Ink);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                name.VerticalAlignment = VerticalAlignment.Center;
                name.CustomMinimumSize = new Vector2(0, 56);
                column.AddChild(name);
                ShowBoard(null);
            }
            column.AddChild(listSlot);
        }

        static int IndexOf(System.Collections.Generic.IReadOnlyList<Core.GameVariant> list, Core.GameVariant item)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == item)
                    return i;
            return 0;
        }

        tabs.Selected += ShowGame;
        ShowGame(tabs.SelectedIndex);
    }

    private void ShowSettings()
    {
        var (popup, body) = OpenPopup("설정");
        body.CustomMinimumSize = new Vector2(620, 0);

        body.AddChild(VolumeRow("배경음악", Audio.I.BgmVolume, v => Audio.I.BgmVolume = v, null));
        body.AddChild(VolumeRow("효과음", Audio.I.SfxVolume, v => Audio.I.SfxVolume = v, Audio.I.Click));

        if (!OS.HasFeature("mobile"))
        {
            var sizes = App.WindowSizes.Select(s => new Choice($"{s.X}×{s.Y}")).Append(new Choice("전체 화면")).ToList();
            var screen = ChoiceRow.Create("화면", sizes, App.I.ResolutionIndex, 160, chipHeight: 70);
            screen.Selected += App.I.ApplyResolution;
            body.AddChild(screen);
        }

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
