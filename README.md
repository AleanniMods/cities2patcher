Here’s a cleaner README rewrite with support scope, safer claims, current modes, restore flow, and credits adjusted for `AleanniMods/cities2patcher`.

```md
# Cities: Skylines II macOS / Wine Patcher

A community compatibility patcher for running **Cities: Skylines II** through **CrossOver/Wine on macOS**.

This project patches selected managed assemblies to work around Wine-specific filesystem and platform behaviour that can prevent the game, Paradox Mods, or some code mods from loading correctly.

GitHub: [AleanniMods/cities2patcher](https://github.com/AleanniMods/cities2patcher)

---

## Support Status

Supported target environment:

- Cities: Skylines II `v1.5.10f1+`
- CrossOver `26+`
- macOS on Apple Silicon
- Steam version of Cities: Skylines II running inside CrossOver

This is an unofficial community patcher. It is not affiliated with, endorsed by, or supported by Colossal Order, Paradox Interactive, CodeWeavers, Steam, or Apple.

Game updates can change the assemblies this tool patches. If the game updates, run the patcher again. It will detect already-patched files, skip patches that are no longer needed, and create backups before modifying files.

---

## Features

### Core Compatibility Fixes

Applies compatibility patches for Wine-specific issues that can cause:

- Startup crashes
- Asset loading failures
- Mod loading failures
- Broken file and directory operations
- `IOException: Success` and related Wine filesystem errors

### Paradox Mods Support

Full patch mode applies additional fixes for Paradox Mods functionality, including download, install, lock, cancellation, and filesystem handling issues seen under Wine.

### Mod Compatibility Patches

Optional mod patching can patch supported community mod assemblies inside the Cities: Skylines II AppData folder.

Currently supported mod patch targets include:

- `ExtraAssetsImporter.dll`

These patches are only applied when using the mod patch option.

### Backups And Restore

Before modifying a DLL, the patcher creates a `.bak` backup beside the original file.

The restore option can restore backed-up game and mod DLLs without manually copying files.

---

## Installation

Clone the repository and run the patcher:

```bash
git clone https://github.com/AleanniMods/cities2patcher.git
cd cities2patcher
python3 patch.py
```

You can also pass the game Managed directory directly:

```bash
python3 patch.py "/path/to/Cities2_Data/Managed"
```

---

## Patch Modes

When launched, the patcher offers these modes:

### 1. Lightweight

Patches the core game assemblies needed for game launch and asset loading.

Targets:

- `Colossal.IO.dll`
- `Colossal.IO.AssetDatabase.dll`

Recommended if you do not use Paradox Mods.

### 2. Full Patch

Applies all Lightweight patches plus Paradox Mods compatibility fixes.

Targets:

- `Colossal.IO.dll`
- `Colossal.IO.AssetDatabase.dll`
- `PDX.SDK.dll`

Recommended for most players using Paradox Mods.

### 3. Mod Files

Patches supported mod assemblies inside the Cities: Skylines II AppData folder only.

This does not patch the base game DLLs.

You will be prompted for the Cities: Skylines II AppData folder, which is the folder containing:

- `Player.log`
- `Logs`
- `ModsData`
- `.cache`

Example:

```text
~/Library/Application Support/CrossOver/Bottles/<bottle>/drive_c/users/crossover/AppData/LocalLow/Colossal Order/Cities Skylines II
```

### 4. Restore

Restores backed-up game and mod DLLs from `.bak` files.

Use this before troubleshooting a clean state, after a failed patch, or before reporting an issue upstream.

---

## Direct Commands

Full patch with known paths:

```bash
dotnet run --project cs2patcher -- \
  "/path/to/Cities2_Data/Managed" \
  full \
  --apply
```

Mod files only:

```bash
dotnet run --project cs2patcher -- \
  "/path/to/Cities2_Data/Managed" \
  mods \
  --apply \
  --appdata "/path/to/Cities Skylines II AppData"
```

---

## Dependencies

The patcher uses:

- Python 3
- .NET SDK / runtime for the C# IL patcher
- Mono.Cecil for assembly rewriting

If `dotnet` is not found, the Python launcher can attempt to install the required .NET SDK through Homebrew.

---

## After A Game Update

Run the patcher again:

```bash
python3 patch.py
```

The patcher will:

- Detect updated assemblies
- Skip already-applied patches
- Reapply required fixes
- Create fresh backups where needed

---

## Recommended CrossOver Settings

| Setting | Recommended Value |
| --- | --- |
| Graphics | D3DMetal |
| Synchronization | MSync |
| Windows Version | Windows 10 or 11 |
| AVX | Enabled |
| High Resolution Mode | Enabled |

D3DMetal is currently the practical option for DirectX 12 games such as Cities: Skylines II under CrossOver.

---

## Recommended In-Game Settings

| Setting | Recommended Value |
| --- | --- |
| Display Mode | Fullscreen Windowed |
| Resolution | 1080p to 1440p |
| VSync | Disabled |
| Performance Preference | Frame Rate |
| Dynamic Resolution | DLSS Balanced or FSR Quality |
| Depth of Field | Disabled |
| Motion Blur | Disabled |

These settings are suggestions only. Performance depends heavily on Mac model, city size, mods, assets, and CrossOver version.

---

## Technical Documentation

Detailed patch notes, root-cause analysis, and IL-level implementation details are documented in:

```text
docs/technical.md
```

---

## Reporting Issues

When reporting a problem, include:

- Cities: Skylines II version
- CrossOver version
- macOS version
- Mac model
- Patch mode used
- Whether restore was tested
- `Player.log`
- Relevant files from the `Logs` folder

For mod patch issues, also include the affected mod name and the AppData path being used.

---

## Credits

This project builds on prior community work to make Cities: Skylines II playable under CrossOver/Wine on macOS.

Credits and thanks to:

- [alexqzd/cs2-crossover-patcher](https://github.com/alexqzd/cs2-crossover-patcher) for the original CrossOver patching work and compatibility research.
- [alien-agent/cs2-macos-patcher](https://github.com/alien-agent/cs2-macos-patcher) for the macOS patcher foundation and earlier implementation work.
- The Cities: Skylines II modding community for documenting issues, testing fixes, and sharing logs.
- CodeWeavers and Wine contributors for the compatibility layer that makes this possible.

Current repository and maintenance:

- [AleanniMods/cities2patcher](https://github.com/AleanniMods/cities2patcher)

---

## Disclaimer

This tool modifies local game and mod DLLs. Backups are created automatically, but you should still use it at your own risk.

If something breaks, use Restore mode or verify the game files through Steam.
```
