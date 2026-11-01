using System.Numerics;

namespace BeltAndBarricade.Sim;

public sealed class TutorialHint
{
    /// <summary>Building id to place here, or "mark" for a plain marker.</summary>
    public string Building { get; init; } = "mark";
    public int X { get; init; }
    public int Y { get; init; }
    public Direction Dir { get; init; } = Direction.North;

    public Int2 Tile => new(X, Y);
    public bool IsMarker => Building == "mark";
}

public sealed class TutorialStep
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public PointF Focus { get; init; } = new();
    public float Zoom { get; init; } = 1f;
    /// <summary>What completes the step; null means the player clicks Next.</summary>
    public string? Goal { get; init; }
    public int Count { get; init; } = 1;
    public string? Item { get; init; }
    /// <summary>Command-card button to highlight: a building id, or "filter:&lt;item&gt;".</summary>
    public string? Button { get; init; }
    public bool ReleaseWaves { get; init; }
    public List<TutorialHint> Hints { get; init; } = new();
}

public sealed class PointF
{
    public float X { get; init; }
    public float Y { get; init; }
    public Vector2 ToVector() => new(X, Y);
}

public sealed class TutorialDef
{
    public List<TutorialStep> Steps { get; init; } = new();

    public static TutorialDef Load(Func<string, string> readFile, string mapName) =>
        GameData.Parse<TutorialDef>(readFile($"tutorial_{mapName}.json"));
}

/// <summary>
/// Runs a guided tutorial over a <see cref="World"/>: holds the waves, tracks the current step, and advances
/// when the step's goal is met. Only reads sim state; any changes go through commands.
/// </summary>
public sealed class Tutorial
{
    private readonly TutorialDef _def;
    private readonly int _playerId;
    private int _platesAtStepStart;

    public int StepIndex { get; private set; }
    public int StepCount => _def.Steps.Count;
    public bool Finished { get; private set; }
    public TutorialStep Current => _def.Steps[Math.Min(StepIndex, StepCount - 1)];
    /// <summary>True when the current step waits for the player to click Next.</summary>
    public bool NeedsNext => !Finished && Current.Goal == null;
    public bool IsLastStep => StepIndex == StepCount - 1;
    /// <summary>Hints from every step, so building ahead of the tutorial still lines up with the plan.</summary>
    public IEnumerable<TutorialHint> AllHints => _def.Steps.SelectMany(s => s.Hints);

    public Tutorial(TutorialDef def, World w, int playerId = 0)
    {
        _def = def;
        _playerId = playerId;
        w.Enqueue(new HoldWavesCommand(playerId, true));
        Enter(w);
    }

    /// <summary>Call after each sim step; advances when the current goal is met.</summary>
    public void Update(World w)
    {
        if (Finished || Current.Goal == null) return;
        if (IsGoalMet(w, Current)) Advance(w);
    }

    /// <summary>The player clicked Next on an informational step.</summary>
    public void Next(World w)
    {
        if (NeedsNext) Advance(w);
    }

    public void Skip(World w) => Finish(w);

    /// <summary>Whether a hint has been satisfied (a site or building of the right type and facing is there).</summary>
    public static bool IsHintDone(World w, TutorialHint hint)
    {
        if (hint.IsMarker) return false;
        var b = w.BuildingAt(hint.Tile);
        if (b == null || b.Def.Id != hint.Building || b.Origin != hint.Tile) return false;
        return !IsDirectional(b.Def) || b.Dir == hint.Dir;
    }

    public static bool IsDirectional(BuildingDef def) =>
        def.Kind is BuildingKind.Belt or BuildingKind.Inserter or BuildingKind.Drill;

    private void Advance(World w)
    {
        if (IsLastStep)
        {
            Finish(w);
            return;
        }
        StepIndex++;
        Enter(w);
    }

    private void Enter(World w)
    {
        _platesAtStepStart = w.Nexus.Received.Count("iron_plate");
        if (Current.ReleaseWaves) w.Enqueue(new HoldWavesCommand(_playerId, false));
    }

    private void Finish(World w)
    {
        if (Finished) return;
        Finished = true;
        w.Enqueue(new HoldWavesCommand(_playerId, false));
    }

    private bool IsGoalMet(World w, TutorialStep step) => step.Goal switch
    {
        "engineerAt" => step.Hints.Count > 0 &&
            Vector2.Distance(w.Engineers[_playerId].Pos, step.Hints[0].Tile.Center) <= 1.0f,
        // Every hinted building exists, finished, facing the right way.
        "hintsBuilt" => step.Hints.All(h => IsHintDone(w, h) && w.BuildingAt(h.Tile) is not ConstructionSite),
        "furnaceSmelting" => w.Buildings.OfType<Crafter>().Any(c => c.Def.Id == "furnace" && (c.Active != null || c.Output.Total > 0)),
        "platesDelivered" => w.Nexus.Received.Count("iron_plate") >= _platesAtStepStart + step.Count,
        "nexusFilter" => w.Buildings.OfType<Inserter>().Any(i => i.Filter == step.Item && w.BuildingAt(i.Back) is Nexus),
        "turretArmed" => w.Buildings.OfType<Turret>().Any(t => t.Ammo > 0 || t.Rounds > 0),
        "waveStarted" => w.Waves.WavesStarted >= 1,
        _ => false,
    };
}
