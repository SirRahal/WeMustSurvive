using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>
/// An alien. All behaviours head for the Nexus along the flow field by default:
/// <list type="bullet">
/// <item>Melee: chews through whatever blocks its path; "distractible" ones also wreck belts/poles they walk over.</item>
/// <item>Ranged (Spitter): stops to spit acid at any player structure in range.</item>
/// <item>Sapper: hunts the nearest logistics or power building, then explodes.</item>
/// </list>
/// </summary>
public sealed class Enemy
{
    private const float EngineerAggroRange = 1.2f;
    private const float AttackReach = 0.2f;
    private const float SapperSearchRange = 14f;
    private const int SapperRetargetTicks = 60;

    public int Id { get; }
    public EnemyDef Def { get; }
    public Vector2 Pos { get; private set; }
    public float Hp { get; set; }
    public float MaxHp { get; }
    public float Damage { get; }
    public bool Dead { get; set; }
    /// <summary>For the view: what this enemy is currently attacking, if anything.</summary>
    public int? AttackingId { get; private set; }
    /// <summary>Small per-enemy offset from tile centers so groups don't collapse into one dot.</summary>
    private readonly Vector2 _jitter;
    private int _attackCooldown;
    private int _retarget;
    private int? _sapTargetId;
    private readonly List<Vector2> _path = new();

    public Enemy(int id, EnemyDef def, Vector2 pos, float statScale, Rng rng)
    {
        Id = id;
        Def = def;
        Pos = pos;
        MaxHp = Hp = def.MaxHp * statScale;
        Damage = def.Damage * statScale;
        _jitter = new Vector2(rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f) * 0.5f;
    }

    public void Tick(World w)
    {
        if (_attackCooldown > 0) _attackCooldown--;
        AttackingId = null;

        switch (Def.Behavior)
        {
            case EnemyBehavior.Sapper:
                SapperTick(w);
                return;
            case EnemyBehavior.Ranged when RangedAttack(w):
                return;
        }

        if (Def.Behavior == EnemyBehavior.Melee && BiteEngineer(w)) return;
        FollowFlow(w);
    }

    // ------------------------------------------------------------------ shared movement

    /// <summary>Walks the flow field toward the Nexus, attacking whatever blocks the way.</summary>
    private void FollowFlow(World w)
    {
        var tile = Int2.Floor(Pos);
        var next = w.Flow.NextStep(tile);
        if (next == null) return;

        var blocker = w.BuildingAt(next.Value);
        if (blocker != null && blocker.BlocksEnemies)
        {
            if (blocker.DistanceTo(Pos) <= Def.Radius + AttackReach)
            {
                if (Def.Behavior == EnemyBehavior.Sapper) Explode(w);
                else Attack(w, blocker);
                return;
            }
            MoveToward(next.Value.Center);
            return;
        }

        // Distractible melee aliens wreck walkable infrastructure (belts, poles, inserters) in their path.
        if (blocker != null && Def.Distractible && Id % 2 == 0 && blocker is not ConstructionSite { Started: false }
            && blocker.DistanceTo(Pos) <= Def.Radius + AttackReach)
        {
            Attack(w, blocker);
            return;
        }

        MoveToward(next.Value.Center + _jitter);
    }

    private void Attack(World w, Building target)
    {
        AttackingId = target.Id;
        if (_attackCooldown > 0) return;
        w.DamageBuilding(target, Damage);
        _attackCooldown = Def.AttackTicks;
    }

    private bool BiteEngineer(World w)
    {
        foreach (var eng in w.Engineers)
        {
            if (!eng.Alive || Vector2.Distance(eng.Pos, Pos) > EngineerAggroRange) continue;
            if (_attackCooldown == 0)
            {
                eng.TakeDamage(w, Damage);
                _attackCooldown = Def.AttackTicks;
            }
            return true;
        }
        return false;
    }

    private void MoveToward(Vector2 target)
    {
        var delta = target - Pos;
        float step = Def.Speed / World.TicksPerSecond;
        float len = delta.Length();
        Pos = len <= step ? target : Pos + delta / len * step;
    }

    // ------------------------------------------------------------------ spitter

    /// <summary>Spits at the nearest structure in range. Returns true if it stayed put to attack.</summary>
    private bool RangedAttack(World w)
    {
        Building? best = null;
        float bestD = Def.Range;
        foreach (var b in w.Buildings)
        {
            if (b.Dead || b is ConstructionSite { Started: false }) continue;
            float d = b.DistanceTo(Pos);
            if (d <= bestD)
            {
                bestD = d;
                best = b;
            }
        }
        if (best == null) return false;

        AttackingId = best.Id;
        if (_attackCooldown == 0)
        {
            w.Events.Add(new ShotEvent(Pos, best.Center, "acid"));
            w.DamageBuilding(best, Damage);
            _attackCooldown = Def.AttackTicks;
        }
        return true;
    }

    // ------------------------------------------------------------------ sapper

    private static bool IsSapperTarget(Building b) => b.Def.Kind is BuildingKind.Belt or BuildingKind.Inserter
        or BuildingKind.Pole or BuildingKind.Generator or BuildingKind.Drill or BuildingKind.Splitter
        or BuildingKind.TunnelEntrance or BuildingKind.TunnelExit or BuildingKind.Depot;

    private void SapperTick(World w)
    {
        var target = _sapTargetId is int id ? w.BuildingById(id) : null;
        if (target == null || --_retarget <= 0)
        {
            _retarget = SapperRetargetTicks;
            target = PickSapperTarget(w);
        }

        if (target == null)
        {
            FollowFlow(w);
            return;
        }

        AttackingId = target.Id;
        if (target.DistanceTo(Pos) <= Def.Radius + AttackReach + 0.3f)
        {
            Explode(w);
            return;
        }

        if (_path.Count == 0)
        {
            FollowFlow(w);
            return;
        }
        MoveToward(_path[0]);
        if (Vector2.Distance(Pos, _path[0]) < 0.05f) _path.RemoveAt(0);
    }

    private Building? PickSapperTarget(World w)
    {
        _sapTargetId = null;
        _path.Clear();
        Building? best = null;
        float bestD = SapperSearchRange;
        foreach (var b in w.Buildings)
        {
            if (b.Dead || !IsSapperTarget(b) || b is ConstructionSite { Started: false }) continue;
            float d = b.DistanceTo(Pos);
            if (d < bestD)
            {
                bestD = d;
                best = b;
            }
        }
        if (best == null) return null;

        var tiles = Pathfinder.Find(w, Int2.Floor(Pos), t => best.DistanceTo(t.Center) <= 1.0f, best.Center);
        if (tiles == null) return null;
        foreach (var t in tiles) _path.Add(t.Center + _jitter * 0.5f);
        _sapTargetId = best.Id;
        return best;
    }

    private void Explode(World w)
    {
        w.DamageBuildingsInRadius(Pos, Def.Range, Damage);
        w.Events.Add(new ExplosionEvent(Pos, Def.Range, "sapper"));
        Dead = true;
    }

    public void HashInto(StateHash h)
    {
        h.Add(Id);
        h.Add(Pos);
        h.Add(Hp);
        h.Add(_attackCooldown);
        h.Add(_sapTargetId ?? -1);
    }
}
