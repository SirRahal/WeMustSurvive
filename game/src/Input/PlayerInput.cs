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
/// Alt: ping wheel. Ctrl+drag: draw on the map.
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

    private static bool ShiftHeld => Godot.Input.IsKeyPressed(Key.Shift);
    private static bool CtrlHeld => Godot.Input.IsKeyPressed(Key.Ctrl);
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
        if (toolActive && Godot.Input.IsMouseButtonPressed(MouseButton.Left) && !OverHud && !CtrlHeld) DragTo(HoverTile);
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
        PendingBelt = null;
        BuildTool = null;
        DeconstructTool = false;
        AbilityBuildingId = null;
        _dragPlacedAny = false;
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
            case InputEventKey { Keycode: Key.Alt } alt:
                HandlePingKey(alt.Pressed);
                return; // never consume Alt (it's a modifier)

            case InputEventKey { Pressed: true, Echo: false } key:
                if (!HandleKey(key.Keycode)) return;
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when OverHud:
                Hud!.Click();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when CtrlHeld:
                _drawing = true;
                _stroke.Clear();
                _stroke.Add(HoverPoint);
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } when _drawing:
                _drawing = false;
                if (_stroke.Count >= 2) Submit(new DrawCommand(Me, _stroke.ToArray()));
                _stroke.Clear();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Godot.Input.IsKeyPressed(Key.Alt):
                Submit(new PingCommand(Me, PingKind.LookHere, HoverPoint));
                PingWheelOpen = false;
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when AbilityBuildingId is int sw:
                Submit(new FireSuperWeaponCommand(Me, sw, HoverPoint));
                AbilityBuildingId = null;
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                if (BuildTool == null && !DeconstructTool)
                    _selectedId = _world.BuildingAt(HoverTile)?.Id;
                // Placement itself happens in Poll() so dragging works.
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                FlushPendingBelt();
                // Like StarCraft: a placed blueprint ends the tool unless Shift is held.
                if (_dragPlacedAny && !ShiftHeld) CancelTool();
                _dragPlacedAny = false;
                return;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when CtrlHeld:
                Submit(new ClearDrawingsCommand(Me));
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when !OverHud:
                if (BuildTool != null || DeconstructTool || AbilityBuildingId != null) CancelTool();
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

    private bool HandleKey(Key key)
    {
        int digit = key is >= Key.Key1 and <= Key.Key9 ? (int)(key - Key.Key0)
            : key is >= Key.Kp1 and <= Key.Kp9 ? (int)(key - Key.Kp0) : 0;
        if (digit > 0)
        {
            HandleDigit(digit);
            return true;
        }

        switch (key)
        {
            case Key.Escape:
                // Peel back one layer at a time; with nothing to cancel, Esc reaches the pause menu.
                if (ResearchOpen) ResearchOpen = false;
                else if (BuildTool != null || DeconstructTool || AbilityBuildingId != null) CancelTool();
                else if (CardCategory != null) CardCategory = null;
                else if (SelectedBuilding != null) Deselect();
                else return false;
                break;
            case Key.R:
                if (BuildTool != null)
                {
                    GhostDir = GhostDir.RotateCw();
                    _manualRotate = true; // the player chose a facing: stop auto-aiming
                }
                else if (SelectedBuilding != null) RotateSelected();
                else Submit(new RotateBuildingCommand(Me, HoverTile));
                break;
            case Key.T:
                ResearchOpen = !ResearchOpen;
                break;
            case Key.F:
                CycleFilter();
                break;
            case Key.E:
                CycleInputSide();
                break;
            case Key.X:
                Stop();
                break;
            case Key.Delete:
                if (SelectedBuilding != null) DeconstructSelected();
                else ToggleDeconstructTool();
                break;
            case Key.Space:
                if (Sandbox && _world!.Waves.Held) Submit(new HoldWavesCommand(Me, false));
                Submit(new EndPrepCommand(Me));
                break;
            default:
                return false;
        }
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
        var defs = _world!.Data.InCategory(CardCategory).ToList();
        if (digit > defs.Count) return;
        var def = defs[digit - 1];
        if (_world.Research.IsUnlocked(def)) SelectBuildTool(def);
        else Hud?.Notify($"Research {_world.Data.Techs[def.Tech!].Name} first (T)");
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
            if (BuildTool is { Kind: BuildingKind.Belt })
            {
                // Each belt points at the next tile of the drag, so it's placed one step behind the mouse.
                // Corners come out right because the corner belt points along the new direction.
                if (PendingBelt is Int2 pending && pending != t)
                {
                    var dir = DirectionBetween(pending, t);
                    PlaceAt(pending, HintDirAt(pending) ?? dir);
                    GhostDir = dir;
                }
                PendingBelt = t;
            }
            else if (BuildTool != null)
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

    /// <summary>The belt tile under the mouse during a drag, waiting to learn which way the next tile lies.</summary>
    public Int2? PendingBelt { get; private set; }

    /// <summary>Mouse released: the last belt of a drag keeps the drag's direction (or R's facing for a single click).</summary>
    private void FlushPendingBelt()
    {
        if (PendingBelt is Int2 last && BuildTool != null) PlaceAt(last, HintDirAt(last) ?? GhostDir);
        PendingBelt = null;
    }

    private void PlaceAt(Int2 t, Direction dir)
    {
        if (HintDirAt(t) is Direction hinted) GhostDir = hinted;
        // Shift chains onto the current plan; without it the first blueprint replaces the plan and the
        // rest of the same drag chain behind it.
        bool queue = ShiftHeld || _dragPlacedAny;
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
