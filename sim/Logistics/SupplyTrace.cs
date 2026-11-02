namespace BeltAndBarricade.Sim;

/// <summary>
/// Walks the logistics network <i>upstream</i> from a building to find every drill whose output can reach it:
/// directly (a drill pointing at it), or through belts, splitters, tunnels and inserters. For the UI
/// ("fed by 2 iron drills, 1 coal drill").
/// </summary>
public static class SupplyTrace
{
    private const int MaxNodes = 4000;

    public static List<Drill> DrillsFeeding(World w, Building target)
    {
        var drills = new List<Drill>();
        var seen = new HashSet<int> { target.Id };
        var queue = new Queue<Building>();
        queue.Enqueue(target);

        while (queue.Count > 0 && seen.Count < MaxNodes)
        {
            foreach (var feeder in Feeders(w, queue.Dequeue()))
            {
                if (!seen.Add(feeder.Id)) continue;
                if (feeder is Drill d) drills.Add(d);
                // Keep walking through logistics; stop at other machines and storage (they're sources, not pipes).
                if (feeder is Belt or Splitter or TunnelEntrance or TunnelExit or Inserter) queue.Enqueue(feeder);
            }
        }
        return drills;
    }

    /// <summary>Buildings that hand items directly to <paramref name="b"/>.</summary>
    private static IEnumerable<Building> Feeders(World w, Building b)
    {
        // Anything pointing into one of b's tiles: drills, belts, inserters, tunnel exits.
        foreach (var t in b.Tiles)
        foreach (var d in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
        {
            var n = w.BuildingAt(t + d.Offset());
            if (n == null || n == b || n is ConstructionSite) continue;
            bool pointsHere = n.Size == 1 && n.Front == t;
            switch (n)
            {
                case Drill or Inserter or TunnelExit when pointsHere:
                    yield return n;
                    break;
                case Belt belt when pointsHere && !(b is Belt nb && nb.Dir == belt.Dir.Opposite()):
                    yield return n;
                    break;
                case Splitter when b is Belt or IBeltInput:
                    yield return n; // splitters push to all three non-input sides
                    break;
            }
        }

        switch (b)
        {
            // An inserter is fed by whatever it takes from.
            case Inserter ins:
                foreach (var tile in ins.SourceTiles)
                {
                    var src = w.BuildingAt(tile);
                    if (src is IItemSource && src is not ConstructionSite) yield return src;
                }
                break;
            // A tunnel exit is fed by the matching entrance behind it.
            case TunnelExit exit:
                for (int i = 1; i <= 8; i++)
                {
                    var off = exit.Dir.Opposite().Offset();
                    if (w.BuildingAt(exit.Origin + new Int2(off.X * i, off.Y * i)) is TunnelEntrance tin && tin.FindExit(w) == exit)
                    {
                        yield return tin;
                        break;
                    }
                }
                break;
        }
    }
}
