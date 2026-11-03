# Design document coverage

Status of each section of the design document (`Init Instructions.txt`), and the best-guess decisions made where
the document left things open. All balance numbers live in `game/data/*.json`.

| # | Section | Status | Notes / decisions |
|---|---------|--------|-------------------|
| 1 | High concept | ✅ | Solo and 2-player co-op; factory feeds every defense. Training map ~25 min, standard map ~50 min. |
| 2 | Core loop | ✅ | Mine → belts/inserters → smelt → research → build defenses → hold waves → expand to richer zones → super weapons. |
| 3 | Engineer avatars | ✅ | RTS order queue (move/build/repair), A* pathing, auto-firing sidearm, respawn at the Nexus. Orders show a path line and target outline in each player's colour, visible to the partner. |
| 4 | Resources & tiers | ✅ | Coal, iron, copper, silver, gold, each smelted in furnaces. **Claim zones**: 1.6× mining speed and **double yield**. Richer/deeper ores sit farther out. |
| 5 | Tiered buildings | ✅ | Belts ×3 tiers, tunnels (underground belts), splitters, inserters ×2, drills ×3, furnaces ×2, Munitions Works, labs, generators, poles, storage depots, walls ×4 (Basic/Reinforced/Heavy/Fortress), turrets ×5 (Gun/Laser/Rocket/Flame/Heavy), Nuclear Silo, Satellite Uplink. |
| 5 | "Build if materials exist anywhere in the shared network" | ✅ | The **storage network** = Nexus + every Storage Depot; costs are paid from the Nexus first, then depots. Depots make remote outposts possible without long belts. |
| 6 | Power grid | ✅ | Poles link within wire reach; buildings within supply radius join the grid. Satisfaction = supply/demand; consumers slow down proportionally, and below 25% they **brown out** entirely. Coal generators burn fuel in proportion to load; the Nexus supplies 200 kW. |
| 7 | Shared research | ✅ | 16 techs over 4 tiers; costs mix current and previous tiers; prerequisites; labs (more labs = faster). Research is shared and synced in co-op. |
| 8 | Waves | ✅ | Strict timer, prep window you can end early (call-wave button), next-wave preview. |
| 8 | Enemy roster | ✅ | Grunt, Runner, Brute (tank), Spitter (stops to spit acid at structures), Sapper (hunts belts/poles/generators/drills, explodes). Grunts and Brutes chew through belts and poles they walk over ("when convenient"). |
| 8 | Loss / Nexus | ✅ | Nexus = main storage + starting power + weak built-in gun. Loss when it dies. Surrender also counts as a loss. |
| 9 | Map 1 "The Funnel" | ✅ | V-shaped valley, single choke, coal/iron near the Nexus, copper halfway up the arms, silver claim zone behind the choke. 10 waves: 1–4 iron tier, 5–6 introduce Spitters/Brutes/Sappers (copper/power/lasers expected), 7–10 heavy silver-tier pressure. A test confirms a fortified choke can hold all 10 waves. |
| 9 | Teaching goals | ✅ | 16-step guided tutorial on first play (skippable, replayable) teaching mining, smelting, belts, inserters, filters, turrets, ammo and walls; the closing step points to research and copper. |
| 10 | Super weapons | ✅ | Nuclear Silo (3 min charge, radius 8, 3000 damage, 5 s warning) and Satellite Uplink (1 min, radius 3.5, 800 damage). Both charge on grid power; any player can fire them at any target. Gold-tier, so they're used on the standard map. |
| 11 | Meta progression | ✅ | Meta points for wins (Funnel 3, Crossroads 6, +2 first clear). 49-node skill tree in 6 clusters; each cluster's notables are only reachable through a node fed by the **neighbouring** cluster (the doc's interleaving). Small nodes +4–8%, notables +10–30%. Refundable, resettable, saved in `user://profile.json`. |
| 12 | Solo mode | ✅ | Health, damage and count each scaled by ∛0.45 → overall threat ≈45% of co-op (≈55% weaker). Everything else identical. |
| 13 | Co-op communication | ✅ | **Alt ping wheel** (Look here, Defend, On my way, Need iron/copper/silver, Build here, Danger), Alt+click quick ping, **Ctrl+drag freehand drawing** (fades after 30 s, Ctrl+right-click erases yours), partner's orders and targets highlighted. |
| — | Co-op networking | ✅ (LAN/IP) | Host-authoritative command scheduling with deterministic lockstep simulation and periodic desync hashing. Works over direct IP / LAN today; Steam transport is documented in `docs/STEAM.md`. The host's skill-tree bonuses apply to both players. |
| 14 | Visual style | 🟡 | Pre-rendered isometric sprites from Prop Studio for every building (states: construction, damaged, wrecked; joined walls), enemies, the engineer, items, effects, ground, ore deposits and decals, animated from game state where strips exist (belts, inserter poses, unit walks) with coded motion elsewhere. Building work loops, final textures and audio are still to do. |
| 15 | Pillars | ✅ | Factory is the weapon (ammo, rockets, shells, coal and power all come from the factory); meaningful expansion (claim zones, gold pockets near the lanes); clear tiers; co-op clarity; readable wave timer and funnel. |

## Extras not in the document

- Second map, **The Crossroads** (15 waves, two lanes, gold claim pockets), so gold-tier content and super weapons are reachable.
- **Sandbox** mode (all research, plenty of resources, waves on demand) for testing.
- Main menu with map select, settings (game speed, edge scrolling, fullscreen, help, minimap), controls reference.
- Minimap, research panel, storage network panel, super weapon strip, building tooltips.
- **Ore depletion feedback:** every drill shows a bar of the ore left under it (blinks red under 15%). When the tile
  runs dry the ore is removed from the map, the drill shows an "empty" icon, its status says Depleted, and both
  players get a message.

## Not done yet

- Steam integration itself (needs an App ID; see `docs/STEAM.md`)
- Final art, animation, audio and music
- Controller / Steam Deck support
- Save/load mid-game
- More maps and enemy types beyond the Map 1 roster
- A real balance pass with players
