namespace BeltAndBarricade.Sim;

/// <summary>Stat names that meta-progression (the skill tree) can modify.</summary>
public static class Stat
{
    public const string MiningSpeed = "mining_speed";
    public const string ClaimYield = "claim_yield";
    public const string BeltSpeed = "belt_speed";
    public const string InserterSpeed = "inserter_speed";
    public const string CraftingSpeed = "crafting_speed";
    public const string PowerOutput = "power_output";
    /// <summary>Negative values reduce consumption.</summary>
    public const string PowerUse = "power_use";
    public const string TurretDamage = "turret_damage";
    public const string TurretRange = "turret_range";
    public const string WallHp = "wall_hp";
    public const string ResearchSpeed = "research_speed";
    /// <summary>Negative values shorten super-weapon charge time.</summary>
    public const string SuperWeaponCharge = "superweapon_charge";
    public const string SuperWeaponDamage = "superweapon_damage";
    public const string EngineerSpeed = "engineer_speed";
    public const string BuildSpeed = "build_speed";
    public const string RepairSpeed = "repair_speed";
    /// <summary>Flat: extra iron plates at the start of a map.</summary>
    public const string StartingIron = "starting_iron";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [MiningSpeed] = "mining speed",
        [ClaimYield] = "claim zone yield",
        [BeltSpeed] = "belt speed",
        [InserterSpeed] = "inserter speed",
        [CraftingSpeed] = "smelting & crafting speed",
        [PowerOutput] = "power output",
        [PowerUse] = "power consumption",
        [TurretDamage] = "turret damage",
        [TurretRange] = "turret range",
        [WallHp] = "wall health",
        [ResearchSpeed] = "research speed",
        [SuperWeaponCharge] = "super weapon charge time",
        [SuperWeaponDamage] = "super weapon damage",
        [EngineerSpeed] = "engineer move speed",
        [BuildSpeed] = "construction speed",
        [RepairSpeed] = "repair speed",
        [StartingIron] = "starting iron plates",
    };
}

/// <summary>
/// Additive stat bonuses (e.g. mining_speed +0.05 = +5%). <see cref="Mult"/> turns them into multipliers.
/// Immutable once a world is created, so both co-op peers simulate with identical values.
/// </summary>
public sealed class Modifiers
{
    private readonly SortedDictionary<string, float> _bonus = new(StringComparer.Ordinal);

    public static Modifiers None => new();

    public IReadOnlyDictionary<string, float> Bonuses => _bonus;

    public Modifiers Add(string stat, float value)
    {
        _bonus[stat] = Raw(stat) + value;
        return this;
    }

    /// <summary>Sum of bonuses for a stat (0 if none).</summary>
    public float Raw(string stat) => _bonus.TryGetValue(stat, out float v) ? v : 0f;

    /// <summary>1 + bonus, clamped so a stat can never drop below 20%.</summary>
    public float Mult(string stat) => MathF.Max(0.2f, 1f + Raw(stat));

    public void HashInto(StateHash h)
    {
        foreach (var (k, v) in _bonus)
        {
            h.Add(k);
            h.Add(v);
        }
    }
}
