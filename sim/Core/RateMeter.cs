namespace BeltAndBarricade.Sim;

/// <summary>
/// Counts items per minute over a sliding one-minute window, for the UI ("29 iron ore/min").
/// Not part of the simulation state (it never influences gameplay), so it isn't hashed.
/// </summary>
public sealed class RateMeter
{
    private const int Window = 60 * World.TicksPerSecond;

    private readonly SortedDictionary<string, Queue<long>> _events = new(StringComparer.Ordinal);
    private long _since = -1;

    public void Start(long tick)
    {
        if (_since < 0) _since = tick;
    }

    public void Add(string item, long tick, int count = 1)
    {
        if (!_events.TryGetValue(item, out var q)) _events[item] = q = new Queue<long>();
        for (int i = 0; i < count; i++) q.Enqueue(tick);
    }

    /// <summary>Items per minute over the last minute (scaled up while the building is younger than a minute).</summary>
    public float PerMinute(string item, long now)
    {
        if (!_events.TryGetValue(item, out var q)) return 0;
        while (q.Count > 0 && q.Peek() <= now - Window) q.Dequeue();
        long span = Math.Clamp(now - Math.Max(_since, 0), 10 * World.TicksPerSecond, Window);
        return q.Count * (float)Window / span;
    }

    public IEnumerable<(string Item, float PerMinute)> All(long now) =>
        _events.Keys.ToList().Select(k => (k, PerMinute(k, now))).Where(r => r.Item2 > 0);
}
