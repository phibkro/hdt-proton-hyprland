# HDT with Steam Proton on NixOS / Hyprland

A local compatibility plugin and launcher for **HSReplay's Hearthstone Deck Tracker (HDT)** alongside Hearthstone launched through a Battle.net Steam shortcut. Tested with HDT 1.60.1, Proton Experimental / Wine 11.0, and Hyprland 0.56.2 using **XWayland**. This is not a general native-Wayland port or an official HearthSim project.

The tested profile provides a shared Steam Proton session, transparent overlay rendering, clearing hover previews, geometry updates, foreground stacking and selective control input. **Arena match recording failed an earlier test; live draft offers/picks and ArenaSmith ratings remain untested.** See [observed compatibility and limitations](docs/COMPATIBILITY.md).

## What the fixes do

| Problem | Mechanism |
|---|---|
| Separate Wine server despite the same prefix | Join the running Steam wineserver's user/mount namespace, derive its Proton and inherit its environment in memory |
| Black WPF overlay | Request HDT-only software rendering and `d3d9.shaderModel = 1` |
| Preview pixels remain after WPF hides the preview | Give only HDT's overlay a black background at **1/255 opacity**; faint tint is a deliberate workaround |
| Overlay does not follow the game | Plugin calls HDT's own `UpdatePosition` when geometry differs |
| Overlay drops behind the game | Exact-title Hyprland rules and overlay-only stacking; lower it when another app is active |
| Blank space blocks Hearthstone, or controls cannot receive clicks | Export HDT's marked clickable rectangles and allow compositor input only while the pointer is over a control |

Slight flicker was accepted in testing; zero flicker is not established. The tested button was the close button on the blue Mulligan Guide announcement. Do not infer every control works.

Later in the same desktop session, HDT stayed alive while its UI heartbeat/control exports stopped updating. The control bridge expired stale data as designed. This remains an unresolved stability limitation; the repository publishes the observed compatibility workarounds, not a fully stable Linux port.

## Prerequisites

- Nix with devenv, a running Hyprland **Lua-config** compositor, and native Steam. The pinned `devenv.lock` supplies the project toolchain.
- Battle.net/Hearthstone already installed through a Steam shortcut. Keep its existing prefix and Proton version.
- Genuine Microsoft .NET Framework 4.8 working in that prefix. Wine Mono was insufficient in the observed setup. Verify loaded CLR rather than trusting a registry marker alone.
- A complete HDT application directory, including `HearthstoneDeckTracker.exe` and companion files. No Microsoft, Blizzard, HDT binaries or account data are distributed here.

