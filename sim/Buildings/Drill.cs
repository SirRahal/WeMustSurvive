namespace BeltAndBarricade.Sim;

/// <summary>
/// Burner drill. Mines the ore under it and outputs to the tile it faces.
/// Drills sitting on coal fuel themselves; others need coal delivered. Claim zones mine faster.
/// </summary>
public sealed class Drill : Building, IItemAcceptor
{
    public const float ClaimZoneSpeedMultiplier = 1.6f;

    private FuelTank _fuel;
    private int _progress;
    private string? _output;

    public string? Ore { get; private set; }
    public bool NeedsFuel => Def.UsesFuel && Ore != "coal";
    public int Coal => _fuel.Coal;

    public override void Tick(World w)
    {
        if (_output != null)
        {
            if (!PushInto(w, Front, _output)) return;
            _output = null;
        }

        Ore = w.Map.OreAt(Origin);
        if (Ore == null) return;
        if (NeedsFuel && !_fuel.Burn(w)) return;

        int mineTicks = w.Map.IsClaimZone(Origin)
            ? (int)(Def.MineTicks / ClaimZoneSpeedMultiplier)
            : Def.MineTicks;
        if (++_progress < mineTicks) return;

        _progress = 0;
        _output = w.Map.Mine(Origin);
        if (_output != null && PushInto(w, Front, _output)) _output = null;
    }

    public bool CanAccept(string item) => item == "coal" && NeedsFuel && _fuel.Coal < FuelTank.MaxCoal;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        _fuel.Coal++;
        return true;
    }

    public override string Status
    {
        get
        {
            if (Ore == null) return "No ore";
            if (_output != null) return "Output blocked";
            if (NeedsFuel && _fuel.Coal == 0 && _fuel.BurnTicksLeft <= 0) return "No fuel";
            return $"Mining {Ore}";
        }
    }

    public override void CollectContents(Inventory into)
    {
        into.Add("coal", _fuel.Coal);
        if (_output != null) into.Add(_output);
        _fuel.Coal = 0;
        _output = null;
    }

    public override void HashInto(StateHash h)
    {
        _fuel.HashInto(h);
        h.Add(_progress);
        h.Add(_output);
    }
}
