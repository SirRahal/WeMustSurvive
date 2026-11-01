# Belt & Barricade

Post-apocalyptic co-op factory defense (Godot 4 + C#). The design doc is in `Init Instructions.txt`.

**Current milestone:** solo vertical slice of Map 1 "The Funnel", waves 1–4. You mine coal and iron, belt
ore into furnaces, craft ammo, and defend the Nexus with walls and gun turrets.

## Layout

| Path | What |
|------|------|
| `sim/` | Pure C# simulation, with no Godot dependency. Fixed 60 Hz ticks, changed only via `ICommand`s. |
| `sim.tests/` | xUnit tests for the sim, including a same-seed replay hash test (guards future co-op netcode). |
| `game/` | Godot project: isometric rendering, input → commands, HUD. Open `game/project.godot`. |
| `game/data/` | All balance data (items, recipes, buildings, enemies, map, waves) as JSON. |

## Running

1. Install **Godot 4.7.x, .NET edition** (the csproj uses `Godot.NET.Sdk/4.7.2`) and the .NET 10 SDK.
2. Open `game/project.godot` in Godot and press **Build**, then **Play** (F5).
3. Tests: `dotnet test sim.tests`

When exporting, add `data/*.json` to the export preset's *Filters to export non-resource files*.

## Controls

RTS-style. The engineer is your builder: you give it orders, and it walks there and does the work.

| Input | Action |
|-------|--------|
| WASD / arrows / screen edge / middle-drag | Pan camera |
| Wheel | Zoom |
| C | Jump camera to the engineer |
| Command card (bottom right) or 1–7 | Pick a blueprint (belt, inserter, drill, furnace, ammo workshop, wall, gun turret) |
| LMB (with blueprint) / drag | Place construction site(s). Cost is paid now; they join the engineer's build queue and get built in order |
| R | Rotate blueprint / selected building |
| RMB | Move engineer (interrupts, then it resumes its build queue); on a damaged building = repair, on an unfinished site = resume building |
| RMB (with blueprint) / Esc | Cancel blueprint |
| Shift | With RMB: do it after the current jobs. With a blueprint: keep placing after the click |
| LMB on a building | Select it. The command card then shows its actions (rotate, filters, repair, deconstruct) |
| X | Stop. Clears orders; unstarted sites are refunded |
| Del | Deconstruct selected building, or toggle the deconstruct tool (LMB / drag) |
| F | Cycle filter on the selected or hovered inserter (required to pull from the Nexus) |
| Space | Start the next wave early |
| H | Toggle help |

## Rules of thumb

- Drills on coal fuel themselves. Every other drill, furnace and workshop needs coal delivered.
- Drills output straight into the building or belt they face. Belts can feed directly into the Nexus.
- Building costs are paid from Nexus storage. Turret ammo is crafted in workshops (2 plates → 1 magazine) and delivered by inserter.
- Enemies path around walls when a route exists and chew through the cheapest wall when it doesn't.
