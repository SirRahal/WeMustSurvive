namespace BeltAndBarricade.Sim;

/// <summary>Burns coal to supply power to its grid. Fuel burns in proportion to how loaded the grid is.</summary>
public sealed class Generator : Building, IItemAcceptor, IBurner
{
    private FuelTank _fuel;

    public int Coal => _fuel.Coal;
    public bool HasFuel => _fuel.HasFuel;
    public bool BurnsFuel => true;
    public int CoalCapacity => FuelTank.MaxCoal * 2;
    public bool Burning => _fuel.BurnTicksLeft > 0;
    public bool OutOfFuel => !_fuel.HasFuel;
    private bool _canBurn;
    /// <summary>Fraction of capacity the grid used last tick (for the UI).</summary>
    public float Load { get; private set; }

    /// <summary>Full output while it has coal of its own (delivered by a drill, belt or inserter).</summary>
    public float AvailableOutput(World w)
    {
        _canBurn = _fuel.CanBurn;
        return _canBurn ? Def.PowerOutput * w.Mods.Mult(Stat.PowerOutput) : 0f;
    }

    /// <summary>Called by the power system with the grid's load fraction (0..1).</summary>
    public void Run(World w, float load)
    {
        Load = load;
        if (load > 0) _fuel.Burn(w, load);
    }

    public bool CanAccept(string item) => item == "coal" && _fuel.Coal < CoalCapacity;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        _fuel.Coal++;
        return true;
    }

    public override string Status => !_canBurn ? FuelTank.StarvedText : GridId < 0 ? "Not connected to a pole" : $"Load {Load:P0}";

    public override void CollectContents(Inventory into)
    {
        into.Add("coal", _fuel.Coal);
        _fuel.Coal = 0;
    }

    public override void HashInto(StateHash h) => _fuel.HashInto(h);
}

/// <summary>Links to other poles within wire reach and powers buildings within its supply radius.</summary>
public sealed class PowerPole : Building
{
    public override string Status => GridId < 0 ? "" : "Connected";
}

/// <summary>Contributes to the current research. Labs need no power.</summary>
public sealed class Lab : Building
{
    public bool Working { get; set; }
    public override string Status => Working ? "Researching" : "Idle (pick a research)";
}

/// <summary>
/// Nuclear Silo / Satellite Uplink. Charges on grid power; when full, any player can fire its ability at a target.
/// </summary>
public sealed class SuperWeapon : Building
{
    public float Charge { get; private set; }
    public float ChargeNeeded(World w) => Def.ChargeTicks * w.Mods.Mult(Stat.SuperWeaponCharge);
    public bool IsReady(World w) => Charge >= ChargeNeeded(w);

    protected override bool IsBusy => true;

    public override void Tick(World w)
    {
        if (!IsReady(w)) Charge = MathF.Min(ChargeNeeded(w), Charge + PowerFactor);
    }

    public void Fire() => Charge = 0;

    public override string Status => PowerFactor <= 0 ? "No power - not charging" : "Charging";

    public override void HashInto(StateHash h) => h.Add(Charge);
}
