using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
using BeltAndBarricade.View;
using Godot;

namespace BeltAndBarricade;

/// <summary>
/// Composition root. Owns the sim <see cref="World"/>, steps it at the fixed physics rate (60 Hz),
/// and wires the view, input, RTS camera, and HUD around it.
/// </summary>
public partial class Main : Node2D
{
    public const int LocalPlayer = 0;
    private const string MapName = "funnel";

    private GameData _data = null!;
    private Scenario _scenario = null!;
    private TutorialDef _tutorialDef = null!;
    private World _world = null!;
    private Tutorial? _tutorial;
    /// <summary>Set once the tutorial is finished or skipped, so restarts go straight into the game.</summary>
    private bool _tutorialDone;
    private int _focusedStep = -1;

    private TerrainView _terrain = null!;
    private WorldView _view = null!;
    private PlayerInput _input = null!;
    private CameraController _camera = null!;
    private Hud _hud = null!;
    private PauseMenu _menu = null!;
    private Settings _settings = null!;

    public override void _Ready()
    {
        _settings = Settings.Load();
        _data = GameData.Load(ReadData);
        _scenario = GameData.LoadScenario(ReadData, MapName);
        _tutorialDef = TutorialDef.Load(ReadData, MapName);

        AddChild(_terrain = new TerrainView { Name = "Terrain" });
        AddChild(_view = new WorldView { Name = "World" });
        AddChild(_input = new PlayerInput { Name = "Input" });
        AddChild(_camera = new CameraController { Name = "Camera" });
        var hudLayer = new CanvasLayer { Name = "HudLayer" };
        AddChild(hudLayer);
        hudLayer.AddChild(_hud = new Hud { Name = "Hud" });
        _input.Hud = _hud;
        _hud.TutorialNextRequested += () => _tutorial?.Next(_world);
        _hud.TutorialSkipRequested += () => _tutorial?.Skip(_world);

        var menuLayer = new CanvasLayer { Name = "MenuLayer", Layer = 10 };
        AddChild(menuLayer);
        menuLayer.AddChild(_menu = new PauseMenu { Name = "PauseMenu", Settings = _settings });
        WireMenu();
        ApplySettings();

        StartGame();
    }

    // ------------------------------------------------------------------ pause menu

    private void WireMenu()
    {
        _menu.ResumeRequested += Resume;
        _menu.RestartRequested += () => { Resume(); StartGame(); };
        _menu.ReplayTutorialRequested += () => { _tutorialDone = false; Resume(); StartGame(); };
        _menu.SkipTutorialRequested += () => { _tutorial?.Skip(_world); Resume(); };
        _menu.SurrenderRequested += () => { _world.Enqueue(new SurrenderCommand(LocalPlayer)); Resume(); };
        _menu.QuitRequested += () => GetTree().Quit();
        _menu.SettingsChanged += () => { ApplySettings(); _settings.Save(); };
    }

    private void Pause()
    {
        _menu.CanSurrender = _world.Outcome == Outcome.Playing;
        _menu.TutorialActive = _tutorial is { Finished: false };
        GetTree().Paused = true;
        _menu.Open();
    }

    private void Resume()
    {
        _menu.Close();
        GetTree().Paused = false;
    }

    private void ApplySettings()
    {
        Engine.TimeScale = _settings.GameSpeed;
        _camera.EdgePan = _settings.EdgePan;
        var mode = _settings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
    }

    private void StartGame()
    {
        _world = new World(_data, _scenario, Time.GetTicksUsec(), playerCount: 1);
        _tutorial = _tutorialDone ? null : new Tutorial(_tutorialDef, _world, LocalPlayer);
        _focusedStep = -1;

        _terrain.Bind(_world);
        _input.Bind(_world);
        _view.Bind(_world, _input);
        _hud.Bind(_world, _input);
        _input.Tutorial = _view.Tutorial = _hud.Tutorial = _tutorial;
        _camera.SetMapSize(_world.Map.Width, _world.Map.Height);
        _camera.CenterOn(Iso.ToScreen(_world.Engineers[LocalPlayer].Pos));
    }

    public override void _PhysicsProcess(double delta)
    {
        _input.Poll();
        _world.Step();
        _view.Consume(_world.Events);
        _hud.Consume(_world.Events);
        UpdateTutorial();
    }

    /// <summary>Advances the tutorial and flies the camera to each new step's focus point.</summary>
    private void UpdateTutorial()
    {
        if (_tutorial == null) return;
        _tutorial.Update(_world);
        if (_tutorial.Finished)
        {
            _tutorialDone = true;
            return;
        }
        if (_tutorial.StepIndex == _focusedStep) return;
        _focusedStep = _tutorial.StepIndex;
        var step = _tutorial.Current;
        _camera.FlyTo(Iso.ToScreen(step.Focus.ToVector()), step.Zoom);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            // Reaches here only if nothing (blueprint, selection) consumed Esc first.
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                Pause();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.C }:
                _camera.CenterOn(Iso.ToScreen(_world.Engineers[LocalPlayer].Pos));
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.H }:
                _hud.ToggleHelp();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter } when _world.Outcome != Outcome.Playing:
                StartGame();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter } when _tutorial is { NeedsNext: true }:
                _tutorial.Next(_world);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private static string ReadData(string file)
    {
        string path = $"res://data/{file}";
        if (!Godot.FileAccess.FileExists(path)) throw new FileNotFoundException($"Missing game data: {path}");
        return Godot.FileAccess.GetFileAsString(path);
    }
}
