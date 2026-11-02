using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace BeltAndBarricade.Net;

/// <summary>
/// Two-player connection over ENet (direct IP / LAN). Messages are small JSON objects with a "t" type field.
/// This is the only transport-specific class: swapping in Steam networking (GodotSteam / Steamworks.NET)
/// means reimplementing Host/Join/Send/poll here; the lockstep sync above it stays the same.
/// </summary>
public partial class NetSession : Node
{
    public const int DefaultPort = 7777;
    public const int ProtocolVersion = 1;

    private ENetMultiplayerPeer? _peer;
    private int _remoteId;

    public bool IsHost { get; private set; }
    public bool IsConnected => _remoteId != 0;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<JsonObject>? MessageReceived;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public Error Host(int port = DefaultPort)
    {
        Close();
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateServer(port, 1);
        if (err != Error.Ok) return err;
        IsHost = true;
        Hook();
        return Error.Ok;
    }

    public Error Join(string address, int port = DefaultPort)
    {
        Close();
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateClient(address, port);
        if (err != Error.Ok) return err;
        IsHost = false;
        Hook();
        return Error.Ok;
    }

    private void Hook()
    {
        _peer!.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        _peer.PeerConnected += id =>
        {
            _remoteId = (int)id;
            Connected?.Invoke();
        };
        _peer.PeerDisconnected += id =>
        {
            if (id != _remoteId) return;
            _remoteId = 0;
            Disconnected?.Invoke();
        };
    }

    public void Send(JsonObject msg)
    {
        if (_peer == null || _remoteId == 0) return;
        _peer.SetTargetPeer(_remoteId);
        _peer.PutPacket(Encoding.UTF8.GetBytes(msg.ToJsonString()));
    }

    public override void _Process(double delta)
    {
        if (_peer == null) return;
        _peer.Poll();
        while (_peer != null && _peer.GetAvailablePacketCount() > 0)
        {
            var bytes = _peer.GetPacket();
            if (JsonNode.Parse(Encoding.UTF8.GetString(bytes)) is JsonObject msg) MessageReceived?.Invoke(msg);
        }
    }

    public void Close()
    {
        if (_peer == null) return;
        if (_remoteId != 0) Send(new JsonObject { ["t"] = "bye" });
        _peer.Close();
        _peer = null;
        _remoteId = 0;
    }

    public override void _ExitTree() => Close();

    /// <summary>This machine's LAN addresses, for telling a friend where to connect.</summary>
    public static IEnumerable<string> LocalAddresses() => IP.GetLocalAddresses()
        .Where(a => a.Contains('.') && !a.StartsWith("127.") && !a.StartsWith("169.254."));
}
