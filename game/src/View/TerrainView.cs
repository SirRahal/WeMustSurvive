using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.View;

/// <summary>Draws the ground, impassable rock, ore, and claim zones. Redrawn occasionally as ore depletes.</summary>
public partial class TerrainView : Node2D
{
    private static readonly Color Ground = new(0.24f, 0.23f, 0.20f);
    private static readonly Color GroundAlt = new(0.26f, 0.25f, 0.22f);
    private static readonly Color Rock = new(0.11f, 0.10f, 0.10f);
    private static readonly Color SpawnGround = new(0.30f, 0.16f, 0.20f);
    private static readonly Color ClaimOutline = new(1f, 0.82f, 0.3f, 0.8f);

    private World? _world;
    private double _redrawTimer;

    public void Bind(World world)
    {
        _world = world;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _redrawTimer -= delta;
        if (_redrawTimer > 0) return;
        _redrawTimer = 2.0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null) return;
        var map = _world.Map;

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var t = new Int2(x, y);
            var terrain = map.TerrainAt(t);
            if (terrain == Terrain.Blocked)
            {
                // Only draw rock that borders playable space; the rest is void.
                if (!TouchesOpen(map, t)) continue;
                Iso.DrawBox(this, x, y, 1, 14, Rock, 0);
                continue;
            }

            var color = terrain == Terrain.NoBuild ? SpawnGround : ((x + y) % 2 == 0 ? Ground : GroundAlt);
            DrawColoredPolygon(Iso.Diamond(x, y, 1), color);

            string? ore = map.OreAt(t);
            if (ore != null)
            {
                var oreColor = Iso.ParseColor(_world.Data.Items[ore].Color);
                // Richer tiles show more ore dots.
                int dots = 1 + Math.Min(3, map.OreAmount(t) / 150);
                for (int i = 0; i < dots; i++)
                {
                    float ox = 0.3f + 0.4f * ((i * 37 + x * 13 + y * 7) % 10) / 10f;
                    float oy = 0.3f + 0.4f * ((i * 53 + x * 3 + y * 17) % 10) / 10f;
                    DrawCircle(Iso.ToScreen(x + ox, y + oy), 4f, oreColor);
                }
            }

            if (map.IsClaimZone(t))
            {
                var d = Iso.Diamond(x, y, 1, 0, 0.04f);
                DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, ClaimOutline, 1.5f);
            }
        }
    }

    private static bool TouchesOpen(GameMap map, Int2 t)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (map.IsPassable(new Int2(t.X + dx, t.Y + dy))) return true;
        return false;
    }
}
