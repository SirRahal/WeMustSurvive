using System.Numerics;

namespace BeltAndBarricade.Sim;

public sealed class Enemy
{
    private const float EngineerAggroRange = 1.2f;
    private const float AttackReach = 0.2f;

    public int Id { get; }
    public EnemyDef Def { get; }
    public Vector2 Pos { get; private set; }
    public float Hp { get; set; }
    public float MaxHp { get; }
    public float Damage { get; }
    public bool Dead { get; set; }
    /// <summary>Small per-enemy offset from tile centers so groups don't collapse into one dot.</summary>
    private readonly Vector2 _jitter;
    private int _attackCooldown;

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

        // Engineers who wander too close get bitten.
        foreach (var eng in w.Engineers)
        {
            if (!eng.Alive || Vector2.Distance(eng.Pos, Pos) > EngineerAggroRange) continue;
            if (_attackCooldown == 0)
            {
                eng.TakeDamage(w, Damage);
                _attackCooldown = Def.AttackTicks;
            }
            return;
        }

        var tile = Int2.Floor(Pos);
        var next = w.Flow.NextStep(tile);
        if (next == null) return;

        var blocker = w.BuildingAt(next.Value);
        if (blocker != null && blocker.BlocksEnemies)
        {
            if (DistanceToTile(next.Value) <= Def.Radius + AttackReach)
            {
                if (_attackCooldown == 0)
                {
                    w.DamageBuilding(blocker, Damage);
                    _attackCooldown = Def.AttackTicks;
                }
                return;
            }
            MoveToward(next.Value.Center);
            return;
        }

        MoveToward(next.Value.Center + _jitter);
    }

    private void MoveToward(Vector2 target)
    {
        var delta = target - Pos;
        float step = Def.Speed / World.TicksPerSecond;
        float len = delta.Length();
        Pos = len <= step ? target : Pos + delta / len * step;
    }

    private float DistanceToTile(Int2 t)
    {
        float cx = Math.Clamp(Pos.X, t.X, t.X + 1);
        float cy = Math.Clamp(Pos.Y, t.Y, t.Y + 1);
        return Vector2.Distance(Pos, new Vector2(cx, cy));
    }

    public void HashInto(StateHash h)
    {
        h.Add(Id);
        h.Add(Pos);
        h.Add(Hp);
        h.Add(_attackCooldown);
    }
}
