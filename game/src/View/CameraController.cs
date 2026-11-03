using BeltAndBarricade.Input;
using Godot;

namespace BeltAndBarricade.View;

/// <summary>
/// Free RTS camera: WASD/arrow keys, screen-edge panning, middle-mouse drag, and wheel zoom.
/// Can follow a moving target (the hero) until the player pans, and turns the view in animated quarter turns
/// around the map centre (Q / Z). Stays within the map's on-screen bounds.
/// </summary>
public partial class CameraController : Camera2D
{
    private const float PanSpeed = 1100f;
    private const float EdgeMargin = 6f;
    private const float MinZoom = 0.35f, MaxZoom = 2.0f;

    private const float FlySharpness = 3.5f;
    private const float FollowSharpness = 8f;
    private const float TurnSharpness = 14f;

    private Rect2 _bounds;
    private int _mapW, _mapH;
    private float _targetAngle;
    private bool _dragging;
    /// <summary>Tutorial flight target as a grid point, so it stays right if the view turns mid-flight.</summary>
    private System.Numerics.Vector2? _flyTarget;
    private float _flyZoom = 1f;
    private Func<Vector2>? _follow;

    /// <summary>True while the camera tracks a target (see <see cref="Follow"/>).</summary>
    public bool IsFollowing => _follow != null;

    /// <summary>Screen-edge panning; handy to turn off while debugging in a windowed editor.</summary>
    public bool EdgePan { get; set; } = true;

    /// <summary>Quarter turns the view is at (or heading to), 0..3.</summary>
    public int QuarterTurns => (int)Mathf.PosMod(Mathf.Round(_targetAngle / (Mathf.Pi / 2)), 4);

    public void SetMapSize(int width, int height)
    {
        _mapW = width;
        _mapH = height;
        Iso.SetMap(width, height);
        _targetAngle = 0;
        _bounds = Iso.Bounds(width, height);
    }

    /// <summary>Turns the view by quarter turns (+1 = clockwise), animated, keeping the centre of the screen in place.</summary>
    public void Turn(int quarters) => _targetAngle += quarters * Mathf.Pi / 2;

    public void CenterOn(Vector2 screenPos)
    {
        _flyTarget = null;
        _follow = null;
        Position = screenPos;
        Clamp();
    }

    /// <summary>Keeps the camera on a moving point (screen coords) until the player pans, drags or jumps elsewhere.</summary>
    public void Follow(Func<Vector2> target)
    {
        _flyTarget = null;
        _follow = target;
    }

    /// <summary>Smoothly glides to a point and zoom level (used by the tutorial). Any manual camera input cancels it.</summary>
    public void FlyTo(Vector2 screenPos, float zoom)
    {
        _follow = null;
        _flyTarget = Iso.ToGrid(screenPos);
        _flyZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
    }

    public override void _Process(double delta)
    {
        TurnStep((float)(delta / Math.Max(Engine.TimeScale, 0.01)));
        if (UI.IssueReporter.IsOpen) return; // typing a report: don't pan on WASD or the screen edge
        var dir = Vector2.Zero;
        if (Godot.Input.IsActionPressed(Bindings.CameraUp)) dir.Y -= 1;
        if (Godot.Input.IsActionPressed(Bindings.CameraDown)) dir.Y += 1;
        if (Godot.Input.IsActionPressed(Bindings.CameraLeft)) dir.X -= 1;
        if (Godot.Input.IsActionPressed(Bindings.CameraRight)) dir.X += 1;

        if (EdgePan && !_dragging && DisplayServer.WindowIsFocused())
        {
            var mouse = GetViewport().GetMousePosition();
            var size = GetViewportRect().Size;
            if (new Rect2(Vector2.Zero, size).HasPoint(mouse))
            {
                if (mouse.X <= EdgeMargin) dir.X -= 1;
                if (mouse.X >= size.X - EdgeMargin) dir.X += 1;
                if (mouse.Y <= EdgeMargin) dir.Y -= 1;
                if (mouse.Y >= size.Y - EdgeMargin) dir.Y += 1;
            }
        }

        // Camera moves in real time regardless of game speed.
        delta /= Math.Max(Engine.TimeScale, 0.01);
        if (dir != Vector2.Zero)
        {
            _flyTarget = null;
            _follow = null;
            Position += dir.Normalized() * PanSpeed * (float)delta / Zoom.X;
            Clamp();
        }
        else if (_follow != null)
        {
            Position = Position.Lerp(_follow(), 1f - Mathf.Exp(-FollowSharpness * (float)delta));
            Clamp();
        }
        else if (_flyTarget is { } grid)
        {
            var target = Iso.ToScreen(grid);
            float t = 1f - Mathf.Exp(-FlySharpness * (float)delta);
            Position = Position.Lerp(target, t);
            SetZoom(Mathf.Lerp(Zoom.X, _flyZoom, t));
            Clamp();
            if (Position.DistanceTo(target) < 1f && Mathf.Abs(Zoom.X - _flyZoom) < 0.005f) _flyTarget = null;
        }
    }

    /// <summary>Eases the view angle toward the target, pivoting on whatever is at the centre of the screen.</summary>
    private void TurnStep(float realDelta)
    {
        float diff = _targetAngle - Iso.Angle;
        if (diff == 0) return;
        var centre = Iso.ToGrid(Position);
        Iso.Angle = Mathf.Abs(diff) < 0.002f ? _targetAngle : Iso.Angle + diff * (1f - Mathf.Exp(-TurnSharpness * realDelta));
        _bounds = Iso.Bounds(_mapW, _mapH);
        Position = Iso.ToScreen(centre);
        Clamp();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey k when k.Pressed && !k.Echo && k.IsAction(Bindings.TurnLeft):
                Turn(-1);
                break;
            case InputEventKey k when k.Pressed && !k.Echo && k.IsAction(Bindings.TurnRight):
                Turn(1);
                break;
            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.ZoomIn):
                _flyTarget = null;
                SetZoom(Zoom.X * 1.1f);
                break;
            case InputEventMouseButton mb when mb.Pressed && mb.IsAction(Bindings.ZoomOut):
                _flyTarget = null;
                SetZoom(Zoom.X / 1.1f);
                break;
            case InputEventMouseButton mb when mb.IsAction(Bindings.CameraDrag):
                _flyTarget = null;
                _follow = null;
                _dragging = mb.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                Position -= motion.Relative / Zoom.X;
                Clamp();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void SetZoom(float z)
    {
        z = Mathf.Clamp(z, MinZoom, MaxZoom);
        Zoom = new Vector2(z, z);
    }

    private void Clamp()
    {
        if (_bounds.Size == Vector2.Zero) return;
        Position = new Vector2(
            Mathf.Clamp(Position.X, _bounds.Position.X, _bounds.End.X),
            Mathf.Clamp(Position.Y, _bounds.Position.Y, _bounds.End.Y));
    }
}
