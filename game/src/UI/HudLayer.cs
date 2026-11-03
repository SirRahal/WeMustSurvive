using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// One HUD pop-up drawn on its own canvas item, so it can fade and slide in on its own (<see cref="Hud"/> sets
/// <see cref="CanvasItem.Modulate"/> and <see cref="Control.Position"/>), with a blurred backdrop behind it.
/// </summary>
public partial class HudLayer : Control
{
    private const float OpenSeconds = 0.16f, SlidePx = 6;

    private Polygon2D? _blur;
    private float _t;

    /// <summary>Draws the pop-up's contents (called from this layer's <c>_Draw</c>).</summary>
    public Action<HudLayer>? Paint { get; set; }

    /// <summary>Area behind the pop-up to blur (empty = none).</summary>
    public Vector2[] Backdrop { get; set; } = Array.Empty<Vector2>();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_blur = Glass.Create());
    }

    /// <summary>Advances the open animation: fade + 6 px slide up over 160 ms ease-out while open; hidden at once when closed.</summary>
    public void Animate(bool open, double delta)
    {
        Visible = open;
        _t = open ? Math.Min(1, _t + (float)delta / OpenSeconds) : 0;
        float e = 1 - (1 - _t) * (1 - _t); // ease-out
        Modulate = Colors.White with { A = e };
        Position = new Vector2(0, SlidePx * (1 - e));
        if (_blur != null) _blur.Polygon = Backdrop;
        QueueRedraw();
    }

    /// <summary>Restarts the open animation (e.g. the tooltip moved to another element).</summary>
    public void Restart() => _t = 0;

    public override void _Draw() => Paint?.Invoke(this);
}

/// <summary>Frosted-glass backdrop: a polygon drawn behind its parent that shows the screen behind it, blurred.</summary>
public static class Glass
{
    private static ShaderMaterial? _material;

    private const string ShaderCode = @"shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float blur_lod = 2.5;
void fragment() {
    COLOR = vec4(textureLod(screen_tex, SCREEN_UV, blur_lod).rgb, 1.0);
}";

    public static Polygon2D Create() => new()
    {
        ShowBehindParent = true,
        Material = _material ??= new ShaderMaterial { Shader = new Shader { Code = ShaderCode } },
    };

    public static Vector2[] Rect(Rect2 r) =>
        new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
}
