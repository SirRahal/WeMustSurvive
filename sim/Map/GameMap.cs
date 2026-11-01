namespace BeltAndBarricade.Sim;

public enum Terrain : byte
{
    Ground,
    /// <summary>Impassable, unbuildable (the funnel's arms).</summary>
    Blocked,
    /// <summary>Walkable but unbuildable (enemy spawn area).</summary>
    NoBuild,
}

public sealed class GameMap
{
    private readonly Terrain[] _terrain;
    private readonly string?[] _ore;
    private readonly int[] _oreAmount;
    private readonly bool[] _claim;

    public int Width { get; }
    public int Height { get; }
    public List<Int2> SpawnTiles { get; } = new();

    public GameMap(int width, int height)
    {
        Width = width;
        Height = height;
        _terrain = new Terrain[width * height];
        _ore = new string?[width * height];
        _oreAmount = new int[width * height];
        _claim = new bool[width * height];
    }

    public bool InBounds(Int2 t) => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height;
    public int Index(Int2 t) => t.Y * Width + t.X;

    public Terrain TerrainAt(Int2 t) => InBounds(t) ? _terrain[Index(t)] : Terrain.Blocked;
    public void SetTerrain(Int2 t, Terrain v) => _terrain[Index(t)] = v;

    public bool IsBuildable(Int2 t) => TerrainAt(t) == Terrain.Ground;
    public bool IsPassable(Int2 t) => TerrainAt(t) != Terrain.Blocked;

    public string? OreAt(Int2 t) => InBounds(t) && _oreAmount[Index(t)] > 0 ? _ore[Index(t)] : null;
    public int OreAmount(Int2 t) => InBounds(t) ? _oreAmount[Index(t)] : 0;
    public bool IsClaimZone(Int2 t) => InBounds(t) && _claim[Index(t)];

    public void SetOre(Int2 t, string item, int amount, bool claimZone)
    {
        int i = Index(t);
        _ore[i] = item;
        _oreAmount[i] = amount;
        _claim[i] = claimZone;
    }

    /// <summary>Removes one unit of ore; returns the item mined or null if empty.</summary>
    public string? Mine(Int2 t)
    {
        string? ore = OreAt(t);
        if (ore != null) _oreAmount[Index(t)]--;
        return ore;
    }

    public void HashInto(StateHash h)
    {
        foreach (int a in _oreAmount) h.Add(a);
    }
}

/// <summary>Builds the V-shaped funnel map from MapDef parameters.</summary>
public static class MapGenerator
{
    public static GameMap Generate(MapDef def)
    {
        var map = new GameMap(def.Width, def.Height);
        float cx = def.Width / 2f;

        for (int y = 0; y < def.Height; y++)
        for (int x = 0; x < def.Width; x++)
        {
            var t = new Int2(x, y);
            float dx = MathF.Abs(x + 0.5f - cx);
            Terrain terrain;

            if (x == 0 || y == 0 || x == def.Width - 1 || y == def.Height - 1)
                terrain = Terrain.Blocked;
            else if (y < def.SpawnRows)
                terrain = dx <= def.ChokeHalfWidth + 6 ? Terrain.NoBuild : Terrain.Blocked;
            else if (y <= def.ChokeBottom)
                terrain = dx <= def.ChokeHalfWidth ? Terrain.Ground : Terrain.Blocked;
            else
            {
                float k = (y - def.ChokeBottom) / (float)(def.Height - 1 - def.ChokeBottom);
                float half = def.VTopHalfWidth + (def.VBottomHalfWidth - def.VTopHalfWidth) * k;
                terrain = dx <= half ? Terrain.Ground : Terrain.Blocked;
            }

            map.SetTerrain(t, terrain);
            if (terrain == Terrain.NoBuild && y >= 1 && y < def.SpawnRows - 1 && dx <= def.ChokeHalfWidth + 3)
                map.SpawnTiles.Add(t);
        }

        foreach (var p in def.Patches)
        {
            int r = (int)MathF.Ceiling(p.Radius);
            for (int y = p.Y - r; y <= p.Y + r; y++)
            for (int x = p.X - r; x <= p.X + r; x++)
            {
                var t = new Int2(x, y);
                float d = MathF.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
                if (d <= p.Radius && map.IsBuildable(t))
                    map.SetOre(t, p.Item, p.Amount, p.ClaimZone);
            }
        }

        return map;
    }
}
