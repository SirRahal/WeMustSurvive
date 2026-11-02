namespace BeltAndBarricade.Sim;

/// <summary>
/// All turret types. Ammo turrets load <see cref="BuildingDef.RoundsPerAmmo"/> shots per ammo item (gun: magazines,
/// rocket: rockets, flame: coal, heavy: shells); laser turrets have no ammo and run on grid power instead.
/// Splash turrets damage everything around the target.
/// </summary>
public sealed class Turret : Building, IItemAcceptor
{
    private const int FiringMemoryTicks = 90;

    public int Ammo { get; private set; }
    public int Rounds { get; private set; }
    private float _cooldown;
    private int _firingMemory;

    public bool UsesAmmo => Def.AmmoItem != null;
    public float Range(World w) => Def.Range * w.Mods.Mult(Stat.TurretRange);
    protected override bool IsBusy => _firingMemory > 0;

    public override void Tick(World w)
    {
        if (_firingMemory > 0) _firingMemory--;
        float speed = PowerFactor;
        if (speed <= 0) return;
        if (_cooldown > 0) { _cooldown -= speed; return; }
        if (UsesAmmo && Rounds == 0 && Ammo == 0) return;

        var target = w.NearestEnemy(Center, Range(w));
        if (target == null) return;

        float damage = Def.Damage * w.Mods.Mult(Stat.TurretDamage);
        w.Events.Add(new ShotEvent(Center, target.Pos, Def.ShotKind));
        if (Def.Splash > 0) w.DamageEnemiesInRadius(target.Pos, Def.Splash, damage);
        else w.DamageEnemy(target, damage);

        if (UsesAmmo)
        {
            if (Rounds == 0)
            {
                Ammo--;
                Rounds = Def.RoundsPerAmmo;
            }
            Rounds--;
        }
        _cooldown = Def.CooldownTicks;
        _firingMemory = FiringMemoryTicks;
    }

    public bool CanAccept(string item) => UsesAmmo && item == Def.AmmoItem && Ammo < Def.AmmoCapacity;

    public bool TryInsert(string item)
    {
        if (!CanAccept(item)) return false;
        Ammo++;
        return true;
    }

    public override string Status
    {
        get
        {
            if (!UsesAmmo) return PowerFactor <= 0 ? "No power" : Power < 1 ? $"Low power ({Power:P0})" : "Ready";
            if (Ammo == 0 && Rounds == 0) return "No ammo";
            string full = Ammo >= Def.AmmoCapacity ? " (full)" : "";
            return Def.RoundsPerAmmo > 1
                ? $"Ammo {Ammo}/{Def.AmmoCapacity}{full} +{Rounds} shots loaded"
                : $"Ammo {Ammo}/{Def.AmmoCapacity}{full}";
        }
    }

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
        h.Add(_firingMemory);
    }
}
