using System.Text.Json.Nodes;
using BeltAndBarricade.Net;
using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// Co-op lobby. The host picks the map and starts; the start message carries the map, seed and the host's
/// skill-tree modifiers so both machines build an identical world.
/// </summary>
public partial class LobbyScreen : Control
{
    private readonly App _app;
    private readonly bool _host;
    private readonly string _address;
    private Label _status = null!;
    private Button? _start;
    private OptionButton? _map;
    private readonly string? _autoStartMap;

    /// <param name="autoStartMap">Host only: start this map as soon as a player joins (automation).</param>
    public LobbyScreen(App app, bool host, string address, string? autoStartMap = null)
    {
        _app = app;
        _host = host;
        _address = address;
        _autoStartMap = autoStartMap;
    }

    private NetSession Net => _app.Net;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = UiKit.Backdrop };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new MenuBackdrop());

        var col = UiKit.Column(12);
        col.AddChild(UiKit.Label(_host ? "Hosting a co-op game" : "Joining a co-op game", 28, UiKit.Accent));
        _status = UiKit.Label("", 17, UiKit.Text, wrap: true);
        col.AddChild(_status);

        if (_host)
        {
            var ips = string.Join(", ", NetSession.LocalAddresses());
            col.AddChild(UiKit.Label($"Your address: {(ips == "" ? "unknown" : ips)}   Port: {NetSession.DefaultPort}", 16, UiKit.Gold, wrap: true));
            col.AddChild(UiKit.Label("Map", 16, UiKit.Dim));
            _map = new OptionButton();
            foreach (var (id, sc) in _app.Scenarios) _map.AddItem(sc.Map.Name);
            col.AddChild(_map);
            col.AddChild(UiKit.Label("Your skill tree bonuses apply to both players.", 14, UiKit.Dim));
            _start = UiKit.Button("Start game", StartAsHost, width: 260);
            _start.Disabled = true;
            col.AddChild(_start);
        }
        col.AddChild(UiKit.Button("Back", () => _app.ShowMainMenu(), danger: true, width: 260));
        AddChild(UiKit.Centered(UiKit.Panel(col, 520)));

        Net.Connected += OnConnected;
        Net.Disconnected += OnDisconnected;
        Net.MessageReceived += OnMessage;

        var err = _host ? Net.Host() : Net.Join(_address);
        _status.Text = err != Error.Ok
            ? $"Could not {(_host ? "host" : "connect")}: {err}"
            : _host ? "Waiting for a player to join..." : $"Connecting to {_address}:{NetSession.DefaultPort}...";
    }

    public override void _ExitTree()
    {
        Net.Connected -= OnConnected;
        Net.Disconnected -= OnDisconnected;
        Net.MessageReceived -= OnMessage;
    }

    private void OnConnected()
    {
        Net.Send(new JsonObject { ["t"] = "hello", ["version"] = NetSession.ProtocolVersion });
        _status.Text = _host ? "A player has joined! Pick a map and start." : "Connected. Waiting for the host to start...";
        if (_start != null) _start.Disabled = false;
        if (_host && _autoStartMap != null) CallDeferred(MethodName.StartAsHost);
    }

    private void OnDisconnected()
    {
        _status.Text = _host ? "The player left. Waiting for a player to join..." : "Disconnected from host.";
        if (_start != null) _start.Disabled = true;
    }

    private void OnMessage(JsonObject msg)
    {
        switch ((string?)msg["t"])
        {
            case "hello" when (int)msg["version"]! != NetSession.ProtocolVersion:
                _status.Text = "Version mismatch: both players need the same build of the game.";
                if (_start != null) _start.Disabled = true;
                break;
            case "start" when !_host:
                var mods = new Modifiers();
                foreach (var (stat, value) in msg["mods"]!.AsObject()) mods.Add(stat, (float)value!);
                _app.StartGame(new GameConfig((string)msg["map"]!, ulong.Parse((string)msg["seed"]!), 2, 1, false, mods, Net));
                break;
        }
    }

    private void StartAsHost()
    {
        string mapId = _autoStartMap ?? _app.Scenarios.Keys.ElementAt(_map!.Selected);
        ulong seed = Time.GetTicksUsec();
        var mods = _app.LocalModifiers();
        var modsJson = new JsonObject();
        foreach (var (stat, value) in mods.Bonuses) modsJson[stat] = value;
        Net.Send(new JsonObject { ["t"] = "start", ["map"] = mapId, ["seed"] = seed.ToString(), ["mods"] = modsJson });
        _app.StartGame(new GameConfig(mapId, seed, 2, 0, false, mods, Net));
    }
}
