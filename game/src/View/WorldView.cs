using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.View;

/// <summary>
/// Draws everything that moves or changes: buildings, belt items, enemies, engineers, shots, explosions,
/// order lines, pings and drawings, power coverage, and the build ghost. Reads sim state; never mutates it.
/// </summary>
public partial class WorldView : Node2D
{
    private const float UnitLift = 10f;
    private const double PingSeconds = 5;
    private const double DrawingSeconds = 30;

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
    private static readonly Color DeconstructTint = new(1f, 0.25f, 0.2f, 0.45f);
    private static readonly Color HintColor = new(1f, 0.85f, 0.25f);
    private static readonly Color PowerArea = new(0.3f, 0.7f, 1f, 0.10f);
    private static readonly Color Wire = new(0.85f, 0.65f, 0.3f, 0.7f);
    private static readonly Color NoPower = new(1f, 0.25f, 0.2f);

    private sealed record Shot(Vector2 From, Vector2 To, string Kind, int Frames, int Total);
    private sealed record Blast(SVec Pos, float Radius, string Kind, double Start, double Duration);
    private sealed record Ping(int Player, PingKind Kind, SVec Pos, double Start);
    private sealed record Drawing(int Player, SVec[] Points, double Start);

    private readonly List<Shot> _shots = new();
    private readonly List<Blast> _blasts = new();
    private readonly List<Ping> _pings = new();
    private readonly List<Drawing> _drawings = new();
    private readonly List<(SVec Pos, float Radius, double Lands, string Kind)> _incoming = new();
    private Font _font = null!;
    private World? _world;
    private PlayerInput? _input;

    /// <summary>Active tutorial, if any; its current step's hints are drawn on the map.</summary>
    public Tutorial? Tutorial { get; set; }
    /// <summary>Logistics buildings whose items never arrive anywhere (refreshed a few times a second).</summary>
    public Dictionary<int, LogisticsIssue> Issues { get; set; } = new();

    public static Color PlayerColor(int player) => player == 0 ? new Color(0.25f, 0.75f, 1f) : new Color(1f, 0.6f, 0.2f);

    private static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready() => _font = ThemeDB.FallbackFont;

    public void Bind(World world, PlayerInput input)
    {
        _world = world;
        _input = input;
        Issues = new();
        _shots.Clear();
        _blasts.Clear();
        _pings.Clear();
        _drawings.Clear();
        _incoming.Clear();
        _transfers.Clear();
    }

