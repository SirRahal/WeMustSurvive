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
    /// <summary>Pause this recipe while the storage network already holds this many of its output (0 = no limit).</summary>
    public int StockLimit { get; init; }
}

public enum BuildingKind
{
    Belt, Inserter, Drill, Crafter, Wall, Turret, Nexus,
    Generator, Pole, Lab, Splitter, TunnelEntrance, TunnelExit, Depot, SuperWeapon,
}

public sealed class BuildingDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public BuildingKind Kind { get; init; }
    /// <summary>Command-card category: logistics, production, power, defense, special. Empty = not player-buildable.</summary>
    public string Category { get; init; } = "";
    /// <summary>Build-menu group: upgrade tiers name their base building here and share its slot. Empty = its own id.</summary>
    public string Family { get; init; } = "";
    /// <summary>Research that unlocks this building; null = available from the start.</summary>
    public string? Tech { get; init; }
    public Dictionary<string, int> Cost { get; init; } = new();
    public float MaxHp { get; init; } = 100;
    public bool BlocksEnemies { get; init; }
    public int Size { get; init; } = 1;
    public string Color { get; init; } = "#ffffff";
    /// <summary>Ticks of engineer work to construct.</summary>
    public int BuildTicks { get; init; } = 60;

    // Power (kW). Consumers draw PowerDraw while busy and IdleDraw otherwise.
    public float PowerDraw { get; init; }
    public float IdleDraw { get; init; }
    public float PowerOutput { get; init; }
    /// <summary>Poles / Nexus: buildings whose footprint is within this many tiles are on the grid.</summary>
    public float SupplyRadius { get; init; }
    /// <summary>Poles / Nexus: connect to other poles within this distance.</summary>
    public float WireReach { get; init; }

    // Belt
    public int BeltSpeed { get; init; }
    // Inserter
    public int SwingTicks { get; init; }
    /// <summary>Inserters: tiles reached behind it, over belts in between (1 = only the neighbour).</summary>
    public int Reach { get; init; } = 1;
    // Drill
    public int MineTicks { get; init; }
    // Drill / Crafter / Generator
    public bool UsesFuel { get; init; }
    // Crafter
    public List<string> Recipes { get; init; } = new();
    /// <summary>Crafting speed multiplier (electric furnaces are faster).</summary>
    public float CraftSpeed { get; init; } = 1f;
    // Turret / Nexus
    public float Range { get; init; }
    public float Damage { get; init; }
    public int CooldownTicks { get; init; }
    public string? AmmoItem { get; init; }
    public int RoundsPerAmmo { get; init; }
    public int AmmoCapacity { get; init; }
    /// <summary>Area damage radius around the target (0 = single target).</summary>
    public float Splash { get; init; }
    /// <summary>Visual style of shots: bullet, laser, rocket, flame, heavy.</summary>
    public string ShotKind { get; init; } = "bullet";
    // Tunnel
    public int TunnelReach { get; init; }
    // Lab
    public float ResearchRate { get; init; }
    // Depot
    public int Capacity { get; init; }
    // Super weapon
    public string AbilityName { get; init; } = "";
    public int ChargeTicks { get; init; }
    public float StrikeRadius { get; init; }
    public float StrikeDamage { get; init; }
    public int StrikeDelayTicks { get; init; }
}

public enum EnemyBehavior
{
    /// <summary>Walks the flow field to the Nexus, chewing through whatever blocks it.</summary>
    Melee,
    /// <summary>Stops to spit acid at any structure in range.</summary>
    Ranged,
    /// <summary>Hunts logistics and power infrastructure, then explodes.</summary>
    Sapper,
}

public sealed class EnemyDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public EnemyBehavior Behavior { get; init; }
    public float MaxHp { get; init; }
    /// <summary>Tiles per second.</summary>
    public float Speed { get; init; }
    public float Damage { get; init; }
    public int AttackTicks { get; init; }
    public float Radius { get; init; } = 0.3f;
    /// <summary>Ranged: attack range. Sapper: explosion radius.</summary>
    public float Range { get; init; }
    /// <summary>Melee: also attacks belts/poles/etc. that it walks across.</summary>
    public bool Distractible { get; init; }
    public string Color { get; init; } = "#ff00ff";
}

