using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>
/// Every player action is a command. Commands are the only way player intent changes the sim,
/// so in co-op the host can apply commands received from clients exactly as it applies local ones.
/// Move/repair orders with <c>Queue</c> set are appended to the engineer's queue (Shift-click); without it they
/// interrupt and run first. Blueprints always join the back of the queue.
/// </summary>
public interface ICommand
{
    int PlayerId { get; }

    /// <summary>Applies the command. Returns null on success or a reason it was rejected.</summary>
    string? Apply(World w);
}

/// <summary>Pays for a construction site and adds it to the engineer's build queue.</summary>
public sealed record PlaceBuildingCommand(int PlayerId, string DefId, Int2 Origin, Direction Dir) : ICommand
{
    public string? Apply(World w) => w.TryPlace(PlayerId, DefId, Origin, Dir);
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
        else if (b.Hp < b.Def.MaxHp) eng.Order(w, new RepairOrder(b.Id), Queue);
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
        if (b == null || b.Size != 1 || b.Def.Kind == BuildingKind.Wall) return "Nothing to rotate";
        b.Dir = b.Dir.RotateCw();
        return null;
    }
}

public sealed record SetInserterFilterCommand(int PlayerId, Int2 Tile, string? Item) : ICommand
{
    public string? Apply(World w)
    {
        if (w.BuildingAt(Tile) is not Inserter ins) return "Not an inserter";
        ins.Filter = Item;
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
