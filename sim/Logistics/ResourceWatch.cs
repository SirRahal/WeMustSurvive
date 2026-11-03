namespace BeltAndBarricade.Sim;

/// <summary>Worst last. Stuck: there's stock, but buildings that need it aren't getting it.</summary>
public enum ResourceLevel { Ok, Falling, Stuck, Low, Empty }

/// <summary>One row of the storage panel: how much there is, where it's heading, and what to do about it.</summary>
public sealed record ResourceStatus(string Item, int Count, float InPerMinute, float NetPerMinute, ResourceLevel Level,
    string Short, IReadOnlyList<string> Advice);

/// <summary>
/// Watches the storage network so the player can see a stall coming: samples totals once a second and reports, per
/// item, the income (delivered by belts/inserters), the net trend, and plain-language advice drawn from the actual
/// base (which drills, furnaces and turrets are waiting, and why). UI only: never influences the simulation.
/// </summary>
public sealed class ResourceWatch
{
    private const int SampleTicks = World.TicksPerSecond;
    private const int WindowSamples = 60;
    /// <summary>Always shown (even at zero): everything else needs them.</summary>
    private static readonly string[] Core = { "coal", "iron_plate" };

    private readonly Queue<(long Tick, Dictionary<string, int> Stock, Dictionary<string, int> Received)> _samples = new();

    public void Sample(World w)
    {
        if (_samples.Count > 0 && w.Tick - _samples.Last().Tick < SampleTicks) return;
        if (_samples.Count > 0 && w.Tick < _samples.Last().Tick) _samples.Clear(); // world restarted
        _samples.Enqueue((w.Tick, w.Network.Totals().ToDictionary(e => e.Key, e => e.Value), Received(w)));
        while (_samples.Count > WindowSamples + 1) _samples.Dequeue();
    }

    private static Dictionary<string, int> Received(World w)
    {
        var total = new Inventory();
        total.AddAll(w.Nexus.Received);
        foreach (var b in w.Buildings)
            if (b is Depot { Dead: false } d) total.AddAll(d.Received);
        return total.Entries.ToDictionary(e => e.Key, e => e.Value);
    }

    /// <summary>Rows for the items worth showing, in item-list order.</summary>
    public List<ResourceStatus> Report(World w)
    {
        var rows = new List<ResourceStatus>();
        foreach (var item in w.Data.Items.Keys)
        {
            int count = w.Network.Count(item);
            var waiting = Waiting(w, item).ToList();
            if (count == 0 && waiting.Count == 0 && !Core.Contains(item)) continue;
            rows.Add(Status(w, item, count, waiting));
        }
        return rows;
    }

    public ResourceStatus Status(World w, string item) => Status(w, item, w.Network.Count(item), Waiting(w, item).ToList());

    private ResourceStatus Status(World w, string item, int count, List<string> waiting)
    {
        var (income, net) = Rates(item);
        float minutesLeft = net < 0 ? count / -net : float.PositiveInfinity;
        var level = count == 0 ? ResourceLevel.Empty
            : count < LowThreshold(w, item) || minutesLeft < 1.5f ? ResourceLevel.Low
            : waiting.Count > 0 ? ResourceLevel.Stuck
            : net <= -1 ? ResourceLevel.Falling
            : ResourceLevel.Ok;
        string shortText = level switch
        {
            ResourceLevel.Empty => "EMPTY",
            ResourceLevel.Low => "LOW",
            ResourceLevel.Stuck => "STUCK",
            ResourceLevel.Falling => "FALLING",
            _ => "",
        };

        var advice = new List<string>();
        advice.Add(net < -0.5f
            ? $"{count} in storage. +{income:0}/min in, {net:0}/min overall" + (float.IsFinite(minutesLeft) ? $" (runs out in ~{FormatMinutes(minutesLeft)})." : ".")
            : $"{count} in storage. +{income:0}/min in, {(net >= 0 ? "+" : "")}{net:0}/min overall.");
        advice.AddRange(waiting);
        advice.Add("Used for: " + UsedFor(w, item));
        if (level == ResourceLevel.Stuck)
            advice.Add($"FIX: storage has plenty, it just isn't reaching them. Point an inserter into each one, from the "
                       + $"Nexus, a depot or a belt carrying {w.Data.Items[item].Name}.");
        else if (level != ResourceLevel.Ok || income <= 0) advice.AddRange(Fixes(w, item, income));
        return new ResourceStatus(item, count, income, net, level, shortText, advice);
    }

