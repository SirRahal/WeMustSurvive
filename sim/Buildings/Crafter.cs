namespace BeltAndBarricade.Sim;

/// <summary>
/// Furnaces and workshops. Automatically runs the first of its recipes whose inputs are available.
/// Inserters feed inputs and coal in, and pull outputs out. Electric versions run on grid power instead of coal.
/// </summary>
public sealed class Crafter : Building, IItemAcceptor, IItemSource, IBurner
{
    public const int InputBatches = 5;
    public const int MaxOutput = 20;

    private FuelTank _fuel;
    private List<RecipeDef> _recipes = new();

    public Inventory Input { get; } = new();
    public Inventory Output { get; } = new();
    /// <summary>Items delivered in (ore, plates...) and fuel burned, per minute. UI only.</summary>
    public RateMeter Received { get; } = new();
    public RateMeter Produced { get; } = new();
    private long _now;
    public RecipeDef? Active { get; private set; }
    /// <summary>The recipe it ran most recently (UI only: what an idle crafter is waiting for).</summary>
    public RecipeDef? LastRecipe { get; private set; }
    public float Progress { get; private set; }
    public int Coal => _fuel.Coal;
    public bool BurnsFuel => Def.UsesFuel;
    public int CoalCapacity => FuelTank.MaxCoal;
    public bool Burning => _fuel.BurnTicksLeft > 0;
    public bool OutOfFuel => _fuel.Starved;
    public IReadOnlyList<RecipeDef> Recipes => _recipes;

    protected override bool IsBusy => Active != null;

    protected override void Bind(GameData data) => _recipes = Def.Recipes.Select(r => data.Recipes[r]).ToList();

    public override void Tick(World w)
    {
        _now = w.Tick;
        Received.Start(w.Tick);
        Produced.Start(w.Tick);
        if (Active == null)
        {
            StockedOut = null;
            Active = _recipes.FirstOrDefault(r =>
                Input.HasAll(r.Inputs) && r.Outputs.All(o => Output.Count(o.Key) + o.Value <= MaxOutput) && !AtStockLimit(w, r));
            if (Active == null) return;
            LastRecipe = Active;
            Input.RemoveAll(Active.Inputs);
            Progress = 0;
        }

        float speed = PowerFactor * Def.CraftSpeed * w.Mods.Mult(Stat.CraftingSpeed);
        if (speed <= 0) return;
        if (Def.UsesFuel && !_fuel.Burn(w)) return;
        Progress += speed;
        if (Progress < Active.Ticks) return;

        Output.AddAll(Active.Outputs);
        foreach (var (item, n) in Active.Outputs) Produced.Add(item, w.Tick, n);
        Active = null;
    }

    /// <summary>Set when a recipe is paused because the storage network already has plenty of its output.</summary>
    public string? StockedOut { get; private set; }

    /// <summary>
    /// Munitions recipes pause once the network holds their stock limit, so a workshop feeding the Nexus doesn't
    /// quietly turn all your iron into ammo.
    /// </summary>
    private bool AtStockLimit(World w, RecipeDef r)
    {
        if (r.StockLimit <= 0) return false;
        var output = r.Outputs.Keys.First();
        if (w.Network.Count(output) < r.StockLimit) return false;
        if (Input.HasAll(r.Inputs)) StockedOut = output;
        return true;
    }

    /// <summary>Most this crafter can turn out per minute of <paramref name="recipe"/>'s first output (at full power).</summary>
    public float MaxPerMinute(World w, RecipeDef recipe) =>
        60f * World.TicksPerSecond / recipe.Ticks * Def.CraftSpeed * w.Mods.Mult(Stat.CraftingSpeed) * recipe.Outputs.Values.First();

    private bool IsFuel(string item) => Def.UsesFuel && item == "coal";

    public bool CanAccept(string item)
    {
        if (IsFuel(item)) return _fuel.Coal < FuelTank.MaxCoal;
        return _recipes.Any(r => r.Inputs.TryGetValue(item, out int n) && Input.Count(item) < n * InputBatches);
    }

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        if (IsFuel(item)) _fuel.Coal++;
        else Input.Add(item);
        Received.Add(item, _now);
        return true;
    }

    public bool TryTake(Func<string, bool> want, out string item)
    {
        foreach (var (id, _) in Output.Entries)
        {
            if (!want(id)) continue;
            Output.Remove(id);
            item = id;
            return true;
        }
        item = "";
        return false;
    }

    public override string Status
    {
        get
        {
            if (Active == null && StockedOut != null) return $"Paused: storage already has plenty of {StockedOut.Replace('_', ' ')}";
            if (Active == null) return "Waiting for input";
            if (NeedsPower && PowerFactor <= 0) return "No power";
            if (Def.UsesFuel && _fuel.Starved) return FuelTank.StarvedText;
            return $"Crafting {Active.Id} {Math.Min(99, (int)(Progress * 100 / Active.Ticks))}%";
        }
    }

    public override void CollectContents(Inventory into)
    {
        into.AddAll(Input);
        into.AddAll(Output);
        if (Active != null) into.AddAll(Active.Inputs);
        into.Add("coal", _fuel.Coal);
        Input.Clear();
        Output.Clear();
        Active = null;
        _fuel.Coal = 0;
    }

    public override void HashInto(StateHash h)
    {
        Input.HashInto(h);
        Output.HashInto(h);
        h.Add(Active?.Id);
        h.Add(Progress);
        _fuel.HashInto(h);
    }
}
