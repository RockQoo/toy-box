using System;
using System.Collections.Generic;
using Godot;
using ToyBox.Common;

namespace ToyBox.Games.Sudoku;

/// <summary>Tool buttons under the animals.</summary>
public enum SudokuTool { Erase, Memo, Hint, Undo }

/// <summary>
/// The right column: the animal buttons (`sudoku/btn_bg.png`, `btn_bg_selected.png` while an animal is held) in two
/// columns, then the tools (지우개, 메모 on 6 x 6 only, 힌트, 되돌리기). A small number in each animal button's corner
/// is its PC key.
/// </summary>
public partial class SudokuPalette : VBoxContainer
{
    public const float Width = 252f;

    private readonly List<TextureButton> _animals = new();
    private readonly List<Control> _animalHolders = new();
    private readonly Dictionary<SudokuTool, TextureButton> _tools = new();
    private readonly Dictionary<SudokuTool, Label> _toolLabels = new();
    private Texture2D _bg = null!;
    private Texture2D _bgHeld = null!;
    private int _held;

    public event Action<int>? AnimalPressed;
    public event Action<SudokuTool>? ToolPressed;

    public static SudokuPalette Create(int animals)
    {
        var palette = new SudokuPalette { MouseFilter = MouseFilterEnum.Ignore };
        palette.Build(animals);
        return palette;
    }

    private void Build(int animals)
    {
        _bg = Ui.Tex("res://assets/sprites/sudoku/btn_bg.png");
        _bgHeld = Ui.Tex("res://assets/sprites/sudoku/btn_bg_selected.png");
        AddThemeConstantOverride("separation", 18);

        var size = animals == 4 ? 118f : 112f;
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 10);
        grid.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        for (var v = 1; v <= animals; v++)
        {
            var animal = v;
            var button = new TextureButton
            {
                TextureNormal = _bg,
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
                CustomMinimumSize = new Vector2(size, size),
                FocusMode = FocusModeEnum.None,
            };
            var face = Ui.Icon(SudokuArt.FacePath(v), size * 0.72f);
            face.Position = new Vector2(size * 0.14f, size * 0.12f);
            face.Size = new Vector2(size * 0.72f, size * 0.72f);
            button.AddChild(face);
            var key = Ui.Label(v.ToString(), 18, UiTheme.InkSoft);
            key.Position = new Vector2(size * 0.14f, size * 0.07f);
            key.MouseFilter = MouseFilterEnum.Ignore;
            button.AddChild(key);
            Ui.MakeSquishy(button);
            button.Pressed += () => AnimalPressed?.Invoke(animal);
            grid.AddChild(button);
            _animals.Add(button);
            _animalHolders.Add(face);
        }
        AddChild(grid);

        var tools = new List<(SudokuTool Tool, string Icon, string Text)>
        {
            (SudokuTool.Erase, "erase", "지우개"),
        };
        if (animals > 4)
            tools.Add((SudokuTool.Memo, "memo", "메모"));
        tools.Add((SudokuTool.Hint, "hint", "힌트"));
        tools.Add((SudokuTool.Undo, "undo", "되돌리기"));

        var toolSize = tools.Count == 3 ? 72f : 82f;
        var toolGrid = new GridContainer { Columns = tools.Count == 3 ? 3 : 2, MouseFilter = MouseFilterEnum.Ignore };
        toolGrid.AddThemeConstantOverride("h_separation", tools.Count == 3 ? 8 : 26);
        toolGrid.AddThemeConstantOverride("v_separation", 6);
        toolGrid.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        foreach (var (tool, icon, text) in tools)
        {
            var t = tool;
            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", 0);
            var button = new TextureButton
            {
                TextureNormal = Ui.Tex($"res://assets/sprites/ui/{icon}.png"),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(toolSize, toolSize),
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            };
            Ui.MakeSquishy(button);
            button.Pressed += () => ToolPressed?.Invoke(t);
            column.AddChild(button);
            var label = Ui.Label(text, 20, UiTheme.Ink, 6, Colors.White);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.CustomMinimumSize = new Vector2(Mathf.Max(toolSize, 80), 0);
            column.AddChild(label);
            toolGrid.AddChild(column);
            _tools[tool] = button;
            _toolLabels[tool] = label;
        }
        AddChild(toolGrid);
    }

    /// <summary>The animal being held (0: none): its button turns yellow.</summary>
    public void SetHeld(int animal)
    {
        _held = animal;
        for (var i = 0; i < _animals.Count; i++)
            _animals[i].TextureNormal = i + 1 == animal ? _bgHeld : _bg;
    }

    public int Held => _held;

    /// <summary>Animals already on the board everywhere they belong look a little faded (still pressable).</summary>
    public void SetDone(int animal, bool done) =>
        _animalHolders[animal - 1].Modulate = done ? new Color(1, 1, 1, 0.4f) : Colors.White;

    public void SetMemoOn(bool on)
    {
        if (!_tools.TryGetValue(SudokuTool.Memo, out var button))
            return;
        button.SelfModulate = on ? Colors.White : new Color(0.85f, 0.85f, 0.85f);
        _toolLabels[SudokuTool.Memo].Text = on ? "메모 켜짐" : "메모";
        _toolLabels[SudokuTool.Memo].AddThemeColorOverride("font_color", on ? UiTheme.Green.Darkened(0.3f) : UiTheme.Ink);
    }

    public void SetUndoEnabled(bool enabled)
    {
        var button = _tools[SudokuTool.Undo];
        button.Disabled = !enabled;
        button.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.4f);
    }

    public Control AnimalButton(int animal) => _animals[animal - 1];

    public Control? Tool(SudokuTool tool) => _tools.TryGetValue(tool, out var b) ? b : null;

    public void Pulse(SudokuTool tool)
    {
        if (Tool(tool) is { } control)
            Ui.Punch(control, 0.2f);
    }
}