    private static string FormatMinutes(float m) => m < 1 ? $"{Math.Max(1, (int)(m * 60))} s" : $"{m:0.#} min";

    /// <summary>Income (delivered per minute) and net change per minute over the sample window.</summary>
    private (float Income, float Net) Rates(string item)
    {
        if (_samples.Count < 2) return (0, 0);
        var first = _samples.Peek();
        var last = _samples.Last();
        float minutes = (last.Tick - first.Tick) / (60f * World.TicksPerSecond);
        if (minutes <= 0) return (0, 0);
        int Get(Dictionary<string, int> d) => d.TryGetValue(item, out int n) ? n : 0;
        float income = Math.Max(0, Get(last.Received) - Get(first.Received)) / minutes;
        float net = (Get(last.Stock) - Get(first.Stock)) / minutes;
        return (income, net);
    }

    /// <summary>Below this the item is "low": enough for one useful build or a minute of fuel.</summary>
    private static int LowThreshold(World w, string item)
    {
        if (item == "coal") return Math.Max(5, Burners(w).Count() * 2);
        if (item == "iron_plate") return 20;
        if (w.Buildings.OfType<Turret>().Any(t => t.Def.AmmoItem == item)) return 10;
        return 0;
    }

    private static IEnumerable<Building> Burners(World w) => w.Buildings.Where(b =>
        b is Drill { NeedsFuel: true } || b is Generator || b is Crafter { Def.UsesFuel: true });

    /// <summary>Buildings that are stuck right now for lack of <paramref name="item"/>, as advice lines.</summary>
    private static IEnumerable<string> Waiting(World w, string item)
    {
        if (item == "coal")
        {
            var starved = w.Buildings.Where(b => b.Status == FuelTank.StarvedText).ToList();
            if (starved.Count > 0) yield return $"STALLED: {Group(starved)} have no coal.";
        }

        var empty = w.Buildings.OfType<Turret>().Where(t => t.Def.AmmoItem == item && t.Ammo == 0 && t.Rounds == 0).ToList();
        if (empty.Count > 0) yield return $"STALLED: {Group(empty)} out of {w.Data.Items[item].Name}.";

        var hungry = w.Buildings.OfType<Crafter>().Where(c => WaitingFor(w, c, item)).ToList();
        if (hungry.Count > 0) yield return $"STALLED: {Group(hungry)} waiting for {w.Data.Items[item].Name}.";
    }

    /// <summary>
    /// Idle, short of <paramref name="item"/> for the recipe it runs, and fed nothing for a whole minute (a furnace
    /// between two ore deliveries is idle too, but it isn't stuck).
    /// </summary>
    private static bool WaitingFor(World w, Crafter c, string item)
    {
        if (c.Active != null || c.StockedOut != null) return false;
        var recipe = c.Recipes.Count == 1 ? c.Recipes[0] : c.LastRecipe;
        if (recipe == null || !recipe.Inputs.TryGetValue(item, out int n) || c.Input.Count(item) >= n) return false;
        return !c.Received.All(w.Tick).Any(r => r.Item != "coal");
    }

    private static string Group(IEnumerable<Building> bs) => string.Join(", ", bs.GroupBy(b => b.Def.Name)
        .Select(g => g.Count() == 1 ? $"1 {g.Key}" : $"{g.Count()} {g.Key}s"));

