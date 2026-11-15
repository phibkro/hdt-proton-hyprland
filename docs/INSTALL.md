# Install and start

## Before setup

Use the **existing Battle.net Steam shortcut, prefix and Proton version**. The tested profile is **Proton Experimental / Wine 11.0**, HDT 1.60.1 and Hyprland 0.56.2 with XWayland. Native Wayland is untested. The launcher discovers the actual running wineserver and derives its Proton path; it does not hardcode Experimental or switch versions.

You need Nix/devenv, native Steam, Hyprland with its Lua API, and working native Microsoft .NET Framework 4.8 in the game's prefix. Wine Mono failed in this investigation. Installing .NET modifies the prefix: stop its processes and preserve a recoverable copy first. This project does not silently install .NET or alter the game prefix. See the evaluated [Linux Proton install guide](https://github.com/Bakedpotato-99/Hearthstone-Deck-Tracker-linux-proton-install) and [protonfixes work](https://github.com/Open-Wine-Components/umu-protonfixes/pull/557); neither establishes this project's Hyprland behavior.

Copy `hdt.local.example.json` to `hdt.local.json` and set the actual numeric Steam shortcut ID. This private file is the configuration root. Optional `steamRoot` selects another absolute Steam directory. `HS_APP_ID` and `STEAM_ROOT` override those values; `HDT_APP_DIR` selects the complete HDT application directory. The default app directory is `app/` under the checkout.

Use the authentic HDT installer to obtain a **complete installed Velopack layout** (`Update.exe`, `packages/`, `current/`). Point `app` at `managed/current`, or set `HDT_APP_DIR` to the installation's `current/` directory. Keep companion files and the installation parent. The launcher resolves physical paths so the updater can locate `Update.exe`. An extracted `lib/app` alone starts HDT but produced “Updating to latest version failed.”

The inspected installer for HDT 1.60.1 had SHA-256 `25a83214b2bde1a290ba7bb2f1979f23ad374280ce90f1fff2c1da1d053a2e95`. Its checked CLI supports `--silent --installto <Windows-path> --log <Windows-path>`; see [Velopack setup reference](https://docs.velopack.io/reference/cli/content/setup-windows). It must run in the same live Steam namespace/prefix. The investigation's full installation already exists; setup below preserves it. A portable full-install/.NET bootstrap is **not yet supplied**. No HDT binaries or accounts are distributed here.

## Apply the compatibility plugin

Start Battle.net through its existing Steam shortcut. HDT must have been opened once to create its Roaming settings. If necessary, initialize it with `HDT_DIRECT=1 devenv shell -- hdt-launch` after installing HDT, then leave it open or close it normally.

From the checkout:

```sh
devenv shell -- hdt-setup
```

This compiles against your actual HDT companion assemblies using the prefix's native .NET compiler, normally closes only the identified HDT window if open, backs up/replaces its compatibility plugin, enables its plugin entry, and backs up/sets the three overlay background/menu visibility settings. It fails if the tracker refuses to exit; close HDT yourself and retry. It never force-stops a process. Other plugins/settings remain intact. Private recovery files stay under `backups/`.

Start the patched profile:

```sh
devenv shell -- hdt-overlay-launch
```

No HDT executable patch is required: the compatibility changes are a plugin, process-local rendering flags, and compositor integration. Detailed observed coverage is in [COMPATIBILITY.md](COMPATIBILITY.md).

## Start alongside the Steam shortcut

The startup chain is:

```text
Steam Battle.net shortcut → existing desktop HDT watcher service
                        ↘ original Steam/Proton command → Battle.net → Hearthstone
                                                          ↓ window detected
                                                     patched HDT launcher
```

The tested machine already has `hdt-proton-autostart.service`. Its canonical Home Manager declaration is [homelab PR #92](https://github.com/phibkro/homelab/pull/92). It runs the same project watcher, starts HDT once per game process and reuses an existing tracker. Manually closing HDT does not trigger repeated relaunches in that game session. The root `autostart.sh` forwards to `scripts/autostart.sh` for existing service declarations.

To obtain the exact line for your checkout:

```sh
devenv shell -- hdt-steam-options
```

Paste its output into **Steam → existing Battle.net shortcut → Properties → Launch Options**. The generated line has this shape:

```sh
bash '/absolute/path/to/hdt-proton/scripts/steam-launch.sh' %command%
```

This belongs to **Steam's shortcut Launch Options**. Battle.net's per-game additional arguments are passed to Hearthstone and cannot launch this host integration. Keep the Steam shortcut target, app ID and compatibility selection unchanged. If you already use a wrapper, compose it around `%command%` rather than discarding it. No Steam configuration is edited by these tools.

The wrapper requests the existing systemd user service, then execs Steam's original command with its original arguments. If the service is unavailable it warns and still starts the game. It requires the separately configured desktop service; it is not a portable service installer. The service's session startup remains useful when Battle.net outlives its initial launcher process. Repeated service starts are idempotent.

Remove the wrapper from Launch Options to undo that integration. To disable desktop startup entirely, disable its declaration (Home Manager on the tested machine) and stop the service when you're finished playing. Do not stop it mid-match: its cgroup can own HDT. For a temporary stop outside play: `systemctl --user stop hdt-proton-autostart.service`.

Steam-client launch acceptance for the new wrapper is pending. Its actual subprocess argument/exit behavior and activation of the already-running desktop service are checked independently.

## Comparing stable Proton

As of this investigation, **only Experimental is installed locally**. Valve distinguishes [stable, Next and Experimental](https://github.com/ValveSoftware/Proton/wiki/Proton-Versions). Stable is a comparison candidate, not a demonstrated fix. Check Steam's available numbered releases at test time; do not infer the current stable version from a stale guide.

1. Finish play, close HDT and all Battle.net/Hearthstone processes, and stop the watcher. Verify the prefix has no live wineserver. Preserve a consistent copy of the **entire compatdata directory**, including metadata outside `pfx`, plus the shortcut's current compatibility selection and HDT data. The existing pre-.NET recovery copy predates later settings and is not a substitute for this new checkpoint.
2. Install the desired stable Proton through Steam. For maximum isolation, make a separate test Steam shortcut/prefix copy and configure this project with that new app ID. This uses extra disk and may require Battle.net authentication. A direct same-prefix test instead needs the stopped checkpoint above and deliberate rollback of the whole directory; avoid an unprotected prefix downgrade.
3. Launch through Steam yourself. The HDT launcher derives that session's Proton automatically. Verify native CLR loading, startup/game detection, fresh Power.log, overlay transparency, move/resize, game clicks, controls, hover clearing and Alt-Tab. Check an actual saved Arena match, live offer/pick updates and Premium ratings separately.
4. If anything regresses, stop the test processes before restoring the checkpoint and Experimental selection. Never copy registry files or switch wineservers underneath a running game.

No stable switch or prefix copy was performed for the repository reorganization.
