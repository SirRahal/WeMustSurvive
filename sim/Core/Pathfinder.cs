using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>A* over the tile grid for engineers. Blocking buildings and impassable terrain are solid.</summary>
public static class Pathfinder
{
    private const float Diagonal = 1.41421356f;

    private static readonly Int2[] Neighbors =
    {
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
        new(1, -1), new(1, 1), new(-1, 1), new(-1, -1),
    };

    public static bool IsWalkable(World w, Int2 t)
    {
        if (!w.Map.IsPassable(t)) return false;
        var b = w.BuildingAt(t);
        return b == null || !b.BlocksEnemies;
    }

    /// <summary>
    /// Tiles to walk through (excluding <paramref name="start"/>) to reach the nearest tile satisfying
    /// <paramref name="isGoal"/>. Empty if already there; null if unreachable.
    /// </summary>
    public static List<Int2>? Find(World w, Int2 start, Func<Int2, bool> isGoal, Vector2 heuristicTarget)
    {
        if (isGoal(start)) return new List<Int2>();

        var map = w.Map;
        int n = map.Width * map.Height;
        var g = new float[n];
        var parent = new int[n];
        var closed = new bool[n];
        Array.Fill(g, float.PositiveInfinity);
        Array.Fill(parent, -1);

        var open = new PriorityQueue<Int2, float>();
        g[map.Index(start)] = 0;
        open.Enqueue(start, Heuristic(start, heuristicTarget));

        while (open.TryDequeue(out var cur, out _))
        {
            int ci = map.Index(cur);
            if (closed[ci]) continue;
            closed[ci] = true;
            if (isGoal(cur)) return Reconstruct(map, parent, start, cur);

            foreach (var off in Neighbors)
            {
                var nb = cur + off;
                if (!IsWalkable(w, nb)) continue;
                bool diag = off.X != 0 && off.Y != 0;
                if (diag && (!IsWalkable(w, new Int2(nb.X, cur.Y)) || !IsWalkable(w, new Int2(cur.X, nb.Y)))) continue;

                int ni = map.Index(nb);
                float ng = g[ci] + (diag ? Diagonal : 1f);
                if (ng >= g[ni]) continue;
                g[ni] = ng;
                parent[ni] = ci;
                open.Enqueue(nb, ng + Heuristic(nb, heuristicTarget));
            }
        }
        return null;
    }

    private static float Heuristic(Int2 t, Vector2 target)
    {
        float dx = MathF.Abs(t.X + 0.5f - target.X), dy = MathF.Abs(t.Y + 0.5f - target.Y);
        return MathF.Max(dx, dy) + (Diagonal - 1) * MathF.Min(dx, dy);
    }

    private static List<Int2> Reconstruct(GameMap map, int[] parent, Int2 start, Int2 end)
    {
        var path = new List<Int2>();
        int startIndex = map.Index(start);
        for (int i = map.Index(end); i != startIndex; i = parent[i])
            path.Add(new Int2(i % map.Width, i / map.Width));
        path.Reverse();
        return path;
    }
}
