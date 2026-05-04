# Tactical Camera Controller

A BepInEx plugin for **Nuclear Option** that turns the cockpit target camera (the small MFD camera) into a free-look surveillance / lasing pod — pan, tilt, and zoom around the world even without a target lock, and the camera auto-stabilises on whatever ground point you aim at.

## Features

- **Manual mode toggle** — press one key to take over the target camera. Forces it on even when no target is locked.
- **Free pan / tilt / zoom** with keyboard
- **Auto ground lock** — when you stop panning, the plugin raycasts where you're looking, snaps to that world point, and tracks it as the aircraft moves around it (works as JTAC / lasing-style designation)
- **Up to 40× zoom** (Ifrit-grade), with **zoom-aware sensitivity** — at high magnification, pan speed scales down for fine aiming
- **Floating-origin safe** — lock points stored as `GlobalPosition` so they don't drift when the world Datum rebases
- **Self-collision filter** — raycast skips your own aircraft body parts
- **MFD HUD overlay** — RNG / ALT / HDG / GRID / MODE / Mag automatically populated for the manual lock point (uses the game's existing TargetScreenUI fields)
- **Hit-point reticle** — visible `[X]` marker at the lock position on the MFD
- **IR ↔ Color toggle** — bind a key to flip the camera's thermal mode at will (the toggle persists in manual mode; the game can't override it)
- **Auto-revert** — when the game locks a real target, the plugin steps aside and lets vanilla auto-aim take over

## Install

1. Install [BepInEx 5 (x64)](https://github.com/BepInEx/BepInEx/releases) into your Nuclear Option folder
2. Drop `TargetCamControl.dll` into `BepInEx/plugins/`
3. Launch the game once so the plugin registers its actions in Rewired
4. Open **Settings → Controls → Debug** category and bind whichever keys you like to the `TargetCamControl::*` actions

No external dependencies. The plugin registers its Rewired actions itself.

## Default suggested binds

| Action | Suggested key |
|--------|---------------|
| `TargetCamControl::ToggleManual` | F |
| `TargetCamControl::PanLeft` / `PanRight` | Numpad 4 / 6 |
| `TargetCamControl::TiltUp` / `TiltDown` | Numpad 8 / 2 |
| `TargetCamControl::ZoomIn` / `ZoomOut` | Numpad + / - |
| `TargetCamControl::ResetView` | Numpad 5 |
| `TargetCamControl::ForceColor` | I (IR/Color toggle) |

The `*Axis` actions are placeholders for future analog joystick support and can be left unbound for now.

## How to use

1. Get airborne in any aircraft with a target camera (Ifrit, Compass, Brawler, etc.)
2. Press your **ToggleManual** key — the MFD lights up and shows the world ahead
3. Pan / tilt with your keys until you're aiming where you want
4. Stop pressing keys — the camera **snaps onto that ground point** and tracks it as you fly around
5. Pan again to slew to a new spot; press ToggleManual again to release control back to the game

## Config

`BepInEx/config/com.noms.targetcamcontrol.cfg` — most settings are self-documenting. Key tunables:

- `PanSpeedDegPerSec` / `TiltSpeedDegPerSec` — base rate (scaled by zoom)
- `ZoomSpeedDegPerSec` — FOV change rate
- `MinFOV` / `MaxFOV` — `0.25` to `80` degrees by default (= 0.125× to 40× zoom)
- `AutoLockMinDist` / `AutoLockMaxDist` — raycast distance limits (meters)

## Known limitations

- **HOTAS / hat-switch binding** is not supported yet. I want HOTAS support too and I'm actively working on it — Nuclear Option routes custom Rewired actions into the Debug category which only accepts keyboard input, so it needs a different approach. For now, JoyToKey / reWASD can map joystick → keyboard keys if you want HOTAS-style control in the meantime.
- A real target lock will auto-exit manual mode (the camera follows the in-game target instead). Re-press ToggleManual to take control again.

## Building from source

Requires the .NET Framework 4.7.2 SDK. The csproj points at the default Steam path for Nuclear Option's managed DLLs — edit if yours differs.

```
dotnet build -c Release
```

Outputs `bin/Release/net472/TargetCamControl.dll`.

## License

MIT.
