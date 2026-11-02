namespace BeltAndBarricade.Sim;

/// <summary>
/// Single-lane belt tile. Items carry an integer position in [0, <see cref="Length"/>) and keep at least
/// <see cref="Spacing"/> apart (4 per tile). An item that would advance past the end is handed to the next tile,
/// carrying its overflow. Integer positions keep belt motion exact and deterministic.
/// </summary>
public sealed class Belt : Building, IItemSource
{
    public const int Length = 256;
    public const int Spacing = 64;
    public const int DropPosition = Length / 2 - 1;

    public struct BeltItem
    {
        public string Item;
        public int Pos;
        /// <summary>Tick on which this item already advanced (via hand-off), so it isn't moved twice.</summary>
        public long MovedTick;
    }

    /// <summary>Ordered front (highest Pos) to back.</summary>
    public List<BeltItem> Items { get; } = new();

    public bool CanInsertAt(int pos)
    {
        if (pos < 0 || pos >= Length) return false;
        foreach (var it in Items)
            if (Math.Abs(it.Pos - pos) < Spacing) return false;
        return true;
    }

    public bool TryInsertAt(string item, int pos, long movedTick = -1)
    {
        if (!CanInsertAt(pos)) return false;
        int i = 0;
        while (i < Items.Count && Items[i].Pos > pos) i++;
        Items.Insert(i, new BeltItem { Item = item, Pos = pos, MovedTick = movedTick });
        return true;
    }

    // Fractional speed from belt_speed bonuses, carried between ticks so +4% actually adds up.
    private float _speedCarry;

    public override void Tick(World w)
    {
        if (Items.Count == 0) return;

        float exact = Def.BeltSpeed * w.Mods.Mult(Stat.BeltSpeed) + _speedCarry;
        int speed = (int)exact;
        _speedCarry = exact - speed;

        // Hand the front item to whatever we face once it would run off the end.
        int overflow = Items[0].Pos + speed - Length;
        if (overflow >= 0 && TryHandOff(w, Items[0].Item, overflow))
            Items.RemoveAt(0);

        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            if (it.MovedTick == w.Tick) continue;
            int limit = i == 0 ? Length - 1 : Items[i - 1].Pos - Spacing;
            it.Pos = Math.Max(it.Pos, Math.Min(it.Pos + speed, limit));
            Items[i] = it;
        }
    }

    private bool TryHandOff(World w, string item, int entryPos) => w.BuildingAt(Front) switch
    {
        Belt next when next.Dir != Dir.Opposite() => next.TryInsertAt(item, entryPos, w.Tick),
        IBeltInput target => target.TryInsert(item),
        _ => false,
    };

    public bool TryTake(Func<string, bool> want, out string item)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (!want(Items[i].Item)) continue;
            item = Items[i].Item;
            Items.RemoveAt(i);
            return true;
        }
        item = "";
        return false;
    }

    public override void CollectContents(Inventory into)
    {
        foreach (var it in Items) into.Add(it.Item);
        Items.Clear();
    }

    public override void HashInto(StateHash h)
    {
        foreach (var it in Items)
        {
            h.Add(it.Item);
            h.Add(it.Pos);
        }
    }
}
