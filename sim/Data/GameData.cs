using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeltAndBarricade.Sim;

public sealed class ItemDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#ffffff";
    /// <summary>Ticks of burner operation one unit provides. 0 = not a fuel.</summary>
    public int FuelTicks { get; init; }
}

public sealed class RecipeDef
{
    public string Id { get; init; } = "";
    public Dictionary<string, int> Inputs { get; init; } = new();
    public Dictionary<string, int> Outputs { get; init; } = new();
    public int Ticks { get; init; } = 60;
}

public enum BuildingKind { Belt, Inserter, Drill, Crafter, Wall, Turret, Nexus }

public sealed class BuildingDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public BuildingKind Kind { get; init; }
    public int Hotkey { get; init; }
    public Dictionary<string, int> Cost { get; init; } = new();
    public float MaxHp { get; init; } = 100;
    public bool BlocksEnemies { get; init; }
    public int Size { get; init; } = 1;
    public string Color { get; init; } = "#ffffff";
    /// <summary>Ticks of engineer work to construct.</summary>
    public int BuildTicks { get; init; } = 60;

    // Belt
    public int BeltSpeed { get; init; }
    // Inserter
    public int SwingTicks { get; init; }
    // Drill
    public int MineTicks { get; init; }
    // Drill / Crafter
    public bool UsesFuel { get; init; }
    // Crafter
    public List<string> Recipes { get; init; } = new();
    // Turret / Nexus
    public float Range { get; init; }
    public float Damage { get; init; }
    public int CooldownTicks { get; init; }
    public string? AmmoItem { get; init; }
    public int RoundsPerAmmo { get; init; }
    public int AmmoCapacity { get; init; }
}

public sealed class EnemyDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float MaxHp { get; init; }
    /// <summary>Tiles per second.</summary>
    public float Speed { get; init; }
    public float Damage { get; init; }
    public int AttackTicks { get; init; }
    public float Radius { get; init; } = 0.3f;
    public string Color { get; init; } = "#ff00ff";
}

public sealed class SpawnGroupDef
{
    public string Enemy { get; init; } = "";
    public int Count { get; init; }
    public int IntervalTicks { get; init; } = 60;
}

public sealed class WaveDef
{
    public List<SpawnGroupDef> Groups { get; init; } = new();
}

public sealed class WaveSchedule
{
    public int FirstWaveTicks { get; init; }
    public int BetweenWaveTicks { get; init; }
    public List<WaveDef> Waves { get; init; } = new();
}

public sealed class PointDef
{
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed class PatchDef
{
    public string Item { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public float Radius { get; init; }
    public int Amount { get; init; }
    public bool ClaimZone { get; init; }
}

public sealed class MapDef
{
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int SpawnRows { get; init; }
    public int ChokeTop { get; init; }
    public int ChokeBottom { get; init; }
    public int ChokeHalfWidth { get; init; }
    public float VTopHalfWidth { get; init; }
    public float VBottomHalfWidth { get; init; }
    public PointDef Nexus { get; init; } = new();
    public Dictionary<string, int> StartingItems { get; init; } = new();
    public List<PatchDef> Patches { get; init; } = new();
}

public sealed record Scenario(MapDef Map, WaveSchedule Waves);

/// <summary>All static definitions, loaded from the JSON files in game/data.</summary>
public sealed class GameData
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public required IReadOnlyDictionary<string, ItemDef> Items { get; init; }
    public required IReadOnlyDictionary<string, RecipeDef> Recipes { get; init; }
    public required IReadOnlyDictionary<string, BuildingDef> Buildings { get; init; }
    public required IReadOnlyDictionary<string, EnemyDef> Enemies { get; init; }

    /// <param name="readFile">Returns the text of a file in the data folder, given its file name.</param>
    public static GameData Load(Func<string, string> readFile) => new()
    {
        Items = Parse<List<ItemDef>>(readFile("items.json")).ToDictionary(d => d.Id),
        Recipes = Parse<List<RecipeDef>>(readFile("recipes.json")).ToDictionary(d => d.Id),
        Buildings = Parse<List<BuildingDef>>(readFile("buildings.json")).ToDictionary(d => d.Id),
        Enemies = Parse<List<EnemyDef>>(readFile("enemies.json")).ToDictionary(d => d.Id),
    };

    public static Scenario LoadScenario(Func<string, string> readFile, string mapName) => new(
        Parse<MapDef>(readFile($"map_{mapName}.json")),
        Parse<WaveSchedule>(readFile($"waves_{mapName}.json")));

    public static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidDataException($"Could not parse {typeof(T).Name}");

    public int FuelTicks(string item) => Items.TryGetValue(item, out var d) ? d.FuelTicks : 0;
}
