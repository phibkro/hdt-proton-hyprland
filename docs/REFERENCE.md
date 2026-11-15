# Command and configuration reference

## Commands

`devenv.nix` defines these commands. Each command runs from a project devenv shell. The outer form is `devenv shell -- COMMAND`.

| Command | Behavior |
|---|---|
| `hdt-setup` | Builds the plugin, closes an identified HDT normally if open, installs the plugin, and changes the overlay settings |
| `hdt-overlay-launch` | Builds the X11 helper, applies runtime Hyprland rules, and starts HDT with the compatibility defaults |
| `hdt-launch` | Starts HDT in the running Steam server's namespace without the overlay defaults |
| `hdt-launch --build-plugin` | Compiles `src/plugin/ProtonCompatibility.cs` against the installed HDT assemblies and native .NET compiler |
| `hdt-launch --window-probe` | Compiles and runs the read-only Win32 window probe |
| `hdt-status` | Reports selected window, rendering, log, and saved-match fields |
| `hdt-steam-options` | Prints a quoted Steam Launch Options line for this checkout |
| `hdt-check` | Runs the selected tests, shellcheck, TypeScript bundles, and native helper builds |
| `hdt-build-surface-probe` | Builds the read-only XComposite probe |
| `rea` | Runs the pinned REA investigation tool. HDT does not require REA at runtime |

`hdt-setup` takes no arguments. A build failure prevents installation. An HDT close timeout prevents installation and configuration changes. Configuration failure stops setup after the preceding plugin installation step. Setup reports the failure and retains its backups.

## Installed package commands

The Nix package provides `hdt-proton`. Its subcommands reuse the same scripts as devenv.

| Subcommand | Behavior |
|---|---|
| `open` | Focuses the existing HDT window or starts its service |
| `restart` | Restarts the tracker service after checks that protect the game process |
| `watch` | Runs the existing automatic-start watcher |
| `setup` | Applies the compatibility plugin and settings |
| `status` | Reports the filtered runtime status |
| `overlay-launch` | Starts HDT with the compatibility defaults |
| `raw-launch` | Starts the namespace launcher with its supplied arguments |
| `steam-options` | Prints the installed Steam Launch Options line |
| `steam-run` | Starts the service and executes Steam's original arguments |
| `build-surface-probe` | Checks the prebuilt probe in a package, or compiles it in a checkout |

The desktop entry and Home Manager module use `nix/desktop-entry.desktop.in` as their shared definition. The desktop entry has the ID `io.github.phibkro.hdt-proton` and a `Restart tracker` action.

## Configuration

`src/runtime/config.ts` reads `hdt.local.json` from the mutable data directory. In a checkout, the default is the checkout root. The installed wrapper defaults to `${XDG_DATA_HOME:-$HOME/.local/share}/hdt-proton`. Git ignores the local file.

| Key or environment variable | Meaning and precedence |
|---|---|
| `steamAppId` | Required numeric Steam shortcut ID unless `HS_APP_ID` is set |
| `HS_APP_ID` | Overrides `steamAppId` |
| `steamRoot` | Optional absolute Steam directory |
| `STEAM_ROOT` | Overrides `steamRoot`. The default is `$HOME/.local/share/Steam` |
| `HDT_PROJECT_DIR` | Overrides the mutable directory for configuration, app files, backups, and evidence. It must be absolute and outside the Nix store |
| `HDT_NATIVE_DIR` | Selects the prebuilt native helper directory. The Nix wrapper sets this value |
| `HDT_APP_DIR` | Selects the complete HDT application directory. The default is `app/` under the checkout |

The configuration rejects unknown JSON keys and IDs outside the unsigned 32-bit range. The prefix path is `steamapps/compatdata/<steamAppId>/pfx` under the selected Steam directory.

Window matching uses both the exact title and the class `steam_app_<steamAppId>`. The launcher requires one live Proton wineserver for the configured prefix. It derives the Proton path from that server rather than selecting a Proton version.

## Overlay options

These environment variables affect HDT on its next launch. They do not change the running game.

