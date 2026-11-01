namespace BeltAndBarricade.Sim;

/// <summary>
/// The core the players defend. Acts as shared storage (building costs are paid from it),
/// accepts anything belts or inserters deliver, and has a weak built-in gun with unlimited ammo.
/// </summary>
public sealed class Nexus : Building, IItemAcceptor, IItemSource
{
    private int _cooldown;

    public Inventory Storage { get; } = new();
    /// <summary>Running total of everything delivered by belts and inserters (not starting stock).</summary>
    public Inventory Received { get; } = new();

    public override void Tick(World w)
    {
        if (_cooldown > 0) { _cooldown--; return; }
        if (w.FireAtNearestEnemy(Center, Def.Range, Def.Damage)) _cooldown = Def.CooldownTicks;
    }

    public bool CanAccept(string item) => true;

    public bool TryInsert(string item)
    {
        Storage.Add(item);
        Received.Add(item);
        return true;
    }

    public bool TryTake(Func<string, bool> want, out string item)
    {
        foreach (var (id, _) in Storage.Entries)
        {
            if (!want(id)) continue;
            Storage.Remove(id);
            item = id;
            return true;
        }
        item = "";
        return false;
    }

    public override void HashInto(StateHash h)
    {
        Storage.HashInto(h);
        h.Add(_cooldown);
    }
}
