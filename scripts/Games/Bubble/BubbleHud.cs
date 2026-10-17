using System;
using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Bubble;

/// <summary>
/// The HUD at the sides of the tall board (docs/games/13-bubble.md 화면 구조): "그만하기", which board, and a small faint
/// clock and shot count on the left; the bubbles left on the right (the buddy stands under it).
/// <see cref="ShowCleared"/> plays the big "다 터뜨렸어요!".
/// </summary>
public partial class BubbleHud : Control
{
    public const float LeftWidth = 240f;
    public const float RightWidth = 300f;
    private const float Margin = 18f;

    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private Label _boardLabel = null!;
    private Label _clockLabel = null!;
    private Label _shotsLabel = null!;
    private Label _leftLabel = null!;

    public Control LeftLabel => _leftLabel;

    public static BubbleHud Create()
    {
        var hud = new BubbleHud { MouseFilter = MouseFilterEnum.Ignore };
        hud.SetAnchorsPreset(LayoutPreset.FullRect);
        hud.Build();
        return hud;
    }

    private static PanelContainer InfoPanel(float alpha = 0.95f) => Ui.Panel(Colors.White with { A = alpha }, 12, 4);

    private void Build()
    {
        _left = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _left.AddThemeConstantOverride("separation", 14);
        AddChild(_left);
        _left.AddChild(Ui.Button("그만하기", () => QuitConfirm.Open(GetParent()), null, 24, null, new Vector2(LeftWidth - Margin * 2, 64)));

        // Which board, then the clock (small and faint: not a race) and the shots
        var infoPanel = InfoPanel(0.8f);
        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 2);
        _boardLabel = Ui.Label("", 26, UiTheme.Ink);
        _boardLabel.HorizontalAlignment = HorizontalAlignment.Center;
        info.AddChild(_boardLabel);
        _clockLabel = Ui.Label("", 24, UiTheme.InkSoft);
        _clockLabel.HorizontalAlignment = HorizontalAlignment.Center;
        info.AddChild(_clockLabel);
        _shotsLabel = Ui.Label("", 24, UiTheme.InkSoft);
        _shotsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        info.AddChild(_shotsLabel);
        infoPanel.AddChild(info);
        _left.AddChild(infoPanel);

        _right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _right.AddThemeConstantOverride("separation", 12);
        AddChild(_right);
        var leftPanel = InfoPanel();
        var leftColumn = new VBoxContainer();
        leftColumn.AddThemeConstantOverride("separation", 0);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(Ui.Icon("res://assets/sprites/menu/bubble.png", 54));
        _leftLabel = Ui.Label("", 40, UiTheme.Orange, 8, UiTheme.Ink);
        row.AddChild(_leftLabel);
        leftColumn.AddChild(row);
        var caption = Ui.Label("남았어요", 26, UiTheme.InkSoft);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        leftColumn.AddChild(caption);
        leftPanel.AddChild(leftColumn);
        _right.AddChild(leftPanel);

        Resized += Place;
    }

    public override void _Ready() => Place();

    private void Place()
    {
        var size = GetViewportRect().Size;
        _left.Position = new Vector2(Margin, Margin);
        _left.Size = new Vector2(LeftWidth - Margin * 2, 0);
        _right.Position = new Vector2(size.X - RightWidth + Margin, Margin);
        _right.Size = new Vector2(RightWidth - Margin * 2, 0);
    }

    public void SetBoard(string text) => _boardLabel.Text = text;

    public void SetClock(long ms) => _clockLabel.Text = $"{ms / 1000f:0.0}초";

    public void SetShots(int shots) => _shotsLabel.Text = $"쏜 방울 {shots}번";

    public void SetLeft(int bubbles, bool punch = false)
    {
        _leftLabel.Text = $"방울 {bubbles}개";
        if (punch)
            Ui.Punch(_leftLabel, 0.3f);
    }

    /// <summary>A big "다 터뜨렸어요!", then <paramref name="then"/>.</summary>
    public void ShowCleared(Action then)
    {
        Audio.I.DuckMusic(-12f);
        var done = Ui.Label("다 터뜨렸어요!", 96, UiTheme.Yellow, 24, UiTheme.Ink);
        done.HorizontalAlignment = HorizontalAlignment.Center;
        done.VerticalAlignment = VerticalAlignment.Center;
        done.MouseFilter = MouseFilterEnum.Ignore;
        done.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(done);
        done.PivotOffset = GetViewportRect().Size / 2;
        done.Scale = new Vector2(0.3f, 0.3f);
        var tween = done.CreateTween();
        tween.TweenProperty(done, "scale", Vector2.One, 0.4f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(1.4f);
        tween.TweenProperty(done, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Callable.From(() =>
        {
            done.QueueFree();
            then();
        }));
    }
}
