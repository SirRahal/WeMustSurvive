using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// Space-theme tokens, fonts and shared drawing for the in-game HUD. Colour rules: cyan only for interactive and
/// selected things, amber only for the wave, the Nexus and pause, red only for warnings, everything else neutral.
/// Display font (Oxanium) for numbers, titles and section labels; body font (Inter) for labels. Both use tabular
/// figures. Corner radius is 2 everywhere.
/// </summary>
public static class HudTheme
{
    // ------------------------------------------------------------------ tokens
    public static readonly Color PlateTop = new(13 / 255f, 24 / 255f, 46 / 255f, 0.90f);
    public static readonly Color PlateBottom = new(7 / 255f, 13 / 255f, 27 / 255f, 0.90f);
    public static readonly Color PopoverBg = new(8 / 255f, 14 / 255f, 28 / 255f, 0.94f);
    public static readonly Color ChipBg = new(120 / 255f, 170 / 255f, 1f, 0.05f);
    public static readonly Color ButtonTop = new("#15243D");
    public static readonly Color ButtonBottom = new("#0F1A2E");
    public static readonly Color ButtonBorder = new("#24365A");
    public static readonly Color ChipBorder = new("#22324D");
    public static readonly Color BorderBottom = new("#0A1222");
    public static readonly Color PopoverTopEdge = new("#3A5F8F");
    public static readonly Color Bevel = new(140 / 255f, 200 / 255f, 1f, 0.08f);
    public static readonly Color HoverBg = new("#1A2B47");
    public static readonly Color HoverBorder = new("#3A5F8F");
    public static readonly Color HoverGlow = new(59 / 255f, 212 / 255f, 240 / 255f, 0.15f);
    public static readonly Color Text = new("#E6EEF8");
    public static readonly Color TextMuted = new("#8FA3BF");
    public static readonly Color Icon = new("#C9D6E8");
    public static readonly Color Accent = new("#3BD4F0");
    public static readonly Color AccentLight = new("#A6F0FF");
    public static readonly Color SelectedTop = new("#0F3E52");
    public static readonly Color SelectedBottom = new("#0A2C3B");
    public static readonly Color SelectedGlow = new(59 / 255f, 212 / 255f, 240 / 255f, 0.35f);
    public static readonly Color Warm = new("#FFB547");
    public static readonly Color WarmLight = new("#FFC66B");
    public static readonly Color WarmLighter = new("#FFD27A");
    /// <summary>Right end of the Nexus integrity gradient.</summary>
    public static readonly Color WarmDeep = new("#FF9A3C");
    /// <summary>Wave countdown in its last 10 seconds.</summary>
    public static readonly Color Urgent = new("#FF8A5B");
    public static readonly Color Danger = new("#FF6B5B");
    public static readonly Color DangerLight = new("#FF9A8E");
    public static readonly Color Good = new("#5FE3A1");
    public static readonly Color Scanline = new(130 / 255f, 190 / 255f, 1f, 0.03f);

    // Names the HUD already uses, mapped onto the tokens above.
    public static readonly Color BarBg = PlateBottom;
    public static readonly Color PanelBg = ChipBg;
    public static readonly Color ButtonBg = ButtonBottom;
    public static readonly Color TooltipBorder = PopoverTopEdge;
    public static readonly Color AccentBg = SelectedBottom;
    public static readonly Color AccentText = AccentLight;
    public static readonly Color Warn = Warm;
    public static readonly Color WarnBg = new(1f, 181 / 255f, 71 / 255f, 0.12f);
    public static readonly Color DangerBg = new(1f, 107 / 255f, 91 / 255f, 0.10f);
    public static readonly Color DangerBorder = new(1f, 107 / 255f, 91 / 255f, 0.45f);
    public static readonly Color DangerText = DangerLight;
    /// <summary>Research is not one of the accent jobs, so its icon is neutral.</summary>
    public static readonly Color Research = Icon;
    public static readonly Color Locked = new("#5C6E88");
    /// <summary>Pulsing outline for the button the tutorial points at.</summary>
    public static readonly Color HintGlow = WarmLighter;

    public const int ChipRadius = 2;
    public const int TooltipRadius = 2;

    // ------------------------------------------------------------------ fonts

    private static Font? _display, _displaySemi, _section, _body, _bodyMedium, _bodySemi;

