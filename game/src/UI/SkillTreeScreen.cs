using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// The meta-progression tree. Drag to pan, wheel to zoom, click a glowing node to buy it with a meta point,
/// right-click an owned node to refund it (if that doesn't disconnect anything).
/// </summary>
public partial class SkillTreeScreen : Control
{
    private const float Unit = 95f;
    private static readonly Color LinkOff = new(0.3f, 0.33f, 0.4f);
    private static readonly Color NodeOff = new(0.16f, 0.18f, 0.22f);

    private readonly App _app;
    private Vector2 _pan;
    private float _zoom = 0.72f;
    private bool _dragging;
    private string? _hover;
    private Font _font = null!;

    public SkillTreeScreen(App app) => _app = app;

    private SkillTree Tree => _app.Tree;
    private Profile Profile => _app.Profile;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _font = ThemeDB.FallbackFont;

        var bar = new HBoxContainer { Position = new Vector2(16, 16) };
        bar.AddThemeConstantOverride("separation", 12);
        bar.AddChild(UiKit.Button("Back", () => _app.ShowMainMenu(), width: 140));
        bar.AddChild(UiKit.Button("Reset tree", () => { Profile.ResetTree(); QueueRedraw(); }, danger: true, width: 160));
        AddChild(bar);
    }

    private Vector2 Origin => GetViewportRect().Size / 2 + _pan;
    private Vector2 ScreenPos(SkillNode n) => Origin + new Vector2(n.X, n.Y) * Unit * _zoom;

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _zoom = Mathf.Min(2f, _zoom * 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _zoom = Mathf.Max(0.5f, _zoom / 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                if (_hover != null) Profile.TryAllocate(Tree, _hover);
                else _dragging = true;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _dragging = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when _hover != null:
                Profile.TryRefund(Tree, _hover);
                break;
            case InputEventMouseMotion m:
                if (_dragging) _pan += m.Relative;
                _hover = Tree.Nodes.Values.FirstOrDefault(n => n.Id != SkillTree.StartId &&
                    ScreenPos(n).DistanceTo(m.Position) <= Radius(n))?.Id;
                break;
        }
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            _app.ShowMainMenu();
        }
    }

    private float Radius(SkillNode n) => (n.Notable ? 22 : n.Id == SkillTree.StartId ? 26 : 13) * _zoom;

    private Color ClusterColor(SkillNode n) =>
        Tree.Clusters.FirstOrDefault(c => c.Id == n.Cluster) is { } c ? Color.FromHtml(c.Color) : UiKit.Gold;

    private bool Owned(string id) => id == SkillTree.StartId || Profile.Allocated.Contains(id);

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), UiKit.Backdrop);

        // Cluster labels around the outside.
        foreach (var c in Tree.Clusters)
        {
            var deep = Tree.Nodes.Values.FirstOrDefault(n => n.Cluster == c.Id && n.Id.EndsWith("_n2"));
            if (deep == null) continue;
            var p = Origin + new Vector2(deep.X, deep.Y) * Unit * _zoom * 1.22f;
            DrawString(_font, p - new Vector2(100, 0), c.Name.ToUpperInvariant(), HorizontalAlignment.Center, 200, 15, Color.FromHtml(c.Color) with { A = 0.8f });
        }

        foreach (var n in Tree.Nodes.Values)
        foreach (var l in n.Links)
        {
            var other = Tree.Nodes[l];
            bool lit = Owned(n.Id) && Owned(l);
            DrawLine(ScreenPos(n), ScreenPos(other), lit ? UiKit.Gold : LinkOff, lit ? 4 : 2, true);
        }

        foreach (var n in Tree.Nodes.Values)
        {
            var p = ScreenPos(n);
            float r = Radius(n);
            bool owned = Owned(n.Id);
            bool available = Tree.CanAllocate(Profile.Allocated, n.Id);
            var c = ClusterColor(n);
            DrawCircle(p, r, owned ? c : NodeOff);
            float ring = available ? 2 + 1.5f * Mathf.Sin((float)Time.GetTicksMsec() / 200f) : 1.5f;
            DrawArc(p, r + 2, 0, Mathf.Tau, 32, owned ? UiKit.Gold : available ? c : LinkOff, ring + 1, true);
            if (n.Notable) DrawArc(p, r - 5 * _zoom, 0, Mathf.Tau, 32, owned ? Colors.White with { A = 0.7f } : c with { A = 0.5f }, 2, true);
        }

        DrawHeader(size);
        DrawTooltip();
    }

    public override void _Process(double delta) => QueueRedraw(); // pulsing "available" rings

    private void DrawHeader(Vector2 size)
    {
        string text = $"Meta points: {Profile.MetaPoints}    Allocated: {Profile.SpentPoints} / {Tree.Nodes.Count - 1}";
        DrawString(_font, new Vector2(0, 40), text, HorizontalAlignment.Center, size.X, 22, UiKit.Gold);
        DrawString(_font, new Vector2(0, 66), "Click a glowing node to buy it. Right-click to refund. Drag to pan, wheel to zoom. " +
            "Deep notables need a node from the neighbouring cluster.", HorizontalAlignment.Center, size.X, 14, UiKit.Dim);

        var mods = Tree.ToModifiers(Profile.Allocated);
        float y = 110;
        DrawString(_font, new Vector2(size.X - 330, y), "Active bonuses", HorizontalAlignment.Left, -1, 16, UiKit.Accent);
        foreach (var (stat, v) in mods.Bonuses)
        {
            y += 20;
            DrawString(_font, new Vector2(size.X - 330, y), SkillTree.FormatStat(stat, v), HorizontalAlignment.Left, 320, 14, UiKit.Text);
        }
    }

    private void DrawTooltip()
    {
        if (_hover == null || !Tree.Nodes.TryGetValue(_hover, out var n)) return;
        var p = ScreenPos(n) + new Vector2(24, -10);
        var lines = new List<(string, Color)> { (n.Name + (n.Notable ? "  (Notable)" : ""), ClusterColor(n)) };
        lines.AddRange(n.Describe().Split('\n').Select(l => (l, UiKit.Text)));
        string state = Owned(n.Id) ? "Owned (right-click to refund)"
            : Tree.CanAllocate(Profile.Allocated, n.Id) ? Profile.MetaPoints > 0 ? "Click to buy (1 point)" : "Need a meta point (win a map)"
            : "Not connected yet";
        lines.Add((state, UiKit.Dim));
        float w = lines.Max(l => _font.GetStringSize(l.Item1, HorizontalAlignment.Left, -1, 15).X) + 20;
        DrawRect(new Rect2(p, new Vector2(w, lines.Count * 20 + 10)), UiKit.PanelBg);
        float y = p.Y + 20;
        foreach (var (text, color) in lines)
        {
            DrawString(_font, new Vector2(p.X + 10, y), text, HorizontalAlignment.Left, -1, 15, color);
            y += 20;
        }
    }
}
