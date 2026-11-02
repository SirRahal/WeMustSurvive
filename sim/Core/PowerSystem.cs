namespace BeltAndBarricade.Sim;

/// <summary>
/// Factorio-style electric network. Poles (and the Nexus) link to each other within wire reach to form grids;
/// any building whose footprint lies within a node's supply radius is on that grid. Each tick a grid's
/// satisfaction = supply / demand (capped at 1); consumers run at that fraction, and below
/// <see cref="Building.BrownoutCutoff"/> they shut off entirely (brownout).
/// </summary>
public sealed class PowerSystem
{
    public sealed class Grid
    {
        public int Id;
        public readonly List<Building> Nodes = new();
        public readonly List<Generator> Generators = new();
        public readonly List<Building> Consumers = new();
        public bool HasNexus;
        public float Capacity;
        public float Demand;
        public float Satisfaction = 1f;
    }

    private readonly World _world;
    private readonly List<Grid> _grids = new();
    private bool _dirty = true;

    public PowerSystem(World world) => _world = world;

    public IReadOnlyList<Grid> Grids => _grids;

    public void MarkDirty() => _dirty = true;

    public Grid? GridOf(Building b) => b.GridId >= 0 && b.GridId < _grids.Count ? _grids[b.GridId] : null;

    public void Tick()
    {
        if (_dirty) Rebuild();
        var mods = _world.Mods;
        foreach (var g in _grids)
        {
            float cap = g.HasNexus ? _world.Nexus.Def.PowerOutput * mods.Mult(Stat.PowerOutput) : 0f;
            float genCap = 0f;
            foreach (var gen in g.Generators) genCap += gen.AvailableOutput(_world);
            cap += genCap;

            float demand = 0f;
            foreach (var c in g.Consumers) demand += c.PowerDemand;
            demand *= mods.Mult(Stat.PowerUse);

            g.Capacity = cap;
            g.Demand = demand;
            g.Satisfaction = demand <= 0 ? 1f : MathF.Min(1f, cap / demand);

            // Generators cover whatever the Nexus can't, sharing the load.
            float nexusShare = cap - genCap;
            float genLoad = genCap <= 0 ? 0f : Math.Clamp((demand - nexusShare) / genCap, 0f, 1f);
            foreach (var gen in g.Generators) gen.Run(_world, gen.AvailableOutput(_world) > 0 ? genLoad : 0f);
            foreach (var c in g.Consumers) c.Power = g.Satisfaction;
        }
    }

    private void Rebuild()
    {
        _dirty = false;
        _grids.Clear();
        var buildings = _world.Buildings.Where(b => !b.Dead && b is not ConstructionSite).ToList();
        foreach (var b in buildings) b.GridId = -1;

        // Union-find over nodes (anything with a supply radius: poles and the Nexus).
        var nodes = buildings.Where(b => b.Def.SupplyRadius > 0).ToList();
        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            float reach = MathF.Min(nodes[i].Def.WireReach, nodes[j].Def.WireReach);
            if (System.Numerics.Vector2.Distance(nodes[i].Center, nodes[j].Center) <= reach)
                parent[Find(i)] = Find(j);
        }

        var gridOfRoot = new Dictionary<int, Grid>();
        for (int i = 0; i < nodes.Count; i++)
        {
            int root = Find(i);
            if (!gridOfRoot.TryGetValue(root, out var g))
            {
                g = new Grid { Id = _grids.Count };
                _grids.Add(g);
                gridOfRoot[root] = g;
            }
            g.Nodes.Add(nodes[i]);
            g.HasNexus |= nodes[i] is Nexus;
            nodes[i].GridId = g.Id;
        }

        // Attach generators and consumers to the first node (by id) whose supply area reaches them.
        foreach (var b in buildings)
        {
            if (b.Def.SupplyRadius > 0) continue;
            bool isGen = b is Generator;
            if (!isGen && !b.NeedsPower) continue;
            var node = nodes.FirstOrDefault(n => Covers(n, b));
            if (node == null)
            {
                b.Power = isGen ? 1f : 0f;
                continue;
            }
            var grid = _grids[node.GridId];
            b.GridId = grid.Id;
            if (isGen) grid.Generators.Add((Generator)b);
            else grid.Consumers.Add(b);
        }

        // Unpowered-by-design buildings always run at full speed.
        foreach (var b in buildings)
            if (!b.NeedsPower) b.Power = 1f;
    }

    /// <summary>Chebyshev distance from the node's center to the building's footprint within the supply radius.</summary>
    public static bool Covers(Building node, Building b)
    {
        float dx = MathF.Max(0, MathF.Max(b.Origin.X - node.Center.X, node.Center.X - (b.Origin.X + b.Size)));
        float dy = MathF.Max(0, MathF.Max(b.Origin.Y - node.Center.Y, node.Center.Y - (b.Origin.Y + b.Size)));
        return MathF.Max(dx, dy) <= node.Def.SupplyRadius;
    }

    public void HashInto(StateHash h)
    {
        foreach (var g in _grids) h.Add(g.Satisfaction);
    }
}
