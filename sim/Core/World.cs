using System.Numerics;

namespace BeltAndBarricade.Sim;

public enum Outcome { Playing, Won, Lost }

/// <summary>
/// The whole simulation. Advance it with <see cref="Step"/> at a fixed <see cref="TicksPerSecond"/>;
/// change it only through <see cref="Enqueue"/>d commands. Has no engine dependencies.
/// </summary>
public sealed class World
{
    public const int TicksPerSecond = 60;
    /// <summary>Solo threat relative to co-op (the design calls for ~55% weaker).</summary>
    public const float SoloThreat = 0.45f;

    private readonly List<Building> _buildings = new();
    private readonly Dictionary<int, Building> _byId = new();
    private readonly List<Enemy> _enemies = new();
    private readonly List<Engineer> _engineers = new();
    private readonly List<ICommand> _pending = new();
    private readonly int[] _occupancy;
    private int _nextId = 1;
    private bool _flowDirty = true;

    public GameData Data { get; }
    public GameMap Map { get; }
    public Rng Rng { get; }
    public long Tick { get; private set; }
    public Outcome Outcome { get; private set; }
    /// <summary>True if the game was lost by surrendering rather than by losing the Nexus.</summary>
    public bool Surrendered { get; private set; }
    /// <summary>
    /// Per-axis multiplier applied to enemy health, damage, and count. Applied to all three,
    /// so overall threat scales by its cube (solo: cbrt(0.45)^3 = 0.45).
    /// </summary>
    public float ThreatAxis { get; }
    public Nexus Nexus { get; }
    public Inventory Storage => Nexus.Storage;
    public Vector2 EngineerSpawn { get; }
    public WaveDirector Waves { get; }
    public FlowField Flow { get; }
    public IReadOnlyList<Building> Buildings => _buildings;
    public IReadOnlyList<Enemy> Enemies => _enemies;
    public IReadOnlyList<Engineer> Engineers => _engineers;
    /// <summary>Events from the most recent <see cref="Step"/>. Cleared at the start of each step.</summary>
    public List<SimEvent> Events { get; } = new();

    public World(GameData data, Scenario scenario, ulong seed, int playerCount = 1)
    {
        Data = data;
        Map = MapGenerator.Generate(scenario.Map);
        Rng = new Rng(seed);
        ThreatAxis = playerCount <= 1 ? MathF.Cbrt(SoloThreat) : 1f;
        _occupancy = new int[Map.Width * Map.Height];

        var nexusOrigin = new Int2(scenario.Map.Nexus.X, scenario.Map.Nexus.Y);
        Nexus = (Nexus)AddBuilding(data.Buildings["nexus"], nexusOrigin, Direction.North);
        Storage.AddAll(scenario.Map.StartingItems);

        EngineerSpawn = new Vector2(Nexus.Center.X, Nexus.Origin.Y - 1.5f);
        for (int p = 0; p < playerCount; p++)
            _engineers.Add(new Engineer(p, EngineerSpawn + new Vector2(p * 1.5f, 0)));

        Flow = new FlowField(this);
        Waves = new WaveDirector(scenario.Waves);
    }

    public void Enqueue(ICommand cmd) => _pending.Add(cmd);

    public void Step()
    {
        Events.Clear();
        if (Outcome != Outcome.Playing) return;

        foreach (var cmd in _pending)
        {
            string? error = cmd.Apply(this);
            if (error != null) Message(cmd.PlayerId, error);
        }
        _pending.Clear();
        if (Outcome != Outcome.Playing) return; // e.g. a surrender command

        if (_flowDirty)
        {
            Flow.Recompute();
            _flowDirty = false;
        }

        foreach (var e in _engineers) e.Tick(this);
        for (int i = 0; i < _buildings.Count; i++)
            if (!_buildings[i].Dead) _buildings[i].Tick(this);
        foreach (var e in _enemies)
            if (!e.Dead) e.Tick(this);
        Waves.Tick(this);

        _buildings.RemoveAll(b => b.Dead);
        _enemies.RemoveAll(e => e.Dead);

        if (Nexus.Dead) Outcome = Outcome.Lost;
        else if (Waves.AllSpawned && _enemies.Count == 0) Outcome = Outcome.Won;
        Tick++;
    }

    // ---------------------------------------------------------------- queries

    public Building? BuildingAt(Int2 t) =>
        Map.InBounds(t) && _occupancy[Map.Index(t)] is int id and > 0 ? _byId[id] : null;

    public Building? BuildingById(int id) => _byId.TryGetValue(id, out var b) && !b.Dead ? b : null;

    public Enemy? NearestEnemy(Vector2 p, float range)
    {
        Enemy? best = null;
        float bestD = range * range;
        foreach (var e in _enemies)
        {
            if (e.Dead) continue;
            float d = Vector2.DistanceSquared(e.Pos, p);
            if (d <= bestD)
            {
                bestD = d;
                best = e;
            }
        }
        return best;
    }

    /// <summary>Why <paramref name="def"/> can't be placed here, or null if it can (ignoring cost).</summary>
    public string? CheckPlacement(int playerId, BuildingDef def, Int2 origin)
    {
        if (def.Kind == BuildingKind.Nexus) return "Can't build another Nexus";
        var eng = _engineers[playerId];
        if (!eng.Alive) return "Engineer is down";

        for (int y = 0; y < def.Size; y++)
        for (int x = 0; x < def.Size; x++)
        {
            var t = new Int2(origin.X + x, origin.Y + y);
            if (!Map.IsBuildable(t)) return "Can't build there";
            if (BuildingAt(t) != null) return "Tile is occupied";
            if (def.BlocksEnemies && _enemies.Any(e => Int2.Floor(e.Pos) == t)) return "Enemy in the way";
        }

        if (def.Kind == BuildingKind.Drill && Map.OreAt(origin) == null) return "Drills must be placed on ore";
        return null;
    }

