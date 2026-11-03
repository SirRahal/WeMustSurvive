# Belt & Barricade

Post-apocalyptic factory defense for 1–2 players (Godot 4.7 + C#). The design doc is in `Init Instructions.txt`;
what's implemented from it is tracked in [`docs/DESIGN_STATUS.md`](docs/DESIGN_STATUS.md), and Steam shipping steps
are in [`docs/STEAM.md`](docs/STEAM.md).

Mine and smelt five resource tiers, belt everything around, power your base, research better tech, and turn it
all into walls, turrets and super weapons to hold the Nexus against timed alien waves.

## Running

1. Install **Godot 4.7.x .NET edition** and the .NET 10 SDK.
2. Open `game/project.godot` in Godot, press **Build**, then **Play** (F5).
3. Tests: `dotnet test sim.tests`

From the main menu: **Play** (The Funnel with optional tutorial, The Crossroads, Sandbox), **Co-op** (host / join
by IP), **Skill Tree**, **Settings**, **Controls**.

### Command-line switches (after `--`)

| Switch | Effect |
|--------|--------|
| `--autostart=funnel` | Jump straight into a solo game on a map (`funnel` / `crossroads`) |
| `--sandbox[=map]` | Sandbox game: all research, 5000 of everything, waves start from the call-wave button |
| `--showcase` | Sandbox with one of every building pre-built, plus a belt loop through the Nexus (art and animation check) |
| `--autohost=funnel` / `--autojoin=127.0.0.1` | Co-op host that starts as soon as someone joins / auto-join |
| `--smoke=SECONDS` | Play by itself, print a `SMOKE ...` state summary, and quit (use with `--headless`) |
| `--tutorial` | With `--autostart`, start with the tutorial |
| `--screenshot=PATH` | Save a PNG of the window after 3 s and quit (pair with Godot's `--resolution 1920x1080`) |
| `--zoom=N` | Camera zoom when the game starts (1 = normal, 2 = close; for screenshots) |
| `--hud=storage,controls,research,tool:ID,turn:N` | Open HUD panels, hold a blueprint, or turn the view N quarter turns when the game starts (for screenshots) |

Example two-instance co-op smoke test:

```
godot --path game --headless -- --autohost=funnel --smoke=45
godot --path game --headless -- --autojoin=127.0.0.1 --smoke=40   # prints desync=none when in sync
```

## Layout

| Path | What |
|------|------|
| `sim/` | Pure C# simulation (no Godot): world, buildings, power grid, research, enemies, waves, pathfinding, commands, co-op lockstep, skill tree, tutorial. Fixed 60 Hz, deterministic. |
| `sim.tests/` | xUnit tests (67): logistics, power, research, enemies, super weapons, modifiers, tutorial playthrough, balance checks, command serialization, host/client sync with latency and desync detection. |
| `game/src/` | Godot layer: `App` (screens), `GameSession` (one game), `View/` (isometric rendering, camera), `Input/` (RTS controls → commands), `UI/` (HUD, menus, skill tree, lobby), `Net/` (ENet transport). |
| `game/data/` | All content and balance as JSON: items, recipes, buildings, enemies, techs, maps, waves, tutorial, skill tree. |
| `game/art/props/` | World sprites from Prop Studio: atlas pages and `sprites.json` (refresh with `tools\sync-art.bat`); anything missing draws as coded shapes. |
| `docs/` | Design coverage and Steam guide. |

## Controls

| Input | Action |
|-------|--------|
| WASD / arrows / screen edge / middle-drag | Pan camera |
| Wheel | Zoom |
| Q / Z, or the arrows on the minimap | Turn the view a quarter turn left / right around the map (the minimap turns with it) |
| C, or double-click the engineer portrait (bottom left) | Camera centres on your engineer and follows him until you pan; a single click on the portrait selects him |
| M, or the map button (top left) | Show / hide the minimap (it also opens when a wave starts; while it is closed, red markers on the screen edge point at incoming waves) |
| Build panel (bottom) or 1–6 then 1–8 | Open a category tab (Logistics, Production, Power, Walls, Turrets, Special), then pick a blueprint. Upgrade tiers share one slot: click or press it again for the next unlocked version, or hover the slot and pick a tier in the pop-up |
| LMB with blueprint / drag | Place construction site(s); cost is paid now. The blueprint stays in hand until you right-click or press Esc. The first placement after picking it replaces the engineer's plan (unstarted blueprints are refunded) unless Shift is held; later ones chain behind it |
| R | Rotate blueprint / selected building |
| Belts: drag | A ghost L-shaped route follows the mouse (straight along the first direction you drag, then one turn; red tiles are blocked or unaffordable). Release to place it, R mid-drag flips the corner, right-click or Esc drops the route |
| RMB | Move engineer; on a damaged building = repair; on a site = resume |
| Shift + any order | Chain it after the current jobs (StarCraft-style; queued targets are numbered on the map) |
| LMB on a building | Select it; the build panel shows its details and actions (rotate, filters, repair, deconstruct, fire super weapon) |
| T | Research panel |
| F | Cycle inserter filter: Any item, each item, Auto (picks from its neighbours) |
| E | Inserter input side: Auto (behind, then left, then right) / Back / Left / Right, for 90° moves |
| X | Stop (unstarted sites refunded) |
| Del | Deconstruct selected, or toggle the deconstruct tool |
| Space | Pause / resume (the speed control in the top bar also sets 1×, 2×, 3×) |
| Call-wave button (top bar, next to the wave timer) | Call the next wave early |
| Hold Alt, aim, release / Alt+click | Ping wheel / quick ping |
| Ctrl+drag / Ctrl+RMB | Draw on the map / erase your drawings |
| H | Controls panel (every binding, read from the input map) |
| Esc | Cancel → close category → deselect → pause menu |

## Rules of thumb

- **Coal** fuels burner drills, furnaces and generators. They **only burn coal delivered to them**: point a coal drill at them, run a belt with an inserter, or put an inserter between the Nexus/a depot and the machine. Drills on coal fuel themselves.
- **Drills aim themselves** while you place them (furnace that smelts the ore > belt > storage); the ghost shows where the ore will go. Press R to override.
- When belts, inserters, tunnels or drills don't deliver anywhere, the end of the chain gets a red marker. Hover any part of the chain to see orange broken-link rings on every piece of it, a line to the end, and what's missing.
- Drills output into whatever they face. Belts feed straight into the Nexus, depots, splitters and tunnels.
- **Inserters** drop where their arrow points and take from behind or either side (90° moves). They carry any item the target can use; taking from the Nexus/depot onto a belt needs a filter (or press Auto filter).
- **Storage network** = Nexus + depots. Costs, research and repairs draw from all of it.
- **Power**: the Nexus supplies 200 kW nearby. Generators (coal) + poles extend it. Overloaded grids slow down; below 25% they shut off.
- **Research** needs at least one Laboratory; cost is paid when you start.
- **Ammo**: Munitions Works makes magazines (2 iron), rockets (silver + 2 iron) and heavy shells (gold + 2 silver). Flame turrets burn coal; lasers use power.
- **Claim zones** (gold outline) mine 1.6× faster with double yield.
- **Enemies**: Spitters shoot structures from range, Sappers blow up belts and power, Brutes soak damage, and Grunts chew belts they walk over.
- **Super weapons** charge on power; fire from the strip above the bottom-left of the toolbar, or select the building.
