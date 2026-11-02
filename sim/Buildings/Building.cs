using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>Something an inserter, belt, or drill can push an item into.</summary>
public interface IItemAcceptor
{
    bool CanAccept(string item);
    bool TryInsert(string item);
}

/// <summary>Something an inserter can pull an item out of.</summary>
public interface IItemSource
{
    bool TryTake(Func<string, bool> want, out string item);
}

/// <summary>Acceptors a belt can feed directly from its end (no inserter needed).</summary>
public interface IBeltInput : IItemAcceptor { }

public abstract class Building
{
    /// <summary>Below this grid satisfaction, powered buildings shut down entirely.</summary>
    public const float BrownoutCutoff = 0.25f;

    public int Id { get; private set; }
    public BuildingDef Def { get; private set; } = null!;
    public Int2 Origin { get; private set; }
    public Direction Dir { get; set; }
    public float Hp { get; set; }
    /// <summary>Max HP after modifiers (e.g. wall health bonuses).</summary>
    public float MaxHp { get; private set; }
    public bool Dead { get; set; }

    /// <summary>Grid satisfaction (0..1) delivered by the power system last tick. 1 for unpowered buildings.</summary>
    public float Power { get; set; } = 1f;
    /// <summary>Index of the power grid this building is on, or -1.</summary>
    public int GridId { get; set; } = -1;

    public int Size => Def.Size;
    /// <summary>Whether enemies must break this to pass (and engineers can't walk through it).</summary>
    public virtual bool BlocksEnemies => Def.BlocksEnemies;
    public bool NeedsPower => Def.PowerDraw > 0;
    /// <summary>Speed multiplier from power: 1 when unpowered-by-design, satisfaction otherwise, 0 in a brownout.</summary>
    public float PowerFactor => !NeedsPower ? 1f : Power < BrownoutCutoff ? 0f : Power;
    /// <summary>kW this building wants from its grid this tick.</summary>
    public float PowerDemand => !NeedsPower ? 0f : IsBusy ? Def.PowerDraw : Def.IdleDraw;
    /// <summary>Whether the building is actively working (draws full power).</summary>
    protected virtual bool IsBusy => true;

    public Vector2 Center => new(Origin.X + Size / 2f, Origin.Y + Size / 2f);
    /// <summary>The tile this building faces (outputs into), measured from its origin. Only meaningful for 1x1 buildings.</summary>
    public Int2 Front => Origin + Dir.Offset();
    public Int2 Back => Origin + Dir.Opposite().Offset();

    public IEnumerable<Int2> Tiles
    {
        get
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                yield return new Int2(Origin.X + x, Origin.Y + y);
        }
    }

    /// <summary>Distance from <paramref name="p"/> to the nearest point of this building's footprint (0 if inside).</summary>
    public float DistanceTo(Vector2 p)
    {
        float cx = Math.Clamp(p.X, Origin.X, Origin.X + Size);
        float cy = Math.Clamp(p.Y, Origin.Y, Origin.Y + Size);
        return Vector2.Distance(p, new Vector2(cx, cy));
    }

    public static Building Create(GameData data, BuildingDef def, int id, Int2 origin, Direction dir, float maxHp)
    {
        Building b = def.Kind switch
        {
            BuildingKind.Belt => new Belt(),
            BuildingKind.Inserter => new Inserter(),
            BuildingKind.Drill => new Drill(),
            BuildingKind.Crafter => new Crafter(),
            BuildingKind.Wall => new Wall(),
            BuildingKind.Turret => new Turret(),
            BuildingKind.Nexus => new Nexus(),
            BuildingKind.Generator => new Generator(),
            BuildingKind.Pole => new PowerPole(),
            BuildingKind.Lab => new Lab(),
            BuildingKind.Splitter => new Splitter(),
            BuildingKind.TunnelEntrance => new TunnelEntrance(),
            BuildingKind.TunnelExit => new TunnelExit(),
            BuildingKind.Depot => new Depot(),
            BuildingKind.SuperWeapon => new SuperWeapon(),
            _ => throw new ArgumentOutOfRangeException(nameof(def), def.Kind, null),
        };
        return b.Init(data, def, id, origin, dir, maxHp, maxHp);
    }

    /// <summary>Creates an unbuilt construction site for <paramref name="def"/>.</summary>
    public static ConstructionSite CreateSite(GameData data, BuildingDef def, int id, Int2 origin, Direction dir, float maxHp) =>
        (ConstructionSite)new ConstructionSite().Init(data, def, id, origin, dir, maxHp, maxHp * ConstructionSite.StartHpFraction);

    private Building Init(GameData data, BuildingDef def, int id, Int2 origin, Direction dir, float maxHp, float hp)
    {
        Id = id;
        Def = def;
        Origin = origin;
        Dir = dir;
        MaxHp = maxHp;
        Hp = hp;
        Bind(data);
        return this;
    }

    /// <summary>Resolve any definition references once at creation.</summary>
    protected virtual void Bind(GameData data) { }

    public virtual void Tick(World w) { }

    /// <summary>Moves any held items into <paramref name="into"/> (used to refund on deconstruction).</summary>
    public virtual void CollectContents(Inventory into) { }

    /// <summary>Short status line for the UI.</summary>
    public virtual string Status => "";

    public virtual void HashInto(StateHash h) { }

    /// <summary>Pushes an item into whatever sits on <paramref name="tile"/> (belt or acceptor). Used by drills and tunnels.</summary>
    protected static bool PushInto(World w, Int2 tile, string item) => w.BuildingAt(tile) switch
    {
        Belt belt => belt.TryInsertAt(item, Belt.DropPosition),
        IItemAcceptor a => a.TryInsert(item),
        _ => false,
    };

    /// <summary>Pushes onto the start of a belt (or into an acceptor). Used by splitters and tunnel exits.</summary>
    protected static bool PushOnto(World w, Building from, Int2 tile, string item) => w.BuildingAt(tile) switch
    {
        Belt belt when belt.Front != from.Origin => belt.TryInsertAt(item, 0, w.Tick),
        IBeltInput a => a.TryInsert(item),
        _ => false,
    };
}

public sealed class Wall : Building { }

/// <summary>
/// Burner fuel bookkeeping shared by drills, crafters and generators. Coal delivered into the building is burned
/// first; when that runs out it automatically draws coal from the storage network (Nexus + depots), so players
/// only have to get coal home, not route it to every machine.
/// </summary>
public struct FuelTank
{
    public const int MaxCoal = 5;

    public int Coal;
    public float BurnTicksLeft;
    /// <summary>True when the last burn found no coal locally or in storage.</summary>
    public bool Starved;

    public bool HasFuel => Coal > 0 || BurnTicksLeft > 0;

    /// <summary>Could this tank keep burning (local fuel, or coal available in storage)?</summary>
    public bool CanBurn(World w) => HasFuel || w.Network.Count("coal") > 0;

    /// <summary>Consumes <paramref name="amount"/> ticks of fuel, lighting a new coal if needed. False when out of fuel.</summary>
    public bool Burn(World w, float amount = 1f)
    {
        if (BurnTicksLeft <= 0)
        {
            if (Coal > 0) Coal--;
            else if (!w.Network.Remove("coal"))
            {
                Starved = true;
                return false;
            }
            BurnTicksLeft += w.Data.FuelTicks("coal");
        }
        Starved = false;
        BurnTicksLeft -= amount;
        return true;
    }

    public const string StarvedText = "No fuel: no coal here or in storage. Mine coal into the Nexus";

    public void HashInto(StateHash h)
    {
        h.Add(Coal);
        h.Add(BurnTicksLeft);
    }
}