public sealed class TechDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>0 coal, 1 iron, 2 copper, 3 silver, 4 gold.</summary>
    public int Tier { get; init; }
    public Dictionary<string, int> Cost { get; init; } = new();
    /// <summary>Lab-ticks of work (one basic lab contributes 1 per tick).</summary>
    public int Ticks { get; init; } = 1800;
    public List<string> Requires { get; init; } = new();
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

/// <summary>A terrain stamp for "shapes" layouts: a rectangle, circle, or thick line painted with one terrain type.</summary>
public sealed class ShapeDef
{
    /// <summary>"rect" (X,Y,W,H), "circle" (X,Y,R) or "line" (X,Y to X2,Y2 with half-width R).</summary>
    public string Type { get; init; } = "rect";
    public Terrain Terrain { get; init; } = Terrain.Ground;
    public int X { get; init; }
    public int Y { get; init; }
    public int X2 { get; init; }
    public int Y2 { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public float R { get; init; }
}

public sealed class MapDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>"funnel" (parametric V) or "shapes" (painted from <see cref="Shapes"/>).</summary>
    public string Layout { get; init; } = "funnel";
    public int Minutes { get; init; }
    /// <summary>Meta points awarded for winning.</summary>
    public int MetaPoints { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    // Funnel layout parameters
    public int SpawnRows { get; init; }
    public int ChokeTop { get; init; }
    public int ChokeBottom { get; init; }
    public int ChokeHalfWidth { get; init; }
    public float VTopHalfWidth { get; init; }
    public float VBottomHalfWidth { get; init; }
    // Shapes layout parameters (applied in order over an all-blocked map)
    public List<ShapeDef> Shapes { get; init; } = new();
    public List<ShapeDef> SpawnZones { get; init; } = new();
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

    /// <summary>Map ids in menu order.</summary>
    public static readonly string[] MapIds = { "funnel", "crossroads" };

    public required IReadOnlyDictionary<string, ItemDef> Items { get; init; }
    public required IReadOnlyDictionary<string, RecipeDef> Recipes { get; init; }
    public required IReadOnlyDictionary<string, BuildingDef> Buildings { get; init; }
    /// <summary>Buildings in file order (the order they appear on the command card).</summary>
    public required IReadOnlyList<BuildingDef> BuildingList { get; init; }
    public required IReadOnlyDictionary<string, EnemyDef> Enemies { get; init; }
    public required IReadOnlyDictionary<string, TechDef> Techs { get; init; }
    public required IReadOnlyList<TechDef> TechList { get; init; }

    /// <param name="readFile">Returns the text of a file in the data folder, given its file name.</param>
    public static GameData Load(Func<string, string> readFile)
    {
        var buildings = Parse<List<BuildingDef>>(readFile("buildings.json"));
        var techs = Parse<List<TechDef>>(readFile("techs.json"));
        return new GameData
        {
            Items = Parse<List<ItemDef>>(readFile("items.json")).ToDictionary(d => d.Id),
            Recipes = Parse<List<RecipeDef>>(readFile("recipes.json")).ToDictionary(d => d.Id),
            Buildings = buildings.ToDictionary(d => d.Id),
            BuildingList = buildings,
            Enemies = Parse<List<EnemyDef>>(readFile("enemies.json")).ToDictionary(d => d.Id),
            Techs = techs.ToDictionary(t => t.Id),
            TechList = techs,
        };
    }

    public static Scenario LoadScenario(Func<string, string> readFile, string mapName) => new(
        Parse<MapDef>(readFile($"map_{mapName}.json")),
        Parse<WaveSchedule>(readFile($"waves_{mapName}.json")));

    public static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json.TrimStart('﻿'), JsonOptions) ?? throw new InvalidDataException($"Could not parse {typeof(T).Name}");

    public int FuelTicks(string item) => Items.TryGetValue(item, out var d) ? d.FuelTicks : 0;

    /// <summary>Buildings players can place, in a command-card category.</summary>
    public IEnumerable<BuildingDef> InCategory(string category) => BuildingList.Where(b => b.Category == category);

    /// <summary>Build-menu slots for a category: one list per family, members in data order (lowest tier first).</summary>
    public List<List<BuildingDef>> FamiliesIn(string category) =>
        InCategory(category).GroupBy(b => b.Family == "" ? b.Id : b.Family).Select(g => g.ToList()).ToList();
}
