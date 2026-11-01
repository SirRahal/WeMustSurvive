using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.View;

/// <summary>
/// Draws everything that moves or changes: buildings, belt items, enemies, engineers, shots, and the build ghost.
/// Reads sim state every frame; never mutates it.
/// </summary>
public partial class WorldView : Node2D
{
    private const float UnitLift = 10f;
    private const int ShotFrames = 4;

    private static readonly Color EngineerColor = new(0.25f, 0.75f, 1f);
    private static readonly Color ShotColor = new(1f, 0.9f, 0.5f);
    private static readonly Color HpBack = new(0, 0, 0, 0.6f);
    private static readonly Color HpGood = new(0.3f, 0.9f, 0.4f);
    private static readonly Color HpBad = new(0.95f, 0.3f, 0.25f);
    private static readonly Color GhostOk = new(0.4f, 1f, 0.5f, 0.45f);
    private static readonly Color GhostBad = new(1f, 0.3f, 0.3f, 0.45f);
    private static readonly Color Hover = new(1f, 1f, 1f, 0.6f);
    private static readonly Color Blueprint = new(0.4f, 0.85f, 1f, 0.85f);
    private static readonly Color Scaffold = new(1f, 0.85f, 0.4f, 0.7f);
    private static readonly Color Selected = new(0.35f, 1f, 0.55f, 0.95f);
    private static readonly Color MoveLine = new(0.45f, 1f, 0.55f, 0.8f);
    private static readonly Color BuildLine = new(0.4f, 0.85f, 1f, 0.8f);
    private static readonly Color RepairLine = new(1f, 0.85f, 0.35f, 0.8f);
    private static readonly Color DeconstructTint = new(1f, 0.25f, 0.2f, 0.45f);

    private static readonly Color HintColor = new(1f, 0.85f, 0.25f);

    private readonly List<(Vector2 From, Vector2 To, int Frames)> _shots = new();

    /// <summary>Active tutorial, if any; its current step's hints are drawn on the map.</summary>
    public Tutorial? Tutorial { get; set; }
    private World? _world;
    private PlayerInput? _input;

    public void Bind(World world, PlayerInput input)
    {
        _world = world;
        _input = input;
        _shots.Clear();
    }

    /// <summary>Called after each sim step to pick up one-shot visual events.</summary>
    public void Consume(IEnumerable<SimEvent> events)
    {
        foreach (var e in events)
            if (e is ShotEvent s)
                _shots.Add((Iso.ToScreen(s.From) + new Vector2(0, -24), Iso.ToScreen(s.To) + new Vector2(0, -UnitLift), ShotFrames));
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world == null || _input == null) return;

        // Flat layer: belts and their items, flat blueprints, selection, hover, and order lines.
        foreach (var b in _world.Buildings)
        {
            if (b is Belt belt) DrawBelt(belt);
            else if (b is ConstructionSite { Started: false } plan) DrawBlueprint(plan);
        }
        DrawSelection();
        DrawHover();
        DrawOrders();

