using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using BeltAndBarricade.View;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.UI;

/// <summary>
/// Screen-space overlay. A top bar (wave, call-wave, speed, Nexus, resources with a storage dropdown, power,
/// research, menu: icons and numbers only, details in hover tooltips) and a bottom toolbar (minimap, build panel
/// with category tabs and blueprint slots, or the selected building's info and actions, and a 2x2 command grid).
/// Also the research panel (T), Controls panel (H), ping wheel (Alt), super weapon strip, tutorial panel, messages
/// and end screens. Laid out in 1920x1080 design units and scaled to the window height.
/// </summary>
public partial class Hud : Control
{
    private const float DesignHeight = 1080;
    /// <summary>Overall HUD size relative to the 1920x1080 design (0.8 = everything 20% smaller).</summary>
    private const float UiScale = 0.8f;
    private const float BarH = 56, BarPad = 28, BarCut = 16, SidePad = 16, Gap = 8;
    private const float ChipH = 36, IconBtnW = 44;
    // Toolbar: categories as one row of tabs above the slots, so the bar stays short while the building art is large.
    private const float ToolbarH = 164, ToolbarPadTop = 12, ToolbarPadBottom = 10, ToolbarSide = 28, ToolbarGap = 16, ToolbarCut = 24;
    private const float EngineerW = 120, CommandsW = 300, SlotHeaderH = 20, ArtWellPx = 72;
    /// <summary>Gap between the category dock's 3 x 2 square buttons (their size follows the slot height).</summary>
    private const float DockGap = 6;
    private const double DoubleClickSeconds = 0.35;
    private const float DropdownW = 360, ControlsW = 560;
    private const double MessageSeconds = 3.5;
    private const float TutorialW = 620;
    private const int TutorialBodySize = 15;
    private static readonly string[] TierNames = { "Coal", "Iron", "Copper", "Silver", "Gold" };
    private static readonly string[] SpeedTitles = { "Pause", "Normal speed", "Double speed", "Triple speed" };

    private enum TipAnchor { BelowLeft, BelowRight, Above, Mouse }

    /// <summary>Hover text: a title line and body lines, placed relative to the element (or the mouse).</summary>
    /// <param name="TitleColor">Title colour (default text colour); a coloured title also shows the first body line brighter.</param>
    private sealed record Tip(string Title, IReadOnlyList<string> Body, TipAnchor Anchor = TipAnchor.Mouse, float Width = 0, Color? TitleColor = null);

    /// <param name="Key">Identifies the button for tutorial highlighting (building id, "cat:&lt;id&gt;", "filter:&lt;item&gt;", "research", "back").</param>
    /// <param name="DefId">The building a build slot or version button places.</param>
    private sealed record Button(Rect2 Rect, string Label, string Sub, bool Enabled, bool Active, Action OnClick,
        string? Key = null, Tip? Tip = null, string? DefId = null);

    private readonly List<(string Text, double Until)> _messages = new();
    // Hit-testing order: overlays first, then the bars.
    private readonly List<Button> _tutorialButtons = new();
    private readonly List<Button> _researchButtons = new();
    private readonly List<Button> _overlayButtons = new();
    private readonly List<Button> _swButtons = new();
    private readonly List<Button> _barButtons = new();
    private readonly List<Button> _toolButtons = new();
    private readonly List<Button> _pickerButtons = new();
    private readonly List<(Rect2 Rect, Tip Tip)> _chipTips = new();
    private readonly List<Rect2> _panels = new();
    private readonly Dictionary<(Color, Color, int), StyleBoxFlat> _boxes = new();
    private World? _world;
    private PlayerInput? _input;
    private LockstepPeer? _peer;
    private Font _font = null!;
    /// <summary>Canvas the current draw calls go to: the HUD itself, or the pop-up layer being painted.</summary>
    private CanvasItem C => _target ?? this;
    private CanvasItem? _target;

    // Layout, recomputed every frame.
    private Rect2 _barRect, _toolbarRect, _waveChip, _nexusChip, _powerChip, _resButton, _dropdownRect;
    private Rect2 _speedGroup, _minimapPanel, _minimapRect, _buildPanel, _commandsPanel, _controlsRect;
    private Rect2 _tutorialRect, _researchRect, _pickerRect, _engineerRect, _slotHeader;
    private float _dockDivider = -1;
    /// <summary>Where the build slots go, or a selected building's info and actions.</summary>
    private Rect2 _slotArea;
    private List<List<BuildingDef>> _slotFamilies = new();
    private float _waveDivider, _resDivider;
    private readonly List<(ResourceStatus Status, float X)> _resItems = new();
    private int _resOverflow;
    private readonly List<(Rect2 Rect, ResourceStatus Status, bool Trouble)> _resourceRows = new();
    private string _shownCategory = PlayerInput.Categories[0].Id;

    private ImageTexture? _minimapTex;
    private double _minimapAge;
    private ResourceWatch _watch = new();
    private List<ResourceStatus> _report = new();
    private double _reportAge;
    private readonly Dictionary<string, ResourceLevel> _alerted = new();
    private readonly Dictionary<string, double> _alertedAt = new();
    private bool _storageOpen, _controlsOpen, _wasResearchOpen;
    private double _lastHeroClick = -1;
    private bool _engineerSelected;
    /// <summary>x positions of the toolbar's fading section dividers.</summary>
    private readonly List<float> _dividers = new();
    private string? _pickerKey, _pickerForce;
    private float _pickerCaretX;
    private List<BuildingDef>? _pickerFamily;
    private (Tip Tip, Rect2 Rect, List<string> Body)? _tip;
    private string _tipKey = "";
    private List<System.Numerics.Vector2>? _spawnCentres;
    private HudLayer _layerMinimap = null!, _layerStorage = null!, _layerControls = null!, _layerPicker = null!, _layerTooltip = null!, _layerTop = null!;
    private Polygon2D _barGlass = null!, _toolbarGlass = null!;
    // Motion: the Nexus damage trail and resource number flashes.
    private float _nexusLast = 1, _trailFrom = 1;
    private double _trailAt = -10;
    private readonly Dictionary<string, (int Count, double At, bool Up)> _flash = new();

    public Tutorial? Tutorial { get; set; }
    /// <summary>Logistics buildings whose items never arrive anywhere (refreshed a few times a second).</summary>
    public Dictionary<int, LogisticsIssue> Issues { get; set; } = new();
    public CameraController? Camera { get; set; }
    /// <summary>The logistics problem under the mouse on the map (badge or chain), for its pop-up.</summary>
    public Func<(Building At, LogisticsIssue Issue)?>? HoveredIssue { get; set; }
    public Settings Settings { get; set; } = new();
    public bool IsCoop { get; set; }
    public bool Sandbox { get; set; }
    /// <summary>False for a co-op client: the host sets the speed.</summary>
    public bool CanChangeSpeed { get; set; } = true;
    /// <summary>True while the speed control has the game paused.</summary>
    public bool SimPaused { get; set; }
    /// <summary>Meta points awarded for the finished game (shown on the end screen).</summary>
    public int MetaEarned { get; set; }
    public event Action? TutorialNextRequested;
    public event Action? TutorialSkipRequested;
    /// <summary>Speed control: 0 pauses, otherwise one of <see cref="Settings.Speeds"/>.</summary>
    public event Action<float>? SpeedRequested;
    public event Action? MenuRequested;
    /// <summary>Double-click on the hero portrait: the camera should follow the hero.</summary>
    public event Action? FollowHeroRequested;

    private IEnumerable<Button> AllButtons =>
        _pickerButtons.Concat(_tutorialButtons).Concat(_researchButtons).Concat(_overlayButtons).Concat(_swButtons).Concat(_barButtons).Concat(_toolButtons);

