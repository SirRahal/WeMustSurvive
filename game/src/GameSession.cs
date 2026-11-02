using System.Text.Json.Nodes;
using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
using BeltAndBarricade.View;
using Godot;

namespace BeltAndBarricade;

/// <summary>
/// One game in progress. Owns the sim <see cref="World"/> and its <see cref="LockstepPeer"/> (local, co-op host or
/// co-op client), steps it at the fixed physics rate (60 Hz), and wires the view, input, camera, HUD and menu.
/// </summary>
public partial class GameSession : Node2D
{
    /// <summary>Player index controlled on this machine (0 solo/host, 1 joining player).</summary>
    public static int LocalPlayer { get; private set; }

    private const int MaxCatchUpSteps = 4;

    private readonly App _app;
    private GameConfig _cfg;
    private World _world = null!;
    private LockstepPeer _peer = null!;
    private Tutorial? _tutorial;
    private int _focusedStep = -1;
    private bool _resultRecorded;

    private TerrainView _terrain = null!;
    private WorldView _view = null!;
    private PlayerInput _input = null!;
    private CameraController _camera = null!;
    private Hud _hud = null!;
    private PauseMenu _menu = null!;

    private bool IsCoop => _cfg.Net != null;
    private bool IsHost => _cfg.Net is not { IsHost: false };

    public GameSession(App app, GameConfig cfg)
    {
        _app = app;
        _cfg = cfg;
    }

    public override void _Ready()
    {
        AddChild(_terrain = new TerrainView { Name = "Terrain" });
        AddChild(_view = new WorldView { Name = "World" });
        AddChild(_input = new PlayerInput { Name = "Input", Submit = cmd => _peer.Submit(cmd), Sandbox = _cfg.Sandbox });
        AddChild(_camera = new CameraController { Name = "Camera" });
        var hudLayer = new CanvasLayer { Name = "HudLayer" };
        AddChild(hudLayer);
        hudLayer.AddChild(_hud = new Hud { Name = "Hud", Camera = _camera, Settings = _app.Settings, IsCoop = IsCoop, Sandbox = _cfg.Sandbox });
        _input.Hud = _hud;
        _hud.TutorialNextRequested += () => _tutorial?.Next(_world);
        _hud.TutorialSkipRequested += SkipTutorial;

        var menuLayer = new CanvasLayer { Name = "MenuLayer", Layer = 10 };
        AddChild(menuLayer);
        menuLayer.AddChild(_menu = new PauseMenu { Name = "PauseMenu", Settings = _app.Settings, IsCoop = IsCoop });
        WireMenu();

        if (_cfg.Net is { } net)
        {
            net.MessageReceived += OnNetMessage;
            net.Disconnected += OnPartnerLeft;
        }

        ApplySettings();
        StartWorld();
    }

    public override void _ExitTree()
    {
        if (_cfg.Net is { } net)
        {
            net.MessageReceived -= OnNetMessage;
            net.Disconnected -= OnPartnerLeft;
        }
        Engine.TimeScale = 1;
    }

    private void StartWorld()
    {
        LocalPlayer = _cfg.LocalPlayer;
        var scenario = _app.Scenarios[_cfg.MapId];
        _world = new World(_app.Data, scenario, _cfg.Seed, _cfg.PlayerCount, _cfg.Mods);
        if (_cfg.Sandbox)
        {
            foreach (var t in _app.Data.TechList) _world.Research.Grant(t.Id);
            foreach (var item in _app.Data.Items.Keys) _world.Storage.Add(item, 5000);
            _world.Waves.Held = true;
            if (_app.ShowcaseMode) BuildShowcase();
        }
        _peer = CreatePeer();
        _tutorial = _cfg.Tutorial && !IsCoop ? new Tutorial(_app.Tutorial, _world, LocalPlayer) : null;
        _focusedStep = -1;
        _resultRecorded = false;

        _terrain.Bind(_world);
        _input.Bind(_world);
        _view.Bind(_world, _input);
        _hud.Bind(_world, _input, _peer);
        _input.Tutorial = _view.Tutorial = _hud.Tutorial = _tutorial;
        _camera.SetMapSize(_world.Map.Width, _world.Map.Height);
        _camera.CenterOn(Iso.ToScreen(_world.Engineers[LocalPlayer].Pos));
    }

