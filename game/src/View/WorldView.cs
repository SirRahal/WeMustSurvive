using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
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
    /// <summary>Power cable: dark and thin so it stays out of the way; brighter while placing or selecting power.</summary>
    private static readonly Color Wire = new(0.05f, 0.05f, 0.06f, 0.55f);
    private static readonly Color WireLit = new(0.35f, 0.75f, 1f, 0.8f);
    private static readonly Color NoPower = new(1f, 0.25f, 0.2f);

    private sealed record Shot(Vector2 From, Vector2 To, SVec Dir, string Kind, int Frames, int Total);
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

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        // Sprites come from @2x atlases with mipmaps: smooth when zoomed out, sharp when zoomed in.
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

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
        _poses.Clear();
        _corpses.Clear();
        _wrecks.Clear();
        _decals.Clear();
        _firedAt.Clear();
        _beltRuns.Clear();
    }

    /// <summary>Called after each sim step to pick up one-shot visual events.</summary>
    public void Consume(IEnumerable<SimEvent> events)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case ShotEvent s:
                    if (_world!.BuildingAt(Int2.Floor(s.From)) is Turret shooter) _firedAt[shooter.Id] = Now;
                    int frames = s.Kind switch { "rocket" => 10, "heavy" => 12, "laser" => 5, "flame" => 6, "acid" => 8, _ => 4 };
                    _shots.Add(new Shot(Iso.ToScreen(s.From) + new Vector2(0, -22), Iso.ToScreen(s.To) + new Vector2(0, -UnitLift), s.To - s.From, s.Kind, frames, frames));
                    if (s.Kind is "rocket" or "heavy") _blasts.Add(new Blast(s.To, s.Kind == "heavy" ? 2f : 1.6f, s.Kind, Now + frames / 60.0, 0.35));
                    break;
                case ExplosionEvent x:
                    _blasts.Add(new Blast(x.Pos, x.Radius, x.Kind, Now, x.Kind == "nuke" ? 2.5 : x.Kind == "orbital" ? 1.2 : 0.5));
                    _incoming.RemoveAll(i => i.Pos == x.Pos);
                    if (x.Kind is "nuke" or "orbital" or "sapper" || x.Radius >= 1.5f)
                        _decals.Add(new Decal(x.Kind is "nuke" or "orbital" ? "crater" : "scorch", x.Pos, Math.Max(1f, x.Radius / 1.5f), Now, DecalSeconds));
                    break;
                case BuildingDestroyedEvent bd when _world!.Data.Buildings.TryGetValue(bd.DefId, out var def):
                    var center = new SVec(bd.Origin.X + def.Size / 2f, bd.Origin.Y + def.Size / 2f);
                    _wrecks.Add(new Wreck(def, bd.Origin, center, Now));
                    _decals.Add(new Decal("rubble", center, def.Size, Now, DecalSeconds));
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

        // Flat layer: decals, power coverage, belts and their items, flat blueprints, selection, hover, and order lines.
        DrawDecals(now);
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
        TrackUnits(now);
        foreach (var w in _wrecks)
            drawables.Add((Iso.Depth(w.Center), () => DrawWreck(w, now)));
        foreach (var c in _corpses)
            drawables.Add((Iso.Depth(c.Pos), () => DrawCorpse(c, now)));
        foreach (var e in _world.Enemies)
            drawables.Add((Iso.Depth(e.Pos), () => DrawEnemy(e)));
        foreach (var eng in _world.Engineers)
            drawables.Add((Iso.Depth(eng.Pos), () => DrawEngineer(eng)));
        drawables.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var d in drawables) d.Draw();

        DrawPowerWires();
        DrawFeedFlows();
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

    /// <summary>Prop Studio sprites; anything without one falls back to the coded boxes and circles.</summary>
    private static PropSprites Props => PropSprites.Shared;

    /// <summary>Default frames-per-second for strips sprites.json gives no rate for.</summary>
    private const float WorkFps = 10, IdleFps = 4, WalkFps = 8;

    /// <summary>When each turret last fired (real seconds), for its firing frame.</summary>
    private readonly Dictionary<int, double> _firedAt = new();

    /// <summary>
    /// A building's sprite frame: <paramref name="main"/> (a direction, aim, ore_direction, wall joins...), with a
    /// state ("construction", "damaged", "wrecked") if it has that frame. Null if the building has no sprite.
    /// </summary>
    private static PropSprites.Frame? BuildingFrame(string id, Direction dir, string main, string? state = null)
    {
        if (Props.Frames("buildings", id) is not { } frames) return null;
        string d = PropSprites.DirName(dir);
        if (state != null)
        {
            // A variant piece ("mask_5_b") shares its base piece's state frames ("mask_5_damaged").
            string baseMain = main.EndsWith("_b") ? main[..^2] : main;
            foreach (var n in new[] { $"{main}_{state}", $"{baseMain}_{state}", $"{d}_{state}", $"s_{state}", state })
                if (frames.TryGetValue(n, out var sf)) return sf;
        }
        foreach (var n in new[] { main, d, "default", "s", $"{d}_0" })
            if (frames.TryGetValue(n, out var f)) return f;
        // Animated-only frames (a drill's "coal_east_1..4", the nexus "north_1..8"): the strip's first frame.
        foreach (var n in new[] { main, d })
            if (Props.Strip("buildings", id, n) is { Length: > 0 } strip) return strip[0];
        return frames.FirstOrDefault(kv => !kv.Key.EndsWith("_construction") && !kv.Key.EndsWith("_damaged")
                                           && !kv.Key.EndsWith("_wrecked") && kv.Key is not ("construction" or "damaged" or "wrecked")).Value
               ?? frames.Values.First();
    }

    /// <summary>The frame name for a live building: turret aim, drill ore and lamp, inserter swing, wall joins, or its facing.</summary>
    private string MainFrame(Building b)
    {
        string d = PropSprites.DirName(b is Nexus ? Direction.North : b.Dir);
        switch (b)
        {
            case Turret tu:
                return _world!.NearestEnemy(tu.Center, tu.Range(_world)) is { } foe ? PropSprites.AimName(foe.Pos - tu.Center) : "s";
            case Drill drill:
                // Its own ore's rig even once mined out, with the red lamp.
                string? ore = drill.Ore ?? drill.OreHere(_world!);
                string name = (ore ?? drill.LastOre ?? "iron_ore").Split('_')[0];
                return ore == null ? $"{name}_{d}_empty" : $"{name}_{d}";
            case Inserter ins:
                // Swing poses in play order: pickup, mid, drop, mid.
                float t = InserterSwing(ins);
                return d + (t < 1 / 3f ? "_pickup" : t < 2 / 3f ? "_mid" : "_drop");
            case Wall:
                return WallJoins(b.Origin, b.Def.Id);
            default:
                return d;
        }
    }

    /// <summary>Whether a building is busy, so its working strip plays (or its coded motion shows).</summary>
    private static bool IsWorking(Building b) => b switch
    {
        Drill d => d.Status.StartsWith("Mining"),
        Generator g => g.HasFuel && g.Load > 0,
        Crafter c => c.Active != null,
        Lab lab => lab.Working,
        Splitter s => s.Busy,
        TunnelEntrance t => t.Busy,
        TunnelExit t => t.Busy,
        Depot d => d.Storage.Total > 0,
        Nexus => true,
        SuperWeapon => true,
        _ => false,
    };

    /// <summary>
    /// A live building's frame, driven by its state: an inserter's swing strip ("&lt;dir&gt;_swing_N", by how far the hand
    /// is through its swing) or its three poses; a turret's firing frame ("&lt;aim&gt;_fire") just after a shot; for
    /// anything working, the strip after its frame name ("south_0".., "coal_east_0"..) looping at its fps, offset per
    /// building. <paramref name="animated"/> is false when no strip played, so the coded motion is drawn instead.
    /// </summary>
    private PropSprites.Frame? LiveFrame(Building b, out bool animated)
    {
        animated = false;
        var dir = b is Nexus ? Direction.North : b.Dir;
        string main = MainFrame(b);
        string? state = b is Turret ? null : DamageState(b);
        if (state != null) return BuildingFrame(b.Def.Id, dir, main, state);
        string id = b.Def.Id;
        switch (b)
        {
            case Inserter ins:
            {
                // The full cycle strip "<dir>_1..N" (pickup, out to the drop, and back), or a one-way swing strip.
                string dn = PropSprites.DirName(dir);
                var cycle = Props.Strip("buildings", id, dn);
                if (cycle.Length > 0)
                {
                    animated = true;
                    return cycle[Math.Min(cycle.Length - 1, (int)(InserterCycle(ins) * cycle.Length))];
                }
                var swing = Props.Strip("buildings", id, dn + "_swing");
                if (swing.Length > 0)
                {
                    animated = true;
                    return swing[(int)MathF.Round(InserterSwing(ins) * (swing.Length - 1))];
                }
                break;
            }
            case SuperWeapon sw when Props.Strip("buildings", id, "open") is { Length: > 0 } open:
                // The silo's hatches: shut while charging, open once it can fire.
                return sw.Charge >= sw.ChargeNeeded(_world!) ? open[^1] : open[0];
            case Turret:
            {
                // Just after a shot: the "<aim>_fire_N" strip played through once, or a single "<aim>_fire" frame.
                double since = Now - _firedAt.GetValueOrDefault(b.Id, double.NegativeInfinity);
                var fires = Props.Strip("buildings", id, main + "_fire");
                double length = fires.Length > 0 ? fires.Length / Props.Fps("buildings", id, main + "_fire", 15) : 0.12;
                if (since >= length) break;
                var fire = fires.Length > 0 ? fires[Math.Min(fires.Length - 1, (int)(since / length * fires.Length))] : Props.Get("buildings", id, main + "_fire");
                if (fire != null)
                {
                    animated = true;
                    return fire;
                }
                break;
            }
            default:
                // The working strip: "<frame>_N" (a drill's bit, the nexus), "<frame>_work_N", or a named loop: "work"
                // (lab, workshop), "lit" (a furnace red hot), "run" (generator), "turn" (uplink dish).
                float fps = b is Nexus ? IdleFps : WorkFps;
                if (IsWorking(b))
                {
                    foreach (var strip in new[] { main, main + "_work", "work", "lit", "run", "turn" })
                        if (Props.Play("buildings", id, strip, Now, fps, b.Id * 3) is { } f)
                        {
                            animated = true;
                            return f;
                        }
                }
                // Idle: the cold/stopped frame where there is one (a furnace or generator "off").
                else if (Props.First("buildings", id, "off", "idle") is { } off) return off;
                break;
        }
        return BuildingFrame(id, dir, main, null);
    }

    /// <summary>
    /// Where an inserter is in its full cycle, 0..1: out from the pickup side to the drop side (0..0.5), then back
    /// (0.5..1). Idle hands wait at the pickup side.
    /// </summary>
    private static float InserterCycle(Inserter ins) => ins.State switch
    {
        Inserter.Phase.Swinging => InserterSwing(ins) / 2,
        Inserter.Phase.Returning => 1 - InserterSwing(ins) / 2,
        _ => 0,
    };

    /// <summary>
    /// A wall's joined piece for wall <paramref name="id"/> at <paramref name="o"/>: a bit for each side whose neighbour
    /// is a wall of any tier (or a wall being built), N = 1, E = 2, S = 4, W = 8, turned for the view angle. The frame
    /// is "mask_&lt;n&gt;" if the sprite has it, else the older joined-letters name ("ne", "nesw"), else the block ("default").
    /// </summary>
    private string WallJoins(Int2 o, string id)
    {
        int mask = 0;
        foreach (var dir in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
        {
            var n = _world!.BuildingAt(o + dir.Offset());
            if (n is Wall || n is ConstructionSite { Def.Kind: BuildingKind.Wall })
                mask |= PropSprites.ViewTurn(dir) switch { Direction.North => 1, Direction.East => 2, Direction.South => 4, _ => 8 };
        }
        // Alternate tiles use the "_b" piece (other rust and patches) so a long run doesn't repeat.
        if (Props.Get("buildings", id, $"mask_{mask}") != null)
            return (o.X + o.Y) % 2 != 0 && Props.Get("buildings", id, $"mask_{mask}_b") != null ? $"mask_{mask}_b" : $"mask_{mask}";
        string s = ((mask & 1) != 0 ? "n" : "") + ((mask & 2) != 0 ? "e" : "") + ((mask & 4) != 0 ? "s" : "") + ((mask & 8) != 0 ? "w" : "");
        return s == "" || Props.Get("buildings", id, s) == null ? "default" : s;
    }

    /// <summary>How far a belt's slats have run (in belt positions), and when its items last moved (sim tick).</summary>
    private sealed class BeltRun
    {
        public long Signature;
        public long MovedTick;
        public double Last;
        public float Travel;
    }

    private readonly Dictionary<int, BeltRun> _beltRuns = new();

    /// <summary>
    /// Belt slat frame 0..<paramref name="frames"/>-1: one cycle moves the slats 0.25 m, at the belt's speed in real
    /// time. A jammed belt (carrying items that haven't moved for a few sim ticks) stops; a paused game doesn't count.
    /// </summary>
    private int BeltPhase(Belt belt, int frames)
    {
        long sig = belt.Items.Count;
        foreach (var it in belt.Items) sig = sig * 31 + it.Pos;
        double now = Now;
        long tick = _world!.Tick;
        if (!_beltRuns.TryGetValue(belt.Id, out var run))
            _beltRuns[belt.Id] = run = new BeltRun { Signature = sig, MovedTick = tick, Last = now };
        if (sig != run.Signature)
        {
            run.Signature = sig;
            run.MovedTick = tick;
        }
        bool jammed = belt.Items.Count > 0 && tick - run.MovedTick > 6;
        if (!jammed) run.Travel += (float)(now - run.Last) * World.TicksPerSecond * belt.Def.BeltSpeed * _world.Mods.Mult(Stat.BeltSpeed);
        run.Last = now;
        float cycle = 0.25f / Props.MetresPerTile * Belt.Length;
        run.Travel %= cycle * 1000;
        return (int)(run.Travel / (cycle / frames)) % frames;
    }

    /// <summary>Damage state for a building's sprite: "damaged" under half health.</summary>
    private static string? DamageState(Building b) => b.Hp < b.MaxHp * 0.5f ? "damaged" : null;

    /// <summary>Height of a sprite's top above the footprint centre, for overlays (HP bars, warnings).</summary>
    private static float SpriteHeight(PropSprites.Frame f) => Math.Max(8, f.Anchor.Y - 6);

    /// <summary>Smoke puffs rising from each of a frame's smoke points (frame drawn with its anchor on <paramref name="ground"/>).</summary>
    private void DrawSmoke(PropSprites.Frame f, Vector2 ground, float t, int seed)
    {
        for (int s = 0; s < f.Smoke.Length; s++)
        {
            var from = ground - f.Anchor + f.Smoke[s];
            for (int i = 0; i < 3; i++)
            {
                float k = (t * 0.9f + i / 3f + (seed * 7 + s * 3) % 10 / 10f) % 1f;
                var p = from + new Vector2(3 * k + 2 * MathF.Sin((t + i) * 2.1f), -22 * k);
                DrawCircle(p, 2 + 4 * k, new Color(0.55f, 0.55f, 0.58f, 0.45f * (1 - k)));
            }
        }
    }

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
        string id = BeltSpriteId(belt);
        if (!Props.Has("buildings", id)) id = belt.Def.Id;
        string d = PropSprites.DirName(belt.Dir);
        int slats = Math.Max(1, Props.Strip("buildings", id, d).Length);
        var frame = BuildingFrame(id, belt.Dir, $"{d}_{BeltPhase(belt, slats)}", DamageState(belt));
        if (frame != null) PropSprites.Draw(this, frame, Iso.ToScreen(belt.Center));
        else
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

    /// <summary>
    /// Belt sprite id: the corner piece named for the side it's fed from (looking the way it runs); a junction piece
    /// with a gap in the side rail(s) a belt feeds in through ("_side_left", "_side_right", "_side_both") when it is also
    /// fed from behind, or from both sides; else the straight one. Callers fall back to straight without the sprite.
    /// </summary>
    private string BeltSpriteId(Belt belt)
    {
        if (CornerSide(belt) is { } side)
            return belt.Def.Id + (side == belt.Dir.RotateCw() ? "_turn_right" : "_turn_left");
        var w = _world!;
        bool Feeds(Direction from) => w.BuildingAt(belt.Origin + from.Offset()) is Belt n && n.Front == belt.Origin;
        bool right = Feeds(belt.Dir.RotateCw()), left = Feeds(belt.Dir.RotateCw().Opposite());
        return left && right ? belt.Def.Id + "_side_both"
            : left ? belt.Def.Id + "_side_left"
            : right ? belt.Def.Id + "_side_right"
            : belt.Def.Id;
    }

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
        if (Props.Get("items", item, "default") is { } f)
        {
            PropSprites.Draw(this, f, p, r / 5f * 1.15f, Colors.White with { A = alpha });
            return;
        }
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
        var frame = BuildingFrame(b.Def.Id, b.Dir, PropSprites.DirName(b.Dir), DamageState(b));
        bool sprite = frame != null;
        if (frame != null) PropSprites.Draw(this, frame, Iso.ToScreen(b.Center));
        else
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
        // The building's own sprite as a hologram, where it has one.
        if (SiteFrame(site, null) is { } holo)
            PropSprites.Draw(this, holo, Iso.ToScreen(site.Center), 1, Blueprint with { A = 0.35f });
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
        if (SiteFrame(site, "construction") is { } frame)
        {
            // The scaffolded construction frame, firming up as the work goes on.
            PropSprites.Draw(this, frame, Iso.ToScreen(site.Center), 1, Colors.White with { A = 0.55f + 0.45f * site.Fraction });
            full = SpriteHeight(frame);
        }
        else
        {
            float h = Math.Max(3, full * site.Fraction);
            Iso.DrawBox(this, o.X, o.Y, site.Size, h, Iso.ParseColor(site.Def.Color).Darkened(0.25f) with { A = 0.9f });
            var floor = Iso.Diamond(o.X, o.Y, site.Size, 0, 0.04f);
            var top = Iso.Diamond(o.X, o.Y, site.Size, full, 0.04f);
            DrawPolyline(new[] { top[0], top[1], top[2], top[3], top[0] }, Scaffold, 1f);
            for (int i = 0; i < 4; i++) DrawLine(floor[i], top[i], Scaffold, 1f);
        }
        DrawProgressBar(Iso.ToScreen(site.Center) + new Vector2(0, -full - 10), 34 * site.Size, site.Fraction, Blueprint);
        if (site.Hp < site.MaxHp * site.Fraction)
            DrawHpBar(Iso.ToScreen(site.Center) + new Vector2(0, -full - 16), 34 * site.Size, site.Hp / site.MaxHp);
    }

    private PropSprites.Frame? SiteFrame(ConstructionSite site, string? state) => PlannedFrame(site.Def, site.Origin, site.Dir, state);

    /// <summary>
    /// The sprite frame for a building not yet running (a site or the build ghost) at <paramref name="origin"/> facing
    /// <paramref name="dir"/>, in <paramref name="state"/> if it has that frame.
    /// </summary>
    private PropSprites.Frame? PlannedFrame(BuildingDef def, Int2 origin, Direction dir, string? state)
    {
        if (def.Kind == BuildingKind.Nexus) dir = Direction.North;
        string d = PropSprites.DirName(dir);
        string main = def.Kind switch
        {
            BuildingKind.Drill => $"{(Drill.OreUnder(_world!, origin, def.Size) ?? "iron_ore").Split('_')[0]}_{d}",
            BuildingKind.Inserter => d + "_pickup",
            BuildingKind.Belt => d + "_0",
            BuildingKind.Wall => WallJoins(origin, def.Id),
            _ => d,
        };
        return BuildingFrame(def.Id, dir, main, state);
    }

    private void DrawBuilding(Building b)
    {
        var color = Iso.ParseColor(b.Def.Color);
        float height = HeightOf(b.Def);
        // Walls fill their tile so a run of boxes reads as one wall.
        float inset = b is Inserter ? 0.3f : b is PowerPole ? 0.4f : b is Wall ? 0f : 0.08f;
        float t = (float)Time.GetTicksMsec() / 1000f;
        // Prop Studio sprite if there is one, its frame driven by the building's state; otherwise a box.
        var ground = Iso.ToScreen(b.Center);
        var frame = LiveFrame(b, out bool animated);
        bool sprite = frame != null;
        bool working = IsWorking(b);
        if (frame != null)
        {
            float scale = Props.FootprintScale(b.Def.Id, b.Size);
            PropSprites.Draw(this, frame, ground, scale);
            height = SpriteHeight(frame) * scale;
            bool damaged = DamageState(b) != null;
            if (frame.Smoke.Length > 0 && (working && b is not (Nexus or SuperWeapon) || damaged)) DrawSmoke(frame, ground, t, b.Id);
            if (damaged) DrawDamageSparks(ground + new Vector2(0, -height * 0.6f), t, b.Id);
            // Until Prop Studio has a strip for it, the coded motion goes on top of the still sprite.
            if (!animated && working) DrawWorkingMotion(b, frame, ground, height, t);
        }
        else if (b is not PowerPole) Iso.DrawBox(this, b.Origin.X, b.Origin.Y, b.Size, height, color, inset);
        var top = ground + new Vector2(0, -height);

        // With a sprite, only live overlays are drawn (ammo, smoke, fill, the held item); the sprite shows the rest.
        switch (b)
        {
            case Inserter ins:
                DrawInserterArm(ins, sprite ? HeightOf(b.Def) : height, arm: !sprite);
                break;
            case Drill d when !sprite:
                DrawArrow(b.Origin, b.Dir, Colors.White with { A = 0.7f }, height);
                if (d.Status.StartsWith("Mining"))
                    DrawLine(top + new Vector2(-6, -3), top + new Vector2(6, 3).Rotated(t * 8), Colors.White with { A = 0.6f }, 2);
                DrawOreGauge(d, top);
                break;
            case Drill d:
                DrawOreGauge(d, top); // live overlay: ore left under the drill, shown over its sprite too
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
                if (!sprite && g.HasFuel && g.Load > 0)
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
                if (!sprite) DrawCircle(top + new Vector2(0, -10), 16, color.Darkened(0.3f));
                DrawArc(top + new Vector2(0, -10), 18, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * charge, 32,
                    ready ? HintColor with { A = 0.6f + 0.4f * MathF.Sin(t * 6) } : new Color(0.6f, 0.9f, 1f), 3, true);
                if (sprite) break;
                if (sw.Def.Id == "nuclear_silo") DrawColoredPolygon(new[] { top + new Vector2(0, -26), top + new Vector2(-6, -8), top + new Vector2(6, -8) }, new Color(0.9f, 0.9f, 0.8f));
                else DrawArc(top + new Vector2(0, -16), 10, Mathf.Pi, Mathf.Tau, 16, Colors.White, 2, true);
                break;
        }

        if (b is IBurner { BurnsFuel: true } burner) DrawFuelGauge(burner, top, t);
        if (b.NeedsPower && b.PowerFactor <= 0 && (int)(t * 2) % 2 == 0)
            DrawPowerWarning(top + new Vector2(0, -24));
        if (b.Hp < b.MaxHp)
            DrawHpBar(Iso.ToScreen(b.Center) + new Vector2(0, -height - 10), 34 * b.Size, b.Hp / b.MaxHp);
    }

    /// <summary>
    /// Coded motion over a still sprite while Prop Studio has no strip for it: dust and a spinning bit on a mining
    /// drill, a fire glow in a working furnace or workshop, a pulse in the Nexus core and a working lab, and exhaust from
    /// a running generator without smoke points.
    /// </summary>
    private void DrawWorkingMotion(Building b, PropSprites.Frame frame, Vector2 ground, float height, float t)
    {
        float seed = b.Id * 0.37f;
        switch (b)
        {
            case Drill:
            {
                // The bit: a glint turning round the bore, and dust kicked up round the footprint.
                var bit = ground + new Vector2(0, -height * 0.35f);
                var spin = new Vector2(MathF.Cos(t * 9 + seed) * 6, MathF.Sin(t * 9 + seed) * 3);
                DrawLine(bit - spin, bit + spin, Colors.White with { A = 0.5f }, 2);
                for (int i = 0; i < 4; i++)
                {
                    float k = (t * 1.3f + i / 4f + seed) % 1f;
                    float a = Mathf.Tau * (i / 4f) + seed;
                    var p = ground + new Vector2(MathF.Cos(a) * (8 + 14 * k) * b.Size, MathF.Sin(a) * (4 + 7 * k) * b.Size - 6 * k);
                    DrawCircle(p, 2 + 3 * k, new Color(0.62f, 0.55f, 0.42f, 0.45f * (1 - k)));
                }
                break;
            }
            case Crafter when b.Def.Id.Contains("furnace"):
                DrawFurnaceHeat(b, ground, height, t);
                break;
            case Crafter c:
            {
                bool workshop = c.Def.Id == "workshop";
                float flicker = 0.55f + 0.25f * MathF.Sin(t * 13 + seed) + 0.2f * MathF.Sin(t * 29 + seed * 3);
                var mouth = ground + new Vector2(0, -height * 0.3f);
                var glow = workshop ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 0.5f, 0.12f);
                DrawCircle(mouth, 9 * b.Size, glow with { A = 0.18f * flicker });
                DrawCircle(mouth, 4 * b.Size, glow with { A = 0.45f * flicker });
                if (workshop && (int)(t * 8 + seed) % 3 == 0) DrawDamageSparks(mouth, t, b.Id);
                break;
            }
            case Nexus:
            {
                var core = CorePoint(b);
                float pulse = 0.5f + 0.5f * MathF.Sin(t * 3.3f);
                DrawCircle(core, 16, Core with { A = 0.10f + 0.12f * pulse });
                DrawCircle(core, 7, Core with { A = 0.35f + 0.4f * pulse });
                Ring(core, t * 0.5f % 1f, 8, 40);
                break;
            }
            case Lab:
            {
                float pulse = 0.5f + 0.5f * MathF.Sin(t * 4 + seed);
                var lamp = ground + new Vector2(0, -height + 6);
                DrawCircle(lamp, 9, new Color(0.5f, 0.8f, 1f, 0.15f + 0.2f * pulse));
                DrawCircle(lamp, 4, new Color(0.7f, 0.9f, 1f, 0.5f + 0.4f * pulse));
                break;
            }
            case Generator when frame.Smoke.Length == 0:
                var stack = ground + new Vector2(8, -height);
                for (int i = 0; i < 3; i++)
                    DrawCircle(stack + new Vector2(i * 2, -((t * 20 + i * 7) % 18)), 3 + i, new Color(0.6f, 0.6f, 0.65f, 0.4f));
                break;
        }
    }

    /// <summary>
    /// A smelting furnace burning red hot (until Prop Studio's working loop lands): a warm pool of light on the ground
    /// in front, a pulsing red heat halo round the body, a flickering orange-to-yellow firebox mouth, and embers rising.
    /// Real time, phase offset per furnace.
    /// </summary>
    private void DrawFurnaceHeat(Building b, Vector2 ground, float height, float t)
    {
        float seed = b.Id * 1.618f;
        float flicker = 0.6f + 0.22f * MathF.Sin(t * 11 + seed) + 0.18f * MathF.Sin(t * 27 + seed * 2.3f);
        float pulse = 0.5f + 0.5f * MathF.Sin(t * 2.4f + seed);
        var off = b.Dir.Offset();
        var front = new SVec(off.X, off.Y);

        // Light spilling on the ground in front of the firebox.
        var pool = Iso.ToScreen(b.Center + front * (0.55f * b.Size));
        DrawSetTransform(pool, 0, new Vector2(1, 0.5f));
        for (int i = 3; i >= 1; i--)
            DrawCircle(Vector2.Zero, 9 * i * b.Size, new Color(1f, 0.45f, 0.1f, 0.06f * flicker));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // Heat halo round the body: concentric low-alpha circles, breathing.
        var body = ground + new Vector2(0, -height * 0.45f);
        for (int i = 4; i >= 1; i--)
            DrawCircle(body, (7 + 6 * i + 3 * pulse) * b.Size, new Color(1f, 0.18f, 0.05f, (0.035f + 0.025f * pulse)));

        // The firebox mouth, low on the front face: orange round a yellow-white core.
        var mouth = Iso.ToScreen(b.Center + front * (0.42f * b.Size)) + new Vector2(0, -height * 0.22f);
        DrawCircle(mouth, 8 * b.Size, new Color(1f, 0.35f, 0.05f, 0.30f * flicker));
        DrawCircle(mouth, 5 * b.Size, new Color(1f, 0.6f, 0.15f, 0.55f * flicker));
        DrawCircle(mouth, 2.5f * b.Size, new Color(1f, 0.92f, 0.55f, 0.85f * flicker));

        // Embers drifting up from the top and fading.
        var vent = ground + new Vector2(0, -height + 4);
        for (int i = 0; i < 5; i++)
        {
            float k = (t * 0.55f + i / 5f + seed) % 1f;
            float sway = 4 * MathF.Sin(t * 3 + i * 1.7f + seed);
            var p = vent + new Vector2((i - 2) * 3 * b.Size + sway * k, -30 * k);
            DrawCircle(p, 1.6f * (1 - k) + 0.6f, new Color(1f, 0.55f + 0.4f * (1 - k), 0.15f, 0.9f * (1 - k)));
        }
    }

    /// <summary>A few short-lived sparks flicking out of <paramref name="at"/> (damage, welding, a workshop at work).</summary>
    private void DrawDamageSparks(Vector2 at, float t, int seed)
    {
        // Bursts come and go: on for a fifth of every second-and-a-bit, at a different moment per building.
        float phase = (t * 0.8f + seed * 0.618f) % 1f;
        if (phase > 0.2f) return;
        float k = phase / 0.2f;
        for (int i = 0; i < 5; i++)
        {
            float a = -Mathf.Pi / 2 + (i - 2) * 0.5f + seed;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            var p = at + dir * (4 + 12 * k) + new Vector2(0, 10 * k * k);
            DrawLine(p, p - dir * 3, new Color(1f, 0.85f, 0.35f, 1 - k), 1.5f);
        }
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

    /// <summary>How far an inserter's hand is through its swing: 0 at the pickup side, 1 at the drop side.</summary>
    private static float InserterSwing(Inserter ins) => Math.Clamp(ins.State switch
    {
        Inserter.Phase.Swinging => ins.Progress / ins.SwingTicksFor(ins.TookDistance),
        Inserter.Phase.Returning => 1f - ins.Progress / ins.SwingTicksFor(ins.TookDistance),
        _ => 0f,
    }, 0f, 1f);

    /// <param name="arm">False when a sprite already shows the arm; the held item and filter are still drawn.</param>
    private void DrawInserterArm(Inserter ins, float height, bool arm = true)
    {
        float t = InserterSwing(ins);
        var c = ins.Origin.Center;
        // The arm sweeps around the base from the side it took from to the side it drops on: a straight
        // half-turn for back-to-front, a quarter-turn for side inputs. Reaching over a belt, it starts stretched out
        // to the far tile and pulls in as it swings.
        var from = ins.State == Inserter.Phase.Idle ? Inserter.SideDir(ins.Dir, ins.Input) : ins.TookFrom;
        float a0 = Angle(from), a1 = Angle(ins.Dir);
        float delta = Mathf.Wrap(a1 - a0, -Mathf.Pi, Mathf.Pi);
        if (Mathf.Abs(Mathf.Abs(delta) - Mathf.Pi) < 0.01f) delta = Mathf.Pi; // straight through: always swing the same way
        float a = a0 + delta * t;
        var basePt = Iso.ToScreen(c) + new Vector2(0, -height);
        float far = ins.State == Inserter.Phase.Idle ? 0 : ins.TookDistance - 1;
        float len = 0.45f + far * (1 - Math.Min(1, t * 2));
        var hand = Iso.ToScreen(c.X + MathF.Cos(a) * len, c.Y + MathF.Sin(a) * len) + new Vector2(0, -height - 6);
        if (arm) DrawLine(basePt, hand, Iso.ParseColor(ins.Def.Color).Lightened(0.3f), 3f);
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
        if (!ShowingPower()) return;
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

    /// <summary>Placing or selecting anything electrical: show supply areas and light up the cables.</summary>
    private bool ShowingPower()
    {
        var tool = _input!.BuildTool;
        return tool != null && (tool.PowerDraw > 0 || tool.Kind is BuildingKind.Pole or BuildingKind.Generator)
               || _input.SelectedBuilding is { } sel && (sel.NeedsPower || sel is PowerPole or Generator);
    }

    /// <summary>
    /// Cables between power nodes. The grid links every pair in reach, but drawing all of them makes a web; only the
    /// shortest links that join each grid (a minimum spanning tree, Prim's) are drawn, as thin sagging cable.
    /// </summary>
    private void DrawPowerWires()
    {
        var nodes = _world!.Buildings.Where(b => b.Def.SupplyRadius > 0 && b is not ConstructionSite).ToList();
        int n = nodes.Count;
        if (n < 2) return;
        var color = ShowingPower() ? WireLit : Wire;
        var inTree = new bool[n];
        var best = new float[n];
        var from = new int[n];
        for (int start = 0; start < n; start++)
        {
            if (inTree[start]) continue;
            // Grow one grid's tree from here.
            Array.Fill(best, float.MaxValue);
            best[start] = 0;
            from[start] = -1;
            while (true)
            {
                int k = -1;
                for (int i = 0; i < n; i++)
                    if (!inTree[i] && best[i] < float.MaxValue && (k < 0 || best[i] < best[k])) k = i;
                if (k < 0) break;
                inTree[k] = true;
                if (from[k] >= 0) DrawCable(nodes[from[k]], nodes[k], color);
                for (int i = 0; i < n; i++)
                {
                    if (inTree[i]) continue;
                    float d = SVec.Distance(nodes[k].Center, nodes[i].Center);
                    if (d <= MathF.Min(nodes[k].Def.WireReach, nodes[i].Def.WireReach) && d < best[i])
                    {
                        best[i] = d;
                        from[i] = k;
                    }
                }
            }
        }
    }

    /// <summary>One cable between two power nodes' tops, sagging in the middle.</summary>
    private void DrawCable(Building x, Building y, Color color)
    {
        var a = Iso.ToScreen(x.Center) + new Vector2(0, x is Nexus ? -40 : -30);
        var b = Iso.ToScreen(y.Center) + new Vector2(0, y is Nexus ? -40 : -30);
        float sag = 4 + a.DistanceTo(b) * 0.04f;
        var pts = new Vector2[9];
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (float)(pts.Length - 1);
            pts[i] = a.Lerp(b, t) + new Vector2(0, sag * 4 * t * (1 - t));
        }
        DrawPolyline(pts, color, 1f, true);
    }

    // ------------------------------------------------------------------ logistics warnings

    /// <summary>
    /// Every disconnected logistics building gets a small orange broken-link badge; each chain end gets a pulsing
    /// red marker. Hovering a disconnected building highlights where its chain ends.
    /// </summary>
    /// <summary>The logistics problem whose badge or chain is under the mouse (for the HUD's pop-up), if any.</summary>
    public (Building At, LogisticsIssue Issue)? HoveredIssue { get; private set; }

    private const float BadgeR = 11;

    /// <summary>Badge centre over a chain's end building: above its roof on a short stem.</summary>
    private static Vector2 BadgePoint(Building end) => Iso.ToScreen(end.Center) + new Vector2(0, -HeightOf(end.Def) - 30);

    /// <summary>
    /// Logistics problems stay quiet: one round icon badge over the building where each broken chain ends (red =
    /// broken, amber = needs something), none for dead ends. Hovering the badge or any piece of the chain traces the flow and outlines
    /// what's involved; the HUD shows the explanation in a pop-up.
    /// </summary>
    private void DrawLogisticsIssues()
    {
        HoveredIssue = null;
        if (Issues.Count == 0) return;
        var w = _world!;
        float t = (float)Time.GetTicksMsec() / 1000f;
        var mouse = GetGlobalMousePosition();

        // Which chain is hovered: its badge first, then any building in it.
        // Dead ends get no badge (items piling up already shows it); hovering the belt still explains it.
        var ends = Issues.Values.GroupBy(i => i.End).Where(g => !g.Key.Dead && !IssueLook.Quiet(g.First())).ToList();
        foreach (var g in ends)
            if (mouse.DistanceTo(BadgePoint(g.Key)) <= BadgeR + 3) HoveredIssue = (g.Key, g.First());
        if (HoveredIssue == null && w.BuildingAt(_input!.HoverTile) is { } over && Issues.TryGetValue(over.Id, out var overIssue))
            HoveredIssue = (over, overIssue);

        if (HoveredIssue is var (at, issue))
        {
            var (_, color, _) = IssueLook.Of(issue);
            // Flow: dashed from the hovered piece to where its chain ends, the end tile outlined.
            if (issue.End != at) DrawDashedLine(Iso.ToScreen(at.Center), Iso.ToScreen(issue.End.Center), color, 2f, 6f);
            DashedFootprint(issue.End, color);
            // Inserters: the source (cyan) and the drop tile (amber), like the arrows in their own UI.
            if (issue.End is Inserter ins)
            {
                if (Inserter.LikelySource(w, ins.Origin, ins.Dir, ins.Input, ins.Reach) is { } source)
                {
                    DashedFootprint(source, Core);
                    DrawDashedLine(Iso.ToScreen(source.Center) + new Vector2(0, -HeightOf(source.Def) * 0.5f), Iso.ToScreen(ins.Center) + new Vector2(0, -8), Core, 2f, 5f);
                }
                var drop = ins.Front;
                var d = Iso.Diamond(drop.X, drop.Y, 1, 0, 0.06f);
                for (int k = 0; k < 4; k++) DrawDashedLine(d[k], d[(k + 1) % 4], HudTheme.Warm, 2f, 5f);
                DrawDashedLine(Iso.ToScreen(ins.Center) + new Vector2(0, -8), Iso.ToScreen(drop.X + 0.5f, drop.Y + 0.5f), HudTheme.Warm, 2f, 5f);
                DrawSetTransform(Iso.ToScreen(ins.Center), 0, new Vector2(1, 0.5f));
                DrawArc(Vector2.Zero, 26, 0, Mathf.Tau, 32, Core, 2f, true);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        foreach (var g in ends)
        {
            var end = g.Key;
            var (icon, color, _) = IssueLook.Of(g.First());
            bool hot = HoveredIssue is var (_, hi) && hi.End == end;
            var c = BadgePoint(end);
            float pulse = 0.5f + 0.5f * MathF.Sin(t * 4);
            // Stem down to the roof, a soft pulsing halo, then the badge.
            DrawLine(c + new Vector2(0, BadgeR), Iso.ToScreen(end.Center) + new Vector2(0, -HeightOf(end.Def) - 4), color with { A = 0.7f }, 1.5f);
            DrawCircle(c, BadgeR + 4 + 2 * pulse, color with { A = (hot ? 0.3f : 0.14f) * (0.6f + 0.4f * pulse) });
            DrawCircle(c, BadgeR, new Color(0.04f, 0.07f, 0.13f, 0.92f));
            DrawArc(c, BadgeR, 0, Mathf.Tau, 28, color, hot ? 2.5f : 2f, true);
            DrawTextureRect(Icons.Get(icon, color, 28), new Rect2(c - new Vector2(7, 7), new Vector2(14, 14)), false);
        }
    }

    /// <summary>Dashed outline of a building's footprint.</summary>
    private void DashedFootprint(Building b, Color color)
    {
        var d = Iso.Diamond(b.Origin.X, b.Origin.Y, b.Size, 0, -0.08f);
        for (int k = 0; k < 4; k++) DrawDashedLine(d[k], d[(k + 1) % 4], color, 2f, 6f);
    }

    // ------------------------------------------------------------------ units

    /// <summary>What the view remembers of a unit between frames: where it was, which way it faces, how far it has walked.</summary>
    private sealed class Pose
    {
        public SVec Pos;
        public SVec Facing = new(0, 1);
        public float Walked;
        public double MovedAt = double.NegativeInfinity;
        public double SeenAt;
        public string Sprite = "";
    }

    /// <summary>A fallen enemy left lying for a moment.</summary>
    private sealed record Corpse(string Sprite, SVec Pos, SVec Facing, double Start);

    /// <summary>A destroyed building's wreck, left on its footprint until it fades or something is built there.</summary>
    private sealed record Wreck(BuildingDef Def, Int2 Origin, SVec Center, double Start);

    /// <summary>A ground decal (scorch, crater, rubble) that fades out.</summary>
    private sealed record Decal(string Id, SVec Pos, float Scale, double Start, double Duration);

    private const double CorpseSeconds = 4, WreckSeconds = 10, DecalSeconds = 40;
    /// <summary>Tiles walked per stride frame.</summary>
    private const float StrideTiles = 0.3f;

    /// <summary>Poses by enemy id, and by -1 - player for engineers.</summary>
    private readonly Dictionary<int, Pose> _poses = new();
    private readonly List<Corpse> _corpses = new();
    private readonly List<Wreck> _wrecks = new();
    private readonly List<Decal> _decals = new();

    private static int EngineerKey(Engineer eng) => -1 - eng.PlayerId;

    /// <summary>Where an engineer's sprites are: section and id, per player colour (null if Prop Studio has none yet).</summary>
    private static (string Section, string Id)? EngineerArt(Engineer eng)
    {
        foreach (var id in new[] { $"engineer_p{eng.PlayerId + 1}", eng.PlayerId == 0 ? "engineer_blue" : "engineer_orange" })
            foreach (var section in new[] { "engineer", "units" })
                if (Props.Has(section, id)) return (section, id);
        return null;
    }

    private static string EngineerSprite(Engineer eng) => EngineerArt(eng)?.Id ?? "engineer";

    /// <summary>Updates facings and strides from how units moved, and leaves corpses for enemies that died.</summary>
    private void TrackUnits(double now)
    {
        void Track(int key, SVec pos, string sprite)
        {
            if (!_poses.TryGetValue(key, out var p))
            {
                _poses[key] = new Pose { Pos = pos, SeenAt = now, Sprite = sprite };
                return;
            }
            var d = pos - p.Pos;
            float len = d.Length();
            if (len > 1e-4f && len < 3f) // a jump is a respawn, not a step
            {
                p.Facing = d;
                p.Walked += len;
                p.MovedAt = now;
            }
            p.Pos = pos;
            p.SeenAt = now;
        }

        foreach (var e in _world!.Enemies) Track(e.Id, e.Pos, e.Def.Id);
        foreach (var eng in _world.Engineers)
            if (eng.Alive) Track(EngineerKey(eng), eng.Pos, EngineerSprite(eng));
        foreach (var (key, p) in _poses.Where(kv => kv.Key >= 0 && kv.Value.SeenAt < now).ToList())
        {
            // Sappers blow themselves up; everything else falls where it stood.
            if (_world.Data.Enemies.TryGetValue(p.Sprite, out var def) && def.Behavior != EnemyBehavior.Sapper)
                _corpses.Add(new Corpse(p.Sprite, p.Pos, p.Facing, now));
            _poses.Remove(key);
        }
        _corpses.RemoveAll(c => now - c.Start > CorpseSeconds);
        _wrecks.RemoveAll(w => now - w.Start > WreckSeconds || _world.BuildingAt(w.Origin) != null);
        _decals.RemoveAll(d => now - d.Start > d.Duration);
    }

    /// <summary>
    /// A unit's frame, by what it's doing. Acting (attacking, building): the "&lt;facing&gt;_work" strip looping in real
    /// time, else the "_act" strip or frame. Moving: the "&lt;facing&gt;_walk" strip (else the "&lt;facing&gt;_1", "_2" strides),
    /// stepped by distance walked so feet don't slide. Standing: the "&lt;facing&gt;_idle" strip, else the first stride.
    /// </summary>
    private static PropSprites.Frame? UnitFrame(string section, string id, Pose pose, double now, bool acting, SVec? actToward, int seed)
    {
        string facing = PropSprites.AimName(actToward is { } at && at != SVec.Zero ? at : pose.Facing);
        if (acting)
        {
            var act = Props.Play(section, id, facing + "_work", now, WorkFps, seed)
                      ?? Props.Play(section, id, facing + "_attack", now, WorkFps, seed)
                      ?? Props.Play(section, id, facing + "_act", now, WorkFps, seed)
                      ?? Props.Get(section, id, facing + "_act");
            if (act != null) return act;
        }
        bool moving = now - pose.MovedAt < 0.15;
        string walk = Props.Strip(section, id, facing + "_walk").Length > 0 ? facing + "_walk" : facing;
        if (moving && Props.At(section, id, walk, (int)(pose.Walked / StrideTiles)) is { } step) return step;
        return Props.Play(section, id, facing + "_idle", now, IdleFps / 2, seed)
               ?? Props.At(section, id, walk, 0)
               ?? Props.First(section, id, "s_1", "s_walk_1");
    }

    /// <summary>A downed unit's frame: "downed", or "&lt;facing&gt;_dead".</summary>
    private static PropSprites.Frame? DeadFrame(string section, string id, SVec facing) =>
        Props.First(section, id, "downed", PropSprites.AimName(facing) + "_dead");

    private void DrawCorpse(Corpse c, double now)
    {
        float age = (float)(now - c.Start);
        if (DeadFrame("enemies", c.Sprite, c.Facing) is not { } f) return;
        float fade = Math.Clamp((float)(CorpseSeconds - age) / 1.5f, 0, 1);
        PropSprites.Draw(this, f, Iso.ToScreen(c.Pos), 1, Colors.White with { A = fade });
    }

    private void DrawWreck(Wreck w, double now)
    {
        var f = BuildingFrame(w.Def.Id, Direction.South, "", "wrecked");
        if (f == null) return;
        float fade = Math.Clamp((float)(WreckSeconds - (now - w.Start)) / 3f, 0, 1);
        PropSprites.Draw(this, f, Iso.ToScreen(w.Center), 1, Colors.White with { A = fade });
        if (f.Smoke.Length > 0) DrawSmoke(f, Iso.ToScreen(w.Center), (float)now, w.Origin.X * 31 + w.Origin.Y);
    }

    private void DrawDecals(double now)
    {
        foreach (var d in _decals)
        {
            float age = (float)(now - d.Start);
            var f = Props.Get("ground", d.Id, PropSprites.Variant(Int2.Floor(d.Pos)));
            if (f == null) continue;
            float fade = Math.Clamp((float)(d.Duration - age) / 8f, 0, 1);
            PropSprites.Draw(this, f, Iso.ToScreen(d.Pos), d.Scale, Colors.White with { A = fade });
        }
    }

    private void DrawEnemy(Enemy e)
    {
        var ground = Iso.ToScreen(e.Pos);
        float r = e.Def.Radius * Iso.TileW * 0.55f;
        var color = Iso.ParseColor(e.Def.Color);
        if (_poses.TryGetValue(e.Id, out var pose))
        {
            var target = e.AttackingId is int id ? _world!.BuildingById(id) : null;
            if (UnitFrame("enemies", e.Def.Id, pose, Now, e.AttackingId != null, target?.Center - e.Pos, e.Id) is { } frame)
            {
                // Shadows are baked into the sprite; only the overlays go on top.
                PropSprites.Draw(this, frame, ground);
                if (e.Def.Behavior == EnemyBehavior.Sapper && (Time.GetTicksMsec() / 150 + (ulong)e.Id) % 2 == 0)
                    DrawCircle(ground + new Vector2(0, -frame.Anchor.Y - 2), 2.5f, new Color(1f, 1f, 0.3f));
                if (e.Hp < e.MaxHp)
                    DrawHpBar(ground + new Vector2(0, -frame.Anchor.Y - 6), Math.Max(22, r * 2.2f), e.Hp / e.MaxHp);
                return;
            }
        }
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
        var pose = _poses.GetValueOrDefault(EngineerKey(eng));
        if (!eng.Alive)
        {
            // Lying where it fell until it respawns.
            if (pose != null && EngineerArt(eng) is { } downedArt && DeadFrame(downedArt.Section, downedArt.Id, pose.Facing) is { } dead)
                PropSprites.Draw(this, dead, Iso.ToScreen(pose.Pos), 1, Colors.White with { A = 0.8f });
            return;
        }
        var ground = Iso.ToScreen(eng.Pos);
        var workTarget = eng.WorkingOn is int wid ? _world!.BuildingById(wid) : null;
        var art = EngineerArt(eng);
        var frame = pose != null && art is { } a
            ? UnitFrame(a.Section, a.Id, pose, Now, workTarget != null, workTarget?.Center - eng.Pos, eng.PlayerId * 5)
            : null;
        DrawSetTransform(ground, 0, new Vector2(1, 0.5f));
        DrawArc(Vector2.Zero, 15, 0, Mathf.Tau, 32, eng.PlayerId == GameSession.LocalPlayer ? Selected : color, 2f, true);
        if (frame == null) DrawCircle(Vector2.Zero, 10, new Color(0, 0, 0, 0.35f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        float height = 34;
        if (frame != null)
        {
            PropSprites.Draw(this, frame, ground);
            height = frame.Anchor.Y + 4;
        }
        else
        {
            DrawRect(new Rect2(ground + new Vector2(-6, -24), new Vector2(12, 18)), color.Darkened(0.2f));
            DrawCircle(ground + new Vector2(0, -28), 7, color);
        }
        if (_world!.Engineers.Count > 1)
            DrawString(_font, ground + new Vector2(-20, -height - 6), $"P{eng.PlayerId + 1}", HorizontalAlignment.Center, 40, 11, color);
        if (eng.Hp < Engineer.MaxHp)
            DrawHpBar(ground + new Vector2(0, -height - 12), 26, eng.Hp / Engineer.MaxHp);

        // Welding sparks toward whatever it's building or repairing.
        if (eng.WorkingOn is int id && _world.BuildingById(id) is { } target)
        {
            var tool = ground + new Vector2(0, -18);
            var hit = Iso.ToScreen(target.Center) + new Vector2(0, -8);
            float flicker = (Time.GetTicksMsec() / 60) % 2 == 0 ? 1f : 0.5f;
            DrawLine(tool, hit, Scaffold with { A = flicker }, 2f);
            if (EffectFrame("sparks", null, (float)(Now * 4 % 1)) is { } sparks) PropSprites.Draw(this, sparks, hit);
            else DrawCircle(hit, 3f * flicker + 1, Colors.White with { A = flicker });
        }
    }

    // ------------------------------------------------------------------ effects

    /// <summary>
    /// An effect's frame <paramref name="k"/> (0..1) of the way through: steps "0", "1"..., or "&lt;facing&gt;_&lt;step&gt;"
    /// for directional effects facing <paramref name="dir"/> (a grid vector).
    /// </summary>
    private static PropSprites.Frame? EffectFrame(string id, SVec? dir, float k)
    {
        if (Props.Frames("effects", id) is not { } frames) return null;
        string prefix = dir is { } d && d != SVec.Zero ? PropSprites.AimName(d) + "_" : "";
        int steps = 0;
        while (frames.ContainsKey(prefix + steps)) steps++;
        if (steps == 0) return null;
        return frames[prefix + Math.Clamp((int)(k * steps), 0, steps - 1)];
    }

    /// <summary>
    /// Shots: Prop Studio effects where there are frames (muzzle flash, tracer, rocket, flame jet, spit, impact sparks),
    /// coded lines otherwise. The laser stays a coded beam (its sprite is a fixed tile long), with sparks where it hits.
    /// </summary>
    private void DrawShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            float a = s.Frames / (float)s.Total;
            if (!DrawShotSprite(s, a))
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

    /// <summary>Draws a shot from effect sprites; false if they're missing (the coded version is drawn instead).</summary>
    private bool DrawShotSprite(Shot s, float a)
    {
        float k = 1 - a; // 0 at the muzzle, 1 at the target
        var flight = s.From.Lerp(s.To, k);
        switch (s.Kind)
        {
            case "laser":
                if (EffectFrame("sparks", null, k) is not { } hit) return false;
                DrawLine(s.From, s.To, new Color(1f, 0.25f, 0.45f, a), 3f);
                DrawLine(s.From, s.To, new Color(1f, 0.9f, 0.95f, a), 1f);
                PropSprites.Draw(this, hit, s.To);
                return true;
            case "rocket":
                if (EffectFrame("rocket_flight", s.Dir, (k * 6) % 1) is not { } rocket) return false;
                DrawLine(s.From.Lerp(s.To, Math.Max(0, k - 0.2f)), flight, new Color(0.8f, 0.8f, 0.8f, 0.4f), 2f);
                PropSprites.Draw(this, rocket, flight);
                return true;
            case "flame":
                if (EffectFrame("flame_jet", s.Dir, k) is not { } flame) return false;
                PropSprites.Draw(this, flame, s.From.Lerp(s.To, 0.5f), 1, Colors.White with { A = Math.Min(1, a * 2) });
                return true;
            case "acid":
                if (EffectFrame("spit", s.Dir, (k * 4) % 1) is not { } spit) return false;
                PropSprites.Draw(this, spit, flight);
                return true;
            default:
                // Gun and heavy shots: a muzzle flash, the tracer or shell in flight, sparks where it lands.
                bool heavy = s.Kind == "heavy";
                if (EffectFrame("bullet", s.Dir, 0) is not { } bullet) return false;
                if (EffectFrame(heavy ? "heavy_flash" : "muzzle_flash", s.Dir, k * 2) is { } flash && k < 0.5f)
                    PropSprites.Draw(this, flash, s.From);
                PropSprites.Draw(this, bullet, flight, heavy ? 1.5f : 1);
                if (!heavy && k > 0.6f && EffectFrame("sparks", null, (k - 0.6f) / 0.4f) is { } sparks)
                    PropSprites.Draw(this, sparks, s.To);
                return true;
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
            string fx = b.Kind switch { "nuke" => "nuke", "orbital" => "orbital_strike", "heavy" => "heavy_blast", _ => "explosion" };
            var sprite = EffectFrame(fx, null, k);
            DrawSetTransform(Iso.ToScreen(b.Pos), 0, new Vector2(1, 0.5f));
            if (sprite == null) DrawCircle(Vector2.Zero, r * Iso.TileW * 0.7f, c with { A = 0.55f * (1 - k) });
            // The shock ring stays: it shows the blast radius the sprite can't.
            DrawArc(Vector2.Zero, r * Iso.TileW * 0.72f, 0, Mathf.Tau, 48, Colors.White with { A = (sprite == null ? 0.8f : 0.4f) * (1 - k) }, sprite == null ? 3 : 2, true);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            if (sprite != null)
            {
                // Sized to the blast: the sprite's width spans the radius ring's width.
                float scale = Math.Clamp(b.Radius * Iso.TileW * 1.44f / sprite.Size.X, 0.5f, 3f);
                PropSprites.Draw(this, sprite, Iso.ToScreen(b.Pos), scale);
                continue;
            }
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

    private static readonly Color CoalPip = new(0.62f, 0.64f, 0.68f);
    private static readonly Color Flame = new(1f, 0.6f, 0.15f);
    private static readonly Color FeedOutline = new(1f, 0.72f, 0.25f, 0.9f);

    /// <summary>
    /// Coal stored in a burner: one pip per coal it can hold (filled = coal waiting), a flickering flame while a piece
    /// is burning, and a blinking NO FUEL once it wants to burn and has nothing. Makes the fuel buffer visible, so
    /// a furnace that keeps going for a while after its coal supply is removed is obviously running on its stock.
    /// </summary>
    private void DrawFuelGauge(IBurner burner, Vector2 top, float t)
    {
        var pos = top + new Vector2(0, 9);
        if (burner.OutOfFuel && !burner.Burning && burner.Coal == 0)
        {
            if ((int)(t * 2) % 2 == 0)
                DrawString(_font, pos + new Vector2(-24, 4), "NO FUEL", HorizontalAlignment.Center, 48, 10, HpBad);
            return;
        }

        int cap = burner.CoalCapacity;
        float pip = Math.Min(5f, 28f / cap), gap = 1.5f;
        float width = cap * (pip + gap) - gap;
        var start = pos + new Vector2(-width / 2 + 5, 0);
        for (int i = 0; i < cap; i++)
            DrawRect(new Rect2(start + new Vector2(i * (pip + gap), 0), new Vector2(pip, 3)), i < burner.Coal ? CoalPip : HpBack);
        if (burner.Burning)
        {
            float flicker = 0.75f + 0.25f * MathF.Sin(t * 13f);
            var f = start + new Vector2(-6, 3);
            DrawColoredPolygon(new[] { f + new Vector2(-3, 0), f + new Vector2(0, -7 * flicker), f + new Vector2(3, 0) }, Flame);
        }
    }

    /// <summary>
    /// Drill -> building links: a faint arrow from each drill into the machine or storage it feeds, with dots in the
    /// ore's colour running along it while the drill is actually delivering. Shows which buildings work together
    /// (coal drill -> furnace, coal drill -> iron drill, iron drill -> furnace). Belts already show their items.
    /// </summary>
    private void DrawFeedFlows()
    {
        var w = _world!;
        float t = (float)Time.GetTicksMsec() / 1000f;
        foreach (var b in w.Buildings)
        {
            if (b is not Drill d || d.Ore is not { } ore) continue;
            var target = w.BuildingAt(d.Front);
            if (target is null or Belt or Splitter or TunnelEntrance or TunnelExit or ConstructionSite) continue;
            if (!LogisticsCheck.AcceptsType(w, target, ore)) continue;

            var color = Iso.ParseColor(w.Data.Items[ore].Color);
            if (color.Luminance < 0.35f) color = color.Lightened(0.45f);
            var lift = new Vector2(0, -10);
            var from = Iso.ToScreen(d.Center) + lift;
            var to = Iso.ToScreen(d.Front.Center) + lift;
            var dir = (to - from).Normalized();
            var tip = to - dir * 6;
            DrawLine(from, tip, color with { A = 0.35f }, 2f, true);
            var side = new Vector2(-dir.Y, dir.X) * 4;
            DrawColoredPolygon(new[] { tip + dir * 5, tip + side, tip - side }, color with { A = 0.5f });

            if (!d.Status.StartsWith("Mining")) continue; // linked, but nothing moving right now
            for (int i = 0; i < 3; i++)
            {
                float p = (t * 0.9f + i / 3f) % 1f;
                DrawCircle(from.Lerp(tip, p), 2.6f, color);
            }
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
        if (b is Turret tu) Iso.DrawGroundEllipse(this, b.Center, tu.Range(_world!), Selected with { A = 0.35f });
        else if (b is Nexus) Iso.DrawGroundEllipse(this, b.Center, b.Def.Range, Selected with { A = 0.35f });
        else if (b is Inserter ins) DrawInserterLinks(ins.Origin, ins.Dir, ins.Input, ins.Reach);

        // Who works with it: drills and inserters handing items into it, and whatever a drill hands its output to.
        void Outline(Building n)
        {
            var o = Iso.Diamond(n.Origin.X, n.Origin.Y, n.Size, 0, -0.02f);
            DrawPolyline(new[] { o[0], o[1], o[2], o[3], o[0] }, FeedOutline, 2f, true);
        }
        var tiles = b.Tiles.ToHashSet();
        foreach (var n in _world!.Buildings)
            if (n != b && n is Drill or Inserter && tiles.Contains(n.Front)) Outline(n);
        if (b is Drill && _world.BuildingAt(b.Front) is { } fed and not (Belt or ConstructionSite)) Outline(fed);
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
        if (PlannedFrame(def, t, _input.GhostDir, null) is { } ghost)
            PropSprites.Draw(this, ghost, Iso.ToScreen(t.X + def.Size / 2f, t.Y + def.Size / 2f), 1, tint with { A = 0.6f });
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
        if (def.Kind == BuildingKind.Inserter) DrawInserterPreview(t, _input.GhostDir, Math.Max(1, def.Reach));
        if (def.Kind == BuildingKind.Drill) DrawDrillPreview(t, _input.GhostDir, def.Size);
    }

    /// <summary>Placing a drill: show what it will drop its ore into, in red if that can't take it.</summary>
    private void DrawDrillPreview(Int2 t, Direction dir, int size)
    {
        var world = _world!;
        if (Drill.OreUnder(world, t, size) is not { } ore) return;
        var front = Building.FrontOf(t, size, dir);
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
        DrawString(_font, Iso.ToScreen(t.X + size / 2f, t.Y + size / 2f) + new Vector2(-110, -34), text, HorizontalAlignment.Center, 220, 13, color);
    }

    /// <summary>
    /// Placing an inserter: mark the tile it takes from (blue, behind) and gives to (green, in front), with what's
    /// there and the filter it will pick, so a backwards inserter is obvious before it's built.
    /// </summary>
    private void DrawInserterPreview(Int2 t, Direction dir, int reach) => DrawInserterLinks(t, dir, InputSide.Auto, reach);

    /// <summary>
    /// Inserter at <paramref name="t"/>: outline the tile it gives to (green, where the arrow points) and the tiles it can
    /// take from (blue: behind it out to its reach, or a side for a 90° move), with names and the filter it will pick,
    /// so a backwards or sideways mistake is obvious.
    /// </summary>
    private void DrawInserterLinks(Int2 t, Direction dir, InputSide input, int reach)
    {
        var world = _world!;
        var frontTile = t + dir.Offset();
        var target = world.BuildingAt(frontTile);
        var source = Inserter.LikelySource(world, t, dir, input, reach);
        var take = new Color(0.4f, 0.7f, 1f);
        var give = new Color(0.4f, 1f, 0.5f);

        void Mark(Int2 tile, Color c, string text, float width)
        {
            var d = Iso.Diamond(tile.X, tile.Y, 1, 0, 0.1f);
            DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, c, width, true);
            if (text != "") DrawString(_font, Iso.ToScreen(tile.Center) + new Vector2(-90, 26), text, HorizontalAlignment.Center, 180, 13, c);
        }

        // Every tile it could take from, faintly; the building it will actually use, strongly (labelled once).
        bool labelled = false;
        foreach (var (tile, _, _) in Inserter.Sources(t, dir, input, reach))
        {
            bool chosen = source != null && world.BuildingAt(tile) == source;
            Mark(tile, chosen ? take : take with { A = 0.35f }, chosen && !labelled ? $"takes from: {source!.Def.Name}" : "", chosen ? 2f : 1f);
            labelled |= chosen;
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
