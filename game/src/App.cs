using BeltAndBarricade.Input;
using BeltAndBarricade.Net;
using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
using Godot;

namespace BeltAndBarricade;

/// <summary>Everything needed to start a game, whether solo or co-op.</summary>
/// <param name="LocalPlayer">0 for solo / host, 1 for the joining player.</param>
/// <param name="Sandbox">All research done, plenty of resources, waves start only when the player asks. No rewards.</param>
public sealed record GameConfig(string MapId, ulong Seed, int PlayerCount, int LocalPlayer, bool Tutorial, Modifiers Mods, NetSession? Net,
    bool Sandbox = false);

/// <summary>
/// Root node. Loads data once, owns settings/profile and the network session, and switches between screens:
/// main menu, co-op lobby, skill tree, and the game itself.
/// </summary>
public partial class App : Node
{
    private Node? _screen;

    public GameData Data { get; private set; } = null!;
    public IReadOnlyDictionary<string, Scenario> Scenarios { get; private set; } = null!;
    public SkillTree Tree { get; private set; } = null!;
    public TutorialDef Tutorial { get; private set; } = null!;
    public Settings Settings { get; private set; } = null!;
    public Profile Profile { get; private set; } = null!;
    public NetSession Net { get; private set; } = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Settings = Settings.Load();
        Profile = Profile.Load();
        Data = GameData.Load(ReadData);
        Scenarios = GameData.MapIds.ToDictionary(id => id, id => GameData.LoadScenario(ReadData, id));
        Tree = SkillTree.Load(ReadData);
        Tutorial = TutorialDef.Load(ReadData, "funnel");
        AddChild(Net = new NetSession { Name = "Net" });
        Bindings.Register();
        if (OS.IsDebugBuild()) AddChild(new IssueReporter { Name = "IssueReporter", Context = DescribeScreen });
        ApplyDisplaySettings();
        ShowMainMenu();
        RunCommandLine();
    }

    /// <summary>True when launched with --smoke: the game issues some commands by itself (for automated tests).</summary>
    public bool SmokeMode { get; private set; }
    /// <summary>--showcase (debug): sandbox with one of every building pre-built.</summary>
    public bool ShowcaseMode { get; private set; }

    /// <summary>--hud=storage,controls,research (debug): HUD panels to open when a game starts, for screenshots.</summary>
    public string HudPanels { get; private set; } = "";

    /// <summary>
    /// Automation switches (after "--" on the Godot command line):
    /// --autostart=MAP (solo; add --tutorial for the tutorial), --autohost=MAP, --autojoin=ADDRESS,
    /// --smoke=SECONDS (play itself, print a summary, quit), --screenshot=PATH (save a PNG after 3 s and quit).
    /// </summary>
    private void RunCommandLine()
    {
        var args = OS.GetCmdlineUserArgs().Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
        HudPanels = args.GetValueOrDefault("--hud", "");
        if (args.TryGetValue("--screenshot", out var shotPath))
        {
            GetTree().CreateTimer(3, processAlways: true).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng(shotPath);
                GD.Print($"SCREENSHOT {shotPath} {GetViewport().GetVisibleRect().Size}");
                GetTree().Quit();
            };
        }
        if (args.TryGetValue("--smoke", out var secs))
        {
            SmokeMode = true;
            GetTree().CreateTimer(double.Parse(secs), processAlways: true).Timeout += () =>
            {
                GD.Print("SMOKE " + (_screen is GameSession g ? g.DebugSummary() : $"screen={_screen?.Name}"));
                GetTree().Quit();
            };
        }
        if (args.TryGetValue("--autostart", out var map)) StartSolo(map, tutorial: args.ContainsKey("--tutorial"));
        else if (args.TryGetValue("--sandbox", out var sandboxMap)) StartSandbox(sandboxMap == "" ? "funnel" : sandboxMap);
        else if (args.ContainsKey("--showcase"))
        {
            ShowcaseMode = true;
            StartSandbox("funnel");
        }
        else if (args.TryGetValue("--autohost", out var hostMap)) Switch(new LobbyScreen(this, true, "", autoStartMap: hostMap) { Name = "Lobby" });
        else if (args.TryGetValue("--autojoin", out var address)) ShowLobby(host: false, address);
    }

    /// <summary>Current screen and, in a game, its state summary (attached to in-game issue reports).</summary>
    public string DescribeScreen() =>
        _screen is GameSession g ? $"screen={_screen.Name} {g.DebugSummary()}" : $"screen={_screen?.Name ?? "none"}";

    public void ApplyDisplaySettings()
    {
        var current = DisplayServer.WindowGetMode();
        bool isFullscreen = current is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        // Only switch when fullscreen actually changes, so a maximized window stays maximized.
        if (Settings.Fullscreen && !isFullscreen) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        else if (!Settings.Fullscreen && isFullscreen) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
    }

    /// <summary>Modifiers from the local player's skill tree.</summary>
    public Modifiers LocalModifiers() => Tree.ToModifiers(Profile.Allocated);

    public void ShowMainMenu(string? notice = null)
    {
        Net.Close();
        GetTree().Paused = false;
        Engine.TimeScale = 1;
        Switch(new MainMenu(this, notice) { Name = "MainMenu" });
    }

    public void ShowSkillTree() => Switch(new SkillTreeScreen(this) { Name = "SkillTree" });

    public void ShowLobby(bool host, string address = "") => Switch(new LobbyScreen(this, host, address) { Name = "Lobby" });

    public void StartSolo(string mapId, bool tutorial) =>
        StartGame(new GameConfig(mapId, Time.GetTicksUsec(), 1, 0, tutorial && mapId == "funnel", LocalModifiers(), null));

    public void StartSandbox(string mapId) =>
        StartGame(new GameConfig(mapId, Time.GetTicksUsec(), 1, 0, false, LocalModifiers(), null, Sandbox: true));

    public void StartGame(GameConfig cfg) => Switch(new GameSession(this, cfg) { Name = "Game" });

    private void Switch(Node screen)
    {
        _screen?.QueueFree();
        _screen = screen;
        AddChild(screen);
    }

    public static string ReadData(string file)
    {
        string path = $"res://data/{file}";
        if (!Godot.FileAccess.FileExists(path)) throw new FileNotFoundException($"Missing game data: {path}");
        return Godot.FileAccess.GetFileAsString(path);
    }
}