    /// <summary>Oxanium 700: big numbers and headings.</summary>
    public static Font Display => _display ??= Load("Oxanium-Variable.ttf", 700);
    /// <summary>Oxanium 600: numbers in chips, rows and pop-up titles.</summary>
    public static Font DisplaySemi => _displaySemi ??= Load("Oxanium-Variable.ttf", 600);
    /// <summary>Oxanium 600 with 0.14em tracking, for all-caps section labels (draw with <see cref="SectionLabel"/>).</summary>
    public static Font Section => _section ??= Load("Oxanium-Variable.ttf", 600, spacing: 2);
    /// <summary>Inter Regular.</summary>
    public static Font Body => _body ??= Load("Inter-Variable.ttf", 400);
    public static Font BodyMedium => _bodyMedium ??= Load("Inter-Variable.ttf", 500);
    public static Font BodySemi => _bodySemi ??= Load("Inter-Variable.ttf", 600);

    private static Font Load(string file, int weight, int spacing = 0)
    {
        string path = $"res://assets/fonts/{file}";
        // Imported resource first; a raw load covers a checkout the editor hasn't imported yet.
        Font? baseFont = ResourceLoader.Exists(path) ? ResourceLoader.Load<FontFile>(path) : null;
        if (baseFont == null)
        {
            var raw = new FontFile();
            baseFont = raw.LoadDynamicFont(ProjectSettings.GlobalizePath(path)) == Error.Ok ? raw : ThemeDB.FallbackFont;
        }
        var ts = TextServerManager.GetPrimaryInterface();
        var v = new FontVariation { BaseFont = baseFont, SpacingGlyph = spacing };
        v.OpentypeFeatures = new Godot.Collections.Dictionary { { ts.NameToTag("tnum"), 1 } };
        v.VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wght"), weight } };
        return v;
    }

