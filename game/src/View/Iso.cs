using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.View;

/// <summary>
/// Isometric projection. The sim grid is square; this maps grid space (tiles) to screen space (pixels) and back.
/// Grid +X runs down-right on screen, grid +Y runs down-left.
/// </summary>
public static class Iso
{
    public const float TileW = 64f;
    public const float TileH = 32f;

    private static readonly Dictionary<string, Color> ColorCache = new();

    public static Vector2 ToScreen(float gx, float gy) => new((gx - gy) * TileW / 2, (gx + gy) * TileH / 2);
    public static Vector2 ToScreen(SVec g) => ToScreen(g.X, g.Y);

    public static SVec ToGrid(Vector2 s)
    {
        float a = s.X / (TileW / 2);
        float b = s.Y / (TileH / 2);
        return new SVec((a + b) / 2, (b - a) / 2);
    }

    /// <summary>Converts a screen-space direction (e.g. WASD) into the matching grid-space direction.</summary>
    public static SVec ScreenDirToGrid(Vector2 dir)
    {
        if (dir == Vector2.Zero) return SVec.Zero;
        var g = ToGrid(dir * TileH);
        return SVec.Normalize(g);
    }

    /// <summary>Footprint diamond for a size x size area at (x, y), raised by <paramref name="lift"/> pixels.</summary>
    public static Vector2[] Diamond(float x, float y, float size, float lift = 0, float inset = 0)
    {
        var up = new Vector2(0, -lift);
        float a = x + inset, b = y + inset, s = size - inset * 2;
        return new[]
        {
            ToScreen(a, b) + up,
            ToScreen(a + s, b) + up,
            ToScreen(a + s, b + s) + up,
            ToScreen(a, b + s) + up,
        };
    }

    /// <summary>Draws a shaded isometric box: top face plus the two viewer-facing sides.</summary>
    public static void DrawBox(CanvasItem ci, float x, float y, float size, float height, Color color, float inset = 0.08f)
    {
        var floor = Diamond(x, y, size, 0, inset);
        var top = Diamond(x, y, size, height, inset);
        // floor/top order: [0]=north, [1]=east, [2]=south, [3]=west
        ci.DrawColoredPolygon(new[] { floor[3], floor[2], top[2], top[3] }, color.Darkened(0.35f));
        ci.DrawColoredPolygon(new[] { floor[2], floor[1], top[1], top[2] }, color.Darkened(0.18f));
        ci.DrawColoredPolygon(top, color);
        ci.DrawPolyline(new[] { top[0], top[1], top[2], top[3], top[0] }, color.Lightened(0.25f), 1f);
    }

    public static void DrawGroundEllipse(CanvasItem ci, SVec center, float radius, Color color, int segments = 48)
    {
        var pts = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i * Mathf.Tau / segments;
            pts[i] = ToScreen(center.X + MathF.Cos(t) * radius, center.Y + MathF.Sin(t) * radius);
        }
        ci.DrawPolyline(pts, color, 1.5f, true);
    }

    public static Color ParseColor(string html)
    {
        if (!ColorCache.TryGetValue(html, out var c))
            ColorCache[html] = c = Color.FromHtml(html);
        return c;
    }

    public static Vector2 ToGodot(this SVec v) => new(v.X, v.Y);
}
