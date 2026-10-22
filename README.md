# Berry Bury Berry VR

A SteamVR mod for **Berry Bury Berry** (Steam): play the whole game in virtual reality, with finger-tracked cartoon gloves, stick turning, laser or finger-touch menus and natural throwing.

## Installation

1. Download **`BerryVR-Setup.exe`** from the [latest release](https://github.com/heurazy/Berry-Burry-Berry-VR/releases/latest).
2. Close the game, run `BerryVR-Setup.exe` and choose **[1] Install**. The game is found automatically through Steam (otherwise, paste the game folder path).
3. Start **SteamVR**, put on your headset, then launch Berry Bury Berry from Steam.

Without SteamVR, the game starts normally on your monitor. To uninstall, run the installer again and choose **[2]**.

Tested with Meta Quest through Virtual Desktop. Index (Knuckles) and Vive bindings are included.

## Controls

| Right hand | |
|---|---|
| Grip | Pick up / put down / vacuum |
| Trigger | Throw / use / shoot / click UI |
| A | Jump |
| B | Tap: drop, hold: Gaping Maw |
| Stick | Turn (smooth or snap) |
| Stick click | Gaping Maw (rotate in build mode) |

| Left hand | |
|---|---|
| Stick | Move |
| Stick click | Berry Blitz |
| Grip | Sprint |
| Trigger | Air Blast |
| X | Tap: shop, hold: Berry Blitz |
| Y | Tap: pause, hold: move the hole |
| Menu | Tap: pause, hold: recenter |

In-game screens (the PC, ...) can also be pressed directly with your index finger.

## Settings

`BepInEx\config\berryvr.plugin.cfg` (created on first launch, edit it while the game is closed):

- **Comfort**: `TurnMode` (Smooth/Snap), turn speed and angle, `Vignette` (off by default), `HeightOffset` (for seated play)
- **Feel**: `Haptics`, `FingerTouchUI`
- **Visuals**: `CartoonGloves`, `GloveScale`, pop gun position (`GunOffsetRight/Up/Forward`, in cm)

## Building

Requirements: .NET SDK, Visual Studio Build Tools (C++), and the game installed.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

This produces `dist\BerryVR-Setup.exe`. The game folder is detected through Steam, or pass `-GameDir <folder>`.

## Layout

- `BerryVR/`: the BepInEx plugin (C#), the SteamVR actions and bindings, and `native/`, the OpenVR render bridge (C++).
- `installer/`: the one-file installer (compiled with the C# compiler that ships with Windows).
- `third_party/`: BepInEx 5.4.23.2 (MIT) and `openvr_api.dll` (BSD-3), with their licenses.

No game files are included: the game's assemblies are only referenced at build time.

## License

Apache 2.0, see [LICENSE](LICENSE). Berry Bury Berry belongs to its creators; this is an unofficial fan mod.
