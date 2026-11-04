using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeltAndBarricade.Sim;

/// <summary>
/// Every player action is a command. Commands are the only way player intent changes the sim, so in co-op the
/// host can order commands from both players and both machines apply exactly the same ones on the same tick.
/// Orders with <c>Queue</c> set (Shift held) are chained onto the end of the engineer's queue; without it they replace
/// the current plan (unstarted blueprints in it are refunded), like StarCraft.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(PlaceBuildingCommand), "place")]
[JsonDerivedType(typeof(MoveCommand), "move")]
[JsonDerivedType(typeof(RepairCommand), "repair")]
[JsonDerivedType(typeof(StopCommand), "stop")]
[JsonDerivedType(typeof(RemoveBuildingCommand), "remove")]
[JsonDerivedType(typeof(RotateBuildingCommand), "rotate")]
[JsonDerivedType(typeof(SetInserterFilterCommand), "filter")]
[JsonDerivedType(typeof(SetInserterInputCommand), "input")]
[JsonDerivedType(typeof(EndPrepCommand), "endprep")]
[JsonDerivedType(typeof(SurrenderCommand), "surrender")]
[JsonDerivedType(typeof(HoldWavesCommand), "hold")]
[JsonDerivedType(typeof(StartResearchCommand), "research")]
[JsonDerivedType(typeof(CancelResearchCommand), "cancelresearch")]
[JsonDerivedType(typeof(FireSuperWeaponCommand), "fire")]
[JsonDerivedType(typeof(PingCommand), "ping")]
[JsonDerivedType(typeof(DrawCommand), "draw")]
[JsonDerivedType(typeof(ClearDrawingsCommand), "cleardraw")]
public interface ICommand
{
    int PlayerId { get; }

    /// <summary>Applies the command. Returns null on success or a reason it was rejected.</summary>
    string? Apply(World w);
}

/// <summary>Serializes commands for the network (and replays).</summary>
public static class CommandCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true, // System.Numerics.Vector2 exposes X/Y as fields
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ICommand cmd) => JsonSerializer.Serialize(cmd, Options);

    public static ICommand Deserialize(string json) =>
        JsonSerializer.Deserialize<ICommand>(json, Options) ?? throw new InvalidDataException("Bad command");
}

/// <summary>Pays for a construction site and orders the engineer to build it (chained after current orders when queued).</summary>
public sealed record PlaceBuildingCommand(int PlayerId, string DefId, Int2 Origin, Direction Dir, bool Queue = false) : ICommand
{
    public string? Apply(World w) => w.TryPlace(PlayerId, DefId, Origin, Dir, Queue);
}

public sealed record MoveCommand(int PlayerId, Vector2 Target, bool Queue = false) : ICommand
{
    public string? Apply(World w)
    {
        if (!w.Map.IsPassable(Int2.Floor(Target))) return "Can't move there";
        var eng = w.Engineers[PlayerId];
        if (!eng.Alive) return "Engineer is down";
        eng.Order(w, new MoveOrder(Target), Queue);
        return null;
    }
}

/// <summary>Repairs a damaged building, or resumes construction of an unfinished site.</summary>
public sealed record RepairCommand(int PlayerId, Int2 Tile, bool Queue = false) : ICommand
{
    public string? Apply(World w)
    {
        var b = w.BuildingAt(Tile);
        if (b == null) return "Nothing to repair";
        var eng = w.Engineers[PlayerId];
        if (!eng.Alive) return "Engineer is down";
        if (b is ConstructionSite) eng.Order(w, new BuildOrder(b.Id), Queue);
        else if (b.Hp < b.MaxHp) eng.Order(w, new RepairOrder(b.Id), Queue);
        else return $"{b.Def.Name} is undamaged";
        return null;
    }
}

/// <summary>Clears the engineer's orders (unstarted construction sites are refunded).</summary>
public sealed record StopCommand(int PlayerId) : ICommand
{
    public string? Apply(World w)
    {
        w.Engineers[PlayerId].ClearOrders(w);
        return null;
    }
}