| Variable | Compatibility default | Effect |
|---|---|---|
| `HDT_DIRECT` | `1` | Uses the running Proton installation's Wine executable |
| `HDT_RENDERER` | `dxvk-software` | Adds `d3d9.shaderModel = 1` to HDT's DXVK configuration. `inherit` omits that workaround. `wine-gdi` is a retained failed experiment |
| `HDT_ALPHA_FLOOR` | `1` | Gives the overlay a black background at 1/255 opacity |
| `HDT_CONTROL_INPUT` | `1` | Starts the bridge that allows input over HDT's marked controls |
| `HDT_HOVER_DIAGNOSTICS` | Unset | A value of `1` exports tooltip, preview, UI-update, and mouse-counter diagnostics |
| `HDT_DEBUG` | Unset | A value of `1` enables CLR and exception traces through `WINEDEBUG` |

The plugin requests WPF software rendering. Version 0.5.0 also prevents cosmetic Arena score particle emitters from initializing. Scores and badge artwork remain enabled. `HDT_ELEMENT_INPUT=1` is a rejected XShape experiment and causes a launch error.

## HDT installation

The tested installation uses HDT 1.60.1 with assembly version 1.60.1.9028. The application targets .NET Framework 4.7.2. The tested prefix has native Microsoft .NET Framework 4.8.

The complete Velopack layout contains `Update.exe`, `packages/`, and `current/`. `HDT_APP_DIR` identifies `current/`. The launcher resolves the executable's physical path so HDT can locate the updater beside that directory.

The inspected installer had this SHA-256:

```text
25a83214b2bde1a290ba7bb2f1979f23ad374280ce90f1fff2c1da1d053a2e95
```

That executable supports `--silent`, `--installto`, and `--log`. Installation used the same live Steam namespace and prefix. The options were checked against the executable and the [Velopack setup reference](https://docs.velopack.io/reference/cli/content/setup-windows).

The repository does not provide a complete HDT or .NET installer. The evaluated [Linux Proton guide](https://github.com/Bakedpotato-99/Hearthstone-Deck-Tracker-linux-proton-install) covers .NET installation. Its desktop results do not establish Hyprland compatibility. Related installation work appears in [umu-protonfixes PR #557](https://github.com/Open-Wine-Components/umu-protonfixes/pull/557).

## Settings and backups

The plugin installer uses HDT's Roaming `Plugins` directory. It enables the `ProtonCompatibility.dll` entry in `plugins.xml` and preserves other entries.

The overlay configuration command changes these settings to `false`:

- `HideInMenu`.
- `HideInBackground`.
- `HideMenuOverlayInBackground`.

The commands preserve their first backup copies under `backups/hdt-before-render/`. Those copies cover `plugins.xml`, `config.xml`, and a previous compatibility DLL if one exists.

The installer refuses settings writes while an accessible `HearthstoneDeck` process remains. That conservative rule includes HDT instances in other prefixes. The commands never force-terminate a process.

## Automatic startup

`scripts/steam-launch.sh` requests `hdt-proton-autostart.service`, then executes Steam's original arguments. A service failure produces a warning and does not block the game command.

`scripts/autostart.sh` checks the exact Hearthstone window every two seconds. A runtime lock prevents duplicate monitors. The watcher starts HDT once per game process or reuses an existing HDT window. A manual HDT exit does not trigger a relaunch during that same game session.

Root `autostart.sh` forwards to `scripts/autostart.sh` for existing service declarations. The tested machine's service is declared separately in [homelab PR #93](https://github.com/phibkro/homelab/pull/93).

## Source and private files

`git ls-files src scripts tests docs` lists the published implementation and documentation.

| Location | Responsibility |
|---|---|
| `src/runtime/` | Configuration, launch, plugin installation, HDT settings, and status |
| `src/plugin/` | The HDT C# compatibility plugin |
| `src/hyprland/` | Lua rules, the input bridge, and the X11 window helper |
| `src/diagnostics/` | Read-only Win32 and XComposite probes |
| `scripts/` | Setup, Steam integration, the watcher, and checks |
| `tests/` | Tests selected by `scripts/check.sh` |
| `nix/` | The package, Home Manager module, and shared desktop entry |
| `docs/specs/` | Feature contracts and their recorded verification |
| `devenv.nix`, `devenv.yaml`, and `devenv.lock` | Commands and toolchain dependencies |

The checkout root also contains private installation, configuration, and investigation files. Git excludes `app`, `managed`, `hdt.local.json`, `backups`, `evidence`, `cache`, `reference`, and `local`. Runtime helper paths remain under `evidence/`.
