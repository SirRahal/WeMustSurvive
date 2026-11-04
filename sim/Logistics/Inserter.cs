namespace BeltAndBarricade.Sim;

/// <summary>Which neighbour an inserter takes from. Auto tries behind, then left, then right.</summary>
public enum InputSide { Auto, Back, Left, Right }

/// <summary>
/// A sorter: picks an item up and drops it on the tile in front (the arrow). It reaches up to <see cref="Reach"/>
/// tiles behind it, over belts in between, so one sorter can pull from several parallel belts (nearest first); it can
/// also take from either side next to it (a 90° move). <see cref="Input"/> chooses, and Auto tries behind, then the
/// sides, taking from the first one that has something the target wants. Longer reaches swing a little slower. Only picks items the target can currently accept, so it never gets stuck
/// holding something unusable.
/// By default it moves any item the target can accept. Moving from shared storage (the Nexus or a depot) onto a belt or
/// into other storage needs a filter, otherwise it would empty storage. The optional <see cref="AutoFilter"/> picks one
/// from the neighbours: a turret wants its ammo, a workshop wants plates, output from a crafter is what it makes.
/// </summary>
public sealed class Inserter : Building
{
    public enum Phase { Idle, Swinging, Returning }

    public Phase State { get; private set; }
    public float Progress { get; private set; }
    public string? Held { get; private set; }
    public string? Filter { get; set; }
    /// <summary>When set (the "Auto filter" button), the filter follows the neighbours. Off by default: Any item.</summary>
    public bool AutoFilter { get; set; }
    public InputSide Input { get; set; } = InputSide.Auto;
    /// <summary>The side the current/last item was taken from (for the arm animation).</summary>
    public Direction TookFrom { get; private set; }
    /// <summary>How many tiles away the current/last item was taken from (1 = next to it).</summary>
    public int TookDistance { get; private set; } = 1;
    /// <summary>Tiles it reaches behind it (from the data; 1 = only the neighbour).</summary>
    public int Reach => Math.Max(1, Def.Reach);
    /// <summary>Extra swing time per tile of reach beyond the first.</summary>
    public const float ReachSwingFactor = 0.25f;

    protected override bool IsBusy => State != Phase.Idle;

    /// <summary>The world direction of a side, relative to the facing <paramref name="dir"/>.</summary>
    public static Direction SideDir(Direction dir, InputSide side) => side switch
    {
        InputSide.Left => dir.RotateCw().Opposite(),
        InputSide.Right => dir.RotateCw(),
        _ => dir.Opposite(),
    };

    /// <summary>Sides this inserter may take from, in priority order.</summary>
    public static IEnumerable<Direction> SourceDirs(Direction dir, InputSide input) => input == InputSide.Auto
        ? new[] { SideDir(dir, InputSide.Back), SideDir(dir, InputSide.Left), SideDir(dir, InputSide.Right) }
        : new[] { SideDir(dir, input) };

    /// <summary>
    /// Tiles it may take from, in priority order, with their side and distance: behind it out to
    /// <paramref name="reach"/> tiles (nearest first), and one tile out on either side.
    /// </summary>
    public static IEnumerable<(Int2 Tile, Direction Side, int Distance)> Sources(Int2 origin, Direction dir, InputSide input, int reach)
    {
        foreach (var d in SourceDirs(dir, input))
        {
            int far = d == dir.Opposite() ? Math.Max(1, reach) : 1;
            var o = d.Offset();
            for (int k = 1; k <= far; k++) yield return (origin + new Int2(o.X * k, o.Y * k), d, k);
        }
    }

    public IEnumerable<Int2> SourceTiles => Sources(Origin, Dir, Input, Reach).Select(s => s.Tile);

    /// <summary>Every building in reach it could take from (belts and item sources), nearest first.</summary>
    public IEnumerable<Building> SourcesInReach(World w)
    {
        var front = w.BuildingAt(Front);
        return SourceTiles.Select(w.BuildingAt).OfType<Building>()
            .Where(b => b != front && b is not ConstructionSite && (b is Belt || b is IItemSource)).Distinct();
    }

    /// <summary>Ticks for one swing when the item came from <paramref name="distance"/> tiles away.</summary>
    public float SwingTicksFor(int distance) => Def.SwingTicks * (1 + ReachSwingFactor * (distance - 1));

