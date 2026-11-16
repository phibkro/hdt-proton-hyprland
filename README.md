# HDT with Steam Proton on NixOS and Hyprland

This project runs Hearthstone Deck Tracker with Hearthstone from an existing Battle.net Steam shortcut. It provides an HDT plugin, a launcher, and Hyprland rules. The HDT executable is unmodified.

The [tested configuration](docs/reference/compatibility.md#tested-environment) uses Proton Experimental and XWayland on Hyprland. In the desktop comparison, Stable 11.0-2 showed submenu slowdowns and temporary freezes that were less apparent after returning to Experimental. Experimental still has unresolved freezes and crashes. See the [Proton version comparison](docs/reference/compatibility.md#proton-version-comparison). Native Wayland remains untested.

Plugin 0.5.0 prevents Arena score particles from triggering the reproduced WPF rendering failure. Ratings, scores, and badge artwork remain enabled. The operator completed the current draft with the workaround. Existing Nix installations must [update their package before rebuilding the plugin](docs/guides/nix.md#update-an-existing-home-manager-installation).

Plugin 0.5.1 adds local UI-stall recordings and trace export. It does not establish a fix for the reported Battlegrounds freeze. See [freeze diagnostics](docs/guides/diagnostics.md).

The Linux host is now a native Rust executable. The C# plugin still runs inside HDT through Proton. See [Rust build and verification](docs/development/rust.md); the migrated launcher passed the recorded Hyprland desktop and Arena checks. Other desktops remain experimental.

## Repository layout

| Path | Responsibility |
|---|---|
| `src/host/` | Native Rust launcher, desktop control, independent journal and trace export |
| `src/plugin/` | C# compatibility plugin loaded inside HDT through Proton |
| `src/hyprland/` | Compositor-specific rules |
| `src/replay/`, `tests/` | Account-free WPF replay and grouped boundary tests |
| `scripts/` | Stable command entry points and developer check drivers |
| `nix/`, `tooling/` | Packaging, analyzer support projects and reviewed fuzz seeds |
| `benches/`, `fuzz/` | Performance baselines and coverage-guided targets |
| `docs/` | Guides, reference, development workflow and investigations |

Tool-discovered configuration files live directly at the root. They are ordinary
files, without symlinks or forwarding configuration. Private installations,
account state, captures and local worktrees remain ignored.
The separate `feat/avalonia-ui` branch contains the native client migration; it is
not part of the installed compatibility package.

## Documentation

See the [documentation index](docs/README.md) for the full structure.

| Task or question | Document |
|---|---|
| Review and merge changes with reproducible verification | [PR review workflow](docs/development/pr-review.md) |
| Install through Nix and register the desktop app | [Install the compatibility tools with Nix](docs/guides/nix.md) |
| Apply the plugin and start HDT with Steam | [Apply HDT compatibility fixes](docs/guides/installation.md) |
| Build the plugin or diagnose an overlay problem | [Check and diagnose HDT](docs/development/README.md) |
| Repeat synthetic Arena UI transitions without the live game | [Replay HDT UI transitions in isolation](docs/guides/ui-replay.md) |
| Preserve logs independently when HDT freezes | [Capture evidence when HDT freezes](docs/guides/freeze-capture.md) |
| Test tracking, draft updates, or another Proton version | [Test HDT with Hearthstone](docs/guides/testing.md) |
| Look up commands, settings, or source files | [Command and configuration reference](docs/reference/commands.md) |
| Check what passed and what remains untested | [Observed compatibility](docs/reference/compatibility.md) |
| Understand the rendering, input, and window fixes | [HDT compatibility design](docs/reference/architecture.md) |
| Distinguish window, draft-state, and rendering failures | [HDT windows, state, and failure observations](docs/reference/components.md) |
| Review the Arena particle reproduction for upstream | [Upstream report draft](docs/investigations/upstream-report.md) |

## Scope

The current configuration passed user checks for transparency, preview clearing, window movement, resizing, Alt-Tab, and overlay input. HDT also saved Arena match results, redraft picks, and ArenaSmith ratings. The [compatibility record](docs/reference/compatibility.md) states the limits of each observation, including historical UI stalls.

`hdt-setup` applies the compatibility plugin and settings to an existing HDT installation. It does not install HDT, .NET, or the desktop startup service. The Steam wrapper starts that existing service and preserves Steam's original command and Proton selection.

The repository contains no account data or third-party binaries. The MIT license covers its original source. HDT, Hearthstone, .NET, and their assets retain their respective terms.
