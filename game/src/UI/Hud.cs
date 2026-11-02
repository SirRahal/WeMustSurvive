using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using BeltAndBarricade.View;
using Godot;
using SVec = System.Numerics.Vector2;

namespace BeltAndBarricade.UI;

/// <summary>
/// Screen-space overlay in the style of an RTS console: wave/Nexus/research status (top-left), storage network
/// (top-right), super weapons, selection info (bottom-center), a 3x3 command card with categories (bottom-right),
/// minimap (bottom-left), research panel (T), ping wheel (Alt), tutorial panel, messages and end screens.
/// </summary>
public partial class Hud : Control
{
    private const int FontSize = 16;
    private const float Pad = 12f;
    private const double MessageSeconds = 3.5;
    private const float CellW = 104, CellH = 66, CellGap = 6;
    private const float InfoW = 480, InfoH = 178;
    private const float TutorialW = 620;
    private const float MinimapW = 260;
    private const float StorageW = 300;

    private static readonly Color PanelColor = new(0.05f, 0.06f, 0.08f, 0.85f);
    private static readonly Color PanelEdge = new(0.35f, 0.42f, 0.5f, 0.6f);
    private static readonly Color Text = new(0.92f, 0.93f, 0.95f);
    private static readonly Color Dim = new(0.6f, 0.63f, 0.68f);
    private static readonly Color Accent = new(0.35f, 0.8f, 1f);
    private static readonly Color Warn = new(1f, 0.45f, 0.35f);
    private static readonly Color Good = new(0.45f, 0.95f, 0.5f);
    private static readonly Color HintGlow = new(1f, 0.85f, 0.25f);
    private static readonly Color ButtonBg = new(0.11f, 0.13f, 0.17f, 0.95f);
    private static readonly Color ButtonHover = new(0.17f, 0.22f, 0.29f, 0.95f);
    private static readonly Color ButtonActive = new(0.15f, 0.3f, 0.4f, 0.95f);
    private static readonly string[] TierNames = { "Coal", "Iron", "Copper", "Silver", "Gold" };

    private static readonly string[] HelpLines =
    {
        "Camera: WASD / edge / middle-drag, wheel zoom, C engineer",
        "Build: card or 1-6 then 1-8, LMB place, drag lines, R rotate",
        "RMB: move, repair, resume site.  Shift: chain orders.  T: research",
        "Alt: ping wheel.  Ctrl+drag: draw.  F/E inserter filter/side",
        "Space: next wave now.  Esc: menu.  H: hide help",
    };

    /// <param name="Key">Identifies the button for tutorial highlighting (building id, "cat:&lt;id&gt;", "filter:&lt;item&gt;").</param>
    private sealed record Button(Rect2 Rect, string Label, string Hotkey, string Sub, bool Enabled, bool Active, Action OnClick,
        string? Key = null, string Tooltip = "");

    private readonly List<(string Text, double Until)> _messages = new();
    private readonly List<Button> _buttons = new();
    private readonly List<Button> _tutorialButtons = new();
    private readonly List<Button> _researchButtons = new();
    private readonly List<Button> _swButtons = new();
    private readonly List<Rect2> _panels = new();
    private World? _world;
    private PlayerInput? _input;
    private LockstepPeer? _peer;
    private Font _font = null!;
    private Rect2 _tutorialRect;
    private Rect2 _researchRect;
    private Rect2 _minimapRect;
    private ImageTexture? _minimapTex;
    private double _minimapAge;
    private ResourceWatch _watch = new();
    private List<ResourceStatus> _report = new();
    private double _reportAge;
    private readonly List<(Rect2 Rect, ResourceStatus Status)> _resourceRows = new();
    private readonly Dictionary<string, ResourceLevel> _alerted = new();
    private readonly Dictionary<string, double> _alertedAt = new();

    public Tutorial? Tutorial { get; set; }
    /// <summary>Logistics buildings whose items never arrive anywhere (refreshed a few times a second).</summary>
    public Dictionary<int, LogisticsIssue> Issues { get; set; } = new();
    public CameraController? Camera { get; set; }
    public Settings Settings { get; set; } = new();
    public bool IsCoop { get; set; }
    public bool Sandbox { get; set; }
    /// <summary>Meta points awarded for the finished game (shown on the end screen).</summary>
    public int MetaEarned { get; set; }
    public event Action? TutorialNextRequested;
    public event Action? TutorialSkipRequested;

    private IEnumerable<Button> AllButtons => _tutorialButtons.Concat(_researchButtons).Concat(_swButtons).Concat(_buttons);

