# CLAUDE.md

Shared instructions for every Claude Code session on this repo. Two developers work on it, **both on every part of
the project**, each with their own Claude. This file is how both Claudes stay in sync, so keep it current: when you
correct Claude about something that should apply to both of you, add it here in the same change.

Personal, uncommitted notes go in `CLAUDE.local.md` (gitignored).

## The project

**Belt & Barricade** (repo: `SirRahal/WeMustSurvive`, public) is a post-apocalyptic factory-defense game for
1–2 players: Factorio-style logistics plus wave-based tower defense, defending a Nexus from alien waves.

- Design doc: `Init Instructions.txt` (the original design, treat it as the source of intent).
- What's implemented vs. the design: `docs/DESIGN_STATUS.md`. **Read this before starting a feature**, and update it
  in the same change when a feature lands or changes.
- Steam shipping notes: `docs/STEAM.md`.
- Player-facing overview, controls and command-line switches: `README.md`.

## Stack and layout

Godot **4.7.x .NET edition**, C# on **.NET 10**.

| Path | What |
|------|------|
| `sim/` | Pure C# simulation, **no Godot references**. Fixed 60 Hz, deterministic (co-op runs lockstep and compares state hashes). |
| `sim.tests/` | xUnit tests for the sim. They load the real `game/data/*.json`. |
| `game/src/` | Godot layer: `App` (root, switches screens), `GameSession` (one game), `View/`, `Input/`, `UI/`, `Net/`. |
| `game/data/` | All content and balance as JSON (items, recipes, buildings, enemies, techs, maps, waves, tutorial, skill tree). |
| `docs/` | Design coverage and Steam guide. |

## Commands

```
dotnet build game/BeltAndBarricade.sln      # compile the game + sim
dotnet test sim.tests                       # run the sim tests (keep these green)
godot --path game                           # run the game (or open game/project.godot and press F5)
godot --path game --headless -- --autostart=funnel --smoke=30   # headless smoke run, prints a SMOKE summary
```

Two-instance co-op desync check (should print `desync=none`):

```
godot --path game --headless -- --autohost=funnel --smoke=45
godot --path game --headless -- --autojoin=127.0.0.1 --smoke=40
```

## Code rules

- **Keep `sim/` deterministic and Godot-free.** No `System.Random` without a seeded instance from the world, no wall
  clock time, no iteration over unordered collections where order affects state, no Godot types. Game state changes
  only through `Commands` so both co-op peers apply them on the same tick.
- Gameplay numbers belong in `game/data/*.json`, not hard-coded in C#.
- UI is built in code from Godot controls using `UI/UiKit.cs` styles; don't add `.tscn` scenes for menus.
- New sim behaviour gets a test in `sim.tests/`. GIT PUSH runs `dotnet test sim.tests` before it touches `main`.
- Match the existing style: file-scoped namespaces, nullable enabled, `/// <summary>` on public types.

## How we work together (both of us, both Claudes)

We don't split the codebase by owner, so **GitHub Issues are how we avoid colliding.**

1. **Every piece of work is an issue.** File one before starting (the in-game **Report issue** button / F8 does this,
   or `gh issue create`). Bugs get the `bug` label, ideas get `enhancement`.
2. **Claim it before you start:** assign yourself on the issue. If someone else is assigned, ask them before touching it.
   Claude: before starting work, check open issues (`gh issue list`) for anything overlapping the same files or
   system, and warn the user if there is one.
3. **One branch per issue**, named `<name>/<issue#>-short-desc` (e.g. `sari/14-belt-jam`). Do your work there.
4. **"GIT PULL" syncs you.** When anyone says **GIT PULL**, Claude runs the `git-pull` skill
   (`.claude/skills/git-pull/SKILL.md`): set uncommitted work aside, merge the latest `origin/main` into the current
   branch, resolve conflicts (picking the best version), put the uncommitted work back, and check the build and tests.
   It never pushes. Do this before starting a session.
5. **"GIT PUSH" ships your work.** When anyone says **GIT PUSH**, Claude runs the `git-push` skill
   (`.claude/skills/git-push/SKILL.md`): commit everything, merge `origin/main` into the branch and resolve conflicts
   (picking the best version), build and run the tests, merge the branch into `main` with a merge commit, push `main`,
   then switch back to the branch. It stops without touching `main` if the build or tests fail. The feature branch
   itself is not pushed. **No code reviews for now**; pull requests are optional. Put `Closes #<issue>` in the commit
   message to close the issue.
6. **Conflict hotspots:** `game/project.godot`, `*.csproj`, `game/scenes/Main.tscn`, the big JSON files in
   `game/data/`, `UI/Hud.cs`, and `GameSession.cs`. Keep edits to these minimal and focused, and say in the issue
   when you'll be changing one of them.
7. If a change alters these rules, the architecture, or the commands above, update this file in the same change.

## Dev tools

- **Issue reporter** (`game/src/UI/IssueReporter.cs`): debug builds only. A "Report issue" button at the top of the
  screen, or **F8** anywhere. It files a GitHub issue with a title, type, assignee (Sari / Kyle, mapped to GitHub
  usernames in the `Assignees` table), description, the current screen/game
  state, and optionally a screenshot ("Add Screenshot" checkbox). Screenshots are committed to the
  `issue-screenshots` branch (never `main`) and embedded in the issue. The repo is public, so screenshots are public.
  Auth: uses `WMS_GITHUB_TOKEN` / `GITHUB_TOKEN` / `GH_TOKEN` if set, otherwise the GitHub login git already has
  (`git credential fill`), so no extra setup is needed if you can `git push`.
