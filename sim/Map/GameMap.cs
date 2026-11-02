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

/// <summary>Builds a map from its MapDef: either the parametric V-shaped funnel or a list of painted shapes.</summary>
public static class MapGenerator
{
    public static GameMap Generate(MapDef def)
    {
        var map = def.Layout == "shapes" ? GenerateShapes(def) : GenerateFunnel(def);
        PlacePatches(map, def);
        return map;
    }

    private static GameMap GenerateShapes(MapDef def)
    {
        var map = new GameMap(def.Width, def.Height);
        for (int y = 0; y < def.Height; y++)
        for (int x = 0; x < def.Width; x++)
            map.SetTerrain(new Int2(x, y), Terrain.Blocked);

        foreach (var s in def.Shapes) Paint(map, s, s.Terrain);
        foreach (var z in def.SpawnZones)
        {
            Paint(map, z, Terrain.NoBuild);
            ForEachTile(map, z, t => map.SpawnTiles.Add(t));
        }

        // Keep a solid border so nothing walks off the map.
        for (int x = 0; x < def.Width; x++)
        {
            map.SetTerrain(new Int2(x, 0), Terrain.Blocked);
            map.SetTerrain(new Int2(x, def.Height - 1), Terrain.Blocked);
        }
        for (int y = 0; y < def.Height; y++)
        {
            map.SetTerrain(new Int2(0, y), Terrain.Blocked);
            map.SetTerrain(new Int2(def.Width - 1, y), Terrain.Blocked);
        }
        map.SpawnTiles.RemoveAll(t => !map.IsPassable(t));
        return map;
    }

    private static void Paint(GameMap map, ShapeDef s, Terrain terrain) => ForEachTile(map, s, t => map.SetTerrain(t, terrain));

    private static void ForEachTile(GameMap map, ShapeDef s, Action<Int2> action)
    {
        if (s.Type == "line")
        {
            var a = new System.Numerics.Vector2(s.X, s.Y);
            var b = new System.Numerics.Vector2(s.X2, s.Y2);
            int r = (int)MathF.Ceiling(s.R);
            for (int y = Math.Min(s.Y, s.Y2) - r; y <= Math.Max(s.Y, s.Y2) + r; y++)
            for (int x = Math.Min(s.X, s.X2) - r; x <= Math.Max(s.X, s.X2) + r; x++)
            {
                var t = new Int2(x, y);
                var p = new System.Numerics.Vector2(x, y);
                var ab = b - a;
                float k = ab.LengthSquared() == 0 ? 0 : Math.Clamp(System.Numerics.Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
                if (map.InBounds(t) && System.Numerics.Vector2.Distance(p, a + ab * k) <= s.R) action(t);
            }
            return;
        }
        if (s.Type == "circle")
        {
            int r = (int)MathF.Ceiling(s.R);
            for (int y = s.Y - r; y <= s.Y + r; y++)
            for (int x = s.X - r; x <= s.X + r; x++)
            {
                var t = new Int2(x, y);
                if (map.InBounds(t) && (x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) <= s.R * s.R) action(t);
            }
            return;
        }
        for (int y = s.Y; y < s.Y + s.H; y++)
        for (int x = s.X; x < s.X + s.W; x++)
        {
            var t = new Int2(x, y);
            if (map.InBounds(t)) action(t);
        }
    }

    private static GameMap GenerateFunnel(MapDef def)
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
        return map;
    }

    private static void PlacePatches(GameMap map, MapDef def)
    {
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
    }
}