/// <summary>Deconstructs a building (or cancels a site) for a full refund, including any items inside.</summary>
public sealed record RemoveBuildingCommand(int PlayerId, Int2 Tile) : ICommand
{
    public string? Apply(World w) => w.TryRemove(PlayerId, Tile);
}

public sealed record RotateBuildingCommand(int PlayerId, Int2 Tile) : ICommand
{
    public string? Apply(World w)
    {
        var b = w.BuildingAt(Tile);
        if (b == null || b.Size != 1 && b is not Drill || b.Def.Kind is BuildingKind.Wall or BuildingKind.Pole) return "Nothing to rotate";
        b.Dir = b.Dir.RotateCw();
        return null;
    }
}

/// <summary>Sets an inserter's filter by hand (null = any item), or with <c>Auto</c> hands it back to automatic choice.</summary>
public sealed record SetInserterFilterCommand(int PlayerId, Int2 Tile, string? Item, bool Auto = false) : ICommand
{
    public string? Apply(World w)
    {
        if (w.BuildingAt(Tile) is not Inserter ins) return "Not an inserter";
        ins.AutoFilter = Auto;
        ins.Filter = Auto ? Inserter.SuggestFilter(w, Inserter.LikelySource(w, ins.Origin, ins.Dir, ins.Input, ins.Reach), w.BuildingAt(ins.Front)) : Item;
        return null;
    }
}

/// <summary>Which side an inserter takes from: Auto (back, then left, then right), Back, Left or Right.</summary>
public sealed record SetInserterInputCommand(int PlayerId, Int2 Tile, InputSide Side) : ICommand
{
    public string? Apply(World w)
    {
        if (w.BuildingAt(Tile) is not Inserter ins) return "Not an inserter";
        ins.Input = Side;
        return null;
    }
}

public sealed record EndPrepCommand(int PlayerId) : ICommand
{
    public string? Apply(World w)
    {
        if (w.Waves.Held) return "Waves are on hold during the tutorial";
        return w.Waves.EndPrepEarly() ? null : "Can't skip while a wave is spawning";
    }
}

/// <summary>Concedes the game (a loss).</summary>
public sealed record SurrenderCommand(int PlayerId) : ICommand
{
    public string? Apply(World w)
    {
        w.Surrender();
        return null;
    }
}

/// <summary>Freezes or resumes the wave countdown (used by the tutorial).</summary>
public sealed record HoldWavesCommand(int PlayerId, bool Hold) : ICommand
{
    public string? Apply(World w)
    {
        w.Waves.Held = Hold;
        return null;
    }
}

public sealed record StartResearchCommand(int PlayerId, string TechId) : ICommand
{
    public string? Apply(World w) => w.Research.Start(w, TechId);
}

public sealed record CancelResearchCommand(int PlayerId) : ICommand
{
    public string? Apply(World w) => w.Research.Cancel(w);
}

/// <summary>Fires a charged Nuclear Silo or Satellite Uplink at a map position.</summary>
public sealed record FireSuperWeaponCommand(int PlayerId, int BuildingId, Vector2 Target) : ICommand
{
    public string? Apply(World w) => w.FireSuperWeapon(PlayerId, BuildingId, Target);
}

/// <summary>A co-op ping (Alt + command wheel). Changes nothing; the view shows it to everyone.</summary>
public sealed record PingCommand(int PlayerId, PingKind Kind, Vector2 Pos) : ICommand
{
    public string? Apply(World w)
    {
        w.Events.Add(new PingEvent(PlayerId, Kind, Pos));
        return null;
    }
}

/// <summary>A freehand line drawn on the map for teammates.</summary>
public sealed record DrawCommand(int PlayerId, Vector2[] Points) : ICommand
{
    public string? Apply(World w)
    {
        if (Points.Length >= 2) w.Events.Add(new DrawEvent(PlayerId, Points));
        return null;
    }
}

public sealed record ClearDrawingsCommand(int PlayerId) : ICommand
{
    public string? Apply(World w)
    {
        w.Events.Add(new ClearDrawingsEvent(PlayerId));
        return null;
    }
}
