# DRG Survivor checkpoint auto retry and dive snapshot

This repository preserves a personal Deep Rock Galactic: Survivor mod experiment and a captured Endless dive for later study.

## Captured dive

`snapshot/drg_current_run_slot2.dat` is the exact mid-run checkpoint after one duplicate artifact was replaced with Nitragenic Powder. The checkpoint has 14 stages cleared and holds a Strong Armed Driller with Impact Axe, Neurotoxin Grenade, Colette Wave Cooker, and Cryo Grenade. `snapshot/drg_save_slot2_1.dat` is the corresponding latest profile save for that slot. Both files are preserved byte for byte; see `SHA256SUMS.txt`.

To revisit the dive, back up your existing DRG Survivor saves first, then restore the two files to the game's save directory under `steam/<account>/`. The checkpoint may depend on the recorded game version, DLC, and profile progression. These files are an archive, not a portable guarantee that a future game version will resume the run.

## Mod

`mod/AutoRetry.dll` is version 0.1.5 of a BepInEx IL2CPP plugin. On death or a missed drop pod, it attempts to restart from the game's existing stage checkpoint. Victory and explicit mission abort retain the game's normal handling. Source is in `src/`.

The plugin checks the hashes of `GameAssembly.dll`, `UnityPlayer.dll`, and `global-metadata.dat` before installing hooks. It only supports the recorded game build (`23854500`) and will refuse a changed build. It needs a compatible BepInEx IL2CPP installation. The game and BepInEx binaries are not included here.

**Validation limit:** the plugin compiled and passed synthetic checks, but repeated real-game retries, stage transitions, victory, and reward persistence were not fully validated. One live retry worked; a later run encountered a native failure of uncertain cause. Treat this as an archived experimental mod, not a verified release.

## Build

With a matching game's BepInEx installation available, run:

```sh
dotnet build src/AutoRetry.csproj -p:LoaderRoot=/path/to/game -c Release
```

The project references BepInEx and generated game interop DLLs from that directory. `tests/` contains small synthetic regression programs for the retry policy helpers.

## Scope

This is an unofficial fan mod and save snapshot. No game executables, BepInEx runtime, personal logs, or local launcher configuration are included.
