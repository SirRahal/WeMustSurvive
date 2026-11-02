using BeltAndBarricade.Net;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>Title screen: play (map select), co-op host/join, skill tree, settings, controls, quit.</summary>
public partial class MainMenu : Control
{
    private readonly App _app;
    private readonly string? _notice;
    private VBoxContainer _page = null!;

    public MainMenu(App app, string? notice)
    {
        _app = app;
        _notice = notice;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = UiKit.Backdrop };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new MenuBackdrop());

        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", 40);

        var left = UiKit.Column(10);
        var title = UiKit.Label("BELT & BARRICADE", 46, UiKit.Accent);
        left.AddChild(title);
        left.AddChild(UiKit.Label("Post-apocalyptic factory defense", 18, UiKit.Dim));
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        left.AddChild(UiKit.Button("Play", ShowPlay, width: 280));
        left.AddChild(UiKit.Button("Co-op", ShowCoop, width: 280));
        left.AddChild(UiKit.Button($"Skill Tree  ({_app.Profile.MetaPoints} pts)", _app.ShowSkillTree, width: 280));
        left.AddChild(UiKit.Button("Settings", ShowSettings, width: 280));
        left.AddChild(UiKit.Button("Controls", ShowControls, width: 280));
        left.AddChild(UiKit.Button("Quit", () => GetTree().Quit(), danger: true, width: 280));
        root.AddChild(left);

        _page = UiKit.Column(12);
        _page.CustomMinimumSize = new Vector2(560, 0);
        root.AddChild(UiKit.Panel(_page, 600));

        AddChild(UiKit.Centered(root));
        if (_notice != null) ShowNotice(_notice);
        else ShowPlay();
        ((Button)left.GetChild(3)).GrabFocus();
    }

    private void Page(string title)
    {
        UiKit.Clear(_page);
        _page.AddChild(UiKit.Label(title, 26, UiKit.Accent));
    }

    private void ShowNotice(string text)
    {
        Page("Notice");
        _page.AddChild(UiKit.Label(text, 18, UiKit.Warn, wrap: true));
        _page.AddChild(UiKit.Button("OK", ShowPlay));
    }

    private void ShowPlay()
    {
        Page("Choose a map");
        var profile = _app.Profile;
        foreach (var (id, scenario) in _app.Scenarios)
        {
            var map = scenario.Map;
            var box = UiKit.Column(6);
            box.AddChild(UiKit.Label(map.Name, 22, UiKit.Text));
            box.AddChild(UiKit.Label(map.Description, 15, UiKit.Dim, wrap: true));
            int wins = profile.Wins.GetValueOrDefault(id);
            string record = wins > 0 ? $"Won {wins}x" : profile.BestWave.TryGetValue(id, out int best) ? $"Best: reached wave {best}" : "Not played yet";
            box.AddChild(UiKit.Label($"{scenario.Waves.Waves.Count} waves, ~{map.Minutes} min. Reward: {map.MetaPoints} meta points (+{Profile.FirstClearBonus} first clear). {record}", 14, UiKit.Gold, wrap: true));

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 16);
            CheckBox? tutorial = null;
            if (id == "funnel")
            {
                tutorial = new CheckBox { Text = "Play the tutorial", ButtonPressed = !profile.TutorialDone };
                row.AddChild(tutorial);
            }
            row.AddChild(UiKit.Button($"Play {map.Name}", () => _app.StartSolo(id, tutorial?.ButtonPressed ?? false), width: 260));
            box.AddChild(row);
            _page.AddChild(box);
            _page.AddChild(new HSeparator());
        }
        _page.AddChild(UiKit.Label("Sandbox: all research, 5000 of everything, waves start when you press Space. No rewards.", 14, UiKit.Dim, wrap: true));
        _page.AddChild(UiKit.Button("Sandbox (The Funnel)", () => _app.StartSandbox("funnel"), width: 260));
        int nodes = profile.Allocated.Count;
        _page.AddChild(UiKit.Label(nodes == 0 ? "Win maps to earn meta points for the Skill Tree." : $"Skill tree bonuses active: {nodes} node(s).", 14, UiKit.Dim));
    }

    private void ShowCoop()
    {
        Page("Co-op (2 players)");
        _page.AddChild(UiKit.Label("Both players share one factory and one Nexus. Enemies are at full co-op strength.\n" +
                                   "Host: share your IP address (shown in the lobby) and keep port " + NetSession.DefaultPort + " open (LAN works out of the box).",
            15, UiKit.Dim, wrap: true));
        _page.AddChild(UiKit.Button("Host a game", () => _app.ShowLobby(host: true), width: 260));
        _page.AddChild(new HSeparator());
        _page.AddChild(UiKit.Label("Join a game at address:", 16));
        var address = new LineEdit { Text = _app.Settings.LastJoinAddress, PlaceholderText = "e.g. 192.168.1.20", CustomMinimumSize = new Vector2(260, 36) };
        _page.AddChild(address);
        _page.AddChild(UiKit.Button("Join", () =>
        {
            _app.Settings.LastJoinAddress = address.Text.Trim();
            _app.Settings.Save();
            _app.ShowLobby(host: false, _app.Settings.LastJoinAddress);
        }, width: 260));
        _page.AddChild(UiKit.Label("Steam invites will replace IP addresses once the game is set up on Steamworks (see docs/STEAM.md).", 13, UiKit.Dim, wrap: true));
    }

    private void ShowSettings()
    {
        Page("Settings");
        SettingsPanel.Build(_page, _app.Settings, coop: false, () =>
        {
            _app.Settings.Save();
            _app.ApplyDisplaySettings();
        });
    }

    private void ShowControls()
    {
        Page("Controls");
        _page.AddChild(PauseMenu.ControlsGrid());
    }
}

/// <summary>Slow-drifting isometric grid and alien dots behind the title screen.</summary>
public partial class MenuBackdrop : Control
{
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        float t = (float)Time.GetTicksMsec() / 1000f;
        var line = new Color(0.35f, 0.8f, 1f, 0.06f);
        float offset = t * 12 % 64;
        for (float x = -size.Y * 2; x < size.X + size.Y * 2; x += 64)
        {
            DrawLine(new Vector2(x + offset, 0), new Vector2(x + offset + size.Y * 2, size.Y), line);
            DrawLine(new Vector2(x - offset, 0), new Vector2(x - offset - size.Y * 2, size.Y), line);
        }
        for (int i = 0; i < 24; i++)
        {
            float px = (i * 197.3f + t * (10 + i % 5 * 6)) % (size.X + 100) - 50;
            float py = size.Y * (0.15f + 0.7f * ((i * 0.618f) % 1f));
            DrawCircle(new Vector2(px, py), 3 + i % 3, new Color(0.6f, 0.25f, 0.85f, 0.25f));
        }
    }
}
