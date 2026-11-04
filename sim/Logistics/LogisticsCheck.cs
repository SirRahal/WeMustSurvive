namespace BeltAndBarricade.Sim;

/// <summary>Why a logistics building's items never arrive anywhere useful, and where its chain ends.</summary>
/// <param name="Short">A few words for the on-map label (e.g. "WRONG WAY").</param>
/// <param name="Reason">Full explanation with the fix, for the tooltip.</param>
/// <param name="End">The building where the chain stops (fix it there).</param>
public sealed record LogisticsIssue(string Short, string Reason, Building End);

/// <summary>
/// Traces every belt, splitter, tunnel, inserter and drill downstream to see whether its items reach the Nexus,
/// a depot, or a building that uses them (an inserter dropping into a furnace or turret counts). Anything that
/// dead-ends gets a <see cref="LogisticsIssue"/>, including a guess at the common mistakes: things facing the wrong
/// way, belts running away from the Nexus, belts fed straight into machines. Read-only; meant for the UI.
/// </summary>
public static class LogisticsCheck
{
    private const int MaxChain = 2000;

    public static Dictionary<int, LogisticsIssue> Analyze(World w)
    {
        var results = new Dictionary<int, LogisticsIssue?>();
        foreach (var b in w.Buildings)
            if (!b.Dead && b is not ConstructionSite && IsLogistics(b)) Trace(w, b, results, new HashSet<int>(), 0);
        return results.Where(r => r.Value != null).ToDictionary(r => r.Key, r => r.Value!);
    }

    public static bool IsLogistics(Building b) =>
        b is Belt or Splitter or TunnelEntrance or TunnelExit or Inserter or Drill;

    private static bool IsStorage(Building? b) => b is Nexus or Depot;

    /// <summary>Whether <paramref name="b"/> can ever take this kind of item (ignoring how full it is right now).</summary>
    public static bool AcceptsType(World w, Building b, string item) => b switch
    {
        Nexus or Depot or Splitter or TunnelEntrance or TunnelExit or Belt => true,
        // Only burner drills on non-coal ore take coal (a drill sitting on coal fuels itself).
        Drill d => item == "coal" && d.Def.UsesFuel && w.Map.OreAt(d.Origin) != "coal",
        Crafter c => c.Recipes.Any(r => r.Inputs.ContainsKey(item)) || c.Def.UsesFuel && item == "coal",
        Turret t => t.Def.AmmoItem == item,
        Generator => item == "coal",
        _ => false,
    };
    private static bool CanGive(Building? b) => b is Belt or IItemSource;
    private static bool CanTake(Building? b) => b is Belt or IItemAcceptor;

    /// <summary>Returns null if the building's output is delivered somewhere useful, otherwise the issue.</summary>
    private static LogisticsIssue? Trace(World w, Building b, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth)
    {
        if (memo.TryGetValue(b.Id, out var known)) return known;
        if (!path.Add(b.Id) || depth > MaxChain)
            return new LogisticsIssue("LOOP", "The belts loop back on themselves, so items circle forever. Break the loop or lead it to the Nexus.", b);

        LogisticsIssue? result = b switch
        {
            Belt belt => Feed(w, belt, belt.Front, memo, path, depth, "belt"),
            TunnelExit exit => Feed(w, exit, exit.Front, memo, path, depth, "Tunnel Out"),
            TunnelEntrance tin => tin.FindExit(w) is { } exit
                ? Trace(w, exit, memo, path, depth + 1)
                : new LogisticsIssue("NO EXIT", $"This Tunnel In has no Tunnel Out facing {tin.Dir} within {tin.Def.TunnelReach} tiles ahead. Place one (same direction) on the far side.", tin),
            Splitter s => TraceSplitter(w, s, memo, path, depth),
            Inserter ins => TraceInserter(w, ins, memo, path, depth),
            Drill d => TraceDrill(w, d, memo, path, depth),
            _ => null,
        };
        path.Remove(b.Id);
        memo[b.Id] = result;
        return result;
    }

