using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
using BeltAndBarricade.View;
using Godot;

namespace BeltAndBarricade.Input;

/// <summary>
/// RTS-style controls, translated into sim <see cref="ICommand"/>s. Never touches sim state directly,
/// which is what lets the same commands later be sent over the network to a co-op host.
///
/// Left-click: place the selected blueprint (drag for lines), or select a building.
/// Right-click: move the engineer, or repair / resume construction on the building under the cursor.
/// Blueprints always join the build queue. Shift: queue a move/repair after current jobs, and keep the blueprint selected.
/// </summary>
public partial class PlayerInput : Node2D
{
    /// <summary>Inserter filter choices offered on the command card and by the F key (null = no filter).</summary>
    public static readonly string?[] FilterChoices = { null, "ammo_magazine", "iron_plate", "iron_ore", "coal" };

    private World? _world;
    private Int2? _lastDragTile;
    private bool _dragPlacedAny;
    private int? _selectedId;

    /// <summary>Blueprint being placed, if any.</summary>
    public BuildingDef? BuildTool { get; private set; }
    /// <summary>True while the deconstruct tool is active.</summary>
    public bool DeconstructTool { get; private set; }
    public Direction GhostDir { get; private set; } = Direction.East;
    public Int2 HoverTile { get; private set; }
    public System.Numerics.Vector2 HoverPoint { get; private set; }
    /// <summary>Lets clicks on HUD panels go to the HUD instead of the world.</summary>
    public Hud? Hud { get; set; }
    /// <summary>Active tutorial, if any: blueprints placed on a hint tile take the hinted facing.</summary>
    public Tutorial? Tutorial { get; set; }

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

    public void Bind(World world)
    {
        _world = world;
        _lastDragTile = null;
        _selectedId = null;
        CancelTool();
    }

    /// <summary>Called once per sim tick, before <see cref="World.Step"/>.</summary>
    public void Poll()
    {
        if (_world == null) return;
        HoverPoint = Iso.ToGrid(GetGlobalMousePosition());
        HoverTile = Int2.Floor(HoverPoint);
        if (HintDirAt(HoverTile) is Direction hinted) GhostDir = hinted;

        bool toolActive = BuildTool != null || DeconstructTool;
        if (toolActive && Godot.Input.IsMouseButtonPressed(MouseButton.Left) && !OverHud) DragTo(HoverTile);
        else _lastDragTile = null;
    }

