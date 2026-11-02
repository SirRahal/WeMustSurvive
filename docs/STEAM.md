# Shipping Belt & Barricade on Steam

What's already in place, and the remaining steps that need a Steamworks account. None of these can be done from
the codebase alone.

## Already done in the project

- **Windows export preset** (`game/export_presets.cfg`): exports to `export/windows/BeltAndBarricade.exe` and
  includes the `data/*.json` game data. Install export templates first: Godot editor → *Editor → Manage Export
  Templates → Download* (must match 4.7.2 .NET).
- **Co-op networking behind one class** (`game/src/Net/NetSession.cs`). Today it uses ENet (direct IP / LAN).
  The lockstep sync (`sim/Net/Lockstep.cs`) doesn't care about the transport, so moving to Steam is a matter of
  re-implementing `Host / Join / Send / poll` on Steam networking.
- **Player data** lives in Godot's user folder (`user://profile.json`, `user://settings.cfg`), which on Windows is
  `%APPDATA%\Godot\app_userdata\Belt & Barricade\`. That path is what Steam Cloud should sync.

## 1. Steamworks setup

1. Join Steamworks (steamworks.steampowered.com) and pay the app credit (Steam Direct fee).
2. Create the app. Note the **App ID**; you'll get a depot ID for Windows too.
3. Fill in the store page (capsules, screenshots, trailer, description, tags such as *Automation, Tower Defense,
   Co-op, Base Building, Sci-fi*). The store page must be live ("coming soon") for at least 2 weeks before release.

## 2. Steam API in the game (C#)

Recommended: **Steamworks.NET** (pure C# wrapper; works with Godot .NET).

1. Add the package to `game/BeltAndBarricade.csproj`: `<PackageReference Include="Steamworks.NET" Version="..." />`.
2. Copy `steam_api64.dll` next to the exported exe (and into the project root for editor runs).
3. For development only, put a `steam_appid.txt` containing your App ID next to the exe. Don't ship it.
4. Initialise in `App._Ready()` (`SteamAPI.Init()`), call `SteamAPI.RunCallbacks()` every frame, and
   `SteamAPI.Shutdown()` on exit.

### Co-op over Steam

Replace the ENet internals of `NetSession` with:

- **Lobbies**: `SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2)` on host; invites via the Steam
  overlay (`SteamFriends.ActivateGameOverlayInviteDialog`); handle `GameLobbyJoinRequested_t` on the guest.
- **Messages**: `SteamNetworkingMessages.SendMessageToUser` / `ReceiveMessagesOnChannel` (reliable flag) carrying the
  same JSON messages `NetSession` sends today (`hello`, `start`, `cmd`, `batch`, `hash`, `restart`, `bye`).
- Keep the Join-by-IP option as a fallback for LAN parties if you like.

The lobby screen (`game/src/UI/LobbyScreen.cs`) then shows the Steam friend who joined instead of an IP address.

### Suggested achievements

| Id | Name | Trigger |
|----|------|---------|
| `FIRST_FACTORY` | Plates Delivered | Finish the tutorial |
| `FUNNEL_CLEAR` | The Funnel Holds | Win The Funnel |
| `CROSSROADS_CLEAR` | Two Fronts | Win The Crossroads |
| `COOP_WIN` | Shared Ownership | Win any map in co-op |
| `ELECTRIFIED` | Lights On | Research Electricity |
| `NUKE` | Overkill | Launch a nuke |
| `NO_WALLS` | Open Door Policy | Win The Funnel without building walls |
| `TREE_FULL` | Master Engineer | Allocate every skill tree node |

Wire them where the matching events already exist: `ResearchCompletedEvent`, `StrikeIncomingEvent`, and
`GameSession.RecordResultOnce()`.

### Steam Cloud

Use **Auto-Cloud** in the Steamworks settings with root `WinAppDataRoaming` and path
`Godot/app_userdata/Belt & Barricade`, pattern `*.json;*.cfg`.

## 3. Building and uploading (SteamPipe)

1. Export from Godot (or headless: `Godot_v4.7.2-stable_mono_win64_console.exe --path game --headless --export-release "Windows Desktop" ../export/windows/BeltAndBarricade.exe`).
2. Download the Steamworks SDK and use `tools/ContentBuilder`. Example `app_build_<APPID>.vdf`:

```
"AppBuild"
{
  "AppID" "<APPID>"
  "Desc" "Belt & Barricade build"
  "ContentRoot" "..\content\"
  "BuildOutput" "..\output\"
  "Depots"
  {
    "<DEPOTID>"
    {
      "FileMapping" { "LocalPath" "*" "DepotPath" "." "recursive" "1" }
    }
  }
}
```

3. Copy `export/windows/*` into `content/`, then run `steamcmd +login <builder account> +run_app_build ..\scripts\app_build_<APPID>.vdf +quit`.
4. In Steamworks, set the build live on a beta branch, test it, then on `default`.

## 4. Before release checklist

- [ ] Real art and audio (the current visuals are clean placeholders; there's no sound yet)
- [ ] Steam networking transport + invites (above)
- [ ] Controller / Steam Deck support (currently mouse + keyboard only)
- [ ] Balance passes on both maps with real players (the numbers are first guesses; see `game/data/*.json`)
- [ ] Achievements, Steam Cloud, rich presence
- [ ] Crash reporting / logs location in the support FAQ
- [ ] Store page, trailer, press kit; review the Steam onboarding checklist
