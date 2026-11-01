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

public abstract class Building
{
    public int Id { get; private set; }
    public BuildingDef Def { get; private set; } = null!;
    public Int2 Origin { get; private set; }
    public Direction Dir { get; set; }
    public float Hp { get; set; }
    public bool Dead { get; set; }

    public int Size => Def.Size;
    /// <summary>Whether enemies must break this to pass (and engineers can't walk through it).</summary>
    public virtual bool BlocksEnemies => Def.BlocksEnemies;
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

    public static Building Create(GameData data, BuildingDef def, int id, Int2 origin, Direction dir)
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
            _ => throw new ArgumentOutOfRangeException(nameof(def), def.Kind, null),
        };
        return b.Init(data, def, id, origin, dir, def.MaxHp);
    }

    /// <summary>Creates an unbuilt construction site for <paramref name="def"/>.</summary>
    public static ConstructionSite CreateSite(GameData data, BuildingDef def, int id, Int2 origin, Direction dir) =>
        (ConstructionSite)new ConstructionSite().Init(data, def, id, origin, dir, def.MaxHp * ConstructionSite.StartHpFraction);

    private Building Init(GameData data, BuildingDef def, int id, Int2 origin, Direction dir, float hp)
    {
        Id = id;
        Def = def;
        Origin = origin;
        Dir = dir;
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

    /// <summary>Pushes an item into whatever sits on <paramref name="tile"/> (belt or acceptor). Used by drills.</summary>
    protected static bool PushInto(World w, Int2 tile, string item) => w.BuildingAt(tile) switch
    {
        Belt belt => belt.TryInsertAt(item, Belt.DropPosition),
        IItemAcceptor a => a.TryInsert(item),
        _ => false,
    };
}

public sealed class Wall : Building { }

/// <summary>Burner fuel bookkeeping shared by drills and crafters.</summary>
public struct FuelTank
{
    public const int MaxCoal = 5;

    public int Coal;
    public int BurnTicksLeft;

    /// <summary>Consumes one tick of fuel, lighting a new coal if needed. Returns false when out of fuel.</summary>
    public bool Burn(World w)
    {
        if (BurnTicksLeft <= 0)
        {
            if (Coal <= 0) return false;
            Coal--;
            BurnTicksLeft += w.Data.FuelTicks("coal");
        }
        BurnTicksLeft--;
        return true;
    }

    public void HashInto(StateHash h)
    {
        h.Add(Coal);
        h.Add(BurnTicksLeft);
    }
}
