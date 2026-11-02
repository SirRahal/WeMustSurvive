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
| `--sandbox[=map]` | Sandbox game: all research, 5000 of everything, waves start on Space |
| `--showcase` | Sandbox with one of every building pre-built (art check) |
| `--autohost=funnel` / `--autojoin=127.0.0.1` | Co-op host that starts as soon as someone joins / auto-join |
| `--smoke=SECONDS` | Play by itself, print a `SMOKE ...` state summary, and quit (use with `--headless`) |

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
| `docs/` | Design coverage and Steam guide. |

## Controls

| Input | Action |
|-------|--------|
| WASD / arrows / screen edge / middle-drag | Pan camera |
| Wheel | Zoom |
| C | Jump to your engineer |
| Command card (bottom right) or 1–6 then 1–8 | Open a category (Logistics, Production, Power, Walls, Turrets, Special), then pick a blueprint |
| LMB with blueprint / drag | Place construction site(s); cost is paid now. Without Shift this replaces the engineer's plan (unstarted blueprints are refunded) |
| R | Rotate blueprint / selected building |
| RMB | Move engineer; on a damaged building = repair; on a site = resume |
| Shift + any order | Chain it after the current jobs (StarCraft-style; queued targets are numbered on the map). Also keeps the blueprint selected |
| LMB on a building | Select it; the command card shows its actions (rotate, filters, repair, deconstruct, fire super weapon) |
| T | Research panel |
| F | Cycle inserter filter: Any item, each item, Auto (picks from its neighbours) |
| E | Inserter input side: Auto (behind, then left, then right) / Back / Left / Right, for 90° moves |
| X | Stop (unstarted sites refunded) |
| Del | Deconstruct selected, or toggle the deconstruct tool |
| Space | Call the next wave early |
| Hold Alt, aim, release / Alt+click | Ping wheel / quick ping |
| Ctrl+drag / Ctrl+RMB | Draw on the map / erase your drawings |
| H | Toggle help |
| Esc | Cancel → close category → deselect → pause menu |

## Rules of thumb

- **Coal** fuels burner drills, furnaces and generators. They burn coal delivered to them first, then **draw it from the Nexus/depots automatically**, so just keep coal flowing home. Drills on coal fuel themselves.
- **Drills aim themselves** while you place them (furnace that smelts the ore > belt > storage); the ghost shows where the ore will go. Press R to override.
- Disconnected belts, inserters, tunnels and drills show an orange broken-link badge; the end of the chain gets a red marker. Hover for what's missing.
- Drills output into whatever they face. Belts feed straight into the Nexus, depots, splitters and tunnels.
- **Inserters** drop where their arrow points and take from behind or either side (90° moves). They carry any item the target can use; taking from the Nexus/depot onto a belt needs a filter (or press Auto filter).
- **Storage network** = Nexus + depots. Costs, research and repairs draw from all of it.
- **Power**: the Nexus supplies 200 kW nearby. Generators (coal) + poles extend it. Overloaded grids slow down; below 25% they shut off.
- **Research** needs at least one Laboratory; cost is paid when you start.
- **Ammo**: Munitions Works makes magazines (2 iron), rockets (silver + 2 iron) and heavy shells (gold + 2 silver). Flame turrets burn coal; lasers use power.
- **Claim zones** (gold outline) mine 1.6× faster with double yield.
- **Enemies**: Spitters shoot structures from range, Sappers blow up belts and power, Brutes soak damage, and Grunts chew belts they walk over.
- **Super weapons** charge on power; fire from the strip under the storage panel, or select the building.