The [Linux Proton guide](https://github.com/Bakedpotato-99/Hearthstone-Deck-Tracker-linux-proton-install) describes .NET installation, but tested Cinnamon X11 and leaves Wayland untested. Installing .NET changes a prefix: stop all processes in it and make a recoverable backup first. Our local install used the matching Steam runtime, Proton's `PROTON_DLL_COPY=*`, Winetricks `dotnet48`, then restored Windows 10 mode. That is installation history, not an unattended installer supplied by this repository. [Related protonfixes prior art](https://github.com/Open-Wine-Components/umu-protonfixes/pull/557).

## Configure and build

Copy `hdt.local.example.json` to **`hdt.local.json`**, then replace its example Steam shortcut ID with your actual ID. The local file is ignored by Git. For a different Steam root, add `"steamRoot": "/absolute/path/to/Steam"`. The corresponding prefix is `steamapps/compatdata/<ID>/pfx`.

Environment alternatives: `HS_APP_ID` overrides the ID, `STEAM_ROOT` overrides Steam's root, and `HDT_APP_DIR` selects a complete HDT directory outside this checkout. Otherwise put the application files under `app/`. Default Steam root is `$HOME/.local/share/Steam`. Exact title **and** `steam_app_<ID>` matches prevent rules from targeting every window in the shared prefix.

Start the existing Battle.net shortcut through Steam. In this checkout:

```sh
devenv shell
hdt-launch --build-plugin
HDT_DIRECT=1 hdt-launch
```

After HDT has initialized once, use a second project shell:

```sh
bun install-plugin.ts
bun configure-overlay.ts
hdt-overlay-launch
```

The installer normally closes only the identified HDT main window, waits for exit, copies the built plugin to HDT's authoritative Roaming Plugins directory, and enables its one settings entry. It preserves other plugin entries. The configuration command requires HDT stopped, backs up `config.xml`, and sets only `HideInMenu`, `HideInBackground` and `HideMenuOverlayInBackground` false. It must run between HDT shutdown and relaunch as shown. Private backups remain under `backups/`.

For later launches, start Battle.net through Steam first, then simply run:

```sh
devenv shell -- hdt-overlay-launch
```

This applies exact-title compositor rules and launches HDT in the running session. It never intentionally starts another Wine server or restarts Hearthstone. If no unique matching live server exists, it fails rather than guessing. A deleted fsync shared-memory file requires fully restarting the Steam game session yourself; do not delete shared-memory files underneath a live server.

## Tested installer extraction

Prefer a **full installed layout** when using HDT's updater. The extraction below starts HDT but omits Velopack's installation machinery: it produced `Velopack.Exceptions.NotInstalledException` and the “Updating to latest version failed” banner. In the tested session, running the verified installer in the same live Proton namespace with `--silent --installto <project-owned Windows path>` created `Update.exe`, `packages/` and `current/`; its next update check successfully reported “No new update available.” A future version download/application has not been tested. The compatibility plugin remains in HDT's Roaming data directory. Keep the entire installation, and select its `current/` directory with `HDT_APP_DIR`, or link `app` to it; the launcher resolves the executable's physical path before invoking Wine. Close only HDT and back up its data before changing layouts.

These installer options were checked against the actual verified executable's `--help` and the [Velopack setup reference](https://docs.velopack.io/reference/cli/content/setup-windows). This repository does not yet provide an automated full-install command. Do not run a different Wine/Proton session against an already-running prefix.

For the **specific tested** HDT 1.60.1 installer, the SHA-256 was `25a83214b2bde1a290ba7bb2f1979f23ad374280ce90f1fff2c1da1d053a2e95`, with an embedded ZIP at `0x770400`. Do not apply that offset to any different installer. The following is useful for inspection or a deliberately extracted application, with the updater limitation above:

From a project shell, with `HDT_INSTALLER` set to its absolute path and `app/` not already present:

```sh
test "$(sha256sum "$HDT_INSTALLER" | cut -d ' ' -f1)" = 25a83214b2bde1a290ba7bb2f1979f23ad374280ce90f1fff2c1da1d053a2e95 || exit 1
mkdir -p cache/installer
tail -c +$((0x770400 + 1)) "$HDT_INSTALLER" > cache/installer/payload.zip
unzip -t cache/installer/payload.zip
unzip -q cache/installer/payload.zip 'lib/app/*' -d cache/installer/extracted
mkdir app
cp -a cache/installer/extracted/lib/app/. app/
```

All 151 archive entries passed CRC checking in the original investigation. The full `lib/app` tree contained 147 regular files. The extracted tracker assembly was 1.60.1.9028 targeting .NET Framework 4.7.2. The plugin is compiled against the actual supplied executable, not against an assumed matching upstream checkout.

## Diagnostics and rollback

`hdt-status` reports only selected runtime, geometry and match-summary fields; it does not print the inherited environment or credentials. Its optional read-only REA process capture can retain these observations; `rea` pins rea-agents 6.8.0. REA is an investigation tool, not a runtime dependency.

- `hdt-launch --window-probe`: compile/run a read-only Win32 geometry/styles helper.
- `HDT_HOVER_DIAGNOSTICS=1 hdt-overlay-launch`: enable UI heartbeat, tooltip show/hide requests, visible-preview counts and overlay mouse-event counters in `evidence/hover-state.json`. No card names/account contents.
- `hdt-build-surface-probe`, then `bun surface-check.ts`: sample the last preview rectangle in the exact overlay's XComposite backing pixmap. It records alpha counts/hash, not screenshots or game/root pixels.
- `HDT_ALPHA_FLOOR=0 hdt-overlay-launch`: omit the 1/255 background on the next launch.
- `HDT_CONTROL_INPUT=0 hdt-overlay-launch`: disable the control bridge on the next launch; the blanket non-focusable rule prevents control clicks.
- `HDT_RENDERER=inherit hdt-overlay-launch`: omit the DXVK shader workaround; the plugin still requests WPF software rendering. `wine-gdi` is a retained failed rendering experiment, not a recommended fix.

Stop the existing HDT launcher before relaunching with different flags. To remove compositor rules/timers/subscription, stop the launcher first, then `bun overlay-rule.ts --disable`. This cleanup path is source-reviewed but was not executed during the accepted desktop test. No host configuration file is edited; rules are runtime-only.

To remove the plugin, close HDT and remove only `ProtonCompatibility.dll` from its Roaming Plugins directory. HDT regenerates its local plugin cache on startup. Restore the saved individual HDT settings if desired; preserve unrelated settings. This does not undo a prior .NET installation.

The retired `HDT_ELEMENT_INPUT=1` XShape-only experiment is rejected by the launcher. It did not establish working control routing. The current bridge has a session nonce, exact window identity/geometry/freshness checks and a roughly one-second expiry if updates stop. It emits no synthetic clicks and does not modify game memory.

## Development and provenance

```sh
HS_APP_ID=1234567890 bun test ./config.test.ts
bun build launch.ts control-input.ts status.ts overlay-rule.ts surface-check.ts install-plugin.ts configure-overlay.ts --target=bun --outdir=evidence/check-build
hdt-build-surface-probe
```

The C# plugin requires the real HDT companion assemblies and the native .NET compiler in its prefix. The clean-checkout checks and desktop acceptance have different coverage; see [compatibility](docs/COMPATIBILITY.md). Existing Bun orchestration is preserved under a [temporary engineering exception](docs/engineering-exception.md).

Prior art evaluated: [same-prefix HDT launcher](https://github.com/alexzfe/HDT-linux), [HDT/Linux instructions](https://github.com/borisbabic/hearthstone_hdt_linux), [WPF/DXVK rendering workaround](https://github.com/doitsujin/dxvk/issues/4883), and [Hyprland top-level input-region discussion](https://github.com/hyprwm/Hyprland/discussions/11834). The alpha-floor and compositor gate are local experiments supported by the observations documented here. No third-party implementation source or assemblies are included. The original plugin uses HDT's supported public extension interface; the HDT executable was not patched.

MIT covers this repository's original source only. HDT, Hearthstone, Microsoft .NET and their assets remain subject to their respective terms. `.gitignore` uses an explicit publication allowlist so local configuration, app files, evidence, prefix backups and investigation notes stay local.