    private LockstepPeer CreatePeer()
    {
        if (_cfg.Net is not { } net) return new LocalPeer(_world);
        if (net.IsHost)
        {
            var host = new LockstepHost(_world);
            host.BatchReady += b => net.Send(new JsonObject
            {
                ["t"] = "batch",
                ["tick"] = b.Tick,
                ["cmds"] = new JsonArray(b.Commands.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            });
            host.HashReady += (tick, hash) => net.Send(new JsonObject { ["t"] = "hash", ["tick"] = tick, ["hash"] = hash.ToString() });
            return host;
        }
        var client = new LockstepClient(_world);
        client.SendToHost += json => net.Send(new JsonObject { ["t"] = "cmd", ["cmd"] = json });
        return client;
    }

    // ------------------------------------------------------------------ simulation loop

    public override void _PhysicsProcess(double delta)
    {
        _input.Poll();
        SmokeCommands();
        int steps = 0;
        while (_peer.CanStep && steps < (IsHost ? 1 : MaxCatchUpSteps))
        {
            _peer.Step();
            if (_world.Tick % 15 == 0) _view.Issues = _hud.Issues = LogisticsCheck.Analyze(_world);
            _view.Consume(_world.Events);
            _hud.Consume(_world.Events);
            UpdateTutorial();
            steps++;
        }
        RecordResultOnce();
    }

    /// <summary>Advances the tutorial and flies the camera to each new step's focus point.</summary>
    private void UpdateTutorial()
    {
        if (_tutorial == null) return;
        _tutorial.Update(_world);
        if (_tutorial.Finished)
        {
            MarkTutorialDone();
            return;
        }
        if (_tutorial.StepIndex == _focusedStep) return;
        _focusedStep = _tutorial.StepIndex;
        var step = _tutorial.Current;
        _camera.FlyTo(Iso.ToScreen(step.Focus.ToVector()), step.Zoom);
    }

    private void SkipTutorial()
    {
        _tutorial?.Skip(_world);
        MarkTutorialDone();
    }

    private void MarkTutorialDone()
    {
        if (_app.Profile.TutorialDone) return;
        _app.Profile.TutorialDone = true;
        _app.Profile.Save();
    }

    /// <summary>--showcase (debug): one of every building near the Nexus, powered, plus a few enemies to shoot at.</summary>
    private void BuildShowcase()
    {
        var w = _world;
        var n = w.Nexus.Origin; // Funnel: (39,63)
        void At(string id, int dx, int dy, Direction d = Direction.East) => w.PlaceDirect(id, new Int2(n.X + dx, n.Y + dy), d);
        At("pole", 4, -3);
        At("pole", -4, -3);
        var gen = (Generator)w.PlaceDirect("generator", new Int2(n.X + 6, n.Y - 4));
        for (int i = 0; i < 10; i++) gen.TryInsert("coal");
        At("generator", -6, -4);
        string[] row1 = { "electric_furnace", "furnace", "workshop", "lab", "depot", "electric_drill", "advanced_drill" };
        for (int i = 0; i < row1.Length; i++) if (row1[i].Contains("drill")) { } else At(row1[i], -6 + i * 2, -6);
        string[] walls = { "wall", "reinforced_wall", "heavy_wall", "fortress_wall" };
        for (int i = 0; i < walls.Length; i++) At(walls[i], -3 + i, -12);
        string[] turrets = { "gun_turret", "laser_turret", "rocket_turret", "flame_turret", "heavy_turret" };
        for (int i = 0; i < turrets.Length; i++)
        {
            var t = w.PlaceDirect(turrets[i], new Int2(n.X - 5 + i * 3, n.Y - 9));
            if (t is Turret tu && tu.UsesAmmo) for (int k = 0; k < 10; k++) tu.TryInsert(tu.Def.AmmoItem!);
        }
        for (int x = -8; x <= -4; x++) At(x == -6 ? "splitter" : "belt", x, 1);
        At("tunnel_entrance", -8, 3);
        At("wall", -7, 3);
        At("tunnel_exit", -6, 3);
        At("inserter", -3, 1, Direction.North);
        At("fast_inserter", -3, 2, Direction.South);
        w.PlaceDirect("satellite_uplink", new Int2(n.X + 5, n.Y + 1));
        w.PlaceDirect("nuclear_silo", new Int2(n.X + 8, n.Y + 1));
        foreach (var e in new[] { "grunt", "runner", "brute", "spitter", "sapper" })
            for (int i = 0; i < 2; i++) w.SpawnEnemy(w.Data.Enemies[e], new System.Numerics.Vector2(n.X + 1.5f + i * 2, n.Y - 24f - i));
    }

    /// <summary>One-line state summary for automated smoke tests.</summary>
    public string DebugSummary()
    {
        string desync = _peer is LockstepClient c ? c.DesyncTick?.ToString() ?? "none" : "n/a";
        return $"role={(IsCoop ? IsHost ? "host" : "client" : "solo")} map={_cfg.MapId} tick={_world.Tick} buildings={_world.Buildings.Count} " +
               $"enemies={_world.Enemies.Count} wave={_world.Waves.WavesStarted} outcome={_world.Outcome} desync={desync}";
    }

    /// <summary>--smoke: issue a few commands on a fixed schedule so automated runs exercise the sim and sync.</summary>
    private void SmokeCommands()
    {
        if (!_app.SmokeMode) return;
        long t = _world.Tick;
        var nexus = _world.Nexus.Origin;
        if (t == 30 && IsHost) _peer.Submit(new EndPrepCommand(LocalPlayer));
        if (t % 120 == 60 && t < 2400)
        {
            int i = (int)(t / 120);
            var tile = new Int2(nexus.X - 6 + i % 12, nexus.Y - 4 - LocalPlayer * 2);
            _peer.Submit(new PlaceBuildingCommand(LocalPlayer, "wall", tile, Direction.North));
        }
        if (t % 300 == 150) _peer.Submit(new PingCommand(LocalPlayer, PingKind.Danger, _world.Nexus.Center));
    }

    private void RecordResultOnce()
    {
        if (_resultRecorded || _world.Outcome == Outcome.Playing) return;
        _resultRecorded = true;
        if (_cfg.Sandbox) return; // no rewards for sandbox games
        bool won = _world.Outcome == Outcome.Won;
        _hud.MetaEarned = _app.Profile.RecordResult(_world.MapDef, won, _world.Waves.WavesStarted);
    }

    // ------------------------------------------------------------------ co-op messages

    private void OnNetMessage(JsonObject msg)
    {
        switch ((string?)msg["t"])
        {
            case "batch" when _peer is LockstepClient client:
                var cmds = msg["cmds"]!.AsArray().Select(n => (string)n!).ToList();
                client.ReceiveBatch(new TickBatch((long)msg["tick"]!, cmds));
                break;
            case "hash" when _peer is LockstepClient client:
                client.ReceiveHash((long)msg["tick"]!, ulong.Parse((string)msg["hash"]!));
                break;
            case "cmd" when _peer is LockstepHost host:
                host.ReceiveFromClient((string)msg["cmd"]!);
                break;
            case "restart" when !IsHost:
                _cfg = _cfg with { Seed = ulong.Parse((string)msg["seed"]!) };
                Resume();
                StartWorld();
                break;
            case "bye":
                OnPartnerLeft();
                break;
        }
    }

    private void OnPartnerLeft()
    {
        if (IsHost) _hud.Notify("Your partner left. Their engineer stands idle.");
        else _app.ShowMainMenu("The host left the game.");
    }

    // ------------------------------------------------------------------ pause menu

    private void WireMenu()
    {
        _menu.ResumeRequested += Resume;
        _menu.RestartRequested += Restart;
        _menu.ReplayTutorialRequested += () => { Resume(); _app.StartSolo("funnel", tutorial: true); };
        _menu.SkipTutorialRequested += () => { SkipTutorial(); Resume(); };
        _menu.SurrenderRequested += () => { _peer.Submit(new SurrenderCommand(LocalPlayer)); Resume(); };
        _menu.QuitToMenuRequested += () => _app.ShowMainMenu();
        _menu.QuitRequested += () => GetTree().Quit();
        _menu.SettingsChanged += () => { ApplySettings(); _app.Settings.Save(); };
    }

    private void Restart()
    {
        Resume();
        _cfg = _cfg with { Seed = Time.GetTicksUsec() };
        _cfg.Net?.Send(new JsonObject { ["t"] = "restart", ["seed"] = _cfg.Seed.ToString() });
        StartWorld();
    }

    private void Pause()
    {
        _menu.CanSurrender = _world.Outcome == Outcome.Playing;
        _menu.CanRestart = IsHost;
        _menu.TutorialActive = _tutorial is { Finished: false };
        if (!IsCoop) GetTree().Paused = true;
        _menu.Open();
    }

    private void Resume()
    {
        _menu.Close();
        GetTree().Paused = false;
    }

    private void ApplySettings()
    {
        // In co-op only the host's speed matters (the client follows the host's batches).
        Engine.TimeScale = IsCoop && !IsHost ? 1f : _app.Settings.GameSpeed;
        _camera.EdgePan = _app.Settings.EdgePan;
        _app.ApplyDisplaySettings();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            // Reaches here only if nothing (blueprint, selection, panel) consumed Esc first.
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                Pause();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.C }:
                _camera.CenterOn(Iso.ToScreen(_world.Engineers[LocalPlayer].Pos));
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.H }:
                _app.Settings.ShowHelp = !_app.Settings.ShowHelp;
                _app.Settings.Save();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter } when _world.Outcome != Outcome.Playing && IsHost:
                Restart();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter } when _tutorial is { NeedsNext: true }:
                _tutorial.Next(_world);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }
}
