namespace BeltAndBarricade.Sim;

/// <summary>
/// Dijkstra distance field toward the Nexus. Blocking buildings are passable at a cost proportional to their HP,
/// so enemies route around defenses when a path exists and chew through them when it doesn't.
/// </summary>
public sealed class FlowField
{
    private const float Diagonal = 1.41421356f;
    private const float BuildingBaseCost = 2f;
    private const float HpPerTileCost = 25f;

    private static readonly Int2[] Neighbors =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
        new(1, -1), new(1, 1), new(-1, 1), new(-1, -1),
    };

    private readonly World _world;
    private readonly float[] _dist;

    public FlowField(World world)
    {
        _world = world;
        _dist = new float[world.Map.Width * world.Map.Height];
    }

    public float DistanceAt(Int2 t) => _world.Map.InBounds(t) ? _dist[_world.Map.Index(t)] : float.PositiveInfinity;

    public void Recompute()
    {
        var map = _world.Map;
        Array.Fill(_dist, float.PositiveInfinity);
        var queue = new PriorityQueue<Int2, float>();

        foreach (var t in _world.Nexus.Tiles)
        {
            _dist[map.Index(t)] = 0;
            queue.Enqueue(t, 0);
        }

        while (queue.TryDequeue(out var cur, out float d))
        {
            if (d > _dist[map.Index(cur)]) continue;
            // Cost an enemy pays to step INTO `cur` from a neighbor.
            float enterCost = EnterCost(cur);

            foreach (var off in Neighbors)
            {
                var n = cur + off;
                if (!map.IsPassable(n) || !CanStep(n, cur)) continue;
                float nd = d + enterCost * (off.X != 0 && off.Y != 0 ? Diagonal : 1f);
                int ni = map.Index(n);
                if (nd < _dist[ni])
                {
                    _dist[ni] = nd;
                    queue.Enqueue(n, nd);
                }
            }
        }
    }

    /// <summary>Cheapest neighboring tile to move toward from <paramref name="from"/>, or null if the Nexus is unreachable.</summary>
    public Int2? NextStep(Int2 from)
    {
        float best = float.PositiveInfinity;
        Int2? result = null;
        foreach (var off in Neighbors)
        {
            var n = from + off;
            if (!_world.Map.IsPassable(n) || !CanStep(from, n)) continue;
            // Total cost via n includes what it costs to enter n (e.g. chewing through a wall).
            float d = DistanceAt(n) + EnterCost(n) * (off.X != 0 && off.Y != 0 ? Diagonal : 1f);
            if (d < best)
            {
                best = d;
                result = n;
            }
        }
        return result;
    }

    private float EnterCost(Int2 t)
    {
        var b = _world.BuildingAt(t);
        if (b == null || !b.BlocksEnemies || b is Nexus) return 1f;
        return BuildingBaseCost + b.Hp / HpPerTileCost;
    }

    /// <summary>Diagonal steps may not cut corners past terrain or blocking buildings.</summary>
    private bool CanStep(Int2 a, Int2 b)
    {
        if (a.X == b.X || a.Y == b.Y) return true;
        return IsOpen(new Int2(b.X, a.Y)) && IsOpen(new Int2(a.X, b.Y));
    }

    private bool IsOpen(Int2 t)
    {
        if (!_world.Map.IsPassable(t)) return false;
        var b = _world.BuildingAt(t);
        return b == null || !b.BlocksEnemies;
    }
}
