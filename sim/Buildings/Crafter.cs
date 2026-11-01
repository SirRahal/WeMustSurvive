namespace BeltAndBarricade.Sim;

/// <summary>
/// Furnaces and workshops. Automatically runs the first of its recipes whose inputs are available.
/// Inserters feed inputs and coal in, and pull outputs out.
/// </summary>
public sealed class Crafter : Building, IItemAcceptor, IItemSource
{
    public const int InputBatches = 5;
    public const int MaxOutput = 20;

    private FuelTank _fuel;
    private List<RecipeDef> _recipes = new();

    public Inventory Input { get; } = new();
    public Inventory Output { get; } = new();
    public RecipeDef? Active { get; private set; }
    public int Progress { get; private set; }
    public int Coal => _fuel.Coal;

    protected override void Bind(GameData data) => _recipes = Def.Recipes.Select(r => data.Recipes[r]).ToList();

    public override void Tick(World w)
    {
        if (Active == null)
        {
            Active = _recipes.FirstOrDefault(r =>
                Input.HasAll(r.Inputs) && r.Outputs.All(o => Output.Count(o.Key) + o.Value <= MaxOutput));
            if (Active == null) return;
            Input.RemoveAll(Active.Inputs);
            Progress = 0;
        }

        if (Def.UsesFuel && !_fuel.Burn(w)) return;
        if (++Progress < Active.Ticks) return;

        Output.AddAll(Active.Outputs);
        Active = null;
    }

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
            if (Active == null) return "Waiting for input";
            if (Def.UsesFuel && _fuel.Coal == 0 && _fuel.BurnTicksLeft <= 0) return "No fuel";
            return $"Crafting {Active.Id} {Progress * 100 / Active.Ticks}%";
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