    private bool TutorialActive => Tutorial is { Finished: false };
    private int Me => GameSession.LocalPlayer;
    /// <summary>HUD size in design units: 1080 tall, as wide as the window's aspect allows.</summary>
    private Vector2 Screen => Size;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _font = HudTheme.Body;
        // Frosted glass behind the bars, then one layer per pop-up (above the HUD; each fades in on its own).
        AddChild(_barGlass = Glass.Create());
        AddChild(_toolbarGlass = Glass.Create());
        _layerMinimap = AddLayer(DrawMinimapPanel);
        _layerStorage = AddLayer(DrawStorageDropdown);
        _layerControls = AddLayer(DrawControls);
        _layerPicker = AddLayer(DrawTierPicker);
        _layerTooltip = AddLayer(DrawTooltip);
        _layerTop = AddLayer(DrawTopmost);
        FitToWindow();
    }

    private HudLayer AddLayer(Action draw)
    {
        var layer = new HudLayer { Paint = l => PaintOn(l, draw) };
        AddChild(layer);
        return layer;
    }

    /// <summary>Runs <paramref name="draw"/> with its draw calls going to <paramref name="target"/> (from a pop-up layer's <c>_Draw</c>).</summary>
    private void PaintOn(CanvasItem target, Action draw)
    {
        if (_world == null || _input == null) return;
        _target = target;
        try { draw(); }
        finally { _target = null; }
    }

    public void Bind(World world, PlayerInput input, LockstepPeer peer)
    {
        _world = world;
        _input = input;
        _peer = peer;
        _messages.Clear();
        Issues = new();
        MetaEarned = 0;
        _minimapTex = null;
        _watch = new ResourceWatch();
        _report = new();
        _alerted.Clear();
        _alertedAt.Clear();
        _storageOpen = _controlsOpen = false;
        _engineerSelected = false;
        _pickerKey = null;
        _pickerFamily = null;
        _spawnCentres = null;
        _flash.Clear();
        _nexusLast = _trailFrom = 1;
    }

    /// <summary>True if the mouse is over any HUD panel (so world clicks should be ignored).</summary>
    public bool IsMouseOverHud()
    {
        var m = GetLocalMousePosition();
        return _panels.Any(r => r.HasPoint(m));
    }

    /// <summary>Activates whatever HUD element is under the mouse.</summary>
    public void Click()
    {
        var m = GetLocalMousePosition();
        if (_storageOpen && !_dropdownRect.HasPoint(m) && !_resButton.HasPoint(m)) _storageOpen = false;
        if (_pickerKey != null && !_pickerRect.HasPoint(m) && !_toolButtons.Any(b => b.Sub == "tiers" && b.Rect.HasPoint(m))) _pickerKey = null;
        var b = AllButtons.FirstOrDefault(b => b.Rect.HasPoint(m));
        if (b != null)
        {
            if (b.Enabled) b.OnClick();
            return;
        }
        if (Settings.ShowMinimap && _minimapRect.HasPoint(m)) CenterCameraOnMinimap(m);
    }

    /// <summary>A click on the map while the storage dropdown is open just closes it. True if it did.</summary>
    public bool DismissPopups()
    {
        if (!_storageOpen && _pickerKey == null) return false;
        _storageOpen = false;
        _pickerKey = null;
        return true;
    }

    /// <summary>Esc: closes the storage dropdown or the Controls panel. True if something closed.</summary>
    public bool CloseOverlay()
    {
        if (_pickerKey != null) _pickerKey = null;
        else if (_storageOpen) _storageOpen = false;
        else if (_controlsOpen) _controlsOpen = false;
        else if (_engineerSelected) _engineerSelected = false;
        else return false;
        return true;
    }

    /// <summary>--hud=storage,controls,research,hero,tool:ID,picker:FAMILY,turn:N (debug): opens panels so screenshots can show them.</summary>
    public void OpenPanels(string panels)
    {
        var names = panels.Split(',');
        _storageOpen = names.Contains("storage");
        _controlsOpen = names.Contains("controls");
        if (names.Contains("research") && _input != null) _input.ResearchOpen = true;
        if (names.Contains("hero")) _engineerSelected = true;
        // select:ID selects the first building of that kind; select:ID#N the Nth (1-based).
        if (names.FirstOrDefault(n => n.StartsWith("select:")) is { } select)
        {
            var parts = select[7..].Split('#');
            int nth = parts.Length > 1 && int.TryParse(parts[1], out var k) ? k - 1 : 0;
            if (_world!.Buildings.Where(b => b.Def.Id == parts[0]).Skip(nth).FirstOrDefault() is { } chosen) _input!.Select(chosen);
        }
        // picker:FAMILY holds the tier picker open over that family's slot.
        if (names.FirstOrDefault(n => n.StartsWith("picker:")) is { } picker) _pickerForce = picker[7..];
        // turn:N turns the view N quarter turns.
        if (names.FirstOrDefault(n => n.StartsWith("turn:")) is { } turn && int.TryParse(turn[5..], out int quarters)) Camera?.Turn(quarters);
        // tool:ID puts that blueprint in hand (shows the version strip).
        if (names.FirstOrDefault(n => n.StartsWith("tool:")) is { } tool && _world!.Data.Buildings.TryGetValue(tool[5..], out var def))
        {
            _input!.OpenCategory(def.Category);
            _input.SelectVariant(def);
        }
    }

    public void ToggleControls()
    {
        _controlsOpen = !_controlsOpen;
        if (_controlsOpen) _storageOpen = false;
    }

    private void ToggleStorage()
    {
        _storageOpen = !_storageOpen;
        if (_storageOpen) _controlsOpen = false;
    }

    public void Notify(string text)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _messages.RemoveAll(m => m.Text == text);
        _messages.Add((text, now + MessageSeconds));
        if (_messages.Count > 5) _messages.RemoveRange(0, _messages.Count - 5);
    }

    public void Consume(IEnumerable<SimEvent> events)
    {
        foreach (var e in events)
        {
            if (e is WaveStartedEvent && !Settings.ShowMinimap) ToggleMap(); // the map opens when a wave starts
            string? text = e switch
            {
                MessageEvent m when m.PlayerId == Me && m.Text != "Tile is occupied" => m.Text,
                WaveStartedEvent w => $"Wave {w.Wave} incoming!",
                BuildingDestroyedEvent d => $"{_world!.Data.Buildings[d.DefId].Name} destroyed",
                ResearchCompletedEvent r => $"Research complete: {_world!.Data.Techs[r.TechId].Name}",
                StrikeIncomingEvent s => s.Kind == "nuke" ? "Nuclear launch detected!" : "Orbital strike inbound!",
                PingEvent p when p.PlayerId != Me => $"{PlayerName(p.PlayerId)}: {PlayerInput.PingOptions.First(o => o.Kind == p.Kind).Label}",
                _ => null,
            };
            if (text != null) Notify(text);
        }
        double now = Time.GetTicksMsec() / 1000.0;
        _messages.RemoveAll(m => m.Until < now);
    }

    public static string PlayerName(int id) => $"P{id + 1}";

    public override void _Process(double delta)
    {
        FitToWindow();
        if (_world == null) return;
        _minimapAge += delta;
        UpdateResources(delta);
        if (_input!.ResearchOpen && !_wasResearchOpen) _storageOpen = _controlsOpen = false;
        _wasResearchOpen = _input.ResearchOpen;
        Layout(delta);
        var m = GetLocalMousePosition();
        if (Settings.ShowMinimap && Godot.Input.IsMouseButtonPressed(MouseButton.Left) && _minimapRect.HasPoint(m) && !AllButtons.Any(b => b.Rect.HasPoint(m)))
            CenterCameraOnMinimap(m);
        UpdateLayers(delta);
        QueueRedraw();
    }

    /// <summary>Glass behind the bars and pop-ups, and each pop-up's open animation.</summary>
    private void UpdateLayers(double delta)
    {
        _barGlass.Polygon = HudTheme.PlatePoints(_barRect, cutBR: BarCut, cutBL: BarCut);
        _toolbarGlass.Polygon = HudTheme.PlatePoints(_toolbarRect, cutTL: ToolbarCut, cutTR: ToolbarCut);
        void Pop(HudLayer layer, bool open, Rect2 rect)
        {
            layer.Backdrop = open ? Glass.Rect(rect) : Array.Empty<Vector2>();
            layer.Animate(open, delta);
        }
        Pop(_layerMinimap, Settings.ShowMinimap, _minimapPanel);
        Pop(_layerStorage, _storageOpen, _dropdownRect);
        Pop(_layerControls, _controlsOpen, _controlsRect);
        Pop(_layerPicker, _pickerFamily != null, _pickerRect);
        string key = _tip?.Tip.Title ?? "";
        if (key != _tipKey)
        {
            _layerTooltip.Restart();
            _tipKey = key;
        }
        Pop(_layerTooltip, _tip != null, _tip?.Rect ?? default);
        _layerTop.QueueRedraw();
    }

    /// <summary>Scales the HUD so its coordinate space is always <see cref="DesignHeight"/> / <see cref="UiScale"/> tall.</summary>
    private void FitToWindow()
    {
        var vp = GetViewportRect().Size;
        if (vp.Y <= 0) return;
        float k = vp.Y / DesignHeight * UiScale;
        Position = Vector2.Zero;
        Scale = new Vector2(k, k);
        Size = vp / k;
    }

    public override void _Draw()
    {
        if (_world == null || _input == null) return;
        var size = Screen;
        DrawTopBar();
        DrawToolbar();
        DrawSuperWeaponStrip();
        DrawIncoming();
        if (TutorialActive) DrawTutorial();
        DrawMessages(size);
        if (_input.ResearchOpen) DrawResearch();
    }

    /// <summary>Above every pop-up: tutorial highlights, the ping wheel, the desync warning and the end screen.</summary>
    private void DrawTopmost()
    {
        var size = Screen;
        DrawTutorialGlow();
        if (_input!.PingWheelOpen) DrawPingWheel();
        DrawDesync(size);
        DrawOutcome(size);
    }

    // ================================================================== layout

    /// <summary>Rebuilds every clickable element and the list of mouse-blocking panels.</summary>
    private void Layout(double delta)
    {
        _tutorialButtons.Clear();
        _researchButtons.Clear();
        _overlayButtons.Clear();
        _swButtons.Clear();
        _barButtons.Clear();
        _toolButtons.Clear();
        _pickerButtons.Clear();
        _chipTips.Clear();
        _panels.Clear();

        LayoutTopBar();
        if (Settings.ShowMinimap) LayoutMinimapPanel();
        LayoutToolbar();
        LayoutTierPicker();
        LayoutSuperWeapons();
        LayoutTutorial();
        if (_storageOpen) LayoutStorageDropdown();
        if (_controlsOpen) LayoutControls();
        if (_input!.ResearchOpen) LayoutResearch();
        ComputeTooltip();
    }

    // ================================================================== top bar

    private void LayoutTopBar()
    {
        var w = _world!;
        var input = _input!;
        var size = Screen;
        _barRect = new Rect2(0, 0, size.X, BarH);
        _panels.Add(_barRect);
        float top = (BarH - ChipH) / 2;

        // Map toggle.
        float x = BarPad;
        _barButtons.Add(new Button(new Rect2(x, top, IconBtnW, ChipH), "map", "", true, Settings.ShowMinimap, ToggleMap, "map",
            new Tip("Map", new[] { Settings.ShowMinimap ? "Hide the minimap." : "Show the minimap." }, TipAnchor.BelowLeft)));
        x += IconBtnW + Gap;

        // Wave chip: flag, wave count, divider, countdown.
        var waves = w.Waves;
        float countW = TextW(HudTheme.Display, WaveCount, 17);
        float chipW = 12 + 20 + 8 + countW + 10 + 1 + 10 + TextW(HudTheme.DisplaySemi, WaveClock, 16) + 12;
        _waveChip = new Rect2(x, top, chipW, ChipH);
        _waveDivider = x + 12 + 20 + 8 + countW + 10;
        _chipTips.Add((_waveChip, new Tip($"Wave {waves.WavesStarted} of {waves.TotalWaves}", WaveTipBody(), TipAnchor.BelowLeft)));
        x = _waveChip.End.X + Gap;

        // Call the next wave now (also releases held waves in Sandbox).
        bool canCall = w.Outcome == Outcome.Playing && waves.HasMoreWaves && (!waves.Held || Sandbox);
        string callBody = !waves.HasMoreWaves ? "No more waves to call."
            : waves.Held && !Sandbox ? "Waves start after the tutorial."
            : waves.Held ? "Start the waves."
            : $"Start wave {waves.WavesStarted + 1} now instead of waiting.";
        _barButtons.Add(new Button(new Rect2(x, top, IconBtnW, ChipH), "call_wave", "", canCall, false, input.CallNextWave, "call_wave",
            new Tip("Call next wave", new[] { callBody }, TipAnchor.BelowLeft)));
        x += IconBtnW + Gap;

        // Speed: pause, 1x, 2x, 3x as one segmented group.
        _speedGroup = new Rect2(x, (BarH - 34) / 2, 4 * IconBtnW, 34);
        for (int i = 0; i < 4; i++)
        {
            float speed = i == 0 ? 0 : Settings.Speeds[i - 1];
            bool active = i == 0 ? SimPaused : !SimPaused && Math.Abs(Settings.GameSpeed - speed) < 0.01f;
            string body = !CanChangeSpeed ? "The host controls game speed."
                : i == 0 ? "Stop the clock. You can still plan and queue orders." : $"Run the game at {speed:0}x.";
            _barButtons.Add(new Button(new Rect2(x + i * IconBtnW, _speedGroup.Position.Y, IconBtnW, 34), i == 0 ? "pause" : $"{speed:0}×", "speed",
                CanChangeSpeed, active, () => SpeedRequested?.Invoke(speed), Tip: new Tip(SpeedTitles[i], new[] { body }, TipAnchor.BelowLeft)));
        }
        x = _speedGroup.End.X + Gap;

        // Nexus integrity.
        _nexusChip = new Rect2(x, top, 12 + 20 + 10 + 160 + 12, ChipH);
        _chipTips.Add((_nexusChip, new Tip("Nexus",
            new[] { $"Integrity {Math.Ceiling(Math.Max(0, w.Nexus.Hp))} / {w.Nexus.MaxHp:0}", "If it falls, the run is over." }, TipAnchor.BelowLeft)));
        float leftEnd = _nexusChip.End.X;

        // Right side, laid out from the right edge.
        float r = size.X - BarPad;
        r -= IconBtnW;
        _barButtons.Add(new Button(new Rect2(r, top, IconBtnW, ChipH), "menu", "", true, false, () => MenuRequested?.Invoke(),
            Tip: new Tip("Menu", new[] { "Settings, restart and quit." }, TipAnchor.BelowRight)));
        r -= Gap + IconBtnW;
        var research = w.Research;
        string resBody = research.Current is { } cur ? $"{cur.Name}: {research.Fraction:P0}" : "Nothing in progress. Click to pick a tech.";
        _barButtons.Add(new Button(new Rect2(r, top, IconBtnW, ChipH), "research", "", true, input.ResearchOpen,
            () => input.ResearchOpen = !input.ResearchOpen, "research", new Tip("Research", new[] { resBody }, TipAnchor.BelowRight)));
        float powerW = 12 + 20 + 8 + 56 + 12;
        r -= Gap + powerW;
        _powerChip = new Rect2(r, top, powerW, ChipH);
        var grid = w.Power.GridOf(w.Nexus);
        _chipTips.Add((_powerChip, new Tip("Base power",
            new[] { grid == null ? "Not connected." : $"In use {grid.Demand:0} / {grid.Capacity:0} kW" }, TipAnchor.BelowRight)));
        r -= Gap;
        _resDivider = r;
        r -= Gap;

        // Resources: every reported item as icon + amount, as many as fit, then "+N".
        _resItems.Clear();
        _resOverflow = 0;
        float maxW = r - leftEnd - 2 * Gap;
        float rx = 12;
        for (int i = 0; i < _report.Count; i++)
        {
            float itemW = 20 + 6 + TextW(HudTheme.DisplaySemi, Amount(_report[i].Count), ResSize) + 14;
            if (rx + itemW + 16 + 12 + 40 > maxW && i < _report.Count - 1)
            {
                _resOverflow = _report.Count - i;
                rx += TextW(HudTheme.DisplaySemi, $"+{_resOverflow}", ResSize) + 14;
                break;
            }
            _resItems.Add((_report[i], rx));
            rx += itemW;
        }
        float resW = rx + 16 + 12;
        _resButton = new Rect2(r - resW, top, resW, ChipH);
        int problems = _report.Count(Trouble);
        _barButtons.Add(new Button(_resButton, "resources", "", true, _storageOpen, ToggleStorage, "storage",
            new Tip("Storage", new[] { problems > 0 ? $"{problems} need attention. Click for details." : "Click for amounts and net flow." }, TipAnchor.BelowRight)));
    }

    /// <summary>Resource amounts in the top bar (Oxanium 16).</summary>
    private const int ResSize = 16;

    /// <summary>Map button and M: shows or hides the minimap (remembered in the settings).</summary>
    public void ToggleMap()
    {
        Settings.ShowMinimap = !Settings.ShowMinimap;
        Settings.Save();
    }

    private string WaveCount => $"{_world!.Waves.WavesStarted}/{_world.Waves.TotalWaves}";

    private string WaveClock
    {
        get
        {
            var waves = _world!.Waves;
            if (waves.Held || !waves.HasMoreWaves) return "–:––";
            int secs = (waves.TicksUntilNextWave + World.TicksPerSecond - 1) / World.TicksPerSecond;
            return $"{secs / 60}:{secs % 60:00}";
        }
    }

    private IReadOnlyList<string> WaveTipBody()
    {
        var w = _world!;
        var waves = w.Waves;
        var body = new List<string>();
        if (waves.Held) body.Add(Sandbox ? "Waves on hold until you call them." : "Waves start after the tutorial.");
        else if (waves.HasMoreWaves) body.Add($"Next wave in {WaveClock}");
        else body.Add("Final wave. Hold out!");
        var preview = string.Join(", ", waves.PreviewNext(w).Select(p => $"{p.Count} {p.Enemy.Name}"));
        if (preview != "") body.Add($"Incoming {preview}");
        body.Add($"On the field {w.Enemies.Count}");
        foreach (var eng in w.Engineers.Where(e => e.PlayerId != Me))
            body.Add($"{PlayerName(eng.PlayerId)}: {(eng.Alive ? DescribeOrder(eng) : "downed")}");
        return body;
    }

    private void DrawTopBar()
    {
        var w = _world!;
        var mouse = GetLocalMousePosition();
        HudTheme.Plate(C, _barRect, cutBR: BarCut, cutBL: BarCut);
        // Cyan glow line along the bottom edge, fading out at both ends.
        GlowLine(new Vector2(BarCut, BarH - 0.5f), new Vector2(_barRect.Size.X - BarCut, BarH - 0.5f), 0.7f, 0f);

        // Wave chip: amber flag and countdown (amber is the wave's colour), count in white.
        Chip(_waveChip, mouse);
        float cy = _waveChip.GetCenter().Y;
        IconAt("wave", HudTheme.Warm, new Vector2(_waveChip.Position.X + 22, cy), 20);
        TextMid(HudTheme.Display, _waveChip.Position.X + 40, cy, WaveCount, 17, HudTheme.Text);
        C.DrawLine(new Vector2(_waveDivider, cy - 10), new Vector2(_waveDivider, cy + 10), HudTheme.ChipBorder, 1);
        var waves = w.Waves;
        bool urgent = !waves.Held && waves.HasMoreWaves && waves.TicksUntilNextWave <= 10 * World.TicksPerSecond;
        GlowText(HudTheme.DisplaySemi, _waveDivider + 11, cy, WaveClock, 16, urgent ? HudTheme.Urgent with { A = Pulse1s() } : HudTheme.Warm);

        // Speed group background, then each segment.
        HudTheme.Button(C, _speedGroup, HudTheme.Look.Normal);
        foreach (var b in _barButtons)
        {
            if (b.Sub == "speed") DrawSpeedButton(b, mouse);
            else DrawBarIconButton(b, mouse);
        }

        // Nexus chip: amber hexagon with a soft glow, amber integrity bar.
        Chip(_nexusChip, mouse);
        cy = _nexusChip.GetCenter().Y;
        var hex = new Vector2(_nexusChip.Position.X + 22, cy);
        C.DrawCircle(hex, 13, HudTheme.Warm with { A = 0.10f });
        C.DrawCircle(hex, 9, HudTheme.Warm with { A = 0.10f });
        IconAt("nexus", HudTheme.Warm, hex, 20);
        float frac = Math.Clamp(w.Nexus.Hp / w.Nexus.MaxHp, 0, 1);
        NexusBar(new Rect2(_nexusChip.Position.X + 42, cy - 4, 160, 8), frac);

        // Divider before power, then the power chip: neutral bolt, cyan usage bar (red when overloaded).
        float dy = BarH / 2;
        C.DrawLine(new Vector2(_resDivider, dy - 12), new Vector2(_resDivider, dy + 12), HudTheme.ChipBorder, 1);
        Chip(_powerChip, mouse);
        cy = _powerChip.GetCenter().Y;
        IconAt("power", HudTheme.Icon, new Vector2(_powerChip.Position.X + 22, cy), 20);
        var grid = w.Power.GridOf(w.Nexus);
        float use = grid == null || grid.Capacity <= 0 ? 0 : Math.Clamp(grid.Demand / grid.Capacity, 0, 1);
        Bar(new Rect2(_powerChip.Position.X + 40, cy - 4, 56, 8), use, grid is { Satisfaction: < 1 } ? HudTheme.Danger : HudTheme.Accent);
    }

    private void DrawSpeedButton(Button b, Vector2 mouse)
    {
        bool pause = b.Label == "pause";
        bool hover = b.Enabled && b.Rect.HasPoint(mouse);
        var tint = pause ? HudTheme.Warm : HudTheme.Accent;
        if (b.Active)
        {
            // Active: tinted background and a 2 px underline in cyan (amber for pause).
            C.DrawRect(b.Rect.Grow(-1), tint with { A = 0.14f });
            C.DrawRect(new Rect2(b.Rect.Position.X + 1, b.Rect.End.Y - 3, b.Rect.Size.X - 2, 2), tint);
        }
        else if (hover) HudTheme.Button(C, b.Rect, HudTheme.Look.Hover);
        var color = b.Active ? pause ? HudTheme.WarmLight : HudTheme.AccentLight : b.Enabled ? HudTheme.Text : HudTheme.Locked;
        if (pause) IconAt("pause", color, b.Rect.GetCenter(), 18);
        else TextCentered(HudTheme.DisplaySemi, b.Rect, b.Label, 15, color);
    }

    private void DrawBarIconButton(Button b, Vector2 mouse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        HudTheme.Button(C, b.Rect, LookOf(b, hover));
        var c = b.Rect.GetCenter();
        var neutral = b.Active ? HudTheme.AccentLight : HudTheme.Icon;
        switch (b.Label)
        {
            case "map":
                IconAt("map", neutral, c, 20);
                break;
            case "call_wave":
                IconAt("call_wave", b.Enabled ? HudTheme.Accent : HudTheme.Locked, c, 20);
                break;
            case "menu":
                IconAt("menu", neutral, c, 20);
                break;
            case "research":
                IconAt("research", neutral, c, 20);
                if (_world!.Research.Current == null) GlowDot(b.Rect.Position + new Vector2(b.Rect.Size.X - 9, 9), 3.5f, HudTheme.Accent);
                break;
            case "resources":
                DrawResourcesButton(b);
                break;
        }
    }

    private void DrawResourcesButton(Button b)
    {
        var w = _world!;
        float cy = b.Rect.GetCenter().Y;
        foreach (var (r, rx) in _resItems)
        {
            float x = b.Rect.Position.X + rx;
            C.DrawTextureRect(Icons.Item(w.Data.Items[r.Item], 20), new Rect2(x, cy - 10, 20, 20), false);
            TextMid(HudTheme.DisplaySemi, x + 26, cy, Amount(r.Count), ResSize, Flashed(r.Item, Trouble(r) ? HudTheme.DangerText : HudTheme.Text));
        }
        if (_resOverflow > 0)
        {
            float x = b.Rect.Position.X + (_resItems.Count > 0 ? _resItems[^1].X + 20 + 6 + TextW(HudTheme.DisplaySemi, Amount(_resItems[^1].Status.Count), ResSize) + 14 : 12);
            TextMid(HudTheme.DisplaySemi, x, cy, $"+{_resOverflow}", ResSize, HudTheme.TextMuted);
        }
        IconAt("chevron", HudTheme.TextMuted, new Vector2(b.Rect.End.X - 20, cy), 16);
        if (_report.Any(Trouble)) GlowDot(b.Rect.Position + new Vector2(3, 3), 5, HudTheme.Danger);
    }

    /// <summary>A small dot with a soft halo (research idle, resource problem).</summary>
    private void GlowDot(Vector2 c, float r, Color color)
    {
        C.DrawCircle(c, r * 2.2f, color with { A = 0.12f });
        C.DrawCircle(c, r * 1.5f, color with { A = 0.22f });
        C.DrawCircle(c, r, color);
    }

    /// <summary>Text vertically centred on <paramref name="centerY"/> with a soft glow in its own colour.</summary>
    private void GlowText(Font f, float x, float centerY, string text, int size, Color c)
    {
        float y = Baseline(f, centerY, size);
        foreach (var o in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
            C.DrawString(f, new Vector2(x, y) + o, text, HorizontalAlignment.Left, -1, size, c with { A = 0.22f });
        C.DrawString(f, new Vector2(x, y), text, HorizontalAlignment.Left, -1, size, c);
    }

    /// <summary>1 px cyan line with a soft glow, its alpha fading from <paramref name="mid"/> in the centre to <paramref name="ends"/> at both ends.</summary>
    private void GlowLine(Vector2 a, Vector2 b, float mid, float ends)
    {
        var m = (a + b) / 2;
        var pts = new[] { a, m, b };
        C.DrawPolylineColors(pts, new[] { HudTheme.Accent with { A = ends * 0.3f }, HudTheme.Accent with { A = mid * 0.3f }, HudTheme.Accent with { A = ends * 0.3f } }, 4);
        C.DrawPolylineColors(pts, new[] { HudTheme.Accent with { A = ends }, HudTheme.Accent with { A = mid }, HudTheme.Accent with { A = ends } }, 1);
    }

    /// <summary>Opacity pulse between 1 and 0.45 on a 1 s loop (urgent countdown, incoming marker).</summary>
    private static float Pulse1s() => 0.725f + 0.275f * MathF.Cos((float)(Time.GetTicksMsec() / 1000.0) * Mathf.Tau);

    /// <summary>
    /// Nexus integrity: amber gradient with a heartbeat glow (two quick pulses then rest, 2.4 s loop) and a pale
    /// damage trail that holds 450 ms after a hit, then drains over 600 ms.
    /// </summary>
    private void NexusBar(Rect2 r, float frac)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (frac < _nexusLast - 0.0001f)
        {
            _trailFrom = Math.Max(TrailValue(now), _nexusLast);
            _trailAt = now;
        }
        _nexusLast = frac;
        float phase = (float)(now % 2.4);
        float beat = Bump(phase, 0.14f) + Bump(phase - 0.3f, 0.14f);
        if (beat > 0)
        {
            C.DrawRect(r.Grow(4), HudTheme.Warm with { A = 0.12f * beat });
            C.DrawRect(r.Grow(2), HudTheme.Warm with { A = 0.22f * beat });
        }
        GradientBar(r, frac, HudTheme.WarmLighter, HudTheme.WarmDeep);
        float trail = Math.Max(frac, TrailValue(now));
        if (trail > frac + 0.0001f)
            C.DrawRect(new Rect2(r.Position.X + r.Size.X * frac, r.Position.Y + 1, r.Size.X * (trail - frac), r.Size.Y - 2), new Color("#FFF1D6"));
    }

    private float TrailValue(double now)
    {
        double age = now - _trailAt;
        return age < 0.45 ? _trailFrom : Mathf.Lerp(_trailFrom, _nexusLast, (float)Math.Min(1, (age - 0.45) / 0.6));
    }

    private static float Bump(float x, float width) => x < 0 || x > width ? 0 : MathF.Sin(x / width * Mathf.Pi);

    /// <summary>A resource amount's colour: flashes green (up) or red (down) when it changes, fading back over 400 ms.</summary>
    private Color Flashed(string item, Color normal)
    {
        if (!_flash.TryGetValue(item, out var f)) return normal;
        float k = (float)((Time.GetTicksMsec() / 1000.0 - f.At) / 0.4);
        return k >= 1 ? normal : (f.Up ? HudTheme.Good : HudTheme.Danger).Lerp(normal, Math.Max(0, k));
    }

    /// <summary>Progress bar with a horizontal gradient fill (left colour to right colour).</summary>
    private void GradientBar(Rect2 r, float frac, Color left, Color right)
    {
        Box(r, HudTheme.ButtonBg, HudTheme.ChipBorder, 1);
        if (frac <= 0) return;
        var fill = new Rect2(r.Position, new Vector2(r.Size.X * frac, r.Size.Y)).Grow(-1);
        var rightNow = left.Lerp(right, frac);
        C.DrawPolygon(new[] { fill.Position, new Vector2(fill.End.X, fill.Position.Y), fill.End, new Vector2(fill.Position.X, fill.End.Y) },
            new[] { left, rightNow, rightNow, left });
    }

    // ================================================================== storage dropdown

    private const float RowH = 36, HintH = 18;

    private void LayoutStorageDropdown()
    {
        float h = 52 + _report.Sum(r => RowH + (Trouble(r) ? HintH : 0)) + 8;
        int depots = _world!.Buildings.Count(b => b is Depot);
        if (depots > 0) h += 22;
        _dropdownRect = new Rect2(_resButton.End.X - DropdownW, BarH + Gap, DropdownW, h);
        _panels.Add(_dropdownRect);

        _resourceRows.Clear();
        float y = _dropdownRect.Position.Y + 48;
        foreach (var r in _report)
        {
            bool trouble = Trouble(r);
            var row = new Rect2(_dropdownRect.Position.X + 8, y, DropdownW - 16, RowH + (trouble ? HintH : 0));
            _resourceRows.Add((row, r, trouble));
            y += row.Size.Y;
        }
    }

    private void DrawStorageDropdown()
    {
        var w = _world!;
        var d = _dropdownRect;
        HudTheme.Popover(C, d);
        HudTheme.SectionLabel(C, new Vector2(d.Position.X + 16, d.Position.Y + 30), "Storage");
        C.DrawString(HudTheme.Body, new Vector2(d.Position.X, d.Position.Y + 30), "Amount · Net flow", HorizontalAlignment.Right, d.Size.X - 16, 12, HudTheme.TextMuted);

        var mouse = GetLocalMousePosition();
        foreach (var (row, r, trouble) in _resourceRows)
        {
            if (trouble) Box(row, HudTheme.DangerBg, HudTheme.DangerBorder);
            else if (row.HasPoint(mouse)) Box(row, HudTheme.PanelBg, HudTheme.ChipBorder);
            float cy = row.Position.Y + RowH / 2;
            var item = w.Data.Items[r.Item];
            C.DrawTextureRect(Icons.Item(item, 24), new Rect2(row.Position.X + 8, cy - 12, 24, 24), false);
            TextMid(HudTheme.BodyMedium, row.Position.X + 42, cy, item.Name, 14, HudTheme.Text);
            float amountRight = row.End.X - 80;
            var amountColor = Flashed(r.Item, trouble ? HudTheme.DangerText : HudTheme.Text);
            C.DrawString(HudTheme.DisplaySemi, new Vector2(row.Position.X, Baseline(HudTheme.DisplaySemi, cy, 15)), Amount(r.Count),
                HorizontalAlignment.Right, amountRight - row.Position.X, 15, amountColor);
            int net = (int)MathF.Round(r.NetPerMinute);
            string flow = net == 0 ? "±0" : net > 0 ? $"+{net}" : $"−{-net}";
            var flowColor = net > 0 ? HudTheme.Good : net < 0 ? HudTheme.Danger : HudTheme.TextMuted;
            C.DrawString(HudTheme.DisplaySemi, new Vector2(row.Position.X, Baseline(HudTheme.DisplaySemi, cy, 13)), flow,
                HorizontalAlignment.Right, row.Size.X - 10, 13, flowColor);
            if (trouble)
                C.DrawString(HudTheme.Body, new Vector2(row.Position.X + 42, row.Position.Y + RowH + 8), FixHint(r, row.Size.X - 54),
                    HorizontalAlignment.Left, row.Size.X - 50, 12, HudTheme.DangerText);
        }
        int depots = w.Buildings.Count(b => b is Depot);
        if (depots > 0)
            TextBaseline(HudTheme.Body, d.Position.X + 16, d.End.Y - 14, $"Nexus + {depots} depot{(depots == 1 ? "" : "s")}", 12, HudTheme.TextMuted);
    }

    /// <summary>One line under a resource in trouble: what's wrong, then the first concrete thing to do.</summary>
    private string FixHint(ResourceStatus r, float width)
    {
        string lead = r.Level switch
        {
            ResourceLevel.Empty => "Empty",
            ResourceLevel.Low => "Running low",
            ResourceLevel.Stuck => "Not reaching the buildings that need it",
            _ => "Falling",
        };
        string? fix = r.Advice.Skip(1).FirstOrDefault(a => a.StartsWith("FIX:") || a.StartsWith("Build") || a.StartsWith("Place"))
                      ?? r.Advice.Skip(1).FirstOrDefault(a => a.StartsWith("STALLED:"));
        string text = fix == null ? lead + "." : $"{lead}. {fix.Replace("FIX: ", "").Replace("STALLED: ", "")}";
        return Ellipsize(HudTheme.Body, text, 12, width);
    }

    private static bool Trouble(ResourceStatus r) => r.Level >= ResourceLevel.Stuck;

    private string ResourceTooltip(ResourceStatus r, out string title)
    {
        var name = _world!.Data.Items[r.Item].Name;
        title = r.Short == "" ? name : $"{name}: {r.Short}";
        return string.Join("\n", r.Advice);
    }

    /// <summary>Refreshes the resource report and pops a message when something gets worse.</summary>
    private void UpdateResources(double delta)
    {
        var w = _world!;
        _watch.Sample(w);
        _reportAge += delta;
        if (_reportAge < 0.5 && _report.Count > 0) return;
        _reportAge = 0;
        _report = _watch.Report(w);
        double flashAt = Time.GetTicksMsec() / 1000.0;
        foreach (var r in _report)
        {
            if (!_flash.TryGetValue(r.Item, out var f)) _flash[r.Item] = (r.Count, -10, true);
            else if (f.Count != r.Count) _flash[r.Item] = (r.Count, flashAt, r.Count > f.Count);
        }

        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var r in _report)
        {
            var before = _alerted.GetValueOrDefault(r.Item);
            _alerted[r.Item] = r.Level;
            if (r.Level <= before || r.Level < ResourceLevel.Stuck || w.Tick < 5 * World.TicksPerSecond) continue;
            if (_alertedAt.TryGetValue(r.Item, out double at) && now - at < 30) continue;
            _alertedAt[r.Item] = now;
            Notify($"{w.Data.Items[r.Item].Name} is {r.Short.ToLowerInvariant()}. Open Resources (top right) to see what to do.");
        }
    }

    // ================================================================== bottom toolbar

    private void LayoutToolbar()
    {
        var size = Screen;
        float y0 = size.Y - ToolbarH;
        _toolbarRect = new Rect2(0, y0, size.X, ToolbarH);
        _panels.Add(_toolbarRect);
        float top = y0 + ToolbarPadTop, h = ToolbarH - ToolbarPadTop - ToolbarPadBottom;

        // Engineer portrait.
        var eng = _world!.Engineers[Me];
        if (_input!.SelectedBuilding != null) _engineerSelected = false;
        _engineerRect = new Rect2(ToolbarSide, top, EngineerW, h);
        _toolButtons.Add(new Button(_engineerRect, IsCoop ? $"Engineer {PlayerName(Me)}" : "Engineer", "hero", true, _engineerSelected, HeroClicked, "hero",
            new Tip("Engineer", new[] { $"Health {Math.Ceiling(Math.Max(0, eng.Hp))} / {Engineer.MaxHp}", HeroStatus(eng),
                "Click to select. Double-click to centre the camera." }, TipAnchor.Above)));

        _commandsPanel = new Rect2(size.X - ToolbarSide - CommandsW, top, CommandsW, h);
        float left = _engineerRect.End.X + ToolbarGap;
        _buildPanel = new Rect2(left, top, _commandsPanel.Position.X - ToolbarGap - left, h);
        _dividers.Clear();
        _dividers.Add(_engineerRect.End.X + ToolbarGap / 2);
        _dividers.Add(_commandsPanel.Position.X - ToolbarGap / 2);

        LayoutBuildPanel();
        LayoutCommands();
    }

    private static string HeroStatus(Engineer eng) => !eng.Alive ? $"Down. Back in {eng.RespawnIn / World.TicksPerSecond + 1}s."
        : eng.Orders.Count == 0 ? "Idle" : eng.Orders[0] switch
        {
            MoveOrder => "Moving",
            BuildOrder => "Building",
            RepairOrder => "Repairing",
            _ => "Working",
        };

    /// <summary>Status badge on the portrait: icon and colour for what the engineer is doing.</summary>
    private static (string Icon, Color Color) HeroBadge(Engineer eng) => !eng.Alive ? ("close", HudTheme.Danger)
        : eng.Orders.Count == 0 ? ("clock", HudTheme.TextMuted) : eng.Orders[0] switch
        {
            MoveOrder => ("move", HudTheme.Accent),
            BuildOrder => ("hammer", HudTheme.Accent),
            RepairOrder => ("wrench", HudTheme.Accent),
            _ => ("clock", HudTheme.Accent),
        };

    /// <summary>Portrait: a click selects the engineer (dropping any building selection), a double-click also centres and follows him.</summary>
    private void HeroClicked()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _input!.Deselect();
        _engineerSelected = true;
        if (now - _lastHeroClick < DoubleClickSeconds) FollowHeroRequested?.Invoke();
        _lastHeroClick = now;
    }

    private void LayoutBuildPanel()
    {
        var w = _world!;
        var input = _input!;
        _shownCategory = input.CardCategory ?? _shownCategory;
        var p = _buildPanel;

        // One header row over the whole build area (category name, unlocked count); below it the category dock and
        // the slots share the same top and bottom.
        _slotHeader = new Rect2(p.Position.X, p.Position.Y, p.Size.X, SlotHeaderH);
        float slotTop = p.Position.Y + SlotHeaderH + 4;
        float rowH = p.End.Y - slotTop;

        // Category dock: icon-only squares, 3 columns x 2 rows; the names are in the tooltips and the header.
        float cell = (rowH - DockGap) / 2;
        for (int i = 0; i < PlayerInput.Categories.Length; i++)
        {
            var (id, label) = PlayerInput.Categories[i];
            var families = w.Data.FamiliesIn(id);
            int unlocked = families.Count(f => f.Any(w.Research.IsUnlocked));
            var rect = new Rect2(p.Position.X + i % 3 * (cell + DockGap), slotTop + i / 3 * (cell + DockGap), cell, cell);
            // The dock stays up while a building is selected: picking a category drops the selection.
            _toolButtons.Add(new Button(rect, label, "category", true, input.CardCategory == id, () =>
            {
                input.Deselect();
                input.OpenCategory(id);
            }, $"cat:{id}",
                new Tip(label, new[] { CategoryTooltip(id), $"{unlocked} of {families.Count} unlocked" }, TipAnchor.Above)));
        }
        float dockW = 3 * cell + 2 * DockGap;
        _dockDivider = p.Position.X + dockW + ToolbarGap / 2;

        // One slot per family: the tier in use shows; its ^ toggle opens the tier picker. A selected building's info
        // and actions take this area instead (the dock stays).
        float sx = p.Position.X + dockW + ToolbarGap;
        _slotFamilies = w.Data.FamiliesIn(_shownCategory);
        float slotH = p.End.Y - slotTop;
        _slotArea = new Rect2(sx, slotTop, p.End.X - sx, slotH);
        if (input.SelectedBuilding is { } sel)
        {
            LayoutSelection(sel);
            return;
        }
        float slotW = (p.End.X - sx - 7 * Gap) / 8;
        string shown = _shownCategory;
        for (int i = 0; i < 8; i++)
        {
            var rect = new Rect2(sx + i * (slotW + Gap), slotTop, slotW, slotH);
            if (i >= _slotFamilies.Count)
            {
                _toolButtons.Add(new Button(rect, "", "unused", false, false, () => { }));
                continue;
            }
            var family = _slotFamilies[i];
            var def = input.BuildTool is { } tool && family.Contains(tool) ? tool : input.SlotVariant(family);
            bool unlocked = family.Any(w.Research.IsUnlocked);
            string familyId = PlayerInput.FamilyOf(def);
            // Tier toggle first, so it wins the click over the slot underneath it.
            if (family.Count > 1)
                _toolButtons.Add(new Button(new Rect2(rect.End.X - 26, rect.Position.Y + 4, 22, 22), "", "tiers", true, _pickerKey == familyId,
                    () => _pickerKey = _pickerKey == familyId ? null : familyId, $"tiers:{familyId}",
                    new Tip("Tiers", new[] { _pickerKey == familyId ? "Hide the other tiers." : "Show every tier of this building." }, TipAnchor.Above)));
            var body = new List<string>();
            if (w.Research.IsUnlocked(def)) body.Add("Cost " + CostText(def.Cost, full: true));
            else
            {
                body.Add("Locked. Unlock it in Research.");
                if (def.Tech != null) body.Add($"Needs {w.Data.Techs[def.Tech].Name}");
            }
            // Locked families stay enabled so they can be hovered (picking one says what to research).
            _toolButtons.Add(new Button(rect, def.Name, unlocked ? "slot" : "locked", true, input.BuildTool != null && family.Contains(input.BuildTool), () =>
            {
                if (input.CardCategory != shown) input.OpenCategory(shown);
                input.PickSlot(family);
            }, familyId, new Tip(def.Name, body, TipAnchor.Above), def.Id));
        }
    }

    // ------------------------------------------------------------------ tier picker

    private const float CardW = 190, CardH = 124;

    /// <summary>
    /// The tier picker opens above a slot when its ^ toggle is clicked, and stays until the toggle is clicked again,
    /// a tier is picked, Esc, or a click anywhere else.
    /// </summary>
    private void LayoutTierPicker()
    {
        if (_pickerForce != null) _pickerKey = _pickerForce;
        var slot = _pickerKey == null ? null : _toolButtons.FirstOrDefault(b => b.Sub is "slot" or "locked" && b.Key == _pickerKey);
        if (slot == null || _input!.SelectedBuilding != null)
        {
            _pickerKey = null;
            _pickerFamily = null;
            return;
        }

        var w = _world!;
        var input = _input!;
        var family = _slotFamilies.First(f => PlayerInput.FamilyOf(f[0]) == slot.Key);
        _pickerFamily = family;
        float width = family.Count * CardW + (family.Count - 1) * Gap + 24;
        float height = 12 + 22 + 8 + CardH + 12;
        float x = Math.Clamp(slot.Rect.GetCenter().X - width / 2, 8, Screen.X - 8 - width);
        _pickerRect = new Rect2(x, slot.Rect.Position.Y - 12 - height, width, height);
        _pickerCaretX = slot.Rect.GetCenter().X;
        _panels.Add(_pickerRect);

        string category = _shownCategory;
        var inUse = input.BuildTool is { } t && family.Contains(t) ? t : input.SlotVariant(family);
        for (int i = 0; i < family.Count; i++)
        {
            var d = family[i];
            bool unlocked = w.Research.IsUnlocked(d);
            string status = !unlocked ? "Unlock in Research" : d == inUse ? "In use" : "Click to use";
            var card = new Rect2(x + 12 + i * (CardW + Gap), _pickerRect.Position.Y + 12 + 22 + 8, CardW, CardH);
            _pickerButtons.Add(new Button(card, d.Name, status, unlocked, d == inUse && unlocked, () =>
            {
                if (input.CardCategory != category) input.OpenCategory(category);
                input.SelectVariant(d);
                _pickerKey = null;
            }, d.Id, null, d.Id));
        }
    }

    private void DrawTierPicker()
    {
        if (_pickerFamily is not { } family) return;
        var w = _world!;
        var r = _pickerRect;
        HudTheme.Popover(C, r);
        // Caret pointing down at the slot.
        float cx = Math.Clamp(_pickerCaretX, r.Position.X + 14, r.End.X - 14);
        C.DrawColoredPolygon(new[] { new Vector2(cx - 8, r.End.Y - 1), new Vector2(cx + 8, r.End.Y - 1), new Vector2(cx, r.End.Y + 8) }, HudTheme.PopoverBg);
        C.DrawPolyline(new[] { new Vector2(cx - 8, r.End.Y), new Vector2(cx, r.End.Y + 8), new Vector2(cx + 8, r.End.Y) }, HudTheme.ChipBorder, 1);

        HudTheme.SectionLabel(C, new Vector2(r.Position.X + 12, r.Position.Y + 28), family[0].Name);
        string hint = !family.Any(w.Research.IsUnlocked) ? "Not researched yet" : family.Count > 1 ? "Choose a tier" : "Click the slot to build";
        C.DrawString(HudTheme.Body, new Vector2(r.Position.X, r.Position.Y + 28), hint, HorizontalAlignment.Right, r.Size.X - 12, 12, HudTheme.TextMuted);

        var mouse = GetLocalMousePosition();
        for (int i = 0; i < _pickerButtons.Count; i++)
        {
            var b = _pickerButtons[i];
            var d = w.Data.Buildings[b.DefId!];
            bool hover = b.Enabled && b.Rect.HasPoint(mouse);
            var cr = hover ? b.Rect with { Position = b.Rect.Position - new Vector2(0, 2) } : b.Rect;
            HudTheme.Button(C, cr, b.Active ? HudTheme.Look.Selected : hover ? HudTheme.Look.Hover : HudTheme.Look.Normal);
            if (hover) C.DrawRect(cr, HudTheme.Accent, false, 1);
            var well = new Rect2(cr.Position + new Vector2(10, 10), new Vector2(56, 56));
            ArtWell(well, d, !b.Enabled);
            float tx = well.End.X + 10;
            C.DrawString(HudTheme.Section, new Vector2(tx, cr.Position.Y + 30), $"TIER {Roman(i + 1)}", HorizontalAlignment.Left, -1, 11,
                b.Enabled ? HudTheme.AccentLight : HudTheme.Locked);
            C.DrawString(HudTheme.BodySemi, new Vector2(tx, cr.Position.Y + 50), Ellipsize(HudTheme.BodySemi, d.Name, 13, cr.End.X - tx - 8),
                HorizontalAlignment.Left, -1, 13, b.Enabled ? HudTheme.Text : HudTheme.TextMuted);
            float sy = cr.Position.Y + 88;
            if (b.Active)
            {
                IconAt("check", HudTheme.Accent, new Vector2(cr.Position.X + 18, sy - 4), 14);
                C.DrawString(HudTheme.BodySemi, new Vector2(cr.Position.X + 30, sy), "In use", HorizontalAlignment.Left, -1, 12, HudTheme.AccentLight);
            }
            else C.DrawString(HudTheme.BodyMedium, new Vector2(cr.Position.X + 10, sy), b.Sub, HorizontalAlignment.Left, -1, 12,
                b.Enabled ? HudTheme.Text : HudTheme.Locked);
            string detail = b.Enabled ? CostText(d.Cost, full: true) : d.Tech != null ? $"Needs {w.Data.Techs[d.Tech].Name}" : "";
            C.DrawString(HudTheme.Body, new Vector2(cr.Position.X + 10, sy + 18), Ellipsize(HudTheme.Body, detail, 11, cr.Size.X - 20),
                HorizontalAlignment.Left, -1, 11, HudTheme.TextMuted);
        }
    }

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => n.ToString() };

    // ------------------------------------------------------------------ commands

    private void LayoutCommands()
    {
        var input = _input!;
        var p = _commandsPanel;
        float bw = (p.Size.X - 8) / 2, bh = (p.Size.Y - 8) / 2;
        Rect2 Cell(int i) => new(p.Position.X + i % 2 * (bw + 8), p.Position.Y + i / 2 * (bh + 8), bw, bh);
        _toolButtons.Add(new Button(Cell(0), "Research", "research", true, input.ResearchOpen, () => input.ResearchOpen = !input.ResearchOpen, "research",
            new Tip("Research", new[] { "Pick technologies to unlock new buildings." }, TipAnchor.Above)));
        _toolButtons.Add(new Button(Cell(1), "Deconstruct", "deconstruct", true, input.DeconstructTool, input.ToggleDeconstructTool, "deconstruct",
            new Tip("Deconstruct", new[] { "Click or drag over buildings to remove them for a full refund." }, TipAnchor.Above)));
        _toolButtons.Add(new Button(Cell(2), "Ping", "ping", true, input.PingTool, input.TogglePingTool, "ping",
            new Tip("Ping", new[] { "Click the map to mark a spot for your team." }, TipAnchor.Above)));
        _toolButtons.Add(new Button(Cell(3), "Controls", "controls", true, _controlsOpen, ToggleControls, "controls",
            new Tip("Controls", new[] { "Every key and mouse binding." }, TipAnchor.Above)));
    }

    private string CategoryTooltip(string id) => id switch
    {
        "logistics" => "Belts, inserters, tunnels and splitters.",
        "production" => "Drills, furnaces, munitions and labs.",
        "power" => "Generators and power poles.",
        "walls" => "Walls of every tier.",
        "turrets" => "Gun, laser, rocket, flame and heavy turrets.",
        "special" => "Storage depots and super weapons.",
        _ => "",
    };

    private static string CategoryIcon(string id) => id switch
    {
        "logistics" => "belt",
        "production" => "smelter",
        "power" => "power",
        "walls" => "wall",
        "turrets" => "turret",
        _ => "beacon",
    };

    private void DrawToolbar()
    {
        var mouse = GetLocalMousePosition();
        var t = _toolbarRect;
        HudTheme.Plate(C, t, cutTL: ToolbarCut, cutTR: ToolbarCut);
        // Top edge: glow line brighter at the ends, a glowing diamond at each end, a short bright segment in the middle.
        float y0 = t.Position.Y + 0.5f;
        GlowLine(new Vector2(ToolbarCut, y0), new Vector2(t.End.X - ToolbarCut, y0), 0.3f, 0.9f);
        GlowDiamond(new Vector2(ToolbarCut, y0), 7);
        GlowDiamond(new Vector2(t.End.X - ToolbarCut, y0), 7);
        var seg = new Rect2(t.GetCenter().X - 60, t.Position.Y - 1, 120, 3);
        C.DrawRect(seg.Grow(3), HudTheme.Accent with { A = 0.12f });
        C.DrawRect(seg, HudTheme.Accent);

        foreach (float x in _dividers) FadeDivider(x, t.Position.Y + ToolbarPadTop, t.End.Y - ToolbarPadBottom);
        if (_dockDivider > 0) FadeDivider(_dockDivider, _slotHeader.End.Y + 4, t.End.Y - ToolbarPadBottom);

        var h = _slotHeader;
        HudTheme.SectionLabel(C, new Vector2(h.Position.X, h.Position.Y + 13), PlayerInput.Categories.First(c => c.Id == _shownCategory).Label);
        if (_input!.SelectedBuilding is { } sel)
        {
            HudTheme.SectionLabel(C, new Vector2(_slotArea.Position.X, h.Position.Y + 13), "Selected");
            DrawSelectionInfo(sel);
        }
        else
        {
            int unlocked = _slotFamilies.Count(f => f.Any(_world!.Research.IsUnlocked));
            C.DrawString(HudTheme.Body, new Vector2(h.Position.X, h.Position.Y + 13), $"{unlocked} of {_slotFamilies.Count} unlocked",
                HorizontalAlignment.Right, h.Size.X, 12, HudTheme.TextMuted);
        }
        foreach (var b in _toolButtons.Where(b => b.Sub == "tiers")) _tierToggles.Add(b);

        foreach (var b in _toolButtons)
        {
            switch (b.Sub)
            {
                case "hero":
                    DrawHero(b, mouse);
                    break;
                case "category":
                    DrawCategory(b, mouse);
                    break;
                case "slot" or "locked" or "unused":
                    DrawSlot(b, mouse);
                    break;
                case "tiers":
                    break; // drawn on top of the slots below
                case "research" or "deconstruct" or "ping" or "controls":
                    DrawCommand(b, mouse);
                    break;
                case "action":
                    DrawAction(b, mouse);
                    break;
                case "mode":
                    DrawMode(b, mouse);
                    break;
                case "card":
                    DrawItemCard(b, mouse);
                    break;
                case "close":
                    break; // drawn with the selection header
                default:
                    DrawCardButton(b, mouse, compact: true);
                    break;
            }
        }
        DrawTierToggles(mouse);
    }

    private readonly List<Button> _tierToggles = new();

    /// <summary>The ^ tier toggles in the corner of multi-tier slots (open: cyan, pointing down).</summary>
    private void DrawTierToggles(Vector2 mouse)
    {
        foreach (var b in _tierToggles)
        {
            var r = b.Rect;
            HudTheme.Button(C, r, b.Active ? HudTheme.Look.Selected : r.HasPoint(mouse) ? HudTheme.Look.Hover : HudTheme.Look.Normal);
            IconAt(b.Active ? "chevron" : "chevron_up", b.Active ? HudTheme.AccentLight : HudTheme.Icon, r.GetCenter(), 14);
        }
        _tierToggles.Clear();
    }

    /// <summary>1 px vertical divider that fades out at the top and bottom.</summary>
    private void FadeDivider(float x, float top, float bottom)
    {
        var c = HudTheme.PopoverTopEdge;
        C.DrawPolylineColors(new[] { new Vector2(x, top), new Vector2(x, (top + bottom) / 2), new Vector2(x, bottom) },
            new[] { c with { A = 0 }, c with { A = 0.8f }, c with { A = 0 } }, 1);
    }

    private void GlowDiamond(Vector2 c, float size)
    {
        float h = size / 2;
        C.DrawCircle(c, size, HudTheme.Accent with { A = 0.15f });
        C.DrawColoredPolygon(new[] { c + new Vector2(0, -h), c + new Vector2(h, 0), c + new Vector2(0, h), c + new Vector2(-h, 0) }, HudTheme.AccentLight);
    }

    private void DrawHero(Button b, Vector2 mouse)
    {
        var eng = _world!.Engineers[Me];
        bool hover = b.Rect.HasPoint(mouse);
        var r = b.Rect;
        HudTheme.Button(C, r, LookOf(b, hover));
        if (b.Active)
        {
            HudTheme.Brackets(C, r, 10, HudTheme.AccentLight);
            Shimmer(r);
        }
        var well = new Rect2(r.GetCenter().X - 40, r.Position.Y + 10, 80, 80);
        DashedRect(well, HudTheme.ChipBorder);
        // Prop Studio portrait in the player's colour (greyed while downed); the stroke icon if it's missing.
        if (Portrait(eng.PlayerId) is { } portrait)
            C.DrawTextureRect(portrait, well, false, eng.Alive ? Colors.White : new Color(0.45f, 0.45f, 0.5f, 0.8f));
        else IconAt("engineer", eng.Alive ? WorldView.PlayerColor(eng.PlayerId) : HudTheme.Locked, well.GetCenter(), 56);
        var (icon, color) = HeroBadge(eng);
        var badge = well.End - new Vector2(4, 4);
        C.DrawCircle(badge, 12, HudTheme.PopoverBg);
        C.DrawArc(badge, 12, 0, Mathf.Tau, 24, color, 1.5f, true);
        IconAt(icon, color, badge, 14);
        float frac = eng.Alive ? Math.Clamp(eng.Hp / Engineer.MaxHp, 0, 1) : 0;
        Bar(new Rect2(r.GetCenter().X - 44, well.End.Y + 12, 88, 4), frac, frac > 0.35f ? HudTheme.Good : HudTheme.Danger);
        TextCentered(HudTheme.BodyMedium, new Rect2(r.Position.X, well.End.Y + 20, r.Size.X, 18), b.Label, 12, b.Active ? HudTheme.AccentLight : HudTheme.TextMuted);
    }

    /// <summary>Category dock button: icon only (the name is in the tooltip and above the slots).</summary>
    private static readonly Dictionary<int, Texture2D?> Portraits = new();

    /// <summary>The engineer's 80 x 80 HUD portrait for a player (the @2x render, drawn at half size), or null without art.</summary>
    private static Texture2D? Portrait(int player)
    {
        if (Portraits.TryGetValue(player, out var cached)) return cached;
        string path = $"res://art/props/portraits/portrait_engineer_p{player + 1}@2x.png";
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && Image.LoadFromFile(ProjectSettings.GlobalizePath(path)) is { } img && !img.IsEmpty())
            tex = ImageTexture.CreateFromImage(img); // checkout the editor hasn't imported yet
        return Portraits[player] = tex;
    }

    private void DrawCategory(Button b, Vector2 mouse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        bool shown = b.Key == $"cat:{_shownCategory}";
        HudTheme.Button(C, b.Rect, b.Active ? HudTheme.Look.Selected : hover || shown ? HudTheme.Look.Hover : HudTheme.Look.Normal);
        IconAt(CategoryIcon(b.Key![4..]), b.Active || shown ? HudTheme.AccentLight : HudTheme.Icon, b.Rect.GetCenter(), 24);
        if (b.Active) HudTheme.Brackets(C, b.Rect, 7, HudTheme.AccentLight);
    }

    private void DrawSlot(Button b, Vector2 mouse)
    {
        if (b.Sub == "unused")
        {
            HudTheme.Button(C, b.Rect, HudTheme.Look.Normal);
            C.DrawRect(b.Rect, HudTheme.PlateBottom with { A = 0.7f });
            return;
        }
        var w = _world!;
        bool hover = b.Rect.HasPoint(mouse) || _pickerKey == b.Key && _pickerFamily != null; // its tiers are showing
        bool locked = b.Sub == "locked";
        var r = hover ? b.Rect with { Position = b.Rect.Position - new Vector2(0, 2) } : b.Rect;
        HudTheme.Button(C, r, b.Active ? HudTheme.Look.Selected : hover ? HudTheme.Look.Hover : HudTheme.Look.Normal);
        if (hover && !b.Active) C.DrawRect(r, HudTheme.Accent, false, 1);
        if (b.Active)
        {
            HudTheme.Brackets(C, r, 10, HudTheme.AccentLight);
            Shimmer(r);
        }
        var def = w.Data.Buildings[b.DefId!];
        var family = _slotFamilies.FirstOrDefault(f => f.Contains(def)) ?? new List<BuildingDef> { def };
        var well = new Rect2(r.GetCenter().X - ArtWellPx / 2, r.Position.Y + 6, ArtWellPx, ArtWellPx);
        ArtWell(well, def, locked);

        string name = Ellipsize(HudTheme.BodyMedium, b.Label, 12, r.Size.X - 8);
        TextCentered(HudTheme.BodyMedium, new Rect2(r.Position.X, well.End.Y + 2, r.Size.X, 16), name, 12,
            locked ? HudTheme.TextMuted : b.Active ? HudTheme.AccentLight : HudTheme.Text);

        // Tier pips: cyan with a glow = in use, grey = unlocked, dark = locked.
        if (family.Count <= 1) return;
        const float pw = 14, ph = 3, pg = 3;
        float px = r.GetCenter().X - (family.Count * pw + (family.Count - 1) * pg) / 2, py = well.End.Y + 21;
        foreach (var d in family)
        {
            var pip = new Rect2(px, py, pw, ph);
            if (d == def && !locked)
            {
                C.DrawRect(pip.Grow(2), HudTheme.Accent with { A = 0.25f });
                C.DrawRect(pip, HudTheme.Accent);
            }
            else C.DrawRect(pip, w.Research.IsUnlocked(d) ? HudTheme.TextMuted : HudTheme.ButtonBorder);
            px += pw + pg;
        }
    }

    /// <summary>Dashed art well with the building's sprite (or its icon if it has none); locked = dimmed with a lock badge.</summary>
    private void ArtWell(Rect2 well, BuildingDef def, bool locked)
    {
        C.DrawRect(well, new Color(0, 0, 0, 0.18f));
        DashedRect(well, HudTheme.ButtonBorder);
        var modulate = Colors.White with { A = locked ? 0.35f : 1f };
        if (!BuildingSprites.Shared.DrawFit(C, def.Id, well.Grow(-4), modulate))
            IconAt(Icons.ForBuilding(def), Icons.BuildingColor(def) with { A = modulate.A }, well.GetCenter(), well.Size.X * 0.62f);
        if (!locked) return;
        var badge = well.End - new Vector2(6, 6);
        C.DrawCircle(badge, 9, HudTheme.PopoverBg);
        C.DrawArc(badge, 9, 0, Mathf.Tau, 20, HudTheme.Locked, 1, true);
        IconAt("lock", HudTheme.TextMuted, badge, 11);
    }

    private void DashedRect(Rect2 r, Color color)
    {
        var p = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position };
        for (int i = 0; i < 4; i++) C.DrawDashedLine(p[i], p[i + 1], color, 1, 4);
    }

    /// <summary>A soft light band that sweeps left to right across a selected element, then rests (3.2 s loop).</summary>
    private void Shimmer(Rect2 r)
    {
        float t = (float)(Time.GetTicksMsec() / 1000.0 % 3.2) / 3.2f;
        if (t > 0.5f) return; // resting half of the loop
        float bw = r.Size.X * 0.45f;
        float xa = r.Position.X - bw + (r.Size.X + bw) * (t / 0.5f), xm = xa + bw / 2, xb = xa + bw;
        var peak = new Color(166 / 255f, 240 / 255f, 1f, 0.16f);
        void Quad(float x0, float x1, float a0, float a1)
        {
            float c0 = Math.Max(x0, r.Position.X), c1 = Math.Min(x1, r.End.X);
            if (c1 <= c0) return;
            float A(float x) => Mathf.Lerp(a0, a1, (x - x0) / (x1 - x0));
            var l = peak with { A = peak.A * A(c0) };
            var rr = peak with { A = peak.A * A(c1) };
            C.DrawPolygon(new[] { new Vector2(c0, r.Position.Y), new Vector2(c1, r.Position.Y), new Vector2(c1, r.End.Y), new Vector2(c0, r.End.Y) },
                new[] { l, rr, rr, l });
        }
        Quad(xa, xm, 0, 1);
        Quad(xm, xb, 1, 0);
    }

    private void DrawCommand(Button b, Vector2 mouse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        HudTheme.Button(C, b.Rect, LookOf(b, hover));
        var c = b.Rect.GetCenter();
        IconAt(b.Sub, b.Active ? HudTheme.AccentLight : HudTheme.Icon, c - new Vector2(0, 9), 22);
        TextCentered(HudTheme.BodyMedium, new Rect2(b.Rect.Position.X, c.Y + 6, b.Rect.Size.X, 18), b.Label, 12, b.Active ? HudTheme.AccentLight : HudTheme.Text);
    }

    /// <summary>Card-style button with a label and a small second line (selection actions, research, super weapons).</summary>
    private void DrawCardButton(Button b, Vector2 mouse, bool compact = false)
    {
        bool hover = b.Rect.HasPoint(mouse);
        HudTheme.Button(C, b.Rect, LookOf(b, hover));
        var color = !b.Enabled ? HudTheme.Locked : b.Active ? HudTheme.AccentText : HudTheme.Text;
        int labelSize = compact ? 12 : 13, subSize = compact ? 11 : 12;
        float x = b.Rect.Position.X + 10, width = b.Rect.Size.X - 20;
        if (b.Sub == "")
        {
            TextCentered(HudTheme.BodySemi, b.Rect, b.Label, labelSize, color);
            return;
        }
        float cy = b.Rect.GetCenter().Y;
        C.DrawString(HudTheme.BodySemi, new Vector2(x, cy - (compact ? 1 : 2)), Ellipsize(HudTheme.BodySemi, b.Label, labelSize, width), HorizontalAlignment.Left, width, labelSize, color);
        C.DrawString(HudTheme.Body, new Vector2(x, cy + (compact ? 12 : 14)), Ellipsize(HudTheme.Body, b.Sub, subSize, width), HorizontalAlignment.Left, width, subSize, HudTheme.TextMuted);
    }

    // ================================================================== selection (replaces the slots; the dock stays)

    private const float SelBlockMaxW = 500, ModeW = 140, CardW2 = 100, CardMinW = 44, CardIconOnlyW = 72, LowStock = 5;
    private Rect2 _selBlock, _sideBlock;
    private readonly List<(string Label, float X)> _cardGroups = new();
    private readonly Dictionary<string, int> _cardCounts = new();
    private string _filterSummary = "";

    /// <summary>
    /// Selected building: on the left its portrait, name and HP, a chip row showing where items come from and go
    /// (inserters), and its actions as icon buttons; on the right an inserter's FILTER picker, or the building's
    /// details. A close button in the header deselects.
    /// </summary>
    private void LayoutSelection(Building sel)
    {
        var a = _slotArea;
        _selBlock = new Rect2(a.Position, new Vector2(Math.Min(SelBlockMaxW, a.Size.X * 0.36f), a.Size.Y));
        _sideBlock = new Rect2(_selBlock.End.X + ToolbarGap * 1.5f, a.Position.Y, a.End.X - _selBlock.End.X - ToolbarGap * 1.5f, a.Size.Y);
        _dividers.Add(_selBlock.End.X + ToolbarGap * 0.75f);
        _toolButtons.Add(new Button(new Rect2(_selBlock.End.X - 22, _slotHeader.Position.Y - 2, 22, 22), "close", "close", true, false, _input!.Deselect, "back",
            new Tip("Close", new[] { "Back to building." }, TipAnchor.Above)));

        // Actions: icon above label, one row under the info.
        var actions = BuildingCommands(sel).Where(b => b != null).Select(b => b!).ToList();
        const float gap = 8;
        float ay = _selBlock.Position.Y + 62, ah = _selBlock.End.Y - ay;
        float aw = Math.Min(116, (_selBlock.Size.X - (actions.Count - 1) * gap) / Math.Max(1, actions.Count));
        for (int i = 0; i < actions.Count; i++)
            _toolButtons.Add(actions[i] with { Rect = new Rect2(_selBlock.Position.X + i * (aw + gap), ay, aw, ah), Sub = "action" });

        _cardGroups.Clear();
        _cardCounts.Clear();
        if (sel is Inserter ins) LayoutFilter(ins);
    }

    /// <summary>Inserter FILTER: Any / Auto / One item, then item cards for what the source holds and what it doesn't.</summary>
    private void LayoutFilter(Inserter ins)
    {
        var w = _world!;
        var input = _input!;
        var b = _sideBlock;
        bool one = !ins.AutoFilter && ins.Filter != null;
        string? autoItem = ins.AutoFilter ? ins.Filter : null;
        _filterSummary = ins.AutoFilter ? autoItem != null ? $"Auto: moving {ItemName(autoItem)}" : "Auto: nothing to move yet"
            : one ? $"Moving {ItemName(ins.Filter)} only" : "Moving any item";

        // Mode column.
        float mh = (b.Size.Y - 2 * 6) / 3;
        var source = Inserter.LikelySource(w, ins.Origin, ins.Dir, ins.Input);
        var stocked = SourceStock(source);
        string? firstItem = stocked.Select(s => s.Item).FirstOrDefault() ?? PlayerInput.FilterChoices.FirstOrDefault(f => f != null);
        (string Label, bool Active, Action Click, string Key, string Tip)[] modes =
        {
            ("Any item", !ins.AutoFilter && ins.Filter == null, () => input.SetSelectedFilter(null), "filter:", "Take whatever the source offers."),
            ("Auto", ins.AutoFilter, input.SetSelectedAutoFilter, "filter:auto", "Let it choose: what the building in front needs, or what the one behind makes."),
            ("One item", one, () => { if (!one && firstItem != null) input.SetSelectedFilter(firstItem); }, "filter:one", "Move only the item picked on the right."),
        };
        for (int i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            _toolButtons.Add(new Button(new Rect2(b.Position.X, b.Position.Y + i * (mh + 6), ModeW, mh), m.Label, "mode", true, m.Active, m.Click, m.Key,
                new Tip(m.Label, new[] { m.Tip }, TipAnchor.Above, 260)));
        }

        // Item cards: stocked at the source (with counts), then items it could carry that the source doesn't hold. Every
        // card fits: they narrow (icon only below CardIconOnlyW) before anything is left out, and the picked one never is.
        float x = b.Position.X + ModeW + 16, top = b.Position.Y + 18, ch = b.End.Y - top, right = b.End.X;
        string? picked = one ? ins.Filter : autoItem;
        var missing = WantedItems(ins, source).Where(i => stocked.All(s => s.Item != i)).ToList();
        float GroupGap() => stocked.Count > 0 && missing.Count > 0 ? 16 : 0;
        int n = stocked.Count + missing.Count;
        float cw = n == 0 ? CardW2 : Mathf.Clamp((right - x - GroupGap() - 8 * (n - 1)) / n, CardMinW, CardW2);
        int room = (int)((right - x - GroupGap() + 8) / (cw + 8));
        if (n > room)
        {
            // Still too many at the narrowest: drop items the source doesn't hold (keeping the picked one), then stock.
            missing = missing.Where(i => i == picked).Concat(missing.Where(i => i != picked)).Take(Math.Max(0, room - stocked.Count)).ToList();
            if (stocked.Count > room)
                stocked = stocked.Where(s => s.Item == picked).Concat(stocked.Where(s => s.Item != picked)).Take(room).ToList();
        }
        void Card(string item, int count, bool inStock)
        {
            _cardCounts[item] = inStock ? count : -1;
            _toolButtons.Add(new Button(new Rect2(x, top, cw, ch), w.Data.Items[item].Name, "card", true, item == picked, () => input.SetSelectedFilter(item), $"filter:{item}",
                new Tip(w.Data.Items[item].Name, new[] { inStock ? $"{count} here. Click to move only this." : "None here right now. Click to move only this." }, TipAnchor.Above, 240)));
            x += cw + 8;
        }
        if (stocked.Count > 0) _cardGroups.Add(($"In {source!.Def.Name}", x));
        foreach (var (item, count) in stocked) Card(item, count, true);
        if (missing.Count > 0)
        {
            x += GroupGap();
            _cardGroups.Add((stocked.Count > 0 ? "Not stocked here" : source == null ? "Nothing to take from" : $"Not in {source.Def.Name}", x));
            foreach (var item in missing) Card(item, 0, false);
        }
    }

    /// <summary>What the inserter's source holds right now, by name so cards don't jump around as counts change.</summary>
    private List<(string Item, int Count)> SourceStock(Building? source)
    {
        var counts = new Dictionary<string, int>();
        void Add(string item, int n) => counts[item] = counts.GetValueOrDefault(item) + n;
        switch (source)
        {
            case Nexus nx: foreach (var (item, n) in nx.Storage.Entries) Add(item, n); break;
            case Depot d: foreach (var (item, n) in d.Storage.Entries) Add(item, n); break;
            case Crafter c: foreach (var (item, n) in c.Output.Entries) Add(item, n); break;
            case Belt belt: foreach (var it in belt.Items) Add(it.Item, 1); break;
        }
        return counts.Where(kv => kv.Value > 0).OrderBy(kv => _world!.Data.Items[kv.Key].Name, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Items worth offering as a filter: what the target uses, else the usual filter choices.</summary>
    private IEnumerable<string> WantedItems(Inserter ins, Building? source)
    {
        var w = _world!;
        var target = w.BuildingAt(ins.Front);
        var wanted = new List<string>();
        if (target is Crafter c) wanted.AddRange(c.Recipes.SelectMany(r => r.Inputs.Keys));
        if (target is Turret { UsesAmmo: true } tu) wanted.Add(tu.Def.AmmoItem!);
        if (source is Drill { Ore: { } ore }) wanted.Add(ore);
        wanted.AddRange(PlayerInput.FilterChoices.Where(f => f != null).Select(f => f!));
        return wanted.Distinct();
    }

    private void DrawSelectionInfo(Building b)
    {
        var w = _world!;
        var s = _selBlock;
        var mouse = GetLocalMousePosition();
        var well = new Rect2(s.Position, new Vector2(56, 56));
        ArtWell(well, b.Def, false);
        float tx = well.End.X + 10, tw = s.End.X - tx;
        string title = b is ConstructionSite ? $"{b.Def.Name} (site)" : b.Def.Name;
        C.DrawString(HudTheme.DisplaySemi, new Vector2(tx, s.Position.Y + 15), Ellipsize(HudTheme.DisplaySemi, title, 16, tw - 70), HorizontalAlignment.Left, -1, 16, HudTheme.Text);
        C.DrawString(HudTheme.DisplaySemi, new Vector2(tx, s.Position.Y + 15), $"{Math.Ceiling(b.Hp)} / {b.MaxHp:0}", HorizontalAlignment.Right, tw, 13, HudTheme.TextMuted);
        float frac = Math.Clamp(b.Hp / b.MaxHp, 0, 1);
        Bar(new Rect2(tx, s.Position.Y + 22, tw, 4), frac, frac > 0.35f ? HudTheme.Good : HudTheme.Danger);

        // Chips: where items come from -> what moves -> where they go (inserters); otherwise the building's status.
        float cy = s.Position.Y + 33;
        if (b is Inserter ins)
        {
            var source = Inserter.LikelySource(w, ins.Origin, ins.Dir, ins.Input);
            var target = w.BuildingAt(ins.Front);
            float x = tx;
            x = FlowChip(x, cy, source?.Def.Name ?? "Nothing", source != null ? Icons.Get(Icons.ForBuilding(source.Def), HudTheme.AccentLight, 28) : null, HudTheme.Accent);
            x = ChipArrow(x, cy);
            var item = ins.Filter;
            x = FlowChip(x, cy, item != null ? ItemName(item) : ins.AutoFilter ? "Auto" : "Any item",
                item != null ? Icons.Item(w.Data.Items[item], 28) : null, HudTheme.ChipBorder);
            x = ChipArrow(x, cy);
            FlowChip(x, cy, target?.Def.Name ?? "Nothing", target != null ? Icons.Get(Icons.ForBuilding(target.Def), HudTheme.WarmLight, 28) : null, HudTheme.Warm);
        }
        else
        {
            string status = b.Status != "" ? b.Status : BuildingDetails(b).FirstOrDefault() ?? "";
            C.DrawString(HudTheme.Body, new Vector2(tx, cy + 15), Ellipsize(HudTheme.Body, status, 12, tw), HorizontalAlignment.Left, -1, 12, HudTheme.TextMuted);
        }

        // Header extras: the close button, and the right block's label.
        foreach (var close in _toolButtons.Where(t => t.Sub == "close"))
        {
            if (close.Rect.HasPoint(mouse)) HudTheme.Button(C, close.Rect, HudTheme.Look.Hover);
            IconAt("close", close.Rect.HasPoint(mouse) ? HudTheme.Text : HudTheme.TextMuted, close.Rect.GetCenter(), 14);
        }
        var side = _sideBlock;
        float hy = _slotHeader.Position.Y + 13;
        if (b is Inserter)
        {
            HudTheme.SectionLabel(C, new Vector2(side.Position.X, hy), "Filter");
            C.DrawString(HudTheme.BodyMedium, new Vector2(side.Position.X, hy), _filterSummary, HorizontalAlignment.Right, side.Size.X, 12, HudTheme.Text);
            foreach (var (label, gx) in _cardGroups)
                C.DrawString(HudTheme.Body, new Vector2(gx, side.Position.Y + 12), label, HorizontalAlignment.Left, -1, 12, HudTheme.TextMuted);
        }
        else
        {
            HudTheme.SectionLabel(C, new Vector2(side.Position.X, hy), "Details");
            var lines = BuildingDetails(b).ToList();
            float colW = side.Size.X / 2 - 12, y = side.Position.Y + 14;
            for (int i = 0; i < lines.Count && i < 12; i++)
            {
                float x = side.Position.X + i / 6 * (colW + 24);
                C.DrawString(HudTheme.Body, new Vector2(x, y + i % 6 * 18), Ellipsize(HudTheme.Body, lines[i], 12, colW), HorizontalAlignment.Left, -1, 12, HudTheme.TextMuted);
            }
        }
    }

    /// <summary>A flow chip: icon and name in a bordered pill. Returns the x after it.</summary>
    private float FlowChip(float x, float y, string text, Texture2D? icon, Color border)
    {
        float textW = TextW(HudTheme.BodyMedium, text, 12);
        var r = new Rect2(x, y, 8 + (icon != null ? 18 : 0) + textW + 10, 22);
        C.DrawRect(r, border with { A = 0.10f });
        C.DrawRect(r, border, false, 1);
        float tx = r.Position.X + 8;
        if (icon != null)
        {
            C.DrawTextureRect(icon, new Rect2(tx, y + 4, 14, 14), false);
            tx += 18;
        }
        TextMid(HudTheme.BodyMedium, tx, r.GetCenter().Y, text, 12, HudTheme.Text);
        return r.End.X;
    }

    private float ChipArrow(float x, float y)
    {
        IconAt("move", HudTheme.TextMuted, new Vector2(x + 12, y + 11), 14);
        return x + 24;
    }

    /// <summary>Selection action: icon above its label.</summary>
    private void DrawAction(Button b, Vector2 mouse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        HudTheme.Button(C, b.Rect, LookOf(b, hover));
        string icon = b.Label switch
        {
            "Rotate" => "turn_right",
            "Take from" => "ping",
            "Repair" => "wrench",
            "Deconstruct" or "Cancel" => "deconstruct",
            "Resume build" => "hammer",
            _ => "beacon",
        };
        var color = !b.Enabled ? HudTheme.Locked : b.Active ? HudTheme.AccentLight : icon == "ping" ? HudTheme.Accent : HudTheme.Icon;
        var c = b.Rect.GetCenter();
        IconAt(icon, color, c - new Vector2(0, 9), 18);
        TextCentered(HudTheme.BodySemi, new Rect2(b.Rect.Position.X, c.Y + 5, b.Rect.Size.X, 16), Ellipsize(HudTheme.BodySemi, b.Label, 12, b.Rect.Size.X - 6), 12,
            !b.Enabled ? HudTheme.Locked : HudTheme.Text);
    }

    /// <summary>Filter mode: a radio button with its label.</summary>
    private void DrawMode(Button b, Vector2 mouse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        HudTheme.Button(C, b.Rect, LookOf(b, hover));
        var dot = new Vector2(b.Rect.Position.X + 16, b.Rect.GetCenter().Y);
        C.DrawArc(dot, 6, 0, Mathf.Tau, 20, b.Active ? HudTheme.Accent : HudTheme.Icon, 1.5f, true);
        if (b.Active)
        {
            C.DrawCircle(dot, 6, HudTheme.Accent with { A = 0.25f });
            C.DrawCircle(dot, 3.5f, HudTheme.Accent);
        }
        TextMid(HudTheme.BodySemi, dot.X + 14, dot.Y, b.Label, 12, b.Active ? HudTheme.AccentLight : HudTheme.Text);
    }

    /// <summary>Item card: count in the corner (red when low), the item's picture, its name; dimmed if the source has none.</summary>
    private void DrawItemCard(Button b, Vector2 mouse)
    {
        var item = b.Key![7..];
        int count = _cardCounts.GetValueOrDefault(item, -1);
        bool inStock = count >= 0;
        bool hover = b.Rect.HasPoint(mouse);
        var r = hover ? b.Rect with { Position = b.Rect.Position - new Vector2(0, 2) } : b.Rect;
        HudTheme.Button(C, r, b.Active ? HudTheme.Look.Selected : hover ? HudTheme.Look.Hover : HudTheme.Look.Normal);
        if (!inStock && !b.Active) C.DrawRect(r, HudTheme.PlateBottom with { A = 0.55f });
        if (b.Active) HudTheme.Brackets(C, r, 10, HudTheme.AccentLight);
        if (inStock)
            C.DrawString(HudTheme.DisplaySemi, new Vector2(r.Position.X, r.Position.Y + 16), Amount(count), HorizontalAlignment.Right, r.Size.X - 8, 12,
                count < LowStock ? HudTheme.DangerLight : HudTheme.TextMuted);
        var c = r.GetCenter();
        var alpha = inStock || b.Active ? 1f : 0.45f;
        if (r.Size.X < CardIconOnlyW)
        {
            // Narrow: just the picture (the name is in the tooltip).
            C.DrawTextureRect(Icons.Item(_world!.Data.Items[item], 32), new Rect2(c - new Vector2(14, 10), new Vector2(28, 28)), false, Colors.White with { A = alpha });
            return;
        }
        C.DrawTextureRect(Icons.Item(_world!.Data.Items[item], 32), new Rect2(c - new Vector2(14, 22), new Vector2(28, 28)), false, Colors.White with { A = alpha });
        var lines = WrapPx(HudTheme.BodySemi, b.Label, 12, r.Size.X - 10).Take(2).ToList();
        float ly = c.Y + 18 - (lines.Count - 1) * 7;
        foreach (var line in lines)
        {
            TextCentered(HudTheme.BodySemi, new Rect2(r.Position.X, ly - 8, r.Size.X, 16), line, 12,
                b.Active ? HudTheme.AccentLight : inStock ? HudTheme.Text : HudTheme.Locked);
            ly += 15;
        }
    }

    private List<Button?> BuildingCommands(Building b)
    {
        var input = _input!;
        var w = _world!;
        var list = new List<Button?>();
        bool rotatable = b.Size == 1 && b.Def.Kind is BuildingKind.Belt or BuildingKind.Inserter or BuildingKind.Drill
            or BuildingKind.Splitter or BuildingKind.TunnelEntrance or BuildingKind.TunnelExit;
        var rotate = new Button(default, "Rotate", $"Facing {b.Dir.ToString().ToLowerInvariant()}", true, false, input.RotateSelected);

        if (b is ConstructionSite site)
        {
            list.Add(new Button(default, "Resume build", $"{site.Fraction:P0} done", true, false, input.RepairSelected));
            if (rotatable) list.Add(rotate);
        }
        else if (b is Inserter ins)
        {
            // Filtering lives in the FILTER panel beside the actions.
            list.Add(rotate);
            list.Add(new Button(default, "Take from", ins.Input.ToString(), true, false, input.CycleInputSide, "input",
                TipFrom($"Take from: {ins.Input}\nWhich side to take from: Auto (behind, then left, then right),\nor fixed Back / Left / Right. Inserters drop where the arrow points.")));
        }
        else if (b is SuperWeapon sw)
        {
            bool ready = sw.IsReady(w);
            list.Add(new Button(default, sw.Def.AbilityName, ready ? "Ready: click a target" : $"Charging {sw.Charge / sw.ChargeNeeded(w):P0}",
                ready, input.AbilityBuildingId == sw.Id, () => input.BeginAbilityTargeting(sw.Id),
                Tip: TipFrom($"{sw.Def.AbilityName}\nRadius {sw.Def.StrikeRadius:0.#}, damage {sw.Def.StrikeDamage * w.Mods.Mult(Stat.SuperWeaponDamage):0}.")));
        }
        else if (rotatable)
        {
            list.Add(rotate);
        }

        if (b is not ConstructionSite)
            list.Add(new Button(default, "Repair", "Uses iron plates", b.Hp < b.MaxHp, false, input.RepairSelected));
        if (b is not Nexus)
            list.Add(new Button(default, b is ConstructionSite ? "Cancel" : "Deconstruct", "Full refund", true, false, input.DeconstructSelected));
        return list;
    }

    /// <summary>Keys to glow for the current tutorial step: the button itself and the category tab that contains it.</summary>
    private HashSet<string> TutorialHighlightKeys()
    {
        var keys = new HashSet<string>();
        if (!TutorialActive || Tutorial!.CurrentButton(_world!) is not { } key) return keys;
        keys.Add(key);
        if (_world!.Data.Buildings.TryGetValue(key, out var def))
        {
            keys.Add($"cat:{def.Category}");
            // A building is selected, so the tabs are hidden: point at Back first.
            if (_input!.SelectedBuilding != null) keys.Add("back");
        }
        return keys;
    }

    private void DrawTutorialGlow()
    {
        var highlight = TutorialHighlightKeys();
        if (highlight.Count == 0) return;
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Time.GetTicksMsec() / 180f);
        foreach (var b in AllButtons)
        {
            if (b.Key == null || !highlight.Contains(b.Key) || b.Active) continue;
            // Category tabs only glow until the right one is open; a blueprint only glows once it's on screen.
            C.DrawStyleBox(Glow(pulse), b.Rect.Grow(2 + 2 * pulse));
        }
    }

    private StyleBoxFlat Glow(float pulse)
    {
        var s = HudTheme.Box(new Color(0, 0, 0, 0), HudTheme.HintGlow with { A = 0.5f + 0.5f * pulse }, HudTheme.ChipRadius + 2, 3);
        return s;
    }

    // ================================================================== controls panel

    private const float ControlsRowH = 30;

    private void LayoutControls()
    {
        int perCol = (Bindings.Rows.Length + 1) / 2;
        float h = 56 + perCol * ControlsRowH + 14;
        _controlsRect = new Rect2(_toolbarRect.End.X - SidePad - ControlsW, _toolbarRect.Position.Y - Gap - h, ControlsW, h);
        _panels.Add(_controlsRect);
        _overlayButtons.Add(new Button(new Rect2(_controlsRect.End.X - 12 - 36, _controlsRect.Position.Y + 10, 36, 36), "close", "", true, false,
            () => _controlsOpen = false, Tip: new Tip("Close", Array.Empty<string>(), TipAnchor.Above)));
    }

    private void DrawControls()
    {
        var r = _controlsRect;
        var mouse = GetLocalMousePosition();
        HudTheme.Popover(C, r);
        HudTheme.SectionLabel(C, new Vector2(r.Position.X + 16, r.Position.Y + 34), "Controls");
        foreach (var b in _overlayButtons.Where(b => b.Label == "close"))
        {
            HudTheme.Button(C, b.Rect, b.Rect.HasPoint(mouse) ? HudTheme.Look.Hover : HudTheme.Look.Normal);
            IconAt("close", HudTheme.Text, b.Rect.GetCenter(), 16);
        }

        int perCol = (Bindings.Rows.Length + 1) / 2;
        float colW = (r.Size.X - 32 - 16) / 2;
        for (int i = 0; i < Bindings.Rows.Length; i++)
        {
            var row = Bindings.Rows[i];
            float x = r.Position.X + 16 + i / perCol * (colW + 16);
            float cy = r.Position.Y + 56 + i % perCol * ControlsRowH + ControlsRowH / 2;
            TextMid(HudTheme.Body, x, cy, row.Label, 13, HudTheme.Text);
            string keys = row.Keys();
            float chipW = TextW(HudTheme.DisplaySemi, keys, 13) + 16;
            var chip = new Rect2(x + colW - chipW, cy - 12, chipW, 24);
            HudTheme.Chip(C, chip);
            TextCentered(HudTheme.DisplaySemi, chip, keys, 13, HudTheme.Text);
        }
    }

    // ================================================================== research panel

    private void LayoutResearch()
    {
        var size = Screen;
        _researchRect = new Rect2((size.X - 940) / 2, BarH + 20, 940, 540);
        _panels.Add(_researchRect);
        var w = _world!;
        var input = _input!;
        _researchButtons.Add(new Button(new Rect2(_researchRect.End.X - 110, _researchRect.Position.Y + 12, 96, 36), "Close", "", true, false,
            () => input.ResearchOpen = false));
        if (w.Research.Current != null)
            _researchButtons.Add(new Button(new Rect2(_researchRect.End.X - 214, _researchRect.Position.Y + 12, 96, 36), "Cancel", "", true, false,
                input.CancelResearch, Tip: TipFrom("Cancel\nStop and refund the current research.")));

        const float colW = 222, btnH = 60;
        for (int tier = 1; tier <= 4; tier++)
        {
            var techs = w.Data.TechList.Where(t => t.Tier == tier).ToList();
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                var rect = new Rect2(_researchRect.Position.X + 16 + (tier - 1) * (colW + 8), _researchRect.Position.Y + 110 + i * (btnH + 6), colW, btnH);
                bool done = w.Research.IsDone(t.Id);
                bool current = w.Research.Current == t;
                string? why = done || current ? null : w.Research.CheckStart(w, t);
                string sub = done ? "Done" : current ? $"Researching {w.Research.Fraction:P0}" : CostText(t.Cost);
                _researchButtons.Add(new Button(rect, t.Name, sub, !done && !current && why == null, current,
                    () => input.StartResearch(t.Id), $"tech:{t.Id}",
                    TipFrom($"{t.Name}\n{t.Description}\nCost: {CostText(t.Cost, full: true)}\nTime: {t.Ticks / 60f:0}s with one lab" +
                    (t.Requires.Count > 0 ? "\nRequires: " + string.Join(", ", t.Requires.Select(r => w.Data.Techs[r].Name)) : "") +
                    (why != null ? $"\n{why}" : "") +
                    "\nUnlocks: " + string.Join(", ", w.Data.BuildingList.Where(b => b.Tech == t.Id).Select(b => b.Name)))));
            }
        }
    }

    private void DrawResearch()
    {
        var r = _researchRect;
        var w = _world!;
        HudTheme.Popover(C, r);
        HudTheme.SectionLabel(C, new Vector2(r.Position.X + 16, r.Position.Y + 34), "Research");
        int labs = w.Buildings.Count(b => b is Lab);
        string status = w.Research.Current is { } cur
            ? $"Researching {cur.Name}: {w.Research.Fraction:P0}   ({labs} lab{(labs == 1 ? "" : "s")})"
            : labs == 0 ? "Build a Laboratory (Production) to research. Costs are paid when research starts."
            : $"Pick a technology. {labs} lab{(labs == 1 ? "" : "s")} working.";
        C.DrawString(HudTheme.Body, r.Position + new Vector2(16, 62), status, HorizontalAlignment.Left, r.Size.X - 250, 14, labs == 0 ? HudTheme.Danger : HudTheme.Text);
        if (w.Research.Current != null) Bar(new Rect2(r.Position + new Vector2(16, 72), new Vector2(r.Size.X - 250, 6)), w.Research.Fraction, HudTheme.Research);
        for (int tier = 1; tier <= 4; tier++)
            C.DrawString(HudTheme.BodySemi, r.Position + new Vector2(16 + (tier - 1) * 230, 100), $"TIER {tier} - {TierNames[tier].ToUpperInvariant()}",
                HorizontalAlignment.Left, -1, 12, HudTheme.TextMuted);

        var mouse = GetLocalMousePosition();
        foreach (var b in _researchButtons)
        {
            DrawCardButton(b, mouse);
            if (b.Sub == "Done") Box(b.Rect, HudTheme.Good with { A = 0.12f }, HudTheme.Good with { A = 0.5f });
        }
    }

    // ================================================================== super weapons

    private void LayoutSuperWeapons()
    {
        var w = _world!;
        var input = _input!;
        var sws = w.Buildings.OfType<SuperWeapon>().ToList();
        float y = _toolbarRect.Position.Y - Gap - 44;
        foreach (var sw in sws)
        {
            bool ready = sw.IsReady(w);
            var rect = new Rect2(BarPad, y, 240, 44);
            _swButtons.Add(new Button(rect, sw.Def.AbilityName, ready ? "Ready" : $"Charging {sw.Charge / sw.ChargeNeeded(w):P0}", ready,
                input.AbilityBuildingId == sw.Id, () => input.BeginAbilityTargeting(sw.Id), Tip: TipFrom($"{sw.Def.AbilityName}\nClick, then click a target on the map.")));
            _panels.Add(rect);
            y -= 44 + Gap;
        }
    }

    private void DrawSuperWeaponStrip()
    {
        var mouse = GetLocalMousePosition();
        foreach (var b in _swButtons)
        {
            DrawCardButton(b, mouse);
            if (b.Enabled && !b.Active) Box(b.Rect, new Color(0, 0, 0, 0), HudTheme.Warn);
        }
    }

    // ================================================================== minimap (drop-down under the map button)

    private const float MinimapW = 302, MinimapMapW = 280, MinimapMapH = 162, MinimapHeaderH = 36;

    private void LayoutMinimapPanel()
    {
        _minimapPanel = new Rect2(BarPad, BarH + 12, MinimapW, MinimapHeaderH + MinimapMapH + 11);
        _minimapRect = new Rect2(_minimapPanel.Position.X + 11, _minimapPanel.Position.Y + MinimapHeaderH, MinimapMapW, MinimapMapH);
        _panels.Add(_minimapPanel);
        const float b = 26;
        float bx = _minimapPanel.End.X - 8 - b, by = _minimapPanel.Position.Y + 5;
        _overlayButtons.Add(new Button(new Rect2(bx, by, b, b), "collapse", "mini", true, false, ToggleMap,
            Tip: new Tip("Hide map", Array.Empty<string>(), TipAnchor.BelowRight)));
        _overlayButtons.Add(new Button(new Rect2(bx - 4 - b, by, b, b), "turn_right", "mini", true, false, () => Camera?.Turn(1),
            Tip: new Tip("Turn view right", new[] { "A quarter turn around the map." }, TipAnchor.BelowRight)));
        _overlayButtons.Add(new Button(new Rect2(bx - 2 * (4 + b), by, b, b), "turn_left", "mini", true, false, () => Camera?.Turn(-1),
            Tip: new Tip("Turn view left", new[] { "A quarter turn around the map." }, TipAnchor.BelowRight)));
    }

    /// <summary>The minimap is the world's on-screen projection at the current view angle, scaled to fit the map area.</summary>
    private (Vector2 Offset, float Scale) MinimapFit()
    {
        var b = Iso.Bounds(_world!.Map.Width, _world.Map.Height);
        float s = Math.Min(_minimapRect.Size.X / b.Size.X, _minimapRect.Size.Y / b.Size.Y);
        return (_minimapRect.Position + (_minimapRect.Size - b.Size * s) / 2 - b.Position * s, s);
    }

    /// <summary>A world screen-space point (what the camera sees) on the minimap.</summary>
    private Vector2 MinimapPoint(Vector2 worldScreen)
    {
        var (offset, s) = MinimapFit();
        return offset + worldScreen * s;
    }

    private Vector2 MinimapPoint(float gx, float gy) => MinimapPoint(Iso.ToScreen(gx, gy));

    private void CenterCameraOnMinimap(Vector2 m)
    {
        if (Camera == null) return;
        var (offset, s) = MinimapFit();
        Camera.CenterOn((m - offset) / s);
    }

    private void DrawMinimapPanel()
    {
        var map = _world!.Map;
        if (_minimapTex == null || _minimapAge > 10)
        {
            _minimapAge = 0;
            var img = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var t = new Int2(x, y);
                var c = map.TerrainAt(t) switch
                {
                    Terrain.Blocked => new Color(0, 0, 0, 0),
                    Terrain.NoBuild => new Color(0.45f, 0.18f, 0.24f),
                    _ => new Color(0.26f, 0.25f, 0.22f),
                };
                if (map.OreAt(t) is { } ore) c = Iso.ParseColor(_world.Data.Items[ore].Color).Darkened(0.1f);
                img.SetPixel(x, y, c);
            }
            _minimapTex = ImageTexture.CreateFromImage(img);
        }

        var panel = _minimapPanel;
        HudTheme.Popover(C, panel);
        HudTheme.SectionLabel(C, new Vector2(panel.Position.X + 12, panel.Position.Y + 23), _world.MapDef.Name);
        var mouse = GetLocalMousePosition();
        foreach (var b in _overlayButtons.Where(b => b.Sub == "mini"))
        {
            HudTheme.Button(C, b.Rect, b.Rect.HasPoint(mouse) ? HudTheme.Look.Hover : HudTheme.Look.Normal);
            IconAt(b.Label == "collapse" ? "chevron_up" : b.Label, HudTheme.Icon, b.Rect.GetCenter(), 14);
        }

        // Map well with an inset shadow.
        var mr = _minimapRect;
        C.DrawRect(mr, new Color("#050A14"));
        var origin = MinimapPoint(0, 0);
        C.DrawSetTransformMatrix(new Transform2D(MinimapPoint(1, 0) - origin, MinimapPoint(0, 1) - origin, origin));
        C.DrawTexture(_minimapTex, Vector2.Zero);
        foreach (var b in _world.Buildings)
            C.DrawRect(new Rect2(b.Origin.X, b.Origin.Y, b.Size, b.Size), b is Nexus ? HudTheme.Warm : b is ConstructionSite ? HudTheme.Accent with { A = 0.4f } : new Color(0.85f, 0.85f, 0.9f));
        foreach (var e in _world.Enemies)
            C.DrawRect(new Rect2(e.Pos.X - 0.7f, e.Pos.Y - 0.7f, 1.4f, 1.4f), HudTheme.Danger);
        C.DrawSetTransformMatrix(Transform2D.Identity);
        foreach (var eng in _world.Engineers.Where(e => e.Alive))
            C.DrawCircle(MinimapPoint(eng.Pos.X, eng.Pos.Y), 3.5f, WorldView.PlayerColor(eng.PlayerId));
        RadarSweep(mr);
        for (int i = 0; i < 6; i++) C.DrawRect(mr.Grow(-i), new Color(0, 0, 0, 0.35f * (1 - i / 6f)), false, 1);
        C.DrawRect(mr, HudTheme.ChipBorder, false, 1);

        // Camera view outline.
        if (Camera != null)
        {
            var vp = GetViewportRect().Size / Camera.Zoom;
            var c = Camera.GetScreenCenterPosition();
            var corners = new[] { c - vp / 2, c + new Vector2(vp.X, -vp.Y) / 2, c + vp / 2, c + new Vector2(-vp.X, vp.Y) / 2 }
                .Select(p => ClampTo(mr, MinimapPoint(p))).ToArray();
            C.DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, HudTheme.AccentLight with { A = 0.8f }, 1f);
        }
    }

    private static Vector2 ClampTo(Rect2 r, Vector2 p) => new(Math.Clamp(p.X, r.Position.X, r.End.X), Math.Clamp(p.Y, r.Position.Y, r.End.Y));

    /// <summary>Radar sweep: a cyan wedge (22% fading to 0 over 70°) turning once every 5 s, clipped to the map.</summary>
    private void RadarSweep(Rect2 r)
    {
        const int steps = 14;
        const float spread = 70 * Mathf.Pi / 180;
        float lead = (float)(Time.GetTicksMsec() / 1000.0 % 5 / 5 * Mathf.Tau);
        var c = r.GetCenter();
        float Reach(float a)
        {
            var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            float tx = d.X != 0 ? (d.X > 0 ? r.End.X - c.X : r.Position.X - c.X) / d.X : float.MaxValue;
            float ty = d.Y != 0 ? (d.Y > 0 ? r.End.Y - c.Y : r.Position.Y - c.Y) / d.Y : float.MaxValue;
            return Math.Min(tx, ty);
        }
        for (int i = 0; i < steps; i++)
        {
            float a0 = lead - spread * i / steps, a1 = lead - spread * (i + 1) / steps;
            var p0 = c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * Reach(a0);
            var p1 = c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * Reach(a1);
            var k0 = HudTheme.Accent with { A = 0.22f * (1 - (float)i / steps) };
            var k1 = HudTheme.Accent with { A = 0.22f * (1 - (float)(i + 1) / steps) };
            C.DrawPolygon(new[] { c, p0, p1 }, new[] { k0 with { A = k0.A * 0.4f }, k0, k1 });
        }
    }

    // ================================================================== incoming-enemy marker

    /// <summary>Spawn areas (centres of clusters of spawn tiles), worked out once per map.</summary>
    private List<System.Numerics.Vector2> SpawnCentres()
    {
        if (_spawnCentres != null) return _spawnCentres;
        var clusters = new List<List<Int2>>();
        foreach (var t in _world!.Map.SpawnTiles)
        {
            var near = clusters.FirstOrDefault(c => c.Any(o => Math.Abs(o.X - t.X) + Math.Abs(o.Y - t.Y) <= 6));
            if (near != null) near.Add(t);
            else clusters.Add(new List<Int2> { t });
        }
        return _spawnCentres = clusters.Select(c => new System.Numerics.Vector2(
            (float)c.Average(t => t.X) + 0.5f, (float)c.Average(t => t.Y) + 0.5f)).ToList();
    }

    /// <summary>
    /// With the map closed and a wave 10 s or less away: a pulsing red chip on the screen edge nearest each spawn,
    /// with an arrow toward it and the wave's enemy count.
    /// </summary>
    private void DrawIncoming()
    {
        var w = _world!;
        var waves = w.Waves;
        if (Settings.ShowMinimap || Camera == null || waves.Held || !waves.HasMoreWaves || waves.TicksUntilNextWave > 10 * World.TicksPerSecond) return;
        int count = waves.PreviewNext(w).Sum(p => p.Count);
        var area = new Rect2(0, BarH, Screen.X, Screen.Y - BarH - ToolbarH).Grow(-48);
        var centre = area.GetCenter();
        float k = Scale.X;
        var vp = GetViewportRect().Size;
        float pulse = 0.725f + 0.275f * MathF.Cos((float)(Time.GetTicksMsec() / 1000.0) * Mathf.Tau);
        foreach (var spawn in SpawnCentres())
        {
            var hud = ((Iso.ToScreen(spawn) - Camera.GetScreenCenterPosition()) * Camera.Zoom + vp / 2) / k;
            var dir = (hud - centre).Normalized();
            var pos = hud;
            if (!area.HasPoint(hud))
            {
                // Walk from the centre toward the spawn until the edge of the play area.
                float tx = dir.X != 0 ? ((dir.X > 0 ? area.End.X : area.Position.X) - centre.X) / dir.X : float.MaxValue;
                float ty = dir.Y != 0 ? ((dir.Y > 0 ? area.End.Y : area.Position.Y) - centre.Y) / dir.Y : float.MaxValue;
                pos = centre + dir * Math.Min(tx, ty);
            }
            var chip = new Rect2(pos - new Vector2(30, 15), new Vector2(60, 30));
            C.DrawRect(chip.Grow(4), HudTheme.Danger with { A = 0.12f * pulse });
            C.DrawRect(chip, new Color(0.16f, 0.05f, 0.05f, 0.9f * pulse));
            C.DrawRect(chip, HudTheme.Danger with { A = pulse }, false, 1);
            var tip = chip.GetCenter() + dir * 26;
            var side = new Vector2(-dir.Y, dir.X) * 6;
            C.DrawColoredPolygon(new[] { tip, tip - dir * 9 + side, tip - dir * 9 - side }, HudTheme.Danger with { A = pulse });
            TextCentered(HudTheme.DisplaySemi, chip, count.ToString(), 15, HudTheme.DangerLight with { A = pulse });
        }
    }

    // ================================================================== tooltips

    /// <summary>Works out this frame's tooltip (button, chip, storage row or world) and where it goes.</summary>
    private void ComputeTooltip()
    {
        _tip = null;
        var m = GetLocalMousePosition();
        Rect2 anchor = default;
        var hovered = AllButtons.FirstOrDefault(b => b.Rect.HasPoint(m) && b.Tip != null);
        Tip? tip = hovered?.Tip;
        if (hovered != null) anchor = hovered.Rect;
        if (tip == null && _storageOpen && _resourceRows.FirstOrDefault(r => r.Rect.HasPoint(m)) is { Status: { } status })
        {
            string body = ResourceTooltip(status, out string title);
            tip = new Tip(title, body.Split('\n').SelectMany(l => Wrap(l, 66)).ToList());
        }
        if (tip == null && _chipTips.FirstOrDefault(c => c.Rect.HasPoint(m)) is { Tip: { } chipTip } chip)
        {
            tip = chipTip;
            anchor = chip.Rect;
        }
        if (tip == null && AllButtons.Any(b => b.Rect.HasPoint(m))) return;
        if (tip == null && !IsMouseOverHud() && HoveredIssue?.Invoke() is var (_, issue))
        {
            var (_, color, title) = View.IssueLook.Of(issue);
            var (what, fix) = View.IssueLook.Explain(issue);
            tip = new Tip(title, fix == "" ? new[] { what } : new[] { what, fix }, TipAnchor.Mouse, 320, color);
        }
        if (tip == null && !IsMouseOverHud() && WorldTooltip() is { } world) tip = TipFrom(world);
        if (tip == null) return;

        float wrapW = tip.Width > 0 ? tip.Width - 24 : 0;
        var body2 = wrapW > 0 ? tip.Body.SelectMany(l => WrapPx(HudTheme.Body, l, 13, wrapW)).ToList() : tip.Body.ToList();
        float width = tip.Width > 0 ? tip.Width
            : Math.Max(TextW(HudTheme.DisplaySemi, tip.Title, 16), body2.Select(l => TextW(HudTheme.Body, l, 13)).DefaultIfEmpty(0).Max()) + 24;
        float height = 14 + 20 + body2.Count * 18 + (body2.Count > 0 ? 6 : 0) + 6 + (tip.TitleColor != null ? 4 : 0);
        var size = Screen;
        var pos = tip.Anchor switch
        {
            TipAnchor.BelowLeft => new Vector2(anchor.Position.X, anchor.End.Y + 8),
            TipAnchor.BelowRight => new Vector2(anchor.End.X - width, anchor.End.Y + 8),
            TipAnchor.Above => new Vector2(anchor.GetCenter().X - width / 2, anchor.Position.Y - 8 - height),
            _ => m + new Vector2(18, 24),
        };
        if (tip.Anchor == TipAnchor.Mouse && pos.Y + height > size.Y) pos.Y = m.Y - height - 8;
        pos.X = Math.Clamp(pos.X, 4, Math.Max(4, size.X - width - 4));
        pos.Y = Math.Clamp(pos.Y, 4, Math.Max(4, size.Y - height - 4));
        _tip = (tip, new Rect2(pos, new Vector2(width, height)), body2);
    }

    private void DrawTooltip()
    {
        if (_tip is not { } shown) return;
        var (tip, rect, body) = shown;
        HudTheme.Popover(C, rect);
        TextBaseline(HudTheme.DisplaySemi, rect.Position.X + 12, rect.Position.Y + 28, tip.Title, 16, tip.TitleColor ?? HudTheme.Text);
        float y = rect.Position.Y + 28 + 22;
        // A coloured title marks a problem pop-up: what's wrong in body text, the fix muted below it.
        int bright = tip.TitleColor != null ? WrapPx(HudTheme.Body, tip.Body[0], 13, rect.Size.X - 24).Count() : 0;
        for (int i = 0; i < body.Count; i++)
        {
            if (i == bright && bright > 0) y += 4;
            TextBaseline(HudTheme.Body, rect.Position.X + 12, y, body[i], 13, i < bright ? HudTheme.Text : HudTheme.TextMuted);
            y += 18;
        }
    }

    /// <summary>Old-style "Title\nline\nline" text as a mouse-anchored tip.</summary>
    private static Tip TipFrom(string text)
    {
        var lines = text.Split('\n');
        return new Tip(lines[0], lines.Skip(1).ToList());
    }

    private string? WorldTooltip()
    {
        var w = _world!;
        var t = _input!.HoverTile;
        if (!w.Map.InBounds(t)) return null;
        if (w.BuildingAt(t) is { } b)
        {
            string text = b.Status == "" ? b.Def.Name : $"{b.Def.Name}\n{b.Status}";
            if (b is Crafter or Drill) text += "\n" + string.Join("\n", BuildingDetails(b).Where(l => !l.StartsWith("Grid")));
            return text;
        }
        if (w.Map.OreAt(t) is { } ore)
            return $"{w.Data.Items[ore].Name} ({w.Map.OreAmount(t)})" + (w.Map.IsClaimZone(t) ? "\nClaim zone: faster mining, double yield" : "");
        return null;
    }

    /// <summary>Tooltip text for a disconnected logistics building: the problem and where its chain ends.</summary>
    private static string DescribeIssue(Building b, LogisticsIssue issue)
    {
        var lines = new List<string> { $"PROBLEM: {issue.Short}. Items from here never reach the Nexus or a building that uses them." };
        lines.AddRange(Wrap(issue.Reason, 60));
        lines.Add(issue.End == b
            ? "This is where the chain ends (red marker)."
            : $"The chain ends at the {issue.End.Def.Name} at ({issue.End.Origin.X}, {issue.End.Origin.Y}), shown with a red marker.");
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line != "")
            {
                yield return line;
                line = "";
            }
            line = line == "" ? word : line + " " + word;
        }
        if (line != "") yield return line;
    }

    private static IEnumerable<string> WrapPx(Font font, string text, int size, float width)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string next = line == "" ? word : line + " " + word;
            if (line != "" && TextW(font, next, size) > width)
            {
                yield return line;
                next = word;
            }
            line = next;
        }
        if (line != "") yield return line;
    }

    // ================================================================== messages, ping wheel, end screens

    private void DrawMessages(Vector2 size)
    {
        float y = size.Y * 0.3f;
        foreach (var (text, _) in _messages)
        {
            C.DrawString(HudTheme.BodySemi, new Vector2(1, y + 1), text, HorizontalAlignment.Center, size.X, 20, Colors.Black);
            C.DrawString(HudTheme.BodySemi, new Vector2(0, y), text, HorizontalAlignment.Center, size.X, 20, HudTheme.Text);
            y += 28;
        }
    }

    private void DrawPingWheel()
    {
        var c = _input!.PingWheelScreen;
        int choice = _input.PingWheelChoice();
        C.DrawCircle(c, 110, new Color(0, 0, 0, 0.55f));
        C.DrawArc(c, 110, 0, Mathf.Tau, 48, HudTheme.TooltipBorder, 2, true);
        for (int i = 1; i < PlayerInput.PingOptions.Length; i++)
        {
            float a = (i - 1 + 0.5f) * Mathf.Tau / 7;
            var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * 75;
            bool sel = i == choice;
            if (sel) C.DrawCircle(p, 30, HudTheme.Accent with { A = 0.3f });
            C.DrawString(HudTheme.BodySemi, p + new Vector2(-50, 5), PlayerInput.PingOptions[i].Label, HorizontalAlignment.Center, 100, sel ? 14 : 13,
                sel ? HudTheme.AccentText : HudTheme.Text);
        }
        C.DrawCircle(c, 24, choice == 0 ? HudTheme.Accent with { A = 0.45f } : HudTheme.PanelBg);
        C.DrawString(HudTheme.BodySemi, c + new Vector2(-40, 5), "Here", HorizontalAlignment.Center, 80, 13, HudTheme.Text);
    }

    private void DrawDesync(Vector2 size)
    {
        if (_peer is not LockstepClient { DesyncTick: long tick }) return;
        var r = new Rect2(size.X / 2 - 300, BarH + 140, 600, 34);
        Box(r, HudTheme.DangerBg, HudTheme.DangerBorder);
        TextCentered(HudTheme.BodySemi, r, $"Desync detected (tick {tick}). Ask the host to restart.", 14, HudTheme.DangerText);
    }

    private void DrawOutcome(Vector2 size)
    {
        var w = _world!;
        if (w.Outcome == Outcome.Playing) return;
        C.DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.55f));
        bool won = w.Outcome == Outcome.Won;
        string title = won ? $"{w.MapDef.Name.ToUpperInvariant()} HOLDS" : w.Surrendered ? "YOU SURRENDERED" : "THE NEXUS HAS FALLEN";
        C.DrawString(HudTheme.Display, new Vector2(0, size.Y / 2 - 30), title, HorizontalAlignment.Center, size.X, 52, won ? HudTheme.Good : HudTheme.Danger);
        C.DrawString(HudTheme.Body, new Vector2(0, size.Y / 2 + 10), $"Waves reached: {w.Waves.WavesStarted} / {w.Waves.TotalWaves}", HorizontalAlignment.Center, size.X, 18, HudTheme.Text);
        if (won) C.DrawString(HudTheme.BodySemi, new Vector2(0, size.Y / 2 + 40), $"+{MetaEarned} meta points for the Skill Tree", HorizontalAlignment.Center, size.X, 20, HudTheme.HintGlow);
        string next = IsCoop && Me != 0 ? "Waiting for the host to restart, or Esc for the menu" : "Press Enter to play again, or Esc for the menu";
        C.DrawString(HudTheme.Body, new Vector2(0, size.Y / 2 + 74), next, HorizontalAlignment.Center, size.X, 18, HudTheme.Text);
    }

    // ================================================================== tutorial

    private void LayoutTutorial()
    {
        if (!TutorialActive) return;
        var tut = Tutorial!;
        var size = Screen;
        float bodyH = HudTheme.Body.GetMultilineStringSize(tut.Current.Body, HorizontalAlignment.Left, TutorialW - 32, TutorialBodySize).Y;
        _tutorialRect = new Rect2((size.X - TutorialW) / 2, BarH + 12, TutorialW, 74 + bodyH + 52);
        _panels.Add(_tutorialRect);

        float by = _tutorialRect.End.Y - 46;
        float right = _tutorialRect.End.X - 16;
        if (tut.NeedsNext)
        {
            _tutorialButtons.Add(new Button(new Rect2(right - 130, by, 130, 36), tut.IsLastStep ? "Finish" : "Next", "", true, true,
                () => TutorialNextRequested?.Invoke(), "tut"));
            right -= 140;
        }
        _tutorialButtons.Add(new Button(new Rect2(right - 130, by, 130, 36), "Skip tutorial", "", true, false,
            () => TutorialSkipRequested?.Invoke(), "tut"));
    }

    private void DrawTutorial()
    {
        var tut = Tutorial!;
        var r = _tutorialRect;
        HudTheme.Popover(C, r);
        Box(r, new Color(0, 0, 0, 0), HudTheme.HintGlow with { A = 0.6f }, HudTheme.TooltipRadius);

        TextBaseline(HudTheme.BodySemi, r.Position.X + 16, r.Position.Y + 24, $"TUTORIAL · STEP {tut.StepIndex + 1} OF {tut.StepCount}", 12, HudTheme.HintGlow);
        TextBaseline(HudTheme.Display, r.Position.X + 16, r.Position.Y + 52, tut.Current.Title, 22, HudTheme.Text);
        C.DrawMultilineString(HudTheme.Body, r.Position + new Vector2(16, 78), tut.Current.Body, HorizontalAlignment.Left, TutorialW - 32, TutorialBodySize, -1, HudTheme.Text);

        if (!tut.NeedsNext)
            TextBaseline(HudTheme.Body, r.Position.X + 16, r.End.Y - 22, "Complete the step to continue", 13, HudTheme.TextMuted);

        var mouse = GetLocalMousePosition();
        foreach (var b in _tutorialButtons)
        {
            bool hover = b.Rect.HasPoint(mouse);
            HudTheme.Button(C, b.Rect, b.Active ? HudTheme.Look.Selected : hover ? HudTheme.Look.Hover : HudTheme.Look.Normal, warm: b.Active);
            TextCentered(HudTheme.BodySemi, b.Rect, b.Label, 14, b.Active ? HudTheme.WarmLighter : HudTheme.Text);
        }
    }

    // ================================================================== building details

    private IEnumerable<string> BuildingDetails(Building b)
    {
        var w = _world!;
        switch (b)
        {
            case Crafter c:
                yield return (c.Def.UsesFuel ? $"Fuel {c.Coal}   " : "") + $"In: {Describe(c.Input)}   Out: {Describe(c.Output)}";
                foreach (var line in ProductionLines(c)) yield return line;
                break;
            case Drill d:
                yield return $"Mining {RateText(d.Mined.PerMinute(d.Ore ?? "", w.Tick))}/min (max {RateText(d.MaxPerMinute(w))}/min)"
                             + (d.NeedsFuel ? $"   Fuel: {d.Coal} coal" : "");
                if (d.OreStart > 0)
                    yield return d.Depleted
                        ? "Ore left: none (tile is empty)"
                        : $"Ore left: {d.OreLeft} / {d.OreStart} ({d.OreFraction:P0})";
                break;
            case Generator g:
                yield return $"Fuel: {g.Coal} coal";
                break;
            case Depot d:
                yield return Describe(d.Storage);
                break;
            case TunnelEntrance t:
                yield return t.FindExit(w) != null ? "Linked to a Tunnel Out" : $"No Tunnel Out facing {t.Dir} within {t.Def.TunnelReach} tiles";
                break;
            case Turret tu:
                yield return $"Range {tu.Range(w):0.#}" + (tu.UsesAmmo ? $", uses {w.Data.Items[tu.Def.AmmoItem!].Name}" : ", runs on power");
                break;
            case Nexus:
                yield return "Main storage and a little power. Defend it!";
                break;
            case Inserter ins:
                var from = Inserter.LikelySource(w, ins.Origin, ins.Dir, ins.Input);
                var to = w.BuildingAt(ins.Front);
                string side = ins.Input == InputSide.Auto ? "auto" : ins.Input.ToString().ToLowerInvariant();
                yield return $"Takes from: {from?.Def.Name ?? "nothing"} ({side})   Gives to: {to?.Def.Name ?? "nothing"}";
                yield return $"Filter: {ItemName(ins.Filter)}{(ins.AutoFilter ? " (automatic)" : "")}";
                break;
            case Lab:
                yield return w.Research.Current is { } cur ? $"Researching {cur.Name}" : "Open Research to choose a technology";
                break;
        }
        if (b.NeedsPower || b is PowerPole or Generator or Nexus)
        {
            var grid = w.Power.GridOf(b);
            yield return grid == null ? "Not connected to power (place a pole nearby)"
                : $"Grid: {grid.Demand:0} / {grid.Capacity:0} kW ({grid.Satisfaction:P0})";
        }
    }

    private string DescribeOrder(Engineer eng)
    {
        if (eng.Orders.Count == 0) return "Idle";
        var w = _world!;
        return eng.Orders[0] switch
        {
            MoveOrder => "Moving",
            BuildOrder b when w.BuildingById(b.SiteId) is ConstructionSite s =>
                eng.WorkingOn == s.Id ? $"Building {s.Def.Name} ({s.Fraction:P0})" : $"Walking to build {s.Def.Name}",
            RepairOrder r when w.BuildingById(r.BuildingId) is { } t =>
                eng.WorkingOn == t.Id ? $"Repairing {t.Def.Name}" : $"Walking to repair {t.Def.Name}",
            _ => "Working",
        };
    }

    private string Describe(Inventory inv)
    {
        var parts = inv.Entries.Select(e => $"{e.Value} {ShortName(e.Key)}").ToList();
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    /// <summary>
    /// For furnaces and workshops: which drills feed it, measured in/out per minute, and whether supply or
    /// smelting speed is the bottleneck.
    /// </summary>
    private IEnumerable<string> ProductionLines(Crafter c)
    {
        var w = _world!;
        long now = w.Tick;
        var drills = SupplyTrace.DrillsFeeding(w, c);
        if (drills.Count == 0)
        {
            yield return "Fed by no drills";
        }
        else
        {
            var byOre = drills.GroupBy(d => w.Map.OreAt(d.Origin) ?? "nothing")
                .Select(g => $"{g.Count()} {(g.Key == "nothing" ? "empty" : ShortName(g.Key))}");
            yield return $"Fed by {drills.Count} drill{(drills.Count == 1 ? "" : "s")}: {string.Join(", ", byOre)}";
        }

        var inRates = c.Received.All(now).ToList();
        var outRates = c.Produced.All(now).ToList();
        yield return "In: " + (inRates.Count == 0 ? "nothing yet" : string.Join(", ", inRates.Select(r => $"{RateText(r.PerMinute)} {ShortName(r.Item)}/min")))
                   + "   Out: " + (outRates.Count == 0 ? "nothing yet" : string.Join(", ", outRates.Select(r => $"{RateText(r.PerMinute)} {ShortName(r.Item)}/min")));

        // Bottleneck: compare what the feeding drills can mine of an input with how fast this machine can use it.
        var oreDrills = drills.Where(d => w.Map.OreAt(d.Origin) is { } ore && ore != "coal").ToList();
        var recipe = c.Active ?? c.Recipes.FirstOrDefault(r => oreDrills.Any(d => r.Inputs.ContainsKey(w.Map.OreAt(d.Origin)!)));
        if (recipe != null)
        {
            string input = recipe.Inputs.Keys.First();
            float supply = oreDrills.Where(d => w.Map.OreAt(d.Origin) == input).Sum(d => d.MaxPerMinute(w)) / recipe.Inputs[input];
            float capacity = c.MaxPerMinute(w, recipe);
            string verdict = supply > capacity * 1.05f ? " - drills outpace it: add another furnace"
                : supply < capacity * 0.95f ? " - add drills to keep it busy" : " - balanced";
            yield return $"Can make {RateText(capacity)}/min; drills supply enough for {RateText(supply)}/min{verdict}";
        }
    }

    // ================================================================== drawing helpers

    /// <summary>Frame look for a HUD button: disabled, selected (active), hover, or normal.</summary>
    private static HudTheme.Look LookOf(Button b, bool hover) =>
        !b.Enabled ? HudTheme.Look.Disabled : b.Active ? HudTheme.Look.Selected : hover ? HudTheme.Look.Hover : HudTheme.Look.Normal;

    private void Box(Rect2 r, Color bg, Color border, int radius = HudTheme.ChipRadius)
    {
        var key = (bg, border, radius);
        if (!_boxes.TryGetValue(key, out var sb)) _boxes[key] = sb = HudTheme.Box(bg, border, radius);
        C.DrawStyleBox(sb, r);
    }

    private void Chip(Rect2 r, Vector2 mouse) => HudTheme.Chip(C, r, r.HasPoint(mouse));

    private void Bar(Rect2 r, float frac, Color fill)
    {
        int radius = (int)(r.Size.Y / 2);
        Box(r, HudTheme.ButtonBg, HudTheme.ChipBorder, radius);
        if (frac > 0) Box(new Rect2(r.Position, new Vector2(Math.Max(r.Size.Y, r.Size.X * frac), r.Size.Y)), fill, fill, radius);
    }

    private void IconAt(string id, Color color, Vector2 center, float px) =>
        C.DrawTextureRect(Icons.Get(id, color, (int)px), new Rect2(center - new Vector2(px / 2, px / 2), new Vector2(px, px)), false);

    private static float TextW(Font f, string s, int size) => f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    private static float Baseline(Font f, float centerY, int size) => centerY + (f.GetAscent(size) - f.GetDescent(size)) / 2;

    private void TextBaseline(Font f, float x, float y, string s, int size, Color c) =>
        C.DrawString(f, new Vector2(x, y), s, HorizontalAlignment.Left, -1, size, c);

    /// <summary>Left-aligned text vertically centred on <paramref name="centerY"/>.</summary>
    private void TextMid(Font f, float x, float centerY, string s, int size, Color c) =>
        C.DrawString(f, new Vector2(x, Baseline(f, centerY, size)), s, HorizontalAlignment.Left, -1, size, c);

    private void TextCentered(Font f, Rect2 r, string s, int size, Color c) =>
        C.DrawString(f, new Vector2(r.Position.X, Baseline(f, r.GetCenter().Y, size)), s, HorizontalAlignment.Center, r.Size.X, size, c);

    private static string Ellipsize(Font f, string s, int size, float width)
    {
        if (TextW(f, s, size) <= width) return s;
        while (s.Length > 1 && TextW(f, s + "…", size) > width) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    private static string Amount(int n) => n < 10000 ? n.ToString() : $"{n / 1000f:0.#}k";

    private static string RateText(float perMinute) => perMinute >= 10 ? $"{perMinute:0}" : $"{perMinute:0.#}";

    private string ItemName(string? item) => item == null ? "any item" : _world!.Data.Items[item].Name;

    private string CostText(IReadOnlyDictionary<string, int> cost, bool full = false) =>
        string.Join(full ? ", " : " ", cost.Select(c => $"{c.Value} {(full ? _world!.Data.Items[c.Key].Name : ShortName(c.Key))}"));

    private string ShortName(string item) => item switch
    {
        "iron_plate" => "iron",
        "copper_plate" => "copper",
        "silver_bar" => "silver",
        "gold_bar" => "gold",
        "ammo_magazine" => "ammo",
        "heavy_shell" => "shells",
        _ => _world!.Data.Items[item].Name.ToLowerInvariant(),
    };
}
