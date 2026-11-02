namespace BeltAndBarricade.Sim;

/// <summary>
/// A building that has been ordered (and paid for) but not finished. <see cref="Building.Def"/> is the
/// building it will become. It only blocks enemies once an engineer has started work, and it gains HP
/// as construction progresses, like a StarCraft structure going up.
/// </summary>
public sealed class ConstructionSite : Building
{
    public const float StartHpFraction = 0.1f;

    public float Progress { get; private set; }
    public bool Started => Progress > 0;
    public int BuildTicks => Math.Max(1, Def.BuildTicks);
    public float Fraction => Math.Min(1f, Progress / BuildTicks);

    public override bool BlocksEnemies => Started && Def.BlocksEnemies;
    protected override bool IsBusy => false;

    /// <summary>Advances construction by <paramref name="rate"/> ticks of work. Returns true when complete.</summary>
    public bool Advance(float rate = 1f)
    {
        Progress += rate;
        Hp = MathF.Min(MaxHp, Hp + MaxHp * (1 - StartHpFraction) * rate / BuildTicks);
        return Progress >= BuildTicks;
    }

    public override string Status => Started ? $"Under construction {Fraction:P0}" : "Waiting for engineer";

    public override void HashInto(StateHash h) => h.Add(Progress);
}
