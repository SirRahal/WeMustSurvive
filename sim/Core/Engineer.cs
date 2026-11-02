using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>Something an engineer has been told to do. Orders run one at a time, in queue order.</summary>
public abstract record EngineerOrder;

public sealed record MoveOrder(Vector2 Target) : EngineerOrder;

/// <summary>Walk to a construction site and build it.</summary>
public sealed record BuildOrder(int SiteId) : EngineerOrder;

public sealed record RepairOrder(int BuildingId) : EngineerOrder;

/// <summary>
/// A player's avatar. Driven RTS-style by an order queue (move / build / repair); paths around obstacles
/// and carries a small sidearm that fires automatically.
/// </summary>
public sealed class Engineer
{
    public const float MaxHp = 100;
    public const float Speed = 6f;
    public const int RespawnTicks = 5 * World.TicksPerSecond;
    public const float WeaponRange = 7f;
    public const float WeaponDamage = 5f;
    public const int WeaponCooldown = 20;
    /// <summary>How close (tiles, to the footprint edge) an engineer must be to build or repair.</summary>
    public const float WorkReach = 1.5f;
    public const float RepairPerTick = 2f;
    /// <summary>HP restored per iron plate spent on repairs.</summary>
    public const float RepairHpPerPlate = 50f;

    private readonly List<EngineerOrder> _orders = new();
    private readonly List<Vector2> _path = new();
    private bool _hasPath;
    private int _weaponCooldown;
    private float _repairCredit;

    public int PlayerId { get; }
    public Vector2 Pos { get; private set; }
    public float Hp { get; private set; } = MaxHp;
    public int RespawnIn { get; private set; }
    public bool Alive => RespawnIn == 0;
    public IReadOnlyList<EngineerOrder> Orders => _orders;
    /// <summary>Remaining waypoints toward the current order's destination.</summary>
    public IReadOnlyList<Vector2> Path => _path;
    /// <summary>Id of the building currently being built or repaired, if any (for effects).</summary>
    public int? WorkingOn { get; private set; }

    public Engineer(int playerId, Vector2 pos)
    {
        PlayerId = playerId;
        Pos = pos;
    }

    /// <summary>
    /// StarCraft-style: a queued order (Shift held) is chained onto the end; an unqueued order replaces the whole
    /// plan. Replaced blueprints that were never started are cancelled and refunded; half-built ones stay put.
    /// </summary>
    public void Order(World w, EngineerOrder order, bool queue)
    {
        if (!queue)
        {
            int cancelled = ClearOrders(w);
            if (cancelled > 0)
                w.Message(PlayerId, $"{cancelled} queued blueprint{(cancelled == 1 ? "" : "s")} cancelled and refunded (hold Shift to chain orders)");
        }
        _orders.Add(order);
    }

    /// <summary>Drops all orders. Sites that were never started are cancelled and refunded. Returns how many.</summary>
    public int ClearOrders(World w)
    {
        int cancelled = 0;
        foreach (var o in _orders)
            if (o is BuildOrder b && w.BuildingById(b.SiteId) is ConstructionSite { Started: false } site)
            {
                w.CancelSite(site);
                cancelled++;
            }
        _orders.Clear();
        ResetPath();
        WorkingOn = null;
        return cancelled;
    }

    public void Tick(World w)
    {
        if (!Alive)
        {
            if (--RespawnIn == 0)
            {
                Pos = w.EngineerSpawn;
                Hp = MaxHp;
            }
            return;
        }

        if (_weaponCooldown > 0) _weaponCooldown--;
        else if (w.FireAtNearestEnemy(Pos, WeaponRange, WeaponDamage)) _weaponCooldown = WeaponCooldown;

        WorkingOn = null;
        if (_orders.Count == 0) return;

        bool done = _orders[0] switch
        {
            MoveOrder m => StepMove(w, m),
            BuildOrder b => StepBuild(w, b),
            RepairOrder r => StepRepair(w, r),
            _ => true,
        };
        if (done)
        {
            _orders.RemoveAt(0);
            ResetPath();
        }
    }

    // ------------------------------------------------------------------ order steps (return true when finished)

    private bool StepMove(World w, MoveOrder m)
    {
        if (!_hasPath)
        {
            var goal = Int2.Floor(m.Target);
            var tiles = Pathfinder.Find(w, Int2.Floor(Pos), t => t == goal, m.Target);
            if (tiles == null)
            {
                w.Message(PlayerId, "Can't get there");
                return true;
            }
            SetPath(tiles, m.Target);
        }
        return FollowPath(w);
    }

