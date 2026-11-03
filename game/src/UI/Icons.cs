using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// Placeholder HUD icons: SVG on a 24x24 viewBox, rasterized once per colour and size and cached. Stroke icons use
/// round caps and joins; resource icons are filled. Swap the paths here when real art exists.
/// </summary>
public static class Icons
{
    private const float Oversample = 2f;

    /// <summary>Stroke icons: SVG body (paths, circles, rects) drawn with the caller's colour.</summary>
    private static readonly Dictionary<string, string> Stroke = new()
    {
        ["belt"] = P("M3 8h18M3 16h18M6 8a4 4 0 0 0 0 8M18 8a4 4 0 0 1 0 8M8 12h6M12 10l2 2-2 2"),
        ["inserter"] = P("M4 21h8M8 21v-3M8 18l3-7 6-2M17 9l3-3M17 9l3 1M11 11a1 1 0 1 0 0 .01"),
        ["miner"] = P("M4 10Q12 2 20 10M12 6v15M9 21h6"),
        ["smelter"] = P("M5 21V10h14v11zM4 21h16M8 10V5h3v5M12 19c-1.8 0-2.8-1.2-2.4-2.7.3-1 1.4-1.4 1.4-2.8 1.5.8 3 2.2 3 3.6 0 1.1-.9 1.9-2 1.9z"),
        ["assembler"] = P("M12 8.5a3.5 3.5 0 1 0 0 7a3.5 3.5 0 1 0 0-7zM12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9L7 7M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1"),
        ["ammo_press"] = P("M8 21V11q0-6 4-8 4 2 4 8v10zM8 15h8M8 18h8"),
        ["wall"] = P("M3 6h18v13H3zM3 12.5h18M10 6v6.5M15 12.5V19M6 12.5V19"),
        ["turret"] = P("M4 21h16M6 21v-4h12v4M8 17v-3a4 4 0 0 1 8 0v3M14 11.5l7-4.5"),
        ["beacon"] = P("M12 11v10M8 21h8M10 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0M6.5 5a8 8 0 0 0 0 8M17.5 5a8 8 0 0 1 0 8M3.8 2.5a12 12 0 0 0 0 13M20.2 2.5a12 12 0 0 1 0 13"),
        ["wave"] = P("M5 21V4M5 4h11l-2 4 2 4H5"),
        ["nexus"] = P("M12 2l8.5 5v10L12 22l-8.5-5V7zM12 8l3.5 2v4L12 16l-3.5-2v-4z"),
        ["power"] = P("M13 2L4 14h7l-1 8 9-12h-7l1-8z"),
        ["research"] = P("M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2h12.4a1.5 1.5 0 0 0 1.3-2L14 9V3"),
        ["deconstruct"] = P("M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"),
        ["ping"] = "<circle cx='12' cy='12' r='3'/><circle cx='12' cy='12' r='8'/>",
        ["controls"] = "<circle cx='12' cy='12' r='9'/>" + P("M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .9-1 1.7M12 17h.01"),
        ["lock"] = "<rect x='5' y='11' width='14' height='10' rx='2'/>" + P("M8 11V7a4 4 0 0 1 8 0v4"),
        // Not in the mockup: drawn in the same style.
        ["call_wave"] = P("M5 5l8 7-8 7zM15 5v14M19 5v14"),
        ["pause"] = P("M9 5v14M15 5v14"),
        ["menu"] = P("M4 7h16M4 12h16M4 17h16"),
        ["chevron"] = P("M6 9l6 6 6-6"),
        ["close"] = P("M6 6l12 12M18 6L6 18"),
        ["tunnel"] = P("M3 20V11a9 9 0 0 1 18 0v9M7 20v-8a5 5 0 0 1 10 0v8M2 20h20"),
        ["splitter"] = P("M4 12h6M10 12l4-6h6M10 12l4 6h6M17 3l3 3-3 3M17 15l3 3-3 3"),
        ["generator"] = P("M3 8h18v12H3zM7 8V5h4v3M13 11l-3 4h4l-3 4"),
        ["pole"] = P("M12 3v18M6 7h12M8 11h8M8 21h8M6 7l-1 3M18 7l1 3"),
        ["depot"] = P("M3 8l9-5 9 5v10l-9 5-9-5zM3 8l9 5 9-5M12 13v10"),
        ["check"] = P("M5 12l4 4 10-10"),
        ["chevron_up"] = P("M6 15l6-6 6 6"),
        ["clock"] = "<circle cx='12' cy='12' r='8'/>" + P("M12 8v4l3 2"),
        ["move"] = P("M5 12h14M13 6l6 6-6 6"),
        ["hammer"] = P("M13 4l7 7-3 3-7-7zM10 7l-6 6 3 3 6-6"),
        ["wrench"] = P("M14.5 3.5a4.5 4.5 0 0 0-4 6.4L4 16.4 7.6 20l6.5-6.5a4.5 4.5 0 0 0 6.4-4l-2.8 2.8-2.9-.6-.6-2.9z"),
        ["map"] = P("M3 6l6-3 6 3 6-3v15l-6 3-6-3-6 3zM9 3v15M15 6v15"),
        ["turn_left"] = P("M9 14L4 9l5-5M4 9h10a6 6 0 0 1 0 12h-3"),
        ["turn_right"] = P("M15 14l5-5-5-5M20 9H10a6 6 0 0 0 0 12h3"),
        ["engineer"] =P("M7 10V9a5 5 0 0 1 10 0v1M5 10h14M9 10v1a3 3 0 0 0 6 0v-1M12 4v2M5 21v-1a5 5 0 0 1 5-5h4a5 5 0 0 1 5 5v1"),
        ["lab"] = P("M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2h12.4a1.5 1.5 0 0 0 1.3-2L14 9V3M7 15h10"),
    };

