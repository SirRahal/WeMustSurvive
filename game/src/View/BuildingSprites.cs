using BeltAndBarricade.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.View;

/// <summary>
/// Pre-rendered building sprites from Prop Studio (<c>game/art/buildings</c>, synced by <c>tools/sync-art.bat</c>).
/// <c>sprites.json</c> lists each building id's frames: four facings (south/east/north/west) for directional buildings,
/// eight aims (s, se, e, ...) for turrets, or one "default". Each frame has an anchor: the pixel over the centre of
/// the footprint. Frames are picked for the current view angle, so a building keeps its facing as the view turns.
/// Buildings without a sprite are drawn as boxes by <see cref="WorldView"/>.
/// </summary>
public sealed class BuildingSprites
{
    private const string Dir = "res://art/buildings/";

    /// <summary>A frame's @2x texture, drawn at half size so it stays sharp when zoomed in.</summary>
    private sealed record Frame(Texture2D Texture, Vector2 Anchor, Vector2 Size);

    private static readonly string[] Aims = { "e", "se", "s", "sw", "w", "nw", "n", "ne" };

    private readonly Dictionary<string, Dictionary<string, Frame>> _buildings = new();
    /// <summary>Item sprites (one frame each), drawn on belts and in storage animations.</summary>
    private readonly Dictionary<string, Frame> _items = new();

    public static BuildingSprites Load()
    {
        var sprites = new BuildingSprites();
        string jsonPath = Dir + "sprites.json";
        if (!Godot.FileAccess.FileExists(jsonPath)) return sprites;
        var doc = Json.ParseString(Godot.FileAccess.GetFileAsString(jsonPath)).AsGodotDictionary();
        foreach (var (id, entry) in doc["buildings"].AsGodotDictionary())
        {
            var frames = new Dictionary<string, Frame>();
            foreach (var (name, f) in entry.AsGodotDictionary()["frames"].AsGodotDictionary())
            {
                var frame = f.AsGodotDictionary();
                var tex = LoadTexture(Dir + (string)frame["file@2x"]);
                if (tex == null) continue;
                var anchor = frame["anchor@2x"].AsInt32Array();
                var size = frame["size@2x"].AsInt32Array();
                frames[(string)name] = new Frame(tex, new Vector2(anchor[0], anchor[1]) / 2, new Vector2(size[0], size[1]) / 2);
            }
            if (frames.Count > 0) sprites._buildings[(string)id] = frames;
        }
        if (doc.ContainsKey("items"))
            foreach (var (id, entry) in doc["items"].AsGodotDictionary())
            {
                var frame = entry.AsGodotDictionary()["frames"].AsGodotDictionary()["default"].AsGodotDictionary();
                // Items are tiny: the 4x render, drawn at a quarter size, stays sharp when zoomed in.
                string key = frame.ContainsKey("file@4x") ? "@4x" : "@2x";
                float div = key == "@4x" ? 4 : 2;
                var tex = LoadTexture(Dir + (string)frame["file" + key]);
                if (tex == null) continue;
                var anchor = frame["anchor" + key].AsInt32Array();
                var size = frame["size" + key].AsInt32Array();
                sprites._items[(string)id] = new Frame(tex, new Vector2(anchor[0], anchor[1]) / div, new Vector2(size[0], size[1]) / div);
            }
        return sprites;
    }

    private static BuildingSprites? _shared;
    /// <summary>The game's sprites, loaded once (map renderer and HUD art wells).</summary>
    public static BuildingSprites Shared => _shared ??= Load();

    public bool Has(string id) => _buildings.ContainsKey(id);

    /// <summary>Draws an item's sprite centred on <paramref name="at"/> at <paramref name="scale"/>. False if it has none.</summary>
    public bool DrawItem(CanvasItem ci, string item, Vector2 at, float scale = 1, Color? modulate = null)
    {
        if (!_items.TryGetValue(item, out var f)) return false;
        ci.DrawTextureRect(f.Texture, new Rect2(at - f.Anchor * scale, f.Size * scale), false, modulate ?? Colors.White);
        return true;
    }

    /// <summary>Draws a building's front-facing frame scaled to fit <paramref name="box"/> (build slots, tier cards). False if it has no sprite.</summary>
    public bool DrawFit(CanvasItem ci, string id, Rect2 box, Color modulate)
    {
        if (!_buildings.TryGetValue(id, out var frames)) return false;
        var frame = frames.GetValueOrDefault("south") ?? frames.GetValueOrDefault("default") ?? frames.GetValueOrDefault("s") ?? frames.Values.First();
        float scale = Math.Min(box.Size.X / frame.Size.X, box.Size.Y / frame.Size.Y);
        var size = frame.Size * scale;
        ci.DrawTextureRect(frame.Texture, new Rect2(box.GetCenter() - size / 2, size), false, modulate);
        return true;
    }

    /// <summary>
    /// Draws <paramref name="id"/>'s sprite for a building on <paramref name="ground"/> (screen point of its footprint
    /// centre). <paramref name="facing"/> is a grid direction (buildings) and <paramref name="aim"/> a grid vector
    /// (turrets); both are turned by the view so the right side shows. False if there is no sprite.
    /// </summary>
    public bool Draw(CanvasItem ci, string id, Vector2 ground, Direction? facing = null, SVec? aim = null, Color? modulate = null)
    {
        if (!_buildings.TryGetValue(id, out var frames)) return false;
        Frame? frame = null;
        if (aim is { } a && a != SVec.Zero) frame = frames.GetValueOrDefault(AimName(ViewTurn(a)));
        if (frame == null && facing is { } f) frame = frames.GetValueOrDefault(ViewTurn(f).ToString().ToLowerInvariant());
        frame ??= frames.GetValueOrDefault("default") ?? frames.GetValueOrDefault("s") ?? frames.Values.First();
        ci.DrawTextureRect(frame.Texture, new Rect2(ground - frame.Anchor, frame.Size), false, modulate ?? Colors.White);
        return true;
    }

    /// <summary>A grid direction as it looks at the current view angle (nearest quarter turn).</summary>
    private static Direction ViewTurn(Direction d)
    {
        for (int i = 0; i < Iso.QuarterTurns; i++) d = d.RotateCw();
        return d;
    }

    private static SVec ViewTurn(SVec v)
    {
        for (int i = 0; i < Iso.QuarterTurns; i++) v = new SVec(-v.Y, v.X); // a clockwise quarter turn, as Direction.RotateCw
        return v;
    }

    /// <summary>Nearest of the eight aim frames; "e" = grid +X, "s" = grid +Y.</summary>
    private static string AimName(SVec v)
    {
        float angle = Mathf.PosMod(MathF.Atan2(v.Y, v.X), Mathf.Tau);
        return Aims[(int)MathF.Round(angle / (Mathf.Tau / 8)) % 8];
    }

    private static Texture2D? LoadTexture(string path)
    {
        // Imported texture first; a raw PNG load covers a checkout the editor hasn't imported yet.
        if (ResourceLoader.Exists(path)) return ResourceLoader.Load<Texture2D>(path);
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        return img == null || img.IsEmpty() ? null : ImageTexture.CreateFromImage(img);
    }
}
