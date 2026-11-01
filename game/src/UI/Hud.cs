using BeltAndBarricade.Input;
using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// Screen-space overlay in the style of an RTS console: wave/Nexus status (top-left), storage (top-right),
/// selection info (bottom-center), and a 3x3 command card (bottom-right) whose buttons drive <see cref="PlayerInput"/>.
/// </summary>
public partial class Hud : Control
{
    private const int FontSize = 16;
    private const float Pad = 12f;
    private const double MessageSeconds = 3.0;
    private const float CellW = 104, CellH = 66, CellGap = 6;
    private const float InfoW = 460, InfoH = 140;

    private static readonly Color PanelColor = new(0.05f, 0.06f, 0.08f, 0.85f);
    private static readonly Color PanelEdge = new(0.35f, 0.42f, 0.5f, 0.6f);
    private static readonly Color Text = new(0.92f, 0.93f, 0.95f);
    private static readonly Color Dim = new(0.6f, 0.63f, 0.68f);
    private static readonly Color Accent = new(0.35f, 0.8f, 1f);
    private static readonly Color Warn = new(1f, 0.45f, 0.35f);
    private static readonly Color Good = new(0.45f, 0.95f, 0.5f);
    private static readonly Color ButtonBg = new(0.11f, 0.13f, 0.17f, 0.95f);
    private static readonly Color ButtonHover = new(0.17f, 0.22f, 0.29f, 0.95f);
    private static readonly Color ButtonActive = new(0.15f, 0.3f, 0.4f, 0.95f);

    private static readonly string[] HelpLines =
    {
        "Camera: WASD / edge / middle-drag, wheel zoom, C engineer",
        "Build: card or 1-7, LMB place, drag for lines, R rotate",
        "RMB: move (Shift: after current jobs), repair, resume site",
        "LMB building: select.  F filter  X stop  Del deconstruct",
        "Space: next wave now.  Esc: menu.  H: hide help",
    };

    private const float TutorialW = 620;
    private static readonly Color HintGlow = new(1f, 0.85f, 0.25f);

    /// <param name="Key">Identifies the button for tutorial highlighting (building id or "filter:&lt;item&gt;").</param>
    private sealed record Button(Rect2 Rect, string Label, string Hotkey, string Sub, bool Enabled, bool Active, Action OnClick, string? Key = null);

    private readonly List<(string Text, double Until)> _messages = new();
    private readonly List<Button> _buttons = new();
    private readonly List<Button> _tutorialButtons = new();
    private readonly List<Rect2> _panels = new();
    private World? _world;
    private PlayerInput? _input;
    private bool _showHelp = true;
    private Font _font = null!;
    private Rect2 _tutorialRect;

    /// <summary>Active tutorial, if any. Drawn as an instruction panel at the top of the screen.</summary>
    public Tutorial? Tutorial { get; set; }
    public event Action? TutorialNextRequested;
    public event Action? TutorialSkipRequested;