    private static readonly Dictionary<string, ImageTexture> Cache = new();

    private static string P(string d) => $"<path d='{d}'/>";

    /// <summary>A stroke icon tinted <paramref name="color"/>, rasterized for <paramref name="px"/> design pixels.</summary>
    public static Texture2D Get(string id, Color color, int px)
    {
        string key = $"{id}|{color.ToHtml()}|{px}";
        if (Cache.TryGetValue(key, out var tex)) return tex;
        string body = Stroke.GetValueOrDefault(id) ?? Stroke["controls"];
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' fill='none' " +
                     $"stroke='#{color.ToHtml(false)}' stroke-width='1.75' stroke-linecap='round' stroke-linejoin='round'>{body}</svg>";
        return Cache[key] = Rasterize(svg, px);
    }

    /// <summary>Filled resource icon. Coal, iron plates and magazines have their own art; other items get a shape by type in their data colour.</summary>
    public static Texture2D Item(ItemDef item, int px)
    {
        string key = $"item:{item.Id}|{px}";
        if (Cache.TryGetValue(key, out var tex)) return tex;
        var c = new Color(item.Color);
        string light = Hex(c.Lightened(0.15f)), dark = Hex(c.Darkened(0.25f)), edge = Hex(c.Lightened(0.45f));
        string body = item.Id switch
        {
            "coal" => "<path d='M4 15l2.5-6.5L12 6l6 3.5 2 6-4.5 4.5L8 20.5z' fill='#3A3F47' stroke='#9AA4B2' stroke-width='1.25' stroke-linejoin='round'/>",
            "iron_plate" => "<path d='M3 13l9-5 9 5-9 5z' fill='#AEB7C2'/><path d='M3 10l9-5 9 5-9 5z' fill='#E1E6EC'/>",
            "ammo_magazine" => "<path d='M7 8h10v12a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1z' fill='#C9A227'/>" +
                               "<rect x='8.5' y='3' width='3' height='5' rx='1.5' fill='#F2C94C'/><rect x='12.5' y='3' width='3' height='5' rx='1.5' fill='#F2C94C'/>",
            "rocket" => $"<path d='M12 2q4 4 4 11v4H8v-4q0-7 4-11z' fill='{light}'/><path d='M8 14l-3 4v3l3-2zM16 14l3 4v3l-3-2z' fill='{dark}'/>",
            "heavy_shell" => $"<path d='M8 21V11q0-6 4-8 4 2 4 8v10z' fill='{light}'/><path d='M8 17h8v4H8z' fill='{dark}'/>",
            _ when item.Id.EndsWith("_ore") => $"<path d='M4 15l2.5-6.5L12 6l6 3.5 2 6-4.5 4.5L8 20.5z' fill='{light}' stroke='{edge}' stroke-width='1.25' stroke-linejoin='round'/>",
            _ => $"<path d='M3 13l9-5 9 5-9 5z' fill='{dark}'/><path d='M3 10l9-5 9 5-9 5z' fill='{light}'/>",
        };
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'>{body}</svg>";
        return Cache[key] = Rasterize(svg, px);
    }

    /// <summary>Icon id for a building, by kind (and a few ids that look different from their kind).</summary>
    public static string ForBuilding(BuildingDef def) => def.Id switch
    {
        "workshop" => "ammo_press",
        _ => def.Kind switch
        {
            BuildingKind.Belt => "belt",
            BuildingKind.Inserter => "inserter",
            BuildingKind.Drill => "miner",
            BuildingKind.Crafter => "smelter",
            BuildingKind.Lab => "lab",
            BuildingKind.Generator => "generator",
            BuildingKind.Pole => "pole",
            BuildingKind.Wall => "wall",
            BuildingKind.Turret => "turret",
            BuildingKind.Depot => "depot",
            BuildingKind.SuperWeapon => "beacon",
            BuildingKind.TunnelEntrance or BuildingKind.TunnelExit => "tunnel",
            BuildingKind.Splitter => "splitter",
            BuildingKind.Nexus => "nexus",
            _ => "assembler",
        },
    };

    /// <summary>The building's data colour, lifted so dark ones still read on the toolbar.</summary>
    public static Color BuildingColor(BuildingDef def)
    {
        var c = new Color(def.Color);
        return c.Luminance < 0.45f ? c.Lerp(Colors.White, 0.45f - c.Luminance + 0.15f) : c;
    }

    private static string Hex(Color c) => "#" + c.ToHtml(false);

    private static ImageTexture Rasterize(string svg, int px)
    {
        var img = new Image();
        img.LoadSvgFromString(svg, px * Oversample / 24f);
        return ImageTexture.CreateFromImage(img);
    }
}