    // ------------------------------------------------------------------ actions (keys and command card)

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
        BuildTool = null;
        DeconstructTool = false;
        _dragPlacedAny = false;
    }

    public void Deselect() => _selectedId = null;

    public void Stop() => Send(new StopCommand(Main.LocalPlayer));

    public void RotateSelected()
    {
        if (SelectedBuilding is { } b) Send(new RotateBuildingCommand(Main.LocalPlayer, b.Origin));
    }

    public void RepairSelected()
    {
        if (SelectedBuilding is { } b) Send(new RepairCommand(Main.LocalPlayer, b.Origin, ShiftHeld));
    }

    public void DeconstructSelected()
    {
        if (SelectedBuilding is not { } b || b is Nexus) return;
        Send(new RemoveBuildingCommand(Main.LocalPlayer, b.Origin));
        _selectedId = null;
    }

    public void SetSelectedFilter(string? item)
    {
        if (SelectedBuilding is Inserter ins) Send(new SetInserterFilterCommand(Main.LocalPlayer, ins.Origin, item));
    }

    // ------------------------------------------------------------------ raw input

    private bool OverHud => Hud != null && Hud.IsMouseOverHud();

    public override void _UnhandledInput(InputEvent e)
    {
        if (_world == null) return;

        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                if (!HandleKey(key.Keycode)) return;
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when OverHud:
                Hud!.Click();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                if (BuildTool == null && !DeconstructTool)
                    _selectedId = _world.BuildingAt(HoverTile)?.Id;
                // Placement itself happens in Poll() so dragging works.
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                // Like StarCraft: a placed blueprint ends the tool unless Shift is held.
                if (_dragPlacedAny && !ShiftHeld) CancelTool();
                _dragPlacedAny = false;
                return;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when !OverHud:
                if (BuildTool != null || DeconstructTool) CancelTool();
                else RightClick();
                break;

            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private bool HandleKey(Key key)
    {
        switch (key)
        {
            case >= Key.Key1 and <= Key.Key9:
                SelectHotkey((int)(key - Key.Key0));
                break;
            case >= Key.Kp1 and <= Key.Kp9:
                SelectHotkey((int)(key - Key.Kp0));
                break;
            case Key.Escape:
                // Cancel first; with nothing to cancel, let it through so Main opens the pause menu.
                if (BuildTool != null || DeconstructTool) CancelTool();
                else if (SelectedBuilding != null) Deselect();
                else return false;
                break;
            case Key.R:
                if (BuildTool != null) GhostDir = GhostDir.RotateCw();
                else if (SelectedBuilding != null) RotateSelected();
                else Send(new RotateBuildingCommand(Main.LocalPlayer, HoverTile));
                break;
            case Key.F:
                CycleFilter();
                break;
            case Key.X:
                Stop();
                break;
            case Key.Delete:
                if (SelectedBuilding != null) DeconstructSelected();
                else ToggleDeconstructTool();
                break;
            case Key.Space:
                Send(new EndPrepCommand(Main.LocalPlayer));
                break;
            default:
                return false;
        }
        return true;
    }

    private void SelectHotkey(int hotkey)
    {
        var def = _world!.Data.Buildings.Values.FirstOrDefault(d => d.Hotkey == hotkey);
        if (def != null) SelectBuildTool(def);
    }

    private void RightClick()
    {
        var b = _world!.BuildingAt(HoverTile);
        if (b is ConstructionSite || (b != null && b.Hp < b.Def.MaxHp))
            Send(new RepairCommand(Main.LocalPlayer, HoverTile, ShiftHeld));
        else
            Send(new MoveCommand(Main.LocalPlayer, HoverPoint, ShiftHeld));
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

        var prev = from;
        foreach (var t in path)
        {
            if (BuildTool != null)
            {
                // Belts laid by dragging point along the drag.
                if (BuildTool.Kind == BuildingKind.Belt && t != prev) GhostDir = DirectionBetween(prev, t);
                if (HintDirAt(t) is Direction hinted) GhostDir = hinted;
                if (_world!.CheckPlacement(Main.LocalPlayer, BuildTool, t) == null)
                {
                    Send(new PlaceBuildingCommand(Main.LocalPlayer, BuildTool.Id, t, GhostDir));
                    _dragPlacedAny = true;
                }
                else if (!_dragPlacedAny)
                {
                    // Let the sim report why the very first placement failed.
                    Send(new PlaceBuildingCommand(Main.LocalPlayer, BuildTool.Id, t, GhostDir));
                }
            }
            else if (_world!.BuildingAt(t) is not null and not Nexus)
            {
                Send(new RemoveBuildingCommand(Main.LocalPlayer, t));
                _dragPlacedAny = true;
            }
            prev = t;
        }
        _lastDragTile = tile;
    }

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
        int i = Array.IndexOf(FilterChoices, target.Filter);
        Send(new SetInserterFilterCommand(Main.LocalPlayer, target.Origin, FilterChoices[(i + 1) % FilterChoices.Length]));
    }

    private static Direction DirectionBetween(Int2 a, Int2 b) => (b.X - a.X, b.Y - a.Y) switch
    {
        (1, 0) => Direction.East,
        (-1, 0) => Direction.West,
        (0, 1) => Direction.South,
        _ => Direction.North,
    };

    private void Send(ICommand cmd) => _world!.Enqueue(cmd);
}