    /// <summary>Called after each sim step to pick up one-shot visual events.</summary>
    public void Consume(IEnumerable<SimEvent> events)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case ShotEvent s:
                    int frames = s.Kind switch { "rocket" => 10, "heavy" => 12, "laser" => 5, "flame" => 6, "acid" => 8, _ => 4 };
                    _shots.Add(new Shot(Iso.ToScreen(s.From) + new Vector2(0, -22), Iso.ToScreen(s.To) + new Vector2(0, -UnitLift), s.Kind, frames, frames));
                    if (s.Kind is "rocket" or "heavy") _blasts.Add(new Blast(s.To, s.Kind == "heavy" ? 2f : 1.6f, s.Kind, Now + frames / 60.0, 0.35));
                    break;
                case ExplosionEvent x:
                    _blasts.Add(new Blast(x.Pos, x.Radius, x.Kind, Now, x.Kind == "nuke" ? 2.5 : x.Kind == "orbital" ? 1.2 : 0.5));
                    _incoming.RemoveAll(i => i.Pos == x.Pos);
                    break;
                case StrikeIncomingEvent si:
                    _incoming.Add((si.Pos, si.Radius, Now + si.DelayTicks / 60.0, si.Kind));
                    break;
                case PingEvent p:
                    _pings.Add(new Ping(p.PlayerId, p.Kind, p.Pos, Now));
                    break;
                case DrawEvent d:
                    _drawings.Add(new Drawing(d.PlayerId, d.Points, Now));
                    break;
                case ClearDrawingsEvent c:
                    _drawings.RemoveAll(d => d.Player == c.PlayerId);
                    break;
                case ItemStoredEvent st:
                    _transfers.Add(new Transfer(st.From, st.Storage, st.Item, true, Now));
                    break;
                case ItemTakenEvent tk:
                    _transfers.Add(new Transfer(tk.To, tk.Storage, tk.Item, false, Now));
                    break;
            }
        }
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world == null || _input == null) return;
        double now = Now;
        _pings.RemoveAll(p => now - p.Start > PingSeconds);
        _drawings.RemoveAll(d => now - d.Start > DrawingSeconds);
        _blasts.RemoveAll(b => now - b.Start > b.Duration);

        // Flat layer: power coverage, belts and their items, flat blueprints, selection, hover, and order lines.
        DrawPowerCoverage();
        foreach (var b in _world.Buildings)
        {
            if (b is Belt belt) DrawBelt(belt);
            else if (b is ConstructionSite { Started: false } plan) DrawBlueprint(plan);
            else if (b is Splitter or TunnelEntrance or TunnelExit) DrawFlatLogistics(b);
        }
        DrawSelection();
        DrawHover();
        DrawOrders();
        DrawDrawings(now);

        // Depth-sorted layer: everything with height. Sort key = screen depth at the current view angle.
        var drawables = new List<(float Depth, Action Draw)>();
        foreach (var b in _world.Buildings)
        {
            if (b is ConstructionSite site)
            {
                if (site.Started) drawables.Add((Iso.Depth(b.Center), () => DrawSite(site)));
            }
            else if (b is not (Belt or Splitter or TunnelEntrance or TunnelExit))
            {
                drawables.Add((Iso.Depth(b.Center), () => DrawBuilding(b)));
            }
        }
        foreach (var e in _world.Enemies)
            drawables.Add((Iso.Depth(e.Pos), () => DrawEnemy(e)));
        foreach (var eng in _world.Engineers)
            if (eng.Alive) drawables.Add((Iso.Depth(eng.Pos), () => DrawEngineer(eng)));
        drawables.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var d in drawables) d.Draw();

        DrawPowerWires();
        DrawLogisticsIssues();
        DrawShots();
        DrawTransfers(now);
        DrawBlasts(now);
        DrawIncoming(now);
        DrawPings(now);
        DrawHints();
        DrawGhost();
    }

    // ------------------------------------------------------------------ buildings

    /// <summary>Prop Studio sprites; buildings without one fall back to boxes.</summary>
    private static BuildingSprites Sprites => BuildingSprites.Shared;

    private static float HeightOf(BuildingDef def) => def.Kind switch
    {
        BuildingKind.Belt => 2,
        BuildingKind.Inserter => 8,
        BuildingKind.Drill => 24,
        BuildingKind.Crafter => 28,
        BuildingKind.Wall => def.Id switch { "fortress_wall" => 34, "heavy_wall" => 30, "reinforced_wall" => 26, _ => 22 },
        BuildingKind.Turret => 18,
        BuildingKind.Nexus => 46,
        BuildingKind.Generator => 26,
        BuildingKind.Pole => 6,
        BuildingKind.Lab => 22,
        BuildingKind.Depot => 20,
        BuildingKind.SuperWeapon => 30,
        _ => 6,
    };

    private void DrawBelt(Belt belt)
    {
        var o = belt.Origin;
        var color = Iso.ParseColor(belt.Def.Color);
        if (!Sprites.Draw(this, BeltSpriteId(belt), Iso.ToScreen(belt.Center), belt.Dir))
        {
            DrawColoredPolygon(Iso.Diamond(o.X, o.Y, 1, 0, 0.12f), color.Darkened(0.55f));
            DrawArrow(belt.Origin, belt.Dir, color.Darkened(0.2f), 0);
        }

        // Items ride the belt's own path: straight through, or curving in from the side on a corner piece.
        var side = CornerSide(belt);
        foreach (var it in belt.Items)
        {
            var p = Iso.ToScreen(BeltPoint(belt, side, it.Pos / (float)Belt.Length)) + new Vector2(0, -ItemLift);
            DrawItem(p, it.Item, 5f);
        }
        if (belt.Hp < belt.MaxHp) DrawHpBar(Iso.ToScreen(belt.Center) + new Vector2(0, -10), 30, belt.Hp / belt.MaxHp);
    }

    /// <summary>Height of items above the belt surface, in screen pixels.</summary>
    private const float ItemLift = 4;

    /// <summary>
    /// The side a corner belt is fed from (only one side feeds it, nothing from behind), or null for a straight run.
    /// </summary>
    private Direction? CornerSide(Belt belt)
    {
        var w = _world!;
        bool Feeds(Direction side) => w.BuildingAt(belt.Origin + side.Offset()) is Belt n && n.Front == belt.Origin;
        var right = belt.Dir.RotateCw();
        var left = right.Opposite();
        if (Feeds(belt.Dir.Opposite())) return null;
        if (Feeds(left) && !Feeds(right)) return left;
        if (Feeds(right) && !Feeds(left)) return right;
        return null;
    }

    /// <summary>Belt sprite id: the corner piece named for the side it's fed from (looking the way it runs), or the straight one.</summary>
    private string BeltSpriteId(Belt belt) => CornerSide(belt) is { } side
        ? belt.Def.Id + (side == belt.Dir.RotateCw() ? "_turn_right" : "_turn_left")
        : belt.Def.Id;

    /// <summary>
    /// Grid point at <paramref name="t"/> (0 = where items enter, 1 = the front edge) along a belt: a straight line
    /// from the back edge, or for a corner a quarter curve from the side edge through the tile centre's pull.
    /// </summary>
    private static SVec BeltPoint(Belt belt, Direction? side, float t)
    {
        var c = belt.Center;
        var d = belt.Dir.Offset();
        var front = c + new SVec(d.X, d.Y) * 0.5f;
        if (side is not { } s) return SVec.Lerp(c - new SVec(d.X, d.Y) * 0.5f, front, t);
        var so = s.Offset();
        var entry = c + new SVec(so.X, so.Y) * 0.5f;
        float u = 1 - t;
        return entry * (u * u) + c * (2 * u * t) + front * (t * t);
    }

    /// <summary>An item: its Prop Studio sprite (sized by <paramref name="r"/>; 5 = normal), or a coloured dot without one.</summary>
    private void DrawItem(Vector2 p, string item, float r, float alpha = 1)
    {
        if (Sprites.DrawItem(this, item, p, r / 5f * 1.15f, Colors.White with { A = alpha })) return;
        DrawCircle(p, r, Iso.ParseColor(_world!.Data.Items[item].Color) with { A = alpha });
        DrawArc(p, r, 0, Mathf.Tau, 12, Colors.Black with { A = alpha }, 1f);
    }

    // ------------------------------------------------------------------ storage animations

    /// <summary>An item going into (or coming out of) the Nexus or a depot; <paramref name="Point"/> is the belt edge or inserter.</summary>
    private sealed record Transfer(SVec Point, Int2 Storage, string Item, bool In, double Start);

    private readonly List<Transfer> _transfers = new();
    private const double InSeconds = 0.5, OutSeconds = 0.4, RingSeconds = 0.35;
    private static readonly Color Core = new("#3BD4F0");

    /// <summary>Where items vanish into / appear from: high up in the storage building.</summary>
    private static Vector2 CorePoint(Building storage) =>
        Iso.ToScreen(storage.Center) + new Vector2(0, storage is Nexus ? -62 : -22);

    /// <summary>
    /// Items arc up into the Nexus core, shrinking with a cyan trail, and the core answers with a ring pulse; items an
    /// inserter takes out pop from the core with a small burst and arc down to the inserter's hand.
    /// </summary>
    private void DrawTransfers(double now)
    {
        _transfers.RemoveAll(t => now - t.Start > (t.In ? InSeconds + RingSeconds : OutSeconds) || _world!.BuildingAt(t.Storage) == null);
        foreach (var t in _transfers)
        {
            var storage = _world!.BuildingAt(t.Storage)!;
            var core = CorePoint(storage);
            var ground = Iso.ToScreen(t.Point) + new Vector2(0, t.In ? -ItemLift : -14);
            double age = now - t.Start, dur = t.In ? InSeconds : OutSeconds;
            if (age < dur)
            {
                float k = (float)(age / dur);
                float e = t.In ? k * k : 1 - (1 - k) * (1 - k); // in: accelerates into the core; out: pops then settles
                var (a, b) = t.In ? (ground, core) : (core, ground);
                var ctrl = (a + b) / 2 + new Vector2(0, -36);
                Vector2 At(float x) => a * ((1 - x) * (1 - x)) + ctrl * (2 * (1 - x) * x) + b * (x * x);
                float scale = t.In ? Mathf.Lerp(1f, 0.35f, e) : Mathf.Lerp(0.35f, 1f, e);
                for (int i = 3; i >= 1; i--)
                    DrawCircle(At(Math.Max(0, e - 0.07f * i)), 4 * scale, Core with { A = 0.18f / i });
                DrawCircle(At(e), 9 * scale, Core with { A = 0.22f });
                DrawItem(At(e), t.Item, 5 * scale, t.In ? 1 - 0.5f * e : 0.5f + 0.5f * e);
                if (!t.In && age < 0.2) Ring(core, (float)(age / 0.2), 4, 16);
            }
            else if (t.In) Ring(core, (float)((age - dur) / RingSeconds), 6, 26);
        }
    }

    /// <summary>A flat cyan ring expanding from <paramref name="from"/> to <paramref name="to"/> pixels as <paramref name="k"/> goes 0..1, fading out.</summary>
    private void Ring(Vector2 c, float k, float from, float to)
    {
        DrawSetTransform(c, 0, new Vector2(1, 0.5f));
        float r = Mathf.Lerp(from, to, k);
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 32, Core with { A = 0.9f * (1 - k) }, 2, true);
        DrawCircle(Vector2.Zero, r * 0.6f, Core with { A = 0.18f * (1 - k) });
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Splitters and tunnel ends sit flat on the ground like belts.</summary>
    private void DrawFlatLogistics(Building b)
    {
        var o = b.Origin;
        var color = Iso.ParseColor(b.Def.Color);
        bool sprite = Sprites.Draw(this, b.Def.Id, Iso.ToScreen(b.Center), b.Dir);
        if (!sprite)
        {
            DrawColoredPolygon(Iso.Diamond(o.X, o.Y, 1, 0, 0.08f), color.Darkened(0.35f));
            var top = Iso.Diamond(o.X, o.Y, 1, 4, 0.18f);
            DrawColoredPolygon(top, color.Darkened(b is TunnelEntrance or TunnelExit ? 0.6f : 0.1f));
        }
        switch (b)
        {
            case Splitter when sprite:
                break;
            case Splitter:
                DrawArrow(o, b.Dir, Colors.White with { A = 0.8f }, 5);
                DrawArrow(o, b.Dir.RotateCw(), Colors.White with { A = 0.5f }, 5);
                DrawArrow(o, b.Dir.RotateCw().Opposite(), Colors.White with { A = 0.5f }, 5);
                break;
            case TunnelEntrance tin:
                if (!sprite) DrawArrow(o, b.Dir, Colors.White with { A = 0.8f }, 5);
                if (tin.FindExit(_world!) is { } exit)
                    DrawDashedLine(Iso.ToScreen(tin.Center), Iso.ToScreen(exit.Center), color with { A = 0.6f }, 2f, 6f);
                break;
            default:
                if (!sprite) DrawArrow(o, b.Dir, Colors.White with { A = 0.8f }, 5);
                break;
        }
        if (b.Hp < b.MaxHp) DrawHpBar(Iso.ToScreen(b.Center) + new Vector2(0, -12), 30, b.Hp / b.MaxHp);
    }

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
        if (Tutorial.IsDirectional(site.Def) || site.Def.Kind is BuildingKind.Splitter or BuildingKind.TunnelEntrance or BuildingKind.TunnelExit)
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
        if (site.Hp < site.MaxHp * site.Fraction)
            DrawHpBar(Iso.ToScreen(site.Center) + new Vector2(0, -full - 16), 34 * site.Size, site.Hp / site.MaxHp);
    }

    private void DrawBuilding(Building b)
    {
        var color = Iso.ParseColor(b.Def.Color);
        float height = HeightOf(b.Def);
        float inset = b is Inserter ? 0.3f : b is PowerPole ? 0.4f : 0.08f;
        // Prop Studio sprite if there is one (the Nexus sprite's facing is its dock, at the back); otherwise a box.
        var turretAim = b is Turret aimer && _world!.NearestEnemy(aimer.Center, aimer.Range(_world)) is { } foe ? foe.Pos - aimer.Center : (SVec?)null;
        bool sprite = Sprites.Draw(this, b.Def.Id, Iso.ToScreen(b.Center), b is Nexus ? Direction.North : b.Dir, turretAim);
        if (!sprite && b is not PowerPole) Iso.DrawBox(this, b.Origin.X, b.Origin.Y, b.Size, height, color, inset);
        var top = Iso.ToScreen(b.Center) + new Vector2(0, -height);
        float t = (float)Time.GetTicksMsec() / 1000f;

        // With a sprite, only live overlays are drawn (ammo, smoke, fill); the sprite already shows the rest.
        switch (b)
        {
            case Inserter ins when !sprite:
                DrawInserterArm(ins, height);
                break;
            case Drill d when !sprite:
                DrawArrow(b.Origin, b.Dir, Colors.White with { A = 0.7f }, height);
                if (d.Status.StartsWith("Mining"))
                    DrawLine(top + new Vector2(-6, -3), top + new Vector2(6, 3).Rotated(t * 8), Colors.White with { A = 0.6f }, 2);
                DrawOreGauge(d, top);
                break;
            case Crafter { Active: not null } c when !sprite:
                DrawColoredPolygon(Iso.Diamond(b.Origin.X, b.Origin.Y, 1, height, 0.32f), c.Def.Id == "workshop" ? new Color(0.9f, 0.85f, 0.3f) : new Color(1f, 0.55f, 0.15f));
                break;
            case Turret tu:
                DrawTurretHead(tu, top, color, barrel: !sprite);
                break;
            case Nexus when !sprite:
                float pulse = 0.6f + 0.4f * MathF.Sin(t * 3.3f);
                DrawCircle(top + new Vector2(0, -14), 12f, color.Lightened(0.4f) with { A = pulse });
                break;
            case Generator g:
                if (!sprite) DrawRect(new Rect2(top + new Vector2(6, -16), new Vector2(6, 16)), color.Darkened(0.4f));
                if (g.HasFuel && g.Load > 0)
                    for (int i = 0; i < 3; i++)
                        DrawCircle(top + new Vector2(9 + i * 2, -20 - ((t * 20 + i * 7) % 18)), 3 + i, new Color(0.6f, 0.6f, 0.65f, 0.4f));
                break;
            case PowerPole when !sprite:
                var foot = Iso.ToScreen(b.Center);
                DrawLine(foot, foot + new Vector2(0, -34), color.Darkened(0.3f), 3);
                DrawLine(foot + new Vector2(-8, -30), foot + new Vector2(8, -30), color, 2);
                break;
            case Lab lab when !sprite:
                var glow = lab.Working ? new Color(0.5f, 0.8f, 1f, 0.6f + 0.4f * MathF.Sin(t * 4)) : new Color(0.4f, 0.45f, 0.5f);
                DrawCircle(top + new Vector2(0, -6), 7, glow);
                break;
            case Depot dep:
                if (!sprite) DrawLine(top + new Vector2(-14, 0), top + new Vector2(14, 0), color.Lightened(0.3f), 2);
                DrawProgressBar(top + new Vector2(0, -6), 26, dep.Storage.Total / (float)dep.Def.Capacity, new Color(0.8f, 0.8f, 0.5f));
                break;
            case SuperWeapon sw:
                float charge = sw.Charge / sw.ChargeNeeded(_world!);
                bool ready = charge >= 1f;
                DrawCircle(top + new Vector2(0, -10), 16, color.Darkened(0.3f));
                DrawArc(top + new Vector2(0, -10), 18, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * charge, 32,
                    ready ? HintColor with { A = 0.6f + 0.4f * MathF.Sin(t * 6) } : new Color(0.6f, 0.9f, 1f), 3, true);
                if (sw.Def.Id == "nuclear_silo") DrawColoredPolygon(new[] { top + new Vector2(0, -26), top + new Vector2(-6, -8), top + new Vector2(6, -8) }, new Color(0.9f, 0.9f, 0.8f));
                else DrawArc(top + new Vector2(0, -16), 10, Mathf.Pi, Mathf.Tau, 16, Colors.White, 2, true);
                break;
        }

        if (b.NeedsPower && b.PowerFactor <= 0 && (int)(t * 2) % 2 == 0)
            DrawPowerWarning(top + new Vector2(0, -24));
        if (b.Hp < b.MaxHp)
            DrawHpBar(Iso.ToScreen(b.Center) + new Vector2(0, -height - 10), 34 * b.Size, b.Hp / b.MaxHp);
    }

    /// <param name="barrel">False when a sprite already shows the head (it picks the frame nearest the aim).</param>
    private void DrawTurretHead(Turret tu, Vector2 top, Color color, bool barrel = true)
    {
        var head = top + new Vector2(0, -6);
        if (barrel)
        {
            var target = _world!.NearestEnemy(tu.Center, tu.Range(_world));
            var aim = target != null ? (Iso.ToScreen(target.Pos) - head).Normalized() : new Vector2(1, 0.5f).Normalized();
            float len = tu.Def.Id switch { "heavy_turret" => 22, "rocket_turret" => 16, "flame_turret" => 12, _ => 14 };
            float width = tu.Def.Id switch { "heavy_turret" => 6, "rocket_turret" => 5, "laser_turret" => 3, _ => 4 };
            DrawCircle(head, 9f, color.Lightened(0.2f));
            DrawLine(head, head + aim * len, color.Darkened(0.5f), width);
            if (tu.Def.Id == "laser_turret") DrawCircle(head, 4f, new Color(1f, 0.3f, 0.5f, tu.PowerFactor > 0 ? 1f : 0.3f));
        }
        if (tu.UsesAmmo && tu.Ammo == 0 && tu.Rounds == 0)
            DrawString(_font, head + new Vector2(-20, -14), "NO AMMO", HorizontalAlignment.Center, 40, 10, HpBad);
        if (tu.UsesAmmo)
        {
            // Ammo storage: one pip per magazine/rocket/shell it can hold.
            int cap = tu.Def.AmmoCapacity;
            float pip = Math.Min(6f, 30f / cap), gap = 1.5f;
            var start = head + new Vector2(-(cap * (pip + gap) - gap) / 2, 14);
            var ammoColor = Iso.ParseColor(_world!.Data.Items[tu.Def.AmmoItem!].Color);
            for (int i = 0; i < cap; i++)
                DrawRect(new Rect2(start + new Vector2(i * (pip + gap), 0), new Vector2(pip, 3)),
                    i < tu.Ammo ? ammoColor : new Color(0, 0, 0, 0.5f));
        }
    }

    private void DrawPowerWarning(Vector2 p)
    {
        DrawColoredPolygon(new[] { p + new Vector2(0, -9), p + new Vector2(-8, 6), p + new Vector2(8, 6) }, NoPower);
        DrawString(_font, p + new Vector2(-5, 5), "!", HorizontalAlignment.Center, 10, 12, Colors.White);
    }

    private void DrawInserterArm(Inserter ins, float height)
    {
        float t = ins.State switch
        {
            Inserter.Phase.Swinging => ins.Progress / ins.Def.SwingTicks,
            Inserter.Phase.Returning => 1f - ins.Progress / ins.Def.SwingTicks,
            _ => 0f,
        };
        t = Math.Clamp(t, 0f, 1f);
        var c = ins.Origin.Center;
        // The arm sweeps around the base from the side it took from to the side it drops on: a straight
        // half-turn for back-to-front, a quarter-turn for side inputs.
        var from = ins.State == Inserter.Phase.Idle ? Inserter.SideDir(ins.Dir, ins.Input) : ins.TookFrom;
        float a0 = Angle(from), a1 = Angle(ins.Dir);
        float delta = Mathf.Wrap(a1 - a0, -Mathf.Pi, Mathf.Pi);
        if (Mathf.Abs(Mathf.Abs(delta) - Mathf.Pi) < 0.01f) delta = Mathf.Pi; // straight through: always swing the same way
        float a = a0 + delta * t;
        var basePt = Iso.ToScreen(c) + new Vector2(0, -height);
        var hand = Iso.ToScreen(c.X + MathF.Cos(a) * 0.45f, c.Y + MathF.Sin(a) * 0.45f) + new Vector2(0, -height - 6);
        DrawLine(basePt, hand, Iso.ParseColor(ins.Def.Color).Lightened(0.3f), 3f);
        if (ins.Held != null) DrawItem(hand, ins.Held, 4.5f);
        if (ins.Filter != null) DrawCircle(basePt, 3f, Iso.ParseColor(_world!.Data.Items[ins.Filter].Color));
    }

    /// <summary>Grid-space angle of a direction (East = 0, South = +90°).</summary>
    private static float Angle(Direction d)
    {
        var o = d.Offset();
        return MathF.Atan2(o.Y, o.X);
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

    // ------------------------------------------------------------------ power

    /// <summary>When placing anything electrical, show every pole's supply area.</summary>
    private void DrawPowerCoverage()
    {
        var tool = _input!.BuildTool;
        bool show = tool != null && (tool.PowerDraw > 0 || tool.Kind is BuildingKind.Pole or BuildingKind.Generator)
                    || _input.SelectedBuilding is { } sel && (sel.NeedsPower || sel is PowerPole or Generator);
        if (!show) return;
        foreach (var n in _world!.Buildings.Where(b => b.Def.SupplyRadius > 0 && b is not ConstructionSite))
        {
            float r = n.Def.SupplyRadius;
            var c = n.Center;
            var d = new[]
            {
                Iso.ToScreen(c.X - r, c.Y - r), Iso.ToScreen(c.X + r, c.Y - r),
                Iso.ToScreen(c.X + r, c.Y + r), Iso.ToScreen(c.X - r, c.Y + r),
            };
            DrawColoredPolygon(d, PowerArea);
            DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, PowerArea with { A = 0.4f }, 1f);
        }
    }

    private void DrawPowerWires()
    {
        var nodes = _world!.Buildings.Where(b => b.Def.SupplyRadius > 0 && b is not ConstructionSite).ToList();
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            float reach = MathF.Min(nodes[i].Def.WireReach, nodes[j].Def.WireReach);
            if (SVec.Distance(nodes[i].Center, nodes[j].Center) > reach) continue;
            var a = Iso.ToScreen(nodes[i].Center) + new Vector2(0, nodes[i] is Nexus ? -40 : -30);
            var b = Iso.ToScreen(nodes[j].Center) + new Vector2(0, nodes[j] is Nexus ? -40 : -30);
            var mid = (a + b) / 2 + new Vector2(0, 8);
            DrawPolyline(new[] { a, mid, b }, Wire, 1.2f, true);
        }
    }

    // ------------------------------------------------------------------ logistics warnings

    /// <summary>
    /// Every disconnected logistics building gets a small orange broken-link badge; each chain end gets a pulsing
    /// red marker. Hovering a disconnected building highlights where its chain ends.
    /// </summary>
    private void DrawLogisticsIssues()
    {
        if (Issues.Count == 0) return;
        var w = _world!;
        float t = (float)Time.GetTicksMsec() / 1000f;
        var badge = new Color(1f, 0.6f, 0.15f);
        var endColor = new Color(1f, 0.25f, 0.2f);

        // Per-building rings only for the chain under the mouse; the end marker below is always shown.
        var hoveredEnd = w.BuildingAt(_input!.HoverTile) is { } over && Issues.TryGetValue(over.Id, out var overIssue) ? overIssue.End : null;
        foreach (var (id, chain) in Issues)
        {
            if (chain.End != hoveredEnd || w.BuildingById(id) is not { } b) continue;
            var p = Iso.ToScreen(b.Center) + new Vector2(10, -HeightOf(b.Def) - 12);
            // Broken chain link: two rings with a gap.
            DrawArc(p + new Vector2(-4, 0), 4, 0.6f, Mathf.Tau - 0.6f, 12, badge, 2, true);
            DrawArc(p + new Vector2(4, 0), 4, Mathf.Pi + 0.6f, Mathf.Pi + Mathf.Tau - 0.6f, 12, badge, 2, true);
        }

        foreach (var group in Issues.Values.GroupBy(i => i.End))
        {
            var end = group.Key;
            if (end.Dead) continue;
            var c = Iso.ToScreen(end.Center);
            float pulse = 0.5f + 0.5f * MathF.Sin(t * 5);
            DrawSetTransform(c, 0, new Vector2(1, 0.5f));
            DrawArc(Vector2.Zero, 18 + 4 * pulse, 0, Mathf.Tau, 24, endColor with { A = 0.5f + 0.5f * pulse }, 2.5f, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            var x = c + new Vector2(0, -HeightOf(end.Def) - 26);
            DrawLine(x + new Vector2(-5, -5), x + new Vector2(5, 5), endColor, 3);
            DrawLine(x + new Vector2(-5, 5), x + new Vector2(5, -5), endColor, 3);

            // Always-visible short label so the problem reads at a glance (hover for the full explanation).
            string label = group.First().Short;
            float labelW = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 12).X + 10;
            var lp = x + new Vector2(-labelW / 2, -26);
            DrawRect(new Rect2(lp, new Vector2(labelW, 17)), new Color(0.35f, 0.05f, 0.05f, 0.85f));
            DrawString(_font, lp + new Vector2(5, 13), label, HorizontalAlignment.Left, -1, 12, Colors.White);
        }

        // Hovering a broken building: trace a line to where its chain ends.
        if (_world!.BuildingAt(_input!.HoverTile) is { } hovered && Issues.TryGetValue(hovered.Id, out var issue) && issue.End != hovered)
        {
            DrawDashedLine(Iso.ToScreen(hovered.Center), Iso.ToScreen(issue.End.Center), endColor, 2f, 6f);
            var d = Iso.Diamond(issue.End.Origin.X, issue.End.Origin.Y, issue.End.Size, 0, -0.06f);
            DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, endColor, 2.5f, true);
        }
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
        switch (e.Def.Behavior)
        {
            case EnemyBehavior.Ranged:
                DrawCircle(body, r, color);
                DrawCircle(body + new Vector2(0, -r * 0.2f), r * 0.45f, color.Lightened(0.5f));
                break;
            case EnemyBehavior.Sapper:
                DrawCircle(body, r, color);
                bool blink = (Time.GetTicksMsec() / 150 + (ulong)e.Id) % 2 == 0;
                DrawCircle(body + new Vector2(0, -r - 3), 3, blink ? new Color(1f, 1f, 0.3f) : new Color(0.6f, 0.2f, 0.1f));
                break;
            default:
                DrawCircle(body, r, color);
                if (e.Def.Id == "brute") DrawArc(body, r, 0, Mathf.Tau, 20, color.Darkened(0.5f), 3, true);
                break;
        }
        DrawCircle(body + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.22f, Colors.White with { A = 0.8f });
        if (e.Hp < e.MaxHp)
            DrawHpBar(body + new Vector2(0, -r - 6), Math.Max(22, r * 2.2f), e.Hp / e.MaxHp);
    }

    private void DrawEngineer(Engineer eng)
    {
        var color = PlayerColor(eng.PlayerId);
        var ground = Iso.ToScreen(eng.Pos);
        DrawSetTransform(ground, 0, new Vector2(1, 0.5f));
        DrawArc(Vector2.Zero, 15, 0, Mathf.Tau, 32, eng.PlayerId == GameSession.LocalPlayer ? Selected : color, 2f, true);
        DrawCircle(Vector2.Zero, 10, new Color(0, 0, 0, 0.35f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawRect(new Rect2(ground + new Vector2(-6, -24), new Vector2(12, 18)), color.Darkened(0.2f));
        DrawCircle(ground + new Vector2(0, -28), 7, color);
        if (_world!.Engineers.Count > 1)
            DrawString(_font, ground + new Vector2(-20, -40), $"P{eng.PlayerId + 1}", HorizontalAlignment.Center, 40, 11, color);
        if (eng.Hp < Engineer.MaxHp)
            DrawHpBar(ground + new Vector2(0, -46), 26, eng.Hp / Engineer.MaxHp);

        // Welding sparks toward whatever it's building or repairing.
        if (eng.WorkingOn is int id && _world.BuildingById(id) is { } target)
        {
            var tool = ground + new Vector2(0, -18);
            var hit = Iso.ToScreen(target.Center) + new Vector2(0, -8);
            float flicker = (Time.GetTicksMsec() / 60) % 2 == 0 ? 1f : 0.5f;
            DrawLine(tool, hit, Scaffold with { A = flicker }, 2f);
            DrawCircle(hit, 3f * flicker + 1, Colors.White with { A = flicker });
        }
    }

    // ------------------------------------------------------------------ effects

    private void DrawShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            float a = s.Frames / (float)s.Total;
            switch (s.Kind)
            {
                case "laser":
                    DrawLine(s.From, s.To, new Color(1f, 0.25f, 0.45f, a), 3f);
                    DrawLine(s.From, s.To, new Color(1f, 0.9f, 0.95f, a), 1f);
                    break;
                case "rocket":
                case "heavy":
                    var p = s.From.Lerp(s.To, 1 - a);
                    DrawCircle(p, s.Kind == "heavy" ? 5 : 4, s.Kind == "heavy" ? new Color(0.7f, 0.5f, 1f) : new Color(1f, 0.5f, 0.2f));
                    DrawLine(s.From.Lerp(s.To, Math.Max(0, 1 - a - 0.2f)), p, new Color(0.8f, 0.8f, 0.8f, 0.4f), 2f);
                    break;
                case "flame":
                    for (int k = 0; k < 4; k++)
                        DrawCircle(s.From.Lerp(s.To, (k + 1) / 4f), 4 + k * 2, new Color(1f, 0.5f - k * 0.08f, 0.1f, 0.5f * a));
                    break;
                case "acid":
                    var q = s.From.Lerp(s.To, 1 - a);
                    DrawCircle(q, 4, new Color(0.5f, 1f, 0.2f, 0.9f));
                    break;
                default:
                    DrawLine(s.From, s.To, ShotColor with { A = a }, 2f);
                    break;
            }
            if (s.Frames <= 1) _shots.RemoveAt(i);
            else _shots[i] = s with { Frames = s.Frames - 1 };
        }
    }

    private void DrawBlasts(double now)
    {
        foreach (var b in _blasts)
        {
            float k = (float)((now - b.Start) / b.Duration);
            if (k < 0) continue;
            var c = b.Kind switch
            {
                "nuke" => new Color(1f, 0.85f, 0.4f),
                "orbital" => new Color(0.5f, 0.9f, 1f),
                "sapper" => new Color(1f, 0.55f, 0.15f),
                "heavy" => new Color(0.75f, 0.55f, 1f),
                _ => new Color(1f, 0.6f, 0.25f),
            };
            float r = b.Radius * (0.4f + 0.6f * k);
            DrawSetTransform(Iso.ToScreen(b.Pos), 0, new Vector2(1, 0.5f));
            DrawCircle(Vector2.Zero, r * Iso.TileW * 0.7f, c with { A = 0.55f * (1 - k) });
            DrawArc(Vector2.Zero, r * Iso.TileW * 0.72f, 0, Mathf.Tau, 48, Colors.White with { A = 0.8f * (1 - k) }, 3, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            if (b.Kind == "orbital" && k < 0.3f)
                DrawLine(Iso.ToScreen(b.Pos) + new Vector2(0, -900), Iso.ToScreen(b.Pos), new Color(0.6f, 0.95f, 1f, 1 - k * 3), 10);
            if (b.Kind == "nuke")
                DrawCircle(Iso.ToScreen(b.Pos) + new Vector2(0, -60 * k), 30 * k + 10, new Color(1f, 0.7f, 0.3f, 0.5f * (1 - k)));
        }
    }

    private void DrawIncoming(double now)
    {
        foreach (var (pos, radius, lands, kind) in _incoming)
        {
            float blink = (Time.GetTicksMsec() / 120) % 2 == 0 ? 1f : 0.4f;
            Iso.DrawGroundEllipse(this, pos, radius, new Color(1f, 0.2f, 0.15f, blink));
            double left = Math.Max(0, lands - now);
            DrawString(_font, Iso.ToScreen(pos) + new Vector2(-40, -10), $"{(kind == "nuke" ? "NUKE" : "STRIKE")} {left:0.0}s",
                HorizontalAlignment.Center, 80, 13, new Color(1f, 0.3f, 0.2f));
        }
    }

    private void DrawPings(double now)
    {
        foreach (var p in _pings)
        {
            float age = (float)(now - p.Start);
            var c = PlayerColor(p.Player);
            var center = Iso.ToScreen(p.Pos);
            for (int i = 0; i < 2; i++)
            {
                float k = (age * 1.2f + i * 0.5f) % 1f;
                DrawSetTransform(center, 0, new Vector2(1, 0.5f));
                DrawArc(Vector2.Zero, 10 + 40 * k, 0, Mathf.Tau, 32, c with { A = 1 - k }, 3, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            var color = p.Kind is PingKind.Danger ? HpBad : c;
            DrawLine(center, center + new Vector2(0, -40), color, 3);
            DrawCircle(center + new Vector2(0, -44), 6, color);
            string label = PlayerInput.PingOptions.First(o => o.Kind == p.Kind).Label;
            DrawString(_font, center + new Vector2(-60, -56), label, HorizontalAlignment.Center, 120, 14, color);
        }
    }

    private void DrawDrawings(double now)
    {
        foreach (var d in _drawings)
        {
            float fade = (float)Math.Clamp((DrawingSeconds - (now - d.Start)) / 5, 0, 1);
            DrawPolyline(d.Points.Select(p => Iso.ToScreen(p)).ToArray(), PlayerColor(d.Player) with { A = 0.9f * fade }, 3f, true);
        }
        if (_input!.Stroke.Count >= 2)
            DrawPolyline(_input.Stroke.Select(p => Iso.ToScreen(p)).ToArray(), PlayerColor(GameSession.LocalPlayer), 3f, true);
    }

    /// <summary>
    /// Ore left under a drill: a bar in the ore's colour (blinking red when nearly dry), or an "empty" icon (an
    /// ore-coloured ring with a red slash) once the tile is mined out.
    /// </summary>
    private void DrawOreGauge(Drill d, Vector2 top)
    {
        if (d.LastOre is not { } ore || d.OreStart <= 0) return;
        var oreColor = Iso.ParseColor(_world!.Data.Items[ore].Color);
        if (oreColor.Luminance < 0.35f) oreColor = oreColor.Lightened(0.45f); // coal would vanish on the dark bar

        if (d.Depleted)
        {
            var c = top + new Vector2(0, -14);
            DrawCircle(c, 8, HpBack);
            DrawArc(c, 6.5f, 0, Mathf.Tau, 20, oreColor with { A = 0.8f }, 2, true);
            DrawLine(c + new Vector2(-5, 5), c + new Vector2(5, -5), HpBad, 2.5f, true);
            return;
        }

        bool low = d.OreFraction < 0.15f;
        bool blinkOff = low && (int)(Time.GetTicksMsec() / 400) % 2 == 0;
        DrawProgressBar(top + new Vector2(0, -5), 26, d.OreFraction, blinkOff ? HpBad : oreColor);
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
        if (b is Turret tu) Iso.DrawGroundEllipse(this, b.Center, tu.Range(_world!), Selected with { A = 0.35f });
        else if (b is Nexus) Iso.DrawGroundEllipse(this, b.Center, b.Def.Range, Selected with { A = 0.35f });
        else if (b is Inserter ins) DrawInserterLinks(ins.Origin, ins.Dir, ins.Input);
    }

    /// <summary>
    /// Each engineer's order queue as lines on the ground in that player's colour: the live path to the current
    /// target, then dashes between queued targets. Targets are outlined so the other player sees them too.
    /// </summary>
    private void DrawOrders()
    {
        foreach (var eng in _world!.Engineers)
        {
            if (!eng.Alive || eng.Orders.Count == 0) continue;
            var color = PlayerColor(eng.PlayerId) with { A = 0.8f };
            var from = eng.Pos;
            for (int i = 0; i < eng.Orders.Count; i++)
            {
                var (target, building) = OrderTarget(eng.Orders[i]);
                if (target is not SVec to) continue;

                if (i == 0 && eng.Path.Count > 0)
                {
                    var pts = new List<Vector2> { Iso.ToScreen(from) };
                    pts.AddRange(eng.Path.Select(p => Iso.ToScreen(p)));
                    DrawPolyline(pts.ToArray(), color, 2f, true);
                    from = eng.Path[^1];
                }
                if (i > 0 || eng.Path.Count == 0)
                    DrawDashedLine(Iso.ToScreen(from), Iso.ToScreen(to), color with { A = 0.5f }, 1.5f, 8f);

                if (building != null && eng.PlayerId != GameSession.LocalPlayer)
                {
                    var d = Iso.Diamond(building.Origin.X, building.Origin.Y, building.Size, 0, -0.06f);
                    DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, color, 2f, true);
                }
                DrawSetTransform(Iso.ToScreen(to), 0, new Vector2(1, 0.5f));
                DrawArc(Vector2.Zero, 7, 0, Mathf.Tau, 16, color, 2f, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                // Number the chain so Shift-queued orders read in sequence.
                if (eng.Orders.Count > 1)
                {
                    var label = Iso.ToScreen(to) + new Vector2(-10, -14);
                    DrawCircle(label + new Vector2(10, -5), 8, new Color(0, 0, 0, 0.6f));
                    DrawString(_font, label, (i + 1).ToString(), HorizontalAlignment.Center, 20, 12, color with { A = 1 });
                }
                from = to;
            }
        }
    }

    private (SVec? Target, Building? Building) OrderTarget(EngineerOrder order) => order switch
    {
        MoveOrder m => (m.Target, null),
        BuildOrder b when _world!.BuildingById(b.SiteId) is { } site => (site.Center, site),
        RepairOrder r when _world!.BuildingById(r.BuildingId) is { } t => (t.Center, t),
        _ => (null, null),
    };

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

    private void DrawGhost()
    {
        var world = _world!;
        var t = _input!.HoverTile;

        if (_input.AbilityBuildingId is int swId && world.BuildingById(swId) is SuperWeapon sw)
        {
            Iso.DrawGroundEllipse(this, _input.HoverPoint, sw.Def.StrikeRadius, HintColor);
            Iso.DrawGroundEllipse(this, _input.HoverPoint, sw.Def.StrikeRadius * 0.5f, HintColor with { A = 0.5f });
            DrawString(_font, Iso.ToScreen(_input.HoverPoint) + new Vector2(-60, -14), sw.Def.AbilityName, HorizontalAlignment.Center, 120, 13, HintColor);
            return;
        }

        var def = _input.BuildTool;
        if (def == null) return;

        // A belt route being dragged: every tile of the L, with its arrow; red where it can't go.
        if (_input.BeltRoute.Count > 0)
        {
            int placed = 0;
            int affordable = def.Cost.Count == 0 ? int.MaxValue : def.Cost.Min(c => world.Network.Count(c.Key) / Math.Max(1, c.Value));
            foreach (var (tile, dir) in _input.BeltRoute)
            {
                bool fits = world.CheckPlacement(GameSession.LocalPlayer, def, tile) == null && placed < affordable;
                if (fits) placed++;
                var tileTint = fits ? GhostOk : GhostBad;
                DrawColoredPolygon(Iso.Diamond(tile.X, tile.Y, 1, 0, 0.05f), tileTint);
                DrawArrow(tile, dir, tileTint with { A = 0.95f }, 1);
            }
            var count = _input.BeltRoute.Count;
            DrawString(_font, Iso.ToScreen(t.X + 0.5f, t.Y + 0.5f) + new Vector2(14, -14), $"{count}", HorizontalAlignment.Left, -1, 14, Colors.White);
            return;
        }

        bool ok = world.CheckPlacement(GameSession.LocalPlayer, def, t) == null && world.Network.Has(def.Cost);
        var tint = ok ? GhostOk : GhostBad;
        DrawColoredPolygon(Iso.Diamond(t.X, t.Y, def.Size, 0, 0.05f), tint);
        if (Tutorial.IsDirectional(def) || def.Kind is BuildingKind.Splitter or BuildingKind.TunnelEntrance or BuildingKind.TunnelExit)
            DrawArrow(t, _input.GhostDir, tint with { A = 0.9f }, 1);
        if (def.Kind == BuildingKind.Turret)
            Iso.DrawGroundEllipse(this, new SVec(t.X + 0.5f, t.Y + 0.5f), def.Range * world.Mods.Mult(Stat.TurretRange), tint);
        if (def.SupplyRadius > 0)
        {
            float r = def.SupplyRadius;
            var c = new SVec(t.X + 0.5f, t.Y + 0.5f);
            var d = new[] { Iso.ToScreen(c.X - r, c.Y - r), Iso.ToScreen(c.X + r, c.Y - r), Iso.ToScreen(c.X + r, c.Y + r), Iso.ToScreen(c.X - r, c.Y + r) };
            DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, tint, 1.5f);
        }
        if (def.Kind == BuildingKind.Inserter) DrawInserterPreview(t, _input.GhostDir);
        if (def.Kind == BuildingKind.Drill) DrawDrillPreview(t, _input.GhostDir);
    }

    /// <summary>Placing a drill: show what it will drop its ore into, in red if that can't take it.</summary>
    private void DrawDrillPreview(Int2 t, Direction dir)
    {
        var world = _world!;
        if (world.Map.OreAt(t) is not { } ore) return;
        var front = t + dir.Offset();
        var target = world.BuildingAt(front);
        string oreName = world.Data.Items[ore].Name.ToLowerInvariant();
        (string text, Color color) = target switch
        {
            null => ($"drops {oreName} on empty ground", new Color(1f, 0.7f, 0.3f)),
            ConstructionSite site => ($"-> {site.Def.Name} (being built)", new Color(0.4f, 1f, 0.5f)),
            _ when LogisticsCheck.AcceptsType(world, target, ore) => ($"-> {target.Def.Name}", new Color(0.4f, 1f, 0.5f)),
            _ => ($"{target.Def.Name} can't take {oreName}! (R to rotate)", HpBad),
        };
        var d = Iso.Diamond(front.X, front.Y, 1, 0, 0.1f);
        DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, color, 2f, true);
        DrawString(_font, Iso.ToScreen(t.Center) + new Vector2(-110, -34), text, HorizontalAlignment.Center, 220, 13, color);
    }

    /// <summary>
    /// Placing an inserter: mark the tile it takes from (blue, behind) and gives to (green, in front), with what's
    /// there and the filter it will pick, so a backwards inserter is obvious before it's built.
    /// </summary>
    private void DrawInserterPreview(Int2 t, Direction dir) => DrawInserterLinks(t, dir, InputSide.Auto);

    /// <summary>
    /// Inserter at <paramref name="t"/>: outline the tile it gives to (green, where the arrow points) and the tile it will
    /// take from (blue: behind, or a side for a 90° move), with names and the filter it will pick, so a backwards or
    /// sideways mistake is obvious.
    /// </summary>
    private void DrawInserterLinks(Int2 t, Direction dir, InputSide input)
    {
        var world = _world!;
        var frontTile = t + dir.Offset();
        var target = world.BuildingAt(frontTile);
        var source = Inserter.LikelySource(world, t, dir, input);
        var take = new Color(0.4f, 0.7f, 1f);
        var give = new Color(0.4f, 1f, 0.5f);

        void Mark(Int2 tile, Color c, string text, float width)
        {
            var d = Iso.Diamond(tile.X, tile.Y, 1, 0, 0.1f);
            DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, c, width, true);
            if (text != "") DrawString(_font, Iso.ToScreen(tile.Center) + new Vector2(-90, 26), text, HorizontalAlignment.Center, 180, 13, c);
        }

        // Every side it could take from, faintly; the one it will actually use, strongly.
        foreach (var d in Inserter.SourceDirs(dir, input))
        {
            var tile = t + d.Offset();
            bool chosen = source != null && world.BuildingAt(tile) == source;
            Mark(tile, chosen ? take : take with { A = 0.35f }, chosen ? $"takes from: {source!.Def.Name}" : "", chosen ? 2f : 1f);
        }
        if (source == null)
            DrawString(_font, Iso.ToScreen(t.Center) + new Vector2(-90, 40), "nothing to take from yet", HorizontalAlignment.Center, 180, 13, take);
        Mark(frontTile, give, $"gives to: {target?.Def.Name ?? "nothing"}", 2f);

        string? filter = Inserter.SuggestFilter(world, source, target);
        if (filter != null)
            DrawString(_font, Iso.ToScreen(t.Center) + new Vector2(-90, -30), $"auto filter: {world.Data.Items[filter].Name}",
                HorizontalAlignment.Center, 180, 13, HintColor);
        var back = world.BuildingAt(t + dir.Opposite().Offset());
        bool sourceGives = source is Belt or IItemSource;
        bool targetGives = target is Belt or IItemSource, backTakes = back is Belt or IItemAcceptor;
        if (!sourceGives && targetGives && backTakes)
            DrawString(_font, Iso.ToScreen(t.Center) + new Vector2(-90, -46), "BACKWARDS - press R twice",
                HorizontalAlignment.Center, 180, 14, HpBad);
    }

    private static Direction DirOf(Int2 d) => Math.Abs(d.X) >= Math.Abs(d.Y)
        ? d.X >= 0 ? Direction.East : Direction.West
        : d.Y >= 0 ? Direction.South : Direction.North;
}
