using Godot;

namespace BeltAndBarricade.View;

/// <summary>
/// Free RTS camera: WASD/arrow keys, screen-edge panning, middle-mouse drag, and wheel zoom.
/// Stays within the map's on-screen bounds.
/// </summary>
public partial class CameraController : Camera2D
{
    private const float PanSpeed = 1100f;
    private const float EdgeMargin = 6f;
    private const float MinZoom = 0.35f, MaxZoom = 2.0f;

    private const float FlySharpness = 3.5f;

    private Rect2 _bounds;
    private bool _dragging;
    private Vector2? _flyTarget;
    private float _flyZoom = 1f;

    /// <summary>Screen-edge panning; handy to turn off while debugging in a windowed editor.</summary>
    public bool EdgePan { get; set; } = true;

    public void SetMapSize(int width, int height)
    {
        float left = Iso.ToScreen(0, height).X, right = Iso.ToScreen(width, 0).X;
        float bottom = Iso.ToScreen(width, height).Y;
        _bounds = new Rect2(left, 0, right - left, bottom);
    }

    public void CenterOn(Vector2 screenPos)
    {
        _flyTarget = null;
        Position = screenPos;
        Clamp();
    }

    /// <summary>Smoothly glides to a point and zoom level (used by the tutorial). Any manual camera input cancels it.</summary>
    public void FlyTo(Vector2 screenPos, float zoom)
    {
        _flyTarget = screenPos;
        _flyZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
    }

    public override void _Process(double delta)
    {
        if (UI.IssueReporter.IsOpen) return; // typing a report: don't pan on WASD or the screen edge
        var dir = Vector2.Zero;
        if (Godot.Input.IsKeyPressed(Key.W) || Godot.Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Godot.Input.IsKeyPressed(Key.S) || Godot.Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Godot.Input.IsKeyPressed(Key.A) || Godot.Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Godot.Input.IsKeyPressed(Key.D) || Godot.Input.IsKeyPressed(Key.Right)) dir.X += 1;

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
            Position += dir.Normalized() * PanSpeed * (float)delta / Zoom.X;
            Clamp();
        }
        else if (_flyTarget is Vector2 target)
        {
            float t = 1f - Mathf.Exp(-FlySharpness * (float)delta);
            Position = Position.Lerp(target, t);
            SetZoom(Mathf.Lerp(Zoom.X, _flyZoom, t));
            Clamp();
            if (Position.DistanceTo(target) < 1f && Mathf.Abs(Zoom.X - _flyZoom) < 0.005f) _flyTarget = null;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _flyTarget = null;
                SetZoom(Zoom.X * 1.1f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _flyTarget = null;
                SetZoom(Zoom.X / 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _flyTarget = null;
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