    private bool TutorialActive => Tutorial is { Finished: false };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = ThemeDB.FallbackFont;
    }

    public void Bind(World world, PlayerInput input)
    {
        _world = world;
        _input = input;
        _messages.Clear();
    }

    public void ToggleHelp() => _showHelp = !_showHelp;

    /// <summary>True if the mouse is over any HUD panel (so world clicks should be ignored).</summary>
    public bool IsMouseOverHud()
    {
        var m = GetLocalMousePosition();
        return _panels.Any(r => r.HasPoint(m));
    }

    /// <summary>Activates the command-card button under the mouse, if any.</summary>
    public void Click()
    {
        var m = GetLocalMousePosition();
        var b = _buttons.Concat(_tutorialButtons).FirstOrDefault(b => b.Rect.HasPoint(m));
        if (b is { Enabled: true }) b.OnClick();
    }

    public void Consume(IEnumerable<SimEvent> events)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var e in events)
        {
            string? text = e switch
            {
                MessageEvent m when m.PlayerId == Main.LocalPlayer && m.Text != "Tile is occupied" => m.Text,
                WaveStartedEvent w => $"Wave {w.Wave} incoming!",
                BuildingDestroyedEvent d => $"{_world!.Data.Buildings[d.DefId].Name} destroyed",
                _ => null,
            };
            if (text == null) continue;
            _messages.RemoveAll(m => m.Text == text);
            _messages.Add((text, now + MessageSeconds));
        }
        _messages.RemoveAll(m => m.Until < now);
        if (_messages.Count > 5) _messages.RemoveRange(0, _messages.Count - 5);
    }

    public override void _Process(double delta)
    {
        if (_world != null) Layout();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null || _input == null) return;
        var size = GetViewportRect().Size;

        DrawWavePanel();
        DrawStoragePanel(size);
        DrawInfoPanel(size);
        DrawCommandCard();
        DrawTooltip();
        DrawMessages(size);
        if (TutorialActive) DrawTutorial();
        else if (_showHelp) DrawHelp(size);
        DrawOutcome(size);
    }

    // ------------------------------------------------------------------ tutorial

    private const int TutorialBodySize = 15;

    private void LayoutTutorial()
    {
        _tutorialButtons.Clear();
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
                true, true, () => TutorialNextRequested?.Invoke()));
            right -= 140;
        }
        _tutorialButtons.Add(new Button(new Rect2(right - 130, by, 130, 30), "Skip tutorial", "", "",
            true, false, () => TutorialSkipRequested?.Invoke()));
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

    // ------------------------------------------------------------------ layout

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

    /// <summary>Rebuilds the command-card buttons and the list of mouse-blocking panels.</summary>
    private void Layout()
    {
        _buttons.Clear();
        _panels.Clear();
        _panels.Add(CardRect);
        _panels.Add(InfoRect);
        LayoutTutorial();

        var actions = _input!.SelectedBuilding is { } sel ? BuildingCommands(sel) : EngineerCommands();
        for (int i = 0; i < actions.Count && i < 9; i++)
        {
            if (actions[i] is not { } a) continue;
            var card = CardRect;
            var rect = new Rect2(
                card.Position.X + CellGap + (i % 3) * (CellW + CellGap),
                card.Position.Y + CellGap + (i / 3) * (CellH + CellGap),
                CellW, CellH);
            _buttons.Add(a with { Rect = rect });
        }
    }

    private List<Button?> EngineerCommands()
    {
        var input = _input!;
        var list = new List<Button?>();
        foreach (var def in _world!.Data.Buildings.Values.Where(d => d.Hotkey > 0).OrderBy(d => d.Hotkey))
        {
            string cost = string.Join(" ", def.Cost.Select(c => $"{c.Value} {ShortName(c.Key)}"));
            list.Add(new Button(default, def.Name, def.Hotkey.ToString(), cost,
                _world.Storage.HasAll(def.Cost), input.BuildTool == def, () => input.SelectBuildTool(def), def.Id));
        }
        while (list.Count < 7) list.Add(null);
        list.Add(new Button(default, "Stop", "X", "clear orders", _world.Engineers[Main.LocalPlayer].Orders.Count > 0, false, input.Stop));
        list.Add(new Button(default, "Deconstruct", "Del", "full refund", true, input.DeconstructTool, input.ToggleDeconstructTool));
        return list;
    }

    private List<Button?> BuildingCommands(Building b)
    {
        // Fixed slots: top rows = building-specific, bottom row = repair / deconstruct / back.
        var input = _input!;
        var slots = new Button?[9];
        bool rotatable = b.Size == 1 && b.Def.Kind is BuildingKind.Belt or BuildingKind.Inserter or BuildingKind.Drill;
        var rotate = new Button(default, "Rotate", "R", b.Dir.ToString(), true, false, input.RotateSelected);

        if (b is ConstructionSite site)
        {
            slots[0] = new Button(default, "Resume build", "RMB", $"{site.Fraction:P0} done", true, false, input.RepairSelected);
            if (rotatable) slots[1] = rotate;
        }
        else if (b is Inserter ins)
        {
            for (int i = 0; i < PlayerInput.FilterChoices.Length; i++)
            {
                string? item = PlayerInput.FilterChoices[i];
                string label = item == null ? "Any item" : _world!.Data.Items[item].Name;
                slots[i] = new Button(default, label, "F", "filter", true, ins.Filter == item, () => input.SetSelectedFilter(item), $"filter:{item}");
            }
            slots[5] = rotate;
        }
        else if (rotatable)
        {
            slots[0] = rotate;
        }

        if (b is not ConstructionSite)
            slots[6] = new Button(default, "Repair", "RMB", "iron plates", b.Hp < b.Def.MaxHp, false, input.RepairSelected);
        if (b is not Nexus)
            slots[7] = new Button(default, b is ConstructionSite ? "Cancel" : "Deconstruct", "Del", "full refund", true, false, input.DeconstructSelected);
        slots[8] = new Button(default, "Back", "Esc", "deselect", true, false, input.Deselect);
        return slots.ToList();
    }

    // ------------------------------------------------------------------ panels

    private void DrawCommandCard()
    {
        DrawPanel(CardRect);
        var mouse = GetLocalMousePosition();
        string? highlight = TutorialActive ? Tutorial!.Current.Button : null;
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Time.GetTicksMsec() / 180f);
        foreach (var b in _buttons)
        {
            bool hover = b.Rect.HasPoint(mouse);
            DrawRect(b.Rect, b.Active ? ButtonActive : hover && b.Enabled ? ButtonHover : ButtonBg);
            DrawRect(b.Rect, b.Active ? Accent : PanelEdge, false, b.Active ? 2f : 1f);
            if (highlight != null && b.Key == highlight && !b.Active)
                DrawRect(b.Rect.Grow(2 + 2 * pulse), HintGlow with { A = 0.5f + 0.5f * pulse }, false, 3f);
            var color = b.Enabled ? Text : Dim;
            DrawString(_font, b.Rect.Position + new Vector2(6, 20), b.Label, HorizontalAlignment.Left, CellW - 12, 14, color);
            DrawString(_font, b.Rect.Position + new Vector2(6, CellH - 10), b.Sub, HorizontalAlignment.Left, CellW - 34, 12, b.Enabled ? Dim : Warn);
            DrawString(_font, b.Rect.Position + new Vector2(CellW - 34, CellH - 10), b.Hotkey, HorizontalAlignment.Right, 28, 12, Accent);
        }
    }

    private void DrawInfoPanel(Vector2 size)
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
            hpFrac = b.Hp / b.Def.MaxHp;
            lines.Add(($"HP {Math.Ceiling(b.Hp)} / {b.Def.MaxHp}", Text));
            if (b.Status != "") lines.Add((b.Status, Text));
            switch (b)
            {
                case Crafter c:
                    lines.Add(($"Fuel {c.Coal}   In: {Describe(c.Input)}   Out: {Describe(c.Output)}", Dim));
                    break;
                case Drill { NeedsFuel: true } d:
                    lines.Add(($"Fuel: {d.Coal} coal", Dim));
                    break;
                case Nexus:
                    lines.Add(("Storage for everything you build. Defend it!", Dim));
                    break;
            }
        }
        else
        {
            var eng = w.Engineers[Main.LocalPlayer];
            title = "Engineer";
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
        }

        DrawString(_font, rect.Position + new Vector2(12, 24), title, HorizontalAlignment.Left, InfoW - 24, 18, Accent);
        var bar = new Rect2(rect.Position + new Vector2(12, 34), new Vector2(InfoW - 24, 8));
        DrawRect(bar, new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Math.Clamp(hpFrac, 0, 1), bar.Size.Y)), hpFrac > 0.35f ? Good : Warn);
        float y = rect.Position.Y + 64;
        foreach (var (text, color) in lines)
        {
            DrawString(_font, new Vector2(rect.Position.X + 12, y), text, HorizontalAlignment.Left, InfoW - 24, 15, color);
            y += 20;
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
        if (IsMouseOverHud()) return;
        var w = _world!;
        var t = _input!.HoverTile;
        if (!w.Map.InBounds(t)) return;

        string? text = null;
        if (w.BuildingAt(t) is { } b)
            text = b.Status == "" ? b.Def.Name : $"{b.Def.Name}: {b.Status}";
        else if (w.Map.OreAt(t) is { } ore)
            text = $"{w.Data.Items[ore].Name} ({w.Map.OreAmount(t)})" + (w.Map.IsClaimZone(t) ? " - claim zone" : "");
        if (text == null) return;

        var pos = GetLocalMousePosition() + new Vector2(18, 28);
        float width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 14).X + 16;
        DrawRect(new Rect2(pos, new Vector2(width, 24)), PanelColor);
        DrawString(_font, pos + new Vector2(8, 17), text, HorizontalAlignment.Left, -1, 14, Text);
    }

    private void DrawWavePanel()
    {
        var w = _world!;
        var waves = w.Waves;
        var lines = new List<(string, Color)>
        {
            ($"Wave {waves.WavesStarted} / {waves.TotalWaves}", Accent),
        };
        if (waves.Held)
        {
            lines.Add(("Waves on hold", Good));
            lines.Add(("(starts after the tutorial)", Dim));
        }
        else if (waves.HasMoreWaves)
        {
            int secs = (waves.TicksUntilNextWave + World.TicksPerSecond - 1) / World.TicksPerSecond;
            lines.Add(($"Next wave in {secs / 60}:{secs % 60:00}", secs <= 15 ? Warn : Text));
            if (!waves.IsSpawning) lines.Add(("[Space] start it now", Dim));
        }
        else
        {
            lines.Add(("Final wave - hold out!", Warn));
        }
        lines.Add(($"Enemies: {w.Enemies.Count}", Text));

        var rect = DrawLines(new Vector2(Pad, Pad), lines, 240);

        var bar = new Rect2(rect.Position + new Vector2(0, rect.Size.Y + 6), new Vector2(240, 18));
        float frac = Math.Max(0, w.Nexus.Hp / w.Nexus.Def.MaxHp);
        DrawRect(bar, PanelColor);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * frac, bar.Size.Y)), frac > 0.35f ? Good : Warn);
        DrawString(_font, bar.Position + new Vector2(6, 14), $"Nexus {Math.Ceiling(w.Nexus.Hp)} / {w.Nexus.Def.MaxHp}", HorizontalAlignment.Left, -1, 13, Colors.Black);
        _panels.Add(rect.Merge(bar));
    }

    private void DrawStoragePanel(Vector2 size)
    {
        var lines = new List<(string, Color)> { ("Nexus storage", Accent) };
        foreach (var (item, n) in _world!.Storage.Entries)
            lines.Add(($"{_world.Data.Items[item].Name,-14} {n,5}", Text));
        if (lines.Count == 1) lines.Add(("(empty)", Dim));
        _panels.Add(DrawLines(new Vector2(size.X - 220 - Pad, Pad), lines, 220));
    }

    private void DrawMessages(Vector2 size)
    {
        float y = size.Y * 0.28f;
        foreach (var (text, _) in _messages)
        {
            DrawString(_font, new Vector2(0, y), text, HorizontalAlignment.Center, size.X, 20, Text);
            y += 26;
        }
    }

    /// <summary>Compact help in the bottom-left corner, clear of the play area and the info panel.</summary>
    private void DrawHelp(Vector2 size)
    {
        const int lineH = 18;
        float width = Math.Min(430, InfoRect.Position.X - Pad * 2);
        var rect = new Rect2(Pad, size.Y - Pad - HelpLines.Length * lineH - 12, width, HelpLines.Length * lineH + 12);
        DrawRect(rect, PanelColor);
        float y = rect.Position.Y + 20;
        foreach (var line in HelpLines)
        {
            DrawString(_font, new Vector2(rect.Position.X + 10, y), line, HorizontalAlignment.Left, width - 16, 13, Dim);
            y += lineH;
        }
        _panels.Add(rect);
    }

    private void DrawOutcome(Vector2 size)
    {
        var outcome = _world!.Outcome;
        if (outcome == Outcome.Playing) return;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.55f));
        string title = outcome == Outcome.Won ? "THE FUNNEL HOLDS"
            : _world.Surrendered ? "YOU SURRENDERED" : "THE NEXUS HAS FALLEN";
        DrawString(_font, new Vector2(0, size.Y / 2 - 10), title, HorizontalAlignment.Center, size.X, 48, outcome == Outcome.Won ? Good : Warn);
        DrawString(_font, new Vector2(0, size.Y / 2 + 36), "Press Enter to play again, or Esc for the menu", HorizontalAlignment.Center, size.X, 20, Text);
    }

    // ------------------------------------------------------------------ helpers

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

    private string ShortName(string item) => _world!.Data.Items[item].Name.Split(' ')[0].ToLowerInvariant() switch
    {
        "ammo" => "ammo",
        var s => item.EndsWith("_ore") ? s + " ore" : s,
    };
}
