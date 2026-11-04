namespace BeltAndBarricade.Sim;

/// <summary>
/// Takes items in (from a belt behind it or an inserter) and deals them out round-robin to the tiles
/// ahead, left and right. Never sends an item back the way it came.
/// </summary>
public sealed class Splitter : Building, IBeltInput
{
    private const int BufferSize = 2;
    private readonly List<string> _buffer = new();
    private int _next;

    /// <summary>Holding items to pass on (the view plays its working loop).</summary>
    public bool Busy => _buffer.Count > 0;

    public bool CanAccept(string item) => _buffer.Count < BufferSize;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        _buffer.Add(item);
        return true;
    }

    public override void Tick(World w)
    {
        if (_buffer.Count == 0) return;
        Span<Direction> outs = stackalloc Direction[] { Dir, Dir.RotateCw(), Dir.RotateCw().Opposite() };
        for (int i = 0; i < outs.Length; i++)
        {
            var d = outs[(_next + i) % outs.Length];
            if (!PushOnto(w, this, Origin + d.Offset(), _buffer[0])) continue;
            _buffer.RemoveAt(0);
            _next = (_next + i + 1) % outs.Length;
            return;
        }
    }

    public override void CollectContents(Inventory into)
    {
        foreach (var it in _buffer) into.Add(it);
        _buffer.Clear();
    }

    public override void HashInto(StateHash h)
    {
        h.Add(_next);
        foreach (var it in _buffer) h.Add(it);
    }
}

/// <summary>
/// Belt items entering here travel underground to the nearest <see cref="TunnelExit"/> ahead (same facing,
/// within reach). Tunnels don't block anything, so walls can sit on top of the run.
/// </summary>
public sealed class TunnelEntrance : Building, IBeltInput
{
    private const int Capacity = 4;
    private const int TicksPerTile = 8;
    private readonly Queue<(string Item, long Arrive)> _queue = new();
    private long _lastTick;

    /// <summary>Items are travelling underground from here (the view plays its working loop).</summary>
    public bool Busy => _queue.Count > 0;

    public TunnelExit? FindExit(World w)
    {
        for (int i = 1; i <= Def.TunnelReach; i++)
            if (w.BuildingAt(Origin + new Int2(Dir.Offset().X * i, Dir.Offset().Y * i)) is TunnelExit exit && exit.Dir == Dir)
                return exit;
        return null;
    }

    public bool CanAccept(string item) => _queue.Count < Capacity;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        _queue.Enqueue((item, _lastTick + TicksPerTile * Def.TunnelReach));
        return true;
    }

    public override void Tick(World w)
    {
        _lastTick = w.Tick;
        if (_queue.Count == 0) return;
        var (item, arrive) = _queue.Peek();
        if (w.Tick < arrive) return;
        if (FindExit(w) is { } exit && exit.TryInsert(item)) _queue.Dequeue();
    }

    public override void CollectContents(Inventory into)
    {
        foreach (var (item, _) in _queue) into.Add(item);
        _queue.Clear();
    }

    public override void HashInto(StateHash h)
    {
        foreach (var (item, arrive) in _queue)
        {
            h.Add(item);
            h.Add(arrive);
        }
    }
}

/// <summary>Emits tunnelled items onto the tile it faces.</summary>
public sealed class TunnelExit : Building, IItemAcceptor
{
    private const int Capacity = 4;
    private readonly List<string> _buffer = new();

    /// <summary>Holding items to put out (the view plays its working loop).</summary>
    public bool Busy => _buffer.Count > 0;

    public bool CanAccept(string item) => _buffer.Count < Capacity;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        _buffer.Add(item);
        return true;
    }

    public override void Tick(World w)
    {
        if (_buffer.Count > 0 && PushOnto(w, this, Front, _buffer[0])) _buffer.RemoveAt(0);
    }

    public override void CollectContents(Inventory into)
    {
        foreach (var it in _buffer) into.Add(it);
        _buffer.Clear();
    }

    public override void HashInto(StateHash h)
    {
        foreach (var it in _buffer) h.Add(it);
    }
}

/// <summary>
/// Remote storage that joins the shared network: its contents count toward building costs, research and repairs,
/// just like the Nexus. Belts can feed it directly; inserters can pull from it with a filter.
/// </summary>
public sealed class Depot : Building, IBeltInput, IItemSource
{
    public Inventory Storage { get; } = new();
    /// <summary>Running total of everything delivered by belts and inserters (UI only, not hashed).</summary>
    public Inventory Received { get; } = new();

    public bool CanAccept(string item) => Storage.Total < Def.Capacity;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        Storage.Add(item);
        Received.Add(item);
        return true;
    }

    public bool TryTake(Func<string, bool> want, out string item)
    {
        foreach (var (id, _) in Storage.Entries)
        {
            if (!want(id)) continue;
            Storage.Remove(id);
            item = id;
            return true;
        }
        item = "";
        return false;
    }

    public override string Status => $"Stored {Storage.Total} / {Def.Capacity}";

    public override void CollectContents(Inventory into)
    {
        into.AddAll(Storage);
        Storage.Clear();
    }

    public override void HashInto(StateHash h) => Storage.HashInto(h);
}