    public static StyleBoxFlat Box(Color bg, Color border, int radius = ChipRadius, int borderWidth = 1)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(radius);
        return s;
    }

    /// <summary>Pop-up box: background, border, drop shadow (0 16 40 rgba(0,0,0,.6)).</summary>
    public static StyleBoxFlat TooltipBox()
    {
        var s = Box(PopoverBg, ChipBorder, TooltipRadius);
        s.ShadowColor = new Color(0, 0, 0, 0.6f);
        s.ShadowSize = 20;
        s.ShadowOffset = new Vector2(0, 16);
        return s;
    }

    // ------------------------------------------------------------------ shared drawing

    public enum Look { Normal, Hover, Selected, Disabled }

    private static StyleBoxFlat? _hoverGlow, _selectedGlow, _warmGlow, _popoverGlow, _popover;

    /// <summary>
    /// Bar/toolbar background: a vertical plate gradient with faint scanlines (1 px every 3 px). Corners can be cut at
    /// 45° (top-left, top-right, bottom-right, bottom-left, in pixels).
    /// </summary>
    public static void Plate(CanvasItem ci, Rect2 r, float cutTL = 0, float cutTR = 0, float cutBR = 0, float cutBL = 0)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        if (r.Size.X <= 0 || r.Size.Y <= 0) return; // not laid out yet (first frame)
        var pts = PlatePoints(r, cutTL, cutTR, cutBR, cutBL);
        var colors = pts.Select(p => PlateTop.Lerp(PlateBottom, (p.Y - y0) / r.Size.Y)).ToArray();
        ci.DrawPolygon(pts, colors);
        for (float y = y0 + 1.5f; y < y1; y += 3)
        {
            float top = y - y0, bottom = y1 - y;
            float left = x0 + Math.Max(cutTL - top, 0) + Math.Max(cutBL - bottom, 0);
            float right = x1 - Math.Max(cutTR - top, 0) - Math.Max(cutBR - bottom, 0);
            if (right > left) ci.DrawLine(new Vector2(left, y), new Vector2(right, y), Scanline, 1);
        }
    }

    /// <summary>Outline of a plate with 45° corner cuts (also the shape of its glass backdrop).</summary>
    public static Vector2[] PlatePoints(Rect2 r, float cutTL = 0, float cutTR = 0, float cutBR = 0, float cutBL = 0)
    {
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        var pts = new[]
        {
            new Vector2(x0 + cutTL, y0), new Vector2(x1 - cutTR, y0), new Vector2(x1, y0 + cutTR),
            new Vector2(x1, y1 - cutBR), new Vector2(x1 - cutBR, y1), new Vector2(x0 + cutBL, y1),
            new Vector2(x0, y1 - cutBL), new Vector2(x0, y0 + cutTL),
        };
        // An uncut corner repeats a point, which polygon triangulation rejects.
        return pts.Where((p, i) => p != pts[(i + 1) % pts.Length]).ToArray();
    }

    /// <summary>Read-only chip in the top bar.</summary>
    public static void Chip(CanvasItem ci, Rect2 r, bool hover = false)
    {
        ci.DrawRect(r, ChipBg);
        ci.DrawRect(r, hover ? HoverBorder : ChipBorder, false, 1);
    }

    /// <summary>
    /// Button / tab / slot frame: gradient, 1 px border with a darker bottom edge and a bevel highlight. Hover lifts
    /// the colours and adds a soft cyan glow; selected is the cyan (or amber, <paramref name="warm"/>) selected style.
    /// </summary>
    public static void Button(CanvasItem ci, Rect2 r, Look look, bool warm = false)
    {
        var tint = warm ? Warm : Accent;
        switch (look)
        {
            case Look.Hover:
                ci.DrawStyleBox(_hoverGlow ??= Glow(HoverGlow, 12), r);
                VGradient(ci, r, HoverBg, ButtonBottom.Lerp(HoverBg, 0.5f));
                break;
            case Look.Selected:
                ci.DrawStyleBox(warm ? _warmGlow ??= Glow(Warm with { A = 0.3f }, 18) : _selectedGlow ??= Glow(SelectedGlow, 18), r);
                VGradient(ci, r, warm ? new Color("#4A3412") : SelectedTop, warm ? new Color("#33230B") : SelectedBottom);
                // Inset glow: a few fading inner outlines.
                for (int i = 1; i <= 4; i++) ci.DrawRect(r.Grow(-i), tint with { A = 0.15f * (1 - i / 5f) }, false, 1);
                break;
            default:
                VGradient(ci, r, ButtonTop, ButtonBottom);
                break;
        }
        if (look == Look.Disabled) ci.DrawRect(r, new Color(0, 0, 0, 0.45f));

        var border = look switch { Look.Selected => tint, Look.Hover => HoverBorder, _ => ButtonBorder };
        ci.DrawRect(r, border, false, 1);
        if (look != Look.Selected)
            ci.DrawLine(new Vector2(r.Position.X, r.End.Y - 0.5f), new Vector2(r.End.X, r.End.Y - 0.5f), BorderBottom, 1);
        ci.DrawLine(r.Position + new Vector2(1, 1.5f), new Vector2(r.End.X - 1, r.Position.Y + 1.5f), Bevel, 1);
    }

    /// <summary>
    /// Pop-up frame: shadow and faint cyan outer glow, background, 1 px border with a lit top edge, and cyan corner
    /// brackets top-left and bottom-right.
    /// </summary>
    public static void Popover(CanvasItem ci, Rect2 r)
    {
        ci.DrawStyleBox(_popoverGlow ??= Glow(Accent with { A = 0.10f }, 14), r);
        ci.DrawStyleBox(_popover ??= TooltipBox(), r);
        ci.DrawLine(r.Position + new Vector2(0, 0.5f), new Vector2(r.End.X, r.Position.Y + 0.5f), PopoverTopEdge, 1);
        Brackets(ci, r, 12, Accent, allCorners: false);
    }

    /// <summary>L-shaped corner brackets (2 px): top-left and bottom-right, or all four corners.</summary>
    public static void Brackets(CanvasItem ci, Rect2 r, float len, Color color, bool allCorners = true)
    {
        void Corner(Vector2 c, float dx, float dy)
        {
            ci.DrawLine(c, c + new Vector2(dx * len, 0), color, 2);
            ci.DrawLine(c, c + new Vector2(0, dy * len), color, 2);
        }
        Corner(r.Position + new Vector2(1, 1), 1, 1);
        Corner(r.End - new Vector2(1, 1), -1, -1);
        if (!allCorners) return;
        Corner(new Vector2(r.End.X - 1, r.Position.Y + 1), -1, 1);
        Corner(new Vector2(r.Position.X + 1, r.End.Y - 1), 1, -1);
    }

    /// <summary>All-caps section label in the display font with letter spacing (LOGISTICS, STORAGE, CONTROLS).</summary>
    public static void SectionLabel(CanvasItem ci, Vector2 baseline, string text, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1) =>
        ci.DrawString(Section, baseline, text.ToUpperInvariant(), align, width, 13, AccentLight);

    public static void VGradient(CanvasItem ci, Rect2 r, Color top, Color bottom) =>
        ci.DrawPolygon(new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) },
            new[] { top, top, bottom, bottom });

    /// <summary>A transparent box whose shadow, centred, works as a soft outer glow.</summary>
    private static StyleBoxFlat Glow(Color color, int size)
    {
        var s = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ShadowColor = color, ShadowSize = size };
        s.SetCornerRadiusAll(ChipRadius);
        return s;
    }
}
