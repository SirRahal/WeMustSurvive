using BeltAndBarricade.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.View;

/// <summary>
/// The pre-rendered sprites from Prop Studio (<c>game/art/props</c>, synced by <c>tools/sync-art.bat</c>).
/// <c>sprites.json</c> has a section each (<c>buildings</c>, <c>units</c>, <c>enemies</c>, <c>items</c>, <c>effects</c>,
/// <c>ground</c>); each id has named frames, each frame a size, an anchor (the pixel over the footprint centre, a
/// unit's feet or an effect's centre) and a place on an atlas page. Frames are drawn from the @2x atlases at half size
/// (items from @4x at a quarter) so they stay sharp zoomed in; the pages get mipmaps so they stay smooth zoomed out.
/// Frame names are grid directions; <see cref="ViewTurn(Direction)"/> turns them for the current view angle.
/// </summary>
public sealed class PropSprites
{
    private const string Dir = "res://art/props/";

    /// <summary>One frame: its region of an atlas page, and its size, anchor and smoke points in 1x pixels.</summary>
    public sealed record Frame(Texture2D Page, Rect2 Region, Vector2 Size, Vector2 Anchor, Vector2[] Smoke);

    private static readonly string[] Aims = { "e", "se", "s", "sw", "w", "nw", "n", "ne" };

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, Frame>>> _sections = new();
    private readonly Dictionary<string, Texture2D?> _pages = new();

    /// <summary>Metres per tile in the renders (belt slats are spaced in metres).</summary>
    public float MetresPerTile { get; private set; } = 2f;

    private static PropSprites? _shared;
    /// <summary>The game's sprites, loaded once (map, terrain and HUD art wells).</summary>
    public static PropSprites Shared => _shared ??= Load();

    public static PropSprites Load()
    {
        var sprites = new PropSprites();
        string jsonPath = Dir + "sprites.json";
        if (!Godot.FileAccess.FileExists(jsonPath)) return sprites;
        var doc = Json.ParseString(Godot.FileAccess.GetFileAsString(jsonPath)).AsGodotDictionary();
        if (doc.TryGetValue("metres_per_tile", out var mpt)) sprites.MetresPerTile = (float)mpt.AsDouble();
        foreach (string section in new[] { "buildings", "engineer", "units", "enemies", "items", "effects", "ground" })
        {
            if (!doc.TryGetValue(section, out var entries)) continue;
            var ids = new Dictionary<string, Dictionary<string, Frame>>();
            // Items are tiny: the 4x render, drawn at a quarter size, stays sharp when zoomed in.
            var scales = section == "items" ? new[] { ("@4x", 4f), ("@2x", 2f), ("", 1f) } : new[] { ("@2x", 2f), ("", 1f) };
            foreach (var (id, entry) in entries.AsGodotDictionary())
            {
                var frames = new Dictionary<string, Frame>();
                foreach (var (name, f) in entry.AsGodotDictionary()["frames"].AsGodotDictionary())
                    if (sprites.ReadFrame(f.AsGodotDictionary(), scales) is { } frame)
                        frames[(string)name] = frame;
                if (frames.Count > 0) ids[(string)id] = frames;
                sprites.ReadFps(section, (string)id, entry.AsGodotDictionary());
            }
            sprites._sections[section] = ids;
        }
        return sprites;
    }

    private Frame? ReadFrame(Godot.Collections.Dictionary f, (string Key, float Div)[] scales)
    {
        foreach (var (key, div) in scales)
        {
            if (!f.ContainsKey("atlas" + key)) continue;
            var at = f["atlas" + key].AsGodotArray();
            var page = Page((string)at[0]);
            if (page == null) continue;
            var size = f["size" + key].AsInt32Array();
            var anchor = f["anchor" + key].AsInt32Array();
            var smoke = f.TryGetValue("smoke" + key, out var s)
                ? s.AsGodotArray().Select(p => { var q = p.AsInt32Array(); return new Vector2(q[0], q[1]) / div; }).ToArray()
                : Array.Empty<Vector2>();
            return new Frame(page, new Rect2((int)at[1], (int)at[2], size[0], size[1]),
                new Vector2(size[0], size[1]) / div, new Vector2(anchor[0], anchor[1]) / div, smoke);
        }
        return null;
    }

