# Cities: Skylines 2 — macOS / Wine Patcher

A compatibility patcher for **Cities: Skylines 2** running under CrossOver/Wine on macOS.

The patcher targets Wine-specific filesystem and platform behaviour that can otherwise cause startup crashes, asset loading failures, and broken Paradox Mods downloads.

Repository: [AleanniMods/cities2patcher](https://github.com/AleanniMods/cities2patcher)

---

## Supported Environment

- Cities: Skylines 2 v1.5.8f1+
- CrossOver 26+
- Apple Silicon Macs
- Steam Windows build running through CrossOver/Wine

---

## Features

### Core Compatibility Fixes

Patches known Wine-specific issues in the game assemblies that can cause:

- Startup crashes
- Asset loading failures
- Long-path file open failures
- Broken filesystem checks
- Stability issues during startup and gameplay

### Paradox Mods Support

Full mode patches Wine-specific issues in `PDX.SDK.dll` that can prevent Paradox Mods downloads, installs, and file operations from completing correctly.

### ExtraAssetsImporter Note

This patcher does not patch ExtraAssetsImporter or convert ExtraAssetsImporter assets.

If you use ExtraAssetsImporter under CrossOver/Wine, you may still need to delete the generated `ModsData/ExtraAssetsImporter` folder before starting the game. Some ExtraAssetsImporter database/cache files can trigger Wine filesystem errors on relaunch, and removing that generated folder lets the mod rebuild it cleanly.

### Automatic Detection

The patcher automatically:

- Finds Cities: Skylines 2 across CrossOver bottles
- Detects already-patched files
- Creates `.bak` backups before modification
- Applies only required fixes

---

## Installation

Open Terminal, paste the following command, then press Enter:

```bash
git clone https://github.com/AleanniMods/cities2patcher && python3 cities2patcher/patch.py
```

The patcher will:

1. Locate your Cities: Skylines 2 installation.
2. Ask whether to apply Lightweight or Full mode.
3. Install the .NET SDK through Homebrew if `dotnet` is missing.
4. Back up original DLLs.
5. Apply the selected patches.

> The current patcher runs a small C# patching tool, so `dotnet` is required for both modes. If Homebrew is installed, the launcher can install it automatically.

---

## Patch Modes

### Lightweight

Applies game-launch, asset-loading, and core filesystem compatibility fixes.

This mode patches:

- `Colossal.IO.dll`
- `Colossal.IO.AssetDatabase.dll`

### Full

Applies all Lightweight fixes plus Paradox Mods fixes.

This mode additionally patches:

- `PDX.SDK.dll`

Recommended if you use the in-game Paradox Mods browser or need Paradox Mods downloads to work reliably.

---

## After a Game Update

Run the patcher again:

```bash
python3 cities2patcher/patch.py
```

The patcher will detect updated assemblies, skip unchanged patches, and reapply fixes where needed.

---

## Manual Game Location

If automatic detection fails, pass the Managed directory directly:

```bash
python3 cities2patcher/patch.py "/path/to/Cities2_Data/Managed"
```

Typical CrossOver location:

```text
~/Library/Application Support/CrossOver/Bottles/<bottle-name>/drive_c/
  Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Managed
```

---

## Restoring Original DLLs

Backups are stored beside the patched DLLs with a `.bak` suffix.

To restore manually:

```bash
cd "<path-to>/Cities2_Data/Managed"
cp Colossal.IO.dll.bak Colossal.IO.dll
cp Colossal.IO.AssetDatabase.dll.bak Colossal.IO.AssetDatabase.dll
cp PDX.SDK.dll.bak PDX.SDK.dll
```

Only restore `PDX.SDK.dll` if Full mode was applied.

---

## Recommended CrossOver Settings

| Setting | Recommended Value | Notes |
|---|---|---|
| Graphics | D3DMetal | Required for practical DirectX 12 support. |
| Synchronization | MSync | Usually better than ESync for this game. |
| DLSS / MetalFX | Enabled | Enable DLSS in-game as well. |
| High Resolution Mode | Enabled | Avoids Retina pixel-doubling issues. |
| Windows Version | Windows 10 or 11 | Older Windows modes can break required runtime features. |
| AVX | Enabled | CrossOver 25+ can expose AVX through Rosetta. |

---

## Recommended In-Game Settings

| Setting | Recommended Value |
|---|---|
| Display Mode | Fullscreen Windowed |
| Resolution | 1080p-1440p |
| VSync | Disabled |
| Performance Preference | Frame Rate |
| Dynamic Resolution | DLSS Balanced or FSR Quality |
| Depth of Field | Disabled |
| Motion Blur | Disabled |

---

## Technical Documentation

For detailed root-cause analysis and IL-level implementation notes, see [docs/technical.md](docs/technical.md).

---

## Credits

This project builds on prior Cities: Skylines 2 CrossOver patching work, including:

- [alexqzd/cs2-crossover-patcher](https://github.com/alexqzd/cs2-crossover-patcher)
- [alien-agent/cs2-macos-patcher](https://github.com/alien-agent/cs2-macos-patcher)

Current repository: [AleanniMods/cities2patcher](https://github.com/AleanniMods/cities2patcher)