    private static string UsedFor(World w, string item)
    {
        var uses = new List<string>();
        if (w.Data.FuelTicks(item) > 0)
        {
            int burners = Burners(w).Count();
            float perMin = 60f * World.TicksPerSecond / w.Data.FuelTicks(item);
            uses.Add(burners > 0 ? $"fuel ({burners} burner{(burners == 1 ? "" : "s")}, up to {burners * perMin:0}/min)" : "fuel for drills, furnaces, generators");
        }
        foreach (var r in w.Data.Recipes.Values.Where(r => r.Inputs.ContainsKey(item)))
            uses.Add($"making {w.Data.Items[r.Outputs.Keys.First()].Name}");
        if (w.Data.Buildings.Values.Any(b => b.Cost.ContainsKey(item))) uses.Add("building costs");
        if (item == "iron_plate") uses.Add("repairs");
        if (w.Data.Buildings.Values.Any(b => b.AmmoItem == item)) uses.Add("turret ammo");
        return uses.Count == 0 ? "research and special buildings" : string.Join(", ", uses.Distinct());
    }

    /// <summary>What the player can do to get more, based on what's actually built.</summary>
    private static IEnumerable<string> Fixes(World w, string item, float income)
    {
        string name = w.Data.Items[item].Name;
        var recipe = w.Data.Recipes.Values.FirstOrDefault(r => r.Outputs.ContainsKey(item));

        if (recipe == null)
        {
            // Mined straight from the ground.
            var drills = w.Buildings.OfType<Drill>().Where(d => d.Ore == item).ToList();
            if (drills.Count == 0)
            {
                yield return $"FIX: build a {w.Data.Buildings["drill"].Name} on a {name} patch (hover the ground to find one).";
                yield return "Point it into the Nexus, or at a belt that runs into the Nexus.";
                yield break;
            }
            float mined = drills.Sum(d => d.Mined.PerMinute(item, w.Tick));
            var starved = drills.Count(d => d.Status == FuelTank.StarvedText);
            if (starved > 0) yield return $"FIX: {starved} of your {name} drills have no coal. Deliver it: point a coal drill at them, or feed them with an inserter from a coal belt or the Nexus.";
            if (income <= 0 && mined > 0)
                yield return $"FIX: your {drills.Count} {name} drill(s) mine {mined:0}/min but none reaches storage. Follow red markers on belts.";
            else if (income <= 0)
                yield return $"FIX: your {drills.Count} {name} drill(s) aren't delivering. Click one to see why it's waiting.";
            else
                yield return $"FIX: add more drills on {name}: each mines about {drills[0].MaxPerMinute(w):0}/min.";
            yield break;
        }

        // Made by a crafter.
        var inputs = string.Join(" + ", recipe.Inputs.Select(i => $"{i.Value} {w.Data.Items[i.Key].Name}"));
        var maker = w.Data.Buildings.Values.FirstOrDefault(b => b.Recipes.Contains(recipe.Id));
        var crafters = w.Buildings.OfType<Crafter>().Where(c => c.Recipes.Contains(recipe)).ToList();
        if (crafters.Count == 0)
        {
            yield return $"FIX: build a {maker?.Name ?? "crafter"}: it turns {inputs} into {name}.";
            yield return $"Feed it with an inserter (from the Nexus or a belt) and move the {name} out to the Nexus.";
            yield break;
        }
        var missing = recipe.Inputs.Keys.Where(i => w.Network.Count(i) == 0).Select(i => w.Data.Items[i].Name).ToList();
        if (crafters.All(c => c.Active == null && c.StockedOut == null))
            yield return $"FIX: your {Group(crafters)} {(crafters.Count == 1 ? "is" : "are")} idle: {(missing.Count > 0 ? $"get {string.Join(", ", missing)} first." : $"feed them {inputs} with an inserter.")}";
        if (crafters.Any(c => c.Status == FuelTank.StarvedText)) yield return "FIX: some of them have no coal. Feed them coal with an inserter (from a coal belt or the Nexus) or point a coal drill at them.";
        if (income <= 0 && crafters.Any(c => c.Produced.PerMinute(item, w.Tick) > 0))
            yield return $"FIX: {name} is made but never reaches storage: add an inserter + belt (or a turret) to take it out.";
        else if (income > 0)
            yield return $"FIX: build another {maker?.Name ?? "crafter"} to make more.";
    }
}
