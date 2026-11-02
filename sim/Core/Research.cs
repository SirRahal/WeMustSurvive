namespace BeltAndBarricade.Sim;

/// <summary>
/// Shared research. Starting a technology pays its cost from the storage network; labs then work through
/// its ticks (each lab adds its research rate per tick, scaled by the research_speed bonus).
/// </summary>
public sealed class ResearchState
{
    private readonly HashSet<string> _completed = new();

    public TechDef? Current { get; private set; }
    public float Progress { get; private set; }
    public IReadOnlyCollection<string> Completed => _completed;
    public float Fraction => Current == null ? 0 : Math.Min(1f, Progress / Current.Ticks);

    public bool IsDone(string techId) => _completed.Contains(techId);

    public bool IsUnlocked(BuildingDef def) => def.Tech == null || _completed.Contains(def.Tech);

    public bool PrerequisitesMet(TechDef t) => t.Requires.All(_completed.Contains);

    /// <summary>Why <paramref name="t"/> can't be started, or null if it can.</summary>
    public string? CheckStart(World w, TechDef t)
    {
        if (_completed.Contains(t.Id)) return $"{t.Name} is already researched";
        if (Current != null) return $"Already researching {Current.Name}";
        if (!PrerequisitesMet(t))
            return "Requires " + string.Join(", ", t.Requires.Where(r => !_completed.Contains(r)).Select(r => w.Data.Techs[r].Name));
        string? missing = w.Network.FirstMissing(t.Cost);
        return missing == null ? null : $"Not enough {w.Data.Items[missing].Name}";
    }

    public string? Start(World w, string techId)
    {
        if (!w.Data.Techs.TryGetValue(techId, out var t)) return $"Unknown research '{techId}'";
        string? error = CheckStart(w, t);
        if (error != null) return error;
        w.Network.Remove(t.Cost);
        Current = t;
        Progress = 0;
        return null;
    }

    /// <summary>Stops the current research and refunds its cost to the Nexus.</summary>
    public string? Cancel(World w)
    {
        if (Current == null) return "Nothing is being researched";
        w.Storage.AddAll(Current.Cost);
        Current = null;
        Progress = 0;
        return null;
    }

    public void Tick(World w)
    {
        float rate = 0;
        foreach (var b in w.Buildings)
        {
            if (b is not Lab lab) continue;
            lab.Working = Current != null;
            if (Current != null) rate += lab.Def.ResearchRate * lab.PowerFactor;
        }
        if (Current == null || rate <= 0) return;

        Progress += rate * w.Mods.Mult(Stat.ResearchSpeed);
        if (Progress < Current.Ticks) return;

        _completed.Add(Current.Id);
        w.Events.Add(new ResearchCompletedEvent(Current.Id));
        Current = null;
        Progress = 0;
    }

    /// <summary>For tests and debug tools: mark a tech done without paying for it.</summary>
    public void Grant(string techId) => _completed.Add(techId);

    public void HashInto(StateHash h)
    {
        foreach (var id in _completed.OrderBy(x => x, StringComparer.Ordinal)) h.Add(id);
        h.Add(Current?.Id);
        h.Add(Progress);
    }
}

/// <summary>
/// The shared logistics network for paying costs: the Nexus plus every storage depot.
/// Payments draw from the Nexus first, then depots in build order. Refunds go to the Nexus.
/// </summary>
public sealed class StorageNetwork
{
    private readonly World _world;

    public StorageNetwork(World world) => _world = world;

    private IEnumerable<Inventory> Stores()
    {
        yield return _world.Storage;
        foreach (var b in _world.Buildings)
            if (b is Depot { Dead: false } d) yield return d.Storage;
    }

    public int Count(string item) => Stores().Sum(s => s.Count(item));

    /// <summary>Totals across the whole network, sorted by item id.</summary>
    public IEnumerable<KeyValuePair<string, int>> Totals()
    {
        var total = new Inventory();
        foreach (var s in Stores()) total.AddAll(s);
        return total.Entries;
    }

    public string? FirstMissing(IReadOnlyDictionary<string, int> cost) =>
        cost.FirstOrDefault(c => Count(c.Key) < c.Value).Key;

    public bool Has(IReadOnlyDictionary<string, int> cost) => FirstMissing(cost) == null;

    public bool Remove(IReadOnlyDictionary<string, int> cost)
    {
        if (!Has(cost)) return false;
        foreach (var (item, n) in cost) Remove(item, n);
        return true;
    }

    public bool Remove(string item, int n = 1)
    {
        if (Count(item) < n) return false;
        foreach (var s in Stores())
        {
            int take = Math.Min(n, s.Count(item));
            s.Remove(item, take);
            n -= take;
            if (n == 0) break;
        }
        return true;
    }
}
