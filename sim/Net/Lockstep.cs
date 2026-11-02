namespace BeltAndBarricade.Sim;

/// <summary>A tick's worth of commands, as decided by the host.</summary>
public sealed record TickBatch(long Tick, List<string> Commands);

/// <summary>
/// Co-op synchronisation, independent of the transport (ENet today, Steam later).
///
/// The host is the authority on <i>which</i> commands run on <i>which</i> tick: every player's commands are sent to the
/// host, which schedules them <see cref="Delay"/> ticks ahead and broadcasts a batch for every tick. Both
/// machines then run the identical deterministic simulation from identical inputs. Every
/// <see cref="HashInterval"/> ticks the host publishes a state hash so clients can detect a desync.
/// </summary>
public abstract class LockstepPeer
{
    /// <summary>Ticks between a command being issued and being executed (absorbs network latency).</summary>
    public const int Delay = 6;
    public const int HashInterval = 120;

    protected readonly World World;
    protected readonly Dictionary<long, List<ICommand>> Schedule = new();

    protected LockstepPeer(World world) => World = world;

    /// <summary>Set when a hash mismatch was detected (client only).</summary>
    public long? DesyncTick { get; protected set; }

    /// <summary>A local player's command. The host schedules it; a client sends it to the host.</summary>
    public abstract void Submit(ICommand cmd);

    /// <summary>Whether the world can advance one tick right now.</summary>
    public abstract bool CanStep { get; }

    /// <summary>Applies this tick's scheduled commands and steps the world.</summary>
    public virtual void Step()
    {
        if (Schedule.Remove(World.Tick, out var cmds))
            foreach (var c in cmds) World.Enqueue(c);
        World.Step();
    }
}

public sealed class LockstepHost : LockstepPeer
{
    private readonly List<ICommand> _incoming = new();

    /// <summary>Called with each batch to broadcast to clients.</summary>
    public event Action<TickBatch>? BatchReady;
    /// <summary>Called with (tick, hash) every <see cref="LockstepPeer.HashInterval"/> ticks, after stepping that tick.</summary>
    public event Action<long, ulong>? HashReady;

    public LockstepHost(World world) : base(world) { }

    public override void Submit(ICommand cmd) => _incoming.Add(cmd);

    /// <summary>A command received from a client.</summary>
    public void ReceiveFromClient(string json) => _incoming.Add(CommandCodec.Deserialize(json));

    public override bool CanStep => true;

    public override void Step()
    {
        long execTick = World.Tick + Delay;
        var batch = new TickBatch(execTick, _incoming.Select(CommandCodec.Serialize).ToList());
        if (_incoming.Count > 0) Schedule[execTick] = new List<ICommand>(_incoming);
        _incoming.Clear();
        BatchReady?.Invoke(batch);

        long tick = World.Tick;
        base.Step();
        if (tick % HashInterval == 0 && World.Tick > tick) HashReady?.Invoke(tick, World.ComputeHash());
    }
}

public sealed class LockstepClient : LockstepPeer
{
    private readonly Dictionary<long, ulong> _myHashes = new();
    private readonly Dictionary<long, ulong> _hostHashes = new();
    private long _latestBatch = Delay - 1; // ticks before the first delay window never have commands

    /// <summary>Called with each local command, to send to the host.</summary>
    public event Action<string>? SendToHost;

    public LockstepClient(World world) : base(world) { }

    public override void Submit(ICommand cmd) => SendToHost?.Invoke(CommandCodec.Serialize(cmd));

    public void ReceiveBatch(TickBatch batch)
    {
        if (batch.Commands.Count > 0) Schedule[batch.Tick] = batch.Commands.Select(CommandCodec.Deserialize).ToList();
        _latestBatch = Math.Max(_latestBatch, batch.Tick);
    }

    public void ReceiveHash(long tick, ulong hash)
    {
        _hostHashes[tick] = hash;
        Compare(tick);
    }

    /// <summary>How many ticks the client could step right now (it lags the host by up to this much).</summary>
    public long TicksAvailable => _latestBatch - World.Tick + 1;

    public override bool CanStep => World.Tick <= _latestBatch;

    public override void Step()
    {
        long tick = World.Tick;
        base.Step();
        if (tick % HashInterval == 0 && World.Tick > tick)
        {
            _myHashes[tick] = World.ComputeHash();
            Compare(tick);
        }
    }

    private void Compare(long tick)
    {
        if (!_myHashes.TryGetValue(tick, out var mine) || !_hostHashes.TryGetValue(tick, out var theirs)) return;
        if (mine != theirs) DesyncTick ??= tick;
        _myHashes.Remove(tick);
        _hostHashes.Remove(tick);
    }
}

/// <summary>Single-player: commands simply run on the next tick.</summary>
public sealed class LocalPeer : LockstepPeer
{
    public LocalPeer(World world) : base(world) { }
    public override void Submit(ICommand cmd) => World.Enqueue(cmd);
    public override bool CanStep => true;
}
