# Development and diagnostics

## Source map

| Location | Responsibility |
|---|---|
| `src/runtime/` | Configuration, same-session launcher, plugin installation, HDT settings and sanitized status |
| `src/plugin/ProtonCompatibility.cs` | HDT public .NET Framework extension: rendering, geometry and control-region export |
| `src/hyprland/` | Lua rules, host control bridge and identity-checked X11 transient relationship |
| `src/diagnostics/` | Read-only Win32 geometry and XComposite overlay-surface probes |
| `scripts/` | Setup, checks, Steam wrapper and automatic-start entry points |
| `tests/` | Bounded Bun tests; never recurse into private prefix backups |
| `docs/specs/` | Observable feature contracts and their verification |
| `devenv.nix` + lockfiles | Toolchain and public developer commands |

This is a small mixed-language project: conventional source/test/script boundaries are sufficient. C# is required by the HDT plugin interface, C by the direct Xlib probes, Bash by host command composition. Existing Bun orchestration has a [registered temporary exception](engineering-exception.md); no web/infra scaffold applies. Keep game/prefix/process authority at the existing boundaries.

The checkout root anchors private `hdt.local.json`, `app`/`managed`, `backups`, `evidence`, `cache`, `reference` and `local` directories. They are ignored by Git. Active runtime helpers remain under `evidence/`; changing source layout does not relocate these or user data. The publication allowlist admits individual reviewed files only.

## Checks

```sh
devenv shell -- hdt-check
```

This selects tests explicitly, runs shellcheck, bundles TypeScript and compiles both native helpers with warnings as errors. Subprocess tests verify Steam argv, quoting and exit status, including an unavailable startup service. The service boundary in those tests is controlled; this does not claim a real Steam client launch.

Compile the actual plugin separately with Battle.net already running through Steam and a complete HDT/native .NET installation:

```sh
devenv shell -- hdt-launch --build-plugin
```

This uses the real prefix's .NET compiler and supplied HDT assemblies. It does not install the resulting DLL or restart the tracker. Apply it with `hdt-setup` when HDT can be closed normally. Do not terminate a shared wineserver to close HDT.

## Diagnostics and rollback

`devenv shell -- hdt-status` reports selected runtime, geometry and match-summary fields. It does not print the inherited environment, account names or card choices. REA is pinned by the `rea` command for optional investigation, not needed at runtime. Record direct runtime observations separately from build tests and operator assertions.

- `hdt-launch --window-probe` compiles/runs the read-only Win32 geometry/styles probe.
- `HDT_HOVER_DIAGNOSTICS=1 hdt-overlay-launch` enables private UI heartbeat, tooltip show/hide requests, preview visibility and mouse counters.
- `hdt-build-surface-probe`, then `bun src/diagnostics/surface-check.ts` samples the last preview area in the identified overlay's XComposite backing pixmap, without reading game/root pixels.
- `HDT_ALPHA_FLOOR=0 hdt-overlay-launch` omits the faint 1/255 background on the next launch.
- `HDT_CONTROL_INPUT=0 hdt-overlay-launch` disables selective control input on the next launch; blanket non-focusable behavior then prevents control clicks.
- `HDT_RENDERER=inherit hdt-overlay-launch` omits the DXVK shader workaround; the plugin still requests WPF software rendering. `wine-gdi` is a retained failed experiment.

Stop the existing HDT launcher before relaunching with different flags. To remove runtime rules, stop the launcher first, then `bun src/hyprland/overlay-rule.ts --disable`. This cleanup path was source-reviewed, not part of desktop acceptance. No host configuration file is changed by runtime rules.

To remove the plugin, close HDT normally and remove only `ProtonCompatibility.dll` from its Roaming Plugins directory. Restore the saved individual settings if desired, preserving unrelated changes. This does not undo .NET installation. Setup preserves the first pre-change plugin/settings copies under `backups/hdt-before-render/`; retain additional checkpoints before experimenting with newer versions.

Retired `HDT_ELEMENT_INPUT=1` XShape-only input is explicitly rejected. Current control routing uses a session nonce, exact title/class/PID checks, fresh geometry and expiring rectangles; no synthetic clicks or game memory writes. The private snapshot includes cached scene/menu/draft-panel and main input counters without additional HearthMirror polling or account/card identifiers.

## Prior art and provenance

Evaluated: [same-prefix HDT launcher](https://github.com/alexzfe/HDT-linux), [HDT/Linux instructions](https://github.com/borisbabic/hearthstone_hdt_linux), [Linux Proton install guide](https://github.com/Bakedpotato-99/Hearthstone-Deck-Tracker-linux-proton-install), [WPF/DXVK rendering issue](https://github.com/doitsujin/dxvk/issues/4883), and [Hyprland input-region discussion](https://github.com/hyprwm/Hyprland/discussions/11834). These supplied investigation leads, not copied implementation. The same-prefix technique was adapted to the actual Steam namespace because prefix equality alone did not share its server here. Existing systemd service supervision is reused for Steam startup rather than adding a second process monitor. [Steam's documented wrapper convention](https://help.steampowered.com/en/faqs/view/145A-FE54-F37B-278A) supports placing a host wrapper before `%command%`.

The alpha-floor and compositor gate are local workarounds supported by recorded desktop observations. The source tree inspected at upstream commit `2698623dc0e91491671a614ae176f9db68831aa7` was not proven identical to the installed build. The executable remains unmodified; compilation uses actual installed assemblies.