    public override void Tick(World w)
    {
        float speed = PowerFactor * w.Mods.Mult(Stat.InserterSpeed);
        if (speed <= 0) return;
        var target = w.BuildingAt(Front);
        switch (State)
        {
            case Phase.Idle:
                if (target == null) return;
                Building? tried = null;
                foreach (var (tile, d, k) in Sources(Origin, Dir, Input, Reach))
                {
                    // A big building fills several tiles in reach: try each once, and never the target itself.
                    if (w.BuildingAt(tile) is not { } source || source == tried || source == target) continue;
                    tried = source;
                    if (!TryTakeFrom(w, source, target, out string item)) continue;
                    if (source is Nexus or Depot) w.Events.Add(new ItemTakenEvent(source.Origin, Center, item));
                    Held = item;
                    TookFrom = d;
                    TookDistance = k;
                    State = Phase.Swinging;
                    Progress = 0;
                    return;
                }
                // Nothing to take right now: show the filter for the most likely source rather than the last side tried.
                if (AutoFilter) Filter = SuggestFilter(w, LikelySource(w, Origin, Dir, Input, Reach), target);
                break;

            case Phase.Swinging:
                if (Progress < SwingTicksFor(TookDistance)) { Progress += speed; return; }
                if (target != null && Drop(target, Held!))
                {
                    if (target is Nexus or Depot) w.Events.Add(new ItemStoredEvent(Center, target.Origin, Held!));
                    Held = null;
                    State = Phase.Returning;
                    Progress = 0;
                }
                break;

            case Phase.Returning:
                Progress += speed;
                if (Progress >= SwingTicksFor(TookDistance)) State = Phase.Idle;
                break;
        }
    }

    private bool TryTakeFrom(World w, Building source, Building target, out string item)
    {
        item = "";
        if (source is not IItemSource src || source is ConstructionSite) return false;
        if (AutoFilter) Filter = SuggestFilter(w, source, target);
        if (source is Nexus or Depot && Filter == null && !FeedsMachine(target)) return false;
        return src.TryTake(it => Wants(target, it), out item);
    }

    /// <summary>
    /// Dropping into a machine (turret, workshop, furnace...) only ever takes what that machine can use, so "any item"
    /// is safe even from storage. Dropping onto a belt or into storage would drain the Nexus, so that needs a filter.
    /// </summary>
    public static bool FeedsMachine(Building? target) => target is not (null or Belt or IBeltInput);

    /// <summary>
    /// The filter an inserter between <paramref name="source"/> and <paramref name="target"/> should use: what the
    /// target is built to consume, otherwise what the source produces. Null means "anything".
    /// </summary>
    public static string? SuggestFilter(World w, Building? source, Building? target)
    {
        if (source == null || target == null || source is ConstructionSite || target is ConstructionSite) return null;
        bool storage = source is Nexus or Depot;
        bool Available(string item) => !storage || w.Network.Count(item) > 0;

        string? wanted = target switch
        {
            Turret t => t.Def.AmmoItem,
            Generator => "coal",
            Drill d when d.NeedsFuel => "coal",
            // Prefer a recipe the source can actually supply; its first missing input.
            Crafter c => c.Recipes.Where(r => r.Inputs.Keys.All(Available)).Select(r => r.Inputs.Keys.First()).FirstOrDefault()
                         ?? (c.Def.UsesFuel && Available("coal") ? "coal" : null),
            _ => null,
        };
        if (wanted != null) return wanted;

        // Taking out of a crafter: whatever it is making / has made (e.g. Munitions Works -> ammo).
        if (source is Crafter cs)
            return cs.Output.Entries.Select(e => e.Key).FirstOrDefault() ?? cs.Active?.Outputs.Keys.First()
                ?? cs.Recipes.LastOrDefault()?.Outputs.Keys.First();
        return null;
    }

    /// <summary>The building an inserter would take from, for the UI (the first in reach with something useful).</summary>
    public static Building? LikelySource(World w, Int2 origin, Direction dir, InputSide input, int reach = 1)
    {
        var front = w.BuildingAt(origin + dir.Offset());
        var near = Sources(origin, dir, input, reach).Select(s => w.BuildingAt(s.Tile)).OfType<Building>().Where(b => b != front).ToList();
        return near.FirstOrDefault(b => b is not ConstructionSite && (b is Belt || b is IItemSource)) ?? near.FirstOrDefault();
    }

    private bool Wants(Building target, string item)
    {
        if (Filter != null && item != Filter) return false;
        return target switch
        {
            Belt b => b.CanInsertAt(Belt.DropPosition),
            IItemAcceptor a => a.CanAccept(item),
            _ => false,
        };
    }

    private static bool Drop(Building target, string item) => target switch
    {
        Belt b => b.TryInsertAt(item, Belt.DropPosition),
        IItemAcceptor a => a.TryInsert(item),
        _ => false,
    };

    public override void CollectContents(Inventory into)
    {
        if (Held != null) into.Add(Held);
        Held = null;
    }

    public override string Status => PowerFactor <= 0 ? "No power" : "";

    public override void HashInto(StateHash h)
    {
        h.Add((int)State);
        h.Add(Progress);
        h.Add(Held);
        h.Add(Filter);
        h.Add(AutoFilter ? 1 : 0);
        h.Add((int)Input);
        h.Add((int)TookFrom);
        h.Add(TookDistance);
    }
}