    private bool StepBuild(World w, BuildOrder order)
    {
        if (w.BuildingById(order.SiteId) is not ConstructionSite site) return true;
        if (!InReach(site)) return !Approach(w, site);

        ResetPath();
        WorkingOn = site.Id;
        return w.AdvanceConstruction(site, w.Mods.Mult(Stat.BuildSpeed));
    }

    private bool StepRepair(World w, RepairOrder order)
    {
        var b = w.BuildingById(order.BuildingId);
        if (b == null || b is ConstructionSite || b.Hp >= b.MaxHp) return true;
        if (!InReach(b)) return !Approach(w, b);

        ResetPath();
        if (_repairCredit < RepairPerTick)
        {
            if (!w.Network.Remove("iron_plate"))
            {
                w.Message(PlayerId, "Repair needs iron plates in the Nexus");
                return true;
            }
            _repairCredit += RepairHpPerPlate;
        }
        WorkingOn = b.Id;
        float amount = MathF.Min(RepairPerTick * w.Mods.Mult(Stat.RepairSpeed), b.MaxHp - b.Hp);
        b.Hp += amount;
        _repairCredit -= amount;
        return b.Hp >= b.MaxHp;
    }

    // ------------------------------------------------------------------ movement

    /// <summary>Walks toward a spot within reach of <paramref name="b"/>. Returns false if it can't be reached.</summary>
    private bool Approach(World w, Building b)
    {
        if (!_hasPath)
        {
            var tiles = Pathfinder.Find(w, Int2.Floor(Pos), t => CanWorkFrom(t.Center, b), b.Center);
            if (tiles == null)
            {
                w.Message(PlayerId, $"Can't reach {b.Def.Name}");
                return false;
            }
            // The pathfinder judges reach from tile centres. If our own tile qualifies but we're standing at its
            // edge (out of reach), step to its centre rather than "arriving" forever without being in reach.
            if (tiles.Count == 0) tiles.Add(Int2.Floor(Pos));
            SetPath(tiles, null);
        }
        FollowPath(w);
        return true;
    }

    private bool InReach(Building b) => CanWorkFrom(Pos, b);

    /// <summary>Close enough to the footprint to work on it, but not standing inside it.</summary>
    private static bool CanWorkFrom(Vector2 p, Building b)
    {
        float cx = Math.Clamp(p.X, b.Origin.X, b.Origin.X + b.Size);
        float cy = Math.Clamp(p.Y, b.Origin.Y, b.Origin.Y + b.Size);
        bool inside = cx == p.X && cy == p.Y;
        return !inside && Vector2.Distance(p, new Vector2(cx, cy)) <= WorkReach;
    }

    private void SetPath(List<Int2> tiles, Vector2? exactEnd)
    {
        _path.Clear();
        foreach (var t in tiles) _path.Add(t.Center);
        if (exactEnd is Vector2 end)
        {
            if (_path.Count > 0) _path[^1] = end;
            else _path.Add(end);
        }
        _hasPath = true;
    }

    private void ResetPath()
    {
        _path.Clear();
        _hasPath = false;
    }

    /// <summary>Advances along the current path. Returns true once the end is reached.</summary>
    private bool FollowPath(World w)
    {
        if (_path.Count == 0)
        {
            _hasPath = false;
            return true;
        }

        // Something got built in the way: re-plan next tick.
        var nextTile = Int2.Floor(_path[0]);
        if (nextTile != Int2.Floor(Pos) && !Pathfinder.IsWalkable(w, nextTile))
        {
            ResetPath();
            return false;
        }

        float step = Speed * w.Mods.Mult(Stat.EngineerSpeed) / World.TicksPerSecond;
        var delta = _path[0] - Pos;
        float len = delta.Length();
        if (len <= step)
        {
            Pos = _path[0];
            _path.RemoveAt(0);
        }
        else
        {
            Pos += delta / len * step;
        }
        return _path.Count == 0;
    }

    public void TakeDamage(World w, float dmg)
    {
        if (!Alive) return;
        Hp -= dmg;
        if (Hp > 0) return;
        Hp = 0;
        RespawnIn = RespawnTicks;
        // Orders survive: after respawning the engineer picks up its build queue again.
        _orders.RemoveAll(o => o is MoveOrder);
        ResetPath();
        WorkingOn = null;
        w.Message(PlayerId, "Your engineer was downed! Respawning at the Nexus...");
    }

    public void HashInto(StateHash h)
    {
        h.Add(PlayerId);
        h.Add(Pos);
        h.Add(Hp);
        h.Add(RespawnIn);
        h.Add(_weaponCooldown);
        h.Add(_repairCredit);
        h.Add(_orders.Count);
        foreach (var p in _path) h.Add(p);
    }
}
