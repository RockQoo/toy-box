using Godot;

namespace ToyBox.Common;

/// <summary>
/// "정말 그만할까요?" over a paused game. The big bright choice is "계속하기"; "그만하기" goes to the main menu.
/// The tree is paused while it is open (countdowns and spawns hold too) and unpaused when it closes or the scene changes.
/// </summary>
public partial class QuitConfirm : Control
{
    /// <summary>Opens the question on <paramref name="parent"/> (a CanvasLayer) unless one is already open there.</summary>
    public static void Open(Node parent)
    {
        foreach (var child in parent.GetChildren())
            if (child is QuitConfirm)
                return;
        var confirm = new QuitConfirm { ProcessMode = ProcessModeEnum.Always, MouseFilter = MouseFilterEnum.Ignore };
        confirm.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(confirm);
    }

    public override void _Ready()
    {
        GetTree().Paused = true;
        AddChild(Ui.Backdrop());
        var panel = Ui.Panel(margin: 28);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 20);
        panel.AddChild(body);
        var question = Ui.Label("정말 그만할까요?", 44, UiTheme.Ink);
        question.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(question);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        // "Keep playing" is the big, bright choice
        buttons.AddChild(Ui.Button("계속하기", QueueFree, UiTheme.Green, 34, Ui.Tex("res://assets/sprites/ui/play.png"),
            new Vector2(260, 84)));
        buttons.AddChild(Ui.Button("그만하기", () =>
        {
            GetTree().Paused = false;
            App.I.GoToMainMenu();
        }, null, 28, null, new Vector2(180, 84)));
        body.AddChild(buttons);
        AddChild(Ui.Centered(panel));
        Callable.From(() => Ui.PopIn(panel)).CallDeferred();
    }

    public override void _ExitTree() => GetTree().Paused = false;
}
