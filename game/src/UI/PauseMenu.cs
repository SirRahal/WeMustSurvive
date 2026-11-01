using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// The Esc menu. Built from real Godot controls so it works with mouse and keyboard (arrow keys + Enter)
/// while the rest of the game is paused. Destructive choices ask for confirmation first.
/// </summary>
public partial class PauseMenu : Control
{
    private static readonly Color Dim = new(0, 0, 0, 0.6f);
    private static readonly Color PanelBg = new(0.06f, 0.07f, 0.09f, 0.97f);
    private static readonly Color Edge = new(0.35f, 0.8f, 1f, 0.8f);
    private static readonly Color ButtonBg = new(0.12f, 0.14f, 0.18f);
    private static readonly Color ButtonHover = new(0.18f, 0.24f, 0.32f);
    private static readonly Color Danger = new(0.55f, 0.16f, 0.14f);

    private static readonly (string Keys, string Action)[] ControlsList =
    {
        ("WASD / arrows / screen edge", "Pan the camera"),
        ("Middle mouse drag", "Pan the camera"),
        ("Mouse wheel", "Zoom"),
        ("C", "Jump to your engineer"),
        ("1-7 or command card", "Pick a blueprint"),
        ("Left click / drag", "Place blueprint(s); they join the build queue"),
        ("R", "Rotate blueprint or selected building"),
        ("Right click", "Move engineer, or repair / resume a building"),
        ("Shift + right click", "Do it after the current jobs"),
        ("Shift + left click", "Keep placing the same blueprint"),
        ("Left click a building", "Select it (actions on the command card)"),
        ("F", "Cycle inserter filter"),
        ("X", "Stop: clear orders, refund unstarted sites"),
        ("Delete", "Deconstruct (full refund)"),
        ("Space", "Call the next wave early"),
        ("H", "Show / hide the help panel"),
        ("Esc", "Cancel, deselect, or open this menu"),
    };

    private VBoxContainer _content = null!;
    private Label _title = null!;
    private Action? _back;

    /// <summary>Settings shown on the Settings page; changes are applied through <see cref="SettingsChanged"/>.</summary>
    public Settings Settings { get; set; } = new();
    /// <summary>Whether Surrender is offered (false once the game is already over).</summary>
    public bool CanSurrender { get; set; } = true;
    public bool TutorialActive { get; set; }

    public event Action? ResumeRequested;
    public event Action? RestartRequested;
    public event Action? ReplayTutorialRequested;
    public event Action? SkipTutorialRequested;
    public event Action? SurrenderRequested;
    public event Action? QuitRequested;
    public event Action? SettingsChanged;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // keeps working while the tree is paused
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;

        var dim = new ColorRect { Color = Dim, MouseFilter = MouseFilterEnum.Stop };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer();
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        panel.AddThemeStyleboxOverride("panel", Box(PanelBg, Edge, 2, 22));
        center.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 28);
        _title.AddThemeColorOverride("font_color", Edge);
        column.AddChild(_title);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 8);
        column.AddChild(_content);
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
        Page("Paused", back: null);
        var resume = AddButton("Resume", () => ResumeRequested?.Invoke());
        AddButton("Restart", () => Confirm("Restart this map? Your progress will be lost.", "Restart", () => RestartRequested?.Invoke()));
        if (TutorialActive)
            AddButton("Skip tutorial", () => SkipTutorialRequested?.Invoke());
        else
            AddButton("Replay tutorial", () => Confirm("Restart the map with the tutorial?", "Replay tutorial", () => ReplayTutorialRequested?.Invoke()));
        AddButton("Controls", ShowControls);
        AddButton("Settings", ShowSettings);
        if (CanSurrender)
            AddButton("Surrender", () => Confirm("Surrender? This counts as a defeat.", "Surrender", () => SurrenderRequested?.Invoke()), danger: true);
        AddButton("Quit to desktop", () => Confirm("Quit the game? Unsaved progress will be lost.", "Quit", () => QuitRequested?.Invoke()), danger: true);
        resume.GrabFocus();
    }

    private void Confirm(string question, string yesLabel, Action yes)
    {
        Page("Are you sure?", back: ShowMain);
        var label = new Label { Text = question, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 17);
        _content.AddChild(label);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        _content.AddChild(row);
        var no = MakeButton("Cancel", ShowMain);
        row.AddChild(no);
        row.AddChild(MakeButton(yesLabel, yes, danger: true));
        no.GrabFocus();
    }

    private void ShowControls()
    {
        Page("Controls", back: ShowMain);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (var (keys, action) in ControlsList)
        {
            var k = new Label { Text = keys };
            k.AddThemeColorOverride("font_color", Edge);
            grid.AddChild(k);
            grid.AddChild(new Label { Text = action });
        }
        _content.AddChild(grid);
        AddButton("Back", ShowMain).GrabFocus();
    }

    private void ShowSettings()
    {
        Page("Settings", back: ShowMain);

        var speedRow = new HBoxContainer();
        speedRow.AddChild(new Label { Text = "Game speed", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var speed = new OptionButton();
        foreach (float s in Settings.Speeds) speed.AddItem($"{s:0.#}x");
        speed.Selected = Array.IndexOf(Settings.Speeds, Settings.GameSpeed);
        speed.ItemSelected += i =>
        {
            Settings.GameSpeed = Settings.Speeds[(int)i];
            SettingsChanged?.Invoke();
        };
        speedRow.AddChild(speed);
        _content.AddChild(speedRow);

        var edge = new CheckButton { Text = "Screen-edge scrolling", ButtonPressed = Settings.EdgePan };
        edge.Toggled += on =>
        {
            Settings.EdgePan = on;
            SettingsChanged?.Invoke();
        };
        _content.AddChild(edge);

        var full = new CheckButton { Text = "Fullscreen", ButtonPressed = Settings.Fullscreen };
        full.Toggled += on =>
        {
            Settings.Fullscreen = on;
            SettingsChanged?.Invoke();
        };
        _content.AddChild(full);

        AddButton("Back", ShowMain);
        speed.GrabFocus();
    }

    // ------------------------------------------------------------------ helpers

    private void Page(string title, Action? back)
    {
        _title.Text = title;
        _back = back;
        foreach (var child in _content.GetChildren()) child.QueueFree();
    }

    private Button AddButton(string text, Action onPressed, bool danger = false)
    {
        var b = MakeButton(text, onPressed, danger);
        _content.AddChild(b);
        return b;
    }

    private static Button MakeButton(string text, Action onPressed, bool danger = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(160, 42), FocusMode = FocusModeEnum.All };
        var bg = danger ? Danger : ButtonBg;
        b.AddThemeStyleboxOverride("normal", Box(bg, bg.Lightened(0.15f), 1, 8));
        b.AddThemeStyleboxOverride("hover", Box(danger ? Danger.Lightened(0.15f) : ButtonHover, Edge, 1, 8));
        b.AddThemeStyleboxOverride("pressed", Box(bg.Darkened(0.2f), Edge, 1, 8));
        b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), Edge, 2, 8));
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += onPressed;
        return b;
    }

    private static StyleBoxFlat Box(Color bg, Color border, int borderWidth, int margin)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(4);
        s.SetContentMarginAll(margin);
        return s;
    }
}