    /// <summary>Where a belt (or tunnel exit) delivers its items.</summary>
    private static LogisticsIssue? Feed(World w, Building from, Int2 tile, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth, string what)
    {
        var issue = FeedForward(w, from, tile, memo, path, depth, what);
        // A belt that dead-ends is still fine if an inserter is picking its items off (and that inserter delivers).
        if (issue != null && from is Belt && PickedUpByWorkingInserter(w, from, memo, path, depth)) return null;
        return issue;
    }

    /// <summary>The longest sorter reach in the game (fast inserter).</summary>
    private const int MaxSorterReach = 3;

    private static bool PickedUpByWorkingInserter(World w, Building belt, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth)
    {
        // Sorters reach over other belts, so look a few tiles out in each direction.
        foreach (var d in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
        for (int k = 1; k <= MaxSorterReach; k++)
            if (w.BuildingAt(belt.Origin + new Int2(d.Offset().X * k, d.Offset().Y * k)) is Inserter ins && ins.SourceTiles.Contains(belt.Origin)
                && !path.Contains(ins.Id) && Trace(w, ins, memo, path, depth + 1) == null)
                return true;
        return false;
    }

    private static LogisticsIssue? FeedForward(World w, Building from, Int2 tile, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth, string what)
    {
        var next = w.BuildingAt(tile);
        switch (next)
        {
            case null:
                return RunsAwayFromStorage(w, from) is { } store
                    ? new LogisticsIssue("WRONG WAY", $"These belts run away from the {store.Def.Name}. Re-lay them by dragging toward it, or rotate each belt (R).", from)
                    : new LogisticsIssue("DEAD END", $"The chain ends here: this {what} points at empty ground. Continue the belt to the Nexus or a depot.", from);
            case ConstructionSite:
                return null; // still being built; don't nag
            case Nexus or Depot:
                return null;
            case Belt nb when nb.Dir == from.Dir.Opposite():
                return new LogisticsIssue("HEAD-ON", $"This {what} and the next belt point into each other. Rotate one of them (R).", from);
            case Belt or Splitter or TunnelEntrance:
                return Trace(w, next, memo, path, depth + 1);
            default:
                return new LogisticsIssue("NEEDS INSERTER", $"This {what} runs straight into a {next.Def.Name}. Belts can't feed buildings: stop the belt beside it and put an inserter (facing the {next.Def.Name}) in between.", from);
        }
    }

    /// <summary>
    /// If the belt chain ending at <paramref name="end"/> started right next to the Nexus or a depot (with its back to
    /// it), the player almost certainly laid it the wrong way round. Returns that storage, if so.
    /// </summary>
    private static Building? RunsAwayFromStorage(World w, Building end)
    {
        var cur = end;
        var seen = new HashSet<int>();
        while (seen.Add(cur.Id) && seen.Count < MaxChain)
        {
            if (IsStorage(w.BuildingAt(cur.Back))) return w.BuildingAt(cur.Back);
            // Step to the belt feeding this one from behind.
            if (w.BuildingAt(cur.Back) is Belt prev && prev.Front == cur.Origin) cur = prev;
            else return null;
        }
        return null;
    }

    private static LogisticsIssue? TraceSplitter(World w, Splitter s, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth)
    {
        LogisticsIssue? first = null;
        bool anyOutput = false;
        foreach (var d in new[] { s.Dir, s.Dir.RotateCw(), s.Dir.RotateCw().Opposite() })
        {
            var next = w.BuildingAt(s.Origin + d.Offset());
            if (next == null || next is Belt nb && nb.Front == s.Origin) continue;
            if (next is not (Belt or IBeltInput or ConstructionSite)) continue;
            anyOutput = true;
            var issue = next is Nexus or Depot or ConstructionSite ? null : Trace(w, next, memo, path, depth + 1);
            if (issue == null) return null; // at least one output gets through
            first ??= issue;
        }
        return anyOutput ? first : new LogisticsIssue("NO OUTPUT", "This splitter has nowhere to send items: put belts ahead, left or right of it.", s);
    }

    private static LogisticsIssue? TraceInserter(World w, Inserter ins, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth)
    {
        var target = w.BuildingAt(ins.Front);
        var candidates = ins.SourceTiles.Select(w.BuildingAt).Where(b => b != null).ToList();
        if (target is ConstructionSite || candidates.Any(b => b is ConstructionSite)) return null;
        var source = candidates.FirstOrDefault(CanGive);
        var back = w.BuildingAt(ins.Back);
        string where = ins.Input switch
        {
            InputSide.Auto => "behind it or at its sides",
            InputSide.Back => "behind it",
            _ => $"on its {ins.Input.ToString().ToLowerInvariant()} side",
        };

        // The classic mistake: an inserter that would work if it faced the other way.
        if (source == null && CanGive(target) && CanTake(back))
            return new LogisticsIssue("WRONG WAY",
                $"This inserter faces the wrong way. It drops where the arrow points: it should take from the {target!.Def.Name} and give to the {back!.Def.Name}. Select it and press R twice.", ins);

        if (source == null)
            return candidates.Count == 0
                ? new LogisticsIssue("NOTHING TO TAKE", $"This inserter has nothing {where} to pick up from. Inserters take from any side except the arrow's (set which with E).", ins)
                : new LogisticsIssue("NOTHING TO TAKE", $"This inserter is next to a {candidates[0]!.Def.Name}, which doesn't give items out.", ins);
        if (IsStorage(source) && ins.Filter == null && !Inserter.FeedsMachine(target))
            return new LogisticsIssue("NEEDS FILTER", $"This inserter takes from the {source.Def.Name} and drops onto a belt, so it needs a filter (otherwise it would empty storage onto the belt). Select it and pick an item (or press F).", ins);
        return target switch
        {
            null => new LogisticsIssue("DEAD END", "This inserter has nothing in front of it (where the arrow points) to drop into.", ins),
            Belt => Trace(w, target, memo, path, depth + 1),
            IItemAcceptor => null, // delivering into a furnace, turret, workshop, generator, Nexus...
            _ => new LogisticsIssue("CAN'T TAKE ITEMS", $"This inserter drops into a {target.Def.Name}, which can't take items.", ins),
        };
    }

    private static LogisticsIssue? TraceDrill(World w, Drill d, Dictionary<int, LogisticsIssue?> memo, HashSet<int> path, int depth)
    {
        var target = w.BuildingAt(d.Front);
        switch (target)
        {
            case ConstructionSite:
                return null;
            case Belt:
                return Trace(w, target, memo, path, depth + 1);
            case IItemAcceptor when w.Map.OreAt(d.Origin) is not { } ore || AcceptsType(w, target, ore):
                return null;
            case IItemAcceptor:
                string mined = w.Map.OreAt(d.Origin)!.Replace('_', ' ');
                string why = target is Drill
                    ? $"The {target.Def.Name} in front sits on coal and fuels itself, so it won't take {mined}. Point this drill at a furnace, a belt, or a drill on iron."
                    : $"This drill mines {mined}, but the {target.Def.Name} in front can't use it. Point it at a belt or something that uses {mined}.";
                return new LogisticsIssue("CAN'T USE", why, d);
        }
        // Pointing at nothing (or a wall) while something useful sits on another side: just rotate it.
        var better = new[] { d.Dir.RotateCw(), d.Dir.Opposite(), d.Dir.RotateCw().Opposite() }
            .Select(dir => w.BuildingAt(d.Origin + dir.Offset()))
            .FirstOrDefault(b => b is Belt or IItemAcceptor && b is not ConstructionSite);
        if (better != null)
            return new LogisticsIssue("ROTATE", $"This drill's output (the arrow) points away from the {better.Def.Name} next to it. Select the drill and press R until the arrow points at it.", d);
        return target == null
            ? new LogisticsIssue("DEAD END", "This drill's output (the arrow) points at empty ground. Put a belt, furnace or depot there.", d)
            : new LogisticsIssue("CAN'T TAKE ORE", $"This drill outputs into a {target.Def.Name}, which can't take ore.", d);
    }
}
