namespace BeltAndBarricade.Sim;

/// <summary>
/// Waves arrive on a strict timer. The countdown to the next wave is the preparation window,
/// which players may end early. Spawn counts are scaled by the world's threat factor (solo vs co-op).
/// </summary>
public sealed class WaveDirector
{
    private sealed class ActiveGroup
    {
        public required EnemyDef Enemy;
        public int Remaining;
        public int Interval;
        public int Timer;
    }

    private readonly WaveSchedule _schedule;
    private readonly List<ActiveGroup> _active = new();

    /// <summary>Number of waves that have started (1-based index of the current wave).</summary>
    public int WavesStarted { get; private set; }
    public int TotalWaves => _schedule.Waves.Count;
    public int TicksUntilNextWave { get; private set; }
    public bool HasMoreWaves => WavesStarted < TotalWaves;
    public bool IsSpawning => _active.Count > 0;
    public bool AllSpawned => !HasMoreWaves && !IsSpawning;
    /// <summary>While held (e.g. during the tutorial), the countdown to the next wave is frozen.</summary>
    public bool Held { get; set; }

    public WaveDirector(WaveSchedule schedule)
    {
        _schedule = schedule;
        TicksUntilNextWave = schedule.FirstWaveTicks;
    }

    public void Tick(World w)
    {
        if (HasMoreWaves && !Held && --TicksUntilNextWave <= 0) StartWave(w);

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var g = _active[i];
            if (--g.Timer > 0) continue;
            w.SpawnEnemy(g.Enemy);
            g.Timer = g.Interval;
            if (--g.Remaining == 0) _active.RemoveAt(i);
        }
    }

    /// <summary>Enemy counts (after solo/co-op scaling) of the next wave to start, or empty if none remain.</summary>
    public IEnumerable<(EnemyDef Enemy, int Count)> PreviewNext(World w)
    {
        if (!HasMoreWaves) yield break;
        foreach (var g in _schedule.Waves[WavesStarted].Groups)
            yield return (w.Data.Enemies[g.Enemy], Math.Max(1, (int)MathF.Round(g.Count * w.ThreatAxis)));
    }

    /// <summary>Skips the rest of the preparation window.</summary>
    public bool EndPrepEarly()
    {
        if (!HasMoreWaves || IsSpawning) return false;
        TicksUntilNextWave = 1;
        return true;
    }

    private void StartWave(World w)
    {
        var wave = _schedule.Waves[WavesStarted];
        WavesStarted++;
        TicksUntilNextWave = _schedule.BetweenWaveTicks;
        foreach (var g in wave.Groups)
        {
            int count = Math.Max(1, (int)MathF.Round(g.Count * w.ThreatAxis));
            _active.Add(new ActiveGroup { Enemy = w.Data.Enemies[g.Enemy], Remaining = count, Interval = g.IntervalTicks, Timer = 1 });
        }
        w.Events.Add(new WaveStartedEvent(WavesStarted));
    }

    public void HashInto(StateHash h)
    {
        h.Add(WavesStarted);
        h.Add(TicksUntilNextWave);
        h.Add(Held ? 1 : 0);
        foreach (var g in _active)
        {
            h.Add(g.Enemy.Id);
            h.Add(g.Remaining);
            h.Add(g.Timer);
        }
    }
}
