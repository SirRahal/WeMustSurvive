namespace BeltAndBarricade.Sim;

public sealed class SkillCluster
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#ffffff";
}

public sealed class SkillNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Cluster id; empty for the start node and crossover bridges.</summary>
    public string Cluster { get; init; } = "";
    public float X { get; init; }
    public float Y { get; init; }
    public bool Notable { get; init; }
    public Dictionary<string, float> Stats { get; init; } = new();
    /// <summary>Neighbours (links are undirected).</summary>
    public List<string> Links { get; init; } = new();

    public string Describe() => Stats.Count == 0
        ? "Where every engineer starts."
        : string.Join("\n", Stats.Select(s => SkillTree.FormatStat(s.Key, s.Value)));
}

/// <summary>
/// The between-maps meta progression tree (Path of Exile style). Nodes are bought with meta points and must
/// connect back to the start node. Allocated nodes become <see cref="Modifiers"/> for the next map.
/// </summary>
public sealed class SkillTree
{
    public const string StartId = "start";

    public IReadOnlyList<SkillCluster> Clusters { get; }
    public IReadOnlyDictionary<string, SkillNode> Nodes { get; }
    private readonly Dictionary<string, HashSet<string>> _adj = new();

    public SkillTree(List<SkillCluster> clusters, List<SkillNode> nodes)
    {
        Clusters = clusters;
        Nodes = nodes.ToDictionary(n => n.Id);
        foreach (var n in nodes) _adj[n.Id] = new HashSet<string>();
        foreach (var n in nodes)
        foreach (var l in n.Links)
        {
            _adj[n.Id].Add(l);
            _adj[l].Add(n.Id);
        }
    }

    private sealed class Doc
    {
        public List<SkillCluster> Clusters { get; init; } = new();
        public List<SkillNode> Nodes { get; init; } = new();
    }

    public static SkillTree Load(Func<string, string> readFile)
    {
        var doc = GameData.Parse<Doc>(readFile("skilltree.json"));
        return new SkillTree(doc.Clusters, doc.Nodes);
    }

    public IEnumerable<string> Neighbors(string id) => _adj[id];

    /// <summary>A node can be bought if it isn't owned yet and touches the start or an owned node.</summary>
    public bool CanAllocate(IReadOnlySet<string> owned, string id) =>
        Nodes.ContainsKey(id) && id != StartId && !owned.Contains(id) && _adj[id].Any(n => n == StartId || owned.Contains(n));

    /// <summary>A node can be refunded if everything else owned stays connected to the start.</summary>
    public bool CanRefund(IReadOnlySet<string> owned, string id)
    {
        if (!owned.Contains(id)) return false;
        var rest = new HashSet<string>(owned);
        rest.Remove(id);
        return Reachable(rest).IsSupersetOf(rest);
    }

    private HashSet<string> Reachable(HashSet<string> owned)
    {
        var seen = new HashSet<string> { StartId };
        var stack = new Stack<string>();
        stack.Push(StartId);
        while (stack.Count > 0)
            foreach (var n in _adj[stack.Pop()])
                if (owned.Contains(n) && seen.Add(n)) stack.Push(n);
        seen.Remove(StartId);
        return seen;
    }

    public Modifiers ToModifiers(IEnumerable<string> owned)
    {
        var mods = new Modifiers();
        foreach (var id in owned.OrderBy(x => x, StringComparer.Ordinal))
            if (Nodes.TryGetValue(id, out var n))
                foreach (var (stat, v) in n.Stats) mods.Add(stat, v);
        return mods;
    }

    public static string FormatStat(string stat, float value)
    {
        string label = Stat.Labels.TryGetValue(stat, out var l) ? l : stat;
        if (stat == Stat.StartingIron) return $"+{value:0} {label}";
        return $"{(value >= 0 ? "+" : "")}{value * 100:0}% {label}";
    }
}
