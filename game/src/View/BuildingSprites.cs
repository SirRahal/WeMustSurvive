using Godot;

namespace BeltAndBarricade.View;

/// <summary>
/// Building and item sprites for the HUD's art wells (build slots, tier cards), drawn from <see cref="PropSprites"/>.
/// The map draws through <see cref="PropSprites"/> directly.
/// </summary>
public sealed class BuildingSprites
{
    private static BuildingSprites? _shared;
    public static BuildingSprites Shared => _shared ??= new BuildingSprites();

    private static PropSprites Props => PropSprites.Shared;

    public bool Has(string id) => Props.Has("buildings", id);

    /// <summary>Draws an item's sprite centred on <paramref name="at"/> at <paramref name="scale"/>. False if it has none.</summary>
    public bool DrawItem(CanvasItem ci, string item, Vector2 at, float scale = 1, Color? modulate = null)
    {
        if (Props.Get("items", item, "default") is not { } f) return false;
        PropSprites.Draw(ci, f, at, scale, modulate);
        return true;
    }

    /// <summary>Draws a building's front-facing frame scaled to fit <paramref name="box"/> (build slots, tier cards). False if it has no sprite.</summary>
    public bool DrawFit(CanvasItem ci, string id, Rect2 box, Color modulate)
    {
        var frame = Props.First("buildings", id, "south", "default", "s", "iron_south", "south_0");
        if (frame == null && Props.Frames("buildings", id) is { Count: > 0 } all) frame = all.Values.First();
        if (frame == null) return false;
        PropSprites.DrawFit(ci, frame, box, modulate);
        return true;
    }
}