    private Texture2D? Page(string file)
    {
        if (_pages.TryGetValue(file, out var tex)) return tex;
        string path = Dir + file;
        // Imported texture first; a raw PNG load covers a checkout the editor hasn't imported yet. Either way the
        // page is rebuilt with mipmaps so sprites stay smooth when the camera zooms out.
        Image? img = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path)?.GetImage() : null;
        img ??= Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        if (img != null && !img.IsEmpty())
        {
            if (img.IsCompressed()) img.Decompress();
            img.GenerateMipmaps();
            tex = ImageTexture.CreateFromImage(img);
        }
        return _pages[file] = tex;
    }

    public bool Has(string section, string id) => _sections.TryGetValue(section, out var ids) && ids.ContainsKey(id);

    // ------------------------------------------------------------------ animation strips

    /// <summary>Frames-per-second from sprites.json: an id's "fps" (all its strips), and "anims": {strip: fps or {"fps": n}}.</summary>
    private readonly Dictionary<(string Section, string Id), (float? All, Dictionary<string, float> Strips)> _fps = new();
    private readonly Dictionary<(string Section, string Id, string Prefix), Frame[]> _strips = new();

    private void ReadFps(string section, string id, Godot.Collections.Dictionary entry)
    {
        float? all = entry.TryGetValue("fps", out var f) ? (float)f.AsDouble() : null;
        var strips = new Dictionary<string, float>();
        if (entry.TryGetValue("anims", out var anims))
            foreach (var (name, a) in anims.AsGodotDictionary())
            {
                if (a.VariantType is Godot.Variant.Type.Float or Godot.Variant.Type.Int) strips[(string)name] = (float)a.AsDouble();
                else if (a.AsGodotDictionary().TryGetValue("fps", out var af)) strips[(string)name] = (float)af.AsDouble();
            }
        if (all != null || strips.Count > 0) _fps[(section, id)] = (all, strips);
    }

    /// <summary>A strip's frames-per-second from sprites.json, or <paramref name="fallback"/> if it gives none.</summary>
    public float Fps(string section, string id, string prefix, float fallback)
    {
        if (!_fps.TryGetValue((section, id), out var fps)) return fallback;
        // A strip is named by its prefix ("south_0".. is "south"), or by what follows a facing ("s_walk" is "walk").
        if (fps.Strips.TryGetValue(prefix, out var s)) return s;
        int cut = prefix.IndexOf('_');
        if (cut >= 0 && fps.Strips.TryGetValue(prefix[(cut + 1)..], out s)) return s;
        return fps.All ?? fallback;
    }

    /// <summary>
    /// An animation strip: the frames "&lt;prefix&gt;_0", "_1"... (or from "_1"), in order. Empty if there are none.
    /// </summary>
    public Frame[] Strip(string section, string id, string prefix)
    {
        if (_strips.TryGetValue((section, id, prefix), out var strip)) return strip;
        var list = new List<Frame>();
        if (Frames(section, id) is { } frames)
        {
            int i = frames.ContainsKey(prefix + "_0") ? 0 : 1;
            while (frames.TryGetValue($"{prefix}_{i}", out var f)) { list.Add(f); i++; }
        }
        return _strips[(section, id, prefix)] = list.ToArray();
    }

    /// <summary>
    /// The strip's frame at <paramref name="seconds"/> (real time, looping) at its fps, shifted by
    /// <paramref name="offset"/> frames so neighbours aren't in lockstep. Null if there is no strip.
    /// </summary>
    public Frame? Play(string section, string id, string prefix, double seconds, float fallbackFps, int offset = 0)
    {
        var strip = Strip(section, id, prefix);
        if (strip.Length == 0) return null;
        long i = (long)(seconds * Fps(section, id, prefix, fallbackFps)) + offset;
        return strip[(int)(((i % strip.Length) + strip.Length) % strip.Length)];
    }

    /// <summary>The strip's frame <paramref name="index"/> (wrapping), or null if there is no strip.</summary>
    public Frame? At(string section, string id, string prefix, int index)
    {
        var strip = Strip(section, id, prefix);
        return strip.Length == 0 ? null : strip[((index % strip.Length) + strip.Length) % strip.Length];
    }

    /// <summary>All of an id's frames, or null if it has no sprite.</summary>
    public IReadOnlyDictionary<string, Frame>? Frames(string section, string id) =>
        _sections.TryGetValue(section, out var ids) && ids.TryGetValue(id, out var frames) ? frames : null;

    public Frame? Get(string section, string id, string frame) =>
        Frames(section, id) is { } frames && frames.TryGetValue(frame, out var f) ? f : null;

    /// <summary>The first of <paramref name="names"/> that <paramref name="id"/> has.</summary>
    public Frame? First(string section, string id, params string[] names)
    {
        if (Frames(section, id) is not { } frames) return null;
        foreach (var n in names)
            if (frames.TryGetValue(n, out var f)) return f;
        return null;
    }

    /// <summary>Draws <paramref name="f"/> with its anchor on <paramref name="at"/>.</summary>
    public static void Draw(CanvasItem ci, Frame f, Vector2 at, float scale = 1, Color? modulate = null) =>
        ci.DrawTextureRectRegion(f.Page, new Rect2(at - f.Anchor * scale, f.Size * scale), f.Region, modulate ?? Colors.White);

    /// <summary>Draws <paramref name="f"/> scaled to fit <paramref name="box"/>, centred.</summary>
    public static void DrawFit(CanvasItem ci, Frame f, Rect2 box, Color modulate)
    {
        float scale = Math.Min(box.Size.X / f.Size.X, box.Size.Y / f.Size.Y);
        var size = f.Size * scale;
        ci.DrawTextureRectRegion(f.Page, new Rect2(box.GetCenter() - size / 2, size), f.Region, modulate);
    }

    // ------------------------------------------------------------------ frame names

    /// <summary>A grid direction as it looks at the current view angle (nearest quarter turn).</summary>
    public static Direction ViewTurn(Direction d)
    {
        for (int i = 0; i < Iso.QuarterTurns; i++) d = d.RotateCw();
        return d;
    }

    /// <summary>A grid vector as it looks at the current view angle (nearest quarter turn).</summary>
    public static SVec ViewTurn(SVec v)
    {
        for (int i = 0; i < Iso.QuarterTurns; i++) v = new SVec(-v.Y, v.X); // a clockwise quarter turn, as Direction.RotateCw
        return v;
    }

    /// <summary>A direction's frame name at the current view angle: "south", "east"...</summary>
    public static string DirName(Direction d) => ViewTurn(d).ToString().ToLowerInvariant();

    /// <summary>Nearest of the eight aims/facings at the current view angle; "e" = grid +X, "s" = grid +Y.</summary>
    public static string AimName(SVec v)
    {
        v = ViewTurn(v);
        float angle = Mathf.PosMod(MathF.Atan2(v.Y, v.X), Mathf.Tau);
        return Aims[(int)MathF.Round(angle / (Mathf.Tau / 8)) % 8];
    }

    /// <summary>One of four variants ("v1".."v4") picked by a hash of a tile, so the map isn't repetitive.</summary>
    public static string Variant(Int2 t)
    {
        uint h = unchecked((uint)t.X * 374761393u + (uint)t.Y * 668265263u);
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        return "v" + (1 + (h >> 16) % 4);
    }
}
