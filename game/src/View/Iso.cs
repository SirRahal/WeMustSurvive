using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.View;

/// <summary>
/// Isometric projection. The sim grid is square; this maps grid space (tiles) to screen space (pixels) and back.
/// At view angle 0, grid +X runs down-right on screen and grid +Y runs down-left. The view can turn around the map
/// centre (<see cref="Angle"/>): the grid is rotated first, then projected, so everything drawn through here turns.
/// View only: the sim never sees the angle.
/// </summary>
public static class Iso
{
    public const float TileW = 64f;
    public const float TileH = 32f;

    private static readonly Dictionary<string, Color> ColorCache = new();
    private static float _angle, _cos = 1, _sin;
    private static float _cx, _cy;

    /// <summary>View rotation in radians (quarter turns are multiples of Pi/2; values in between during the swing).</summary>
    public static float Angle
    {
        get => _angle;
        set
        {
            _angle = value;
            _cos = MathF.Cos(value);
            _sin = MathF.Sin(value);
        }
    }

    /// <summary>The view angle as whole quarter turns, 0..3 (nearest one while the view is swinging).</summary>
    public static int QuarterTurns => (int)Mathf.PosMod(MathF.Round(_angle / (MathF.PI / 2)), 4);

    /// <summary>The map the view turns around (its centre is the pivot). Resets the angle.</summary>
    public static void SetMap(int width, int height)
    {
        _cx = width / 2f;
        _cy = height / 2f;
        Angle = 0;
    }

    public static Vector2 ToScreen(float gx, float gy)
    {
        float dx = gx - _cx, dy = gy - _cy;
        float rx = _cx + dx * _cos - dy * _sin, ry = _cy + dx * _sin + dy * _cos;
        return new((rx - ry) * TileW / 2, (rx + ry) * TileH / 2);
    }

    public static Vector2 ToScreen(SVec g) => ToScreen(g.X, g.Y);

    public static SVec ToGrid(Vector2 s)
    {
        float a = s.X / (TileW / 2);
        float b = s.Y / (TileH / 2);
        float dx = (a + b) / 2 - _cx, dy = (b - a) / 2 - _cy;
        return new SVec(_cx + dx * _cos + dy * _sin, _cy - dx * _sin + dy * _cos);
    }

    /// <summary>Screen depth for painter's order: things lower on screen are nearer and draw later.</summary>
    public static float Depth(float gx, float gy) => ToScreen(gx, gy).Y;
    public static float Depth(SVec g) => Depth(g.X, g.Y);

    /// <summary>Screen-space bounds of a width x height grid at the current angle.</summary>
    public static Rect2 Bounds(int width, int height)
    {
        var corners = new[] { ToScreen(0, 0), ToScreen(width, 0), ToScreen(width, height), ToScreen(0, height) };
        var r = new Rect2(corners[0], Vector2.Zero);
        foreach (var c in corners) r = r.Expand(c);
        return r;
    }

    /// <summary>Converts a screen-space direction (e.g. WASD) into the matching grid-space direction.</summary>
    public static SVec ScreenDirToGrid(Vector2 dir)
    {
        if (dir == Vector2.Zero) return SVec.Zero;
        var g = ToGrid(dir * TileH) - ToGrid(Vector2.Zero);
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

    /// <summary>Draws a shaded isometric box: top face plus the sides facing the viewer (whichever they are at this angle).</summary>
    public static void DrawBox(CanvasItem ci, float x, float y, float size, float height, Color color, float inset = 0.08f)
    {
        var floor = Diamond(x, y, size, 0, inset);
        var top = Diamond(x, y, size, height, inset);
        var mid = (floor[0] + floor[1] + floor[2] + floor[3]) / 4;
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            var edge = (floor[i] + floor[j]) / 2;
            if (edge.Y <= mid.Y) continue; // back side: hidden behind the top
            // Left-facing sides are in shadow, right-facing ones catch the light.
            float shade = edge.X < mid.X ? 0.35f : 0.18f;
            ci.DrawColoredPolygon(new[] { floor[i], floor[j], top[j], top[i] }, color.Darkened(shade));
        }
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
