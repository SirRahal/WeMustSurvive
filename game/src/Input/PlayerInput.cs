using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
using BeltAndBarricade.View;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.Input;

/// <summary>
/// RTS-style controls, translated into sim <see cref="ICommand"/>s and handed to <see cref="Submit"/> (which routes
/// them locally or through the co-op host). Never touches sim state directly.
///
/// Left-click: place the selected blueprint (drag for lines), select a building, or fire a targeted ability.
/// Right-click: move the engineer, or repair / resume construction on the building under the cursor.
/// Shift: chain the order after current jobs (StarCraft-style; without it a new order replaces the plan), and keep the blueprint.
/// Alt: ping wheel. Ctrl+drag: draw on the map. Keys and buttons are InputMap actions (see <see cref="Bindings"/>).
/// </summary>
public partial class PlayerInput : Node2D
{
    /// <summary>Inserter filter choices offered on the command card and by the F key (null = no filter).</summary>
    public static readonly string?[] FilterChoices =
        { null, "ammo_magazine", "iron_plate", "copper_plate", "coal", "rocket", "silver_bar", "heavy_shell" };

    /// <summary>Command-card categories, in order (their number keys are 1..6).</summary>
    public static readonly (string Id, string Label)[] Categories =
    {
        ("logistics", "Logistics"), ("production", "Production"), ("power", "Power"),
        ("walls", "Walls"), ("turrets", "Turrets"), ("special", "Special"),
    };

    /// <summary>Ping wheel options; index 0 is the centre (quick ping), the rest go around clockwise from the top.</summary>
    public static readonly (PingKind Kind, string Label)[] PingOptions =
    {
        (PingKind.LookHere, "Look here"), (PingKind.Defend, "Defend"), (PingKind.OnMyWay, "On my way"),
        (PingKind.NeedIron, "Need iron"), (PingKind.NeedCopper, "Need copper"), (PingKind.NeedSilver, "Need silver"),
        (PingKind.BuildHere, "Build here"), (PingKind.Danger, "Danger!"),
    };

    private World? _world;
    private Int2? _lastDragTile;
    private bool _dragPlacedAny;
    /// <summary>Something was placed since this tool was picked: later placements chain onto the plan instead of replacing it.</summary>
    private bool _toolPlacedAny;
    private int? _selectedId;
    private readonly List<SVec> _stroke = new();
    private bool _drawing;

    public Action<ICommand> Submit { get; set; } = _ => { };
    /// <summary>Sandbox games hold the waves until the player presses Space.</summary>
    public bool Sandbox { get; set; }
    /// <summary>Blueprint being placed, if any.</summary>
    public BuildingDef? BuildTool { get; private set; }
    /// <summary>True while the deconstruct tool is active.</summary>
    public bool DeconstructTool { get; private set; }
    /// <summary>True while the Ping button is armed: the next left click on the map pings there.</summary>
    public bool PingTool { get; private set; }
    /// <summary>Super weapon whose ability is being aimed, if any.</summary>
    public int? AbilityBuildingId { get; private set; }
    /// <summary>Open command-card category (null = the main card).</summary>
    public string? CardCategory { get; private set; }
    public bool ResearchOpen { get; set; }
    public Direction GhostDir { get; private set; } = Direction.East;
    public Int2 HoverTile { get; private set; }
    public SVec HoverPoint { get; private set; }
    public Hud? Hud { get; set; }
    /// <summary>Active tutorial, if any: blueprints placed on a hint tile take the hinted facing.</summary>
    public Tutorial? Tutorial { get; set; }

    // Ping wheel state (screen coords are HUD coords).
    public bool PingWheelOpen { get; private set; }
    public Vector2 PingWheelScreen { get; private set; }
    private SVec _pingWorld;
    /// <summary>Stroke being drawn right now (Ctrl + drag), in grid coords.</summary>
    public IReadOnlyList<SVec> Stroke => _stroke;

    public Building? SelectedBuilding
    {
        get
        {
            if (_selectedId is not int id) return null;
            var b = _world?.BuildingById(id);
            if (b == null) _selectedId = null;
            return b;
        }
    }

    private static bool ShiftHeld => Bindings.Held(Bindings.Chain);
    private static bool CtrlHeld => Bindings.Held(Bindings.Draw);
    private int Me => GameSession.LocalPlayer;

    public void Bind(World world)
    {
        _world = world;
        _lastDragTile = null;
        _selectedId = null;
        CardCategory = null;
        ResearchOpen = false;
        PingWheelOpen = false;
        _drawing = false;
        _stroke.Clear();
        CancelTool();
    }