        // Depth-sorted layer: everything with height. Sort key = screen depth (grid x + y).
        var drawables = new List<(float Depth, Action Draw)>();
        foreach (var b in _world.Buildings)
        {
            if (b is ConstructionSite site)
            {
                if (site.Started) drawables.Add((b.Center.X + b.Center.Y, () => DrawSite(site)));
            }
            else if (b is not Belt)
            {
                drawables.Add((b.Center.X + b.Center.Y, () => DrawBuilding(b)));
            }
        }
        foreach (var e in _world.Enemies)
            drawables.Add((e.Pos.X + e.Pos.Y, () => DrawEnemy(e)));
        foreach (var eng in _world.Engineers)
            if (eng.Alive) drawables.Add((eng.Pos.X + eng.Pos.Y, () => DrawEngineer(eng)));
        drawables.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var d in drawables) d.Draw();

        DrawShots();
        DrawHints();
        DrawGhost();
    }

    /// <summary>
    /// Tutorial hints: each tile still to build pulses with the building's outline and facing,
    /// and a bouncing pointer marks the next one.
    /// </summary>
    private void DrawHints()
    {
        if (Tutorial is not { Finished: false } tut) return;
        float pulse = 0.55f + 0.45f * MathF.Sin((float)Time.GetTicksMsec() / 180f);
        bool pointed = false;

        foreach (var hint in tut.Current.Hints)
        {
            if (Tutorial.IsHintDone(_world!, hint)) continue;
            var t = hint.Tile;

            if (hint.IsMarker)
            {
                DrawSetTransform(Iso.ToScreen(t.Center), 0, new Vector2(1, 0.5f));
                DrawArc(Vector2.Zero, 18 + 4 * pulse, 0, Mathf.Tau, 32, HintColor with { A = pulse }, 3f, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else
            {
                var def = _world!.Data.Buildings[hint.Building];
                var d = Iso.Diamond(t.X, t.Y, def.Size, 0, 0.02f);
                DrawColoredPolygon(d, Iso.ParseColor(def.Color) with { A = 0.25f * pulse });
                DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, HintColor with { A = pulse }, 2.5f, true);
                if (Tutorial.IsDirectional(def)) DrawArrow(t, hint.Dir, HintColor with { A = 0.9f }, 2);
            }

            if (!pointed)
            {
                pointed = true;
                float bob = 6f * MathF.Sin((float)Time.GetTicksMsec() / 220f);
                var tip = Iso.ToScreen(t.Center) + new Vector2(0, -34 + bob);
                DrawColoredPolygon(new[] { tip, tip + new Vector2(-9, -14), tip + new Vector2(9, -14) }, HintColor);
                DrawRect(new Rect2(tip + new Vector2(-3, -28), new Vector2(6, 15)), HintColor);
            }
        }
    }

    // ------------------------------------------------------------------ buildings

    private void DrawBelt(Belt belt)
    {
        var o = belt.Origin;
        var color = Iso.ParseColor(belt.Def.Color);
        DrawColoredPolygon(Iso.Diamond(o.X, o.Y, 1, 0, 0.12f), color.Darkened(0.55f));
        DrawArrow(belt.Origin, belt.Dir, color.Darkened(0.2f), 0);

        var dir = belt.Dir.Offset();
        foreach (var it in belt.Items)
        {
            float t = it.Pos / (float)Belt.Length - 0.5f;
            var p = Iso.ToScreen(o.X + 0.5f + dir.X * t, o.Y + 0.5f + dir.Y * t) + new Vector2(0, -3);
            DrawCircle(p, 5f, Iso.ParseColor(_world!.Data.Items[it.Item].Color));
            DrawArc(p, 5f, 0, Mathf.Tau, 12, Colors.Black, 1f);
        }
    }

    private static float HeightOf(BuildingDef def) => def.Kind switch
    {
        BuildingKind.Belt => 2,
        BuildingKind.Inserter => 8,
        BuildingKind.Drill => 24,
        BuildingKind.Crafter => 28,
        BuildingKind.Wall => 22,
        BuildingKind.Turret => 18,
        BuildingKind.Nexus => 46,
        _ => 10,
    };

    /// <summary>A paid-for site nobody has started: a translucent holographic outline.</summary>
    private void DrawBlueprint(ConstructionSite site)
    {
        var o = site.Origin;
        var fill = Blueprint with { A = 0.18f + 0.08f * MathF.Sin((float)Time.GetTicksMsec() / 250f) };
        DrawColoredPolygon(Iso.Diamond(o.X, o.Y, site.Size, 0, 0.06f), fill);
        var top = Iso.Diamond(o.X, o.Y, site.Size, HeightOf(site.Def), 0.06f);
        DrawPolyline(new[] { top[0], top[1], top[2], top[3], top[0] }, Blueprint, 1.2f);
        var floor = Iso.Diamond(o.X, o.Y, site.Size, 0, 0.06f);
        for (int i = 1; i < 4; i++) DrawLine(floor[i], top[i], Blueprint with { A = 0.6f }, 1f);
        if (site.Def.Kind is BuildingKind.Belt or BuildingKind.Inserter or BuildingKind.Drill)
            DrawArrow(o, site.Dir, Blueprint, 1);
    }

    /// <summary>A site under construction: the building rises with progress, wrapped in scaffold lines.</summary>
    private void DrawSite(ConstructionSite site)
    {
        var o = site.Origin;
        float full = HeightOf(site.Def);
        float h = Math.Max(3, full * site.Fraction);
        Iso.DrawBox(this, o.X, o.Y, site.Size, h, Iso.ParseColor(site.Def.Color).Darkened(0.25f) with { A = 0.9f });
        var floor = Iso.Diamond(o.X, o.Y, site.Size, 0, 0.04f);
        var top = Iso.Diamond(o.X, o.Y, site.Size, full, 0.04f);
        DrawPolyline(new[] { top[0], top[1], top[2], top[3], top[0] }, Scaffold, 1f);
        for (int i = 0; i < 4; i++) DrawLine(floor[i], top[i], Scaffold, 1f);
        DrawProgressBar(Iso.ToScreen(site.Center) + new Vector2(0, -full - 10), 34 * site.Size, site.Fraction, Blueprint);
        if (site.Hp < site.Def.MaxHp * site.Fraction)
            DrawHpBar(Iso.ToScreen(site.Center) + new Vector2(0, -full - 16), 34 * site.Size, site.Hp / site.Def.MaxHp);
    }

    private void DrawBuilding(Building b)
    {
        var color = Iso.ParseColor(b.Def.Color);
        float height = HeightOf(b.Def);
        float inset = b is Inserter ? 0.3f : 0.08f;
        Iso.DrawBox(this, b.Origin.X, b.Origin.Y, b.Size, height, color, inset);

        switch (b)
        {
            case Inserter ins:
                DrawInserterArm(ins, height);
                break;
            case Drill:
                DrawArrow(b.Origin, b.Dir, Colors.White with { A = 0.7f }, height);
                break;
            case Crafter { Active: not null }:
                DrawColoredPolygon(Iso.Diamond(b.Origin.X, b.Origin.Y, 1, height, 0.32f), new Color(1f, 0.55f, 0.15f));
                break;
            case Turret:
                var top = Iso.ToScreen(b.Center) + new Vector2(0, -height);
                DrawCircle(top + new Vector2(0, -6), 9f, color.Lightened(0.2f));
                DrawCircle(top + new Vector2(0, -6), 4f, color.Darkened(0.4f));
                break;
            case Nexus:
                var core = Iso.ToScreen(b.Center) + new Vector2(0, -height - 14);
                float pulse = 0.6f + 0.4f * MathF.Sin((float)Time.GetTicksMsec() / 300f);
                DrawCircle(core, 12f, color.Lightened(0.4f) with { A = pulse });
                break;
        }

        if (b.Hp < b.Def.MaxHp)
            DrawHpBar(Iso.ToScreen(b.Center) + new Vector2(0, -height - 10), 34 * b.Size, b.Hp / b.Def.MaxHp);
    }

    private void DrawInserterArm(Inserter ins, float height)
    {
        float t = ins.State switch
        {
            Inserter.Phase.Swinging => ins.Progress / (float)ins.Def.SwingTicks,
            Inserter.Phase.Returning => 1f - ins.Progress / (float)ins.Def.SwingTicks,
            _ => 0f,
        };
        var c = ins.Origin.Center;
        var d = ins.Dir.Offset();
        var basePt = Iso.ToScreen(c) + new Vector2(0, -height);
        var hand = Iso.ToScreen(c.X + d.X * (t - 0.5f) * 0.9f, c.Y + d.Y * (t - 0.5f) * 0.9f) + new Vector2(0, -height - 6);
        DrawLine(basePt, hand, Iso.ParseColor(ins.Def.Color).Lightened(0.3f), 3f);
        if (ins.Held != null)
            DrawCircle(hand, 4.5f, Iso.ParseColor(_world!.Data.Items[ins.Held].Color));
        if (ins.Filter != null)
            DrawCircle(basePt, 3f, Iso.ParseColor(_world!.Data.Items[ins.Filter].Color));
    }

    private void DrawArrow(Int2 tile, Direction dir, Color color, float lift)
    {
        var c = tile.Center;
        var d = dir.Offset();
        var side = new SVec(-d.Y, d.X);
        var up = new Vector2(0, -lift);
        var tip = Iso.ToScreen(c.X + d.X * 0.3f, c.Y + d.Y * 0.3f) + up;
        var l = Iso.ToScreen(c.X - d.X * 0.15f + side.X * 0.2f, c.Y - d.Y * 0.15f + side.Y * 0.2f) + up;
        var r = Iso.ToScreen(c.X - d.X * 0.15f - side.X * 0.2f, c.Y - d.Y * 0.15f - side.Y * 0.2f) + up;
        DrawColoredPolygon(new[] { tip, l, r }, color);
    }

    // ------------------------------------------------------------------ units

    private void DrawEnemy(Enemy e)
    {
        var ground = Iso.ToScreen(e.Pos);
        float r = e.Def.Radius * Iso.TileW * 0.55f;
        var color = Iso.ParseColor(e.Def.Color);
        DrawSetTransform(ground, 0, new Vector2(1, 0.5f));
        DrawCircle(Vector2.Zero, r, new Color(0, 0, 0, 0.35f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        var body = ground + new Vector2(0, -UnitLift);
        DrawCircle(body, r, color);
        DrawCircle(body + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.25f, Colors.White with { A = 0.8f });
        if (e.Hp < e.MaxHp)
            DrawHpBar(body + new Vector2(0, -r - 6), 22, e.Hp / e.MaxHp);
    }

    private void DrawEngineer(Engineer eng)
    {
        var ground = Iso.ToScreen(eng.Pos);
        // Selection ring: the engineer is the player's unit, so it is always "selected".
        if (eng.PlayerId == Main.LocalPlayer)
        {
            DrawSetTransform(ground, 0, new Vector2(1, 0.5f));
            DrawArc(Vector2.Zero, 15, 0, Mathf.Tau, 32, Selected, 2f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        DrawSetTransform(ground, 0, new Vector2(1, 0.5f));
        DrawCircle(Vector2.Zero, 10, new Color(0, 0, 0, 0.35f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawRect(new Rect2(ground + new Vector2(-6, -24), new Vector2(12, 18)), EngineerColor.Darkened(0.2f));
        DrawCircle(ground + new Vector2(0, -28), 7, EngineerColor);
        if (eng.Hp < Engineer.MaxHp)
            DrawHpBar(ground + new Vector2(0, -42), 26, eng.Hp / Engineer.MaxHp);

        // Welding sparks toward whatever it's building or repairing.
        if (eng.WorkingOn is int id && _world!.BuildingById(id) is { } target)
        {
            var tool = ground + new Vector2(0, -18);
            var hit = Iso.ToScreen(target.Center) + new Vector2(0, -8);
            float flicker = (Time.GetTicksMsec() / 60) % 2 == 0 ? 1f : 0.5f;
            DrawLine(tool, hit, Scaffold with { A = flicker }, 2f);
            DrawCircle(hit, 3f * flicker + 1, Colors.White with { A = flicker });
        }
    }

    private void DrawShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            DrawLine(s.From, s.To, ShotColor with { A = s.Frames / (float)ShotFrames }, 2f);
            if (--s.Frames <= 0) _shots.RemoveAt(i);
            else _shots[i] = s;
        }
    }

    private void DrawHpBar(Vector2 center, float width, float frac) =>
        DrawProgressBar(center, width, frac, frac > 0.4f ? HpGood : HpBad);

    private void DrawProgressBar(Vector2 center, float width, float frac, Color color)
    {
        var rect = new Rect2(center - new Vector2(width / 2, 2), new Vector2(width, 4));
        DrawRect(rect, HpBack);
        DrawRect(new Rect2(rect.Position, new Vector2(width * Math.Clamp(frac, 0, 1), 4)), color);
    }

    // ------------------------------------------------------------------ input overlays

    private void DrawHover()
    {
        var t = _input!.HoverTile;
        if (!_world!.Map.InBounds(t)) return;
        if (_input.DeconstructTool && _world.BuildingAt(t) is { } doomed and not Nexus)
        {
            DrawColoredPolygon(Iso.Diamond(doomed.Origin.X, doomed.Origin.Y, doomed.Size), DeconstructTint);
            return;
        }
        var d = Iso.Diamond(t.X, t.Y, 1);
        DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, Hover, 1.5f);
    }

    private void DrawSelection()
    {
        if (_input!.SelectedBuilding is not { } b) return;
        var d = Iso.Diamond(b.Origin.X, b.Origin.Y, b.Size, 0, -0.04f);
        DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, Selected, 2.5f, true);
        if (b is Turret or Nexus)
            Iso.DrawGroundEllipse(this, b.Center, b.Def.Range, Selected with { A = 0.35f });
    }

    /// <summary>
    /// The engineer's order queue as lines on the ground: the live path to the current target, then
    /// straight dashes between queued targets. In co-op both players see these.
    /// </summary>
    private void DrawOrders()
    {
        foreach (var eng in _world!.Engineers)
        {
            if (!eng.Alive || eng.Orders.Count == 0) continue;
            var from = eng.Pos;
            for (int i = 0; i < eng.Orders.Count; i++)
            {
                var (target, color) = OrderTarget(eng.Orders[i]);
                if (target is not SVec to) continue;

                if (i == 0 && eng.Path.Count > 0)
                {
                    var pts = new List<Vector2> { Iso.ToScreen(from) };
                    pts.AddRange(eng.Path.Select(p => Iso.ToScreen(p)));
                    DrawPolyline(pts.ToArray(), color, 2f, true);
                    from = eng.Path[^1];
                }
                if (i > 0 || eng.Path.Count == 0)
                    DrawDashedLine(Iso.ToScreen(from), Iso.ToScreen(to), color with { A = 0.6f }, 1.5f, 8f);

                DrawSetTransform(Iso.ToScreen(to), 0, new Vector2(1, 0.5f));
                DrawArc(Vector2.Zero, 7, 0, Mathf.Tau, 16, color, 2f, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                from = to;
            }
        }
    }

    private (SVec? Target, Color Color) OrderTarget(EngineerOrder order) => order switch
    {
        MoveOrder m => (m.Target, MoveLine),
        BuildOrder b => (_world!.BuildingById(b.SiteId)?.Center, BuildLine),
        RepairOrder r => (_world!.BuildingById(r.BuildingId)?.Center, RepairLine),
        _ => (null, MoveLine),
    };

    private void DrawGhost()
    {
        var def = _input!.BuildTool;
        if (def == null) return;
        var world = _world!;

        var t = _input.HoverTile;
        bool ok = world.CheckPlacement(Main.LocalPlayer, def, t) == null && world.Storage.HasAll(def.Cost);
        var tint = ok ? GhostOk : GhostBad;
        DrawColoredPolygon(Iso.Diamond(t.X, t.Y, def.Size, 0, 0.05f), tint);
        if (def.Kind is BuildingKind.Belt or BuildingKind.Inserter or BuildingKind.Drill)
            DrawArrow(t, _input.GhostDir, tint with { A = 0.9f }, 1);
        if (def.Kind == BuildingKind.Turret)
            Iso.DrawGroundEllipse(this, new SVec(t.X + 0.5f, t.Y + 0.5f), def.Range, tint);
    }
}