    // ---------------------------------------------------------------- mutations (called by commands / sim)

    /// <summary>
    /// Pays for a construction site at <paramref name="origin"/> and adds it to the end of the engineer's
    /// build queue. Blueprints always queue, so nothing you place is silently abandoned.
    /// </summary>
    public string? TryPlace(int playerId, string defId, Int2 origin, Direction dir)
    {
        if (!Data.Buildings.TryGetValue(defId, out var def)) return $"Unknown building '{defId}'";
        string? error = CheckPlacement(playerId, def, origin);
        if (error != null) return error;

        var eng = _engineers[playerId];
        string? missing = Storage.FirstMissing(def.Cost);
        if (missing != null) return $"Not enough {Data.Items[missing].Name}";

        Storage.RemoveAll(def.Cost);
        var site = Building.CreateSite(Data, def, _nextId++, origin, dir);
        Register(site);
        eng.Order(this, new BuildOrder(site.Id), queue: true);
        return null;
    }

    /// <summary>One tick of engineer work on <paramref name="site"/>. Returns true when it completes.</summary>
    public bool AdvanceConstruction(ConstructionSite site)
    {
        bool wasStarted = site.Started;
        bool done = site.Advance();
        if (!wasStarted && site.BlocksEnemies) _flowDirty = true;
        if (!done) return false;

        RemoveBuilding(site);
        var b = AddBuilding(site.Def, site.Origin, site.Dir);
        b.Hp = site.Hp;
        return true;
    }

    /// <summary>Cancels an unfinished site and refunds its full cost.</summary>
    public void CancelSite(ConstructionSite site)
    {
        Storage.AddAll(site.Def.Cost);
        RemoveBuilding(site);
    }

    public string? TryRemove(int playerId, Int2 tile)
    {
        var b = BuildingAt(tile);
        if (b == null) return "Nothing to remove";
        if (b is Nexus) return "Can't remove the Nexus";

        Storage.AddAll(b.Def.Cost);
        b.CollectContents(Storage);
        RemoveBuilding(b);
        return null;
    }

    public void DamageBuilding(Building b, float dmg)
    {
        if (b.Dead) return;
        b.Hp -= dmg;
        if (b.Hp > 0)
        {
            // HP feeds the flow field cost, so walls being chewed become more attractive to path through.
            if (b.BlocksEnemies) _flowDirty = true;
            return;
        }
        Events.Add(new BuildingDestroyedEvent(b.Origin, b.Def.Id));
        RemoveBuilding(b);
    }

    /// <summary>Damages the nearest enemy in range. Returns true if a shot was fired.</summary>
    public bool FireAtNearestEnemy(Vector2 from, float range, float damage)
    {
        var target = NearestEnemy(from, range);
        if (target == null) return false;
        Events.Add(new ShotEvent(from, target.Pos));
        target.Hp -= damage;
        if (target.Hp <= 0) target.Dead = true;
        return true;
    }

    public Enemy SpawnEnemy(EnemyDef def) => SpawnEnemy(def, Map.SpawnTiles[Rng.Next(Map.SpawnTiles.Count)].Center);

    public Enemy SpawnEnemy(EnemyDef def, Vector2 pos)
    {
        var e = new Enemy(_nextId++, def, pos, ThreatAxis, Rng);
        _enemies.Add(e);
        return e;
    }

    /// <summary>Places a finished building instantly, with no cost. For scenario setup, tests, and debug tools.</summary>
    public Building PlaceDirect(string defId, Int2 origin, Direction dir = Direction.North) =>
        AddBuilding(Data.Buildings[defId], origin, dir);

    public void Message(int playerId, string text) => Events.Add(new MessageEvent(playerId, text));

    /// <summary>Ends the game as a loss. Called via <see cref="SurrenderCommand"/>.</summary>
    public void Surrender()
    {
        if (Outcome != Outcome.Playing) return;
        Surrendered = true;
        Outcome = Outcome.Lost;
    }

    private Building AddBuilding(BuildingDef def, Int2 origin, Direction dir) =>
        Register(Building.Create(Data, def, _nextId++, origin, dir));

    private Building Register(Building b)
    {
        _buildings.Add(b);
        _byId[b.Id] = b;
        foreach (var t in b.Tiles) _occupancy[Map.Index(t)] = b.Id;
        if (b.BlocksEnemies) _flowDirty = true;
        return b;
    }

    private void RemoveBuilding(Building b)
    {
        b.Dead = true;
        _byId.Remove(b.Id);
        foreach (var t in b.Tiles) _occupancy[Map.Index(t)] = 0;
        if (b.BlocksEnemies) _flowDirty = true;
    }

    // ---------------------------------------------------------------- determinism

    public ulong ComputeHash()
    {
        var h = new StateHash();
        h.Add(Tick);
        h.Add((long)Rng.State);
        Map.HashInto(h);
        foreach (var b in _buildings)
        {
            h.Add(b.Id);
            h.Add(b.Def.Id);
            h.Add(b.Origin.X);
            h.Add(b.Origin.Y);
            h.Add((int)b.Dir);
            h.Add(b.Hp);
            b.HashInto(h);
        }
        foreach (var e in _enemies) e.HashInto(h);
        foreach (var e in _engineers) e.HashInto(h);
        Waves.HashInto(h);
        return h.Value;
    }
}