    /// <summary>Called once per physics tick, before the sim steps.</summary>
    public void Poll()
    {
        if (_world == null) return;
        HoverPoint = Iso.ToGrid(GetGlobalMousePosition());
        HoverTile = Int2.Floor(HoverPoint);
        if ((HintDirAt(HoverTile) ?? AutoDir(HoverTile)) is Direction aimed) GhostDir = aimed;

        if (_drawing)
        {
            if (_stroke.Count == 0 || SVec.Distance(_stroke[^1], HoverPoint) > 0.35f) _stroke.Add(HoverPoint);
            return;
        }

        bool toolActive = BuildTool != null || DeconstructTool;
        bool held = Godot.Input.IsActionPressed(Bindings.Place) && !CtrlHeld;
        if (BuildTool is { Kind: BuildingKind.Belt } && held && (_routeStart != null || !OverHud)) UpdateBeltRoute(HoverTile);
        else if (toolActive && held && !OverHud && _routeStart == null) DragTo(HoverTile);
        else _lastDragTile = null;
    }

    // ------------------------------------------------------------------ actions (keys and command card)

    public void OpenCategory(string? category)
    {
        CardCategory = category;
        CancelTool();
    }

    public void SelectBuildTool(BuildingDef def)
    {
        bool same = BuildTool == def;
        CancelTool();
        if (!same) BuildTool = def;
    }

    public void ToggleDeconstructTool()
    {
        bool was = DeconstructTool;
        CancelTool();
        DeconstructTool = !was;
    }

    public void CancelTool()
    {
        _manualRotate = false;
        ClearBeltRoute();
        BuildTool = null;
        DeconstructTool = false;
        PingTool = false;
        AbilityBuildingId = null;
        _dragPlacedAny = false;
        _toolPlacedAny = false;
    }

    public void TogglePingTool()
    {
        bool was = PingTool;
        CancelTool();
        PingTool = !was;
    }

    /// <summary>Calls the next wave now (in Sandbox, also releases the held waves).</summary>
    public void CallNextWave()
    {
        if (Sandbox && _world!.Waves.Held) Submit(new HoldWavesCommand(Me, false));
        Submit(new EndPrepCommand(Me));
    }

    public void BeginAbilityTargeting(int superWeaponId)
    {
        CancelTool();
        AbilityBuildingId = superWeaponId;
    }

    public void Deselect() => _selectedId = null;

    public void Stop() => Submit(new StopCommand(Me));

    public void RotateSelected()
    {
        if (SelectedBuilding is { } b) Submit(new RotateBuildingCommand(Me, b.Origin));
    }

    public void RepairSelected()
    {
        if (SelectedBuilding is { } b) Submit(new RepairCommand(Me, b.Origin, ShiftHeld));
    }

    public void DeconstructSelected()
    {
        if (SelectedBuilding is not { } b || b is Nexus) return;
        Submit(new RemoveBuildingCommand(Me, b.Origin));
        _selectedId = null;
    }

    public void SetSelectedFilter(string? item)
    {
        if (SelectedBuilding is Inserter ins) Submit(new SetInserterFilterCommand(Me, ins.Origin, item));
    }

    public void StartResearch(string techId) => Submit(new StartResearchCommand(Me, techId));
    public void CancelResearch() => Submit(new CancelResearchCommand(Me));

    // ------------------------------------------------------------------ raw input

    private bool OverHud => Hud != null && Hud.IsMouseOverHud();

    public override void _UnhandledInput(InputEvent e)
    {
        if (_world == null) return;

        switch (e)
        {
            case InputEventKey k when k.IsAction(Bindings.PingWheel) && !k.Echo:
                HandlePingKey(k.Pressed);
                return; // never consume Alt (it's a modifier)

            case InputEventKey { Pressed: true, Echo: false } key:
                if (!HandleKey(key)) return;
                break;

            // HUD clicks are always the left button, whatever "place" is bound to.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when OverHud:
                Hud!.Click();
                break;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Place) && Hud?.DismissPopups() == true:
                break; // a click outside an open dropdown only closes it

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Place) && CtrlHeld:
                _drawing = true;
                _stroke.Clear();
                _stroke.Add(HoverPoint);
                break;

