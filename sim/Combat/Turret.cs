namespace BeltAndBarricade.Sim;

/// <summary>Ammo-fed turret. Each ammo item loads <see cref="BuildingDef.RoundsPerAmmo"/> shots.</summary>
public sealed class Turret : Building, IItemAcceptor
{
    public int Ammo { get; private set; }
    public int Rounds { get; private set; }
    private int _cooldown;

    public override void Tick(World w)
    {
        if (_cooldown > 0) { _cooldown--; return; }
        if (Rounds == 0 && Ammo == 0) return;
        if (!w.FireAtNearestEnemy(Center, Def.Range, Def.Damage)) return;
        if (Rounds == 0)
        {
            Ammo--;
            Rounds = Def.RoundsPerAmmo;
        }
        Rounds--;
        _cooldown = Def.CooldownTicks;
    }

    public bool CanAccept(string item) => item == Def.AmmoItem && Ammo < Def.AmmoCapacity;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        Ammo++;
        return true;
    }

    public override string Status => Ammo == 0 && Rounds == 0 ? "No ammo" : $"Ammo {Ammo} (+{Rounds} rounds)";

    public override void CollectContents(Inventory into)
    {
        if (Def.AmmoItem != null) into.Add(Def.AmmoItem, Ammo);
        Ammo = 0;
    }

    public override void HashInto(StateHash h)
    {
        h.Add(Ammo);
        h.Add(Rounds);
        h.Add(_cooldown);
    }
}
