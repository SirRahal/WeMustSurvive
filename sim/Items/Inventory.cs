namespace BeltAndBarricade.Sim;

/// <summary>Item counts keyed by item id. Sorted so iteration (and therefore hashing) is deterministic.</summary>
public sealed class Inventory
{
    private readonly SortedDictionary<string, int> _counts = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, int>> Entries => _counts.Where(kv => kv.Value > 0);

    public int Total => _counts.Values.Sum();

    public int Count(string item) => _counts.TryGetValue(item, out int n) ? n : 0;

    public void Add(string item, int n = 1)
    {
        if (n <= 0) return;
        _counts[item] = Count(item) + n;
    }

    public bool Remove(string item, int n = 1)
    {
        int have = Count(item);
        if (have < n) return false;
        _counts[item] = have - n;
        return true;
    }

    public void AddAll(IReadOnlyDictionary<string, int> items)
    {
        foreach (var (item, n) in items) Add(item, n);
    }

    public void AddAll(Inventory other)
    {
        foreach (var (item, n) in other.Entries) Add(item, n);
    }

    /// <summary>Returns the first item id we don't have enough of, or null if all are available.</summary>
    public string? FirstMissing(IReadOnlyDictionary<string, int> cost)
    {
        foreach (var (item, n) in cost)
            if (Count(item) < n) return item;
        return null;
    }

    public bool HasAll(IReadOnlyDictionary<string, int> cost) => FirstMissing(cost) == null;

    public bool RemoveAll(IReadOnlyDictionary<string, int> cost)
    {
        if (!HasAll(cost)) return false;
        foreach (var (item, n) in cost) Remove(item, n);
        return true;
    }

    public void Clear() => _counts.Clear();

    public void HashInto(StateHash h)
    {
        foreach (var (item, n) in Entries)
        {
            h.Add(item);
            h.Add(n);
        }
    }
}
