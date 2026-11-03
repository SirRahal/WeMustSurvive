using Godot;

namespace BeltAndBarricade.Input;

/// <summary>
/// Every rebindable control as a Godot InputMap action, registered at startup. Input handlers check these actions
/// rather than raw keys, and the HUD's Controls panel reads its key names from the same map, so a rebind shows up
/// in both places.
/// </summary>
public static class Bindings
{
    public const string CameraUp = "camera_up", CameraLeft = "camera_left", CameraDown = "camera_down", CameraRight = "camera_right";
    public const string CameraDrag = "camera_drag", ZoomIn = "zoom_in", ZoomOut = "zoom_out", JumpToEngineer = "jump_to_engineer";
    public const string TurnLeft = "turn_view_left", TurnRight = "turn_view_right", ToggleMap = "toggle_map";
    public const string Place = "place", Command = "command", Chain = "chain_orders", Stop = "stop_orders";
    public const string Rotate = "rotate", PingWheel = "ping_wheel", Draw = "draw";
    public const string InserterFilter = "inserter_filter", InserterSide = "inserter_side", Deconstruct = "deconstruct";
    public const string Research = "research", Pause = "pause", Controls = "controls", Menu = "menu";

    /// <summary>Number actions 1..8: build category on the first press, then a blueprint slot.</summary>
    public static string BuildSlot(int n) => $"build_{n}";

    /// <summary>One row of the Controls panel: what it does and how its keys are shown.</summary>
    public sealed record Row(string Label, Func<string> Keys);

    public static readonly Row[] Rows =
    {
        new("Move camera", () => Joined("", CameraUp, CameraLeft, CameraDown, CameraRight) + " / edge"),
        new("Pan camera", () => KeyName(CameraDrag) + "-drag"),
        new("Zoom", () => Joined(" / ", ZoomIn, ZoomOut)),
        new("Turn view", () => Joined(" / ", TurnLeft, TurnRight)),
        new("Follow engineer", () => KeyName(JumpToEngineer)),
        new("Select engineer", () => "Click portrait"),
        new("Show / hide map", () => KeyName(ToggleMap)),
        new("Build category", () => $"{KeyName(BuildSlot(1))}–{KeyName(BuildSlot(6))}"),
        new("Pick building", () => $"{KeyName(BuildSlot(1))}–{KeyName(BuildSlot(8))}"),
        new("Place", () => KeyName(Place)),
        new("Place a line", () => "Drag"),
        new("Rotate", () => KeyName(Rotate)),
        new("Move / repair / resume", () => KeyName(Command)),
        new("Chain orders", () => KeyName(Chain)),
        new("Clear orders", () => KeyName(Stop)),
        new("Ping wheel", () => KeyName(PingWheel)),
        new("Draw", () => KeyName(Draw) + " + drag"),
        new("Inserter filter / side", () => Joined(" / ", InserterFilter, InserterSide)),
        new("Deconstruct", () => KeyName(Deconstruct)),
        new("Research", () => KeyName(Research)),
        new("Pause / resume", () => KeyName(Pause)),
        new("Controls", () => KeyName(Controls)),
        new("Menu", () => KeyName(Menu)),
    };

    /// <summary>Adds the default events for every action that isn't in the InputMap yet (safe to call twice).</summary>
    public static void Register()
    {
        Add(CameraUp, K(Key.W), K(Key.Up));
        Add(CameraLeft, K(Key.A), K(Key.Left));
        Add(CameraDown, K(Key.S), K(Key.Down));
        Add(CameraRight, K(Key.D), K(Key.Right));
        Add(CameraDrag, M(MouseButton.Middle));
        Add(ZoomIn, M(MouseButton.WheelUp));
        Add(ZoomOut, M(MouseButton.WheelDown));
        Add(JumpToEngineer, K(Key.C));
        Add(ToggleMap, K(Key.M));
        Add(TurnLeft, K(Key.Q));
        Add(TurnRight, K(Key.Z));
        Add(Place, M(MouseButton.Left));
        Add(Command, M(MouseButton.Right));
        Add(Chain, K(Key.Shift));
        Add(Stop, K(Key.X));
        Add(Rotate, K(Key.R));
        Add(PingWheel, K(Key.Alt));
        Add(Draw, K(Key.Ctrl));
        Add(InserterFilter, K(Key.F));
        Add(InserterSide, K(Key.E));
        Add(Deconstruct, K(Key.Delete));
        Add(Research, K(Key.T));
        Add(Pause, K(Key.Space));
        Add(Controls, K(Key.H));
        Add(Menu, K(Key.Escape));
        for (int n = 1; n <= 8; n++) Add(BuildSlot(n), K(Key.Key0 + n), K(Key.Kp0 + n));
    }

    /// <summary>True while the action's key or button is held (modifier actions such as Shift).</summary>
    public static bool Held(string action) => Godot.Input.IsActionPressed(action);

    /// <summary>The 1-based build slot an event presses, or 0.</summary>
    public static int SlotPressed(InputEvent e)
    {
        for (int n = 1; n <= 8; n++)
            if (e.IsActionPressed(BuildSlot(n))) return n;
        return 0;
    }

    /// <summary>Short display name of the action's first event ("C", "Space", "LMB", "Wheel").</summary>
    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action)) return "?";
        var events = InputMap.ActionGetEvents(action);
        return events.Count == 0 ? "-" : EventName(events[0]);
    }

    private static string Joined(string sep, params string[] actions) =>
        string.Join(sep, actions.Select(KeyName).Distinct());

    private static string EventName(InputEvent e) => e switch
    {
        InputEventKey k => k.Keycode switch
        {
            Key.Delete => "Del",
            Key.Escape => "Esc",
            Key.Ctrl => "Ctrl",
            _ => OS.GetKeycodeString(k.Keycode),
        },
        InputEventMouseButton m => m.ButtonIndex switch
        {
            MouseButton.Left => "LMB",
            MouseButton.Right => "RMB",
            MouseButton.Middle => "Middle",
            MouseButton.WheelUp or MouseButton.WheelDown => "Wheel",
            _ => $"Mouse {(int)m.ButtonIndex}",
        },
        _ => e.AsText(),
    };

    private static void Add(string action, params InputEvent[] events)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var e in events) InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey K(Key key) => new() { Keycode = key };
    private static InputEventMouseButton M(MouseButton button) => new() { ButtonIndex = button };
}
