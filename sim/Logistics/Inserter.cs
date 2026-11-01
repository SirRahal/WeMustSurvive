namespace BeltAndBarricade.Sim;

/// <summary>
/// Picks an item from the tile behind it and drops it on the tile in front. Only picks items the target can
/// currently accept, so it never gets stuck holding something unusable.
/// Pulling from the Nexus requires a filter, otherwise it would empty storage onto the ground.
/// </summary>
public sealed class Inserter : Building
{
    public enum Phase { Idle, Swinging, Returning }

    public Phase State { get; private set; }
    public int Progress { get; private set; }
    public string? Held { get; private set; }
    public string? Filter { get; set; }

    public override void Tick(World w)
    {
        var target = w.BuildingAt(Front);
        switch (State)
        {
            case Phase.Idle:
                if (target == null) return;
                var source = w.BuildingAt(Back);
                if (source is Nexus && Filter == null) return;
                if (source is IItemSource src && src.TryTake(it => Wants(target, it), out string item))
                {
                    Held = item;
                    State = Phase.Swinging;
                    Progress = 0;
                }
                break;

            case Phase.Swinging:
                if (Progress < Def.SwingTicks) { Progress++; return; }
                if (target != null && Drop(target, Held!))
                {
                    Held = null;
                    State = Phase.Returning;
                    Progress = 0;
                }
                break;

            case Phase.Returning:
                if (++Progress >= Def.SwingTicks) State = Phase.Idle;
                break;
        }
    }

    private bool Wants(Building target, string item)
    {
        if (Filter != null && item != Filter) return false;
        return target switch
        {
            Belt b => b.CanInsertAt(Belt.DropPosition),
            IItemAcceptor a => a.CanAccept(item),
            _ => false,
        };
    }

    private static bool Drop(Building target, string item) => target switch
    {
        Belt b => b.TryInsertAt(item, Belt.DropPosition),
        IItemAcceptor a => a.TryInsert(item),
        _ => false,
    };

    public override void CollectContents(Inventory into)
    {
        if (Held != null) into.Add(Held);
        Held = null;
    }

    public override string Status => Filter == null ? "No filter" : $"Filter: {Filter}";

    public override void HashInto(StateHash h)
    {
        h.Add((int)State);
        h.Add(Progress);
        h.Add(Held);
        h.Add(Filter);
    }
}