    private bool TutorialActive => Tutorial is { Finished: false };
    private int Me => GameSession.LocalPlayer;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = ThemeDB.FallbackFont;
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
        var b = AllButtons.FirstOrDefault(b => b.Rect.HasPoint(m));
        if (b != null)
        {
            if (b.Enabled) b.OnClick();
            return;
        }
        if (_minimapRect.HasPoint(m)) CenterCameraOnMinimap(m);
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
        if (_world == null) return;
        _minimapAge += delta;
        UpdateResources(delta);
        Layout();
        var m = GetLocalMousePosition();
        if (Godot.Input.IsMouseButtonPressed(MouseButton.Left) && _minimapRect.HasPoint(m) && !AllButtons.Any(b => b.Rect.HasPoint(m)))
            CenterCameraOnMinimap(m);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null || _input == null) return;
        var size = GetViewportRect().Size;

        DrawWavePanel();
        DrawStoragePanel(size);
        DrawSuperWeaponStrip(size);
        DrawInfoPanel();
        DrawCommandCard();
        if (Settings.ShowMinimap) DrawMinimap();
        if (TutorialActive) DrawTutorial();
        else if (Settings.ShowHelp) DrawHelp();
        DrawTooltip();
        DrawMessages(size);
        if (_input.ResearchOpen) DrawResearch();
        if (_input.PingWheelOpen) DrawPingWheel();
        DrawDesync(size);
        DrawOutcome(size);
    }

    // ================================================================== layout

    private Rect2 CardRect
    {
        get
        {
            var size = GetViewportRect().Size;
            float w = 3 * CellW + 4 * CellGap, h = 3 * CellH + 4 * CellGap;
            return new Rect2(size.X - w - Pad, size.Y - h - Pad, w, h);
        }
    }

    private Rect2 InfoRect
    {
        get
        {
            var size = GetViewportRect().Size;
            float x = Math.Min((size.X - InfoW) / 2, CardRect.Position.X - InfoW - Pad);
            return new Rect2(x, size.Y - InfoH - Pad, InfoW, InfoH);
        }
    }

    private Rect2 HelpRect
    {
        get
        {
            float bottom = Settings.ShowMinimap ? _minimapRect.Position.Y - 8 : GetViewportRect().Size.Y - Pad;
            float h = HelpLines.Length * 18 + 12;
            return new Rect2(Pad, bottom - h, Math.Min(430, InfoRect.Position.X - Pad * 2), h);
        }
    }

    /// <summary>Rebuilds every clickable element and the list of mouse-blocking panels.</summary>
    private void Layout()
    {
        _buttons.Clear();
        _tutorialButtons.Clear();
        _researchButtons.Clear();
        _swButtons.Clear();
        _panels.Clear();
        _panels.Add(CardRect);
        _panels.Add(InfoRect);
        LayoutMinimap();
        if (Settings.ShowMinimap) _panels.Add(_minimapRect);
        LayoutTutorial();
        LayoutSuperWeapons();
        if (_input!.ResearchOpen) LayoutResearch();

        var actions = _input.SelectedBuilding is { } sel ? BuildingCommands(sel)
            : _input.CardCategory is { } cat ? CategoryCommands(cat)
            : MainCommands();
        var card = CardRect;
        for (int i = 0; i < actions.Count && i < 9; i++)
        {
            if (actions[i] is not { } a) continue;
            var rect = new Rect2(
                card.Position.X + CellGap + (i % 3) * (CellW + CellGap),
                card.Position.Y + CellGap + (i / 3) * (CellH + CellGap),
                CellW, CellH);
            _buttons.Add(a with { Rect = rect });
        }
    }

    // ================================================================== command card

    private List<Button?> MainCommands()
    {
        var input = _input!;
        var list = new List<Button?>();
        for (int i = 0; i < PlayerInput.Categories.Length; i++)
        {
            var (id, label) = PlayerInput.Categories[i];
            int unlocked = _world!.Data.InCategory(id).Count(_world.Research.IsUnlocked);
            int total = _world.Data.InCategory(id).Count();
            list.Add(new Button(default, label, (i + 1).ToString(), $"{unlocked}/{total} open", true, false,
                () => input.OpenCategory(id), $"cat:{id}", CategoryTooltip(id)));
        }
        var research = _world!.Research;
        list.Add(new Button(default, "Research", "T", research.Current != null ? $"{research.Fraction:P0}" : "pick a tech", true,
            input.ResearchOpen, () => input.ResearchOpen = !input.ResearchOpen, "research", "Open the research panel."));
        list.Add(new Button(default, "Stop", "X", "clear orders", _world.Engineers[Me].Orders.Count > 0, false, input.Stop,
            Tooltip: "Clear the engineer's orders. Unstarted blueprints are refunded."));
        list.Add(new Button(default, "Deconstruct", "Del", "full refund", true, input.DeconstructTool, input.ToggleDeconstructTool,
            Tooltip: "Click or drag over buildings to remove them for a full refund."));
        return list;
    }

    private string CategoryTooltip(string id) => id switch
    {
        "logistics" => "Belts, tunnels, splitters and inserters.",
        "production" => "Drills, furnaces, munitions and labs.",
        "power" => "Generators and power poles.",
        "walls" => "Walls of every tier.",
        "turrets" => "Gun, laser, rocket, flame and heavy turrets.",
        "special" => "Storage depots and super weapons.",
        _ => "",
    };

    private List<Button?> CategoryCommands(string category)
    {
        var input = _input!;
        var w = _world!;
        var slots = new Button?[9];
        var defs = w.Data.InCategory(category).ToList();
        for (int i = 0; i < defs.Count && i < 8; i++)
        {
            var def = defs[i];
            bool unlocked = w.Research.IsUnlocked(def);
            string sub = unlocked ? CostText(def.Cost) : "LOCKED";
            slots[i] = new Button(default, def.Name, (i + 1).ToString(), sub, unlocked, input.BuildTool == def,
                () => input.SelectBuildTool(def), def.Id, BuildingTooltip(def, unlocked));
        }
        slots[8] = new Button(default, "Back", "Esc", "main card", true, false, () => input.OpenCategory(null), "back");
        return slots.ToList();
    }

    private string BuildingTooltip(BuildingDef def, bool unlocked)
    {
        var lines = new List<string> { def.Name, def.Description, "Cost: " + CostText(def.Cost, full: true) };
        if (def.PowerDraw > 0) lines.Add($"Power: {def.PowerDraw:0} kW");
        if (def.PowerOutput > 0) lines.Add($"Produces {def.PowerOutput:0} kW");
        if (def.Kind == BuildingKind.Turret) lines.Add($"Range {def.Range:0.#}, damage {def.Damage:0.#}" + (def.Splash > 0 ? $" (splash {def.Splash:0.#})" : ""));
        if (def.Kind == BuildingKind.Wall) lines.Add($"HP {def.MaxHp:0}");
        if (!unlocked) lines.Add($"Requires research: {_world!.Data.Techs[def.Tech!].Name} (press T)");
        return string.Join("\n", lines);
    }

    private List<Button?> BuildingCommands(Building b)
    {
        // Fixed slots: top rows = building-specific, bottom row = repair / deconstruct / back.
        var input = _input!;
        var w = _world!;
        var slots = new Button?[9];
        bool rotatable = b.Size == 1 && b.Def.Kind is BuildingKind.Belt or BuildingKind.Inserter or BuildingKind.Drill
            or BuildingKind.Splitter or BuildingKind.TunnelEntrance or BuildingKind.TunnelExit;
        var rotate = new Button(default, "Rotate", "R", b.Dir.ToString(), true, false, input.RotateSelected);

        if (b is ConstructionSite site)
        {
            slots[0] = new Button(default, "Resume build", "RMB", $"{site.Fraction:P0} done", true, false, input.RepairSelected);
            if (rotatable) slots[1] = rotate;
        }
        else if (b is Inserter ins)
        {
            slots[0] = new Button(default, "Auto filter", "F", ins.AutoFilter ? ItemName(ins.Filter) : "pick for me", true, ins.AutoFilter,
                input.SetSelectedAutoFilter, "filter:auto", "Let the inserter choose: what the building in front needs,\nor what the building behind makes.");
            for (int i = 0; i < 3; i++)
            {
                string? item = PlayerInput.FilterChoices[i];
                string label = item == null ? "Any item" : w.Data.Items[item].Name;
                slots[i + 1] = new Button(default, label, "F", "filter", true, !ins.AutoFilter && ins.Filter == item,
                    () => input.SetSelectedFilter(item), $"filter:{item}");
            }
            slots[4] = new Button(default, "Take from", "E", ins.Input.ToString().ToLowerInvariant(), true, false, input.CycleInputSide,
                "input", "Which side to take from: Auto (behind, then left, then right),\nor fixed Back / Left / Right. Inserters drop where the arrow points.");
            slots[5] = rotate;
        }
        else if (b is SuperWeapon sw)
        {
            bool ready = sw.IsReady(w);
            slots[0] = new Button(default, sw.Def.AbilityName, "LMB", ready ? "READY - click target" : $"charging {sw.Charge / sw.ChargeNeeded(w):P0}",
                ready, input.AbilityBuildingId == sw.Id, () => input.BeginAbilityTargeting(sw.Id),
                Tooltip: $"Radius {sw.Def.StrikeRadius:0.#}, damage {sw.Def.StrikeDamage * w.Mods.Mult(Stat.SuperWeaponDamage):0}.");
        }
        else if (rotatable)
        {
            slots[0] = rotate;
        }

        if (b is not ConstructionSite)
            slots[6] = new Button(default, "Repair", "RMB", "iron plates", b.Hp < b.MaxHp, false, input.RepairSelected);
        if (b is not Nexus)
            slots[7] = new Button(default, b is ConstructionSite ? "Cancel" : "Deconstruct", "Del", "full refund", true, false, input.DeconstructSelected);
        slots[8] = new Button(default, "Back", "Esc", "deselect", true, false, input.Deselect, "back");
        return slots.ToList();
    }

    private void DrawCommandCard()
    {
        DrawPanel(CardRect);
        var mouse = GetLocalMousePosition();
        var highlight = TutorialHighlightKeys();
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Time.GetTicksMsec() / 180f);
        foreach (var b in _buttons) DrawCardButton(b, mouse, highlight, pulse);
    }

    private void DrawCardButton(Button b, Vector2 mouse, HashSet<string> highlight, float pulse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        DrawRect(b.Rect, b.Active ? ButtonActive : hover && b.Enabled ? ButtonHover : ButtonBg);
        DrawRect(b.Rect, b.Active ? Accent : PanelEdge, false, b.Active ? 2f : 1f);
        if (b.Key != null && highlight.Contains(b.Key) && !b.Active)
            DrawRect(b.Rect.Grow(2 + 2 * pulse), HintGlow with { A = 0.5f + 0.5f * pulse }, false, 3f);
        var color = b.Enabled ? Text : Dim;
        DrawString(_font, b.Rect.Position + new Vector2(6, 20), b.Label, HorizontalAlignment.Left, b.Rect.Size.X - 12, b.Label.Length > 12 ? 12 : 14, color);
        DrawString(_font, b.Rect.Position + new Vector2(6, b.Rect.Size.Y - 10), b.Sub, HorizontalAlignment.Left, b.Rect.Size.X - 34, 12, b.Enabled ? Dim : Warn);
        DrawString(_font, b.Rect.Position + new Vector2(b.Rect.Size.X - 34, b.Rect.Size.Y - 10), b.Hotkey, HorizontalAlignment.Right, 28, 12, Accent);
    }

    /// <summary>Keys to glow for the current tutorial step: the button itself and the category that contains it.</summary>
    private HashSet<string> TutorialHighlightKeys()
    {
        var keys = new HashSet<string>();
        if (!TutorialActive || Tutorial!.CurrentButton(_world!) is not { } key) return keys;
        keys.Add(key);
        if (_world!.Data.Buildings.TryGetValue(key, out var def))
        {
            keys.Add($"cat:{def.Category}");
            // Wrong page for this blueprint (another category open, or a building selected): point at Back first.
            var input = _input!;
            if (input.SelectedBuilding != null || input.CardCategory != null && input.CardCategory != def.Category)
                keys.Add("back");
        }
        else if (key.StartsWith("filter:") && _input!.SelectedBuilding is not Inserter && _input.CardCategory != null)
        {
            keys.Add("back");
        }
        return keys;
    }

    // ================================================================== research panel

    private void LayoutResearch()
    {
        var size = GetViewportRect().Size;
        _researchRect = new Rect2((size.X - 940) / 2, 70, 940, 540);
        _panels.Add(_researchRect);
        var w = _world!;
        var input = _input!;
        _researchButtons.Add(new Button(new Rect2(_researchRect.End.X - 110, _researchRect.Position.Y + 10, 100, 30), "Close", "T", "", true, false,
            () => input.ResearchOpen = false));
        if (w.Research.Current != null)
            _researchButtons.Add(new Button(new Rect2(_researchRect.End.X - 220, _researchRect.Position.Y + 10, 100, 30), "Cancel", "", "", true, false,
                input.CancelResearch, Tooltip: "Stop and refund the current research."));

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
                _researchButtons.Add(new Button(rect, t.Name, done ? "OK" : "", sub, !done && !current && why == null, current,
                    () => input.StartResearch(t.Id), $"tech:{t.Id}",
                    $"{t.Name}\n{t.Description}\nCost: {CostText(t.Cost, full: true)}\nTime: {t.Ticks / 60f:0}s with one lab" +
                    (t.Requires.Count > 0 ? "\nRequires: " + string.Join(", ", t.Requires.Select(r => w.Data.Techs[r].Name)) : "") +
                    (why != null ? $"\n{why}" : "") +
                    "\nUnlocks: " + string.Join(", ", w.Data.BuildingList.Where(b => b.Tech == t.Id).Select(b => b.Name))));
            }
        }
    }

    private void DrawResearch()
    {
        var r = _researchRect;
        var w = _world!;
        DrawRect(r, new Color(0.04f, 0.05f, 0.07f, 0.96f));
        DrawRect(r, Accent with { A = 0.6f }, false, 2f);
        DrawString(_font, r.Position + new Vector2(16, 30), "RESEARCH", HorizontalAlignment.Left, -1, 22, Accent);
        int labs = w.Buildings.Count(b => b is Lab);
        string status = w.Research.Current is { } cur
            ? $"Researching {cur.Name}: {w.Research.Fraction:P0}   ({labs} lab{(labs == 1 ? "" : "s")})"
            : labs == 0 ? "Build a Laboratory (Production) to research. Costs are paid when research starts."
            : $"Pick a technology. {labs} lab{(labs == 1 ? "" : "s")} working.";
        DrawString(_font, r.Position + new Vector2(16, 56), status, HorizontalAlignment.Left, r.Size.X - 250, 15, labs == 0 ? Warn : Text);
        if (w.Research.Current != null)
        {
            var bar = new Rect2(r.Position + new Vector2(16, 66), new Vector2(r.Size.X - 250, 8));
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * w.Research.Fraction, bar.Size.Y)), Accent);
        }
        for (int tier = 1; tier <= 4; tier++)
            DrawString(_font, r.Position + new Vector2(16 + (tier - 1) * 230, 100), $"TIER {tier} - {TierNames[tier].ToUpperInvariant()}",
                HorizontalAlignment.Left, -1, 13, Dim);

        var mouse = GetLocalMousePosition();
        var highlight = TutorialHighlightKeys();
        foreach (var b in _researchButtons)
        {
            if (b.Key == null)
            {
                DrawPlainButton(b, mouse);
                continue;
            }
            DrawCardButton(b, mouse, highlight, 0);
            if (b.Hotkey == "OK") DrawRect(b.Rect, Good with { A = 0.25f });
        }
    }

    private void DrawPlainButton(Button b, Vector2 mouse)
    {
        bool hover = b.Rect.HasPoint(mouse);
        DrawRect(b.Rect, hover ? ButtonHover : ButtonBg);
        DrawRect(b.Rect, PanelEdge, false, 1f);
        DrawString(_font, b.Rect.Position + new Vector2(0, 20), b.Label, HorizontalAlignment.Center, b.Rect.Size.X, 14, Text);
    }

    // ================================================================== super weapons

    private Rect2 SuperWeaponRect => new(GetViewportRect().Size.X - StorageW - Pad, StorageBottom + 8, StorageW, 0);
    private float StorageBottom { get; set; } = 120;

    private void LayoutSuperWeapons()
    {
        var w = _world!;
        var input = _input!;
        var sws = w.Buildings.OfType<SuperWeapon>().ToList();
        float y = SuperWeaponRect.Position.Y;
        foreach (var sw in sws)
        {
            bool ready = sw.IsReady(w);
            var rect = new Rect2(SuperWeaponRect.Position.X, y, StorageW, 34);
            _swButtons.Add(new Button(rect, sw.Def.AbilityName, "", ready ? "READY" : $"{sw.Charge / sw.ChargeNeeded(w):P0}", ready,
                input.AbilityBuildingId == sw.Id, () => input.BeginAbilityTargeting(sw.Id), Tooltip: "Click, then click a target on the map."));
            _panels.Add(rect);
            y += 40;
        }
    }

    private void DrawSuperWeaponStrip(Vector2 size)
    {
        var mouse = GetLocalMousePosition();
        foreach (var b in _swButtons)
        {
            bool hover = b.Rect.HasPoint(mouse);
            var bg = b.Enabled ? new Color(0.45f, 0.12f, 0.1f, 0.95f) : PanelColor;
            DrawRect(b.Rect, hover && b.Enabled ? bg.Lightened(0.2f) : bg);
            DrawRect(b.Rect, b.Enabled ? HintGlow : PanelEdge, false, b.Enabled ? 2 : 1);
            DrawString(_font, b.Rect.Position + new Vector2(10, 22), b.Label, HorizontalAlignment.Left, -1, 15, Text);
            DrawString(_font, b.Rect.Position + new Vector2(0, 22), b.Sub, HorizontalAlignment.Right, b.Rect.Size.X - 10, 14, b.Enabled ? HintGlow : Dim);
        }
    }

    // ================================================================== minimap

    private float MinimapScale => MinimapW / ((_world!.Map.Width + _world.Map.Height) / 2f);

    private void LayoutMinimap()
    {
        var map = _world!.Map;
        float s = MinimapScale;
        float h = (map.Width + map.Height) * s / 4;
        _minimapRect = new Rect2(Pad, GetViewportRect().Size.Y - h - Pad - 16, MinimapW, h + 16);
    }

    private Vector2 MinimapPoint(float gx, float gy)
    {
        float s = MinimapScale;
        return _minimapRect.Position + new Vector2((gx - gy) * s / 2 + _world!.Map.Height * s / 2, 8 + (gx + gy) * s / 4);
    }

    private void CenterCameraOnMinimap(Vector2 m)
    {
        if (Camera == null) return;
        float s = MinimapScale;
        var local = m - _minimapRect.Position - new Vector2(_world!.Map.Height * s / 2, 8);
        float a = local.X / (s / 2), b = local.Y / (s / 4);
        Camera.CenterOn(Iso.ToScreen((a + b) / 2, (b - a) / 2));
    }

    private void DrawMinimap()
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

        DrawRect(_minimapRect, PanelColor);
        DrawRect(_minimapRect, PanelEdge, false, 1f);
        float s = MinimapScale;
        var origin = _minimapRect.Position + new Vector2(map.Height * s / 2, 8);
        DrawSetTransformMatrix(new Transform2D(new Vector2(s / 2, s / 4), new Vector2(-s / 2, s / 4), origin));
        DrawTexture(_minimapTex, Vector2.Zero);
        foreach (var b in _world.Buildings)
            DrawRect(new Rect2(b.Origin.X, b.Origin.Y, b.Size, b.Size), b is Nexus ? Accent : b is ConstructionSite ? Accent with { A = 0.4f } : new Color(0.85f, 0.85f, 0.9f));
        foreach (var e in _world.Enemies)
            DrawRect(new Rect2(e.Pos.X - 0.7f, e.Pos.Y - 0.7f, 1.4f, 1.4f), Warn);
        DrawSetTransformMatrix(Transform2D.Identity);

        foreach (var eng in _world.Engineers.Where(e => e.Alive))
            DrawCircle(MinimapPoint(eng.Pos.X, eng.Pos.Y), 3.5f, WorldView.PlayerColor(eng.PlayerId));

        // Camera view outline.
        if (Camera != null)
        {
            var vp = GetViewportRect().Size / Camera.Zoom;
            var c = Camera.GetScreenCenterPosition();
            var corners = new[] { c - vp / 2, c + new Vector2(vp.X, -vp.Y) / 2, c + vp / 2, c + new Vector2(-vp.X, vp.Y) / 2 }
                .Select(p => { var g = Iso.ToGrid(p); return MinimapPoint(g.X, g.Y); }).ToArray();
            DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, Colors.White with { A = 0.7f }, 1f);
        }
        DrawString(_font, _minimapRect.Position + new Vector2(6, _minimapRect.Size.Y - 4), _world.MapDef.Name, HorizontalAlignment.Left, -1, 11, Dim);
    }

    // ================================================================== panels

    private void DrawWavePanel()
    {
        var w = _world!;
        var waves = w.Waves;
        var lines = new List<(string, Color)> { ($"Wave {waves.WavesStarted} / {waves.TotalWaves}", Accent) };
        if (waves.Held)
        {
            lines.Add(("Waves on hold", Good));
            lines.Add((Sandbox ? "(press Space to start)" : "(starts after the tutorial)", Dim));
        }
        else if (waves.HasMoreWaves)
        {
            int secs = (waves.TicksUntilNextWave + World.TicksPerSecond - 1) / World.TicksPerSecond;
            lines.Add(($"Next wave in {secs / 60}:{secs % 60:00}" + (waves.IsSpawning ? "" : "  [Space]"), secs <= 15 ? Warn : Text));
            var preview = string.Join(", ", waves.PreviewNext(w).Select(p => $"{p.Count} {p.Enemy.Name}"));
            lines.Add(($"Next: {preview}", Dim));
        }
        else
        {
            lines.Add(("Final wave - hold out!", Warn));
        }
        lines.Add(($"Enemies: {w.Enemies.Count}", Text));
        lines.Add((w.Research.Current is { } t ? $"Research: {t.Name} {w.Research.Fraction:P0}" : "Research: none  [T]", w.Research.Current != null ? Text : Dim));
        if (w.Power.GridOf(w.Nexus) is { } grid)
            lines.Add(($"Base power: {grid.Demand:0}/{grid.Capacity:0} kW", grid.Satisfaction < 1 ? Warn : Dim));
        foreach (var eng in w.Engineers.Where(e => e.PlayerId != Me))
            lines.Add(($"{PlayerName(eng.PlayerId)}: {(eng.Alive ? DescribeOrder(eng) : "downed")}", WorldView.PlayerColor(eng.PlayerId)));

        var rect = DrawLines(new Vector2(Pad, Pad), lines, 300);
        var bar = new Rect2(rect.Position + new Vector2(0, rect.Size.Y + 6), new Vector2(300, 18));
        float frac = Math.Max(0, w.Nexus.Hp / w.Nexus.MaxHp);
        DrawRect(bar, PanelColor);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * frac, bar.Size.Y)), frac > 0.35f ? Good : Warn);
        DrawString(_font, bar.Position + new Vector2(6, 14), $"Nexus {Math.Ceiling(w.Nexus.Hp)} / {w.Nexus.MaxHp}", HorizontalAlignment.Left, -1, 13, Colors.Black);
        _panels.Add(rect.Merge(bar));
    }

    /// <summary>
    /// Storage with a trend per item (net change per minute) and a LOW / EMPTY / STUCK / FALLING tag.
    /// Hovering a row explains what's using it, what's stalled, and what to build about it.
    /// </summary>
    private void DrawStoragePanel(Vector2 size)
    {
        var w = _world!;
        const float rowH = 22;
        int depots = w.Buildings.Count(b => b is Depot);
        var rect = new Rect2(size.X - StorageW - Pad, Pad, StorageW, 34 + _report.Count * rowH + (depots > 0 ? 20 : 0) + 8);
        DrawRect(rect, PanelColor);
        int problems = _report.Count(r => r.Level >= ResourceLevel.Stuck);
        DrawString(_font, rect.Position + new Vector2(10, 22), "Storage", HorizontalAlignment.Left, -1, FontSize, Accent);
        DrawString(_font, rect.Position + new Vector2(0, 22), problems > 0 ? $"{problems} problem{(problems == 1 ? "" : "s")} - hover for help" : "per minute",
            HorizontalAlignment.Right, StorageW - 10, 12, problems > 0 ? Warn : Dim);

        _resourceRows.Clear();
        bool blink = Time.GetTicksMsec() / 500 % 2 == 0;
        float y = rect.Position.Y + 32;
        foreach (var r in _report)
        {
            var row = new Rect2(rect.Position.X + 4, y, StorageW - 8, rowH - 2);
            _resourceRows.Add((row, r));
            var tint = r.Level switch
            {
                ResourceLevel.Empty => Warn with { A = blink ? 0.45f : 0.25f },
                ResourceLevel.Low => Warn with { A = 0.25f },
                ResourceLevel.Stuck or ResourceLevel.Falling => HintGlow with { A = 0.15f },
                _ => new Color(0, 0, 0, 0),
            };
            if (row.HasPoint(GetLocalMousePosition())) tint = tint.A > 0 ? tint with { A = tint.A + 0.15f } : ButtonHover;
            DrawRect(row, tint);

            DrawRect(new Rect2(row.Position + new Vector2(6, 5), new Vector2(10, 10)), Iso.ParseColor(w.Data.Items[r.Item].Color));
            float baseY = row.Position.Y + 15;
            DrawString(_font, new Vector2(row.Position.X + 22, baseY), w.Data.Items[r.Item].Name, HorizontalAlignment.Left, 120, 14, Text);
            DrawString(_font, new Vector2(row.Position.X + 120, baseY), r.Count.ToString(), HorizontalAlignment.Right, 60, 14, Text);
            int net = (int)MathF.Round(r.NetPerMinute);
            DrawString(_font, new Vector2(row.Position.X + 186, baseY), net == 0 ? "-" : net > 0 ? $"+{net}" : $"{net}",
                HorizontalAlignment.Left, 50, 13, net > 0 ? Good : net < 0 ? Warn : Dim);
            if (r.Short != "")
                DrawString(_font, new Vector2(row.Position.X, baseY), r.Short, HorizontalAlignment.Right, row.Size.X - 4, 12,
                    r.Level >= ResourceLevel.Low ? Warn : HintGlow);
            y += rowH;
        }
        if (depots > 0)
            DrawString(_font, new Vector2(rect.Position.X + 10, y + 14), $"Nexus + {depots} depot{(depots == 1 ? "" : "s")}", HorizontalAlignment.Left, -1, 13, Dim);
        StorageBottom = rect.End.Y;
        _panels.Add(rect);
    }

    private string ResourceTooltip(ResourceStatus r)
    {
        var name = _world!.Data.Items[r.Item].Name;
        var lines = new List<string> { r.Short == "" ? name : $"{name}: {r.Short}" };
        foreach (var a in r.Advice) lines.AddRange(Wrap(a, 66));
        return string.Join("\n", lines);
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

        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var r in _report)
        {
            var before = _alerted.GetValueOrDefault(r.Item);
            _alerted[r.Item] = r.Level;
            if (r.Level <= before || r.Level < ResourceLevel.Stuck || w.Tick < 5 * World.TicksPerSecond) continue;
            if (_alertedAt.TryGetValue(r.Item, out double at) && now - at < 30) continue;
            _alertedAt[r.Item] = now;
            Notify($"{w.Data.Items[r.Item].Name} is {r.Short}. Hover it in Storage (top right) to see what to do.");
        }
    }

    private void DrawInfoPanel()
    {
        var rect = InfoRect;
        DrawPanel(rect);
        var w = _world!;
        var lines = new List<(string, Color)>();
        float hpFrac;
        string title;

        if (_input!.SelectedBuilding is { } b)
        {
            title = b is ConstructionSite ? $"{b.Def.Name} (construction site)" : b.Def.Name;
            hpFrac = b.Hp / b.MaxHp;
            lines.Add(($"HP {Math.Ceiling(b.Hp)} / {b.MaxHp:0}", Text));
            if (b.Status != "") lines.Add((b.Status, Text));
            lines.AddRange(BuildingDetails(b).Select(l => (l, Dim)));
        }
        else
        {
            var eng = w.Engineers[Me];
            title = IsCoop ? $"Engineer ({PlayerName(Me)})" : "Engineer";
            hpFrac = eng.Hp / Engineer.MaxHp;
            if (!eng.Alive)
            {
                lines.Add(($"Downed - respawning in {eng.RespawnIn / World.TicksPerSecond + 1}s", Warn));
            }
            else
            {
                lines.Add(($"HP {Math.Ceiling(eng.Hp)} / {Engineer.MaxHp}", Text));
                lines.Add((DescribeOrder(eng), Text));
                if (eng.Orders.Count > 1) lines.Add(($"+{eng.Orders.Count - 1} queued order(s)", Dim));
            }
            if (_input.BuildTool is { } tool) lines.Add(($"Placing {tool.Name}: LMB place, R rotate, RMB cancel", Accent));
            else if (_input.DeconstructTool) lines.Add(("Deconstructing: LMB / drag to remove, RMB cancel", Warn));
            else if (_input.AbilityBuildingId != null) lines.Add(("Click a target for the strike. RMB cancels.", HintGlow));
            else if (_input.CardCategory != null) lines.Add(("Pick a blueprint (number keys) or Esc for the main card", Dim));
        }

        DrawString(_font, rect.Position + new Vector2(12, 24), title, HorizontalAlignment.Left, InfoW - 24, 18, Accent);
        var bar = new Rect2(rect.Position + new Vector2(12, 34), new Vector2(InfoW - 24, 8));
        DrawRect(bar, new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Math.Clamp(hpFrac, 0, 1), bar.Size.Y)), hpFrac > 0.35f ? Good : Warn);
        float y = rect.Position.Y + 62;
        foreach (var (text, color) in lines.Take(6))
        {
            DrawString(_font, new Vector2(rect.Position.X + 12, y), text, HorizontalAlignment.Left, InfoW - 24, 15, color);
            y += 19;
        }
    }

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
                yield return w.Research.Current is { } cur ? $"Researching {cur.Name}" : "Press T to choose research";
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
        if (eng.Orders.Count == 0) return "Idle - right-click to move, or pick a blueprint";
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

    private void DrawTooltip()
    {
        var m = GetLocalMousePosition();
        string? text = AllButtons.FirstOrDefault(b => b.Rect.HasPoint(m))?.Tooltip;
        if (string.IsNullOrEmpty(text) && _resourceRows.FirstOrDefault(r => r.Rect.HasPoint(m)) is { Status: { } hovered }) text = ResourceTooltip(hovered);
        if (string.IsNullOrEmpty(text) && !IsMouseOverHud()) text = WorldTooltip();
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Split('\n');
        float width = lines.Max(l => _font.GetStringSize(l, HorizontalAlignment.Left, -1, 14).X) + 16;
        float height = lines.Length * 18 + 8;
        var size = GetViewportRect().Size;
        var pos = m + new Vector2(18, 24);
        if (pos.X + width > size.X) pos.X = size.X - width - 4;
        if (pos.Y + height > size.Y) pos.Y = m.Y - height - 8;
        DrawRect(new Rect2(pos, new Vector2(width, height)), new Color(0.03f, 0.04f, 0.06f, 0.95f));
        DrawRect(new Rect2(pos, new Vector2(width, height)), PanelEdge, false, 1f);
        float y = pos.Y + 18;
        for (int i = 0; i < lines.Length; i++)
        {
            DrawString(_font, new Vector2(pos.X + 8, y), lines[i], HorizontalAlignment.Left, -1, 14, i == 0 ? Accent : Text);
            y += 18;
        }
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
            if (Issues.TryGetValue(b.Id, out var issue)) text += "\n" + DescribeIssue(b, issue);
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

    private void DrawMessages(Vector2 size)
    {
        float y = size.Y * 0.3f;
        foreach (var (text, _) in _messages)
        {
            DrawString(_font, new Vector2(1, y + 1), text, HorizontalAlignment.Center, size.X, 20, Colors.Black);
            DrawString(_font, new Vector2(0, y), text, HorizontalAlignment.Center, size.X, 20, Text);
            y += 26;
        }
    }

    private void DrawHelp()
    {
        var rect = HelpRect;
        DrawRect(rect, PanelColor);
        float y = rect.Position.Y + 20;
        foreach (var line in HelpLines)
        {
            DrawString(_font, new Vector2(rect.Position.X + 10, y), line, HorizontalAlignment.Left, rect.Size.X - 16, 13, Dim);
            y += 18;
        }
        _panels.Add(rect);
    }

    private void DrawPingWheel()
    {
        var c = _input!.PingWheelScreen;
        int choice = _input.PingWheelChoice();
        DrawCircle(c, 110, new Color(0, 0, 0, 0.55f));
        DrawArc(c, 110, 0, Mathf.Tau, 48, PanelEdge, 2, true);
        for (int i = 1; i < PlayerInput.PingOptions.Length; i++)
        {
            float a = (i - 1 + 0.5f) * Mathf.Tau / 7;
            var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * 75;
            bool sel = i == choice;
            if (sel) DrawCircle(p, 30, HintGlow with { A = 0.35f });
            DrawString(_font, p + new Vector2(-50, 5), PlayerInput.PingOptions[i].Label, HorizontalAlignment.Center, 100, sel ? 15 : 13, sel ? HintGlow : Text);
        }
        DrawCircle(c, 24, choice == 0 ? HintGlow with { A = 0.5f } : PanelColor);
        DrawString(_font, c + new Vector2(-40, 5), "Here", HorizontalAlignment.Center, 80, 13, Text);
    }

    private void DrawDesync(Vector2 size)
    {
        if (_peer is not LockstepClient { DesyncTick: long tick }) return;
        var r = new Rect2(size.X / 2 - 300, size.Y * 0.22f, 600, 30);
        DrawRect(r, new Color(0.5f, 0.05f, 0.05f, 0.9f));
        DrawString(_font, r.Position + new Vector2(0, 21), $"Desync detected (tick {tick}). Ask the host to restart.", HorizontalAlignment.Center, r.Size.X, 15, Text);
    }

    private void DrawOutcome(Vector2 size)
    {
        var w = _world!;
        if (w.Outcome == Outcome.Playing) return;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.55f));
        bool won = w.Outcome == Outcome.Won;
        string title = won ? $"{w.MapDef.Name.ToUpperInvariant()} HOLDS" : w.Surrendered ? "YOU SURRENDERED" : "THE NEXUS HAS FALLEN";
        DrawString(_font, new Vector2(0, size.Y / 2 - 30), title, HorizontalAlignment.Center, size.X, 48, won ? Good : Warn);
        DrawString(_font, new Vector2(0, size.Y / 2 + 10), $"Waves reached: {w.Waves.WavesStarted} / {w.Waves.TotalWaves}", HorizontalAlignment.Center, size.X, 18, Text);
        if (won) DrawString(_font, new Vector2(0, size.Y / 2 + 38), $"+{MetaEarned} meta points for the Skill Tree", HorizontalAlignment.Center, size.X, 20, HintGlow);
        string next = IsCoop && Me != 0 ? "Waiting for the host to restart, or Esc for the menu" : "Press Enter to play again, or Esc for the menu";
        DrawString(_font, new Vector2(0, size.Y / 2 + 72), next, HorizontalAlignment.Center, size.X, 18, Text);
    }

    // ================================================================== tutorial

    private const int TutorialBodySize = 15;

    private void LayoutTutorial()
    {
        if (!TutorialActive) return;
        var tut = Tutorial!;
        var size = GetViewportRect().Size;
        float bodyH = _font.GetMultilineStringSize(tut.Current.Body, HorizontalAlignment.Left, TutorialW - 32, TutorialBodySize).Y;
        _tutorialRect = new Rect2((size.X - TutorialW) / 2, Pad, TutorialW, 74 + bodyH + 48);
        _panels.Add(_tutorialRect);

        float by = _tutorialRect.End.Y - 40;
        float right = _tutorialRect.End.X - 16;
        if (tut.NeedsNext)
        {
            _tutorialButtons.Add(new Button(new Rect2(right - 130, by, 130, 30), tut.IsLastStep ? "Finish" : "Next  >", "Enter", "",
                true, true, () => TutorialNextRequested?.Invoke(), "tut"));
            right -= 140;
        }
        _tutorialButtons.Add(new Button(new Rect2(right - 130, by, 130, 30), "Skip tutorial", "", "",
            true, false, () => TutorialSkipRequested?.Invoke(), "tut"));
    }

    private void DrawTutorial()
    {
        var tut = Tutorial!;
        var r = _tutorialRect;
        DrawRect(r, PanelColor);
        DrawRect(r, HintGlow with { A = 0.7f }, false, 2f);

        DrawString(_font, r.Position + new Vector2(16, 22), $"TUTORIAL - STEP {tut.StepIndex + 1} OF {tut.StepCount}", HorizontalAlignment.Left, -1, 12, HintGlow);
        DrawString(_font, r.Position + new Vector2(16, 50), tut.Current.Title, HorizontalAlignment.Left, TutorialW - 32, 22, Text);
        DrawMultilineString(_font, r.Position + new Vector2(16, 76), tut.Current.Body, HorizontalAlignment.Left, TutorialW - 32, TutorialBodySize, -1, Text);

        if (!tut.NeedsNext)
            DrawString(_font, new Vector2(r.Position.X + 16, r.End.Y - 20), "Complete the step to continue", HorizontalAlignment.Left, -1, 13, Dim);

        var mouse = GetLocalMousePosition();
        foreach (var b in _tutorialButtons)
        {
            bool hover = b.Rect.HasPoint(mouse);
            DrawRect(b.Rect, b.Active ? (hover ? HintGlow : HintGlow.Darkened(0.15f)) : (hover ? ButtonHover : ButtonBg));
            DrawRect(b.Rect, b.Active ? HintGlow : PanelEdge, false, 1f);
            string label = b.Hotkey == "" ? b.Label : $"{b.Label}  ({b.Hotkey})";
            DrawString(_font, b.Rect.Position + new Vector2(0, 20), label, HorizontalAlignment.Center, b.Rect.Size.X, 14, b.Active ? Colors.Black : Text);
        }
    }

    // ================================================================== helpers

    private void DrawPanel(Rect2 rect)
    {
        DrawRect(rect, PanelColor);
        DrawRect(rect, PanelEdge, false, 1f);
    }

    /// <summary>Draws a panel with one line of text per entry and returns its rect.</summary>
    private Rect2 DrawLines(Vector2 pos, List<(string Text, Color Color)> lines, float width)
    {
        var rect = new Rect2(pos, new Vector2(width, lines.Count * 20 + 12));
        DrawRect(rect, PanelColor);
        float y = pos.Y + 22;
        foreach (var (text, color) in lines)
        {
            DrawString(_font, new Vector2(pos.X + 10, y), text, HorizontalAlignment.Left, width - 16, FontSize, color);
            y += 20;
        }
        return rect;
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
