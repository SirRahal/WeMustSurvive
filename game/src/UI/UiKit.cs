using Godot;

namespace BeltAndBarricade.UI;

/// <summary>Shared look for menu screens built from Godot controls.</summary>
public static class UiKit
{
    public static readonly Color Backdrop = new(0.04f, 0.045f, 0.06f);
    public static readonly Color PanelBg = new(0.06f, 0.07f, 0.09f, 0.97f);
    public static readonly Color Accent = new(0.35f, 0.8f, 1f);
    public static readonly Color Text = new(0.92f, 0.93f, 0.95f);
    public static readonly Color Dim = new(0.6f, 0.63f, 0.68f);
    public static readonly Color Good = new(0.45f, 0.95f, 0.5f);
    public static readonly Color Warn = new(1f, 0.45f, 0.35f);
    public static readonly Color Gold = new(1f, 0.85f, 0.3f);
    private static readonly Color ButtonBg = new(0.12f, 0.14f, 0.18f);
    private static readonly Color ButtonHover = new(0.18f, 0.24f, 0.32f);
    private static readonly Color Danger = new(0.55f, 0.16f, 0.14f);

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int margin = 8)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(4);
        s.SetContentMarginAll(margin);
        return s;
    }

    public static Button Button(string text, Action onPressed, bool danger = false, float width = 220)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width, 42), FocusMode = Control.FocusModeEnum.All };
        var bg = danger ? Danger : ButtonBg;
        b.AddThemeStyleboxOverride("normal", Box(bg, bg.Lightened(0.15f)));
        b.AddThemeStyleboxOverride("hover", Box(danger ? Danger.Lightened(0.15f) : ButtonHover, Accent));
        b.AddThemeStyleboxOverride("pressed", Box(bg.Darkened(0.2f), Accent));
        b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), Accent, 2));
        b.AddThemeStyleboxOverride("disabled", Box(bg.Darkened(0.4f), bg));
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += onPressed;
        return b;
    }

    public static Label Label(string text, int size = 16, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Text);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static PanelContainer Panel(Control content, float minWidth = 420)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0) };
        panel.AddThemeStyleboxOverride("panel", Box(PanelBg, Accent with { A = 0.8f }, 2, 22));
        panel.AddChild(content);
        return panel;
    }

    public static VBoxContainer Column(int separation = 10)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    /// <summary>Full-screen container that centres its single child.</summary>
    public static CenterContainer Centered(Control child)
    {
        var c = new CenterContainer();
        c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        c.AddChild(child);
        return c;
    }

    public static void Clear(Node n)
    {
        foreach (var child in n.GetChildren()) child.QueueFree();
    }
}
