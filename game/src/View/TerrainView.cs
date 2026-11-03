using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.View;

/// <summary>
/// Draws the ground, impassable rock, ore, and claim zones. Redrawn occasionally as ore depletes, and every frame
/// while the view is turning.
/// </summary>
public partial class TerrainView : Node2D
{
    private static readonly Color Ground = new(0.24f, 0.23f, 0.20f);
    private static readonly Color GroundAlt = new(0.26f, 0.25f, 0.22f);
    private static readonly Color Rock = new(0.11f, 0.10f, 0.10f);
    private static readonly Color SpawnGround = new(0.30f, 0.16f, 0.20f);
    private static readonly Color ClaimOutline = new(1f, 0.82f, 0.3f, 0.8f);
    /// <summary>Ground sprites in the enemy spawn area (no building) are tinted toward <see cref="SpawnGround"/>.</summary>
    private static readonly Color SpawnTint = new(1f, 0.62f, 0.68f);

    private World? _world;
    private double _redrawTimer;
    private float _drawnAngle;

    public void Bind(World world)
    {
        _world = world;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Iso.Angle != _drawnAngle)
        {
            QueueRedraw();
            return;
        }
        _redrawTimer -= delta;
        if (_redrawTimer > 0) return;
        _redrawTimer = 2.0;
        QueueRedraw();
    }

    public override void _Ready() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

    public override void _Draw()
    {
        if (_world == null) return;
        var map = _world.Map;
        _drawnAngle = Iso.Angle;
        var rocks = new List<Int2>();
        // Prop Studio ground tiles line up only at whole quarter turns; while the view swings, flat diamonds.
        bool sprites = PropSprites.Shared.Has("ground", "ground") && IsQuarterTurn(Iso.Angle);
        var tiles = new List<Int2>();

        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var t = new Int2(x, y);
            if (map.TerrainAt(t) != Terrain.Blocked) tiles.Add(t);
            // Only draw rock that borders playable space; the rest is void. Drawn last, back to front.
            else if (TouchesOpen(map, t)) rocks.Add(t);
        }
        if (sprites)
            tiles.Sort((a, b) => Iso.Depth(a.X + 0.5f, a.Y + 0.5f).CompareTo(Iso.Depth(b.X + 0.5f, b.Y + 0.5f)));

        foreach (var t in tiles)
        {
            int x = t.X, y = t.Y;
            var terrain = map.TerrainAt(t);
            string? ore = map.OreAt(t);
            if (sprites && GroundSprite(t, terrain, ore))
            {
                if (map.IsClaimZone(t)) DrawClaim(x, y);
                continue;
            }

            var color = terrain == Terrain.NoBuild ? SpawnGround : ((x + y) % 2 == 0 ? Ground : GroundAlt);
            DrawColoredPolygon(Iso.Diamond(x, y, 1), color);

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

            if (map.IsClaimZone(t)) DrawClaim(x, y);
        }

        rocks.Sort((a, b) => Iso.Depth(a.X + 0.5f, a.Y + 0.5f).CompareTo(Iso.Depth(b.X + 0.5f, b.Y + 0.5f)));
        foreach (var r in rocks)
        {
            if (sprites && PropSprites.Shared.Get("ground", "rock", PropSprites.Variant(r)) is { } rock)
                PropSprites.Draw(this, rock, Iso.ToScreen(r.X + 0.5f, r.Y + 0.5f));
            else
                Iso.DrawBox(this, r.X, r.Y, 1, 14, Rock, 0);
        }
    }

    /// <summary>
    /// The tile's Prop Studio ground: an ore deposit (by the item's first word: iron_ore -> iron_deposit) or plain
    /// ground, tinted for the enemy spawn area. False if there's no sprite for it.
    /// </summary>
    private bool GroundSprite(Int2 t, Terrain terrain, string? ore)
    {
        var props = PropSprites.Shared;
        string variant = PropSprites.Variant(t);
        var frame = ore != null ? props.Get("ground", ore.Split('_')[0] + "_deposit", variant) : null;
        frame ??= props.Get("ground", "ground", variant);
        if (frame == null) return false;
        PropSprites.Draw(this, frame, Iso.ToScreen(t.X + 0.5f, t.Y + 0.5f), 1, terrain == Terrain.NoBuild ? SpawnTint : Colors.White);
        return true;
    }

    private void DrawClaim(int x, int y)
    {
        var d = Iso.Diamond(x, y, 1, 0, 0.04f);
        DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, ClaimOutline, 1.5f);
    }

    private static bool IsQuarterTurn(float angle)
    {
        float q = angle / (MathF.PI / 2);
        return MathF.Abs(q - MathF.Round(q)) < 0.001f;
    }

    private static bool TouchesOpen(GameMap map, Int2 t)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (map.IsPassable(new Int2(t.X + dx, t.Y + dy))) return true;
        return false;
    }
}
