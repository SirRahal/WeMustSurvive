namespace BeltAndBarricade.Sim;

/// <summary>
/// Mines the ore under it and outputs to the tile it faces. Burner drills burn coal (drills sitting on coal
/// fuel themselves); electric drills run on grid power. Claim zones mine faster and yield double.
/// </summary>
public sealed class Drill : Building, IItemAcceptor
{
    public const float ClaimZoneSpeedMultiplier = 1.6f;
    public const int ClaimZoneYield = 2;

    private FuelTank _fuel;
    private float _progress;
    private string? _output;
    private int _outputCount;

    public string? Ore { get; private set; }
    /// <summary>The ore this drill has been mining; kept after the tile runs dry (for the empty icon).</summary>
    public string? LastOre { get; private set; }
    /// <summary>Ore left under the drill, and how much the tile started with (refreshed every tick).</summary>
    public int OreLeft { get; private set; }
    public int OreStart { get; private set; }
    /// <summary>0..1 share of the tile's ore still in the ground.</summary>
    public float OreFraction => OreStart <= 0 ? 0 : (float)OreLeft / OreStart;
    /// <summary>The tile had ore and it is all mined out.</summary>
    public bool Depleted => OreStart > 0 && OreLeft == 0;
    /// <summary>Ore mined per minute (UI only).</summary>
    public RateMeter Mined { get; } = new();
    public bool NeedsFuel => Def.UsesFuel && Ore != "coal";
    public int Coal => _fuel.Coal;
    public float Fraction => Def.MineTicks <= 0 ? 0 : _progress / Def.MineTicks;

    protected override bool IsBusy => Ore != null && _outputCount == 0;

    public override void Tick(World w)
    {
        OreLeft = w.Map.OreAmount(Origin);
        OreStart = w.Map.OreStartAmount(Origin);
        if (_outputCount > 0)
        {
            if (!PushInto(w, Front, _output!))
            {
                _blockedReason = BlockedReason(w, _output!);
                return;
            }
            _blockedReason = null;
            if (--_outputCount > 0) return;
            _output = null;
        }

        Mined.Start(w.Tick);
        Ore = w.Map.OreAt(Origin);
        if (Ore == null) return;
        LastOre = Ore;
        float speed = PowerFactor * w.Mods.Mult(Stat.MiningSpeed);
        if (speed <= 0) return;
        bool claim = w.Map.IsClaimZone(Origin);
        if (claim) speed *= ClaimZoneSpeedMultiplier;
        if (NeedsFuel && !_fuel.Burn(w)) return;

        _progress += speed;
        if (_progress < Def.MineTicks) return;
        _progress -= Def.MineTicks;

        _output = w.Map.Mine(Origin);
        if (_output == null) return;
        OreLeft = w.Map.OreAmount(Origin);
        if (OreLeft == 0)
            for (int p = 0; p < w.Engineers.Count; p++)
                w.Message(p, $"A {Def.Name} ran out of {w.Data.Items[_output].Name.ToLowerInvariant()}");
        _outputCount = claim ? (int)MathF.Round(ClaimZoneYield * w.Mods.Mult(Stat.ClaimYield)) : 1;
        Mined.Add(_output, w.Tick, _outputCount);
        if (PushInto(w, Front, _output) && --_outputCount == 0) _output = null;
    }

    private string? _blockedReason;

    /// <summary>Plain-language reason the drill can't hand over what it mined.</summary>
    private string BlockedReason(World w, string item)
    {
        string name = item.Replace('_', ' ');
        return w.BuildingAt(Front) switch
        {
            null => "nothing in front to take it",
            Belt => "the belt in front is full",
            Crafter { Def.UsesFuel: true } c when item == "coal" && !c.Recipes.Any(r => r.Inputs.ContainsKey("coal")) =>
                $"the {c.Def.Name} is full of coal; it only burns coal while smelting ore, so feed it ore too",
            Drill d when item == "coal" && w.Map.OreAt(d.Origin) == "coal" => $"the {d.Def.Name} in front is on coal and fuels itself, so it won't take coal",
            Drill d when item == "coal" => $"the {d.Def.Name} in front is full of coal",
            Drill d => $"a {d.Def.Name} only takes coal, not {name}",
            { } b when b is IItemAcceptor && !LogisticsCheck.AcceptsType(w, b, item) => $"the {b.Def.Name} can't use {name}",
            { } b => $"the {b.Def.Name} in front is full",
        };
    }

    /// <summary>Most ore per minute this drill can mine where it stands (full fuel/power, bonuses and claim zone included).</summary>
    public float MaxPerMinute(World w)
    {
        float perTick = w.Mods.Mult(Stat.MiningSpeed) / Def.MineTicks;
        if (w.Map.IsClaimZone(Origin)) perTick *= ClaimZoneSpeedMultiplier * MathF.Round(ClaimZoneYield * w.Mods.Mult(Stat.ClaimYield));
        return perTick * 60 * World.TicksPerSecond;
    }

    /// <summary>
    /// Best way for a drill placed on <paramref name="tile"/> to face: a furnace that smelts its ore first, then
    /// something that burns coal (for coal), a belt, then storage. Null if no neighbour can take the ore.
    /// Used to aim drills automatically while placing them.
    /// </summary>
    public static Direction? BestOutputDir(World w, Int2 tile)
    {
        if (w.Map.OreAt(tile) is not { } ore) return null;
        Direction? best = null;
        int bestScore = 0;
        foreach (var d in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
        {
            var b = w.BuildingAt(tile + d.Offset());
            var def = b?.Def;
            int score = b switch
            {
                null => 0,
                Belt belt when belt.Front == tile => 0, // would feed straight back into the drill
                _ when def!.Kind == BuildingKind.Crafter && def.Recipes.Any(r => w.Data.Recipes[r].Inputs.ContainsKey(ore)) => 4,
                _ when ore == "coal" && (def!.UsesFuel || def.Kind == BuildingKind.Generator) && w.Map.OreAt(b.Origin) != "coal" => 3,
                _ when def!.Kind is BuildingKind.Belt or BuildingKind.Splitter or BuildingKind.TunnelEntrance => 3,
                _ when def!.Kind is BuildingKind.Nexus or BuildingKind.Depot => 2,
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        return best;
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
            if (_outputCount > 0) return _blockedReason == null ? "Output blocked" : $"Waiting: {_blockedReason}";
            if (Depleted) return "Depleted: the ore here is used up (deconstruct it for a full refund)";
            if (Ore == null) return "No ore";
            if (NeedsPower && PowerFactor <= 0) return "No power";
            if (NeedsFuel && _fuel.Starved) return FuelTank.StarvedText;
            return $"Mining {Ore}";
        }
    }

    public override void CollectContents(Inventory into)
    {
        into.Add("coal", _fuel.Coal);
        if (_output != null) into.Add(_output, _outputCount);
        _fuel.Coal = 0;
        _output = null;
        _outputCount = 0;
    }

    public override void HashInto(StateHash h)
    {
        _fuel.HashInto(h);
        h.Add(_progress);
        h.Add(_output);
        h.Add(_outputCount);
    }
}
