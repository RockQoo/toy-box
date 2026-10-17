using System;
using Godot;
using ToyBox.Core;

namespace ToyBox.Common;

/// <summary>
/// PC name field that composes Hangul itself (<see cref="HangulComposer"/>, 두벌식) instead of relying on the OS IME.
/// On the user's Windows setup Godot's LineEdit never received IME composition, so Korean names came out as Latin
/// letters. Letter keys are read by physical position (+Shift), so Caps Lock and the IME's own mode do not matter.
/// <see cref="Korean"/> switches between 한글 and English input (the 한/영 button next to the field).
/// </summary>
public partial class HangulField : PanelContainer
{
    public event Action? Submitted;
    public event Action? ModeChanged;

    private const float BlinkSeconds = 0.55f;

    private HangulComposer _composer = null!;
    private RichTextLabel _label = null!;
    private string _placeholder = "";
    private bool _korean = true;
    private float _blink;

    public static HangulField Create(int maxLength, string placeholder, Vector2 minSize)
    {
        var field = new HangulField
        {
            _composer = new HangulComposer(maxLength),
            _placeholder = placeholder,
            CustomMinimumSize = minSize,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
        };
        field.AddThemeStyleboxOverride("panel", UiTheme.Box(Colors.White, UiTheme.Ink, 4, 10, 14));
        field._label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        field._label.AddThemeFontSizeOverride("normal_font_size", 32);
        field._label.AddThemeColorOverride("default_color", UiTheme.Ink);
        field.AddChild(field._label);
        field.FocusEntered += field.Refresh;
        field.FocusExited += field.Refresh;
        return field;
    }

    /// <summary>True for 한글 input, false for English letters.</summary>
    public bool Korean
    {
        get => _korean;
        set
        {
            if (_korean == value)
                return;
            _korean = value;
            _composer.Commit();
            Refresh();
            ModeChanged?.Invoke();
        }
    }

    /// <summary>What the field shows right now, including the open syllable (does not close it).</summary>
    public string ShownText => _composer.Text;

    /// <summary>The typed text, with any open syllable closed.</summary>
    public string Text
    {
        get
        {
            _composer.Commit();
            return _composer.Text;
        }
    }

    public void Clear()
    {
        _composer.Clear();
        Refresh();
    }

    public override void _Ready() => Refresh();

    public override void _Process(double delta)
    {
        if (!HasFocus())
            return;
        _blink += (float)delta;
        if (_blink >= BlinkSeconds * 2)
            _blink -= BlinkSeconds * 2;
        Refresh();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } or InputEventScreenTouch { Pressed: true })
        {
            GrabFocus();
            AcceptEvent();
            return;
        }
        if (e is not InputEventKey { Pressed: true } key)
            return;

        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                AcceptEvent();
                Submitted?.Invoke();
                return;
            case Key.Backspace:
                _composer.Backspace();
                break;
            default:
                if (key.Echo)
                    return; // Only Backspace repeats
                if (_korean && key.PhysicalKeycode is >= Key.A and <= Key.Z
                    && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed)
                {
                    var letter = (char)('a' + (key.PhysicalKeycode - Key.A));
                    _composer.InputJamo(HangulComposer.JamoForKey(letter, key.ShiftPressed)!.Value);
                }
                else if (key.Unicode >= 32 && key.Unicode != 127 && !key.CtrlPressed)
                {
                    _composer.InputChar((char)key.Unicode);
                }
                else
                {
                    return;
                }
                break;
        }
        _blink = 0;
        Refresh();
        AcceptEvent();
    }

    private void Refresh()
    {
        if (_label is null)
            return;
        var text = _composer.Text;
        var caretOn = HasFocus() && _blink < BlinkSeconds;
        // The caret is always present (transparent when hidden) so the centered text does not shift while blinking
        var caret = $"[color=#{(caretOn ? UiTheme.Blue : Colors.Transparent).ToHtml()}]|[/color]";
        string body;
        if (text.Length == 0)
        {
            body = $"[color=#{UiTheme.InkSoft.ToHtml()}]{_placeholder}[/color]";
            body = HasFocus() ? caret + body : body;
        }
        else
        {
            // The open syllable is underlined, like an IME composition
            var open = _composer.IsComposing ? 1 : 0;
            var done = Escape(text[..^open]);
            var composing = open > 0 ? $"[u]{Escape(text[^1..])}[/u]" : "";
            body = done + composing + caret;
        }
        _label.Text = $"[center]{body}[/center]";
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
