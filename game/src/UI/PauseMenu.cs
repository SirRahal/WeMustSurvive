using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// The Esc menu. Built from real Godot controls so it works with mouse and keyboard (arrow keys + Enter).
/// In single player the game pauses underneath; in co-op it keeps running. Destructive choices ask first.
/// </summary>
public partial class PauseMenu : Control
{
    public static readonly (string Keys, string Action)[] ControlsList =
    {
        ("WASD / arrows / screen edge", "Pan the camera (also middle-mouse drag)"),
        ("Mouse wheel", "Zoom"),
        ("C", "Jump to your engineer"),
        ("Command card / 1-9", "Open a category, then pick a blueprint"),
        ("Left click / drag", "Place blueprint(s); replaces the engineer's plan"),
        ("R", "Rotate blueprint or selected building"),
        ("Right click", "Move engineer, or repair / resume a building"),
        ("Shift + any order", "Chain it after the current jobs (numbers show the order)"),
        ("Shift + left click", "Also keeps the blueprint selected"),
        ("Left click a building", "Select it (actions on the command card)"),
        ("T", "Research panel"),
        ("F", "Cycle inserter filter (Auto first)"),
        ("E", "Inserter input side: Auto / Back / Left / Right (90° moves)"),
        ("X", "Stop: clear orders, refund unstarted sites"),
        ("Delete", "Deconstruct (full refund)"),
        ("Space", "Call the next wave early"),
        ("Hold Alt + move + release", "Ping wheel (Alt+click: quick ping)"),
        ("Ctrl + left drag", "Draw on the map (Ctrl + right click: erase)"),
        ("H", "Show / hide the help panel"),
        ("Esc", "Cancel, deselect, or open this menu"),
    };

    private VBoxContainer _content = null!;
    private Label _title = null!;
    private Action? _back;

    public Settings Settings { get; set; } = new();
    public bool CanSurrender { get; set; } = true;
    public bool CanRestart { get; set; } = true;
    public bool TutorialActive { get; set; }
    public bool IsCoop { get; set; }

    public event Action? ResumeRequested;
    public event Action? RestartRequested;
    public event Action? ReplayTutorialRequested;
    public event Action? SkipTutorialRequested;
    public event Action? SurrenderRequested;
    public event Action? QuitToMenuRequested;
    public event Action? QuitRequested;
    public event Action? SettingsChanged;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // keeps working while the tree is paused
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var column = UiKit.Column(14);
        _title = UiKit.Label("", 28, UiKit.Accent);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_title);
        _content = UiKit.Column(8);
        column.AddChild(_content);
        AddChild(UiKit.Centered(UiKit.Panel(column)));
    }

    public void Open()
    {
        Visible = true;
        ShowMain();
    }

    public void Close() => Visible = false;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        if (_back != null) _back();
        else ResumeRequested?.Invoke();
    }

    // ------------------------------------------------------------------ pages

    private void ShowMain()
    {
        Page(IsCoop ? "Menu (game keeps running)" : "Paused", back: null);
        var resume = Add(UiKit.Button("Resume", () => ResumeRequested?.Invoke()));
        if (CanRestart)
            Add(UiKit.Button("Restart", () => Confirm("Restart this map? Progress will be lost.", "Restart", () => RestartRequested?.Invoke())));
        if (TutorialActive)
            Add(UiKit.Button("Skip tutorial", () => SkipTutorialRequested?.Invoke()));
        else if (!IsCoop)
            Add(UiKit.Button("Replay tutorial", () => Confirm("Restart The Funnel with the tutorial?", "Replay tutorial", () => ReplayTutorialRequested?.Invoke())));
        Add(UiKit.Button("Controls", ShowControls));
        Add(UiKit.Button("Settings", ShowSettings));
        if (CanSurrender)
            Add(UiKit.Button("Surrender", () => Confirm("Surrender? This counts as a defeat.", "Surrender", () => SurrenderRequested?.Invoke()), danger: true));
        Add(UiKit.Button("Quit to main menu", () => Confirm("Leave this game?", "Quit to menu", () => QuitToMenuRequested?.Invoke()), danger: true));
        Add(UiKit.Button("Quit to desktop", () => Confirm("Quit the game? Unsaved progress will be lost.", "Quit", () => QuitRequested?.Invoke()), danger: true));
        resume.GrabFocus();
    }

    private void Confirm(string question, string yesLabel, Action yes)
    {
        Page("Are you sure?", back: ShowMain);
        var label = UiKit.Label(question, 17, wrap: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        _content.AddChild(label);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        _content.AddChild(row);
        var no = UiKit.Button("Cancel", ShowMain, width: 160);
        row.AddChild(no);
        row.AddChild(UiKit.Button(yesLabel, yes, danger: true, width: 160));
        no.GrabFocus();
    }

    private void ShowControls()
    {
        Page("Controls", back: ShowMain);
        _content.AddChild(ControlsGrid());
        Add(UiKit.Button("Back", ShowMain)).GrabFocus();
    }

    public static GridContainer ControlsGrid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 3);
        foreach (var (keys, action) in ControlsList)
        {
            grid.AddChild(UiKit.Label(keys, 15, UiKit.Accent));
            grid.AddChild(UiKit.Label(action, 15));
        }
        return grid;
    }

    private void ShowSettings()
    {
        Page("Settings", back: ShowMain);
        SettingsPanel.Build(_content, Settings, IsCoop, () => SettingsChanged?.Invoke());
        Add(UiKit.Button("Back", ShowMain));
    }

    // ------------------------------------------------------------------ helpers

    private void Page(string title, Action? back)
    {
        _title.Text = title;
        _back = back;
        UiKit.Clear(_content);
    }

    private Button Add(Button b)
    {
        _content.AddChild(b);
        return b;
    }
}

/// <summary>Settings controls, shared by the pause menu and the main menu.</summary>
public static class SettingsPanel
{
    public static void Build(Container into, Settings settings, bool coop, Action changed)
    {
        var speedRow = new HBoxContainer();
        var speedLabel = UiKit.Label(coop ? "Game speed (host only)" : "Game speed");
        speedLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        speedRow.AddChild(speedLabel);
        var speed = new OptionButton { Disabled = coop };
        foreach (float s in Settings.Speeds) speed.AddItem($"{s:0.#}x");
        speed.Selected = Array.IndexOf(Settings.Speeds, settings.GameSpeed);
        speed.ItemSelected += i =>
        {
            settings.GameSpeed = Settings.Speeds[(int)i];
            changed();
        };
        speedRow.AddChild(speed);
        into.AddChild(speedRow);

        AddToggle(into, "Screen-edge scrolling", settings.EdgePan, on => { settings.EdgePan = on; changed(); });
        AddToggle(into, "Fullscreen", settings.Fullscreen, on => { settings.Fullscreen = on; changed(); });
        AddToggle(into, "Show minimap", settings.ShowMinimap, on => { settings.ShowMinimap = on; changed(); });
        speed.GrabFocus();
    }

    private static void AddToggle(Container into, string text, bool value, Action<bool> onToggle)
    {
        var t = new CheckButton { Text = text, ButtonPressed = value };
        t.Toggled += on => onToggle(on);
        into.AddChild(t);
    }
}