            case InputEventMouseButton mb when !mb.Pressed && mb.IsAction(Bindings.Place) && _drawing:
                _drawing = false;
                if (_stroke.Count >= 2) Submit(new DrawCommand(Me, _stroke.ToArray()));
                _stroke.Clear();
                break;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Place) && (Bindings.Held(Bindings.PingWheel) || PingTool):
                Submit(new PingCommand(Me, PingKind.LookHere, HoverPoint));
                PingWheelOpen = false;
                PingTool = false;
                break;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Place) && AbilityBuildingId is int sw:
                Submit(new FireSuperWeaponCommand(Me, sw, HoverPoint));
                AbilityBuildingId = null;
                break;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Place):
                if (BuildTool == null && !DeconstructTool)
                    _selectedId = _world.BuildingAt(HoverTile)?.Id;
                // Placement itself happens in Poll() so dragging works.
                break;

            case InputEventMouseButton mb when !mb.Pressed && mb.IsAction(Bindings.Place):
                PlaceBeltRoute();
                // Tools stay in hand after placing or deconstructing; right-click (or Esc) puts them away.
                _toolPlacedAny |= _dragPlacedAny;
                _dragPlacedAny = false;
                return;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Command) && CtrlHeld:
                Submit(new ClearDrawingsCommand(Me));
                break;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Command) && _routeStart != null:
                ClearBeltRoute(); // right-click mid-drag drops the route but keeps the belt in hand
                break;

            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.Command) && !OverHud:
                if (BuildTool != null || DeconstructTool || PingTool || AbilityBuildingId != null) CancelTool();
                else RightClick();
                break;

            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void HandlePingKey(bool pressed)
    {
        if (pressed && !PingWheelOpen)
        {
            PingWheelOpen = true;
            PingWheelScreen = Hud?.GetLocalMousePosition() ?? GetViewport().GetMousePosition();
            _pingWorld = HoverPoint;
            return;
        }
        if (pressed || !PingWheelOpen) return;
        PingWheelOpen = false;
        Submit(new PingCommand(Me, PingOptions[PingWheelChoice()].Kind, _pingWorld));
    }

    /// <summary>Index into <see cref="PingOptions"/> for the current mouse position relative to the wheel centre.</summary>
    public int PingWheelChoice()
    {
        var mouse = Hud?.GetLocalMousePosition() ?? GetViewport().GetMousePosition();
        var d = mouse - PingWheelScreen;
        if (d.Length() < 28) return 0;
        // 7 sectors clockwise from straight up.
        float angle = Mathf.PosMod(Mathf.Atan2(d.X, -d.Y), Mathf.Tau);
        return 1 + (int)(angle / (Mathf.Tau / 7)) % 7;
    }

    private bool HandleKey(InputEventKey e)
    {
        int digit = Bindings.SlotPressed(e);
        if (digit > 0)
        {
            HandleDigit(digit);
            return true;
        }

        if (e.IsActionPressed(Bindings.Menu))
        {
            // Peel back one layer at a time; with nothing to cancel, Esc reaches the pause menu.
            if (_routeStart != null) ClearBeltRoute();
            else if (Hud?.CloseOverlay() == true) { }
            else if (ResearchOpen) ResearchOpen = false;
            else if (BuildTool != null || DeconstructTool || PingTool || AbilityBuildingId != null) CancelTool();
            else if (CardCategory != null) CardCategory = null;
            else if (SelectedBuilding != null) Deselect();
            else return false;
        }
        else if (e.IsActionPressed(Bindings.Rotate))
        {
            if (_routeStart != null)
            {
                _routeFlip = !_routeFlip; // R mid-drag swaps which leg of the L comes first
                UpdateBeltRoute(HoverTile);
            }
            else if (BuildTool != null)
            {
                GhostDir = GhostDir.RotateCw();
                _manualRotate = true; // the player chose a facing: stop auto-aiming
            }
            else if (SelectedBuilding != null) RotateSelected();
            else Submit(new RotateBuildingCommand(Me, HoverTile));
        }
        else if (e.IsActionPressed(Bindings.Research)) ResearchOpen = !ResearchOpen;
        else if (e.IsActionPressed(Bindings.InserterFilter)) CycleFilter();
        else if (e.IsActionPressed(Bindings.InserterSide)) CycleInputSide();
        else if (e.IsActionPressed(Bindings.Stop)) Stop();
        else if (e.IsActionPressed(Bindings.Deconstruct))
        {
            if (SelectedBuilding != null) DeconstructSelected();
            else ToggleDeconstructTool();
        }
        else return false;
        return true;
    }

    /// <summary>Number keys: pick a category on the main card, or a blueprint inside an open category.</summary>
    private void HandleDigit(int digit)
    {
        if (SelectedBuilding != null) Deselect();
        if (CardCategory == null)
        {
            if (digit <= Categories.Length) OpenCategory(Categories[digit - 1].Id);
            return;
        }
        var families = _world!.Data.FamiliesIn(CardCategory);
        if (digit <= families.Count) PickSlot(families[digit - 1]);
    }

    /// <summary>Version last picked for each build-menu family (by family id).</summary>
    private readonly Dictionary<string, string> _variantChoice = new();

    /// <summary>Build-menu family id: the base building an upgrade tier belongs to.</summary>
    public static string FamilyOf(BuildingDef def) => def.Family == "" ? def.Id : def.Family;

    /// <summary>The version a family's slot places: the one last picked if still unlocked, else the highest unlocked tier.</summary>
    public BuildingDef SlotVariant(List<BuildingDef> family)
    {
        var research = _world!.Research;
        if (_variantChoice.TryGetValue(FamilyOf(family[0]), out var id) && family.FirstOrDefault(d => d.Id == id) is { } chosen && research.IsUnlocked(chosen))
            return chosen;
        // Highest unlocked tier; a member on the base's own tech (Tunnel Out next to Tunnel In) is a sibling, not an upgrade.
        return family.LastOrDefault(d => research.IsUnlocked(d) && (d == family[0] || d.Tech != family[0].Tech)) ?? family[0];
    }

    /// <summary>A build slot (click or number key): picks its version, or if it's already in hand, steps to the next unlocked one.</summary>
    public void PickSlot(List<BuildingDef> family)
    {
        var research = _world!.Research;
        if (BuildTool is { } tool && family.Contains(tool))
        {
            var unlocked = family.Where(research.IsUnlocked).ToList();
            if (unlocked.Count > 1) SelectVariant(unlocked[(unlocked.IndexOf(tool) + 1) % unlocked.Count]);
            else SelectBuildTool(tool); // only one version: a second press puts it away, as before
            return;
        }
        var def = SlotVariant(family);
        if (research.IsUnlocked(def)) SelectVariant(def);
        else Hud?.Notify($"Research {_world.Data.Techs[def.Tech!].Name} first");
    }

    /// <summary>Picks a specific version (version strip) and remembers it for the family's slot.</summary>
    public void SelectVariant(BuildingDef def)
    {
        _variantChoice[FamilyOf(def)] = def.Id;
        if (BuildTool != def) SelectBuildTool(def);
    }

    private void RightClick()
    {
        var b = _world!.BuildingAt(HoverTile);
        if (b is ConstructionSite || (b != null && b.Hp < b.MaxHp))
            Submit(new RepairCommand(Me, HoverTile, ShiftHeld));
        else
            Submit(new MoveCommand(Me, HoverPoint, ShiftHeld));
    }

    private void DragTo(Int2 tile)
    {
        if (_lastDragTile == tile) return;

        // Walk tile by tile from the last drag position so fast mouse moves don't leave gaps.
        var from = _lastDragTile ?? tile;
        var cur = from;
        var path = new List<Int2> { cur };
        while (cur != tile)
        {
            if (cur.X != tile.X) cur = cur with { X = cur.X + Math.Sign(tile.X - cur.X) };
            else cur = cur with { Y = cur.Y + Math.Sign(tile.Y - cur.Y) };
            path.Add(cur);
        }
        if (_lastDragTile != null) path.RemoveAt(0);

        foreach (var t in path)
        {
            if (BuildTool != null)
            {
                PlaceAt(t, HintDirAt(t) ?? AutoDir(t) ?? GhostDir);
            }
            else if (_world!.BuildingAt(t) is not null and not Nexus)
            {
                Submit(new RemoveBuildingCommand(Me, t));
                _dragPlacedAny = true;
            }
        }
        _lastDragTile = tile;
    }

    // ------------------------------------------------------------------ belt routes

    private Int2? _routeStart;
    private bool? _routeXFirst;
    private bool _routeFlip;
    private readonly List<(Int2 Tile, Direction Dir)> _route = new();

    /// <summary>
    /// The belt route being dragged (ghost preview): an L from where the drag started to the mouse, straight along the
    /// first direction dragged, then one turn. Each belt points at the next; the last keeps the final leg's direction.
    /// </summary>
    public IReadOnlyList<(Int2 Tile, Direction Dir)> BeltRoute => _route;

    private void UpdateBeltRoute(Int2 end)
    {
        _routeStart ??= end;
        var s = _routeStart.Value;
        int dx = end.X - s.X, dy = end.Y - s.Y;
        // The first way the mouse leaves the start tile picks the first leg; R flips it.
        if (_routeXFirst == null && (dx != 0 || dy != 0)) _routeXFirst = Math.Abs(dx) >= Math.Abs(dy);
        bool xFirst = (_routeXFirst ?? true) ^ _routeFlip;
        var corner = xFirst ? new Int2(end.X, s.Y) : new Int2(s.X, end.Y);

        var tiles = new List<Int2> { s };
        void Walk(Int2 to)
        {
            var cur = tiles[^1];
            while (cur != to)
            {
                cur = cur.X != to.X ? cur with { X = cur.X + Math.Sign(to.X - cur.X) } : cur with { Y = cur.Y + Math.Sign(to.Y - cur.Y) };
                tiles.Add(cur);
            }
        }
        Walk(corner);
        Walk(end);

        _route.Clear();
        for (int i = 0; i < tiles.Count; i++)
        {
            var dir = i + 1 < tiles.Count ? DirectionBetween(tiles[i], tiles[i + 1])
                : tiles.Count > 1 ? DirectionBetween(tiles[i - 1], tiles[i]) : GhostDir;
            _route.Add((tiles[i], HintDirAt(tiles[i]) ?? dir));
        }
        if (tiles.Count > 1) GhostDir = _route[^1].Dir;
    }

    /// <summary>Mouse released: every belt of the route goes down, in order.</summary>
    private void PlaceBeltRoute()
    {
        if (BuildTool is { Kind: BuildingKind.Belt })
            foreach (var (t, d) in _route) PlaceAt(t, d);
        ClearBeltRoute();
    }

    private void ClearBeltRoute()
    {
        _routeStart = null;
        _routeXFirst = null;
        _routeFlip = false;
        _route.Clear();
    }

    private void PlaceAt(Int2 t, Direction dir)
    {
        if (HintDirAt(t) is Direction hinted) GhostDir = hinted;
        // The first blueprint after picking a tool replaces the plan (unless Shift chains it); everything placed
        // after that with the same tool, in this drag or later clicks, chains behind it.
        bool queue = ShiftHeld || _dragPlacedAny || _toolPlacedAny;
        if (_world!.CheckPlacement(Me, BuildTool!, t) == null)
        {
            Submit(new PlaceBuildingCommand(Me, BuildTool!.Id, t, dir, queue));
            _dragPlacedAny = true;
        }
        else if (!_dragPlacedAny)
        {
            // Let the sim report why the very first placement failed.
            Submit(new PlaceBuildingCommand(Me, BuildTool!.Id, t, dir, queue));
        }
    }

    private bool _manualRotate;

    /// <summary>Drills aim themselves at the best neighbour for their ore until the player rotates with R.</summary>
    private Direction? AutoDir(Int2 t) =>
        BuildTool is { Kind: BuildingKind.Drill } && !_manualRotate ? Drill.BestOutputDir(_world!, t) : null;

    /// <summary>The facing a tutorial hint asks for at <paramref name="t"/>, if the current blueprint matches it.</summary>
    private Direction? HintDirAt(Int2 t)
    {
        if (BuildTool == null || Tutorial is not { Finished: false } tut || !Tutorial.IsDirectional(BuildTool)) return null;
        var hint = tut.AllHints.FirstOrDefault(h => h.Tile == t && h.Building == BuildTool.Id);
        return hint?.Dir;
    }

    private void CycleFilter()
    {
        var target = SelectedBuilding as Inserter ?? _world!.BuildingAt(HoverTile) as Inserter;
        if (target == null) return;
        // Cycle: Auto -> each item -> Auto. ("Any item" is FilterChoices[0].)
        if (target.AutoFilter)
        {
            Submit(new SetInserterFilterCommand(Me, target.Origin, FilterChoices[0]));
            return;
        }
        int i = Array.IndexOf(FilterChoices, target.Filter);
        if (i == FilterChoices.Length - 1) Submit(new SetInserterFilterCommand(Me, target.Origin, null, Auto: true));
        else Submit(new SetInserterFilterCommand(Me, target.Origin, FilterChoices[i + 1]));
    }

    /// <summary>E: which side the selected or hovered inserter takes from (Auto, Back, Left, Right).</summary>
    public void CycleInputSide()
    {
        var target = SelectedBuilding as Inserter ?? _world!.BuildingAt(HoverTile) as Inserter;
        if (target == null) return;
        var next = (InputSide)(((int)target.Input + 1) % 4);
        Submit(new SetInserterInputCommand(Me, target.Origin, next));
    }

    public void SetSelectedAutoFilter()
    {
        if (SelectedBuilding is Inserter ins) Submit(new SetInserterFilterCommand(Me, ins.Origin, null, Auto: true));
    }

    private static Direction DirectionBetween(Int2 a, Int2 b) => (b.X - a.X, b.Y - a.Y) switch
    {
        (1, 0) => Direction.East,
        (-1, 0) => Direction.West,
        (0, 1) => Direction.South,
        _ => Direction.North,
    };
}
